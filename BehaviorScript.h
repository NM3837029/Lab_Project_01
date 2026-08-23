#pragma once
// 外部ライブラリ・標準ライブラリのインクルード。
#include "json.hpp"      // スクリプトAST（JSON）を扱うためのJSONライブラリ（nlohmann::json）
#include "Logger.h"      // 実行時エラーをログファイルに記録するためのロガー
#include <vector>        // コールスタック（std::vector<ScriptFrame>）に使用
#include <unordered_map> // スクリプト変数（std::unordered_map<std::string, float>）に使用
#include <string>
#include <functional>    // 弾生成/SE再生/画面演出などのコールバック（std::function）に使用
#include <cmath>         // sqrtf/atan2f/sinf/cosf等の数学関数に使用
#include <cstdlib>       // rand()/RAND_MAX（Randレポーターの乱数生成）に使用
// 以降、json という名前で nlohmann::json を使えるようにする別名定義
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
    // アクターの現在位置（ワールド座標）へのポインタ。呼び出し側の実体(Enemy/Gimmick)が
    // 持つx/y変数を直接指しており、スクリプト側から書き換えるとそのまま実体の位置が変わる。
    float* x = nullptr;
    float* y = nullptr;
    // アクターの速度（1フレームあたりの移動量）へのポインタ。x/yと同様に実体側の変数を直接指す。
    float* vx = nullptr;
    float* vy = nullptr;
    int* direction = nullptr;   // 0=右向き, 1=左向き（既存のenemy.direction/gim系と同じ規約）
    float* scale = nullptr;     // 存在しない対象はnullptrのままでよい（安全にスキップされる）
    bool* invincible = nullptr; // 同上（無敵状態フラグへのポインタ。対象に無ければnullptrのままでよい）
    float* angle = nullptr;     // 見た目の回転角（Parts-M2で新設。ドアのパネルや砲塔の向きなど）

    // 呼び出し側が毎フレーム計算して渡す、このフレームのタイムスケール（globalTimeScale等）。
    // 早送り/スローモーション中にネイティブ実装の敵/ギミックと体感速度を揃えるため、
    // 移動量やWaitのフレーム消費に反映する。既定の1.0なら従来と同じ挙動になる。
    float timeScale = 1.0f;

    // センシング用に呼び出し側が毎フレーム計算して渡すスナップショット
    float playerX = 0.0f, playerY = 0.0f;                   // プレイヤーの現在のワールド座標（DirectionToPlayer等で使用）
    bool isGrounded = false;                                // このアクターが現在地面に接地しているかどうか
    bool wallAheadLeft = false, wallAheadRight = false;     // 左側/右側の少し先に壁があるかどうか
    bool groundAheadLeft = false, groundAheadRight = false; // 左側/右側の少し先に地面があるかどうか（崖判定用）

    // Feature: Composite Multi-Part Objects (Parts-M2) — パーツ(部品)としての実行時のみ意味を持つ
    float parentX = 0.0f, parentY = 0.0f; // 親(複合体本体)の現在のワールド座標
    float parentDirection = 1.0f;         // 親の向き（+1=右向き/-1=左向き）。SetLocalOffset等で
                                           // 進行方向に応じてパーツを反転させたい場合に使う
    bool hasParent = false;               // このアクターがパーツかどうか
    int partIndex = 0;                    // 親のparts[]内インデックス（同一スクリプトを複数パーツで共有し、
                                           // パーツごとに異なる位相をつけるためのPartIndexレポーターに使う）

    // 環境依存のグローバル呼び出し（弾生成・SE再生・画面演出）はコールバックとして注入する
    std::function<void(float angleRad, float speed, float damage)> shoot;         // 弾を発射する処理（角度[ラジアン]・速度・ダメージ量を渡す）
    std::function<void(const std::string& slot)> playSound;                       // 効果音を再生する処理（再生するスロット名を渡す）
    std::function<void(const std::string& kind, float intensity)> visualEffect;   // 画面演出を発生させる処理（種類と強度を渡す）
};

