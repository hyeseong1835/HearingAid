using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using HearingAid.Core;
using HearingAid.Core.Audio;

namespace HearingAid;

/// <summary>프로필 편집 화면에서 오디오 소스 하나를 편집하는 묶음: 머리글(번호·종류·제거) + 별명 + 설명 + 설정 행들.
/// 프로필은 오디오 소스를 여러 개 가질 수 있어, 이 컨트롤을 소스 수만큼 위아래로 쌓는다.</summary>
class AudioSourceEditor : FlowLayoutPanel
{
    readonly Config config;
    readonly int rowWidth;

    readonly Panel header = new();
    readonly Label title = new();
    readonly ComboBox typeCombo = new();
    readonly Button removeBtn = new();
    readonly Panel aliasPanel = new();
    readonly TextBox aliasBox = new();
    readonly Label descLabel = new();
    readonly Label errorLabel = new();
    readonly List<AudioSourceSettingRow> rows = new();

    /// <summary>불러온 파일에서 이 소스가 몇 번째였는지. 저장할 때 디스크의 같은 원소를 바탕으로 삼는 데 쓴다
    /// (실행 중에 소스가 스스로 학습해 저장한 값을 잃지 않도록). 새로 추가한 소스면 null.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] // 디자이너가 저장할 값이 아니다(WFO1000)
    public int? OriginIndex { get; set; }

    /// <summary>불러온 원본. 종류를 바꿨다가 되돌리면 원래 값이 다시 보이도록 남겨 둔다.</summary>
    readonly JsonObject? seed;

    int index;

    public event Action? Changed;
    /// <summary>종류나 별명을 바꿨을 때. 탭 이름을 갱신하는 데 쓴다(설정 값이 바뀔 때마다 탭을 다시 그리지 않도록 따로 둔다).</summary>
    public event Action? TitleChanged;
    public event Action<AudioSourceEditor>? RemoveRequested;

    public string? SelectedType => typeCombo.SelectedItem as string;

    /// <summary>탭·목록에 보일 이름. 별명을 비워 두면 종류의 시스템 이름(예: WasapiLoopback)이다.</summary>
    public string Alias => !string.IsNullOrWhiteSpace(aliasBox.Text) ? aliasBox.Text.Trim() : SelectedType ?? "";

    public AudioSourceEditor(Config config, JsonObject? seed, int? originIndex, int rowWidth)
    {
        this.config = config;
        this.seed = seed;
        this.rowWidth = rowWidth;
        OriginIndex = originIndex;

        FlowDirection = FlowDirection.TopDown;
        WrapContents = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Margin = new Padding(0, 0, 0, 12);

        title.AutoSize = true;
        title.Font = new Font("Segoe UI", 10, FontStyle.Bold);
        title.Location = new Point(12, 9);

        typeCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        typeCombo.Location = new Point(130, 4);
        typeCombo.Width = 300;
        foreach (var n in Config.GetAvailableAudioSourceTypeNames()) typeCombo.Items.Add(n);
        // 항목 값은 설정 파일에 쓰는 type 이름 그대로 두고, 화면에는 알아보기 쉬운 이름을 보여준다.
        typeCombo.FormattingEnabled = true;
        typeCombo.Format += (s, e) =>
        {
            if (e.ListItem is string n && Config.GetAudioSourceInfo(n)?.DisplayName is { } display)
                e.Value = $"{display} ({n})";
        };

        removeBtn.Text = "✕ 제거";
        removeBtn.Size = new Size(84, 28);
        removeBtn.Location = new Point(rowWidth - 96, 4);
        removeBtn.Click += (s, e) => RemoveRequested?.Invoke(this);

        header.Size = new Size(rowWidth, 36);
        header.Margin = new Padding(0, 0, 0, 4);
        header.BackColor = Theme.Surface;
        header.Controls.AddRange([title, typeCombo, removeBtn]);

        // 별명: 목록의 소스 칸과 내보내기에 쓰인다. 비워 두면 종류 이름이 회색 안내 글자로 보인다.
        var aliasLabel = new Label { Text = "별명", AutoSize = true, Location = new Point(12, 8) };
        aliasBox.Location = new Point(130, 4);
        aliasBox.Width = 300;
        aliasBox.Text = seed?[Config.AliasKey] is JsonValue av && av.TryGetValue(out string? alias) ? alias : "";
        aliasBox.TextChanged += (s, e) => { TitleChanged?.Invoke(); Changed?.Invoke(); };
        var aliasHint = new Label { Text = "목록·내보내기에 표시", AutoSize = true, ForeColor = Theme.SubText, Location = new Point(440, 8) };
        aliasPanel.Size = new Size(rowWidth, 34);
        aliasPanel.Margin = new Padding(0, 0, 0, 2);
        aliasPanel.Controls.AddRange([aliasLabel, aliasBox, aliasHint]);

        descLabel.AutoSize = true;
        descLabel.Margin = new Padding(12, 2, 12, 8);
        descLabel.MaximumSize = new Size(rowWidth - 24, 0);

        errorLabel.AutoSize = true;
        errorLabel.Margin = new Padding(12, 0, 12, 4);
        errorLabel.ForeColor = Color.IndianRed;

        Controls.Add(header);
        Controls.Add(aliasPanel);
        Controls.Add(descLabel);

        string? seedType = Config.GetAudioSourceType(seed);
        typeCombo.SelectedIndex = seedType is null ? -1
            : typeCombo.Items.Cast<string>().ToList().FindIndex(n => n.Equals(seedType, StringComparison.OrdinalIgnoreCase));
        RebuildRows();
        aliasBox.PlaceholderText = SelectedType ?? "";
        typeCombo.SelectedIndexChanged += (s, e) =>
        {
            aliasBox.PlaceholderText = SelectedType ?? "";
            RebuildRows();
            TitleChanged?.Invoke();
            Changed?.Invoke();
        };
    }

