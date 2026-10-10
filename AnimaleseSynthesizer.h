#pragma once
#include <algorithm>
#include <cmath>
#include <cstdint>
#include <fstream>
#include <map>
#include <random>
#include <string>
#include <vector>
#if __has_include("DxLib.h")
#include "DxLib.h"
#define ANIMALESE_HAS_DXLIB 1
#endif

// ======================================================
// AnimaleseSynthesizer - 「どうぶつの森」風の音声合成エンジン
// 日本語（ひらがな・カタカナ・漢字・英記号）UTF-8対応
// 外部音声ファイル不要のプロシージャル波形合成
// ======================================================

// キャラクターの声質設定
struct AnimaleseVoice {
  float pitchMultiplier =
      1.0f; // 音高倍率 (0.7:コワイ系/低音 〜 1.4:元気系/高音)
  float speedMultiplier = 1.0f; // 再生速度倍率 (0.8:のんびり 〜 1.3:早口)
  float volume = 0.8f;          // 音量 (0.0 〜 1.0)
  float jitter = 0.05f;         // ピッチの微小な揺らぎ幅 (0.03〜0.08)

  // プリセット
  static AnimaleseVoice Normal() {
    return AnimaleseVoice{1.0f, 1.0f, 0.8f, 0.05f};
  }
  static AnimaleseVoice High() { // 元気系・ハキハキ系・子供
    return AnimaleseVoice{1.35f, 1.15f, 0.85f, 0.06f};
  }
  static AnimaleseVoice Low() { // コワイ系・オトナ系・威厳
    return AnimaleseVoice{0.75f, 0.9f, 0.8f, 0.04f};
  }
  static AnimaleseVoice Fast() { // 早口・あわてんぼう
    return AnimaleseVoice{1.1f, 1.35f, 0.8f, 0.06f};
  }
};

class AnimaleseSynthesizer {
public:
  enum class Vowel { A, I, U, E, O, N, Pause };

  struct Phoneme {
    Vowel vowel = Vowel::A;
    float pitchOffset = 0.0f;      // 半音単位等の微調整
    bool isConsonantBurst = false; // か行・た行などの子音アタック
    bool isFricative = false;      // さ行・は行などの摩擦ノイズ
    bool isPause = false;          // 読点やスペース
  };

#pragma pack(push, 1)
  // 16bit PCM モノラル WAVヘッダー構造体
  struct WavHeader {
    char riff[4] = {'R', 'I', 'F', 'F'};
    uint32_t fileSize = 0;
    char wave[4] = {'W', 'A', 'V', 'E'};
    char fmt[4] = {'f', 'm', 't', ' '};
    uint32_t fmtSize = 16;
    uint16_t audioFormat = 1; // PCM
    uint16_t numChannels = 1; // モノラル
    uint32_t sampleRate = 44100;
    uint32_t byteRate = 44100 * 1 * 2;
    uint16_t blockAlign = 2;
    uint16_t bitsPerSample = 16;
    char data[4] = {'d', 'a', 't', 'a'};
    uint32_t dataSize = 0;
  };
#pragma pack(pop)

private:
  const uint32_t sampleRate = 44100;

  // 母音ごとのフォルマント周波数 [F1, F2, F3] (Hz)
  void GetFormantFrequencies(Vowel v, float &f1, float &f2, float &f3) const {
    switch (v) {
    case Vowel::A:
      f1 = 800.0f;
      f2 = 1250.0f;
      f3 = 2500.0f;
      break;
    case Vowel::I:
      f1 = 300.0f;
      f2 = 2300.0f;
      f3 = 3000.0f;
      break;
    case Vowel::U:
      f1 = 360.0f;
      f2 = 1200.0f;
      f3 = 2400.0f;
      break;
    case Vowel::E:
      f1 = 530.0f;
      f2 = 1850.0f;
      f3 = 2500.0f;
      break;
    case Vowel::O:
      f1 = 500.0f;
      f2 = 900.0f;
      f3 = 2400.0f;
      break;
    case Vowel::N:
      f1 = 280.0f;
      f2 = 1100.0f;
      f3 = 2200.0f;
      break;
    default:
      f1 = 400.0f;
      f2 = 1200.0f;
      f3 = 2400.0f;
      break;
    }
  }

