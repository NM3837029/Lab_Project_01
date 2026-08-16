using System;
using System.Drawing;
using System.Windows.Forms;
using System.IO;

namespace Lab_Editor;

// 敵（等）の表示スケールをドラッグ操作で視覚的に調整するページ。
// HitboxEditorPageControl と同様のドラッグUIパターンを踏襲する。
// 構造改修フェーズ5dでForm(SizeEditorForm)からUserControlへ抽出。
public class SizeEditorPageControl : UserControl
{
    public event EventHandler? Saved;
    public event EventHandler? Cancelled;
    public Button PrimaryActionButton => btnSave;
    public Button SecondaryActionButton => btnCancel;

    private PictureBox pb = null!;
    private Label lblInfo = null!;
    private Button btnSave = null!, btnCancel = null!, btnReset = null!;
    private Image? sprite;

    public float ResultScale { get; private set; }

    private float fitScale = 1.0f;   // プレビューウィンドウに収めるための倍率（保存対象ではない）
    private float currentScale;      // 実際にゲームへ反映されるスケール（保存対象）
    private bool isDragging = false;
    private Point dragStart;
    private float dragStartScale;
    private const int HANDLE_SIZE = 10;

    public SizeEditorPageControl(string imagePath, float initialScale)
    {
        Dock = DockStyle.Fill;
        Font = UiTheme.Base;

        currentScale = initialScale > 0 ? initialScale : 1.0f;
        ResultScale = currentScale;

        pb = new PictureBox
        {
            Location = new Point(10, 10),
            Size = new Size(560, 440),
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.LightGray,
            Cursor = Cursors.SizeNWSE
        };

        if (File.Exists(imagePath))
        {
            try { sprite = Image.FromFile(imagePath); } catch { sprite = null; }
        }
        if (sprite != null)
        {
            // ウィンドウ内に収まるようにベースの表示倍率を決める（この倍率自体は保存されない）
            fitScale = Math.Min((float)pb.Width / sprite.Width, (float)pb.Height / sprite.Height);
            if (fitScale > 4f) fitScale = 4f;
            if (fitScale <= 0f) fitScale = 1f;
        }

        pb.Paint += Pb_Paint;
        pb.MouseDown += Pb_MouseDown;
        pb.MouseMove += Pb_MouseMove;
        pb.MouseUp += (s, e) => isDragging = false;

        lblInfo = new Label
        {
            Location = new Point(10, 458),
            Size = new Size(560, 46),
            Font = new Font("Meiryo UI", 9.5f),
            ForeColor = Color.DimGray
        };
        UpdateInfoLabel();

        btnReset = new Button { Text = "リセット (1.0x)", Location = new Point(10, 512), Size = new Size(120, 32) };
        btnReset.Click += (s, e) => { currentScale = 1.0f; UpdateInfoLabel(); pb.Invalidate(); };

        btnSave = new Button { Text = "保存", Location = new Point(400, 512), Size = new Size(80, 32) };
        UiTheme.StylePrimaryButton(btnSave);
        btnSave.Click += (s, e) => { ResultScale = currentScale; Saved?.Invoke(this, EventArgs.Empty); };

        btnCancel = new Button { Text = "キャンセル", Location = new Point(490, 512), Size = new Size(90, 32) };
        btnCancel.Click += (s, e) => Cancelled?.Invoke(this, EventArgs.Empty);

        Controls.AddRange(new Control[] { pb, lblInfo, btnReset, btnSave, btnCancel });
    }

    // プレビュー領域内での実際の描画矩形（fitScale × currentScale を反映）
    private Rectangle GetDrawRect()
    {
        if (sprite == null) return new Rectangle(0, 0, pb.Width, pb.Height);
        float scale = fitScale * currentScale;
        int w = Math.Max(2, (int)(sprite.Width * scale));
        int h = Math.Max(2, (int)(sprite.Height * scale));
        int x = (pb.Width - w) / 2;
        int y = (pb.Height - h) / 2;
        return new Rectangle(x, y, w, h);
    }

    private void Pb_Paint(object? sender, PaintEventArgs e)
    {
        e.Graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
        var rect = GetDrawRect();

        if (sprite != null)
            e.Graphics.DrawImage(sprite, rect);

        using var pen = new Pen(Color.DeepSkyBlue, 2);
        e.Graphics.DrawRectangle(pen, rect);

        // 右下のリサイズハンドル
        e.Graphics.FillRectangle(Brushes.Yellow, rect.Right - HANDLE_SIZE / 2, rect.Bottom - HANDLE_SIZE / 2, HANDLE_SIZE, HANDLE_SIZE);
        e.Graphics.DrawRectangle(Pens.Black, rect.Right - HANDLE_SIZE / 2, rect.Bottom - HANDLE_SIZE / 2, HANDLE_SIZE, HANDLE_SIZE);
    }

    private bool IsOnHandle(int x, int y)
    {
        var rect = GetDrawRect();
        return Math.Abs(x - rect.Right) <= 8 && Math.Abs(y - rect.Bottom) <= 8;
    }

    private void Pb_MouseDown(object? sender, MouseEventArgs e)
    {
        if (sprite == null) return;
        if (IsOnHandle(e.X, e.Y))
        {
            isDragging = true;
            dragStart = e.Location;
            dragStartScale = currentScale;
        }
    }

    private void Pb_MouseMove(object? sender, MouseEventArgs e)
    {
        if (sprite == null) return;
        pb.Cursor = IsOnHandle(e.X, e.Y) || isDragging ? Cursors.SizeNWSE : Cursors.Default;

        if (!isDragging) return;

        // ドラッグの縦横平均移動量からスケールの増減を算出（右下へ引っ張るほど拡大）
        float dx = e.X - dragStart.X;
        float dy = e.Y - dragStart.Y;
        float delta = (dx + dy) / 2.0f;
        float newScale = dragStartScale + delta * 0.01f;
        if (newScale < 0.1f) newScale = 0.1f;
        if (newScale > 10f) newScale = 10f;
        currentScale = newScale;

        UpdateInfoLabel();
        pb.Invalidate();
    }

    private void UpdateInfoLabel()
    {
        int nw = sprite?.Width ?? 0;
        int nh = sprite?.Height ?? 0;
        lblInfo.Text =
            $"Scale: {currentScale:F2}x   ゲーム内表示サイズ: {(int)(nw * currentScale)} x {(int)(nh * currentScale)} px （元画像: {nw} x {nh} px）\n" +
            "右下の黄色いハンドルをドラッグしてサイズを調整してください。";
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) sprite?.Dispose();
        base.Dispose(disposing);
    }
}
