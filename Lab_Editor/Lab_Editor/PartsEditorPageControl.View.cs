using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace Lab_Editor;

// ======================================================
// PartsEditorPageControl（合成プレビュー）
//
// 以前の合成プレビューは「本体の画像を画面の半分に収める」固定の倍率で描いていたため、
//   ・大きな合成体（芋虫のような横長）は左右が見切れて全体が見えない
//   ・小さなパーツは点にしか見えず、掴めない
//   ・位置合わせは1px単位のドラッグか数値入力だけ
// だった。ここでは表示を「ワールド座標（本体の左上が0,0・単位は論理px）」と「画面」の2つに分け、
//   画面 = パン + ワールド × ズーム
// の変換を1か所(W2S / S2W)に集めた。描画・掴む判定・ドラッグはすべてこの変換を通るので、
// 見えている場所と掴める場所は食い違わない。
// ======================================================
public partial class PartsEditorPageControl
{
    // ==== キャンバス用の自前パネル ====
    // ちらつきを防ぐ二重バッファと、矢印キーを「入力キー」として受け取る設定を持つパネル。
    // 標準のPanelは矢印キーをフォーカス移動に使ってしまい、KeyDownに届かない。
    private sealed class CanvasPanel : Panel
    {
        public CanvasPanel()
        {
            DoubleBuffered = true;
            ResizeRedraw = true;
            TabStop = true;
            SetStyle(ControlStyles.Selectable, true);
        }
        protected override bool IsInputKey(Keys keyData)
        {
            switch (keyData & Keys.KeyCode)
            {
                case Keys.Left: case Keys.Right: case Keys.Up: case Keys.Down: return true;
            }
            return base.IsInputKey(keyData);
        }
    }

    // ==== 表示の状態 ====
    private float _zoom = 1f;                 // 画面px ÷ ワールド単位
    private PointF _pan = new(40f, 40f);      // ワールド原点(0,0)が画面のどこにあるか
    private const float GridStep = 8f;        // グリッドとスナップの間隔（論理px）
    private CheckBox _chkGrid = null!, _chkSnap = null!, _chkHitbox = null!, _chkIds = null!;
    private Label _lblZoom = null!;
    private bool _showGrid => _chkGrid == null! || _chkGrid.Checked;
    private bool _snapOn => _chkSnap == null! || _chkSnap.Checked;
    private bool _showHitbox => _chkHitbox != null! && _chkHitbox.Checked;
    private bool _showIds => _chkIds == null! || _chkIds.Checked;

    // ==== 操作の状態 ====
    private enum DragMode { None, Move, Resize, Pan, Rubber }
    private DragMode _drag = DragMode.None;
    private Point _dragStartScreen;                   // ドラッグを始めたときのマウス（画面）
    private PointF _dragStartWorld;                   // 同（ワールド）
    private PointF _panStart;                         // パンを始めたときの _pan
    private int _dragPrimary = -1;                    // 掴んだパーツ
    private Dictionary<int, PointF> _dragStartOffsets = new(); // 移動を始めたときの、各パーツの offset
    private SizeF _resizeStartSize;                   // 大きさ変更を始めたときの表示サイズ（ワールド）
    private bool _dragChanged;                        // ドラッグ中に実際に値が変わったか（履歴に積むか判断する）
    private Rectangle _rubber;                        // 範囲選択の矩形（画面）
    private bool _rubberAdditive;                     // 範囲選択が既存の選択に加えるか（Ctrl/Shift）
    private HashSet<int> _rubberBase = new();         // 範囲選択を始めたときの選択
    private int _hoverIndex = -1;                     // マウスが乗っているパーツ
    private bool _hoverHandle;                        // マウスがつまみの上か
    private bool _spaceDown;                          // Spaceを押している（押しながらドラッグで移動）

    private ImageAttributes? _bodyAttr;               // 本体の絵を薄く描くための設定

    // ==== 座標 ====

    // 本体の論理サイズ（ワールド単位）。当たり判定の大きさが分かっていればそれ、無ければ画像の原寸。
    // ゲームはパーツの offset を、この本体の左上を原点として解釈している。
    private (float w, float h) BodySize()
    {
        if (baseLogicalW > 0f && baseLogicalH > 0f) return (baseLogicalW, baseLogicalH);
        if (baseSprite != null) return (baseSprite.Width, baseSprite.Height);
        return (32f, 32f);
    }

    // パーツの表示サイズ（ワールド単位）。表示サイズは画像の原寸ではなくパーツ定義の width/height（論理サイズ）で決まり、
    // 0のときだけ画像の原寸を使う。ゲームと同じ解釈。
    private (float w, float h) EffectivePartSize(PartDef p)
    {
        var thumb = GetPartThumb(p);
        float lw = p.width > 0 ? p.width : (thumb?.Width ?? 24);
        float lh = p.height > 0 ? p.height : (thumb?.Height ?? 24);
        return (lw * p.scale, lh * p.scale);
    }

    private PointF W2S(float wx, float wy) => new(_pan.X + wx * _zoom, _pan.Y + wy * _zoom);
    private PointF S2W(Point sp) => new((sp.X - _pan.X) / _zoom, (sp.Y - _pan.Y) / _zoom);

