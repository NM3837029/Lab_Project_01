#pragma once
#include <string>
#include <fstream>
#include <chrono>
#include <iomanip>
#include <sstream>
#include <direct.h>
#include <windows.h>  // CreateDirectoryW（ログ出力先の作成に使う）
#include <cstdlib>    // _wdupenv_s（%LOCALAPPDATA% の取得に使う）

// ゲーム全体で共通して使うログ出力クラス。
// エラー情報や実行情報を "logs/error.log" というテキストファイルに
// 追記していくだけのシンプルな仕組みで、すべてstatic関数として提供しているため
// インスタンス化せずに Logger::Error(...) のようにどこからでも呼び出せる。
class Logger {
public:
    // ログファイルのフルパスを返す。初回呼び出し時に一度だけ決定してキャッシュする。
    //
    // 【なぜ場所を切り替えるのか】
    // 従来はカレントディレクトリ直下の "logs/error.log" 固定だった。
    // 開発中はそれで良いが、配布版が Program Files 配下などにインストールされると
    // 書き込み権限が無く、ofstream が黙って失敗してログが1行も残らなくなる
    // （元の実装は is_open() が false なら何もせず return するため、失敗にも気づけない）。
    //
    // 【なぜ NDEBUG で分けないのか】
    // 「Release版だけ %LOCALAPPDATA% へ」とすると、Releaseビルドを手元でデバッグしたい
    // ときにログが見えなくなる。ビルド構成ではなく「実際に書き込めるかどうか」で
    // 決めるのが正しい。開発時は今までどおり logs/error.log に出る。
    static const std::wstring& LogFilePath() {
        static std::wstring cached = ResolveLogFilePath();
        return cached;
    }

private:
    // 書き込み先を実際に試して決める（LogFilePath から一度だけ呼ばれる）。
    static std::wstring ResolveLogFilePath() {
        // まずは従来どおりカレントディレクトリ直下の logs/ を試す。
        CreateDirectoryW(L"logs", NULL);
        {
            std::wstring local = L"logs\\error.log";
            std::ofstream probe(local.c_str(), std::ios::app);
            if (probe.is_open()) return local;
        }

        // 書けなかった（インストール先が読み取り専用など）。
        // ユーザーごとの書き込み可能フォルダへ逃がす。
        wchar_t* base = nullptr;
        size_t len = 0;
        if (_wdupenv_s(&base, &len, L"LOCALAPPDATA") == 0 && base != nullptr) {
            std::wstring dir(base);
            free(base);
            if (!dir.empty()) {
                dir += L"\\LabProject01";
                CreateDirectoryW(dir.c_str(), NULL);
                dir += L"\\logs";
                CreateDirectoryW(dir.c_str(), NULL);
                return dir + L"\\error.log";
            }
        }
        // どちらも駄目なら従来のパスを返す（開けなければ各関数が黙って諦める）。
        return L"logs\\error.log";
    }

public:
    // エラー内容をログファイルに記録する関数。
    // className : エラーが発生したクラス名（呼び出し元を特定するために記録する）
    // funcName  : エラーが発生した関数名
    // message   : エラーの内容を説明する文字列
    // fileName  : 関連するファイル名（省略可。指定があればログに追記する）
    // stageName : 関連するステージ名（省略可。指定があればログに追記する）
    static void Error(const std::string& className, const std::string& funcName, const std::string& message, const std::string& fileName = "", const std::string& stageName = "") {
        // ログファイルを「追記モード」で開く。上書きせず既存のログの下に書き足していく。
        // 出力先は LogFilePath() が「書き込める場所」を選んで返す（初回のみ判定）。
        // ワイド文字のパスを渡しているのは、%LOCALAPPDATA% がユーザー名を含み、
        // それが日本語だった場合にナロー文字列では正しく開けないため。
        std::ofstream out(LogFilePath().c_str(), std::ios::app);
        // ファイルを開けなかった場合（アクセス権限がない等）は何もせず処理を終える。
        if (!out.is_open()) return;

        // 現在時刻を取得し、ログに残すための時刻文字列を組み立てる。
        auto now = std::chrono::system_clock::now();
        std::time_t now_c = std::chrono::system_clock::to_time_t(now);
        std::tm tm_buf;
        // localtime_s はスレッドセーフ版のローカル時刻変換関数。tm_buf に結果を格納する。
        localtime_s(&tm_buf, &now_c);

        // "YYYY-MM-DD HH:MM:SS" の形式に時刻を整形する。
        std::stringstream ss;
        ss << std::put_time(&tm_buf, "%Y-%m-%d %H:%M:%S");

        // 「[時刻] [クラス名::関数名] ERROR: メッセージ」という形式で1行分を書き込む。
        out << "[" << ss.str() << "] "
            << "[" << className << "::" << funcName << "] "
            << "ERROR: " << message;
        // ファイル名が指定されていれば、どのファイルに関するエラーかを追記する。
        if (!fileName.empty()) {
            out << " | File: " << fileName;
        }
        // ステージ名が指定されていれば、どのステージで発生したエラーかを追記する。
        if (!stageName.empty()) {
            out << " | Stage: " << stageName;
        }
        // 1件分のログの終わりとして改行を入れる。
        out << "\n";
    }

    // エラーではなく、通常の実行情報（デバッグ用のメモなど）をログファイルに記録する関数。
    // 書き込み先のファイルは Error と同じ "logs/error.log" を共有している。
    // className : ログを出力したクラス名
    // funcName  : ログを出力した関数名
    // message   : 記録したい内容
    static void Info(const std::string& className, const std::string& funcName, const std::string& message) {
        // 追記モードでログファイルを開く（出力先の決め方は Error 側のコメントを参照）。
        std::ofstream out(LogFilePath().c_str(), std::ios::app);
        // ファイルを開けなければ何もせず終了する。
        if (!out.is_open()) return;

        // 現在時刻を取得して文字列に整形する処理は Error 関数と同じ内容。
        auto now = std::chrono::system_clock::now();
        std::time_t now_c = std::chrono::system_clock::to_time_t(now);
        std::tm tm_buf;
        localtime_s(&tm_buf, &now_c);

        std::stringstream ss;
        ss << std::put_time(&tm_buf, "%Y-%m-%d %H:%M:%S");

        // 「[時刻] [クラス名::関数名] INFO: メッセージ」という形式で1行分を書き込む。
        out << "[" << ss.str() << "] "
            << "[" << className << "::" << funcName << "] "
            << "INFO: " << message << "\n";
    }
};