  // UTF-8 文字列から Unicode コードポイントを1つ読み進めるヘルパー
  static uint32_t ReadUtf8CodePoint(const std::string &str, size_t &index) {
    if (index >= str.size())
      return 0;
    unsigned char c0 = static_cast<unsigned char>(str[index]);

    if (c0 < 0x80) {
      index += 1;
      return c0;
    } else if ((c0 & 0xE0) == 0xC0) {
      if (index + 1 >= str.size()) {
        index += 1;
        return 0;
      }
      unsigned char c1 = static_cast<unsigned char>(str[index + 1]);
      index += 2;
      return ((c0 & 0x1F) << 6) | (c1 & 0x3F);
    } else if ((c0 & 0xF0) == 0xE0) {
      if (index + 2 >= str.size()) {
        index += 1;
        return 0;
      }
      unsigned char c1 = static_cast<unsigned char>(str[index + 1]);
      unsigned char c2 = static_cast<unsigned char>(str[index + 2]);
      index += 3;
      return ((c0 & 0x0F) << 12) | ((c1 & 0x3F) << 6) | (c2 & 0x3F);
    } else if ((c0 & 0xF8) == 0xF0) {
      if (index + 3 >= str.size()) {
        index += 1;
        return 0;
      }
      unsigned char c1 = static_cast<unsigned char>(str[index + 1]);
      unsigned char c2 = static_cast<unsigned char>(str[index + 2]);
      unsigned char c3 = static_cast<unsigned char>(str[index + 3]);
      index += 4;
      return ((c0 & 0x07) << 18) | ((c1 & 0x3F) << 12) | ((c2 & 0x3F) << 6) |
             (c3 & 0x3F);
    }
    index += 1;
    return 0;
  }

