#pragma once
// ============================================================
// FramePacer.h — ゲームを「1秒に60コマ」で動かすための待ち合わせ
//
// 【なぜ必要か】
// このゲームの動き（歩く速さ・重力・敵の射撃間隔・巻き戻しの長さなど）は、
// すべて「1コマあたり何px動くか」「何コマ待つか」で書かれている。
// つまり 1秒に何コマ進むかで、ゲーム全体の速さがそのまま変わる。
//
// これまでは ScreenFlip() の垂直同期（モニターの書き換えを待つ仕組み）に任せていたので、
//   ・60Hz のモニター  … 1秒に60コマ（作った人が見ている速さ）
//   ・120Hz のモニター … 1秒に120コマ（2倍速）
//   ・144Hz のモニター … 1秒に144コマ（2.4倍速）
// と、遊ぶ人のモニターによって速さが変わっていた。
// 「キャラも敵も速すぎる、撃ってくる間隔も短すぎる」という感想は、これが原因。
// （作った人のPCは60Hzなので、手元では普通の速さに見えていた）
//
// 【どう直すか】
// どのPCでも、こちらで「前のコマから 1/60 秒たつまで」待ってから次のコマへ進む。
// 垂直同期だけに頼らないのは、垂直同期が効かないPCがあるため。実際に開発用のノートPC
// （ウィンドウ表示・GPUが2つある構成）で測ると、垂直同期を入れたままでも 1秒に60コマを超えて回っていた。
// 垂直同期そのものは、DxLib_Init より前にモニターの書き換え回数（Hz）を調べて
//   ・60Hz 前後のモニター … 入れたまま（効けば画面のちらつき＝ティアリングが出ない。待ちはこちらでも行う）
//   ・それ以外のモニター … 切る（120Hz で垂直同期を入れたままだと、1/60 秒の待ちと噛み合わず
//                          コマの間隔が 1/120 秒と 2/120 秒で交互にぶれてカクつくため）
// とする。待ち方は、残りが長いうちは Sleep(1) で CPU を休ませ、最後の2ms弱だけ時計を見ながら待つ
// （Sleep は1msぴったりには起きないことがあるので、最後まで Sleep に任せると遅れる）。
//
// 【使い方】
//   FramePacer::ConfigureBeforeDxInit();  // DxLib_Init の前に1回
//   FramePacer::Wait();                   // 毎コマ、ScreenFlip() の直後に1回
// メインループ以外で自前の ScreenFlip ループを回している画面（Esc のメニューなど）でも、
// 同じように ScreenFlip の直後に Wait() を呼ぶ（呼ばないと、垂直同期を切ったPCでは
// そのループが1秒に何千回も回り、CPU を使い切ってしまう）。
// ============================================================
#include "DxLib.h"
#include <windows.h>

namespace FramePacer {

    // 1コマの長さ（マイクロ秒）。60コマ/秒 = 16666.67μs。
    inline LONGLONG& PeriodUs() { static LONGLONG v = 16667; return v; }
    // 前のコマを「出した」予定時刻（マイクロ秒）。0 は「まだ1コマも出していない」。
    inline LONGLONG& LastUs() { static LONGLONG v = 0; return v; }
    // 垂直同期を入れているか（true なら 60Hz 前後のモニター）。
    inline bool& UseVSync() { static bool v = true; return v; }

    // いま主モニターが1秒に何回書き換わるか（Hz）。調べられなければ 0。
    inline int QueryRefreshRate() {
        DEVMODEW dm = {};
        dm.dmSize = sizeof(dm);
        if (EnumDisplaySettingsW(nullptr, ENUM_CURRENT_SETTINGS, &dm)) return (int)dm.dmDisplayFrequency;
        return 0;
    }

    // DxLib_Init より前に呼ぶ（SetWaitVSyncFlag は初期化前でないと効かないため）。
    inline void ConfigureBeforeDxInit() {
        int hz = QueryRefreshRate();
        // 59Hz（実際は 59.94Hz）〜 61Hz を「60Hz のモニター」とみなす。
        // 0 や 1（調べられなかった・ハードウェア既定値）のときは、安全側で自前の待ちにする。
        bool sixty = (hz >= 59 && hz <= 61);
        UseVSync() = sixty;
        SetWaitVSyncFlag(sixty ? TRUE : FALSE);
        // 1コマの長さは、垂直同期の有無にかかわらず常に 1/60 秒。
        // 垂直同期が効いている 60Hz のモニターでは、ScreenFlip が書き換えを待つ間に 1/60 秒が過ぎるので、
        // Wait() はほとんど待たずに戻る（二重に待ってカクつくことはない）。
        PeriodUs() = 16667;
    }

    // 1コマの終わり（ScreenFlip の直後）に呼ぶ。前のコマから PeriodUs() 経つまで待つ。
    inline void Wait() {
        const LONGLONG period = PeriodUs();
        LONGLONG now = GetNowHiPerformanceCount();
        LONGLONG& last = LastUs();
        if (last == 0) { last = now; return; } // 最初の1コマは基準を取るだけ
        LONGLONG target = last + period;
        // 重い処理（ステージの読み込みなど）で1コマ以上遅れたときは、取り返そうとしない。
        // 取り返そうとすると、そのあと何コマも待たずに連続で進み、一瞬だけ早送りしたように見えてしまう。
        if (now > target + period) { last = now; return; }
        // 残りが2msより長いうちは Sleep(1) で CPU を休ませる
        while (target - now > 2000) {
            Sleep(1);
            now = GetNowHiPerformanceCount();
        }
        // 最後は時計を見ながら待つ（Sleep の寝坊で遅れないように）
        while (now < target) {
            now = GetNowHiPerformanceCount();
        }
        last = target; // 予定時刻を基準に進める（実際の起床時刻にすると、寝坊のぶんだけ毎コマ遅れが溜まる）
    }

} // namespace FramePacer
