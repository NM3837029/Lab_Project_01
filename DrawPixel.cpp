#include "DxLib.h"
#include <vector>
#include <stdio.h>
#include <fstream>
#include <string>
#include <map>
#include <cstdlib>
#include "json.hpp"
#include "Logger.h"
#include "GamePaths.h"
#include "GameConfig.h"
#include <exception>

#include "imgui.h"
#include "imgui_impl_win32.h"
#include "imgui_impl_dx11.h"
#include <d3d11.h>



// ImGui(Dear ImGui)側が提供しているWin32用メッセージハンドラの宣言。
// DxLibが作成したウィンドウに届くWindowsメッセージ（マウス/キーボード入力等）を
// ImGuiにも渡してあげないと、ImGuiで作ったUI（デバッグ用エディタ画面など）が操作できなくなるため必要。
extern IMGUI_IMPL_API LRESULT ImGui_ImplWin32_WndProcHandler(HWND hWnd, UINT msg, WPARAM wParam, LPARAM lParam);

// このゲームが使う独自のウィンドウプロシージャ（Windowsからのメッセージを受け取る窓口関数）。
// DxLibのデフォルトの処理の前に、まずImGuiへメッセージを渡してUI操作を成立させている。
// ImGui側がメッセージを「自分が処理した」と判断した場合はtrue相当の値を返し、
// そうでなければ0を返して後続の通常処理に委ねる。
LRESULT CALLBACK CustomWndProc(HWND hwnd, UINT msg, WPARAM wParam, LPARAM lParam) {
    if (ImGui_ImplWin32_WndProcHandler(hwnd, msg, wParam, lParam))
        return true;
    return 0;
}

// true の場合、このプログラムを「ゲーム本編」ではなく「Lab_Editor専用のアセット編集モード」として起動する。
// 起動時の引数やモード判定によって切り替えられ、ゲームループと編集用UIの挙動を分岐させるために使う。
bool isDedicatedEditorMode = false;

// 警告番号26800（「ムーブ済みの可能性があるオブジェクトの使用」等、C++コアガイドラインの警告）を無効化する。
// このプロジェクトでは意図的にムーブ後の変数を再利用する箇所があるため、誤検知の警告を抑制している。
#pragma warning(disable: 26800)
#include "SoundManager.h"
#include "EventManager.h"
#include "AnimationController.h"
#include "BehaviorScript.h"

// ======================================================
// Feature: Composite Multi-Part Objects (Parts-M1)
// 敵/ギミック/アイテムを、複数の画像パーツの組み合わせとして構成するためのテンプレート/ランタイム構造体。
// 例: 開閉する扉の各パネル、ファイアバーの各火の玉、体が分かれた敵の各部位。
// parts配列が空のままなら既存のenemies.json/gimmicks.json/items.jsonは完全に無変更で動作する。
// ======================================================
struct PartDef {
    std::string id = "";           // このパーツを識別するための名前（スクリプトから参照する際のキーにもなる）
    std::string sprite_path = "";  // パーツの見た目に使う画像ファイルのパス
    int graphHandle = -1;          // sprite_pathをLoadGraphした結果のハンドル（読み込み前は-1のまま）
    float offsetX = 0.0f, offsetY = 0.0f; // 親からの初期相対オフセット（見た目のアンカー）
    int width = 0, height = 0;             // 既定=画像サイズ
    int hitboxOffsetX = 0, hitboxOffsetY = 0; // パーツ座標から当たり判定矩形までのオフセット
    int hitboxWidth = 32, hitboxHeight = 32;  // 当たり判定矩形のサイズ
    float scale = 1.0f;   // 表示倍率（1.0=等倍）
    int hp = 0;      // 0=破壊不能(常在ハザード) / 1以上=個別に破壊可能
    int zOrder = 0;  // 負=親より奥に描画、正=親より手前
    // ギミックのパーツがプレイヤーに接触ダメージを与えるかどうか。
    //
    // 敵のパーツは元から「触れたら1ダメージ」だが、ギミックのパーツには接触判定が無く、
    // 回転する棘の輪・ファイアバー・振り子といった「見るからに危険な回転ハザード」が
    // 実際には素通りできる飾りでしかなかった。見た目と機能が食い違うと
    // プレイヤーは何を避ければいいのか学習できないため、明示的に危険だと宣言できるようにする。
    //
    // 既定はfalse。扉の板やアイテムの装飾オーブのように「当たっても何も起きない」パーツが
    // 大半なので、危険にしたいパーツだけJSONで deadly:true を書く方式にしてある。
    bool deadly = false;
    json script = json::array(); // このパーツ専用の行動スクリプト（JSON形式のAST。未使用なら空配列のまま）
};

// ランタイム側のパーツ状態。PartDefへのポインタは持たず、スポーン時に値をコピーする
// （この構造体を長時間保持するEnemy/Gimmick/Itemが、既存のenemyDefs等と同じく
//   毎フレームdefを再検索する慣習に合わせるため。詳細はプラン文書を参照）。
struct PartInstance {
    float x = 0.0f, y = 0.0f; // 見た目のアンカー座標（世界座標）。hitboxOffsetは焼き込まず判定のたびに加算する
    int handle = -1;          // 描画に使う画像ハンドル（PartDef.graphHandleをコピーしたもの）
    float scale = 1.0f;       // 表示倍率
    float angle = 0.0f;       // 回転角度（ラジアン）。スクリプト等で動的に変化させる
    int hp = 0;                // 残り耐久力（0のままなら破壊不能パーツ）
    bool isActive = true;      // falseになったら破壊済み扱いで描画・当たり判定の対象から外す
    int hitboxOffsetX = 0, hitboxOffsetY = 0, hitboxWidth = 32, hitboxHeight = 32; // 当たり判定用の矩形情報（PartDefからコピー）
    int width = 0, height = 0; // 表示サイズ（PartDefからコピー）
    int zOrder = 0;            // 描画順（負=親より奥、正=親より手前）
    bool deadly = false;       // 接触ダメージを与えるパーツか（PartDefからコピー）
    int partIndex = 0;         // 親のparts[]内インデックス（PartIndexレポーター、Start()時の再検索に使う）
    ScriptState scriptState;   // OnSpawn用
    ScriptState reactiveState; // OnDamaged/OnDeath専用（Parts-M6）

    // ---- 複合オブジェクトのパーツ追従（親の拡大・傾けへの連動）----
    // x/y はワールド座標だが、それは「ローカルオフセット＋親の姿勢」から毎フレーム組み直した
    // 導出値であって真の値ではない。真の値はこの localX/localY のほう。
    // ワールド座標を積み上げていく方式にすると回転の反復合成で誤差が溜まり、
    // 編集を元に戻してもパーツが元の位置へ帰ってこなくなるため、ローカルを正とする。
    float localX = 0.0f, localY = 0.0f;
    // 直前に ApplyPartsParentPose が書き込んだワールド座標。
    // 「スクリプトがワールド座標を直接書いたかどうか」をこれとの一致で判定する。
    // 一致していれば誰も触っていないので localX/localY をそのまま信用でき、
    // 親の姿勢が変わったフレームでもパーツは正しく追従する。
    float appliedX = 0.0f, appliedY = 0.0f;
    // このフレームの親の姿勢（描画と当たり判定が同じ値を見るようここへ写す）。
    float parentScaleX = 1.0f, parentScaleY = 1.0f; // 親の横・縦倍率
    float parentTilt    = 0.0f;                      // 親の配置時からの傾き（描画角度に加算する）
    float parentUniform = 1.0f;                      // パーツ自身の表示倍率・判定サイズに掛ける一様倍率
};

// 敵1種類分の「定義データ」。enemies.jsonから読み込まれる、いわば敵の設計図。
// 実際にステージへ配置された1体1体の状態はPlacedEnemy／実行時のEnemyクラス側が持ち、
// このEnemyDefはあくまで「この種類の敵は何ができるか」を表す共通データとして参照される。
struct EnemyDef {
    std::string id = "";           // 敵の種類を一意に識別するID文字列（ステージデータからの参照キー）
    std::string name = "";         // エディタ上に表示する人間向けの名前
    int type_enum = 0;             // 敵の行動タイプ（PATROL/JUMPER/CHASER等）を表す番号。AssetManagerForm.csの並び順と対応
    int hp = 0;                    // 初期HP（耐久力）
    int width = 0;                 // 表示幅（画像読み込み後に実サイズへ上書きされる）
    int height = 0;                // 表示高さ（画像読み込み後に実サイズへ上書きされる）
    std::string sprite_path = "";  // 見た目に使う画像ファイルのパス
    int graphHandle = -1;          // sprite_pathをLoadGraphした結果のハンドル（未読込時は-1）
    std::string seSpawn = "";      // 出現時に鳴らす効果音ファイル名
    std::string seAttack = "";     // 攻撃時に鳴らす効果音ファイル名
    std::string seDamage = "";     // 被ダメージ時に鳴らす効果音ファイル名
    std::string seDeath = "";      // 撃破時に鳴らす効果音ファイル名
    int hitboxOffsetX = 0;         // 表示座標から当たり判定矩形までのXオフセット
    int hitboxOffsetY = 0;         // 表示座標から当たり判定矩形までのYオフセット
    int hitboxWidth = 32;          // 当たり判定矩形の幅
    int hitboxHeight = 32;         // 当たり判定矩形の高さ
    float scale = 1.0f; // Visual Size Editor で設定される表示スケール倍率

    // ==== Feature: Configurable Behavior Parameters (M1) ====
    // 各フィールドは既定値 -1 (未設定) の場合、そのtype_enumの従来のハードコード挙動を
    // そのまま再現する値がLoadAssetDefinitions()で補完される。既存のenemies.jsonは無変更で動作する。
    float moveSpeed = -1.0f;         // PATROL/WALKER/CHASER/FLOATER/SHRINKER/SHIELD/SIZE_SHIFTER/TEMPO_WARPER/BRIGHTNESS_PHANTOM/COLOR_SHIFTER の移動速度係数(baseSpeedへの乗数)
    float enragedMoveSpeed = -1.0f;  // SHRINKER: 覚醒(縮小)後の移動速度係数
    float actionInterval = -1.0f;    // JUMPER/STATIONARY/PATROL_SHOOTER/SPREAD_SHOOTER/AIMED_SHOOTER/TELEPORTERの周期的行動の間隔(フレーム)
    float jumpPowerMult = -1.0f;     // JUMPER/CHASER のジャンプ力係数(baseJumpPowerへの乗数)
    float triggerRange = -1.0f;      // DASH_CHARGERの発動距離 / FALLERの真下判定幅 / PATROL_SHOOTERの索敵X範囲
    float detectionRangeY = -1.0f;   // PATROL_SHOOTERの索敵Y範囲
    float projectileSpeed = -1.0f;   // 各種射撃タイプの弾速係数(BULLET_SPEEDへの乗数)
    float chargeTime = -1.0f;        // DASH_CHARGERの溜め時間(フレーム)
    float dashSpeedMult = -1.0f;     // DASH_CHARGERの突進速度係数(DASH_SPEEDへの乗数)
    float dashDuration = -1.0f;      // DASH_CHARGERの突進継続時間(フレーム)
    float cooldownTime = -1.0f;      // DASH_CHARGERの突進後クールダウン / FALLERの着地後クールダウン(フレーム)
    float fallDelay = -1.0f;         // FALLERの落下開始までの遅延(フレーム)
    float spreadAngle = -1.0f;       // SPREAD_SHOOTERの拡散角度(ラジアン、片側)
    int   spreadCount = -1;          // SPREAD_SHOOTERの弾数
    float floatAmplitude = -1.0f;    // FLOATERの浮遊振幅(px)
    float floatFrequency = -1.0f;    // FLOATERの浮遊周波数
    float teleportRangeMin = -1.0f;  // TELEPORTERのワープ先オフセット最小値(px)
    float teleportRangeMax = -1.0f;  // TELEPORTERのワープ先オフセット最大値(px)
    float shrinkFactor = -1.0f;      // SHRINKERの被弾時の縮小率
    float shieldOnDuration = -1.0f;  // SHIELDの無敵状態の継続時間(フレーム)
    float shieldOffDuration = -1.0f; // SHIELDの無敵解除状態の継続時間(フレーム)
    float mimicDelayFrames = -1.0f;  // MIMIC_GHOSTの遅延フレーム数
    float sizeAmplitude = -1.0f;     // SIZE_SHIFTERのスケール振幅
    float sizeFrequency = -1.0f;     // SIZE_SHIFTERのスケール周波数
    float minScale = -1.0f;          // SIZE_SHIFTERの最小スケールクランプ
    float tempoFrequency = -1.0f;    // TEMPO_WARPERの周波数
    float tempoMin = -1.0f;          // TEMPO_WARPERのspeedScale最小値
    float tempoMax = -1.0f;          // TEMPO_WARPERのspeedScale最大値
    float effectRange = -1.0f;       // BRIGHTNESS_PHANTOM/COLOR_SHIFTER/ZOOM_DISRUPTORの効果範囲(px)
    float brightnessMin = -1.0f;     // BRIGHTNESS_PHANTOMの最小輝度
    float tintStrength = -1.0f;      // COLOR_SHIFTERの色シフト強度
    float zoomAmplitude = -1.0f;     // ZOOM_DISRUPTORのズーム振幅
    float zoomFrequency = -1.0f;     // ZOOM_DISRUPTORのズーム周波数

    // ==== 敵の動き大幅改良プラン Phase 1 ====
    float shockwaveRadius = -1.0f;       // FALLERの着地ショックウェイブ半径(px)
    float fastForwardJitter = -1.0f;     // FALLERが早送り中に落下しているときの左右ジッター量(px)
    float fastForwardAttackMult = -1.0f; // STATIONARY/PATROL_SHOOTERが早送り中に攻撃間隔を詰める倍率
    float diagonalFallSpeed = -1.0f;     // FALLERの編集機能(方向反転)連動：向きをスポーン時から変更されたときの斜め落下速度(px/フレーム)

    // 新敵ロスター対応 — この敵がプレイヤーの「一時停止」を無視して動き続けるかどうか。
    // 幽霊タイプの敵のように「止めても止まらないので逃げるしかない」相手を作るためのフラグ。
    // 既定は false なので、このキーを書いていない既存の敵定義は従来どおり一時停止で止まる。
    // ※ Lab_Editor 側の EnemyDef にも同名プロパティを用意してあること。
    //   無いとエディタで保存した瞬間にこのキーが黙って消えてしまう。
    bool ignorePause = false;

    // ==== 敵の行動改良（新ロスター向けに追加したパラメータ） ====
    // いずれも -1 / false のままなら従来の挙動と完全に同じになるようにしてあるので、
    // このキーを書いていない既存の enemies.json は無変更で動く。
    // ※ Lab_Editor 側の EnemyDef にも同名プロパティを用意してあること。
    //   無いとエディタで保存した瞬間にこのキーが黙って消えてしまう。
    bool radialFire = false;            // SPREAD_SHOOTER: trueなら正面ファンではなく360度全方位へ均等に撃つ
    float spreadRotationStep = -1.0f;   // SPREAD_SHOOTER: 1斉射ごとに発射角度をずらす量(ラジアン)。渦巻き弾幕を作る
    float verticalTrackSpeed = -1.0f;   // FLOATER: 浮遊の中心高度をプレイヤーのYへ寄せる速さ(px/フレーム)。0なら従来どおり高さ固定
    float riseSpeed = -1.0f;            // FALLER: クールダウン後に元の高さへ戻る速さ(px/フレーム)。0以下なら従来どおり瞬間復帰
    // プレイヤーへ接触ダメージを与えるたびに、パーツ(parts)を尾側から1つ消費するか。
    // いもむしのように「攻撃するほど胴体が短くなり、使い切ると力尽きる」相手を作るためのフラグ。
    // 残りの節数がそのまま「あと何回危険な攻撃が来るか」の可視化になる。
    // 既定はfalseなので、パーツを持つ既存の敵（砲台・ドッスン等）の挙動は一切変わらない。
    bool consumePartOnAttack = false;

    // Feature: Puzzle-like Behavior Scripting (M2) — type_enum==ENEMY_CUSTOM_SCRIPTの時に使うJSON ASTブロック配列
    json script = json::array();

    // Feature: 編集リアクションのJSON宣言 — 「編集されたらどうなるか」をJSONだけで組むためのブロック。
    // scriptと同じく入れ子JSONを丸ごと保持するので、キーの追加はこことLab_EditorのC#モデルの2箇所で済む。
    // 空なら何も起きず、従来どおりの挙動になる（既存の全アセットは空のまま）。
    json editReactions = json::object();

    // Feature: Composite Multi-Part Objects (Parts-M1)
    std::vector<PartDef> parts;
};

// ギミック（回転する橋、沈むリフト、ワープ床など）1種類分の定義データ。gimmicks.jsonから読み込まれる。
struct GimmickDef {
    std::string id = "";           // ギミックの種類を一意に識別するID文字列
    std::string name = "";         // エディタ上に表示する人間向けの名前
    int type_enum = 0;             // ギミックの種類（ROTATING_BRIDGE/FALLING_LIFT等）を表す番号
    std::string sprite_path = "";  // 見た目に使う画像ファイルのパス
    int graphHandle = -1;          // sprite_pathをLoadGraphした結果のハンドル
    int hitboxOffsetX = 0;         // 表示座標から当たり判定矩形までのXオフセット
    int hitboxOffsetY = 0;         // 表示座標から当たり判定矩形までのYオフセット
    int hitboxWidth = 32;          // 当たり判定矩形の幅
    int hitboxHeight = 32;         // 当たり判定矩形の高さ
    std::string seActivate = "";   // 作動時（起動・接触時など）に鳴らす効果音ファイル名

    // ==== Feature: Configurable Behavior Parameters (M1) ====
    // 各フィールドは既定値 -1 (未設定) の場合、そのtype_enumの従来のハードコード挙動を
    // そのまま再現する値がLoadAssetDefinitions()で補完される。既存のgimmicks.jsonは無変更で動作する。
    float rotationSpeed = -1.0f;        // ROTATING_BRIDGE: 1フレームあたりの回転量(ラジアン)
    float sinkSpeed = -1.0f;            // FALLING_LIFT: 降下速度(px/フレーム)
    float maxDepthOffset = -1.0f;       // FALLING_LIFT: 地面からの最大沈み込み量(px)
    float pushOutDistance = -1.0f;      // REFLECT_MIRROR: 反射後に弾を押し出す距離係数
    float triggerWidthThreshold = -1.0f;// WEIGHT_SWITCH: 起動に必要なBOXの横幅(px)
    float travelDistance = -1.0f;       // MOVING_PLATFORM/FRAMESTEP_LIFT: 上端からの可動距離(px)
    float oscillationSpeed = -1.0f;     // MOVING_PLATFORM: 往復の速さ
    float stepIncrement = -1.0f;        // FRAMESTEP_LIFT: コマ送り1回あたりの移動割合(0-1)
    float standDelayFrames = -1.0f;     // CHIKUWA_BLOCK: 乗ってから落下するまでの時間(フレーム)
    float standTolerancePx = -1.0f;     // CHIKUWA_BLOCK: 「乗っている」判定の許容誤差(px)
    float respawnDelayFrames = -1.0f;   // CHIKUWA_BLOCK: 落下後、元の位置に復活するまでの時間(フレーム)
    float radius = -1.0f;               // TIME_FIELD: 効果範囲の半径(px)
    float brightLevel = -1.0f;          // BRIGHTNESS_ZONE(明転)/SLOWMO_FIELD: 明るさ倍率
    float darkLevel = -1.0f;            // BRIGHTNESS_ZONE(暗転): 明るさ倍率
    float tintR = -1.0f, tintG = -1.0f, tintB = -1.0f; // COLOR_ZONE: 色調(RGB倍率)
    float zoomLevel = -1.0f;            // ZOOM_LENS/SLOWMO_FIELD: ズーム倍率
    float warpOffsetPx = -1.0f;         // CUT_PORTAL: ワープ後に押し出す位置オフセット(px)

    // 新ギミックロスター対応 — 「作動中」の見た目に差し替えるための第2スプライト。
    // 重量スイッチの押し込み（スイッチオフ.png ⇔ スイッチオン.png）のように、
    // 状態がひと目で分かる必要があるギミック向け。空文字なら従来どおり sprite 1枚で描画する。
    // ※ Lab_Editor 側の GimmickDef にも同名プロパティを用意してあること。
    std::string spriteAlt_path = "";
    int graphHandleAlt = -1;            // spriteAlt_pathをLoadGraphした結果のハンドル（未設定/失敗時は-1）

    // Feature: Puzzle-like Behavior Scripting (M2) — type_enum==GIMMICK_CUSTOM_SCRIPTの時に使うJSON ASTブロック配列
    json script = json::array();

    // Feature: 編集リアクションのJSON宣言（詳細はEnemyDefの同名フィールドのコメント参照）
    json editReactions = json::object();

    // Feature: Composite Multi-Part Objects (Parts-M1)
    std::vector<PartDef> parts;
};

std::vector<EnemyDef> enemyDefs;     // 読み込み済みの敵定義（enemies.json由来）を全種類分保持する一覧
std::vector<GimmickDef> gimmickDefs; // 読み込み済みのギミック定義（gimmicks.json由来）を全種類分保持する一覧

// ステージ上に実際に配置された「敵1体分」の情報。どの定義(EnemyDef)を使うか、どこに置くかだけを持つ軽量な構造体。
struct PlacedEnemy {
    int def_idx;  // enemyDefs配列内でのインデックス（この敵がどの種類の敵かを指す）
    float x, y;   // ステージ内でのスポーン座標（ワールド座標）
};
// ステージ上に実際に配置された「ギミック1個分」の情報。
struct PlacedGimmick {
    int def_idx;  // gimmickDefs配列内でのインデックス
    float x, y;   // ステージ内でのスポーン座標
    std::string stringParam = ""; // ポータルの遷移先など
};
std::vector<PlacedEnemy> editorPlacedEnemies;     // 現在編集中/プレイ中のステージに配置されている敵の一覧
std::vector<PlacedGimmick> editorPlacedGimmicks;  // 現在編集中/プレイ中のステージに配置されているギミックの一覧
#include <filesystem>
namespace fs = std::filesystem;
std::string currentStageFileName = "stage_01.json"; // 現在読み込み・保存対象になっているステージファイル名

// アイテム1種類分の定義データ。items.jsonから読み込まれる。
struct ItemDef {
    std::string id = "";             // アイテムの種類を一意に識別するID文字列
    std::string name = "";           // エディタ上に表示する人間向けの名前
    int type_enum = 0;               // アイテムの種類を表す番号
    std::string sprite_path = "";    // 見た目に使う画像ファイルのパス
    std::string grant_ability = "";  // 取得したプレイヤーに付与する能力名（例: "canDoubleJump"）
    int graphHandle = -1;            // sprite_pathをLoadGraphした結果のハンドル
    int hitboxOffsetX = 0;           // 表示座標から当たり判定矩形までのXオフセット
    int hitboxOffsetY = 0;           // 表示座標から当たり判定矩形までのYオフセット
    int hitboxWidth = 32;            // 当たり判定矩形の幅
    int hitboxHeight = 32;           // 当たり判定矩形の高さ
    std::string seCollect = "";      // 取得時に鳴らす効果音ファイル名

    // Feature: Composite Multi-Part Objects (Parts-M1)
    std::vector<PartDef> parts;
};

// プレイヤーが現在使える能力（アイテムで解禁されるアクション）をまとめた構造体。
// ステージ側の設定や取得済みアイテムによって書き換えられ、Player側の入力処理が参照する。
struct PlayerCapabilities {
    bool canDoubleJump = false;      // 二段ジャンプができるか
    bool canDash = false;            // ダッシュができるか
    bool canShootFireball = false;   // 火の玉を撃てるか
    bool canFly = false;             // 飛行（滞空）ができるか
    int baseJumpPower = -12;         // 基本ジャンプ力（負の値ほど高く跳ぶ、Y速度への初期値）
    float baseSpeed = 4.0f;          // 基本移動速度
};
PlayerCapabilities editorPlayerCaps; // 現在のステージ/プレイ中に有効なプレイヤー能力の実体

// ===== Feature: 編集コストゲージ (Edit Cost Gauge) =====
// プレイ中にプレイヤーが使える「編集系ツール」（巻き戻し・一時停止・早送り・画面エフェクト・
// オブジェクト個別編集）それぞれについて、そもそも使用を許可するかどうかを表すフラグ集。
// ステージ側の設定で一部の操作を封印したり、アイテム取得で解禁したりするのに使う。
struct EditToolFlags {
    bool rewindEnabled = true;       // 時間巻き戻し操作を許可するか
    bool pauseEnabled = true;        // 一時停止操作を許可するか
    bool fastForwardEnabled = true;  // 早送り操作を許可するか
    bool screenEffectEnabled = true; // Z/X/C（ズーム・明暗）+ T（色フィルタ）をまとめた「画面エフェクト」
    bool objectEditEnabled = true;   // 右クリックによる個別オブジェクト編集（選択・コンテキストメニュー全体）
    // Feature: カット機能の復活 — 下部タイムラインでのカット（区間を丸ごと飛ばす編集）を許可するか。
    // 「オブジェクト編集」とは独立したフラグにしてあるので、
    //   ・オブジェクト編集は禁止だがカットは許す
    //   ・カットだけ禁止する（ステージを飛ばされたくない場面）
    // といった組み合わせをステージ単位で作れる。
    bool cutEnabled = true;
};

// 編集系ツールを使い続けるための「コストゲージ」に関する数値設定。
// ゲージは時間経過で回復し、各操作を使うたびに消費される。値が尽きるとその操作が使えなくなる。
struct EditCostSettings {
    float maxCost = 100.0f;              // ゲージの最大値
    float regenPerSec = 6.0f;            // 何も使っていない間、1秒あたりに自然回復する量
    float drainRewindPerSec = 18.0f;     // 巻き戻し操作を保持している間、1秒あたりに消費する量
    float drainPausePerSec = 4.0f;       // 一時停止を保持している間、1秒あたりに消費する量
    float drainFastForwardPerSec = 10.0f;// 早送りを保持している間、1秒あたりに消費する量
    float drainScreenEffectPerSec = 8.0f; // Z/X/C保持中 or 色フィルタ非ゼロ中に加算
    float flatColorCycle = 5.0f;         // 色フィルタを1段階切り替えるたびに一括で消費する固定量
    float flatMenuToggle = 8.0f;     // コンテキストメニューの巻き戻し/一時停止トグル、複数選択の一括トグルも同額
    float flatSpeedChange = 6.0f;        // 速度変更操作1回あたりの固定消費量
    float flatDirectionFlip = 4.0f;      // 向き反転操作1回あたりの固定消費量
    float flatResetAll = 10.0f;          // 全リセット操作1回あたりの固定消費量
    // Feature: カット機能の復活 — タイムラインカットを1本作るのにかかる基本消費量。
    // カットはステージの一区間をまるごと飛ばせる最も強力な編集操作なので、他より高めに設定してある。
    // 「距離に関わらず必ず取られる分」＝取っ掛かりのコスト。
    float flatCutCreate = 20.0f;
    // Feature: カットコストの距離変動 — カットで飛ばす距離1タイル(32px)あたりの追加消費量。
    //
    // 従来カットは長さに関係なく一律 flatCutCreate だけで作れたため、
    // 「ゲージが溜まったらステージの端から端まで1本引いて丸ごと飛ばす」のが常に最適解になっていた。
    // 距離に比例した追加コストを課すと
    //   ・短いカット … 安い。詰まった数タイルを飛ばす、細かい局所的な使い方ができる
    //   ・長いカット … 高い。ゲージを使い切る覚悟が要る、ここぞの一手になる
    // という使い分けが生まれ、「どこをどれだけ飛ばすか」自体が考えどころになる。
    // 実際の総額は ComputeCutCreateCost() を参照。0にすれば従来どおりの定額に戻せる。
    float cutCostPerTile = 1.2f;
};

// アイテムで恒久解禁された操作（セッション永続。editorPlayerCapsと同じ扱いでResetStageではクリアしない）
EditToolFlags unlockedEditTools = { false, false, false, false, false, false };

std::vector<ItemDef> itemDefs; // 読み込み済みのアイテム定義（items.json由来）を全種類分保持する一覧
// ステージ上に実際に配置された「アイテム1個分」の情報。
struct PlacedItem {
    int def_idx;  // itemDefs配列内でのインデックス（このアイテムがどの種類かを指す）
    float x, y;   // ステージ内でのスポーン座標
};
std::vector<PlacedItem> editorPlacedItems; // 現在編集中/プレイ中のステージに配置されているアイテムの一覧

// ==== Feature: Configurable Behavior Parameters (M1) ====
// EnemyDef/GimmickDefの新パラメータのうち -1 (未設定) のままのフィールドに、
// そのtype_enumが従来ハードコードされていた値をそのまま補完する。
// これにより既存のenemies.json/gimmicks.jsonを一切変更せずに挙動が完全に維持される。
// type_enumの数値は AssetManagerForm.cs の EnemyTypes/GimmickTypes 一覧の並び順と対応する。
void ApplyEnemyDefaultParams(EnemyDef& def) {
    // fill: 対象フィールドがまだ-1.0f（=JSONで未指定）のときだけ、legacyDefaultで上書きする補助関数。
    // 既にJSON側で値が指定されていれば(0以上なら)何もしないので、既存ステージの調整値を壊さない。
    auto fill = [](float& field, float legacyDefault) { if (field < 0.0f) field = legacyDefault; };
    // type_enum（敵の行動タイプ）ごとに、そのタイプが従来ハードコードで使っていた既定値を補完する。
    // 各case行末のコメントはAssetManagerForm.cs側のEnemyTypes一覧に対応するタイプ名。
    switch (def.type_enum) {
        case 0: fill(def.moveSpeed, 0.4f); break; // PATROL
        case 1: fill(def.actionInterval, 90.0f); fill(def.jumpPowerMult, 0.7f); break; // JUMPER
        case 2: fill(def.actionInterval, 120.0f); fill(def.projectileSpeed, 0.6f); fill(def.fastForwardAttackMult, 2.2f); break; // STATIONARY
        case 3: // PATROL_SHOOTER
            fill(def.triggerRange, 300.0f); fill(def.detectionRangeY, 100.0f);
            fill(def.moveSpeed, 0.5f); fill(def.cooldownTime, 60.0f); fill(def.projectileSpeed, 0.5f);
            fill(def.fastForwardAttackMult, 2.2f);
            break;
        case 4: fill(def.moveSpeed, 0.35f); fill(def.triggerRange, 300.0f); break; // WALKER
        case 5: fill(def.moveSpeed, 0.55f); fill(def.jumpPowerMult, 0.8f); fill(def.triggerRange, 300.0f); break; // CHASER
        case 6: // DASH_CHARGER
            fill(def.triggerRange, 260.0f); fill(def.chargeTime, 30.0f);
            fill(def.dashSpeedMult, 1.5f); fill(def.dashDuration, 40.0f); fill(def.cooldownTime, 70.0f);
            break;
        // FALLER: riseSpeedは0.0fで補完＝「復帰は瞬間移動」。従来と同じ挙動を保つ
        case 7: fill(def.triggerRange, 24.0f); fill(def.fallDelay, 10.0f); fill(def.cooldownTime, 120.0f); fill(def.shockwaveRadius, 60.0f); fill(def.fastForwardJitter, 30.0f); fill(def.diagonalFallSpeed, 2.5f); fill(def.riseSpeed, 0.0f); break; // FALLER
        case 8: // SPREAD_SHOOTER
            fill(def.actionInterval, 150.0f); fill(def.spreadAngle, 0.35f); fill(def.projectileSpeed, 0.5f);
            if (def.spreadCount < 0) def.spreadCount = 3;
            // 0.0fで補完＝「斉射ごとの角度ずらし無し」。従来の固定ファン射撃と完全に同じ挙動になる。
            fill(def.spreadRotationStep, 0.0f);
            break;
        case 9: fill(def.actionInterval, 130.0f); fill(def.projectileSpeed, 0.55f); break; // AIMED_SHOOTER
        // FLOATER: verticalTrackSpeedは0.0fで補完＝「高度は据え置き」。従来と同じ挙動を保つ
        case 10: fill(def.floatAmplitude, 40.0f); fill(def.floatFrequency, 0.05f); fill(def.moveSpeed, 0.2f); fill(def.triggerRange, 300.0f); fill(def.verticalTrackSpeed, 0.0f); break; // FLOATER
        case 11: fill(def.actionInterval, 180.0f); fill(def.teleportRangeMin, 120.0f); fill(def.teleportRangeMax, 220.0f); break; // TELEPORTER
        case 12: fill(def.moveSpeed, 0.35f); fill(def.enragedMoveSpeed, 0.9f); fill(def.shrinkFactor, 0.6f); fill(def.triggerRange, 300.0f); break; // SHRINKER
        case 13: fill(def.moveSpeed, 0.3f); fill(def.shieldOffDuration, 150.0f); fill(def.shieldOnDuration, 90.0f); fill(def.triggerRange, 300.0f); break; // SHIELD
        case 14: fill(def.mimicDelayFrames, 90.0f); break; // MIMIC_GHOST
        case 15: fill(def.moveSpeed, 0.25f); fill(def.sizeAmplitude, 0.5f); fill(def.sizeFrequency, 0.04f); fill(def.minScale, 0.4f); fill(def.triggerRange, 300.0f); break; // SIZE_SHIFTER
        case 16: fill(def.moveSpeed, 0.4f); fill(def.tempoFrequency, 0.05f); fill(def.tempoMin, 0.3f); fill(def.tempoMax, 1.6f); fill(def.triggerRange, 300.0f); break; // TEMPO_WARPER
        case 17: fill(def.moveSpeed, 0.3f); fill(def.effectRange, 320.0f); fill(def.brightnessMin, 0.35f); fill(def.triggerRange, 300.0f); break; // BRIGHTNESS_PHANTOM
        case 18: fill(def.moveSpeed, 0.3f); fill(def.effectRange, 320.0f); fill(def.tintStrength, 0.6f); fill(def.triggerRange, 300.0f); break; // COLOR_SHIFTER
        case 19: fill(def.effectRange, 280.0f); fill(def.zoomAmplitude, 0.25f); fill(def.zoomFrequency, 0.08f); fill(def.triggerRange, 300.0f); break; // ZOOM_DISRUPTOR
        case 21: // POUNCER（飛びかかり）。DASH_CHARGERと同じ項目名を使い回すが、突進ではなくジャンプに使う
            fill(def.moveSpeed, 0.7f);       // 通常時の走行速度係数（速めに追ってくる）
            fill(def.triggerRange, 200.0f);  // この距離まで詰めると飛びかかりを始める
            fill(def.chargeTime, 22.0f);     // 飛ぶ前の溜め（＝プレイヤーが避け始める合図）
            // 飛距離は概ね D ≒ 416 × jumpPowerMult × dashSpeedMult [px] になる。
            // 既定は triggerRange(200px) とほぼ同じ距離に着地する組み合わせにしてあり、
            // 「飛びかかられたら着地点は自分の足元」と読めるようにしている。
            // これより大きくすると相手を大きく飛び越してしまい、追い詰められている感じが消える。
            fill(def.jumpPowerMult, 1.0f);   // 飛びかかりのジャンプ力係数
            fill(def.dashSpeedMult, 0.5f);   // 飛びかかりの水平速度係数
            fill(def.dashDuration, 120.0f);  // 空中に居られる上限フレーム（着地を取り逃した時の保険）
            fill(def.cooldownTime, 45.0f);   // 着地後の硬直＝反撃の窓
            break;
        default: break;
    }
}

void ApplyGimmickDefaultParams(GimmickDef& def) {
    // ApplyEnemyDefaultParamsと同じ考え方：未指定(-1.0f)のフィールドにのみ従来の既定値を補完する。
    auto fill = [](float& field, float legacyDefault) { if (field < 0.0f) field = legacyDefault; };
    // type_enum（ギミックの種類）ごとに既定値を補完する。case行末のコメントはギミックタイプ名。
    switch (def.type_enum) {
        case 1: fill(def.rotationSpeed, 0.015f); break; // ROTATING_BRIDGE
        case 4: fill(def.sinkSpeed, 1.5f); fill(def.maxDepthOffset, 20.0f); break; // FALLING_LIFT
        case 5: fill(def.pushOutDistance, 1.5f); break; // REFLECT_MIRROR
        case 6: fill(def.triggerWidthThreshold, 140.0f); break; // WEIGHT_SWITCH
        case 11: fill(def.standDelayFrames, 45.0f); fill(def.standTolerancePx, 10.0f); fill(def.respawnDelayFrames, 180.0f); break; // CHIKUWA_BLOCK
        case 12: fill(def.radius, 100.0f); break; // TIME_FIELD
        case 14: fill(def.travelDistance, 96.0f); fill(def.oscillationSpeed, 0.02f); break; // MOVING_PLATFORM
        case 17: fill(def.travelDistance, 128.0f); fill(def.stepIncrement, 0.15f); break; // FRAMESTEP_LIFT
        case 18: fill(def.brightLevel, 1.6f); fill(def.darkLevel, 0.35f); break; // BRIGHTNESS_ZONE
        case 19: fill(def.tintR, 1.0f); fill(def.tintG, 0.6f); fill(def.tintB, 1.0f); break; // COLOR_ZONE
        case 20: fill(def.zoomLevel, 1.6f); break; // ZOOM_LENS
        case 21: fill(def.zoomLevel, 1.3f); fill(def.brightLevel, 0.85f); break; // SLOWMO_FIELD
        case 0: fill(def.warpOffsetPx, 8.0f); break; // CUT_PORTAL
        default: break;
    }
}

// Feature: Composite Multi-Part Objects (Parts-M1) — 敵/ギミック/アイテム共通の「parts」テンプレート配列パーサー
// parentObj（敵/ギミック/アイテム1件分のJSONオブジェクト）に"parts"配列があれば、その各要素を
// PartDefへ変換してoutPartsに積んでいく。"parts"が無い/配列でない場合は何もしない
// （＝既存のparts無し定義はこれまで通りの見た目・挙動を保つ）。
void ParsePartDefsFromJson(const json& parentObj, std::vector<PartDef>& outParts) {
    if (!parentObj.contains("parts") || !parentObj["parts"].is_array()) return;
    for (const auto& p : parentObj["parts"]) {
        if (!p.is_object()) continue;
        PartDef part;
        // JSONの各キーをPartDefのフィールドへ読み込む。キーが存在しなければ第2引数の既定値を使う。
        part.id = p.value("id", "");
        part.sprite_path = p.value("sprite", "");
        part.offsetX = p.value("offsetX", 0.0f);
        part.offsetY = p.value("offsetY", 0.0f);
        part.width = p.value("width", 0);
        part.height = p.value("height", 0);
        part.hitboxOffsetX = p.value("hitboxOffsetX", 0);
        part.hitboxOffsetY = p.value("hitboxOffsetY", 0);
        part.hitboxWidth = p.value("hitboxWidth", 32);
        part.hitboxHeight = p.value("hitboxHeight", 32);
        part.scale = p.value("scale", 1.0f);
        part.hp = p.value("hp", 0);
        part.zOrder = p.value("zOrder", 0);
        part.deadly = p.value("deadly", false); // 既定false＝従来どおり無害な装飾パーツ
        if (p.contains("script") && p["script"].is_array()) part.script = p["script"];
        // 画像パスが指定されていれば実際に読み込み、幅・高さ・当たり判定サイズが
        // JSONで未指定（0のまま）だった場合は画像の実サイズで補完する。
        if (!part.sprite_path.empty()) {
            part.graphHandle = LoadGraph(part.sprite_path.c_str());
            if (part.graphHandle >= 0) {
                int gw, gh;
                GetGraphSize(part.graphHandle, &gw, &gh);
                if (part.width <= 0) part.width = gw;
                if (part.height <= 0) part.height = gh;
                if (part.hitboxWidth == 0) part.hitboxWidth = gw;
                if (part.hitboxHeight == 0) part.hitboxHeight = gh;
            }
        }
        outParts.push_back(part);
    }
}

// ゲーム起動時（およびエディタ起動時）に1度だけ呼ばれ、assets/enemies.json・assets/items.json・
// assets/gimmicks.json をそれぞれ読み込んで、enemyDefs／itemDefs／gimmickDefsの3つの一覧を構築する。
// ここで作られる定義データは「敵/アイテム/ギミックの種類ごとの共通設定」であり、
// ステージ内の個別配置情報（PlacedEnemy等）とは別物なので注意。
void LoadAssetDefinitions() {
    // ---- 敵定義(enemies.json)の読み込み ----
    {
        std::ifstream ef("assets/enemies.json");
        if (ef.is_open()) {
            json ej = json::parse(ef, nullptr, false);
            if (!ej.is_discarded() && ej.is_array()) {
                for (const auto& e : ej) {
                    if (!e.is_object()) continue;
                    EnemyDef def;
                    // 基本情報（各フィールドの意味はEnemyDef構造体側のコメントを参照）
                    def.id = e.value("id", "");
                    def.name = e.value("name", "");
                    def.type_enum = e.value("type_enum", 0);
                    def.hp = e.value("hp", 1);
                    def.width = e.value("width", 32);
                    def.height = e.value("height", 32);
                    def.sprite_path = e.value("sprite", "");
                    def.seSpawn = e.value("seSpawn", "");
                    def.seAttack = e.value("seAttack", "");
                    def.seDamage = e.value("seDamage", "");
                    def.seDeath = e.value("seDeath", "");
                    def.hitboxOffsetX = e.value("hitboxOffsetX", 0);
                    def.hitboxOffsetY = e.value("hitboxOffsetY", 0);
                    def.hitboxWidth = e.value("hitboxWidth", def.width);
                    def.hitboxHeight = e.value("hitboxHeight", def.height);
                    def.scale = e.value("scale", 1.0f);
                    // 画像を読み込む。
                    // 【重要】以前はここで width/height を画像の実サイズで必ず上書きしていたが、
                    // 新素材は全て 640x640 の共通キャンバスで描かれているため、それをやると
                    // 全ての敵の「論理サイズ」が 640 になってしまい、ComputeFitScale() が
                    // 表示倍率を 1.0（＝原寸640px）と算出して画面が敵1体で埋まる。
                    // そこで JSON の width/height を正とし、未指定（0以下）のときだけ画像サイズで補完する。
                    if (!def.sprite_path.empty()) {
                        def.graphHandle = LoadGraph(def.sprite_path.c_str());
                        int gw, gh;
                        GetGraphSize(def.graphHandle, &gw, &gh);
                        if (def.width <= 0) def.width = gw;
                        if (def.height <= 0) def.height = gh;
                        if (def.hitboxWidth == 0) def.hitboxWidth = def.width;
                        if (def.hitboxHeight == 0) def.hitboxHeight = def.height;
                    }
                    // Feature: Configurable Behavior Parameters (M1)
                    // 行動パラメータ群をJSONから読み込む。未指定のキーは-1.0f(未設定)のままにしておき、
                    // 後でApplyEnemyDefaultParams()がtype_enumに応じた既定値を補完する。
                    def.moveSpeed = e.value("moveSpeed", -1.0f);
                    def.enragedMoveSpeed = e.value("enragedMoveSpeed", -1.0f);
                    def.actionInterval = e.value("actionInterval", -1.0f);
                    def.jumpPowerMult = e.value("jumpPowerMult", -1.0f);
                    def.triggerRange = e.value("triggerRange", -1.0f);
                    def.detectionRangeY = e.value("detectionRangeY", -1.0f);
                    def.projectileSpeed = e.value("projectileSpeed", -1.0f);
                    def.chargeTime = e.value("chargeTime", -1.0f);
                    def.dashSpeedMult = e.value("dashSpeedMult", -1.0f);
                    def.dashDuration = e.value("dashDuration", -1.0f);
                    def.cooldownTime = e.value("cooldownTime", -1.0f);
                    def.fallDelay = e.value("fallDelay", -1.0f);
                    def.spreadAngle = e.value("spreadAngle", -1.0f);
                    def.spreadCount = e.value("spreadCount", -1);
                    def.floatAmplitude = e.value("floatAmplitude", -1.0f);
                    def.floatFrequency = e.value("floatFrequency", -1.0f);
                    def.teleportRangeMin = e.value("teleportRangeMin", -1.0f);
                    def.teleportRangeMax = e.value("teleportRangeMax", -1.0f);
                    def.shrinkFactor = e.value("shrinkFactor", -1.0f);
                    def.shieldOnDuration = e.value("shieldOnDuration", -1.0f);
                    def.shieldOffDuration = e.value("shieldOffDuration", -1.0f);
                    def.mimicDelayFrames = e.value("mimicDelayFrames", -1.0f);
                    // 新敵ロスター対応 — 一時停止を無視するか（既定false＝従来どおり止まる）
                    def.ignorePause = e.value("ignorePause", false);
                    // 敵の行動改良で追加したパラメータ。未指定なら false / -1 のままにしておき、
                    // ApplyEnemyDefaultParams() が「従来の挙動と完全に同じ」値を後から補完する。
                    def.radialFire = e.value("radialFire", false);
                    def.spreadRotationStep = e.value("spreadRotationStep", -1.0f);
                    def.verticalTrackSpeed = e.value("verticalTrackSpeed", -1.0f);
                    def.riseSpeed = e.value("riseSpeed", -1.0f);
                    def.consumePartOnAttack = e.value("consumePartOnAttack", false);
                    def.sizeAmplitude = e.value("sizeAmplitude", -1.0f);
                    def.sizeFrequency = e.value("sizeFrequency", -1.0f);
                    def.minScale = e.value("minScale", -1.0f);
                    def.tempoFrequency = e.value("tempoFrequency", -1.0f);
                    def.tempoMin = e.value("tempoMin", -1.0f);
                    def.tempoMax = e.value("tempoMax", -1.0f);
                    def.effectRange = e.value("effectRange", -1.0f);
                    def.brightnessMin = e.value("brightnessMin", -1.0f);
                    def.tintStrength = e.value("tintStrength", -1.0f);
                    def.zoomAmplitude = e.value("zoomAmplitude", -1.0f);
                    def.zoomFrequency = e.value("zoomFrequency", -1.0f);
                    def.shockwaveRadius = e.value("shockwaveRadius", -1.0f);
                    def.fastForwardJitter = e.value("fastForwardJitter", -1.0f);
                    def.fastForwardAttackMult = e.value("fastForwardAttackMult", -1.0f);
                    def.diagonalFallSpeed = e.value("diagonalFallSpeed", -1.0f);
                    // Feature: Puzzle-like Behavior Scripting (M2)
                    // カスタムスクリプト（ブロックのJSON配列）が指定されていれば読み込む
                    if (e.contains("script") && e["script"].is_array()) def.script = e["script"];
                    // Feature: 編集リアクションのJSON宣言
                    if (e.contains("edit_reactions") && e["edit_reactions"].is_object()) def.editReactions = e["edit_reactions"];
                    // 未設定(-1.0f)のパラメータをtype_enumごとの従来既定値で補完する
                    ApplyEnemyDefaultParams(def);
                    // 複数パーツで構成される敵の場合、"parts"配列を読み込む
                    ParsePartDefsFromJson(e, def.parts);
                    enemyDefs.push_back(def);
                }
            } else {
                // JSONとして解釈できなかった（構文エラー等）場合はログに残して処理を続行する
                Logger::Error("DrawPixel", "LoadAssetDefinitions", "Failed to parse enemies.json", "assets/enemies.json");
            }
        }
    }

    // ---- アイテム定義(items.json)の読み込み ----
    {
        std::ifstream itf("assets/items.json");
        if (itf.is_open()) {
            json ij = json::parse(itf, nullptr, false);
            if (!ij.is_discarded() && ij.is_array()) {
                for (const auto& i : ij) {
                    if (!i.is_object()) continue;
                    ItemDef def;
                    def.id = i.value("id", "");
                    def.name = i.value("name", "");
                    def.type_enum = i.value("type_enum", 0);
                    def.sprite_path = i.value("sprite", "");
                    def.grant_ability = i.value("grant_ability", "");
                    def.seCollect = i.value("seCollect", "");
                    def.hitboxOffsetX = i.value("hitboxOffsetX", 0);
                    def.hitboxOffsetY = i.value("hitboxOffsetY", 0);
                    def.hitboxWidth = i.value("hitboxWidth", 32);
                    def.hitboxHeight = i.value("hitboxHeight", 32);
                    if (!def.sprite_path.empty()) {
                        def.graphHandle = LoadGraph(def.sprite_path.c_str());
                        if (def.hitboxWidth == 0) {
                            int w, h; GetGraphSize(def.graphHandle, &w, &h);
                            def.hitboxWidth = w; def.hitboxHeight = h;
                        }
                    }
                    ParsePartDefsFromJson(i, def.parts);
                    itemDefs.push_back(def);
                }
            } else {
                Logger::Error("DrawPixel", "LoadAssetDefinitions", "Failed to parse items.json", "assets/items.json");
            }
        }
    }

    // ---- ギミック定義(gimmicks.json)の読み込み ----
    {
        std::ifstream gf("assets/gimmicks.json");
        if (gf.is_open()) {
            json gj = json::parse(gf, nullptr, false);
            if (!gj.is_discarded() && gj.is_array()) {
                for (const auto& g : gj) {
                    if (!g.is_object()) continue;
                    GimmickDef def;
                    def.id = g.value("id", "");
                    def.name = g.value("name", "");
                    def.type_enum = g.value("type_enum", 0);
                    def.sprite_path = g.value("sprite", "");
                    def.seActivate = g.value("seActivate", "");
                    def.hitboxOffsetX = g.value("hitboxOffsetX", 0);
                    def.hitboxOffsetY = g.value("hitboxOffsetY", 0);
                    def.hitboxWidth = g.value("hitboxWidth", 32);
                    def.hitboxHeight = g.value("hitboxHeight", 32);
                    if (!def.sprite_path.empty()) {
                        def.graphHandle = LoadGraph(def.sprite_path.c_str());
                        if (def.hitboxWidth == 0) {
                            int w, h; GetGraphSize(def.graphHandle, &w, &h);
                            def.hitboxWidth = w; def.hitboxHeight = h;
                        }
                    }
                    // Feature: Configurable Behavior Parameters (M1)
                    // ギミック用の行動パラメータをJSONから読み込む（未指定は-1.0fのまま）
                    def.rotationSpeed = g.value("rotationSpeed", -1.0f);
                    def.sinkSpeed = g.value("sinkSpeed", -1.0f);
                    def.maxDepthOffset = g.value("maxDepthOffset", -1.0f);
                    def.pushOutDistance = g.value("pushOutDistance", -1.0f);
                    def.triggerWidthThreshold = g.value("triggerWidthThreshold", -1.0f);
                    def.travelDistance = g.value("travelDistance", -1.0f);
                    def.oscillationSpeed = g.value("oscillationSpeed", -1.0f);
                    def.stepIncrement = g.value("stepIncrement", -1.0f);
                    def.standDelayFrames = g.value("standDelayFrames", -1.0f);
                    def.standTolerancePx = g.value("standTolerancePx", -1.0f);
                    def.respawnDelayFrames = g.value("respawnDelayFrames", -1.0f);
                    def.radius = g.value("radius", -1.0f);
                    def.brightLevel = g.value("brightLevel", -1.0f);
                    def.darkLevel = g.value("darkLevel", -1.0f);
                    def.tintR = g.value("tintR", -1.0f);
                    def.tintG = g.value("tintG", -1.0f);
                    def.tintB = g.value("tintB", -1.0f);
                    def.zoomLevel = g.value("zoomLevel", -1.0f);
                    def.warpOffsetPx = g.value("warpOffsetPx", -1.0f);
                    // 新ギミックロスター対応 — 作動中に差し替える画像。
                    // 起動時に一度だけ読み込んでハンドルを持っておく（毎フレームのLoadGraphは重いため）。
                    def.spriteAlt_path = g.value("spriteAlt", "");
                    if (!def.spriteAlt_path.empty()) {
                        def.graphHandleAlt = LoadGraph(def.spriteAlt_path.c_str());
                        if (def.graphHandleAlt == -1) {
                            Logger::Error("DrawPixel", "LoadAssetDefinitions", "Failed to load gimmick spriteAlt", def.spriteAlt_path.c_str());
                        }
                    }
                    // Feature: Puzzle-like Behavior Scripting (M2)
                    if (g.contains("script") && g["script"].is_array()) def.script = g["script"];
                    // Feature: 編集リアクションのJSON宣言
                    if (g.contains("edit_reactions") && g["edit_reactions"].is_object()) def.editReactions = g["edit_reactions"];
                    ApplyGimmickDefaultParams(def);
                    ParsePartDefsFromJson(g, def.parts);
                    gimmickDefs.push_back(def);
                }
            } else {
                Logger::Error("DrawPixel", "LoadAssetDefinitions", "Failed to parse gimmicks.json", "assets/gimmicks.json");
            }
        }
    }
}

// ===== SE検索ヘルパー (assetId → 各種 Def の効果音IDを引く) =====
// id文字列からenemyDefs一覧を線形探索し、一致する定義へのポインタを返す。見つからなければnullptr。
const EnemyDef* FindEnemyDef(const std::string& assetId) {
    for (const auto& d : enemyDefs) if (d.id == assetId) return &d;
    return nullptr;
}
// id文字列からgimmickDefs一覧を線形探索し、一致する定義へのポインタを返す。見つからなければnullptr。
const GimmickDef* FindGimmickDef(const std::string& assetId) {
    for (const auto& d : gimmickDefs) if (d.id == assetId) return &d;
    return nullptr;
}
// id文字列からitemDefs一覧を線形探索し、一致する定義へのポインタを返す。見つからなければnullptr。
const ItemDef* FindItemDef(const std::string& assetId) {
    for (const auto& d : itemDefs) if (d.id == assetId) return &d;
    return nullptr;
}

// Feature: Composite Multi-Part Objects (Parts-M1) — PartDef群からランタイムのPartInstance群を構築する。
// 親(敵/ギミック/アイテム)のResetStage時に呼び、baseX/baseYには親の(既に確定済みの)スポーン座標を渡す。
std::vector<PartInstance> BuildPartInstances(const std::vector<PartDef>& defs, float baseX, float baseY) {
    std::vector<PartInstance> result;
    // parts定義配列の各要素を、親の現在位置(baseX, baseY)を基準にしたランタイム状態(PartInstance)へ変換する
    for (size_t pi = 0; pi < defs.size(); pi++) {
        const PartDef& pd = defs[pi];
        PartInstance inst;
        // オフセットを親座標に加算して、パーツのワールド座標を確定させる
        inst.x = baseX + pd.offsetX;
        inst.y = baseY + pd.offsetY;
        // 複合オブジェクトのパーツ追従 — 定義上のオフセットがそのまま初期ローカル位置になる。
        // 親が未編集のあいだ、この値から組み直したワールド座標は上の2行と完全に一致する。
        inst.localX = pd.offsetX;
        inst.localY = pd.offsetY;
        inst.appliedX = inst.x;
        inst.appliedY = inst.y;
        inst.handle = pd.graphHandle;
        inst.scale = pd.scale;
        inst.hp = pd.hp;
        inst.isActive = true;
        inst.hitboxOffsetX = pd.hitboxOffsetX;
        inst.hitboxOffsetY = pd.hitboxOffsetY;
        inst.hitboxWidth = pd.hitboxWidth;
        inst.hitboxHeight = pd.hitboxHeight;
        inst.width = pd.width;
        inst.height = pd.height;
        inst.zOrder = pd.zOrder;
        inst.deadly = pd.deadly;
        inst.partIndex = (int)pi;
        result.push_back(inst);
    }
    return result;
}

// 新アセット移行対応 — 素材画像の実サイズを、定義側が意図した論理サイズへ収める表示倍率を求める。
//
// 新しい素材は全て 640x640 の共通キャンバスで描かれているため、DrawRotaGraph に倍率 1.0 を
// そのまま渡すと画面いっぱいの大きさで描画されてしまう（DrawRotaGraph は「画像の原寸 × 倍率」で描くため）。
// そこで「論理サイズ ÷ 画像サイズ」を求め、これを既存の scale に掛けて渡す。
// こうすると scale の意味を従来どおり「1.0 = 定義サイズちょうど」に保てるので、
// SHRINKER の縮小演出やスクリプトの SetScale といった既存の倍率操作を一切変えずに済む。
//
// 縦横比が素材と論理サイズで食い違っていても枠からはみ出さないよう、幅基準・高さ基準の小さい方を採る。
// 画像が未読み込み(-1)、または論理サイズが未設定(0以下)の場合は 1.0 を返し、従来どおり原寸で描画する。
static float ComputeFitScale(int graphHandle, float logicalW, float logicalH) {
    if (graphHandle < 0) return 1.0f;
    if (logicalW <= 0.0f && logicalH <= 0.0f) return 1.0f;
    int imgW = 0, imgH = 0;
    GetGraphSize(graphHandle, &imgW, &imgH);
    if (imgW <= 0 || imgH <= 0) return 1.0f;
    float sx = (logicalW > 0.0f) ? (logicalW / (float)imgW) : 0.0f;
    float sy = (logicalH > 0.0f) ? (logicalH / (float)imgH) : 0.0f;
    if (sx <= 0.0f) return sy;
    if (sy <= 0.0f) return sx;
    return (sx < sy) ? sx : sy;
}

// ===================================================================================
// 複合オブジェクトのパーツ追従 — 親の姿勢とその適用
// ===================================================================================

// パーツが親から継承する倍率の上下限。
//
// ファイアバー/振り子/回転する棘の輪は当たり判定が 8x8 しかないのに、
// スケール編集は「マウスの移動量をそのまま width に足す」方式なので、
// 100px ドラッグしただけで倍率が 13 倍を超える。そのまま腕に掛けると
// 長さ1700px級のハザードが画面を覆い尽くしてしまう。
// 描画と当たり判定の両方で必ずこのクランプ済みの値を使うこと
// （片方だけ掛けると「見えていないのに当たる」最悪のズレになる）。
static const float PART_RATIO_MIN = 0.25f;
static const float PART_RATIO_MAX = 4.0f;
static float ClampPartRatio(float v) {
    if (!(v > 0.0f)) return 1.0f; // 0・負・NaN は「編集されていない」とみなす
    if (v < PART_RATIO_MIN) return PART_RATIO_MIN;
    if (v > PART_RATIO_MAX) return PART_RATIO_MAX;
    return v;
}

// 親(敵/ギミック/アイテム)1体ぶんの「このフレームの姿勢」。
// 各記号の意味は BehaviorScript.h の PartLocalToWorld のコメントを参照。
struct ParentPose {
    float px = 0.0f, py = 0.0f; // P: 親のアンカー座標
    float qx = 0.0f, qy = 0.0f; // Q: 親アンカー→配置時の親中心
    float sx = 1.0f, sy = 1.0f; // s: 配置時に対する倍率（クランプ済み）
    float tilt = 0.0f;          // θ: 配置時からの傾き
    float uniform = 1.0f;       // u: パーツに掛ける一様倍率（クランプ済み）
};

// 親のアンカー座標・現在の描画サイズ・編集差分から ParentPose を組み立てる。
//
// ピボット Q は「現在の半サイズ ÷ 現在の倍率」で逆算する。配置時のサイズを直接持たないのは、
// ENEMY_SHRINKER の復活補正（基準側にも縮小率が掛かる）や ENEMY_SIZE_SHIFTER の
// 毎フレームの scale 書き換えといった特殊ケースがあり、
// 「配置時はこうだったはず」と決め打ちするとピボットが実際の描画中心とズレて、
// パーツだけが本体から浮いてしまうため。逆算なら P + Q*sx が常に描画中心に一致する。
//
// DrawRotaGraph は縦横別倍率を取れないので、パーツ自身の表示倍率は sx と sy の平均にする。
inline ParentPose MakeParentPose(float px, float py, float curW, float curH,
                                 float scaleRatio, float heightRatio, float tilt) {
    ParentPose pose;
    pose.px = px; pose.py = py;
    pose.sx = ClampPartRatio(scaleRatio);
    pose.sy = ClampPartRatio(heightRatio);
    pose.qx = (curW * 0.5f) / pose.sx;
    pose.qy = (curH * 0.5f) / pose.sy;
    pose.tilt = tilt;
    pose.uniform = ClampPartRatio((pose.sx + pose.sy) * 0.5f);
    return pose;
}

// パーツ自身の半サイズ(h)。親の倍率は含めない（含めると変換の中で二重に掛かる）。
inline void GetPartHalfSize(const PartInstance& p, float& hx, float& hy) {
    hx = (p.width  * p.scale) * 0.5f;
    hy = (p.height * p.scale) * 0.5f;
}

// パーツの当たり判定矩形を求める唯一の入口。
//
// 以前はこの式が6箇所へコピーされており、1箇所でも直し忘れると
// 「見えているのに当たらない」「見えていないのに当たる」という追跡の難しいバグになる。
// hitboxOffset にも倍率を掛けるのが正しい（従来は生ピクセルのままだったが、
// 現存する52パーツの hitboxOffset は全て(0,0)なので、この修正に回帰は無い）。
inline void GetPartHitRect(const PartInstance& p, float& outX, float& outY, float& outW, float& outH) {
    float m = p.scale * p.parentUniform;
    outX = p.x + (float)p.hitboxOffsetX * m;
    outY = p.y + (float)p.hitboxOffsetY * m;
    outW = (float)p.hitboxWidth  * m;
    outH = (float)p.hitboxHeight * m;
}

// パーツのスクリプトを実行した「直後」に呼ぶ。
//
// SetLocalOffset / SetLocalOffsetPolar はローカルオフセット自体を書き換えるので、ここでは何もしない。
// 一方 SetPosition / OffsetPosition のようにワールド座標を直接書くopが使われた場合は、
// ローカルが実際の位置と食い違ったままになるため、ワールドから逆変換して取り直す。
//
// 「前回 Apply が書いた座標と一致するか」で判定しているのがポイント。
// 現在の姿勢で組み直した座標と比べる実装にすると、親の姿勢が変わったフレームに
// 「スクリプトが動かした」と誤判定してローカルを取り直してしまい、
// スクリプトを持たないパーツが永久に親へ追従できなくなる。
void CapturePartsLocal(std::vector<PartInstance>& parts, const ParentPose& pose) {
    for (auto& part : parts) {
        if (part.x == part.appliedX && part.y == part.appliedY) continue; // 誰も触っていない
        float hx, hy; GetPartHalfSize(part, hx, hy);
        // SetLocalOffset系ならローカルも同時に更新済みなので、現在のローカルで説明がつく。
        // その場合は逆変換を通さない（往復の丸め誤差を乗せないため）。
        float wx = 0.0f, wy = 0.0f;
        PartLocalToWorld(pose.px, pose.py, pose.qx, pose.qy, pose.sx, pose.sy,
                         pose.tilt, pose.uniform, hx, hy, part.localX, part.localY, wx, wy);
        if (fabsf(part.x - wx) > 0.001f || fabsf(part.y - wy) > 0.001f) {
            PartWorldToLocal(pose.px, pose.py, pose.qx, pose.qy, pose.sx, pose.sy,
                             pose.tilt, pose.uniform, hx, hy, part.x, part.y,
                             part.localX, part.localY);
        }
        part.appliedX = part.x;
        part.appliedY = part.y;
    }
}

// ローカルオフセットと親の姿勢から、パーツのワールド座標を組み直す。
//
// 「毎フレーム必ず通る場所」に置くこと。編集ツールでドラッグしている最中は CanUpdate が
// false を返して全オブジェクトのスクリプトが1ティックも回らないため、
// スクリプト任せにするとまさにプレイヤーが拡大・回転している最中だけパーツが固まる。
// ここを通していれば、移動ドラッグ中・一時停止中・巻き戻し中でもパーツは本体に張り付く。
//
// ⚠ 必ず CapturePartsLocal より「後」に呼ぶこと。逆順にすると、スクリプトが動かない
// フレームで「親に張り付いた位置」をローカルとして取り直してしまい、編集差分が毎フレーム
// 累積して発散する。
void ApplyPartsParentPose(std::vector<PartInstance>& parts, const ParentPose& pose) {
    for (auto& part : parts) {
        float hx, hy; GetPartHalfSize(part, hx, hy);
        PartLocalToWorld(pose.px, pose.py, pose.qx, pose.qy, pose.sx, pose.sy,
                         pose.tilt, pose.uniform, hx, hy, part.localX, part.localY,
                         part.x, part.y);
        part.appliedX = part.x;
        part.appliedY = part.y;
        part.parentScaleX = pose.sx;
        part.parentScaleY = pose.sy;
        part.parentTilt = pose.tilt;
        part.parentUniform = pose.uniform;
    }
}

// ScriptActor へ親の姿勢を流し込む。SetLocalOffset系がローカル→ワールドの合成に使う。
// 敵・ギミック・アイテムの3つのパーツループが同じ内容を書くため、ここへ集約する。
void FillScriptPartTransform(ScriptActor& actor, PartInstance& part, const ParentPose& pose) {
    float hx, hy; GetPartHalfSize(part, hx, hy);
    actor.parentPivotX = pose.qx; actor.parentPivotY = pose.qy;
    actor.parentScaleX = pose.sx; actor.parentScaleY = pose.sy;
    actor.parentTilt = pose.tilt;
    actor.parentUniform = pose.uniform;
    actor.selfHalfX = hx; actor.selfHalfY = hy;
    actor.partLocalX = &part.localX;
    actor.partLocalY = &part.localY;
}

// Feature: Composite Multi-Part Objects (Parts-M5) — パーツの描画。
// zOrder<0のパーツは親本体の描画より先に、zOrder>=0のパーツは後に呼ぶ2パス方式にするため、
// wantBehindParent引数でどちらのパスを描画するかを切り替える。
void DrawPartsPass(const std::vector<PartInstance>& parts, float cameraX, float cameraY, bool wantBehindParent) {
    for (const auto& part : parts) {
        if (!part.isActive) continue; // 破壊済みのパーツは描画しない
        // wantBehindParent==trueのパス呼び出し時はzOrder<0（親より奥）のパーツだけ、
        // wantBehindParent==falseの呼び出し時はzOrder>=0（親より手前）のパーツだけを描画する
        if ((part.zOrder < 0) != wantBehindParent) continue;
        if (part.handle < 0) continue; // 画像が読み込まれていないパーツは描画しない
        // ワールド座標からカメラ位置を引いて画面上の座標に変換し、パーツの中心座標を求める。
        // 複合オブジェクトのパーツ追従 — 親を拡大したぶん(parentUniform)はパーツのサイズにも効くので、
        // 中心を出すときの半サイズにも同じ倍率を掛ける。掛け忘れると絵の位置が半サイズぶんずれる。
        float pm = part.scale * part.parentUniform;
        int pcx = (int)(part.x + (part.width * pm) / 2.0f - cameraX);
        int pcy = (int)(part.y + (part.height * pm) / 2.0f - cameraY);
        // 新アセット移行対応 — パーツ画像(640x640)をパーツ定義の width/height へ収める倍率を掛ける
        float partFit = ComputeFitScale(part.handle, (float)part.width, (float)part.height);
        // 親の傾きはパーツ自身の角度に加算する。剛体として親と一緒に回るのが既定の挙動で、
        // 「傾けてもプレイヤーを向き続ける」パーツはスクリプト側で ParentTilt を引いて打ち消す。
        DrawRotaGraph(pcx, pcy, partFit * pm, part.angle + part.parentTilt, part.handle, TRUE);
    }
}



// ===================================================================================
// UI素材化 — img/UI*.png を使ったUI共通描画ヘルパー
// ===================================================================================
//
// これまでUIは DrawBox による単色の矩形と DrawTriangle で組まれていたが、
// 専用のウィンドウ枠の絵（img/UIウィンドウ.png）と再生/一時停止アイコンが用意されたので、
// UIの見た目をすべてそちらに寄せる。
//
// 素材が「クリーム色の明るいウィンドウ」なので、UI上に載せる文字は
// 従来の白～薄灰ではまったく読めなくなる。そのため文字色も合わせて
// 下記の Ui〜() で定義した暗いインク色に統一する。
// 色を定数(const int)ではなく関数にしてあるのは、GetColor() が
// 描画モードの色深度に依存するため、DxLib_Init より前の静的初期化時に
// 呼ばれる形にしたくないから。

inline int UiInk() { return GetColor(32, 34, 40); }        // 主要テキスト（ほぼ黒）
inline int UiInkSub() { return GetColor(112, 116, 126); }  // 補助テキスト・無効表示
inline int UiInkAccent() { return GetColor(20, 84, 132); } // 強調（ウィンドウ枠の青と同系色）
inline int UiInkWarn() { return GetColor(196, 52, 52); }   // 警告・削除など危険寄りの操作
inline int UiInkOk() { return GetColor(28, 122, 68); }     // 正常・有効を示す緑

// --- 9スライス描画のパラメータ ---
// 9スライスとは、1枚の枠絵を「四隅・上下左右の辺・中央」の9領域に切り分け、
//   ・四隅は引き伸ばさない
//   ・上下の辺は横方向だけ、左右の辺は縦方向だけ引き伸ばす
//   ・中央だけ縦横とも引き伸ばす
// という描き方のこと。こうするとどんな大きさのウィンドウを作っても
// 角の丸みや枠線の太さが潰れず、1枚の素材を全UIで使い回せる。
//
// UIウィンドウ.png は 640x640 で、周囲におよそ 17〜26px の透明な余白がある。
// その余白まで含めて描くと小さいウィンドウが内側に痩せて見えるので、
// 全周 16px を切り落とした内側だけを素材として扱う。
const int UI_WIN_SRC_PAD = 16;                              // 素材から切り落とす透明余白(px)
const int UI_WIN_SRC_SPAN = 640 - UI_WIN_SRC_PAD * 2;       // 実際に使う素材の一辺(px)
const int UI_WIN_SRC_SLICE = 60;                            // 素材側で「枠」として扱う縁の太さ(px)
const int UI_WIN_DEST_SLICE = 14;                           // 画面上での枠の太さ(px)

// UIウィンドウを9スライスで描く。
// handleが未読み込み(-1)の場合は、素材が無くてもUIが消えないよう単色の枠でフォールバックする。
void DrawUiWindow(int x1, int y1, int x2, int y2, int handle) {
    int w = x2 - x1;
    int h = y2 - y1;
    if (w <= 0 || h <= 0) return;
    if (handle < 0) {
        DrawBox(x1, y1, x2, y2, GetColor(252, 246, 236), TRUE);
        DrawBox(x1, y1, x2, y2, GetColor(20, 84, 132), FALSE);
        return;
    }
    // 枠の太さは基本 UI_WIN_DEST_SLICE。ただしウィンドウ自体が小さいときに
    // 左右（上下）の枠がぶつかって潰れないよう、短辺の1/3を上限にする。
    int ds = UI_WIN_DEST_SLICE;
    int limit = (w < h ? w : h) / 3;
    if (ds > limit) ds = limit;
    if (ds < 2) ds = 2;

    const int ss = UI_WIN_SRC_SLICE;
    const int sm = UI_WIN_SRC_SPAN - ss * 2; // 素材の中央部分の一辺
    const int sx0 = UI_WIN_SRC_PAD, sx1 = sx0 + ss, sx2 = sx1 + sm;
    const int sy0 = UI_WIN_SRC_PAD, sy1 = sy0 + ss, sy2 = sy1 + sm;
    const int dx1 = x1 + ds, dx2 = x2 - ds;
    const int dy1 = y1 + ds, dy2 = y2 - ds;

    // 四隅（引き伸ばさず、枠の太さぶんに縮めて描く）
    DrawRectExtendGraph(x1,  y1,  dx1, dy1, sx0, sy0, ss, ss, handle, TRUE);
    DrawRectExtendGraph(dx2, y1,  x2,  dy1, sx2, sy0, ss, ss, handle, TRUE);
    DrawRectExtendGraph(x1,  dy2, dx1, y2,  sx0, sy2, ss, ss, handle, TRUE);
    DrawRectExtendGraph(dx2, dy2, x2,  y2,  sx2, sy2, ss, ss, handle, TRUE);
    // 上下の辺（横方向だけ引き伸ばす）
    DrawRectExtendGraph(dx1, y1,  dx2, dy1, sx1, sy0, sm, ss, handle, TRUE);
    DrawRectExtendGraph(dx1, dy2, dx2, y2,  sx1, sy2, sm, ss, handle, TRUE);
    // 左右の辺（縦方向だけ引き伸ばす）
    DrawRectExtendGraph(x1,  dy1, dx1, dy2, sx0, sy1, ss, sm, handle, TRUE);
    DrawRectExtendGraph(dx2, dy1, x2,  dy2, sx2, sy1, ss, sm, handle, TRUE);
    // 中央（縦横とも引き伸ばす）
    DrawRectExtendGraph(dx1, dy1, dx2, dy2, sx1, sy1, sm, sm, handle, TRUE);
}

// UIアイコンを (cx, cy) を中心に boxSize x boxSize の正方形へ収めて描く。
// 素材はどれも 640x640 の共通キャンバスに中央寄せで描かれているので、
// キャンバスごと正方形に押し込めば自動的に中央揃えになる。
void DrawUiIcon(int cx, int cy, int boxSize, int handle) {
    if (handle < 0 || boxSize <= 0) return;
    int half = boxSize / 2;
    DrawExtendGraph(cx - half, cy - half, cx - half + boxSize, cy - half + boxSize, handle, TRUE);
}

// 定数（ゲーム全体で使う基本的な数値設定）
const int SCREEN_WIDTH = 640;    // ゲーム内部の描画解像度（横）
const int SCREEN_HEIGHT = 480;   // ゲーム内部の描画解像度（縦）
const int WINDOW_WIDTH = 1280;   // 実際に表示するウィンドウの幅（内部解像度から拡大表示する）
const int WINDOW_HEIGHT = 720;   // 実際に表示するウィンドウの高さ

// エディタ下部の一時停止／再開ボタンの矩形。
// 描画側とクリック判定側の両方から参照させて、見た目と当たり判定が絶対にズレないようにする
// （以前は片方だけ広げてしまい、ボタンの端を押しても反応しない領域ができていた）。
// 再生／一時停止アイコン1つぶんだけの正方形寄りサイズ。
const int PAUSE_BUTTON_X1 = WINDOW_WIDTH / 2 - 24;
const int PAUSE_BUTTON_X2 = WINDOW_WIDTH / 2 + 24;
const int PAUSE_BUTTON_Y1 = WINDOW_HEIGHT - 94;
const int PAUSE_BUTTON_Y2 = WINDOW_HEIGHT - 66;
const float GRAVITY = 0.5f;      // 1フレームあたりの重力加速度（Y速度に毎フレーム加算される）
const int MAX_BULLETS = 40;      // 同時に存在できる弾の最大数
const float BULLET_SPEED = 20.0f;// 弾の基本速度
// 弾の描画サイズ(px)。当たり判定側が全ての判定箇所で 16x16 決め打ちになっているため、
// 見た目と判定がズレないよう同じ値を定数化して描画にも使う。
const int BULLET_DRAW_SIZE = 16;
const float JUMP_POWER = -12.0f; // プレイヤーの基本ジャンプ力（負の値で上方向）
const float WALK_SPEED = 4.0f;   // プレイヤーの基本歩行速度
const float DASH_SPEED = 8.0f;   // プレイヤーの基本ダッシュ速度

const int TILE_SIZE = 32;        // マップの1タイルあたりのピクセルサイズ
const int MAP_WIDTH_TILES = 80;  // 2560 / 32 = 80
const int MAP_HEIGHT_TILES = 15; // 480 / 32 = 15

// Feature: カットコストの距離変動 — タイムラインカット1本の作成コストを求める。
//
// startRatio / endRatio はタイムライン帯上の位置を 0.0(ステージ左端)〜1.0(右端) で表したもの。
// この2点の間隔にステージの実幅(stageWidthPx)を掛けると、そのカットが実際に飛ばす距離[px]になる。
// それをタイル数へ直し、1タイルあたり cutCostPerTile を足し込む。
//
//     総コスト = flatCutCreate + cutCostPerTile × 飛ばすタイル数
//
// クリックの順序で start > end になることがあるので、必ず差の絶対値を取る。
float ComputeCutCreateCost(float startRatio, float endRatio, float stageWidthPx, const EditCostSettings& costSettings) {
    float span = endRatio - startRatio;
    if (span < 0.0f) span = -span;
    if (stageWidthPx <= 0.0f) return costSettings.flatCutCreate; // ステージ幅が未確定なら基本コストのみ
    float spanTiles = (span * stageWidthPx) / (float)TILE_SIZE;
    float total = costSettings.flatCutCreate + costSettings.cutCostPerTile * spanTiles;
    if (total < 0.0f) total = 0.0f;
    return total;
}

// マップを構成するタイルの種類。マップデータ(数値配列)の各マスがこの番号で表現される。
enum TileType {
    TILE_NONE = 0,  // 何もない（すり抜けられる空間）
    TILE_JIMEN = 1, // 地面タイル
    TILE_TUTI = 2,  // 土タイル
    TILE_TYPE_MAX   // タイル種別数の番兵
};

// 現在のゲーム全体の進行状態（シーン）。
//
// TITLE と STAGE_SELECT は「ゲームプレイのループに乗らない画面」で、
// メインループの先頭で早期continueして専用の描画へ分岐する。
// そのため CanUpdate やリザルト描画といったゲームプレイ側の処理には一切到達しない。
// 値を足すときは必ず末尾へ。既存の値の並びを崩さないこと。
enum GameScene {
    PLAY,             // 通常プレイ中
    RESULT_GAMEOVER,  // ゲームオーバー結果画面
    RESULT_VICTORY,   // クリア（勝利）結果画面
    TITLE,            // タイトル画面
    STAGE_SELECT      // ステージセレクト画面
};

// 1種類のタイルの見た目・当たり判定情報をまとめた定義データ。
struct TileDefinition {
    TileType type;        // このタイル定義がどのTileTypeに対応するか
    int handle;            // 描画に使う画像ハンドル
    bool isCollidable;     // trueなら地形として衝突判定の対象になる（プレイヤー・敵が乗れる/ぶつかる）
    bool deadly = false;   // 返った場合ただちゲームオーバー
    const char* name;      // デバッグ表示・エディタ表示用の名前
    // Feature: タイル表示範囲調整機能 — spriteが複数タイルをまとめたタイルセット画像の場合に、
    // そのうちどの矩形部分を表示に使うかを指定する。srcW/srcHが0のままなら画像全体を使う
    // （tileDefs構築直後の解決ループで画像サイズへ解決される。従来互換）。
    int srcX = 0, srcY = 0, srcW = 0, srcH = 0;
};

// 履歴上限（時間巻き戻し機能のために毎フレーム状態を記録しておく配列の最大長）
const size_t MAX_HISTORY_FRAMES = 600; // 60fpsで約10秒間

// 巻き戻し機能のために毎フレーム記録する、プレイヤー1フレーム分のスナップショット。
// このState構造体を履歴として貯めておき、巻き戻し操作時に過去のものへ丸ごと復元する。
struct PlayerState {
    float x, y, vx, vy;    // 座標と速度
    int direction;         // 向いている方向（左右）
    bool isJumping;        // ジャンプ中かどうか
    float scale, angle, speedScale; // 表示スケール・回転角度・行動速度倍率
    bool isPaused;          // このフレーム時点で一時停止中だったか
    int hp;                 // このフレーム時点のHP
};

enum EnemyType {
    ENEMY_PATROL,             // 往復パトロールする敵
    ENEMY_JUMPER,             // 繰り返しジャンプする敵
    ENEMY_STATIONARY,         // 固定位置から定期的に射撃する敵
    ENEMY_PATROL_SHOOTER,     // 索敵して射撃する敵（旧DrawPixel2.cpp準拠）

    // ===== ここから追加タイプ（シンプル→トリッキー→複雑）=====
    ENEMY_WALKER,             // 歩いてくる：常にプレイヤー方向へ地上を歩く（崖・壁で反転）
    ENEMY_CHASER,             // 追っかけてくる：WALKERに加え、進路を塞がれると自動ジャンプする
    ENEMY_DASH_CHARGER,       // 突進：射程内に入ると溜めてから高速直進突進、その後クールダウン
    ENEMY_FALLER,             // 落ちてくる敵：待機し、プレイヤーが真下を通ると落下してくる
    ENEMY_SPREAD_SHOOTER,     // 拡散弾：固定位置から3方向に弾を拡散射撃
    ENEMY_AIMED_SHOOTER,      // 照準弾：発射時のプレイヤー位置へ正確に狙い撃つ
    ENEMY_FLOATER,            // 浮遊敵：重力を受けずサインカーブで浮遊しながら接近
    ENEMY_TELEPORTER,         // テレポーター：一定間隔でプレイヤー付近へ瞬間移動する
    ENEMY_SHRINKER,           // 分裂もどき：致死ダメージを受けると一度だけ縮小・高速化して復活する
    ENEMY_SHIELD,             // シールド：一定間隔で無敵状態になり弾を無効化する（無敵中は金色に発光）
    ENEMY_MIMIC_GHOST,        // 幽霊敵：プレイヤーの巻き戻し履歴を遅延再生し、過去の動きをなぞる
    ENEMY_SIZE_SHIFTER,       // 大きさが変わる敵：scaleが周期的に変化し当たり判定も連動して変化
    ENEMY_TEMPO_WARPER,       // 速さ操作敵：speedScaleが周期的に激しく変化し接近速度が乱れる
    ENEMY_BRIGHTNESS_PHANTOM, // 明るさ操作敵：射程内で画面を暗転させる（新画面エフェクト連携）
    ENEMY_COLOR_SHIFTER,      // 色調整敵：射程内で画面を色調変化させる（新画面エフェクト連携）
    ENEMY_ZOOM_DISRUPTOR,     // ズーム撹乱敵：射程内で画面ズームを揺さぶる（新画面エフェクト連携）
    ENEMY_CUSTOM_SCRIPT,      // Feature: Puzzle-like Behavior Scripting (M2) — EnemyDef.scriptのJSONブロックで挙動を自作する
    ENEMY_POUNCER,            // 飛びかかり：普段は高速で地上を追い、射程に入ると溜めてから放物線ジャンプで飛びかかる

    ENEMY_TYPE_COUNT          // 種別数の番兵。新しい敵タイプは必ずこの直前に追加すること
};

// 巻き戻し機能のために毎フレーム記録する、敵1体・1フレーム分のスナップショット。
// 「プレイヤーが編集ツールでこの軸を触ったか」を覚えておくためのビットフラグ。
//
// 編集リアクションの判定は基本的に「配置時の値(editBase*)と現在値の差」を毎フレーム見るだけで済み、
// その方が巻き戻しと自然に噛み合う（値そのものが履歴に入っているため）。
// ただし ENEMY_SIZE_SHIFTER のように「AIが毎フレーム自分でscaleを上書きする」型では
// 差分がAI由来なのか編集由来なのか区別できないので、そういう型のためだけに
// 「触られた事実」を明示的に立てておく。
enum EditDirtyBits : unsigned int {
    EDIT_DIRTY_NONE   = 0u,
    EDIT_DIRTY_SCALE  = 1u << 0, // 拡大・縮小された
    EDIT_DIRTY_ANGLE  = 1u << 1, // 回転された
    EDIT_DIRTY_DIR    = 1u << 2, // 向きを反転された
    EDIT_DIRTY_SPEED  = 1u << 3, // 速度を変えられた
    EDIT_DIRTY_POS    = 1u << 4, // 移動された
    EDIT_DIRTY_WIDTH  = 1u << 5, // 横幅を変えられた（ギミックのみ）
    EDIT_DIRTY_HEIGHT = 1u << 6, // 縦幅を変えられた（ギミックのみ）
};

struct EnemyState {
    float x, y, vx, vy;      // 座標と速度
    int direction;            // 向いている方向
    float scale, angle, speedScale; // 表示スケール・回転角度・行動速度倍率
    bool isActive;             // 生存しているか（撃破済みならfalse）
    bool isPaused;              // このフレーム時点で一時停止中だったか
    EnemyType type;              // 敵の行動タイプ
    int hp;                      // このフレーム時点のHP

    // AI内部状態（巻き戻し時に座標だけでなく状態機械も過去に戻すため保持する）
    float customTimer; // 行動タイマー（周期的な行動の経過時間管理などに使う汎用カウンタ）
    int aiState;        // AIの現在の状態（タイプごとに意味が異なる状態遷移番号）
    float patrolLeft;   // PATROL系の巡回範囲の左端
    float patrolRight;  // PATROL系の巡回範囲の右端
    float auxF1;         // 汎用の補助float値その1（意味は敵タイプごとに異なる）
    float auxF2;         // 汎用の補助float値その2
    int auxState;         // 汎用の補助状態番号
    bool auxFlag;          // 汎用の補助フラグ
    float auxF3;           // 汎用の補助float値その3
};

// 巻き戻し機能のために毎フレーム記録する、弾1発・1フレーム分のスナップショット。
struct BulletState {
    float x, y, vx, vy;   // 座標と速度
    bool isActive;         // 存在しているか
    bool isPlayerOwned;    // プレイヤーが発射した弾か（true）、敵が発射した弾か（false）
};

enum GimmickType {
    GIMMICK_CUT_PORTAL,       // タイムラインのカット・ポータル
    GIMMICK_ROTATING_BRIDGE,  // 自動回転する橋
    GIMMICK_MANUAL_BRIDGE,    // 手動で回転可能な橋
    GIMMICK_BREAKABLE_BLOCK,  // 破壊可能な石ブロック
    GIMMICK_FALLING_LIFT,     // 乗ると落下し、巻き戻すと上昇する砂時計リフト
    GIMMICK_REFLECT_MIRROR,   // 角度に応じてプレイヤーの弾を反射する鏡（Rキーで調整可能）
    GIMMICK_WEIGHT_SWITCH,    // スケール変更したボックスが上に乗ると起動する重量スイッチ
    GIMMICK_SCALABLE_BOX,     // エディター内でSキーによって横幅を変更できるボックス
    GIMMICK_GATE_DOOR,        // 重量スイッチがアクティブなときに開く（isActive=falseになる）ゲート扉
    GIMMICK_SPIKES,           // プレイヤーにダメージを与える即死トゲ床
    GIMMICK_SCALABLE_GROUND,  // 自由にサイズ変更可能な地面ブロック
    GIMMICK_CHIKUWA_BLOCK,    // 乗ると一定時間後に落下するちくわブロック
    GIMMICK_TIME_FIELD,       // この範囲内だけポーズ中でも時間が進む反・一時停止装置
    GIMMICK_CHOMPER,          // プレイヤーが触れると即死し、敵が触れると互いに消滅する敵食らいギミック

    // ===== ここから追加テンプレート（地形ギミック）=====
    GIMMICK_MOVING_PLATFORM,  // 動く足場：val1(上端)～val2(下端)を自動で往復する足場
    GIMMICK_PUSHABLE_ROCK,    // 岩：プレイヤー・敵の進行を塞ぐ固定障害物
    GIMMICK_FASTFORWARD_GATE, // 早送りゲート：早送りモード中(Fキー)だけ通過できる壁
    GIMMICK_FRAMESTEP_LIFT,   // コマ送りリフト：コマ送り操作（一時停止中の→キー）で一歩ずつ動くリフト
    GIMMICK_BRIGHTNESS_ZONE,  // 明暗ゾーン：範囲内にいる間、画面が暗転/明転する
    GIMMICK_COLOR_ZONE,       // 色調ゾーン：範囲内にいる間、画面の色調が変化する
    GIMMICK_ZOOM_LENS,        // ズームレンズ：範囲内にいる間、画面がズームする
    GIMMICK_SLOWMO_FIELD,     // スローフィールド：範囲内でスローモーション演出（視覚効果）がかかる

    // ===== プレイヤーが能動的に使う編集ツール（T/Z/X/C）と連動する地形 =====
    GIMMICK_COLOR_LOCK_PLATFORM,     // 色ロック足場：param("1"/"2"/"3"=赤/緑/青)とプレイヤーの色フィルタが一致する時だけ実体化する
    GIMMICK_BRIGHTNESS_LOCK_PLATFORM,// 明暗ロック足場：param("dark"/"bright")と現在の画面の明るさが一致する時だけ実体化する
    GIMMICK_CUSTOM_SCRIPT,           // Feature: Puzzle-like Behavior Scripting (M2) — GimmickDef.scriptのJSONブロックで挙動を自作する

    GIMMICK_CHECKPOINT               // Feature: チェックポイント — 触れると復帰地点として記録され、ゲームオーバー/落下死からの
                                      // リトライがステージ開始位置ではなくこの地点から再開するようになる（val1>0.5=このチェックポイントが現在の復帰地点）
};

// 巻き戻し機能のために毎フレーム記録する、ギミック1個・1フレーム分のスナップショット。
struct GimmickState {
    float x, y;     // 座標
    float angle;     // 回転角度
    bool isActive;    // 有効/実体化しているか
};

// 将来の拡張用のアイテムシステムテンプレート
enum ItemType {
    ITEM_NONE, // 未設定
    ITEM_COIN, // コイン（スコア/収集要素）
    ITEM_HEAL  // 回復アイテム
};

// 巻き戻し機能のために毎フレーム記録する、アイテム1個・1フレーム分のスナップショット。
struct ItemState {
    float x, y;         // 座標
    bool isCollected;    // 取得済みかどうか
};

// ステージ上に実際に存在するアイテム1個分の実行時状態。
struct Item {
    ItemType type;   // アイテムの種類
    float x, y;       // 現在座標
    float width, height;              // 表示サイズ
    float spriteWidth, spriteHeight;  // 元画像のサイズ
    float hitboxOffsetX, hitboxOffsetY; // 当たり判定オフセット
    float hitboxWidth, hitboxHeight;    // 当たり判定サイズ
    bool isActive;      // 有効かどうか（画面外に出た等で無効化されることがある）
    bool isCollected;    // プレイヤーに取得済みかどうか

    // 巻き戻しトラック
    bool isRewinding;              // 現在巻き戻し再生中かどうか
    std::vector<ItemState> history; // 過去フレームのスナップショット履歴（巻き戻し用）

    std::string assetId = ""; // ItemDef.id への参照（SE検索用）
    int handle = -1;          // ItemDef.graphHandle（カスタムスプライト）。-1ならcoinHandleを使う

    // Feature: Composite Multi-Part Objects (Parts-M1)
    std::vector<PartInstance> parts; // このアイテムを構成する追加パーツ（無ければ空のまま）
};

// 主要な構造体
// マップ上の足場（地形の代わりに使われる線分ベースの当たり判定、または装飾用の直線）を表す。
struct Platform {
    float x1, y1; // 始点座標
    float x2, y2; // 終点座標
};

// プレイヤーキャラクターの実行時状態。
struct Player {
    float x, y;       // 現在座標
    float vx, vy;      // 現在速度
    int handle;         // 描画に使う画像ハンドル
    int direction;       // 向いている方向（左右）
    bool isJumping;       // ジャンプ中かどうか
    int width, height;    // 表示サイズ
    float scale;            // 表示スケール倍率
    float angle;             // 表示回転角度
    float speedScale;         // 行動速度倍率（スロー/早送り等の影響を受ける）
    bool isPaused;              // 一時停止中かどうか
    int hp;                      // 現在HP

    // 被ダメージ後の無敵時間（フレーム）。0より大きい間は敵との接触ダメージを受けない
    float invulnTimer = 0.0f;

    // 現在乗っている動くギミックのインデックス（乗っていなければ-1）。ギミックの移動量を追従させるために使う
    int ridingGimmickIndex = -1;

    // 巻き戻しトラック
    bool isRewinding;                 // 現在巻き戻し再生中かどうか
    std::vector<PlayerState> history; // 過去フレームのスナップショット履歴（巻き戻し用）

    // Feature 2: アニメーション
    AnimationController anim; // スプライトアニメーションの再生状態を管理するコントローラ
};

// 敵1体分の実行時状態。EnemyDef（種類ごとの共通定義）とは別に、個体ごとの現在位置・HP・AI状態を持つ。
struct Enemy {
    EnemyType type;   // 敵の行動タイプ
    float x, y;        // 現在座標
    float vx, vy;        // 現在速度
    int handle;            // 描画に使う画像ハンドル
    int direction;           // 向いている方向
    int width, height;        // 表示サイズ
    int spriteWidth, spriteHeight; // 元画像のサイズ
    int hitboxOffsetX, hitboxOffsetY; // 当たり判定オフセット
    int hitboxWidth, hitboxHeight;    // 当たり判定サイズ
    float scale;   // 表示スケール倍率
    float angle;    // 表示回転角度
    float speedScale; // 行動速度倍率
    bool isActive;      // 生存しているか
    bool isPaused;        // 一時停止中かどうか
    int hp;                 // 現在HP

    // AI用のパラメータ
    float customTimer;
    int aiState;
    float patrolLeft;  // PATROL_SHOOTER用
    float patrolRight; // PATROL_SHOOTER用

    // 巻き戻しトラック
    bool isRewinding;
    std::vector<EnemyState> history;

    // Feature 2: アニメーション
    std::string assetId;
    AnimationController anim;

    // 新規敵タイプ用の汎用スクラッチ領域（末尾追加・デフォルト値ありなので既存の集成初期化を壊さない）
    float auxF1 = 0.0f;   // 例: ダッシュ突進の残り距離、フェイザーの位相
    float auxF2 = 0.0f;   // 例: テレポート先候補、分裂済みフラグ用の残りHP比
    int   auxState = 0;   // 例: シールドON/OFF、テレポートのクールダウン段階
    bool  auxFlag = false; // 例: 分裂済みかどうか
    float auxF3 = 0.0f;   // 例: FALLERのスポーン時X座標（復帰先）

    // 現在乗っている動くギミックのインデックス（乗っていなければ-1）。ギミックの移動量を追従させるために使う
    int ridingGimmickIndex = -1;

    // Feature: Puzzle-like Behavior Scripting (M2) — type==ENEMY_CUSTOM_SCRIPTの実行状態
    ScriptState scriptState;
    ScriptState reactiveState; // OnDamaged/OnDeath専用（Parts-M6）

    // Feature: Composite Multi-Part Objects (Parts-M1)
    std::vector<PartInstance> parts;

    // Feature: 編集リアクション — ステージ配置時（ResetStage）の値をここに焼いておき、
    // 「現在値とどれだけズレているか」でプレイヤーが何を編集したかを毎フレーム導出する。
    // ResetStage以外では書き換わらないので巻き戻し履歴(EnemyState)には入れない。
    // 既存の集成初期化（ステージ読み込み・ハードコードステージ等）を壊さないよう、
    // 必ず既定値つきで構造体の末尾に置くこと。
    float        editBaseScale = 1.0f;  // 配置時のscale
    float        editBaseAngle = 0.0f;  // 配置時のangle
    float        editBaseX = 0.0f;      // 配置時のX（移動されたかの判定と、ホーム追従に使う）
    float        editBaseY = 0.0f;      // 配置時のY
    int          editBaseDirection = 0; // 配置時の向き
    unsigned int editDirtyMask = 0u;    // EditDirtyBitsの論理和
};

// ギミック1個分の実行時状態。GimmickDef（種類ごとの共通定義）とは別に、個体ごとの現在位置・状態を持つ。
struct Gimmick {
    GimmickType type;  // ギミックの種類
    float x, y;          // 現在座標
    float width, height;   // 表示サイズ
    float spriteWidth, spriteHeight; // 元画像のサイズ
    float hitboxOffsetX, hitboxOffsetY; // 当たり判定オフセット
    float hitboxWidth, hitboxHeight;    // 当たり判定サイズ
    bool isActive;    // 有効/実体化しているか
    float val1, val2; // ギミック固有の値 (例: ポータルのワープ先ターゲット比率など)
    float angle;      // 橋の回転角度
    float customTimer; // ギミック固有のタイマー（周期動作等に使用）
    bool isPaused;    // 個別ギミックの一時停止

    // 巻き戻しトラック
    bool isRewinding;               // 現在巻き戻し再生中かどうか
    std::vector<GimmickState> history; // 過去フレームのスナップショット履歴（巻き戻し用）

    // 末尾に追加（既存の集成初期化リストを壊さないようデフォルト値を持たせる）
    std::string assetId = "";  // GimmickDef.id への参照（SE検索・タイプ判定補助用）
    std::string param = "";    // ステージJSONの "param" フィールド（ポータル遷移先など）
    int handle = -1;           // GimmickDef.graphHandle（カスタムスプライト）。-1なら種別ごとの既定画像を使う

    // Feature: Puzzle-like Behavior Scripting (M2) — type==GIMMICK_CUSTOM_SCRIPTの実行状態
    ScriptState scriptState;
    ScriptState reactiveState; // OnDamaged/OnDeath専用（Parts-M6）

    // Feature: Composite Multi-Part Objects (Parts-M1)
    std::vector<PartInstance> parts;

    // Feature: ポータルの作り直し（友人フィードバック対応）— CUT_PORTAL:
    // 前フレームにプレイヤーと接触していたかどうか（同じフレーム内での往復ワープ連鎖や、
    // 転送先に立った瞬間の即時再トリガーを防ぐエッジトリガー用）
    bool portalWasTouching = false;

    // Feature: カット機能の復活 — CUT_PORTALの「2つの使われ方」を区別するためのフラグ。
    //   false : ステージJSONにX,Y配置された通常のワープポータル。同じparamを持つ相手と対で使う。
    //   true  : 下部タイムライン上でCtrl+クリックして作る「タイムラインカット」。
    //           座標は持たず、val1/val2に「ステージ全長に対する比率(0.0〜1.0)」で始点・終点を持つ。
    //           プレイヤーがその区間の境界をまたぐと、区間そのものを飛ばして反対側へ送られる
    //           （動画編集でクリップを切り取ると、再生が切り取った部分を飛ばすのと同じ挙動）。
    // 実行時にCtrl+クリックで生成されるときだけtrueになるので、既存のステージデータには影響しない。
    bool isTimelineCut = false;

    // 動くギミックに乗っているプレイヤー/敵を追従させるための、直近フレームの移動量
    float lastDeltaX = 0.0f, lastDeltaY = 0.0f;

    // Feature: 編集リアクション — ギミックにも「速度」と「向き反転」を持たせる。
    // 従来はこの2つのフィールドが無かったため、インスペクタとコンテキストメニューが
    // ギミック選択時だけ "Speed: N/A" / "Flip: N/A" になり、
    // 6つある編集操作のうち2つがギミックに対して完全に死んでいた。
    float speedScale = 1.0f; // このギミック個体の時間倍率（0で停止、2で倍速）
    int   direction  = 0;    // 0=正方向 / 1=逆方向。往復や回転の向きを反転させるのに使う

    // Feature: 編集リアクション — 配置時の値（詳細はEnemy側の同名フィールドのコメント参照）。
    // width/heightはGimmickStateに入っておらず巻き戻し対象外なので、
    // 「編集は巻き戻らない」という全体方針とも一致する。
    float        editBaseX = 0.0f, editBaseY = 0.0f;
    float        editBaseWidth = 0.0f, editBaseHeight = 0.0f;
    float        editBaseAngle = 0.0f;
    unsigned int editDirtyMask = 0u;
};

// 弾1発分の実行時状態。
struct Bullet {
    float x = 0.0f;   // 現在X座標
    float y = 0.0f;   // 現在Y座標
    float vx = 0.0f;  // X方向速度
    float vy = 0.0f;  // Y方向速度
    bool isActive = false; // 発射中/存在しているか
    int handle = 0;         // 描画に使う画像ハンドル
    bool isPlayerOwned = true; // 弾丸の所有者の追跡用

    // Feature: 編集リアクション — 撃った側のscaleを引き継ぐ弾の大きさ。
    // 砲台を拡大すると大きく遅い弾に、縮小すると小さく速い弾になる、という反応を成立させる。
    // 描画倍率と当たり判定の両方に掛かる。1.0なら従来と完全に同じ。
    float scale = 1.0f;

    // 巻き戻しトラック
    bool isRewinding = false;          // 現在巻き戻し再生中かどうか
    std::vector<BulletState> history; // 過去フレームのスナップショット履歴（巻き戻し用）
};

// タイムライン編集（カット・ポータル）用の「カット地点」情報。
struct CutPoint {
    float timePos;       // このカット地点の元の時間位置
    float targetTimePos; // ワープ先となる時間位置
};

// 右クリックメニュー等、画面上に表示するコンテキストメニューの状態。
struct ContextMenu {
    bool isOpen;               // 現在表示中かどうか
    int x, y, width, height;   // 表示位置とサイズ（画面座標）
};

// 背景として重ねて描画するレイヤー（視差スクロール背景など）の設定。
struct BackgroundLayer {
    std::string sprite;      // 背景画像のパス
    int handle = -1;         // spriteをLoadGraphした結果のハンドル
    int drawOrder = 0;       // 描画順（値が小さいほど奥に描画）
    float scrollRate = 0.3f; // カメラ移動に対するスクロール速度の割合（視差効果。1.0でカメラと同速）
    bool loop = true;        // 画像端に達したときに繰り返し表示するか
    float offsetX = 0.0f;    // 初期表示位置の水平オフセット
    float offsetY = 0.0f;    // 初期表示位置の垂直オフセット
};

// 1ステージ分の全データ（地形マップ、配置物、背景、BGM等）をまとめた構造体。
// stage_XX.jsonから読み込まれ、ステージ開始時にこの内容をもとに実行時のEnemy/Gimmick/Item等が生成される。
struct StageData {
    int id = 0;             // ステージ番号
    char name[64] = "";      // ステージ名（表示用）
    std::string sourceFile = ""; // assets/stages/ 配下のファイル名（GoToStageでの再読み込み判定に使用）
    std::vector<std::vector<int>> map; // 地形タイルマップ（[行][列]にTileTypeの数値が入る2次元配列）

    std::vector<std::vector<int>> decoMapBack;  // プレイヤーより奥に描画される装飾タイルマップ
    std::vector<std::vector<int>> decoMapFront; // プレイヤーより手前に描画される装飾タイルマップ
    std::vector<BackgroundLayer> backgrounds;   // 視差スクロール背景レイヤーの一覧

    std::string bgmId = ""; // このステージで再生するBGMの識別子

    bool testMode = false; // trueの場合、動作確認用の特殊モードでステージを開始する

    json triggersJson = json::array(); // ステージ内のトリガー（イベント発生条件）定義（JSON配列のまま保持）

    float playerStartX = 0.0f; // プレイヤーの初期スポーンX座標
    float playerStartY = 0.0f; // プレイヤーの初期スポーンY座標
    float goalX = -1.0f;   // -1 = ゴール未設定
    float goalY = -1.0f;   // ゴールのY座標
    std::vector<Enemy> enemies;     // このステージに配置される敵の実行時状態一覧
    std::vector<Gimmick> gimmicks;  // このステージに配置されるギミックの実行時状態一覧
    std::vector<Item> items;        // このステージに配置されるアイテムの実行時状態一覧
    std::vector<Platform> platforms;// このステージの足場（線分）一覧

    // Feature: 編集コストゲージ（ステージ単位設定）
    EditToolFlags editToolFlags;         // このステージで許可する編集ツールの設定
    EditCostSettings editCostSettings;   // このステージでの編集コストゲージの数値設定
};

// エディタ上で現在何が選択されているかを表す種別。
enum SelectedType {
    SELECT_NONE,    // 何も選択していない
    SELECT_PLAYER,  // プレイヤーを選択中
    SELECT_ENEMY,   // 敵を選択中
    SELECT_GIMMICK  // ギミックを選択中
};

// このギミックの angle を「AI側が毎フレーム書き換えている」かどうかを返す。
//
// 一部のギミックは angle を自前の状態に使っており、プレイヤーが編集ツールで回すと壊れる：
//   ・GIMMICK_ROTATING_BRIDGE … 毎フレーム rotationSpeed を加算し続ける（＝編集しても即座に上書きされる）
//   ・GIMMICK_CHIKUWA_BLOCK   … angle を「落下中フラグ」(0.0/1.0)として流用している。
//                                少しでも回すと落下中と誤判定され、さらに実体化条件からも外れて
//                                「その場で落ち続ける上に足場でもない」壊れた状態になる。
//   ・GIMMICK_CUSTOM_SCRIPT   … ScriptActor.angle 経由でスクリプトが自由に書き換える
// これらは回転編集の対象から外し、幅や速度など別の軸で編集させる。
bool GimmickAngleIsAiOwned(GimmickType type) {
    return type == GIMMICK_ROTATING_BRIDGE
        || type == GIMMICK_CHIKUWA_BLOCK
        || type == GIMMICK_CUSTOM_SCRIPT;
}

// ============================================================================
// Feature: 編集リアクション（Edit Reaction）
//
// このゲームの編集ツール（拡大・回転・速度・向き反転・一時停止・巻き戻し）は、
// 従来「当たり判定のサイズが変わる」「見た目が回る」程度の汎用的な効果しか持たず、
// 相手が誰であっても同じことしか起きなかった。とくに angle は完全に描画専用で、
// ゲームプレイ上の意味がゼロだった。
//
// ここから下のヘルパは「プレイヤーが配置時の状態からどれだけ手を加えたか」を
// 正規化した差分(EditReaction)として取り出し、敵AIやギミックが読めるようにする。
// これにより「拡大すると重くなる」「傾けると照準が固定される」といった
// 相手ごとに異なる反応を、共通の物差しの上で書けるようになる。
//
// 判定は原則ステートレス（配置時の値との比較）にしてある。scale/angle/direction は
// 巻き戻し履歴に含まれているので、余計な状態を足さなくても巻き戻しと自然に整合する。
// ============================================================================
namespace {
    // しきい値。「プレイヤーがマウスをどれだけ動かしたら反応するか」であって
    // 相手ごとに変える性質のものではないため、ここに一元化して定数で持つ。
    // ドラッグ感度もそれぞれ固定値（scale 0.01/px、angle 0.02rad/px、speed 0.05/px）なので、
    // 下の値はおおよそ「15px / 10px / 5px 動かしたら反応する」に相当する。
    constexpr float EDIT_SCALE_EPS = 0.15f;      // 拡大・縮小とみなす倍率のズレ
    constexpr float EDIT_TILT_EPS  = 0.20f;      // 傾けたとみなす角度のズレ（約11.5度）
    constexpr float EDIT_SPEED_EPS = 0.25f;      // 速度を変えたとみなすズレ
    constexpr float EDIT_FROZEN_SPEED = 0.05f;   // これ未満の速度倍率は「停止」扱い
    constexpr float EDIT_TILT_TIPPED  = 0.7854f; // 45度。これ以上倒れたら姿勢が変わったとみなす
    constexpr float EDIT_TILT_STEP    = 1.0472f; // 60度。傾きを離散段数に落とすときの1段ぶん
    constexpr float EDIT_PI = 3.14159265f;
}

// 角度差を (-PI, PI] へ畳む。
// 回転ドラッグは angle に加算し続けるだけなので、5回転させれば差は31.4radにもなる。
// そのまま「45度以上か」を判定すると常に真になってしまうため、必ずここを通してから使う。
float NormalizeAngle(float a) {
    while (a >   EDIT_PI) a -= 2.0f * EDIT_PI;
    while (a <= -EDIT_PI) a += 2.0f * EDIT_PI;
    return a;
}

// 傾きから離散段数を作る補助。色ロック足場の「要求色を1つ進める」のように、
// 連続値ではなく段階が欲しい場面で使う。
int EditTiltSteps(float tilt) {
    return (int)((tilt >= 0.0f) ? (tilt / EDIT_TILT_STEP + 0.5f) : (tilt / EDIT_TILT_STEP - 0.5f));
}

// EnemyTypeの表示名。インスペクタのType行と、敵タイプ巡回編集の結果確認に使う。
// enumに新しい型を足したらここにも1行足すこと（未対応の値はUNKNOWNになる）。
const char* EnemyTypeName(EnemyType t) {
    switch (t) {
        case ENEMY_PATROL:             return "PATROL";
        case ENEMY_JUMPER:             return "JUMPER";
        case ENEMY_STATIONARY:         return "STATIONARY";
        case ENEMY_PATROL_SHOOTER:     return "PATROL_SHOOTER";
        case ENEMY_WALKER:             return "WALKER";
        case ENEMY_CHASER:             return "CHASER";
        case ENEMY_DASH_CHARGER:       return "DASH_CHARGER";
        case ENEMY_FALLER:             return "FALLER";
        case ENEMY_SPREAD_SHOOTER:     return "SPREAD_SHOOTER";
        case ENEMY_AIMED_SHOOTER:      return "AIMED_SHOOTER";
        case ENEMY_FLOATER:            return "FLOATER";
        case ENEMY_TELEPORTER:         return "TELEPORTER";
        case ENEMY_SHRINKER:           return "SHRINKER";
        case ENEMY_SHIELD:             return "SHIELD";
        case ENEMY_MIMIC_GHOST:        return "MIMIC_GHOST";
        case ENEMY_SIZE_SHIFTER:       return "SIZE_SHIFTER";
        case ENEMY_TEMPO_WARPER:       return "TEMPO_WARPER";
        case ENEMY_BRIGHTNESS_PHANTOM: return "BRIGHTNESS_PHANTOM";
        case ENEMY_COLOR_SHIFTER:      return "COLOR_SHIFTER";
        case ENEMY_ZOOM_DISRUPTOR:     return "ZOOM_DISRUPTOR";
        case ENEMY_CUSTOM_SCRIPT:      return "CUSTOM_SCRIPT";
        case ENEMY_POUNCER:            return "POUNCER";
        default:                       return "UNKNOWN";
    }
}

// プレイヤーが加えた編集を、AI側が読みやすい形へ正規化したもの。
struct EditReaction {
    float scaleRatio  = 1.0f; // 配置時に対する現在の倍率（敵はscale、ギミックはwidth）
    float heightRatio = 1.0f; // 同上（ギミックの縦幅。敵はscaleと同じ値が入る）
    bool  enlarged = false;   // 拡大された
    bool  shrunk   = false;   // 縮小された
    float tilt      = 0.0f;   // 配置時からの傾き（正規化済みラジアン）
    int   tiltSteps = 0;      // 傾きを60度刻みの段数にしたもの（色送りなど離散的な用途向け）
    bool  tilted    = false;  // 傾けられた
    bool  tipped    = false;  // 45度以上倒された（姿勢が変わったとみなせる）
    float speedRatio = 1.0f;  // 速度倍率そのもの
    bool  hastened = false;   // 速くされた
    bool  slowed   = false;   // 遅くされた
    bool  frozen   = false;   // 速度0にされた
    bool  flipped  = false;   // 向きを反転された
    bool  selfPaused    = false; // 個別に一時停止されている
    bool  selfRewinding = false; // 個別に巻き戻し中
    bool  moved    = false;   // 配置位置から動かされた
    float movedX   = 0.0f;    // 配置位置からのXズレ
    float movedY   = 0.0f;    // 配置位置からのYズレ
    bool  angleIsAiOwned = false; // この型のangleはAIが握っており傾け編集を解釈しない

    // 「拡大されたら鈍く、縮小されたら軽快に」という全型共通の重さの倍率。
    float MassMul() const { return (scaleRatio > 0.01f) ? (1.0f / scaleRatio) : 1.0f; }
};

// 敵1体ぶんの編集差分を求める。edefはHP等の定義（NULL可）。
EditReaction GetEnemyEditReaction(const Enemy& e, const EnemyDef* edef) {
    EditReaction r;

    // ENEMY_SHRINKERは「致死ダメージを受けると自分から縮んで復活する」ため、
    // 素の比較では復活した個体が全て「プレイヤーに縮小された」と誤判定されてしまう。
    // 復活済み(auxFlag)なら基準側にも同じ縮小率を掛けて打ち消す。
    // auxFlagはEnemyStateに入っており巻き戻しでも正しく復元されるので、この補正もステートレスに成立する。
    float effBase = e.editBaseScale;
    if (e.type == ENEMY_SHRINKER && e.auxFlag) {
        effBase *= (edef && edef->shrinkFactor > 0.0f) ? edef->shrinkFactor : 0.6f;
    }
    if (effBase > 0.01f) r.scaleRatio = e.scale / effBase;
    r.heightRatio = r.scaleRatio; // 敵はscaleが1つしかないので縦横で分けられない
    r.enlarged = (r.scaleRatio > 1.0f + EDIT_SCALE_EPS);
    r.shrunk   = (r.scaleRatio < 1.0f - EDIT_SCALE_EPS);

    r.tilt      = NormalizeAngle(e.angle - e.editBaseAngle);
    r.tilted    = (r.tilt > EDIT_TILT_EPS || r.tilt < -EDIT_TILT_EPS);
    r.tiltSteps = EditTiltSteps(r.tilt);
    {
        float t = (r.tilt < 0.0f) ? -r.tilt : r.tilt;
        if (t > EDIT_PI * 0.5f) t = EDIT_PI - t; // 180度回しただけなら姿勢は元と同じ
        r.tipped = (t >= EDIT_TILT_TIPPED);
    }

    r.speedRatio = e.speedScale;
    r.hastened = (r.speedRatio > 1.0f + EDIT_SPEED_EPS);
    r.slowed   = (r.speedRatio < 1.0f - EDIT_SPEED_EPS);
    r.frozen   = (r.speedRatio < EDIT_FROZEN_SPEED);

    r.flipped = (e.direction != e.editBaseDirection);
    r.selfPaused    = e.isPaused;
    r.selfRewinding = e.isRewinding;

    r.movedX = e.x - e.editBaseX;
    r.movedY = e.y - e.editBaseY;
    r.moved  = ((e.editDirtyMask & EDIT_DIRTY_POS) != 0u);
    return r;
}

// ギミック1個ぶんの編集差分を求める。
// 敵と違いギミックは横幅と縦幅を独立に編集できるので、scaleRatio/heightRatioが別々の値になる。
EditReaction GetGimmickEditReaction(const Gimmick& g) {
    EditReaction r;
    if (g.editBaseWidth  > 0.01f) r.scaleRatio  = g.width  / g.editBaseWidth;
    if (g.editBaseHeight > 0.01f) r.heightRatio = g.height / g.editBaseHeight;
    r.enlarged = (r.scaleRatio > 1.0f + EDIT_SCALE_EPS) || (r.heightRatio > 1.0f + EDIT_SCALE_EPS);
    r.shrunk   = (r.scaleRatio < 1.0f - EDIT_SCALE_EPS) || (r.heightRatio < 1.0f - EDIT_SCALE_EPS);

    // 自動回転する橋やちくわブロックのようにangleをAI側が握っている型では、
    // 現在角と配置角の差は「プレイヤーの編集」ではないので傾きとして解釈してはいけない。
    r.angleIsAiOwned = GimmickAngleIsAiOwned(g.type);
    if (!r.angleIsAiOwned) {
        r.tilt      = NormalizeAngle(g.angle - g.editBaseAngle);
        r.tilted    = (r.tilt > EDIT_TILT_EPS || r.tilt < -EDIT_TILT_EPS);
        r.tiltSteps = EditTiltSteps(r.tilt);
        float t = (r.tilt < 0.0f) ? -r.tilt : r.tilt;
        if (t > EDIT_PI * 0.5f) t = EDIT_PI - t;
        r.tipped = (t >= EDIT_TILT_TIPPED);
    }

    r.speedRatio = g.speedScale;
    r.hastened = (r.speedRatio > 1.0f + EDIT_SPEED_EPS);
    r.slowed   = (r.speedRatio < 1.0f - EDIT_SPEED_EPS);
    r.frozen   = (r.speedRatio < EDIT_FROZEN_SPEED);

    r.flipped = (g.direction != 0);
    r.selfPaused    = g.isPaused;
    r.selfRewinding = g.isRewinding;

    r.movedX = g.x - g.editBaseX;
    r.movedY = g.y - g.editBaseY;
    r.moved  = ((g.editDirtyMask & EDIT_DIRTY_POS) != 0u);
    return r;
}

// ---- 複合オブジェクトのパーツ追従 — 親ごとの ParentPose の作り方 ----
// 編集差分(EditReaction)は純関数なので、パーツを更新したい場所でいつでも作り直せる。

// 敵の姿勢。敵は倍率を scale 1つでしか持たないので横縦とも同じ値になる。
// 描画側(DrawPixel.cpp の敵本体描画)が hitboxWidth * scale を中心計算に使っているので、
// ピボットの逆算にも同じ値を渡してズレないようにする。
ParentPose MakeEnemyPose(const Enemy& e, const EnemyDef* edef) {
    EditReaction r = GetEnemyEditReaction(e, edef);
    return MakeParentPose(e.x, e.y,
                          (float)e.hitboxWidth * e.scale, (float)e.hitboxHeight * e.scale,
                          r.scaleRatio, r.heightRatio, r.tilt);
}

// ギミックの姿勢。横幅と縦幅を独立に編集できるので sx != sy になりうる。
// 描画は spriteWidth/spriteHeight を見る（SetGimmickWidth が width と同期させている）ので、
// ピボットの逆算にもそちらを渡す。
// 回転橋やちくわブロックのように angle をAIが握る型では GetGimmickEditReaction が
// tilt を0のままにするため、自動回転が傾け編集と誤解されてパーツごと回る事故は起きない。
ParentPose MakeGimmickPose(const Gimmick& g) {
    EditReaction r = GetGimmickEditReaction(g);
    return MakeParentPose(g.x, g.y, g.spriteWidth, g.spriteHeight,
                          r.scaleRatio, r.heightRatio, r.tilt);
}

// アイテムの姿勢。アイテムは編集ツールで選択できない（SelectedTypeにSELECT_ITEMが無い）ため、
// 編集差分は常に無く、変換は恒等になる。それでも同じ経路を通しておくことで、
// 「スクリプトが動かないフレームでもパーツが親に張り付く」という利点だけは共有できる。
ParentPose MakeItemPose(const Item& it) {
    return MakeParentPose(it.x, it.y, it.width, it.height, 1.0f, 1.0f, 0.0f);
}

// このギミックが45度以上倒されているかどうか。
//
// トゲなら刺が横を向いて無害化、扉なら壁ではなく床になる、というように
// 「倒したかどうか」で振る舞いが切り替わる型がいくつもあるため、判定をここに集約する。
bool GimmickIsTipped(const Gimmick& gim) {
    if (GimmickAngleIsAiOwned(gim.type)) return false;
    float t = NormalizeAngle(gim.angle - gim.editBaseAngle);
    if (t < 0.0f) t = -t;
    if (t > EDIT_PI * 0.5f) t = EDIT_PI - t; // 180度回しただけなら姿勢は元と同じ
    return (t >= EDIT_TILT_TIPPED);
}

// ギミックの「実効的な当たり判定ボックス」を返す。
//
// 傾けたギミックの判定を呼び出し側それぞれで書くと、見た目は倒れているのに判定は縦のまま、
// といったズレが必ず生まれる。倒れ判定はここ1箇所に集約し、
// 足場判定・横方向判定・トゲの即死判定などが全て同じ答えを見るようにする。
void GetGimmickCollisionBox(const Gimmick& gim, float& outX, float& outY, float& outW, float& outH) {
    outX = gim.x; outY = gim.y; outW = gim.width; outH = gim.height;
    if (GimmickAngleIsAiOwned(gim.type)) return;

    float t = NormalizeAngle(gim.angle - gim.editBaseAngle);
    if (t < 0.0f) t = -t;
    if (t > EDIT_PI * 0.5f) t = EDIT_PI - t; // 180度回しただけなら姿勢は元と同じ
    if (t < EDIT_TILT_TIPPED) return;        // 45度未満は倒れていないものとして扱う

    // 45度以上倒れている＝縦横が入れ替わった姿勢。中心を動かさずにw/hを入れ替える。
    // （外接矩形にすると45度付近で判定が膨らみ、見た目より広く当たって理不尽になる）
    float cx = gim.x + gim.width * 0.5f;
    float cy = gim.y + gim.height * 0.5f;
    outW = gim.height; outH = gim.width;
    outX = cx - outW * 0.5f;
    outY = cy - outH * 0.5f;
}

// ギミックの横幅・縦幅を変える唯一の入口。
// 判定側はwidth/heightを、描画側はspriteWidth/spriteHeightを見るという二重管理になっているため、
// どちらか片方だけ書き換えると「見た目と判定がズレる」不具合になる。必ずここを通す。
void SetGimmickWidth(Gimmick& g, float w) {
    if (w < 10.0f) w = 10.0f;
    g.width = w;
    g.spriteWidth = w;
}
void SetGimmickHeight(Gimmick& g, float h) {
    if (h < 10.0f) h = 10.0f;
    g.height = h;
    g.spriteHeight = h;
}

// 画面エフェクト系の編集ツール（T=色フィルタ / X=暗転 / C=明転 / Z=ズーム / F=早送り）の現在値。
//
// これらはWinMain内のローカル変数なので、そのままでは名前空間スコープの判定関数から読めない。
// 「明るくすると幽霊が消える」「色を合わせている間だけ実体化する」といった反応は
// 当たり判定側でも参照する必要があるため、毎フレームここへ写して共有する。
struct ScreenFxSnapshot {
    float brightness = 1.0f;  // 1.0が通常。0.6未満で暗い、1.3超で明るいとみなす
    float zoom       = 1.0f;
    int   colorFilter = 0;    // 0=なし, 1=赤, 2=緑, 3=青
    bool  fastForward = false;
};
ScreenFxSnapshot g_screenFx;

// この敵の「弱点色」。色フィルタがこれと一致している間、実体化したり弱ったりする。
// type_enumから機械的に決めているので、今後追加される敵タイプにも自動で割り当たる。
int EnemyWeakColor(EnemyType t) { return ((int)t % 3) + 1; }

// ============================================================================
// Feature: 編集リアクションのJSON宣言（edit_reactions）
//
// ここまでの実装は「型ごとにC++で書いた固有リアクション」と
// 「全オブジェクトへ無条件で効く共通層」の2階建てになっている。
// この宣言層はその間に入る3枚目で、C++を一切書き換えずに
// JSON（enemies.json / gimmicks.json）だけで反応を組めるようにするためのもの。
//
// 今後追加する新しい敵やギミックは、まず共通層の反応が自動で付き、
// 足りなければこの宣言層で味付けし、それでも足りないときだけC++を書く、という順序で作れる。
//
// 書式：
//   "edit_reactions": {
//     "enlarge":     [ { "effect": "MulParam", "param": "moveSpeed", "by": "invScaleRatio" } ],
//     "shrink":      [ { "effect": "Harmless" }, { "effect": "BecomePlatform" } ],
//     "tilt":        [ { "effect": "AimLock" } ],
//     "flip":        [ { "effect": "FriendlyFire" } ],
//     "pause":       [ { "effect": "BecomePlatform" } ],
//     "rewind":      [ { "effect": "Phase" } ],
//     "fastForward": [ { "effect": "MulParam", "param": "attackRate", "by": 2.0 } ],
//     "dark":        [ { "effect": "MulParam", "param": "sightRange", "by": 0.6 } ],
//     "bright":      [ { "effect": "Harmless" } ],
//     "color":       { "2": [ { "effect": "Vulnerable" } ] }
//   }
//
// "by" には数値のほか "scaleRatio" / "invScaleRatio" / "speedRatio" / "invSpeedRatio" /
// "tilt" という変数名も書ける（そのフレームの編集差分がそのまま入る）。
//
// JSONキーを1つ増やすと通常はC++側・既定値補完・Lab_EditorのC#モデル・エディタUIの
// 4箇所を揃える必要があるが、edit_reactionsは入れ子JSONを丸ごと保持する1キーなので、
// 既存の script と同じくC++とC#モデルの2箇所だけで済む。
// ============================================================================

// JSON宣言を1フレーム分解釈した結果。
struct DeclaredEffects {
    // 状態を切り替える効果
    bool harmless       = false; // 触れてもダメージを与えない
    bool becomePlatform = false; // 乗れる足場になる
    bool phase          = false; // すり抜けられる（弾も当たらない）
    bool invulnerable   = false; // 弾を受け付けない
    bool vulnerable     = false; // 無敵状態を強制的に解除する
    bool destroy        = false; // 消滅する
    bool noGravity      = false; // 重力を受けない
    bool ignoreBounds   = false; // 巡回範囲や崖の判定を無視する
    bool aimLock        = false; // 追尾をやめて固定方向を狙う
    bool friendlyFire   = false; // 撃つ弾がプレイヤー側の所属になる
    bool reverseCycle   = false; // 周期・回転・往復の向きが逆になる
    bool lockValue      = false; // AIによる値の自動上書きを止める

    // 数値の倍率。対象は内部フィールド名ではなく「何が変わるか」で名付けてある。
    float mulMoveSpeed  = 1.0f; // 移動の速さ
    float mulSightRange = 1.0f; // 索敵・効果範囲の広さ
    float mulAttackRate = 1.0f; // 攻撃の頻度（大きいほど手数が増える）

    bool any = false; // 1つでも効果が入ったか（何も宣言が無い場合の早期終了用）
};

// "by" に書かれた値を、このフレームの編集差分から実数へ解決する。
// 数値リテラルならそのまま、変数名なら対応する差分の値を返す。
float ResolveReactionAmount(const json& node, const EditReaction& r, float fallback) {
    if (node.is_number()) return node.get<float>();
    if (node.is_string()) {
        const std::string s = node.get<std::string>();
        if (s == "scaleRatio")    return r.scaleRatio;
        if (s == "invScaleRatio") return (r.scaleRatio > 0.01f) ? (1.0f / r.scaleRatio) : 1.0f;
        if (s == "speedRatio")    return r.speedRatio;
        if (s == "invSpeedRatio") return (r.speedRatio > 0.01f) ? (1.0f / r.speedRatio) : 1.0f;
        if (s == "tilt")          return r.tilt;
        if (s == "heightRatio")   return r.heightRatio;
    }
    return fallback;
}

// 効果リスト（1つのトリガーにぶら下がる配列）を解釈して out へ積む。
void ApplyReactionEffectList(const json& list, const EditReaction& r, DeclaredEffects& out) {
    if (!list.is_array()) return;
    for (const auto& item : list) {
        if (!item.is_object()) continue;
        const std::string fx = item.value("effect", "");
        if (fx.empty()) continue;
        out.any = true;

        if      (fx == "Harmless")       out.harmless = true;
        else if (fx == "Deadly")         out.harmless = false;
        else if (fx == "BecomePlatform") out.becomePlatform = true;
        else if (fx == "Phase")          out.phase = true;
        else if (fx == "Invulnerable")   out.invulnerable = true;
        else if (fx == "Vulnerable")     out.vulnerable = true;
        else if (fx == "Destroy")        out.destroy = true;
        else if (fx == "NoGravity")      out.noGravity = true;
        else if (fx == "IgnoreBounds")   out.ignoreBounds = true;
        else if (fx == "AimLock")        out.aimLock = true;
        else if (fx == "FriendlyFire")   out.friendlyFire = true;
        else if (fx == "ReverseCycle")   out.reverseCycle = true;
        else if (fx == "LockValue")      out.lockValue = true;
        else if (fx == "MulParam" || fx == "AddParam") {
            const std::string param = item.value("param", "");
            float amount = ResolveReactionAmount(item.contains("by") ? item["by"] : json(1.0f), r, 1.0f);
            // AddParamは「倍率へ加算する」意味に統一する（1.0を基準とした相対量として扱う）
            if (fx == "AddParam") amount = 1.0f + amount;
            if      (param == "moveSpeed")  out.mulMoveSpeed  *= amount;
            else if (param == "sightRange") out.mulSightRange *= amount;
            else if (param == "attackRate") out.mulAttackRate *= amount;
        }
    }
}

// edit_reactions の宣言全体を、このフレームの編集差分に照らして解釈する。
// 宣言が無ければ何もせず既定値を返すので、既存のアセット定義の挙動は一切変わらない。
DeclaredEffects EvalDeclaredReactions(const json& decl, const EditReaction& r) {
    DeclaredEffects out;
    if (!decl.is_object() || decl.empty()) return out;

    auto fire = [&](const char* key) {
        auto it = decl.find(key);
        if (it != decl.end()) ApplyReactionEffectList(*it, r, out);
    };

    if (r.enlarged)       fire("enlarge");
    if (r.shrunk)         fire("shrink");
    if (r.tilted)         fire("tilt");
    if (r.tipped)         fire("tipped");
    if (r.hastened)       fire("speedUp");
    if (r.slowed)         fire("slow");
    if (r.frozen)         fire("stop");
    if (r.flipped)        fire("flip");
    if (r.selfPaused)     fire("pause");
    if (r.selfRewinding)  fire("rewind");
    if (r.moved)          fire("move");
    if (g_screenFx.fastForward)        fire("fastForward");
    if (g_screenFx.brightness < 0.6f)  fire("dark");
    if (g_screenFx.brightness > 1.3f)  fire("bright");

    // 色フィルタは「どの色か」で分岐できるよう、色番号をキーにした入れ子オブジェクトで書く
    if (g_screenFx.colorFilter != 0) {
        auto itColor = decl.find("color");
        if (itColor != decl.end() && itColor->is_object()) {
            auto itNum = itColor->find(std::to_string(g_screenFx.colorFilter));
            if (itNum != itColor->end()) ApplyReactionEffectList(*itNum, r, out);
        }
    }
    return out;
}

// 敵アセットの宣言を解釈する薄いラッパ（定義が無い個体でも安全に呼べるようにする）。
DeclaredEffects GetEnemyDeclaredEffects(const Enemy& e, const EnemyDef* edef, const EditReaction& r) {
    if (edef == nullptr) return DeclaredEffects();
    (void)e;
    return EvalDeclaredReactions(edef->editReactions, r);
}

// この敵が今「乗れる足場」として振る舞うかどうか。
//
// 共通層のルールとして、個別に一時停止された敵は誰であっても踏み台になる。
// これに加えて、型ごとに「編集された結果おとなしくなった状態」も足場に含める。
bool EnemyIsStandable(const Enemy& e) {
    if (!e.isActive) return false;
    if (e.isPaused) return true; // 共通層：止めた敵は踏み台になる

    // アセット側のJSON宣言（edit_reactions）で BecomePlatform が指定されていればそれに従う
    {
        const EnemyDef* d = FindEnemyDef(e.assetId);
        if (d != nullptr && !d->editReactions.empty()) {
            DeclaredEffects fx = EvalDeclaredReactions(d->editReactions, GetEnemyEditReaction(e, d));
            if (fx.becomePlatform) return true;
        }
    }

    if (e.type == ENEMY_FALLER) {
        // ドッスンは小さくすると着地の衝撃波を起こせなくなり、
        // ただ落ちてくるだけの安全な台になる（乗って運んでもらう使い道が生まれる）。
        EditReaction r = GetEnemyEditReaction(e, FindEnemyDef(e.assetId));
        if (r.shrunk) return true;
    }
    return false;
}

// この敵が今「触れてもダメージを与えない」状態かどうか。
// 足場になる相手に乗った瞬間に被弾しては成立しないので、EnemyIsStandableと足並みを揃える。
bool EnemyIsHarmless(const Enemy& e) {
    if (e.isRewinding) return true; // 巻き戻し中は過去の残像なのですり抜けられる

    // アセット側のJSON宣言（edit_reactions）で Harmless / Phase が指定されていればそれに従う
    {
        const EnemyDef* d = FindEnemyDef(e.assetId);
        if (d != nullptr && !d->editReactions.empty()) {
            DeclaredEffects fx = EvalDeclaredReactions(d->editReactions, GetEnemyEditReaction(e, d));
            if (fx.harmless || fx.phase) return true;
        }
    }

    // まぼろしは光に弱い。画面を明るくしている間は掻き消えて何もできなくなる
    // （時間を止めても効かない相手に対する、画面エフェクト側からの解答）。
    if (e.type == ENEMY_MIMIC_GHOST && g_screenFx.brightness > 1.3f) return true;

    return EnemyIsStandable(e);
}

// この敵が今「弾を受け付けない」状態かどうか。
//
// まぼろしは普段は実体を持たず弾が素通りする。色フィルタ(Tキー)を弱点色に合わせている間だけ
// 実体化して撃てるようになる、という「画面エフェクトで見えないものを掴む」関係にしてある。
bool EnemyIsBulletProof(const Enemy& e) {
    if (e.type == ENEMY_MIMIC_GHOST) {
        return g_screenFx.colorFilter != EnemyWeakColor(e.type);
    }

    // アセット側のJSON宣言（edit_reactions）による無敵／無敵解除
    const EnemyDef* d = FindEnemyDef(e.assetId);
    if (d != nullptr && !d->editReactions.empty()) {
        DeclaredEffects fx = EvalDeclaredReactions(d->editReactions, GetEnemyEditReaction(e, d));
        if (fx.vulnerable) return false;      // Vulnerableは他の無敵指定より優先する
        if (fx.invulnerable || fx.phase) return true;
    }
    return false;
}

// 軸そろえで描いていたギミックを、編集で傾けられた角度どおりに回転描画する。
//
// トゲや扉のように「倒すと役割が変わる」型は、見た目が回らないと
// 何が起きたのかプレイヤーに全く伝わらない。DrawExtendGraphの代わりにこれを使う。
// x/y/w/h はワールド座標のまま渡す（カメラ補正はこの中で行う）。
void DrawGimmickRotated(const Gimmick& gim, int handle, float camX, float camY,
                        float x, float y, float w, float h) {
    if (handle < 0) return;
    int imgW = 1, imgH = 1;
    GetGraphSize(handle, &imgW, &imgH);
    if (imgW <= 0) imgW = 1;
    if (imgH <= 0) imgH = 1;
    float cx = x + w * 0.5f - camX;
    float cy = y + h * 0.5f - camY;
    double rateX = (double)w / imgW;
    double rateY = (double)h / imgH;
    // 描画に使う角度は「配置時からの差」ではなく現在の角度そのもの。
    // 配置時から傾けて置かれているギミック（縦向きの手動橋など）も正しい姿勢で描くため。
    DrawRotaGraph3((int)cx, (int)cy, imgW / 2, imgH / 2, rateX, rateY, gim.angle, handle, TRUE, FALSE);
}

// ScriptActorへ「編集で何をされたか」と画面エフェクトの現在値を詰める。
//
// 敵本体・敵のパーツ・ギミック本体・ギミックのパーツの4箇所から呼ばれる。
// 同じ内容を4回書くとどこかが更新漏れになるので、必ずこの1関数を通す。
// （アイテムのパーツからは呼ばない。アイテムは編集ツールで選択できず、編集差分が常に空のため）
// パーツには親の編集内容をそのまま渡す（親を拡大したら舌も伸びる、という直感に合わせる）。
void FillScriptEditContext(ScriptActor& actor, const EditReaction& r) {
    actor.editScaleRatio = r.scaleRatio;
    actor.editTilt       = r.tilt;
    actor.editSpeedRatio = r.speedRatio;
    actor.editFlipped    = r.flipped;
    actor.editPaused     = r.selfPaused;
    actor.editRewinding  = r.selfRewinding;
    actor.screenColorFilter = g_screenFx.colorFilter;
    actor.screenBrightness  = g_screenFx.brightness;
    actor.screenZoom        = g_screenFx.zoom;
    actor.isFastForwardNow  = g_screenFx.fastForward;
}

// Feature: 編集リアクション（全オブジェクト共通層）—
// 「個別に一時停止された敵」を足場として扱うための着地判定。
//
// どんな敵が相手でも必ず通用する手札として、止めた敵の上に乗れるようにする。
// 時間を止めた相手を踏み台にして高所へ届く、という解法がどのステージでも成立する。
// 既存のCheckPlatformCollisionは呼び出し箇所が多くシグネチャを変えたくないので、別関数として足す。
// selfには「今判定している本人」を渡す（敵が自分自身の上に乗らないようにするため。プレイヤーならnullptr）。
bool CheckFrozenEnemyPlatform(float& x, float& y, float& vy, int width, int height, float scale,
                              const std::vector<Enemy>& enemies, const Enemy* self = nullptr) {
    float objW = (float)width * scale;
    float objH = (float)height * scale;
    float footY = y + objH;

    for (const auto& e : enemies) {
        if (&e == self) continue;
        if (!EnemyIsStandable(e)) continue;
        float ew = (float)e.hitboxWidth * e.scale;
        if (vy >= 0.0f && x <= e.x + ew && x + objW >= e.x) {
            float threshold = vy + 8.0f; // 高速落下時のすり抜け防止で速度ぶんの余裕を持たせる
            if (footY >= e.y && footY <= e.y + threshold) {
                y = e.y - objH;
                vy = 0.0f;
                return true;
            }
        }
    }
    return false;
}

// プラットフォームおよびアクティブな衝突ギミックへの着地衝突判定
// 着地した場合はtrueを返し、オブジェクトのY座標をプラットフォーム上に着坐するように更新します
// outGimmickIndexを渡すと、動くギミックに着地した場合にそのインデックスを書き込む（乗り物追従用、着地しなかった/ギミック以外に着地した場合は-1）
bool CheckPlatformCollision(float& x, float& y, float& vy, int width, int height, float scale, const std::vector<Platform>& platforms, const std::vector<Gimmick>& gimmicks, int* outGimmickIndex = nullptr) {
    if (outGimmickIndex) *outGimmickIndex = -1;
    float objW = (float)width * scale;
    float objH = (float)height * scale;
    float footY = y + objH;
    float footX = x + objW / 2.0f; // 足元の中心点

    // トンネリングを防ぐために許容誤差を設けたAABB衝突を判定するヘルパーラムダ
    auto CheckLanded = [&](float platX1, float platY1, float platX2) {
        // キャラクターのX方向のバウンディングボックスがプラットフォームのX範囲と重なっているか判定
        if (vy >= 0 && x <= platX2 && x + objW >= platX1) {
            // 許容着地判定：足元Y座標がプラットフォーム上面を越え、かつ適切な許容範囲（落下速度 + 8pxバッファ）以内である場合
            float threshold = vy + 8.0f;
            if (footY >= platY1 && footY <= platY1 + threshold) {
                y = platY1 - objH;
                vy = 0.0f;
                return true;
            }
        }
        return false;
    };

    // 1. ステージプラットフォームの衝突判定
    for (const auto& plat : platforms) {
        if (CheckLanded(plat.x1, plat.y1, plat.x2)) return true;
    }

    // 2. インタラクティブギミック（橋や破壊可能ブロックなど）の衝突判定
    for (size_t gi = 0; gi < gimmicks.size(); ++gi) {
        const auto& gim = gimmicks[gi];
        if (!gim.isActive) continue;

        if (gim.type == GIMMICK_ROTATING_BRIDGE || gim.type == GIMMICK_MANUAL_BRIDGE) {
            // 回転する木製の橋の正確な線分座標を算出
            float gcx = gim.x + gim.width / 2.0f;
            float gcy = gim.y + gim.height / 2.0f;
            float dx = cosf(gim.angle) * gim.width / 2.0f;
            float dy = sinf(gim.angle) * gim.width / 2.0f;

            float x1 = gcx - dx;
            float y1 = gcy - dy;
            float x2 = gcx + dx;
            float y2 = gcy + dy;

            // 補間計算の整合性を保つため、(x1, y1)が常に左側の終点になるようにソート
            if (x1 > x2) {
                float temp = x1; x1 = x2; x2 = temp;
                temp = y1; y1 = y2; y2 = temp;
            }

            // プレイヤーの足元の中心X座標が、回転した橋のX範囲内に収まっているか判定
            if (footX >= x1 && footX <= x2) {
                // もし橋が完全に垂直（x1 == x2）でない場合
                if (fabsf(x2 - x1) > 1.0f) {
                    // 線形補間により、footXにおける傾斜した橋の正確なY座標（高さ）を算出
                    float t = (footX - x1) / (x2 - x1);
                    float platY = y1 + t * (y2 - y1);

                    // 傾斜面に対する許容着地判定
                    float threshold = vy + 8.0f;
                    if (vy >= 0 && footY >= platY && footY <= platY + threshold) {
                        y = platY - objH;
                        vy = 0.0f;
                        return true;
                    }
                }
            }
        }
        else if (gim.type == GIMMICK_BREAKABLE_BLOCK || gim.type == GIMMICK_FALLING_LIFT || gim.type == GIMMICK_SCALABLE_BOX || gim.type == GIMMICK_GATE_DOOR
                 || gim.type == GIMMICK_MOVING_PLATFORM || gim.type == GIMMICK_FRAMESTEP_LIFT || gim.type == GIMMICK_PUSHABLE_ROCK
                 || gim.type == GIMMICK_COLOR_LOCK_PLATFORM || gim.type == GIMMICK_BRIGHTNESS_LOCK_PLATFORM
                 || gim.type == GIMMICK_SPIKES  // Feature: 編集リアクション — 45度以上倒したトゲは無害な足場になる
                 || (gim.type == GIMMICK_CHIKUWA_BLOCK && gim.angle == 0.0f)) {
            // 倒したトゲは「刺さらない床」としてだけ足場になる。立っているトゲは触れれば即死のままなので
            // ここでは足場にしない（乗れてしまうと即死判定と矛盾する）。
            if (gim.type == GIMMICK_SPIKES && !GimmickIsTipped(gim)) continue;

            // Feature: 編集リアクション — 傾けて倒したギミックは縦横が入れ替わった姿勢になる。
            // 見た目と判定がズレないよう、実効ボックスの算出は必ずこのヘルパに通す。
            float gbx, gby, gbw, gbh;
            GetGimmickCollisionBox(gim, gbx, gby, gbw, gbh);
            if (CheckLanded(gbx, gby, gbx + gbw)) {
                if (outGimmickIndex) *outGimmickIndex = (int)gi;
                return true;
            }
        }
    }

    return false;
}

// X方向のタイルマップ衝突判定と補正
void CheckGridCollisionX(float& x, float y, int width, float scale, int height, const std::vector<std::vector<int>>& map, const std::vector<TileDefinition>& tileDefs, float vx) {
    if (vx == 0.0f) return;
    
    float objW = (float)width * scale;
    float objH = (float)height * scale;
    
    int startY = (int)(y / TILE_SIZE);
    int endY = (int)((y + objH - 1.0f) / TILE_SIZE);
    
    int mapH = (int)map.size();
    if (mapH == 0) return;
    int mapW = (int)map[0].size();
    
    // 左方向への移動（左側の壁との衝突）
    if (vx < 0.0f) {
        int startX = (int)(x / TILE_SIZE);
        float cellRightX = (float)(startX + 1) * TILE_SIZE;
        float toleranceX = std::abs(vx) + 8.0f; // 高速移動時のすり抜け防止のため速度依存にする
        if (x < cellRightX && x >= cellRightX - toleranceX) {
            for (int ty = startY; ty <= endY; ++ty) {
                if (ty < 0 || ty >= mapH) continue;
                if (startX >= 0 && startX < mapW) {
                    int tid = map[ty][startX];
                    if (tid >= 0 && tid < (int)tileDefs.size() && tileDefs[tid].isCollidable) {
                        x = cellRightX;
                        break;
                    }
                }
            }
        }
    }
    // 右方向への移動（右側の壁との衝突）
    else if (vx > 0.0f) {
        int endX = (int)((x + objW) / TILE_SIZE);
        float cellLeftX = (float)endX * TILE_SIZE;
        float toleranceX = std::abs(vx) + 8.0f; // 高速移動時のすり抜け防止のため速度依存にする
        if (x + objW > cellLeftX && x + objW <= cellLeftX + toleranceX) {
            for (int ty = startY; ty <= endY; ++ty) {
                if (ty < 0 || ty >= mapH) continue;
                if (endX >= 0 && endX < mapW) {
                    int tid = map[ty][endX];
                    if (tid >= 0 && tid < (int)tileDefs.size() && tileDefs[tid].isCollidable) {
                        x = cellLeftX - objW;
                        break;
                    }
                }
            }
        }
    }
}

// Y方向のタイルマップ衝突判定と補正
bool CheckGridCollisionY(float x, float& y, float& vy, int width, float scale, int height, const std::vector<std::vector<int>>& map, const std::vector<TileDefinition>& tileDefs) {
    float objW = (float)width * scale;
    float objH = (float)height * scale;
    
    int startX = (int)(x / TILE_SIZE);
    int endX = (int)((x + objW - 1.0f) / TILE_SIZE);
    
    int mapH = (int)map.size();
    if (mapH == 0) return false;
    int mapW = (int)map[0].size();
    
    bool isGrounded = false;
    
    // 下方向の衝突（着地）
    if (vy >= 0) {
        int footY = (int)((y + objH) / TILE_SIZE);
        float cellTopY = (float)footY * TILE_SIZE;
        float threshold = vy + 8.0f; // 落下速度に応じた適度なめり込み許容値
        if (y + objH >= cellTopY && y + objH <= cellTopY + threshold) {
            for (int tx = startX; tx <= endX; ++tx) {
                if (tx < 0 || tx >= mapW) continue;
                if (footY >= 0 && footY < mapH) {
                    int tid = map[footY][tx];
                    if (tid >= 0 && tid < (int)tileDefs.size() && tileDefs[tid].isCollidable) {
                        y = cellTopY - objH;
                        vy = 0.0f;
                        isGrounded = true;
                        break;
                    }
                }
            }
        }
    }
    // 上方向の衝突（頭ぶつけ）
    else if (vy < 0) {
        int headY = (int)(y / TILE_SIZE);
        float cellBottomY = (float)(headY + 1) * TILE_SIZE;
        float threshold = -vy + 8.0f;
        if (y <= cellBottomY && y >= cellBottomY - threshold) {
            for (int tx = startX; tx <= endX; ++tx) {
                if (tx < 0 || tx >= mapW) continue;
                if (headY >= 0 && headY < mapH) {
                    int tid = map[headY][tx];
                    if (tid >= 0 && tid < (int)tileDefs.size() && tileDefs[tid].isCollidable) {
                        y = cellBottomY;
                        vy = 0.0f;
                        break;
                    }
                }
            }
        }
    }
    return isGrounded;
}

// X方向についての、実体化した地形系ギミック（伸縮ボックス、伸縮地面、岩、早送りゲート、ゲート扉）との衝突判定。
// タイルマップの衝突とは別に、ギミック側の座標・サイズをもとにX座標を押し戻す。
void CheckGimmickCollisionX(float& x, float y, int width, float scale, int height, float vx, const std::vector<Gimmick>& gimmicks) {
    float objW = (float)width * scale;
    float objH = (float)height * scale;
    
    for (const auto& gim : gimmicks) {
        if (!gim.isActive) continue;
        // Feature: ゲート扉の修正（友人フィードバック対応）— GIMMICK_GATE_DOORを横方向の実体判定に追加。
        // isActive==true（施錠中/閉状態）の間だけこのループに乗り、通行を塞げるようにする
        // （元々ここに無かったため、閉じているはずのドアを素通りできてしまっていた）。
        if (gim.type == GIMMICK_SCALABLE_BOX || gim.type == GIMMICK_SCALABLE_GROUND || gim.type == GIMMICK_PUSHABLE_ROCK || gim.type == GIMMICK_FASTFORWARD_GATE || gim.type == GIMMICK_GATE_DOOR) {
            // Feature: 編集リアクション — 倒した扉は「壁」ではなく「床」になるので、
            // 横方向の実体判定からは外れる。実効ボックスで縦横の入れ替えも反映する。
            if (gim.type == GIMMICK_GATE_DOOR && GimmickIsTipped(gim)) continue;
            float gbx, gby, gbw, gbh;
            GetGimmickCollisionBox(gim, gbx, gby, gbw, gbh);

            // Y軸の重なり判定（少しの遊びを持たせる）
            if (y + objH > gby + 4.0f && y < gby + gbh - 4.0f) {
                float toleranceGX = std::abs(vx) + 12.0f; // 高速移動時のすり抜け防止のため速度依存にする
                if (vx > 0.0f) { // 右方向へ移動中
                    if (x < gbx && x + objW > gbx && x + objW <= gbx + toleranceGX) {
                        x = gbx - objW;
                    }
                }
                else if (vx < 0.0f) { // 左方向へ移動中
                    if (x > gbx + gbw - toleranceGX && x < gbx + gbw) {
                        x = gbx + gbw;
                    }
                }
            }
        }
    }
}

// Y方向についての、実体化した地形系ギミックとの衝突判定。着地した場合はtrueを返す。
bool CheckGimmickCollisionY(float x, float& y, int width, float scale, int height, float& vy, const std::vector<Gimmick>& gimmicks) {
    float objW = (float)width * scale;
    float objH = (float)height * scale;
    bool isGrounded = false;
    
    for (const auto& gim : gimmicks) {
        if (!gim.isActive) continue;
        if (gim.type == GIMMICK_SCALABLE_BOX || gim.type == GIMMICK_SCALABLE_GROUND || gim.type == GIMMICK_PUSHABLE_ROCK || gim.type == GIMMICK_FASTFORWARD_GATE
            || (gim.type == GIMMICK_GATE_DOOR && GimmickIsTipped(gim))) {
            // Feature: 編集リアクション — 倒した扉はここで床として拾う（横の壁判定からは外れている）。
            float gbx, gby, gbw, gbh;
            GetGimmickCollisionBox(gim, gbx, gby, gbw, gbh);

            // X軸の重なり判定
            if (x + objW > gbx + 2.0f && x < gbx + gbw - 2.0f) {
                if (vy >= 0.0f) { // 下方向へ移動中（着地）
                    float footY = y + objH;
                    float threshold = vy + 12.0f;
                    if (footY >= gby && footY <= gby + threshold) {
                        y = gby - objH;
                        vy = 0.0f;
                        isGrounded = true;
                    }
                }
                else if (vy < 0.0f) { // 上方向へ移動中（頭ぶつけ）
                    float threshold = -vy + 12.0f;
                    if (y <= gby + gbh && y >= gby + gbh - threshold) {
                        y = gby + gbh;
                        vy = 0.0f;
                    }
                }
            }
        }
    }
    return isGrounded;
}


// タイルマップとギミック両方を対象にした物理移動＋衝突判定のまとめ関数。
// X方向とY方向を分けて処理する（先にXを動かして壁判定、その後Yを動かして床/天井判定）ことで、
// 斜め移動時にも壁・床の判定が正しく効くようにしている。戻り値は「着地したか」。
bool UpdatePhysicsCollisions(float& x, float& y, float dx, float dy, float& vy, int width, int height, float scale,
                             const std::vector<std::vector<int>>& map, const std::vector<TileDefinition>& tileDefs,
                             const std::vector<Gimmick>& gimmicks) {
    // 先にX方向へ移動させてから、タイル・ギミック両方の左右衝突を判定して座標を補正する
    x += dx;
    CheckGridCollisionX(x, y, width, scale, height, map, tileDefs, dx);
    CheckGimmickCollisionX(x, y, width, scale, height, dx, gimmicks);

    // 続いてY方向へ移動させてから、タイル・ギミック両方の上下衝突を判定して座標を補正する
    y += dy;
    bool gridGrounded = CheckGridCollisionY(x, y, vy, width, scale, height, map, tileDefs);
    bool gimGrounded = CheckGimmickCollisionY(x, y, width, scale, height, vy, gimmicks);

    return gridGrounded || gimGrounded;
}

// プログラムのエントリーポイント（Windowsアプリケーションのmain関数に相当）。
// DxLib/ImGuiの初期化、アセット読み込み、全ステージデータの構築、そしてメインループの実行までを行う、
// このファイルで最も大きな関数。
int WINAPI WinMain(_In_ HINSTANCE h, _In_opt_ HINSTANCE hp, _In_ LPSTR l, _In_ int n)
{
    // ★最初にやること: カレントディレクトリをゲームデータ(assets/ img/ sound/ se/)の
    // 置き場所へ移す。
    //
    // このゲームはアセットを全てCWDからの相対パスで参照している（約67箇所）。
    // exeの出力先は x64\Debug\ でアセットはリポジトリのルート直下という別階層なので、
    // これを行わないと exe をダブルクリックしても何も読み込めない。
    // これまで動いていたのは Lab_Editor が WorkingDirectory を指定して
    // 起動していたからにすぎず、ゲーム単体では配布できない状態だった。
    //
    // ログ出力(Logger)より前に呼ぶ。そうしないと set_terminate ハンドラ内のログが
    // 移動前の場所へ書かれてしまう。
    bool gameRootFound = GamePaths::ResolveAndSetGameRoot();

    // 未処理の例外でstd::terminateが呼ばれた場合のハンドラを登録しておく。
    // 何も対処しないとプログラムが無言で落ちてしまい原因調査が困難なため、
    // 例外の内容（何のエラーだったか）を可能な限りログファイルに書き残してからabortする。
    std::set_terminate([]() {
        Logger::Error("System", "terminate_handler", "std::terminate was called! Unhandled exception.");
        try {
            if (std::current_exception()) {
                std::rethrow_exception(std::current_exception());
            }
        } catch (const std::exception& e) {
            Logger::Error("System", "terminate_handler", std::string("Exception what(): ") + e.what());
        } catch (...) {
            Logger::Error("System", "terminate_handler", "Unknown exception type");
        }
        std::abort();
    });

    Logger::Info("System", "WinMain", "[Init] WinMain Begin");

    // ゲームデータが見つからなかった場合は、ここで理由を伝えて終了する。
    // 黙って真っ白な画面が出て終わるのが配布版で最も困る失敗の仕方なので、
    // 「何が足りないのか」と「どうすれば直るのか」をその場で見せる。
    if (!gameRootFound) {
        Logger::Error("System", "WinMain", "Game data (assets folder) not found near the executable");
        MessageBoxW(NULL,
            L"ゲームデータ(assetsフォルダ)が見つかりませんでした。\n\n"
            L"zipを展開したフォルダの中身を移動していないか確認してください。\n"
            L"実行ファイルと同じ場所に assets / img / sound / se フォルダが必要です。",
            L"Lab Project 01", MB_OK | MB_ICONERROR);
        return -1;
    }

    // DxLibが自動生成する Log.txt を作らない（DxLib_Initより前でのみ有効）。
    // カレントディレクトリ直下に毎回書き出されるため、追跡していると差分が出続けるうえ、
    // 配布版ではインストール先へ書き込むことになる。DxLib自体の診断が要る場面は稀。
    SetOutApplicationLogValidFlag(FALSE);

    // DxLibへ渡す文字列の扱いをUTF-8にする（DxLib_Initより前に呼ぶ必要がある）。
    // 既定はShift-JIS解釈のため、assets/*.json（UTF-8）由来の日本語をDrawStringへ渡すと
    // 文字化けしていた（ShowMessageのヒント等）。ソースもUTF-8なので全体をUTF-8で統一する。
    // なお既存のDrawString呼び出しの文字列は全てASCIIのため、この変更で崩れる表示は無い。
    SetUseCharCodeFormat(DX_CHARCODEFORMAT_UTF8);

    ChangeWindowMode(TRUE); // ウィンドウモードで起動する（フルスクリーンにしない）
    SetGraphMode(WINDOW_WIDTH, WINDOW_HEIGHT, 32); // ウィンドウの解像度とカラービット数を設定
    if (DxLib_Init() == -1) return -1; // DxLibの初期化に失敗したら即座に終了する
    Logger::Info("System", "WinMain", "[Init] DxLib_Init Success");
    // コマンドライン引数でステージファイル名が渡されていれば、起動時に読み込むステージを上書きする
    // （Lab_Editorから「このステージでテストプレイ」を実行したときに使われる仕組み）
    // 引数が付いているかどうかが、そのまま「Lab_Editorからテストプレイで起動されたか」の
    // 判定になる。エディタから来た場合はタイトル画面を挟まず、指定されたステージへ直行する
    // （毎回タイトルを経由させられてはテストプレイにならない）。
    bool bootDirectToStage = false;
    if (l != nullptr && strlen(l) > 0) {
        bootDirectToStage = true;
        currentStageFileName = l;
        // コマンドライン引数の前後に空白/引用符が付くケースへの防御（呼び出し元により混入することがある）
        while (!currentStageFileName.empty() && (currentStageFileName.front() == ' ' || currentStageFileName.front() == '"')) currentStageFileName.erase(0, 1);
        while (!currentStageFileName.empty() && (currentStageFileName.back() == ' ' || currentStageFileName.back() == '"')) currentStageFileName.pop_back();
        if (!currentStageFileName.empty() && currentStageFileName.find(".json") == std::string::npos) {
            currentStageFileName += ".json";
        }
    }
    SetDrawScreen(DX_SCREEN_BACK); // 描画先を裏画面にする（ちらつき防止のダブルバッファリング）

    HWND hwnd = GetMainWindowHandle();
    SetHookWinProc(CustomWndProc); // 先ほど定義した独自ウィンドウプロシージャをDxLibに登録する

    // ---- ImGui（デバッグ/エディタ用UIライブラリ）の初期化 ----
    IMGUI_CHECKVERSION();
    ImGui::CreateContext();
    ImGuiIO& io = ImGui::GetIO(); (void)io;
    io.ConfigFlags |= ImGuiConfigFlags_DockingEnable; // ウィンドウ同士をドッキング（連結）できるようにする
    io.ConfigFlags |= ImGuiConfigFlags_ViewportsEnable; // マルチビューポートを有効化（UIを独立したOSウィンドウにできる）
    // imgui.ini を書き出さない。カレントディレクトリ直下に毎回生成されるため、
    // 追跡していると差分が出続けるうえ、配布版ではインストール先へ書き込むことになる。
    // ImGuiのUIは現状ほぼ使われていないので、ウィンドウ配置を保存する価値もない。
    io.IniFilename = nullptr;
    // 日本語（メイリオ）フォントを読み込み、日本語グリフ範囲を指定してUI上で日本語表示できるようにする。
    // メイリオが無い環境（システムドライブがC:でない等）でも落ちないよう戻り値を確認する。
    // 読めなかった場合はImGuiの既定フォント（英数のみ）になるだけで、ゲーム本体には影響しない。
    if (io.Fonts->AddFontFromFileTTF("C:\\Windows\\Fonts\\meiryo.ttc", 18.0f, NULL, io.Fonts->GetGlyphRangesJapanese()) == nullptr) {
        Logger::Info("System", "WinMain", "[Init] meiryo.ttc not available; ImGui falls back to the default font");
    }

    ImGui::StyleColorsDark(); // UIの配色をダークテーマにする

    // ImGuiをWin32＋DirectX11のバックエンドに接続する（DxLibが内部でDirectX11を使っているため）
    ImGui_ImplWin32_Init(hwnd);
    ImGui_ImplDX11_Init((ID3D11Device*)GetUseDirect3D11Device(), (ID3D11DeviceContext*)GetUseDirect3D11DeviceContext());

    // ゲーム内部解像度(SCREEN_WIDTH x SCREEN_HEIGHT)で描画するための仮想スクリーンを作成する。
    // 実際のウィンドウサイズ(WINDOW_WIDTH x WINDOW_HEIGHT)へは、この仮想スクリーンを拡大して転送する。
    int gameScreen = MakeScreen(SCREEN_WIDTH, SCREEN_HEIGHT, TRUE);
    LoadAssetDefinitions(); // 敵/アイテム/ギミックの定義データ(enemies.json等)を読み込む

    // ゲーム全体の設定（タイトル画面の内容・ステージ一覧・テーマ色など）を読み込む。
    // ファイルが無い/壊れている場合は titleEnabled=false になるだけで、
    // 従来どおり起動即プレイになる（設定が壊れても遊べなくならない）。
    GameCfg::GameConfig gameConfig;
    GameCfg::LoadGameConfig("assets/game_config.json", gameConfig);
    if (!gameConfig.windowTitle.empty()) {
        SetMainWindowText(gameConfig.windowTitle.c_str());
    }

    // 進行状況（どのステージをクリアしたか・最高取得数）。
    // 置き場所は %LOCALAPPDATA%\LabProject01\save.json。
    // 初回起動ではファイルが無いので false が返るが、それは正常な状態。
    GameCfg::SaveData saveData;
    GameCfg::LoadSaveData(saveData);
    {
        // Feature 5: コモンイベント定義の読み込み。CallCommonEventアクションから参照される。
        std::ifstream cef("assets/common_events.json");
        if (cef.is_open()) {
            json cej = json::parse(cef, nullptr, false);
            if (!cej.is_discarded()) {
                EventManager::Get().LoadCommonEventsFromJson(cej);
            } else {
                Logger::Error("DrawPixel", "WinMain", "Failed to parse common_events.json", "assets/common_events.json");
            }
        }
    }
    // 共通で使う画像素材をあらかじめすべて読み込んでおく（毎フレーム読み込むと重いため起動時に一括ロード）。
    // 【重要】ここで読む素材は img/ 直下の日本語ファイル名に全面移行した。旧素材（player.png 等）は
    // リポジトリから一括削除されており、参照し続けると全ハンドルが -1 になって何も描画されなくなる。
    // これらは「アセット定義側に個別スプライトが無かった場合のフォールバック」も兼ねているため、
    // 1枚でも欠けると該当ギミックが不可視になる点に注意すること。
    int playerHandle = LoadGraph("img/プレイヤー.png");
    int bulletHandle = LoadGraph("img/弾.png");
    int jimenHandle = LoadGraph("img/地面緑.png");
    int tutiHandle = LoadGraph("img/地面黄.png");
    int portalHandle = LoadGraph("img/ワープゲート.png");
    int bridgeHandle = LoadGraph("img/床青.png");
    int breakableBlockHandle = LoadGraph("img/地面ピンク.png");
    int liftHandle = LoadGraph("img/乗ったら落ちる床.png");
    int mirrorHandle = LoadGraph("img/床青.png");
    int switchHandle = LoadGraph("img/スイッチオフ.png");
    int boxHandle = LoadGraph("img/地面黄緑.png");
    int doorHandle = LoadGraph("img/扉.png");
    int spikesHandle = LoadGraph("img/棘.png");
    int coinHandle = LoadGraph("img/コイン.png");

    // UI・演出専用の画像素材。従来これらは DrawBox / DrawTriangle による自前のプリミティブ描画で
    // 代用していたが、専用の絵が用意されたので画像に置き換える。
    int uiWindowHandle = LoadGraph("img/UIウィンドウ.png");       // 汎用ウィンドウ枠（9スライスで引き伸ばす）
    int uiPlayHandle = LoadGraph("img/UI再生中.png");             // 再生中(▶)アイコン
    int uiPauseHandle = LoadGraph("img/UI一時停止中.png");        // 一時停止中(||)アイコン
    int energyHandle = LoadGraph("img/エネルギー.png");           // 編集コストゲージのアイコン
    int cursorHandle = LoadGraph("img/マウスホイール.png");       // ゲーム内マウスカーソル（実物は矢印の絵）
    int goalHandle = LoadGraph("img/ゴール.png");                 // ゴール地点
    int checkpointHandle = LoadGraph("img/チェックポイント.png"); // チェックポイントの旗
    int switchOnHandle = LoadGraph("img/スイッチオン.png");       // 押し込まれた状態のスイッチ

    // 新アセット移行の自己診断 —
    // 素材はすべてファイル名が日本語なので、文字コードの設定が1つでも噛み合っていないと
    // LoadGraph が黙って -1 を返し、「ビルドは通るのに画面が真っ黒」という追いにくい壊れ方をする。
    // （具体的には .vcxproj の /utf-8 と SetUseCharCodeFormat(DX_CHARCODEFORMAT_UTF8) の組み合わせ。
    //   どちらかが欠けるとリテラルの文字コードとDxLibの解釈がずれる。）
    // どのファイルが読めなかったのかを起動直後にログへ落としておけば、原因の切り分けが一目で済む。
    {
        struct { const char* label; int handle; } loadedGraphs[] = {
            { "img/プレイヤー.png",        playerHandle },
            { "img/弾.png",                bulletHandle },
            { "img/地面緑.png",            jimenHandle },
            { "img/地面黄.png",            tutiHandle },
            { "img/ワープゲート.png",      portalHandle },
            { "img/床青.png",              bridgeHandle },
            { "img/地面ピンク.png",        breakableBlockHandle },
            { "img/乗ったら落ちる床.png",  liftHandle },
            { "img/スイッチオフ.png",      switchHandle },
            { "img/地面黄緑.png",          boxHandle },
            { "img/扉.png",                doorHandle },
            { "img/棘.png",                spikesHandle },
            { "img/コイン.png",            coinHandle },
            { "img/UIウィンドウ.png",      uiWindowHandle },
            { "img/UI再生中.png",          uiPlayHandle },
            { "img/UI一時停止中.png",      uiPauseHandle },
            { "img/エネルギー.png",        energyHandle },
            { "img/マウスホイール.png",    cursorHandle },
            { "img/ゴール.png",            goalHandle },
            { "img/チェックポイント.png",  checkpointHandle },
            { "img/スイッチオン.png",      switchOnHandle },
        };
        int failedCount = 0;
        for (const auto& g : loadedGraphs) {
            if (g.handle < 0) {
                failedCount++;
                Logger::Error("DrawPixel", "WinMain", "Failed to load graph", g.label);
            }
        }
        if (failedCount == 0) {
            Logger::Info("DrawPixel", "WinMain", "[Init] All core graphics loaded");
        } else {
            Logger::Error("DrawPixel", "WinMain", "Some core graphics failed to load (check /utf-8 build option)",
                          std::to_string(failedCount) + " missing");
        }
    }

    // tileDefs: tiles.json から動的に読み込む (なければデフォルト3種)
    std::vector<TileDefinition> tileDefs;
    {
        tileDefs.push_back({ TILE_NONE, -1, false, false, "None" });
        tileDefs.push_back({ TILE_JIMEN, jimenHandle, true, false, "Jimen" });
        tileDefs.push_back({ TILE_TUTI, tutiHandle, true, false, "Tuti" });

        // tiles.json が存在すれば deadly フラグを反映
        std::ifstream tilesFile("assets/tiles.json");
        if (tilesFile.is_open()) {
            json tj = json::parse(tilesFile, nullptr, false);
            if (tj.is_discarded()) {
                Logger::Error("DrawPixel", "WinMain", "Failed to parse tiles.json");
            } else if (tj.is_array()) {
                for (const auto& t : tj) {
                    if (!t.is_object()) continue;
                    int tid = t.value("id", -1);
                    bool deadly = t.value("deadly", false);
                    bool collidable = t.value("collidable", true);
                    std::string spritePath = t.value("sprite", "");
                    // Feature: タイル表示範囲調整機能 — 0のままなら後段の解決ループで画像全体に解決される
                    int srcX = t.value("srcX", 0);
                    int srcY = t.value("srcY", 0);
                    int srcW = t.value("srcW", 0);
                    int srcH = t.value("srcH", 0);

                    int handle = -1;
                    if (!spritePath.empty()) {
                        // 【重要】tiles.json に書かれるパスは Lab_Editor が "img/xxx.png" 形式で書き出す。
                        // 以前はここで先に "assets/" を前置していたため、常に存在しない
                        // "assets/img/xxx.png" を探しに行き、1回目のロードが必ず失敗していた。
                        // 敵・ギミック・アイテム側はプロジェクトルート起点で読んでいるので、
                        // タイルもルート起点を先に試し、見つからない場合だけ assets/ 配下を探すよう順序を入れ替える。
                        handle = LoadGraph(spritePath.c_str());
                        if (handle == -1) {
                            // ルート直下に無い場合のみ、assets/ 配下に置かれている可能性を試す
                            handle = LoadGraph(("assets/" + spritePath).c_str());
                        }
                    }

                    if (tid >= 0 && tid < (int)tileDefs.size()) {
                        tileDefs[tid].deadly = deadly;
                        tileDefs[tid].isCollidable = collidable;
                        if (handle != -1) tileDefs[tid].handle = handle;
                        tileDefs[tid].srcX = srcX; tileDefs[tid].srcY = srcY;
                        tileDefs[tid].srcW = srcW; tileDefs[tid].srcH = srcH;
                    } else if (tid >= (int)tileDefs.size()) {
                        // 新規タイルを追加
                        TileDefinition newTile;
                        newTile.type = (TileType)tid;
                        newTile.handle = (handle != -1) ? handle : jimenHandle; // デフォルト画像
                        newTile.isCollidable = collidable;
                        newTile.deadly = deadly;
                        newTile.name = "Custom";
                        newTile.srcX = srcX; newTile.srcY = srcY;
                        newTile.srcW = srcW; newTile.srcH = srcH;
                        tileDefs.push_back(newTile);
                    }
                }
            }
        }

        // Feature: タイル表示範囲調整機能 — srcW/srcHが未設定(0)のタイルは、画像全体を使うように解決する
        for (auto& td : tileDefs) {
            if (td.handle >= 0 && (td.srcW <= 0 || td.srcH <= 0)) {
                int iw = 0, ih = 0;
                GetGraphSize(td.handle, &iw, &ih);
                td.srcX = 0; td.srcY = 0; td.srcW = iw; td.srcH = ih;
            }
        }
    }

    // 新アセット移行対応 — プレイヤーおよびC++直書きステージの敵の「論理サイズ」。
    // 以前は GetGraphSize(playerHandle) で画像の実寸をそのまま使っていたが、
    // 新素材は全て 640x640 なので、そのままだと表示だけでなく当たり判定まで 640px になって破綻する。
    // 描画倍率は ComputeFitScale が画像実寸から自動算出するため、ここはタイル1マス(32px)基準の
    // ゲーム的な大きさを直接指定してよい。
    const int PLAYER_LOGICAL_SIZE = 32;
    int pw = PLAYER_LOGICAL_SIZE, ph = PLAYER_LOGICAL_SIZE;

    // オブジェクトの仮初期化（ResetStageで正しく設定されます）
    Player player = { 100.0f, 300.0f, 0.0f, 0.0f, playerHandle, 0, false, pw, ph, 1.0f, 0.0f, 1.0f, false, false, {} };
    std::vector<Enemy> enemies;     // 現在プレイ中のステージの敵一覧（実行時状態）
    std::vector<Platform> platforms;// 現在プレイ中のステージの足場一覧
    std::vector<Gimmick> gimmicks;  // 現在プレイ中のステージのギミック一覧
    std::vector<Item> items;        // 現在プレイ中のステージのアイテム一覧

    std::vector<StageData> stages;  // ゲームに収録されている全ステージのデータ一覧（下でハードコードして構築する）

    // =====================================================================
    // --- ステージ 1: 草原の試練 ---
    // 流れ: 平地スタート → 落下リフト谷 → 中間足場地帯（穴あり）→ 回転橋終盤
    // =====================================================================
    {
        StageData stage1;
        stage1.id = 0;
        strcpy_s(stage1.name, "Stage 1: Grassland Trial");
        stage1.playerStartX = 48.0f;
        stage1.playerStartY = 320.0f;

        stage1.map.assign(MAP_HEIGHT_TILES, std::vector<int>(MAP_WIDTH_TILES, TILE_NONE));

        // -----セクション0: スタート台地 (タイルx:0-9, ピクセル:0-319) -----
        // y=12が地面上面（ピクセルy=384）
        for (int x = 0; x <= 9; x++) {
            stage1.map[12][x] = TILE_JIMEN;
            stage1.map[13][x] = TILE_TUTI;
            stage1.map[14][x] = TILE_TUTI;
        }

        // -----セクション1: 低地平原 (タイルx:10-24, ピクセル:320-799) -----
        // y=13が地面上面（ピクセルy=416）。スタート台地から一段下がる
        for (int x = 10; x <= 24; x++) {
            stage1.map[13][x] = TILE_JIMEN;
            stage1.map[14][x] = TILE_TUTI;
        }
        // 段差の壁タイル
        stage1.map[13][10] = TILE_TUTI;

        // -----セクション2: 落下リフトの谷 (タイルx:25-38, ピクセル:800-1247) -----
        // 谷の左端の崖壁 x=25
        stage1.map[14][25] = TILE_TUTI;
        // 谷の右端の台 x=37-38 (y=13)
        for (int x = 37; x <= 38; x++) {
            stage1.map[13][x] = TILE_JIMEN;
            stage1.map[14][x] = TILE_TUTI;
        }

        // -----セクション3: 中間足場地帯 (タイルx:39-54, ピクセル:1248-1759) -----
        // 少し高め y=12
        for (int x = 39; x <= 54; x++) {
            stage1.map[12][x] = TILE_JIMEN;
            stage1.map[13][x] = TILE_TUTI;
            stage1.map[14][x] = TILE_TUTI;
        }
        // 穴 (タイルx=47-48, ピクセル:1504-1567)
        stage1.map[12][47] = TILE_NONE;
        stage1.map[12][48] = TILE_NONE;
        // 穴を越えるための上空足場 (タイルx=43-46, y=9行目＝ピクセルy=288)
        for (int x = 43; x <= 46; x++) {
            stage1.map[9][x] = TILE_JIMEN;
            stage1.map[10][x] = TILE_TUTI;
        }

        // -----セクション4: 回転橋終盤 (タイルx:55-79, ピクセル:1760-2559) -----
        // 左の踏み台 x=55-58
        for (int x = 55; x <= 58; x++) {
            stage1.map[12][x] = TILE_JIMEN;
            stage1.map[13][x] = TILE_TUTI;
            stage1.map[14][x] = TILE_TUTI;
        }
        // 中間足場 x=60-62 (y=10行目)
        for (int x = 60; x <= 62; x++) {
            stage1.map[10][x] = TILE_JIMEN;
            stage1.map[11][x] = TILE_TUTI;
        }
        // ゴール高台 x=65-79 (y=11行目)
        for (int x = 65; x <= 79; x++) {
            stage1.map[11][x] = TILE_JIMEN;
            stage1.map[12][x] = TILE_TUTI;
            stage1.map[13][x] = TILE_TUTI;
            stage1.map[14][x] = TILE_TUTI;
        }

        // -----プラットフォーム（薄い足場）-----
        stage1.platforms = {
            // セクション2 谷の中間踏み台（左）
            { 1216.0f, 352.0f, 1280.0f, 362.0f },
            // セクション3 穴の上空補助足場
            { 1504.0f, 288.0f, 1600.0f, 298.0f },
        };

        // -----ギミック配置-----
        // セクション2: 谷底トゲ（落ちると即死）
        stage1.gimmicks.push_back({ GIMMICK_SPIKES,         896.0f, 432.0f, 320.0f, 20.0f, 320.0f, 20.0f, 0.0f, 0.0f, 320.0f, 20.0f, true, 0.0f, 0.0f, 0.0f,    0.0f, false, false, {} });
        // セクション2: 落下リフト×2（タイミングよく乗り継ぐ）
        stage1.gimmicks.push_back({ GIMMICK_FALLING_LIFT,   832.0f, 280.0f, 80.0f,  16.0f, 80.0f, 16.0f, 0.0f, 0.0f, 80.0f, 16.0f, true, 0.0f, 0.0f, 0.0f,    0.0f, false, false, {} });
        stage1.gimmicks.push_back({ GIMMICK_FALLING_LIFT,  1024.0f, 220.0f, 80.0f,  16.0f, 80.0f, 16.0f, 0.0f, 0.0f, 80.0f, 16.0f, true, 0.0f, 0.0f, 0.0f,    0.0f, false, false, {} });
        // セクション3: 穴の下トゲ
        stage1.gimmicks.push_back({ GIMMICK_SPIKES,        1472.0f, 432.0f, 96.0f,  20.0f, 96.0f, 20.0f, 0.0f, 0.0f, 96.0f, 20.0f, true, 0.0f, 0.0f, 0.0f,    0.0f, false, false, {} });
        // セクション4: 自動回転橋（谷を渡る主要ルート）
        stage1.gimmicks.push_back({ GIMMICK_ROTATING_BRIDGE, 1824.0f, 350.0f, 120.0f, 16.0f, 120.0f, 16.0f, 0.0f, 0.0f, 120.0f, 16.0f, true, 0.0f, 0.0f, 0.0f, 0.0f, false, false, {} });
        // セクション4: 手動橋（タイミングを自分で制御）
        stage1.gimmicks.push_back({ GIMMICK_MANUAL_BRIDGE, 1984.0f, 290.0f, 120.0f, 16.0f, 120.0f, 16.0f, 0.0f, 0.0f, 120.0f, 16.0f, true, 0.0f, 0.0f, 1.5708f, 0.0f, false, false, {} });
        // Feature: ポータルの作り直し（友人フィードバック対応）— 2地点間テレポート。同じparam値("portal_a")の
        // ポータル同士がペアになり、片方に触れるともう片方の位置へワープする。
        stage1.gimmicks.push_back({ GIMMICK_CUT_PORTAL,  640.0f, 380.0f, 32.0f, 32.0f, 32.0f, 32.0f, 0.0f, 0.0f, 32.0f, 32.0f, true, 0.0f, 0.0f, 0.0f, 0.0f, false, false, {} });
        stage1.gimmicks.back().param = "portal_a";
        stage1.gimmicks.push_back({ GIMMICK_CUT_PORTAL, 2100.0f, 300.0f, 32.0f, 32.0f, 32.0f, 32.0f, 0.0f, 0.0f, 32.0f, 32.0f, true, 0.0f, 0.0f, 0.0f, 0.0f, false, false, {} });
        stage1.gimmicks.back().param = "portal_a";

        // -----敵配置-----
        // セクション1 低地をパトロール
        stage1.enemies.push_back({ ENEMY_PATROL_SHOOTER, 400.0f, 390.0f, 0.0f, 0.0f, playerHandle, 1, pw, ph, pw, ph, 0, 0, pw, ph, 0.9f, 0.0f, 1.0f, true, false, 3, 0.0f, 0, 300.0f, 500.0f, false, {} });
        // セクション3 中間地帯でジャンプ
        stage1.enemies.push_back({ ENEMY_JUMPER,    1380.0f, 350.0f, 0.0f, 0.0f, playerHandle, 1, pw, ph, pw, ph, 0, 0, pw, ph, 1.0f, 0.0f, 1.0f, true, false, 3, 0.0f, 0, 0.0f, 0.0f, false, {} });
        // セクション4 ゴール手前の砲台
        stage1.enemies.push_back({ ENEMY_STATIONARY, 2200.0f, 320.0f, 0.0f, 0.0f, playerHandle, 1, pw, ph, pw, ph, 0, 0, pw, ph, 1.0f, 0.0f, 1.0f, true, false, 3, 0.0f, 0, 0.0f, 0.0f, false, {} });

        // -----コイン配置-----
        // セクション1 平地コイン列（歩けば取れる）
        stage1.items.push_back({ ITEM_COIN,  320.0f, 380.0f, 20.0f, 20.0f, 20.0f, 20.0f, 0.0f, 0.0f, 20.0f, 20.0f, true, false, false, {} });
        stage1.items.push_back({ ITEM_COIN,  400.0f, 380.0f, 20.0f, 20.0f, 20.0f, 20.0f, 0.0f, 0.0f, 20.0f, 20.0f, true, false, false, {} });
        // セクション2 落下リフト上（リスクあり）
        stage1.items.push_back({ ITEM_COIN,  840.0f, 248.0f, 20.0f, 20.0f, 20.0f, 20.0f, 0.0f, 0.0f, 20.0f, 20.0f, true, false, false, {} });
        stage1.items.push_back({ ITEM_COIN, 1032.0f, 188.0f, 20.0f, 20.0f, 20.0f, 20.0f, 0.0f, 0.0f, 20.0f, 20.0f, true, false, false, {} });
        // セクション3 上空足場上
        stage1.items.push_back({ ITEM_COIN, 1540.0f, 256.0f, 20.0f, 20.0f, 20.0f, 20.0f, 0.0f, 0.0f, 20.0f, 20.0f, true, false, false, {} });
        // セクション4 ゴール直前
        stage1.items.push_back({ ITEM_COIN, 2350.0f, 320.0f, 20.0f, 20.0f, 20.0f, 20.0f, 0.0f, 0.0f, 20.0f, 20.0f, true, false, false, {} });
        stage1.items.push_back({ ITEM_COIN, 2430.0f, 320.0f, 20.0f, 20.0f, 20.0f, 20.0f, 0.0f, 0.0f, 20.0f, 20.0f, true, false, false, {} });

        stages.push_back(stage1);
    }
    
    // =====================================================================
    // --- ステージ 2: 要塞突撃 ---
    // 流れ: 高台スタート → 下り坂 + 崖 → 反射鏡の部屋 → 登り段差 → 重量スイッチ広間
    // =====================================================================
    {
        StageData stage2;
        stage2.id = 1;
        strcpy_s(stage2.name, "Stage 2: Fortress Assault");
        stage2.playerStartX = 48.0f;
        stage2.playerStartY = 200.0f;

        stage2.map.assign(MAP_HEIGHT_TILES, std::vector<int>(MAP_WIDTH_TILES, TILE_NONE));

        // -----セクション0: スタート高台 (タイルx:0-6) -----
        // y=8が地面上面（ピクセルy=256）
        for (int x = 0; x <= 6; x++) {
            stage2.map[8][x] = TILE_JIMEN;
            for (int y = 9; y < MAP_HEIGHT_TILES; y++) stage2.map[y][x] = TILE_TUTI;
        }

        // -----セクション1: 下り坂 + 崖 (タイルx:7-18) -----
        // x=7-10: y=10に下がる
        for (int x = 7; x <= 10; x++) {
            stage2.map[10][x] = TILE_JIMEN;
            for (int y = 11; y < MAP_HEIGHT_TILES; y++) stage2.map[y][x] = TILE_TUTI;
        }
        // x=11-14: y=12まで下がる
        for (int x = 11; x <= 14; x++) {
            stage2.map[12][x] = TILE_JIMEN;
            for (int y = 13; y < MAP_HEIGHT_TILES; y++) stage2.map[y][x] = TILE_TUTI;
        }
        // x=15-18: 谷底 y=13
        for (int x = 15; x <= 18; x++) {
            stage2.map[13][x] = TILE_JIMEN;
            stage2.map[14][x] = TILE_TUTI;
        }

        // -----セクション2: 反射鏡の部屋 (タイルx:19-35) -----
        // 部屋の床 y=13
        for (int x = 19; x <= 35; x++) {
            stage2.map[13][x] = TILE_JIMEN;
            stage2.map[14][x] = TILE_TUTI;
        }
        // 部屋の天井 y=8
        for (int x = 19; x <= 34; x++) {
            stage2.map[8][x] = TILE_TUTI;
        }
        // 部屋の左壁
        for (int y = 9; y <= 13; y++) stage2.map[y][19] = TILE_TUTI;
        // 部屋の右壁（破壊可能ブロックで塞がれた出口）
        for (int y = 9; y <= 12; y++) stage2.map[y][35] = TILE_TUTI;

        // -----セクション3: 登り段差 (タイルx:36-50) -----
        // 部屋出口後の通路 x=36-40 (y=13)
        for (int x = 36; x <= 40; x++) {
            stage2.map[13][x] = TILE_JIMEN;
            stage2.map[14][x] = TILE_TUTI;
        }
        // 一段上がる x=41-44 (y=11)
        for (int x = 41; x <= 44; x++) {
            stage2.map[11][x] = TILE_JIMEN;
            for (int y = 12; y < MAP_HEIGHT_TILES; y++) stage2.map[y][x] = TILE_TUTI;
        }
        // さらに上がる x=45-50 (y=9)
        for (int x = 45; x <= 50; x++) {
            stage2.map[9][x] = TILE_JIMEN;
            for (int y = 10; y < MAP_HEIGHT_TILES; y++) stage2.map[y][x] = TILE_TUTI;
        }

        // -----セクション4: 重量スイッチ広間 (タイルx:51-65) -----
        for (int x = 51; x <= 65; x++) {
            stage2.map[9][x] = TILE_JIMEN;
            for (int y = 10; y < MAP_HEIGHT_TILES; y++) stage2.map[y][x] = TILE_TUTI;
        }
        // 広間の天井
        for (int x = 52; x <= 65; x++) stage2.map[4][x] = TILE_TUTI;
        // 広間の左壁
        for (int y = 4; y <= 9; y++) stage2.map[y][51] = TILE_TUTI;

        // -----セクション5: ゴール前廊下 (タイルx:66-79) -----
        for (int x = 66; x <= 79; x++) {
            stage2.map[9][x] = TILE_JIMEN;
            for (int y = 10; y < MAP_HEIGHT_TILES; y++) stage2.map[y][x] = TILE_TUTI;
        }

        // -----プラットフォーム-----
        stage2.platforms = {
            // セクション2 部屋内上空足場（高い位置へ）
            { 672.0f, 352.0f, 800.0f, 362.0f },
            // セクション3 段差補助足場
            { 1344.0f, 288.0f, 1440.0f, 298.0f },
        };

        // -----ギミック配置-----
        // セクション1 崖下のトゲ（転落死）
        stage2.gimmicks.push_back({ GIMMICK_SPIKES,         480.0f, 432.0f,  96.0f, 20.0f, 96.0f, 20.0f, 0.0f, 0.0f, 96.0f, 20.0f, true, 0.0f, 0.0f, 0.0f,    0.0f, false, false, {} });
        // セクション2 反射鏡（弾を反射させてブロックを壊す）
        stage2.gimmicks.push_back({ GIMMICK_REFLECT_MIRROR,  800.0f, 380.0f,  80.0f, 16.0f, 80.0f, 16.0f, 0.0f, 0.0f, 80.0f, 16.0f, true, 0.0f, 0.0f, 0.7854f, 0.0f, false, false, {} });
        // セクション2 破壊可能ブロック（出口を塞ぐ壁）
        stage2.gimmicks.push_back({ GIMMICK_BREAKABLE_BLOCK, 1120.0f, 288.0f, 32.0f, 144.0f, 32.0f, 144.0f, 0.0f, 0.0f, 32.0f, 144.0f, true, 0.0f, 0.0f, 0.0f,  0.0f, false, false, {} });
        // セクション4 拡大可能ボックス（スイッチを踏むための踏み台）
        stage2.gimmicks.push_back({ GIMMICK_SCALABLE_BOX,   1620.0f, 228.0f, 50.0f, 50.0f, 50.0f, 50.0f, 0.0f, 0.0f, 50.0f, 50.0f, true, 0.0f, 0.0f, 0.0f,   0.0f, false, false, {} });
        // セクション4 重量スイッチ（ゲート解錠トリガー）
        stage2.gimmicks.push_back({ GIMMICK_WEIGHT_SWITCH,  1760.0f, 278.0f, 120.0f, 10.0f, 120.0f, 10.0f, 0.0f, 0.0f, 120.0f, 10.0f, true, 0.0f, 0.0f, 0.0f,  0.0f, false, false, {} });
        // セクション4 ゲート扉（スイッチで開く）
        stage2.gimmicks.push_back({ GIMMICK_GATE_DOOR,      2080.0f, 128.0f, 32.0f, 160.0f, 32.0f, 160.0f, 0.0f, 0.0f, 32.0f, 160.0f, true, 0.0f, 0.0f, 0.0f,  0.0f, false, false, {} });
        // セクション5 ゴール前の落下リフト
        stage2.gimmicks.push_back({ GIMMICK_FALLING_LIFT,   2240.0f, 150.0f, 80.0f, 16.0f, 80.0f, 16.0f, 0.0f, 0.0f, 80.0f, 16.0f, true, 0.0f, 0.0f, 0.0f,   0.0f, false, false, {} });

        // -----敵配置-----
        // セクション1 崖をパトロール
        enemies.push_back({ ENEMY_PATROL_SHOOTER, 360.0f, 320.0f, 0.0f, 0.0f, playerHandle, 1, pw, ph, pw, ph, 0, 0, pw, ph, 0.9f, 0.0f, 1.0f, true, false, 3, 0.0f, 0, 200.0f, 500.0f, false, {} });
        // セクション2 部屋内でジャンプ
        enemies.push_back({ ENEMY_JUMPER,      960.0f, 380.0f, 0.0f, 0.0f, playerHandle, 1, pw, ph, pw, ph, 0, 0, pw, ph, 1.0f, 0.0f, 1.0f, true, false, 3, 0.0f, 0, 0.0f, 0.0f, false, {} });
        // セクション4 広間をパトロール
        enemies.push_back({ ENEMY_PATROL_SHOOTER, 1700.0f, 250.0f, 0.0f, 0.0f, playerHandle, 1, pw, ph, pw, ph, 0, 0, pw, ph, 0.9f, 0.0f, 1.0f, true, false, 3, 0.0f, 0, 1500.0f, 1900.0f, false, {} });
        // セクション5 ゴール前砲台（最難関）
        enemies.push_back({ ENEMY_STATIONARY, 2400.0f, 250.0f, 0.0f, 0.0f, playerHandle, 1, pw, ph, pw, ph, 0, 0, pw, ph, 1.2f, 0.0f, 1.0f, true, false, 3, 0.0f, 0, 0.0f, 0.0f, false, {} });

        // -----コイン配置-----
        // セクション1 下り坂
        stage2.items.push_back({ ITEM_COIN,  280.0f, 290.0f, 20.0f, 20.0f, 20.0f, 20.0f, 0.0f, 0.0f, 20.0f, 20.0f, true, false, false, {} });
        stage2.items.push_back({ ITEM_COIN,  380.0f, 350.0f, 20.0f, 20.0f, 20.0f, 20.0f, 0.0f, 0.0f, 20.0f, 20.0f, true, false, false, {} });
        // セクション2 部屋内（危険）
        stage2.items.push_back({ ITEM_COIN,  820.0f, 320.0f, 20.0f, 20.0f, 20.0f, 20.0f, 0.0f, 0.0f, 20.0f, 20.0f, true, false, false, {} });
        // セクション3 段差登り中
        stage2.items.push_back({ ITEM_COIN, 1380.0f, 260.0f, 20.0f, 20.0f, 20.0f, 20.0f, 0.0f, 0.0f, 20.0f, 20.0f, true, false, false, {} });
        // セクション4 広間
        stage2.items.push_back({ ITEM_COIN, 1680.0f, 200.0f, 20.0f, 20.0f, 20.0f, 20.0f, 0.0f, 0.0f, 20.0f, 20.0f, true, false, false, {} });
        stage2.items.push_back({ ITEM_COIN, 1800.0f, 200.0f, 20.0f, 20.0f, 20.0f, 20.0f, 0.0f, 0.0f, 20.0f, 20.0f, true, false, false, {} });
        // セクション5 ゴール直前
            stage2.items.push_back({ ITEM_COIN, 2460.0f, 240.0f, 20.0f, 20.0f, 20.0f, 20.0f, 0.0f, 0.0f, 20.0f, 20.0f, true, false, false, {} });

        stages.push_back(stage2);
    }

    Logger::Info("System", "WinMain", "[Init] StageLoader Success");
    // 冒頭で登録したものと同様の未処理例外ハンドラを再度登録している（内容はほぼ同じで、
    // こちらはabort()ではなくexit(1)で終了する点のみ異なる）。後から追加された安全策と思われる。
    std::set_terminate([]() {
        Logger::Error("System", "Terminate", "Unhandled exception occurred");
        try {
            if (std::current_exception()) std::rethrow_exception(std::current_exception());
        } catch (const std::exception& e) {
            Logger::Error("System", "Terminate", std::string("what(): ") + e.what());
        } catch (...) {
            Logger::Error("System", "Terminate", "Unknown exception type (not derived from std::exception)");
        }
        exit(1);
    });


    // Feature: チェックポイント — 現在の復帰地点（-1,-1 = 未設定＝ステージ開始位置を使う）。
    // ResetStage()では意図的にクリアしない（ゲームオーバー/落下死からのリトライで保持し続けるため）。
    // 実際にステージが切り替わる箇所（SwitchToStageラムダ）でのみクリアする。
    float checkpointX = -1.0f;
    float checkpointY = -1.0f;

    float cameraX = 0.0f; // カメラのワールドX座標（プレイヤーを追従して毎フレーム更新される）
    float cameraY = 0.0f; // Feature: 縦スクロール対応 — cameraXと同様、毎フレームプレイヤーへ追従させる
    float STAGE_WIDTH = 2560.0f; // 現在のステージの実際のマップ幅に応じて毎フレーム更新される。下記メインループ参照。
    float STAGE_HEIGHT = 480.0f; // 現在のステージの実際のマップ高さに応じて毎フレーム更新される。下記メインループ参照。

    // プレイヤーが能動的に使う「編集ツール」の状態
    int playerColorFilter = 0; // 0=なし, 1=赤, 2=緑, 3=青（Tキーで巡回）

    // ===== 画面全体エフェクト（明るさ・色調・ズーム）=====
    // 「動画編集」コンセプトのコアシステム。敵やギミックが毎フレーム Screen_SetXxx() で
    // 目標値を上書きすることでパルス的な演出になり、何も上書きしなければ自動的にニュートラルへ戻る。
    float fxTargetBright = 1.0f, fxCurBright = 1.0f;
    float fxTargetTintR = 1.0f, fxTargetTintG = 1.0f, fxTargetTintB = 1.0f;
    float fxCurTintR = 1.0f, fxCurTintG = 1.0f, fxCurTintB = 1.0f;
    float fxTargetZoom = 1.0f, fxCurZoom = 1.0f;

    auto Screen_SetBrightness = [&](float b) { fxTargetBright = b; };
    auto Screen_SetTint = [&](float r, float g, float b) { fxTargetTintR = r; fxTargetTintG = g; fxTargetTintB = b; };
    auto Screen_SetZoom = [&](float z) { fxTargetZoom = z; };

    // ===== Feature: 編集コストゲージ（ステージ単位設定・実行時状態）=====
    EditToolFlags currentEditTools;   // 現在のステージの許可設定（ResetStageで反映）
    EditCostSettings currentEditCost; // 現在のステージのコスト経済（ResetStageで反映）
    auto IsEditToolEnabled = [&](bool stageFlag, bool unlockedFlag) { return stageFlag || unlockedFlag; };

    auto Screen_BeginFrame = [&]() {
        fxTargetBright = 1.0f;
        fxTargetTintR = fxTargetTintG = fxTargetTintB = 1.0f;
        fxTargetZoom = 1.0f;
    };
    auto Screen_UpdateFrame = [&](float dts) {
        float lerp = std::min<float>(1.0f, 0.08f * dts);
        fxCurBright += (fxTargetBright - fxCurBright) * lerp;
        fxCurTintR  += (fxTargetTintR  - fxCurTintR)  * lerp;
        fxCurTintG  += (fxTargetTintG  - fxCurTintG)  * lerp;
        fxCurTintB  += (fxTargetTintB  - fxCurTintB)  * lerp;
        fxCurZoom   += (fxTargetZoom   - fxCurZoom)   * lerp;
    };
    // gameScreen（オフスクリーン合成済みバッファ）を明るさ・色調・ズームを適用して転送する共通ヘルパー。
    // モニタープレビュー・フルスクリーン再生の両方の最終ブリット地点から呼ばれる、この機能の唯一の統合ポイント。
    auto Screen_DrawComposited = [&](int destX, int destY, int destW, int destH, int screenHandle) {
        int br = (int)std::max<float>(0.0f, std::min<float>(255.0f, fxCurTintR * fxCurBright * 255.0f));
        int bg = (int)std::max<float>(0.0f, std::min<float>(255.0f, fxCurTintG * fxCurBright * 255.0f));
        int bb = (int)std::max<float>(0.0f, std::min<float>(255.0f, fxCurTintB * fxCurBright * 255.0f));
        SetDrawBright(br, bg, bb);

        float zoom = fxCurZoom < 0.1f ? 0.1f : fxCurZoom;
        int srcW = (int)(SCREEN_WIDTH / zoom);
        int srcH = (int)(SCREEN_HEIGHT / zoom);
        if (srcW > SCREEN_WIDTH) srcW = SCREEN_WIDTH;
        if (srcH > SCREEN_HEIGHT) srcH = SCREEN_HEIGHT;
        int srcX = (SCREEN_WIDTH - srcW) / 2;
        int srcY = (SCREEN_HEIGHT - srcH) / 2;
        DrawRectExtendGraph(destX, destY, destX + destW, destY + destH, srcX, srcY, srcX + srcW, srcY + srcH, screenHandle, TRUE);

        SetDrawBright(255, 255, 255);
    };
    std::vector<Bullet> bullets(MAX_BULLETS);
    for (int i = 0; i < MAX_BULLETS; i++) { 
        bullets[i].isActive = false; 
        bullets[i].handle = bulletHandle; 
        bullets[i].isRewinding = false;
    }

    float groundY = 400.0f;
    bool isPaused = false, isEditMode = true, isFastForward = false, isStepFrame = false, isDebugDrawMode = false;
    float editCost = 100.0f; // 編集コストゲージ現在値（ResetStageで currentEditCost.maxCost に上書きされる）
    bool lastF3 = false;
    bool isDragging = false, isScaling = false, isScalingHeight = false, isRotating = false;
    bool isInspScale = false, isInspAngle = false, isInspSpeed = false;

    // Feature 5: イベントアクション実行用の実行時ステート。ShowMessage / MoveCamera / ItemCollected条件で使用。
    bool isShowingMessage = false;
    std::string currentMessageText = "";
    std::string currentMessageSpeaker = "";
    float cameraOverrideX = -1.0f;
    float cameraOverrideTimer = 0.0f;
    std::vector<std::string> collectedItemIds;

    float dragOffsetX = 0, dragOffsetY = 0, baseScale = 1.0f, baseAngle = 0.0f, baseSpeed = 1.0f;
    int lastMouseX = 0, lastMouseY = 0;
    float globalTimeScale = 1.0f;
    // Feature: カット機能の復活 — カット範囲を作る途中の「1点目」を覚えておく変数。
    // Ctrl+クリック1回目でここに比率(0.0〜1.0)が入り、2回目のクリックで区間が確定してカットが生成される。
    // -1.0f は「まだ1点目が打たれていない」を意味する番兵。
    float tempCutStart = -1.0f;

    // 複数選択用コンテナとドラッグ用変数
    std::vector<Player*> selectedPlayers;
    std::vector<Enemy*> selectedEnemies;
    std::vector<Gimmick*> selectedGimmicks;
    bool isAreaSelecting = false;
    int areaSelectStartX = 0, areaSelectStartY = 0;
    int areaSelectEndX = 0, areaSelectEndY = 0;
    
    ContextMenu menu = { false, 0, 0, 160, 160 }; // 6つのオプション用のコンテキストメニューサイズ (高さ 160)
    SetMouseDispFlag(TRUE);

    SelectedType selectedType = SELECT_NONE;
    float* targetScale = nullptr;
    float* targetAngle = nullptr;
    float* targetSpeedScale = nullptr;
    bool* targetPaused = nullptr;
    bool* targetRewind = nullptr;
    int* targetDirection = nullptr;
    EnemyType* targetEnemyType = nullptr;
    Gimmick* targetGimmick = nullptr;
    Enemy* targetEnemy = nullptr;

    int monitorX = (WINDOW_WIDTH - SCREEN_WIDTH) / 2;
    int monitorY = (WINDOW_HEIGHT - SCREEN_HEIGHT) / 2 - 40;

    int currentStageIdx = 0;

    GameScene currentScene = PLAY;
    // 前フレームのシーン。「PLAY からクリアへ移った瞬間」を検出してセーブするために持つ。
    // currentScene = RESULT_VICTORY の代入元は6箇所以上に散っているので、
    // 個々の代入元へセーブ処理を書き足すのではなく、遷移そのものを1箇所で拾う。
    // こうしておけば、後からクリア判定を増やしても自動的にセーブされる。
    GameScene prevSceneForSave = PLAY;

    // ===== JSON ステージ読み込み =====
    // assets/stages/<ファイル名> を読み込みStageDataを構築するラムダ。
    // 起動時の初期ステージ読み込みと、GoToStageアクションによる実行時のステージ切り替えの両方から使う。
    auto LoadStageJson = [&](const std::string& fileNameArg, StageData& jsonStage) -> bool {
        std::string stageJsonPath = "assets/stages/" + fileNameArg;
        std::ifstream stageFile(stageJsonPath);
        if (stageFile.is_open()) {
            try {
                json sj = json::parse(stageFile, nullptr, false);
                if (sj.is_discarded()) {
                    Logger::Error("DrawPixel", "WinMain", "Failed to parse stage json", stageJsonPath);
                } else {

                    jsonStage.id = (int)stages.size();
                    jsonStage.sourceFile = fileNameArg;
                    strcpy_s(jsonStage.name, "JSON Stage");

                    // プレイヤー開始位置
                    if (sj.contains("player_start")) {
                        jsonStage.playerStartX = sj["player_start"]["x"];
                        jsonStage.playerStartY = sj["player_start"]["y"];
                    } else {
                        jsonStage.playerStartX = 48.0f;
                        jsonStage.playerStartY = 320.0f;
                    }

                    // ゴール位置
                    if (sj.contains("goal")) {
                        jsonStage.goalX = sj["goal"].value("x", -1.0f);
                        jsonStage.goalY = sj["goal"].value("y", -1.0f);
                    }

                    // プレイヤー能力
                    if (sj.contains("player_capabilities")) {
                        auto caps = sj["player_capabilities"];
                        editorPlayerCaps.canDoubleJump = caps.value("canDoubleJump", false);
                        editorPlayerCaps.canDash = caps.value("canDash", false);
                        editorPlayerCaps.canShootFireball = caps.value("canShootFireball", false);
                        editorPlayerCaps.canFly = caps.value("canFly", false);
                        editorPlayerCaps.baseJumpPower = caps.value("baseJumpPower", -12);
                        editorPlayerCaps.baseSpeed = caps.value("baseSpeed", 4.0f);
                    }

                    // 編集ツール許可設定（ステージ単位、キー未指定時はデフォルト全部true）
                    {
                        auto etf = sj.value("allowed_edit_tools", json::object());
                        // 【重要】Lab_Editorが書き出すキー名はC#のプロパティ名そのまま（"rewindEnabled" 等）だが、
                        // ここは元々短縮形（"rewind" 等）しか読んでいなかったため、キーが一生一致せず
                        // 常に既定値trueへフォールバックしていた＝「編集ツール設定」がゲームに全く効いていなかった。
                        // 両方の綴りを受け付けるようにして、エディタ側の設定が実際に反映されるようにする。
                        // （長い方＝エディタが書く形式を優先し、無ければ短縮形、それも無ければ従来どおり許可(true)）
                        auto readTool = [&etf](const char* longKey, const char* shortKey) {
                            if (etf.contains(longKey)) return etf.value(longKey, true);
                            return etf.value(shortKey, true);
                        };
                        jsonStage.editToolFlags.rewindEnabled       = readTool("rewindEnabled",       "rewind");
                        jsonStage.editToolFlags.pauseEnabled        = readTool("pauseEnabled",        "pause");
                        jsonStage.editToolFlags.fastForwardEnabled  = readTool("fastForwardEnabled",  "fastForward");
                        jsonStage.editToolFlags.screenEffectEnabled = readTool("screenEffectEnabled", "screenEffect");
                        jsonStage.editToolFlags.objectEditEnabled   = readTool("objectEditEnabled",   "objectEdit");
                        // Feature: カット機能の復活 — キー未指定の既存ステージは従来どおり許可(true)で読み込む
                        jsonStage.editToolFlags.cutEnabled          = readTool("cutEnabled",          "cut");

                        // どの編集ツールが許可された状態でステージが読み込まれたかをログに残す。
                        // 上記のキー名不一致のように「エディタで設定したのに効いていない」種類の不具合は
                        // ゲーム画面からは気付けないため、ステージ作成時の確認手段として記録しておく。
                        {
                            const auto& ef = jsonStage.editToolFlags;
                            char toolLog[192];
                            sprintf_s(toolLog, sizeof(toolLog),
                                      "editTools rewind=%d pause=%d fastForward=%d screenEffect=%d objectEdit=%d cut=%d",
                                      ef.rewindEnabled ? 1 : 0, ef.pauseEnabled ? 1 : 0, ef.fastForwardEnabled ? 1 : 0,
                                      ef.screenEffectEnabled ? 1 : 0, ef.objectEditEnabled ? 1 : 0, ef.cutEnabled ? 1 : 0);
                            Logger::Info("System", "LoadStageJson", toolLog);
                        }
                    }

                    // 編集コスト経済設定（ステージ単位、キー未指定時はコード既定値）
                    {
                        auto ecs = sj.value("edit_cost_settings", json::object());
                        jsonStage.editCostSettings.maxCost                 = ecs.value("maxCost", 100.0f);
                        jsonStage.editCostSettings.regenPerSec             = ecs.value("regenPerSec", 6.0f);
                        jsonStage.editCostSettings.drainRewindPerSec       = ecs.value("drainRewindPerSec", 18.0f);
                        jsonStage.editCostSettings.drainPausePerSec        = ecs.value("drainPausePerSec", 4.0f);
                        jsonStage.editCostSettings.drainFastForwardPerSec  = ecs.value("drainFastForwardPerSec", 10.0f);
                        jsonStage.editCostSettings.drainScreenEffectPerSec = ecs.value("drainScreenEffectPerSec", 8.0f);
                        jsonStage.editCostSettings.flatColorCycle          = ecs.value("flatColorCycle", 5.0f);
                        jsonStage.editCostSettings.flatMenuToggle          = ecs.value("flatMenuToggle", 8.0f);
                        jsonStage.editCostSettings.flatSpeedChange         = ecs.value("flatSpeedChange", 6.0f);
                        jsonStage.editCostSettings.flatDirectionFlip       = ecs.value("flatDirectionFlip", 4.0f);
                        jsonStage.editCostSettings.flatResetAll            = ecs.value("flatResetAll", 10.0f);
                        // Feature: カット機能の復活 — キー未指定の既存ステージJSONでも既定値20で動くようにしておく
                        jsonStage.editCostSettings.flatCutCreate           = ecs.value("flatCutCreate", 20.0f);
                        // Feature: カットコストの距離変動 — 1タイルあたりの追加消費量。
                        // 0を指定すればそのステージだけ従来どおりの「距離に関係なく定額」に戻せる。
                        jsonStage.editCostSettings.cutCostPerTile          = ecs.value("cutCostPerTile", 1.2f);
                    }

                    // サウンド・フラグ
                    jsonStage.testMode = sj.value("test_mode", false);
                    jsonStage.bgmId = sj.value("bgm_id", "");
                    if (sj.contains("triggers") && sj["triggers"].is_array()) {
                        jsonStage.triggersJson = sj["triggers"];
                    }

                    // タイルマップ。map_w/map_h、または実際のmap配列サイズに応じた可変サイズに対応。指定が無ければ既定の80x15。
                    int jsonMapW = sj.value("map_w", MAP_WIDTH_TILES);
                    int jsonMapH = sj.value("map_h", MAP_HEIGHT_TILES);
                    if (sj.contains("map") && sj["map"].is_array() && !sj["map"].empty()) {
                        int mapArrH = (int)sj["map"].size();
                        if (mapArrH > jsonMapH) jsonMapH = mapArrH;
                        if (sj["map"][0].is_array()) {
                            int mapArrW = (int)sj["map"][0].size();
                            if (mapArrW > jsonMapW) jsonMapW = mapArrW;
                        }
                    }
                    if (jsonMapW < 1) jsonMapW = MAP_WIDTH_TILES;
                    if (jsonMapH < 1) jsonMapH = MAP_HEIGHT_TILES;
                    jsonStage.map.assign(jsonMapH, std::vector<int>(jsonMapW, TILE_NONE));
                    jsonStage.decoMapBack.assign(jsonMapH, std::vector<int>(jsonMapW, TILE_NONE));
                    jsonStage.decoMapFront.assign(jsonMapH, std::vector<int>(jsonMapW, TILE_NONE));

                    auto loadLayer = [](const json& j, const std::string& key, std::vector<std::vector<int>>& target) {
                        if (j.contains(key) && j[key].is_array()) {
                            int rows = (int)target.size();
                            int cols = rows > 0 ? (int)target[0].size() : 0;
                            for (int row = 0; row < (int)j[key].size() && row < rows; row++) {
                                auto& rowArr = j[key][row];
                                for (int col = 0; col < (int)rowArr.size() && col < cols; col++) {
                                    target[row][col] = rowArr[col];
                                }
                            }
                        }
                    };
                    loadLayer(sj, "map", jsonStage.map);
                    loadLayer(sj, "deco_back", jsonStage.decoMapBack);
                    loadLayer(sj, "deco_front", jsonStage.decoMapFront);

                    // 背景
                    if (sj.contains("backgrounds") && sj["backgrounds"].is_array()) {
                        for (auto& bj : sj["backgrounds"]) {
                            BackgroundLayer bl;
                            bl.sprite = bj.value("sprite", "");
                            bl.drawOrder = bj.value("drawOrder", 0);
                            bl.scrollRate = bj.value("scrollRate", 0.3f);
                            bl.loop = bj.value("loop", true);
                            bl.offsetX = bj.value("offsetX", 0.0f);
                            bl.offsetY = bj.value("offsetY", 0.0f);
                            if (!bl.sprite.empty()) {
                                std::string path = "assets/" + bl.sprite; // C++側からのパス
                                bl.handle = LoadGraph(path.c_str());
                                if (bl.handle == -1) bl.handle = LoadGraph(bl.sprite.c_str()); // 失敗したらルートで再試行
                            }
                            jsonStage.backgrounds.push_back(bl);
                        }
                    }

                    // 敵の配置
                    if (sj.contains("enemies") && sj["enemies"].is_array()) {
                        for (auto& ej : sj["enemies"]) {
                            std::string id = ej.value("id", "");
                            float x = ej.value("x", 0.0f);
                            float y = ej.value("y", 0.0f);
                            // 巡回範囲（未指定なら従来通り x±200 にフォールバック）
                            float patrolLeft = ej.value("patrol_left", x - 200.0f);
                            float patrolRight = ej.value("patrol_right", x + 200.0f);
                            // マップ範囲外の巡回境界は到達不能になり、敵がマップ端で往復できず張り付いてしまうため範囲内に収める
                            if (patrolLeft < 0.0f) patrolLeft = 0.0f;
                            float patrolMapRight = (float)jsonMapW * TILE_SIZE;
                            if (patrolRight > patrolMapRight) patrolRight = patrolMapRight;
                            if (patrolLeft > patrolRight) patrolLeft = patrolRight;
                            int t_enum = 0; int pw = 32, ph = 32;
                            int sw = 32, sh = 32; int hx = 0, hy = 0;
                            int handle = playerHandle;
                            float defScale = 1.0f;
                            int defHp = 3;
                            for (auto& d : enemyDefs) {
                                if (d.id == id) {
                                    t_enum = d.type_enum;
                                    pw = d.hitboxWidth; ph = d.hitboxHeight;
                                    sw = d.width; sh = d.height;
                                    hx = d.hitboxOffsetX; hy = d.hitboxOffsetY;
                                    handle = d.graphHandle;
                                    defScale = d.scale;
                                    defHp = d.hp;
                                    break;
                                }
                            }
                            // hitboxOffsetX/Y は元画像のピクセル座標系で指定されるため、hitboxWidth/Height と同様に
                            // defScaleを掛けてからワールド座標へ焼き込む（以前はスケールせず加算しており、
                            // scale!=1.0のアセット（例: dossun, 太陽）で当たり判定がスプライトから大きくズレていた）
                            jsonStage.enemies.push_back({ (EnemyType)t_enum, x + hx * defScale, y + hy * defScale, 0.0f, 0.0f, handle, 1, pw, ph, sw, sh, hx, hy, pw, ph, defScale, 0.0f, 1.0f, true, false, defHp, 0.0f, 0, patrolLeft, patrolRight, false, {}, id, AnimationController() });
                        }
                    }

                    // ギミックの配置
                    if (sj.contains("gimmicks") && sj["gimmicks"].is_array()) {
                        for (auto& gj : sj["gimmicks"]) {
                            std::string id = gj.value("id", "");
                            float x = gj.value("x", 0.0f);
                            float y = gj.value("y", 0.0f);
                            std::string param = gj.value("param", "");

                            int t_enum = 0;
                            int pw = 32, ph = 32, sw = 32, sh = 32, hx = 0, hy = 0;
                            int gHandle = -1;
                            for (auto& d : gimmickDefs) {
                                if (d.id == id) {
                                    t_enum = d.type_enum;
                                    pw = d.hitboxWidth; ph = d.hitboxHeight;
                                    sw = d.hitboxWidth; sh = d.hitboxHeight;
                                    hx = d.hitboxOffsetX; hy = d.hitboxOffsetY;
                                    gHandle = d.graphHandle;
                                    break;
                                }
                            }
                            // 初期角度。手動橋(GIMMICK_MANUAL_BRIDGE=2)は「プレイヤーがR+ドラッグで倒して渡る」ギミックなので、
                            // 指定が無ければ縦向き(=渡れない状態)で始める。従来は常に0(水平)で置かれ、最初から渡れてしまっていた。
                            float initAngle = gj.value("angle", (t_enum == GIMMICK_MANUAL_BRIDGE) ? 1.5708f : 0.0f);
                            Gimmick newGim{ (GimmickType)t_enum, x + hx, y + hy, (float)pw, (float)ph, (float)sw, (float)sh, (float)hx, (float)hy, (float)pw, (float)ph, true, 0.0f, 0.0f, initAngle, 0.0f, false, false, {} };
                            newGim.assetId = id;
                            newGim.param = param;
                            newGim.handle = gHandle;
                            jsonStage.gimmicks.push_back(newGim);
                        }
                    }

                    // アイテムの配置
                    if (sj.contains("items") && sj["items"].is_array()) {
                        for (auto& ij : sj["items"]) {
                            std::string id = ij.value("id", "");
                            float x = ij.value("x", 0.0f);
                            float y = ij.value("y", 0.0f);

                            int t_enum = 0;
                            int pw = 32, ph = 32, sw = 32, sh = 32, hx = 0, hy = 0, iHandle = -1;
                            for (auto& d : itemDefs) {
                                if (d.id == id) {
                                    t_enum = d.type_enum;
                                    pw = d.hitboxWidth; ph = d.hitboxHeight;
                                    sw = d.hitboxWidth; sh = d.hitboxHeight;
                                    hx = d.hitboxOffsetX; hy = d.hitboxOffsetY;
                                    iHandle = d.graphHandle;
                                    break;
                                }
                            }
                            Item newItem{ (ItemType)t_enum, x + hx, y + hy, (float)pw, (float)ph, (float)sw, (float)sh, (float)hx, (float)hy, (float)pw, (float)ph, true, false, false, {} };
                            newItem.assetId = id;
                            newItem.handle = iHandle;
                            jsonStage.items.push_back(newItem);
                        }
                    }

                    // プラットフォーム
                    if (sj.contains("platforms") && sj["platforms"].is_array()) {
                        for (auto& pj : sj["platforms"]) {
                            Platform plat;
                            plat.x1 = pj.value("x1", 0.0f);
                            plat.y1 = pj.value("y1", 0.0f);
                            plat.x2 = pj.value("x2", 0.0f);
                            plat.y2 = pj.value("y2", 0.0f);
                            jsonStage.platforms.push_back(plat);
                        }
                    }

                    return true;
                }
            } catch (...) {
                // JSON読み込み失敗
            }
        }
        return false;
    };

    // 起動時の初期ステージ読み込み（エディタからコマンドライン引数で渡されたファイル名）
    {
        StageData initialStage;
        if (LoadStageJson(currentStageFileName, initialStage)) {
            stages.push_back(initialStage);
            currentStageIdx = (int)stages.size() - 1;
        }
    }
    Logger::Info("System", "WinMain", "[Init] stages.json parsing finished");
    if (stages.empty()) {
        Logger::Info("System", "WinMain", "No stages loaded, creating default stage");
    }

    // 死亡時にステージ全体を初期化し、ゲームの整合性を保証するラムダヘルパー
    auto ResetStage = [&]() {
        Logger::Info("System", "ResetStage", "Begin ResetStage");
        const auto& stage = stages[currentStageIdx];

        // Feature: 編集コストゲージ（ステージ単位設定の反映とゲージリセット）
        currentEditTools = stage.editToolFlags;
        currentEditCost = stage.editCostSettings;
        editCost = currentEditCost.maxCost;
        // ステージ切替でその場アクティブだった操作が、新ステージで禁止されている場合は強制解除する
        if (!currentEditTools.pauseEnabled && !unlockedEditTools.pauseEnabled) isPaused = false;
        if (!currentEditTools.fastForwardEnabled && !unlockedEditTools.fastForwardEnabled) isFastForward = false;
        if (!currentEditTools.screenEffectEnabled && !unlockedEditTools.screenEffectEnabled) playerColorFilter = 0;
        if (!currentEditTools.rewindEnabled && !unlockedEditTools.rewindEnabled) player.isRewinding = false;
        if (!currentEditTools.objectEditEnabled && !unlockedEditTools.objectEditEnabled) { selectedType = SELECT_NONE; menu.isOpen = false; }

        // Feature: チェックポイント — 復帰地点が記録済みならステージ開始位置の代わりにそこから再開する
        if (checkpointX >= 0.0f) {
            player.x = checkpointX;
            player.y = checkpointY;
        } else {
            player.x = stage.playerStartX;
            player.y = stage.playerStartY;
        }
        player.vx = 0.0f;
        player.vy = 0.0f;
        player.isJumping = false;
        player.history.clear();
        cameraX = 0.0f;
        cameraY = 0.0f;

        player.anim.LoadForAsset("player", "assets");
        
        enemies = stage.enemies;
        for (auto& enemy : enemies) {
            // enemyDefsから正しいgraphHandle/HP/サイズを検索して適用。
            // ステージJSON由来の配置ならenemy.assetIdは既に正しいIDが入っているのでそれを優先する
            // （以前はここでtype_enumのみによる検索で毎回上書きしており、同じtype_enumを共有する
            //   複数のEnemyDefが存在する場合に誤ったdefのHP/サイズが適用される不具合があった）。
            // ハードコードされた旧ステージ由来などassetId未設定の場合のみtype_enumの最初の一致にフォールバックする。
            enemy.handle = playerHandle; // デフォルト（見つからない場合）
            const EnemyDef* matched = nullptr;
            if (!enemy.assetId.empty()) {
                for (auto& d : enemyDefs) { if (d.id == enemy.assetId) { matched = &d; break; } }
            }
            if (!matched) {
                enemy.assetId = "enemy_" + std::to_string((int)enemy.type);
                for (auto& d : enemyDefs) { if (d.type_enum == (int)enemy.type) { matched = &d; break; } }
            }
            if (matched) {
                if (matched->graphHandle >= 0) enemy.handle = matched->graphHandle;
                if (!matched->id.empty()) enemy.assetId = matched->id;
                enemy.hp = matched->hp;
                enemy.width = matched->hitboxWidth;
                enemy.height = matched->hitboxHeight;
                // Feature: Composite Multi-Part Objects (Parts-M1)
                enemy.parts = BuildPartInstances(matched->parts, enemy.x, enemy.y);
            } else {
                enemy.hp = 3;
                enemy.parts.clear();
            }
            enemy.anim.LoadForAsset(enemy.assetId, "assets");
            enemy.history.clear();

            // Feature: 編集リアクション — 「プレイヤーが何を編集したか」は配置時の値との差で判定する。
            // ResetStageは起動時・ステージ切替・死亡・リトライの全経路が必ず通る唯一の合流点なので、
            // ここで焼いておけば基準値が未設定のまま動き出す個体は存在しない。
            enemy.editBaseScale     = enemy.scale;
            enemy.editBaseAngle     = enemy.angle;
            enemy.editBaseX         = enemy.x;
            enemy.editBaseY         = enemy.y;
            enemy.editBaseDirection = enemy.direction;
            enemy.editDirtyMask     = EDIT_DIRTY_NONE;
        }

        items = stage.items;
        for (auto& item : items) {
            item.history.clear();
            // Feature: Composite Multi-Part Objects (Parts-M1)
            const ItemDef* idef = FindItemDef(item.assetId);
            item.parts = idef ? BuildPartInstances(idef->parts, item.x, item.y) : std::vector<PartInstance>();
        }

        gimmicks = stage.gimmicks;
        for (auto& gim : gimmicks) {
            gim.history.clear();
            // Feature: Puzzle-like Behavior Scripting (M2)。Parts-M1で全ギミックタイプ共通のdef検索に変更
            // （元はGIMMICK_CUSTOM_SCRIPTの時だけ検索していたが、パーツは親のtype_enumに関係なく使えるため）
            const GimmickDef* gdef = FindGimmickDef(gim.assetId);
            if (gim.type == GIMMICK_CUSTOM_SCRIPT) {
                BehaviorInterpreter::Start(gim.scriptState, gdef ? gdef->script : json::array(), "OnSpawn");
            }
            // Feature: Composite Multi-Part Objects (Parts-M1)
            gim.parts = gdef ? BuildPartInstances(gdef->parts, gim.x, gim.y) : std::vector<PartInstance>();
            // Feature: チェックポイント — テンプレートから作り直した直後はval1(発動済みフラグ)が消えるため、
            // 現在の復帰地点と座標が一致するチェックポイントだけ再度「発動済み」の見た目に戻す
            if (gim.type == GIMMICK_CHECKPOINT && checkpointX >= 0.0f && gim.x == checkpointX && gim.y == checkpointY) {
                gim.val1 = 1.0f;
            }

            // Feature: 編集リアクション — 敵側と同じく配置時の値を基準として焼く。
            // 縦幅の基準は height を正とする（Wドラッグは spriteHeight を先に動かして height へ同期し、
            // Sドラッグは width を先に動かして spriteWidth へ同期する、という非対称があるため）。
            gim.speedScale     = 1.0f;
            gim.direction      = 0;
            gim.editBaseX      = gim.x;
            gim.editBaseY      = gim.y;
            gim.editBaseWidth  = gim.width;
            gim.editBaseHeight = gim.height;
            gim.editBaseAngle  = gim.angle;
            gim.editDirtyMask  = EDIT_DIRTY_NONE;
        }

        platforms = stage.platforms;

        selectedPlayers.clear();
        selectedEnemies.clear();
        selectedGimmicks.clear();
        isAreaSelecting = false;

        selectedType = SELECT_NONE;
        targetScale = nullptr; targetAngle = nullptr; targetSpeedScale = nullptr;
        targetPaused = nullptr; targetRewind = nullptr; targetDirection = nullptr;
        targetEnemyType = nullptr; targetGimmick = nullptr; targetEnemy = nullptr;

        for (int i = 0; i < MAX_BULLETS; i++) {
            bullets[i].isActive = false;
            bullets[i].history.clear();
        }
        player.hp = 3;
        player.invulnTimer = 0.0f;
        currentScene = PLAY;

        // Feature 5: イベントアクション実行時ステートのリセット
        isShowingMessage = false;
        currentMessageText = "";
        currentMessageSpeaker = "";
        cameraOverrideX = -1.0f;
        cameraOverrideTimer = 0.0f;
        collectedItemIds.clear();

        // Feature 3: BGM再生
        if (!stage.bgmId.empty()) {
            SoundManager::Get().PlayBgm(stage.bgmId);
        } else {
            SoundManager::Get().StopBgm();
        }
        
        // Feature 5: イベントトリガー初期化
        EventManager::Get().LoadFromJson(stage.triggersJson);
        EventManager::Get().Reset();
    };

    // GoToStageアクション等から呼ばれる、実行時のステージ切り替えヘルパー。
    // 既に読み込み済みのステージがあれば使い回し、無ければassets/stages/から読み込んでstagesに追加した上で
    // currentStageIdxを切り替え、ResetStageで敵・アイテム・ギミック・BGM・トリガーを新ステージの内容に同期する。
    auto SwitchToStage = [&](const std::string& fileName) -> bool {
        // Feature: チェックポイント — 別ステージへ移る場合は前のステージの復帰地点を持ち越さない
        // （ResetStage自体はゲームオーバー/落下死からの同ステージ内リトライで保持し続けたいのでクリアしない）
        checkpointX = -1.0f;
        checkpointY = -1.0f;
        for (int i = 0; i < (int)stages.size(); i++) {
            if (stages[i].sourceFile == fileName) {
                currentStageIdx = i;
                currentStageFileName = fileName;
                ResetStage();
                return true;
            }
        }
        StageData newStage;
        if (LoadStageJson(fileName, newStage)) {
            stages.push_back(newStage);
            currentStageIdx = (int)stages.size() - 1;
            currentStageFileName = fileName;
            ResetStage();
            return true;
        }
        Logger::Error("DrawPixel", "SwitchToStage", "Failed to load stage for GoToStage", fileName);
        return false;
    };

    // 音声マネージャー初期化
    SoundManager::Get().LoadFromJson("assets");

    // イベントマネージャーのアクションコールバック登録
    // 注: StageClear / GoToStage / SetSwitch / CallCommonEvent は EventManager 内部で直接処理されるため、ここには来ない
    EventManager::Get().SetActionCallback([&](const std::string& action, const std::string& p1, const std::string& p2) {
        if (action == "ShowMessage") {
            currentMessageText = p1;
            currentMessageSpeaker = p2;
            isShowingMessage = true;
        } else if (action == "ChangeBgm") {
            SoundManager::Get().PlayBgm(p1);
        } else if (action == "PlaySe") {
            SoundManager::Get().PlaySe(p1);
        } else if (action == "ActivateGimmick") {
            for (auto& gim : gimmicks) {
                if (gim.assetId == p1) {
                    gim.isActive = true;
                    if (!p2.empty()) gim.param = p2;
                }
            }
        } else if (action == "OpenDoor") {
            for (auto& gim : gimmicks) {
                if (gim.type == GIMMICK_GATE_DOOR && gim.assetId == p1) {
                    gim.isActive = false; // isActive=falseが開いた状態を表す
                    gim.val1 = 1.0f;      // 重量スイッチの自動ロジックに上書きされないよう、手動オープン済みであることを記録
                }
            }
        } else if (action == "SpawnEnemy") {
            float sx = player.x, sy = player.y;
            size_t comma = p2.find(',');
            if (comma != std::string::npos) {
                try {
                    sx = std::stof(p2.substr(0, comma));
                    sy = std::stof(p2.substr(comma + 1));
                } catch (...) { sx = player.x; sy = player.y; }
            }
            for (auto& d : enemyDefs) {
                if (d.id == p1) {
                    Enemy e{ (EnemyType)d.type_enum, sx, sy, 0.0f, 0.0f, d.graphHandle, 1,
                             d.hitboxWidth, d.hitboxHeight, d.width, d.height, d.hitboxOffsetX, d.hitboxOffsetY,
                             d.hitboxWidth, d.hitboxHeight, d.scale, 0.0f, 1.0f, true, false, d.hp,
                             0.0f, 0, sx - 200.0f, sx + 200.0f, false, {}, d.id, AnimationController() };
                    e.anim.LoadForAsset(d.id, "assets");
                    enemies.push_back(e);
                    break;
                }
            }
        } else if (action == "SpawnItem") {
            float sx = player.x, sy = player.y;
            size_t comma = p2.find(',');
            if (comma != std::string::npos) {
                try {
                    sx = std::stof(p2.substr(0, comma));
                    sy = std::stof(p2.substr(comma + 1));
                } catch (...) { sx = player.x; sy = player.y; }
            }
            for (auto& d : itemDefs) {
                if (d.id == p1) {
                    Item newItem{ (ItemType)d.type_enum, sx, sy, (float)d.hitboxWidth, (float)d.hitboxHeight,
                                  (float)d.hitboxWidth, (float)d.hitboxHeight, (float)d.hitboxOffsetX, (float)d.hitboxOffsetY,
                                  (float)d.hitboxWidth, (float)d.hitboxHeight, true, false, false, {} };
                    newItem.assetId = d.id;
                    newItem.handle = d.graphHandle;
                    items.push_back(newItem);
                    break;
                }
            }
        } else if (action == "MoveCamera") {
            try {
                cameraOverrideX = std::stof(p1);
                cameraOverrideTimer = 2.0f; // 2秒間、自動追従を止めて指定座標に留まる
            } catch (...) {}
        }
    });


    // ================================================================
    // Feature: タイトル画面・ステージセレクト画面
    //
    // これらは gameScreen(640x480の仮想スクリーン)を経由せず、
    // 実ウィンドウ(1280x720)へ直接描く。理由は2つ。
    //   ・640x480に描いてから拡大すると、大きなタイトル文字がボケる
    //   ・編集UI（左パネル・インスペクタ・タイムライン）と重ねる必要が無い
    // 座標は game_config.json 側が640x480で持ち、等方1.5倍＋左右160pxの余白で配置する
    // （背景画像は全面に敷くので余白は見えない）。
    // ================================================================

    // フォントハンドルのキャッシュ（デザイン上のサイズ → ハンドル）。
    // SetFontSize は呼ぶたびに既定フォントを作り直す重い処理なので、
    // 複数サイズを使うタイトル画面では必ずハンドルを作ってキャッシュする。
    // 失敗値(-1)もキャッシュして、毎フレーム作り直しに行かないようにする。
    std::map<int, int> metaFontCache;
    auto MetaFont = [&](int designSize) -> int {
        auto it = metaFontCache.find(designSize);
        if (it != metaFontCache.end()) return it->second;
        int px = (int)(designSize * GameCfg::DESIGN_SCALE);
        // EDGE付きにするのは、背景画像の上に文字を置いても読めるようにするため。
        // 縁取りは CreateFontToHandle の FontType でしか指定できず、SetFontSize では作れない。
        int fh = CreateFontToHandle("メイリオ", px, 4, DX_FONTTYPE_ANTIALIASING_EDGE_4X4, -1, 2);
        metaFontCache[designSize] = fh;
        return fh;
    };

    // 画像のキャッシュ（パス → ハンドル）。毎フレーム LoadGraph するのは重い
    std::map<std::string, int> metaImageCache;
    auto MetaImage = [&](const std::string& path) -> int {
        if (path.empty()) return -1;
        auto it = metaImageCache.find(path);
        if (it != metaImageCache.end()) return it->second;
        int h = LoadGraph(path.c_str());
        metaImageCache[path] = h;
        return h;
    };

    auto MetaColor = [&](const GameCfg::Color& c) { return GetColor(c.r, c.g, c.b); };
    auto MetaRoleColor = [&](const std::string& role) -> unsigned int {
        if (role == "sub")    return MetaColor(gameConfig.inkSub);
        if (role == "accent") return MetaColor(gameConfig.inkAccent);
        return MetaColor(gameConfig.ink);
    };

    // デザイン座標(640x480)へ文字を描く。align=="center" のとき dx は中心座標。
    auto MetaDrawText = [&](const std::string& s, float dx, float dy, int fontSize,
                            const std::string& align, unsigned int color, bool edge) {
        if (s.empty()) return;
        int x = GameCfg::ToScreenX(dx);
        int y = GameCfg::ToScreenY(dy);
        int fh = MetaFont(fontSize);
        if (fh < 0) {
            // メイリオが使えない環境向けのフォールバック。
            // 見栄えは落ちるが、真っ白な画面になるよりはるかにまし。
            SetFontSize((int)(fontSize * GameCfg::DESIGN_SCALE));
            if (align == "center") x -= GetDrawStringWidth(s.c_str(), (int)s.size()) / 2;
            DrawString(x, y, s.c_str(), color);
            SetFontSize(16);
            return;
        }
        // GetDrawStringWidthToHandle の第2引数(StrLen)は、SetUseCharCodeFormat(UTF8) の状態では
        // 「文字数」ではなく「バイト数」を渡すのが正しい。
        // 実測: フォント30pxで "ラボ"(2文字/6バイト) を計測したところ、
        //   バイト数(6)を渡すと 68px（全角2文字ぶん＋縁取り。期待どおり）
        //   文字数(2)を渡すと 34px（1文字ぶんしか数えていない）
        // std::string::size() はバイト数なのでそのまま渡してよい。
        if (align == "center") x -= GetDrawStringWidthToHandle(s.c_str(), (int)s.size(), fh) / 2;
        DrawStringToHandle(x, y, s.c_str(), color, fh, edge ? GetColor(255, 255, 255) : 0);
    };

    // 指定した幅(デザイン座標)に収まるよう、文字サイズを段階的に落としてから描く。
    //
    // ステージ名やメニュー文言はエディタで自由に書き換えられるので、
    // 固定サイズにするといずれ必ずセルからはみ出す。
    // 「はみ出したら小さくする」を仕組みとして持たせておけば、
    // 長い名前を付けても崩れない。
    auto MetaDrawTextFit = [&](const std::string& str, float dx, float dy, int fontSize,
                               float maxWidthDesign, const std::string& align,
                               unsigned int color, bool edge) {
        if (str.empty()) return;
        int size = fontSize;
        const int minSize = 9; // これ以下は読めないので下限を設ける
        while (size > minSize) {
            int fh = MetaFont(size);
            if (fh < 0) break; // フォントが作れない環境ではフォールバックに任せる
            int wpx = GetDrawStringWidthToHandle(str.c_str(), (int)str.size(), fh);
            if (wpx <= GameCfg::ToScreenLen(maxWidthDesign)) break;
            size -= 1;
        }
        MetaDrawText(str, dx, dy, size, align, color, edge);
    };
    // デザイン座標の矩形をウィンドウ座標へ変換してボタンとして描く。
    // 見た目は既存UIと揃えて UIウィンドウ.png の9スライスを使う。
    auto MetaDrawButton = [&](float dl, float dt, float dw, float dh,
                              const std::string& label, int fontSize, bool hot, bool enabled) {
        int x1 = GameCfg::ToScreenX(dl);
        int y1 = GameCfg::ToScreenY(dt);
        int x2 = x1 + GameCfg::ToScreenLen(dw);
        int y2 = y1 + GameCfg::ToScreenLen(dh);
        if (!enabled)   SetDrawBright(150, 148, 145);
        else if (hot)   SetDrawBright(255, 255, 255);
        else            SetDrawBright(214, 209, 202);
        DrawUiWindow(x1, y1, x2, y2, uiWindowHandle);
        SetDrawBright(255, 255, 255);
        unsigned int col = !enabled ? MetaColor(gameConfig.inkSub)
                         : (hot ? MetaColor(gameConfig.inkAccent) : MetaColor(gameConfig.ink));
        // ラベルはボタンの中央へ置く（縦方向はフォントの高さぶんだけ上げる）
        // ラベルはボタンの中央へ。幅からはみ出す場合は自動で小さくなる
        MetaDrawTextFit(label, dl + dw * 0.5f, dt + dh * 0.5f - fontSize * 0.6f,
                        fontSize, dw - 16.0f, "center", col, false);
    };

    // メニュー項目 i 番目の矩形をデザイン座標で求める。
    //
    // 【重要】このレイアウト式は Lab_Editor 側の配置キャンバスにも同じものがある。
    // 片方だけ変えると「エディタで見た位置」と「実際に出る位置」がズレるので、
    // 変更するときは必ず両方を直すこと。変数を4つ(x,y,w,item_h,gap)に絞ってあるのは
    // そのため。
    //   align=="center" のとき:
    //     left = x - w/2,  top = y + i*(item_h + gap),  width = w,  height = item_h
    auto MetaMenuItemRect = [&](const GameCfg::Element& m, int i,
                                float& l, float& t, float& w, float& h) {
        w = m.w; h = m.itemH;
        l = (m.align == "center") ? (m.x - m.w * 0.5f) : m.x;
        t = m.y + i * (m.itemH + m.gap);
    };

    // 画面全体に背景を敷く（背景画像はウィンドウ全面へ拡大するので左右の余白は見えない）
    auto MetaDrawBackdrop = [&](const std::string& imgPath) {
        DrawBox(0, 0, WINDOW_WIDTH, WINDOW_HEIGHT, MetaColor(gameConfig.backdrop), TRUE);
        int bg = MetaImage(imgPath);
        if (bg >= 0) DrawExtendGraph(0, 0, WINDOW_WIDTH, WINDOW_HEIGHT, bg, TRUE);
    };

    // タイトル/セレクトのカーソル位置と、メインループへ「終了したい」と伝えるフラグ
    int titleCursor = 0;
    int selectCursor = 0;
    bool metaWantExit = false;
    std::string metaBgmPlaying = "";

    // ---- メタシーン（タイトル / ステージセレクト）の更新と描画 ----
    // メインループの先頭から呼ばれ、この中で ScreenFlip() まで済ませる。
    // 呼び出し側は直後に continue するので、ゲームプレイ本体は丸ごと飛ぶ。
    auto UpdateAndDrawMetaScene = [&]() {
        // このシーン専用の入力エッジ検出。
        // ゲームプレイ側の lastLeftClick は早期continueで更新されないため流用できない。
        static bool mPrevClick = false, mPrevUp = false, mPrevDown = false;
        static bool mPrevLeft = false, mPrevRight = false, mPrevDecide = false, mPrevCancel = false;

        int mx, my; GetMousePoint(&mx, &my);
        bool click = (GetMouseInput() & MOUSE_INPUT_LEFT) != 0;
        bool clickEdge = click && !mPrevClick;

        bool kUp     = CheckHitKey(KEY_INPUT_UP) != 0    || CheckHitKey(KEY_INPUT_W) != 0;
        bool kDown   = CheckHitKey(KEY_INPUT_DOWN) != 0  || CheckHitKey(KEY_INPUT_S) != 0;
        bool kLeft   = CheckHitKey(KEY_INPUT_LEFT) != 0  || CheckHitKey(KEY_INPUT_A) != 0;
        bool kRight  = CheckHitKey(KEY_INPUT_RIGHT) != 0 || CheckHitKey(KEY_INPUT_D) != 0;
        bool kDecide = CheckHitKey(KEY_INPUT_RETURN) != 0 || CheckHitKey(KEY_INPUT_Z) != 0;
        // ESCキーはメインループの継続条件でゲーム終了に割り当てられているため、
        // 「戻る」には使えない。BackSpace と X を使う。
        bool kCancel = CheckHitKey(KEY_INPUT_BACK) != 0 || CheckHitKey(KEY_INPUT_X) != 0;

        bool upEdge = kUp && !mPrevUp, downEdge = kDown && !mPrevDown;
        bool leftEdge = kLeft && !mPrevLeft, rightEdge = kRight && !mPrevRight;
        bool decideEdge = kDecide && !mPrevDecide, cancelEdge = kCancel && !mPrevCancel;

        // ゲームプレイ側と同じ脱出口。早期continueでメインループ末尾の判定を飛ばすため、
        // これが無いとタイトル画面から十字キー全押しで抜けられなくなる。
        if (CheckHitKey(KEY_INPUT_UP) && CheckHitKey(KEY_INPUT_DOWN)
            && CheckHitKey(KEY_INPUT_LEFT) && CheckHitKey(KEY_INPUT_RIGHT)) {
            metaWantExit = true;
        }

        // BGM。シーンが変わったときだけ切り替える（毎フレーム呼ぶと鳴り直す）
        const std::string& wantBgm = (currentScene == TITLE) ? gameConfig.titleBgm : gameConfig.selectBgm;
        if (wantBgm != metaBgmPlaying) {
            metaBgmPlaying = wantBgm;
            if (wantBgm.empty()) SoundManager::Get().StopBgm();
            else                 SoundManager::Get().PlayBgm(wantBgm);
        }

        SetDrawScreen(DX_SCREEN_BACK);
        ClearDrawScreen();

        if (currentScene == TITLE) {
            MetaDrawBackdrop(gameConfig.titleBg);

            // ロゴ・タイトル・サブタイトルを描く（メニューは後段でまとめて扱う）
            for (const auto& e : gameConfig.titleElements) {
                if (!e.visible) continue;
                if (e.type == "image") {
                    int h = MetaImage(e.image);
                    if (h < 0) continue;
                    // align=="center" のとき x,y は中心なので左上へ直す
                    float l = (e.align == "center") ? (e.x - e.w * 0.5f) : e.x;
                    float t = (e.align == "center") ? (e.y - e.h * 0.5f) : e.y;
                    DrawExtendGraph(GameCfg::ToScreenX(l), GameCfg::ToScreenY(t),
                                    GameCfg::ToScreenX(l) + GameCfg::ToScreenLen(e.w),
                                    GameCfg::ToScreenY(t) + GameCfg::ToScreenLen(e.h), h, TRUE);
                } else if (e.type == "text") {
                    MetaDrawText(e.text, e.x, e.y, e.fontSize, e.align, MetaRoleColor(e.colorRole), e.edge);
                }
            }

            // メニュー
            const GameCfg::Element* menu = gameConfig.FindElement("menu");
            int itemCount = (int)gameConfig.menuItems.size();
            if (menu != nullptr && menu->visible && itemCount > 0) {
                if (titleCursor < 0) titleCursor = itemCount - 1;
                if (titleCursor >= itemCount) titleCursor = 0;
                if (upEdge)   { titleCursor = (titleCursor - 1 + itemCount) % itemCount; SoundManager::Get().PlaySe("ui_color_cycle"); }
                if (downEdge) { titleCursor = (titleCursor + 1) % itemCount; SoundManager::Get().PlaySe("ui_color_cycle"); }

                int chosen = -1;
                for (int i = 0; i < itemCount; i++) {
                    float l, t, w, h; MetaMenuItemRect(*menu, i, l, t, w, h);
                    int x1 = GameCfg::ToScreenX(l), y1 = GameCfg::ToScreenY(t);
                    int x2 = x1 + GameCfg::ToScreenLen(w), y2 = y1 + GameCfg::ToScreenLen(h);
                    bool hover = (mx >= x1 && mx <= x2 && my >= y1 && my <= y2);
                    if (hover) titleCursor = i; // マウスを乗せたらカーソルもそこへ移す
                    MetaDrawButton(l, t, w, h, gameConfig.menuItems[i].label,
                                   menu->fontSize, (titleCursor == i), true);
                    if (hover && clickEdge) chosen = i;
                }
                if (decideEdge) chosen = titleCursor;

                if (chosen >= 0 && chosen < itemCount) {
                    const std::string& act = gameConfig.menuItems[chosen].action;
                    SoundManager::Get().PlaySe("ui_pause");
                    if (act == "quit") {
                        metaWantExit = true;
                    } else if (act == "continue") {
                        // 最後に遊んだステージから再開する。記録が無ければセレクトへ送る。
                        if (!saveData.lastPlayed.empty() && SwitchToStage(saveData.lastPlayed)) {
                            metaBgmPlaying = ""; // ステージBGMへ切り替わったので追従させる
                        } else {
                            currentScene = STAGE_SELECT;
                            selectCursor = 0;
                        }
                    } else { // "stage_select"
                        currentScene = STAGE_SELECT;
                        selectCursor = 0;
                    }
                }
            }

            MetaDrawText("[Enter]決定  [↑↓]選択  [Esc]終了", 320.0f, 448.0f, 14,
                         "center", MetaColor(gameConfig.inkSub), true);
        }
        else if (currentScene == STAGE_SELECT) {
            MetaDrawBackdrop(gameConfig.selectBg);
            MetaDrawText(gameConfig.heading, gameConfig.headingX, gameConfig.headingY,
                         gameConfig.headingFontSize, "center",
                         MetaColor(gameConfig.inkAccent), true);

            int count = (int)gameConfig.stages.size();
            const GameCfg::Grid& g = gameConfig.grid;
            int cols = (g.cols > 0) ? g.cols : 3;

            if (count > 0) {
                if (selectCursor < 0) selectCursor = 0;
                if (selectCursor >= count) selectCursor = count - 1;
                if (leftEdge)  { selectCursor = (selectCursor - 1 + count) % count; SoundManager::Get().PlaySe("ui_color_cycle"); }
                if (rightEdge) { selectCursor = (selectCursor + 1) % count; SoundManager::Get().PlaySe("ui_color_cycle"); }
                if (upEdge)    { selectCursor = (selectCursor - cols + count * 2) % count; SoundManager::Get().PlaySe("ui_color_cycle"); }
                if (downEdge)  { selectCursor = (selectCursor + cols) % count; SoundManager::Get().PlaySe("ui_color_cycle"); }
            }

            int chosen = -1;
            for (int i = 0; i < count; i++) {
                const GameCfg::StageEntry& s = gameConfig.stages[i];
                bool unlocked = GameCfg::IsStageUnlocked(gameConfig, saveData, (size_t)i);
                bool cleared = saveData.IsCleared(s.file);

                float l = g.x + (i % cols) * (g.cellW + g.gapX);
                float t = g.y + (i / cols) * (g.cellH + g.gapY);
                int x1 = GameCfg::ToScreenX(l), y1 = GameCfg::ToScreenY(t);
                int x2 = x1 + GameCfg::ToScreenLen(g.cellW), y2 = y1 + GameCfg::ToScreenLen(g.cellH);
                bool hover = (mx >= x1 && mx <= x2 && my >= y1 && my <= y2);
                if (hover) selectCursor = i;
                bool hot = (selectCursor == i);

                // 枠
                if (!unlocked)  SetDrawBright(150, 148, 145);
                else if (hot)   SetDrawBright(255, 255, 255);
                else            SetDrawBright(214, 209, 202);
                DrawUiWindow(x1, y1, x2, y2, uiWindowHandle);
                SetDrawBright(255, 255, 255);

                // サムネイル。用意されていない場合はテーマ色のブロックで代替する
                // （画像が無いだけでセルが空白になると、何が並んでいるのか分からなくなる）
                int thumbTop = y1 + GameCfg::ToScreenLen(8.0f);
                int thumbBot = y1 + GameCfg::ToScreenLen(g.cellH * 0.55f);
                int thumbL = x1 + GameCfg::ToScreenLen(10.0f);
                int thumbR = x2 - GameCfg::ToScreenLen(10.0f);
                int th = unlocked ? MetaImage(s.thumbnail) : -1;
                if (th >= 0) {
                    DrawExtendGraph(thumbL, thumbTop, thumbR, thumbBot, th, TRUE);
                } else {
                    DrawBox(thumbL, thumbTop, thumbR, thumbBot,
                            MetaColor(unlocked ? gameConfig.inkAccent : gameConfig.inkSub), TRUE);
                }

                // 名前と進捗
                float labelY = t + g.cellH * 0.60f;
                if (unlocked) {
                    // セル幅から少し内側(左右6pxずつ)に収める
                    MetaDrawTextFit(s.name, l + g.cellW * 0.5f, labelY, 15, g.cellW - 12.0f,
                                    "center", MetaColor(gameConfig.ink), true);
                    char prog[64];
                    sprintf_s(prog, sizeof(prog), "%s  %d / %d",
                              cleared ? "CLEAR" : "- - -",
                              saveData.BestItems(s.file), s.itemTotal);
                    MetaDrawText(prog, l + g.cellW * 0.5f, labelY + 22.0f, 13, "center",
                                 cleared ? MetaColor(gameConfig.inkAccent) : MetaColor(gameConfig.inkSub), true);
                } else {
                    MetaDrawTextFit(gameConfig.lockedLabel, l + g.cellW * 0.5f, labelY, 15,
                                    g.cellW - 12.0f, "center", MetaColor(gameConfig.inkSub), true);
                }

                if (hover && clickEdge) {
                    if (unlocked) chosen = i;
                    else SoundManager::Get().PlaySe("ui_denied"); // 選べない理由が伝わるように音で返す
                }
            }
            if (decideEdge && selectCursor >= 0 && selectCursor < count) {
                if (GameCfg::IsStageUnlocked(gameConfig, saveData, (size_t)selectCursor)) chosen = selectCursor;
                else SoundManager::Get().PlaySe("ui_denied");
            }

            // 「もどる」ボタン
            {
                float bw = 140.0f, bh = 34.0f;
                float bl = 320.0f - bw * 0.5f, bt = 418.0f;
                int x1 = GameCfg::ToScreenX(bl), y1 = GameCfg::ToScreenY(bt);
                int x2 = x1 + GameCfg::ToScreenLen(bw), y2 = y1 + GameCfg::ToScreenLen(bh);
                bool hover = (mx >= x1 && mx <= x2 && my >= y1 && my <= y2);
                MetaDrawButton(bl, bt, bw, bh, gameConfig.backLabel, 16, hover, true);
                if ((hover && clickEdge) || cancelEdge) {
                    SoundManager::Get().PlaySe("ui_pause");
                    currentScene = TITLE;
                }
            }

            if (chosen >= 0 && chosen < count) {
                SoundManager::Get().PlaySe("ui_pause");
                if (SwitchToStage(gameConfig.stages[chosen].file)) {
                    metaBgmPlaying = ""; // ステージBGMへ切り替わったので追従させる
                }
            }

            MetaDrawText("[Enter]決定  [方向キー]選択  [BackSpace]もどる", 320.0f, 462.0f, 13,
                         "center", MetaColor(gameConfig.inkSub), true);
        }

        mPrevClick = click; mPrevUp = kUp; mPrevDown = kDown;
        mPrevLeft = kLeft; mPrevRight = kRight;
        mPrevDecide = kDecide; mPrevCancel = kCancel;

        // 早期continueでメインループ末尾を飛ばすため、ここで自分で締める。
        // SoundManager::Update は再生し終わったSEのハンドルを回収する処理で、
        // これを呼ばないとメニュー音を鳴らすたびにハンドルが溜まり続ける。
        SoundManager::Get().Update();

        ScreenFlip();
    };

    // 最初のステージ初期化
    ResetStage();

    // ResetStage() は内部でステージBGMを鳴らして currentScene を PLAY にするので、
    // タイトルから始めたい場合はその後で上書きする。
    // 引数付き起動（エディタからのテストプレイ）ではタイトルを出さない。
    if (!bootDirectToStage && gameConfig.titleEnabled) {
        currentScene = TITLE;
        SoundManager::Get().StopBgm();
    }

    while (ProcessMessage() == 0 && CheckHitKey(KEY_INPUT_ESCAPE) == 0)
    {
        // Feature: タイトル画面・ステージセレクト画面 —
        // これらのシーンではゲームプレイ本体（約4500行）を丸ごと飛ばす。
        //
        // 条件式であちこちを潰す方式（currentScene != PLAY && != TITLE && ...）にすると、
        // 今後シーンを足すたびに全ての判定箇所を直す必要があり、直し忘れが即バグになる。
        // 先頭で抜けてしまえば、リザルト描画もCanUpdateもそもそも実行されない。
        //
        // なお ImGui の NewFrame と Render はどちらもループ末尾側にあるので、
        // ここで continue すると対で飛ぶ。ScreenFlip と SoundManager::Update は
        // UpdateAndDrawMetaScene の中で自前で呼んでいる。
        if (currentScene == TITLE || currentScene == STAGE_SELECT) {
            UpdateAndDrawMetaScene();
            if (metaWantExit) break;
            continue;
        }

        // ステージ全体の横幅/縦幅は固定タイル数決め打ちではなく、現在のステージの実際のマップサイズから算出する
        if (currentStageIdx >= 0 && currentStageIdx < (int)stages.size() && !stages[currentStageIdx].map.empty() && !stages[currentStageIdx].map[0].empty()) {
            STAGE_WIDTH = (float)stages[currentStageIdx].map[0].size() * TILE_SIZE;
            STAGE_HEIGHT = (float)stages[currentStageIdx].map.size() * TILE_SIZE;
        }

        // 十字キー全押しでゲームを閉じる（エディタへ戻る）
        if (CheckHitKey(KEY_INPUT_UP) && CheckHitKey(KEY_INPUT_DOWN) && CheckHitKey(KEY_INPUT_LEFT) && CheckHitKey(KEY_INPUT_RIGHT)) {
            break;
        }
        int mx, my;
        GetMousePoint(&mx, &my);
        float gx = (float)mx, gy = (float)my;
        if (isEditMode) { gx = (float)(mx - monitorX) + cameraX; gy = (float)(my - monitorY) + cameraY; }

        // Feature: 編集コストゲージ — 各操作の「ステージ許可 or アイテム解禁」判定（このフレームの全キー処理の前に確定させる）
        bool rewindOpEnabled = IsEditToolEnabled(currentEditTools.rewindEnabled, unlockedEditTools.rewindEnabled);
        bool pauseOpEnabled = IsEditToolEnabled(currentEditTools.pauseEnabled, unlockedEditTools.pauseEnabled);
        bool fastForwardOpEnabled = IsEditToolEnabled(currentEditTools.fastForwardEnabled, unlockedEditTools.fastForwardEnabled);
        bool screenEffectOpEnabled = IsEditToolEnabled(currentEditTools.screenEffectEnabled, unlockedEditTools.screenEffectEnabled);
        bool objectEditOpEnabled = IsEditToolEnabled(currentEditTools.objectEditEnabled, unlockedEditTools.objectEditEnabled);
        bool cutOpEnabled        = IsEditToolEnabled(currentEditTools.cutEnabled,        unlockedEditTools.cutEnabled);

        static bool lastMiddleClick = false;
        bool currentMiddleClick = (GetMouseInput() & MOUSE_INPUT_MIDDLE) != 0;
        if (currentMiddleClick && !lastMiddleClick) {
            if (isPaused) { isPaused = false; menu.isOpen = false; SoundManager::Get().PlaySe("ui_pause"); }
            else if (pauseOpEnabled && editCost > 0.0f) { isPaused = true; menu.isOpen = false; SoundManager::Get().PlaySe("ui_pause"); }
            else { SoundManager::Get().PlaySe("ui_denied"); }
        }
        lastMiddleClick = currentMiddleClick;

        // Feature: 操作性改善（友人フィードバック対応）— 一時停止をSpaceキーに変更
        // Feature: 編集コストゲージ — 一時停止は編集コストを消費する継続系操作。停止解除は常に無料
        static bool lastPauseKey = false;
        if (CheckHitKey(KEY_INPUT_SPACE) && !lastPauseKey) {
            if (isPaused) { isPaused = false; SoundManager::Get().PlaySe("ui_pause"); }
            else if (pauseOpEnabled && editCost > 0.0f) { isPaused = true; SoundManager::Get().PlaySe("ui_pause"); }
            else { SoundManager::Get().PlaySe("ui_denied"); }
        }
        lastPauseKey = (CheckHitKey(KEY_INPUT_SPACE) != 0);

        // Feature 5: ShowMessageアクションで表示中のメッセージウィンドウをEnterキーで閉じる
        static bool lastMsgKey = false;
        bool currentMsgKey = CheckHitKey(KEY_INPUT_RETURN) != 0;
        if (isShowingMessage && currentMsgKey && !lastMsgKey) { isShowingMessage = false; }
        lastMsgKey = currentMsgKey;

        // Feature: 編集コストゲージ — 早送りは継続系操作。解除は常に無料
        static bool lastFFKey = false;
        if (CheckHitKey(KEY_INPUT_F) && !lastFFKey) {
            if (isFastForward) { isFastForward = false; SoundManager::Get().PlaySe("ui_fastforward"); }
            else if (fastForwardOpEnabled && editCost > 0.0f) { isFastForward = true; SoundManager::Get().PlaySe("ui_fastforward"); }
            else { SoundManager::Get().PlaySe("ui_denied"); }
        }
        lastFFKey = (CheckHitKey(KEY_INPUT_F) != 0);

        // Tキー：色フィルタを巡回（なし→赤→緑→青→なし）。クロマキー地形と連動する。
        // Feature: 編集コストゲージ — 色フィルタは「画面エフェクト」に属する単発コスト操作。「なし」へ戻す遷移は常に無料
        static bool lastColorKey = false;
        bool currentColorKey = CheckHitKey(KEY_INPUT_T) != 0;
        if (currentColorKey && !lastColorKey) {
            int nextFilter = (playerColorFilter + 1) % 4;
            bool turningOff = (playerColorFilter != 0 && nextFilter == 0);
            if (turningOff || (screenEffectOpEnabled && editCost >= currentEditCost.flatColorCycle)) {
                if (!turningOff) editCost -= currentEditCost.flatColorCycle;
                playerColorFilter = nextFilter;
                SoundManager::Get().PlaySe("ui_color_cycle");
            } else {
                SoundManager::Get().PlaySe("ui_denied");
            }
        }
        lastColorKey = currentColorKey;

        // Mキー：SEのミュート切り替え（ステルス用途）
        static bool lastMuteKey = false;
        bool currentMuteKey = CheckHitKey(KEY_INPUT_M) != 0;
        if (currentMuteKey && !lastMuteKey) {
            SoundManager::Get().SetMuted(!SoundManager::Get().IsMuted());
        }
        lastMuteKey = currentMuteKey;

        static bool lastStepKey = false;
        bool currF3 = CheckHitKey(KEY_INPUT_F3);
        if (currF3 && !lastF3) isDebugDrawMode = !isDebugDrawMode;
        lastF3 = currF3;

        isStepFrame = (isEditMode && isPaused && CheckHitKey(KEY_INPUT_RIGHT) && !lastStepKey);
        lastStepKey = (CheckHitKey(KEY_INPUT_RIGHT) != 0);

        static bool lastLeftClick = false;
        bool currentLeftClick = (GetMouseInput() & MOUSE_INPUT_LEFT) != 0;
        static bool lastRightClick = false;
        bool currentRightClick = (GetMouseInput() & MOUSE_INPUT_RIGHT) != 0;

        globalTimeScale = isFastForward ? 2.0f : 1.0f;
        float finalTimeScale = globalTimeScale;

        // 選択状態とRキーに基づくアクティブな巻き戻しフラグ
        bool isRKeyPressed = (CheckHitKey(KEY_INPUT_R) && isEditMode && rewindOpEnabled && editCost > 0.0f);
        bool isPlayerRewinding = player.isRewinding || (isRKeyPressed && !isRotating && (selectedType == SELECT_PLAYER || selectedType == SELECT_NONE));

        // 「巻き戻しが今アクティブか」の集約フラグ（コストドレイン計算用）
        bool isAnyRewindActive = isRKeyPressed || player.isRewinding;
        if (!isAnyRewindActive) for (auto& e : enemies)  if (e.isRewinding)  { isAnyRewindActive = true; break; }
        if (!isAnyRewindActive) for (auto& g : gimmicks) if (g.isRewinding)  { isAnyRewindActive = true; break; }
        if (!isAnyRewindActive) for (auto& b : bullets)  if (b.isRewinding)  { isAnyRewindActive = true; break; }
        if (!isAnyRewindActive) for (auto& it : items)   if (it.isRewinding) { isAnyRewindActive = true; break; }

        // Feature: カット機能の復活 — このブロックには「個別オブジェクト編集」と「タイムラインカット」の
        // 2系統の操作が同居している。どちらか一方だけを許可したステージを作れるように、
        // 入口はORで通し、実際の操作ごとに objectEditOpEnabled / cutOpEnabled を個別に見る。
        if (isEditMode && (objectEditOpEnabled || cutOpEnabled)) {
            // コンテキストメニューの有効化
            if (currentRightClick && !lastRightClick) { 
                // コンテキストメニューの当たり判定・選択のトリガー
                if (gx >= player.x && gx <= player.x + player.width * player.scale &&
                    gy >= player.y && gy <= player.y + player.height * player.scale) {
                    selectedPlayers.clear(); selectedEnemies.clear(); selectedGimmicks.clear();
                    selectedPlayers.push_back(&player);
                    selectedType = SELECT_PLAYER;
                    targetScale = &player.scale;
                    targetAngle = &player.angle;
                    targetSpeedScale = &player.speedScale;
                    targetPaused = &player.isPaused;
                    targetRewind = &player.isRewinding;
                    targetDirection = &player.direction;
                    targetEnemyType = nullptr;
                    targetGimmick = nullptr;
                    targetEnemy = nullptr;
                }
                else {
                    // 敵の選択状態をチェック
                    bool enemySelected = false;
                    for (auto& enemy : enemies) {
                        if (enemy.isActive && gx >= enemy.x && gx <= enemy.x + enemy.width * enemy.scale &&
                            gy >= enemy.y && gy <= enemy.y + enemy.height * enemy.scale) {
                            selectedPlayers.clear(); selectedEnemies.clear(); selectedGimmicks.clear();
                            selectedEnemies.push_back(&enemy);
                            selectedType = SELECT_ENEMY;
                            targetScale = &enemy.scale;
                            targetAngle = &enemy.angle;
                            targetSpeedScale = &enemy.speedScale;
                            targetPaused = &enemy.isPaused;
                            targetRewind = &enemy.isRewinding;
                            targetDirection = &enemy.direction;
                            targetEnemyType = &enemy.type;
                            targetGimmick = nullptr;
                            targetEnemy = &enemy;
                            enemySelected = true;
                            break;
                        }
                    }
                    
                    if (!enemySelected) {
                        // ギミックの選択状態をチェック
                        // Feature: ポータルの作り直し（友人フィードバック対応）— CUT_PORTALも他のギミックと同様、
                        // 通常のgim.x/gim.yに基づくクリック選択の対象にする（専用タイムラインUIは廃止）
                        bool gimSelected = false;
                        // オブジェクト側の選択は「個別オブジェクト編集」の許可が要る
                        for (auto& gim : gimmicks) {
                            if (!objectEditOpEnabled) break;
                            // タイムラインカットは座標を持たない（x,y,w,hが全て0）ため、
                            // ここで判定するとマップ左上の1点に当たり判定があるように振る舞ってしまう。
                            // カットの選択は後段の「タイムライン帯上での右クリック」で行う。
                            if (gim.isTimelineCut) continue;
                            if (gx >= gim.x && gx <= gim.x + gim.spriteWidth &&
                                gy >= gim.y && gy <= gim.y + gim.spriteHeight) {
                                selectedPlayers.clear(); selectedEnemies.clear(); selectedGimmicks.clear();
                                selectedGimmicks.push_back(&gim);
                                selectedType = SELECT_GIMMICK;
                                targetScale = &gim.width; // スケールを幅にマッピング！
                                targetAngle = &gim.angle;
                                targetSpeedScale = &gim.speedScale; // 従来はcustomTimerを流用しUI上は"N/A"だった
                                targetPaused = &gim.isPaused;
                                targetRewind = &gim.isRewinding;
                                targetDirection = &gim.direction; // 従来はnullptrでギミックだけFlipが死んでいた
                                targetEnemyType = nullptr;
                                targetGimmick = &gim;
                                targetEnemy = nullptr;
                                gimSelected = true;
                                break;
                            }
                        }

                        // Feature: カット機能の復活 — タイムライン帯の上で右クリックした場合は、
                        // その位置に重なっているカットを選択する（選択後、コンテキストメニューから削除できる）。
                        if (!gimSelected && cutOpEnabled && my >= WINDOW_HEIGHT - 60 && my <= WINDOW_HEIGHT - 20) {
                            float ct = (float)(mx - 50) / (float)(WINDOW_WIDTH - 100);
                            for (auto& gim : gimmicks) {
                                if (gim.type != GIMMICK_CUT_PORTAL || !gim.isActive || !gim.isTimelineCut) continue;
                                if (ct < gim.val1 || ct > gim.val2) continue;
                                selectedPlayers.clear(); selectedEnemies.clear(); selectedGimmicks.clear();
                                selectedGimmicks.push_back(&gim);
                                selectedType = SELECT_GIMMICK;
                                // カットは拡大・回転・速度変更のいずれも意味を持たないので、
                                // インスペクタ側の対象ポインタは全てnullptrにしておく（誤操作でクラッシュしないように）
                                targetScale = nullptr;
                                targetAngle = nullptr;
                                targetSpeedScale = nullptr;
                                targetPaused = nullptr;
                                targetRewind = nullptr;
                                targetDirection = &gim.direction; // 従来はnullptrでギミックだけFlipが死んでいた
                                targetEnemyType = nullptr;
                                targetGimmick = &gim;
                                targetEnemy = nullptr;
                                gimSelected = true;
                                break;
                            }
                        }

                        if (!gimSelected) {
                            selectedPlayers.clear(); selectedEnemies.clear(); selectedGimmicks.clear();
                            selectedType = SELECT_NONE;
                            targetScale = nullptr;
                            targetAngle = nullptr;
                            targetSpeedScale = nullptr;
                            targetPaused = nullptr;
                            targetRewind = nullptr;
                            targetDirection = nullptr;
                            targetEnemyType = nullptr;
                            targetGimmick = nullptr;
                            targetEnemy = nullptr;
                        }
                    }
                }
                
                if (selectedType == SELECT_NONE) {
                    menu.isOpen = false;
                } else {
                    // Feature: ポータルの作り直し（友人フィードバック対応）— CUT_PORTAL専用の「Delete Cut」メニューは
                    // 廃止し、他のギミックと同じ標準コンテキストメニュー（巻き戻し/一時停止トグル等）を使う
                    menu.height = 160;
                    menu.isOpen = true;
                    menu.x = mx;
                    menu.y = my;
                }
            }

            // コンテキストメニューのアクショントリガー
            if (currentLeftClick && !lastLeftClick && menu.isOpen) {
                if (mx >= menu.x && mx <= menu.x + menu.width && selectedType != SELECT_NONE) {
                    // Feature: カット機能の復活 — タイムラインカットを選択している場合は専用メニュー（Delete Cutのみ）。
                    // カットに対しては巻き戻し/一時停止/速度といった通常項目が全て無意味で、
                    // かつ target 系ポインタがnullptrなので、通常メニューの処理へ流してはいけない（nullptr参照でクラッシュする）。
                    if (targetGimmick != nullptr && targetGimmick->isTimelineCut) {
                        if (my >= menu.y + 5 && my <= menu.y + 30) {
                            if (editCost >= currentEditCost.flatMenuToggle) {
                                editCost -= currentEditCost.flatMenuToggle;
                                // 巻き戻し履歴ごと存在を消したいので、非表示にするのではなく配列から削除する
                                for (auto it = gimmicks.begin(); it != gimmicks.end(); ++it) {
                                    if (&(*it) == targetGimmick) { gimmicks.erase(it); break; }
                                }
                                selectedPlayers.clear(); selectedEnemies.clear(); selectedGimmicks.clear();
                                selectedType = SELECT_NONE;
                                targetGimmick = nullptr;
                                SoundManager::Get().PlaySe("ui_color_cycle");
                            } else {
                                SoundManager::Get().PlaySe("ui_denied");
                            }
                        }
                        menu.isOpen = false;
                    }
                    else {
                        // 巻き戻しの切り替え（Feature: 編集コストゲージ）
                        if (my >= menu.y + 5 && my <= menu.y + 30) {
                            if (editCost >= currentEditCost.flatMenuToggle) { editCost -= currentEditCost.flatMenuToggle; *targetRewind = !(*targetRewind); }
                            else SoundManager::Get().PlaySe("ui_denied");
                            menu.isOpen = false;
                        }
                        // 一時停止の切り替え（Feature: 編集コストゲージ）
                        else if (my >= menu.y + 31 && my <= menu.y + 55) {
                            if (editCost >= currentEditCost.flatMenuToggle) { editCost -= currentEditCost.flatMenuToggle; *targetPaused = !(*targetPaused); }
                            else SoundManager::Get().PlaySe("ui_denied");
                            menu.isOpen = false;
                        }
                        // 速度 +0.5（Feature: 編集コストゲージ）
                        // Feature: 編集リアクション — ギミックにもspeedScaleを持たせたので、
                        // 以前あった「ギミック選択時は何もしない」という除外は不要になった。
                        else if (my >= menu.y + 56 && my <= menu.y + 80) {
                            if (targetSpeedScale != nullptr) {
                                if (editCost >= currentEditCost.flatSpeedChange) {
                                    editCost -= currentEditCost.flatSpeedChange;
                                    *targetSpeedScale += 0.5f;
                                    if (selectedType == SELECT_GIMMICK && targetGimmick != nullptr) targetGimmick->editDirtyMask |= EDIT_DIRTY_SPEED;
                                    for (auto* e : selectedEnemies) e->editDirtyMask |= EDIT_DIRTY_SPEED;
                                }
                                else SoundManager::Get().PlaySe("ui_denied");
                            }
                            menu.isOpen = false;
                        }
                        // 速度 -0.5（Feature: 編集コストゲージ）
                        else if (my >= menu.y + 81 && my <= menu.y + 105) {
                            if (targetSpeedScale != nullptr) {
                                if (editCost >= currentEditCost.flatSpeedChange) {
                                    editCost -= currentEditCost.flatSpeedChange;
                                    *targetSpeedScale -= 0.5f;
                                    if (*targetSpeedScale < 0) *targetSpeedScale = 0;
                                    if (selectedType == SELECT_GIMMICK && targetGimmick != nullptr) targetGimmick->editDirtyMask |= EDIT_DIRTY_SPEED;
                                    for (auto* e : selectedEnemies) e->editDirtyMask |= EDIT_DIRTY_SPEED;
                                } else SoundManager::Get().PlaySe("ui_denied");
                            }
                            menu.isOpen = false;
                        }
                        // オブジェクトの向きを反転（Feature: 編集コストゲージ）
                        else if (my >= menu.y + 106 && my <= menu.y + 130) {
                            if (targetDirection != nullptr) {
                                if (editCost >= currentEditCost.flatDirectionFlip) {
                                    editCost -= currentEditCost.flatDirectionFlip;
                                    *targetDirection = (*targetDirection == 0 ? 1 : 0);
                                    if (selectedType == SELECT_GIMMICK && targetGimmick != nullptr) targetGimmick->editDirtyMask |= EDIT_DIRTY_DIR;
                                    for (auto* e : selectedEnemies) e->editDirtyMask |= EDIT_DIRTY_DIR;
                                }
                                else SoundManager::Get().PlaySe("ui_denied");
                            }
                            menu.isOpen = false;
                        }
                        // すべてリセット（Feature: 編集コストゲージ）
                        else if (my >= menu.y + 131 && my <= menu.y + 155) {
                            if (editCost >= currentEditCost.flatResetAll) {
                                editCost -= currentEditCost.flatResetAll;
                                // Feature: 編集リアクション — 配置時の値(editBase*)へ丸ごと戻す。
                                // 以前は幅120・角度1.57079といった決め打ちだったため、
                                // 120px以外のギミック（gim_gate_doorは32x160、gim_edit_color_bridgeは224x24）が
                                // リセットのたびに別物のサイズへ化けていた。
                                // 併せてeditDirtyMaskも消す。これがサイズロック等
                                // 「一度編集したらAIが値の所有権を手放す」系リアクションの解除手段になる。
                                if (selectedType == SELECT_GIMMICK && targetGimmick != nullptr) {
                                    SetGimmickWidth(*targetGimmick, targetGimmick->editBaseWidth);
                                    SetGimmickHeight(*targetGimmick, targetGimmick->editBaseHeight);
                                    targetGimmick->angle = targetGimmick->editBaseAngle;
                                    targetGimmick->speedScale = 1.0f;
                                    targetGimmick->direction = 0;
                                    targetGimmick->isPaused = false;
                                    targetGimmick->isRewinding = false;
                                    targetGimmick->editDirtyMask = EDIT_DIRTY_NONE;
                                } else {
                                    for (auto* e : selectedEnemies) {
                                        e->scale = e->editBaseScale;
                                        e->angle = e->editBaseAngle;
                                        e->direction = e->editBaseDirection;
                                        e->speedScale = 1.0f;
                                        e->isPaused = false;
                                        e->isRewinding = false;
                                        e->editDirtyMask = EDIT_DIRTY_NONE;
                                    }
                                    if (selectedEnemies.empty()) {
                                        *targetScale = 1.0f; *targetAngle = 0.0f; *targetSpeedScale = 1.0f; *targetPaused = false; *targetRewind = false;
                                    }
                                }
                            } else SoundManager::Get().PlaySe("ui_denied");
                            menu.isOpen = false;
                        }
                    }
                } else { menu.isOpen = false; }
            }

            // エディタの範囲選択ドラッグ終了
            if (!currentLeftClick && lastLeftClick && isAreaSelecting) {
                isAreaSelecting = false;
                
                float selX1 = (float)(min(areaSelectStartX, areaSelectEndX) - monitorX) + cameraX;
                float selX2 = (float)(max(areaSelectStartX, areaSelectEndX) - monitorX) + cameraX;
                float selY1 = (float)(min(areaSelectStartY, areaSelectEndY) - monitorY) + cameraY;
                float selY2 = (float)(max(areaSelectStartY, areaSelectEndY) - monitorY) + cameraY;

                selectedPlayers.clear();
                selectedEnemies.clear();
                selectedGimmicks.clear();

                // 重複している場合にプレイヤーを選択
                float pw = (float)player.width * player.scale;
                float ph = (float)player.height * player.scale;
                if (player.x + pw >= selX1 && player.x <= selX2 && player.y + ph >= selY1 && player.y <= selY2) {
                    selectedPlayers.push_back(&player);
                }

                // 重複している場合に敵を選択
                for (auto& enemy : enemies) {
                    if (enemy.isActive) {
                        float ew = (float)enemy.hitboxWidth * enemy.scale;
                        float eh = (float)enemy.hitboxHeight * enemy.scale;
                        if (enemy.x + ew >= selX1 && enemy.x <= selX2 && enemy.y + eh >= selY1 && enemy.y <= selY2) {
                            selectedEnemies.push_back(&enemy);
                        }
                    }
                }

                // 重複している場合にギミックを選択
                for (auto& gim : gimmicks) {
                    if (gim.isActive) {
                        if (gim.x + gim.spriteWidth >= selX1 && gim.x <= selX2 && gim.y + gim.spriteHeight >= selY1 && gim.y <= selY2) {
                            selectedGimmicks.push_back(&gim);
                        }
                    }
                }

                // 単一ターゲット用の便利なポインタを最初に選択された要素にバインド
                if (!selectedPlayers.empty()) {
                    selectedType = SELECT_PLAYER;
                    targetScale = &player.scale;
                    targetAngle = &player.angle;
                    targetSpeedScale = &player.speedScale;
                    targetPaused = &player.isPaused;
                    targetRewind = &player.isRewinding;
                    targetDirection = &player.direction;
                    targetEnemyType = nullptr;
                    targetGimmick = nullptr;
                    targetEnemy = nullptr;
                }
                else if (!selectedEnemies.empty()) {
                    selectedType = SELECT_ENEMY;
                    targetScale = &selectedEnemies[0]->scale;
                    targetAngle = &selectedEnemies[0]->angle;
                    targetSpeedScale = &selectedEnemies[0]->speedScale;
                    targetPaused = &selectedEnemies[0]->isPaused;
                    targetRewind = &selectedEnemies[0]->isRewinding;
                    targetDirection = &selectedEnemies[0]->direction;
                    targetEnemyType = &selectedEnemies[0]->type;
                    targetGimmick = nullptr;
                    targetEnemy = selectedEnemies[0];
                }
                else if (!selectedGimmicks.empty()) {
                    selectedType = SELECT_GIMMICK;
                    targetScale = &selectedGimmicks[0]->width;
                    targetAngle = &selectedGimmicks[0]->angle;
                    targetSpeedScale = &selectedGimmicks[0]->customTimer;
                    targetPaused = &selectedGimmicks[0]->isPaused;
                    targetRewind = &selectedGimmicks[0]->isRewinding;
                    targetDirection = nullptr;
                    targetEnemyType = nullptr;
                    targetGimmick = selectedGimmicks[0];
                    targetEnemy = nullptr;
                }
                else {
                    selectedType = SELECT_NONE;
                    targetScale = nullptr; targetAngle = nullptr; targetSpeedScale = nullptr;
                    targetPaused = nullptr; targetRewind = nullptr; targetDirection = nullptr;
                    targetEnemyType = nullptr; targetGimmick = nullptr; targetEnemy = nullptr;
                }
            }

            // エディタUI / ドラッグ操作
            if (currentLeftClick && !menu.isOpen) {
                // 一時停止ボタンのチェック
                if (!lastLeftClick && mx >= PAUSE_BUTTON_X1 && mx <= PAUSE_BUTTON_X2 && my >= PAUSE_BUTTON_Y1 && my <= PAUSE_BUTTON_Y2) {
                    if (isPaused) { isPaused = false; SoundManager::Get().PlaySe("ui_pause"); }
                    else if (pauseOpEnabled && editCost > 0.0f) { isPaused = true; SoundManager::Get().PlaySe("ui_pause"); }
                    else { SoundManager::Get().PlaySe("ui_denied"); }
                }

                // Feature: カット機能の復活 — 下部タイムライン帯へのCtrl+クリックでカット区間を作る。
                // 1回目のクリックで始点(tempCutStart)を打ち、2回目のクリックで区間が確定してカットが生成される。
                // 生成されたカットは isTimelineCut=true のCUT_PORTALとして gimmicks に積まれ、
                // val1=始点比率 / val2=終点比率 を持つ（ワールド座標は持たないので x,y,w,h は全て0）。
                // 編集コストゲージの管理下に置くため、コストが足りなければ生成せずに拒否音を鳴らす。
                // なお、この処理を含むブロック全体が objectEditOpEnabled で囲われているので、
                // 「オブジェクト編集」が封印されているステージではカットも作れない（＝ステージ側で制御できる）。
                if (!lastLeftClick && cutOpEnabled && my >= WINDOW_HEIGHT - 60 && my <= WINDOW_HEIGHT - 20) {
                    if (CheckHitKey(KEY_INPUT_LCONTROL) || CheckHitKey(KEY_INPUT_RCONTROL)) {
                        // クリックX座標をタイムライン帯(左右50px余白)上の比率0.0〜1.0へ変換する
                        float ct = (float)(mx - 50) / (float)(WINDOW_WIDTH - 100);
                        if (ct < 0.0f) ct = 0.0f;
                        if (ct > 1.0f) ct = 1.0f;
                        // Feature: カットコストの距離変動 — 2点目を打った時点で「その長さの」コストを計算する。
                        // 1点目のクリックではまだ距離が決まらないので、コスト判定も2点目まで持ち越す。
                        float pendingCutCost = (tempCutStart >= 0.0f)
                            ? ComputeCutCreateCost(tempCutStart, ct, STAGE_WIDTH, currentEditCost)
                            : currentEditCost.flatCutCreate;
                        if (tempCutStart < 0.0f) {
                            // 1点目：始点を記録するだけ。ここではまだコストを消費しない
                            tempCutStart = ct;
                            SoundManager::Get().PlaySe("ui_color_cycle");
                        } else if (editCost >= pendingCutCost) {
                            // 2点目：区間が確定。クリック順に関係なく小さい方を始点にそろえる
                            float start = (ct > tempCutStart ? tempCutStart : ct);
                            float end   = (ct > tempCutStart ? ct : tempCutStart);
                            // 幅が無いに等しいカットは意味がない上、境界の判定が不安定になるので弾く
                            if (end - start < 0.01f) {
                                tempCutStart = -1.0f;
                                SoundManager::Get().PlaySe("ui_denied");
                            } else {
                                editCost -= pendingCutCost;
                                // push_backでgimmicksが再確保されると、選択中オブジェクトを指している
                                // targetGimmick / selectedGimmicks の生ポインタが全てダングリングになる。
                                // そのまま触ると不正アクセスで落ちるので、追加の前に選択を解除しておく。
                                selectedPlayers.clear(); selectedEnemies.clear(); selectedGimmicks.clear();
                                selectedType = SELECT_NONE;
                                targetScale = nullptr; targetAngle = nullptr; targetSpeedScale = nullptr;
                                targetPaused = nullptr; targetRewind = nullptr; targetDirection = nullptr;
                                targetEnemyType = nullptr; targetGimmick = nullptr; targetEnemy = nullptr;

                                Gimmick cut{ GIMMICK_CUT_PORTAL, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f,
                                             true, start, end, 0.0f, 0.0f, false, false, {} };
                                cut.isTimelineCut = true;
                                gimmicks.push_back(cut);
                                tempCutStart = -1.0f;
                                SoundManager::Get().PlaySe("ui_fastforward");
                            }
                        } else {
                            // コスト不足。始点は残さず捨てて、打ち直しさせる
                            tempCutStart = -1.0f;
                            SoundManager::Get().PlaySe("ui_denied");
                        }
                    }
                }

                // インスペクターのドラッグと切り替え（選択されたすべてに適用）
                if (!lastLeftClick && objectEditOpEnabled && mx >= WINDOW_WIDTH - 240 && selectedType != SELECT_NONE) {
                    // 各行の target 系ポインタは「その対象では意味を持たない項目」の場合にnullptrになる
                    // （インスペクタ表示側は既にN/A表示で分岐している）。タイムラインカットのように
                    // 全項目がnullptrになる選択対象があるため、ドラッグ開始時も必ずnullチェックしてから参照する。
                    if (my >= 80 && my <= 95 && targetScale != nullptr) { isInspScale = true; lastMouseX = mx; baseScale = *targetScale; }
                    else if (my >= 100 && my <= 115 && targetAngle != nullptr) { isInspAngle = true; lastMouseX = mx; baseAngle = *targetAngle; }
                    else if (my >= 120 && my <= 135 && targetSpeedScale != nullptr) { isInspSpeed = true; lastMouseX = mx; baseSpeed = *targetSpeedScale; }
                    else if (my >= 140 && my <= 155 && targetPaused != nullptr) {
                        // Feature: 編集コストゲージ — 複数選択でも定額（対象数に関わらず一発分のコスト）
                        if (editCost >= currentEditCost.flatMenuToggle) {
                            editCost -= currentEditCost.flatMenuToggle;
                            bool nextPaused = !(*targetPaused);
                            for (auto* p : selectedPlayers) p->isPaused = nextPaused;
                            for (auto* e : selectedEnemies) e->isPaused = nextPaused;
                            for (auto* g : selectedGimmicks) g->isPaused = nextPaused;
                        } else SoundManager::Get().PlaySe("ui_denied");
                    }
                    else if (my >= 160 && my <= 175 && targetRewind != nullptr) {
                        if (editCost >= currentEditCost.flatMenuToggle) {
                            editCost -= currentEditCost.flatMenuToggle;
                            bool nextRewind = !(*targetRewind);
                            for (auto* p : selectedPlayers) p->isRewinding = nextRewind;
                            for (auto* e : selectedEnemies) e->isRewinding = nextRewind;
                            for (auto* g : selectedGimmicks) g->isRewinding = nextRewind;
                        } else SoundManager::Get().PlaySe("ui_denied");
                    }
                    else if (my >= 180 && my <= 195 && targetEnemyType != nullptr) {
                        // 旧: %3 固定で最初の3種類しか巡回できなかった。ENEMY_TYPE_COUNTで全種別を巡回対象にする
                        EnemyType nextType = (EnemyType)((*targetEnemyType + 1) % ENEMY_TYPE_COUNT);
                        for (auto* e : selectedEnemies) e->type = nextType;
                    }
                }

                // ドラッグ中の範囲更新
                if (isAreaSelecting) {
                    areaSelectEndX = mx;
                    areaSelectEndY = my;
                }

                // Gキーで可変地面を追加
                static bool lastGKey = false;
                bool currentGKey = CheckHitKey(KEY_INPUT_G) != 0;
                if (currentGKey && !lastGKey && objectEditOpEnabled) {
                    int imgW, imgH;
                    GetGraphSize(jimenHandle, &imgW, &imgH);
                    float h = 32.0f;
                    float tileW = h * ((float)imgW / imgH);
                    float w = tileW * 3.0f; // 初期は3タイル分

                    // push_backでgimmicksが再確保されると、選択中オブジェクトを指している
                    // targetGimmick / selectedGimmicks の生ポインタが全てダングリングになる。
                    // そのまま触ると不正アクセスで落ちるので、追加の前に選択を解除しておく
                    // （タイムラインカット生成側と同じ手順）。
                    selectedPlayers.clear(); selectedEnemies.clear(); selectedGimmicks.clear();
                    selectedType = SELECT_NONE;
                    targetScale = nullptr; targetAngle = nullptr; targetSpeedScale = nullptr;
                    targetPaused = nullptr; targetRewind = nullptr; targetDirection = nullptr;
                    targetEnemyType = nullptr; targetGimmick = nullptr; targetEnemy = nullptr;

                    // 【重要】ここは以前 hitboxWidth / hitboxHeight の2つを渡し忘れており、
                    // 集成初期化の値が1つずつ前へずれていた：
                    //   hitboxWidth ← true(=1.0f) / hitboxHeight ← 0.0f / isActive ← 0.0f(=false)
                    // true→float も 0.0f→bool も「定数式で元の値へ戻せる」ため narrowing とみなされず、
                    // コンパイルは通ってしまう。その結果、Gキーで置いた地面は isActive==false のまま生成され、
                    // 描画も当たり判定も全てスキップされて「置いても何も出てこない」状態だった。
                    // Gimmickのメンバ順（type,x,y,width,height,spriteWidth,spriteHeight,
                    // hitboxOffsetX,hitboxOffsetY,hitboxWidth,hitboxHeight,isActive,val1,val2,
                    // angle,customTimer,isPaused,isRewinding,history）に合わせて19個を渡す。
                    gimmicks.push_back({ GIMMICK_SCALABLE_GROUND, gx - w / 2.0f, gy - h / 2.0f, w, h, w, h,
                                         0.0f, 0.0f, w, h, true, 0.0f, 0.0f, 0.0f, 0.0f, false, false, {} });
                }
                lastGKey = currentGKey;

                // オブジェクトの選択と変形（タイムラインやパネル内ではなく、モニター画面のプレビュー内のみで選択されるようにする）
                if (!lastLeftClick && objectEditOpEnabled && !isDragging && !isScaling && !isScalingHeight && !isRotating && !isInspScale && !isInspAngle && !isInspSpeed && !isAreaSelecting &&
                    mx >= monitorX && mx <= monitorX + SCREEN_WIDTH && my >= monitorY && my <= monitorY + SCREEN_HEIGHT) {
                    
                    // エディタでの破壊可能なブロックのクリックをチェック（破壊する！）
                    bool blockClicked = false;
                    for (auto& gim : gimmicks) {
                        if (gim.type == GIMMICK_BREAKABLE_BLOCK && gim.isActive) {
                            if (gx >= gim.x && gx <= gim.x + gim.spriteWidth &&
                                gy >= gim.y && gy <= gim.y + gim.spriteHeight) {
                                gim.isActive = false;
                                blockClicked = true;
                                break;
                            }
                        }
                    }

                    if (!blockClicked) {
                        // グループドラッグを開始するため、まず現在選択されているオブジェクトをクリックしたかどうかをチェック
                        bool clickedOnSelected = false;
                        if (selectedType != SELECT_NONE) {
                            for (auto* p : selectedPlayers) {
                                if (gx >= p->x && gx <= p->x + p->width * p->scale && gy >= p->y && gy <= p->y + p->height * p->scale) {
                                    clickedOnSelected = true; break;
                                }
                            }
                            for (auto* e : selectedEnemies) {
                                if (e->isActive && gx >= e->x && gx <= e->x + e->hitboxWidth * e->scale &&
                                    gy >= e->y && gy <= e->y + e->hitboxHeight * e->scale) {
                                    clickedOnSelected = true; break;
                                }
                            }
                            for (auto* g : selectedGimmicks) {
                                if (g->isActive && gx >= g->x && gx <= g->x + g->spriteWidth && gy >= g->y && gy <= g->y + g->spriteHeight) {
                                    clickedOnSelected = true; break;
                                }
                            }
                        }

                        if (clickedOnSelected) {
                            // グループドラッグを開始
                            isDragging = true;
                            lastMouseX = mx;
                            lastMouseY = my;
                        }
                        else {
                            // 新しいオブジェクトをクリックした場合、そのオブジェクトのみを選択
                            bool objectClicked = false;
                            
                            // プレイヤーのチェック
                            if (gx >= player.x && gx <= player.x + player.width * player.scale &&
                                gy >= player.y && gy <= player.y + player.height * player.scale) {
                                selectedPlayers.clear(); selectedEnemies.clear(); selectedGimmicks.clear();
                                selectedPlayers.push_back(&player);
                                selectedType = SELECT_PLAYER;
                                targetScale = &player.scale;
                                targetAngle = &player.angle;
                                targetSpeedScale = &player.speedScale;
                                targetPaused = &player.isPaused;
                                targetRewind = &player.isRewinding;
                                targetDirection = &player.direction;
                                targetEnemyType = nullptr; targetGimmick = nullptr; targetEnemy = nullptr;
                                
                                if (CheckHitKey(KEY_INPUT_S)) { isScaling = true; lastMouseY = my; baseScale = player.scale; }
                                else if (CheckHitKey(KEY_INPUT_R)) { isRotating = true; lastMouseX = mx; baseAngle = player.angle; }
                                else { isDragging = true; lastMouseX = mx; lastMouseY = my; }
                                objectClicked = true;
                            }
                            // 敵のチェック
                            if (!objectClicked) {
                                for (auto& enemy : enemies) {
                                    if (enemy.isActive && gx >= enemy.x && gx <= enemy.x + enemy.hitboxWidth * enemy.scale &&
                                        gy >= enemy.y && gy <= enemy.y + enemy.hitboxHeight * enemy.scale) {
                                        selectedPlayers.clear(); selectedEnemies.clear(); selectedGimmicks.clear();
                                        selectedEnemies.push_back(&enemy);
                                        selectedType = SELECT_ENEMY;
                                        targetScale = &enemy.scale;
                                        targetAngle = &enemy.angle;
                                        targetSpeedScale = &enemy.speedScale;
                                        targetPaused = &enemy.isPaused;
                                        targetRewind = &enemy.isRewinding;
                                        targetDirection = &enemy.direction;
                                        targetEnemyType = &enemy.type;
                                        targetGimmick = nullptr;
                                        targetEnemy = &enemy;
                                        
                                        if (CheckHitKey(KEY_INPUT_S)) { isScaling = true; lastMouseY = my; baseScale = enemy.scale; }
                                        else if (CheckHitKey(KEY_INPUT_R)) { isRotating = true; lastMouseX = mx; baseAngle = enemy.angle; }
                                        else { isDragging = true; lastMouseX = mx; lastMouseY = my; }
                                        objectClicked = true;
                                        break;
                                    }
                                }
                            }
                            // ギミックのチェック
                            if (!objectClicked) {
                                for (auto& gim : gimmicks) {
                                    if (gim.isActive) {
                                        if (gx >= gim.x && gx <= gim.x + gim.spriteWidth &&
                                            gy >= gim.y && gy <= gim.y + gim.spriteHeight) {
                                            selectedPlayers.clear(); selectedEnemies.clear(); selectedGimmicks.clear();
                                            selectedGimmicks.push_back(&gim);
                                            selectedType = SELECT_GIMMICK;
                                            targetScale = &gim.width;
                                            targetAngle = &gim.angle;
                                            targetSpeedScale = &gim.speedScale; // 従来はcustomTimerを流用しUI上は"N/A"だった
                                            targetPaused = &gim.isPaused;
                                            targetRewind = &gim.isRewinding;
                                            targetDirection = nullptr;
                                            targetEnemyType = nullptr;
                                            targetGimmick = &gim;
                                            targetEnemy = nullptr;
                                            
                                            if (CheckHitKey(KEY_INPUT_S)) { isScaling = true; lastMouseY = my; baseScale = gim.width; }
                                            else if (CheckHitKey(KEY_INPUT_W)) { isScalingHeight = true; lastMouseY = my; baseScale = gim.spriteHeight; }
                                            else if (CheckHitKey(KEY_INPUT_R)) { isRotating = true; lastMouseX = mx; baseAngle = gim.angle; }
                                            else { isDragging = true; lastMouseX = mx; lastMouseY = my; }
                                            objectClicked = true;
                                            break;
                                        }
                                    }
                                }
                            }

                            // 何もクリックしなかった場合、範囲選択を開始
                            if (!objectClicked) {
                                selectedPlayers.clear(); selectedEnemies.clear(); selectedGimmicks.clear();
                                selectedType = SELECT_NONE;
                                targetScale = nullptr; targetAngle = nullptr; targetSpeedScale = nullptr;
                                targetPaused = nullptr; targetRewind = nullptr; targetDirection = nullptr;
                                targetEnemyType = nullptr; targetGimmick = nullptr; targetEnemy = nullptr;
                                
                                isAreaSelecting = true;
                                areaSelectStartX = mx;
                                areaSelectStartY = my;
                                areaSelectEndX = mx;
                                areaSelectEndY = my;
                            }
                        }
                    }
                }

                // 選択されたすべてのオブジェクトに一斉に変形を適用
                // Feature: 編集リアクション — 「編集はタイムラインの外側にある」という方針。
                //
                // 変形するとき、現在値だけでなく巻き戻し履歴の同じ軸も一括で同じ量だけずらす。
                // こうしないと「傾けた直後に巻き戻すと角度だけ元に戻る」「幅は履歴に無いので残る」といった
                // 軸ごとにバラバラな挙動になり、プレイヤーから何が保存され何が戻るのか読めなくなる。
                // 巻き戻すのは位置・速度・生死だけ、編集した形はそのまま維持される、に統一する。
                // 履歴は最大600件だがドラッグ中のフレームだけの処理なのでコストは無視できる。
                if (isDragging && selectedType != SELECT_NONE) {
                    float prevGx = (float)(lastMouseX - monitorX) + cameraX;
                    float prevGy = (float)(lastMouseY - monitorY) + cameraY;
                    float dx = gx - prevGx;
                    float dy = gy - prevGy;

                    for (auto* p : selectedPlayers) { p->x += dx; p->y += dy; p->vx = 0; p->vy = 0; }
                    for (auto* e : selectedEnemies) {
                        e->x += dx; e->y += dy; e->vx = 0; e->vy = 0;
                        e->editDirtyMask |= EDIT_DIRTY_POS;
                        for (auto& h : e->history) { h.x += dx; h.y += dy; }
                    }
                    for (auto* g : selectedGimmicks) {
                        g->x += dx; g->y += dy;
                        g->editDirtyMask |= EDIT_DIRTY_POS;
                        for (auto& h : g->history) { h.x += dx; h.y += dy; }
                    }

                    lastMouseX = mx;
                    lastMouseY = my;
                }
                if (isScaling && selectedType != SELECT_NONE) {
                    float ds = (float)(lastMouseY - my) * 0.01f;
                    float dw = (float)(lastMouseY - my) * 1.0f;
                    for (auto* p : selectedPlayers) { p->scale += ds; if (p->scale < 0.1f) p->scale = 0.1f; }
                    for (auto* e : selectedEnemies) {
                        e->scale += ds; if (e->scale < 0.1f) e->scale = 0.1f;
                        e->editDirtyMask |= EDIT_DIRTY_SCALE;
                        for (auto& h : e->history) { h.scale = e->scale; } // 編集後の大きさは巻き戻しても維持する
                    }
                    for (auto* g : selectedGimmicks) {
                        g->editDirtyMask |= EDIT_DIRTY_WIDTH;
                        g->width += dw;
                        if (g->width < 10.0f) g->width = 10.0f;
                        if (g->type == GIMMICK_SCALABLE_GROUND) {
                            int imgW, imgH;
                            GetGraphSize(jimenHandle, &imgW, &imgH);
                            float tileW = g->spriteHeight * ((float)imgW / imgH);
                            g->width = roundf(g->width / tileW) * tileW;
                            if (g->width < tileW) g->width = tileW;
                        }
                        g->spriteWidth = g->width; // 描画と重量スイッチはspriteWidthを見るため同期が必須
                    }
                    lastMouseY = my;
                }
                if (isScalingHeight && selectedType != SELECT_NONE) {
                    float dh = (float)(lastMouseY - my) * 1.0f;
                    for (auto* g : selectedGimmicks) {
                        g->editDirtyMask |= EDIT_DIRTY_HEIGHT;
                        g->spriteHeight += dh;
                        if (g->spriteHeight < 10.0f) g->spriteHeight = 10.0f;
                        if (g->type == GIMMICK_SCALABLE_GROUND) {
                            int imgW, imgH;
                            GetGraphSize(jimenHandle, &imgW, &imgH);
                            float tileW = g->spriteHeight * ((float)imgW / imgH);
                            g->width = roundf(g->width / tileW) * tileW;
                            if (g->width < tileW) g->width = tileW;
                            g->spriteWidth = g->width;
                        }
                        g->height = g->spriteHeight; // 当たり判定はheightを見るため同期が必須
                    }
                    lastMouseY = my;
                }
                if (isRotating && selectedType != SELECT_NONE) {
                    float da = (float)(mx - lastMouseX) * 0.02f;
                    for (auto* p : selectedPlayers) { p->angle += da; }
                    for (auto* e : selectedEnemies) {
                        e->angle += da;
                        e->editDirtyMask |= EDIT_DIRTY_ANGLE;
                        for (auto& h : e->history) { h.angle = e->angle; } // 傾けた姿勢は巻き戻しても維持する
                    }
                    // angleをAIが自前の状態に使っている型は回転させない（回すと壊れるため）
                    for (auto* g : selectedGimmicks) {
                        if (GimmickAngleIsAiOwned(g->type)) continue;
                        g->angle += da;
                        g->editDirtyMask |= EDIT_DIRTY_ANGLE;
                        for (auto& h : g->history) { h.angle = g->angle; }
                    }
                    lastMouseX = mx;
                }
                if (isInspScale && selectedType != SELECT_NONE) {
                    float ds = (float)(mx - lastMouseX) * 0.01f;
                    float dw = (float)(mx - lastMouseX) * 1.0f;
                    for (auto* p : selectedPlayers) { p->scale += ds; if (p->scale < 0.1f) p->scale = 0.1f; }
                    for (auto* e : selectedEnemies) {
                        e->scale += ds; if (e->scale < 0.1f) e->scale = 0.1f;
                        e->editDirtyMask |= EDIT_DIRTY_SCALE;
                        for (auto& h : e->history) { h.scale = e->scale; }
                    }
                    for (auto* g : selectedGimmicks) {
                        SetGimmickWidth(*g, g->width + dw);
                        g->editDirtyMask |= EDIT_DIRTY_WIDTH;
                    }
                    lastMouseX = mx;
                }
                if (isInspAngle && selectedType != SELECT_NONE) {
                    float da = (float)(mx - lastMouseX) * 0.02f;
                    for (auto* p : selectedPlayers) { p->angle += da; }
                    for (auto* e : selectedEnemies) {
                        e->angle += da;
                        e->editDirtyMask |= EDIT_DIRTY_ANGLE;
                        for (auto& h : e->history) { h.angle = e->angle; }
                    }
                    // 回転ドラッグ(R+ドラッグ)と同じ理由でAI所有の型は除外する
                    for (auto* g : selectedGimmicks) {
                        if (GimmickAngleIsAiOwned(g->type)) continue;
                        g->angle += da;
                        g->editDirtyMask |= EDIT_DIRTY_ANGLE;
                        for (auto& h : g->history) { h.angle = g->angle; }
                    }
                    lastMouseX = mx;
                }
                if (isInspSpeed && selectedType != SELECT_NONE) {
                    float dsp = (float)(mx - lastMouseX) * 0.05f;
                    for (auto* p : selectedPlayers) { p->speedScale += dsp; if (p->speedScale < 0) p->speedScale = 0; }
                    for (auto* e : selectedEnemies) {
                        e->speedScale += dsp; if (e->speedScale < 0) e->speedScale = 0;
                        e->editDirtyMask |= EDIT_DIRTY_SPEED;
                    }
                    // Feature: 編集リアクション — ギミックもspeedScaleを持つようになったのでここで動かせる
                    for (auto* g : selectedGimmicks) {
                        g->speedScale += dsp; if (g->speedScale < 0) g->speedScale = 0;
                        g->editDirtyMask |= EDIT_DIRTY_SPEED;
                    }
                    lastMouseX = mx;
                }
            } else { 
                isDragging = isScaling = isScalingHeight = isRotating = false; 
                isInspScale = isInspAngle = isInspSpeed = false;
            }
        }

        // --- 装置等による更新可否判定 ---
        // ignoresPause には「この個体はプレイヤーの一時停止を無視して動き続ける」場合に true を渡す。
        // 幽霊タイプの敵のように、時間を止めても止まらない＝止める以外の対処を強いる相手を成立させるための逃げ道。
        // ただしドラッグ中・スケール変更中などの「エディタ操作中」は、操作対象が動くと掴めなくなるので
        // 従来どおり一律停止させる（＝最初の早期returnより後ろで判定する）。
        auto CanUpdate = [&](float ox, float oy, float ow, float oh, float scale, bool isObjPaused, bool ignoresPause = false) {
            // 「PLAY以外は全部止める」ではなく、止めたいシーンを明示する。
            // タイトルやステージセレクトはメインループの先頭で早期continueしており
            // そもそもここへ来ないが、条件を曖昧にしておくと将来シーンを足したときに
            // 意図しない場所が止まる/動くという事故につながる。
            if (currentScene == RESULT_GAMEOVER || currentScene == RESULT_VICTORY
                || isDragging || isScaling || isScalingHeight || isRotating || isShowingMessage) return false;
            if (!isPaused && !isObjPaused && !isInspScale && !isInspAngle && !isInspSpeed) return true;
            if (isStepFrame) return true;
            if (ignoresPause) return true;
            
            // TIME_FIELD ギミックの影響範囲内ならポーズ中でも動ける
            float cx = ox + (ow * scale) / 2.0f;
            float cy = oy + (oh * scale) / 2.0f;
            for (const auto& gim : gimmicks) {
                if (gim.type == GIMMICK_TIME_FIELD && gim.isActive) {
                    float dx = cx - gim.x;
                    float dy = cy - gim.y;
                    // val1 が配置時に設定されていればそれを半径として使い、無ければGimmickDefの既定半径を使う
                    const GimmickDef* gdef = FindGimmickDef(gim.assetId);
                    float radius = gim.val1 > 0 ? gim.val1 : (gdef ? gdef->radius : 100.0f);
                    if (dx * dx + dy * dy <= radius * radius) {
                        return true;
                    }
                }
            }
            return false;
        };

        bool canPlayerAct = CanUpdate(player.x, player.y, (float)player.width, (float)player.height, player.scale, player.isPaused);

        // 射撃（通常モードまたはEnterキー）
        static bool lastShot = false;
        bool isClickInScreen = (mx >= monitorX && mx <= monitorX + SCREEN_WIDTH && my >= monitorY && my <= monitorY + SCREEN_HEIGHT);
        bool currentShot = (CheckHitKey(KEY_INPUT_RETURN) != 0) || (currentLeftClick && isClickInScreen && selectedType == SELECT_NONE);
        if (currentShot && !lastShot && canPlayerAct) {
            for (int i = 0; i < MAX_BULLETS; i++) {
                if (!bullets[i].isActive) {
                    bullets[i].isActive = true;
                    bullets[i].x = player.x + (player.direction == 0 ? (float)player.width * player.scale : -10.0f);
                    bullets[i].y = player.y + (float)player.height * player.scale / 4.0f;
                    bullets[i].vx = (player.direction == 0 ? BULLET_SPEED : -BULLET_SPEED);
                    bullets[i].vy = 0.0f; // Y方向速度は0で初期化（プレイヤーの弾は常に水平方向にのみ飛ぶため）
                    bullets[i].isPlayerOwned = true; // プレイヤーの弾
                    bullets[i].isRewinding = false;
                    bullets[i].history.clear();
                    break;
                }
            }
        }
        lastShot = currentShot;
        bool prevLeftClick = lastLeftClick; // RETRY判定用：lastLeftClick更新前の値を保存
        lastLeftClick = currentLeftClick; lastRightClick = currentRightClick;

        // キーボード移動（通常モード、または一時停止していないエディットモード）
        // editorPlayerCapsのbaseSpeed/baseJumpPowerをステージ設定として使用
        // Feature: ジャンプ連打・オーディオ制御の修正（友人フィードバック対応）— ジャンプキーをB/F/T/M等と同様の
        // edge-trigger方式にする。天井に頭をぶつけて即座にisJumpingがfalseへ戻っても、キーを押しっぱなしのままでは
        // 再発火しないため、SE連続再生や不自然な連続ジャンプを防げる。
        static bool lastJumpKey = false;
        bool currentJumpKey = CheckHitKey(KEY_INPUT_W) != 0;
        if (currentScene == PLAY && canPlayerAct) {
            float baseSpd = editorPlayerCaps.baseSpeed;
            float baseJmp = (float)editorPlayerCaps.baseJumpPower;
            bool isShift = (CheckHitKey(KEY_INPUT_LSHIFT) || CheckHitKey(KEY_INPUT_RSHIFT));
            // ダッシュ能力があればShiftでダッシュ、なければ通常速度のみ
            float speed = (isShift && editorPlayerCaps.canDash) ? baseSpd * 2.0f : baseSpd;
            player.vx = 0;
            if (CheckHitKey(KEY_INPUT_A)) { player.vx = -speed; player.direction = 1; }
            if (CheckHitKey(KEY_INPUT_D)) { player.vx = speed; player.direction = 0; }
            if (currentJumpKey && !lastJumpKey && !player.isJumping) {
                player.vy = baseJmp;
                player.isJumping = true;
                SoundManager::Get().PlaySe("jump");
            }
        } else {
            player.vx = 0;
        }
        lastJumpKey = currentJumpKey;

        // --- バッファへの描画 / 物理更新 ---
        SetDrawScreen(gameScreen);
        ClearDrawScreen();

        auto CheckCollision = [](float x1, float y1, float w1, float h1, float x2, float y2, float w2, float h2) {
            return (x1 < x2 + w2 && x1 + w1 > x2 && y1 < y2 + h2 && y1 + h1 > y2);
        };

        float ts = isStepFrame ? 1.0f : finalTimeScale;

        // Feature: 編集リアクション — 画面エフェクトの現在値を、当たり判定側からも読めるよう共有する。
        // 明暗やズームは1フレーム遅れて追従する値(fxCur*)を使うが、
        // 見えている画面と判定を一致させたいので、追従後の値をそのまま渡すのが正しい。
        g_screenFx.brightness  = fxCurBright;
        g_screenFx.zoom        = fxCurZoom;
        g_screenFx.colorFilter = playerColorFilter;
        g_screenFx.fastForward = isFastForward;
        Screen_BeginFrame(); // 敵/ギミックがこのフレームで上書きしなければニュートラルへ戻る
        BehaviorInterpreter::globalOpsThisFrame = 0; // Feature: Puzzle-like Behavior Scripting (M2) — 全スクリプト実行体の命令数予算を毎フレームリセット
        BehaviorInterpreter::globalFrameCounter += 1.0f; // Feature: Composite Multi-Part Objects (Parts-M2) — TIME_FIELD演出用（ポーズ中も止めずに加算し続ける必要があるためこのままにする）
        // スクリプトのTimeレポーター用の時計はポーズ中に止める。globalFrameCounterのままだと、ポーズ中に経過した
        // フレーム数がポーズ解除後の最初のTickでいきなり反映され、Time依存の回転/振動パーツが不連続にジャンプしてしまう。
        if (!isPaused || isStepFrame) BehaviorInterpreter::scriptTimeCounter += 1.0f;

        // Feature 3 & 5: 各種マネージャーの更新
        if (!isPaused || isStepFrame) {
            float dt = 1.0f / 60.0f * ts;
            SoundManager::Get().Update();
            
            // Feature 2: アニメーションの更新
            player.anim.Update(dt);
            for (auto& enemy : enemies) {
                enemy.anim.Update(dt);
            }
            
            int activeEnemies = 0;
            for (auto& e : enemies) if (e.isActive && e.hp > 0) activeEnemies++;

            // Feature 5: ItemCollected条件用に、収集済みアイテムのassetIdを毎フレーム最新の状態から再構築する
            // 巻き戻しでisCollectedがfalseに戻った場合も正しく追従するよう、蓄積ではなく都度再計算する。
            collectedItemIds.clear();
            for (auto& it : items) if (it.isCollected) collectedItemIds.push_back(it.assetId);

            bool stageClear = false;
            std::string gotoStage = "";
            EventManager::Get().Update(dt, player.x, player.y, activeEnemies, collectedItemIds, stageClear, gotoStage);
            
            if (stageClear) {
                currentScene = RESULT_VICTORY;
            } else if (!gotoStage.empty()) {
                SwitchToStage(gotoStage);
            }
        }

        // 1. プレイヤーの更新（巻き戻し vs 通常物理）
        if (isPlayerRewinding) {
            if (!player.history.empty()) {
                PlayerState s = player.history.back();
                player.history.pop_back();
                player.x = s.x; player.y = s.y; player.vx = s.vx; player.vy = s.vy;
                player.direction = s.direction; player.isJumping = s.isJumping;
                player.scale = s.scale; player.angle = s.angle; player.speedScale = s.speedScale;
                player.isPaused = s.isPaused;
                player.hp = s.hp;
            }
        } else {
            if (canPlayerAct) {
                float pts = ts * player.speedScale;
                float prevPlayerX = player.x; // 移動前のX座標を保持しておく（境界を正確にまたいだ瞬間を検出するのに使う）

                if (player.invulnTimer > 0.0f) player.invulnTimer -= pts;

                // 直前フレームに乗っていた動くギミックの移動量を追従させる（1フレーム遅延の簡易キャリー）
                if (player.ridingGimmickIndex >= 0 && player.ridingGimmickIndex < (int)gimmicks.size()) {
                    player.x += gimmicks[player.ridingGimmickIndex].lastDeltaX;
                    player.y += gimmicks[player.ridingGimmickIndex].lastDeltaY;
                }

                player.vy += GRAVITY * pts; 
                
                // X/Y方向の移動と衝突判定を共通化
                bool isGrounded = UpdatePhysicsCollisions(player.x, player.y, player.vx * pts, player.vy, player.vy,
                                                          player.width, player.height, player.scale,
                                                          stages[currentStageIdx].map, tileDefs, gimmicks);

                // 従来の足場との着地衝突判定
                bool platGrounded = CheckPlatformCollision(player.x, player.y, player.vy, player.width, player.height, player.scale, platforms, gimmicks, &player.ridingGimmickIndex);
                // Feature: 編集リアクション（共通層）— 個別に一時停止した敵は足場になる。
                // 相手が何であっても必ず通用する手札として用意することで、
                // 「届かない高さは、そこにいる敵を止めて踏み台にする」という解法がどのステージでも成立する。
                if (!platGrounded) {
                    platGrounded = CheckFrozenEnemyPlatform(player.x, player.y, player.vy,
                                                            player.width, player.height, player.scale, enemies);
                }
                
                if (isGrounded || platGrounded) {
                    player.isJumping = false;
                }

                // Feature: ポータルの作り直し（友人フィードバック対応）— タイムライン比率の通過判定ではなく、
                // 通常のX,Y配置されたポータル同士をgim.paramの一致でペアリングし、AABB接触で転移させる。
                // 転移直後に転移先のポータルへ即座に押し戻される（無限往復）のを防ぐため、
                // portalWasTouchingによるエッジトリガー（「触れていない→触れた」の瞬間のみ発火）を用いる。
                float pw_scaled = (float)player.width * player.scale;
                float ph_scaled = (float)player.height * player.scale;
                for (auto& gim : gimmicks) {
                    if (gim.type != GIMMICK_CUT_PORTAL || !gim.isActive) continue;
                    if (gim.isTimelineCut) continue; // タイムラインカットは座標を持たないので、このAABB判定の対象外
                    bool touching = CheckCollision(player.x, player.y, pw_scaled, ph_scaled, gim.x, gim.y, gim.spriteWidth, gim.spriteHeight);
                    // 編集リアクション：
                    //  ・幅を広げる → 出口が「広げたぶんだけ先」へずれる。同じポータル対でも到達点を伸ばせる。
                    //  ・向き反転   → 一方通行になる（入口としては働かず、出口専用になる）。
                    //  ・傾ける     → 出口の高さがずれる（上下の別ルートへ飛ばせる）。
                    EditReaction pr = GetGimmickEditReaction(gim);
                    if (touching && !gim.portalWasTouching && !gim.param.empty() && !pr.flipped) {
                        for (auto& other : gimmicks) {
                            if (&other == &gim || other.type != GIMMICK_CUT_PORTAL || !other.isActive) continue;
                            if (other.param != gim.param) continue;
                            player.x = other.x + (gim.width - gim.editBaseWidth);
                            player.y = other.y - sinf(pr.tilt) * 96.0f;
                            other.portalWasTouching = true; // 転送先での即時再トリガーを防ぐ
                            const GimmickDef* gdef = FindGimmickDef(gim.assetId);
                            if (gdef) SoundManager::Get().PlaySe(gdef->seActivate);
                            break;
                        }
                    }
                    gim.portalWasTouching = touching;
                }

                // Feature: カット機能の復活 — タイムラインカットの通過判定。
                // プレイヤーのX座標をステージ全長で割った「タイムライン上の位置(0.0〜1.0)」を求め、
                // 前フレームの位置(prev_tp)と今フレームの位置(tp)を比べて、カット区間の境界を
                // 「またいだ瞬間」だけワープさせる。境界上に立ち止まっているだけでは発火しないので、
                // 行ったり来たりして無限にワープし続けることがない。
                // 右へ進んで始点をまたいだら終点へ、左へ戻って終点をまたいだら始点へ送る＝
                // 動画のタイムラインからその区間を切り取ったのと同じ見え方になる。
                {
                    float tp = player.x / STAGE_WIDTH;
                    float prev_tp = prevPlayerX / STAGE_WIDTH; // 前フレームのタイムライン位置
                    for (auto& gim : gimmicks) {
                        if (gim.type != GIMMICK_CUT_PORTAL || !gim.isActive || !gim.isTimelineCut) continue;
                        float timePos = gim.val1;       // カット区間の始点（比率）
                        float targetTimePos = gim.val2; // カット区間の終点（比率）
                        const GimmickDef* gdef = FindGimmickDef(gim.assetId);
                        // ワープ後に境界へめり込んだまま止まらないよう、少しだけ外側へ押し出す量
                        float warpOffset = gdef ? gdef->warpOffsetPx : 8.0f;

                        // 左の境界（timePos）を左から右へ越えたときのみ前方へワープ
                        if (player.vx > 0 && prev_tp < timePos && tp >= timePos) {
                            player.x = targetTimePos * STAGE_WIDTH + warpOffset;
                            SoundManager::Get().PlaySe(gdef ? gdef->seActivate : "ui_fastforward");
                        }
                        // 右の境界（targetTimePos）を右から左へ越えたときのみ後方へワープ
                        else if (player.vx < 0 && prev_tp > targetTimePos && tp <= targetTimePos) {
                            player.x = timePos * STAGE_WIDTH - pw_scaled - warpOffset;
                            SoundManager::Get().PlaySe(gdef ? gdef->seActivate : "ui_fastforward");
                        }
                    }
                }

                // ステージ境界の制限
                if (player.x < 0.0f) player.x = 0.0f;
                if (player.x > STAGE_WIDTH - (float)player.width * player.scale) {
                    player.x = STAGE_WIDTH - (float)player.width * player.scale;
                }

                // 1. 砂時計リフトの更新（プレイヤーが乗っているとゆっくり降下）
                for (auto& gim : gimmicks) {
                    if (gim.type == GIMMICK_FALLING_LIFT && gim.isActive) {
                        // 編集リアクション：
                        //  ・幅を広げる → 支える面積が増えて沈むのが遅くなる（渡りきる時間を稼げる）。
                        //  ・幅を狭める → 一気に沈む。
                        //  ・向き反転   → 沈まずに逆へ浮上する（上の足場へ運んでもらえる）。
                        //  ・傾ける     → 傾けた向きへ滑りながら沈む。
                        //  ・速度       → 沈下速度そのものが変わる。
                        const GimmickDef* gdef = FindGimmickDef(gim.assetId);
                        EditReaction lr = GetGimmickEditReaction(gim);
                        float sinkSpeed = (gdef ? gdef->sinkSpeed : 1.5f) * lr.MassMul() * gim.speedScale;
                        if (lr.flipped) sinkSpeed = -sinkSpeed; // 反転で浮上する
                        float maxDepth = gdef ? gdef->maxDepthOffset : 20.0f;
                        float objW = (float)player.width * player.scale;
                        float objH = (float)player.height * player.scale;
                        float footY = player.y + objH;
                        // プレイヤーがこのリフトに乗っているかチェック
                        if (player.vy >= 0.0f && player.x + objW >= gim.x && player.x <= gim.x + gim.spriteWidth) {
                            float threshold = player.vy + 8.0f;
                            if (footY >= gim.y && footY <= gim.y + threshold) {
                                gim.y += cosf(lr.tilt) * sinkSpeed * pts; // ゆっくり降下
                                gim.x += sinf(lr.tilt) * sinkSpeed * pts; // 傾けた向きへ滑る
                                if (sinkSpeed > 0.0f && gim.y > groundY - maxDepth) gim.y = groundY - maxDepth; // 地面を限界とする
                                if (gim.y < -200.0f) gim.y = -200.0f; // 浮上時に画面外へ飛び去らないよう天井を張る
                            }
                        }
                    }
                }

                // 2. 重量スイッチとドアの開閉ロジック
                // Feature: ゲート扉の修正（友人フィードバック対応）— 複数の重量スイッチ/ゲート扉が同じステージに
                // 存在する場合に個別ペアリングできるよう、各スイッチのアクティブ状態を個別に計算する。
                // ドア側のparamが空でなければ同じparamを持つスイッチとのみ連動し、空の場合は従来通り
                // 「最初に見つかったスイッチ」を全ドアに適用する（後方互換）。
                struct SwitchState { const Gimmick* gim; bool active; };
                std::vector<SwitchState> switchStates;
                for (const auto& switchGim : gimmicks) {
                    if (switchGim.type != GIMMICK_WEIGHT_SWITCH || !switchGim.isActive) continue;
                    const GimmickDef* gdef = FindGimmickDef(switchGim.assetId);
                    EditReaction sr = GetGimmickEditReaction(switchGim);
                    // 編集リアクション：
                    //  ・幅を広げる → 要求される重さが緩む（小さいボックスでも押せるようになる）。
                    //  ・幅を狭める → より大きく拡大したボックスでないと反応しない。
                    //  ・向き反転   → 条件が反転し「何も乗っていないとき」に作動する常時ONスイッチになる。
                    float switchTriggerThreshold = (gdef ? gdef->triggerWidthThreshold : 140.0f) * sr.MassMul();
                    bool active = false;
                    for (const auto& boxGim : gimmicks) {
                        if (boxGim.type == GIMMICK_SCALABLE_BOX && boxGim.isActive) {
                            // 拡大可能ボックスがスイッチの上にあるかチェック
                            if (boxGim.x + boxGim.spriteWidth >= switchGim.x && boxGim.x <= switchGim.x + switchGim.spriteWidth) {
                                // スイッチが要求する横幅以上に拡大されている必要がある
                                if (boxGim.spriteWidth >= switchTriggerThreshold) active = true;
                            }
                        }
                    }

                    // Feature: 編集リアクション — 敵の重さでもスイッチを押せるようにする。
                    //
                    // これは「編集していない状態の挙動」を変えてしまう唯一の項目なので、
                    // 既存ステージの解法を壊さないよう明示的なオプトインにしてある。
                    // ステージJSONのparamに "enemyweight" を含めたスイッチだけが敵に反応する。
                    // paramは文字列としてLab_Editorを素通りするため、エディタ側の改修は要らない。
                    if (switchGim.param.find("enemyweight") != std::string::npos) {
                        for (const auto& e : enemies) {
                            if (!e.isActive) continue;
                            float ew = (float)e.hitboxWidth * e.scale;
                            float eh = (float)e.hitboxHeight * e.scale;
                            // スイッチの真上に乗っているか（足元がスイッチ上面付近にあるか）
                            if (e.x + ew >= switchGim.x && e.x <= switchGim.x + switchGim.spriteWidth) {
                                float feet = e.y + eh;
                                if (feet >= switchGim.y - 12.0f && feet <= switchGim.y + switchGim.spriteHeight + 12.0f) {
                                    // 拡大された敵ほど重い。等倍でも押せる幅を基準にする
                                    if (ew * e.scale >= switchTriggerThreshold * 0.25f) active = true;
                                }
                            }
                        }
                    }

                    if (sr.flipped) active = !active; // 反転で条件が裏返る
                    switchStates.push_back({ &switchGim, active });
                }

                // スイッチの状態をゲートドアのロックに適用
                for (auto& gim : gimmicks) {
                    if (gim.type == GIMMICK_GATE_DOOR) {
                        if (gim.val1 > 0.5f) continue; // OpenDoorイベントで手動オープン済みのドアは自動ロジックの対象外
                        // Feature: 編集リアクション — 倒した扉は「足場」として使う状態なので、
                        // スイッチ連動でisActiveを毎フレーム上書きされると床ごと消えてしまう。
                        // 倒れている間は自動開閉の対象から外し、プレイヤーが作った足場を維持する。
                        if (GimmickIsTipped(gim)) { gim.isActive = true; continue; }
                        bool switchActive = false;
                        if (!gim.param.empty()) {
                            for (auto& sw : switchStates) if (sw.gim->param == gim.param) switchActive = switchActive || sw.active;
                        } else if (!switchStates.empty()) {
                            switchActive = switchStates[0].active; // 後方互換：param未指定なら最初に見つかったスイッチ
                        }
                        bool wasOpen = !gim.isActive;
                        gim.isActive = !switchActive; // スイッチがアクティブでない場合はドアを閉じる（isActive=true）
                        bool isOpenNow = !gim.isActive;
                        if (isOpenNow && !wasOpen) { // 開いた瞬間だけSE再生
                            const GimmickDef* gdef = FindGimmickDef(gim.assetId);
                            if (gdef) SoundManager::Get().PlaySe(gdef->seActivate);
                        }
                    }
                }
            }
            if (canPlayerAct) {
                player.history.push_back({ player.x, player.y, player.vx, player.vy, player.direction, player.isJumping, player.scale, player.angle, player.speedScale, player.isPaused, player.hp });
                if (player.history.size() > MAX_HISTORY_FRAMES) player.history.erase(player.history.begin());
            }
        }

        // 2. 敵の更新（巻き戻し vs 通常AI）
        for (auto& enemy : enemies) {
            if (!enemy.isActive && enemy.history.empty()) continue;

            bool isThisEnemyRew = enemy.isRewinding || (isRKeyPressed && !isRotating && (selectedType == SELECT_NONE || (selectedType == SELECT_ENEMY && targetEnemy == &enemy)));
            if (isThisEnemyRew) {
                if (!enemy.history.empty()) {
                    EnemyState s = enemy.history.back();
                    enemy.history.pop_back();
                    enemy.x = s.x; enemy.y = s.y; enemy.vx = s.vx; enemy.vy = s.vy;
                    enemy.direction = s.direction; enemy.scale = s.scale; enemy.angle = s.angle;
                    enemy.speedScale = s.speedScale; enemy.isActive = s.isActive;
                    enemy.isPaused = s.isPaused;
                    enemy.type = s.type;
                    enemy.hp = s.hp;
                    enemy.customTimer = s.customTimer; enemy.aiState = s.aiState;
                    enemy.patrolLeft = s.patrolLeft; enemy.patrolRight = s.patrolRight;
                    enemy.auxF1 = s.auxF1; enemy.auxF2 = s.auxF2;
                    enemy.auxState = s.auxState; enemy.auxFlag = s.auxFlag;
                    enemy.auxF3 = s.auxF3;
                }
            } else {
                // 新敵ロスター対応 — 定義側で ignorePause が立っている敵は一時停止中も動かす。
                // 定義が見つからない（旧データ等）場合は false 扱いで従来どおり止まる。
                const EnemyDef* pauseDef = FindEnemyDef(enemy.assetId);
                bool enemyIgnoresPause = (pauseDef != nullptr && pauseDef->ignorePause);
                bool canEnemyAct = enemy.isActive && CanUpdate(enemy.x, enemy.y, (float)enemy.hitboxWidth, (float)enemy.hitboxHeight, enemy.scale, enemy.isPaused, enemyIgnoresPause);
                if (canEnemyAct) {
                    float ets = ts * enemy.speedScale;

                    // 直前フレームに乗っていた動くギミックの移動量を追従させる（1フレーム遅延の簡易キャリー）
                    if (enemy.ridingGimmickIndex >= 0 && enemy.ridingGimmickIndex < (int)gimmicks.size()) {
                        enemy.x += gimmicks[enemy.ridingGimmickIndex].lastDeltaX;
                        enemy.y += gimmicks[enemy.ridingGimmickIndex].lastDeltaY;
                    }

                    // まず重力を適用する（この後の各AI分岐でvxやvyがさらに上書き・加算される）
                    enemy.vy += GRAVITY * ets;

                    // Feature: Configurable Behavior Parameters (M1) — このフレームのEnemyDefを一度だけ引く
                    const EnemyDef* edef = FindEnemyDef(enemy.assetId);

                    // ================= Feature: 編集リアクション（共通の前処理）=================
                    // このフレームの「プレイヤーが配置時からどれだけ手を加えたか」を一度だけ求め、
                    // 以降の全ての分岐で共有する。
                    EditReaction er = GetEnemyEditReaction(enemy, edef);

                    // AI分岐が enemy.vx を上書きする前の値。速度を上げすぎた相手に慣性を持たせるのに使う
                    // （専用のメンバを増やさずに済むよう、上書き前のこの瞬間に退避しておく）。
                    float erPrevVx = enemy.vx;

                    // 全型に共通で効く倍率。各AI分岐はEnemyDefの数値にこれを掛けて使うことで、
                    // 「拡大すると重くて鈍い」「暗いと相手に見つかりにくい」といったルールが
                    // 型ごとに書かなくても揃って効くようになる。
                    float erMass = er.MassMul(); // 拡大で鈍く、縮小で軽快に

                    // 索敵範囲の倍率。画面を暗くすれば見つかりにくく、明るくすれば見つかりやすい。
                    // 明暗の編集ツール(X/C)が、これまで一部のギミック以外に何の意味も持たなかったのを埋める。
                    float erVision = 1.0f;
                    if (fxCurBright < 0.6f)      erVision = 0.6f;
                    else if (fxCurBright > 1.3f) erVision = 1.4f;

                    // 弱点色。色フィルタ(Tキー)が弱点と一致している間は動きが鈍る。
                    // 弱点はtype_enumから機械的に決めるので、今後追加される型にも自動で割り当たる。
                    bool erWeakColor = (playerColorFilter != 0 && playerColorFilter == (((int)enemy.type % 3) + 1));
                    if (erWeakColor) erMass *= 0.7f;

                    // 早送り中の攻撃間隔の詰まり具合。これまでSTATIONARY/PATROL_SHOOTERにしか無かったが、
                    // 「雑に早送りで駆け抜けるとリスクが上がる」というルールは全型に効いてよい。
                    float erFfAtk = isFastForward ? (edef ? edef->fastForwardAttackMult : 2.2f) : 1.0f;

                    // Feature: 編集リアクションのJSON宣言 — アセット側で宣言された効果を解釈し、
                    // 共通の倍率に合流させる。宣言が無ければ何も変わらない（既存アセットは全て空）。
                    // これにより、C++に固有実装を書かなくてもJSONだけで反応を足せる。
                    DeclaredEffects erDecl = GetEnemyDeclaredEffects(enemy, edef, er);
                    if (erDecl.any) {
                        erMass   *= erDecl.mulMoveSpeed;
                        erVision *= erDecl.mulSightRange;
                        erFfAtk  *= erDecl.mulAttackRate;
                        if (erDecl.destroy) {
                            enemy.hp = 0;
                            enemy.isActive = false;
                        }
                    }

                    // AI分岐が傾きを自前で解釈したか。しなかった型には分岐の後で
                    // 「傾けた向きへ坂道のように滑る」という汎用の反応を掛ける。
                    bool erTiltHandled = false;
                    (void)erFfAtk; (void)erVision; // 型によっては使わないための抑制
                    // =========================================================================

                    // 敵タイプ(EnemyType)ごとに行動ロジックを分岐させる
                    switch (enemy.type) {
                        case ENEMY_PATROL: {
                            // シンプルAI：patrolLeft ～ patrolRight の範囲でパトロール
                            //
                            // 編集リアクション：
                            //  ・拡大     → 重く鈍くなる代わりに破城槌になり、進路上の壊せるブロックを押し割る。
                            //  ・縮小     → 軽く速くなり、崖でも止まらずに落ちていく（落として下へ運べる）。
                            //  ・傾ける   → 転がりモード。巡回範囲を無視して傾けた向きへ進み続ける。
                            //  ・向き反転 → 巡回範囲の端を待たずその場で折り返す。
                            //  ・速度を上げすぎる → 慣性で折り返しに失敗し、自分から崖へ飛び出す（共通の後処理）。
                            //  ・暗転     → 足元が見えなくなり、崖でも止まらなくなる。
                            float enemySpeed = editorPlayerCaps.baseSpeed * ets * (edef ? edef->moveSpeed : 0.4f) * erMass;
                            // 巡回範囲が未設定なら ±200px で初期化
                            if (enemy.patrolLeft < 0 && enemy.patrolRight < 0) {
                                enemy.patrolLeft = std::max<float>(0.0f, enemy.x - 200.0f);
                                enemy.patrolRight = enemy.x + 200.0f;
                            }

                            // 敵の行動改良 — 地形を見て反転する。
                            // 従来のPATROLは patrolLeft/patrolRight という「座標の壁」だけで折り返していたため、
                            // 足場から平然とはみ出して空中を歩き、壁があってもめり込んだまま押し続けていた。
                            // 見た目が壊れているだけでなく「どこで折り返すか」がプレイヤーから読めず、
                            // 踏み台にしたり避けたりする計画が立てられないので、
                            // WALKER/CHASERと同じタイル判定（進行方向の足元に床があるか／目の前に壁があるか）を入れて
                            // 崖ぎわと壁ぎわで必ず反転させる。座標指定の巡回範囲はその外枠として今まで通り効く。
                            auto& mpP = stages[currentStageIdx].map;
                            auto probeTileP = [&](float px, float py) -> bool {
                                int tCol = (int)(px / TILE_SIZE);
                                int tRow = (int)(py / TILE_SIZE);
                                if (mpP.empty() || tRow < 0 || tRow >= (int)mpP.size() || tCol < 0 || tCol >= (int)mpP[0].size()) return false;
                                int tid = mpP[tRow][tCol];
                                return (tid >= 0 && tid < (int)tileDefs.size() && tileDefs[tid].isCollidable);
                            };
                            float bodyW = (float)enemy.hitboxWidth * enemy.scale;
                            float bodyH = (float)enemy.hitboxHeight * enemy.scale;
                            // 進行方向のわずかに先を見る。向きは 0=右 / 1=左。
                            float aheadX = (enemy.direction == 0) ? (enemy.x + bodyW + 4.0f) : (enemy.x - 4.0f);
                            bool groundAheadP = probeTileP(aheadX, enemy.y + bodyH + 4.0f); // 足元に床が続いているか
                            bool wallAheadP = probeTileP(aheadX, enemy.y + bodyH * 0.5f);   // 胴の高さに壁があるか
                            // 空中に浮いている個体（足場の無い所に配置された敵）まで崖判定で止めてしまうと
                            // その場で永久に向きを変え続けるだけになるので、今まさに接地している時だけ崖を見る。
                            bool groundedNowP = (std::abs(enemy.vy) < 1.0f) && probeTileP(enemy.x + bodyW * 0.5f, enemy.y + bodyH + 4.0f);

                            // 縮小されている、または画面が暗いときは崖を見なくなる。
                            // 小さくすれば軽くて速い代わりに落ちる、という取引にすることで
                            // 「落として下の階層へ運ぶ」「谷へ落として排除する」という使い道が生まれる。
                            bool seesCliff = !(er.shrunk || g_screenFx.brightness < 0.6f);
                            bool turnByTerrain = wallAheadP || (seesCliff && groundedNowP && !groundAheadP);

                            // 拡大されたまるびは破城槌になり、進路上の壊せるブロックを押し割って進む。
                            // ドッスンの着地時と同じく、その場で相手のisActiveを落とすだけなので
                            // ギミック配列の要素数は変わらず、選択中ポインタを壊す心配がない。
                            if (er.enlarged) {
                                float ramX = (enemy.direction == 0) ? (enemy.x + bodyW) : (enemy.x - 8.0f);
                                for (auto& gimRam : gimmicks) {
                                    if (gimRam.type != GIMMICK_BREAKABLE_BLOCK || !gimRam.isActive) continue;
                                    if (ramX >= gimRam.x && ramX <= gimRam.x + gimRam.spriteWidth &&
                                        enemy.y + bodyH > gimRam.y && enemy.y < gimRam.y + gimRam.spriteHeight) {
                                        gimRam.isActive = false;
                                        const GimmickDef* gdefRam = FindGimmickDef(gimRam.assetId);
                                        if (gdefRam) SoundManager::Get().PlaySe(gdefRam->seActivate);
                                        wallAheadP = false; // 割った直後は壁扱いを解いて進ませる
                                        turnByTerrain = false;
                                    }
                                }
                            }

                            if (er.tilted) {
                                // 転がりモード：まるびは丸いので、傾けられるとその向きへ転がり続ける。
                                // 巡回範囲という「見えない壁」を無視するため、坂道のように使って
                                // スイッチの上や谷の向こうまで運べる。壁に当たったときだけ跳ね返る。
                                erTiltHandled = true;
                                float rollDir = (er.tilt > 0.0f) ? 1.0f : -1.0f;
                                enemy.direction = (rollDir > 0.0f) ? 0 : 1;
                                float rollSpeed = enemySpeed * (1.0f + std::abs(sinf(er.tilt)) * 1.5f);
                                enemy.vx = rollDir * rollSpeed;
                                if (wallAheadP) {
                                    // 壁では跳ね返る（見た目の回転も逆向きになるので挙動が読める）
                                    enemy.angle = enemy.editBaseAngle - er.tilt;
                                    enemy.vx = 0.0f;
                                }
                            } else if (enemy.direction == 0) {
                                enemy.vx = enemySpeed;
                                if (turnByTerrain || (enemy.patrolRight > 0 && enemy.x + enemy.vx >= enemy.patrolRight)) {
                                    enemy.direction = 1;
                                    enemy.vx = 0.0f; // 反転したフレームは踏み込まず、次フレームから逆向きに歩き出す
                                }
                            } else {
                                enemy.vx = -enemySpeed;
                                if (turnByTerrain || (enemy.x + enemy.vx <= enemy.patrolLeft)) {
                                    enemy.direction = 0;
                                    enemy.vx = 0.0f;
                                }
                            }
                            break;
                        }
                        case ENEMY_JUMPER: {
                            // ジャンプAI：定期的にジャンプ（タイルマップ＋足場両方で着地チェック）
                            //
                            // 編集リアクション：
                            //  ・拡大     → 重いぶん跳ぶ間隔が伸びるが、一度の跳躍が高くなる。
                            //  ・縮小     → 小刻みに跳ね続ける。
                            //  ・傾ける   → 真上ではなく傾けた向きへ斜めに跳ぶ（跳ぶ先を誘導できる）。
                            //  ・向き反転 → 着地のたびに左右交互へ跳ぶ移動体になる。
                            //  ・速度0    → 跳ばなくなり、その場の台になる。
                            float jumpInterval = (edef ? edef->actionInterval : 90.0f) * er.scaleRatio;
                            float jumpPowerMult = (edef ? edef->jumpPowerMult : 0.7f) * er.scaleRatio;
                            enemy.customTimer += ets;
                            enemy.vx = 0.0f;

                            // 着地チェック: vyが0かつ少し下にタイル/足場があるか
                            bool isGrounded = false;
                            {
                                float testY = enemy.y + 2.0f; // 1px下
                                float testVY = 1.0f;
                                bool tileGround = CheckGridCollisionY(enemy.x, testY, testVY,
                                    enemy.hitboxWidth, enemy.scale, enemy.hitboxHeight,
                                    stages[currentStageIdx].map, tileDefs);
                                bool platGround = CheckPlatformCollision(enemy.x, testY, testVY,
                                    enemy.hitboxWidth, enemy.hitboxHeight, enemy.scale, platforms, gimmicks);
                                isGrounded = (tileGround || platGround) && (std::abs(enemy.vy) < 1.0f);
                            }

                            if (isGrounded && enemy.customTimer >= jumpInterval) {
                                float jumpMag = (float)(-editorPlayerCaps.baseJumpPower) * jumpPowerMult;
                                enemy.vy = -jumpMag;
                                if (er.tilted) {
                                    // 傾けられた向きへ斜めに跳ぶ（真上を0度として時計回り）
                                    erTiltHandled = true;
                                    enemy.vy = -cosf(er.tilt) * jumpMag;
                                    enemy.vx =  sinf(er.tilt) * jumpMag;
                                } else if (er.flipped) {
                                    // 向きを反転させると、着地のたびに左右交互へ跳ぶ移動体になる
                                    enemy.vx = (enemy.direction == 0 ? 1.0f : -1.0f) * jumpMag * 0.5f;
                                    enemy.direction = (enemy.direction == 0) ? 1 : 0;
                                }
                                enemy.customTimer = 0.0f;
                            }
                            break;
                        }
                        case ENEMY_STATIONARY: {
                            // 固定AI：全方位からプレイヤーへ正確に狙い撃つ。発射直前は画面を軽くズームインして溜めを予告する。
                            // 編集機能連動：早送り中は攻撃間隔がfastForwardAttackMult倍で詰まり、雑に駆け抜けるとリスクが上がる
                            // （一時停止はCanUpdate経由で既にタイマーごと止まるため、丁寧に近づけば安全という差別化になる）。
                            //
                            // 編集リアクション：
                            //  ・傾ける   → 照準ロック。追尾をやめ、傾けた向きへ固定で撃ち続ける。
                            //  ・拡大／縮小 → 弾の大きさと速さが変わる。
                            //  ・向き反転 → 味方撃ちになり、撃った弾が他の敵に当たる。
                            //  ・暗転     → 溜めの予告ズームが弱まり、いつ撃たれるか読みにくくなる。
                            float shootInterval = edef ? edef->actionInterval : 120.0f;
                            float projSpeed = (edef ? edef->projectileSpeed : 0.6f) * erMass;
                            float ffAtkMultS = edef ? edef->fastForwardAttackMult : 2.2f;
                            enemy.vx = 0.0f;

                            float pCenterXs = player.x + (player.width * player.scale) / 2.0f;
                            float pCenterYs = player.y + (player.height * player.scale) / 2.0f;
                            float eCenterXs = enemy.x + (enemy.hitboxWidth * enemy.scale) / 2.0f;
                            float eCenterYs = enemy.y + (enemy.hitboxHeight * enemy.scale) / 2.0f;
                            enemy.direction = (pCenterXs < eCenterXs) ? 1 : 0;

                            enemy.customTimer += ets * (isFastForward ? ffAtkMultS : 1.0f);

                            // 発射直前（残り20フレーム以内）は溜めエフェクトとして画面をわずかにズームインさせる
                            float remainingS = shootInterval - enemy.customTimer;
                            float distToPlayerS = sqrtf((pCenterXs - eCenterXs) * (pCenterXs - eCenterXs) + (pCenterYs - eCenterYs) * (pCenterYs - eCenterYs));
                            if (remainingS <= 20.0f && remainingS > 0.0f && distToPlayerS < 500.0f) {
                                float chargeTs = 1.0f - (remainingS / 20.0f);
                                Screen_SetZoom(1.0f + chargeTs * 0.06f);
                            }

                            if (enemy.customTimer >= shootInterval) {
                                float dxS = pCenterXs - eCenterXs;
                                float dyS = pCenterYs - eCenterYs;
                                float distS = sqrtf(dxS * dxS + dyS * dyS);
                                if (distS < 1.0f) distS = 1.0f;
                                float dirXs = dxS / distS;
                                float dirYs = dyS / distS;
                                if (er.tilted) {
                                    // 照準ロック：真上を0度として傾けた向きへ固定射撃する
                                    erTiltHandled = true;
                                    dirXs = sinf(er.tilt);
                                    dirYs = -cosf(er.tilt);
                                }
                                for (int i = 0; i < MAX_BULLETS; i++) {
                                    if (!bullets[i].isActive) {
                                        bullets[i].isActive = true;
                                        bullets[i].x = eCenterXs;
                                        bullets[i].y = eCenterYs;
                                        float spdS = BULLET_SPEED * projSpeed;
                                        bullets[i].vx = dirXs * spdS;
                                        bullets[i].vy = dirYs * spdS;
                                        bullets[i].scale = er.scaleRatio;
                                        bullets[i].isPlayerOwned = er.flipped; // 反転で味方撃ちになる
                                        bullets[i].isRewinding = false;
                                        bullets[i].history.clear();
                                        break;
                                    }
                                }
                                enemy.customTimer = 0.0f;
                            }
                            break;
                        }
                        case ENEMY_PATROL_SHOOTER: {
                            // 索敵＆射撃AI。索敵後は全方位からプレイヤーへ正確に狙い撃つ（従来は水平のみ）。
                            // 編集機能連動：早送り中は攻撃間隔がfastForwardAttackMult倍で詰まる（STATIONARYと同様の設計）。
                            //
                            // 編集リアクション：
                            //  ・拡大     → 索敵範囲が広がる代わりに歩きが鈍る。
                            //  ・縮小     → 索敵が狭くなり、すり抜けやすくなる。
                            //  ・傾ける   → 照準が固定され、プレイヤーではなく傾けた向きへ撃つ。
                            //  ・向き反転 → 見つけても撃たずに逃げ出す。
                            //  ・暗転     → 索敵範囲が縮み、目の前まで気付かれない。
                            float detectX = (edef ? edef->triggerRange : 300.0f) * er.scaleRatio * erVision;
                            float detectY = (edef ? edef->detectionRangeY : 100.0f) * er.scaleRatio * erVision;
                            float patrolSpd = (edef ? edef->moveSpeed : 0.5f) * erMass;
                            float cooldown = edef ? edef->cooldownTime : 60.0f;
                            float projSpeed = edef ? edef->projectileSpeed : 0.5f;
                            float ffAtkMultP = edef ? edef->fastForwardAttackMult : 2.2f;
                            float pCenterX = player.x + (player.width * player.scale) / 2.0f;
                            float pCenterY = player.y + (player.height * player.scale) / 2.0f;
                            float eCenterX = enemy.x + (enemy.hitboxWidth * enemy.scale) / 2.0f;
                            float eCenterY = enemy.y + (enemy.hitboxHeight * enemy.scale) / 2.0f;
                            float distX = std::abs(pCenterX - eCenterX);
                            float distY = std::abs(pCenterY - eCenterY);

                            // aiState: 0=パトロール中, 1=攻撃(索敵成功)中
                            if (distX <= detectX && distY <= detectY) enemy.aiState = 1; // 索敵範囲内 → 攻撃状態へ
                            else enemy.aiState = 0; // 索敵範囲外 → パトロール状態へ

                            if (enemy.aiState == 0) {
                                if (enemy.direction == 1) {
                                    enemy.vx = -WALK_SPEED * ets * patrolSpd;
                                    if (enemy.x <= enemy.patrolLeft) enemy.direction = 0;
                                } else {
                                    enemy.vx = WALK_SPEED * ets * patrolSpd;
                                    if (enemy.x >= enemy.patrolRight) enemy.direction = 1;
                                }
                            } else if (enemy.aiState == 1) {
                                enemy.vx = 0.0f;
                                enemy.direction = (player.x < enemy.x) ? 1 : 0;
                                if (enemy.customTimer > 0) enemy.customTimer -= 1.0f * ets * (isFastForward ? ffAtkMultP : 1.0f);

                                // 発射直前（残り20フレーム以内）は溜めエフェクトとして画面をわずかにズームインさせる
                                if (enemy.customTimer <= 20.0f && enemy.customTimer > 0.0f && distX < 500.0f && distY < 500.0f) {
                                    float chargeTp = 1.0f - (enemy.customTimer / 20.0f);
                                    Screen_SetZoom(1.0f + chargeTp * 0.06f);
                                }

                                if (enemy.customTimer <= 0) {
                                    float dxP = pCenterX - eCenterX;
                                    float dyP = pCenterY - eCenterY;
                                    float distP = sqrtf(dxP * dxP + dyP * dyP);
                                    if (distP < 1.0f) distP = 1.0f;
                                    for (int i = 0; i < MAX_BULLETS; i++) {
                                        if (!bullets[i].isActive) {
                                            bullets[i].isActive = true;
                                            bullets[i].x = eCenterX;
                                            bullets[i].y = eCenterY;
                                            float spdP = BULLET_SPEED * projSpeed;
                                            bullets[i].vx = dxP / distP * spdP;
                                            bullets[i].vy = dyP / distP * spdP;
                                            bullets[i].isPlayerOwned = false;
                                            bullets[i].isRewinding = false;
                                            bullets[i].history.clear();
                                            break;
                                        }
                                    }
                                    enemy.customTimer = cooldown;
                                }
                            }
                            break;
                        }

                        // ===== ここから追加タイプ =====
                        case ENEMY_WALKER: {
                            // 歩いてくる：索敵範囲内にいる間だけプレイヤー方向へ歩く。崖のふちで止まる。
                            //
                            // 編集リアクション：
                            //  ・拡大     → 重く鈍いが、進路上の壊せるブロックを押し割る。
                            //  ・縮小     → 軽く速くなり、崖でも止まらず落ちていく。
                            //  ・向き反転 → プレイヤーから逃げる方向へ歩く。
                            //  ・暗転     → 索敵範囲が縮む。
                            float triggerRangeWk = (edef ? edef->triggerRange : 300.0f) * erVision;
                            float speed = editorPlayerCaps.baseSpeed * ets * (edef ? edef->moveSpeed : 0.35f) * erMass;
                            if (std::abs(player.x - enemy.x) < triggerRangeWk) {
                                enemy.direction = (player.x < enemy.x) ? 1 : 0;
                                if (er.flipped) enemy.direction = (enemy.direction == 1) ? 0 : 1; // 反転で逃走
                                enemy.vx = (enemy.direction == 1) ? -speed : speed;

                                // 拡大されたWALKERは壊せるブロックを押し割って進む
                                if (er.enlarged) {
                                    float ramXw = enemy.x + (enemy.direction == 1 ? -8.0f : (float)enemy.hitboxWidth * enemy.scale);
                                    for (auto& gimW : gimmicks) {
                                        if (gimW.type != GIMMICK_BREAKABLE_BLOCK || !gimW.isActive) continue;
                                        if (ramXw >= gimW.x && ramXw <= gimW.x + gimW.spriteWidth &&
                                            enemy.y + (float)enemy.hitboxHeight * enemy.scale > gimW.y &&
                                            enemy.y < gimW.y + gimW.spriteHeight) {
                                            gimW.isActive = false;
                                            const GimmickDef* gdefW = FindGimmickDef(gimW.assetId);
                                            if (gdefW) SoundManager::Get().PlaySe(gdefW->seActivate);
                                        }
                                    }
                                }

                                float aheadX = enemy.x + (enemy.direction == 1 ? -8.0f : (float)enemy.hitboxWidth * enemy.scale + 8.0f);
                                float footY = enemy.y + (float)enemy.hitboxHeight * enemy.scale + 4.0f;
                                auto& mpW = stages[currentStageIdx].map;
                                int tColW = (int)(aheadX / TILE_SIZE);
                                int tRowW = (int)(footY / TILE_SIZE);
                                bool groundAhead = false;
                                if (!mpW.empty() && tRowW >= 0 && tRowW < (int)mpW.size() && tColW >= 0 && tColW < (int)mpW[0].size()) {
                                    int tid = mpW[tRowW][tColW];
                                    groundAhead = (tid >= 0 && tid < (int)tileDefs.size() && tileDefs[tid].isCollidable);
                                }
                                // 縮小されている、または暗くて足元が見えないときは崖で止まらず落ちる
                                if (!groundAhead && !er.shrunk && g_screenFx.brightness >= 0.6f) enemy.vx = 0.0f;
                            } else {
                                enemy.vx = 0.0f;
                            }
                            break;
                        }
                        case ENEMY_CHASER: {
                            // 追っかけてくる：索敵範囲内にいる間だけWALKERに加え、壁に当たると自動でジャンプする
                            //
                            // 編集リアクション：
                            //  ・拡大     → 重くて壁を越えられなくなる（段差で足止めできる）。
                            //  ・縮小     → 軽くなって跳躍力が上がり、より高い壁も越えてくる。
                            //  ・傾ける   → 追跡の狙いが横にずれ、まっすぐ来なくなる。
                            //  ・向き反転 → 逃げに転じる。
                            //  ・暗転     → 索敵範囲が縮む。
                            float triggerRangeCh = (edef ? edef->triggerRange : 300.0f) * erVision;
                            float speed = editorPlayerCaps.baseSpeed * ets * (edef ? edef->moveSpeed : 0.55f) * erMass;
                            float jumpPowerMult = (edef ? edef->jumpPowerMult : 0.8f) * erMass;
                            if (std::abs(player.x - enemy.x) < triggerRangeCh) {
                                enemy.direction = (player.x < enemy.x) ? 1 : 0;
                                if (er.flipped) enemy.direction = (enemy.direction == 1) ? 0 : 1; // 反転で逃走
                                enemy.vx = (enemy.direction == 1) ? -speed : speed;

                                float aheadX = enemy.x + (enemy.direction == 1 ? -4.0f : (float)enemy.hitboxWidth * enemy.scale + 4.0f);
                                float midY = enemy.y + (float)enemy.hitboxHeight * enemy.scale * 0.5f;
                                auto& mpC = stages[currentStageIdx].map;
                                int tColC = (int)(aheadX / TILE_SIZE);
                                int tRowC = (int)(midY / TILE_SIZE);
                                bool wallAhead = false;
                                if (!mpC.empty() && tRowC >= 0 && tRowC < (int)mpC.size() && tColC >= 0 && tColC < (int)mpC[0].size()) {
                                    int tid = mpC[tRowC][tColC];
                                    wallAhead = (tid >= 0 && tid < (int)tileDefs.size() && tileDefs[tid].isCollidable);
                                }
                                // 拡大されて重くなった個体は壁を越えられない（段差で足止めできる）
                                if (wallAhead && std::abs(enemy.vy) < 1.0f && !er.enlarged) {
                                    enemy.vy = (float)editorPlayerCaps.baseJumpPower * jumpPowerMult;
                                }
                            } else {
                                enemy.vx = 0.0f;
                            }
                            break;
                        }
                        case ENEMY_DASH_CHARGER: {
                            // 突進：射程内に入ると溜め→高速直進→クールダウン。auxState: 0待機/1溜め/2突進/3クールダウン
                            //
                            // 編集リアクション：
                            //  ・傾ける   → 突進の向きを固定できる。壊せるブロックへ突っ込ませる誘導に使える。
                            //  ・拡大     → 溜めが長く突進も長い、重い破城槌になる（壊せるブロックを粉砕する）。
                            //  ・縮小     → 溜めが短くなり、短距離を何度も突進してくる。
                            //  ・向き反転 → プレイヤーとは逆方向へ突進する。
                            //  ・暗転     → 突進を始める間合いが近くなる。
                            float triggerRange = (edef ? edef->triggerRange : 260.0f) * erVision;
                            float chargeTime = (edef ? edef->chargeTime : 30.0f) * er.scaleRatio;
                            float dashSpeedMult = (edef ? edef->dashSpeedMult : 1.5f) * erMass;
                            float dashDuration = (edef ? edef->dashDuration : 40.0f) * er.scaleRatio;
                            float cooldownTime = edef ? edef->cooldownTime : 70.0f;
                            float distXd = player.x - enemy.x;
                            if (enemy.auxState == 0) {
                                enemy.vx = 0.0f;
                                if (std::abs(distXd) < triggerRange) {
                                    enemy.auxState = 1; enemy.customTimer = chargeTime;
                                    enemy.direction = (distXd < 0) ? 1 : 0;
                                    if (er.flipped) enemy.direction = (enemy.direction == 1) ? 0 : 1; // 反転で逆へ突進
                                }
                            } else if (enemy.auxState == 1) {
                                enemy.vx = 0.0f;
                                // 溜め中はプレイヤーの回り込みに対応できるよう、突進が始まる瞬間まで方向を追従させ続ける
                                enemy.direction = (distXd < 0) ? 1 : 0;
                                enemy.customTimer -= ets;
                                if (enemy.customTimer <= 0) { enemy.auxState = 2; enemy.customTimer = dashDuration; }
                            } else if (enemy.auxState == 2) {
                                float dashSpeed = DASH_SPEED * ets * dashSpeedMult;
                                enemy.vx = (enemy.direction == 1) ? -dashSpeed : dashSpeed;
                                if (er.tilted) {
                                    // 傾けられた向きへ突進する（斜め上へ跳ぶような突進もできる）
                                    erTiltHandled = true;
                                    enemy.vx = sinf(er.tilt) * dashSpeed;
                                    enemy.vy = -cosf(er.tilt) * dashSpeed * 0.5f;
                                }
                                // 拡大された突進は破城槌になり、当たった壊せるブロックを粉砕する
                                if (er.enlarged) {
                                    float ramXd = enemy.x + (enemy.direction == 1 ? -8.0f : (float)enemy.hitboxWidth * enemy.scale);
                                    for (auto& gimD : gimmicks) {
                                        if (gimD.type != GIMMICK_BREAKABLE_BLOCK || !gimD.isActive) continue;
                                        if (ramXd >= gimD.x && ramXd <= gimD.x + gimD.spriteWidth &&
                                            enemy.y + (float)enemy.hitboxHeight * enemy.scale > gimD.y &&
                                            enemy.y < gimD.y + gimD.spriteHeight) {
                                            gimD.isActive = false;
                                            const GimmickDef* gdefD = FindGimmickDef(gimD.assetId);
                                            if (gdefD) SoundManager::Get().PlaySe(gdefD->seActivate);
                                        }
                                    }
                                }
                                enemy.customTimer -= ets;
                                if (enemy.customTimer <= 0) { enemy.auxState = 3; enemy.customTimer = cooldownTime; }
                            } else {
                                enemy.vx = 0.0f;
                                enemy.customTimer -= ets;
                                if (enemy.customTimer <= 0) enemy.auxState = 0;
                            }
                            break;
                        }
                        case ENEMY_FALLER: {
                            // 待機(0)：静止 → プレイヤーが真下を通ると落下(1、前半は落下予兆の溜め・後半は実落下) → 着地後クールダウン(2) → 元の高さへ復帰
                            //
                            // 編集リアクション：
                            //  ・傾ける   → 落下角度をそのまま指定できる（向き反転による斜め落下の上位互換）。
                            //  ・拡大     → 着地の衝撃波が大きくなる。壊せるブロックをまとめて割れる。
                            //  ・縮小     → 衝撃波を起こせなくなり、ただの安全な台になる（EnemyIsStandable参照）。
                            //  ・速度を下げる → ゆっくり落ちるので、乗って運んでもらう昇降機として使える。
                            //  ・移動     → 復帰先も一緒に移動する（動かしたのに元の位置へ帰るのでは意味がないため）。
                            //  ・早送り   → 既存どおり左右にジッターして直下が読みにくくなる。
                            float triggerWidth = (edef ? edef->triggerRange : 24.0f) * erVision;
                            float fallDelay = edef ? edef->fallDelay : 10.0f;
                            float cooldownTime = edef ? edef->cooldownTime : 120.0f;
                            // 拡大すると衝撃波の範囲も比例して広がる
                            float shockwaveRadiusF = (edef ? edef->shockwaveRadius : 60.0f) * er.scaleRatio;
                            float ffJitter = edef ? edef->fastForwardJitter : 30.0f;
                            float diagonalSpeedF = edef ? edef->diagonalFallSpeed : 2.5f;
                            // スポーン（＝ステージ配置）時点のX/Y/向きを一度だけ記録しておく。
                            // これが「元の場所」＝復帰先の基準になる（配置位置そのものをそのまま復帰先にする）。
                            // 編集機能連動：向きはauxF1に記録し、以後プレイヤーが「方向反転」編集ツールで
                            // enemy.directionを変えたかどうかを毎フレーム比較できるようにする。
                            if (!enemy.auxFlag) {
                                enemy.auxF1 = (float)enemy.direction;
                                enemy.auxF2 = enemy.y;
                                enemy.auxF3 = enemy.x;
                                enemy.auxFlag = true;
                            }
                            // 移動編集で持ち上げられたら、復帰先(auxF2/auxF3)も一緒に動かす。
                            // これをしないと「せっかく安全な場所へどかしたのに、次の周期で元の位置へ戻ってくる」
                            // という、動かした意味が消える挙動になる。
                            if (er.moved) {
                                enemy.auxF3 += er.movedX;
                                enemy.auxF2 += er.movedY;
                                enemy.editBaseX = enemy.x;
                                enemy.editBaseY = enemy.y;
                                enemy.editDirtyMask &= ~(unsigned int)EDIT_DIRTY_POS;
                            }
                            // 落下方向。傾け編集があればそれを優先し、無ければ従来どおり
                            // 「向き反転されたか」で左右どちらかへ斜めに落ちる。
                            bool aimedSideways = ((int)enemy.auxF1) != enemy.direction;
                            if (er.tilted) erTiltHandled = true;
                            if (enemy.auxState == 0) {
                                enemy.vx = 0.0f; enemy.vy = 0.0f;
                                if (std::abs(player.x - enemy.x) < triggerWidth && player.y > enemy.y) {
                                    enemy.auxState = 1;
                                    enemy.customTimer = fallDelay;
                                }
                            } else if (enemy.auxState == 1) {
                                if (enemy.customTimer > 0) {
                                    // 落下予兆（溜め）フェーズ：描画側で影を成長させて見せる
                                    enemy.vx = 0.0f;
                                    enemy.customTimer -= ets;
                                } else {
                                    // 実落下フェーズ。編集機能連動：
                                    // ・「方向反転」ツールでスポーン時から向きを変えられていたら、その向きへ斜めに落ちる
                                    //   （プレイヤーが狙いを付けて誘導し、真下から外れた場所のギミックを起動できるようにする）
                                    // ・早送り中は左右にジッターして直下を読みにくくする
                                    //   （スローモーション中はets自体が小さくなるため、自然に予兆がゆっくり見えて見切りやすくなる）
                                    float diagVx = aimedSideways ? ((enemy.direction == 0 ? 1.0f : -1.0f) * diagonalSpeedF * ets) : 0.0f;
                                    // 傾けられている場合は角度どおりの斜め落下にする（真上を0度として時計回り）
                                    if (er.tilted) diagVx = sinf(er.tilt) * diagonalSpeedF * 2.0f * ets;
                                    float jitterVx = isFastForward ? sinf(enemy.y * 0.15f) * ffJitter * ets * 0.1f : 0.0f;
                                    enemy.vx = diagVx + jitterVx;

                                    // 着地判定 —
                                    // 【重要】ここは以前 `std::abs(enemy.vy) < 0.5f`（＝速度がほぼ0なら着地とみなす）だったが、
                                    // これは通常速度では絶対に成立しない条件だった。
                                    //   ・重力は毎フレーム enemy.vy += GRAVITY(0.5f) * ets で加算される
                                    //   ・着地すると CheckGridCollisionY が enemy.vy = 0 に落とす
                                    // つまり地面に乗っている間、このAI分岐に来た時点の vy は毎フレームぴったり 0.5 になり、
                                    // 「0.5 < 0.5」は偽。結果として着地が一度も検出されず、
                                    // ドッスンは落下状態のまま地面に座り込み、クールダウンにも復帰処理にも進まなかった。
                                    // （着地ショックウェイブと壊せるブロックの破壊も同じ理由で一度も発動していなかった。
                                    //   スローモーション中だけ ets < 1 になり、偶然 0.5 未満になって動いていた。）
                                    //
                                    // 速度を見るのをやめ、JUMPER/CUSTOM_SCRIPTと同じ「1〜2px下に地面があるか」を
                                    // 直接調べる接地プローブへ置き換える。こちらは時間スケールに一切依存しない。
                                    // CheckGridCollisionY / CheckPlatformCollision は座標と速度を参照渡しで書き換えるため、
                                    // 必ずコピーを渡して本体の x / y / vy を汚さないようにする
                                    // （とくに x は斜め落下の着地点＝復帰の起点になるので触られると困る）。
                                    bool landedOnGround = false;
                                    if (enemy.vy >= 0.0f) { // 上昇中に「着地」しないようにガードする
                                        float probeX = enemy.x;
                                        float probeY = enemy.y + 2.0f;
                                        float probeVY = 1.0f;
                                        bool tileGround = CheckGridCollisionY(probeX, probeY, probeVY,
                                            enemy.hitboxWidth, enemy.scale, enemy.hitboxHeight,
                                            stages[currentStageIdx].map, tileDefs);
                                        bool platGround = CheckPlatformCollision(probeX, probeY, probeVY,
                                            enemy.hitboxWidth, enemy.hitboxHeight, enemy.scale, platforms, gimmicks);
                                        landedOnGround = (tileGround || platGround);
                                    }
                                    if (landedOnGround) {
                                        // 着地の瞬間：踏みつけだけでなく着地地点周辺にもショックウェイブ判定を発生させる
                                        float ew_scaledF = (float)enemy.hitboxWidth * enemy.scale;
                                        float eh_scaledF = (float)enemy.hitboxHeight * enemy.scale;
                                        float pw_scaledF = (float)player.width * player.scale;
                                        float ph_scaledF = (float)player.height * player.scale;
                                        float ecxF = enemy.x + ew_scaledF / 2.0f;
                                        float ecyF = enemy.y + eh_scaledF;
                                        float pcxF = player.x + pw_scaledF / 2.0f;
                                        float pcyF = player.y + ph_scaledF / 2.0f;
                                        float ddxF = pcxF - ecxF, ddyF = pcyF - ecyF;
                                        float distF = sqrtf(ddxF * ddxF + ddyF * ddyF);
                                        // 縮小されたドッスンは衝撃波を起こせない（乗れる安全な台として振る舞う）
                                        if (!er.shrunk && distF <= shockwaveRadiusF && !isPlayerRewinding && player.invulnTimer <= 0.0f) {
                                            player.hp--;
                                            player.invulnTimer = 60.0f;
                                            float knockDirF = (distF > 0.01f) ? (ddxF / distF) : ((pcxF < ecxF) ? -1.0f : 1.0f);
                                            player.vx = knockDirF * 6.0f;
                                            player.vy = -4.0f;
                                            if (player.hp <= 0) currentScene = RESULT_GAMEOVER;
                                        }
                                        // 着地地点周辺の壊せるブロックも一緒に破壊する（斜め誘導で狙って割れるようにするギミック連携）
                                        for (auto& gimF : gimmicks) {
                                            if (er.shrunk) break; // 縮小中は何も壊せない
                                            if (gimF.type == GIMMICK_BREAKABLE_BLOCK && gimF.isActive) {
                                                float bgcx = gimF.x + gimF.spriteWidth / 2.0f;
                                                float bgcy = gimF.y + gimF.spriteHeight / 2.0f;
                                                float bgdx = bgcx - ecxF, bgdy = bgcy - ecyF;
                                                if (sqrtf(bgdx * bgdx + bgdy * bgdy) <= shockwaveRadiusF) {
                                                    gimF.isActive = false;
                                                    const GimmickDef* gdefFaller = FindGimmickDef(gimF.assetId);
                                                    if (gdefFaller) SoundManager::Get().PlaySe(gdefFaller->seActivate);
                                                }
                                            }
                                        }
                                        enemy.auxState = 2;
                                        enemy.customTimer = cooldownTime;
                                    }
                                }
                            } else {
                                enemy.vx = 0.0f; enemy.vy = 0.0f;
                                enemy.customTimer -= ets;
                                if (enemy.customTimer <= 0) {
                                    // 敵の行動改良 — 元の場所（＝配置位置）へ「せり上がって」戻る。
                                    //
                                    // 従来はクールダウンが明けた瞬間に enemy.x/y へ配置座標を代入する完全な瞬間移動だった。
                                    // そのため「落ちてくる→消える→いきなり天井に居る」という見え方になり、
                                    // プレイヤーは真下を通れる安全な時間がどれだけ残っているのかを一切読めなかった。
                                    // riseSpeed を与えると毎フレーム riseSpeed[px] ずつ配置座標へ近づくので、
                                    // 「まだ戻りきっていない今のうちに真下を走り抜ける」という攻略が成立する。
                                    //
                                    // 座標を直接動かして共通の物理更新を通さないのは、従来の瞬間移動と同じ理由。
                                    // 斜め落下で天井の下へ潜り込んだ個体が、戻る途中で天井に引っ掛かって
                                    // 二度と待機状態へ復帰できなくなるのを防ぐ。
                                    float riseSpeedF = edef ? edef->riseSpeed : 0.0f;
                                    if (riseSpeedF <= 0.0f) {
                                        // 従来どおりの瞬間復帰（riseSpeed未設定の既存の敵定義はこちらを通る）
                                        enemy.x = enemy.auxF3;
                                        enemy.y = enemy.auxF2;
                                        enemy.auxState = 0;
                                    } else {
                                        float stepF = riseSpeedF * ets;
                                        // XとYそれぞれ、残り距離がstep以下になったらぴったり合わせる（行き過ぎ防止）
                                        float dxHome = enemy.auxF3 - enemy.x;
                                        float dyHome = enemy.auxF2 - enemy.y;
                                        if (std::abs(dxHome) <= stepF) enemy.x = enemy.auxF3;
                                        else                           enemy.x += (dxHome > 0.0f ? stepF : -stepF);
                                        if (std::abs(dyHome) <= stepF) enemy.y = enemy.auxF2;
                                        else                           enemy.y += (dyHome > 0.0f ? stepF : -stepF);
                                        // XもYも定位置に着いたら、また落下を待つ待機状態へ戻す
                                        if (enemy.x == enemy.auxF3 && enemy.y == enemy.auxF2) enemy.auxState = 0;
                                    }
                                }
                            }
                            break;
                        }
                        case ENEMY_SPREAD_SHOOTER: {
                            // 拡散弾：一定間隔で複数方向へ同時に射撃する。
                            //
                            // 敵の行動改良 — radialFire を立てると「正面へのファン」ではなく
                            // 「360度への全方位ばらまき」になり、さらに spreadRotationStep の分だけ
                            // 斉射ごとに発射角度がずれていくので、弾が渦を描いて広がる。
                            // 従来の正面ファンは、敵から離れた側へ歩いているだけで永久に安全＝
                            // 撃たれていること自体に意味が無かった。全方位＋角度ずらしにすると
                            // 「弾と弾の隙間がどこに来るか」を読んで抜ける遊びになり、一時停止やスローとも噛み合う。
                            // radialFire=false のままなら従来と完全に同じ挙動になる。
                            //
                            // 編集リアクション：
                            //  ・傾ける   → 弾幕全体の発射角がずれる。弾と弾の隙間の位置を任意に回せる。
                            //  ・拡大     → 弾数が増えて密になる（無理に増やしすぎないよう上限を設ける）。
                            //  ・縮小     → 弾数が減り、隙間が広がる。縮めて抜ける、が正攻法になる。
                            //  ・向き反転 → 渦の回転方向が逆になる。
                            //  ・速度     → 斉射間隔と渦の回り方が変わる（etsが効くので自動）。
                            float shootInterval = edef ? edef->actionInterval : 150.0f;
                            float spreadAngle = edef ? edef->spreadAngle : 0.35f;
                            int spreadCount = (edef && edef->spreadCount > 0) ? edef->spreadCount : 3;
                            // 弾プール(MAX_BULLETS)は敵もプレイヤーも共有している。
                            // 拡大しすぎた1体に使い切られるとプレイヤーが撃てなくなるので必ず上限を設ける。
                            spreadCount = (int)(spreadCount * er.scaleRatio + 0.5f);
                            if (spreadCount < 2)  spreadCount = 2;
                            if (spreadCount > 12) spreadCount = 12;
                            float projSpeed = edef ? edef->projectileSpeed : 0.5f;
                            bool radialFire = (edef && edef->radialFire);
                            float rotStep = edef ? edef->spreadRotationStep : 0.0f;
                            if (er.flipped) rotStep = -rotStep; // 向きを反転すると渦が逆回りになる
                            enemy.vx = 0.0f;
                            enemy.direction = (player.x < enemy.x) ? 1 : 0;
                            enemy.customTimer += ets * erFfAtk;
                            if (enemy.customTimer >= shootInterval) {
                                float baseDir = (enemy.direction == 0) ? 1.0f : -1.0f;
                                // auxF1に「これまでの累積回転量」を貯めておく。斉射のたびにrotStep分だけ回る。
                                // 2πを超えたら折り返し、長時間プレイしても値が発散しないようにする。
                                // 傾けたぶんを渦の累積回転に足し込む（auxF1の自動回転は上書きせず合成する）
                                if (er.tilted) erTiltHandled = true;
                                float spin = enemy.auxF1 + er.tilt;
                                float ecxSp = enemy.x + (float)enemy.hitboxWidth * enemy.scale * 0.5f;
                                float ecySp = enemy.y + (float)enemy.hitboxHeight * enemy.scale * 0.5f;
                                for (int a = 0; a < spreadCount; a++) {
                                    // 全方位モード：0～2πをspreadCount等分し、そこにspinを加算した向きへ撃つ。
                                    // 正面ファンモード：-spreadAngle ～ +spreadAngle を等間隔に割り振る（従来どおり）。
                                    float angle = radialFire
                                        ? (spin + 6.2831853f * ((float)a / (float)spreadCount))
                                        : ((spreadCount > 1)
                                            ? (-spreadAngle + (2.0f * spreadAngle) * ((float)a / (float)(spreadCount - 1)) + spin)
                                            : spin);
                                    for (int i = 0; i < MAX_BULLETS; i++) {
                                        if (!bullets[i].isActive) {
                                            bullets[i].isActive = true;
                                            float spd = BULLET_SPEED * projSpeed;
                                            if (radialFire) {
                                                // 全方位なので発射位置は敵の中心。左右どちらを向いているかは関係ない
                                                bullets[i].x = ecxSp;
                                                bullets[i].y = ecySp;
                                                bullets[i].vx = spd * cosf(angle);
                                                bullets[i].vy = spd * sinf(angle);
                                            } else {
                                                bullets[i].x = enemy.x + (enemy.direction == 0 ? (float)enemy.hitboxWidth * enemy.scale : -10.0f);
                                                bullets[i].y = enemy.y + (float)enemy.hitboxHeight * enemy.scale / 4.0f;
                                                bullets[i].vx = baseDir * spd * cosf(angle);
                                                bullets[i].vy = spd * sinf(angle);
                                            }
                                            bullets[i].scale = er.scaleRatio; // 拡大した本体の弾は大きい
                                            bullets[i].isPlayerOwned = false;
                                            bullets[i].isRewinding = false;
                                            bullets[i].history.clear();
                                            break;
                                        }
                                    }
                                }
                                enemy.auxF1 = enemy.auxF1 + rotStep;
                                if (enemy.auxF1 > 6.2831853f) enemy.auxF1 -= 6.2831853f;
                                if (enemy.auxF1 < -6.2831853f) enemy.auxF1 += 6.2831853f;
                                enemy.customTimer = 0.0f;
                            }
                            break;
                        }
                        case ENEMY_AIMED_SHOOTER: {
                            // 照準弾：発射時のプレイヤー位置へ正確に狙い撃つ。
                            //
                            // 編集リアクション：
                            //  ・傾ける → 「照準ロック」。プレイヤーを追うのをやめ、傾けた向きへ固定で撃ち続ける。
                            //             スイッチや壊せるブロックを敵に撃たせる、という使い方ができる。
                            //  ・拡大   → 大きく遅い弾。空中で追い越せるので足場感覚で扱える。
                            //  ・縮小   → 小さく速い弾。避けにくいが、当たり判定も小さい。
                            //  ・向き反転 → 弾の所属がプレイヤー側に変わり、他の敵に当たる「味方撃ち砲台」になる。
                            //  ・早送り → 発射間隔が詰まる。
                            //  ・暗転   → 狙いがぶれる（プレイヤーの位置を正確に掴めなくなる）。
                            float shootInterval = edef ? edef->actionInterval : 130.0f;
                            float projSpeed = (edef ? edef->projectileSpeed : 0.55f) * erMass;
                            enemy.vx = 0.0f;
                            enemy.customTimer += ets * erFfAtk;
                            if (enemy.customTimer >= shootInterval) {
                                float pCenterX = player.x + (player.width * player.scale) / 2.0f;
                                float pCenterY = player.y + (player.height * player.scale) / 2.0f;
                                float eCenterX = enemy.x + (enemy.hitboxWidth * enemy.scale) / 2.0f;
                                float eCenterY = enemy.y + (enemy.hitboxHeight * enemy.scale) / 2.0f;

                                // 暗転中は狙いがぶれる。明るさが下がるほどブレ幅が大きくなる
                                if (fxCurBright < 0.6f) {
                                    float jitter = (1.0f - fxCurBright) * 160.0f;
                                    pCenterX += (float)(rand() % 201 - 100) / 100.0f * jitter;
                                    pCenterY += (float)(rand() % 201 - 100) / 100.0f * jitter;
                                }

                                float dxA = pCenterX - eCenterX;
                                float dyA = pCenterY - eCenterY;
                                float distA = sqrtf(dxA * dxA + dyA * dyA);
                                if (distA < 1.0f) distA = 1.0f;
                                float dirXa = dxA / distA;
                                float dirYa = dyA / distA;

                                if (er.tilted) {
                                    // 照準ロック：追尾をやめ、真上(-Y)を0度として傾けた向きへ撃つ。
                                    // 傾き0で真上、時計回りに倒すほど右下へ向く直感的な対応にしてある。
                                    erTiltHandled = true;
                                    dirXa = sinf(er.tilt);
                                    dirYa = -cosf(er.tilt);
                                }
                                enemy.direction = (dirXa < 0.0f) ? 1 : 0;

                                for (int i = 0; i < MAX_BULLETS; i++) {
                                    if (!bullets[i].isActive) {
                                        bullets[i].isActive = true;
                                        bullets[i].x = eCenterX;
                                        bullets[i].y = eCenterY;
                                        float spd = BULLET_SPEED * projSpeed;
                                        bullets[i].vx = dirXa * spd;
                                        bullets[i].vy = dirYa * spd;
                                        bullets[i].scale = er.scaleRatio; // 拡大した砲台の弾は大きい
                                        // 向きを反転させた砲台は「味方撃ち」になり、撃った弾が他の敵に当たる
                                        bullets[i].isPlayerOwned = er.flipped;
                                        bullets[i].isRewinding = false;
                                        bullets[i].history.clear();
                                        break;
                                    }
                                }
                                enemy.customTimer = 0.0f;
                            }
                            break;
                        }
                        case ENEMY_FLOATER: {
                            // 浮遊敵：重力を打ち消してサインカーブで浮遊しながらゆっくり接近
                            //
                            // 編集リアクション：
                            //  ・拡大     → 重くなって浮力を失い、そのまま落ちる。落として足場やスイッチに使える。
                            //  ・縮小     → 軽くなって振幅も追尾速度も上がり、素早く絡んでくる。
                            //  ・傾ける   → 浮遊の軸が傾き、縦揺れが斜め・横揺れに変わる。
                            //  ・向き反転 → 追尾をやめて逆に逃げていく。
                            //  ・移動     → 浮遊の中心高度も一緒に移動する。
                            float amplitude = (edef ? edef->floatAmplitude : 40.0f) * erMass;
                            float frequency = edef ? edef->floatFrequency : 0.05f;
                            if (enemy.auxF2 == 0.0f) enemy.auxF2 = enemy.y;
                            // 移動編集で持ち上げられたら浮遊中心も追従させる（元の高度へ戻ろうとしないように）
                            if (er.moved) {
                                enemy.auxF2 += er.movedY;
                                enemy.editBaseX = enemy.x;
                                enemy.editBaseY = enemy.y;
                                enemy.editDirtyMask &= ~(unsigned int)EDIT_DIRTY_POS;
                            }
                            enemy.customTimer += ets;

                            // 敵の行動改良 — 浮遊の中心高度(auxF2)をプレイヤーの高さへゆっくり寄せる。
                            // 従来のFLOATERは配置された高さを一切変えず横方向にしか寄って来なかったため、
                            // 段差を1つ上るか下りるかするだけで完全に無力化でき、
                            // 「空を飛んでいる敵」なのに地形だけで完封できてしまう噛み合わなさがあった。
                            // 高度も追ってくるようにすると上下に逃げるだけでは振り切れなくなり、
                            // 代わりに一時停止やスローで止めて抜けるという編集ツール側の解答が要る相手になる。
                            // verticalTrackSpeed が 0（＝未設定の既存定義）なら高度は据え置きで従来どおり。
                            float vTrack = (edef ? edef->verticalTrackSpeed : 0.0f) * erMass;
                            float triggerRangeVt = (edef ? edef->triggerRange : 300.0f) * erVision;
                            if (vTrack > 0.0f && std::abs(player.x - enemy.x) < triggerRangeVt) {
                                // プレイヤーの中心の高さを目標にする（足元ではなく胴を狙うので接触しやすい）
                                float targetCenterY = player.y + (float)player.height * player.scale * 0.5f;
                                float homeCenterY = enemy.auxF2 + (float)enemy.hitboxHeight * enemy.scale * 0.5f;
                                float dyTrack = targetCenterY - homeCenterY;
                                float stepTrack = vTrack * ets;
                                if (std::abs(dyTrack) <= stepTrack) enemy.auxF2 += dyTrack;
                                else                                enemy.auxF2 += (dyTrack > 0.0f ? stepTrack : -stepTrack);
                                if (enemy.auxF2 < 0.0f) enemy.auxF2 = 0.0f; // 画面外の上空へ抜けていかないよう下限を張る
                            }

                            // 傾けられていると浮遊の軸そのものが倒れ、縦揺れが斜め・横揺れに変わる
                            float swing = sinf(enemy.customTimer * frequency) * amplitude;
                            float swingX = 0.0f;
                            float swingY = swing;
                            if (er.tilted) {
                                erTiltHandled = true;
                                swingX = swing * sinf(er.tilt);
                                swingY = swing * cosf(er.tilt);
                            }
                            float desiredY = enemy.auxF2 + swingY;

                            // 拡大されると浮力を失い、重力に任せて落下する。
                            // 落としてスイッチを踏ませたり、下の足場を作ったりする使い道が生まれる。
                            // （vyへ代入しない＝この分岐の手前で加算された重力がそのまま残る）
                            if (!er.enlarged) {
                                // enemy.yを直接上書きすると天井・床とのY方向衝突判定を素通りしてしまうため、
                                // 目標Yとの差分をvyとして渡し、他の敵と同じ経路（共通の物理更新）でY衝突判定を通す
                                enemy.vy = desiredY - enemy.y;
                            }

                            float triggerRangeFl = (edef ? edef->triggerRange : 300.0f) * erVision;
                            if (std::abs(player.x - enemy.x) < triggerRangeFl) {
                                float speed = editorPlayerCaps.baseSpeed * ets * (edef ? edef->moveSpeed : 0.2f) * erMass;
                                enemy.direction = (player.x < enemy.x) ? 1 : 0;
                                // 向きを反転させると追尾をやめて逃げに転じる
                                if (er.flipped) enemy.direction = (enemy.direction == 1) ? 0 : 1;
                                enemy.vx = (enemy.direction == 1) ? -speed : speed;
                                enemy.vx += swingX * frequency; // 傾けた軸ぶんの横揺れ
                            } else {
                                enemy.vx = swingX * frequency;
                            }
                            break;
                        }
                        case ENEMY_TELEPORTER: {
                            // テレポーター：一定間隔でプレイヤー付近へ瞬間移動する
                            //
                            // 編集リアクション：
                            //  ・拡大     → 出現距離が伸び、遠くにしか現れなくなる（間合いを稼げる）。
                            //  ・縮小     → 距離が縮み、真横に貼り付いてくる。
                            //  ・傾ける   → 出現する方角が固定される（回り込まれる側を選べる）。
                            //  ・向き反転 → プレイヤーから離れる側へワープするようになる。
                            //  ・速度0    → ワープそのものが止まる。
                            float interval = edef ? edef->actionInterval : 180.0f;
                            float rangeMin = (edef ? edef->teleportRangeMin : 120.0f) * er.scaleRatio;
                            float rangeMax = (edef ? edef->teleportRangeMax : 220.0f) * er.scaleRatio;
                            float rangeSpan = std::max<float>(0.0f, rangeMax - rangeMin);
                            enemy.vx = 0.0f;
                            enemy.customTimer += ets;
                            if (enemy.customTimer >= interval) {
                                float sideX = (rand() % 2 == 0) ? 1.0f : -1.0f;
                                // 向きを反転させると、プレイヤーから遠ざかる側にしか現れなくなる
                                if (er.flipped) sideX = (player.x < enemy.x) ? 1.0f : -1.0f;
                                float offsetX = rangeMin + (float)(rand() % (int)std::max<float>(1.0f, rangeSpan));
                                // Y座標もプレイヤー基準で再抽選する（元々Xしか動かず地形にめり込む原因になっていた）
                                float sideY = (rand() % 2 == 0) ? 1.0f : -1.0f;
                                float offsetY = (float)(rand() % (int)std::max<float>(1.0f, rangeSpan * 0.5f));
                                float destX = player.x + sideX * offsetX;
                                float destY = player.y + sideY * offsetY;
                                if (er.tilted) {
                                    // 傾けられている場合、出現方角を角度どおりに固定する（真上を0度として時計回り）
                                    erTiltHandled = true;
                                    float radius = rangeMin + rangeSpan * 0.5f;
                                    destX = player.x + sinf(er.tilt) * radius;
                                    destY = player.y - cosf(er.tilt) * radius;
                                }
                                if (destX < 0.0f) destX = 0.0f;
                                if (destY < 0.0f) destY = 0.0f;

                                // 着地先が壁の中でないか簡易チェック。壁の中なら今回は見送り、customTimerを
                                // リセットしないので次フレームに自動で再抽選される
                                auto& mpTp = stages[currentStageIdx].map;
                                int tColTp = (int)((destX + (float)enemy.hitboxWidth * enemy.scale * 0.5f) / TILE_SIZE);
                                int tRowTp = (int)((destY + (float)enemy.hitboxHeight * enemy.scale * 0.5f) / TILE_SIZE);
                                bool destSolid = false;
                                if (!mpTp.empty() && tRowTp >= 0 && tRowTp < (int)mpTp.size() && tColTp >= 0 && tColTp < (int)mpTp[0].size()) {
                                    int tid = mpTp[tRowTp][tColTp];
                                    destSolid = (tid >= 0 && tid < (int)tileDefs.size() && tileDefs[tid].isCollidable);
                                }
                                if (!destSolid) {
                                    enemy.x = destX;
                                    enemy.y = destY;
                                    enemy.customTimer = 0.0f;
                                }
                            }
                            break;
                        }
                        case ENEMY_SHRINKER: {
                            // 分裂もどき：索敵範囲内では通常時ゆっくり接近。被弾で縮小・高速化した後(auxFlag)は素早く接近する。
                            //
                            // 編集リアクション：
                            //  ・縮小     → 「もう縮む余地が無い」状態にできる。復活の権利を先に使い切らせるので、
                            //               次の一撃で確実に倒せるようになる（撃つ前に縮めるのが正攻法）。
                            //  ・拡大     → 復活の権利が戻る代わりに動きが鈍る。
                            //  ・向き反転 → 追跡ではなく逃走に転じる。
                            //  ・暗転     → 索敵範囲が狭まり、近づくまで気付かれない。
                            float triggerRangeSk = (edef ? edef->triggerRange : 300.0f) * erVision;
                            float normalMult = edef ? edef->moveSpeed : 0.35f;
                            float enragedMult = edef ? edef->enragedMoveSpeed : 0.9f;

                            // プレイヤーが自分の手で縮めたなら、それは「もう分裂した後」と同じ状態とみなす。
                            // 拡大された場合は逆に、復活の権利を取り戻す。
                            if (er.shrunk && !enemy.auxFlag)  enemy.auxFlag = true;
                            if (er.enlarged && enemy.auxFlag) enemy.auxFlag = false;

                            float mult = enemy.auxFlag ? enragedMult : normalMult;
                            float speed = editorPlayerCaps.baseSpeed * ets * mult * erMass;
                            if (std::abs(player.x - enemy.x) < triggerRangeSk) {
                                enemy.direction = (player.x < enemy.x) ? 1 : 0;
                                if (er.flipped) enemy.direction = (enemy.direction == 1) ? 0 : 1; // 反転で逃走
                                enemy.vx = (enemy.direction == 1) ? -speed : speed;
                            } else {
                                enemy.vx = 0.0f;
                            }
                            break;
                        }
                        case ENEMY_SHIELD: {
                            // シールド：索敵範囲内ではゆっくり接近しつつ、一定間隔で無敵状態(auxFlag)を切り替える（無敵中は描画側で発光）
                            //
                            // 編集リアクション：
                            //  ・縮小     → 無敵の持続が短くなる。「撃つ前に縮める」のが正攻法になる。
                            //  ・拡大     → 無敵が長引き、ほとんど撃ち込む隙が無くなる。
                            //  ・向き反転 → 無敵のON/OFFが入れ替わる（今まさに無敵なら即座に解ける）。
                            //  ・速度0    → 切り替えが止まり、そのときの状態で固定される。
                            //  ・暗転     → 索敵範囲が縮む。
                            float triggerRangeSh = (edef ? edef->triggerRange : 300.0f) * erVision;
                            float offDuration = (edef ? edef->shieldOffDuration : 150.0f) * erMass;
                            float onDuration = (edef ? edef->shieldOnDuration : 90.0f) * er.scaleRatio;
                            float speed = editorPlayerCaps.baseSpeed * ets * (edef ? edef->moveSpeed : 0.3f) * erMass;
                            if (std::abs(player.x - enemy.x) < triggerRangeSh) {
                                enemy.direction = (player.x < enemy.x) ? 1 : 0;
                                enemy.vx = (enemy.direction == 1) ? -speed : speed;
                            } else {
                                enemy.vx = 0.0f;
                            }

                            enemy.customTimer += ets;
                            if (!enemy.auxFlag && enemy.customTimer >= offDuration) { enemy.auxFlag = true; enemy.customTimer = 0.0f; }
                            else if (enemy.auxFlag && enemy.customTimer >= onDuration) { enemy.auxFlag = false; enemy.customTimer = 0.0f; }
                            // 向きを反転させると無敵のON/OFFが入れ替わる。
                            // auxFlagそのものは書き換えず表示上の状態だけ反転させると描画と食い違うので、
                            // ここで実体ごと入れ替えてしまう（反転しっぱなしなら常に位相が逆になるだけ）。
                            if (er.flipped) {
                                enemy.auxFlag = !enemy.auxFlag;
                                enemy.direction = enemy.editBaseDirection; // 反転は一度だけ効かせる
                            }
                            break;
                        }
                        case ENEMY_MIMIC_GHOST: {
                            // 幽霊敵：プレイヤーの巻き戻し履歴を遅延再生して過去の動きをなぞる。
                            //
                            // 編集リアクション：
                            //  ・拡大     → 遅延が伸びる＝より古い過去をなぞるので、後ろへ離れていく。
                            //  ・縮小     → 遅延が縮む＝今に近い動きをなぞるので、真後ろまで迫ってくる。
                            //  ・傾ける   → 再生位置が左右にずれる。軌跡を横にずらして避けられる。
                            //  ・速度     → 遅延の消化速度が変わる（速くすると追いつかれる）。
                            //  ・向き反転 → 軌跡を左右反転して再生する（鏡像の自分が来る）。
                            //  ・一時停止 → この型はignorePauseでグローバルの一時停止が効かないが、
                            //               名指しの個別一時停止だけは効く（＝止めたいなら指定しろ、という差別化）。
                            //  ・色フィルタ → 色を付けている間だけ実体化して弾が当たる（通常は無敵）。
                            //  ・明転     → 光で消える（無害化）／暗転 → 遅延が縮んで強化される。
                            enemy.vx = 0.0f; enemy.vy = 0.0f;
                            // 基準の遅延フレーム数はauxF1に一度だけ入れておき、
                            // 編集による倍率はそこから毎フレーム導出する（初期化ガードを壊さないため）。
                            if (enemy.auxF1 <= 0.0f) enemy.auxF1 = edef ? edef->mimicDelayFrames : 90.0f;

                            float delayF = enemy.auxF1 * er.scaleRatio;        // 大きいほど古い過去をなぞる
                            if (er.speedRatio > 0.01f) delayF /= er.speedRatio; // 速くすると今に追いついてくる
                            if (fxCurBright < 0.6f)    delayF *= 0.6f;          // 暗いと距離を詰めてくる
                            if (delayF < 1.0f) delayF = 1.0f;
                            if (delayF > (float)(MAX_HISTORY_FRAMES - 1)) delayF = (float)(MAX_HISTORY_FRAMES - 1);

                            size_t delay = (size_t)delayF;
                            if (player.history.size() > delay) {
                                const PlayerState& past = player.history[player.history.size() - 1 - delay];
                                float gx2 = past.x;
                                if (er.flipped) {
                                    // 軌跡を左右反転して再生する。配置位置を鏡の面として折り返すので、
                                    // 「自分の動きの鏡像」が反対側から迫ってくる。
                                    gx2 = enemy.editBaseX * 2.0f - past.x;
                                }
                                if (er.tilted) {
                                    // 傾けたぶん、再生位置を横へずらす
                                    erTiltHandled = true;
                                    gx2 += sinf(er.tilt) * 120.0f;
                                }
                                enemy.x = gx2;
                                enemy.y = past.y;
                            }
                            break;
                        }
                        case ENEMY_SIZE_SHIFTER: {
                            // 大きさが変わる敵：索敵範囲内で接近しつつ、scaleが周期的に変化（当たり判定も連動）
                            //
                            // 編集リアクション：
                            //  ・拡大／縮小 → 「サイズロック」。この敵は普段AIが毎フレームscaleを書き換えているが、
                            //                 プレイヤーが一度でも大きさを編集したら所有権を手放し、その大きさで固定される。
                            //                 小さく固定して隙間を通す／大きく固定して足場にする、という両方の使い道がある。
                            //                 Reset All（editDirtyMaskのクリア）で周期変化が再開する。
                            //  ・傾ける   → 大きさの周期がずれる（膨らむ瞬間をずらせる）。
                            //  ・速度0    → 周期が止まり、そのときの大きさのまま固定される。
                            //  ・向き反転 → 大きくなる／小さくなるの位相が逆になる。
                            float triggerRangeSz = (edef ? edef->triggerRange : 300.0f) * erVision;
                            float amplitude = edef ? edef->sizeAmplitude : 0.5f;
                            float frequency = edef ? edef->sizeFrequency : 0.04f;
                            float minSc = edef ? edef->minScale : 0.4f;
                            float speed = editorPlayerCaps.baseSpeed * ets * (edef ? edef->moveSpeed : 0.25f) * erMass;
                            if (std::abs(player.x - enemy.x) < triggerRangeSz) {
                                enemy.direction = (player.x < enemy.x) ? 1 : 0;
                                enemy.vx = (enemy.direction == 1) ? -speed : speed;
                            } else {
                                enemy.vx = 0.0f;
                            }

                            enemy.customTimer += ets;
                            // 一度でも大きさを編集されたら、AIはscaleに触らない（＝プレイヤーが固定できる）。
                            // この型はAIが毎フレームscaleを上書きするため、
                            // 「配置時との差」では編集の有無を判別できず、明示的なダーティビットが必要になる。
                            if (!(enemy.editDirtyMask & EDIT_DIRTY_SCALE)) {
                                float phase = enemy.customTimer * frequency + er.tilt;
                                if (er.flipped) phase = -phase; // 向き反転で膨張と収縮が入れ替わる
                                if (er.tilted) erTiltHandled = true;
                                enemy.scale = 1.0f + sinf(phase) * amplitude;
                                if (enemy.scale < minSc) enemy.scale = minSc;
                            }
                            break;
                        }
                        case ENEMY_TEMPO_WARPER: {
                            // 速さ操作敵：索敵範囲内で接近しつつ、speedScaleが周期的に激しく変化し接近速度が乱れる。
                            //
                            // 編集リアクション：
                            //  ・速度を編集する → 「テンポロック」。この敵はAIが毎フレームspeedScaleを書き換えるので
                            //                     普段は速度が読めないが、一度でも速度を編集すると所有権を手放し、
                            //                     指定した速さで固定される。Reset Allで自動変動が再開する。
                            //  ・拡大     → 速さの振れ幅が大きくなり、さらに読みにくくなる。
                            //  ・傾ける   → 変動の位相がずれる。
                            //  ・向き反転 → 追跡ではなく逃走に転じる。
                            float triggerRangeTw = (edef ? edef->triggerRange : 300.0f) * erVision;
                            float frequency = edef ? edef->tempoFrequency : 0.05f;
                            float tempoMin = edef ? edef->tempoMin : 0.3f;
                            float tempoMax = edef ? edef->tempoMax : 1.6f;
                            // 拡大すると振れ幅が広がる（速いときはより速く、遅いときはより遅く）
                            float tempoMid = (tempoMin + tempoMax) * 0.5f;
                            tempoMin = tempoMid + (tempoMin - tempoMid) * er.scaleRatio;
                            tempoMax = tempoMid + (tempoMax - tempoMid) * er.scaleRatio;
                            enemy.customTimer += ets;
                            // 速度を一度でも編集されたら、AIはspeedScaleに触らない（プレイヤーがテンポを固定できる）。
                            // ジオメトリのサイズロックと同じ考え方で、AIが値を握っている型に手綱を渡す仕組み。
                            if (!(enemy.editDirtyMask & EDIT_DIRTY_SPEED)) {
                                if (er.tilted) erTiltHandled = true;
                                enemy.speedScale = tempoMin + (tempoMax - tempoMin)
                                                 * (0.5f + 0.5f * sinf(enemy.customTimer * frequency + er.tilt));
                            }
                            float speed = editorPlayerCaps.baseSpeed * ets * (edef ? edef->moveSpeed : 0.4f) * erMass;
                            if (std::abs(player.x - enemy.x) < triggerRangeTw) {
                                enemy.direction = (player.x < enemy.x) ? 1 : 0;
                                if (er.flipped) enemy.direction = (enemy.direction == 1) ? 0 : 1;
                                enemy.vx = (enemy.direction == 1) ? -speed : speed;
                            } else {
                                enemy.vx = 0.0f;
                            }
                            break;
                        }
                        case ENEMY_BRIGHTNESS_PHANTOM: {
                            // 明るさ操作敵：索敵範囲内で接近しつつ、射程内で画面を暗転させる。
                            //
                            // 編集リアクション：
                            //  ・明転(Cキー) → プレイヤー自身が画面を明るくしている間は暗転させる力を打ち消せる。
                            //                  「相手の妨害を、同じ編集ツールで正面から打ち消す」関係になっている。
                            //  ・拡大     → 効果範囲が広がる。
                            //  ・縮小     → 範囲が狭まり、近づかない限り無害になる。
                            //  ・向き反転 → 暗転ではなく逆に明転させてくる（明暗ロック足場のあるステージで意味が変わる）。
                            //  ・速度0    → 追ってこなくなるが、暗転そのものは続く。
                            float triggerRangeBp = (edef ? edef->triggerRange : 300.0f) * erVision;
                            float range = (edef ? edef->effectRange : 320.0f) * er.scaleRatio;
                            float brightnessMin = edef ? edef->brightnessMin : 0.35f;
                            float speed = editorPlayerCaps.baseSpeed * ets * (edef ? edef->moveSpeed : 0.3f) * erMass;
                            if (std::abs(player.x - enemy.x) < triggerRangeBp) {
                                enemy.direction = (player.x < enemy.x) ? 1 : 0;
                                enemy.vx = (enemy.direction == 1) ? -speed : speed;
                            } else {
                                enemy.vx = 0.0f;
                            }

                            float distB = std::abs(player.x - enemy.x);
                            // プレイヤーが明転(C)を使っている間は打ち消される。
                            // cHeldThisFrameは後段で判定されるため、1フレーム前の結果であるfxCurBrightを見る。
                            bool counteredBp = (fxCurBright > 1.3f);
                            if (distB < range && !counteredBp) {
                                float t = 1.0f - (distB / range);
                                if (er.flipped) {
                                    // 反転すると暗転ではなく明転させてくる
                                    Screen_SetBrightness(1.0f + t * 0.7f);
                                } else {
                                    Screen_SetBrightness(1.0f - t * (1.0f - brightnessMin));
                                }
                            }
                            break;
                        }
                        case ENEMY_COLOR_SHIFTER: {
                            // 色調整敵：索敵範囲内で接近しつつ、射程内で画面の色調をシフトさせる。
                            //
                            // 編集リアクション：
                            //  ・色フィルタ(Tキー) → この敵が押し付けてくる色と同じ色を自分でも掛けている間は
                            //                        打ち消せる。色ロック足場のあるステージでは
                            //                        「足場を出すための色」を敵に奪われるので、これが解答になる。
                            //  ・拡大／縮小 → 効果範囲が伸び縮みする。
                            //  ・傾ける   → 押し付けてくる色が赤→緑→青と切り替わる。
                            //  ・向き反転 → 補色（押し付ける色と残す色が逆）になる。
                            float triggerRangeCs = (edef ? edef->triggerRange : 300.0f) * erVision;
                            float range = (edef ? edef->effectRange : 320.0f) * er.scaleRatio;
                            float tintStrength = edef ? edef->tintStrength : 0.6f;
                            float speed = editorPlayerCaps.baseSpeed * ets * (edef ? edef->moveSpeed : 0.3f) * erMass;
                            if (std::abs(player.x - enemy.x) < triggerRangeCs) {
                                enemy.direction = (player.x < enemy.x) ? 1 : 0;
                                enemy.vx = (enemy.direction == 1) ? -speed : speed;
                            } else {
                                enemy.vx = 0.0f;
                            }

                            float distCol = std::abs(player.x - enemy.x);
                            // 傾けると押し付けてくる色が変わる（1=赤 / 2=緑 / 3=青）
                            int shiftColor = 1 + (((er.tiltSteps % 3) + 3) % 3);
                            if (er.tilted) erTiltHandled = true;
                            // 同じ色を自分でも掛けていれば打ち消せる
                            bool counteredCs = (playerColorFilter == shiftColor);
                            if (distCol < range && !counteredCs) {
                                float t = 1.0f - (distCol / range);
                                float dim = 1.0f - t * tintStrength;
                                float keep = 1.0f;
                                if (er.flipped) { float tmp = dim; dim = keep; keep = tmp; } // 反転で補色になる
                                if (shiftColor == 1)      Screen_SetTint(keep, dim, dim);
                                else if (shiftColor == 2) Screen_SetTint(dim, keep, dim);
                                else                      Screen_SetTint(dim, dim, keep);
                            }
                            break;
                        }
                        case ENEMY_ZOOM_DISRUPTOR: {
                            // ズーム撹乱敵：射程内で画面ズームを周期的に揺さぶる。
                            //
                            // 編集リアクション：
                            //  ・速度0    → 揺さぶりのタイマーごと止まり、画面が落ち着く（最も素直な対処法）。
                            //  ・拡大     → 揺れ幅が大きくなり、距離感が掴めなくなる。
                            //  ・縮小     → 揺れ幅も範囲も小さくなる。
                            //  ・傾ける   → 揺れの周期がずれる。
                            //  ・向き反転 → ズームインではなくズームアウト方向に歪ませてくる。
                            float range = (edef ? edef->effectRange : 280.0f) * er.scaleRatio;
                            float amplitude = (edef ? edef->zoomAmplitude : 0.25f) * er.scaleRatio;
                            float frequency = edef ? edef->zoomFrequency : 0.08f;
                            enemy.vx = 0.0f;
                            enemy.customTimer += ets;
                            float distZ = std::abs(player.x - enemy.x);
                            if (distZ < range) {
                                if (er.tilted) erTiltHandled = true;
                                float wave = sinf(enemy.customTimer * frequency + er.tilt) * amplitude;
                                if (er.flipped) wave = -wave; // 反転でズームアウト側へ歪む
                                Screen_SetZoom(1.0f + wave);
                            }
                            break;
                        }
                        case ENEMY_POUNCER: {
                            // 飛びかかり敵（いもむし用）。
                            // 「高速で地面を這って迫る」→「溜め」→「放物線を描いて飛びかかる」→「着地硬直」を繰り返す。
                            // DASH_CHARGERの4状態機械と同じ骨格だが、水平突進ではなくジャンプなので
                            // 一度だけ初速を与えたあとは重力任せにし、着地するまで速度に触らない
                            // （毎フレーム上書きすると放物線にならず、直線的な飛行になってしまう）。
                            // auxState: 0=追跡 / 1=溜め / 2=飛行中 / 3=着地硬直
                            //
                            // 編集リアクション：
                            //  ・傾ける   → 飛びかかりの射出角そのものを指定できる。真上や後方へ撃ち出して
                            //               「敵を砲弾として使う」ことができる。
                            //  ・拡大     → 重いぶん溜めが長くなるが、跳躍力と飛距離が伸びる。
                            //  ・縮小     → 溜めが短くなり、小刻みに連続で飛びかかってくる。
                            //  ・向き反転 → 溜め中に反転させると、その向きへ飛ぶ（狙いを付け替えられる）。
                            //  ・一時停止 → 飛行中に止めると空中で固定され、そのまま足場になる。
                            //  ・暗転     → 飛びかかりの間合いに入りにくくなる。
                            float triggerRangePc = (edef ? edef->triggerRange : 200.0f) * erVision;
                            // 大きいほど溜めが長く、小さいほど短い
                            float chargeTimePc = (edef ? edef->chargeTime : 22.0f) * er.scaleRatio;
                            float jumpMultPc = (edef ? edef->jumpPowerMult : 1.15f) * er.scaleRatio;
                            float dashMultPc = (edef ? edef->dashSpeedMult : 0.9f) * er.scaleRatio;
                            float airLimitPc = edef ? edef->dashDuration : 120.0f;
                            float cooldownPc = edef ? edef->cooldownTime : 45.0f;
                            float runSpeedPc = editorPlayerCaps.baseSpeed * ets * (edef ? edef->moveSpeed : 0.7f) * erMass;
                            float distXpc = player.x - enemy.x;

                            // 進行方向の足元に床が続いているか（崖から落ちないための判定）。
                            // WALKER/PATROLと同じタイルプローブ。
                            auto& mpPc = stages[currentStageIdx].map;
                            auto probeTilePc = [&](float px, float py) -> bool {
                                int tCol = (int)(px / TILE_SIZE);
                                int tRow = (int)(py / TILE_SIZE);
                                if (mpPc.empty() || tRow < 0 || tRow >= (int)mpPc.size() || tCol < 0 || tCol >= (int)mpPc[0].size()) return false;
                                int tid = mpPc[tRow][tCol];
                                return (tid >= 0 && tid < (int)tileDefs.size() && tileDefs[tid].isCollidable);
                            };
                            float bodyWpc = (float)enemy.hitboxWidth * enemy.scale;
                            float bodyHpc = (float)enemy.hitboxHeight * enemy.scale;

                            if (enemy.auxState == 0) {
                                // --- 追跡：プレイヤーの方へ走る。崖のふちでは止まって落ちない ---
                                enemy.direction = (distXpc < 0.0f) ? 1 : 0;
                                enemy.vx = (enemy.direction == 1) ? -runSpeedPc : runSpeedPc;
                                float aheadXpc = (enemy.direction == 0) ? (enemy.x + bodyWpc + 6.0f) : (enemy.x - 6.0f);
                                if (!probeTilePc(aheadXpc, enemy.y + bodyHpc + 4.0f)) enemy.vx = 0.0f;
                                if (std::abs(distXpc) < triggerRangePc) {
                                    enemy.auxState = 1;
                                    enemy.customTimer = chargeTimePc;
                                }
                            } else if (enemy.auxState == 1) {
                                // --- 溜め：その場で止まり、飛ぶ直前まで向きだけプレイヤーへ追従させる ---
                                enemy.vx = 0.0f;
                                enemy.direction = (distXpc < 0.0f) ? 1 : 0;
                                enemy.customTimer -= ets;
                                if (enemy.customTimer <= 0.0f) {
                                    // 初速を一度だけ与える。以降は共通の重力処理が放物線を作る
                                    float launchMag = DASH_SPEED * dashMultPc;
                                    enemy.vy = (float)editorPlayerCaps.baseJumpPower * jumpMultPc;
                                    enemy.vx = (enemy.direction == 1 ? -1.0f : 1.0f) * launchMag;
                                    if (er.tilted) {
                                        // 傾けられている場合は、その角度をそのまま射出方向にする。
                                        // 真上を0度として時計回り。真上に近づけるほど高く、
                                        // 倒すほど水平に遠くへ飛ぶ、という直感どおりの対応になる。
                                        erTiltHandled = true;
                                        float mag = launchMag + (float)(-editorPlayerCaps.baseJumpPower) * jumpMultPc;
                                        enemy.vx =  sinf(er.tilt) * mag;
                                        enemy.vy = -cosf(er.tilt) * mag;
                                    }
                                    enemy.auxState = 2;
                                    enemy.customTimer = 0.0f;
                                    if (edef) SoundManager::Get().PlaySe(edef->seAttack);
                                }
                            } else if (enemy.auxState == 2) {
                                // --- 飛行中：速度には触らない。落下に転じてから接地したら着地とみなす ---
                                enemy.customTimer += ets;
                                bool landedPc = false;
                                // 飛び出した直後は足元にまだ地面があるので、上昇中(vy<0)は着地判定しない。
                                // 参照渡しで書き換えられるため、必ずコピーを渡して本体の座標を汚さない。
                                if (enemy.vy >= 0.0f && enemy.customTimer > 4.0f) {
                                    float probeXpc = enemy.x;
                                    float probeYpc = enemy.y + 2.0f;
                                    float probeVYpc = 1.0f;
                                    bool tileG = CheckGridCollisionY(probeXpc, probeYpc, probeVYpc,
                                        enemy.hitboxWidth, enemy.scale, enemy.hitboxHeight,
                                        stages[currentStageIdx].map, tileDefs);
                                    bool platG = CheckPlatformCollision(probeXpc, probeYpc, probeVYpc,
                                        enemy.hitboxWidth, enemy.hitboxHeight, enemy.scale, platforms, gimmicks);
                                    landedPc = (tileG || platG);
                                }
                                // 何かの拍子に着地を取り逃しても空中で固まらないよう、滞空時間に上限を設けておく
                                if (landedPc || enemy.customTimer >= airLimitPc) {
                                    enemy.vx = 0.0f;
                                    enemy.auxState = 3;
                                    enemy.customTimer = cooldownPc;
                                }
                            } else {
                                // --- 着地硬直：完全に停止する。ここがプレイヤーの反撃・すり抜けの窓になる ---
                                enemy.vx = 0.0f;
                                enemy.customTimer -= ets;
                                if (enemy.customTimer <= 0.0f) enemy.auxState = 0;
                            }
                            break;
                        }
                        case ENEMY_CUSTOM_SCRIPT: {
                            // Feature: Puzzle-like Behavior Scripting (M2) — EnemyDef.scriptのJSONブロックで駆動する
                            if (edef && !enemy.scriptState.started && !enemy.scriptState.finished && !enemy.scriptState.faulted) {
                                BehaviorInterpreter::Start(enemy.scriptState, edef->script, "OnSpawn");
                            }

                            ScriptActor actor;
                            actor.timeScale = ets; // 早送り/スローモーションをスクリプト駆動の敵にも反映する
                            FillScriptEditContext(actor, er); // Feature: 編集リアクション
                            actor.x = &enemy.x; actor.y = &enemy.y;
                            actor.vx = &enemy.vx; actor.vy = &enemy.vy;
                            actor.direction = &enemy.direction;
                            actor.scale = &enemy.scale;
                            actor.angle = &enemy.angle; // Feature: Composite Multi-Part Objects (Parts-M2)
                            actor.playerX = player.x; actor.playerY = player.y;

                            // 接地・進行方向の壁/足場の有無を判定（WALKER/CHASER/JUMPERと同じタイル判定を流用）
                            {
                                float testY = enemy.y + 2.0f;
                                float testVY = 1.0f;
                                bool tileGround = CheckGridCollisionY(enemy.x, testY, testVY,
                                    enemy.hitboxWidth, enemy.scale, enemy.hitboxHeight,
                                    stages[currentStageIdx].map, tileDefs);
                                bool platGround = CheckPlatformCollision(enemy.x, testY, testVY,
                                    enemy.hitboxWidth, enemy.hitboxHeight, enemy.scale, platforms, gimmicks);
                                actor.isGrounded = (tileGround || platGround) && (std::abs(enemy.vy) < 1.0f);
                            }
                            auto& mpS = stages[currentStageIdx].map;
                            auto checkAheadTile = [&](float dx) -> bool {
                                float aheadX = enemy.x + dx;
                                float footY = enemy.y + (float)enemy.hitboxHeight * enemy.scale + 4.0f;
                                int tCol = (int)(aheadX / TILE_SIZE);
                                int tRow = (int)(footY / TILE_SIZE);
                                if (!mpS.empty() && tRow >= 0 && tRow < (int)mpS.size() && tCol >= 0 && tCol < (int)mpS[0].size()) {
                                    int tid = mpS[tRow][tCol];
                                    return (tid >= 0 && tid < (int)tileDefs.size() && tileDefs[tid].isCollidable);
                                }
                                return false;
                            };
                            actor.groundAheadLeft = checkAheadTile(-8.0f);
                            actor.groundAheadRight = checkAheadTile((float)enemy.hitboxWidth * enemy.scale + 8.0f);
                            actor.wallAheadLeft = checkAheadTile(-4.0f);
                            actor.wallAheadRight = checkAheadTile((float)enemy.hitboxWidth * enemy.scale + 4.0f);

                            Enemy* enemyPtr = &enemy;
                            actor.shoot = [&bullets, enemyPtr](float angleRad, float speed, float damage) {
                                (void)damage; // Bullet構造体に damage フィールドが無いため現状1固定ダメージ（将来拡張の余地）
                                for (int i = 0; i < MAX_BULLETS; i++) {
                                    if (!bullets[i].isActive) {
                                        bullets[i].isActive = true;
                                        bullets[i].x = enemyPtr->x + (float)enemyPtr->hitboxWidth * enemyPtr->scale / 2.0f;
                                        bullets[i].y = enemyPtr->y + (float)enemyPtr->hitboxHeight * enemyPtr->scale / 2.0f;
                                        bullets[i].vx = cosf(angleRad) * speed;
                                        bullets[i].vy = sinf(angleRad) * speed;
                                        bullets[i].isPlayerOwned = false;
                                        bullets[i].isRewinding = false;
                                        bullets[i].history.clear();
                                        break;
                                    }
                                }
                            };
                            actor.playSound = [](const std::string& slot) { SoundManager::Get().PlaySe(slot); };
                            actor.visualEffect = [&](const std::string& kind, float intensity) {
                                if (kind == "brightness") Screen_SetBrightness(intensity);
                                else if (kind == "zoom") Screen_SetZoom(intensity);
                            };

                            BehaviorInterpreter::Tick(enemy.scriptState, actor);
                            break;
                        }
                    }

                    // ================= Feature: 編集リアクション（共通の後処理）=================
                    // AI分岐が決めたこのフレームの速度に、全型共通の反応を後がけする。
                    // ここでまとめて処理しておくことで、敵タイプごとのAIを1つずつ書き換えなくても、
                    // 今後追加される新しい型まで含めて必ず傾け・速度編集に反応するようになる。

                    // 傾けられた相手は、その向きへ坂道を滑るように押される。
                    // 傾きを自前で解釈する型（照準ロックや斜め落下など）には二重に掛けない。
                    if (!erTiltHandled && er.tilted) {
                        enemy.vx += sinf(er.tilt) * 0.35f * ets;
                    }

                    // 速くしすぎた相手は制御を失う。前フレームの速度を強く引きずるようになるので、
                    // 折り返しや急停止に失敗してオーバーランする。
                    // 「とりあえず速度を上げる」が万能の解にならないようにするための歯止め。
                    if (er.speedRatio >= 2.0f) {
                        enemy.vx = erPrevVx * 0.85f + enemy.vx * 0.15f;
                    }

                    // JSON宣言で NoGravity が指定されていれば、このフレームの落下をなかったことにする
                    if (erDecl.noGravity) enemy.vy = 0.0f;
                    // =========================================================================

                    // X/Y方向の移動と衝突判定を共通化
                    bool enemyGrounded = UpdatePhysicsCollisions(enemy.x, enemy.y, enemy.vx, enemy.vy, enemy.vy, 
                                                                 enemy.hitboxWidth, enemy.hitboxHeight, enemy.scale, 
                                                                 stages[currentStageIdx].map, tileDefs, gimmicks);

                    // 足場との着地衝突判定
                    CheckPlatformCollision(enemy.x, enemy.y, enemy.vy, enemy.hitboxWidth, enemy.hitboxHeight, enemy.scale, platforms, gimmicks, &enemy.ridingGimmickIndex);
                    // Feature: 編集リアクション（共通層）— 敵も止まった敵の上に乗る。
                    // プレイヤー側だけ乗れて敵はすり抜ける、という食い違いを作らないため同じ判定を通す。
                    // selfに自分を渡して、自分自身の上に着地してしまうのを防ぐ。
                    CheckFrozenEnemyPlatform(enemy.x, enemy.y, enemy.vy,
                                             enemy.hitboxWidth, enemy.hitboxHeight, enemy.scale, enemies, &enemy);

                    // 地面との衝突によりvy=0になった場合はisJumping相当をリセット
                    if (enemyGrounded) { enemy.vy = 0.0f; }

                    // マップ下端に落ちた場合も止める（フォールスルー防止）
                    float mapBottom = (float)(stages[currentStageIdx].map.size()) * TILE_SIZE;
                    if (enemy.y > mapBottom) { enemy.y = mapBottom - (float)enemy.hitboxHeight * enemy.scale; enemy.vy = 0.0f; }

                    // 敵のステージ境界制限
                    if (enemy.x < 0.0f) enemy.x = 0.0f;
                    float mapRight = (float)(stages[currentStageIdx].map.empty() ? 0 : stages[currentStageIdx].map[0].size()) * TILE_SIZE;
                    if (enemy.x > mapRight - (float)enemy.hitboxWidth * enemy.scale) {
                        enemy.x = mapRight - (float)enemy.hitboxWidth * enemy.scale;
                    }

                    // Feature: Composite Multi-Part Objects (Parts-M3)
                    // パーツは親のtype_enum(挙動タイプ)に関係なく、独立して自分のスクリプトを実行する
                    //
                    // 複合オブジェクトのパーツ追従 — AI が本体を動かし終えた「今の姿勢」を先に求めておく。
                    // SetLocalOffset系がローカル→ワールドの合成に使い、ループ後の回収でも同じ値を使う。
                    ParentPose ePartPose = MakeEnemyPose(enemy, edef);
                    if (edef) {
                        for (auto& part : enemy.parts) {
                            if (!part.isActive) continue;
                            if (!part.scriptState.started && !part.scriptState.finished && !part.scriptState.faulted) {
                                json partScript = (part.partIndex >= 0 && part.partIndex < (int)edef->parts.size())
                                    ? edef->parts[part.partIndex].script : json::array();
                                BehaviorInterpreter::Start(part.scriptState, partScript, "OnSpawn");
                            }
                            ScriptActor partActor;
                            partActor.timeScale = ets; // 早送り/スローモーションをスクリプト駆動のパーツにも反映する
                            FillScriptEditContext(partActor, er); // 親に加えられた編集をパーツにも伝える
                            partActor.x = &part.x; partActor.y = &part.y;
                            partActor.scale = &part.scale; partActor.angle = &part.angle;
                            partActor.hasParent = true;
                            partActor.parentX = enemy.x; partActor.parentY = enemy.y;
                            partActor.parentDirection = (enemy.direction == 0) ? 1.0f : -1.0f; // 0=右向き, 1=左向き（enemy.directionの規約に合わせる）
                            partActor.partIndex = part.partIndex;
                            FillScriptPartTransform(partActor, part, ePartPose); // 親の拡大・傾けをパーツの位置計算へ合成する
                            partActor.playerX = player.x; partActor.playerY = player.y;
                            PartInstance* partPtr = &part;
                            partActor.shoot = [&bullets, partPtr](float angleRad, float speed, float damage) {
                                (void)damage;
                                for (int i = 0; i < MAX_BULLETS; i++) {
                                    if (!bullets[i].isActive) {
                                        bullets[i].isActive = true;
                                        bullets[i].x = partPtr->x; bullets[i].y = partPtr->y;
                                        bullets[i].vx = cosf(angleRad) * speed;
                                        bullets[i].vy = sinf(angleRad) * speed;
                                        bullets[i].isPlayerOwned = false;
                                        bullets[i].isRewinding = false;
                                        bullets[i].history.clear();
                                        break;
                                    }
                                }
                            };
                            partActor.playSound = [](const std::string& slot) { SoundManager::Get().PlaySe(slot); };
                            partActor.visualEffect = [&](const std::string& kind, float intensity) {
                                if (kind == "brightness") Screen_SetBrightness(intensity);
                                else if (kind == "zoom") Screen_SetZoom(intensity);
                            };
                            BehaviorInterpreter::Tick(part.scriptState, partActor);
                        }
                        // 複合オブジェクトのパーツ追従 — スクリプトがワールド座標を直接書いた場合に備えて
                        // ローカルを取り直す（SetLocalOffset系しか使っていなければ何も起きない）。
                        // 必ず ApplyPartsParentPose より前で呼ぶこと。
                        CapturePartsLocal(enemy.parts, ePartPose);
                    }
                }
                if (canEnemyAct) {
                    enemy.history.push_back({ enemy.x, enemy.y, enemy.vx, enemy.vy, enemy.direction, enemy.scale, enemy.angle, enemy.speedScale, enemy.isActive, enemy.isPaused, enemy.type, enemy.hp,
                                               enemy.customTimer, enemy.aiState, enemy.patrolLeft, enemy.patrolRight, enemy.auxF1, enemy.auxF2, enemy.auxState, enemy.auxFlag, enemy.auxF3 });
                    if (enemy.history.size() > MAX_HISTORY_FRAMES) enemy.history.erase(enemy.history.begin());
                }
            }
            // 複合オブジェクトのパーツ追従 — ローカルオフセットと親の姿勢からワールド座標を組み直す。
            // 巻き戻し中・一時停止中・編集ジェスチャ中（CanUpdateがfalse）でスクリプトが1ティックも
            // 回らないフレームでも必ずここを通るので、パーツは常に本体へ張り付いたままになる。
            // ドラッグで本体を掴んで動かしている最中にパーツだけ置き去りになる既存の不具合も、これで直る。
            if (!enemy.parts.empty()) {
                ApplyPartsParentPose(enemy.parts, MakeEnemyPose(enemy, FindEnemyDef(enemy.assetId)));
            }
        }

        // アイテムシステムの更新（巻き戻しの統合を含む）
        for (auto& item : items) {
            bool isItemRew = isRKeyPressed && (selectedType == SELECT_NONE); // アイテムのグローバル巻き戻しサポート
            if (isItemRew) {
                if (!item.history.empty()) {
                    ItemState s = item.history.back();
                    item.history.pop_back();
                    item.x = s.x; item.y = s.y;
                    item.isCollected = s.isCollected;
                    if (!item.isCollected) item.isActive = true;
                }
            } else {
                if (!isPaused || isStepFrame) {
                    // アイテムの通常衝突検出
                    if (item.isActive && !item.isCollected) {
                        float pw_scaled = (float)player.width * player.scale;
                        float ph_scaled = (float)player.height * player.scale;
                        // 衝突チェック
                        if (player.x < item.x + item.hitboxWidth && player.x + pw_scaled > item.x &&
                            player.y < item.y + item.hitboxHeight && player.y + ph_scaled > item.y) {
                            item.isCollected = true;
                            item.isActive = false;
                            const ItemDef* idef = FindItemDef(item.assetId);
                            if (idef) {
                                SoundManager::Get().PlaySe(idef->seCollect);
                                // Feature: 能力付与アイテム — grant_abilityに応じてプレイヤーの能力を解放する
                                if (idef->grant_ability == "DoubleJump") editorPlayerCaps.canDoubleJump = true;
                                else if (idef->grant_ability == "Dash") editorPlayerCaps.canDash = true;
                                else if (idef->grant_ability == "ShootFireball") editorPlayerCaps.canShootFireball = true;
                                else if (idef->grant_ability.rfind("RestoreEditCost:", 0) == 0) {
                                    float amt = 0.0f;
                                    try { amt = std::stof(idef->grant_ability.substr(16)); } catch (...) { amt = 0.0f; }
                                    editCost += amt;
                                    if (editCost > currentEditCost.maxCost) editCost = currentEditCost.maxCost;
                                }
                                else if (idef->grant_ability.rfind("UnlockEditTool:", 0) == 0) {
                                    std::string op = idef->grant_ability.substr(15);
                                    if (op == "Rewind") unlockedEditTools.rewindEnabled = true;
                                    else if (op == "Pause") unlockedEditTools.pauseEnabled = true;
                                    else if (op == "FastForward") unlockedEditTools.fastForwardEnabled = true;
                                    else if (op == "ScreenEffect") unlockedEditTools.screenEffectEnabled = true;
                                    else if (op == "ObjectEdit") unlockedEditTools.objectEditEnabled = true;
                                    // Feature: カット機能の復活 — カットもアイテムで恒久解禁できるようにする
                                    else if (op == "Cut") unlockedEditTools.cutEnabled = true;
                                }
                            }
                        }
                    }

                    // Feature: Composite Multi-Part Objects (Parts-M4) — パーツに触れてもアイテム全体を回収する
                    // （アイテムは回収と同時に消えるため、パーツ単体を破壊する概念は無い＝装飾/収集判定のみ）
                    if (item.isActive && !item.isCollected) {
                        float pw_scaled = (float)player.width * player.scale;
                        float ph_scaled = (float)player.height * player.scale;
                        for (const auto& part : item.parts) {
                            if (!part.isActive) continue;
                            float partHX, partHY, partW, partH;
                            // 複合オブジェクトのパーツ追従 — 判定矩形は親の倍率込みで1箇所にまとめてある
                            GetPartHitRect(part, partHX, partHY, partW, partH);
                            if (CheckCollision(player.x, player.y, pw_scaled, ph_scaled,
                                                partHX, partHY, partW, partH)) {
                                item.isCollected = true;
                                item.isActive = false;
                                const ItemDef* idef = FindItemDef(item.assetId);
                                if (idef) {
                                    SoundManager::Get().PlaySe(idef->seCollect);
                                    // Feature: 能力付与アイテム — grant_abilityに応じてプレイヤーの能力を解放する
                                    if (idef->grant_ability == "DoubleJump") editorPlayerCaps.canDoubleJump = true;
                                    else if (idef->grant_ability == "Dash") editorPlayerCaps.canDash = true;
                                    else if (idef->grant_ability == "ShootFireball") editorPlayerCaps.canShootFireball = true;
                                    else if (idef->grant_ability.rfind("RestoreEditCost:", 0) == 0) {
                                        float amt = 0.0f;
                                        try { amt = std::stof(idef->grant_ability.substr(16)); } catch (...) { amt = 0.0f; }
                                        editCost += amt;
                                        if (editCost > currentEditCost.maxCost) editCost = currentEditCost.maxCost;
                                    }
                                    else if (idef->grant_ability.rfind("UnlockEditTool:", 0) == 0) {
                                        std::string op = idef->grant_ability.substr(15);
                                        if (op == "Rewind") unlockedEditTools.rewindEnabled = true;
                                        else if (op == "Pause") unlockedEditTools.pauseEnabled = true;
                                        else if (op == "FastForward") unlockedEditTools.fastForwardEnabled = true;
                                        else if (op == "ScreenEffect") unlockedEditTools.screenEffectEnabled = true;
                                        else if (op == "ObjectEdit") unlockedEditTools.objectEditEnabled = true;
                                    // Feature: カット機能の復活 — カットもアイテムで恒久解禁できるようにする
                                    else if (op == "Cut") unlockedEditTools.cutEnabled = true;
                                    }
                                }
                                break;
                            }
                        }
                    }

                    // Feature: Composite Multi-Part Objects (Parts-M3)
                    if (item.isActive && !item.isCollected) {
                        const ItemDef* idefForParts = FindItemDef(item.assetId);
                        // 複合オブジェクトのパーツ追従 — アイテムは編集できないので変換は恒等だが、
                        // 敵・ギミックと同じ経路に乗せておくことで扱いを1つに統一する。
                        ParentPose iPartPose = MakeItemPose(item);
                        if (idefForParts) {
                            for (auto& part : item.parts) {
                                if (!part.isActive) continue;
                                if (!part.scriptState.started && !part.scriptState.finished && !part.scriptState.faulted) {
                                    json partScript = (part.partIndex >= 0 && part.partIndex < (int)idefForParts->parts.size())
                                        ? idefForParts->parts[part.partIndex].script : json::array();
                                    BehaviorInterpreter::Start(part.scriptState, partScript, "OnSpawn");
                                }
                                ScriptActor partActor;
                                partActor.timeScale = ts; // 早送り/スローモーションをスクリプト駆動のパーツにも反映する
                                partActor.x = &part.x; partActor.y = &part.y;
                                partActor.scale = &part.scale; partActor.angle = &part.angle;
                                partActor.hasParent = true;
                                partActor.parentX = item.x; partActor.parentY = item.y;
                                partActor.partIndex = part.partIndex;
                                FillScriptPartTransform(partActor, part, iPartPose);
                                partActor.playerX = player.x; partActor.playerY = player.y;
                                partActor.playSound = [](const std::string& slot) { SoundManager::Get().PlaySe(slot); };
                                partActor.visualEffect = [&](const std::string& kind, float intensity) {
                                    if (kind == "brightness") Screen_SetBrightness(intensity);
                                    else if (kind == "zoom") Screen_SetZoom(intensity);
                                };
                                BehaviorInterpreter::Tick(part.scriptState, partActor);
                            }
                            CapturePartsLocal(item.parts, iPartPose);
                        }
                    }
                    item.history.push_back({ item.x, item.y, item.isCollected });
                    if (item.history.size() > MAX_HISTORY_FRAMES) item.history.erase(item.history.begin());
                }
            }
            // 複合オブジェクトのパーツ追従 — 毎フレーム必ず通る場所でワールド座標を組み直す
            if (!item.parts.empty()) ApplyPartsParentPose(item.parts, MakeItemPose(item));
        }

        // 3. 弾の更新（巻き戻し vs 通常物理）
        for (int i = 0; i < MAX_BULLETS; i++) {
            if (bullets[i].isActive || !bullets[i].history.empty()) {
                bool isBltRew = bullets[i].isRewinding || (isRKeyPressed && selectedType == SELECT_NONE);
                if (isBltRew) {
                    if (!bullets[i].history.empty()) {
                        BulletState s = bullets[i].history.back();
                        bullets[i].history.pop_back();
                        bullets[i].x = s.x; bullets[i].y = s.y; bullets[i].vx = s.vx; bullets[i].vy = s.vy;
                        bullets[i].isActive = s.isActive;
                        bullets[i].isPlayerOwned = s.isPlayerOwned;
                    }
                } else {
                    bool canBulletAct = CanUpdate(bullets[i].x, bullets[i].y, 16.0f, 16.0f, 1.0f, false);
                    if (canBulletAct) {
                        if (bullets[i].isActive) {
                            bullets[i].x += bullets[i].vx * ts;
                            bullets[i].y += bullets[i].vy * ts; // 垂直方向の弾の物理挙動をサポート
                            
                            // タイルマップとの衝突判定。固定80x15ではなく、現在のステージの実際のマップサイズで判定する。
                            int tx = (int)(bullets[i].x / TILE_SIZE);
                            int ty = (int)(bullets[i].y / TILE_SIZE);
                            int curMapH = (int)stages[currentStageIdx].map.size();
                            int curMapW = curMapH > 0 ? (int)stages[currentStageIdx].map[0].size() : 0;
                            if (ty >= 0 && ty < curMapH && tx >= 0 && tx < curMapW) {
                                TileType t = (TileType)stages[currentStageIdx].map[ty][tx];
                                if (tileDefs[t].isCollidable) {
                                    bullets[i].isActive = false; // 壁に衝突して消滅
                                }
                            }

                            // 画面外チェック（Y方向も拡張）
                            // 縦スクロール対応: 弾の消滅範囲もSCREEN_HEIGHT固定ではなくSTAGE_HEIGHTを使う
                            if (bullets[i].isActive && (bullets[i].x < -50 || bullets[i].x > STAGE_WIDTH + 50 || bullets[i].y < -50 || bullets[i].y > STAGE_HEIGHT + 50)) {
                                bullets[i].isActive = false;
                            }

                            // 反射ミラー（GIMMICK_REFLECT_MIRROR）での弾の反射チェック
                            for (const auto& gim : gimmicks) {
                                if (gim.type == GIMMICK_REFLECT_MIRROR && gim.isActive) {
                                    // 反射のバウンディングボックス交差チェック
                                    if (bullets[i].x >= gim.x && bullets[i].x <= gim.x + gim.spriteWidth &&
                                        bullets[i].y >= gim.y && bullets[i].y <= gim.y + gim.spriteHeight) {

                                        // 反射物理：角度 gim.angle は板の回転を表す
                                        // 面の法線ベクトル N は ( -sin(angle), cos(angle) )
                                        float nx = -sinf(gim.angle);
                                        float ny = cosf(gim.angle);

                                        // 反射速度ベクトル：R = V - 2 * (V . N) * N
                                        float dot = bullets[i].vx * nx + bullets[i].vy * ny;
                                        bullets[i].vx = bullets[i].vx - 2.0f * dot * nx;
                                        bullets[i].vy = bullets[i].vy - 2.0f * dot * ny;

                                        // 編集リアクション：
                                        //  ・幅を広げる → 反射した弾が加速する（遠くのスイッチまで届かせられる）。
                                        //  ・向き反転   → 反射した弾の所属が入れ替わり、敵の弾を跳ね返して
                                        //                 敵に当てられるようになる。
                                        EditReaction mr = GetGimmickEditReaction(gim);
                                        if (mr.scaleRatio > 1.0f) {
                                            bullets[i].vx *= mr.scaleRatio;
                                            bullets[i].vy *= mr.scaleRatio;
                                        }
                                        if (mr.flipped) bullets[i].isPlayerOwned = !bullets[i].isPlayerOwned;

                                        // 無限反射ループを避けるため、弾をミラーから少し押し出す
                                        const GimmickDef* gdef = FindGimmickDef(gim.assetId);
                                        float pushOut = gdef ? gdef->pushOutDistance : 1.5f;
                                        bullets[i].x += bullets[i].vx * pushOut;
                                        bullets[i].y += bullets[i].vy * pushOut;
                                        break;
                                    }
                                }
                            }
                        }
                    }
                    if (canBulletAct) {
                        bullets[i].history.push_back({ bullets[i].x, bullets[i].y, bullets[i].vx, bullets[i].vy, bullets[i].isActive, bullets[i].isPlayerOwned });
                        if (bullets[i].history.size() > MAX_HISTORY_FRAMES) bullets[i].history.erase(bullets[i].history.begin());
                    }
                }
            }
        }

        // 4. ギミックの更新（物理挙動、回転、および巻き戻し履歴）
        for (auto& gim : gimmicks) {
            float beforeGimX = gim.x, beforeGimY = gim.y;
            bool isGimRew = gim.isRewinding || (isRKeyPressed && !isRotating && (selectedType == SELECT_NONE || (selectedType == SELECT_GIMMICK && targetGimmick == &gim)));
            if (isGimRew) {
                if (!gim.history.empty()) {
                    GimmickState s = gim.history.back();
                    gim.history.pop_back();
                    gim.x = s.x; gim.y = s.y;
                    gim.angle = s.angle;
                    gim.isActive = s.isActive;
                }
                // 巻き戻しによる位置ジャンプは乗っているプレイヤー/敵に追従させない
                gim.lastDeltaX = 0.0f;
                gim.lastDeltaY = 0.0f;
            } else {
                bool canGimAct = CanUpdate(gim.x, gim.y, gim.spriteWidth, gim.spriteHeight, 1.0f, gim.isPaused);
                if (canGimAct) {
                    // Feature: 編集リアクション — ギミックにも個体ごとの速度編集を効かせる。
                    // これで「動く足場をゆっくりにして乗る」「回転橋を止める」といった操作が成立する。
                    float gts = ts * gim.speedScale;

                    // ============ Feature: 編集リアクション（ギミック共通の前処理）============
                    // このギミックに加えられた編集差分を一度だけ求め、以降の分岐で共有する。
                    EditReaction gr = GetGimmickEditReaction(gim);
                    // 「幅を広げたぶん動きが重くなる」倍率。狭めれば軽快に、広げれば鈍重になる。
                    float grMass = gr.MassMul();
                    float grDir = gr.flipped ? -1.0f : 1.0f; // 向き反転で往復や回転が逆になる

                    // Feature: 編集リアクション — 往復や昇降の「基準点」を持つギミックは、
                    // 移動編集で掴んで動かされたら基準点も一緒に運ぶ。
                    // これをしないと、動かした次のフレームに元の場所へ戻ってしまい、
                    // 「動かせるのに動かした意味がない」という状態になる。
                    if (gr.moved && (gim.type == GIMMICK_MOVING_PLATFORM || gim.type == GIMMICK_FRAMESTEP_LIFT)) {
                        gim.editBaseX = gim.x;
                        gim.editBaseY = gim.y;
                        gim.editDirtyMask &= ~(unsigned int)EDIT_DIRTY_POS;
                        gr.moved = false;
                    }

                    // Feature: 編集リアクションのJSON宣言 — ギミック側もアセットで反応を足せる。
                    {
                        const GimmickDef* grDef = FindGimmickDef(gim.assetId);
                        if (grDef != nullptr && !grDef->editReactions.empty()) {
                            DeclaredEffects gfx = EvalDeclaredReactions(grDef->editReactions, gr);
                            grMass *= gfx.mulMoveSpeed;
                            if (gfx.reverseCycle) grDir = -grDir;
                            if (gfx.destroy)      gim.isActive = false;
                        }
                    }
                    // =====================================================================

                    if (gim.type == GIMMICK_ROTATING_BRIDGE) {
                        // 橋を自動回転。
                        // 編集リアクション：幅を広げるほど重くて回転が遅くなり、向き反転で逆回りになる。
                        // （この型のangleはAIが握っているので傾け編集は受け付けない＝GimmickAngleIsAiOwned）
                        const GimmickDef* gdef = FindGimmickDef(gim.assetId);
                        float rotSpeed = (gdef ? gdef->rotationSpeed : 0.015f) * grMass * grDir;
                        gim.angle += rotSpeed * gts;
                    }
                    else if (gim.type == GIMMICK_CHIKUWA_BLOCK) {
                        // ちくわブロックロジック（gim.customTimerをrideTimer、gim.val1を元のY座標、gim.val2をfallDelay、gim.angleをisFallingフラグとして使用）
                        // 編集リアクション：
                        //  ・幅を広げる → 支える面積が増え、落ちるまでの猶予が伸びる。
                        //  ・幅を狭める → すぐ落ちる。
                        //  ・向き反転   → 落ちずに上へ飛んでいく（天井側の足場を作れる）。
                        //  ・速度       → 猶予の進み方が変わる（gtsに乗る）。
                        // （この型はangleを「落下中フラグ」に流用しているため傾け編集は受け付けない）
                        const GimmickDef* gdef = FindGimmickDef(gim.assetId);
                        float standDelay = (gdef ? gdef->standDelayFrames : 45.0f) * gr.scaleRatio;
                        float standTolerance = gdef ? gdef->standTolerancePx : 10.0f;
                        float respawnDelay = gdef ? gdef->respawnDelayFrames : 180.0f;
                        // gim.val1 が未初期化(0)なら現在Yを記録し、fallDelayをセット
                        if (gim.val1 == 0.0f) {
                            gim.val1 = gim.y;
                            gim.val2 = standDelay;
                        }

                        // プレイヤーが上に乗っているか判定
                        float pw_scaled = (float)player.width * player.scale;
                        float ph_scaled = (float)player.height * player.scale;
                        float footY = player.y + ph_scaled;
                        bool isPlayerOn = false;
                        if (player.vy >= 0 && player.x + pw_scaled > gim.x && player.x < gim.x + gim.spriteWidth) {
                            if (footY >= gim.y && footY <= gim.y + standTolerance) {
                                isPlayerOn = true;
                            }
                        }

                        if (isPlayerOn && gim.angle == 0.0f) {
                            gim.customTimer += 1.0f * gts;
                            if (gim.customTimer >= gim.val2) {
                                gim.angle = 1.0f; // angle=1.0を「落下中」フラグとして流用する（このギミックでは角度そのものは描画に使わないため）
                                // val2はここまで「待機フレーム数」として使っていたが、これ以降は疑似落下速度として
                                // 再利用するため、0から加速し直すようリセットする（しないと待機フレーム数分の
                                // 初速がついた状態で瞬間ワープ落下してしまう）
                                gim.val2 = 0.0f;
                            }
                        } else if (gim.angle == 0.0f) {
                            gim.customTimer = 0.0f;
                            gim.y = gim.val1;
                        }

                        if (gim.angle != 0.0f) {
                            // 落下中。gim.val2 は本来fallDelayだが、vyの管理に使いたいが別の変数がない。
                            // 代わりに val2 を少しずつ増やして擬似vyにする
                            gim.val2 += GRAVITY * gts;
                            gim.y += gim.val2 * gts * grDir; // 向き反転で上へ飛んでいく
                            if (gim.y > SCREEN_HEIGHT + 100 || gim.y < -200.0f) {
                                gim.customTimer += 1.0f * gts;
                                if (gim.customTimer > respawnDelay) {
                                    gim.angle = 0.0f;
                                    gim.y = gim.val1;
                                    gim.val2 = standDelay;
                                    gim.customTimer = 0.0f;
                                }
                            }
                        }
                    }
                    else if (gim.type == GIMMICK_CHOMPER) {
                        // 敵食いギミック。
                        // 編集リアクション：
                        //  ・幅／高さを変える → 捕食範囲がそのまま伸び縮みする。
                        //  ・向き反転         → プレイヤーを食わなくなる代わりに、敵だけを食い続ける
                        //                       （敵を排除する装置として使い回せる）。
                        // 対プレイヤー
                        float pw_scaled = (float)player.width * player.scale;
                        float ph_scaled = (float)player.height * player.scale;
                        if (!gr.flipped && CheckCollision(player.x, player.y, pw_scaled, ph_scaled, gim.x, gim.y, gim.spriteWidth, gim.spriteHeight)) {
                            player.hp = 0;
                            const GimmickDef* gdef = FindGimmickDef(gim.assetId);
                            if (gdef) SoundManager::Get().PlaySe(gdef->seActivate);
                        }
                        // 対敵
                        for (auto& enemy : enemies) {
                            if (enemy.isActive && CheckCollision(enemy.x, enemy.y, (float)enemy.hitboxWidth * enemy.scale, (float)enemy.hitboxHeight * enemy.scale, gim.x, gim.y, gim.spriteWidth, gim.spriteHeight)) {
                                enemy.hp = 0;
                                enemy.isActive = false;
                                // 向きを反転させた個体は消えずに残り、次の敵も食べ続ける
                                if (!gr.flipped) gim.isActive = false; // 互いに消滅
                                const GimmickDef* gdef = FindGimmickDef(gim.assetId);
                                if (gdef) SoundManager::Get().PlaySe(gdef->seActivate);
                            }
                        }
                    }
                    else if (gim.type == GIMMICK_MOVING_PLATFORM) {
                        // 動く足場：val1(上端)～val2(下端)を自動で往復する
                        // Feature: 動く足場の空中配置対応（友人フィードバック対応）— 従来は配置Yを上端として
                        // 常に下方向にのみ振動する式だったため、空中に置いても地面側へ沈むように見えていた。
                        // 配置Yを振動の中心とし、±travel/2の範囲で往復するようにする。
                        // 編集リアクション：
                        //  ・傾ける     → 往復の軸そのものが傾く。縦揺れの足場を横移動や斜め移動に変えられる。
                        //  ・幅を広げる → 往復距離が伸びる（届かなかった場所まで運んでくれる）。
                        //  ・速度       → 往復が速く/遅くなる。
                        //  ・向き反転   → 往復の位相が逆になり、待ち合わせのタイミングをずらせる。
                        const GimmickDef* gdef = FindGimmickDef(gim.assetId);
                        float travel = (gdef ? gdef->travelDistance : 96.0f) * gr.scaleRatio;
                        float oscSpeed = (gdef ? gdef->oscillationSpeed : 0.02f) * grMass;
                        gim.customTimer += oscSpeed * gts;
                        // -1〜+1の往復量。位相は向き反転で入れ替わる
                        float wave = sinf(gim.customTimer) * grDir;
                        // 配置位置(editBase*)を中心として、傾けた軸の向きへ往復させる。
                        // val1/val2/customTimerは既に別用途で埋まっているため、軸の基準にはeditBaseX/Yを使う。
                        gim.x = gim.editBaseX + sinf(gr.tilt) * travel * 0.5f * wave;
                        gim.y = gim.editBaseY + cosf(gr.tilt) * travel * 0.5f * wave;
                    }
                    else if (gim.type == GIMMICK_FRAMESTEP_LIFT) {
                        // コマ送りリフト：一時停止中の→キー（コマ送り）1回ごとに一歩だけ動く
                        // 編集リアクション：
                        //  ・幅を広げる → 1コマあたりの移動量が増える（少ないコマ送りで遠くまで行ける）。
                        //  ・傾ける     → 昇降ではなく傾けた向きへ進む。
                        //  ・向き反転   → 進む向きが逆になる。
                        //  ・速度       → 1コマの刻み幅が変わる。
                        const GimmickDef* gdef = FindGimmickDef(gim.assetId);
                        float travel = (gdef ? gdef->travelDistance : 128.0f) * gr.scaleRatio;
                        float stepInc = (gdef ? gdef->stepIncrement : 0.15f) * gim.speedScale;
                        if (isStepFrame) {
                            gim.customTimer += stepInc * grDir;
                            if (gim.customTimer > 1.0f) gim.customTimer = 0.0f;
                            if (gim.customTimer < 0.0f) gim.customTimer = 1.0f;
                        }
                        // 傾けた向きへ進ませる。傾き0なら従来どおり真下(+Y)方向への昇降になる。
                        gim.x = gim.editBaseX - sinf(gr.tilt) * travel * gim.customTimer;
                        gim.y = gim.editBaseY + cosf(gr.tilt) * travel * gim.customTimer;
                    }
                    else if (gim.type == GIMMICK_BREAKABLE_BLOCK) {
                        // 編集リアクション：45度以上傾けると自重で崩れる。
                        // クリックで直接叩かなくても、離れた場所のブロックを回して壊せるようになる。
                        // isActiveは巻き戻し履歴に入っているので、巻き戻せばちゃんと元通り積み直る。
                        if (gr.tipped) {
                            gim.isActive = false;
                            const GimmickDef* gdefBb = FindGimmickDef(gim.assetId);
                            if (gdefBb) SoundManager::Get().PlaySe(gdefBb->seActivate);
                        }
                    }
                    else if (gim.type == GIMMICK_PUSHABLE_ROCK) {
                        // 編集リアクション：
                        //  ・縮小する → 支えを失って転がる岩になる。傾けた向き（無ければ向き反転の向き）へ
                        //               転がっていくので、下のスイッチを押させたり通路を空けたりできる。
                        //  ・等倍以上 → 従来どおりその場を塞ぐ固定障害物のまま。
                        // val1/val2/customTimerがこの型では未使用なので、val1を疑似的な横速度として使う。
                        if (gr.shrunk) {
                            float rollDir = (gr.tilt != 0.0f) ? ((gr.tilt > 0.0f) ? 1.0f : -1.0f) : (gr.flipped ? -1.0f : 1.0f);
                            gim.val1 += rollDir * 0.12f * gts;
                            if (gim.val1 >  4.0f) gim.val1 =  4.0f;
                            if (gim.val1 < -4.0f) gim.val1 = -4.0f;
                            gim.x += gim.val1 * gts;
                            gim.angle += gim.val1 * 0.05f * gts; // 転がっている見た目にする
                        } else {
                            gim.val1 = 0.0f;
                        }
                    }
                    else if (gim.type == GIMMICK_CHECKPOINT) {
                        // 編集リアクション：
                        //  ・拡大する   → 作動範囲が広がる（通り道から外れていても記録される）。
                        //  ・向き反転   → 一度きりの制限が外れ、通るたびに復帰地点を上書きし直す。
                        // 実際の記録処理は接触判定側にあるので、ここでは再武装だけ行う。
                        if (gr.flipped) gim.val1 = 0.0f;
                    }
                    else if (gim.type == GIMMICK_FASTFORWARD_GATE) {
                        // 早送りゲート：早送りモード中(Fキー)だけ通過できる壁。
                        // 編集リアクション：
                        //  ・傾ける   → 開く条件が反転し、「早送り中だけ閉じる」壁になる。
                        //  ・向き反転 → 早送りではなくコマ送り（一時停止中の→キー）で開くゲートに変わる。
                        if (gr.flipped)      gim.isActive = !isStepFrame;
                        else if (gr.tipped)  gim.isActive = isFastForward;
                        else                 gim.isActive = !isFastForward;
                    }
                    else if (gim.type == GIMMICK_BRIGHTNESS_ZONE || gim.type == GIMMICK_COLOR_ZONE
                             || gim.type == GIMMICK_ZOOM_LENS || gim.type == GIMMICK_SLOWMO_FIELD) {
                        // 画面全体エフェクト系ゾーン：範囲内にプレイヤーがいる間だけ効果を発動する
                        const GimmickDef* gdef = FindGimmickDef(gim.assetId);
                        float pw_s = (float)player.width * player.scale;
                        float ph_s = (float)player.height * player.scale;
                        if (CheckCollision(player.x, player.y, pw_s, ph_s, gim.x, gim.y, gim.spriteWidth, gim.spriteHeight)) {
                            // 編集リアクション：
                            //  ・幅／高さを変える → 効果範囲がそのまま伸び縮みする（判定にspriteサイズを使っているため自動）。
                            //  ・向き反転         → 効果が反転する（暗転↔明転、ズームイン↔ズームアウト、色↔補色）。
                            //  ・速度             → 効果の強さが変わる（1.0が既定値）。
                            float fxStr = gim.speedScale; // 速度編集をそのまま「効果の強さ」に読み替える
                            if (gim.type == GIMMICK_BRIGHTNESS_ZONE) {
                                // val1 > 0.5 なら明転、それ以外は暗転（デフォルト）
                                float bright = gdef ? gdef->brightLevel : 1.6f;
                                float dark = gdef ? gdef->darkLevel : 0.35f;
                                bool wantBright = (gim.val1 > 0.5f);
                                if (gr.flipped) wantBright = !wantBright;
                                float lvl = wantBright ? bright : dark;
                                Screen_SetBrightness(1.0f + (lvl - 1.0f) * fxStr);
                            } else if (gim.type == GIMMICK_COLOR_ZONE) {
                                float r = gdef ? gdef->tintR : 1.0f, g = gdef ? gdef->tintG : 0.6f, b = gdef ? gdef->tintB : 1.0f;
                                if (gr.flipped) { r = 2.0f - r; g = 2.0f - g; b = 2.0f - b; } // 補色寄りへ反転
                                Screen_SetTint(1.0f + (r - 1.0f) * fxStr,
                                               1.0f + (g - 1.0f) * fxStr,
                                               1.0f + (b - 1.0f) * fxStr);
                            } else if (gim.type == GIMMICK_ZOOM_LENS) {
                                float z = gdef ? gdef->zoomLevel : 1.6f;
                                if (gr.flipped) z = (z > 0.01f) ? (1.0f / z) : 1.0f; // 反転でズームアウトになる
                                Screen_SetZoom(1.0f + (z - 1.0f) * fxStr);
                            } else { // GIMMICK_SLOWMO_FIELD（視覚効果のみのスローモーション演出）
                                float z = gdef ? gdef->zoomLevel : 1.3f;
                                float b2 = gdef ? gdef->brightLevel : 0.85f;
                                if (gr.flipped) { z = (z > 0.01f) ? (1.0f / z) : 1.0f; b2 = 2.0f - b2; }
                                Screen_SetZoom(1.0f + (z - 1.0f) * fxStr);
                                Screen_SetBrightness(1.0f + (b2 - 1.0f) * fxStr);
                            }
                        }
                    }
                    else if (gim.type == GIMMICK_COLOR_LOCK_PLATFORM) {
                        // 色ロック足場：paramの色番号とプレイヤーの色フィルタが一致する時だけ実体化する。
                        // 編集リアクション：
                        //  ・傾ける   → 要求する色が赤→緑→青と1段ずつ進む（足場の出し方を組み替えられる）。
                        //  ・向き反転 → 条件が反転し「その色以外なら実体化する」足場になる。
                        int requiredColor = gim.param.empty() ? 1 : atoi(gim.param.c_str());
                        // 60度ごとに1色ずらす。負の傾きでも正しく巡回するよう剰余を正規化する
                        requiredColor = ((((requiredColor - 1 + gr.tiltSteps) % 3) + 3) % 3) + 1;
                        bool colorMatch = (requiredColor == playerColorFilter);
                        gim.isActive = gr.flipped ? !colorMatch : colorMatch;
                    }
                    else if (gim.type == GIMMICK_BRIGHTNESS_LOCK_PLATFORM) {
                        // 明暗ロック足場：param("dark"既定/"bright")と現在の画面の明るさが一致する時だけ実体化する。
                        // 編集リアクション：
                        //  ・向き反転   → 要求する明暗が入れ替わる（dark↔bright）。
                        //  ・幅を広げる → 判定のしきい値が緩くなり、中途半端な明るさでも実体化する。
                        bool wantsBright = (gim.param == "bright");
                        if (gr.flipped) wantsBright = !wantsBright;
                        // 拡大するほどしきい値が1.0へ寄る＝条件が緩む
                        float loose = (gr.scaleRatio > 1.0f) ? (gr.scaleRatio - 1.0f) * 0.3f : 0.0f;
                        if (loose > 0.35f) loose = 0.35f;
                        gim.isActive = wantsBright ? (fxCurBright > 1.3f - loose) : (fxCurBright < 0.6f + loose);
                    }
                    else if (gim.type == GIMMICK_CUSTOM_SCRIPT) {
                        // Feature: Puzzle-like Behavior Scripting (M2) — GimmickDef.scriptのJSONブロックで駆動する
                        ScriptActor actor;
                        actor.timeScale = gts; // 早送り/スローモーションをスクリプト駆動のギミックにも反映する
                        FillScriptEditContext(actor, gr); // Feature: 編集リアクション
                        actor.x = &gim.x; actor.y = &gim.y;
                        actor.angle = &gim.angle; // Feature: Composite Multi-Part Objects (Parts-M2)
                        actor.playerX = player.x; actor.playerY = player.y;
                        // ギミックにはvx/vy/direction/scaleに相当するフィールドが無いため、それらを使うブロックは
                        // 自動的に何もしない（ScriptActorのnullチェックにより安全にスキップされる）
                        Gimmick* gimPtr = &gim;
                        actor.shoot = [&bullets, gimPtr](float angleRad, float speed, float damage) {
                            (void)damage;
                            for (int i = 0; i < MAX_BULLETS; i++) {
                                if (!bullets[i].isActive) {
                                    bullets[i].isActive = true;
                                    bullets[i].x = gimPtr->x + gimPtr->spriteWidth / 2.0f;
                                    bullets[i].y = gimPtr->y + gimPtr->spriteHeight / 2.0f;
                                    bullets[i].vx = cosf(angleRad) * speed;
                                    bullets[i].vy = sinf(angleRad) * speed;
                                    bullets[i].isPlayerOwned = false;
                                    bullets[i].isRewinding = false;
                                    bullets[i].history.clear();
                                    break;
                                }
                            }
                        };
                        actor.playSound = [](const std::string& slot) { SoundManager::Get().PlaySe(slot); };
                        actor.visualEffect = [&](const std::string& kind, float intensity) {
                            if (kind == "brightness") Screen_SetBrightness(intensity);
                            else if (kind == "zoom") Screen_SetZoom(intensity);
                        };
                        BehaviorInterpreter::Tick(gim.scriptState, actor);
                    }

                    // Feature: Composite Multi-Part Objects (Parts-M3)
                    // パーツは親のtype(挙動タイプ)に関係なく、独立して自分のスクリプトを実行する
                    const GimmickDef* gdefForParts = FindGimmickDef(gim.assetId);
                    // 複合オブジェクトのパーツ追従 — ギミック本体が動き終えた「今の姿勢」
                    ParentPose gPartPose = MakeGimmickPose(gim);
                    if (gdefForParts) {
                        for (auto& part : gim.parts) {
                            if (!part.isActive) continue;
                            if (!part.scriptState.started && !part.scriptState.finished && !part.scriptState.faulted) {
                                json partScript = (part.partIndex >= 0 && part.partIndex < (int)gdefForParts->parts.size())
                                    ? gdefForParts->parts[part.partIndex].script : json::array();
                                BehaviorInterpreter::Start(part.scriptState, partScript, "OnSpawn");
                            }
                            ScriptActor partActor;
                            partActor.timeScale = gts; // 早送り/スローモーションをスクリプト駆動のパーツにも反映する
                            partActor.x = &part.x; partActor.y = &part.y;
                            partActor.scale = &part.scale; partActor.angle = &part.angle;
                            partActor.hasParent = true;
                            FillScriptEditContext(partActor, gr); // 親に加えられた編集をパーツにも伝える（敵のパーツと揃える）
                            partActor.parentX = gim.x; partActor.parentY = gim.y;
                            partActor.parentDirection = (gim.direction == 0) ? 1.0f : -1.0f; // 0=正方向, それ以外=反転
                            partActor.partIndex = part.partIndex;
                            FillScriptPartTransform(partActor, part, gPartPose);
                            partActor.playerX = player.x; partActor.playerY = player.y;
                            PartInstance* partPtr = &part;
                            partActor.shoot = [&bullets, partPtr](float angleRad, float speed, float damage) {
                                (void)damage;
                                for (int i = 0; i < MAX_BULLETS; i++) {
                                    if (!bullets[i].isActive) {
                                        bullets[i].isActive = true;
                                        bullets[i].x = partPtr->x; bullets[i].y = partPtr->y;
                                        bullets[i].vx = cosf(angleRad) * speed;
                                        bullets[i].vy = sinf(angleRad) * speed;
                                        bullets[i].isPlayerOwned = false;
                                        bullets[i].isRewinding = false;
                                        bullets[i].history.clear();
                                        break;
                                    }
                                }
                            };
                            partActor.playSound = [](const std::string& slot) { SoundManager::Get().PlaySe(slot); };
                            partActor.visualEffect = [&](const std::string& kind, float intensity) {
                                if (kind == "brightness") Screen_SetBrightness(intensity);
                                else if (kind == "zoom") Screen_SetZoom(intensity);
                            };
                            BehaviorInterpreter::Tick(part.scriptState, partActor);

                            // Feature: Composite Multi-Part Objects (Parts-M4) — パーツごとの接触ダメージ判定
                            // （ギミック本体側の個別ロジック(CHOMPER/SPIKES等)とは独立して、パーツは常にこの一律の
                            //   接触ダメージ判定を持つ。hp==0のパーツは常在ハザードとして永続的にこの判定を持ち続ける）
                            if (!isPlayerRewinding) {
                                float pw_ = (float)player.width * player.scale;
                                float ph_ = (float)player.height * player.scale;
                                float partHX, partHY, partW, partH;
                                // 複合オブジェクトのパーツ追従 — 判定矩形は親の倍率込みで1箇所にまとめてある
                                GetPartHitRect(part, partHX, partHY, partW, partH);
                                if (CheckCollision(player.x, player.y, pw_, ph_,
                                                    partHX, partHY, partW, partH)) {
                                    player.hp--;
                                    if (player.hp <= 0) currentScene = RESULT_GAMEOVER;
                                }
                            }
                        }
                        CapturePartsLocal(gim.parts, gPartPose); // 必ず ApplyPartsParentPose より前
                    }
                }
                if (canGimAct) {
                    gim.history.push_back({ gim.x, gim.y, gim.angle, gim.isActive });
                    if (gim.history.size() > MAX_HISTORY_FRAMES) gim.history.erase(gim.history.begin());
                }
                // このフレームの移動量を記録し、乗っているプレイヤー/敵が次フレーム追従できるようにする
                gim.lastDeltaX = gim.x - beforeGimX;
                gim.lastDeltaY = gim.y - beforeGimY;
            }
            // 複合オブジェクトのパーツ追従 — 毎フレーム必ず通る場所でワールド座標を組み直す
            if (!gim.parts.empty()) ApplyPartsParentPose(gim.parts, MakeGimmickPose(gim));
        }

        // プレイヤーが能動的に使う画面エフェクト操作（敵/ギミックの演出より後に適用し、プレイヤーの意図を優先する）
        // Z:ズーム保持, X:暗転保持, C:明転保持, T(上で処理済み)の色フィルタを継続適用
        // Feature: 編集コストゲージ — Z/X/Cは「画面エフェクト」の継続系操作
        bool zHeldThisFrame = false, xHeldThisFrame = false, cHeldThisFrame = false;
        if (currentScene == PLAY && canPlayerAct) {
            zHeldThisFrame = CheckHitKey(KEY_INPUT_Z) != 0;
            xHeldThisFrame = CheckHitKey(KEY_INPUT_X) != 0;
            cHeldThisFrame = CheckHitKey(KEY_INPUT_C) != 0;
            bool canUseScreenFx = screenEffectOpEnabled && editCost > 0.0f;
            if (zHeldThisFrame && canUseScreenFx) Screen_SetZoom(1.6f);
            if (xHeldThisFrame && canUseScreenFx) Screen_SetBrightness(0.3f);
            if (cHeldThisFrame && canUseScreenFx) Screen_SetBrightness(1.7f);

            if (playerColorFilter == 1) Screen_SetTint(1.0f, 0.4f, 0.4f);
            else if (playerColorFilter == 2) Screen_SetTint(0.4f, 1.0f, 0.4f);
            else if (playerColorFilter == 3) Screen_SetTint(0.4f, 0.4f, 1.0f);
        }

        // ===== Feature: 編集コストゲージ：継続系ドレイン／自然回復／ゼロ到達時の強制解除 =====
        // メインループは実時間で毎フレーム回るため（isPausedはCanUpdateが判定するシミュレーション更新のみを止める）、
        // ここは ts/globalTimeScale を使わず「1/60秒=1フレーム」の実時間換算で加減算する。
        {
            const float perFrame = 1.0f / 60.0f;
            float drainPerSec = 0.0f;
            if (isAnyRewindActive)                drainPerSec += currentEditCost.drainRewindPerSec;
            if (isPaused)                         drainPerSec += currentEditCost.drainPausePerSec;
            if (isFastForward)                    drainPerSec += currentEditCost.drainFastForwardPerSec;
            if (zHeldThisFrame || xHeldThisFrame || cHeldThisFrame || playerColorFilter != 0)
                                                   drainPerSec += currentEditCost.drainScreenEffectPerSec;

            if (drainPerSec > 0.0f) editCost -= drainPerSec * perFrame;
            else                    editCost += currentEditCost.regenPerSec * perFrame;
            if (editCost > currentEditCost.maxCost) editCost = currentEditCost.maxCost;

            if (editCost <= 0.0f) {
                editCost = 0.0f;
                if (isPaused) isPaused = false;
                if (isFastForward) isFastForward = false;
                if (playerColorFilter != 0) playerColorFilter = 0;
                if (player.isRewinding) player.isRewinding = false;
                for (auto& e : enemies)  e.isRewinding = false;
                for (auto& g : gimmicks) g.isRewinding = false;
                for (auto& b : bullets)  b.isRewinding = false;
                for (auto& it : items)   it.isRewinding = false;
                SoundManager::Get().PlaySe("ui_denied");
            }
        }

        Screen_UpdateFrame(ts); // 敵/ギミックがこのフレームで設定した目標値へ滑らかに追従

        // --- カメラスクロールシステム ---
        if (!isPaused) {
            float targetCamX;
            if (cameraOverrideTimer > 0.0f) {
                // MoveCameraイベントアクション：一定時間プレイヤー追従を止めて指定座標に留まる
                targetCamX = cameraOverrideX;
                cameraOverrideTimer -= 1.0f / 60.0f * ts;
            } else {
                targetCamX = player.x - 300.0f;
            }
            if (targetCamX < 0.0f) targetCamX = 0.0f;
            if (targetCamX > STAGE_WIDTH - 640.0f) targetCamX = STAGE_WIDTH - 640.0f;
            cameraX += (targetCamX - cameraX) * 0.1f;

            // Feature: 縦スクロール対応 — cameraXと全く同じ考え方でY方向もプレイヤーへ追従させる。
            // ただしマップの高さが画面(480px)以下の場合（従来の全ステージがこれに該当）は
            // maxCamYが負になるため0にクランプし、cameraYが常に0のまま＝従来通りの固定カメラになる。
            float maxCamY = STAGE_HEIGHT - 480.0f;
            if (maxCamY < 0.0f) maxCamY = 0.0f;
            float targetCamY = player.y - 240.0f;
            if (targetCamY < 0.0f) targetCamY = 0.0f;
            if (targetCamY > maxCamY) targetCamY = maxCamY;
            cameraY += (targetCamY - cameraY) * 0.1f;
        }

        // --- 戦闘衝突判定 ＆ ヒット検出 ---
        if (!isPaused || isStepFrame) {

            float pw_scaled = (float)player.width * player.scale;
            float ph_scaled = (float)player.height * player.scale;

            // プレイヤーの落下死判定
            // Feature: 縦スクロール対応 — 従来は「画面の下端(SCREEN_HEIGHT=480固定)を越えたら死」だったため、
            // カメラが縦にスクロールして画面より下までマップが続く場合に成立しなくなる。
            // 「ステージ実際の高さ(STAGE_HEIGHT)を越えたら死」に変更する。
            // 15タイル(480px)以下の既存ステージでは STAGE_HEIGHT==SCREEN_HEIGHT のため挙動は変わらない。
            if (player.y > STAGE_HEIGHT + 50.0f) {
                ResetStage();
            }

            // 1. プレイヤー vs トゲ（即死）
            for (const auto& gim : gimmicks) {
                if (gim.type == GIMMICK_SPIKES && gim.isActive) {
                    // Feature: 編集リアクション — 45度以上倒したトゲは刺が横を向き、踏んでも死ななくなる
                    // （足場側の判定はCheckPlatformCollisionが拾う）。
                    // 拡大・縮小した場合は判定範囲もそれに追従する。
                    if (GimmickIsTipped(gim)) continue;
                    float sbx, sby, sbw, sbh;
                    GetGimmickCollisionBox(gim, sbx, sby, sbw, sbh);
                    if (CheckCollision(player.x, player.y, pw_scaled, ph_scaled, sbx, sby, sbw, sbh)) {
                        ResetStage();
                        break;
                    }
                }
            }

            // Feature: チェックポイント — 触れた瞬間に復帰地点として記録する（val1>0.5で「発動済み」を表す）。
            // isPlayerRewinding中は巻き戻し閲覧なので発動させない。
            if (!isPlayerRewinding) {
                for (auto& gim : gimmicks) {
                    if (gim.type == GIMMICK_CHECKPOINT && gim.isActive && gim.val1 < 0.5f) {
                        if (CheckCollision(player.x, player.y, pw_scaled, ph_scaled, gim.x, gim.y, gim.hitboxWidth, gim.hitboxHeight)) {
                            gim.val1 = 1.0f;
                            checkpointX = gim.x;
                            checkpointY = gim.y;
                            const GimmickDef* gdefCp = FindGimmickDef(gim.assetId);
                            if (gdefCp) SoundManager::Get().PlaySe(gdefCp->seActivate);
                        }
                    }
                }
            }

            // Feature: 危険パーツ — プレイヤー vs ギミックのパーツ（回転する棘の輪・ファイアバー・振り子など）。
            // 敵のパーツと同じく1ダメージ＋ノックバック＋無敵時間にそろえてある。
            // トゲ床(GIMMICK_SPIKES)の即死ではなく被ダメージ扱いにしたのは、
            // これらが自分から動き回るハザードで、避けきれない位置に来ることがあるため。
            // 即死にすると理不尽になる一方、無傷では避ける意味が無くなるので、その中間を取る。
            if (!isPlayerRewinding && player.invulnTimer <= 0.0f) {
                for (const auto& gimHz : gimmicks) {
                    if (!gimHz.isActive) continue;
                    bool hitDeadlyPart = false;
                    float partCx = 0.0f;
                    for (const auto& part : gimHz.parts) {
                        if (!part.isActive || !part.deadly) continue;
                        float partHX, partHY, partW, partH;
                        // 複合オブジェクトのパーツ追従 — 判定矩形は親の倍率込みで1箇所にまとめてある
                        GetPartHitRect(part, partHX, partHY, partW, partH);
                        if (CheckCollision(player.x, player.y, pw_scaled, ph_scaled,
                                           partHX, partHY, partW, partH)) {
                            hitDeadlyPart = true;
                            partCx = partHX + partW / 2.0f;
                            break;
                        }
                    }
                    if (hitDeadlyPart) {
                        player.hp--;
                        player.invulnTimer = 60.0f;
                        // 当たったパーツから離れる向きへ弾き飛ばす（ハザードの上で連続被弾し続けるのを防ぐ）
                        float playerCx = player.x + pw_scaled / 2.0f;
                        player.vx = (playerCx < partCx ? -1.0f : 1.0f) * 6.0f;
                        player.vy = -4.0f;
                        const GimmickDef* gdefHz = FindGimmickDef(gimHz.assetId);
                        if (gdefHz) SoundManager::Get().PlaySe(gdefHz->seActivate);
                        if (player.hp <= 0) currentScene = RESULT_GAMEOVER;
                        break; // 同一フレームで複数のハザードに多重被弾させない
                    }
                }
            }

            // 2. プレイヤー vs 敵の衝突判定
            // consumePartOnAttack の敵はヒット時にパーツを消費するため、const参照では回せない
            if (!isPlayerRewinding) {
                for (auto& enemy : enemies) {
                    if (enemy.isActive && !isPlayerRewinding && player.invulnTimer <= 0.0f) {
                        // この敵が現在巻き戻し中の場合はスキップ
                        bool isThisEnemyRew = (isRKeyPressed && !isRotating && (selectedType == SELECT_NONE || (selectedType == SELECT_ENEMY && targetEnemy == &enemy)));
                        if (isThisEnemyRew) continue;

                        // Feature: 編集リアクション（共通層）—
                        // ・個別に一時停止した敵は「足場」になるので、乗った瞬間に被弾しては成立しない。
                        // ・個別に巻き戻し中の敵は過去の再生＝残像なので、すり抜けられる。
                        // ・型ごとに「編集されて無害化した状態」もここに含まれる（縮めたドッスン等）。
                        if (EnemyIsHarmless(enemy)) continue;

                        float ew_scaled = (float)enemy.hitboxWidth * enemy.scale;
                        float eh_scaled = (float)enemy.hitboxHeight * enemy.scale;
                        bool hitBody = CheckCollision(player.x, player.y, pw_scaled, ph_scaled, enemy.x, enemy.y, ew_scaled, eh_scaled);

                        // Feature: Composite Multi-Part Objects (Parts-M4) — パーツごとの接触ダメージ判定
                        bool hitPart = false;
                        for (const auto& part : enemy.parts) {
                            if (!part.isActive) continue;
                            float partHX, partHY, partW, partH;
                            // 複合オブジェクトのパーツ追従 — 判定矩形は親の倍率込みで1箇所にまとめてある
                            GetPartHitRect(part, partHX, partHY, partW, partH);
                            if (CheckCollision(player.x, player.y, pw_scaled, ph_scaled,
                                                partHX, partHY, partW, partH)) {
                                hitPart = true;
                                break;
                            }
                        }

                        // 本体・パーツのどちらかにヒットした時点で1ダメージのみ（同一フレームでの多重ヒットを防止）。
                        // 被弾後は無敵時間を与え、敵から離れる方向へノックバックさせる。
                        if (hitBody || hitPart) {
                            player.hp--;
                            player.invulnTimer = 60.0f;
                            float enemyCenterX = enemy.x + ew_scaled / 2.0f;
                            float playerCenterX = player.x + pw_scaled / 2.0f;
                            float knockDir = (playerCenterX < enemyCenterX) ? -1.0f : 1.0f;
                            player.vx = knockDir * 6.0f;
                            player.vy = -4.0f;
                            {
                                const EnemyDef* edef = FindEnemyDef(enemy.assetId);
                                if (edef) SoundManager::Get().PlaySe(edef->seAttack);

                                // 攻撃するたびに胴体を消費する敵（いもむし）の処理。
                                // 尾＝parts配列の末尾側なので、後ろから探して最初に見つかった生存パーツを落とす。
                                // こうすると「頭から順に短くなる」のではなく、尻尾から削れていく見た目になる。
                                // Feature: 編集リアクション — 弱点色のフィルタを掛けている間は節を消費しない。
                                // いもむしは「攻撃させて節を減らす」のが本来の攻略だが、
                                // 色フィルタで実体を歪めている間はその消耗を止められる。
                                // 節を温存させたまま通したい場面（後で足場として使いたい等）の逃げ道になる。
                                bool suppressConsume = (g_screenFx.colorFilter != 0 &&
                                                        g_screenFx.colorFilter == EnemyWeakColor(enemy.type));
                                if (edef != nullptr && edef->consumePartOnAttack && !suppressConsume) {
                                    bool consumed = false;
                                    for (int pi = (int)enemy.parts.size() - 1; pi >= 0; pi--) {
                                        if (enemy.parts[pi].isActive) {
                                            enemy.parts[pi].isActive = false;
                                            consumed = true;
                                            break;
                                        }
                                    }
                                    // 消費した結果、残っている節が1つも無くなったら力尽きて撃破される。
                                    // 「消費できる節が最初から無かった」場合も同じ扱いにしておくと、
                                    // パーツ定義を空にしたときに無敵の敵ができあがる事故を防げる。
                                    bool anyLeft = false;
                                    for (const auto& part : enemy.parts) { if (part.isActive) { anyLeft = true; break; } }
                                    if (!anyLeft) {
                                        enemy.isActive = false;
                                        SoundManager::Get().PlaySe(edef->seDeath);
                                    }
                                    (void)consumed;
                                }
                            }
                            if (player.hp <= 0) {
                                currentScene = RESULT_GAMEOVER;
                            }
                        }
                    }
                }
            }

            // 3. 弾 vs キャラクター/トゲ/ブロックの衝突判定
            for (int i = 0; i < MAX_BULLETS; i++) {
                if (bullets[i].isActive) {
                    if (bullets[i].isPlayerOwned) {
                        // プレイヤーの弾が敵にヒット
                        for (auto& enemy : enemies) {
                            if (enemy.isActive) {
                                bool isThisEnemyRew = (isRKeyPressed && !isRotating && (selectedType == SELECT_NONE || (selectedType == SELECT_ENEMY && targetEnemy == &enemy)));
                                if (isThisEnemyRew) continue;
                                // Feature: 編集リアクション（共通層）— 巻き戻し中の敵は残像なので弾も貫通する。
                                // 接触ダメージだけ無効で弾は当たる、という半端な状態にすると
                                // 「すり抜けられる相手」という理解と噛み合わなくなるため揃える。
                                if (enemy.isRewinding) continue;

                                float ew_scaled = (float)enemy.hitboxWidth * enemy.scale;
                                float eh_scaled = (float)enemy.hitboxHeight * enemy.scale;
                                if (CheckCollision(bullets[i].x, bullets[i].y, 16.0f * bullets[i].scale, 16.0f * bullets[i].scale, enemy.x, enemy.y, ew_scaled, eh_scaled)) {
                                    // Feature: 編集リアクション — 実体を持たない状態の敵は弾が素通りする
                                    // （まぼろしは色フィルタを合わせている間だけ撃てる）
                                    if (EnemyIsBulletProof(enemy)) continue;
                                    if (enemy.type == ENEMY_SHIELD && enemy.auxFlag) {
                                        // シールド発動中はダメージ無効（弾だけ消える）
                                        bullets[i].isActive = false;
                                        break;
                                    }
                                    enemy.hp--;
                                    const EnemyDef* edef = FindEnemyDef(enemy.assetId);
                                    // Feature: Composite Multi-Part Objects (Parts-M6) — OnDamaged/OnDeathの発火（ENEMY_CUSTOM_SCRIPTのみ。
                                    // scriptState(OnSpawn用)とは別のreactiveStateを使うため、通常挙動のForeverループは止まらない）
                                    Enemy* enemyForReact = &enemy;
                                    auto buildReactActor = [&]() {
                                        ScriptActor a;
                                        a.x = &enemyForReact->x; a.y = &enemyForReact->y;
                                        a.vx = &enemyForReact->vx; a.vy = &enemyForReact->vy;
                                        a.direction = &enemyForReact->direction; a.scale = &enemyForReact->scale; a.angle = &enemyForReact->angle;
                                        a.playerX = player.x; a.playerY = player.y;
                                        a.playSound = [](const std::string& slot) { SoundManager::Get().PlaySe(slot); };
                                        a.visualEffect = [&](const std::string& kind, float intensity) {
                                            if (kind == "brightness") Screen_SetBrightness(intensity);
                                            else if (kind == "zoom") Screen_SetZoom(intensity);
                                        };
                                        return a;
                                    };
                                    if (enemy.hp <= 0) {
                                        if (enemy.type == ENEMY_SHRINKER && !enemy.auxFlag) {
                                            // 分裂もどき：一度だけ縮小・高速化して復活する
                                            enemy.auxFlag = true;
                                            enemy.hp = 1;
                                            enemy.scale *= (edef ? edef->shrinkFactor : 0.6f);
                                            if (edef) SoundManager::Get().PlaySe(edef->seDamage);
                                        } else {
                                            enemy.isActive = false;      // 敵を倒す
                                            if (edef) SoundManager::Get().PlaySe(edef->seDeath);
                                            if (enemy.type == ENEMY_CUSTOM_SCRIPT && edef) {
                                                ScriptActor reactActor = buildReactActor();
                                                BehaviorInterpreter::FireReactiveHat(enemy.reactiveState, edef->script, "OnDeath", reactActor);
                                            }
                                        }
                                    } else if (edef) {
                                        SoundManager::Get().PlaySe(edef->seDamage);
                                        if (enemy.type == ENEMY_CUSTOM_SCRIPT) {
                                            ScriptActor reactActor = buildReactActor();
                                            BehaviorInterpreter::FireReactiveHat(enemy.reactiveState, edef->script, "OnDamaged", reactActor);
                                        }
                                    }
                                    bullets[i].isActive = false; // 弾を消滅
                                    break;
                                }

                                // Feature: Composite Multi-Part Objects (Parts-M4/M6) — 本体に当たらなかった場合のみパーツも判定
                                if (bullets[i].isActive) {
                                    const EnemyDef* edefForParts = FindEnemyDef(enemy.assetId);
                                    for (auto& part : enemy.parts) {
                                        if (!part.isActive) continue;
                                        float partHX, partHY, partW, partH;
                                        // 複合オブジェクトのパーツ追従 — 判定矩形は親の倍率込みで1箇所にまとめてある
                                        GetPartHitRect(part, partHX, partHY, partW, partH);
                                        if (CheckCollision(bullets[i].x, bullets[i].y, 16.0f * bullets[i].scale, 16.0f * bullets[i].scale,
                                                            partHX, partHY, partW, partH)) {
                                            if (part.hp > 0) {
                                                part.hp--;
                                                bool partDied = part.hp <= 0;
                                                if (partDied) part.isActive = false;
                                                if (edefForParts && part.partIndex >= 0 && part.partIndex < (int)edefForParts->parts.size()) {
                                                    ScriptActor reactActor;
                                                    reactActor.x = &part.x; reactActor.y = &part.y;
                                                    reactActor.scale = &part.scale; reactActor.angle = &part.angle;
                                                    reactActor.hasParent = true;
                                                    reactActor.parentX = enemy.x; reactActor.parentY = enemy.y;
                                                    reactActor.partIndex = part.partIndex;
                                                    reactActor.playerX = player.x; reactActor.playerY = player.y;
                                                    reactActor.playSound = [](const std::string& slot) { SoundManager::Get().PlaySe(slot); };
                                                    reactActor.visualEffect = [&](const std::string& kind, float intensity) {
                                                        if (kind == "brightness") Screen_SetBrightness(intensity);
                                                        else if (kind == "zoom") Screen_SetZoom(intensity);
                                                    };
                                                    const json& partScript = edefForParts->parts[part.partIndex].script;
                                                    BehaviorInterpreter::FireReactiveHat(part.reactiveState, partScript, partDied ? "OnDeath" : "OnDamaged", reactActor);
                                                }
                                            }
                                            bullets[i].isActive = false;
                                            break;
                                        }
                                    }
                                }
                                if (!bullets[i].isActive) break; // パーツに当たった場合もこの弾はもう他の敵を判定しない
                            }
                        }

                        // プレイヤーの弾が破壊可能なブロックにヒット
                        if (bullets[i].isActive) {
                            for (auto& gim : gimmicks) {
                                if (gim.type == GIMMICK_BREAKABLE_BLOCK && gim.isActive) {
                                    if (CheckCollision(bullets[i].x, bullets[i].y, 16.0f * bullets[i].scale, 16.0f * bullets[i].scale, gim.x, gim.y, gim.spriteWidth, gim.spriteHeight)) {
                                        gim.isActive = false;
                                        bullets[i].isActive = false;
                                        const GimmickDef* gdef = FindGimmickDef(gim.assetId);
                                        if (gdef) SoundManager::Get().PlaySe(gdef->seActivate);
                                        break;
                                    }
                                }
                            }
                        }

                        // Feature: Composite Multi-Part Objects (Parts-M4/M6) — プレイヤーの弾がギミックのパーツにヒット
                        if (bullets[i].isActive) {
                            for (auto& gim : gimmicks) {
                                if (!gim.isActive) continue;
                                const GimmickDef* gdefForPartHit = FindGimmickDef(gim.assetId);
                                for (auto& part : gim.parts) {
                                    if (!part.isActive) continue;
                                    float partHX, partHY, partW, partH;
                                    // 複合オブジェクトのパーツ追従 — 判定矩形は親の倍率込みで1箇所にまとめてある
                                    GetPartHitRect(part, partHX, partHY, partW, partH);
                                    if (CheckCollision(bullets[i].x, bullets[i].y, 16.0f * bullets[i].scale, 16.0f * bullets[i].scale,
                                                        partHX, partHY, partW, partH)) {
                                        if (part.hp > 0) {
                                            part.hp--;
                                            bool partDied = part.hp <= 0;
                                            if (partDied) part.isActive = false;
                                            if (gdefForPartHit && part.partIndex >= 0 && part.partIndex < (int)gdefForPartHit->parts.size()) {
                                                ScriptActor reactActor;
                                                reactActor.x = &part.x; reactActor.y = &part.y;
                                                reactActor.scale = &part.scale; reactActor.angle = &part.angle;
                                                reactActor.hasParent = true;
                                                reactActor.parentX = gim.x; reactActor.parentY = gim.y;
                                                reactActor.partIndex = part.partIndex;
                                                reactActor.playerX = player.x; reactActor.playerY = player.y;
                                                reactActor.playSound = [](const std::string& slot) { SoundManager::Get().PlaySe(slot); };
                                                reactActor.visualEffect = [&](const std::string& kind, float intensity) {
                                                    if (kind == "brightness") Screen_SetBrightness(intensity);
                                                    else if (kind == "zoom") Screen_SetZoom(intensity);
                                                };
                                                const json& partScript = gdefForPartHit->parts[part.partIndex].script;
                                                BehaviorInterpreter::FireReactiveHat(part.reactiveState, partScript, partDied ? "OnDeath" : "OnDamaged", reactActor);
                                            }
                                        }
                                        bullets[i].isActive = false;
                                        break;
                                    }
                                }
                                if (!bullets[i].isActive) break;
                            }
                        }
                    } else {
                        // 敵の弾がプレイヤーにヒット
                        if (!isPlayerRewinding) {
                            if (CheckCollision(bullets[i].x, bullets[i].y, 16.0f * bullets[i].scale, 16.0f * bullets[i].scale, player.x, player.y, pw_scaled, ph_scaled)) {
                                player.hp--;
                                if (player.hp <= 0) {
                                    currentScene = RESULT_GAMEOVER;
                                }
                                bullets[i].isActive = false;
                            }
                        }
                    }
                }
            }
        }

        // ===== ゴール判定 =====
        {
            const auto& curStage = stages[currentStageIdx];
            if (curStage.goalX >= 0 && currentScene == PLAY && !isPlayerRewinding) {
                float _pw = (float)player.width * player.scale;
                float _ph = (float)player.height * player.scale;
                float gx = curStage.goalX, gy = curStage.goalY;
                float gw = (float)TILE_SIZE, gh = (float)TILE_SIZE;
                if (CheckCollision(player.x, player.y, _pw, _ph, gx, gy, gw, gh)) {
                    currentScene = RESULT_VICTORY;
                }
            }
        }

        // ===== ゴールの描画 =====
        // 新素材移行 — 以前はプリミティブ描画（棒＋三角の旗）で組み立てていたが、
        // 専用の絵（img/ゴール.png）が用意されたのでそちらに差し替える。
        // 光柱と拡縮の脈動は「遠くからでも見つかる目印」として残す。
        {
            const auto& curStage = stages[currentStageIdx];
            if (curStage.goalX >= 0) {
                int gx = (int)(curStage.goalX - cameraX);
                int gy = (int)(curStage.goalY - cameraY);
                // 目印の光柱（ゴールの位置を遠くからでも分かるようにする）
                SetDrawBlendMode(DX_BLENDMODE_ALPHA, 40);
                DrawBox(gx + 6, 0, gx + TILE_SIZE - 6, gy + TILE_SIZE, GetColor(255, 240, 120), TRUE);
                SetDrawBlendMode(DX_BLENDMODE_NOBLEND, 0);
                // ゴール本体。ゆっくり脈動させて「ここが目的地」であることを目立たせる
                float goalPulse = 1.0f + sinf(BehaviorInterpreter::globalFrameCounter * 0.06f) * 0.06f;
                int goalSize = (int)(TILE_SIZE * 1.5f * goalPulse);
                DrawUiIcon(gx + TILE_SIZE / 2, gy + TILE_SIZE / 2, goalSize, goalHandle);
                DrawString(gx - 4, gy - TILE_SIZE - 18, "GOAL", GetColor(255, 255, 160));
            }
        }

        // ===== チェックポイントの描画 =====
        // 新素材移行 — 専用の旗の絵（img/チェックポイント.png）に差し替える。
        // 未発動と発動済みを絵柄で描き分けることはできないので、
        //   ・未発動 … 輝度を落として色褪せた旗にする
        //   ・発動済み … 光の柱を足元に出し、旗をわずかに上下させて「生きている」感を出す
        // という演出側の差で「ここが今の復帰地点か」を判別できるようにする。
        for (const auto& gim : gimmicks) {
            if (gim.type != GIMMICK_CHECKPOINT || !gim.isActive) continue;
            int cgx = (int)(gim.x - cameraX);
            int cgy = (int)(gim.y - cameraY);
            bool cpActive = gim.val1 > 0.5f;
            int flagBob = 0;
            if (cpActive) {
                // 発動済み：足元にうっすら発光する光の柱を出す
                SetDrawBlendMode(DX_BLENDMODE_ALPHA, 60);
                DrawBox(cgx + 4, cgy - TILE_SIZE, cgx + TILE_SIZE - 4, cgy + TILE_SIZE, GetColor(120, 255, 150), TRUE);
                SetDrawBlendMode(DX_BLENDMODE_NOBLEND, 0);
                flagBob = (int)(sinf(BehaviorInterpreter::globalFrameCounter * 0.09f + gim.x) * 2.0f);
            } else {
                SetDrawBright(150, 150, 155); // 未発動：色褪せて見えるよう輝度を落とす
            }
            // 旗は「棒の根元が配置マスの底」に来るよう、1マス分せり上げた位置を中心にして描く
            DrawUiIcon(cgx + TILE_SIZE / 2, cgy + flagBob, (int)(TILE_SIZE * 1.9f), checkpointHandle);
            SetDrawBright(255, 255, 255);
            DrawString(cgx - 10, cgy - TILE_SIZE - 18, cpActive ? "CHECKPOINT!" : "checkpoint", cpActive ? GetColor(150, 255, 180) : GetColor(180, 180, 185));
        }

        // ===== deadly タイル判定 =====
        if (currentScene == PLAY && !isPlayerRewinding) {
            float _pw = (float)player.width * player.scale;
            float _ph = (float)player.height * player.scale;
            int tileRow = (int)((player.y + _ph * 0.9f) / TILE_SIZE);
            int tileCol = (int)((player.x + _pw * 0.5f) / TILE_SIZE);
            const auto& curStageMap = stages[currentStageIdx].map;
            if (tileRow >= 0 && tileRow < (int)curStageMap.size() &&
                tileCol >= 0 && tileCol < (int)curStageMap[0].size()) {
                int tid = curStageMap[tileRow][tileCol];
                if (tid >= 0 && tid < (int)tileDefs.size() && tileDefs[tid].deadly) {
                    currentScene = RESULT_GAMEOVER;
                }
            }
        }

        // 空中足場の描画
        for (const auto& plat : platforms) {
            DrawBox((int)(plat.x1 - cameraX), (int)(plat.y1 - cameraY), (int)(plat.x2 - cameraX), (int)(plat.y2 - cameraY), GetColor(100, 80, 60), TRUE);
            DrawBox((int)(plat.x1 - cameraX), (int)(plat.y1 - cameraY), (int)(plat.x2 - cameraX), (int)(plat.y2 - cameraY), GetColor(160, 130, 90), FALSE);
        }

        // ステージギミックの描画
        for (const auto& gim : gimmicks) {
            if (!gim.isActive) continue;

            DrawPartsPass(gim.parts, cameraX, cameraY, true); // Feature: Composite Multi-Part Objects (Parts-M5) — zOrder<0のパーツを先に描画

            if (gim.type == GIMMICK_ROTATING_BRIDGE || gim.type == GIMMICK_MANUAL_BRIDGE) {
                // 橋を画像で描画 (回転とスケーリング)。カスタムスプライトが設定されていればそちらを優先する
                int useHandle = gim.handle >= 0 ? gim.handle : bridgeHandle;
                int imgW, imgH;
                GetGraphSize(useHandle, &imgW, &imgH);
                float gcx = gim.x + gim.spriteWidth / 2.0f;
                float gcy = gim.y + gim.spriteHeight / 2.0f;
                double extRateX = (double)gim.spriteWidth / imgW;
                double extRateY = (double)gim.spriteHeight / imgH;
                if (extRateY < 1.0) extRateY = 20.0 / imgH; // 薄すぎる場合は最低限の厚みを持たせる

                // DrawRotaGraph3 を使用して縦横別スケールで中心回転描画
                DrawRotaGraph3((int)(gcx - cameraX), (int)(gcy - cameraY), imgW / 2, imgH / 2, extRateX, extRateY, gim.angle, useHandle, TRUE, FALSE);

                // 回転軸を描画
                DrawCircle((int)(gcx - cameraX), (int)(gcy - cameraY), 5, GetColor(255, 255, 255), TRUE);

                // 一時停止/巻き戻し中の橋の頭上インジケータ
                if (gim.isPaused) DrawString((int)(gim.x - cameraX), (int)(gim.y - cameraY) - 20, "|| PAUSE", GetColor(255, 255, 100));
                else if (gim.isRewinding) DrawString((int)(gim.x - cameraX), (int)(gim.y - cameraY) - 20, "<< REW", GetColor(255, 100, 100));
            }
            else if (gim.type == GIMMICK_BREAKABLE_BLOCK) {
                // ヒビの入った石ブロック画像をスケーリングして描画（カスタムスプライト優先）。
                // 編集リアクションで傾けられると崩れるので、崩れる寸前の傾きが見えるよう回転描画する。
                int useHandle = gim.handle >= 0 ? gim.handle : breakableBlockHandle;
                DrawGimmickRotated(gim, useHandle, cameraX, cameraY,
                                   gim.x, gim.y, gim.spriteWidth, gim.spriteHeight);
            }
            else if (gim.type == GIMMICK_FALLING_LIFT) {
                // リフト画像を描画（カスタムスプライト優先）
                int useHandle = gim.handle >= 0 ? gim.handle : liftHandle;
                int x1 = (int)(gim.x - cameraX);
                int y1 = (int)(gim.y - cameraY);
                int x2 = (int)(gim.x + gim.spriteWidth - cameraX);
                int y2 = (int)(gim.y + gim.spriteHeight - cameraY);
                DrawExtendGraph(x1, y1, x2, y2, useHandle, TRUE);
                if (gim.isPaused) DrawString(x1, y1 - 20, "|| PAUSE", GetColor(255, 255, 100));
                else if (gim.isRewinding) DrawString(x1, y1 - 20, "<< REW", GetColor(255, 100, 100));
            }
            else if (gim.type == GIMMICK_REFLECT_MIRROR) {
                // 反射ミラー画像を描画 (回転とスケーリング)（カスタムスプライト優先）
                int useHandle = gim.handle >= 0 ? gim.handle : mirrorHandle;
                int imgW, imgH;
                GetGraphSize(useHandle, &imgW, &imgH);
                float gcx = gim.x + gim.spriteWidth / 2.0f;
                float gcy = gim.y + gim.spriteHeight / 2.0f;
                double extRateX = (double)gim.spriteWidth / imgW;
                double extRateY = 24.0 / imgH; // ミラーの厚み

                DrawRotaGraph3((int)(gcx - cameraX), (int)(gcy - cameraY), imgW / 2, imgH / 2, extRateX, extRateY, gim.angle, useHandle, TRUE, FALSE);
                DrawCircle((int)(gcx - cameraX), (int)(gcy - cameraY), 4, GetColor(255, 255, 0), TRUE);
                if (gim.isPaused) DrawString((int)(gim.x - cameraX), (int)(gim.y - cameraY) - 35, "|| PAUSE", GetColor(255, 255, 100));
            }
            else if (gim.type == GIMMICK_WEIGHT_SWITCH) {
                // 重量スイッチ画像を描画（カスタムスプライト優先）
                int useHandle = gim.handle >= 0 ? gim.handle : switchHandle;
                int x1 = (int)(gim.x - cameraX);
                int y1 = (int)(gim.y - cameraY);
                int x2 = (int)(gim.x + gim.spriteWidth - cameraX);
                int y2 = (int)(gim.y + gim.spriteHeight - cameraY);

                bool isActiveState = false;
                for (const auto& other : gimmicks) {
                    if (other.type == GIMMICK_GATE_DOOR) {
                        isActiveState = !other.isActive;
                        break;
                    }
                }

                // 新ギミックロスター対応 — 作動中は spriteAlt（押し込まれたスイッチの絵）へ差し替える。
                // spriteAlt が未設定の定義では従来どおり緑の色味を乗せて「入っている」ことを示す。
                const GimmickDef* swDef = FindGimmickDef(gim.assetId);
                bool hasAltSprite = (isActiveState && swDef != nullptr && swDef->graphHandleAlt >= 0);
                if (hasAltSprite) {
                    useHandle = swDef->graphHandleAlt;
                } else if (isActiveState) {
                    SetDrawBright(100, 255, 100); // 起動時は緑っぽく
                }
                DrawExtendGraph(x1, y1, x2, y2, useHandle, TRUE);
                SetDrawBright(255, 255, 255);
            }
            else if (gim.type == GIMMICK_SCALABLE_BOX) {
                // 拡大可能ブロック画像を描画（カスタムスプライト優先）
                int useHandle = gim.handle >= 0 ? gim.handle : boxHandle;
                int x1 = (int)(gim.x - cameraX);
                int y1 = (int)(gim.y - cameraY);
                int x2 = (int)(gim.x + gim.spriteWidth - cameraX);
                int y2 = (int)(gim.y + gim.spriteHeight - cameraY);
                DrawExtendGraph(x1, y1, x2, y2, useHandle, TRUE);
                if (gim.isPaused) DrawString(x1, y1 - 20, "|| PAUSE", GetColor(255, 255, 100));
                else if (gim.isRewinding) DrawString(x1, y1 - 20, "<< REW", GetColor(255, 100, 100));
            }
            else if (gim.type == GIMMICK_GATE_DOOR) {
                // ゲートドア画像を描画（カスタムスプライト優先）。
                // 編集リアクションで倒すと足場になるため、倒れている姿勢がそのまま見えるように回転描画する。
                int useHandle = gim.handle >= 0 ? gim.handle : doorHandle;
                DrawGimmickRotated(gim, useHandle, cameraX, cameraY,
                                   gim.x, gim.y, gim.spriteWidth, gim.spriteHeight);
                if (GimmickIsTipped(gim)) {
                    // 倒して足場に転用中であることを明示する
                    DrawString((int)(gim.x - cameraX), (int)(gim.y - cameraY) - 20, "FLOOR", UiInkOk());
                }
            }
            else if (gim.type == GIMMICK_CUT_PORTAL && !gim.isTimelineCut) {
                // Feature: ポータルの作り直し（友人フィードバック対応）— タイムライン比率(val1/val2)ではなく、
                // 他のギミックと同じくgim.x/gim.yに基づいて通常のスプライト描画を行う。
                // Feature: カット機能の復活 — タイムラインカットはワールド座標を持たない（x,y,w,hが全て0）ので
                // ここでは描かず、下部パネルのタイムライン帯にだけ帯として描画する。
                int useHandle = gim.handle >= 0 ? gim.handle : portalHandle;
                int x1 = (int)(gim.x - cameraX);
                int y1 = (int)(gim.y - cameraY);
                int x2 = (int)(gim.x + gim.spriteWidth - cameraX);
                int y2 = (int)(gim.y + gim.spriteHeight - cameraY);
                DrawExtendGraph(x1, y1, x2, y2, useHandle, TRUE);
            }
            else if (gim.type == GIMMICK_SPIKES) {
                // トゲトゲ画像を描画（カスタムスプライト優先）。
                // 45度以上倒すと刺が横を向いて無害な足場に変わるので、
                // 「今は刺さるのか、乗れるのか」が姿勢だけで読めるよう必ず回転描画する。
                int useHandle = gim.handle >= 0 ? gim.handle : spikesHandle;
                bool spikeSafe = GimmickIsTipped(gim);
                if (spikeSafe) SetDrawBright(150, 200, 255); // 無害化中は青みがかった色で示す
                DrawGimmickRotated(gim, useHandle, cameraX, cameraY,
                                   gim.x, gim.y, gim.spriteWidth, gim.spriteHeight);
                if (spikeSafe) {
                    SetDrawBright(255, 255, 255);
                    DrawString((int)(gim.x - cameraX), (int)(gim.y - cameraY) - 18, "SAFE", UiInkOk());
                }
            }
            else if (gim.type == GIMMICK_SCALABLE_GROUND) {
                // 画像サイズに合わせてタイリング描画する（カスタムスプライト優先）
                int useHandle = gim.handle >= 0 ? gim.handle : jimenHandle;
                int imgW, imgH;
                GetGraphSize(useHandle, &imgW, &imgH);
                float tileW = gim.spriteHeight * ((float)imgW / imgH);
                
                int startX = (int)(gim.x - cameraX);
                int y1 = (int)(gim.y - cameraY);
                int y2 = (int)(gim.y + gim.spriteHeight - cameraY);
                
                for (float x = 0; x < gim.spriteWidth; x += tileW) {
                    float drawW = tileW;
                    if (x + tileW > gim.spriteWidth) drawW = gim.spriteWidth - x;
                    if (drawW <= 0.0f) break;
                    
                    int x1 = startX + (int)x;
                    int x2 = startX + (int)(x + drawW);
                    int srcW = (int)((drawW / tileW) * imgW);
                    
                    DrawRectExtendGraph(x1, y1, x2, y2, 0, 0, srcW, imgH, useHandle, TRUE);
                }
            }
            else if (gim.type == GIMMICK_CHIKUWA_BLOCK) {
                // ちくわブロックの描画
                float shakeX = 0.0f;
                // 乗られていて、まだ落ちていない時はカタカタ揺らす
                if (gim.customTimer > 0 && gim.angle == 0.0f) shakeX = sinf(gim.customTimer * 1.0f) * 3.0f;
                
                int cx1 = (int)(gim.x + shakeX - cameraX);
                int cy1 = (int)(gim.y - cameraY);
                int cx2 = (int)(gim.x + gim.spriteWidth + shakeX - cameraX);
                int cy2 = (int)(gim.y + gim.spriteHeight - cameraY);
                if (gim.handle >= 0) {
                    DrawExtendGraph(cx1, cy1, cx2, cy2, gim.handle, TRUE);
                } else {
                    DrawBox(cx1, cy1, cx2, cy2, GetColor(222, 184, 135), TRUE);
                    DrawBox(cx1, cy1, cx2, cy2, GetColor(139, 69, 19), FALSE);
                }
            }
            else if (gim.type == GIMMICK_TIME_FIELD) {
                // Feature: TIME_FIELDの見た目変更（友人フィードバック対応）— 単なる円塗りではなく、
                // 中央に棒を配置し、そこから円状にエネルギーが脈動しながら放出されている表現にする。
                // アニメーションはBehaviorInterpreter::globalFrameCounter（ポーズ中も止まらず加算され続ける）を
                // 使うことで、「この一時停止装置の中だけ時間が流れ続ける」というギミックの意味と一致させる。
                float radius = gim.val1 > 0 ? gim.val1 : 100.0f;
                int centerX = (int)(gim.x - cameraX);
                int centerY = (int)(gim.y - cameraY);

                // 範囲全体のうっすらとした塗り（従来通り範囲を把握しやすくする）
                SetDrawBlendMode(DX_BLENDMODE_ALPHA, 25);
                DrawCircle(centerX, centerY, (int)radius, GetColor(0, 255, 255), TRUE);
                SetDrawBlendMode(DX_BLENDMODE_NOBLEND, 0);

                // 同心円状に脈動しながら広がるエネルギーリング
                const int ringCount = 3;
                for (int i = 0; i < ringCount; i++) {
                    float phase = fmodf(BehaviorInterpreter::globalFrameCounter * 1.5f + (float)i * (100.0f / ringCount * 3.0f), 300.0f) / 300.0f; // 0.0〜1.0を繰り返す
                    float ringRadius = radius * phase;
                    int alpha = (int)(180 * (1.0f - phase));
                    if (alpha < 0) alpha = 0;
                    SetDrawBlendMode(DX_BLENDMODE_ALPHA, alpha);
                    DrawCircle(centerX, centerY, (int)ringRadius, GetColor(0, 255, 255), FALSE);
                }
                SetDrawBlendMode(DX_BLENDMODE_NOBLEND, 0);

                // 中央の棒（縦棒、エネルギーの発生源）
                int rodHalfHeight = (int)(radius * 0.35f);
                if (rodHalfHeight < 8) rodHalfHeight = 8;
                DrawLine(centerX - 1, centerY - rodHalfHeight, centerX - 1, centerY + rodHalfHeight, GetColor(0, 200, 200));
                DrawLine(centerX + 1, centerY - rodHalfHeight, centerX + 1, centerY + rodHalfHeight, GetColor(0, 200, 200));
                DrawLine(centerX, centerY - rodHalfHeight, centerX, centerY + rodHalfHeight, GetColor(220, 255, 255));
            }
            else if (gim.type == GIMMICK_CHOMPER) {
                // 敵食いギミックの描画（紫色のボックス）
                int cx1 = (int)(gim.x - cameraX);
                int cy1 = (int)(gim.y - cameraY);
                int cx2 = (int)(gim.x + gim.spriteWidth - cameraX);
                int cy2 = (int)(gim.y + gim.spriteHeight - cameraY);
                if (gim.handle >= 0) DrawExtendGraph(cx1, cy1, cx2, cy2, gim.handle, TRUE);
                else DrawBox(cx1, cy1, cx2, cy2, GetColor(128, 0, 128), TRUE);
            }
            else if (gim.type == GIMMICK_MOVING_PLATFORM || gim.type == GIMMICK_FRAMESTEP_LIFT) {
                // 動く足場／コマ送りリフトの描画（カスタムスプライト優先、無ければ木目調の板）
                int mx1 = (int)(gim.x - cameraX);
                int my1 = (int)(gim.y - cameraY);
                int mx2 = (int)(gim.x + gim.spriteWidth - cameraX);
                int my2 = (int)(gim.y + gim.spriteHeight - cameraY);
                if (gim.handle >= 0) {
                    DrawExtendGraph(mx1, my1, mx2, my2, gim.handle, TRUE);
                } else {
                    DrawBox(mx1, my1, mx2, my2, GetColor(180, 140, 90), TRUE);
                    DrawBox(mx1, my1, mx2, my2, GetColor(90, 60, 30), FALSE);
                }
                if (gim.type == GIMMICK_FRAMESTEP_LIFT) DrawString(mx1, my1 - 18, "STEP", GetColor(255, 255, 0));
            }
            else if (gim.type == GIMMICK_PUSHABLE_ROCK) {
                // 岩の描画（カスタムスプライト優先）。
                // 縮小すると転がり出すので、転がっている回転が見えるように回転描画する。
                int rx1 = (int)(gim.x - cameraX);
                int ry1 = (int)(gim.y - cameraY);
                int rx2 = (int)(gim.x + gim.spriteWidth - cameraX);
                int ry2 = (int)(gim.y + gim.spriteHeight - cameraY);
                if (gim.handle >= 0) {
                    DrawGimmickRotated(gim, gim.handle, cameraX, cameraY,
                                       gim.x, gim.y, gim.spriteWidth, gim.spriteHeight);
                } else {
                    DrawBox(rx1, ry1, rx2, ry2, GetColor(120, 120, 125), TRUE);
                    DrawBox(rx1, ry1, rx2, ry2, GetColor(60, 60, 65), FALSE);
                }
            }
            else if (gim.type == GIMMICK_FASTFORWARD_GATE) {
                // 早送りゲート：非アクティブ（通過可能＝早送り中）の間は半透明に（カスタムスプライト優先）
                int fx1 = (int)(gim.x - cameraX);
                int fy1 = (int)(gim.y - cameraY);
                int fx2 = (int)(gim.x + gim.spriteWidth - cameraX);
                int fy2 = (int)(gim.y + gim.spriteHeight - cameraY);
                if (!gim.isActive) SetDrawBlendMode(DX_BLENDMODE_ALPHA, 90);
                if (gim.handle >= 0) DrawExtendGraph(fx1, fy1, fx2, fy2, gim.handle, TRUE);
                else DrawBox(fx1, fy1, fx2, fy2, GetColor(0, 200, 255), TRUE);
                if (!gim.isActive) SetDrawBlendMode(DX_BLENDMODE_NOBLEND, 0);
                DrawString(fx1, fy1 - 18, "FF GATE", GetColor(0, 200, 255));
            }
            else if (gim.type == GIMMICK_COLOR_LOCK_PLATFORM) {
                // 色ロック足場：実体化中は不透明、不一致中は色付きの半透明ゴーストで位置だけ示す
                int requiredColor = gim.param.empty() ? 1 : atoi(gim.param.c_str());
                int lockColor = requiredColor == 2 ? GetColor(80, 220, 80)
                               : requiredColor == 3 ? GetColor(80, 140, 255)
                                                     : GetColor(255, 90, 90);
                int lx1 = (int)(gim.x - cameraX);
                int ly1 = (int)(gim.y - cameraY);
                int lx2 = (int)(gim.x + gim.spriteWidth - cameraX);
                int ly2 = (int)(gim.y + gim.spriteHeight - cameraY);
                if (gim.isActive) {
                    DrawBox(lx1, ly1, lx2, ly2, lockColor, TRUE);
                    DrawBox(lx1, ly1, lx2, ly2, GetColor(255, 255, 255), FALSE);
                } else {
                    SetDrawBlendMode(DX_BLENDMODE_ALPHA, 60);
                    DrawBox(lx1, ly1, lx2, ly2, lockColor, TRUE);
                    SetDrawBlendMode(DX_BLENDMODE_NOBLEND, 0);
                }
            }
            else if (gim.type == GIMMICK_BRIGHTNESS_LOCK_PLATFORM) {
                // 明暗ロック足場：実体化中は不透明、不一致中は半透明のゴーストで位置だけ示す
                bool wantsBright = (gim.param == "bright");
                int lockColor = wantsBright ? GetColor(255, 240, 150) : GetColor(60, 60, 90);
                int bx1 = (int)(gim.x - cameraX);
                int by1 = (int)(gim.y - cameraY);
                int bx2 = (int)(gim.x + gim.spriteWidth - cameraX);
                int by2 = (int)(gim.y + gim.spriteHeight - cameraY);
                if (gim.isActive) {
                    DrawBox(bx1, by1, bx2, by2, lockColor, TRUE);
                    DrawBox(bx1, by1, bx2, by2, GetColor(255, 255, 255), FALSE);
                } else {
                    SetDrawBlendMode(DX_BLENDMODE_ALPHA, 60);
                    DrawBox(bx1, by1, bx2, by2, lockColor, TRUE);
                    SetDrawBlendMode(DX_BLENDMODE_NOBLEND, 0);
                }
            }
            else if (gim.type == GIMMICK_BRIGHTNESS_ZONE || gim.type == GIMMICK_COLOR_ZONE
                     || gim.type == GIMMICK_ZOOM_LENS || gim.type == GIMMICK_SLOWMO_FIELD) {
                // 画面エフェクトゾーンの範囲を半透明の枠で可視化
                int zx1 = (int)(gim.x - cameraX);
                int zy1 = (int)(gim.y - cameraY);
                int zx2 = (int)(gim.x + gim.spriteWidth - cameraX);
                int zy2 = (int)(gim.y + gim.spriteHeight - cameraY);
                int zoneColor = gim.type == GIMMICK_BRIGHTNESS_ZONE ? GetColor(255, 255, 150)
                               : gim.type == GIMMICK_COLOR_ZONE     ? GetColor(220, 120, 255)
                               : gim.type == GIMMICK_ZOOM_LENS      ? GetColor(120, 220, 255)
                                                                      : GetColor(160, 160, 255);
                SetDrawBlendMode(DX_BLENDMODE_ALPHA, 50);
                DrawBox(zx1, zy1, zx2, zy2, zoneColor, TRUE);
                SetDrawBlendMode(DX_BLENDMODE_NOBLEND, 0);
                DrawBox(zx1, zy1, zx2, zy2, zoneColor, FALSE);
            }
            else if (gim.type == GIMMICK_CUSTOM_SCRIPT) {
                // Feature: Puzzle-like Behavior Scripting (M2) — カスタムスプライトがあればそれを、無ければ簡易プレースホルダーを描画
                int cx1 = (int)(gim.x - cameraX);
                int cy1 = (int)(gim.y - cameraY);
                int cx2 = (int)(gim.x + gim.spriteWidth - cameraX);
                int cy2 = (int)(gim.y + gim.spriteHeight - cameraY);
                if (gim.handle >= 0) {
                    DrawExtendGraph(cx1, cy1, cx2, cy2, gim.handle, TRUE);
                } else {
                    DrawBox(cx1, cy1, cx2, cy2, GetColor(0, 255, 255), TRUE);
                    DrawBox(cx1, cy1, cx2, cy2, GetColor(255, 255, 255), FALSE);
                }
                if (isDebugDrawMode) {
                    const char* status = gim.scriptState.faulted ? "FAULT" : gim.scriptState.finished ? "DONE" : "RUN";
                    DrawFormatString(cx1, cy1 - 16, GetColor(0, 255, 255),
                        "SCRIPT:%s stack=%d wait=%d", status, (int)gim.scriptState.callStack.size(), gim.scriptState.waitFramesRemaining);
                    if (gim.scriptState.faulted) {
                        DrawFormatString(cx1, cy1 - 32, GetColor(255, 60, 60), "ERR: %s", gim.scriptState.faultMsg.c_str());
                    }
                }
                // Feature: Puzzle-like Behavior Scripting (M7) — デバッグモードでなくても、
                // スクリプトがFaulted状態（不正なJSON等で停止）になった個体は常に警告マークを出す
                if (gim.scriptState.faulted) {
                    DrawFormatString(cx1, cy1 - (isDebugDrawMode ? 48 : 16), GetColor(255, 60, 60), "SCRIPT ERROR");
                }
            }

            DrawPartsPass(gim.parts, cameraX, cameraY, false); // Feature: Composite Multi-Part Objects (Parts-M5) — zOrder>=0のパーツを後に描画
        }

        // 共通レイヤー描画ラムダ
        auto DrawLayer = [&](const std::vector<std::vector<int>>& mapToDraw) {
            int mapRowCount = (int)mapToDraw.size();
            int mapColCount = mapRowCount > 0 ? (int)mapToDraw[0].size() : 0;
            for (int ty = 0; ty < mapRowCount; ty++) {
                for (int tx = 0; tx < mapColCount; tx++) {
                    int tid = mapToDraw[ty][tx];
                    if (tid <= 0 || tid >= (int)tileDefs.size()) continue;
                    if (tileDefs[tid].handle < 0) continue;
                    int drawX = tx * TILE_SIZE - (int)cameraX;
                    int drawY = ty * TILE_SIZE - (int)cameraY;
                    if (drawX >= -TILE_SIZE && drawX < SCREEN_WIDTH && drawY >= -TILE_SIZE && drawY < SCREEN_HEIGHT) {
                        // Feature: タイル表示範囲調整機能 — タイルセット画像から指定範囲(srcX/Y/W/H)だけを切り出して描画する
                        DrawRectExtendGraph(drawX, drawY, drawX + TILE_SIZE, drawY + TILE_SIZE,
                            tileDefs[tid].srcX, tileDefs[tid].srcY, tileDefs[tid].srcW, tileDefs[tid].srcH,
                            tileDefs[tid].handle, TRUE);
                    }
                }
            }
        };

        // Feature 1: 背景レイヤー（遠景）の描画
        for (const auto& bl : stages[currentStageIdx].backgrounds) {
            if (bl.handle >= 0) {
                int imgW, imgH;
                GetGraphSize(bl.handle, &imgW, &imgH);
                if (imgW > 0 && imgH > 0) {
                    float parallaxCamX = cameraX * bl.scrollRate;
                    float parallaxCamY = cameraY * bl.scrollRate; // 縦スクロール対応：横と同じ比率で背景を追従させる
                    float drawX = -fmod(parallaxCamX, (float)imgW) + bl.offsetX;
                    float drawY = -parallaxCamY + bl.offsetY;
                    DrawGraph((int)drawX, (int)drawY, bl.handle, TRUE);
                    if (bl.loop) {
                        DrawGraph((int)(drawX + imgW), (int)drawY, bl.handle, TRUE);
                    }
                }
            }
        }

        // Feature 1: 装飾レイヤー (背面) の描画
        DrawLayer(stages[currentStageIdx].decoMapBack);

        // タイルマップの描画（可変サイズ対応）
        const auto& currentMap = stages[currentStageIdx].map;
        int mapRowCount = (int)currentMap.size();
        int mapColCount = mapRowCount > 0 ? (int)currentMap[0].size() : 0;
        for (int ty = 0; ty < mapRowCount; ty++) {
            for (int tx = 0; tx < mapColCount; tx++) {
                int tid = currentMap[ty][tx];
                if (tid <= 0 || tid >= (int)tileDefs.size()) continue;
                if (tileDefs[tid].handle < 0) continue;
                int drawX = tx * TILE_SIZE - (int)cameraX;
                int drawY = ty * TILE_SIZE - (int)cameraY;
                if (drawX >= -TILE_SIZE && drawX < SCREEN_WIDTH && drawY >= -TILE_SIZE && drawY < SCREEN_HEIGHT) {
                    // Feature: タイル表示範囲調整機能 — タイルセット画像から指定範囲(srcX/Y/W/H)だけを切り出して描画する
                    DrawRectExtendGraph(drawX, drawY, drawX + TILE_SIZE, drawY + TILE_SIZE,
                        tileDefs[tid].srcX, tileDefs[tid].srcY, tileDefs[tid].srcW, tileDefs[tid].srcH,
                        tileDefs[tid].handle, TRUE);
                    if (isDebugDrawMode && tileDefs[tid].isCollidable) {
                        DrawBox(drawX, drawY, drawX + TILE_SIZE, drawY + TILE_SIZE, GetColor(0, 255, 255), FALSE);
                    }
                }
            }
        }

        // Feature 1: 装飾レイヤー (前面) の描画
        DrawLayer(stages[currentStageIdx].decoMapFront);

        // 前の床の描画は完全に削除されました

        // アイテムの描画（assetIdごとのスプライトがあればそれを使い、無ければcoinHandleにフォールバック）
        for (const auto& item : items) {
            if (item.isActive && !item.isCollected) {
                DrawPartsPass(item.parts, cameraX, cameraY, true); // Feature: Composite Multi-Part Objects (Parts-M5)
                int icx = (int)(item.x - cameraX);
                int icy = (int)(item.y - cameraY);
                int useHandle = item.handle >= 0 ? item.handle : coinHandle;
                DrawExtendGraph(icx, icy, icx + (int)item.spriteWidth, icy + (int)item.spriteHeight, useHandle, TRUE);
                DrawPartsPass(item.parts, cameraX, cameraY, false); // Feature: Composite Multi-Part Objects (Parts-M5)
            }
        }

        // 敵の描画
        for (const auto& enemy : enemies) {
            if (enemy.isActive) {
                // 敵の動き大幅改良プラン Phase 1-A: FALLER(どっすん)の落下予兆テレグラフ。
                // 溜めフェーズ(auxState==1かつcustomTimer>0)の間、影を徐々に濃く・大きく表示して
                // 「もうすぐ落ちる」を視覚化する。スローモーション中はetsが小さくなり自然にゆっくり育つため
                // 見切りやすくなり、早送り中は左右にジッターさせて正確な読みを難しくする（編集機能との連動）。
                if (enemy.type == ENEMY_FALLER && enemy.auxState == 1 && enemy.customTimer > 0.0f) {
                    const EnemyDef* fallerTelDef = FindEnemyDef(enemy.assetId);
                    float fallDelayTel = fallerTelDef ? fallerTelDef->fallDelay : 10.0f;
                    if (fallDelayTel < 1.0f) fallDelayTel = 1.0f;
                    float telProgress = 1.0f - (enemy.customTimer / fallDelayTel);
                    if (telProgress < 0.0f) telProgress = 0.0f; if (telProgress > 1.0f) telProgress = 1.0f;
                    float maxShadowR = (fallerTelDef && fallerTelDef->shockwaveRadius > 0.0f) ? fallerTelDef->shockwaveRadius : 60.0f;
                    int shadowCx = (int)(enemy.x + (enemy.hitboxWidth * enemy.scale) / 2.0f - cameraX);
                    int shadowCy = (int)(enemy.y + enemy.hitboxHeight * enemy.scale - cameraY);
                    if (isFastForward) shadowCx += (int)(sinf(BehaviorInterpreter::globalFrameCounter * 0.9f) * 10.0f);
                    int shadowR = (int)(maxShadowR * telProgress);
                    if (shadowR > 0) {
                        SetDrawBlendMode(DX_BLENDMODE_ALPHA, (int)(160 * telProgress));
                        DrawCircle(shadowCx, shadowCy, shadowR, GetColor(255, 60, 60), TRUE);
                        SetDrawBlendMode(DX_BLENDMODE_NOBLEND, 0);
                    }
                }
                DrawPartsPass(enemy.parts, cameraX, cameraY, true); // Feature: Composite Multi-Part Objects (Parts-M5)
                int imgW, imgH;
                GetGraphSize(enemy.handle, &imgW, &imgH);
                int ecx = (int)(enemy.x + (enemy.hitboxWidth * enemy.scale) / 2.0f - cameraX);
                int ecy = (int)(enemy.y - cameraY + (enemy.hitboxHeight * enemy.scale) / 2.0f);
                // 新敵ロスター対応 — 以前はここで全ての敵に一律 SetDrawBright(255,120,120) の赤い色味を
                // 掛けていた。全員が同じ img/enemy.png を使っていた頃は「赤い＝敵」という唯一の見分けだったが、
                // 種類ごとに色も形も違う専用素材へ移行した今は、緑のいもむし・青い砲台・黄色いハニカム・
                // 紫の幽霊が全部くすんだ赤に染まってしまい、せっかくの描き分けが潰れる。
                // さらにパーツ(DrawPartsPass)はこの色味の外で描かれるため、
                // 「胴体だけ緑で頭だけ赤い」といった本体とパーツの食い違いも起きていた。
                // よって通常時は素材の色をそのまま出し、色味を乗せるのは
                // 「今どういう状態か」を示す必要がある無敵中(SHIELD)だけに限定する。
                if (enemy.type == ENEMY_SHIELD && enemy.auxFlag) SetDrawBright(255, 230, 100); // 無敵中は金色に発光
                else SetDrawBright(255, 255, 255);
                if (enemy.anim.HasClip(enemy.anim.currentClip)) {
                    // animations.jsonにこの敵のクリップが定義されていればスプライトシートアニメーションで描画
                    int animH = enemy.anim.GetCurrentFrameHeight();
                    int animCy = (int)(enemy.y - cameraY + (animH * enemy.scale) / 2.0f);
                    enemy.anim.DrawAt(ecx, animCy, enemy.scale, enemy.angle, enemy.direction != 0);
                } else if (enemy.direction == 0) {
                    // 新アセット移行対応 — 640x640の素材を敵定義の表示サイズへ収める倍率を掛ける
                    DrawRotaGraph(ecx, ecy, ComputeFitScale(enemy.handle, (float)enemy.width, (float)enemy.height) * enemy.scale, enemy.angle, enemy.handle, TRUE);
                } else {
                    DrawRotaGraph(ecx, ecy, ComputeFitScale(enemy.handle, (float)enemy.width, (float)enemy.height) * enemy.scale, enemy.angle, enemy.handle, TRUE, TRUE);
                }
                SetDrawBright(255, 255, 255); // 輝度リセット
                
                if (isDebugDrawMode) {
                    DrawBox((int)(enemy.x - cameraX), (int)(enemy.y - cameraY), (int)(enemy.x + enemy.hitboxWidth * enemy.scale - cameraX), (int)(enemy.y + enemy.hitboxHeight * enemy.scale - cameraY), GetColor(0, 255, 0), FALSE); // 当たり判定
                    DrawBox((int)(ecx - (imgW * enemy.scale) / 2.0f), (int)(ecy - (imgH * enemy.scale) / 2.0f), (int)(ecx + (imgW * enemy.scale) / 2.0f), (int)(ecy + (imgH * enemy.scale) / 2.0f), GetColor(255, 0, 0), FALSE); // 描画枠
                }

                bool isThisEnemyRew = enemy.isRewinding || (isRKeyPressed && !isRotating && (selectedType == SELECT_NONE || (selectedType == SELECT_ENEMY && targetEnemy == &enemy)));
                if (isThisEnemyRew) DrawString((int)(enemy.x - cameraX), (int)(enemy.y - cameraY) - 20, "<< REW", GetColor(255, 100, 100));
                else if (enemy.isPaused) DrawString((int)(enemy.x - cameraX), (int)(enemy.y - cameraY) - 20, "|| PAUSE", GetColor(255, 255, 100));
                
                // 敵HP表示
                if (enemy.hp > 0) {
                    DrawFormatString((int)(enemy.x - cameraX), (int)(enemy.y - cameraY) - 40, GetColor(255, 0, 0), "HP:%d", enemy.hp);
                }

                // Feature: Puzzle-like Behavior Scripting (M2/M7) — スクリプト実行状態の表示
                if (isDebugDrawMode && enemy.type == ENEMY_CUSTOM_SCRIPT) {
                    const char* status = enemy.scriptState.faulted ? "FAULT" : enemy.scriptState.finished ? "DONE" : "RUN";
                    DrawFormatString((int)(enemy.x - cameraX), (int)(enemy.y - cameraY) - 56, GetColor(0, 255, 255),
                        "SCRIPT:%s stack=%d wait=%d", status, (int)enemy.scriptState.callStack.size(), enemy.scriptState.waitFramesRemaining);
                    if (enemy.scriptState.faulted) {
                        DrawFormatString((int)(enemy.x - cameraX), (int)(enemy.y - cameraY) - 72, GetColor(255, 60, 60), "ERR: %s", enemy.scriptState.faultMsg.c_str());
                    }
                }
                // デバッグモードでなくても、Faulted状態の敵は常に警告マークを出す（M7）
                if (enemy.type == ENEMY_CUSTOM_SCRIPT && enemy.scriptState.faulted) {
                    DrawFormatString((int)(enemy.x - cameraX), (int)(enemy.y - cameraY) - (isDebugDrawMode ? 88 : 56), GetColor(255, 60, 60), "SCRIPT ERROR");
                }

                DrawPartsPass(enemy.parts, cameraX, cameraY, false); // Feature: Composite Multi-Part Objects (Parts-M5)
            }
        }

        // プレイヤーの描画
        int cx = (int)(player.x + (player.width * player.scale) / 2.0f - cameraX);
        int cy = (int)(player.y - cameraY + (player.height * player.scale));
        
        int pImgW = 0, pImgH = 0;
        if (player.anim.HasClip(player.anim.currentClip)) {
            pImgH = player.anim.GetCurrentFrameHeight();
            int drawCy = (int)(cy - (pImgH * player.scale) / 2.0f);
            // 左右反転の基準 — img/プレイヤー.png は目が左側に描かれた「左向き」の絵。
            // そのため direction==0(右向き) のときこそ画像を反転しなければならない。
            // 以前は逆（direction==1で反転）だったので、プレイヤーは常に進行方向と逆を向いていた。
            player.anim.DrawAt(cx, drawCy, player.scale, player.angle, player.direction == 0);
        } else {
            GetGraphSize(player.handle, &pImgW, &pImgH);
            // 新アセット移行対応 — 640x640の素材をプレイヤーの表示サイズへ収める倍率。
            // 描画の縦位置(drawCy)は「実画像サイズ×倍率」で決めているので、
            // 倍率を掛けたあとの見かけの高さで計算し直さないと足元の位置がずれる。
            float playerFit = ComputeFitScale(player.handle, (float)player.width, (float)player.height);
            int drawCy = (int)(cy - (pImgH * playerFit * player.scale) / 2.0f);
            // 素材が左向きなので、右を向いているとき(direction==0)に反転して描く。
            // 敵側は素材ごとに事情が違うため、ここではプレイヤーの描画だけを直している。
            if (player.direction == 0) DrawRotaGraph(cx, drawCy, playerFit * player.scale, player.angle, player.handle, TRUE, TRUE);
            else DrawRotaGraph(cx, drawCy, playerFit * player.scale, player.angle, player.handle, TRUE);
        }
        
        if (isDebugDrawMode) {
            DrawBox((int)(player.x - cameraX), (int)(player.y - cameraY), (int)(player.x + player.width * player.scale - cameraX), (int)(player.y + player.height * player.scale - cameraY), GetColor(0, 255, 0), FALSE);
            if (pImgH > 0) {
                int drawCy = (int)(cy - (pImgH * player.scale) / 2.0f);
                DrawBox((int)(cx - (pImgW > 0 ? pImgW : pImgH) * player.scale / 2.0f), (int)(drawCy - (pImgH * player.scale) / 2.0f), 
                        (int)(cx + (pImgW > 0 ? pImgW : pImgH) * player.scale / 2.0f), (int)(drawCy + (pImgH * player.scale) / 2.0f), GetColor(255, 0, 0), FALSE);
            }
        }
        if (isPlayerRewinding) DrawString((int)(player.x - cameraX), (int)(player.y - cameraY) - 20, "<< REW", GetColor(100, 255, 100));
        else if (player.isPaused) DrawString((int)(player.x - cameraX), (int)(player.y - cameraY) - 20, "|| PAUSE", GetColor(255, 255, 100));
        
        // プレイヤーHP表示
        if (player.hp > 0) {
            DrawFormatString((int)(player.x - cameraX), (int)(player.y - cameraY) - 40, GetColor(0, 255, 0), "HP:%d", player.hp);
        }

        // 選択枠の描画
        if (isEditMode) {
            for (auto* p : selectedPlayers) {
                DrawBox((int)(p->x - cameraX), (int)(p->y - cameraY), (int)(p->x + p->width * p->scale - cameraX), (int)(p->y + p->height * p->scale - cameraY), GetColor(255, 255, 0), FALSE);
            }
            for (auto* e : selectedEnemies) {
                if (e->isActive) {
                    DrawBox((int)(e->x - cameraX), (int)(e->y - cameraY), (int)(e->x + e->hitboxWidth * e->scale - cameraX), (int)(e->y + e->hitboxHeight * e->scale - cameraY), GetColor(255, 255, 0), FALSE);
                }
            }
            for (auto* g : selectedGimmicks) {
                if (g->isActive) {
                    DrawBox((int)(g->x - cameraX), (int)(g->y - cameraY), (int)(g->x + g->spriteWidth - cameraX), (int)(g->y + g->spriteHeight - cameraY), GetColor(255, 255, 0), FALSE);
                }
            }
        }

        // 弾の描画
        for (int i = 0; i < MAX_BULLETS; i++) {
            // 新アセット移行対応 — 以前は DrawGraph で原寸描画していたため、640x640の弾画像だと
            // 画面が弾で埋まってしまう。弾の当たり判定は各所で 16x16 固定なので、描画もそれに合わせる。
            //
            // 弾の向きの可視化 — 軸そろえの DrawExtendGraph をやめ、進行方向へ回転させて描く。
            // 砲台のように狙う角度が変わる敵は、弾がどちらへ飛んでいるかが分からないと
            // 「今の一発は自分に向いているのか」を読めず、避ける判断ができなかった。
            // img/弾.png は横長の楕円で「角度0＝右向き」に描かれているので、
            // 速度ベクトルの atan2 をそのまま回転角に使える（絵の向きを補正する必要が無い）。
            // 回転すると弾が進行方向へ伸びた曳光弾のように見えるため、向きが一目で分かる。
            if (bullets[i].isActive) {
                // 当たり判定が各所で (x, y, 16, 16) 固定なので、その矩形の中心を回転中心にする
                int bcx = (int)(bullets[i].x - cameraX) + BULLET_DRAW_SIZE / 2;
                int bcy = (int)(bullets[i].y - cameraY) + BULLET_DRAW_SIZE / 2;
                float bulletAngle = atan2f(bullets[i].vy, bullets[i].vx);
                // Feature: 編集リアクション — 撃った敵の大きさを弾の見た目にも反映する
                float bulletFit = ComputeFitScale(bullets[i].handle, (float)BULLET_DRAW_SIZE, (float)BULLET_DRAW_SIZE) * bullets[i].scale;
                DrawRotaGraph(bcx, bcy, bulletFit, bulletAngle, bullets[i].handle, TRUE);
            }
        }

        // OSD：コイン枚数と編集コストゲージ（正しくプレビューされるようにgameScreen内に描画）
        // UI素材化 — 単色のDrawBox2枚だったものを UIウィンドウ.png の9スライス枠1枚にまとめ、
        // 「何のカウンタか」を英字ラベルではなくアイコンの絵（コイン.png / エネルギー.png）で示す。
        int collectedCoins = 0;
        for (const auto& item : items) { if (item.isCollected) collectedCoins++; }
        {
            const int panelX1 = 8, panelY1 = 8, panelX2 = 196, panelY2 = 84;
            DrawUiWindow(panelX1, panelY1, panelX2, panelY2, uiWindowHandle);

            // --- 1段目：コインの取得枚数 ---
            DrawUiIcon(34, 30, 34, coinHandle);
            char coinStr[32];
            sprintf_s(coinStr, sizeof(coinStr), "%d / %d", collectedCoins, (int)items.size());
            DrawString(56, 23, coinStr, UiInk());

            // --- 2段目：編集コストゲージ ---
            float ratio = editCost / currentEditCost.maxCost;
            if (ratio < 0.0f) ratio = 0.0f; if (ratio > 1.0f) ratio = 1.0f;
            bool isLow = ratio < 0.2f;
            bool blinkOn = ((long)(BehaviorInterpreter::globalFrameCounter) / 15) % 2 == 0;
            int barColor = isLow ? (blinkOn ? GetColor(255, 80, 80) : GetColor(90, 30, 30)) : GetColor(0, 170, 225);

            DrawUiIcon(34, 60, 38, energyHandle);
            // ゲージの器は暗い溝にしておく。こうしておけば残量が減ってバーが後退しても、
            // 上に乗る白文字がクリーム色の枠内背景に溶けず常に読める。
            const int gx1 = 54, gy1 = 50, gx2 = 186, gy2 = 70;
            DrawBox(gx1, gy1, gx2, gy2, GetColor(56, 52, 48), TRUE);
            DrawBox(gx1, gy1, gx1 + (int)((gx2 - gx1) * ratio), gy2, barColor, TRUE);
            DrawBox(gx1, gy1, gx2, gy2, UiInkAccent(), FALSE);
            char editCostStr[32];
            sprintf_s(editCostStr, sizeof(editCostStr), "%d / %d", (int)editCost, (int)currentEditCost.maxCost);
            DrawString(gx1 + 6, gy1 + 3, editCostStr, GetColor(255, 255, 255));
        }

        // 編集ツールの状態インジケータ（色フィルタ・ミュート）。
        // 上のOSDパネルが横196pxまで伸びたので、重ならない位置へずらしてある。
        if (playerColorFilter != 0) {
            const char* filterName = playerColorFilter == 1 ? "RED" : playerColorFilter == 2 ? "GREEN" : "BLUE";
            int filterColor = playerColorFilter == 1 ? GetColor(255, 90, 90) : playerColorFilter == 2 ? GetColor(90, 220, 90) : GetColor(90, 150, 255);
            DrawFormatString(206, 16, filterColor, "FILTER: %s", filterName);
        }
        if (SoundManager::Get().IsMuted()) {
            DrawString(206, 34, "MUTED", GetColor(200, 200, 200));
        }

        // ヘルプガイドのOSDオーバーレイ
        // 実際のキー割り当てに合わせた表記にしてある（ジャンプは[W]、[SPACE]は一時停止、
        // 一時停止中の[→]がコマ送り）。ここが実装とズレていると、編集ツール前提のステージが
        // 「操作が分からないから詰む」だけの理不尽なものになってしまうため、必ず同期させること。
        if (isPaused) {
            DrawString(10, SCREEN_HEIGHT - 38, "PAUSED: [RIGHT]: Step 1 Frame  [SPACE]/[MiddleClick]: Resume", GetColor(255, 255, 120));
            DrawString(10, SCREEN_HEIGHT - 22, "EDITING: Drag to Move, [S]+Drag Vert. to Scale, [R]+Drag Horiz. to Rotate, RightClick for Menu", GetColor(200, 200, 200));
        } else {
            DrawString(10, SCREEN_HEIGHT - 38, "PLAYING: [A][D]:Move  [W]:Jump  [SHIFT]:Dash  [ENTER]/Click Screen:Shot", GetColor(50, 255, 50));
            DrawString(10, SCREEN_HEIGHT - 22, "EDIT: [R]:Rewind  [SPACE]:Pause  [F]:FastFwd  [T]:Color  [Z]:Zoom  [X]:Dark  [C]:Bright  [M]:Mute", GetColor(120, 220, 255));
        }

        // Feature 5: ShowMessageアクションによるメッセージウィンドウ
        if (isShowingMessage) {
            // UI素材化 — 半透明の黒箱から UIウィンドウ.png の枠へ差し替える。
            // 背景がクリーム色になるので、文字は白ではなく暗いインク色で描く。
            int boxX = 20, boxY = SCREEN_HEIGHT - 110, boxW = SCREEN_WIDTH - 40, boxH = 90;
            DrawUiWindow(boxX, boxY, boxX + boxW, boxY + boxH, uiWindowHandle);
            if (!currentMessageSpeaker.empty()) {
                DrawString(boxX + 18, boxY + 14, currentMessageSpeaker.c_str(), UiInkAccent());
            }
            DrawString(boxX + 18, boxY + (currentMessageSpeaker.empty() ? 20 : 38), currentMessageText.c_str(), UiInk());
            DrawString(boxX + boxW - 118, boxY + boxH - 26, "[ENTER] to close", UiInkSub());
        }

        // Feature: 進行状況のセーブ — PLAY からクリアへ移った瞬間だけ記録する。
        // 毎フレーム書くと磨耗するし、代入元ごとに書くと書き漏らす。
        if (prevSceneForSave == PLAY && currentScene == RESULT_VICTORY) {
            // 取得数はゲーム内OSDと同じ数え方（isCollected な全アイテム）にそろえる。
            // コイン専用のカウンタは存在しないので、ここだけコインに絞ると
            // プレイ中の表示とセレクト画面の表示が食い違う。
            int gotItems = 0;
            for (const auto& it : items) { if (it.isCollected) gotItems++; }
            GameCfg::RecordClear(saveData, currentStageFileName, gotItems);
            GameCfg::WriteSaveData(saveData);
            Logger::Info("GameCfg", "RecordClear",
                "cleared " + currentStageFileName + " items=" + std::to_string(gotItems));
        }
        prevSceneForSave = currentScene;

        // リザルト画面
        //
        // ボタンは縦積み。横並びにすると日本語ラベル（「つぎのステージへ」等）が入らない。
        // 矩形は「描画」と「クリック判定」で同じ値を使い回す（PAUSE_BUTTON_* と同じ方針。
        // 片方だけ動かすと、見た目の端を押しても反応しない領域ができる）。
        if (currentScene == RESULT_GAMEOVER || currentScene == RESULT_VICTORY) {
            SetDrawBlendMode(DX_BLENDMODE_ALPHA, 180);
            DrawBox(0, 0, SCREEN_WIDTH, SCREEN_HEIGHT, GetColor(0, 0, 0), TRUE);
            SetDrawBlendMode(DX_BLENDMODE_NOBLEND, 0);

            // 「つぎのステージへ」を出せるか判定する。
            // 今プレイしているステージが game_config.json の一覧の何番目かを探し、
            // 次のエントリがあり、かつ（今回のクリアを反映済みのセーブで）解放されていること。
            int curIdx = -1;
            for (int i = 0; i < (int)gameConfig.stages.size(); i++) {
                if (gameConfig.stages[i].file == currentStageFileName) { curIdx = i; break; }
            }
            bool hasNext = false;
            int nextIdx = -1;
            if (currentScene == RESULT_VICTORY && curIdx >= 0 && curIdx + 1 < (int)gameConfig.stages.size()) {
                nextIdx = curIdx + 1;
                hasNext = GameCfg::IsStageUnlocked(gameConfig, saveData, (size_t)nextIdx);
            }

            // 出すボタンを上から順に組み立てる（0=つぎへ / 1=もういちど / 2=セレクトへ）
            struct ResultBtn { const char* label; int kind; };
            ResultBtn btns[3];
            int btnCount = 0;
            if (hasNext) btns[btnCount++] = { gameConfig.nextLabel.c_str(), 0 };
            btns[btnCount++] = { gameConfig.retryLabel.c_str(), 1 };
            // タイトル画面を使わない設定のときはセレクトへ戻れても意味が無いので出さない
            if (gameConfig.titleEnabled && !gameConfig.stages.empty()) {
                btns[btnCount++] = { gameConfig.selectLabel.c_str(), 2 };
            }

            const int RESULT_BTN_W = 220, RESULT_BTN_H = 40, RESULT_BTN_GAP = 10;
            int btnX = SCREEN_WIDTH / 2 - RESULT_BTN_W / 2;
            int btnTop = SCREEN_HEIGHT / 2 + 6;

            // ゲーム画面は等倍で monitorX/monitorY の位置へ転送されるので、
            // ウィンドウ座標のマウスを内部解像度側へ寄せてから判定する。
            int relX = isEditMode ? (mx - monitorX) : mx;
            int relY = isEditMode ? (my - monitorY) : my;

            // 見出し
            SetFontSize(40);
            {
                const std::string& head = (currentScene == RESULT_GAMEOVER)
                                        ? gameConfig.gameoverText : gameConfig.victoryText;
                int hw = GetDrawStringWidth(head.c_str(), (int)head.size());
                DrawString(SCREEN_WIDTH / 2 - hw / 2, SCREEN_HEIGHT / 2 - 76, head.c_str(),
                           (currentScene == RESULT_GAMEOVER) ? GetColor(255, 90, 90) : GetColor(255, 235, 80));
            }
            SetFontSize(16);

            // クリア時はそのステージの取得数も出す（セレクト画面の表示と同じ数え方）
            if (currentScene == RESULT_VICTORY && curIdx >= 0) {
                char prog[64];
                sprintf_s(prog, sizeof(prog), "ITEM  %d / %d",
                          saveData.BestItems(currentStageFileName), gameConfig.stages[curIdx].itemTotal);
                int pw2 = GetDrawStringWidth(prog, (int)strlen(prog));
                DrawString(SCREEN_WIDTH / 2 - pw2 / 2, SCREEN_HEIGHT / 2 - 26, prog, GetColor(230, 230, 230));
            }

            for (int i = 0; i < btnCount; i++) {
                int y1 = btnTop + i * (RESULT_BTN_H + RESULT_BTN_GAP);
                int y2 = y1 + RESULT_BTN_H;
                bool isHover = (relX >= btnX && relX <= btnX + RESULT_BTN_W && relY >= y1 && relY <= y2);

                if (isHover) SetDrawBright(255, 255, 255);
                else         SetDrawBright(205, 200, 194);
                DrawUiWindow(btnX, y1, btnX + RESULT_BTN_W, y2, uiWindowHandle);
                SetDrawBright(255, 255, 255);
                int lw = GetDrawStringWidth(btns[i].label, (int)strlen(btns[i].label));
                DrawString(btnX + RESULT_BTN_W / 2 - lw / 2, y1 + RESULT_BTN_H / 2 - 8,
                           btns[i].label, isHover ? UiInkAccent() : UiInk());

                if (currentLeftClick && !prevLeftClick && isHover) {
                    if (btns[i].kind == 0 && nextIdx >= 0) {
                        SwitchToStage(gameConfig.stages[nextIdx].file); // 中で ResetStage が呼ばれ PLAY へ戻る
                    } else if (btns[i].kind == 1) {
                        ResetStage();
                    } else {
                        // セレクトへ戻る。ResetStage は呼ばない（次に選んだときに走る）
                        currentScene = STAGE_SELECT;
                        SoundManager::Get().StopBgm();
                    }
                    break; // このフレームで複数のボタンに反応させない
                }
            }
        }

        // --- 最終ワークスペースレイアウト出力 ---
        SetDrawScreen(DX_SCREEN_BACK);
        ClearDrawScreen();
        if (isEditMode) {
            DrawBox(0, 0, WINDOW_WIDTH, WINDOW_HEIGHT, GetColor(30, 30, 30), TRUE); // ワークスペース背景
            // UI素材化 — 左パネルを UIウィンドウ.png の9スライス枠で描く。
            // 素材が明るいクリーム色なので、この中に載せる文字は全て暗いインク色(UiInk系)に統一する。
            DrawUiWindow(0, 0, 250, WINDOW_HEIGHT - 100, uiWindowHandle);
            
            // パネルタイトルと、いま再生中か一時停止中かの表示。
            // 「再生中/一時停止中」は文字だけでなく専用アイコン（UI再生中.png / UI一時停止中.png）でも示す。
            DrawString(24, 22, "STAGE EDITOR", UiInkAccent());
            DrawUiIcon(38, 60, 26, isPaused ? uiPauseHandle : uiPlayHandle);
            DrawString(60, 53, isPaused ? "PAUSED" : "PLAYING", isPaused ? UiInkWarn() : UiInkOk());

            // 編集ツールの解禁・許可状況の一覧。
            // ステージ側の封印(allowed_edit_tools)とアイテムによる恒久解禁の両方を踏まえた
            // 「今このステージで実際に使えるか」を表示する。使えない操作をプレイヤーが
            // 延々と試して詰まるのを防ぐためのガイドなので、判定は入力処理と同じ変数を参照している。
            {
                DrawString(24, 92, "EDIT TOOLS", UiInkSub());
                struct { const char* label; bool on; } toolRows[] = {
                    { "Rewind   [R]",     rewindOpEnabled },
                    { "Pause    [SPACE]", pauseOpEnabled },
                    { "FastFwd  [F]",     fastForwardOpEnabled },
                    { "Screen   [TZXC]",  screenEffectOpEnabled },
                    { "Object   [RClick]", objectEditOpEnabled },
                    { "Cut      [Ctrl]",  cutOpEnabled },
                };
                for (int ti = 0; ti < 6; ti++) {
                    int rowY = 114 + ti * 20;
                    DrawString(24, rowY, toolRows[ti].label, toolRows[ti].on ? UiInk() : UiInkSub());
                    DrawString(200, rowY, toolRows[ti].on ? "ON" : "--", toolRows[ti].on ? UiInkOk() : UiInkSub());
                }
            }

            // 残り編集コスト。下部のゲージと同じ情報だが、エディタ側でも常に見えるようにしておく。
            {
                float leftRatio = editCost / currentEditCost.maxCost;
                if (leftRatio < 0.0f) leftRatio = 0.0f; if (leftRatio > 1.0f) leftRatio = 1.0f;
                DrawUiIcon(38, 262, 30, energyHandle);
                DrawBox(60, 254, 226, 272, GetColor(56, 52, 48), TRUE);
                DrawBox(60, 254, 60 + (int)(166 * leftRatio), 272, GetColor(0, 170, 225), TRUE);
                DrawBox(60, 254, 226, 272, UiInkAccent(), FALSE);
                DrawFormatString(66, 257, GetColor(255, 255, 255), "%d / %d", (int)editCost, (int)currentEditCost.maxCost);
            }

            DrawUiWindow(WINDOW_WIDTH - 250, 0, WINDOW_WIDTH, WINDOW_HEIGHT - 100, uiWindowHandle); // 右パネル（インスペクター）
            DrawUiWindow(0, WINDOW_HEIGHT - 100, WINDOW_WIDTH, WINDOW_HEIGHT, uiWindowHandle);        // 下部パネル（タイムライン）

            // モニタープレビューウィンドウ
            DrawBox(monitorX - 2, monitorY - 2, monitorX + SCREEN_WIDTH + 2, monitorY + SCREEN_HEIGHT + 2, GetColor(100, 100, 100), FALSE);
            Screen_DrawComposited(monitorX, monitorY, SCREEN_WIDTH, SCREEN_HEIGHT, gameScreen);

            // 範囲選択矩形の描画
            if (isAreaSelecting) {
                int x1 = min(areaSelectStartX, areaSelectEndX);
                int y1 = min(areaSelectStartY, areaSelectEndY);
                int x2 = max(areaSelectStartX, areaSelectEndX);
                int y2 = max(areaSelectStartY, areaSelectEndY);
                
                SetDrawBlendMode(DX_BLENDMODE_ALPHA, 60);
                DrawBox(x1, y1, x2, y2, GetColor(100, 150, 255), TRUE); // 半透明の青い塗りつぶし
                SetDrawBlendMode(DX_BLENDMODE_NOBLEND, 0);
                DrawBox(x1, y1, x2, y2, GetColor(100, 150, 255), FALSE); // 不透明の青い輪郭線
            }

            // Feature: カット機能の復活 — 下部パネルのタイムライン表示。
            // 帯の左端がステージ左端、右端がステージ右端に対応し、赤い縦棒が現在のプレイヤー位置（再生ヘッド）。
            // Ctrl+クリックで打った1点目はシアンの縦線、確定したカット区間は赤い半透明の帯で示す。
            // 帯そのものは暗い溝にしておく。パネルがクリーム色になったので、
            // 帯まで明るくすると「どこがタイムラインか」の輪郭が消えてしまう。
            DrawBox(50, WINDOW_HEIGHT - 60, WINDOW_WIDTH - 50, WINDOW_HEIGHT - 40, GetColor(56, 52, 48), TRUE);
            DrawBox(50, WINDOW_HEIGHT - 60, WINDOW_WIDTH - 50, WINDOW_HEIGHT - 40, UiInkAccent(), FALSE);
            float mxp = 50.0f + (player.x / (float)STAGE_WIDTH) * (float)(WINDOW_WIDTH - 100);
            DrawBox((int)mxp - 2, WINDOW_HEIGHT - 70, (int)mxp + 2, WINDOW_HEIGHT - 30, GetColor(255, 0, 0), TRUE);

            // 確定済みカット区間の描画（選択中のものは輪郭を白くして分かるようにする）
            for (auto& gim : gimmicks) {
                if (gim.type != GIMMICK_CUT_PORTAL || !gim.isTimelineCut) continue;
                int cx1 = 50 + (int)(gim.val1 * (float)(WINDOW_WIDTH - 100));
                int cx2 = 50 + (int)(gim.val2 * (float)(WINDOW_WIDTH - 100));
                SetDrawBlendMode(DX_BLENDMODE_ALPHA, 80);
                DrawBox(cx1, WINDOW_HEIGHT - 60, cx2, WINDOW_HEIGHT - 40, GetColor(200, 50, 50), TRUE);
                SetDrawBlendMode(DX_BLENDMODE_NOBLEND, 0);
                bool isSel = (targetGimmick == &gim);
                int edgeColor = isSel ? GetColor(255, 255, 255) : GetColor(255, 255, 0);
                DrawLine(cx1, WINDOW_HEIGHT - 60, cx1, WINDOW_HEIGHT - 40, edgeColor);
                DrawLine(cx2, WINDOW_HEIGHT - 60, cx2, WINDOW_HEIGHT - 40, edgeColor);
                if (isSel) DrawBox(cx1, WINDOW_HEIGHT - 60, cx2, WINDOW_HEIGHT - 40, edgeColor, FALSE);
            }
            // 打ちかけの始点（2点目のクリック待ち）と、確定前のコストプレビュー。
            //
            // Feature: カットコストの距離変動 — 長さでコストが変わる以上、
            // 「今マウスを離したらいくら取られるのか」が見えないと運任せの操作になってしまう。
            // そこで始点からマウス位置までを仮の帯として描き、その区間の総コストを実数値で出す。
            // 払えない長さなら帯と数字を赤にして、クリックしても弾かれることを事前に伝える。
            if (tempCutStart >= 0.0f) {
                int px = 50 + (int)(tempCutStart * (float)(WINDOW_WIDTH - 100));
                // マウスX座標をタイムライン上の比率へ変換する（クリック判定と同じ式）
                float hoverRatio = (float)(mx - 50) / (float)(WINDOW_WIDTH - 100);
                if (hoverRatio < 0.0f) hoverRatio = 0.0f;
                if (hoverRatio > 1.0f) hoverRatio = 1.0f;
                float previewCost = ComputeCutCreateCost(tempCutStart, hoverRatio, STAGE_WIDTH, currentEditCost);
                bool affordable = (editCost >= previewCost);
                int hx = 50 + (int)(hoverRatio * (float)(WINDOW_WIDTH - 100));
                int bandL = (px < hx) ? px : hx;
                int bandR = (px < hx) ? hx : px;

                // 仮の帯（払えるならシアン、払えないなら赤）
                SetDrawBlendMode(DX_BLENDMODE_ALPHA, 90);
                DrawBox(bandL, WINDOW_HEIGHT - 60, bandR, WINDOW_HEIGHT - 40,
                        affordable ? GetColor(0, 200, 255) : GetColor(220, 60, 60), TRUE);
                SetDrawBlendMode(DX_BLENDMODE_NOBLEND, 0);
                DrawLine(px, WINDOW_HEIGHT - 75, px, WINDOW_HEIGHT - 25, GetColor(0, 255, 255));
                DrawLine(hx, WINDOW_HEIGHT - 75, hx, WINDOW_HEIGHT - 25,
                         affordable ? GetColor(0, 255, 255) : GetColor(255, 90, 90));

                // コスト表示。帯の中央上に「距離(タイル数)」と「総コスト / 残ゲージ」を出す。
                float spanRatio = hoverRatio - tempCutStart;
                if (spanRatio < 0.0f) spanRatio = -spanRatio;
                int spanTiles = (int)((spanRatio * STAGE_WIDTH) / (float)TILE_SIZE);
                int labelX = (bandL + bandR) / 2 - 70;
                if (labelX < 50) labelX = 50;
                if (labelX > WINDOW_WIDTH - 200) labelX = WINDOW_WIDTH - 200;
                DrawFormatString(labelX, WINDOW_HEIGHT - 78,
                                 affordable ? UiInkAccent() : UiInkWarn(),
                                 "CUT %d tiles  COST %.0f / %.0f", spanTiles, previewCost, editCost);
            }
            // 操作ヒント。中央のPAUSEボタン(x 590..690)に文字がかぶらない長さに収めてある。
            // カットのコストは距離で変わるので、その計算式もここに出しておく。
            DrawString(50, WINDOW_HEIGHT - 86, "TIMELINE  [Ctrl]+Click x2 = Cut / RightClick = Select", UiInkSub());
            // カットのコスト式は、中央のPAUSEボタン(x 580..700)より右側の空きスペースに出す。
            // 操作ヒントと同じ行に置くことで、下のタイムライン帯やコストプレビューと行がぶつからない。
            DrawFormatString(760, WINDOW_HEIGHT - 86, UiInkSub(),
                             "CUT COST = %.0f + %.1f / tile", currentEditCost.flatCutCreate, currentEditCost.cutCostPerTile);

            // 動的インスペクターパネルの描画
            DrawString(WINDOW_WIDTH - 240, 22, "INSPECTOR", UiInkAccent());
            if (selectedType == SELECT_NONE) {
                DrawString(WINDOW_WIDTH - 240, 50, "No Object Selected", UiInkSub());
            } else {
                char objName[64] = "Selected: UNKNOWN";
                size_t totalSelected = selectedPlayers.size() + selectedEnemies.size() + selectedGimmicks.size();
                if (totalSelected > 1) {
                    sprintf_s(objName, sizeof(objName), "Selected: MULTI (%d objs)", (int)totalSelected);
                } else {
                    if (selectedType == SELECT_PLAYER) sprintf_s(objName, sizeof(objName), "Selected: PLAYER");
                    else if (selectedType == SELECT_ENEMY) sprintf_s(objName, sizeof(objName), "Selected: ENEMY");
                    else if (selectedType == SELECT_GIMMICK && targetGimmick != nullptr) {
                        if (targetGimmick->type == GIMMICK_ROTATING_BRIDGE) sprintf_s(objName, sizeof(objName), "Selected: AUTO BRIDGE");
                        else if (targetGimmick->type == GIMMICK_MANUAL_BRIDGE) sprintf_s(objName, sizeof(objName), "Selected: MANUAL BRIDGE");
                        else if (targetGimmick->type == GIMMICK_FALLING_LIFT) sprintf_s(objName, sizeof(objName), "Selected: FALLING LIFT");
                        else if (targetGimmick->type == GIMMICK_REFLECT_MIRROR) sprintf_s(objName, sizeof(objName), "Selected: REFLECT MIRROR");
                        else if (targetGimmick->type == GIMMICK_SCALABLE_BOX) sprintf_s(objName, sizeof(objName), "Selected: SCALABLE BOX");
                        else if (targetGimmick->type == GIMMICK_WEIGHT_SWITCH) sprintf_s(objName, sizeof(objName), "Selected: WEIGHT SWITCH");
                        else if (targetGimmick->type == GIMMICK_GATE_DOOR) sprintf_s(objName, sizeof(objName), "Selected: GATE DOOR");
                        // Feature: カット機能の復活 — 同じCUT_PORTAL型でも、タイムラインカットと
                        // ワールド配置のワープポータルは別物なので表示名を分ける
                        else if (targetGimmick->type == GIMMICK_CUT_PORTAL) {
                            sprintf_s(objName, sizeof(objName),
                                      targetGimmick->isTimelineCut ? "Selected: TIMELINE CUT" : "Selected: PORTAL");
                        }
                    }
                }
                DrawString(WINDOW_WIDTH - 240, 50, objName, UiInkAccent());
                
                // ドラッグ操作中の行はアクセント色にして「今つまんでいる項目」を示す
                if (targetScale != nullptr) {
                    if (selectedType == SELECT_GIMMICK) {
                        DrawFormatString(WINDOW_WIDTH - 240, 80, isInspScale ? UiInkAccent() : UiInk(), "Width: %.0f", *targetScale);
                    } else {
                        DrawFormatString(WINDOW_WIDTH - 240, 80, isInspScale ? UiInkAccent() : UiInk(), "Scale: %.2f", *targetScale);
                    }
                } else {
                    DrawFormatString(WINDOW_WIDTH - 240, 80, UiInkSub(), "Scale: N/A");
                }

                if (targetAngle != nullptr) {
                    DrawFormatString(WINDOW_WIDTH - 240, 100, isInspAngle ? UiInkAccent() : UiInk(), "Angle: %.2f", *targetAngle);
                } else {
                    DrawFormatString(WINDOW_WIDTH - 240, 100, UiInkSub(), "Angle: N/A");
                }
                
                // Feature: 編集リアクション — ギミックもspeedScaleを持つようになったため、
                // 以前の「ギミック選択時は無条件でN/A」という分岐は不要になった。
                if (targetSpeedScale != nullptr) {
                    DrawFormatString(WINDOW_WIDTH - 240, 120, isInspSpeed ? UiInkAccent() : UiInk(), "Speed: %.1f", *targetSpeedScale);
                } else {
                    DrawFormatString(WINDOW_WIDTH - 240, 120, UiInkSub(), "Speed: N/A");
                }

                // 個別一時停止はアイコンでも示す（この対象だけ時間が止まっているかどうか）
                if (targetPaused != nullptr) {
                    DrawUiIcon(WINDOW_WIDTH - 254, 147, 18, *targetPaused ? uiPauseHandle : uiPlayHandle);
                    DrawFormatString(WINDOW_WIDTH - 240, 140, *targetPaused ? UiInkWarn() : UiInk(), "Pause: %s", *targetPaused ? "TRUE" : "FALSE");
                } else {
                    DrawFormatString(WINDOW_WIDTH - 240, 140, UiInkSub(), "Pause: N/A");
                }

                if (targetRewind != nullptr) {
                    DrawFormatString(WINDOW_WIDTH - 240, 160, *targetRewind ? UiInkWarn() : UiInk(), "Rewind: %s", *targetRewind ? "TRUE (<<)" : "FALSE");
                } else {
                    DrawFormatString(WINDOW_WIDTH - 240, 160, UiInkSub(), "Rewind: N/A");
                }
                
                if (selectedType == SELECT_ENEMY && targetEnemyType != nullptr) {
                    // 以前は3種類しか名前解決しておらず、残り19種は全て"UNKNOWN"と表示されていた。
                    // インスペクタのType行は敵タイプ巡回編集の結果を確認する唯一の手段なので全種を出す。
                    DrawFormatString(WINDOW_WIDTH - 240, 180, UiInkOk(), "Type: %s", EnemyTypeName(*targetEnemyType));
                }

                // Feature: 編集リアクション — 今この個体に効いている編集を1行で示す。
                // 「傾けたら照準が固定された」のような反応は、起きていることが読めなければ
                // パズルの手札として使いようがないため、UIでの可視化は機能の一部として必須。
                {
                    EditReaction ins;
                    bool hasReact = false;
                    if (selectedType == SELECT_ENEMY && targetEnemy != nullptr) {
                        ins = GetEnemyEditReaction(*targetEnemy, FindEnemyDef(targetEnemy->assetId));
                        hasReact = true;
                    } else if (selectedType == SELECT_GIMMICK && targetGimmick != nullptr && !targetGimmick->isTimelineCut) {
                        ins = GetGimmickEditReaction(*targetGimmick);
                        hasReact = true;
                    }
                    std::string body;
                    auto appendTag = [&](const char* tag) {
                        if (!body.empty()) body += "+";
                        body += tag;
                    };
                    if (hasReact) {
                        if (ins.enlarged)      appendTag("BIG");
                        if (ins.shrunk)        appendTag("SMALL");
                        if (ins.tipped)        appendTag("TIPPED");
                        else if (ins.tilted)   appendTag("TILT");
                        if (ins.frozen)        appendTag("STOP");
                        else if (ins.hastened) appendTag("FAST");
                        else if (ins.slowed)   appendTag("SLOW");
                        if (ins.flipped)       appendTag("FLIP");
                        if (ins.selfPaused)    appendTag("PAUSED");
                        if (ins.selfRewinding) appendTag("REWIND");
                        if (ins.moved)         appendTag("MOVED");
                    }
                    std::string reactStr = "React: " + (body.empty() ? std::string("NONE") : body);
                    DrawString(WINDOW_WIDTH - 240, 195, reactStr.c_str(), body.empty() ? UiInkSub() : UiInkAccent());
                }

                DrawString(WINDOW_WIDTH - 240, 215, "(Drag values / click to toggle)", UiInkSub());
            }

            // 下部一時停止ボタン。
            // UI素材化 — ボタンの下地を UIウィンドウ.png にし、
            // 「押すとどうなるか」を再生/一時停止アイコンで示す
            // （一時停止中は再生アイコン＝押せば再開、再生中は一時停止アイコン＝押せば止まる）。
            {
                // アイコンだけのボタンにしたので、文字ぶんの横幅を詰めて正方形寄りにする。
                // 矩形は PAUSE_BUTTON_* 定数を描画とクリック判定の両方で共有しているため、
                // 見た目と当たり判定が食い違うことがない（以前はここだけ広げてズレていた）。
                bool pbHover = (mx >= PAUSE_BUTTON_X1 && mx <= PAUSE_BUTTON_X2 && my >= PAUSE_BUTTON_Y1 && my <= PAUSE_BUTTON_Y2);
                if (pbHover) SetDrawBright(255, 255, 255);
                else         SetDrawBright(214, 209, 202);
                DrawUiWindow(PAUSE_BUTTON_X1, PAUSE_BUTTON_Y1, PAUSE_BUTTON_X2, PAUSE_BUTTON_Y2, uiWindowHandle);
                SetDrawBright(255, 255, 255);
                // 「押すとどうなるか」はアイコンだけで伝わるので、PAUSE/RESUMEの文字は出さない
                DrawUiIcon((PAUSE_BUTTON_X1 + PAUSE_BUTTON_X2) / 2, (PAUSE_BUTTON_Y1 + PAUSE_BUTTON_Y2) / 2,
                           20, isPaused ? uiPlayHandle : uiPauseHandle);
            }

            // コンテキストメニューポップアップの描画
            if (menu.isOpen) {
                // UI素材化 — ポップアップメニューも UIウィンドウ.png の枠で描く
                DrawUiWindow(menu.x, menu.y, menu.x + menu.width, menu.y + menu.height, uiWindowHandle);

                // Feature: カット機能の復活 — カット選択中は「Delete Cut」だけを出す専用メニュー
                if (targetGimmick != nullptr && targetGimmick->isTimelineCut) {
                    DrawString(menu.x + 10, menu.y + 10, "Delete Cut", UiInkWarn()); // 目立つ赤で表示
                    DrawString(menu.x + 10, menu.y + 40, "(timeline cut)", UiInkSub());
                }
                else {
                    char rewindStr[32];
                    sprintf_s(rewindStr, sizeof(rewindStr), "Rewind: %s", (targetRewind && *targetRewind) ? "ON" : "OFF");
                    char pausedStr[32];
                    sprintf_s(pausedStr, sizeof(pausedStr), "Pause: %s", (targetPaused && *targetPaused) ? "ON" : "OFF");

                    DrawString(menu.x + 10, menu.y + 10, rewindStr, UiInk());
                    DrawString(menu.x + 10, menu.y + 35, pausedStr, UiInk());

                    // Feature: 編集リアクション — ギミックにも速度と向き反転を実装したので、
                    // 全ての選択対象で6つの操作が等しく使える
                    DrawString(menu.x + 10, menu.y + 60, "Speed +0.5", targetSpeedScale ? UiInk() : UiInkSub());
                    DrawString(menu.x + 10, menu.y + 85, "Speed -0.5", targetSpeedScale ? UiInk() : UiInkSub());
                    DrawString(menu.x + 10, menu.y + 110, "Flip Object", targetDirection ? UiInk() : UiInkSub());
                    DrawString(menu.x + 10, menu.y + 135, "Reset All", UiInk());
                }
            }

            // Feature: 編集リアクション（共通層）の可視化 —
            // 一時停止した敵は「乗れる足場」に、巻き戻し中の敵は「すり抜けられる残像」になる。
            // どちらも見た目が普段と同じままでは、乗ってよいのか触れたら死ぬのかが判断できないので、
            // 敵の頭上に状態を明示する。
            for (const auto& enemyHud : enemies) {
                if (!enemyHud.isActive) continue;
                int hx = (int)(enemyHud.x - cameraX);
                int hy = (int)(enemyHud.y - cameraY) - 18;
                if (enemyHud.isPaused) {
                    DrawString(hx, hy, "|| STAND", UiInkOk());       // 止まっている＝踏み台にできる
                } else if (enemyHud.isRewinding) {
                    DrawString(hx, hy, "<< PHASE", UiInkAccent());   // 巻き戻し中＝すり抜けられる
                } else if (EnemyIsHarmless(enemyHud)) {
                    DrawString(hx, hy, "SAFE", UiInkOk());           // 型ごとの条件で無害化している
                }
            }

            // グローバル巻き戻しの視覚的フィードバックインジケータ
            if (isRKeyPressed && selectedType == SELECT_NONE) {
                static int blinkTimer = 0;
                blinkTimer = (blinkTimer + 1) % 30;
                if (blinkTimer < 15) {
                    DrawString(WINDOW_WIDTH / 2 - 80, 20, "<< GLOBAL REWINDING", GetColor(255, 100, 100));
                }
            }

            // 従来ここに出していたモード表示は、左パネル上部の再生/一時停止アイコン付き表示へ統合した
            // （同じ場所に "STAGE EDITOR" と重なって二重に見えていたため）。
        } else {
            // 非エディタ表示（ゲーム画面のみをウィンドウ全体へ拡大表示するモード）
            Screen_DrawComposited(0, 0, WINDOW_WIDTH, WINDOW_HEIGHT, gameScreen);
            if (isPaused) {
                // UI素材化 — 「PAUSED」の文字だけだったものを、
                // UIウィンドウ.pngの枠と一時停止アイコンを合わせた表示に差し替える。
                const int puX1 = WINDOW_WIDTH / 2 - 110, puY1 = WINDOW_HEIGHT / 2 - 34;
                const int puX2 = WINDOW_WIDTH / 2 + 110, puY2 = WINDOW_HEIGHT / 2 + 34;
                DrawUiWindow(puX1, puY1, puX2, puY2, uiWindowHandle);
                DrawUiIcon(puX1 + 44, (puY1 + puY2) / 2, 36, uiPauseHandle);
                DrawString(puX1 + 78, (puY1 + puY2) / 2 - 8, "PAUSED", UiInk());
            }
        }
        // ImGuiのフレーム開始
        ImGui_ImplDX11_NewFrame();
        ImGui_ImplWin32_NewFrame();
        ImGui::NewFrame();

        if (isDedicatedEditorMode) {
            // Stage Manager ウィンドウ
            ImGui::Begin("Stage Manager");
            ImGui::Text("Current: %s", currentStageFileName.c_str());
            ImGui::Separator();
            static char newStageName[128] = "new_stage";
            ImGui::InputText("##newstg", newStageName, 128);
            ImGui::SameLine();
            if (ImGui::Button("Create New")) {
                currentStageFileName = std::string(newStageName) + ".json";
                editorPlacedEnemies.clear();
                editorPlacedGimmicks.clear();
                editorPlacedItems.clear();
            }
            ImGui::Separator();
            ImGui::Text("Stage List:");
            if (fs::exists("assets/stages")) {
                for (const auto& entry : fs::directory_iterator("assets/stages")) {
                    if (entry.path().extension() == ".json") {
                        std::string fname = entry.path().filename().string();
                        if (ImGui::Selectable(fname.c_str(), currentStageFileName == fname)) {
                            currentStageFileName = fname;
                            editorPlacedEnemies.clear(); editorPlacedGimmicks.clear(); editorPlacedItems.clear();
                            std::ifstream sf("assets/stages/" + currentStageFileName);
                            if (sf.is_open()) {
                                json j = json::parse(sf, nullptr, false);
                                if (j.is_discarded()) {
                                    Logger::Error("DrawPixel", "EditorMode", "Failed to parse stage json", currentStageFileName);
                                    continue;
                                }
                                if (j.contains("enemies")) {
                                    for (auto& e : j["enemies"]) {
                                        std::string id = e["id"];
                                        int def_idx = -1;
                                        for(int k=0; k<enemyDefs.size(); k++) { if(enemyDefs[k].id == id) { def_idx=k; break; } }
                                        if (def_idx >= 0) editorPlacedEnemies.push_back({def_idx, e["x"], e["y"]});
                                    }
                                }
                                if (j.contains("gimmicks")) {
                                    for (auto& e : j["gimmicks"]) {
                                        std::string id = e["id"];
                                        int def_idx = -1;
                                        for(int k=0; k<gimmickDefs.size(); k++) { if(gimmickDefs[k].id == id) { def_idx=k; break; } }
                                        if (def_idx >= 0) {
                                            PlacedGimmick pg = {def_idx, e["x"], e["y"], ""};
                                            if (e.contains("param")) pg.stringParam = e["param"];
                                            editorPlacedGimmicks.push_back(pg);
                                        }
                                    }
                                }
                                if (j.contains("items")) {
                                    for (auto& e : j["items"]) {
                                        std::string id = e["id"];
                                        int def_idx = -1;
                                        for(int k=0; k<itemDefs.size(); k++) { if(itemDefs[k].id == id) { def_idx=k; break; } }
                                        if (def_idx >= 0) editorPlacedItems.push_back({def_idx, e["x"], e["y"]});
                                    }
                                }
                                if (j.contains("player_capabilities")) {
                                    auto caps = j["player_capabilities"];
                                    editorPlayerCaps.canDoubleJump = caps["canDoubleJump"];
                                    editorPlayerCaps.canDash = caps["canDash"];
                                    editorPlayerCaps.canShootFireball = caps["canShootFireball"];
                                    editorPlayerCaps.canFly = caps["canFly"];
                                    editorPlayerCaps.baseJumpPower = caps["baseJumpPower"];
                                    editorPlayerCaps.baseSpeed = caps["baseSpeed"];
                                }
                            }
                        }
                    }
                }
            }
            ImGui::End();

            // Tool Palette ウィンドウ
            ImGui::Begin("Tool Palette");
            static int selectedEnemyIdx = -1;
            static int selectedGimmickIdx = -1;
            static int selectedItemIdx = -1;
            if (ImGui::CollapsingHeader("Enemies", ImGuiTreeNodeFlags_DefaultOpen)) {
                for (int i = 0; i < enemyDefs.size(); i++) {
                    if (ImGui::Selectable(enemyDefs[i].name.c_str(), selectedEnemyIdx == i)) {
                        selectedEnemyIdx = i; selectedGimmickIdx = -1; selectedItemIdx = -1;
                    }
                }
            }
            if (ImGui::CollapsingHeader("Gimmicks", ImGuiTreeNodeFlags_DefaultOpen)) {
                for (int i = 0; i < gimmickDefs.size(); i++) {
                    if (ImGui::Selectable(gimmickDefs[i].name.c_str(), selectedGimmickIdx == i)) {
                        selectedGimmickIdx = i; selectedEnemyIdx = -1; selectedItemIdx = -1;
                    }
                }
            }
            if (ImGui::CollapsingHeader("Items", ImGuiTreeNodeFlags_DefaultOpen)) {
                for (int i = 0; i < itemDefs.size(); i++) {
                    if (ImGui::Selectable(itemDefs[i].name.c_str(), selectedItemIdx == i)) {
                        selectedItemIdx = i; selectedEnemyIdx = -1; selectedGimmickIdx = -1;
                    }
                }
            }
            ImGui::Separator();
            if (ImGui::Button(u8"ステージを保存 (Save)")) {
                json stageData;
                stageData["enemies"] = json::array();
                for (auto& e : editorPlacedEnemies) {
                    json j = { {"id", enemyDefs[e.def_idx].id}, {"x", e.x}, {"y", e.y} };
                    stageData["enemies"].push_back(j);
                }
                stageData["gimmicks"] = json::array();
                for (auto& g : editorPlacedGimmicks) {
                    json j = { {"id", gimmickDefs[g.def_idx].id}, {"x", g.x}, {"y", g.y} };
                    if (g.stringParam != "") j["param"] = g.stringParam;
                    stageData["gimmicks"].push_back(j);
                }
                stageData["items"] = json::array();
                for (auto& i : editorPlacedItems) {
                    json j = { {"id", itemDefs[i.def_idx].id}, {"x", i.x}, {"y", i.y} };
                    stageData["items"].push_back(j);
                }
                stageData["player_capabilities"] = {
                    {"canDoubleJump", editorPlayerCaps.canDoubleJump},
                    {"canDash", editorPlayerCaps.canDash},
                    {"canShootFireball", editorPlayerCaps.canShootFireball},
                    {"canFly", editorPlayerCaps.canFly},
                    {"baseJumpPower", editorPlayerCaps.baseJumpPower},
                    {"baseSpeed", editorPlayerCaps.baseSpeed}
                };
                std::ofstream ofs("assets/stages/" + currentStageFileName);
                ofs << stageData.dump(4);
            }
            ImGui::Separator();
            if (ImGui::Button(u8"プレイテスト開始 (Play Test)", ImVec2(-1, 30))) {
                isDedicatedEditorMode = false;
                try {
                    std::ifstream sf("assets/stages/" + currentStageFileName);
                    if (sf.is_open()) {
                        json stageData = json::parse(sf, nullptr, false);
                        if (stageData.is_discarded()) {
                            Logger::Error("DrawPixel", "TestPlay", "Failed to parse stage json", currentStageFileName);
                            continue;
                        }
                        player.x = 100.0f; player.y = 100.0f; player.vx = 0.0f; player.vy = 0.0f;
                        if (stageData.contains("player_capabilities")) {
                            auto caps = stageData["player_capabilities"];
                            editorPlayerCaps.canDoubleJump = caps["canDoubleJump"];
                            editorPlayerCaps.canDash = caps["canDash"];
                            editorPlayerCaps.canShootFireball = caps["canShootFireball"];
                            editorPlayerCaps.canFly = caps["canFly"];
                            editorPlayerCaps.baseJumpPower = caps["baseJumpPower"];
                            editorPlayerCaps.baseSpeed = caps["baseSpeed"];
                        }
                        enemies.clear();
                        if (stageData.contains("enemies")) {
                            for (auto& e : stageData["enemies"]) {
                                std::string id = e["id"]; float x = e["x"]; float y = e["y"];
                                int t_enum = 0; int pw = 32, ph = 32;
                                for (auto& d : enemyDefs) { if (d.id == id) { t_enum = d.type_enum; pw = d.width; ph = d.height; break; } }
                                enemies.push_back({ (EnemyType)t_enum, x, y, 0.0f, 0.0f, playerHandle, 1, pw, ph, pw, ph, 0, 0, pw, ph, 1.0f, 0.0f, 1.0f, true, false, 3, 0.0f, 0, x - 200.0f, x + 200.0f, false, {}, id, AnimationController() });
                            }
                        }
                    }
                } catch (...) {}
            }
            ImGui::End();

            // Player Settings ウィンドウ
            ImGui::Begin("Player Settings");
            ImGui::Text("Initial Player Capabilities");
            ImGui::Checkbox("Can Double Jump", &editorPlayerCaps.canDoubleJump);
            ImGui::Checkbox("Can Dash", &editorPlayerCaps.canDash);
            ImGui::Checkbox("Can Shoot Fireball", &editorPlayerCaps.canShootFireball);
            ImGui::Checkbox("Can Fly", &editorPlayerCaps.canFly);
            ImGui::SliderInt("Base Jump Power", &editorPlayerCaps.baseJumpPower, -20, -5);
            ImGui::SliderFloat("Base Speed", &editorPlayerCaps.baseSpeed, 1.0f, 10.0f);
            ImGui::End();

            // Properties ウィンドウ
            ImGui::Begin("Properties");
            if (selectedEnemyIdx >= 0 && selectedEnemyIdx < enemyDefs.size()) {
                ImGui::Text("Selected Enemy: %s", enemyDefs[selectedEnemyIdx].name.c_str());
                ImGui::Image((void*)(intptr_t)enemyDefs[selectedEnemyIdx].graphHandle, ImVec2(64, 64));
            } else if (selectedGimmickIdx >= 0 && selectedGimmickIdx < gimmickDefs.size()) {
                ImGui::Text("Selected Gimmick: %s", gimmickDefs[selectedGimmickIdx].name.c_str());
                ImGui::Image((void*)(intptr_t)gimmickDefs[selectedGimmickIdx].graphHandle, ImVec2(64, 64));
                static char portalTarget[128] = "stage_02.json";
                if (gimmickDefs[selectedGimmickIdx].id == "gim_portal") {
                    ImGui::InputText("Target Stage", portalTarget, 128);
                }
            } else if (selectedItemIdx >= 0 && selectedItemIdx < itemDefs.size()) {
                ImGui::Text("Selected Item: %s", itemDefs[selectedItemIdx].name.c_str());
                ImGui::Image((void*)(intptr_t)itemDefs[selectedItemIdx].graphHandle, ImVec2(64, 64));
                ImGui::Text("Grants: %s", itemDefs[selectedItemIdx].grant_ability.c_str());
            }
            ImGui::End();

            // 配置処理
            // 十字キー全押しでゲームを閉じる（エディタへ戻る）
        if (CheckHitKey(KEY_INPUT_UP) && CheckHitKey(KEY_INPUT_DOWN) && CheckHitKey(KEY_INPUT_LEFT) && CheckHitKey(KEY_INPUT_RIGHT)) {
            break;
        }
        int mx, my; GetMousePoint(&mx, &my);
            static bool wasClick = false;
            if ((GetMouseInput() & MOUSE_INPUT_LEFT) && !ImGui::GetIO().WantCaptureMouse) {
                if (!wasClick) {
                    if (selectedEnemyIdx >= 0) {
                        editorPlacedEnemies.push_back({selectedEnemyIdx, (float)mx, (float)my});
                    } else if (selectedGimmickIdx >= 0) {
                        std::string prm = "";
                        if (gimmickDefs[selectedGimmickIdx].id == "gim_portal") prm = "stage_02.json";
                        editorPlacedGimmicks.push_back({selectedGimmickIdx, (float)mx, (float)my, prm});
                    } else if (selectedItemIdx >= 0) {
                        editorPlacedItems.push_back({selectedItemIdx, (float)mx, (float)my});
                    }
                }
                wasClick = true;
            } else {
                wasClick = false;
            }

            // プレビュー描画（配置済みのものを描画）
            for (auto& e : editorPlacedEnemies) DrawGraph((int)e.x, (int)e.y, enemyDefs[e.def_idx].graphHandle, TRUE);
            for (auto& g : editorPlacedGimmicks) DrawGraph((int)g.x, (int)g.y, gimmickDefs[g.def_idx].graphHandle, TRUE);
            for (auto& i : editorPlacedItems) DrawGraph((int)i.x, (int)i.y, itemDefs[i.def_idx].graphHandle, TRUE);
        }

        // F5キーでエディタに戻る
        

        // UI素材化 — ゲーム内マウスカーソル。
        // OS標準のカーソルを消し、代わりに img/マウスホイール.png（矢印の絵）を描く。
        // ただし専用エディタモード(ImGui)のときだけはOSカーソルのままにする。
        // ImGuiのウィンドウはこの後に描かれるため、自前カーソルだとImGuiの上に出せず
        // パネル上でカーソルが見えなくなってしまうから。
        {
            bool useOwnCursor = !isDedicatedEditorMode && cursorHandle >= 0;
            SetMouseDispFlag(useOwnCursor ? FALSE : TRUE);
            if (useOwnCursor) {
                int cmx = 0, cmy = 0;
                GetMousePoint(&cmx, &cmy);
                // 素材の矢印の先端は 640x640 キャンバス上の (188, 93) 付近にある。
                // 表示サイズ40pxに縮めると先端は左上から (12, 6) の位置に来るので、
                // その分だけ左上へずらして描くと、絵の先端が実際のマウス座標と一致する。
                const int cursorSize = 40;
                DrawExtendGraph(cmx - 12, cmy - 6, cmx - 12 + cursorSize, cmy - 6 + cursorSize, cursorHandle, TRUE);
            }
        }

        ImGui::Render();
        ImGui_ImplDX11_RenderDrawData(ImGui::GetDrawData());
        
        ImGuiIO& iof = ImGui::GetIO();
        if (iof.ConfigFlags & ImGuiConfigFlags_ViewportsEnable) {
            ImGui::UpdatePlatformWindows();
            ImGui::RenderPlatformWindowsDefault();
        }

        ScreenFlip();
    }

    ImGui_ImplDX11_Shutdown();
    ImGui_ImplWin32_Shutdown();
    ImGui::DestroyContext();

    SoundManager::Get().Release();
    DxLib_End();
    return 0;
}

/*
================================================================================
【新規要素追加用 拡張テンプレート ＆ 実装ガイド】
================================================================================
今後、新しい敵(Enemy)、ステージギミック(Gimmick)、アイテム(Item)を追加する際は、
以下の手順とテンプレートに従ってコードを追加してください。
すべての要素はデータ指向で管理されているため、巻き戻し機能や一時停止と自動で同期します。

--------------------------------------------------------------------------------
1. 新しい「敵 (Enemy)」の追加手順
--------------------------------------------------------------------------------
[Step 1] `EnemyType` 列挙型に新しいタイプを追加します。
    (例: `ENEMY_FLYING` を enum EnemyType に追加)

[Step 2] 物理・更新ループ の `switch (enemy.type)` に新しいAI挙動を追加します。
    【テンプレート】
    case ENEMY_FLYING: {
        // 空を飛ぶAIの処理
        enemy.customTimer += ets;
        enemy.vx = cosf(enemy.customTimer * 0.05f) * WALK_SPEED * 0.5f; // 波打つように動く
        enemy.vy = 0.0f; // 重力を無視して空中浮遊
        enemy.x += enemy.vx;
        break;
    }

[Step 3] `WinMain` の初期化で、`enemy.type = ENEMY_FLYING` のようにタイプを指定して配置します。

--------------------------------------------------------------------------------
2. 新しい「ステージギミック (Gimmick)」の追加手順
--------------------------------------------------------------------------------
[Step 1] `GimmickType` 列挙型に新しいタイプを追加します。
    (例: `GIMMICK_MOVING_FLOOR` (動く足場) を追加)

[Step 2] 物理更新でのギミック更新処理を追加します。
    【テンプレート】
    case GIMMICK_MOVING_FLOOR: {
        // 左右に自動で動く足場の処理
        gim.customTimer += ts;
        gim.x = gim.val1 + sinf(gim.customTimer * 0.03f) * gim.val2; // 初期位置 val1 から範囲 val2 で往復
        break;
    }

[Step 3] 衝突判定（`CheckPlatformCollision`）や戦闘処理への適用：
    ギミックがダメージ床なら、「Player vs Gimmick」の衝突判定を `Combat Collisions` に追加して、
    プレイヤーをスタート地点へ戻す処理を記述します。

[Step 4] 描画ループ内のギミック描画処理に新しい描画コードを追加します。

--------------------------------------------------------------------------------
3. 新しい「アイテム (Item)」の追加手順
--------------------------------------------------------------------------------
アイテムのベースデータ構造（Item, ItemState, ItemType）はすでに定義済みです。
今後、マップ内に配置して回収できるようにするための手順です。

[Step 1] `ItemType` 列挙型に新しいタイプを追加します。
    (例: `ITEM_KEY` (鍵) を追加)

[Step 2] `WinMain` に `std::vector<Item> items;` コンテナへの初期配置を追加します。
    【テンプレート】
    items.push_back({ ITEM_COIN, 800.0f, 320.0f, 20.0f, 20.0f, 20.0f, 20.0f, 0.0f, 0.0f, 20.0f, 20.0f, true, false, false, {} });

[Step 3] 物理更新ループに「アイテムの更新・巻き戻し」を追加します。
    【テンプレート】
    for (auto& item : items) {
        if (item.isRewinding) {
            // アイテム回収状態の逆再生
            if (!item.history.empty()) {
                ItemState s = item.history.back();
                item.history.pop_back();
                item.x = s.x; item.y = s.y;
                item.isCollected = s.isCollected;
                if (!item.isCollected) item.isActive = true; // 回収前なら再出現
            }
        } else {
            if (!isPaused || isStepFrame) {
                // プレイヤーとの回収判定
                if (item.isActive && !item.isCollected) {
                    if (CheckCollision(player.x, player.y, pw, ph, item.x, item.y, item.width, item.height)) {
                        item.isCollected = true;
                        item.isActive = false; // 画面から消す
                        const ItemDef* idef = FindItemDef(item.assetId);
                        if (idef) SoundManager::Get().PlaySe(idef->seCollect);
                        // コイン獲得数増加などの処理
                    }
                }
                item.history.push_back({ item.x, item.y, item.isCollected });
            }
        }
    }

[Step 4] 描画処理で `DrawCircle` や金色の矩形としてアイテムを描画します。
    【テンプレート】
    for (const auto& item : items) {
        if (item.isActive && !item.isCollected) {
            DrawCircle((int)(item.x - cameraX), (int)item.y, 8, GetColor(255, 215, 0), TRUE); // 金色のコイン
        }
    }
================================================================================
*/






























