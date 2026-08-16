namespace Lab_Editor;

// Feature: UI改善（友人フィードバック対応）— 「行・列」の数を数値指定して矩形範囲を一括でタイル埋めする機能。
// 開始行・開始列・行数・列数を入力し、OKを押すと選択中のタイルIDでその範囲を塗りつぶす。
public class BulkTileFillForm : Form
{
    public int StartRow { get; private set; }
    public int StartCol { get; private set; }
    public int RowCount { get; private set; }
    public int ColCount { get; private set; }

    private NumericUpDown nudStartRow = null!, nudStartCol = null!, nudRowCount = null!, nudColCount = null!;

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

        var table = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 2, AutoSize = true, Padding = new Padding(12) };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));

        nudStartRow = new NumericUpDown { Minimum = 0, Maximum = Math.Max(0, mapH - 1), Value = 0, Width = 100 };
        nudStartCol = new NumericUpDown { Minimum = 0, Maximum = Math.Max(0, mapW - 1), Value = 0, Width = 100 };
        nudRowCount = new NumericUpDown { Minimum = 1, Maximum = mapH, Value = Math.Min(5, mapH), Width = 100 };
        nudColCount = new NumericUpDown { Minimum = 1, Maximum = mapW, Value = Math.Min(5, mapW), Width = 100 };

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

        var pnlBottom = new Panel { Dock = DockStyle.Bottom, Height = 46 };
        var flowRight = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) };
        var btnCancel = new Button { Text = "キャンセル", AutoSize = true, Padding = new Padding(10, 5, 10, 5) };
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
        AcceptButton = btnOk;
        CancelButton = btnCancel;
    }

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