// コールスタックの1フレーム分。通常のブロック列・Forever・Repeat(N)・RepeatUntil(cond)を
// この1つの構造体で表現する（isForever/repeatRemaining/isUntilLoopで種別を区別）。
struct ScriptFrame {
    const json* body = nullptr;      // 実行中のブロック列（JSON配列）へのポインタ。ownedProgramの内部を指す
    size_t pc = 0;                   // 次に実行するブロックのインデックス（プログラムカウンタ）
    bool isForever = false;          // trueならForeverループ（1周し終えるたびに先頭へ戻り続ける）
    int repeatRemaining = -1;        // >=0 の場合Repeat(N)。1周ごとに消費し、0になったら終了する
    bool isUntilLoop = false;        // trueならRepeatUntilループ（untilCondが満たされるまで繰り返す）
    const json* untilCond = nullptr; // RepeatUntilの終了条件式（isUntilLoop==trueのときのみ使用）

    // 通常のブロック列（If/IfElse/OnSpawn直下など）用のフレームを生成する
    static ScriptFrame Plain(const json* b) { ScriptFrame f; f.body = b; return f; }
    // Foreverループ用のフレームを生成する
    static ScriptFrame ForeverLoop(const json* b) { ScriptFrame f; f.body = b; f.isForever = true; return f; }
    // Repeat(N)ループ用のフレームを生成する（nは繰り返し回数）
    static ScriptFrame CountLoop(const json* b, int n) { ScriptFrame f; f.body = b; f.repeatRemaining = n; return f; }
    // RepeatUntilループ用のフレームを生成する（condが終了条件式）
    static ScriptFrame UntilLoop(const json* b, const json* cond) { ScriptFrame f; f.body = b; f.isUntilLoop = true; f.untilCond = cond; return f; }
};

struct ScriptState {
    // Start()時にprogram（EnemyDef/GimmickDef.script）をディープコピーして所有する。
    // ScriptFrame.bodyはこのownedProgramの内部を指すため、呼び出し元のenemyDefs/gimmickDefsが
    // 後で再確保・変更されても（例: 実行中にSpawnEnemyで敵ベクタが再確保される等）ダングリングポインタに
    // ならず安全に動作する。ScriptState自体はEnemy/Gimmickのメンバとして値で保持されるため、
    // 所有権はそのままEnemy/Gimmickインスタンスのライフタイムに一致する。
    json ownedProgram = json::array();
    std::vector<ScriptFrame> callStack;           // 実行中のブロック列・ループのスタック（ネストした制御構造を表す）
    float waitFramesRemaining = 0.0f;             // timeScale分ずつ減算するためfloatで保持する
    std::unordered_map<std::string, float> vars;  // スクリプト変数（SetVar/ChangeVar/GetVarで読み書きする）
    bool started = false;   // Start()が呼ばれて実行が開始されたかどうか
    bool finished = false;  // スクリプトの実行がすべて完了したかどうか（完了後はTick()が何もしなくなる）
    bool faulted = false;   // 実行中に致命的なエラーが発生したかどうか（発生後はTick()が何もしなくなる）
    std::string faultMsg;   // faulted==trueのときのエラー内容メッセージ
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

    // Feature: Composite Multi-Part Objects (Parts-M2) — グローバル経過フレーム数。
    // リセットせず、毎フレーム加算し続けるだけ（Waitのフレームカウントとは無関係の「時計」）。
    // ポーズ中も止まらないため、GIMMICK_TIME_FIELDの「ポーズ中も動き続ける」演出はこちらを直接参照する。
    static inline float globalFrameCounter = 0.0f;

    // スクリプトの Time レポーターが参照する時計。globalFrameCounterと異なりポーズ中は増加しないため、
    // ポーズ解除直後にTime依存の回転/振動パーツが不連続にジャンプすることがない。
    static inline float scriptTimeCounter = 0.0f;

