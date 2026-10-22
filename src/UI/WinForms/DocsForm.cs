using System.Diagnostics;
using System.Reflection;
using Markdig;
using Markdig.Renderers;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace HearingAid;

/// <summary>앱에 내장된 사용 설명서(docs/app/*.md, docs/discord.md)를 보여주는 창.
/// 문서는 EmbeddedResource로 실행 파일에 들어 있어 배포 폴더와 따로 놀지 않고, GitHub에서도 같은 파일을 읽을 수 있다.
/// Markdown은 Markdig로 HTML로 바꿔 WebBrowser에 띄운다.</summary>
public class DocsForm : Form
{
    /// <summary>왼쪽 목록에 보일 순서. 이름은 리소스 파일 이름(확장자 제외)이고, 제목은 문서의 첫 # 제목을 쓴다.</summary>
    static readonly string[] Order = ["getting-started", "audio-sources", "models", "discord", "troubleshooting"];

    static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UsePipeTables()
        .UseAutoIdentifiers(Markdig.Extensions.AutoIdentifiers.AutoIdentifierOptions.GitHub)
        .UseEmphasisExtras()
        .Build();

    static DocsForm? instance;

    readonly ListBox nav = new();
    readonly WebBrowser browser = new();
    readonly List<(string Name, string Title, string Markdown)> docs = LoadDocs();
    string? currentName;

    /// <summary>문서 창을 연다. 이미 열려 있으면 그 창을 앞으로 가져온다.</summary>
    public static void Open(IWin32Window? owner, string name = "getting-started", string? anchor = null)
    {
        if (instance is null || instance.IsDisposed)
        {
            instance = new DocsForm();
            instance.Show(owner);
        }
        else
        {
            if (instance.WindowState == FormWindowState.Minimized) instance.WindowState = FormWindowState.Normal;
            instance.Activate();
        }
        instance.ShowDoc(name, anchor);
    }

