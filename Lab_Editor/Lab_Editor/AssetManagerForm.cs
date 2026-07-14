using Newtonsoft.Json;

namespace Lab_Editor;

/// <summary>
/// アセット管理 - 敵・ギミック・アイテムの総合編集ウィンドウ
/// ・画像プレビュー付き
/// ・ファイルダイアログでスプライト選択（imgフォルダへ自動コピー）
/// ・敵の巡回範囲・HP・サイズ等フル編集
/// ・type_enumごとのパラメータ説明
/// ・ギミック・アイテムもフル編集
/// </summary>
public partial class AssetManagerForm : Form
{
    private readonly string assetsPath;
    private readonly string projectRoot;
    private AssetDefinitions assets;

    private TabControl tabControl = null!;
    private DataGridView dgvEnemies = null!, dgvGimmicks = null!, dgvItems = null!;
    private ListBox lstCommonEvents = null!;
    private List<CommonEventDef> _commonEvents = new();
    private TextBox txtSearch = null!;
    private Button btnDuplicate = null!;
    private PictureBox pbPreview = null!;
    private Label lblPreviewPath = null!;
    private Button btnSave = null!, btnClose = null!;
    private RichTextBox rtbTypeHint = null!;

    // ==== Feature: Configurable Behavior Parameters (M1) ====
    // 行(DataGridViewRow)ごとに、グリッド列には出さない挙動パラメータ本体を保持する。
    // idではなく行オブジェクト自体をキーにすることで、id欄のリネームによる不整合を避ける。
    private readonly Dictionary<DataGridViewRow, EnemyDef> _enemyParams = new();
    private readonly Dictionary<DataGridViewRow, GimmickDef> _gimmickParams = new();
    private Panel pnlBehaviorParams = null!;
    private Label lblTypeHintTitle = null!;
    private bool _isUpdatingBehaviorPanel = false;

    // type_enum ごとに表示する挙動パラメータ欄 (フィールド名, ラベル, 小数点以下桁数)
    private static readonly Dictionary<int, (string Field, string Label, int Decimals)[]> EnemyParamFields = new()
    {
        [0] = new[] { ("moveSpeed", "移動速度係数", 2) }, // PATROL
        [1] = new[] { ("actionInterval", "ジャンプ間隔(フレーム)", 0), ("jumpPowerMult", "ジャンプ力係数", 2) }, // JUMPER
        [2] = new[] { ("actionInterval", "射撃間隔(フレーム)", 0), ("projectileSpeed", "弾速係数", 2) }, // STATIONARY
        [3] = new[] { ("triggerRange", "索敵X範囲(px)", 0), ("detectionRangeY", "索敵Y範囲(px)", 0), ("moveSpeed", "巡回速度係数", 2), ("cooldownTime", "射撃後クールダウン(フレーム)", 0), ("projectileSpeed", "弾速係数", 2) }, // PATROL_SHOOTER
        [4] = new[] { ("moveSpeed", "移動速度係数", 2) }, // WALKER
        [5] = new[] { ("moveSpeed", "移動速度係数", 2), ("jumpPowerMult", "ジャンプ力係数", 2) }, // CHASER
        [6] = new[] { ("triggerRange", "発動距離(px)", 0), ("chargeTime", "溜め時間(フレーム)", 0), ("dashSpeedMult", "突進速度係数", 2), ("dashDuration", "突進継続時間(フレーム)", 0), ("cooldownTime", "クールダウン(フレーム)", 0) }, // DASH_CHARGER
        [7] = new[] { ("triggerRange", "真下判定幅(px)", 0), ("fallDelay", "落下開始遅延(フレーム)", 0), ("cooldownTime", "着地後クールダウン(フレーム)", 0) }, // FALLER
        [8] = new[] { ("actionInterval", "射撃間隔(フレーム)", 0), ("spreadAngle", "拡散角度(ラジアン)", 2), ("spreadCount", "弾数", 0), ("projectileSpeed", "弾速係数", 2) }, // SPREAD_SHOOTER
        [9] = new[] { ("actionInterval", "射撃間隔(フレーム)", 0), ("projectileSpeed", "弾速係数", 2) }, // AIMED_SHOOTER
        [10] = new[] { ("floatAmplitude", "浮遊振幅(px)", 0), ("floatFrequency", "浮遊周波数", 3), ("moveSpeed", "接近速度係数", 2) }, // FLOATER
        [11] = new[] { ("actionInterval", "テレポート間隔(フレーム)", 0), ("teleportRangeMin", "オフセット最小(px)", 0), ("teleportRangeMax", "オフセット最大(px)", 0) }, // TELEPORTER
        [12] = new[] { ("moveSpeed", "通常時速度係数", 2), ("enragedMoveSpeed", "覚醒後速度係数", 2), ("shrinkFactor", "縮小率", 2) }, // SHRINKER
        [13] = new[] { ("moveSpeed", "移動速度係数", 2), ("shieldOffDuration", "無敵解除継続(フレーム)", 0), ("shieldOnDuration", "無敵継続(フレーム)", 0) }, // SHIELD
        [14] = new[] { ("mimicDelayFrames", "遅延フレーム数", 0) }, // MIMIC_GHOST
        [15] = new[] { ("moveSpeed", "移動速度係数", 2), ("sizeAmplitude", "スケール振幅", 2), ("sizeFrequency", "スケール周波数", 3), ("minScale", "最小スケール", 2) }, // SIZE_SHIFTER
        [16] = new[] { ("moveSpeed", "基準速度係数", 2), ("tempoFrequency", "周波数", 3), ("tempoMin", "speedScale最小", 2), ("tempoMax", "speedScale最大", 2) }, // TEMPO_WARPER
        [17] = new[] { ("moveSpeed", "移動速度係数", 2), ("effectRange", "効果範囲(px)", 0), ("brightnessMin", "最小輝度", 2) }, // BRIGHTNESS_PHANTOM
        [18] = new[] { ("moveSpeed", "移動速度係数", 2), ("effectRange", "効果範囲(px)", 0), ("tintStrength", "色シフト強度", 2) }, // COLOR_SHIFTER
        [19] = new[] { ("effectRange", "効果範囲(px)", 0), ("zoomAmplitude", "ズーム振幅", 2), ("zoomFrequency", "ズーム周波数", 3) }, // ZOOM_DISRUPTOR
    };

    private static readonly Dictionary<int, (string Field, string Label, int Decimals)[]> GimmickParamFields = new()
    {
        [0] = new[] { ("warpOffsetPx", "ワープ後オフセット(px)", 1) }, // CUT_PORTAL
        [1] = new[] { ("rotationSpeed", "回転速度(rad/フレーム)", 3) }, // ROTATING_BRIDGE
        [4] = new[] { ("sinkSpeed", "降下速度(px/フレーム)", 2), ("maxDepthOffset", "最大沈み込み(px)", 0) }, // FALLING_LIFT
        [5] = new[] { ("pushOutDistance", "押し出し距離係数", 2) }, // REFLECT_MIRROR
        [6] = new[] { ("triggerWidthThreshold", "起動に必要な横幅(px)", 0) }, // WEIGHT_SWITCH
        [11] = new[] { ("standDelayFrames", "乗ってから落下まで(フレーム)", 0), ("standTolerancePx", "乗り判定の許容誤差(px)", 0), ("respawnDelayFrames", "復活までの時間(フレーム)", 0) }, // CHIKUWA_BLOCK
        [12] = new[] { ("radius", "効果範囲の半径(px)", 0) }, // TIME_FIELD
        [14] = new[] { ("travelDistance", "可動距離(px)", 0), ("oscillationSpeed", "往復の速さ", 3) }, // MOVING_PLATFORM
        [17] = new[] { ("travelDistance", "可動距離(px)", 0), ("stepIncrement", "1回あたりの移動割合", 2) }, // FRAMESTEP_LIFT
        [18] = new[] { ("brightLevel", "明転時の輝度", 2), ("darkLevel", "暗転時の輝度", 2) }, // BRIGHTNESS_ZONE
        [19] = new[] { ("tintR", "色調R", 2), ("tintG", "色調G", 2), ("tintB", "色調B", 2) }, // COLOR_ZONE
        [20] = new[] { ("zoomLevel", "ズーム倍率", 2) }, // ZOOM_LENS
        [21] = new[] { ("zoomLevel", "ズーム倍率", 2), ("brightLevel", "明るさ倍率", 2) }, // SLOWMO_FIELD
    };

