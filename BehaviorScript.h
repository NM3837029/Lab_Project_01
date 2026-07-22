#pragma once
#include "json.hpp"
#include "Logger.h"
#include <vector>
#include <unordered_map>
#include <string>
#include <functional>
#include <cmath>
#include <cstdlib>
using json = nlohmann::json;

// ======================================================
// BehaviorScript - Scratch風ブロックスクリプトの実行エンジン
// Feature: Puzzle-like Behavior Scripting (M2)
//
// スクリプトはJSON AST（jsonそのもの）をツリーウォーク型インタプリタで直接実行する。
// バイトコードへのコンパイルは行わない（C#側のブロックキャンバスが保存するJSONを
// そのまま実行できるようにするため、また単一ヘッダで完結させ再コンパイル不要にするため）。
//
// 実行状態(ScriptState)はフレームをまたいで再開可能な明示的コールスタックを持つ。
// Wait系ブロックに達すると即座にTick()を抜け、次フレーム以降で続きから再開する。
// Forever/Repeatのボディが1周してpc=0に戻る瞬間は、Waitが無くても必ずそのティックを
// 打ち切る（安全装置）。これにより、ユーザーが作った無限ループが1フレームを専有して
// ゲーム全体をフリーズさせることはない。
// ======================================================

// スクリプトが読み書きする対象（敵/ギミック等）の共通ビュー。
// インタプリタ本体はEnemy/Gimmick等の実体の型を一切知らず、このフラットな構造体だけを介して
// 状態を読み書きする。呼び出し側（DrawPixel.cpp）が毎フレーム、対象の実体からこれを組み立てる。
struct ScriptActor {
    float* x = nullptr;
    float* y = nullptr;
    float* vx = nullptr;
    float* vy = nullptr;
    int* direction = nullptr;   // 0=右向き, 1=左向き（既存のenemy.direction/gim系と同じ規約）
    float* scale = nullptr;     // 存在しない対象はnullptrのままでよい（安全にスキップされる）
    bool* invincible = nullptr; // 同上
    float* angle = nullptr;     // 見た目の回転角（Parts-M2で新設。ドアのパネルや砲塔の向きなど）

    // センシング用に呼び出し側が毎フレーム計算して渡すスナップショット
    float playerX = 0.0f, playerY = 0.0f;
    bool isGrounded = false;
    bool wallAheadLeft = false, wallAheadRight = false;
    bool groundAheadLeft = false, groundAheadRight = false;

    // Feature: Composite Multi-Part Objects (Parts-M2) — パーツ(部品)としての実行時のみ意味を持つ
    float parentX = 0.0f, parentY = 0.0f; // 親(複合体本体)の現在のワールド座標
    bool hasParent = false;               // このアクターがパーツかどうか
    int partIndex = 0;                    // 親のparts[]内インデックス（同一スクリプトを複数パーツで共有し、
                                           // パーツごとに異なる位相をつけるためのPartIndexレポーターに使う）

    // 環境依存のグローバル呼び出し（弾生成・SE再生・画面演出）はコールバックとして注入する
    std::function<void(float angleRad, float speed, float damage)> shoot;
    std::function<void(const std::string& slot)> playSound;
    std::function<void(const std::string& kind, float intensity)> visualEffect;
};

// コールスタックの1フレーム分。通常のブロック列・Forever・Repeat(N)・RepeatUntil(cond)を
// この1つの構造体で表現する（isForever/repeatRemaining/isUntilLoopで種別を区別）。
struct ScriptFrame {
    const json* body = nullptr;
    size_t pc = 0;
    bool isForever = false;
    int repeatRemaining = -1;     // >=0 の場合Repeat(N)。1周ごとに消費し、0になったら終了する
    bool isUntilLoop = false;
    const json* untilCond = nullptr;

