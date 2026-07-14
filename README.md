# Lab_Project_01

DxLib(C++)で作られた2Dアクションゲーム本体と、それ専用のステージ/アセット編集ツール「Lab_Editor」(C# / .NET 8 / WinForms)から構成されるプロジェクトです。時間巻き戻し(リワインド)ギミックを軸にした横スクロールアクションで、敵・ギミック・アイテム・トリガーイベント・BGM/SEなどをすべてLab_Editor側のGUIとJSONアセットファイルで組み立てます。

## 目次

- [構成](#構成)
- [導入方法（セットアップ）](#導入方法セットアップ)
- [使い方](#使い方)
- [機能](#機能)
- [敵・ギミックの挙動スクリプト（パズル的プログラミング機能）](#敵ギミックの挙動スクリプトパズル的プログラミング機能)
- [よくある問題と対処方法](#よくある問題と対処方法)
- [既知の制限・今後の予定](#既知の制限今後の予定)

## 構成

```
Lab_Project_01/
├─ Lab_Project_01.sln / .vcxproj   … ゲーム本体(C++)のVisual Studioプロジェクト
├─ DrawPixel.cpp                    … ゲーム本体のメインソース（WinMain・ゲームループ・全ロジック）
├─ SoundManager.h / EventManager.h / AnimationController.h / BehaviorScript.h / Logger.h
│                                    … ゲーム本体が使う各サブシステムのヘッダオンリー実装
├─ DxLib_VC/                        … DxLib本体（同梱の外部ライブラリ、変更不要）
├─ imgui/                           … ゲーム内デバッグ/簡易編集UI用のDear ImGui
├─ assets/                          … ゲームが読み込むアセット定義・ステージデータ（JSON）
│   ├─ enemies.json / gimmicks.json / items.json / tiles.json
│   ├─ bgm.json / se.json / ui_se.json / animations.json / common_events.json
│   └─ stages/*.json                … 各ステージのタイルマップ・配置情報
├─ img/ , sound/                    … スプライト画像・音声ファイル本体
├─ x64/Debug/Lab_Project_01.exe     … ビルド後のゲーム実行ファイル（Lab_Editorから起動される）
├─ logs/error.log , Log.txt         … ゲーム本体の実行ログ（DxLib内部ログ／独自ログ）
└─ Lab_Editor/
    └─ Lab_Editor/Lab_Editor.csproj … ステージ・アセット編集エディタ本体(C# WinForms)
```

## 導入方法（セットアップ）

### 必要環境

- Windows 10/11
- **Visual Studio 2022**（「C++によるデスクトップ開発」ワークロード）… ゲーム本体のビルド用
- **.NET 8 SDK** … Lab_Editorのビルド用
- DxLibは`DxLib_VC/`に同梱済みのため追加インストール不要

### ビルド手順

1. **ゲーム本体**
   - `Lab_Project_01.sln` をVisual Studio 2022で開く
   - 構成 `Debug` / プラットフォーム `x64` を選択してビルド
   - 成功すると `x64/Debug/Lab_Project_01.exe` が生成される
   - コマンドラインの場合:
     ```
     "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe" Lab_Project_01.vcxproj /p:Configuration=Debug /p:Platform=x64
     ```

2. **Lab_Editor**
   - `Lab_Editor/Lab_Editor/Lab_Editor.csproj` を Visual Studio または `dotnet build` でビルド
     ```
     cd Lab_Editor/Lab_Editor
     dotnet build
     ```
   - 生成された `bin/Debug/net8.0-windows/Lab_Editor.exe` を実行する

Lab_Editorはプロジェクトルート（`Lab_Project_01.vcxproj`が存在するフォルダ）を自動検出し、`assets/`配下を直接読み書きします。ゲーム本体側は**先にビルドしておく**必要があります（Lab_Editorの「▶ プレイ」ボタンは`x64/Debug/Lab_Project_01.exe`を起動する仕組みのため）。

## 使い方

1. Lab_Editorを起動すると、既存のステージ一覧が左側に表示される（無ければ`stage_01`が自動生成される）
2. ツールバーで **レイヤー(遠景/メイン/近景/イベント)** と **ツール(ペン/消しゴム/選択)** を切り替えてタイルマップを編集
3. 右側パネルで敵・ギミック・アイテムを選択し、マップ上をクリックして配置（イベントレイヤー時）
4. 「アセット管理」ボタンから敵・ギミック・アイテム・コモンイベントの定義そのものを編集（後述のパラメータ調整もここで行う）
5. 「▶ プレイ」でゲーム本体を起動して実際に確認、または「ここからプレイ」で任意の位置からテストプレイ
6. 保存は自動（タイル編集の1ストローク完了時、各種設定変更時）。ゲーム内で**矢印キー全押し**するとゲームを閉じてエディタに戻る（隠しショートカット）

## 機能

### ゲーム本体
- 横スクロール2Dアクション、時間巻き戻し（リワインド）システム
- 敵20種＋ギミック24種の作り込まれたプリセット挙動（巡回・追跡・射撃・浮遊・シールド・幽霊敵・画面演出系など）
- 3層タイルレイヤー（遠景/メイン/近景）、視差スクロール背景
- トリガーイベント（6条件）＋アクション（12種）システム、RPGツクール風の「コモンイベント」で使い回し可能
- BGM/SE管理、スプライトアニメーション、当たり判定(Hitbox)・表示スケールの視覚編集

### Lab_Editor
- タイルマップの3層編集、敵/ギミック/アイテム/イベントの配置
- アセット管理（敵・ギミック・アイテム・コモンイベントの定義編集、検索・複製機能付き）
- タイル定義エディタ、サウンド管理、背景設定、Hitboxエディタ、サイズ(スケール)エディタ、アニメーションエディタ
- CSVからのステージ一括生成、「ここからプレイ」による任意地点テストプレイ
- **敵・ギミックの挙動パラメータ編集**（後述）: タイプに応じて表示項目が切り替わるパネルで、速度・間隔・射程などを数値で直接調整可能

## 敵・ギミックの挙動スクリプト（パズル的プログラミング機能）

既存の21種の敵・24種のギミックには「⚙ 挙動パラメータ」パネル（タイプ選択時に自動で表示項目が切り替わる）で速度・射撃間隔・索敵範囲などを調整できます。それだけでは足りない、完全にオリジナルな敵・ギミックを作りたい場合のために、**Scratchを参考にしたブロック的スクリプトシステム**を用意しています（現状は本体側のインタプリタのみ実装済みで、ビジュアルなドラッグ&ドロップ編集画面はまだ無く、JSONを手書きする必要があります）。

### 使い方

`assets/enemies.json` / `assets/gimmicks.json` の該当エントリで `type_enum` を **20**（敵）または **24**（ギミック）＝`ENEMY_CUSTOM_SCRIPT` / `GIMMICK_CUSTOM_SCRIPT` にし、`script` フィールドにブロック配列を書きます。

```json
{
  "id": "my_custom_enemy",
  "name": "自作の敵",
  "type_enum": 20,
  "hp": 3, "width": 32, "height": 32, "sprite": "img/enemy.png",
  "script": [
    {
      "hat": "OnSpawn",
      "body": [
        { "op": "Forever", "body": [
          { "op": "MoveDirection", "dir": "Toward", "speed": 2 },
          { "op": "Wait", "frames": 30 },
          { "op": "Shoot", "angle": { "op": "DirectionToPlayer" }, "speed": 6, "damage": 1 }
        ]}
      ]
    }
  ]
}
```

実際に動く参考例が `assets/enemies.json`（`enemy_script_sample` / `enemy_script_patrol` / `enemy_script_stationary` / `enemy_script_dashcharger`）と `assets/gimmicks.json`（`gim_script_movingplatform`）に入っているので、そのまま参照・改造してください。

### 命令（ブロック）一覧

| カテゴリ | 命令 | 内容 |
|---|---|---|
| 制御 | `Forever { body }` | 本体を無限に繰り返す |
| 制御 | `Repeat { count, body }` | count回繰り返す |
| 制御 | `RepeatUntil { cond, body }` | condが真になるまで繰り返す |
| 制御 | `If { cond, body }` / `IfElse { cond, body, else }` | 条件分岐 |
| 制御 | `Wait { frames }` | 指定フレーム待機 |
| 制御 | `WaitUntil { cond }` | condが真になるまで待機 |
| 動き | `MoveDirection { dir: "Left"/"Right"/"Toward"/"Away", speed }` | 方向・速度を指定して移動 |
| 動き | `ApplyImpulse { vx, vy }` | 速度を直接設定 |
| 動き | `SetPosition { x, y }` / `OffsetPosition { dx, dy }` | 位置を設定／相対移動 |
| 動き | `FaceTowards` | プレイヤーの方を向く |
| 動き | `Oscillate { min, max, periodFrames }` | Y座標をmin〜maxで周期的に振動（動く足場等向け） |
| 攻撃・演出 | `Shoot { angle, speed, damage }` | 指定角度(ラジアン)へ弾を発射 |
| 攻撃・演出 | `ShootAtPlayer { speed, damage }` | プレイヤーへ自動照準して発射 |
| 攻撃・演出 | `SetInvincible { on }` / `SetScale { scale }` | 無敵状態／表示スケールの変更 |
| 攻撃・演出 | `SetVisualEffect { kind: "brightness"/"zoom", intensity }` | 画面演出 |
| 攻撃・演出 | `PlaySound { slot }` | 効果音を再生（`se.json`/`ui_se.json`に登録したidを指定。BGM再生には未対応） |
| 変数 | `SetVar { name, value }` / `ChangeVar { name, value }` | 変数の設定／加算 |
| センサー(式) | `SelfX` / `SelfY` / `PlayerX` / `PlayerY` / `DistanceToPlayer` / `DirectionToPlayer` / `Random{min,max}` / `GetVar{name}` | 数値を返す式ブロック |
| 条件(式) | `Gt`/`Lt`/`Eq`/`And`/`Or`/`Not` `{a,b}` / `IsGrounded` / `IsWallAhead` / `IsGroundAhead` | 真偽値を返す式ブロック |

`hat`は現状 `"OnSpawn"`（配置された瞬間から実行開始）のみ実際に動きます。`"OnDamaged"`/`"OnDeath"`もJSONとしては読み込めますが、発火させる仕組みは今後の実装予定です。

### 安全設計

無限ループを含むスクリプトを書いてもゲームがフリーズしないよう、以下の安全装置が入っています。

- `Forever`/`Repeat`が1周してループの先頭に戻る瞬間、必ずそのフレームの実行を打ち切る（次フレームで続きから再開）
- 1体・1フレームあたりの命令実行数に上限（500）
- 全スクリプト実行体の合計で1フレームあたりの命令数に上限（20000）
- コールスタックの深さに上限（32）。超えた場合はエラーとしてログに記録し、そのスクリプトの実行を停止する（"Faulted"状態、ゲーム自体は継続）

デバッグモード表示中（`isDebugDrawMode`）は、スクリプトで動く敵・ギミックの上に実行状態（RUN/DONE/FAULT、スタック深さ、待機フレーム数）が表示されます。

## よくある問題と対処方法

| 症状 | 原因・対処 |
|---|---|
| Lab_Editorの「▶ プレイ」を押しても何も起きない／「ゲーム実行ファイルが見つかりません」 | ゲーム本体(`Lab_Project_01.vcxproj`)を先にビルドし、`x64/Debug/Lab_Project_01.exe`を生成してください |
| `dotnet build`が「ファイルをコピーできません...別のプロセスによってロックされています」で失敗する | Lab_Editor.exeが起動中です。閉じてから再ビルドするか、`dotnet build -c Release`で別出力先にビルドしてください |
| 敵・ギミックの画像が表示されない／`Log.txt`に「画像ファイル...がありません」と出る | 該当のスプライト画像(`img/`フォルダ)が実際には存在しません。アセット管理の「📁選択」で画像を割り当ててください |
| BGM/SEが鳴らない | `assets/bgm.json`/`se.json`/`ui_se.json`のエントリで **id・name・fileがすべて空でない**ことを確認してください（IDが空のエントリは読み込まれません）。サウンド管理画面で保存しようとすると空欄チェックで警告が出ます |
| 敵のHP・移動速度などをテンプレートで変更しても反映されない | アセット管理画面で編集後、必ず「💾 保存して閉じる」を押してください。マップ上の配置自体を打ち直す必要はありません |
| 自作スクリプトを書いた敵・ギミックが動かない／エラーになる | `logs/error.log`を確認してください。`[BehaviorScript] [Tick] ERROR: ...`という行があれば、スクリプトのJSON構造（`body`が配列になっているか、`cond`の式が正しいか等）を見直してください。何も表示されない場合は`type_enum`が20（敵）/24（ギミック）になっているか確認してください |
| 新しく`.h`/`.cpp`ファイルを追加したらビルドエラーになる（謎の構文エラーが大量に出る） | このプロジェクトはソースファイルの文字コード判定でCP932(Shift-JIS)とUTF-8のせめぎ合いがあり、**LF改行のみのファイルだと日本語コメントの解釈でコンパイラが誤爆することがあります**。新規ファイルは既存ファイルと同様にCRLF改行で保存してください |
| ステージファイルを直接編集したらゲームが起動しなくなった | JSON構文エラーの可能性が高いです。`node -e "JSON.parse(require('fs').readFileSync('assets/stages/xxx.json','utf8'))"`などで構文チェックしてから再度読み込んでください |

## 既知の制限・今後の予定

- 敵・ギミックのビジュアルなブロックエディタ（Scratch風のドラッグ&ドロップ画面）はまだ実装されていません。現状はJSONを手書きする必要があります
- スクリプトの`OnDamaged`/`OnDeath`ハットは解析はされますが、発火するトリガー（被弾・死亡時のフック）はまだ実装されていません
- 一部のギミック（ポータル・橋・破壊ブロック等）は専用スプライト画像が用意されておらず、ゲーム内で表示が欠けることがあります
- 効果音は`jump`（ジャンプ音）のみ登録済みで、敵の攻撃音・被弾音・死亡音などは素材が未整備です
