using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace HearingAid;

/// <summary>앱 전체가 공유하는 어두운 테마. 각 폼은 생성자 끝에서 Theme.Apply(this)만 부르면 되고,
/// 나중에 추가되는 컨트롤(프로필 설정 행 등)도 ControlAdded를 통해 자동으로 같은 스타일이 입혀진다.
/// 색을 직접 지정한 Panel/Label은 건드리지 않는다 — 입력 컨트롤과 버튼, 목록류만 스타일을 바꾼다.</summary>
static class Theme
{
    public static readonly Color Background = Color.FromArgb(24, 24, 28);
    public static readonly Color Surface = Color.FromArgb(32, 32, 38);
    public static readonly Color Control = Color.FromArgb(48, 48, 56);
    public static readonly Color ControlHover = Color.FromArgb(62, 62, 72);
    public static readonly Color Border = Color.FromArgb(66, 66, 76);
    public static readonly Color Text = Color.FromArgb(232, 232, 236);
    public static readonly Color SubText = Color.FromArgb(150, 150, 160);
    public static readonly Color Accent = Color.FromArgb(0, 120, 212);
    public static readonly Color AccentHover = Color.FromArgb(30, 142, 232);
    public static readonly Color Danger = Color.FromArgb(196, 52, 40);
    public static readonly Color DangerHover = Color.FromArgb(218, 72, 58);
    public static readonly Color Success = Color.FromArgb(108, 203, 95);
    public static readonly Color Link = Color.FromArgb(86, 156, 230);

    public static readonly Font Font = new("Segoe UI", 10f);

    public enum ButtonKind { Normal, Primary, Danger }

    /// <summary>ControlAdded 구독을 컨트롤마다 한 번만 하기 위한 표시. 같은 컨트롤을 뗐다 다시 붙여도 핸들러가 쌓이지 않는다.</summary>
    static readonly ConditionalWeakTable<Control, object> hooked = new();

    public static void Apply(Form form)
    {
        form.BackColor = Background;
        form.ForeColor = Text;

        if (form.IsHandleCreated) UseDarkTitleBar(form.Handle);
        else form.HandleCreated += (s, e) => UseDarkTitleBar(form.Handle);

        ApplyTree(form);
    }

    static void ApplyTree(Control c)
    {
        Style(c);
        foreach (Control child in c.Controls) ApplyTree(child);

        if (!hooked.TryGetValue(c, out _))
        {
            hooked.Add(c, new object());
            c.ControlAdded += (s, e) => { if (e.Control is { } added) ApplyTree(added); };
        }
    }

    static void Style(Control c)
    {
        switch (c)
        {
            case Button b when b.Tag is not ButtonKind:
                StyleButton(b, ButtonKind.Normal);
                break;
            case TextBox tb:
                tb.BackColor = Surface;
                tb.ForeColor = Text;
                tb.BorderStyle = BorderStyle.FixedSingle;
                if (tb.Multiline) UseDarkScrollBars(tb);
                break;
            case ComboBox cb:
                cb.FlatStyle = FlatStyle.Flat;
                cb.BackColor = Surface;
                cb.ForeColor = Text;
                UseDarkScrollBars(cb, "DarkMode_CFD");
                BlockWheelValueChange(cb, () => cb.DroppedDown);
                break;
            case NumericUpDown nud:
                nud.BackColor = Surface;
                nud.ForeColor = Text;
                nud.BorderStyle = BorderStyle.FixedSingle;
                BlockWheelValueChange(nud);
                break;
            case CheckBox chk:
                chk.FlatStyle = FlatStyle.Flat;
                chk.FlatAppearance.BorderColor = Border;
                chk.FlatAppearance.CheckedBackColor = Accent;
                chk.Cursor = Cursors.Hand;
                break;
            case LinkLabel link:
                link.LinkColor = Link;
                link.ActiveLinkColor = AccentHover;
                break;
            case ListBox lb:
                lb.BorderStyle = BorderStyle.None;
                lb.ForeColor = Text;
                UseDarkScrollBars(lb);
                break;
            case ListView lv:
                StyleListView(lv);
                break;
            case DataGridView grid:
                StyleGrid(grid);
                break;
            case ScrollableControl sc when sc.AutoScroll:
                UseDarkScrollBars(sc);
                break;
        }
    }