  // コードポイントから音素（母音・子音・イントネーション）へ変換
  Phoneme CodePointToPhoneme(uint32_t cp, uint32_t nextCp) const {
    Phoneme p;

    // 空白・改行・記号の処理
    if (cp == ' ' || cp == '\t' || cp == '\n' ||
        cp == 0x3000 /* 全角スペース */) {
      p.isPause = true;
      return p;
    }
    if (cp == ',' || cp == '.' || cp == 0x3001 /* 、 */ ||
        cp == 0x3002 /* 。 */) {
      p.isPause = true;
      return p;
    }
    // 促音「っ」「ッ」
    if (cp == 0x3063 || cp == 0x30C3) {
      p.isPause = true;
      return p;
    }

    // カタカナ (0x30A1〜0x30F6) を ひらがな (0x3041〜0x3096) に正規化
    if (cp >= 0x30A1 && cp <= 0x30F6) {
      cp = cp - 0x30A1 + 0x3041;
    }

    // ひらがな五十音マッピング
    if (cp >= 0x3041 && cp <= 0x3096) {
      // 五十音テーブルに基づく母音・子音判定
      // あ行: 3041(ぁ), 3042(あ) ...
      switch (cp) {
      case 0x3041:
      case 0x3042:
        p.vowel = Vowel::A;
        break;
      case 0x3043:
      case 0x3044:
        p.vowel = Vowel::I;
        break;
      case 0x3045:
      case 0x3046:
        p.vowel = Vowel::U;
        break;
      case 0x3047:
      case 0x3048:
        p.vowel = Vowel::E;
        break;
      case 0x3049:
      case 0x304A:
        p.vowel = Vowel::O;
        break;

      case 0x304B:
      case 0x304C:
        p.vowel = Vowel::A;
        p.isConsonantBurst = true;
        break; // か・が
      case 0x304D:
      case 0x304E:
        p.vowel = Vowel::I;
        p.isConsonantBurst = true;
        break; // き・ぎ
      case 0x304F:
      case 0x3050:
        p.vowel = Vowel::U;
        p.isConsonantBurst = true;
        break; // く・ぐ
      case 0x3051:
      case 0x3052:
        p.vowel = Vowel::E;
        p.isConsonantBurst = true;
        break; // け・げ
      case 0x3053:
      case 0x3054:
        p.vowel = Vowel::O;
        p.isConsonantBurst = true;
        break; // こ・ご

      case 0x3055:
      case 0x3056:
        p.vowel = Vowel::A;
        p.isFricative = true;
        break; // さ・ざ
      case 0x3057:
      case 0x3058:
        p.vowel = Vowel::I;
        p.isFricative = true;
        break; // し・じ
      case 0x3059:
      case 0x305A:
        p.vowel = Vowel::U;
        p.isFricative = true;
        break; // す・ず
      case 0x305B:
      case 0x305C:
        p.vowel = Vowel::E;
        p.isFricative = true;
        break; // せ・ぜ
      case 0x305D:
      case 0x305E:
        p.vowel = Vowel::O;
        p.isFricative = true;
        break; // そ・ぞ

      case 0x305F:
      case 0x3060:
        p.vowel = Vowel::A;
        p.isConsonantBurst = true;
        break; // た・だ
      case 0x3061:
      case 0x3062:
        p.vowel = Vowel::I;
        p.isConsonantBurst = true;
        break; // ち・ぢ
      case 0x3064:
      case 0x3065:
        p.vowel = Vowel::U;
        p.isConsonantBurst = true;
        break; // つ・づ
      case 0x3066:
      case 0x3067:
        p.vowel = Vowel::E;
        p.isConsonantBurst = true;
        break; // て・で
      case 0x3068:
      case 0x3069:
        p.vowel = Vowel::O;
        p.isConsonantBurst = true;
        break; // と・ど

      case 0x306A:
        p.vowel = Vowel::A;
        break; // な
      case 0x306B:
        p.vowel = Vowel::I;
        break; // に
      case 0x306C:
        p.vowel = Vowel::U;
        break; // ぬ
      case 0x306D:
        p.vowel = Vowel::E;
        break; // ね
      case 0x306E:
        p.vowel = Vowel::O;
        break; // の

      case 0x306F:
      case 0x3070:
      case 0x3071:
        p.vowel = Vowel::A;
        p.isFricative = true;
        break; // は・ば・ぱ
      case 0x3072:
      case 0x3073:
      case 0x3074:
        p.vowel = Vowel::I;
        p.isFricative = true;
        break; // ひ・び・ぴ
      case 0x3075:
      case 0x3076:
      case 0x3077:
        p.vowel = Vowel::U;
        p.isFricative = true;
        break; // ふ・ぶ・ぷ
      case 0x3078:
      case 0x3079:
      case 0x307A:
        p.vowel = Vowel::E;
        p.isFricative = true;
        break; // へ・べ・ぺ
      case 0x307B:
      case 0x307C:
      case 0x307D:
        p.vowel = Vowel::O;
        p.isFricative = true;
        break; // ほ・ぼ・ぽ

      case 0x307E:
        p.vowel = Vowel::A;
        break; // ま
      case 0x307F:
        p.vowel = Vowel::I;
        break; // み
      case 0x3080:
        p.vowel = Vowel::U;
        break; // む
      case 0x3081:
        p.vowel = Vowel::E;
        break; // め
      case 0x3082:
        p.vowel = Vowel::O;
        break; // も

      case 0x3083:
      case 0x3084:
        p.vowel = Vowel::A;
        break; // ゃ・や
      case 0x3085:
      case 0x3086:
        p.vowel = Vowel::U;
        break; // ゅ・ゆ
      case 0x3087:
      case 0x3088:
        p.vowel = Vowel::O;
        break; // ょ・よ

      case 0x3089:
        p.vowel = Vowel::A;
        break; // ら
      case 0x308A:
        p.vowel = Vowel::I;
        break; // り
      case 0x308B:
        p.vowel = Vowel::U;
        break; // る
      case 0x308C:
        p.vowel = Vowel::E;
        break; // れ
      case 0x308D:
        p.vowel = Vowel::O;
        break; // ろ

      case 0x308E:
      case 0x308F:
        p.vowel = Vowel::A;
        break; // ゎ・わ
      case 0x3092:
        p.vowel = Vowel::O;
        break; // を
      case 0x3093:
        p.vowel = Vowel::N;
        break; // ん
      default:
        p.vowel = Vowel::A;
        break;
      }

      // コードポイントに応じた自然な音高分散（キャラクターの抑揚）
      p.pitchOffset = static_cast<float>((cp % 7) - 3) * 0.04f;
    }
    // 長音「ー」
    else if (cp == 0x30FC) {
      p.vowel = Vowel::O; // 前の母音に類似
      p.pitchOffset = -0.02f;
    }
    // 英字 (A-Z, a-z)
    else if ((cp >= 'a' && cp <= 'z') || (cp >= 'A' && cp <= 'Z')) {
      char lower = static_cast<char>(std::tolower(cp));
      if (lower == 'a')
        p.vowel = Vowel::A;
      else if (lower == 'i')
        p.vowel = Vowel::I;
      else if (lower == 'u')
        p.vowel = Vowel::U;
      else if (lower == 'e')
        p.vowel = Vowel::E;
      else if (lower == 'o')
        p.vowel = Vowel::O;
      else {
        // 子音字はハッシュで母音を決定
        Vowel vowels[] = {Vowel::A, Vowel::I, Vowel::U, Vowel::E, Vowel::O};
        p.vowel = vowels[lower % 5];
        if (lower == 'k' || lower == 't' || lower == 'p')
          p.isConsonantBurst = true;
        if (lower == 's' || lower == 'f' || lower == 'h')
          p.isFricative = true;
      }
      p.pitchOffset = static_cast<float>((cp % 5) - 2) * 0.05f;
    }
    // 漢字およびその他の文字（ハッシュ値から自然な母音と音高を割り当て）
    else {
      Vowel vowels[] = {Vowel::A, Vowel::I, Vowel::U,
                        Vowel::E, Vowel::O, Vowel::N};
      p.vowel = vowels[cp % 6];
      p.pitchOffset = static_cast<float>((cp % 9) - 4) * 0.03f;
      if ((cp % 4) == 0)
        p.isConsonantBurst = true;
      if ((cp % 5) == 0)
        p.isFricative = true;
    }

    // 次の文字が疑問符「？」「?」ならピッチを跳ね上げる（疑問のイントネーション）
    if (nextCp == '?' || nextCp == 0xFF1F /* 全角？ */) {
      p.pitchOffset += 0.35f;
    }

    return p;
  }

