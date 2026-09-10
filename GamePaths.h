#pragma once
#include <windows.h>
#include <string>

// ゲームデータ（assets/ img/ sound/ se/）の置き場所を見つけて、
// カレントディレクトリをそこへ移すためのユーティリティ。
//
// 【なぜ必要か】
// このゲームはアセットを全てカレントディレクトリからの相対パスで参照している。
//   std::ifstream("assets/enemies.json")   LoadGraph("img/プレイヤー.png")
//   "assets/stages/" + fileName            LoadSoundMem("sound/xxx.wav")
// といった参照が4ファイルに約67箇所ある。
// 一方 exe の出力先は x64\Debug\ で、アセットはリポジトリのルート直下にあるため
// 階層が違う。つまり exe をダブルクリックしても、あるいは exe だけコピーしても
// 何も読み込めずに真っ白な画面になる。
// これまで動いていたのは Lab_Editor が WorkingDirectory にプロジェクトルートを
// 指定してゲームを起動していたからにすぎず、ゲーム単体では配布できない状態だった。
//
// 【なぜ「パス解決関数を作って67箇所を書き換える」ではなくCWDを移すのか】
// 1. 67箇所のうち10箇所はJSON由来の変数パスで、包み忘れても コンパイルは通る。
//    1箇所でも漏らすと「配布版でだけ特定の敵が見えない」という最悪の壊れ方をする。
//    CWDを移す方式なら書き換え0箇所で全部同時に解決する。
// 2. 文字コードの都合。ソースの文字列リテラルは /execution-charset:utf-8 により
//    UTF-8バイト列で、SetUseCharCodeFormat(DX_CHARCODEFORMAT_UTF8) と整合している。
//    ここで「exeのパス + 相対パス」をナロー文字列で連結する方式にすると、
//    exeパス側はCP932、リテラル側はUTF-8という混在が生まれて事故る。
//    CWDをワイド文字で一度だけ移してしまえば、以降の相対パスは今のバイト列のまま
//    何も変わらない。
namespace GamePaths {

    // 指定フォルダがゲームデータのルートかどうかを、目印ファイルの有無で判定する。
    //
    // 目印に assets\enemies.json を使う理由:
    //   Lab_Editor 側（AppPaths.cs）は Lab_Project_01.vcxproj を目印にしているが、
    //   配布物に .vcxproj は入れないので、こちらで同じ目印を使うことはできない。
    //   assets\enemies.json は開発時にも配布時にも必ず存在する。
    inline bool IsGameRoot(const std::wstring& dir) {
        std::wstring marker = dir + L"\\assets\\enemies.json";
        DWORD attr = GetFileAttributesW(marker.c_str());
        return (attr != INVALID_FILE_ATTRIBUTES) && !(attr & FILE_ATTRIBUTE_DIRECTORY);
    }

    // exe の場所を起点に親フォルダを遡ってゲームデータのルートを探し、
    // 見つかったらカレントディレクトリをそこへ移す。
    //
    //   配布時: exe と assets が同じ階層にあるので0階層目で即ヒットする
    //   開発時: exe は x64\Debug\ にあるので2階層上のリポジトリルートでヒットする
    //
    // 戻り値 false は「ゲームデータが1つも見つからなかった」を意味する。
    // その場合カレントディレクトリは一切触らないので、
    // 呼び出し側は従来どおりの挙動（CWD依存）にフォールバックすることもできるし、
    // エラーを出して終了することもできる。
    inline bool ResolveAndSetGameRoot() {
        wchar_t exePath[MAX_PATH] = { 0 };
        if (GetModuleFileNameW(NULL, exePath, MAX_PATH) == 0) return false;

        std::wstring dir(exePath);
        size_t slash = dir.find_last_of(L"\\/");
        if (slash == std::wstring::npos) return false;
        dir = dir.substr(0, slash); // exe のあるフォルダ

        // 8階層も遡れば開発時のどんな配置でも届く。
        // 上限を設けておかないと、見つからなかったときにドライブのルートまで
        // 舐め続けることになる。
        for (int i = 0; i < 8; i++) {
            if (IsGameRoot(dir)) {
                return SetCurrentDirectoryW(dir.c_str()) != 0;
            }
            size_t up = dir.find_last_of(L"\\/");
            if (up == std::wstring::npos) break; // これ以上遡れない
            std::wstring parent = dir.substr(0, up);
            if (parent == dir || parent.empty()) break; // "C:" まで来たら打ち止め
            dir = parent;
        }
        return false;
    }

    // ユーザーごとの書き込み可能フォルダ（%LOCALAPPDATA%\LabProject01）を返す。
    //
    // セーブデータやログの置き場所に使う。インストール先（Program Files配下など）へ
    // 書きに行くと権限エラーで黙って失敗するため、書き込むものはここへ集める。
    // 環境変数が取れなかった場合は空文字を返すので、呼び出し側でフォールバックすること。
    inline std::wstring UserDataDir() {
        wchar_t* base = nullptr;
        size_t len = 0;
        if (_wdupenv_s(&base, &len, L"LOCALAPPDATA") != 0 || base == nullptr) return L"";
        std::wstring dir(base);
        free(base);
        if (dir.empty()) return L"";
        dir += L"\\LabProject01";
        CreateDirectoryW(dir.c_str(), NULL); // 既に在ればERROR_ALREADY_EXISTSが返るだけ
        return dir;
    }

} // namespace GamePaths
