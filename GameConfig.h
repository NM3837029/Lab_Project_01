#pragma once
#include <string>
#include <vector>
#include <map>
#include <fstream>
#include <algorithm>  // stable_sort（ステージをorder順に並べ替えるのに使う）
#include <windows.h>
#include "json.hpp"
#include "Logger.h"
#include "GamePaths.h"

// ゲーム全体の設定（assets/game_config.json）と、進行状況のセーブデータを扱う。
//
// 【なぜ必要か】
// これまでこのゲームには「ゲーム全体の設定」を持つ場所が無く、タイトル文字も
// 初期ステージ名もC++のソースに直書きされていた。ステージ一覧に至っては
// マニフェスト自体が存在せず、ステージJSONに表示名のキーすら無かった。
//
// 【なぜステージJSONではなく別ファイルなのか】
// Lab_Editor の StageData.SaveToFile はステージJSONを毎回ゼロから組み立て直すため、
// ステージJSONに表示名やサムネイルを書き足しても、エディタで一度保存した瞬間に消える。
// メタ情報は必ずこの game_config.json 側に置くこと。
//
// 【なぜ AssetDefinitions（enemies.json等の一括読み書き）に混ぜないのか】
// エディタの AssetDefinitions.SaveToFolder は呼ばれるたびに9本のJSONを全部書き直す。
// そこに混ぜると「敵の当たり判定を直そうとしてアセット管理を保存したら、
// タイトル画面のレイアウトも書き直された」が起きる。独立させておく。
namespace GameCfg {

    using json = nlohmann::json;

    // ------------------------------------------------------------
    // 座標系について
    //
    // 全ての座標・サイズは「ゲーム内部解像度 640x480」で持つ。
    // 実行時は等方1.5倍して 960x720 とし、左右に160pxずつ余白を置いて
    // 1280x720 のウィンドウ中央へ描く（背景画像だけは全面に敷くので余白は見えない）。
    // エディタの配置キャンバスも 640x480 なので、そのまま見たとおりの位置になる。
    // ------------------------------------------------------------
    const int   DESIGN_W = 640;
    const int   DESIGN_H = 480;
    const float DESIGN_SCALE = 1.5f;          // min(1280/640, 720/480)
    const int   DESIGN_OX = (1280 - 640 * 3 / 2) / 2; // = 160
    const int   DESIGN_OY = 0;                // 480*1.5 = 720 でぴったり収まる

    // デザイン座標（640x480）をウィンドウ座標（1280x720）へ変換する
    inline int ToScreenX(float x) { return DESIGN_OX + (int)(x * DESIGN_SCALE); }
    inline int ToScreenY(float y) { return DESIGN_OY + (int)(y * DESIGN_SCALE); }
    inline int ToScreenLen(float v) { return (int)(v * DESIGN_SCALE); }

    struct Color { int r = 0, g = 0, b = 0; };

    // タイトル画面に置く要素1つぶん。
    // key は "logo" / "title" / "subtitle" / "menu" の固定4種。
    // 配列にしてあるのは将来的に自由配置へ広げられるようにするためだが、
    // 今はキーで引くだけなので、エディタ側も「要素の追加/削除」を作らなくてよい。
    struct Element {
        std::string key;
        std::string type = "text";   // "text" | "image" | "menu"
        bool visible = true;
        std::string text;            // type=="text"
        std::string image;           // type=="image"
        float x = 0.0f, y = 0.0f;    // align=="center" のとき x は中心座標
        float w = 0.0f, h = 0.0f;
        int fontSize = 20;
        std::string align = "center"; // "center" | "left"
        std::string colorRole = "ink"; // "ink" | "sub" | "accent"
        bool edge = true;            // 文字に縁取りを付けるか（背景画像の上でも読めるように）
        // type=="menu" のときだけ使う
        float itemH = 48.0f;
        float gap = 12.0f;
    };

