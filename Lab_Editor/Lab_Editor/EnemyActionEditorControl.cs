using System.Reflection;

namespace Lab_Editor;

// 「敵の動きを調整」画面の、多彩行動（type 23）の行動リスト編集欄。
//
// 敵がする行動（待機・歩く・ジャンプ・突進・弾・瞬間移動・暗転・色変化・ズーム）を並べ、
// 切り替え方（順番／ランダム／重み付き）と、各行動の数値を決める。
// このコントロールは EnemyDef を直接書き換え、変えたことを Changed で知らせる（プレビューの更新は呼び出し側）。
public class EnemyActionEditorControl : UserControl
{
    public event Action? Changed;

    private readonly EnemyDef _def;
    private bool _suppress;
    private readonly ListBox _list = new();
    private readonly ComboBox _cboMode = new();
    private readonly Button _btnAdd = new(), _btnDup = new(), _btnDel = new(), _btnUp = new(), _btnDown = new();
    private readonly FlowLayoutPanel _card = new();
    private readonly Label _lblKindHint = new(), _lblEmpty = new();

    public EnemyActionEditorControl(EnemyDef def)
    {
        _def = def;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Font = UiTheme.Base;
        Margin = new Padding(0);

        var flow = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = new Padding(0) };
        Controls.Add(flow);

        flow.Controls.Add(new Label
        {
            Text = "この敵は、下の行動を切り替えながら繰り返します。行動の数・種類・順番を自由に決められます。",
            AutoSize = true, MaximumSize = new Size(410, 0), ForeColor = Color.DimGray, Margin = new Padding(0, 0, 0, 4),
        });