  // コードポイントを50音サンプルファイル名（キー）に変換する
  // 濁音・半濁音は対応する清音に自動フォールバック
  std::string CodePointToKey(uint32_t cp) const {
    // 空白・句読点・促音「っ」は pause
    if (cp == ' ' || cp == '\t' || cp == '\n' || cp == 0x3000 || cp == ',' ||
        cp == '.' || cp == 0x3001 || cp == 0x3002 || cp == 0x3063 ||
        cp == 0x30C3) {
      return "pause";
    }

    // カタカナ (0x30A1〜0x30F6) を ひらがなに正規化
    if (cp >= 0x30A1 && cp <= 0x30F6) {
      cp = cp - 0x30A1 + 0x3041;
    }

    // ひらがな五十音マッピング（濁音・半濁音は清音へ）
    switch (cp) {
    case 0x3041:
    case 0x3042:
      return "a";
    case 0x3043:
    case 0x3044:
      return "i";
    case 0x3045:
    case 0x3046:
      return "u";
    case 0x3047:
    case 0x3048:
      return "e";
    case 0x3049:
    case 0x304A:
      return "o";

    case 0x304B:
    case 0x304C:
      return "ka"; // か, が -> ka
    case 0x304D:
    case 0x304E:
      return "ki"; // き, ぎ -> ki
    case 0x304F:
    case 0x3050:
      return "ku"; // く, ぐ -> ku
    case 0x3051:
    case 0x3052:
      return "ke"; // け, げ -> ke
    case 0x3053:
    case 0x3054:
      return "ko"; // こ, ご -> ko

    case 0x3055:
    case 0x3056:
      return "sa"; // さ, ざ -> sa
    case 0x3057:
    case 0x3058:
      return "shi"; // し, じ -> shi
    case 0x3059:
    case 0x305A:
      return "su"; // す, ず -> su
    case 0x305B:
    case 0x305C:
      return "se"; // せ, ぜ -> se
    case 0x305D:
    case 0x305E:
      return "so"; // そ, ぞ -> so

    case 0x305F:
    case 0x3060:
      return "ta"; // た, だ -> ta
    case 0x3061:
    case 0x3062:
      return "ti"; // ち, ぢ -> ti
    case 0x3064:
    case 0x3065:
      return "tu"; // つ, づ -> tu
    case 0x3066:
    case 0x3067:
      return "te"; // て, で -> te
    case 0x3068:
    case 0x3069:
      return "to"; // と, ど -> to

    case 0x306A:
      return "na";
    case 0x306B:
      return "ni";
    case 0x306C:
      return "nu";
    case 0x306D:
      return "ne";
    case 0x306E:
      return "no";

    case 0x306F:
    case 0x3070:
    case 0x3071:
      return "ha"; // は, ば, ぱ -> ha
    case 0x3072:
    case 0x3073:
    case 0x3074:
      return "hi"; // ひ, び, ぴ -> hi
    case 0x3075:
    case 0x3076:
    case 0x3077:
      return "hu"; // ふ, ぶ, ぷ -> hu
    case 0x3078:
    case 0x3079:
    case 0x307A:
      return "he"; // へ, べ, ぺ -> he
    case 0x307B:
    case 0x307C:
    case 0x307D:
      return "ho"; // ほ, ぼ, ぽ -> ho

    case 0x307E:
      return "ma";
    case 0x307F:
      return "mi";
    case 0x3080:
      return "mu";
    case 0x3081:
      return "me";
    case 0x3082:
      return "mo";

    case 0x3083:
    case 0x3084:
      return "ya"; // ゃ, や -> ya
    case 0x3085:
    case 0x3086:
      return "yu"; // ゅ, ゆ -> yu
    case 0x3087:
    case 0x3088:
      return "yo"; // ょ, よ -> yo

    case 0x3089:
      return "ra";
    case 0x308A:
      return "ri";
    case 0x308B:
      return "ru";
    case 0x308C:
      return "re";
    case 0x308D:
      return "ro";

    case 0x308E:
    case 0x308F:
      return "wa"; // ゎ, わ -> wa
    case 0x3092:
      return "wo"; // を -> wo
    case 0x3093:
      return "nn"; // ん -> nn
    case 0x30FC:
      return "repeat"; // 長音「ー」は前の音をリピート
    default:
      break;
    }

    // 英字の場合
    if ((cp >= 'a' && cp <= 'z') || (cp >= 'A' && cp <= 'Z')) {
      char lower = static_cast<char>(std::tolower(cp));
      if (lower == 'a')
        return "a";
      if (lower == 'i')
        return "i";
      if (lower == 'u')
        return "u";
      if (lower == 'e')
        return "e";
      if (lower == 'o')
        return "o";
      static const char *consKeys[] = {"ka", "sa", "ta", "na", "ha",
                                       "ma", "ya", "ra", "wa", "nn"};
      return consKeys[lower % 10];
    }

    // 漢字やその他の文字はハッシュから50音に割り当て
    static const char *all50Keys[] = {
        "a",  "i",  "u",  "e",  "o",  "ka", "ki", "ku", "ke", "ko", "sa", "shi",
        "su", "se", "so", "ta", "ti", "tu", "te", "to", "na", "ni", "nu", "ne",
        "no", "ha", "hi", "hu", "he", "ho", "ma", "mi", "mu", "me", "mo", "ya",
        "yu", "yo", "ra", "ri", "ru", "re", "ro", "wa", "wo", "nn"};
    return all50Keys[cp % 46];
  }

