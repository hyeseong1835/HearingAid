using HearingAid.Core;
using HearingAid.Core.Stt;

namespace HearingAid;

/// <summary>설정 창의 '모델' 페이지. Whisper 모델 다운로드/삭제와 기본 모델(appsettings.json의 model) 지정을 한다.
/// 첫 실행(설치된 모델 없음) 때는 ModelManagerDialog가 이 페이지를 안내문과 함께 띄운다.
/// 모델 이름만으로는 무엇을 받아야 할지 알기 어려우므로, 추천 모델은 위쪽 안내 상자(바로 받기 버튼)와
/// 목록 맨 위의 강조된 줄로 두 번 보여준다.</summary>
public class ModelManagerPage : UserControl
{
    readonly string baseConfigPath;
    readonly bool engineRunning;
    readonly bool firstRun;

    readonly ListView list = new();
    readonly Button downloadBtn = new();
    readonly Button cancelBtn = new();
    readonly Button deleteBtn = new();
    readonly Button defaultBtn = new();
    readonly ProgressBar progress = new();
    readonly Label progressLabel = new();

    // 추천 모델 안내 상자
    readonly Panel recommendBox = new();
    readonly Label recommendTitle = new();
    readonly Label recommendDesc = new();
    readonly Button recommendBtn = new();

    /// <summary>추천 모델을 표시하는 색. 파란 강조색(선택·주 버튼)과 겹치지 않게 금색 계열을 쓴다.</summary>
    static readonly Color RecommendText = Color.FromArgb(244, 204, 96);
    static readonly Color RecommendBack = Color.FromArgb(52, 45, 22);
    static readonly Color RecommendBorder = Color.FromArgb(150, 122, 48);

    Font boldFont = null!;

    CancellationTokenSource? downloadCts;
    string? downloadingId;

    /// <summary>기본 모델을 바꿨는지. MainForm이 엔진을 다시 만들지 판단하는 데 쓴다.</summary>
    public bool Changed { get; private set; }

    /// <summary>설치된 모델 목록이 바뀌었을 때(다운로드·삭제). 첫 실행 창이 '시작' 버튼을 강조하는 데 쓴다.</summary>
    public event Action? InstalledChanged;

    public ModelManagerPage(string baseConfigPath, bool engineRunning, bool firstRun)
    {
        this.baseConfigPath = baseConfigPath;
        this.engineRunning = engineRunning;
        this.firstRun = firstRun;
        BuildUi();
        RefreshList(ModelStore.RecommendedId);
    }

    string? BaseDefaultId => Config.GetBaseValue(baseConfigPath, "model");
    string? SelectedId => list.SelectedItems.Count > 0 ? (string)list.SelectedItems[0].Tag! : null;

    void BuildUi()
    {
        Dock = DockStyle.Fill;
        Font = new Font("Segoe UI", 10);
        BackColor = Color.FromArgb(24, 24, 28);
        ForeColor = Color.Gainsboro;
        boldFont = new Font(Font, FontStyle.Bold);

        var intro = new Label
        {
            Dock = DockStyle.Top, Height = !firstRun && engineRunning ? 58 : 38,
            Padding = new Padding(8, 8, 8, 0), ForeColor = Color.Silver,
            Text = firstRun
                ? "음성 인식에 쓸 Whisper 모델을 하나 이상 내려받아야 합니다. 잘 모르겠다면 아래 추천 모델을 받으세요."
                : "여러 모델을 받아 두고, 프로필 설정에서 프로필별·화자별로 다른 모델을 지정할 수 있습니다."
                  + (engineRunning ? "\n캡처 중에는 삭제와 기본 모델 지정을 할 수 없습니다." : ""),
        };

        BuildRecommendBox();
        var recommendWrap = new Panel { Dock = DockStyle.Top, Height = 92, Padding = new Padding(8, 4, 8, 10) };
        recommendWrap.Controls.Add(recommendBox);

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
        list.Columns.Add("모델", 210);
        list.Columns.Add("크기", 80);
        list.Columns.Add("상태", 120);
        list.Columns.Add("설명", 380);
        list.SelectedIndexChanged += (s, e) => UpdateButtons();
        list.DoubleClick += async (s, e) => { if (downloadBtn.Enabled && SelectedId is { } id) await DownloadAsync(id); };

        progress.Dock = DockStyle.Top; progress.Height = 18;
        progressLabel.Dock = DockStyle.Top; progressLabel.Height = 24;
        progressLabel.Padding = new Padding(4, 4, 0, 0); progressLabel.ForeColor = Color.Silver;

        var buttons = new Panel { Dock = DockStyle.Top, Height = 44 };
        downloadBtn.Text = "다운로드"; downloadBtn.Width = 100; downloadBtn.Height = 30; downloadBtn.Location = new Point(4, 6);
        downloadBtn.Click += async (s, e) => { if (SelectedId is { } id) await DownloadAsync(id); };
        cancelBtn.Text = "취소"; cancelBtn.Width = 80; cancelBtn.Height = 30; cancelBtn.Location = new Point(110, 6);
        cancelBtn.Click += (s, e) => downloadCts?.Cancel();
        deleteBtn.Text = "삭제"; deleteBtn.Width = 80; deleteBtn.Height = 30; deleteBtn.Location = new Point(196, 6);
        deleteBtn.Click += (s, e) => DeleteSelected();
        defaultBtn.Text = "기본 모델로 지정"; defaultBtn.Width = 140; defaultBtn.Height = 30; defaultBtn.Location = new Point(282, 6);
        defaultBtn.Click += (s, e) => { if (SelectedId is { } id) SetDefault(id); };
        buttons.Controls.AddRange([downloadBtn, cancelBtn, deleteBtn, defaultBtn]);

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 90, Padding = new Padding(8, 6, 8, 0), BackColor = Theme.Surface };
        bottom.Controls.Add(buttons);
        bottom.Controls.Add(progressLabel);
        bottom.Controls.Add(progress);

