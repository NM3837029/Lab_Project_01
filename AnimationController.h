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

// 1つのアニメーション（例：Idle、Walk、Attackなど）の設定情報をまとめておく構造体。
// Lab_Editor側で作成したanimations.jsonの内容を、そのままこの構造体に読み込んで使う。
struct AnimationClip {
    std::string name;       // アニメーションの名前（"Idle"や"Walk"など、Play()で指定するキーになる）
    int handle = -1;        // DrawRectGraph 用ハンドル（LoadGraphで読み込んだスプライトシート画像のハンドル。-1は未読み込みを意味する）
    int frameCountX = 1;    // 横分割数（スプライトシート画像を横方向に何コマに分割しているか）
    int frameCountY = 1;    // 縦分割数（スプライトシート画像を縦方向に何コマに分割しているか）
    int startFrame = 0;     // アニメーション開始時のフレーム番号（コマ番号。0始まり）
    int endFrame = 0;       // アニメーション終了時のフレーム番号（このフレームまで再生したらループまたは停止する）
    float fps = 8.0f;       // 1秒間に何コマ進めるか（フレームレート）
    bool loop = true;       // trueならendFrameまで到達したらstartFrameに戻ってループ再生する。falseなら最後のフレームで停止する
    int srcW = 0, srcH = 0; // スプライトシート全体サイズ（画像全体の幅と高さ。1コマ分のサイズを求めるのに使う）
};

// スプライトシートを使ったアニメーション再生・描画を管理するクラス。
// キャラクターや敵などのアセットごとに1つ持たせて使う想定。
class AnimationController {
public:
    std::string currentClip; // 現在再生中のクリップ名（clipsMapのキーと一致する）
    int currentFrame = 0;    // 現在表示しているフレーム（コマ）番号
    float timer = 0.0f;      // 次のフレームに進むまでの経過時間を貯めるタイマー（秒）
    bool finished = false;   // ループしないクリップが最後まで再生し終わったかどうかのフラグ

