#pragma once
// ======================================================================
// UiTheme.h - プレイ画面のまわりの「見た目のテーマ」（2000年代の編集ソフト風）
//
// 【なぜ作ったか】
// 2026-11-26、ユーザーから「右側の形式は良いが、全体的になんとなくAIっぽい。
// この作品は2000年代の編集ソフトを意識しているので、その雰囲気に合わせたい」と言われた。
// 角の丸い枠・丸い札・半透明の色の縁・大きくなめらかな文字・一色のゲージ、といった
// 今どきの作りをやめ、当時の編集ソフトの作法（四角い角、1pxの線で表す立体、
// グラデーションの見出しバー、目盛りのメーター、点線の選択）で描き直す。
//
// 見た目は2種類あり、遊ぶ人が Esc メニューの「設定」で選ぶ（セーブデータの "ui_theme"）。
//   DARK  … 暗い灰色（既定）。2000年代後半のプロ用映像ソフト風
//   LIGHT … 明るい灰色。2000年代前半の Windows ソフト風
// 2つの違いは、この「色の表」と、少しの作法（見出しバーのグラデーションの向き、
// 押し込まれたボタンの地、メーターの塗り方）だけにしてある。
// 部品（右パネル・左バー・下のパネル・情報窓・扇形メニュー・Esc メニュー）は、
// 色を直接書かずにこの表と描き方を呼ぶので、見た目を切り替えると全部が一度に変わる。
//
// 【矩形の決まり】
// Box は左上 (x1,y1) と右下 (x2,y2) で、右下の点も含む（EditPanel::HitBox と同じ）。
// 描き方の関数はすべてこの決まりで線を引く（右端の線は x2 の列、下端の線は y2 の行）。
//
// DxLib と UiFont.h を include したあとで include すること。
// ======================================================================

#include "UiFont.h"
#include <cmath>
#include <string>

