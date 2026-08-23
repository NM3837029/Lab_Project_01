namespace Lab_Editor;

// Feature: UI改善（友人フィードバック対応）— 「行・列」の数を数値指定して矩形範囲を一括でタイル埋めする機能。
// 開始行・開始列・行数・列数を入力し、OKを押すと選択中のタイルIDでその範囲を塗りつぶす。
public class BulkTileFillForm : Form
{
    // OK押下確定後、呼び出し元が読み取る結果（矩形範囲の開始位置とサイズ）
    public int StartRow { get; private set; }
    public int StartCol { get; private set; }
    public int RowCount { get; private set; }
    public int ColCount { get; private set; }

    // 開始行・開始列・行数・列数を入力するための数値入力欄
    private NumericUpDown nudStartRow = null!, nudStartCol = null!, nudRowCount = null!, nudColCount = null!;

    // コンストラクタ。
    // mapW : マップ全体の横方向のタイル数（列数の入力上限に使う）
    // mapH : マップ全体の縦方向のタイル数（行数の入力上限に使う）
    public BulkTileFillForm(int mapW, int mapH)
    {
        Text = "数値指定で一括配置";
        Size = new Size(320, 260);
        MinimumSize = new Size(280, 240);
        StartPosition = FormStartPosition.CenterParent;
        Font = UiTheme.Base;
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;
        MinimizeBox = true;

        // ラベルと入力欄を2列で並べるためのテーブルレイアウト
        var table = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 2, AutoSize = true, Padding = new Padding(12) };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));

        // 各入力欄の範囲：開始行/開始列はマップの端まで、行数/列数は1〜マップ全体の大きさまでを許容する。
        // 既定値の行数/列数は、マップが小さい場合でも範囲外にならないよう5とマップサイズの小さい方を採用する。
        nudStartRow = new NumericUpDown { Minimum = 0, Maximum = Math.Max(0, mapH - 1), Value = 0, Width = 100 };
        nudStartCol = new NumericUpDown { Minimum = 0, Maximum = Math.Max(0, mapW - 1), Value = 0, Width = 100 };
        nudRowCount = new NumericUpDown { Minimum = 1, Maximum = mapH, Value = Math.Min(5, mapH), Width = 100 };
        nudColCount = new NumericUpDown { Minimum = 1, Maximum = mapW, Value = Math.Min(5, mapW), Width = 100 };

        // ラベル＋入力欄の1行分をテーブルに追加するローカル関数（重複コードをまとめたもの）
        void AddRow(string label, Control ctrl)
        {
            var lbl = new Label { Text = label, AutoSize = true, TextAlign = ContentAlignment.MiddleLeft, Anchor = AnchorStyles.Left, Margin = new Padding(0, 8, 0, 0) };
            ctrl.Margin = new Padding(0, 4, 0, 4);
            table.Controls.Add(lbl);
            table.Controls.Add(ctrl);
        }
        AddRow("開始行 (Row)", nudStartRow);
        AddRow("開始列 (Col)", nudStartCol);
        AddRow("行数 (Rows)", nudRowCount);
        AddRow("列数 (Cols)", nudColCount);

        // 操作内容を補足するヒント文（グレーの小さめ文字で表示する）
        var lblHint = new Label
        {
            Dock = DockStyle.Top,
            AutoSize = false,
            Height = 40,
            Text = "現在パレットで選択中のタイルで、指定した矩形範囲を一括で塗りつぶします。",
            ForeColor = Color.DarkGray,
            Font = UiTheme.Small,
            Padding = new Padding(12, 0, 12, 0)
        };

        // 画面下部に「キャンセル」「配置」ボタンを右揃えで配置する
        var pnlBottom = new Panel { Dock = DockStyle.Bottom, Height = 46 };
        var flowRight = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) };
        var btnCancel = new Button { Text = "キャンセル", AutoSize = true, Padding = new Padding(10, 5, 10, 5) };
        // キャンセル時はDialogResultをCancelにしてフォームを閉じるだけ（入力値は反映しない）
        btnCancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
        var btnOk = new Button { Text = "✅ 配置", AutoSize = true, Padding = new Padding(10, 5, 10, 5) };
        UiTheme.StylePrimaryButton(btnOk);
        btnOk.Click += BtnOk_Click;
        flowRight.Controls.Add(btnCancel);
        flowRight.Controls.Add(btnOk);
        pnlBottom.Controls.Add(flowRight);

        Controls.Add(table);
        Controls.Add(lblHint);
        Controls.Add(pnlBottom);
        // Enter/Escキーでもそれぞれ配置/キャンセルできるようにする
        AcceptButton = btnOk;
        CancelButton = btnCancel;
    }

    // 「配置」ボタン押下時の処理。各入力欄の値を結果プロパティへ確定し、DialogResult.OKでフォームを閉じる。
    private void BtnOk_Click(object? sender, EventArgs e)
    {
        StartRow = (int)nudStartRow.Value;
        StartCol = (int)nudStartCol.Value;
        RowCount = (int)nudRowCount.Value;
        ColCount = (int)nudColCount.Value;
        DialogResult = DialogResult.OK;
        Close();
    }
}
