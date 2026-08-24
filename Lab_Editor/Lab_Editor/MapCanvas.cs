using System.Drawing;
using System.Drawing.Drawing2D;

namespace Lab_Editor;

/// <summary>
/// タイルマップ + 配置オブジェクトのビジュアルキャンバス
/// Feature 1: 装飾レイヤー (DecoLayerBack / DecoLayerFront) 対応
/// Feature 4: TestPlay モード対応
/// Feature 5: トリガー矩形の描画・配置対応
/// </summary>
public class MapCanvas : Panel
{
    // エディタ画面上で1タイルを何pxとして描画するか（表示専用のサイズ）。
    public const int TILE_SIZE = 20;
    // 実際のゲーム内で1タイルが何pxに相当するか。座標変換（ワールド座標⇔画面座標）の基準値として使う。
    public const int GAME_TILE = 32;

    // キャンバス上でマウス操作がどの編集対象に作用するかを表すモード一覧。
    // toolStrip側のボタンやラジオボタンで切り替えられ、HandleLeft/HandleRight/OnPaintの分岐に使われる。
    public enum EditMode
    {
        Tile,            // メインのタイルレイヤーを編集
        DecoLayerBack,   // Feature 1: 装飾後景レイヤー（プレイヤーより奥に描画される装飾用タイル）
        DecoLayerFront,  // Feature 1: 装飾前景レイヤー（プレイヤーより手前に描画される装飾用タイル）
        Enemy,           // 敵キャラクターの配置
        Gimmick,         // ギミック（仕掛け）の配置
        Item,            // アイテムの配置
        Select,          // 配置済みオブジェクトの選択（プロパティグリッド表示用）
        PlayerStart,     // プレイヤー開始位置の指定
        Goal,            // ゴール位置の指定
        Trigger,         // Feature 5: トリガー矩形配置（ドラッグで範囲指定するイベント発生エリア）
        TestPlay,        // Feature 4: ここからプレイ（クリック位置取得。実際のテストプレイ開始地点を選ぶ）
        Eraser           // 消しゴムモード（タイル/装飾/配置オブジェクトを削除する）
    }

    // 現在アクティブな編集モード。既定値はタイル編集。
    public EditMode CurrentMode { get; set; } = EditMode.Tile;
    // タイルモードで現在選択中のタイルID（パレットから選ばれる）。
    public int SelectedTileId { get; set; } = 1;
    // 敵/ギミック/アイテムモードで現在選択中のアセットID（未選択ならnull）。
    public string? SelectedAssetId { get; set; }

    // 編集対象のステージデータ本体（マップ配列・配置オブジェクト等を保持）。
    public StageData? Stage { get; set; }
    // タイル定義や敵/ギミック/アイテムの定義など、アセット全体の情報。
    public AssetDefinitions? Assets { get; set; }

    // 選択モードで選ばれている配置済みオブジェクト（敵/ギミック/アイテム/トリガーのいずれか、またはnull）。
    // プロパティグリッド側で編集する対象として外部から参照される。
    public object? SelectedObject { get; set; }

    // 現在の水平スクロール量（px単位、画面表示上のオフセット）。
    public int ScrollX { get; set; } = 0;
    // 現在の垂直スクロール量（px単位、画面表示上のオフセット）。
    public int ScrollY { get; set; } = 0;

    // Feature 5: トリガー矩形ドラッグ用の状態管理フィールド群。
    private Point _triggerDragStart;      // ドラッグ開始時のマウス座標（キャンバス座標）
    private Rectangle _triggerDragRect;   // ドラッグ中に更新される矩形（プレビュー描画にも使う）
    private bool _isTriggerDragging = false; // 現在トリガードラッグ中かどうか

    // 外部（Form1側）へ通知するためのイベント群。
    public event EventHandler? ObjectSelected;   // 配置済みオブジェクトが選択された時に発火
    public event EventHandler? StageModified;    // ステージデータが変更された時に発火（保存フラグ更新等に使われる）
    public event EventHandler? EditCompleted;    // 一連のドラッグ編集操作が完了した時に発火（Undo履歴の確定タイミング等）

    /// <summary>Feature 4: ここからプレイのクリック座標（ワールド座標）</summary>
    public event EventHandler<(float wx, float wy)>? TestPlayClicked;

    /// <summary>Feature 5: トリガー矩形が確定された時のイベント</summary>
    public event EventHandler<EventTrigger>? TriggerPlaced;

    // 左ボタンドラッグ中かどうか（タイル連続配置などに使用）。
    private bool isDragging = false;
    // 右ボタンドラッグ中かどうか（タイル連続削除に使用）。
    private bool isRightDrag = false;

    // Feature: 配置の重複防止 — Enemy/Gimmick/Itemモードは「クリック(ドラッグ含む)1ストロークにつき1個」に制限する。
    // タイルモードのようにドラッグ中連続で置きたいわけではないため、MouseDown〜MouseUpの間で既に1個配置済みなら
    // MouseMoveでの追加配置をスキップする。
    private bool _strokePlacedAsset = false;

    // タイル色マップ（Tile ID → Color）。
    // スプライト画像が用意されていないタイルIDに対する、代替の塗りつぶし色を定義する初期値。
    private Dictionary<int, Color> _tileColors = new()
    {
        [0] = Color.FromArgb(180, 210, 240),
        [1] = Color.FromArgb(80, 160, 60),
        [2] = Color.FromArgb(140, 100, 60),
        [3] = Color.FromArgb(204, 51, 51),
        [4] = Color.FromArgb(85, 85, 85),
    };

    // タイル画像マップ（Tile ID → Image）。RefreshTileColors()でAssetsから読み込んで構築される。
    private Dictionary<int, Image> _tileImages = new();

