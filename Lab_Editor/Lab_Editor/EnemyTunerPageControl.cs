using System.Reflection;

namespace Lab_Editor;

// 「敵の動きを調整」画面。
//
// アセット管理の敵の行から開き、その敵の挙動パラメータ（EnemyDef の各数値）を、
// 日本語の名前・単位・説明つきのスライダーで調整する。左側のプレビューで、数値が「どう動くか」を絵で確かめられる。
//
// 【これまでとの違い】
// アセット管理の右パネルにも型ごとの数値欄はあったが、平らな数値のリストで、
//   ・単位・範囲・説明が無い　・-1（既定を使う）が数字のまま見える　・何が変わるのか分からない
// という状態だった。ここでは、項目をグループに分け、「既定を使う」を明示し、プレビューを付けた。
// 項目の定義は EnemyTuneSchema（型ごと）にあり、画面はそれを読んで作る。
//
// 編集は EnemyDef を直接書き換える。キャンセルしたときは、開いたときの値へ戻す。
public class EnemyTunerPageControl : UserControl
{
    public event EventHandler? Saved;
    public event EventHandler? Cancelled;
    // 「ゲームで試す」が押されたとき（ホスト側が、保存→一時ステージ→ゲーム起動を行う）
    public event EventHandler? TestPlayRequested;
    public Button PrimaryActionButton => _btnOk;
    public Button SecondaryActionButton => _btnCancel;

    private readonly EnemyDef _def;
    private readonly TuneType _type;
    private readonly Dictionary<string, PropertyInfo> _props = new();
    private readonly Dictionary<string, TuneParam> _byKey = new();
    private readonly Dictionary<string, float> _snapshot = new();
    private readonly List<Action> _refreshers = new();   // 画面の表示を、いまの値へ合わせ直す処理
    private readonly EnemyTuneCanvas _canvas;
    private Button _btnOk = null!, _btnCancel = null!;
    private bool _suppress;
    private SplitContainer _split = null!;

