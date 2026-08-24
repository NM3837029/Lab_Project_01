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

// 1つのサウンド（BGMまたはSE）の設定情報と、読み込んだ再生用ハンドルをまとめておく構造体。
struct SoundEntry {
    std::string id;       // サウンドを識別するID（PlayBgm/PlaySeで指定するキーになる）
    std::string file;      // 読み込む音声ファイルのパス
    bool isLoop = false;   // ループ再生するかどうか（現状BGMは常にループ再生、この値自体は参照専用の情報）
    int handle = -1;       // LoadSoundMemで読み込んだ音声データのハンドル。-1は未読み込みを意味する
    // Feature: サウンド・アセット管理の刷新 — 0.0〜1.0の音量（Lab_Editor側のSoundDef.volumeと対応）。
    // 再生時にDxLibの0〜255スケールへ変換して ChangeVolumeSoundMem に渡す。
    float volume = 1.0f;
};

// DxLibを使ってBGMと効果音（SE）の読み込み・再生・停止・後片付けを一括管理するクラス。
// シングルトンとして実装されており、SoundManager::Get()で唯一のインスタンスにアクセスする。
class SoundManager {
public:
    // シングルトンインスタンスを取得する関数。
    // static変数として1つだけ生成され、プログラム全体で共有される。
    static SoundManager& Get() {
        static SoundManager instance;
        return instance;
    }

    // 指定したアセットフォルダ配下のbgm.json / se.json / ui_se.jsonを読み込み、
    // BGMマップとSEマップを構築する関数。ステージ読み込み時などに呼び出す想定。
    // assetsPath : bgm.json等が置かれているフォルダのパス
    void LoadFromJson(const std::string& assetsPath) {
        LoadEntries(assetsPath + "/bgm.json", bgmMap);
        LoadEntries(assetsPath + "/se.json",  seMap);
        // UI音（メニュー選択・決定・キャンセル等）はseMapに統合し、PlaySe(id)でそのまま再生可能にする。
        // カテゴリ分けはエディタ側の整理用の概念であり、再生側はSE/UI音を区別しない。
        LoadEntries(assetsPath + "/ui_se.json", seMap);
    }

    // 指定したIDのBGMを再生する関数。
    // id           : 再生したいBGMのID（bgm.jsonに登録されているもの）
    // forceRestart : trueの場合、既に同じBGMを再生中でも最初から再生し直す
    void PlayBgm(const std::string& id, bool forceRestart = false) {
        auto it = bgmMap.find(id);
        // 指定されたIDが見つからない、またはハンドルが無効（読み込み失敗）の場合は何もしない。
        if (it == bgmMap.end() || it->second.handle < 0) return;
        int h = it->second.handle;

        // 既に同じBGMが再生中で、かつ強制リスタートが指定されていない場合は何もしない
        // （毎フレーム呼んでも再生が途切れないようにするための早期リターン）。
        if (currentBgmId == id && CheckSoundMem(h) == 1 && !forceRestart) return;

        // 別のBGMに切り替える、または強制的に再生し直すために、まず現在のBGMを停止する。
        StopBgm();
        currentBgmId = id;
        // JSONで設定された0.0〜1.0の音量値を、DxLibが扱う0〜255のスケールに変換して適用する。
        ChangeVolumeSoundMem((int)(it->second.volume * 255), h);
        // ループ再生モードで再生を開始する。
        PlaySoundMem(h, DX_PLAYTYPE_LOOP, TRUE);
    }

    // 現在再生中のBGMを停止する関数。
    void StopBgm() {
        // 何も再生していない場合は何もしない。
        if (currentBgmId.empty()) return;
        auto it = bgmMap.find(currentBgmId);
        if (it != bgmMap.end() && it->second.handle >= 0)
            StopSoundMem(it->second.handle);
        // 再生中BGMのIDをクリアしておく。
        currentBgmId = "";
    }

