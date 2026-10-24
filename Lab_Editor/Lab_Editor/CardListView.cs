using System.Drawing.Drawing2D;

namespace Lab_Editor;

// ======================================================
// CardListView - 画像付きの「カード一覧」を出す共通部品
//
// 今までの右パネルは、名前だけの ListBox や色付きボタンの並びだった。
// 敵・ギミック・アイテム・タイル・配置済みイベントのどれも、「絵を見て選ぶ」ほうが速いので、
// 絵＋名前を1枚のカードとして並べる部品を1つ作り、すべての一覧でこれを使い回す。
//
// 2つの並べ方がある。
//   Grid : 正方形に近いカードを、幅に合わせて何列にも並べる（パレット向け）
//   Rows : 1項目を横長の1行にして縦に並べる（配置済みイベントのように、補足の文字も出したい一覧向け）
// Group が変わるところには見出しが入る（「👾 敵 (12)」のように件数つき）。
// ======================================================

// 一覧に並べる1項目の中身。
public sealed class CardItem
{
    // 項目を一意に決める文字列。選択状態の保持や、外部からの選択に使う。
    public string Key = "";
    // カードに大きく出す名前。
    public string Title = "";
    // 名前の下に小さく出す補足（Rows のときだけ表示する）。
    public string Subtitle = "";
    // 見出しの文字（空なら見出しなし）。同じ値が続く間は1つの見出しにまとまる。
    public string Group = "";
    // カードに出す絵。無ければ絵なしで文字だけになる。
    public Image? Icon;
    // 種類の目印の色（Grid は左上の点、Rows は左端の帯に使う）。
    public Color Accent = UiTheme.Accent;
    // マウスを乗せたときに出す説明。
    public string Tip = "";
    // 呼び出し側が自由に持たせる付属データ（元の定義や配置物そのもの）。
    public object? Tag;
}

// 一覧の並べ方。
public enum CardLayout { Grid, Rows }

public sealed class CardListView : Control
{
    // 画面に置いた1つ分の領域。項目のカードか、グループ見出しのどちらか。
    private sealed class Cell
    {
        public Rectangle Bounds;      // スクロール前の位置（コンテンツ座標）
        public CardItem? Item;        // カードのとき
        public string? Header;        // 見出しのとき
    }

    private readonly List<CardItem> _items = new();
    private readonly List<Cell> _cells = new();
    // 縦スクロールバー。中身が収まるときは隠す。
    private readonly VScrollBar _vs = new() { Dock = DockStyle.Right, Visible = false };
    // 説明の吹き出し。
    private readonly ToolTip _tip = new() { InitialDelay = 400, ReshowDelay = 100, AutoPopDelay = 8000 };

    private int _scroll;                 // 現在のスクロール量（コンテンツ座標での、画面上端の位置）
    private int _contentHeight;          // 全体の高さ
    private Cell? _hover;                // マウスが乗っているカード
    private string? _selectedKey;        // 選ばれている項目のKey（主役の選択）
    private readonly HashSet<string> _marked = new(); // 薄く強調する項目のKey（複数選択の残り）
    private string? _lastTipKey;         // 直前に吹き出しを出した項目（同じ項目で出し直さないため）
    private string? _pendingEnsure;      // 表示される前に「見える所へ」と頼まれた項目（表示されたら動かす）

    private CardLayout _layout = CardLayout.Grid;
    public CardLayout LayoutMode
    {
        get => _layout;
        set { if (_layout == value) return; _layout = value; Relayout(); }
    }

    // 項目が1つも無いときに、中央へ出す案内文。
    public string EmptyText { get; set; } = "";

    // 項目をクリックした（選んだ）とき。キーボードの矢印で選び直したときも出る。
    public event EventHandler<CardItem>? ItemClicked;
    // 項目をダブルクリック／Enterしたとき。
    public event EventHandler<CardItem>? ItemActivated;
    // 項目を右クリックしたとき。画面座標ではなく、この部品の座標で位置が来る。
    public event EventHandler<(CardItem item, Point location)>? ItemContext;
    // Delete キーが押されたとき（選ばれている項目があるとき）。
    public event EventHandler<CardItem>? DeletePressed;

