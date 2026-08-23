#pragma once
#include <string>
#include <vector>
#include <set>
#include <functional>
#include <fstream>
#include <cstdlib>
#include "json.hpp"
#include "SoundManager.h"
#include "Logger.h"
using json = nlohmann::json;

// ======================================================
// EventManager - イベント／トリガーシステム
// Feature 5: イベントトリガー
// ======================================================

// 1つのイベントアクション（何かを実行する命令）を表す構造体。
// トリガーやコモンイベントが持つ「actions」配列の1要素に対応する。
struct EventActionEntry {
    std::string action;          // 実行するアクションの種類（"StageClear"、"SetSwitch"など）
    std::string param1, param2;  // アクションごとに意味が変わるパラメータ（例：GoToStageなら移動先ステージ名）
    float delay = 0.0f;          // このアクションを実行するまでの遅延時間（秒）。0なら即座に実行する
};

// コモンイベントの定義（RPGツクールMZのコモンイベントに相当する仕組み）。
// CallCommonEventアクションから名前（id）で呼び出され、まとめて登録されたアクション群を実行する。
// Common event definition (RPG Maker MZ style). Referenced by the CallCommonEvent action.
struct CommonEventDef {
    std::string id;                        // コモンイベントを識別するID（CallCommonEventのparam1と対応）
    std::string name;                       // 表示・管理用の名前（実処理には使わない）
    std::vector<EventActionEntry> actions;  // このコモンイベントが呼ばれたときに実行するアクションの並び
};

// マップ上の特定の領域に配置される、1つのイベントトリガーを表す構造体。
// 「プレイヤーが入ったら」「敵を全滅させたら」などの条件と、それに応じて実行するアクションを持つ。
struct EventTrigger {
    std::string id;                          // トリガーを識別するID
    float x = 0, y = 0, w = 64, h = 480;      // トリガー領域の左上座標（x, y）と大きさ（w, h）
    std::string condition;                    // 発動条件の種類（"PlayerEnter"、"AllEnemiesDefeated"など）
    std::string conditionParam;               // 条件の種類ごとに使う追加パラメータ（例：SwitchOnならスイッチID）
    bool oneShot = true;                      // trueなら一度発動したら二度と発動しない（使い切りのイベント）
    bool triggered = false;                   // 既に発動済みかどうかのフラグ
    std::vector<EventActionEntry> actions;    // 条件が満たされたときに実行するアクションの並び

    // Runtime-only state, not persisted to JSON.
    // 以下はJSONには保存されず、プレイ中の一時的な状態としてのみ使うメンバー。
    bool wasPlayerInside = false; // for PlayerExit（直前のフレームでプレイヤーが領域内にいたかどうか。PlayerExit判定に使う）
    float elapsedTime = 0.0f;     // for TimerExpired（このトリガーが有効になってからの経過時間。TimerExpired判定に使う）
};

// アクションを実行する際に、EventManagerの外部（呼び出し元）へ処理を委譲するためのコールバック型。
// 引数は順に「アクション名」「param1」「param2」。StageClear等の組み込みアクション以外はこれ経由で処理される。
using ActionCallback = std::function<void(const std::string&, const std::string&, const std::string&)>;

// マップ上のイベントトリガー（当たり判定つきの発動条件）と、コモンイベントを管理するクラス。
// シングルトンとして実装されており、EventManager::Get()で唯一のインスタンスにアクセスする。
class EventManager {
public:
    // シングルトンインスタンスを取得する関数。
    // static変数として1つだけ生成され、プログラム全体で共有される。
    static EventManager& Get() {
        static EventManager instance;
        return instance;
    }

    // 組み込みアクション（StageClear、GoToStage等）以外のアクションが実行されたときに
    // 呼び出されるコールバック関数を登録する。
    void SetActionCallback(ActionCallback cb) { callback = cb; }

