using System;
using System.Drawing;
using System.Windows.Forms;
using System.IO;

namespace Lab_Editor;

// 敵（等）の表示スケールをドラッグ操作で視覚的に調整するページ。
// プレビュー画像の右下にあるハンドルをマウスでドラッグすることで、
// 実際にゲーム内で表示される拡大率（スケール）を直感的に決められるようにする。
// HitboxEditorPageControl と同様のドラッグUIパターンを踏襲する。
// 構造改修フェーズ5dでForm(SizeEditorForm)からUserControlへ抽出。
public class SizeEditorPageControl : UserControl
{
    // 保存ボタンが押されたときに発生するイベント（呼び出し元はこれを購読して結果を受け取る）
    public event EventHandler? Saved;
    // キャンセルボタンが押されたときに発生するイベント
    public event EventHandler? Cancelled;
    // シェル側（WorkbenchShellForm）がAcceptButtonとして割り当てるための、保存ボタンへの参照
    public Button PrimaryActionButton => btnSave;
    // シェル側がCancelButtonとして割り当てるための、キャンセルボタンへの参照
    public Button SecondaryActionButton => btnCancel;

    // プレビュー画像を描画する領域
    private PictureBox pb = null!;
    // 現在のスケール値やサイズを文字で説明するラベル
    private Label lblInfo = null!;
    // 保存／キャンセル／リセットの各ボタン
    private Button btnSave = null!, btnCancel = null!, btnReset = null!;
    // 編集対象の画像（読み込みに失敗した場合はnullのまま）
    private Image? sprite;

    // 呼び出し元へ返す最終的なスケール値（保存ボタンが押された時点のcurrentScale）
    public float ResultScale { get; private set; }

    private float fitScale = 1.0f;   // プレビューウィンドウに収めるための倍率（保存対象ではない。表示上の見やすさだけを目的とする）
    private float currentScale;      // 実際にゲームへ反映されるスケール（保存対象。ユーザーがドラッグで調整する値）
    private bool isDragging = false; // 現在ハンドルをドラッグ中かどうか
    private Point dragStart;         // ドラッグを開始したときのマウス座標
    private float dragStartScale;    // ドラッグを開始した時点でのcurrentScale（差分計算の基準値）
    private const int HANDLE_SIZE = 10; // 右下のリサイズハンドル（黄色い四角）の一辺の大きさ（ピクセル）

    // コンストラクタ。
    // imagePath    : プレビュー表示する画像ファイルのパス
    // initialScale : 編集開始時点でのスケール初期値（0以下の場合は1.0倍として扱う）
    public SizeEditorPageControl(string imagePath, float initialScale)
    {
        Dock = DockStyle.Fill;
        Font = UiTheme.Base;

        // 初期スケールが0以下（未設定等）の場合は1.0倍にフォールバックする
        currentScale = initialScale > 0 ? initialScale : 1.0f;
        ResultScale = currentScale;

        // プレビュー画像を表示するためのピクチャーボックスを生成する
        pb = new PictureBox
        {
            Location = new Point(10, 10),
            Size = new Size(560, 440),
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.LightGray,
            Cursor = Cursors.SizeNWSE
        };

        // 指定パスに画像ファイルが存在すれば読み込む。読み込みに失敗した場合は
        // 例外を握りつぶしてspriteをnullのままにし、以降は「画像なし」として扱う。
        if (File.Exists(imagePath))
        {
            try { sprite = Image.FromFile(imagePath); } catch { sprite = null; }
        }
        if (sprite != null)
        {
            // ウィンドウ内に収まるようにベースの表示倍率を決める（この倍率自体は保存されない）
            // 横幅・縦幅それぞれで「収まる倍率」を求め、小さい方を採用することで画像全体が確実に収まるようにする
            fitScale = Math.Min((float)pb.Width / sprite.Width, (float)pb.Height / sprite.Height);
            // 極端に小さい画像で倍率が異常に大きくなり過ぎないよう上限を設ける
            if (fitScale > 4f) fitScale = 4f;
            // 万一0以下になった場合（想定外のサイズ等）は等倍にフォールバックする
            if (fitScale <= 0f) fitScale = 1f;
        }

        // ピクチャーボックスの描画・マウス操作に対するイベントハンドラを登録する
        pb.Paint += Pb_Paint;
        pb.MouseDown += Pb_MouseDown;
        pb.MouseMove += Pb_MouseMove;
        // マウスボタンを離したら必ずドラッグ状態を解除する
        pb.MouseUp += (s, e) => isDragging = false;

        // 現在のスケール値や表示サイズを説明するラベルを生成する
        lblInfo = new Label
        {
            Location = new Point(10, 458),
            Size = new Size(560, 46),
            Font = new Font("Meiryo UI", 9.5f),
            ForeColor = Color.DimGray
        };
        // ラベルの文言を初期状態に合わせて更新する
        UpdateInfoLabel();

        // 「リセット」ボタン：スケールを1.0倍（等倍）に戻す
        btnReset = new Button { Text = "リセット (1.0x)", Location = new Point(10, 512), Size = new Size(120, 32) };
        btnReset.Click += (s, e) => { currentScale = 1.0f; UpdateInfoLabel(); pb.Invalidate(); };

        // 「保存」ボタン：現在のスケールを結果として確定し、Savedイベントを発火する
        btnSave = new Button { Text = "保存", Location = new Point(400, 512), Size = new Size(80, 32) };
        UiTheme.StylePrimaryButton(btnSave);
        btnSave.Click += (s, e) => { ResultScale = currentScale; Saved?.Invoke(this, EventArgs.Empty); };

        // 「キャンセル」ボタン：変更を破棄してCancelledイベントを発火する
        btnCancel = new Button { Text = "キャンセル", Location = new Point(490, 512), Size = new Size(90, 32) };
        btnCancel.Click += (s, e) => Cancelled?.Invoke(this, EventArgs.Empty);

        // 生成した全コントロールをこのページに追加する
        Controls.AddRange(new Control[] { pb, lblInfo, btnReset, btnSave, btnCancel });
    }