    // タイル通行設定（Tile ID → (当たり判定あり, 即死)）。MZ風の通行設定可視化用に使う。
    private Dictionary<int, (bool collidable, bool deadly)> _tileMeta = new();

    // タイル画像・スプライト画像を読み込む際の基準となるアセットフォルダのパス。
    public string AssetsPath { get; set; } = "";

    // 装飾レイヤー用のやや暗い色変換（視覚的区別のため半透明表示にする）。
    // 引数の色にアルファ値160を適用した半透明色を返す。
    private static Color DecoColor(Color c) => Color.FromArgb(160, c);

    // タイルカラーと画像を Assets から再構築する。
    // Assets（タイル定義一覧）が変更された時や、AssetsPathが変わった時に呼び出す想定。
    public void RefreshTileColors()
    {
        // アセット情報が未設定の場合は何もできないため即終了。
        if (Assets == null) return;
        // 既存のキャッシュをすべてクリアしてから再構築する。
        _tileColors.Clear();
        // 画像リソースは明示的にDisposeしてメモリリークを防ぐ。
        foreach (var img in _tileImages.Values) { img.Dispose(); }
        _tileImages.Clear();
        _tileMeta.Clear();

        // タイル定義を1件ずつ処理し、色情報・通行設定・画像をそれぞれのキャッシュに格納していく。
        foreach (var t in Assets.Tiles)
        {
            try
            {
                // HTMLカラーコード文字列（例: "#RRGGBB"）をColor型に変換する。
                var c = ColorTranslator.FromHtml(t.color);
                _tileColors[t.id] = c;
            }
            catch
            {
                // カラーコードの形式が不正な場合はグレーで代替する（読み込み失敗による表示崩れを防ぐ）。
                _tileColors[t.id] = Color.Gray;
            }

            // 当たり判定の有無・即死判定をメタ情報として保持（OnPaintでの可視化に使用）。
            _tileMeta[t.id] = (t.collidable, t.deadly);

            // 画像の読み込み（スプライトパスが指定されている場合のみ）。
            if (!string.IsNullOrEmpty(t.sprite) && !string.IsNullOrEmpty(AssetsPath))
            {
                string imgPath = System.IO.Path.Combine(AssetsPath, t.sprite);
                if (System.IO.File.Exists(imgPath))
                {
                    try
                    {
                        _tileImages[t.id] = Image.FromFile(imgPath);
                    }
                    catch
                    {
                        // 読み込み失敗時は無視（画像なしの色塗りつぶし表示にフォールバックされる）
                    }
                }
            }
        }
        // タイルID 0（何も配置されていない状態）用の色が未定義の場合は、既定の空色を設定しておく。
        if (!_tileColors.ContainsKey(0)) _tileColors[0] = Color.FromArgb(180, 210, 240);
        // 色/画像の再構築が完了したので再描画を要求する。
        Invalidate();
    }

    // コンストラクタ：描画のちらつきを防ぐためのダブルバッファリング設定を行う。
    public MapCanvas()
    {
        // GDI+描画をバッファに一旦描いてから画面に転送することで、再描画時のちらつきを抑える。
        DoubleBuffered = true;
        // WinFormsのペイント制御スタイルを、独自描画＋ダブルバッファリング前提の設定に切り替える。
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        // キャンバスの背景色（マップ外の余白部分に見える色）を空色に設定。
        BackColor = Color.FromArgb(180, 210, 240);
    }

    // キャンバス全体の描画処理。WinFormsのPaintイベントに対応するオーバーライド。
    // レイヤーの重ね順、グリッド線、各種マーカー（プレイヤー/ゴール/敵/ギミック/アイテム/トリガー）を
    // すべてここで一括して描画する。
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        // ステージが読み込まれていない場合は描画する内容がないため終了。
        if (Stage == null) return;

        var g = e.Graphics;
        // 大量のタイルを高速に描画するため、アンチエイリアス等を無効化した高速モードを使用。
        g.SmoothingMode = SmoothingMode.HighSpeed;

        // 現在のスクロール位置とキャンバスサイズから、実際に画面に映る範囲のタイル座標（開始/終了の行・列）を算出する。
        // +2 しているのは、キャンバス端で半端に見切れるタイルも確実に描画されるようにするための余裕分。
        int startCol = ScrollX / TILE_SIZE;
        int endCol = Math.Min(Stage.MapW, startCol + Width / TILE_SIZE + 2);
        int startRow = ScrollY / TILE_SIZE;
        int endRow = Math.Min(Stage.MapH, startRow + Height / TILE_SIZE + 2);

        // ==== レイヤー描画順: 装飾後景 → メイン → 装飾前景 ====

        // 現在の編集モードに応じて、どのレイヤーを「アクティブ（明るく表示）」にするかを判定する。
        bool isL1 = CurrentMode == EditMode.DecoLayerBack;
        bool isL2 = CurrentMode == EditMode.Tile;
        bool isL3 = CurrentMode == EditMode.DecoLayerFront;
        bool isL4 = CurrentMode == EditMode.Trigger || CurrentMode == EditMode.PlayerStart || CurrentMode == EditMode.Goal || CurrentMode == EditMode.Enemy || CurrentMode == EditMode.Gimmick || CurrentMode == EditMode.Item;
        // Select/TestPlay/Eraserモードではどのレイヤーも「非アクティブ扱いにしない」（全レイヤーを通常表示する）。
        bool isNeutral = CurrentMode == EditMode.Select || CurrentMode == EditMode.TestPlay || CurrentMode == EditMode.Eraser;

        // 1. 装飾レイヤー（後景）を最初に描画する（一番奥に見える）。
        DrawLayer(g, Stage.DecoLayerBack, startRow, endRow, startCol, endCol, isDecoBefore: true, isActive: isL1 || isNeutral);

