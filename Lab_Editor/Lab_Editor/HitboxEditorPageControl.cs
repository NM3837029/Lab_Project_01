using System;
using System.Drawing;
using System.Windows.Forms;
using System.IO;

namespace Lab_Editor;

// ======================================================
// HitboxEditorPageControl - スプライト画像上で当たり判定（ヒットボックス）の矩形範囲を
// ドラッグ操作で設定するためのUIコントロール。
// タイル表示範囲エディタ（TileRegionEditorForm）とほぼ同じドラッグ操作の仕組みを持つが、
// こちらは「実際にゲーム内で当たり判定として使う矩形」を編集する点が異なる。
// ======================================================
public class HitboxEditorPageControl : UserControl
{
    // 保存ボタンが押されたときに呼び出し元へ通知するイベント
    public event EventHandler? Saved;
    // キャンセルボタンが押されたときに呼び出し元へ通知するイベント
    public event EventHandler? Cancelled;
    // 呼び出し元の画面（ページ切り替えを行うホスト側）が、共通の配置で
    // 保存/キャンセルボタンを扱えるように公開しているプロパティ
    public Button PrimaryActionButton => btnSave;
    public Button SecondaryActionButton => btnCancel;

    // スプライト画像を表示し、その上でドラッグ操作を受け付けるピクチャーボックス
    private PictureBox pb = null!;
    private Button btnSave = null!, btnCancel = null!;
    private Label lblInfo = null!;
    // 読み込んだスプライト画像。ファイルが見つからない場合はnullのままになる
    private Image? sprite;

    // 保存確定後の当たり判定（元画像上でのオフセット座標とサイズ）。呼び出し元はこれを敵/アイテム定義に反映する
    public int HitboxOffsetX { get; private set; }
    public int HitboxOffsetY { get; private set; }
    public int HitboxWidth { get; private set; }
    public int HitboxHeight { get; private set; }

    // 現在編集中の当たり判定矩形（元画像上でのピクセル座標系）
    private Rectangle hitboxRect;
    // マウスドラッグ中かどうか
    private bool isDragging = false;
    // ドラッグ開始時のマウス座標（PictureBox上のスクリーン座標）
    private Point dragStart;
    // ドラッグ開始時点での当たり判定矩形（差分計算の基準として使う）
    private Rectangle dragStartRect;

    // 0: none, 1: top-left, 2: top-right, 3: bottom-left, 4: bottom-right, 5: center
    // 現在ドラッグ中のハンドル種別。0は「ハンドルではない場所」を表す
    private int dragMode = 0;
    // 四隅ハンドルの表示サイズ（ピクセル）
    private const int HANDLE_SIZE = 6;

    // Feature: UI改善（提案書 HB-2）— 小さいスプライトを1px単位で調整しやすいよう、ホイールでズームできるようにする
    // ホイール操作によるズーム倍率（1.0が等倍）
    private float _zoomFactor = 1.0f;
    // Feature: UI改善（提案書 HB-3）— ドラッグ操作を1回分だけCtrl+Zで戻せるようにする
    // Ctrl+Zで1段階だけ戻すために、直前の操作開始前の当たり判定矩形を1件だけ覚えておく
    private Rectangle _previousRect;

    public HitboxEditorPageControl(string imagePath, int ox, int oy, int w, int h)
    {
        Dock = DockStyle.Fill;
        Font = UiTheme.Base;

        HitboxOffsetX = ox;
        HitboxOffsetY = oy;
        HitboxWidth = w;
        HitboxHeight = h;

        hitboxRect = new Rectangle(ox, oy, w, h);

        // スプライト画像を表示し、ドラッグ操作を受け付けるメイン領域
        pb = new PictureBox
        {
            Location = new Point(10, 10),
            Size = new Size(560, 480),
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.LightGray,
            Cursor = Cursors.Cross
        };

        if (File.Exists(imagePath))
        {
            sprite = Image.FromFile(imagePath);
            // ドット絵は本来「等倍（1:1）」で表示するのが望ましいが、
            // ドット絵素材は通常サイズが小さい（例：32x32など）ため、
            // 見やすくするために後段のGetImageDrawRectで最大10倍程度まで自動的に拡大表示している。
        }

        pb.Paint += Pb_Paint;
        pb.MouseDown += Pb_MouseDown;
        pb.MouseMove += Pb_MouseMove;
        pb.MouseUp += Pb_MouseUp;
        pb.MouseWheel += Pb_MouseWheel;

        // 操作方法を示す案内ラベル
        lblInfo = new Label { Location = new Point(10, 500), Size = new Size(560, 20), Text = "ドラッグで当たり判定を設定してください（ホイールでズーム、Ctrl+Zで直前の操作を1回戻す）" };

        // 現在の当たり判定を確定するボタン
        btnSave = new Button { Text = "保存", Location = new Point(400, 500), Size = new Size(80, 30) };
        btnSave.Click += (s, e) => {
            HitboxOffsetX = hitboxRect.X;
            HitboxOffsetY = hitboxRect.Y;
            HitboxWidth = hitboxRect.Width;
            HitboxHeight = hitboxRect.Height;
            Saved?.Invoke(this, EventArgs.Empty);
        };

        // 変更を破棄して呼び出し元へ戻るボタン
        btnCancel = new Button { Text = "キャンセル", Location = new Point(490, 500), Size = new Size(80, 30) };
        btnCancel.Click += (s, e) => Cancelled?.Invoke(this, EventArgs.Empty);

        Controls.AddRange(new Control[] { pb, lblInfo, btnSave, btnCancel });
    }