    // program（enemies.json等の"script"フィールド）からhatNameのハット（"OnSpawn"等）を探し、
    // 見つかればその本体から実行を開始する。見つからなければ何もしない（finished=trueのまま）。
    static void Start(ScriptState& state, const json& program, const std::string& hatName) {
        state = ScriptState(); // 既存の状態を破棄し、まっさらな状態から開始する
        state.ownedProgram = program; // ディープコピーして所有権を持つ（呼び出し元の寿命に依存しないようにする）
        const json* body = FindHatBody(state.ownedProgram, hatName); // 指定されたハット名の本体を探す
        if (!body) { state.finished = true; return; } // 見つからなければ何もせず完了扱いにする
        state.callStack.push_back(ScriptFrame::Plain(body)); // 見つかった本体をコールスタックの最初のフレームとして積む
        state.started = true; // 実行開始済みとしてマークする
    }

    // Feature: Composite Multi-Part Objects (Parts-M6) — OnDamaged/OnDeath等の反応イベントを発火する。
    // 呼び出し側が持つ専用のreactiveState（scriptStateとは別のScriptState）に対して使うことを想定している。
    // Start()は毎回stateを完全にリセットするため、OnSpawn用のscriptState(Foreverループ等)を巻き込まないよう、
    // 必ずreactiveState専用のScriptStateへ対して呼ぶこと。v1では、そのティック内で完結する処理
    // （PlaySound/SetVisualEffect/Shoot等）のみを想定し、Waitを跨ぐ複数フレームの演出はサポートしない。
    static void FireReactiveHat(ScriptState& reactiveState, const json& program, const std::string& hatName, ScriptActor& actor) {
        Start(reactiveState, program, hatName); // 該当ハットからreactiveStateを初期化する
        Tick(reactiveState, actor);             // そのティック内で即座に（Waitに当たるまで）実行する
    }

