using System.Media;
using Newtonsoft.Json;

namespace Lab_Editor;

// ======================================================
// SoundAssignmentForm - 登録した音を、鳴らしたい場面へ割り当てる画面
//
// 「サウンド管理」で登録した BGM / SE の ID を、実際に鳴らす場面（敵の被弾音、ジャンプの音、ステージの BGM …）へ結びつける。
// 以前は敵・ギミック・アイテム・プレイヤー・編集ツール…ごとに別々の表が縦に並び、何十行もスクロールして探す作りだった。
// 今回は次の作りに改めた。
//    左 : 「割り当て先」の一覧（絵つき。敵・ギミック・アイテム・ステージ・プレイヤー操作・編集ツール・タイトル/リザルト）
//         各行に「設定済み 2/4」のように進み具合が出るので、まだ音を付けていない物がすぐ分かる
//    右 : 選んだ割り当て先の「場面ごとの音」（出現音・攻撃音…）。音の名前で選び、その場で試聴できる
// 呼び出し側との約束（コンストラクタの引数と Result 系プロパティ）は以前のまま。
// 敵・ギミック・アイテム・設定は複製を編集し、保存したときだけ結果を返す（キャンセルすれば元は汚れない）。
// ======================================================
public class SoundAssignmentForm : EditorFormBase
{
    public List<EnemyDef> ResultEnemies { get; private set; } = new();
    public List<GimmickDef> ResultGimmicks { get; private set; } = new();
    public List<ItemDef> ResultItems { get; private set; } = new();
    // ステージが開かれていないときは null のまま（呼び出し側は BGM を一切更新しない）
    public string? ResultStageBgmId { get; private set; }
    // プレイヤー操作音・編集ツール音・UI音を反映済みのゲーム全体設定
    public GameConfig ResultGameConfig { get; private set; } = new();

    private List<EnemyDef> _enemies;
    private List<GimmickDef> _gimmicks;
    private List<ItemDef> _items;
    private GameConfig _config;
    private readonly string? _currentStageName;
    private string _stageBgmId;

    // 音の候補（IDと表示名）。SE はふつうの効果音と UI 音を合わせたもの。
    private readonly List<SoundDef> _se, _bgm;
    private readonly Dictionary<string, string> _fileOf = new();   // 音ID → 音声ファイル（試聴用）
    private readonly Dictionary<string, string> _nameOf = new();   // 音ID → 表示名

    // 行定義：(表示ラベル, GameConfig 側のプロパティ名)。項目を増やすときはここへ1行足すだけ。
    private static readonly (string Label, string Prop)[] PlayerSeRows =
    {
        ("ジャンプ", "jump"), ("着地", "land"), ("ダッシュ", "dash"), ("射撃", "shoot"), ("被弾", "damage"), ("力尽きる", "death"),
    };
    private static readonly (string Label, string Prop)[] EditSeRows =
    {
        ("巻き戻し", "rewind"), ("拡大縮小を確定", "scale"), ("回転を確定", "rotate"), ("移動を確定", "move"), ("向きを反転", "flip"),
        ("編集をリセット", "reset"), ("コマ送り", "step"), ("一時停止／解除", "pause"), ("早送り切替", "fast_forward"),
        ("色フィルタ切替", "color_filter"), ("カット確定", "cut"), ("操作できない", "denied"), ("編集ゲージ切れ", "cost_empty"),
    };
    private static readonly (string Label, string Prop)[] MetaSeRows =
    {
        ("カーソル移動", "cursor"), ("決定", "decide"), ("戻る", "cancel"), ("ステージクリア", "clear"), ("ゲームオーバー", "gameover"),
    };

    // 割り当て先1つ分。Slots は「場面ごとの音」の取り出し口と書き込み口。
    private sealed class Slot
    {
        public string Label = "";
        public bool IsBgm;
        public Func<string> Get = () => "";
        public Action<string> Set = _ => { };
    }
    private sealed class Target
    {
        public string Key = "";
        public string Group = "";
        public string Title = "";
        public string Kind = "";        // enemy / gimmick / item / stage / player / edit / meta
        public Image? Icon;
        public string Note = "";        // 右の見出しの下に出す説明
        public List<Slot> Slots = new();
    }

    private List<Target> _targets = new();
    private Target? _cur;

