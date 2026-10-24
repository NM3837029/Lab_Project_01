namespace Lab_Editor;

// 配置する物の種類（敵・ギミック・アイテム）。
public enum AssetKind { Enemy, Gimmick, Item }

// ======================================================
// PlacementPalettePanel - 右パネルの「配置対象」タブ
//
// 以前は、敵・ギミック・アイテムが高さ70px程度の小さなリストボックス3つで、名前しか見えず、
// 数が増えると探せなかった。ここでは絵つきのカードを並べ、検索と種類の絞り込みで探せるようにする。
//   1. 上の検索欄／絞り込みで探す
//   2. カードをクリックして「これを置く」と決める（下の帯に、選んだ物の絵と内容が出る）
//   3. マップをクリックして置く（置く操作そのものは MapCanvas が受け持つ）
// ======================================================
public sealed class PlacementPalettePanel : UserControl
{
    private readonly TextBox _txtSearch = new() { Dock = DockStyle.Fill, PlaceholderText = "🔍 名前・IDで探す", BorderStyle = BorderStyle.FixedSingle };
    private readonly ChipBar _chips = new();
    private readonly CardListView _list = new() { Dock = DockStyle.Fill, EmptyText = "該当する物がありません" };
    private readonly SelectionStrip _strip = new();

    private AssetDefinitions _assets = new();
    private AssetThumbs? _thumbs;
    private (AssetKind kind, string id)? _current;

    // 「これを置く」を選んだとき。マップ側が、この種類・IDを置くモードに入る。
    public event Action<AssetKind, string>? AssetPicked;
    // 選択を解除したとき（解除ボタン）。マップ側は選択モードへ戻す。
    public event Action? PickCleared;

    public (AssetKind kind, string id)? Current => _current;

    public PlacementPalettePanel()
    {
        BackColor = UiTheme.PanelBack;
        Font = UiTheme.Base;

        var searchHost = new Panel { Dock = DockStyle.Top, Height = UiTheme.S(this, 34), Padding = new Padding(UiTheme.S(this, 6), UiTheme.S(this, 6), UiTheme.S(this, 6), UiTheme.S(this, 4)) };
        searchHost.Controls.Add(_txtSearch);
        _txtSearch.TextChanged += (s, e) => Rebuild();

        _chips.SelectionChanged += (s, e) => Rebuild();

        _list.ItemClicked += (s, item) => PickFromItem(item);

        _strip.ClearClicked += (s, e) => { ClearPick(); PickCleared?.Invoke(); };

        // 右ボタンで作る物（トリガー・開始位置・ゴール）の使い方。場所をとらないよう、検索欄の右の「ℹ」を押したときだけ吹き出しで出す。
        var btnInfo = new Button
        {
            Dock = DockStyle.Right, Width = UiTheme.S(this, 30), Text = "ℹ", Font = UiTheme.Bold,
            FlatStyle = FlatStyle.Flat, Cursor = Cursors.Help, ForeColor = UiTheme.Accent, BackColor = UiTheme.CardBack,
        };
        btnInfo.FlatAppearance.BorderColor = UiTheme.CardBorder;
        var infoTip = new ToolTip { IsBalloon = true, ToolTipTitle = "⚡ トリガー・開始位置・ゴールの置き方", ToolTipIcon = ToolTipIcon.Info };
        btnInfo.Click += (s, e) => infoTip.Show(
            "何も無い所を右ボタンでドラッグ：トリガーの範囲を作る\r\n"
          + "何も無い所を右クリック：ここにトリガー／開始位置／ゴール\r\n"
          + "トリガーを右クリック：編集・削除", btnInfo, 0, btnInfo.Height + 2, 12000);
        btnInfo.Leave += (s, e) => infoTip.Hide(btnInfo);
        searchHost.Controls.Add(btnInfo);

        // 追加順の決まり：Fill を先に、あとから足したものほど端に寄る。
        Controls.Add(_list);
        Controls.Add(_strip);
        Controls.Add(_chips);
        Controls.Add(searchHost);
    }