    // 1フレーム分だけ実行を進める。Wait系に達するか、安全装置の上限に達するとその場で戻る。
    static void Tick(ScriptState& state, ScriptActor& actor) {
        // 既にエラー発生済み／完了済み／未開始のいずれかであれば、何もせず抜ける
        if (state.faulted || state.finished || !state.started) return;
        // Wait中であれば、timeScale分だけ待機フレーム数を減らして今フレームは何もしない
        // （timeScaleが0以下の異常値の場合は1.0として扱い、待機が永遠に終わらないようにする）
        if (state.waitFramesRemaining > 0.0f) { state.waitFramesRemaining -= (actor.timeScale > 0.0f ? actor.timeScale : 1.0f); return; }

        int opsThisTick = 0; // このTick呼び出し内で実行したブロック数（アクター単体の安全装置用カウンタ）
        while (!state.callStack.empty()) {
            // 安全装置1: 全アクター合計の1フレームあたり実行数が上限を超えたら、他の個体に予算を譲ってここで打ち切る
            if (++globalOpsThisFrame > kMaxGlobalOpsPerFrame) return; // 他の個体がフレーム予算を使い切った
            // 安全装置2: このアクター単体の1フレームあたり実行数が上限を超えたら、続きは次フレームに持ち越す
            if (++opsThisTick > kMaxOpsPerTick) return;               // このフレームはここまで、次フレームへ持ち越し
            // 安全装置3: コールスタックのネストが深くなりすぎている場合は、無限再帰等の不正なスクリプトとみなし停止する
            if ((int)state.callStack.size() > kMaxCallDepth) {
                Fault(state, "call stack too deep (possible malformed script)");
                return;
            }

            ScriptFrame& frame = state.callStack.back(); // 現在実行中の最も内側のフレーム（ブロック列）を取得する

            // 現在のフレームの末尾まで実行し終えた場合の後処理
            if (frame.pc >= frame.body->size()) {
                if (frame.isForever) {
                    frame.pc = 0; // 先頭に戻して次回も繰り返す
                    return; // Foreverが1周した瞬間は必ずこのティックを終える（安全装置）
                }
                if (frame.repeatRemaining >= 0) {
                    frame.repeatRemaining--; // 残り回数を1つ消費する
                    if (frame.repeatRemaining > 0) { frame.pc = 0; return; } // まだ残りがあれば先頭に戻る（Repeatが1周した瞬間も同様にこのティックを終える）
                    state.callStack.pop_back(); // 規定回数を使い切ったのでこのフレームを終了する
                    if (state.callStack.empty()) { state.finished = true; return; } // 呼び出し元も無ければスクリプト全体が完了
                    continue; // 呼び出し元のフレームに戻って続きを実行する
                }
                if (frame.isUntilLoop) {
                    // 終了条件を評価する（条件式が無ければ常に終了扱いとする）
                    bool done = frame.untilCond ? EvalBool(*frame.untilCond, actor, state) : true;
                    if (!done) { frame.pc = 0; return; } // まだ終了条件を満たしていなければ先頭に戻ってこのティックを終える
                    state.callStack.pop_back(); // 終了条件を満たしたのでこのフレームを終了する
                    if (state.callStack.empty()) { state.finished = true; return; }
                    continue;
                }
                // 通常のブロック列（If/IfElse/OnSpawn直下など）が最後まで実行された
                state.callStack.pop_back(); // このフレームを終了し、呼び出し元のフレームに戻る
                if (state.callStack.empty()) { state.finished = true; return; } // 呼び出し元も無ければスクリプト全体が完了
                continue;
            }

            if (!frame.body->is_array()) {
                // 手編集された不正なJSON等、想定外の状態に対する安全網。ここに来ることは通常無いはず。
                Fault(state, "frame.body is not an array (type=" + std::string(frame.body->type_name()) + ")");
                return;
            }
            const json& block = (*frame.body)[frame.pc]; // 現在のプログラムカウンタが指すブロックを取得する
            frame.pc++; // 先に進めてから実行する（Wait/WaitUntilで正しく再開できるようにするため）
            if (!block.is_object()) continue; // ブロックの形式が不正な場合は無視して次へ進む
            if (ExecuteBlock(block, state, actor)) return; // Wait系はここでティックを終える
        }
        state.finished = true; // コールスタックが空になった（＝すべての実行が完了した）
    }

private:
    // スクリプトを致命的エラー状態にし、エラー内容をログに記録する。
    // msg : エラーの内容を説明する文字列
    static void Fault(ScriptState& state, const std::string& msg) {
        state.faulted = true;   // 以降のTick()呼び出しを無効化する
        state.faultMsg = msg;   // エラー内容を保持しておく（デバッグ表示等に使える）
        Logger::Error("BehaviorScript", "Tick", msg); // ログファイルにも記録しておく
    }

    // programの中から、指定したhatName（"OnSpawn"等）を持つスクリプトを探し、その本体(body配列)へのポインタを返す。
    // 見つからない場合、またはprogramの形式が不正な場合はnullptrを返す。
    static const json* FindHatBody(const json& program, const std::string& hatName) {
        if (!program.is_array()) return nullptr; // programがJSON配列でなければ探索不能
        for (const auto& script : program) {
            // scriptがオブジェクトで、"hat"フィールドがhatNameと一致し、かつ"body"が配列であるものを探す
            if (script.is_object() && script.value("hat", "") == hatName
                && script.contains("body") && script["body"].is_array())
                return &script["body"];
        }
        return nullptr; // 該当するハットが見つからなかった
    }

    // ── 数値/真偽値の評価（引数は数値リテラル or ネストしたレポーターブロック） ──

    // blockからkeyというフィールドを取り出し、数値として評価して返す汎用ヘルパー。
    // フィールドが無ければdefaultValを返し、数値リテラルならそのまま数値を返し、
    // オブジェクト（ネストしたレポーターブロック）ならEvalNumberで再帰的に評価する。
    static float GetNumberArg(const json& block, const std::string& key, float defaultVal, ScriptActor& actor, ScriptState& state) {
        if (!block.contains(key)) return defaultVal;   // フィールドが存在しない場合は既定値を返す
        const json& v = block[key];
        if (v.is_number()) return v.get<float>();       // 数値リテラルならそのまま返す
        if (v.is_object()) return EvalNumber(v, actor, state); // レポーターブロックなら再帰的に評価する
        return defaultVal; // それ以外の型（不正な値）の場合は既定値を返す
    }

