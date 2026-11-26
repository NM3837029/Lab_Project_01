#pragma once
// ======================================================================
// EditPanel.h - ゲーム画面の右パネル（インスペクター）の部品
//
// 【なぜ作ったか】
// 左のアイコンバー（EditToolbar.h）は「暗い平らな下地・青い枠で“いま効いている”を示す・小さなアイコン」に
// 作り替えたが、右のインスペクターだけは、クリーム色の窓枠に文字を並べただけの昔のままで、
// 画面の左右で見た目がちぐはぐだった。さらに
//   ・どの行が操作できて、どの行がドラッグで動かせるのか、文字だけでは分からない
//   ・「反応」の行は、タグが増えるとパネルの幅で「…」と切れていた
// という不満もあった。
// そこで、左バーと同じ配色・同じ「カード」の見た目に揃え、行ごとにアイコンを付けた。
//
// 【この部品の役割の範囲】
// ここには「カードを描く・アイコンを描く・チップを描く」といった見た目の部品だけを置く。
// どの行がいつ操作できるか、ドラッグでどの値が動くか、という判断は今までどおり DrawPixel.cpp にある。
// 行の位置と当たり判定は、DrawPixel.cpp の Insp 名前空間の表1か所で決める（描画もクリック判定も同じ値を見る）。
//
// アイコンは EditToolbar と同じく、コードで描く（画像素材を増やさない）。
// DxLib を include したあとで include すること。
// ======================================================================

#include "UiFont.h"
#include <algorithm>
#include <cmath>
#include <cstring>
#include <string>