        // 2. メインタイルレイヤーを描画する（地形の主体）。
        DrawLayer(g, Stage.Map, startRow, endRow, startCol, endCol, isDecoBefore: false, isActive: isL2 || isNeutral);

        // 3. グリッド線を描画し、タイル境界を分かりやすくする。
        using var gridPen = new Pen(Color.FromArgb(40, 0, 0, 0), 1);
        for (int row = startRow; row <= endRow; row++)
            g.DrawLine(gridPen, 0, row * TILE_SIZE - ScrollY, Width, row * TILE_SIZE - ScrollY);
        for (int col = startCol; col <= endCol; col++)
            g.DrawLine(gridPen, col * TILE_SIZE - ScrollX, 0, col * TILE_SIZE - ScrollX, Height);

        // 4. 装飾レイヤー（前景）を描画する（プレイヤーより手前に見える装飾）。
        DrawLayer(g, Stage.DecoLayerFront, startRow, endRow, startCol, endCol, isDecoBefore: true, isActive: isL3 || isNeutral);

        // 5. マップ外境界線を描画し、マップの有効範囲を視覚的に示す。
        int mapRight = Stage.MapW * TILE_SIZE - ScrollX;
        int mapBottom = Stage.MapH * TILE_SIZE - ScrollY;
        using var borderPen = new Pen(Color.FromArgb(200, 255, 100, 0), 2);
        g.DrawRectangle(borderPen, 0, 0, mapRight, mapBottom);

        // 6. プレイヤー開始位置 (P) を描画する。
        DrawMarker(g, Stage.PlayerStartX, Stage.PlayerStartY, "P", Color.FromArgb(0, 200, 100));

        // 7. ゴール (G) を描画する（未設定の場合はX座標が負値になっているため描画をスキップ）。
        if (Stage.GoalX >= 0)
            DrawMarker(g, Stage.GoalX, Stage.GoalY, "G", Color.FromArgb(255, 215, 0));

        // 7.5. 敵の巡回範囲プレビュー（MZ の移動ルートプレビュー風の点線表示）。
        DrawPatrolRanges(g);

        // 8. 敵（type_enumごとのアイコンで表示。テキスト表記のみでは判別しにくいため）
        foreach (var en in Stage.Enemies)
        {
            // 配置済みの敵IDから、対応する敵定義のtype_enumを引いてアイコン文字を決定する（定義が見つからない場合は-1として扱う）。
            string icon = AssetIcons.ForEnemy(Assets?.Enemies.FirstOrDefault(d => d.id == en.Id)?.type_enum ?? -1);
            // 現在選択中のオブジェクトであればマゼンタで強調表示する。
            DrawMarker(g, en.X, en.Y, icon, SelectedObject == en ? Color.Magenta : Color.FromArgb(220, 50, 50), 11f);
        }

        // 9. ギミック（敵と同様、type_enumに応じたアイコンで描画）
        foreach (var gi in Stage.Gimmicks)
        {
            string icon = AssetIcons.ForGimmick(Assets?.Gimmicks.FirstOrDefault(d => d.id == gi.Id)?.type_enum ?? -1);
            DrawMarker(g, gi.X, gi.Y, icon, SelectedObject == gi ? Color.Magenta : Color.FromArgb(50, 120, 220), 11f);
        }

        // 10. アイテム（同上のロジックでアイコン表示）
        foreach (var it in Stage.Items)
        {
            string icon = AssetIcons.ForItem(Assets?.Items.FirstOrDefault(d => d.id == it.Id)?.type_enum ?? -1);
            DrawMarker(g, it.X, it.Y, icon, SelectedObject == it ? Color.Magenta : Color.FromArgb(255, 200, 0), 11f);
        }

        // 11. トリガー矩形 (Feature 5)。1件以上存在する場合のみ描画処理を呼ぶ（無駄な呼び出しを避ける）。
        if (Stage.Triggers.Count > 0)
            DrawTriggers(g);

        // 12. ドラッグ中のトリガー矩形プレビュー。実際にドラッグ操作中で、かつ矩形に幅・高さがある場合のみ表示。
        if (_isTriggerDragging && _triggerDragRect.Width > 0 && _triggerDragRect.Height > 0)
        {
            using var trigBrush = new SolidBrush(Color.FromArgb(60, 255, 140, 0));
            using var trigPen = new Pen(Color.OrangeRed, 2) { DashStyle = DashStyle.Dash };
            g.FillRectangle(trigBrush, _triggerDragRect);
            g.DrawRectangle(trigPen, _triggerDragRect);
        }

