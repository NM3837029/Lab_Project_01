using System;
using System.Drawing;
using System.Windows.Forms;

namespace Lab_Editor;

// 配置ごとの「大きさ」と「角度」を編集する小さなダイアログ。
//
// ■ ここで設定する値の意味
// アセット定義の大きさは「その種類の標準」で、こちらはそれを配置単位で上書きするもの。
// ただし単に見た目を変えるだけではなく、**ゲーム内の編集ツールで同じ操作をしたのと
// まったく同じ扱い**になる。つまり
//   ・2倍にして置いた敵は、プレイヤーがS+ドラッグで2倍にしたときと同じように重くなる
//   ・傾けて置いたドッスンは、プレイヤーがR+ドラッグで傾けたときと同じ向きへ落ちる
//   ・45度以上倒して置いたトゲは、倒された扱い（無害）で始まる
// という反応が最初から成立する。
//
// ■ 操作の粒度もゲーム内に合わせてある
// ゲーム内の編集ツールは、敵は倍率1つ（S+ドラッグ）、ギミックは横幅(S)と縦幅(W)を
// 別々に変える。この画面もそれに合わせて、敵・アイテムは1つ、ギミックは2つの倍率を出す。
//
// 角度はゲーム側がラジアンで持っているが、入力は度のほうが扱いやすいので
// 画面では度で見せ、確定時にラジアンへ直して返す。
public class PlacedTransformForm : Form
{
    // 確定した値。DialogResult が OK のときだけ意味を持つ。
    public float ResultScale { get; private set; }
    public float ResultScaleY { get; private set; }
    public float ResultAngle { get; private set; } // ラジアン

    private readonly NumericUpDown _nudScale;
    private readonly NumericUpDown? _nudScaleY;
    private readonly NumericUpDown _nudAngle;
    private readonly Label _lblReaction;
    // 角度をAI側が握っている型（自動回転する橋・ちくわブロック等）。
    // これらはゲーム内でも回転できないので、この画面でも角度を触らせない。
    private readonly bool _angleIsAiOwned;
    // scale     : 編集開始時の大きさ（ギミックなら横倍率）
    // scaleY    : ギミックの縦倍率。敵・アイテムでは使わない
    // angleRad  : 編集開始時の角度（ラジアン）
    // separateAxes  : 横縦を別々に出すか（ギミック = true）
    // angleIsAiOwned: 角度をAIが握っている型か（回転を禁止する）
    public PlacedTransformForm(float scale, float scaleY, float angleRad,
                               bool separateAxes = false, bool angleIsAiOwned = false)
    {
        _angleIsAiOwned = angleIsAiOwned;

        Text = "大きさ・角度";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        Font = UiTheme.Base;
        // 高さは項目数で変わるので、組み立て終わってから実際の位置に合わせて決める

        int y = 12;
        Controls.Add(UiTheme.CreateLabel(
            separateAxes ? "横の大きさ（1.0 = アセットの標準サイズ）"
                         : "大きさ（1.0 = アセットの標準サイズ）",
            new Point(12, y)));
        _nudScale = UiTheme.CreateNumericUpDown(new Point(12, y + 20), 100, 0.1m, 10m, 2, 0.1m);
        _nudScale.Value = (decimal)Math.Clamp(scale <= 0 ? 1.0f : scale, 0.1f, 10f);
        _nudScale.ValueChanged += (s, e) => UpdateReaction();
        Controls.Add(_nudScale);
        y += 54;

        if (separateAxes)
        {
            Controls.Add(UiTheme.CreateLabel("縦の大きさ（ゲーム内のWドラッグにあたります）", new Point(12, y)));
            _nudScaleY = UiTheme.CreateNumericUpDown(new Point(12, y + 20), 100, 0.1m, 10m, 2, 0.1m);
            _nudScaleY.Value = (decimal)Math.Clamp(scaleY <= 0 ? 1.0f : scaleY, 0.1f, 10f);
            _nudScaleY.ValueChanged += (s, e) => UpdateReaction();
            Controls.Add(_nudScaleY);
            y += 54;
        }

        Controls.Add(UiTheme.CreateLabel(
            angleIsAiOwned ? "角度（この型は角度をAIが使うため変更できません）" : "角度（度）",
            new Point(12, y)));
        _nudAngle = UiTheme.CreateNumericUpDown(new Point(12, y + 20), 100, -360m, 360m, 1, 5m);
        _nudAngle.Value = (decimal)Math.Clamp(angleRad * 180.0 / Math.PI, -360.0, 360.0);
        _nudAngle.Enabled = !angleIsAiOwned;
        _nudAngle.ValueChanged += (s, e) => UpdateReaction();
        Controls.Add(_nudAngle);

        // よく使う角度はボタン一発で入れられるようにする（数値を打つより速い）
        int bx = 124;
        foreach (int deg in new[] { 0, 45, 90, 180 })
        {
            int captured = deg;
            // 幅を詰めすぎると "180°" が "180" のように切れるので、3桁＋度記号ぶんを確保する
            var b = UiTheme.CreateButton(deg + "°", new Point(bx, y + 19), new Size(56, 24));
            b.Enabled = !angleIsAiOwned;
            b.Click += (s, e) => _nudAngle.Value = captured;
            Controls.Add(b);
            bx += 60;
        }
        y += 52;

        // 「この設定だとゲーム内でどう扱われるか」をその場で出す。
        // 数値だけ見ても、45度を境に「倒れた」扱いになることなどは分からないため。
        _lblReaction = new Label
        {
            Location = new Point(12, y),
            Size = new Size(372, 40),
            Font = new Font("Meiryo UI", 8f),
            ForeColor = Color.DimGray,
        };
        Controls.Add(_lblReaction);
        y += 46;

        var btnOk = UiTheme.CreateButton("OK", new Point(206, y), new Size(84, 28));
        UiTheme.StylePrimaryButton(btnOk);
        btnOk.Click += (s, e) =>
        {
            ResultScale = (float)_nudScale.Value;
            ResultScaleY = _nudScaleY != null ? (float)_nudScaleY.Value : (float)_nudScale.Value;
            ResultAngle = (float)((double)_nudAngle.Value * Math.PI / 180.0);
            DialogResult = DialogResult.OK;
            Close();
        };
        Controls.Add(btnOk);

        var btnCancel = UiTheme.CreateButton("キャンセル", new Point(296, y), new Size(90, 28));
        UiTheme.StyleSecondaryButton(btnCancel);
        btnCancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
        Controls.Add(btnCancel);

        AcceptButton = btnOk;
        CancelButton = btnCancel;

        // 実際に並べ終わった位置から高さを決める（項目数で変わるため決め打ちにしない）
        ClientSize = new Size(398, y + 40);

        UpdateReaction();
    }

