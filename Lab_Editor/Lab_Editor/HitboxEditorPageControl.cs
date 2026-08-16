using System;
using System.Drawing;
using System.Windows.Forms;
using System.IO;

namespace Lab_Editor;

public class HitboxEditorPageControl : UserControl
{
    public event EventHandler? Saved;
    public event EventHandler? Cancelled;
    public Button PrimaryActionButton => btnSave;
    public Button SecondaryActionButton => btnCancel;

    private PictureBox pb = null!;
    private Button btnSave = null!, btnCancel = null!;
    private Label lblInfo = null!;
    private Image? sprite;
    
    public int HitboxOffsetX { get; private set; }
    public int HitboxOffsetY { get; private set; }
    public int HitboxWidth { get; private set; }
    public int HitboxHeight { get; private set; }

    private Rectangle hitboxRect;
    private bool isDragging = false;
    private Point dragStart;
    private Rectangle dragStartRect;

    // 0: none, 1: top-left, 2: top-right, 3: bottom-left, 4: bottom-right, 5: center
    private int dragMode = 0;
    private const int HANDLE_SIZE = 6;

    // Feature: UI改善（提案書 HB-2）— 小さいスプライトを1px単位で調整しやすいよう、ホイールでズームできるようにする
    private float _zoomFactor = 1.0f;
    // Feature: UI改善（提案書 HB-3）— ドラッグ操作を1回分だけCtrl+Zで戻せるようにする
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
            // Size the PictureBox to the image size if possible, or zoom. 
            // Better to show it at 1:1 scale for pixel art!
            // But pixel art is usually small (e.g. 32x32), so we should scale it up by e.g. 4x or 8x.
        }

        pb.Paint += Pb_Paint;
        pb.MouseDown += Pb_MouseDown;
        pb.MouseMove += Pb_MouseMove;
        pb.MouseUp += Pb_MouseUp;
        pb.MouseWheel += Pb_MouseWheel;

        lblInfo = new Label { Location = new Point(10, 500), Size = new Size(560, 20), Text = "ドラッグで当たり判定を設定してください（ホイールでズーム、Ctrl+Zで直前の操作を1回戻す）" };

        btnSave = new Button { Text = "保存", Location = new Point(400, 500), Size = new Size(80, 30) };
        btnSave.Click += (s, e) => {
            HitboxOffsetX = hitboxRect.X;
            HitboxOffsetY = hitboxRect.Y;
            HitboxWidth = hitboxRect.Width;
            HitboxHeight = hitboxRect.Height;
            Saved?.Invoke(this, EventArgs.Empty);
        };

        btnCancel = new Button { Text = "キャンセル", Location = new Point(490, 500), Size = new Size(80, 30) };
        btnCancel.Click += (s, e) => Cancelled?.Invoke(this, EventArgs.Empty);

        Controls.AddRange(new Control[] { pb, lblInfo, btnSave, btnCancel });
    }

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

    private void Pb_Paint(object? sender, PaintEventArgs e)
    {
        e.Graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
        e.Graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
        
        var drawRect = GetImageDrawRect();
        
        if (sprite != null)
        {
            e.Graphics.DrawImage(sprite, drawRect);
        }

        // Draw grid or something?
        
        // Draw Hitbox
        if (sprite != null)
        {
            float scale = (float)drawRect.Width / sprite.Width;
            int hx = drawRect.X + (int)(hitboxRect.X * scale);
            int hy = drawRect.Y + (int)(hitboxRect.Y * scale);
            int hw = (int)(hitboxRect.Width * scale);
            int hh = (int)(hitboxRect.Height * scale);

            using var brush = new SolidBrush(Color.FromArgb(120, 255, 0, 0));
            e.Graphics.FillRectangle(brush, hx, hy, hw, hh);
            e.Graphics.DrawRectangle(Pens.Red, hx, hy, hw, hh);

            // Draw handles
            e.Graphics.FillRectangle(Brushes.White, hx - 3, hy - 3, 6, 6);
            e.Graphics.FillRectangle(Brushes.White, hx + hw - 3, hy - 3, 6, 6);
            e.Graphics.FillRectangle(Brushes.White, hx - 3, hy + hh - 3, 6, 6);
            e.Graphics.FillRectangle(Brushes.White, hx + hw - 3, hy + hh - 3, 6, 6);
        }
    }

    private int GetHandleUnderCursor(int cx, int cy)
    {
        if (sprite == null) return 0;
        var drawRect = GetImageDrawRect();
        float scale = (float)drawRect.Width / sprite.Width;
        int hx = drawRect.X + (int)(hitboxRect.X * scale);
        int hy = drawRect.Y + (int)(hitboxRect.Y * scale);
        int hw = (int)(hitboxRect.Width * scale);
        int hh = (int)(hitboxRect.Height * scale);

        if (Math.Abs(cx - hx) <= 5 && Math.Abs(cy - hy) <= 5) return 1;
        if (Math.Abs(cx - (hx+hw)) <= 5 && Math.Abs(cy - hy) <= 5) return 2;
        if (Math.Abs(cx - hx) <= 5 && Math.Abs(cy - (hy+hh)) <= 5) return 3;
        if (Math.Abs(cx - (hx+hw)) <= 5 && Math.Abs(cy - (hy+hh)) <= 5) return 4;
        if (cx >= hx && cx <= hx+hw && cy >= hy && cy <= hy+hh) return 5;
        return 0;
    }

    private void Pb_MouseWheel(object? sender, MouseEventArgs e)
    {
        if (sprite == null) return;
        _zoomFactor += e.Delta > 0 ? 0.25f : -0.25f;
        _zoomFactor = Math.Clamp(_zoomFactor, 1.0f, 8.0f);
        pb.Invalidate();
    }

    private void Pb_MouseDown(object? sender, MouseEventArgs e)
    {
        if (sprite == null) return;
        _previousRect = hitboxRect; // Feature: UI改善（提案書 HB-3）— この操作を始める直前の状態を1件だけ覚えておく
        dragMode = GetHandleUnderCursor(e.X, e.Y);
        if (dragMode == 0)
        {
            // If clicking outside, maybe start a new rect?
            var drawRect = GetImageDrawRect();
            float scale = (float)drawRect.Width / sprite.Width;
            int sx = (int)((e.X - drawRect.X) / scale);
            int sy = (int)((e.Y - drawRect.Y) / scale);
            if (sx >= 0 && sx < sprite.Width && sy >= 0 && sy < sprite.Height)
            {
                hitboxRect = new Rectangle(sx, sy, 1, 1);
                dragMode = 4; // drag bottom right
            }
        }
        isDragging = true;
        dragStart = e.Location;
        dragStartRect = hitboxRect;
    }

    private void Pb_MouseMove(object? sender, MouseEventArgs e)
    {
        if (sprite == null) return;
        
        int handle = GetHandleUnderCursor(e.X, e.Y);
        if (handle == 1 || handle == 4) pb.Cursor = Cursors.SizeNWSE;
        else if (handle == 2 || handle == 3) pb.Cursor = Cursors.SizeNESW;
        else if (handle == 5) pb.Cursor = Cursors.SizeAll;
        else pb.Cursor = Cursors.Cross;

        if (isDragging)
        {
            var drawRect = GetImageDrawRect();
            float scale = (float)drawRect.Width / sprite.Width;
            
            int dx = (int)((e.X - dragStart.X) / scale);
            int dy = (int)((e.Y - dragStart.Y) / scale);

            var newRect = dragStartRect;

            if (dragMode == 5) // move
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

    private void Pb_MouseUp(object? sender, MouseEventArgs e)
    {
        isDragging = false;
        dragMode = 0;
    }

    // Feature: UI改善（提案書 HB-3）
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

    protected override void Dispose(bool disposing)
    {
        if (disposing) sprite?.Dispose();
        base.Dispose(disposing);
    }
}