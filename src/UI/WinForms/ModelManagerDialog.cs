using HearingAid.Core.Stt;

namespace HearingAid;

/// <summary>첫 실행(설치된 모델 없음) 때 메인 창보다 먼저 띄우는 모델 선택 창. 내용은 설정 창의 '모델' 페이지와 같고,
/// 이 창을 닫아야 메인 창이 열리므로 아래에 "HearingAid 시작" 버튼을 둔다.</summary>
public class ModelManagerDialog : Form
{
    readonly ModelManagerPage page;
    readonly Button startBtn = new();

    public ModelManagerDialog(string baseConfigPath)
    {
        Text = "모델 선택";
        ClientSize = new Size(840, 620); // 위쪽 추천 모델 상자가 들어가도 목록이 충분히 보이게
        StartPosition = FormStartPosition.CenterScreen; // 첫 실행엔 부모 창이 없다
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false;
        Font = Theme.Font;

        page = new ModelManagerPage(baseConfigPath, engineRunning: false, firstRun: true);

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 52, BackColor = Theme.Surface };
        startBtn.Text = "HearingAid 시작 ▶";
        startBtn.Size = new Size(170, 32);
        startBtn.Location = new Point(ClientSize.Width - 186, 10);
        startBtn.Click += (s, e) => Close();
        bottom.Controls.Add(startBtn);

        Controls.Add(page);
        Controls.Add(new Panel { Dock = DockStyle.Bottom, Height = 1, BackColor = Theme.Border });
        Controls.Add(bottom);
        Theme.Apply(this);

        // 모델이 하나라도 생기면 "시작"이 주 버튼이 된다(그 전에는 페이지의 "다운로드"가 주 버튼).
        page.InstalledChanged += UpdateStartButton;
        UpdateStartButton();
    }

    void UpdateStartButton()
    {
        bool ready = ModelStore.ListInstalled().Count > 0;
        Theme.StyleButton(startBtn, ready ? Theme.ButtonKind.Primary : Theme.ButtonKind.Normal);
        AcceptButton = ready ? startBtn : null;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!page.ConfirmClose()) e.Cancel = true;
        base.OnFormClosing(e);
    }
}
