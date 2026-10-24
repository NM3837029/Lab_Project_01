using Newtonsoft.Json;

namespace Lab_Editor;

// ───────────────────────────────────────────────
//  TileEditorForm - マップに敷き詰める地形タイルを登録・編集する画面
//
//  以前は ID / 名前 / 色 / 当たり判定 / 即死 / 画像パス / 破壊条件 … が20列近く横に並んだ表で、
//  画面が狭いと全部が潰れて読めなかった。今回は次の作りに改めた。
//    左 : タイルの絵が並んだ一覧（右パネルの「タイル」と同じ見た目。名前・IDで探せる）
//    右 : 選んだタイルの大きなプレビューと、日本語の項目名の設定
//         （画像と表示範囲・名前・色・当たり判定・即死・壊し方）
//  元に戻す／やり直し、ID 重複・名前未入力の警告、ID=0 を消せない決まりなど、以前の機能は引き継ぐ。
//  保存先は assets/tiles.json。呼び出し側との約束（コンストラクタの引数と ResultTiles）も以前のまま。
// ───────────────────────────────────────────────
public class TileEditorForm : EditorFormBase
{
    private readonly string assetsPath;
    private List<TileDef> tiles;
    private readonly AssetDefinitions? assets;
    private TileDef? _cur;
    private bool _sync;

    public List<TileDef> ResultTiles => tiles;

    private readonly HistoryManager<List<TileDef>> _history = new();
    private readonly System.Windows.Forms.Timer _snapTimer = new() { Interval = 500 };
    // 画像ファイルの記憶（フルパス → 絵）。タイルセットは1枚を複数のタイルが使うので、読み込みを使い回す。
    private readonly Dictionary<string, Bitmap?> _sheets = new();

    private readonly TextBox _txtSearch = new() { Dock = DockStyle.Fill, PlaceholderText = "🔍 名前・IDで探す", BorderStyle = BorderStyle.FixedSingle };
    private readonly CardListView _list = new() { Dock = DockStyle.Fill };
    private readonly FieldStack _form = new() { Dock = DockStyle.Fill };
    private readonly Label _empty = new()
    {
        Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = UiTheme.TextMuted, BackColor = UiTheme.CardBack,
        Text = "左の一覧からタイルを選ぶか、\r\n「＋ タイルを追加」で作ってください。",
    };
    private readonly ImageViewport _preview = new() { Height = 170, Placeholder = "画像がないので、色で表示されます" };
    private readonly Label _lblSprite = new() { AutoSize = true, Font = UiTheme.Base };
    private readonly Label _lblRange = new() { AutoSize = true, Font = UiTheme.Small, ForeColor = UiTheme.TextMuted };
    private readonly Label _lblId = new() { AutoSize = true, Font = UiTheme.Bold };
    private readonly TextBox _txtName = new() { Font = UiTheme.Base };
    private readonly ColorField _color = new();
    private readonly CheckBox _chkCollide = new() { Text = "当たり判定あり（乗れる・ぶつかる）", AutoSize = true, Font = UiTheme.Base };
    private readonly CheckBox _chkDeadly = new() { Text = "即死（触れるとミスになる）", AutoSize = true, Font = UiTheme.Base };
    private readonly CheckBox _chkSlam = new() { Text = "ドッスンの落下の衝撃で壊れる", AutoSize = true, Font = UiTheme.Base };
    private readonly CheckBox _chkRam = new() { Text = "敵の体当たり・突進で壊れる", AutoSize = true, Font = UiTheme.Base };
    private readonly CheckBox _chkBullet = new() { Text = "弾で壊れる", AutoSize = true, Font = UiTheme.Base };
    private readonly CheckBox _chkPlayer = new() { Text = "編集中にクリックすると壊れる", AutoSize = true, Font = UiTheme.Base };
    private readonly NumericUpDown _minScale = Kit.Num(0, 99, 2, 0.1m, 100);
    private readonly ComboBox _cmbBreakSe = new() { DropDownStyle = ComboBoxStyle.DropDown, Font = UiTheme.Base };
    private readonly Label _warn = new() { AutoSize = false, Height = 0, ForeColor = Color.Firebrick, Font = UiTheme.Small, Tag = "hint" };

