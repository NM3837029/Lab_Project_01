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
    public static readonly Color PrimaryButtonBack = Color.FromArgb(40, 167, 69);
    public static readonly Color PrimaryButtonFore = Color.White;

    public static readonly Color NoticeBack = Color.FromArgb(255, 244, 214);
    public static readonly Color NoticeFore = Color.FromArgb(120, 80, 0);

    public static readonly Color PanelBackLight = Color.FromArgb(250, 250, 250);
    public static readonly Color SeparatorColor = Color.Silver;

    // ── フォント (フォームごとの new Font(...) 重複生成を避けて共有する) ──
    private const string FontFamily = "Meiryo UI";
    public static readonly Font Base = new(FontFamily, 9f);
    public static readonly Font Bold = new(FontFamily, 9f, FontStyle.Bold);
    public static readonly Font Small = new(FontFamily, 8f);
    public static readonly Font Heading = new(FontFamily, 10f, FontStyle.Bold);

    // ── ボタン装飾 ──────────────────────────────
    public static void StylePrimaryButton(Button b)
    {
        b.BackColor = PrimaryButtonBack;
        b.ForeColor = PrimaryButtonFore;
        b.FlatStyle = FlatStyle.Flat;
    }

    public static void StyleSecondaryButton(Button b)
    {
        b.FlatStyle = FlatStyle.Flat;
    }

    // ── 定型コントロール生成 (EventEditorForm等が個別に持っていた
    //    AddLabel/MakeNud/MakeButton 相当のヘルパーを共通化したもの) ──
    public static Label CreateLabel(string text, Point location, bool bold = false) =>
        new()
        {
            Text = text,
            Location = location,
            AutoSize = true,
            Font = bold ? Bold : Base,
        };

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

    public static Button CreateButton(string text, Point location, Size size) =>
        new()
        {
            Text = text,
            Location = location,
            Size = size,
            Font = Base,
        };

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
    public static void ApplyResizableChrome(Form form)
    {
        Size minSize = form.Size;
        form.FormBorderStyle = FormBorderStyle.Sizable;
        form.MaximizeBox = true;
        form.MinimizeBox = true;
        form.MinimumSize = minSize;
    }
}