  std::map<std::string, int> sampleHandles; // 50音サンプルのDxLibハンドル

public:
  AnimaleseSynthesizer() = default;

  // テキストから 16bit PCM サンプル列を生成
  std::vector<int16_t>
  SynthesizePcm(const std::string &utf8Text,
                const AnimaleseVoice &voice = AnimaleseVoice::Normal()) {
    std::vector<int16_t> samples;
    if (utf8Text.empty())
      return samples;

    // 疑似乱数生成器（シード固定または再現性のある乱数）
    std::mt19937 rng(42);
    std::uniform_real_distribution<float> jitterDist(-voice.jitter,
                                                     voice.jitter);
    std::uniform_real_distribution<float> noiseDist(-0.15f, 0.15f);

    // 1文字あたりの持続時間（基本 60ms 〜 70ms）
    float charDuration = 0.065f / (std::max)(0.2f, voice.speedMultiplier);
    int samplesPerChar = static_cast<int>(sampleRate * charDuration);

    // UTF-8 文字をコードポイント列へ分解
    std::vector<uint32_t> codePoints;
    size_t idx = 0;
    while (idx < utf8Text.size()) {
      uint32_t cp = ReadUtf8CodePoint(utf8Text, idx);
      if (cp != 0) {
        codePoints.push_back(cp);
      }
    }

    // 基本ピッチ（ベース周波数: 260Hz 程度）
    const float baseF0 = 260.0f * voice.pitchMultiplier;

    for (size_t i = 0; i < codePoints.size(); ++i) {
      uint32_t cp = codePoints[i];
      uint32_t nextCp = (i + 1 < codePoints.size()) ? codePoints[i + 1] : 0;

      Phoneme ph = CodePointToPhoneme(cp, nextCp);

      // 休符（句読点・空白）の場合
      if (ph.isPause) {
        int pauseSamples = static_cast<int>(samplesPerChar * 0.8f);
        samples.insert(samples.end(), pauseSamples, 0);
        continue;
      }

      // この文字のピッチ周波数
      float f0 = baseF0 * (1.0f + ph.pitchOffset + jitterDist(rng));
      f0 = (std::max)(80.0f, (std::min)(1600.0f, f0)); // ガード

      // フォルマント周波数
      float form1, form2, form3;
      GetFormantFrequencies(ph.vowel, form1, form2, form3);

      // 1文字分の波形生成
      for (int s = 0; s < samplesPerChar; ++s) {
        float t = static_cast<float>(s) / sampleRate;

        // 1. 基本音源（パルス波＋サイン波のブレンドでファミコン/レトロ音響）
        float phase = std::fmod(t * f0, 1.0f);
        float pulse = (phase < 0.45f) ? 0.6f : -0.6f;
        float subSine = 0.4f * std::sin(2.0f * 3.14159265f * f0 * t);
        float rawWave = pulse * 0.6f + subSine * 0.4f;

        // 2. 母音フォルマントによる共鳴倍音（レゾナンスの付加）
        float formantRes = 0.35f * std::sin(2.0f * 3.14159265f * form1 * t) +
                           0.25f * std::sin(2.0f * 3.14159265f * form2 * t) +
                           0.15f * std::sin(2.0f * 3.14159265f * form3 * t);

        // 3. 子音アタック（破裂音や摩擦ノイズ）
        float noise = 0.0f;
        float progress = static_cast<float>(s) / samplesPerChar;
        if (ph.isConsonantBurst && progress < 0.2f) {
          noise = noiseDist(rng) * (1.0f - progress / 0.2f) * 1.5f;
        } else if (ph.isFricative && progress < 0.35f) {
          noise = noiseDist(rng) * 0.8f;
        }

        // 4. 合成とエンベロープ（アタック・ディケイによるクリック音防止）
        float envelope = 1.0f;
        if (progress < 0.15f) {
          envelope = progress / 0.15f; // アタック（フェードイン）
        } else if (progress > 0.70f) {
          envelope = (1.0f - progress) / 0.30f; // リリース（フェードアウト）
        }

        float sampleVal = (rawWave * 0.5f + formantRes * 0.5f + noise) *
                          envelope * voice.volume;
        sampleVal = (std::max)(-1.0f, (std::min)(1.0f, sampleVal));

        samples.push_back(static_cast<int16_t>(sampleVal * 32767.0f));
      }
    }

    return samples;
  }