    public int ItemCount => _items.Count;
    public string? SelectedKey => _selectedKey;

    public CardListView()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
               | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        TabStop = true;
        BackColor = UiTheme.PanelBack;
        Font = UiTheme.Base;
        _vs.Scroll += (s, e) => { _scroll = _vs.Value; Invalidate(); };
        Controls.Add(_vs);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _tip.Dispose();
        base.Dispose(disposing);
    }

    // ── 外から使う操作 ──────────────────────────────

    // 項目を丸ごと入れ替える。スクロール位置と選択は、可能な範囲で保つ。
    public void SetItems(IEnumerable<CardItem> items)
    {
        _items.Clear();
        _items.AddRange(items);
        _hover = null;
        if (_selectedKey != null && !_items.Any(i => i.Key == _selectedKey)) _selectedKey = null;
        Relayout();
    }

    // 選択を外から変える。ItemClicked は発火しない（呼び出し側が自分で反映済みのため）。
    public void SetSelectedKey(string? key, bool scrollIntoView = true)
    {
        if (_selectedKey == key) return;
        _selectedKey = key;
        if (scrollIntoView && key != null) EnsureVisible(key);
        Invalidate();
    }

    // 薄く強調する項目を外から変える（キャンバスで複数選んでいるとき、一覧にも映すため）。
    public void SetMarkedKeys(IEnumerable<string> keys)
    {
        _marked.Clear();
        foreach (var k in keys) _marked.Add(k);
        Invalidate();
    }

    // 指定した項目が画面に入るまでスクロールする。
    public void EnsureVisible(string key)
    {
        // まだ画面に出ていない（隠れたタブの中など、高さがほぼ無い）ときは、動かさずに覚えておく。
        // 高さの無い枠に「収める」と、項目の下端まで無駄にスクロールしてしまうため。表示された時点でやり直す。
        if (ClientSize.Height < UiTheme.S(this, 40)) { _pendingEnsure = key; return; }
        var cell = _cells.FirstOrDefault(c => c.Item?.Key == key);
        if (cell == null) return;
        int pad = UiTheme.S(this, 6);
        if (cell.Bounds.Top - pad < _scroll) SetScroll(cell.Bounds.Top - pad);
        else if (cell.Bounds.Bottom + pad > _scroll + ClientSize.Height) SetScroll(cell.Bounds.Bottom + pad - ClientSize.Height);
    }

    // ── 並べ直し ────────────────────────────────

    private void Relayout()
    {
        // 縦スクロールバーが要るかどうかは、並べてみないと分からない。
        // まずバーなしで並べ、高さがあふれたらバーありの幅で並べ直す。
        _vs.Visible = false;
        BuildCells(ClientSize.Width);
        if (_contentHeight > ClientSize.Height && ClientSize.Height > 0)
        {
            _vs.Visible = true;
            BuildCells(ClientSize.Width - _vs.Width);
        }
        UpdateScrollBar();
        if (_pendingEnsure != null && ClientSize.Height >= UiTheme.S(this, 40))
        {
            var key = _pendingEnsure; _pendingEnsure = null;
            EnsureVisible(key);
        }
        Invalidate();
    }

    private void BuildCells(int usableWidth)
    {
        _cells.Clear();
        int pad = UiTheme.S(this, 8);
        int gap = UiTheme.S(this, 6);
        int headerH = UiTheme.S(this, 28);
        int y = pad;

        // 見出しに件数を出すため、グループごとの数を先に数える。
        var counts = new Dictionary<string, int>();
        foreach (var it in _items) counts[it.Group] = counts.GetValueOrDefault(it.Group) + 1;

        int avail = Math.Max(UiTheme.S(this, 40), usableWidth - pad * 2);
        int cellW = UiTheme.S(this, 86), cellH = UiTheme.S(this, 96);
        int cols = Math.Max(1, (avail + gap) / (cellW + gap));
        int gridW = (avail - gap * (cols - 1)) / cols;       // 余りを各カードへ配って、右端まで使う
        int rowH = UiTheme.S(this, 48);

        string? lastGroup = null;
        int col = 0;
        foreach (var it in _items)
        {
            // グループが変わったら、見出しを入れる（Gridでは行を折り返してから）。
            if (it.Group != lastGroup)
            {
                if (_layout == CardLayout.Grid && col != 0) { y += cellH + gap; col = 0; }
                lastGroup = it.Group;
                if (it.Group.Length > 0)
                {
                    _cells.Add(new Cell { Bounds = new Rectangle(pad, y, avail, headerH), Header = $"{it.Group}  ({counts[it.Group]})" });
                    y += headerH + UiTheme.S(this, 2);
                }
            }

            if (_layout == CardLayout.Grid)
            {
                _cells.Add(new Cell { Bounds = new Rectangle(pad + col * (gridW + gap), y, gridW, cellH), Item = it });
                if (++col >= cols) { col = 0; y += cellH + gap; }
            }
            else
            {
                _cells.Add(new Cell { Bounds = new Rectangle(pad, y, avail, rowH), Item = it });
                y += rowH + UiTheme.S(this, 4);
            }
        }
        if (_layout == CardLayout.Grid && col != 0) y += cellH + gap;
        _contentHeight = y + pad;
    }

    private void UpdateScrollBar()
    {
        int view = Math.Max(1, ClientSize.Height);
        int max = Math.Max(0, _contentHeight - view);
        _scroll = Math.Clamp(_scroll, 0, max);
        if (_vs.Visible)
        {
            _vs.SmallChange = UiTheme.S(this, 28);
            _vs.LargeChange = view;
            _vs.Minimum = 0;
            _vs.Maximum = _contentHeight;       // Maximum は「LargeChange分だけ手前までしか動かない」仕様なので、全体の高さを入れる
            _vs.Value = Math.Min(_scroll, Math.Max(0, _vs.Maximum - _vs.LargeChange + 1));
        }
    }

    private void SetScroll(int value)
    {
        int max = Math.Max(0, _contentHeight - ClientSize.Height);
        _scroll = Math.Clamp(value, 0, max);
        if (_vs.Visible) _vs.Value = Math.Min(_scroll, Math.Max(0, _vs.Maximum - _vs.LargeChange + 1));
        Invalidate();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        Relayout();
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        Relayout();
    }

    // ── 描画 ────────────────────────────────────

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        int offY = -_scroll;

        if (_items.Count == 0)
        {
            if (EmptyText.Length > 0)
                TextRenderer.DrawText(g, EmptyText, UiTheme.Base, ClientRectangle, UiTheme.TextMuted,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
            return;
        }

        foreach (var cell in _cells)
        {
            var r = cell.Bounds;
            r.Offset(0, offY);
            if (r.Bottom < 0 || r.Top > ClientSize.Height) continue;   // 画面外は描かない

            if (cell.Header != null) { PaintHeader(g, r, cell.Header); continue; }
            var item = cell.Item!;
            bool selected = item.Key == _selectedKey;
            bool marked = _marked.Contains(item.Key);
            bool hover = ReferenceEquals(cell, _hover);
            if (_layout == CardLayout.Grid) PaintGridCard(g, r, item, selected, marked, hover);
            else PaintRowCard(g, r, item, selected, marked, hover);
        }
    }

    // グループ見出し（文字＋その右に細い線）。
    private void PaintHeader(Graphics g, Rectangle r, string text)
    {
        var textRect = new Rectangle(r.X + UiTheme.S(this, 2), r.Y, r.Width, r.Height);
        TextRenderer.DrawText(g, text, UiTheme.Bold, textRect, UiTheme.TextMuted,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        var size = TextRenderer.MeasureText(g, text, UiTheme.Bold);
        using var pen = new Pen(UiTheme.CardBorder);
        int lx = textRect.X + size.Width + UiTheme.S(this, 8);
        if (lx < r.Right) g.DrawLine(pen, lx, r.Y + r.Height / 2, r.Right, r.Y + r.Height / 2);
    }

    // カード共通の背景と枠（選択中は強調色の太い枠）。
    private void PaintCardFrame(Graphics g, Rectangle r, bool selected, bool marked, bool hover)
    {
        using var path = RoundedRect(r, UiTheme.S(this, 6));
        Color back = selected ? UiTheme.CardSelectedBack : marked ? Color.FromArgb(235, 243, 253) : hover ? UiTheme.CardHover : UiTheme.CardBack;
        using (var br = new SolidBrush(back)) g.FillPath(br, path);
        using var pen = new Pen(selected ? UiTheme.Accent : UiTheme.CardBorder, selected ? UiTheme.S(this, 2) : 1f);
        g.DrawPath(pen, path);
    }

    // 絵を、枠の中へ縦横比を保ったまま中央に描く。ドット絵がにじまないよう最近傍補間にする。
    private static void DrawIcon(Graphics g, Image icon, Rectangle box)
    {
        var oldMode = g.InterpolationMode; var oldPix = g.PixelOffsetMode;
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        float k = Math.Min((float)box.Width / icon.Width, (float)box.Height / icon.Height);
        float w = icon.Width * k, h = icon.Height * k;
        g.DrawImage(icon, box.X + (box.Width - w) / 2f, box.Y + (box.Height - h) / 2f, w, h);
        g.InterpolationMode = oldMode; g.PixelOffsetMode = oldPix;
    }

    private void PaintGridCard(Graphics g, Rectangle r, CardItem item, bool selected, bool marked, bool hover)
    {
        PaintCardFrame(g, r, selected, marked, hover);
        int iconSize = UiTheme.S(this, 48);
        var iconBox = new Rectangle(r.X + (r.Width - iconSize) / 2, r.Y + UiTheme.S(this, 8), iconSize, iconSize);
        if (item.Icon != null) DrawIcon(g, item.Icon, iconBox);

        // 左上の小さな丸＝種類の色
        int dot = UiTheme.S(this, 8);
        using (var br = new SolidBrush(item.Accent)) g.FillEllipse(br, r.X + UiTheme.S(this, 6), r.Y + UiTheme.S(this, 6), dot, dot);

        var titleRect = new Rectangle(r.X + UiTheme.S(this, 3), iconBox.Bottom + UiTheme.S(this, 4), r.Width - UiTheme.S(this, 6), r.Bottom - iconBox.Bottom - UiTheme.S(this, 6));
        TextRenderer.DrawText(g, item.Title, UiTheme.Small, titleRect, UiTheme.TextMain,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
    }

    private void PaintRowCard(Graphics g, Rectangle r, CardItem item, bool selected, bool marked, bool hover)
    {
        PaintCardFrame(g, r, selected, marked, hover);
        // 左端の色の帯
        var bar = new Rectangle(r.X + UiTheme.S(this, 3), r.Y + UiTheme.S(this, 6), UiTheme.S(this, 4), r.Height - UiTheme.S(this, 12));
        using (var br = new SolidBrush(item.Accent)) g.FillRectangle(br, bar);

        int iconSize = r.Height - UiTheme.S(this, 12);
        var iconBox = new Rectangle(bar.Right + UiTheme.S(this, 8), r.Y + UiTheme.S(this, 6), iconSize, iconSize);
        if (item.Icon != null) DrawIcon(g, item.Icon, iconBox);

        int tx = iconBox.Right + UiTheme.S(this, 8);
        int tw = r.Right - tx - UiTheme.S(this, 6);
        var titleRect = new Rectangle(tx, r.Y + UiTheme.S(this, 5), tw, UiTheme.S(this, 20));
        TextRenderer.DrawText(g, item.Title, UiTheme.Bold, titleRect, UiTheme.TextMain,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        var subRect = new Rectangle(tx, titleRect.Bottom, tw, UiTheme.S(this, 18));
        TextRenderer.DrawText(g, item.Subtitle, UiTheme.Small, subRect, UiTheme.TextMuted,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
    }

    private static GraphicsPath RoundedRect(Rectangle r, int radius)
    {
        int d = Math.Max(2, radius * 2);
        var p = new GraphicsPath();
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d - 1, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d - 1, r.Bottom - d - 1, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d - 1, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    // ── マウス ──────────────────────────────────

    private Cell? CellAt(Point p)
    {
        int cy = p.Y + _scroll;
        foreach (var c in _cells)
            if (c.Item != null && c.Bounds.Contains(p.X, cy)) return c;
        return null;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var cell = CellAt(e.Location);
        if (!ReferenceEquals(cell, _hover))
        {
            _hover = cell;
            Invalidate();
            Cursor = cell != null ? Cursors.Hand : Cursors.Default;
        }
        // 吹き出しは項目が変わったときだけ出し直す（同じ項目の上で動かしても点滅させない）。
        string? key = cell?.Item?.Key;
        if (key != _lastTipKey)
        {
            _lastTipKey = key;
            _tip.Hide(this);
            if (cell?.Item != null && cell.Item.Tip.Length > 0)
                _tip.Show(cell.Item.Tip, this, e.X + UiTheme.S(this, 14), e.Y + UiTheme.S(this, 18));
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hover != null) { _hover = null; Invalidate(); }
        _lastTipKey = null;
        _tip.Hide(this);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        var cell = CellAt(e.Location);
        if (cell?.Item == null) return;
        if (e.Button == MouseButtons.Left)
        {
            _selectedKey = cell.Item.Key;
            Invalidate();
            ItemClicked?.Invoke(this, cell.Item);
        }
        else if (e.Button == MouseButtons.Right)
        {
            // 右クリックでも、その項目を選んでからメニューを出す（どれに対する操作か分かるように）。
            _selectedKey = cell.Item.Key;
            Invalidate();
            ItemClicked?.Invoke(this, cell.Item);
            ItemContext?.Invoke(this, (cell.Item, e.Location));
        }
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        var cell = CellAt(e.Location);
        if (cell?.Item != null && e.Button == MouseButtons.Left) ItemActivated?.Invoke(this, cell.Item);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        // ホイール1ノッチで、カード約1段ぶん動かす。
        SetScroll(_scroll - e.Delta / 120 * UiTheme.S(this, 56));
    }

    // ── キーボード ──────────────────────────────

    protected override bool IsInputKey(Keys keyData)
    {
        switch (keyData & Keys.KeyCode)
        {
            case Keys.Up: case Keys.Down: case Keys.Left: case Keys.Right:
            case Keys.Home: case Keys.End: case Keys.Enter: case Keys.Delete:
                return true;
        }
        return base.IsInputKey(keyData);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        var itemCells = _cells.Where(c => c.Item != null).ToList();
        if (itemCells.Count == 0) return;
        int cur = itemCells.FindIndex(c => c.Item!.Key == _selectedKey);

        Cell? target = null;
        switch (e.KeyCode)
        {
            case Keys.Left: target = itemCells[Math.Max(0, cur <= 0 ? 0 : cur - 1)]; break;
            case Keys.Right: target = itemCells[Math.Min(itemCells.Count - 1, cur + 1)]; break;
            case Keys.Home: target = itemCells[0]; break;
            case Keys.End: target = itemCells[^1]; break;
            case Keys.Up:
            case Keys.Down:
                if (cur < 0) { target = itemCells[0]; break; }
                // 行きたい向きにあるカードのうち、いちばん近い段の、いちばん近い横位置のものへ。
                var from = itemCells[cur].Bounds;
                int sign = e.KeyCode == Keys.Down ? 1 : -1;
                target = itemCells.Where(c => (c.Bounds.Y - from.Y) * sign > 2)
                    .OrderBy(c => Math.Abs(c.Bounds.Y - from.Y) * 10000 + Math.Abs(c.Bounds.X - from.X))
                    .FirstOrDefault();
                break;
            case Keys.Enter:
                if (cur >= 0) ItemActivated?.Invoke(this, itemCells[cur].Item!);
                e.Handled = true; return;
            case Keys.Delete:
                if (cur >= 0) DeletePressed?.Invoke(this, itemCells[cur].Item!);
                e.Handled = true; return;
        }
        if (target?.Item != null && target.Item.Key != _selectedKey)
        {
            _selectedKey = target.Item.Key;
            EnsureVisible(_selectedKey);
            Invalidate();
            ItemClicked?.Invoke(this, target.Item);
        }
        e.Handled = true;
    }

    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
}
