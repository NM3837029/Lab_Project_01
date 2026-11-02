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
// 書体と太さは、ゲーム全体の既定（メイリオ・アンチエイリアス）に合わせてある。
inline int Handle(int size) {
    static std::map<int, int> cache;
    auto it = cache.find(size);
    if (it != cache.end()) return it->second;
    int fh = CreateFontToHandle("メイリオ", size, 6, DX_FONTTYPE_ANTIALIASING);
    cache[size] = fh;
    return fh;
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
