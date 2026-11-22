namespace Lab_Editor;

// ======================================================
// GradeCondition - 色・明るさで出入りするギミックの「出る条件」を扱う共通のモデル
//
// 対象のギミック：
//   ・補正スイッチ(26)／補正ロック足場(27) … 「見る値」と「下限〜上限」の範囲条件
//   ・色ロック足場(22) … 赤・緑・青のどれか（置いた物の param に "1"/"2"/"3"）
//   ・明暗ロック足場(23) … 暗い・明るい（置いた物の param に "dark"/"bright"）
//
// 以前はこれらを「数字を打つ」「文字を打つ」で決めていて、値ごとに単位も範囲も違ううえ、
// -999／999（未指定）や "1"／"dark" といった約束を覚えていないと決められなかった。
// ここに「値ごとの情報」「文章にする」「表示の単位に直す」「param との変換」をまとめ、
// アセット画面・ステージの配置・地図の印のすべてが同じ言葉と同じ範囲で条件を見せられるようにする。
//
// 【重要】ゲーム側 DrawPixel.cpp の次の関数と、値も言い方も一致させること。
//   GradeRange（未指定時の既定範囲）・GradeConditionLabel（条件の文字）・GradeConditionColor（条件の色）
//   色ロック足場の param の読み方（空・不正は赤）・明暗ロック足場の param の読み方（"bright" 以外は暗い）
// ======================================================

// 条件の種類（GradeConditionEditor の表示の切り替えにも使う）
public enum GradeKind
{
    Range,      // 補正スイッチ・補正ロック足場：見る値＋下限〜上限
    ColorLock,  // 色ロック足場：赤・緑・青
    BrightLock, // 明暗ロック足場：暗い・明るい
}

// 見る値1つぶんの情報。ゲームの内部の値と、人に見せる値（％や度）の両方を扱う。
public sealed class GradeChannelInfo
{
    public int Id;                         // 見る値の番号（0〜7。ゲームの gradeChannel と同じ）
    public string Name = "";               // 表示名（例：明るさ）
    public string Unit = "";               // 表示の単位（％・度・倍。コントラストと色温度は単位なしの ±100）
    public float ViewMin, ViewMax;          // つまみで動かせる範囲（表示の単位で）
    public float DefLo, DefHi;              // 下限・上限が両方とも未指定のときの既定範囲（ゲームの内部の値。-999/999 は端なし）
    public Func<float, float> ToView = v => v;   // ゲームの内部の値 → 表示の値
    public Func<float, float> FromView = v => v; // 表示の値 → ゲームの内部の値
    public Color Low = Color.Black, High = Color.White; // 帯のグラデーションの両端の色（色相だけは虹で別に描く）
    public bool IsFilter => Id == 5;        // 色フィルタだけは範囲ではなく「赤・緑・青」を選ぶ
}

public static class GradeCondition
{
    // 「未指定」を表す値。ゲーム側と同じく -999 / 999（判定は ±998 を越えているか）
    public const float NoLo = -999f, NoHi = 999f;
    public static bool IsNoLo(float v) => v <= -998f;
    public static bool IsNoHi(float v) => v >= 998f;

