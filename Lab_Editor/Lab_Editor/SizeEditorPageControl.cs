using System;
using System.Drawing;
using System.Windows.Forms;
using System.IO;

namespace Lab_Editor;

// オブジェクトの「ゲーム内での大きさ」を決めるページ。
//
// 従来はプレビュー右下のハンドルをドラッグするしか手段が無く、
// 「32px ちょうどにしたい」といった狙った値にするのが事実上できなかった。
// さらに表示されていた px 値がゲームの実際の描画と一致していなかったため、
// 表示を信じて合わせても、ゲームでは違う大きさで出るという食い違いがあった。
//
// ここでは次の4つを足している：
//   ・数値入力     … 倍率を直接打てる（ドラッグも今までどおり残す）
//   ・プリセット   … 16/32/48/64/96px。押すと「その大きさになる倍率」を逆算して入れる
//   ・仕上がり幅   … 縦横比を保ったまま、仕上がりの幅[px]を1つ入れるだけで決められる
//   ・判定の追従   … 「当たり判定も合わせる」で hitbox も同じ比率で更新する
//
// ■ ゲーム内の実際の大きさについて
// 敵は ComputeFitScale(handle, def.width, def.height) * def.scale という倍率で描かれる。
// ComputeFitScale は「画像を論理サイズ(width/height)に収める倍率」なので、
// 結局のところ画面上の大きさは ≒ def.width × scale になる。画像の原寸は関係ない。
// 以前のラベルは「画像の原寸 × scale」を出しており、ここが食い違いの原因だった。
//
// ギミックとアイテムには scale も width/height も無く、hitboxWidth/Height が
// そのまま描画サイズを兼ねている。そちらは倍率ではなく hitbox の数値を直接動かす。
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
    // 倍率の直接入力と、仕上がり幅[px]の直接入力
    private NumericUpDown nudScale = null!, nudTargetW = null!;
    // 当たり判定を表示サイズに追従させるか
    private CheckBox chkHitbox = null!;
    // 編集対象の画像（読み込みに失敗した場合はnullのまま）
    private Image? sprite;
    // NumericUpDown を内部から書き換えるときに、値変更イベントが再帰的に発火するのを防ぐ
    private bool suppressEvents = false;

    // 呼び出し元へ返す最終的な値
    public float ResultScale { get; private set; }
    public int ResultHitboxWidth { get; private set; }
    public int ResultHitboxHeight { get; private set; }

    private float fitScale = 1.0f;   // プレビューウィンドウに収めるための倍率（保存対象ではない。表示上の見やすさだけを目的とする）
    private float currentScale;      // 実際にゲームへ反映されるスケール（保存対象。ユーザーがドラッグで調整する値）
    private bool isDragging = false; // 現在ハンドルをドラッグ中かどうか
    private Point dragStart;         // ドラッグを開始したときのマウス座標
    private float dragStartScale;    // ドラッグを開始した時点でのcurrentScale（差分計算の基準値）
    private const int HANDLE_SIZE = 10; // 右下のリサイズハンドル（黄色い四角）の一辺の大きさ（ピクセル）

    // 論理サイズ（敵の def.width / def.height）。ゲーム内の見た目の大きさはこれ×scaleで決まる。
    private readonly int logicalW, logicalH;
    // 当たり判定サイズ。追従チェックがONのときだけ書き換える。
    private int hitboxW, hitboxH;
    // 倍率(scale)を持つ型か。敵はtrue、ギミック・アイテムはfalse（hitboxそのものが大きさ）。
    private readonly bool hasScale;
    // 編集開始時の値。「当たり判定も合わせる」の比率計算の基準にする。
    private readonly int baseHitboxW, baseHitboxH;
    private readonly float baseScale;

    // コンストラクタ。
    // imagePath    : プレビュー表示する画像ファイルのパス
    // initialScale : 編集開始時点でのスケール初期値（0以下の場合は1.0倍として扱う）
    // logicalW/H   : 論理サイズ。敵なら def.width/height、ギミック・アイテムなら hitbox と同じ値
    // hitboxW/H    : 当たり判定サイズ
    // hasScale     : 倍率を持つ型か（敵=true / ギミック・アイテム=false）
    public SizeEditorPageControl(string imagePath, float initialScale,
                                 int logicalW = 0, int logicalH = 0,
                                 int hitboxW = 0, int hitboxH = 0,
                                 bool hasScale = true)
    {
        Dock = DockStyle.Fill;
        Font = UiTheme.Base;

        // 初期スケールが0以下（未設定等）の場合は1.0倍にフォールバックする
        currentScale = initialScale > 0 ? initialScale : 1.0f;
        ResultScale = currentScale;
        baseScale = currentScale;
        this.hasScale = hasScale;
        this.hitboxW = hitboxW > 0 ? hitboxW : 32;
        this.hitboxH = hitboxH > 0 ? hitboxH : 32;
        baseHitboxW = this.hitboxW;
        baseHitboxH = this.hitboxH;
        ResultHitboxWidth = this.hitboxW;
        ResultHitboxHeight = this.hitboxH;
        // 論理サイズが渡されていない古い呼び出しでは、当たり判定を論理サイズとして扱う
        this.logicalW = logicalW > 0 ? logicalW : this.hitboxW;
        this.logicalH = logicalH > 0 ? logicalH : this.hitboxH;

        // プレビュー画像を表示するためのピクチャーボックスを生成する
        pb = new PictureBox
        {
            Location = new Point(10, 10),
            Size = new Size(560, 360),
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

        // ==== 数値での指定 ====
        var lblScale = UiTheme.CreateLabel("倍率", new Point(10, 382));
        lblScale.Size = new Size(36, 20);
        nudScale = UiTheme.CreateNumericUpDown(new Point(48, 378), 70, 0.1m, 10m, 2, 0.1m);
        nudScale.Enabled = hasScale;
        nudScale.Value = (decimal)Math.Clamp(currentScale, 0.1f, 10f);
        nudScale.ValueChanged += (s, e) =>
        {
            if (suppressEvents) return;
            currentScale = (float)nudScale.Value;
            SyncFromScale();
        };

        // 縦横比を保ったまま「仕上がりの幅」だけで決められるようにする。
        // 狙った大きさに合わせたいときは倍率より直感的なので、こちらを主役に置く。
        var lblTarget = UiTheme.CreateLabel("仕上がり幅", new Point(128, 382));
        lblTarget.Size = new Size(76, 20);
        nudTargetW = UiTheme.CreateNumericUpDown(new Point(206, 378), 70, 1m, 2048m, 0, 1m);
        nudTargetW.Value = (decimal)Math.Clamp((int)Math.Round(GameWidth()), 1, 2048);
        nudTargetW.ValueChanged += (s, e) =>
        {
            if (suppressEvents) return;
            ApplyTargetWidth((float)nudTargetW.Value);
        };

        // よく使う大きさはボタン一発で入れられるようにする（タイル1マスは32px）
        var lblPreset = UiTheme.CreateLabel("プリセット", new Point(290, 382));
        lblPreset.Size = new Size(62, 20);
        int px = 356;
        foreach (int preset in new[] { 16, 32, 48, 64, 96 })
        {
            int captured = preset;
            var b = UiTheme.CreateButton(preset.ToString(), new Point(px, 377), new Size(38, 26));
            b.Click += (s, e) => ApplyTargetWidth(captured);
            Controls.Add(b);
            px += 40;
        }

        // 当たり判定を表示サイズへ追従させるかどうか。
        // 大きさだけ変えて判定が置き去りになると、見た目と当たり判定がズレた敵ができてしまう。
        chkHitbox = new CheckBox
        {
            Text = "当たり判定も同じ比率で合わせる",
            Location = new Point(10, 410),
            Size = new Size(260, 22),
            Checked = true,
            // 倍率を持たない型では当たり判定そのものが大きさなので、追従という概念が無い
            Visible = hasScale,
        };
        chkHitbox.CheckedChanged += (s, e) => { SyncFromScale(); };

        // 現在のスケール値や表示サイズを説明するラベルを生成する
        lblInfo = new Label
        {
            Location = new Point(10, 436),
            Size = new Size(560, 64),
            Font = new Font("Meiryo UI", 9.5f),
            ForeColor = Color.DimGray
        };
        // ラベルの文言を初期状態に合わせて更新する
        UpdateInfoLabel();

        // 「リセット」ボタン：編集開始時点の値へ戻す
        btnReset = new Button { Text = "リセット", Location = new Point(10, 506), Size = new Size(120, 32) };
        btnReset.Click += (s, e) =>
        {
            currentScale = baseScale;
            hitboxW = baseHitboxW; hitboxH = baseHitboxH;
            SyncFromScale();
        };

        // 「保存」ボタン：現在の値を結果として確定し、Savedイベントを発火する
        btnSave = new Button { Text = "保存", Location = new Point(400, 506), Size = new Size(80, 32) };
        UiTheme.StylePrimaryButton(btnSave);
        btnSave.Click += (s, e) =>
        {
            ResultScale = currentScale;
            ResultHitboxWidth = hitboxW;
            ResultHitboxHeight = hitboxH;
            Saved?.Invoke(this, EventArgs.Empty);
        };

        // 「キャンセル」ボタン：変更を破棄してCancelledイベントを発火する
        btnCancel = new Button { Text = "キャンセル", Location = new Point(490, 506), Size = new Size(90, 32) };
        btnCancel.Click += (s, e) => Cancelled?.Invoke(this, EventArgs.Empty);

        // 生成した全コントロールをこのページに追加する
        Controls.AddRange(new Control[] { pb, lblScale, nudScale, lblTarget, nudTargetW, lblPreset, chkHitbox, lblInfo, btnReset, btnSave, btnCancel });
    }

    // ゲーム内での実際の表示幅[px]。
    // 敵は「論理サイズ × 倍率」、ギミック・アイテムは当たり判定の幅がそのまま表示幅になる。
    private float GameWidth() => hasScale ? logicalW * currentScale : hitboxW;

    // ゲーム内での実際の表示高さ[px]。
    private float GameHeight() => hasScale ? logicalH * currentScale : hitboxH;

    // 「仕上がりの幅[px]」を指定されたときに、そこへ到達する値を逆算して入れる。
    // 倍率を持つ型なら倍率を、持たない型なら当たり判定そのものを、縦横比を保ったまま動かす。
    private void ApplyTargetWidth(float targetW)
    {
        if (targetW <= 0f) return;
        if (hasScale)
        {
            if (logicalW <= 0) return;
            float ns = targetW / logicalW;
            currentScale = Math.Clamp(ns, 0.1f, 10f);
        }
        else
        {
            // 元の当たり判定の縦横比を保ったまま拡縮する
            float ratio = (baseHitboxW > 0) ? (float)baseHitboxH / baseHitboxW : 1f;
            hitboxW = Math.Max(1, (int)Math.Round(targetW));
            hitboxH = Math.Max(1, (int)Math.Round(targetW * ratio));
        }
        SyncFromScale();
    }

    // 現在の倍率を基準に、当たり判定・各入力欄・プレビューをまとめて整合させる。
    // 入力欄を書き換えるとValueChangedが再帰的に飛ぶので、suppressEventsで止めてから行う。
    private void SyncFromScale()
    {
        if (hasScale && chkHitbox != null && chkHitbox.Checked && baseScale > 0f)
        {
            // 倍率が baseScale から何倍になったかに合わせて、当たり判定も同じだけ動かす
            float ratio = currentScale / baseScale;
            hitboxW = Math.Max(1, (int)Math.Round(baseHitboxW * ratio));
            hitboxH = Math.Max(1, (int)Math.Round(baseHitboxH * ratio));
        }
        else if (hasScale)
        {
            hitboxW = baseHitboxW; hitboxH = baseHitboxH;
        }

        suppressEvents = true;
        nudScale.Value = (decimal)Math.Clamp(currentScale, 0.1f, 10f);
        nudTargetW.Value = (decimal)Math.Clamp((int)Math.Round(GameWidth()), 1, 2048);
        suppressEvents = false;

        UpdateInfoLabel();
        pb.Invalidate();
    }

    // プレビュー領域内での実際の描画矩形を計算する（fitScale × currentScale を反映）。
    // 画像がない場合はピクチャーボックス全体を返す（呼び出し側での例外を避けるための安全策）。
    private Rectangle GetDrawRect()
    {
        if (sprite == null) return new Rectangle(0, 0, pb.Width, pb.Height);
        // 倍率を持たない型では「当たり判定＝表示サイズ」なので、
        // 元の当たり判定からの倍率をプレビューの拡縮に使う。
        float shown = hasScale ? currentScale
                               : (baseHitboxW > 0 ? (float)hitboxW / baseHitboxW : 1f);
        // 表示用の倍率（ウィンドウに収める分）とユーザー指定の倍率を掛け合わせた最終倍率
        float scale = fitScale * shown;
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

        // タイル1マス(32px)を同じ縮尺で重ねて、ゲーム内でどのくらいの大きさになるかを目で比べられるようにする。
        // 数値だけだと「32px がどのくらいか」が掴みづらいため。
        float gw = GameWidth();
        if (gw > 0.1f)
        {
            float pxPerGamePx = rect.Width / gw;
            int tile = (int)Math.Round(32 * pxPerGamePx);
            // 目安の枠がプレビューからはみ出すようでは比べようがないので、収まるときだけ描く
            if (tile >= 4 && tile <= pb.Height - 30 && tile <= pb.Width - 12)
            {
                using var tilePen = new Pen(Color.OrangeRed, 1) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash };
                e.Graphics.DrawRectangle(tilePen, 6, 6, tile, tile);
                using var f = new Font("Meiryo UI", 7.5f);
                e.Graphics.DrawString("1マス(32px)", f, Brushes.OrangeRed, 6, 6 + tile + 2);
            }
        }

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
            dragStartScale = hasScale ? currentScale
                                      : (baseHitboxW > 0 ? (float)hitboxW / baseHitboxW : 1f);
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

        if (hasScale)
        {
            currentScale = newScale;
        }
        else
        {
            // 倍率を持たない型では当たり判定そのものを動かす（縦横比は維持）
            hitboxW = Math.Max(1, (int)Math.Round(baseHitboxW * newScale));
            hitboxH = Math.Max(1, (int)Math.Round(baseHitboxH * newScale));
        }

        // 表示内容を最新のスケールに合わせて更新し、再描画を要求する
        SyncFromScale();
    }

    // 現在の設定が、ゲーム内で実際にどう出るかを説明ラベルに反映する。
    private void UpdateInfoLabel()
    {
        int nw = sprite?.Width ?? 0;
        int nh = sprite?.Height ?? 0;
        // タイル何マスぶんかを添えると、ステージに置いたときの感覚が掴みやすい
        float tiles = GameWidth() / 32.0f;
        string head = hasScale
            ? $"倍率: {currentScale:F2}x   ゲーム内の大きさ: {(int)Math.Round(GameWidth())} x {(int)Math.Round(GameHeight())} px（約 {tiles:F1} マス）"
            : $"ゲーム内の大きさ: {hitboxW} x {hitboxH} px（約 {tiles:F1} マス）";
        string second = hasScale
            ? $"論理サイズ {logicalW} x {logicalH} px × 倍率 で決まります（元画像 {nw} x {nh} px は表示倍率にのみ影響）。"
            : $"この型は当たり判定の大きさがそのまま表示サイズです（元画像 {nw} x {nh} px）。";
        string third = $"当たり判定: {hitboxW} x {hitboxH} px" +
                       (hasScale && chkHitbox != null && chkHitbox.Checked ? "（表示サイズに追従中）" : "");
        lblInfo.Text = head + "\n" + second + "\n" + third;
    }

    // コントロール破棄時に、読み込んだ画像リソースを確実に解放する（メモリリーク防止）。
    protected override void Dispose(bool disposing)
    {
        if (disposing) sprite?.Dispose();
        base.Dispose(disposing);
    }
}
