using System.Text.Json.Nodes;
using HearingAid.Core;
using HearingAid.Core.Update;

namespace HearingAid;

/// <summary>프로필과 상관없는 앱 전체 설정(appsettings.json). 지금은 업데이트 관련 설정만 있다.</summary>
public class AppSettingsDialog : Form
{
    readonly string baseConfigPath;

    readonly CheckBox checkOnStart = new();
    readonly RadioButton stableRadio = new();
    readonly RadioButton latestRadio = new();

    /// <summary>업데이트 채널을 바꿨는지. MainForm이 새 채널로 다시 확인하는 데 쓴다.</summary>
    public bool ChannelChanged { get; private set; }

    public AppSettingsDialog(string baseConfigPath)
    {
        this.baseConfigPath = baseConfigPath;
        BuildUi();
        Theme.Apply(this);

        Config cfg = Config.LoadBase(baseConfigPath);
        checkOnStart.Checked = cfg.CheckForUpdates;
        (cfg.IncludeDevBuilds ? latestRadio : stableRadio).Checked = true;
    }

    void BuildUi()
    {
        Text = "설정";
        Width = 560; Height = 440;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false;
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

        var channelLabel = new Label { Text = "업데이트 채널", AutoSize = true, Font = new Font(Theme.Font, FontStyle.Bold), Margin = new Padding(0, 0, 0, 6) };

        stableRadio.Text = "안정 릴리스 (권장)";
        latestRadio.Text = "최신 개발 버전";
        foreach (var r in new[] { stableRadio, latestRadio })
        {
            r.AutoSize = true;
            r.Margin = new Padding(0, 4, 0, 0);
        }

        content.Controls.AddRange([
            title, version, checkOnStart, channelLabel,
            stableRadio, Hint("검증을 거쳐 정식으로 출시된 버전만 받습니다."),
            latestRadio, Hint("기능이 main에 합쳐질 때마다 자동으로 만들어지는 버전까지 받습니다. " +
                              "새 기능과 수정을 먼저 쓸 수 있지만, 아직 충분히 확인되지 않아 문제가 있을 수 있습니다."),
            Hint("개발 버전을 쓰다가 안정 릴리스로 돌아가면, 지금 버전보다 새로운 정식 릴리스가 나올 때 업데이트됩니다.", top: 12),
        ]);

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 52, BackColor = Theme.Surface };
        var saveBtn = new Button { Text = "저장", Size = new Size(90, 32), Location = new Point(16, 10) };
        saveBtn.Click += (s, e) => { Save(); DialogResult = DialogResult.OK; Close(); };
        var cancelBtn = new Button { Text = "취소", Size = new Size(80, 32), Location = new Point(112, 10) };
        cancelBtn.Click += (s, e) => Close();
        bottom.Controls.AddRange([saveBtn, cancelBtn]);
        Theme.StyleButton(saveBtn, Theme.ButtonKind.Primary);
        AcceptButton = saveBtn;
        CancelButton = cancelBtn;

        Controls.Add(content);
        Controls.Add(new Panel { Dock = DockStyle.Bottom, Height = 1, BackColor = Theme.Border });
        Controls.Add(bottom);
    }

    static Label Hint(string text, int top = 0) => new()
    {
        Text = text, AutoSize = true, MaximumSize = new Size(480, 0), ForeColor = Theme.SubText,
        Margin = new Padding(20, top, 0, 6),
    };

    void Save()
    {
        bool wasLatest = Config.LoadBase(baseConfigPath).IncludeDevBuilds;
        Config.SetBaseValue(baseConfigPath, "checkForUpdates", JsonValue.Create(checkOnStart.Checked));
        // 기본값(안정)은 파일에 적지 않는다.
        Config.SetBaseValue(baseConfigPath, "updateChannel", latestRadio.Checked ? JsonValue.Create(Config.LatestChannel) : null);
        ChannelChanged = wasLatest != latestRadio.Checked;
    }
}
