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
// EventManager - Event/Trigger system
// Feature 5: Event triggers
// ======================================================

struct EventActionEntry {
    std::string action;
    std::string param1, param2;
    float delay = 0.0f;
};

// Common event definition (RPG Maker MZ style). Referenced by the CallCommonEvent action.
struct CommonEventDef {
    std::string id;
    std::string name;
    std::vector<EventActionEntry> actions;
};

struct EventTrigger {
    std::string id;
    float x = 0, y = 0, w = 64, h = 480;
    std::string condition;
    std::string conditionParam;
    bool oneShot = true;
    bool triggered = false;
    std::vector<EventActionEntry> actions;

    // Runtime-only state, not persisted to JSON.
    bool wasPlayerInside = false; // for PlayerExit
    float elapsedTime = 0.0f;     // for TimerExpired
};

using ActionCallback = std::function<void(const std::string&, const std::string&, const std::string&)>;

class EventManager {
public:
    static EventManager& Get() {
        static EventManager instance;
        return instance;
    }

    void SetActionCallback(ActionCallback cb) { callback = cb; }

    void LoadFromJson(const json& j) {
        triggers.clear();
        actionQueue.clear();

        if (j.is_null() || j.empty()) return;

        if (!j.is_array()) {
            Logger::Error("EventManager", "LoadFromJson", "Trigger data must be an array.");
            return;
        }

        for (const auto& tj : j) {
            if (!tj.is_object()) {
                Logger::Error("EventManager", "LoadFromJson", "Trigger item is not an object.");
                continue;
            }

            EventTrigger tr;
            tr.id = tj.value("id", "");
            tr.x = tj.value("x", 0.0f);
            tr.y = tj.value("y", 0.0f);
            tr.w = tj.value("width", 64.0f);
            tr.h = tj.value("height", 480.0f);
            tr.condition = tj.value("condition", "PlayerEnter");
            tr.conditionParam = tj.value("conditionParam", "");
            tr.oneShot = tj.value("oneShot", true);
            tr.triggered = false;
            tr.wasPlayerInside = false;
            tr.elapsedTime = 0.0f;

            if (tj.contains("actions") && tj["actions"].is_array()) {
                for (const auto& aj : tj["actions"]) {
                    if (!aj.is_object()) continue;
                    EventActionEntry ae;
                    ae.action = aj.value("action", "");
                    ae.param1 = aj.value("param1", "");
                    ae.param2 = aj.value("param2", "");
                    ae.delay  = aj.value("delay", 0.0f);
                    tr.actions.push_back(ae);
                }
            }
            triggers.push_back(tr);
        }
    }

    // Loads common event definitions from assets/common_events.json. Referenced by the CallCommonEvent action.
    void LoadCommonEventsFromJson(const json& j) {
        commonEvents.clear();
        if (j.is_null() || !j.is_array()) return;

        for (const auto& cj : j) {
            if (!cj.is_object()) continue;
            CommonEventDef ce;
            ce.id = cj.value("id", "");
            ce.name = cj.value("name", "");
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

    void Reset() {
        for (size_t i = 0; i < triggers.size(); i++) {
            triggers[i].triggered = false;
            triggers[i].wasPlayerInside = false;
            triggers[i].elapsedTime = 0.0f;
        }
        actionQueue.clear();
    }

    // Shared switch state used by the SetSwitch action and the SwitchOn condition.
    void SetSwitch(const std::string& id, bool on) {
        if (on) activeSwitches.insert(id);
        else activeSwitches.erase(id);
    }
    bool IsSwitchOn(const std::string& id) const { return activeSwitches.count(id) > 0; }

    // collectedItemIds: assetIds of items collected so far on this stage, used by the ItemCollected condition.
    void Update(float dt, float playerX, float playerY, int enemyCount,
                const std::vector<std::string>& collectedItemIds,
                bool& stageClear, std::string& gotoStage) {

        for (size_t i = 0; i < triggers.size(); i++) {
            auto& tr = triggers[i];
            if (tr.oneShot && tr.triggered) continue;

            bool isInside = (playerX >= tr.x && playerX <= tr.x + tr.w &&
                             playerY >= tr.y && playerY <= tr.y + tr.h);
            tr.elapsedTime += dt;

            bool condMet = false;
            if (tr.condition == "PlayerEnter") {
                if (isInside) condMet = true;
            } else if (tr.condition == "PlayerExit") {
                if (tr.wasPlayerInside && !isInside) condMet = true;
            } else if (tr.condition == "AllEnemiesDefeated") {
                if (enemyCount == 0) condMet = true;
            } else if (tr.condition == "SwitchOn") {
                if (IsSwitchOn(tr.conditionParam)) condMet = true;
            } else if (tr.condition == "ItemCollected") {
                if (tr.conditionParam.empty()) {
                    if (!collectedItemIds.empty()) condMet = true;
                } else {
                    for (const auto& iid : collectedItemIds) {
                        if (iid == tr.conditionParam) { condMet = true; break; }
                    }
                }
            } else if (tr.condition == "TimerExpired") {
                float threshold = (float)atof(tr.conditionParam.c_str());
                if (tr.elapsedTime >= threshold) condMet = true;
            }

            tr.wasPlayerInside = isInside;

            if (condMet) {
                tr.triggered = true;
                for (size_t k = 0; k < tr.actions.size(); k++) {
                    auto& ac = tr.actions[k];
                    if (ac.delay > 0.0f) {
                        actionQueue.push_back({ac, ac.delay});
                    } else {
                        ExecuteAction(ac, stageClear, gotoStage);
                    }
                }
            }
        }

        for (int i = (int)actionQueue.size() - 1; i >= 0; i--) {
            actionQueue[i].timer -= dt;
            if (actionQueue[i].timer <= 0.0f) {
                ExecuteAction(actionQueue[i].entry, stageClear, gotoStage);
                actionQueue.erase(actionQueue.begin() + i);
            }
        }
    }

private:
    std::vector<EventTrigger> triggers;
    std::vector<CommonEventDef> commonEvents;
    std::set<std::string> activeSwitches;
    ActionCallback callback;

    struct QueuedAction {
        EventActionEntry entry;
        float timer;
    };
    std::vector<QueuedAction> actionQueue;

    EventManager() = default;
    ~EventManager() = default;
    EventManager(const EventManager&) = delete;
    EventManager& operator=(const EventManager&) = delete;

    void ExecuteAction(const EventActionEntry& ac, bool& stageClear, std::string& gotoStage) {
        if (ac.action == "StageClear") {
            stageClear = true;
        } else if (ac.action == "GoToStage") {
            gotoStage = ac.param1;
        } else if (ac.action == "SetSwitch") {
            bool on = !(ac.param2 == "off" || ac.param2 == "OFF" || ac.param2 == "Off");
            SetSwitch(ac.param1, on);
        } else if (ac.action == "CallCommonEvent") {
            for (const auto& ce : commonEvents) {
                if (ce.id == ac.param1) {
                    for (const auto& sub : ce.actions) {
                        if (sub.delay > 0.0f) {
                            actionQueue.push_back({sub, sub.delay});
                        } else {
                            ExecuteAction(sub, stageClear, gotoStage); // recurse to support nested CallCommonEvent
                        }
                    }
                    break;
                }
            }
        } else if (callback) {
            callback(ac.action, ac.param1, ac.param2);
        }
    }
};
