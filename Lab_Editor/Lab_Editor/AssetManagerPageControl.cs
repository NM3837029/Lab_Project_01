// Newtonsoft.Json（Json.NET）を使用する。挙動パラメータ(EnemyDef/GimmickDef/ItemDef)を
// ディープコピーする際に、JSONへ一度シリアライズしてから逆シリアライズする手法(DuplicateXxxRow系)で利用している。
using Newtonsoft.Json;

namespace Lab_Editor;

/// <summary>
/// アセット管理 - 敵・ギミック・アイテムの総合編集ページ（構造改修フェーズ5でForm→UserControlへ抽出）
/// ・画像プレビュー付き
/// ・ファイルダイアログでスプライト選択（imgフォルダへ自動コピー）
/// ・敵の巡回範囲・HP・サイズ等フル編集
/// ・type_enumごとのパラメータ説明
/// ・ギミック・アイテムもフル編集
/// </summary>
public class AssetManagerPageControl : UserControl
{
    // このクラスはWinFormsの「UserControl」（Formそのものではなく、他のFormに埋め込んで使う部品）として作られている。
    // UserControlにはFormが持っているDialogResultプロパティやClose()メソッドが存在しないため、
    // 「保存された/キャンセルされた」という結果をこのクラスの外へ伝える手段としてイベントを使う。
    // ホスト側（AssetManagerFormという薄いラッパーFormか、WorkbenchShellFormという新しいシェル画面）が
    // これらのイベントを購読(subscribe)しておき、Savedが発火したらForm.Close()を呼んだり、
    // shell.GoBack()を呼んで元の画面に戻ったりする、という形で変換して使う。
    public event EventHandler? Saved;
    public event EventHandler? Cancelled;
    // ホスト側が「保存ボタン」「閉じる(キャンセル)ボタン」の見た目や配置を扱えるように、
    // このコントロール内部で生成したボタンそのものを外部へ公開しているプロパティ。
    public Button PrimaryActionButton => btnSave;
    public Button SecondaryActionButton => btnClose;

    // ここから下は「ドリルダウン編集」、つまりこのページの中だけでは完結せず、
    // 別の詳細編集画面（当たり判定エディタ、サイズ調整、挙動スクリプトエディタ等）を
    // 開いてもらう必要がある操作をイベントとして外部（ホスト）に依頼するためのもの。
    // ホスト側（薄いラッパーFormの場合、またはWorkbenchShellForm経由でForm1が配線している場合）が
    // これらを購読し、「モーダルFormとしてポップアップで開く」か「シェルの中でページ遷移する」かを
    // 自分の都合で決める。このクラス自身は「編集をお願いして、結果を待って受け取る」以上のことは
    // 一切ホストに要求しない（＝どちらの開き方をするかをこのクラスは知らなくてよい）。
    public event HitboxEditRequestHandler? HitboxEditRequested;
    public event SizeEditRequestHandler? SizeEditRequested;
    public event BehaviorScriptEditRequestHandler? BehaviorScriptEditRequested;
    public event PartsEditRequestHandler? PartsEditRequested;
    public event CommonEventEditRequestHandler? CommonEventEditRequested;

    // assets.json等が置かれているアセットフォルダへのパス（コンストラクタで渡され、以後変更しない）
    private readonly string assetsPath;
    // プロジェクトのルートフォルダへのパス。スプライト画像を「imgフォルダ」へコピーする際の基準になる
    private readonly string projectRoot;
    // 編集対象のアセット定義データ本体（敵・ギミック・アイテム・コモンイベントのリストをまとめて持つ）
    private AssetDefinitions assets;

    // 機能追加: UI改善（友人からのフィードバックを受けて対応）— 以前は敵/ギミック/アイテムを
    // タブで切り替える構成だったが、タブを廃止し、1つの縦積み（縦にスクロールする）ビュー＋
    // 上部のタグボタン（複数選択可能なチェックボタン）で表示/非表示を絞り込む構成に変更した。
    // 各セクション自体（中身のグリッドやリスト）は既存のものをそのまま流用しており、
    // データの読み書きロジック（保存/読み込み処理）は一切変更していない。
    // sectionEnemy等は「見出し＋中身のグリッド」をひとまとめにしたパネルで、タグボタンでVisibleを切り替える対象。
    private Panel sectionEnemy = null!, sectionGimmick = null!, sectionItem = null!, sectionCommonEvent = null!;
    // 敵・ギミック・アイテムそれぞれの一覧を表示する表形式コントロール(DataGridView)
    private DataGridView dgvEnemies = null!, dgvGimmicks = null!, dgvItems = null!;
    // コモンイベント（複数のトリガーから呼び出せる共通処理）の一覧を表示するリストボックス
    private ListBox lstCommonEvents = null!;
    // コモンイベート定義の実体を保持するリスト。保存時にassets.CommonEventsへ書き戻す
    private List<CommonEventDef> _commonEvents = new();
    // ID・名前で絞り込むための検索ボックス
    private TextBox txtSearch = null!;
    // 選択中の行を複製するボタン（複製先の種別は選択されている行の種類に応じて自動判定する）
    private Button btnDuplicate = null!;
    // 右側パネルに表示するスプライト画像のプレビュー用コントロール
    private PictureBox pbPreview = null!;
    // プレビュー中の画像のファイルパスを表示するラベル
    private Label lblPreviewPath = null!;
    // 保存ボタンと閉じる(キャンセル)ボタン
    private Button btnSave = null!, btnClose = null!;
    // 選択中の種別(敵/ギミック/アイテム/コモンイベント)のtype_enum一覧・説明文を表示するリッチテキストボックス
    private RichTextBox rtbTypeHint = null!;

    // ==== 機能: 敵/ギミックごとに調整可能な挙動パラメータ (Configurable Behavior Parameters, 通称 M1) ====
    // 敵・ギミックのDataGridViewには「ID・名前・タイプ・HP」等の基本項目しか列として出していないが、
    // それとは別に、選ばれたtype_enumに応じて細かい挙動パラメータ（移動速度係数や射撃間隔など）を
    // 行(DataGridViewRow)ごとに保持しておく必要がある。この辞書がその保持場所にあたる。
    // キーには行の"id"文字列ではなく行オブジェクト(DataGridViewRow)自身を使っている。
    // これは、ユーザーがグリッド上でid欄の文字を書き換えた場合でも、それが原因で
    // 「どのパラメータがどの行のものか」が食い違ってしまう不整合を避けるための工夫である。
    private readonly Dictionary<DataGridViewRow, EnemyDef> _enemyParams = new();
    private readonly Dictionary<DataGridViewRow, GimmickDef> _gimmickParams = new();
    // 機能: 複数パーツからなる複合オブジェクト (Composite Multi-Part Objects, 通称 Parts-M7)
    // — アイテムについても敵/ギミックと同様に、グリッドの列には表示しない付加情報（partsという
    // 子パーツの配列など）を、対応する行に紐づけて保持しておく必要があるため用意した辞書。
    private readonly Dictionary<DataGridViewRow, ItemDef> _itemParams = new();
    // 選択中の敵/ギミックの挙動パラメータ入力欄をまとめて表示するパネル（type_enumごとに中身を動的に作り直す）
    private Panel pnlBehaviorParams = null!;
    // 右側パネルの見出しラベル。「📋 タイプ説明」または「⚙ 挙動パラメータ」のどちらかの文言に切り替わる
    private Label lblTypeHintTitle = null!;
    // 挙動パラメータ欄(NumericUpDown)の値を「プログラム側から」書き換えている最中かどうかを示すフラグ。
    // trueの間はValueChangedイベント内の処理をスキップし、ユーザー未操作なのに値が上書きされる無限ループや
    // 意図しない書き込みを防ぐ（UpdateBehaviorParamsPanel参照）。
    private bool _isUpdatingBehaviorPanel = false;

    // type_enum(敵のタイプ番号)ごとに表示する挙動パラメータ欄の定義一覧。
    // タプルの中身は (フィールド名: EnemyDefのプロパティ名, ラベル: 画面に表示する日本語名, 小数点以下桁数: NumericUpDownの表示桁数)。
    // 例えば type_enum=0 (巡回)を選ぶと、"moveSpeed"というプロパティを「移動速度係数」というラベルで、
    // 小数点以下2桁のNumericUpDownとして自動的に画面へ並べる、という設定になっている。
    private static readonly Dictionary<int, (string Field, string Label, int Decimals)[]> EnemyParamFields = new()
    {
        [0] = new[] { ("moveSpeed", "移動速度係数", 2) }, // type_enum=0: 巡回(Patrol)
        [1] = new[] { ("actionInterval", "ジャンプ間隔(フレーム)", 0), ("jumpPowerMult", "ジャンプ力係数", 2) }, // type_enum=1: ジャンプ(Jumper)
        [2] = new[] { ("actionInterval", "射撃間隔(フレーム)", 0), ("projectileSpeed", "弾速係数", 2), ("fastForwardAttackMult", "早送り中の攻撃間隔倍率", 2) }, // type_enum=2: 固定砲台(Stationary)
        [3] = new[] { ("triggerRange", "索敵X範囲(px)", 0), ("detectionRangeY", "索敵Y範囲(px)", 0), ("moveSpeed", "巡回速度係数", 2), ("cooldownTime", "射撃後クールダウン(フレーム)", 0), ("projectileSpeed", "弾速係数", 2), ("fastForwardAttackMult", "早送り中の攻撃間隔倍率", 2) }, // type_enum=3: 巡回砲台(Patrol+Shoot)
        [4] = new[] { ("moveSpeed", "移動速度係数", 2) }, // type_enum=4: 歩いてくる(Walker)
        [5] = new[] { ("moveSpeed", "移動速度係数", 2), ("jumpPowerMult", "ジャンプ力係数", 2) }, // type_enum=5: 追っかけてくる(Chaser)
        [6] = new[] { ("triggerRange", "発動距離(px)", 0), ("chargeTime", "溜め時間(フレーム)", 0), ("dashSpeedMult", "突進速度係数", 2), ("dashDuration", "突進継続時間(フレーム)", 0), ("cooldownTime", "クールダウン(フレーム)", 0) }, // type_enum=6: 突進(Dash Charger)
        [7] = new[] { ("triggerRange", "真下判定幅(px)", 0), ("fallDelay", "落下開始遅延(フレーム)", 0), ("cooldownTime", "着地後クールダウン(フレーム)", 0), ("shockwaveRadius", "着地ショックウェイブ半径(px)", 0), ("fastForwardJitter", "早送り中の落下ジッター量(px)", 0), ("diagonalFallSpeed", "方向反転時の斜め落下速度(px/フレーム)", 2) }, // type_enum=7: 落ちてくる敵(Faller)
        [8] = new[] { ("actionInterval", "射撃間隔(フレーム)", 0), ("spreadAngle", "拡散角度(ラジアン)", 2), ("spreadCount", "弾数", 0), ("projectileSpeed", "弾速係数", 2) }, // type_enum=8: 拡散弾(Spread Shooter)
        [9] = new[] { ("actionInterval", "射撃間隔(フレーム)", 0), ("projectileSpeed", "弾速係数", 2) }, // type_enum=9: 照準弾(Aimed Shooter)
        [10] = new[] { ("floatAmplitude", "浮遊振幅(px)", 0), ("floatFrequency", "浮遊周波数", 3), ("moveSpeed", "接近速度係数", 2) }, // type_enum=10: 浮遊敵(Floater)
        [11] = new[] { ("actionInterval", "テレポート間隔(フレーム)", 0), ("teleportRangeMin", "オフセット最小(px)", 0), ("teleportRangeMax", "オフセット最大(px)", 0) }, // type_enum=11: テレポーター(Teleporter)
        [12] = new[] { ("moveSpeed", "通常時速度係数", 2), ("enragedMoveSpeed", "覚醒後速度係数", 2), ("shrinkFactor", "縮小率", 2) }, // type_enum=12: 分裂もどき(Shrinker)
        [13] = new[] { ("moveSpeed", "移動速度係数", 2), ("shieldOffDuration", "無敵解除継続(フレーム)", 0), ("shieldOnDuration", "無敵継続(フレーム)", 0) }, // type_enum=13: シールド(Shield)
        [14] = new[] { ("mimicDelayFrames", "遅延フレーム数", 0) }, // type_enum=14: 幽霊敵(Mimic Ghost)
        [15] = new[] { ("moveSpeed", "移動速度係数", 2), ("sizeAmplitude", "スケール振幅", 2), ("sizeFrequency", "スケール周波数", 3), ("minScale", "最小スケール", 2) }, // type_enum=15: 大きさが変わる敵(Size Shifter)
        [16] = new[] { ("moveSpeed", "基準速度係数", 2), ("tempoFrequency", "周波数", 3), ("tempoMin", "speedScale最小", 2), ("tempoMax", "speedScale最大", 2) }, // type_enum=16: 速さ操作敵(Tempo Warper)
        [17] = new[] { ("moveSpeed", "移動速度係数", 2), ("effectRange", "効果範囲(px)", 0), ("brightnessMin", "最小輝度", 2) }, // type_enum=17: 明るさ操作敵(Brightness Phantom)
        [18] = new[] { ("moveSpeed", "移動速度係数", 2), ("effectRange", "効果範囲(px)", 0), ("tintStrength", "色シフト強度", 2) }, // type_enum=18: 色調整敵(Color Shifter)
        [19] = new[] { ("effectRange", "効果範囲(px)", 0), ("zoomAmplitude", "ズーム振幅", 2), ("zoomFrequency", "ズーム周波数", 3) }, // type_enum=19: ズーム撹乱敵(Zoom Disruptor)
    };
    // type_enum(ギミックのタイプ番号)ごとに表示する挙動パラメータ欄の定義一覧。中身の意味はEnemyParamFieldsと同じ形式。
    // ここに定義が無いtype_enum（＝配列の添字にキーが存在しない番号）は、そのギミックに調整可能なパラメータが
    // 無いことを意味し、その場合はUpdateBehaviorParamsPanel側でパラメータ欄を出さずに従来の説明文だけを表示する。
    private static readonly Dictionary<int, (string Field, string Label, int Decimals)[]> GimmickParamFields = new()
    {
        [0] = new[] { ("warpOffsetPx", "ワープ後オフセット(px)", 1) }, // type_enum=0: ポータル(Cut Portal)
        [1] = new[] { ("rotationSpeed", "回転速度(rad/フレーム)", 3) }, // type_enum=1: 回転橋・自動(Rotating Bridge)
        [4] = new[] { ("sinkSpeed", "降下速度(px/フレーム)", 2), ("maxDepthOffset", "最大沈み込み(px)", 0) }, // type_enum=4: 落下リフト(Falling Lift)
        [5] = new[] { ("pushOutDistance", "押し出し距離係数", 2) }, // type_enum=5: 反射鏡(Reflect Mirror)
        [6] = new[] { ("triggerWidthThreshold", "起動に必要な横幅(px)", 0) }, // type_enum=6: 重量スイッチ(Weight Switch)
        [11] = new[] { ("standDelayFrames", "乗ってから落下まで(フレーム)", 0), ("standTolerancePx", "乗り判定の許容誤差(px)", 0), ("respawnDelayFrames", "復活までの時間(フレーム)", 0) }, // type_enum=11: ちくわブロック(Chikuwa Block)
        [12] = new[] { ("radius", "効果範囲の半径(px)", 0) }, // type_enum=12: 時間フィールド(Time Field)
        [14] = new[] { ("travelDistance", "可動距離(px)", 0), ("oscillationSpeed", "往復の速さ", 3) }, // type_enum=14: 動く足場(Moving Platform)
        [17] = new[] { ("travelDistance", "可動距離(px)", 0), ("stepIncrement", "1回あたりの移動割合", 2) }, // type_enum=17: コマ送りリフト(Framestep Lift)
        [18] = new[] { ("brightLevel", "明転時の輝度", 2), ("darkLevel", "暗転時の輝度", 2) }, // type_enum=18: 明暗ゾーン(Brightness Zone)
        [19] = new[] { ("tintR", "色調R", 2), ("tintG", "色調G", 2), ("tintB", "色調B", 2) }, // type_enum=19: 色調ゾーン(Color Zone)
        [20] = new[] { ("zoomLevel", "ズーム倍率", 2) }, // type_enum=20: ズームレンズ(Zoom Lens)
        [21] = new[] { ("zoomLevel", "ズーム倍率", 2), ("brightLevel", "明るさ倍率", 2) }, // type_enum=21: スローフィールド(Slowmo Field)
    };

