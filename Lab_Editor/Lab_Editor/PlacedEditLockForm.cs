using System;
using System.Drawing;
using System.Windows.Forms;

namespace Lab_Editor;

// 置いた物のぶんの「編集操作の可否」を設定する小さなダイアログ（ステージ画面の右クリック → 編集の可否を設定...）。
//
// ■ 何を決めるか
// ゲーム内でプレイヤーがその物に対してできる編集操作（拡大縮小・回転・移動・向き反転・個別の一時停止・
// 個別の巻き戻し・速度変更）を、操作ごとに次の3つから選ぶ。
//   既定 … アセットの設定に従う（アセット管理の「○○を禁止」。何も書き出さない）
//   許可 … この物だけ、アセットが禁止していても許可する
//   禁止 … この物だけ、アセットが許可していても禁止する
// 同じ敵・ギミックでも、置いた場所ごとに変えられる（たとえば、チュートリアルで「この箱だけ動かせる」）。
//
// ■ 複数選択のとき（multi = true）
// 選んだ全員へ同時に設定する。操作ごとに「変更しない」を足してあり、これが初期値。
// 「変更しない」の操作は、各自の今の設定がそのまま残る（触っていない項目で、全員の値が上書きされないようにするため）。
//
// 確定した値は Result に、どの操作を変えるかは Changed に入る（DialogResult が OK のときだけ意味を持つ）。
// ゲーム側の判定は DrawPixel.cpp の IsEnemyEditLocked / IsGimmickEditLocked（置いた物ごとの上書き → アセットの順）。
public class PlacedEditLockForm : Form
{
    // 確定した値。キャンセル時は使わない
    public PlacedEditLocks Result { get; private set; }
    // 操作ごとに「この操作の設定を書き換えるか」。1つだけのとき（multi=false）は全部 true
    public bool[] Changed { get; } = { true, true, true, true, true, true, true };

    // 操作ごとの選択肢。Tag は 値（null=既定 / false=許可 / true=禁止）、または KeepTag（変更しない）
    private static readonly object KeepTag = new();
    private readonly RadioButton[][] _radios = new RadioButton[7][];

