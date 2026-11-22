# 色・明るさの「出る条件」編集の改良 実装計画

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 色・明るさで出入りするギミックの条件を、共通の条件エディタで作る・置く・確かめるときに迷わず決められるようにする。

**Architecture:** エディタ側に条件のモデル（GradeCondition.cs）と共通の部品（GradeConditionEditor.cs）を作り、アセット画面と、ステージの右クリック「出る条件…」の両方で使う。ゲーム側は置いた物ごとの条件の上書きを読み、近くのロック足場・スイッチにメーターを出す。

**Tech Stack:** C# / .NET 8 WinForms（Lab_Editor）、C++ / DxLib（DrawPixel.cpp）、nlohmann/json、Newtonsoft.Json

**Spec:** `docs/superpowers/specs/2026-11-22-color-brightness-condition-editing-design.md`

## Global Constraints

- コメントは自分のコードすべて日本語で細かく書く
- C++ は UTF-8（BOM付き）・CRLF。C# の既存ファイルも BOM付きを保つ
- 既存の値の意味を守る：gradeMin/gradeMax の -999/999 は「未指定」、22 の param は "1"/"2"/"3"（空なら赤）、23 の param は "bright" 以外はすべて暗い
- 何も触っていないステージ・アセットの JSON に新しいキーを書かない（置いた物ごとの上書きは `gradeChannel`・`gradeMin`・`gradeMax` の3キー、上書きしたときだけ）
- 既存のステージ・アセットの挙動を変えない
- チームの未コミットのアセット（`assets/stages/Dossun.json`・`Color.json`）に触れない
- 検証用の一時フック（TMPHOOK）はコミット前に必ず外す。ゲーム・エディタが起動中なら、出力先を scratchpad に変えてビルドする
- コミット時は `update_prebuilt.bat`（tools/update_prebuilt.ps1）で prebuilt/ を作り直す

## Review Focus

- 両方「未指定」（-999/999）の条件：エディタの文章・つまみの位置が、ゲームの既定範囲（`GradeRange` の L/H 表）と同じであること → Task 1 の検査
- 色フィルタ（見る値5）は範囲ではなく赤緑青：つまみを出さず、下限＝上限＝色番号で保存すること → Task 1・2 の検査
- 置いた物の上書きを「アセットの設定に従う」に戻したとき、3キーがJSONから消えること → Task 4 の検査
- ゲーム内 ImGui エディタで保存しても上書きの3キーが残ること → Task 4 の検査
- 22 の param が空・不正（"abc"）でも、エディタは「赤」として表示し、ゲームと一致すること → Task 1 の検査

---

## ファイル構成

| ファイル | 役割 |
|---|---|
| `Lab_Editor/Lab_Editor/GradeCondition.cs`（新規） | 条件のモデル：見る値ごとの情報表、文章化、表示単位との変換、条件の色、22/23 の param との相互変換 |
| `Lab_Editor/Lab_Editor/GradeConditionEditor.cs`（新規） | 共通の部品（UserControl）：見る値の選択、グラデーションの帯と2つのつまみ、未指定チェック、文章、よく使うボタン。22/23 用の簡易モード |
| `Lab_Editor/Lab_Editor/GradeConditionForm.cs`（新規） | ステージ用のダイアログ（部品＋「アセットの設定に従う」＋OK/キャンセル） |
| `Lab_Editor/Lab_Editor/ZonePreview.cs`（新規） | 18・19 の見本の絵を作る（グレー帯と色見本に補正をかける） |
| `AssetManagerPageControl.cs` / `.Ui.cs` | 26・27 を部品に置き換え、18・19 に見本を足す |
| `StageData.cs` | `PlacedGimmick` に上書き3値、読み書き |
| `MapCanvas.cs` | 右クリック「出る条件…」、地図の印 |
| `DrawPixel.cpp` | 上書きの読み込み、判定・文字・色を個体単位に、ImGui の保持、近くのメーター |

---

### Task 1: 条件のモデル（GradeCondition.cs）

**Files:** Create `Lab_Editor/Lab_Editor/GradeCondition.cs`。検査は scratchpad の検証プログラム（harness）。