namespace EditPanel {

// 矩形（左上と右下。右下も含む）
struct Box { int x1, y1, x2, y2; };

inline bool HitBox(const Box& b, int x, int y) { return x >= b.x1 && x <= b.x2 && y >= b.y1 && y <= b.y2; }

// ---- 左バーと同じ配色 ----
// 色は関数にして、呼ぶたびに GetColor で作る（DxLib の初期化前に色を作らないため）。
inline unsigned int ColBack()        { return GetColor(46, 42, 40); }     // パネルの下地（左バーと同じ）
inline unsigned int ColEdge()        { return GetColor(84, 120, 176); }   // モニター側の縁（枠の青と同系色）
inline unsigned int ColDivider()     { return GetColor(78, 76, 86); }     // 区切り線
inline unsigned int ColInk()         { return GetColor(238, 240, 250); }  // 主要な文字
inline unsigned int ColInkSub()      { return GetColor(178, 186, 210); }  // 補助の文字・使えない行
inline unsigned int ColInkAccent()   { return GetColor(160, 220, 255); }  // 強調（いまつかんでいる項目など）
inline unsigned int ColInkOk()       { return GetColor(143, 224, 160); }  // 有効・正常を示す緑
inline unsigned int ColInkWarn()     { return GetColor(255, 200, 80); }   // 注意（停止中・巻き戻し中など）
inline unsigned int ColGroove()      { return GetColor(60, 62, 80); }     // ボタン・スライダーの溝の下地

// 文字の横幅（指定の大きさで測る）
inline int TextWidth(const char* s, int size) {
    return UiFont::Width(s, size);
}

// 指定の大きさで文字を描く（描いたあと、元の大きさへ戻す）
inline void DrawText(int x, int y, const char* s, unsigned int col, int size) {
    UiFont::Draw(x, y, s, col, size);
}

// 幅 maxW に収まるまで末尾を削り、削ったときは「…」を付ける（UTF-8 の文字の途中では切らない）
inline std::string FitText(const std::string& src, int maxW, int size) {
    std::string s = src;
    auto W = [&](const std::string& t) { return TextWidth(t.c_str(), size); };
    if (W(s) <= maxW) return s;
    while (s.size() > 1) {
        size_t cut = s.size() - 1;
        while (cut > 0 && ((unsigned char)s[cut] & 0xC0) == 0x80) cut--;
        s.resize(cut);
        if (W(s + "…") <= maxW) break;
    }
    return s + "…";
}

// ---- カード（1行ぶんのボタン）----
//   active … いまつかんでいる／効いている（青く光る）   hover … マウスが重なっている   usable … この対象で操作できる行
// 見た目は左バーの DrawButton と同じ：平らな下地、上半分の薄いハイライト、1pxの枠。
inline void DrawCard(const Box& b, bool active, bool hover, bool usable) {
    unsigned int back = !usable ? GetColor(50, 50, 56) : active ? GetColor(28, 64, 118) : hover ? GetColor(84, 94, 128) : ColGroove();
    unsigned int edge = !usable ? GetColor(84, 84, 92) : active ? GetColor(120, 190, 255) : hover ? GetColor(200, 214, 245) : GetColor(124, 134, 168);
    DrawBox(b.x1, b.y1, b.x2, b.y2, back, TRUE);
    SetDrawBlendMode(DX_BLENDMODE_ALPHA, usable ? 30 : 12);                       // 上半分を少し明るくして、ボタンらしい厚みを出す
    DrawBox(b.x1 + 1, b.y1 + 1, b.x2 - 1, (b.y1 + b.y2) / 2, GetColor(255, 255, 255), TRUE);
    SetDrawBlendMode(DX_BLENDMODE_NOBLEND, 0);
    DrawBox(b.x1, b.y1, b.x2, b.y2, edge, FALSE);
}

// ---- アイコン（コードで描く）----
// ICON_FAST 以降は、右クリックの扇形メニューで使う
// ICON_GRID 以降は、ポーズ画面（システムメニュー）で使う
// ICON_LAYER は、右クリックの扇形メニューの「レイヤー」。既存の番号がずれないよう、末尾に足している
enum Icon { ICON_SCALE, ICON_ANGLE, ICON_SPEED, ICON_PAUSE, ICON_PLAY, ICON_REWIND, ICON_MOVE,
            ICON_FAST, ICON_SLOW, ICON_FLIP, ICON_RESET, ICON_CUTDEL,
            ICON_GRID, ICON_KEYS, ICON_POWER, ICON_LAYER, ICON_LOCK };

// 円弧を細い線の連なりで描く。角度は度で、数学の向き（上が正）。
inline void DrawArc(float cx, float cy, float r, float a0, float a1, unsigned int col, float th) {
    const float kPi = 3.14159265f;
    const float step = 12.0f;
    for (float a = a0; a < a1; a += step) {
        float b = (a + step < a1) ? a + step : a1;
        DrawLineAA(cx + cosf(a * kPi / 180.0f) * r, cy - sinf(a * kPi / 180.0f) * r,
                   cx + cosf(b * kPi / 180.0f) * r, cy - sinf(b * kPi / 180.0f) * r, col, th);
    }
}

// アイコン1つ。cx,cy は中心、おおよそ 18px 四方に収まる。
inline void DrawIcon(Icon k, int cx, int cy, unsigned int col) {
    switch (k) {
        case ICON_SCALE: {
            // 小さな四角から、右下へ広がる矢印（大きさ）
            DrawBox(cx - 8, cy - 8, cx + 1, cy + 1, col, FALSE);
            DrawBox(cx - 8, cy - 8, cx - 4, cy - 4, col, TRUE);
            DrawLine(cx, cy, cx + 8, cy + 8, col, 2);
            DrawLine(cx + 8, cy + 8, cx + 8, cy + 2, col, 2);
            DrawLine(cx + 8, cy + 8, cx + 2, cy + 8, col, 2);
            break;
        }
        case ICON_ANGLE: {
            // 回転の矢印（角度）
            DrawArc((float)cx, (float)cy, 7.0f, 40.0f, 320.0f, col, 2.0f);
            float ex = cx + cosf(40.0f * 3.14159265f / 180.0f) * 7.0f, ey = cy - sinf(40.0f * 3.14159265f / 180.0f) * 7.0f;
            DrawLine((int)ex, (int)ey, (int)ex + 5, (int)ey - 1, col, 2);
            DrawLine((int)ex, (int)ey, (int)ex + 1, (int)ey + 5, col, 2);
            DrawCircle(cx, cy, 1, col, TRUE);
            break;
        }
        case ICON_SPEED: {
            // 半円の計器と針（速さ）
            DrawArc((float)cx, (float)cy + 4.0f, 8.0f, 10.0f, 170.0f, col, 2.0f);
            DrawLine(cx, cy + 4, cx + 4, cy - 3, col, 2);
            DrawLine(cx - 8, cy + 6, cx + 8, cy + 6, col, 1);
            break;
        }
        case ICON_PAUSE: {
            DrawBox(cx - 6, cy - 7, cx - 2, cy + 7, col, TRUE);
            DrawBox(cx + 2, cy - 7, cx + 6, cy + 7, col, TRUE);
            break;
        }
        case ICON_PLAY: {
            DrawTriangle(cx - 5, cy - 7, cx - 5, cy + 7, cx + 7, cy, col, TRUE);
            break;
        }
        case ICON_REWIND: {
            DrawTriangle(cx + 1, cy - 6, cx + 1, cy + 6, cx - 7, cy, col, TRUE);
            DrawTriangle(cx + 8, cy - 6, cx + 8, cy + 6, cx, cy, col, TRUE);
            break;
        }
        case ICON_FAST: {
            // 右向きの三角を2つ（加速）
            DrawTriangle(cx - 8, cy - 6, cx - 8, cy + 6, cx - 1, cy, col, TRUE);
            DrawTriangle(cx, cy - 6, cx, cy + 6, cx + 7, cy, col, TRUE);
            break;
        }
        case ICON_SLOW: {
            // 右向きの三角を1つと、手前で止まる縦の棒（減速）
            DrawTriangle(cx - 7, cy - 6, cx - 7, cy + 6, cx + 1, cy, col, TRUE);
            DrawBox(cx + 4, cy - 6, cx + 6, cy + 6, col, TRUE);
            break;
        }
        case ICON_FLIP: {
            // 左右に向いた矢印と、真ん中の折り返しの線（反転）
            DrawLine(cx - 8, cy, cx + 8, cy, col, 2);
            DrawLine(cx - 8, cy, cx - 3, cy - 5, col, 2);
            DrawLine(cx - 8, cy, cx - 3, cy + 5, col, 2);
            DrawLine(cx + 8, cy, cx + 3, cy - 5, col, 2);
            DrawLine(cx + 8, cy, cx + 3, cy + 5, col, 2);
            DrawLine(cx, cy - 8, cx, cy + 8, col, 1);
            break;
        }
        case ICON_RESET: {
            // 左まわりに戻る矢印（リセット）
            DrawArc((float)cx, (float)cy, 7.0f, 220.0f, 500.0f, col, 2.0f);
            float ex = cx + cosf(220.0f * 3.14159265f / 180.0f) * 7.0f, ey = cy - sinf(220.0f * 3.14159265f / 180.0f) * 7.0f;
            DrawLine((int)ex, (int)ey, (int)ex - 5, (int)ey - 1, col, 2);
            DrawLine((int)ex, (int)ey, (int)ex - 1, (int)ey + 5, col, 2);
            break;
        }
        case ICON_CUTDEL: {
            // ×印（カット削除）
            DrawLine(cx - 6, cy - 6, cx + 6, cy + 6, col, 3);
            DrawLine(cx - 6, cy + 6, cx + 6, cy - 6, col, 3);
            break;
        }
        case ICON_GRID: {
            // 4つの小さな四角（ステージの一覧）
            DrawBox(cx - 8, cy - 8, cx - 2, cy - 2, col, TRUE);
            DrawBox(cx + 2, cy - 8, cx + 8, cy - 2, col, TRUE);
            DrawBox(cx - 8, cy + 2, cx - 2, cy + 8, col, TRUE);
            DrawBox(cx + 2, cy + 2, cx + 8, cy + 8, col, TRUE);
            break;
        }
        case ICON_KEYS: {
            // キーボード（操作一覧）：外枠と、キーの点、スペースバー
            DrawBox(cx - 9, cy - 6, cx + 9, cy + 6, col, FALSE);
            DrawBox(cx - 6, cy - 3, cx - 5, cy - 2, col, TRUE);
            DrawBox(cx - 2, cy - 3, cx - 1, cy - 2, col, TRUE);
            DrawBox(cx + 2, cy - 3, cx + 3, cy - 2, col, TRUE);
            DrawBox(cx + 5, cy - 3, cx + 6, cy - 2, col, TRUE);
            DrawLine(cx - 5, cy + 3, cx + 5, cy + 3, col, 2);
            break;
        }
        case ICON_POWER: {
            // 電源の印：上が欠けた輪と、縦の棒（おわる）
            DrawArc((float)cx, (float)cy + 1.0f, 7.0f, 120.0f, 420.0f, col, 2.0f);
            DrawLine(cx, cy - 8, cx, cy, col, 2);
            break;
        }
        case ICON_MOVE: {
            // 走る矢印（動き）
            DrawLine(cx - 8, cy - 4, cx - 3, cy - 4, col, 1);
            DrawLine(cx - 8, cy + 4, cx - 3, cy + 4, col, 1);
            DrawLine(cx - 6, cy, cx + 7, cy, col, 2);
            DrawLine(cx + 7, cy, cx + 2, cy - 5, col, 2);
            DrawLine(cx + 7, cy, cx + 2, cy + 5, col, 2);
            break;
        }
        case ICON_LAYER: {
            // ずらして重ねた3枚の板（レイヤー）。奥の2枚は枠だけ、いちばん手前だけ塗る
            DrawBox(cx - 3, cy - 9, cx + 9, cy + 1, col, FALSE);
            DrawBox(cx - 6, cy - 6, cx + 6, cy + 4, col, FALSE);
            DrawBox(cx - 9, cy - 3, cx + 3, cy + 7, col, TRUE);
            break;
        }
        case ICON_LOCK: {
            // 錠前（このステージでは使えない操作）：上の弓形と、下の四角い本体
            DrawArc((float)cx, (float)cy - 2.0f, 3.5f, 0.0f, 180.0f, col, 1.6f);
            DrawLine(cx - 3, cy - 2, cx - 3, cy, col, 1);
            DrawLine(cx + 3, cy - 2, cx + 3, cy, col, 1);
            DrawBox(cx - 5, cy, cx + 6, cy + 7, col, TRUE);
            break;
        }
    }
}

// ---- チップ（「拡大」「加速」のような小さな札）----
// 描いた札の右端のxを返す。続けて次の札を並べるときに使う。
inline int DrawChip(int x, int y, const char* s, unsigned int fg, unsigned int back, unsigned int edge, int size = 12) {
    int w = TextWidth(s, size) + 12;
    DrawBox(x, y, x + w, y + size + 6, back, TRUE);
    DrawBox(x, y, x + w, y + size + 6, edge, FALSE);
    DrawText(x + 6, y + 3, s, fg, size);
    return x + w;
}

// ---- 選んだ物の絵の枠 ----
// handle が有効ならその絵を枠に収めて出す。無効なら、kind の印（0=なにも選んでいない 1=ブロック 2=複数）を描く。
inline void DrawThumb(const Box& b, int handle, int kind) {
    DrawBox(b.x1, b.y1, b.x2, b.y2, GetColor(30, 30, 36), TRUE);
    const int cx = (b.x1 + b.x2) / 2, cy = (b.y1 + b.y2) / 2;
    if (handle >= 0) {
        int iw = 0, ih = 0;
        GetGraphSize(handle, &iw, &ih);
        if (iw > 0 && ih > 0) {
            float sc = (std::min)((float)(b.x2 - b.x1 - 6) / (float)iw, (float)(b.y2 - b.y1 - 6) / (float)ih);
            DrawRotaGraph(cx, cy, sc, 0.0, handle, TRUE);
        }
    } else if (kind == 1) {
        // ブロック（ギミック）の印：四角と、中の継ぎ目
        DrawBox(cx - 11, cy - 11, cx + 11, cy + 11, GetColor(150, 158, 190), FALSE);
        DrawLine(cx - 11, cy, cx + 11, cy, GetColor(150, 158, 190));
        DrawLine(cx, cy - 11, cx, cy, GetColor(150, 158, 190));
        DrawLine(cx - 5, cy, cx - 5, cy + 11, GetColor(150, 158, 190));
    } else if (kind == 2) {
        // 複数選択の印：重ねた四角
        DrawBox(cx - 12, cy - 12, cx + 4, cy + 4, GetColor(150, 158, 190), FALSE);
        DrawBox(cx - 4, cy - 4, cx + 12, cy + 12, GetColor(200, 206, 230), FALSE);
    } else {
        // なにも選んでいない：点線の四角
        for (int i = 0; i < 5; i++) {
            int o = -12 + i * 6;
            DrawLine(cx + o, cy - 12, cx + o + 3, cy - 12, GetColor(110, 116, 140), 1);
            DrawLine(cx + o, cy + 12, cx + o + 3, cy + 12, GetColor(110, 116, 140), 1);
            DrawLine(cx - 12, cy + o, cx - 12, cy + o + 3, GetColor(110, 116, 140), 1);
            DrawLine(cx + 12, cy + o, cx + 12, cy + o + 3, GetColor(110, 116, 140), 1);
        }
    }
    DrawBox(b.x1, b.y1, b.x2, b.y2, GetColor(124, 134, 168), FALSE);
}

// ---- 扇形（パイ）の1枚 ----
// 中心 (cx,cy) から、内側の半径 r0・外側の半径 r1 の間を、角度 a0〜a1（度。真上が0、時計回りが正）で塗る。
// 右クリックの扇形メニューの1項目ぶん。塗りは細かい三角形の連なり（隣と隙間が出ないよう、端点は整数で共有する）、
// 輪郭は AA の線で描く。a1-a0 が 360 のときは、輪っか全体を描く（放射状の縁は引かない）。
inline void DrawPieSector(int cx, int cy, float r0, float r1, float a0, float a1,
                          unsigned int fill, unsigned int edge, float edgeW) {
    const float kPi = 3.14159265f;
    auto PX = [&](float r, float a) { return cx + (int)lroundf(r * sinf(a * kPi / 180.0f)); };
    auto PY = [&](float r, float a) { return cy - (int)lroundf(r * cosf(a * kPi / 180.0f)); };
    int steps = (int)ceilf((a1 - a0) / 5.0f);
    if (steps < 1) steps = 1;
    for (int i = 0; i < steps; i++) {
        float s = a0 + (a1 - a0) * (float)i / (float)steps;
        float e = a0 + (a1 - a0) * (float)(i + 1) / (float)steps;
        DrawTriangle(PX(r1, s), PY(r1, s), PX(r1, e), PY(r1, e), PX(r0, e), PY(r0, e), fill, TRUE);
        DrawTriangle(PX(r1, s), PY(r1, s), PX(r0, e), PY(r0, e), PX(r0, s), PY(r0, s), fill, TRUE);
        DrawLineAA((float)PX(r1, s), (float)PY(r1, s), (float)PX(r1, e), (float)PY(r1, e), edge, edgeW);
        DrawLineAA((float)PX(r0, s), (float)PY(r0, s), (float)PX(r0, e), (float)PY(r0, e), edge, edgeW);
    }
    if (a1 - a0 < 359.0f) {
        DrawLineAA((float)PX(r0, a0), (float)PY(r0, a0), (float)PX(r1, a0), (float)PY(r1, a0), edge, edgeW);
        DrawLineAA((float)PX(r0, a1), (float)PY(r0, a1), (float)PX(r1, a1), (float)PY(r1, a1), edge, edgeW);
    }
}

// ======================================================================
// 右パネル上半分「選んだ物」の枠の部品（2026-11-26 の作り直しで追加）
// ======================================================================
// テスターから「右上はボタンが小さく、何に対してできるのか分かりにくい」と言われたので、
// 青い枠でひとまとめにし、選んだ物のカードと3列の大きなタイルで見せる。
// ここには見た目の部品だけを置き、どのタイルがいつ使えるか・押したら何が起きるかは DrawPixel.cpp が決める。
// タイルの位置は InspLayout.h の表で決める。

// 定義の名前を「（」の前（名前）と後ろ（説明）に分ける。
// 例：「回転させる敵（近くの物を回し続ける）」→ 名前「回転させる敵」、説明「近くの物を回し続ける」。
// 全角の括弧だけを見る（半角の括弧は名前の一部として使われることがあるので分けない）。
// 先頭が「（」のとき（名前が空になってしまうとき）は分けない。閉じ括弧が無ければ、後ろを全部説明にする。
inline void SplitNameDesc(const std::string& full, std::string& name, std::string& desc) {
    static const std::string open = "（", close = "）";
    const size_t pos = full.find(open);
    if (pos == std::string::npos || pos == 0) { name = full; desc.clear(); return; }
    name = full.substr(0, pos);
    desc = full.substr(pos + open.size());
    if (desc.size() >= close.size() && desc.compare(desc.size() - close.size(), close.size(), close) == 0) {
        desc.resize(desc.size() - close.size());
    }
}

// 角の丸い枠（下地を塗って、縁を半透明の色で描く）。右パネルの「選んだ物」の枠に使う
inline void DrawSection(const Box& b, unsigned int edge, unsigned int back) {
    DrawRoundRectAA((float)b.x1, (float)b.y1, (float)b.x2, (float)b.y2, 10.0f, 10.0f, 16, back, TRUE);
    SetDrawBlendMode(DX_BLENDMODE_ALPHA, 110);
    DrawRoundRectAA((float)b.x1, (float)b.y1, (float)b.x2, (float)b.y2, 10.0f, 10.0f, 16, edge, FALSE, 1.0f);
    SetDrawBlendMode(DX_BLENDMODE_NOBLEND, 0);
}

// 枠の見出し：色の点＋太字の題名。y は文字の上端
inline void DrawSectionHeader(int x, int y, const char* title, unsigned int dot, unsigned int ink) {
    DrawCircleAA((float)(x + 4), (float)(y + 8), 4.0f, 12, dot, TRUE);
    UiFont::DrawBold(x + 13, y, title, ink, 13);
}

// タイルの見た目の状態
//   TILE_NORMAL … ふつう   TILE_HOVER … マウスが乗っている   TILE_ACTIVE … つかんでいる／ON
//   TILE_DISABLED … いまは使えない（沈んだ色）
enum TileLook { TILE_NORMAL, TILE_HOVER, TILE_ACTIVE, TILE_DISABLED };

// タイルの下地と縁。ふだんは縁を下地と同じ色にして、線を減らす（ごちゃごちゃして見えないように）。
// 乗っている・つかんでいるときだけ、縁で目立たせる。
inline void DrawTile(const Box& b, TileLook look) {
    unsigned int back, edge;
    switch (look) {
        case TILE_HOVER:    back = GetColor(74, 77, 102); edge = GetColor(200, 214, 245); break;
        case TILE_ACTIVE:   back = GetColor(28, 64, 118); edge = GetColor(120, 190, 255); break;
        case TILE_DISABLED: back = GetColor(48, 46, 54);  edge = GetColor(48, 46, 54);    break;
        default:            back = GetColor(58, 59, 78);  edge = GetColor(58, 59, 78);    break;
    }
    DrawRoundRectAA((float)b.x1, (float)b.y1, (float)b.x2, (float)b.y2, 8.0f, 8.0f, 12, back, TRUE);
    DrawRoundRectAA((float)b.x1, (float)b.y1, (float)b.x2, (float)b.y2, 8.0f, 8.0f, 12, edge, FALSE, 1.0f);
}

// 点線の四角（何も選んでいないときの案内の枠）
inline void DrawDashedBox(const Box& b, unsigned int col) {
    for (int x = b.x1 + 6; x < b.x2 - 6; x += 7) {
        DrawLine(x, b.y1, (std::min)(x + 4, b.x2 - 6), b.y1, col);
        DrawLine(x, b.y2, (std::min)(x + 4, b.x2 - 6), b.y2, col);
    }
    for (int y = b.y1 + 6; y < b.y2 - 6; y += 7) {
        DrawLine(b.x1, y, b.x1, (std::min)(y + 4, b.y2 - 6), col);
        DrawLine(b.x2, y, b.x2, (std::min)(y + 4, b.y2 - 6), col);
    }
}

// 文字を、左右の真ん中にそろえて描く（size のふつうの太さ／bold で太字）
inline void DrawCentered(int cx, int y, const char* s, unsigned int col, int size, bool bold) {
    if (bold) UiFont::DrawBold(cx - UiFont::WidthBold(s, size) / 2, y, s, col, size);
    else      UiFont::Draw(cx - UiFont::Width(s, size) / 2, y, s, col, size);
}

// 何も選んでいないとき：点線の枠の中に、右ボタンが青いマウスの絵と「右クリックで選ぶ」案内。
// 以前は灰色の行を7本並べていたが、「何をすればいいか」だけを大きく見せるほうが伝わる。
inline void DrawEmptyState(const Box& b) {
    const unsigned int line = GetColor(92, 102, 128), sub = ColInkSub(), blue = GetColor(120, 190, 255);
    DrawDashedBox(b, line);
    const int cx = (b.x1 + b.x2) / 2;
    const int top = b.y1 + 40;
    // マウス：右ボタンだけ青く塗り、左右のボタンの境と、ボタンと本体の境を線で描く
    DrawBox(cx + 1, top + 2, cx + 13, top + 20, blue, TRUE);
    DrawRoundRectAA((float)(cx - 15), (float)top, (float)(cx + 15), (float)(top + 46), 14.0f, 14.0f, 16, GetColor(154, 162, 188), FALSE, 2.0f);
    DrawLine(cx, top, cx, top + 20, GetColor(154, 162, 188), 2);
    DrawLine(cx - 15, top + 20, cx + 15, top + 20, GetColor(154, 162, 188), 2);
    DrawCentered(cx, top + 62, "画面の敵やギミックを", ColInk(), 14, true);
    DrawCentered(cx, top + 82, "右クリックで選ぶ", ColInk(), 14, true);
    DrawCentered(cx, top + 112, "選ぶと、ここにその物の", sub, 11, false);
    DrawCentered(cx, top + 128, "大きさ・角度・速さなどの", sub, 11, false);
    DrawCentered(cx, top + 144, "ボタンが出ます", sub, 11, false);
}

// このステージでは物の編集が封印されているとき：錠前と、使えないことの説明
inline void DrawLockedState(const Box& b) {
    const int cx = (b.x1 + b.x2) / 2, cy = (b.y1 + b.y2) / 2;
    DrawRoundRectAA((float)b.x1, (float)b.y1, (float)b.x2, (float)b.y2, 8.0f, 8.0f, 12, GetColor(44, 41, 40), TRUE);
    DrawIcon(ICON_LOCK, cx, cy - 30, GetColor(255, 220, 160));
    DrawCentered(cx, cy - 8, "このステージでは", GetColor(255, 220, 160), 12, false);
    DrawCentered(cx, cy + 12, "物の編集が封印されています", GetColor(255, 220, 160), 12, false);
}

// ---- モニター上の選択の角かっこ ----
// 選んだ物を、四隅だけの「角かっこ」で囲む（以前は黄色い四角の枠だった）。
// 色を右パネルの「選んだ物」の青い枠・カードの縁と同じにして、「パネルのボタンはこの物に効く」とつなげて見せる。
// 角の長さは短い辺の1/4。小さな物でも見えるよう最小6px、大きな物でもうるさくならないよう最大14px。
inline int BracketLen(int w, int h) {
    const int shortSide = (w < h) ? w : h;
    int len = shortSide / 4;
    if (len < 6) len = 6;
    if (len > 14) len = 14;
    return len;
}

// 角かっこを描く。どんな背景でも見えるよう、先に暗い縁（太さ4）を描き、その上に色の線（太さ2）を重ねる
inline void DrawCornerBrackets(int x1, int y1, int x2, int y2, unsigned int col, unsigned int edge) {
    const int len = BracketLen(x2 - x1, y2 - y1);
    for (int pass = 0; pass < 2; pass++) {
        const unsigned int c = (pass == 0) ? edge : col;
        const int th = (pass == 0) ? 4 : 2;
        DrawLine(x1, y1, x1 + len, y1, c, th); DrawLine(x1, y1, x1, y1 + len, c, th);   // 左上
        DrawLine(x2, y1, x2 - len, y1, c, th); DrawLine(x2, y1, x2, y1 + len, c, th);   // 右上
        DrawLine(x1, y2, x1 + len, y2, c, th); DrawLine(x1, y2, x1, y2 - len, c, th);   // 左下
        DrawLine(x2, y2, x2 - len, y2, c, th); DrawLine(x2, y2, x2, y2 - len, c, th);   // 右下
    }
}

// 選んだ物のカード：左に絵、右に太字の名前と小さな説明、右上に種類の札（敵 HP3／ギミック／自分…）。
// 縁は青い2px で、モニターの上の選択の角かっこと同じ色にして「この物の編集」だと分かるようにする。
// kind は DrawThumb と同じ（0=なし 1=ブロック 2=複数）。
inline void DrawTargetCard(const Box& b, int handle, int kind, const std::string& name, const std::string& desc,
                           const char* tag, unsigned int tagFg, unsigned int tagBg) {
    const unsigned int blue = GetColor(120, 190, 255);
    DrawRoundRectAA((float)b.x1, (float)b.y1, (float)b.x2, (float)b.y2, 8.0f, 8.0f, 12, GetColor(42, 40, 48), TRUE);
    DrawRoundRectAA((float)b.x1, (float)b.y1, (float)b.x2, (float)b.y2, 8.0f, 8.0f, 12, blue, FALSE, 2.0f);
    // 絵の枠：カードの高さに合わせた正方形
    const int th = (b.y2 - b.y1) - 10;
    const Box tb = { b.x1 + 5, b.y1 + 5, b.x1 + 5 + th, b.y1 + 5 + th };
    DrawThumb(tb, handle, kind);
    // 名前・札・説明の並び。名前はなるべく切らない（「何を選んだか」がこのカードのいちばん大事な情報なので）。
    //   説明がある（敵）  … 1行目＝名前と、右端に札　2行目＝説明
    //   説明が無い（ギミック・プレイヤー）… 1行目＝名前（幅いっぱい）　2行目＝札
    // 名前が入りきらないときは、まず文字を 15px→13px に小さくし、それでも入らないときだけ末尾を「…」にする。
    const int tx = tb.x2 + 8;
    const int fullMax = b.x2 - 6 - tx;
    const int tagW = TextWidth(tag, 10) + 12;
    const int nameMax = desc.empty() ? fullMax : (fullMax - tagW - 4);
    int nameSize = 15;
    if (UiFont::WidthBold(name.c_str(), nameSize) > nameMax) nameSize = 13;
    std::string shown = name;
    if (UiFont::WidthBold(shown.c_str(), nameSize) > nameMax) {
        while (shown.size() > 1 && UiFont::WidthBold((shown + "…").c_str(), nameSize) > nameMax) {
            size_t cut = shown.size() - 1;
            while (cut > 0 && ((unsigned char)shown[cut] & 0xC0) == 0x80) cut--;
            shown.resize(cut);
        }
        shown += "…";
    }
    auto DrawTag = [&](int x1, int y1) {
        DrawRoundRectAA((float)x1, (float)y1, (float)(x1 + tagW), (float)(y1 + 15), 7.0f, 7.0f, 10, tagBg, TRUE);
        DrawText(x1 + 6, y1 + 1, tag, tagFg, 10);
    };
    if (desc.empty()) {
        UiFont::DrawBold(tx, b.y1 + 4 + (15 - nameSize), shown.c_str(), ColInk(), nameSize);
        DrawTag(tx, b.y2 - 19);
    } else {
        UiFont::DrawBold(tx, b.y1 + 5 + (15 - nameSize), shown.c_str(), ColInk(), nameSize);
        DrawTag(b.x2 - 6 - tagW, b.y1 + 6);
        DrawText(tx, b.y1 + 28, FitText(desc, fullMax, 11).c_str(), ColInkSub(), 11);
    }
}

} // namespace EditPanel