    public TileEditorForm(string assetsPath, List<TileDef> currentTiles, AssetDefinitions? assets = null)
        : base("タイルエディタ", "マップに敷き詰める地形タイルの、見た目・性質・壊し方を設定します。", new Size(1160, 740), new Size(940, 600))
    {
        this.assetsPath = assetsPath;
        this.assets = assets;
        // 呼び出し元のリストを直接書き換えないよう、各要素を複製して持つ。
        tiles = currentTiles.Select(Clone).ToList();

        BuildUi();
        _snapTimer.Tick += (s, e) => { _snapTimer.Stop(); CommitNow(); };
        _history.Push(Snapshot());
        InitUndo(Undo, Redo);
        UpdateUndoState(_history.CanUndo, _history.CanRedo);
        RebuildList();
        if (tiles.Count > 0) Select(tiles.FirstOrDefault(t => t.id == 1) ?? tiles[0]); else ShowForm(null);
    }

    private static TileDef Clone(TileDef t) => new()
    {
        id = t.id, name = t.name, color = t.color, collidable = t.collidable, deadly = t.deadly, sprite = t.sprite,
        srcX = t.srcX, srcY = t.srcY, srcW = t.srcW, srcH = t.srcH,
        breakBySlam = t.breakBySlam, breakByRam = t.breakByRam, breakByBullet = t.breakByBullet, breakByPlayer = t.breakByPlayer,
        breakMinScale = t.breakMinScale, breakSe = t.breakSe,
    };

    // ── 画面の組み立て ───────────────────────────
    private void BuildUi()
    {
        var left = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.PanelBack };
        var searchHost = new Panel { Dock = DockStyle.Top, Height = UiTheme.S(this, 36), Padding = new Padding(8, 8, 8, 4) };
        searchHost.Controls.Add(_txtSearch);
        _txtSearch.TextChanged += (s, e) => RebuildList();
        left.Controls.Add(_list);
        left.Controls.Add(searchHost);
        _list.ItemClicked += (s, item) => { var t = tiles.FirstOrDefault(x => KeyOf(x) == item.Key); if (t != null) { _cur = t; ShowForm(t); } };
        _list.DeletePressed += (s, item) => Delete();

