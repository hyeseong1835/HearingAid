namespace HearingAid;

/// <summary>내보내기 포맷(TXT/CSV) 선택을 위한 최소 대화상자.</summary>
class ExportDialog : Form
{
    public enum Format { Txt, Csv }
    public Format? SelectedFormat { get; private set; }

    public ExportDialog()
    {
        Text = "내보내기";
        // Height는 제목 표시줄까지 포함한 크기라 아래 버튼이 잘린다. 안쪽 영역 크기로 지정한다.
        ClientSize = new Size(280, 150);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false; MinimizeBox = false;
        Font = new Font("Segoe UI", 10);
        BackColor = Color.FromArgb(24, 24, 28);
        ForeColor = Color.Gainsboro;

        var txtBtn = new Button { Text = "TXT로 내보내기", Width = 240, Height = 36, Location = new Point(20, 16) };
        txtBtn.Click += (s, e) => { SelectedFormat = Format.Txt; DialogResult = DialogResult.OK; Close(); };

        var csvBtn = new Button { Text = "CSV로 내보내기", Width = 240, Height = 36, Location = new Point(20, 60) };
        csvBtn.Click += (s, e) => { SelectedFormat = Format.Csv; DialogResult = DialogResult.OK; Close(); };

        var cancelBtn = new Button { Text = "취소", Width = 240, Height = 30, Location = new Point(20, 104) };
        cancelBtn.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };

        Controls.Add(txtBtn); Controls.Add(csvBtn); Controls.Add(cancelBtn);
        CancelButton = cancelBtn;

        Theme.Apply(this);
    }
}
