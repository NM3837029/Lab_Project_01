# プレイ画面 右パネル作り直し 実装計画

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 右パネルを「青い枠＝選んだ物（大きなタイル）」と「オレンジの枠＝画面全体（色相の輪＋中の四角）」の2枠に作り直す。

**Architecture:** 判定・コスト・値の計算は一切変えず、配置（当たり判定の表）・入力の受け方・描画だけを差し替える。純粋な計算（配置・マウス→値・色）はヘッダーの関数にして単体テストし、見た目は検証用の別ビルドで画面を撮って確かめる。

**Tech Stack:** C++17 / DxLib（D3D11）/ MSBuild（VS2022 v143）

**Spec:** `docs/superpowers/specs/2026-11-26-play-right-panel-redesign-design.md`

## Global Constraints

- 画面 1280×720、右パネル x=1030〜1280・y=0〜620。青い枠 x=1041〜1271・y=10〜286、オレンジの枠 x=1041〜1271・y=296〜610
- 値の範囲・しきい値は変えない：明るさ 0.3〜1.7、暗い<0.6、明るい>1.3、強さ 0〜1、判定は強さ≥0.5 かつ 赤0/緑120/青240度 ±35度
- 文字は `UiFont`（サイズ別ハンドル）で描く。毎フレーム `SetFontSize` を呼ばない。毎フレーム1画素ずつの描画をしない
- 自作ソースは **BOM付き UTF-8**。新しいヘッダーを Write ツールで作ったら必ず BOM を付ける（無いと日本語が壊れてビルドが通らない）
- コメントは日本語で細かく（このリポジトリの約束）。`std::min/max` は使えない（Windows の min/max マクロ）。`(std::min)(a,b)` か素の `min(`
- ユーザーの `assets/`・`img/`・未コミットの `assets/stages/Color.json`・`mumei.json` に触れない。検証は scratchpad の別ビルドで行う
- コミットはタスクごと。push はしない（ユーザーの運用：feature push→main ff はユーザーが決める）

## 検証の道具（作成済み・scratchpad）

`SP = C:\Users\naots\AppData\Local\Temp\claude\70dd764c11bb5af2\d9009759-f5e3-412d-ac9a-4dadd173f026\scratchpad`

