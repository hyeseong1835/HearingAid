using HearingAid.Core.Stt;
using HearingAid.Core.Update;

namespace HearingAid;

/// <summary>새 버전 정보와 릴리스 노트를 보여주고, 동의하면 내려받아 교체 모드로 넘긴다.
/// 교체가 시작되면 ApplierLaunched가 true가 되고, MainForm은 곧바로 앱을 종료해야 한다.</summary>
public class UpdateDialog : Form
{
    readonly ReleaseInfo release;
    readonly bool engineRunning;

    readonly Button updateBtn = new();
    readonly Button closeBtn = new();
    readonly ProgressBar progress = new();
    readonly Label progressLabel = new();

    CancellationTokenSource? cts;

    public bool ApplierLaunched { get; private set; }

    public UpdateDialog(ReleaseInfo release, bool engineRunning)
    {
        this.release = release;
        this.engineRunning = engineRunning;
        BuildUi();
        Theme.Apply(this);
        Theme.StyleButton(updateBtn, Theme.ButtonKind.Primary);
    }

    void BuildUi()
    {
        Text = "업데이트";
        Width = 560; Height = 440;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false;
        Font = new Font("Segoe UI", 10);
        BackColor = Color.FromArgb(24, 24, 28);
        ForeColor = Color.Gainsboro;

        var header = new Label
        {
            Dock = DockStyle.Top, Height = 52, Padding = new Padding(8, 8, 8, 0),
            Text = $"{(release.IsDevBuild ? "새 개발 버전" : "새 버전")}이 있습니다: v{AppUpdater.Display(release.Version)}  " +
                   $"(현재 v{AppUpdater.Display(AppUpdater.CurrentVersion)})\n" +
                   $"다운로드 크기 {ModelStore.FormatSize(release.ZipSize)}. 설정·프로필·모델은 그대로 유지됩니다.",
        };

        var notes = new TextBox
        {
            Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
            BackColor = Color.FromArgb(30, 30, 34), ForeColor = Color.Gainsboro, BorderStyle = BorderStyle.None,
            Text = string.IsNullOrWhiteSpace(release.Notes) ? "(릴리스 노트 없음)" : release.Notes.Replace("\r\n", "\n").Replace("\n", "\r\n"),
        };

        progress.Dock = DockStyle.Top; progress.Height = 18; progress.Visible = false;
        progressLabel.Dock = DockStyle.Top; progressLabel.Height = 24;
        progressLabel.Padding = new Padding(4, 4, 0, 0); progressLabel.ForeColor = Color.Silver;
        progressLabel.Text = engineRunning ? "업데이트하면 캡처가 중지되고 앱이 다시 시작됩니다." : "업데이트하면 앱이 다시 시작됩니다.";

        var buttons = new Panel { Dock = DockStyle.Top, Height = 44 };
        updateBtn.Text = "업데이트 후 재시작"; updateBtn.Width = 160; updateBtn.Height = 30; updateBtn.Location = new Point(4, 6);
        updateBtn.Click += async (s, e) => await UpdateAsync();
        closeBtn.Text = "닫기"; closeBtn.Width = 80; closeBtn.Height = 30; closeBtn.Location = new Point(170, 6);
        closeBtn.Click += (s, e) => Close();
        buttons.Controls.Add(updateBtn);
        buttons.Controls.Add(closeBtn);

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 90, Padding = new Padding(4, 4, 4, 0) };
        bottom.Controls.Add(buttons);
        bottom.Controls.Add(progressLabel);
        bottom.Controls.Add(progress);

        Controls.Add(notes);
        Controls.Add(header);
        Controls.Add(bottom);
    }

    async Task UpdateAsync()
    {
        updateBtn.Enabled = false;
        closeBtn.Text = "취소";
        progress.Visible = true;
        cts = new CancellationTokenSource();

        var reporter = new Progress<(long Done, long Total)>(p =>
        {
            if (IsDisposed) return;
            progress.Value = p.Total > 0 ? (int)Math.Min(100, p.Done * 100 / p.Total) : 0;
            progressLabel.Text = $"다운로드 중  {ModelStore.FormatSize(p.Done)} / {ModelStore.FormatSize(p.Total)}";
        });

        try
        {
            string stageDir = await AppUpdater.DownloadAndStageAsync(release, reporter, cts.Token);
            progressLabel.Text = "업데이트를 적용하기 위해 다시 시작합니다...";
            AppUpdater.LaunchApplier(stageDir);
            ApplierLaunched = true;
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (OperationCanceledException)
        {
            if (!IsDisposed) progressLabel.Text = "업데이트를 취소했습니다.";
        }
        catch (Exception ex)
        {
            if (!IsDisposed) progressLabel.Text = $"업데이트 실패: {ex.Message}";
        }
        finally
        {
            cts.Dispose();
            cts = null;
            if (!IsDisposed)
            {
                updateBtn.Enabled = !ApplierLaunched;
                closeBtn.Text = "닫기";
                progress.Visible = false;
            }
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        cts?.Cancel();
        base.OnFormClosing(e);
    }
}