    // パーツの画面上の矩形。小さすぎて掴めなくなる（点にしか見えない）のを避けるため、最低6pxは確保する。
    private RectangleF PartScreenRect(PartDef p, float ox, float oy)
    {
        var s = EffectivePartSize(p);
        var tl = W2S(ox, oy);
        return new RectangleF(tl.X, tl.Y, Math.Max(s.w * _zoom, 6f), Math.Max(s.h * _zoom, 6f));
    }

    // 全パーツと本体が収まる倍率・位置にする。最初に開いたとき・「全体表示」・テンプレート追加後に呼ぶ。
    private void FitView()
    {
        if (pnlComposer == null! || pnlComposer.Width < 20 || pnlComposer.Height < 20) return;
        var (bw, bh) = BodySize();
        float minX = 0f, minY = 0f, maxX = bw, maxY = bh;
        foreach (var p in parts)
        {
            var s = EffectivePartSize(p);
            minX = Math.Min(minX, p.offsetX); minY = Math.Min(minY, p.offsetY);
            maxX = Math.Max(maxX, p.offsetX + s.w); maxY = Math.Max(maxY, p.offsetY + s.h);
        }
        float w = Math.Max(maxX - minX, 8f), h = Math.Max(maxY - minY, 8f);
        const float margin = 40f;
        float z = Math.Min((pnlComposer.Width - margin * 2f) / w, (pnlComposer.Height - margin * 2f) / h);
        _zoom = Math.Clamp(z, 0.25f, 16f);
        _pan = new PointF(pnlComposer.Width / 2f - (minX + w / 2f) * _zoom, pnlComposer.Height / 2f - (minY + h / 2f) * _zoom);
        UpdateZoomLabel();
        pnlComposer.Invalidate();
    }

    // 画面の中心を保ったまま倍率を factor 倍にする（＋／－ボタン）
    private void ZoomBy(float factor) => ZoomAt(new Point(pnlComposer.Width / 2, pnlComposer.Height / 2), factor);

    // 指定した画面上の点が動かないように倍率を変える（ホイール）。マウスの下の物を見たまま拡大縮小できる。
    private void ZoomAt(Point screenPt, float factor)
    {
        var before = S2W(screenPt);
        _zoom = Math.Clamp(_zoom * factor, 0.25f, 40f);
        _pan = new PointF(screenPt.X - before.X * _zoom, screenPt.Y - before.Y * _zoom);
        UpdateZoomLabel();
        pnlComposer.Invalidate();
    }

    private void UpdateZoomLabel()
    {
        if (_lblZoom != null!) _lblZoom.Text = $"{_zoom * 100f:0}%";
    }

    // グリッドへの吸着。Alt を押している間は吸着しない（細かく合わせたいとき）。
    private float Snap(float v)
    {
        if (!_snapOn || (Control.ModifierKeys & Keys.Alt) != 0) return v;
        return MathF.Round(v / GridStep) * GridStep;
    }

    // ==== 画面の組み立て（プレビュー本体と、その上の表示設定バー） ====

    // 中央パネル（再生バー・表示設定・合成プレビューのキャンバス）を組み立てて返す。
    private Panel BuildComposerSide()
    {
        var pnl = new Panel { Dock = DockStyle.Fill };

        // 1段目：再生バー。Feature: UI改善（提案書 PT-1）— 保存してゲームを起動しなくても、その場で動きを確認できる。
        var flowPlayback = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = true, Padding = new Padding(4, 3, 4, 1) };
        // 再生/停止を切り替えるトグルボタン。
        // AutoSize のボタンは標準の最小幅(75px)より縮まないので、GrowAndShrink にして文字に合わせた幅にする
        Button Small(string text, int pad = 6) => new Button { Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(pad, 2, pad, 2), Margin = new Padding(2, 2, 2, 2) };
        _btnPlayToggle = Small("▶ 再生", 8);
        _btnPlayToggle.Click += (s, e) => TogglePreviewPlayback();
        // 経過時間を0に巻き戻すボタン（再生を止めずに巻き戻すことも可能）。
        var btnResetTime = Small("⏮ 0に戻す");
        btnResetTime.Click += (s, e) => { _previewTime = 0f; pnlComposer.Invalidate(); };
        var sep1 = new Label { Text = "│", AutoSize = true, ForeColor = Color.Silver, Margin = new Padding(2, 6, 2, 0) };
        var btnFit = Small("⛶ 全体表示");
        btnFit.Click += (s, e) => FitView();
        var btnZoomIn = Small("＋", 4);
        btnZoomIn.Click += (s, e) => ZoomBy(1.4f);
        var btnZoomOut = Small("－", 4);
        btnZoomOut.Click += (s, e) => ZoomBy(1f / 1.4f);
        _lblZoom = new Label { Text = "100%", AutoSize = true, Margin = new Padding(4, 7, 4, 0), MinimumSize = new Size(44, 0), ForeColor = Color.DimGray };
        var sep2 = new Label { Text = "│", AutoSize = true, ForeColor = Color.Silver, Margin = new Padding(2, 6, 2, 0) };
        _chkGrid = new CheckBox { Text = "グリッド", Checked = true, AutoSize = true, Margin = new Padding(3, 5, 3, 0) };
        _chkSnap = new CheckBox { Text = "吸着(8px)", Checked = true, AutoSize = true, Margin = new Padding(3, 5, 3, 0) };
        _chkHitbox = new CheckBox { Text = "当たり判定", Checked = false, AutoSize = true, Margin = new Padding(3, 5, 3, 0) };
        _chkIds = new CheckBox { Text = "ID", Checked = true, AutoSize = true, Margin = new Padding(3, 5, 3, 0) };
        foreach (var chk in new[] { _chkGrid, _chkSnap, _chkHitbox, _chkIds }) chk.CheckedChanged += (s, e) => pnlComposer.Invalidate();
        var tip = new ToolTip();
        tip.SetToolTip(_chkSnap, "ドラッグ・つまみをグリッドに吸着させます。Altを押している間は吸着しません");
        tip.SetToolTip(_chkHitbox, "すべてのパーツの当たり判定（赤い点線）を表示します。選んだパーツは常に表示されます");
        tip.SetToolTip(btnFit, "全パーツと本体が収まる大きさにします（Home / F キーでも）");
        flowPlayback.Controls.AddRange(new Control[] { _btnPlayToggle, btnResetTime, sep1, btnFit, btnZoomOut, btnZoomIn, _lblZoom, sep2, _chkGrid, _chkSnap, _chkHitbox, _chkIds });