    struct MenuItem {
        std::string label;
        std::string action; // "stage_select" | "continue" | "quit"
    };

    struct Grid {
        float x = 70.0f, y = 100.0f;
        int cols = 3;
        float cellW = 160.0f, cellH = 120.0f;
        float gapX = 18.0f, gapY = 18.0f;
    };

    struct StageEntry {
        std::string file;        // assets/stages/ 配下のファイル名。セーブのキーでもある
        std::string name;        // セレクト画面に出す表示名
        std::string thumbnail;   // 省略可。無ければ色ブロック＋名前で代替する
        int order = 0;
        std::string unlock = "always"; // "always" | "prev_clear" | "require"
        std::vector<std::string> require; // unlock=="require" のとき、クリアが必要なファイル名
        int itemTotal = 0;       // そのステージのアイテム総数（エディタが自動計算して書く）
    };

    // ------------------------------------------------------------
    // 効果音の割り当て
    //
    // これまでプレイヤーの操作音・編集ツールの音・UIの音は、すべて
    // DrawPixel.cpp の中に "jump" / "ui_pause" といったリテラルで直書きされていた。
    // そのためサウンド割り当て画面から触れず、音を変えるにはコードを書き換えるしかなかった。
    // ここへ出しておくと、Lab_Editor の「サウンド割り当て」から設定できるようになる。
    //
    // 値は効果音の「id」（se.json / ui_se.json の id）であって、ファイル名ではない。
    // 空文字は「鳴らさない」を意味する（SoundManager::PlaySe が空idを無視する）。

    // プレイヤーの操作に紐づく効果音。
    struct PlayerSe {
        std::string jump   = "se_jump";  // ジャンプした瞬間
        std::string land   = "";          // 着地した瞬間
        std::string dash   = "";          // ダッシュを始めた瞬間
        std::string shoot  = "se_shoot"; // 弾を撃った瞬間
        std::string damage = "se_hit";   // ダメージを受けた瞬間
        std::string death  = "";          // 力尽きた瞬間（ゲームオーバーへ移る）
    };

    // ゲーム内編集ツールの操作音。
    //
    // 従来は「失敗したときだけ ui_denied が鳴り、成功したときは無音」という
    // 一貫性の無い状態だった。成功側にも音を割り当てられるようにする。
    struct EditSe {
        std::string rewind    = "ui_rewind";     // 巻き戻し中
        std::string scale     = "ui_edit_apply"; // 拡大縮小を確定した
        std::string rotate    = "ui_edit_apply"; // 回転を確定した
        std::string move      = "ui_edit_apply"; // 移動を確定した
        std::string flip      = "ui_edit_apply"; // 向きを反転した
        std::string reset     = "ui_cancel";     // 編集をリセットした
        std::string step      = "ui_cursor";     // コマ送りした
        std::string pause      = "ui_pause";       // 一時停止/解除
        std::string fastForward = "ui_fastforward"; // 早送りの切替
        std::string colorFilter = "ui_color_cycle"; // 色フィルタの切替
        std::string cut         = "ui_fastforward"; // タイムラインのカット確定
        std::string denied    = "ui_denied";     // 操作が拒否された
        std::string costEmpty = "ui_denied";     // 編集ゲージを使い切った
    };

    // タイトル・セレクト・リザルトなど、ゲーム外の画面で鳴る音。
    struct MetaSe {
        std::string cursor   = "ui_cursor"; // メニューのカーソル移動
        std::string decide   = "ui_decide"; // 決定
        std::string cancel   = "ui_cancel"; // 戻る・キャンセル
        std::string clear    = "";           // ステージクリア
        std::string gameover = "";           // ゲームオーバー
    };

    struct GameConfig {
        int version = 1;
        std::string windowTitle = "Lab Project 01";
        // false なら従来どおり起動即プレイになる（何かあったときの緊急回避用）
        bool titleEnabled = true;

