#pragma once
#include <string>
#include <vector>
#include <functional>
#include <fstream>
#include "json.hpp"
#include "SoundManager.h"
#include "Logger.h"
using json = nlohmann::json;

// ======================================================
// EventManager - イベント・トリガーシステム
// Feature 5: イベント・トリガー
// ======================================================

struct EventActionEntry {
    std::string action;
    std::string param1, param2;
    float delay = 0.0f;
};

struct EventTrigger {
    std::string id;
    float x = 0, y = 0, w = 64, h = 480;
    std::string condition;
    std::string conditionParam;
    bool oneShot = true;
    bool triggered = false;
    std::vector<EventActionEntry> actions;
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
            tr.w = tj.value("w", 64.0f);
            tr.h = tj.value("h", 480.0f);
            tr.condition = tj.value("condition", "PlayerEnter");
            tr.conditionParam = tj.value("conditionParam", "");
            tr.oneShot = tj.value("oneShot", true);
            tr.triggered = false;

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

    void Reset() {
        for (size_t i = 0; i < triggers.size(); i++) {
            triggers[i].triggered = false;
        }
        actionQueue.clear();
    }

    void Update(float dt, float playerX, float playerY, int enemyCount,
                bool& stageClear, std::string& gotoStage) {
        
        for (size_t i = 0; i < triggers.size(); i++) {
            auto& tr = triggers[i];
            if (tr.oneShot && tr.triggered) continue;

            bool condMet = false;
            if (tr.condition == "PlayerEnter") {
                if (playerX >= tr.x && playerX <= tr.x + tr.w &&
                    playerY >= tr.y && playerY <= tr.y + tr.h) {
                    condMet = true;
                }
            } else if (tr.condition == "AllEnemiesDefeated") {
                if (enemyCount == 0) condMet = true;
            }

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
        } else if (ac.action == "GotoStage") {
            gotoStage = ac.param1;
        } else if (callback) {
            callback(ac.action, ac.param1, ac.param2);
        }
    }
};