    // 見る値の一覧。並び（添字）はゲームの gradeChannel の番号と同じ。
    // 範囲は ColorGrade.h の PanelState のコメント（明るさ 0.3〜1.7・コントラスト -1〜1・彩度 0〜2・色相 -180〜180・色温度 -1〜1）、
    // 既定範囲は DrawPixel.cpp の GradeRange の L/H 表をそのまま写している。
    private static readonly GradeChannelInfo[] Channels =
    {
        new() { Id = 0, Name = "明るさ", Unit = "%", ViewMin = 30, ViewMax = 170, DefLo = NoLo, DefHi = 0.6f,
                ToView = v => v * 100f, FromView = v => v / 100f, Low = Color.FromArgb(20, 20, 30), High = Color.FromArgb(255, 250, 220) },
        new() { Id = 1, Name = "コントラスト", Unit = "", ViewMin = -100, ViewMax = 100, DefLo = 0.3f, DefHi = NoHi,
                ToView = v => v * 100f, FromView = v => v / 100f, Low = Color.FromArgb(150, 150, 150), High = Color.FromArgb(20, 20, 20) },
        new() { Id = 2, Name = "彩度", Unit = "%", ViewMin = 0, ViewMax = 200, DefLo = NoLo, DefHi = 0.2f,
                ToView = v => v * 100f, FromView = v => v / 100f, Low = Color.FromArgb(150, 150, 150), High = Color.FromArgb(255, 40, 160) },
        new() { Id = 3, Name = "色相", Unit = "度", ViewMin = -180, ViewMax = 180, DefLo = 30f, DefHi = 180f,
                ToView = v => v, FromView = v => v, Low = Color.Red, High = Color.Red },
        new() { Id = 4, Name = "色温度", Unit = "", ViewMin = -100, ViewMax = 100, DefLo = 0.3f, DefHi = NoHi,
                ToView = v => v * 100f, FromView = v => v / 100f, Low = Color.FromArgb(80, 140, 255), High = Color.FromArgb(255, 160, 60) },
        new() { Id = 5, Name = "色フィルタ", Unit = "", ViewMin = 1, ViewMax = 3, DefLo = 1f, DefHi = 1f,
                ToView = v => v, FromView = v => v, Low = Color.Red, High = Color.Blue },
        new() { Id = 6, Name = "拡大中（Zキー）", Unit = "倍", ViewMin = 1.0f, ViewMax = 2.0f, DefLo = 1.15f, DefHi = NoHi,
                ToView = v => v, FromView = v => v, Low = Color.FromArgb(200, 235, 255), High = Color.FromArgb(40, 160, 230) },
        new() { Id = 7, Name = "縮小中（Vキー）", Unit = "倍", ViewMin = 0.5f, ViewMax = 1.0f, DefLo = NoLo, DefHi = 0.95f,
                ToView = v => v, FromView = v => v, Low = Color.FromArgb(230, 140, 40), High = Color.FromArgb(255, 235, 200) },
    };

    public static int ChannelCount => Channels.Length;

    // 見る値の情報を返す。範囲外の番号（-1 = 未設定を含む）は、ゲームと同じく「明るさ」として扱う。
    public static GradeChannelInfo Channel(int ch) => (ch < 0 || ch >= Channels.Length) ? Channels[0] : Channels[ch];

    // ゲームの gradeChannel（float）を番号へ。ゲームの GradeChannelOf と同じく四捨五入し、範囲外は0。
    public static int ChannelOf(float gradeChannel)
    {
        int ch = (int)Math.Floor(gradeChannel + 0.5f);
        return (ch < 0 || ch > 7) ? 0 : ch;
    }

    // 実際に判定に使われる範囲。下限・上限が両方とも未指定なら、見る値ごとの既定範囲になる（ゲームの GradeRange と同じ）。
    // 片方だけ未指定なら、そちら側は端なし（-999 / 999）のまま。
    public static void EffectiveRange(int ch, float lo, float hi, out float effLo, out float effHi)
    {
        var info = Channel(ch);
        if (IsNoLo(lo) && IsNoHi(hi)) { effLo = info.DefLo; effHi = info.DefHi; return; }
        effLo = lo; effHi = hi;
    }

    // ゲームの printf の %+d と同じ書き方（正なら +、0 は +0）。四捨五入もゲームと同じく 0 から遠い側へ。
    private static string Signed(float v) { int n = (int)(v + (v < 0 ? -0.5f : 0.5f)); return (n >= 0 ? "+" : "") + n.ToString(); }
    private static int Pct(float v) => (int)(v * 100f + 0.5f);

