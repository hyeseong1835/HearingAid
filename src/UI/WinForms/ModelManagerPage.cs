using HearingAid.Core;
using HearingAid.Core.Stt;

namespace HearingAid;

/// <summary>설정 창의 '모델' 페이지. Whisper 모델 다운로드/삭제와 기본 모델(appsettings.json의 model) 지정을 한다.
/// 첫 실행(설치된 모델 없음) 때는 ModelManagerDialog가 이 페이지를 안내문과 함께 띄운다.
/// 모델 이름만으로는 무엇을 받아야 할지 알기 어려우므로, 추천 모델은 위쪽 카드에서 이유와 함께 보여주고(바로 받기 버튼),
/// 목록에서는 맨 위 줄에 별표만 붙인다.</summary>
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

    // 추천 모델 카드
    readonly Panel recommendBox = new();
    readonly Label recommendTag = new();
    readonly Label recommendName = new();
    readonly Label recommendDesc = new();
    readonly Button recommendBtn = new();

    /// <summary>추천 표시 색. 파란 강조색(선택·주 버튼)과 겹치지 않게 금색을 쓰되, 카드 바탕은 다른 화면과 같은 Surface로 두고
    /// 왼쪽 띠·별표·'추천' 글자에만 쓴다.</summary>
    static readonly Color RecommendText = Color.FromArgb(244, 204, 96);
    static readonly Color StatusColor = Color.Silver;
    static readonly Color ErrorColor = Color.FromArgb(240, 120, 110);

    const int SizeColumn = 1;
    const int StateColumn = 2;
    const int DescColumn = 3;

    Font boldFont = null!;
    Font nameFont = null!;

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

    static bool SameId(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    void BuildUi()
    {
        Dock = DockStyle.Fill;
        Font = new Font("Segoe UI", 10);
        BackColor = Color.FromArgb(24, 24, 28);
        ForeColor = Color.Gainsboro;
        boldFont = new Font(Font, FontStyle.Bold);
        nameFont = new Font("Segoe UI", 12, FontStyle.Bold);

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
        var recommendWrap = new Panel { Dock = DockStyle.Top, Height = 100, Padding = new Padding(8, 4, 8, 12) };
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
        progressLabel.Padding = new Padding(4, 4, 0, 0); progressLabel.ForeColor = StatusColor;
        progressLabel.AutoEllipsis = true;

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

        // Dock은 나중에 추가한 것부터 바깥쪽에 붙는다: 안내문 → 추천 카드 → 목록 순으로 위에서부터 놓인다.
        Controls.Add(list);
        Controls.Add(recommendWrap);
        Controls.Add(intro);
        Controls.Add(bottom);
        UpdateButtons();
    }

    /// <summary>추천 모델 카드: 다른 화면과 같은 어두운 바탕에 왼쪽 금색 띠, "★ 추천" 꼬리표와 모델 이름·크기, 이유 한두 줄,
    /// 오른쪽에 바로 받기 버튼. 예전처럼 상자 전체를 금색으로 칠하면 파란 주 버튼·선택 색과 부딪혀 어수선했다.</summary>
    void BuildRecommendBox()
    {
        var info = ModelStore.Recommended;

        recommendBox.Dock = DockStyle.Fill;
        recommendBox.BackColor = Theme.Surface;
        recommendBox.Paint += (s, e) =>
        {
            using var border = new Pen(Theme.Border);
            e.Graphics.DrawRectangle(border, 0, 0, recommendBox.Width - 1, recommendBox.Height - 1);
            using var bar = new SolidBrush(RecommendText);
            e.Graphics.FillRectangle(bar, 0, 0, 4, recommendBox.Height);
        };

        recommendTag.AutoSize = true;
        recommendTag.Location = new Point(16, 14);
        recommendTag.Font = boldFont;
        recommendTag.ForeColor = RecommendText;
        recommendTag.BackColor = Theme.Surface;
        recommendTag.Text = "★ 추천";

        recommendName.AutoSize = false;
        recommendName.AutoEllipsis = true;
        recommendName.Height = 26;
        recommendName.TextAlign = ContentAlignment.MiddleLeft;
        recommendName.Font = nameFont;
        recommendName.ForeColor = Theme.Text;
        recommendName.BackColor = Theme.Surface;
        recommendName.Text = $"{info.Id}   {ModelStore.FormatSize(info.SizeBytes)}";

        recommendDesc.AutoSize = false;
        recommendDesc.AutoEllipsis = true;
        recommendDesc.Location = new Point(16, 42);
        recommendDesc.Height = 36;
        recommendDesc.ForeColor = Theme.SubText;
        recommendDesc.BackColor = Theme.Surface;
        recommendDesc.Text = "한국어 받아 적기에 가장 정확합니다. 개인·비영리 무료(BatiAI 라이선스)\n" +
                             "한국어가 아닌 말은 large-v3-turbo를 받으세요.";

        recommendBtn.Size = new Size(170, 34);
        recommendBtn.Click += async (s, e) => await OnRecommendClickedAsync();

        recommendBox.Controls.AddRange([recommendTag, recommendName, recommendDesc, recommendBtn]);
        recommendBox.Resize += (s, e) => LayoutRecommendBox();
        LayoutRecommendBox();
    }

    void LayoutRecommendBox()
    {
        recommendBtn.Location = new Point(recommendBox.Width - recommendBtn.Width - 16, (recommendBox.Height - recommendBtn.Height) / 2);
        int right = recommendBtn.Left - 16;
        recommendName.Location = new Point(recommendTag.Right + 8, 10);
        recommendName.Width = Math.Max(right - recommendName.Left, 60);
        recommendDesc.Width = Math.Max(right - recommendDesc.Left, 60);
    }

    async Task OnRecommendClickedAsync()
    {
        string id = ModelStore.RecommendedId;
        if (!ModelStore.IsInstalled(id)) await DownloadAsync(id);
        else SetDefault(id);
    }

    /// <summary>카드 버튼: 설치 전이면 "받기", 설치했지만 기본 모델이 아니면 "기본 모델로 지정", 기본이면 사용 중 표시.</summary>
    void UpdateRecommendButton(string? defaultId)
    {
        string id = ModelStore.RecommendedId;
        bool busy = downloadingId is not null;
        bool installed = ModelStore.IsInstalled(id);
        bool isDefault = installed && SameId(defaultId, id);

        if (SameId(downloadingId, id))
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
            recommendBtn.Text = "✓ 기본 모델로 사용 중";
            recommendBtn.Enabled = false;
        }
        Theme.StyleButton(recommendBtn, recommendBtn.Enabled ? Theme.ButtonKind.Primary : Theme.ButtonKind.Normal);
    }

    /// <summary>목록을 지금 상태(설치 여부·기본 모델·다운로드 중)에 맞게 다시 채운다.
    /// 줄 구성이 같으면 글자와 색만 바꾼다 — 줄을 모두 지우고 다시 만들면 선택·스크롤이 흔들리고,
    /// 직접 그리는 목록이 새 글자로 다시 그려지지 않는 때가 있어 "기본 모델로 지정해도 상태가 안 바뀌는" 것처럼 보였다.</summary>
    void RefreshList(string? selectId = null)
    {
        selectId ??= SelectedId;
        string? defaultId = ModelStore.ResolveDefault(BaseDefaultId);
        var installed = ModelStore.ListInstalled();

        // 추천 모델을 맨 위에 두고, 나머지는 카탈로그 순서, 끝에 직접 넣은 모델.
        var ids = ModelStore.Catalog.Select(m => m.Id)
            .OrderBy(id => SameId(id, ModelStore.RecommendedId) ? 0 : 1)
            .Concat(installed.Where(id => ModelStore.FindInfo(id) is null))
            .ToList();

        bool sameRows = list.Items.Count == ids.Count
            && list.Items.Cast<ListViewItem>().Select(it => (string)it.Tag!).SequenceEqual(ids, StringComparer.OrdinalIgnoreCase);

        list.BeginUpdate();
        if (!sameRows)
        {
            list.Items.Clear();
            foreach (string id in ids)
            {
                var item = new ListViewItem { Tag = id, UseItemStyleForSubItems = false };
                item.SubItems.Add(""); // 크기
                item.SubItems.Add(""); // 상태
                item.SubItems.Add(""); // 설명
                list.Items.Add(item);
            }
        }
        foreach (ListViewItem item in list.Items) FillRow(item, installed, defaultId);
        list.EndUpdate();

        var selectItem = list.Items.Cast<ListViewItem>().FirstOrDefault(it => SameId((string)it.Tag!, selectId));
        if (selectItem is not null)
        {
            selectItem.Selected = true;
            selectItem.Focused = true;
            selectItem.EnsureVisible();
        }
        list.Invalidate();

        UpdateButtons(defaultId);
        InstalledChanged?.Invoke();
    }

    void FillRow(ListViewItem item, IReadOnlyList<string> installed, string? defaultId)
    {
        string id = (string)item.Tag!;
        var info = ModelStore.FindInfo(id);
        bool recommended = SameId(id, ModelStore.RecommendedId);
        bool downloading = SameId(id, downloadingId);
        bool isInstalled = installed.Contains(id, StringComparer.OrdinalIgnoreCase);
        bool isDefault = isInstalled && SameId(id, defaultId);
        long size = info?.SizeBytes ?? (ModelStore.GetPath(id) is { } p ? new FileInfo(p).Length : 0);

        item.Text = recommended ? $"★ {id}" : id;
        item.Font = recommended ? boldFont : list.Font;
        item.ForeColor = recommended ? RecommendText : list.ForeColor;

        item.SubItems[SizeColumn].Text = ModelStore.FormatSize(size);
        item.SubItems[DescColumn].Text = info?.Description ?? "직접 추가한 모델";

        var state = item.SubItems[StateColumn];
        (state.Text, state.ForeColor) =
            downloading ? ("다운로드 중", Theme.Link)
            : isDefault ? ("설치됨 (기본)", Theme.Success)
            : isInstalled ? ("설치됨", Theme.Success)
            : recommended ? ("추천", RecommendText)
            : ("-", Theme.SubText);
    }

    void UpdateButtons(string? defaultId = null)
    {
        defaultId ??= ModelStore.ResolveDefault(BaseDefaultId);
        bool busy = downloadingId is not null;
        string? sel = SelectedId;
        bool installed = sel is not null && ModelStore.IsInstalled(sel);
        bool isDefault = installed && SameId(sel, defaultId);

        downloadBtn.Enabled = !busy && sel is not null && !installed && ModelStore.FindInfo(sel) is not null;
        cancelBtn.Enabled = busy;
        deleteBtn.Enabled = !busy && !engineRunning && installed;
        // 이미 기본인 모델은 눌러도 바뀔 것이 없으므로, 그 사실을 버튼 글자로 보여주고 비활성으로 둔다.
        defaultBtn.Text = isDefault ? "✓ 기본 모델" : "기본 모델로 지정";
        defaultBtn.Enabled = !busy && !engineRunning && installed && !isDefault;
        progress.Visible = busy;
        UpdateRecommendButton(defaultId);
    }

    void SetStatus(string text, bool error = false)
    {
        progressLabel.Text = text;
        progressLabel.ForeColor = error ? ErrorColor : StatusColor;
    }

    async Task DownloadAsync(string id)
    {
        if (downloadingId is not null || ModelStore.FindInfo(id) is not { } info) return;

        downloadingId = id;
        downloadCts = new CancellationTokenSource();
        progress.Value = 0;
        SetStatus($"{id} 다운로드 준비 중...");
        RefreshList(id);

        var reporter = new Progress<(long Done, long Total)>(p =>
        {
            if (IsDisposed) return;
            progress.Value = p.Total > 0 ? (int)Math.Min(100, p.Done * 100 / p.Total) : 0;
            SetStatus($"{id}  {ModelStore.FormatSize(p.Done)} / {ModelStore.FormatSize(p.Total)}");
        });

        bool completed = false;
        try
        {
            await ModelStore.DownloadAsync(info, reporter, downloadCts.Token);
            completed = true;
        }
        catch (OperationCanceledException)
        {
            SetStatus($"{id} 다운로드 취소됨");
        }
        catch (Exception ex)
        {
            SetStatus($"{id} 다운로드 실패: {ex.Message}", error: true);
        }
        finally
        {
            downloadCts?.Dispose();
            downloadCts = null;
            downloadingId = null;
        }
        if (IsDisposed) return;

        if (completed)
        {
            SetStatus($"{id} 다운로드 완료");
            // 기본 모델이 아직 정해지지 않았다면 처음 받은 모델을 기본으로 삼는다.
            // 다운로드와는 별개의 일이라 try 밖에서 한다 — 설정 파일 저장 실패를 "다운로드 실패"로 보이지 않게.
            if (string.IsNullOrEmpty(BaseDefaultId) && !engineRunning && TrySetDefault(id))
                SetStatus($"{id} 다운로드 완료. 기본 모델로 지정했습니다.");
        }
        RefreshList(id);
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
        if (SameId(BaseDefaultId, id)) TrySetDefault(null);
        SetStatus($"{id} 삭제됨");
        RefreshList();
    }

    void SetDefault(string id)
    {
        if (TrySetDefault(id)) SetStatus($"기본 모델: {id}");
        RefreshList(id);
    }

    /// <summary>appsettings.json의 model을 바꾸고, 바로 다시 읽어 실제로 바뀌었는지 확인한다.
    /// 쓰기가 실패하거나(권한, 잠김) 읽은 값이 다르면 이유를 아래 상태 줄에 빨갛게 보여준다 — 예전에는 아무 표시 없이 지나가
    /// 버튼이 안 먹는 것처럼 보였다.</summary>
    bool TrySetDefault(string? id)
    {
        try
        {
            Config.SetDefaultModel(baseConfigPath, id);
            string? saved = BaseDefaultId;
            bool ok = id is null ? string.IsNullOrEmpty(saved) : SameId(saved, id);
            if (!ok) throw new IOException($"저장한 뒤 다시 읽은 값이 다릅니다 ({saved ?? "없음"}).");
            Changed = true;
            return true;
        }
        catch (Exception ex)
        {
            SetStatus($"기본 모델을 appsettings.json에 저장하지 못했습니다: {ex.Message}", error: true);
            return false;
        }
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