    // 履歴（元に戻す）1件ぶん。設定オブジェクトも JSON にして持つ。
    public class Snapshot
    {
        public List<EnemyDef> Enemies { get; set; } = new();
        public List<GimmickDef> Gimmicks { get; set; } = new();
        public List<ItemDef> Items { get; set; } = new();
        public string StageBgmId { get; set; } = "";
        public string ConfigJson { get; set; } = "";
    }
    private readonly HistoryManager<Snapshot> _history = new();

    private readonly AssetThumbs _thumbs = new(Path.Combine(AppPaths.ProjectRoot, "assets"));
    private SoundPlayer? _player;

    private readonly TextBox _txtSearch = new() { Dock = DockStyle.Fill, PlaceholderText = "🔍 名前・IDで探す", BorderStyle = BorderStyle.FixedSingle };
    private readonly ChipBar _chips = new();
    private readonly CardListView _list = new() { Dock = DockStyle.Fill, LayoutMode = CardLayout.Rows };
    private readonly FieldStack _detail = new() { Dock = DockStyle.Fill };

    private sealed class Choice
    {
        public string Id = ""; public string Text = "";
        public override string ToString() => Text;
    }

    public SoundAssignmentForm(List<EnemyDef> enemies, List<GimmickDef> gimmicks, List<ItemDef> items,
        List<SoundDef> se, List<SoundDef> uiSe, List<SoundDef> bgm,
        string? currentStageName, string currentStageBgmId, GameConfig config)
        : base("サウンド割り当て", "登録した音を、鳴らしたい場面に結びつけます。空欄は「鳴らさない」です。",
               new Size(1180, 740), new Size(940, 600))
    {
        _enemies = CloneList(enemies);
        _gimmicks = CloneList(gimmicks);
        _items = CloneList(items);
        _currentStageName = currentStageName;
        _stageBgmId = currentStageBgmId ?? "";
        // 設定は JSON 経由で複製する（未知のキーも失わずに持ち越せる）。
        _config = JsonConvert.DeserializeObject<GameConfig>(JsonConvert.SerializeObject(config)) ?? new GameConfig();

        _se = se.Concat(uiSe).ToList();
        _bgm = bgm.ToList();
        foreach (var d in _se.Concat(_bgm)) { _fileOf[d.id] = d.file; _nameOf[d.id] = d.name; }

        BuildUi();
        RebuildTargets();
        _history.Push(TakeSnapshot());
        InitUndo(Undo, Redo);
        UpdateUndoState(_history.CanUndo, _history.CanRedo);
        RebuildList();
        var first = _targets.FirstOrDefault();
        if (first != null) Select(first);
    }

    private static List<T> CloneList<T>(List<T> src) =>
        JsonConvert.DeserializeObject<List<T>>(JsonConvert.SerializeObject(src)) ?? new List<T>();

    // ── 画面の組み立て ───────────────────────────
    private void BuildUi()
    {
        var left = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.PanelBack };
        var searchHost = new Panel { Dock = DockStyle.Top, Height = UiTheme.S(this, 36), Padding = new Padding(8, 8, 8, 4) };
        searchHost.Controls.Add(_txtSearch);
        _txtSearch.TextChanged += (s, e) => RebuildList();
        _chips.SelectionChanged += (s, e) => RebuildList();
        left.Controls.Add(_list);
        left.Controls.Add(_chips);
        left.Controls.Add(searchHost);
        _list.ItemClicked += (s, item) => { var t = _targets.FirstOrDefault(x => x.Key == item.Key); if (t != null) Select(t); };