    // type_enum の説明
    private static readonly (int type, string desc, string detail)[] EnemyTypes =
    {
        (0, "0 = 巡回 (Patrol)", "左右にpatrolLeft～patrolRightの範囲で巡回します。\npatrol_left/patrol_rightをステージJSON配置時に指定可能。"),
        (1, "1 = ジャンプ (Jumper)", "その場で定期的にジャンプします。重力が適用されます。"),
        (2, "2 = 固定砲台 (Stationary)", "プレイヤーに向いて定期的に弾を撃ちます。移動しません。"),
        (3, "3 = 巡回砲台 (Patrol+Shoot)", "近づいたプレイヤーを攻撃し、それ以外は巡回します。"),
        (4, "4 = 歩いてくる (Walker)", "常にプレイヤー方向へ地上を歩きます。崖のふちで自動的に止まります。"),
        (5, "5 = 追っかけてくる (Chaser)", "Walkerより速く追跡し、壁に当たると自動でジャンプします。"),
        (6, "6 = 突進 (Dash Charger)", "射程内に入ると溜めてから高速直進で突進し、その後クールダウンします。"),
        (7, "7 = 落ちてくる敵 (Faller)", "上空で待機し、プレイヤーが真下を通過すると落下してきます。"),
        (8, "8 = 拡散弾 (Spread Shooter)", "固定位置から3方向に弾を拡散射撃します。"),
        (9, "9 = 照準弾 (Aimed Shooter)", "発射時のプレイヤー位置へ正確に狙い撃ちます。"),
        (10, "10 = 浮遊敵 (Floater)", "重力を受けず、サインカーブで上下に浮遊しながらゆっくり接近します。"),
        (11, "11 = テレポーター (Teleporter)", "一定間隔でプレイヤー付近へ瞬間移動します。"),
        (12, "12 = 分裂もどき (Shrinker)", "致死ダメージを受けると一度だけ縮小・高速化して復活します（2回目で死亡）。"),
        (13, "13 = シールド (Shield)", "一定間隔で無敵状態（金色に発光）になり、弾によるダメージを無効化します。"),
        (14, "14 = 幽霊敵 (Mimic Ghost)", "プレイヤーの巻き戻し履歴を約1.5秒遅延して再生し、過去の動きをなぞります。"),
        (15, "15 = 大きさが変わる敵 (Size Shifter)", "scaleが周期的に変化し、当たり判定の大きさも連動して変わります。"),
        (16, "16 = 速さ操作敵 (Tempo Warper)", "speedScaleが周期的に激しく変化し、接近速度が乱れます。"),
        (17, "17 = 明るさ操作敵 (Brightness Phantom)", "射程内で画面を暗転させます（新画面エフェクト機能と連携）。"),
        (18, "18 = 色調整敵 (Color Shifter)", "射程内で画面の色調を変化させます（新画面エフェクト機能と連携）。"),
        (19, "19 = ズーム撹乱敵 (Zoom Disruptor)", "射程内で画面ズームを周期的に揺さぶります（新画面エフェクト機能と連携）。"),
    };
    private static readonly (int type, string desc)[] GimmickTypes =
    {
        (0, "0 = ポータル"),
        (1, "1 = 回転橋(自動)"),
        (2, "2 = 回転橋(手動)"),
        (3, "3 = 破壊ブロック"),
        (4, "4 = 落下リフト"),
        (5, "5 = 反射鏡"),
        (6, "6 = 重量スイッチ"),
        (7, "7 = スケールボックス"),
        (8, "8 = ゲート扉"),
        (9, "9 = 棘床"),
        (10, "10 = スケール地面"),
        (11, "11 = ちくわブロック"),
        (12, "12 = 時間フィールド"),
        (13, "13 = 食らいギミック"),
        (14, "14 = 動く足場 (Moving Platform)"),
        (15, "15 = 岩 (Pushable Rock)"),
        (16, "16 = 早送りゲート (Fastforward Gate)"),
        (17, "17 = コマ送りリフト (Framestep Lift)"),
        (18, "18 = 明暗ゾーン (Brightness Zone)"),
        (19, "19 = 色調ゾーン (Color Zone)"),
        (20, "20 = ズームレンズ (Zoom Lens)"),
        (21, "21 = スローフィールド (Slowmo Field)"),
        (22, "22 = 色ロック足場 (Color Lock Platform)"),
        (23, "23 = 明暗ロック足場 (Brightness Lock Platform)"),
    };
    private static readonly (int type, string desc)[] ItemTypes =
    {
        (0, "0 = なし"),
        (1, "1 = コイン"),
        (2, "2 = 回復アイテム"),
    };

    public AssetManagerForm(string assetsPath, AssetDefinitions assets)
    {
        this.assetsPath = assetsPath;
        this.projectRoot = Path.GetDirectoryName(assetsPath)!;
        this.assets = assets;
        _commonEvents = assets.CommonEvents
            .Select(ce => new CommonEventDef { id = ce.id, name = ce.name, actions = new List<EventActionEntry>(ce.actions) })
            .ToList();
        InitUI();
        LoadData();
    }

    private List<string> GetStageFileNames()
    {
        string stagesPath = Path.Combine(assetsPath, "stages");
        if (!Directory.Exists(stagesPath)) return new List<string>();
        return Directory.GetFiles(stagesPath, "*.json")
            .Select(Path.GetFileName)
            .Where(n => n != null && n != "_test_play.json")
            .Select(n => n!)
            .ToList();
    }

