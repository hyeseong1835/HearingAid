using HearingAid.Core;

namespace HearingAid;

/// <summary>설정 창. VSCode 설정처럼 왼쪽 목록에서 고른 페이지 하나를 오른쪽에 보여준다.
/// 프로필 편집·모델 관리·업데이트 설정을 이 한 창에 모았다. 각 페이지는 처음 열 때 만든다.</summary>
public class SettingsDialog : Form
{
    public enum Page { Profiles, Models, Updates }

    static readonly (Page Id, string Title)[] Pages =
    [
        (Page.Profiles, "프로필"),
        (Page.Models, "모델"),
        (Page.Updates, "업데이트"),
    ];

    readonly ITranscriber engine;
    readonly string baseConfigPath;
    readonly string profilesDir;
    readonly string? initialProfile;
    readonly bool startWithNewProfile;

    readonly ListBox nav = new();
    readonly Panel host = new();
    Page current;

    ProfileSettingsPage? profilePage;
    ModelManagerPage? modelPage;
    UpdateSettingsPage? updatePage;

    /// <summary>프로필 페이지에서 "이 프로필 사용"으로 새로 만든 엔진. MainForm이 창을 닫은 뒤 교체한다.</summary>
    public ITranscriber? NewEngine => profilePage?.NewEngine;
    /// <summary>기본 모델을 바꿨는지(엔진을 다시 만들어야 하는지).</summary>
    public bool ModelsChanged => modelPage?.Changed == true;
    /// <summary>업데이트 채널을 바꿨는지(새 채널로 다시 확인해야 하는지).</summary>
    public bool ChannelChanged => updatePage?.ChannelChanged == true;

    public SettingsDialog(ITranscriber engine, string baseConfigPath, string profilesDir,
        Page start = Page.Profiles, string? profileName = null, bool newProfile = false)
    {
        this.engine = engine;
        this.baseConfigPath = baseConfigPath;
        this.profilesDir = profilesDir;
        initialProfile = profileName;
        startWithNewProfile = newProfile;

        Text = "설정";
        ClientSize = new Size(1020, 700);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false;
        Font = Theme.Font;
        KeyPreview = true;
        KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) Close(); };

        nav.Dock = DockStyle.Fill;
        nav.BackColor = Theme.Surface;
        nav.BorderStyle = BorderStyle.None;
        nav.IntegralHeight = false;
        nav.DrawMode = DrawMode.OwnerDrawFixed;
        nav.ItemHeight = 38;
        foreach (var p in Pages) nav.Items.Add(p.Title);
        nav.DrawItem += DrawNavItem;
        nav.SelectedIndexChanged += (s, e) =>
        {
            if (nav.SelectedIndex >= 0) ShowPage(Pages[nav.SelectedIndex].Id);
        };

        var navBar = new Panel { Dock = DockStyle.Left, Width = 160, BackColor = Theme.Surface, Padding = new Padding(0, 12, 0, 0) };
        var navTitle = new Label
        {
            Text = "설정", Dock = DockStyle.Top, Height = 36, Padding = new Padding(16, 4, 0, 0),
            Font = new Font("Segoe UI", 12, FontStyle.Bold), ForeColor = Theme.Text,
        };
        navBar.Controls.Add(nav);
        navBar.Controls.Add(navTitle);

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 48, BackColor = Theme.Surface };
        var closeBtn = new Button { Text = "닫기", Size = new Size(80, 30), Location = new Point(ClientSize.Width - 96, 9) };
        closeBtn.Click += (s, e) => Close();
        bottom.Controls.Add(closeBtn);

        host.Dock = DockStyle.Fill;

        Controls.Add(host);
        Controls.Add(new Panel { Dock = DockStyle.Left, Width = 1, BackColor = Theme.Border });
        Controls.Add(navBar);
        Controls.Add(new Panel { Dock = DockStyle.Bottom, Height = 1, BackColor = Theme.Border });
        Controls.Add(bottom);
        Theme.Apply(this);

        nav.SelectedIndex = Array.FindIndex(Pages, p => p.Id == start);
    }

    void ShowPage(Page page)
    {
        current = page;
        nav.Invalidate(); // 선택이 바뀌는 순간에는 current가 이전 값이라, 바꾼 뒤 다시 그려야 고른 항목이 강조된다

        Control control = page switch
        {
            Page.Profiles => profilePage ??= new ProfileSettingsPage(engine, baseConfigPath, profilesDir, initialProfile, startWithNewProfile),
            Page.Models => modelPage ??= new ModelManagerPage(baseConfigPath, engine.IsRunning, firstRun: false),
            _ => updatePage ??= new UpdateSettingsPage(baseConfigPath),
        };

        host.SuspendLayout();
        foreach (Control c in host.Controls) c.Visible = c == control;
        if (!host.Controls.Contains(control)) host.Controls.Add(control); // 처음 보일 때 크기가 잡힌 뒤 내용을 읽는다
        control.Visible = true;
        host.ResumeLayout();
    }

    void DrawNavItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0) return;
        bool selected = Pages[e.Index].Id == current;
        using (var back = new SolidBrush(selected ? Theme.SelectedRow : Theme.Surface))
            e.Graphics.FillRectangle(back, e.Bounds);
        if (selected)
            using (var bar = new SolidBrush(Theme.Accent))
                e.Graphics.FillRectangle(bar, e.Bounds.Left, e.Bounds.Top + 6, 3, e.Bounds.Height - 12);

        using var font = new Font(nav.Font, selected ? FontStyle.Bold : FontStyle.Regular);
        TextRenderer.DrawText(e.Graphics, Pages[e.Index].Title, font,
            new Rectangle(e.Bounds.Left + 16, e.Bounds.Top, e.Bounds.Width - 20, e.Bounds.Height),
            selected ? Color.White : Theme.SubText,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPrefix);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // 저장하지 않은 프로필 변경과 진행 중인 모델 다운로드를 확인한다.
        if (profilePage?.ConfirmUnsavedChanges() == false || modelPage?.ConfirmClose() == false)
            e.Cancel = true;
        base.OnFormClosing(e);
    }
}