    // PictureBox内で画像を実際に描画する矩形（拡大縮小率・中央寄せ後の位置とサイズ）を計算する。
    // ウィンドウより大きい画像は自動的に縮小し、逆に小さい画像は拡大したうえで、
    // さらにホイールズーム倍率（_zoomFactor）を掛け合わせる。
    private Rectangle GetImageDrawRect()
    {
        if (sprite == null) return new Rectangle(0, 0, pb.Width, pb.Height);
        float scale = Math.Min((float)pb.Width / sprite.Width, (float)pb.Height / sprite.Height);
        if (scale > 10) scale = 10; // 小さい画像を拡大しすぎない上限
        // 下限は設けない：ウィンドウより大きい画像は縮小してウィンドウ内に収める
        scale *= _zoomFactor; // Feature: UI改善（提案書 HB-2）— ホイールズームを反映する
        int drawW = (int)(sprite.Width * scale);
        int drawH = (int)(sprite.Height * scale);
        int drawX = (pb.Width - drawW) / 2;
        int drawY = (pb.Height - drawH) / 2;
        return new Rectangle(drawX, drawY, drawW, drawH);
    }

    // PictureBoxの再描画のたびに呼ばれる、スプライト本体と当たり判定のハイライト表示を行う処理。
    private void Pb_Paint(object? sender, PaintEventArgs e)
    {
        // ドット絵をぼかさず、くっきりと拡大表示するための設定
        e.Graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
        e.Graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;

        var drawRect = GetImageDrawRect();

        if (sprite != null)
        {
            e.Graphics.DrawImage(sprite, drawRect);
        }

        // （グリッド線などの追加描画が必要になった場合はここに実装する余地がある。現状は未実装）

        // 当たり判定の矩形を描画する
        if (sprite != null)
        {
            // 元画像上の当たり判定（hitboxRect）を、現在の描画スケールに合わせて画面上の座標に変換する
            float scale = (float)drawRect.Width / sprite.Width;
            int hx = drawRect.X + (int)(hitboxRect.X * scale);
            int hy = drawRect.Y + (int)(hitboxRect.Y * scale);
            int hw = (int)(hitboxRect.Width * scale);
            int hh = (int)(hitboxRect.Height * scale);

            // 半透明の赤で塗りつぶし、赤い枠線で囲んで当たり判定範囲を強調表示する
            using var brush = new SolidBrush(Color.FromArgb(120, 255, 0, 0));
            e.Graphics.FillRectangle(brush, hx, hy, hw, hh);
            e.Graphics.DrawRectangle(Pens.Red, hx, hy, hw, hh);

            // 四隅にドラッグ用の白いハンドル（四角）を描画する
            e.Graphics.FillRectangle(Brushes.White, hx - 3, hy - 3, 6, 6);
            e.Graphics.FillRectangle(Brushes.White, hx + hw - 3, hy - 3, 6, 6);
            e.Graphics.FillRectangle(Brushes.White, hx - 3, hy + hh - 3, 6, 6);
            e.Graphics.FillRectangle(Brushes.White, hx + hw - 3, hy + hh - 3, 6, 6);
        }
    }

