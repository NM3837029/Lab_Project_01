#pragma once
#include "DxLib.h"
#include <string>
#include <map>
#include <vector>
#include <fstream>
#include "json.hpp"
#include "Logger.h"
using json = nlohmann::json;

// ======================================================
// SoundManager - DXライブラリを使ったBGM/SE管理クラス
// Feature 3: サウンド管理
// ======================================================

struct SoundEntry {
    std::string id;
    std::string file;
    bool isLoop = false;
    int handle = -1;
    // Feature: サウンド・アセット管理の刷新 — 0.0〜1.0の音量（Lab_Editor側のSoundDef.volumeと対応）。
    // 再生時にDxLibの0〜255スケールへ変換して ChangeVolumeSoundMem に渡す。
    float volume = 1.0f;
};

class SoundManager {
public:
    static SoundManager& Get() {
        static SoundManager instance;
        return instance;
    }

    void LoadFromJson(const std::string& assetsPath) {
        LoadEntries(assetsPath + "/bgm.json", bgmMap);
        LoadEntries(assetsPath + "/se.json",  seMap);
        // UI音（メニュー選択・決定・キャンセル等）はseMapに統合し、PlaySe(id)でそのまま再生可能にする。
        // カテゴリ分けはエディタ側の整理用の概念であり、再生側はSE/UI音を区別しない。
        LoadEntries(assetsPath + "/ui_se.json", seMap);
    }

    void PlayBgm(const std::string& id, bool forceRestart = false) {
        auto it = bgmMap.find(id);
        if (it == bgmMap.end() || it->second.handle < 0) return;
        int h = it->second.handle;

        if (currentBgmId == id && CheckSoundMem(h) == 1 && !forceRestart) return;

        StopBgm();
        currentBgmId = id;
        ChangeVolumeSoundMem((int)(it->second.volume * 255), h);
        PlaySoundMem(h, DX_PLAYTYPE_LOOP, TRUE);
    }

    void StopBgm() {
        if (currentBgmId.empty()) return;
        auto it = bgmMap.find(currentBgmId);
        if (it != bgmMap.end() && it->second.handle >= 0)
            StopSoundMem(it->second.handle);
        currentBgmId = "";
    }

    // Mute toggle for the player's stealth tool (M key). Only suppresses newly-triggered SE, not BGM.
    void SetMuted(bool muted) { isMuted = muted; }
    bool IsMuted() const { return isMuted; }

    void PlaySe(const std::string& id) {
        if (id.empty() || isMuted) return;
        auto it = seMap.find(id);
        if (it == seMap.end() || it->second.handle < 0) return;
        int dup = DuplicateSoundMem(it->second.handle);
        if (dup >= 0) {
            // 音量は複製後の再生用ハンドル(dup)に適用する（元のit->second.handleに適用すると
            // 以降ロードし直すまで全ての複製に影響してしまうため）
            ChangeVolumeSoundMem((int)(it->second.volume * 255), dup);
            PlaySoundMem(dup, DX_PLAYTYPE_BACK, TRUE);
            tempHandles.push_back(dup);
        }
    }

    void Update() {
        for (int i = (int)tempHandles.size() - 1; i >= 0; i--) {
            if (CheckSoundMem(tempHandles[i]) == 0) {
                DeleteSoundMem(tempHandles[i]);
                tempHandles.erase(tempHandles.begin() + i);
            }
        }
    }

    void Release() {
        StopBgm();
        for (std::map<std::string, SoundEntry>::iterator it = bgmMap.begin(); it != bgmMap.end(); ++it) {
            if (it->second.handle >= 0) { DeleteSoundMem(it->second.handle); it->second.handle = -1; }
        }
        for (std::map<std::string, SoundEntry>::iterator it = seMap.begin(); it != seMap.end(); ++it) {
            if (it->second.handle >= 0) { DeleteSoundMem(it->second.handle); it->second.handle = -1; }
        }
        for (size_t i = 0; i < tempHandles.size(); i++) {
            DeleteSoundMem(tempHandles[i]);
        }
        tempHandles.clear();
        bgmMap.clear();
        seMap.clear();
        currentBgmId = "";
    }

    bool HasBgm(const std::string& id) const {
        auto it = bgmMap.find(id);
        return it != bgmMap.end() && it->second.handle >= 0;
    }
    bool HasSe(const std::string& id) const {
        auto it = seMap.find(id);
        return it != seMap.end() && it->second.handle >= 0;
    }

private:
    std::map<std::string, SoundEntry> bgmMap;
    std::map<std::string, SoundEntry> seMap;
    std::vector<int> tempHandles;
    std::string currentBgmId;
    bool isMuted = false;

    SoundManager() = default;
    ~SoundManager() { Release(); }
    SoundManager(const SoundManager&) = delete;
    SoundManager& operator=(const SoundManager&) = delete;

    void LoadEntries(const std::string& path, std::map<std::string, SoundEntry>& target) {
        std::ifstream f(path);
        if (!f.is_open()) return;
        
        json j = json::parse(f, nullptr, false);
        if (j.is_discarded()) {
            Logger::Error("SoundManager", "LoadEntries", "JSON parse error", path);
            return;
        }

        if (!j.is_array()) {
            Logger::Error("SoundManager", "LoadEntries", "JSON is not an array", path);
            return;
        }

        for (const auto& e : j) {
            if (!e.is_object()) continue;

            SoundEntry entry;
            entry.id   = e.value("id", "");
            entry.file = e.value("file", "");
            entry.isLoop = e.value("isLoop", false);
            entry.volume = e.value("volume", 1.0f);
            if (entry.volume < 0.0f) entry.volume = 0.0f;
            if (entry.volume > 1.0f) entry.volume = 1.0f;

            if (!entry.id.empty() && !entry.file.empty()) {
                entry.handle = LoadSoundMem(entry.file.c_str());
                if (entry.handle < 0) {
                    Logger::Error("SoundManager", "LoadEntries", "Failed to load sound file", entry.file);
                } else {
                    target[entry.id] = entry;
                }
            }
        }
    }
};
