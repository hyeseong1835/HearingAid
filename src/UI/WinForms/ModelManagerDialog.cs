using HearingAid.Core;
using HearingAid.Core.Stt;

namespace HearingAid;

/// <summary>Whisper 모델 다운로드/삭제와 기본 모델(appsettings.json의 model) 지정을 한다.
/// 첫 실행(설치된 모델 없음) 때도 이 창을 안내문과 함께 띄운다.</summary>
public class ModelManagerDialog : Form
{
    readonly string baseConfigPath;
    readonly bool engineRunning;
    readonly bool firstRun;

    readonly ListView list = new();
    readonly Button downloadBtn = new();
    readonly Button cancelBtn = new();
    readonly Button deleteBtn = new();
    readonly Button defaultBtn = new();
    readonly Button closeBtn = new();
    readonly ProgressBar progress = new();
    readonly Label progressLabel = new();

    CancellationTokenSource? downloadCts;
    string? downloadingId;

    /// <summary>기본 모델을 바꿨는지. MainForm이 엔진을 다시 만들지 판단하는 데 쓴다.</summary>
    public bool Changed { get; private set; }

    public ModelManagerDialog(string baseConfigPath, bool engineRunning, bool firstRun)
    {
        this.baseConfigPath = baseConfigPath;
        this.engineRunning = engineRunning;
        this.firstRun = firstRun;
        BuildUi();
        Theme.Apply(this);
        Theme.StyleButton(downloadBtn, Theme.ButtonKind.Primary);
        RefreshList(firstRun ? ModelStore.RecommendedId : null);
    }

    string? BaseDefaultId => Config.GetBaseValue(baseConfigPath, "model");
    string? SelectedId => list.SelectedItems.Count > 0 ? (string)list.SelectedItems[0].Tag! : null;

    void BuildUi()
    {
        Text = firstRun ? "모델 선택" : "모델 관리";
        Width = 760; Height = 520;
        StartPosition = firstRun ? FormStartPosition.CenterScreen : FormStartPosition.CenterParent; // 첫 실행엔 부모 창이 없다
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false;
        Font = new Font("Segoe UI", 10);
        BackColor = Color.FromArgb(24, 24, 28);
        ForeColor = Color.Gainsboro;

        var intro = new Label
        {
            Dock = DockStyle.Top, Height = firstRun ? 64 : 40,
            Padding = new Padding(8, 8, 8, 0), ForeColor = Color.Silver,
            Text = firstRun
                ? "음성 인식에 쓸 Whisper 모델을 하나 이상 내려받아야 합니다.\n잘 모르겠다면 추천 모델(large-v3-turbo)을 받으세요. 저사양 PC라면 small이나 양자화(q5) 모델이 낫습니다."
                : "여러 모델을 받아 두고, 프로필 설정에서 프로필별·화자별로 다른 모델을 지정할 수 있습니다."
                  + (engineRunning ? "\n캡처 중에는 삭제와 기본 모델 지정을 할 수 없습니다." : ""),
        };

        list.Dock = DockStyle.Fill;
        list.View = View.Details;
        list.FullRowSelect = true;
        list.MultiSelect = false;
        list.HideSelection = false;
        list.HeaderStyle = ColumnHeaderStyle.Nonclickable;
        list.BackColor = Theme.Background;
        list.SmallImageList = new ImageList { ImageSize = new Size(1, 28) }; // 행 높이를 넓히기 위한 빈 이미지 목록
        list.ForeColor = Color.Gainsboro;
        list.BorderStyle = BorderStyle.None;
        list.Columns.Add("모델", 180);
        list.Columns.Add("크기", 80);
        list.Columns.Add("상태", 130);
        list.Columns.Add("설명", 330);
        list.SelectedIndexChanged += (s, e) => UpdateButtons();
        list.DoubleClick += async (s, e) => { if (downloadBtn.Enabled) await DownloadSelectedAsync(); };

        progress.Dock = DockStyle.Top; progress.Height = 18;
        progressLabel.Dock = DockStyle.Top; progressLabel.Height = 24;
        progressLabel.Padding = new Padding(4, 4, 0, 0); progressLabel.ForeColor = Color.Silver;

        var buttons = new Panel { Dock = DockStyle.Top, Height = 44 };
        downloadBtn.Text = "다운로드"; downloadBtn.Width = 100; downloadBtn.Height = 30; downloadBtn.Location = new Point(4, 6);
        downloadBtn.Click += async (s, e) => await DownloadSelectedAsync();
        cancelBtn.Text = "취소"; cancelBtn.Width = 80; cancelBtn.Height = 30; cancelBtn.Location = new Point(110, 6);
        cancelBtn.Click += (s, e) => downloadCts?.Cancel();
        deleteBtn.Text = "삭제"; deleteBtn.Width = 80; deleteBtn.Height = 30; deleteBtn.Location = new Point(196, 6);
        deleteBtn.Click += (s, e) => DeleteSelected();
        defaultBtn.Text = "기본 모델로 지정"; defaultBtn.Width = 140; defaultBtn.Height = 30; defaultBtn.Location = new Point(282, 6);
        defaultBtn.Click += (s, e) => { if (SelectedId is { } id) SetDefault(id); };
        closeBtn.Text = "닫기"; closeBtn.Width = 80; closeBtn.Height = 30; closeBtn.Location = new Point(428, 6);
        closeBtn.Click += (s, e) => Close();
        buttons.Controls.AddRange([downloadBtn, cancelBtn, deleteBtn, defaultBtn, closeBtn]);

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 90, Padding = new Padding(8, 6, 8, 0), BackColor = Theme.Surface };
        bottom.Controls.Add(buttons);
        bottom.Controls.Add(progressLabel);
        bottom.Controls.Add(progress);

