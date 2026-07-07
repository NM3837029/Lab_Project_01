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
// AnimationController - スプライトシートアニメーション
// Feature 2: スプライトアニメーション
// ======================================================

struct AnimationClip {
    std::string name;
    int handle = -1;        // DrawRectGraph 用ハンドル
    int frameCountX = 1;    // 横分割数
    int frameCountY = 1;    // 縦分割数
    int startFrame = 0;
    int endFrame = 0;
    float fps = 8.0f;
    bool loop = true;
    int srcW = 0, srcH = 0; // スプライトシート全体サイズ
};

class AnimationController {
public:
    std::string currentClip;
    int currentFrame = 0;
    float timer = 0.0f;
    bool finished = false;

    void LoadForAsset(const std::string& assetId, const std::string& assetsPath) {
        Release();
        std::string path = assetsPath + "/animations.json";
        std::ifstream f(path);
        if (!f.is_open()) return;

        json j = json::parse(f, nullptr, false);
        if (j.is_discarded()) {
            Logger::Error("AnimationController", "LoadForAsset", "JSON parse error", path);
            return;
        }

        if (!j.is_array()) {
            Logger::Error("AnimationController", "LoadForAsset", "JSON is not an array", path);
            return;
        }

        for (const auto& aset : j) {
            if (!aset.is_object()) continue;
            if (aset.value("assetId", "") != assetId) continue;
            if (!aset.contains("clips") || !aset["clips"].is_array()) break;

            for (const auto& cj : aset["clips"]) {
                if (!cj.is_object()) continue;

                AnimationClip clip;
                clip.name        = cj.value("name", "Idle");
                std::string spritePath = cj.value("sprite", "");
                clip.frameCountX = cj.value("frameCountX", 1);
                if (clip.frameCountX <= 0) clip.frameCountX = 1; // Prevent div by 0
                clip.frameCountY = cj.value("frameCountY", 1);
                if (clip.frameCountY <= 0) clip.frameCountY = 1; // Prevent div by 0
                clip.startFrame  = cj.value("startFrame", 0);
                clip.endFrame    = cj.value("endFrame", 0);
                clip.fps         = cj.value("fps", 8.0f);
                clip.loop        = cj.value("loop", true);
                
                if (!spritePath.empty()) {
                    clip.handle = LoadGraph(spritePath.c_str());
                    if (clip.handle >= 0) {
                        GetGraphSize(clip.handle, &clip.srcW, &clip.srcH);
                    } else {
                        Logger::Error("AnimationController", "LoadForAsset", "Failed to load sprite graph", spritePath);
                    }
                }
                clipsMap[clip.name] = clip;
            }
            break;
        }

        if (!clipsMap.empty()) {
            currentClip = clipsMap.begin()->first;
        }
    }

    void Play(const std::string& clipName, bool forceRestart = false) {
        if (clipName == currentClip && !forceRestart) return;
        if (clipsMap.find(clipName) == clipsMap.end()) return;
        currentClip = clipName;
        currentFrame = clipsMap[clipName].startFrame;
        timer = 0.0f;
        finished = false;
    }

    void Update(float dt) {
        if (currentClip.empty() || finished) return;
        auto it = clipsMap.find(currentClip);
        if (it == clipsMap.end()) return;
        auto& clip = it->second;
        if (clip.fps <= 0.0f) return;

        timer += dt;
        float frameTime = 1.0f / clip.fps;
        while (timer >= frameTime) {
            timer -= frameTime;
            currentFrame++;
            if (currentFrame > clip.endFrame) {
                if (clip.loop) {
                    currentFrame = clip.startFrame;
                } else {
                    currentFrame = clip.endFrame;
                    finished = true;
                    break;
                }
            }
        }
    }

    void DrawAt(int cx, int cy, float scale, double angle, bool flipX = false) const {
        if (currentClip.empty()) return;
        auto it = clipsMap.find(currentClip);
        if (it == clipsMap.end()) return;
        const auto& clip = it->second;
        if (clip.handle < 0 || clip.srcW <= 0 || clip.srcH <= 0) return;

        int fw = clip.srcW / clip.frameCountX;
        int fh = clip.srcH / clip.frameCountY;
        int frame = currentFrame;
        int fx = (frame % clip.frameCountX) * fw;
        int fy = (frame / clip.frameCountX) * fh;

        DrawRectRotaGraph(cx, cy, fx, fy, fw, fh, scale, angle, clip.handle, TRUE, flipX ? TRUE : FALSE);
    }

    int GetHandle() const {
        auto it = clipsMap.find(currentClip);
        if (it != clipsMap.end()) return it->second.handle;
        return -1;
    }

    int GetCurrentFrameHeight() const {
        if (currentClip.empty()) return 0;
        auto it = clipsMap.find(currentClip);
        if (it == clipsMap.end()) return 0;
        const auto& clip = it->second;
        if (clip.frameCountY <= 0) return 0;
        return clip.srcH / clip.frameCountY;
    }

    bool HasClip(const std::string& name) const {
        return clipsMap.find(name) != clipsMap.end();
    }

    void Release() {
        for (std::map<std::string, AnimationClip>::iterator it = clipsMap.begin(); it != clipsMap.end(); ++it) {
            if (it->second.handle >= 0) { DeleteGraph(it->second.handle); it->second.handle = -1; }
        }
        clipsMap.clear();
        currentClip = "";
        currentFrame = 0;
        timer = 0.0f;
        finished = false;
    }

private:
    std::map<std::string, AnimationClip> clipsMap;
};
