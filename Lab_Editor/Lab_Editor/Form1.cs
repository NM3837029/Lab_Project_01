using System.Diagnostics;
using Newtonsoft.Json.Linq;

namespace Lab_Editor;

// ステージエディタのメインウィンドウ（Form1）。
// このクラスが、ステージ一覧・マップキャンバス・パレット（タイル/敵/ギミック/アイテム）・
// プロパティグリッド・各種ツールバーなど、エディタ画面全体の制御をまとめて受け持っている。
// 実際のUIレイアウト（ボタンやテキストボックスの配置）は Form1.Designer.cs 側で組み立てており、
// このファイルではそれらのコントロールに対するイベント処理（クリック時の挙動など）とデータの読み書きを担当する。
public partial class Form1 : Form
{
    // プロジェクトのルートフォルダ（ゲーム本体のフォルダ）への絶対パス。
    private readonly string projectRoot;
    // "assets" フォルダ（タイル・敵・ギミック・アイテムなどの定義ファイル置き場）への絶対パス。
    private readonly string assetsPath;
    // "assets/stages" フォルダ（各ステージのJSONファイル置き場）への絶対パス。
    private readonly string stagesPath;
    // テストプレイ・通常プレイで起動するゲーム本体（exe）への絶対パス。
    private readonly string exePath;
    // Feature 4: 「ここからプレイ」機能用の一時ステージJSONファイルのパス。
    // 本来のステージファイルを上書きせず、この専用ファイルに開始位置だけ差し替えた内容を書き出して起動する。
    private readonly string testPlayJsonPath;

    // 現在読み込んでいるアセット定義（タイル/敵/ギミック/アイテム/BGM/SEなど）一式。
    private AssetDefinitions assets = new();
    // 現在編集中のステージデータ本体。まだステージが選択されていない場合は null。
    private StageData? currentStage;
    // 現在編集中のステージのファイル名（拡張子込み、フォルダパスは含まない）。
    private string currentStageFile = "";
    // ステージ読み込み処理の最中だけ true にするフラグ。
    // 読み込み中にUIの値（NumericUpDownやCheckBoxなど）を設定すると、その変更イベントが発火して
    // 「読み込んだ直後にまた保存してしまう」といった不要な処理が走ってしまうため、それを防ぐために使う。
    private bool _loading = false;
    // 元に戻す(Undo)・やり直し(Redo)のための編集履歴を管理するクラスのインスタンス。
    private HistoryManager historyMgr = new HistoryManager();

    // 現在押されているキーの集合。矢印キー同時押しでゲーム起動するショートカット判定などに使う。
    private HashSet<Keys> pressedKeys = new();

    // イベントリスト(Layer 4)の各行 ↔ 実オブジェクトの対応。"START"/"GOAL" は特別扱い。
    // （lstPlacedEvents の行インデックスと同じ順序で、対応する実体（トリガー/敵/ギミック/アイテム、
    // またはスタート/ゴールを表す文字列）を保持しておくためのリスト）
    private List<object?> _placedEventRefs = new();

    // コンストラクタ。各種パスを組み立て、UIコンポーネントを初期化する。
    public Form1()
    {
        // ゲームプロジェクトのルートパスを取得し、そこからassets関連の各パスを組み立てる。
        projectRoot = AppPaths.ProjectRoot;
        assetsPath = Path.Combine(projectRoot, "assets");
        stagesPath = Path.Combine(assetsPath, "stages");
        exePath = Path.Combine(projectRoot, "x64", "Debug", "Lab_Project_01.exe");
        // Feature 4: テストプレイ専用の一時JSONファイルのパスをあらかじめ決めておく。
        testPlayJsonPath = Path.Combine(stagesPath, "_test_play.json");
        // Designerで定義されたUIコントロール一式を生成・配置する（Form1.Designer.cs の処理を呼び出す）。
        InitializeComponent();
        // マップキャンバスにアセットフォルダのパスを教えておく（スプライト読み込み等に使用）。
        mapCanvas.AssetsPath = assetsPath;
        // フォーム自身がキー入力を最初に受け取れるようにする（子コントロールにフォーカスがあってもショートカットを拾うため）。
        KeyPreview = true;
    }

    // キーが押された瞬間に呼ばれる処理。矢印キー全部同時押しでのゲーム起動や、Undo/Redoのショートカットを判定する。
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        // 押されたキーを記録しておく。
        pressedKeys.Add(e.KeyCode);
        // 上下左右キーがすべて同時に押されている状態を検出したら、隠しショートカットとしてゲームを起動する。
        if (pressedKeys.Contains(Keys.Up) && pressedKeys.Contains(Keys.Down) &&
            pressedKeys.Contains(Keys.Left) && pressedKeys.Contains(Keys.Right))
        { pressedKeys.Clear(); LaunchGame(); }

