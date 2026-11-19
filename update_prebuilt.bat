@echo off
rem ============================================================
rem prebuilt\ のビルド済み exe（ゲーム本体とエディタ）を作り直すための入口。
rem 実体は tools\update_prebuilt.ps1 にある（.ps1 はダブルクリックで実行できないため、この bat を入口にしている）。
rem 使い方: このファイルをダブルクリックするだけ。終わったら prebuilt\ をコミットする。
rem ============================================================
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\update_prebuilt.ps1" %*
echo.
pause
