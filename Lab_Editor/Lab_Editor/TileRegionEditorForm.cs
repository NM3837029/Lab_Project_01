using System;
using System.Drawing;
using System.Windows.Forms;
using System.IO;

namespace Lab_Editor;

// ======================================================
// TileRegionEditorForm - タイルセット画像の中から「表示に使う矩形範囲」をドラッグで選ぶエディタ
// Feature: タイル表示範囲調整機能
//
// 1枚の大きなタイルセット画像を複数のタイル定義で使い回せるよう、HitboxEditorFormと同じ
// ドラッグ操作（角ハンドル/中央移動、ホイールズーム、Ctrl+Zで1段階戻す）を流用しつつ、
// ・選択範囲は常に画像の実ピクセル範囲内に収まるようクランプする（DxLib側の矩形描画に必要）
// ・選択範囲の外側を暗く塗り、「実際に表示される部分」が視覚的に一目で分かるようにする
// という2点をHitboxEditorFormから変更している。
// ======================================================
public class TileRegionEditorForm : Form
{
    // タイル画像を表示し、その上でドラッグ操作を受け付けるピクチャーボックス
    private PictureBox pb = null!;
    private Button btnSave = null!, btnCancel = null!, btnUseWhole = null!;
    private Label lblInfo = null!;
    // 読み込んだタイルセット画像。ファイルが見つからない場合はnullのままになる
    private Image? sprite;

    // 保存確定後の表示範囲（元画像上でのピクセル座標とサイズ）。呼び出し元はこれをタイル定義に反映する
    public int SrcX { get; private set; }
    public int SrcY { get; private set; }
    public int SrcWidth { get; private set; }
    public int SrcHeight { get; private set; }

    // 現在編集中の選択範囲（元画像上でのピクセル座標系）
    private Rectangle cropRect;
    // マウスドラッグ中かどうか
    private bool isDragging = false;
    // ドラッグ開始時のマウス座標（PictureBox上のスクリーン座標）
    private Point dragStart;
    // ドラッグ開始時点での選択範囲（差分計算の基準として使う）
    private Rectangle dragStartRect;

    // 0: none, 1: top-left, 2: top-right, 3: bottom-left, 4: bottom-right, 5: center
    // 現在ドラッグ中のハンドル種別。0は「ハンドルではない場所」を表す
    private int dragMode = 0;

    // ホイール操作によるズーム倍率（1.0が等倍）
    private float _zoomFactor = 1.0f;
    // Ctrl+Zで1段階だけ戻すために、直前の操作開始前の選択範囲を1件だけ覚えておく
    private Rectangle _previousRect;

