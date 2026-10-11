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
// このヘッダーは DrawPixel.cpp の1つの翻訳単位からだけ読み込まれる（Ui* 関数とDxLibに依存するので、
// UiInk() などが定義されたあとで #include すること）。
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
inline void DrawGraded(int dx1, int dy1, int dx2, int dy2, int sx1, int sy1, int sx2, int sy2,
                       int src, float mr, float mg, float mb) {
    auto clamp255 = [](float v) { return (int)(std::max)(0.0f, (std::min)(255.0f, v * 255.0f)); };
    SetDrawBright(clamp255((std::min)(1.0f, mr)), clamp255((std::min)(1.0f, mg)), clamp255((std::min)(1.0f, mb)));
    DrawRectExtendGraph(dx1, dy1, dx2, dy2, sx1, sy1, sx2, sy2, src, TRUE);
    if (mr > 1.0f || mg > 1.0f || mb > 1.0f) {
        SetDrawBlendMode(DX_BLENDMODE_ADD, 255);
        SetDrawBright(clamp255(mr - 1.0f), clamp255(mg - 1.0f), clamp255(mb - 1.0f));
        DrawRectExtendGraph(dx1, dy1, dx2, dy2, sx1, sy1, sx2, sy2, src, TRUE);
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

// パネルの操作状態（ドラッグ中の項目、ダブルクリック判定用の時刻、ボタンの押下表示）
struct PanelUi {
    int drag = -1;                   // -1=なし 0〜4=スライダー 10=色の輪
    int frame = 0;                   // 通算フレーム（ダブルクリック判定用）
    int lastPress[kSliderCount + 1] = { -999, -999, -999, -999, -999, -999 }; // 最後に押したフレーム（5=色の輪）
    int flash = 0;                   // 「全リセット」を押した直後の強調（フレーム）
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

inline bool InPanel(int windowW, int mx, int my) {
    return mx >= PanelX(windowW) - 6 && my >= kTitleY - 4 && my <= kPanelBottom;
}

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

// 入力を処理して PanelState を書き換える。locked＝このステージでは画面エフェクトが封印されている
inline PanelResult Update(PanelState& st, PanelUi& ui, const PanelInput& in, bool locked, int windowW) {
    PanelResult res;
    ui.frame++;
    if (ui.flash > 0) ui.flash--;
    const int px = PanelX(windowW);
    const int wcx = WheelCX(windowW);
    bool inside = InPanel(windowW, in.mx, in.my);

    // ドラッグ中の更新（枠の外へ出ても握ったまま）
    if (ui.drag >= 0) {
        if (!in.down || locked) {
            ui.drag = -1;
        } else {
            res.consumed = true;
            res.dragging = true;
            if (ui.drag < kSliderCount) {
                float nv = SliderFromX(ui.drag, in.mx, px);
                if (nv != SliderValue(st, ui.drag)) { SliderValue(st, ui.drag) = nv; res.changed = true; }
            } else {
                PanelState before = st;
                WheelFromMouse(st, in.mx, in.my, wcx, kWheelCY);
                if (before.tintHue != st.tintHue || before.tintAmount != st.tintAmount) res.changed = true;
            }
            return res;
        }
    }

    if (!inside) return res;

    // パネルの上でのクリック・ホイールは、背後の編集操作（オブジェクト選択や拡縮）へ回さない
    if (in.pressed || in.wheel != 0) res.consumed = true;
    if (locked) return res;

    // ホイール：ポインタが乗っている項目を微調整
    if (in.wheel != 0) {
        for (int i = 0; i < kSliderCount; i++) {
            int ry = kSliderY0 + i * kSliderPitch;
            if (in.my >= ry && in.my < ry + kSliderPitch) {
                const SliderInfo& si = Slider(i);
                float nv = (std::max)(si.lo, (std::min)(si.hi, SliderValue(st, i) + si.wheelStep * (float)in.wheel));
                if (fabsf(nv - si.def) < si.wheelStep * 0.5f) nv = si.def;
                SliderValue(st, i) = nv; res.changed = true;
                return res;
            }
        }
        float dx = (float)(in.mx - wcx), dy = (float)(in.my - kWheelCY);
        if (sqrtf(dx * dx + dy * dy) <= (float)kWheelR + 10.0f) {
            st.tintAmount = (std::max)(0.0f, (std::min)(1.0f, st.tintAmount + 0.05f * (float)in.wheel));
            res.changed = true;
        }
        return res;
    }

    if (!in.pressed) return res;

    // 全リセット
    if (Hit(ResetAllRect(px), in.mx, in.my)) {
        Reset(st); ui.flash = 12; res.changed = true; return res;
    }
    // 赤・緑・青・なし
    for (int i = 0; i < 4; i++) {
        if (Hit(QuickRect(px, i), in.mx, in.my)) {
            SetQuickFilter(st, i == 3 ? 0 : i + 1); res.changed = true; return res;
        }
    }
    // スライダー（押した瞬間にその場へ飛び、握ったまま動かせる。短い間隔の2回押しは初期値へ戻す）
    for (int i = 0; i < kSliderCount; i++) {
        int ry = kSliderY0 + i * kSliderPitch;
        if (in.my >= ry && in.my < ry + kSliderPitch) {
            if (ui.frame - ui.lastPress[i] <= 20) {
                SliderValue(st, i) = Slider(i).def; res.changed = true; ui.lastPress[i] = -999;
                return res;
            }
            ui.lastPress[i] = ui.frame;
            ui.drag = i;
            SliderValue(st, i) = SliderFromX(i, in.mx, px);
            res.changed = true; res.dragging = true;
            return res;
        }
    }
    // 色の輪
    {
        float dx = (float)(in.mx - wcx), dy = (float)(in.my - kWheelCY);
        if (sqrtf(dx * dx + dy * dy) <= (float)kWheelR + 4.0f) {
            if (ui.frame - ui.lastPress[kSliderCount] <= 20) {
                st.tintAmount = 0.0f; res.changed = true; ui.lastPress[kSliderCount] = -999;
                return res;
            }
            ui.lastPress[kSliderCount] = ui.frame;
            ui.drag = 10;
            WheelFromMouse(st, in.mx, in.my, wcx, kWheelCY);
            res.changed = true; res.dragging = true;
            return res;
        }
    }
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

    // タイトル行
    DrawString(px, kTitleY, "カラー補正", inkAccent);
    {
        Rect rr = ResetAllRect(px);
        DrawBox(rr.x1, rr.y1, rr.x2, rr.y2, ui.flash > 0 ? inkAccent : groove, TRUE);
        DrawBox(rr.x1, rr.y1, rr.x2, rr.y2, inkAccent, FALSE);
        DrawString(rr.x1 + 7, rr.y1 + 2, "リセット", GetColor(255, 255, 255));
    }

    // テストチャート：上＝補正前、下＝いまの補正をかけたもの
    DrawString(px, kChartY, "元", inkSub);
    DrawString(px, kChartY + 18, "後", inkSub);
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
            if (j < 0)      DrawString(px + 4 + 56, ry, "[暗い判定]", GetColor(60, 90, 190));
            else if (j > 0) DrawString(px + 4 + 56, ry, "[明るい判定]", GetColor(200, 130, 20));
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
        DrawBox(kx - 4, ty - 4, kx + 4, ty + 12, ink, FALSE);
    }

    // 色の輪
    {
        if (res.wheel >= 0) DrawGraph(wcx - (kWheelR + 1), kWheelCY - (kWheelR + 1), res.wheel, TRUE);
        DrawCircle(wcx, kWheelCY, kWheelR, inkAccent, FALSE);
        // 赤・緑・青の「判定が効く範囲」を輪の外側に太線で示す
        const float centers[3] = { 0.0f, 120.0f, 240.0f };
        const unsigned int cols[3] = { GetColor(230, 60, 60), GetColor(60, 190, 60), GetColor(70, 110, 240) };
        const char* names[3] = { "R", "G", "B" };
        for (int c = 0; c < 3; c++) {
            for (float a = centers[c] - kFilterZoneDeg; a < centers[c] + kFilterZoneDeg; a += 4.0f) {
                float a2 = (std::min)(a + 4.0f, centers[c] + kFilterZoneDeg);
                float r0 = (float)kWheelR + 4.0f;
                DrawLineAA(wcx + cosf(a * kPi / 180.0f) * r0, kWheelCY - sinf(a * kPi / 180.0f) * r0,
                           wcx + cosf(a2 * kPi / 180.0f) * r0, kWheelCY - sinf(a2 * kPi / 180.0f) * r0, cols[c], 3.0f);
            }
            float lr = (float)kWheelR + 15.0f;
            DrawString(wcx + (int)(cosf(centers[c] * kPi / 180.0f) * lr) - 4, kWheelCY - (int)(sinf(centers[c] * kPi / 180.0f) * lr) - 8, names[c], cols[c]);
        }
        // 現在の位置
        float r1 = st.tintAmount * (float)kWheelR;
        int dotx = wcx + (int)(cosf(st.tintHue * kPi / 180.0f) * r1);
        int doty = kWheelCY - (int)(sinf(st.tintHue * kPi / 180.0f) * r1);
        DrawCircle(dotx, doty, 6, GetColor(255, 255, 255), TRUE);
        DrawCircle(dotx, doty, 6, ink, FALSE);

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
    DrawString(px + 4, 584, "ダブルクリック＝初期値", inkSub);
    DrawString(px + 4, 602, "コスト：明るさ・色のみ", inkSub);

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