**Interfaces（Produces）:**
- `enum GradeKind { Range, ColorLock, BrightLock }`
- `sealed class GradeChannelInfo { int Id; string Name; string Unit; float ViewMin, ViewMax; float DefLo, DefHi; Func<float,float> ToView, FromView; Color Low, High; }`（ToView/FromView は内部値⇔表示値。明るさ・彩度は×100、コントラスト・色温度は×100、色相はそのまま、拡大縮小はそのまま）
- `static class GradeCondition`
  - `GradeChannelInfo Channel(int ch)`（0〜7、範囲外は0）
  - `void EffectiveRange(int ch, float lo, float hi, out float effLo, out float effHi)`（両方未指定なら DefLo/DefHi。ゲームの `GradeRange` と同じ表）
  - `string Sentence(int ch, float lo, float hi)`（例「明るさが 30〜60% のとき出る」「明るさが 60% 以下のとき出る」「赤のフィルタのとき出る」。ゲームの `GradeConditionLabel` の言い方に合わせ、末尾に「のとき出る」）
  - `string Short(int ch, float lo, float hi)`（地図の印用：「赤」「暗」「明≤60%」など）
  - `Color ConditionColor(int ch, float lo, float hi)`
  - `int ColorFromParam(string p)`（空・不正→1）／`string ParamFromColor(int c)`
  - `bool BrightFromParam(string p)`（"bright" のときだけ true）／`string ParamFromBright(bool b)`
  - `string ColorSentence(int c)`／`string BrightSentence(bool bright)`

- [ ] **Step 1:** harness に検査を書く：`EffectiveRange(0,-999,999)` → (-999, 0.6)、`(6,-999,999)` → (1.15, 999)、`Sentence(0,0.3f,0.6f)` == "明るさが 30〜60% のとき出る"、`Sentence(5,2,2)` == "緑のフィルタのとき出る"、`ColorFromParam("")`==1、`ColorFromParam("abc")`==1、`ColorFromParam("3")`==3、`BrightFromParam("BRIGHT")`==false、`Channel(9).Id`==0
- [ ] **Step 2:** harness をビルドして失敗（型が無い）を確認
- [ ] **Step 3:** GradeCondition.cs を実装（値の表は spec 5.1 の表、既定は DrawPixel.cpp `GradeRange` の L/H をそのまま写す）
- [ ] **Step 4:** harness で全検査 PASS

### Task 2: 共通の部品（GradeConditionEditor.cs）

**Interfaces:**
- Consumes: Task 1 の全部
- Produces: `class GradeConditionEditor : UserControl`
  - `GradeKind Kind { get; set; }`（Range／ColorLock／BrightLock でモード切替）
  - `int ChannelValue; float Lo; float Hi;`（Range 用。-999/999＝未指定）
  - `int ColorValue;`（ColorLock 用 1〜3）／`bool BrightValue;`（BrightLock 用）
  - `event EventHandler? ConditionChanged`
  - `void SetRange(int ch, float lo, float hi)`／`void SetColor(int c)`／`void SetBright(bool b)`（イベントを出さずに値を入れる）

- 見た目は spec 5.2 のとおり。帯は自前描画（OnPaint）。つまみのドラッグで表示値→内部値へ変換して Lo/Hi を更新。見る値5は赤緑青の3ボタン。よく使うボタンは GimmickPresets の補正スイッチ・ロック足場の条件（暗いとき／明るいとき／白黒／暖色／寒色／コントラスト高）
- [ ] **Step 1:** harness に検査：部品を作り `SetRange(0,-999,0.6f)` → 「下限なし」がチェック、文章ラベルが "明るさが 60% 以下のとき出る"。見る値を5へ変えると帯が隠れ3ボタンが出る。赤ボタン押下で Lo==Hi==1 かつ ConditionChanged が1回
- [ ] **Step 2:** 失敗を確認 → 実装 → PASS
- [ ] **Step 3:** 部品のスクリーンショット（Range・ColorLock・BrightLock）を撮って目で確認

### Task 3: アセット画面（26・27 置き換え、18・19 見本）