    private void InitUI()
    {
        Text = "アセット管理エディタ - 敵 / ギミック / アイテム";
        Size = new Size(1100, 620);
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Meiryo UI", 9);

        // ===== 検索・複製ツールバー (MZ風: ID/名前検索 + 選択行の複製) =====
        var pnlToolbar = new Panel { Location = new Point(5, 5), Size = new Size(820, 30) };
        var lblSearch = new Label { Text = "🔍", Location = new Point(0, 6), Size = new Size(20, 20) };
        txtSearch = new TextBox { Location = new Point(20, 3), Size = new Size(260, 23), PlaceholderText = "ID・名前で検索..." };
        txtSearch.TextChanged += (s, e) => ApplySearchFilter();
        btnDuplicate = new Button { Text = "⧉ 選択行を複製", Location = new Point(290, 2), Size = new Size(130, 25) };
        btnDuplicate.Click += BtnDuplicate_Click;
        pnlToolbar.Controls.AddRange(new Control[] { lblSearch, txtSearch, btnDuplicate });

        tabControl = new TabControl { Location = new Point(5, 38), Size = new Size(820, 517) };

        // ===== 右サイドパネル =====
        var pnlRight = new Panel { Location = new Point(835, 5), Size = new Size(250, 550), BorderStyle = BorderStyle.FixedSingle };

        var lblPrev = new Label { Text = "🖼 スプライトプレビュー", Location = new Point(5, 5), Size = new Size(240, 20), Font = new Font("Meiryo UI", 9, FontStyle.Bold) };
        pbPreview = new PictureBox { Location = new Point(5, 28), Size = new Size(238, 180), BorderStyle = BorderStyle.FixedSingle, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Black };
        lblPreviewPath = new Label { Location = new Point(5, 212), Size = new Size(238, 40), Font = new Font("Meiryo UI", 7), ForeColor = Color.Gray, Text = "(選択なし)" };

        lblTypeHintTitle = new Label { Text = "📋 タイプ説明", Location = new Point(5, 258), Size = new Size(238, 20), Font = new Font("Meiryo UI", 9, FontStyle.Bold) };
        rtbTypeHint = new RichTextBox
        {
            Location = new Point(5, 280),
            Size = new Size(238, 265),
            ReadOnly = true,
            ScrollBars = RichTextBoxScrollBars.Vertical,
            Font = new Font("Meiryo UI", 8),
            BackColor = Color.FromArgb(250, 250, 250),
            BorderStyle = BorderStyle.None
        };

        // Feature: Configurable Behavior Parameters (M1) — 選択中の敵/ギミック行のtype_enumに応じて
        // 挙動パラメータの入力欄を動的に切り替えるパネル。rtbTypeHintと同じ場所に重ねて表示し、
        // 行が選択されている間だけこちらを前面に出す（何も選択されていない時は従来通りタイプ一覧を見せる）。
        pnlBehaviorParams = new Panel
        {
            Location = new Point(5, 280),
            Size = new Size(238, 265),
            AutoScroll = true,
            Visible = false
        };

        pnlRight.Controls.AddRange(new Control[] { lblPrev, pbPreview, lblPreviewPath, lblTypeHintTitle, rtbTypeHint, pnlBehaviorParams });

        // ===== 敵タブ =====
        var tabEnemies = new TabPage("👾 敵 (Enemies)");
        dgvEnemies = CreateEnemyGrid();
        tabEnemies.Controls.Add(dgvEnemies);

        // ===== ギミックタブ =====
        var tabGimmicks = new TabPage("🔧 ギミック (Gimmicks)");
        dgvGimmicks = CreateGimmickGrid();
        tabGimmicks.Controls.Add(dgvGimmicks);

        // ===== アイテムタブ =====
        var tabItems = new TabPage("💎 アイテム (Items)");
        dgvItems = CreateItemGrid();
        tabItems.Controls.Add(dgvItems);

        // ===== コモンイベントタブ (RPGツクールMZ風: 複数トリガーから呼び出せる共通処理) =====
        var tabCommonEvents = new TabPage("🔔 コモンイベント");
        lstCommonEvents = new ListBox { Location = new Point(5, 5), Size = new Size(790, 470), Font = new Font("Meiryo UI", 9) };
        lstCommonEvents.DoubleClick += (s, e) => EditSelectedCommonEvent();
        var btnCeEdit = new Button { Text = "✎ 編集", Location = new Point(5, 480), Size = new Size(100, 26) };
        btnCeEdit.Click += (s, e) => EditSelectedCommonEvent();
        var btnCeDelete = new Button { Text = "🗑 削除", Location = new Point(110, 480), Size = new Size(100, 26) };
        btnCeDelete.Click += (s, e) => DeleteSelectedCommonEvent();
        tabCommonEvents.Controls.AddRange(new Control[] { lstCommonEvents, btnCeEdit, btnCeDelete });

        tabControl.TabPages.AddRange(new TabPage[] { tabEnemies, tabGimmicks, tabItems, tabCommonEvents });
        tabControl.SelectedIndexChanged += (s, e) => { txtSearch.Text = ""; pnlBehaviorParams.Visible = false; rtbTypeHint.Visible = true; UpdateTypeHint(); };

        // ===== 下部ボタン =====
        var pnlBottom = new Panel { Location = new Point(5, 558), Size = new Size(1082, 40) };

        btnSave = new Button
        {
            Text = "💾 保存して閉じる",
            Location = new Point(720, 5), Size = new Size(160, 30),
            BackColor = Color.FromArgb(40, 167, 69), ForeColor = Color.White, FlatStyle = FlatStyle.Flat,
            Font = new Font("Meiryo UI", 10, FontStyle.Bold)
        };
        btnSave.Click += BtnSave_Click;

        btnClose = new Button { Text = "キャンセル", Location = new Point(600, 5), Size = new Size(110, 30) };
        btnClose.Click += (s, e) => Close();

        var btnAddEnemy = new Button { Text = "＋ 敵追加", Location = new Point(5, 5), Size = new Size(100, 30) };
        btnAddEnemy.Click += (s, e) => AddRow(dgvEnemies, GetDefaultEnemyRow());

        var btnAddGimmick = new Button { Text = "＋ ギミック追加", Location = new Point(110, 5), Size = new Size(120, 30) };
        btnAddGimmick.Click += (s, e) => AddRow(dgvGimmicks, GetDefaultGimmickRow());

        var btnAddItem = new Button { Text = "＋ アイテム追加", Location = new Point(235, 5), Size = new Size(120, 30) };
        btnAddItem.Click += (s, e) => AddRow(dgvItems, GetDefaultItemRow());

        var btnAddCommonEvent = new Button { Text = "＋ コモンイベント追加", Location = new Point(360, 5), Size = new Size(150, 30) };
        btnAddCommonEvent.Click += (s, e) => AddCommonEvent();

        pnlBottom.Controls.AddRange(new Control[] { btnAddEnemy, btnAddGimmick, btnAddItem, btnAddCommonEvent, btnClose, btnSave });

        Controls.AddRange(new Control[] { pnlToolbar, tabControl, pnlRight, pnlBottom });
        RefreshCommonEventsList();
        UpdateTypeHint();
    }

    private DataGridView CreateEnemyGrid()
    {
        var dgv = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            Font = new Font("Meiryo UI", 9),
            RowHeadersWidth = 25
        };

        dgv.Columns.AddRange(new DataGridViewColumn[]
        {
            new DataGridViewTextBoxColumn { Name="id",       HeaderText="ID",       FillWeight=80 },
            new DataGridViewTextBoxColumn { Name="name",     HeaderText="名前",     FillWeight=100 },
            new DataGridViewComboBoxColumn
            {
                Name="type_enum", HeaderText="タイプ", FillWeight=100,
                DataSource = EnemyTypes.Select(t => t.desc).ToArray(),
                DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton
            },
            new DataGridViewTextBoxColumn { Name="hp",     HeaderText="HP",        FillWeight=40 },
            new DataGridViewTextBoxColumn { Name="width",  HeaderText="幅px",       FillWeight=40 },
            new DataGridViewTextBoxColumn { Name="height", HeaderText="高さpx",     FillWeight=40 },
            new DataGridViewTextBoxColumn { Name="hitboxOffsetX", Visible=false },
            new DataGridViewTextBoxColumn { Name="hitboxOffsetY", Visible=false },
            new DataGridViewTextBoxColumn { Name="hitboxWidth", Visible=false },
            new DataGridViewTextBoxColumn { Name="hitboxHeight", Visible=false },
            new DataGridViewTextBoxColumn { Name="scale", Visible=false },
            new DataGridViewTextBoxColumn { Name="sprite", HeaderText="画像パス",   FillWeight=160, ReadOnly=true },
            new DataGridViewButtonColumn  { Name="btnHitbox", HeaderText="Hitbox", Text="🎯", UseColumnTextForButtonValue=true, FillWeight=35 },
            new DataGridViewButtonColumn  { Name="btnSize",   HeaderText="Size",   Text="📏", UseColumnTextForButtonValue=true, FillWeight=35 },
            new DataGridViewButtonColumn  { Name="btnSprite", HeaderText="📁選択",  Text="📁", UseColumnTextForButtonValue=true, FillWeight=35 },
            new DataGridViewButtonColumn  { Name="btnDel",    HeaderText="🗑削除",   Text="🗑", UseColumnTextForButtonValue=true, FillWeight=30 },
        });

