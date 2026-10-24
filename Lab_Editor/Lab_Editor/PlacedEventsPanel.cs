namespace Lab_Editor;

// ======================================================
// PlacedEventsPanel - 右パネルの「イベントリスト」タブ
//
// 以前は「👾 ID (x,y)」と文字が並ぶだけのリストボックスで、
//   ・ダブルクリックしないとマップに反映されない
//   ・物を置いたり消したりしても、一覧が更新されない（読み込み・元に戻す・やり直しのときだけ更新）
//   ・どれがどの敵か、名前でも絵でも分からない
// という状態だった。ここでは次のように作り替える。
//   ・種類ごとにまとめ、絵と名前と位置を1行に出す（見出しに件数）
//   ・1回クリックで、マップがその物へ飛んで選択される。マップで選んだ物は、一覧側でも強調される
//   ・検索と種類の絞り込み、右クリックメニュー（表示・編集・削除）、Delete キーで削除
// ======================================================
public sealed class PlacedEventsPanel : UserControl
{
    private const string SpecialGroup = "📍 スタート・ゴール";
    private const string EnemyGroup = "👾 敵";
    private const string GimmickGroup = "🔧 ギミック";
    private const string ItemGroup = "💎 アイテム";
    private const string TriggerGroup = "⚡ トリガー";

    private readonly TextBox _txtSearch = new() { Dock = DockStyle.Fill, PlaceholderText = "🔍 名前・IDで探す", BorderStyle = BorderStyle.FixedSingle };
    private readonly ChipBar _chips = new();
    private readonly CardListView _list = new() { Dock = DockStyle.Fill, LayoutMode = CardLayout.Rows, EmptyText = "" };

    private AssetDefinitions _assets = new();
    private AssetThumbs? _thumbs;
    private StageData? _stage;

    // 配置物（参照）→ 一覧上のKey。同じ内容の別の物を取り違えないよう、参照の同一性で対応づける。
    private readonly Dictionary<object, string> _keyOf = new(ReferenceEqualityComparer.Instance);
    // Key → 配置物（"START" / "GOAL" の文字列も含む）。
    private readonly Dictionary<string, object> _targetOf = new();

    // 1回クリックしたとき。引数は配置物（START/GOALは文字列）。マップをそこへ飛ばして選択する。
    public event Action<object>? ItemPicked;
    // ダブルクリック／Enterしたとき。トリガーなら編集画面を開く。
    public event Action<object>? ItemActivated;
    // 削除を求められたとき（右クリックメニュー／Deleteキー）。
    public event Action<object>? DeleteRequested;

    public PlacedEventsPanel()
    {
        BackColor = UiTheme.PanelBack;
        Font = UiTheme.Base;

        var searchHost = new Panel { Dock = DockStyle.Top, Height = UiTheme.S(this, 34), Padding = new Padding(UiTheme.S(this, 6), UiTheme.S(this, 6), UiTheme.S(this, 6), UiTheme.S(this, 4)) };
        searchHost.Controls.Add(_txtSearch);
        _txtSearch.TextChanged += (s, e) => Rebuild();

        _chips.SetChips(ChipDefs(0, 0, 0, 0, 0), "all");
        _chips.SelectionChanged += (s, e) => Rebuild();

        _list.ItemClicked += (s, item) => { if (_targetOf.TryGetValue(item.Key, out var t)) ItemPicked?.Invoke(t); };
        _list.ItemActivated += (s, item) => { if (_targetOf.TryGetValue(item.Key, out var t)) ItemActivated?.Invoke(t); };
        _list.DeletePressed += (s, item) => { if (_targetOf.TryGetValue(item.Key, out var t) && t is not string) DeleteRequested?.Invoke(t); };
        _list.ItemContext += (s, e) => ShowMenu(e.item, e.location);

        Controls.Add(_list);
        Controls.Add(_chips);
        Controls.Add(searchHost);
    }

    private static IEnumerable<(string, string)> ChipDefs(int all, int en, int gi, int it, int tr) => new[]
    {
        // 5つを1段に収めるため、種類は絵文字と件数だけにする（見出しの「👾 敵 (16)」で名前は分かる）。
        ("all", $"すべて {all}"),
        ("enemy", $"👾 {en}"),
        ("gimmick", $"🔧 {gi}"),
        ("item", $"💎 {it}"),
        ("trigger", $"⚡ {tr}"),
    };

