namespace Lab_Editor;

// ───────────────────────────────────────────────
//  BackgroundSettingsForm - ステージの背景レイヤーを設定する画面
//
//  「背景レイヤー」は、遠景・中景・近景のように、カメラが動いたとき違う速さで流れる画像の層。
//  以前は drawOrder / scrollRate / offsetX… という英語の列が並んだ表だったが、
//  今回は次の作りに改めた。
//    左   : レイヤーの一覧（手前のものが上。絵のサムネイルと「速さ・ループ」を1行で見せる）
//    右上 : 全レイヤーを重ねて自動で流す、大きなパララックスプレビュー（つまみを動かすとその場で変わる）
//    右下 : 選んだレイヤーの設定（画像・単色・動き・位置）を、日本語の項目名のフォームで編集
//  呼び出し側との約束（コンストラクタの引数と ResultLayers）は以前のまま。
// ───────────────────────────────────────────────
public class BackgroundSettingsForm : EditorFormBase
{
    // 「保存して閉じる」が押された時点でのレイヤー一覧。呼び出し元は OK で返ってきた後にこれを読み、ステージへ反映する。
    public List<BackgroundLayer> ResultLayers { get; private set; } = new();

    private readonly string _projectRoot;
    private readonly string _imgDir;
    // 編集中のレイヤー（渡された一覧の複製。キャンセルしても元は汚れない）
    private readonly List<BackgroundLayer> _layers = new();
    private BackgroundLayer? _cur;
    // 一覧の行 ↔ レイヤー（参照の同一性で対応づける）
    private readonly Dictionary<string, BackgroundLayer> _byKey = new();
    private readonly Dictionary<BackgroundLayer, string> _keyOf = new(ReferenceEqualityComparer.Instance);
    private int _keySeq;

    private readonly CardListView _list = new() { Dock = DockStyle.Fill, LayoutMode = CardLayout.Rows };
    private readonly Panel _preview = new() { Dock = DockStyle.Top, Height = 180, BackColor = Color.FromArgb(140, 190, 230) };
    private readonly Panel _detailHost = new() { Dock = DockStyle.Fill };
    private readonly FieldStack _detail = new() { Dock = DockStyle.Fill };
    private readonly Label _empty = new()
    {
        Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = UiTheme.TextMuted, BackColor = UiTheme.CardBack,
        Text = "左の一覧からレイヤーを選ぶか、\r\n「＋ 背景を追加」で新しいレイヤーを作ってください。",
    };

    // 詳細フォームの部品
    private readonly ImageViewport _imgView = new() { Height = 120, Placeholder = "画像は未設定（単色だけのレイヤー）" };
    private readonly Label _lblFile = new() { AutoSize = true, ForeColor = UiTheme.TextMuted, Font = UiTheme.Small };
    private readonly ColorField _color = new();
    private readonly SliderField _rate = new(0, 2, 0.05, 2, "0 ＝ 動かない（遠景）", "1 ＝ 手前と同じ速さ");
    private readonly CheckBox _loop = new() { Text = "横にくり返し並べる（ループ）", AutoSize = true, Font = UiTheme.Base };
    private readonly NumericUpDown _order = Kit.Num(-99, 99, 0, 1, 90);
    private readonly NumericUpDown _offX = Kit.Num(-9999, 9999, 0, 1, 100);
    private readonly NumericUpDown _offY = Kit.Num(-9999, 9999, 0, 1, 100);
    private bool _sync;

