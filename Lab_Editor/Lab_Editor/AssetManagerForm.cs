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

    // Feature: UI改善（友人フィードバック対応） — タブを廃止し、1つの縦積みビュー＋
    // 上部のタグボタン（複数選択可）で絞り込む構成にした。各セクション自体は
    // 既存のグリッド/リストをそのまま流用する（データの読み書きロジックは無変更）。
    private Panel sectionEnemy = null!, sectionGimmick = null!, sectionItem = null!, sectionCommonEvent = null!;
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
    // Feature: Composite Multi-Part Objects (Parts-M7) — アイテムも敵/ギミックと同様に、
    // グリッドに出さない付加情報（parts等）を行に紐づけて保持する
    private readonly Dictionary<DataGridViewRow, ItemDef> _itemParams = new();
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
        (20, "20 = カスタムスクリプト (Custom Script)", "「🧩 挙動スクリプトを編集」ボタンから、ブロックを組み立てて挙動を自作します。"),
    };
    // Feature: UI改善（提案書 CUT-2/AM-1）— 敵タイプ(EnemyTypes)には既にあった「plain-languageの説明文(detail)」を
    // ギミック/アイテムのタイプにも同様に用意し、type_enumという数字だけでなく実際の挙動が分かるようにする。
    private static readonly (int type, string desc, string detail)[] GimmickTypes =
    {
        (0, "0 = ポータル", "同じparam値を持つポータルを2つ配置すると対になり、片方に触れるともう片方の位置へワープします。"),
        (1, "1 = 回転橋(自動)", "常に一定速度で回転し続ける橋です。橋が水平に近い向きの間だけプレイヤーが乗れます。"),
        (2, "2 = 回転橋(手動)", "初期状態は縦向き(通行不可)。プレイヤーがRキー+ドラッグで回転させ、水平にすると渡れるようになります。"),
        (3, "3 = 破壊ブロック", "左クリックで壊せるブロックです。壊すとその場所を通行できるようになります。"),
        (4, "4 = 落下リフト", "プレイヤーが乗ると少しずつ沈み込んでいく足場です。"),
        (5, "5 = 反射鏡", "触れた弾やプレイヤーを跳ね返します。"),
        (6, "6 = 重量スイッチ", "同じparam値のスケールボックスがtriggerWidthThreshold以上に広がると起動し、同じparam値のゲート扉を開きます。"),
        (7, "7 = スケールボックス", "Sキー+ドラッグで横幅を伸縮できる箱です。重量スイッチを起動するために使います。"),
        (8, "8 = ゲート扉", "対応する重量スイッチが起動している間だけ通行できる扉です。param未指定なら最初に見つかったスイッチと連動します。"),
        (9, "9 = 棘床", "触れるとダメージを受ける固定の棘です。"),
        (10, "10 = スケール地面", "Sキー+ドラッグで大きさを変えられる足場です。"),
        (11, "11 = ちくわブロック", "乗ってからstandDelayFrames経つと崩れ落ち、respawnDelayFrames後に元へ戻ります。Rキー巻き戻しで復旧できます。"),
        (12, "12 = 時間フィールド", "範囲内の時間の流れを操作する装置です（一時停止/反・一時停止の演出に使用）。"),
        (13, "13 = 食らいギミック", "触れると即座にダメージを与える障害物です。"),
        (14, "14 = 動く足場 (Moving Platform)", "配置した位置を中心に、travelDistanceで指定した距離だけ上下に周期的に往復する足場です。"),
        (15, "15 = 岩 (Pushable Rock)", "プレイヤーが横から押して動かせる岩です。"),
        (16, "16 = 早送りゲート (Fastforward Gate)", "触れている間、ゲーム内時間の進みを早めます（Fキーの効果と同種の演出）。"),
        (17, "17 = コマ送りリフト (Framestep Lift)", "Spaceキーで一時停止した状態から→キーを押すたびに1段ずつ移動するリフトです。"),
        (18, "18 = 明暗ゾーン (Brightness Zone)", "範囲内に入っている間、画面の明るさをbrightLevel/darkLevelの間で変化させる演出ゾーンです。"),
        (19, "19 = 色調ゾーン (Color Zone)", "範囲内に入っている間、画面の色調をtintR/G/Bへ変化させる演出ゾーンです。"),
        (20, "20 = ズームレンズ (Zoom Lens)", "範囲内に入っている間、カメラをzoomLevelの倍率までズームさせる演出ゾーンです。"),
        (21, "21 = スローフィールド (Slowmo Field)", "範囲内に入っている間、スローモーション+暗転効果をかけるゾーンです。"),
        (22, "22 = 色ロック足場 (Color Lock Platform)", "paramで指定した色(Tキーの色フィルタ)とプレイヤーの現在の色が一致している間だけ実体化する足場です。"),
        (23, "23 = 明暗ロック足場 (Brightness Lock Platform)", "paramで指定した明暗状態(Xキー)とプレイヤーの現在の状態が一致している間だけ実体化する足場です。"),
        (24, "24 = カスタムスクリプト (Custom Script)", "「🧩 挙動スクリプトを編集」ボタンから、ブロックを組み立てて挙動を自作します。"),
    };
    private static readonly (int type, string desc, string detail)[] ItemTypes =
    {
        (0, "0 = なし", "特に効果を持たない、装飾・プレースホルダー用のアイテムです。"),
        (1, "1 = コイン", "取得するとスコア/所持コイン数が増えます。"),
        (2, "2 = 回復アイテム", "取得するとプレイヤーのHPを回復します。"),
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
        Size = new Size(1160, 720);
        MinimumSize = new Size(900, 560);
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Meiryo UI", 9);

        // ===== 上部: 検索ボックス + タグ絞り込みボタン =====
        // Feature: UI改善 — 検索欄と、複数選択可能な種別タグボタン（チェック状態のボタン=Appearance.Button）を
        // 1つのDock=Topパネルにまとめる。タグはCheckBoxで実装し、押した状態(Checked)がタグON。
        // Feature: UI改善 — 検索/タグ行もウィンドウ幅次第で折り返しうるため、固定Heightではなく
        // AutoSizeにして必要な行数ぶん高さが伸びるようにする（下段のグリッド等がはみ出しを防ぐ）。
        var pnlTop = new Panel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };

        var pnlToolbar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(4) };
        var lblSearch = new Label { Text = "🔍", AutoSize = true, Margin = new Padding(2, 6, 0, 0) };
        txtSearch = new TextBox { Width = 240, Margin = new Padding(4, 3, 12, 0), PlaceholderText = "ID・名前で検索..." };
        txtSearch.TextChanged += (s, e) => ApplySearchFilter();
        btnDuplicate = new Button { Text = "⧉ 選択行を複製", AutoSize = true, Padding = new Padding(6, 4, 6, 4), Margin = new Padding(4, 1, 0, 0) };
        btnDuplicate.Click += BtnDuplicate_Click;
        pnlToolbar.Controls.AddRange(new Control[] { lblSearch, txtSearch, btnDuplicate });

        var pnlTags = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(4, 0, 4, 4) };
        var chkTagEnemy = new CheckBox { Text = "👾 敵", Appearance = Appearance.Button, Checked = true, AutoSize = true, Padding = new Padding(8, 4, 8, 4), Margin = new Padding(0, 0, 4, 0) };
        var chkTagGimmick = new CheckBox { Text = "🔧 ギミック", Appearance = Appearance.Button, Checked = true, AutoSize = true, Padding = new Padding(8, 4, 8, 4), Margin = new Padding(0, 0, 4, 0) };
        var chkTagItem = new CheckBox { Text = "💎 アイテム", Appearance = Appearance.Button, Checked = true, AutoSize = true, Padding = new Padding(8, 4, 8, 4), Margin = new Padding(0, 0, 4, 0) };
        var chkTagCommonEvent = new CheckBox { Text = "🔔 コモンイベント", Appearance = Appearance.Button, Checked = true, AutoSize = true, Padding = new Padding(8, 4, 8, 4), Margin = new Padding(0, 0, 4, 0) };
        chkTagEnemy.CheckedChanged += (s, e) => sectionEnemy.Visible = chkTagEnemy.Checked;
        chkTagGimmick.CheckedChanged += (s, e) => sectionGimmick.Visible = chkTagGimmick.Checked;
        chkTagItem.CheckedChanged += (s, e) => sectionItem.Visible = chkTagItem.Checked;
        chkTagCommonEvent.CheckedChanged += (s, e) => sectionCommonEvent.Visible = chkTagCommonEvent.Checked;
        pnlTags.Controls.AddRange(new Control[] { chkTagEnemy, chkTagGimmick, chkTagItem, chkTagCommonEvent });

        pnlTop.Controls.Add(pnlTags);
        pnlTop.Controls.Add(pnlToolbar);

        // ===== 右サイドパネル =====
        var pnlRight = new Panel { Dock = DockStyle.Right, Width = 260, BorderStyle = BorderStyle.FixedSingle };
        var flowRight = new FlowLayoutPanel { Dock = DockStyle.Top, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(5) };

        var lblPrev = new Label { Text = "🖼 スプライトプレビュー（ホイールでズーム）", AutoSize = true, Font = new Font("Meiryo UI", 9, FontStyle.Bold), Margin = new Padding(0, 0, 0, 4) };
        var pnlPreviewHost = new Panel { Width = 238, Height = 180, BorderStyle = BorderStyle.FixedSingle, BackColor = Color.Black, AutoScroll = true, Margin = new Padding(0, 0, 0, 2) };
        pbPreview = new PictureBox { SizeMode = PictureBoxSizeMode.Normal, BackColor = Color.Black };
        pnlPreviewHost.Controls.Add(pbPreview);
        pnlPreviewHost.MouseWheel += PnlPreviewHost_MouseWheel;
        lblPreviewPath = new Label { Width = 238, Height = 40, Font = new Font("Meiryo UI", 7), ForeColor = Color.Gray, Text = "(選択なし)", Margin = new Padding(0, 0, 0, 4) };

        lblTypeHintTitle = new Label { Text = "📋 タイプ説明", Width = 238, Font = new Font("Meiryo UI", 9, FontStyle.Bold), Margin = new Padding(0, 4, 0, 2) };
        rtbTypeHint = new RichTextBox
        {
            Width = 238,
            Height = 330,
            ReadOnly = true,
            ScrollBars = RichTextBoxScrollBars.Vertical,
            Font = new Font("Meiryo UI", 8),
            BackColor = Color.FromArgb(250, 250, 250),
            BorderStyle = BorderStyle.None
        };

        // Feature: Configurable Behavior Parameters (M1) — 選択中の敵/ギミック行のtype_enumに応じて
        // 挙動パラメータの入力欄を動的に切り替えるパネル。rtbTypeHintと同じ場所（表示/非表示切替）。
        pnlBehaviorParams = new Panel
        {
            Width = 238,
            Height = 330,
            AutoScroll = true,
            Visible = false
        };

        flowRight.Controls.Add(lblPrev);
        flowRight.Controls.Add(pnlPreviewHost);
        flowRight.Controls.Add(lblPreviewPath);
        flowRight.Controls.Add(lblTypeHintTitle);
        flowRight.Controls.Add(rtbTypeHint);
        flowRight.Controls.Add(pnlBehaviorParams);
        pnlRight.Controls.Add(flowRight);

        // ===== 下部ボタン（右詰め/左詰めFlowLayoutPanelで自動配置） =====
        // Feature: UI改善 — 左詰め側はボタン数が多くWrapContentsで折り返しうるため、
        // 固定Heightだと折り返した行（保存/キャンセルボタンを含む）がパネルの外にはみ出して
        // 見えなくなっていた。pnlBottom自体をAutoSizeにして必要な行数ぶん高さが伸びるようにする。
        var pnlBottom = new Panel { Dock = DockStyle.Bottom, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
        var flowBottomRight = new FlowLayoutPanel { Dock = DockStyle.Top, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8, 6, 8, 2), AutoSize = true };
        btnSave = new Button { Text = "💾 保存して閉じる", AutoSize = true, Padding = new Padding(10, 6, 10, 6), BackColor = Color.FromArgb(40, 167, 69), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Font = new Font("Meiryo UI", 10, FontStyle.Bold) };
        btnSave.Click += BtnSave_Click;
        btnClose = new Button { Text = "キャンセル", AutoSize = true, Padding = new Padding(10, 6, 10, 6) };
        btnClose.Click += (s, e) => Close();
        flowBottomRight.Controls.Add(btnSave);
        flowBottomRight.Controls.Add(btnClose);

        var flowBottomLeft = new FlowLayoutPanel { Dock = DockStyle.Top, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(8, 2, 8, 6), AutoSize = true, WrapContents = true };
        var btnAddEnemy = new Button { Text = "＋ 敵追加", AutoSize = true, Padding = new Padding(6, 5, 6, 5) };
        btnAddEnemy.Click += (s, e) => AddRow(dgvEnemies, GetDefaultEnemyRow());
        var btnAddGimmick = new Button { Text = "＋ ギミック追加", AutoSize = true, Padding = new Padding(6, 5, 6, 5) };
        btnAddGimmick.Click += (s, e) => AddRow(dgvGimmicks, GetDefaultGimmickRow());
        var btnAddItem = new Button { Text = "＋ アイテム追加", AutoSize = true, Padding = new Padding(6, 5, 6, 5) };
        btnAddItem.Click += (s, e) => AddRow(dgvItems, GetDefaultItemRow());
        var btnAddCommonEvent = new Button { Text = "＋ コモンイベント追加", AutoSize = true, Padding = new Padding(6, 5, 6, 5) };
        btnAddCommonEvent.Click += (s, e) => AddCommonEvent();
        // Feature: Composite Multi-Part Objects (Parts-M7) — 敵/ギミック/アイテム共通。type_enumに関係なく使える
        var btnPartsEditor = new Button { Text = "🧩 パーツを編集", AutoSize = true, Padding = new Padding(6, 5, 6, 5) };
        btnPartsEditor.Click += (s, e) => BtnPartsEditor_Click();
        // Feature: Puzzle-like Behavior Scripting (M4) — ブロックエディタを開く
        var btnBehaviorScript = new Button { Text = "🧩 挙動スクリプトを編集", AutoSize = true, Padding = new Padding(6, 5, 6, 5) };
        btnBehaviorScript.Click += (s, e) => BtnBehaviorScript_Click();
        // Feature: UI改善（提案書 CUT-2/AM-1）— type_enumを数字の羅列から選ぶのではなく、
        // アイコン・名前・説明が並んだカード一覧から選べるようにする
        var btnTypeCardPicker = new Button { Text = "🔍 タイプをカードから選ぶ", AutoSize = true, Padding = new Padding(6, 5, 6, 5) };
        btnTypeCardPicker.Click += (s, e) => BtnTypeCardPicker_Click();
        flowBottomLeft.Controls.AddRange(new Control[] { btnAddEnemy, btnAddGimmick, btnAddItem, btnAddCommonEvent, btnPartsEditor, btnBehaviorScript, btnTypeCardPicker });

        pnlBottom.Controls.Add(flowBottomLeft);
        pnlBottom.Controls.Add(flowBottomRight);

        // ===== 中央: 敵/ギミック/アイテム/コモンイベントを1つの縦スクロールビューに集約 =====
        // Feature: UI改善 — 以前はFlowLayoutPanel(AutoSize)の直接の子にDock=Fillのグリッドを置いており、
        // 親のサイズが子から逆算される一方で子は親のサイズから逆算しようとする循環に陥り、グリッドが
        // 極端に狭く潰れていた。ここでは各セクションを「高さ固定・幅は可変(Dock=Top)」のPanelにし、
        // その内部でグリッドをDock=Fillにする（親の高さが確定しているためDock=Fillが安全に働く）。
        // これにより、ウィンドウ幅に合わせてグリッドの横幅も正しく追従するようになる。
        var pnlSections = new Panel { Dock = DockStyle.Fill, AutoScroll = true };

        dgvEnemies = CreateEnemyGrid();
        sectionEnemy = BuildSection("👾 敵 (Enemies)", dgvEnemies, 300);

        dgvGimmicks = CreateGimmickGrid();
        sectionGimmick = BuildSection("🔧 ギミック (Gimmicks)", dgvGimmicks, 300);

        dgvItems = CreateItemGrid();
        sectionItem = BuildSection("💎 アイテム (Items)", dgvItems, 220);

        // ===== コモンイベント (RPGツクールMZ風: 複数トリガーから呼び出せる共通処理) =====
        lstCommonEvents = new ListBox { Font = new Font("Meiryo UI", 9) };
        lstCommonEvents.DoubleClick += (s, e) => EditSelectedCommonEvent();
        lstCommonEvents.SelectedIndexChanged += (s, e) => { if (lstCommonEvents.SelectedIndex >= 0) ClearOtherSelections(AssetKind.CommonEvent); };
        var pnlCommonEventContent = new Panel();
        lstCommonEvents.Dock = DockStyle.Fill;
        var flowCeButtons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 34, Padding = new Padding(2) };
        var btnCeEdit = new Button { Text = "✎ 編集", AutoSize = true, Padding = new Padding(6, 4, 6, 4) };
        btnCeEdit.Click += (s, e) => EditSelectedCommonEvent();
        var btnCeDelete = new Button { Text = "🗑 削除", AutoSize = true, Padding = new Padding(6, 4, 6, 4) };
        btnCeDelete.Click += (s, e) => DeleteSelectedCommonEvent();
        flowCeButtons.Controls.AddRange(new Control[] { btnCeEdit, btnCeDelete });
        pnlCommonEventContent.Controls.Add(lstCommonEvents);
        pnlCommonEventContent.Controls.Add(flowCeButtons);
        sectionCommonEvent = BuildSection("🔔 コモンイベント", pnlCommonEventContent, 240);

        // Dock=Topは「先に追加したものほど端(上)に近づく」ため、この順番がそのまま表示順になる
        pnlSections.Controls.Add(sectionEnemy);
        pnlSections.Controls.Add(sectionGimmick);
        pnlSections.Controls.Add(sectionItem);
        pnlSections.Controls.Add(sectionCommonEvent);

        Controls.Add(pnlSections);
        Controls.Add(pnlRight);
        Controls.Add(pnlBottom);
        Controls.Add(pnlTop);
        RefreshCommonEventsList();
        UpdateTypeHint();
    }

    // 種別セクション1つぶんの見出し+中身をまとめる。
    // Feature: UI改善 — 高さ固定・幅可変(Dock=Top)のPanelにすることで、中身(グリッド等)をDock=Fillにしても
    // 安全（親の高さが確定しているため）。ウィンドウ幅に応じて中身の横幅も正しく追従する。
    private Panel BuildSection(string title, Control content, int contentHeight)
    {
        const int titleHeight = 24;
        const int topMargin = 4, bottomMargin = 14;
        var section = new Panel
        {
            Dock = DockStyle.Top,
            Height = titleHeight + contentHeight + topMargin + bottomMargin,
            Padding = new Padding(4, topMargin, 4, bottomMargin),
        };
        var lbl = new Label { Dock = DockStyle.Top, Height = titleHeight, Text = title, Font = new Font(Font, FontStyle.Bold), TextAlign = ContentAlignment.BottomLeft };
        content.Dock = DockStyle.Fill;
        content.Margin = new Padding(0);
        // Dock=Fillの子を先に追加し、その後にDock=Topの見出しラベルを追加する（安全なDock順）
        section.Controls.Add(content);
        section.Controls.Add(lbl);
        return section;
    }

    private void PnlPreviewHost_MouseWheel(object? sender, MouseEventArgs e)
    {
        if (pbPreview.Image == null) return;
        float factor = e.Delta > 0 ? 1.15f : 1f / 1.15f;
        _previewZoom = Math.Clamp(_previewZoom * factor, 0.1f, 8f);
        ApplyPreviewZoom();
    }

    private float _previewZoom = 1f;

    private void ApplyPreviewZoom()
    {
        if (pbPreview.Image == null) return;
        pbPreview.Size = new Size((int)(pbPreview.Image.Width * _previewZoom), (int)(pbPreview.Image.Height * _previewZoom));
    }

    private DataGridView CreateEnemyGrid()
    {
        // Feature: UI改善 — Dock=FillはAutoSizeのFlowLayoutPanel(BuildSection内)の直接の子には設定しない。
        // 親がAutoSizeで子のサイズから逆算する一方、Dock=Fillは親のサイズから子を逆算しようとするため
        // サイズ計算が循環してしまい、グリッドが極端に狭く潰れる不具合の原因になっていた。
        // ここでは固定サイズ（BuildSectionでWidth/Heightを明示指定）にする。
        var dgv = new DataGridView
        {
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            Font = new Font("Meiryo UI", 9),
            RowHeadersWidth = 25
        };

        dgv.Columns.AddRange(new DataGridViewColumn[]
        {
            new DataGridViewTextBoxColumn { Name="icon",     HeaderText="",         FillWeight=30, ReadOnly=true },
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
        dgv.SelectionChanged += (s, e) => { if (dgv.SelectedRows.Count > 0) ClearOtherSelections(AssetKind.Enemy); UpdatePreview(dgv); UpdateBehaviorParamsPanel(dgv, isEnemy: true); UpdateTypeHint(); };
        dgv.CurrentCellDirtyStateChanged += (s, e) => { if (dgv.IsCurrentCellDirty) dgv.CommitEdit(DataGridViewDataErrorContexts.Commit); };
        dgv.CellValueChanged += (s, e) => { if (dgv.Columns[e.ColumnIndex].Name == "type_enum") { UpdateBehaviorParamsPanel(dgv, isEnemy: true); RefreshIconCell(dgv, e.RowIndex, isEnemy: true, isGimmick: false); } };
        return dgv;
    }

    private DataGridView CreateGimmickGrid()
    {
        var dgv = new DataGridView
        {
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            Font = new Font("Meiryo UI", 9),
            RowHeadersWidth = 25
        };

        dgv.Columns.AddRange(new DataGridViewColumn[]
        {
            new DataGridViewTextBoxColumn { Name="icon",     HeaderText="",        FillWeight=30, ReadOnly=true },
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
        dgv.SelectionChanged += (s, e) => { if (dgv.SelectedRows.Count > 0) ClearOtherSelections(AssetKind.Gimmick); UpdatePreview(dgv); UpdateBehaviorParamsPanel(dgv, isEnemy: false); UpdateTypeHint(); };
        dgv.CurrentCellDirtyStateChanged += (s, e) => { if (dgv.IsCurrentCellDirty) dgv.CommitEdit(DataGridViewDataErrorContexts.Commit); };
        dgv.CellValueChanged += (s, e) => { if (dgv.Columns[e.ColumnIndex].Name == "type_enum") { UpdateBehaviorParamsPanel(dgv, isEnemy: false); RefreshIconCell(dgv, e.RowIndex, isEnemy: false, isGimmick: true); } };
        dgv.DataError += (s, e) => HandleDataError(dgv, e);
        return dgv;
    }

    private DataGridView CreateItemGrid()
    {
        var dgv = new DataGridView
        {
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            Font = new Font("Meiryo UI", 9),
            RowHeadersWidth = 25
        };

        dgv.Columns.AddRange(new DataGridViewColumn[]
        {
            new DataGridViewTextBoxColumn { Name="icon",         HeaderText="",          FillWeight=30, ReadOnly=true },
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
        dgv.SelectionChanged += (s, e) => { if (dgv.SelectedRows.Count > 0) ClearOtherSelections(AssetKind.Item); UpdatePreview(dgv); UpdateTypeHint(); };
        dgv.CurrentCellDirtyStateChanged += (s, e) => { if (dgv.IsCurrentCellDirty) dgv.CommitEdit(DataGridViewDataErrorContexts.Commit); };
        dgv.CellValueChanged += (s, e) => { if (dgv.Columns[e.ColumnIndex].Name == "type_enum") RefreshIconCell(dgv, e.RowIndex, isEnemy: false, isGimmick: false); };
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

            // imgフォルダへコピー（同名で内容の異なるファイルは連番を付けて別名保存する。Parts-M7）
            string relPath = ImageImportHelper.CopyIntoImgFolder(projectRoot, ofd.FileName);
            dgv.Rows[e.RowIndex].Cells["sprite"].Value = relPath;
            ShowPreview(Path.Combine(projectRoot, relPath.Replace('/', '\\')));
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
        return new object[] { AssetIcons.ForEnemy(EnemyTypes[0].type), newId, "新敵", EnemyTypes[0].desc, 3, 32, 32, "", "画像選択", "削除" };
    }

    private object[] GetDefaultGimmickRow()
    {
        string newId = MakeUniqueSequentialId(dgvGimmicks, "gimmick_");
        return new object[] { AssetIcons.ForGimmick(GimmickTypes[0].type), newId, "新しいギミック", GimmickTypes[0].desc, "", "📁", "🗑" };
    }

    private object[] GetDefaultItemRow()
    {
        string newId = MakeUniqueSequentialId(dgvItems, "item_");
        return new object[] { AssetIcons.ForItem(ItemTypes[0].type), newId, "新しいアイテム", ItemTypes[0].desc, "", "", "📁", "🗑" };
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
                // Feature: UI改善 — プレビューズーム。小さいドット絵は見やすいよう自動的に拡大した初期値にする
                _previewZoom = 1f;
                if (pbPreview.Image.Width > 0 && pbPreview.Image.Width < 100)
                    _previewZoom = Math.Min(8f, 160f / pbPreview.Image.Width);
                ApplyPreviewZoom();
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

    // Feature: Composite Multi-Part Objects (Parts-M7)
    private ItemDef GetOrCreateItemParams(DataGridViewRow row)
    {
        if (!_itemParams.TryGetValue(row, out var def)) { def = new ItemDef(); _itemParams[row] = def; }
        return def;
    }

    // Feature: UI改善 — タブ廃止に伴い、「現在操作対象とみなす種別」をタブのインデックスではなく
    // 「どのグリッド/リストに選択中の行があるか」から判定する（同時に選択できるのは1つのみになるよう
    // 各グリッドのSelectionChangedで他を解除する。ClearOtherSelections参照）。
    private enum AssetKind { None = -1, Enemy = 0, Gimmick = 1, Item = 2, CommonEvent = 3 }

    private AssetKind GetActiveKind()
    {
        if (dgvEnemies.SelectedRows.Count > 0) return AssetKind.Enemy;
        if (dgvGimmicks.SelectedRows.Count > 0) return AssetKind.Gimmick;
        if (dgvItems.SelectedRows.Count > 0) return AssetKind.Item;
        if (lstCommonEvents.SelectedIndex >= 0) return AssetKind.CommonEvent;
        return AssetKind.None;
    }

    // exceptを除く全ての種別の選択を解除する（1つの種別だけが選択状態を持つようにする）
    private void ClearOtherSelections(AssetKind except)
    {
        if (except != AssetKind.Enemy) dgvEnemies.ClearSelection();
        if (except != AssetKind.Gimmick) dgvGimmicks.ClearSelection();
        if (except != AssetKind.Item) dgvItems.ClearSelection();
        if (except != AssetKind.CommonEvent) lstCommonEvents.ClearSelected();
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

    // Feature: UI改善 — グリッドのicon列を、行のtype_enum(選択中の値)に応じたAssetIconsの絵文字で更新する
    private void RefreshIconCell(DataGridView dgv, int rowIndex, bool isEnemy, bool isGimmick)
    {
        if (rowIndex < 0 || rowIndex >= dgv.Rows.Count) return;
        int typeEnum = GetSelectedTypeEnum(dgv.Rows[rowIndex]);
        string icon = isEnemy ? AssetIcons.ForEnemy(typeEnum) : isGimmick ? AssetIcons.ForGimmick(typeEnum) : AssetIcons.ForItem(typeEnum);
        dgv.Rows[rowIndex].Cells["icon"].Value = icon;
    }

    // Feature: Puzzle-like Behavior Scripting (M6)
    // 現在選択中のタブ・行がtype_enum=20(敵)/24(ギミック)＝カスタムスクリプトであれば
    // ブロックエディタを開き、OKで閉じたらそのEnemyDef/GimmickDef.scriptへ書き戻す。
    private const int CustomScriptEnemyType = 20;
    private const int CustomScriptGimmickType = 24;

    private void BtnBehaviorScript_Click()
    {
        var kind = GetActiveKind();
        if (kind == AssetKind.Enemy)
        {
            var row = dgvEnemies.SelectedRows[0];
            if (GetSelectedTypeEnum(row) != CustomScriptEnemyType)
            {
                MessageBox.Show("挙動スクリプトは、タイプが「20 = カスタムスクリプト」の敵にのみ設定できます。\nタイプ列で切り替えてからお試しください。", "対象外", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            var def = GetOrCreateEnemyParams(row);
            using var form = new BehaviorScriptEditorForm($"敵: {row.Cells["id"].Value}", def.script);
            if (form.ShowDialog() == DialogResult.OK) def.script = form.ResultScript;
        }
        else if (kind == AssetKind.Gimmick)
        {
            var row = dgvGimmicks.SelectedRows[0];
            if (GetSelectedTypeEnum(row) != CustomScriptGimmickType)
            {
                MessageBox.Show("挙動スクリプトは、タイプが「24 = カスタムスクリプト」のギミックにのみ設定できます。\nタイプ列で切り替えてからお試しください。", "対象外", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            var def = GetOrCreateGimmickParams(row);
            using var form = new BehaviorScriptEditorForm($"ギミック: {row.Cells["id"].Value}", def.script);
            if (form.ShowDialog() == DialogResult.OK) def.script = form.ResultScript;
        }
        else
        {
            MessageBox.Show("挙動スクリプトは「敵」または「ギミック」を選択してからお使いください。", "対象外", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    // Feature: Composite Multi-Part Objects (Parts-M7)
    // 敵/ギミック/アイテムいずれのタブでも、タイプ(type_enum)に関係なく使える
    // （パーツは親のタイプとは独立して機能するため、挙動スクリプトのようなタイプ制限は設けない）
    private void BtnPartsEditor_Click()
    {
        var kind = GetActiveKind();
        if (kind == AssetKind.Enemy)
        {
            var row = dgvEnemies.SelectedRows[0];
            var def = GetOrCreateEnemyParams(row);
            string sprite = row.Cells["sprite"].Value?.ToString() ?? "";
            using var form = new PartsEditorForm($"敵: {row.Cells["id"].Value}", def.parts, projectRoot, sprite);
            if (form.ShowDialog() == DialogResult.OK) def.parts = form.ResultParts;
        }
        else if (kind == AssetKind.Gimmick)
        {
            var row = dgvGimmicks.SelectedRows[0];
            var def = GetOrCreateGimmickParams(row);
            string sprite = row.Cells["sprite"].Value?.ToString() ?? "";
            using var form = new PartsEditorForm($"ギミック: {row.Cells["id"].Value}", def.parts, projectRoot, sprite);
            if (form.ShowDialog() == DialogResult.OK) def.parts = form.ResultParts;
        }
        else if (kind == AssetKind.Item)
        {
            var row = dgvItems.SelectedRows[0];
            var def = GetOrCreateItemParams(row);
            string sprite = row.Cells["sprite"].Value?.ToString() ?? "";
            using var form = new PartsEditorForm($"アイテム: {row.Cells["id"].Value}", def.parts, projectRoot, sprite);
            if (form.ShowDialog() == DialogResult.OK) def.parts = form.ResultParts;
        }
        else
        {
            MessageBox.Show("パーツ編集は「敵」「ギミック」「アイテム」のいずれかを選択してからお使いください。", "対象外", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    // Feature: UI改善（提案書 CUT-2/AM-1）— コンボボックスの数字入り文字列から選ぶのではなく、
    // アイコン・名前・plain-languageの説明が並んだカード一覧をクリックしてタイプを選べるようにする。
    // 選択結果は既存のtype_enumコンボボックス列(desc文字列で管理)へそのまま書き戻すため、
    // ステージ側の読み込み/保存ロジックには一切手を入れていない。
    private void BtnTypeCardPicker_Click()
    {
        var kind = GetActiveKind();
        DataGridView? dgv = kind switch { AssetKind.Enemy => dgvEnemies, AssetKind.Gimmick => dgvGimmicks, AssetKind.Item => dgvItems, _ => null };
        if (dgv == null || dgv.SelectedRows.Count == 0)
        {
            MessageBox.Show("タイプ選択は「敵」「ギミック」「アイテム」のいずれかの行を選択してからお使いください。", "対象外", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var row = dgv.SelectedRows[0];
        if (row.Cells["type_enum"] is not DataGridViewComboBoxCell combo) return;

        var options = kind switch
        {
            AssetKind.Enemy => EnemyTypes.Select(t => (t.type, t.desc, t.detail, icon: AssetIcons.ForEnemy(t.type))).ToList(),
            AssetKind.Gimmick => GimmickTypes.Select(t => (t.type, t.desc, t.detail, icon: AssetIcons.ForGimmick(t.type))).ToList(),
            _ => ItemTypes.Select(t => (t.type, t.desc, t.detail, icon: AssetIcons.ForItem(t.type))).ToList(),
        };
        int current = GetSelectedTypeEnum(row);
        using var picker = new TypeCardPickerForm(options, current);
        if (picker.ShowDialog() != DialogResult.OK || picker.SelectedType < 0) return;

        var vals = (string[]?)combo.DataSource;
        if (vals == null || picker.SelectedType >= vals.Length) return;
        combo.Value = vals[picker.SelectedType];
        UpdateBehaviorParamsPanel(dgv, isEnemy: kind == AssetKind.Enemy);
        RefreshIconCell(dgv, row.Index, isEnemy: kind == AssetKind.Enemy, isGimmick: kind == AssetKind.Gimmick);
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
        switch (GetActiveKind())
        {
            case AssetKind.Enemy:
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
            case AssetKind.Gimmick:
                rtbTypeHint.AppendText("【ギミックタイプ一覧】\n\n");
                foreach (var (type, desc, detailG) in GimmickTypes)
                {
                    rtbTypeHint.SelectionFont = new Font("Meiryo UI", 8, FontStyle.Bold);
                    rtbTypeHint.SelectionColor = Color.DarkGreen;
                    rtbTypeHint.AppendText(desc + "\n");
                    rtbTypeHint.SelectionFont = new Font("Meiryo UI", 7.5f);
                    rtbTypeHint.SelectionColor = Color.DarkGray;
                    rtbTypeHint.AppendText(detailG + "\n\n");
                }
                rtbTypeHint.SelectionFont = new Font("Meiryo UI", 7.5f);
                rtbTypeHint.SelectionColor = Color.DarkGray;
                rtbTypeHint.AppendText(
                    "\n【param欄の使い方（マップ上に配置後、プロパティグリッドで編集）】\n" +
                    "・色ロック足場: \"1\"=赤 / \"2\"=緑 / \"3\"=青（プレイヤーがTキーで切替する色フィルタと一致した時だけ実体化）\n" +
                    "・明暗ロック足場: \"dark\"(既定)=画面が暗い時だけ実体化 / \"bright\"=明るい時だけ実体化\n" +
                    "（プレイヤー側の操作: T=色フィルタ切替, Z=ズーム, X=暗転, C=明転, M=SEミュート）");
                break;
            case AssetKind.Item:
                rtbTypeHint.AppendText("【アイテムタイプ一覧】\n\n");
                foreach (var (type, desc, detailI) in ItemTypes)
                {
                    rtbTypeHint.SelectionFont = new Font("Meiryo UI", 8, FontStyle.Bold);
                    rtbTypeHint.SelectionColor = Color.DarkRed;
                    rtbTypeHint.AppendText(desc + "\n");
                    rtbTypeHint.SelectionFont = new Font("Meiryo UI", 7.5f);
                    rtbTypeHint.SelectionColor = Color.DarkGray;
                    rtbTypeHint.AppendText(detailI + "\n\n");
                }
                rtbTypeHint.AppendText("\n【grant_ability フィールド】\n");
                rtbTypeHint.AppendText("取得時にプレイヤーに付与する能力名を入力。\n例: canDoubleJump, canDash, canShootFireball\n");
                break;
            case AssetKind.CommonEvent:
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
            dgvEnemies.Rows.Add(AssetIcons.ForEnemy(e.type_enum), e.id, e.name, typeLabel, e.hp, e.width, e.height, e.hitboxOffsetX, e.hitboxOffsetY, e.hitboxWidth, e.hitboxHeight, e.scale, e.sprite, "🎯", "📏", "📁", "🗑");
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
            dgvGimmicks.Rows.Add(AssetIcons.ForGimmick(g.type_enum), g.id, g.name, typeLabel, g.hitboxOffsetX, g.hitboxOffsetY, g.hitboxWidth, g.hitboxHeight, g.sprite, "🎯", "📁", "🗑");
            _gimmickParams[dgvGimmicks.Rows[dgvGimmicks.Rows.Count - 1]] = g;
        }

        dgvItems.Rows.Clear();
        _itemParams.Clear();
        foreach (var i in assets.Items)
        {
            string typeLabel = ItemTypes.FirstOrDefault(t => t.type == i.type_enum).desc;
            if (string.IsNullOrEmpty(typeLabel) || !ItemTypes.Any(t => t.desc == typeLabel))
            {
                System.IO.File.AppendAllText(Path.Combine(AppPaths.LogsDir, "warning_log.txt"), $"[WARNING] AssetManagerForm: Item ID '{i.id}' has invalid type_enum '{i.type_enum}'. Auto-converted to default.\n");
                typeLabel = ItemTypes[0].desc;
            }
            dgvItems.Rows.Add(AssetIcons.ForItem(i.type_enum), i.id, i.name, typeLabel, i.hitboxOffsetX, i.hitboxOffsetY, i.hitboxWidth, i.hitboxHeight, i.sprite, i.grant_ability, "🎯", "📁", "🗑");
            // Feature: Composite Multi-Part Objects (Parts-M7) — 行オブジェクトに紐づけて非グリッド項目(parts等)を保持する
            _itemParams[dgvItems.Rows[dgvItems.Rows.Count - 1]] = i;
        }
    }

    // Feature: UI改善（提案書 CUT-3）— ID重複や画像未設定のまま保存すると、ステージ側からの参照が
    // 意図せず別の定義を指してしまったり、ゲーム内で表示されないままになったりして気づきにくい。
    private static List<string> ValidateAssets(List<EnemyDef> enemies, List<GimmickDef> gimmicks, List<ItemDef> items, List<CommonEventDef> commonEvents)
    {
        var warnings = new List<string>();

        void CheckDup<T>(List<T> list, Func<T, string> idSel, string kind)
        {
            var dups = list.GroupBy(idSel).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            if (dups.Count > 0)
                warnings.Add($"{kind}のIDが重複しています: {string.Join(", ", dups)}（後に定義した方だけが有効になります）。");
        }
        CheckDup(enemies, e => e.id, "敵");
        CheckDup(gimmicks, g => g.id, "ギミック");
        CheckDup(items, i => i.id, "アイテム");
        CheckDup(commonEvents, c => c.id, "コモンイベント");

        var noSpriteEnemies = enemies.Where(e => string.IsNullOrWhiteSpace(e.sprite)).Select(e => e.id).ToList();
        if (noSpriteEnemies.Count > 0)
            warnings.Add($"画像が未設定の敵があります (ID: {string.Join(", ", noSpriteEnemies)})。ゲーム内で表示されません。");

        return warnings;
    }

    // ===== 保存 =====
    private void BtnSave_Click(object? sender, EventArgs e)
    {
        var enemies = ReadEnemies();
        var gimmicks = ReadGimmicks();
        var items = ReadItems();

        var warnings = ValidateAssets(enemies, gimmicks, items, _commonEvents);
        if (warnings.Count > 0)
        {
            string msg = "保存前に確認してください:\n\n" +
                string.Join("\n", warnings.Take(8)) +
                (warnings.Count > 8 ? $"\n…他{warnings.Count - 8}件" : "") +
                "\n\nこのまま保存しますか？";
            if (MessageBox.Show(msg, "確認", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        }

        assets.Enemies = enemies;
        assets.Gimmicks = gimmicks;
        assets.Items = items;
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

            // Feature: Composite Multi-Part Objects (Parts-M7) — 行に紐づく保持済みItemDef（parts等）を
            // 土台にし、グリッドで編集可能な基本フィールドだけをそこへ反映する（新規に作り直すとpartsが失われるため）
            var def = GetOrCreateItemParams(row);
            def.id = id;
            def.name = row.Cells["name"].Value?.ToString() ?? "";
            def.type_enum = typeIdx;
            def.hitboxOffsetX = IntCell(row, "hitboxOffsetX", 0);
            def.hitboxOffsetY = IntCell(row, "hitboxOffsetY", 0);
            def.hitboxWidth = IntCell(row, "hitboxWidth", 32);
            def.hitboxHeight = IntCell(row, "hitboxHeight", 32);
            def.sprite = row.Cells["sprite"].Value?.ToString() ?? "";
            def.grant_ability = row.Cells["grant_ability"].Value?.ToString() ?? "";
            list.Add(def);
        }
        return list;
    }

    private static int IntCell(DataGridViewRow row, string col, int def = 0)
        => int.TryParse(row.Cells[col].Value?.ToString(), out var v) ? v : def;

    private static float FloatCell(DataGridViewRow row, string col, float def = 0f)
        => float.TryParse(row.Cells[col].Value?.ToString(), out var v) ? v : def;

    // ===== コモンイベント =====
    // Feature: UI改善（提案書 AM-4）— 件数だけでなく「何をするイベントか」がタイトルだけで
    // 一目で分かるよう、実行内容(アクション種別)を矢印でつないだ要約を添える。
    private void RefreshCommonEventsList()
    {
        lstCommonEvents.Items.Clear();
        foreach (var ce in _commonEvents)
        {
            string summary = ce.actions.Count == 0
                ? "(実行内容が未設定)"
                : string.Join("→", ce.actions.Take(4).Select(a => a.action)) + (ce.actions.Count > 4 ? "…" : "");
            lstCommonEvents.Items.Add($"🔔 {ce.id} : {ce.name}  【{summary}】");
        }
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
    // Feature: UI改善 — タブ廃止により全種別が同時に画面上へ並ぶため、検索は表示中の全グリッド/一覧へ横断的に適用する
    private void ApplySearchFilter()
    {
        string q = txtSearch.Text.Trim();
        FilterGrid(dgvEnemies, q);
        FilterGrid(dgvGimmicks, q);
        FilterGrid(dgvItems, q);
        FilterCommonEventsList(q);
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
        switch (GetActiveKind())
        {
            case AssetKind.Enemy: DuplicateEnemyRow(); break;
            case AssetKind.Gimmick: DuplicateGimmickRow(); break;
            case AssetKind.Item: DuplicateItemRow(); break;
            case AssetKind.CommonEvent: DuplicateCommonEvent(); break;
            default: MessageBox.Show("複製する行を選択してください。", "未選択", MessageBoxButtons.OK, MessageBoxIcon.Information); break;
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
            r.Cells["icon"].Value ?? "👾",
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
            r.Cells["icon"].Value ?? "🔧",
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
            r.Cells["icon"].Value ?? "💎",
            newId, (r.Cells["name"].Value?.ToString() ?? "") + "のコピー", r.Cells["type_enum"].Value ?? ItemTypes[0].desc,
            r.Cells["hitboxOffsetX"].Value ?? 0, r.Cells["hitboxOffsetY"].Value ?? 0,
            r.Cells["hitboxWidth"].Value ?? 32, r.Cells["hitboxHeight"].Value ?? 32,
            r.Cells["sprite"].Value ?? "", r.Cells["grant_ability"].Value ?? "", "🎯", "📁", "🗑"
        });
        // Feature: Composite Multi-Part Objects (Parts-M7) — parts等の非グリッド項目も複製する
        var srcDef = GetOrCreateItemParams(r);
        _itemParams[dgvItems.Rows[dgvItems.Rows.Count - 1]] = JsonConvert.DeserializeObject<ItemDef>(JsonConvert.SerializeObject(srcDef))!;
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

// ======================================================
// TypeCardPickerForm - type_enumを数字ではなくカード形式で選ぶダイアログ
// Feature: UI改善（提案書 CUT-2/AM-1）
// ======================================================
public class TypeCardPickerForm : Form
{
    public int SelectedType { get; private set; } = -1;

    public TypeCardPickerForm(List<(int type, string desc, string detail, string icon)> options, int currentType)
    {
        Text = "🔍 タイプをカードから選ぶ";
        Size = new Size(560, 640);
        MinimumSize = new Size(420, 360);
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Meiryo UI", 9);

        var lblHint = new Label
        {
            Dock = DockStyle.Top,
            Height = 30,
            Padding = new Padding(8, 6, 8, 0),
            Text = "カードをクリックすると、そのタイプを選んで閉じます。",
            Font = new Font(Font.FontFamily, 8f),
            ForeColor = Color.DarkSlateGray,
        };

        var pnlList = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            Padding = new Padding(8),
        };

        foreach (var opt in options)
        {
            bool isCurrent = opt.type == currentType;
            var card = new Panel
            {
                Width = 500,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = isCurrent ? Color.FromArgb(255, 248, 220) : Color.White,
                Margin = new Padding(2, 2, 2, 6),
                Padding = new Padding(8),
                Cursor = Cursors.Hand,
            };
            var inner = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
            var lblHead = new Label
            {
                AutoSize = true,
                Text = $"{opt.icon}  {opt.desc}" + (isCurrent ? "　（現在選択中）" : ""),
                Font = new Font(Font, FontStyle.Bold),
            };
            var lblDetail = new Label
            {
                AutoSize = true,
                MaximumSize = new Size(470, 0),
                Text = opt.detail,
                ForeColor = Color.DimGray,
                Font = new Font(Font.FontFamily, 8f),
                Margin = new Padding(0, 4, 0, 0),
            };
            inner.Controls.Add(lblHead);
            inner.Controls.Add(lblDetail);
            card.Controls.Add(inner);

            int capturedType = opt.type;
            void Choose()
            {
                SelectedType = capturedType;
                DialogResult = DialogResult.OK;
                Close();
            }
            card.Click += (s, e) => Choose();
            foreach (var c in AllDescendants(card)) c.Click += (s, e) => Choose();
            foreach (var c in AllDescendants(card)) c.Cursor = Cursors.Hand;

            pnlList.Controls.Add(card);
        }

        var pnlBottom = new Panel { Dock = DockStyle.Bottom, Height = 46 };
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) };
        var btnCancel = new Button { Text = "キャンセル", DialogResult = DialogResult.Cancel, AutoSize = true, Padding = new Padding(10, 5, 10, 5) };
        flow.Controls.Add(btnCancel);
        pnlBottom.Controls.Add(flow);
        CancelButton = btnCancel;

        Controls.Add(pnlList);
        Controls.Add(pnlBottom);
        Controls.Add(lblHint);
    }

    private static IEnumerable<Control> AllDescendants(Control root)
    {
        foreach (Control c in root.Controls)
        {
            yield return c;
            foreach (var d in AllDescendants(c)) yield return d;
        }
    }
}