**Files:** Modify `AssetManagerPageControl.cs`（GimmickParamFields の 26/27 を空に）、`AssetManagerPageControl.Ui.cs`（BuildBehaviorSections で 26/27 のとき部品を足す、18/19 のとき ZonePreview を足す）、Create `ZonePreview.cs`

**Interfaces:**
- Consumes: Task 2 の `GradeConditionEditor`
- Produces: `static Bitmap ZonePreview.Render(float tr, float tg, float tb, float sat, float hue, float contrast, int w, int h)`、`static Bitmap ZonePreview.RenderBright(float level, int w, int h)`（-1 は既定値扱い：色は1、明転1.6／暗転0.35）

- [ ] **Step 1:** harness で 27 のアセットを選んだ画面に部品があり、部品の操作で GimmickDef の gradeChannel/gradeMin/gradeMax が変わり、保存した JSON に同じ値が入ることを検査。18/19 で見本の PictureBox があり、数字を変えると絵が変わる（画素の比較）
- [ ] **Step 2:** 実装 → PASS、画面のスクリーンショット確認
- [ ] **Step 3:** 既存の gimmicks.json を読み込んで何も触らず保存 → 差分なし
- [ ] **Step 4:** README 追記、コミット（段階1）

### Task 4: ステージの配置とゲーム側の上書き

**Files:** Modify `StageData.cs`（PlacedGimmick に `float? GradeChannelOv, GradeMinOv, GradeMaxOv`、読み書き）、`MapCanvas.cs`（右クリック「出る条件…」、`DrawConditionBadge`）、Create `GradeConditionForm.cs`、Modify `DrawPixel.cpp`

**Interfaces:**
- Consumes: Task 1・2
- Produces（C++）: `Gimmick` に `bool gradeOv; float gradeOvCh, gradeOvMin, gradeOvMax;`、`PlacedGimmick` に同じ4つ。`GradeConditionMet(const Gimmick&, bool flipped)`・`GradeConditionLabel(const Gimmick&)`・`GradeConditionColor(const Gimmick&)` を、個体の上書き→アセットの順に見る形で追加し、26・27 の呼び出しを全部置き換える。読み込みは配置エントリの3キーが全部数値のときだけ上書きあり

- [ ] **Step 1:** harness 検査：22 の配置で「出る条件…」相当の処理を呼び、緑を選ぶと Param=="2"。27 の配置で上書きを決めると JSON の配置エントリに3キー、「アセットの設定に従う」に戻すと3キーが消える。上書きなしのステージは保存しても差分なし
- [ ] **Step 2:** 実装 → PASS。地図の印のスクリーンショット確認
- [ ] **Step 3:** ゲーム：検証フックの別ビルドで、アセットは「暗いとき」の 27 に配置上書き「明るさ 130% 以上」を付け、明るさを 1.5 にすると実体化・0.4 では消えることをログで確認。ImGui 保存の往復で3キーが残ることを確認。フックを外す
- [ ] **Step 4:** README 追記、コミット（段階2）

### Task 5: ゲーム内の近くのメーター

**Files:** Modify `DrawPixel.cpp`（HUD 層の描画に `DrawConditionMeters` ラムダを追加）

- 対象：22・23・26・27、isTimelineCut でない、プレイヤーと同じ層、中心どうしの距離が 4×TILE_SIZE 以内
- 範囲条件：幅 96px の帯（spec 5.2 と同じグラデーション）＋出る範囲を明るく＋今の値に縦線。22：3つの丸。23：帯＋しきい値 60%・130%＋今の値
- 位置：ギミックの上端の上（既存の条件文字の下）。ワールド→画面の変換はゲーム画面の転送矩形を通す（HUD 層の X()/Y() と同じ）
- [ ] **Step 1:** 検証フックの別ビルドで、27・22・23 の近くに立たせたスクリーンショットを撮り、メーターが出ていて値の縦線が動くこと（明るさを変えた2枚）を目で確認。遠いときは出ないこと
- [ ] **Step 2:** フックを外し、README 追記、prebuilt を作り直してコミット（段階3）
