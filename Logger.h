#pragma once
#include <string>
#include <fstream>
#include <chrono>
#include <iomanip>
#include <sstream>
#include <direct.h>

// ゲーム全体で共通して使うログ出力クラス。
// エラー情報や実行情報を "logs/error.log" というテキストファイルに
// 追記していくだけのシンプルな仕組みで、すべてstatic関数として提供しているため
// インスタンス化せずに Logger::Error(...) のようにどこからでも呼び出せる。
class Logger {
public:
    // エラー内容をログファイルに記録する関数。
    // className : エラーが発生したクラス名（呼び出し元を特定するために記録する）
    // funcName  : エラーが発生した関数名
    // message   : エラーの内容を説明する文字列
    // fileName  : 関連するファイル名（省略可。指定があればログに追記する）
    // stageName : 関連するステージ名（省略可。指定があればログに追記する）
    static void Error(const std::string& className, const std::string& funcName, const std::string& message, const std::string& fileName = "", const std::string& stageName = "") {
        // logs フォルダがまだ存在しない場合に備えて、書き込み前に必ず作成しておく。
        // 既に存在する場合でも _mkdir はエラーを返すだけで問題は起きない。
        _mkdir("logs");
        // ログファイルを「追記モード」で開く。上書きせず既存のログの下に書き足していく。
        std::ofstream out("logs/error.log", std::ios::app);
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
        // Error と同様に、logs フォルダが無ければ作成しておく。
        _mkdir("logs");
        // 追記モードでログファイルを開く。
        std::ofstream out("logs/error.log", std::ios::app);
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