    static ScriptFrame Plain(const json* b) { ScriptFrame f; f.body = b; return f; }
    static ScriptFrame ForeverLoop(const json* b) { ScriptFrame f; f.body = b; f.isForever = true; return f; }
    static ScriptFrame CountLoop(const json* b, int n) { ScriptFrame f; f.body = b; f.repeatRemaining = n; return f; }
    static ScriptFrame UntilLoop(const json* b, const json* cond) { ScriptFrame f; f.body = b; f.isUntilLoop = true; f.untilCond = cond; return f; }
};

struct ScriptState {
    // Start()時にprogram（EnemyDef/GimmickDef.script）をディープコピーして所有する。
    // ScriptFrame.bodyはこのownedProgramの内部を指すため、呼び出し元のenemyDefs/gimmickDefsが
    // 後で再確保・変更されても（例: 実行中にSpawnEnemyで敵ベクタが再確保される等）ダングリングポインタに
    // ならず安全に動作する。ScriptState自体はEnemy/Gimmickのメンバとして値で保持されるため、
    // 所有権はそのままEnemy/Gimmickインスタンスのライフタイムに一致する。
    json ownedProgram = json::array();
    std::vector<ScriptFrame> callStack;
    int waitFramesRemaining = 0;
    std::unordered_map<std::string, float> vars;
    bool started = false;
    bool finished = false;
    bool faulted = false;
    std::string faultMsg;
};

class BehaviorInterpreter {
public:
    // 安全装置（フリーズ防止）のしきい値
    static constexpr int kMaxOpsPerTick = 500;      // 1体・1フレームあたりの命令実行数上限
    static constexpr int kMaxGlobalOpsPerFrame = 20000; // 全スクリプト実行体・1フレームあたりの合計上限
    static constexpr int kMaxCallDepth = 32;        // コールスタックのネスト深度上限

    // 全スクリプト実行体で共有する、1フレームあたりの命令実行カウンタ。
    // メインループの先頭で毎フレーム0にリセットすること。
    static inline int globalOpsThisFrame = 0;

    // Feature: Composite Multi-Part Objects (Parts-M2) — Timeレポーター用のグローバル経過フレーム数。
    // リセットせず、毎フレーム加算し続けるだけ（Waitのフレームカウントとは無関係の「時計」）。
    static inline float globalFrameCounter = 0.0f;

    // program（enemies.json等の"script"フィールド）からhatNameのハット（"OnSpawn"等）を探し、
    // 見つかればその本体から実行を開始する。見つからなければ何もしない（finished=trueのまま）。
    static void Start(ScriptState& state, const json& program, const std::string& hatName) {
        state = ScriptState();
        state.ownedProgram = program; // ディープコピーして所有権を持つ（呼び出し元の寿命に依存しないようにする）
        const json* body = FindHatBody(state.ownedProgram, hatName);
        if (!body) { state.finished = true; return; }
        state.callStack.push_back(ScriptFrame::Plain(body));
        state.started = true;
    }

    // Feature: Composite Multi-Part Objects (Parts-M6) — OnDamaged/OnDeath等の反応イベントを発火する。
    // 呼び出し側が持つ専用のreactiveState（scriptStateとは別のScriptState）に対して使うことを想定している。
    // Start()は毎回stateを完全にリセットするため、OnSpawn用のscriptState(Foreverループ等)を巻き込まないよう、
    // 必ずreactiveState専用のScriptStateへ対して呼ぶこと。v1では、そのティック内で完結する処理
    // （PlaySound/SetVisualEffect/Shoot等）のみを想定し、Waitを跨ぐ複数フレームの演出はサポートしない。
    static void FireReactiveHat(ScriptState& reactiveState, const json& program, const std::string& hatName, ScriptActor& actor) {
        Start(reactiveState, program, hatName);
        Tick(reactiveState, actor);
    }