    // 数値を返すレポーターブロック(expr)を評価する。exprが数値リテラルならそのまま返し、
    // オブジェクトなら"op"フィールドで種類を判定し、対応する計算結果を返す。
    // 未知のopの場合は0.0fを返す（未対応の演算子や将来の拡張に対する安全策）。
    static float EvalNumber(const json& expr, ScriptActor& actor, ScriptState& state) {
        if (expr.is_number()) return expr.get<float>(); // 数値リテラルはそのまま返す
        std::string op = expr.value("op", "");
        if (op == "Const") return expr.value("value", 0.0f);              // 定数値を返す
        if (op == "SelfX") return actor.x ? *actor.x : 0.0f;               // 自分自身のX座標を返す
        if (op == "SelfY") return actor.y ? *actor.y : 0.0f;               // 自分自身のY座標を返す
        if (op == "PlayerX") return actor.playerX;                        // プレイヤーのX座標を返す
        if (op == "PlayerY") return actor.playerY;                        // プレイヤーのY座標を返す
        if (op == "DistanceToPlayer") {
            // プレイヤーと自分自身の座標差から、ピタゴラスの定理で直線距離を求める
            float dx = actor.playerX - (actor.x ? *actor.x : 0.0f);
            float dy = actor.playerY - (actor.y ? *actor.y : 0.0f);
            return sqrtf(dx * dx + dy * dy);
        }
        if (op == "DirectionToPlayer") {
            // プレイヤーへ向かう方向を、自分自身からの相対座標のatan2でラジアン角として求める
            float dx = actor.playerX - (actor.x ? *actor.x : 0.0f);
            float dy = actor.playerY - (actor.y ? *actor.y : 0.0f);
            return atan2f(dy, dx);
        }
        if (op == "Random") {
            // min～maxの範囲でランダムな値を1つ返す（maxがmin以下なら不正指定とみなしminを返す）
            float mn = GetNumberArg(expr, "min", 0.0f, actor, state);
            float mx = GetNumberArg(expr, "max", 1.0f, actor, state);
            if (mx <= mn) return mn;
            return mn + ((float)rand() / (float)RAND_MAX) * (mx - mn);
        }
        if (op == "GetVar") {
            // 指定した名前のスクリプト変数を読み取る。未定義の変数は0.0fとして扱う
            auto it = state.vars.find(expr.value("name", ""));
            return it != state.vars.end() ? it->second : 0.0f;
        }
        if (op == "Add") return GetNumberArg(expr, "a", 0.0f, actor, state) + GetNumberArg(expr, "b", 0.0f, actor, state); // a + b
        if (op == "Sub") return GetNumberArg(expr, "a", 0.0f, actor, state) - GetNumberArg(expr, "b", 0.0f, actor, state); // a - b
        if (op == "Mul") return GetNumberArg(expr, "a", 0.0f, actor, state) * GetNumberArg(expr, "b", 0.0f, actor, state); // a * b
        if (op == "Div") {
            // a / b（0除算を避けるため、bが0の場合は0.0fを返す）
            float b = GetNumberArg(expr, "b", 1.0f, actor, state);
            return b != 0.0f ? GetNumberArg(expr, "a", 0.0f, actor, state) / b : 0.0f;
        }
        // Feature: Composite Multi-Part Objects (Parts-M2) — 汎用の三角関数・時計・親座標・パーツ位相
        if (op == "Sin") return sinf(GetNumberArg(expr, "a", 0.0f, actor, state)); // 引数(ラジアン)のsin値を返す
        if (op == "Cos") return cosf(GetNumberArg(expr, "a", 0.0f, actor, state)); // 引数(ラジアン)のcos値を返す
        if (op == "Time") return BehaviorInterpreter::scriptTimeCounter;          // ポーズ中は進まないスクリプト用の時計の現在値を返す
        if (op == "ParentX") return actor.parentX;                                // 親(複合体本体)のX座標を返す
        if (op == "ParentY") return actor.parentY;                                // 親(複合体本体)のY座標を返す
        if (op == "ParentDirection") return actor.parentDirection;                // 親の向き(+1=右向き/-1=左向き)を返す
        if (op == "PartIndex") return (float)actor.partIndex;                     // 親のparts[]内での自分のインデックスを返す
        return 0.0f; // 未知のop（将来の拡張やタイプミス）は0.0fを返す
    }