    // JSONデータからイベントトリガーの一覧を読み込み、内部状態を構築する関数。
    // ステージ切り替え時などに呼び出される想定。
    void LoadFromJson(const json& j) {
        // 前のステージのトリガー・待機中アクションが残らないよう、読み込み前に必ずクリアする。
        triggers.clear();
        actionQueue.clear();

        // データが無い、または空の場合はトリガー無しとして正常終了する。
        if (j.is_null() || j.empty()) return;

        // トップレベルは配列である想定。配列でなければ不正なデータとしてエラーログに記録する。
        if (!j.is_array()) {
            Logger::Error("EventManager", "LoadFromJson", "Trigger data must be an array.");
            return;
        }

        // 配列の要素を1つずつトリガーとして読み込んでいく。
        for (const auto& tj : j) {
            if (!tj.is_object()) {
                // 配列の要素がオブジェクトでない場合は不正なデータとしてログに記録し、この要素はスキップする。
                Logger::Error("EventManager", "LoadFromJson", "Trigger item is not an object.");
                continue;
            }

            EventTrigger tr;
            // 各項目をJSONから取得する。value()の第2引数はキーが存在しない場合のデフォルト値。
            tr.id = tj.value("id", "");
            tr.x = tj.value("x", 0.0f);
            tr.y = tj.value("y", 0.0f);
            tr.w = tj.value("width", 64.0f);
            tr.h = tj.value("height", 480.0f);
            tr.condition = tj.value("condition", "PlayerEnter");
            tr.conditionParam = tj.value("conditionParam", "");
            tr.oneShot = tj.value("oneShot", true);
            // 以下はランタイム状態なので、読み込み時は必ず初期値にリセットする。
            tr.triggered = false;
            tr.wasPlayerInside = false;
            tr.elapsedTime = 0.0f;

            // このトリガーに紐づくアクション一覧を読み込む。
            if (tj.contains("actions") && tj["actions"].is_array()) {
                for (const auto& aj : tj["actions"]) {
                    if (!aj.is_object()) continue; // アクション要素がオブジェクトでなければスキップ
                    EventActionEntry ae;
                    ae.action = aj.value("action", "");
                    ae.param1 = aj.value("param1", "");
                    ae.param2 = aj.value("param2", "");
                    ae.delay  = aj.value("delay", 0.0f);
                    tr.actions.push_back(ae);
                }
            }
            // 完成したトリガーを一覧に追加する。
            triggers.push_back(tr);
        }
    }

    // Loads common event definitions from assets/common_events.json. Referenced by the CallCommonEvent action.
    // assets/common_events.jsonからコモンイベントの定義一覧を読み込む関数。
    // ここで読み込んだ定義は、CallCommonEventアクションが実行されたときにidで検索して使われる。
    void LoadCommonEventsFromJson(const json& j) {
        // 前回読み込んだコモンイベントが残らないよう、必ずクリアしてから読み込む。
        commonEvents.clear();
        // データが無い、または配列でない場合はコモンイベント無しとして終了する（エラー扱いにはしない）。
        if (j.is_null() || !j.is_array()) return;

        // 配列の要素を1つずつコモンイベント定義として読み込んでいく。
        for (const auto& cj : j) {
            if (!cj.is_object()) continue; // 要素がオブジェクトでなければスキップ
            CommonEventDef ce;
            ce.id = cj.value("id", "");
            ce.name = cj.value("name", "");
            // このコモンイベントが持つアクション一覧を読み込む（構造はトリガーのactionsと同じ）。
            if (cj.contains("actions") && cj["actions"].is_array()) {
                for (const auto& aj : cj["actions"]) {
                    if (!aj.is_object()) continue;
                    EventActionEntry ae;
                    ae.action = aj.value("action", "");
                    ae.param1 = aj.value("param1", "");
                    ae.param2 = aj.value("param2", "");
                    ae.delay  = aj.value("delay", 0.0f);
                    ce.actions.push_back(ae);
                }
            }
            commonEvents.push_back(ce);
        }
    }

    // すべてのトリガーの発動状態（triggered、wasPlayerInside、elapsedTime）と、
    // 実行待ちのアクションキューをリセットする関数。ステージのリトライ時などに呼び出す想定。
    void Reset() {
        for (size_t i = 0; i < triggers.size(); i++) {
            triggers[i].triggered = false;
            triggers[i].wasPlayerInside = false;
            triggers[i].elapsedTime = 0.0f;
        }
        actionQueue.clear();
    }