    // 1フレーム分だけ実行を進める。Wait系に達するか、安全装置の上限に達するとその場で戻る。
    static void Tick(ScriptState& state, ScriptActor& actor) {
        if (state.faulted || state.finished || !state.started) return;
        if (state.waitFramesRemaining > 0) { state.waitFramesRemaining--; return; }

        int opsThisTick = 0;
        while (!state.callStack.empty()) {
            if (++globalOpsThisFrame > kMaxGlobalOpsPerFrame) return; // 他の個体がフレーム予算を使い切った
            if (++opsThisTick > kMaxOpsPerTick) return;               // このフレームはここまで、次フレームへ持ち越し
            if ((int)state.callStack.size() > kMaxCallDepth) {
                Fault(state, "call stack too deep (possible malformed script)");
                return;
            }

            ScriptFrame& frame = state.callStack.back();

            if (frame.pc >= frame.body->size()) {
                if (frame.isForever) {
                    frame.pc = 0;
                    return; // Foreverが1周した瞬間は必ずこのティックを終える（安全装置）
                }
                if (frame.repeatRemaining >= 0) {
                    frame.repeatRemaining--;
                    if (frame.repeatRemaining > 0) { frame.pc = 0; return; } // Repeatが1周した瞬間も同様
                    state.callStack.pop_back();
                    if (state.callStack.empty()) { state.finished = true; return; }
                    continue;
                }
                if (frame.isUntilLoop) {
                    bool done = frame.untilCond ? EvalBool(*frame.untilCond, actor, state) : true;
                    if (!done) { frame.pc = 0; return; }
                    state.callStack.pop_back();
                    if (state.callStack.empty()) { state.finished = true; return; }
                    continue;
                }
                // 通常のブロック列（If/IfElse/OnSpawn直下など）が最後まで実行された
                state.callStack.pop_back();
                if (state.callStack.empty()) { state.finished = true; return; }
                continue;
            }

            if (!frame.body->is_array()) {
                // 手編集された不正なJSON等、想定外の状態に対する安全網。ここに来ることは通常無いはず。
                Fault(state, "frame.body is not an array (type=" + std::string(frame.body->type_name()) + ")");
                return;
            }
            const json& block = (*frame.body)[frame.pc];
            frame.pc++; // 先に進めてから実行する（Wait/WaitUntilで正しく再開できるようにするため）
            if (!block.is_object()) continue;
            if (ExecuteBlock(block, state, actor)) return; // Wait系はここでティックを終える
        }
        state.finished = true;
    }

private:
    static void Fault(ScriptState& state, const std::string& msg) {
        state.faulted = true;
        state.faultMsg = msg;
        Logger::Error("BehaviorScript", "Tick", msg);
    }

    static const json* FindHatBody(const json& program, const std::string& hatName) {
        if (!program.is_array()) return nullptr;
        for (const auto& script : program) {
            if (script.is_object() && script.value("hat", "") == hatName
                && script.contains("body") && script["body"].is_array())
                return &script["body"];
        }
        return nullptr;
    }

    // ── 数値/真偽値の評価（引数は数値リテラル or ネストしたレポーターブロック） ──

    static float GetNumberArg(const json& block, const std::string& key, float defaultVal, ScriptActor& actor, ScriptState& state) {
        if (!block.contains(key)) return defaultVal;
        const json& v = block[key];
        if (v.is_number()) return v.get<float>();
        if (v.is_object()) return EvalNumber(v, actor, state);
        return defaultVal;
    }