    // 条件の短い文字。ゲームの GradeConditionLabel と1文字も違わないようにしてある（ゲーム内の足場の上に出る文字と同じ）。
    public static string Label(int ch, float lo, float hi)
    {
        ch = Channel(ch).Id;
        EffectiveRange(ch, lo, hi, out lo, out hi);
        bool noLo = IsNoLo(lo), noHi = IsNoHi(hi);
        switch (ch)
        {
            case 0:
                if (noLo && hi <= 0.7f) return "暗いとき";
                if (noHi && lo >= 1.2f) return "明るいとき";
                if (noLo) return $"明るさ {Pct(hi)}%以下";
                if (noHi) return $"明るさ {Pct(lo)}%以上";
                return $"明るさ {Pct(lo)}〜{Pct(hi)}%";
            case 1:
                if (noHi) return $"コントラスト {Signed(lo * 100f)}以上";
                if (noLo) return $"コントラスト {Signed(hi * 100f)}以下";
                return $"コントラスト {Signed(lo * 100f)}〜{Signed(hi * 100f)}";
            case 2:
                if (noLo && hi <= 0.3f) return "白黒のとき";
                if (noHi && lo >= 1.2f) return "あざやかなとき";
                if (noLo) return $"彩度 {Pct(hi)}%以下";
                if (noHi) return $"彩度 {Pct(lo)}%以上";
                return $"彩度 {Pct(lo)}〜{Pct(hi)}%";
            case 3:
                if (noHi) return $"色相 {Signed(lo)}度以上";
                if (noLo) return $"色相 {Signed(hi)}度以下";
                return $"色相 {Signed(lo)}〜{Signed(hi)}度";
            case 4:
                if (noHi && lo > 0f) return "暖色にしたとき";
                if (noLo && hi < 0f) return "寒色にしたとき";
                if (noHi) return $"色温度 {Signed(lo * 100f)}以上";
                if (noLo) return $"色温度 {Signed(hi * 100f)}以下";
                return $"色温度 {Signed(lo * 100f)}〜{Signed(hi * 100f)}";
            case 6: return "拡大中（Z）";
            case 7: return "縮小中（V）";
            default:
            {
                string[] names = { "フィルタなし", "赤フィルタ", "緑フィルタ", "青フィルタ" };
                int l = Math.Max(0, (int)(lo + 0.5f)), h = Math.Min(3, (int)(hi + 0.5f));
                if (l > 3) l = 3;
                if (h < 0) h = 0;
                return l == h ? names[l] : $"{names[l]}〜{names[h]}";
            }
        }
    }

    // 条件の文字を文にする。
    //   「とき」で終わる文字（暗いとき）    → そのまま「に出る」
    //   数字で終わる文字（明るさ 30〜60%）  → 「 のときに出る」（数字と「の」がくっついて読みにくいので、空白を1つ挟む）
    //   それ以外（赤フィルタ・拡大中（Z）） → 「のときに出る」
    private static string ToSentence(string label)
    {
        if (label.EndsWith("とき")) return label + "に出る";
        char last = label.Length > 0 ? label[^1] : ' ';
        bool numeric = char.IsDigit(last) || last == '%' || last == '度' || label.EndsWith("以上") || label.EndsWith("以下");
        return label + (numeric ? " のときに出る" : "のときに出る");
    }

    // 条件を1文にしたもの（条件エディタ・ダイアログに出す）。例：「暗いときに出る」「明るさ 30〜60% のときに出る」
    public static string Sentence(int ch, float lo, float hi) => ToSentence(Label(ch, lo, hi));

    // 地図の印に出す、ごく短い文字（例：「暗い」「赤」「明≤60%」）
    public static string Short(int ch, float lo, float hi)
    {
        ch = Channel(ch).Id;
        EffectiveRange(ch, lo, hi, out float elo, out float ehi);
        string label = Label(ch, lo, hi);
        switch (ch)
        {
            case 0:
                if (label == "暗いとき") return "暗い";
                if (label == "明るいとき") return "明るい";
                if (IsNoLo(elo)) return $"明≤{Pct(ehi)}%";
                if (IsNoHi(ehi)) return $"明≥{Pct(elo)}%";
                return $"明{Pct(elo)}-{Pct(ehi)}%";
            case 1: return "コ" + label.Replace("コントラスト ", "");
            case 2:
                if (label == "白黒のとき") return "白黒";
                if (label == "あざやかなとき") return "鮮やか";
                return "彩" + label.Replace("彩度 ", "");
            case 3: return "色相" + label.Replace("色相 ", "");
            case 4:
                if (label == "暖色にしたとき") return "暖色";
                if (label == "寒色にしたとき") return "寒色";
                return "温" + label.Replace("色温度 ", "");
            case 6: return "拡大中";
            case 7: return "縮小中";
            default: return label.Replace("フィルタ", "");
        }
    }