        // Ctrl+Z で元に戻す、Ctrl+Y でやり直し。
        if (e.Control && e.KeyCode == Keys.Z) Undo();
        if (e.Control && e.KeyCode == Keys.Y) Redo();
    }
    // キーが離された瞬間に呼ばれる処理。押下中キーの記録から取り除くだけ。
    protected override void OnKeyUp(KeyEventArgs e) { base.OnKeyUp(e); pressedKeys.Remove(e.KeyCode); }

    // ===== 初期化 =====
    // フォームが画面に表示される直前に呼ばれる初期化処理。
    // 必要なフォルダの作成、アセット/ステージ一覧の読み込み、初回起動時のガイド表示などをここで行う。
    private void Form1_Load(object? sender, EventArgs e)
    {
        // ステージ保存フォルダがまだ無ければ作成する。
        if (!Directory.Exists(stagesPath)) Directory.CreateDirectory(stagesPath);
        // Feature 3: sound フォルダ（BGM/SEファイルの置き場）を事前に作成しておく。
        // 無い状態でサウンド管理を開くとエラーになる可能性があるため、起動時に必ず用意する。
        Directory.CreateDirectory(Path.Combine(projectRoot, "sound"));
        // assetsフォルダからタイル/敵/ギミック/アイテムなどの定義一式を読み込む。
        assets = AssetDefinitions.LoadFromFolder(assetsPath);
        // 読み込んだアセットをもとに右側パレット（タイル/敵/ギミック/アイテムの一覧）を再構築する。
        RefreshPalette();
        // ステージフォルダの中身を左側のステージ一覧に反映する。
        RefreshStageList();
        // ステージが1つも存在しない場合（初回起動など）は、サンプルとして"stage_01"を自動生成する。
        if (Directory.GetFiles(stagesPath, "*.json").Length == 0) CreateNewStage("stage_01");
        // ゲーム本体(exe)が見つかるかどうかをステータスバーに表示する。
        UpdateStatus();
        // 初回起動時のみ、使い方ガイドを自動的に表示する。
        ShowFirstRunGuideIfNeeded();
    }

    // Feature: UI改善（提案書 MW-1）— 初めて開いたときだけ使い方ガイドを自動表示する。
    // 一度表示したらマーカーファイルを作り、以後は「ヘルプ」メニューから任意で開く形に切り替える。
    private void ShowFirstRunGuideIfNeeded()
    {
        // ガイドを表示済みかどうかを記録するための目印ファイルのパス。
        string marker = Path.Combine(AppPaths.LogsDir, "first_run_guide_shown.flag");
        // 目印ファイルが既に存在する＝表示済みなら、何もせず終了する。
        if (File.Exists(marker)) return;
        // 目印ファイルを作成する（失敗しても致命的ではないので例外は握りつぶす）。
        try { File.WriteAllText(marker, DateTime.Now.ToString("o")); } catch { }
        // 使い方ガイドのダイアログをモーダル表示する。
        new HelpForm().ShowDialog(this);
    }

    // ===== ステージリスト =====
    // stagesフォルダの中身を走査して、左側のステージ一覧リストボックスを最新の状態に更新する。
    private void RefreshStageList()
    {
        lstStages.Items.Clear();
        if (Directory.Exists(stagesPath))
            foreach (var f in Directory.GetFiles(stagesPath, "*.json"))
            {
                var name = Path.GetFileName(f);
                // Feature 4: テストプレイ専用の一時JSON("_test_play.json")は、
                // 実際に編集対象となるステージではないため一覧には表示しない。
                if (name != "_test_play.json")
                    lstStages.Items.Add(name);
            }
        // まだ何も選択されていない状態でリストに項目があれば、先頭を自動選択して即座に編集できるようにする。
        if (lstStages.Items.Count > 0 && lstStages.SelectedIndex < 0)
            lstStages.SelectedIndex = 0;
    }

    // ステージ一覧の選択項目が変わったときに呼ばれる。選択されたファイル名を記録し、その内容を読み込む。
    private void lstStages_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (lstStages.SelectedItem == null) return;
        currentStageFile = lstStages.SelectedItem.ToString() ?? "";
        LoadCurrentStage();
    }

    // 現在選択されているステージファイルをディスクから読み込み、UI上の各項目（マップ・プレイヤー設定・
    // 編集ツール設定・編集コスト設定など）すべてに反映させる処理。
    private void LoadCurrentStage()
    {
        if (string.IsNullOrEmpty(currentStageFile)) return;
        // 読み込み中フラグを立てる。これにより、この後NumericUpDownやCheckBoxの値を設定しても
        // PlayerSetting_Changed などの変更イベントハンドラ内で「読み込んだ内容を即座に上書き保存する」
        // という無駄な処理が走らないようにする。
        _loading = true;
        try
        {
            // JSONファイルからステージデータ本体を読み込む。
            currentStage = StageData.LoadFromFile(Path.Combine(stagesPath, currentStageFile));
            // Undo/Redo履歴をクリアし、読み込んだ直後の状態を履歴の最初の1件として積む。
            historyMgr.Clear();
            historyMgr.Push(currentStage);

            // マップキャンバスに新しいステージとアセットを渡し、タイルの色情報を再構築してから再描画する。
            mapCanvas.Stage = currentStage;
            mapCanvas.Assets = assets;
            mapCanvas.RefreshTileColors();
            // スクロール位置をステージの左上へリセットする。
            mapCanvas.ScrollX = 0;
            mapCanvas.ScrollY = 0;
            hScrollMap.Value = 0;
            vScrollMap.Value = 0;
            UpdateScrollBars();
            mapCanvas.Invalidate();

            // プレイヤーの開始座標をUIに反映する（マップ範囲外の値が入っていた場合に備えてクランプする）。
            numStartX.Value = Math.Clamp((decimal)currentStage.PlayerStartX, 0, 9999);
            numStartY.Value = Math.Clamp((decimal)currentStage.PlayerStartY, 0, 9999);
            // プレイヤーが使えるアクション（2段ジャンプ・ダッシュ・火の玉・飛行）のチェック状態を反映する。
            chkDoubleJump.Checked = currentStage.Capabilities.canDoubleJump;
            chkDash.Checked = currentStage.Capabilities.canDash;
            chkFireball.Checked = currentStage.Capabilities.canShootFireball;
            chkFly.Checked = currentStage.Capabilities.canFly;
            // ジャンプ力・移動速度の数値を反映する（想定される範囲内にクランプしてから設定）。
            numJumpPower.Value = Math.Clamp(currentStage.Capabilities.baseJumpPower, -30, 0);
            numSpeed.Value = (decimal)Math.Clamp(currentStage.Capabilities.baseSpeed, 0.5f, 20f);

            // 編集ツール（巻き戻し/一時停止/早送り/画面エフェクト/オブジェクト編集）の許可設定を反映する。
            chkEditRewind.Checked = currentStage.EditTools.rewindEnabled;
            chkEditPause.Checked = currentStage.EditTools.pauseEnabled;
            chkEditFastForward.Checked = currentStage.EditTools.fastForwardEnabled;
            chkEditScreenFx.Checked = currentStage.EditTools.screenEffectEnabled;
            chkEditObjectEdit.Checked = currentStage.EditTools.objectEditEnabled;

            // 編集コスト（ゲージの最大値・自然回復・各操作ごとの消費量）の数値を反映する。
            numEditMaxCost.Value = (decimal)Math.Clamp(currentStage.EditCost.maxCost, 0, 999);
            numEditRegen.Value = (decimal)Math.Clamp(currentStage.EditCost.regenPerSec, 0, 999);
            numEditDrainRewind.Value = (decimal)Math.Clamp(currentStage.EditCost.drainRewindPerSec, 0, 999);
            numEditDrainPause.Value = (decimal)Math.Clamp(currentStage.EditCost.drainPausePerSec, 0, 999);
            numEditDrainFF.Value = (decimal)Math.Clamp(currentStage.EditCost.drainFastForwardPerSec, 0, 999);
            numEditDrainScreenFx.Value = (decimal)Math.Clamp(currentStage.EditCost.drainScreenEffectPerSec, 0, 999);
            numEditFlatColorCycle.Value = (decimal)Math.Clamp(currentStage.EditCost.flatColorCycle, 0, 999);
            numEditFlatMenuToggle.Value = (decimal)Math.Clamp(currentStage.EditCost.flatMenuToggle, 0, 999);
            numEditFlatSpeedChange.Value = (decimal)Math.Clamp(currentStage.EditCost.flatSpeedChange, 0, 999);
            numEditFlatDirectionFlip.Value = (decimal)Math.Clamp(currentStage.EditCost.flatDirectionFlip, 0, 999);
            numEditFlatResetAll.Value = (decimal)Math.Clamp(currentStage.EditCost.flatResetAll, 0, 999);

            // マップサイズ（幅・高さ）の数値を反映する。
            numMapW.Value = currentStage.MapW;
            numMapH.Value = currentStage.MapH;

            // 画面上部の情報バーに、現在編集中のステージ名・サイズ・配置数のサマリを表示する。
            lblCurrentStage.Text = $"編集中: {currentStageFile}  ({currentStage.MapW}×{currentStage.MapH}) | 敵:{currentStage.Enemies.Count} ギミック:{currentStage.Gimmicks.Count} トリガー:{currentStage.Triggers.Count}";
            // 配置済みイベント一覧（Layer 4のリスト）を最新の内容に更新する。
            RefreshPlacedEvents();
        }
        // 読み込み処理が終わったら（例外が出た場合も含めて）必ず読み込み中フラグを下ろす。
        finally { _loading = false; }
    }

    // ===== スクロールバー =====
    // マップのサイズとキャンバスの表示領域サイズから、スクロールバーの可動範囲（Maximum）を計算し直す処理。
    private void UpdateScrollBars()
    {
        if (currentStage == null) return;
        // マップ全体のピクセルサイズからキャンバスの表示サイズを引いた分だけスクロールできればよい。
        // 端に少し余裕(+50)を持たせて、マップの右端・下端がキャンバスちょうどに来ても窮屈にならないようにしている。
        int maxX = Math.Max(0, currentStage.MapW * MapCanvas.TILE_SIZE - mapCanvas.Width + 50);
        int maxY = Math.Max(0, currentStage.MapH * MapCanvas.TILE_SIZE - mapCanvas.Height + 50);
        hScrollMap.Maximum = maxX + hScrollMap.LargeChange;
        vScrollMap.Maximum = maxY + vScrollMap.LargeChange;
        // マップサイズが縮小された場合など、現在のスクロール位置が新しい可動範囲を超えていたら丸め込む。
        hScrollMap.Value = Math.Min(hScrollMap.Value, maxX);
        vScrollMap.Value = Math.Min(vScrollMap.Value, maxY);
    }

    // 水平スクロールバーが操作されたら、キャンバスのスクロール位置に反映して再描画する。
    private void hScrollMap_Scroll(object? sender, ScrollEventArgs e)
    { mapCanvas.ScrollX = e.NewValue; mapCanvas.Invalidate(); }

    // 垂直スクロールバーが操作されたら、キャンバスのスクロール位置に反映して再描画する。
    private void vScrollMap_Scroll(object? sender, ScrollEventArgs e)
    { mapCanvas.ScrollY = e.NewValue; mapCanvas.Invalidate(); }

    // ===== パレット（タイル・敵・ギミック・アイテム）=====
    // アセット定義（assets）の内容をもとに、右側パレットのタイルボタン群・敵/ギミック/アイテムの
    // リストボックスの中身をすべて作り直す処理。アセット管理画面で追加/変更/削除が行われた後に呼ばれる。
    private void RefreshPalette()
    {
        // 既存のタイルボタンを全部消してから作り直す。
        flpTiles.Controls.Clear();
        foreach (var t in assets.Tiles)
        {
            // タイルの色情報（HTMLカラー文字列）をColorに変換する。変換に失敗した場合はグレーで代用する。
            Color c;
            try { c = ColorTranslator.FromHtml(t.color); } catch { c = Color.Gray; }
            // タイル1つにつき1つのボタンを生成する。ボタンの背景色をタイルの色に合わせ、
            // 文字色は背景の明るさに応じて白/黒を自動選択して視認性を確保する。
            var btn = new Button
            {
                Text = $"{t.id}:{t.name}",
                Tag = t.id,
                Size = new Size(104, 34),
                BackColor = c,
                ForeColor = c.GetBrightness() < 0.5f ? Color.White : Color.Black,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Meiryo UI", 7),
                TextAlign = ContentAlignment.MiddleRight,
                Padding = new Padding(0, 0, 3, 0),
            };
            // Feature: UI改善（提案書 MP-1）— タイルボタンが単色の四角のみで見た目が分からなかったため、
            // スプライトが設定されていれば実際の画像を小さいアイコンとして添える（無ければ従来通り色のみ）。
            var icon = LoadTileIcon(t.sprite, 24);
            if (icon != null)
            {
                // アイコン画像が読み込めた場合は、ボタンの左側に画像・その右にテキストを並べて表示する。
                btn.Image = icon;
                btn.ImageAlign = ContentAlignment.MiddleLeft;
                btn.TextImageRelation = TextImageRelation.ImageBeforeText;
            }
            btn.Click += TileBtn_Click;
            flpTiles.Controls.Add(btn);
        }

        // 敵・ギミック・アイテムの一覧も、それぞれのリストボックスへ名前だけを表示する形で作り直す。
        lstEnemies.Items.Clear();
        foreach (var e in assets.Enemies) lstEnemies.Items.Add($"{e.name}");

        lstGimmicks.Items.Clear();
        foreach (var g in assets.Gimmicks) lstGimmicks.Items.Add($"{g.name}");

        lstItems.Items.Clear();
        foreach (var i in assets.Items) lstItems.Items.Add($"{i.name}");
    }

    // タイルのスプライト画像ファイルを読み込み、指定サイズの正方形アイコン（Bitmap）として返す。
    // spritePath : assetsフォルダからの相対パス（例: "assets/sprites/grass.png"）。
    // size       : 生成するアイコンの一辺のピクセル数。
    // 画像が存在しない・読み込みに失敗した場合は null を返し、呼び出し側は色のみの表示にフォールバックする。
    private static Image? LoadTileIcon(string spritePath, int size)
    {
        if (string.IsNullOrEmpty(spritePath)) return null;
        // 相対パスをプロジェクトルート基準の絶対パスに変換する（スラッシュ区切りをWindowsのバックスラッシュに直す）。
        string full = Path.Combine(AppPaths.ProjectRoot, spritePath.Replace('/', '\\'));
        if (!File.Exists(full)) return null;
        try
        {
            // ファイルを読み取り専用・共有ありで開く（エディタが動いている間に他プロセスから読まれても問題ないように）。
            using var fs = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var src = Image.FromStream(fs);
            // 元画像を指定サイズの正方形ビットマップへ縮小描画する。ドット絵の輪郭がぼやけないように
            // 補間モードを「最近傍（NearestNeighbor）」にしてピクセルをくっきり保つ。
            var bmp = new Bitmap(size, size);
            using var g = Graphics.FromImage(bmp);
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
            g.DrawImage(src, 0, 0, size, size);
            return bmp;
        }
        catch { return null; }
    }

    // タイルパレットのボタンがクリックされたときの処理。クリックされたタイルを「現在選択中のタイル」に設定する。
    private void TileBtn_Click(object? sender, EventArgs e)
    {
        if (sender is Button btn && btn.Tag is int id)
        {
            // 現在アクティブなレイヤーモードを保持しつつタイル選択。
            // 遠景/近景レイヤー編集中にタイルを選んでも、その専用レイヤーへの配置モードを維持し、
            // それ以外（メインレイヤー編集中など）の場合のみ通常のTileモードに切り替える。
            if (mapCanvas.CurrentMode != MapCanvas.EditMode.DecoLayerBack &&
                mapCanvas.CurrentMode != MapCanvas.EditMode.DecoLayerFront)
                mapCanvas.CurrentMode = MapCanvas.EditMode.Tile;
            mapCanvas.SelectedTileId = id;
            // パレットからタイルを選んだので、ツールバー側の表示も必ず「ペン」に同期させる（下記コメント参照）。
            SyncToolButtonsToPen();
            // 選択されたボタンだけ見た目を「押し込まれた状態(Standard)」にし、他は通常のフラット表示に戻す。
            foreach (Control c in flpTiles.Controls)
                if (c is Button b) b.FlatStyle = b == btn ? FlatStyle.Standard : FlatStyle.Flat;
        }
    }

    // Feature: UI改善（友人フィードバック対応）— 行・列数を数値指定して矩形範囲を一括でタイル埋めする。
    // 1マスずつクリックして塗るのが手間だという友人からのフィードバックを受けて追加した機能。
    private void BtnBulkFill_Click(object? sender, EventArgs e)
    {
        if (currentStage == null) { MessageBox.Show("先にステージを選択してください。"); return; }

        // 現在編集中のレイヤー（遠景/メイン/近景）に応じて、書き込み先の2次元配列を選ぶ。
        int[,] targetLayer = mapCanvas.CurrentMode switch
        {
            MapCanvas.EditMode.DecoLayerBack => currentStage.DecoLayerBack,
            MapCanvas.EditMode.DecoLayerFront => currentStage.DecoLayerFront,
            _ => currentStage.Map
        };

        // 開始行/開始列・行数/列数を入力させる専用ダイアログを表示する。キャンセルされたら何もしない。
        using var form = new BulkTileFillForm(currentStage.MapW, currentStage.MapH);
        if (form.ShowDialog() != DialogResult.OK) return;

        // 指定された矩形範囲がマップサイズをはみ出さないように、終了位置をマップの端でクランプする。
        int rEnd = Math.Min(currentStage.MapH, form.StartRow + form.RowCount);
        int cEnd = Math.Min(currentStage.MapW, form.StartCol + form.ColCount);
        // 範囲内のすべてのマスに、現在選択中のタイルIDを敷き詰める。
        for (int r = form.StartRow; r < rEnd; r++)
            for (int c = form.StartCol; c < cEnd; c++)
                targetLayer[r, c] = mapCanvas.SelectedTileId;

        mapCanvas.Invalidate();
        SaveCurrentStage();
    }

    // 敵リストの選択項目が変わったときの処理。選ばれた敵を「マップに配置するアセット」として選択状態にする。
    private void lstEnemies_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (lstEnemies.SelectedIndex >= 0 && lstEnemies.SelectedIndex < assets.Enemies.Count)
        {
            mapCanvas.CurrentMode = MapCanvas.EditMode.Enemy;
            mapCanvas.SelectedAssetId = assets.Enemies[lstEnemies.SelectedIndex].id;
            // 敵・ギミック・アイテムは同時に1種類しか配置できないため、他の2つの選択は解除しておく。
            lstGimmicks.ClearSelected(); lstItems.ClearSelected();
            SyncToolButtonsToPen();
            UpdateLayerInfo();
        }
    }

    // ギミックリストの選択項目が変わったときの処理。選ばれたギミックを配置対象アセットに設定する。
    private void lstGimmicks_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (lstGimmicks.SelectedIndex >= 0 && lstGimmicks.SelectedIndex < assets.Gimmicks.Count)
        {
            mapCanvas.CurrentMode = MapCanvas.EditMode.Gimmick;
            mapCanvas.SelectedAssetId = assets.Gimmicks[lstGimmicks.SelectedIndex].id;
            lstEnemies.ClearSelected(); lstItems.ClearSelected();
            SyncToolButtonsToPen();
            UpdateLayerInfo();
        }
    }

    // アイテムリストの選択項目が変わったときの処理。選ばれたアイテムを配置対象アセットに設定する。
    private void lstItems_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (lstItems.SelectedIndex >= 0 && lstItems.SelectedIndex < assets.Items.Count)
        {
            mapCanvas.CurrentMode = MapCanvas.EditMode.Item;
            mapCanvas.SelectedAssetId = assets.Items[lstItems.SelectedIndex].id;
            lstEnemies.ClearSelected(); lstGimmicks.ClearSelected();
            SyncToolButtonsToPen();
            UpdateLayerInfo();
        }
    }

    // ===== Undo / Redo =====
    // 直前の編集操作を取り消し、履歴上の1つ前の状態にステージを戻す。
    private void Undo()
    {
        if (historyMgr.CanUndo)
        {
            currentStage = historyMgr.Undo();
            mapCanvas.Stage = currentStage;
            mapCanvas.Invalidate();
            // 取り消した後の状態をファイルにも保存し、イベント一覧の表示も最新化する。
            SaveCurrentStage();
            RefreshPlacedEvents();
        }
    }

    // Undoで取り消した操作をもう一度やり直し、履歴上の1つ先の状態にステージを進める。
    private void Redo()
    {
        if (historyMgr.CanRedo)
        {
            currentStage = historyMgr.Redo();
            mapCanvas.Stage = currentStage;
            mapCanvas.Invalidate();
            SaveCurrentStage();
            RefreshPlacedEvents();
        }
    }

    // ===== ツールバー・レイヤー =====
    // レイヤー切り替えボタン（Layer1〜4）がクリックされたときの処理。
    // クリックされたボタンだけをチェック状態にし、対応する編集モードとパレットタブに切り替える。
    private void TsbLayer_Click(object? sender, EventArgs e)
    {
        // 4つのレイヤーボタンは排他的に1つだけチェックされる（ラジオボタンのような挙動）。
        tsbLayer1.Checked = sender == tsbLayer1;
        tsbLayer2.Checked = sender == tsbLayer2;
        tsbLayer3.Checked = sender == tsbLayer3;
        tsbLayer4.Checked = sender == tsbLayer4;

        // 選ばれたレイヤーに応じてキャンバスの編集モードを切り替え、右側パレットも対応するタブへ切り替える。
        if (tsbLayer1.Checked) { mapCanvas.CurrentMode = MapCanvas.EditMode.DecoLayerBack; tabRight.SelectedTab = tabTilePalette; }
        else if (tsbLayer2.Checked) { mapCanvas.CurrentMode = MapCanvas.EditMode.Tile; tabRight.SelectedTab = tabTilePalette; }
        else if (tsbLayer3.Checked) { mapCanvas.CurrentMode = MapCanvas.EditMode.DecoLayerFront; tabRight.SelectedTab = tabTilePalette; }
        else if (tsbLayer4.Checked)
        {
            // イベントモード時はイベント配置パレットを表示し、
            // ラジオボタン/敵・ギミック・アイテムの選択状態から実際のサブモードを決定し直す。
            tabRight.SelectedTab = tabEventPalette;
            UpdateEventModeSubTool();
        }
        UpdateLayerInfo();
        mapCanvas.Invalidate();
    }

    // ツールバーのペン/消しゴム/選択ボタンがクリックされたときの処理。
    private void TsbTool_Click(object? sender, EventArgs e)
    {
        // 3つのツールボタンも排他的に1つだけチェックされる。
        tsbPen.Checked = sender == tsbPen;
        tsbEraser.Checked = sender == tsbEraser;
        tsbSelect.Checked = sender == tsbSelect;

        if (tsbEraser.Checked) mapCanvas.CurrentMode = MapCanvas.EditMode.Eraser;
        else if (tsbSelect.Checked) mapCanvas.CurrentMode = MapCanvas.EditMode.Select;
        else if (tsbPen.Checked)
        {
            // ペンに戻す際、現在チェックされているレイヤーボタンを再度クリックしたのと同じ処理を呼び出し、
            // そのレイヤーに応じた編集モード（タイル/遠景/近景/イベント）に戻す。
            TsbLayer_Click(tsbLayer1.Checked ? tsbLayer1 : (tsbLayer2.Checked ? tsbLayer2 : (tsbLayer3.Checked ? tsbLayer3 : tsbLayer4)), EventArgs.Empty);
        }
        UpdateLayerInfo();
    }

    // Bugfix: タイル/敵/ギミック/アイテムをパレットから選ぶと mapCanvas.CurrentMode が
    // 配置モードへ強制的に切り替わる（TileBtn_Click・lstEnemies/lstGimmicks/lstItems_SelectedIndexChanged）が、
    // ツールバーの 消しゴム/選択 ボタンはチェックされたまま残っていた。そのため「消しゴムを選んだのに
    // パレットを触った後クリックすると消えずに置かれる（＝消しゴムが効かないように見える）」という
    // 状態不整合が発生していた。パレット選択時は必ずペンツールへ表示上も同期させる。
    private void SyncToolButtonsToPen()
    {
        tsbPen.Checked = true;
        tsbEraser.Checked = false;
        tsbSelect.Checked = false;
    }

    // イベントパレット内のラジオボタン（トリガー/開始位置/ゴール）の選択状態が変わったときの処理。
    // 現在Layer4（イベントレイヤー）が選択されている場合のみ、実際の編集モードに反映する。
    private void EventModeItem_CheckedChanged(object? sender, EventArgs e)
    {
        if (tsbLayer4.Checked)
            UpdateEventModeSubTool();
    }

    // イベントレイヤー(Layer4)内で、具体的にどの種類のイベントを配置するモードにするかを、
    // ラジオボタンおよび敵/ギミック/アイテムリストの選択状態から総合的に判定して設定する。
    private void UpdateEventModeSubTool()
    {
        // 優先順位: トリガー用ラジオボタン → 開始位置 → ゴール → 敵選択中 → ギミック選択中 → アイテム選択中。
        // どれにも該当しなければ「選択モード」をデフォルトとする。
        if (rbTrigger.Checked) mapCanvas.CurrentMode = MapCanvas.EditMode.Trigger;
        else if (rbPlayerStart.Checked) mapCanvas.CurrentMode = MapCanvas.EditMode.PlayerStart;
        else if (rbGoal.Checked) mapCanvas.CurrentMode = MapCanvas.EditMode.Goal;
        else if (lstEnemies.SelectedIndex >= 0) mapCanvas.CurrentMode = MapCanvas.EditMode.Enemy;
        else if (lstGimmicks.SelectedIndex >= 0) mapCanvas.CurrentMode = MapCanvas.EditMode.Gimmick;
        else if (lstItems.SelectedIndex >= 0) mapCanvas.CurrentMode = MapCanvas.EditMode.Item;
        else mapCanvas.CurrentMode = MapCanvas.EditMode.Select; // デフォルト
        UpdateLayerInfo();
    }

    // ===== 配置済みイベントリスト (Layer 4) =====
    // 現在のステージに配置されているスタート/ゴール/トリガー/敵/ギミック/アイテムをすべて洗い出し、
    // 画面右側の「イベントリスト」タブに一覧表示する。あわせて _placedEventRefs にも同じ順序で
    // 実体（またはSTART/GOALを表す文字列）を積んでおき、リストの行と実オブジェクトを対応付けられるようにする。
    private void RefreshPlacedEvents()
    {
        lstPlacedEvents.Items.Clear();
        _placedEventRefs.Clear();
        if (currentStage == null) return;

        // スタート位置は常に1つ存在するので、必ず先頭に表示する。
        lstPlacedEvents.Items.Add($"🚶 Start ({currentStage.PlayerStartX}, {currentStage.PlayerStartY})");
        _placedEventRefs.Add("START");

        // ゴールは未設定の場合(-1)があるので、設定されている時だけ表示する。
        if (currentStage.GoalX >= 0)
        {
            lstPlacedEvents.Items.Add($"🏁 Goal ({currentStage.GoalX}, {currentStage.GoalY})");
            _placedEventRefs.Add("GOAL");
        }

        // 続けてトリガー・敵・ギミック・アイテムを種類ごとにまとめて列挙する。
        // それぞれ絵文字アイコンを頭に付け、座標も一緒に表示することで一覧性を高めている。
        foreach (var t in currentStage.Triggers) { lstPlacedEvents.Items.Add($"⚡ {t.id} ({t.x},{t.y})"); _placedEventRefs.Add(t); }
        foreach (var e in currentStage.Enemies) { lstPlacedEvents.Items.Add($"👾 {e.Id} ({e.X},{e.Y})"); _placedEventRefs.Add(e); }
        foreach (var g in currentStage.Gimmicks) { lstPlacedEvents.Items.Add($"🔧 {g.Id} ({g.X},{g.Y})"); _placedEventRefs.Add(g); }
        foreach (var i in currentStage.Items) { lstPlacedEvents.Items.Add($"💎 {i.Id} ({i.X},{i.Y})"); _placedEventRefs.Add(i); }
    }

    // イベントリストの選択項目が変わったときの処理。現状は空実装。
    private void LstPlacedEvents_SelectedIndexChanged(object? sender, EventArgs e)
    {
        // リストで選択したらキャンバス上でも選択状態にする処理をここに入れる（拡張用の空フック）。
    }

    // イベントリスト(Layer 4)をダブルクリックすると、該当オブジェクトへキャンバスをジャンプさせて選択する
    // (MZの「リスト選択で該当イベントの編集画面に直接ジャンプ」に相当)。トリガーは編集ダイアログも開く。
    private void LstPlacedEvents_DoubleClick(object? sender, EventArgs e)
    {
        int idx = lstPlacedEvents.SelectedIndex;
        if (currentStage == null || idx < 0 || idx >= _placedEventRefs.Count) return;

        // 選択されている行に対応する実体の種類によって、ジャンプ先座標の取得元とその後の挙動を分岐する。
        switch (_placedEventRefs[idx])
        {
            case "START":
                // スタート位置には実オブジェクトが無いので target は null のまま座標だけ渡す。
                SelectAndScrollTo(null, currentStage.PlayerStartX, currentStage.PlayerStartY);
                break;
            case "GOAL":
                // ゴールが未設定(-1)の場合は何もしない。
                if (currentStage.GoalX >= 0) SelectAndScrollTo(null, currentStage.GoalX, currentStage.GoalY);
                break;
            case EventTrigger t:
                // トリガーの場合はキャンバス上へジャンプするだけでなく、編集ダイアログも自動的に開く。
                SelectAndScrollTo(t, t.x, t.y);
                OpenTriggerEditor(t);
                break;
            case PlacedEnemy en:
                SelectAndScrollTo(en, en.X, en.Y);
                break;
            case PlacedGimmick g:
                SelectAndScrollTo(g, g.X, g.Y);
                break;
            case PlacedItem it:
                SelectAndScrollTo(it, it.X, it.Y);
                break;
        }
    }

    // キャンバス上の指定オブジェクトへスクロールして選択状態にする。
    // target : プロパティグリッドとキャンバスの選択状態に反映するオブジェクト（スタート/ゴールの場合はnull）。
    // worldX/worldY : ゲーム内座標系での目標位置。
    private void SelectAndScrollTo(object? target, float worldX, float worldY)
    {
        if (currentStage == null) return;
        // ゲーム内座標系（GAME_TILE単位）からエディタの描画座標系（TILE_SIZE単位）へ変換するための倍率。
        float scale = (float)MapCanvas.TILE_SIZE / MapCanvas.GAME_TILE;
        int px = (int)(worldX * scale);
        int py = (int)(worldY * scale);

        // 対象がキャンバスのちょうど中央に来るようなスクロール位置を計算し、マップ範囲外に出ないようクランプする。
        int maxX = Math.Max(0, currentStage.MapW * MapCanvas.TILE_SIZE - mapCanvas.Width + 50);
        int maxY = Math.Max(0, currentStage.MapH * MapCanvas.TILE_SIZE - mapCanvas.Height + 50);
        int scrollX = Math.Clamp(px - mapCanvas.Width / 2, 0, maxX);
        int scrollY = Math.Clamp(py - mapCanvas.Height / 2, 0, maxY);

        // キャンバスの選択オブジェクトとスクロール位置を更新し、スクロールバーのつまみ位置も揃える。
        mapCanvas.SelectedObject = target;
        mapCanvas.ScrollX = scrollX;
        mapCanvas.ScrollY = scrollY;
        UpdateScrollBars();
        hScrollMap.Value = Math.Min(scrollX, hScrollMap.Maximum);
        vScrollMap.Value = Math.Min(scrollY, vScrollMap.Maximum);
        // プロパティグリッドにも同じオブジェクトを表示し、その場でプロパティを編集できるようにする。
        propertyGrid.SelectedObject = target;
        mapCanvas.Invalidate();
    }

    // 現在の編集モードに応じて、画面上部のレイヤー情報ラベル（lblLayerInfo）の文言を切り替える。
    private void UpdateLayerInfo()
    {
        lblLayerInfo.Text = mapCanvas.CurrentMode switch
        {
            MapCanvas.EditMode.DecoLayerBack => "Layer 1: 遠景レイヤー編集中",
            MapCanvas.EditMode.Tile => "Layer 2: メイン（地形）編集中",
            MapCanvas.EditMode.DecoLayerFront => "Layer 3: 近景レイヤー編集中",
            MapCanvas.EditMode.Trigger => "Layer 4: トリガー配置（ドラッグで矩形）",
            MapCanvas.EditMode.PlayerStart => "Layer 4: 開始位置設定",
            MapCanvas.EditMode.Goal => "Layer 4: ゴール設定",
            MapCanvas.EditMode.Enemy => "Layer 4: 敵の配置",
            MapCanvas.EditMode.Gimmick => "Layer 4: ギミックの配置",
            MapCanvas.EditMode.Item => "Layer 4: アイテムの配置",
            MapCanvas.EditMode.TestPlay => "📍 クリックでテストプレイ開始位置を指定",
            _ => "選択 / 消去 モード"
        };
    }

    // ===== プロパティ =====
    // キャンバス上でオブジェクトが選択されたときに呼ばれる。プロパティグリッドに選択中オブジェクトを表示する。
    private void mapCanvas_ObjectSelected(object? sender, EventArgs e)
    {
        propertyGrid.SelectedObject = mapCanvas.SelectedObject;
        // トリガーが選択された場合は編集フォームを開く (Feature 5)。
        // トリガーだけは座標や種別以外に多くの設定項目を持つため、専用の編集ダイアログで詳細を編集させる。
        if (mapCanvas.SelectedObject is EventTrigger trigger)
        {
            OpenTriggerEditor(trigger);
        }
    }

    // StageModified はドラッグ中のタイル1マスごとに何度も発火するため、
    // ここでは即時保存せずキャンバスの再描画のみ行う（保存はストローク完了時の EditCompleted にまとめる）。
    // つまり、このイベントハンドラは意図的に「何もしない」実装になっている（過剰な保存でパフォーマンスが落ちるのを防ぐため）。
    private void mapCanvas_StageModified(object? sender, EventArgs e) { }

    // 1回分の編集操作（例: ドラッグでのタイル塗り一連の動作）が完了したときに呼ばれる。
    // ここでファイルへの保存とUndo履歴への追加をまとめて行う。
    private void mapCanvas_EditCompleted(object? sender, EventArgs e)
    {
        if (currentStage != null)
        {
            SaveCurrentStage();
            historyMgr.Push(currentStage);
        }
    }

    // Feature 4: ここからプレイ （マップクリックでテストプレイ開始位置確定）。
    // 「テストプレイ」モード中にマップ上をクリックすると、そのクリック位置を開始位置とした
    // 一時ステージを作成し、実際にそこからゲームを起動して動作確認できるようにする機能。
    private void mapCanvas_TestPlayClicked(object? sender, (float wx, float wy) pos)
    {
        if (currentStage == null) return;
        // テストプレイモードを解除して通常モードへ戻す（クリック1回で自動的にLayer2/ペンモードに復帰させる）。
        tsbLayer2.Checked = true;
        TsbLayer_Click(tsbLayer2, EventArgs.Empty);

        // 一時JSONを生成して起動。
        try
        {
            SaveCurrentStage(); // まず現在の編集内容を通常のステージファイルとして保存しておく。
            // 本来のステージファイルとは別に、開始位置だけクリックした座標に差し替えた一時ファイルを書き出す。
            currentStage.SaveAsTestPlay(testPlayJsonPath, pos.wx, pos.wy);
            LaunchGameWithFile("_test_play.json");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"テストプレイの準備に失敗しました:\n{ex.Message}", "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // Feature 5: トリガー矩形が確定された時（キャンバス上でドラッグしてトリガーの範囲を描き終えたタイミング）。
    private void mapCanvas_TriggerPlaced(object? sender, EventTrigger trigger)
    {
        // 新規トリガーとしてイベントエディタを開く。
        OpenTriggerEditor(trigger, isNew: true);
    }

    // トリガー編集画面などで「ジャンプ先ステージ」を選ばせるための、テスト用一時ファイルを除いたステージ名一覧を返す。
    private List<string> GetStageFileNames()
    {
        if (!Directory.Exists(stagesPath)) return new List<string>();
        return Directory.GetFiles(stagesPath, "*.json")
            .Select(Path.GetFileName)
            .Where(n => n != null && n != "_test_play.json")
            .Select(n => n!)
            .ToList();
    }

    // トリガー（イベント）の編集ダイアログを開く。
    // trigger : 編集対象のトリガー（新規作成時は仮のデータが渡ってくる）。
    // isNew   : 新規作成中なら true、既存トリガーの編集なら false。
    private void OpenTriggerEditor(EventTrigger trigger, bool isNew = false)
    {
        var form = new EventEditorForm(trigger, assets, GetStageFileNames());
        if (form.ShowDialog() == DialogResult.OK)
        {
            if (isNew && currentStage != null)
            {
                // 新規作成の場合は、編集結果をそのままトリガー一覧に追加する。
                currentStage.Triggers.Add(form.ResultTrigger);
            }
            else if (!isNew && currentStage != null)
            {
                // 既存トリガーを更新。同じidを持つ要素を探して置き換える。
                var idx = currentStage.Triggers.FindIndex(t => t.id == trigger.id);
                if (idx >= 0) currentStage.Triggers[idx] = form.ResultTrigger;
            }
            SaveCurrentStage();
            mapCanvas.Invalidate();
        }
        else if (!isNew)
        {
            // キャンセル時 — 既存トリガーの編集をキャンセルした場合は、削除するかどうかを確認する。
            // （新規作成をキャンセルした場合は、そもそもまだ何も追加されていないのでこの確認は不要＝isNewの時は素通り）
            if (MessageBox.Show("このトリガーを削除しますか？", "確認", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                currentStage?.Triggers.RemoveAll(t => t.id == trigger.id);
                SaveCurrentStage();
                mapCanvas.Invalidate();
            }
        }
    }

    // ===== プレイヤー設定変更 =====
    // プレイヤー開始座標・アクション許可・ジャンプ力・速度など、左パネルの各種設定値が変更されたときに呼ばれる。
    // ステージ読み込み中（_loading中）に発生した変更イベントは無視し、それ以外は変更のたびに即座に保存する。
    private void PlayerSetting_Changed(object? sender, EventArgs e)
    {
        if (_loading || currentStage == null) return;
        SaveCurrentStage();
    }

    // ===== 保存 =====
    // 現在UIに表示されている各種設定値をすべて currentStage に書き戻し、ステージファイルとして保存する。
    private void SaveCurrentStage()
    {
        if (currentStage == null || string.IsNullOrEmpty(currentStageFile)) return;

        // プレイヤーの開始座標をUIの数値からステージデータへ書き戻す。
        currentStage.PlayerStartX = (float)numStartX.Value;
        currentStage.PlayerStartY = (float)numStartY.Value;
        // プレイヤーが使えるアクション（2段ジャンプ・ダッシュ・火の玉・飛行）のチェック状態を書き戻す。
        currentStage.Capabilities.canDoubleJump = chkDoubleJump.Checked;
        currentStage.Capabilities.canDash = chkDash.Checked;
        currentStage.Capabilities.canShootFireball = chkFireball.Checked;
        currentStage.Capabilities.canFly = chkFly.Checked;
        // ジャンプ力・移動速度の数値を書き戻す。
        currentStage.Capabilities.baseJumpPower = (int)numJumpPower.Value;
        currentStage.Capabilities.baseSpeed = (float)numSpeed.Value;

        // 編集ツール（巻き戻し/一時停止/早送り/画面エフェクト/オブジェクト編集）の許可設定を書き戻す。
        currentStage.EditTools.rewindEnabled = chkEditRewind.Checked;
        currentStage.EditTools.pauseEnabled = chkEditPause.Checked;
        currentStage.EditTools.fastForwardEnabled = chkEditFastForward.Checked;
        currentStage.EditTools.screenEffectEnabled = chkEditScreenFx.Checked;
        currentStage.EditTools.objectEditEnabled = chkEditObjectEdit.Checked;

        // 編集コストゲージの最大値・自然回復量・各操作の消費量（秒あたり／1回あたり）を書き戻す。
        currentStage.EditCost.maxCost = (float)numEditMaxCost.Value;
        currentStage.EditCost.regenPerSec = (float)numEditRegen.Value;
        currentStage.EditCost.drainRewindPerSec = (float)numEditDrainRewind.Value;
        currentStage.EditCost.drainPausePerSec = (float)numEditDrainPause.Value;
        currentStage.EditCost.drainFastForwardPerSec = (float)numEditDrainFF.Value;
        currentStage.EditCost.drainScreenEffectPerSec = (float)numEditDrainScreenFx.Value;
        currentStage.EditCost.flatColorCycle = (float)numEditFlatColorCycle.Value;
        currentStage.EditCost.flatMenuToggle = (float)numEditFlatMenuToggle.Value;
        currentStage.EditCost.flatSpeedChange = (float)numEditFlatSpeedChange.Value;
        currentStage.EditCost.flatDirectionFlip = (float)numEditFlatDirectionFlip.Value;
        currentStage.EditCost.flatResetAll = (float)numEditFlatResetAll.Value;

        // ここまでの内容をすべてJSONファイルとして書き出す。
        currentStage.SaveToFile(Path.Combine(stagesPath, currentStageFile));
    }

    // 「保存」ボタンが押されたときの処理。保存を実行し、完了メッセージを表示する。
    private void btnSave_Click(object? sender, EventArgs e)
    {
        SaveCurrentStage();
        MessageBox.Show("ステージを保存しました！", "保存完了", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    // ===== ステージサイズ変更 =====
    // 「リサイズ」ボタンが押されたときの処理。マップの幅・高さを変更する（縮小時は範囲外データが失われる）。
    private void btnResize_Click(object? sender, EventArgs e)
    {
        if (currentStage == null) return;
        int newW = (int)numMapW.Value;
        int newH = (int)numMapH.Value;
        // サイズが変わっていなければ何もしない。
        if (newW == currentStage.MapW && newH == currentStage.MapH) return;

        // データが失われる可能性があるため、実行前に必ず確認ダイアログを出す。
        var res = MessageBox.Show(
            $"ステージサイズを {currentStage.MapW}×{currentStage.MapH} → {newW}×{newH} に変更します。\n範囲外のデータは失われます。続けますか？",
            "サイズ変更確認", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (res != DialogResult.Yes) return;

        // 実際にマップサイズを変更し、スクロールバー・キャンバス・情報表示・保存をすべて更新する。
        currentStage.ResizeMap(newW, newH);
        UpdateScrollBars();
        mapCanvas.Invalidate();
        lblCurrentStage.Text = $"編集中: {currentStageFile}  ({newW}×{newH})";
        SaveCurrentStage();
    }

    // ===== 新規・削除 =====
    // 「＋新規」ボタンが押されたときの処理。テキストボックスに入力された名前で新しいステージを作成する。
    private void btnCreateStage_Click(object? sender, EventArgs e)
    {
        string name = txtNewStage.Text.Trim();
        if (string.IsNullOrEmpty(name)) { MessageBox.Show("名前を入力してください"); return; }
        CreateNewStage(name);
        txtNewStage.Text = "";
    }

    // 指定された名前で新しいステージ（空のStageData）を作成し、保存してから一覧・選択状態を更新する。
    private void CreateNewStage(string name)
    {
        // 拡張子が付いていなければ自動的に ".json" を補う。
        if (!name.EndsWith(".json")) name += ".json";
        currentStageFile = name;
        // デフォルト値だけを持つ空のステージデータを新規作成する。
        currentStage = new StageData();
        SaveCurrentStage();
        RefreshStageList();
        // 作成したステージが一覧の中で選択された状態になるようにインデックスを探して選択する。
        for (int i = 0; i < lstStages.Items.Count; i++)
            if (lstStages.Items[i].ToString() == name) { lstStages.SelectedIndex = i; break; }
    }

    // 「🗑削除」ボタンが押されたときの処理。確認の上で現在選択中のステージファイルを削除する。
    private void btnDeleteStage_Click(object? sender, EventArgs e)
    {
        if (string.IsNullOrEmpty(currentStageFile)) return;
        if (MessageBox.Show($"「{currentStageFile}」を削除しますか？", "確認",
            MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
        {
            string path = Path.Combine(stagesPath, currentStageFile);
            if (File.Exists(path)) File.Delete(path);
            // 削除後は編集中のステージが無い状態に戻し、一覧を更新する。
            currentStageFile = ""; currentStage = null;
            RefreshStageList();
        }
    }

    // ===== プレイテスト =====
    // 「▶プレイ」ボタン（ツールバー・メニュー共通）が押されたときの処理。現在のステージでゲームを起動する。
    private void btnPlay_Click(object? sender, EventArgs e) => LaunchGame();

    // 現在編集中のステージを保存してから、そのステージファイルでゲームを起動する。
    private void LaunchGame()
    {
        SaveCurrentStage();
        LaunchGameWithFile(currentStageFile);
    }

    // 指定したステージファイル名を引数としてゲーム本体(exe)を起動する共通処理。
    // 起動前にexeの存在・ステージファイル名の指定有無をチェックし、問題があればエラーメッセージを表示する。
    private void LaunchGameWithFile(string stageFileName)
    {
        if (!File.Exists(exePath))
        { MessageBox.Show($"ゲーム実行ファイルが見つかりません:\n{exePath}", "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error); return; }
        if (string.IsNullOrEmpty(stageFileName))
        { MessageBox.Show("ステージを選択してください。", "選択エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        // ゲーム本体をプロジェクトルートを作業ディレクトリとして起動し、ステージファイル名をコマンドライン引数として渡す。
        try { Process.Start(new ProcessStartInfo { FileName = exePath, WorkingDirectory = projectRoot, Arguments = stageFileName }); }
        catch (Exception ex) { MessageBox.Show($"起動失敗:\n{ex.Message}", "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    // Feature 4: ここからプレイ。
    // Feature: UI改善（友人フィードバック対応）— 確認モーダルポップアップはうざいとの指摘のため削除。
    // 案内は既存のlblLayerInfo（UpdateLayerInfo内で「📍 クリックでテストプレイ開始位置を指定」を表示）に一本化する。
    // このボタンを押すと即座にゲームが始まるわけではなく、まず「テストプレイ開始位置を指定するモード」に入る。
    // 実際の起動は、続けてキャンバス上をクリックした瞬間（mapCanvas_TestPlayClicked）に行われる。
    private void btnTestPlay_Click(object? sender, EventArgs e)
    {
        if (currentStage == null) { MessageBox.Show("ステージを選択してください。"); return; }
        mapCanvas.CurrentMode = MapCanvas.EditMode.TestPlay;
        UpdateLayerInfo();
    }

    // Feature 1: 背景設定。背景レイヤー（多重スクロール背景など）を設定する専用画面を開く。
    private void btnBgSettings_Click(object? sender, EventArgs e)
    {
        if (currentStage == null) { MessageBox.Show("ステージを選択してください。"); return; }
        var form = new BackgroundSettingsForm(projectRoot, currentStage.Backgrounds);
        if (form.ShowDialog() == DialogResult.OK)
        {
            // OKで閉じられた場合のみ、編集結果をステージに反映して保存する。
            currentStage.Backgrounds = form.ResultLayers;
            SaveCurrentStage();
        }
    }

    // Feature 3: サウンド管理。BGM/SE/UI効果音のカタログ（一覧）を編集する画面を開く。
    private void btnSoundMgr_Click(object? sender, EventArgs e)
    {
        var form = new SoundManagerForm(projectRoot, assets.Bgm, assets.Se, assets.UiSe,
            assets.Enemies, assets.Gimmicks, assets.Items, assets.CommonEvents);
        if (form.ShowDialog() == DialogResult.OK)
        {
            // 編集後のBGM/SE/UI効果音一覧をアセット定義へ反映し、assetsフォルダに保存する。
            assets.Bgm = form.ResultBgm;
            assets.Se = form.ResultSe;
            assets.UiSe = form.ResultUiSe;
            assets.SaveToFolder(assetsPath);
        }
    }

    // Feature: サウンド・アセット管理の刷新 — カタログのBGM/SE IDを敵/ギミック/アイテム/現在のステージへ割り当てる。
    // 「サウンド管理」ではBGM/SEそのものを登録するだけだったが、こちらは「どの敵がどのSEを鳴らすか」
    // といった実際の割り当て作業を行うための専用画面を開く。
    private void btnSoundAssign_Click(object? sender, EventArgs e)
    {
        // 現在ステージが開かれていればそのファイル名とBGM設定を渡し、画面側でステージへのBGM割り当ても行えるようにする。
        string? stageName = currentStage != null && !string.IsNullOrEmpty(currentStageFile) ? currentStageFile : null;
        string stageBgmId = currentStage?.BgmId ?? "";
        var form = new SoundAssignmentForm(assets.Enemies, assets.Gimmicks, assets.Items,
            assets.Se, assets.UiSe, assets.Bgm, stageName, stageBgmId);
        if (form.ShowDialog() == DialogResult.OK)
        {
            // 敵・ギミック・アイテムそれぞれに割り当てられたサウンドIDの変更をアセット定義に反映して保存する。
            assets.Enemies = form.ResultEnemies;
            assets.Gimmicks = form.ResultGimmicks;
            assets.Items = form.ResultItems;
            assets.SaveToFolder(assetsPath);

            // ステージのBGM割り当てが変更されていれば、そちらも保存する。
            if (form.ResultStageBgmId != null && currentStage != null)
            {
                currentStage.BgmId = form.ResultStageBgmId;
                SaveCurrentStage();
            }
        }
    }

    // Feature 2: アニメーションエディタ。敵/ギミック/アイテムのアニメーション（コマ送り設定）を編集する。
    private void btnAnimEditor_Click(object? sender, EventArgs e)
    {
        // アセットIDを選択させるためのリスト。敵・ギミック・アイテムすべてのIDをまとめて1つのリストにする。
        var ids = assets.Enemies.Select(x => x.id)
            .Concat(assets.Gimmicks.Select(x => x.id))
            .Concat(assets.Items.Select(x => x.id))
            .ToList();

        // 編集対象になるアセットが1つも登録されていなければ、その旨を伝えて終了する。
        if (ids.Count == 0) { MessageBox.Show("アセットが登録されていません。先にアセット管理でアセットを追加してください。"); return; }

        // 簡易選択ダイアログ。専用フォームを作らず、その場でListBoxとOKボタンだけの小さなフォームを組み立てて表示する。
        string? selectedId = null;
        using var picker = new Form
        {
            Text = "アニメーション編集するアセットを選択", Size = new Size(360, 200),
            StartPosition = FormStartPosition.CenterParent
        };
        var lb = new ListBox { Dock = DockStyle.Fill };
        ids.ForEach(id => lb.Items.Add(id));
        var btnOk = new Button { Text = "OK", Dock = DockStyle.Bottom, DialogResult = DialogResult.OK };
        picker.Controls.AddRange(new Control[] { lb, btnOk });
        picker.AcceptButton = btnOk;
        if (picker.ShowDialog() == DialogResult.OK && lb.SelectedItem != null)
            selectedId = lb.SelectedItem.ToString();

        // 何も選ばれずに閉じられた場合は処理を打ち切る。
        if (string.IsNullOrEmpty(selectedId)) return;

        // 選ばれたアセットに既存のアニメーション設定があればそれを、無ければ空の新規設定を編集対象とする。
        var existing = assets.Animations.FirstOrDefault(a => a.assetId == selectedId) ?? new AnimationSet { assetId = selectedId };
        var form = new AnimationEditorForm(projectRoot, existing);
        if (form.ShowDialog() == DialogResult.OK)
        {
            // 既存のアニメーション設定があれば置き換え、無ければ新規追加する。
            var idx = assets.Animations.FindIndex(a => a.assetId == selectedId);
            if (idx >= 0) assets.Animations[idx] = form.ResultSet;
            else assets.Animations.Add(form.ResultSet);
            assets.SaveToFolder(assetsPath);
        }
    }

    // ===== アセット管理 =====
    // UI改善（構造改修フェーズ5e）— 唯一の挙動変更コミット。以前はここから
    // AssetManagerForm→PartsEditorForm→BehaviorScriptEditorForm/HitboxEditorForm と
    // 最大3重にShowDialog()が積み重なっていた（ダイアログの上にダイアログが開き、さらにその上に…という
    // ネスト構造で、閉じる操作も「戻る」ではなく「ウィンドウを閉じる」を何度も繰り返す必要があり分かりにくかった）。
    // ここではWorkbenchShellFormを1つだけ開き、
    // アセット管理・パーツ編集・挙動スクリプト/当たり判定/サイズ/コモンイベント編集を
    // すべて同じウィンドウ内のページ遷移（パンくず＋戻るボタン）に置き換える。
    // 各PageControlの「編集を頼む」イベント自体はフェーズ5b～5dで既に用意済みで、
    // AssetManagerForm/PartsEditorForm（薄いラッパー）は引き続きShowDialog()で
    // 応じているため、このメソッドが「shell.NavigateToで応じる」別の応じ方を
    // 提供するだけで済む（＝既存のイベント購読の仕組みはそのままに、応答の仕方だけをこの関数内で差し替えている）。
    private void btnAssetManager_Click(object? sender, EventArgs e)
    {
        // すべてのページ遷移の入れ物となる、1つだけのシェル（外枠）ウィンドウ。
        var shell = new WorkbenchShellForm();

        // 「当たり判定編集」への遷移要求を処理するローカル関数。
        // 保存されたら結果をコールバック(onSaved)に渡してシェルを1つ前のページへ戻し、
        // キャンセルされた場合も同様に前のページへ戻る。
        void HandleHitboxRequest(string fullPath, int ox, int oy, int w, int h, Action<int, int, int, int> onSaved)
        {
            var page = new HitboxEditorPageControl(fullPath, ox, oy, w, h);
            page.Saved += (s, ev) => { onSaved(page.HitboxOffsetX, page.HitboxOffsetY, page.HitboxWidth, page.HitboxHeight); shell.GoBack(); };
            page.Cancelled += (s, ev) => shell.GoBack();
            shell.NavigateTo(page, "当たり判定編集", page.PrimaryActionButton, page.SecondaryActionButton);
        }

        // 「サイズ編集」への遷移要求を処理するローカル関数。考え方はHandleHitboxRequestと同じ。
        void HandleSizeRequest(string fullPath, float curScale, Action<float> onSaved)
        {
            var page = new SizeEditorPageControl(fullPath, curScale);
            page.Saved += (s, ev) => { onSaved(page.ResultScale); shell.GoBack(); };
            page.Cancelled += (s, ev) => shell.GoBack();
            shell.NavigateTo(page, "サイズ編集", page.PrimaryActionButton, page.SecondaryActionButton);
        }

        // 「挙動スクリプト編集」への遷移要求を処理するローカル関数。考え方は上と同じ。
        void HandleBehaviorScriptRequest(string label, JArray initialScript, Action<JArray> onSaved)
        {
            var page = new BehaviorScriptEditorPageControl(label, initialScript);
            page.Saved += (s, script) => { onSaved(script); shell.GoBack(); };
            page.Cancelled += (s, ev) => shell.GoBack();
            shell.NavigateTo(page, label, page.PrimaryActionButton, page.SecondaryActionButton);
        }

        // 「パーツ編集」への遷移要求を処理するローカル関数。
        void HandlePartsEditRequest(string label, List<PartDef> initialParts, string baseSpritePath, Action<List<PartDef>> onSaved)
        {
            var page = new PartsEditorPageControl(label, initialParts, projectRoot, baseSpritePath);
            page.Saved += (s, parts) => { onSaved(parts); shell.GoBack(); };
            page.Cancelled += (s, ev) => shell.GoBack();
            // パーツ編集の中からさらに当たり判定/挙動スクリプトを開く場合も、同じシェル内でページ遷移する
            // （ここが以前の3重ネストのうち最も深い階層。ここもGoBack一発で親のパーツ編集に戻れる）。
            page.HitboxEditRequested += HandleHitboxRequest;
            page.BehaviorScriptEditRequested += HandleBehaviorScriptRequest;
            shell.NavigateTo(page, label, page.PrimaryActionButton, page.SecondaryActionButton);
        }

        // 「コモンイベント編集」への遷移要求を処理するローカル関数。
        // ステージ名一覧の取得だけルート画面(rootPage)経由で行う必要があるため、rootPageを引数として受け取っている。
        void HandleCommonEventRequest(AssetManagerPageControl rootPage, CommonEventDef ev, Action<CommonEventDef> onSaved)
        {
            var page = new CommonEventEditorPageControl(ev, assets, rootPage.GetStageFileNames());
            page.Saved += (s, result) => { onSaved(result); shell.GoBack(); };
            page.Cancelled += (s, ev2) => shell.GoBack();
            shell.NavigateTo(page, "コモンイベント編集", page.PrimaryActionButton, page.SecondaryActionButton);
        }

        // ルート（最初に表示される）ページ = アセット管理画面本体。
        var rootPage = new AssetManagerPageControl(assetsPath, assets);
        // ルートページで保存/キャンセルされたら、シェル全体をその結果でダイアログとして閉じる。
        rootPage.Saved += (s, ev) => { shell.DialogResult = DialogResult.OK; shell.Close(); };
        rootPage.Cancelled += (s, ev) => { shell.DialogResult = DialogResult.Cancel; shell.Close(); };
        // ルートページから発生しうる各種「編集を頼む」イベントに、上で定義したローカル関数をそれぞれ結び付ける。
        rootPage.HitboxEditRequested += HandleHitboxRequest;
        rootPage.SizeEditRequested += HandleSizeRequest;
        rootPage.BehaviorScriptEditRequested += HandleBehaviorScriptRequest;
        rootPage.PartsEditRequested += HandlePartsEditRequest;
        rootPage.CommonEventEditRequested += (ev, onSaved) => HandleCommonEventRequest(rootPage, ev, onSaved);

        // シェルの最初のページとしてルートページを表示する。
        shell.NavigateTo(rootPage, "アセット管理", rootPage.PrimaryActionButton, rootPage.SecondaryActionButton);

        // シェル全体をモーダル表示し、OKで閉じられた（＝どこかのページで保存された）場合のみ
        // アセット定義を読み直してパレット・マップキャンバスの表示を最新化する。
        if (shell.ShowDialog(this) == DialogResult.OK)
        { assets = AssetDefinitions.LoadFromFolder(assetsPath); RefreshPalette(); if (currentStage != null) { mapCanvas.Assets = assets; mapCanvas.RefreshTileColors(); } }
    }

    // ===== タイルエディタ =====
    // タイル（地形パーツ）の定義を編集する専用画面を開く。保存されたらアセットとパレット・キャンバス表示を更新する。
    private void btnTileEditor_Click(object? sender, EventArgs e)
    {
        var form = new TileEditorForm(assetsPath, assets.Tiles);
        if (form.ShowDialog() == DialogResult.OK)
        { assets = AssetDefinitions.LoadFromFolder(assetsPath); RefreshPalette(); if (currentStage != null) { mapCanvas.Assets = assets; mapCanvas.RefreshTileColors(); } }
    }

    // ===== CSV インポート =====
    // CSVファイルを選ばせて読み込み、その内容から新しいステージ(JSON)を生成する処理。
    // 表計算ソフトでマップを組み立ててから取り込みたい場合などに使う機能。
    private void btnImportCsv_Click(object? sender, EventArgs e)
    {
        using var ofd = new OpenFileDialog { Filter = "CSVファイル|*.csv|すべて|*.*", Title = "CSVからステージを生成" };
        if (ofd.ShowDialog() != DialogResult.OK) return;
        try
        {
            // CSVを解析してステージデータを組み立てる。
            var data = StageData.LoadFromCsv(ofd.FileName);
            // 生成するステージのファイル名は、CSVファイル名から拡張子を除いたものに ".json" を付けたものにする。
            string baseName = Path.GetFileNameWithoutExtension(ofd.FileName) + ".json";
            string savePath = Path.Combine(stagesPath, baseName);
            data.SaveToFile(savePath);

            // 生成完了メッセージを出し、ステージ一覧を更新した上で、今作ったステージを自動的に選択状態にする。
            MessageBox.Show($"CSVからステージ「{baseName}」を生成しました！\nサイズ: {data.MapW}×{data.MapH}", "生成完了", MessageBoxButtons.OK, MessageBoxIcon.Information);
            RefreshStageList();
            for (int i = 0; i < lstStages.Items.Count; i++)
                if (lstStages.Items[i].ToString() == baseName) { lstStages.SelectedIndex = i; break; }
        }
        catch (Exception ex)
        { MessageBox.Show($"CSVの読み込みに失敗しました:\n{ex.Message}", "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    // 画面下部のステータスバーに、ゲーム本体(exe)がビルド済みかどうかを表示する。
    // 見つかれば緑色で「検出済み」、見つからなければ赤色で警告を表示し、プレイ関連の操作が失敗する原因を事前に伝える。
    private void UpdateStatus()
    {
        if (File.Exists(exePath)) { lblStatus.Text = "✅ ゲームエンジン検出済み"; lblStatus.ForeColor = Color.Green; }
        else { lblStatus.Text = "⚠ ゲームのビルドが見つかりません"; lblStatus.ForeColor = Color.Red; }
    }
}
