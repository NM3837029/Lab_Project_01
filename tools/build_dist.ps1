# ============================================================
# 配布用パッケージの作成スクリプト
#
# Release|x64 をビルドし、exe と必要なアセットだけを dist\LabProject01\ に集め、
# zip に固める。展開してexeをダブルクリックすればそのまま遊べる状態になる。
#
# 直接実行せず、リポジトリルートの build_dist.bat をダブルクリックすること
# （PowerShell の .ps1 は既定でダブルクリック実行できないため、bat が薄い入口になっている）。
#
# 使い方:
#   build_dist.bat                  … ビルドして zip まで作る
#   build_dist.bat -SkipBuild       … 既存の x64\Release\ を使って zip だけ作り直す
#   build_dist.bat -Version 0.2.0   … zip のファイル名に付けるバージョン
# ============================================================
param(
    [switch]$SkipBuild,
    [string]$Version = "0.1.0"
)

$ErrorActionPreference = "Stop"

# このスクリプトは tools\ にあるので、1つ上がリポジトリルート
$Root = Split-Path -Parent $PSScriptRoot
Set-Location $Root

Write-Host ""
Write-Host "=== Lab Project 01 配布パッケージの作成 ===" -ForegroundColor Cyan
Write-Host "リポジトリルート: $Root"
Write-Host ""

# ------------------------------------------------------------
# 1. Release|x64 をビルドする
# ------------------------------------------------------------
if (-not $SkipBuild) {
    # MSBuild の場所は vswhere に聞く。
    # README にはインストール先のパスが直書きされているが、VS の版やエディションが
    # 違う環境では通らないので、公式の探索ツールを使う。
    $vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
    if (-not (Test-Path $vswhere)) {
        throw "vswhere.exe が見つかりません。Visual Studio 2022 がインストールされているか確認してください。`n  期待した場所: $vswhere"
    }

    $msbuild = & $vswhere -latest -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\MSBuild.exe" | Select-Object -First 1
    if (-not $msbuild) {
        throw "MSBuild.exe が見つかりません。Visual Studio Installer で「C++によるデスクトップ開発」ワークロードが入っているか確認してください。"
    }

    Write-Host "[1/5] Release|x64 をビルドしています..." -ForegroundColor Yellow
    Write-Host "      MSBuild: $msbuild"

    # .sln ではなく .vcxproj を指定する。
    # .sln だと Lab_Editor(C#) まで巻き込むが、エディタは開発ツールなので配布物には入れない。
    & $msbuild "Lab_Project_01.vcxproj" /p:Configuration=Release /p:Platform=x64 /m /v:minimal /nologo
    if ($LASTEXITCODE -ne 0) {
        throw "ビルドに失敗しました。上のエラーを確認してください。"
    }
    Write-Host "      ビルド成功" -ForegroundColor Green
} else {
    Write-Host "[1/5] ビルドをスキップしました (-SkipBuild)" -ForegroundColor DarkGray
}

$exe = Join-Path $Root "x64\Release\Lab_Project_01.exe"
if (-not (Test-Path $exe)) {
    throw "実行ファイルが見つかりません: $exe`n  -SkipBuild を外して実行してください。"
}

# ------------------------------------------------------------
# 2. 出力先を作り直す
# ------------------------------------------------------------
$DistRoot = Join-Path $Root "dist"
$OutDir   = Join-Path $DistRoot "LabProject01"

Write-Host "[2/5] 出力先を準備しています..." -ForegroundColor Yellow
# 前回の残骸が混ざると「消したはずのステージが配布物に入っている」といった事故になるため、
# 毎回まるごと作り直す
if (Test-Path $OutDir) { Remove-Item $OutDir -Recurse -Force }
New-Item -ItemType Directory -Path $OutDir -Force | Out-Null

# ------------------------------------------------------------
# 3. 中身をコピーする
# ------------------------------------------------------------
Write-Host "[3/5] ファイルをコピーしています..." -ForegroundColor Yellow

Copy-Item $exe -Destination $OutDir