    DocsForm()
    {
        Text = "HearingAid 문서";
        Width = 1000; Height = 720;
        MinimumSize = new Size(700, 400);
        StartPosition = FormStartPosition.CenterParent;
        Font = Theme.Font;
        KeyPreview = true;
        KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) Close(); };

        nav.Dock = DockStyle.Fill;
        nav.BackColor = Theme.Surface;
        nav.DrawMode = DrawMode.OwnerDrawFixed;
        nav.ItemHeight = 36;
        nav.IntegralHeight = false;
        nav.DrawItem += DrawNavItem;
        foreach (var doc in docs) nav.Items.Add(doc.Title);
        nav.SelectedIndexChanged += (s, e) =>
        {
            if (nav.SelectedIndex >= 0 && docs[nav.SelectedIndex].Name != currentName)
                ShowDoc(docs[nav.SelectedIndex].Name);
        };

        var navPanel = new Panel { Dock = DockStyle.Left, Width = 200, BackColor = Theme.Surface, Padding = new Padding(0, 10, 0, 0) };
        navPanel.Controls.Add(nav);
        var navLine = new Panel { Dock = DockStyle.Left, Width = 1, BackColor = Theme.Border };

        browser.Dock = DockStyle.Fill;
        browser.AllowWebBrowserDrop = false;
        browser.IsWebBrowserContextMenuEnabled = false;
        browser.WebBrowserShortcutsEnabled = true; // Ctrl+C, Ctrl+F
        browser.ScriptErrorsSuppressed = true;
        browser.Navigating += OnNavigating;
        browser.DocumentCompleted += (s, e) => ScrollToPendingAnchor();

        Controls.Add(browser);
        Controls.Add(navLine);
        Controls.Add(navPanel);
        Theme.Apply(this);
    }

    static List<(string, string, string)> LoadDocs()
    {
        var asm = Assembly.GetExecutingAssembly();
        var result = new List<(string, string, string)>();
        foreach (string name in Order)
        {
            using var stream = asm.GetManifestResourceStream($"docs/{name}.md");
            if (stream is null) continue;
            string md = new StreamReader(stream).ReadToEnd();
            string title = md.Split('\n').FirstOrDefault(l => l.StartsWith("# "))?[2..].Trim() ?? name;
            result.Add((name, title, md));
        }
        return result;
    }

    string? pendingAnchor;

    void ShowDoc(string name, string? anchor = null)
    {
        int index = docs.FindIndex(d => d.Name == name);
        if (index < 0) return;

        if (name == currentName)
        {
            ScrollTo(anchor);
            return;
        }

        currentName = name;
        pendingAnchor = anchor;
        nav.SelectedIndex = index;
        browser.DocumentText = BuildHtml(docs[index].Markdown);
    }

    void ScrollToPendingAnchor()
    {
        if (pendingAnchor is null) return;
        ScrollTo(pendingAnchor);
        pendingAnchor = null;
    }

    void ScrollTo(string? anchor)
    {
        if (browser.Document is not { } doc) return;
        if (string.IsNullOrEmpty(anchor)) { doc.Window?.ScrollTo(0, 0); return; }
        doc.GetElementById(anchor)?.ScrollIntoView(true);
    }

    /// <summary>문서 안의 링크는 내부 주소(hearingaid://doc/이름/앵커)로 바꿔 두었으므로 여기서 가로채 처리한다.
    /// 웹 주소는 기본 브라우저로 연다. DocumentText를 바꿀 때의 about:blank 이동만 통과시킨다.</summary>
    void OnNavigating(object? sender, WebBrowserNavigatingEventArgs e)
    {
        Uri url = e.Url!;
        if (url.Scheme == "about") return;
        e.Cancel = true;

        if (url.Scheme == LinkScheme)
        {
            string[] parts = url.AbsolutePath.Trim('/').Split('/', 2);
            string name = Uri.UnescapeDataString(parts[0]);
            string? anchor = parts.Length > 1 && parts[1].Length > 0 ? Uri.UnescapeDataString(parts[1]) : null;
            ShowDoc(name == CurrentDoc ? currentName! : name, anchor);
        }
        else if (url.Scheme is "http" or "https")
        {
            try { Process.Start(new ProcessStartInfo(url.AbsoluteUri) { UseShellExecute = true }); } catch { }
        }
    }

    const string LinkScheme = "hearingaid";

    const string CurrentDoc = "-";

    /// <summary>문서 링크를 내부 주소로 바꾼다. "models.md", "../discord.md#문제-해결", "#설정-항목" 모두
    /// hearingaid://doc/이름/앵커 형태가 된다(같은 문서 안의 링크는 이름 자리에 "-"). 웹 주소는 그대로 둔다.</summary>
    static string RewriteLink(string url)
    {
        if (url.Contains("://") || url.StartsWith("mailto:")) return url;

        string path = url, anchor = "";
        int hash = url.IndexOf('#');
        if (hash >= 0) { path = url[..hash]; anchor = url[(hash + 1)..]; }

        string name = path.Length == 0 ? CurrentDoc : Path.GetFileNameWithoutExtension(path);
        return $"{LinkScheme}://doc/{Uri.EscapeDataString(name)}/{Uri.EscapeDataString(anchor)}";
    }

    string BuildHtml(string markdown)
    {
        MarkdownDocument doc = Markdown.Parse(markdown, Pipeline);
        foreach (var link in doc.Descendants<LinkInline>())
            if (!link.IsImage && link.Url is { } url) link.Url = RewriteLink(url);

        using var writer = new StringWriter();
        var renderer = new HtmlRenderer(writer);
        Pipeline.Setup(renderer);
        renderer.Render(doc);

        return $"""
            <!DOCTYPE html>
            <html><head>
            <meta charset="utf-8">
            <meta http-equiv="X-UA-Compatible" content="IE=edge">
            <style>{Css}</style>
            </head><body><article>{writer}</article></body></html>
            """;
    }

    static string Hex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    // WebBrowser는 IE 엔진이라 CSS 변수를 못 쓴다. 색은 Theme 값을 그대로 넣는다.
    static readonly string Css = $$"""
        html { scrollbar-base-color: {{Hex(Theme.Control)}}; scrollbar-arrow-color: {{Hex(Theme.SubText)}};
               scrollbar-track-color: {{Hex(Theme.Background)}}; }
        body { margin: 0; background: {{Hex(Theme.Background)}}; color: {{Hex(Theme.Text)}};
               font-family: 'Segoe UI', 'Malgun Gothic', sans-serif; font-size: 15px; line-height: 1.65; }
        article { max-width: 760px; padding: 20px 36px 60px; }
        h1 { font-size: 26px; margin: 4px 0 16px; padding-bottom: 10px; border-bottom: 1px solid {{Hex(Theme.Border)}}; }
        h2 { font-size: 20px; margin: 30px 0 10px; }
        h3 { font-size: 16px; margin: 22px 0 8px; }
        p, ul, ol, table, blockquote { margin: 0 0 12px; }
        ul, ol { padding-left: 24px; }
        li { margin: 3px 0; }
        a { color: {{Hex(Theme.Link)}}; text-decoration: none; }
        a:hover { text-decoration: underline; }
        strong { color: #ffffff; }
        code { font-family: Consolas, monospace; font-size: 13px; background: {{Hex(Theme.Control)}};
               padding: 1px 5px; border-radius: 3px; }
        pre { background: {{Hex(Theme.Surface)}}; padding: 10px 14px; border: 1px solid {{Hex(Theme.Border)}}; overflow-x: auto; }
        pre code { background: transparent; padding: 0; }
        table { border-collapse: collapse; }
        th, td { border: 1px solid {{Hex(Theme.Border)}}; padding: 6px 10px; text-align: left; vertical-align: top; }
        th { background: {{Hex(Theme.Surface)}}; color: {{Hex(Theme.SubText)}}; font-weight: 600; }
        blockquote { border-left: 3px solid {{Hex(Theme.Accent)}}; background: {{Hex(Theme.Surface)}};
                     padding: 8px 14px; margin-left: 0; }
        blockquote p { margin: 4px 0; }
        hr { border: 0; border-top: 1px solid {{Hex(Theme.Border)}}; margin: 26px 0; }
        """;

    void DrawNavItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0) return;
        bool selected = (e.State & DrawItemState.Selected) != 0;
        using (var back = new SolidBrush(selected ? Theme.SelectedRow : Theme.Surface))
            e.Graphics.FillRectangle(back, e.Bounds);
        if (selected)
            using (var bar = new SolidBrush(Theme.Accent))
                e.Graphics.FillRectangle(bar, e.Bounds.X, e.Bounds.Y, 3, e.Bounds.Height);

        TextRenderer.DrawText(e.Graphics, nav.Items[e.Index].ToString(), Theme.Font,
            new Rectangle(e.Bounds.X + 16, e.Bounds.Y, e.Bounds.Width - 20, e.Bounds.Height),
            selected ? Color.White : Theme.Text,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        instance = null;
        base.OnFormClosed(e);
    }
}
