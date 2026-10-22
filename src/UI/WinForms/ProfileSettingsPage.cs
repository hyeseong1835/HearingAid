using System.Text.Json;
using System.Text.Json.Nodes;
using HearingAid.Core;
using HearingAid.Core.Audio;
using HearingAid.Core.Stt;

namespace HearingAid;

/// <summary>설정 창의 '프로필' 페이지. 프로필(profiles/&lt;이름&gt;.json) 목록을 관리하고, 선택한 프로필을 적용해
/// Config/IAudioSource/TranscriberEngine을 통째로 새로 만든다. 실제 교체는 MainForm이 수행한다
/// (이 페이지는 NewEngine에 결과만 담아둔다).</summary>
public class ProfileSettingsPage : UserControl
{
    readonly ITranscriber engine;
    readonly string baseConfigPath;
    readonly string profilesDir;

    public ITranscriber? NewEngine { get; private set; }

    // 위쪽 프로필 선택 줄 (VSCode 설정의 User/Workspace 선택처럼, 고른 프로필의 설정을 아래에서 편집한다)
    readonly ComboBox list = new();
    readonly Button addBtn = new();
    readonly Button removeBtn = new();
    readonly Label activeBadge = new();

    // 왼쪽 섹션 목록 (VSCode 설정의 목차처럼, 고른 섹션 하나만 오른쪽에 보인다)
    enum Section { AudioSources, Models, Advanced }
    static readonly (Section Id, string Title, string Description)[] Sections =
    [
        (Section.AudioSources, "오디오 소스", "무엇을 듣고 받아 적을지 정합니다. 여러 소스를 동시에 쓸 수 있습니다."),
        (Section.Models, "모델", "이 프로필에서 쓸 음성 인식 모델과 화자별 모델을 정합니다."),
        (Section.Advanced, "고급", "위에서 다루지 않는 설정(language, threads 등)을 JSON으로 재정의합니다."),
    ];
    readonly ListBox sectionList = new();
    readonly Panel sectionBar = new();
    Section currentSection = Section.AudioSources;
    readonly Label sectionTitle = new();
    readonly Label sectionDesc = new();

    // 우측 상세 패널
    readonly Panel detailScroll = new();
    readonly FlowLayoutPanel contentPanel = new();
    readonly Label detailTitle = new();
    readonly Label baseNoteLabel = new();
    readonly Panel modelHeaderPanel = new();
    readonly CheckBox overrideModelCheck = new();
    readonly ComboBox modelCombo = new();
    readonly Label speakerModelLabel = new();
    readonly DataGridView speakerGrid = new();
    readonly DataGridViewComboBoxColumn speakerModelColumn = new();
    readonly Panel speakerButtonsPanel = new();
    readonly FlowLayoutPanel sourceTabs = new();
    readonly Label aliasWarning = new();
    readonly FlowLayoutPanel sourcesPanel = new();
    AudioSourceEditor? selectedEditor;
    readonly Label otherJsonLabel = new();
    readonly TextBox otherJsonBox = new();
    readonly Label jsonErrorLabel = new();

    // 하단 바
    readonly Label hint = new();
    readonly Button saveBtn = new();
    readonly Button applyBtn = new();

    ProfileItem? selected;
    JsonObject currentPatch = new();
    bool suppressEvents;
    bool suppressSelection;
    /// <summary>불러온 뒤(또는 저장한 뒤) 바꾼 것이 있는지. 닫거나 다른 프로필로 옮길 때 저장할지 묻는 데 쓴다.</summary>
    bool dirty;
    readonly List<AudioSourceEditor> editors = new();

    /// <summary>전용 UI로 편집하므로 "기타 설정 재정의 (JSON)"에서는 빼는 키.</summary>
    static readonly string[] DedicatedKeys = ["audioSources", "audioSource", "model", "speakerModels"];

    sealed class ProfileItem
    {
        public readonly string? Name;
        public readonly bool Active;
        /// <summary>저장된 오디오 소스가 있는지. 없으면 프로필 목록에 경고 표시를 한다. 저장할 때 갱신된다.</summary>
        public bool HasAudioSource;
        public ProfileItem(string? name, bool active, bool hasAudioSource = true)
        {
            Name = name; Active = active; HasAudioSource = hasAudioSource;
        }
        public override string ToString() => Name ?? "";
    }

    /// <summary>처음 보일 때 고를 프로필(없으면 사용 중인 프로필)과, 보이자마자 새 프로필 만들기를 띄울지.</summary>
    readonly string? initialProfile;
    readonly bool startWithNewProfile;

    public ProfileSettingsPage(ITranscriber engine, string baseConfigPath, string profilesDir,
        string? initialProfile = null, bool startWithNewProfile = false)
    {
        this.engine = engine;
        this.baseConfigPath = baseConfigPath;
        this.profilesDir = profilesDir;
        this.initialProfile = initialProfile;
        this.startWithNewProfile = startWithNewProfile;
        BuildUi();
        Theme.StyleButton(saveBtn, Theme.ButtonKind.Primary);
    }