# ゲームが起動時に読むフォルダ。GamePaths::ResolveAndSetGameRoot() が
# exe と同じ階層にある assets\enemies.json を目印にルートを判定するので、
# この4つは必ず exe と同じ階層に置くこと。
foreach ($dir in @("assets", "img", "sound", "se")) {
    $src = Join-Path $Root $dir
    if (-not (Test-Path $src)) { throw "必要なフォルダがありません: $src" }
    Copy-Item $src -Destination $OutDir -Recurse
    Write-Host "      $dir"
}

# ------------------------------------------------------------
# 4. 配布物に不要なものを取り除く
# ------------------------------------------------------------
Write-Host "[4/5] 不要なファイルを除いています..." -ForegroundColor Yellow

# エディタの「ここからプレイ」が書き出す一時ステージ。
# 中身は編集中のステージの複製で、プレイヤーには意味がない。
# （エディタ側もステージ一覧から除外している。除外ルールを揃えること）
$testPlay = Join-Path $OutDir "assets\stages\_test_play.json"
if (Test-Path $testPlay) { Remove-Item $testPlay -Force; Write-Host "      _test_play.json を除外" }

# 構成図などのドキュメント用画像。ゲームは読み込まない。
Get-ChildItem (Join-Path $OutDir "img") -Filter *.svg -ErrorAction SilentlyContinue | ForEach-Object {
    Remove-Item $_.FullName -Force
    Write-Host "      $($_.Name) を除外"
}

# 遊び方の説明。zip を受け取った人が最初に開くファイル。
$readme = @"
Lab Project 01
==============

■ 遊びかた
  Lab_Project_01.exe をダブルクリックすると始まります。

  移動          … A / D
  ジャンプ      … W
  ショット      … Enter（または画面内を左クリック）
  ダッシュ      … Shift（ダッシュを取ったあと）

  このゲームの中心は「編集ツール」です。プレイ中に敵やしかけを直接いじって、
  進めない場所を進めるようにします。

  R 長押し      … 時間を巻き戻す
  スペース      … 一時停止（止めた敵は足場になります）
  F             … 早送り
  T             … 画面の色を変える
  Z / X / C     … ズーム / 暗くする / 明るくする
  右クリック    … 敵やしかけを選んでメニューを出す
  S + ドラッグ  … 大きさを変える
  W + ドラッグ  … 高さを変える（しかけのみ）
  R + ドラッグ  … 回転させる

  相手によって、同じ操作でも起きることが変わります。
  例えばトゲは45度倒すと刺さらなくなって足場になり、
  砲台は傾けると狙いが固定されて、しかけを撃たせることができます。

■ 起動時に警告が出た場合
  「Windows によって PC が保護されました」と表示されることがあります。
  これは開発元の署名が付いていないソフト全てに出るもので、異常ではありません。
  「詳細情報」→「実行」の順に押すと起動します。

■ セーブデータの場所
  %LOCALAPPDATA%\LabProject01\
  進行状況を消したい場合はこのフォルダを削除してください。

■ 注意
  このフォルダの中身（assets / img / sound / se）は移動しないでください。
  実行ファイルと同じ場所に無いとゲームが起動できません。
"@
$readme | Out-File -FilePath (Join-Path $OutDir "README.txt") -Encoding UTF8

# ------------------------------------------------------------
# 5. zip に固める
# ------------------------------------------------------------
Write-Host "[5/5] zip を作成しています..." -ForegroundColor Yellow
$zip = Join-Path $DistRoot "LabProject01_v$Version.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path $OutDir -DestinationPath $zip -CompressionLevel Optimal

$sizeMB = [math]::Round((Get-Item $zip).Length / 1MB, 1)
$stageCount = (Get-ChildItem (Join-Path $OutDir "assets\stages") -Filter *.json).Count

Write-Host ""
Write-Host "=== 完了 ===" -ForegroundColor Green
Write-Host "  フォルダ : $OutDir"
Write-Host "  zip      : $zip  ($sizeMB MB)"
Write-Host "  ステージ : $stageCount 本"
Write-Host ""
Write-Host "zip を別の場所に展開して、Lab_Project_01.exe が起動するか確認してください。" -ForegroundColor Cyan
Write-Host ""