    // アセット定義を読み込み直したときに呼ぶ。一覧を作り直す（選択は、同じ物が残っていれば保つ）。
    public void Bind(AssetDefinitions assets, AssetThumbs thumbs)
    {
        _assets = assets;
        _thumbs = thumbs;
        _chips.SetChips(new[]
        {
            ("all", "すべて"),
            ("enemy", $"👾 敵 {assets.Enemies.Count}"),
            ("gimmick", $"🔧 ギミック {assets.Gimmicks.Count}"),
            ("item", $"💎 アイテム {assets.Items.Count}"),
        }, _chips.SelectedValue.Length > 0 ? _chips.SelectedValue : "all");
        // 読み直した結果、選んでいた物が無くなっていたら選択を外す。
        if (_current is { } cur && !Exists(cur.kind, cur.id)) _current = null;
        Rebuild();
        RefreshStrip();
    }

    // 選択を外す（マップ側の操作で、置くモードを抜けたときなど。イベントは出さない）。
    public void ClearPick()
    {
        _current = null;
        _list.SetSelectedKey(null);
        RefreshStrip();
    }

    private bool Exists(AssetKind kind, string id) => kind switch
    {
        AssetKind.Enemy => _assets.Enemies.Any(e => e.id == id),
        AssetKind.Gimmick => _assets.Gimmicks.Any(g => g.id == id),
        _ => _assets.Items.Any(i => i.id == id),
    };

    // 名前のうち、全角かっこ「（」より前の部分（かっこが先頭にあるときは全文）。
    private static string ShortName(string name)
    {
        int k = name.IndexOf('（');
        return k > 0 ? name[..k].Trim() : name;
    }

    private static string KeyOf(AssetKind kind, string id) => $"{kind}:{id}";

    private void PickFromItem(CardItem item)
    {
        if (item.Tag is not (AssetKind kind, string id)) return;
        _current = (kind, id);
        RefreshStrip();
        AssetPicked?.Invoke(kind, id);
    }

    // 検索語・絞り込みに合わせて、カード一覧を作り直す。
    private void Rebuild()
    {
        if (_thumbs == null) return;
        int px = UiTheme.S(this, 48);
        string q = _txtSearch.Text.Trim();
        string chip = _chips.SelectedValue;
        bool Match(string id, string name) =>
            q.Length == 0 || id.Contains(q, StringComparison.OrdinalIgnoreCase) || name.Contains(q, StringComparison.OrdinalIgnoreCase);

        var items = new List<CardItem>();
        // カードは幅が狭いので、「歩く敵（歩いて詰めてくる）」のような名前は、かっこの前だけを大きく出す。
        // ただし、短くすると別の物と同じ名前になってしまう場合は、見分けがつくよう全文を使う。
        var shortCount = new Dictionary<string, int>();
        foreach (var n in _assets.Enemies.Select(x => x.name).Concat(_assets.Gimmicks.Select(x => x.name)).Concat(_assets.Items.Select(x => x.name)))
            shortCount[ShortName(n)] = shortCount.GetValueOrDefault(ShortName(n)) + 1;
        string CardTitle(string name, string id) => name.Length == 0 ? id : shortCount.GetValueOrDefault(ShortName(name)) > 1 ? name : ShortName(name);
        if (chip is "all" or "enemy")
            foreach (var e in _assets.Enemies.Where(e => Match(e.id, e.name)))
                items.Add(new CardItem
                {
                    Key = KeyOf(AssetKind.Enemy, e.id), Title = CardTitle(e.name, e.id),
                    Group = "👾 敵", Accent = UiTheme.KindEnemy, Icon = _thumbs.ForEnemy(e, px),
                    Tip = $"{e.name}\nID: {e.id}\nHP {e.hp} ・ {e.width}×{e.height}px",
                    Tag = (AssetKind.Enemy, e.id),
                });
        if (chip is "all" or "gimmick")
            foreach (var g in _assets.Gimmicks.Where(g => Match(g.id, g.name)))
                items.Add(new CardItem
                {
                    Key = KeyOf(AssetKind.Gimmick, g.id), Title = CardTitle(g.name, g.id),
                    Group = "🔧 ギミック", Accent = UiTheme.KindGimmick, Icon = _thumbs.ForGimmick(g, px),
                    Tip = $"{g.name}\nID: {g.id}\n{g.hitboxWidth}×{g.hitboxHeight}px",
                    Tag = (AssetKind.Gimmick, g.id),
                });
        if (chip is "all" or "item")
            foreach (var i in _assets.Items.Where(i => Match(i.id, i.name)))
                items.Add(new CardItem
                {
                    Key = KeyOf(AssetKind.Item, i.id), Title = CardTitle(i.name, i.id),
                    Group = "💎 アイテム", Accent = UiTheme.KindItem, Icon = _thumbs.ForItem(i, px),
                    Tip = $"{i.name}\nID: {i.id}" + (i.grant_ability.Length > 0 ? $"\n付与する能力: {i.grant_ability}" : ""),
                    Tag = (AssetKind.Item, i.id),
                });

        _list.EmptyText = q.Length > 0 ? $"「{q}」に合う物がありません" : "まだ登録がありません。\r\nメニュー「アセット管理」から追加できます。";
        _list.SetItems(items);
        _list.SetSelectedKey(_current is { } c ? KeyOf(c.kind, c.id) : null, scrollIntoView: false);
    }