        // 13. アクティブレイヤーのインジケーター（画面左上に現在の編集モードを文字で表示）。
        DrawLayerIndicator(g);
    }

    // 指定されたレイヤー配列（タイルID二次元配列）を、可視範囲だけ走査して描画するヘルパーメソッド。
    // layer      : 描画対象のタイルID配列（Stage.Map / DecoLayerBack / DecoLayerFront のいずれか）
    // startRow〜endCol : 描画すべきタイル範囲（画面に映る範囲のみを対象にすることで描画負荷を抑える）
    // isDecoBefore : 装飾レイヤーかどうか（trueの場合、斜線ハッチングを重ねて装飾であることを視覚的に示す）
    // isActive   : このレイヤーが現在の編集対象としてアクティブかどうか（非アクティブなら暗くする）
    private void DrawLayer(Graphics g, int[,] layer, int startRow, int endRow, int startCol, int endCol, bool isDecoBefore, bool isActive)
    {
        for (int row = startRow; row < endRow; row++)
        {
            for (int col = startCol; col < endCol; col++)
            {
                int tileId = layer[row, col];
                // タイルID 0 は「何も配置されていない」ことを意味するため描画をスキップする。
                if (tileId == 0) continue;
                int px = col * TILE_SIZE - ScrollX;
                int py = row * TILE_SIZE - ScrollY;

                if (_tileImages.TryGetValue(tileId, out var img))
                {
                    // スプライト画像が用意されている場合はそれを描画する。
                    g.DrawImage(img, px, py, TILE_SIZE, TILE_SIZE);
                }
                else
                {
                    // 画像がない場合は従来の色塗りつぶしにフォールバックする。
                    var baseColor = _tileColors.TryGetValue(tileId, out var c) ? c : Color.Gray;
                    using var brush = new SolidBrush(baseColor);
                    g.FillRectangle(brush, px, py, TILE_SIZE, TILE_SIZE);
                }

                // 非アクティブレイヤーは黒半透明を重ねて暗くする（MZ風の「今どのレイヤーを触っているか」を示す演出）。
                if (!isActive)
                {
                    using var shade = new SolidBrush(Color.FromArgb(120, 0, 0, 0));
                    g.FillRectangle(shade, px, py, TILE_SIZE, TILE_SIZE);
                }

                // 装飾レイヤーには斜線ハッチングを重ね、メインレイヤーと視覚的に区別できるようにする。
                if (isDecoBefore)
                {
                    using var hatch = new HatchBrush(HatchStyle.ForwardDiagonal, Color.FromArgb(30, 255, 255, 255), Color.Transparent);
                    g.FillRectangle(hatch, px, py, TILE_SIZE, TILE_SIZE);
                }

                // メインレイヤーのみ：通行設定を可視化する（MZ風の当たり判定プレビュー）。
                if (!isDecoBefore && _tileMeta.TryGetValue(tileId, out var meta))
                {
                    if (meta.deadly)
                    {
                        // 即死タイルは赤い×印で警告表示する。
                        using var deadlyPen = new Pen(Color.FromArgb(220, 255, 0, 0), 2);
                        g.DrawLine(deadlyPen, px + 2, py + 2, px + TILE_SIZE - 2, py + TILE_SIZE - 2);
                        g.DrawLine(deadlyPen, px + TILE_SIZE - 2, py + 2, px + 2, py + TILE_SIZE - 2);
                    }
                    else if (!meta.collidable)
                    {
                        // すり抜け可能（当たり判定なし）タイルは水色の点線枠で示す。
                        using var passPen = new Pen(Color.FromArgb(200, 0, 220, 220), 1) { DashStyle = DashStyle.Dot };
                        g.DrawRectangle(passPen, px + 1, py + 1, TILE_SIZE - 2, TILE_SIZE - 2);
                    }
                }
            }
        }
    }

    // 各敵の巡回範囲（PatrolLeft〜PatrolRight）を点線＋端の縦棒で描画するヘルパーメソッド。
    // ワールド座標系の値を、表示スケール(TILE_SIZE / GAME_TILE)を使って画面座標に変換してから描画する。
    private void DrawPatrolRanges(Graphics g)
    {
        float scale = (float)TILE_SIZE / GAME_TILE;
        foreach (var en in Stage!.Enemies)
        {
            // 巡回範囲が未設定（左端が負）または範囲が無効（右端が左端以下）の敵は描画対象外。
            if (en.PatrolLeft < 0 || en.PatrolRight <= en.PatrolLeft) continue;

            int lx = (int)(en.PatrolLeft * scale) - ScrollX;
            int rx = (int)(en.PatrolRight * scale) - ScrollX;
            // 縦位置は敵の中心（Y + GAME_TILEの半分）を基準にする。
            int ly = (int)((en.Y + GAME_TILE / 2f) * scale) - ScrollY;
            bool selected = SelectedObject == en;

            // 選択中の敵は太いマゼンタ線、それ以外は細い赤系の線で描画する。
            using var patrolPen = new Pen(selected ? Color.Magenta : Color.FromArgb(170, 220, 60, 60), selected ? 2.5f : 1.5f)
            { DashStyle = DashStyle.Dash };
            g.DrawLine(patrolPen, lx, ly, rx, ly);
            // 左右の端に短い縦棒を描き、範囲の境界を分かりやすくする。
            g.DrawLine(patrolPen, lx, ly - 5, lx, ly + 5);
            g.DrawLine(patrolPen, rx, ly - 5, rx, ly + 5);
        }
    }

    // Feature 5: ステージに登録されているトリガー矩形をすべて描画するヘルパーメソッド。
    // 半透明のオレンジ塗りつぶし＋枠線＋ID文字ラベルで表示する。
    private void DrawTriggers(Graphics g)
    {
        float scale = (float)TILE_SIZE / GAME_TILE;
        foreach (var t in Stage!.Triggers)
        {
            // ワールド座標（ゲーム内座標）を画面座標に変換する。
            int px = (int)(t.x * scale) - ScrollX;
            int py = (int)(t.y * scale) - ScrollY;
            int pw = (int)(t.width * scale);
            int ph = (int)(t.height * scale);
            using var brush = new SolidBrush(Color.FromArgb(45, 255, 140, 0));
            using var pen = new Pen(Color.OrangeRed, 1.5f);
            g.FillRectangle(brush, px, py, pw, ph);
            g.DrawRectangle(pen, px, py, pw, ph);
            // トリガーID表示（矩形の左上に小さく描画）
            using var font = new Font("Meiryo UI", 6, FontStyle.Bold);
            using var tb = new SolidBrush(Color.OrangeRed);
            g.DrawString($"T:{t.id}", font, tb, px + 2, py + 2);
        }
    }

    // 現在の編集モードを画面左上にラベル表示するヘルパーメソッド。
    // 装飾レイヤー編集中／トリガー配置中／テストプレイ位置指定中など、
    // ユーザーが「今何をしているか」を見失わないようにするための補助表示。
    private void DrawLayerIndicator(Graphics g)
    {
        string layerName = CurrentMode switch
        {
            EditMode.DecoLayerBack => "📌 装飾レイヤー[後景]編集中",
            EditMode.DecoLayerFront => "📌 装飾レイヤー[前景]編集中",
            EditMode.Trigger => "📌 トリガー配置中 (ドラッグで矩形)",
            EditMode.TestPlay => "📍 クリックでテストプレイ開始位置を指定",
            // 上記以外のモードでは特に表示するメッセージがないため空文字にする。
            _ => ""
        };
        // 表示すべきメッセージがない場合は何も描画せず終了。
        if (string.IsNullOrEmpty(layerName)) return;

        // 半透明の黒背景を敷いた上に黄色文字でラベルを描画し、視認性を高める。
        using var bgBrush = new SolidBrush(Color.FromArgb(160, 30, 30, 30));
        using var font = new Font("Meiryo UI", 8, FontStyle.Bold);
        using var tb = new SolidBrush(Color.Yellow);
        var sz = g.MeasureString(layerName, font);
        g.FillRectangle(bgBrush, 4, 4, sz.Width + 6, sz.Height + 4);
        g.DrawString(layerName, font, tb, 7, 6);
    }

    // 配置オブジェクト（プレイヤー開始位置/ゴール/敵/ギミック/アイテム）を1つの正方形マーカーとして描画する共通処理。
    // worldX, worldY : ワールド座標（ゲーム内座標）でのオブジェクトの位置
    // label           : マーカー中央に表示する短いラベル文字（例："P"、絵文字アイコンなど）
    // color            : マーカーの塗りつぶし色・枠線色の基準色
    // fontSize         : ラベル文字のフォントサイズ（省略時は7）
    private void DrawMarker(Graphics g, float worldX, float worldY, string label, Color color, float fontSize = 7f)
    {
        float scale = (float)TILE_SIZE / GAME_TILE;
        int px = (int)(worldX * scale) - ScrollX;
        int py = (int)(worldY * scale) - ScrollY;
        int sz = TILE_SIZE;
        // キャンバス表示範囲外にあるマーカーは描画コストを省くためスキップする。
        if (px + sz < 0 || px > Width || py + sz < 0 || py > Height) return;

        // 半透明の塗りつぶし＋不透明の枠線で正方形マーカーを描画する。
        using var brush = new SolidBrush(Color.FromArgb(180, color));
        g.FillRectangle(brush, px, py, sz, sz);
        using var pen = new Pen(color, 2);
        g.DrawRectangle(pen, px, py, sz, sz);
        // マーカー内にラベル文字を白色で描画する。
        using var font = new Font("Meiryo UI", fontSize, FontStyle.Bold);
        using var tb = new SolidBrush(Color.White);
        g.DrawString(label, font, tb, px + 1, py + 2);
    }

    // キャンバス座標（マウス位置等、画面上のpx）→ タイル座標（マップ配列の列・行インデックス）へ変換する。
    // スクロール量を加味した上で、マップの範囲内にクランプ（丸め込み）する。
    private (int col, int row) ToTile(int cx, int cy)
    {
        // ステージ未読み込みの場合は原点を返しておく（呼び出し元でStage==nullチェックが行われている前提）。
        if (Stage == null) return (0, 0);
        int col = Math.Clamp((cx + ScrollX) / TILE_SIZE, 0, Stage.MapW - 1);
        int row = Math.Clamp((cy + ScrollY) / TILE_SIZE, 0, Stage.MapH - 1);
        return (col, row);
    }

    // キャンバス座標 → ワールド座標（グリッドスナップ）へ変換する。
    // 敵/ギミック/アイテム/プレイヤー開始位置/ゴールなど、配置座標をタイル境界にきっちり合わせたい場合に使用する。
    private (float wx, float wy) ToWorldSnapped(int cx, int cy)
    {
        float scale = (float)GAME_TILE / TILE_SIZE;
        // 一度ゲーム座標に変換した上でGAME_TILE単位に切り捨て、グリッドに吸着（スナップ）させる。
        float wx = (float)(Math.Floor((cx + ScrollX) * scale / GAME_TILE) * GAME_TILE);
        float wy = (float)(Math.Floor((cy + ScrollY) * scale / GAME_TILE) * GAME_TILE);
        return (wx, wy);
    }

    // Feature: UI改善（友人フィードバック対応）— ToTileと同様にマップ範囲へクランプする
    // キャンバス座標 → ワールド座標（スナップなし、任意精度）へ変換する。
    // トリガー矩形のドラッグ終端やテストプレイのクリック位置など、グリッドに縛られない自由な座標が必要な場面で使用する。
    private (float wx, float wy) ToWorld(int cx, int cy)
    {
        if (Stage == null) return (0, 0);
        float scale = (float)GAME_TILE / TILE_SIZE;
        float wx = (cx + ScrollX) * scale;
        float wy = (cy + ScrollY) * scale;
        // マップの外側を指した場合でも、結果がマップ範囲内に収まるようクランプする。
        wx = Math.Clamp(wx, 0f, Stage.MapW * (float)GAME_TILE);
        wy = Math.Clamp(wy, 0f, Stage.MapH * (float)GAME_TILE);
        return (wx, wy);
    }

    // ワールド座標系の矩形（wx, wy, ww, wh）を、現在のスクロール位置・表示スケールを反映した
    // 画面上のスクリーン矩形（Rectangle）へ変換する。
    private Rectangle ToScreenRect(float wx, float wy, float ww, float wh)
    {
        float scale = (float)TILE_SIZE / GAME_TILE;
        int px = (int)(wx * scale) - ScrollX;
        int py = (int)(wy * scale) - ScrollY;
        int pw = (int)(ww * scale);
        int ph = (int)(wh * scale);
        return new Rectangle(px, py, pw, ph);
    }

    // マウスボタン押下時の処理。編集モードに応じてタイル配置・オブジェクト配置・選択・トリガードラッグ開始などを行う。
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (Stage == null) return;

        // Feature 5: トリガーモードのドラッグ開始。左クリックでドラッグ矩形の起点を記録する。
        if (CurrentMode == EditMode.Trigger && e.Button == MouseButtons.Left)
        {
            _triggerDragStart = e.Location;
            _triggerDragRect = new Rectangle(e.X, e.Y, 0, 0);
            _isTriggerDragging = true;
            return;
        }

        // 左クリック：ドラッグ開始フラグを立て、ストローク内配置済みフラグをリセットしてから編集処理を実行する。
        if (e.Button == MouseButtons.Left) { isDragging = true; _strokePlacedAsset = false; HandleLeft(e.X, e.Y); }
        // 右クリック：ドラッグ削除の開始として扱う。
        else if (e.Button == MouseButtons.Right) { isRightDrag = true; HandleRight(e.X, e.Y); }
    }

    // マウス移動時の処理。ドラッグ中であれば継続して編集を適用し、モードに応じてカーソル形状も切り替える。
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (Stage == null) return;

        // Feature 5: トリガードラッグ中の矩形更新。始点と現在位置から矩形の左上・幅・高さを再計算する。
        if (_isTriggerDragging && e.Button == MouseButtons.Left)
        {
            int x = Math.Min(_triggerDragStart.X, e.X);
            int y = Math.Min(_triggerDragStart.Y, e.Y);
            int w = Math.Abs(e.X - _triggerDragStart.X);
            int h = Math.Abs(e.Y - _triggerDragStart.Y);
            _triggerDragRect = new Rectangle(x, y, w, h);
            Invalidate();
            return;
        }

        // カーソル変更：現在のモードに応じて分かりやすいカーソル形状に切り替える。
        if (CurrentMode == EditMode.TestPlay) Cursor = Cursors.Cross;
        else if (CurrentMode == EditMode.Trigger) Cursor = Cursors.SizeNWSE;
        else Cursor = Cursors.Default;

        // 左ドラッグ中なら継続して配置処理を行う（タイルの連続塗り等）。
        if (isDragging) HandleLeft(e.X, e.Y);
        // 右ドラッグ中はタイルモードに限り連続削除を行う（他モードでの誤削除を防ぐため）。
        else if (isRightDrag && CurrentMode == EditMode.Tile) HandleRight(e.X, e.Y);
    }

    // マウスボタン解放時の処理。ドラッグ状態を終了し、必要であれば確定処理（トリガー生成・編集完了通知）を行う。
    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);

        // Feature 5: トリガーのドラッグ確定。左ボタンが離されたタイミングでトリガーを生成する。
        if (_isTriggerDragging && e.Button == MouseButtons.Left)
        {
            _isTriggerDragging = false;
            // 矩形が小さすぎる（4px未満）場合は誤クリックとみなし、トリガーを生成しない。
            if (_triggerDragRect.Width > 4 && _triggerDragRect.Height > 4)
            {
                var (wx1, wy1) = ToWorld(_triggerDragRect.Left, _triggerDragRect.Top);
                var (wx2, wy2) = ToWorld(_triggerDragRect.Right, _triggerDragRect.Bottom);
                var trigger = new EventTrigger
                {
                    // トリガーIDは連番形式（trigger_001など）で自動採番する。
                    id = $"trigger_{Stage!.Triggers.Count + 1:D3}",
                    x = wx1, y = wy1,
                    width = wx2 - wx1,
                    height = wy2 - wy1
                };
                // 呼び出し元（Form1）へ生成したトリガーを通知し、実際のリスト追加は呼び出し元に委ねる。
                TriggerPlaced?.Invoke(this, trigger);
            }
            _triggerDragRect = Rectangle.Empty;
            Invalidate();
            return;
        }

        // 何らかの編集ドラッグ（左/右）が行われていたかどうかを判定してから、両フラグをリセットする。
        bool wasEditing = isDragging || isRightDrag;
        isDragging = isRightDrag = false;

        // 実際に編集操作が行われていた場合のみ、編集完了イベントを発火する（Undo履歴確定等に使われる）。
        if (wasEditing)
        {
            EditCompleted?.Invoke(this, EventArgs.Empty);
        }
    }

    // 左クリック（および左ドラッグ中）の編集処理を、現在の編集モードに応じて振り分ける。
    // cx, cy : キャンバス座標（マウス位置）
    private void HandleLeft(int cx, int cy)
    {
        if (Stage == null) return;
        switch (CurrentMode)
        {
            case EditMode.Tile:
                // クリック位置のタイルを選択中のタイルIDに置き換える（変化がある場合のみ更新・通知する）。
                var (col, row) = ToTile(cx, cy);
                if (Stage.Map[row, col] != SelectedTileId)
                { Stage.Map[row, col] = SelectedTileId; Fire(); }
                break;

            case EditMode.Eraser:
                // 消しゴムモード：メイン/装飾後景/装飾前景のいずれかにタイルが存在すればすべて消去する。
                var (colE, rowE) = ToTile(cx, cy);
                bool deletedTile = false;
                if (Stage.Map[rowE, colE] != 0) { Stage.Map[rowE, colE] = 0; deletedTile = true; }
                if (Stage.DecoLayerBack[rowE, colE] != 0) { Stage.DecoLayerBack[rowE, colE] = 0; deletedTile = true; }
                if (Stage.DecoLayerFront[rowE, colE] != 0) { Stage.DecoLayerFront[rowE, colE] = 0; deletedTile = true; }
                if (deletedTile) { Fire(); }
                // タイル以外（敵/ギミック/アイテム/トリガー）もこの位置にあれば併せて削除を試みる。
                DoDelete(cx, cy);
                break;

            case EditMode.DecoLayerBack: // Feature 1
                // 装飾後景レイヤーへタイルを配置する（メインレイヤーと同じロジックだが対象配列が異なる）。
                var (dcb, drb) = ToTile(cx, cy);
                if (Stage.DecoLayerBack[drb, dcb] != SelectedTileId)
                { Stage.DecoLayerBack[drb, dcb] = SelectedTileId; Fire(); }
                break;

            case EditMode.DecoLayerFront: // Feature 1
                // 装飾前景レイヤーへタイルを配置する。
                var (dcf, drf) = ToTile(cx, cy);
                if (Stage.DecoLayerFront[drf, dcf] != SelectedTileId)
                { Stage.DecoLayerFront[drf, dcf] = SelectedTileId; Fire(); }
                break;

            case EditMode.Enemy:
                // アセットが選択されており、かつこのストローク内でまだ配置していない場合のみ1体配置する。
                if (!string.IsNullOrEmpty(SelectedAssetId) && !_strokePlacedAsset)
                {
                    var (wx, wy) = ToWorldSnapped(cx, cy);
                    Stage.Enemies.Add(new PlacedEnemy { Id = SelectedAssetId, X = wx, Y = wy });
                    _strokePlacedAsset = true;
                    Fire();
                }
                break;

            case EditMode.Gimmick:
                // 敵と同様のロジックでギミックを1個配置する。
                if (!string.IsNullOrEmpty(SelectedAssetId) && !_strokePlacedAsset)
                {
                    var (wx, wy) = ToWorldSnapped(cx, cy);
                    Stage.Gimmicks.Add(new PlacedGimmick { Id = SelectedAssetId, X = wx, Y = wy });
                    _strokePlacedAsset = true;
                    Fire();
                }
                break;

            case EditMode.Item:
                // 敵/ギミックと同様のロジックでアイテムを1個配置する。
                if (!string.IsNullOrEmpty(SelectedAssetId) && !_strokePlacedAsset)
                {
                    var (wx, wy) = ToWorldSnapped(cx, cy);
                    Stage.Items.Add(new PlacedItem { Id = SelectedAssetId, X = wx, Y = wy });
                    _strokePlacedAsset = true;
                    Fire();
                }
                break;

            case EditMode.PlayerStart:
                // プレイヤー開始位置をクリック位置（グリッドスナップ）に更新する。
                var (psx, psy) = ToWorldSnapped(cx, cy);
                Stage.PlayerStartX = psx;
                Stage.PlayerStartY = psy;
                Fire();
                break;

            case EditMode.Goal:
                // ゴール位置をクリック位置（グリッドスナップ）に更新する。
                var (gx, gy) = ToWorldSnapped(cx, cy);
                Stage.GoalX = gx;
                Stage.GoalY = gy;
                Fire();
                break;

            case EditMode.Select:
                // 選択モード：クリック位置に最も近いオブジェクトを選択する。
                DoSelect(cx, cy);
                break;

            case EditMode.TestPlay: // Feature 4
                // Feature: UI改善（友人フィードバック対応）— マップ範囲外をクリックした場合は何も起きないようにする
                int tpCol = (cx + ScrollX) / TILE_SIZE;
                int tpRow = (cy + ScrollY) / TILE_SIZE;
                if (tpCol < 0 || tpCol >= Stage.MapW || tpRow < 0 || tpRow >= Stage.MapH) break;
                // マップ範囲内であれば、テストプレイ開始座標として呼び出し元へ通知する。
                var (tx, ty) = ToWorld(cx, cy);
                TestPlayClicked?.Invoke(this, (tx, ty));
                break;
        }
        // 編集内容を画面に反映するため再描画を要求する。
        Invalidate();
    }

    // 右クリック（および右ドラッグ中）の編集処理。基本的に「削除」操作を担当する。
    private void HandleRight(int cx, int cy)
    {
        if (Stage == null) return;
        if (CurrentMode == EditMode.Tile)
        {
            // メインレイヤーのタイルを消去する（既に空なら何もしない）。
            var (col, row) = ToTile(cx, cy);
            if (Stage.Map[row, col] != 0) { Stage.Map[row, col] = 0; Fire(); Invalidate(); }
        }
        else if (CurrentMode == EditMode.DecoLayerBack) // Feature 1
        {
            // 装飾後景レイヤーのタイルを消去する。
            var (col, row) = ToTile(cx, cy);
            if (Stage.DecoLayerBack[row, col] != 0) { Stage.DecoLayerBack[row, col] = 0; Fire(); Invalidate(); }
        }
        else if (CurrentMode == EditMode.DecoLayerFront) // Feature 1
        {
            // 装飾前景レイヤーのタイルを消去する。
            var (col, row) = ToTile(cx, cy);
            if (Stage.DecoLayerFront[row, col] != 0) { Stage.DecoLayerFront[row, col] = 0; Fire(); Invalidate(); }
        }
        else
        {
            // タイル系以外のモードでは、配置済みオブジェクト（敵/ギミック/アイテム/トリガー）の削除を試みる。
            DoDelete(cx, cy);
        }
    }

    // Feature: 選択/削除ロジックの改善 — 複数重なっている場合はクリック位置に最も近い中心を持つものを優先する
    // （従来は敵→ギミック→アイテム→トリガーの固定順で最初に見つかったものを機械的に採用していた）。
    //
    // Bugfix: 敵/ギミック/アイテムは DrawMarker() により常に (X, Y) 起点の GAME_TILE 四方の
    // 固定サイズアイコンとして描画される（実データのhitboxWidth/Height/Offsetはゲーム内の当たり判定用で、
    // エディタ上の見た目とは無関係）。以前はここで実際のhitboxを判定範囲に使っていたため、
    // hitboxWidth/Heightが0のアセット（enemies.jsonの約半数）は判定矩形の面積がゼロになり
    // クリックでは絶対に選択/削除できず、hitboxOffsetが大きいアセットは判定範囲が見えているアイコンの
    // 位置から大きくズレてしまい、「アイコンをクリックしても消しゴム/選択が反応しない」原因になっていた。
    // 見た目のアイコンと判定範囲を一致させるため、常にマーカーと同じ固定footprintを使う。
    //
    // 各オブジェクトの「クリック判定に使う矩形（ワールド座標系）」を返す。
    // obj : PlacedEnemy / PlacedGimmick / PlacedItem / EventTrigger のいずれか
    // 戻り値 : (左上X, 左上Y, 幅, 高さ)
    private (float x, float y, float w, float h) GetFootprint(object obj)
    {
        switch (obj)
        {
            case PlacedEnemy pe:
                // 敵はDrawMarkerと同じくGAME_TILE四方の固定サイズとして扱う。
                return (pe.X, pe.Y, GAME_TILE, GAME_TILE);
            case PlacedGimmick pg:
                return (pg.X, pg.Y, GAME_TILE, GAME_TILE);
            case PlacedItem pi:
                return (pi.X, pi.Y, GAME_TILE, GAME_TILE);
            case EventTrigger t:
                // トリガーは実際に指定された矩形サイズをそのまま判定範囲として使う。
                return (t.x, t.y, t.width, t.height);
            default:
                return (0, 0, GAME_TILE, GAME_TILE);
        }
    }

    // 選択モードでのクリック処理。クリック位置を含むオブジェクトのうち、中心に最も近いものを選択状態にする。
    private void DoSelect(int cx, int cy)
    {
        if (Stage == null) return;
        var (wx, wy) = ToWorld(cx, cy);
        object? best = null;
        float bestDist = float.MaxValue;

        // クリック位置がオブジェクトのfootprint内にあるかを判定し、範囲内であれば
        // 中心点からの距離（2乗距離、平方根計算を省いて軽量化）を比較して最も近いものを選ぶローカル関数。
        void Consider(object obj)
        {
            var (fx, fy, fw, fh) = GetFootprint(obj);
            // クリック位置がこのオブジェクトの矩形範囲外であれば候補にしない。
            if (wx < fx || wx > fx + fw || wy < fy || wy > fy + fh) return;
            float ccx = fx + fw / 2f, ccy = fy + fh / 2f;
            float dist = (wx - ccx) * (wx - ccx) + (wy - ccy) * (wy - ccy);
            if (dist < bestDist) { bestDist = dist; best = obj; }
        }

        // 敵→ギミック→アイテム→トリガーの順に全オブジェクトを検討する（優先順ではなく単なる走査順）。
        foreach (var e in Stage.Enemies) Consider(e);
        foreach (var gi in Stage.Gimmicks) Consider(gi);
        foreach (var it in Stage.Items) Consider(it);
        foreach (var t in Stage.Triggers) Consider(t);

        // 見つかった最有力候補（またはnull＝選択解除）を選択状態として反映する。
        SelectedObject = best;
        ObjectSelected?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    // 消しゴムモード・右クリック削除で使う、クリック位置に最も近いオブジェクトを1つ削除する処理。
    // DoSelectと同様の「最も近い中心を優先」ロジックを使うが、選択ではなく削除アクションを実行する点が異なる。
    private void DoDelete(int cx, int cy)
    {
        if (Stage == null) return;
        var (wx, wy) = ToWorld(cx, cy);
        float bestDist = float.MaxValue;
        // 実際に削除を実行するためのアクション（見つかった時点では実行せず、最有力候補が確定してから呼び出す）。
        Action? removeBest = null;

        void Consider(object obj, Action remove)
        {
            var (fx, fy, fw, fh) = GetFootprint(obj);
            if (wx < fx || wx > fx + fw || wy < fy || wy > fy + fh) return;
            float ccx = fx + fw / 2f, ccy = fy + fh / 2f;
            float dist = (wx - ccx) * (wx - ccx) + (wy - ccy) * (wy - ccy);
            if (dist < bestDist) { bestDist = dist; removeBest = remove; }
        }

        // 各リストのインデックスをローカル変数にキャプチャしてから、そのインデックスを使った削除アクションを渡す
        // （ラムダ式内でループ変数iを直接使うと、実行時に最終値だけが参照されてしまう問題を避けるため）。
        for (int i = 0; i < Stage.Enemies.Count; i++) { int idx = i; Consider(Stage.Enemies[i], () => Stage.Enemies.RemoveAt(idx)); }
        for (int i = 0; i < Stage.Gimmicks.Count; i++) { int idx = i; Consider(Stage.Gimmicks[i], () => Stage.Gimmicks.RemoveAt(idx)); }
        for (int i = 0; i < Stage.Items.Count; i++) { int idx = i; Consider(Stage.Items[i], () => Stage.Items.RemoveAt(idx)); }
        for (int i = 0; i < Stage.Triggers.Count; i++) { int idx = i; Consider(Stage.Triggers[i], () => Stage.Triggers.RemoveAt(idx)); }

        // 削除対象が見つかった場合のみ実際に削除を実行し、変更通知と再描画を行う。
        if (removeBest != null) { removeBest(); Fire(); Invalidate(); }
    }

    // ステージデータが変更されたことを外部（Form1）へ通知するための共通ヘルパー。
    private void Fire()
    {
        StageModified?.Invoke(this, EventArgs.Empty);
    }
}
