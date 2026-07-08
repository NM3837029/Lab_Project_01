using System;
using System.Drawing;
using System.Windows.Forms;
using System.IO;

namespace Lab_Editor;

public class HitboxEditorForm : Form
{
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

    public HitboxEditorForm(string imagePath, int ox, int oy, int w, int h)
    {
        Text = "当たり判定(Hitbox)エディタ";
        Size = new Size(600, 600);
        StartPosition = FormStartPosition.CenterParent;

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

        lblInfo = new Label { Location = new Point(10, 500), Size = new Size(300, 20), Text = "ドラッグで当たり判定を設定してください" };

        btnSave = new Button { Text = "保存", Location = new Point(400, 500), Size = new Size(80, 30) };
        btnSave.Click += (s, e) => {
            HitboxOffsetX = hitboxRect.X;
            HitboxOffsetY = hitboxRect.Y;
            HitboxWidth = hitboxRect.Width;
            HitboxHeight = hitboxRect.Height;
            DialogResult = DialogResult.OK;
            Close();
        };

        btnCancel = new Button { Text = "キャンセル", Location = new Point(490, 500), Size = new Size(80, 30) };
        btnCancel.Click += (s, e) => Close();

        Controls.AddRange(new Control[] { pb, lblInfo, btnSave, btnCancel });
    }

    private Rectangle GetImageDrawRect()
    {
        if (sprite == null) return new Rectangle(0, 0, pb.Width, pb.Height);
        float scale = Math.Min((float)pb.Width / sprite.Width, (float)pb.Height / sprite.Height);
        if (scale > 10) scale = 10; // 小さい画像を拡大しすぎない上限
        // 下限は設けない：ウィンドウより大きい画像は縮小してウィンドウ内に収める
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

    private void Pb_MouseDown(object? sender, MouseEventArgs e)
    {
        if (sprite == null) return;
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
            else if (dragMode == 1) // top-left
            {
                newRect.X += dx; newRect.Y += dy;
                newRect.Width -= dx; newRect.Height -= dy;
            }
            else if (dragMode == 2) // top-right
            {
                newRect.Y += dy;
                newRect.Width += dx; newRect.Height -= dy;
            }
            else if (dragMode == 3) // bottom-left
            {
                newRect.X += dx;
                newRect.Width -= dx; newRect.Height += dy;
            }
            else if (dragMode == 4) // bottom-right
            {
                newRect.Width += dx; newRect.Height += dy;
            }

            if (newRect.Width < 1) newRect.Width = 1;
            if (newRect.Height < 1) newRect.Height = 1;

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

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        base.OnFormClosed(e);
        sprite?.Dispose();
    }
}