    // name        : 画面に出す対象の名前（アセットの名前。複数のときは「選択中の n個」）
    // current     : いまの設定（このダイアログでは書き換えず、確定時に Result を作る）。複数のときは使わない
    // assetLocks  : アセット側の設定（true=アセットがその操作を禁止している）。複数のときは出さない
    // isItem      : アイテムを含むか。アイテムはゲーム内で選択できず、設定は保存されるが現在は効果が無いことを断る
    // multi       : 複数選択か（「変更しない」を足す）
    public PlacedEditLockForm(string name, PlacedEditLocks current, bool[] assetLocks, bool isItem, bool multi = false)
    {
        Result = current.Clone();

        Text = "編集の可否を設定";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        Font = UiTheme.Base;

        int y = 12;
        Controls.Add(UiTheme.CreateLabel("対象： " + (string.IsNullOrEmpty(name) ? "（名前なし）" : name), new Point(12, y)));
        y += 24;

        var lblHelp = new Label
        {
            Location = new Point(12, y),
            Size = new Size(468, 40),
            Font = new Font("Meiryo UI", 8f),
            ForeColor = Color.DimGray,
            Text = "ゲーム内でプレイヤーがこの" + (multi ? "物" : "1つ") + "に対してできる編集操作を決めます。"
                 + Environment.NewLine
                 + (multi ? "「変更しない」の操作は、それぞれの今の設定がそのまま残ります。"
                          : "既定＝アセットの設定に従う ／ 許可・禁止＝この1つだけ、アセットの設定より優先"),
        };
        Controls.Add(lblHelp);
        y += 46;

        if (isItem)
        {
            // アイテムはゲーム内の編集ツールの選択対象になっていない（SELECT_ITEM が存在しない）。
            // 保存はされるので、選択できるようになったときにそのまま効く。いまは効かないことを先に伝える。
            var note = new Label
            {
                Location = new Point(12, y),
                Size = new Size(468, 34),
                Font = new Font("Meiryo UI", 8f, FontStyle.Bold),
                ForeColor = Color.Firebrick,
                Text = "※ アイテムは、いまのところゲーム内の編集ツールで選択できません。" + Environment.NewLine
                     + "   この設定は保存されますが、現在は効果がありません。",
            };
            Controls.Add(note);
            y += 40;
        }

        // 列の見出し。複数選択のときだけ「変更なし」の列を足して、全体を左へ寄せる（アセットの設定欄は出さない）
        int colBase = multi ? 150 : 176;
        int colStep = multi ? 64 : 60;
        int ci = 0;
        if (multi) Controls.Add(UiTheme.CreateLabel("変更なし", new Point(colBase + (ci++) * colStep - 6, y)));
        Controls.Add(UiTheme.CreateLabel("既定", new Point(colBase + (ci++) * colStep, y)));
        Controls.Add(UiTheme.CreateLabel("許可", new Point(colBase + (ci++) * colStep, y)));
        Controls.Add(UiTheme.CreateLabel("禁止", new Point(colBase + (ci++) * colStep, y)));
        y += 22;

        for (int i = 0; i < 7; i++)
        {
            Controls.Add(UiTheme.CreateLabel(PlacedEditLocks.Labels[i], new Point(12, y + 3)));

            // 行ごとに別のパネルへ入れる（同じ親に置くと、7行ぶんが1つのグループになってしまう）
            var row = new Panel { Location = new Point(colBase - 10, y), Size = new Size(multi ? 270 : 190, 26) };
            var list = new System.Collections.Generic.List<RadioButton>();
            int px = 14;
            if (multi) { list.Add(new RadioButton { Location = new Point(px, 3), Size = new Size(20, 20), Tag = KeepTag, Checked = true }); px += colStep; }
            list.Add(new RadioButton { Location = new Point(px, 3), Size = new Size(20, 20), Tag = null }); px += colStep;
            list.Add(new RadioButton { Location = new Point(px, 3), Size = new Size(20, 20), Tag = false }); px += colStep;
            list.Add(new RadioButton { Location = new Point(px, 3), Size = new Size(20, 20), Tag = true });
            foreach (var rb in list) row.Controls.Add(rb);
            Controls.Add(row);
            _radios[i] = list.ToArray();

            if (!multi)
            {
                var cur = current.Get(i);
                foreach (var rb in list)
                    if ((cur == null && rb.Tag == null) || (cur == false && rb.Tag is false) || (cur == true && rb.Tag is true)) rb.Checked = true;

                // 「既定」のときの結果が分かるよう、アセットの設定を横に出す
                bool assetDenies = i < assetLocks.Length && assetLocks[i];
                Controls.Add(new Label
                {
                    Location = new Point(364, y + 4),
                    Size = new Size(120, 18),
                    Font = new Font("Meiryo UI", 8f),
                    ForeColor = assetDenies ? Color.Firebrick : Color.DimGray,
                    Text = assetDenies ? "アセット：禁止" : "アセット：許可",
                });
            }
            y += 28;
        }
        y += 6;

        var btnReset = UiTheme.CreateButton(multi ? "すべて変更しない" : "すべて既定に戻す", new Point(12, y), new Size(130, 28));
        UiTheme.StyleSecondaryButton(btnReset);
        // 複数のときは「変更しない」、1つのときは「既定」（どちらも各行の先頭の選択肢）へ戻す
        btnReset.Click += (s, e) => { foreach (var set in _radios) set[0].Checked = true; };
        Controls.Add(btnReset);

        var btnOk = UiTheme.CreateButton("OK", new Point(296, y), new Size(84, 28));
        UiTheme.StylePrimaryButton(btnOk);
        btnOk.Click += (s, e) =>
        {
            var r = new PlacedEditLocks();
            for (int i = 0; i < 7; i++)
            {
                Changed[i] = true;
                // 選ばれているラジオの Tag（null=既定 / false=許可 / true=禁止 / KeepTag=変更しない）をそのまま値にする
                foreach (var rb in _radios[i])
                {
                    if (!rb.Checked) continue;
                    if (ReferenceEquals(rb.Tag, KeepTag)) Changed[i] = false;
                    else r.Set(i, rb.Tag as bool?);
                    break;
                }
            }
            Result = r;
            DialogResult = DialogResult.OK;
            Close();
        };
        Controls.Add(btnOk);

        var btnCancel = UiTheme.CreateButton("キャンセル", new Point(386, y), new Size(94, 28));
        UiTheme.StyleSecondaryButton(btnCancel);
        btnCancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
        Controls.Add(btnCancel);

        AcceptButton = btnOk;
        CancelButton = btnCancel;

        // 実際に並べ終わった位置から高さを決める（アイテムのときは注意書きの分だけ高くなる）
        ClientSize = new Size(496, y + 44);
    }
}