    // 真偽値を返すレポーターブロック(expr)を評価する。exprが真偽値リテラルならそのまま返し、
    // オブジェクトなら"op"フィールドで種類を判定し、対応する比較・論理演算の結果を返す。
    // 未知のopの場合はfalseを返す（未対応の演算子や将来の拡張に対する安全策）。
    static bool EvalBool(const json& expr, ScriptActor& actor, ScriptState& state) {
        if (expr.is_boolean()) return expr.get<bool>(); // 真偽値リテラルはそのまま返す
        std::string op = expr.value("op", "");
        if (op == "Gt") return GetNumberArg(expr, "a", 0.0f, actor, state) > GetNumberArg(expr, "b", 0.0f, actor, state); // a > b
        if (op == "Lt") return GetNumberArg(expr, "a", 0.0f, actor, state) < GetNumberArg(expr, "b", 0.0f, actor, state); // a < b
        if (op == "Eq") return GetNumberArg(expr, "a", 0.0f, actor, state) == GetNumberArg(expr, "b", 0.0f, actor, state); // a == b
        // AND/OR/NOTは、対応するフィールドが存在しない場合はfalse扱いとして評価する
        if (op == "And") return expr.contains("a") && expr.contains("b") && EvalBool(expr["a"], actor, state) && EvalBool(expr["b"], actor, state); // a かつ b
        if (op == "Or") return (expr.contains("a") && EvalBool(expr["a"], actor, state)) || (expr.contains("b") && EvalBool(expr["b"], actor, state)); // a または b
        if (op == "Not") return expr.contains("a") && !EvalBool(expr["a"], actor, state); // aの否定
        if (op == "IsGrounded") return actor.isGrounded; // 現在地面に接地しているか
        // 向いている方向側の壁/地面判定を返す（direction: 0=右向き, 1=左向き）
        if (op == "IsWallAhead") return (actor.direction && *actor.direction == 1) ? actor.wallAheadLeft : actor.wallAheadRight;
        if (op == "IsGroundAhead") return (actor.direction && *actor.direction == 1) ? actor.groundAheadLeft : actor.groundAheadRight;
        return false; // 未知のop（将来の拡張やタイプミス）はfalseを返す
    }

    // ── ブロック実行。trueを返すとそのティックはここで終了する（Wait系） ──