- 単体テスト：`SP\tests\<name>.cpp` を書き、`cmd /c "SP\tests\run_test.bat <name>"`。DxLib.h とリポジトリのヘッダーを読める。終了コード0＝合格。テストは `assert` ではなく失敗数を数えて `return fails ? 1 : 0`、失敗した項目名を printf する
- 画面の検証：`SP\capture_build.ps1`（リポジトリのソースをコピーしてフックを入れ、`SP\testroot\bin` へビルド。約2分）→ `SP\capture_run.ps1 -Stage <名前> -Script <台本> -Out <フォルダ>`。台本の書式は `SP\build\TmpHook.h` の先頭（m=マウス, k=キー, w=ホイール, s=撮影, q=終了）。stage_demo では、プレイヤー付近の敵「回転させる敵」が画面 (618,455) にいる（右クリックで選択。扇形メニューも開くので、続けて空き場所を左クリックして閉じる）
- 検証用ステージは `SP\testroot\assets\stages\` に置く（ユーザーの assets ではない）

## Review Focus

1. 何も選んでいない→選んだ→選び直し で、オレンジの枠の位置が1pxも動かないこと（Task 4 のテストで枠の座標を固定）
2. 輪の上と四角の上の境目（四角の角の近く）を押したとき、四角が優先されること（Task 1 のテスト `hit_square_corner_beats_ring`）
3. 強さ0のまま輪を押すと強さ1になり、判定が出ること（Task 2 のテスト `ring_press_from_zero_sets_full_strength`）
4. 画面エフェクト封印のステージで、輪・四角・弧・タブ・白い四角のどれを押しても値が変わらないこと（Task 2 の `locked_ignores_everything`）
5. ギミック（3段）と敵（2段）を交互に選んでも、タイルの当たり判定が見た目とずれないこと（Task 4 の `rowat_matches_rowbox_*`）

---

### Task 1: 色の輪＋四角の計算（ColorGrade.h 4章の前半）

**Files:**
- Modify: `ColorGrade.h`（4章の配置定数・`PanelX`/`InPanel`/`WheelCX`/`SliderFromX`/`WheelFromMouse`/`QuickRect`/`ResetAllRect` を置き換え）
- Test: `SP\tests\cg_picker_test.cpp`

**Interfaces:**
- Produces（すべて `namespace ColorGrade`、windowW は 1280 を渡す）：
  - 定数 `kSecX1(w)=w-239, kSecX2(w)=w-9, kSecTop=296, kSecBottom=610, kTabY=328, kTabH=22, kRingCX(w)=w-125, kRingCY=452, kRingOuter=91, kRingInner=77, kArcBand=8, kSquare=104`
  - `enum PickPart { PICK_NONE, PICK_RING, PICK_SQUARE, PICK_ARC_R, PICK_ARC_G, PICK_ARC_B, PICK_SWATCH_RESET, PICK_TAB_COLOR, PICK_TAB_FINE, PICK_RESET_ALL, PICK_FINE0 /* +0..3 = コントラスト,彩度,色相,色温度 */ };`
  - `PickPart HitPart(int tab, int windowW, int mx, int my)`（tab 0=色と明るさ 1=細かい調整。順番：タブ→リセット→白い四角→四角→輪→弧・四隅の文字→細かい調整のスライダー）
  - `void HueFromMouse(PanelState& s, int mx, int my, int windowW)`（右=0度・上=90度、今の色の輪と同じ向き）
  - `void SquareFromMouse(PanelState& s, int mx, int my, int windowW)`（横→`tintAmount` 0〜1、縦→`bright` 下端0.3〜上端1.7。強さ<0.04は0、明るさ1.0±0.035は1.0に吸い付く）
  - `void SquarePos(float amount, float bright, int windowW, int& x, int& y)`
  - `void SquareEdgeColor(float tintHue, float& r, float& g, float& b)`（＝強さ1・色温度0の `TintMultiplier`）
  - `void NowColor(const PanelState& s, float& r, float& g, float& b)`（`TintMultiplier(s)`×明るさ。明るさ>1は `m + (1-m)*((b-1)/0.7*0.5)`）
  - `bool InPanel(int windowW, int mx, int my)`（オレンジの枠の矩形）

- [ ] **Step 1: 失敗するテストを書く**（`cg_picker_test.cpp`）
  - `square_center_is_neutral`：`SquareFromMouse` に四角の左端・縦中央 → amount==0, bright==1.0
  - `square_corners`：右上→(1.0, 1.7)、左下→(0.0, 0.3)
  - `square_roundtrip`：`SquarePos(0.7,0.45)`→`SquareFromMouse` で ±0.02 以内に戻る
  - `hue_directions`：中心の右→0度、左上60度方向→120度付近（±1）、左下→240度付近
  - `hit_square_corner_beats_ring`：四角の右上角の1px内側 → `PICK_SQUARE`
  - `hit_ring_band`：中心から右へ半径84 → `PICK_RING`、半径95で角度0 → `PICK_ARC_R`、半径95で角度60 → `PICK_NONE`
  - `hit_tabs`：(kSecX1+20, kTabY+10)→`PICK_TAB_COLOR`、(kSecX2-20, kTabY+10)→`PICK_TAB_FINE`
  - `fine_tab_hides_picker`：tab=1 で輪の中心 → `PICK_NONE` 以外の `PICK_FINE0+n` か `PICK_NONE`（輪にならない）
  - `nowcolor_neutral_is_white`：初期状態 → (1,1,1)。`SetQuickFilter(赤)` → (1.0,0.4,0.4)
- [ ] **Step 2:** `run_test.bat cg_picker_test` → ビルド失敗（関数が無い）を確認
- [ ] **Step 3:** 上の関数を実装。古い `kTitleY/kChartY/kSliderY0/kWheelCY/kWheelR` を使う関数は Task 3 で消すまで残してよい
- [ ] **Step 4:** `run_test.bat cg_picker_test` → exit 0
- [ ] **Step 5:** `run_test.bat smoke_test` も exit 0。コミット `カラー補正：色の輪＋四角の配置とマウス→値の計算`

### Task 2: ColorGrade::Update を新しい部品で作り直す

**Files:** Modify `ColorGrade.h`（`PanelUi`・`Update`）／ Test `SP\tests\cg_update_test.cpp`

**Interfaces:**
- Consumes: Task 1 の全関数
- Produces: `PanelUi` に `int tab = 0;`、`lastPress` を7要素（0〜4=スライダー, 5=輪, 6=四角）。`ui.drag`：20=輪、21=四角、1〜4=細かい調整のスライダー（`Slider(i)` の添字のまま）。`Update` の引数・戻り値は今と同じ

- [ ] **Step 1: 失敗するテスト**（押す→離す、を `PanelInput` の列で与えるヘルパー `Press(st,ui,x,y)` を作る）
  - `square_drag_sets_both`：四角の右下寄りを押してドラッグ → amount>0.5 かつ bright<0.6、`res.dragging` が true、離すと false
  - `ring_press_from_zero_sets_full_strength`：初期状態で輪の左上（緑）を押す → amount==1, `JudgeFilter`==2
  - `arc_click_snaps`：青の弧を押す → hue==240, amount==1
  - `double_click_square_resets`：四角を2回（20フレーム以内）→ bright==1, amount==0
  - `wheel_square_brightness`：四角の上で wheel=+1 → bright 1.05、wheel=-1 で 1.0 に戻る
  - `wheel_ring_hue`：輪の上で wheel=+1 → hue +5
  - `swatch_reset_keeps_cosmetic`：彩度0.2・明るさ0.5・赤 → 白い四角を押す → bright 1, amount 0, saturation 0.2 のまま
  - `tabs_switch`：細かい調整タブを押す → ui.tab==1、彩度スライダーを左端まで → saturation==0
  - `drag_outside_keeps_grip`：四角を押したまま枠の外へ → まだ値が変わる
  - `locked_ignores_everything`：locked=true で各部品を押す → 値は変わらず、`consumed` は true
  - `outside_not_consumed`：枠の外のクリック → consumed false
- [ ] **Step 2:** 失敗を確認 → **Step 3:** 実装（今の `Update` の流れ＝ドラッグ継続→枠外は無視→ホイール→押した瞬間、を保つ）→ **Step 4:** 合格 → **Step 5:** コミット `カラー補正：輪・四角・弧・タブのマウス操作`

### Task 3: 画面全体の枠の描画とゲームへのつなぎ込み

**Files:** Modify `ColorGrade.h`（`Resources`・`CreateResources`・`Draw`、古い配置定数と前/後の帯・小さな色の輪を削除）、`DrawPixel.cpp`（`ColorGrade::Draw` の呼び出し、ステージ読み込み時の処理、ヒント行）

**Interfaces:**
- Produces: `void Draw(const PanelState& st, const PanelUi& ui, Resources& res, bool locked, int windowW, bool stageUsesFine, float briPreview /* X押下中0.3・C押下中1.7・それ以外 -1 */, int ink, int inkSub, int inkAccent, int groove, int inkOk, int inkWarn)`
- `DrawPixel.cpp` に `bool g_stageUsesFineGrade`：ステージ読み込み時に1回、置いたギミックのうち型26/27で見る値（`GradeSpecOf(const Gimmick&)` の結果、上書き込み）が1〜4の物があれば true
- ステージ読み込み時（`cgUi.drag = -1;` の所）で `cgUi.tab = 0;`

- [ ] **Step 1:** 検証ステージ `SP\testroot\assets\stages\t_fine.json` を作る（stage_demo の写し＋プレイヤー付近に `gim_grade_sw_mono` を1個）
- [ ] **Step 2:** 描画を実装。輪は `CreateResources` で1回だけ画像化。四角は `DrawPrimitive2D`（頂点色つき三角形リスト、`DX_NONE_GRAPH`）で3枚：①左＝白・右＝`SquareEdgeColor` ②下半分：黒、中央α0→下端α0.7 ③上半分：白、中央α0→上端α0.5。点線（明るさ0.6/1.3・強さ0.5）、「明るい」「暗い」、つまみ、`briPreview>=0` なら薄いつまみを重ねる。右下の2色は `NowColor`。判定チップ、キーのヒント、封印の覆い。細かい調整タブは4本のスライダー（今の溝の色の付け方を流用）
- [ ] **Step 3:** オレンジの枠にマウスがある間、ヒント行に「ダブルクリックで元に戻す」
- [ ] **Step 4:** `capture_build.ps1` → BUILD OK。台本で撮影：何もしない／T を1回（赤）／四角の右下をドラッグ（暗い＋色）／C を押したまま／細かい調整タブ（stage_demo と t_fine）。画像を開き、仕様 5.3 と見比べる（チップの文字・札の有無・封印）
- [ ] **Step 5:** フック無しの通常ビルドが通ること（`MSBuild Lab_Project_01.vcxproj /p:Configuration=Release /p:Platform=x64 /p:OutDir=SP\relcheck\`）。コミット `カラー補正：色の輪＋四角の画面とタブ`

### Task 4: 選んだ物のタイル配置（InspLayout.h）

**Files:** Create `InspLayout.h`（`DrawPixel.cpp` 3216〜3265 の `namespace Insp` を移して作り直す）、Modify `DrawPixel.cpp`（元の場所で `#include "InspLayout.h"` と `static_assert(Insp::kWindowW == WINDOW_WIDTH)`）／ Test `SP\tests\insp_layout_test.cpp`