        // Dock은 나중에 추가한 것부터 바깥쪽에 붙는다: 안내문 → 추천 상자 → 목록 순으로 위에서부터 놓인다.
        Controls.Add(list);
        Controls.Add(recommendWrap);
        Controls.Add(intro);
        Controls.Add(bottom);
        UpdateButtons();
    }

    /// <summary>추천 모델 안내 상자: 금색 테두리 안에 모델 이름·크기·이유와 바로 받기 버튼.</summary>
    void BuildRecommendBox()
    {
        var info = ModelStore.Recommended;

        recommendBox.Dock = DockStyle.Fill;
        recommendBox.BackColor = RecommendBack;
        recommendBox.Paint += (s, e) =>
        {
            using var pen = new Pen(RecommendBorder);
            e.Graphics.DrawRectangle(pen, 0, 0, recommendBox.Width - 1, recommendBox.Height - 1);
        };

        recommendTitle.AutoSize = false;
        recommendTitle.AutoEllipsis = true;
        recommendTitle.Location = new Point(14, 10);
        recommendTitle.Height = 26;
        recommendTitle.Font = new Font("Segoe UI", 12, FontStyle.Bold);
        recommendTitle.ForeColor = RecommendText;
        recommendTitle.BackColor = RecommendBack;
        recommendTitle.Text = $"★ 추천 모델   {info.Id}  ({ModelStore.FormatSize(info.SizeBytes)})";

        recommendDesc.AutoSize = false;
        recommendDesc.AutoEllipsis = true;
        recommendDesc.Location = new Point(16, 40);
        recommendDesc.Height = 34;
        recommendDesc.ForeColor = Color.FromArgb(214, 204, 176);
        recommendDesc.BackColor = RecommendBack;
        recommendDesc.Text = "한국어 받아 적기에 가장 정확합니다. 개인·비영리 무료(BatiAI 라이선스)\n" +
                             "한국어가 아닌 말은 large-v3-turbo를 받으세요.";

        recommendBtn.Size = new Size(180, 36);
        recommendBtn.Click += async (s, e) => await OnRecommendClickedAsync();

        recommendBox.Controls.AddRange([recommendTitle, recommendDesc, recommendBtn]);
        recommendBox.Resize += (s, e) => LayoutRecommendBox();
        LayoutRecommendBox();
    }

    void LayoutRecommendBox()
    {
        recommendBtn.Location = new Point(recommendBox.Width - recommendBtn.Width - 14, (recommendBox.Height - recommendBtn.Height) / 2);
        int textWidth = Math.Max(recommendBtn.Left - 24, 100);
        recommendTitle.Width = textWidth;
        recommendDesc.Width = textWidth;
    }

    async Task OnRecommendClickedAsync()
    {
        string id = ModelStore.RecommendedId;
        if (!ModelStore.IsInstalled(id)) await DownloadAsync(id);
        else SetDefault(id);
    }

    /// <summary>추천 상자 버튼: 설치 전이면 "받기", 설치했지만 기본 모델이 아니면 "기본 모델로 지정", 기본이면 완료 표시.</summary>
    void UpdateRecommendButton()
    {
        string id = ModelStore.RecommendedId;
        bool busy = downloadingId is not null;
        bool installed = ModelStore.IsInstalled(id);
        bool isDefault = installed && string.Equals(ModelStore.ResolveDefault(BaseDefaultId), id, StringComparison.OrdinalIgnoreCase);

        if (downloadingId == id)
        {
            recommendBtn.Text = "다운로드 중...";
            recommendBtn.Enabled = false;
        }
        else if (!installed)
        {
            recommendBtn.Text = "★ 추천 모델 받기";
            recommendBtn.Enabled = !busy;
        }
        else if (!isDefault)
        {
            recommendBtn.Text = "기본 모델로 지정";
            recommendBtn.Enabled = !busy && !engineRunning;
        }
        else
        {
            recommendBtn.Text = "✓ 설치됨 (기본)";
            recommendBtn.Enabled = false;
        }
        Theme.StyleButton(recommendBtn, recommendBtn.Enabled ? Theme.ButtonKind.Primary : Theme.ButtonKind.Normal);
    }

    void RefreshList(string? selectId = null)
    {
        selectId ??= SelectedId;
        string? defaultId = ModelStore.ResolveDefault(BaseDefaultId);
        var installed = ModelStore.ListInstalled();

        list.BeginUpdate();
        list.Items.Clear();

        // 추천 모델을 맨 위에 두고, 나머지는 카탈로그 순서, 끝에 직접 넣은 모델.
        var ids = ModelStore.Catalog.Select(m => m.Id)
            .OrderBy(id => id == ModelStore.RecommendedId ? 0 : 1)
            .Concat(installed.Where(id => ModelStore.FindInfo(id) is null));
        foreach (string id in ids)
        {
            var info = ModelStore.FindInfo(id);
            bool recommended = id == ModelStore.RecommendedId;
            bool isInstalled = installed.Contains(id, StringComparer.OrdinalIgnoreCase);
            bool isDefault = isInstalled && string.Equals(id, defaultId, StringComparison.OrdinalIgnoreCase);
            string state = id == downloadingId ? "다운로드 중"
                : isDefault ? "설치됨 (기본)"
                : isInstalled ? "설치됨"
                : recommended ? "★ 추천"
                : "-";
            long size = info?.SizeBytes ?? (ModelStore.GetPath(id) is { } p ? new FileInfo(p).Length : 0);

            var item = new ListViewItem(recommended ? $"★ {id}" : id) { Tag = id, UseItemStyleForSubItems = false };
            item.SubItems.Add(ModelStore.FormatSize(size));
            var stateSub = item.SubItems.Add(state);
            item.SubItems.Add(info?.Description ?? "직접 추가한 모델");
            stateSub.ForeColor = isInstalled ? Color.FromArgb(110, 200, 120) : Color.Gray;

            if (recommended)
            {
                // 추천 줄은 금색 바탕·글자로 칠하고 굵게 써서 다른 줄과 확실히 구분되게 한다.
                foreach (ListViewItem.ListViewSubItem sub in item.SubItems)
                {
                    sub.BackColor = RecommendBack;
                    sub.Font = boldFont;
                    if (sub != stateSub || !isInstalled) sub.ForeColor = RecommendText;
                }
            }
            list.Items.Add(item);

            if (string.Equals(id, selectId, StringComparison.OrdinalIgnoreCase)) item.Selected = true;
        }
        list.EndUpdate();
        UpdateButtons();
        InstalledChanged?.Invoke();
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
        UpdateRecommendButton();
    }

    async Task DownloadAsync(string id)
    {
        if (downloadingId is not null || ModelStore.FindInfo(id) is not { } info) return;

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

    /// <summary>창을 닫아도 되는지. 다운로드 중이면 취소하고 닫을지 묻는다.</summary>
    public bool ConfirmClose()
    {
        if (downloadCts is null) return true;
        if (MessageBox.Show(this, "다운로드 중입니다. 취소하고 닫을까요?", "다운로드 중",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return false;
        downloadCts.Cancel();
        return true;
    }
}