    // 下の帯（いま選んでいる物の絵と内容）を更新する。
    private void RefreshStrip()
    {
        if (_current is not { } c || _thumbs == null) { _strip.SetContent(null, "", ""); return; }
        int px = UiTheme.S(this, 48);
        switch (c.kind)
        {
            case AssetKind.Enemy:
                var e = _assets.Enemies.FirstOrDefault(x => x.id == c.id);
                if (e != null) _strip.SetContent(_thumbs.ForEnemy(e, px), e.name.Length > 0 ? e.name : e.id, $"敵 ・ ID {e.id} ・ HP {e.hp}");
                break;
            case AssetKind.Gimmick:
                var g = _assets.Gimmicks.FirstOrDefault(x => x.id == c.id);
                if (g != null) _strip.SetContent(_thumbs.ForGimmick(g, px), g.name.Length > 0 ? g.name : g.id, $"ギミック ・ ID {g.id}");
                break;
            default:
                var i = _assets.Items.FirstOrDefault(x => x.id == c.id);
                if (i != null) _strip.SetContent(_thumbs.ForItem(i, px), i.name.Length > 0 ? i.name : i.id, $"アイテム ・ ID {i.id}");
                break;
        }
    }

    // 下の帯。選んでいる物の絵・名前・内容と、解除ボタンを出す。
    private sealed class SelectionStrip : Panel
    {
        private Image? _icon;
        private string _title = "", _detail = "";
        private readonly Button _btnClear = new() { Text = "✕ 解除", FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand, Font = UiTheme.Small };

        public event EventHandler? ClearClicked;

        public SelectionStrip()
        {
            Dock = DockStyle.Bottom;
            Height = UiTheme.S(this, 58);
            BackColor = UiTheme.CardBack;
            DoubleBuffered = true;
            _btnClear.FlatAppearance.BorderColor = UiTheme.CardBorder;
            _btnClear.Click += (s, e) => ClearClicked?.Invoke(this, EventArgs.Empty);
            Controls.Add(_btnClear);
        }

        public void SetContent(Image? icon, string title, string detail)
        {
            _icon = icon; _title = title; _detail = detail;
            _btnClear.Visible = icon != null;
            Invalidate();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            int w = UiTheme.S(this, 64), h = UiTheme.S(this, 26);
            _btnClear.SetBounds(ClientSize.Width - w - UiTheme.S(this, 8), (ClientSize.Height - h) / 2, w, h);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            using (var pen = new Pen(UiTheme.CardBorder)) g.DrawLine(pen, 0, 0, Width, 0);
            int pad = UiTheme.S(this, 10);
            if (_icon == null)
            {
                TextRenderer.DrawText(g, "置きたい物をクリックで選び、\r\nマップをクリックして置きます", UiTheme.Small,
                    new Rectangle(pad, 0, Width - pad * 2, Height), UiTheme.TextMuted,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
                return;
            }
            int box = Height - pad * 2;
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
            g.DrawImage(_icon, new Rectangle(pad, pad, box, box));
            int tx = pad + box + pad;
            int tw = Math.Max(10, _btnClear.Left - tx - UiTheme.S(this, 6));
            TextRenderer.DrawText(g, _title, UiTheme.Bold, new Rectangle(tx, pad, tw, box / 2), UiTheme.TextMain,
                TextFormatFlags.Left | TextFormatFlags.Bottom | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(g, _detail, UiTheme.Small, new Rectangle(tx, pad + box / 2 + UiTheme.S(this, 2), tw, box / 2), UiTheme.TextMuted,
                TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        }
    }
}
