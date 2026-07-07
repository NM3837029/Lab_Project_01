@echo off
REM ============================================
REM  Lab Engine - ステージエディタ 起動スクリプト
REM ============================================
REM このファイルをダブルクリックするとエディタが起動します。

cd /d "%~dp0"

set EDITOR_EXE=Lab_Editor\Lab_Editor\bin\Publish\Lab_Editor.exe

if not exist "%EDITOR_EXE%" (
    echo.
    echo ■ エディタが見つかりません。ビルドを試みます...
    echo.
    dotnet publish "Lab_Editor\Lab_Editor" -c Release -o "Lab_Editor\Lab_Editor\bin\Publish" --self-contained false
    if errorlevel 1 (
        echo.
        echo ■ ビルドに失敗しました。.NET SDK がインストールされているか確認してください。
        pause
        exit /b 1
    )
)

echo ■ Lab Engine ステージエディタを起動しています...
start "" "%EDITOR_EXE%"