    // Shared switch state used by the SetSwitch action and the SwitchOn condition.
    // SetSwitchアクションとSwitchOn条件の両方から参照される、共有スイッチ状態を切り替える関数。
    // id : スイッチを識別する名前
    // on : trueならスイッチをONにする（activeSwitchesに追加）、falseならOFFにする（削除）
    void SetSwitch(const std::string& id, bool on) {
        if (on) activeSwitches.insert(id);
        else activeSwitches.erase(id);
    }
    // 指定したIDのスイッチが現在ONになっているかどうかを調べる関数。
    bool IsSwitchOn(const std::string& id) const { return activeSwitches.count(id) > 0; }

    // collectedItemIds: assetIds of items collected so far on this stage, used by the ItemCollected condition.
    // 毎フレーム呼び出して、すべてのトリガーの発動条件をチェックし、条件を満たしたものを実行する関数。
    // dt              : 前回のUpdate呼び出しからの経過時間（秒）
    // playerX, playerY: プレイヤーの現在座標（トリガー領域との当たり判定に使う）
    // enemyCount      : 現在のステージに残っている敵の数（AllEnemiesDefeated条件に使う）
    // collectedItemIds: このステージでこれまでに取得したアイテムのアセットID一覧（ItemCollected条件に使う）
    // stageClear      : （出力）StageClearアクションが実行された場合にtrueがセットされる
    // gotoStage       : （出力）GoToStageアクションが実行された場合に移動先のステージ名がセットされる
    void Update(float dt, float playerX, float playerY, int enemyCount,
                const std::vector<std::string>& collectedItemIds,
                bool& stageClear, std::string& gotoStage) {

        // 登録されているすべてのトリガーについて、条件判定と発動処理を行う。
        for (size_t i = 0; i < triggers.size(); i++) {
            auto& tr = triggers[i];
            // 使い切り（oneShot）のトリガーで既に発動済みのものは、これ以上チェックしない。
            if (tr.oneShot && tr.triggered) continue;

            // プレイヤーがこのトリガーの矩形領域内にいるかどうかを判定する。
            bool isInside = (playerX >= tr.x && playerX <= tr.x + tr.w &&
                             playerY >= tr.y && playerY <= tr.y + tr.h);
            // TimerExpired条件用に、このトリガーが存在してからの経過時間を積算しておく。
            tr.elapsedTime += dt;

            // 条件の種類ごとに判定を行い、満たしていればcondMetをtrueにする。
            bool condMet = false;
            if (tr.condition == "PlayerEnter") {
                // プレイヤーが領域内に入っているだけで即座に条件成立とする。
                if (isInside) condMet = true;
            } else if (tr.condition == "PlayerExit") {
                // 直前は領域内にいて、今は領域外に出た（＝退出した瞬間）場合に条件成立とする。
                if (tr.wasPlayerInside && !isInside) condMet = true;
            } else if (tr.condition == "AllEnemiesDefeated") {
                // 敵の残数が0になった場合に条件成立とする。
                if (enemyCount == 0) condMet = true;
            } else if (tr.condition == "SwitchOn") {
                // 指定したスイッチIDがONになっている場合に条件成立とする。
                if (IsSwitchOn(tr.conditionParam)) condMet = true;
            } else if (tr.condition == "ItemCollected") {
                if (tr.conditionParam.empty()) {
                    // conditionParamが指定されていない場合は「何か1つでもアイテムを取得していれば成立」とする。
                    if (!collectedItemIds.empty()) condMet = true;
                } else {
                    // conditionParamが指定されている場合は、そのアセットIDのアイテムを取得済みかどうかを調べる。
                    for (const auto& iid : collectedItemIds) {
                        if (iid == tr.conditionParam) { condMet = true; break; }
                    }
                }
            } else if (tr.condition == "TimerExpired") {
                // conditionParamに指定された秒数（文字列）をfloatに変換し、経過時間がそれ以上になったら成立とする。
                float threshold = (float)atof(tr.conditionParam.c_str());
                if (tr.elapsedTime >= threshold) condMet = true;
            }

            // 次フレームのPlayerExit判定のために、今回の「領域内にいたか」を記録しておく。
            tr.wasPlayerInside = isInside;

            // 条件が成立した場合は、発動済みフラグを立てたうえで紐づくアクションをすべて処理する。
            if (condMet) {
                tr.triggered = true;
                for (size_t k = 0; k < tr.actions.size(); k++) {
                    auto& ac = tr.actions[k];
                    if (ac.delay > 0.0f) {
                        // 遅延が指定されている場合は即実行せず、待機キューに積んで後で実行する。
                        actionQueue.push_back({ac, ac.delay});
                    } else {
                        // 遅延が無ければその場ですぐに実行する。
                        ExecuteAction(ac, stageClear, gotoStage);
                    }
                }
            }
        }

        // 待機中のアクションキューを処理する。逆順にループしているのは、
        // 実行後にerase()で要素を削除してもインデックスがずれないようにするため。
        for (int i = (int)actionQueue.size() - 1; i >= 0; i--) {
            actionQueue[i].timer -= dt;
            if (actionQueue[i].timer <= 0.0f) {
                // 待機時間が経過したアクションを実行し、キューから取り除く。
                ExecuteAction(actionQueue[i].entry, stageClear, gotoStage);
                actionQueue.erase(actionQueue.begin() + i);
            }
        }
    }

private:
    std::vector<EventTrigger> triggers;        // 読み込まれているすべてのイベントトリガー
    std::vector<CommonEventDef> commonEvents;  // 読み込まれているすべてのコモンイベント定義
    std::set<std::string> activeSwitches;      // 現在ONになっているスイッチのID集合
    ActionCallback callback;                   // 組み込み以外のアクションを処理するための外部コールバック