**Interfaces:**
- Produces（`namespace Insp`）：`kWindowW=1280`、`enum Row`（今と同じ並び）、`inline bool lookMode`、`PANEL_W=250`、`SEC_X1=1041, SEC_X2=1271, SEC_Y1=10, SEC_Y2=286`、`EditPanel::Box CardBox()`（敵 y40〜96／ギミック y40〜86, x1048〜1264）、`RowBox(int row)`（列 x1=1048+c*74, 幅68。敵：1段目 y103〜165 = SCALE/ANGLE/SPEED、2段目 y171〜233 = PAUSE/REWIND/TYPE、反応 y240〜260。ギミック：y93〜141 = SCALE/ANGLE/SPEED、y147〜195 = PAUSE/REWIND/LOOK_FLIP、y201〜249 = LOOK_HUE/LOOK_BRIGHT/LOOK_ALPHA、反応 y256〜276）、`RowAt(mx,my)`（今と同じ飛ばし方）。Task 5 までは描画が使う `Left()/Right()/HEAD_Y/HEAD_SIZE` を残す

- [ ] **Step 1: 失敗するテスト**：`rowbox_enemy_grid`・`rowbox_gimmick_grid`（上の座標そのまま）、`rowat_matches_rowbox_enemy`/`_gimmick`（各タイルの中心で RowAt が同じ行、隙間 x=1117 で -1）、`type_hidden_in_lookmode`、`tiles_inside_section`（全タイルが SEC 矩形の中）、`orange_section_fixed`（`SEC_Y2 + 10 == ColorGrade::kSecTop`）
- [ ] **Step 2〜4:** 失敗→実装→合格。`capture_build.ps1` が通る
- [ ] **Step 5:** コミット `右パネル：選んだ物のタイル配置を InspLayout.h へ`