        Color ink{ 32, 34, 40 };
        Color inkSub{ 112, 116, 126 };
        Color inkAccent{ 20, 84, 132 };
        Color backdrop{ 246, 240, 228 };

        // タイトル画面
        std::string titleBg, titleBgm;
        std::vector<Element> titleElements;
        std::vector<MenuItem> menuItems;

        // ステージセレクト画面
        std::string selectBg, selectBgm;
        std::string heading = "STAGE SELECT";
        float headingX = 320.0f, headingY = 40.0f;
        int headingFontSize = 32;
        Grid grid;
        std::string backLabel = "BACK";
        std::string lockedLabel = "? ? ?";

        std::vector<StageEntry> stages;

        // リザルト画面
        std::string nextLabel = "NEXT STAGE";
        std::string retryLabel = "RETRY";
        std::string selectLabel = "STAGE SELECT";
        std::string victoryText = "VICTORY!";
        std::string gameoverText = "GAME OVER";

        // 効果音の割り当て（詳細は上の各構造体のコメント参照）
        PlayerSe playerSe;
        EditSe   editSe;
        MetaSe   metaSe;

        // key に一致する要素を探す。無ければ nullptr。
        const Element* FindElement(const std::string& key) const {
            for (const auto& e : titleElements) if (e.key == key) return &e;
            return nullptr;
        }
    };

    // ------------------------------------------------------------
    // セーブデータ
    //
    // キーは必ずステージのファイル名にすること。
    // stages ベクタのインデックスを使ってはいけない —— DrawPixel.cpp は
    // JSON読み込みより前にハードコードのステージを2本 push_back しており、
    // 添字と game_config.json の並びが一致しないため。
    // ------------------------------------------------------------
    struct StageRecord {
        bool cleared = false;
        int items = 0;   // そのステージで取った最高数（再挑戦で減らない）
        int plays = 0;
    };

    struct SaveData {
        int version = 1;
        std::string lastPlayed;
        std::map<std::string, StageRecord> stages;

        bool IsCleared(const std::string& file) const {
            auto it = stages.find(file);
            return it != stages.end() && it->second.cleared;
        }
        int BestItems(const std::string& file) const {
            auto it = stages.find(file);
            return it != stages.end() ? it->second.items : 0;
        }
    };

    // ------------------------------------------------------------
    // 読み込み
    // ------------------------------------------------------------

    inline Color ReadColor(const json& j, const char* key, Color fallback) {
        if (!j.contains(key) || !j[key].is_array() || j[key].size() < 3) return fallback;
        Color c;
        c.r = j[key][0].get<int>();
        c.g = j[key][1].get<int>();
        c.b = j[key][2].get<int>();
        return c;
    }