    // 遅延実行待ちのアクションを、残り待機時間とセットで保持するための構造体。
    struct QueuedAction {
        EventActionEntry entry; // 実行するアクションの内容
        float timer;            // 実行までの残り時間（秒）
    };
    std::vector<QueuedAction> actionQueue; // 遅延実行待ちのアクション一覧

    // シングルトンにするため、コンストラクタ・デストラクタをprivateにし、
    // コピーコンストラクタ・代入演算子を削除して複製できないようにしている。
    EventManager() = default;
    ~EventManager() = default;
    EventManager(const EventManager&) = delete;
    EventManager& operator=(const EventManager&) = delete;

    // 1つのアクションを実際に実行する関数。組み込みのアクション種別はここで直接処理し、
    // それ以外は外部から登録されたcallbackに処理を委譲する。
    // ac         : 実行するアクションの内容
    // stageClear : （出力）StageClearアクションの場合にtrueがセットされる
    // gotoStage  : （出力）GoToStageアクションの場合に移動先ステージ名がセットされる
    void ExecuteAction(const EventActionEntry& ac, bool& stageClear, std::string& gotoStage) {
        if (ac.action == "StageClear") {
            // ステージクリア扱いにする（呼び出し元がこのフラグを見て遷移処理を行う）。
            stageClear = true;
        } else if (ac.action == "GoToStage") {
            // 指定されたステージ名への移動を呼び出し元に伝える。
            gotoStage = ac.param1;
        } else if (ac.action == "SetSwitch") {
            // param2が"off"（大文字小文字問わず）でなければON、"off"であればOFFとしてスイッチを設定する。
            bool on = !(ac.param2 == "off" || ac.param2 == "OFF" || ac.param2 == "Off");
            SetSwitch(ac.param1, on);
        } else if (ac.action == "CallCommonEvent") {
            // param1で指定されたIDのコモンイベントを探す。
            for (const auto& ce : commonEvents) {
                if (ce.id == ac.param1) {
                    // 見つかったコモンイベントが持つアクションを、通常のトリガーと同じ要領で実行する。
                    for (const auto& sub : ce.actions) {
                        if (sub.delay > 0.0f) {
                            actionQueue.push_back({sub, sub.delay});
                        } else {
                            // recurse to support nested CallCommonEvent
                            // 再帰呼び出しにすることで、コモンイベントの中からさらに別のコモンイベントを
                            // 呼び出す（ネストしたCallCommonEvent）ケースにも対応できるようにしている。
                            ExecuteAction(sub, stageClear, gotoStage);
                        }
                    }
                    break; // 目的のコモンイベントを処理し終えたので、以降の検索は不要
                }
            }
        } else if (callback) {
            // 組み込みで対応していないアクション種別は、外部から登録されたコールバックに丸ごと渡す。
            callback(ac.action, ac.param1, ac.param2);
        }
    }
};
