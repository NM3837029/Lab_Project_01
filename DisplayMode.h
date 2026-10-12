#pragma once
// ============================================================================
// DisplayMode.h - ウィンドウの大きさ・フルスクリーン・マウス座標の補正
//
// 【なぜ作ったか】
// ゲームは 1280x720 の画面（内部解像度）をそのままの大きさのウィンドウに出していたため、
// 大きなディスプレイで遊ぶと、キャラ・ブロック・ギミックのすべてが小さく見えていた。
// 画面の大きさは変えず、「ウィンドウに合わせて画面全体を拡大して見せる」ことで、全部をまとめて大きくする。
//   ・ウィンドウの縁をつかんで大きさを変えられる（縦横比 16:9 を保つ）。
//     DxLib の SetWindowSizeChangeEnableFlag(TRUE, TRUE) が、画面をウィンドウの大きさへ拡大して転送する
//   ・起動時のウィンドウは、デスクトップに収まる最大（最大1.5倍＝1920x1080）にする
//   ・F11 でフルスクリーン（縁なしウィンドウでモニターを覆う。縦横比が違うモニターでは、黒い余白を付ける）
//
// フルスクリーンに DxLib の ChangeWindowMode を使わないのは、切り替えのたびにグラフィックスシステムが
// 作り直されて、MakeScreen で作った画面や ImGui の描画資源が失われる恐れがあるため。
// 縁なしウィンドウ化は、ウィンドウの種類（スタイル）と位置・大きさを変えるだけで済む。
// ============================================================================
#include "DxLib.h"
#include <windows.h>
#include <cmath>
#include <cstdlib>

