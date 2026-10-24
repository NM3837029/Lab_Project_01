namespace Lab_Editor;

// ───────────────────────────────────────────────
//  AnimationEditorForm - スプライトシート（コマが並んだ1枚絵）からアニメーションを作る画面
//
//  以前は name / sprite / FCX / FCY / start / end / fps / loop の表で、数字を打ち込んでからでないと
//  どんな動きになるのか分からなかった。今回は次の作りに改めた。
//    左   : クリップ（「歩く」「攻撃」など1つの動き）の一覧。先頭コマの絵が見える
//    中央 : 選んだクリップの動くプレビューと、スプライトシート全体（コマの格子と、使うコマの範囲を重ねて表示）
//           シートのコマをクリックすると開始コマ、右クリックすると終了コマになる
//    右   : 日本語の項目名の設定（名前・画像・コマの分け方・範囲・速さ・ループ）
//  呼び出し側との約束（コンストラクタの引数と ResultSet）は以前のまま。
// ───────────────────────────────────────────────
public class AnimationEditorForm : EditorFormBase
{
    public AnimationSet ResultSet { get; private set; } = null!;

    private readonly string _projectRoot;
    private readonly string _imgDir;
    private readonly List<AnimationClip> _clips = new();
    private AnimationClip? _cur;
    private readonly Dictionary<AnimationClip, string> _keyOf = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<string, AnimationClip> _byKey = new();
    private int _keySeq;
    private bool _sync;

    // 画像の記憶（パス → シート全体の絵）
    private readonly Dictionary<string, Bitmap?> _sheets = new();

    // 画面の部品
    private readonly TextBox _txtAssetId = new() { Font = UiTheme.Base };
    private readonly CardListView _list = new() { Dock = DockStyle.Fill, LayoutMode = CardLayout.Rows };
    private readonly ImageViewport _anim = new() { Dock = DockStyle.Fill, Placeholder = "クリップを選ぶと、ここで動きます" };
    private readonly ImageViewport _sheetView = new() { Dock = DockStyle.Fill, Placeholder = "スプライトシートが未設定です" };
    private readonly Label _lblFrame = new() { AutoSize = true, ForeColor = UiTheme.TextMuted, Font = UiTheme.Small };
    private Button _btnPlay = null!;
    private readonly FieldStack _form = new() { Dock = DockStyle.Fill };
    private readonly Label _empty = new()
    {
        Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = UiTheme.TextMuted, BackColor = UiTheme.CardBack,
        Text = "左の一覧からクリップを選ぶか、\r\n「＋ クリップを追加」で作ってください。",
    };

    private readonly TextBox _txtName = new() { Font = UiTheme.Base };
    private readonly Label _lblSprite = new() { AutoSize = true, Font = UiTheme.Base };
    private readonly NumericUpDown _fcx = Kit.Num(1, 64, 0, 1, 90);
    private readonly NumericUpDown _fcy = Kit.Num(1, 64, 0, 1, 90);
    private readonly NumericUpDown _start = Kit.Num(0, 4095, 0, 1, 90);
    private readonly NumericUpDown _end = Kit.Num(0, 4095, 0, 1, 90);
    private readonly SliderField _fps = new(1, 60, 1, 0, "ゆっくり", "はやい");
    private readonly CheckBox _loop = new() { Text = "最後のコマまで行ったら最初に戻る（ループ）", AutoSize = true, Font = UiTheme.Base };

    // 再生
    private readonly System.Windows.Forms.Timer _timer = new();
    private int _frame;
    private bool _playing;
    private bool _held;   // ループなしで最後のコマに着いて止まっている

    public AnimationEditorForm(string projectRoot, AnimationSet animSet)
        : base("アニメーションエディタ", "スプライトシートのコマを選んで、動きを作ります。シートのコマをクリックすると、使う範囲を決められます。",
               new Size(1280, 760), new Size(1040, 640))
    {
        _projectRoot = projectRoot;
        _imgDir = Path.Combine(projectRoot, "img");
        foreach (var c in animSet.clips ?? new List<AnimationClip>())
            _clips.Add(new AnimationClip
            {
                name = c.name, sprite = c.sprite, frameCountX = c.frameCountX, frameCountY = c.frameCountY,
                startFrame = c.startFrame, endFrame = c.endFrame, fps = c.fps, loop = c.loop,
            });
        _txtAssetId.Text = animSet.assetId ?? "";

        BuildUi();
        _timer.Tick += Tick;
        RebuildList();
        if (_clips.Count > 0) Select(_clips[0]); else ShowForm(null);
    }