        var right = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.CardBack };
        right.Controls.Add(_form);
        right.Controls.Add(_empty);

        var split = new SplitContainer { Dock = DockStyle.Fill, SplitterWidth = 6, FixedPanel = FixedPanel.Panel1, BackColor = UiTheme.CardBorder };
        split.Panel1.Controls.Add(left);
        split.Panel2.Controls.Add(right);
        Body.Controls.Add(split);
        SplitWhenReady(split, 260, 480, 420);

        BuildForm();

        LeftBar.Controls.Add(Kit.Btn("＋ タイルを追加", Add));
        LeftBar.Controls.Add(Kit.Btn("⧉ 複製", Duplicate));
        LeftBar.Controls.Add(Kit.Btn("🗑 削除", Delete, danger: true));
        LeftBar.Controls.Add(Kit.Btn("🧱 壊せるブロック（ギミック）の設定…", EditBreakableGimmicks));
    }

    private void BuildForm()
    {
        // 破壊音の候補（効果音とUI音のID）。登録が無い／読み込めていないときは、自由入力だけになる。
        _cmbBreakSe.Items.Add("");
        if (assets != null) foreach (var d in assets.Se.Concat(assets.UiSe)) _cmbBreakSe.Items.Add(d.id);

        _form.AddItem(_warn);
        _form.AddItem(_preview);
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Margin = new Padding(0, 6, 0, 0) };
        row.Controls.Add(Kit.Btn("📁 画像を選ぶ", PickSprite, primary: true));
        row.Controls.Add(Kit.Btn("🖼 表示範囲を選ぶ", PickRegion));
        row.Controls.Add(Kit.Btn("✕ 画像を外す", () => { if (_cur == null) return; _cur.sprite = ""; _cur.srcX = _cur.srcY = _cur.srcW = _cur.srcH = 0; Changed(immediate: true); ShowForm(_cur); }));
        _form.AddItem(row);
        _form.AddItem(_lblSprite);
        _form.AddItem(_lblRange);

        _form.AddItem(Kit.Heading("🏷 基本"));
        _form.AddItem(_lblId);
        _form.AddItem(Kit.Hint("マップのデータには、この番号が入ります。番号は自動で決まり、あとから変えられません。"));
        _form.AddItem(Kit.Field("名前", _txtName));
        _form.AddItem(Kit.Field("色", _color, "画像がないタイルは、この色で表示されます。画像があるときは、画像のない部分の下地になります。"));

        _form.AddItem(Kit.Heading("⚔ 性質"));
        _form.AddItem(_chkCollide);
        _form.AddItem(_chkDeadly);

        _form.AddItem(Kit.Heading("💥 壊し方"));
        _form.AddItem(Kit.Hint("チェックしないと、このタイルは絶対に壊れない地形になります。"));
        _form.AddItem(_chkSlam);
        _form.AddItem(_chkRam);
        _form.AddItem(_chkBullet);
        _form.AddItem(_chkPlayer);
        _form.AddItem(Kit.Field("壊すのに必要な相手の大きさ", _minScale, "0 なら大きさを問いません。2 なら、2倍以上に大きくした相手でないと壊せません。"));
        _form.AddItem(Kit.Field("壊れたときに鳴らす音（ID）", _cmbBreakSe, "空欄なら無音です。音は「サウンド管理」で登録します。"));

        _txtName.TextChanged += (s, e) => Edit(t => t.name = _txtName.Text);
        _color.ValueChanged += (s, e) => Edit(t => t.color = _color.Value.Length == 7 ? _color.Value : "#CCCCCC");
        _chkCollide.CheckedChanged += (s, e) => Edit(t => t.collidable = _chkCollide.Checked, immediate: true);
        _chkDeadly.CheckedChanged += (s, e) => Edit(t => t.deadly = _chkDeadly.Checked, immediate: true);
        _chkSlam.CheckedChanged += (s, e) => Edit(t => t.breakBySlam = _chkSlam.Checked, immediate: true);
        _chkRam.CheckedChanged += (s, e) => Edit(t => t.breakByRam = _chkRam.Checked, immediate: true);
        _chkBullet.CheckedChanged += (s, e) => Edit(t => t.breakByBullet = _chkBullet.Checked, immediate: true);
        _chkPlayer.CheckedChanged += (s, e) => Edit(t => t.breakByPlayer = _chkPlayer.Checked, immediate: true);
        _minScale.ValueChanged += (s, e) => Edit(t => t.breakMinScale = (float)_minScale.Value);
        _cmbBreakSe.TextChanged += (s, e) => Edit(t => t.breakSe = _cmbBreakSe.Text.Trim());
    }

    private void Edit(Action<TileDef> apply, bool immediate = false)
    {
        if (_sync || _cur == null) return;
        apply(_cur);
        Changed(immediate);
    }

    private void Changed(bool immediate = false)
    {
        MarkDirty();
        RebuildList();
        RefreshPreview();
        if (immediate) CommitNow(); else { _snapTimer.Stop(); _snapTimer.Start(); }
    }

    // ── 一覧 ────────────────────────────────────
    private static string KeyOf(TileDef t) => "tile:" + t.id;

    private Bitmap? Sheet(string spritePath)
    {
        if (string.IsNullOrEmpty(spritePath)) return null;
        string full = Path.Combine(Path.GetDirectoryName(assetsPath)!, spritePath.Replace('/', '\\'));
        if (_sheets.TryGetValue(full, out var b)) return b;
        b = Kit.LoadBitmap(full);
        _sheets[full] = b;
        return b;
    }

    // タイルの絵（表示範囲で切り出したもの）。画像が無ければ null。
    private Bitmap? CropOf(TileDef t)
    {
        var sheet = Sheet(t.sprite);
        if (sheet == null) return null;
        var src = (t.srcW > 0 && t.srcH > 0) ? new Rectangle(t.srcX, t.srcY, t.srcW, t.srcH) : new Rectangle(0, 0, sheet.Width, sheet.Height);
        src.Intersect(new Rectangle(0, 0, sheet.Width, sheet.Height));
        if (src.Width <= 0 || src.Height <= 0) src = new Rectangle(0, 0, sheet.Width, sheet.Height);
        var bmp = new Bitmap(src.Width, src.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.DrawImage(sheet, new Rectangle(0, 0, src.Width, src.Height), src, GraphicsUnit.Pixel);
        return bmp;
    }

    private static Color ColorOf(TileDef t) => Kit.ParseHex(t.color) ?? Color.Gray;

    private void RebuildList()
    {
        string q = _txtSearch.Text.Trim();
        var items = new List<CardItem>();
        foreach (var t in tiles)
        {
            if (q.Length > 0 && !t.id.ToString().Contains(q) && !t.name.Contains(q, StringComparison.OrdinalIgnoreCase)) continue;
            using var crop = CropOf(t);
            var traits = new List<string>();
            if (t.collidable) traits.Add("当たり判定あり");
            if (t.deadly) traits.Add("即死");
            if (t.breakBySlam || t.breakByRam || t.breakByBullet || t.breakByPlayer) traits.Add("壊せる");
            items.Add(new CardItem
            {
                Key = KeyOf(t), Title = t.name.Length > 0 ? t.name : $"タイル{t.id}",
                Accent = t.deadly ? UiTheme.KindEnemy : t.collidable ? UiTheme.KindTile : Color.FromArgb(190, 196, 204),
                Icon = Kit.Thumb(crop, 48, ColorOf(t)),
                Tip = $"ID {t.id}\n" + (traits.Count > 0 ? string.Join(" ・ ", traits) : "すり抜けられる"),
            });
        }
        _list.EmptyText = q.Length > 0 ? $"「{q}」に合うタイルがありません" : "タイルがありません";
        _list.SetItems(items);
        if (_cur != null) _list.SetSelectedKey(KeyOf(_cur), scrollIntoView: false);
    }

    private void Select(TileDef t)
    {
        _cur = t;
        _list.SetSelectedKey(KeyOf(t));
        ShowForm(t);
    }

    private void ShowForm(TileDef? t)
    {
        _empty.Visible = t == null;
        _form.Visible = t != null;
        if (t == null) return;
        _sync = true;
        _lblId.Text = $"ID  {t.id}";
        _txtName.Text = t.name;
        _color.Value = t.color ?? "";
        _chkCollide.Checked = t.collidable; _chkDeadly.Checked = t.deadly;
        _chkSlam.Checked = t.breakBySlam; _chkRam.Checked = t.breakByRam; _chkBullet.Checked = t.breakByBullet; _chkPlayer.Checked = t.breakByPlayer;
        Kit.SetNum(_minScale, (decimal)t.breakMinScale);
        _cmbBreakSe.Text = t.breakSe ?? "";
        _sync = false;

        var sheet = Sheet(t.sprite);
        _lblSprite.Text = string.IsNullOrEmpty(t.sprite) ? "（画像は選ばれていません）" : t.sprite + (sheet == null ? "  ⚠ 見つかりません" : $"   {sheet.Width}×{sheet.Height}px");
        _lblSprite.ForeColor = sheet == null && !string.IsNullOrEmpty(t.sprite) ? Color.Firebrick : UiTheme.TextMain;
        _lblRange.Text = sheet == null ? "" : (t.srcW > 0 && t.srcH > 0 ? $"表示範囲: 左から{t.srcX}・上から{t.srcY}、{t.srcW}×{t.srcH}px を使います" : "表示範囲: 画像ぜんぶを使います（タイルセット画像なら「表示範囲を選ぶ」で1マスを切り出します）");
        RefreshPreview();
        UpdateWarning();
        _form.ScrollToTop();
    }

    private void RefreshPreview()
    {
        if (_cur == null) return;
        var old = _preview.Image;
        var crop = CropOf(_cur);
        if (crop == null)
        {
            // 画像がないタイルは、色で塗った四角を出す。
            var c = new Bitmap(32, 32);
            using (var g = Graphics.FromImage(c)) g.Clear(ColorOf(_cur));
            crop = c;
        }
        _preview.Image = crop;
        old?.Dispose();
    }

    // 保存しても問題ないかを、フォームの上に赤字で知らせる。
    private void UpdateWarning()
    {
        if (_cur == null) return;
        var msgs = new List<string>();
        if (tiles.Count(t => t.id == _cur.id) > 1) msgs.Add("⚠ 同じ ID のタイルが複数あります。後に定義した方だけが有効になります。");
        if (string.IsNullOrWhiteSpace(_cur.name)) msgs.Add("⚠ 名前が空です。");
        _warn.Text = string.Join("\r\n", msgs);
        _warn.Height = msgs.Count == 0 ? 0 : 20 * msgs.Count + 4;
        _warn.Visible = msgs.Count > 0;
    }

    // ── 操作 ────────────────────────────────────
    private void Add()
    {
        int id = tiles.Count > 0 ? tiles.Max(t => t.id) + 1 : 0;
        var t = new TileDef { id = id, name = $"新タイル{id}", color = "#888888" };
        tiles.Add(t);
        MarkDirty();
        RebuildList();
        Select(t);
        CommitNow();
        _txtName.Focus(); _txtName.SelectAll();
    }

    private void Duplicate()
    {
        if (_cur == null) return;
        var t = Clone(_cur);
        t.id = tiles.Max(x => x.id) + 1;
        t.name = _cur.name + "のコピー";
        tiles.Add(t);
        MarkDirty();
        RebuildList();
        Select(t);
        CommitNow();
    }

    private void Delete()
    {
        if (_cur == null) return;
        // ID=0 は、ゲーム側で「何も置いていない」を表す特別なタイルなので、消せない。
        if (_cur.id == 0) { MessageBox.Show("ID 0 のタイル（空気）は、「何も置いていない」を表す特別なタイルなので削除できません。", "削除できません", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        if (MessageBox.Show($"タイル「{_cur.name}」（ID {_cur.id}）を削除しますか？\n\nこのタイルを使っているステージのマスは、見た目が失われます。", "確認", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        int i = tiles.IndexOf(_cur);
        tiles.Remove(_cur);
        _cur = null;
        MarkDirty();
        RebuildList();
        if (tiles.Count > 0) Select(tiles[Math.Min(i, tiles.Count - 1)]); else ShowForm(null);
        CommitNow();
    }

    // 画像ファイルを選ぶ。プロジェクトルートからの相対パスで覚える（元の画像は動かさない）。
    private void PickSprite()
    {
        if (_cur == null) return;
        using var ofd = new OpenFileDialog { Filter = "画像ファイル|*.png;*.jpg;*.bmp|すべて|*.*", Title = "タイルの画像を選ぶ" };
        if (ofd.ShowDialog() != DialogResult.OK) return;
        string root = Path.GetDirectoryName(assetsPath)!;
        string rel = Path.GetRelativePath(root, ofd.FileName).Replace('\\', '/');
        string full = Path.Combine(root, rel.Replace('/', '\\'));
        if (_sheets.Remove(full, out var old)) old?.Dispose();   // 同じ名前で差し替えたときのため、読み直す
        _cur.sprite = rel;
        // 新しい画像を選んだら、表示範囲は画像ぜんぶに戻す。
        _cur.srcX = _cur.srcY = _cur.srcW = _cur.srcH = 0;
        Changed(immediate: true);
        ShowForm(_cur);
    }

    // 画像のうち、このタイルに使う部分をドラッグで選ぶ専用画面を開く。
    private void PickRegion()
    {
        if (_cur == null) return;
        if (string.IsNullOrEmpty(_cur.sprite)) { MessageBox.Show("先に「画像を選ぶ」で画像を指定してください。", "未選択", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        string full = Path.Combine(Path.GetDirectoryName(assetsPath)!, _cur.sprite.Replace('/', '\\'));
        if (!File.Exists(full)) { MessageBox.Show("画像ファイルが見つかりません:\n" + full, "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error); return; }
        using var form = new TileRegionEditorForm(full, _cur.srcX, _cur.srcY, _cur.srcW, _cur.srcH);
        if (form.ShowDialog(this) != DialogResult.OK) return;
        _cur.srcX = form.SrcX; _cur.srcY = form.SrcY; _cur.srcW = form.SrcWidth; _cur.srcH = form.SrcHeight;
        Changed(immediate: true);
        ShowForm(_cur);
    }

    // 「壊せるブロック」はタイルではなくギミックとして作られているので、タイルの画面から辿れるよう導線を置いてある。
    // gimmicks.json のうち壊せるブロック型（type_enum=3）だけを取り出し、タイルと同じ言葉で壊し方を決める。OKで gimmicks.json を書き戻す。
    private void EditBreakableGimmicks()
    {
        if (assets == null) { MessageBox.Show("アセット定義を読み込めていないため開けません。", "壊せるブロックの設定"); return; }
        var blocks = assets.Gimmicks.Where(g => g.type_enum == 3).ToList();
        if (blocks.Count == 0) { MessageBox.Show("壊せるブロック型のギミックが登録されていません。", "壊せるブロックの設定"); return; }

        using var dlg = new Form
        {
            Text = "壊せるブロック（ギミック）の設定", Size = new Size(620, 700), MinimumSize = new Size(520, 480),
            StartPosition = FormStartPosition.CenterParent, Font = UiTheme.Base, BackColor = UiTheme.PanelBack,
        };
        var stack = new FieldStack { Dock = DockStyle.Fill };
        var rows = new List<(GimmickDef g, CheckBox slam, CheckBox ram, CheckBox bullet, CheckBox player, CheckBox tip, NumericUpDown scale)>();
        stack.AddItem(Kit.Hint("壊せるブロック（ギミックの「破壊ブロック」型）ごとに、誰がどうすれば壊れるかを決めます。"));
        foreach (var g in blocks)
        {
            var slam = new CheckBox { Text = "ドッスンの落下の衝撃で壊れる", Checked = g.breakBySlam, AutoSize = true };
            var ram = new CheckBox { Text = "敵の体当たりで壊れる", Checked = g.breakByRam, AutoSize = true };
            var bullet = new CheckBox { Text = "弾で壊れる", Checked = g.breakByBullet, AutoSize = true };
            var player = new CheckBox { Text = "クリックで壊れる", Checked = g.breakByPlayer, AutoSize = true };
            // 傾けて自重で崩れるのはギミックだけの概念（タイルは傾かない）。
            var tip = new CheckBox { Text = "45度以上傾けると自重で崩れる", Checked = g.breakByTip, AutoSize = true };
            var scale = Kit.Num(0, 99, 2, 0.1m, 100); Kit.SetNum(scale, (decimal)g.breakMinScale);
            stack.AddItem(Kit.Heading($"🧱 {(g.name.Length > 0 ? g.name : g.id)}   〔{g.id}〕"));
            foreach (var c in new Control[] { slam, ram, bullet, player, tip }) stack.AddItem(c);
            stack.AddItem(Kit.Field("壊すのに必要な相手の大きさ（0＝問わない）", scale));
            rows.Add((g, slam, ram, bullet, player, tip, scale));
        }
        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 54, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(12, 10, 12, 8), BackColor = UiTheme.CardBack };
        var ok = Kit.Btn("💾 保存", () => { dlg.DialogResult = DialogResult.OK; }, primary: true);
        var cancel = Kit.Btn("キャンセル", () => { dlg.DialogResult = DialogResult.Cancel; });
        bar.Controls.Add(ok); bar.Controls.Add(cancel);
        dlg.Controls.Add(stack);
        dlg.Controls.Add(bar);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        foreach (var (g, slam, ram, bullet, player, tip, scale) in rows)
        {
            g.breakBySlam = slam.Checked; g.breakByRam = ram.Checked; g.breakByBullet = bullet.Checked;
            g.breakByPlayer = player.Checked; g.breakByTip = tip.Checked; g.breakMinScale = (float)scale.Value;
        }
        try
        {
            // タイルの保存とは独立して、ここで gimmicks.json だけを書き戻す（SaveToFolder は9本のJSONを全部書き直すので使わない）。
            File.WriteAllText(Path.Combine(assetsPath, "gimmicks.json"), JsonConvert.SerializeObject(assets.Gimmicks, Formatting.Indented));
            MessageBox.Show("壊せるブロックの設定を保存しました。", "壊せるブロックの設定");
        }
        catch (Exception ex) { MessageBox.Show("保存に失敗しました: " + ex.Message, "壊せるブロックの設定"); }
    }

    // ── 元に戻す／やり直し ───────────────────────
    private List<TileDef> Snapshot() => tiles.Select(Clone).ToList();

    private void CommitNow()
    {
        _snapTimer.Stop();
        _history.Push(Snapshot());
        UpdateUndoState(_history.CanUndo, _history.CanRedo);
        UpdateWarning();
    }

    private void Restore(List<TileDef>? list)
    {
        if (list == null) return;
        int curId = _cur?.id ?? 1;
        tiles = list;
        _cur = tiles.FirstOrDefault(t => t.id == curId) ?? tiles.FirstOrDefault();
        RebuildList();
        if (_cur != null) Select(_cur); else ShowForm(null);
        UpdateUndoState(_history.CanUndo, _history.CanRedo);
    }

    private void Undo() { if (_snapTimer.Enabled) CommitNow(); if (_history.CanUndo) Restore(_history.Undo()); }
    private void Redo() { if (_history.CanRedo) Restore(_history.Redo()); }

    // ── 保存 ────────────────────────────────────
    // 保存前に、ID の重複と名前の未入力を確認する（あとから配置したステージで、意図しないタイルになりやすいため）。
    private static List<string> Validate(List<TileDef> list)
    {
        var warnings = new List<string>();
        var dup = list.GroupBy(t => t.id).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (dup.Count > 0) warnings.Add($"ID が重複しています: {string.Join(", ", dup)}（後に定義した方だけが有効になります）。");
        var empty = list.Where(t => string.IsNullOrWhiteSpace(t.name)).Select(t => t.id).ToList();
        if (empty.Count > 0) warnings.Add($"名前が未入力のタイルがあります (ID: {string.Join(", ", empty)})。");
        return warnings;
    }

    protected override void OnSave()
    {
        var warnings = Validate(tiles);
        if (warnings.Count > 0 &&
            MessageBox.Show("保存前に確認してください:\n\n" + string.Join("\n", warnings) + "\n\nこのまま保存しますか？", "確認", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            return;
        File.WriteAllText(Path.Combine(assetsPath, "tiles.json"), JsonConvert.SerializeObject(tiles, Formatting.Indented));
        DialogResult = DialogResult.OK;
        Close();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _snapTimer.Dispose();
        _preview.Image?.Dispose();
        foreach (var b in _sheets.Values) b?.Dispose();
        base.OnFormClosed(e);
    }
}
