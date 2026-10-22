using System.Text.Json.Nodes;
using HearingAid.Core;
using HearingAid.Core.Update;

namespace HearingAid;

/// <summary>설정 창의 '업데이트' 페이지(appsettings.json). VSCode 설정처럼 바꾸는 즉시 저장한다.</summary>
public class UpdateSettingsPage : UserControl
{
    readonly string baseConfigPath;
    readonly bool initialLatest;
    bool loading = true;

    readonly CheckBox checkOnStart = new();
    readonly RadioButton stableRadio = new();
    readonly RadioButton latestRadio = new();

    /// <summary>창을 연 뒤 업데이트 채널이 바뀌었는지. MainForm이 새 채널로 다시 확인하는 데 쓴다.</summary>
    public bool ChannelChanged => latestRadio.Checked != initialLatest;

    public UpdateSettingsPage(string baseConfigPath)
    {
        this.baseConfigPath = baseConfigPath;
        BuildUi();

        Config cfg = Config.LoadBase(baseConfigPath);
        initialLatest = cfg.IncludeDevBuilds;
        checkOnStart.Checked = cfg.CheckForUpdates;
        (initialLatest ? latestRadio : stableRadio).Checked = true;
        loading = false;
    }

    void BuildUi()
    {
        Dock = DockStyle.Fill;
        Font = Theme.Font;

        var content = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false,
            Padding = new Padding(20, 16, 20, 0), AutoScroll = true,
        };

        var title = new Label { Text = "업데이트", AutoSize = true, Font = new Font("Segoe UI", 12, FontStyle.Bold), Margin = new Padding(0, 0, 0, 2) };
        var version = new Label
        {
            Text = $"현재 버전: v{AppUpdater.Display(AppUpdater.CurrentVersion)}",
            AutoSize = true, ForeColor = Theme.SubText, Margin = new Padding(0, 0, 0, 14),
        };

        checkOnStart.Text = "시작할 때 새 버전 확인";
        checkOnStart.AutoSize = true;
        checkOnStart.Margin = new Padding(0, 0, 0, 16);
        checkOnStart.CheckedChanged += (s, e) => Save();

        var channelLabel = new Label { Text = "업데이트 채널", AutoSize = true, Font = new Font(Theme.Font, FontStyle.Bold), Margin = new Padding(0, 0, 0, 6) };

        stableRadio.Text = "안정 릴리스 (권장)";
        latestRadio.Text = "최신 개발 버전";
        foreach (var r in new[] { stableRadio, latestRadio })
        {
            r.AutoSize = true;
            r.Margin = new Padding(0, 4, 0, 0);
            r.CheckedChanged += (s, e) => { if (((RadioButton)s!).Checked) Save(); };
        }

        content.Controls.AddRange([
            title, version, checkOnStart, channelLabel,
            stableRadio, Hint("검증을 거쳐 정식으로 출시된 버전만 받습니다."),
            latestRadio, Hint("기능이 main에 합쳐질 때마다 자동으로 만들어지는 버전까지 받습니다. " +
                              "새 기능과 수정을 먼저 쓸 수 있지만, 아직 충분히 확인되지 않아 문제가 있을 수 있습니다."),
            Hint("개발 버전을 쓰다가 안정 릴리스로 돌아가면, 지금 버전보다 새로운 정식 릴리스가 나올 때 업데이트됩니다.", top: 12),
            Hint("바꾸는 즉시 저장됩니다.", top: 12),
        ]);
        Controls.Add(content);
    }

    static Label Hint(string text, int top = 0) => new()
    {
        Text = text, AutoSize = true, MaximumSize = new Size(560, 0), ForeColor = Theme.SubText,
        Margin = new Padding(20, top, 0, 6),
    };

    void Save()
    {
        if (loading) return;
        Config.SetBaseValue(baseConfigPath, "checkForUpdates", JsonValue.Create(checkOnStart.Checked));
        // 기본값(안정)은 파일에 적지 않는다.
        Config.SetBaseValue(baseConfigPath, "updateChannel", latestRadio.Checked ? JsonValue.Create(Config.LatestChannel) : null);
    }
}
