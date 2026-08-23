#include "DxLib.h"
#include <vector>
#include <stdio.h>
#include <fstream>
#include <string>
#include <map>
#include <cstdlib>
#include "json.hpp"
#include "Logger.h"
#include <exception>

#include "imgui.h"
#include "imgui_impl_win32.h"
#include "imgui_impl_dx11.h"
#include <d3d11.h>



extern IMGUI_IMPL_API LRESULT ImGui_ImplWin32_WndProcHandler(HWND hWnd, UINT msg, WPARAM wParam, LPARAM lParam);

LRESULT CALLBACK CustomWndProc(HWND hwnd, UINT msg, WPARAM wParam, LPARAM lParam) {
    if (ImGui_ImplWin32_WndProcHandler(hwnd, msg, wParam, lParam))
        return true;
    return 0;
}

bool isDedicatedEditorMode = false;

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
    std::string id = "";
    std::string sprite_path = "";
    int graphHandle = -1;
    float offsetX = 0.0f, offsetY = 0.0f; // 親からの初期相対オフセット（見た目のアンカー）
    int width = 0, height = 0;             // 既定=画像サイズ
    int hitboxOffsetX = 0, hitboxOffsetY = 0;
    int hitboxWidth = 32, hitboxHeight = 32;
    float scale = 1.0f;
    int hp = 0;      // 0=破壊不能(常在ハザード) / 1以上=個別に破壊可能
    int zOrder = 0;  // 負=親より奥に描画、正=親より手前
    json script = json::array();
};

// ランタイム側のパーツ状態。PartDefへのポインタは持たず、スポーン時に値をコピーする
// （この構造体を長時間保持するEnemy/Gimmick/Itemが、既存のenemyDefs等と同じく
//   毎フレームdefを再検索する慣習に合わせるため。詳細はプラン文書を参照）。
struct PartInstance {
    float x = 0.0f, y = 0.0f; // 見た目のアンカー座標（世界座標）。hitboxOffsetは焼き込まず判定のたびに加算する
    int handle = -1;
    float scale = 1.0f;
    float angle = 0.0f;
    int hp = 0;
    bool isActive = true;
    int hitboxOffsetX = 0, hitboxOffsetY = 0, hitboxWidth = 32, hitboxHeight = 32;
    int width = 0, height = 0;
    int zOrder = 0;
    int partIndex = 0;         // 親のparts[]内インデックス（PartIndexレポーター、Start()時の再検索に使う）
    ScriptState scriptState;   // OnSpawn用
    ScriptState reactiveState; // OnDamaged/OnDeath専用（Parts-M6）
};

struct EnemyDef {
    std::string id = "";
    std::string name = "";
    int type_enum = 0;
    int hp = 0;
    int width = 0;
    int height = 0;
    std::string sprite_path = "";
    int graphHandle = -1;
    std::string seSpawn = "";
    std::string seAttack = "";
    std::string seDamage = "";
    std::string seDeath = "";
    int hitboxOffsetX = 0;
    int hitboxOffsetY = 0;
    int hitboxWidth = 32;
    int hitboxHeight = 32;
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

    // Feature: Puzzle-like Behavior Scripting (M2) — type_enum==ENEMY_CUSTOM_SCRIPTの時に使うJSON ASTブロック配列
    json script = json::array();

    // Feature: Composite Multi-Part Objects (Parts-M1)
    std::vector<PartDef> parts;
};

struct GimmickDef {
    std::string id = "";
    std::string name = "";
    int type_enum = 0;
    std::string sprite_path = "";
    int graphHandle = -1;
    int hitboxOffsetX = 0;
    int hitboxOffsetY = 0;
    int hitboxWidth = 32;
    int hitboxHeight = 32;
    std::string seActivate = "";

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

    // Feature: Puzzle-like Behavior Scripting (M2) — type_enum==GIMMICK_CUSTOM_SCRIPTの時に使うJSON ASTブロック配列
    json script = json::array();

    // Feature: Composite Multi-Part Objects (Parts-M1)
    std::vector<PartDef> parts;
};

std::vector<EnemyDef> enemyDefs;
std::vector<GimmickDef> gimmickDefs;
struct PlacedEnemy {
    int def_idx;
    float x, y;
};
struct PlacedGimmick {
    int def_idx;
    float x, y;
    std::string stringParam = ""; // ポータルの遷移先など
};
std::vector<PlacedEnemy> editorPlacedEnemies;
std::vector<PlacedGimmick> editorPlacedGimmicks;
#include <filesystem>
namespace fs = std::filesystem;
std::string currentStageFileName = "stage_01.json";
struct ItemDef {
    std::string id = "";
    std::string name = "";
    int type_enum = 0;
    std::string sprite_path = "";
    std::string grant_ability = "";
    int graphHandle = -1;
    int hitboxOffsetX = 0;
    int hitboxOffsetY = 0;
    int hitboxWidth = 32;
    int hitboxHeight = 32;
    std::string seCollect = "";

    // Feature: Composite Multi-Part Objects (Parts-M1)
    std::vector<PartDef> parts;
};

struct PlayerCapabilities {
    bool canDoubleJump = false;
    bool canDash = false;
    bool canShootFireball = false;
    bool canFly = false;
    int baseJumpPower = -12;
    float baseSpeed = 4.0f;
};
PlayerCapabilities editorPlayerCaps;

// ===== Feature: 編集コストゲージ (Edit Cost Gauge) =====
struct EditToolFlags {
    bool rewindEnabled = true;
    bool pauseEnabled = true;
    bool fastForwardEnabled = true;
    bool screenEffectEnabled = true; // Z/X/C（ズーム・明暗）+ T（色フィルタ）をまとめた「画面エフェクト」
    bool objectEditEnabled = true;   // 右クリックによる個別オブジェクト編集（選択・コンテキストメニュー全体）
};

struct EditCostSettings {
    float maxCost = 100.0f;
    float regenPerSec = 6.0f;
    float drainRewindPerSec = 18.0f;
    float drainPausePerSec = 4.0f;
    float drainFastForwardPerSec = 10.0f;
    float drainScreenEffectPerSec = 8.0f; // Z/X/C保持中 or 色フィルタ非ゼロ中に加算
    float flatColorCycle = 5.0f;
    float flatMenuToggle = 8.0f;     // コンテキストメニューの巻き戻し/一時停止トグル、複数選択の一括トグルも同額
    float flatSpeedChange = 6.0f;
    float flatDirectionFlip = 4.0f;
    float flatResetAll = 10.0f;
};

// アイテムで恒久解禁された操作（セッション永続。editorPlayerCapsと同じ扱いでResetStageではクリアしない）
EditToolFlags unlockedEditTools = { false, false, false, false, false };

std::vector<ItemDef> itemDefs;
struct PlacedItem {
    int def_idx;
    float x, y;
};
std::vector<PlacedItem> editorPlacedItems;

// ==== Feature: Configurable Behavior Parameters (M1) ====
// EnemyDef/GimmickDefの新パラメータのうち -1 (未設定) のままのフィールドに、
// そのtype_enumが従来ハードコードされていた値をそのまま補完する。
// これにより既存のenemies.json/gimmicks.jsonを一切変更せずに挙動が完全に維持される。
// type_enumの数値は AssetManagerForm.cs の EnemyTypes/GimmickTypes 一覧の並び順と対応する。
void ApplyEnemyDefaultParams(EnemyDef& def) {
    auto fill = [](float& field, float legacyDefault) { if (field < 0.0f) field = legacyDefault; };
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
        case 7: fill(def.triggerRange, 24.0f); fill(def.fallDelay, 10.0f); fill(def.cooldownTime, 120.0f); fill(def.shockwaveRadius, 60.0f); fill(def.fastForwardJitter, 30.0f); fill(def.diagonalFallSpeed, 2.5f); break; // FALLER
        case 8: // SPREAD_SHOOTER
            fill(def.actionInterval, 150.0f); fill(def.spreadAngle, 0.35f); fill(def.projectileSpeed, 0.5f);
            if (def.spreadCount < 0) def.spreadCount = 3;
            break;
        case 9: fill(def.actionInterval, 130.0f); fill(def.projectileSpeed, 0.55f); break; // AIMED_SHOOTER
        case 10: fill(def.floatAmplitude, 40.0f); fill(def.floatFrequency, 0.05f); fill(def.moveSpeed, 0.2f); fill(def.triggerRange, 300.0f); break; // FLOATER
        case 11: fill(def.actionInterval, 180.0f); fill(def.teleportRangeMin, 120.0f); fill(def.teleportRangeMax, 220.0f); break; // TELEPORTER
        case 12: fill(def.moveSpeed, 0.35f); fill(def.enragedMoveSpeed, 0.9f); fill(def.shrinkFactor, 0.6f); fill(def.triggerRange, 300.0f); break; // SHRINKER
        case 13: fill(def.moveSpeed, 0.3f); fill(def.shieldOffDuration, 150.0f); fill(def.shieldOnDuration, 90.0f); fill(def.triggerRange, 300.0f); break; // SHIELD
        case 14: fill(def.mimicDelayFrames, 90.0f); break; // MIMIC_GHOST
        case 15: fill(def.moveSpeed, 0.25f); fill(def.sizeAmplitude, 0.5f); fill(def.sizeFrequency, 0.04f); fill(def.minScale, 0.4f); fill(def.triggerRange, 300.0f); break; // SIZE_SHIFTER
        case 16: fill(def.moveSpeed, 0.4f); fill(def.tempoFrequency, 0.05f); fill(def.tempoMin, 0.3f); fill(def.tempoMax, 1.6f); fill(def.triggerRange, 300.0f); break; // TEMPO_WARPER
        case 17: fill(def.moveSpeed, 0.3f); fill(def.effectRange, 320.0f); fill(def.brightnessMin, 0.35f); fill(def.triggerRange, 300.0f); break; // BRIGHTNESS_PHANTOM
        case 18: fill(def.moveSpeed, 0.3f); fill(def.effectRange, 320.0f); fill(def.tintStrength, 0.6f); fill(def.triggerRange, 300.0f); break; // COLOR_SHIFTER
        case 19: fill(def.effectRange, 280.0f); fill(def.zoomAmplitude, 0.25f); fill(def.zoomFrequency, 0.08f); fill(def.triggerRange, 300.0f); break; // ZOOM_DISRUPTOR
        default: break;
    }
}