  // WAV形式のバイナリイメージ（ヘッダー + PCMデータ）をメモリ上に構築
  // DxLibの LoadSoundMemByMemImage() に渡すのに最適
  std::vector<uint8_t>
  CreateWavMemoryImage(const std::string &utf8Text,
                       const AnimaleseVoice &voice = AnimaleseVoice::Normal()) {
    std::vector<int16_t> pcm = SynthesizePcm(utf8Text, voice);
    if (pcm.empty())
      return {};

    WavHeader header;
    header.sampleRate = sampleRate;
    header.byteRate = sampleRate * 1 * sizeof(int16_t);
    header.blockAlign = sizeof(int16_t);
    header.bitsPerSample = 16;
    header.dataSize = static_cast<uint32_t>(pcm.size() * sizeof(int16_t));
    header.fileSize = sizeof(WavHeader) - 8 + header.dataSize;

    std::vector<uint8_t> wavBytes(sizeof(WavHeader) + header.dataSize);
    std::memcpy(wavBytes.data(), &header, sizeof(WavHeader));
    std::memcpy(wavBytes.data() + sizeof(WavHeader), pcm.data(),
                header.dataSize);

    return wavBytes;
  }

#ifdef ANIMALESE_HAS_DXLIB
  // DxLib用のサウンドハンドルを直接メモリから作成（ファイル作成不要！）
  // 戻り値: DxLibサウンドハンドル (失敗時は -1)
  int CreateDxLibSoundHandle(
      const std::string &utf8Text,
      const AnimaleseVoice &voice = AnimaleseVoice::Normal()) {
    std::vector<uint8_t> wavImage = CreateWavMemoryImage(utf8Text, voice);
    if (wavImage.empty())
      return -1;

    int handle = LoadSoundMemByMemImage(wavImage.data(),
                                        static_cast<int>(wavImage.size()));
    return handle;
  }
#endif

