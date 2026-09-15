using System;
using System.Drawing;
using System.Windows.Forms;

namespace Lab_Editor;

// 配置ごとの「大きさ」と「角度」を編集する小さなダイアログ。
//
// アセット定義の大きさは「その種類の標準」で、こちらはそれを配置単位で上書きするもの。
// 同じ敵を大小いくつも並べたい、最初から傾けて置きたい、といった調整に使う。
// ステージ編集画面で配置物を右クリック →「大きさ・角度を編集...」から開く。
//
// 角度はゲーム側がラジアンで持っているが、入力は度のほうが扱いやすいので
// 画面では度で見せ、確定時にラジアンへ直して返す。
public class PlacedTransformForm : Form
{
    // 確定した値。DialogResult が OK のときだけ意味を持つ。
    public float ResultScale { get; private set; }
    public float ResultAngle { get; private set; } // ラジアン

    private readonly NumericUpDown _nudScale;
    private readonly NumericUpDown _nudAngle;

    public PlacedTransformForm(float scale, float angleRad)
    {
        Text = "大きさ・角度";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(344, 168);
        Font = UiTheme.Base;

        Controls.Add(UiTheme.CreateLabel("大きさ（1.0 = アセットの標準サイズ）", new Point(12, 14)));
        _nudScale = UiTheme.CreateNumericUpDown(new Point(12, 34), 100, 0.1m, 10m, 2, 0.1m);
        _nudScale.Value = (decimal)Math.Clamp(scale <= 0 ? 1.0f : scale, 0.1f, 10f);
        Controls.Add(_nudScale);

        Controls.Add(UiTheme.CreateLabel("角度（度）", new Point(12, 68)));
        _nudAngle = UiTheme.CreateNumericUpDown(new Point(12, 88), 100, -360m, 360m, 1, 5m);
        _nudAngle.Value = (decimal)Math.Clamp(angleRad * 180.0 / Math.PI, -360.0, 360.0);
        Controls.Add(_nudAngle);

        // よく使う角度はボタン一発で入れられるようにする（数値を打つより速い）
        int bx = 124;
        foreach (int deg in new[] { 0, 45, 90, 180 })
        {
            int captured = deg;
            // 幅を詰めすぎると "180°" が "18" のように切れてしまうので、3桁ぶんを確保する
            var b = UiTheme.CreateButton(deg + "°", new Point(bx, 87), new Size(48, 24));
            b.Click += (s, e) => _nudAngle.Value = captured;
            Controls.Add(b);
            bx += 52;
        }

        var btnOk = UiTheme.CreateButton("OK", new Point(152, 128), new Size(84, 28));
        UiTheme.StylePrimaryButton(btnOk);
        btnOk.Click += (s, e) =>
        {
            ResultScale = (float)_nudScale.Value;
            ResultAngle = (float)((double)_nudAngle.Value * Math.PI / 180.0);
            DialogResult = DialogResult.OK;
            Close();
        };
        Controls.Add(btnOk);

        var btnCancel = UiTheme.CreateButton("キャンセル", new Point(242, 128), new Size(90, 28));
        UiTheme.StyleSecondaryButton(btnCancel);
        btnCancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
        Controls.Add(btnCancel);

        AcceptButton = btnOk;
        CancelButton = btnCancel;
    }
}