    // アセット定義を読み込み直したときに呼ぶ（名前・絵の引き当てに使う）。
    public void Bind(AssetDefinitions assets, AssetThumbs thumbs)
    {
        _assets = assets;
        _thumbs = thumbs;
        Rebuild();
    }

    // 表示するステージを渡す（読み込み・編集のたびに呼んでよい。スクロール位置は保たれる）。
    public void SetStage(StageData? stage)
    {
        _stage = stage;
        Rebuild();
    }

    // マップ側で選ばれている物を、一覧にも映す（イベントは出さない）。
    // 1つなら強調枠つき、複数なら薄い強調で全部示す。
    public void SetSelection(IReadOnlyList<object> selected)
    {
        var keys = selected.Where(o => _keyOf.ContainsKey(o)).Select(o => _keyOf[o]).ToList();
        _list.SetMarkedKeys(keys.Skip(1));
        _list.SetSelectedKey(keys.Count > 0 ? keys[0] : null);
    }

    // 一覧のどれも選んでいない状態にする。
    public void ClearSelection()
    {
        _list.SetMarkedKeys(Array.Empty<string>());
        _list.SetSelectedKey(null);
    }

    // 位置の表示は、プロパティ表（X座標・Y座標）と同じピクセル値にそろえる。
    private static string Pos(float x, float y) => $"({x:0}, {y:0})";

    private static string Extra(float scale, float angle)
    {
        var s = "";
        if (Math.Abs(scale - 1f) > 0.001f) s += $" ・ ×{scale:0.##}";
        if (Math.Abs(angle) > 0.001f) s += $" ・ {angle * 180f / (float)Math.PI:0}°";
        return s;
    }