    // assets/game_config.json を読む。
    // ファイルが無い・壊れている場合は false を返し、out は既定値のまま
    // titleEnabled=false になる（＝設定が壊れても従来どおりプレイできる）。
    inline bool LoadGameConfig(const std::string& path, GameConfig& out) {
        out = GameConfig();

        std::ifstream f(path);
        if (!f.is_open()) {
            out.titleEnabled = false;
            Logger::Info("GameCfg", "LoadGameConfig", "game_config.json not found; title screen disabled");
            return false;
        }

        // 例外を投げない解析。壊れたJSONでゲームごと落とさないため
        json j = json::parse(f, nullptr, false);
        if (j.is_discarded() || !j.is_object()) {
            out.titleEnabled = false;
            Logger::Error("GameCfg", "LoadGameConfig", "game_config.json is broken; title screen disabled", path);
            return false;
        }

        out.version = j.value("version", 1);
        out.windowTitle = j.value("window_title", out.windowTitle);
        out.titleEnabled = j.value("title_enabled", true);

        if (j.contains("theme") && j["theme"].is_object()) {
            const json& t = j["theme"];
            out.ink = ReadColor(t, "ink", out.ink);
            out.inkSub = ReadColor(t, "ink_sub", out.inkSub);
            out.inkAccent = ReadColor(t, "ink_accent", out.inkAccent);
            out.backdrop = ReadColor(t, "backdrop", out.backdrop);
        }

        if (j.contains("title_screen") && j["title_screen"].is_object()) {
            const json& ts = j["title_screen"];
            out.titleBg = ts.value("background_image", "");
            out.titleBgm = ts.value("bgm_id", "");
            if (ts.contains("elements") && ts["elements"].is_array()) {
                for (const auto& ej : ts["elements"]) {
                    Element e;
                    e.key = ej.value("key", "");
                    e.type = ej.value("type", "text");
                    e.visible = ej.value("visible", true);
                    e.text = ej.value("text", "");
                    e.image = ej.value("image", "");
                    e.x = ej.value("x", 0.0f);
                    e.y = ej.value("y", 0.0f);
                    e.w = ej.value("w", 0.0f);
                    e.h = ej.value("h", 0.0f);
                    e.fontSize = ej.value("font_size", 20);
                    e.align = ej.value("align", "center");
                    e.colorRole = ej.value("color_role", "ink");
                    e.edge = ej.value("edge", true);
                    e.itemH = ej.value("item_h", 48.0f);
                    e.gap = ej.value("gap", 12.0f);
                    out.titleElements.push_back(e);
                }
            }
            if (ts.contains("menu_items") && ts["menu_items"].is_array()) {
                for (const auto& mj : ts["menu_items"]) {
                    MenuItem m;
                    m.label = mj.value("label", "");
                    m.action = mj.value("action", "stage_select");
                    out.menuItems.push_back(m);
                }
            }
        }

        if (j.contains("stage_select") && j["stage_select"].is_object()) {
            const json& ss = j["stage_select"];
            out.selectBg = ss.value("background_image", "");
            out.selectBgm = ss.value("bgm_id", "");
            out.heading = ss.value("heading", out.heading);
            out.headingX = ss.value("heading_x", out.headingX);
            out.headingY = ss.value("heading_y", out.headingY);
            out.headingFontSize = ss.value("heading_font_size", out.headingFontSize);
            out.backLabel = ss.value("back_label", out.backLabel);
            out.lockedLabel = ss.value("locked_label", out.lockedLabel);
            if (ss.contains("grid") && ss["grid"].is_object()) {
                const json& g = ss["grid"];
                out.grid.x = g.value("x", out.grid.x);
                out.grid.y = g.value("y", out.grid.y);
                out.grid.cols = g.value("cols", out.grid.cols);
                out.grid.cellW = g.value("cell_w", out.grid.cellW);
                out.grid.cellH = g.value("cell_h", out.grid.cellH);
                out.grid.gapX = g.value("gap_x", out.grid.gapX);
                out.grid.gapY = g.value("gap_y", out.grid.gapY);
            }
        }

        if (j.contains("stages") && j["stages"].is_array()) {
            for (const auto& sj : j["stages"]) {
                StageEntry s;
                s.file = sj.value("file", "");
                if (s.file.empty()) continue; // ファイル名の無いエントリは意味を持たない
                s.name = sj.value("name", s.file);
                s.thumbnail = sj.value("thumbnail", "");
                s.order = sj.value("order", (int)out.stages.size());
                s.unlock = sj.value("unlock", "always");
                s.itemTotal = sj.value("item_total", 0);
                if (sj.contains("require") && sj["require"].is_array()) {
                    for (const auto& rj : sj["require"]) {
                        if (rj.is_string()) s.require.push_back(rj.get<std::string>());
                    }
                }
                out.stages.push_back(s);
            }
            // order の昇順に並べ替える。JSON の記述順に依存させると、
            // 手で編集したときに prev_clear の判定が不定になる。
            std::stable_sort(out.stages.begin(), out.stages.end(),
                [](const StageEntry& a, const StageEntry& b) { return a.order < b.order; });
        }

        if (j.contains("result") && j["result"].is_object()) {
            const json& r = j["result"];
            out.nextLabel = r.value("next_label", out.nextLabel);
            out.retryLabel = r.value("retry_label", out.retryLabel);
            out.selectLabel = r.value("select_label", out.selectLabel);
            out.victoryText = r.value("victory_text", out.victoryText);
            out.gameoverText = r.value("gameover_text", out.gameoverText);
        }

        // 効果音の割り当て。キーが無ければ構造体の既定値（＝従来と同じ音）がそのまま残る。
        if (j.contains("player_se") && j["player_se"].is_object()) {
            const json& s = j["player_se"];
            out.playerSe.jump   = s.value("jump",   out.playerSe.jump);
            out.playerSe.land   = s.value("land",   out.playerSe.land);
            out.playerSe.dash   = s.value("dash",   out.playerSe.dash);
            out.playerSe.shoot  = s.value("shoot",  out.playerSe.shoot);
            out.playerSe.damage = s.value("damage", out.playerSe.damage);
            out.playerSe.death  = s.value("death",  out.playerSe.death);
        }
        if (j.contains("edit_se") && j["edit_se"].is_object()) {
            const json& s = j["edit_se"];
            out.editSe.rewind    = s.value("rewind",     out.editSe.rewind);
            out.editSe.scale     = s.value("scale",      out.editSe.scale);
            out.editSe.rotate    = s.value("rotate",     out.editSe.rotate);
            out.editSe.move      = s.value("move",       out.editSe.move);
            out.editSe.flip      = s.value("flip",       out.editSe.flip);
            out.editSe.reset     = s.value("reset",      out.editSe.reset);
            out.editSe.step      = s.value("step",       out.editSe.step);
            out.editSe.pause       = s.value("pause",        out.editSe.pause);
            out.editSe.fastForward = s.value("fast_forward", out.editSe.fastForward);
            out.editSe.colorFilter = s.value("color_filter", out.editSe.colorFilter);
            out.editSe.cut         = s.value("cut",          out.editSe.cut);
            out.editSe.denied    = s.value("denied",     out.editSe.denied);
            out.editSe.costEmpty = s.value("cost_empty", out.editSe.costEmpty);
        }
        if (j.contains("meta_se") && j["meta_se"].is_object()) {
            const json& s = j["meta_se"];
            out.metaSe.cursor   = s.value("cursor",   out.metaSe.cursor);
            out.metaSe.decide   = s.value("decide",   out.metaSe.decide);
            out.metaSe.cancel   = s.value("cancel",   out.metaSe.cancel);
            out.metaSe.clear    = s.value("clear",    out.metaSe.clear);
            out.metaSe.gameover = s.value("gameover", out.metaSe.gameover);
        }

        Logger::Info("GameCfg", "LoadGameConfig",
            "loaded: stages=" + std::to_string(out.stages.size()) +
            " elements=" + std::to_string(out.titleElements.size()) +
            " menu=" + std::to_string(out.menuItems.size()));
        return true;
    }

