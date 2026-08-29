using System.Text.Json;
using System.Text.Json.Nodes;
using HearingAid.Core;
using HearingAid.Core.Audio;

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
    readonly Panel overrideHeaderPanel = new();
    readonly CheckBox overrideAudioSourceCheck = new();
    readonly ComboBox typeCombo = new();
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
    readonly List<AudioSourceSettingRow> currentRows = new();

    sealed class ProfileItem
    {
        public readonly string? Name; // null => "(기본값)"
        public readonly bool Active;
        public ProfileItem(string? name, bool active) { Name = name; Active = active; }
        public override string ToString() => $"{(Active ? "● " : "   ")}{Name ?? "(기본값)"}";
    }

    public ProfileSettingsForm(ITranscriber engine, string baseConfigPath, string profilesDir)
    {
        this.engine = engine;
        this.baseConfigPath = baseConfigPath;
        this.profilesDir = profilesDir;
        BuildUi();
        RefreshList();
    }

    string ProfilePath(string name) => Path.Combine(profilesDir, name + ".json");

    void BuildUi()
    {
        Text = "프로필 설정";
        Width = 820; Height = 660;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false;
        Font = new Font("Segoe UI", 10);
        BackColor = Color.FromArgb(24, 24, 28);
        ForeColor = Color.Gainsboro;

        // --- 사이드바 (좌) ---
        var sidebar = new Panel { Dock = DockStyle.Left, Width = 220 };
        list.Dock = DockStyle.Fill;
        list.BackColor = Color.FromArgb(24, 24, 28);
        list.ForeColor = Color.Gainsboro;
        list.BorderStyle = BorderStyle.None;
        list.IntegralHeight = false;
        list.SelectedIndexChanged += (s, e) => OnSelectionChanged();

        var sideButtons = new Panel { Dock = DockStyle.Bottom, Height = 36 };
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
        detailTitle.Font = new Font("Segoe UI", 11, FontStyle.Bold);

        baseNoteLabel.AutoSize = true;
        baseNoteLabel.Margin = new Padding(12, 0, 12, 0);
        baseNoteLabel.ForeColor = Color.Silver;
        baseNoteLabel.Text = "(기본값)은 appsettings.json 자체이며 여기서 편집할 수 없습니다. 새 프로필을 만들어 재정의하세요.";
        baseNoteLabel.MaximumSize = new Size(540, 0);

        overrideAudioSourceCheck.Text = "오디오 소스 재정의";
        overrideAudioSourceCheck.AutoSize = true;
        overrideAudioSourceCheck.Location = new Point(12, 4);
        overrideAudioSourceCheck.CheckedChanged += (s, e) =>
        {
            if (suppressEvents) return;
            typeCombo.Enabled = overrideAudioSourceCheck.Checked;
            RebuildAudioSourceRows(seedFromPatch: true);
            UpdateButtons();
        };

        typeCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        typeCombo.Location = new Point(160, 1);
        typeCombo.Width = 200;
        foreach (var n in Config.GetAvailableAudioSourceTypeNames()) typeCombo.Items.Add(n);
        typeCombo.SelectedIndexChanged += (s, e) =>
        {
            if (suppressEvents) return;
            string? currentType = (currentPatch["audioSource"] as JsonObject)?["type"]?.GetValue<string>();
            bool sameType = typeCombo.SelectedItem is string t && string.Equals(t, currentType, StringComparison.OrdinalIgnoreCase);
            RebuildAudioSourceRows(seedFromPatch: sameType);
            UpdateButtons();
        };

        overrideHeaderPanel.Height = 32;
        overrideHeaderPanel.Margin = new Padding(0, 0, 0, 4);
        overrideHeaderPanel.Controls.Add(overrideAudioSourceCheck);
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
        body.Controls.Add(sidebar);

        // --- 하단 바 ---
        current.Dock = DockStyle.Top; current.Height = 24;
        current.Padding = new Padding(4, 4, 0, 0); current.ForeColor = Color.Silver;

        hint.Dock = DockStyle.Top; hint.Height = 28;
        hint.Padding = new Padding(4, 2, 4, 0); hint.ForeColor = Color.Goldenrod;

        var buttons = new Panel { Dock = DockStyle.Top, Height = 44 };
        saveBtn.Text = "저장"; saveBtn.Width = 80; saveBtn.Height = 30; saveBtn.Location = new Point(4, 6);
        saveBtn.Click += (s, e) => Save();
        applyBtn.Text = "적용"; applyBtn.Width = 80; applyBtn.Height = 30; applyBtn.Location = new Point(90, 6);
        applyBtn.Click += async (s, e) => await ApplyAsync();
        refreshBtn.Text = "새로고침"; refreshBtn.Width = 80; refreshBtn.Height = 30; refreshBtn.Location = new Point(176, 6);
        refreshBtn.Click += (s, e) => RefreshList();
        closeBtn.Text = "닫기"; closeBtn.Width = 80; closeBtn.Height = 30; closeBtn.Location = new Point(262, 6);
        closeBtn.Click += (s, e) => Close();
        buttons.Controls.Add(saveBtn);
        buttons.Controls.Add(applyBtn);
        buttons.Controls.Add(refreshBtn);
        buttons.Controls.Add(closeBtn);

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 96 };
        bottom.Controls.Add(buttons);
        bottom.Controls.Add(hint);
        bottom.Controls.Add(current);

        Controls.Add(body);
        Controls.Add(bottom);
    }

    void RefreshList()
    {
        string? keepName = selected?.Name;
        bool hadSelection = selected is not null;

        list.Items.Clear();
        list.Items.Add(new ProfileItem(null, engine.Config.EffectiveProfileName is null));
        foreach (var n in Config.ListProfileNames(profilesDir))
            list.Items.Add(new ProfileItem(n, string.Equals(n, engine.Config.EffectiveProfileName, StringComparison.OrdinalIgnoreCase)));

        current.Text = $"현재 프로필: {engine.Config.EffectiveProfileName ?? "(기본값)"}";

        int idx = -1;
        if (hadSelection)
            idx = list.Items.Cast<ProfileItem>().ToList().FindIndex(i => string.Equals(i.Name, keepName, StringComparison.OrdinalIgnoreCase));

        int newIndex = idx >= 0 ? idx : 0;
        if (list.SelectedIndex == newIndex) OnSelectionChanged();
        else list.SelectedIndex = newIndex;
    }

    void OnSelectionChanged()
    {
        if (list.SelectedItem is not ProfileItem item)
        {
            selected = null;
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
        currentRows.Clear();
        jsonErrorLabel.Text = "";

        if (selected!.Name is null)
        {
            detailTitle.Text = "(기본값)";
            currentPatch = new JsonObject();
            RenderContentPanel();
            return;
        }

        detailTitle.Text = selected.Name;
        string path = ProfilePath(selected.Name);
        currentPatch = File.Exists(path)
            ? (JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? new JsonObject())
            : new JsonObject();

        bool hasOverride = currentPatch.ContainsKey("audioSource");
        JsonObject? seed = currentPatch["audioSource"] as JsonObject ?? engine.Config.AudioSource;
        string? seedType = seed is not null && seed.TryGetPropertyValue("type", out var t) && t is not null
            ? t.GetValue<string>()
            : (typeCombo.Items.Count > 0 ? (string)typeCombo.Items[0]! : null);

        suppressEvents = true;
        try
        {
            overrideAudioSourceCheck.Checked = hasOverride;
            typeCombo.Enabled = hasOverride;
            typeCombo.SelectedItem = seedType;

            var otherObj = new JsonObject();
            foreach (var kv in currentPatch.ToList())
                if (kv.Key != "audioSource")
                    otherObj[kv.Key] = kv.Value?.DeepClone();
            otherJsonBox.Text = otherObj.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        }
        finally { suppressEvents = false; }

        RebuildAudioSourceRows(seedFromPatch: true);
        ValidateOtherJson();
    }

    void RebuildAudioSourceRows(bool seedFromPatch)
    {
        foreach (var r in currentRows) contentPanel.Controls.Remove(r);
        currentRows.Clear();

        if (overrideAudioSourceCheck.Checked && typeCombo.SelectedItem is string type)
        {
            JsonObject? seed = seedFromPatch
                ? (currentPatch["audioSource"] as JsonObject ?? engine.Config.AudioSource)
                : null;

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

        RenderContentPanel();
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

        contentPanel.Controls.Add(overrideHeaderPanel);
        foreach (var row in currentRows) contentPanel.Controls.Add(row);
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
        bool isBase = selected?.Name is null;
        bool jsonOk = isBase || IsOtherJsonValid();
        bool isActive = selected is not null &&
            string.Equals(selected.Name, engine.Config.EffectiveProfileName, StringComparison.OrdinalIgnoreCase);

        saveBtn.Enabled = selected is not null && !isBase && jsonOk;
        removeBtn.Enabled = selected is not null && !isBase && !isActive;

        if (engine.IsRunning)
        {
            applyBtn.Enabled = false;
            hint.Text = "캡처를 정지한 뒤 프로필을 전환할 수 있습니다.";
        }
        else
        {
            applyBtn.Enabled = selected is not null && jsonOk;
            hint.Text = "";
        }
    }

    void Save()
    {
        if (selected?.Name is not { } name) return;
        foreach (var row in currentRows) row.CommitPending();
        if (!IsOtherJsonValid()) return;

        string path = ProfilePath(name);
        var onDisk = File.Exists(path)
            ? (JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? new JsonObject())
            : new JsonObject();

        var combined = new JsonObject();
        if (overrideAudioSourceCheck.Checked && typeCombo.SelectedItem is string type)
        {
            // 디스크의 audioSource를 베이스로 삼는다 — 그래야 다이얼로그가 열려 있는 동안
            // 백그라운드에서 실시간 학습·저장된 knownUsers 같은 값이 유실되지 않는다.
            var asObj = onDisk["audioSource"] is JsonObject existing ? existing.DeepClone().AsObject() : new JsonObject();
            asObj["type"] = type;
            foreach (var row in currentRows)
                if (!Equals(row.Value, row.Spec.DefaultValue))
                    asObj[row.Spec.Name] = JsonSerializer.SerializeToNode(row.Value, row.Spec.ClrType, Config.AudioSourceValueJsonOptions);
            combined["audioSource"] = asObj;
        }

        var otherParsed = JsonNode.Parse(string.IsNullOrWhiteSpace(otherJsonBox.Text) ? "{}" : otherJsonBox.Text) as JsonObject ?? new JsonObject();
        foreach (var kv in otherParsed.ToList())
            if (kv.Key != "audioSource")
                combined[kv.Key] = kv.Value?.DeepClone();

        Directory.CreateDirectory(profilesDir);
        File.WriteAllText(path, combined.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        RefreshList();

        // 지금 저장한 프로필이 활성 프로필이면, 재시작 없이도 실행 중인 오디오 소스가
        // 고친 값(예: knownUsers 이름)을 바로 반영하도록 알린다.
        if (combined["audioSource"] is JsonObject savedAudioSource &&
            string.Equals(name, engine.Config.EffectiveProfileName, StringComparison.OrdinalIgnoreCase))
            engine.ApplyRuntimeOptions(savedAudioSource);
    }

    async Task ApplyAsync()
    {
        if (engine.IsRunning) return;
        applyBtn.Enabled = false;
        try
        {
            if (selected?.Name is not null) Save();
            string? profileName = selected?.Name;

            NewEngine?.Dispose(); // 이전에 이 폼에서 만들었지만 MainForm에 넘기지 못한 engine(있다면) 정리
            Config.SetActiveProfile(baseConfigPath, profileName); // Build보다 먼저: null(기본값)일 때도 정확히 반영되도록
            NewEngine = Config.BuildEngine(baseConfigPath, profilesDir, profileName);

            RefreshList();
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
        if (string.Equals(name, engine.Config.EffectiveProfileName, StringComparison.OrdinalIgnoreCase))
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
