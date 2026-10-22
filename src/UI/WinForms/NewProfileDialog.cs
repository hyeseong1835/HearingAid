namespace HearingAid;

/// <summary>새 프로필 이름 입력을 위한 최소 대화상자. 생성되는 프로필 파일은 빈 override({})로 시작한다.</summary>
class NewProfileDialog : Form
{
    readonly TextBox nameBox = new();
    readonly Label error = new();
    readonly IReadOnlyList<string> existingNames;

    public string? ProfileName { get; private set; }

    public NewProfileDialog(IEnumerable<string> existingNames)
    {
        this.existingNames = existingNames.ToList();
        BuildUi();
    }

    void BuildUi()
    {
        Text = "새 프로필 추가";
        ClientSize = new Size(344, 146); // Height는 제목 표시줄을 포함해 아래 버튼이 잘릴 수 있어 안쪽 영역으로 지정한다
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false; MinimizeBox = false;
        Font = new Font("Segoe UI", 10);
        BackColor = Color.FromArgb(24, 24, 28);
        ForeColor = Color.Gainsboro;

        Controls.Add(new Label { Text = "이름", Location = new Point(12, 16), AutoSize = true });
        nameBox.Location = new Point(90, 12); nameBox.Width = 240;

        error.Location = new Point(12, 48); error.Size = new Size(320, 40); error.ForeColor = Color.IndianRed;

        var okBtn = new Button { Text = "추가", Width = 80, Height = 30, Location = new Point(160, 100) };
        okBtn.Click += (s, e) => TryAccept();
        var cancelBtn = new Button { Text = "취소", Width = 80, Height = 30, Location = new Point(250, 100) };
        cancelBtn.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };

        Controls.Add(nameBox); Controls.Add(error); Controls.Add(okBtn); Controls.Add(cancelBtn);
        AcceptButton = okBtn; CancelButton = cancelBtn;

        Theme.Apply(this);
        Theme.StyleButton(okBtn, Theme.ButtonKind.Primary);
    }

    void TryAccept()
    {
        string name = nameBox.Text.Trim();
        if (string.IsNullOrEmpty(name))
        { error.Text = "이름을 입력하세요."; return; }
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        { error.Text = "파일 이름으로 사용할 수 없는 문자가 포함되어 있습니다."; return; }
        if (name.EndsWith('.'))
        { error.Text = "이름이 마침표(.)로 끝날 수 없습니다."; return; }
        if (IsReservedName(name))
        { error.Text = "Windows가 예약한 이름(CON, NUL, COM1 등)은 쓸 수 없습니다."; return; }
        if (existingNames.Contains(name, StringComparer.OrdinalIgnoreCase))
        { error.Text = "이미 사용 중인 이름입니다."; return; }

        ProfileName = name;
        DialogResult = DialogResult.OK;
        Close();
    }

    /// <summary>Windows 장치 이름. 확장자가 붙어도(예: con.json) 파일을 만들 수 없다.</summary>
    static bool IsReservedName(string name)
    {
        string stem = name.Split('.')[0].TrimEnd().ToUpperInvariant();
        if (stem is "CON" or "PRN" or "AUX" or "NUL" or "CONIN$" or "CONOUT$") return true;
        return stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT"))
               && (char.IsAsciiDigit(stem[3]) || stem[3] is '¹' or '²' or '³');
    }
}
