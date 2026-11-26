#pragma once
// ============================================================================
// ColorGrade.h - 「カラー補正パネル」と、画面に掛ける色補正の計算・描画ヘルパー
//
// 【なぜ作ったか】
// これまでの画面の明るさ・色の操作は「X長押し＝固定の暗転(0.3)」「C長押し＝固定の明転(1.7)」
// 「Tキー＝赤→緑→青の3段階」だけで、どれだけ暗い／どの色になるのかを自分で決められなかった。
// しかも「明転(1.7)」は SetDrawBright が255で頭打ちになるため、見た目には何も変わっていなかった。
// ここでは、右パネルの下半分の「画面全体」の枠で、画面の色と明るさを自分で決められるようにする。
// （2026-11-26 作り直し：テスターから「ごちゃごちゃして分かりにくい」と言われたため、
//   スライダー5本＋小さな色の輪＋前/後のテストチャートの形をやめ、ペイントソフトの色の選び方に寄せた）
//   ・「色と明るさ」タブ … 外の色相の輪＝色フィルタの色、中の四角の横＝色フィルタの強さ、縦＝明るさ。
//                          ゲームの判定とコストに効くのはこの2つだけなので、1つの点でまとめて決められるようにした
//   ・「細かい調整」タブ … コントラスト／彩度／色相／色温度のスライダー（コストはかからない）
//   ・ゲームの判定の表示 … 「明るさ：暗い」「フィルタ：赤」のような札と、四角の上の点線（判定の境目）で見せる
//
// 【ゲームの判定との関係】
// 画面にはスライダーの値がそのまま反映されるが、ゲームの判定（敵の弱点色・色ロック足場・暗いと動く敵…）は
// これまでどおり「暗い／ふつう／明るい」と「赤・緑・青・なし」で行う。連続した値から次のように自動で決める。
//   ・明るさ  … 0.6未満＝暗い、1.3より上＝明るい（敵やギミックが見ているしきい値と同じ）
//   ・色フィルタ … 輪の強さが0.5以上で、色相が 赤(0度)・緑(120度)・青(240度) の ±35度以内なら 1/2/3。それ以外は0
// そのため、既存の敵・ギミック・ステージは1つも直さずに、そのまま動く。
//
// このヘッダーは DrawPixel.cpp の1つの翻訳単位からだけ読み込まれる（DxLib にだけ依存する。UIの色は引数で受け取る）。
// ============================================================================
#include "DxLib.h"
#include "UiFont.h"
#include "UiTheme.h"
#include <cmath>
#include <algorithm>
#include <cstdio>