    // 1つのブロック(block)を実行する。戻り値がtrueの場合、Wait系ブロックにより
    // このティックの実行はここで打ち切られ、続きは次フレームのTick()で再開される。
    // falseの場合は、呼び出し元のTick()ループが引き続き次のブロックを実行する。
    static bool ExecuteBlock(const json& block, ScriptState& state, ScriptActor& actor) {
        std::string op = block.value("op", "");

        // --- Control（制御構文） ---
        if (op == "Forever") {
            // ボディを無限ループとしてコールスタックに積む（Tick()側の安全装置で1周ごとに必ず一旦停止する）
            if (block.contains("body") && block["body"].is_array())
                state.callStack.push_back(ScriptFrame::ForeverLoop(&block["body"]));
            return false;
        }
        if (op == "Repeat") {
            // countで指定された回数だけボディを繰り返す。countが0以下の場合は一度も実行しない
            int n = (int)GetNumberArg(block, "count", 1.0f, actor, state);
            if (n > 0 && block.contains("body") && block["body"].is_array())
                state.callStack.push_back(ScriptFrame::CountLoop(&block["body"], n));
            return false;
        }
        if (op == "RepeatUntil") {
            // condが真になるまでボディを繰り返す（condが省略された場合はTick()側で毎回true扱いとなり1周で終了する）
            if (block.contains("body") && block["body"].is_array())
                state.callStack.push_back(ScriptFrame::UntilLoop(&block["body"], block.contains("cond") ? &block["cond"] : nullptr));
            return false;
        }
        if (op == "If") {
            // condが真の場合のみボディを実行する
            bool cond = block.contains("cond") && EvalBool(block["cond"], actor, state);
            if (cond && block.contains("body") && block["body"].is_array())
                state.callStack.push_back(ScriptFrame::Plain(&block["body"]));
            return false;
        }
        if (op == "IfElse") {
            // condが真ならbody、偽ならelseの方を実行する
            bool cond = block.contains("cond") && EvalBool(block["cond"], actor, state);
            const char* key = cond ? "body" : "else";
            if (block.contains(key) && block[key].is_array())
                state.callStack.push_back(ScriptFrame::Plain(&block[key]));
            return false;
        }
        if (op == "Wait") {
            // 指定フレーム数だけ待機する（framesが負値の場合は0に丸める）。trueを返してこのティックを即終了する
            state.waitFramesRemaining = (std::max)(0.0f, GetNumberArg(block, "frames", 0.0f, actor, state));
            return true;
        }
        if (op == "WaitUntil") {
            // condが満たされるまで、このブロックに留まり続ける（毎フレーム再評価する）
            bool met = block.contains("cond") && EvalBool(block["cond"], actor, state);
            if (!met) { state.callStack.back().pc--; return true; } // 同じブロックを次フレームも再評価する
            return false; // 条件を満たしたので次のブロックへ進む
        }

        // --- Motion（移動系） ---
        if (op == "MoveDirection") {
            // 指定された方向(dir)・速度(speed)でX方向の速度(vx)を設定する。
            // dir: "Left"=常に左, "Right"=常に右, "Toward"=プレイヤーの方へ, "Away"=プレイヤーから離れる方へ
            std::string dir = block.value("dir", "Toward");
            float speed = GetNumberArg(block, "speed", 0.0f, actor, state);
            if (!actor.vx || !actor.x) return false; // 対象がvx/xを持たない場合は何もしない
            float vx = 0.0f;
            if (dir == "Left") { vx = -speed; if (actor.direction) *actor.direction = 1; }
            else if (dir == "Right") { vx = speed; if (actor.direction) *actor.direction = 0; }
            else {
                // "Toward"/"Away": プレイヤーが自分より左にいるかどうかで方向を決める
                bool towardLeft = actor.playerX < *actor.x;
                if (dir == "Away") towardLeft = !towardLeft; // "Away"の場合は逆方向にする
                vx = towardLeft ? -speed : speed;
                if (actor.direction) *actor.direction = towardLeft ? 1 : 0;
            }
            *actor.vx = vx * actor.timeScale; // タイムスケールを反映して実際の速度に設定する
            return false;
        }
        if (op == "ApplyImpulse") {
            // 明示的に指定された成分だけタイムスケールを反映する（省略時は現在値を維持する既存仕様を壊さないため）
            if (actor.vx && block.contains("vx")) *actor.vx = GetNumberArg(block, "vx", 0.0f, actor, state) * actor.timeScale;
            if (actor.vy && block.contains("vy")) *actor.vy = GetNumberArg(block, "vy", 0.0f, actor, state) * actor.timeScale;
            return false;
        }
        if (op == "SetPosition") {
            // x/yが指定されていればその値に、省略されていれば現在値のまま座標を設定する
            if (actor.x) *actor.x = GetNumberArg(block, "x", *actor.x, actor, state);
            if (actor.y) *actor.y = GetNumberArg(block, "y", *actor.y, actor, state);
            return false;
        }
        if (op == "OffsetPosition") {
            // 現在の座標にdx/dyを加算する（相対移動）
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
                *actor.x = actor.parentX + cosf(ang) * rad; // 角度と半径から極座標→直交座標に変換してXへ反映
                *actor.y = actor.parentY + sinf(ang) * rad; // 同様にYへ反映
            }
            return false;
        }
        if (op == "SetAngle") {
            // 見た目の回転角を設定する（省略時は現在値を維持する）
            if (actor.angle) *actor.angle = GetNumberArg(block, "angle", *actor.angle, actor, state);
            return false;
        }
        if (op == "FaceTowards") {
            // プレイヤーがいる方向を向く（プレイヤーが自分より左にいればdirection=1、右にいれば0）
            if (actor.direction && actor.x) *actor.direction = (actor.playerX < *actor.x) ? 1 : 0;
            return false;
        }
        if (op == "Oscillate") {
            // 動く足場などを想定し、Y座標をmin～maxの間で周期的に振動させる糖衣構文。
            // 位相はこのスクリプト専用の変数(__oscPhase)に保持する。
            float mn = GetNumberArg(block, "min", 0.0f, actor, state);
            float mx = GetNumberArg(block, "max", 1.0f, actor, state);
            float periodFrames = GetNumberArg(block, "periodFrames", 60.0f, actor, state);
            float& phase = state.vars["__oscPhase"]; // 位相を変数として永続化し、フレームをまたいで蓄積する
            // 周期(periodFrames)から1フレームあたりの位相増分を求め、タイムスケールを反映して加算する
            phase += (periodFrames > 0.0f ? (2.0f * 3.14159265f / periodFrames) : 0.0f) * actor.timeScale;
            float t = (sinf(phase) + 1.0f) * 0.5f; // sin波を0.0～1.0の範囲に正規化する
            if (actor.y) *actor.y = mn + (mx - mn) * t; // 正規化した値でmin～maxの間を補間してYに反映する
            return false;
        }

