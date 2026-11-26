#pragma once
// ============================================================================
// ColorGrade.h - 「カラー補正パネル」と、画面に掛ける色補正の計算・描画ヘルパー
//
// 【なぜ作ったか】
// これまでの画面の明るさ・色の操作は「X長押し＝固定の暗転(0.3)」「C長押し＝固定の明転(1.7)」
// 「Tキー＝赤→緑→青の3段階」だけで、どれだけ暗い／どの色になるのかを自分で決められなかった。
// しかも「明転(1.7)」は SetDrawBright が255で頭打ちになるため、見た目には何も変わっていなかった。
// ここでは動画編集ソフト（Premiere の Lumetri カラー、Photoshop の色調補正）に近い操作を用意する。
//   ・スライダー … 明るさ／コントラスト／彩度／色相／色温度（ダブルクリックで初期値へ戻る）
//   ・色の輪   … 色フィルタの色（角度）と強さ（中心からの距離）を、輪の上でつまんで決める
//   ・テストチャート … 灰色の階調と虹色の帯を「補正前／補正後」で並べ、いま何が起きているかを見せる
//   ・ゲームの判定の表示 … 「いま暗い判定」「いま赤フィルタ判定」を文字で出す
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
// 2. 画像（色の輪・テストチャート・補正の作業用画面）
// ---------------------------------------------------------------------------
const int kWheelR  = 44;                 // 色の輪の半径(px)
const int kChartW  = 200, kChartH = 16;  // テストチャートの大きさ（上半分＝灰色の階調、下半分＝虹色）

struct Resources {
    int  wheel     = -1;   // 色の輪の画像
    int  chartSrc  = -1;   // テストチャートの元画像
    int  chartDst  = -1;   // 補正をかけたあとのテストチャート
    int  workScreen = -1;  // ゲーム画面にコントラスト・彩度・色相をかけた結果の置き場
    bool filterOk  = true; // GraphFilter が失敗したら false。以降は補正なしで描く（落とさない）
};