    // ------------------------------------------------------------
    // セーブデータの読み書き
    //
    // 置き場所は %LOCALAPPDATA%\LabProject01\save.json。
    // インストール先（Program Files配下など）へ書きに行くと権限エラーで
    // 黙って失敗するため、ユーザーごとの書き込み可能フォルダへ置く。
    // ------------------------------------------------------------

    inline std::wstring SaveFilePath() {
        std::wstring dir = GamePaths::UserDataDir();
        if (dir.empty()) return L"";
        return dir + L"\\save.json";
    }

    inline bool LoadSaveData(SaveData& out) {
        out = SaveData();
        std::wstring path = SaveFilePath();
        if (path.empty()) return false;

        std::ifstream f(path.c_str());
        if (!f.is_open()) return false; // 初回起動。エラーではない

        json j = json::parse(f, nullptr, false);
        f.close();

        bool broken = (j.is_discarded() || !j.is_object() || j.value("version", 0) != 1);
        if (broken) {
            // 壊れていても消さない。プレイヤーの進行を失わせるうえ、原因調査もできなくなる。
            // 日時付きの名前へ退避してから、空のセーブで続行する。
            SYSTEMTIME st; GetLocalTime(&st);
            wchar_t stamp[64];
            swprintf_s(stamp, L".corrupt.%04d%02d%02d_%02d%02d%02d.json",
                st.wYear, st.wMonth, st.wDay, st.wHour, st.wMinute, st.wSecond);
            std::wstring backup = path.substr(0, path.size() - 5) + stamp; // ".json" を除いて付け替える
            MoveFileW(path.c_str(), backup.c_str());
            Logger::Error("GameCfg", "LoadSaveData", "save.json was broken; renamed and started fresh");
            return false;
        }

        out.version = j.value("version", 1);
        out.lastPlayed = j.value("last_played", "");
        if (j.contains("stages") && j["stages"].is_object()) {
            for (auto it = j["stages"].begin(); it != j["stages"].end(); ++it) {
                StageRecord r;
                r.cleared = it.value().value("cleared", false);
                r.items = it.value().value("items", 0);
                r.plays = it.value().value("plays", 0);
                out.stages[it.key()] = r;
            }
        }
        return true;
    }

