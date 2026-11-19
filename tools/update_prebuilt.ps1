# ============================================================
# ビルド済み exe（prebuilt\）を作り直すスクリプト
#
# 【なぜ prebuilt\ があるのか】
# x64\ や Lab_Editor\...\bin\ はビルドのたびに中身が変わるので .gitignore で Git から外してある。
# そのため Git から取ってきただけの人（Visual Studio でビルドしていない人）の手元には
# Lab_Project_01.exe も Lab_Editor.exe も無く、「見つからない」状態になっていた。
# そこで、ビルド済みの exe をこのフォルダに入れて Git で配る。
#
#   prebuilt\Lab_Project_01.exe          … ゲーム本体（Release|x64）
#   prebuilt\Lab_Editor\Lab_Editor.exe   … エディタ（1ファイルにまとめたもの）
#   prebuilt\BUILD_INFO.txt              … どのコミットから作ったか
#
# エディタは「.NET 8 デスクトップ ランタイム」を使う形で作る（1.8MB）。
# ランタイムまで中に入れた形（約72MB）にもできるが、Git は古い版も全部持ち続けるので、
# 更新のたびにリポジトリが72MBずつ太り、取ってくるのが遅くなる。
# ランタイムが入っていないPCでは、初回の起動時に Windows が導入を案内してくれる（1回入れれば以後不要）。
#
# 【使い方】リポジトリルートの update_prebuilt.bat をダブルクリックする。
# C++ か エディタ(C#) のコードを直したら、これを実行してから一緒にコミットする。
# ============================================================
$ErrorActionPreference = "Stop"

# このスクリプトは tools\ にあるので、1つ上がリポジトリルート
$Root = Split-Path -Parent $PSScriptRoot
Set-Location $Root
$Pre = Join-Path $Root "prebuilt"
New-Item -ItemType Directory -Force $Pre | Out-Null

Write-Host ""
Write-Host "=== ビルド済み exe（prebuilt）の作成 ===" -ForegroundColor Cyan

# ---- 1. ゲーム本体（C++）を Release|x64 でビルドする ----
# MSBuild の場所は vswhere に聞く（VS の版やエディションが違っても見つかるように）。
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
if (-not (Test-Path $vswhere)) { throw "vswhere.exe が見つかりません。Visual Studio 2022 が必要です。" }
$msbuild = & $vswhere -latest -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\MSBuild.exe" | Select-Object -First 1
if (-not $msbuild) { throw "MSBuild.exe が見つかりません。「C++によるデスクトップ開発」ワークロードを入れてください。" }

Write-Host "[1/3] ゲーム本体をビルドしています..." -ForegroundColor Yellow
# ゲームが起動したままだと exe を上書きできずに失敗するので、先に閉じておくこと
& $msbuild "Lab_Project_01.vcxproj" /p:Configuration=Release /p:Platform=x64 /m /v:minimal /nologo
if ($LASTEXITCODE -ne 0) { throw "ゲーム本体のビルドに失敗しました（起動中なら閉じてからやり直してください）。" }
Copy-Item -Force (Join-Path $Root "x64\Release\Lab_Project_01.exe") (Join-Path $Pre "Lab_Project_01.exe")

# ---- 2. エディタ（C#）を1ファイルにまとめて出力する ----
Write-Host "[2/3] エディタをビルドしています..." -ForegroundColor Yellow
$EdOut = Join-Path $Pre "Lab_Editor"
if (Test-Path $EdOut) { Remove-Item $EdOut -Recurse -Force }
& dotnet publish "Lab_Editor\Lab_Editor\Lab_Editor.csproj" -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o $EdOut --nologo -v:quiet
if ($LASTEXITCODE -ne 0) { throw "エディタのビルドに失敗しました（エディタが起動中なら閉じてください）。" }
# デバッグ用の .pdb は配る必要が無いので消す（.gitignore でも外してある）
Get-ChildItem $EdOut -Filter *.pdb | Remove-Item -Force

# ---- 3. どのコミットから作ったかを書き残す ----
Write-Host "[3/3] BUILD_INFO.txt を書いています..." -ForegroundColor Yellow
$commit = (& git rev-parse --short HEAD) 2>$null
$dirty = (& git status --porcelain --untracked-files=no) 2>$null
$info = @(
    "作成日時: " + (Get-Date -Format "yyyy-MM-dd HH:mm"),
    "元にしたコミット: " + $commit + $(if ($dirty) { "（＋まだコミットしていない変更あり）" } else { "" })
)
# BOM付きUTF-8で書く（メモ帳など、どのエディタで開いても文字化けしないように）
[System.IO.File]::WriteAllLines((Join-Path $Pre "BUILD_INFO.txt"), $info, (New-Object System.Text.UTF8Encoding $true))

Write-Host ""
Write-Host "完了しました。prebuilt\ の中身をコミットしてください。" -ForegroundColor Green
Get-ChildItem $Pre -Recurse -File | ForEach-Object { "  {0}  ({1:N1} MB)" -f $_.FullName.Substring($Root.Length + 1), ($_.Length / 1MB) }