    private void Rebuild()
    {
        _keyOf.Clear();
        _targetOf.Clear();
        if (_thumbs == null || _stage == null)
        {
            _chips.SetChips(ChipDefs(0, 0, 0, 0, 0), _chips.SelectedValue);
            _list.EmptyText = "ステージを選ぶと、置いた物がここに並びます";
            _list.SetItems(Array.Empty<CardItem>());
            return;
        }

        int px = UiTheme.S(this, 40);
        string q = _txtSearch.Text.Trim();
        string chip = _chips.SelectedValue;
        bool Match(params string[] texts) => q.Length == 0 || texts.Any(t => t.Contains(q, StringComparison.OrdinalIgnoreCase));
        int n = 0;
        string Register(object target, string prefix) { var k = $"{prefix}#{n++}"; _keyOf[target] = k; _targetOf[k] = target; return k; }

        var items = new List<CardItem>();

        // スタート・ゴール（種類の絞り込みが「すべて」のときだけ出す）
        if (chip == "all")
        {
            if (Match("スタート", "開始", "start"))
                items.Add(new CardItem
                {
                    Key = Register("START", "sp"), Title = "スタート位置", Subtitle = Pos(_stage.PlayerStartX, _stage.PlayerStartY),
                    Group = SpecialGroup, Accent = UiTheme.KindSpecial, Icon = _thumbs.ForEmoji("🚶", UiTheme.KindSpecial, px),
                    Tip = "プレイヤーが最初に立つ場所",
                });
            if (_stage.GoalX >= 0 && Match("ゴール", "goal"))
                items.Add(new CardItem
                {
                    Key = Register("GOAL", "sp"), Title = "ゴール", Subtitle = Pos(_stage.GoalX, _stage.GoalY),
                    Group = SpecialGroup, Accent = UiTheme.KindSpecial, Icon = _thumbs.ForEmoji("🏁", UiTheme.KindSpecial, px),
                    Tip = "ここへ着くとステージクリア",
                });
        }

        if (chip is "all" or "enemy")
            foreach (var e in _stage.Enemies)
            {
                var def = _assets.Enemies.FirstOrDefault(d => d.id == e.Id);
                string name = def?.name is { Length: > 0 } ? def.name : e.Id;
                if (!Match(name, e.Id)) continue;
                items.Add(new CardItem
                {
                    Key = Register(e, "en"), Title = name,
                    Subtitle = (def == null ? "⚠ 定義なし ・ " : "") + $"{Pos(e.X, e.Y)}{Extra(e.Scale, e.Angle)}",
                    Group = EnemyGroup, Accent = UiTheme.KindEnemy, Icon = _thumbs.ForEnemy(def, px),
                    Tip = $"{name}\nID: {e.Id}\n位置 {Pos(e.X, e.Y)}",
                });
            }

        if (chip is "all" or "gimmick")
            foreach (var g in _stage.Gimmicks)
            {
                var def = _assets.Gimmicks.FirstOrDefault(d => d.id == g.Id);
                string name = def?.name is { Length: > 0 } ? def.name : g.Id;
                if (!Match(name, g.Id)) continue;
                items.Add(new CardItem
                {
                    Key = Register(g, "gi"), Title = name,
                    Subtitle = (def == null ? "⚠ 定義なし ・ " : "") + $"{Pos(g.X, g.Y)}{Extra(g.Scale, g.Angle)}",
                    Group = GimmickGroup, Accent = UiTheme.KindGimmick, Icon = _thumbs.ForGimmick(def, px),
                    Tip = $"{name}\nID: {g.Id}\n位置 {Pos(g.X, g.Y)}" + (g.Param.Length > 0 ? $"\nパラメータ: {g.Param}" : ""),
                });
            }

        if (chip is "all" or "item")
            foreach (var i in _stage.Items)
            {
                var def = _assets.Items.FirstOrDefault(d => d.id == i.Id);
                string name = def?.name is { Length: > 0 } ? def.name : i.Id;
                if (!Match(name, i.Id)) continue;
                items.Add(new CardItem
                {
                    Key = Register(i, "it"), Title = name,
                    Subtitle = (def == null ? "⚠ 定義なし ・ " : "") + $"{Pos(i.X, i.Y)}{Extra(i.Scale, i.Angle)}",
                    Group = ItemGroup, Accent = UiTheme.KindItem, Icon = _thumbs.ForItem(def, px),
                    Tip = $"{name}\nID: {i.Id}\n位置 {Pos(i.X, i.Y)}",
                });
            }

        if (chip is "all" or "trigger")
            foreach (var t in _stage.Triggers)
            {
                if (!Match(t.id, t.condition)) continue;
                items.Add(new CardItem
                {
                    Key = Register(t, "tr"), Title = t.id.Length > 0 ? t.id : "(名前なし)",
                    Subtitle = $"{t.condition} ・ アクション{t.actions.Count}件 ・ {Pos(t.x, t.y)}",
                    Group = TriggerGroup, Accent = UiTheme.KindTrigger, Icon = _thumbs.ForEmoji("⚡", UiTheme.KindTrigger, px),
                    Tip = $"トリガー {t.id}\n条件: {t.condition}\n範囲 {t.width:0}×{t.height:0}\nダブルクリックで編集",
                });
            }

        // 件数の表示は、絞り込みや検索に関わらず、ステージ全体の数にする。
        _chips.SetText("all", $"すべて {_stage.Enemies.Count + _stage.Gimmicks.Count + _stage.Items.Count + _stage.Triggers.Count}");
        _chips.SetText("enemy", $"👾 {_stage.Enemies.Count}");
        _chips.SetText("gimmick", $"🔧 {_stage.Gimmicks.Count}");
        _chips.SetText("item", $"💎 {_stage.Items.Count}");
        _chips.SetText("trigger", $"⚡ {_stage.Triggers.Count}");

        bool anyPlaced = _stage.Enemies.Count + _stage.Gimmicks.Count + _stage.Items.Count + _stage.Triggers.Count > 0;
        _list.EmptyText = q.Length > 0 ? $"「{q}」に合う物がありません"
            : anyPlaced ? "この種類の物はまだ置いていません"
            : "まだ何も置いていません。\r\n「配置対象」から選んで、マップをクリックしましょう。";
        _list.SetItems(items);
    }

    // 右クリックメニュー。選んだ物に対する操作だけを出す。
    private void ShowMenu(CardItem item, Point location)
    {
        if (!_targetOf.TryGetValue(item.Key, out var target)) return;
        var menu = new ContextMenuStrip { Font = UiTheme.Base };
        menu.Items.Add("📍 マップでここを表示", null, (s, e) => ItemPicked?.Invoke(target));
        if (target is EventTrigger) menu.Items.Add("✏ トリガーを編集...", null, (s, e) => ItemActivated?.Invoke(target));
        if (target is not string)
        {
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("🗑 削除", null, (s, e) => DeleteRequested?.Invoke(target));
        }
        menu.Closed += (s, e) => menu.Dispose();
        menu.Show(_list, location);
    }
}
