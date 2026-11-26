#pragma once
// ======================================================================
// UiFont.h - 小さな文字を毎フレーム描くための、サイズ別フォントのキャッシュ
//
// 【なぜ作ったか】
// 左のアイコンバー・右のカード・ブロックの目印は、10〜14px の小さな文字を使う。
// これまでは SetFontSize(小さい) → 描く → SetFontSize(元) と切り替えていたが、
// DxLib の SetFontSize は「呼ぶたびに既定フォントを作り直す」重い処理で、
// 1フレームに数十回呼ぶと、その1フレームが約0.27秒（約4fps）まで遅くなった。
// （実測：左バー描画 約110ms、インスペクター 約52ms、下のボタン周り 約97ms。ほぼすべてが SetFontSize だった）
// そこで、サイズごとに CreateFontToHandle で一度だけフォントを作って覚えておき、
// DrawStringToHandle / GetDrawStringWidthToHandle で描く。既定フォントの設定は一切触らない。
//
// DxLib を include したあとで include すること。
// ======================================================================

#include <cstring>
#include <map>

namespace UiFont {

// 指定サイズのフォントハンドル。初回だけ作り、以後は同じものを返す。
// 作れなかったとき(-1)も覚えておき、毎フレーム作り直しに行かない（呼び出し側は -1 のとき既定フォントで描く）。
// 書体の選び方（2026-11-26 に「2000年代の編集ソフト風」へ変えた）。
// 当時の Windows の画面の文字「MS UI Gothic」を、ぼかし（アンチエイリアス）なしで使う。
// 日本語版でない Windows など、MS UI Gothic が無いPCでは、それまでのメイリオ・アンチエイリアスに戻す。
// どちらを使うかは、引数（MS UI Gothic があるか）だけで決まる純粋な関数にして、テストで確かめる。
inline const char* ChooseFace(bool hasMsUiGothic) { return hasMsUiGothic ? "MS UI Gothic" : "メイリオ"; }
inline int ChooseType(bool hasMsUiGothic) { return hasMsUiGothic ? DX_FONTTYPE_NORMAL : DX_FONTTYPE_ANTIALIASING; }

// MS UI Gothic がこのPCにあるか。最初に聞いたときに1回だけ調べて覚える
inline bool HasMsUiGothic() {
    static int has = -1;
    if (has < 0) has = (CheckFontName("MS UI Gothic") == TRUE) ? 1 : 0;
    return has == 1;
}

// thick はフォントの太さ（DxLib の 0〜9。既定の 6 がふつう、9 が太字）。サイズと太さの組ごとに1つ作って覚える
// （右パネルの見出し・選んだ物の名前・タイルの値を太字にするため、2026-11-26 に太さを足した）。
inline int Handle(int size, int thick = 6) {
    static std::map<int, int> cache;
    const int key = size * 16 + thick;  // 太さは 0〜9 なので、16倍して足せばサイズと重ならない
    auto it = cache.find(key);
    if (it != cache.end()) return it->second;
    const bool ms = HasMsUiGothic();
    int fh = CreateFontToHandle(ChooseFace(ms), size, thick, ChooseType(ms));
    cache[key] = fh;
    return fh;
}

// 太字で描く（見出し・名前・値など、目立たせたい文字に使う）
inline void DrawBold(int x, int y, const char* s, unsigned int col, int size) {
    int fh = Handle(size, 9);
    if (fh < 0) { DrawString(x, y, s, col); return; }
    DrawStringToHandle(x, y, s, col, fh);
}

// 太字での文字の横幅
inline int WidthBold(const char* s, int size) {
    int fh = Handle(size, 9);
    if (fh < 0) return GetDrawStringWidth(s, (int)strlen(s));
    return GetDrawStringWidthToHandle(s, (int)strlen(s), fh);
}

// 指定サイズで文字を描く
inline void Draw(int x, int y, const char* s, unsigned int col, int size) {
    int fh = Handle(size);
    if (fh < 0) { DrawString(x, y, s, col); return; }
    DrawStringToHandle(x, y, s, col, fh);
}

// 指定サイズでの文字の横幅（UTF-8 なので長さはバイト数）
inline int Width(const char* s, int size) {
    int fh = Handle(size);
    if (fh < 0) return GetDrawStringWidth(s, (int)strlen(s));
    return GetDrawStringWidthToHandle(s, (int)strlen(s), fh);
}

} // namespace UiFont