    // 敵・アイテム用の簡易コンストラクタ（横縦の区別が無いもの）。
    public PlacedTransformForm(float scale, float angleRad)
        : this(scale, scale, angleRad, separateAxes: false, angleIsAiOwned: false) { }

    // 今の入力値だと、ゲーム内でどの編集リアクションが成立するかを説明する。
    //
    // しきい値はゲーム側(DrawPixel.cpp の EDIT_SCALE_EPS / EDIT_TILT_EPS / EDIT_TILT_TIPPED)と
    // 揃えてある。45度以上倒れているかどうかは多くのギミック・敵で挙動が切り替わる境目なので、
    // 「今どちら側にいるか」が入力しながら分かるようにしておく。
    private void UpdateReaction()
    {
        float sx = (float)_nudScale.Value;
        float sy = _nudScaleY != null ? (float)_nudScaleY.Value : sx;
        double deg = (double)_nudAngle.Value;

        var parts = new System.Collections.Generic.List<string>();
        if (sx > 1.05f || sy > 1.05f) parts.Add("拡大");
        else if (sx < 0.95f || sy < 0.95f) parts.Add("縮小");

        // 180度回しただけなら姿勢としては元と同じ（ゲーム側の判定と同じ畳み方）
        double t = Math.Abs(((deg % 360) + 540) % 360 - 180);
        t = 180 - t;
        if (t > 90) t = 180 - t;
        if (_angleIsAiOwned)
        {
            parts.Add("角度は編集扱いになりません");
        }
        else if (t >= 45)
        {
            parts.Add("45度以上＝倒れた扱い");
        }
        else if (t > 1)
        {
            parts.Add("傾き");
        }

        _lblReaction.Text = parts.Count == 0
            ? "ゲーム内での扱い： 編集されていない状態で始まります。"
            : "ゲーム内での扱い： " + string.Join(" ／ ", parts) + Environment.NewLine
              + "（編集ツールで同じ操作をしたときと同じ反応になります）";
    }
}