inline void CreateResources(Resources& r, int screenW, int screenH) {
    // 色の輪：中心＝白（色を掛けない）、縁＝その色相の純色
    int D = kWheelR * 2 + 2;
    int si = MakeARGB8ColorSoftImage(D, D);
    if (si >= 0) {
        float c = (float)D * 0.5f;
        for (int y = 0; y < D; y++) {
            for (int x = 0; x < D; x++) {
                float dx = (float)x + 0.5f - c, dy = (float)y + 0.5f - c;
                float rr = sqrtf(dx * dx + dy * dy);
                if (rr > (float)kWheelR + 0.5f) continue;
                float hue = atan2f(-dy, dx) * 180.0f / kPi; // 右が0度、上が90度（数学の向き）
                float sat = (std::min)(1.0f, rr / (float)kWheelR);
                float cr, cg, cb;
                HsvToRgb(hue, sat, 1.0f, cr, cg, cb);
                float edge = (std::min)(1.0f, (std::max)(0.0f, ((float)kWheelR + 0.5f - rr)));
                DrawPixelSoftImage(si, x, y, (int)(cr * 255.0f), (int)(cg * 255.0f), (int)(cb * 255.0f), (int)(edge * 255.0f));
            }
        }
        r.wheel = CreateGraphFromSoftImage(si);
        DeleteSoftImage(si);
    }

    // テストチャート：上半分は黒→白の灰色階調、下半分は左から右へ虹色（色相0〜360度）
    r.chartSrc = MakeScreen(kChartW, kChartH, TRUE);
    r.chartDst = MakeScreen(kChartW, kChartH, TRUE);
    if (r.chartSrc >= 0) {
        int prev = GetDrawScreen();
        SetDrawScreen(r.chartSrc);
        ClearDrawScreen();
        for (int x = 0; x < kChartW; x++) {
            int g = x * 255 / (kChartW - 1);
            DrawBox(x, 0, x + 1, kChartH / 2, GetColor(g, g, g), TRUE);
            float cr, cg, cb;
            HsvToRgb((float)x * 360.0f / (float)kChartW, 1.0f, 1.0f, cr, cg, cb);
            DrawBox(x, kChartH / 2, x + 1, kChartH, GetColor((int)(cr * 255.0f), (int)(cg * 255.0f), (int)(cb * 255.0f)), TRUE);
        }
        SetDrawScreen(prev);
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
// 配置は右パネル(インスペクター)の左端 PX から。インスペクターは y=215 までなので、その下を使う。
const int kPanelW   = 230;
const int kTitleY   = 232;
const int kChartY   = 254;  // 補正前(上2帯)・補正後(下2帯)
const int kSliderY0 = 296;  // スライダー1本目の上端
const int kSliderPitch = 32;
const int kSliderCount = 5;
const int kWheelCY  = 518;  // 色の輪の中心Y
const int kPanelBottom = 618;

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

inline int PanelX(int windowW) { return windowW - 240; }

inline int WheelCX(int windowW) { return PanelX(windowW) + 60; }

// スライダーの値を、マウスのX座標から決める。初期値の近く(幅の2.5%)ではぴたりと初期値へ吸い付く
inline float SliderFromX(int i, int mx, int px) {
    const SliderInfo& si = Slider(i);
    float t = (float)(mx - (px + 8)) / (float)(kPanelW - 16);
    t = (std::max)(0.0f, (std::min)(1.0f, t));
    float v = si.lo + (si.hi - si.lo) * t;
    if (fabsf(v - si.def) < (si.hi - si.lo) * 0.025f) v = si.def;
    if (i == 3) v = roundf(v);
    return v;
}

// 色の輪の上のマウス位置から、色相と強さを決める
inline void WheelFromMouse(PanelState& s, int mx, int my, int cx, int cy) {
    float dx = (float)(mx - cx), dy = (float)(my - cy);
    float rr = sqrtf(dx * dx + dy * dy) / (float)kWheelR;
    s.tintHue = WrapDeg(atan2f(-dy, dx) * 180.0f / kPi);
    float a = (std::min)(1.0f, rr);
    if (a < 0.12f) a = 0.0f;       // 中心付近＝かけない
    if (a > 0.92f) a = 1.0f;       // 縁の近く＝いっぱい
    s.tintAmount = a;
}

// 色の輪を「赤・緑・青・なし」のどれかへ一気に合わせる（Tキーと同じ状態）
inline void SetQuickFilter(PanelState& s, int f) {
    if (f <= 0) { s.tintAmount = 0.0f; return; }
    s.tintHue = (f - 1) * 120.0f;
    s.tintAmount = 1.0f;
}

// ボタンなどの矩形（Update と Draw で同じ値を使う）
struct Rect { int x1, y1, x2, y2; };
inline Rect ResetAllRect(int px) { return { px + 146, kTitleY - 2, px + kPanelW - 6, kTitleY + 18 }; }
inline Rect QuickRect(int px, int i) { return { px + 130 + i * 24, 484, px + 130 + i * 24 + 21, 506 }; }
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
inline int RightAlignX(const char* s, int rightX) { return rightX - GetDrawStringWidth(s, (int)strlen(s)); }

inline void FormatValue(int i, float v, char* buf, size_t n) {
    switch (i) {
        case 0: sprintf_s(buf, n, "%d%%", (int)roundf(v * 100.0f)); break;
        case 1: sprintf_s(buf, n, "%+d", (int)roundf(v * 100.0f)); break;
        case 2: sprintf_s(buf, n, "%d%%", (int)roundf(v * 100.0f)); break;
        case 3: sprintf_s(buf, n, "%+d度", (int)roundf(v)); break;
        default: sprintf_s(buf, n, "%+d", (int)roundf(v * 100.0f)); break;
    }
}

// パネル全体を描く。呼び出し側は SetDrawScreen(DX_SCREEN_BACK) の状態で、右パネルの枠を描いたあとに呼ぶ。
//   ink / inkSub / inkAccent / groove … ゲームのUIテーマの色（UiInk() など）
inline void Draw(const PanelState& st, const PanelUi& ui, Resources& res, bool locked, int windowW,
                 int ink, int inkSub, int inkAccent, int groove, int inkOk, int inkWarn) {
    const int px = PanelX(windowW);
    const int wcx = WheelCX(windowW);

    // 上のインスペクターとの区切り線と、タイトル行
    DrawLine(px - 4, kTitleY - 8, windowW - 8, kTitleY - 8, GetColor(78, 76, 86));
    DrawString(px, kTitleY, "カラー補正", inkAccent);
    {
        Rect rr = ResetAllRect(px);
        DrawBox(rr.x1, rr.y1, rr.x2, rr.y2, ui.flash > 0 ? inkAccent : groove, TRUE);
        DrawBox(rr.x1, rr.y1, rr.x2, rr.y2, GetColor(124, 134, 168), FALSE);
        DrawString(rr.x1 + 7, rr.y1 + 2, "リセット", GetColor(255, 255, 255));
    }

    // テストチャート：上＝補正前、下＝いまの補正をかけたもの
    DrawString(px, kChartY, "前", inkSub);        // 補正前（上の帯）
    DrawString(px, kChartY + 18, "後", inkSub);   // 補正後（下の帯）
    const int chx = px + 26;
    if (res.chartSrc >= 0) {
        DrawGraph(chx, kChartY, res.chartSrc, TRUE);
        float mr, mg, mb;
        TintMultiplier(st, mr, mg, mb);
        mr *= st.bright; mg *= st.bright; mb *= st.bright;
        int showHandle = res.chartSrc;
        if (res.chartDst >= 0 && NeedsFilter(st.saturation, st.hue, st.contrast)
            && ApplyFilters(res, res.chartSrc, res.chartDst, st.saturation, st.hue, st.contrast)) showHandle = res.chartDst;
        DrawGraded(chx, kChartY + 18, chx + kChartW, kChartY + 18 + kChartH, 0, 0, kChartW, kChartH, showHandle, mr, mg, mb);
    }
    DrawBox(chx - 1, kChartY - 1, chx + kChartW + 1, kChartY + kChartH + 1, inkAccent, FALSE);
    DrawBox(chx - 1, kChartY + 17, chx + kChartW + 1, kChartY + 18 + kChartH + 1, inkAccent, FALSE);

    // スライダー
    const int tx0 = px + 8, tx1 = px + kPanelW - 8;
    for (int i = 0; i < kSliderCount; i++) {
        const SliderInfo& si = Slider(i);
        int ry = kSliderY0 + i * kSliderPitch;
        float v = SliderValueC(st, i);
        bool changed = fabsf(v - si.def) > (si.hi - si.lo) * 0.004f;

        DrawString(px + 4, ry, si.label, changed ? ink : inkSub);
        // 明るさだけは、ゲームがいま何と判定しているかをラベルの横へ出す
        if (i == 0) {
            int j = JudgeBright(v);
            if (j < 0)      DrawString(px + 4 + 56, ry, "[暗い判定]", GetColor(120, 160, 255));
            else if (j > 0) DrawString(px + 4 + 56, ry, "[明るい判定]", GetColor(255, 190, 80));
        }
        char vb[24];
        FormatValue(i, v, vb, sizeof(vb));
        DrawString(RightAlignX(vb, tx1), ry, vb, changed ? inkAccent : inkSub);

        // つまみの溝。色の帯にして、動かすとどうなるかを見せる
        int ty = ry + 19;
        for (int x = tx0; x < tx1; x++) {
            float t = (float)(x - tx0) / (float)(tx1 - tx0);
            int cr = 120, cg = 120, cb = 120;
            if (i == 0) { int g = (int)(60 + 180 * t); cr = cg = cb = g; }
            else if (i == 1) { int g = (int)(150 - 70 * fabsf(t * 2.0f - 1.0f)); cr = cg = cb = g; } // 中央が灰色、両端ほど濃い（強弱の幅）
            else if (i == 2) { float k = t; cr = (int)(130 + 125 * k); cg = (int)(130 - 80 * k); cb = (int)(130 - 80 * k); }
            else if (i == 3) { float fr, fg, fb; HsvToRgb(t * 360.0f, 0.75f, 1.0f, fr, fg, fb); cr = (int)(fr * 255); cg = (int)(fg * 255); cb = (int)(fb * 255); }
            else { cr = (int)(90 + 165 * t); cg = (int)(150 + 20 * t); cb = (int)(255 - 185 * t); }
            DrawLine(x, ty, x, ty + 8, GetColor(cr, cg, cb));
        }
        if (i == 0) {
            // 暗い判定(0.6未満)・明るい判定(1.3超)の境目に印を付ける
            for (int k = 0; k < 2; k++) {
                float bv = k == 0 ? kDarkBelow : kBrightAbove;
                int bx = tx0 + (int)((bv - si.lo) / (si.hi - si.lo) * (float)(tx1 - tx0));
                DrawLine(bx, ty - 2, bx, ty + 10, inkAccent);
            }
        }
        // 初期値の目印
        {
            int dx = tx0 + (int)((si.def - si.lo) / (si.hi - si.lo) * (float)(tx1 - tx0));
            DrawLine(dx, ty + 9, dx, ty + 12, ink);
        }
        DrawBox(tx0, ty, tx1, ty + 8, inkAccent, FALSE);
        int kx = tx0 + (int)((v - si.lo) / (si.hi - si.lo) * (float)(tx1 - tx0));
        DrawBox(kx - 4, ty - 4, kx + 4, ty + 12, GetColor(255, 255, 255), TRUE);
        DrawBox(kx - 4, ty - 4, kx + 4, ty + 12, GetColor(20, 20, 24), FALSE);
    }

    // 色の輪
    {
        if (res.wheel >= 0) DrawGraph(wcx - (kWheelR + 1), kWheelCY - (kWheelR + 1), res.wheel, TRUE);
        DrawCircle(wcx, kWheelCY, kWheelR, inkAccent, FALSE);
        // 赤・緑・青の「判定が効く範囲」を輪の外側に太線で示す
        const float centers[3] = { 0.0f, 120.0f, 240.0f };
        const unsigned int cols[3] = { GetColor(230, 60, 60), GetColor(60, 190, 60), GetColor(70, 110, 240) };
        const char* names[3] = { "赤", "緑", "青" };
        for (int c = 0; c < 3; c++) {
            for (float a = centers[c] - kFilterZoneDeg; a < centers[c] + kFilterZoneDeg; a += 4.0f) {
                float a2 = (std::min)(a + 4.0f, centers[c] + kFilterZoneDeg);
                float r0 = (float)kWheelR + 4.0f;
                DrawLineAA(wcx + cosf(a * kPi / 180.0f) * r0, kWheelCY - sinf(a * kPi / 180.0f) * r0,
                           wcx + cosf(a2 * kPi / 180.0f) * r0, kWheelCY - sinf(a2 * kPi / 180.0f) * r0, cols[c], 3.0f);
            }
            float lr = (float)kWheelR + 15.0f;
            DrawString(wcx + (int)(cosf(centers[c] * kPi / 180.0f) * lr) - 8, kWheelCY - (int)(sinf(centers[c] * kPi / 180.0f) * lr) - 8, names[c], cols[c]);
        }
        // 現在の位置
        float r1 = st.tintAmount * (float)kWheelR;
        int dotx = wcx + (int)(cosf(st.tintHue * kPi / 180.0f) * r1);
        int doty = kWheelCY - (int)(sinf(st.tintHue * kPi / 180.0f) * r1);
        DrawCircle(dotx, doty, 6, GetColor(255, 255, 255), TRUE);
        DrawCircle(dotx, doty, 6, GetColor(20, 20, 24), FALSE);

        // 右側：数値と判定、クイック選択
        int rx = px + 130;
        DrawString(rx, 462, "色フィルタ", ink);
        const unsigned int qcols[4] = { cols[0], cols[1], cols[2], GetColor(200, 200, 200) };
        const char* qn[4] = { "赤", "緑", "青", "×" };
        for (int q = 0; q < 4; q++) {
            Rect rr = QuickRect(px, q);
            DrawBox(rr.x1, rr.y1, rr.x2, rr.y2, qcols[q], TRUE);
            DrawBox(rr.x1, rr.y1, rr.x2, rr.y2, inkAccent, FALSE);
            DrawString(rr.x1 + 3, rr.y1 + 3, qn[q], q == 3 ? ink : GetColor(255, 255, 255));
        }
        char b1[32], b2[32];
        sprintf_s(b1, sizeof(b1), "色相 %d度", (int)roundf(WrapDeg(st.tintHue)));
        sprintf_s(b2, sizeof(b2), "強さ %d%%", (int)roundf(st.tintAmount * 100.0f));
        DrawString(rx, 514, b1, st.tintAmount > 0.0f ? ink : inkSub);
        DrawString(rx, 534, b2, st.tintAmount > 0.0f ? ink : inkSub);
        int jf = JudgeFilter(st);
        const char* jn = jf == 1 ? "赤フィルタ" : jf == 2 ? "緑フィルタ" : jf == 3 ? "青フィルタ" : "判定なし";
        DrawString(rx, 556, jn, jf ? cols[jf - 1] : inkSub);
    }

    // 注意書き
    DrawString(px + 4, 578, "ダブルクリック＝初期値", inkSub); // 枠の下端（618）に食い込まない高さ
    DrawString(px + 4, 596, "コスト：明るさ・色のみ", inkSub);

    // 封印されているステージ
    if (locked) {
        SetDrawBlendMode(DX_BLENDMODE_ALPHA, 170);
        DrawBox(px - 6, kTitleY + 20, windowW - 2, kPanelBottom, GetColor(30, 26, 24), TRUE);
        SetDrawBlendMode(DX_BLENDMODE_NOBLEND, 0);
        DrawString(px + 6, 420, "このステージでは", GetColor(255, 220, 160));
        DrawString(px + 6, 442, "画面エフェクトが封印", GetColor(255, 220, 160));
        DrawString(px + 6, 464, "されています", GetColor(255, 220, 160));
    }
    (void)inkOk; (void)inkWarn;
}

} // namespace ColorGrade