        var modeLine = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, WrapContents = false, AutoSize = true, Margin = new Padding(0, 0, 0, 4) };
        modeLine.Controls.Add(new Label { Text = "行動の選び方", AutoSize = true, Margin = new Padding(0, 6, 6, 0) });
        _cboMode.DropDownStyle = ComboBoxStyle.DropDownList; _cboMode.Width = 260;
        _cboMode.Items.AddRange(new object[] { "順番どおり（上から下へ、繰り返す）", "ランダム（同じ行動は続けない）", "重み付き（選ばれやすさに応じて）" });
        _cboMode.SelectedIndexChanged += (s, e) =>
        {
            if (_suppress) return;
            _def.actionMode = _cboMode.SelectedIndex switch { 1 => "random", 2 => "weighted", _ => "sequence" };
            LoadCard(); Changed?.Invoke();
        };
        modeLine.Controls.Add(_cboMode);
        flow.Controls.Add(modeLine);

        _list.Width = 410; _list.Height = 130; _list.IntegralHeight = false;
        _list.SelectedIndexChanged += (s, e) => LoadCard();
        flow.Controls.Add(_list);

        var bar = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = true, Margin = new Padding(0, 2, 0, 2) };
        void Small(Button b, string t) { b.Text = t; b.AutoSize = true; b.AutoSizeMode = AutoSizeMode.GrowAndShrink; b.Padding = new Padding(5, 2, 5, 2); b.Margin = new Padding(0, 0, 3, 3); }
        Small(_btnAdd, "＋ 行動を追加 ▾"); Small(_btnDup, "複製"); Small(_btnDel, "削除"); Small(_btnUp, "↑"); Small(_btnDown, "↓");
        _btnAdd.Click += (s, e) => BuildAddMenu().Show(_btnAdd, new Point(0, _btnAdd.Height));
        _btnDup.Click += (s, e) => { var a = Cur(); if (a == null) return; _def.actions.Insert(_list.SelectedIndex + 1, a.Clone()); Reload(_list.SelectedIndex + 1); Changed?.Invoke(); };
        _btnDel.Click += (s, e) => { if (Cur() == null) return; int i = _list.SelectedIndex; _def.actions.RemoveAt(i); Reload(Math.Min(i, _def.actions.Count - 1)); Changed?.Invoke(); };
        _btnUp.Click += (s, e) => Move(-1);
        _btnDown.Click += (s, e) => Move(+1);
        bar.Controls.AddRange(new Control[] { _btnAdd, _btnDup, _btnDel, _btnUp, _btnDown });
        flow.Controls.Add(bar);

        _lblEmpty.Text = "行動がまだありません。「＋ 行動を追加」から選んでください。\n（例：歩く → ジャンプ → 弾を撃つ → 突進 → 休む …）";
        _lblEmpty.AutoSize = true; _lblEmpty.MaximumSize = new Size(410, 0); _lblEmpty.ForeColor = Color.FromArgb(150, 90, 0);
        flow.Controls.Add(_lblEmpty);

        _card.FlowDirection = FlowDirection.TopDown; _card.WrapContents = false; _card.AutoSize = true; _card.AutoSizeMode = AutoSizeMode.GrowAndShrink; _card.Margin = new Padding(0, 4, 0, 4);
        flow.Controls.Add(_card);

        _suppress = true;
        _cboMode.SelectedIndex = _def.actionMode switch { "random" => 1, "weighted" => 2, _ => 0 };
        _suppress = false;
        Reload(0);
    }

    private EnemyAction? Cur() => (_list.SelectedIndex >= 0 && _list.SelectedIndex < _def.actions.Count) ? _def.actions[_list.SelectedIndex] : null;

    private void Reload(int select)
    {
        _suppress = true;
        _list.Items.Clear();
        for (int i = 0; i < _def.actions.Count; i++) _list.Items.Add($"{i + 1}.  {EnemyActionInfo.Describe(_def.actions[i])}");
        if (select >= 0 && select < _list.Items.Count) _list.SelectedIndex = select;
        else if (_list.Items.Count > 0) _list.SelectedIndex = 0;
        _suppress = false;
        LoadCard();
    }

    private void RefreshListText()
    {
        _suppress = true;
        int sel = _list.SelectedIndex;
        for (int i = 0; i < _def.actions.Count && i < _list.Items.Count; i++) _list.Items[i] = $"{i + 1}.  {EnemyActionInfo.Describe(_def.actions[i])}";
        if (sel >= 0 && sel < _list.Items.Count) _list.SelectedIndex = sel;
        _suppress = false;
    }

    private void Move(int dir)
    {
        if (Cur() == null) return;
        int i = _list.SelectedIndex, j = i + dir;
        if (j < 0 || j >= _def.actions.Count) return;
        (_def.actions[i], _def.actions[j]) = (_def.actions[j], _def.actions[i]);
        Reload(j); Changed?.Invoke();
    }

    private ContextMenuStrip BuildAddMenu()
    {
        var menu = new ContextMenuStrip();
        foreach (var k in EnemyActionInfo.Kinds)
        {
            var kk = k;
            var item = new ToolStripMenuItem(kk.Label) { ToolTipText = kk.Hint };
            item.Click += (s, e) => { _def.actions.Add(EnemyActionInfo.CreateDefault(kk.Name)); Reload(_def.actions.Count - 1); Changed?.Invoke(); };
            menu.Items.Add(item);
        }
        return menu;
    }

    // ================= 編集カード（種類ごとに項目が変わる）=================

    private void LoadCard()
    {
        var a = Cur();
        _btnDup.Enabled = _btnDel.Enabled = _btnUp.Enabled = _btnDown.Enabled = a != null;
        _lblEmpty.Visible = _def.actions.Count == 0;
        _card.SuspendLayout();
        while (_card.Controls.Count > 0) { var c = _card.Controls[0]; _card.Controls.RemoveAt(0); c.Dispose(); }
        if (a == null) { _card.ResumeLayout(); return; }

        var kind = EnemyActionInfo.Get(a.kind);
        // 種類の切り替え
        var kindLine = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, WrapContents = false, AutoSize = true, Margin = new Padding(0, 0, 0, 2) };
        kindLine.Controls.Add(new Label { Text = "この行動の種類", AutoSize = true, Margin = new Padding(0, 6, 6, 0) });
        var cboKind = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 230 };
        cboKind.Items.AddRange(EnemyActionInfo.Kinds.Select(k => (object)k.Label).ToArray());
        cboKind.SelectedIndex = Array.FindIndex(EnemyActionInfo.Kinds, k => k.Name == a.kind);
        cboKind.SelectedIndexChanged += (s, e) =>
        {
            if (_suppress) return;
            // 種類を変えたら、その種類の初期値（時間などの共通項目は引き継ぐ）
            var fresh = EnemyActionInfo.CreateDefault(EnemyActionInfo.Kinds[Math.Max(0, cboKind.SelectedIndex)].Name);
            fresh.weight = a.weight;
            _def.actions[_list.SelectedIndex] = fresh;
            RefreshListText(); LoadCard(); Changed?.Invoke();
        };
        kindLine.Controls.Add(cboKind);
        _card.Controls.Add(kindLine);
        _card.Controls.Add(new Label { Text = kind.Hint, AutoSize = true, MaximumSize = new Size(410, 0), ForeColor = Color.Gray, Font = UiTheme.Small, Margin = new Padding(0, 0, 0, 4) });

        foreach (var f in kind.Fields) _card.Controls.Add(BuildField(a, f));
        // 重み（重み付きのときだけ意味を持つ）
        if (_def.actionMode == "weighted")
            _card.Controls.Add(BuildField(a, new EnemyActionInfo.Field { Prop = "weight", Label = "選ばれやすさ（重み）", Min = 0, Max = 20, Step = 0.5f, Decimals = 1, Hint = "大きいほど選ばれやすくなります。全体に対する割合で決まります。" }));
        _card.ResumeLayout();
    }

    private static PropertyInfo Prop(string name) => typeof(EnemyAction).GetProperty(name)!;

    private Control BuildField(EnemyAction a, EnemyActionInfo.Field f)
    {
        var row = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = new Padding(0, 0, 0, 6) };
        var name = new Label { Text = f.Label, AutoSize = true, Margin = new Padding(0, 2, 0, 0) };
        row.Controls.Add(name);
        var pi = Prop(f.Prop);
        float Get() => pi.GetValue(a) switch { float x => x, int i => i, bool b => b ? 1f : 0f, _ => 0f };
        void Set(float v) { if (pi.PropertyType == typeof(float)) pi.SetValue(a, v); else if (pi.PropertyType == typeof(int)) pi.SetValue(a, (int)MathF.Round(v)); else pi.SetValue(a, v > 0.5f); }

        switch (f.Type)
        {
            case EnemyActionInfo.FieldType.Flag:
            {
                var chk = new CheckBox { Text = f.Label, AutoSize = true, Checked = Get() > 0.5f };
                row.Controls.Clear(); row.Controls.Add(chk);
                chk.CheckedChanged += (s, e) => { if (_suppress) return; Set(chk.Checked ? 1f : 0f); RefreshListText(); Changed?.Invoke(); };
                break;
            }
            case EnemyActionInfo.FieldType.Choice:
            {
                var cbo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
                cbo.Items.AddRange(f.Choices.Cast<object>().ToArray());
                cbo.SelectedIndex = Math.Clamp((int)Get(), 0, f.Choices.Length - 1);
                cbo.SelectedIndexChanged += (s, e) => { if (_suppress) return; Set(cbo.SelectedIndex); Changed?.Invoke(); };
                row.Controls.Add(cbo);
                break;
            }
            case EnemyActionInfo.FieldType.Color:
            {
                var line = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, WrapContents = false, AutoSize = true, Margin = new Padding(0) };
                var btn = new Button { Size = new Size(90, 28), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb((int)a.colorR, (int)a.colorG, (int)a.colorB) };
                var lbl = new Label { AutoSize = true, Margin = new Padding(6, 8, 0, 0), ForeColor = Color.DimGray, Text = $"R{a.colorR:0} G{a.colorG:0} B{a.colorB:0}" };
                btn.Click += (s, e) =>
                {
                    using var dlg = new ColorDialog { Color = btn.BackColor, FullOpen = true };
                    if (dlg.ShowDialog() != DialogResult.OK) return;
                    a.colorR = dlg.Color.R; a.colorG = dlg.Color.G; a.colorB = dlg.Color.B;
                    btn.BackColor = dlg.Color; lbl.Text = $"R{a.colorR:0} G{a.colorG:0} B{a.colorB:0}";
                    Changed?.Invoke();
                };
                line.Controls.Add(btn); line.Controls.Add(lbl);
                row.Controls.Add(line);
                break;
            }
            default:
            {
                int steps = (int)Math.Max(1, Math.Round((f.Max - f.Min) / f.Step));
                var bar = new TrackBar { Minimum = 0, Maximum = steps, TickStyle = TickStyle.None, Width = 280, Height = 26, LargeChange = Math.Max(1, steps / 10) };
                var nud = new NumericUpDown { Minimum = (decimal)f.Min, Maximum = (decimal)f.Max, DecimalPlaces = f.Decimals, Increment = (decimal)f.Step, Width = 100, Margin = new Padding(6, 2, 0, 0) };
                var line = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, WrapContents = false, AutoSize = true, Margin = new Padding(0) };
                line.Controls.Add(bar); line.Controls.Add(nud);
                row.Controls.Add(line);
                int ToTick(float v) => (int)Math.Clamp(MathF.Round((v - f.Min) / f.Step), 0, steps);
                _suppress = true;
                float cur = Math.Clamp(Get(), f.Min, f.Max);
                nud.Value = (decimal)cur; bar.Value = ToTick(cur);
                _suppress = false;
                bar.ValueChanged += (s, e) =>
                {
                    if (_suppress) return;
                    float v = Math.Clamp(MathF.Round((f.Min + bar.Value * f.Step) / f.Step) * f.Step, f.Min, f.Max);
                    Set(v); _suppress = true; nud.Value = (decimal)v; _suppress = false; RefreshListText(); Changed?.Invoke();
                };
                nud.ValueChanged += (s, e) =>
                {
                    if (_suppress) return;
                    float v = (float)nud.Value;
                    Set(v); _suppress = true; bar.Value = ToTick(v); _suppress = false; RefreshListText(); Changed?.Invoke();
                };
                break;
            }
        }
        if (!string.IsNullOrEmpty(f.Hint))
            row.Controls.Add(new Label { Text = f.Hint, AutoSize = true, MaximumSize = new Size(400, 0), ForeColor = Color.Gray, Font = UiTheme.Small, Margin = new Padding(0, 1, 0, 0) });
        return row;
    }
}