void ApplyGimmickDefaultParams(GimmickDef& def) {
    auto fill = [](float& field, float legacyDefault) { if (field < 0.0f) field = legacyDefault; };
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
void ParsePartDefsFromJson(const json& parentObj, std::vector<PartDef>& outParts) {
    if (!parentObj.contains("parts") || !parentObj["parts"].is_array()) return;
    for (const auto& p : parentObj["parts"]) {
        if (!p.is_object()) continue;
        PartDef part;
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
        if (p.contains("script") && p["script"].is_array()) part.script = p["script"];
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

void LoadAssetDefinitions() {
    {
        std::ifstream ef("assets/enemies.json");
        if (ef.is_open()) {
            json ej = json::parse(ef, nullptr, false);
            if (!ej.is_discarded() && ej.is_array()) {
                for (const auto& e : ej) {
                    if (!e.is_object()) continue;
                    EnemyDef def;
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
                    if (!def.sprite_path.empty()) {
                        def.graphHandle = LoadGraph(def.sprite_path.c_str());
                        int gw, gh;
                        GetGraphSize(def.graphHandle, &gw, &gh);
                        def.width = gw;
                        def.height = gh;
                        if (def.hitboxWidth == 0) def.hitboxWidth = gw;
                        if (def.hitboxHeight == 0) def.hitboxHeight = gh;
                    }
                    // Feature: Configurable Behavior Parameters (M1)
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
                    if (e.contains("script") && e["script"].is_array()) def.script = e["script"];
                    ApplyEnemyDefaultParams(def);
                    ParsePartDefsFromJson(e, def.parts);
                    enemyDefs.push_back(def);
                }
            } else {
                Logger::Error("DrawPixel", "LoadAssetDefinitions", "Failed to parse enemies.json", "assets/enemies.json");
            }
        }
    }

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
                    // Feature: Puzzle-like Behavior Scripting (M2)
                    if (g.contains("script") && g["script"].is_array()) def.script = g["script"];
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
const EnemyDef* FindEnemyDef(const std::string& assetId) {
    for (const auto& d : enemyDefs) if (d.id == assetId) return &d;
    return nullptr;
}
const GimmickDef* FindGimmickDef(const std::string& assetId) {
    for (const auto& d : gimmickDefs) if (d.id == assetId) return &d;
    return nullptr;
}
const ItemDef* FindItemDef(const std::string& assetId) {
    for (const auto& d : itemDefs) if (d.id == assetId) return &d;
    return nullptr;
}

// Feature: Composite Multi-Part Objects (Parts-M1) — PartDef群からランタイムのPartInstance群を構築する。
// 親(敵/ギミック/アイテム)のResetStage時に呼び、baseX/baseYには親の(既に確定済みの)スポーン座標を渡す。
std::vector<PartInstance> BuildPartInstances(const std::vector<PartDef>& defs, float baseX, float baseY) {
    std::vector<PartInstance> result;
    for (size_t pi = 0; pi < defs.size(); pi++) {
        const PartDef& pd = defs[pi];
        PartInstance inst;
        inst.x = baseX + pd.offsetX;
        inst.y = baseY + pd.offsetY;
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
        inst.partIndex = (int)pi;
        result.push_back(inst);
    }
    return result;
}

// Feature: Composite Multi-Part Objects (Parts-M5) — パーツの描画。
// zOrder<0のパーツは親本体の描画より先に、zOrder>=0のパーツは後に呼ぶ2パス方式にするため、
// wantBehindParent引数でどちらのパスを描画するかを切り替える。
void DrawPartsPass(const std::vector<PartInstance>& parts, float cameraX, float cameraY, bool wantBehindParent) {
    for (const auto& part : parts) {
        if (!part.isActive) continue;
        if ((part.zOrder < 0) != wantBehindParent) continue;
        if (part.handle < 0) continue;
        int pcx = (int)(part.x + (part.width * part.scale) / 2.0f - cameraX);
        int pcy = (int)(part.y + (part.height * part.scale) / 2.0f - cameraY);
        DrawRotaGraph(pcx, pcy, part.scale, part.angle, part.handle, TRUE);
    }
}



// 定数
const int SCREEN_WIDTH = 640;
const int SCREEN_HEIGHT = 480;
const int WINDOW_WIDTH = 1280;
const int WINDOW_HEIGHT = 720;
const float GRAVITY = 0.5f;
const int MAX_BULLETS = 40;
const float BULLET_SPEED = 20.0f;
const float JUMP_POWER = -12.0f;
const float WALK_SPEED = 4.0f;
const float DASH_SPEED = 8.0f;

const int TILE_SIZE = 32;
const int MAP_WIDTH_TILES = 80;  // 2560 / 32 = 80
const int MAP_HEIGHT_TILES = 15; // 480 / 32 = 15

enum TileType {
    TILE_NONE = 0,
    TILE_JIMEN = 1,
    TILE_TUTI = 2,
    TILE_TYPE_MAX
};

enum GameScene {
    PLAY,
    RESULT_GAMEOVER,
    RESULT_VICTORY
};

struct TileDefinition {
    TileType type;
    int handle;
    bool isCollidable;
    bool deadly = false;   // 返った場合ただちゲームオーバー
    const char* name;
    // Feature: タイル表示範囲調整機能 — spriteが複数タイルをまとめたタイルセット画像の場合に、
    // そのうちどの矩形部分を表示に使うかを指定する。srcW/srcHが0のままなら画像全体を使う
    // （tileDefs構築直後の解決ループで画像サイズへ解決される。従来互換）。
    int srcX = 0, srcY = 0, srcW = 0, srcH = 0;
};

// 履歴上限
const size_t MAX_HISTORY_FRAMES = 600; // 60fpsで約10秒間

// 個々の履歴ステート
struct PlayerState {
    float x, y, vx, vy;
    int direction;
    bool isJumping;
    float scale, angle, speedScale;
    bool isPaused;
    int hp;
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

    ENEMY_TYPE_COUNT          // 種別数の番兵。新しい敵タイプは必ずこの直前に追加すること
};

struct EnemyState {
    float x, y, vx, vy;
    int direction;
    float scale, angle, speedScale;
    bool isActive;
    bool isPaused;
    EnemyType type;
    int hp;

    // AI内部状態（巻き戻し時に座標だけでなく状態機械も過去に戻すため保持する）
    float customTimer;
    int aiState;
    float patrolLeft;
    float patrolRight;
    float auxF1;
    float auxF2;
    int auxState;
    bool auxFlag;
    float auxF3;
};

struct BulletState {
    float x, y, vx, vy;
    bool isActive;
    bool isPlayerOwned;
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
    GIMMICK_CUSTOM_SCRIPT            // Feature: Puzzle-like Behavior Scripting (M2) — GimmickDef.scriptのJSONブロックで挙動を自作する
};

struct GimmickState {
    float x, y;
    float angle;
    bool isActive;
};

// 将来の拡張用のアイテムシステムテンプレート
enum ItemType {
    ITEM_NONE,
    ITEM_COIN,
    ITEM_HEAL
};

struct ItemState {
    float x, y;
    bool isCollected;
};

struct Item {
    ItemType type;
    float x, y;
    float width, height;
    float spriteWidth, spriteHeight;
    float hitboxOffsetX, hitboxOffsetY;
    float hitboxWidth, hitboxHeight;
    bool isActive;
    bool isCollected;

    // 巻き戻しトラック
    bool isRewinding;
    std::vector<ItemState> history;

    std::string assetId = ""; // ItemDef.id への参照（SE検索用）
    int handle = -1;          // ItemDef.graphHandle（カスタムスプライト）。-1ならcoinHandleを使う

    // Feature: Composite Multi-Part Objects (Parts-M1)
    std::vector<PartInstance> parts;
};

// 主要な構造体
struct Platform {
    float x1, y1;
    float x2, y2;
};

struct Player {
    float x, y;
    float vx, vy;
    int handle;
    int direction;
    bool isJumping;
    int width, height;
    float scale;
    float angle;
    float speedScale;
    bool isPaused;
    int hp;

    // 被ダメージ後の無敵時間（フレーム）。0より大きい間は敵との接触ダメージを受けない
    float invulnTimer = 0.0f;

    // 現在乗っている動くギミックのインデックス（乗っていなければ-1）。ギミックの移動量を追従させるために使う
    int ridingGimmickIndex = -1;

    // 巻き戻しトラック
    bool isRewinding;
    std::vector<PlayerState> history;

    // Feature 2: アニメーション
    AnimationController anim;
};

struct Enemy {
    EnemyType type;
    float x, y;
    float vx, vy;
    int handle;
    int direction; 
    int width, height;
    int spriteWidth, spriteHeight;
    int hitboxOffsetX, hitboxOffsetY;
    int hitboxWidth, hitboxHeight;
    float scale;
    float angle;
    float speedScale;
    bool isActive;
    bool isPaused;
    int hp;

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
};

struct Gimmick {
    GimmickType type;
    float x, y;
    float width, height;
    float spriteWidth, spriteHeight;
    float hitboxOffsetX, hitboxOffsetY;
    float hitboxWidth, hitboxHeight;
    bool isActive;
    float val1, val2; // ギミック固有の値 (例: ポータルのワープ先ターゲット比率など)
    float angle;      // 橋の回転角度
    float customTimer;
    bool isPaused;    // 個別ギミックの一時停止

    // 巻き戻しトラック
    bool isRewinding;
    std::vector<GimmickState> history;

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

    // 動くギミックに乗っているプレイヤー/敵を追従させるための、直近フレームの移動量
    float lastDeltaX = 0.0f, lastDeltaY = 0.0f;
};

struct Bullet {
    float x = 0.0f;
    float y = 0.0f;
    float vx = 0.0f;
    float vy = 0.0f;
    bool isActive = false;
    int handle = 0;
    bool isPlayerOwned = true; // 弾丸の所有者の追跡用

    // 巻き戻しトラック
    bool isRewinding = false;
    std::vector<BulletState> history;
};

struct CutPoint {
    float timePos;
    float targetTimePos;
};

struct ContextMenu {
    bool isOpen;
    int x, y, width, height;
};

struct BackgroundLayer {
    std::string sprite;
    int handle = -1;
    int drawOrder = 0;
    float scrollRate = 0.3f;
    bool loop = true;
    float offsetX = 0.0f;
    float offsetY = 0.0f;
};

struct StageData {
    int id = 0;
    char name[64] = "";
    std::string sourceFile = ""; // assets/stages/ 配下のファイル名（GoToStageでの再読み込み判定に使用）
    std::vector<std::vector<int>> map;
    
    // Feature 1
    std::vector<std::vector<int>> decoMapBack;
    std::vector<std::vector<int>> decoMapFront;
    std::vector<BackgroundLayer> backgrounds;
    
    // Feature 3
    std::string bgmId = "";
    
    // Feature 4
    bool testMode = false;

    // Feature 5
    json triggersJson = json::array();

    float playerStartX = 0.0f;
    float playerStartY = 0.0f;
    float goalX = -1.0f;   // -1 = ゴール未設定
    float goalY = -1.0f;
    std::vector<Enemy> enemies;
    std::vector<Gimmick> gimmicks;
    std::vector<Item> items;
    std::vector<Platform> platforms;

    // Feature: 編集コストゲージ（ステージ単位設定）
    EditToolFlags editToolFlags;
    EditCostSettings editCostSettings;
};

enum SelectedType {
    SELECT_NONE,
    SELECT_PLAYER,
    SELECT_ENEMY,
    SELECT_GIMMICK
};

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
                 || (gim.type == GIMMICK_CHIKUWA_BLOCK && gim.angle == 0.0f)) {
            if (CheckLanded(gim.x, gim.y, gim.x + gim.width)) {
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

void CheckGimmickCollisionX(float& x, float y, int width, float scale, int height, float vx, const std::vector<Gimmick>& gimmicks) {
    float objW = (float)width * scale;
    float objH = (float)height * scale;
    
    for (const auto& gim : gimmicks) {
        if (!gim.isActive) continue;
        // Feature: ゲート扉の修正（友人フィードバック対応）— GIMMICK_GATE_DOORを横方向の実体判定に追加。
        // isActive==true（施錠中/閉状態）の間だけこのループに乗り、通行を塞げるようにする
        // （元々ここに無かったため、閉じているはずのドアを素通りできてしまっていた）。
        if (gim.type == GIMMICK_SCALABLE_BOX || gim.type == GIMMICK_SCALABLE_GROUND || gim.type == GIMMICK_PUSHABLE_ROCK || gim.type == GIMMICK_FASTFORWARD_GATE || gim.type == GIMMICK_GATE_DOOR) {
            // Y軸の重なり判定（少しの遊びを持たせる）
            if (y + objH > gim.y + 4.0f && y < gim.y + gim.height - 4.0f) {
                float toleranceGX = std::abs(vx) + 12.0f; // 高速移動時のすり抜け防止のため速度依存にする
                if (vx > 0.0f) { // 右方向へ移動中
                    if (x < gim.x && x + objW > gim.x && x + objW <= gim.x + toleranceGX) {
                        x = gim.x - objW;
                    }
                }
                else if (vx < 0.0f) { // 左方向へ移動中
                    if (x > gim.x + gim.width - toleranceGX && x < gim.x + gim.width) {
                        x = gim.x + gim.width;
                    }
                }
            }
        }
    }
}

bool CheckGimmickCollisionY(float x, float& y, int width, float scale, int height, float& vy, const std::vector<Gimmick>& gimmicks) {
    float objW = (float)width * scale;
    float objH = (float)height * scale;
    bool isGrounded = false;
    
    for (const auto& gim : gimmicks) {
        if (!gim.isActive) continue;
        if (gim.type == GIMMICK_SCALABLE_BOX || gim.type == GIMMICK_SCALABLE_GROUND || gim.type == GIMMICK_PUSHABLE_ROCK || gim.type == GIMMICK_FASTFORWARD_GATE) {
            // X軸の重なり判定
            if (x + objW > gim.x + 2.0f && x < gim.x + gim.width - 2.0f) {
                if (vy >= 0.0f) { // 下方向へ移動中（着地）
                    float footY = y + objH;
                    float threshold = vy + 12.0f;
                    if (footY >= gim.y && footY <= gim.y + threshold) {
                        y = gim.y - objH;
                        vy = 0.0f;
                        isGrounded = true;
                    }
                }
                else if (vy < 0.0f) { // 上方向へ移動中（頭ぶつけ）
                    float threshold = -vy + 12.0f;
                    if (y <= gim.y + gim.height && y >= gim.y + gim.height - threshold) {
                        y = gim.y + gim.height;
                        vy = 0.0f;
                    }
                }
            }
        }
    }
    return isGrounded;
}


bool UpdatePhysicsCollisions(float& x, float& y, float dx, float dy, float& vy, int width, int height, float scale, 
                             const std::vector<std::vector<int>>& map, const std::vector<TileDefinition>& tileDefs, 
                             const std::vector<Gimmick>& gimmicks) {
    x += dx;
    CheckGridCollisionX(x, y, width, scale, height, map, tileDefs, dx);
    CheckGimmickCollisionX(x, y, width, scale, height, dx, gimmicks);
    
    y += dy;
    bool gridGrounded = CheckGridCollisionY(x, y, vy, width, scale, height, map, tileDefs);
    bool gimGrounded = CheckGimmickCollisionY(x, y, width, scale, height, vy, gimmicks);
    
    return gridGrounded || gimGrounded;
}

int WINAPI WinMain(_In_ HINSTANCE h, _In_opt_ HINSTANCE hp, _In_ LPSTR l, _In_ int n)
{
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

    // DxLibへ渡す文字列の扱いをUTF-8にする（DxLib_Initより前に呼ぶ必要がある）。
    // 既定はShift-JIS解釈のため、assets/*.json（UTF-8）由来の日本語をDrawStringへ渡すと
    // 文字化けしていた（ShowMessageのヒント等）。ソースもUTF-8なので全体をUTF-8で統一する。
    // なお既存のDrawString呼び出しの文字列は全てASCIIのため、この変更で崩れる表示は無い。
    SetUseCharCodeFormat(DX_CHARCODEFORMAT_UTF8);

    ChangeWindowMode(TRUE);
    SetGraphMode(WINDOW_WIDTH, WINDOW_HEIGHT, 32);
    if (DxLib_Init() == -1) return -1;
    Logger::Info("System", "WinMain", "[Init] DxLib_Init Success");
    if (l != nullptr && strlen(l) > 0) {
        currentStageFileName = l;
        // コマンドライン引数の前後に空白/引用符が付くケースへの防御（呼び出し元により混入することがある）
        while (!currentStageFileName.empty() && (currentStageFileName.front() == ' ' || currentStageFileName.front() == '"')) currentStageFileName.erase(0, 1);
        while (!currentStageFileName.empty() && (currentStageFileName.back() == ' ' || currentStageFileName.back() == '"')) currentStageFileName.pop_back();
        if (!currentStageFileName.empty() && currentStageFileName.find(".json") == std::string::npos) {
            currentStageFileName += ".json";
        }
    }
    SetDrawScreen(DX_SCREEN_BACK);

    HWND hwnd = GetMainWindowHandle();
    SetHookWinProc(CustomWndProc);

    IMGUI_CHECKVERSION();
    ImGui::CreateContext();
    ImGuiIO& io = ImGui::GetIO(); (void)io;
    io.ConfigFlags |= ImGuiConfigFlags_DockingEnable;
    io.ConfigFlags |= ImGuiConfigFlags_ViewportsEnable; // マルチビューポートを有効化（UIを独立したOSウィンドウにできる）
    io.Fonts->AddFontFromFileTTF("C:\\Windows\\Fonts\\meiryo.ttc", 18.0f, NULL, io.Fonts->GetGlyphRangesJapanese());

    ImGui::StyleColorsDark();

    ImGui_ImplWin32_Init(hwnd);
    ImGui_ImplDX11_Init((ID3D11Device*)GetUseDirect3D11Device(), (ID3D11DeviceContext*)GetUseDirect3D11DeviceContext());

    int gameScreen = MakeScreen(SCREEN_WIDTH, SCREEN_HEIGHT, TRUE);
    LoadAssetDefinitions();
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
    int playerHandle = LoadGraph("img/player.png");
    int bulletHandle = LoadGraph("img/bullet.png");
    int jimenHandle = LoadGraph("img/jimen.png");
    int tutiHandle = LoadGraph("img/tuti.png");
    int portalHandle = LoadGraph("img/portal.png");
    int bridgeHandle = LoadGraph("img/bridge.png");
    int breakableBlockHandle = LoadGraph("img/breakable_block.png");
    int liftHandle = LoadGraph("img/lift.png");
    int mirrorHandle = LoadGraph("img/mirror.png");
    int switchHandle = LoadGraph("img/switch.png");
    int boxHandle = LoadGraph("img/box.png");
    int doorHandle = LoadGraph("img/door.png");
    int spikesHandle = LoadGraph("img/spikes.png");
    int coinHandle = LoadGraph("img/coin.png");

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
                        handle = LoadGraph(("assets/" + spritePath).c_str());
                        if (handle == -1) {
                            // Try absolute or relative from executable if not in assets/
                            handle = LoadGraph(spritePath.c_str());
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

    int pw, ph;
    GetGraphSize(playerHandle, &pw, &ph);

    // オブジェクトの仮初期化（ResetStageで正しく設定されます）
    Player player = { 100.0f, 300.0f, 0.0f, 0.0f, playerHandle, 0, false, pw, ph, 1.0f, 0.0f, 1.0f, false, false, {} };
    std::vector<Enemy> enemies;
    std::vector<Platform> platforms;
    std::vector<Gimmick> gimmicks;
    std::vector<Item> items;

    std::vector<StageData> stages;
    
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
    
    
    float cameraX = 0.0f;
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
                        jsonStage.editToolFlags.rewindEnabled       = etf.value("rewind", true);
                        jsonStage.editToolFlags.pauseEnabled        = etf.value("pause", true);
                        jsonStage.editToolFlags.fastForwardEnabled  = etf.value("fastForward", true);
                        jsonStage.editToolFlags.screenEffectEnabled = etf.value("screenEffect", true);
                        jsonStage.editToolFlags.objectEditEnabled   = etf.value("objectEdit", true);
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

        player.x = stage.playerStartX;
        player.y = stage.playerStartY;
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

    // 最初のステージ初期化
    ResetStage();

    while (ProcessMessage() == 0 && CheckHitKey(KEY_INPUT_ESCAPE) == 0)
    {
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

        if (isEditMode && objectEditOpEnabled) {
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
                        for (auto& gim : gimmicks) {
                            if (gx >= gim.x && gx <= gim.x + gim.spriteWidth &&
                                gy >= gim.y && gy <= gim.y + gim.spriteHeight) {
                                selectedPlayers.clear(); selectedEnemies.clear(); selectedGimmicks.clear();
                                selectedGimmicks.push_back(&gim);
                                selectedType = SELECT_GIMMICK;
                                targetScale = &gim.width; // スケールを幅にマッピング！
                                targetAngle = &gim.angle;
                                targetSpeedScale = &gim.customTimer;
                                targetPaused = &gim.isPaused;
                                targetRewind = &gim.isRewinding;
                                targetDirection = nullptr;
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
                    {
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
                        // 速度 +0.5（Feature: 編集コストゲージ。ギミック選択時は元々no-opなので課金しない）
                        else if (my >= menu.y + 56 && my <= menu.y + 80) {
                            if (selectedType != SELECT_GIMMICK) {
                                if (editCost >= currentEditCost.flatSpeedChange) { editCost -= currentEditCost.flatSpeedChange; *targetSpeedScale += 0.5f; }
                                else SoundManager::Get().PlaySe("ui_denied");
                            }
                            menu.isOpen = false;
                        }
                        // 速度 -0.5（Feature: 編集コストゲージ）
                        else if (my >= menu.y + 81 && my <= menu.y + 105) {
                            if (selectedType != SELECT_GIMMICK) {
                                if (editCost >= currentEditCost.flatSpeedChange) {
                                    editCost -= currentEditCost.flatSpeedChange;
                                    *targetSpeedScale -= 0.5f;
                                    if (*targetSpeedScale < 0) *targetSpeedScale = 0;
                                } else SoundManager::Get().PlaySe("ui_denied");
                            }
                            menu.isOpen = false;
                        }
                        // オブジェクトの向きを反転（Feature: 編集コストゲージ）
                        else if (my >= menu.y + 106 && my <= menu.y + 130) {
                            if (targetDirection != nullptr) {
                                if (editCost >= currentEditCost.flatDirectionFlip) { editCost -= currentEditCost.flatDirectionFlip; *targetDirection = (*targetDirection == 0 ? 1 : 0); }
                                else SoundManager::Get().PlaySe("ui_denied");
                            }
                            menu.isOpen = false;
                        }
                        // すべてリセット（Feature: 編集コストゲージ）
                        else if (my >= menu.y + 131 && my <= menu.y + 155) {
                            if (editCost >= currentEditCost.flatResetAll) {
                                editCost -= currentEditCost.flatResetAll;
                                if (selectedType == SELECT_GIMMICK && targetGimmick != nullptr) {
                                    targetGimmick->width = 120.0f;
                                    targetGimmick->spriteWidth = 120.0f; // 描画側(spriteWidth)も併せて戻す
                                    targetGimmick->angle = (targetGimmick->type == GIMMICK_MANUAL_BRIDGE) ? 1.57079f : 0.0f;
                                    targetGimmick->isPaused = false;
                                    targetGimmick->isRewinding = false;
                                } else {
                                    *targetScale = 1.0f; *targetAngle = 0.0f; *targetSpeedScale = 1.0f; *targetPaused = false; *targetRewind = false;
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
                if (!lastLeftClick && mx >= WINDOW_WIDTH / 2 - 50 && mx <= WINDOW_WIDTH / 2 + 50 && my >= WINDOW_HEIGHT - 90 && my <= WINDOW_HEIGHT - 70) {
                    if (isPaused) { isPaused = false; SoundManager::Get().PlaySe("ui_pause"); }
                    else if (pauseOpEnabled && editCost > 0.0f) { isPaused = true; SoundManager::Get().PlaySe("ui_pause"); }
                    else { SoundManager::Get().PlaySe("ui_denied"); }
                }

                // Feature: ポータルの作り直し（友人フィードバック対応）— タイムライン上のCtrl+クリックによる
                // ポータル生成は廃止。ポータルはC#側のLab_Editor（通常のX,Y配置フロー）で作成する。

                // インスペクターのドラッグと切り替え（選択されたすべてに適用）
                if (!lastLeftClick && mx >= WINDOW_WIDTH - 240 && selectedType != SELECT_NONE) {
                    if (my >= 80 && my <= 95) { isInspScale = true; lastMouseX = mx; baseScale = *targetScale; }
                    else if (my >= 100 && my <= 115) { isInspAngle = true; lastMouseX = mx; baseAngle = *targetAngle; }
                    else if (my >= 120 && my <= 135) { isInspSpeed = true; lastMouseX = mx; baseSpeed = *targetSpeedScale; }
                    else if (my >= 140 && my <= 155) {
                        // Feature: 編集コストゲージ — 複数選択でも定額（対象数に関わらず一発分のコスト）
                        if (editCost >= currentEditCost.flatMenuToggle) {
                            editCost -= currentEditCost.flatMenuToggle;
                            bool nextPaused = !(*targetPaused);
                            for (auto* p : selectedPlayers) p->isPaused = nextPaused;
                            for (auto* e : selectedEnemies) e->isPaused = nextPaused;
                            for (auto* g : selectedGimmicks) g->isPaused = nextPaused;
                        } else SoundManager::Get().PlaySe("ui_denied");
                    }
                    else if (my >= 160 && my <= 175) {
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
                if (currentGKey && !lastGKey) {
                    int imgW, imgH;
                    GetGraphSize(jimenHandle, &imgW, &imgH);
                    float h = 32.0f;
                    float tileW = h * ((float)imgW / imgH);
                    float w = tileW * 3.0f; // 初期は3タイル分
                    gimmicks.push_back({ GIMMICK_SCALABLE_GROUND, gx - w / 2.0f, gy - h / 2.0f, w, h, w, h, 0.0f, 0.0f, true, 0.0f, 0.0f, 0.0f, 0.0f, false, false, {} });
                }
                lastGKey = currentGKey;

                // オブジェクトの選択と変形（タイムラインやパネル内ではなく、モニター画面のプレビュー内のみで選択されるようにする）
                if (!lastLeftClick && !isDragging && !isScaling && !isScalingHeight && !isRotating && !isInspScale && !isInspAngle && !isInspSpeed && !isAreaSelecting &&
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
                                            targetSpeedScale = &gim.customTimer;
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
                if (isDragging && selectedType != SELECT_NONE) {
                    float prevGx = (float)(lastMouseX - monitorX) + cameraX;
                    float prevGy = (float)(lastMouseY - monitorY) + cameraY;
                    float dx = gx - prevGx;
                    float dy = gy - prevGy;

                    for (auto* p : selectedPlayers) { p->x += dx; p->y += dy; p->vx = 0; p->vy = 0; }
                    for (auto* e : selectedEnemies) { e->x += dx; e->y += dy; e->vx = 0; e->vy = 0; }
                    for (auto* g : selectedGimmicks) { g->x += dx; g->y += dy; }
                    
                    lastMouseX = mx;
                    lastMouseY = my;
                }
                if (isScaling && selectedType != SELECT_NONE) {
                    float ds = (float)(lastMouseY - my) * 0.01f;
                    float dw = (float)(lastMouseY - my) * 1.0f;
                    for (auto* p : selectedPlayers) { p->scale += ds; if (p->scale < 0.1f) p->scale = 0.1f; }
                    for (auto* e : selectedEnemies) { e->scale += ds; if (e->scale < 0.1f) e->scale = 0.1f; }
                    for (auto* g : selectedGimmicks) {
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
                    for (auto* e : selectedEnemies) { e->angle += da; }
                    for (auto* g : selectedGimmicks) { g->angle += da; }
                    lastMouseX = mx;
                }
                if (isInspScale && selectedType != SELECT_NONE) {
                    float ds = (float)(mx - lastMouseX) * 0.01f;
                    float dw = (float)(mx - lastMouseX) * 1.0f;
                    for (auto* p : selectedPlayers) { p->scale += ds; if (p->scale < 0.1f) p->scale = 0.1f; }
                    for (auto* e : selectedEnemies) { e->scale += ds; if (e->scale < 0.1f) e->scale = 0.1f; }
                    for (auto* g : selectedGimmicks) { g->width += dw; if (g->width < 10.0f) g->width = 10.0f; g->spriteWidth = g->width; }
                    lastMouseX = mx;
                }
                if (isInspAngle && selectedType != SELECT_NONE) {
                    float da = (float)(mx - lastMouseX) * 0.02f;
                    for (auto* p : selectedPlayers) { p->angle += da; }
                    for (auto* e : selectedEnemies) { e->angle += da; }
                    for (auto* g : selectedGimmicks) { g->angle += da; }
                    lastMouseX = mx;
                }
                if (isInspSpeed && selectedType != SELECT_NONE) {
                    float dsp = (float)(mx - lastMouseX) * 0.05f;
                    for (auto* p : selectedPlayers) { p->speedScale += dsp; if (p->speedScale < 0) p->speedScale = 0; }
                    for (auto* e : selectedEnemies) { e->speedScale += dsp; if (e->speedScale < 0) e->speedScale = 0; }
                    lastMouseX = mx;
                }
            } else { 
                isDragging = isScaling = isScalingHeight = isRotating = false; 
                isInspScale = isInspAngle = isInspSpeed = false;
            }
        }

        // --- 装置等による更新可否判定 ---
        auto CanUpdate = [&](float ox, float oy, float ow, float oh, float scale, bool isObjPaused) {
            if (currentScene != PLAY || isDragging || isScaling || isScalingHeight || isRotating || isShowingMessage) return false;
            if (!isPaused && !isObjPaused && !isInspScale && !isInspAngle && !isInspSpeed) return true;
            if (isStepFrame) return true;
            
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
                    bullets[i].vy = 0.0f; // Initialize vertical velocity to 0
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
                float prevPlayerX = player.x; // Keep track of previous X to detect precise boundary crossing

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
                    bool touching = CheckCollision(player.x, player.y, pw_scaled, ph_scaled, gim.x, gim.y, gim.spriteWidth, gim.spriteHeight);
                    if (touching && !gim.portalWasTouching && !gim.param.empty()) {
                        for (auto& other : gimmicks) {
                            if (&other == &gim || other.type != GIMMICK_CUT_PORTAL || !other.isActive) continue;
                            if (other.param != gim.param) continue;
                            player.x = other.x;
                            player.y = other.y;
                            other.portalWasTouching = true; // 転送先での即時再トリガーを防ぐ
                            const GimmickDef* gdef = FindGimmickDef(gim.assetId);
                            if (gdef) SoundManager::Get().PlaySe(gdef->seActivate);
                            break;
                        }
                    }
                    gim.portalWasTouching = touching;
                }

                // ステージ境界の制限
                if (player.x < 0.0f) player.x = 0.0f;
                if (player.x > STAGE_WIDTH - (float)player.width * player.scale) {
                    player.x = STAGE_WIDTH - (float)player.width * player.scale;
                }

                // 1. 砂時計リフトの更新（プレイヤーが乗っているとゆっくり降下）
                for (auto& gim : gimmicks) {
                    if (gim.type == GIMMICK_FALLING_LIFT && gim.isActive) {
                        const GimmickDef* gdef = FindGimmickDef(gim.assetId);
                        float sinkSpeed = gdef ? gdef->sinkSpeed : 1.5f;
                        float maxDepth = gdef ? gdef->maxDepthOffset : 20.0f;
                        float objW = (float)player.width * player.scale;
                        float objH = (float)player.height * player.scale;
                        float footY = player.y + objH;
                        // プレイヤーがこのリフトに乗っているかチェック
                        if (player.vy >= 0.0f && player.x + objW >= gim.x && player.x <= gim.x + gim.spriteWidth) {
                            float threshold = player.vy + 8.0f;
                            if (footY >= gim.y && footY <= gim.y + threshold) {
                                gim.y += sinkSpeed * pts; // ゆっくり降下
                                if (gim.y > groundY - maxDepth) gim.y = groundY - maxDepth; // 地面を限界とする
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
                    float switchTriggerThreshold = gdef ? gdef->triggerWidthThreshold : 140.0f;
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
                    switchStates.push_back({ &switchGim, active });
                }

                // スイッチの状態をゲートドアのロックに適用
                for (auto& gim : gimmicks) {
                    if (gim.type == GIMMICK_GATE_DOOR) {
                        if (gim.val1 > 0.5f) continue; // OpenDoorイベントで手動オープン済みのドアは自動ロジックの対象外
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
                bool canEnemyAct = enemy.isActive && CanUpdate(enemy.x, enemy.y, (float)enemy.hitboxWidth, (float)enemy.hitboxHeight, enemy.scale, enemy.isPaused);
                if (canEnemyAct) {
                    float ets = ts * enemy.speedScale;

                    // 直前フレームに乗っていた動くギミックの移動量を追従させる（1フレーム遅延の簡易キャリー）
                    if (enemy.ridingGimmickIndex >= 0 && enemy.ridingGimmickIndex < (int)gimmicks.size()) {
                        enemy.x += gimmicks[enemy.ridingGimmickIndex].lastDeltaX;
                        enemy.y += gimmicks[enemy.ridingGimmickIndex].lastDeltaY;
                    }

                    // Apply Gravity first
                    enemy.vy += GRAVITY * ets;

                    // Feature: Configurable Behavior Parameters (M1) — このフレームのEnemyDefを一度だけ引く
                    const EnemyDef* edef = FindEnemyDef(enemy.assetId);

                    // Switch behavior based on EnemyType
                    switch (enemy.type) {
                        case ENEMY_PATROL: {
                            // シンプルAI：patrolLeft ～ patrolRight の範囲でパトロール
                            float enemySpeed = editorPlayerCaps.baseSpeed * ets * (edef ? edef->moveSpeed : 0.4f);
                            // 巡回範囲が未設定なら ±200px で初期化
                            if (enemy.patrolLeft < 0 && enemy.patrolRight < 0) {
                                enemy.patrolLeft = std::max<float>(0.0f, enemy.x - 200.0f);
                                enemy.patrolRight = enemy.x + 200.0f;
                            }
                            if (enemy.direction == 0) {
                                enemy.vx = enemySpeed;
                                if (enemy.patrolRight > 0 && enemy.x + enemy.vx >= enemy.patrolRight) {
                                    enemy.direction = 1;
                                }
                            } else {
                                enemy.vx = -enemySpeed;
                                if (enemy.x + enemy.vx <= enemy.patrolLeft) {
                                    enemy.direction = 0;
                                }
                            }
                            break;
                        }
                        case ENEMY_JUMPER: {
                            // ジャンプAI：定期的にジャンプ（タイルマップ＋足場両方で着地チェック）
                            float jumpInterval = edef ? edef->actionInterval : 90.0f;
                            float jumpPowerMult = edef ? edef->jumpPowerMult : 0.7f;
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
                                enemy.vy = (float)editorPlayerCaps.baseJumpPower * jumpPowerMult;
                                enemy.customTimer = 0.0f;
                            }
                            break;
                        }
                        case ENEMY_STATIONARY: {
                            // 固定AI：全方位からプレイヤーへ正確に狙い撃つ。発射直前は画面を軽くズームインして溜めを予告する。
                            // 編集機能連動：早送り中は攻撃間隔がfastForwardAttackMult倍で詰まり、雑に駆け抜けるとリスクが上がる
                            // （一時停止はCanUpdate経由で既にタイマーごと止まるため、丁寧に近づけば安全という差別化になる）。
                            float shootInterval = edef ? edef->actionInterval : 120.0f;
                            float projSpeed = edef ? edef->projectileSpeed : 0.6f;
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
                                for (int i = 0; i < MAX_BULLETS; i++) {
                                    if (!bullets[i].isActive) {
                                        bullets[i].isActive = true;
                                        bullets[i].x = eCenterXs;
                                        bullets[i].y = eCenterYs;
                                        float spdS = BULLET_SPEED * projSpeed;
                                        bullets[i].vx = dxS / distS * spdS;
                                        bullets[i].vy = dyS / distS * spdS;
                                        bullets[i].isPlayerOwned = false; // 敵の弾
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
                            float detectX = edef ? edef->triggerRange : 300.0f;
                            float detectY = edef ? edef->detectionRangeY : 100.0f;
                            float patrolSpd = edef ? edef->moveSpeed : 0.5f;
                            float cooldown = edef ? edef->cooldownTime : 60.0f;
                            float projSpeed = edef ? edef->projectileSpeed : 0.5f;
                            float ffAtkMultP = edef ? edef->fastForwardAttackMult : 2.2f;
                            float pCenterX = player.x + (player.width * player.scale) / 2.0f;
                            float pCenterY = player.y + (player.height * player.scale) / 2.0f;
                            float eCenterX = enemy.x + (enemy.hitboxWidth * enemy.scale) / 2.0f;
                            float eCenterY = enemy.y + (enemy.hitboxHeight * enemy.scale) / 2.0f;
                            float distX = std::abs(pCenterX - eCenterX);
                            float distY = std::abs(pCenterY - eCenterY);

                            // aiState: 0=PATROL, 1=ATTACK
                            if (distX <= detectX && distY <= detectY) enemy.aiState = 1; // ATTACK
                            else enemy.aiState = 0; // PATROL

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
                            float triggerRangeWk = edef ? edef->triggerRange : 300.0f;
                            float speed = editorPlayerCaps.baseSpeed * ets * (edef ? edef->moveSpeed : 0.35f);
                            if (std::abs(player.x - enemy.x) < triggerRangeWk) {
                                enemy.direction = (player.x < enemy.x) ? 1 : 0;
                                enemy.vx = (enemy.direction == 1) ? -speed : speed;

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
                                if (!groundAhead) enemy.vx = 0.0f;
                            } else {
                                enemy.vx = 0.0f;
                            }
                            break;
                        }
                        case ENEMY_CHASER: {
                            // 追っかけてくる：索敵範囲内にいる間だけWALKERに加え、壁に当たると自動でジャンプする
                            float triggerRangeCh = edef ? edef->triggerRange : 300.0f;
                            float speed = editorPlayerCaps.baseSpeed * ets * (edef ? edef->moveSpeed : 0.55f);
                            float jumpPowerMult = edef ? edef->jumpPowerMult : 0.8f;
                            if (std::abs(player.x - enemy.x) < triggerRangeCh) {
                                enemy.direction = (player.x < enemy.x) ? 1 : 0;
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
                                if (wallAhead && std::abs(enemy.vy) < 1.0f) {
                                    enemy.vy = (float)editorPlayerCaps.baseJumpPower * jumpPowerMult;
                                }
                            } else {
                                enemy.vx = 0.0f;
                            }
                            break;
                        }
                        case ENEMY_DASH_CHARGER: {
                            // 突進：射程内に入ると溜め→高速直進→クールダウン。auxState: 0待機/1溜め/2突進/3クールダウン
                            float triggerRange = edef ? edef->triggerRange : 260.0f;
                            float chargeTime = edef ? edef->chargeTime : 30.0f;
                            float dashSpeedMult = edef ? edef->dashSpeedMult : 1.5f;
                            float dashDuration = edef ? edef->dashDuration : 40.0f;
                            float cooldownTime = edef ? edef->cooldownTime : 70.0f;
                            float distXd = player.x - enemy.x;
                            if (enemy.auxState == 0) {
                                enemy.vx = 0.0f;
                                if (std::abs(distXd) < triggerRange) { enemy.auxState = 1; enemy.customTimer = chargeTime; enemy.direction = (distXd < 0) ? 1 : 0; }
                            } else if (enemy.auxState == 1) {
                                enemy.vx = 0.0f;
                                // 溜め中はプレイヤーの回り込みに対応できるよう、突進が始まる瞬間まで方向を追従させ続ける
                                enemy.direction = (distXd < 0) ? 1 : 0;
                                enemy.customTimer -= ets;
                                if (enemy.customTimer <= 0) { enemy.auxState = 2; enemy.customTimer = dashDuration; }
                            } else if (enemy.auxState == 2) {
                                float dashSpeed = DASH_SPEED * ets * dashSpeedMult;
                                enemy.vx = (enemy.direction == 1) ? -dashSpeed : dashSpeed;
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
                            float triggerWidth = edef ? edef->triggerRange : 24.0f;
                            float fallDelay = edef ? edef->fallDelay : 10.0f;
                            float cooldownTime = edef ? edef->cooldownTime : 120.0f;
                            float shockwaveRadiusF = edef ? edef->shockwaveRadius : 60.0f;
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
                            bool aimedSideways = ((int)enemy.auxF1) != enemy.direction;
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
                                    float jitterVx = isFastForward ? sinf(enemy.y * 0.15f) * ffJitter * ets * 0.1f : 0.0f;
                                    enemy.vx = diagVx + jitterVx;
                                    if (std::abs(enemy.vy) < 0.5f) {
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
                                        if (distF <= shockwaveRadiusF && !isPlayerRewinding && player.invulnTimer <= 0.0f) {
                                            player.hp--;
                                            player.invulnTimer = 60.0f;
                                            float knockDirF = (distF > 0.01f) ? (ddxF / distF) : ((pcxF < ecxF) ? -1.0f : 1.0f);
                                            player.vx = knockDirF * 6.0f;
                                            player.vy = -4.0f;
                                            if (player.hp <= 0) currentScene = RESULT_GAMEOVER;
                                        }
                                        // 着地地点周辺の壊せるブロックも一緒に破壊する（斜め誘導で狙って割れるようにするギミック連携）
                                        for (auto& gimF : gimmicks) {
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
                                    // 元の場所（＝配置位置）へ復帰して待機状態へ。斜め誘導で着地X座標がずれていても
                                    // X/Yとも配置時の座標へ戻す（従来はYしか戻しておらず、斜め落下後にXがずれたままだった）。
                                    enemy.x = enemy.auxF3;
                                    enemy.y = enemy.auxF2;
                                    enemy.auxState = 0;
                                }
                            }
                            break;
                        }
                        case ENEMY_SPREAD_SHOOTER: {
                            // 拡散弾：固定位置から3方向へ拡散射撃
                            float shootInterval = edef ? edef->actionInterval : 150.0f;
                            float spreadAngle = edef ? edef->spreadAngle : 0.35f;
                            int spreadCount = (edef && edef->spreadCount > 0) ? edef->spreadCount : 3;
                            float projSpeed = edef ? edef->projectileSpeed : 0.5f;
                            enemy.vx = 0.0f;
                            enemy.direction = (player.x < enemy.x) ? 1 : 0;
                            enemy.customTimer += ets;
                            if (enemy.customTimer >= shootInterval) {
                                float baseDir = (enemy.direction == 0) ? 1.0f : -1.0f;
                                for (int a = 0; a < spreadCount; a++) {
                                    // spreadCount本を -spreadAngle ～ +spreadAngle の範囲に等間隔配置（spreadCount==1なら正面のみ）
                                    float angle = (spreadCount > 1)
                                        ? (-spreadAngle + (2.0f * spreadAngle) * ((float)a / (float)(spreadCount - 1)))
                                        : 0.0f;
                                    for (int i = 0; i < MAX_BULLETS; i++) {
                                        if (!bullets[i].isActive) {
                                            bullets[i].isActive = true;
                                            bullets[i].x = enemy.x + (enemy.direction == 0 ? (float)enemy.hitboxWidth * enemy.scale : -10.0f);
                                            bullets[i].y = enemy.y + (float)enemy.hitboxHeight * enemy.scale / 4.0f;
                                            float spd = BULLET_SPEED * projSpeed;
                                            bullets[i].vx = baseDir * spd * cosf(angle);
                                            bullets[i].vy = spd * sinf(angle);
                                            bullets[i].isPlayerOwned = false;
                                            bullets[i].isRewinding = false;
                                            bullets[i].history.clear();
                                            break;
                                        }
                                    }
                                }
                                enemy.customTimer = 0.0f;
                            }
                            break;
                        }
                        case ENEMY_AIMED_SHOOTER: {
                            // 照準弾：発射時のプレイヤー位置へ正確に狙い撃つ
                            float shootInterval = edef ? edef->actionInterval : 130.0f;
                            float projSpeed = edef ? edef->projectileSpeed : 0.55f;
                            enemy.vx = 0.0f;
                            enemy.customTimer += ets;
                            if (enemy.customTimer >= shootInterval) {
                                float pCenterX = player.x + (player.width * player.scale) / 2.0f;
                                float pCenterY = player.y + (player.height * player.scale) / 2.0f;
                                float eCenterX = enemy.x + (enemy.hitboxWidth * enemy.scale) / 2.0f;
                                float eCenterY = enemy.y + (enemy.hitboxHeight * enemy.scale) / 2.0f;
                                float dxA = pCenterX - eCenterX;
                                float dyA = pCenterY - eCenterY;
                                float distA = sqrtf(dxA * dxA + dyA * dyA);
                                if (distA < 1.0f) distA = 1.0f;
                                enemy.direction = (dxA < 0) ? 1 : 0;
                                for (int i = 0; i < MAX_BULLETS; i++) {
                                    if (!bullets[i].isActive) {
                                        bullets[i].isActive = true;
                                        bullets[i].x = eCenterX;
                                        bullets[i].y = eCenterY;
                                        float spd = BULLET_SPEED * projSpeed;
                                        bullets[i].vx = dxA / distA * spd;
                                        bullets[i].vy = dyA / distA * spd;
                                        bullets[i].isPlayerOwned = false;
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
                            float amplitude = edef ? edef->floatAmplitude : 40.0f;
                            float frequency = edef ? edef->floatFrequency : 0.05f;
                            if (enemy.auxF2 == 0.0f) enemy.auxF2 = enemy.y;
                            enemy.customTimer += ets;
                            float desiredY = enemy.auxF2 + sinf(enemy.customTimer * frequency) * amplitude;
                            // enemy.yを直接上書きすると天井・床とのY方向衝突判定を素通りしてしまうため、
                            // 目標Yとの差分をvyとして渡し、他の敵と同じ経路（共通の物理更新）でY衝突判定を通す
                            enemy.vy = desiredY - enemy.y;
                            float triggerRangeFl = edef ? edef->triggerRange : 300.0f;
                            if (std::abs(player.x - enemy.x) < triggerRangeFl) {
                                float speed = editorPlayerCaps.baseSpeed * ets * (edef ? edef->moveSpeed : 0.2f);
                                enemy.direction = (player.x < enemy.x) ? 1 : 0;
                                enemy.vx = (enemy.direction == 1) ? -speed : speed;
                            } else {
                                enemy.vx = 0.0f;
                            }
                            break;
                        }
                        case ENEMY_TELEPORTER: {
                            // テレポーター：一定間隔でプレイヤー付近へ瞬間移動する
                            float interval = edef ? edef->actionInterval : 180.0f;
                            float rangeMin = edef ? edef->teleportRangeMin : 120.0f;
                            float rangeMax = edef ? edef->teleportRangeMax : 220.0f;
                            float rangeSpan = std::max<float>(0.0f, rangeMax - rangeMin);
                            enemy.vx = 0.0f;
                            enemy.customTimer += ets;
                            if (enemy.customTimer >= interval) {
                                float sideX = (rand() % 2 == 0) ? 1.0f : -1.0f;
                                float offsetX = rangeMin + (float)(rand() % (int)std::max<float>(1.0f, rangeSpan));
                                // Y座標もプレイヤー基準で再抽選する（元々Xしか動かず地形にめり込む原因になっていた）
                                float sideY = (rand() % 2 == 0) ? 1.0f : -1.0f;
                                float offsetY = (float)(rand() % (int)std::max<float>(1.0f, rangeSpan * 0.5f));
                                float destX = player.x + sideX * offsetX;
                                float destY = player.y + sideY * offsetY;
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
                            float triggerRangeSk = edef ? edef->triggerRange : 300.0f;
                            float normalMult = edef ? edef->moveSpeed : 0.35f;
                            float enragedMult = edef ? edef->enragedMoveSpeed : 0.9f;
                            float mult = enemy.auxFlag ? enragedMult : normalMult;
                            float speed = editorPlayerCaps.baseSpeed * ets * mult;
                            if (std::abs(player.x - enemy.x) < triggerRangeSk) {
                                enemy.direction = (player.x < enemy.x) ? 1 : 0;
                                enemy.vx = (enemy.direction == 1) ? -speed : speed;
                            } else {
                                enemy.vx = 0.0f;
                            }
                            break;
                        }
                        case ENEMY_SHIELD: {
                            // シールド：索敵範囲内ではゆっくり接近しつつ、一定間隔で無敵状態(auxFlag)を切り替える（無敵中は描画側で発光）
                            float triggerRangeSh = edef ? edef->triggerRange : 300.0f;
                            float offDuration = edef ? edef->shieldOffDuration : 150.0f;
                            float onDuration = edef ? edef->shieldOnDuration : 90.0f;
                            float speed = editorPlayerCaps.baseSpeed * ets * (edef ? edef->moveSpeed : 0.3f);
                            if (std::abs(player.x - enemy.x) < triggerRangeSh) {
                                enemy.direction = (player.x < enemy.x) ? 1 : 0;
                                enemy.vx = (enemy.direction == 1) ? -speed : speed;
                            } else {
                                enemy.vx = 0.0f;
                            }

                            enemy.customTimer += ets;
                            if (!enemy.auxFlag && enemy.customTimer >= offDuration) { enemy.auxFlag = true; enemy.customTimer = 0.0f; }
                            else if (enemy.auxFlag && enemy.customTimer >= onDuration) { enemy.auxFlag = false; enemy.customTimer = 0.0f; }
                            break;
                        }
                        case ENEMY_MIMIC_GHOST: {
                            // 幽霊敵：プレイヤーの巻き戻し履歴を遅延再生して過去の動きをなぞる
                            enemy.vx = 0.0f; enemy.vy = 0.0f;
                            if (enemy.auxF1 <= 0.0f) enemy.auxF1 = edef ? edef->mimicDelayFrames : 90.0f;
                            size_t delay = (size_t)enemy.auxF1;
                            if (player.history.size() > delay) {
                                const PlayerState& past = player.history[player.history.size() - 1 - delay];
                                enemy.x = past.x;
                                enemy.y = past.y;
                            }
                            break;
                        }
                        case ENEMY_SIZE_SHIFTER: {
                            // 大きさが変わる敵：索敵範囲内で接近しつつ、scaleが周期的に変化（当たり判定も連動）
                            float triggerRangeSz = edef ? edef->triggerRange : 300.0f;
                            float amplitude = edef ? edef->sizeAmplitude : 0.5f;
                            float frequency = edef ? edef->sizeFrequency : 0.04f;
                            float minSc = edef ? edef->minScale : 0.4f;
                            float speed = editorPlayerCaps.baseSpeed * ets * (edef ? edef->moveSpeed : 0.25f);
                            if (std::abs(player.x - enemy.x) < triggerRangeSz) {
                                enemy.direction = (player.x < enemy.x) ? 1 : 0;
                                enemy.vx = (enemy.direction == 1) ? -speed : speed;
                            } else {
                                enemy.vx = 0.0f;
                            }

                            enemy.customTimer += ets;
                            enemy.scale = 1.0f + sinf(enemy.customTimer * frequency) * amplitude;
                            if (enemy.scale < minSc) enemy.scale = minSc;
                            break;
                        }
                        case ENEMY_TEMPO_WARPER: {
                            // 速さ操作敵：索敵範囲内で接近しつつ、speedScaleが周期的に激しく変化し接近速度が乱れる
                            float triggerRangeTw = edef ? edef->triggerRange : 300.0f;
                            float frequency = edef ? edef->tempoFrequency : 0.05f;
                            float tempoMin = edef ? edef->tempoMin : 0.3f;
                            float tempoMax = edef ? edef->tempoMax : 1.6f;
                            enemy.customTimer += ets;
                            enemy.speedScale = tempoMin + (tempoMax - tempoMin) * (0.5f + 0.5f * sinf(enemy.customTimer * frequency));
                            float speed = editorPlayerCaps.baseSpeed * ets * (edef ? edef->moveSpeed : 0.4f);
                            if (std::abs(player.x - enemy.x) < triggerRangeTw) {
                                enemy.direction = (player.x < enemy.x) ? 1 : 0;
                                enemy.vx = (enemy.direction == 1) ? -speed : speed;
                            } else {
                                enemy.vx = 0.0f;
                            }
                            break;
                        }
                        case ENEMY_BRIGHTNESS_PHANTOM: {
                            // 明るさ操作敵：索敵範囲内で接近しつつ、射程内で画面を暗転させる
                            float triggerRangeBp = edef ? edef->triggerRange : 300.0f;
                            float range = edef ? edef->effectRange : 320.0f;
                            float brightnessMin = edef ? edef->brightnessMin : 0.35f;
                            float speed = editorPlayerCaps.baseSpeed * ets * (edef ? edef->moveSpeed : 0.3f);
                            if (std::abs(player.x - enemy.x) < triggerRangeBp) {
                                enemy.direction = (player.x < enemy.x) ? 1 : 0;
                                enemy.vx = (enemy.direction == 1) ? -speed : speed;
                            } else {
                                enemy.vx = 0.0f;
                            }

                            float distB = std::abs(player.x - enemy.x);
                            if (distB < range) {
                                float t = 1.0f - (distB / range);
                                Screen_SetBrightness(1.0f - t * (1.0f - brightnessMin));
                            }
                            break;
                        }
                        case ENEMY_COLOR_SHIFTER: {
                            // 色調整敵：索敵範囲内で接近しつつ、射程内で画面を赤みがかった色調へシフトさせる
                            float triggerRangeCs = edef ? edef->triggerRange : 300.0f;
                            float range = edef ? edef->effectRange : 320.0f;
                            float tintStrength = edef ? edef->tintStrength : 0.6f;
                            float speed = editorPlayerCaps.baseSpeed * ets * (edef ? edef->moveSpeed : 0.3f);
                            if (std::abs(player.x - enemy.x) < triggerRangeCs) {
                                enemy.direction = (player.x < enemy.x) ? 1 : 0;
                                enemy.vx = (enemy.direction == 1) ? -speed : speed;
                            } else {
                                enemy.vx = 0.0f;
                            }

                            float distCol = std::abs(player.x - enemy.x);
                            if (distCol < range) {
                                float t = 1.0f - (distCol / range);
                                Screen_SetTint(1.0f, 1.0f - t * tintStrength, 1.0f - t * tintStrength);
                            }
                            break;
                        }
                        case ENEMY_ZOOM_DISRUPTOR: {
                            // ズーム撹乱敵：射程内で画面ズームを周期的に揺さぶる
                            float range = edef ? edef->effectRange : 280.0f;
                            float amplitude = edef ? edef->zoomAmplitude : 0.25f;
                            float frequency = edef ? edef->zoomFrequency : 0.08f;
                            enemy.vx = 0.0f;
                            enemy.customTimer += ets;
                            float distZ = std::abs(player.x - enemy.x);
                            if (distZ < range) {
                                Screen_SetZoom(1.0f + sinf(enemy.customTimer * frequency) * amplitude);
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

                    // X/Y方向の移動と衝突判定を共通化
                    bool enemyGrounded = UpdatePhysicsCollisions(enemy.x, enemy.y, enemy.vx, enemy.vy, enemy.vy, 
                                                                 enemy.hitboxWidth, enemy.hitboxHeight, enemy.scale, 
                                                                 stages[currentStageIdx].map, tileDefs, gimmicks);

                    // 足場との着地衝突判定
                    CheckPlatformCollision(enemy.x, enemy.y, enemy.vy, enemy.hitboxWidth, enemy.hitboxHeight, enemy.scale, platforms, gimmicks, &enemy.ridingGimmickIndex);

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
                            partActor.x = &part.x; partActor.y = &part.y;
                            partActor.scale = &part.scale; partActor.angle = &part.angle;
                            partActor.hasParent = true;
                            partActor.parentX = enemy.x; partActor.parentY = enemy.y;
                            partActor.parentDirection = (enemy.direction == 0) ? 1.0f : -1.0f; // 0=右向き, 1=左向き（enemy.directionの規約に合わせる）
                            partActor.partIndex = part.partIndex;
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
                    }
                }
                if (canEnemyAct) {
                    enemy.history.push_back({ enemy.x, enemy.y, enemy.vx, enemy.vy, enemy.direction, enemy.scale, enemy.angle, enemy.speedScale, enemy.isActive, enemy.isPaused, enemy.type, enemy.hp,
                                               enemy.customTimer, enemy.aiState, enemy.patrolLeft, enemy.patrolRight, enemy.auxF1, enemy.auxF2, enemy.auxState, enemy.auxFlag, enemy.auxF3 });
                    if (enemy.history.size() > MAX_HISTORY_FRAMES) enemy.history.erase(enemy.history.begin());
                }
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
                            float partW = (float)part.hitboxWidth * part.scale;
                            float partH = (float)part.hitboxHeight * part.scale;
                            if (CheckCollision(player.x, player.y, pw_scaled, ph_scaled,
                                                part.x + part.hitboxOffsetX, part.y + part.hitboxOffsetY, partW, partH)) {
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
                                    }
                                }
                                break;
                            }
                        }
                    }

                    // Feature: Composite Multi-Part Objects (Parts-M3)
                    if (item.isActive && !item.isCollected) {
                        const ItemDef* idefForParts = FindItemDef(item.assetId);
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
                                partActor.playerX = player.x; partActor.playerY = player.y;
                                partActor.playSound = [](const std::string& slot) { SoundManager::Get().PlaySe(slot); };
                                partActor.visualEffect = [&](const std::string& kind, float intensity) {
                                    if (kind == "brightness") Screen_SetBrightness(intensity);
                                    else if (kind == "zoom") Screen_SetZoom(intensity);
                                };
                                BehaviorInterpreter::Tick(part.scriptState, partActor);
                            }
                        }
                    }
                    item.history.push_back({ item.x, item.y, item.isCollected });
                    if (item.history.size() > MAX_HISTORY_FRAMES) item.history.erase(item.history.begin());
                }
            }
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
                    float gts = ts;
                    if (gim.type == GIMMICK_ROTATING_BRIDGE) {
                        // 橋を自動回転
                        const GimmickDef* gdef = FindGimmickDef(gim.assetId);
                        float rotSpeed = gdef ? gdef->rotationSpeed : 0.015f;
                        gim.angle += rotSpeed * gts;
                    }
                    else if (gim.type == GIMMICK_CHIKUWA_BLOCK) {
                        // ちくわブロックロジック（gim.customTimerをrideTimer、gim.val1を元のY座標、gim.val2をfallDelay、gim.angleをisFallingフラグとして使用）
                        const GimmickDef* gdef = FindGimmickDef(gim.assetId);
                        float standDelay = gdef ? gdef->standDelayFrames : 45.0f;
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
                                gim.angle = 1.0f; // isFalling = true
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
                            gim.y += gim.val2 * gts;
                            if (gim.y > SCREEN_HEIGHT + 100) {
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
                        // 敵食いギミック
                        // 対プレイヤー
                        float pw_scaled = (float)player.width * player.scale;
                        float ph_scaled = (float)player.height * player.scale;
                        if (CheckCollision(player.x, player.y, pw_scaled, ph_scaled, gim.x, gim.y, gim.spriteWidth, gim.spriteHeight)) {
                            player.hp = 0;
                            const GimmickDef* gdef = FindGimmickDef(gim.assetId);
                            if (gdef) SoundManager::Get().PlaySe(gdef->seActivate);
                        }
                        // 対敵
                        for (auto& enemy : enemies) {
                            if (enemy.isActive && CheckCollision(enemy.x, enemy.y, (float)enemy.hitboxWidth * enemy.scale, (float)enemy.hitboxHeight * enemy.scale, gim.x, gim.y, gim.spriteWidth, gim.spriteHeight)) {
                                enemy.hp = 0;
                                enemy.isActive = false;
                                gim.isActive = false; // 互いに消滅
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
                        const GimmickDef* gdef = FindGimmickDef(gim.assetId);
                        float travel = gdef ? gdef->travelDistance : 96.0f;
                        float oscSpeed = gdef ? gdef->oscillationSpeed : 0.02f;
                        if (gim.val1 == 0.0f && gim.val2 == 0.0f) { gim.val1 = gim.y - travel * 0.5f; gim.val2 = gim.y + travel * 0.5f; }
                        gim.customTimer += oscSpeed * gts;
                        float t = (sinf(gim.customTimer) + 1.0f) * 0.5f;
                        gim.y = gim.val1 + (gim.val2 - gim.val1) * t;
                    }
                    else if (gim.type == GIMMICK_FRAMESTEP_LIFT) {
                        // コマ送りリフト：一時停止中の→キー（コマ送り）1回ごとに一歩だけ動く
                        const GimmickDef* gdef = FindGimmickDef(gim.assetId);
                        float travel = gdef ? gdef->travelDistance : 128.0f;
                        float stepInc = gdef ? gdef->stepIncrement : 0.15f;
                        if (gim.val1 == 0.0f && gim.val2 == 0.0f) { gim.val1 = gim.y; gim.val2 = gim.y + travel; }
                        if (isStepFrame) {
                            gim.customTimer += stepInc;
                            if (gim.customTimer > 1.0f) gim.customTimer = 0.0f;
                        }
                        gim.y = gim.val1 + (gim.val2 - gim.val1) * gim.customTimer;
                    }
                    else if (gim.type == GIMMICK_FASTFORWARD_GATE) {
                        // 早送りゲート：早送りモード中(Fキー)だけ通過できる壁
                        gim.isActive = !isFastForward;
                    }
                    else if (gim.type == GIMMICK_BRIGHTNESS_ZONE || gim.type == GIMMICK_COLOR_ZONE
                             || gim.type == GIMMICK_ZOOM_LENS || gim.type == GIMMICK_SLOWMO_FIELD) {
                        // 画面全体エフェクト系ゾーン：範囲内にプレイヤーがいる間だけ効果を発動する
                        const GimmickDef* gdef = FindGimmickDef(gim.assetId);
                        float pw_s = (float)player.width * player.scale;
                        float ph_s = (float)player.height * player.scale;
                        if (CheckCollision(player.x, player.y, pw_s, ph_s, gim.x, gim.y, gim.spriteWidth, gim.spriteHeight)) {
                            if (gim.type == GIMMICK_BRIGHTNESS_ZONE) {
                                // val1 > 0.5 なら明転、それ以外は暗転（デフォルト）
                                float bright = gdef ? gdef->brightLevel : 1.6f;
                                float dark = gdef ? gdef->darkLevel : 0.35f;
                                Screen_SetBrightness(gim.val1 > 0.5f ? bright : dark);
                            } else if (gim.type == GIMMICK_COLOR_ZONE) {
                                float r = gdef ? gdef->tintR : 1.0f, g = gdef ? gdef->tintG : 0.6f, b = gdef ? gdef->tintB : 1.0f;
                                Screen_SetTint(r, g, b); // 紫がかった色調（既定値）
                            } else if (gim.type == GIMMICK_ZOOM_LENS) {
                                Screen_SetZoom(gdef ? gdef->zoomLevel : 1.6f);
                            } else { // GIMMICK_SLOWMO_FIELD（視覚効果のみのスローモーション演出）
                                Screen_SetZoom(gdef ? gdef->zoomLevel : 1.3f);
                                Screen_SetBrightness(gdef ? gdef->brightLevel : 0.85f);
                            }
                        }
                    }
                    else if (gim.type == GIMMICK_COLOR_LOCK_PLATFORM) {
                        // 色ロック足場：paramの色番号とプレイヤーの色フィルタが一致する時だけ実体化する
                        int requiredColor = gim.param.empty() ? 1 : atoi(gim.param.c_str());
                        gim.isActive = (requiredColor == playerColorFilter);
                    }
                    else if (gim.type == GIMMICK_BRIGHTNESS_LOCK_PLATFORM) {
                        // 明暗ロック足場：param("dark"既定/"bright")と現在の画面の明るさが一致する時だけ実体化する
                        bool wantsBright = (gim.param == "bright");
                        gim.isActive = wantsBright ? (fxCurBright > 1.3f) : (fxCurBright < 0.6f);
                    }
                    else if (gim.type == GIMMICK_CUSTOM_SCRIPT) {
                        // Feature: Puzzle-like Behavior Scripting (M2) — GimmickDef.scriptのJSONブロックで駆動する
                        ScriptActor actor;
                        actor.timeScale = gts; // 早送り/スローモーションをスクリプト駆動のギミックにも反映する
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
                            partActor.parentX = gim.x; partActor.parentY = gim.y;
                            partActor.partIndex = part.partIndex;
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
                                float partW = (float)part.hitboxWidth * part.scale;
                                float partH = (float)part.hitboxHeight * part.scale;
                                if (CheckCollision(player.x, player.y, pw_, ph_,
                                                    part.x + part.hitboxOffsetX, part.y + part.hitboxOffsetY, partW, partH)) {
                                    player.hp--;
                                    if (player.hp <= 0) currentScene = RESULT_GAMEOVER;
                                }
                            }
                        }
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
                    if (CheckCollision(player.x, player.y, pw_scaled, ph_scaled, gim.x, gim.y, gim.spriteWidth, gim.spriteHeight)) {
                        ResetStage();
                        break;
                    }
                }
            }

            // 2. プレイヤー vs 敵の衝突判定
            if (!isPlayerRewinding) {
                for (const auto& enemy : enemies) {
                    if (enemy.isActive && !isPlayerRewinding && player.invulnTimer <= 0.0f) {
                        // この敵が現在巻き戻し中の場合はスキップ
                        bool isThisEnemyRew = (isRKeyPressed && !isRotating && (selectedType == SELECT_NONE || (selectedType == SELECT_ENEMY && targetEnemy == &enemy)));
                        if (isThisEnemyRew) continue;

                        float ew_scaled = (float)enemy.hitboxWidth * enemy.scale;
                        float eh_scaled = (float)enemy.hitboxHeight * enemy.scale;
                        bool hitBody = CheckCollision(player.x, player.y, pw_scaled, ph_scaled, enemy.x, enemy.y, ew_scaled, eh_scaled);

                        // Feature: Composite Multi-Part Objects (Parts-M4) — パーツごとの接触ダメージ判定
                        bool hitPart = false;
                        for (const auto& part : enemy.parts) {
                            if (!part.isActive) continue;
                            float partW = (float)part.hitboxWidth * part.scale;
                            float partH = (float)part.hitboxHeight * part.scale;
                            if (CheckCollision(player.x, player.y, pw_scaled, ph_scaled,
                                                part.x + part.hitboxOffsetX, part.y + part.hitboxOffsetY, partW, partH)) {
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

                                float ew_scaled = (float)enemy.hitboxWidth * enemy.scale;
                                float eh_scaled = (float)enemy.hitboxHeight * enemy.scale;
                                if (CheckCollision(bullets[i].x, bullets[i].y, 16.0f, 16.0f, enemy.x, enemy.y, ew_scaled, eh_scaled)) {
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
                                        float partW = (float)part.hitboxWidth * part.scale;
                                        float partH = (float)part.hitboxHeight * part.scale;
                                        if (CheckCollision(bullets[i].x, bullets[i].y, 16.0f, 16.0f,
                                                            part.x + part.hitboxOffsetX, part.y + part.hitboxOffsetY, partW, partH)) {
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
                                    if (CheckCollision(bullets[i].x, bullets[i].y, 16.0f, 16.0f, gim.x, gim.y, gim.spriteWidth, gim.spriteHeight)) {
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
                                    float partW = (float)part.hitboxWidth * part.scale;
                                    float partH = (float)part.hitboxHeight * part.scale;
                                    if (CheckCollision(bullets[i].x, bullets[i].y, 16.0f, 16.0f,
                                                        part.x + part.hitboxOffsetX, part.y + part.hitboxOffsetY, partW, partH)) {
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
                            if (CheckCollision(bullets[i].x, bullets[i].y, 16.0f, 16.0f, player.x, player.y, pw_scaled, ph_scaled)) {
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
        // 従来はゴールが当たり判定のみで一切描画されておらず、プレイヤーから位置が見えなかった。
        // 外部画像に依存せず確実に表示されるよう、旗をプリミティブ描画で組み立てる。
        {
            const auto& curStage = stages[currentStageIdx];
            if (curStage.goalX >= 0) {
                int gx = (int)(curStage.goalX - cameraX);
                int gy = (int)(curStage.goalY - cameraY);
                // 目印の光柱（ゴールの位置を遠くからでも分かるようにする）
                SetDrawBlendMode(DX_BLENDMODE_ALPHA, 40);
                DrawBox(gx + 10, 0, gx + TILE_SIZE - 10, gy + TILE_SIZE, GetColor(255, 240, 120), TRUE);
                SetDrawBlendMode(DX_BLENDMODE_NOBLEND, 0);
                // 支柱
                DrawBox(gx + 12, gy - TILE_SIZE, gx + 17, gy + TILE_SIZE, GetColor(230, 230, 235), TRUE);
                DrawBox(gx + 12, gy - TILE_SIZE, gx + 17, gy + TILE_SIZE, GetColor(120, 120, 130), FALSE);
                // 旗（はためきをsinで表現）
                float wave = sinf(BehaviorInterpreter::globalFrameCounter * 0.12f) * 3.0f;
                int fx = gx + 17, fy = gy - TILE_SIZE + 4;
                DrawTriangle(fx, fy, fx, fy + 18, fx + 22 + (int)wave, fy + 9, GetColor(230, 60, 70), TRUE);
                // 台座
                DrawBox(gx + 4, gy + TILE_SIZE - 6, gx + TILE_SIZE - 4, gy + TILE_SIZE, GetColor(200, 180, 90), TRUE);
                DrawString(gx - 4, gy - TILE_SIZE - 18, "GOAL", GetColor(255, 255, 160));
            }
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
                // ヒビの入った石ブロック画像をスケーリングして描画（カスタムスプライト優先）
                int useHandle = gim.handle >= 0 ? gim.handle : breakableBlockHandle;
                int bx1 = (int)(gim.x - cameraX);
                int by1 = (int)(gim.y - cameraY);
                int bx2 = (int)(gim.x + gim.spriteWidth - cameraX);
                int by2 = (int)(gim.y + gim.spriteHeight - cameraY);
                DrawExtendGraph(bx1, by1, bx2, by2, useHandle, TRUE);
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

                if (isActiveState) SetDrawBright(100, 255, 100); // 起動時は緑っぽく
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
                // ゲートドア画像を描画（カスタムスプライト優先）
                int useHandle = gim.handle >= 0 ? gim.handle : doorHandle;
                int x1 = (int)(gim.x - cameraX);
                int y1 = (int)(gim.y - cameraY);
                int x2 = (int)(gim.x + gim.spriteWidth - cameraX);
                int y2 = (int)(gim.y + gim.spriteHeight - cameraY);
                DrawExtendGraph(x1, y1, x2, y2, useHandle, TRUE);
            }
            else if (gim.type == GIMMICK_CUT_PORTAL) {
                // Feature: ポータルの作り直し（友人フィードバック対応）— タイムライン比率(val1/val2)ではなく、
                // 他のギミックと同じくgim.x/gim.yに基づいて通常のスプライト描画を行う
                int useHandle = gim.handle >= 0 ? gim.handle : portalHandle;
                int x1 = (int)(gim.x - cameraX);
                int y1 = (int)(gim.y - cameraY);
                int x2 = (int)(gim.x + gim.spriteWidth - cameraX);
                int y2 = (int)(gim.y + gim.spriteHeight - cameraY);
                DrawExtendGraph(x1, y1, x2, y2, useHandle, TRUE);
            }
            else if (gim.type == GIMMICK_SPIKES) {
                // トゲトゲ画像を描画（カスタムスプライト優先）
                int useHandle = gim.handle >= 0 ? gim.handle : spikesHandle;
                int sx1 = (int)(gim.x - cameraX);
                int sy1 = (int)(gim.y - cameraY);
                int sx2 = (int)(gim.x + gim.spriteWidth - cameraX);
                int sy2 = (int)(gim.y + gim.spriteHeight - cameraY);
                DrawExtendGraph(sx1, sy1, sx2, sy2, useHandle, TRUE);
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
                // 岩（固定障害物）の描画（カスタムスプライト優先）
                int rx1 = (int)(gim.x - cameraX);
                int ry1 = (int)(gim.y - cameraY);
                int rx2 = (int)(gim.x + gim.spriteWidth - cameraX);
                int ry2 = (int)(gim.y + gim.spriteHeight - cameraY);
                if (gim.handle >= 0) {
                    DrawExtendGraph(rx1, ry1, rx2, ry2, gim.handle, TRUE);
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
                if (enemy.type == ENEMY_SHIELD && enemy.auxFlag) SetDrawBright(255, 230, 100); // 無敵中は金色に発光
                else SetDrawBright(255, 120, 120);
                if (enemy.anim.HasClip(enemy.anim.currentClip)) {
                    // animations.jsonにこの敵のクリップが定義されていればスプライトシートアニメーションで描画
                    int animH = enemy.anim.GetCurrentFrameHeight();
                    int animCy = (int)(enemy.y - cameraY + (animH * enemy.scale) / 2.0f);
                    enemy.anim.DrawAt(ecx, animCy, enemy.scale, enemy.angle, enemy.direction != 0);
                } else if (enemy.direction == 0) {
                    DrawRotaGraph(ecx, ecy, enemy.scale, enemy.angle, enemy.handle, TRUE);
                } else {
                    DrawRotaGraph(ecx, ecy, enemy.scale, enemy.angle, enemy.handle, TRUE, TRUE);
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
            player.anim.DrawAt(cx, drawCy, player.scale, player.angle, player.direction == 1);
        } else {
            GetGraphSize(player.handle, &pImgW, &pImgH);
            int drawCy = (int)(cy - (pImgH * player.scale) / 2.0f);
            if (player.direction == 0) DrawRotaGraph(cx, drawCy, player.scale, player.angle, player.handle, TRUE);
            else DrawRotaGraph(cx, drawCy, player.scale, player.angle, player.handle, TRUE, TRUE);
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
            if (bullets[i].isActive) DrawGraph((int)(bullets[i].x - cameraX), (int)(bullets[i].y - cameraY), bullets[i].handle, TRUE);
        }

        // OSD：コインカウント（正しくプレビューされるようにgameScreen内に描画）
        int collectedCoins = 0;
        for (const auto& item : items) { if (item.isCollected) collectedCoins++; }
        char coinStr[32];
        sprintf_s(coinStr, sizeof(coinStr), "COINS: %d / %d", collectedCoins, (int)items.size());
        
        // OSDボックスの描画
        DrawBox(10, 10, 150, 40, GetColor(30, 30, 30), TRUE);
        DrawBox(10, 10, 150, 40, GetColor(255, 215, 0), FALSE);
        DrawString(20, 18, coinStr, GetColor(255, 215, 0));

        // OSD：編集コストゲージ（コインカウンターの直下）
        {
            float ratio = editCost / currentEditCost.maxCost;
            if (ratio < 0.0f) ratio = 0.0f; if (ratio > 1.0f) ratio = 1.0f;
            bool isLow = ratio < 0.2f;
            bool blinkOn = ((long)(BehaviorInterpreter::globalFrameCounter) / 15) % 2 == 0;
            int barColor = isLow ? (blinkOn ? GetColor(255, 80, 80) : GetColor(90, 30, 30)) : GetColor(0, 200, 255);

            DrawBox(10, 45, 150, 65, GetColor(30, 30, 30), TRUE);
            DrawBox(10, 45, (int)(10 + 140 * ratio), 65, barColor, TRUE);
            DrawBox(10, 45, 150, 65, GetColor(255, 215, 0), FALSE);
            char editCostStr[32];
            sprintf_s(editCostStr, sizeof(editCostStr), "EDIT: %d / %d", (int)editCost, (int)currentEditCost.maxCost);
            DrawString(16, 50, editCostStr, GetColor(255, 255, 255));
        }

        // 編集ツールの状態インジケータ（色フィルタ・ミュート）
        if (playerColorFilter != 0) {
            const char* filterName = playerColorFilter == 1 ? "RED" : playerColorFilter == 2 ? "GREEN" : "BLUE";
            int filterColor = playerColorFilter == 1 ? GetColor(255, 90, 90) : playerColorFilter == 2 ? GetColor(90, 220, 90) : GetColor(90, 150, 255);
            DrawFormatString(160, 18, filterColor, "FILTER: %s", filterName);
        }
        if (SoundManager::Get().IsMuted()) {
            DrawString(160, 34, "MUTED", GetColor(150, 150, 150));
        }

        // ヘルプガイドのOSDオーバーレイ
        if (isPaused) {
            DrawString(10, SCREEN_HEIGHT - 25, "PAUSED (EDITING): Drag, [S]+Drag Vert. to Scale, [R]+Drag Horiz. to Rotate, [MiddleClick] to Resume", GetColor(200, 200, 200));
        } else {
            DrawString(10, SCREEN_HEIGHT - 38, "PLAYING: [A][D]: Move  [SPACE]: Jump  [ENTER]/Click Screen: Shot  [MiddleClick] to Pause", GetColor(50, 255, 50));
            DrawString(10, SCREEN_HEIGHT - 22, "EDIT TOOLS: [T]:Color Filter  [Z]:Zoom  [X]:Darken  [C]:Brighten  [M]:Mute", GetColor(120, 220, 255));
        }

        // Feature 5: ShowMessageアクションによるメッセージウィンドウ
        if (isShowingMessage) {
            int boxX = 20, boxY = SCREEN_HEIGHT - 110, boxW = SCREEN_WIDTH - 40, boxH = 90;
            SetDrawBlendMode(DX_BLENDMODE_ALPHA, 200);
            DrawBox(boxX, boxY, boxX + boxW, boxY + boxH, GetColor(0, 0, 0), TRUE);
            SetDrawBlendMode(DX_BLENDMODE_NOBLEND, 0);
            DrawBox(boxX, boxY, boxX + boxW, boxY + boxH, GetColor(255, 255, 255), FALSE);
            if (!currentMessageSpeaker.empty()) {
                DrawString(boxX + 12, boxY + 8, currentMessageSpeaker.c_str(), GetColor(255, 220, 120));
            }
            DrawString(boxX + 12, boxY + (currentMessageSpeaker.empty() ? 12 : 32), currentMessageText.c_str(), GetColor(255, 255, 255));
            DrawString(boxX + boxW - 110, boxY + boxH - 22, "[ENTER] to close", GetColor(180, 180, 180));
        }

        // リザルト画面
        if (currentScene != PLAY) {
            SetDrawBlendMode(DX_BLENDMODE_ALPHA, 180);
            DrawBox(0, 0, SCREEN_WIDTH, SCREEN_HEIGHT, GetColor(0, 0, 0), TRUE);
            SetDrawBlendMode(DX_BLENDMODE_NOBLEND, 0);

            int btnW = 160, btnH = 50;
            int btnX = SCREEN_WIDTH / 2 - btnW / 2;
            int btnY = SCREEN_HEIGHT / 2 + 30;

            int relX = isEditMode ? (mx - monitorX) : mx;
            int relY = isEditMode ? (my - monitorY) : my;
            bool isHover = (relX >= btnX && relX <= btnX + btnW && relY >= btnY && relY <= btnY + btnH);

            if (currentLeftClick && !prevLeftClick && isHover) {
                ResetStage();
            }

            SetFontSize(48);
            if (currentScene == RESULT_GAMEOVER) {
                DrawString(SCREEN_WIDTH / 2 - 120, SCREEN_HEIGHT / 2 - 50, "GAME OVER", GetColor(255, 50, 50));
            }
            else {
                DrawString(SCREEN_WIDTH / 2 - 100, SCREEN_HEIGHT / 2 - 50, "VICTORY!", GetColor(255, 255, 50));
            }
            SetFontSize(16);

            DrawBox(btnX, btnY, btnX + btnW, btnY + btnH, isHover ? GetColor(150, 150, 150) : GetColor(80, 80, 80), TRUE);
            DrawBox(btnX, btnY, btnX + btnW, btnY + btnH, GetColor(255, 255, 255), FALSE);
            DrawString(btnX + 55, btnY + 17, "RETRY", GetColor(255, 255, 255));
        }

        // --- 最終ワークスペースレイアウト出力 ---
        SetDrawScreen(DX_SCREEN_BACK);
        ClearDrawScreen();
        if (isEditMode) {
            DrawBox(0, 0, WINDOW_WIDTH, WINDOW_HEIGHT, GetColor(30, 30, 30), TRUE); // ワークスペース背景
            DrawBox(0, 0, 250, WINDOW_HEIGHT - 100, GetColor(45, 45, 45), TRUE); // 左パネル
            
            // パネルタイトル
            DrawString(20, 20, "STAGE EDITOR", GetColor(200, 200, 200));

            DrawBox(WINDOW_WIDTH - 250, 0, WINDOW_WIDTH, WINDOW_HEIGHT - 100, GetColor(45, 45, 45), TRUE); // 右パネル（インスペクター）
            DrawBox(0, WINDOW_HEIGHT - 100, WINDOW_WIDTH, WINDOW_HEIGHT, GetColor(40, 40, 40), TRUE); // 下部パネル（タイムライン）

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

            // Feature: ポータルの作り直し（友人フィードバック対応）— タイムライントラック/再生ヘッド/カット表示は
            // CUT_PORTALの専用UIだったため廃止（ポータルは通常のギミックとして画面上に直接描画・選択される）

            // 動的インスペクターパネルの描画
            DrawString(WINDOW_WIDTH - 240, 20, "[INSPECTOR]", GetColor(200, 200, 200));
            if (selectedType == SELECT_NONE) {
                DrawString(WINDOW_WIDTH - 240, 50, "No Object Selected", GetColor(150, 150, 150));
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
                        else if (targetGimmick->type == GIMMICK_CUT_PORTAL) sprintf_s(objName, sizeof(objName), "Selected: PORTAL");
                    }
                }
                DrawString(WINDOW_WIDTH - 240, 50, objName, GetColor(0, 255, 255));
                
                if (targetScale != nullptr) {
                    if (selectedType == SELECT_GIMMICK) {
                        DrawFormatString(WINDOW_WIDTH - 240, 80, isInspScale ? GetColor(255, 255, 0) : GetColor(255, 255, 255), "Width: %.0f", *targetScale);
                    } else {
                        DrawFormatString(WINDOW_WIDTH - 240, 80, isInspScale ? GetColor(255, 255, 0) : GetColor(255, 255, 255), "Scale: %.2f", *targetScale);
                    }
                } else {
                    DrawFormatString(WINDOW_WIDTH - 240, 80, GetColor(150, 150, 150), "Scale: N/A");
                }

                if (targetAngle != nullptr) {
                    DrawFormatString(WINDOW_WIDTH - 240, 100, isInspAngle ? GetColor(255, 255, 0) : GetColor(255, 255, 255), "Angle: %.2f", *targetAngle);
                } else {
                    DrawFormatString(WINDOW_WIDTH - 240, 100, GetColor(150, 150, 150), "Angle: N/A");
                }
                
                if (selectedType == SELECT_GIMMICK) {
                    DrawFormatString(WINDOW_WIDTH - 240, 120, GetColor(150, 150, 150), "Speed: N/A");
                } else {
                    if (targetSpeedScale != nullptr) {
                        DrawFormatString(WINDOW_WIDTH - 240, 120, isInspSpeed ? GetColor(255, 255, 0) : GetColor(255, 255, 255), "Speed: %.1f", *targetSpeedScale);
                    } else {
                        DrawFormatString(WINDOW_WIDTH - 240, 120, GetColor(150, 150, 150), "Speed: N/A");
                    }
                }

                if (targetPaused != nullptr) {
                    DrawFormatString(WINDOW_WIDTH - 240, 140, *targetPaused ? GetColor(255, 255, 100) : GetColor(200, 200, 200), "Pause: %s", *targetPaused ? "TRUE (||)" : "FALSE");
                } else {
                    DrawFormatString(WINDOW_WIDTH - 240, 140, GetColor(150, 150, 150), "Pause: N/A");
                }

                if (targetRewind != nullptr) {
                    DrawFormatString(WINDOW_WIDTH - 240, 160, *targetRewind ? GetColor(255, 80, 80) : GetColor(200, 200, 200), "Rewind: %s", *targetRewind ? "TRUE (<<)" : "FALSE");
                } else {
                    DrawFormatString(WINDOW_WIDTH - 240, 160, GetColor(150, 150, 150), "Rewind: N/A");
                }
                
                if (selectedType == SELECT_ENEMY && targetEnemyType != nullptr) {
                    const char* typeName = "UNKNOWN";
                    if (*targetEnemyType == ENEMY_PATROL) typeName = "PATROL";
                    else if (*targetEnemyType == ENEMY_JUMPER) typeName = "JUMPER";
                    else if (*targetEnemyType == ENEMY_STATIONARY) typeName = "STATIONARY";
                    DrawFormatString(WINDOW_WIDTH - 240, 180, GetColor(0, 255, 0), "Type: %s", typeName);
                }

                DrawString(WINDOW_WIDTH - 240, 210, "(Drag values / click to toggle)", GetColor(150, 150, 150));
            }

            // 下部一時停止ボタン
            DrawBox(WINDOW_WIDTH / 2 - 50, WINDOW_HEIGHT - 90, WINDOW_WIDTH / 2 + 50, WINDOW_HEIGHT - 70, GetColor(80, 80, 80), TRUE);
            DrawString(WINDOW_WIDTH / 2 - 25, WINDOW_HEIGHT - 85, isPaused ? "RESUME" : "PAUSE", GetColor(255, 255, 255));

            // コンテキストメニューポップアップの描画
            if (menu.isOpen) {
                DrawBox(menu.x, menu.y, menu.x + menu.width, menu.y + menu.height, GetColor(50, 50, 50), TRUE);
                DrawBox(menu.x, menu.y, menu.x + menu.width, menu.y + menu.height, GetColor(180, 180, 180), FALSE);

                {
                    char rewindStr[32];
                    sprintf_s(rewindStr, sizeof(rewindStr), "Rewind: %s", (targetRewind && *targetRewind) ? "ON" : "OFF");
                    char pausedStr[32];
                    sprintf_s(pausedStr, sizeof(pausedStr), "Pause: %s", (targetPaused && *targetPaused) ? "ON" : "OFF");

                    DrawString(menu.x + 10, menu.y + 10, rewindStr, GetColor(255, 255, 255));
                    DrawString(menu.x + 10, menu.y + 35, pausedStr, GetColor(255, 255, 255));

                    if (selectedType == SELECT_GIMMICK) {
                        DrawString(menu.x + 10, menu.y + 60, "Speed: N/A", GetColor(120, 120, 120));
                        DrawString(menu.x + 10, menu.y + 85, "Speed: N/A", GetColor(120, 120, 120));
                        DrawString(menu.x + 10, menu.y + 110, "Flip: N/A", GetColor(120, 120, 120));
                    } else {
                        DrawString(menu.x + 10, menu.y + 60, "Speed +0.5", GetColor(255, 255, 255));
                        DrawString(menu.x + 10, menu.y + 85, "Speed -0.5", GetColor(255, 255, 255));
                        DrawString(menu.x + 10, menu.y + 110, "Flip Object", GetColor(255, 255, 255));
                    }
                    DrawString(menu.x + 10, menu.y + 135, "Reset All", GetColor(255, 255, 255));
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

            if (isPaused) {
                DrawString(10, 10, "PROFESSIONAL EDIT MODE (PAUSED)", GetColor(255, 255, 0));
            } else {
                DrawString(10, 10, "PROFESSIONAL PLAY MODE (RUNNING)", GetColor(50, 255, 50));
            }
        } else {
            Screen_DrawComposited(0, 0, WINDOW_WIDTH, WINDOW_HEIGHT, gameScreen);
            if (isPaused) DrawString(WINDOW_WIDTH / 2 - 40, WINDOW_HEIGHT / 2, "PAUSED", GetColor(255, 255, 255));
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






