    // 敵のtype_enum(タイプ番号)ごとの説明一覧。
    // desc: グリッドのコンボボックスやカード選択画面の見出しに使う短い表示名（"番号 = 名前 (英語名)"の形式）
    // detail: カード選択画面(TypeCardPickerForm)や右側の説明パネル(rtbTypeHint)に表示する、動作を平易な言葉で説明した文章
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
    // 機能追加: UI改善（提案書のCUT-2/AM-1という項目に対応）— 敵タイプ(EnemyTypes)には元々あった
    // 「専門用語を使わない平易な説明文(detail)」を、ギミック/アイテムのタイプにも同じように用意した。
    // これにより、type_enumという数字の羅列だけを見ても分からなかった実際の挙動が、
    // 誰が見てもひと目で分かるようになる。
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
        (25, "25 = チェックポイント (Checkpoint)", "プレイヤーが触れるとその地点が復帰地点として記録されます。ゲームオーバーや落下死からのリトライは、記録済みならステージ開始位置ではなくこの地点から再開します。1ステージに複数置くと直近で触れたものが有効になります。"),
    };
    // アイテムのtype_enum(タイプ番号)ごとの説明一覧。敵・ギミックと同じ (番号, 表示名, 詳細説明) の形式
    private static readonly (int type, string desc, string detail)[] ItemTypes =
    {
        (0, "0 = なし", "特に効果を持たない、装飾・プレースホルダー用のアイテムです。"),
        (1, "1 = コイン", "取得するとスコア/所持コイン数が増えます。"),
        (2, "2 = 回復アイテム", "取得するとプレイヤーのHPを回復します。"),
    };

    // コンストラクタ。
    // assetsPath : アセット定義(assets.json等)が置かれているフォルダへのパス
    // assets     : 既に読み込み済みの敵・ギミック・アイテム・コモンイベント定義データ
    public AssetManagerPageControl(string assetsPath, AssetDefinitions assets)
    {
        this.assetsPath = assetsPath;
        // プロジェクトルートは、渡されたassetsPathの「1つ上のフォルダ」として求める
        // （スプライト画像をコピーする先のimgフォルダ等が、ここを基準に決まるため）。
        this.projectRoot = Path.GetDirectoryName(assetsPath)!;
        this.assets = assets;
        // コモンイベント一覧は、渡されたassetsをそのまま参照するのではなく、
        // 個々の要素・アクションリストを丸ごと複製(ディープコピーに近い形)して_commonEventsへ持っておく。
        // これにより、このページ上で編集している最中の内容が保存操作を行うまでassets本体には影響しない。
        _commonEvents = assets.CommonEvents
            .Select(ce => new CommonEventDef { id = ce.id, name = ce.name, actions = new List<EventActionEntry>(ce.actions) })
            .ToList();
        // 画面上の各コントロール(グリッド・ボタン・プレビュー等)を組み立てる
        InitUI();
        // assetsの内容を各グリッド/リストへ流し込んで表示する
        LoadData();
    }

    // stagesフォルダに存在するステージJSONファイル名の一覧を返す。
    // （呼び出し元でステージ選択UI等に使われることを想定している）
    public List<string> GetStageFileNames()
    {
        string stagesPath = Path.Combine(assetsPath, "stages");
        // stagesフォルダ自体が存在しない場合は、エラーにせず空のリストを返す
        if (!Directory.Exists(stagesPath)) return new List<string>();
        return Directory.GetFiles(stagesPath, "*.json")
            .Select(Path.GetFileName)
            // "_test_play.json"はテストプレイ専用の一時ファイルなので、通常のステージ一覧からは除外する
            .Where(n => n != null && n != "_test_play.json")
            .Select(n => n!)
            .ToList();
    }

    // このページを構成する全コントロール（検索欄・タグボタン・プレビュー・各グリッド・ボタン類）を
    // ここで一括して生成し、配置する。コンストラクタから一度だけ呼ばれる、いわば画面の組み立て工程。
    private void InitUI()
    {
        // このUserControl自体を親(ホストFormやシェル画面)いっぱいに広げる
        Dock = DockStyle.Fill;
        // アプリ全体で統一されたフォント設定(UiTheme.Base)を適用する
        Font = UiTheme.Base;

        // ===== 上部: 検索ボックス + タグ絞り込みボタン =====
        // 機能追加: UI改善 — 検索欄と、複数選択できる種別タグボタン（押し込み状態を持つボタン=Appearance.Button）を
        // 1つのDock=Top(画面上端に張り付く)パネルにまとめている。タグはCheckBoxコントロールで実装しており、
        // 押した状態(Checked=true)が「そのタグがON＝該当セクションを表示する」という意味になる。
        // 機能追加: UI改善 — 検索欄とタグ行はウィンドウの横幅によっては折り返して2行以上になる可能性があるため、
        // 高さを固定値にせず、AutoSize（中身に合わせて自動で高さが伸縮する設定）にしている。
        // こうしないと、折り返した分の行が下段のグリッド等に隠れてはみ出してしまう。
        var pnlTop = new Panel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };

        // 検索アイコン・検索ボックス・複製ボタンを横に並べるツールバー
        var pnlToolbar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(4) };
        var lblSearch = new Label { Text = "🔍", AutoSize = true, Margin = new Padding(2, 6, 0, 0) };
        txtSearch = new TextBox { Width = 240, Margin = new Padding(4, 3, 12, 0), PlaceholderText = "ID・名前で検索..." };
        // 検索ボックスの文字が変わるたびに、即座に絞り込みを再適用する（ボタン押下等は不要）
        txtSearch.TextChanged += (s, e) => ApplySearchFilter();
        btnDuplicate = new Button { Text = "⧉ 選択行を複製", AutoSize = true, Padding = new Padding(6, 4, 6, 4), Margin = new Padding(4, 1, 0, 0) };
        btnDuplicate.Click += BtnDuplicate_Click;
        pnlToolbar.Controls.AddRange(new Control[] { lblSearch, txtSearch, btnDuplicate });

        // 敵/ギミック/アイテム/コモンイベントの各セクションを表示するかどうかを切り替えるタグボタン群
        var pnlTags = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(4, 0, 4, 4) };
        // 4つのタグボタンはすべて初期状態でChecked=true（＝すべてのセクションを表示した状態で開始する）
        var chkTagEnemy = new CheckBox { Text = "👾 敵", Appearance = Appearance.Button, Checked = true, AutoSize = true, Padding = new Padding(8, 4, 8, 4), Margin = new Padding(0, 0, 4, 0) };
        var chkTagGimmick = new CheckBox { Text = "🔧 ギミック", Appearance = Appearance.Button, Checked = true, AutoSize = true, Padding = new Padding(8, 4, 8, 4), Margin = new Padding(0, 0, 4, 0) };
        var chkTagItem = new CheckBox { Text = "💎 アイテム", Appearance = Appearance.Button, Checked = true, AutoSize = true, Padding = new Padding(8, 4, 8, 4), Margin = new Padding(0, 0, 4, 0) };
        var chkTagCommonEvent = new CheckBox { Text = "🔔 コモンイベント", Appearance = Appearance.Button, Checked = true, AutoSize = true, Padding = new Padding(8, 4, 8, 4), Margin = new Padding(0, 0, 4, 0) };
        // チェック状態が変わるたびに、対応するセクションパネルのVisibleを直接切り替える（単純なON/OFF連動）
        chkTagEnemy.CheckedChanged += (s, e) => sectionEnemy.Visible = chkTagEnemy.Checked;
        chkTagGimmick.CheckedChanged += (s, e) => sectionGimmick.Visible = chkTagGimmick.Checked;
        chkTagItem.CheckedChanged += (s, e) => sectionItem.Visible = chkTagItem.Checked;
        chkTagCommonEvent.CheckedChanged += (s, e) => sectionCommonEvent.Visible = chkTagCommonEvent.Checked;
        pnlTags.Controls.AddRange(new Control[] { chkTagEnemy, chkTagGimmick, chkTagItem, chkTagCommonEvent });

        // タグ行を先に追加し、その下にツールバー行を追加する（Dock=Topなので追加順=画面上での並び順になる）
        pnlTop.Controls.Add(pnlTags);
        pnlTop.Controls.Add(pnlToolbar);

        // ===== 右サイドパネル（プレビュー・タイプ説明・挙動パラメータをまとめて表示） =====
        var pnlRight = new Panel { Dock = DockStyle.Right, Width = 260, BorderStyle = BorderStyle.FixedSingle };
        // 右サイドの中身は縦に並べるFlowLayoutPanel。AutoSizeで必要な高さぶんだけ伸びる
        var flowRight = new FlowLayoutPanel { Dock = DockStyle.Top, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(5) };

        // スプライト画像のプレビュー表示エリア（黒背景の枠+マウスホイールでズーム可能）
        var lblPrev = new Label { Text = "🖼 スプライトプレビュー（ホイールでズーム）", AutoSize = true, Font = new Font("Meiryo UI", 9, FontStyle.Bold), Margin = new Padding(0, 0, 0, 4) };
        var pnlPreviewHost = new Panel { Width = 238, Height = 180, BorderStyle = BorderStyle.FixedSingle, BackColor = Color.Black, AutoScroll = true, Margin = new Padding(0, 0, 0, 2) };
        pbPreview = new PictureBox { SizeMode = PictureBoxSizeMode.Normal, BackColor = Color.Black };
        pnlPreviewHost.Controls.Add(pbPreview);
        // プレビュー枠の上でマウスホイールを回すとズームイン/アウトする（PnlPreviewHost_MouseWheel参照）
        pnlPreviewHost.MouseWheel += PnlPreviewHost_MouseWheel;
        lblPreviewPath = new Label { Width = 238, Height = 40, Font = new Font("Meiryo UI", 7), ForeColor = Color.Gray, Text = "(選択なし)", Margin = new Padding(0, 0, 0, 4) };

        // 「タイプ説明」パネル。選択中の種別のtype_enum一覧・平易な説明文をここに表示する
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

        // 機能: 敵/ギミックごとに調整可能な挙動パラメータ (M1) — 選択中の敵/ギミック行のtype_enumに
        // 応じて、挙動パラメータの入力欄(NumericUpDown群)を動的に組み立てて表示するためのパネル。
        // rtbTypeHint（タイプ説明欄）と同じ場所に重ねて配置しておき、状況に応じてどちらか一方だけを
        // Visible=trueにすることで、あたかも1つの枠の中身が切り替わっているように見せている。
        pnlBehaviorParams = new Panel
        {
            Width = 238,
            Height = 330,
            AutoScroll = true,
            Visible = false
        };

        // 右サイドパネルへ、上から順に「プレビュー見出し→プレビュー枠→パス表示→タイプ説明見出し→
        // タイプ説明欄→挙動パラメータパネル」の順で追加する（FlowLayoutPanelなのでこの順に縦に並ぶ）
        flowRight.Controls.Add(lblPrev);
        flowRight.Controls.Add(pnlPreviewHost);
        flowRight.Controls.Add(lblPreviewPath);
        flowRight.Controls.Add(lblTypeHintTitle);
        flowRight.Controls.Add(rtbTypeHint);
        flowRight.Controls.Add(pnlBehaviorParams);
        pnlRight.Controls.Add(flowRight);

        // ===== 下部ボタン（右詰め=保存/キャンセル、左詰め=追加系ボタン群。FlowLayoutPanelで自動配置） =====
        // 機能追加: UI改善 — 左詰め側はボタンの数が多く、ウィンドウが狭いとWrapContentsによって
        // 複数行に折り返される。以前は下段パネルの高さを固定値にしていたため、折り返して増えた行
        // （保存/キャンセルボタンを含む行）がパネルの外側にはみ出して見えなくなる不具合があった。
        // そこでpnlBottom自体をAutoSizeにし、必要な行数ぶんだけ自動的に高さが伸びるようにしてある。
        var pnlBottom = new Panel { Dock = DockStyle.Bottom, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
        // 保存ボタン・キャンセルボタンは右詰め(RightToLeft方向に並べる)ことで、右端から順に配置される
        var flowBottomRight = new FlowLayoutPanel { Dock = DockStyle.Top, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8, 6, 8, 2), AutoSize = true };
        // 保存ボタン。目立つよう緑色の背景色にしている
        btnSave = new Button { Text = "💾 保存して閉じる", AutoSize = true, Padding = new Padding(10, 6, 10, 6), BackColor = Color.FromArgb(40, 167, 69), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Font = new Font("Meiryo UI", 10, FontStyle.Bold) };
        btnSave.Click += BtnSave_Click;
        btnClose = new Button { Text = "キャンセル", AutoSize = true, Padding = new Padding(10, 6, 10, 6) };
        // キャンセルボタンは保存処理を一切行わず、Cancelledイベントを発火してホスト側に後始末を任せるだけ
        btnClose.Click += (s, e) => Cancelled?.Invoke(this, EventArgs.Empty);
        flowBottomRight.Controls.Add(btnSave);
        flowBottomRight.Controls.Add(btnClose);

        // 敵/ギミック/アイテム/コモンイベントの「追加」ボタンと、パーツ編集・挙動スクリプト編集・
        // タイプカード選択の各ボタンを左詰めで並べる行
        var flowBottomLeft = new FlowLayoutPanel { Dock = DockStyle.Top, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(8, 2, 8, 6), AutoSize = true, WrapContents = true };
        var btnAddEnemy = new Button { Text = "＋ 敵追加", AutoSize = true, Padding = new Padding(6, 5, 6, 5) };
        // 押すと敵グリッドへ既定値(GetDefaultEnemyRow)の新規行を1行追加する
        btnAddEnemy.Click += (s, e) => AddRow(dgvEnemies, GetDefaultEnemyRow());
        var btnAddGimmick = new Button { Text = "＋ ギミック追加", AutoSize = true, Padding = new Padding(6, 5, 6, 5) };
        btnAddGimmick.Click += (s, e) => AddRow(dgvGimmicks, GetDefaultGimmickRow());
        var btnAddItem = new Button { Text = "＋ アイテム追加", AutoSize = true, Padding = new Padding(6, 5, 6, 5) };
        btnAddItem.Click += (s, e) => AddRow(dgvItems, GetDefaultItemRow());
        var btnAddCommonEvent = new Button { Text = "＋ コモンイベント追加", AutoSize = true, Padding = new Padding(6, 5, 6, 5) };
        btnAddCommonEvent.Click += (s, e) => AddCommonEvent();
        // 機能: 複数パーツからなる複合オブジェクト (Parts-M7) — 敵/ギミック/アイテムのどのグリッドを
        // 選択していても共通で使えるボタン。パーツは親オブジェクトのtype_enumとは独立して機能するため、
        // 挙動スクリプト編集ボタンのような「特定タイプでなければ使えない」という制限は設けていない。
        var btnPartsEditor = new Button { Text = "🧩 パーツを編集", AutoSize = true, Padding = new Padding(6, 5, 6, 5) };
        btnPartsEditor.Click += (s, e) => BtnPartsEditor_Click();
        // 機能: ブロック(パズル)組み立て式の挙動スクリプティング (M4) — ブロックエディタ画面を開くボタン
        var btnBehaviorScript = new Button { Text = "🧩 挙動スクリプトを編集", AutoSize = true, Padding = new Padding(6, 5, 6, 5) };
        btnBehaviorScript.Click += (s, e) => BtnBehaviorScript_Click();
        // 機能追加: UI改善（提案書のCUT-2/AM-1という項目に対応）— コンボボックスの中から数字と文字が
        // 並んだ選択肢を選ぶのではなく、アイコン・名前・説明文が並んだカード一覧をクリックして
        // type_enumを選べるようにするためのボタン
        var btnTypeCardPicker = new Button { Text = "🔍 タイプをカードから選ぶ", AutoSize = true, Padding = new Padding(6, 5, 6, 5) };
        btnTypeCardPicker.Click += (s, e) => BtnTypeCardPicker_Click();
        flowBottomLeft.Controls.AddRange(new Control[] { btnAddEnemy, btnAddGimmick, btnAddItem, btnAddCommonEvent, btnPartsEditor, btnBehaviorScript, btnTypeCardPicker });

        pnlBottom.Controls.Add(flowBottomLeft);
        pnlBottom.Controls.Add(flowBottomRight);

        // ===== 中央: 敵/ギミック/アイテム/コモンイベントを1つの縦スクロールビューに集約 =====
        // 機能追加: UI改善 — 以前はFlowLayoutPanel(AutoSize)の直接の子としてDock=Fillのグリッドを
        // 置いていたが、これだと「親がAutoSizeで子のサイズから逆算しようとする」のに対して
        // 「子はDock=Fillで親のサイズから逆算しようとする」という、お互いがお互いを基準にして
        // サイズを決めようとする循環（無限ループのようなもの）に陥ってしまい、結果としてグリッドが
        // 極端に狭く潰れて表示される不具合の原因になっていた。
        // ここでは各セクションを「高さは固定値・幅だけウィンドウに合わせて可変(Dock=Top)」のPanelにし、
        // その内部でグリッドをDock=Fillにする、という構成にしている。親の高さが確定しているため、
        // 子のDock=Fillが安全に機能する。これにより、ウィンドウ幅の変化にもグリッドの横幅が正しく追従する。
        var pnlSections = new Panel { Dock = DockStyle.Fill, AutoScroll = true };

        // 敵セクション: グリッド生成＋見出し付きパネル化
        dgvEnemies = CreateEnemyGrid();
        sectionEnemy = BuildSection("👾 敵 (Enemies)", dgvEnemies, 300);

        // ギミックセクション
        dgvGimmicks = CreateGimmickGrid();
        sectionGimmick = BuildSection("🔧 ギミック (Gimmicks)", dgvGimmicks, 300);

        // アイテムセクション
        dgvItems = CreateItemGrid();
        sectionItem = BuildSection("💎 アイテム (Items)", dgvItems, 220);

        // ===== コモンイベント (RPGツクールMZ風: 複数トリガーから呼び出せる共通処理) =====
        lstCommonEvents = new ListBox { Font = new Font("Meiryo UI", 9) };
        // ダブルクリックで選択中のコモンイベントを編集画面へ
        lstCommonEvents.DoubleClick += (s, e) => EditSelectedCommonEvent();
        // 選択が変わったら「今アクティブな種別」をコモンイベントに切り替え、他のグリッドの選択を解除する
        lstCommonEvents.SelectedIndexChanged += (s, e) => { if (lstCommonEvents.SelectedIndex >= 0) ClearOtherSelections(AssetKind.CommonEvent); };
        var pnlCommonEventContent = new Panel();
        lstCommonEvents.Dock = DockStyle.Fill;
        // リストの下に「編集」「削除」ボタンを並べる行
        var flowCeButtons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 34, Padding = new Padding(2) };
        var btnCeEdit = new Button { Text = "✎ 編集", AutoSize = true, Padding = new Padding(6, 4, 6, 4) };
        btnCeEdit.Click += (s, e) => EditSelectedCommonEvent();
        var btnCeDelete = new Button { Text = "🗑 削除", AutoSize = true, Padding = new Padding(6, 4, 6, 4) };
        btnCeDelete.Click += (s, e) => DeleteSelectedCommonEvent();
        flowCeButtons.Controls.AddRange(new Control[] { btnCeEdit, btnCeDelete });
        pnlCommonEventContent.Controls.Add(lstCommonEvents);
        pnlCommonEventContent.Controls.Add(flowCeButtons);
        sectionCommonEvent = BuildSection("🔔 コモンイベント", pnlCommonEventContent, 240);

        // Dock=Topの子コントロールは「先に追加したものほど画面の端(この場合は上)に近づく」性質があるため、
        // ここでのControls.Add呼び出し順が、そのまま画面上でのセクションの表示順（敵→ギミック→アイテム→
        // コモンイベントの順）になる。
        pnlSections.Controls.Add(sectionEnemy);
        pnlSections.Controls.Add(sectionGimmick);
        pnlSections.Controls.Add(sectionItem);
        pnlSections.Controls.Add(sectionCommonEvent);

        // 最後にこのUserControl自身へ各パネルを追加する。WinFormsのDockでは後から追加したものが
        // 優先的に外側（画面の端）を占有するため、Dock=Fillのpnlsectionsを最初に追加している。
        Controls.Add(pnlSections);
        Controls.Add(pnlRight);
        Controls.Add(pnlBottom);
        Controls.Add(pnlTop);
        // コモンイベント一覧の初期表示を反映させる
        RefreshCommonEventsList();
        // 右側の「タイプ説明」欄の初期表示を反映させる（まだ何も選択されていない状態の表示）
        UpdateTypeHint();
    }

    // 種別セクション1つぶんの「見出しラベル＋中身のコントロール」をひとまとめのPanelにする補助メソッド。
    // title         : セクションの見出しに表示する文字列（例:「👾 敵 (Enemies)」）
    // content       : 見出しの下に配置する中身のコントロール（グリッドやリスト等）
    // contentHeight : 中身の高さ(px)。セクション全体の高さは、この値+見出しの高さ+上下マージンで決まる
    //
    // 機能追加: UI改善 — 高さを固定値にし、幅だけウィンドウに合わせて可変(Dock=Top)にすることで、
    // 中身(グリッド等)側をDock=Fillにしても安全に働く（親の高さが確定しているため）。
    // これにより、ウィンドウ幅の変化に応じて中身の横幅も正しく追従するようになる。
    private Panel BuildSection(string title, Control content, int contentHeight)
    {
        // 見出しラベルの高さと、パネル上下の余白の大きさを定数として定義しておく
        const int titleHeight = 24;
        const int topMargin = 4, bottomMargin = 14;
        // セクション全体のPanel。高さは「見出し+中身+上下マージン」の合計値で固定する
        var section = new Panel
        {
            Dock = DockStyle.Top,
            Height = titleHeight + contentHeight + topMargin + bottomMargin,
            Padding = new Padding(4, topMargin, 4, bottomMargin),
        };
        // 見出しラベル。太字にして中身との区別をつけ、下揃え(BottomLeft)にして中身の直上にくっつける
        var lbl = new Label { Dock = DockStyle.Top, Height = titleHeight, Text = title, Font = new Font(Font, FontStyle.Bold), TextAlign = ContentAlignment.BottomLeft };
        // 中身(グリッド等)はパネルいっぱいに広げ、余白は持たせない
        content.Dock = DockStyle.Fill;
        content.Margin = new Padding(0);
        // WinFormsのDockは「後から追加したコントロールほど優先的に外側の領域を占有する」性質があるため、
        // Dock=Fillの中身を先に追加し、その後にDock=Topの見出しラベルを追加する（この順番でないと
        // 見出しの分だけ中身が正しく縮まず、レイアウトが崩れる）。
        section.Controls.Add(content);
        section.Controls.Add(lbl);
        return section;
    }

    // プレビュー枠(pnlPreviewHost)の上でマウスホイールが回された時に呼ばれる。
    // ホイールを上に回す(Delta>0)と拡大、下に回すと縮小するようにズーム率を段階的に変化させる。
    private void PnlPreviewHost_MouseWheel(object? sender, MouseEventArgs e)
    {
        // まだ画像が読み込まれていない場合は何もしない
        if (pbPreview.Image == null) return;
        // ホイール1段階につき約15%ずつ拡大/縮小する係数を求める
        float factor = e.Delta > 0 ? 1.15f : 1f / 1.15f;
        // ズーム率を現在値に係数を掛けた値にし、極端に小さく/大きくなりすぎないよう0.1倍～8倍の範囲に収める
        _previewZoom = Math.Clamp(_previewZoom * factor, 0.1f, 8f);
        // 実際にPictureBoxのサイズへ反映する
        ApplyPreviewZoom();
    }

    // プレビュー画像の現在のズーム倍率(1.0が等倍)
    private float _previewZoom = 1f;

    // _previewZoomの値を元に、実際にプレビュー用PictureBoxの表示サイズを計算して適用する
    private void ApplyPreviewZoom()
    {
        if (pbPreview.Image == null) return;
        pbPreview.Size = new Size((int)(pbPreview.Image.Width * _previewZoom), (int)(pbPreview.Image.Height * _previewZoom));
    }

    // 敵一覧を表示するDataGridViewを新規に組み立てて返す。
    // 列構成: アイコン/ID/名前/タイプ(type_enum)/HP/幅/高さ/(非表示の当たり判定情報)/画像パス/各種操作ボタン
    private DataGridView CreateEnemyGrid()
    {
        // 機能追加: UI改善 — Dock=Fillは、AutoSize設定のFlowLayoutPanel(BuildSection呼び出し元)の
        // 直接の子コントロールには設定しない方がよい。親がAutoSizeで「子のサイズから親のサイズを逆算する」
        // のに対し、子がDock=Fillだと「親のサイズから自分のサイズを逆算しよう」とするため、
        // お互いに依存し合ってサイズ計算が循環してしまい、結果としてグリッドが極端に狭く
        // 潰れて表示される不具合の原因になっていた。
        // そのため、ここではグリッド自体には固定サイズを持たせず（BuildSection側でWidth/Heightを
        // 明示的に指定した固定サイズのPanelに入れることで）安全な構成にしている。
        var dgv = new DataGridView
        {
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            Font = new Font("Meiryo UI", 9),
            RowHeadersWidth = 25
        };

        // グリッドの列を一括で定義する。
        // icon/id/name/type_enum/hp/width/heightは画面に表示される列。
        // hitboxOffsetX～scaleまでの4~5列はVisible=falseの「非表示列」で、当たり判定やスケール値を
        // 内部的にセルの値として保持しておくためだけに使う（Hitbox/Sizeボタンから編集される）。
        // spriteは画像パスの表示（編集不可、ボタン経由でのみ変更）。
        // 末尾のbtnHitbox/btnSize/btnSprite/btnDelは、押すと処理が走るボタン列。
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
            new DataGridViewButtonColumn  { Name="btnHitbox", HeaderText="Hitbox", Text="🎯", UseColumnTextForButtonValue=true, FillWeight=35, ToolTipText="この行の当たり判定(Hitbox)を編集します" },
            new DataGridViewButtonColumn  { Name="btnSize",   HeaderText="Size",   Text="📏", UseColumnTextForButtonValue=true, FillWeight=35, ToolTipText="画像の表示サイズ(拡大率)を調整します" },
            new DataGridViewButtonColumn  { Name="btnSprite", HeaderText="📁選択",  Text="📁", UseColumnTextForButtonValue=true, FillWeight=35, ToolTipText="スプライト画像ファイルを選択します" },
            new DataGridViewButtonColumn  { Name="btnDel",    HeaderText="🗑削除",   Text="🗑", UseColumnTextForButtonValue=true, FillWeight=30, ToolTipText="この行を削除します" },
        });

        // 各種イベントの配線。
        // CellContentClick: ボタン列(btnHitbox等)がクリックされた時にHandleGridButtonへ処理を委譲する
        dgv.CellContentClick += (s, e) => HandleGridButton(dgv, e);
        // SelectionChanged: 行の選択が変わったら、他グリッドの選択解除・プレビュー更新・
        // 挙動パラメータパネル更新・タイプ説明欄更新をまとめて行う
        dgv.SelectionChanged += (s, e) => { if (dgv.SelectedRows.Count > 0) ClearOtherSelections(AssetKind.Enemy); UpdatePreview(dgv); UpdateBehaviorParamsPanel(dgv, isEnemy: true); UpdateTypeHint(); };
        // CurrentCellDirtyStateChanged: コンボボックス等、値が変わった瞬間にCommitEditしないと
        // CellValueChangedが即座には発火しない列があるため、変更中フラグが立ったら即コミットする
        dgv.CurrentCellDirtyStateChanged += (s, e) => { if (dgv.IsCurrentCellDirty) dgv.CommitEdit(DataGridViewDataErrorContexts.Commit); };
        // CellValueChanged: type_enum列の値が変わったら、挙動パラメータパネルとアイコン表示を更新する
        dgv.CellValueChanged += (s, e) => { if (dgv.Columns[e.ColumnIndex].Name == "type_enum") { UpdateBehaviorParamsPanel(dgv, isEnemy: true); RefreshIconCell(dgv, e.RowIndex, isEnemy: true, isGimmick: false); } };
        return dgv;
    }

    // ギミック一覧を表示するDataGridViewを新規に組み立てて返す。基本構成はCreateEnemyGridと同様だが、
    // HP/幅/高さ/スケール列を持たない（ギミックはこれらのパラメータを使わないため）。
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
            new DataGridViewButtonColumn  { Name="btnHitbox", HeaderText="Hitbox", Text="🎯", UseColumnTextForButtonValue=true, FillWeight=35, ToolTipText="この行の当たり判定(Hitbox)を編集します" },
            new DataGridViewButtonColumn  { Name="btnSprite", HeaderText="📁選択", Text="📁", UseColumnTextForButtonValue=true, FillWeight=35, ToolTipText="スプライト画像ファイルを選択します" },
            new DataGridViewButtonColumn  { Name="btnDel",    HeaderText="🗑削除",  Text="🗑", UseColumnTextForButtonValue=true, FillWeight=30, ToolTipText="この行を削除します" },
        });

        // イベント配線はCreateEnemyGridとほぼ同様。DataErrorだけ追加で拾っており、
        // コンボボックスセルで想定外の値が入った際に詳細ログを残す(HandleDataError参照)。
        dgv.CellContentClick += (s, e) => HandleGridButton(dgv, e);
        dgv.SelectionChanged += (s, e) => { if (dgv.SelectedRows.Count > 0) ClearOtherSelections(AssetKind.Gimmick); UpdatePreview(dgv); UpdateBehaviorParamsPanel(dgv, isEnemy: false); UpdateTypeHint(); };
        dgv.CurrentCellDirtyStateChanged += (s, e) => { if (dgv.IsCurrentCellDirty) dgv.CommitEdit(DataGridViewDataErrorContexts.Commit); };
        dgv.CellValueChanged += (s, e) => { if (dgv.Columns[e.ColumnIndex].Name == "type_enum") { UpdateBehaviorParamsPanel(dgv, isEnemy: false); RefreshIconCell(dgv, e.RowIndex, isEnemy: false, isGimmick: true); } };
        dgv.DataError += (s, e) => HandleDataError(dgv, e);
        return dgv;
    }

    // アイテム一覧を表示するDataGridViewを新規に組み立てて返す。
    // 敵/ギミック向けの挙動パラメータパネル(pnlBehaviorParams)は使わず、grant_ability(付与能力)列を持つ点が特徴。
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
            new DataGridViewButtonColumn  { Name="btnHitbox", HeaderText="Hitbox", Text="🎯", UseColumnTextForButtonValue=true, FillWeight=35, ToolTipText="この行の当たり判定(Hitbox)を編集します" },
            new DataGridViewButtonColumn  { Name="btnSprite", HeaderText="📁選択", Text="📁", UseColumnTextForButtonValue=true, FillWeight=35, ToolTipText="スプライト画像ファイルを選択します" },
            new DataGridViewButtonColumn  { Name="btnDel",    HeaderText="🗑削除",  Text="🗑", UseColumnTextForButtonValue=true, FillWeight=30, ToolTipText="この行を削除します" },
        });

        // アイテムのSelectionChangedでは、敵/ギミックと違いUpdateBehaviorParamsPanelを呼ばない
        // （アイテムには挙動パラメータの概念が無いため）。
        dgv.CellContentClick += (s, e) => HandleGridButton(dgv, e);
        dgv.SelectionChanged += (s, e) => { if (dgv.SelectedRows.Count > 0) ClearOtherSelections(AssetKind.Item); UpdatePreview(dgv); UpdateTypeHint(); };
        dgv.CurrentCellDirtyStateChanged += (s, e) => { if (dgv.IsCurrentCellDirty) dgv.CommitEdit(DataGridViewDataErrorContexts.Commit); };
        dgv.CellValueChanged += (s, e) => { if (dgv.Columns[e.ColumnIndex].Name == "type_enum") RefreshIconCell(dgv, e.RowIndex, isEnemy: false, isGimmick: false); };
        dgv.DataError += (s, e) => HandleDataError(dgv, e);
        return dgv;
    }

    // ===== 行操作 =====
    // 敵/ギミック/アイテムいずれかのグリッドで、末尾のボタン列(btnSprite/btnHitbox/btnSize/btnDel)が
    // クリックされたときにまとめて呼ばれる共通ハンドラ。CellContentClickイベントから渡されるe.ColumnIndex
    // の列名を見て、どのボタンが押されたかをif/elseで振り分ける。3つのグリッドすべてで同じメソッドを
    // 使い回しているため、dgv引数で「今操作されたのはどのグリッドか」を受け取っている。
    private void HandleGridButton(DataGridView dgv, DataGridViewCellEventArgs e)
    {
        // 列ヘッダー行や行が存在しない位置がクリックされた場合はe.RowIndexが負になるため、その場合は何もしない
        if (e.RowIndex < 0) return;
        string colName = dgv.Columns[e.ColumnIndex].Name;

        if (colName == "btnSprite")
        {
            // スプライト画像選択ボタン。標準のファイル選択ダイアログでpng/jpg/bmp（またはすべてのファイル）を選ばせる
            using var ofd = new OpenFileDialog { Filter = "画像ファイル|*.png;*.jpg;*.bmp|すべて|*.*", Title = "スプライト画像を選択" };
            if (ofd.ShowDialog() != DialogResult.OK) return;

            // imgフォルダへコピー（同名で内容の異なるファイルは連番を付けて別名保存する。Parts-M7）
            string relPath = ImageImportHelper.CopyIntoImgFolder(projectRoot, ofd.FileName);
            // グリッドのsprite列にはプロジェクトルートからの相対パスを保存する（絶対パスをそのまま保存すると
            // 開発環境ごとにフォルダ構成が変わったときにパスが壊れてしまうため）
            dgv.Rows[e.RowIndex].Cells["sprite"].Value = relPath;
            // 選んだ直後にそのままプレビューへ反映し、選び間違いにすぐ気付けるようにする
            ShowPreview(Path.Combine(projectRoot, relPath.Replace('/', '\\')));
            lblPreviewPath.Text = relPath;
        }
        else if (colName == "btnHitbox")
        {
            // 当たり判定編集ボタン。この行の現在のスプライトパスと当たり判定4値(オフセットX/Y・幅・高さ)を
            // 読み取り、HitboxEditRequestedイベントとしてホスト側へ「編集を依頼」する。
            // このクラス自身は当たり判定エディタのUIを持たず、ホスト側が開いた編集画面の結果を
            // コールバック(第6引数のラムダ式)経由で受け取って、対応するセルへ書き戻すだけの役割に徹している。
            string spritePath = dgv.Rows[e.RowIndex].Cells["sprite"].Value?.ToString() ?? "";
            string fullPath = string.IsNullOrEmpty(spritePath) ? "" : Path.Combine(projectRoot, spritePath);
            int ox = IntCell(dgv.Rows[e.RowIndex], "hitboxOffsetX", 0);
            int oy = IntCell(dgv.Rows[e.RowIndex], "hitboxOffsetY", 0);
            int w = IntCell(dgv.Rows[e.RowIndex], "hitboxWidth", 32);
            int h = IntCell(dgv.Rows[e.RowIndex], "hitboxHeight", 32);

            HitboxEditRequested?.Invoke(fullPath, ox, oy, w, h, (rox, roy, rw, rh) =>
            {
                // ホスト側の当たり判定エディタで「OK」された結果がここに返ってくる。
                // 非表示列(hitboxOffsetX等)へそのまま書き戻すことで、保存時にReadEnemies等から拾われる。
                dgv.Rows[e.RowIndex].Cells["hitboxOffsetX"].Value = rox;
                dgv.Rows[e.RowIndex].Cells["hitboxOffsetY"].Value = roy;
                dgv.Rows[e.RowIndex].Cells["hitboxWidth"].Value = rw;
                dgv.Rows[e.RowIndex].Cells["hitboxHeight"].Value = rh;
            });
        }
        else if (colName == "btnSize")
        {
            // 表示サイズ(拡大率scale)調整ボタン。敵グリッドにしか存在しない列だが、
            // ハンドラ自体は共通なので、画像が未選択の場合のガードだけここで行っている。
            string spritePath = dgv.Rows[e.RowIndex].Cells["sprite"].Value?.ToString() ?? "";
            if (string.IsNullOrEmpty(spritePath)) { MessageBox.Show("先に画像を選択してください。", "サイズ調整", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            string fullPath = Path.Combine(projectRoot, spritePath);
            float curScale = FloatCell(dgv.Rows[e.RowIndex], "scale", 1.0f);

            // HitboxEditRequestedと同様、実際のサイズ調整UIはホスト側に委ね、結果だけscale列へ反映する
            SizeEditRequested?.Invoke(fullPath, curScale, rScale => dgv.Rows[e.RowIndex].Cells["scale"].Value = rScale);
        }
        else if (colName == "btnDel")
        {
            // 削除ボタン。誤操作防止のため、必ず確認ダイアログを挟んでからでないと削除しない
            if (MessageBox.Show("この行を削除しますか？", "確認", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                dgv.Rows.RemoveAt(e.RowIndex);
        }
    }

    // 「＋◯◯追加」ボタン群から呼ばれる、グリッドへの新規行追加の共通処理。
    // valuesは列の並び順どおりに渡された初期値の配列（GetDefaultEnemyRow等が組み立てる）。
    // 追加した行を選択状態にし、さらにスクロール位置を追加行が見える位置まで自動で動かすことで、
    // ユーザーが「今どの行が新しく増えたか」を見失わないようにしている。
    private void AddRow(DataGridView dgv, object[] values)
    {
        dgv.Rows.Add(values);
        dgv.Rows[dgv.Rows.Count - 1].Selected = true;
        dgv.FirstDisplayedScrollingRowIndex = dgv.Rows.Count - 1;
    }

    // DataGridViewComboBoxColumn(type_enum列)に、コンボボックスの選択肢一覧に存在しない値が
    // 設定されてしまった場合などに発生するDataErrorイベントのハンドラ。
    // 本来は起きてはならない状況（バグ調査対象）なので、握りつぶさずに詳細情報をログファイルへ書き出した上で
    // あえて例外を再送出(throw)し、開発中に気付けるようにしている。
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

    // dgv内の既存id("prefix"+数字)と衝突しない最小の連番idを生成する。
    // 例えば既にenemy_1・enemy_2が存在すればenemy_3を返す、という単純な採番方式。
    // 大文字小文字の違いだけのID重複も衝突とみなすため、HashSetの比較にはOrdinalIgnoreCaseを使っている。
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

    // 「＋敵追加」ボタンから呼ばれる、新規敵行の初期値一式を組み立てる。
    // 戻り値の配列は、CreateEnemyGridで定義した列の並び順(icon/id/name/type_enum/hp/width/height/...)と
    // 対応させる必要がある点に注意（順序を変えると意図しない列に値が入ってしまう）。
    // タイプは既定でEnemyTypes[0](type_enum=0: 巡回)を選んだ状態で追加される。
    private object[] GetDefaultEnemyRow()
    {
        string newId = MakeUniqueSequentialId(dgvEnemies, "enemy_");
        return new object[] { AssetIcons.ForEnemy(EnemyTypes[0].type), newId, "新敵", EnemyTypes[0].desc, 3, 32, 32, "", "画像選択", "削除" };
    }

    // 「＋ギミック追加」ボタンから呼ばれる、新規ギミック行の初期値一式。考え方はGetDefaultEnemyRowと同じ
    private object[] GetDefaultGimmickRow()
    {
        string newId = MakeUniqueSequentialId(dgvGimmicks, "gimmick_");
        return new object[] { AssetIcons.ForGimmick(GimmickTypes[0].type), newId, "新しいギミック", GimmickTypes[0].desc, "", "📁", "🗑" };
    }

    // 「＋アイテム追加」ボタンから呼ばれる、新規アイテム行の初期値一式。考え方はGetDefaultEnemyRowと同じ
    private object[] GetDefaultItemRow()
    {
        string newId = MakeUniqueSequentialId(dgvItems, "item_");
        return new object[] { AssetIcons.ForItem(ItemTypes[0].type), newId, "新しいアイテム", ItemTypes[0].desc, "", "", "📁", "🗑" };
    }

    // ===== プレビュー更新 =====
    // 引数dgvの選択中の行(1行目のみ)からsprite列の値を読み取り、右側パネルのプレビュー画像を更新する。
    // 敵/ギミック/アイテムのどのグリッドのSelectionChangedからも共通で呼ばれる想定のため、
    // sprite列自体が存在しない(＝間違ったグリッドが渡された)場合にも安全に抜けるガードを入れている。
    private void UpdatePreview(DataGridView dgv)
    {
        if (dgv.SelectedRows.Count == 0) return;
        var row = dgv.SelectedRows[0];
        if (!dgv.Columns.Contains("sprite")) return;
        string sp = row.Cells["sprite"].Value?.ToString() ?? "";
        // まだ画像パスが設定されていない行(新規追加直後など)は、プレビューを空にしてその旨を表示する
        if (string.IsNullOrEmpty(sp)) { pbPreview.Image = null; lblPreviewPath.Text = "(画像なし)"; return; }
        string fullPath = Path.Combine(projectRoot, sp.Replace('/', '\\'));
        ShowPreview(fullPath);
        lblPreviewPath.Text = sp;
    }

    // 指定したフルパスの画像ファイルを実際に読み込み、pbPreview(PictureBox)へ表示する。
    // UpdatePreview（グリッド選択変更時）とHandleGridButton内のbtnSprite処理（画像を新しく選んだ直後）の
    // 両方から呼ばれる共通の描画処理。
    private void ShowPreview(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                // Image.FromFile()ではなくFileStream経由で読み込んでいるのは、Image.FromFile()だと
                // 読み込んだ後もファイルへのロックが残ってしまい、同じ画像を別の場所からコピー/上書きしようと
                // した際に失敗することがあるため。usingでストリームを確実に閉じることでロックを残さない。
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                pbPreview.Image = Image.FromStream(fs);
                // 機能追加: UI改善 — プレビューズーム。ドット絵など元の解像度が小さい画像(幅100px未満)は
                // 等倍のままだと見づらいため、幅が160px相当になるようズーム率を自動計算して初期表示する
                // （最大8倍まで。あまりに小さい画像で極端な倍率にならないよう上限を設けている）。
                _previewZoom = 1f;
                if (pbPreview.Image.Width > 0 && pbPreview.Image.Width < 100)
                    _previewZoom = Math.Min(8f, 160f / pbPreview.Image.Width);
                ApplyPreviewZoom();
            }
            else
            {
                // ファイルが移動/削除されている等、パスはあるが実体が見つからないケース
                pbPreview.Image = null;
                lblPreviewPath.Text = "⚠ ファイルが見つかりません";
            }
        }
        // 画像として読み込めない壊れたファイル等、予期しない例外はプレビューを空にするだけで握りつぶす
        // （プレビュー表示に失敗しても編集作業自体は続行できるようにするための意図的なフォールバック）
        catch { pbPreview.Image = null; }
    }

    // ==== 機能: 敵/ギミックごとに調整可能な挙動パラメータ (Configurable Behavior Parameters, 通称 M1) ====

    // _enemyParams辞書から、この行に対応するEnemyDefを取り出す。まだ紐づいていない行（例えば
    // 「＋敵追加」で追加した直後の新規行）の場合は、この場で空のEnemyDefを新規作成して辞書へ登録してから返す。
    // これにより呼び出し側は「まだ存在するか」を気にせず常に非nullのEnemyDefを受け取れる。
    private EnemyDef GetOrCreateEnemyParams(DataGridViewRow row)
    {
        if (!_enemyParams.TryGetValue(row, out var def)) { def = new EnemyDef(); _enemyParams[row] = def; }
        return def;
    }

    // GetOrCreateEnemyParamsのギミック版。考え方は完全に同じ
    private GimmickDef GetOrCreateGimmickParams(DataGridViewRow row)
    {
        if (!_gimmickParams.TryGetValue(row, out var def)) { def = new GimmickDef(); _gimmickParams[row] = def; }
        return def;
    }

    // 機能: 複数パーツからなる複合オブジェクト (Composite Multi-Part Objects, 通称 Parts-M7)
    // GetOrCreateEnemyParamsのアイテム版。考え方は完全に同じ
    private ItemDef GetOrCreateItemParams(DataGridViewRow row)
    {
        if (!_itemParams.TryGetValue(row, out var def)) { def = new ItemDef(); _itemParams[row] = def; }
        return def;
    }

    // 機能追加: UI改善 — タブ廃止に伴い、「現在操作対象とみなす種別」をタブのインデックスではなく
    // 「どのグリッド/リストに選択中の行があるか」から判定する（同時に選択できるのは1つのみになるよう
    // 各グリッドのSelectionChangedで他を解除する。ClearOtherSelections参照）。
    private enum AssetKind { None = -1, Enemy = 0, Gimmick = 1, Item = 2, CommonEvent = 3 }

    // 敵/ギミック/アイテムの各グリッドとコモンイベントのリストボックスを順番に見ていき、
    // 実際に選択行(選択項目)が存在する最初の種別を「現在アクティブな種別」として返す。
    // どれも選択されていなければAssetKind.Noneを返す。パーツ編集・挙動スクリプト編集・複製・
    // タイプカード選択など、複数のボタンが「今どの種別を操作対象にすべきか」を判定するために使う。
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

    // 機能追加: UI改善 — グリッドのicon列を、行のtype_enum(選択中の値)に応じたAssetIconsの絵文字で更新する。
    // type_enumコンボボックスの値が変わるたび(CellValueChanged)に呼ばれ、ユーザーがタイプを切り替えたら
    // 一覧の見た目（先頭の絵文字アイコン）も即座に連動して変わるようにしている。
    // isEnemy/isGimmickの2つのbool引数で「アイテム」も含めた3種別のどれかを判別している
    // （isEnemy=false かつ isGimmick=false ならアイテム、という消去法の判定方式）。
    private void RefreshIconCell(DataGridView dgv, int rowIndex, bool isEnemy, bool isGimmick)
    {
        if (rowIndex < 0 || rowIndex >= dgv.Rows.Count) return;
        int typeEnum = GetSelectedTypeEnum(dgv.Rows[rowIndex]);
        string icon = isEnemy ? AssetIcons.ForEnemy(typeEnum) : isGimmick ? AssetIcons.ForGimmick(typeEnum) : AssetIcons.ForItem(typeEnum);
        dgv.Rows[rowIndex].Cells["icon"].Value = icon;
    }

    // 機能: ブロック(パズル)組み立て式の挙動スクリプティング (Puzzle-like Behavior Scripting, 通称 M6)
    // 現在選択中のタブ・行がtype_enum=20(敵)/24(ギミック)＝カスタムスクリプトであれば
    // ブロックエディタを開き、OKで閉じたらそのEnemyDef/GimmickDef.scriptへ書き戻す。
    // これらの定数は、EnemyTypes/GimmickTypes配列における「カスタムスクリプト」の項目のtype番号と
    // 一致させる必要がある（配列側の定義を変更した場合はここも合わせて変更しないと判定がずれる）。
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
            BehaviorScriptEditRequested?.Invoke($"敵: {row.Cells["id"].Value}", def.script, script => def.script = script);
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
            BehaviorScriptEditRequested?.Invoke($"ギミック: {row.Cells["id"].Value}", def.script, script => def.script = script);
        }
        else
        {
            MessageBox.Show("挙動スクリプトは「敵」または「ギミック」を選択してからお使いください。", "対象外", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    // 機能: 複数パーツからなる複合オブジェクト (Composite Multi-Part Objects, 通称 Parts-M7)
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
            PartsEditRequested?.Invoke($"敵: {row.Cells["id"].Value}", def.parts, sprite, parts => def.parts = parts);
        }
        else if (kind == AssetKind.Gimmick)
        {
            var row = dgvGimmicks.SelectedRows[0];
            var def = GetOrCreateGimmickParams(row);
            string sprite = row.Cells["sprite"].Value?.ToString() ?? "";
            PartsEditRequested?.Invoke($"ギミック: {row.Cells["id"].Value}", def.parts, sprite, parts => def.parts = parts);
        }
        else if (kind == AssetKind.Item)
        {
            var row = dgvItems.SelectedRows[0];
            var def = GetOrCreateItemParams(row);
            string sprite = row.Cells["sprite"].Value?.ToString() ?? "";
            PartsEditRequested?.Invoke($"アイテム: {row.Cells["id"].Value}", def.parts, sprite, parts => def.parts = parts);
        }
        else
        {
            MessageBox.Show("パーツ編集は「敵」「ギミック」「アイテム」のいずれかを選択してからお使いください。", "対象外", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    // 機能追加: UI改善（提案書 CUT-2/AM-1という項目に対応）— コンボボックスの数字入り文字列から選ぶのではなく、
    // アイコン・名前・平易な言葉での説明が並んだカード一覧をクリックしてタイプを選べるようにする。
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
        // type_enum列がコンボボックス列でなければ(想定外の状況)、以降の処理を安全にあきらめる
        if (row.Cells["type_enum"] is not DataGridViewComboBoxCell combo) return;

        // 選択中の種別に応じたタイプ一覧(EnemyTypes/GimmickTypes/ItemTypes)を、カード表示用のicon付きの
        // タプルへ変換する。アイコンはAssetIcons側の対応するFor◯◯メソッドから取得している。
        var options = kind switch
        {
            AssetKind.Enemy => EnemyTypes.Select(t => (t.type, t.desc, t.detail, icon: AssetIcons.ForEnemy(t.type))).ToList(),
            AssetKind.Gimmick => GimmickTypes.Select(t => (t.type, t.desc, t.detail, icon: AssetIcons.ForGimmick(t.type))).ToList(),
            _ => ItemTypes.Select(t => (t.type, t.desc, t.detail, icon: AssetIcons.ForItem(t.type))).ToList(),
        };
        int current = GetSelectedTypeEnum(row);
        // カード選択ダイアログをモーダルで開く。キャンセルされた場合やSelectedTypeが未設定(-1)のままの場合は何もしない
        using var picker = new TypeCardPickerForm(options, current);
        if (picker.ShowDialog() != DialogResult.OK || picker.SelectedType < 0) return;

        // 選ばれたtype番号を、既存のコンボボックス列が使っているdesc文字列(表示名)に変換して書き戻す。
        // こうすることで、カードから選んでもコンボボックスから選んでも内部的には全く同じ形式のデータになり、
        // 保存処理(ReadEnemies等)側の実装を変更する必要がない。
        var vals = (string[]?)combo.DataSource;
        if (vals == null || picker.SelectedType >= vals.Length) return;
        combo.Value = vals[picker.SelectedType];
        // タイプが変わったので、挙動パラメータパネルとアイコン表示も手動で更新しておく
        // （コンボボックスの値をコード側からセットした場合、CellValueChangedが発火しないことがあるための保険）
        UpdateBehaviorParamsPanel(dgv, isEnemy: kind == AssetKind.Enemy);
        RefreshIconCell(dgv, row.Index, isEnemy: kind == AssetKind.Enemy, isGimmick: kind == AssetKind.Gimmick);
    }

    // 選択中の敵/ギミック行のtype_enumに応じて、挙動パラメータの入力欄を動的に組み立てる。
    // 該当タイプに調整可能なパラメータが無い場合は非表示にし、従来のタイプ一覧説明を見せる。
    // EnemyParamFields/GimmickParamFields（クラス冒頭で定義した「どのtype_enumにどのフィールドを
    // 表示するか」のテーブル）を元に、リフレクション(GetProperty/GetValue/SetValue)でEnemyDef/GimmickDef
    // の該当プロパティへ直接読み書きするNumericUpDownをその場で動的に生成している。
    private void UpdateBehaviorParamsPanel(DataGridView dgv, bool isEnemy)
    {
        // 何も選択されていなければパラメータパネルを隠し、タイプ説明欄を出す
        if (dgv.SelectedRows.Count == 0) { pnlBehaviorParams.Visible = false; rtbTypeHint.Visible = true; return; }
        var row = dgv.SelectedRows[0];
        int typeEnum = GetSelectedTypeEnum(row);
        var fieldMap = isEnemy ? EnemyParamFields : GimmickParamFields;

        // 選択中のtype_enumに対応する調整可能パラメータの定義が存在しない（またはフィールド0件）場合は、
        // パラメータパネルではなく従来のタイプ一覧説明(rtbTypeHint)を表示する
        if (!fieldMap.TryGetValue(typeEnum, out var fields) || fields.Length == 0)
        {
            pnlBehaviorParams.Visible = false;
            rtbTypeHint.Visible = true;
            lblTypeHintTitle.Text = "📋 タイプ説明";
            return;
        }

        // 行に紐づくEnemyDef/GimmickDef本体（既に無ければ新規作成）を取得し、以降このオブジェクトの
        // プロパティへ直接値を読み書きする
        object paramsObj = isEnemy ? GetOrCreateEnemyParams(row) : GetOrCreateGimmickParams(row);
        // これから複数のNumericUpDownのValueをプログラム側から設定するため、その間はValueChangedの
        // 中身をスキップさせるフラグを立てておく（フィールド宣言のコメント参照。無限ループ・誤書き込み防止）
        _isUpdatingBehaviorPanel = true;
        pnlBehaviorParams.SuspendLayout();
        // 前回選択されていた行のパラメータ欄がまだ残っている可能性があるため、一旦すべて作り直す
        pnlBehaviorParams.Controls.Clear();

        // fields配列（(プロパティ名, ラベル文言, 小数点桁数)の並び）を1件ずつ、ラベル+NumericUpDownの
        // ペアとして縦に並べていく。yはこのパネル内でのY座標（次の項目を配置する高さ）を表す
        int y = 4;
        foreach (var (field, label, decimals) in fields)
        {
            // フィールド名の文字列からリフレクションでEnemyDef/GimmickDef側のプロパティ情報を取得する。
            // こうすることで、type_enumごとに専用のUIコードを1件ずつ書かずに済んでいる。
            var prop = paramsObj.GetType().GetProperty(field)!;
            var lbl = new Label { Text = label, Location = new Point(4, y + 3), Size = new Size(230, 15), Font = new Font("Meiryo UI", 7.5f) };
            var nud = new NumericUpDown
            {
                Location = new Point(4, y + 18),
                Size = new Size(140, 22),
                DecimalPlaces = decimals,
                // 小数点桁数が0(整数値)ならクリック1回で1ずつ、小数を持つ値ならその最小単位(例: 桁数2なら0.01)ずつ増減する
                Increment = decimals > 0 ? (decimal)Math.Pow(10, -decimals) : 1m,
                // 極端な値を防ぐための一律の上下限（挙動パラメータの種類ごとに個別の上下限は設けていない）
                Minimum = -100000m,
                Maximum = 100000m,
                // paramsObjの現在値をfloat→decimalへ変換して初期表示する
                Value = (decimal)Convert.ToSingle(prop.GetValue(paramsObj))
            };
            nud.ValueChanged += (s, e) =>
            {
                // プログラム側からValueを設定している最中(上のValue=...行が動いた瞬間)は無視する。
                // ここを無視しないと、初期値を設定しただけなのに「ユーザーが値を変更した」と誤判定してしまう。
                if (_isUpdatingBehaviorPanel) return;
                // EnemyDef/GimmickDef側のプロパティの型(int or float)に合わせてキャストして書き戻す
                if (prop.PropertyType == typeof(int)) prop.SetValue(paramsObj, (int)nud.Value);
                else prop.SetValue(paramsObj, (float)nud.Value);
            };
            pnlBehaviorParams.Controls.Add(lbl);
            pnlBehaviorParams.Controls.Add(nud);
            // 次の項目はラベル+入力欄ぶんの高さ(42px)だけ下にずらして配置する
            y += 42;
        }

        pnlBehaviorParams.ResumeLayout();
        // 全ての初期値設定が終わったので、以降のユーザー操作によるValueChangedは正常に処理されるようフラグを戻す
        _isUpdatingBehaviorPanel = false;

        // タイプ説明欄を隠してパラメータパネルを表示し、見出しラベルの文言も切り替える
        rtbTypeHint.Visible = false;
        pnlBehaviorParams.Visible = true;
        lblTypeHintTitle.Text = "⚙ 挙動パラメータ";
    }

    // 右サイドパネルの「📋 タイプ説明」欄(rtbTypeHint)の中身を、現在アクティブな種別(GetActiveKind)に応じて
    // 丸ごと書き直す。RichTextBoxのSelectionFont/SelectionColorを使い、見出し部分は太字+種別ごとの色、
    // 説明本文はグレーの小さめフォント、という体裁を種別ごとのdesc/detail一覧を1件ずつ流し込んで再現している。
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
    // コンストラクタから一度だけ呼ばれる。assets(コンストラクタで渡された既存の定義データ)の内容を
    // 敵/ギミック/アイテムそれぞれのDataGridViewへ1行ずつ流し込んで、グリッドの表示を初期化する。
    // コモンイベント側は既にコンストラクタで_commonEventsへ複製済みのため、ここでは扱わない
    // （表示自体はInitUI内のRefreshCommonEventsList呼び出しで行われる）。
    private void LoadData()
    {
        dgvEnemies.Rows.Clear();
        _enemyParams.Clear();
        foreach (var e in assets.Enemies)
        {
            // 保存済みJSON側のtype_enumが、現在のEnemyTypes定義に存在しない番号になっている場合
            // （例えば古いバージョンで定義されていたタイプ番号を削除した後にファイルを読み込んだ場合など）に
            // 備えたフォールバック処理。コンボボックスのDataSourceに存在しない値を設定すると
            // DataError（HandleDataError参照）が発生してしまうため、警告ログを残した上でtype_enum=0に丸める。
            string typeLabel = EnemyTypes.FirstOrDefault(t => t.type == e.type_enum).desc;
            if (string.IsNullOrEmpty(typeLabel) || !EnemyTypes.Any(t => t.desc == typeLabel))
            {
                System.IO.File.AppendAllText(Path.Combine(AppPaths.LogsDir, "warning_log.txt"), $"[WARNING] AssetManagerForm: Enemy ID '{e.id}' has invalid type_enum '{e.type_enum}'. Auto-converted to default.\n");
                typeLabel = EnemyTypes[0].desc;
            }
            dgvEnemies.Rows.Add(AssetIcons.ForEnemy(e.type_enum), e.id, e.name, typeLabel, e.hp, e.width, e.height, e.hitboxOffsetX, e.hitboxOffsetY, e.hitboxWidth, e.hitboxHeight, e.scale, e.sprite, "🎯", "📏", "📁", "🗑");
            // 機能: 敵/ギミックごとに調整可能な挙動パラメータ (M1) — 行オブジェクトに紐づけて挙動パラメータ本体を保持する。
            // ここでは新しくEnemyDefを作り直すのではなく、assetsから読み込んだeそのものを紐づけている点に注意
            // （こうしないと、JSONに保存されていた挙動パラメータ・SE設定・スクリプト等の情報が失われてしまう）
            _enemyParams[dgvEnemies.Rows[dgvEnemies.Rows.Count - 1]] = e;
        }

        dgvGimmicks.Rows.Clear();
        _gimmickParams.Clear();
        foreach (var g in assets.Gimmicks)
        {
            // 敵と同様、type_enumが現在の定義に存在しない場合のフォールバック（詳細は敵側のコメント参照）
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
            // 敵・ギミックと同様のフォールバック処理（詳細は敵側のコメント参照）
            string typeLabel = ItemTypes.FirstOrDefault(t => t.type == i.type_enum).desc;
            if (string.IsNullOrEmpty(typeLabel) || !ItemTypes.Any(t => t.desc == typeLabel))
            {
                System.IO.File.AppendAllText(Path.Combine(AppPaths.LogsDir, "warning_log.txt"), $"[WARNING] AssetManagerForm: Item ID '{i.id}' has invalid type_enum '{i.type_enum}'. Auto-converted to default.\n");
                typeLabel = ItemTypes[0].desc;
            }
            dgvItems.Rows.Add(AssetIcons.ForItem(i.type_enum), i.id, i.name, typeLabel, i.hitboxOffsetX, i.hitboxOffsetY, i.hitboxWidth, i.hitboxHeight, i.sprite, i.grant_ability, "🎯", "📁", "🗑");
            // 機能: 複数パーツからなる複合オブジェクト (Parts-M7) — 行オブジェクトに紐づけて非グリッド項目(parts等)を保持する
            _itemParams[dgvItems.Rows[dgvItems.Rows.Count - 1]] = i;
        }
    }

    // 機能追加: UI改善（提案書のCUT-3という項目に対応）— ID重複や画像未設定のまま保存すると、ステージ側からの参照が
    // 意図せず別の定義を指してしまったり、ゲーム内で表示されないままになったりして気づきにくい。
    // そこで保存の直前にこのメソッドで軽い自己診断を行い、問題があれば警告文の一覧を返す
    // （呼び出し元のBtnSave_Clickでは、警告が1件でもあればユーザーに続行するかどうかを確認する）。
    // 戻り値の警告文は「保存を止める」ものではなく、あくまで注意喚起であることに留意（保存自体は続行できる）。
    private static List<string> ValidateAssets(List<EnemyDef> enemies, List<GimmickDef> gimmicks, List<ItemDef> items, List<CommonEventDef> commonEvents)
    {
        var warnings = new List<string>();

        // 同じ種別のリスト内でidが重複している項目をまとめて検出するローカル関数。
        // idSelでリスト要素からid文字列を取り出す方法を渡すことで、敵/ギミック/アイテム/コモンイベントの
        // 4種類すべてに対して同じロジックを使い回している。
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

        // 画像パスが空のまま保存しようとしている敵を検出する（ギミック/アイテムは画像が無くても
        // 致命的ではないケースがあるため、ここでは敵のみをチェック対象にしている）
        var noSpriteEnemies = enemies.Where(e => string.IsNullOrWhiteSpace(e.sprite)).Select(e => e.id).ToList();
        if (noSpriteEnemies.Count > 0)
            warnings.Add($"画像が未設定の敵があります (ID: {string.Join(", ", noSpriteEnemies)})。ゲーム内で表示されません。");

        return warnings;
    }

    // ===== 保存 =====
    // 「💾 保存して閉じる」ボタンのクリックハンドラ。グリッドの内容をEnemyDef/GimmickDef/ItemDefの
    // リストへ変換し、ValidateAssetsで軽く自己診断してから、実際にassets.SaveToFolderでJSONへ書き出す。
    private void BtnSave_Click(object? sender, EventArgs e)
    {
        var enemies = ReadEnemies();
        var gimmicks = ReadGimmicks();
        var items = ReadItems();

        // 保存前バリデーション。ID重複や画像未設定などの警告があれば、内容を一覧表示した上で
        // 「それでも保存するか」をユーザーに確認する。警告はあくまで注意喚起であり保存を強制的に止めはしない。
        var warnings = ValidateAssets(enemies, gimmicks, items, _commonEvents);
        if (warnings.Count > 0)
        {
            // 警告が大量にある場合にメッセージボックスが際限なく縦長にならないよう、先頭8件だけ表示し
            // それ以上は「…他◯件」という形でまとめる
            string msg = "保存前に確認してください:\n\n" +
                string.Join("\n", warnings.Take(8)) +
                (warnings.Count > 8 ? $"\n…他{warnings.Count - 8}件" : "") +
                "\n\nこのまま保存しますか？";
            if (MessageBox.Show(msg, "確認", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        }

        // 読み取ったリストをassets本体へ反映し、assetsPathフォルダへJSONとして書き出す
        assets.Enemies = enemies;
        assets.Gimmicks = gimmicks;
        assets.Items = items;
        assets.CommonEvents = _commonEvents;
        assets.SaveToFolder(assetsPath);
        MessageBox.Show("アセット定義を保存しました！\n\n※画像はimgフォルダへコピー済みです。\nゲームを再ビルドすると新しいスプライトが反映されます。",
            "保存完了", MessageBoxButtons.OK, MessageBoxIcon.Information);
        // このUserControlはFormのClose()を持たないため、保存完了をホスト側に伝えるためのイベントを発火する
        // （クラス冒頭のコメント参照。ホスト側がこれを購読してForm.Close()やGoBack()を呼ぶ）
        Saved?.Invoke(this, EventArgs.Empty);
    }

    // 敵グリッド(dgvEnemies)の全行を読み取り、EnemyDefのリストへ変換する。BtnSave_Clickから呼ばれる。
    private List<EnemyDef> ReadEnemies()
    {
        var list = new List<EnemyDef>();
        foreach (DataGridViewRow row in dgvEnemies.Rows)
        {
            // DataGridViewは末尾に「新規行を入力するための空行」を自動的に持つため、それは読み飛ばす
            if (row.IsNewRow) continue;
            string? id = row.Cells["id"].Value?.ToString();
            // idが空の行（データが実質的に未入力の行）は保存対象から除外する
            if (string.IsNullOrWhiteSpace(id)) continue;

            // type_enum: コンボボックスの選択インデックスから取得。
            // 以下の2段階の処理になっているのは、
            // (1)まずdesc文字列に含まれる番号や単語からのゆるい文字列一致で候補を探し、
            // (2)その後、実際のDataGridViewComboBoxCellとしての選択値インデックス取得を試みて、
            //    見つかればそちらの結果で上書きする、というフォールバック構成になっているため。
            // 通常時は(2)が必ず成功して(1)の結果を上書きするが、何らかの理由でセルがコンボボックスとして
            // 認識できない状況（例外的なケース）でも(1)の緩い一致により大きく外れた値にはなりにくいよう
            // 保険をかけている。
            int typeIdx = 0;
            string typeStr = row.Cells["type_enum"].Value?.ToString() ?? "";
            for (int i = 0; i < EnemyTypes.Length; i++)
                if (EnemyTypes[i].desc.Split('=')[1].Trim().Split(' ')[0] == typeStr ||
                    typeStr == i.ToString() || EnemyTypes[i].desc.Contains(typeStr)) { typeIdx = i; break; }
            // ComboBoxのインデックスで取得試み（こちらが取得できれば上の緩い一致より優先して採用する）
            if (row.Cells["type_enum"] is DataGridViewComboBoxCell combo)
            {
                var vals = (string[]?)combo.DataSource;
                if (vals != null)
                {
                    int foundIdx = Array.IndexOf(vals, combo.Value?.ToString() ?? "");
                    if (foundIdx >= 0) typeIdx = foundIdx;
                }
            }

            // 機能: 敵/ギミックごとに調整可能な挙動パラメータ (M1) — 行に紐づく保持済みEnemyDef（挙動パラメータ・SE等を保持）を
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

    // ギミックグリッド(dgvGimmicks)の全行を読み取り、GimmickDefのリストへ変換する。
    // 敵側と異なり、type_enumはComboBoxセルからのインデックス取得のみ（緩い文字列一致のフォールバックは無い）。
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

            // 機能: 敵/ギミックごとに調整可能な挙動パラメータ (M1) — 行に紐づく保持済みGimmickDef（挙動パラメータ・SE等を保持）を
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

    // アイテムグリッド(dgvItems)の全行を読み取り、ItemDefのリストへ変換する。考え方はReadGimmicksとほぼ同じだが、
    // grant_ability(付与能力)列も併せて読み取る点がアイテム固有。
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

            // 機能: 複数パーツからなる複合オブジェクト (Parts-M7) — 行に紐づく保持済みItemDef（parts等）を
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

    // 指定した列のセル値を安全にintへ変換する。数値として解釈できない場合(空文字・不正な文字列等)はdefを返す
    private static int IntCell(DataGridViewRow row, string col, int def = 0)
        => int.TryParse(row.Cells[col].Value?.ToString(), out var v) ? v : def;

    // IntCellのfloat版。scale等の小数値を持つ列の読み取りに使う
    private static float FloatCell(DataGridViewRow row, string col, float def = 0f)
        => float.TryParse(row.Cells[col].Value?.ToString(), out var v) ? v : def;

    // ===== コモンイベント =====
    // 機能追加: UI改善（提案書のAM-4という項目に対応）— 件数だけでなく「何をするイベントか」がタイトルだけで
    // 一目で分かるよう、実行内容(アクション種別)を矢印でつないだ要約を添える。
    // _commonEventsリストの内容を丸ごと読み直してlstCommonEventsの表示項目を作り直す（差分更新はしない）。
    private void RefreshCommonEventsList()
    {
        lstCommonEvents.Items.Clear();
        foreach (var ce in _commonEvents)
        {
            // アクションが1つも登録されていなければその旨を、そうでなければ先頭4件までのアクション名を
            // 「→」でつないだ要約文字列を作る。5件以上ある場合は末尾に「…」を付けて省略を示す。
            string summary = ce.actions.Count == 0
                ? "(実行内容が未設定)"
                : string.Join("→", ce.actions.Take(4).Select(a => a.action)) + (ce.actions.Count > 4 ? "…" : "");
            lstCommonEvents.Items.Add($"🔔 {ce.id} : {ce.name}  【{summary}】");
        }
    }

    // 「＋コモンイベント追加」ボタンから呼ばれる。まず衝突しない連番のidを決めた上で空のCommonEventDefを作り、
    // CommonEventEditRequestedイベント経由でホスト側に編集画面を開いてもらう。
    // 敵/ギミック/アイテムの追加(AddRow)と違い即座にグリッドへ行を足すのではなく、先に編集画面を開かせて
    // OKされた結果だけを_commonEventsへ追加する、という一手間多い流れになっている
    // （コモンイベントはアクション列を持たないリスト表示のため、詳細内容は必ず専用の編集画面で組み立てる必要があるため）。
    private void AddCommonEvent()
    {
        int n = _commonEvents.Count + 1;
        string newId = $"common_event_{n}";
        while (_commonEvents.Any(c => c.id == newId)) { n++; newId = $"common_event_{n}"; }

        var newEvent = new CommonEventDef { id = newId, name = "新しいコモンイベント" };
        CommonEventEditRequested?.Invoke(newEvent, result =>
        {
            _commonEvents.Add(result);
            RefreshCommonEventsList();
        });
    }

    // リストボックスのダブルクリック、または「✎ 編集」ボタンから呼ばれる。
    // 選択中のコモンイベントをホスト側の編集画面に渡し、OKされたら_commonEventsの該当要素を置き換える。
    private void EditSelectedCommonEvent()
    {
        int idx = lstCommonEvents.SelectedIndex;
        if (idx < 0 || idx >= _commonEvents.Count) { MessageBox.Show("編集するコモンイベントを選択してください。"); return; }

        CommonEventEditRequested?.Invoke(_commonEvents[idx], result =>
        {
            _commonEvents[idx] = result;
            RefreshCommonEventsList();
            // リスト再構築で選択状態が失われるため、編集していた項目を選択したままにしておく
            lstCommonEvents.SelectedIndex = idx;
        });
    }

    // 「🗑 削除」ボタンから呼ばれる。誤操作防止のため確認ダイアログを挟んでから_commonEventsリストから取り除く
    private void DeleteSelectedCommonEvent()
    {
        int idx = lstCommonEvents.SelectedIndex;
        if (idx < 0 || idx >= _commonEvents.Count) { MessageBox.Show("削除するコモンイベントを選択してください。"); return; }
        if (MessageBox.Show($"コモンイベント「{_commonEvents[idx].id}」を削除しますか？", "確認", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        _commonEvents.RemoveAt(idx);
        RefreshCommonEventsList();
    }

    // ===== 検索フィルタ (MZ風 ID/名前検索) =====
    // 機能追加: UI改善 — タブ廃止により全種別が同時に画面上へ並ぶため、検索は表示中の全グリッド/一覧へ横断的に適用する。
    // txtSearchのTextChangedイベントから呼ばれ、入力するたびリアルタイムに絞り込みを更新する。
    private void ApplySearchFilter()
    {
        string q = txtSearch.Text.Trim();
        FilterGrid(dgvEnemies, q);
        FilterGrid(dgvGimmicks, q);
        FilterGrid(dgvItems, q);
        FilterCommonEventsList(q);
    }

    // 敵/ギミック/アイテムのいずれかのグリッドに対し、id列またはname列にqueryを含まない行を非表示(Visible=false)にする。
    // 行そのものを削除するわけではないため、検索欄を空にすればすぐに全件表示へ戻せる。
    private static void FilterGrid(DataGridView dgv, string query)
    {
        foreach (DataGridViewRow row in dgv.Rows)
        {
            if (row.IsNewRow) continue;
            // 検索文字列が空なら絞り込みなし（全行表示）
            if (string.IsNullOrEmpty(query)) { row.Visible = true; continue; }
            string id = row.Cells["id"].Value?.ToString() ?? "";
            string name = dgv.Columns.Contains("name") ? row.Cells["name"].Value?.ToString() ?? "" : "";
            row.Visible = id.Contains(query, StringComparison.OrdinalIgnoreCase) || name.Contains(query, StringComparison.OrdinalIgnoreCase);
        }
    }

    // コモンイベントのリストボックス用の絞り込み。DataGridViewのように行単位でVisibleを切り替える仕組みが
    // ListBoxには無いため、こちらは該当する項目だけを毎回作り直して再表示する方式にしている
    // （RefreshCommonEventsListとほぼ同じ表示処理だが、絞り込み後は簡易表示（要約文なし・件数表示のみ）になる）。
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
    // ツールバーの「⧉ 選択行を複製」ボタンから呼ばれる共通の入り口。GetActiveKindで「今どの種別が
    // 選択されているか」を判定し、それぞれの種別専用の複製処理へ振り分ける。
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

    // 複製元のbaseIdを元に、"baseId_copy"→衝突していれば"baseId_copy2"→"baseId_copy3"…という具合に
    // 衝突しない複製先idを決める。MakeUniqueSequentialId（新規追加用の連番採番）とは用途が異なるための別実装。
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

    // 敵グリッドで選択中の行を複製する。表示列の値をそのままコピーした新しい行をグリッドへ追加した上で、
    // _enemyParamsに保持している非表示の挙動パラメータ本体もJSONシリアライズ/デシリアライズを介して
    // ディープコピーする（単純に代入するだけだと新旧の行が同じEnemyDefインスタンスを共有してしまい、
    // 片方を編集するともう片方も変わってしまうため）。
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
        // 機能: 敵/ギミックごとに調整可能な挙動パラメータ (M1) — 挙動パラメータもディープコピーして複製する。
        // JSON経由での往復変換（クラス冒頭のコメント参照）を使うことで、参照を共有しない独立したコピーを作れる。
        var srcDef = GetOrCreateEnemyParams(r);
        _enemyParams[dgvEnemies.Rows[dgvEnemies.Rows.Count - 1]] = JsonConvert.DeserializeObject<EnemyDef>(JsonConvert.SerializeObject(srcDef))!;
    }

    // ギミックグリッドで選択中の行を複製する。考え方はDuplicateEnemyRowと同じ
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
        // 機能: 敵/ギミックごとに調整可能な挙動パラメータ (M1) — 挙動パラメータもディープコピーして複製する
        var srcDef = GetOrCreateGimmickParams(r);
        _gimmickParams[dgvGimmicks.Rows[dgvGimmicks.Rows.Count - 1]] = JsonConvert.DeserializeObject<GimmickDef>(JsonConvert.SerializeObject(srcDef))!;
    }

    // アイテムグリッドで選択中の行を複製する。考え方はDuplicateEnemyRowと同じ
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
        // 機能: 複数パーツからなる複合オブジェクト (Parts-M7) — parts等の非グリッド項目も複製する
        var srcDef = GetOrCreateItemParams(r);
        _itemParams[dgvItems.Rows[dgvItems.Rows.Count - 1]] = JsonConvert.DeserializeObject<ItemDef>(JsonConvert.SerializeObject(srcDef))!;
    }

    // コモンイベントリストで選択中の項目を複製する。グリッド系と違いDataGridViewRowを使わないため、
    // JSONシリアライズではなくactionsリストの要素を1件ずつ新しいEventActionEntryへコピーする方式にしている。
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
// TypeCardPickerForm - type_enumを数字入りの文字列から選ぶのではなく、アイコン・名前・平易な説明が
// 並んだ「カード」を一覧表示し、クリックするだけでタイプを選べるようにするための単純なモーダルダイアログ。
// 機能追加: UI改善（提案書のCUT-2/AM-1という項目に対応）
// BtnTypeCardPicker_Click（AssetManagerPageControl側）から呼び出され、選ばれた結果はSelectedTypeプロパティ
// 経由で呼び出し元へ伝わる（DialogResult.OKかどうかも合わせて確認される想定）。
// ======================================================
public class TypeCardPickerForm : Form
{
    // ユーザーがクリックして選んだtype番号。何も選ばれていない(キャンセルされた等)場合は-1のまま
    public int SelectedType { get; private set; } = -1;

    // options: カードとして並べるタイプの一覧 (type番号, 表示名, 詳細説明, 絵文字アイコン) のタプル配列
    // currentType: 現在選択されている（呼び出し元で選ばれていた）type番号。該当するカードをハイライト表示するために使う
    public TypeCardPickerForm(List<(int type, string desc, string detail, string icon)> options, int currentType)
    {
        Text = "🔍 タイプをカードから選ぶ";
        Size = new Size(560, 640);
        MinimumSize = new Size(420, 360);
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Meiryo UI", 9);

        // 画面上部の操作案内ラベル
        var lblHint = new Label
        {
            Dock = DockStyle.Top,
            Height = 30,
            Padding = new Padding(8, 6, 8, 0),
            Text = "カードをクリックすると、そのタイプを選んで閉じます。",
            Font = new Font(Font.FontFamily, 8f),
            ForeColor = Color.DarkSlateGray,
        };

        // カードを縦に並べるスクロール可能なリストパネル
        var pnlList = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            Padding = new Padding(8),
        };

        // optionsの1件ずつを「見出し(アイコン+タイプ名)＋詳細説明」を縦に積んだ1枚のカード(Panel)として組み立てる
        foreach (var opt in options)
        {
            // 現在選択中のtype番号と一致するカードは背景色を薄い黄色にして目立たせ、「（現在選択中）」の文言も添える
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

            // このカードがクリックされたときにSelectedTypeへ結果を設定し、DialogResult.OKでダイアログを閉じる
            // ローカル関数。foreachのイテレーション変数(opt.type)をラムダ式内でそのまま使うと、ループが進むたびに
            // 参照先が変わって全カードが最後の値を指してしまう問題があるため、capturedTypeへ一度コピーしてから使う。
            int capturedType = opt.type;
            void Choose()
            {
                SelectedType = capturedType;
                DialogResult = DialogResult.OK;
                Close();
            }
            card.Click += (s, e) => Choose();
            // カード自体だけでなく、その中の見出しラベル・詳細ラベル等の子孫コントロール上をクリックしても
            // 同じように選択が反応するよう、再帰的に取得した全子孫コントロールにもClickハンドラを配線する
            // （そうしないと、ラベルの文字の上をクリックしたときだけ反応しない、という体験になってしまう）。
            foreach (var c in AllDescendants(card)) c.Click += (s, e) => Choose();
            // カーソルも同様に、カード全体のどこにマウスを乗せても「クリックできる」ことが分かる手の形にする
            foreach (var c in AllDescendants(card)) c.Cursor = Cursors.Hand;

            pnlList.Controls.Add(card);
        }

        // 下部の「キャンセル」ボタン。押すとDialogResult.Cancelでダイアログが閉じ、SelectedTypeは-1のままになる
        var pnlBottom = new Panel { Dock = DockStyle.Bottom, Height = 46 };
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) };
        var btnCancel = new Button { Text = "キャンセル", DialogResult = DialogResult.Cancel, AutoSize = true, Padding = new Padding(10, 5, 10, 5) };
        flow.Controls.Add(btnCancel);
        pnlBottom.Controls.Add(flow);
        // CancelButtonに設定しておくことで、Escキーを押した場合もキャンセル扱いで閉じるようになる
        CancelButton = btnCancel;

        Controls.Add(pnlList);
        Controls.Add(pnlBottom);
        Controls.Add(lblHint);
    }

    // rootの配下にある全てのコントロールを、階層の深さに関わらず再帰的に列挙するヘルパー。
    // カード(Panel)の中にFlowLayoutPanel、その中にLabelが2つ、という入れ子構造の全てへ
    // 同じイベントハンドラやカーソル設定を一括で配線するために使っている。
    private static IEnumerable<Control> AllDescendants(Control root)
    {
        foreach (Control c in root.Controls)
        {
            yield return c;
            foreach (var d in AllDescendants(c)) yield return d;
        }
    }
}