    static float EvalNumber(const json& expr, ScriptActor& actor, ScriptState& state) {
        if (expr.is_number()) return expr.get<float>();
        std::string op = expr.value("op", "");
        if (op == "Const") return expr.value("value", 0.0f);
        if (op == "SelfX") return actor.x ? *actor.x : 0.0f;
        if (op == "SelfY") return actor.y ? *actor.y : 0.0f;
        if (op == "PlayerX") return actor.playerX;
        if (op == "PlayerY") return actor.playerY;
        if (op == "DistanceToPlayer") {
            float dx = actor.playerX - (actor.x ? *actor.x : 0.0f);
            float dy = actor.playerY - (actor.y ? *actor.y : 0.0f);
            return sqrtf(dx * dx + dy * dy);
        }
        if (op == "DirectionToPlayer") {
            float dx = actor.playerX - (actor.x ? *actor.x : 0.0f);
            float dy = actor.playerY - (actor.y ? *actor.y : 0.0f);
            return atan2f(dy, dx);
        }
        if (op == "Random") {
            float mn = GetNumberArg(expr, "min", 0.0f, actor, state);
            float mx = GetNumberArg(expr, "max", 1.0f, actor, state);
            if (mx <= mn) return mn;
            return mn + ((float)rand() / (float)RAND_MAX) * (mx - mn);
        }
        if (op == "GetVar") {
            auto it = state.vars.find(expr.value("name", ""));
            return it != state.vars.end() ? it->second : 0.0f;
        }
        if (op == "Add") return GetNumberArg(expr, "a", 0.0f, actor, state) + GetNumberArg(expr, "b", 0.0f, actor, state);
        if (op == "Sub") return GetNumberArg(expr, "a", 0.0f, actor, state) - GetNumberArg(expr, "b", 0.0f, actor, state);
        if (op == "Mul") return GetNumberArg(expr, "a", 0.0f, actor, state) * GetNumberArg(expr, "b", 0.0f, actor, state);
        if (op == "Div") {
            float b = GetNumberArg(expr, "b", 1.0f, actor, state);
            return b != 0.0f ? GetNumberArg(expr, "a", 0.0f, actor, state) / b : 0.0f;
        }
        // Feature: Composite Multi-Part Objects (Parts-M2) — 汎用の三角関数・時計・親座標・パーツ位相
        if (op == "Sin") return sinf(GetNumberArg(expr, "a", 0.0f, actor, state));
        if (op == "Cos") return cosf(GetNumberArg(expr, "a", 0.0f, actor, state));
        if (op == "Time") return BehaviorInterpreter::globalFrameCounter;
        if (op == "ParentX") return actor.parentX;
        if (op == "ParentY") return actor.parentY;
        if (op == "PartIndex") return (float)actor.partIndex;
        return 0.0f;
    }

    static bool EvalBool(const json& expr, ScriptActor& actor, ScriptState& state) {
        if (expr.is_boolean()) return expr.get<bool>();
        std::string op = expr.value("op", "");
        if (op == "Gt") return GetNumberArg(expr, "a", 0.0f, actor, state) > GetNumberArg(expr, "b", 0.0f, actor, state);
        if (op == "Lt") return GetNumberArg(expr, "a", 0.0f, actor, state) < GetNumberArg(expr, "b", 0.0f, actor, state);
        if (op == "Eq") return GetNumberArg(expr, "a", 0.0f, actor, state) == GetNumberArg(expr, "b", 0.0f, actor, state);
        if (op == "And") return expr.contains("a") && expr.contains("b") && EvalBool(expr["a"], actor, state) && EvalBool(expr["b"], actor, state);
        if (op == "Or") return (expr.contains("a") && EvalBool(expr["a"], actor, state)) || (expr.contains("b") && EvalBool(expr["b"], actor, state));
        if (op == "Not") return expr.contains("a") && !EvalBool(expr["a"], actor, state);
        if (op == "IsGrounded") return actor.isGrounded;
        if (op == "IsWallAhead") return (actor.direction && *actor.direction == 1) ? actor.wallAheadLeft : actor.wallAheadRight;
        if (op == "IsGroundAhead") return (actor.direction && *actor.direction == 1) ? actor.groundAheadLeft : actor.groundAheadRight;
        return false;
    }

    // ── ブロック実行。trueを返すとそのティックはここで終了する（Wait系） ──