namespace ColorGrade {

const float kPi = 3.14159265358979f;

// ゲームが「暗い／明るい」とみなすしきい値（敵・ギミックの判定コードと同じ値）
const float kDarkBelow   = 0.6f;
const float kBrightAbove = 1.3f;
// 明るさスライダーの範囲（X/Cキーの固定値 0.3 / 1.7 を両端にしてある）
const float kBrightMin = 0.3f;
const float kBrightMax = 1.7f;
// 色フィルタの判定：強さの下限と、赤緑青それぞれの色相の許容幅（度）
const float kFilterMinAmount = 0.5f;
const float kFilterZoneDeg   = 35.0f;

// ---------------------------------------------------------------------------
// 1. 補正の値
// ---------------------------------------------------------------------------
struct PanelState {
    float bright      = 1.0f;  // 明るさ（露出）。1.0＝そのまま。0.3〜1.7
    float contrast    = 0.0f;  // コントラスト。-1〜+1（0＝そのまま）
    float saturation  = 1.0f;  // 彩度。0〜2（1＝そのまま、0＝白黒）
    float hue         = 0.0f;  // 色相の回転（度）。-180〜180
    float temperature = 0.0f;  // 色温度。-1(寒色)〜+1(暖色)
    float tintHue     = 0.0f;  // 色フィルタの色（色の輪の角度、度）。0=赤 120=緑 240=青
    float tintAmount  = 0.0f;  // 色フィルタの強さ。0〜1（0＝かけない）
};

inline void Reset(PanelState& s) { s = PanelState(); }

inline float WrapDeg(float d) {
    d = fmodf(d, 360.0f);
    if (d < 0.0f) d += 360.0f;
    return d;
}

// 色相(度)・彩度・明度 → RGB(0〜1)
inline void HsvToRgb(float hDeg, float s, float v, float& r, float& g, float& b) {
    float h = WrapDeg(hDeg) / 60.0f;
    int   i = (int)h;
    float f = h - (float)i;
    float p = v * (1.0f - s), q = v * (1.0f - s * f), t = v * (1.0f - s * (1.0f - f));
    switch (i % 6) {
        case 0: r = v; g = t; b = p; break;
        case 1: r = q; g = v; b = p; break;
        case 2: r = p; g = v; b = t; break;
        case 3: r = p; g = q; b = v; break;
        case 4: r = t; g = p; b = v; break;
        default: r = v; g = p; b = q; break;
    }
}

// 明るさを変えているか。0.05以内のずれは「触っていない」とみなす（編集コストを食わせない余白）
inline bool BrightActive(const PanelState& s) { return fabsf(s.bright - 1.0f) > 0.05f; }
// 色フィルタをかけているか
inline bool TintActive(const PanelState& s) { return s.tintAmount > 0.05f; }
// コストを使う補正（ゲームの判定に関わる＝明るさと色フィルタ）
inline bool CostlyActive(const PanelState& s) { return BrightActive(s) || TintActive(s); }
// 見た目だけの補正（コントラスト・彩度・色相・色温度）。コストは掛からない
inline bool CosmeticActive(const PanelState& s) {
    return fabsf(s.contrast) > 0.01f || fabsf(s.saturation - 1.0f) > 0.01f
        || fabsf(s.hue) > 0.5f || fabsf(s.temperature) > 0.01f;
}

// 色フィルタ（色の輪）が画面に掛ける乗算の色。赤(0度)・強さ1なら (1.0, 0.4, 0.4) で、
// これまでの「Tキーの赤フィルタ」と同じ見た目になる。色温度も同じ乗算にまとめる
// （SetDrawBright は255で頭打ちなので、暖色は青を、寒色は赤を「引く」形で表す）。
inline void TintMultiplier(const PanelState& s, float& r, float& g, float& b) {
    float cr, cg, cb;
    HsvToRgb(s.tintHue, 1.0f, 1.0f, cr, cg, cb);
    float a = (std::max)(0.0f, (std::min)(1.0f, s.tintAmount));
    r = 1.0f - a * (1.0f - (0.4f + 0.6f * cr));
    g = 1.0f - a * (1.0f - (0.4f + 0.6f * cg));
    b = 1.0f - a * (1.0f - (0.4f + 0.6f * cb));
    float t = s.temperature;
    if (t > 0.0f)      { b *= 1.0f - 0.45f * t; g *= 1.0f - 0.12f * t; }
    else if (t < 0.0f) { r *= 1.0f + 0.45f * t; g *= 1.0f + 0.12f * t; }
}

// 色フィルタの判定（ゲームが見る値）。0=なし 1=赤 2=緑 3=青
inline int JudgeFilter(const PanelState& s) {
    if (s.tintAmount < kFilterMinAmount) return 0;
    const float centers[3] = { 0.0f, 120.0f, 240.0f };
    for (int i = 0; i < 3; i++) {
        float d = fabsf(WrapDeg(s.tintHue) - centers[i]);
        if (d > 180.0f) d = 360.0f - d;
        if (d <= kFilterZoneDeg) return i + 1;
    }
    return 0;
}

// 明るさの判定。-1=暗い 0=ふつう +1=明るい
inline int JudgeBright(float b) {
    if (b < kDarkBelow) return -1;
    if (b > kBrightAbove) return 1;
    return 0;
}

// ---------------------------------------------------------------------------
// 2. 画像（色相の輪・補正の作業用画面）
// ---------------------------------------------------------------------------
// 以前の「小さな色の輪（中心＝白）」と「前/後のテストチャート」は、色相の輪＋中の四角に置き換えたので作らない。
struct Resources {
    int  ring      = -1;   // 色相の輪の画像（外側の半径 kRingOuter、内側の半径 kRingInner の帯）
    int  workScreen = -1;  // ゲーム画面にコントラスト・彩度・色相をかけた結果の置き場
    bool filterOk  = true; // GraphFilter が失敗したら false。以降は補正なしで描く（落とさない）
};

// 色相の輪の大きさ（4-A章の配置と同じ値。画像は起動時に1回だけ作るので、ここで先に決めておく）
const int kRingImgOuter = 91, kRingImgInner = 77;

inline void CreateResources(Resources& r, int screenW, int screenH) {
    // 色相の輪：帯の上の各点を、その角度の純色（彩度・明度いっぱい）で塗る。
    // 向きは数学の向き（右が0度＝赤、上が90度）で、今までの色の輪・判定（赤0・緑120・青240度）と同じ。
    // 外周と内周は 1px ぶん透明度をなだらかにして、ギザギザに見えないようにする。
    const int D = kRingImgOuter * 2 + 2;
    int si = MakeARGB8ColorSoftImage(D, D);
    if (si >= 0) {
        const float c = (float)D * 0.5f;
        for (int y = 0; y < D; y++) {
            for (int x = 0; x < D; x++) {
                const float dx = (float)x + 0.5f - c, dy = (float)y + 0.5f - c;
                const float rr = sqrtf(dx * dx + dy * dy);
                // 帯の外と内からの距離で、端を 1px ぼかした不透明度を決める
                const float outer = (float)kRingImgOuter + 0.5f - rr;
                const float inner = rr - ((float)kRingImgInner - 0.5f);
                const float edge = (std::min)(1.0f, (std::max)(0.0f, (std::min)(outer, inner)));
                if (edge <= 0.0f) { DrawPixelSoftImage(si, x, y, 0, 0, 0, 0); continue; }
                const float hue = atan2f(-dy, dx) * 180.0f / kPi;
                float cr, cg, cb;
                HsvToRgb(hue, 1.0f, 1.0f, cr, cg, cb);
                DrawPixelSoftImage(si, x, y, (int)(cr * 255.0f), (int)(cg * 255.0f), (int)(cb * 255.0f), (int)(edge * 255.0f));
            }
        }
        r.ring = CreateGraphFromSoftImage(si);
        DeleteSoftImage(si);
    }

    // ゲーム画面に補正をかけるための作業用画面（ゲーム画面と同じ大きさ）
    r.workScreen = MakeScreen(screenW, screenH, TRUE);
}

// ---------------------------------------------------------------------------
// 3. 画面への補正（コントラスト・彩度・色相は GraphFilter、明るさ・色フィルタは描画時の乗算）
// ---------------------------------------------------------------------------

// コントラスト・彩度・色相のどれかが効いているか
inline bool NeedsFilter(float sat, float hue, float contrast) {
    return fabsf(sat - 1.0f) > 0.01f || fabsf(hue) > 0.5f || fabsf(contrast) > 0.01f;
}

// src に 彩度・色相(HSBフィルタ) と コントラスト(レベル補正) をかけて dst へ入れる。
// 失敗したら false（以降は補正なしで描かれるだけで、ゲームは止まらない）。
inline bool ApplyFilters(Resources& r, int src, int dst, float sat, float hue, float contrast) {
    if (!r.filterOk || src < 0 || dst < 0) return false;
    bool hsb = fabsf(sat - 1.0f) > 0.01f || fabsf(hue) > 0.5f;
    bool lvl = fabsf(contrast) > 0.01f;
    if (!hsb && !lvl) return false;

    int cur = src;
    if (hsb) {
        // 彩度：1.0=そのまま、0=白黒、2=倍。HSBフィルタの彩度は -255(白黒)〜 の差分で渡す。
        int s255 = (int)((sat - 1.0f) * 255.0f);
        if (s255 < -255) s255 = -255;
        if (GraphFilterBlt(src, dst, DX_GRAPH_FILTER_HSB, 0, (int)hue, s255, 0) != 0) { r.filterOk = false; return false; }
        cur = dst;
    }
    if (lvl) {
        // コントラスト：+なら入力の暗い側・明るい側を切って引き伸ばし、-なら出力を灰色へ寄せる
        int m = (int)(fabsf(contrast) * 80.0f);
        int inMin = 0, inMax = 255, outMin = 0, outMax = 255;
        if (contrast > 0.0f) { inMin = m; inMax = 255 - m; }
        else                 { outMin = m; outMax = 255 - m; }
        int rc;
        if (cur == dst) rc = GraphFilter(dst, DX_GRAPH_FILTER_LEVEL, inMin, inMax, 100, outMin, outMax);
        else            rc = GraphFilterBlt(src, dst, DX_GRAPH_FILTER_LEVEL, inMin, inMax, 100, outMin, outMax);
        if (rc != 0) { r.filterOk = false; return false; }
    }
    return true;
}

// src を (mr,mg,mb) 倍して描く。
// SetDrawBright は255（＝等倍）で頭打ちなので、1.0を超える分（明るさ>1）は、
// 同じ絵をもう一度「加算合成」で重ねて足す。チャンネルごとに別の倍率でも正確に c×m になる。
// 元画像の範囲は「左上(sx1,sy1) と 幅・高さ(sw,sh)」で渡す（DxLib の DrawRectExtendGraph と同じ。右下の座標ではない）。
inline void DrawGraded(int dx1, int dy1, int dx2, int dy2, int sx1, int sy1, int sw, int sh,
                       int src, float mr, float mg, float mb) {
    auto clamp255 = [](float v) { return (int)(std::max)(0.0f, (std::min)(255.0f, v * 255.0f)); };
    SetDrawBright(clamp255((std::min)(1.0f, mr)), clamp255((std::min)(1.0f, mg)), clamp255((std::min)(1.0f, mb)));
    DrawRectExtendGraph(dx1, dy1, dx2, dy2, sx1, sy1, sw, sh, src, TRUE);
    if (mr > 1.0f || mg > 1.0f || mb > 1.0f) {
        SetDrawBlendMode(DX_BLENDMODE_ADD, 255);
        SetDrawBright(clamp255(mr - 1.0f), clamp255(mg - 1.0f), clamp255(mb - 1.0f));
        DrawRectExtendGraph(dx1, dy1, dx2, dy2, sx1, sy1, sw, sh, src, TRUE);
        SetDrawBlendMode(DX_BLENDMODE_NOBLEND, 0);
    }
    SetDrawBright(255, 255, 255);
}

// ---------------------------------------------------------------------------
// 4. パネル（右パネルの下半分）の配置・入力・描画
// ---------------------------------------------------------------------------
// スライダーの項目（0=明るさ 1=コントラスト 2=彩度 3=色相 4=色温度）。
// 明るさは「色と明るさ」タブの四角の縦で動かし、1〜4 は「細かい調整」タブのスライダーで動かす。
const int kSliderCount = 5;

struct SliderInfo { const char* label; float lo, hi, def, wheelStep; };
inline const SliderInfo& Slider(int i) {
    static const SliderInfo k[kSliderCount] = {
        { "明るさ",       kBrightMin, kBrightMax, 1.0f,  0.05f },
        { "コントラスト", -1.0f,      1.0f,       0.0f,  0.05f },
        { "彩度",         0.0f,       2.0f,       1.0f,  0.05f },
        { "色相",         -180.0f,    180.0f,     0.0f,  5.0f  },
        { "色温度",       -1.0f,      1.0f,       0.0f,  0.05f },
    };
    return k[i];
}
inline float& SliderValue(PanelState& s, int i) {
    switch (i) {
        case 0: return s.bright;
        case 1: return s.contrast;
        case 2: return s.saturation;
        case 3: return s.hue;
        default: return s.temperature;
    }
}
inline float SliderValueC(const PanelState& s, int i) { return SliderValue(const_cast<PanelState&>(s), i); }

// つかんでいる部品の番号（PanelUi::drag）。1〜4 は「細かい調整」のスライダーで、Slider(i) の添字そのまま
const int kDragRing   = 20;  // 色相の輪
const int kDragSquare = 21;  // 中の四角
// ダブルクリック判定用の「最後に押したフレーム」の置き場（PanelUi::lastPress の添字）
const int kPressRing = 5, kPressSquare = 6, kPressCount = 7;

// パネルの操作状態（開いているタブ、つかんでいる部品、ダブルクリック判定用の時刻、ボタンの押下表示）
struct PanelUi {
    int tab = 0;                     // 開いているタブ。0＝色と明るさ、1＝細かい調整（ステージを出入りしたら 0 に戻す）
    int drag = -1;                   // -1=なし 1〜4=細かい調整のスライダー 20=色相の輪 21=中の四角
    int frame = 0;                   // 通算フレーム（ダブルクリック判定用）
    int lastPress[kPressCount] = { -999, -999, -999, -999, -999, -999, -999 }; // 最後に押したフレーム（0〜4=スライダー 5=輪 6=四角）
    int flash = 0;                   // 「リセット」を押した直後の強調（フレーム）
};

struct PanelInput {
    int  mx = 0, my = 0;
    bool down = false;     // いま左ボタンが押されているか
    bool pressed = false;  // このフレームで押された瞬間か
    int  wheel = 0;        // ホイール回転量（上へ回すと正）
};

struct PanelResult {
    bool consumed = false; // このフレームのマウス入力をパネルが使った（背後の編集操作へ回さない）
    bool dragging = false; // つまみを握っている（握っている間は世界の更新を止める）
    bool changed = false;  // 値が変わった
};

// 色の輪を「赤・緑・青・なし」のどれかへ一気に合わせる（Tキーと同じ状態）
inline void SetQuickFilter(PanelState& s, int f) {
    if (f <= 0) { s.tintAmount = 0.0f; return; }
    s.tintHue = (f - 1) * 120.0f;
    s.tintAmount = 1.0f;
}

// ボタンなどの矩形（Update と Draw で同じ値を使う）
struct Rect { int x1, y1, x2, y2; };
inline bool Hit(const Rect& r, int x, int y) { return x >= r.x1 && x <= r.x2 && y >= r.y1 && y <= r.y2; }

// ---------------------------------------------------------------------------
// 4-A. 「画面全体」の枠（色相の輪＋中の四角）の配置と、マウス→値・値→色の計算
// ---------------------------------------------------------------------------
// 【なぜ作り直すか】
// テスターから「右下の色・明るさがごちゃごちゃして分かりにくい」と言われた。
// 前/後の帯・スライダー5本・小さな色の輪・ボタン4つ・数値3つ・注意書き2行が同じ重さで並び、
// ゲームの判定とコストに効く「明るさ」と「色フィルタ」がどれなのか見分けがつかなかった。
// そこで、ペイントソフトでおなじみの「色相の輪＋中の四角」1つに、効く2つの値をまとめる。
//   ・外の輪   … 色フィルタの色（tintHue）。向きは今までの色の輪と同じ（右＝赤0度、左上＝緑120度、左下＝青240度）
//   ・四角の横 … 色フィルタの強さ（tintAmount）。左端＝かけない、右端＝いっぱい
//   ・四角の縦 … 明るさ（bright）。下端＝0.3、上端＝1.7、真ん中＝1.0（ふつう）
// 判定に効かない4項目（コントラスト・彩度・色相・色温度）は「細かい調整」タブへ移す。
//
// ここに置くのは「どこが何か（配置）」と「マウスの位置→値」「値→色」の計算だけで、DxLib は呼ばない。
// 描画（Draw）と入力（Update）は、この配置と計算を同じように見ることで、見た目と当たり判定をずらさない。
// 座標はすべてウィンドウ座標（1280×720）。右端からの距離で決めるので、windowW を受け取る。

// 枠の外形（右パネルの下半分。上の「選んだ物」の枠は y=10〜286 で、その10px下から始まる）
inline int kSecX1(int w) { return w - 239; }   // 枠の左端x（1280 のとき 1041）
inline int kSecX2(int w) { return w - 9; }     // 枠の右端x（1280 のとき 1271）
const int kSecTop    = 296;                     // 枠の上端y
const int kSecBottom = 610;                     // 枠の下端y（下のタイムラインの帯は 620 から）
// 枠の中身の左右（枠の線から 8px 内側）
inline int kSecInX1(int w) { return kSecX1(w) + 8; }   // 1049
inline int kSecInX2(int w) { return kSecX2(w) - 8; }   // 1263
// 見出しの行（「画面全体」と、右端の「リセット」ボタン）
const int kHeadY = 304, kHeadH = 18;
inline Rect ResetButtonRect(int w) { return { kSecInX2(w) - 54, kHeadY, kSecInX2(w), kHeadY + kHeadH }; }
// タブの行（左＝色と明るさ、右＝細かい調整。2つの間は 4px あける）
const int kTabY = 328, kTabH = 22;
inline Rect TabRect(int w, int i) {
    const int mid = (kSecInX1(w) + kSecInX2(w)) / 2;
    return (i == 0) ? Rect{ kSecInX1(w), kTabY, mid - 2, kTabY + kTabH } : Rect{ mid + 2, kTabY, kSecInX2(w), kTabY + kTabH };
}
// 輪と四角を置く場所（y=356〜548 の 192px）。中心は枠の中身の左端から 106px
inline int kRingCX(int w) { return w - 125; }  // 1280 のとき 1155
const int kRingCY    = 452;
const int kRingOuter = 91;   // 色相の輪の外側の半径
const int kRingInner = 77;   // 色相の輪の内側の半径
const int kArcBand   = 8;    // 輪のすぐ外側の「判定の弧」を押せる幅（細い弧を、押しやすいよう太めに拾う）
static_assert(kRingOuter == kRingImgOuter && kRingInner == kRingImgInner, "色相の輪の画像と配置の半径をそろえる");
const int kSquare    = 104;  // 中の四角の一辺（内側の半径77の円に収まる大きさ）
const int kPickTop = 356, kPickBottom = 548;  // 輪と四角を置く場所の上端・下端
// 四角の左上（右下は +kSquare で、その座標自体は含まない）
inline int SquareLeft(int w) { return kRingCX(w) - kSquare / 2; }
inline int SquareTop()       { return kRingCY - kSquare / 2; }
// 四隅の文字（左上＝緑、右上＝赤、左下＝青）。押すと、その色にぴったり合わせる
inline Rect CornerLabelRect(int w, int c /* 0=赤 1=緑 2=青 */) {
    const int x1 = kSecInX1(w), x2 = kSecInX2(w);
    if (c == 0) return { x2 - 15, kPickTop, x2, kPickTop + 15 };
    if (c == 1) return { x1, kPickTop, x1 + 15, kPickTop + 15 };
    return { x1, kPickBottom - 15, x1 + 15, kPickBottom };
}
// 右下の2色（手前＝いまの色、後ろ＝元の白）。この34px四方のどこを押しても「元に戻す」
inline Rect SwatchRect(int w) { return { kSecInX2(w) - 34, kPickBottom - 34, kSecInX2(w), kPickBottom }; }
// 「細かい調整」タブのスライダー4本（コントラスト・彩度・色相・色温度）。1本ぶんは 40px、間隔 46px
const int kFineY0 = 362, kFinePitch = 46, kFineRowH = 40;
inline int FineTrackX1(int w) { return kSecInX1(w) + 4; }
inline int FineTrackX2(int w) { return kSecInX2(w) - 4; }

// マウスが「画面全体」の枠の中か（枠の線の上も含む）。枠の中のクリック・ホイールは背後の編集操作へ回さない
inline bool InPanel(int w, int mx, int my) {
    return mx >= kSecX1(w) && mx <= kSecX2(w) && my >= kSecTop && my <= kSecBottom;
}

// パネルのどの部品の上か。HitPart の結果で、Update はどの操作を始めるかを決める
enum PickPart {
    PICK_NONE,
    PICK_RING,          // 色相の輪
    PICK_SQUARE,        // 中の四角
    PICK_ARC_R, PICK_ARC_G, PICK_ARC_B,  // 判定の弧（と四隅の文字）
    PICK_SWATCH_RESET,  // 右下の2色（元に戻す）
    PICK_TAB_COLOR, PICK_TAB_FINE,       // タブ
    PICK_RESET_ALL,     // 見出しの「リセット」
    PICK_FINE0          // 細かい調整のスライダー。+0〜3 ＝ コントラスト・彩度・色相・色温度
};

// 輪の中心からの距離と角度（数学の向き：右＝0度、上＝90度）
inline float RingDist(int w, int mx, int my) {
    const float dx = (float)(mx - kRingCX(w)), dy = (float)(my - kRingCY);
    return sqrtf(dx * dx + dy * dy);
}
inline float RingAngle(int w, int mx, int my) {
    return WrapDeg(atan2f(-(float)(my - kRingCY), (float)(mx - kRingCX(w))) * 180.0f / kPi);
}

// マウスがパネルのどの部品の上か。tab＝いま開いているタブ（0＝色と明るさ、1＝細かい調整）。
// 調べる順番は「タブ→リセット→右下の2色→四角→輪→判定の弧・四隅の文字→細かい調整のスライダー」。
// 四角は輪より先に見る（四角の角は輪の内側の近くにあるので、四角を優先しないと角が押しにくい）。
inline PickPart HitPart(int tab, int w, int mx, int my) {
    if (Hit(TabRect(w, 0), mx, my)) return PICK_TAB_COLOR;
    if (Hit(TabRect(w, 1), mx, my)) return PICK_TAB_FINE;
    if (Hit(ResetButtonRect(w), mx, my)) return PICK_RESET_ALL;
    if (tab == 0) {
        if (Hit(SwatchRect(w), mx, my)) return PICK_SWATCH_RESET;
        if (mx >= SquareLeft(w) && mx < SquareLeft(w) + kSquare && my >= SquareTop() && my < SquareTop() + kSquare) return PICK_SQUARE;
        const float d = RingDist(w, mx, my);
        if (d >= (float)(kRingInner - 2) && d <= (float)(kRingOuter + 1)) return PICK_RING;
        if (d > (float)(kRingOuter + 1) && d <= (float)(kRingOuter + kArcBand)) {
            // 判定の弧：赤0度・緑120度・青240度それぞれ ±35度（kFilterZoneDeg）の範囲だけ
            const float a = RingAngle(w, mx, my);
            const float centers[3] = { 0.0f, 120.0f, 240.0f };
            for (int i = 0; i < 3; i++) {
                float dd = fabsf(a - centers[i]);
                if (dd > 180.0f) dd = 360.0f - dd;
                if (dd <= kFilterZoneDeg) return (PickPart)(PICK_ARC_R + i);
            }
        }
        for (int c = 0; c < 3; c++) if (Hit(CornerLabelRect(w, c), mx, my)) return (PickPart)(PICK_ARC_R + c);
        return PICK_NONE;
    }
    // 細かい調整のタブ：スライダーの行（ラベルの行と溝を合わせた 40px）
    if (mx >= kSecInX1(w) && mx <= kSecInX2(w)) {
        for (int k = 0; k < 4; k++) {
            const int y = kFineY0 + k * kFinePitch;
            if (my >= y && my < y + kFineRowH) return (PickPart)(PICK_FINE0 + k);
        }
    }
    return PICK_NONE;
}

// 輪の上のマウス位置から色相を決める（強さには触らない）
inline void HueFromMouse(PanelState& s, int mx, int my, int w) {
    s.tintHue = RingAngle(w, mx, my);
}

// 四角の上のマウス位置から、強さ（横）と明るさ（縦）を同時に決める。
// 四角の外へはみ出しても、端の値で止める（つかんだまま枠の外へ出たときのため）。
// 「かけていない」状態へ戻しやすいよう、強さ0.04未満は0、明るさ1.0±0.035は1.0へ吸い付かせる
// （0.035は縦の幅の2.5%。今までのスライダーの吸い付きと同じ割合）。
inline void SquareFromMouse(PanelState& s, int mx, int my, int w) {
    float tx = (float)(mx - SquareLeft(w)) / (float)(kSquare - 1);
    float ty = (float)(my - SquareTop()) / (float)(kSquare - 1);
    tx = (std::max)(0.0f, (std::min)(1.0f, tx));
    ty = (std::max)(0.0f, (std::min)(1.0f, ty));
    float amount = tx;
    float bright = kBrightMax - (kBrightMax - kBrightMin) * ty;
    if (amount < 0.04f) amount = 0.0f;
    if (fabsf(bright - 1.0f) <= 0.035f) bright = 1.0f;
    s.tintAmount = amount;
    s.bright = bright;
}

// 値から四角の上の位置（つまみを描く場所）を求める。SquareFromMouse の逆
inline void SquarePos(float amount, float bright, int w, int& x, int& y) {
    const float tx = (std::max)(0.0f, (std::min)(1.0f, amount));
    const float ty = (std::max)(0.0f, (std::min)(1.0f, (kBrightMax - bright) / (kBrightMax - kBrightMin)));
    x = SquareLeft(w) + (int)lroundf(tx * (float)(kSquare - 1));
    y = SquareTop() + (int)lroundf(ty * (float)(kSquare - 1));
}

// 四角の右端の色＝その色相で「強さいっぱい」にしたとき画面に掛かる乗算の色（色温度は含めない）。
// 四角の塗りは、左端の白からこの色へ横に変わる。
inline void SquareEdgeColor(float tintHue, float& r, float& g, float& b) {
    PanelState t;
    t.tintHue = tintHue;
    t.tintAmount = 1.0f;
    TintMultiplier(t, r, g, b);
}

// いま画面に掛かっている色（右下の手前の色）。色フィルタ・色温度の乗算に、明るさを掛ける。
// 明るさが1を超える分は「白より明るい色」になって描けないので、白へ寄せて薄くした色で表す
// （1.7 のとき、残りの半分だけ白へ寄せる）。
inline void NowColor(const PanelState& s, float& r, float& g, float& b) {
    TintMultiplier(s, r, g, b);
    const float br = s.bright;
    if (br <= 1.0f) {
        r *= br; g *= br; b *= br;
    } else {
        const float k = (br - 1.0f) / (kBrightMax - 1.0f) * 0.5f;
        r += (1.0f - r) * k; g += (1.0f - g) * k; b += (1.0f - b) * k;
    }
}

// 「細かい調整」のスライダー i（1〜4。Slider(i) の添字）の値を、マウスのX座標から決める。
// 溝の左端＝下限、右端＝上限。初期値の近く（幅の2.5%）ではぴたりと初期値へ吸い付く。色相は1度刻み。
inline float FineSliderFromX(int i, int mx, int w) {
    const SliderInfo& si = Slider(i);
    float t = (float)(mx - FineTrackX1(w)) / (float)(FineTrackX2(w) - FineTrackX1(w));
    t = (std::max)(0.0f, (std::min)(1.0f, t));
    float v = si.lo + (si.hi - si.lo) * t;
    if (fabsf(v - si.def) < (si.hi - si.lo) * 0.025f) v = si.def;
    if (i == 3) v = roundf(v);
    return v;
}

// 値が1つでも変わったか（Update の「changed」を正しく返すため）
inline bool SameValues(const PanelState& a, const PanelState& b) {
    return a.bright == b.bright && a.contrast == b.contrast && a.saturation == b.saturation && a.hue == b.hue
        && a.temperature == b.temperature && a.tintHue == b.tintHue && a.tintAmount == b.tintAmount;
}

// 入力を処理して PanelState を書き換える。locked＝このステージでは画面エフェクトが封印されている。
// 流れは今までと同じ：
//   1. つかんでいる部品があれば、離すまで動かし続ける（枠の外へ出てもつかんだまま）
//   2. 枠の外のマウスは相手にしない（背後の編集操作へ回す）
//   3. ホイール：乗っている部品を少しずつ動かす（四角＝明るさ、輪＝色相、スライダー＝その値）
//   4. 押した瞬間：押した部品の操作を始める
// 部品の場所は HitPart（4-A章）で決める。描画も同じ配置を見るので、見た目と当たり判定がずれない。
inline PanelResult Update(PanelState& st, PanelUi& ui, const PanelInput& in, bool locked, int windowW) {
    PanelResult res;
    ui.frame++;
    if (ui.flash > 0) ui.flash--;
    const int w = windowW;
    const PanelState before = st;

    // 1. ドラッグ中の更新
    if (ui.drag >= 0) {
        if (!in.down || locked) {
            ui.drag = -1;
        } else {
            res.consumed = true;
            res.dragging = true;
            if (ui.drag == kDragRing)        HueFromMouse(st, in.mx, in.my, w);
            else if (ui.drag == kDragSquare) SquareFromMouse(st, in.mx, in.my, w);
            else if (ui.drag >= 1 && ui.drag < kSliderCount) SliderValue(st, ui.drag) = FineSliderFromX(ui.drag, in.mx, w);
            res.changed = !SameValues(before, st);
            return res;
        }
    }

    // 2. 枠の外は相手にしない
    if (!InPanel(w, in.mx, in.my)) return res;

    // 枠の上でのクリック・ホイールは、背後の編集操作（物の選択・ホイール拡縮・射撃）へ回さない。
    // 封印されたステージでも「押したこと」は使い切る（押した先の物が選ばれてしまわないように）
    if (in.pressed || in.wheel != 0) res.consumed = true;
    if (locked) return res;

    const PickPart part = HitPart(ui.tab, w, in.mx, in.my);

    // 3. ホイール：乗っている部品を微調整する
    if (in.wheel != 0) {
        if (part == PICK_SQUARE) {
            // 四角の上は明るさ（縦の軸）。1.0 を通り過ぎるときは 1.0 でぴたりと止める
            float nv = (std::max)(kBrightMin, (std::min)(kBrightMax, st.bright + 0.05f * (float)in.wheel));
            if (fabsf(nv - 1.0f) < 0.025f) nv = 1.0f;
            st.bright = nv;
        } else if (part == PICK_RING) {
            // 輪の上は色相を 5 度ずつ回す
            st.tintHue = WrapDeg(st.tintHue + 5.0f * (float)in.wheel);
        } else if (part >= PICK_FINE0) {
            const int i = (int)(part - PICK_FINE0) + 1;
            const SliderInfo& si = Slider(i);
            float nv = (std::max)(si.lo, (std::min)(si.hi, SliderValue(st, i) + si.wheelStep * (float)in.wheel));
            if (fabsf(nv - si.def) < si.wheelStep * 0.5f) nv = si.def;
            SliderValue(st, i) = nv;
        }
        res.changed = !SameValues(before, st);
        return res;
    }

    if (!in.pressed) return res;

    // 4. 押した瞬間
    switch (part) {
        case PICK_TAB_COLOR: ui.tab = 0; return res;
        case PICK_TAB_FINE:  ui.tab = 1; return res;
        case PICK_RESET_ALL:
            // 見出しの「リセット」：細かい調整も含めて、全部を初期値へ
            Reset(st); ui.flash = 12;
            break;
        case PICK_SWATCH_RESET:
            // 右下の2色：明るさと色フィルタだけ元に戻す（細かい調整はそのまま）
            st.bright = 1.0f; st.tintAmount = 0.0f;
            break;
        case PICK_ARC_R: case PICK_ARC_G: case PICK_ARC_B:
            // 判定の弧・四隅の文字：その色に、強さいっぱいでぴったり合わせる（Tキーと同じ状態）
            SetQuickFilter(st, (int)(part - PICK_ARC_R) + 1);
            break;
        case PICK_SQUARE:
            // 短い間隔の2回押しは、明るさと強さを初期値へ
            if (ui.frame - ui.lastPress[kPressSquare] <= 20) {
                st.bright = 1.0f; st.tintAmount = 0.0f; ui.lastPress[kPressSquare] = -999;
                break;
            }
            ui.lastPress[kPressSquare] = ui.frame;
            ui.drag = kDragSquare;
            SquareFromMouse(st, in.mx, in.my, w);
            res.dragging = true;
            break;
        case PICK_RING:
            // 短い間隔の2回押しは、色フィルタを外す（今までの色の輪と同じ）
            if (ui.frame - ui.lastPress[kPressRing] <= 20) {
                st.tintAmount = 0.0f; ui.lastPress[kPressRing] = -999;
                break;
            }
            ui.lastPress[kPressRing] = ui.frame;
            ui.drag = kDragRing;
            HueFromMouse(st, in.mx, in.my, w);
            // 色フィルタをかけていないときに輪を押したら、強さをいっぱいにする
            // （色を選んだのに何も起きない、を作らない。強さは四角の横で弱められる）
            if (!TintActive(st)) st.tintAmount = 1.0f;
            res.dragging = true;
            break;
        default:
            if (part >= PICK_FINE0) {
                const int i = (int)(part - PICK_FINE0) + 1;
                const int rowTop = kFineY0 + (i - 1) * kFinePitch;
                if (ui.frame - ui.lastPress[i] <= 20) {
                    SliderValue(st, i) = Slider(i).def; ui.lastPress[i] = -999;
                    break;
                }
                ui.lastPress[i] = ui.frame;
                // ラベルの行（上の14px）を押しても値は飛ばさない。値を動かすのは溝の高さから
                // （ダブルクリックでの初期値戻しは、ラベルの上でも効く＝上の分岐）
                if (in.my < rowTop + 14) return res;
                ui.drag = i;
                SliderValue(st, i) = FineSliderFromX(i, in.mx, w);
                res.dragging = true;
            }
            break;
    }
    res.changed = !SameValues(before, st);
    return res;
}

// ---- 描画 ----
// 色と描き方は、見た目のテーマ（UiTheme.h、2000年代の編集ソフト風）から取る。
// 文字はすべて UiFont（サイズ別のフォントハンドル）で描く。SetFontSize を毎フレーム呼ぶと極端に重くなるため。

// スライダーの値を、画面に出す文字にする（0=明るさ% 1=コントラスト± 2=彩度% 3=色相の度 4=色温度±）
inline void FormatValue(int i, float v, char* buf, size_t n) {
    switch (i) {
        case 0: sprintf_s(buf, n, "%d%%", (int)roundf(v * 100.0f)); break;
        case 1: sprintf_s(buf, n, "%+d", (int)roundf(v * 100.0f)); break;
        case 2: sprintf_s(buf, n, "%d%%", (int)roundf(v * 100.0f)); break;
        case 3: sprintf_s(buf, n, "%+d度", (int)roundf(v)); break;
        default: sprintf_s(buf, n, "%+d", (int)roundf(v * 100.0f)); break;
    }
}

// 点線（3px 描いて 3px あける）。a＝不透明度 0〜255
inline void DashH(int x1, int x2, int y, unsigned int col, int a) {
    SetDrawBlendMode(DX_BLENDMODE_ALPHA, a);
    for (int x = x1; x < x2; x += 6) DrawLine(x, y, (std::min)(x + 3, x2), y, col);
    SetDrawBlendMode(DX_BLENDMODE_NOBLEND, 0);
}
inline void DashV(int x, int y1, int y2, unsigned int col, int a) {
    SetDrawBlendMode(DX_BLENDMODE_ALPHA, a);
    for (int y = y1; y < y2; y += 6) DrawLine(x, y, x, (std::min)(y + 3, y2), col);
    SetDrawBlendMode(DX_BLENDMODE_NOBLEND, 0);
}

// 頂点ごとに色を付けた四角形を1枚描く（左上・右上・左下・右下の色。透明度つき）。
// 色の変わり方が位置について一次式なら、頂点の色だけで正確に塗れる（画像を作り直さなくてよい）。
inline void DrawGradQuad(float x1, float y1, float x2, float y2, COLOR_U8 tl, COLOR_U8 tr, COLOR_U8 bl, COLOR_U8 br) {
    VERTEX2D v[6];
    auto Set = [](VERTEX2D& p, float x, float y, COLOR_U8 c) { p.pos = VGet(x, y, 0.0f); p.rhw = 1.0f; p.dif = c; p.u = 0.0f; p.v = 0.0f; };
    Set(v[0], x1, y1, tl); Set(v[1], x2, y1, tr); Set(v[2], x1, y2, bl);
    Set(v[3], x2, y1, tr); Set(v[4], x2, y2, br); Set(v[5], x1, y2, bl);
    DrawPrimitive2D(v, 6, DX_PRIMTYPE_TRIANGLELIST, DX_NONE_GRAPH, TRUE);
}

// 「色と明るさ」タブの中身：へこんだ黒い窓の中に、判定の弧・色相の輪・中の四角・四隅の文字・右下の2色
inline void DrawColorTab(const PanelState& st, Resources& res, int w, float briPreview) {
    const int cx = kRingCX(w), cy = kRingCY;
    const unsigned int zoneCols[3] = { GetColor(230, 60, 60), GetColor(60, 190, 60), GetColor(70, 110, 240) };

    // 窓：へこんだ黒い窓（当時の色選びの窓・プレビューの枠）。輪の内側の透けた所からもこの地が見える
    UiTheme::Sunken({ kSecInX1(w), kPickTop, kSecInX2(w), kPickBottom }, UiTheme::P().well);

    // 判定の弧：輪のすぐ外に、赤・緑・青それぞれ ±35度 の範囲を太い弧で描く（押すとその色に合わせる）
    {
        const float centers[3] = { 0.0f, 120.0f, 240.0f };
        const float r0 = (float)kRingOuter + 3.5f;
        for (int c = 0; c < 3; c++) {
            for (float a = centers[c] - kFilterZoneDeg; a < centers[c] + kFilterZoneDeg; a += 4.0f) {
                const float a2 = (std::min)(a + 4.0f, centers[c] + kFilterZoneDeg);
                DrawLineAA(cx + cosf(a * kPi / 180.0f) * r0, cy - sinf(a * kPi / 180.0f) * r0,
                           cx + cosf(a2 * kPi / 180.0f) * r0, cy - sinf(a2 * kPi / 180.0f) * r0, zoneCols[c], 4.0f);
            }
        }
    }
    // 色相の輪（起動時に作った画像）と、いまの色相の位置のつまみ（帯の真ん中の半径。黒と白の二重の輪）
    if (res.ring >= 0) DrawGraph(cx - (kRingImgOuter + 1), cy - (kRingImgOuter + 1), res.ring, TRUE);
    {
        const float rm = ((float)kRingOuter + (float)kRingInner) * 0.5f;
        const float a = st.tintHue * kPi / 180.0f;
        const float px = cx + cosf(a) * rm, py = cy - sinf(a) * rm;
        DrawCircleAA(px, py, 6.0f, 16, GetColor(0, 0, 0), FALSE, 3.5f);
        DrawCircleAA(px, py, 6.0f, 16, GetColor(255, 255, 255), FALSE, 2.0f);
    }

    // 中の四角：横＝色フィルタの強さ、縦＝明るさ。塗りは「その点を選んだら画面に掛かる色」。
    //   1枚目：左の2頂点＝白、右の2頂点＝その色相で強さいっぱいの乗算の色
    //   2枚目：下半分に黒を重ねる（明るさ1.0の高さで透明 → 下端の0.3で不透明度0.7）
    //   3枚目：上半分に白を重ねる（明るさ1.0の高さで透明 → 上端の1.7で半透明）。白より明るい色は描けないので「白っぽさ」で表す
    {
        float er, eg, eb;
        SquareEdgeColor(st.tintHue, er, eg, eb);
        const COLOR_U8 white = GetColorU8(255, 255, 255, 255);
        const COLOR_U8 edge  = GetColorU8((int)(er * 255.0f), (int)(eg * 255.0f), (int)(eb * 255.0f), 255);
        const float L = (float)SquareLeft(w), T = (float)SquareTop();
        const float R = L + (float)kSquare, B = T + (float)kSquare;
        const float mid = T + (kBrightMax - 1.0f) / (kBrightMax - kBrightMin) * (float)(kSquare - 1) + 0.5f;
        SetDrawBlendMode(DX_BLENDMODE_NOBLEND, 0);
        DrawGradQuad(L, T, R, B, white, edge, white, edge);
        SetDrawBlendMode(DX_BLENDMODE_ALPHA, 255);
        DrawGradQuad(L, mid, R, B, GetColorU8(0, 0, 0, 0), GetColorU8(0, 0, 0, 0), GetColorU8(0, 0, 0, 179), GetColorU8(0, 0, 0, 179));
        DrawGradQuad(L, T, R, mid, GetColorU8(255, 255, 255, 128), GetColorU8(255, 255, 255, 128), GetColorU8(255, 255, 255, 0), GetColorU8(255, 255, 255, 0));
        SetDrawBlendMode(DX_BLENDMODE_NOBLEND, 0);
        DrawBox((int)L - 1, (int)T - 1, (int)R + 1, (int)B + 1, GetColor(0, 0, 0), FALSE);   // 四角の黒い縁

        // 判定の境目：横の点線＝明るさ 1.3（上）と 0.6（下）、縦の点線＝強さ 0.5
        const int li = (int)L, ti = (int)T, ri = (int)R;
        const int yBright = ti + (int)lroundf((kBrightMax - kBrightAbove) / (kBrightMax - kBrightMin) * (float)(kSquare - 1));
        const int yDark   = ti + (int)lroundf((kBrightMax - kDarkBelow) / (kBrightMax - kBrightMin) * (float)(kSquare - 1));
        const int xHalf   = li + (int)lroundf(kFilterMinAmount * (float)(kSquare - 1));
        DashH(li, ri, yBright, GetColor(20, 20, 24), 170);
        DashH(li, ri, yDark, GetColor(255, 255, 255), 190);
        DashV(xHalf, ti, ti + kSquare, GetColor(110, 110, 120), 220);
        UiFont::Draw(li + 3, ti + 2, "明るい", GetColor(58, 52, 40), 10);
        UiFont::Draw(li + 3, ti + kSquare - 13, "暗い", GetColor(216, 222, 240), 10);

        // いまの値のつまみ（黒と白の二重の輪）。X / C を押している間は、その高さに薄いつまみも出す
        if (briPreview >= 0.0f) {
            int gx, gy;
            SquarePos(st.tintAmount, briPreview, w, gx, gy);
            SetDrawBlendMode(DX_BLENDMODE_ALPHA, 140);
            DrawCircleAA((float)gx, (float)gy, 6.0f, 16, GetColor(255, 255, 255), FALSE, 2.0f);
            SetDrawBlendMode(DX_BLENDMODE_NOBLEND, 0);
        }
        int kx, ky;
        SquarePos(st.tintAmount, st.bright, w, kx, ky);
        DrawCircleAA((float)kx, (float)ky, 6.0f, 16, GetColor(0, 0, 0), FALSE, 3.5f);
        DrawCircleAA((float)kx, (float)ky, 6.0f, 16, GetColor(255, 255, 255), FALSE, 2.0f);
    }

    // 四隅の文字（押すとその色に合わせる）。黒い窓の上なので、明るい色の文字
    {
        const char* names[3] = { "赤", "緑", "青" };
        const unsigned int cols[3] = { GetColor(255, 138, 138), GetColor(111, 216, 111), GetColor(143, 168, 255) };
        for (int c = 0; c < 3; c++) {
            const Rect r = CornerLabelRect(w, c);
            UiFont::Draw(r.x1 + 3, r.y1 + 3, names[c], cols[c], 12);
        }
    }

    // 右下の2色（ペイントソフトの描画色・背景色の四角）：後ろ＝元の色（白）、手前＝いま画面に掛かっている色。
    // どちらを押しても明るさと色フィルタを元に戻す
    {
        const Rect s = SwatchRect(w);
        DrawBox(s.x2 - 19, s.y2 - 19, s.x2 - 2, s.y2 - 2, GetColor(255, 255, 255), TRUE);
        DrawBox(s.x2 - 19, s.y2 - 19, s.x2 - 2, s.y2 - 2, GetColor(0, 0, 0), FALSE);
        float nr, ng, nb;
        NowColor(st, nr, ng, nb);
        DrawBox(s.x1 + 2, s.y1 + 2, s.x1 + 23, s.y1 + 23, GetColor((int)(nr * 255.0f), (int)(ng * 255.0f), (int)(nb * 255.0f)), TRUE);
        DrawBox(s.x1 + 2, s.y1 + 2, s.x1 + 23, s.y1 + 23, GetColor(0, 0, 0), FALSE);
        DrawBox(s.x1 + 1, s.y1 + 1, s.x1 + 24, s.y1 + 24, GetColor(160, 160, 160), FALSE);
    }
}

// 「細かい調整」タブの中身：コントラスト・彩度・色相・色温度のスライダー4本。
// 溝はへこんだ細い窓に色の帯（動かすとどうなるかを見せる）、つまみは盛り上がった小さなボタン、値はドラッグで変える数字
inline void DrawFineTab(const PanelState& st, int w) {
    const UiTheme::Palette& tp = UiTheme::P();
    const int tx0 = FineTrackX1(w), tx1 = FineTrackX2(w);
    for (int k = 0; k < 4; k++) {
        const int i = k + 1;                     // Slider(i) の添字（1=コントラスト 2=彩度 3=色相 4=色温度）
        const SliderInfo& si = Slider(i);
        const int rowTop = kFineY0 + k * kFinePitch;
        const float v = SliderValueC(st, i);
        const bool changed = fabsf(v - si.def) > (si.hi - si.lo) * 0.004f;

        UiFont::Draw(kSecInX1(w) + 4, rowTop, si.label, UiTheme::Col(changed ? tp.ink : tp.inkSub), 12);
        char vb[24];
        FormatValue(i, v, vb, sizeof(vb));
        UiTheme::HotText(kSecInX2(w) - 4 - UiFont::Width(vb, 12), rowTop, vb, 12);

        const int ty = rowTop + 20;
        UiTheme::Sunken({ tx0 - 2, ty - 2, tx1 + 2, ty + 9 }, tp.well);
        for (int x = tx0; x < tx1; x++) {
            const float t = (float)(x - tx0) / (float)(tx1 - tx0);
            int cr = 120, cg = 120, cb = 120;
            if (i == 1) { int g = (int)(150 - 70 * fabsf(t * 2.0f - 1.0f)); cr = cg = cb = g; }  // 中央が灰色、両端ほど濃い
            else if (i == 2) { cr = (int)(130 + 125 * t); cg = (int)(130 - 80 * t); cb = (int)(130 - 80 * t); }
            else if (i == 3) { float fr, fg, fb; HsvToRgb(t * 360.0f, 0.75f, 1.0f, fr, fg, fb); cr = (int)(fr * 255); cg = (int)(fg * 255); cb = (int)(fb * 255); }
            else { cr = (int)(90 + 165 * t); cg = (int)(150 + 20 * t); cb = (int)(255 - 185 * t); }
            DrawLine(x, ty, x, ty + 7, GetColor(cr, cg, cb));
        }
        // 初期値の目印（溝の下の小さな刻み）
        const int dx = tx0 + (int)((si.def - si.lo) / (si.hi - si.lo) * (float)(tx1 - tx0));
        DrawLine(dx, ty + 11, dx, ty + 14, UiTheme::Col(tp.ink));
        // つまみ：盛り上がった小さなボタン
        const int kx = tx0 + (int)((v - si.lo) / (si.hi - si.lo) * (float)(tx1 - tx0));
        UiTheme::Raised({ kx - 4, ty - 5, kx + 4, ty + 12 });
    }
}

// 「画面全体」の枠を丸ごと描く。呼び出し側は SetDrawScreen(DX_SCREEN_BACK) の状態で、右パネルの地を描いたあとに呼ぶ。
//   locked        … このステージでは画面エフェクトが封印されている（中身の代わりに、そう書く）
//   stageUsesFine … このステージに、コントラスト・彩度・色相・色温度を見るギミックがある（タブに「使う」の札）
//   briPreview    … X / C を押している間の明るさ（0.3 / 1.7）。押していなければ負の値
// 枠の作り（2000年代の編集ソフト風）：
//   暗い灰色 … 立体の縁のページの左上に太字の「画面全体」　　明るい灰色 … グループ枠「画面全体の色と明るさ」
//   その中に、リセットのボタン・タブ（色と明るさ｜細かい調整）・タブの中身・数値・判定の行
inline void Draw(const PanelState& st, const PanelUi& ui, Resources& res, bool locked, int windowW,
                 bool stageUsesFine, float briPreview) {
    const int w = windowW;
    const UiTheme::Palette& tp = UiTheme::P();
    const bool dark = (UiTheme::Current() == UiTheme::DARK);
    const int x1 = kSecX1(w), x2 = kSecX2(w), ix1 = kSecInX1(w), ix2 = kSecInX2(w);

    // 枠
    if (dark) {
        UiTheme::Fill({ x1, kSecTop, x2, kSecBottom }, tp.faceIn);
        UiTheme::Bevel({ x1, kSecTop, x2, kSecBottom }, tp.hilite, tp.dark, tp.light, tp.shadow);
        UiFont::DrawBold(ix1, kHeadY + 3, "画面全体", UiTheme::Col(tp.ink), 12);
    } else {
        UiTheme::GroupBox({ x1, kSecTop + 4, x2, kSecBottom }, "画面全体の色と明るさ");
    }
    // リセット：盛り上がったボタン（押した直後の少しの間は押し込まれたボタン）
    {
        const Rect rb = ResetButtonRect(w);
        const UiTheme::Box b = { rb.x1, rb.y1, rb.x2, rb.y2 };
        if (ui.flash > 0) UiTheme::Pressed(b); else UiTheme::Raised(b);
        const int tw = UiFont::Width("リセット", 12);
        UiFont::Draw((rb.x1 + rb.x2 + 1 - tw) / 2, UiTheme::TextTop(b, 12), "リセット",
                     UiTheme::Col((ui.flash > 0 && dark) ? tp.selInk : tp.ink), 12);
    }

    // タブ：開いているほうが手前。名前の右の小さな札で、コストがかかるかどうかを見せる
    for (int i = 0; i < 2; i++) {
        const Rect tr = TabRect(w, i);
        const bool active = (ui.tab == i);
        UiTheme::Tab({ tr.x1, tr.y1, tr.x2, tr.y2 }, "", active);
        const char* label = (i == 0) ? "色と明るさ" : "細かい調整";
        // 札：色と明るさ＝「コスト」。細かい調整＝ふだんは「無料」、このステージのギミックが見ているときは「使う」
        const bool useBadge = (i == 1 && stageUsesFine);
        const char* badge = (i == 0) ? "コスト" : (useBadge ? "使う" : "無料");
        const int lw = active ? UiFont::WidthBold(label, 12) : UiFont::Width(label, 12);
        const int bw = UiFont::Width(badge, 10) + 6;
        const int sx = (tr.x1 + tr.x2 - (lw + 4 + bw)) / 2;
        const int ty = active ? UiTheme::TextTop({ tr.x1, tr.y1, tr.x2, tr.y2 }, 12) : UiTheme::TextTop({ tr.x1, tr.y1 + 2, tr.x2, tr.y2 }, 12);
        if (active) UiFont::DrawBold(sx, ty, label, UiTheme::Col(tp.ink), 12);
        else        UiFont::Draw(sx, ty, label, UiTheme::Col(tp.inkSub), 12);
        const UiTheme::Box bb = { sx + lw + 4, ty, sx + lw + 4 + bw, ty + 12 };
        if (useBadge) {
            UiTheme::Fill(bb, tp.sel);
            UiFont::Draw(bb.x1 + 3, bb.y1 + 1, badge, UiTheme::Col(tp.selInk), 10);
        } else {
            const UiTheme::Rgb bc = (i == 0) ? tp.warn : tp.inkSub;
            DrawBox(bb.x1, bb.y1, bb.x2 + 1, bb.y2 + 1, UiTheme::Col(bc), FALSE);
            UiFont::Draw(bb.x1 + 3, bb.y1 + 1, badge, UiTheme::Col(bc), 10);
        }
    }

    if (locked) {
        // 封印されているステージ：中身の代わりに、錠前の絵と、使えないことを書く
        const char* l1 = "このステージでは";
        const char* l2 = "画面エフェクトが封印されています";
        UiTheme::Sunken({ ix1, kPickTop, ix2, kPickBottom }, tp.faceIn);
        UiFont::Draw((x1 + x2 - UiFont::Width(l1, 12)) / 2, 440, l1, UiTheme::Col(tp.inkSub), 12);
        UiFont::Draw((x1 + x2 - UiFont::Width(l2, 12)) / 2, 458, l2, UiTheme::Col(tp.inkSub), 12);
        return;
    }

    if (ui.tab == 0) DrawColorTab(st, res, w, briPreview);
    else             DrawFineTab(st, w);

    // いまの値（色相・強さ・明るさ）の行。ここは読むだけの数字なので、ドラッグの印（点線の下線）は付けない
    {
        char b1[24], b2[24], b3[24];
        sprintf_s(b1, sizeof(b1), "%d°", (int)roundf(WrapDeg(st.tintHue)));
        sprintf_s(b2, sizeof(b2), "%d%%", (int)roundf(st.tintAmount * 100.0f));
        sprintf_s(b3, sizeof(b3), "%d%%", (int)roundf(st.bright * 100.0f));
        int x = ix1 + 2;
        const int y = kPickBottom + 7;
        auto Pair = [&](const char* lab, const char* val) {
            UiFont::Draw(x, y, lab, UiTheme::Col(tp.inkSub), 12);
            x += UiFont::Width(lab, 12) + 4;
            UiFont::Draw(x, y, val, UiTheme::Col(tp.ink), 12);
            x += UiFont::Width(val, 12) + 12;
        };
        Pair("色相", b1); Pair("強さ", b2); Pair("明るさ", b3);
    }
    // ゲームがいま何と判定しているか（浅くへこんだ1行）
    {
        const int jb = JudgeBright(st.bright);
        const int jf = JudgeFilter(st);
        const char* bt = jb < 0 ? "暗い" : jb > 0 ? "明るい" : "ふつう";
        const char* ft[4] = { "なし", "赤", "緑", "青" };
        char line[96];
        sprintf_s(line, sizeof(line), "判定：明るさ %s ／ フィルタ %s", bt, ft[jf]);
        UiTheme::StatusPane({ ix1, kPickBottom + 26, ix2, kPickBottom + 45 }, line);
    }
}

} // namespace ColorGrade