    // 条件の色（地図の枠・見本）。ゲームの GradeConditionColor と同じ色。
    public static Color ConditionColor(int ch, float lo, float hi)
    {
        ch = Channel(ch).Id;
        EffectiveRange(ch, lo, hi, out lo, out hi);
        switch (ch)
        {
            case 0: return (hi <= 0.7f && IsNoLo(lo)) ? Color.FromArgb(60, 60, 110) : Color.FromArgb(255, 240, 150);
            case 1: return Color.FromArgb(210, 210, 220);
            case 2: return (hi <= 0.3f && IsNoLo(lo)) ? Color.FromArgb(150, 150, 150) : Color.FromArgb(255, 90, 170);
            case 3: return Color.FromArgb(170, 120, 255);
            case 4: return lo > 0f ? Color.FromArgb(255, 170, 70) : Color.FromArgb(90, 150, 255);
            case 6: return Color.FromArgb(120, 220, 255);
            case 7: return Color.FromArgb(255, 200, 120);
            default: return FilterColor((int)(lo + 0.5f));
        }
    }

    // 色フィルタの番号（1赤 2緑 3青）の色。ゲームの色ロック足場の描画と同じ色。
    public static Color FilterColor(int c) => c == 2 ? Color.FromArgb(80, 220, 80) : c == 3 ? Color.FromArgb(80, 140, 255) : Color.FromArgb(255, 90, 90);
    public static string FilterName(int c) => c == 2 ? "緑" : c == 3 ? "青" : "赤";

    // ===== 色ロック足場(22)の param =====
    // ゲーム（DrawPixel.cpp の色ロック足場）と1つも違わない読み方にする：
    //   空なら赤(1)。それ以外は C の atoi で読み（先頭の数字だけ。数字で始まらなければ 0）、
    //   ((n−1) mod 3)+1 で 1〜3 へ一周させる。つまり "0"・"abc" は青、"4" は赤、"2x" は緑になる。
    // 以前は不正な値を「赤」と見せていたが、ゲームでは青を要求していたので、地図の印と実際が食い違っていた。
    public static int ColorFromParam(string? p)
    {
        if (string.IsNullOrEmpty(p)) return 1;
        int n = Atoi(p);
        return ((((n - 1) % 3) + 3) % 3) + 1;
    }

    // C の atoi と同じ読み方：先頭の空白を飛ばし、符号と続く数字だけを読む（数字が無ければ 0）
    private static int Atoi(string s)
    {
        int i = 0;
        while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        int sign = 1;
        if (i < s.Length && (s[i] == '+' || s[i] == '-')) { if (s[i] == '-') sign = -1; i++; }
        long v = 0;
        while (i < s.Length && s[i] >= '0' && s[i] <= '9' && v < int.MaxValue) { v = v * 10 + (s[i] - '0'); i++; }
        return (int)(sign * Math.Min(v, int.MaxValue));
    }
    public static string ParamFromColor(int c) => (c >= 1 && c <= 3 ? c : 1).ToString();
    public static string ColorSentence(int c) => ToSentence(FilterName(c) + "フィルタ");
    public static Color ColorLockColor(int c) => FilterColor(c);

    // ===== 明暗ロック足場(23)の param =====
    // ゲームは param が "bright" と完全一致するときだけ「明るい」、それ以外（空・"BRIGHT" など）はすべて「暗い」。
    public static bool BrightFromParam(string? p) => p == "bright";
    public static string ParamFromBright(bool bright) => bright ? "bright" : "dark";
    public static string BrightSentence(bool bright) => bright ? "明るいときに出る" : "暗いときに出る";
    // ゲームの明暗ロック足場の色（明るい＝黄、暗い＝紺）
    public static Color BrightLockColor(bool bright) => bright ? Color.FromArgb(255, 240, 150) : Color.FromArgb(60, 60, 90);
}
