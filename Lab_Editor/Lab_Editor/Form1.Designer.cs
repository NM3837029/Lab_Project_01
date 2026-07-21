namespace Lab_Editor;

partial class Form1
{
    private System.ComponentModel.IContainer components = null;
    protected override void Dispose(bool disposing) { if (disposing && components != null) components.Dispose(); base.Dispose(disposing); }

    // ===== メニュー・ツールバー =====
    private System.Windows.Forms.MenuStrip menuStrip1 = null!;
    private System.Windows.Forms.ToolStrip toolStrip1 = null!;
    
    // ===== コントロール宣言 =====
    private MapCanvas mapCanvas = null!;
    private System.Windows.Forms.HScrollBar hScrollMap = null!;
    private System.Windows.Forms.VScrollBar vScrollMap = null!;

    // 左パネル (TabControlで整理)
    private System.Windows.Forms.TabControl tabLeft = null!;
    private System.Windows.Forms.TabPage tabStages = null!;
    private System.Windows.Forms.TabPage tabMapProps = null!;

    private System.Windows.Forms.ListBox lstStages = null!;
    private System.Windows.Forms.TextBox txtNewStage = null!;
    private System.Windows.Forms.Button btnCreateStage = null!, btnDeleteStage = null!;
    private System.Windows.Forms.Button btnImportCsv = null!;

    private System.Windows.Forms.NumericUpDown numStartX = null!, numStartY = null!;
    private System.Windows.Forms.CheckBox chkDoubleJump = null!, chkDash = null!, chkFireball = null!, chkFly = null!;
    private System.Windows.Forms.NumericUpDown numJumpPower = null!, numSpeed = null!;
    private System.Windows.Forms.NumericUpDown numMapW = null!, numMapH = null!;
    private System.Windows.Forms.Button btnResize = null!;

    // ツールバー用ボタン等
    private System.Windows.Forms.ToolStripButton tsbLayer1 = null!;
    private System.Windows.Forms.ToolStripButton tsbLayer2 = null!;
    private System.Windows.Forms.ToolStripButton tsbLayer3 = null!;
    private System.Windows.Forms.ToolStripButton tsbLayer4 = null!; // イベントモード
    private System.Windows.Forms.ToolStripSeparator toolStripSeparator1 = null!;
    private System.Windows.Forms.ToolStripButton tsbPen = null!;
    private System.Windows.Forms.ToolStripButton tsbEraser = null!;
    private System.Windows.Forms.ToolStripButton tsbSelect = null!;
    private System.Windows.Forms.ToolStripSeparator toolStripSeparator2 = null!;
    private System.Windows.Forms.ToolStripButton tsbPlay = null!;
    private System.Windows.Forms.ToolStripButton tsbTestPlayHere = null!;

    private System.Windows.Forms.Label lblCurrentStage = null!, lblStatus = null!, lblLayerInfo = null!;

    // 右パネル (TabControl)
    private System.Windows.Forms.TabControl tabRight = null!;
    private System.Windows.Forms.TabPage tabTilePalette = null!;
    private System.Windows.Forms.TabPage tabEventPalette = null!;
    private System.Windows.Forms.TabPage tabEventList = null!;

    private System.Windows.Forms.FlowLayoutPanel flpTiles = null!;
    private System.Windows.Forms.Button btnBulkFill = null!;

    // イベントパレット（敵・ギミック・アイテム）
    private System.Windows.Forms.ListBox lstEnemies = null!, lstGimmicks = null!, lstItems = null!;
    private System.Windows.Forms.RadioButton rbTrigger = null!, rbPlayerStart = null!, rbGoal = null!;
    
    // イベントリスト (配置済み)
    private System.Windows.Forms.ListBox lstPlacedEvents = null!;

    // プロパティ
    private System.Windows.Forms.PropertyGrid propertyGrid = null!;