    /// <summary>목록은 페이지가 처음 보일 때 읽는다. 그때는 설정 창 안에서 크기가 정해져 있어
    /// 오디오 소스 편집기 폭을 제대로 잡을 수 있다(생성자 시점에는 기본 크기라 좁게 잡힌다).</summary>
    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        // 설정 창의 배치가 끝난 다음 메시지에서 읽는다(OnLoad 시점에는 아직 크기가 확정되지 않았을 수 있다).
        BeginInvoke(() =>
        {
            if (initialProfile is not null) selected = new ProfileItem(initialProfile, false);
            RefreshList();
            if (startWithNewProfile) AddNew();
        });
    }

    string ProfilePath(string name) => Path.Combine(profilesDir, name + ".json");

    /// <summary>사용 중인 프로필 이름. "사용"을 누른 직후에는 MainForm이 아직 새 엔진으로 바꾸기 전이라
    /// engine.Config는 이전 프로필을 가리키므로, 이 창에서 만든 NewEngine을 우선한다.</summary>
    string? ActiveProfileName => (NewEngine ?? engine).Config.EffectiveProfileName;

    bool ProfileHasAudioSource(string name)
    {
        try
        {
            var node = JsonNode.Parse(File.ReadAllText(ProfilePath(name))) as JsonObject;
            return node is not null && Config.ReadAudioSourceList(node).Any(s => Config.GetAudioSourceType(s) is not null);
        }
        catch { return false; } // 읽을 수 없는 파일도 고쳐야 할 대상으로 표시한다
    }

    bool IsActive(string? name) => name is not null && string.Equals(name, ActiveProfileName, StringComparison.OrdinalIgnoreCase);

    /// <summary>프로필 콤보 항목: 사용 중인 프로필은 초록 점, 오디오 소스가 없는 프로필은 ⚠로 표시한다.
    /// 펼친 목록에서 가리킨 항목만 강조색으로 칠하고, 접힌 상태(편집 영역)는 늘 어두운 바탕이다.</summary>
    void DrawProfileItem(object? sender, DrawItemEventArgs e)
    {
        bool inEditArea = (e.State & DrawItemState.ComboBoxEdit) != 0;
        bool highlighted = !inEditArea && (e.State & DrawItemState.Selected) != 0;
        using (var back = new SolidBrush(highlighted ? Theme.Accent : Theme.Surface))
            e.Graphics.FillRectangle(back, e.Bounds);
        if (e.Index < 0 || list.Items[e.Index] is not ProfileItem item) return;

        if (item.Active)
            using (var dot = new SolidBrush(Theme.Success))
                e.Graphics.FillEllipse(dot, e.Bounds.Left + 8, e.Bounds.Top + e.Bounds.Height / 2 - 4, 8, 8);

        int warnWidth = item.HasAudioSource ? 0 : 24;
        if (!item.HasAudioSource)
            TextRenderer.DrawText(e.Graphics, "⚠", list.Font,
                new Rectangle(e.Bounds.Right - 26, e.Bounds.Top, 24, e.Bounds.Height), Color.Goldenrod,
                TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);

        var textBounds = new Rectangle(e.Bounds.Left + 22, e.Bounds.Top, e.Bounds.Width - 26 - warnWidth, e.Bounds.Height);
        TextRenderer.DrawText(e.Graphics, item.Name ?? "", list.Font, textBounds, highlighted ? Color.White : Theme.Text,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }

    void BuildUi()
    {
        Dock = DockStyle.Fill;
        Font = new Font("Segoe UI", 10);
        BackColor = Color.FromArgb(24, 24, 28);
        ForeColor = Color.Gainsboro;

        // --- 프로필 선택 줄 (위) ---
        var topBar = new Panel { Dock = DockStyle.Top, Height = 56, BackColor = Theme.Surface };
        var profileLabel = new Label { Text = "프로필", AutoSize = true, ForeColor = Theme.SubText, Location = new Point(16, 18) };
        list.DropDownStyle = ComboBoxStyle.DropDownList;
        list.DrawMode = DrawMode.OwnerDrawFixed; // 직접 그리므로 Theme이 다시 손대지 않는다
        list.ItemHeight = 26;
        list.Location = new Point(76, 13);
        list.Width = 260;
        list.DrawItem += DrawProfileItem;
        list.SelectedIndexChanged += (s, e) =>
        {
            if (suppressSelection) return;
            if (!ConfirmUnsavedChanges())
            {
                suppressSelection = true;
                list.SelectedItem = list.Items.Cast<ProfileItem>().FirstOrDefault(i => i.Name == selected?.Name);
                suppressSelection = false;
                return;
            }
            OnSelectionChanged();
        };

        addBtn.Text = "+ 새 프로필"; addBtn.Size = new Size(110, 32); addBtn.Location = new Point(348, 12);
        addBtn.Click += (s, e) => AddNew();
        removeBtn.Text = "삭제"; removeBtn.Size = new Size(70, 32); removeBtn.Location = new Point(464, 12);
        removeBtn.Click += (s, e) => RemoveSelected();

        // 오른쪽: 사용 상태 표시와 "이 프로필 사용" 버튼. 고른 프로필을 바로 옆에서 적용할 수 있게 한다.
        activeBadge.AutoSize = true;
        activeBadge.Margin = new Padding(0, 6, 10, 0);
        applyBtn.Text = "이 프로필 사용"; applyBtn.Size = new Size(130, 32); applyBtn.Margin = new Padding(0);
        applyBtn.Click += async (s, e) => await ApplyAsync();
        var activeArea = new FlowLayoutPanel
        {
            Dock = DockStyle.Right, AutoSize = true, WrapContents = false,
            Padding = new Padding(0, 12, 14, 0), BackColor = Theme.Surface,
        };
        activeArea.Controls.AddRange([activeBadge, applyBtn]);

        topBar.Controls.AddRange([profileLabel, list, addBtn, removeBtn, activeArea]);

        // --- 상세 패널 (우) ---
        detailTitle.AutoSize = true;
        detailTitle.Margin = new Padding(12, 8, 0, 8);
        detailTitle.Font = new Font("Segoe UI", 14, FontStyle.Bold);

        baseNoteLabel.AutoSize = true;
        baseNoteLabel.Margin = new Padding(12, 0, 12, 0);
        baseNoteLabel.ForeColor = Color.Silver;
        baseNoteLabel.Text = "아직 프로필이 없습니다. 위쪽 '+ 새 프로필'로 만들어 보세요.\n" +
                             "프로필마다 무엇을 들을지(오디오 소스)를 정해야 받아 적기를 시작할 수 있습니다.";
        baseNoteLabel.MaximumSize = new Size(540, 0);

        // --- 모델 재정의 ---
        overrideModelCheck.Text = "모델 재정의";
        overrideModelCheck.AutoSize = true;
        overrideModelCheck.Location = new Point(12, 4);
        overrideModelCheck.CheckedChanged += (s, e) => { modelCombo.Enabled = overrideModelCheck.Checked; MarkDirty(); };

        modelCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        modelCombo.Location = new Point(160, 1);
        modelCombo.Width = 200;
        modelCombo.SelectedIndexChanged += (s, e) => MarkDirty();

        modelHeaderPanel.Size = new Size(560, 34); // 폭을 안 주면 기본 200px라 오른쪽 콤보박스가 잘린다
        modelHeaderPanel.Margin = new Padding(0, 0, 0, 4);
        modelHeaderPanel.Controls.Add(overrideModelCheck);
        modelHeaderPanel.Controls.Add(modelCombo);

        // --- 화자별 모델 ---
        speakerModelLabel.AutoSize = true;
        speakerModelLabel.Margin = new Padding(12, 4, 0, 4);
        speakerModelLabel.ForeColor = Color.Silver;
        speakerModelLabel.Text = "화자별 모델 (목록에 없는 화자는 위 모델을 사용)";

        speakerGrid.Width = 560;
        speakerGrid.Height = 120;
        speakerGrid.Margin = new Padding(12, 0, 12, 0);
        speakerGrid.AllowUserToAddRows = true;
        speakerGrid.AllowUserToDeleteRows = true;
        speakerGrid.RowHeadersVisible = false;
        speakerGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        speakerGrid.BackgroundColor = Color.FromArgb(30, 30, 34);
        speakerGrid.DefaultCellStyle.BackColor = Color.FromArgb(30, 30, 34);
        speakerGrid.DefaultCellStyle.ForeColor = Color.Gainsboro;
        speakerGrid.EnableHeadersVisualStyles = false;
        speakerGrid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(40, 40, 46);
        speakerGrid.ColumnHeadersDefaultCellStyle.ForeColor = Color.Gainsboro;
        speakerGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "speaker", HeaderText = "화자 (표시 이름)" });
        speakerModelColumn.Name = "model";
        speakerModelColumn.HeaderText = "모델";
        speakerModelColumn.FlatStyle = FlatStyle.Flat;
        speakerGrid.Columns.Add(speakerModelColumn);
        // 설치되지 않은 모델 id가 파일에 적혀 있으면 콤보 셀이 예외를 던진다. 그 값은 Load에서 항목에 추가하므로 무시해도 된다.
        speakerGrid.DataError += (s, e) => e.ThrowException = false;
        speakerGrid.CellValueChanged += (s, e) => MarkDirty();
        speakerGrid.UserDeletedRow += (s, e) => MarkDirty();

        var addSpeakersBtn = new Button { Text = "인식된 화자 추가", Width = 150, Height = 28, Location = new Point(12, 2) };
        addSpeakersBtn.Click += (s, e) => AddRecognizedSpeakers();
        speakerButtonsPanel.Height = 34;
        speakerButtonsPanel.Margin = new Padding(0, 4, 0, 8);
        speakerButtonsPanel.Controls.Add(addSpeakersBtn);

        // 오디오 소스는 프로필마다 반드시 정한다(기본값에서 물려받지 않는다). 여러 개를 동시에 쓸 수 있고,
        // 브라우저 탭처럼 위쪽 탭으로 고르면 아래에는 그 소스 하나의 설정만 보인다.
        sourceTabs.FlowDirection = FlowDirection.LeftToRight;
        sourceTabs.WrapContents = true;
        sourceTabs.AutoSize = true;
        sourceTabs.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        sourceTabs.Margin = new Padding(12, 0, 0, 6);

        aliasWarning.AutoSize = true;
        aliasWarning.Margin = new Padding(12, 0, 12, 8);
        aliasWarning.MaximumSize = new Size(560, 0);
        aliasWarning.ForeColor = Color.Goldenrod;
        aliasWarning.Visible = false;

        sourcesPanel.FlowDirection = FlowDirection.TopDown;
        sourcesPanel.WrapContents = false;
        sourcesPanel.AutoSize = true;
        sourcesPanel.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        sourcesPanel.Margin = new Padding(0);

        otherJsonLabel.AutoSize = true;
        otherJsonLabel.Margin = new Padding(12, 12, 0, 4);
        otherJsonLabel.ForeColor = Color.Silver;
        otherJsonLabel.Text = "기타 설정 재정의 (JSON)";

        otherJsonBox.Multiline = true;
        otherJsonBox.ScrollBars = ScrollBars.Vertical;
        otherJsonBox.Width = 560;
        otherJsonBox.Height = 140;
        otherJsonBox.Margin = new Padding(12, 0, 12, 0);
        otherJsonBox.Font = new Font("Consolas", 9.5f);
        otherJsonBox.TextChanged += (s, e) => { if (!suppressEvents) { ValidateOtherJson(); MarkDirty(); } };

        jsonErrorLabel.AutoSize = true;
        jsonErrorLabel.Margin = new Padding(12, 4, 0, 0);
        jsonErrorLabel.ForeColor = Color.IndianRed;

        contentPanel.FlowDirection = FlowDirection.TopDown;
        contentPanel.WrapContents = false;
        contentPanel.AutoSize = true;
        contentPanel.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        contentPanel.Dock = DockStyle.Top;

        detailScroll.Dock = DockStyle.Fill;
        detailScroll.AutoScroll = true;
        detailScroll.Controls.Add(contentPanel);

        // --- 섹션 목록 (가운데) ---
        sectionList.Dock = DockStyle.Fill;
        sectionList.BackColor = Theme.Surface;
        sectionList.BorderStyle = BorderStyle.None;
        sectionList.IntegralHeight = false;
        sectionList.DrawMode = DrawMode.OwnerDrawFixed;
        sectionList.ItemHeight = 34;
        foreach (var section in Sections) sectionList.Items.Add(section.Title);
        sectionList.DrawItem += DrawSectionItem;
        sectionList.SelectedIndexChanged += (s, e) =>
        {
            if (sectionList.SelectedIndex < 0 || Sections[sectionList.SelectedIndex].Id == currentSection) return;
            currentSection = Sections[sectionList.SelectedIndex].Id;
            // 목록은 선택이 바뀌는 순간 항목을 다시 그리는데 그때는 currentSection이 아직 이전 값이다.
            // 바꾼 뒤 한 번 더 그려야 고른 항목이 선택된 모양이 된다.
            sectionList.Invalidate();
            RenderContentPanel();
            detailScroll.AutoScrollPosition = Point.Empty;
        };
        sectionList.SelectedIndex = 0;
        sectionBar.Dock = DockStyle.Left;
        sectionBar.Width = 150;
        sectionBar.Padding = new Padding(0, 12, 0, 0);
        sectionBar.BackColor = Theme.Surface;
        sectionBar.Controls.Add(sectionList);

        sectionTitle.AutoSize = true;
        sectionTitle.Margin = new Padding(12, 4, 0, 2);
        sectionTitle.Font = new Font("Segoe UI", 12, FontStyle.Bold);
        sectionDesc.AutoSize = true;
        sectionDesc.Margin = new Padding(12, 0, 12, 12);
        sectionDesc.MaximumSize = new Size(560, 0);
        sectionDesc.ForeColor = Theme.SubText;

        var body = new Panel { Dock = DockStyle.Fill };
        body.Controls.Add(detailScroll);
        body.Controls.Add(new Panel { Dock = DockStyle.Left, Width = 1, BackColor = Theme.Border });
        body.Controls.Add(sectionBar);

        // --- 하단 바 ---
        hint.Dock = DockStyle.Top; hint.Height = 28;
        hint.Padding = new Padding(4, 6, 4, 0); hint.ForeColor = Color.Goldenrod;

        var buttons = new Panel { Dock = DockStyle.Top, Height = 44 };
        saveBtn.Text = "저장"; saveBtn.Width = 90; saveBtn.Height = 30; saveBtn.Location = new Point(4, 6);
        saveBtn.Click += (s, e) => Save();
        buttons.Controls.Add(saveBtn);

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 74, BackColor = Theme.Surface, Padding = new Padding(8, 0, 8, 0) };
        bottom.Controls.Add(buttons);
        bottom.Controls.Add(hint);

        Controls.Add(body);
        Controls.Add(new Panel { Dock = DockStyle.Top, Height = 1, BackColor = Theme.Border });
        Controls.Add(topBar);
        Controls.Add(new Panel { Dock = DockStyle.Bottom, Height = 1, BackColor = Theme.Border });
        Controls.Add(bottom);
    }

    /// <summary>프로필 목록을 다시 채운다. reloadDetail이 false면 선택은 유지하되 오른쪽 상세 화면은
    /// 다시 읽지 않는다(적용처럼 목록 표시만 바뀌는 경우).</summary>
    void RefreshList(bool reloadDetail = true)
    {
        string? keepName = selected?.Name;
        bool hadSelection = selected is not null;

        suppressSelection = true;
        try
        {
            list.Items.Clear();
            foreach (var n in Config.ListProfileNames(profilesDir))
                list.Items.Add(new ProfileItem(n, IsActive(n), ProfileHasAudioSource(n)));

            // 처음 열 때는 지금 사용 중인 프로필을, 이후에는 보던 프로필을 그대로 선택한다.
            var items = list.Items.Cast<ProfileItem>().ToList();
            int idx = hadSelection
                ? items.FindIndex(i => string.Equals(i.Name, keepName, StringComparison.OrdinalIgnoreCase))
                : items.FindIndex(i => i.Active);

            list.SelectedIndex = items.Count == 0 ? -1 : Math.Max(idx, 0);
        }
        finally { suppressSelection = false; }

        if (reloadDetail)
        {
            OnSelectionChanged();
        }
        else
        {
            selected = list.SelectedItem as ProfileItem;
            UpdateButtons();
        }
    }

    void OnSelectionChanged()
    {
        if (list.SelectedItem is not ProfileItem item)
        {
            selected = null;
            detailTitle.Text = "프로필 없음";
            RenderContentPanel();
            UpdateButtons();
            return;
        }
        selected = item;
        LoadDetailForSelected();
        UpdateButtons();
    }

    void LoadDetailForSelected()
    {
        jsonErrorLabel.Text = "";

        if (selected?.Name is null) return;

        detailTitle.Text = selected.Name;
        string path = ProfilePath(selected.Name);
        currentPatch = File.Exists(path)
            ? (JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? new JsonObject())
            : new JsonObject();

        suppressEvents = true;
        try
        {
            // 저장된 오디오 소스가 없으면 종류를 고르지 않은 편집기 하나를 두고 경고를 보여준다.
            ClearSourceEditors();
            var sources = Config.ReadAudioSourceList(currentPatch)
                .Where(src => Config.GetAudioSourceType(src) is not null).ToList();
            for (int i = 0; i < sources.Count; i++)
                AddSourceEditorCore(sources[i], i);
            if (editors.Count == 0)
                AddSourceEditorCore(null, null);
            RenumberSourceEditors();
            SelectSourceEditor(editors[0]);

            LoadModelOverrides();

            var otherObj = new JsonObject();
            foreach (var kv in currentPatch.ToList())
                if (!DedicatedKeys.Contains(kv.Key))
                    otherObj[kv.Key] = kv.Value?.DeepClone();
            otherJsonBox.Text = otherObj.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        }
        finally { suppressEvents = false; }

        RenderContentPanel();
        ValidateOtherJson();
        dirty = false;
        UpdateButtons();
        detailScroll.AutoScrollPosition = Point.Empty; // 다른 프로필을 고른 것이므로 맨 위부터 보여준다
    }

    int EditorWidth => Math.Max(detailScroll.ClientSize.Width - 24, 300);

    AudioSourceEditor AddSourceEditorCore(JsonObject? seed, int? originIndex)
    {
        var editor = new AudioSourceEditor(engine.Config, seed, originIndex, EditorWidth);
        editor.Changed += MarkDirty;
        editor.TitleChanged += RefreshSourceTabs;
        editor.RemoveRequested += RemoveSourceEditor;
        editor.Visible = false; // 탭으로 고른 것만 보인다
        editors.Add(editor);
        sourcesPanel.Controls.Add(editor);
        return editor;
    }

    void AddSourceEditor()
    {
        var editor = AddSourceEditorCore(null, null);
        RenumberSourceEditors();
        SelectSourceEditor(editor);
        MarkDirty();
    }

    void RemoveSourceEditor(AudioSourceEditor editor)
    {
        if (editors.Count <= 1) return;
        int index = editors.IndexOf(editor);
        editors.Remove(editor);
        sourcesPanel.Controls.Remove(editor);
        editor.Dispose();
        RenumberSourceEditors();
        // 지운 탭이 보던 탭이면 그 자리의 이웃 탭으로 옮긴다.
        SelectSourceEditor(selectedEditor is not null && editors.Contains(selectedEditor)
            ? selectedEditor
            : editors[Math.Min(index, editors.Count - 1)]);
        MarkDirty();
    }

    void SelectSourceEditor(AudioSourceEditor editor)
    {
        selectedEditor = editor;
        sourcesPanel.SuspendLayout();
        foreach (var e in editors) e.Visible = e == editor;
        sourcesPanel.ResumeLayout();
        RefreshSourceTabs();
    }

    /// <summary>탭 줄을 다시 만든다: 소스마다 "번호 별명" 탭, 끝에 "+ 추가" 탭. 고른 탭은 강조색으로 칠한다.
    /// 소스가 하나뿐이면 여러 소스를 동시에 쓸 수 있다는 안내를 옆에 붙인다. 탭은 끌어서 순서를 바꿀 수 있다.</summary>
    void RefreshSourceTabs()
    {
        var duplicates = DuplicateAliases();

        sourceTabs.SuspendLayout();
        try
        {
            foreach (var old in sourceTabs.Controls.Cast<Control>().ToList())
            {
                sourceTabs.Controls.Remove(old);
                old.Dispose();
            }
            sourceTabs.MaximumSize = new Size(EditorWidth, 0);

            for (int i = 0; i < editors.Count; i++)
            {
                var editor = editors[i];
                string name = editor.SelectedType is null ? "⚠ 종류 선택"
                    : duplicates.Contains(editor.Alias) ? $"⚠ {editor.Alias}"
                    : editor.Alias;
                var tab = SourceTabButton($"{i + 1}  {name}");
                Theme.StyleButton(tab, editor == selectedEditor ? Theme.ButtonKind.Primary : Theme.ButtonKind.Normal);
                tab.Click += (s, e) => SelectSourceEditor(editor);
                if (editors.Count > 1) EnableTabDrag(tab, editor);
                sourceTabs.Controls.Add(tab);
            }

            var addTab = SourceTabButton("+ 추가");
            Theme.StyleButton(addTab, Theme.ButtonKind.Normal);
            addTab.Click += (s, e) => AddSourceEditor();
            sourceTabs.Controls.Add(addTab);

            if (editors.Count == 1)
                sourceTabs.Controls.Add(new Label
                {
                    Text = "소스를 더 추가하면 동시에 받아 적습니다",
                    AutoSize = true, ForeColor = Theme.SubText, Margin = new Padding(6, 8, 0, 0)
                });
        }
        finally { sourceTabs.ResumeLayout(); }

        // 별명이 겹치면 목록·내보내기에서 어느 소스의 말인지 구분되지 않으므로 경고한다(저장은 막지 않는다).
        aliasWarning.Visible = duplicates.Count > 0;
        if (duplicates.Count > 0)
        {
            var groups = duplicates.Select(alias =>
                $"'{alias}' ({string.Join(", ", editors.Select((e, i) => (e, i))
                    .Where(p => p.e.SelectedType is not null && string.Equals(p.e.Alias, alias, StringComparison.OrdinalIgnoreCase))
                    .Select(p => $"{p.i + 1}번"))})");
            aliasWarning.Text = $"⚠ 별명이 겹칩니다: {string.Join(", ", groups)}. " +
                                "목록과 내보내기에서 어느 소스인지 구분되지 않으니 별명을 다르게 지정하세요.";
        }
    }

    /// <summary>종류를 고른 소스 중 두 개 이상이 같은 별명(대소문자 무시)을 쓰는 별명 목록.</summary>
    HashSet<string> DuplicateAliases() =>
        editors.Where(e => e.SelectedType is not null)
            .GroupBy(e => e.Alias, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>탭을 끌어 다른 탭 위에 놓으면 그 자리로 옮긴다. 조금 움직여야 끌기로 보므로 클릭은 그대로 탭 선택이다.</summary>
    void EnableTabDrag(Button tab, AudioSourceEditor editor)
    {
        Point? downAt = null;
        tab.AllowDrop = true;
        tab.MouseDown += (s, e) => { if (e.Button == MouseButtons.Left) downAt = e.Location; };
        tab.MouseUp += (s, e) => downAt = null;
        tab.MouseMove += (s, e) =>
        {
            if (downAt is not { } start || e.Button != MouseButtons.Left) return;
            var drag = SystemInformation.DragSize;
            if (Math.Abs(e.X - start.X) < drag.Width && Math.Abs(e.Y - start.Y) < drag.Height) return;
            downAt = null;
            tab.DoDragDrop(editor, DragDropEffects.Move);
        };
        tab.DragOver += (s, e) =>
            e.Effect = e.Data?.GetData(typeof(AudioSourceEditor)) is AudioSourceEditor dragged && dragged != editor
                ? DragDropEffects.Move : DragDropEffects.None;
        tab.DragDrop += (s, e) =>
        {
            if (e.Data?.GetData(typeof(AudioSourceEditor)) is not AudioSourceEditor dragged || dragged == editor) return;
            // 끌던 탭 버튼은 DoDragDrop이 끝나야 정리할 수 있으므로 탭 다시 그리기는 다음 메시지로 미룬다.
            BeginInvoke(() => MoveSourceEditor(dragged, editors.IndexOf(editor)));
        };
    }

    /// <summary>소스 순서를 바꾼다. 순서가 곧 소스 번호이므로 번호·탭을 다시 매기고 옮긴 소스를 보여준다.
    /// 저장해야 파일에 반영된다(디스크의 원래 자리는 OriginIndex가 기억하므로 학습된 값은 그대로 따라간다).</summary>
    void MoveSourceEditor(AudioSourceEditor editor, int newIndex)
    {
        if (IsDisposed || !editors.Contains(editor) || newIndex < 0) return;
        editors.Remove(editor);
        editors.Insert(Math.Min(newIndex, editors.Count), editor);
        for (int i = 0; i < editors.Count; i++) sourcesPanel.Controls.SetChildIndex(editors[i], i);
        RenumberSourceEditors();
        SelectSourceEditor(editor);
        MarkDirty();
    }

    static Button SourceTabButton(string text) => new()
    {
        Text = text,
        AutoSize = true,
        MinimumSize = new Size(64, 32),
        Padding = new Padding(8, 0, 8, 0),
        Margin = new Padding(0, 0, 4, 4),
    };

    void ClearSourceEditors()
    {
        foreach (var editor in editors)
        {
            sourcesPanel.Controls.Remove(editor);
            editor.Dispose();
        }
        editors.Clear();
    }

    void RenumberSourceEditors()
    {
        for (int i = 0; i < editors.Count; i++)
            editors[i].SetIndex(i, removable: editors.Count > 1);
    }

    static string? StringValue(JsonNode? node) =>
        node is JsonValue v && v.TryGetValue(out string? s) && !string.IsNullOrWhiteSpace(s) ? s : null;

    /// <summary>currentPatch의 model/speakerModels를 모델 콤보와 화자 그리드에 채운다.
    /// 설치되지 않은 모델이 적혀 있어도 값이 사라지지 않도록 선택지에 함께 넣는다.</summary>
    void LoadModelOverrides()
    {
        string? modelOverride = StringValue(currentPatch["model"]);
        var speakerModels = (currentPatch["speakerModels"] as JsonObject)?
            .Select(kv => (Speaker: kv.Key, Model: StringValue(kv.Value)))
            .Where(p => p.Model is not null)
            .ToList() ?? [];

        var choices = ModelStore.ListInstalled()
            .Concat(speakerModels.Select(p => p.Model!))
            .Concat(modelOverride is null ? Array.Empty<string>() : new[] { modelOverride })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        modelCombo.Items.Clear();
        modelCombo.Items.AddRange(choices);
        overrideModelCheck.Checked = modelOverride is not null;
        modelCombo.Enabled = modelOverride is not null;
        modelCombo.SelectedItem = modelOverride
            ?? choices.FirstOrDefault(c => c.Equals(ModelStore.ResolveDefault(engine.Config.Model), StringComparison.OrdinalIgnoreCase));

        speakerGrid.Rows.Clear();
        speakerModelColumn.Items.Clear();
        speakerModelColumn.Items.AddRange(choices);
        foreach (var (speaker, model) in speakerModels)
            speakerGrid.Rows.Add(speaker, model!);
    }

    void AddRecognizedSpeakers()
    {
        var existing = speakerGrid.Rows.Cast<DataGridViewRow>()
            .Where(r => !r.IsNewRow)
            .Select(r => r.Cells["speaker"].Value?.ToString()?.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (string speaker in engine.GetRecodes()
                     .Select(kv => kv.Value.speaker)
                     .OfType<string>()
                     .Where(s => !string.IsNullOrWhiteSpace(s))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (existing.Add(speaker))
            {
                int i = speakerGrid.Rows.Add();
                speakerGrid.Rows[i].Cells["speaker"].Value = speaker; // 모델 칸은 비워 둔다
            }
        }
    }

    /// <summary>오른쪽에 고른 섹션 하나만 그린다. 프로필이 없으면 섹션 목록을 숨기고 안내만 보인다.</summary>
    void RenderContentPanel()
    {
        contentPanel.SuspendLayout();
        try
        {
            contentPanel.Controls.Clear();

            sectionBar.Visible = selected?.Name is not null;
            if (selected?.Name is null)
            {
                contentPanel.Controls.Add(detailTitle);
                contentPanel.Controls.Add(baseNoteLabel);
                return;
            }

            var info = Sections.First(x => x.Id == currentSection);
            sectionTitle.Text = info.Title;
            sectionDesc.Text = info.Description;
            contentPanel.Controls.Add(sectionTitle);
            contentPanel.Controls.Add(sectionDesc);

            switch (currentSection)
            {
                case Section.AudioSources:
                    contentPanel.Controls.Add(sourceTabs);
                    contentPanel.Controls.Add(aliasWarning);
                    contentPanel.Controls.Add(sourcesPanel);
                    break;
                case Section.Models:
                    contentPanel.Controls.Add(modelHeaderPanel);
                    contentPanel.Controls.Add(speakerModelLabel);
                    contentPanel.Controls.Add(speakerGrid);
                    contentPanel.Controls.Add(speakerButtonsPanel);
                    break;
                case Section.Advanced:
                    contentPanel.Controls.Add(otherJsonLabel);
                    contentPanel.Controls.Add(otherJsonBox);
                    contentPanel.Controls.Add(jsonErrorLabel);
                    break;
            }
        }
        finally { contentPanel.ResumeLayout(); }
    }

    /// <summary>섹션 목록 항목. 고른 섹션은 강조색 막대로, 고칠 것이 있는 섹션(오디오 소스 없음, JSON 오류)은 ⚠로 표시한다.</summary>
    void DrawSectionItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0) return;
        var section = Sections[e.Index];
        bool isSelected = section.Id == currentSection;

        using (var back = new SolidBrush(isSelected ? Theme.SelectedRow : Theme.Surface))
            e.Graphics.FillRectangle(back, e.Bounds);
        if (isSelected)
            using (var bar = new SolidBrush(Theme.Accent))
                e.Graphics.FillRectangle(bar, e.Bounds.Left, e.Bounds.Top + 6, 3, e.Bounds.Height - 12);

        bool needsAttention = selected?.Name is not null && section.Id switch
        {
            Section.AudioSources => !editors.Any(x => x.SelectedType is not null) || DuplicateAliases().Count > 0,
            Section.Advanced => !IsOtherJsonValid(),
            _ => false,
        };
        if (needsAttention)
            TextRenderer.DrawText(e.Graphics, "⚠", sectionList.Font,
                new Rectangle(e.Bounds.Right - 28, e.Bounds.Top, 24, e.Bounds.Height), Color.Goldenrod,
                TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);

        using var font = new Font(sectionList.Font, isSelected ? FontStyle.Bold : FontStyle.Regular);
        TextRenderer.DrawText(e.Graphics, section.Title, font,
            new Rectangle(e.Bounds.Left + 16, e.Bounds.Top, e.Bounds.Width - 44, e.Bounds.Height),
            isSelected ? Color.White : Theme.SubText,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }

    bool IsOtherJsonValid()
    {
        try { return (JsonNode.Parse(string.IsNullOrWhiteSpace(otherJsonBox.Text) ? "{}" : otherJsonBox.Text) as JsonObject) is not null; }
        catch { return false; }
    }

    void ValidateOtherJson()
    {
        jsonErrorLabel.Text = IsOtherJsonValid() ? "" : "JSON 형식이 올바르지 않습니다.";
        UpdateButtons();
    }

    void UpdateButtons()
    {
        sectionList.Invalidate();
        bool hasSelection = selected?.Name is not null;
        bool jsonOk = !hasSelection || IsOtherJsonValid();
        bool isActive = IsActive(selected?.Name);

        saveBtn.Enabled = hasSelection && jsonOk;
        saveBtn.Text = dirty ? "저장 •" : "저장"; // 저장하지 않은 변경이 있으면 점으로 표시
        removeBtn.Enabled = hasSelection && !isActive;
        activeBadge.Text = !hasSelection ? "" : isActive ? "● 사용 중" : "사용 중이 아님";
        activeBadge.ForeColor = isActive ? Theme.Success : Theme.SubText;
        // 사용 중인 프로필이면 고친 내용을 엔진에 다시 반영하는 버튼이 된다.
        applyBtn.Text = isActive ? "변경 적용" : "이 프로필 사용";

        bool hasAudioSource = editors.Any(e => e.SelectedType is not null);
        if (engine.IsRunning)
        {
            applyBtn.Enabled = false;
            hint.Text = "캡처를 정지한 뒤 프로필을 바꿀 수 있습니다.";
        }
        else if (hasSelection && !hasAudioSource)
        {
            applyBtn.Enabled = false;
            hint.Text = "⚠ 오디오 소스를 고르면 이 프로필을 사용할 수 있습니다.";
        }
        else
        {
            applyBtn.Enabled = hasSelection && jsonOk;
            hint.Text = "";
        }
    }

    /// <summary>고른 프로필을 저장한다. 고급 섹션의 JSON이 잘못돼 저장하지 못하면 false.</summary>
    bool Save()
    {
        if (selected?.Name is not { } name) return true;
        foreach (var editor in editors) editor.CommitPending();
        if (speakerGrid.IsCurrentCellInEditMode) speakerGrid.EndEdit();
        if (!IsOtherJsonValid())
        {
            hint.Text = "⚠ 고급 섹션의 JSON 형식이 올바르지 않아 저장하지 못했습니다.";
            return false;
        }

        string path = ProfilePath(name);
        var onDisk = File.Exists(path)
            ? (JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? new JsonObject())
            : new JsonObject();

        var combined = new JsonObject();
        if (overrideModelCheck.Checked && modelCombo.SelectedItem is string model)
            combined["model"] = model;

        var speakerModels = new JsonObject();
        foreach (DataGridViewRow r in speakerGrid.Rows)
        {
            if (r.IsNewRow) continue;
            string? speaker = r.Cells["speaker"].Value?.ToString()?.Trim();
            string? speakerModel = r.Cells["model"].Value?.ToString();
            if (!string.IsNullOrEmpty(speaker) && !string.IsNullOrEmpty(speakerModel))
                speakerModels[speaker] = speakerModel;
        }
        if (speakerModels.Count > 0)
            combined["speakerModels"] = speakerModels;

        var diskSources = Config.ReadAudioSourceList(onDisk)
            .Where(src => Config.GetAudioSourceType(src) is not null).ToList();
        var savedSources = new List<JsonObject>();
        foreach (var editor in editors)
        {
            JsonObject? original = editor.OriginIndex is int o && o < diskSources.Count ? diskSources[o] : null;
            if (editor.BuildJson(original) is { } obj) savedSources.Add(obj);
        }
        if (savedSources.Count > 0)
            combined["audioSources"] = new JsonArray(savedSources.Select(o => (JsonNode)o.DeepClone()).ToArray());

        var otherParsed = JsonNode.Parse(string.IsNullOrWhiteSpace(otherJsonBox.Text) ? "{}" : otherJsonBox.Text) as JsonObject ?? new JsonObject();
        foreach (var kv in otherParsed.ToList())
            if (!DedicatedKeys.Contains(kv.Key))
                combined[kv.Key] = kv.Value?.DeepClone();

        Directory.CreateDirectory(profilesDir);
        File.WriteAllText(path, combined.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        // 화면은 이미 저장한 내용 그대로이므로 다시 읽어 그리지 않는다(깜빡임·스크롤 초기화 방지).
        currentPatch = combined.DeepClone().AsObject();
        if (selected is not null)
        {
            selected.HasAudioSource = savedSources.Count > 0;
            list.Invalidate(); // 프로필 목록의 경고 표시 갱신
        }

        // 종류를 고르지 않아 빠진 편집기는 화면에서도 정리하고(하나는 남긴다), 남은 것은 저장된 자리를 기억한다.
        var kept = editors.Where(e => e.SelectedType is not null).ToList();
        if (kept.Count > 0)
            foreach (var dropped in editors.Except(kept).ToList())
                RemoveSourceEditor(dropped);
        for (int i = 0; i < kept.Count; i++) kept[i].OriginIndex = i;

        // 지금 저장한 프로필이 활성 프로필이면, 재시작 없이도 실행 중인 오디오 소스가
        // 고친 값(예: knownUsers 이름)을 바로 반영하도록 알린다.
        if (string.Equals(name, engine.Config.EffectiveProfileName, StringComparison.OrdinalIgnoreCase))
            for (int i = 0; i < savedSources.Count; i++)
                engine.ApplyRuntimeOptions(i, savedSources[i]);

        dirty = false;
        UpdateButtons();
        hint.Text = $"'{name}' 저장됨";
        return true;
    }

    void MarkDirty()
    {
        if (suppressEvents) return;
        dirty = true;
        UpdateButtons();
    }

    /// <summary>저장하지 않은 변경이 있으면 저장할지 묻는다. 계속 진행해도 되면 true(저장했거나 버리기로 함),
    /// 취소했거나 저장에 실패했으면 false.</summary>
    public bool ConfirmUnsavedChanges()
    {
        if (!dirty || selected?.Name is not { } name) return true;
        var answer = MessageBox.Show(this, $"'{name}' 프로필에 저장하지 않은 변경이 있습니다.\n저장할까요?",
            "저장하지 않은 변경", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);
        return answer switch
        {
            DialogResult.Yes => Save(),
            DialogResult.No => true,
            _ => false,
        };
    }



    async Task ApplyAsync()
    {
        if (engine.IsRunning || selected?.Name is not { } profileName) return;
        applyBtn.Enabled = false;
        try
        {
            if (!Save()) return;

            NewEngine?.Dispose(); // 이전에 이 폼에서 만들었지만 MainForm에 넘기지 못한 engine(있다면) 정리
            Config.SetActiveProfile(baseConfigPath, profileName);
            NewEngine = Config.BuildEngine(baseConfigPath, profilesDir, profileName);

            RefreshList(reloadDetail: false);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "프로필 적용 실패", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { UpdateButtons(); }
    }

    void AddNew()
    {
        if (!ConfirmUnsavedChanges()) return;
        using var dlg = new NewProfileDialog(Config.ListProfileNames(profilesDir));
        if (dlg.ShowDialog(this) != DialogResult.OK || dlg.ProfileName is not { } name) return;

        Directory.CreateDirectory(profilesDir);
        File.WriteAllText(ProfilePath(name), "{}");
        selected = new ProfileItem(name, false);
        RefreshList();
    }

    void RemoveSelected()
    {
        if (selected?.Name is not { } name) return;
        if (IsActive(name))
        {
            MessageBox.Show(this, "현재 사용 중인 프로필은 제거할 수 없습니다.", "제거 불가", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (MessageBox.Show(this, $"'{name}' 프로필을 제거하시겠습니까?", "제거 확인",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            return;

        File.Delete(ProfilePath(name));
        dirty = false;
        selected = null;
        RefreshList();
    }
}
