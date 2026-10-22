using System.Text.Json;
using System.Text.Json.Nodes;
using HearingAid.Core;
using HearingAid.Core.Audio;
using HearingAid.Core.Stt;

namespace HearingAid;

/// <summary>프로필(profiles/&lt;이름&gt;.json) 목록을 관리하고, 선택한 프로필을 적용해
/// Config/IAudioSource/TranscriberEngine을 통째로 새로 만든다. 실제 교체는 MainForm이 수행한다
/// (이 폼은 NewEngine에 결과만 담아둔다).</summary>
public class ProfileSettingsForm : Form
{
    readonly ITranscriber engine;
    readonly string baseConfigPath;
    readonly string profilesDir;

    public ITranscriber? NewEngine { get; private set; }

    // 좌측 사이드바
    readonly ListBox list = new();
    readonly Button addBtn = new();
    readonly Button removeBtn = new();

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
    readonly Panel overrideHeaderPanel = new();
    readonly Label audioSourceTitle = new();
    readonly ComboBox typeCombo = new();
    readonly Label audioSourceDescLabel = new();
    readonly Label otherJsonLabel = new();
    readonly TextBox otherJsonBox = new();
    readonly Label jsonErrorLabel = new();

    // 하단 바
    readonly Label current = new();
    readonly Label hint = new();
    readonly Button saveBtn = new();
    readonly Button applyBtn = new();
    readonly Button refreshBtn = new();
    readonly Button closeBtn = new();

    ProfileItem? selected;
    JsonObject currentPatch = new();
    bool suppressEvents;
    bool suppressSelection;
    readonly List<AudioSourceSettingRow> currentRows = new();

    /// <summary>전용 UI로 편집하므로 "기타 설정 재정의 (JSON)"에서는 빼는 키.</summary>
    static readonly string[] DedicatedKeys = ["audioSource", "model", "speakerModels"];

    sealed class ProfileItem
    {
        public readonly string? Name;
        public readonly bool Active;
        /// <summary>저장된 오디오 소스가 있는지. 없으면 사이드바에 경고 표시를 한다. 저장할 때 갱신된다.</summary>
        public bool HasAudioSource;
        public ProfileItem(string? name, bool active, bool hasAudioSource = true)
        {
            Name = name; Active = active; HasAudioSource = hasAudioSource;
        }
        public override string ToString() => Name ?? "";
    }