    // マウス座標(cx, cy)がどのハンドル（四隅または中央）の上にあるかを判定する。
    // 戻り値はdragModeと同じ意味の数値（0:なし,1:左上,2:右上,3:左下,4:右下,5:中央=移動）。
    private int GetHandleUnderCursor(int cx, int cy)
    {
        if (sprite == null) return 0;
        var drawRect = GetImageDrawRect();
        float scale = (float)drawRect.Width / sprite.Width;
        int hx = drawRect.X + (int)(hitboxRect.X * scale);
        int hy = drawRect.Y + (int)(hitboxRect.Y * scale);
        int hw = (int)(hitboxRect.Width * scale);
        int hh = (int)(hitboxRect.Height * scale);

        // 各ハンドルの当たり判定は中心から±5px。優先順位は四隅→中央の順にチェックする
        if (Math.Abs(cx - hx) <= 5 && Math.Abs(cy - hy) <= 5) return 1;
        if (Math.Abs(cx - (hx+hw)) <= 5 && Math.Abs(cy - hy) <= 5) return 2;
        if (Math.Abs(cx - hx) <= 5 && Math.Abs(cy - (hy+hh)) <= 5) return 3;
        if (Math.Abs(cx - (hx+hw)) <= 5 && Math.Abs(cy - (hy+hh)) <= 5) return 4;
        if (cx >= hx && cx <= hx+hw && cy >= hy && cy <= hy+hh) return 5;
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
    // ハンドル外（画像の空いている場所）をクリックした場合はその位置から新しい1x1の当たり判定を作り始める。
    private void Pb_MouseDown(object? sender, MouseEventArgs e)
    {
        if (sprite == null) return;
        _previousRect = hitboxRect; // Feature: UI改善（提案書 HB-3）— この操作を始める直前の状態を1件だけ覚えておく
        dragMode = GetHandleUnderCursor(e.X, e.Y);
        if (dragMode == 0)
        {
            // ハンドル以外の場所（画像内）をクリックした場合：クリック位置を起点に新しい当たり判定を開始する
            var drawRect = GetImageDrawRect();
            float scale = (float)drawRect.Width / sprite.Width;
            int sx = (int)((e.X - drawRect.X) / scale);
            int sy = (int)((e.Y - drawRect.Y) / scale);
            if (sx >= 0 && sx < sprite.Width && sy >= 0 && sy < sprite.Height)
            {
                hitboxRect = new Rectangle(sx, sy, 1, 1);
                dragMode = 4; // 右下ハンドルをドラッグしている状態として扱い、そのまま拡大操作に移行できるようにする
            }
        }
        isDragging = true;
        dragStart = e.Location;
        dragStartRect = hitboxRect;
    }

    // マウス移動時の処理。ドラッグ中でなければカーソル形状をハンドルの種類に応じて変えるだけ、
    // ドラッグ中であれば移動量に応じて当たり判定矩形（hitboxRect）を再計算する。
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

            if (dragMode == 5) // 中央ハンドル＝矩形全体の移動
            {
                newRect.X += dx;
                newRect.Y += dy;
            }
            else if (dragMode != 0)
            {
                // Feature: 当たり判定エディタの角ドラッグ修正（友人フィードバック対応）
                // 角ドラッグ中に反対側の角を追い越すと、従来はWidth/Heightだけ1にクランプされてX/Yが
                // 再計算されず、固定されるべき反対側の角がズレていた。ここでは「固定されるべき反対側の角(anchor)」と
                // 「操作中の自由な角(free)」を明示的に求め、それらからMin/Maxで矩形を再構成することで、
                // 追い越した場合も反対側の角が常に正しい位置に固定されるようにする。
                int anchorX, anchorY, freeX, freeY;
                switch (dragMode)
                {
                    case 1: // top-left をドラッグ → anchorはbottom-right
                        anchorX = dragStartRect.Right; anchorY = dragStartRect.Bottom;
                        freeX = dragStartRect.X + dx; freeY = dragStartRect.Y + dy;
                        break;
                    case 2: // top-right をドラッグ → anchorはbottom-left
                        anchorX = dragStartRect.X; anchorY = dragStartRect.Bottom;
                        freeX = dragStartRect.Right + dx; freeY = dragStartRect.Y + dy;
                        break;
                    case 3: // bottom-left をドラッグ → anchorはtop-right
                        anchorX = dragStartRect.Right; anchorY = dragStartRect.Y;
                        freeX = dragStartRect.X + dx; freeY = dragStartRect.Bottom + dy;
                        break;
                    default: // 4: bottom-right をドラッグ → anchorはtop-left
                        anchorX = dragStartRect.X; anchorY = dragStartRect.Y;
                        freeX = dragStartRect.Right + dx; freeY = dragStartRect.Bottom + dy;
                        break;
                }
                newRect.X = Math.Min(anchorX, freeX);
                newRect.Y = Math.Min(anchorY, freeY);
                newRect.Width = Math.Max(1, Math.Abs(freeX - anchorX));
                newRect.Height = Math.Max(1, Math.Abs(freeY - anchorY));
            }

            hitboxRect = newRect;
            lblInfo.Text = $"Hitbox: X={hitboxRect.X}, Y={hitboxRect.Y}, W={hitboxRect.Width}, H={hitboxRect.Height}";
            pb.Invalidate();
        }
    }

    // マウスボタンが離されたらドラッグ状態を終了する。
    private void Pb_MouseUp(object? sender, MouseEventArgs e)
    {
        isDragging = false;
        dragMode = 0;
    }

    // Feature: UI改善（提案書 HB-3）
    // フォーム全体でCtrl+Zを受け付け、直前の操作前の当たり判定矩形へ1段階だけ巻き戻す。
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.Z))
        {
            hitboxRect = _previousRect;
            lblInfo.Text = $"直前の操作を戻しました: X={hitboxRect.X}, Y={hitboxRect.Y}, W={hitboxRect.Width}, H={hitboxRect.Height}";
            pb.Invalidate();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    // コントロールが破棄されるタイミングで、読み込んだスプライト画像リソースを確実に解放する（メモリリーク防止）。
    protected override void Dispose(bool disposing)
    {
        if (disposing) sprite?.Dispose();
        base.Dispose(disposing);
    }
}
