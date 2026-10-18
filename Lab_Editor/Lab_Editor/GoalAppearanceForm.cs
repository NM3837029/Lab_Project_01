using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace Lab_Editor;

// ステージの「ゴールの見た目」を変える小さなダイアログ（地図の右クリック →「ゴールの見た目を変える...」）。
//
// ■ 変えられるもの
//   ・絵        … 好きな画像を選ぶ（選んだ画像は、背景設定と同じく、プロジェクトの img フォルダへコピーする）。
//                 「既定に戻す」で、全ステージ共通の絵（img/ゴール.png）へ戻る。
//   ・大きさ    … 倍率。**表示と当たり判定の両方**に掛かる（1.0倍＝従来どおり。表示48px・当たり判定32px四方）。
//
// 光の柱と「ゴール」の文字は、今までどおり出る（このダイアログでは変えない）。
//
// ■ 返す値（DialogResult が OK のときだけ意味を持つ）
//   ResultSprite … "img/xxx.png" 形式の相対パス。空文字は「既定の絵」
//   ResultScale  … 大きさの倍率
public class GoalAppearanceForm : Form
{
    public string ResultSprite { get; private set; }
    public float ResultScale { get; private set; }

    private readonly string _projectRoot;
    private readonly TextBox _txtSprite;
    private readonly PictureBox _preview;
    private readonly NumericUpDown _nudScale;
    private readonly Label _lblNote;
    private string _sprite;

    // projectRoot : プロジェクトのルート（img フォルダの場所の基準）
    // sprite      : いまの絵のパス。空なら既定の絵
    // scale       : いまの大きさの倍率
    public GoalAppearanceForm(string projectRoot, string? sprite, float scale)
    {
        _projectRoot = projectRoot;
        _sprite = sprite ?? "";
        ResultSprite = _sprite;
        ResultScale = scale <= 0 ? 1.0f : scale;

        Text = "ゴールの見た目";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        Font = UiTheme.Base;

        int y = 12;
        Controls.Add(UiTheme.CreateLabel("ゴールの絵", new Point(12, y)));
        _txtSprite = new TextBox { Location = new Point(12, y + 20), Size = new Size(280, 24), ReadOnly = true };
        Controls.Add(_txtSprite);
        var btnPick = UiTheme.CreateButton("画像を選ぶ...", new Point(298, y + 19), new Size(100, 26));
        btnPick.Click += (s, e) => PickImage();
        Controls.Add(btnPick);
        y += 56;

        // 絵のプレビュー（正方形に収めて表示する。市松模様は使わず、透明部分は薄い灰色の地に載る）
        _preview = new PictureBox
        {
            Location = new Point(12, y),
            Size = new Size(150, 150),
            SizeMode = PictureBoxSizeMode.Zoom,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.FromArgb(235, 235, 235),
        };
        Controls.Add(_preview);

        Controls.Add(UiTheme.CreateLabel("大きさ（1.0 = 今までの大きさ）", new Point(176, y)));
        _nudScale = UiTheme.CreateNumericUpDown(new Point(176, y + 20), 100, 0.25m, 8m, 2, 0.25m);
        _nudScale.Value = (decimal)Math.Clamp(ResultScale, 0.25f, 8f);
        _nudScale.ValueChanged += (s, e) => UpdateNote();
        Controls.Add(_nudScale);
        var btnReset = UiTheme.CreateButton("既定に戻す", new Point(176, y + 56), new Size(100, 26));
        btnReset.Click += (s, e) =>
        {
            _sprite = "";
            _nudScale.Value = 1.0m;
            RefreshSprite();
        };
        Controls.Add(btnReset);
        y += 160;

        _lblNote = new Label
        {
            Location = new Point(12, y),
            Size = new Size(386, 48),
            Font = UiTheme.Small,
            ForeColor = Color.DimGray,
        };
        Controls.Add(_lblNote);
        y += 56;

        var btnOk = UiTheme.CreateButton("OK", new Point(214, y), new Size(84, 28));
        UiTheme.StylePrimaryButton(btnOk);
        btnOk.Click += (s, e) =>
        {
            ResultSprite = _sprite;
            ResultScale = (float)_nudScale.Value;
            DialogResult = DialogResult.OK;
            Close();
        };
        Controls.Add(btnOk);
        var btnCancel = UiTheme.CreateButton("キャンセル", new Point(304, y), new Size(94, 28));
        UiTheme.StyleSecondaryButton(btnCancel);
        btnCancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
        Controls.Add(btnCancel);
        AcceptButton = btnOk;
        CancelButton = btnCancel;

        ClientSize = new Size(410, y + 40);
        RefreshSprite();
        UpdateNote();
    }

    // いまの絵のパスを画面（パス欄とプレビュー）へ反映する。空なら既定の絵を出す。
    private void RefreshSprite()
    {
        _txtSprite.Text = string.IsNullOrEmpty(_sprite) ? "（既定の絵：img/ゴール.png）" : _sprite;
        string rel = string.IsNullOrEmpty(_sprite) ? "img/ゴール.png" : _sprite;
        _preview.Image?.Dispose();
        _preview.Image = LoadImage(Path.Combine(_projectRoot, rel));
    }

    // ファイルを掴んだままにするとゲーム本体が同じ画像を読めなくなるので、読み切ってから Bitmap にする
    private static Image? LoadImage(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read);
            return new Bitmap(Image.FromStream(fs));
        }
        catch { return null; }
    }

    private void UpdateNote()
    {
        float k = (float)_nudScale.Value;
        _lblNote.Text = $"表示は約{(int)(48 * k)}px、当たり判定は{(int)(32 * k)}px四方になります。"
                      + "光の柱と「ゴール」の文字は、今までどおり出ます。";
    }

    // 画像を選び、プロジェクトの img フォルダへコピーして、「img/ファイル名」の相対パスを設定する。
    private void PickImage()
    {
        using var dlg = new OpenFileDialog
        {
            Title = "ゴールの画像を選択",
            Filter = "画像ファイル|*.png;*.jpg;*.bmp;*.gif|すべて|*.*",
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            string imgDir = Path.Combine(_projectRoot, "img");
            Directory.CreateDirectory(imgDir);
            string dest = Path.Combine(imgDir, Path.GetFileName(dlg.FileName));
            // すでに img フォルダ内の同じファイルなら、コピーは要らない
            if (!string.Equals(dlg.FileName, dest, StringComparison.OrdinalIgnoreCase))
                File.Copy(dlg.FileName, dest, overwrite: true);
            _sprite = "img/" + Path.GetFileName(dlg.FileName);
            RefreshSprite();
        }
        catch (Exception ex)
        {
            MessageBox.Show("画像のコピーに失敗しました:\n" + ex.Message, "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
