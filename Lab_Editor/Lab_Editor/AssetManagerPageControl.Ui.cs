using Newtonsoft.Json;

namespace Lab_Editor;

// ======================================================
// アセットワークベンチ（AssetManagerPageControl）の画面部分
//
// 以前は、敵・ギミック・アイテム・コモンイベントが4つの大きな表（DataGridView）として縦に積まれ、
// 右に細長いプレビューと説明欄、下に10個前後のボタンが並んでいた。表の列には ID・タイプ番号・幅px などが
// 並び、絵も見えず、1つの項目を直すためにスクロールして探す作りだった。
//
// 今回は画面（見た目と操作）だけを次の作りへ改めた。データの持ち方・保存・複製・テンプレート／プリセット作成・
// パーツ編集などの処理は、以前のまま（従来の表を「画面に出さないデータ置き場」として使い続ける）。
//   左 : 敵・ギミック・アイテム・コモンイベントの、絵つきカード一覧（検索と種類の絞り込み）
//   右 : 選んだ物の大きなプレビューと、日本語の項目名のフォーム
//        （画像・当たり判定・大きさ・パーツ／ID・名前・タイプ／HP・大きさ／タイプごとの調整項目／編集の禁止）
//   下 : 新しく作る（メニュー）・テンプレートやプリセットから作る・保存
// ======================================================
public partial class AssetManagerPageControl
{
    // 画面に出さない「データ置き場」の表3つ（行がそのまま1件の定義にあたる）を載せておく入れ物。
    private Panel _modelHost = null!;

    // いま選んでいる物。表の行か、コモンイベントのどちらか一方。
    private DataGridView? _curDgv;
    private DataGridViewRow? _curRow;
    private CommonEventDef? _curCe;

    private AssetThumbs _thumbs = null!;
    private TextBox _txtSearch = null!;
    private ChipBar _chips = null!;
    private CardListView _cards = null!;
    private FieldStack _detail = null!;
    private Label _emptyDetail = null!;
    private ImageViewport _preview = null!;
    private bool _dirty;
    private bool _building;     // 詳細フォームを組み立てている最中（入力欄の変更イベントを無視する）

    // 一覧の行 ↔ カード。参照の同一性で対応づける。
    private readonly Dictionary<object, string> _keyOf = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<string, (AssetKind kind, DataGridView? dgv, DataGridViewRow? row, CommonEventDef? ce)> _byKey = new();
    private int _keySeq;
    private readonly System.Windows.Forms.Timer _refreshTimer = new() { Interval = 250 };

    // ── 画面の組み立て ───────────────────────────
    private void InitUI()
    {
        Dock = DockStyle.Fill;
        Font = UiTheme.Base;
        BackColor = UiTheme.PanelBack;
        _thumbs = new AssetThumbs(assetsPath);

        // データ置き場の表。画面には出さない（Visible=false）が、データの出し入れはこれまでどおり表のセルで行う。
        dgvEnemies = CreateEnemyGrid();
        dgvGimmicks = CreateGimmickGrid();
        dgvItems = CreateItemGrid();
        _modelHost = new Panel { Visible = false, Size = new Size(10, 10) };
        _modelHost.Controls.Add(dgvEnemies);
        _modelHost.Controls.Add(dgvGimmicks);
        _modelHost.Controls.Add(dgvItems);
        Controls.Add(_modelHost);

        // ── 左: 検索・絞り込み・カード一覧 ──
        var left = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.PanelBack };
        var searchHost = new Panel { Dock = DockStyle.Top, Height = UiTheme.S(this, 38), Padding = new Padding(8, 8, 8, 4) };
        _txtSearch = new TextBox { Dock = DockStyle.Fill, PlaceholderText = "🔍 ID・名前で探す", BorderStyle = BorderStyle.FixedSingle };
        searchHost.Controls.Add(_txtSearch);
        _txtSearch.TextChanged += (s, e) => RebuildCards();
        _chips = new ChipBar();
        _chips.SelectionChanged += (s, e) => RebuildCards();
        _cards = new CardListView { Dock = DockStyle.Fill };
        _cards.ItemClicked += (s, item) => { if (_byKey.TryGetValue(item.Key, out var t)) SelectTarget(t.kind, t.dgv, t.row, t.ce); };
        _cards.ItemActivated += (s, item) => { if (_curCe != null) EditSelectedCommonEvent(); };
        _cards.DeletePressed += (s, item) => DeleteCurrent();
        left.Controls.Add(_cards);
        left.Controls.Add(_chips);
        left.Controls.Add(searchHost);