        // 合成プレビューを実際に描画するキャンバス。背景をダークグレーにして、明るい色のパーツ画像が見やすいようにしている。
        pnlComposer = new CanvasPanel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(44, 46, 52) };
        pnlComposer.Paint += PnlComposer_Paint;
        pnlComposer.MouseDown += PnlComposer_MouseDown;
        pnlComposer.MouseMove += PnlComposer_MouseMove;
        pnlComposer.MouseUp += PnlComposer_MouseUp;
        pnlComposer.MouseWheel += (s, e) => ZoomAt(e.Location, e.Delta > 0 ? 1.18f : 1f / 1.18f);
        // ホイールは「フォーカスのあるコントロール」へ届くので、マウスが乗ったらキャンバスへフォーカスを移す。
        // ただし、数値や文字を入力している最中は、その入力を横取りしない。
        pnlComposer.MouseEnter += (s, e) =>
        {
            var f = FindForm()?.ActiveControl;
            if (f is not (TextBox or NumericUpDown or ComboBox)) pnlComposer.Focus();
        };
        pnlComposer.MouseLeave += (s, e) => { _hoverIndex = -1; _hoverHandle = false; if (_lblCoords != null!) _lblCoords.Text = ""; pnlComposer.Invalidate(); };
        pnlComposer.KeyDown += (s, e) =>
        {
            if (e.KeyCode == Keys.Space) { _spaceDown = true; pnlComposer.Cursor = Cursors.Hand; e.Handled = true; }
            else if (e.KeyCode == Keys.Home || (e.KeyCode == Keys.F && !e.Control)) { FitView(); e.Handled = true; }
            else if (e.KeyCode is Keys.Oemplus or Keys.Add) { ZoomBy(1.25f); e.Handled = true; }
            else if (e.KeyCode is Keys.OemMinus or Keys.Subtract) { ZoomBy(0.8f); e.Handled = true; }
        };
        pnlComposer.KeyUp += (s, e) => { if (e.KeyCode == Keys.Space) { _spaceDown = false; pnlComposer.Cursor = Cursors.Default; } };
        pnlComposer.ContextMenuStrip = BuildPartContextMenu();
        // パネルのサイズが初めて確定したとき、全体が収まる表示にする（Loadより前に大きさが変わることがあるため）
        pnlComposer.Resize += (s, e) => { if (!_viewFitted && pnlComposer.Width > 50 && pnlComposer.Height > 50) { _viewFitted = true; FitView(); } };

        pnl.Controls.Add(pnlComposer);
        pnl.Controls.Add(flowPlayback);
        return pnl;
    }
    private bool _viewFitted;

    // 再生バーの「▶ 再生」/「⏸ 停止」ボタンが押されたときに、再生状態を反転させる。
    private void TogglePreviewPlayback()
    {
        _isPlaying = !_isPlaying;
        if (_isPlaying)
        {
            _btnPlayToggle.Text = "⏸ 停止";
            // タイマーが未作成であれば、ここで初めて生成する（初回再生時に1度だけ作られる）。
            if (_previewTimer == null)
            {
                _previewTimer = new System.Windows.Forms.Timer { Interval = 16 }; // 実機と同じ約60fps相当でTimeを進める
                // Tickのたびに経過時間を1フレーム分進めて、キャンバスを再描画する。
                _previewTimer.Tick += (s, e) => { _previewTime += 1.0f; pnlComposer.Invalidate(); };
            }
            _previewTimer.Start();
        }
        else
        {
            _btnPlayToggle.Text = "▶ 再生";
            _previewTimer?.Stop();
        }
        pnlComposer.Invalidate();
    }

    // 本体の基準スプライト画像をファイルから読み込む。合成プレビューに薄く表示し、
    // 各パーツの位置関係を把握しやすくするための目印として使う。
    private void LoadBaseSprite()
    {
        if (string.IsNullOrEmpty(baseSpritePath)) return;
        string full = Path.Combine(projectRoot, baseSpritePath.Replace('/', '\\'));
        if (!File.Exists(full)) return;
        try
        {
            // 他プロセス（ゲーム本体等）が同じファイルを開いていても読み込めるよう、
            // 共有読み取り(FileShare.Read)モードでストリームを開いてから画像化する。
            using var fs = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read);
            baseSprite = Image.FromStream(fs);
        }
        catch { baseSprite = null; } // 画像が壊れている等で読み込みに失敗しても、致命的エラーにはせず「画像なし」として続行する
    }

    // 指定パーツのサムネイル画像を取得する。一度読み込んだ画像はキャッシュされ、
    // 同じパーツについて2回目以降はファイルI/Oを行わずキャッシュから即座に返す。
    private Image? GetPartThumb(PartDef p)
    {
        if (_partThumbCache.TryGetValue(p, out var cached)) return cached;
        Image? img = null;
        if (!string.IsNullOrEmpty(p.sprite))
        {
            string full = Path.Combine(projectRoot, p.sprite.Replace('/', '\\'));
            if (File.Exists(full))
            {
                try
                {
                    using var fs = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read);
                    img = Image.FromStream(fs);
                }
                catch { img = null; } // 読み込み失敗時はサムネイルなし（後述の描画側でオレンジの丸に代替表示される）
            }
        }
        // 画像が見つからなかった場合も含めて、結果（nullでも）をキャッシュしておくことで
        // 存在しない画像パスに対して毎回無駄なファイルアクセスを繰り返さないようにする。
        _partThumbCache[p] = img;
        return img;
    }

    // 指定パーツのサムネイルキャッシュを破棄する。画像を差し替えた時や、パーツ自体を削除する時に
    // 呼び出し、古い画像リソースをDisposeしてメモリを解放する。
    private void InvalidatePartThumb(PartDef p)
    {
        if (_partThumbCache.TryGetValue(p, out var old)) { old?.Dispose(); _partThumbCache.Remove(p); }
    }

    // ==== 描画 ====

    // 合成プレビューキャンバスの描画本体。毎フレーム（Invalidateされるたび）呼び出され、
    // 「グリッド → 本体の基準スプライト → 各パーツ（zOrder順）→ 選択・つまみ → 再生表示 → 空のときの案内」
    // の順に描き重ねていく。
    private void PnlComposer_Paint(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        // ドット絵をぼかさずくっきり拡大表示するため、補間モードを最近傍法にする。
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.SmoothingMode = SmoothingMode.None;

        DrawGridAndBody(g);

        // zOrderの小さい順（奥から手前へ）に描画することで、値が大きいパーツが上に重なって見えるようにする。
        var order = Enumerable.Range(0, parts.Count).OrderBy(i => parts[i].zOrder).ToList();
        var poses = new Dictionary<int, (float ox, float oy, float angle)>();
        foreach (int i in order)
        {
            var p = parts[i];
            // 通常時（停止中）はパーツに設定された固定のoffsetX/offsetYをそのまま使う。
            float ox = p.offsetX, oy = p.offsetY, angleRad = 0f;
            if (_isPlaying)
            {
                // 再生中は挙動スクリプトを現在時刻(_previewTime)とパーツ番号(i=PartIndex)で評価し、
                // スクリプトが位置や角度を動的に変えている場合はその評価結果で上書きする。
                var pose = ScriptPreviewEvaluator.Evaluate(p.script, _previewTime, i);
                if (pose.HasOffset) { ox = pose.OffsetX; oy = pose.OffsetY; }
                if (pose.HasAngle) angleRad = pose.Angle;
            }
            poses[i] = (ox, oy, angleRad);
            var rect = PartScreenRect(p, ox, oy);
            var thumb = GetPartThumb(p);

            // 回転角度がある場合は、パーツの中心を軸に回転描画するため一時的に座標系を
            // 「中心へ平行移動→回転→元に戻す」という変換にしてから描き、描画後に元の座標系へ復元する。
            // 回転の軸はパーツの中心。ゲーム側のDrawRotaGraphも中心を軸に回すので、それに合わせる。
            var savedState = angleRad != 0f ? g.Save() : null;
            if (angleRad != 0f)
            {
                float pivotX = rect.X + rect.Width / 2f, pivotY = rect.Y + rect.Height / 2f;
                g.TranslateTransform(pivotX, pivotY);
                g.RotateTransform(angleRad * 180f / MathF.PI);
                g.TranslateTransform(-pivotX, -pivotY);
            }
            // サムネイル画像があればそれを描画し、なければ「画像未設定」を表すオレンジ色の丸で代替表示する。
            if (thumb != null) g.DrawImage(thumb, rect);
            else
            {
                using var b = new SolidBrush(Color.FromArgb(160, 255, 140, 0));
                g.FillEllipse(b, rect);
            }
            if (savedState != null) g.Restore(savedState);
        }

        // 枠・当たり判定・ID・つまみは、すべてのパーツを描いたあとに上から重ねる（パーツの絵に隠れないように）。
        foreach (int i in order)
        {
            var p = parts[i];
            var (ox, oy, _) = poses[i];
            var rect = PartScreenRect(p, ox, oy);
            bool isSel = _selected.Contains(i);
            bool isPrimary = i == selectedIndex;

            // 「このスクリプトを全パーツへ」適用直後などにハイライト対象になっているパーツは、
            // 通常の枠の外側にもう一段太い緑の枠を追加で描いて目立たせる。
            if (_highlightedParts.Contains(p))
            {
                using var glowPen = new Pen(Color.Lime, 3f);
                g.DrawRectangle(glowPen, rect.X - 3, rect.Y - 3, rect.Width + 6, rect.Height + 6);
            }
            // 枠：主選択は黄色の太枠、他の選択は橙、乗っているパーツは水色、触れるとダメージは赤みのある細枠、その他は白っぽい細枠。
            Color frame = isPrimary ? Color.Yellow
                        : isSel ? Color.Orange
                        : i == _hoverIndex ? Color.Cyan
                        : p.deadly ? Color.FromArgb(220, 255, 90, 90)
                        : Color.FromArgb(150, 255, 255, 255);
            using (var pen = new Pen(frame, isSel ? 2f : (i == _hoverIndex ? 1.6f : 1f)))
                g.DrawRectangle(pen, rect.X, rect.Y, rect.Width, rect.Height);

            // 当たり判定（赤い点線）。ゲームの GetPartHitRect と同じ計算（位置＋オフセット×倍率、大きさ×倍率）。
            if (_showHitbox || isSel)
            {
                var hbTl = W2S(ox + p.hitboxOffsetX * p.scale, oy + p.hitboxOffsetY * p.scale);
                using var hbPen = new Pen(Color.FromArgb(220, 255, 70, 70), 1f) { DashStyle = DashStyle.Dash };
                g.DrawRectangle(hbPen, hbTl.X, hbTl.Y, p.hitboxWidth * p.scale * _zoom, p.hitboxHeight * p.scale * _zoom);
            }

            // パーツのIDを小さな文字で表示し、どのマーカーがどのパーツかを一覧と対応づけやすくする。
            if (_showIds || isSel || i == _hoverIndex)
            {
                using var smallFont = new Font(Font.FontFamily, 7.5f);
                g.DrawString(p.id, smallFont, Brushes.Black, rect.X + 1, rect.Bottom + 2);
                g.DrawString(p.id, smallFont, isSel ? Brushes.Yellow : Brushes.White, rect.X, rect.Bottom + 1);
            }
        }

        // 大きさ変更のつまみ（1つだけ選んでいて、再生中でないとき）
        if (!_isPlaying && _selected.Count == 1 && selectedIndex >= 0 && selectedIndex < parts.Count)
        {
            var hr = HandleRect(selectedIndex);
            using var hb = new SolidBrush(_hoverHandle || _drag == DragMode.Resize ? Color.Yellow : Color.White);
            g.FillRectangle(hb, hr);
            g.DrawRectangle(Pens.Black, hr.X, hr.Y, hr.Width, hr.Height);
        }

        // 範囲選択の矩形
        if (_drag == DragMode.Rubber && _rubber.Width > 0 && _rubber.Height > 0)
        {
            using var fill = new SolidBrush(Color.FromArgb(50, 90, 160, 255));
            g.FillRectangle(fill, _rubber);
            using var pen = new Pen(Color.FromArgb(220, 120, 180, 255), 1f);
            g.DrawRectangle(pen, _rubber);
        }

        // 再生中であることと現在の経過時間を、キャンバス左上に文字で表示する。
        if (_isPlaying)
        {
            using var playFont = new Font(Font.FontFamily, 8f, FontStyle.Bold);
            g.DrawString($"再生中... t={_previewTime:0}", playFont, Brushes.LightGreen, 8, 8);
        }

        // パーツが1つもない場合は、空のキャンバスのままだと何をすればいいか分からないため、
        // 操作方法を案内するメッセージを表示する。
        if (parts.Count == 0)
        {
            using var f = new Font(Font.FontFamily, 10f);
            const string msg = "パーツがありません。\n上の「＋ 追加」か「✨ テンプレートから追加…」から作成してください。";
            var sz = g.MeasureString(msg, f, pnlComposer.Width - 40);
            g.DrawString(msg, f, Brushes.LightGray, new RectangleF((pnlComposer.Width - sz.Width) / 2f, pnlComposer.Height / 2f - sz.Height / 2f, sz.Width + 4, sz.Height + 4));
        }
    }

    // グリッド・原点の十字・本体（薄い絵と点線の枠）を描く
    private void DrawGridAndBody(Graphics g)
    {
        int cw = pnlComposer.Width, ch = pnlComposer.Height;
        var topLeft = S2W(new Point(0, 0));
        var botRight = S2W(new Point(cw, ch));

        // グリッド。8pxの細い線は十分に拡大したときだけ（細かすぎる線で画面が埋まらないように）、32pxの太い線は常に。
        if (_showGrid)
        {
            if (_zoom >= 3f)
            {
                using var fine = new Pen(Color.FromArgb(22, 255, 255, 255), 1f);
                for (float x = MathF.Floor(topLeft.X / GridStep) * GridStep; x <= botRight.X; x += GridStep) { var s = W2S(x, 0); g.DrawLine(fine, s.X, 0, s.X, ch); }
                for (float y = MathF.Floor(topLeft.Y / GridStep) * GridStep; y <= botRight.Y; y += GridStep) { var s = W2S(0, y); g.DrawLine(fine, 0, s.Y, cw, s.Y); }
            }
            if (_zoom >= 0.7f)
            {
                using var coarse = new Pen(Color.FromArgb(44, 255, 255, 255), 1f);
                for (float x = MathF.Floor(topLeft.X / 32f) * 32f; x <= botRight.X; x += 32f) { var s = W2S(x, 0); g.DrawLine(coarse, s.X, 0, s.X, ch); }
                for (float y = MathF.Floor(topLeft.Y / 32f) * 32f; y <= botRight.Y; y += 32f) { var s = W2S(0, y); g.DrawLine(coarse, 0, s.Y, cw, s.Y); }
            }
        }

        // 本体：薄い絵と、点線の枠。パーツと見分けがつくように、パーツより暗く描く。
        var (bw, bh) = BodySize();
        var tl = W2S(0, 0);
        var br = W2S(bw, bh);
        var bodyRect = new RectangleF(tl.X, tl.Y, br.X - tl.X, br.Y - tl.Y);
        if (baseSprite != null)
        {
            if (_bodyAttr == null)
            {
                _bodyAttr = new ImageAttributes();
                _bodyAttr.SetColorMatrix(new ColorMatrix { Matrix33 = 0.42f }); // 不透明度42%
            }
            g.DrawImage(baseSprite, Rectangle.Round(bodyRect), 0, 0, baseSprite.Width, baseSprite.Height, GraphicsUnit.Pixel, _bodyAttr);
        }
        using (var bodyPen = new Pen(Color.FromArgb(170, 130, 190, 255), 1f) { DashStyle = DashStyle.Dash })
            g.DrawRectangle(bodyPen, bodyRect.X, bodyRect.Y, bodyRect.Width, bodyRect.Height);

        // 原点の十字（本体の左上＝パーツの offset の基準）
        using (var axis = new Pen(Color.FromArgb(120, 0, 255, 255), 1f))
        {
            g.DrawLine(axis, tl.X - 8, tl.Y, tl.X + 8, tl.Y);
            g.DrawLine(axis, tl.X, tl.Y - 8, tl.X, tl.Y + 8);
        }
        using var lblFont = new Font(Font.FontFamily, 7.5f);
        g.DrawString("本体の左上 (0,0)", lblFont, Brushes.Aqua, tl.X + 3, tl.Y - 14);
        g.DrawString($"本体 {bw:0.#}×{bh:0.#}", lblFont, Brushes.LightSkyBlue, bodyRect.X, bodyRect.Bottom + 2);
    }

    // 選択した1つのパーツの、右下の大きさ変更つまみの矩形（画面）
    private RectangleF HandleRect(int index)
    {
        var p = parts[index];
        var r = PartScreenRect(p, p.offsetX, p.offsetY);
        const float s = 10f;
        return new RectangleF(r.Right - s / 2f, r.Bottom - s / 2f, s, s);
    }

    // ==== 掴む判定 ====

    // 画面座標ptの位置にあるパーツを探し、そのインデックスを返す（見つからなければ-1）。
    // zOrderが大きい（手前に描かれている）パーツから優先的に判定することで、パーツ同士が重なっている
    // 場合でも「見た目上、一番手前にあるもの」がクリックされたと判定されるようにしている。
    private int FindPartAt(Point pt)
    {
        var order = Enumerable.Range(0, parts.Count).OrderByDescending(i => parts[i].zOrder).ThenByDescending(i => i).ToList();
        foreach (int i in order)
        {
            var p = parts[i];
            // クリック判定の矩形は、必ず描画と同じ関数から求める
            // （別々に書くと「見えている場所と掴める場所が違う」というズレが生まれるため）。
            var rect = PartScreenRect(p, p.offsetX, p.offsetY);
            rect.Inflate(2, 2);
            if (rect.Contains(pt)) return i;
        }
        return -1;
    }

    // ==== マウス操作 ====

    // キャンバス上でマウスボタンが押された時の処理。
    //   中ボタン／Space＋左ボタン … 表示の移動（パン）
    //   つまみ                    … 大きさの変更
    //   パーツ                    … 選択して、そのまま移動できる。Ctrl/Shift＋クリックで選択に加える／外す
    //   何もない所                … 範囲選択（Ctrl/Shift で既存の選択に追加）
    private void PnlComposer_MouseDown(object? sender, MouseEventArgs e)
    {
        pnlComposer.Focus();
        if (e.Button == MouseButtons.Middle || (e.Button == MouseButtons.Left && _spaceDown))
        {
            _drag = DragMode.Pan; _dragStartScreen = e.Location; _panStart = _pan;
            pnlComposer.Cursor = Cursors.SizeAll;
            return;
        }
        int hit = FindPartAt(e.Location);
        if (e.Button == MouseButtons.Right)
        {
            // 右クリック：まだ選んでいないパーツなら、そのパーツだけを選んでからメニューを開く（コンテキストメニュー側が開く）
            if (hit >= 0 && !_selected.Contains(hit)) SelectOnly(hit);
            return;
        }
        if (e.Button != MouseButtons.Left || _isPlaying) return; // 再生中は「今どこにいるか」が動的に変わるため、配置換えは停止してから行う

        bool additive = (Control.ModifierKeys & (Keys.Control | Keys.Shift)) != 0;

        // つまみ（1つだけ選んでいるとき）
        if (_selected.Count == 1 && selectedIndex >= 0 && HandleRect(selectedIndex).Contains(e.Location))
        {
            var p = parts[selectedIndex];
            _drag = DragMode.Resize; _dragPrimary = selectedIndex;
            _dragStartScreen = e.Location; _dragStartWorld = S2W(e.Location); _dragChanged = false;
            var s = EffectivePartSize(p);
            _resizeStartSize = new SizeF(s.w, s.h);
            // 幅・高さが0（＝画像の原寸）のままだと比率で変えられないので、ここで実寸の数値にしておく
            var thumb = GetPartThumb(p);
            if (p.width <= 0) p.width = thumb?.Width ?? 24;
            if (p.height <= 0) p.height = thumb?.Height ?? 24;
            return;
        }

        if (hit >= 0)
        {
            if (additive)
            {
                // Ctrl/Shift＋クリック：選択に加える／すでに選んでいれば外す
                var set = new HashSet<int>(_selected);
                if (!set.Add(hit)) set.Remove(hit);
                SetSelection(set, set.Contains(hit) ? hit : (set.Count > 0 ? set.Min() : -1));
                return;
            }
            // すでに選んでいる物を掴んだときは選択をそのまま（まとめて動かせる）。そうでなければ、その1つだけを選ぶ。
            if (!_selected.Contains(hit)) SelectOnly(hit);
            else if (selectedIndex != hit) { selectedIndex = hit; LoadDetailFromSelection(); pnlComposer.Invalidate(); }
            _drag = DragMode.Move; _dragPrimary = hit;
            _dragStartScreen = e.Location; _dragStartWorld = S2W(e.Location); _dragChanged = false;
            _dragStartOffsets = _selected.ToDictionary(i => i, i => new PointF(parts[i].offsetX, parts[i].offsetY));
            return;
        }

        // 何もない所：範囲選択を始める（修飾キー無しなら、いまの選択は外す）
        _drag = DragMode.Rubber; _dragStartScreen = e.Location; _rubber = new Rectangle(e.Location, Size.Empty);
        _rubberAdditive = additive;
        _rubberBase = additive ? new HashSet<int>(_selected) : new HashSet<int>();
        if (!additive && _selected.Count > 0) SetSelection(Array.Empty<int>(), -1);
    }

    // マウスが動いている間の処理。ドラッグ中はその種類に応じて値を更新し、そうでなければ掴める物にカーソルを合わせる。
    private void PnlComposer_MouseMove(object? sender, MouseEventArgs e)
    {
        var w = S2W(e.Location);
        if (_lblCoords != null!) _lblCoords.Text = $"X {w.X:0.#}  Y {w.Y:0.#}　{_zoom * 100f:0}%";

        switch (_drag)
        {
            case DragMode.Pan:
                _pan = new PointF(_panStart.X + (e.X - _dragStartScreen.X), _panStart.Y + (e.Y - _dragStartScreen.Y));
                pnlComposer.Invalidate();
                return;

            case DragMode.Move:
            {
                // ワールド座標での移動量。ドラッグ開始時点のoffset値に加算する（開始位置からの差分方式にすることで、
                // ドラッグ中に微小なずれが累積することなく正確に追従する）。
                float dx = w.X - _dragStartWorld.X, dy = w.Y - _dragStartWorld.Y;
                if (!_dragStartOffsets.TryGetValue(_dragPrimary, out var prim)) return;
                // グリッドへの吸着は、掴んでいるパーツの位置で決め、他の選択中パーツも同じ量だけ動かす（相対位置を保つ）
                float snappedX = Snap(prim.X + dx), snappedY = Snap(prim.Y + dy);
                float ddx = snappedX - prim.X, ddy = snappedY - prim.Y;
                foreach (var kv in _dragStartOffsets)
                {
                    parts[kv.Key].offsetX = kv.Value.X + ddx;
                    parts[kv.Key].offsetY = kv.Value.Y + ddy;
                }
                _dragChanged = _dragChanged || Math.Abs(ddx) > 0.001f || Math.Abs(ddy) > 0.001f;
                LoadDetailFromSelection();
                pnlComposer.Invalidate();
                return;
            }

            case DragMode.Resize:
            {
                var p = parts[_dragPrimary];
                float newW = Math.Max(_resizeStartSize.Width + (w.X - _dragStartWorld.X), 2f);
                float newH = Math.Max(_resizeStartSize.Height + (w.Y - _dragStartWorld.Y), 2f);
                if ((Control.ModifierKeys & Keys.Shift) == 0)
                {
                    // 既定は縦横比を保つ（Shiftを押している間は自由に変える）。大きく動かしたほうの方向を基準にする。
                    float rW = newW / _resizeStartSize.Width, rH = newH / _resizeStartSize.Height;
                    float f = Math.Abs(rW - 1f) >= Math.Abs(rH - 1f) ? rW : rH;
                    newW = Math.Max(_resizeStartSize.Width * f, 2f);
                    newH = Math.Max(_resizeStartSize.Height * f, 2f);
                    float sw = Snap(newW);
                    if (sw > 0f && Math.Abs(sw - newW) > 0.001f) { f = sw / _resizeStartSize.Width; newW = sw; newH = Math.Max(_resizeStartSize.Height * f, 2f); }
                }
                else { newW = Math.Max(Snap(newW), 2f); newH = Math.Max(Snap(newH), 2f); }
                // 表示サイズ = 論理サイズ × scale なので、論理サイズへ戻して整数で持つ
                p.width = Math.Max(1, (int)MathF.Round(newW / p.scale));
                p.height = Math.Max(1, (int)MathF.Round(newH / p.scale));
                _dragChanged = true;
                LoadDetailFromSelection();
                pnlComposer.Invalidate();
                return;
            }

            case DragMode.Rubber:
            {
                _rubber = new Rectangle(Math.Min(_dragStartScreen.X, e.X), Math.Min(_dragStartScreen.Y, e.Y),
                                        Math.Abs(e.X - _dragStartScreen.X), Math.Abs(e.Y - _dragStartScreen.Y));
                // ドラッグ中も、囲んだパーツをその場で選択状態にして見せる
                var inside = new HashSet<int>(_rubberBase);
                for (int i = 0; i < parts.Count; i++)
                    if (PartScreenRect(parts[i], parts[i].offsetX, parts[i].offsetY).IntersectsWith(_rubber)) inside.Add(i);
                _selected.Clear();
                foreach (int i in inside) _selected.Add(i);
                selectedIndex = _selected.Count > 0 ? _selected.Min() : -1;
                pnlComposer.Invalidate();
                return;
            }
        }

        // ドラッグしていないとき：乗っているパーツ・つまみを調べて、カーソルと強調表示を変える
        int hover = _isPlaying ? -1 : FindPartAt(e.Location);
        bool onHandle = !_isPlaying && _selected.Count == 1 && selectedIndex >= 0 && HandleRect(selectedIndex).Contains(e.Location);
        if (hover != _hoverIndex || onHandle != _hoverHandle)
        {
            _hoverIndex = hover; _hoverHandle = onHandle;
            pnlComposer.Invalidate();
        }
        if (!_spaceDown) pnlComposer.Cursor = onHandle ? Cursors.SizeNWSE : (hover >= 0 ? Cursors.SizeAll : Cursors.Default);
    }

    // マウスを離した時点で、ドラッグの種類に応じて確定する。
    // 移動・大きさ変更は、ドラッグ中の1フレームごとに記録すると履歴が大量に積まれてしまうため、確定時に1件だけ記録する。
    private void PnlComposer_MouseUp(object? sender, MouseEventArgs e)
    {
        var mode = _drag;
        _drag = DragMode.None;
        if (mode == DragMode.Pan) { pnlComposer.Cursor = _spaceDown ? Cursors.Hand : Cursors.Default; return; }
        if (mode == DragMode.Rubber)
        {
            // 範囲選択の結果を、一覧・詳細にも反映する
            SetSelection(_selected.ToList(), selectedIndex);
            return;
        }
        if ((mode == DragMode.Move || mode == DragMode.Resize) && _dragChanged)
        {
            PushHistory();
            if (mode == DragMode.Resize) Toast($"大きさを {EffectivePartSize(parts[_dragPrimary]).w:0.#}×{EffectivePartSize(parts[_dragPrimary]).h:0.#} にしました。");
        }
        _dragPrimary = -1;
        RefreshListCells();
        pnlComposer.Invalidate();
    }

    // ==== 下部（状態表示とOK/キャンセル） ====

    private Panel BuildBottomButtons()
    {
        var pnl = new Panel { Dock = DockStyle.Bottom, Height = 46 };
        // WrapContents を切る。既定(true)のままだと、AutoSize の幅が確定する前にOKボタンが2行目へ折り返し、下へはみ出して見えなくなる。
        var flow = new FlowLayoutPanel { Dock = DockStyle.Right, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Padding = new Padding(8), AutoSize = true };
        _btnCancel = new Button { Text = "キャンセル", AutoSize = true, Padding = new Padding(10, 5, 10, 5) };
        _btnCancel.Click += (s, e) => Cancelled?.Invoke(this, EventArgs.Empty);
        // Feature: UI改善（提案書 CUT-3）— パーツIDが重複していると一覧上で区別できなくなるため保存前に警告する。
        _btnOk = new Button { Text = "💾 OK", AutoSize = true, Padding = new Padding(10, 5, 10, 5), BackColor = Color.FromArgb(40, 167, 69), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
        _btnOk.Click += (s, e) =>
        {
            var dupIds = parts.GroupBy(p => p.id).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            if (dupIds.Count > 0)
            {
                string msg = $"パーツIDが重複しています: {string.Join(", ", dupIds)}\n\nこのまま保存しますか？";
                if (MessageBox.Show(msg, "確認", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            }
            ResultParts = parts;
            Saved?.Invoke(this, parts);
        };
        // RightToLeftのFlowLayoutPanelは追加順が右から並ぶため、先にCancelを追加すると右端になる。OKを一番右(先頭追加)にしたいため逆順で追加する。
        flow.Controls.Add(_btnCancel);
        flow.Controls.Add(_btnOk);

        // 左側は状態表示。操作の結果（トースト）と、普段は操作のヒントを出す。右には、マウス位置の座標と倍率。
        _lblCoords = new Label { Dock = DockStyle.Right, AutoSize = false, Width = 190, TextAlign = ContentAlignment.MiddleRight, ForeColor = Color.DimGray, Padding = new Padding(0, 0, 8, 0) };
        _lblStatus = new Label { Dock = DockStyle.Fill, AutoSize = false, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(10, 0, 0, 0), ForeColor = Color.DimGray };

        pnl.Controls.Add(_lblStatus);
        pnl.Controls.Add(_lblCoords);
        pnl.Controls.Add(flow);
        return pnl;
    }

    // UserControlにはOnFormClosedがないため、Dispose(bool)でタイマー/画像を確実に解放する。
    // シェルのGoBack()はページをControlsから外した直後に必ずDispose()を呼ぶため、
    // モーダル表示だった頃のOnFormClosedと同じタイミングで後始末できる。
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _previewTimer?.Stop();
            _previewTimer?.Dispose();
            _highlightTimer?.Stop();
            _highlightTimer?.Dispose();
            _toastTimer?.Stop();
            _toastTimer?.Dispose();
            _bodyAttr?.Dispose();
            baseSprite?.Dispose();
            foreach (var img in _partThumbCache.Values) img?.Dispose();
        }
        base.Dispose(disposing);
    }
}