### Task 5: 選んだ物の枠の描画

**Files:** Modify `EditPanel.h`、`UiFont.h`、`DrawPixel.cpp`（インスペクター描画ブロック、今の 16651〜16838 付近）／ Test `SP\tests\editpanel_test.cpp`

**Interfaces:**
- `UiFont::Handle(int size, int thick = 6)`（キャッシュのキーに太さを含める）、`UiFont::DrawBold(x,y,s,col,size)`、`UiFont::WidthBold(s,size)`
- `EditPanel`：`void SplitNameDesc(const std::string& full, std::string& name, std::string& desc)`（最初の「（」で分け、末尾の「）」を外す）、`enum Icon` の末尾に `ICON_LOCK`、`void DrawSection(const Box&, unsigned int edge)`、`void DrawSectionHeader(int x,int y,const char* title,unsigned int dot,unsigned int ink)`、`enum TileLook { TILE_NORMAL, TILE_HOVER, TILE_ACTIVE, TILE_DISABLED }`、`void DrawTile(const Box&, TileLook)`、`void DrawEmptyState(const Box&)`

- [ ] **Step 1: 失敗するテスト**：`split_with_paren`（「回転させる敵（近くの物を回し続ける）」→「回転させる敵」「近くの物を回し続ける」）、`split_without_paren`（「動く足場」→そのまま／desc空）、`split_halfwidth_paren_untouched`（半角括弧は分けない）
- [ ] **Step 2〜4:** 失敗→実装→合格
- [ ] **Step 5:** インスペクター描画を作り直す（仕様 5.2：見出し、空の状態、カード、値タイル＝帯＋ホバーで「◀ ドラッグ ▶」＋変更済みの青い点、切り替え・巡回タイル、錠前、封印の覆い、反応チップは無いとき出さない）。`Left()/Right()/HEAD_*` を InspLayout.h から削除
- [ ] **Step 6:** 撮影：何も選んでいない／敵／敵の大きさタイルを右へ80pxドラッグした後（値と青い点）／ギミック（`t_fine.json` のスイッチか、検証ステージに `gim_breakable` を置く）／プレイヤー／`allowed_object_edits` で scale=false のステージ（錠前）／`objectEditEnabled=false`（封印）。キャンバス3枚目と見比べる
- [ ] **Step 7:** コミット `右パネル：選んだ物をカードと大きなタイルに`