    inline bool WriteSaveData(const SaveData& data) {
        std::wstring path = SaveFilePath();
        if (path.empty()) return false;

        json j;
        j["version"] = 1;
        j["last_played"] = data.lastPlayed;
        json sj = json::object();
        for (const auto& kv : data.stages) {
            sj[kv.first] = { {"cleared", kv.second.cleared},
                             {"items",   kv.second.items},
                             {"plays",   kv.second.plays} };
        }
        j["stages"] = sj;

        // 一時ファイルへ書いてから差し替える。
        // 直接上書きすると、書き込み途中で電源が落ちたときにセーブが壊れる。
        std::wstring tmp = path + L".tmp";
        {
            std::ofstream f(tmp.c_str(), std::ios::trunc);
            if (!f.is_open()) {
                Logger::Error("GameCfg", "WriteSaveData", "could not open the temporary save file");
                return false;
            }
            f << j.dump(2);
        }
        if (!MoveFileExW(tmp.c_str(), path.c_str(), MOVEFILE_REPLACE_EXISTING)) {
            Logger::Error("GameCfg", "WriteSaveData", "could not replace the save file");
            return false;
        }
        return true;
    }

    // クリアを記録する。アイテム数は最高記録だけを残す（再挑戦で減らない）。
    inline void RecordClear(SaveData& data, const std::string& file, int itemsGot) {
        if (file.empty()) return;
        StageRecord& r = data.stages[file];
        r.cleared = true;
        if (itemsGot > r.items) r.items = itemsGot;
        r.plays += 1;
        data.lastPlayed = file;
    }

    // このステージが選べる状態かどうか。
    //   always     … 常に選べる
    //   prev_clear … 1つ前（order順）のステージをクリア済みなら選べる
    //   require    … require[] に挙げた全ステージをクリア済みなら選べる
    inline bool IsStageUnlocked(const GameConfig& cfg, const SaveData& save, size_t idx) {
        if (idx >= cfg.stages.size()) return false;
        const StageEntry& s = cfg.stages[idx];

        if (s.unlock == "prev_clear") {
            if (idx == 0) return true; // 先頭は前が無いので常に選べる
            return save.IsCleared(cfg.stages[idx - 1].file);
        }
        if (s.unlock == "require") {
            for (const auto& need : s.require) {
                if (!save.IsCleared(need)) return false;
            }
            return true;
        }
        return true; // "always" と未知の値
    }

} // namespace GameCfg