    public TileRegionEditorForm(string imagePath, int srcX, int srcY, int srcW, int srcH)
    {
        Text = "🖼 タイルの表示範囲エディタ";
        Size = new Size(600, 620);
        MinimumSize = new Size(600, 620);
        StartPosition = FormStartPosition.CenterParent;
        Font = UiTheme.Base;

        // 画像ファイルが存在すれば読み込む。FileStream経由で読み込むことで、
        // Image.FromFileのようにファイルをロックし続けず、読み込み後すぐにハンドルを解放できるようにしている
        if (File.Exists(imagePath))
        {
            using var fs = new FileStream(imagePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            sprite = Image.FromStream(fs);
        }

        // srcW/srcHが未設定(0以下)なら画像全体を初期選択範囲にする
        if (sprite != null && (srcW <= 0 || srcH <= 0))
        {
            srcX = 0; srcY = 0; srcW = sprite.Width; srcH = sprite.Height;
        }
        // 幅・高さは最低でも1になるよう保証し、0以下による描画エラーを防ぐ
        SrcX = srcX; SrcY = srcY; SrcWidth = Math.Max(1, srcW); SrcHeight = Math.Max(1, srcH);
        cropRect = new Rectangle(SrcX, SrcY, SrcWidth, SrcHeight);
        // 渡された初期値が画像範囲をはみ出している可能性があるため、必ず画像内に収まるよう補正する
        ClampRectToImage(ref cropRect);

        // タイル画像を表示し、ドラッグ操作を受け付けるメイン領域
        pb = new PictureBox
        {
            Location = new Point(10, 10),
            Size = new Size(560, 500),
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.LightGray,
            Cursor = Cursors.Cross
        };
        pb.Paint += Pb_Paint;
        pb.MouseDown += Pb_MouseDown;
        pb.MouseMove += Pb_MouseMove;
        pb.MouseUp += Pb_MouseUp;
        pb.MouseWheel += Pb_MouseWheel;

        // 現在の選択範囲や操作方法を表示する案内ラベル
        lblInfo = new Label
        {
            Location = new Point(10, 518),
            Size = new Size(560, 34),
            Text = sprite == null
                ? "画像が見つかりません。先に画像を選択してください。"
                : $"表示範囲: X={cropRect.X}, Y={cropRect.Y}, W={cropRect.Width}, H={cropRect.Height}（画像全体: {sprite.Width}x{sprite.Height}）\nドラッグで選択、ホイールでズーム、Ctrl+Zで直前の操作を1回戻す",
        };

        // 選択範囲を画像全体にリセットするボタン
        btnUseWhole = new Button { Text = "画像全体を使う", Location = new Point(190, 560), Size = new Size(120, 30) };
        btnUseWhole.Click += (s, e) =>
        {
            if (sprite == null) return;
            // Ctrl+Zで戻せるよう、変更前の状態を退避してから範囲を画像全体に置き換える
            _previousRect = cropRect;
            cropRect = new Rectangle(0, 0, sprite.Width, sprite.Height);
            UpdateInfoLabel();
            pb.Invalidate();
        };

        // 現在の選択範囲を確定して閉じるボタン
        btnSave = new Button { Text = "保存", Location = new Point(400, 560), Size = new Size(80, 30) };
        btnSave.Click += (s, e) =>
        {
            SrcX = cropRect.X; SrcY = cropRect.Y; SrcWidth = cropRect.Width; SrcHeight = cropRect.Height;
            DialogResult = DialogResult.OK;
            Close();
        };

        // 変更を破棄して閉じるボタン
        btnCancel = new Button { Text = "キャンセル", Location = new Point(490, 560), Size = new Size(80, 30) };
        btnCancel.Click += (s, e) => Close();

        Controls.AddRange(new Control[] { pb, lblInfo, btnUseWhole, btnSave, btnCancel });
    }

    // 矩形が画像の実ピクセル範囲をはみ出さないよう座標・サイズを補正する。
    // DxLib側の描画処理は画像範囲外の矩形を想定していないため、常にこの範囲内に収める必要がある。
    private void ClampRectToImage(ref Rectangle r)
    {
        if (sprite == null) return;
        r.X = Math.Clamp(r.X, 0, Math.Max(0, sprite.Width - 1));
        r.Y = Math.Clamp(r.Y, 0, Math.Max(0, sprite.Height - 1));
        r.Width = Math.Clamp(r.Width, 1, sprite.Width - r.X);
        r.Height = Math.Clamp(r.Height, 1, sprite.Height - r.Y);
    }

    // 案内ラベルの文言を、現在の選択範囲・画像サイズに合わせて更新する。
    private void UpdateInfoLabel()
    {
        if (sprite == null) return;
        lblInfo.Text = $"表示範囲: X={cropRect.X}, Y={cropRect.Y}, W={cropRect.Width}, H={cropRect.Height}（画像全体: {sprite.Width}x{sprite.Height}）\nドラッグで選択、ホイールでズーム、Ctrl+Zで直前の操作を1回戻す";
    }

    // PictureBox内で画像を実際に描画する矩形（拡大縮小率・中央寄せ後の位置とサイズ）を計算する。
    // ウィンドウより大きい画像は自動的に縮小し、逆に小さい画像は最大10倍まで拡大したうえで、
    // さらにホイールズーム倍率（_zoomFactor）を掛け合わせる。
    private Rectangle GetImageDrawRect()
    {
        if (sprite == null) return new Rectangle(0, 0, pb.Width, pb.Height);
        float scale = Math.Min((float)pb.Width / sprite.Width, (float)pb.Height / sprite.Height);
        if (scale > 10) scale = 10;
        scale *= _zoomFactor;
        int drawW = (int)(sprite.Width * scale);
        int drawH = (int)(sprite.Height * scale);
        int drawX = (pb.Width - drawW) / 2;
        int drawY = (pb.Height - drawH) / 2;
        return new Rectangle(drawX, drawY, drawW, drawH);
    }

    // PictureBoxの再描画のたびに呼ばれる、画像本体と選択範囲のハイライト表示を行う処理。
    private void Pb_Paint(object? sender, PaintEventArgs e)
    {
        // ドット絵をぼかさず、くっきりと拡大表示するための設定
        e.Graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
        e.Graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;

        var drawRect = GetImageDrawRect();
        if (sprite == null) return;

        e.Graphics.DrawImage(sprite, drawRect);

        // 元画像上の選択範囲（cropRect）を、現在の描画スケールに合わせて画面上の座標に変換する
        float scale = (float)drawRect.Width / sprite.Width;
        int cx = drawRect.X + (int)(cropRect.X * scale);
        int cy = drawRect.Y + (int)(cropRect.Y * scale);
        int cw = (int)(cropRect.Width * scale);
        int ch = (int)(cropRect.Height * scale);

        // 選択範囲の「外側」を暗くし、実際に表示される部分がひと目で分かるようにする
        using (var dim = new SolidBrush(Color.FromArgb(150, 0, 0, 0)))
        {
            e.Graphics.FillRectangle(dim, drawRect.X, drawRect.Y, drawRect.Width, cy - drawRect.Y); // 上
            e.Graphics.FillRectangle(dim, drawRect.X, cy + ch, drawRect.Width, drawRect.Bottom - (cy + ch)); // 下
            e.Graphics.FillRectangle(dim, drawRect.X, cy, cx - drawRect.X, ch); // 左
            e.Graphics.FillRectangle(dim, cx + cw, cy, drawRect.Right - (cx + cw), ch); // 右
        }

        // 選択範囲の枠線（緑）と、四隅のドラッグ用ハンドル（白い四角）を描画する
        using var pen = new Pen(Color.Lime, 2f);
        e.Graphics.DrawRectangle(pen, cx, cy, cw, ch);
        e.Graphics.FillRectangle(Brushes.White, cx - 3, cy - 3, 6, 6);
        e.Graphics.FillRectangle(Brushes.White, cx + cw - 3, cy - 3, 6, 6);
        e.Graphics.FillRectangle(Brushes.White, cx - 3, cy + ch - 3, 6, 6);
        e.Graphics.FillRectangle(Brushes.White, cx + cw - 3, cy + ch - 3, 6, 6);
    }

    // マウス座標(cx, cy)がどのハンドル（四隅または中央）の上にあるかを判定する。
    // 戻り値はdragModeと同じ意味の数値（0:なし,1:左上,2:右上,3:左下,4:右下,5:中央=移動）。
    private int GetHandleUnderCursor(int cx, int cy)
    {
        if (sprite == null) return 0;
        var drawRect = GetImageDrawRect();
        float scale = (float)drawRect.Width / sprite.Width;
        int hx = drawRect.X + (int)(cropRect.X * scale);
        int hy = drawRect.Y + (int)(cropRect.Y * scale);
        int hw = (int)(cropRect.Width * scale);
        int hh = (int)(cropRect.Height * scale);

        // 各ハンドルの当たり判定は中心から±5px。優先順位は四隅→中央の順にチェックする
        if (Math.Abs(cx - hx) <= 5 && Math.Abs(cy - hy) <= 5) return 1;
        if (Math.Abs(cx - (hx + hw)) <= 5 && Math.Abs(cy - hy) <= 5) return 2;
        if (Math.Abs(cx - hx) <= 5 && Math.Abs(cy - (hy + hh)) <= 5) return 3;
        if (Math.Abs(cx - (hx + hw)) <= 5 && Math.Abs(cy - (hy + hh)) <= 5) return 4;
        if (cx >= hx && cx <= hx + hw && cy >= hy && cy <= hy + hh) return 5;
        return 0;
    }

    // マウスホイール操作でズーム倍率を変更する（1段階あたり0.25倍、範囲は1.0〜8.0倍に制限）。
    private void Pb_MouseWheel(object? sender, MouseEventArgs e)
    {
        if (sprite == null) return;
        _zoomFactor += e.Delta > 0 ? 0.25f : -0.25f;
        _zoomFactor = Math.Clamp(_zoomFactor, 1.0f, 8.0f);
        pb.Invalidate();
    }

    // マウスボタンが押された瞬間の処理。ハンドル上ならそのハンドルでのドラッグを開始し、
    // ハンドル外（画像の空いている場所）をクリックした場合はその位置から新しい1x1の選択範囲を作り始める。
    private void Pb_MouseDown(object? sender, MouseEventArgs e)
    {
        if (sprite == null) return;
        // Ctrl+Zで1段階戻せるよう、この操作を始める直前の選択範囲を退避しておく
        _previousRect = cropRect;
        dragMode = GetHandleUnderCursor(e.X, e.Y);
        if (dragMode == 0)
        {
            // ハンドル以外の場所（画像内）をクリックした場合：クリック位置を起点に新規選択範囲を開始する
            var drawRect = GetImageDrawRect();
            float scale = (float)drawRect.Width / sprite.Width;
            int sx = (int)((e.X - drawRect.X) / scale);
            int sy = (int)((e.Y - drawRect.Y) / scale);
            if (sx >= 0 && sx < sprite.Width && sy >= 0 && sy < sprite.Height)
            {
                cropRect = new Rectangle(sx, sy, 1, 1);
                // 右下ハンドルをドラッグしている状態として扱うことで、そのままマウス移動でサイズを広げられるようにする
                dragMode = 4;
            }
        }
        isDragging = true;
        dragStart = e.Location;
        dragStartRect = cropRect;
    }

    // マウス移動時の処理。ドラッグ中でなければカーソル形状をハンドルの種類に応じて変えるだけ、
    // ドラッグ中であれば移動量に応じて選択範囲（cropRect）を再計算する。
    private void Pb_MouseMove(object? sender, MouseEventArgs e)
    {
        if (sprite == null) return;

        // ハンドルの種類に応じてマウスカーソルの形状を変え、どの操作ができるかを視覚的に示す
        int handle = GetHandleUnderCursor(e.X, e.Y);
        if (handle == 1 || handle == 4) pb.Cursor = Cursors.SizeNWSE;
        else if (handle == 2 || handle == 3) pb.Cursor = Cursors.SizeNESW;
        else if (handle == 5) pb.Cursor = Cursors.SizeAll;
        else pb.Cursor = Cursors.Cross;

        if (isDragging)
        {
            var drawRect = GetImageDrawRect();
            float scale = (float)drawRect.Width / sprite.Width;

            // 画面上のドラッグ移動量を、画像本来のピクセル単位の移動量に変換する
            int dx = (int)((e.X - dragStart.X) / scale);
            int dy = (int)((e.Y - dragStart.Y) / scale);

            var newRect = dragStartRect;

            if (dragMode == 5) // 移動
            {
                // 中央（全体移動）の場合は単純にX/Yをずらし、画像範囲外に出ないようクランプする
                newRect.X = dragStartRect.X + dx;
                newRect.Y = dragStartRect.Y + dy;
                newRect.X = Math.Clamp(newRect.X, 0, Math.Max(0, sprite.Width - dragStartRect.Width));
                newRect.Y = Math.Clamp(newRect.Y, 0, Math.Max(0, sprite.Height - dragStartRect.Height));
            }
            else if (dragMode != 0)
            {
                // HitboxEditorFormと同じ「反対側の角(anchor)を固定してMin/Maxで再構成する」方式（角の追い越しでズレない）。
                // ドラッグしている角の反対側（anchor）を固定点とし、操作中の角（free）の移動先座標を求めたうえで、
                // それら2点のMin/Maxから矩形を再構成することで、ドラッグで反対側の角を追い越しても破綻しない。
                int anchorX, anchorY, freeX, freeY;
                switch (dragMode)
                {
                    case 1:
                        // 左上をドラッグ中 → 固定点は右下
                        anchorX = dragStartRect.Right; anchorY = dragStartRect.Bottom;
                        freeX = dragStartRect.X + dx; freeY = dragStartRect.Y + dy;
                        break;
                    case 2:
                        // 右上をドラッグ中 → 固定点は左下
                        anchorX = dragStartRect.X; anchorY = dragStartRect.Bottom;
                        freeX = dragStartRect.Right + dx; freeY = dragStartRect.Y + dy;
                        break;
                    case 3:
                        // 左下をドラッグ中 → 固定点は右上
                        anchorX = dragStartRect.Right; anchorY = dragStartRect.Y;
                        freeX = dragStartRect.X + dx; freeY = dragStartRect.Bottom + dy;
                        break;
                    default:
                        // 右下をドラッグ中 → 固定点は左上
                        anchorX = dragStartRect.X; anchorY = dragStartRect.Y;
                        freeX = dragStartRect.Right + dx; freeY = dragStartRect.Bottom + dy;
                        break;
                }
                newRect.X = Math.Min(anchorX, freeX);
                newRect.Y = Math.Min(anchorY, freeY);
                newRect.Width = Math.Max(1, Math.Abs(freeX - anchorX));
                newRect.Height = Math.Max(1, Math.Abs(freeY - anchorY));
                // リサイズ後の矩形も念のため画像範囲内にクランプしておく
                ClampRectToImage(ref newRect);
            }

            cropRect = newRect;
            UpdateInfoLabel();
            pb.Invalidate();
        }
    }

    // マウスボタンが離されたらドラッグ状態を終了する。
    private void Pb_MouseUp(object? sender, MouseEventArgs e)
    {
        isDragging = false;
        dragMode = 0;
    }

    // フォーム全体でCtrl+Zを受け付け、直前の操作前の選択範囲へ1段階だけ巻き戻す。
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.Z))
        {
            cropRect = _previousRect;
            UpdateInfoLabel();
            pb.Invalidate();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    // フォームが閉じられたタイミングで、読み込んだ画像リソースを確実に解放する（メモリリーク防止）。
    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        base.OnFormClosed(e);
        sprite?.Dispose();
    }
}