### Task 6: モニター上の選択枠を青い角かっこに

**Files:** Modify `EditPanel.h`（`void DrawCornerBrackets(int x1,int y1,int x2,int y2,unsigned int col,unsigned int edge)`）、`DrawPixel.cpp`（「選択枠の描画」15462 付近の黄色い `DrawBox` 3か所。ギミックのつまみは残す）

- [ ] **Step 1:** 実装（角の長さ＝短辺の1/4、最小6・最大14px。青2px＋外側に暗い1px）
- [ ] **Step 2:** 撮影：敵・ギミック・プレイヤーを選んだ画面と、明るい背景のステージ（`tutorial_1_kihon` など）で見えること
- [ ] **Step 3:** コミット `選択枠をパネルと同じ青の角かっこに`

### Task 7: 仕上げの確認

- [ ] **Step 1:** 全テスト（`cg_picker_test`/`cg_update_test`/`insp_layout_test`/`editpanel_test`）が exit 0
- [ ] **Step 2:** フレームの速さ：stage_demo で30秒動かし、ログの fps（既存の計測）が今の HEAD と同じく60前後
- [ ] **Step 3:** 同じ台本（敵を選ぶ→大きさタイルをドラッグ→一時停止タイル→T→四角をドラッグ）を HEAD のビルドと新しいビルドで流し、コストの残りと判定（フィルタ・明るさ）が同じであること
- [ ] **Step 4:** `README.md` の右パネル・カラー補正の説明を新しい画面に合わせて直す。コミット
- [ ] **Step 5:** `git grep -n "TMPHOOK\|LABPROJ_\|TmpHook"` が0件
