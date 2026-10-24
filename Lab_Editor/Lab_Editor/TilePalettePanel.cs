namespace Lab_Editor;

// ======================================================
// TilePalettePanel - 右パネルの「タイル」タブ
//
// 以前は「ID:名前」と書いた色つきボタンが並ぶだけで、絵はごく小さなアイコンしか無かった。
// ここでは、タイルの実際の絵（タイルセットなら切り出し範囲）を大きなカードで並べ、
// 名前・IDで探せるようにする。数値指定の一括配置への入口も、この下に置く。
// ======================================================
public sealed class TilePalettePanel : UserControl
{
    private readonly TextBox _txtSearch = new() { Dock = DockStyle.Fill, PlaceholderText = "🔍 名前・IDで探す", BorderStyle = BorderStyle.FixedSingle };
    private readonly CardListView _list = new() { Dock = DockStyle.Fill, EmptyText = "該当するタイルがありません" };
    private readonly Button _btnBulk = new();

    private AssetDefinitions _assets = new();
    private AssetThumbs? _thumbs;
    private int _selectedId = 1;

    // タイルを選んだとき（ID）。
    public event Action<int>? TilePicked;
    // 「数値指定で一括配置」が押されたとき。
    public event Action? BulkFillClicked;

    public TilePalettePanel()
    {
        BackColor = UiTheme.PanelBack;
        Font = UiTheme.Base;

        var searchHost = new Panel { Dock = DockStyle.Top, Height = UiTheme.S(this, 34), Padding = new Padding(UiTheme.S(this, 6), UiTheme.S(this, 6), UiTheme.S(this, 6), UiTheme.S(this, 4)) };
        searchHost.Controls.Add(_txtSearch);
        _txtSearch.TextChanged += (s, e) => Rebuild();

        _list.ItemClicked += (s, item) =>
        {
            if (item.Tag is int id) { _selectedId = id; TilePicked?.Invoke(id); }
        };

        _btnBulk.Dock = DockStyle.Bottom;
        _btnBulk.Height = UiTheme.S(this, 36);
        _btnBulk.Text = "🔢 数値指定で一括配置";
        _btnBulk.FlatStyle = FlatStyle.Flat;
        _btnBulk.FlatAppearance.BorderColor = UiTheme.CardBorder;
        _btnBulk.BackColor = UiTheme.CardBack;
        _btnBulk.Cursor = Cursors.Hand;
        _btnBulk.Click += (s, e) => BulkFillClicked?.Invoke();

        Controls.Add(_list);
        Controls.Add(_btnBulk);
        Controls.Add(searchHost);
    }

    // アセット定義を読み込み直したときに呼ぶ。
    public void Bind(AssetDefinitions assets, AssetThumbs thumbs, int selectedTileId)
    {
        _assets = assets;
        _thumbs = thumbs;
        _selectedId = selectedTileId;
        Rebuild();
    }

    // 外から選択を合わせる（イベントは出さない）。
    public void SelectTile(int id)
    {
        _selectedId = id;
        _list.SetSelectedKey(KeyOf(id));
    }

    private static string KeyOf(int id) => $"tile:{id}";

    private void Rebuild()
    {
        if (_thumbs == null) return;
        int px = UiTheme.S(this, 48);
        string q = _txtSearch.Text.Trim();
        var items = new List<CardItem>();
        foreach (var t in _assets.Tiles)
        {
            if (q.Length > 0 && !t.id.ToString().Contains(q) && !t.name.Contains(q, StringComparison.OrdinalIgnoreCase)) continue;
            var traits = new List<string>();
            if (t.collidable) traits.Add("当たり判定あり");
            if (t.deadly) traits.Add("即死");
            if (t.breakBySlam || t.breakByRam || t.breakByBullet || t.breakByPlayer) traits.Add("壊せる");
            items.Add(new CardItem
            {
                Key = KeyOf(t.id),
                Title = t.name.Length > 0 ? t.name : $"タイル{t.id}",
                Accent = t.deadly ? UiTheme.KindEnemy : t.collidable ? UiTheme.KindTile : Color.FromArgb(190, 196, 204),
                Icon = _thumbs.ForTile(t, px),
                Tip = $"{t.name}\nID: {t.id}" + (traits.Count > 0 ? "\n" + string.Join(" ・ ", traits) : "\nすり抜けられる"),
                Tag = t.id,
            });
        }
        _list.EmptyText = q.Length > 0 ? $"「{q}」に合うタイルがありません" : "タイルが登録されていません。\r\nメニュー「アセット管理」→「タイルエディタ」で追加できます。";
        _list.SetItems(items);
        _list.SetSelectedKey(KeyOf(_selectedId), scrollIntoView: false);
    }
}