    static readonly ConditionalWeakTable<Control, object> wheelHooked = new();

    /// <summary>설정 화면을 휠로 스크롤하다 마우스가 콤보박스·숫자 상자 위를 지나가면 값이 바뀌어 버린다.
    /// 휠로는 값을 바꾸지 않고, 대신 그 컨트롤을 담은 스크롤 영역을 스크롤한다. allow가 true면(예: 목록이 펼쳐짐) 그대로 둔다.</summary>
    static void BlockWheelValueChange(Control c, Func<bool>? allow = null)
    {
        if (wheelHooked.TryGetValue(c, out _)) return;
        wheelHooked.Add(c, new object());

        c.MouseWheel += (s, e) =>
        {
            if (allow?.Invoke() == true) return;
            if (e is HandledMouseEventArgs handled) handled.Handled = true;

            for (Control? p = c.Parent; p is not null; p = p.Parent)
            {
                if (p is ScrollableControl sc && sc.AutoScroll && sc.VerticalScroll.Visible)
                {
                    // AutoScrollPosition은 읽을 때 음수, 쓸 때 양수로 다룬다.
                    Point pos = sc.AutoScrollPosition;
                    sc.AutoScrollPosition = new Point(-pos.X, Math.Max(0, -pos.Y - e.Delta));
                    break;
                }
            }
        };
    }

    static readonly ConditionalWeakTable<Button, object> enabledHooked = new();

    public static void StyleButton(Button b, ButtonKind kind)
    {
        b.Tag = kind;
        b.FlatStyle = FlatStyle.Flat;
        b.UseVisualStyleBackColor = false;
        b.FlatAppearance.BorderColor = Border;
        b.Cursor = Cursors.Hand;
        ApplyButtonColors(b);

        // Flat 버튼은 비활성이어도 배경색이 그대로라 강조 버튼이 눌러질 것처럼 보인다. 비활성일 땐 회색으로 바꾼다.
        if (!enabledHooked.TryGetValue(b, out _))
        {
            enabledHooked.Add(b, new object());
            b.EnabledChanged += (s, e) => ApplyButtonColors(b);
        }
    }

    static void ApplyButtonColors(Button b)
    {
        var kind = b.Tag is ButtonKind k ? k : ButtonKind.Normal;
        (Color back, Color hover) = !b.Enabled ? (Control, Control) : kind switch
        {
            ButtonKind.Primary => (Accent, AccentHover),
            ButtonKind.Danger => (Danger, DangerHover),
            _ => (Control, ControlHover),
        };

        b.BackColor = back;
        b.ForeColor = !b.Enabled ? SubText : kind == ButtonKind.Normal ? Text : Color.White;
        b.FlatAppearance.BorderSize = kind == ButtonKind.Normal || !b.Enabled ? 1 : 0;
        b.FlatAppearance.MouseOverBackColor = hover;
        b.FlatAppearance.MouseDownBackColor = back;
    }

    /// <summary>Details 보기의 열 머리글은 시스템이 밝게 그리므로 직접 어둡게 그린다. 항목은 기본 그리기를 그대로 쓴다.</summary>
    static void StyleListView(ListView lv)
    {
        lv.BorderStyle = BorderStyle.None;
        lv.ForeColor = Text;
        UseDarkScrollBars(lv);
        SetDoubleBuffered(lv); // OwnerDraw 시 스크롤·갱신 깜빡임 방지

        if (lv.View != View.Details || lv.OwnerDraw) return;
        lv.OwnerDraw = true;
        lv.DrawItem += (s, e) => { };
        lv.DrawSubItem += (s, e) => DrawCell(e, lv);
        lv.DrawColumnHeader += (s, e) => DrawHeader(e, lv.Font);
    }

