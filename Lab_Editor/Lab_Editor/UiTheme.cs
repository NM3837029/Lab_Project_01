using System.Drawing;
using System.Windows.Forms;

namespace Lab_Editor;

// UI改善（構造改修フェーズ1）— 各フォームがバラバラに定義していた色・フォント・
// 定型コントロール生成コードを一箇所に集約する。既存フォームの見た目を勝手に
// 変えないよう、値は既存コードで最も広く使われていたものをそのまま採用している
// (保存系ボタンの緑 RGB(40,167,69) は AssetManagerForm/BehaviorScriptEditorForm/
// HelpForm/TileEditorForm/SoundManagerForm等で既に使われている値、EventEditorForm や
// CommonEventEditorForm 等の一部旧UIだけが青 RGB(70,130,180) を使っており今回そちらを
// 緑に統一する)。
public static class UiTheme
{
    // ── 色 ──────────────────────────────────────
    // 保存系ボタン（プライマリボタン）の背景色・文字色
    public static readonly Color PrimaryButtonBack = Color.FromArgb(40, 167, 69);
    public static readonly Color PrimaryButtonFore = Color.White;

    // 注意書き・警告表示に使う背景色・文字色
    public static readonly Color NoticeBack = Color.FromArgb(255, 244, 214);
    public static readonly Color NoticeFore = Color.FromArgb(120, 80, 0);

    // 明るい背景パネルの背景色
    public static readonly Color PanelBackLight = Color.FromArgb(250, 250, 250);
    // 区切り線（セパレーター）の色
    public static readonly Color SeparatorColor = Color.Silver;

    // ── フォント (フォームごとの new Font(...) 重複生成を避けて共有する) ──
    private const string FontFamily = "Meiryo UI";
    // 通常テキスト用のフォント
    public static readonly Font Base = new(FontFamily, 9f);
    // 強調表示用の太字フォント
    public static readonly Font Bold = new(FontFamily, 9f, FontStyle.Bold);
    // 補足説明等、やや小さめの文字用フォント
    public static readonly Font Small = new(FontFamily, 8f);
    // 見出し用の少し大きめの太字フォント
    public static readonly Font Heading = new(FontFamily, 10f, FontStyle.Bold);

    // ── ボタン装飾 ──────────────────────────────
    // 保存・確定など「主要な操作」を表すボタンに、共通の緑色スタイルを適用する。
    public static void StylePrimaryButton(Button b)
    {
        b.BackColor = PrimaryButtonBack;
        b.ForeColor = PrimaryButtonFore;
        b.FlatStyle = FlatStyle.Flat;
    }

    // キャンセル等「副次的な操作」を表すボタンに、共通のフラットスタイルのみ適用する（色は変えない）。
    public static void StyleSecondaryButton(Button b)
    {
        b.FlatStyle = FlatStyle.Flat;
    }

    // ── 定型コントロール生成 (EventEditorForm等が個別に持っていた
    //    AddLabel/MakeNud/MakeButton 相当のヘルパーを共通化したもの) ──

    // 指定位置にラベルを生成する。bold=trueの場合は太字フォントを使う。
    public static Label CreateLabel(string text, Point location, bool bold = false) =>
        new()
        {
            Text = text,
            Location = location,
            AutoSize = true,
            Font = bold ? Bold : Base,
        };

    // 指定位置に数値入力欄（NumericUpDown）を、共通の既定値・範囲・刻み幅で生成する。
    // width          : 表示幅（省略時80）
    // min/max        : 入力可能な最小値・最大値
    // decimalPlaces  : 小数点以下の表示桁数
    // increment      : 上下ボタン1クリックあたりの増減量
    public static NumericUpDown CreateNumericUpDown(Point location, int width = 80,
        decimal min = -99999, decimal max = 99999, int decimalPlaces = 2, decimal increment = 1m) =>
        new()
        {
            Location = location,
            Width = width,
            Minimum = min,
            Maximum = max,
            DecimalPlaces = decimalPlaces,
            Increment = increment,
            Font = Base,
        };

    // 指定位置・サイズでボタンを生成する（フォントは共通のBaseを使う）。
    public static Button CreateButton(string text, Point location, Size size) =>
        new()
        {
            Text = text,
            Location = location,
            Size = size,
            Font = Base,
        };

    // 指定位置・幅で、高さ1ピクセルの水平な区切り線を生成する。
    public static Panel CreateSeparator(Point location, int width) =>
        new()
        {
            Location = location,
            Size = new Size(width, 1),
            BackColor = SeparatorColor,
        };

    // ── フォームのクロム共通化 ───────────────────
    // 旧UI (FixedDialog/MaximizeBox=false) を解除し、現状のSizeをMinimumSizeへ
    // 昇格させることでリサイズ可能にしつつ、内容が切れるほど小さくできないようにする。
    // form : 適用対象のフォーム
    public static void ApplyResizableChrome(Form form)
    {
        // リサイズ可能にする前に、現在のサイズを最小サイズとして記憶しておく
        // （これより小さくすると内容が見切れてしまうため）
        Size minSize = form.Size;
        form.FormBorderStyle = FormBorderStyle.Sizable;
        form.MaximizeBox = true;
        form.MinimizeBox = true;
        form.MinimumSize = minSize;
    }
}

// SplitContainer の分割位置と最小幅を、例外を出さずに設定するための共通処理。
//
// 【なぜ必要か】
// SplitterDistance / Panel1MinSize / Panel2MinSize は、コントロールが十分な大きさを持つ前に
// 設定すると「SplitterDistance は Panel1MinSize と Panel2MinSize の間でなければなりません」という
// InvalidOperationException になる。ページがシェルへ組み込まれる直前（Load の時点）は、
// 親がまだ小さい／ウィンドウが小さいことがあり、パーツ編集を開いた瞬間にエラーになっていた。
public static class SplitLayout
{
    // 戻り値: 設定できたか。幅（高さ）が足りないときは何もせず false を返すので、
    //         呼び出し側は大きさが変わったときにもう一度呼べばよい。
    public static bool Apply(SplitContainer sc, int panel1Min, int panel2Min, int distance)
    {
        int span = (sc.Orientation == Orientation.Vertical ? sc.Width : sc.Height) - sc.SplitterWidth;
        if (span < panel1Min + panel2Min + 20) return false;
        try
        {
            // 先に分割位置を「確実に収まる範囲」へ置き、そのあとで最小幅を上げる。
            // 逆順だと、最小幅の設定が分割位置を範囲外へ押し出して例外になる。
            int d = Math.Clamp(distance, panel1Min, span - panel2Min);
            sc.SplitterDistance = Math.Clamp(d, 25, Math.Max(25, span - 25));
            sc.Panel1MinSize = panel1Min;
            sc.Panel2MinSize = panel2Min;
            sc.SplitterDistance = d;
            return true;
        }
        catch (InvalidOperationException) { return false; }
        catch (ArgumentException) { return false; }
    }
}
