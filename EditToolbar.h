#pragma once
// ======================================================================
// EditToolbar.h - 編集ツールのアイコンバー（左）と時間操作ボタン（下）、ホバーの説明カード
//
// 【なぜ作ったか】
// これまでの左パネル（幅250px）は、「いま使える編集」「マウス操作」「キー操作」を文字で並べた一覧だった。
//   ・文字が多くて読みにくく、ゲーム画面を狭めていた。
//   ・巻き戻し・早送り・ズームなどは「押せそうに見えて押せない」一覧で、キーでしか使えなかった。
//   ・どんな操作なのかを、使ってみるまで想像するしかなかった。
// そこで、動画編集ソフトのように、ツールをアイコンのボタンにして、
//   ・左端のスリムなバー（幅68px）… 物の編集 / ズーム / 縮小 / 色・明暗
//   ・下のタイムラインの上      … 巻き戻し / 時間停止 / 早送り / カット
// に並べる。ボタンはクリックで使える（キーと同じ効果）。マウスを重ねると、
// キー・説明・コストと、小さな実演アニメ（コードで描くので、画像も動画も要らない）が入ったカードが出る。
// カードの [?] を押すと「使い方・対策ヒント」が開き、そのあいだ時間は止まる（編集コストは消費しない）。
//
// 【この部品の役割の範囲】
// ここには「どこに何を描くか・どこを押したか」だけを置く。ツールを実際に動かす処理（巻き戻しのコスト消費など）は
// 今までどおり DrawPixel.cpp にあり、ここは押された結果（どのボタンか）を返すだけにしてある。
// 見た目と当たり判定が食い違わないよう、ボタンの矩形は ButtonRect() 1か所で決め、描画もクリック判定も同じ値を使う。
//
// DrawPixel.cpp の「UI素材」関数（DrawUiWindow / UiInk 系）と PAUSE_BUTTON_* 定数より後ろで include すること。
// ======================================================================

#include "UiFont.h"
#include <string>
#include <cmath>
#include <cstdio>
#include <cstring>