    public EnemyTunerPageControl(string label, EnemyDef def, int typeEnum)
    {
        _def = def;
        _type = EnemyTuneSchema.Get(typeEnum);
        Font = UiTheme.Base;
        Dock = DockStyle.Fill;

        foreach (var p in typeof(EnemyDef).GetProperties(BindingFlags.Public | BindingFlags.Instance))
            if (p.PropertyType == typeof(float) || p.PropertyType == typeof(int) || p.PropertyType == typeof(bool)) _props[p.Name] = p;
        var allGroups = _type.Groups.Concat(EnemyTuneSchema.CommonGroups()).ToList();
        foreach (var gr in allGroups)
            foreach (var p in gr.Params)
            {
                _byKey[p.Key] = p;
                foreach (var k in new[] { p.Key, p.Key2, p.Key3 }) if (!string.IsNullOrEmpty(k) && _props.ContainsKey(k)) _snapshot[k] = Get(k);
            }

        _canvas = new EnemyTuneCanvas { Dock = DockStyle.Fill, TypeDef = _type, Eff = Eff };

        _split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, FixedPanel = FixedPanel.Panel2 };
        _split.Panel1.Controls.Add(_canvas);
        _split.Panel2.Controls.Add(BuildEditor(label, allGroups));

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 46, BackColor = UiTheme.PanelBackLight };
        _btnOk = new Button { Text = "✔ この内容にする", Size = new Size(150, 30), Anchor = AnchorStyles.Top | AnchorStyles.Right };
        _btnCancel = new Button { Text = "キャンセル", Size = new Size(110, 30), Anchor = AnchorStyles.Top | AnchorStyles.Right };
        UiTheme.StylePrimaryButton(_btnOk);
        UiTheme.StyleSecondaryButton(_btnCancel);
        _btnOk.Click += (s, e) => Saved?.Invoke(this, EventArgs.Empty);
        _btnCancel.Click += (s, e) => { Restore(); Cancelled?.Invoke(this, EventArgs.Empty); };
        var btnTest = new Button { Text = "▶ ゲームで試す", Size = new Size(140, 30), Location = new Point(12, 8) };
        btnTest.Click += (s, e) => TestPlayRequested?.Invoke(this, EventArgs.Empty);
        new ToolTip().SetToolTip(btnTest, "いまの値で、この敵を1体だけ置いたテスト用ステージを作って、ゲームを起動します（先にアセット定義を保存します）。");
        bottom.Controls.Add(_btnOk); bottom.Controls.Add(_btnCancel); bottom.Controls.Add(btnTest);
        bottom.Resize += (s, e) => { _btnOk.Left = bottom.ClientSize.Width - 280; _btnCancel.Left = bottom.ClientSize.Width - 120; _btnOk.Top = _btnCancel.Top = 8; };
        // 【規約】Dock=Fill を先に、Dock=Bottom を後に Add する
        Controls.Add(_split);
        Controls.Add(bottom);

        // 分割位置は実サイズが確定してから（幅が足りないときは見送り、サイズが変わったとき再挑戦）
        bool applied = false;
        void ApplySplit() { if (SplitLayout.Apply(_split, 320, 440, Math.Max(360, _split.Width - 500))) applied = true; }
        SizeChanged += (s, e) => { if (!applied && IsHandleCreated) ApplySplit(); };
        Load += (s, e) => ApplySplit();
    }

    // ================= 値の出し入れ =================

    private float Get(string key)
    {
        if (!_props.TryGetValue(key, out var p)) return 0f;
        object? v = p.GetValue(_def);
        return v switch { float f => f, int i => i, bool b => b ? 1f : 0f, _ => 0f };
    }

    private void Set(string key, float v)
    {
        if (!_props.TryGetValue(key, out var p)) return;
        if (p.PropertyType == typeof(float)) p.SetValue(_def, v);
        else if (p.PropertyType == typeof(int)) p.SetValue(_def, (int)MathF.Round(v));
        else if (p.PropertyType == typeof(bool)) p.SetValue(_def, v > 0.5f);
    }

    // 実際に使われる値（既定を使うものは既定値）。プレビューが読む。色の未指定は -1 のまま返す
    private float Eff(string key)
    {
        float v = Get(key);
        if (!_byKey.TryGetValue(key, out var tp))
        {
            // 色の G/B など、キー違いで引かれた場合
            return v;
        }
        if (tp.Kind == TuneKind.Number) return v < 0 ? tp.Default : v;
        if (tp.Kind == TuneKind.Choice) return v < 0 ? (tp.Key == "counterFilter" ? -1 : tp.ChoiceDefault) : v;
        return v;
    }

    private void Restore()
    {
        foreach (var kv in _snapshot) Set(kv.Key, kv.Value);
    }

    private void ResetAll()
    {
        foreach (var gr in _type.Groups)
            foreach (var p in gr.Params)
            {
                if (p.Kind == TuneKind.Flag) continue;
                foreach (var k in new[] { p.Key, p.Key2, p.Key3 }) if (!string.IsNullOrEmpty(k)) Set(k, -1f);
            }
        RefreshAll();
    }

    private void RefreshAll()
    {
        _suppress = true;
        foreach (var r in _refreshers) r();
        _suppress = false;
        _canvas.Invalidate();
    }

    // ================= 画面の組み立て =================

    private Control BuildEditor(string label, List<TuneGroup> groups)
    {
        var pnl = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(8) };
        var flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(2, 2, 14, 10),
        };

        var title = new Label { Text = $"🎛 {_type.Name}", Font = UiTheme.Heading, AutoSize = true, Margin = new Padding(0, 0, 0, 2) };
        var sub = new Label { Text = $"{label}\n{_type.Summary}", AutoSize = true, MaximumSize = new Size(420, 0), ForeColor = Color.DimGray, Margin = new Padding(0, 0, 0, 4) };
        flow.Controls.Add(title); flow.Controls.Add(sub);

        var btnReset = new Button { Text = "すべて既定に戻す", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(6, 2, 6, 2), Margin = new Padding(0, 0, 0, 6) };
        btnReset.Click += (s, e) =>
        {
            if (MessageBox.Show("この敵の調整項目を、すべて既定（-1）に戻します。よろしいですか？", "既定に戻す", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes) ResetAll();
        };
        flow.Controls.Add(btnReset);

        if (_type.Groups.Count == 0)
            flow.Controls.Add(new Label { Text = "この型には、数値で調整する項目はありません。", AutoSize = true, ForeColor = Color.Gray, Margin = new Padding(0, 4, 0, 8) });

        foreach (var gr in groups)
        {
            var head = new Label { Text = gr.Title, Font = UiTheme.Bold, ForeColor = Color.FromArgb(40, 80, 140), AutoSize = true, Margin = new Padding(0, 10, 0, 0) };
            flow.Controls.Add(head);
            if (!string.IsNullOrEmpty(gr.Note))
                flow.Controls.Add(new Label { Text = gr.Note, AutoSize = true, MaximumSize = new Size(420, 0), ForeColor = Color.Gray, Font = UiTheme.Small, Margin = new Padding(0, 0, 0, 2) });
            var sep = new Panel { Height = 1, Width = 410, BackColor = Color.Silver, Margin = new Padding(0, 2, 0, 4) };
            flow.Controls.Add(sep);
            foreach (var p in gr.Params) flow.Controls.Add(BuildRow(p));
        }
        pnl.Controls.Add(flow);
        return pnl;
    }

    private Control BuildRow(TuneParam p)
    {
        var row = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = new Padding(0, 0, 0, 8), Width = 410 };
        switch (p.Kind)
        {
            case TuneKind.Flag: BuildFlagRow(row, p); break;
            case TuneKind.Color: BuildColorRow(row, p); break;
            case TuneKind.Choice: BuildChoiceRow(row, p); break;
            default: BuildNumberRow(row, p); break;
        }
        if (!string.IsNullOrEmpty(p.Hint))
            row.Controls.Add(new Label { Text = p.Hint, AutoSize = true, MaximumSize = new Size(400, 0), ForeColor = Color.Gray, Font = UiTheme.Small, Margin = new Padding(0, 1, 0, 0) });
        return row;
    }

    // 名前行：名前（変えてあれば強調）と、右端の「既定」チェック
    private (Label name, CheckBox? chk, FlowLayoutPanel line) NameLine(FlowLayoutPanel row, TuneParam p, bool withDefault, string defaultText)
    {
        var line = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, WrapContents = false, AutoSize = true, Margin = new Padding(0) };
        var name = new Label { Text = p.Label + (p.Unit.Length > 0 ? $"（{p.Unit}）" : ""), AutoSize = true, MinimumSize = new Size(250, 0), Margin = new Padding(0, 4, 0, 0) };
        line.Controls.Add(name);
        CheckBox? chk = null;
        if (withDefault)
        {
            chk = new CheckBox { Text = defaultText, AutoSize = true, Margin = new Padding(0, 1, 0, 0) };
            line.Controls.Add(chk);
        }
        row.Controls.Add(line);
        return (name, chk, line);
    }

    private static void Mark(Label name, bool changed)
    {
        name.Font = changed ? UiTheme.Bold : UiTheme.Base;
        name.ForeColor = changed ? Color.FromArgb(0, 90, 170) : SystemColors.ControlText;
    }

    private void BuildNumberRow(FlowLayoutPanel row, TuneParam p)
    {
        var (name, chk, _) = NameLine(row, p, true, "既定を使う");
        int steps = (int)Math.Max(1, Math.Round((p.Max - p.Min) / p.Step));
        float step = p.Step;
        if (steps > 2000) { step = (p.Max - p.Min) / 2000f; steps = 2000; }
        var bar = new TrackBar { Minimum = 0, Maximum = steps, TickStyle = TickStyle.None, Width = 280, Height = 26, SmallChange = 1, LargeChange = Math.Max(1, steps / 10), Margin = new Padding(0) };
        var nud = new NumericUpDown { Minimum = (decimal)p.Min, Maximum = (decimal)p.Max, DecimalPlaces = p.Decimals, Increment = (decimal)p.Step, Width = 100, Margin = new Padding(6, 2, 0, 0) };
        var line = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, WrapContents = false, AutoSize = true, Margin = new Padding(0) };
        line.Controls.Add(bar); line.Controls.Add(nud);
        row.Controls.Add(line);

        float lastCustom = p.FirstCustomValue;
        int ToTick(float v) => (int)Math.Clamp(MathF.Round((v - p.Min) / step), 0, steps);
        float FromTick(int t) => MathF.Round((p.Min + t * step) / p.Step) * p.Step;

        void Refresh()
        {
            float v = Get(p.Key);
            bool isDefault = v < 0f;
            chk!.Checked = isDefault;
            float shown = isDefault ? p.Default : v;
            nud.Value = (decimal)Math.Clamp(shown, p.Min, p.Max);
            bar.Value = ToTick(shown);
            bar.Enabled = nud.Enabled = !isDefault;
            Mark(name, !isDefault);
        }
        chk!.CheckedChanged += (s, e) =>
        {
            if (_suppress) return;
            if (chk.Checked) { var cur = Get(p.Key); if (cur >= 0) lastCustom = cur; Set(p.Key, -1f); }
            else Set(p.Key, lastCustom);
            RefreshAll();
        };
        bar.ValueChanged += (s, e) =>
        {
            if (_suppress || chk.Checked) return;
            float v = Math.Clamp(FromTick(bar.Value), p.Min, p.Max);
            Set(p.Key, v); lastCustom = v;
            _suppress = true; nud.Value = (decimal)v; _suppress = false;
            Mark(name, true); _canvas.Invalidate();
        };
        nud.ValueChanged += (s, e) =>
        {
            if (_suppress || chk.Checked) return;
            float v = (float)nud.Value;
            Set(p.Key, v); lastCustom = v;
            _suppress = true; bar.Value = ToTick(v); _suppress = false;
            Mark(name, true); _canvas.Invalidate();
        };
        _refreshers.Add(Refresh);
        _suppress = true; Refresh(); _suppress = false;
    }

    private void BuildChoiceRow(FlowLayoutPanel row, TuneParam p)
    {
        string defText = p.DefaultText.Length > 0 ? $"既定：{p.DefaultText}" : "既定を使う";
        var (name, chk, _) = NameLine(row, p, true, defText);
        var cbo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 300, Margin = new Padding(0, 2, 0, 0) };
        cbo.Items.AddRange(p.Choices.Cast<object>().ToArray());
        row.Controls.Add(cbo);
        int lastCustom = p.ChoiceDefault;
        void Refresh()
        {
            float v = Get(p.Key);
            bool isDefault = v < 0f;
            chk!.Checked = isDefault;
            cbo.SelectedIndex = Math.Clamp(isDefault ? p.ChoiceDefault : (int)v, 0, p.Choices.Length - 1);
            cbo.Enabled = !isDefault;
            Mark(name, !isDefault);
        }
        chk!.CheckedChanged += (s, e) =>
        {
            if (_suppress) return;
            if (chk.Checked) { var cur = Get(p.Key); if (cur >= 0) lastCustom = (int)cur; Set(p.Key, -1f); }
            else Set(p.Key, lastCustom);
            RefreshAll();
        };
        cbo.SelectedIndexChanged += (s, e) =>
        {
            if (_suppress || chk.Checked) return;
            Set(p.Key, cbo.SelectedIndex); lastCustom = cbo.SelectedIndex;
            Mark(name, true); _canvas.Invalidate();
        };
        _refreshers.Add(Refresh);
        _suppress = true; Refresh(); _suppress = false;
    }

    private void BuildFlagRow(FlowLayoutPanel row, TuneParam p)
    {
        var chk = new CheckBox { Text = p.Label, AutoSize = true, Margin = new Padding(0, 2, 0, 0) };
        row.Controls.Add(chk);
        void Refresh() { chk.Checked = Get(p.Key) > 0.5f; chk.Font = chk.Checked ? UiTheme.Bold : UiTheme.Base; }
        chk.CheckedChanged += (s, e) => { if (_suppress) return; Set(p.Key, chk.Checked ? 1f : 0f); chk.Font = chk.Checked ? UiTheme.Bold : UiTheme.Base; _canvas.Invalidate(); };
        _refreshers.Add(Refresh);
        _suppress = true; Refresh(); _suppress = false;
    }

    private void BuildColorRow(FlowLayoutPanel row, TuneParam p)
    {
        var (name, chk, _) = NameLine(row, p, true, "指定する");
        var line = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, WrapContents = false, AutoSize = true, Margin = new Padding(0) };
        var btn = new Button { Size = new Size(90, 28), FlatStyle = FlatStyle.Flat, Margin = new Padding(0, 2, 6, 0) };
        var lbl = new Label { AutoSize = true, Margin = new Padding(0, 8, 0, 0), ForeColor = Color.DimGray };
        line.Controls.Add(btn); line.Controls.Add(lbl);
        row.Controls.Add(line);
        Color last = Color.FromArgb(0, 120, 255);
        void Refresh()
        {
            bool specified = Get(p.Key) >= 0 && Get(p.Key2) >= 0 && Get(p.Key3) >= 0;
            chk!.Checked = specified;
            if (specified)
            {
                last = Color.FromArgb((int)Math.Clamp(Get(p.Key), 0, 255), (int)Math.Clamp(Get(p.Key2), 0, 255), (int)Math.Clamp(Get(p.Key3), 0, 255));
                btn.BackColor = last;
                lbl.Text = $"R{last.R}  G{last.G}  B{last.B}";
            }
            else { btn.BackColor = SystemColors.Control; lbl.Text = p.DefaultText.Length > 0 ? $"指定なし：{p.DefaultText}" : "指定なし"; }
            btn.Enabled = specified;
            Mark(name, specified);
        }
        chk!.CheckedChanged += (s, e) =>
        {
            if (_suppress) return;
            if (chk.Checked) { Set(p.Key, last.R); Set(p.Key2, last.G); Set(p.Key3, last.B); }
            else { Set(p.Key, -1f); Set(p.Key2, -1f); Set(p.Key3, -1f); }
            RefreshAll();
        };
        btn.Click += (s, e) =>
        {
            using var dlg = new ColorDialog { Color = last, FullOpen = true };
            if (dlg.ShowDialog() != DialogResult.OK) return;
            last = dlg.Color; Set(p.Key, last.R); Set(p.Key2, last.G); Set(p.Key3, last.B);
            RefreshAll();
        };
        _refreshers.Add(Refresh);
        _suppress = true; Refresh(); _suppress = false;
    }
}