    // 指定したアセットIDに対応するアニメーションクリップ一覧を、assets/animations.jsonから読み込む関数。
    // assetId    : どのアセットのアニメーション設定を読み込むかを指定するID
    // assetsPath : アセットファイル（animations.json）が置かれているフォルダのパス
    void LoadForAsset(const std::string& assetId, const std::string& assetsPath) {
        // 前回読み込んだアニメーション情報や画像ハンドルが残っていると二重読み込みになってしまうため、
        // 新しく読み込む前に一旦すべて解放しておく。
        Release();
        // 読み込むJSONファイルのフルパスを組み立てる。
        std::string path = assetsPath + "/animations.json";
        std::ifstream f(path);
        // ファイルが開けない（存在しない等）場合は、これ以上処理を進めずに終了する。
        if (!f.is_open()) return;

        // ファイルの中身をJSONとしてパースする。第3引数にfalseを渡すことで、
        // 例外を投げる代わりにパース失敗時は「discarded」状態のjsonオブジェクトを返してもらう。
        json j = json::parse(f, nullptr, false);
        if (j.is_discarded()) {
            // JSONとして正しく解析できなかった場合はエラーログに記録して終了する。
            Logger::Error("AnimationController", "LoadForAsset", "JSON parse error", path);
            return;
        }

        // animations.jsonの最上位はアセットごとの配列である想定なので、配列でなければ異常とみなす。
        if (!j.is_array()) {
            Logger::Error("AnimationController", "LoadForAsset", "JSON is not an array", path);
            return;
        }

        // 配列の中から、指定されたassetIdに一致する要素を探す。
        for (const auto& aset : j) {
            if (!aset.is_object()) continue; // 配列の要素がオブジェクトでない場合はスキップ
            if (aset.value("assetId", "") != assetId) continue; // assetIdが一致しない要素は無視する
            // 一致する要素が見つかったが、clips配列が無い／配列でない場合はこれ以上読み込めないので中断する。
            if (!aset.contains("clips") || !aset["clips"].is_array()) break;

            // 一致したアセットが持つクリップ（Idle、Walkなど）を1つずつ読み込んでいく。
            for (const auto& cj : aset["clips"]) {
                if (!cj.is_object()) continue; // クリップ要素がオブジェクトでなければスキップ

                AnimationClip clip;
                // 各項目をJSONから取得する。value()の第2引数はキーが存在しない場合のデフォルト値。
                clip.name        = cj.value("name", "Idle");
                std::string spritePath = cj.value("sprite", "");
                clip.frameCountX = cj.value("frameCountX", 1);
                if (clip.frameCountX <= 0) clip.frameCountX = 1; // Prevent div by 0（0除算を防ぐため、0以下なら強制的に1にする）
                clip.frameCountY = cj.value("frameCountY", 1);
                if (clip.frameCountY <= 0) clip.frameCountY = 1; // Prevent div by 0（同上。縦分割数側も0除算防止）
                clip.startFrame  = cj.value("startFrame", 0);
                clip.endFrame    = cj.value("endFrame", 0);
                clip.fps         = cj.value("fps", 8.0f);
                clip.loop        = cj.value("loop", true);

                // スプライト画像のパスが指定されている場合のみ、実際に画像を読み込む。
                if (!spritePath.empty()) {
                    clip.handle = LoadGraph(spritePath.c_str());
                    if (clip.handle >= 0) {
                        // 読み込みに成功したら、画像全体のサイズ（幅・高さ）を取得しておく。
                        // このサイズを分割数で割ることで、1コマ分のサイズを後から計算できる。
                        GetGraphSize(clip.handle, &clip.srcW, &clip.srcH);
                    } else {
                        // 画像の読み込みに失敗した場合はエラーログに残す（ハンドルは-1のままになる）。
                        Logger::Error("AnimationController", "LoadForAsset", "Failed to load sprite graph", spritePath);
                    }
                }
                // 完成したクリップを名前をキーにしてマップへ登録する。
                clipsMap[clip.name] = clip;
            }
            // 目的のアセットの処理が終わったので、以降の配列要素は見なくてよい。
            break;
        }

        // クリップが1つ以上読み込めた場合は、マップの先頭（登録順ではなく名前順で最初）のクリップを
        // デフォルトの再生対象として設定しておく。
        if (!clipsMap.empty()) {
            currentClip = clipsMap.begin()->first;
        }
    }

    // 指定した名前のアニメーションクリップの再生を開始する関数。
    // clipName     : 再生したいクリップの名前
    // forceRestart : trueの場合、既に同じクリップを再生中でも先頭から再生し直す
    void Play(const std::string& clipName, bool forceRestart = false) {
        // 既に同じクリップを再生中で、かつ強制リスタートが指定されていない場合は何もしない（無駄な巻き戻しを防ぐ）。
        if (clipName == currentClip && !forceRestart) return;
        // 指定された名前のクリップが存在しない場合は何もしない。
        if (clipsMap.find(clipName) == clipsMap.end()) return;
        // 再生対象のクリップを切り替え、フレームやタイマー、終了フラグを初期状態に戻す。
        currentClip = clipName;
        currentFrame = clipsMap[clipName].startFrame;
        timer = 0.0f;
        finished = false;
    }

    // 毎フレーム呼び出して、アニメーションの時間経過を進める関数。
    // dt : 前回のUpdate呼び出しからの経過時間（秒）
    void Update(float dt) {
        // 再生中のクリップが無い、またはループしないクリップが既に終了している場合は何もしない。
        if (currentClip.empty() || finished) return;
        auto it = clipsMap.find(currentClip);
        if (it == clipsMap.end()) return; // 再生対象のクリップが見つからない（登録漏れ等）場合は何もしない
        auto& clip = it->second;
        if (clip.fps <= 0.0f) return; // fpsが0以下だと0除算になってしまうため、その場合は進行させない

        // 経過時間をタイマーに積算し、1フレーム分の時間（frameTime）を超えた分だけフレームを進める。
        timer += dt;
        float frameTime = 1.0f / clip.fps;
        while (timer >= frameTime) {
            timer -= frameTime;
            currentFrame++;
            // 終了フレームを超えた場合の処理。
            if (currentFrame > clip.endFrame) {
                if (clip.loop) {
                    // ループ再生の場合は開始フレームに戻す。
                    currentFrame = clip.startFrame;
                } else {
                    // ループしない場合は最後のフレームで止めて、終了フラグを立てる。
                    currentFrame = clip.endFrame;
                    finished = true;
                    break;
                }
            }
        }
    }

