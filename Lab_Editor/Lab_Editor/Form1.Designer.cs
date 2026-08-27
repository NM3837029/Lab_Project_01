namespace Lab_Editor;

// Form1のUIレイアウトを定義するpartialクラス（Windows Forms デザイナ形式の宣言部分）。
// 本ファイルはVisual Studioのデザイナで自動生成される想定のファイルだが、実際には手書きで
// コントロール生成・イベント購読・レイアウト設定をすべて記述している（絶対座標配置＋Dock/SplitContainer混在）。
partial class Form1
{
    // WinFormsのコンポーネントをまとめて管理するコンテナ（Disposeで一括破棄するために使う）。
    private System.ComponentModel.IContainer components = null;
    // フォーム破棄時、componentsに登録された全コンポーネント（Timerなど）をまとめて解放する。
    protected override void Dispose(bool disposing) { if (disposing && components != null) components.Dispose(); base.Dispose(disposing); }

    // ===== メニュー・ツールバー =====
    private System.Windows.Forms.MenuStrip menuStrip1 = null!;  // 画面上部のメニューバー（ファイル/編集/アセット管理/プレイ/ヘルプ）
    private System.Windows.Forms.ToolStrip toolStrip1 = null!;  // メニュー直下のツールバー（レイヤー切替・ツール切替・プレイボタン）

    // ===== コントロール宣言 =====
    private MapCanvas mapCanvas = null!;                        // マップを描画・編集するメインキャンバス
    private System.Windows.Forms.HScrollBar hScrollMap = null!;  // マップキャンバスの水平スクロールバー
    private System.Windows.Forms.VScrollBar vScrollMap = null!;  // マップキャンバスの垂直スクロールバー

    // Feature: UI改善（構造改修フェーズ2）— 絶対座標配置だと最大化/リサイズ時に
    // レイアウトが崩れる（PropertyGridがtabRightの裏に隠れる等）ため、
    // SplitContainer/TableLayoutPanelによるDockベースの構成に置き換える。
    // 外側: 左パネル | それ以外。中: 中央キャンバス | 右パネル。右: タイル/配置タブ | プロパティグリッド。
    private System.Windows.Forms.SplitContainer splitOuter = null!;          // 最も外側の分割：左パネル | それ以外全体
    private System.Windows.Forms.SplitContainer splitCenterRight = null!;    // 中央分割：キャンバスエリア | 右パネル
    private System.Windows.Forms.SplitContainer splitRightVertical = null!;  // 右パネル内の分割：タブ群 | プロパティグリッド
    private System.Windows.Forms.TableLayoutPanel tlpCanvas = null!;         // キャンバス＋スクロールバーを2x2で配置するテーブル
    private System.Windows.Forms.Panel pnlInfoBar = null!;                   // キャンバス上部の情報バー（ステージ名・レイヤー情報）
    private System.Windows.Forms.Panel pnlStatusBar = null!;                 // 画面最下部の全幅ステータスバー

    // 左パネル (TabControlで整理)
    private System.Windows.Forms.TabControl tabLeft = null!;    // 左側のタブコントロール本体
    private System.Windows.Forms.TabPage tabStages = null!;     // 「ステージ」タブ（ステージ一覧・作成・削除）
    private System.Windows.Forms.TabPage tabMapProps = null!;   // 「マップ設定」タブ（サイズ・プレイヤー設定・編集コスト設定）

    private System.Windows.Forms.ListBox lstStages = null!;                         // ステージ一覧リスト
    private System.Windows.Forms.TextBox txtNewStage = null!;                       // 新規ステージ名の入力欄
    private System.Windows.Forms.Button btnCreateStage = null!, btnDeleteStage = null!;  // ステージの新規作成／削除ボタン
    private System.Windows.Forms.Button btnImportCsv = null!;                       // CSVからステージをインポートするボタン

    private System.Windows.Forms.NumericUpDown numStartX = null!, numStartY = null!;  // プレイヤー開始座標のX/Y入力欄
    private System.Windows.Forms.CheckBox chkDoubleJump = null!, chkDash = null!, chkFireball = null!, chkFly = null!;  // プレイヤーの初期アクション許可設定（2段ジャンプ/ダッシュ/火の玉/飛行）
    private System.Windows.Forms.NumericUpDown numJumpPower = null!, numSpeed = null!;  // ジャンプ力・移動速度の数値入力欄
    private System.Windows.Forms.NumericUpDown numMapW = null!, numMapH = null!;        // マップの幅・高さ（タイル数）入力欄

    // 編集ツール許可設定・編集コスト経済設定
    private System.Windows.Forms.CheckBox chkEditRewind = null!, chkEditPause = null!, chkEditFastForward = null!, chkEditScreenFx = null!, chkEditObjectEdit = null!, chkEditCut = null!;  // 各編集ツール（巻き戻し/一時停止/早送り/画面エフェクト/個別オブジェクト編集）をゲーム中に使用可能にするかの許可チェックボックス群
    private System.Windows.Forms.NumericUpDown numEditMaxCost = null!, numEditRegen = null!, numEditDrainRewind = null!, numEditDrainPause = null!, numEditDrainFF = null!, numEditDrainScreenFx = null!;  // 編集コストゲージの最大値・自然回復量・各ツール使用時の消費量（秒あたり）
    private System.Windows.Forms.NumericUpDown numEditFlatColorCycle = null!, numEditFlatMenuToggle = null!, numEditFlatSpeedChange = null!, numEditFlatDirectionFlip = null!, numEditFlatResetAll = null!, numEditFlatCutCreate = null!;  // 単発アクション（色フィルタ切替・メニュートグル等）ごとの固定消費コスト
    private System.Windows.Forms.Button btnResize = null!;  // マップサイズ変更を確定するボタン