        // --- Combat / gameplay actions（戦闘・ゲームプレイ系アクション） ---
        if (op == "Shoot") {
            // 指定された角度(ラジアン)・速度・ダメージで弾を発射する
            float angle = GetNumberArg(block, "angle", 0.0f, actor, state);
            float speed = GetNumberArg(block, "speed", 1.0f, actor, state);
            float damage = GetNumberArg(block, "damage", 1.0f, actor, state);
            if (actor.shoot) actor.shoot(angle, speed, damage);
            return false;
        }
        if (op == "ShootAtPlayer") {
            // プレイヤーのいる方向を自動で狙って弾を発射する
            float dx = actor.playerX - (actor.x ? *actor.x : 0.0f);
            float dy = actor.playerY - (actor.y ? *actor.y : 0.0f);
            float angle = atan2f(dy, dx); // 自分からプレイヤーへの角度を求める
            float speed = GetNumberArg(block, "speed", 1.0f, actor, state);
            float damage = GetNumberArg(block, "damage", 1.0f, actor, state);
            if (actor.shoot) actor.shoot(angle, speed, damage);
            return false;
        }
        if (op == "SetInvincible") {
            // 無敵状態のON/OFFを切り替える
            if (actor.invincible) *actor.invincible = block.value("on", false);
            return false;
        }
        if (op == "SetScale") {
            // 表示スケール（拡大縮小率）を設定する（省略時は現在値を維持する）
            if (actor.scale) *actor.scale = GetNumberArg(block, "scale", *actor.scale, actor, state);
            return false;
        }
        if (op == "SetVisualEffect") {
            // 指定した種類(kind)・強度(intensity)の画面演出を発生させる
            if (actor.visualEffect) actor.visualEffect(block.value("kind", ""), GetNumberArg(block, "intensity", 1.0f, actor, state));
            return false;
        }
        if (op == "PlaySound") {
            // 指定したスロット名の効果音を再生する
            if (actor.playSound) actor.playSound(block.value("slot", ""));
            return false;
        }

        // --- Variables（スクリプト変数） ---
        if (op == "SetVar") { state.vars[block.value("name", "")] = GetNumberArg(block, "value", 0.0f, actor, state); return false; } // 変数に値を代入する
        if (op == "ChangeVar") { state.vars[block.value("name", "")] += GetNumberArg(block, "value", 0.0f, actor, state); return false; } // 変数に値を加算する

        // 未知のop（将来の拡張やタイプミス）は無視して次のブロックへ進む
        return false;
    }
};