    // プレイヤーが使う「ステルスツール」（Mキー）用のミュート切り替え。
    // これがtrueの間は新しく再生しようとしたSEだけが抑制され、BGMには影響しない。
    void SetMuted(bool muted) { isMuted = muted; }
    // 現在ミュート状態かどうかを取得する関数。
    bool IsMuted() const { return isMuted; }

    // 指定したIDの効果音（SE）を1回再生する関数。BGMと違い、同時に何個でも重ねて再生できる。
    // id : 再生したいSEのID（se.jsonまたはui_se.jsonに登録されているもの）
    void PlaySe(const std::string& id) {
        // IDが空、またはミュート中の場合は再生しない。
        if (id.empty() || isMuted) return;
        auto it = seMap.find(id);
        // 指定されたIDが見つからない、またはハンドルが無効な場合は何もしない。
        if (it == seMap.end() || it->second.handle < 0) return;
        // 元のサウンドハンドルを複製する。複製することで、同じSEが重なって再生されても
        // 互いの音量変更や停止タイミングが干渉しないようにしている。
        int dup = DuplicateSoundMem(it->second.handle);
        if (dup >= 0) {
            // 音量は複製後の再生用ハンドル(dup)に適用する（元のit->second.handleに適用すると
            // 以降ロードし直すまで全ての複製に影響してしまうため）
            ChangeVolumeSoundMem((int)(it->second.volume * 255), dup);
            // バックグラウンド再生（他の音と重ねて鳴らせるモード）で1回再生する。
            PlaySoundMem(dup, DX_PLAYTYPE_BACK, TRUE);
            // 再生が終わったら後片付け（DeleteSoundMem）できるよう、一時ハンドルとして記録しておく。
            tempHandles.push_back(dup);
        }
    }

    // 毎フレーム呼び出して、再生が終わったSEの複製ハンドルを解放する関数。
    // これを呼ばないと、PlaySeで複製したハンドルがメモリ上に残り続けてしまう。
    void Update() {
        // 後ろから走査してerase()してもインデックスがずれないようにするため、逆順にループする。
        for (int i = (int)tempHandles.size() - 1; i >= 0; i--) {
            // CheckSoundMemが0（再生中でない）になったハンドルは、再生完了とみなして解放する。
            if (CheckSoundMem(tempHandles[i]) == 0) {
                DeleteSoundMem(tempHandles[i]);
                tempHandles.erase(tempHandles.begin() + i);
            }
        }
    }

    // 読み込んだすべてのBGM・SEのハンドルを解放し、内部状態を初期化する関数。
    // ステージ切り替え時や、アプリケーション終了時の後片付けに使う。
    void Release() {
        // まず再生中のBGMを止めてから解放処理に入る。
        StopBgm();
        // BGMマップに登録されているすべてのハンドルを解放する。
        for (std::map<std::string, SoundEntry>::iterator it = bgmMap.begin(); it != bgmMap.end(); ++it) {
            if (it->second.handle >= 0) { DeleteSoundMem(it->second.handle); it->second.handle = -1; }
        }
        // SEマップに登録されているすべてのハンドルを解放する。
        for (std::map<std::string, SoundEntry>::iterator it = seMap.begin(); it != seMap.end(); ++it) {
            if (it->second.handle >= 0) { DeleteSoundMem(it->second.handle); it->second.handle = -1; }
        }
        // PlaySeで複製された、再生完了待ちの一時ハンドルもすべて解放する。
        for (size_t i = 0; i < tempHandles.size(); i++) {
            DeleteSoundMem(tempHandles[i]);
        }
        // すべてのコンテナと状態を初期状態に戻す。
        tempHandles.clear();
        bgmMap.clear();
        seMap.clear();
        currentBgmId = "";
    }