    private void InitializeComponent()
    {
        this.SuspendLayout();
        var F = new System.Drawing.Font("Meiryo UI", 9);
        var FB = new System.Drawing.Font("Meiryo UI", 9, System.Drawing.FontStyle.Bold);

        // ===== メニューバー =====
        menuStrip1 = new System.Windows.Forms.MenuStrip();
        var menuFile = new System.Windows.Forms.ToolStripMenuItem("ファイル(&F)");
        var miSave = new System.Windows.Forms.ToolStripMenuItem("保存(&S)", null, btnSave_Click) { ShortcutKeys = System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.S };
        var miExit = new System.Windows.Forms.ToolStripMenuItem("終了(&X)", null, (_,_) => this.Close());
        menuFile.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] { miSave, miExit });

        var menuEdit = new System.Windows.Forms.ToolStripMenuItem("編集(&E)");
        var miUndo = new System.Windows.Forms.ToolStripMenuItem("元に戻す(&U)", null, (_,_) => Undo()) { ShortcutKeys = System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.Z };
        var miRedo = new System.Windows.Forms.ToolStripMenuItem("やり直し(&R)", null, (_,_) => Redo()) { ShortcutKeys = System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.Y };
        menuEdit.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] { miUndo, miRedo });

        var menuData = new System.Windows.Forms.ToolStripMenuItem("アセット管理(&D)");
        var miAsset = new System.Windows.Forms.ToolStripMenuItem("アセット管理", null, btnAssetManager_Click);
        var miTile = new System.Windows.Forms.ToolStripMenuItem("タイルエディタ", null, btnTileEditor_Click);
        var miAnim = new System.Windows.Forms.ToolStripMenuItem("アニメーションエディタ", null, btnAnimEditor_Click);
        var miSound = new System.Windows.Forms.ToolStripMenuItem("サウンド管理", null, btnSoundMgr_Click);
        var miBg = new System.Windows.Forms.ToolStripMenuItem("背景設定", null, btnBgSettings_Click);
        menuData.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] { miAsset, miTile, miAnim, miSound, miBg });

        var menuPlay = new System.Windows.Forms.ToolStripMenuItem("プレイ(&P)");
        var miPlay = new System.Windows.Forms.ToolStripMenuItem("テストプレイ", null, btnPlay_Click) { ShortcutKeys = System.Windows.Forms.Keys.F5 };
        menuPlay.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] { miPlay });

        menuStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[] { menuFile, menuEdit, menuData, menuPlay });

        // ===== ツールバー =====
        toolStrip1 = new System.Windows.Forms.ToolStrip();
        
        tsbLayer1 = new System.Windows.Forms.ToolStripButton("Layer 1 (遠景)") { CheckOnClick = true, DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text };
        tsbLayer2 = new System.Windows.Forms.ToolStripButton("Layer 2 (メイン)") { CheckOnClick = true, Checked = true, DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text };
        tsbLayer3 = new System.Windows.Forms.ToolStripButton("Layer 3 (近景)") { CheckOnClick = true, DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text };
        tsbLayer4 = new System.Windows.Forms.ToolStripButton("Layer 4 (イベント)") { CheckOnClick = true, DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text };
        
        tsbLayer1.Click += TsbLayer_Click;
        tsbLayer2.Click += TsbLayer_Click;
        tsbLayer3.Click += TsbLayer_Click;
        tsbLayer4.Click += TsbLayer_Click;

        toolStripSeparator1 = new System.Windows.Forms.ToolStripSeparator();

        tsbPen = new System.Windows.Forms.ToolStripButton("🖊ペン") { CheckOnClick = true, Checked = true, DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text };
        tsbEraser = new System.Windows.Forms.ToolStripButton("⬜消しゴム") { CheckOnClick = true, DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text };
        tsbSelect = new System.Windows.Forms.ToolStripButton("🔍選択") { CheckOnClick = true, DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text };
        
        tsbPen.Click += TsbTool_Click;
        tsbEraser.Click += TsbTool_Click;
        tsbSelect.Click += TsbTool_Click;

        toolStripSeparator2 = new System.Windows.Forms.ToolStripSeparator();
        tsbPlay = new System.Windows.Forms.ToolStripButton("▶ プレイ") { DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text };
        tsbPlay.Click += btnPlay_Click;
        tsbTestPlayHere = new System.Windows.Forms.ToolStripButton("📍 ここから") { DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text };
        tsbTestPlayHere.Click += btnTestPlay_Click;

        toolStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            tsbLayer1, tsbLayer2, tsbLayer3, tsbLayer4,
            toolStripSeparator1,
            tsbPen, tsbEraser, tsbSelect,
            toolStripSeparator2,
            tsbPlay, tsbTestPlayHere
        });

        // ===== 左パネル (TabControl) =====
        tabLeft = new System.Windows.Forms.TabControl { Location = new System.Drawing.Point(0, 50), Size = new System.Drawing.Size(200, 630), Font = F };
        tabStages = new System.Windows.Forms.TabPage("ステージ");
        tabMapProps = new System.Windows.Forms.TabPage("マップ設定");

        // ステージタブ
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

        // マップ設定タブ
        var lblSize = new System.Windows.Forms.Label { Text = "📐 マップサイズ", Font = FB, Location = new System.Drawing.Point(5, 5), Size = new System.Drawing.Size(170, 20) };
        var lblW = new System.Windows.Forms.Label { Text = "W:", Location = new System.Drawing.Point(5, 27), Size = new System.Drawing.Size(20, 18) };
        numMapW = new System.Windows.Forms.NumericUpDown { Location = new System.Drawing.Point(27, 25), Size = new System.Drawing.Size(55, 23), Minimum = 10, Maximum = 500, Value = 80 };
        var lblH = new System.Windows.Forms.Label { Text = "H:", Location = new System.Drawing.Point(87, 27), Size = new System.Drawing.Size(20, 18) };
        numMapH = new System.Windows.Forms.NumericUpDown { Location = new System.Drawing.Point(108, 25), Size = new System.Drawing.Size(45, 23), Minimum = 5, Maximum = 100, Value = 15 };
        btnResize = new System.Windows.Forms.Button { Text = "リサイズ", Location = new System.Drawing.Point(5, 50), Size = new System.Drawing.Size(150, 26) };
        btnResize.Click += btnResize_Click;

        var lblPlayer = new System.Windows.Forms.Label { Text = "⚙ プレイヤー設定", Font = FB, Location = new System.Drawing.Point(5, 85), Size = new System.Drawing.Size(170, 20) };
        var lblSX = new System.Windows.Forms.Label { Text = "開始X:", Location = new System.Drawing.Point(5, 110), Size = new System.Drawing.Size(42, 18) };
        numStartX = new System.Windows.Forms.NumericUpDown { Location = new System.Drawing.Point(48, 108), Size = new System.Drawing.Size(65, 23), Maximum = 9999, Value = 48 };
        numStartX.ValueChanged += PlayerSetting_Changed;
        var lblSY = new System.Windows.Forms.Label { Text = "Y:", Location = new System.Drawing.Point(118, 110), Size = new System.Drawing.Size(18, 18) };
        numStartY = new System.Windows.Forms.NumericUpDown { Location = new System.Drawing.Point(135, 108), Size = new System.Drawing.Size(55, 23), Maximum = 9999, Value = 320 };
        numStartY.ValueChanged += PlayerSetting_Changed;

        chkDoubleJump = new System.Windows.Forms.CheckBox { Text = "2段ジャンプ", Location = new System.Drawing.Point(5, 140), Size = new System.Drawing.Size(168, 20) };
        chkDoubleJump.CheckedChanged += PlayerSetting_Changed;
        chkDash = new System.Windows.Forms.CheckBox { Text = "ダッシュ", Location = new System.Drawing.Point(5, 165), Size = new System.Drawing.Size(168, 20) };
        chkDash.CheckedChanged += PlayerSetting_Changed;
        chkFireball = new System.Windows.Forms.CheckBox { Text = "火の玉", Location = new System.Drawing.Point(5, 190), Size = new System.Drawing.Size(168, 20) };
        chkFireball.CheckedChanged += PlayerSetting_Changed;
        chkFly = new System.Windows.Forms.CheckBox { Text = "飛行", Location = new System.Drawing.Point(5, 215), Size = new System.Drawing.Size(168, 20) };
        chkFly.CheckedChanged += PlayerSetting_Changed;

        var lblJP = new System.Windows.Forms.Label { Text = "ジャンプ力:", Location = new System.Drawing.Point(5, 245), Size = new System.Drawing.Size(75, 18) };
        numJumpPower = new System.Windows.Forms.NumericUpDown { Location = new System.Drawing.Point(85, 243), Size = new System.Drawing.Size(50, 23), Minimum = -30, Maximum = 0, Value = -12 };
        numJumpPower.ValueChanged += PlayerSetting_Changed;
        var lblSp = new System.Windows.Forms.Label { Text = "移動速度:", Location = new System.Drawing.Point(5, 275), Size = new System.Drawing.Size(75, 18) };
        numSpeed = new System.Windows.Forms.NumericUpDown { Location = new System.Drawing.Point(85, 273), Size = new System.Drawing.Size(50, 23), DecimalPlaces = 1, Increment = 0.5m, Maximum = 20, Value = 4 };
        numSpeed.ValueChanged += PlayerSetting_Changed;

        tabMapProps.Controls.AddRange(new System.Windows.Forms.Control[] {
            lblSize, lblW, numMapW, lblH, numMapH, btnResize,
            lblPlayer, lblSX, numStartX, lblSY, numStartY,
            chkDoubleJump, chkDash, chkFireball, chkFly,
            lblJP, numJumpPower, lblSp, numSpeed
        });

        tabLeft.Controls.Add(tabStages);
        tabLeft.Controls.Add(tabMapProps);

        // ===== 右パネル (TabControl) =====
        tabRight = new System.Windows.Forms.TabControl { Location = new System.Drawing.Point(890, 50), Size = new System.Drawing.Size(220, 630), Font = F };
        tabTilePalette = new System.Windows.Forms.TabPage("タイル");
        tabEventPalette = new System.Windows.Forms.TabPage("配置対象");
        tabEventList = new System.Windows.Forms.TabPage("イベントリスト");

        flpTiles = new System.Windows.Forms.FlowLayoutPanel { Dock = System.Windows.Forms.DockStyle.Fill, FlowDirection = System.Windows.Forms.FlowDirection.LeftToRight, AutoScroll = true };
        // Feature: UI改善（友人フィードバック対応）— 数値指定で行・列の範囲を一括配置するボタン。
        // Dock=Fillのflptilesを先にAddし、Dock=Bottomのパネルを後からAddする安全な順序を守る。
        var pnlBulkFill = new System.Windows.Forms.Panel { Dock = System.Windows.Forms.DockStyle.Bottom, Height = 34 };
        btnBulkFill = new System.Windows.Forms.Button { Text = "🔢 数値指定で一括配置", Dock = System.Windows.Forms.DockStyle.Fill, FlatStyle = System.Windows.Forms.FlatStyle.Flat };
        btnBulkFill.Click += BtnBulkFill_Click;
        pnlBulkFill.Controls.Add(btnBulkFill);
        tabTilePalette.Controls.Add(flpTiles);
        tabTilePalette.Controls.Add(pnlBulkFill);

        // イベント配置パレット
        var lblEn = new System.Windows.Forms.Label { Text = "👾 敵", Font = FB, Location = new System.Drawing.Point(5, 5), Size = new System.Drawing.Size(185, 18) };
        lstEnemies = new System.Windows.Forms.ListBox { Location = new System.Drawing.Point(5, 25), Size = new System.Drawing.Size(200, 70) };
        lstEnemies.SelectedIndexChanged += lstEnemies_SelectedIndexChanged;

        var lblGi = new System.Windows.Forms.Label { Text = "🔧 ギミック", Font = FB, Location = new System.Drawing.Point(5, 100), Size = new System.Drawing.Size(185, 18) };
        lstGimmicks = new System.Windows.Forms.ListBox { Location = new System.Drawing.Point(5, 120), Size = new System.Drawing.Size(200, 70) };
        lstGimmicks.SelectedIndexChanged += lstGimmicks_SelectedIndexChanged;

        var lblIt = new System.Windows.Forms.Label { Text = "💎 アイテム", Font = FB, Location = new System.Drawing.Point(5, 195), Size = new System.Drawing.Size(185, 18) };
        lstItems = new System.Windows.Forms.ListBox { Location = new System.Drawing.Point(5, 215), Size = new System.Drawing.Size(200, 60) };
        lstItems.SelectedIndexChanged += lstItems_SelectedIndexChanged;

        rbTrigger = new System.Windows.Forms.RadioButton { Text = "⚡汎用トリガー", Location = new System.Drawing.Point(5, 285), Size = new System.Drawing.Size(120, 20), Checked = true };
        rbPlayerStart = new System.Windows.Forms.RadioButton { Text = "🚶プレイヤー開始位置", Location = new System.Drawing.Point(5, 310), Size = new System.Drawing.Size(200, 20) };
        rbGoal = new System.Windows.Forms.RadioButton { Text = "🏁ゴール位置", Location = new System.Drawing.Point(5, 335), Size = new System.Drawing.Size(200, 20) };
        rbTrigger.CheckedChanged += EventModeItem_CheckedChanged;
        rbPlayerStart.CheckedChanged += EventModeItem_CheckedChanged;
        rbGoal.CheckedChanged += EventModeItem_CheckedChanged;

        tabEventPalette.Controls.AddRange(new System.Windows.Forms.Control[] { lblEn, lstEnemies, lblGi, lstGimmicks, lblIt, lstItems, rbTrigger, rbPlayerStart, rbGoal });

        // イベントリスト (配置済み)
        lstPlacedEvents = new System.Windows.Forms.ListBox { Dock = System.Windows.Forms.DockStyle.Fill };
        lstPlacedEvents.SelectedIndexChanged += LstPlacedEvents_SelectedIndexChanged;
        lstPlacedEvents.DoubleClick += LstPlacedEvents_DoubleClick;
        tabEventList.Controls.Add(lstPlacedEvents);

        tabRight.Controls.Add(tabTilePalette);
        tabRight.Controls.Add(tabEventPalette);
        tabRight.Controls.Add(tabEventList);

        // ===== 共通プロパティグリッド (右パネルの下部) =====
        propertyGrid = new System.Windows.Forms.PropertyGrid { Location = new System.Drawing.Point(890, 480), Size = new System.Drawing.Size(220, 200), HelpVisible = false, ToolbarVisible = false, Font = F };

        // ===== 情報ラベル =====
        lblCurrentStage = new System.Windows.Forms.Label { Font = new System.Drawing.Font("Meiryo UI", 9, System.Drawing.FontStyle.Bold), Text = "編集中: ---", Location = new System.Drawing.Point(210, 50), Size = new System.Drawing.Size(300, 16), ForeColor = System.Drawing.Color.DarkBlue };
        lblLayerInfo = new System.Windows.Forms.Label { Font = new System.Drawing.Font("Meiryo UI", 9), Text = "", Location = new System.Drawing.Point(520, 50), Size = new System.Drawing.Size(300, 16), ForeColor = System.Drawing.Color.DarkGreen };
        lblStatus = new System.Windows.Forms.Label { Location = new System.Drawing.Point(210, 660), Size = new System.Drawing.Size(300, 20) };

        // ===== マップキャンバス =====
        mapCanvas = new MapCanvas { Location = new System.Drawing.Point(210, 70), Size = new System.Drawing.Size(650, 580), BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle };
        mapCanvas.ObjectSelected += mapCanvas_ObjectSelected;
        mapCanvas.StageModified += mapCanvas_StageModified;
        mapCanvas.EditCompleted += mapCanvas_EditCompleted;
        mapCanvas.TestPlayClicked += mapCanvas_TestPlayClicked;
        mapCanvas.TriggerPlaced += mapCanvas_TriggerPlaced;

        hScrollMap = new System.Windows.Forms.HScrollBar { Location = new System.Drawing.Point(210, 650), Size = new System.Drawing.Size(650, 18), Minimum = 0, Maximum = 1800, LargeChange = 200 };
        hScrollMap.Scroll += hScrollMap_Scroll;
        vScrollMap = new System.Windows.Forms.VScrollBar { Location = new System.Drawing.Point(860, 70), Size = new System.Drawing.Size(18, 580), Minimum = 0, Maximum = 500, LargeChange = 100 };
        vScrollMap.Scroll += vScrollMap_Scroll;

        // Form 設定
        this.ClientSize = new System.Drawing.Size(1120, 690);
        this.Controls.Add(menuStrip1);
        this.Controls.Add(toolStrip1);
        this.Controls.Add(tabLeft);
        this.Controls.Add(tabRight);
        this.Controls.Add(propertyGrid);
        this.Controls.Add(lblCurrentStage);
        this.Controls.Add(lblLayerInfo);
        this.Controls.Add(lblStatus);
        this.Controls.Add(mapCanvas);
        this.Controls.Add(hScrollMap);
        this.Controls.Add(vScrollMap);
        this.MainMenuStrip = menuStrip1;
        this.Name = "Form1";
        this.Text = "Lab Editor";
        this.Load += new System.EventHandler(this.Form1_Load);
        
        // 修正：プロパティグリッドの表示順を調整（タブの裏に隠れないように）
        propertyGrid.BringToFront();
        tabRight.Height = 420; // プロパティグリッドのスペースを空ける
        
        this.ResumeLayout(false);
        this.PerformLayout();
    }
}