namespace UiTheme {

// 見た目の種類
enum Id { DARK = 0, LIGHT = 1 };

// 色（0〜255）。GetColor に渡す前の形で持つと、明るさの比（読みやすさ）を計算できる
struct Rgb { int r, g, b; };

// 色の表。名前と用途は設計文書（docs/superpowers/specs/2026-11-26-ui-theme-2000s-design.md の 5.1）と同じ
struct Palette {
    Rgb face;         // パネルの地
    Rgb faceIn;       // パネルの中の地（枠・タブのページの中）
    Rgb workspace;    // モニターのまわりの空き
    Rgb hilite;       // 立体の明るい線（外側・左上）
    Rgb light;        // 立体の内側の明るい線
    Rgb shadow;       // 立体の内側の暗い線
    Rgb dark;         // 立体の暗い線（外側・右下）
    Rgb well;         // へこんだ窓の下地（色の輪・時間表示）
    Rgb field;        // 数値欄の地
    Rgb ink;          // 文字
    Rgb inkSub;       // 補助の文字
    Rgb inkDim;       // 使えない項目の文字
    Rgb hot;          // ドラッグで変える値の文字（点線の下線つき）
    Rgb sel;          // 選ばれている・ON の地
    Rgb selInk;       // 選ばれている・ON の文字
    Rgb captionFrom;  // 見出しバーのグラデーションの始まり
    Rgb captionTo;    // 見出しバーのグラデーションの終わり
    Rgb captionInk;   // 見出しバーの文字
    Rgb timeInk;      // 時間表示の数字
    Rgb meterGood;    // メーター：十分（暗い灰色では緑）
    Rgb meterWarn;    // メーター：減ってきた（暗い灰色では黄）
    Rgb meterBad;     // メーター：残りわずか（赤）
    Rgb meterBlock;   // メーター：明るい灰色の青いブロック
    Rgb danger;       // 「ゲームをおわる」や、使えない・足りないときの赤い文字
};

// 2つの見た目の色の表。値は設計文書の目安から、読みやすさ（Contrast）を確かめて決めた
inline const Palette& PaletteOf(Id id) {
    static const Palette dark = {
        { 58, 58, 58 },  { 62, 62, 62 },  { 42, 42, 42 },
        { 106, 106, 106 }, { 87, 87, 87 }, { 51, 51, 51 }, { 26, 26, 26 },
        { 28, 28, 28 },  { 44, 44, 44 },
        { 216, 216, 216 }, { 168, 168, 168 }, { 110, 110, 110 },
        { 106, 166, 255 },
        { 47, 58, 72 },  { 207, 224, 255 },
        { 92, 92, 92 },  { 68, 68, 68 },  { 207, 207, 207 },
        { 242, 181, 68 },
        { 63, 191, 79 }, { 232, 196, 58 }, { 224, 72, 58 }, { 63, 191, 79 },
        { 255, 150, 130 },
    };
    static const Palette light = {
        { 212, 208, 200 }, { 212, 208, 200 }, { 128, 128, 128 },
        { 255, 255, 255 }, { 232, 230, 224 }, { 128, 128, 128 }, { 64, 64, 64 },
        { 0, 0, 0 },     { 255, 255, 255 },
        { 0, 0, 0 },     { 64, 64, 64 },  { 128, 128, 128 },
        { 10, 36, 106 },
        { 10, 36, 106 }, { 255, 255, 255 },
        { 10, 36, 106 }, { 166, 202, 240 }, { 255, 255, 255 },
        { 76, 255, 76 },
        { 10, 36, 106 }, { 10, 36, 106 }, { 200, 0, 0 },  { 10, 36, 106 },
        { 176, 0, 0 },
    };
    return (id == LIGHT) ? light : dark;
}

// いまの見た目（起動時はセーブデータの値、設定で変えたらその場で書き換える）
inline Id& CurrentRef() { static Id cur = DARK; return cur; }
inline Id Current() { return CurrentRef(); }
inline void Set(Id id) { CurrentRef() = id; }
// いまの見た目の色の表
inline const Palette& P() { return PaletteOf(Current()); }

// セーブデータの文字との行き来（"light" 以外は、空・読めない値も含めて暗い灰色）
inline Id ParseId(const std::string& s) { return (s == "light") ? LIGHT : DARK; }
inline const char* IdName(Id id) { return (id == LIGHT) ? "light" : "dark"; }

// 色の相対輝度（WCAG の式）と、2色の明るさの比（1〜21。大きいほど読みやすい）
inline float Luminance(Rgb c) {
    auto ch = [](int v) {
        const float s = (float)v / 255.0f;
        return (s <= 0.03928f) ? s / 12.92f : powf((s + 0.055f) / 1.055f, 2.4f);
    };
    return 0.2126f * ch(c.r) + 0.7152f * ch(c.g) + 0.0722f * ch(c.b);
}
inline float Contrast(Rgb a, Rgb b) {
    float la = Luminance(a), lb = Luminance(b);
    if (la < lb) { const float t = la; la = lb; lb = t; }
    return (la + 0.05f) / (lb + 0.05f);
}

inline unsigned int Col(Rgb c) { return GetColor(c.r, c.g, c.b); }

// 矩形（右下も含む）
struct Box { int x1, y1, x2, y2; };

// ---- 純粋な計算（テストで確かめる）----

// メーターの、灯す区切りの数。0 なら 0、少しでも残っていれば最低 1（「まだ残っている」が見えるように）
inline int MeterLit(float ratio, int segments) {
    if (segments <= 0 || ratio <= 0.0f) return 0;
    if (ratio >= 1.0f) return segments;
    int n = (int)(ratio * (float)segments);
    return (n < 1) ? 1 : n;
}

// メーターの i 番目の区切りの色。暗い灰色は下から6割が緑・8割5分までが黄・残りが赤（音のレベルメーターと同じ）。
// 明るい灰色は、当時の進み具合バーと同じ青いブロック1色
inline Rgb MeterColor(int i, int segments) {
    const Palette& p = P();
    if (Current() == LIGHT) return p.meterBlock;
    const float t = ((float)i + 0.5f) / (float)(segments > 0 ? segments : 1);
    return (t < 0.6f) ? p.meterGood : (t < 0.85f) ? p.meterWarn : p.meterBad;
}

// 選択の点線（蟻の行列）を流す量。4フレームに1px、8で一周する
inline int AntsOffset(int frame) { return ((frame < 0 ? 0 : frame) / 4) % 8; }

// ---- 描き方 ----

// 横線・縦線（両端の点も含む）
inline void HLine(int x1, int x2, int y, Rgb c) { if (x2 >= x1) DrawLine(x1, y, x2 + 1, y, Col(c)); }
inline void VLine(int x, int y1, int y2, Rgb c) { if (y2 >= y1) DrawLine(x, y1, x, y2 + 1, Col(c)); }
// 塗りつぶし（右下も含む）
inline void Fill(const Box& b, Rgb c) { DrawBox(b.x1, b.y1, b.x2 + 1, b.y2 + 1, Col(c), TRUE); }

// 立体の縁。外側の左上・右下、内側の左上・右下の4色で、盛り上がり／へこみを表す
inline void Bevel(const Box& b, Rgb tlOut, Rgb brOut, Rgb tlIn, Rgb brIn) {
    HLine(b.x1, b.x2 - 1, b.y1, tlOut);     VLine(b.x1, b.y1, b.y2 - 1, tlOut);
    HLine(b.x1, b.x2, b.y2, brOut);         VLine(b.x2, b.y1, b.y2, brOut);
    HLine(b.x1 + 1, b.x2 - 2, b.y1 + 1, tlIn); VLine(b.x1 + 1, b.y1 + 1, b.y2 - 2, tlIn);
    HLine(b.x1 + 1, b.x2 - 1, b.y2 - 1, brIn); VLine(b.x2 - 1, b.y1 + 1, b.y2 - 1, brIn);
}

// 盛り上がったボタン・タブ（地は face）
inline void Raised(const Box& b) {
    const Palette& p = P();
    Fill(b, p.face);
    Bevel(b, p.hilite, p.dark, p.light, p.shadow);
}

// 押し込まれたボタン（ON・押している間）。線を逆にして、地を「選ばれている」色にする。
// 明るい灰色の当時の作法は細かい市松の地だが、1画素ずつ描くと重いので、明るい地1色で代える
inline void Pressed(const Box& b) {
    const Palette& p = P();
    Fill(b, (Current() == LIGHT) ? p.light : p.sel);
    Bevel(b, p.dark, p.hilite, p.shadow, p.light);
}

// へこんだ窓（数値欄・色の輪の窓・時間表示）
inline void Sunken(const Box& b, Rgb fill) {
    const Palette& p = P();
    Fill(b, fill);
    Bevel(b, p.shadow, p.hilite, p.dark, p.light);
}

// 溝の区切り線（暗い線の1px下に明るい線）
inline void Etched(int x1, int x2, int y) {
    const Palette& p = P();
    HLine(x1, x2, y, p.shadow);
    HLine(x1, x2, y + 1, p.hilite);
}

// 文字を、矩形の縦の真ん中に描くための上端
inline int TextTop(const Box& b, int size) { return (b.y1 + b.y2 + 1) / 2 - (size + 1) / 2; }

// グループ枠：溝の線の四角と、左上の見出し文字（見出しの後ろは枠の線を消す）
inline void GroupBox(const Box& b, const char* caption) {
    const Palette& p = P();
    // 溝の四角：暗い線の四角と、1px 右下にずらした明るい線の四角
    HLine(b.x1, b.x2 - 1, b.y1, p.shadow); VLine(b.x1, b.y1, b.y2 - 1, p.shadow);
    HLine(b.x1, b.x2 - 1, b.y2 - 1, p.shadow); VLine(b.x2 - 1, b.y1, b.y2 - 1, p.shadow);
    HLine(b.x1 + 1, b.x2, b.y1 + 1, p.hilite); VLine(b.x1 + 1, b.y1 + 1, b.y2, p.hilite);
    HLine(b.x1, b.x2, b.y2, p.hilite);         VLine(b.x2, b.y1, b.y2, p.hilite);
    if (caption != nullptr && caption[0] != '\0') {
        const int w = UiFont::Width(caption, 12);
        Fill({ b.x1 + 6, b.y1 - 2, b.x1 + 6 + w + 5, b.y1 + 3 }, p.faceIn);
        UiFont::Draw(b.x1 + 8, b.y1 - 6, caption, Col(p.ink), 12);
    }
}

// タブ。開いているタブは手前に出て、下の線が無い（下のページとつながって見える）。
// 開いていないタブは一段低く、文字も薄い
inline void Tab(const Box& b, const char* text, bool active) {
    const Palette& p = P();
    if (active) {
        Fill(b, p.faceIn);
        HLine(b.x1, b.x2 - 1, b.y1, p.hilite); VLine(b.x1, b.y1, b.y2, p.hilite);
        VLine(b.x2, b.y1 + 1, b.y2, p.dark);   VLine(b.x2 - 1, b.y1 + 1, b.y2, p.shadow);
        const int w = UiFont::WidthBold(text, 12);
        UiFont::DrawBold((b.x1 + b.x2 + 1 - w) / 2, TextTop(b, 12), text, Col(p.ink), 12);
    } else {
        const Box lo = { b.x1, b.y1 + 2, b.x2, b.y2 };
        Fill(lo, p.face);
        HLine(lo.x1, lo.x2 - 1, lo.y1, p.hilite); VLine(lo.x1, lo.y1, lo.y2, p.hilite);
        VLine(lo.x2, lo.y1 + 1, lo.y2, p.shadow);
        HLine(b.x1, b.x2, b.y2, p.hilite);    // 下のページの上の縁
        const int w = UiFont::Width(text, 12);
        UiFont::Draw((lo.x1 + lo.x2 + 1 - w) / 2, TextTop(lo, 12), text, Col(p.inkSub), 12);
    }
}

// 頂点ごとに色を付けた四角形（グラデーション）。左上・右上・左下・右下の色
inline void GradQuad(const Box& b, Rgb tl, Rgb tr, Rgb bl, Rgb br) {
    VERTEX2D v[6];
    auto Set = [](VERTEX2D& q, float x, float y, Rgb c) {
        q.pos = VGet(x, y, 0.0f); q.rhw = 1.0f; q.dif = GetColorU8(c.r, c.g, c.b, 255); q.u = 0.0f; q.v = 0.0f;
    };
    const float x1 = (float)b.x1, y1 = (float)b.y1, x2 = (float)(b.x2 + 1), y2 = (float)(b.y2 + 1);
    Set(v[0], x1, y1, tl); Set(v[1], x2, y1, tr); Set(v[2], x1, y2, bl);
    Set(v[3], x2, y1, tr); Set(v[4], x2, y2, br); Set(v[5], x1, y2, bl);
    DrawPrimitive2D(v, 6, DX_PRIMTYPE_TRIANGLELIST, DX_NONE_GRAPH, FALSE);
}

// 見出しバー。暗い灰色は上から下へ、明るい灰色は左から右へのグラデーション（当時の窓の題名の帯）
inline void Caption(const Box& b, const char* text) {
    const Palette& p = P();
    if (Current() == LIGHT) GradQuad(b, p.captionFrom, p.captionTo, p.captionFrom, p.captionTo);
    else                    GradQuad(b, p.captionFrom, p.captionFrom, p.captionTo, p.captionTo);
    if (text != nullptr) UiFont::DrawBold(b.x1 + 5, TextTop(b, 12), text, Col(p.captionInk), 12);
}

// ドラッグで変える値：hot の色の文字と、その下の点線（1px おき）。描いた文字の幅を返す
inline int HotText(int x, int y, const char* text, int size) {
    const Palette& p = P();
    UiFont::Draw(x, y, text, Col(p.hot), size);
    const int w = UiFont::Width(text, size);
    const unsigned int c = Col(p.hot);
    for (int i = 0; i < w; i += 2) DrawPixel(x + i, y + size + 1, c);
    return w;
}

// メーター（へこんだ窓の中に区切りを並べる）。vertical なら下から上へ灯る
inline void Meter(const Box& b, float ratio, bool vertical) {
    const Palette& p = P();
    Sunken(b, (Current() == LIGHT) ? p.field : p.well);
    const int len = vertical ? (b.y2 - b.y1 - 3) : (b.x2 - b.x1 - 3);
    const int seg = (Current() == LIGHT) ? 9 : 6;   // 区切り1つ（＋1〜2pxのすき間）の長さ
    const int gap = (Current() == LIGHT) ? 2 : 1;
    const int count = (len + gap) / (seg + gap);
    const int lit = MeterLit(ratio, count);
    // 明るい灰色は、残りわずか（2割未満）でブロックを赤にする
    const bool low = (Current() == LIGHT) && ratio < 0.2f;
    const Rgb unlit = (Current() == LIGHT) ? p.field : Rgb{ 40, 40, 40 };
    for (int i = 0; i < count; i++) {
        const Rgb c = (i < lit) ? (low ? p.meterBad : MeterColor(i, count)) : unlit;
        if (vertical) {
            const int y2 = b.y2 - 2 - i * (seg + gap), y1 = y2 - seg + 1;
            Fill({ b.x1 + 2, y1, b.x2 - 2, y2 }, c);
        } else {
            const int x1 = b.x1 + 2 + i * (seg + gap);
            Fill({ x1, b.y1 + 2, x1 + seg - 1, b.y2 - 2 }, c);
        }
    }
}

// 状態バーの1区画（浅くへこんだ枠に1行の文字）
inline void StatusPane(const Box& b, const char* text) {
    const Palette& p = P();
    Fill(b, p.face);
    HLine(b.x1, b.x2 - 1, b.y1, p.shadow); VLine(b.x1, b.y1, b.y2 - 1, p.shadow);
    HLine(b.x1, b.x2, b.y2, p.hilite);     VLine(b.x2, b.y1, b.y2, p.hilite);
    if (text != nullptr) UiFont::Draw(b.x1 + 5, TextTop(b, 12), text, Col(p.ink), 12);
}

// 選んだ物の印：白と黒が4pxずつ交互の点線（蟻の行列。frame で流れる）と、四隅・辺の真ん中の8つの小さな四角。
// 白と黒が交互なので、明るい背景でも暗い背景でも、色フィルタが掛かっていても見える
inline void SelectionMarks(int x1, int y1, int x2, int y2, int frame) {
    if (x2 < x1) { const int t = x1; x1 = x2; x2 = t; }
    if (y2 < y1) { const int t = y1; y1 = y2; y2 = t; }
    const unsigned int white = GetColor(255, 255, 255), black = GetColor(0, 0, 0);
    const int phase = AntsOffset(frame);
    // 周りを一続きの道として、4pxごとに色を入れ替える（流れる向きは時計回り）
    int along = 0;
    auto Seg = [&](int ax, int ay, int bx, int by) {
        const int dx = (bx > ax) - (bx < ax), dy = (by > ay) - (by < ay);
        const int n = (bx - ax) * dx + (by - ay) * dy;
        for (int i = 0; i < n; i++, along++) {
            const unsigned int c = (((along + 8 - phase) / 4) % 2 == 0) ? white : black;
            DrawPixel(ax + dx * i, ay + dy * i, c);
        }
    };
    Seg(x1, y1, x2, y1); Seg(x2, y1, x2, y2); Seg(x2, y2, x1, y2); Seg(x1, y2, x1, y1);
    // 8つのつまみ（白い四角に黒い縁）
    const int mx = (x1 + x2) / 2, my = (y1 + y2) / 2;
    const int pts[8][2] = { { x1, y1 }, { mx, y1 }, { x2, y1 }, { x1, my }, { x2, my }, { x1, y2 }, { mx, y2 }, { x2, y2 } };
    for (const auto& q : pts) {
        DrawBox(q[0] - 3, q[1] - 3, q[0] + 3, q[1] + 3, white, TRUE);
        DrawBox(q[0] - 3, q[1] - 3, q[0] + 3, q[1] + 3, black, FALSE);
    }
}

} // namespace UiTheme