    // 一覧のサムネイルと、プレビュー用に読み込んだ画像の記憶
    private readonly Dictionary<string, Image?> _imgCache = new();
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 30 };
    private float _simCameraX;

    public BackgroundSettingsForm(string projectRoot, List<BackgroundLayer> layers)
        : base("背景レイヤー設定", "遠景・中景・近景を重ねて、奥行きのある背景を作ります。右上のプレビューで見え方を確かめながら調整できます。",
               new Size(1180, 720), new Size(960, 600))
    {
        _projectRoot = projectRoot;
        _imgDir = Path.Combine(projectRoot, "img");
        foreach (var l in layers)
            _layers.Add(new BackgroundLayer
            {
                drawOrder = l.drawOrder, sprite = l.sprite, color = l.color, scrollRate = l.scrollRate,
                loop = l.loop, offsetX = l.offsetX, offsetY = l.offsetY,
            });

        BuildUi();
        RebuildList();
        if (_layers.Count > 0) Select(_layers.OrderByDescending(l => l.drawOrder).First());
        else ShowDetail(null);

        _timer.Tick += (s, e) => { _simCameraX += 1.5f; _preview.Invalidate(); };
        _timer.Start();
    }

    // ── 画面の組み立て ───────────────────────────
    private void BuildUi()
    {
        // 左: 一覧
        var left = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.PanelBack };
        var leftHead = new Label
        {
            Dock = DockStyle.Top, Height = 30, Text = "  レイヤー（上ほど手前）", Font = UiTheme.Bold,
            TextAlign = ContentAlignment.MiddleLeft, ForeColor = UiTheme.TextMuted,
        };
        left.Controls.Add(_list);
        left.Controls.Add(leftHead);
        _list.ItemClicked += (s, item) => { if (_byKey.TryGetValue(item.Key, out var l)) { _cur = l; ShowDetail(l); } };
        _list.EmptyText = "レイヤーがありません";

        // 右: プレビュー(上) + 設定(下)
        var right = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.CardBack };
        var previewCap = new Label
        {
            Dock = DockStyle.Top, Height = 26, Text = "  🎬 プレビュー（自動で右へ進みます。奥のレイヤーほどゆっくり流れます）", Font = UiTheme.Small,
            TextAlign = ContentAlignment.MiddleLeft, BackColor = UiTheme.PanelBack, ForeColor = UiTheme.TextMuted,
        };
        _preview.Paint += Preview_Paint;
        _detailHost.Controls.Add(_detail);
        _detailHost.Controls.Add(_empty);
        // Dock の決まり：Fill を先に、あとから足したものが外側（上）。見出しをプレビューの上に置く。
        right.Controls.Add(_detailHost);
        right.Controls.Add(_preview);
        right.Controls.Add(previewCap);

        var split = new SplitContainer { Dock = DockStyle.Fill, SplitterWidth = 6, FixedPanel = FixedPanel.Panel1, BackColor = UiTheme.CardBorder };
        split.Panel1.Controls.Add(left);
        split.Panel2.Controls.Add(right);
        Body.Controls.Add(split);
        SplitWhenReady(split, 260, 520, 320);

        BuildDetailForm();

        // 下の操作ボタン
        LeftBar.Controls.Add(Kit.Btn("＋ 背景を追加", AddLayer));
        LeftBar.Controls.Add(Kit.Btn("⧉ 複製", Duplicate));
        LeftBar.Controls.Add(Kit.Btn("▲ 手前へ", () => MoveLayer(+1)));
        LeftBar.Controls.Add(Kit.Btn("▼ 奥へ", () => MoveLayer(-1)));
        LeftBar.Controls.Add(Kit.Btn("🗑 削除", Delete, danger: true));
    }

    private void BuildDetailForm()
    {
        _detail.AddItem(Kit.Heading("🖼 画像"));
        _detail.AddItem(_imgView);
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 6, 0, 0) };
        row.Controls.Add(Kit.Btn("📁 画像を選ぶ", PickImage, primary: true));
        row.Controls.Add(Kit.Btn("✕ 画像を外す", () => { if (_cur == null) return; _cur.sprite = ""; Changed(); LoadImage(); }));
        _detail.AddItem(row);
        _detail.AddItem(_lblFile);

        _detail.AddItem(Kit.Heading("🎨 単色（下地）"));
        _detail.AddItem(Kit.Field("色", _color, "画像がないレイヤーは、この色でいちめん塗られます。画像があるときは、画像の下に敷かれます。"));
        _color.ValueChanged += (s, e) => { if (_sync || _cur == null) return; _cur.color = _color.Value; Changed(); };

        _detail.AddItem(Kit.Heading("🏃 動き"));
        _detail.AddItem(Kit.Field("流れる速さ（カメラに対する割合）", _rate,
            "小さいほど遠くに見えます。空や山は 0.1〜0.3、手前の草むらは 0.8〜1.0 くらいが目安です。"));
        _rate.ValueChanged += (s, e) => { if (_sync || _cur == null) return; _cur.scrollRate = (float)_rate.Value; Changed(); };
        _detail.AddItem(_loop);
        _loop.CheckedChanged += (s, e) => { if (_sync || _cur == null) return; _cur.loop = _loop.Checked; Changed(); };
        _detail.AddItem(Kit.Hint("オフにすると、画像は1枚だけ流れていき、端まで行くと画面の外へ出ていきます。"));

        _detail.AddItem(Kit.Heading("📍 重なりと位置"));
        _detail.AddItem(Kit.Field("重ね順", _order, "数字が大きいほど手前に描かれます（一覧の上の方）。"));
        _order.ValueChanged += (s, e) => { if (_sync || _cur == null) return; _cur.drawOrder = (int)_order.Value; Changed(rebuildList: true); };
        var offs = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        offs.Controls.Add(Kit.Field("ずらし 横 (px)", _offX, null, 130));
        offs.Controls.Add(new Panel { Width = 16, Height = 1 });
        offs.Controls.Add(Kit.Field("ずらし 縦 (px)", _offY, null, 130));
        _detail.AddItem(offs);
        _detail.AddItem(Kit.Hint("画像の初期位置を動かします。プラスで右・下へずれます。"));
        _offX.ValueChanged += (s, e) => { if (_sync || _cur == null) return; _cur.offsetX = (float)_offX.Value; Changed(); };
        _offY.ValueChanged += (s, e) => { if (_sync || _cur == null) return; _cur.offsetY = (float)_offY.Value; Changed(); };
    }

    // ── 一覧 ────────────────────────────────────
    private string KeyFor(BackgroundLayer l)
    {
        if (!_keyOf.TryGetValue(l, out var k)) { k = "bg" + _keySeq++; _keyOf[l] = k; _byKey[k] = l; }
        return k;
    }

    // 画像のフルパス（相対パス→絶対パス）
    private string Full(string rel) => Path.Combine(_projectRoot, rel.Replace('/', '\\'));

    private Image? GetImage(string rel)
    {
        if (string.IsNullOrEmpty(rel)) return null;
        if (_imgCache.TryGetValue(rel, out var img)) return img;
        img = Kit.LoadBitmap(Full(rel));
        _imgCache[rel] = img;
        return img;
    }

    private void RebuildList()
    {
        var items = new List<CardItem>();
        int n = _layers.Count;
        foreach (var l in _layers.OrderByDescending(x => x.drawOrder))
        {
            var col = Kit.ParseHex(l.color);
            string name = string.IsNullOrEmpty(l.sprite) ? (col != null ? $"単色 {l.color}" : "（空のレイヤー）") : Path.GetFileName(l.sprite);
            items.Add(new CardItem
            {
                Key = KeyFor(l), Title = name,
                Subtitle = $"重ね順 {l.drawOrder} ・ 速さ ×{l.scrollRate:0.##} ・ {(l.loop ? "ループ" : "1枚")}",
                Accent = UiTheme.KindGimmick, Icon = Kit.Thumb(GetImage(l.sprite), 40, col),
                Tip = string.IsNullOrEmpty(l.sprite) ? name : l.sprite,
            });
        }
        _list.SetItems(items);
        if (_cur != null) _list.SetSelectedKey(KeyFor(_cur), scrollIntoView: false);
        _ = n;
    }

    private void Select(BackgroundLayer l)
    {
        _cur = l;
        _list.SetSelectedKey(KeyFor(l));
        ShowDetail(l);
    }

    // 選んだレイヤーの内容をフォームへ映す（映している最中は変更イベントを無視する）。
    private void ShowDetail(BackgroundLayer? l)
    {
        _empty.Visible = l == null;
        _detail.Visible = l != null;
        if (l == null) return;
        _sync = true;
        _color.Value = l.color ?? "";
        _rate.Value = l.scrollRate;
        _loop.Checked = l.loop;
        Kit.SetNum(_order, l.drawOrder);
        Kit.SetNum(_offX, (decimal)l.offsetX);
        Kit.SetNum(_offY, (decimal)l.offsetY);
        _sync = false;
        LoadImage();
        _detail.ScrollToTop();
    }

    private void LoadImage()
    {
        if (_cur == null) return;
        _imgView.Image = GetImage(_cur.sprite);
        _lblFile.Text = string.IsNullOrEmpty(_cur.sprite) ? "" : $"ファイル: {_cur.sprite}" + (_imgView.Image == null ? "  ⚠ 見つかりません" : $"  （{_imgView.Image.Width}×{_imgView.Image.Height}px）");
        _preview.Invalidate();
    }

    // 設定が変わったとき。一覧の1行（名前・補足）とプレビューを更新する。
    private void Changed(bool rebuildList = true)
    {
        MarkDirty();
        if (rebuildList) RebuildList();
        _preview.Invalidate();
    }

    // ── 操作 ────────────────────────────────────
    private void AddLayer()
    {
        // 新しいレイヤーは、いちばん手前（最大の重ね順+1）へ置く。
        var l = new BackgroundLayer { drawOrder = _layers.Count == 0 ? 0 : _layers.Max(x => x.drawOrder) + 1 };
        _layers.Add(l);
        MarkDirty();
        RebuildList();
        Select(l);
    }

    private void Duplicate()
    {
        if (_cur == null) return;
        var c = new BackgroundLayer
        {
            drawOrder = _layers.Max(x => x.drawOrder) + 1, sprite = _cur.sprite, color = _cur.color, scrollRate = _cur.scrollRate,
            loop = _cur.loop, offsetX = _cur.offsetX, offsetY = _cur.offsetY,
        };
        _layers.Add(c);
        MarkDirty();
        RebuildList();
        Select(c);
    }

    private void Delete()
    {
        if (_cur == null) return;
        if (MessageBox.Show("このレイヤーを削除しますか？", "確認", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        var ordered = _layers.OrderByDescending(x => x.drawOrder).ToList();
        int i = ordered.IndexOf(_cur);
        _layers.Remove(_cur);
        _cur = null;
        MarkDirty();
        RebuildList();
        var next = _layers.OrderByDescending(x => x.drawOrder).Skip(Math.Max(0, i - 1)).FirstOrDefault();
        if (next != null) Select(next); else ShowDetail(null);
    }

    // 重ね順を、隣のレイヤーと入れ替える。dir=+1で手前へ、-1で奥へ。
    private void MoveLayer(int dir)
    {
        if (_cur == null) return;
        var ordered = _layers.OrderBy(x => x.drawOrder).ToList();
        int i = ordered.IndexOf(_cur);
        int j = i + dir;
        if (j < 0 || j >= ordered.Count) return;
        var other = ordered[j];
        // 同じ重ね順どうしのときは、入れ替えても変わらないので、1つずらして順序をはっきりさせる。
        if (other.drawOrder == _cur.drawOrder) _cur.drawOrder += dir;
        else (other.drawOrder, _cur.drawOrder) = (_cur.drawOrder, other.drawOrder);
        MarkDirty();
        RebuildList();
        ShowDetail(_cur);
    }

    // 画像ファイルを選び、プロジェクトの img フォルダへコピーして、そのレイヤーに設定する。
    private void PickImage()
    {
        if (_cur == null) return;
        using var dlg = new OpenFileDialog { Title = "背景の画像を選ぶ", Filter = "画像ファイル|*.png;*.jpg;*.bmp;*.gif|すべて|*.*" };
        if (dlg.ShowDialog() != DialogResult.OK) return;
        try
        {
            Directory.CreateDirectory(_imgDir);
            string dest = Path.Combine(_imgDir, Path.GetFileName(dlg.FileName));
            if (!string.Equals(dlg.FileName, dest, StringComparison.OrdinalIgnoreCase)) File.Copy(dlg.FileName, dest, overwrite: true);
            string rel = "img/" + Path.GetFileName(dlg.FileName);
            _imgCache.Remove(rel);          // 同じ名前で絵を差し替えたときのため、記憶を捨てて読み直す
            _cur.sprite = rel;
            Changed();
            LoadImage();
        }
        catch (Exception ex)
        {
            MessageBox.Show("ファイルのコピーに失敗しました:\n" + ex.Message, "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // ── パララックスプレビュー ──────────────────
    // 全レイヤーを、重ね順の小さい（奥の）ものから順に描く。ゲームと同じく、色が下地・画像がその上。
    private void Preview_Paint(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        int pw = _preview.Width, ph = _preview.Height;
        foreach (var l in _layers.OrderBy(x => x.drawOrder))
        {
            if (Kit.ParseHex(l.color) is Color c)
            {
                using var fill = new SolidBrush(c);
                g.FillRectangle(fill, 0, 0, pw, ph);
            }
            var img = GetImage(l.sprite);
            if (img == null) continue;

            float scale = (float)ph / img.Height;
            int drawW = Math.Max(1, (int)(img.Width * scale));
            // 0.3 は、プレビュー内で動きが分かりやすいよう調整した表示用の速度係数（実機そのものの速さではない）。
            float scrollPx = _simCameraX * l.scrollRate * scale * 0.3f;
            int offX = (int)(l.offsetX * scale);
            int offY = (int)(l.offsetY * scale);
            if (l.loop)
            {
                int baseX = -(int)(scrollPx % drawW) + offX;
                while (baseX > 0) baseX -= drawW;
                for (int x = baseX; x < pw; x += drawW) g.DrawImage(img, x, offY, drawW, ph);
            }
            else g.DrawImage(img, -(int)scrollPx + offX, offY, drawW, ph);
        }
        if (_layers.Count == 0)
            TextRenderer.DrawText(g, "レイヤーを追加すると、ここに背景が出ます", UiTheme.Base, new Rectangle(0, 0, pw, ph), Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    // ── 保存 ────────────────────────────────────
    protected override void OnSave()
    {
        ResultLayers = _layers.OrderBy(l => l.drawOrder).Select(l => new BackgroundLayer
        {
            drawOrder = l.drawOrder, sprite = l.sprite ?? "", color = l.color ?? "", scrollRate = l.scrollRate,
            loop = l.loop, offsetX = l.offsetX, offsetY = l.offsetY,
        }).ToList();
        DialogResult = DialogResult.OK;
        Close();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _timer.Stop();
        _timer.Dispose();
        foreach (var i in _imgCache.Values) i?.Dispose();
        base.OnFormClosed(e);
    }
}
