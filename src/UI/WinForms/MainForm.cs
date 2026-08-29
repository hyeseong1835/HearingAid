using HearingAid.Core;

namespace HearingAid;

public class MainForm : Form
{
    ITranscriber engine = null!;
    readonly string baseConfigPath;
    readonly string profilesDir;

    readonly ListView list = new();
    readonly Label status = new();
    readonly Button toggleBtn = new();
    readonly CheckBox autoScroll = new();
    readonly Button profileBtn = new();
    readonly Dictionary<int, ListViewItem> pendingItems = new();
    WaveformForm? waveformForm;

    public MainForm(ITranscriber engine, string baseConfigPath, string profilesDir)
    {
        this.baseConfigPath = baseConfigPath;
        this.profilesDir = profilesDir;
        BuildUi();
        AttachEngine(engine);
    }

    void BuildUi()
    {
        Text = "HearingAid";
        Width = 750; Height = 560;
        Font = new Font("Segoe UI", 10);

        // 상단 바
        var top = new Panel { Dock = DockStyle.Top, Height = 48 };
        toggleBtn.Text = "시작";
        toggleBtn.Width = 80; toggleBtn.Height = 32;
        toggleBtn.Location = new Point(8, 8);
        toggleBtn.Click += async (s, e) => await ToggleAsync();
        top.Controls.Add(toggleBtn);

        autoScroll.Text = "자동 스크롤";
        autoScroll.Checked = true;
        autoScroll.Location = new Point(100, 14);
        autoScroll.AutoSize = true;
        top.Controls.Add(autoScroll);

        profileBtn.Width = 130; profileBtn.Height = 32;
        profileBtn.Location = new Point(200, 8);
        profileBtn.Click += (s, e) => OpenProfileSettings();
        top.Controls.Add(profileBtn);

        var clearBtn = new Button { Text = "지우기", Width = 70, Height = 32, Location = new Point(336, 8) };
        clearBtn.Click += (s, e) => { list.Items.Clear(); pendingItems.Clear(); };
        top.Controls.Add(clearBtn);

        var refreshBtn = new Button { Text = "새로고침", Width = 90, Height = 32, Location = new Point(412, 8) };
        refreshBtn.Click += (s, e) => RefreshRecodes();
        top.Controls.Add(refreshBtn);

        var exportBtn = new Button { Text = "내보내기", Width = 90, Height = 32, Location = new Point(508, 8) };
        exportBtn.Click += (s, e) => Export();
        top.Controls.Add(exportBtn);

        var waveBtn = new Button { Text = "파형", Width = 60, Height = 32, Location = new Point(604, 8) };
        waveBtn.Click += (s, e) => OpenWaveform();
        top.Controls.Add(waveBtn);

        // 리스트
        list.Dock = DockStyle.Fill;
        list.View = View.Details;
        list.FullRowSelect = true;
        list.GridLines = false;
        list.HeaderStyle = ColumnHeaderStyle.Nonclickable;
        list.OwnerDraw = false;
        list.BackColor = Color.FromArgb(24, 24, 28);
        list.ForeColor = Color.Gainsboro;
        list.Font = new Font("Segoe UI", 11);
        list.BorderStyle = BorderStyle.None;

        list.Columns.Add("시간", 90);
        list.Columns.Add("화자", 90);
        list.Columns.Add("텍스트", 470);

        // 텍스트 열이 창 크기에 맞게 늘어나도록
        list.Resize += (s, e) =>
        {
            int w = list.ClientSize.Width - list.Columns[0].Width - list.Columns[1].Width - 4;
            if (w > 100) list.Columns[2].Width = w;
        };

        // 상태 바
        status.Dock = DockStyle.Bottom;
        status.Height = 24;
        status.TextAlign = ContentAlignment.MiddleLeft;
        status.BackColor = Color.FromArgb(40, 40, 46);
        status.ForeColor = Color.Silver;
        status.Text = "대기 중";
        status.Padding = new Padding(6, 0, 0, 0);

        Controls.Add(list);
        Controls.Add(status);
        Controls.Add(top);
    }

    void AttachEngine(ITranscriber e)
    {
        engine = e;
        engine.SetStatusAction += OnStatus;
        engine.OnCreatedText += OnRecodeCreated;
        engine.OnEdited += OnRecodeEdited;
        engine.OnDeleted += OnRecodeDeleted;
        RefreshFromEngine();
    }

    void DetachEngine(ITranscriber e)
    {
        e.SetStatusAction -= OnStatus;
        e.OnCreatedText -= OnRecodeCreated;
        e.OnEdited -= OnRecodeEdited;
        e.OnDeleted -= OnRecodeDeleted;
    }

    void RefreshFromEngine()
    {
        toggleBtn.Text = engine.IsRunning ? "정지" : "시작";
        UpdateProfileButton();
    }

