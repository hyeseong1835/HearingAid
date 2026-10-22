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
    readonly ComboBox profileCombo = new();
    readonly Button editProfilesBtn = new();
    bool suppressProfileSwitch;
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
        Width = 1080; Height = 640;
        MinimumSize = new Size(1000, 420); // 이보다 좁으면 왼쪽(시작·프로필)과 오른쪽 버튼 묶음이 겹친다
        StartPosition = FormStartPosition.CenterScreen;
        Font = Theme.Font;

        // --- 상단 도구 모음: 왼쪽은 캡처 제어, 오른쪽은 보조 기능 ---
        var toolbar = new Panel { Dock = DockStyle.Top, Height = 56, BackColor = Theme.Surface, Padding = new Padding(10, 10, 10, 0) };

        var left = new FlowLayoutPanel { Dock = DockStyle.Left, AutoSize = true, WrapContents = false, BackColor = Theme.Surface };
        toggleBtn.Size = new Size(104, 36);
        toggleBtn.Margin = new Padding(0, 0, 8, 0);
        toggleBtn.Click += async (s, e) => await ToggleAsync();
        // 프로필: 콤보박스로 바로 전환하고, 내용 편집은 옆의 별도 버튼으로 연다.
        var profileLabel = new Label { Text = "프로필", AutoSize = true, ForeColor = Theme.SubText, Margin = new Padding(8, 9, 6, 0) };
        profileCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        profileCombo.Width = 190;
        profileCombo.Margin = new Padding(0, 4, 6, 0);
        profileCombo.SelectionChangeCommitted += (s, e) =>
        {
            SwitchProfile(profileCombo.SelectedItem as string);
            list.Focus(); // 포커스가 남아 있으면 콤보박스가 선택 강조색(흰 바탕)으로 그려져 튄다
        };
        editProfilesBtn.Text = "⚙ 편집";
        editProfilesBtn.Size = new Size(84, 36);
        editProfilesBtn.Margin = new Padding(0);
        editProfilesBtn.Click += (s, e) => OpenProfileSettings();
        left.Controls.AddRange([toggleBtn, profileLabel, profileCombo, editProfilesBtn]);

        var right = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, WrapContents = false, BackColor = Theme.Surface };
        updateBtn.Text = "업데이트";
        updateBtn.Click += async (s, e) => await OnUpdateClickedAsync();
        right.Controls.AddRange([
            ToolButton("파형", (s, e) => OpenWaveform()),
            ToolButton("모델", (s, e) => OpenModelManager()),
            ToolButton("내보내기", (s, e) => Export()),
            ToolButton("지우기", (s, e) => { list.Items.Clear(); pendingItems.Clear(); UpdateEmptyHint(); }),
            ToolButton("새로고침", (s, e) => RefreshRecodes()),
        ]);
        updateBtn.Size = new Size(124, 36);
        updateBtn.Margin = new Padding(6, 0, 0, 0);
        right.Controls.Add(updateBtn);

        toolbar.Controls.Add(right);
        toolbar.Controls.Add(left);
        var toolbarLine = new Panel { Dock = DockStyle.Top, Height = 1, BackColor = Theme.Border };

        // --- 설정 경고 바: 오디오 소스가 없거나 만들 수 없을 때만 보인다 ---
        warningBar.Dock = DockStyle.Top;
        warningBar.Height = 44;
        warningBar.BackColor = Color.FromArgb(58, 46, 18);
        warningBar.Padding = new Padding(14, 6, 10, 6);
        warningLabel.Dock = DockStyle.Fill;
        warningLabel.TextAlign = ContentAlignment.MiddleLeft;
        warningLabel.ForeColor = Color.FromArgb(240, 200, 110);
        warningLabel.AutoEllipsis = true;
        var warningBtn = new Button { Text = "⚙ 오디오 소스 설정", Dock = DockStyle.Right, Width = 150 };
        warningBtn.Click += (s, e) => OpenProfileSettings();
        warningBar.Controls.Add(warningLabel);
        warningBar.Controls.Add(warningBtn);
        warningBar.Visible = false;

        // --- 받아 적은 내용 목록 ---
        list.Dock = DockStyle.Fill;
        list.View = View.Details;
        list.FullRowSelect = true;
        list.HeaderStyle = ColumnHeaderStyle.Nonclickable;
        list.BackColor = Theme.Background;
        list.ForeColor = Theme.Text;
        list.Font = new Font("Segoe UI", 11);
        list.BorderStyle = BorderStyle.None;
        list.ShowItemToolTips = true;
        // 행 높이는 ListView에 직접 설정할 방법이 없어, 높이만 있는 빈 이미지 목록으로 넓힌다.
        list.SmallImageList = new ImageList { ImageSize = new Size(1, 32) };
        list.OwnerDraw = true;
        list.DrawColumnHeader += (s, e) => Theme.DrawHeader(e, Theme.Font);
        list.DrawItem += (s, e) => { };
        list.DrawSubItem += (s, e) => Theme.DrawCell(e, list);

        list.Columns.Add("시간", 96);
        list.Columns.Add("화자", 130);
        list.Columns.Add("내용", 600);

        // 텍스트 열이 창 크기에 맞게 늘어나도록
        list.Resize += (s, e) => FitTextColumn();
        // 화자를 구분하지 않는 소스에서는 화자 열을 폭 0으로 숨긴다. 사용자가 경계를 끌어 다시 펼치지 못하게 막는다.
        list.ColumnWidthChanging += (s, e) =>
        {
            if (e.ColumnIndex == SpeakerColumn && e.NewWidth != 0 && engine is not null && !engine.SpeakerRecognition)
                e.Cancel = true;
        };

        emptyHint.Dock = DockStyle.Fill;
        emptyHint.TextAlign = ContentAlignment.MiddleCenter;
        emptyHint.ForeColor = Theme.SubText;
        emptyHint.BackColor = Theme.Background;
        emptyHint.Font = new Font("Segoe UI", 11);
        emptyHint.Text = "▶ 시작을 누르면 들리는 말이 여기에 받아 적힙니다.\n\n" +
                         "듣는 대상은 '프로필'에서 바꿀 수 있고,\n소리가 들어오는지는 '파형'에서 확인할 수 있습니다.";
        list.Controls.Add(emptyHint);

        // --- 상태 바 ---
        var statusBar = new Panel { Dock = DockStyle.Bottom, Height = 30, BackColor = Theme.Surface, Padding = new Padding(10, 0, 10, 0) };
        status.Dock = DockStyle.Fill;
        status.TextAlign = ContentAlignment.MiddleLeft;
        status.ForeColor = Theme.SubText;
        status.AutoEllipsis = true;
        status.Text = "대기 중";
        autoScroll.Text = "자동 스크롤";
        autoScroll.Checked = true;
        autoScroll.AutoSize = true;
        autoScroll.Dock = DockStyle.Right;
        autoScroll.ForeColor = Theme.Text; // Flat 체크박스는 체크 표시도 ForeColor로 그리므로 흐린 색이면 체크 여부가 안 보인다
        statusBar.Controls.Add(status);
        statusBar.Controls.Add(autoScroll);
        var statusLine = new Panel { Dock = DockStyle.Bottom, Height = 1, BackColor = Theme.Border };

        Controls.Add(list);
        Controls.Add(warningBar);
        Controls.Add(toolbarLine);
        Controls.Add(toolbar);
        Controls.Add(statusLine);
        Controls.Add(statusBar);

        Theme.Apply(this);
        UpdateToggleButton(running: false);
        ActiveControl = list; // 첫 포커스가 프로필 콤보박스로 가면 흰 바탕으로 강조돼 보인다
    }

    readonly Label emptyHint = new();
    readonly Panel warningBar = new();
    readonly Label warningLabel = new();

    static Button ToolButton(string text, EventHandler onClick)
    {
        var b = new Button { Text = text, Height = 36, AutoSize = true, MinimumSize = new Size(72, 36), Margin = new Padding(6, 0, 0, 0) };
        b.Click += onClick;
        return b;
    }

    void UpdateToggleButton(bool running, bool starting = false)
    {
        toggleBtn.Text = starting ? "시작 중..." : running ? "■  정지" : "▶  시작";
        Theme.StyleButton(toggleBtn, running ? Theme.ButtonKind.Danger : Theme.ButtonKind.Primary);
        // 캡처 중에는 프로필을 바꿀 수 없다(엔진을 통째로 새로 만들기 때문).
        profileCombo.Enabled = !running && !starting && Config.ListProfileNames(profilesDir).Count > 0;
    }

    void UpdateEmptyHint() => emptyHint.Visible = list.Items.Count == 0;

    /// <summary>화자별로 구분되는 색. 같은 이름은 항상 같은 색이 되도록 이름의 해시로 고른다.</summary>
    static readonly Color[] SpeakerPalette =
    [
        Color.FromArgb(110, 180, 255), Color.FromArgb(255, 170, 100), Color.FromArgb(130, 220, 140),
        Color.FromArgb(240, 130, 180), Color.FromArgb(200, 160, 255), Color.FromArgb(250, 220, 110),
        Color.FromArgb(100, 220, 220), Color.FromArgb(255, 140, 130),
    ];

    static Color SpeakerColor(string speaker)
    {
        int hash = 0;
        foreach (char ch in speaker) hash = unchecked(hash * 31 + ch); // string.GetHashCode는 실행마다 달라서 쓰지 않는다
        return SpeakerPalette[(int)((uint)hash % SpeakerPalette.Length)];
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
        UpdateToggleButton(engine.IsRunning);
        UpdateProfileButton();
        warningBar.Visible = engine.SetupWarning is not null;
        warningLabel.Text = $"⚠  {engine.SetupWarning}";
        list.Columns[SpeakerColumn].Width = engine.SpeakerRecognition ? 130 : 0;
        FitTextColumn();
    }

    const int SpeakerColumn = 1;

    void FitTextColumn()
    {
        int w = list.ClientSize.Width - list.Columns[0].Width - list.Columns[SpeakerColumn].Width - 4;
        if (w > 100) list.Columns[2].Width = w;
    }

    /// <summary>프로필 콤보박스를 profiles/ 목록으로 다시 채우고 지금 쓰는 프로필을 선택한다.
    /// 프로필이 하나도 없으면 비활성화하고 안내 문구만 보인다(이때는 기본 설정으로 동작).</summary>
    void UpdateProfileButton()
    {
        suppressProfileSwitch = true;
        try
        {
            var names = Config.ListProfileNames(profilesDir);
            profileCombo.Items.Clear();
            if (names.Count == 0)
            {
                profileCombo.Items.Add("(프로필 없음)");
                profileCombo.SelectedIndex = 0;
                profileCombo.Enabled = false;
                return;
            }

            profileCombo.Items.AddRange(names.Cast<object>().ToArray());
            profileCombo.SelectedItem = names.FirstOrDefault(n =>
                string.Equals(n, engine.Config.EffectiveProfileName, StringComparison.OrdinalIgnoreCase));
            profileCombo.Enabled = !engine.IsRunning;
        }
        finally { suppressProfileSwitch = false; }
    }

    void SwitchProfile(string? name)
    {
        if (suppressProfileSwitch || name is null) return;
        if (string.Equals(name, engine.Config.EffectiveProfileName, StringComparison.OrdinalIgnoreCase)) return;
        if (engine.IsRunning)
        {
            status.Text = "캡처를 정지한 뒤 프로필을 바꿀 수 있습니다.";
            UpdateProfileButton();
            return;
        }

        try
        {
            Config.SetActiveProfile(baseConfigPath, name);
            ReplaceEngine(Config.BuildEngine(baseConfigPath, profilesDir, name));
            status.Text = $"프로필 '{name}'(으)로 바꿨습니다.";
        }
        catch (Exception ex)
        {
            status.Text = $"프로필 전환 실패: {ex.Message}";
            UpdateProfileButton();
        }
    }

    void OpenProfileSettings()
    {
        using var dlg = new ProfileSettingsForm(engine, baseConfigPath, profilesDir);
        dlg.ShowDialog(this);
        if (dlg.NewEngine is { } newEngine)
            ReplaceEngine(newEngine);
        else
            UpdateProfileButton(); // 편집 창에서 프로필을 추가·삭제했을 수 있다
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
                Theme.StyleButton(updateBtn, Theme.ButtonKind.Primary);
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
            UpdateToggleButton(running: false);
        }
        else if (engine.SetupWarning is not null)
        {
            // 오디오 소스가 없으면 시작할 수 없으므로 바로 설정 화면으로 안내한다.
            status.Text = engine.SetupWarning;
            OpenProfileSettings();
        }
        else
        {
            toggleBtn.Enabled = false;
            UpdateToggleButton(running: false, starting: true);
            try { await engine.StartAsync(); UpdateToggleButton(running: true); }
            catch (Exception ex)
            {
                // 장치 목록처럼 여러 줄인 오류는 한 줄짜리 상태 표시줄에서 잘리므로 전문은 대화상자로 보여준다.
                status.Text = $"오류: {ex.Message.Split('\n')[0]}";
                UpdateToggleButton(running: false);
                MessageBox.Show(this, ex.Message, "시작할 수 없습니다", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
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
        UpdateEmptyHint();
        foreach (KeyValuePair<int, TranscriptRecode> kv in engine.GetRecodes().OrderBy(kv => kv.Key))
            OnRecodeCreated(kv.Key, kv.Value);
    }

    void OnRecodeCreated(int id, TranscriptRecode recode) => PostToUi(() =>
    {
        bool hasSpeaker = !string.IsNullOrEmpty(recode.speaker);
        var item = new ListViewItem(recode.time.ToString("HH:mm:ss"))
        {
            ForeColor = Theme.SubText,
            UseItemStyleForSubItems = false
        };
        var speakerSub = item.SubItems.Add(hasSpeaker ? recode.speaker : "-");
        speakerSub.ForeColor = hasSpeaker ? SpeakerColor(recode.speaker!) : Theme.SubText;
        speakerSub.Font = new Font(list.Font, FontStyle.Bold);
        // 전사가 끝나기 전에는 흐린 "…"로 두고, 글이 도착하면 OnRecodeEdited에서 본문 색으로 바꾼다.
        var sub = item.SubItems.Add(recode.text ?? "…");
        sub.ForeColor = recode.text is null ? Theme.SubText : Theme.Text;
        item.ToolTipText = recode.text ?? "인식 중...";

        list.Items.Add(item);
        pendingItems[id] = item;
        UpdateEmptyHint();

        if (autoScroll.Checked)
            item.EnsureVisible();
    });

    void OnRecodeEdited(int id, TranscriptRecode newRecode, TranscriptRecode oldRecode) => PostToUi(() =>
    {
        if (!pendingItems.TryGetValue(id, out var existing)) return;

        existing.SubItems[2].Text = newRecode.text ?? "";
        existing.SubItems[2].ForeColor = Theme.Text;
        existing.ToolTipText = newRecode.text;

        if (autoScroll.Checked)
            existing.EnsureVisible();
    });

    void OnRecodeDeleted(int id, TranscriptRecode oldRecode) => PostToUi(() =>
    {
        if (pendingItems.Remove(id, out var existing))
            list.Items.Remove(existing);
        UpdateEmptyHint();
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