    // 指定したIDのBGMが読み込み済み（再生可能）かどうかを調べる関数。
    bool HasBgm(const std::string& id) const {
        auto it = bgmMap.find(id);
        return it != bgmMap.end() && it->second.handle >= 0;
    }
    // 指定したIDのSEが読み込み済み（再生可能）かどうかを調べる関数。
    bool HasSe(const std::string& id) const {
        auto it = seMap.find(id);
        return it != seMap.end() && it->second.handle >= 0;
    }

private:
    std::map<std::string, SoundEntry> bgmMap; // BGMのID→設定・ハンドルのマップ
    std::map<std::string, SoundEntry> seMap;  // SE（UI音含む）のID→設定・ハンドルのマップ
    std::vector<int> tempHandles;             // PlaySeで複製した、再生完了待ちの一時ハンドル一覧
    std::string currentBgmId;                 // 現在再生中のBGMのID（何も再生していなければ空文字）
    bool isMuted = false;                     // SEのミュート状態（trueならPlaySeが無視される）

    // シングルトンにするため、コンストラクタ・デストラクタをprivateにし、
    // コピーコンストラクタ・代入演算子を削除して複製できないようにしている。
    SoundManager() = default;
    // デストラクタでは、確保したサウンドリソースを確実に解放するためにRelease()を呼ぶ。
    ~SoundManager() { Release(); }
    SoundManager(const SoundManager&) = delete;
    SoundManager& operator=(const SoundManager&) = delete;

    // 指定したJSONファイルからサウンド定義を読み込み、targetマップに登録する共通処理。
    // BGM/SE/UI音のいずれの読み込みにも使われる。
    // path   : 読み込むJSONファイルのパス
    // target : 読み込んだSoundEntryを登録する先のマップ（bgmMapまたはseMap）
    void LoadEntries(const std::string& path, std::map<std::string, SoundEntry>& target) {
        std::ifstream f(path);
        // ファイルが開けない（存在しない等）場合は、これ以上処理を進めずに終了する。
        if (!f.is_open()) return;

        // ファイルの中身をJSONとしてパースする。第3引数にfalseを渡すことで、
        // 例外を投げる代わりにパース失敗時は「discarded」状態のjsonオブジェクトを返してもらう。
        json j = json::parse(f, nullptr, false);
        if (j.is_discarded()) {
            // JSONとして正しく解析できなかった場合はエラーログに記録して終了する。
            Logger::Error("SoundManager", "LoadEntries", "JSON parse error", path);
            return;
        }

        // このJSONファイルの最上位はサウンド定義の配列である想定なので、配列でなければ異常とみなす。
        if (!j.is_array()) {
            Logger::Error("SoundManager", "LoadEntries", "JSON is not an array", path);
            return;
        }

        // 配列の要素を1つずつサウンドエントリとして読み込んでいく。
        for (const auto& e : j) {
            if (!e.is_object()) continue; // 要素がオブジェクトでなければスキップ

            SoundEntry entry;
            // 各項目をJSONから取得する。value()の第2引数はキーが存在しない場合のデフォルト値。
            entry.id   = e.value("id", "");
            entry.file = e.value("file", "");
            entry.isLoop = e.value("isLoop", false);
            entry.volume = e.value("volume", 1.0f);
            // 音量は0.0〜1.0の範囲に収まるよう、範囲外の値をクランプ（丸め込み）する。
            if (entry.volume < 0.0f) entry.volume = 0.0f;
            if (entry.volume > 1.0f) entry.volume = 1.0f;

            // IDとファイルパスの両方が指定されている場合のみ、実際に音声ファイルを読み込む。
            if (!entry.id.empty() && !entry.file.empty()) {
                entry.handle = LoadSoundMem(entry.file.c_str());
                if (entry.handle < 0) {
                    // 読み込みに失敗した場合はエラーログに記録し、マップには登録しない
                    // （HasBgm/HasSeがfalseを返すようにするため）。
                    Logger::Error("SoundManager", "LoadEntries", "Failed to load sound file", entry.file);
                } else {
                    // 読み込みに成功した場合のみ、IDをキーにしてマップへ登録する。
                    target[entry.id] = entry;
                }
            }
        }
    }
};