namespace EditToolbar {

// ---- ツールの種類（左バー4つ → 下の時間操作4つの順）----
enum Tool { OBJ = 0, ZOOM, ZOOMOUT, COLOR, REWIND, PAUSE, FF, CUT, COUNT };

const int LEFT_W = 68;   // 左バーの幅（以前の左パネルは250px。この差だけゲーム画面が広く使える）

struct Rect {
    int x1, y1, x2, y2;
    bool Has(int x, int y) const { return x >= x1 && x <= x2 && y >= y1 && y <= y2; }
    int W() const { return x2 - x1; }
    int H() const { return y2 - y1; }
};

// ボタンの矩形。左バーは縦に4つ、下は「巻き戻し｜時間停止｜早送り」を中央に、「カット」を少し離して右に置く。
// 時間停止は、これまでの一時停止ボタン（PAUSE_BUTTON_*）の位置のまま。
inline Rect ButtonRect(Tool t) {
    switch (t) {
        case OBJ:     return { 8, 60,  60, 112 };
        case ZOOM:    return { 8, 122, 60, 174 };
        case ZOOMOUT: return { 8, 184, 60, 236 };
        case COLOR:   return { 8, 246, 60, 298 };
        case REWIND:  return { PAUSE_BUTTON_X1 - 64, PAUSE_BUTTON_Y1, PAUSE_BUTTON_X1 - 16, PAUSE_BUTTON_Y2 };
        case PAUSE:   return { PAUSE_BUTTON_X1,      PAUSE_BUTTON_Y1, PAUSE_BUTTON_X2,      PAUSE_BUTTON_Y2 };
        case FF:      return { PAUSE_BUTTON_X2 + 16, PAUSE_BUTTON_Y1, PAUSE_BUTTON_X2 + 64, PAUSE_BUTTON_Y2 };
        case CUT:     return { PAUSE_BUTTON_X2 + 112, PAUSE_BUTTON_Y1, PAUSE_BUTTON_X2 + 160, PAUSE_BUTTON_Y2 };
        default:      return { 0, 0, 0, 0 };
    }
}
inline bool IsLeftBarTool(Tool t) { return t <= COLOR; }

// マウスが重なっているボタン（無ければ -1）
inline int HitTool(int mx, int my) {
    for (int i = 0; i < COUNT; i++) if (ButtonRect((Tool)i).Has(mx, my)) return i;
    return -1;
}

// ---- ツールごとの説明文 ----
struct ToolInfo {
    const char* name;      // ツール名
    const char* key;       // 操作するキー
    const char* line1;     // 一言の説明（2行）
    const char* line2;
    const char* hint[4];   // 「使い方・対策ヒント」（詳細を開いたときだけ出す）
};

inline const ToolInfo& Info(Tool t) {
    static const ToolInfo kInfo[COUNT] = {
        { "物の編集", "右クリック", "敵・ギミック・自分を選んで、", "大きさ・向き・時間を変える",
          { "・右クリックで物を選びます（円形メニューも出ます）", "・ホイール＝拡大縮小　Ctrl＋ホイール＝回転",
            "・つまみを引いて変形　ダブルクリックで反転", "・Ctrl＋クリックで複数選択　Ctrl＋Z／Yで取り消し" } },
        { "ズーム", "Z（長押し）", "画面を拡大して、", "細部を観察する",
          { "・カーソルの位置を中心に、画面をグッと拡大します", "・ホイールで倍率（1.2〜3倍）を変えられます",
            "・拡大に反応する「レンズ連動ギミック」も起動します", "・アイコンをクリックすると、拡大したままにできます" } },
        { "縮小", "V（長押し）", "画面を縮めて、", "広い範囲を見渡す",
          { "・遠くのスイッチや足場を探すのに使います", "・ホイールで倍率（0.5〜0.9倍）を変えられます",
            "・縮小に反応するギミックも起動します", "・アイコンをクリックすると、縮小したままにできます" } },
        { "色・明暗", "T　X　C", "色フィルタと明るさを、", "切り替える",
          { "・T：赤→緑→青→なし の順に、色フィルタを切り替えます", "・X：画面を暗く　C：画面を明るく（押している間）",
            "・同じ色の足場や壁が現れたり、消えたりします", "・右のカラー補正パネルで、細かく調整もできます" } },
        { "巻き戻し", "R（長押し）", "プレイヤーの時間を、", "逆再生で戻す",
          { "・足場や敵の動きも、いっしょに逆再生されます", "・落下や取り損ねを、やり直すのに使います",
            "・使っている間、毎秒コストを消費します", "・クリックで入／切（切るまで戻り続けます）" } },
        { "時間停止", "Space", "時間を止める。", "もう一度押すと再開",
          { "・止めている間も、物の編集はできます", "・← → キーで、1コマずつ進められます",
            "・止めている間、毎秒コストを消費します", "・再開するのは無料です" } },
        { "早送り", "F", "時間を2倍の速さにする。", "もう一度押すと解除",
          { "・待ち時間を短くできます", "・敵の攻撃や弾も、2倍の速さになります",
            "・使っている間、毎秒コストを消費します", "・解除するのは無料です" } },
        { "カット", "Ctrl＋クリック", "タイムラインの区間を、", "丸ごと飛ばす",
          { "・下のタイムラインを Ctrl＋クリックで2回（始点→終点）", "・右クリックで、作ったカットを選べます",
            "・区間が長いほど、コストがかかります", "・アイコンをクリックすると、使い方の枠が光ります" } },
    };
    return kInfo[t];
}

// ---- 状態（画面に出ている間だけ意味を持つ。ステージをまたいでも持ち越してよい物は無いので、リセットで全部戻す）----
struct State {
    int  hover = -1;             // マウスが乗っているボタン
    int  hoverFrames = 0;        // 同じボタンに乗り続けているフレーム数（すぐ出すとチラつくので、少し待ってからカードを出す）
    int  cardTool = -1;          // いまカードを出しているツール（-1＝出していない）
    int  detail = -1;            // 詳細（[?]）を開いているツール（-1＝閉じている）
    int  leaveFrames = 0;        // 詳細を開いたまま、マウスがカードの外へ出ているフレーム数
    bool autoPaused = false;     // 詳細を開いたために、こちらで時間を止めたか（閉じたら戻す。コストは消費しない）
    bool rewindLatch = false;    // 巻き戻しをクリックで入にしている
    bool zoomLatch = false;      // ズームをクリックで入にしている
    bool zoomOutLatch = false;   // 縮小をクリックで入にしている
    int  cutPulse = 0;           // カットのクリック後、タイムラインの枠を光らせる残りフレーム
    int  demoFrame = 0;          // 実演アニメの時計
};
inline State& S() { static State s; return s; }
inline void Reset() { S() = State(); }

// カードの矩形。ふだんは小さく、詳細（[?]）を開くと広がる。左バーのボタンは右隣、下のボタンは真上に出す。
inline Rect CardRect(Tool t, bool detail) {
    const int w = detail ? 468 : 244;
    const int h = detail ? 372 : 226;
    Rect b = ButtonRect(t);
    int x, y;
    if (IsLeftBarTool(t)) { x = LEFT_W + 8; y = b.y1 - 4; }
    else                  { x = (b.x1 + b.x2) / 2 - w / 2; y = PAUSE_BUTTON_Y1 - 12 - h; }
    if (x < 6) x = 6;
    if (x + w > WINDOW_WIDTH - 6) x = WINDOW_WIDTH - 6 - w;
    if (y < 6) y = 6;
    if (y + h > WINDOW_HEIGHT - 100) y = WINDOW_HEIGHT - 100 - h;   // 下のパネルに潜り込まない
    return { x, y, x + w, y + h };
}
inline Rect DetailButtonRect(Tool t) {   // カードの右上の [?]
    Rect c = CardRect(t, false);
    return { c.x2 - 32, c.y1 + 8, c.x2 - 10, c.y1 + 30 };
}
inline Rect CloseButtonRect(Tool t) {    // 詳細カードの右上の [×]
    Rect c = CardRect(t, true);
    return { c.x2 - 32, c.y1 + 8, c.x2 - 10, c.y1 + 30 };
}

// ---- 毎フレームの更新（ホバーの追跡・カードの出し入れ・クリック）----
// 戻り値：このフレームに「押された」ボタン（無ければ -1）。[?] や [×] の操作は中で処理して、ここでは返さない。
// available … バーを操作できる状況か（拡大表示・メッセージ表示中などは false で、全部ひっこめる）
inline int Update(int mx, int my, bool leftPressed, bool available, bool detailAllowed[COUNT]) {
    State& s = S();
    s.demoFrame++;
    if (s.cutPulse > 0) s.cutPulse--;
    if (!available) { s.hover = -1; s.hoverFrames = 0; s.cardTool = -1; s.detail = -1; s.leaveFrames = 0; return -1; }

    // 詳細を開いている間は、カードの中にいる限り、そのカードを保つ
    if (s.detail >= 0) {
        Rect card = CardRect((Tool)s.detail, true);
        Rect btn  = ButtonRect((Tool)s.detail);
        bool inside = card.Has(mx, my) || btn.Has(mx, my);
        if (inside) s.leaveFrames = 0; else s.leaveFrames++;
        if (leftPressed && CloseButtonRect((Tool)s.detail).Has(mx, my)) { s.detail = -1; s.leaveFrames = 0; return -1; }
        // カードの外をクリックしたら、すぐ閉じる。外へ出ただけなら、少し経ってから閉じる
        // （うっかり触れただけで消えないよう、猶予を持たせる）。
        if (leftPressed && !inside) { s.detail = -1; s.leaveFrames = 0; return -1; }
        if (s.leaveFrames > 40) { s.detail = -1; s.leaveFrames = 0; }
        // 詳細を開いているあいだは、ほかのボタンは押せない（カードの下に隠れているボタンを誤って押さないため）
        if (s.detail >= 0) return -1;
    }

    int h = HitTool(mx, my);
    // 小さなカードの上にいるあいだは、ホバー扱いを保つ（[?] へ移動できるように）
    if (h < 0 && s.cardTool >= 0 && CardRect((Tool)s.cardTool, false).Has(mx, my)) h = s.cardTool;
    if (h == s.hover) { if (h >= 0) s.hoverFrames++; } else { s.hover = h; s.hoverFrames = 0; }
    s.cardTool = (s.hover >= 0 && s.hoverFrames >= 6) ? s.hover : -1;

    if (!leftPressed || s.hover < 0) return -1;
    Tool t = (Tool)s.hover;
    // カードの [?]
    if (s.cardTool >= 0 && detailAllowed[t] && DetailButtonRect(t).Has(mx, my)) { s.detail = t; s.leaveFrames = 0; return -1; }
    // ボタンそのもの
    if (ButtonRect(t).Has(mx, my)) return t;
    return -1;
}

// マウスが、カードの [?] / [×] の上にあるか（押せる物の上では、カーソルを指差しにするため）
inline bool OverCardButton(int mx, int my) {
    State& s = S();
    if (s.detail >= 0)   return CloseButtonRect((Tool)s.detail).Has(mx, my);
    if (s.cardTool >= 0) return DetailButtonRect((Tool)s.cardTool).Has(mx, my);
    return false;
}

// ======================================================================
// 描画
// ======================================================================

inline float Ease(float a) { if (a < 0) a = 0; if (a > 1) a = 1; return a * a * (3.0f - 2.0f * a); }

// 小さな文字（キーのバッジなど）。描いたあとに元の大きさへ戻す。
inline void DrawSmall(int x, int y, const char* str, unsigned int col, int size = 12) {
    UiFont::Draw(x, y, str, col, size); // SetFontSize は毎フレーム呼ぶと極端に重いので、サイズ別のフォントハンドルで描く
}

// 錠前（封印中の印）。cx,cy は錠前の中心。
inline void DrawLock(int cx, int cy) {
    DrawBox(cx - 7, cy - 1, cx + 7, cy + 9, GetColor(60, 60, 66), TRUE);
    DrawCircle(cx, cy - 2, 5, GetColor(60, 60, 66), FALSE, 2);
    DrawBox(cx - 1, cy + 2, cx + 1, cy + 6, GetColor(210, 210, 215), TRUE);
}

// ツールのアイコン（コードで描くので、画像素材が要らない）。cx,cy は中心、s は一辺の目安。
inline void DrawToolIcon(Tool t, int cx, int cy, int s, unsigned int col, bool paused) {
    const int u = s;   // 以降の座標は s を基準にした割合で書く
    auto L = [&](float ax, float ay, float bx, float by, int th) {
        DrawLine(cx + (int)(ax * u), cy + (int)(ay * u), cx + (int)(bx * u), cy + (int)(by * u), col, th);
    };
    switch (t) {
        case OBJ: {
            // 点線の箱と、四隅のつまみ、右下のカーソル
            for (int i = 0; i < 4; i++) {
                float a = -0.30f + i * 0.17f;
                if (i % 2 == 0) {
                    L(a, -0.30f, a + 0.09f, -0.30f, 2); L(a, 0.14f, a + 0.09f, 0.14f, 2);
                    L(-0.30f, a, -0.30f, a + 0.09f, 2); L(0.30f, a, 0.30f, a + 0.09f, 2);
                }
            }
            const float hs = 0.07f;
            float cxs[4] = { -0.30f, 0.30f, -0.30f, 0.30f }, cys[4] = { -0.30f, -0.30f, 0.14f, 0.14f };
            for (int i = 0; i < 4; i++)
                DrawBox(cx + (int)((cxs[i] - hs) * u), cy + (int)((cys[i] - hs) * u), cx + (int)((cxs[i] + hs) * u), cy + (int)((cys[i] + hs) * u), col, TRUE);
            DrawTriangle(cx + (int)(0.10f * u), cy + (int)(0.00f * u), cx + (int)(0.10f * u), cy + (int)(0.42f * u), cx + (int)(0.38f * u), cy + (int)(0.30f * u), col, TRUE);
            break;
        }
        case ZOOM: case ZOOMOUT: {
            int r = (int)(0.27f * u);
            int lx = cx - (int)(0.07f * u), ly = cy - (int)(0.07f * u);
            DrawCircle(lx, ly, r, col, FALSE, 3);
            L(0.12f, 0.12f, 0.40f, 0.40f, 4);
            DrawLine(lx - r / 2, ly, lx + r / 2, ly, col, 3);                                   // 横線（＋と－に共通）
            if (t == ZOOM) DrawLine(lx, ly - r / 2, lx, ly + r / 2, col, 3);                    // 縦線（＋だけ）
            break;
        }
        case COLOR: {
            // 赤・緑・青の3つの円を重ねる（足し合わせると白に近づく見た目）
            int r = (int)(0.24f * u);
            SetDrawBlendMode(DX_BLENDMODE_ALPHA, 190);
            DrawCircle(cx - (int)(0.13f * u), cy - (int)(0.10f * u), r, GetColor(235, 70, 70), TRUE);
            DrawCircle(cx + (int)(0.13f * u), cy - (int)(0.10f * u), r, GetColor(70, 200, 90), TRUE);
            DrawCircle(cx, cy + (int)(0.14f * u), r, GetColor(70, 120, 235), TRUE);
            SetDrawBlendMode(DX_BLENDMODE_NOBLEND, 0);
            DrawCircle(cx - (int)(0.13f * u), cy - (int)(0.10f * u), r, col, FALSE, 1);
            DrawCircle(cx + (int)(0.13f * u), cy - (int)(0.10f * u), r, col, FALSE, 1);
            DrawCircle(cx, cy + (int)(0.14f * u), r, col, FALSE, 1);
            break;
        }
        case REWIND: case FF: {
            // 二重の三角形（巻き戻しは左向き、早送りは右向き）
            float d = (t == REWIND) ? -1.0f : 1.0f;
            for (int i = 0; i < 2; i++) {
                float ox = (i - 0.5f) * 0.36f * d;
                DrawTriangle(cx + (int)((ox + 0.17f * d) * u), cy,
                             cx + (int)((ox - 0.17f * d) * u), cy - (int)(0.26f * u),
                             cx + (int)((ox - 0.17f * d) * u), cy + (int)(0.26f * u), col, TRUE);
            }
            break;
        }
        case PAUSE: {
            if (paused) {   // 止まっているあいだは「押せば再開」を表す再生マーク
                DrawTriangle(cx - (int)(0.16f * u), cy - (int)(0.28f * u), cx - (int)(0.16f * u), cy + (int)(0.28f * u), cx + (int)(0.26f * u), cy, col, TRUE);
            } else {
                DrawBox(cx - (int)(0.22f * u), cy - (int)(0.27f * u), cx - (int)(0.06f * u), cy + (int)(0.27f * u), col, TRUE);
                DrawBox(cx + (int)(0.06f * u), cy - (int)(0.27f * u), cx + (int)(0.22f * u), cy + (int)(0.27f * u), col, TRUE);
            }
            break;
        }
        case CUT: {
            // はさみ（交差する2本の刃と、持ち手の輪）
            L(-0.30f, -0.36f, 0.22f, 0.20f, 3);
            L(0.30f, -0.36f, -0.22f, 0.20f, 3);
            DrawCircle(cx - (int)(0.22f * u), cy + (int)(0.30f * u), (int)(0.12f * u), col, FALSE, 2);
            DrawCircle(cx + (int)(0.22f * u), cy + (int)(0.30f * u), (int)(0.12f * u), col, FALSE, 2);
            break;
        }
        default: break;
    }
}

// 小さな文字の横幅（指定の大きさで測る）
inline int SmallWidth(const char* str, int size) {
    return UiFont::Width(str, size);
}

// ツール1つぶんのボタン（下地・アイコン・キーの小バッジ・状態の光・封印の錠前）。
//   hover  … マウスが重なっている   active … いまそのツールが効いている   locked … このステージで封印されている
// 見た目は、動画編集ソフトの操作ボタンのような、平らな暗い色。明るいアイコンを載せるので、
// 左のバー（暗い下地）でも、下のタイムライン（明るい下地）でも同じように読める。
// （uiHandle は、UI素材の枠を使っていた以前の描き方との互換のために残してある）
inline void DrawButton(Tool t, int uiHandle, bool hover, bool active, bool locked, bool paused) {
    (void)uiHandle;
    Rect r = ButtonRect(t);
    const bool wide = IsLeftBarTool(t);

    // 下地と枠。封印中は沈んだ色、押せる状態で重なっていれば明るく、効いている最中は青く。
    unsigned int back = locked ? GetColor(54, 54, 58) : active ? GetColor(28, 64, 118) : hover ? GetColor(84, 94, 128) : GetColor(60, 62, 80);
    unsigned int edge = locked ? GetColor(92, 92, 98) : active ? GetColor(120, 190, 255) : hover ? GetColor(200, 214, 245) : GetColor(124, 134, 168);
    DrawBox(r.x1, r.y1, r.x2, r.y2, back, TRUE);
    SetDrawBlendMode(DX_BLENDMODE_ALPHA, locked ? 14 : 34);                         // 上半分を少し明るくして、ボタンらしい厚みを出す
    DrawBox(r.x1 + 1, r.y1 + 1, r.x2 - 1, (r.y1 + r.y2) / 2, GetColor(255, 255, 255), TRUE);
    SetDrawBlendMode(DX_BLENDMODE_NOBLEND, 0);
    DrawBox(r.x1, r.y1, r.x2, r.y2, edge, FALSE);

    // いま効いているツールは、枠をゆっくり脈打たせて光らせる
    if (active && !locked) {
        float p = 0.5f + 0.5f * sinf(S().demoFrame * 0.12f);
        SetDrawBlendMode(DX_BLENDMODE_ALPHA, 90 + (int)(150.0f * p));
        DrawBox(r.x1 - 1, r.y1 - 1, r.x2 + 1, r.y2 + 1, GetColor(120, 200, 255), FALSE);
        DrawBox(r.x1 + 1, r.y1 + 1, r.x2 - 1, r.y2 - 1, GetColor(120, 200, 255), FALSE);
        SetDrawBlendMode(DX_BLENDMODE_NOBLEND, 0);
    }

    unsigned int iconCol  = locked ? GetColor(120, 120, 128) : active ? GetColor(160, 220, 255) : GetColor(238, 240, 250);
    unsigned int badgeCol = locked ? GetColor(112, 112, 120) : GetColor(178, 186, 210);
    const int cx = (r.x1 + r.x2) / 2, cy = (r.y1 + r.y2) / 2;

    if (wide) {
        // 左のバー：アイコンを大きく、キーの小さなバッジを下に
        DrawToolIcon(t, cx, cy - 5, 40, iconCol, paused);
        const char* keyShort = (t == OBJ) ? "右クリック" : (t == ZOOM) ? "Z" : (t == ZOOMOUT) ? "V" : "T X C";
        int kw = SmallWidth(keyShort, 10);
        DrawSmall(cx - kw / 2, r.y2 - 14, keyShort, badgeCol, 10);
    } else {
        // 下の時間操作：アイコンは左寄り、キーのバッジは右に（ボタンが低いので、横に並べる）
        DrawToolIcon(t, r.x1 + 15, cy, 22, iconCol, paused);
        const char* keyShort = (t == REWIND) ? "R" : (t == PAUSE) ? "Sp" : (t == FF) ? "F" : "Ctrl";
        int kw = SmallWidth(keyShort, 10);
        DrawSmall(r.x2 - 5 - kw, cy - 6, keyShort, badgeCol, 10);
    }
    if (locked) DrawLock(r.x2 - 11, wide ? r.y2 - 22 : r.y1 + 9);
}

// ---- 実演アニメ（小さな窓の中で、そのツールの効果をミニチュアで見せる）----
// 動画ファイルではなく、四角や丸をコードで動かす。ゲームの見た目を変えても撮り直しが要らず、読み込みの待ちも無い。
// 約4秒（240フレーム）で1周する。窓の外へは描かない（SetDrawArea で切る）。
inline void DrawDemo(Tool t, int x, int y, int w, int h, int tick) {
    int u = tick % 240;
    SetDrawArea(x, y, x + w, y + h);
    DrawBox(x, y, x + w, y + h, GetColor(34, 44, 66), TRUE);                          // 空
    const int gy = y + h - 16;                                                         // 地面の上端
    auto Ground = [&]() { DrawBox(x, gy, x + w, y + h, GetColor(78, 62, 54), TRUE); DrawBox(x, gy, x + w, gy + 2, GetColor(120, 160, 90), TRUE); };
    auto Ball = [&](float px, float py, float a) {                                     // プレイヤー（青い丸）
        int al = (int)(255 * a); if (al < 0) al = 0; if (al > 255) al = 255;
        SetDrawBlendMode(DX_BLENDMODE_ALPHA, al);
        DrawCircle((int)px, (int)py, 7, GetColor(110, 150, 255), TRUE);
        DrawCircle((int)px, (int)py, 7, GetColor(220, 230, 255), FALSE);
        SetDrawBlendMode(DX_BLENDMODE_NOBLEND, 0);
    };
    auto Cursor = [&](int cx, int cy) {                                                // マウスカーソル（白い矢印）
        DrawTriangle(cx, cy, cx, cy + 15, cx + 10, cy + 11, GetColor(255, 255, 255), TRUE);
        DrawTriangle(cx, cy, cx, cy + 15, cx + 10, cy + 11, GetColor(20, 20, 20), FALSE);
    };
    const float fw = (float)w, fh = (float)h;

    switch (t) {
        case OBJ: {
            Ground();
            float ex = x + fw * 0.52f;
            float sz = 32.0f - 18.0f * Ease((u - 90) / 50.0f);                          // 敵が縮む
            if (u >= 200) sz = 32.0f - 18.0f * Ease(1.0f - (u - 200) / 40.0f);
            int top = gy - (int)sz;
            DrawBox((int)(ex - sz / 2), top, (int)(ex + sz / 2), gy, GetColor(214, 70, 66), TRUE);
            if (u >= 80 && u < 205) {                                                    // つまみ
                DrawBox((int)(ex - sz / 2), top, (int)(ex + sz / 2), gy, GetColor(255, 255, 255), FALSE);
                float hx[2] = { ex - sz / 2, ex + sz / 2 }, hy[2] = { (float)top, (float)gy };
                for (int i = 0; i < 2; i++) for (int j = 0; j < 2; j++)
                    DrawBox((int)hx[i] - 3, (int)hy[j] - 3, (int)hx[i] + 3, (int)hy[j] + 3, GetColor(255, 235, 120), TRUE);
            }
            float k = Ease(u / 60.0f);                                                   // カーソルが近づく
            if (u < 150) Cursor((int)(x + fw * 0.88f + (ex - (x + fw * 0.88f)) * k), (int)(y + fh * 0.18f + (gy - 14 - (y + fh * 0.18f)) * k));
            if (u >= 60 && u < 85) {                                                     // クリックの輪
                SetDrawBlendMode(DX_BLENDMODE_ALPHA, 200 - (u - 60) * 7);
                DrawCircle((int)ex, gy - 14, 4 + (u - 60), GetColor(255, 235, 120), FALSE, 2);
                SetDrawBlendMode(DX_BLENDMODE_NOBLEND, 0);
            }
            if (u >= 150) {                                                              // 縮んだ敵を、プレイヤーが飛び越える
                float f = (u - 150) / 80.0f; if (f > 1) f = 1;
                float px = x + fw * 0.10f + fw * 0.80f * f;
                Ball(px, gy - 7 - 36.0f * sinf(3.14159f * Ease((f - 0.25f) / 0.5f)), 1.0f);
            }
            break;
        }
        case ZOOM: case ZOOMOUT: {
            // 世界全体（外枠）と、いま画面に映っている範囲（明るい枠）。ズームは範囲が狭まり、縮小は範囲が広がる。
            DrawBox(x + 8, y + 8, x + w - 8, y + h - 8, GetColor(26, 34, 52), TRUE);
            float sx = x + fw * 0.42f, sy = y + fh * 0.58f, kx = x + fw * 0.72f;
            DrawBox((int)sx - 7, (int)sy - 7, (int)sx + 7, (int)sy + 7, GetColor(90, 190, 255), TRUE);          // 水色の物
            DrawBox((int)kx - 7, (int)sy - 7, (int)kx + 7, (int)sy + 7, GetColor(255, 205, 40), TRUE);                 // 黄色の物（スイッチ）
            float f;                                                                      // 枠の大きさの割合（1＝全体）
            if (t == ZOOM) f = 1.0f - 0.55f * (u < 100 ? Ease(u / 90.0f) : (u < 180 ? 1.0f : 1.0f - Ease((u - 180) / 50.0f)));
            else           f = 0.45f + 0.55f * (u < 100 ? Ease(u / 90.0f) : (u < 180 ? 1.0f : 1.0f - Ease((u - 180) / 50.0f)));
            float fx1 = x + 8 + (fw - 16) * 0.5f * (1 - f) * (t == ZOOM ? 0.62f : 0.3f);
            float fy1 = y + 8 + (fh - 16) * 0.5f * (1 - f) * (t == ZOOM ? 1.1f : 0.9f);
            float fx2 = fx1 + (fw - 16) * f, fy2 = fy1 + (fh - 16) * f;
            if (fx2 > x + w - 8) { fx1 -= fx2 - (x + w - 8); fx2 = (float)(x + w - 8); }
            if (fy2 > y + h - 8) { fy1 -= fy2 - (y + h - 8); fy2 = (float)(y + h - 8); }
            DrawBox((int)fx1 - 1, (int)fy1 - 1, (int)fx2 + 1, (int)fy2 + 1, GetColor(20, 18, 16), FALSE);
            DrawBox((int)fx1, (int)fy1, (int)fx2, (int)fy2, GetColor(235, 245, 255), FALSE);
            DrawBox((int)fx1 + 1, (int)fy1 + 1, (int)fx2 - 1, (int)fy2 - 1, GetColor(235, 245, 255), FALSE);
            char buf[16]; sprintf_s(buf, sizeof(buf), "x%.1f", 1.0f / f);
            DrawSmall((int)fx1 + 5, (int)fy1 + 3, buf, GetColor(255, 240, 150), 14);
            if (t == ZOOMOUT && fx2 > kx + 7) {                                           // 広がった枠に黄色の物が入ったら光る
                SetDrawBlendMode(DX_BLENDMODE_ALPHA, 160);
                DrawCircle((int)kx, (int)sy, 14, GetColor(255, 240, 150), FALSE, 2);
                SetDrawBlendMode(DX_BLENDMODE_NOBLEND, 0);
            }
            break;
        }
        case COLOR: {
            Ground();
            float wx = x + fw * 0.55f;
            bool redFilter = (u >= 70 && u < 150), dark = (u >= 150);
            if (redFilter) {                                                              // 赤フィルタ：赤い壁は輪郭だけになって通れる
                SetDrawBlendMode(DX_BLENDMODE_ALPHA, 70);
                DrawBox((int)wx - 7, gy - 46, (int)wx + 7, gy, GetColor(235, 70, 70), TRUE);
                SetDrawBlendMode(DX_BLENDMODE_NOBLEND, 0);
                DrawBox((int)wx - 7, gy - 46, (int)wx + 7, gy, GetColor(235, 70, 70), FALSE);
            } else {
                DrawBox((int)wx - 7, gy - 46, (int)wx + 7, gy, GetColor(235, 70, 70), TRUE);
            }
            float px;
            if (u < 70) { px = x + fw * 0.10f + u * 1.4f; float lim = wx - 15; if (px > lim) px = lim; }
            else        { px = wx - 15 + (u - 70) * 1.0f; float lim = x + fw * 0.90f; if (px > lim) px = lim; }
            Ball(px, gy - 7, 1.0f);
            const char* lab = redFilter ? "赤フィルタ" : (dark ? "暗転" : "フィルタなし");
            unsigned int lc = redFilter ? GetColor(255, 120, 120) : (dark ? GetColor(200, 200, 255) : GetColor(220, 220, 220));
            DrawSmall(x + 6, y + 4, lab, lc, 14);
            if (dark) {                                                                   // 暗転：画面全体を暗くする
                SetDrawBlendMode(DX_BLENDMODE_ALPHA, 120);
                DrawBox(x, y, x + w, y + h, GetColor(0, 0, 10), TRUE);
                SetDrawBlendMode(DX_BLENDMODE_NOBLEND, 0);
            }
            break;
        }
        case REWIND: {
            Ground();
            bool back = (u >= 110 && u < 220);
            float f = (u < 110) ? u / 110.0f : (back ? 1.0f - (u - 110) / 110.0f : 0.0f);
            float px = x + fw * 0.10f + fw * 0.80f * f;
            // 通ってきた道の残像（巻き戻しでは、この道をたどって戻る）
            for (int i = 1; i <= 5; i++) {
                float ff = f - (back ? -1 : 1) * i * 0.035f;
                if (ff < 0 || ff > 1) continue;
                Ball(x + fw * 0.10f + fw * 0.80f * ff, gy - 7, 0.5f - i * 0.08f);
            }
            Ball(px, gy - 7, 1.0f);
            if (back) {
                SetDrawBlendMode(DX_BLENDMODE_ALPHA, 45);
                DrawBox(x, y, x + w, y + h, GetColor(60, 120, 255), TRUE);
                SetDrawBlendMode(DX_BLENDMODE_NOBLEND, 0);
                DrawSmall(x + w - 46, y + 4, "◀◀", GetColor(150, 200, 255), 16);
            }
            break;
        }
        case PAUSE: {
            Ground();
            float rx = x + fw * 0.5f;
            float fall = (u < 80) ? u * 0.55f : (u < 170 ? 80 * 0.55f : 80 * 0.55f + (u - 170) * 0.9f);
            float ry = y + 10 + fall; if (ry > gy - 9) ry = (float)(gy - 9);
            DrawBox((int)rx - 9, (int)ry - 9, (int)rx + 9, (int)ry + 9, GetColor(150, 150, 160), TRUE);
            DrawBox((int)rx - 9, (int)ry - 9, (int)rx + 9, (int)ry + 9, GetColor(230, 230, 235), FALSE);
            Ball(x + fw * 0.2f, gy - 7, 1.0f);
            bool paused = (u >= 80 && u < 170);
            if (paused) {
                DrawBox(x + 1, y + 1, x + w - 1, y + h - 1, GetColor(255, 235, 140), FALSE);
                DrawBox(x + w - 30, y + 8, x + w - 24, y + 24, GetColor(255, 235, 140), TRUE);
                DrawBox(x + w - 18, y + 8, x + w - 12, y + 24, GetColor(255, 235, 140), TRUE);
                DrawSmall(x + 6, y + 4, "止まった！", GetColor(255, 235, 140), 14);
            }
            break;
        }
        case FF: {
            Ground();
            bool fast = (u >= 100);
            float dist = (u < 100) ? u * 0.9f : 100 * 0.9f + (u - 100) * 1.8f;
            float px = x + fw * 0.08f + dist; float lim = x + fw * 0.92f; if (px > lim) px = lim;
            if (fast) {                                                                   // 速度の線
                for (int i = 1; i <= 3; i++) {
                    SetDrawBlendMode(DX_BLENDMODE_ALPHA, 150 - i * 40);
                    DrawLine((int)px - 10 - i * 8, gy - 12 + i * 3, (int)px - 22 - i * 8, gy - 12 + i * 3, GetColor(255, 255, 255), 2);
                    SetDrawBlendMode(DX_BLENDMODE_NOBLEND, 0);
                }
                DrawSmall(x + w - 40, y + 4, "▶▶", GetColor(255, 200, 120), 16);
            }
            Ball(px, gy - 7, 1.0f);
            break;
        }
        case CUT: {
            Ground();
            float wx = x + fw * 0.50f;
            float c1 = x + fw * 0.36f, c2 = x + fw * 0.64f;
            DrawBox((int)wx - 6, gy - 34, (int)wx + 6, gy, GetColor(150, 150, 160), TRUE);          // 邪魔な壁
            int by = y + h - 8;                                                                       // 下のタイムライン（ミニ）
            DrawBox(x + 6, by - 4, x + w - 6, by + 4, GetColor(20, 24, 34), TRUE);
            if (u >= 40) DrawLine((int)c1, by - 8, (int)c1, by + 8, GetColor(0, 255, 255), 2);   // 1回目のクリック＝始点
            if (u >= 90) {                                                                            // 2回目のクリック＝終点。区間が赤くなる
                DrawLine((int)c2, by - 8, (int)c2, by + 8, GetColor(0, 255, 255), 2);
                SetDrawBlendMode(DX_BLENDMODE_ALPHA, 110);
                DrawBox((int)c1, by - 4, (int)c2, by + 4, GetColor(230, 60, 60), TRUE);
                DrawBox((int)c1, y + 4, (int)c2, gy, GetColor(230, 60, 60), TRUE);
                SetDrawBlendMode(DX_BLENDMODE_NOBLEND, 0);
            }
            if (u < 110) { float cxp = (u < 90) ? ((u < 40) ? x + fw * 0.80f : c1) : c2; if (u < 40) cxp = x + fw * 0.80f + (c1 - (x + fw * 0.80f)) * Ease(u / 40.0f); Cursor((int)cxp, by - 24); }
            if (u >= 110) {                                                                           // プレイヤーはカット区間を飛び越える
                float q = x + fw * 0.10f + (u - 110) * 0.8f;
                if (q >= c1) q += (c2 - c1);
                float lim = x + fw * 0.92f; if (q > lim) q = lim;
                Ball(q, gy - 7, 1.0f);
            } else {
                Ball(x + fw * 0.10f, gy - 7, 1.0f);
            }
            DrawSmall(x + 6, y + 4, "Ctrl＋クリック×2", GetColor(220, 220, 220), 13);
            break;
        }
        default: break;
    }
    SetDrawArea(0, 0, WINDOW_WIDTH, WINDOW_HEIGHT);
}

// ---- 説明カード ----
// locked      … このステージで封印されている → 実演は出さず、使えないことだけを1行で伝える
// costText    … 消費コストの目安（呼び出し側が、そのステージの設定値から作る）
// detail      … 詳細（[?]を開いた状態）で描くか
inline void DrawCard(Tool t, bool locked, const char* costText, bool detail) {
    const ToolInfo& inf = Info(t);
    Rect c = CardRect(t, detail);
    State& s = S();

    // 封印中は、小さな1行カードにする
    if (locked) {
        const char* msg = "このステージでは使えません";
        int tw = GetDrawStringWidth(msg, (int)strlen(msg));
        Rect b = ButtonRect(t);
        int w = tw + 28, h = 54;
        int x = IsLeftBarTool(t) ? LEFT_W + 8 : (b.x1 + b.x2) / 2 - w / 2;
        int y = IsLeftBarTool(t) ? b.y1 : PAUSE_BUTTON_Y1 - 12 - h;
        DrawBox(x, y, x + w, y + h, GetColor(30, 26, 24), TRUE);
        DrawBox(x, y, x + w, y + h, GetColor(120, 120, 126), FALSE);
        DrawSmall(x + 12, y + 7, inf.name, GetColor(230, 230, 230), 16);
        DrawSmall(x + 12, y + 30, msg, GetColor(200, 170, 140), 16);
        return;
    }

    SetDrawBlendMode(DX_BLENDMODE_ALPHA, 242);
    DrawBox(c.x1, c.y1, c.x2, c.y2, GetColor(24, 26, 34), TRUE);
    SetDrawBlendMode(DX_BLENDMODE_NOBLEND, 0);
    DrawBox(c.x1, c.y1, c.x2, c.y2, GetColor(120, 150, 210), FALSE);
    DrawBox(c.x1 + 1, c.y1 + 1, c.x2 - 1, c.y2 - 1, GetColor(60, 80, 130), FALSE);

    // 見出し：ツール名・キーのバッジ
    DrawString(c.x1 + 14, c.y1 + 10, inf.name, GetColor(245, 245, 250));
    int nw = GetDrawStringWidth(inf.name, (int)strlen(inf.name));
    int kw = SmallWidth(inf.key, 12) + 12;
    DrawBox(c.x1 + 22 + nw, c.y1 + 11, c.x1 + 22 + nw + kw, c.y1 + 31, GetColor(40, 70, 130), TRUE);
    DrawBox(c.x1 + 22 + nw, c.y1 + 11, c.x1 + 22 + nw + kw, c.y1 + 31, GetColor(120, 160, 230), FALSE);
    DrawSmall(c.x1 + 28 + nw, c.y1 + 14, inf.key, GetColor(200, 225, 255), 12);

    // 右上のボタン：ふだんは [?]、詳細を開いているときは [×]
    Rect rb = detail ? CloseButtonRect(t) : DetailButtonRect(t);
    DrawBox(rb.x1, rb.y1, rb.x2, rb.y2, GetColor(50, 90, 170), TRUE);
    DrawBox(rb.x1, rb.y1, rb.x2, rb.y2, GetColor(150, 190, 255), FALSE);
    DrawSmall(rb.x1 + 6, rb.y1 + 3, detail ? "×" : "?", GetColor(255, 255, 255), 16);

    // 一言の説明（2行）
    DrawSmall(c.x1 + 14, c.y1 + 38, inf.line1, GetColor(200, 205, 220), 14);
    DrawSmall(c.x1 + 14, c.y1 + 56, inf.line2, GetColor(200, 205, 220), 14);

    // 実演アニメ（詳細では広く）
    int dx = c.x1 + 12, dy = c.y1 + 82, dw = c.W() - 24, dh = detail ? 150 : 110;
    DrawDemo(t, dx, dy, dw, dh, s.demoFrame);
    DrawBox(dx, dy, dx + dw, dy + dh, GetColor(90, 100, 130), FALSE);

    // コストの目安
    DrawSmall(c.x1 + 14, dy + dh + 8, costText, GetColor(255, 214, 140), 13);

    // 詳細：使い方・対策ヒント
    if (detail) {
        int hy = dy + dh + 32;
        DrawBox(c.x1 + 12, hy - 4, c.x2 - 12, hy - 3, GetColor(70, 90, 130), TRUE);
        DrawSmall(c.x1 + 14, hy, "使い方・対策ヒント", GetColor(255, 228, 140), 14);
        for (int i = 0; i < 4; i++) DrawSmall(c.x1 + 14, hy + 22 + i * 20, inf.hint[i], GetColor(215, 220, 232), 14);
    }
}

} // namespace EditToolbar
