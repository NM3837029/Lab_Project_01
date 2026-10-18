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
        Eraser,          // 消しゴムモード（タイル/装飾/配置オブジェクトを削除する）
        Fill             // タイルのバケツ塗り（クリックした所とつながった同じタイルを、選んでいるタイルで埋める）
    }

    // タイルのフィル・選択範囲の塗りつぶしが、どのタイル層に効くか。
    // 編集モード（CurrentMode）とは別に持つ。フィルや範囲選択のツールを使っている間は、CurrentMode がそれらのツールになるため。
    // Form1 が、Layer 1〜3 のボタンに合わせて切り替える（Layer 4 のときはメイン層）。
    public enum TileLayer { Back, Main, Front }
    public TileLayer FillLayer { get; set; } = TileLayer.Main;

    // 現在アクティブな編集モード。既定値はタイル編集。
    public EditMode CurrentMode { get; set; } = EditMode.Tile;
    // タイルモードで現在選択中のタイルID（パレットから選ばれる）。
    public int SelectedTileId { get; set; } = 1;
    // 敵/ギミック/アイテムモードで現在選択中のアセットID（未選択ならnull）。
    public string? SelectedAssetId { get; set; }

    // 編集対象のステージデータ本体（マップ配列・配置オブジェクト等を保持）。
    // 「元に戻す」「やり直し」「ステージの切り替え」はステージの実体ごと差し替えるため、選択していた物の参照は古くなる。
    // そこで、実体が変わったときに選択（物・タイルの範囲）をすべて外し、画面側（プロパティ表）にも知らせる。
    private StageData? _stage;
    public StageData? Stage
    {
        get => _stage;
        set
        {
            if (ReferenceEquals(_stage, value)) return;
            _stage = value;
            bool hadSelection = _selection.Count > 0;
            _selection.Clear();
            _tileSel = null;
            _marqueeActive = false;
            _dragOrig = null;
            if (hadSelection) ObjectSelected?.Invoke(this, EventArgs.Empty);
        }
    }
    // タイル定義や敵/ギミック/アイテムの定義など、アセット全体の情報。
    public AssetDefinitions? Assets { get; set; }

    // 選択モードで選ばれている配置済みオブジェクト。範囲選択で複数選べる（敵・ギミック・アイテム。トリガーは1つだけ）。
    // 選ばれた順に並ぶ。参照の同一性で数える（同じ内容の別の物を同じとは見ない）。
    private readonly List<object> _selection = new();
    // 選択の先頭（1つだけ選ばれているときはその物、何も無ければ null）。
    // 以前の「1つだけ選ぶ」使い方（一覧からの選択・SelectAndScrollTo）のために残してあり、代入すると、その1つだけの選択になる。
    public object? SelectedObject
    {
        get => _selection.Count > 0 ? _selection[0] : null;
        set { _selection.Clear(); if (value != null) _selection.Add(value); }
    }
    // 選択中のすべての物（Form1 がプロパティ表へ渡す）。
    public IReadOnlyList<object> SelectedObjects => _selection;
    public bool IsSelected(object? o) => o != null && _selection.Any(x => ReferenceEquals(x, o));

    // タイルの選択範囲（タイル座標）。範囲ドラッグで作り、「選択範囲を塗りつぶす」「フィル」の範囲になる。無ければ null。
    private TileRect? _tileSel;
    public TileRect? TileSelection => _tileSel;
    public bool HasTileSelection => _tileSel.HasValue;

    // 範囲ドラッグ（何も無い所から始めた四角）の状態。キャンバス座標。
    private bool _marqueeActive;
    private Point _marqueeStart;
    private Rectangle _marqueeRect;
    private bool _marqueeAdditive; // Ctrl を押して始めた（いまの選択へ足す）
    // 選択した物のドラッグ移動の状態。始めた時点の位置を覚えておき、移動量（32pxの格子）から毎回求め直す。
    private List<(object obj, float x, float y)>? _dragOrig;
    private PointF _dragStartWorld;
    private bool _dragMoved;
    // 複数選択の1つを、ドラッグせずにクリックだけしたとき、そのクリックした物だけの選択に絞る（離したときに決める）
    private object? _clickedSelected;

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

    /// <summary>右クリックメニューの「トリガーを編集...」が選ばれた時のイベント（編集ダイアログは Form1 が開く）</summary>
    public event EventHandler<EventTrigger>? TriggerEditRequested;

    // イベント・トリガー系統は右ボタンで操作する（左ボタンの範囲選択・配置と重ならないように）。
    //   右ボタンを動かさずに離す … 右クリックメニュー（物の上ならその物のメニュー、何も無い所なら「ここにトリガー／開始位置／ゴール」）
    //   何も無い所から右ボタンでドラッグ … トリガーの矩形を作る（離すと編集ダイアログが開く）
    // タイル系のモード（メイン・遠景・近景）の右ボタンは、今までどおりタイルを消す。
    private bool _rightPressActive;      // 右ボタンを押していて、メニュー／トリガー作成のどちらかを離したときに決める状態か
    private Point _rightPressPos;        // 右ボタンを押した位置（キャンバス座標）
    private bool _rightDragged;          // 右ボタンを、4px以上ドラッグしたか
    private bool _rightPressOnEmpty;     // 押した位置に配置物が無かったか（物の上から始めたドラッグでは、トリガーを作らない）

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

    // 敵/ギミック/アイテムのスプライト（画像パス → Image）。
    // 従来これらは種類に関係なく20pxの四角＋絵文字で描かれていて、
    // 「実際に置いたらどのくらいの大きさなのか」がエディタ上で分からなかった。
    // 読めなかった場合も null を覚えておき、毎回ファイルを探しにいかないようにする。
    private readonly Dictionary<string, Image?> _assetImages = new();

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
            if (!string.IsNullOrEmpty(t.sprite))
            {
                // 【重要】tiles.json の sprite は "img/xxx.png" のようにプロジェクトルート起点で書かれている。
                // 以前は assets/ フォルダとだけ結合していたため、常に存在しない
                // "assets/img/xxx.png" を探しに行き、エディタ上でタイル画像が一切表示されなかった
                // （ゲーム本体側の DrawPixel.cpp にも同じ二重プレフィックスのバグがあった）。
                // ルート起点を優先し、見つからない場合だけ従来どおり assets/ 配下も探す。
                string imgPath = System.IO.Path.Combine(AppPaths.ProjectRoot, t.sprite);
                if (!System.IO.File.Exists(imgPath) && !string.IsNullOrEmpty(AssetsPath))
                {
                    imgPath = System.IO.Path.Combine(AssetsPath, t.sprite);
                }
                if (System.IO.File.Exists(imgPath))
                {
                    try
                    {
                        // Image.FromFile はファイルを掴んだままにするため、
                        // 別プロセス（ゲーム本体）が同じ画像を読めなくなる可能性がある。
                        // 一旦メモリへ読み切ってからBitmap化することでファイルロックを残さない。
                        using var fs = new System.IO.FileStream(imgPath, System.IO.FileMode.Open, System.IO.FileAccess.Read);
                        _tileImages[t.id] = new Bitmap(Image.FromStream(fs));
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
        // フィルのときは、塗りつぶし先の層（FillLayer）をアクティブ（明るい）として見せる
        bool isFillMode = CurrentMode == EditMode.Fill;
        bool isL1 = CurrentMode == EditMode.DecoLayerBack || (isFillMode && FillLayer == TileLayer.Back);
        bool isL2 = CurrentMode == EditMode.Tile || (isFillMode && FillLayer == TileLayer.Main);
        bool isL3 = CurrentMode == EditMode.DecoLayerFront || (isFillMode && FillLayer == TileLayer.Front);
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
        //    絵を変えてあるときは、その絵を（ゲームと同じ大きさで）描き、当たり判定の枠を金色で重ねる。
        if (Stage.GoalX >= 0)
        {
            var goalImg = GetAssetImage(Stage.GoalSprite);
            if (goalImg != null) DrawGoalSprite(g, goalImg);
            else DrawMarker(g, Stage.GoalX, Stage.GoalY, "G", Color.FromArgb(255, 215, 0));
        }

        // 7.5. 敵の巡回範囲プレビュー（MZ の移動ルートプレビュー風の点線表示）。
        DrawPatrolRanges(g);

        // 8. 敵 — 実際のスプライトを、実際の大きさで描く。
        // 画像が無い／読めない場合は従来どおりの絵文字マーカーへ落とす。
        foreach (var en in Stage.Enemies)
        {
            var def = Assets?.Enemies.FirstOrDefault(d => d.id == en.Id);
            string icon = AssetIcons.ForEnemy(def?.type_enum ?? -1);
            var bodySize = ObjectWorldSize(def?.width ?? 0, def?.height ?? 0, def?.scale ?? 1f, en.Scale);
            // パーツ（黒目・翼・砲身…）も、配置したときの大きさ・角度のとおりに描く。
            // 以前はパーツを描いていなかったため、テンプレートで作った敵を置いてサイズや角度を変えても、
            // ステージ編集の画面では本体しか見えず、パーツが一緒に変わるのか確かめられなかった。
            // 動き（スクリプト）は再現しないので、パーツの「置いてある位置」の静止画になる。
            float partScale = (def?.scale > 0 ? def.scale : 1f) * (en.Scale > 0 ? en.Scale : 1f);
            DrawPlacedParts(g, en.X, en.Y, bodySize, en.Angle, def?.parts, partScale, behind: true);
            DrawPlacedObject(g, en.X, en.Y, bodySize,
                             def?.sprite, en.Angle, icon,
                             IsSelected(en) ? Color.Magenta : Color.FromArgb(220, 50, 50));
            DrawPlacedParts(g, en.X, en.Y, bodySize, en.Angle, def?.parts, partScale, behind: false);
            DrawLockBadge(g, en.X, en.Y, bodySize, en.EditLocks);
        }

        // 9. ギミック — ギミック定義は width/height を持たず、当たり判定サイズが表示サイズを兼ねている
        foreach (var gi in Stage.Gimmicks)
        {
            var def = Assets?.Gimmicks.FirstOrDefault(d => d.id == gi.Id);
            string icon = AssetIcons.ForGimmick(def?.type_enum ?? -1);
            // 横縦で別々の倍率を掛ける（ゲーム側のローダと同じ計算）
            float gimW = (def?.hitboxWidth > 0 ? def.hitboxWidth : GAME_TILE) * (gi.Scale > 0 ? gi.Scale : 1f);
            float gimH = (def?.hitboxHeight > 0 ? def.hitboxHeight : GAME_TILE) * (gi.ScaleY > 0 ? gi.ScaleY : 1f);
            DrawPlacedObject(g, gi.X, gi.Y, new SizeF(gimW, gimH),
                             def?.sprite, gi.Angle, icon,
                             IsSelected(gi) ? Color.Magenta : Color.FromArgb(50, 120, 220));
            DrawLockBadge(g, gi.X, gi.Y, new SizeF(gimW, gimH), gi.EditLocks);
        }

        // 10. アイテム（ギミックと同じくhitboxサイズが表示サイズ）
        foreach (var it in Stage.Items)
        {
            var def = Assets?.Items.FirstOrDefault(d => d.id == it.Id);
            string icon = AssetIcons.ForItem(def?.type_enum ?? -1);
            DrawPlacedObject(g, it.X, it.Y, ObjectWorldSize(def?.hitboxWidth ?? 0, def?.hitboxHeight ?? 0, 1f, it.Scale),
                             def?.sprite, it.Angle, icon,
                             IsSelected(it) ? Color.Magenta : Color.FromArgb(255, 200, 0));
            DrawLockBadge(g, it.X, it.Y, ObjectWorldSize(def?.hitboxWidth ?? 0, def?.hitboxHeight ?? 0, 1f, it.Scale), it.EditLocks);
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

        // 12.5. タイルの選択範囲と、範囲ドラッグ中の四角。選択ツール・フィルのときだけ見せる
        // （ほかのツールのときに残っていると、何の範囲か分からなくなるため）。
        if (CurrentMode == EditMode.Select || CurrentMode == EditMode.Fill)
            DrawSelectionOverlays(g);

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
            bool selected = IsSelected(en);

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
            if (IsSelected(t))
            {
                using var selPen = new Pen(Color.Magenta, 2.5f);
                g.DrawRectangle(selPen, px, py, pw, ph);
            }
            // トリガーID表示（矩形の左上に小さく描画）
            using var font = new Font("Meiryo UI", 6, FontStyle.Bold);
            using var tb = new SolidBrush(Color.OrangeRed);
            g.DrawString($"T:{t.id}", font, tb, px + 2, py + 2);
        }
    }

    // タイルの選択範囲（水色の破線）と、範囲ドラッグ中の四角（青の破線）を描く。
    private void DrawSelectionOverlays(Graphics g)
    {
        if (_tileSel.HasValue)
        {
            var r = _tileSel.Value;
            var rect = new Rectangle(r.Col * TILE_SIZE - ScrollX, r.Row * TILE_SIZE - ScrollY, r.Cols * TILE_SIZE, r.Rows * TILE_SIZE);
            using var fill = new SolidBrush(Color.FromArgb(40, 0, 180, 255));
            using var pen = new Pen(Color.FromArgb(230, 0, 140, 220), 2) { DashStyle = DashStyle.Dash };
            g.FillRectangle(fill, rect);
            g.DrawRectangle(pen, rect);
        }
        if (_marqueeActive && (_marqueeRect.Width > 0 || _marqueeRect.Height > 0))
        {
            using var fill = new SolidBrush(Color.FromArgb(35, 60, 90, 255));
            using var pen = new Pen(Color.RoyalBlue, 1.5f) { DashStyle = DashStyle.Dash };
            g.FillRectangle(fill, _marqueeRect);
            g.DrawRectangle(pen, _marqueeRect);
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
            EditMode.Fill => $"🪣 フィル → {FillLayerName()}" + (_tileSel.HasValue ? "（選択範囲の中だけ）" : ""),
            EditMode.Select => SelectIndicatorText(),
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
    // 配置物のワールド上の表示サイズを求める。
    // 定義が引けない／サイズ未設定のアセットはマス目1つぶんとして扱う
    // （0を返すと描画も当たり判定も消えてしまうため）。
    private static SizeF ObjectWorldSize(int defW, int defH, float defScale, float placedScale)
    {
        float w = (defW > 0 ? defW : GAME_TILE) * (defScale > 0 ? defScale : 1f) * (placedScale > 0 ? placedScale : 1f);
        float h = (defH > 0 ? defH : GAME_TILE) * (defScale > 0 ? defScale : 1f) * (placedScale > 0 ? placedScale : 1f);
        return new SizeF(w, h);
    }

    // 画像パスからスプライトを取得する（キャッシュ付き）。読めなければ null。
    // ファイルを掴んだままにするとゲーム本体が同じ画像を読めなくなるため、
    // タイル画像と同じく FileStream 経由で読み切ってから Bitmap にする。
    private Image? GetAssetImage(string? spritePath)
    {
        if (string.IsNullOrEmpty(spritePath)) return null;
        if (_assetImages.TryGetValue(spritePath, out var cached)) return cached;
        Image? img = null;
        string p = System.IO.Path.Combine(AppPaths.ProjectRoot, spritePath);
        if (!System.IO.File.Exists(p) && !string.IsNullOrEmpty(AssetsPath))
            p = System.IO.Path.Combine(AssetsPath, spritePath);
        if (System.IO.File.Exists(p))
        {
            try
            {
                using var fs = new System.IO.FileStream(p, System.IO.FileMode.Open, System.IO.FileAccess.Read);
                img = new Bitmap(Image.FromStream(fs));
            }
            catch { img = null; }
        }
        _assetImages[spritePath] = img; // 読めなかったことも覚えて再探索を避ける
        return img;
    }

    // 配置済みの敵のパーツを描く。ゲーム本体と同じく、パーツは本体の左上を原点とした位置（offset）を
    // 本体の倍率ぶん拡大し、本体の中心を軸に本体と一緒に回す。zOrder が負のパーツは本体の奥（behind=true）、
    // 0以上は手前（behind=false）に描く。
    private void DrawPlacedParts(Graphics g, float worldX, float worldY, SizeF bodySize, float angleRad,
                                 List<PartDef>? parts, float scale, bool behind)
    {
        if (parts == null || parts.Count == 0) return;
        var body = ToScreenRect(worldX, worldY, bodySize.Width, bodySize.Height);
        if (body.Right < -400 || body.Left > Width + 400 || body.Bottom < -400 || body.Top > Height + 400) return;

        var saved = g.Save();
        if (angleRad != 0f)
        {
            float cx = body.X + body.Width / 2f, cy = body.Y + body.Height / 2f;
            g.TranslateTransform(cx, cy);
            g.RotateTransform(angleRad * 180f / (float)Math.PI);
            g.TranslateTransform(-cx, -cy);
        }
        foreach (var p in parts)
        {
            if ((p.zOrder < 0) != behind) continue;
            var img = GetAssetImage(p.sprite);
            if (img == null) continue;
            // 幅・高さが0のパーツは、画像の原寸に倍率をかけた大きさで描かれる（ゲーム側と同じ）
            float pw = (p.width > 0 ? p.width : img.Width) * p.scale * scale;
            float ph = (p.height > 0 ? p.height : img.Height) * p.scale * scale;
            var r = ToScreenRect(worldX + p.offsetX * scale, worldY + p.offsetY * scale, pw, ph);
            if (r.Width < 2) r.Width = 2;
            if (r.Height < 2) r.Height = 2;
            // パーツの色と加算合成（色を付けたパーツが、ステージ上でも色付きで見える）
            PartColor.TryParse(p.color, out float pcr, out float pcg, out float pcb);
            using var pia = PartColor.MakeAttributes(pcr, pcg, pcb, p.blend == "add" ? 0.8f : 1f);
            if (pia != null) g.DrawImage(img, Rectangle.Round(r), 0, 0, img.Width, img.Height, GraphicsUnit.Pixel, pia);
            else g.DrawImage(img, r);
        }
        g.Restore(saved);
    }

    // 配置済みオブジェクトを1つ描く。
    // 画像があれば実サイズで、無ければ従来どおりの絵文字マーカーで描く。
    private void DrawPlacedObject(Graphics g, float worldX, float worldY, SizeF worldSize,
                                  string? spritePath, float angleRad, string icon, Color color)
    {
        var img = GetAssetImage(spritePath);
        if (img == null)
        {
            DrawMarker(g, worldX, worldY, icon, color, 11f);
            return;
        }
        var rect = ToScreenRect(worldX, worldY, worldSize.Width, worldSize.Height);
        // 画面外は描かない（大きなステージでの描画コストを抑える）
        if (rect.Right < 0 || rect.Left > Width || rect.Bottom < 0 || rect.Top > Height) return;
        // つぶれて見えなくならないよう最低サイズを与える
        if (rect.Width < 4) rect.Width = 4;
        if (rect.Height < 4) rect.Height = 4;

        var saved = angleRad != 0f ? g.Save() : null;
        if (angleRad != 0f)
        {
            // ゲーム本体と同じく中心を軸に回す
            float cx = rect.X + rect.Width / 2f, cy = rect.Y + rect.Height / 2f;
            g.TranslateTransform(cx, cy);
            g.RotateTransform(angleRad * 180f / (float)Math.PI);
            g.TranslateTransform(-cx, -cy);
        }
        g.DrawImage(img, rect);
        if (saved != null) g.Restore(saved);

        // 選択中は枠を重ねて、どれを掴んでいるか分かるようにする
        if (color == Color.Magenta)
        {
            using var pen = new Pen(color, 2);
            g.DrawRectangle(pen, rect);
        }
    }

    // ゴールの絵を描く。ゲームの表示（48px×倍率、中心はゴール位置の中心）に合わせ、
    // 当たり判定（32px×倍率）の枠を金色の細い線で重ねる。
    private void DrawGoalSprite(Graphics g, Image img)
    {
        if (Stage == null) return;
        float scale = (float)TILE_SIZE / GAME_TILE;
        float k = Stage.GoalScale <= 0 ? 1.0f : Stage.GoalScale;
        float cxw = Stage.GoalX + GAME_TILE / 2f, cyw = Stage.GoalY + GAME_TILE / 2f;   // ゴールの中心（ワールド）
        float drawPx = GAME_TILE * 1.5f * k * scale;
        float hitPx = GAME_TILE * k * scale;
        float cx = cxw * scale - ScrollX, cy = cyw * scale - ScrollY;
        g.DrawImage(img, cx - drawPx / 2, cy - drawPx / 2, drawPx, drawPx);
        using var pen = new Pen(Color.FromArgb(220, 255, 215, 0), 2) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash };
        g.DrawRectangle(pen, cx - hitPx / 2, cy - hitPx / 2, hitPx, hitPx);
    }

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

        // 選択ツール：クリックで1つ選ぶ／Ctrl+クリックで足す・外す／何も無い所からのドラッグで範囲選択／選択中の物のドラッグで移動。
        // 離したときの保存・履歴は、下の一般の処理（isDragging → EditCompleted）にそのまま乗せる。
        if (CurrentMode == EditMode.Select && e.Button == MouseButtons.Left)
        {
            isDragging = true;
            SelectDown(e);
            return;
        }

        // 左クリック：ドラッグ開始フラグを立て、ストローク内配置済みフラグをリセットしてから編集処理を実行する。
        if (e.Button == MouseButtons.Left) { isDragging = true; _strokePlacedAsset = false; HandleLeft(e.X, e.Y); }
        // 右ボタン：タイル系のモードは、今までどおり押した瞬間からタイルを消す（ドラッグで連続して消せる）。
        else if (e.Button == MouseButtons.Right && IsTileEraseMode)
        {
            isRightDrag = true;
            HandleRight(e.X, e.Y);
        }
        // それ以外のモードは、離したときに「メニュー」か「トリガーの矩形」かを決める（ここでは覚えるだけ）。
        else if (e.Button == MouseButtons.Right)
        {
            isRightDrag = true;
            _rightPressActive = true;
            _rightPressPos = e.Location;
            _rightDragged = false;
            _rightPressOnEmpty = FindObjectAt(e.X, e.Y) == null;
        }
    }

    // マウス移動時の処理。ドラッグ中であれば継続して編集を適用し、モードに応じてカーソル形状も切り替える。
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (Stage == null) return;

        // 右ボタンのドラッグ：何も無い所から始めていれば、トリガーの矩形を伸ばす（プレビューはトリガーのドラッグと同じ描き方）
        if (_rightPressActive && e.Button == MouseButtons.Right)
        {
            if (!_rightDragged && (Math.Abs(e.X - _rightPressPos.X) >= 4 || Math.Abs(e.Y - _rightPressPos.Y) >= 4))
                _rightDragged = true;
            if (_rightDragged && _rightPressOnEmpty)
            {
                _isTriggerDragging = true;
                _triggerDragRect = new Rectangle(Math.Min(_rightPressPos.X, e.X), Math.Min(_rightPressPos.Y, e.Y),
                                                 Math.Abs(e.X - _rightPressPos.X), Math.Abs(e.Y - _rightPressPos.Y));
                Invalidate();
            }
            return;
        }

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
        else if (CurrentMode == EditMode.Fill) Cursor = Cursors.Cross;
        else Cursor = Cursors.Default;

        // 選択ツールのドラッグ（範囲選択・選択した物の移動）
        if (isDragging && CurrentMode == EditMode.Select) { SelectMove(e); return; }

        // 左ドラッグ中なら継続して配置処理を行う（タイルの連続塗り等）。
        if (isDragging) HandleLeft(e.X, e.Y);
        // 右ドラッグ中はタイルモードに限り連続削除を行う（他モードでの誤削除を防ぐため）。
        else if (isRightDrag && CurrentMode == EditMode.Tile) HandleRight(e.X, e.Y);
    }

    // マウスボタン解放時の処理。ドラッグ状態を終了し、必要であれば確定処理（トリガー生成・編集完了通知）を行う。
    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);

        // 右ボタンを離した：何も無い所からのドラッグならトリガーを作る。それ以外（動かしていない／物の上から）は右クリックメニュー。
        // メニューの各操作は、それぞれ保存・履歴を知らせるので、ここでは EditCompleted を出さない。
        if (_rightPressActive && e.Button == MouseButtons.Right)
        {
            _rightPressActive = false;
            isDragging = isRightDrag = false;
            var rect = _triggerDragRect;
            bool makeTrigger = _rightDragged && _rightPressOnEmpty && rect.Width > 4 && rect.Height > 4;
            _isTriggerDragging = false;
            _triggerDragRect = Rectangle.Empty;
            if (makeTrigger && Stage != null)
            {
                var (wx1, wy1) = ToWorld(rect.Left, rect.Top);
                var (wx2, wy2) = ToWorld(rect.Right, rect.Bottom);
                TriggerPlaced?.Invoke(this, new EventTrigger { id = NextTriggerId(), x = wx1, y = wy1, width = wx2 - wx1, height = wy2 - wy1 });
            }
            else
            {
                ShowObjectMenu(_rightPressPos.X, _rightPressPos.Y);
            }
            Invalidate();
            return;
        }

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
                    id = NextTriggerId(),
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

        // 選択ツールのドラッグの確定（範囲選択の結果・移動の確定）。保存と履歴は、すぐ下の EditCompleted で行う
        if (isDragging && CurrentMode == EditMode.Select && e.Button == MouseButtons.Left) SelectUp(e);

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

            case EditMode.Fill:
                // バケツ塗り。ドラッグ中に何度も塗らないよう、1回のクリック（ストローク）につき1回だけ。
                // 選択範囲があれば、その中だけで塗る（範囲の外をクリックしたときは何もしない）。
                if (!_strokePlacedAsset)
                {
                    _strokePlacedAsset = true;
                    int fc = (cx + ScrollX) / TILE_SIZE, fr = (cy + ScrollY) / TILE_SIZE;
                    if (cx + ScrollX >= 0 && cy + ScrollY >= 0 && fc < Stage.MapW && fr < Stage.MapH)
                    {
                        int n = TileFill.FloodFill(FillLayerArray(), fc, fr, SelectedTileId, _tileSel);
                        if (n > 0) Fire();
                    }
                }
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
            // タイル系以外のモードでは、クリックした配置物に対するメニューを出す。
            //
            // 以前はここで即座に削除していた。確認も取り消しも無いうえ、
            // 「サイズや角度を調整したい」ときに触れる入口がどこにも無かった。
            // 削除はメニューの中へ移してある（消しゴムツールでの削除は今までどおり）。
            ShowObjectMenu(cx, cy);
        }
    }

    // メイン・遠景・近景のタイル編集中か（右ボタンがタイル消しになるモード）
    private bool IsTileEraseMode => CurrentMode == EditMode.Tile || CurrentMode == EditMode.DecoLayerBack || CurrentMode == EditMode.DecoLayerFront;

    // 新しいトリガーのID（trigger_001 形式）。削除で欠番があっても、既存と重ならない最初の番号を使う
    private string NextTriggerId()
    {
        int n = 1;
        if (Stage != null) while (Stage.Triggers.Any(t => t.id == $"trigger_{n:D3}")) n++;
        return $"trigger_{n:D3}";
    }

    // 何も無い所を右クリックしたときのメニュー。イベント系統（トリガー・開始位置・ゴール）は、ここと右ドラッグで作る。
    private void ShowEmptyMenu(int cx, int cy)
    {
        if (Stage == null) return;
        var menu = new ContextMenuStrip();
        var (wx, wy) = ToWorldSnapped(cx, cy);

        var miTrig = new ToolStripMenuItem("ここにトリガーを作る（大きさは、何も無い所を右ボタンでドラッグしても決められます）");
        miTrig.Click += (s, e) =>
            TriggerPlaced?.Invoke(this, new EventTrigger { id = NextTriggerId(), x = wx, y = wy, width = 96f, height = 96f });
        menu.Items.Add(miTrig);
        menu.Items.Add(new ToolStripSeparator());

        var miStart = new ToolStripMenuItem("ここをプレイヤーの開始位置にする");
        miStart.Click += (s, e) =>
        {
            Stage.PlayerStartX = wx; Stage.PlayerStartY = wy;
            Fire(); Invalidate();
            EditCompleted?.Invoke(this, EventArgs.Empty);
        };
        menu.Items.Add(miStart);
        var miGoal = new ToolStripMenuItem("ここをゴールにする");
        miGoal.Click += (s, e) =>
        {
            Stage.GoalX = wx; Stage.GoalY = wy;
            Fire(); Invalidate();
            EditCompleted?.Invoke(this, EventArgs.Empty);
        };
        menu.Items.Add(miGoal);
        // ゴールの絵と大きさ。ゴールが置いてあるときだけ使える（置いてないと、変える対象が無い）
        var miGoalLook = new ToolStripMenuItem("ゴールの見た目を変える...") { Enabled = Stage.GoalX >= 0 };
        miGoalLook.Click += (s, e) => EditGoalAppearance();
        menu.Items.Add(miGoalLook);

        if (_tileSel.HasValue)
        {
            menu.Items.Add(new ToolStripSeparator());
            AddTileSelectionMenuItems(menu);
        }
        menu.Show(PointToScreen(new Point(cx, cy)));
    }

    // 「ゴールの見た目を変える」ダイアログを開き、OKなら絵と大きさをステージへ反映する（保存と履歴も知らせる）。
    private void EditGoalAppearance()
    {
        if (Stage == null || Stage.GoalX < 0) return;
        using var dlg = new GoalAppearanceForm(AppPaths.ProjectRoot, Stage.GoalSprite, Stage.GoalScale);
        if (dlg.ShowDialog(FindForm()) != DialogResult.OK) return;
        _assetImages.Remove(Stage.GoalSprite ?? "");      // 同じ名前で絵を差し替えたときのために、読み込み済みの絵を捨てる
        _assetImages.Remove(dlg.ResultSprite);
        Stage.GoalSprite = dlg.ResultSprite;
        Stage.GoalScale = dlg.ResultScale;
        Fire(); Invalidate();
        EditCompleted?.Invoke(this, EventArgs.Empty);
    }

    // 右クリックした配置物に対するメニューを出す。
    // 何も無い場所ではメニューを出さない（空振りで出ると何に対する操作か紛らわしいため）。
    private void ShowObjectMenu(int cx, int cy)
    {
        if (Stage == null) return;
        object? target = FindObjectAt(cx, cy);
        if (target == null)
        {
            // 何も無い所：トリガー・開始位置・ゴールを作るメニュー（タイルの選択範囲があれば、その項目も出る）
            ShowEmptyMenu(cx, cy);
            return;
        }

        // 複数選択のうちの1つを右クリックしたときは、選択を保ったまま、全員に対するメニューを出す
        // （選び直してしまうと、まとめて選んだのが1つに縮んで、同時に編集できなくなる）。
        if (_selection.Count > 1 && IsSelected(target) && target is not EventTrigger)
        {
            ShowMultiMenu(cx, cy);
            return;
        }

        // メニューを出す前に選択しておく。どれに対する操作なのかが見た目で分かるようにするため。
        SelectedObject = target;
        ObjectSelected?.Invoke(this, EventArgs.Empty);
        Invalidate();

        var menu = new ContextMenuStrip();
        // トリガー：編集ダイアログは、選んだだけでは開かない（範囲選択などの邪魔になるため）。ここから開く。
        if (target is EventTrigger trig)
        {
            var miTrigEdit = new ToolStripMenuItem("トリガーを編集...");
            miTrigEdit.Click += (s, e) => TriggerEditRequested?.Invoke(this, trig);
            menu.Items.Add(miTrigEdit);
            menu.Items.Add(new ToolStripSeparator());
        }
        // トリガーには大きさ・角度の概念が無いので、その場合は削除だけを出す。
        bool canTransform = target is PlacedEnemy || target is PlacedGimmick || target is PlacedItem;
        if (canTransform)
        {
            var miEdit = new ToolStripMenuItem("大きさ・角度を編集...");
            miEdit.Click += (s, e) => EditPlacedTransform(target);
            menu.Items.Add(miEdit);
            // この1つだけ、ゲーム内でプレイヤーにできる編集操作（移動・拡縮・回転…）を許可／禁止する。
            // 設定済みのものは、メニューにも件数を出して「何かが設定されている」ことが分かるようにする。
            var curLocks = GetEditLocks(target);
            string lockText = "編集の可否を設定...";
            if (curLocks != null && curLocks.Any())
                lockText += $"（禁止 {curLocks.DenyCount()} ／ 許可 {curLocks.AllowCount()}）";
            var miLocks = new ToolStripMenuItem(lockText);
            miLocks.Click += (s, e) => EditPlacedLocks(target);
            menu.Items.Add(miLocks);
            menu.Items.Add(new ToolStripSeparator());
        }
        if (_tileSel.HasValue) { AddTileSelectionMenuItems(menu); menu.Items.Add(new ToolStripSeparator()); }
        var miDel = new ToolStripMenuItem("削除");
        miDel.Click += (s, e) =>
        {
            if (target is PlacedEnemy pe) Stage.Enemies.Remove(pe);
            else if (target is PlacedGimmick pg) Stage.Gimmicks.Remove(pg);
            else if (target is PlacedItem pi) Stage.Items.Remove(pi);
            else if (target is EventTrigger tr) Stage.Triggers.Remove(tr);
            SelectedObject = null;
            Fire(); Invalidate();
            // メニューからの操作はマウスのドラッグではないので OnMouseUp の EditCompleted が来ない。
            // ここで知らせないと、保存も「元に戻す」への登録も、次にマウスを操作するまで行われない。
            EditCompleted?.Invoke(this, EventArgs.Empty);
        };
        menu.Items.Add(miDel);
        menu.Show(PointToScreen(new Point(cx, cy)));
    }

    // 複数選択中の右クリックメニュー。大きさ・角度／編集の可否／削除を、選択中の全員に対して行う。
    private void ShowMultiMenu(int cx, int cy)
    {
        var menu = new ContextMenuStrip();
        int n = _selection.Count;
        var miEdit = new ToolStripMenuItem($"大きさ・角度を編集（{n}個）...");
        miEdit.Click += (s, e) => EditSelectedTransform();
        menu.Items.Add(miEdit);
        var miLocks = new ToolStripMenuItem($"編集の可否を設定（{n}個）...");
        miLocks.Click += (s, e) => EditSelectedLocks();
        menu.Items.Add(miLocks);
        menu.Items.Add(new ToolStripSeparator());
        if (_tileSel.HasValue) { AddTileSelectionMenuItems(menu); menu.Items.Add(new ToolStripSeparator()); }
        var miDel = new ToolStripMenuItem($"削除（{n}個）");
        miDel.Click += (s, e) => DeleteSelection();
        menu.Items.Add(miDel);
        menu.Show(PointToScreen(new Point(cx, cy)));
    }

    // タイルの選択範囲に対するメニュー項目（塗りつぶし・空にする・選択を解除）
    private void AddTileSelectionMenuItems(ContextMenuStrip menu)
    {
        var miFill = new ToolStripMenuItem($"選択範囲を、選んでいるタイル（{SelectedTileId}）で塗りつぶす");
        miFill.Click += (s, e) => FillTileSelection();
        menu.Items.Add(miFill);
        var miClear = new ToolStripMenuItem("選択範囲のタイルを空にする");
        miClear.Click += (s, e) => ClearTilesInSelection();
        menu.Items.Add(miClear);
        var miOff = new ToolStripMenuItem("選択を解除");
        miOff.Click += (s, e) => { ClearAllSelection(); };
        menu.Items.Add(miOff);
    }

    // クリック位置にある配置物を1つ返す（DoSelect と同じ「中心がいちばん近いもの」の選び方）。
    private object? FindObjectAt(int cx, int cy)
    {
        if (Stage == null) return null;
        var (wx, wy) = ToWorld(cx, cy);
        object? best = null;
        float bestDist = float.MaxValue;
        void Consider(object obj)
        {
            var (fx, fy, fw, fh) = GetFootprint(obj);
            if (wx < fx || wx > fx + fw || wy < fy || wy > fy + fh) return;
            float ccx = fx + fw / 2f, ccy = fy + fh / 2f;
            float dist = (wx - ccx) * (wx - ccx) + (wy - ccy) * (wy - ccy);
            if (dist < bestDist) { bestDist = dist; best = obj; }
        }
        foreach (var e in Stage.Enemies) Consider(e);
        foreach (var gi in Stage.Gimmicks) Consider(gi);
        foreach (var it in Stage.Items) Consider(it);
        foreach (var t in Stage.Triggers) Consider(t);
        return best;
    }

    // 選択中の全員の大きさ・角度を、まとめて編集する（右クリックメニュー）。
    // ダイアログの各項目に「変更しない」が付いていて、触った項目だけを全員へ書き込む。
    // ギミックの縦の大きさは、縦の大きさを持つ物（ギミック）にだけ、
    // 角度は、AIが角度を使う型のギミック（自動回転する橋など）を除いて書き込む。
    private void EditSelectedTransform()
    {
        var targets = _selection.Where(o => o is PlacedEnemy || o is PlacedGimmick || o is PlacedItem).ToList();
        if (targets.Count == 0) return;
        if (targets.Count == 1) { EditPlacedTransform(targets[0]); return; }

        var first = targets[0];
        var propScale = first.GetType().GetProperty("Scale");
        var propAngle = first.GetType().GetProperty("Angle");
        var propScaleY = first.GetType().GetProperty("ScaleY");
        float curScale = (float)(propScale?.GetValue(first) ?? 1.0f);
        float curAngle = (float)(propAngle?.GetValue(first) ?? 0f);
        bool anyGimmick = targets.Any(o => o is PlacedGimmick);
        float curScaleY = propScaleY != null ? (float)(propScaleY.GetValue(first) ?? 1.0f) : curScale;
        // 角度を変えられる対象が1つも無ければ、角度の欄は触れなくする
        bool allAngleAiOwned = targets.All(IsAngleAiOwned);

        using var dlg = new PlacedTransformForm(curScale, curScaleY, curAngle,
                                                separateAxes: anyGimmick,
                                                angleIsAiOwned: allAngleAiOwned,
                                                multiCount: targets.Count);
        if (dlg.ShowDialog(FindForm()) != DialogResult.OK) return;
        if (!dlg.ApplyScale && !dlg.ApplyScaleY && !dlg.ApplyAngle) return; // 何も変えない
        foreach (var t in targets)
        {
            if (dlg.ApplyScale) t.GetType().GetProperty("Scale")?.SetValue(t, dlg.ResultScale);
            if (dlg.ApplyScaleY) t.GetType().GetProperty("ScaleY")?.SetValue(t, dlg.ResultScaleY);
            if (dlg.ApplyAngle && !IsAngleAiOwned(t)) t.GetType().GetProperty("Angle")?.SetValue(t, dlg.ResultAngle);
        }
        Fire(); Invalidate();
        EditCompleted?.Invoke(this, EventArgs.Empty); // 1回の操作＝履歴1つ
    }

    // 角度をAIが自前の状態に使っている型のギミックか（そうなら、角度を編集として変えてはいけない）
    private bool IsAngleAiOwned(object target)
    {
        if (target is not PlacedGimmick pg) return false;
        int te = Assets?.Gimmicks.FirstOrDefault(d => d.id == pg.Id)?.type_enum ?? -1;
        return GimmickAngleIsAiOwned(te);
    }

    // 選択中の全員の「編集の可否」を、まとめて設定する。「変更しない」の操作は、各自の今の設定を残す。
    private void EditSelectedLocks()
    {
        var targets = _selection.Where(o => o is PlacedEnemy || o is PlacedGimmick || o is PlacedItem).ToList();
        if (targets.Count == 0) return;
        if (targets.Count == 1) { EditPlacedLocks(targets[0]); return; }

        bool hasItem = targets.Any(o => o is PlacedItem);
        using var dlg = new PlacedEditLockForm($"選択中の {targets.Count}個", new PlacedEditLocks(), new bool[7], hasItem, multi: true);
        if (dlg.ShowDialog(FindForm()) != DialogResult.OK) return;
        bool any = false;
        foreach (var t in targets)
        {
            var locks = GetEditLocks(t);
            if (locks == null) continue;
            for (int i = 0; i < 7; i++)
                if (dlg.Changed[i]) { locks.Set(i, dlg.Result.Get(i)); any = true; }
        }
        if (!any) return;
        Fire(); Invalidate();
        EditCompleted?.Invoke(this, EventArgs.Empty);
    }

    // 配置ごとの大きさ・角度を編集する小さなダイアログを出す。
    // 対象の型が違ってもプロパティ名は同じ（Scale / Angle）なので、
    // 3種類ぶんの分岐を書かずにリフレクションで読み書きする。
    private void EditPlacedTransform(object target)
    {
        var propScale = target.GetType().GetProperty("Scale");
        var propAngle = target.GetType().GetProperty("Angle");
        if (propScale == null || propAngle == null) return;
        // ギミックだけ縦の倍率を別に持つ（ゲーム内の編集ツールが横=Sドラッグ／縦=Wドラッグで
        // 別々に変える作りなので、配置ごとの初期値も同じ粒度にしてある）。
        var propScaleY = target.GetType().GetProperty("ScaleY");
        float curScale = (float)(propScale.GetValue(target) ?? 1.0f);
        float curScaleY = propScaleY != null ? (float)(propScaleY.GetValue(target) ?? 1.0f) : curScale;
        float curAngle = (float)(propAngle.GetValue(target) ?? 0f);

        // 角度をAIが自前の状態として使っている型（自動回転する橋・ちくわブロック等）は
        // ゲーム内でも回転できないので、この画面でも触らせない。
        bool angleIsAiOwned = false;
        if (target is PlacedGimmick pgm)
        {
            int te = Assets?.Gimmicks.FirstOrDefault(d => d.id == pgm.Id)?.type_enum ?? -1;
            angleIsAiOwned = GimmickAngleIsAiOwned(te);
        }

        using var dlg = new PlacedTransformForm(curScale, curScaleY, curAngle,
                                                separateAxes: propScaleY != null,
                                                angleIsAiOwned: angleIsAiOwned);
        if (dlg.ShowDialog(FindForm()) != DialogResult.OK) return;
        propScale.SetValue(target, dlg.ResultScale);
        propScaleY?.SetValue(target, dlg.ResultScaleY);
        propAngle.SetValue(target, dlg.ResultAngle);
        Fire(); Invalidate();
        EditCompleted?.Invoke(this, EventArgs.Empty); // メニュー操作の結果も保存・履歴へ（上の削除と同じ理由）
    }

    // 配置物の「編集の可否」の上書きを返す（敵・ギミック・アイテム以外は null）
    private static PlacedEditLocks? GetEditLocks(object target) => target switch
    {
        PlacedEnemy e => e.EditLocks,
        PlacedGimmick g => g.EditLocks,
        PlacedItem i => i.EditLocks,
        _ => null,
    };

    // 右クリックメニュー「編集の可否を設定...」。この配置物だけ、7つの編集操作を「既定／許可／禁止」から選ぶ。
    // 「既定」はアセット側の設定（noMove 等）に従う。アセットが禁止していても、この1つだけ許可することもできる。
    private void EditPlacedLocks(object target)
    {
        var locks = GetEditLocks(target);
        if (locks == null || Assets == null) return;

        // アセット側の設定（ダイアログに「アセットは禁止」と出すため）と、表示名
        bool[] assetLocks = new bool[7];
        string name = "";
        bool isItem = target is PlacedItem;
        if (target is PlacedEnemy pe)
        {
            var d = Assets.Enemies.FirstOrDefault(x => x.id == pe.Id);
            name = d?.name ?? pe.Id;
            if (d != null) assetLocks = new[] { d.noScale, d.noRotate, d.noMove, d.noFlip, d.noPause, d.noRewind, d.noSpeed };
        }
        else if (target is PlacedGimmick pg)
        {
            var d = Assets.Gimmicks.FirstOrDefault(x => x.id == pg.Id);
            name = d?.name ?? pg.Id;
            if (d != null) assetLocks = new[] { d.noScale, d.noRotate, d.noMove, d.noFlip, d.noPause, d.noRewind, d.noSpeed };
        }
        else if (target is PlacedItem pi)
        {
            var d = Assets.Items.FirstOrDefault(x => x.id == pi.Id);
            name = d?.name ?? pi.Id;
            if (d != null) assetLocks = new[] { d.noScale, d.noRotate, d.noMove, d.noFlip, d.noPause, d.noRewind, d.noSpeed };
        }

        using var dlg = new PlacedEditLockForm(name, locks, assetLocks, isItem);
        if (dlg.ShowDialog(FindForm()) != DialogResult.OK) return;
        for (int i = 0; i < 7; i++) locks.Set(i, dlg.Result.Get(i));
        Fire(); Invalidate();
        EditCompleted?.Invoke(this, EventArgs.Empty);
    }

    // 編集の可否を上書きした配置物の右上に、小さな印を出す（禁止があれば赤の「禁n」、許可だけなら緑の「許n」）。
    // 設定したものをステージ画面で見分けられないと、どれに設定したか分からなくなるため。
    private void DrawLockBadge(Graphics g, float worldX, float worldY, SizeF worldSize, PlacedEditLocks locks)
    {
        if (!locks.Any()) return;
        var rect = ToScreenRect(worldX, worldY, worldSize.Width, worldSize.Height);
        if (rect.Right < 0 || rect.Left > Width || rect.Bottom < 0 || rect.Top > Height) return;
        int deny = locks.DenyCount(), allow = locks.AllowCount();
        string text = deny > 0 ? $"禁{deny}" : $"許{allow}";
        var back = deny > 0 ? Color.FromArgb(210, 200, 40, 40) : Color.FromArgb(210, 40, 150, 70);
        using var font = new Font("Meiryo UI", 7f, FontStyle.Bold);
        var size = g.MeasureString(text, font);
        var badge = new RectangleF(rect.Right - size.Width + 2, rect.Top - 4, size.Width, size.Height - 2);
        using var br = new SolidBrush(back);
        g.FillRectangle(br, badge);
        g.DrawString(text, font, Brushes.White, badge.X, badge.Y - 1);
    }

    // 角度をAIが自前の状態に使っているギミックの種類。
    // これらは角度が「今どこまで動いたか」を表す内部状態なので、編集として回してはいけない。
    // ゲーム側の GimmickAngleIsAiOwned（DrawPixel.cpp）と同じ並びを保つこと。
    private static bool GimmickAngleIsAiOwned(int typeEnum)
    {
        return typeEnum == 1     // GIMMICK_ROTATING_BRIDGE（自動で回る橋）
            || typeEnum == 11    // GIMMICK_CHIKUWA_BLOCK（ちくわブロック）
            || typeEnum == 24;   // GIMMICK_CUSTOM_SCRIPT（挙動スクリプトが角度を握る）
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

    // ===== 選択ツール：複数選択・範囲選択・ドラッグ移動 =====

    // 画面に描いている大きさの矩形（ワールド座標）。範囲選択で「囲んだ物」を決めるのに使う。
    // 描く大きさ（OnPaint と同じ計算）に、小さすぎて掴めなくならない最小の大きさを与える。
    private (float x, float y, float w, float h) GetDrawnRect(object obj)
    {
        SizeF size;
        float x, y;
        switch (obj)
        {
            case PlacedEnemy en:
            {
                var def = Assets?.Enemies.FirstOrDefault(d => d.id == en.Id);
                size = ObjectWorldSize(def?.width ?? 0, def?.height ?? 0, def?.scale ?? 1f, en.Scale);
                x = en.X; y = en.Y; break;
            }
            case PlacedGimmick gi:
            {
                var def = Assets?.Gimmicks.FirstOrDefault(d => d.id == gi.Id);
                size = new SizeF((def?.hitboxWidth > 0 ? def.hitboxWidth : GAME_TILE) * (gi.Scale > 0 ? gi.Scale : 1f),
                                 (def?.hitboxHeight > 0 ? def.hitboxHeight : GAME_TILE) * (gi.ScaleY > 0 ? gi.ScaleY : 1f));
                x = gi.X; y = gi.Y; break;
            }
            case PlacedItem it:
            {
                var def = Assets?.Items.FirstOrDefault(d => d.id == it.Id);
                size = ObjectWorldSize(def?.hitboxWidth ?? 0, def?.hitboxHeight ?? 0, 1f, it.Scale);
                x = it.X; y = it.Y; break;
            }
            default:
                return GetFootprint(obj);
        }
        return (x, y, Math.Max(size.Width, 16f), Math.Max(size.Height, 16f));
    }

    // 範囲ドラッグの四角（ワールド座標）に、少しでもかかっている敵・ギミック・アイテムを返す。
    // トリガーは含めない（トリガーは自分の矩形が大きく、範囲選択に混ぜると、ほかの物を選ぶ邪魔になるため。クリックで選ぶ）。
    private List<object> ObjectsInWorldRect(float wx1, float wy1, float wx2, float wy2)
    {
        var list = new List<object>();
        if (Stage == null) return list;
        void Consider(object o)
        {
            var (x, y, w, h) = GetDrawnRect(o);
            if (x < wx2 && x + w > wx1 && y < wy2 && y + h > wy1) list.Add(o);
        }
        foreach (var e in Stage.Enemies) Consider(e);
        foreach (var g in Stage.Gimmicks) Consider(g);
        foreach (var i in Stage.Items) Consider(i);
        return list;
    }

    private bool CtrlHeld => (ModifierKeys & Keys.Control) == Keys.Control;

    // 選択ツールでボタンを押した瞬間。物の上なら選択（Ctrl で足す・外す）と移動の準備、何も無い所なら範囲選択の開始。
    private void SelectDown(MouseEventArgs e)
    {
        _dragOrig = null;
        _clickedSelected = null;
        _marqueeActive = false;
        bool ctrl = CtrlHeld;
        var hit = FindObjectAt(e.X, e.Y);

        if (hit == null)
        {
            // 何も無い所：範囲選択を始める。Ctrl なしなら、いまの選択はここで外す
            _marqueeActive = true;
            _marqueeStart = e.Location;
            _marqueeRect = new Rectangle(e.X, e.Y, 0, 0);
            _marqueeAdditive = ctrl;
            if (!ctrl && _selection.Count > 0) _selection.Clear();
            Invalidate();
            return;
        }

        if (hit is EventTrigger)
        {
            // トリガーは今までどおり、1つだけ選んで専用エディタを開く（範囲選択・移動の対象にしない）
            _selection.Clear();
            _selection.Add(hit);
            ObjectSelected?.Invoke(this, EventArgs.Empty);
            Invalidate();
            return;
        }

        if (ctrl)
        {
            // Ctrl+クリック：選択へ足す／外す。トリガーが選ばれていたら外す（トリガーは混ぜない）
            _selection.RemoveAll(o => o is EventTrigger);
            if (IsSelected(hit)) _selection.RemoveAll(o => ReferenceEquals(o, hit));
            else _selection.Add(hit);
            ObjectSelected?.Invoke(this, EventArgs.Empty);
            Invalidate();
            return;
        }

        if (IsSelected(hit))
        {
            // すでに選んでいる物：選択は保ったまま、ドラッグで全員を動かせる。
            // 動かさずに離したときだけ、その1つに選択を絞る（離したときに決める）
            if (_selection.Count > 1) _clickedSelected = hit;
        }
        else
        {
            _selection.Clear();
            _selection.Add(hit);
            ObjectSelected?.Invoke(this, EventArgs.Empty);
        }

        // 移動の準備：選択中の全員の、いまの位置を覚える
        _dragOrig = _selection.Select(o => (o, GetX(o), GetY(o))).ToList();
        _dragStartWorld = new PointF(ToWorld(e.X, e.Y).wx, ToWorld(e.X, e.Y).wy);
        _dragMoved = false;
        Invalidate();
    }

    private static float GetX(object o) => o switch { PlacedEnemy a => a.X, PlacedGimmick b => b.X, PlacedItem c => c.X, _ => 0f };
    private static float GetY(object o) => o switch { PlacedEnemy a => a.Y, PlacedGimmick b => b.Y, PlacedItem c => c.Y, _ => 0f };
    private static void SetXY(object o, float x, float y)
    {
        switch (o)
        {
            case PlacedEnemy a: a.X = x; a.Y = y; break;
            case PlacedGimmick b: b.X = x; b.Y = y; break;
            case PlacedItem c: c.X = x; c.Y = y; break;
        }
    }

    // 選択ツールのドラッグ中。範囲選択の四角を伸ばす／選択した物を、移動量に合わせて動かす。
    private void SelectMove(MouseEventArgs e)
    {
        if (Stage == null) return;
        if (_marqueeActive)
        {
            int x = Math.Min(_marqueeStart.X, e.X), y = Math.Min(_marqueeStart.Y, e.Y);
            _marqueeRect = new Rectangle(x, y, Math.Abs(e.X - _marqueeStart.X), Math.Abs(e.Y - _marqueeStart.Y));
            Invalidate();
            return;
        }
        if (_dragOrig == null) return;

        // 移動量は、始めた位置からの差を32pxの格子に丸めたもの（置くときと同じ格子の感覚）。
        // 始めた時点の位置からの足し算にするので、行って戻れば元の位置に戻り、元から格子に乗っていない物の端数も保たれる。
        var (wx, wy) = ToWorld(e.X, e.Y);
        float dx = (float)(Math.Round((wx - _dragStartWorld.X) / GAME_TILE) * GAME_TILE);
        float dy = (float)(Math.Round((wy - _dragStartWorld.Y) / GAME_TILE) * GAME_TILE);
        float maxX = Stage.MapW * (float)GAME_TILE, maxY = Stage.MapH * (float)GAME_TILE;
        foreach (var (obj, ox, oy) in _dragOrig)
            SetXY(obj, Math.Clamp(ox + dx, 0f, maxX), Math.Clamp(oy + dy, 0f, maxY));
        if (dx != 0f || dy != 0f) { _dragMoved = true; Fire(); }
        Invalidate();
    }

    // 選択ツールでボタンを離した瞬間。範囲選択の結果を確定する／移動を確定する。
    private void SelectUp(MouseEventArgs e)
    {
        if (Stage == null) return;
        if (_marqueeActive)
        {
            _marqueeActive = false;
            if (_marqueeRect.Width >= 4 && _marqueeRect.Height >= 4)
            {
                // 範囲にかかった物を選ぶ（Ctrl なら、いまの選択へ足す）。同時に、その四角を「タイルの選択範囲」にもする
                var (wx1, wy1) = ToWorld(_marqueeRect.Left, _marqueeRect.Top);
                var (wx2, wy2) = ToWorld(_marqueeRect.Right, _marqueeRect.Bottom);
                if (_marqueeAdditive) _selection.RemoveAll(o => o is EventTrigger);
                foreach (var o in ObjectsInWorldRect(wx1, wy1, wx2, wy2))
                    if (!IsSelected(o)) _selection.Add(o);
                var (c1, r1) = ToTile(_marqueeRect.Left, _marqueeRect.Top);
                var (c2, r2) = ToTile(_marqueeRect.Right, _marqueeRect.Bottom);
                _tileSel = TileRect.FromCorners(c1, r1, c2, r2, Stage.MapW, Stage.MapH);
            }
            else if (!_marqueeAdditive)
            {
                // ほぼ動かさずに離した＝何も無い所をクリックしただけ：すべての選択を外す
                _tileSel = null;
            }
            _marqueeRect = Rectangle.Empty;
            ObjectSelected?.Invoke(this, EventArgs.Empty);
            Invalidate();
            return;
        }

        if (_dragOrig != null)
        {
            // 動かさずに、複数選択の1つをクリックしただけなら、その1つに選択を絞る
            if (!_dragMoved && _clickedSelected != null && !CtrlHeld)
            {
                _selection.Clear();
                _selection.Add(_clickedSelected);
                ObjectSelected?.Invoke(this, EventArgs.Empty);
            }
            _dragOrig = null;
            _clickedSelected = null;
            Invalidate();
        }
    }

    // ----- 外から使う操作（Form1 のボタン・キー）-----

    // 選択の数（物の数）
    public int SelectedCount => _selection.Count;

    // 物とタイルの選択を、すべて外す
    public void ClearAllSelection()
    {
        bool had = _selection.Count > 0;
        _selection.Clear();
        _tileSel = null;
        _marqueeActive = false;
        _dragOrig = null;
        if (had) ObjectSelected?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    // Delete キー／メニューの「削除」。選んでいる物があれば全部消す。無ければ、タイルの選択範囲のタイルを空にする。
    // 何かを消したら true。1回の操作が履歴1つになる。
    public bool DeleteSelection()
    {
        if (Stage == null) return false;
        var targets = _selection.ToList();
        if (targets.Count > 0)
        {
            foreach (var t in targets)
            {
                if (t is PlacedEnemy pe) Stage.Enemies.Remove(pe);
                else if (t is PlacedGimmick pg) Stage.Gimmicks.Remove(pg);
                else if (t is PlacedItem pi) Stage.Items.Remove(pi);
                else if (t is EventTrigger tr) Stage.Triggers.Remove(tr);
            }
            _selection.Clear();
            ObjectSelected?.Invoke(this, EventArgs.Empty);
            Fire(); Invalidate();
            EditCompleted?.Invoke(this, EventArgs.Empty);
            return true;
        }
        if (_tileSel.HasValue) return ClearTilesInSelection();
        return false;
    }

    // 選択範囲のタイルを、いま選んでいるタイルで塗りつぶす（塗りつぶし先は FillLayer）。範囲があれば true
    public bool FillTileSelection()
    {
        if (Stage == null || !_tileSel.HasValue) return false;
        int n = TileFill.FillRect(FillLayerArray(), _tileSel.Value, SelectedTileId);
        if (n > 0) { Fire(); Invalidate(); EditCompleted?.Invoke(this, EventArgs.Empty); }
        return true;
    }

    // 選択範囲のタイルを空（0）にする
    public bool ClearTilesInSelection()
    {
        if (Stage == null || !_tileSel.HasValue) return false;
        int n = TileFill.FillRect(FillLayerArray(), _tileSel.Value, 0);
        if (n > 0) { Fire(); Invalidate(); EditCompleted?.Invoke(this, EventArgs.Empty); }
        return true;
    }

    private int[,] FillLayerArray() => FillLayer switch
    {
        TileLayer.Back => Stage!.DecoLayerBack,
        TileLayer.Front => Stage!.DecoLayerFront,
        _ => Stage!.Map,
    };

    private string FillLayerName() => FillLayer switch
    {
        TileLayer.Back => "Layer 1（遠景）",
        TileLayer.Front => "Layer 3（近景）",
        _ => "Layer 2（メイン）",
    };

    // 選択ツールのとき、画面の左上に出す説明（何も選んでいなければ出さない）
    private string SelectIndicatorText()
    {
        if (_selection.Count == 0 && !_tileSel.HasValue) return "";
        var parts = new List<string>();
        if (_selection.Count > 1) parts.Add($"{_selection.Count}個を選択中（ドラッグで移動／Delete で削除／右クリックで同時編集）");
        else if (_selection.Count == 1) parts.Add("1個を選択中");
        if (_tileSel.HasValue) parts.Add($"範囲 {_tileSel.Value.Cols}×{_tileSel.Value.Rows}マス → {FillLayerName()}");
        return "🔍 " + string.Join(" ／ ", parts);
    }

    // ステージデータが変更されたことを外部（Form1）へ通知するための共通ヘルパー。
    private void Fire()
    {
        StageModified?.Invoke(this, EventArgs.Empty);
    }
}