    static bool ExecuteBlock(const json& block, ScriptState& state, ScriptActor& actor) {
        std::string op = block.value("op", "");

        // --- Control ---
        if (op == "Forever") {
            if (block.contains("body") && block["body"].is_array())
                state.callStack.push_back(ScriptFrame::ForeverLoop(&block["body"]));
            return false;
        }
        if (op == "Repeat") {
            int n = (int)GetNumberArg(block, "count", 1.0f, actor, state);
            if (n > 0 && block.contains("body") && block["body"].is_array())
                state.callStack.push_back(ScriptFrame::CountLoop(&block["body"], n));
            return false;
        }
        if (op == "RepeatUntil") {
            if (block.contains("body") && block["body"].is_array())
                state.callStack.push_back(ScriptFrame::UntilLoop(&block["body"], block.contains("cond") ? &block["cond"] : nullptr));
            return false;
        }
        if (op == "If") {
            bool cond = block.contains("cond") && EvalBool(block["cond"], actor, state);
            if (cond && block.contains("body") && block["body"].is_array())
                state.callStack.push_back(ScriptFrame::Plain(&block["body"]));
            return false;
        }
        if (op == "IfElse") {
            bool cond = block.contains("cond") && EvalBool(block["cond"], actor, state);
            const char* key = cond ? "body" : "else";
            if (block.contains(key) && block[key].is_array())
                state.callStack.push_back(ScriptFrame::Plain(&block[key]));
            return false;
        }
        if (op == "Wait") {
            state.waitFramesRemaining = (std::max)(0, (int)GetNumberArg(block, "frames", 0.0f, actor, state));
            return true;
        }
        if (op == "WaitUntil") {
            bool met = block.contains("cond") && EvalBool(block["cond"], actor, state);
            if (!met) { state.callStack.back().pc--; return true; } // 同じブロックを次フレームも再評価する
            return false;
        }

        // --- Motion ---
        if (op == "MoveDirection") {
            std::string dir = block.value("dir", "Toward");
            float speed = GetNumberArg(block, "speed", 0.0f, actor, state);
            if (!actor.vx || !actor.x) return false;
            float vx = 0.0f;
            if (dir == "Left") { vx = -speed; if (actor.direction) *actor.direction = 1; }
            else if (dir == "Right") { vx = speed; if (actor.direction) *actor.direction = 0; }
            else {
                bool towardLeft = actor.playerX < *actor.x;
                if (dir == "Away") towardLeft = !towardLeft;
                vx = towardLeft ? -speed : speed;
                if (actor.direction) *actor.direction = towardLeft ? 1 : 0;
            }
            *actor.vx = vx;
            return false;
        }
        if (op == "ApplyImpulse") {
            if (actor.vx) *actor.vx = GetNumberArg(block, "vx", *actor.vx, actor, state);
            if (actor.vy) *actor.vy = GetNumberArg(block, "vy", *actor.vy, actor, state);
            return false;
        }
        if (op == "SetPosition") {
            if (actor.x) *actor.x = GetNumberArg(block, "x", *actor.x, actor, state);
            if (actor.y) *actor.y = GetNumberArg(block, "y", *actor.y, actor, state);
            return false;
        }
        if (op == "OffsetPosition") {
            if (actor.x) *actor.x += GetNumberArg(block, "dx", 0.0f, actor, state);
            if (actor.y) *actor.y += GetNumberArg(block, "dy", 0.0f, actor, state);
            return false;
        }
        if (op == "SetLocalOffset") {
            // Feature: Composite Multi-Part Objects (Parts-M2) — 親(複合体本体)の現在座標からの相対位置を設定する。
            // パーツ以外(hasParent==false)では意味を持たないため何もしない。
            if (actor.hasParent && actor.x && actor.y) {
                *actor.x = actor.parentX + GetNumberArg(block, "dx", 0.0f, actor, state);
                *actor.y = actor.parentY + GetNumberArg(block, "dy", 0.0f, actor, state);
            }
            return false;
        }
        if (op == "SetLocalOffsetPolar") {
            // Feature: Composite Multi-Part Objects (Parts-M2追加) — 親を中心に「角度・半径」で相対位置を設定する。
            // ファイアバーのように「同じ角度・異なる半径」のパーツを並べて回転する棒状の配置を、
            // Cos/Sinを2回書かずに1命令で表現できるようにする糖衣構文。
            if (actor.hasParent && actor.x && actor.y) {
                float ang = GetNumberArg(block, "angle", 0.0f, actor, state);
                float rad = GetNumberArg(block, "radius", 0.0f, actor, state);
                *actor.x = actor.parentX + cosf(ang) * rad;
                *actor.y = actor.parentY + sinf(ang) * rad;
            }
            return false;
        }
        if (op == "SetAngle") {
            if (actor.angle) *actor.angle = GetNumberArg(block, "angle", *actor.angle, actor, state);
            return false;
        }
        if (op == "FaceTowards") {
            if (actor.direction && actor.x) *actor.direction = (actor.playerX < *actor.x) ? 1 : 0;
            return false;
        }
        if (op == "Oscillate") {
            // 動く足場などを想定し、Y座標をmin～maxの間で周期的に振動させる糖衣構文。
            // 位相はこのスクリプト専用の変数(__oscPhase)に保持する。
            float mn = GetNumberArg(block, "min", 0.0f, actor, state);
            float mx = GetNumberArg(block, "max", 1.0f, actor, state);
            float periodFrames = GetNumberArg(block, "periodFrames", 60.0f, actor, state);
            float& phase = state.vars["__oscPhase"];
            phase += (periodFrames > 0.0f ? (2.0f * 3.14159265f / periodFrames) : 0.0f);
            float t = (sinf(phase) + 1.0f) * 0.5f;
            if (actor.y) *actor.y = mn + (mx - mn) * t;
            return false;
        }

        // --- Combat / gameplay actions ---
        if (op == "Shoot") {
            float angle = GetNumberArg(block, "angle", 0.0f, actor, state);
            float speed = GetNumberArg(block, "speed", 1.0f, actor, state);
            float damage = GetNumberArg(block, "damage", 1.0f, actor, state);
            if (actor.shoot) actor.shoot(angle, speed, damage);
            return false;
        }
        if (op == "ShootAtPlayer") {
            float dx = actor.playerX - (actor.x ? *actor.x : 0.0f);
            float dy = actor.playerY - (actor.y ? *actor.y : 0.0f);
            float angle = atan2f(dy, dx);
            float speed = GetNumberArg(block, "speed", 1.0f, actor, state);
            float damage = GetNumberArg(block, "damage", 1.0f, actor, state);
            if (actor.shoot) actor.shoot(angle, speed, damage);
            return false;
        }
        if (op == "SetInvincible") {
            if (actor.invincible) *actor.invincible = block.value("on", false);
            return false;
        }
        if (op == "SetScale") {
            if (actor.scale) *actor.scale = GetNumberArg(block, "scale", *actor.scale, actor, state);
            return false;
        }
        if (op == "SetVisualEffect") {
            if (actor.visualEffect) actor.visualEffect(block.value("kind", ""), GetNumberArg(block, "intensity", 1.0f, actor, state));
            return false;
        }
        if (op == "PlaySound") {
            if (actor.playSound) actor.playSound(block.value("slot", ""));
            return false;
        }

        // --- Variables ---
        if (op == "SetVar") { state.vars[block.value("name", "")] = GetNumberArg(block, "value", 0.0f, actor, state); return false; }
        if (op == "ChangeVar") { state.vars[block.value("name", "")] += GetNumberArg(block, "value", 0.0f, actor, state); return false; }

        // 未知のop（将来の拡張やタイプミス）は無視して次のブロックへ進む
        return false;
    }
};