        // ── 右: 詳細フォーム ──
        var right = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.CardBack };
        _detail = new FieldStack { Dock = DockStyle.Fill };
        _emptyDetail = new Label
        {
            Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = UiTheme.TextMuted, BackColor = UiTheme.CardBack,
            Text = "左の一覧から、編集したい物を選んでください。\r\n新しく作るときは、下の「＋ 新しく作る」から。",
        };
        right.Controls.Add(_detail);
        right.Controls.Add(_emptyDetail);

        var split = new SplitContainer { Dock = DockStyle.Fill, SplitterWidth = 6, FixedPanel = FixedPanel.Panel1, BackColor = UiTheme.CardBorder };
        split.Panel1.Controls.Add(left);
        split.Panel2.Controls.Add(right);
        split.HandleCreated += (s, e) => SplitLayout.Apply(split, 280, 480, 400);
        split.SizeChanged += (s, e) => { if (split.Panel1MinSize == 0) SplitLayout.Apply(split, 280, 480, 400); };

        // ── 下: 操作ボタン ──
        var foot = new Panel { Dock = DockStyle.Bottom, Height = 56, BackColor = UiTheme.CardBack };
        var footLine = new Panel { Dock = DockStyle.Top, Height = 1, BackColor = UiTheme.CardBorder };
        var leftBar = new FlowLayoutPanel { Dock = DockStyle.Left, AutoSize = true, WrapContents = false, Padding = new Padding(14, 12, 0, 8), BackColor = UiTheme.CardBack };
        var rightBar = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, WrapContents = false, Padding = new Padding(0, 12, 14, 8), BackColor = UiTheme.CardBack };

        var btnNew = Kit.Btn("＋ 新しく作る ▾", () => { });
        btnNew.Click += (s, e) => ShowNewMenu(btnNew);
        var btnTemplate = Kit.Btn("✨ 敵をテンプレートから作る", () => { BtnNewEnemyFromTemplate_Click(); });
        var btnPreset = Kit.Btn("🎨 色・明るさのプリセット ▾", () => { });
        btnPreset.Click += (s, e) => ShowPresetMenu(btnPreset);
        leftBar.Controls.Add(btnNew);
        leftBar.Controls.Add(btnTemplate);
        leftBar.Controls.Add(btnPreset);

        btnSave = Kit.Btn("💾  保存して閉じる", () => { }, primary: true);
        btnSave.Padding = new Padding(12, 6, 12, 6);
        btnSave.Margin = new Padding(0);
        btnSave.Click += BtnSave_Click;
        btnClose = Kit.Btn("キャンセル", () => { });
        btnClose.Padding = new Padding(12, 6, 12, 6);
        btnClose.Margin = new Padding(0, 0, 10, 0);
        btnClose.Click += (s, e) =>
        {
            if (_dirty && MessageBox.Show("変更したところがあります。保存せずに閉じますか？", "確認", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            Cancelled?.Invoke(this, EventArgs.Empty);
        };
        rightBar.Controls.Add(btnClose);
        rightBar.Controls.Add(btnSave);
        foot.Controls.Add(rightBar);
        foot.Controls.Add(leftBar);
        foot.Controls.Add(footLine);

        // Dock の決まり：Fill を先に、あとから足したものが外側。
        Controls.Add(split);
        Controls.Add(foot);

        _refreshTimer.Tick += (s, e) => { _refreshTimer.Stop(); RebuildCards(); };
        RebuildCards();
        ShowDetailEmpty();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _refreshTimer.Dispose(); _thumbs.Dispose(); _preview?.Image?.Dispose(); }
        base.Dispose(disposing);
    }

    // 「＋ 新しく作る」のメニュー。
    private void ShowNewMenu(Control anchor)
    {
        var m = new ContextMenuStrip { Font = UiTheme.Base };
        m.Items.Add("👾 敵", null, (s, e) => { AddRow(dgvEnemies, GetDefaultEnemyRow()); });
        m.Items.Add("🔧 ギミック", null, (s, e) => { AddRow(dgvGimmicks, GetDefaultGimmickRow()); });
        m.Items.Add("💎 アイテム", null, (s, e) => { AddRow(dgvItems, GetDefaultItemRow()); });
        m.Items.Add("🔔 コモンイベント", null, (s, e) => AddCommonEvent());
        m.Closed += (s, e) => m.Dispose();
        m.Show(anchor, new Point(0, -m.GetPreferredSize(Size.Empty).Height));
    }

    private void ShowPresetMenu(Control anchor)
    {
        var m = new ContextMenuStrip { Font = UiTheme.Base };
        m.Items.Add("👾 色・明るさの敵プリセット", null, (s, e) => BtnNewEnemyFromPreset_Click());
        m.Items.Add("🔧 色・明るさのギミックプリセット", null, (s, e) => BtnNewGimmickFromPreset_Click());
        m.Closed += (s, e) => m.Dispose();
        m.Show(anchor, new Point(0, -m.GetPreferredSize(Size.Empty).Height));
    }

    // ── 一覧（カード） ───────────────────────────
    private string KeyFor(object o, string prefix)
    {
        if (!_keyOf.TryGetValue(o, out var k)) { k = prefix + _keySeq++; _keyOf[o] = k; }
        return k;
    }

    private static string RowText(DataGridViewRow r, string col) => r.Cells[col].Value?.ToString() ?? "";

    // 画面に見せるためだけの、軽い定義（保存には使わない）。見た目に関わる値を行のセルから集める。
    private EnemyDef QuickEnemy(DataGridViewRow r) => new()
    {
        id = RowText(r, "id"), name = RowText(r, "name"), type_enum = GetSelectedTypeEnum(r),
        width = IntCell(r, "width", 32), height = IntCell(r, "height", 32), scale = FloatCell(r, "scale", 1f),
        sprite = RowText(r, "sprite"), hitboxWidth = IntCell(r, "hitboxWidth", 32), hitboxHeight = IntCell(r, "hitboxHeight", 32),
        parts = GetOrCreateEnemyParams(r).parts,
    };
    private GimmickDef QuickGimmick(DataGridViewRow r) => new()
    {
        id = RowText(r, "id"), name = RowText(r, "name"), type_enum = GetSelectedTypeEnum(r), sprite = RowText(r, "sprite"),
        hitboxWidth = IntCell(r, "hitboxWidth", 32), hitboxHeight = IntCell(r, "hitboxHeight", 32), parts = GetOrCreateGimmickParams(r).parts,
    };
    private ItemDef QuickItem(DataGridViewRow r) => new()
    {
        id = RowText(r, "id"), name = RowText(r, "name"), type_enum = GetSelectedTypeEnum(r), sprite = RowText(r, "sprite"),
        hitboxWidth = IntCell(r, "hitboxWidth", 32), hitboxHeight = IntCell(r, "hitboxHeight", 32), parts = GetOrCreateItemParams(r).parts,
    };

    // 名前のうち、全角かっこより前の部分（カードの幅が狭いので、長い説明は省く）。
    private static string ShortName(string name)
    {
        int k = name.IndexOf('（');
        return k > 0 ? name[..k].Trim() : name;
    }

    private void RebuildCards()
    {
        _refreshTimer.Stop();
        _thumbs.ClearThumbs();
        string q = _txtSearch.Text.Trim();
        int nE = dgvEnemies.Rows.Cast<DataGridViewRow>().Count(r => !r.IsNewRow);
        int nG = dgvGimmicks.Rows.Cast<DataGridViewRow>().Count(r => !r.IsNewRow);
        int nI = dgvItems.Rows.Cast<DataGridViewRow>().Count(r => !r.IsNewRow);
        _chips.SetChips(new[]
        {
            ("all", "すべて"), ("enemy", $"👾 敵 {nE}"), ("gimmick", $"🔧 ギミック {nG}"),
            ("item", $"💎 アイテム {nI}"), ("common", $"🔔 コモン {_commonEvents.Count}"),
        }, _chips.SelectedValue.Length > 0 ? _chips.SelectedValue : "all");
        string chip = _chips.SelectedValue;
        int px = UiTheme.S(this, 48);

        bool Match(string id, string name) => q.Length == 0 || id.Contains(q, StringComparison.OrdinalIgnoreCase) || name.Contains(q, StringComparison.OrdinalIgnoreCase);

        // 短くすると同じ名前になる物は、見分けがつくよう全文を使う。
        var allNames = new List<string>();
        foreach (DataGridViewRow r in dgvEnemies.Rows) if (!r.IsNewRow) allNames.Add(RowText(r, "name"));
        foreach (DataGridViewRow r in dgvGimmicks.Rows) if (!r.IsNewRow) allNames.Add(RowText(r, "name"));
        foreach (DataGridViewRow r in dgvItems.Rows) if (!r.IsNewRow) allNames.Add(RowText(r, "name"));
        var shortCount = new Dictionary<string, int>();
        foreach (var n in allNames) shortCount[ShortName(n)] = shortCount.GetValueOrDefault(ShortName(n)) + 1;
        string Title(string name, string id) => name.Length == 0 ? (id.Length > 0 ? id : "（名前なし）") : shortCount.GetValueOrDefault(ShortName(name)) > 1 ? name : ShortName(name);

        _byKey.Clear();
        var items = new List<CardItem>();
        void Add(AssetKind kind, DataGridView dgv, string group, Color accent, Func<DataGridViewRow, Image> icon)
        {
            foreach (DataGridViewRow r in dgv.Rows)
            {
                if (r.IsNewRow) continue;
                string id = RowText(r, "id"), name = RowText(r, "name");
                if (!Match(id, name)) continue;
                string key = KeyFor(r, kind.ToString());
                _byKey[key] = (kind, dgv, r, null);
                items.Add(new CardItem
                {
                    Key = key, Title = Title(name, id), Group = group, Accent = accent, Icon = icon(r),
                    Tip = $"{name}\nID: {id}\nタイプ: {RowText(r, "type_enum")}",
                });
            }
        }
        if (chip is "all" or "enemy") Add(AssetKind.Enemy, dgvEnemies, "👾 敵", UiTheme.KindEnemy, r => _thumbs.ForEnemy(QuickEnemy(r), px));
        if (chip is "all" or "gimmick") Add(AssetKind.Gimmick, dgvGimmicks, "🔧 ギミック", UiTheme.KindGimmick, r => _thumbs.ForGimmick(QuickGimmick(r), px));
        if (chip is "all" or "item") Add(AssetKind.Item, dgvItems, "💎 アイテム", UiTheme.KindItem, r => _thumbs.ForItem(QuickItem(r), px));
        if (chip is "all" or "common")
            foreach (var ce in _commonEvents)
            {
                if (!Match(ce.id, ce.name)) continue;
                string key = KeyFor(ce, "ce");
                _byKey[key] = (AssetKind.CommonEvent, null, null, ce);
                items.Add(new CardItem
                {
                    Key = key, Title = ce.name.Length > 0 ? ce.name : ce.id, Group = "🔔 コモンイベント", Accent = UiTheme.KindTrigger,
                    Icon = _thumbs.ForEmoji("🔔", UiTheme.KindTrigger, px), Tip = $"{ce.name}\nID: {ce.id}\nアクション {ce.actions.Count} 件",
                });
            }
        _cards.EmptyText = q.Length > 0 ? $"「{q}」に合う物がありません" : "まだ何もありません。\r\n下の「＋ 新しく作る」から作れます。";
        _cards.SetItems(items);
        object? cur = (object?)_curRow ?? _curCe;
        if (cur != null && _keyOf.TryGetValue(cur, out var ck)) _cards.SetSelectedKey(ck, scrollIntoView: false);
    }

    // 編集のたびに全部作り直すと重いので、少し待ってから1回だけ作り直す。
    private void ScheduleRebuild() { _refreshTimer.Stop(); _refreshTimer.Start(); }

    // ── 選択 ────────────────────────────────────
    // 選んでいる行（敵・ギミック・アイテムのどれか。選んでいなければ null）
    private DataGridViewRow? SelRow(DataGridView dgv) => ReferenceEquals(_curDgv, dgv) ? _curRow : null;

    private void SelectTarget(AssetKind kind, DataGridView? dgv, DataGridViewRow? row, CommonEventDef? ce)
    {
        _curDgv = dgv; _curRow = row; _curCe = ce;
        object? cur = (object?)row ?? ce;
        if (cur != null && _keyOf.TryGetValue(cur, out var k)) _cards.SetSelectedKey(k);
        if (kind == AssetKind.CommonEvent && ce != null) ShowCommonDetail(ce);
        else if (dgv != null && row != null) ShowDetail(kind, dgv, row);
        else ShowDetailEmpty();
    }

    // 表の行を選ぶ（新しく作った直後など、外から呼ぶ）。
    private void SelectRow(DataGridView dgv, DataGridViewRow row)
    {
        var kind = dgv == dgvEnemies ? AssetKind.Enemy : dgv == dgvGimmicks ? AssetKind.Gimmick : AssetKind.Item;
        _curDgv = dgv; _curRow = row; _curCe = null;
        RebuildCards();
        SelectTarget(kind, dgv, row, null);
    }

    // 作り直したり、別の方法で値を変えたあとに、いま選んでいる物の表示を最新にする。
    private void ReselectCurrent()
    {
        RebuildCards();
        if (_curDgv != null && _curRow != null && _curDgv.Rows.Contains(_curRow))
            SelectTarget(KindOf(_curDgv), _curDgv, _curRow, null);
        else if (_curCe != null && _commonEvents.Contains(_curCe)) SelectTarget(AssetKind.CommonEvent, null, null, _curCe);
        else ShowDetailEmpty();
    }

    private AssetKind KindOf(DataGridView dgv) => dgv == dgvEnemies ? AssetKind.Enemy : dgv == dgvGimmicks ? AssetKind.Gimmick : AssetKind.Item;

    private void ShowDetailEmpty()
    {
        _curDgv = null; _curRow = null; _curCe = null;
        _emptyDetail.Visible = true;
        _detail.Visible = false;
    }

    // 以前の呼び出し口（タイプを変えたあとなどに、右の調整欄を作り直していた）。いまは詳細フォーム全体を作り直す。
    private void UpdateBehaviorParamsPanel(DataGridView dgv, AssetKind kind)
    {
        if (SelRow(dgv) != null) SelectTarget(kind, dgv, _curRow, null);
    }

    // ── 詳細フォーム（敵・ギミック・アイテム） ─────
    private static readonly string[] KindNames = { "敵", "ギミック", "アイテム" };

    private (int type, string desc, string detail)[] TypesOf(AssetKind kind) => kind switch
    {
        AssetKind.Enemy => EnemyTypes, AssetKind.Gimmick => GimmickTypes, _ => ItemTypes,
    };

    private void ShowDetail(AssetKind kind, DataGridView dgv, DataGridViewRow row)
    {
        _building = true;
        _emptyDetail.Visible = false;
        _detail.Visible = true;
        _detail.SuspendLayout();
        _detail.ClearItems();

        int typeEnum = GetSelectedTypeEnum(row);
        var types = TypesOf(kind);
        string emoji = kind == AssetKind.Enemy ? "👾" : kind == AssetKind.Gimmick ? "🔧" : "💎";

        // ── 見出し行（名前＋複製・削除）──
        var head = new Panel { Height = 40, Margin = new Padding(0, 0, 0, 4) };
        var title = new Label { Dock = DockStyle.Fill, Font = new Font("Meiryo UI", 12f, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
        string nm = RowText(row, "name");
        title.Text = $"{emoji} {(nm.Length > 0 ? nm : RowText(row, "id"))}";
        var acts = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, WrapContents = false, Padding = new Padding(0, 4, 0, 0) };
        acts.Controls.Add(Kit.Btn("⧉ 複製", () => BtnDuplicate_Click(null, EventArgs.Empty)));
        acts.Controls.Add(Kit.Btn("🗑 削除", DeleteCurrent, danger: true));
        head.Controls.Add(title);
        head.Controls.Add(acts);
        _detail.AddItem(head);

        // ── 画像のプレビュー（赤い枠＝当たり判定）──
        var oldImg = _preview?.Image;
        _preview = new ImageViewport { Height = 190, Placeholder = "画像が未設定です。「📁 画像を選ぶ」で選んでください" };
        _preview.Overlay = (g, r, k) => DrawHitboxOverlay(g, r, kind, row);
        _detail.AddItem(_preview);
        _detail.AddItem(Kit.Hint("赤い点線の枠が当たり判定です。"));
        oldImg?.Dispose();
        LoadPreviewImage(row);

        // ── 画像・形に関する操作 ──
        var ops = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Margin = new Padding(0, 2, 0, 0) };
        ops.Controls.Add(Kit.Btn("📁 画像を選ぶ", () => RowButton(dgv, row, "btnSprite"), primary: true));
        ops.Controls.Add(Kit.Btn("🎯 当たり判定", () => RowButton(dgv, row, "btnHitbox")));
        ops.Controls.Add(Kit.Btn("📏 大きさ", () => RowButton(dgv, row, "btnSize")));
        ops.Controls.Add(Kit.Btn("🧩 パーツを編集", BtnPartsEditor_Click));
        if (kind == AssetKind.Enemy) ops.Controls.Add(Kit.Btn("🎛 敵の動きを調整", BtnEnemyTune_Click));
        if ((kind == AssetKind.Enemy && typeEnum == CustomScriptEnemyType) || (kind == AssetKind.Gimmick && typeEnum == CustomScriptGimmickType))
            ops.Controls.Add(Kit.Btn("🧩 挙動スクリプトを編集", BtnBehaviorScript_Click));
        _detail.AddItem(ops);
        var spr = RowText(row, "sprite");
        _detail.AddItem(new Label { Text = spr.Length == 0 ? "画像: （未設定）" : "画像: " + spr, AutoSize = true, Font = UiTheme.Small, ForeColor = UiTheme.TextMuted, Margin = new Padding(0, 4, 0, 0) });

        // ── 基本 ──
        _detail.AddItem(Kit.Heading("🏷 基本"));
        var warn = new Label { AutoSize = false, Height = 0, ForeColor = Color.Firebrick, Font = UiTheme.Small, Tag = "hint", Visible = false };
        _detail.AddItem(warn);
        var txtId = new TextBox { Font = UiTheme.Base, Text = RowText(row, "id") };
        var txtName = new TextBox { Font = UiTheme.Base, Text = nm };
        txtId.TextChanged += (s, e) =>
        {
            if (_building) return;
            row.Cells["id"].Value = txtId.Text.Trim();
            _dirty = true; ScheduleRebuild();
            bool dup = dgv.Rows.Cast<DataGridViewRow>().Count(r => !r.IsNewRow && r != row && string.Equals(RowText(r, "id"), txtId.Text.Trim(), StringComparison.OrdinalIgnoreCase)) > 0;
            warn.Text = dup ? "⚠ 同じ ID がほかにもあります。ステージからの参照が混ざるので、変えてください。" : "";
            warn.Visible = dup; warn.Height = dup ? 24 : 0;
        };
        txtName.TextChanged += (s, e) => { if (_building) return; row.Cells["name"].Value = txtName.Text; _dirty = true; ScheduleRebuild(); title.Text = $"{emoji} {(txtName.Text.Length > 0 ? txtName.Text : txtId.Text)}"; };
        _detail.AddItem(Kit.Field("ID（ステージが、この名前でこの物を呼びます）", txtId));
        _detail.AddItem(Kit.Field("名前", txtName));

        var cmbType = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Font = UiTheme.Base, DropDownWidth = 480 };
        foreach (var t in types) cmbType.Items.Add(t.desc);
        cmbType.SelectedIndex = Math.Clamp(typeEnum, 0, types.Length - 1);
        var typeRow = new Panel { Height = 34 };
        cmbType.Dock = DockStyle.Fill;
        var btnCard = Kit.Btn("🔍 カードから選ぶ", BtnTypeCardPicker_Click);
        btnCard.AutoSize = false; btnCard.Dock = DockStyle.Right; btnCard.Width = 150; btnCard.Margin = new Padding(6, 0, 0, 0);
        typeRow.Controls.Add(cmbType);
        typeRow.Controls.Add(btnCard);
        _detail.AddItem(Kit.Field("タイプ（動き方・役割）", typeRow));
        var desc = new Label { Text = types[Math.Clamp(typeEnum, 0, types.Length - 1)].detail, AutoSize = false, Font = UiTheme.Small, ForeColor = UiTheme.TextMuted, Tag = "hint", Margin = new Padding(0, 0, 0, 4) };
        _detail.AddItem(desc);
        cmbType.SelectedIndexChanged += (s, e) =>
        {
            if (_building || cmbType.SelectedIndex < 0) return;
            row.Cells["type_enum"].Value = types[cmbType.SelectedIndex].desc;
            _dirty = true;
            // タイプが変わると、出す調整項目・絵のアイコンが変わるので、画面を作り直す。
            RebuildCards();
            SelectTarget(kind, dgv, row, null);
        };

        if (kind == AssetKind.Enemy)
        {
            var nHp = Kit.Num(1, 99999, 0, 1, 90); Kit.SetNum(nHp, IntCell(row, "hp", 3));
            var nW = Kit.Num(1, 4096, 0, 1, 90); Kit.SetNum(nW, IntCell(row, "width", 32));
            var nH = Kit.Num(1, 4096, 0, 1, 90); Kit.SetNum(nH, IntCell(row, "height", 32));
            nHp.ValueChanged += (s, e) => { if (_building) return; row.Cells["hp"].Value = (int)nHp.Value; _dirty = true; };
            nW.ValueChanged += (s, e) => { if (_building) return; row.Cells["width"].Value = (int)nW.Value; _dirty = true; _preview.Invalidate(); };
            nH.ValueChanged += (s, e) => { if (_building) return; row.Cells["height"].Value = (int)nH.Value; _dirty = true; _preview.Invalidate(); };
            var line = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
            line.Controls.Add(Kit.Field("HP（耐久力）", nHp, null, 120));
            line.Controls.Add(new Panel { Width = 10, Height = 1 });
            line.Controls.Add(Kit.Field("基本の幅 (px)", nW, null, 110));
            line.Controls.Add(new Panel { Width = 10, Height = 1 });
            line.Controls.Add(Kit.Field("基本の高さ (px)", nH, null, 110));
            _detail.AddItem(line);
            _detail.AddItem(Kit.Hint("幅と高さは、ゲーム内での見た目の大きさです。あとから「📏 大きさ」で、画像を見ながら決めることもできます。"));
        }
        else if (kind == AssetKind.Item)
        {
            var txtAb = new TextBox { Font = UiTheme.Base, Text = RowText(row, "grant_ability") };
            txtAb.TextChanged += (s, e) => { if (_building) return; row.Cells["grant_ability"].Value = txtAb.Text; _dirty = true; };
            _detail.AddItem(Kit.Field("取ったときに付ける能力", txtAb, "空欄なら、何も付けません。例: canDoubleJump（2段ジャンプ）、canDash、canShootFireball"));
        }

        BuildBehaviorSections(kind, dgv, row, typeEnum);

        _detail.ResumeLayout(true);
        _detail.ScrollToTop();
        _building = false;
    }

    // 画像のプレビューを読み込む。
    private void LoadPreviewImage(DataGridViewRow row)
    {
        string sp = RowText(row, "sprite");
        _preview.Image = null;
        if (string.IsNullOrEmpty(sp)) return;
        var bmp = Kit.LoadBitmap(Path.Combine(projectRoot, sp.Replace('/', '\\')));
        if (bmp == null) { _preview.Placeholder = "⚠ 画像ファイルが見つかりません\r\n" + sp; return; }
        _preview.Image = bmp;
    }

    // 当たり判定の赤い点線枠を、プレビューの上に描く。
    // 敵は「見た目の大きさ(width×height)」の中の位置、ギミック・アイテムは当たり判定が見た目の大きさそのもの。
    private void DrawHitboxOverlay(Graphics g, RectangleF r, AssetKind kind, DataGridViewRow row)
    {
        int hw = IntCell(row, "hitboxWidth", 32), hh = IntCell(row, "hitboxHeight", 32);
        int ox = IntCell(row, "hitboxOffsetX", 0), oy = IntCell(row, "hitboxOffsetY", 0);
        float lw = kind == AssetKind.Enemy ? IntCell(row, "width", 32) : hw;
        float lh = kind == AssetKind.Enemy ? IntCell(row, "height", 32) : hh;
        if (lw <= 0 || lh <= 0) return;
        float fx = r.Width / lw, fy = r.Height / lh;
        var box = kind == AssetKind.Enemy ? new RectangleF(r.X + ox * fx, r.Y + oy * fy, hw * fx, hh * fy) : r;
        using var pen = new Pen(Color.FromArgb(230, 255, 70, 70), 2) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash };
        g.DrawRectangle(pen, box.X, box.Y, box.Width, box.Height);
    }

    // 表のボタン列（画像選択・当たり判定・大きさ）と同じ処理を、詳細フォームのボタンから呼ぶ。
    private void RowButton(DataGridView dgv, DataGridViewRow row, string column)
    {
        if (!dgv.Columns.Contains(column)) return;
        HandleGridButton(dgv, new DataGridViewCellEventArgs(dgv.Columns[column].Index, row.Index));
    }

    // 画像・当たり判定・大きさが変わったあとに呼ぶ。絵・枠・一覧のカードを最新にする。
    private void OnRowChanged(DataGridView dgv, DataGridViewRow row)
    {
        _dirty = true;
        if (!ReferenceEquals(_curRow, row)) { ScheduleRebuild(); return; }
        _thumbs.ClearThumbs();
        SelectTarget(KindOf(dgv), dgv, row, null);
        RebuildCards();
    }

    // ── 挙動パラメータ（タイプごとの調整項目・共通項目・編集の禁止）──
    private void BuildBehaviorSections(AssetKind kind, DataGridView dgv, DataGridViewRow row, int typeEnum)
    {
        bool isEnemy = kind == AssetKind.Enemy;
        var fieldMap = isEnemy ? EnemyParamFields : (kind == AssetKind.Gimmick ? GimmickParamFields : new Dictionary<int, (string Field, string Label, int Decimals)[]>());
        if (!fieldMap.TryGetValue(typeEnum, out var typeFields)) typeFields = Array.Empty<(string Field, string Label, int Decimals)>();
        object paramsObj = kind == AssetKind.Enemy ? GetOrCreateEnemyParams(row) : kind == AssetKind.Gimmick ? GetOrCreateGimmickParams(row) : (object)GetOrCreateItemParams(row);

        if (typeFields.Length > 0)
        {
            _detail.AddItem(Kit.Heading("⚙ このタイプの調整項目"));
            _detail.AddItem(Kit.Hint("数字は、空のまま（−1）にすると、そのタイプの標準の動きになります。"));
            foreach (var f in typeFields) AddParamField(paramsObj, f.Field, f.Label, f.Decimals);
        }
        if (isEnemy)
        {
            _detail.AddItem(Kit.Heading("🧰 共通の設定"));
            foreach (var f in CommonEnemyParamFields) AddParamField(paramsObj, f.Field, f.Label, f.Decimals);
        }
        _detail.AddItem(Kit.Heading("🔒 ゲーム内の編集の禁止"));
        _detail.AddItem(Kit.Hint(kind == AssetKind.Item
            ? "※ アイテムは今のところ、ゲーム内の編集ツールで選べません。この設定は保存されますが、まだ効果がありません。"
            : "チェックした操作は、プレイヤーがこの物に対して使えなくなります。パズルの制限に使います。"));
        foreach (var f in EditLockParamFields) AddParamField(paramsObj, f.Field, f.Label, f.Decimals);
    }

    // 定義（EnemyDef / GimmickDef / ItemDef）のプロパティ1つを、型に合った入力欄にして詳細フォームへ足す。
    // プロパティ名の文字列からリフレクションで読み書きするので、タイプごとの専用コードは要らない。
    private void AddParamField(object obj, string field, string label, int decimals)
    {
        var prop = obj.GetType().GetProperty(field);
        if (prop == null) return;

        // チェックボックス（真偽値）
        if (prop.PropertyType == typeof(bool))
        {
            var chk = new CheckBox { Text = label, AutoSize = true, Font = UiTheme.Base, Checked = Convert.ToBoolean(prop.GetValue(obj)), Margin = new Padding(0, 3, 0, 3) };
            chk.CheckedChanged += (s, e) => { if (_building) return; prop.SetValue(obj, chk.Checked); _dirty = true; };
            _detail.AddItem(chk);
            return;
        }
        // 名前で選ばせる項目（番号を覚えさせない）
        if (field == "moveMode")
        {
            var cbo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Font = UiTheme.Base };
            cbo.Items.AddRange(new object[] { "自由（どこへでも）", "十字（上下左右だけ）", "X字（斜めだけ）" });
            int cur = Convert.ToInt32(prop.GetValue(obj));
            cbo.SelectedIndex = cur < 0 || cur > 2 ? 0 : cur;
            cbo.SelectedIndexChanged += (s, e) => { if (_building) return; prop.SetValue(obj, cbo.SelectedIndex); _dirty = true; };
            _detail.AddItem(Kit.Field("ドラッグで動かせる向き", cbo));
            return;
        }
        if (field == "gradeChannel")
        {
            var cbo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Font = UiTheme.Base };
            cbo.Items.AddRange(new object[] { "0 = 明るさ", "1 = コントラスト", "2 = 彩度", "3 = 色相", "4 = 色温度", "5 = 色フィルタ (1赤/2緑/3青)", "6 = 拡大中 (Zキー、1.0=通常)", "7 = 縮小中 (Vキー、1.0=通常)" });
            int cur = (int)Math.Round(Convert.ToSingle(prop.GetValue(obj)));
            cbo.SelectedIndex = cur < 0 || cur > 7 ? 0 : cur;
            cbo.SelectedIndexChanged += (s, e) => { if (_building) return; prop.SetValue(obj, (float)cbo.SelectedIndex); _dirty = true; };
            _detail.AddItem(Kit.Field(label, cbo));
            return;
        }
        // 数値
        var nud = new NumericUpDown
        {
            Font = UiTheme.Base, Width = 150, DecimalPlaces = decimals,
            Increment = decimals > 0 ? (decimal)Math.Pow(10, -decimals) : 1m, Minimum = -100000m, Maximum = 100000m,
            Value = Math.Clamp((decimal)Convert.ToSingle(prop.GetValue(obj)), -100000m, 100000m),
        };
        nud.ValueChanged += (s, e) =>
        {
            if (_building) return;
            if (prop.PropertyType == typeof(int)) prop.SetValue(obj, (int)nud.Value);
            else prop.SetValue(obj, (float)nud.Value);
            _dirty = true;
        };
        _detail.AddItem(Kit.Field(label, nud));
    }

    // ── コモンイベントの詳細 ──
    private void ShowCommonDetail(CommonEventDef ce)
    {
        _building = true;
        _emptyDetail.Visible = false;
        _detail.Visible = true;
        _detail.SuspendLayout();
        _detail.ClearItems();

        var head = new Panel { Height = 40, Margin = new Padding(0, 0, 0, 4) };
        var title = new Label { Dock = DockStyle.Fill, Font = new Font("Meiryo UI", 12f, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft, Text = "🔔 " + (ce.name.Length > 0 ? ce.name : ce.id), AutoEllipsis = true };
        var acts = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, WrapContents = false, Padding = new Padding(0, 4, 0, 0) };
        acts.Controls.Add(Kit.Btn("⧉ 複製", () => BtnDuplicate_Click(null, EventArgs.Empty)));
        acts.Controls.Add(Kit.Btn("🗑 削除", DeleteCurrent, danger: true));
        head.Controls.Add(title);
        head.Controls.Add(acts);
        _detail.AddItem(head);

        _detail.AddItem(Kit.Hint("複数のトリガーから、共通で呼び出せる一連のアクションです。トリガー編集で「CallCommonEvent」を選び、このIDを指定すると呼べます。"));
        _detail.AddItem(Kit.Heading("🏷 基本"));
        _detail.AddItem(new Label { Text = $"ID: {ce.id}", AutoSize = true, Font = UiTheme.Bold });
        _detail.AddItem(new Label { Text = $"名前: {ce.name}", AutoSize = true });
        _detail.AddItem(Kit.Heading($"▶ 実行する内容（{ce.actions.Count} 件）"));
        if (ce.actions.Count == 0) _detail.AddItem(Kit.Hint("まだアクションがありません。「✎ 内容を編集」で追加してください。"));
        else
        {
            int i = 1;
            foreach (var a in ce.actions)
                _detail.AddItem(new Label { Text = $"{i++}.  {a.action}" + (string.IsNullOrEmpty(a.param1) ? "" : $"  （{a.param1}）"), AutoSize = true, Font = UiTheme.Base, Margin = new Padding(0, 2, 0, 2) });
        }
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 10, 0, 0) };
        row.Controls.Add(Kit.Btn("✎ 内容を編集", EditSelectedCommonEvent, primary: true));
        _detail.AddItem(row);

        _detail.ResumeLayout(true);
        _detail.ScrollToTop();
        _building = false;
    }

    // ── 削除（敵・ギミック・アイテム・コモンイベント共通）──
    private void DeleteCurrent()
    {
        if (_curCe != null) { DeleteSelectedCommonEvent(); return; }
        if (_curDgv == null || _curRow == null) return;
        string nm = RowText(_curRow, "name"), id = RowText(_curRow, "id");
        if (MessageBox.Show($"「{(nm.Length > 0 ? nm : id)}」（ID {id}）を削除しますか？\n\nこれを置いているステージでは、見えなくなります。", "確認", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        var dgv = _curDgv; var row = _curRow;
        int idx = row.Index;
        _enemyParams.Remove(row); _gimmickParams.Remove(row); _itemParams.Remove(row);
        dgv.Rows.Remove(row);
        _dirty = true;
        _curRow = null; _curDgv = null;
        RebuildCards();
        // 近くの物を選び直す。
        if (dgv.Rows.Count > 0)
        {
            var next = dgv.Rows[Math.Min(idx, dgv.Rows.Count - 1)];
            SelectRow(dgv, next);
        }
        else ShowDetailEmpty();
    }
}