    public ProfileSettingsForm(ITranscriber engine, string baseConfigPath, string profilesDir)
    {
        this.engine = engine;
        this.baseConfigPath = baseConfigPath;
        this.profilesDir = profilesDir;
        BuildUi();
        Theme.Apply(this);
        Theme.StyleButton(saveBtn, Theme.ButtonKind.Primary);
        RefreshList();
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
            return StringValue((node?["audioSource"] as JsonObject)?["type"]) is not null;
        }
        catch { return false; } // 읽을 수 없는 파일도 고쳐야 할 대상으로 표시한다
    }

    bool IsActive(string? name) => name is not null && string.Equals(name, ActiveProfileName, StringComparison.OrdinalIgnoreCase);

    /// <summary>사이드바 항목: 선택된 항목은 강조색 막대, 사용 중인 프로필은 초록 점으로 표시한다.</summary>
    void DrawProfileItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || list.Items[e.Index] is not ProfileItem item) return;
        bool isSelected = (e.State & DrawItemState.Selected) != 0;

        using (var back = new SolidBrush(isSelected ? Theme.Control : Theme.Surface))
            e.Graphics.FillRectangle(back, e.Bounds);
        if (isSelected)
            using (var bar = new SolidBrush(Theme.Accent))
                e.Graphics.FillRectangle(bar, e.Bounds.Left, e.Bounds.Top + 6, 3, e.Bounds.Height - 12);

        if (item.Active)
            using (var dot = new SolidBrush(Theme.Success))
                e.Graphics.FillEllipse(dot, e.Bounds.Left + 12, e.Bounds.Top + e.Bounds.Height / 2 - 4, 8, 8);

        // 오디오 소스가 없는 프로필은 오른쪽에 경고 표시를 붙여, 무엇을 먼저 고쳐야 하는지 보이게 한다.
        int warnWidth = item.HasAudioSource ? 0 : 24;
        if (!item.HasAudioSource)
            TextRenderer.DrawText(e.Graphics, "⚠", list.Font,
                new Rectangle(e.Bounds.Right - 28, e.Bounds.Top, 24, e.Bounds.Height), Color.Goldenrod,
                TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);

        var textBounds = new Rectangle(e.Bounds.Left + 28, e.Bounds.Top, e.Bounds.Width - 32 - warnWidth, e.Bounds.Height);
        TextRenderer.DrawText(e.Graphics, item.Name ?? "", list.Font, textBounds, Theme.Text,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }

    void BuildUi()
    {
        Text = "프로필 편집";
        Width = 820; Height = 660;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false;
        Font = new Font("Segoe UI", 10);
        BackColor = Color.FromArgb(24, 24, 28);
        ForeColor = Color.Gainsboro;

        // --- 사이드바 (좌) ---
        var sidebar = new Panel { Dock = DockStyle.Left, Width = 220, BackColor = Theme.Surface };
        list.Dock = DockStyle.Fill;
        list.BackColor = Theme.Surface;
        list.ForeColor = Theme.Text;
        list.BorderStyle = BorderStyle.None;
        list.IntegralHeight = false;
        list.DrawMode = DrawMode.OwnerDrawFixed;
        list.ItemHeight = 34;
        list.DrawItem += DrawProfileItem;
        list.SelectedIndexChanged += (s, e) => { if (!suppressSelection) OnSelectionChanged(); };

        var sideButtons = new Panel { Dock = DockStyle.Bottom, Height = 40, BackColor = Theme.Surface };
        addBtn.Text = "+ 추가"; addBtn.Width = 100; addBtn.Height = 28; addBtn.Location = new Point(4, 4);
        addBtn.Click += (s, e) => AddNew();
        removeBtn.Text = "- 제거"; removeBtn.Width = 100; removeBtn.Height = 28; removeBtn.Location = new Point(110, 4);
        removeBtn.Click += (s, e) => RemoveSelected();
        sideButtons.Controls.Add(addBtn);
        sideButtons.Controls.Add(removeBtn);

        sidebar.Controls.Add(list);
        sidebar.Controls.Add(sideButtons);

        // --- 상세 패널 (우) ---
        detailTitle.AutoSize = true;
        detailTitle.Margin = new Padding(12, 8, 0, 8);
        detailTitle.Font = new Font("Segoe UI", 14, FontStyle.Bold);

        baseNoteLabel.AutoSize = true;
        baseNoteLabel.Margin = new Padding(12, 0, 12, 0);
        baseNoteLabel.ForeColor = Color.Silver;
        baseNoteLabel.Text = "아직 프로필이 없습니다. 왼쪽 아래 '+ 추가'로 만들어 보세요.\n" +
                             "프로필마다 무엇을 들을지(오디오 소스)를 정해야 받아 적기를 시작할 수 있습니다.";
        baseNoteLabel.MaximumSize = new Size(540, 0);

        // --- 모델 재정의 ---
        overrideModelCheck.Text = "모델 재정의";
        overrideModelCheck.AutoSize = true;
        overrideModelCheck.Location = new Point(12, 4);
        overrideModelCheck.CheckedChanged += (s, e) => modelCombo.Enabled = overrideModelCheck.Checked;

        modelCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        modelCombo.Location = new Point(160, 1);
        modelCombo.Width = 200;

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

        var addSpeakersBtn = new Button { Text = "인식된 화자 추가", Width = 150, Height = 28, Location = new Point(12, 2) };
        addSpeakersBtn.Click += (s, e) => AddRecognizedSpeakers();
        speakerButtonsPanel.Height = 34;
        speakerButtonsPanel.Margin = new Padding(0, 4, 0, 8);
        speakerButtonsPanel.Controls.Add(addSpeakersBtn);

        // 오디오 소스는 프로필마다 반드시 정한다(기본값에서 물려받지 않는다).
        audioSourceTitle.Text = "오디오 소스";
        audioSourceTitle.AutoSize = true;
        audioSourceTitle.Font = new Font("Segoe UI", 10, FontStyle.Bold);
        audioSourceTitle.Location = new Point(12, 6);

        typeCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        typeCombo.Location = new Point(120, 1);
        typeCombo.Width = 320;
        foreach (var n in Config.GetAvailableAudioSourceTypeNames()) typeCombo.Items.Add(n);
        // 항목 값은 설정 파일에 쓰는 type 이름 그대로 두고, 화면에는 알아보기 쉬운 이름을 보여준다.
        typeCombo.FormattingEnabled = true;
        typeCombo.Format += (s, e) =>
        {
            if (e.ListItem is string n && Config.GetAudioSourceInfo(n)?.DisplayName is { } display)
                e.Value = $"{display} ({n})";
        };

        audioSourceDescLabel.AutoSize = true;
        audioSourceDescLabel.Margin = new Padding(12, 0, 12, 8);
        audioSourceDescLabel.MaximumSize = new Size(560, 0);
        audioSourceDescLabel.ForeColor = Color.Silver;
        typeCombo.SelectedIndexChanged += (s, e) =>
        {
            if (suppressEvents) return;
            string? currentType = (currentPatch["audioSource"] as JsonObject)?["type"]?.GetValue<string>();
            bool sameType = typeCombo.SelectedItem is string t && string.Equals(t, currentType, StringComparison.OrdinalIgnoreCase);
            RebuildAudioSourceRows(seedFromPatch: sameType);
            UpdateButtons();
        };

        overrideHeaderPanel.Size = new Size(560, 34);
        overrideHeaderPanel.Margin = new Padding(0, 0, 0, 4);
        overrideHeaderPanel.Controls.Add(audioSourceTitle);
        overrideHeaderPanel.Controls.Add(typeCombo);

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
        otherJsonBox.TextChanged += (s, e) => { if (!suppressEvents) ValidateOtherJson(); };

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

        var body = new Panel { Dock = DockStyle.Fill };
        body.Controls.Add(detailScroll);
        body.Controls.Add(new Panel { Dock = DockStyle.Left, Width = 1, BackColor = Theme.Border });
        body.Controls.Add(sidebar);

        // --- 하단 바 ---
        current.Dock = DockStyle.Top; current.Height = 24;
        current.Padding = new Padding(4, 4, 0, 0); current.ForeColor = Color.Silver;

        hint.Dock = DockStyle.Top; hint.Height = 28;
        hint.Padding = new Padding(4, 2, 4, 0); hint.ForeColor = Color.Goldenrod;

        var buttons = new Panel { Dock = DockStyle.Top, Height = 44 };
        saveBtn.Text = "저장"; saveBtn.Width = 80; saveBtn.Height = 30; saveBtn.Location = new Point(4, 6);
        saveBtn.Click += (s, e) => Save();
        applyBtn.Text = "이 프로필 사용"; applyBtn.Width = 130; applyBtn.Height = 30; applyBtn.Location = new Point(90, 6);
        applyBtn.Click += async (s, e) => await ApplyAsync();
        refreshBtn.Text = "새로고침"; refreshBtn.Width = 80; refreshBtn.Height = 30; refreshBtn.Location = new Point(226, 6);
        refreshBtn.Click += (s, e) => RefreshList();
        closeBtn.Text = "닫기"; closeBtn.Width = 80; closeBtn.Height = 30; closeBtn.Location = new Point(312, 6);
        closeBtn.Click += (s, e) => Close();
        buttons.Controls.Add(saveBtn);
        buttons.Controls.Add(applyBtn);
        buttons.Controls.Add(refreshBtn);
        buttons.Controls.Add(closeBtn);

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 96, BackColor = Theme.Surface, Padding = new Padding(8, 0, 8, 0) };
        bottom.Controls.Add(buttons);
        bottom.Controls.Add(hint);
        bottom.Controls.Add(current);

        Controls.Add(body);
        Controls.Add(new Panel { Dock = DockStyle.Bottom, Height = 1, BackColor = Theme.Border });
        Controls.Add(bottom);
    }

    /// <summary>사이드바 목록을 다시 채운다. reloadDetail이 false면 선택은 유지하되 오른쪽 상세 화면은
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

            current.Text = $"사용 중인 프로필: {ActiveProfileName ?? "없음"}";

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
        // currentRows는 여기서 비우지 않는다 — RebuildAudioSourceRows가 옛 행을 패널에서 빼고 정리한다.
        jsonErrorLabel.Text = "";

        if (selected?.Name is null) return;

        detailTitle.Text = selected.Name;
        string path = ProfilePath(selected.Name);
        currentPatch = File.Exists(path)
            ? (JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? new JsonObject())
            : new JsonObject();

        // 저장된 오디오 소스가 없으면 아무것도 고르지 않은 상태로 두고 경고를 보여준다.
        string? seedType = StringValue((currentPatch["audioSource"] as JsonObject)?["type"]);

        suppressEvents = true;
        try
        {
            typeCombo.SelectedIndex = seedType is null ? -1
                : typeCombo.Items.Cast<string>().ToList().FindIndex(n => n.Equals(seedType, StringComparison.OrdinalIgnoreCase));

            LoadModelOverrides();

            var otherObj = new JsonObject();
            foreach (var kv in currentPatch.ToList())
                if (!DedicatedKeys.Contains(kv.Key))
                    otherObj[kv.Key] = kv.Value?.DeepClone();
            otherJsonBox.Text = otherObj.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        }
        finally { suppressEvents = false; }

        RebuildAudioSourceRows(seedFromPatch: true);
        ValidateOtherJson();
        detailScroll.AutoScrollPosition = Point.Empty; // 다른 프로필을 고른 것이므로 맨 위부터 보여준다
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

    void RebuildAudioSourceRows(bool seedFromPatch)
    {
        var oldRows = currentRows.ToList();
        currentRows.Clear();

        if (typeCombo.SelectedItem is string type)
        {
            JsonObject? seed = seedFromPatch ? currentPatch["audioSource"] as JsonObject : null;

            try
            {
                int rowWidth = Math.Max(detailScroll.ClientSize.Width - 24, 300);
                foreach (var sv in engine.Config.GetSettingValues(new AudioSourceSpec(type, seed)))
                {
                    var row = new AudioSourceSettingRow(sv.Spec, sv.CurrentValue, rowWidth);
                    row.Changed += UpdateButtons;
                    currentRows.Add(row);
                }
            }
            catch (Exception ex)
            {
                jsonErrorLabel.Text = $"오디오 소스 설정을 불러올 수 없습니다: {ex.Message}";
            }
        }

        // 프로필 상세가 이미 그려져 있으면 오디오 소스 부분만 바꿔 끼운다. 패널 전체를 지우고 다시 그리면
        // 깜빡이고 스크롤이 맨 위로 튄다.
        if (contentPanel.Controls.Contains(overrideHeaderPanel))
            ReplaceAudioSourceRows(oldRows);
        else
        {
            foreach (var r in oldRows) r.Dispose();
            RenderContentPanel();
        }
    }

    void ReplaceAudioSourceRows(List<AudioSourceSettingRow> oldRows)
    {
        Point scroll = detailScroll.AutoScrollPosition;
        contentPanel.SuspendLayout();
        try
        {
            foreach (var r in oldRows)
            {
                contentPanel.Controls.Remove(r);
                r.Dispose();
            }

            int index = contentPanel.Controls.IndexOf(overrideHeaderPanel) + 1;

            UpdateAudioSourceDescription();
            if (!contentPanel.Controls.Contains(audioSourceDescLabel)) contentPanel.Controls.Add(audioSourceDescLabel);
            contentPanel.Controls.SetChildIndex(audioSourceDescLabel, index++);

            foreach (var row in currentRows)
            {
                contentPanel.Controls.Add(row);
                contentPanel.Controls.SetChildIndex(row, index++);
            }
        }
        finally { contentPanel.ResumeLayout(); }

        // AutoScrollPosition은 읽을 때 음수, 쓸 때 양수로 다룬다.
        detailScroll.AutoScrollPosition = new Point(-scroll.X, -scroll.Y);
    }

    /// <summary>고른 오디오 소스의 설명을, 고르지 않았으면 먼저 고르라는 경고를 보여준다.</summary>
    void UpdateAudioSourceDescription()
    {
        if (typeCombo.SelectedItem is string type)
        {
            audioSourceDescLabel.Text = Config.GetAudioSourceInfo(type)?.Description ?? "";
            audioSourceDescLabel.ForeColor = Theme.SubText;
        }
        else
        {
            audioSourceDescLabel.Text = "⚠ 오디오 소스를 먼저 고르세요. 무엇을 듣고 받아 적을지 정해야 이 프로필을 사용할 수 있습니다.";
            audioSourceDescLabel.ForeColor = Color.Goldenrod;
        }
    }

    void RenderContentPanel()
    {
        contentPanel.Controls.Clear();
        contentPanel.Controls.Add(detailTitle);

        if (selected?.Name is null)
        {
            contentPanel.Controls.Add(baseNoteLabel);
            return;
        }

        // 오디오 소스가 프로필의 핵심이므로 맨 위에, 모델 관련 설정은 그 아래에 둔다.
        contentPanel.Controls.Add(overrideHeaderPanel);
        UpdateAudioSourceDescription();
        contentPanel.Controls.Add(audioSourceDescLabel);
        foreach (var row in currentRows) contentPanel.Controls.Add(row);
        contentPanel.Controls.Add(modelHeaderPanel);
        contentPanel.Controls.Add(speakerModelLabel);
        contentPanel.Controls.Add(speakerGrid);
        contentPanel.Controls.Add(speakerButtonsPanel);
        contentPanel.Controls.Add(otherJsonLabel);
        contentPanel.Controls.Add(otherJsonBox);
        contentPanel.Controls.Add(jsonErrorLabel);
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
        bool hasSelection = selected?.Name is not null;
        bool jsonOk = !hasSelection || IsOtherJsonValid();
        bool isActive = IsActive(selected?.Name);

        saveBtn.Enabled = hasSelection && jsonOk;
        removeBtn.Enabled = hasSelection && !isActive;

        bool hasAudioSource = typeCombo.SelectedItem is string;
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

    void Save()
    {
        if (selected?.Name is not { } name) return;
        foreach (var row in currentRows) row.CommitPending();
        if (speakerGrid.IsCurrentCellInEditMode) speakerGrid.EndEdit();
        if (!IsOtherJsonValid()) return;

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

        if (typeCombo.SelectedItem is string type)
        {
            // 같은 종류면 디스크의 audioSource를 베이스로 삼는다 — 그래야 다이얼로그가 열려 있는 동안
            // 백그라운드에서 실시간 학습·저장된 값이 유실되지 않는다. 종류가 바뀌었으면 이전 소스의 값(예: 봇 토큰)이
            // 남지 않도록 새로 만든다.
            bool sameType = onDisk["audioSource"] is JsonObject existing
                && string.Equals(StringValue(existing["type"]), type, StringComparison.OrdinalIgnoreCase);
            var asObj = sameType ? onDisk["audioSource"]!.DeepClone().AsObject() : new JsonObject();
            asObj["type"] = type;
            foreach (var row in currentRows)
            {
                // 기본값으로 되돌린 항목은 파일에서 지운다(남겨 두면 "기본값으로 재설정"이 저장되지 않는다).
                if (Equals(row.Value, row.Spec.DefaultValue)) asObj.Remove(row.Spec.Name);
                else asObj[row.Spec.Name] = JsonSerializer.SerializeToNode(row.Value, row.Spec.ClrType, Config.AudioSourceValueJsonOptions);
            }
            combined["audioSource"] = asObj;
        }

        var otherParsed = JsonNode.Parse(string.IsNullOrWhiteSpace(otherJsonBox.Text) ? "{}" : otherJsonBox.Text) as JsonObject ?? new JsonObject();
        foreach (var kv in otherParsed.ToList())
            if (!DedicatedKeys.Contains(kv.Key))
                combined[kv.Key] = kv.Value?.DeepClone();

        Directory.CreateDirectory(profilesDir);
        File.WriteAllText(path, combined.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        // 화면은 이미 저장한 내용 그대로이므로 다시 읽어 그리지 않는다(깜빡임·스크롤 초기화 방지).
        currentPatch = combined.DeepClone().AsObject();
        hint.Text = $"'{name}' 저장됨";
        if (selected is not null)
        {
            selected.HasAudioSource = combined["audioSource"] is not null;
            list.Invalidate(); // 사이드바의 경고 표시 갱신
        }

        // 지금 저장한 프로필이 활성 프로필이면, 재시작 없이도 실행 중인 오디오 소스가
        // 고친 값(예: knownUsers 이름)을 바로 반영하도록 알린다.
        if (combined["audioSource"] is JsonObject savedAudioSource &&
            string.Equals(name, engine.Config.EffectiveProfileName, StringComparison.OrdinalIgnoreCase))
            engine.ApplyRuntimeOptions(savedAudioSource);
    }

    async Task ApplyAsync()
    {
        if (engine.IsRunning || selected?.Name is not { } profileName) return;
        applyBtn.Enabled = false;
        try
        {
            Save();

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
        selected = null;
        RefreshList();
    }
}
