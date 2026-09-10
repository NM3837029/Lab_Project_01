@echo off
rem ============================================================
rem 配布用パッケージを作るための入口。
rem
rem 実体は tools\build_dist.ps1 にある。PowerShell の .ps1 は
rem 既定ではダブルクリックで実行できない（メモ帳で開かれる、または
rem 実行ポリシーで拒否される）ため、この bat を薄い入口として置いている。
rem
rem 使い方: このファイルをダブルクリックするだけ。
rem         引数はそのまま ps1 へ渡される（例: build_dist.bat -SkipBuild）
rem ============================================================
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\build_dist.ps1" %*
echo.
pause
