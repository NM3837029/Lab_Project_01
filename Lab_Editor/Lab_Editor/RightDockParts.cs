namespace Lab_Editor;

// ======================================================
// 右パネルを組み立てる小さな部品（絞り込みボタンの列／上のタブ）
//
// ・ChipBar      : 「すべて／敵／ギミック…」のような、1つだけ選べる絞り込みボタンの横並び。幅が足りなければ折り返す
// ・TabButton    : 下線で現在地を示すタブ1つ分のボタン
// ・RightDockTabs: 右パネル上部のタブ。標準の TabControl は見た目が古く、選択中の下線も出せないので自前で作る
// ======================================================

public sealed class ChipBar : FlowLayoutPanel
{
    private readonly Dictionary<string, RadioButton> _chips = new();
    private bool _suppress;

    // 選ばれている絞り込みの値が変わったとき。
    public event EventHandler? SelectionChanged;
    public string SelectedValue { get; private set; } = "";

    public ChipBar()
    {
        Dock = DockStyle.Top;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        WrapContents = true;
        FlowDirection = FlowDirection.LeftToRight;
        Padding = new Padding(4, 0, 4, 2);
        BackColor = UiTheme.PanelBack;
    }

    // ボタンを作り直す。value は内部の値、text は表示する文字。
    public void SetChips(IEnumerable<(string value, string text)> chips, string selected)
    {
        _suppress = true;
        SuspendLayout();
        foreach (var c in _chips.Values) { Controls.Remove(c); c.Dispose(); }
        _chips.Clear();
        foreach (var (value, text) in chips)
        {
            var rb = new RadioButton
            {
                Appearance = Appearance.Button,
                FlatStyle = FlatStyle.Flat,
                AutoSize = true,
                Text = text,
                Font = UiTheme.Small,
                Padding = new Padding(UiTheme.S(this, 2), UiTheme.S(this, 1), UiTheme.S(this, 2), UiTheme.S(this, 1)),
                Margin = new Padding(0, 0, UiTheme.S(this, 4), UiTheme.S(this, 4)),
                Tag = value,
                Cursor = Cursors.Hand,
            };
            rb.FlatAppearance.BorderColor = UiTheme.CardBorder;
            rb.FlatAppearance.CheckedBackColor = UiTheme.Accent;
            rb.FlatAppearance.MouseOverBackColor = UiTheme.CardHover;
            rb.CheckedChanged += Chip_CheckedChanged;
            _chips[value] = rb;
            Controls.Add(rb);
        }
        if (!_chips.ContainsKey(selected) && _chips.Count > 0) selected = _chips.Keys.First();
        SelectedValue = selected;
        foreach (var kv in _chips) { kv.Value.Checked = kv.Key == selected; Restyle(kv.Value); }
        ResumeLayout();
        _suppress = false;
    }

    // 件数が変わったときなど、文字だけ差し替える（選択は変えない）。
    public void SetText(string value, string text)
    {
        if (_chips.TryGetValue(value, out var rb)) rb.Text = text;
    }

    private void Chip_CheckedChanged(object? sender, EventArgs e)
    {
        if (sender is not RadioButton rb) return;
        Restyle(rb);
        if (_suppress || !rb.Checked) return;
        SelectedValue = (string)rb.Tag!;
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private static void Restyle(RadioButton rb)
    {
        rb.BackColor = rb.Checked ? UiTheme.Accent : UiTheme.CardBack;
        rb.ForeColor = rb.Checked ? Color.White : UiTheme.TextMain;
    }
}

// 下線で現在地を示すタブのボタン。
public sealed class TabButton : RadioButton
{
    private bool _hover;

    public TabButton()
    {
        Appearance = Appearance.Button;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        Cursor = Cursors.Hand;
        Font = UiTheme.Bold;
    }

    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hover = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = false; Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Checked ? UiTheme.CardBack : _hover ? UiTheme.CardHover : UiTheme.PanelBack);
        TextRenderer.DrawText(g, Text, Checked ? UiTheme.Bold : UiTheme.Base, ClientRectangle,
            Checked ? UiTheme.Accent : UiTheme.TextMuted,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
        // 選択中のタブの下に、強調色の太い線を引く。
        if (Checked)
        {
            using var br = new SolidBrush(UiTheme.Accent);
            g.FillRectangle(br, 0, Height - UiTheme.S(this, 3), Width, UiTheme.S(this, 3));
        }
    }
}

// 右パネル上部のタブ。ページ（Control）を登録して切り替える。
public sealed class RightDockTabs : Panel
{
    private readonly Panel _bar = new() { Dock = DockStyle.Top };
    private readonly Panel _body = new() { Dock = DockStyle.Fill };
    private readonly List<(TabButton btn, Control page)> _pages = new();
    private bool _suppress;

    public RightDockTabs()
    {
        BackColor = UiTheme.PanelBack;
        _bar.BackColor = UiTheme.PanelBack;
        // Dock=Fill を先に Add し、Dock=Top を後に Add する（このコードベースの決まり）。
        Controls.Add(_body);
        Controls.Add(_bar);
    }

    // タブを1つ追加する。
    public void AddPage(string text, Control page)
    {
        var btn = new TabButton { Text = text };
        btn.CheckedChanged += (s, e) => { if (!_suppress && btn.Checked) ShowPage(page); };
        page.Dock = DockStyle.Fill;
        page.Visible = false;
        _body.Controls.Add(page);
        _bar.Controls.Add(btn);
        _pages.Add((btn, page));
        LayoutTabs();
        if (_pages.Count == 1) SelectedTab = page;
    }

    // 現在表示しているページ。代入すると、そのタブへ切り替える（呼び出し側のコードを変えずに済むよう、TabControl と同じ名前にした）。
    public Control? SelectedTab
    {
        get => _pages.FirstOrDefault(p => p.btn.Checked).page;
        set
        {
            foreach (var (btn, page) in _pages)
            {
                if (page != value) continue;
                _suppress = true; btn.Checked = true; _suppress = false;
                ShowPage(page);
                return;
            }
        }
    }

    private void ShowPage(Control page)
    {
        foreach (var (_, p) in _pages) p.Visible = p == page;
        page.BringToFront();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        LayoutTabs();
    }

    // タブを横幅いっぱいに等分して並べる。
    private void LayoutTabs()
    {
        if (_pages.Count == 0) return;
        _bar.Height = UiTheme.S(this, 34);
        int w = Math.Max(1, ClientSize.Width / _pages.Count);
        for (int i = 0; i < _pages.Count; i++)
        {
            var b = _pages[i].btn;
            b.SetBounds(i * w, 0, i == _pages.Count - 1 ? ClientSize.Width - i * w : w, _bar.Height);
        }
    }
}