    public static readonly Color SelectedRow = Color.FromArgb(38, 58, 88);
    static readonly Color RowLine = Color.FromArgb(34, 34, 40);

    /// <summary>Details 보기의 칸 하나를 그린다. 시스템 기본 그리기(DrawDefault)는 한글 글꼴 대체 과정에서 일부 단어만
    /// 다른 색으로 그려지는 문제가 있어 직접 그린다. 항목/하위 항목의 ForeColor와 Font를 그대로 따른다.</summary>
    public static void DrawCell(DrawListViewSubItemEventArgs e, ListView lv)
    {
        if (e.Item is null || e.SubItem is null) return;
        // 첫 열의 e.Bounds는 행 전체 폭으로 오므로 열 폭으로 잘라 쓴다.
        var bounds = e.ColumnIndex == 0
            ? new Rectangle(e.Bounds.X, e.Bounds.Y, lv.Columns[0].Width, e.Bounds.Height)
            : e.Bounds;
        if (bounds.Width <= 0) return;

        using (var back = new SolidBrush(e.Item.Selected ? SelectedRow : lv.BackColor))
            e.Graphics.FillRectangle(back, bounds);
        using (var line = new Pen(RowLine))
            e.Graphics.DrawLine(line, bounds.Left, bounds.Bottom - 1, bounds.Right, bounds.Bottom - 1);

        bool useItemStyle = e.Item.UseItemStyleForSubItems;
        Font font = (useItemStyle ? e.Item.Font : e.SubItem.Font) ?? lv.Font;
        Color color = useItemStyle ? e.Item.ForeColor : e.SubItem.ForeColor;
        TextRenderer.DrawText(e.Graphics, e.SubItem.Text, font, Rectangle.Inflate(bounds, -10, 0), color,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }

    public static void DrawHeader(DrawListViewColumnHeaderEventArgs e, Font font)
    {
        using var back = new SolidBrush(Surface);
        e.Graphics.FillRectangle(back, e.Bounds);
        using var line = new Pen(Border);
        e.Graphics.DrawLine(line, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);

        var textBounds = Rectangle.Inflate(e.Bounds, -8, 0);
        TextRenderer.DrawText(e.Graphics, e.Header?.Text, font, textBounds, SubText,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
    }

    static void StyleGrid(DataGridView grid)
    {
        grid.BorderStyle = BorderStyle.FixedSingle;
        grid.BackgroundColor = Surface;
        grid.GridColor = Border;
        grid.EnableHeadersVisualStyles = false;
        grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
        grid.ColumnHeadersDefaultCellStyle.BackColor = Control;
        grid.ColumnHeadersDefaultCellStyle.ForeColor = SubText;
        grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Control;
        grid.DefaultCellStyle.BackColor = Surface;
        grid.DefaultCellStyle.ForeColor = Text;
        grid.DefaultCellStyle.SelectionBackColor = Accent;
        grid.DefaultCellStyle.SelectionForeColor = Color.White;
        grid.RowHeadersDefaultCellStyle.BackColor = Control;
        UseDarkScrollBars(grid);
    }

    static void SetDoubleBuffered(Control c) =>
        typeof(Control).GetProperty("DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(c, true);

    #region Win32

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    static extern int SetWindowTheme(IntPtr hwnd, string? subAppName, string? subIdList);

    /// <summary>Windows 10 1809+/11의 어두운 제목 표시줄. 지원하지 않는 OS에서는 조용히 무시된다.</summary>
    static void UseDarkTitleBar(IntPtr hwnd)
    {
        const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        int on = 1;
        try { DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref on, sizeof(int)); } catch { }
    }

    /// <summary>스크롤 막대 등 시스템이 그리는 부분을 어두운 테마로 바꾼다(Windows 10 1809+).</summary>
    static void UseDarkScrollBars(Control c, string theme = "DarkMode_Explorer")
    {
        void apply() { try { SetWindowTheme(c.Handle, theme, null); } catch { } }
        if (c.IsHandleCreated) apply();
        else c.HandleCreated += (s, e) => apply();
    }

    #endregion
}
