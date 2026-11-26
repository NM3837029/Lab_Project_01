#pragma once
// ======================================================================
// InspLayout.h - 右パネル上半分「選んだ物」の枠の配置（タイルの当たり判定の表）
//
// 【なぜ作ったか】
// テスターから「右上のボタンが小さく、何に対してできるのか分かりにくい」と言われた。
// 以前は高さ22pxの行を7本縦に並べていたが、2026-11-26 の作り直しで、
//   ・青い枠「選んだ物」でひとまとめにし、選んだ物のカードを上に大きく出す
//   ・操作は3列の大きなタイル（幅69×高さ63、ギミックは高さ49）にする
// という形に変えた。このファイルは、そのタイルの位置だけを決める。
//
// 【この表の使われ方】
// 描画（DrawPixel.cpp のインスペクター描画）・クリック（インスペクターのドラッグと切り替え）・
// 指差しカーソル（押せる所の上でカーソルを指の形にする）の3か所が、どれも RowBox / RowAt を見る。
// 3か所が別々に座標を持つと、見た目と押せる所がずれるので、位置はここ1か所でしか決めない。
//
// 【並び】
//   敵・プレイヤー（lookMode == false）… 2段
//     1段目：大きさ／角度／速さ　　2段目：一時停止／巻き戻し／動き
//   ギミック（lookMode == true）… 3段（見た目の4項目が加わる）
//     1段目：大きさ／角度／速さ　　2段目：一時停止／巻き戻し／上下反転　　3段目：色相／明るさ／透明度
//   その下に「効いている編集」のチップの行（ROW_REACT。押せない）
//
// 枠の外形は、下の「画面全体」の枠（ColorGrade.h の kSecX1 / kSecTop など）と左右をそろえ、10px あけて上に置く。
// EditPanel.h（Box / HitBox）を include したあとで include すること。
// ======================================================================

namespace Insp {

// ゲームのウィンドウの幅。DrawPixel.cpp 側で WINDOW_WIDTH と同じであることを static_assert で確かめる
const int kWindowW = 1280;

// 行（タイル）の種類。値の順番は描画・クリック処理が前提にしているので変えない
// ROW_LOOK_* はギミックを選んだときだけ出る見た目の4枚
enum Row { ROW_SCALE, ROW_ANGLE, ROW_SPEED, ROW_PAUSE, ROW_REWIND, ROW_TYPE,
           ROW_LOOK_HUE, ROW_LOOK_BRIGHT, ROW_LOOK_ALPHA, ROW_LOOK_FLIP, ROW_REACT, ROW_COUNT };

// いま選んでいるのがギミックか（true なら3段の並びになる）。クリック・指差し・描画の前に毎回 SyncInspMode で更新する
inline bool lookMode = false;

const int PANEL_W = 250;                 // 右パネルの幅（右端から）
// 青い枠「選んだ物」の外形（右下も含む）
const int SEC_X1 = kWindowW - 239;       // 1041
const int SEC_X2 = kWindowW - 9;         // 1271
const int SEC_Y1 = 10;
const int SEC_Y2 = 286;
// 枠の中身の左右（枠の線から 7px 内側）
const int CX1 = SEC_X1 + 7;              // 1048
const int CX2 = SEC_X2 - 7;              // 1264
// 見出しの行（青い点＋「選んだ物」）の上端
const int HEADER_Y = 17;
// タイルの幅と、タイルどうしの間（3列で CX1〜CX2 をちょうど埋める：68×3 + 6×2 + 3 = 216）
const int TILE_W = 68, TILE_GAP = 6;

// 選んだ物のカード（絵・名前・札）。敵・プレイヤーは高さ56、ギミックは段が増えるので高さ46
inline EditPanel::Box CardBox() {
    return { CX1, 40, CX2, lookMode ? 86 : 96 };
}

// 段の上端・下端（敵は2段×高さ62、ギミックは3段×高さ48。間はどちらも 6px）
inline int TileRowTop(int rowIndex) { return lookMode ? 93 + rowIndex * 54 : 103 + rowIndex * 68; }
inline int TileRowBottom(int rowIndex) { return TileRowTop(rowIndex) + (lookMode ? 48 : 62); }

// タイルの矩形。その並びに無い行（敵での ROW_LOOK_*、ギミックでの ROW_TYPE）は、どこにも当たらない空の矩形を返す
inline EditPanel::Box RowBox(int row) {
    if (row == ROW_REACT) {
        // 「効いている編集」のチップの行：最後の段の 7px 下から高さ20
        const int y = lookMode ? 256 : 240;
        return { CX1, y, CX2, y + 20 };
    }
    int ri = -1, col = 0;  // 何段目・何列目か
    switch (row) {
        case ROW_SCALE:  ri = 0; col = 0; break;
        case ROW_ANGLE:  ri = 0; col = 1; break;
        case ROW_SPEED:  ri = 0; col = 2; break;
        case ROW_PAUSE:  ri = 1; col = 0; break;
        case ROW_REWIND: ri = 1; col = 1; break;
        case ROW_TYPE:        if (!lookMode) { ri = 1; col = 2; } break;
        case ROW_LOOK_FLIP:   if (lookMode)  { ri = 1; col = 2; } break;
        case ROW_LOOK_HUE:    if (lookMode)  { ri = 2; col = 0; } break;
        case ROW_LOOK_BRIGHT: if (lookMode)  { ri = 2; col = 1; } break;
        case ROW_LOOK_ALPHA:  if (lookMode)  { ri = 2; col = 2; } break;
        default: break;
    }
    if (ri < 0) return { 0, 0, -1, -1 };  // 右下が左上より小さい＝どの点も入らない
    const int x1 = CX1 + col * (TILE_W + TILE_GAP);
    return { x1, TileRowTop(ri), x1 + TILE_W, TileRowBottom(ri) };
}

// マウスがどのタイルの上か（タイルの外なら -1）。操作できるのは ROW_REACT より前の行だけ
inline int RowAt(int mx, int my) {
    for (int r = 0; r < ROW_REACT; r++) {
        if (EditPanel::HitBox(RowBox(r), mx, my)) return r;
    }
    return -1;
}

// ---- 以前の行の描画が使っていた値（タスク5で描画を作り直すまでの間だけ残す）----
inline int Left()  { return CX1; }
inline int Right() { return CX2; }
const int HEAD_Y    = 8;
const int HEAD_SIZE = 40;
const int CARD_H    = 22;

} // namespace Insp