  // 任意のファイルパスに .wav ファイルとして書き出す
  bool SaveToWavFile(const std::string &filename, const std::string &utf8Text,
                     const AnimaleseVoice &voice = AnimaleseVoice::Normal()) {
    std::vector<uint8_t> wavImage = CreateWavMemoryImage(utf8Text, voice);
    if (wavImage.empty())
      return false;

    std::ofstream file(filename, std::ios::binary);
    if (!file.is_open())
      return false;

    file.write(reinterpret_cast<const char *>(wavImage.data()),
               wavImage.size());
    return true;
  }

  // テキストから50音キー（"a", "ka", "shi", ...）のリストを抽出
  std::vector<std::string> TextToKeys(const std::string &utf8Text) const {
    std::vector<std::string> keys;
    size_t idx = 0;
    std::string lastValidKey = "a";

    while (idx < utf8Text.size()) {
      uint32_t cp = ReadUtf8CodePoint(utf8Text, idx);
      if (cp == 0)
        continue;

      std::string key = CodePointToKey(cp);
      if (key == "repeat") {
        keys.push_back(lastValidKey);
      } else if (key == "pause") {
        keys.push_back("pause");
      } else if (!key.empty()) {
        keys.push_back(key);
        lastValidKey = key;
      }
    }
    return keys;
  }

#ifdef ANIMALESE_HAS_DXLIB
  // sound/50on
  // フォルダから自作の50音音声ファイル（46ファイル）をDxLibハンドルとして一括ロード
  bool Load50onSamples(const std::string &dirPath = "sound/50on") {
    ReleaseSamples();

    static const char *keyList[] = {
        "a",  "i",  "u",  "e",  "o",  "ka", "ki", "ku", "ke", "ko", "sa", "shi",
        "su", "se", "so", "ta", "ti", "tu", "te", "to", "na", "ni", "nu", "ne",
        "no", "ha", "hi", "hu", "he", "ho", "ma", "mi", "mu", "me", "mo", "ya",
        "yu", "yo", "ra", "ri", "ru", "re", "ro", "wa", "wo", "nn"};

    int loadedCount = 0;
    for (const char *key : keyList) {
      std::string path = dirPath + "/" + key + ".wav";
      int h = LoadSoundMem(path.c_str());
      if (h >= 0) {
        sampleHandles[key] = h;
        loadedCount++;
      }
    }

    return loadedCount > 0;
  }

  // 50音サンプルが読み込み済みかどうか
  bool Has50onSamples() const { return !sampleHandles.empty(); }

  // 特定のキー（"ka" 等）に対応するサウンドハンドルを取得
  int GetSampleHandle(const std::string &key) const {
    auto it = sampleHandles.find(key);
    if (it != sampleHandles.end())
      return it->second;
    return -1;
  }

  // ロードしたサンプルの解放
  void ReleaseSamples() {
    for (auto &pair : sampleHandles) {
      if (pair.second >= 0) {
        DeleteSoundMem(pair.second);
      }
    }
    sampleHandles.clear();
  }
#else
  bool Load50onSamples(const std::string &dirPath = "sound/50on") {
    return false;
  }
  bool Has50onSamples() const { return false; }
  int GetSampleHandle(const std::string &key) const { return -1; }
  void ReleaseSamples() {}
#endif

  ~AnimaleseSynthesizer() { ReleaseSamples(); }
};