namespace DisplayMode {

const int kBaseW = 1280;   // ゲームの画面（内部解像度）。これをウィンドウへ拡大して見せる
const int kBaseH = 720;

inline double Rate(HWND h);   // 下で定義

inline bool    g_fullscreen = false;
inline HWND    g_backdrop   = nullptr;   // 縦横比が違うモニターで、ゲーム画面の外側を黒く塗るための窓
inline RECT    g_savedRect  = { 0, 0, 0, 0 };
inline LONG_PTR g_savedStyle = 0, g_savedExStyle = 0;

inline LRESULT CALLBACK BackdropProc(HWND h, UINT m, WPARAM w, LPARAM l) { return DefWindowProcW(h, m, w, l); }

// 黒い下敷きの窓を、モニター全体に出す（ゲームのウィンドウのすぐ下に置く）
inline void ShowBackdrop(HWND game, const RECT& mon) {
    static bool registered = false;
    if (!registered) {
        WNDCLASSW wc = {};
        wc.lpfnWndProc = BackdropProc;
        wc.hInstance = GetModuleHandleW(nullptr);
        wc.hbrBackground = (HBRUSH)GetStockObject(BLACK_BRUSH);
        wc.lpszClassName = L"LabProjectBackdrop";
        wc.hCursor = LoadCursor(nullptr, IDC_ARROW);
        RegisterClassW(&wc);
        registered = true;
    }
    if (!g_backdrop) {
        g_backdrop = CreateWindowExW(WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE, L"LabProjectBackdrop", L"", WS_POPUP,
                                     mon.left, mon.top, mon.right - mon.left, mon.bottom - mon.top, nullptr, nullptr, GetModuleHandleW(nullptr), nullptr);
    }
    if (g_backdrop) {
        SetWindowPos(g_backdrop, game, mon.left, mon.top, mon.right - mon.left, mon.bottom - mon.top, SWP_NOACTIVATE | SWP_SHOWWINDOW);
        // ゲームの窓を、下敷きより手前へ
        SetWindowPos(game, HWND_TOP, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }
}

inline void HideBackdrop() {
    if (g_backdrop) { DestroyWindow(g_backdrop); g_backdrop = nullptr; }
}

// 起動時の大きさ。デスクトップの作業領域（タスクバーを除く）に収まる、最大1.5倍（1920x1080）
inline void InitWindowSize(HWND h) {
    RECT wa = { 0, 0, 1280, 720 };
    SystemParametersInfoW(SPI_GETWORKAREA, 0, &wa, 0);
    RECT frame = { 0, 0, 0, 0 };
    AdjustWindowRectEx(&frame, (DWORD)GetWindowLongPtrW(h, GWL_STYLE), FALSE, (DWORD)GetWindowLongPtrW(h, GWL_EXSTYLE));
    int availW = (wa.right - wa.left) - (frame.right - frame.left);
    int availH = (wa.bottom - wa.top) - (frame.bottom - frame.top);
    double rate = std::fmin((double)availW / kBaseW, (double)availH / kBaseH);
    if (rate > 1.5) rate = 1.5;
    if (rate < 1.0) rate = 1.0;
    SetWindowSizeExtendRate(rate);
    // 作業領域の中央へ
    int cw = (int)(kBaseW * rate), ch = (int)(kBaseH * rate);
    int ww = cw + (frame.right - frame.left), wh = ch + (frame.bottom - frame.top);
    SetWindowPosition(wa.left + ((wa.right - wa.left) - ww) / 2, wa.top + ((wa.bottom - wa.top) - wh) / 2);
}

// 縁なしにするウィンドウスタイル（SetWindowStyleMode の値）。通常は 0
const int kStyleBorderless = 1;

inline double g_savedRate = 1.0;

inline void EnterFullscreen(HWND h) {
    if (g_fullscreen) return;
    GetWindowRect(h, &g_savedRect);
    g_savedRate = Rate(h);
    HMONITOR mon = MonitorFromWindow(h, MONITOR_DEFAULTTONEAREST);
    MONITORINFO mi = {}; mi.cbSize = sizeof(mi);
    GetMonitorInfoW(mon, &mi);
    const RECT m = mi.rcMonitor;
    int mw = m.right - m.left, mh = m.bottom - m.top;
    // モニターに収まる最大の 16:9（拡大率で決める。DxLib が窓の大きさを決める）
    double rate = std::fmin((double)mw / kBaseW, (double)mh / kBaseH);
    int cw = (int)(kBaseW * rate), ch = (int)(kBaseH * rate);
    SetWindowStyleMode(kStyleBorderless);
    SetWindowSizeExtendRate(rate);
    SetWindowPosition(m.left + (mw - cw) / 2, m.top + (mh - ch) / 2);
    if (cw < mw - 1 || ch < mh - 1) ShowBackdrop(h, m);
    g_fullscreen = true;
}

inline void ExitFullscreen(HWND h) {
    if (!g_fullscreen) return;
    HideBackdrop();
    SetWindowStyleMode(0);
    SetWindowSizeExtendRate(g_savedRate);
    SetWindowPosition(g_savedRect.left, g_savedRect.top);
    g_fullscreen = false;
}

inline void ToggleFullscreen(HWND h) { if (g_fullscreen) ExitFullscreen(h); else EnterFullscreen(h); }

// ウィンドウの縁をつかんで大きさを変えるときに、縦横比 16:9 を保つ（WM_SIZING から呼ぶ）
inline void KeepAspect(HWND h, WPARAM edge, RECT* r) {
    RECT frame = { 0, 0, 0, 0 };
    AdjustWindowRectEx(&frame, (DWORD)GetWindowLongPtrW(h, GWL_STYLE), FALSE, (DWORD)GetWindowLongPtrW(h, GWL_EXSTYLE));
    int fw = frame.right - frame.left, fh = frame.bottom - frame.top;
    int cw = (r->right - r->left) - fw, ch = (r->bottom - r->top) - fh;
    if (cw < 320) cw = 320;
    if (ch < 180) ch = 180;
    bool byWidth = (edge == WMSZ_LEFT || edge == WMSZ_RIGHT || edge == WMSZ_TOPLEFT || edge == WMSZ_TOPRIGHT || edge == WMSZ_BOTTOMLEFT || edge == WMSZ_BOTTOMRIGHT);
    if (edge == WMSZ_TOP || edge == WMSZ_BOTTOM) byWidth = false;
    if (byWidth) ch = cw * kBaseH / kBaseW; else cw = ch * kBaseW / kBaseH;
    int nw = cw + fw, nh = ch + fh;
    // つかんでいない側の辺を固定して、つかんだ辺を動かす
    if (edge == WMSZ_LEFT || edge == WMSZ_TOPLEFT || edge == WMSZ_BOTTOMLEFT) r->left = r->right - nw; else r->right = r->left + nw;
    if (edge == WMSZ_TOP || edge == WMSZ_TOPLEFT || edge == WMSZ_TOPRIGHT) r->top = r->bottom - nh; else r->bottom = r->top + nh;
}

// 画面（1280x720）に対する、いまのウィンドウの拡大率。ウィンドウの大きさが変わると変わる。
inline double Rate(HWND h) {
    RECT c; GetClientRect(h, &c);
    int cw = c.right - c.left;
    return cw > 0 ? (double)cw / kBaseW : 1.0;
}

// マウス座標を、画面（1280x720）の座標で返す。
// DxLib が拡大率を見て補正してくれる場合はそのまま、補正されていない場合は自分で割る
// （どちらでも正しくなるよう、OSのカーソル位置と見比べて、近いほうを選ぶ）。
inline int GetMousePointFixed(int* x, int* y) {
    int r = GetMousePoint(x, y);
    HWND h = GetMainWindowHandle();
    double rate = Rate(h);
    if (h && std::fabs(rate - 1.0) > 0.002) {
        POINT p; GetCursorPos(&p); ScreenToClient(h, &p);
        int rx = (int)std::lround(p.x / rate), ry = (int)std::lround(p.y / rate);
        int dRaw = std::abs(*x - p.x) + std::abs(*y - p.y);
        int dFix = std::abs(*x - rx) + std::abs(*y - ry);
        if (dRaw < dFix) { *x = rx; *y = ry; }   // DxLib は補正していない → 自分で補正
    }
    return r;
}

// F11 でフルスクリーンを切り替える（押した瞬間だけ）。どのシーンのループからも毎フレーム呼べる
inline void PollKeys() {
    static bool last = false;
    bool now = CheckHitKey(KEY_INPUT_F11) != 0;
    if (now && !last) ToggleFullscreen(GetMainWindowHandle());
    last = now;
}

} // namespace DisplayMode