    void UpdateProfileButton() => profileBtn.Text = $"프로필: {engine.Config.EffectiveProfileName ?? "(기본값)"}";

    void OpenProfileSettings()
    {
        using var dlg = new ProfileSettingsForm(engine, baseConfigPath, profilesDir);
        dlg.ShowDialog(this);
        if (dlg.NewEngine is { } newEngine)
            ReplaceEngine(newEngine);
    }

    void ReplaceEngine(ITranscriber newEngine)
    {
        var old = engine;
        DetachEngine(old);
        old.Dispose();
        AttachEngine(newEngine);
        waveformForm?.AttachEngine(newEngine);
    }

    void OpenWaveform()
    {
        if (waveformForm is null || waveformForm.IsDisposed)
        {
            waveformForm = new WaveformForm { Owner = this };
            waveformForm.AttachEngine(engine);
        }
        waveformForm.Show();
        waveformForm.Activate();
    }

    async Task ToggleAsync()
    {
        if (engine.IsRunning)
        {
            engine.StopAsync();
            toggleBtn.Text = "시작";
        }
        else
        {
            toggleBtn.Enabled = false;
            toggleBtn.Text = "시작 중...";
            try { await engine.StartAsync(); toggleBtn.Text = "정지"; }
            catch (Exception ex) { status.Text = $"오류: {ex.Message}"; toggleBtn.Text = "시작"; }
            finally { toggleBtn.Enabled = true; }
        }
    }

    void OnStatus(string msg) => Invoke(() =>
    {
        status.Text = msg;
    });

    void RefreshRecodes()
    {
        list.Items.Clear();
        pendingItems.Clear();
        foreach (KeyValuePair<int, TranscriptRecode> kv in engine.GetRecodes().OrderBy(kv => kv.Key))
            OnRecodeCreated(kv.Key, kv.Value);
    }

    void OnRecodeCreated(int id, TranscriptRecode recode) => Invoke(() =>
    {
        var item = new ListViewItem(recode.time.ToString("HH:mm:ss"))
        {
            ForeColor = Color.Gray,
            UseItemStyleForSubItems = false
        };
        var speakerSub = item.SubItems.Add(string.IsNullOrEmpty(recode.speaker)? "-" : recode.speaker);
        speakerSub.ForeColor = Color.Silver;
        var sub = item.SubItems.Add(recode.text?? "...");
        sub.ForeColor = Color.Gray;
        item.ToolTipText = "인식 중...";
        list.ShowItemToolTips = true;

        list.Items.Add(item);
        pendingItems[id] = item;

        if (autoScroll.Checked)
            item.EnsureVisible();
    });

    void OnRecodeEdited(int id, TranscriptRecode newRecode, TranscriptRecode oldRecode) => Invoke(() =>
    {
        if (!pendingItems.TryGetValue(id, out var existing)) return;

        existing.SubItems[2].Text = newRecode.text ?? "";
        existing.ToolTipText = newRecode.text;
        list.ShowItemToolTips = true;

        if (autoScroll.Checked)
            existing.EnsureVisible();
    });

    void OnRecodeDeleted(int id, TranscriptRecode oldRecode) => Invoke(() =>
    {
        if (pendingItems.Remove(id, out var existing))
            list.Items.Remove(existing);
    });

    void Export()
    {
        using var dlg = new ExportDialog();
        if (dlg.ShowDialog(this) != DialogResult.OK || dlg.SelectedFormat is not { } format) return;

        bool isTxt = format == ExportDialog.Format.Txt;
        using var saveDlg = new SaveFileDialog
        {
            Filter = isTxt ? "Text|*.txt" : "CSV|*.csv",
            FileName = $"transcript_{DateTime.Now:yyyyMMdd_HHmmss}.{(isTxt ? "txt" : "csv")}"
        };
        if (saveDlg.ShowDialog() != DialogResult.OK) return;

        if (isTxt) ExportTxt(saveDlg.FileName);
        else ExportCsv(saveDlg.FileName);
    }

    void ExportTxt(string path)
    {
        using var w = new StreamWriter(path) { NewLine = "\n" };
        foreach (ListViewItem it in list.Items)
            w.WriteLine($"{it.SubItems[1].Text}: {it.SubItems[2].Text}");
    }

    void ExportCsv(string path)
    {
        using var w = new StreamWriter(path, false, new System.Text.UTF8Encoding(true));
        w.WriteLine("시간,화자,텍스트");
        foreach (ListViewItem it in list.Items)
            w.WriteLine($"{CsvField(it.Text)},{CsvField(it.SubItems[1].Text)},{CsvField(it.SubItems[2].Text)}");
    }

    static string CsvField(string value) =>
        value.IndexOfAny([',', '"', '\n', '\r']) < 0 ? value : $"\"{value.Replace("\"", "\"\"")}\"";

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        engine.Dispose();
        base.OnFormClosing(e);
    }
}