        Controls.Add(list);
        Controls.Add(intro);
        Controls.Add(bottom);
        UpdateButtons();
    }

    void RefreshList(string? selectId = null)
    {
        selectId ??= SelectedId;
        string? defaultId = ModelStore.ResolveDefault(BaseDefaultId);
        var installed = ModelStore.ListInstalled();

        list.BeginUpdate();
        list.Items.Clear();

        var ids = ModelStore.Catalog.Select(m => m.Id)
            .Concat(installed.Where(id => ModelStore.FindInfo(id) is null));
        foreach (string id in ids)
        {
            var info = ModelStore.FindInfo(id);
            bool isInstalled = installed.Contains(id, StringComparer.OrdinalIgnoreCase);
            string state = id == downloadingId ? "다운로드 중"
                : !isInstalled ? "-"
                : string.Equals(id, defaultId, StringComparison.OrdinalIgnoreCase) ? "설치됨 (기본)"
                : "설치됨";
            long size = info?.SizeBytes ?? (ModelStore.GetPath(id) is { } p ? new FileInfo(p).Length : 0);

            var item = new ListViewItem(id) { Tag = id, UseItemStyleForSubItems = false };
            item.SubItems.Add(ModelStore.FormatSize(size));
            var stateSub = item.SubItems.Add(state);
            stateSub.ForeColor = isInstalled ? Color.FromArgb(110, 200, 120) : Color.Gray;
            item.SubItems.Add(info?.Description ?? "직접 추가한 모델");
            if (id == ModelStore.RecommendedId) item.Font = new Font(list.Font, FontStyle.Bold);
            list.Items.Add(item);

            if (string.Equals(id, selectId, StringComparison.OrdinalIgnoreCase)) item.Selected = true;
        }
        list.EndUpdate();
        UpdateButtons();
    }

    void UpdateButtons()
    {
        bool busy = downloadingId is not null;
        bool installed = SelectedId is { } id && ModelStore.IsInstalled(id);

        downloadBtn.Enabled = !busy && SelectedId is { } sel && !installed && ModelStore.FindInfo(sel) is not null;
        cancelBtn.Enabled = busy;
        deleteBtn.Enabled = !busy && !engineRunning && installed;
        defaultBtn.Enabled = !busy && !engineRunning && installed;
        progress.Visible = busy;
    }

    async Task DownloadSelectedAsync()
    {
        if (SelectedId is not { } id || ModelStore.FindInfo(id) is not { } info) return;

        downloadingId = id;
        downloadCts = new CancellationTokenSource();
        progress.Value = 0;
        progressLabel.Text = $"{id} 다운로드 준비 중...";
        RefreshList(id);

        var reporter = new Progress<(long Done, long Total)>(p =>
        {
            if (IsDisposed) return;
            progress.Value = p.Total > 0 ? (int)Math.Min(100, p.Done * 100 / p.Total) : 0;
            progressLabel.Text = $"{id}  {ModelStore.FormatSize(p.Done)} / {ModelStore.FormatSize(p.Total)}";
        });

        try
        {
            await ModelStore.DownloadAsync(info, reporter, downloadCts.Token);
            progressLabel.Text = $"{id} 다운로드 완료";

            // 기본 모델이 아직 정해지지 않았다면 처음 받은 모델을 기본으로 삼는다.
            if (string.IsNullOrEmpty(BaseDefaultId) && !engineRunning) SetDefault(id);
        }
        catch (OperationCanceledException)
        {
            progressLabel.Text = $"{id} 다운로드 취소됨";
        }
        catch (Exception ex)
        {
            progressLabel.Text = $"{id} 다운로드 실패: {ex.Message}";
        }
        finally
        {
            downloadCts.Dispose();
            downloadCts = null;
            downloadingId = null;
            if (!IsDisposed) RefreshList(id);
        }
    }

    void DeleteSelected()
    {
        if (SelectedId is not { } id) return;
        if (MessageBox.Show(this, $"'{id}' 모델 파일을 삭제하시겠습니까?", "삭제 확인",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            return;

        try { ModelStore.Delete(id); }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "삭제 실패", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        // 기본 모델을 지웠다면 지정을 풀어 설치된 다른 모델로 자연스럽게 넘어가게 한다.
        if (string.Equals(BaseDefaultId, id, StringComparison.OrdinalIgnoreCase))
        {
            Config.SetDefaultModel(baseConfigPath, null);
            Changed = true;
        }
        RefreshList();
    }

    void SetDefault(string id)
    {
        Config.SetDefaultModel(baseConfigPath, id);
        Changed = true;
        RefreshList(id);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (downloadCts is not null)
        {
            if (MessageBox.Show(this, "다운로드 중입니다. 취소하고 닫을까요?", "다운로드 중",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            {
                e.Cancel = true;
                return;
            }
            downloadCts.Cancel();
        }
        base.OnFormClosing(e);
    }
}