    // 現在のアニメーションフレームを画面上の指定座標に描画する関数。
    // cx, cy  : 描画する中心座標
    // scale   : 拡大率
    // angle   : 回転角度（ラジアン）
    // flipX   : trueの場合、左右反転して描画する
    void DrawAt(int cx, int cy, float scale, double angle, bool flipX = false) const {
        // 再生中のクリップが無ければ描画するものが無いので終了。
        if (currentClip.empty()) return;
        auto it = clipsMap.find(currentClip);
        if (it == clipsMap.end()) return;
        const auto& clip = it->second;
        // 画像ハンドルが無効、またはサイズが不正な場合は描画できないので終了。
        if (clip.handle < 0 || clip.srcW <= 0 || clip.srcH <= 0) return;

        // スプライトシート全体のサイズを分割数で割って、1コマ分の幅・高さを求める。
        int fw = clip.srcW / clip.frameCountX;
        int fh = clip.srcH / clip.frameCountY;
        int frame = currentFrame;
        // 現在のフレーム番号から、スプライトシート内での切り出し位置（左上座標）を計算する。
        // 横方向は「フレーム番号 % 横分割数」、縦方向は「フレーム番号 / 横分割数」で求まる。
        int fx = (frame % clip.frameCountX) * fw;
        int fy = (frame / clip.frameCountX) * fh;

        // DxLibの関数で、スプライトシートの一部分だけを切り出して回転・拡大しながら描画する。
        DrawRectRotaGraph(cx, cy, fx, fy, fw, fh, scale, angle, clip.handle, TRUE, flipX ? TRUE : FALSE);
    }

    // 現在再生中のクリップが使っている画像ハンドルを取得する関数。
    // クリップが見つからない場合は-1を返す。
    int GetHandle() const {
        auto it = clipsMap.find(currentClip);
        if (it != clipsMap.end()) return it->second.handle;
        return -1;
    }

    // 現在再生中のクリップの1コマ分の高さ（ピクセル）を取得する関数。
    // 当たり判定のサイズ計算など、アニメーションの見た目の高さが必要な場面で使う。
    int GetCurrentFrameHeight() const {
        if (currentClip.empty()) return 0;
        auto it = clipsMap.find(currentClip);
        if (it == clipsMap.end()) return 0;
        const auto& clip = it->second;
        if (clip.frameCountY <= 0) return 0; // 0除算防止
        return clip.srcH / clip.frameCountY;
    }

    // 指定した名前のクリップが読み込まれているかどうかを調べる関数。
    bool HasClip(const std::string& name) const {
        return clipsMap.find(name) != clipsMap.end();
    }

    // 読み込んだすべてのアニメーションクリップと画像ハンドルを解放し、状態を初期化する関数。
    // 新しいアセットを読み込む前（LoadForAssetの冒頭）や、明示的な後片付けが必要なときに呼ぶ。
    void Release() {
        // マップ内のすべてのクリップについて、画像ハンドルが有効なら解放する。
        for (std::map<std::string, AnimationClip>::iterator it = clipsMap.begin(); it != clipsMap.end(); ++it) {
            if (it->second.handle >= 0) { DeleteGraph(it->second.handle); it->second.handle = -1; }
        }
        // クリップ一覧と再生状態をすべて初期状態に戻す。
        clipsMap.clear();
        currentClip = "";
        currentFrame = 0;
        timer = 0.0f;
        finished = false;
    }

private:
    // クリップ名をキーとして、読み込んだAnimationClipを保持するマップ。
    std::map<std::string, AnimationClip> clipsMap;
};