        var right = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.CardBack };
        right.Controls.Add(_detail);

        var split = new SplitContainer { Dock = DockStyle.Fill, SplitterWidth = 6, FixedPanel = FixedPanel.Panel1, BackColor = UiTheme.CardBorder };
        split.Panel1.Controls.Add(left);
        split.Panel2.Controls.Add(right);
        Body.Controls.Add(split);
        SplitWhenReady(split, 280, 480, 360);

        LeftBar.Controls.Add(new Label
        {
            Text = "💡 音は「サウンド管理」で登録します。選ぶとすぐ試聴できます（WAVのみ）。",
            AutoSize = true, ForeColor = UiTheme.TextMuted, Font = UiTheme.Small, Margin = new Padding(0, 8, 0, 0),
        });
    }

    // 割り当て先の一覧を、現在のデータから作り直す。
    private void RebuildTargets()
    {
        _targets = new List<Target>();
        int px = UiTheme.S(this, 40);

        if (_currentStageName != null)
            _targets.Add(new Target
            {
                Key = "stage", Group = "🎮 ステージ", Title = $"ステージのBGM（{_currentStageName}）", Kind = "stage",
                Icon = _thumbs.ForEmoji("🎵", UiTheme.KindTrigger, px),
                Note = "いま開いているステージで流す BGM です。",
                Slots = { new Slot { Label = "BGM", IsBgm = true, Get = () => _stageBgmId, Set = v => _stageBgmId = v } },
            });
        else
            _targets.Add(new Target
            {
                Key = "stage", Group = "🎮 ステージ", Title = "ステージのBGM", Kind = "stage",
                Icon = _thumbs.ForEmoji("🎵", Color.Gray, px),
                Note = "先にメイン画面でステージを開いてください。ステージが未選択のため、ここでは設定できません。",
            });

        _targets.Add(MakeConfigTarget("player", "🎮 プレイヤー・システム", "プレイヤー操作の音", "🏃", PlayerSeRows, _config.player_se,
            "ジャンプ・着地・被弾など、プレイヤーを動かすときの音です。"));
        _targets.Add(MakeConfigTarget("edit", "🎮 プレイヤー・システム", "編集ツールの音", "✏️", EditSeRows, _config.edit_se,
            "巻き戻し・拡大縮小・カットなど、編集操作を行うときの音です。"));
        _targets.Add(MakeConfigTarget("meta", "🎮 プレイヤー・システム", "タイトル・リザルトの音", "🏁", MetaSeRows, _config.meta_se,
            "メニュー操作や、ステージクリア・ゲームオーバーの音です。"));

        foreach (var en in _enemies)
        {
            var e = en;
            _targets.Add(new Target
            {
                Key = "enemy:" + e.id, Group = "👾 敵", Title = e.name.Length > 0 ? e.name : e.id, Kind = "enemy",
                Icon = _thumbs.ForEnemy(e, px), Note = $"ID: {e.id}",
                Slots =
                {
                    new Slot { Label = "出現したとき", Get = () => e.seSpawn, Set = v => e.seSpawn = v },
                    new Slot { Label = "攻撃するとき", Get = () => e.seAttack, Set = v => e.seAttack = v },
                    new Slot { Label = "ダメージを受けたとき", Get = () => e.seDamage, Set = v => e.seDamage = v },
                    new Slot { Label = "倒されたとき", Get = () => e.seDeath, Set = v => e.seDeath = v },
                },
            });
        }
        foreach (var gi in _gimmicks)
        {
            var g = gi;
            _targets.Add(new Target
            {
                Key = "gimmick:" + g.id, Group = "🔧 ギミック", Title = g.name.Length > 0 ? g.name : g.id, Kind = "gimmick",
                Icon = _thumbs.ForGimmick(g, px), Note = $"ID: {g.id}",
                Slots = { new Slot { Label = "作動したとき", Get = () => g.seActivate, Set = v => g.seActivate = v } },
            });
        }
        foreach (var it in _items)
        {
            var i = it;
            _targets.Add(new Target
            {
                Key = "item:" + i.id, Group = "💎 アイテム", Title = i.name.Length > 0 ? i.name : i.id, Kind = "item",
                Icon = _thumbs.ForItem(i, px), Note = $"ID: {i.id}",
                Slots = { new Slot { Label = "取ったとき", Get = () => i.seCollect, Set = v => i.seCollect = v } },
            });
        }
    }

    // GameConfig の項目群（プレイヤー・編集・メタ）を、プロパティ名のリフレクションで読み書きする割り当て先にする。
    private Target MakeConfigTarget(string key, string group, string title, string emoji, (string Label, string Prop)[] rows, object target, string note)
    {
        var t = new Target
        {
            Key = key, Group = group, Title = title, Kind = key, Note = note,
            Icon = _thumbs.ForEmoji(emoji, UiTheme.KindSpecial, UiTheme.S(this, 40)),
        };
        foreach (var (label, prop) in rows)
        {
            var pi = target.GetType().GetProperty(prop);
            if (pi == null) continue;
            t.Slots.Add(new Slot
            {
                Label = label,
                Get = () => pi.GetValue(target) as string ?? "",
                Set = v => pi.SetValue(target, v),
            });
        }
        return t;
    }

    // ── 一覧 ────────────────────────────────────
    private void RebuildList()
    {
        string q = _txtSearch.Text.Trim();
        string chip = _chips.SelectedValue;
        _chips.SetChips(new[]
        {
            ("all", "すべて"), ("enemy", $"👾 敵 {_targets.Count(t => t.Kind == "enemy")}"),
            ("gimmick", $"🔧 {_targets.Count(t => t.Kind == "gimmick")}"), ("item", $"💎 {_targets.Count(t => t.Kind == "item")}"),
            ("sys", "🎮 システム"),
        }, chip.Length > 0 ? chip : "all");
        chip = _chips.SelectedValue;

        var items = new List<CardItem>();
        foreach (var t in _targets)
        {
            bool isSys = t.Kind is "stage" or "player" or "edit" or "meta";
            if (chip == "enemy" && t.Kind != "enemy") continue;
            if (chip == "gimmick" && t.Kind != "gimmick") continue;
            if (chip == "item" && t.Kind != "item") continue;
            if (chip == "sys" && !isSys) continue;
            if (q.Length > 0 && !t.Title.Contains(q, StringComparison.OrdinalIgnoreCase) && !t.Key.Contains(q, StringComparison.OrdinalIgnoreCase)) continue;

            int filled = t.Slots.Count(s => !string.IsNullOrEmpty(s.Get()));
            string sub = t.Slots.Count == 0 ? "（ステージ未選択）" : filled == t.Slots.Count ? $"✔ すべて設定済み（{filled}）" : filled == 0 ? $"未設定（0/{t.Slots.Count}）" : $"設定 {filled}/{t.Slots.Count}";
            items.Add(new CardItem
            {
                Key = t.Key, Title = t.Title, Subtitle = sub, Group = t.Group, Icon = t.Icon,
                Accent = t.Kind switch { "enemy" => UiTheme.KindEnemy, "gimmick" => UiTheme.KindGimmick, "item" => UiTheme.KindItem, _ => UiTheme.KindSpecial },
            });
        }
        _list.EmptyText = "該当する割り当て先がありません";
        _list.SetItems(items);
        if (_cur != null) _list.SetSelectedKey(_cur.Key, scrollIntoView: false);
    }

    private void Select(Target t)
    {
        _cur = t;
        _list.SetSelectedKey(t.Key);
        ShowDetail(t);
    }

    // ── 詳細（場面ごとの音） ─────────────────────
    private void ShowDetail(Target t)
    {
        _detail.SuspendLayout();
        _detail.ClearItems();

        var head = new Panel { Height = 56, Margin = new Padding(0, 0, 0, 6) };
        var pic = new PictureBox { Image = t.Icon, SizeMode = PictureBoxSizeMode.Zoom, Dock = DockStyle.Left, Width = 56 };
        var title = new Label { Text = t.Title, Dock = DockStyle.Top, Height = 28, Font = new Font("Meiryo UI", 12f, FontStyle.Bold), TextAlign = ContentAlignment.BottomLeft, Padding = new Padding(8, 0, 0, 0) };
        var note = new Label { Text = t.Note, Dock = DockStyle.Fill, Font = UiTheme.Small, ForeColor = UiTheme.TextMuted, Padding = new Padding(8, 2, 0, 0) };
        head.Controls.Add(note);
        head.Controls.Add(title);
        head.Controls.Add(pic);
        _detail.AddItem(head);

        foreach (var slot in t.Slots)
            _detail.AddItem(MakeSlotRow(slot));

        _detail.ResumeLayout(true);
        _detail.ScrollToTop();
    }

    // 1つの場面ぶん：項目名 ＋ 音の選択 ＋ 試聴 ＋ 解除。
    private Control MakeSlotRow(Slot slot)
    {
        var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Font = UiTheme.Base, DropDownWidth = 360 };
        var list = slot.IsBgm ? _bgm : _se;
        combo.Items.Add(new Choice { Id = "", Text = "（未設定：鳴らさない）" });
        foreach (var d in list) combo.Items.Add(new Choice { Id = d.id, Text = $"{(d.name.Length > 0 ? d.name : d.id)}   〔{d.id}〕" });
        string cur = slot.Get() ?? "";
        int idx = combo.Items.Cast<Choice>().ToList().FindIndex(c => c.Id == cur);
        if (idx < 0 && cur.Length > 0)
        {
            // カタログに無い古い ID が入っているときは、消さずに残して警告表示にする。
            combo.Items.Add(new Choice { Id = cur, Text = $"⚠ {cur}（登録されていません）" });
            idx = combo.Items.Count - 1;
        }
        combo.SelectedIndex = Math.Max(0, idx);

        var btnPlay = Kit.Btn("▶", () => Play(slot.Get()));
        btnPlay.AutoSize = false; btnPlay.Size = new Size(36, 28); btnPlay.Padding = new Padding(0);
        var btnClear = Kit.Btn("✕", () => { combo.SelectedIndex = 0; });
        btnClear.AutoSize = false; btnClear.Size = new Size(36, 28); btnClear.Padding = new Padding(0);

        combo.SelectedIndexChanged += (s, e) =>
        {
            if (combo.SelectedItem is not Choice c) return;
            if (slot.Get() == c.Id) return;
            slot.Set(c.Id);
            MarkDirty();
            _history.Push(TakeSnapshot());
            UpdateUndoState(_history.CanUndo, _history.CanRedo);
            RebuildList();
            // 選んだらすぐ聞けるよう、WAVなら自動で鳴らす。
            if (c.Id.Length > 0) Play(c.Id);
        };

        var row = new Panel { Height = 30 };
        combo.Dock = DockStyle.Fill;
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Right, Width = 86, WrapContents = false, Padding = new Padding(6, 0, 0, 0) };
        btnPlay.Margin = new Padding(0, 0, 4, 0); btnClear.Margin = new Padding(0);
        buttons.Controls.Add(btnPlay);
        buttons.Controls.Add(btnClear);
        row.Controls.Add(combo);
        row.Controls.Add(buttons);
        return Kit.Field(slot.Label, row);
    }

    private void Play(string id)
    {
        if (string.IsNullOrEmpty(id) || !_fileOf.TryGetValue(id, out var rel) || string.IsNullOrEmpty(rel)) return;
        string full = Path.Combine(AppPaths.ProjectRoot, rel.Replace('/', '\\'));
        if (!File.Exists(full) || !full.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)) return;   // WAV 以外は黙って何もしない
        try { _player?.Stop(); _player?.Dispose(); _player = new SoundPlayer(full); _player.Play(); }
        catch { /* 試聴の失敗は編集を止めない */ }
    }

    // ── 元に戻す／やり直し ───────────────────────
    private Snapshot TakeSnapshot() => new()
    {
        Enemies = CloneList(_enemies), Gimmicks = CloneList(_gimmicks), Items = CloneList(_items),
        StageBgmId = _stageBgmId, ConfigJson = JsonConvert.SerializeObject(_config),
    };

    private void Restore(Snapshot? s)
    {
        if (s == null) return;
        _enemies = s.Enemies; _gimmicks = s.Gimmicks; _items = s.Items; _stageBgmId = s.StageBgmId;
        _config = JsonConvert.DeserializeObject<GameConfig>(s.ConfigJson) ?? new GameConfig();
        string? key = _cur?.Key;
        RebuildTargets();
        RebuildList();
        var t = _targets.FirstOrDefault(x => x.Key == key) ?? _targets.FirstOrDefault();
        if (t != null) Select(t);
        UpdateUndoState(_history.CanUndo, _history.CanRedo);
    }

    private void Undo() { if (_history.CanUndo) Restore(_history.Undo()); }
    private void Redo() { if (_history.CanRedo) Restore(_history.Redo()); }

    // ── 保存 ────────────────────────────────────
    protected override void OnSave()
    {
        ResultEnemies = CloneList(_enemies);
        ResultGimmicks = CloneList(_gimmicks);
        ResultItems = CloneList(_items);
        ResultStageBgmId = _currentStageName != null ? _stageBgmId : null;
        ResultGameConfig = _config;
        DialogResult = DialogResult.OK;
        Close();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _player?.Stop(); _player?.Dispose();
        _thumbs.Dispose();
        base.OnFormClosed(e);
    }
}
