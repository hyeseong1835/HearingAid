using HearingAid.Core;
using HearingAid.Core.Update;

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
    readonly Button updateBtn = new();
    readonly Dictionary<int, ListViewItem> pendingItems = new();
    WaveformForm? waveformForm;
    ReleaseInfo? availableUpdate;

    public MainForm(ITranscriber engine, string baseConfigPath, string profilesDir)
    {
        this.baseConfigPath = baseConfigPath;
        this.profilesDir = profilesDir;
        BuildUi();
        AttachEngine(engine);
        Shown += async (s, e) =>
        {
            if (!this.engine.Config.CheckForUpdates) return;
            await CheckForUpdateAsync(silent: true);
            if (availableUpdate is not null) ShowUpdateDialog();
        };
    }

    void BuildUi()
    {
        Text = $"HearingAid v{AppUpdater.CurrentVersion.ToString(3)}";
        Width = 880; Height = 560;
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

        var modelBtn = new Button { Text = "모델", Width = 60, Height = 32, Location = new Point(670, 8) };
        modelBtn.Click += (s, e) => OpenModelManager();
        top.Controls.Add(modelBtn);

        updateBtn.Text = "업데이트"; updateBtn.Width = 130; updateBtn.Height = 32; updateBtn.Location = new Point(736, 8);
        updateBtn.Click += async (s, e) => await OnUpdateClickedAsync();
        top.Controls.Add(updateBtn);

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
        list.Resize += (s, e) => FitTextColumn();
        // 화자를 구분하지 않는 소스에서는 화자 열을 폭 0으로 숨긴다. 사용자가 경계를 끌어 다시 펼치지 못하게 막는다.
        list.ColumnWidthChanging += (s, e) =>
        {
            if (e.ColumnIndex == SpeakerColumn && e.NewWidth != 0 && engine is not null && !engine.SpeakerRecognition)
                e.Cancel = true;
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
        list.Columns[SpeakerColumn].Width = engine.SpeakerRecognition ? 90 : 0;
        FitTextColumn();
    }

    const int SpeakerColumn = 1;

    void FitTextColumn()
    {
        int w = list.ClientSize.Width - list.Columns[0].Width - list.Columns[SpeakerColumn].Width - 4;
        if (w > 100) list.Columns[2].Width = w;
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

    void OpenModelManager()
    {
        using var dlg = new ModelManagerDialog(baseConfigPath, engine.IsRunning, firstRun: false);
        dlg.ShowDialog(this);

        // 기본 모델이 바뀌었으면 지금 프로필 그대로 엔진을 다시 만들어 다음 시작부터 새 모델을 쓰게 한다.
        // (실행 중에는 대화상자가 기본 모델 변경을 막는다.)
        if (dlg.Changed && !engine.IsRunning)
        {
            try { ReplaceEngine(Config.BuildEngine(baseConfigPath, profilesDir, engine.Config.EffectiveProfileName)); }
            catch (Exception ex) { status.Text = $"오류: {ex.Message}"; }
        }
    }

    async Task OnUpdateClickedAsync()
    {
        if (availableUpdate is null)
        {
            await CheckForUpdateAsync(silent: false);
            if (availableUpdate is null) return;
        }
        ShowUpdateDialog();
    }

    void ShowUpdateDialog()
    {
        using var dlg = new UpdateDialog(availableUpdate!, engine.IsRunning);
        dlg.ShowDialog(this);
        if (dlg.ApplierLaunched) Close();
    }

    /// <summary>silent면 실패나 "최신 버전" 결과를 알리지 않는다(시작 시 자동 확인용).</summary>
    async Task CheckForUpdateAsync(bool silent)
    {
        updateBtn.Enabled = false;
        try
        {
            availableUpdate = await AppUpdater.CheckAsync();
            if (availableUpdate is { } r)
            {
                updateBtn.Text = $"업데이트 v{r.Version.ToString(3)}";
                updateBtn.BackColor = Color.FromArgb(0, 122, 204);
                updateBtn.ForeColor = Color.White;
                status.Text = $"새 버전 v{r.Version.ToString(3)}이(가) 있습니다. 오른쪽 위 업데이트 버튼을 누르세요.";
            }
            else if (!silent)
            {
                MessageBox.Show(this, $"최신 버전입니다. (v{AppUpdater.CurrentVersion.ToString(3)})", "업데이트",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
        catch (Exception ex)
        {
            if (!silent)
                MessageBox.Show(this, $"업데이트 확인 실패: {ex.Message}", "업데이트", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            if (!IsDisposed) updateBtn.Enabled = true;
        }
    }

    async Task ToggleAsync()
    {
        if (engine.IsRunning)
        {
            _ = engine.StopAsync();
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

    /// <summary>엔진 이벤트는 오디오/전사 스레드에서 온다. Invoke(동기)를 쓰면 창을 닫는 중 UI 스레드가 엔진 정지를
    /// 기다리는 동안 서로를 기다려 멈추므로, BeginInvoke로 넘기고 이미 닫힌 창이면 버린다.</summary>
    void PostToUi(Action action)
    {
        if (IsDisposed || Disposing || !IsHandleCreated) return;
        try { BeginInvoke(action); }
        catch (InvalidOperationException) { } // 그 사이 핸들이 파괴됨(ObjectDisposedException 포함)
    }

    void OnStatus(string msg) => PostToUi(() =>
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

    void OnRecodeCreated(int id, TranscriptRecode recode) => PostToUi(() =>
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

    void OnRecodeEdited(int id, TranscriptRecode newRecode, TranscriptRecode oldRecode) => PostToUi(() =>
    {
        if (!pendingItems.TryGetValue(id, out var existing)) return;

        existing.SubItems[2].Text = newRecode.text ?? "";
        existing.ToolTipText = newRecode.text;
        list.ShowItemToolTips = true;

        if (autoScroll.Checked)
            existing.EnsureVisible();
    });

    void OnRecodeDeleted(int id, TranscriptRecode oldRecode) => PostToUi(() =>
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
        bool speakers = engine.SpeakerRecognition;
        using var w = new StreamWriter(path) { NewLine = "\n" };
        foreach (ListViewItem it in list.Items)
            w.WriteLine(speakers ? $"{it.SubItems[1].Text}: {it.SubItems[2].Text}" : it.SubItems[2].Text);
    }

    void ExportCsv(string path)
    {
        bool speakers = engine.SpeakerRecognition;
        using var w = new StreamWriter(path, false, new System.Text.UTF8Encoding(true));
        w.WriteLine(speakers ? "시간,화자,텍스트" : "시간,텍스트");
        foreach (ListViewItem it in list.Items)
            w.WriteLine(speakers
                ? $"{CsvField(it.Text)},{CsvField(it.SubItems[1].Text)},{CsvField(it.SubItems[2].Text)}"
                : $"{CsvField(it.Text)},{CsvField(it.SubItems[2].Text)}");
    }

    static string CsvField(string value) =>
        value.IndexOfAny([',', '"', '\n', '\r']) < 0 ? value : $"\"{value.Replace("\"", "\"\"")}\"";

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        DetachEngine(engine); // 정지하는 동안 오는 이벤트는 더 이상 화면에 반영하지 않는다
        engine.Dispose();
        base.OnFormClosing(e);
    }
}