    // プレビュー領域内での実際の描画矩形を計算する（fitScale × currentScale を反映）。
    // 画像がない場合はピクチャーボックス全体を返す（呼び出し側での例外を避けるための安全策）。
    private Rectangle GetDrawRect()
    {
        if (sprite == null) return new Rectangle(0, 0, pb.Width, pb.Height);
        // 表示用の倍率（ウィンドウに収める分）とユーザー指定の倍率を掛け合わせた最終倍率
        float scale = fitScale * currentScale;
        // 最終的な描画サイズ（極端に小さくなりすぎないよう最低2ピクセルを確保する）
        int w = Math.Max(2, (int)(sprite.Width * scale));
        int h = Math.Max(2, (int)(sprite.Height * scale));
        // ピクチャーボックスの中央に配置するためのオフセットを計算する
        int x = (pb.Width - w) / 2;
        int y = (pb.Height - h) / 2;
        return new Rectangle(x, y, w, h);
    }

    // ピクチャーボックスの再描画イベント。画像本体・枠線・リサイズハンドルを描く。
    private void Pb_Paint(object? sender, PaintEventArgs e)
    {
        // ドット絵の輪郭がぼやけないよう、拡大縮小時の補間方式を最近傍法に設定する
        e.Graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
        var rect = GetDrawRect();

        // 画像が読み込めていれば、計算済みの矩形に合わせて描画する
        if (sprite != null)
            e.Graphics.DrawImage(sprite, rect);

        // 画像の周囲に水色の枠線を描いて、現在の表示範囲を分かりやすくする
        using var pen = new Pen(Color.DeepSkyBlue, 2);
        e.Graphics.DrawRectangle(pen, rect);

        // 右下のリサイズハンドル（黄色い四角＋黒枠）を描画する。ここをドラッグするとスケールが変わる。
        e.Graphics.FillRectangle(Brushes.Yellow, rect.Right - HANDLE_SIZE / 2, rect.Bottom - HANDLE_SIZE / 2, HANDLE_SIZE, HANDLE_SIZE);
        e.Graphics.DrawRectangle(Pens.Black, rect.Right - HANDLE_SIZE / 2, rect.Bottom - HANDLE_SIZE / 2, HANDLE_SIZE, HANDLE_SIZE);
    }

    // 指定した座標(x, y)が右下のリサイズハンドルの近く（許容範囲8ピクセル以内）にあるかを判定する。
    private bool IsOnHandle(int x, int y)
    {
        var rect = GetDrawRect();
        return Math.Abs(x - rect.Right) <= 8 && Math.Abs(y - rect.Bottom) <= 8;
    }

    // マウスボタンが押されたときの処理。ハンドル上で押された場合のみドラッグを開始する。
    private void Pb_MouseDown(object? sender, MouseEventArgs e)
    {
        // 画像が無い場合はドラッグ操作自体が無意味なので何もしない
        if (sprite == null) return;
        if (IsOnHandle(e.X, e.Y))
        {
            // ドラッグ開始：開始座標と、その時点のスケール値を記録しておく
            isDragging = true;
            dragStart = e.Location;
            dragStartScale = currentScale;
        }
    }

    // マウス移動時の処理。ドラッグ中であればスケールを更新し、そうでなければカーソル形状のみ切り替える。
    private void Pb_MouseMove(object? sender, MouseEventArgs e)
    {
        if (sprite == null) return;
        // ハンドルの上、またはドラッグ中はリサイズカーソルを表示し、それ以外は通常カーソルに戻す
        pb.Cursor = IsOnHandle(e.X, e.Y) || isDragging ? Cursors.SizeNWSE : Cursors.Default;

        if (!isDragging) return;

        // ドラッグの縦横平均移動量からスケールの増減を算出（右下へ引っ張るほど拡大）
        // dx/dyはドラッグ開始位置からの移動量。斜め方向の動きにも自然に反応するよう平均を取る。
        float dx = e.X - dragStart.X;
        float dy = e.Y - dragStart.Y;
        float delta = (dx + dy) / 2.0f;
        // 移動量に係数0.01を掛けて、ドラッグ開始時点のスケールに加算する（ピクセル単位の細かい調整に対応するため）
        float newScale = dragStartScale + delta * 0.01f;
        // スケールが極端な値（小さすぎ・大きすぎ）にならないようクランプする
        if (newScale < 0.1f) newScale = 0.1f;
        if (newScale > 10f) newScale = 10f;
        currentScale = newScale;

        // 表示内容を最新のスケールに合わせて更新し、再描画を要求する
        UpdateInfoLabel();
        pb.Invalidate();
    }

    // 現在のスケール値・ゲーム内表示サイズ・元画像サイズを説明ラベルに反映する。
    private void UpdateInfoLabel()
    {
        // 画像が無い場合は0×0として扱う
        int nw = sprite?.Width ?? 0;
        int nh = sprite?.Height ?? 0;
        lblInfo.Text =
            $"Scale: {currentScale:F2}x   ゲーム内表示サイズ: {(int)(nw * currentScale)} x {(int)(nh * currentScale)} px （元画像: {nw} x {nh} px）\n" +
            "右下の黄色いハンドルをドラッグしてサイズを調整してください。";
    }

    // コントロール破棄時に、読み込んだ画像リソースを確実に解放する（メモリリーク防止）。
    protected override void Dispose(bool disposing)
    {
        if (disposing) sprite?.Dispose();
        base.Dispose(disposing);
    }
}