        dgv.CellContentClick += (s, e) => HandleGridButton(dgv, e);
        dgv.SelectionChanged += (s, e) => { UpdatePreview(dgv); UpdateBehaviorParamsPanel(dgv, isEnemy: true); };
        dgv.CurrentCellDirtyStateChanged += (s, e) => { if (dgv.IsCurrentCellDirty) dgv.CommitEdit(DataGridViewDataErrorContexts.Commit); };
        dgv.CellValueChanged += (s, e) => { if (dgv.Columns[e.ColumnIndex].Name == "type_enum") UpdateBehaviorParamsPanel(dgv, isEnemy: true); };
        return dgv;
    }

    private DataGridView CreateGimmickGrid()
    {
        var dgv = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            Font = new Font("Meiryo UI", 9),
            RowHeadersWidth = 25
        };

        dgv.Columns.AddRange(new DataGridViewColumn[]
        {
            new DataGridViewTextBoxColumn { Name="id",       HeaderText="ID",      FillWeight=80 },
            new DataGridViewTextBoxColumn { Name="name",     HeaderText="名前",    FillWeight=120 },
            new DataGridViewComboBoxColumn
            {
                Name="type_enum", HeaderText="タイプ", FillWeight=120,
                DataSource = GimmickTypes.Select(t => t.desc).ToArray(),
                DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton
            },
            new DataGridViewTextBoxColumn { Name="hitboxOffsetX", Visible=false },
            new DataGridViewTextBoxColumn { Name="hitboxOffsetY", Visible=false },
            new DataGridViewTextBoxColumn { Name="hitboxWidth", Visible=false },
            new DataGridViewTextBoxColumn { Name="hitboxHeight", Visible=false },
            new DataGridViewTextBoxColumn { Name="sprite", HeaderText="画像パス", FillWeight=200, ReadOnly=true },
            new DataGridViewButtonColumn  { Name="btnHitbox", HeaderText="Hitbox", Text="🎯", UseColumnTextForButtonValue=true, FillWeight=35 },
            new DataGridViewButtonColumn  { Name="btnSprite", HeaderText="📁選択", Text="📁", UseColumnTextForButtonValue=true, FillWeight=35 },
            new DataGridViewButtonColumn  { Name="btnDel",    HeaderText="🗑削除",  Text="🗑", UseColumnTextForButtonValue=true, FillWeight=30 },
        });

        dgv.CellContentClick += (s, e) => HandleGridButton(dgv, e);
        dgv.SelectionChanged += (s, e) => { UpdatePreview(dgv); UpdateBehaviorParamsPanel(dgv, isEnemy: false); };
        dgv.CurrentCellDirtyStateChanged += (s, e) => { if (dgv.IsCurrentCellDirty) dgv.CommitEdit(DataGridViewDataErrorContexts.Commit); };
        dgv.CellValueChanged += (s, e) => { if (dgv.Columns[e.ColumnIndex].Name == "type_enum") UpdateBehaviorParamsPanel(dgv, isEnemy: false); };
        dgv.DataError += (s, e) => HandleDataError(dgv, e);
        return dgv;
    }

    private DataGridView CreateItemGrid()
    {
        var dgv = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            Font = new Font("Meiryo UI", 9),
            RowHeadersWidth = 25
        };

        dgv.Columns.AddRange(new DataGridViewColumn[]
        {
            new DataGridViewTextBoxColumn { Name="id",           HeaderText="ID",        FillWeight=80 },
            new DataGridViewTextBoxColumn { Name="name",         HeaderText="名前",      FillWeight=100 },
            new DataGridViewComboBoxColumn
            {
                Name="type_enum", HeaderText="タイプ", FillWeight=100,
                DataSource = ItemTypes.Select(t => t.desc).ToArray(),
                DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton
            },
            new DataGridViewTextBoxColumn { Name="hitboxOffsetX", Visible=false },
            new DataGridViewTextBoxColumn { Name="hitboxOffsetY", Visible=false },
            new DataGridViewTextBoxColumn { Name="hitboxWidth", Visible=false },
            new DataGridViewTextBoxColumn { Name="hitboxHeight", Visible=false },
            new DataGridViewTextBoxColumn { Name="sprite",        HeaderText="画像パス",  FillWeight=180, ReadOnly=true },
            new DataGridViewTextBoxColumn { Name="grant_ability", HeaderText="付与能力",  FillWeight=100 },
            new DataGridViewButtonColumn  { Name="btnHitbox", HeaderText="Hitbox", Text="🎯", UseColumnTextForButtonValue=true, FillWeight=35 },
            new DataGridViewButtonColumn  { Name="btnSprite", HeaderText="📁選択", Text="📁", UseColumnTextForButtonValue=true, FillWeight=35 },
            new DataGridViewButtonColumn  { Name="btnDel",    HeaderText="🗑削除",  Text="🗑", UseColumnTextForButtonValue=true, FillWeight=30 },
        });

        dgv.CellContentClick += (s, e) => HandleGridButton(dgv, e);
        dgv.SelectionChanged += (s, e) => UpdatePreview(dgv);
        dgv.CurrentCellDirtyStateChanged += (s, e) => { if (dgv.IsCurrentCellDirty) dgv.CommitEdit(DataGridViewDataErrorContexts.Commit); };
        dgv.DataError += (s, e) => HandleDataError(dgv, e);
        return dgv;
    }

    // ===== 行操作 =====
    private void HandleGridButton(DataGridView dgv, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0) return;
        string colName = dgv.Columns[e.ColumnIndex].Name;

        if (colName == "btnSprite")
        {
            using var ofd = new OpenFileDialog { Filter = "画像ファイル|*.png;*.jpg;*.bmp|すべて|*.*", Title = "スプライト画像を選択" };
            if (ofd.ShowDialog() != DialogResult.OK) return;

            // imgフォルダへコピー
            string imgDir = Path.Combine(projectRoot, "img");
            Directory.CreateDirectory(imgDir);
            string destPath = Path.Combine(imgDir, Path.GetFileName(ofd.FileName));
            if (!destPath.Equals(ofd.FileName, StringComparison.OrdinalIgnoreCase))
                File.Copy(ofd.FileName, destPath, overwrite: true);

            // 相対パスとして保存（ゲームはimg/xxxx.pngを使用）
            string relPath = "img/" + Path.GetFileName(ofd.FileName);
            dgv.Rows[e.RowIndex].Cells["sprite"].Value = relPath;
            ShowPreview(destPath);
            lblPreviewPath.Text = relPath;
        }
        else if (colName == "btnHitbox")
        {
            string spritePath = dgv.Rows[e.RowIndex].Cells["sprite"].Value?.ToString() ?? "";
            string fullPath = string.IsNullOrEmpty(spritePath) ? "" : Path.Combine(projectRoot, spritePath);
            int ox = IntCell(dgv.Rows[e.RowIndex], "hitboxOffsetX", 0);
            int oy = IntCell(dgv.Rows[e.RowIndex], "hitboxOffsetY", 0);
            int w = IntCell(dgv.Rows[e.RowIndex], "hitboxWidth", 32);
            int h = IntCell(dgv.Rows[e.RowIndex], "hitboxHeight", 32);

            using var form = new HitboxEditorForm(fullPath, ox, oy, w, h);
            if (form.ShowDialog() == DialogResult.OK)
            {
                dgv.Rows[e.RowIndex].Cells["hitboxOffsetX"].Value = form.HitboxOffsetX;
                dgv.Rows[e.RowIndex].Cells["hitboxOffsetY"].Value = form.HitboxOffsetY;
                dgv.Rows[e.RowIndex].Cells["hitboxWidth"].Value = form.HitboxWidth;
                dgv.Rows[e.RowIndex].Cells["hitboxHeight"].Value = form.HitboxHeight;
            }
        }
        else if (colName == "btnSize")
        {
            string spritePath = dgv.Rows[e.RowIndex].Cells["sprite"].Value?.ToString() ?? "";
            if (string.IsNullOrEmpty(spritePath)) { MessageBox.Show("先に画像を選択してください。", "サイズ調整", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            string fullPath = Path.Combine(projectRoot, spritePath);
            float curScale = FloatCell(dgv.Rows[e.RowIndex], "scale", 1.0f);

            using var form = new SizeEditorForm(fullPath, curScale);
            if (form.ShowDialog() == DialogResult.OK)
                dgv.Rows[e.RowIndex].Cells["scale"].Value = form.ResultScale;
        }
        else if (colName == "btnDel")
        {
            if (MessageBox.Show("この行を削除しますか？", "確認", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                dgv.Rows.RemoveAt(e.RowIndex);
        }
    }

    private void AddRow(DataGridView dgv, object[] values)
    {
        dgv.Rows.Add(values);
        dgv.Rows[dgv.Rows.Count - 1].Selected = true;
        dgv.FirstDisplayedScrollingRowIndex = dgv.Rows.Count - 1;
    }

    private void HandleDataError(DataGridView dgv, DataGridViewDataErrorEventArgs e)
    {
        string colName = dgv.Columns[e.ColumnIndex].Name;
        object val = dgv.Rows[e.RowIndex].Cells[e.ColumnIndex].Value;
        string items = "";
        int dsCount = 0;
        if (dgv.Columns[e.ColumnIndex] is DataGridViewComboBoxColumn cb)
        {
            var ds = cb.DataSource as string[];
            if (ds != null) {
                items = string.Join(", ", ds);
                dsCount = ds.Length;
            }
        }
        string msg = $@"[DataGridViewComboBoxCell Error]
Form: {this.Name}
DataGridView: {dgv.Name}
Row: {e.RowIndex}
Col: {e.ColumnIndex}
Column.Name: {colName}
Column.HeaderText: {dgv.Columns[e.ColumnIndex].HeaderText}
Cell.Value: '{val}'
Cell.FormattedValue: '{dgv.Rows[e.RowIndex].Cells[e.ColumnIndex].FormattedValue}'
Cell.ValueType: {dgv.Rows[e.RowIndex].Cells[e.ColumnIndex].ValueType}
ComboBox.Items: [{items}]
ComboBox.DataSourceCount: {dsCount}
ValueMember: {(dgv.Columns[e.ColumnIndex] as DataGridViewComboBoxColumn)?.ValueMember}
DisplayMember: {(dgv.Columns[e.ColumnIndex] as DataGridViewComboBoxColumn)?.DisplayMember}
Exception.Message: {e.Exception.Message}
Exception.StackTrace: {e.Exception.StackTrace}";

        System.IO.File.AppendAllText(Path.Combine(AppPaths.LogsDir, "error_detail.log"), msg + "\n\n");
        throw new Exception(msg, e.Exception);
    }

    // dgv内の既存id("prefix"+数字)と衝突しない最小の連番idを生成する
    private static string MakeUniqueSequentialId(DataGridView dgv, string prefix)
    {
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (DataGridViewRow row in dgv.Rows)
            if (!row.IsNewRow) existing.Add(row.Cells["id"].Value?.ToString() ?? "");

        int n = 1;
        string candidate;
        do { candidate = $"{prefix}{n}"; n++; } while (existing.Contains(candidate));
        return candidate;
    }

    private object[] GetDefaultEnemyRow()
    {
        string newId = MakeUniqueSequentialId(dgvEnemies, "enemy_");
        return new object[] { newId, "新敵", EnemyTypes[0].desc, 3, 32, 32, "", "画像選択", "削除" };
    }

    private object[] GetDefaultGimmickRow()
    {
        string newId = MakeUniqueSequentialId(dgvGimmicks, "gimmick_");
        return new object[] { newId, "新しいギミック", GimmickTypes[0].desc, "", "📁", "🗑" };
    }

    private object[] GetDefaultItemRow()
    {
        string newId = MakeUniqueSequentialId(dgvItems, "item_");
        return new object[] { newId, "新しいアイテム", ItemTypes[0].desc, "", "", "📁", "🗑" };
    }

    // ===== プレビュー更新 =====
    private void UpdatePreview(DataGridView dgv)
    {
        if (dgv.SelectedRows.Count == 0) return;
        var row = dgv.SelectedRows[0];
        if (!dgv.Columns.Contains("sprite")) return;
        string sp = row.Cells["sprite"].Value?.ToString() ?? "";
        if (string.IsNullOrEmpty(sp)) { pbPreview.Image = null; lblPreviewPath.Text = "(画像なし)"; return; }
        string fullPath = Path.Combine(projectRoot, sp.Replace('/', '\\'));
        ShowPreview(fullPath);
        lblPreviewPath.Text = sp;
    }

    private void ShowPreview(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                // ファイルロックを避けるためにStreamで読む
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                pbPreview.Image = Image.FromStream(fs);
            }
            else
            {
                pbPreview.Image = null;
                lblPreviewPath.Text = "⚠ ファイルが見つかりません";
            }
        }
        catch { pbPreview.Image = null; }
    }

    // ==== Feature: Configurable Behavior Parameters (M1) ====

    private EnemyDef GetOrCreateEnemyParams(DataGridViewRow row)
    {
        if (!_enemyParams.TryGetValue(row, out var def)) { def = new EnemyDef(); _enemyParams[row] = def; }
        return def;
    }

    private GimmickDef GetOrCreateGimmickParams(DataGridViewRow row)
    {
        if (!_gimmickParams.TryGetValue(row, out var def)) { def = new GimmickDef(); _gimmickParams[row] = def; }
        return def;
    }

    // 選択中の行のtype_enumを、そのグリッドのコンボボックス列から読み取る（ReadEnemies/ReadGimmicksと同じ判定方式）
    private static int GetSelectedTypeEnum(DataGridViewRow row)
    {
        if (row.Cells["type_enum"] is DataGridViewComboBoxCell combo)
        {
            var vals = (string[]?)combo.DataSource;
            if (vals != null)
            {
                int idx = Array.IndexOf(vals, combo.Value?.ToString() ?? "");
                if (idx >= 0) return idx;
            }
        }
        return 0;
    }

    // 選択中の敵/ギミック行のtype_enumに応じて、挙動パラメータの入力欄を動的に組み立てる。
    // 該当タイプに調整可能なパラメータが無い場合は非表示にし、従来のタイプ一覧説明を見せる。
    private void UpdateBehaviorParamsPanel(DataGridView dgv, bool isEnemy)
    {
        if (dgv.SelectedRows.Count == 0) { pnlBehaviorParams.Visible = false; rtbTypeHint.Visible = true; return; }
        var row = dgv.SelectedRows[0];
        int typeEnum = GetSelectedTypeEnum(row);
        var fieldMap = isEnemy ? EnemyParamFields : GimmickParamFields;

        if (!fieldMap.TryGetValue(typeEnum, out var fields) || fields.Length == 0)
        {
            pnlBehaviorParams.Visible = false;
            rtbTypeHint.Visible = true;
            lblTypeHintTitle.Text = "📋 タイプ説明";
            return;
        }

        object paramsObj = isEnemy ? GetOrCreateEnemyParams(row) : GetOrCreateGimmickParams(row);
        _isUpdatingBehaviorPanel = true;
        pnlBehaviorParams.SuspendLayout();
        pnlBehaviorParams.Controls.Clear();

        int y = 4;
        foreach (var (field, label, decimals) in fields)
        {
            var prop = paramsObj.GetType().GetProperty(field)!;
            var lbl = new Label { Text = label, Location = new Point(4, y + 3), Size = new Size(230, 15), Font = new Font("Meiryo UI", 7.5f) };
            var nud = new NumericUpDown
            {
                Location = new Point(4, y + 18),
                Size = new Size(140, 22),
                DecimalPlaces = decimals,
                Increment = decimals > 0 ? (decimal)Math.Pow(10, -decimals) : 1m,
                Minimum = -100000m,
                Maximum = 100000m,
                Value = (decimal)Convert.ToSingle(prop.GetValue(paramsObj))
            };
            nud.ValueChanged += (s, e) =>
            {
                if (_isUpdatingBehaviorPanel) return;
                if (prop.PropertyType == typeof(int)) prop.SetValue(paramsObj, (int)nud.Value);
                else prop.SetValue(paramsObj, (float)nud.Value);
            };
            pnlBehaviorParams.Controls.Add(lbl);
            pnlBehaviorParams.Controls.Add(nud);
            y += 42;
        }

        pnlBehaviorParams.ResumeLayout();
        _isUpdatingBehaviorPanel = false;

        rtbTypeHint.Visible = false;
        pnlBehaviorParams.Visible = true;
        lblTypeHintTitle.Text = "⚙ 挙動パラメータ";
    }

    private void UpdateTypeHint()
    {
        rtbTypeHint.Clear();
        switch (tabControl.SelectedIndex)
        {
            case 0:
                rtbTypeHint.AppendText("【敵タイプ一覧】\n\n");
                foreach (var (type, desc, detail) in EnemyTypes)
                {
                    rtbTypeHint.SelectionFont = new Font("Meiryo UI", 8, FontStyle.Bold);
                    rtbTypeHint.SelectionColor = Color.DarkBlue;
                    rtbTypeHint.AppendText(desc + "\n");
                    rtbTypeHint.SelectionFont = new Font("Meiryo UI", 7.5f);
                    rtbTypeHint.SelectionColor = Color.DarkGray;
                    rtbTypeHint.AppendText(detail + "\n\n");
                }
                break;
            case 1:
                rtbTypeHint.AppendText("【ギミックタイプ一覧】\n\n");
                foreach (var (type, desc) in GimmickTypes)
                {
                    rtbTypeHint.SelectionFont = new Font("Meiryo UI", 8, FontStyle.Bold);
                    rtbTypeHint.SelectionColor = Color.DarkGreen;
                    rtbTypeHint.AppendText(desc + "\n");
                }
                rtbTypeHint.SelectionFont = new Font("Meiryo UI", 7.5f);
                rtbTypeHint.SelectionColor = Color.DarkGray;
                rtbTypeHint.AppendText(
                    "\n【param欄の使い方（マップ上に配置後、プロパティグリッドで編集）】\n" +
                    "・色ロック足場: \"1\"=赤 / \"2\"=緑 / \"3\"=青（プレイヤーがTキーで切替する色フィルタと一致した時だけ実体化）\n" +
                    "・明暗ロック足場: \"dark\"(既定)=画面が暗い時だけ実体化 / \"bright\"=明るい時だけ実体化\n" +
                    "（プレイヤー側の操作: T=色フィルタ切替, Z=ズーム, X=暗転, C=明転, M=SEミュート）");
                break;
            case 2:
                rtbTypeHint.AppendText("【アイテムタイプ一覧】\n\n");
                foreach (var (type, desc) in ItemTypes)
                {
                    rtbTypeHint.SelectionFont = new Font("Meiryo UI", 8, FontStyle.Bold);
                    rtbTypeHint.SelectionColor = Color.DarkRed;
                    rtbTypeHint.AppendText(desc + "\n");
                }
                rtbTypeHint.AppendText("\n【grant_ability フィールド】\n");
                rtbTypeHint.AppendText("取得時にプレイヤーに付与する能力名を入力。\n例: canDoubleJump, canDash, canShootFireball\n");
                break;
            case 3:
                rtbTypeHint.AppendText("【コモンイベントとは】\n\n");
                rtbTypeHint.SelectionFont = new Font("Meiryo UI", 7.5f);
                rtbTypeHint.SelectionColor = Color.DarkGray;
                rtbTypeHint.AppendText("複数のトリガーから共通で呼び出せる一連のアクションです。\n\nトリガー編集画面のアクションで「CallCommonEvent」を選び、ここで定義したIDを指定すると呼び出せます。\n\n例: 「SE再生 → メッセージ表示 → アイテム付与」をまとめて1つのコモンイベントにし、複数の宝箱トリガーから使い回す。");
                break;
        }
    }

    // ===== データ読み込み =====
    private void LoadData()
    {
        dgvEnemies.Rows.Clear();
        _enemyParams.Clear();
        foreach (var e in assets.Enemies)
        {
            string typeLabel = EnemyTypes.FirstOrDefault(t => t.type == e.type_enum).desc;
            if (string.IsNullOrEmpty(typeLabel) || !EnemyTypes.Any(t => t.desc == typeLabel))
            {
                System.IO.File.AppendAllText(Path.Combine(AppPaths.LogsDir, "warning_log.txt"), $"[WARNING] AssetManagerForm: Enemy ID '{e.id}' has invalid type_enum '{e.type_enum}'. Auto-converted to default.\n");
                typeLabel = EnemyTypes[0].desc;
            }
            dgvEnemies.Rows.Add(e.id, e.name, typeLabel, e.hp, e.width, e.height, e.hitboxOffsetX, e.hitboxOffsetY, e.hitboxWidth, e.hitboxHeight, e.scale, e.sprite, "🎯", "📏", "📁", "🗑");
            // Feature: Configurable Behavior Parameters (M1) — 行オブジェクトに紐づけて挙動パラメータ本体を保持する
            _enemyParams[dgvEnemies.Rows[dgvEnemies.Rows.Count - 1]] = e;
        }

        dgvGimmicks.Rows.Clear();
        _gimmickParams.Clear();
        foreach (var g in assets.Gimmicks)
        {
            string typeLabel = GimmickTypes.FirstOrDefault(t => t.type == g.type_enum).desc;
            if (string.IsNullOrEmpty(typeLabel) || !GimmickTypes.Any(t => t.desc == typeLabel))
            {
                System.IO.File.AppendAllText(Path.Combine(AppPaths.LogsDir, "warning_log.txt"), $"[WARNING] AssetManagerForm: Gimmick ID '{g.id}' has invalid type_enum '{g.type_enum}'. Auto-converted to default.\n");
                typeLabel = GimmickTypes[0].desc;
            }
            dgvGimmicks.Rows.Add(g.id, g.name, typeLabel, g.hitboxOffsetX, g.hitboxOffsetY, g.hitboxWidth, g.hitboxHeight, g.sprite, "🎯", "📁", "🗑");
            _gimmickParams[dgvGimmicks.Rows[dgvGimmicks.Rows.Count - 1]] = g;
        }

        dgvItems.Rows.Clear();
        foreach (var i in assets.Items)
        {
            string typeLabel = ItemTypes.FirstOrDefault(t => t.type == i.type_enum).desc;
            if (string.IsNullOrEmpty(typeLabel) || !ItemTypes.Any(t => t.desc == typeLabel)) 
            {
                System.IO.File.AppendAllText(Path.Combine(AppPaths.LogsDir, "warning_log.txt"), $"[WARNING] AssetManagerForm: Item ID '{i.id}' has invalid type_enum '{i.type_enum}'. Auto-converted to default.\n");
                typeLabel = ItemTypes[0].desc;
            }
            dgvItems.Rows.Add(i.id, i.name, typeLabel, i.hitboxOffsetX, i.hitboxOffsetY, i.hitboxWidth, i.hitboxHeight, i.sprite, i.grant_ability, "🎯", "📁", "🗑");
        }
    }

    // ===== 保存 =====
    private void BtnSave_Click(object? sender, EventArgs e)
    {
        assets.Enemies = ReadEnemies();
        assets.Gimmicks = ReadGimmicks();
        assets.Items = ReadItems();
        assets.CommonEvents = _commonEvents;
        assets.SaveToFolder(assetsPath);
        MessageBox.Show("アセット定義を保存しました！\n\n※画像はimgフォルダへコピー済みです。\nゲームを再ビルドすると新しいスプライトが反映されます。",
            "保存完了", MessageBoxButtons.OK, MessageBoxIcon.Information);
        DialogResult = DialogResult.OK;
        Close();
    }

    private List<EnemyDef> ReadEnemies()
    {
        var list = new List<EnemyDef>();
        foreach (DataGridViewRow row in dgvEnemies.Rows)
        {
            if (row.IsNewRow) continue;
            string? id = row.Cells["id"].Value?.ToString();
            if (string.IsNullOrWhiteSpace(id)) continue;

            // type_enum: コンボボックスの選択インデックスから取得
            int typeIdx = 0;
            string typeStr = row.Cells["type_enum"].Value?.ToString() ?? "";
            for (int i = 0; i < EnemyTypes.Length; i++)
                if (EnemyTypes[i].desc.Split('=')[1].Trim().Split(' ')[0] == typeStr ||
                    typeStr == i.ToString() || EnemyTypes[i].desc.Contains(typeStr)) { typeIdx = i; break; }
            // ComboBoxのインデックスで取得試み
            if (row.Cells["type_enum"] is DataGridViewComboBoxCell combo)
            {
                var vals = (string[]?)combo.DataSource;
                if (vals != null)
                {
                    int foundIdx = Array.IndexOf(vals, combo.Value?.ToString() ?? "");
                    if (foundIdx >= 0) typeIdx = foundIdx;
                }
            }

            // Feature: Configurable Behavior Parameters (M1) — 行に紐づく保持済みEnemyDef（挙動パラメータ・SE等を保持）を
            // 土台にし、グリッドで編集可能な基本フィールドだけをそこへ反映する（新規に作り直すと挙動パラメータが失われるため）
            var def = GetOrCreateEnemyParams(row);
            def.id = id;
            def.name = row.Cells["name"].Value?.ToString() ?? "";
            def.type_enum = typeIdx;
            def.hp = IntCell(row, "hp", 3);
            def.width = IntCell(row, "width", 32);
            def.height = IntCell(row, "height", 32);
            def.hitboxOffsetX = IntCell(row, "hitboxOffsetX", 0);
            def.hitboxOffsetY = IntCell(row, "hitboxOffsetY", 0);
            def.hitboxWidth = IntCell(row, "hitboxWidth", 32);
            def.hitboxHeight = IntCell(row, "hitboxHeight", 32);
            def.scale = FloatCell(row, "scale", 1.0f);
            def.sprite = row.Cells["sprite"].Value?.ToString() ?? "";
            list.Add(def);
        }
        return list;
    }

    private List<GimmickDef> ReadGimmicks()
    {
        var list = new List<GimmickDef>();
        foreach (DataGridViewRow row in dgvGimmicks.Rows)
        {
            if (row.IsNewRow) continue;
            string? id = row.Cells["id"].Value?.ToString();
            if (string.IsNullOrWhiteSpace(id)) continue;

            int typeIdx = 0;
            if (row.Cells["type_enum"] is DataGridViewComboBoxCell combo)
            {
                var vals = (string[]?)combo.DataSource;
                if (vals != null)
                {
                    int foundIdx = Array.IndexOf(vals, combo.Value?.ToString() ?? "");
                    if (foundIdx >= 0) typeIdx = foundIdx;
                }
            }

            // Feature: Configurable Behavior Parameters (M1) — 行に紐づく保持済みGimmickDef（挙動パラメータ・SE等を保持）を
            // 土台にし、グリッドで編集可能な基本フィールドだけをそこへ反映する
            var def = GetOrCreateGimmickParams(row);
            def.id = id;
            def.name = row.Cells["name"].Value?.ToString() ?? "";
            def.type_enum = typeIdx;
            def.hitboxOffsetX = IntCell(row, "hitboxOffsetX", 0);
            def.hitboxOffsetY = IntCell(row, "hitboxOffsetY", 0);
            def.hitboxWidth = IntCell(row, "hitboxWidth", 32);
            def.hitboxHeight = IntCell(row, "hitboxHeight", 32);
            def.sprite = row.Cells["sprite"].Value?.ToString() ?? "";
            list.Add(def);
        }
        return list;
    }

    private List<ItemDef> ReadItems()
    {
        var list = new List<ItemDef>();
        foreach (DataGridViewRow row in dgvItems.Rows)
        {
            if (row.IsNewRow) continue;
            string? id = row.Cells["id"].Value?.ToString();
            if (string.IsNullOrWhiteSpace(id)) continue;

            int typeIdx = 0;
            if (row.Cells["type_enum"] is DataGridViewComboBoxCell combo)
            {
                var vals = (string[]?)combo.DataSource;
                if (vals != null)
                {
                    int foundIdx = Array.IndexOf(vals, combo.Value?.ToString() ?? "");
                    if (foundIdx >= 0) typeIdx = foundIdx;
                }
            }

            list.Add(new ItemDef
            {
                id = id,
                name = row.Cells["name"].Value?.ToString() ?? "",
                type_enum = typeIdx,
                hitboxOffsetX = IntCell(row, "hitboxOffsetX", 0),
                hitboxOffsetY = IntCell(row, "hitboxOffsetY", 0),
                hitboxWidth = IntCell(row, "hitboxWidth", 32),
                hitboxHeight = IntCell(row, "hitboxHeight", 32),
                sprite = row.Cells["sprite"].Value?.ToString() ?? "",
                grant_ability = row.Cells["grant_ability"].Value?.ToString() ?? ""
            });
        }
        return list;
    }

    private static int IntCell(DataGridViewRow row, string col, int def = 0)
        => int.TryParse(row.Cells[col].Value?.ToString(), out var v) ? v : def;

    private static float FloatCell(DataGridViewRow row, string col, float def = 0f)
        => float.TryParse(row.Cells[col].Value?.ToString(), out var v) ? v : def;

    // ===== コモンイベント =====
    private void RefreshCommonEventsList()
    {
        lstCommonEvents.Items.Clear();
        foreach (var ce in _commonEvents)
            lstCommonEvents.Items.Add($"{ce.id} : {ce.name} ({ce.actions.Count}件)");
    }

    private void AddCommonEvent()
    {
        int n = _commonEvents.Count + 1;
        string newId = $"common_event_{n}";
        while (_commonEvents.Any(c => c.id == newId)) { n++; newId = $"common_event_{n}"; }

        var form = new CommonEventEditorForm(new CommonEventDef { id = newId, name = "新しいコモンイベント" }, assets, GetStageFileNames());
        if (form.ShowDialog() == DialogResult.OK)
        {
            _commonEvents.Add(form.ResultEvent);
            RefreshCommonEventsList();
        }
    }

    private void EditSelectedCommonEvent()
    {
        int idx = lstCommonEvents.SelectedIndex;
        if (idx < 0 || idx >= _commonEvents.Count) { MessageBox.Show("編集するコモンイベントを選択してください。"); return; }

        var form = new CommonEventEditorForm(_commonEvents[idx], assets, GetStageFileNames());
        if (form.ShowDialog() == DialogResult.OK)
        {
            _commonEvents[idx] = form.ResultEvent;
            RefreshCommonEventsList();
            lstCommonEvents.SelectedIndex = idx;
        }
    }

    private void DeleteSelectedCommonEvent()
    {
        int idx = lstCommonEvents.SelectedIndex;
        if (idx < 0 || idx >= _commonEvents.Count) { MessageBox.Show("削除するコモンイベントを選択してください。"); return; }
        if (MessageBox.Show($"コモンイベント「{_commonEvents[idx].id}」を削除しますか？", "確認", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        _commonEvents.RemoveAt(idx);
        RefreshCommonEventsList();
    }

    // ===== 検索フィルタ (MZ風 ID/名前検索) =====
    private void ApplySearchFilter()
    {
        string q = txtSearch.Text.Trim();
        switch (tabControl.SelectedIndex)
        {
            case 0: FilterGrid(dgvEnemies, q); break;
            case 1: FilterGrid(dgvGimmicks, q); break;
            case 2: FilterGrid(dgvItems, q); break;
            case 3: FilterCommonEventsList(q); break;
        }
    }

    private static void FilterGrid(DataGridView dgv, string query)
    {
        foreach (DataGridViewRow row in dgv.Rows)
        {
            if (row.IsNewRow) continue;
            if (string.IsNullOrEmpty(query)) { row.Visible = true; continue; }
            string id = row.Cells["id"].Value?.ToString() ?? "";
            string name = dgv.Columns.Contains("name") ? row.Cells["name"].Value?.ToString() ?? "" : "";
            row.Visible = id.Contains(query, StringComparison.OrdinalIgnoreCase) || name.Contains(query, StringComparison.OrdinalIgnoreCase);
        }
    }

    private void FilterCommonEventsList(string query)
    {
        lstCommonEvents.Items.Clear();
        var filtered = string.IsNullOrEmpty(query)
            ? _commonEvents
            : _commonEvents.Where(c => c.id.Contains(query, StringComparison.OrdinalIgnoreCase) || c.name.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var ce in filtered)
            lstCommonEvents.Items.Add($"{ce.id} : {ce.name} ({ce.actions.Count}件)");
    }

    // ===== 選択行の複製 (MZ風: 似た定義を素早く量産) =====
    private void BtnDuplicate_Click(object? sender, EventArgs e)
    {
        switch (tabControl.SelectedIndex)
        {
            case 0: DuplicateEnemyRow(); break;
            case 1: DuplicateGimmickRow(); break;
            case 2: DuplicateItemRow(); break;
            case 3: DuplicateCommonEvent(); break;
        }
    }

    private static string MakeUniqueId(DataGridView dgv, string baseId)
    {
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (DataGridViewRow row in dgv.Rows)
            if (!row.IsNewRow) existing.Add(row.Cells["id"].Value?.ToString() ?? "");

        string candidate = baseId + "_copy";
        int n = 2;
        while (existing.Contains(candidate)) { candidate = $"{baseId}_copy{n}"; n++; }
        return candidate;
    }

    private void DuplicateEnemyRow()
    {
        if (dgvEnemies.SelectedRows.Count == 0) { MessageBox.Show("複製する行を選択してください。"); return; }
        var r = dgvEnemies.SelectedRows[0];
        string newId = MakeUniqueId(dgvEnemies, r.Cells["id"].Value?.ToString() ?? "enemy");
        AddRow(dgvEnemies, new object[]
        {
            newId, (r.Cells["name"].Value?.ToString() ?? "") + "のコピー", r.Cells["type_enum"].Value ?? EnemyTypes[0].desc,
            r.Cells["hp"].Value ?? 3, r.Cells["width"].Value ?? 32, r.Cells["height"].Value ?? 32,
            r.Cells["hitboxOffsetX"].Value ?? 0, r.Cells["hitboxOffsetY"].Value ?? 0,
            r.Cells["hitboxWidth"].Value ?? 32, r.Cells["hitboxHeight"].Value ?? 32,
            r.Cells["scale"].Value ?? 1.0f,
            r.Cells["sprite"].Value ?? "", "🎯", "📏", "📁", "🗑"
        });
        // Feature: Configurable Behavior Parameters (M1) — 挙動パラメータも複製する
        var srcDef = GetOrCreateEnemyParams(r);
        _enemyParams[dgvEnemies.Rows[dgvEnemies.Rows.Count - 1]] = JsonConvert.DeserializeObject<EnemyDef>(JsonConvert.SerializeObject(srcDef))!;
    }

    private void DuplicateGimmickRow()
    {
        if (dgvGimmicks.SelectedRows.Count == 0) { MessageBox.Show("複製する行を選択してください。"); return; }
        var r = dgvGimmicks.SelectedRows[0];
        string newId = MakeUniqueId(dgvGimmicks, r.Cells["id"].Value?.ToString() ?? "gimmick");
        AddRow(dgvGimmicks, new object[]
        {
            newId, (r.Cells["name"].Value?.ToString() ?? "") + "のコピー", r.Cells["type_enum"].Value ?? GimmickTypes[0].desc,
            r.Cells["hitboxOffsetX"].Value ?? 0, r.Cells["hitboxOffsetY"].Value ?? 0,
            r.Cells["hitboxWidth"].Value ?? 32, r.Cells["hitboxHeight"].Value ?? 32,
            r.Cells["sprite"].Value ?? "", "🎯", "📁", "🗑"
        });
        // Feature: Configurable Behavior Parameters (M1) — 挙動パラメータも複製する
        var srcDef = GetOrCreateGimmickParams(r);
        _gimmickParams[dgvGimmicks.Rows[dgvGimmicks.Rows.Count - 1]] = JsonConvert.DeserializeObject<GimmickDef>(JsonConvert.SerializeObject(srcDef))!;
    }

    private void DuplicateItemRow()
    {
        if (dgvItems.SelectedRows.Count == 0) { MessageBox.Show("複製する行を選択してください。"); return; }
        var r = dgvItems.SelectedRows[0];
        string newId = MakeUniqueId(dgvItems, r.Cells["id"].Value?.ToString() ?? "item");
        AddRow(dgvItems, new object[]
        {
            newId, (r.Cells["name"].Value?.ToString() ?? "") + "のコピー", r.Cells["type_enum"].Value ?? ItemTypes[0].desc,
            r.Cells["hitboxOffsetX"].Value ?? 0, r.Cells["hitboxOffsetY"].Value ?? 0,
            r.Cells["hitboxWidth"].Value ?? 32, r.Cells["hitboxHeight"].Value ?? 32,
            r.Cells["sprite"].Value ?? "", r.Cells["grant_ability"].Value ?? "", "🎯", "📁", "🗑"
        });
    }

    private void DuplicateCommonEvent()
    {
        int idx = lstCommonEvents.SelectedIndex;
        if (idx < 0 || idx >= _commonEvents.Count) { MessageBox.Show("複製するコモンイベントを選択してください。"); return; }
        var src = _commonEvents[idx];
        var existing = new HashSet<string>(_commonEvents.Select(c => c.id), StringComparer.OrdinalIgnoreCase);
        string newId = src.id + "_copy";
        int n = 2;
        while (existing.Contains(newId)) { newId = $"{src.id}_copy{n}"; n++; }

        _commonEvents.Add(new CommonEventDef
        {
            id = newId,
            name = src.name + "のコピー",
            actions = src.actions.Select(a => new EventActionEntry { action = a.action, param1 = a.param1, param2 = a.param2, delay = a.delay }).ToList()
        });
        RefreshCommonEventsList();
    }
}