    // ツールバー用ボタン等
    private System.Windows.Forms.ToolStripButton tsbLayer1 = null!;  // レイヤー1（遠景＝装飾後景）切替ボタン
    private System.Windows.Forms.ToolStripButton tsbLayer2 = null!;  // レイヤー2（メインタイル）切替ボタン
    private System.Windows.Forms.ToolStripButton tsbLayer3 = null!;  // レイヤー3（近景＝装飾前景）切替ボタン
    private System.Windows.Forms.ToolStripButton tsbLayer4 = null!; // イベントモード（敵/ギミック/アイテム/トリガー等）切替ボタン
    private System.Windows.Forms.ToolStripSeparator toolStripSeparator1 = null!;  // レイヤー切替ボタン群とツール切替ボタン群の間の区切り線
    private System.Windows.Forms.ToolStripButton tsbPen = null!;     // ペン（配置）ツール切替ボタン
    private System.Windows.Forms.ToolStripButton tsbEraser = null!;  // 消しゴムツール切替ボタン
    private System.Windows.Forms.ToolStripButton tsbSelect = null!;  // 選択ツール切替ボタン
    private System.Windows.Forms.ToolStripSeparator toolStripSeparator2 = null!;  // ツール切替ボタン群とプレイ系ボタンの間の区切り線
    private System.Windows.Forms.ToolStripButton tsbPlay = null!;          // 通常テストプレイ開始ボタン
    private System.Windows.Forms.ToolStripButton tsbTestPlayHere = null!;  // 「ここから」テストプレイ開始ボタン（クリック位置指定モード）

    private System.Windows.Forms.Label lblCurrentStage = null!, lblStatus = null!, lblLayerInfo = null!;  // 編集中ステージ名／ステータスバー文言／現在レイヤー情報を表示するラベル群

    // 右パネル (TabControl)
    private System.Windows.Forms.TabControl tabRight = null!;       // 右側のタブコントロール本体
    private System.Windows.Forms.TabPage tabTilePalette = null!;    // 「タイル」タブ（タイルパレット）
    private System.Windows.Forms.TabPage tabEventPalette = null!;   // 「配置対象」タブ（敵/ギミック/アイテム/トリガー等の選択）
    private System.Windows.Forms.TabPage tabEventList = null!;      // 「イベントリスト」タブ（配置済みオブジェクトの一覧）

    private System.Windows.Forms.FlowLayoutPanel flpTiles = null!;  // タイルパレットのアイコンを流し込むパネル
    private System.Windows.Forms.Button btnBulkFill = null!;        // 数値指定で範囲を一括配置するボタン

    // イベントパレット（敵・ギミック・アイテム）
    private System.Windows.Forms.ListBox lstEnemies = null!, lstGimmicks = null!, lstItems = null!;  // 敵/ギミック/アイテムの選択リスト
    private System.Windows.Forms.RadioButton rbTrigger = null!, rbPlayerStart = null!, rbGoal = null!;  // 汎用トリガー/プレイヤー開始位置/ゴール位置の配置モード選択ラジオボタン

    // イベントリスト (配置済み)
    private System.Windows.Forms.ListBox lstPlacedEvents = null!;  // 現在のステージに配置済みの敵/ギミック/アイテム/トリガー一覧

    // プロパティ
    private System.Windows.Forms.PropertyGrid propertyGrid = null!;  // 選択中オブジェクトのプロパティを編集するグリッド