    // ── 画面の組み立て ───────────────────────────
    private void BuildUi()
    {
        // 左: アセットID + クリップ一覧
        var left = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.PanelBack };
        var idHost = new Panel { Dock = DockStyle.Top, Height = 62, Padding = new Padding(10, 6, 10, 0) };
        idHost.Controls.Add(Kit.Field("対象のアセットID", _txtAssetId));
        _txtAssetId.TextChanged += (s, e) => MarkDirty();
        left.Controls.Add(_list);
        left.Controls.Add(idHost);
        _list.ItemClicked += (s, item) => { if (_byKey.TryGetValue(item.Key, out var c)) { _cur = c; ShowForm(c); } };

        // 中央: プレビュー(上) + シート(下)
        var center = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterWidth = 6, BackColor = UiTheme.CardBorder };
        var previewHost = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.CardBack };
        var pvBar = new Panel { Dock = DockStyle.Bottom, Height = 40, BackColor = UiTheme.CardBack };
        _btnPlay = Kit.Btn("⏸ 止める", TogglePlay);
        _btnPlay.Location = new Point(10, 6);
        _lblFrame.Location = new Point(110, 12);
        pvBar.Controls.Add(_btnPlay);
        pvBar.Controls.Add(_lblFrame);
        previewHost.Controls.Add(_anim);
        previewHost.Controls.Add(pvBar);
        center.Panel1.Controls.Add(previewHost);

        var sheetHost = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.CardBack };
        var shCap = new Label
        {
            Dock = DockStyle.Top, Height = 26, Text = "  スプライトシート（クリック＝開始コマ ／ 右クリック＝終了コマ。青い枠が使うコマ）",
            Font = UiTheme.Small, ForeColor = UiTheme.TextMuted, TextAlign = ContentAlignment.MiddleLeft, BackColor = UiTheme.PanelBack,
        };
        sheetHost.Controls.Add(_sheetView);
        sheetHost.Controls.Add(shCap);
        center.Panel2.Controls.Add(sheetHost);
        _sheetView.Overlay = DrawSheetOverlay;
        _sheetView.MouseDown += SheetMouseDown;

        // 右: フォーム
        var right = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.CardBack };
        right.Controls.Add(_form);
        right.Controls.Add(_empty);
        BuildForm();

        // 3分割（左｜中央｜右）。外側で左を、内側で右を分ける。
        var outer = new SplitContainer { Dock = DockStyle.Fill, SplitterWidth = 6, FixedPanel = FixedPanel.Panel1, BackColor = UiTheme.CardBorder };
        var inner = new SplitContainer { Dock = DockStyle.Fill, SplitterWidth = 6, FixedPanel = FixedPanel.Panel2, BackColor = UiTheme.CardBorder };
        outer.Panel1.Controls.Add(left);
        inner.Panel1.Controls.Add(center);
        inner.Panel2.Controls.Add(right);
        outer.Panel2.Controls.Add(inner);
        Body.Controls.Add(outer);
        SplitWhenReady(outer, 240, 600, 280);
        inner.HandleCreated += (s, e) => SplitLayout.Apply(inner, 360, 320, Math.Max(361, inner.Width - 360));
        inner.SizeChanged += (s, e) => { if (inner.Panel2.Width < 320 || inner.Panel2MinSize == 0) SplitLayout.Apply(inner, 360, 320, Math.Max(361, inner.Width - 360)); };
        center.HandleCreated += (s, e) => SplitLayout.Apply(center, 120, 120, Math.Max(121, (int)(center.Height * 0.52)));
        center.SizeChanged += (s, e) => { if (center.Panel1MinSize == 0) SplitLayout.Apply(center, 120, 120, Math.Max(121, (int)(center.Height * 0.52))); };

        LeftBar.Controls.Add(Kit.Btn("＋ クリップを追加", AddClip));
        LeftBar.Controls.Add(Kit.Btn("⧉ 複製", Duplicate));
        LeftBar.Controls.Add(Kit.Btn("🗑 削除", Delete, danger: true));
    }

    private void BuildForm()
    {
        _form.AddItem(Kit.Field("クリップの名前", _txtName, "ゲームのスクリプトが、この名前で動きを呼び出します（例: Idle=待機、Walk=歩く）。"));
        _form.AddItem(Kit.Heading("🖼 スプライトシート"));
        _form.AddItem(_lblSprite);
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 6, 0, 0) };
        row.Controls.Add(Kit.Btn("📁 画像を選ぶ", PickSprite, primary: true));
        _form.AddItem(row);

        _form.AddItem(Kit.Heading("✂ コマの分け方"));
        var cut = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        cut.Controls.Add(Kit.Field("横に何コマ", _fcx, null, 120));
        cut.Controls.Add(new Panel { Width = 12, Height = 1 });
        cut.Controls.Add(Kit.Field("縦に何コマ", _fcy, null, 120));
        _form.AddItem(cut);
        _form.AddItem(Kit.Hint("1枚の絵を、横×縦に等しく分けた1つ1つが「コマ」です。番号は左上が0で、右へ数えて、次の段へ続きます。"));

        _form.AddItem(Kit.Heading("🎞 使うコマと速さ"));
        var range = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        range.Controls.Add(Kit.Field("開始コマ", _start, null, 120));
        range.Controls.Add(new Panel { Width = 12, Height = 1 });
        range.Controls.Add(Kit.Field("終了コマ", _end, null, 120));
        _form.AddItem(range);
        _form.AddItem(Kit.Field("1秒に切り替えるコマ数", _fps, "数字が大きいほど速く動きます。歩きなら 8〜12、速い攻撃なら 16〜24 くらいが目安です。"));
        _form.AddItem(_loop);

        _txtName.TextChanged += (s, e) => Edit(c => c.name = _txtName.Text);
        _fcx.ValueChanged += (s, e) => Edit(c => c.frameCountX = (int)_fcx.Value, reloadRange: true);
        _fcy.ValueChanged += (s, e) => Edit(c => c.frameCountY = (int)_fcy.Value, reloadRange: true);
        _start.ValueChanged += (s, e) => Edit(c => { c.startFrame = (int)_start.Value; if (c.endFrame < c.startFrame) c.endFrame = c.startFrame; }, reloadRange: true);
        _end.ValueChanged += (s, e) => Edit(c => { c.endFrame = (int)_end.Value; if (c.startFrame > c.endFrame) c.startFrame = c.endFrame; }, reloadRange: true);
        _fps.ValueChanged += (s, e) => Edit(c => c.fps = (float)_fps.Value, restart: false);
        _loop.CheckedChanged += (s, e) => Edit(c => c.loop = _loop.Checked);
    }

    // 入力欄の変更を、選択中のクリップへ書き戻す。
    private void Edit(Action<AnimationClip> apply, bool reloadRange = false, bool restart = true)
    {
        if (_sync || _cur == null) return;
        apply(_cur);
        MarkDirty();
        if (reloadRange) { LoadRangeFields(_cur); }
        RebuildList();
        ApplyTimer();
        if (restart) { _frame = Math.Max(0, _cur.startFrame); _held = false; }
        if (!_playing) StartPlay();
        _sheetView.Invalidate();
        ShowFrame();
    }

    // ── 一覧 ────────────────────────────────────
    private string KeyFor(AnimationClip c)
    {
        if (!_keyOf.TryGetValue(c, out var k)) { k = "c" + _keySeq++; _keyOf[c] = k; _byKey[k] = c; }
        return k;
    }

    private Bitmap? Sheet(string rel)
    {
        if (string.IsNullOrEmpty(rel)) return null;
        if (_sheets.TryGetValue(rel, out var b)) return b;
        b = Kit.LoadBitmap(Path.Combine(_projectRoot, rel.Replace('/', '\\')));
        _sheets[rel] = b;
        return b;
    }

    // シートから、指定コマの絵を切り出す。範囲外・シートなしなら null。
    private static Bitmap? CutFrame(Bitmap? sheet, AnimationClip c, int index)
    {
        if (sheet == null || c.frameCountX <= 0 || c.frameCountY <= 0) return null;
        int cw = sheet.Width / c.frameCountX, ch = sheet.Height / c.frameCountY;
        if (cw <= 0 || ch <= 0) return null;
        int col = index % c.frameCountX, row = index / c.frameCountX;
        if (row >= c.frameCountY || index < 0) return null;
        var bmp = new Bitmap(cw, ch, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.DrawImage(sheet, new Rectangle(0, 0, cw, ch), new Rectangle(col * cw, row * ch, cw, ch), GraphicsUnit.Pixel);
        return bmp;
    }

    private void RebuildList()
    {
        var items = new List<CardItem>();
        foreach (var c in _clips)
        {
            using var f0 = CutFrame(Sheet(c.sprite), c, c.startFrame);
            int frames = Math.Max(1, c.endFrame - c.startFrame + 1);
            items.Add(new CardItem
            {
                Key = KeyFor(c), Title = c.name.Length > 0 ? c.name : "（名前なし）",
                Subtitle = $"{frames}コマ（{c.startFrame}〜{c.endFrame}） ・ {c.fps:0.#}fps ・ {(c.loop ? "ループ" : "1回")}",
                Accent = UiTheme.KindTrigger, Icon = Kit.Thumb(f0, 40),
                Tip = string.IsNullOrEmpty(c.sprite) ? "画像が未設定" : c.sprite,
            });
        }
        _list.EmptyText = "クリップがありません";
        _list.SetItems(items);
        if (_cur != null) _list.SetSelectedKey(KeyFor(_cur), scrollIntoView: false);
    }

    private void Select(AnimationClip c)
    {
        _cur = c;
        _list.SetSelectedKey(KeyFor(c));
        ShowForm(c);
    }

    private void ShowForm(AnimationClip? c)
    {
        _empty.Visible = c == null;
        _form.Visible = c != null;
        _timer.Stop(); _playing = false;
        if (c == null) { _anim.Image = null; _sheetView.Image = null; _lblFrame.Text = ""; return; }
        _sync = true;
        _txtName.Text = c.name;
        _fcx.Value = Math.Clamp(c.frameCountX, 1, 64);
        _fcy.Value = Math.Clamp(c.frameCountY, 1, 64);
        LoadRangeFields(c);
        _fps.Value = c.fps;
        _loop.Checked = c.loop;
        _sync = false;

        var sheet = Sheet(c.sprite);
        _lblSprite.Text = string.IsNullOrEmpty(c.sprite) ? "（画像が選ばれていません）"
            : $"{c.sprite}" + (sheet == null ? "  ⚠ 見つかりません" : $"   {sheet.Width}×{sheet.Height}px");
        _lblSprite.ForeColor = sheet == null && !string.IsNullOrEmpty(c.sprite) ? Color.Firebrick : UiTheme.TextMain;
        _sheetView.Image = sheet;
        _sheetView.Invalidate();
        _form.ScrollToTop();

        _frame = c.startFrame; _held = false;
        ApplyTimer();
        StartPlay();
        ShowFrame();
    }

    // 開始・終了コマの入力欄の上限を、コマの総数に合わせる。
    private void LoadRangeFields(AnimationClip c)
    {
        bool was = _sync; _sync = true;
        int total = Math.Max(1, c.frameCountX * c.frameCountY);
        _start.Maximum = total - 1; _end.Maximum = total - 1;
        c.startFrame = Math.Clamp(c.startFrame, 0, total - 1);
        c.endFrame = Math.Clamp(c.endFrame, c.startFrame, total - 1);
        _start.Value = c.startFrame; _end.Value = c.endFrame;
        _sync = was;
    }

    // ── 再生 ────────────────────────────────────
    private void ApplyTimer()
    {
        if (_cur == null) return;
        _timer.Interval = _cur.fps > 0 ? Math.Max(15, (int)(1000f / _cur.fps)) : 100;
    }

    private void StartPlay()
    {
        if (_cur == null || Sheet(_cur.sprite) == null) return;
        _playing = true; _held = false;
        _btnPlay.Text = "⏸ 止める";
        _timer.Start();
    }

    private void TogglePlay()
    {
        if (_playing) { _timer.Stop(); _playing = false; _btnPlay.Text = "▶ 再生"; return; }
        if (_cur != null && (_held || _frame >= _cur.endFrame)) _frame = _cur.startFrame;
        StartPlay();
    }

    private void Tick(object? sender, EventArgs e)
    {
        if (_cur == null) return;
        if (_cur.endFrame <= _cur.startFrame) { _frame = _cur.startFrame; ShowFrame(); return; }   // 1コマだけ＝止まった絵
        _frame++;
        if (_frame > _cur.endFrame)
        {
            if (_cur.loop) _frame = _cur.startFrame;
            else { _frame = _cur.endFrame; _held = true; _timer.Stop(); _playing = false; _btnPlay.Text = "▶ もう一度"; }
        }
        ShowFrame();
    }

    private void ShowFrame()
    {
        if (_cur == null) return;
        var old = _anim.Image;
        _anim.Image = CutFrame(Sheet(_cur.sprite), _cur, _frame);
        old?.Dispose();
        _lblFrame.Text = _anim.Image == null ? "" : $"コマ {_frame}（{_cur.startFrame}〜{_cur.endFrame}）";
        _sheetView.Invalidate();
    }

    // ── スプライトシートの重ね描きとクリック ──────
    // コマの格子線、使うコマの範囲、いま表示中のコマの枠を、シートの上に重ねる。
    private void DrawSheetOverlay(Graphics g, RectangleF r, float k)
    {
        if (_cur == null) return;
        int fx = Math.Max(1, _cur.frameCountX), fy = Math.Max(1, _cur.frameCountY);
        float cw = r.Width / fx, ch = r.Height / fy;
        using var rangeBrush = new SolidBrush(Color.FromArgb(70, 0, 160, 255));
        using var gridPen = new Pen(Color.FromArgb(120, 255, 255, 255));
        using var curPen = new Pen(Color.FromArgb(255, 255, 200, 0), 2);
        using var edgePen = new Pen(Color.FromArgb(255, 0, 150, 255), 2);
        for (int i = _cur.startFrame; i <= _cur.endFrame && i < fx * fy; i++)
            g.FillRectangle(rangeBrush, r.X + (i % fx) * cw, r.Y + (i / fx) * ch, cw, ch);
        for (int x = 0; x <= fx; x++) g.DrawLine(gridPen, r.X + x * cw, r.Y, r.X + x * cw, r.Bottom);
        for (int y = 0; y <= fy; y++) g.DrawLine(gridPen, r.X, r.Y + y * ch, r.Right, r.Y + y * ch);
        if (_cur.startFrame < fx * fy) g.DrawRectangle(edgePen, r.X + (_cur.startFrame % fx) * cw, r.Y + (_cur.startFrame / fx) * ch, cw, ch);
        if (_cur.endFrame < fx * fy) g.DrawRectangle(edgePen, r.X + (_cur.endFrame % fx) * cw, r.Y + (_cur.endFrame / fx) * ch, cw, ch);
        if (_frame < fx * fy) g.DrawRectangle(curPen, r.X + (_frame % fx) * cw + 2, r.Y + (_frame / fx) * ch + 2, cw - 4, ch - 4);
        // コマ番号
        for (int i = 0; i < fx * fy && cw > 26 && ch > 18; i++)
            TextRenderer.DrawText(g, i.ToString(), UiTheme.Small, new Point((int)(r.X + (i % fx) * cw + 3), (int)(r.Y + (i / fx) * ch + 1)), Color.White);
    }

    // シートのコマをクリック＝開始コマ、右クリック＝終了コマ。
    private void SheetMouseDown(object? sender, MouseEventArgs e)
    {
        if (_cur == null || _sheetView.Image == null || _sheetView.ImageRect.IsEmpty) return;
        var r = _sheetView.ImageRect;
        if (!r.Contains(e.Location)) return;
        int fx = Math.Max(1, _cur.frameCountX), fy = Math.Max(1, _cur.frameCountY);
        int col = Math.Clamp((int)((e.X - r.X) / (r.Width / fx)), 0, fx - 1);
        int row = Math.Clamp((int)((e.Y - r.Y) / (r.Height / fy)), 0, fy - 1);
        int idx = row * fx + col;
        if (e.Button == MouseButtons.Right) { _cur.endFrame = idx; if (_cur.startFrame > idx) _cur.startFrame = idx; }
        else if (e.Button == MouseButtons.Left) { _cur.startFrame = idx; if (_cur.endFrame < idx) _cur.endFrame = idx; }
        else return;
        MarkDirty();
        LoadRangeFields(_cur);
        _frame = _cur.startFrame; _held = false;
        RebuildList();
        if (!_playing) StartPlay();
        ShowFrame();
    }

    // ── 操作 ────────────────────────────────────
    private void AddClip()
    {
        // 直前のクリップと同じ画像・分け方から始めると、続けて作りやすい。
        var c = new AnimationClip { name = $"clip{_clips.Count + 1}", fps = 12f };
        if (_cur != null) { c.sprite = _cur.sprite; c.frameCountX = _cur.frameCountX; c.frameCountY = _cur.frameCountY; }
        _clips.Add(c);
        MarkDirty();
        RebuildList();
        Select(c);
    }

    private void Duplicate()
    {
        if (_cur == null) return;
        var c = new AnimationClip
        {
            name = _cur.name + "_copy", sprite = _cur.sprite, frameCountX = _cur.frameCountX, frameCountY = _cur.frameCountY,
            startFrame = _cur.startFrame, endFrame = _cur.endFrame, fps = _cur.fps, loop = _cur.loop,
        };
        _clips.Insert(_clips.IndexOf(_cur) + 1, c);
        MarkDirty();
        RebuildList();
        Select(c);
    }

    private void Delete()
    {
        if (_cur == null) return;
        if (MessageBox.Show($"クリップ「{_cur.name}」を削除しますか？", "確認", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        int i = _clips.IndexOf(_cur);
        _clips.Remove(_cur);
        _cur = null;
        MarkDirty();
        RebuildList();
        if (_clips.Count > 0) Select(_clips[Math.Min(i, _clips.Count - 1)]); else ShowForm(null);
    }

    // スプライトシートの画像を選び、プロジェクトの img フォルダへコピーして設定する。
    private void PickSprite()
    {
        if (_cur == null) return;
        using var dlg = new OpenFileDialog { Title = "スプライトシートを選ぶ", Filter = "画像ファイル|*.png;*.jpg;*.bmp|すべて|*.*" };
        if (dlg.ShowDialog() != DialogResult.OK) return;
        try
        {
            Directory.CreateDirectory(_imgDir);
            string dest = Path.Combine(_imgDir, Path.GetFileName(dlg.FileName));
            if (!string.Equals(dlg.FileName, dest, StringComparison.OrdinalIgnoreCase)) File.Copy(dlg.FileName, dest, overwrite: true);
            string rel = "img/" + Path.GetFileName(dlg.FileName);
            if (_sheets.Remove(rel, out var old)) old?.Dispose();    // 同じ名前で差し替えたときのため、読み直す
            _cur.sprite = rel;
            MarkDirty();
            RebuildList();
            ShowForm(_cur);
        }
        catch (Exception ex)
        {
            MessageBox.Show("ファイルのコピーに失敗しました:\n" + ex.Message, "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // ── 保存 ────────────────────────────────────
    protected override void OnSave()
    {
        ResultSet = new AnimationSet
        {
            assetId = _txtAssetId.Text,
            clips = _clips.Select(c => new AnimationClip
            {
                name = c.name, sprite = c.sprite, frameCountX = c.frameCountX, frameCountY = c.frameCountY,
                startFrame = c.startFrame, endFrame = c.endFrame, fps = c.fps, loop = c.loop,
            }).ToList(),
        };
        DialogResult = DialogResult.OK;
        Close();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _timer.Stop(); _timer.Dispose();
        _anim.Image?.Dispose();
        foreach (var b in _sheets.Values) b?.Dispose();
        base.OnFormClosed(e);
    }
}
