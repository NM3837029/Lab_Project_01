#pragma once
#include <string>
#include <fstream>
#include <chrono>
#include <iomanip>
#include <sstream>
#include <direct.h>

class Logger {
public:
    static void Error(const std::string& className, const std::string& funcName, const std::string& message, const std::string& fileName = "", const std::string& stageName = "") {
        _mkdir("logs"); // logsディレクトリが存在しない場合は作成
        std::ofstream out("logs/error.log", std::ios::app);
        if (!out.is_open()) return;

        auto now = std::chrono::system_clock::now();
        std::time_t now_c = std::chrono::system_clock::to_time_t(now);
        std::tm tm_buf;
        localtime_s(&tm_buf, &now_c);

        std::stringstream ss;
        ss << std::put_time(&tm_buf, "%Y-%m-%d %H:%M:%S");

        out << "[" << ss.str() << "] "
            << "[" << className << "::" << funcName << "] "
            << "ERROR: " << message;
        if (!fileName.empty()) {
            out << " | File: " << fileName;
        }
        if (!stageName.empty()) {
            out << " | Stage: " << stageName;
        }
        out << "\n";
    }

    static void Info(const std::string& className, const std::string& funcName, const std::string& message) {
        _mkdir("logs");
        std::ofstream out("logs/error.log", std::ios::app);
        if (!out.is_open()) return;

        auto now = std::chrono::system_clock::now();
        std::time_t now_c = std::chrono::system_clock::to_time_t(now);
        std::tm tm_buf;
        localtime_s(&tm_buf, &now_c);

        std::stringstream ss;
        ss << std::put_time(&tm_buf, "%Y-%m-%d %H:%M:%S");

        out << "[" << ss.str() << "] "
            << "[" << className << "::" << funcName << "] "
            << "INFO: " << message << "\n";
    }
};