    // フォーム上の全コントロールを生成し、イベントハンドラの購読・レイアウトの組み立てを行う初期化処理。
    // Visual Studioデザイナが自動生成する典型的な構造だが、本プロジェクトでは手書きで管理している。
    private void InitializeComponent()
    {
        this.SuspendLayout();
        // 共通で使うフォント（通常／太字）をあらかじめ用意しておく。
        var F = new System.Drawing.Font("Meiryo UI", 9);
        var FB = new System.Drawing.Font("Meiryo UI", 9, System.Drawing.FontStyle.Bold);

        // ===== メニューバー =====
        menuStrip1 = new System.Windows.Forms.MenuStrip();
        // 「ファイル」メニュー：保存・終了
        var menuFile = new System.Windows.Forms.ToolStripMenuItem("ファイル(&F)");
        var miSave = new System.Windows.Forms.ToolStripMenuItem("保存(&S)", null, btnSave_Click) { ShortcutKeys = System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.S };
        var miExit = new System.Windows.Forms.ToolStripMenuItem("終了(&X)", null, (_,_) => this.Close());
        menuFile.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] { miSave, miExit });

        // 「編集」メニュー：元に戻す・やり直し
        var menuEdit = new System.Windows.Forms.ToolStripMenuItem("編集(&E)");
        var miUndo = new System.Windows.Forms.ToolStripMenuItem("元に戻す(&U)", null, (_,_) => Undo()) { ShortcutKeys = System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.Z };
        var miRedo = new System.Windows.Forms.ToolStripMenuItem("やり直し(&R)", null, (_,_) => Redo()) { ShortcutKeys = System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.Y };
        menuEdit.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] { miUndo, miRedo });

        // 「アセット管理」メニュー：各種エディタ（アセット/タイル/アニメーション/サウンド/背景）を開く入り口
        var menuData = new System.Windows.Forms.ToolStripMenuItem("アセット管理(&D)");
        var miAsset = new System.Windows.Forms.ToolStripMenuItem("アセット管理", null, btnAssetManager_Click);
        var miTile = new System.Windows.Forms.ToolStripMenuItem("タイルエディタ", null, btnTileEditor_Click);
        var miAnim = new System.Windows.Forms.ToolStripMenuItem("アニメーションエディタ", null, btnAnimEditor_Click);
        var miSound = new System.Windows.Forms.ToolStripMenuItem("サウンド管理", null, btnSoundMgr_Click);
        // Feature: サウンド・アセット管理の刷新 — カタログ登録済みのBGM/SEを敵/ギミック/アイテム/ステージへ
        // 割り当てる専用画面。「サウンド管理」のすぐ下に配置する。
        var miSoundAssign = new System.Windows.Forms.ToolStripMenuItem("サウンド割り当て", null, btnSoundAssign_Click);
        var miBg = new System.Windows.Forms.ToolStripMenuItem("背景設定", null, btnBgSettings_Click);
        menuData.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] { miAsset, miTile, miAnim, miSound, miSoundAssign, miBg });

        // 「プレイ」メニュー：テストプレイ開始（F5ショートカット付き）
        var menuPlay = new System.Windows.Forms.ToolStripMenuItem("プレイ(&P)");
        var miPlay = new System.Windows.Forms.ToolStripMenuItem("テストプレイ", null, btnPlay_Click) { ShortcutKeys = System.Windows.Forms.Keys.F5 };
        menuPlay.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] { miPlay });

        // Feature: UI改善（提案書 MW-2）— 詰まったときに参照できる使い方ガイドをメニューから開けるようにする
        var menuHelp = new System.Windows.Forms.ToolStripMenuItem("ヘルプ(&H)");
        var miHelp = new System.Windows.Forms.ToolStripMenuItem("使い方ガイド", null, (_, _) => new HelpForm().ShowDialog(this));
        menuHelp.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] { miHelp });

        // 組み立てた各メニューをメニューバーへ登録する。
        menuStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[] { menuFile, menuEdit, menuData, menuPlay, menuHelp });

        // ===== ツールバー =====
        toolStrip1 = new System.Windows.Forms.ToolStrip();

        // レイヤー切替ボタン（4種）。CheckOnClickでトグル式の押下状態を持たせる。Layer2（メイン）を既定でチェック済みにする。
        tsbLayer1 = new System.Windows.Forms.ToolStripButton("Layer 1 (遠景)") { CheckOnClick = true, DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text };
        tsbLayer2 = new System.Windows.Forms.ToolStripButton("Layer 2 (メイン)") { CheckOnClick = true, Checked = true, DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text };
        tsbLayer3 = new System.Windows.Forms.ToolStripButton("Layer 3 (近景)") { CheckOnClick = true, DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text };
        tsbLayer4 = new System.Windows.Forms.ToolStripButton("Layer 4 (イベント)") { CheckOnClick = true, DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text };

        // 4つとも同一のクリックハンドラで処理し、押されたボタンに応じて排他的にレイヤーを切り替える。
        tsbLayer1.Click += TsbLayer_Click;
        tsbLayer2.Click += TsbLayer_Click;
        tsbLayer3.Click += TsbLayer_Click;
        tsbLayer4.Click += TsbLayer_Click;

        toolStripSeparator1 = new System.Windows.Forms.ToolStripSeparator();

        // ツール切替ボタン（ペン/消しゴム/選択）。ペンを既定でチェック済みにする。
        tsbPen = new System.Windows.Forms.ToolStripButton("🖊ペン") { CheckOnClick = true, Checked = true, DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text };
        tsbEraser = new System.Windows.Forms.ToolStripButton("⬜消しゴム") { CheckOnClick = true, DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text };
        tsbSelect = new System.Windows.Forms.ToolStripButton("🔍選択") { CheckOnClick = true, DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text };

        tsbPen.Click += TsbTool_Click;
        tsbEraser.Click += TsbTool_Click;
        tsbSelect.Click += TsbTool_Click;

        toolStripSeparator2 = new System.Windows.Forms.ToolStripSeparator();
        // 通常のテストプレイ開始ボタン（プレイヤー開始位置から開始）。
        tsbPlay = new System.Windows.Forms.ToolStripButton("▶ プレイ") { DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text };
        tsbPlay.Click += btnPlay_Click;
        // 「ここから」テストプレイ開始ボタン（マップ上の任意位置をクリックしてそこから開始）。
        tsbTestPlayHere = new System.Windows.Forms.ToolStripButton("📍 ここから") { DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text };
        tsbTestPlayHere.Click += btnTestPlay_Click;

        // ツールバーへ、レイヤー切替→区切り線→ツール切替→区切り線→プレイ系ボタンの順で登録する。
        toolStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            tsbLayer1, tsbLayer2, tsbLayer3, tsbLayer4,
            toolStripSeparator1,
            tsbPen, tsbEraser, tsbSelect,
            toolStripSeparator2,
            tsbPlay, tsbTestPlayHere
        });

        // ===== 左パネル (TabControl) =====
        tabLeft = new System.Windows.Forms.TabControl { Dock = System.Windows.Forms.DockStyle.Fill, Font = F };
        tabStages = new System.Windows.Forms.TabPage("ステージ");
        tabMapProps = new System.Windows.Forms.TabPage("マップ設定");

        // ステージタブ：ステージ一覧・新規作成・削除・CSVインポートのコントロール群を配置する。
        lstStages = new System.Windows.Forms.ListBox { Location = new System.Drawing.Point(5, 5), Size = new System.Drawing.Size(180, 200) };
        lstStages.SelectedIndexChanged += lstStages_SelectedIndexChanged;
        txtNewStage = new System.Windows.Forms.TextBox { Location = new System.Drawing.Point(5, 210), Size = new System.Drawing.Size(180, 23), PlaceholderText = "新ステージ名..." };
        btnCreateStage = new System.Windows.Forms.Button { Text = "＋新規", Location = new System.Drawing.Point(5, 240), Size = new System.Drawing.Size(85, 26) };
        btnCreateStage.Click += btnCreateStage_Click;
        btnDeleteStage = new System.Windows.Forms.Button { Text = "🗑削除", Location = new System.Drawing.Point(100, 240), Size = new System.Drawing.Size(85, 26) };
        btnDeleteStage.Click += btnDeleteStage_Click;
        btnImportCsv = new System.Windows.Forms.Button { Text = "CSVインポート", Location = new System.Drawing.Point(5, 275), Size = new System.Drawing.Size(180, 26) };
        btnImportCsv.Click += btnImportCsv_Click;

        tabStages.Controls.AddRange(new System.Windows.Forms.Control[] { lstStages, txtNewStage, btnCreateStage, btnDeleteStage, btnImportCsv });

        // マップ設定タブ：マップサイズ変更セクション
        var lblSize = new System.Windows.Forms.Label { Text = "📐 マップサイズ", Font = FB, Location = new System.Drawing.Point(5, 5), Size = new System.Drawing.Size(170, 20) };
        var lblW = new System.Windows.Forms.Label { Text = "W:", Location = new System.Drawing.Point(5, 27), Size = new System.Drawing.Size(20, 18) };
        numMapW = new System.Windows.Forms.NumericUpDown { Location = new System.Drawing.Point(27, 25), Size = new System.Drawing.Size(55, 23), Minimum = 10, Maximum = 500, Value = 80 };
        var lblH = new System.Windows.Forms.Label { Text = "H:", Location = new System.Drawing.Point(87, 27), Size = new System.Drawing.Size(20, 18) };
        numMapH = new System.Windows.Forms.NumericUpDown { Location = new System.Drawing.Point(108, 25), Size = new System.Drawing.Size(45, 23), Minimum = 5, Maximum = 100, Value = 15 };
        btnResize = new System.Windows.Forms.Button { Text = "リサイズ", Location = new System.Drawing.Point(5, 50), Size = new System.Drawing.Size(150, 26) };
        btnResize.Click += btnResize_Click;

        // マップ設定タブ：プレイヤー初期設定セクション（開始座標・アクション許可・ジャンプ力/移動速度）
        var lblPlayer = new System.Windows.Forms.Label { Text = "⚙ プレイヤー設定", Font = FB, Location = new System.Drawing.Point(5, 85), Size = new System.Drawing.Size(170, 20) };
        var lblSX = new System.Windows.Forms.Label { Text = "開始X:", Location = new System.Drawing.Point(5, 110), Size = new System.Drawing.Size(42, 18) };
        numStartX = new System.Windows.Forms.NumericUpDown { Location = new System.Drawing.Point(48, 108), Size = new System.Drawing.Size(65, 23), Maximum = 9999, Value = 48 };
        numStartX.ValueChanged += PlayerSetting_Changed;
        var lblSY = new System.Windows.Forms.Label { Text = "Y:", Location = new System.Drawing.Point(118, 110), Size = new System.Drawing.Size(18, 18) };
        numStartY = new System.Windows.Forms.NumericUpDown { Location = new System.Drawing.Point(135, 108), Size = new System.Drawing.Size(55, 23), Maximum = 9999, Value = 320 };
        numStartY.ValueChanged += PlayerSetting_Changed;

        // プレイヤーが最初から使えるアクション（2段ジャンプ/ダッシュ/火の玉/飛行）の許可チェックボックス。
        chkDoubleJump = new System.Windows.Forms.CheckBox { Text = "2段ジャンプ", Location = new System.Drawing.Point(5, 140), Size = new System.Drawing.Size(168, 20) };
        chkDoubleJump.CheckedChanged += PlayerSetting_Changed;
        chkDash = new System.Windows.Forms.CheckBox { Text = "ダッシュ", Location = new System.Drawing.Point(5, 165), Size = new System.Drawing.Size(168, 20) };
        chkDash.CheckedChanged += PlayerSetting_Changed;
        chkFireball = new System.Windows.Forms.CheckBox { Text = "火の玉", Location = new System.Drawing.Point(5, 190), Size = new System.Drawing.Size(168, 20) };
        chkFireball.CheckedChanged += PlayerSetting_Changed;
        chkFly = new System.Windows.Forms.CheckBox { Text = "飛行", Location = new System.Drawing.Point(5, 215), Size = new System.Drawing.Size(168, 20) };
        chkFly.CheckedChanged += PlayerSetting_Changed;

        // ジャンプ力・移動速度の数値パラメータ。
        var lblJP = new System.Windows.Forms.Label { Text = "ジャンプ力:", Location = new System.Drawing.Point(5, 245), Size = new System.Drawing.Size(75, 18) };
        numJumpPower = new System.Windows.Forms.NumericUpDown { Location = new System.Drawing.Point(85, 243), Size = new System.Drawing.Size(50, 23), Minimum = -30, Maximum = 0, Value = -12 };
        numJumpPower.ValueChanged += PlayerSetting_Changed;
        var lblSp = new System.Windows.Forms.Label { Text = "移動速度:", Location = new System.Drawing.Point(5, 275), Size = new System.Drawing.Size(75, 18) };
        numSpeed = new System.Windows.Forms.NumericUpDown { Location = new System.Drawing.Point(85, 273), Size = new System.Drawing.Size(50, 23), DecimalPlaces = 1, Increment = 0.5m, Maximum = 20, Value = 4 };
        numSpeed.ValueChanged += PlayerSetting_Changed;

        // Feature: 編集コストゲージ — ステージ単位の許可設定・コスト経済設定
        // マップ設定タブ内のコンテンツが縦に長くなるため、タブ自体をスクロール可能にする。
        tabMapProps.AutoScroll = true;

        // 編集ツール許可設定セクション：ゲーム中にどの編集ツールを使用可能にするかのチェックボックス群（すべて既定で許可）。
        var lblEditTools = new System.Windows.Forms.Label { Text = "🎬 編集ツール設定（許可）", Font = FB, Location = new System.Drawing.Point(5, 310), Size = new System.Drawing.Size(200, 20) };
        chkEditRewind = new System.Windows.Forms.CheckBox { Text = "巻き戻し", Location = new System.Drawing.Point(5, 332), Size = new System.Drawing.Size(180, 20), Checked = true };
        chkEditRewind.CheckedChanged += PlayerSetting_Changed;
        chkEditPause = new System.Windows.Forms.CheckBox { Text = "一時停止", Location = new System.Drawing.Point(5, 354), Size = new System.Drawing.Size(180, 20), Checked = true };
        chkEditPause.CheckedChanged += PlayerSetting_Changed;
        chkEditFastForward = new System.Windows.Forms.CheckBox { Text = "早送り", Location = new System.Drawing.Point(5, 376), Size = new System.Drawing.Size(180, 20), Checked = true };
        chkEditFastForward.CheckedChanged += PlayerSetting_Changed;
        chkEditScreenFx = new System.Windows.Forms.CheckBox { Text = "画面エフェクト(ズーム/明暗/色)", Location = new System.Drawing.Point(5, 398), Size = new System.Drawing.Size(200, 20), Checked = true };
        chkEditScreenFx.CheckedChanged += PlayerSetting_Changed;
        chkEditObjectEdit = new System.Windows.Forms.CheckBox { Text = "個別オブジェクト編集", Location = new System.Drawing.Point(5, 420), Size = new System.Drawing.Size(180, 20), Checked = true };
        chkEditObjectEdit.CheckedChanged += PlayerSetting_Changed;
        // タイムラインカット（区間を丸ごと飛ばす編集）の許可。個別オブジェクト編集とは独立して切り替えられる。
        chkEditCut = new System.Windows.Forms.CheckBox { Text = "カット(タイムライン)", Location = new System.Drawing.Point(5, 442), Size = new System.Drawing.Size(180, 20), Checked = true };
        chkEditCut.CheckedChanged += PlayerSetting_Changed;

        // 編集コスト設定セクション：編集ツール使用に伴うコストゲージの経済設定（最大値・自然回復・各種消費量）。
        var lblEditCost = new System.Windows.Forms.Label { Text = "💰 編集コスト設定", Font = FB, Location = new System.Drawing.Point(5, 474), Size = new System.Drawing.Size(200, 20) };

        // コストゲージの最大値。
        var lblEcMax = new System.Windows.Forms.Label { Text = "最大値:", Location = new System.Drawing.Point(5, 498), Size = new System.Drawing.Size(115, 18) };
        numEditMaxCost = new System.Windows.Forms.NumericUpDown { Location = new System.Drawing.Point(120, 496), Size = new System.Drawing.Size(60, 23), DecimalPlaces = 1, Increment = 1m, Maximum = 999, Value = 100 };
        numEditMaxCost.ValueChanged += PlayerSetting_Changed;

        // 何もしていない時間経過で自然回復する量（1秒あたり）。
        var lblEcRegen = new System.Windows.Forms.Label { Text = "自然回復/秒:", Location = new System.Drawing.Point(5, 522), Size = new System.Drawing.Size(115, 18) };
        numEditRegen = new System.Windows.Forms.NumericUpDown { Location = new System.Drawing.Point(120, 520), Size = new System.Drawing.Size(60, 23), DecimalPlaces = 1, Increment = 0.5m, Maximum = 999, Value = 6 };
        numEditRegen.ValueChanged += PlayerSetting_Changed;

        // 継続使用系ツール（巻き戻し/一時停止/早送り/画面エフェクト）の秒あたり消費量。
        var lblEcDrainRewind = new System.Windows.Forms.Label { Text = "巻き戻し消費/秒:", Location = new System.Drawing.Point(5, 546), Size = new System.Drawing.Size(115, 18) };
        numEditDrainRewind = new System.Windows.Forms.NumericUpDown { Location = new System.Drawing.Point(120, 544), Size = new System.Drawing.Size(60, 23), DecimalPlaces = 1, Increment = 0.5m, Maximum = 999, Value = 18 };
        numEditDrainRewind.ValueChanged += PlayerSetting_Changed;

        var lblEcDrainPause = new System.Windows.Forms.Label { Text = "一時停止消費/秒:", Location = new System.Drawing.Point(5, 570), Size = new System.Drawing.Size(115, 18) };
        numEditDrainPause = new System.Windows.Forms.NumericUpDown { Location = new System.Drawing.Point(120, 568), Size = new System.Drawing.Size(60, 23), DecimalPlaces = 1, Increment = 0.5m, Maximum = 999, Value = 4 };
        numEditDrainPause.ValueChanged += PlayerSetting_Changed;

        var lblEcDrainFF = new System.Windows.Forms.Label { Text = "早送り消費/秒:", Location = new System.Drawing.Point(5, 594), Size = new System.Drawing.Size(115, 18) };
        numEditDrainFF = new System.Windows.Forms.NumericUpDown { Location = new System.Drawing.Point(120, 592), Size = new System.Drawing.Size(60, 23), DecimalPlaces = 1, Increment = 0.5m, Maximum = 999, Value = 10 };
        numEditDrainFF.ValueChanged += PlayerSetting_Changed;

        var lblEcDrainScreenFx = new System.Windows.Forms.Label { Text = "画面エフェクト消費/秒:", Location = new System.Drawing.Point(5, 618), Size = new System.Drawing.Size(115, 18) };
        numEditDrainScreenFx = new System.Windows.Forms.NumericUpDown { Location = new System.Drawing.Point(120, 616), Size = new System.Drawing.Size(60, 23), DecimalPlaces = 1, Increment = 0.5m, Maximum = 999, Value = 8 };
        numEditDrainScreenFx.ValueChanged += PlayerSetting_Changed;

        // 単発アクション（ボタンを押した瞬間に一度だけ消費する系）の固定消費量。
        var lblEcFlatColorCycle = new System.Windows.Forms.Label { Text = "色フィルタ切替:", Location = new System.Drawing.Point(5, 642), Size = new System.Drawing.Size(115, 18) };
        numEditFlatColorCycle = new System.Windows.Forms.NumericUpDown { Location = new System.Drawing.Point(120, 640), Size = new System.Drawing.Size(60, 23), DecimalPlaces = 1, Increment = 0.5m, Maximum = 999, Value = 5 };
        numEditFlatColorCycle.ValueChanged += PlayerSetting_Changed;

        var lblEcFlatMenuToggle = new System.Windows.Forms.Label { Text = "メニュートグル:", Location = new System.Drawing.Point(5, 666), Size = new System.Drawing.Size(115, 18) };
        numEditFlatMenuToggle = new System.Windows.Forms.NumericUpDown { Location = new System.Drawing.Point(120, 664), Size = new System.Drawing.Size(60, 23), DecimalPlaces = 1, Increment = 0.5m, Maximum = 999, Value = 8 };
        numEditFlatMenuToggle.ValueChanged += PlayerSetting_Changed;

        var lblEcFlatSpeedChange = new System.Windows.Forms.Label { Text = "速度変更:", Location = new System.Drawing.Point(5, 690), Size = new System.Drawing.Size(115, 18) };
        numEditFlatSpeedChange = new System.Windows.Forms.NumericUpDown { Location = new System.Drawing.Point(120, 688), Size = new System.Drawing.Size(60, 23), DecimalPlaces = 1, Increment = 0.5m, Maximum = 999, Value = 6 };
        numEditFlatSpeedChange.ValueChanged += PlayerSetting_Changed;

        var lblEcFlatDirectionFlip = new System.Windows.Forms.Label { Text = "向き反転:", Location = new System.Drawing.Point(5, 714), Size = new System.Drawing.Size(115, 18) };
        numEditFlatDirectionFlip = new System.Windows.Forms.NumericUpDown { Location = new System.Drawing.Point(120, 712), Size = new System.Drawing.Size(60, 23), DecimalPlaces = 1, Increment = 0.5m, Maximum = 999, Value = 4 };
        numEditFlatDirectionFlip.ValueChanged += PlayerSetting_Changed;

        var lblEcFlatResetAll = new System.Windows.Forms.Label { Text = "すべてリセット:", Location = new System.Drawing.Point(5, 738), Size = new System.Drawing.Size(115, 18) };
        numEditFlatResetAll = new System.Windows.Forms.NumericUpDown { Location = new System.Drawing.Point(120, 736), Size = new System.Drawing.Size(60, 23), DecimalPlaces = 1, Increment = 0.5m, Maximum = 999, Value = 10 };
        numEditFlatResetAll.ValueChanged += PlayerSetting_Changed;

        // タイムラインカットを1本作るたびの固定消費量。区間を丸ごと飛ばせる最も強力な操作なので既定値は高め。
        var lblEcFlatCutCreate = new System.Windows.Forms.Label { Text = "カット作成:", Location = new System.Drawing.Point(5, 762), Size = new System.Drawing.Size(115, 18) };
        numEditFlatCutCreate = new System.Windows.Forms.NumericUpDown { Location = new System.Drawing.Point(120, 760), Size = new System.Drawing.Size(60, 23), DecimalPlaces = 1, Increment = 0.5m, Maximum = 999, Value = 20 };
        numEditFlatCutCreate.ValueChanged += PlayerSetting_Changed;

        // マップ設定タブへ、上記で組み立てた全コントロールをまとめて登録する。
        tabMapProps.Controls.AddRange(new System.Windows.Forms.Control[] {
            lblSize, lblW, numMapW, lblH, numMapH, btnResize,
            lblPlayer, lblSX, numStartX, lblSY, numStartY,
            chkDoubleJump, chkDash, chkFireball, chkFly,
            lblJP, numJumpPower, lblSp, numSpeed,
            lblEditTools, chkEditRewind, chkEditPause, chkEditFastForward, chkEditScreenFx, chkEditObjectEdit, chkEditCut,
            lblEditCost,
            lblEcMax, numEditMaxCost, lblEcRegen, numEditRegen,
            lblEcDrainRewind, numEditDrainRewind, lblEcDrainPause, numEditDrainPause,
            lblEcDrainFF, numEditDrainFF, lblEcDrainScreenFx, numEditDrainScreenFx,
            lblEcFlatColorCycle, numEditFlatColorCycle, lblEcFlatMenuToggle, numEditFlatMenuToggle,
            lblEcFlatSpeedChange, numEditFlatSpeedChange, lblEcFlatDirectionFlip, numEditFlatDirectionFlip,
            lblEcFlatResetAll, numEditFlatResetAll,
            lblEcFlatCutCreate, numEditFlatCutCreate
        });

        tabLeft.Controls.Add(tabStages);
        tabLeft.Controls.Add(tabMapProps);

        // ===== 右パネル (TabControl) =====
        tabRight = new System.Windows.Forms.TabControl { Dock = System.Windows.Forms.DockStyle.Fill, Font = F };
        tabTilePalette = new System.Windows.Forms.TabPage("タイル");
        tabEventPalette = new System.Windows.Forms.TabPage("配置対象");
        tabEventList = new System.Windows.Forms.TabPage("イベントリスト");

        // タイルパレットタブ：タイルアイコンをFlowLayoutPanelで並べ、下部に一括配置ボタンを配置する。
        flpTiles = new System.Windows.Forms.FlowLayoutPanel { Dock = System.Windows.Forms.DockStyle.Fill, FlowDirection = System.Windows.Forms.FlowDirection.LeftToRight, AutoScroll = true };
        // Feature: UI改善（友人フィードバック対応）— 数値指定で行・列の範囲を一括配置するボタン。
        // Dock=Fillのflptilesを先にAddし、Dock=Bottomのパネルを後からAddする安全な順序を守る。
        var pnlBulkFill = new System.Windows.Forms.Panel { Dock = System.Windows.Forms.DockStyle.Bottom, Height = 34 };
        btnBulkFill = new System.Windows.Forms.Button { Text = "🔢 数値指定で一括配置", Dock = System.Windows.Forms.DockStyle.Fill, FlatStyle = System.Windows.Forms.FlatStyle.Flat };
        btnBulkFill.Click += BtnBulkFill_Click;
        pnlBulkFill.Controls.Add(btnBulkFill);
        tabTilePalette.Controls.Add(flpTiles);
        tabTilePalette.Controls.Add(pnlBulkFill);

        // イベント配置パレット：敵/ギミック/アイテムの選択リストと、汎用トリガー/開始位置/ゴールの配置モード選択。
        var lblEn = new System.Windows.Forms.Label { Text = "👾 敵", Font = FB, Location = new System.Drawing.Point(5, 5), Size = new System.Drawing.Size(185, 18) };
        lstEnemies = new System.Windows.Forms.ListBox { Location = new System.Drawing.Point(5, 25), Size = new System.Drawing.Size(200, 70) };
        lstEnemies.SelectedIndexChanged += lstEnemies_SelectedIndexChanged;

        var lblGi = new System.Windows.Forms.Label { Text = "🔧 ギミック", Font = FB, Location = new System.Drawing.Point(5, 100), Size = new System.Drawing.Size(185, 18) };
        lstGimmicks = new System.Windows.Forms.ListBox { Location = new System.Drawing.Point(5, 120), Size = new System.Drawing.Size(200, 70) };
        lstGimmicks.SelectedIndexChanged += lstGimmicks_SelectedIndexChanged;

        var lblIt = new System.Windows.Forms.Label { Text = "💎 アイテム", Font = FB, Location = new System.Drawing.Point(5, 195), Size = new System.Drawing.Size(185, 18) };
        lstItems = new System.Windows.Forms.ListBox { Location = new System.Drawing.Point(5, 215), Size = new System.Drawing.Size(200, 60) };
        lstItems.SelectedIndexChanged += lstItems_SelectedIndexChanged;

        // 汎用トリガー配置モードを既定選択にしておく。
        rbTrigger = new System.Windows.Forms.RadioButton { Text = "⚡汎用トリガー", Location = new System.Drawing.Point(5, 285), Size = new System.Drawing.Size(120, 20), Checked = true };
        rbPlayerStart = new System.Windows.Forms.RadioButton { Text = "🚶プレイヤー開始位置", Location = new System.Drawing.Point(5, 310), Size = new System.Drawing.Size(200, 20) };
        rbGoal = new System.Windows.Forms.RadioButton { Text = "🏁ゴール位置", Location = new System.Drawing.Point(5, 335), Size = new System.Drawing.Size(200, 20) };
        rbTrigger.CheckedChanged += EventModeItem_CheckedChanged;
        rbPlayerStart.CheckedChanged += EventModeItem_CheckedChanged;
        rbGoal.CheckedChanged += EventModeItem_CheckedChanged;

        tabEventPalette.Controls.AddRange(new System.Windows.Forms.Control[] { lblEn, lstEnemies, lblGi, lstGimmicks, lblIt, lstItems, rbTrigger, rbPlayerStart, rbGoal });

        // イベントリスト (配置済み)：現在のステージに配置されているオブジェクトの一覧をリスト表示する。
        lstPlacedEvents = new System.Windows.Forms.ListBox { Dock = System.Windows.Forms.DockStyle.Fill };
        lstPlacedEvents.SelectedIndexChanged += LstPlacedEvents_SelectedIndexChanged;
        lstPlacedEvents.DoubleClick += LstPlacedEvents_DoubleClick;
        tabEventList.Controls.Add(lstPlacedEvents);

        tabRight.Controls.Add(tabTilePalette);
        tabRight.Controls.Add(tabEventPalette);
        tabRight.Controls.Add(tabEventList);

        // ===== 共通プロパティグリッド =====
        // 選択中オブジェクトのプロパティを編集するグリッド。ヘルプ欄・ツールバーは非表示にしてコンパクトに使う。
        propertyGrid = new System.Windows.Forms.PropertyGrid { Dock = System.Windows.Forms.DockStyle.Fill, HelpVisible = false, ToolbarVisible = false, Font = F };

        // ===== 情報ラベル =====
        // 編集中のステージ名を表示するラベル（濃い青字）。
        lblCurrentStage = new System.Windows.Forms.Label { Font = new System.Drawing.Font("Meiryo UI", 9, System.Drawing.FontStyle.Bold), Text = "編集中: ---", Location = new System.Drawing.Point(6, 6), Size = new System.Drawing.Size(300, 16), ForeColor = System.Drawing.Color.DarkBlue };
        // 現在アクティブなレイヤーの情報を表示するラベル（緑字）。
        lblLayerInfo = new System.Windows.Forms.Label { Font = new System.Drawing.Font("Meiryo UI", 9), Text = "", Location = new System.Drawing.Point(316, 6), Size = new System.Drawing.Size(300, 16), ForeColor = System.Drawing.Color.DarkGreen };
        // 画面最下部に表示する汎用ステータスメッセージ用ラベル。
        lblStatus = new System.Windows.Forms.Label { Dock = System.Windows.Forms.DockStyle.Fill, TextAlign = System.Drawing.ContentAlignment.MiddleLeft, Padding = new System.Windows.Forms.Padding(6, 0, 0, 0) };

        // Feature: UI改善（構造改修フェーズ2）— ステージ名/レイヤー情報を表示する帯をキャンバス上部に固定表示する。
        pnlInfoBar = new System.Windows.Forms.Panel { Dock = System.Windows.Forms.DockStyle.Top, Height = 26 };
        pnlInfoBar.Controls.Add(lblCurrentStage);
        pnlInfoBar.Controls.Add(lblLayerInfo);

        // 画面下端の全幅ステータスバー（以前は地図領域の下、幅300pxのみだった）
        pnlStatusBar = new System.Windows.Forms.Panel { Dock = System.Windows.Forms.DockStyle.Bottom, Height = 24, BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle };
        pnlStatusBar.Controls.Add(lblStatus);

        // ===== マップキャンバス =====
        // メインの編集キャンバス。MapCanvas側で発火する各種イベントをここで購読する。
        mapCanvas = new MapCanvas { Dock = System.Windows.Forms.DockStyle.Fill, BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle };
        mapCanvas.ObjectSelected += mapCanvas_ObjectSelected;
        mapCanvas.StageModified += mapCanvas_StageModified;
        mapCanvas.EditCompleted += mapCanvas_EditCompleted;
        mapCanvas.TestPlayClicked += mapCanvas_TestPlayClicked;
        mapCanvas.TriggerPlaced += mapCanvas_TriggerPlaced;

        // マップキャンバス用の水平・垂直スクロールバー。範囲・大きな移動量(LargeChange)を初期設定する。
        hScrollMap = new System.Windows.Forms.HScrollBar { Dock = System.Windows.Forms.DockStyle.Fill, Minimum = 0, Maximum = 1800, LargeChange = 200 };
        hScrollMap.Scroll += hScrollMap_Scroll;
        vScrollMap = new System.Windows.Forms.VScrollBar { Dock = System.Windows.Forms.DockStyle.Fill, Minimum = 0, Maximum = 500, LargeChange = 100 };
        vScrollMap.Scroll += vScrollMap_Scroll;

        // キャンバス+スクロールバーを2x2グリッドで配置（右列/下段はスクロールバー分の固定幅）
        tlpCanvas = new System.Windows.Forms.TableLayoutPanel { Dock = System.Windows.Forms.DockStyle.Fill, ColumnCount = 2, RowCount = 2 };
        tlpCanvas.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100f));
        tlpCanvas.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 18f));
        tlpCanvas.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100f));
        tlpCanvas.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 18f));
        tlpCanvas.Controls.Add(mapCanvas, 0, 0);
        tlpCanvas.Controls.Add(vScrollMap, 1, 0);
        tlpCanvas.Controls.Add(hScrollMap, 0, 1);

        // 中央エリア = 情報バー(上) + キャンバスグリッド(残り全部)
        var pnlCenterArea = new System.Windows.Forms.Panel { Dock = System.Windows.Forms.DockStyle.Fill };
        pnlCenterArea.Controls.Add(tlpCanvas);
        pnlCenterArea.Controls.Add(pnlInfoBar);

        // 右側: タイル/配置タブ(上) | プロパティグリッド(下)
        splitRightVertical = new System.Windows.Forms.SplitContainer { Dock = System.Windows.Forms.DockStyle.Fill, Orientation = System.Windows.Forms.Orientation.Horizontal, SplitterWidth = 6 };
        splitRightVertical.Panel1.Controls.Add(tabRight);
        splitRightVertical.Panel2.Controls.Add(propertyGrid);

        // 中央: キャンバスエリア | 右パネル
        splitCenterRight = new System.Windows.Forms.SplitContainer { Dock = System.Windows.Forms.DockStyle.Fill, Orientation = System.Windows.Forms.Orientation.Vertical, SplitterWidth = 6 };
        splitCenterRight.Panel1.Controls.Add(pnlCenterArea);
        splitCenterRight.Panel2.Controls.Add(splitRightVertical);

        // 最外: 左パネル | それ以外
        splitOuter = new System.Windows.Forms.SplitContainer { Dock = System.Windows.Forms.DockStyle.Fill, Orientation = System.Windows.Forms.Orientation.Vertical, SplitterWidth = 6 };
        splitOuter.Panel1.Controls.Add(tabLeft);
        splitOuter.Panel2.Controls.Add(splitCenterRight);

        // SplitterDistanceはコントロールがDockされ実サイズが確定してから設定する
        // （PartsEditorForm等と同じ理由：先に設定すると無視/例外になることがある）
        this.Shown += (s, e) =>
        {
            // フォーム表示後、各SplitContainerの実際の幅・高さを基準に分割位置を計算して設定する。
            // 最小値（Math.Max側）を設けることで、極端に小さいウィンドウでもパネルが潰れきらないようにしている。
            splitOuter.SplitterDistance = System.Math.Max(160, (int)(splitOuter.Width * 0.18));
            splitCenterRight.SplitterDistance = System.Math.Max(400, splitCenterRight.Width - 230);
            splitRightVertical.SplitterDistance = System.Math.Max(280, splitRightVertical.Height - 210);
        };

        // Form 設定：初期クライアントサイズ・最小サイズ・コントロールの追加順（Dockの重なり順に影響する）を設定する。
        this.ClientSize = new System.Drawing.Size(1120, 690);
        this.MinimumSize = new System.Drawing.Size(900, 640);
        this.Controls.Add(splitOuter);
        this.Controls.Add(pnlStatusBar);
        this.Controls.Add(toolStrip1);
        this.Controls.Add(menuStrip1);
        // メニューストリップをフォームのメインメニューとして関連付ける（Altキー操作等に必要）。
        this.MainMenuStrip = menuStrip1;
        this.Name = "Form1";
        this.Text = "Lab Editor";
        this.Load += new System.EventHandler(this.Form1_Load);

        this.ResumeLayout(false);
        this.PerformLayout();
    }
}
