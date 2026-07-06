namespace Lab_Editor;

partial class Form1
{
    private System.ComponentModel.IContainer components = null;
    protected override void Dispose(bool disposing) { if (disposing && components != null) components.Dispose(); base.Dispose(disposing); }

    // ===== コントロール宣言 =====
    private MapCanvas mapCanvas = null!;
    private HScrollBar hScrollMap = null!;
    private VScrollBar vScrollMap = null!;

    // 左パネル
    private ListBox lstStages = null!;
    private TextBox txtNewStage = null!;
    private Button btnCreateStage = null!, btnDeleteStage = null!;
    private NumericUpDown numStartX = null!, numStartY = null!;
    private CheckBox chkDoubleJump = null!, chkDash = null!, chkFireball = null!, chkFly = null!;
    private NumericUpDown numJumpPower = null!, numSpeed = null!;
    private NumericUpDown numMapW = null!, numMapH = null!;
    private Button btnResize = null!;

    // ツールバー
    private RadioButton rbTileMode = null!, rbErase = null!, rbSelect = null!, rbPlayerStart = null!, rbGoal = null!;
    private RadioButton rbDecoBack = null!, rbDecoFront = null!, rbTrigger = null!; // Feature 1,5
    private Label lblCurrentStage = null!, lblStatus = null!, lblLayerInfo = null!;

    // タイルパレット
    private FlowLayoutPanel flpTiles = null!;

    // 右パネル（敵・ギミック・アイテム）
    private ListBox lstEnemies = null!, lstGimmicks = null!, lstItems = null!;
    private PropertyGrid propertyGrid = null!;

    // ボタン
    private Button btnPlay = null!, btnSave = null!, btnAssetManager = null!;
    private Button btnTileEditor = null!, btnImportCsv = null!;
    private Button btnTestPlay = null!;     // Feature 4
    private Button btnBgSettings = null!;   // Feature 1
    private Button btnSoundMgr = null!;     // Feature 3
    private Button btnAnimEditor = null!;   // Feature 2

