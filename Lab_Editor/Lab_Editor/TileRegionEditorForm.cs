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
    private PictureBox pb = null!;
    private Button btnSave = null!, btnCancel = null!, btnUseWhole = null!;
    private Label lblInfo = null!;
    private Image? sprite;

    public int SrcX { get; private set; }
    public int SrcY { get; private set; }
    public int SrcWidth { get; private set; }
    public int SrcHeight { get; private set; }

    private Rectangle cropRect;
    private bool isDragging = false;
    private Point dragStart;
    private Rectangle dragStartRect;

    // 0: none, 1: top-left, 2: top-right, 3: bottom-left, 4: bottom-right, 5: center
    private int dragMode = 0;

    private float _zoomFactor = 1.0f;
    private Rectangle _previousRect;

    public TileRegionEditorForm(string imagePath, int srcX, int srcY, int srcW, int srcH)
    {
        Text = "🖼 タイルの表示範囲エディタ";
        Size = new Size(600, 620);
        MinimumSize = new Size(600, 620);
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Meiryo UI", 9);

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
        SrcX = srcX; SrcY = srcY; SrcWidth = Math.Max(1, srcW); SrcHeight = Math.Max(1, srcH);
        cropRect = new Rectangle(SrcX, SrcY, SrcWidth, SrcHeight);
        ClampRectToImage(ref cropRect);

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

        lblInfo = new Label
        {
            Location = new Point(10, 518),
            Size = new Size(560, 34),
            Text = sprite == null
                ? "画像が見つかりません。先に画像を選択してください。"
                : $"表示範囲: X={cropRect.X}, Y={cropRect.Y}, W={cropRect.Width}, H={cropRect.Height}（画像全体: {sprite.Width}x{sprite.Height}）\nドラッグで選択、ホイールでズーム、Ctrl+Zで直前の操作を1回戻す",
        };

        btnUseWhole = new Button { Text = "画像全体を使う", Location = new Point(190, 560), Size = new Size(120, 30) };
        btnUseWhole.Click += (s, e) =>
        {
            if (sprite == null) return;
            _previousRect = cropRect;
            cropRect = new Rectangle(0, 0, sprite.Width, sprite.Height);
            UpdateInfoLabel();
            pb.Invalidate();
        };

        btnSave = new Button { Text = "保存", Location = new Point(400, 560), Size = new Size(80, 30) };
        btnSave.Click += (s, e) =>
        {
            SrcX = cropRect.X; SrcY = cropRect.Y; SrcWidth = cropRect.Width; SrcHeight = cropRect.Height;
            DialogResult = DialogResult.OK;
            Close();
        };

        btnCancel = new Button { Text = "キャンセル", Location = new Point(490, 560), Size = new Size(80, 30) };
        btnCancel.Click += (s, e) => Close();

        Controls.AddRange(new Control[] { pb, lblInfo, btnUseWhole, btnSave, btnCancel });
    }

    private void ClampRectToImage(ref Rectangle r)
    {
        if (sprite == null) return;
        r.X = Math.Clamp(r.X, 0, Math.Max(0, sprite.Width - 1));
        r.Y = Math.Clamp(r.Y, 0, Math.Max(0, sprite.Height - 1));
        r.Width = Math.Clamp(r.Width, 1, sprite.Width - r.X);
        r.Height = Math.Clamp(r.Height, 1, sprite.Height - r.Y);
    }

    private void UpdateInfoLabel()
    {
        if (sprite == null) return;
        lblInfo.Text = $"表示範囲: X={cropRect.X}, Y={cropRect.Y}, W={cropRect.Width}, H={cropRect.Height}（画像全体: {sprite.Width}x{sprite.Height}）\nドラッグで選択、ホイールでズーム、Ctrl+Zで直前の操作を1回戻す";
    }

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

    private void Pb_Paint(object? sender, PaintEventArgs e)
    {
        e.Graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
        e.Graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;

        var drawRect = GetImageDrawRect();
        if (sprite == null) return;

        e.Graphics.DrawImage(sprite, drawRect);

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

        using var pen = new Pen(Color.Lime, 2f);
        e.Graphics.DrawRectangle(pen, cx, cy, cw, ch);
        e.Graphics.FillRectangle(Brushes.White, cx - 3, cy - 3, 6, 6);
        e.Graphics.FillRectangle(Brushes.White, cx + cw - 3, cy - 3, 6, 6);
        e.Graphics.FillRectangle(Brushes.White, cx - 3, cy + ch - 3, 6, 6);
        e.Graphics.FillRectangle(Brushes.White, cx + cw - 3, cy + ch - 3, 6, 6);
    }

    private int GetHandleUnderCursor(int cx, int cy)
    {
        if (sprite == null) return 0;
        var drawRect = GetImageDrawRect();
        float scale = (float)drawRect.Width / sprite.Width;
        int hx = drawRect.X + (int)(cropRect.X * scale);
        int hy = drawRect.Y + (int)(cropRect.Y * scale);
        int hw = (int)(cropRect.Width * scale);
        int hh = (int)(cropRect.Height * scale);

        if (Math.Abs(cx - hx) <= 5 && Math.Abs(cy - hy) <= 5) return 1;
        if (Math.Abs(cx - (hx + hw)) <= 5 && Math.Abs(cy - hy) <= 5) return 2;
        if (Math.Abs(cx - hx) <= 5 && Math.Abs(cy - (hy + hh)) <= 5) return 3;
        if (Math.Abs(cx - (hx + hw)) <= 5 && Math.Abs(cy - (hy + hh)) <= 5) return 4;
        if (cx >= hx && cx <= hx + hw && cy >= hy && cy <= hy + hh) return 5;
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
        _previousRect = cropRect;
        dragMode = GetHandleUnderCursor(e.X, e.Y);
        if (dragMode == 0)
        {
            var drawRect = GetImageDrawRect();
            float scale = (float)drawRect.Width / sprite.Width;
            int sx = (int)((e.X - drawRect.X) / scale);
            int sy = (int)((e.Y - drawRect.Y) / scale);
            if (sx >= 0 && sx < sprite.Width && sy >= 0 && sy < sprite.Height)
            {
                cropRect = new Rectangle(sx, sy, 1, 1);
                dragMode = 4;
            }
        }
        isDragging = true;
        dragStart = e.Location;
        dragStartRect = cropRect;
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

            if (dragMode == 5) // 移動
            {
                newRect.X = dragStartRect.X + dx;
                newRect.Y = dragStartRect.Y + dy;
                newRect.X = Math.Clamp(newRect.X, 0, Math.Max(0, sprite.Width - dragStartRect.Width));
                newRect.Y = Math.Clamp(newRect.Y, 0, Math.Max(0, sprite.Height - dragStartRect.Height));
            }
            else if (dragMode != 0)
            {
                // HitboxEditorFormと同じ「反対側の角(anchor)を固定してMin/Maxで再構成する」方式（角の追い越しでズレない）
                int anchorX, anchorY, freeX, freeY;
                switch (dragMode)
                {
                    case 1:
                        anchorX = dragStartRect.Right; anchorY = dragStartRect.Bottom;
                        freeX = dragStartRect.X + dx; freeY = dragStartRect.Y + dy;
                        break;
                    case 2:
                        anchorX = dragStartRect.X; anchorY = dragStartRect.Bottom;
                        freeX = dragStartRect.Right + dx; freeY = dragStartRect.Y + dy;
                        break;
                    case 3:
                        anchorX = dragStartRect.Right; anchorY = dragStartRect.Y;
                        freeX = dragStartRect.X + dx; freeY = dragStartRect.Bottom + dy;
                        break;
                    default:
                        anchorX = dragStartRect.X; anchorY = dragStartRect.Y;
                        freeX = dragStartRect.Right + dx; freeY = dragStartRect.Bottom + dy;
                        break;
                }
                newRect.X = Math.Min(anchorX, freeX);
                newRect.Y = Math.Min(anchorY, freeY);
                newRect.Width = Math.Max(1, Math.Abs(freeX - anchorX));
                newRect.Height = Math.Max(1, Math.Abs(freeY - anchorY));
                ClampRectToImage(ref newRect);
            }

            cropRect = newRect;
            UpdateInfoLabel();
            pb.Invalidate();
        }
    }

    private void Pb_MouseUp(object? sender, MouseEventArgs e)
    {
        isDragging = false;
        dragMode = 0;
    }

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

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        base.OnFormClosed(e);
        sprite?.Dispose();
    }
}