    /// <summary>번호(0부터)와 제거 버튼 표시 여부를 정한다. 소스가 하나뿐이면 제거 대신 종류만 바꾸게 한다.</summary>
    public void SetIndex(int index, bool removable)
    {
        this.index = index;
        title.Text = $"오디오 소스 {index + 1}";
        removeBtn.Visible = removable;
        UpdateDescription();
    }

    void RebuildRows()
    {
        SuspendLayout();
        try
        {
            foreach (var r in rows)
            {
                Controls.Remove(r);
                r.Dispose();
            }
            rows.Clear();
            Controls.Remove(errorLabel);
            errorLabel.Text = "";

            if (SelectedType is { } type)
            {
                // 불러온 소스와 같은 종류면 그 값으로, 종류를 바꿨으면 기본값으로 시작한다.
                bool sameType = string.Equals(Config.GetAudioSourceType(seed), type, StringComparison.OrdinalIgnoreCase);
                try
                {
                    foreach (var sv in config.GetSettingValues(new AudioSourceSpec(type, sameType ? seed : null)))
                    {
                        var row = new AudioSourceSettingRow(sv.Spec, sv.CurrentValue, rowWidth);
                        row.Changed += () => Changed?.Invoke();
                        rows.Add(row);
                        Controls.Add(row);
                    }
                }
                catch (Exception ex)
                {
                    errorLabel.Text = $"오디오 소스 설정을 불러올 수 없습니다: {ex.Message}";
                    Controls.Add(errorLabel);
                }
            }
            UpdateDescription();
        }
        finally { ResumeLayout(); }
    }

    /// <summary>고른 오디오 소스의 설명을, 고르지 않았으면 먼저 고르라는 경고를 보여준다.</summary>
    void UpdateDescription()
    {
        if (SelectedType is { } type)
        {
            descLabel.Text = Config.GetAudioSourceInfo(type)?.Description ?? "";
            descLabel.ForeColor = Theme.SubText;
        }
        else
        {
            descLabel.Text = index == 0
                ? "⚠ 오디오 소스를 먼저 고르세요. 무엇을 듣고 받아 적을지 정해야 이 프로필을 사용할 수 있습니다."
                : "⚠ 종류를 고르세요. 고르지 않은 소스는 저장할 때 빠집니다.";
            descLabel.ForeColor = Color.Goldenrod;
        }
    }

    /// <summary>DataGridView 등 편집 중인 값을 확정한다. 저장 직전에 부른다.</summary>
    public void CommitPending()
    {
        foreach (var row in rows) row.CommitPending();
    }

    /// <summary>저장할 JSON 객체를 만든다. 종류를 고르지 않았으면 null.
    /// onDisk는 디스크에 지금 저장된, 이 소스의 원래 자리 원소다. 같은 종류면 그것을 바탕으로 삼아 다이얼로그가
    /// 열려 있는 동안 백그라운드에서 학습·저장된 값을 잃지 않게 하고, 종류가 바뀌었으면 이전 소스의 값(예: 봇 토큰)이
    /// 남지 않도록 새로 만든다.</summary>
    public JsonObject? BuildJson(JsonObject? onDisk)
    {
        if (SelectedType is not { } type) return null;

        bool sameType = string.Equals(Config.GetAudioSourceType(onDisk), type, StringComparison.OrdinalIgnoreCase);
        var obj = sameType ? onDisk!.DeepClone().AsObject() : new JsonObject();
        obj["type"] = type;
        // 기본값(종류 이름)과 같거나 비워 둔 별명은 저장하지 않는다.
        string alias = aliasBox.Text.Trim();
        if (alias.Length == 0 || alias == type) obj.Remove(Config.AliasKey);
        else obj[Config.AliasKey] = alias;
        foreach (var row in rows)
        {
            // 기본값으로 되돌린 항목은 파일에서 지운다(남겨 두면 "기본값으로 재설정"이 저장되지 않는다).
            if (Equals(row.Value, row.Spec.DefaultValue)) obj.Remove(row.Spec.Name);
            else obj[row.Spec.Name] = JsonSerializer.SerializeToNode(row.Value, row.Spec.ClrType, Config.AudioSourceValueJsonOptions);
        }
        return obj;
    }
}