    private void InitializeComponent()
    {
        SuspendLayout();
        var F = new System.Drawing.Font("Meiryo UI", 9);
        var FB = new System.Drawing.Font("Meiryo UI", 9, System.Drawing.FontStyle.Bold);

        // ===== 左パネル (0,0) 180×680 =====
        var panelLeft = new Panel { Location = new System.Drawing.Point(0, 0), Size = new System.Drawing.Size(180, 680), BorderStyle = BorderStyle.FixedSingle };

        var lblStages = new Label { Text = "📁 ステージ一覧", Font = FB, Location = new System.Drawing.Point(5, 5), Size = new System.Drawing.Size(170, 20) };
        lstStages = new ListBox { Font = F, Location = new System.Drawing.Point(5, 28), Size = new System.Drawing.Size(165, 160) };
        lstStages.SelectedIndexChanged += lstStages_SelectedIndexChanged;

        txtNewStage = new TextBox { Font = F, Location = new System.Drawing.Point(5, 193), Size = new System.Drawing.Size(165, 23), PlaceholderText = "新ステージ名..." };
        btnCreateStage = new Button { Font = F, Text = "＋新規", Location = new System.Drawing.Point(5, 220), Size = new System.Drawing.Size(78, 26) };
        btnCreateStage.Click += btnCreateStage_Click;
        btnDeleteStage = new Button { Font = F, Text = "🗑削除", Location = new System.Drawing.Point(90, 220), Size = new System.Drawing.Size(78, 26) };
        btnDeleteStage.Click += btnDeleteStage_Click;

        // マップサイズ
        var lblSize = new Label { Text = "📐 マップサイズ", Font = FB, Location = new System.Drawing.Point(5, 255), Size = new System.Drawing.Size(170, 20) };
        var lblW = new Label { Font = F, Text = "W:", Location = new System.Drawing.Point(5, 278), Size = new System.Drawing.Size(20, 18) };
        numMapW = new NumericUpDown { Font = F, Location = new System.Drawing.Point(27, 276), Size = new System.Drawing.Size(55, 23), Minimum = 10, Maximum = 500, Value = 80 };
        var lblH = new Label { Font = F, Text = "H:", Location = new System.Drawing.Point(87, 278), Size = new System.Drawing.Size(20, 18) };
        numMapH = new NumericUpDown { Font = F, Location = new System.Drawing.Point(108, 276), Size = new System.Drawing.Size(45, 23), Minimum = 5, Maximum = 100, Value = 15 };
        btnResize = new Button { Font = F, Text = "リサイズ", Location = new System.Drawing.Point(5, 303), Size = new System.Drawing.Size(165, 26) };
        btnResize.Click += btnResize_Click;

        // プレイヤー設定
        var lblPlayer = new Label { Text = "⚙ プレイヤー設定", Font = FB, Location = new System.Drawing.Point(5, 340), Size = new System.Drawing.Size(170, 20) };
        var lblSX = new Label { Font = F, Text = "開始X:", Location = new System.Drawing.Point(5, 364), Size = new System.Drawing.Size(42, 18) };
        numStartX = new NumericUpDown { Font = F, Location = new System.Drawing.Point(48, 362), Size = new System.Drawing.Size(65, 23), Maximum = 9999, Value = 48 };
        numStartX.ValueChanged += PlayerSetting_Changed;
        var lblSY = new Label { Font = F, Text = "Y:", Location = new System.Drawing.Point(118, 364), Size = new System.Drawing.Size(18, 18) };
        numStartY = new NumericUpDown { Font = F, Location = new System.Drawing.Point(132, 362), Size = new System.Drawing.Size(38, 23), Maximum = 9999, Value = 320 };
        numStartY.ValueChanged += PlayerSetting_Changed;

        chkDoubleJump = new CheckBox { Font = F, Text = "2段ジャンプ", Location = new System.Drawing.Point(5, 390), Size = new System.Drawing.Size(168, 20) };
        chkDoubleJump.CheckedChanged += PlayerSetting_Changed;
        chkDash = new CheckBox { Font = F, Text = "ダッシュ", Location = new System.Drawing.Point(5, 412), Size = new System.Drawing.Size(168, 20) };
        chkDash.CheckedChanged += PlayerSetting_Changed;
        chkFireball = new CheckBox { Font = F, Text = "火の玉", Location = new System.Drawing.Point(5, 434), Size = new System.Drawing.Size(168, 20) };
        chkFireball.CheckedChanged += PlayerSetting_Changed;
        chkFly = new CheckBox { Font = F, Text = "飛行", Location = new System.Drawing.Point(5, 456), Size = new System.Drawing.Size(168, 20) };
        chkFly.CheckedChanged += PlayerSetting_Changed;

        var lblJP = new Label { Font = F, Text = "ジャンプ力:", Location = new System.Drawing.Point(5, 482), Size = new System.Drawing.Size(68, 18) };
        numJumpPower = new NumericUpDown { Font = F, Location = new System.Drawing.Point(78, 480), Size = new System.Drawing.Size(50, 23), Minimum = -30, Maximum = 0, Value = -12 };
        numJumpPower.ValueChanged += PlayerSetting_Changed;
        var lblSp = new Label { Font = F, Text = "移動速度:", Location = new System.Drawing.Point(5, 508), Size = new System.Drawing.Size(68, 18) };
        numSpeed = new NumericUpDown { Font = F, Location = new System.Drawing.Point(78, 506), Size = new System.Drawing.Size(50, 23), DecimalPlaces = 1, Increment = 0.5m, Maximum = 20, Value = 4 };
        numSpeed.ValueChanged += PlayerSetting_Changed;

        panelLeft.Controls.AddRange(new Control[] {
            lblStages, lstStages, txtNewStage, btnCreateStage, btnDeleteStage,
            lblSize, lblW, numMapW, lblH, numMapH, btnResize,
            lblPlayer, lblSX, numStartX, lblSY, numStartY,
            chkDoubleJump, chkDash, chkFireball, chkFly,
            lblJP, numJumpPower, lblSp, numSpeed
        });

        // ===== ツールバー (180,0) 700×52 (2行に拡張) =====
        var panelTools = new Panel { Location = new System.Drawing.Point(180, 0), Size = new System.Drawing.Size(700, 50), BackColor = System.Drawing.Color.FromArgb(240, 240, 245), BorderStyle = BorderStyle.FixedSingle };

        // 1行目: 基本編集ツール
        rbTileMode = new RadioButton { Font = F, Text = "🖊メイン", Location = new System.Drawing.Point(5, 4), Size = new System.Drawing.Size(72, 20), Checked = true };
        rbTileMode.CheckedChanged += rbTool_CheckedChanged;
        rbErase = new RadioButton { Font = F, Text = "⬜消去", Location = new System.Drawing.Point(80, 4), Size = new System.Drawing.Size(65, 20) };
        rbErase.CheckedChanged += rbTool_CheckedChanged;
        rbDecoBack = new RadioButton { Font = F, Text = "🌿後景Deco", Location = new System.Drawing.Point(148, 4), Size = new System.Drawing.Size(88, 20) };
        rbDecoBack.CheckedChanged += rbTool_CheckedChanged;
        rbDecoFront = new RadioButton { Font = F, Text = "🌸前景Deco", Location = new System.Drawing.Point(239, 4), Size = new System.Drawing.Size(88, 20) };
        rbDecoFront.CheckedChanged += rbTool_CheckedChanged;
        rbSelect = new RadioButton { Font = F, Text = "🔍選択", Location = new System.Drawing.Point(330, 4), Size = new System.Drawing.Size(65, 20) };
        rbSelect.CheckedChanged += rbTool_CheckedChanged;
        rbPlayerStart = new RadioButton { Font = F, Text = "🚶開始", Location = new System.Drawing.Point(398, 4), Size = new System.Drawing.Size(65, 20) };
        rbPlayerStart.CheckedChanged += rbTool_CheckedChanged;
        rbGoal = new RadioButton { Font = F, Text = "🏁ゴール", Location = new System.Drawing.Point(466, 4), Size = new System.Drawing.Size(72, 20) };
        rbGoal.CheckedChanged += rbTool_CheckedChanged;
        rbTrigger = new RadioButton { Font = F, Text = "⚡トリガー", Location = new System.Drawing.Point(541, 4), Size = new System.Drawing.Size(80, 20) };
        rbTrigger.CheckedChanged += rbTool_CheckedChanged;

        // 2行目: ラベル
        lblCurrentStage = new Label { Font = new System.Drawing.Font("Meiryo UI", 7, System.Drawing.FontStyle.Bold), Text = "編集中: ---", Location = new System.Drawing.Point(5, 30), Size = new System.Drawing.Size(480, 16), ForeColor = System.Drawing.Color.DarkBlue };
        lblLayerInfo = new Label { Font = new System.Drawing.Font("Meiryo UI", 7), Text = "", Location = new System.Drawing.Point(490, 30), Size = new System.Drawing.Size(200, 16), ForeColor = System.Drawing.Color.DarkGreen };

        panelTools.Controls.AddRange(new Control[] {
            rbTileMode, rbErase, rbDecoBack, rbDecoFront, rbSelect, rbPlayerStart, rbGoal, rbTrigger,
            lblCurrentStage, lblLayerInfo
        });

        // ===== タイルパレット (180,50) 700×60 =====
        var panelTiles = new Panel { Location = new System.Drawing.Point(180, 50), Size = new System.Drawing.Size(700, 55), BorderStyle = BorderStyle.FixedSingle, BackColor = System.Drawing.Color.FromArgb(250, 250, 250) };
        var lblTilePal = new Label { Text = "🧱タイル:", Font = FB, Location = new System.Drawing.Point(4, 4), Size = new System.Drawing.Size(55, 18) };
        flpTiles = new FlowLayoutPanel { Location = new System.Drawing.Point(60, 2), Size = new System.Drawing.Size(635, 50), FlowDirection = FlowDirection.LeftToRight, AutoScroll = true };
        panelTiles.Controls.AddRange(new Control[] { lblTilePal, flpTiles });

        // ===== マップキャンバス (180,105) 682×475 =====
        mapCanvas = new MapCanvas { Location = new System.Drawing.Point(180, 105), Size = new System.Drawing.Size(682, 475), BorderStyle = BorderStyle.FixedSingle };
        mapCanvas.ObjectSelected += mapCanvas_ObjectSelected;
        mapCanvas.StageModified += mapCanvas_StageModified;
        mapCanvas.TestPlayClicked += mapCanvas_TestPlayClicked;         // Feature 4
        mapCanvas.TriggerPlaced += mapCanvas_TriggerPlaced;            // Feature 5

        hScrollMap = new HScrollBar { Location = new System.Drawing.Point(180, 580), Size = new System.Drawing.Size(682, 18), Minimum = 0, Maximum = 1800, LargeChange = 200 };
        hScrollMap.Scroll += hScrollMap_Scroll;

        vScrollMap = new VScrollBar { Location = new System.Drawing.Point(862, 105), Size = new System.Drawing.Size(18, 475), Minimum = 0, Maximum = 500, LargeChange = 100 };
        vScrollMap.Scroll += vScrollMap_Scroll;

        // ===== 右パネル (880,0) 200×680 =====
        var panelRight = new Panel { Location = new System.Drawing.Point(880, 0), Size = new System.Drawing.Size(200, 680), BorderStyle = BorderStyle.FixedSingle };

        var lblEn = new Label { Text = "👾 敵", Font = FB, Location = new System.Drawing.Point(5, 5), Size = new System.Drawing.Size(185, 18) };
        lstEnemies = new ListBox { Font = F, Location = new System.Drawing.Point(5, 25), Size = new System.Drawing.Size(185, 70) };
        lstEnemies.SelectedIndexChanged += lstEnemies_SelectedIndexChanged;

        var lblGi = new Label { Text = "🔧 ギミック", Font = FB, Location = new System.Drawing.Point(5, 100), Size = new System.Drawing.Size(185, 18) };
        lstGimmicks = new ListBox { Font = F, Location = new System.Drawing.Point(5, 120), Size = new System.Drawing.Size(185, 80) };
        lstGimmicks.SelectedIndexChanged += lstGimmicks_SelectedIndexChanged;

        var lblIt = new Label { Text = "💎 アイテム", Font = FB, Location = new System.Drawing.Point(5, 206), Size = new System.Drawing.Size(185, 18) };
        lstItems = new ListBox { Font = F, Location = new System.Drawing.Point(5, 226), Size = new System.Drawing.Size(185, 60) };
        lstItems.SelectedIndexChanged += lstItems_SelectedIndexChanged;

        var lblProp = new Label { Text = "📋 プロパティ", Font = FB, Location = new System.Drawing.Point(5, 292), Size = new System.Drawing.Size(185, 18) };
        propertyGrid = new PropertyGrid { Font = F, Location = new System.Drawing.Point(5, 312), Size = new System.Drawing.Size(185, 310), HelpVisible = false, ToolbarVisible = false };

        panelRight.Controls.AddRange(new Control[] { lblEn, lstEnemies, lblGi, lstGimmicks, lblIt, lstItems, lblProp, propertyGrid });

        // ===== 下部ボタンバー (180,598) 700×78 =====
        var panelBottom = new Panel { Location = new System.Drawing.Point(180, 598), Size = new System.Drawing.Size(700, 78), BackColor = System.Drawing.Color.FromArgb(245, 245, 245), BorderStyle = BorderStyle.FixedSingle };

        // 行1: プレイ系
        btnPlay = new Button {
            Text = "▶ プレイテスト", Font = new System.Drawing.Font("Meiryo UI", 10, System.Drawing.FontStyle.Bold),
            BackColor = System.Drawing.Color.FromArgb(40, 167, 69), ForeColor = System.Drawing.Color.White, FlatStyle = FlatStyle.Flat,
            Location = new System.Drawing.Point(5, 6), Size = new System.Drawing.Size(140, 30)
        };
        btnPlay.Click += btnPlay_Click;

        btnTestPlay = new Button {  // Feature 4
            Text = "📍 ここからプレイ", Font = F,
            BackColor = System.Drawing.Color.FromArgb(0, 150, 136), ForeColor = System.Drawing.Color.White, FlatStyle = FlatStyle.Flat,
            Location = new System.Drawing.Point(150, 6), Size = new System.Drawing.Size(130, 30)
        };
        btnTestPlay.Click += btnTestPlay_Click;

        btnSave = new Button { Text = "💾 保存", Font = F, Location = new System.Drawing.Point(285, 6), Size = new System.Drawing.Size(80, 30) };
        btnSave.Click += btnSave_Click;

        // 行2: エディタ系
        btnAssetManager = new Button { Text = "📦アセット管理", Font = F, Location = new System.Drawing.Point(5, 42), Size = new System.Drawing.Size(105, 28) };
        btnAssetManager.Click += btnAssetManager_Click;

        btnTileEditor = new Button { Text = "🧱タイルエディタ", Font = F, Location = new System.Drawing.Point(115, 42), Size = new System.Drawing.Size(115, 28) };
        btnTileEditor.Click += btnTileEditor_Click;

        btnSoundMgr = new Button { Text = "🎵サウンド管理", Font = F, Location = new System.Drawing.Point(235, 42), Size = new System.Drawing.Size(110, 28) };  // Feature 3
        btnSoundMgr.Click += btnSoundMgr_Click;

        btnBgSettings = new Button { Text = "🌅背景設定", Font = F, Location = new System.Drawing.Point(350, 42), Size = new System.Drawing.Size(90, 28) };  // Feature 1
        btnBgSettings.Click += btnBgSettings_Click;

        btnAnimEditor = new Button { Text = "🎬アニメ", Font = F, Location = new System.Drawing.Point(445, 42), Size = new System.Drawing.Size(80, 28) };  // Feature 2
        btnAnimEditor.Click += btnAnimEditor_Click;

        btnImportCsv = new Button { Text = "📄CSVから生成", Font = F, Location = new System.Drawing.Point(530, 42), Size = new System.Drawing.Size(110, 28) };
        btnImportCsv.Click += btnImportCsv_Click;

        lblStatus = new Label { Font = new System.Drawing.Font("Meiryo UI", 8), Text = "...", Location = new System.Drawing.Point(370, 10), Size = new System.Drawing.Size(320, 20) };

        panelBottom.Controls.AddRange(new Control[] {
            btnPlay, btnTestPlay, btnSave,
            btnAssetManager, btnTileEditor, btnSoundMgr, btnBgSettings, btnAnimEditor, btnImportCsv,
            lblStatus
        });

        // ===== フォーム =====
        AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new System.Drawing.Size(1082, 678);
        MinimumSize = new System.Drawing.Size(1000, 640);
        Controls.AddRange(new Control[] { panelLeft, panelTools, panelTiles, mapCanvas, hScrollMap, vScrollMap, panelRight, panelBottom });
        Text = "Lab Engine - ステージエディタ";
        StartPosition = FormStartPosition.CenterScreen;
        Load += Form1_Load;
        ResumeLayout(false);
    }
}
