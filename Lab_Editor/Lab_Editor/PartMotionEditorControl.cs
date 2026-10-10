namespace Lab_Editor;

// パーツ編集画面の詳細パネルに置く、「動き（モーション）」の編集欄。
//
// 選んでいるパーツのモーション一覧（PartDef.motions）を、スクリプトを組まずに
// 「いつ（トリガー）× どんな動き（種類）× スライダー」で作る。
// このコントロール自身は PartDef を知らない。Bind で渡された List<PartMotion> を直接書き換え、
// 変えたことを Changed イベントで知らせる（履歴・プレビューの更新は呼び出し側）。
public class PartMotionEditorControl : UserControl
{
    // 値が変わったとき。引数は履歴をまとめるためのキー（同じキーが続く間は1手にまとまる）
    public event Action<string>? Changed;
    // 「他の選択パーツへコピー」が押されたとき
    public event Action? CopyToOthersRequested;

    private List<PartMotion>? _motions;
    private bool _suppress;

    private readonly CheckedListBox _list = new();
    private readonly Button _btnAdd = new(), _btnDup = new(), _btnDel = new(), _btnUp = new(), _btnDown = new(), _btnCopy = new();
    private readonly Panel _card = new();
    private readonly ComboBox _cboTrigger = new(), _cboKind = new(), _cboEasing = new();
    private readonly NumericUpDown _numTrigParam = new(), _numAmount = new(), _numPeriod = new(), _numDuration = new(),
                                   _numDelay = new(), _numPhase = new(), _numPhaseIdx = new(), _numAxis = new();
    private readonly Label _lblTrigParam = new(), _lblAmount = new(), _lblHintTrigger = new(), _lblHintKind = new(), _lblEmpty = new();
    private readonly Dictionary<Control, Control> _rowOf = new(); // 入力欄 → その行（表示/非表示を切り替える）

    public PartMotionEditorControl()
    {
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Font = UiTheme.Base;
        Margin = new Padding(0);

        var flow = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = new Padding(0), Padding = new Padding(0),
        };
        Controls.Add(flow);

        var intro = new Label
        {
            Text = "いつ・どんな動きをするかを選ぶだけで、パーツを動かせます。いくつでも重ねられます。",
            AutoSize = true, MaximumSize = new Size(300, 0), ForeColor = Color.DimGray, Margin = new Padding(0, 0, 0, 3),
        };
        flow.Controls.Add(intro);

        _list.Width = 296; _list.Height = 92; _list.CheckOnClick = false; _list.IntegralHeight = false;
        _list.ItemCheck += (s, e) =>
        {
            if (_suppress || _motions == null || e.Index < 0 || e.Index >= _motions.Count) return;
            _motions[e.Index].enabled = e.NewValue == CheckState.Checked;
            Changed?.Invoke("motion-enabled");
        };
        _list.MouseDown += (s, e) =>
        {
            // チェック欄（左端）の外を押したときだけ選択。チェック欄はそのままチェックの切り替え
            int i = _list.IndexFromPoint(e.Location);
            if (i >= 0 && e.X > 20) { _list.SelectedIndex = i; }
            else if (i >= 0) { _list.SetItemChecked(i, !_list.GetItemChecked(i)); _list.SelectedIndex = i; }
        };
        _list.SelectedIndexChanged += (s, e) => LoadCard();
        flow.Controls.Add(_list);

        var bar = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = true, Margin = new Padding(0, 2, 0, 2) };
        StyleSmall(_btnAdd, "＋ 動きを追加 ▾");
        StyleSmall(_btnDup, "複製");
        StyleSmall(_btnDel, "削除");
        StyleSmall(_btnUp, "↑");
        StyleSmall(_btnDown, "↓");
        _btnAdd.Click += (s, e) => BuildAddMenu().Show(_btnAdd, new Point(0, _btnAdd.Height));
        _btnDup.Click += (s, e) => DuplicateSelected();
        _btnDel.Click += (s, e) => DeleteSelected();
        _btnUp.Click += (s, e) => MoveSelected(-1);
        _btnDown.Click += (s, e) => MoveSelected(+1);
        bar.Controls.AddRange(new Control[] { _btnAdd, _btnDup, _btnDel, _btnUp, _btnDown });
        flow.Controls.Add(bar);

        _lblEmpty.Text = "動きはまだありません。「＋ 動きを追加」から選んでください。\n（例：攻撃したとき前へ突く、溜めている間ふくらむ、ずっとゆらゆら…）";
        _lblEmpty.AutoSize = true; _lblEmpty.MaximumSize = new Size(300, 0); _lblEmpty.ForeColor = Color.FromArgb(150, 90, 0);
        _lblEmpty.Margin = new Padding(0, 4, 0, 4);
        flow.Controls.Add(_lblEmpty);

        BuildCard();
        flow.Controls.Add(_card);

        StyleSmall(_btnCopy, "この動きを、選んでいる他のパーツへコピー");
        _btnCopy.Click += (s, e) => CopyToOthersRequested?.Invoke();
        flow.Controls.Add(_btnCopy);
        var tip = new ToolTip();
        tip.SetToolTip(_btnCopy, "選んでいるパーツ（複数選択）の全員へ、このパーツの動きの一覧をそのままコピーします。\n「パーツ番号ごとのずれ」を使うと、コピーしたパーツが少しずつずれて動きます。");
    }

    private static void StyleSmall(Button b, string text)
    {
        b.Text = text; b.AutoSize = true; b.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        b.Padding = new Padding(5, 2, 5, 2); b.Margin = new Padding(0, 0, 3, 3); b.Font = UiTheme.Base;
    }

    // ================= 編集カード =================

    private void BuildCard()
    {
        _card.AutoSize = true; _card.AutoSizeMode = AutoSizeMode.GrowAndShrink; _card.Margin = new Padding(0, 4, 0, 4);
        var t = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2, Margin = new Padding(0), Location = new Point(0, 0) };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 118));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _card.Controls.Add(t);

        void Row(string label, Control c, Label? dynamicLabel = null)
        {
            int r = t.RowCount; t.RowCount = r + 1; t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var lbl = dynamicLabel ?? new Label();
            lbl.Text = label; lbl.AutoSize = true; lbl.MaximumSize = new Size(116, 0); lbl.Anchor = AnchorStyles.Left; lbl.Margin = new Padding(0, 6, 3, 3);
            c.Margin = new Padding(3);
            t.Controls.Add(lbl, 0, r); t.Controls.Add(c, 1, r);
            _rowOf[c] = lbl; // ラベルも一緒に隠すため、ラベルを覚えておく
        }
        void Hint(Label l)
        {
            int r = t.RowCount; t.RowCount = r + 1; t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            l.AutoSize = true; l.MaximumSize = new Size(290, 0); l.ForeColor = Color.Gray; l.Font = UiTheme.Small; l.Margin = new Padding(0, 0, 0, 4);
            t.Controls.Add(l, 0, r); t.SetColumnSpan(l, 2);
        }
        void NumSetup(NumericUpDown n, decimal min, decimal max, int dec, decimal inc)
        { n.Minimum = min; n.Maximum = max; n.DecimalPlaces = dec; n.Increment = inc; n.Width = 90; }

        _cboTrigger.DropDownStyle = ComboBoxStyle.DropDownList; _cboTrigger.Width = 170;
        _cboTrigger.Items.AddRange(PartMotionInfo.Triggers.Select(x => x.Label).Cast<object>().ToArray());
        _cboTrigger.SelectedIndexChanged += (s, e) => { if (_suppress) return; var m = Cur(); if (m == null) return; m.trigger = PartMotionInfo.Triggers[Math.Max(0, _cboTrigger.SelectedIndex)].Name; ApplyTriggerDefaults(m); Refresh2("motion-trigger"); };
        Row("いつ動くか", _cboTrigger);
        NumSetup(_numTrigParam, 0, 1000, 2, 1);
        Row("", _numTrigParam, _lblTrigParam);
        Hint(_lblHintTrigger);

        _cboKind.DropDownStyle = ComboBoxStyle.DropDownList; _cboKind.Width = 170;
        _cboKind.Items.AddRange(PartMotionInfo.Kinds.Select(x => x.Label).Cast<object>().ToArray());
        _cboKind.SelectedIndexChanged += (s, e) => { if (_suppress) return; var m = Cur(); if (m == null) return; var oldK = PartMotionInfo.Kind(m.kind); m.kind = PartMotionInfo.Kinds[Math.Max(0, _cboKind.SelectedIndex)].Name; var nk = PartMotionInfo.Kind(m.kind); m.amount = nk.AmountDefault; Refresh2("motion-kind"); };
        Row("どんな動き", _cboKind);
        NumSetup(_numAmount, -2000, 2000, 2, 1);
        Row("", _numAmount, _lblAmount);
        Hint(_lblHintKind);

        NumSetup(_numPeriod, 1, 2000, 0, 5);
        Row("1周の長さ(フレーム)", _numPeriod);
        NumSetup(_numDuration, 1, 2000, 0, 5);
        Row("動いている長さ(フレーム)", _numDuration);
        NumSetup(_numDelay, 0, 2000, 0, 5);
        Row("始まるまで(フレーム)", _numDelay);
        NumSetup(_numPhase, -4, 4, 2, 0.05m);
        Row("位相のずれ(0〜1)", _numPhase);
        NumSetup(_numPhaseIdx, -2, 2, 3, 0.05m);
        Row("パーツ番号ごとのずれ", _numPhaseIdx);
        NumSetup(_numAxis, -360, 360, 0, 15);
        Row("動く向き(度)", _numAxis);
        _cboEasing.DropDownStyle = ComboBoxStyle.DropDownList; _cboEasing.Width = 170;
        _cboEasing.Items.AddRange(new object[] { "なめらか", "一定の速さ（三角）", "カクカク" });
        _cboEasing.SelectedIndexChanged += (s, e) => { if (_suppress) return; var m = Cur(); if (m == null) return; m.easing = Math.Max(0, _cboEasing.SelectedIndex); Refresh2("motion-easing"); };
        Row("波の形", _cboEasing);

        void Hook(NumericUpDown n, Action<PartMotion, float> set, string key)
            => n.ValueChanged += (s, e) => { if (_suppress) return; var m = Cur(); if (m == null) return; set(m, (float)n.Value); Refresh2(key, reloadCard: false); };
        Hook(_numTrigParam, (m, v) => m.trigger_param = v, "motion-trigparam");
        Hook(_numAmount, (m, v) => m.amount = v, "motion-amount");
        Hook(_numPeriod, (m, v) => m.period = v, "motion-period");
        Hook(_numDuration, (m, v) => m.duration = v, "motion-duration");
        Hook(_numDelay, (m, v) => m.delay = v, "motion-delay");
        Hook(_numPhase, (m, v) => m.phase = v, "motion-phase");
        Hook(_numPhaseIdx, (m, v) => m.phase_by_index = v, "motion-phaseidx");
        Hook(_numAxis, (m, v) => m.axis = v, "motion-axis");
    }

    // トリガーを変えたとき、そのトリガーの初期値（距離など）を入れる
    private static void ApplyTriggerDefaults(PartMotion m)
    {
        var ti = PartMotionInfo.Trigger(m.trigger);
        if (ti.ParamLabel != null && (m.trigger_param <= 0f || m.trigger_param > ti.ParamMax)) m.trigger_param = ti.ParamDefault;
    }

    // ================= 外から使う =================

    // 編集対象を差し替える。motions が null なら（パーツ未選択）欄を無効にする。
    public void Bind(List<PartMotion>? motions)
    {
        _motions = motions;
        Enabled = motions != null;
        int keep = _list.SelectedIndex;
        RefreshList(Math.Min(keep, (motions?.Count ?? 0) - 1));
    }

    // 選んでいる動き（無ければ null）
    public PartMotion? Current => Cur();
    public int SelectedIndex => _list.SelectedIndex;
    public void SelectIndex(int i) { if (i >= 0 && i < _list.Items.Count) _list.SelectedIndex = i; }

    private PartMotion? Cur() => (_motions != null && _list.SelectedIndex >= 0 && _list.SelectedIndex < _motions.Count) ? _motions[_list.SelectedIndex] : null;

    // ================= 一覧 =================

    private void RefreshList(int select)
    {
        _suppress = true;
        _list.Items.Clear();
        if (_motions != null)
            foreach (var m in _motions) _list.Items.Add(PartMotionInfo.Describe(m), m.enabled);
        if (select >= 0 && select < _list.Items.Count) _list.SelectedIndex = select;
        else if (_list.Items.Count > 0) _list.SelectedIndex = 0;
        _suppress = false;
        LoadCard();
    }

    // 一覧の1行の表示だけ更新する（選択は動かさない）
    private void RefreshListText()
    {
        if (_motions == null) return;
        _suppress = true;
        int sel = _list.SelectedIndex;
        for (int i = 0; i < _motions.Count && i < _list.Items.Count; i++) _list.Items[i] = PartMotionInfo.Describe(_motions[i]);
        if (sel >= 0 && sel < _list.Items.Count) _list.SelectedIndex = sel;
        for (int i = 0; i < _motions.Count && i < _list.Items.Count; i++) _list.SetItemChecked(i, _motions[i].enabled);
        _suppress = false;
    }

    private void Refresh2(string key, bool reloadCard = true)
    {
        RefreshListText();
        if (reloadCard) LoadCard();
        Changed?.Invoke(key);
    }

    private void LoadCard()
    {
        var m = Cur();
        bool has = m != null;
        _card.Visible = has;
        _btnCopy.Visible = has;
        _btnDup.Enabled = _btnDel.Enabled = _btnUp.Enabled = _btnDown.Enabled = has;
        _lblEmpty.Visible = _motions != null && _motions.Count == 0;
        if (m == null) return;

        _suppress = true;
        var ti = PartMotionInfo.Trigger(m.trigger);
        var ki = PartMotionInfo.Kind(m.kind);
        _cboTrigger.SelectedIndex = PartMotionInfo.TriggerIndex(m.trigger);
        _cboKind.SelectedIndex = PartMotionInfo.KindIndex(m.kind);

        _lblTrigParam.Text = ti.ParamLabel ?? "";
        SetVisible(_numTrigParam, ti.ParamLabel != null);
        if (ti.ParamLabel != null)
        {
            _numTrigParam.Minimum = (decimal)ti.ParamMin; _numTrigParam.Maximum = (decimal)ti.ParamMax; _numTrigParam.Increment = (decimal)ti.ParamStep;
            _numTrigParam.DecimalPlaces = ti.ParamStep < 1 ? 2 : 0;
            _numTrigParam.Value = Clamp(m.trigger_param, _numTrigParam);
        }
        _lblHintTrigger.Text = ti.Hint + (ti.EnemyOnly ? "（敵のパーツだけで動きます）" : "");

        _lblAmount.Text = ki.AmountLabel;
        _numAmount.Minimum = (decimal)ki.AmountMin; _numAmount.Maximum = (decimal)ki.AmountMax;
        _numAmount.Increment = (decimal)ki.AmountStep; _numAmount.DecimalPlaces = ki.AmountDecimals;
        _numAmount.Value = Clamp(m.amount, _numAmount);
        _lblHintKind.Text = ki.Hint;

        SetVisible(_numPeriod, !ti.IsEvent && ki.UsesPeriod);
        SetVisible(_numDuration, ti.IsEvent);
        _numPeriod.Value = Clamp(m.period, _numPeriod);
        _numDuration.Value = Clamp(m.duration, _numDuration);
        _numDelay.Value = Clamp(m.delay, _numDelay);
        _numPhase.Value = Clamp(m.phase, _numPhase);
        _numPhaseIdx.Value = Clamp(m.phase_by_index, _numPhaseIdx);
        SetVisible(_numPhase, ki.UsesPeriod && ki.Name != "spin");
        SetVisible(_numPhaseIdx, ki.UsesPeriod && ki.Name != "spin" || ki.Name == "shake");
        SetVisible(_numAxis, ki.UsesAxis);
        _numAxis.Value = Clamp(m.axis, _numAxis);
        SetVisible(_cboEasing, ki.UsesEasing);
        _cboEasing.SelectedIndex = Math.Clamp(m.easing, 0, 2);
        _suppress = false;
    }

    private void SetVisible(Control c, bool visible)
    {
        c.Visible = visible;
        if (_rowOf.TryGetValue(c, out var lbl)) lbl.Visible = visible;
    }

    private static decimal Clamp(float v, NumericUpDown n) => Math.Max(n.Minimum, Math.Min(n.Maximum, (decimal)v));

    // ================= 追加・複製・削除・並べ替え =================

    private ContextMenuStrip BuildAddMenu()
    {
        var menu = new ContextMenuStrip();
        var sub = new ToolStripMenuItem("よくある動き（すぐ使える）");
        foreach (var (label, make) in Presets())
        {
            var item = new ToolStripMenuItem(label);
            item.Click += (s, e) => AddMotion(make());
            sub.DropDownItems.Add(item);
        }
        menu.Items.Add(sub);
        menu.Items.Add(new ToolStripSeparator());
        foreach (var k in PartMotionInfo.Kinds)
        {
            var kk = k;
            var item = new ToolStripMenuItem(kk.Label) { ToolTipText = kk.Hint };
            item.Click += (s, e) => AddMotion(PartMotionInfo.CreateDefault(kk.Name));
            menu.Items.Add(item);
        }
        return menu;
    }

    // 「よくある動き」。名前と、その動きを作る関数
    public static IEnumerable<(string label, Func<PartMotion> make)> Presets()
    {
        yield return ("攻撃したとき、前へ突く", () => new PartMotion { trigger = "attack", kind = "thrust", amount = 16, axis = 0, duration = 24 });
        yield return ("溜めている間、ふくらむ", () => new PartMotion { trigger = "charge", kind = "pulse", amount = 0.3f, period = 24 });
        yield return ("ずっと、ゆらゆら上下に揺れる", () => new PartMotion { trigger = "always", kind = "sway", amount = 4, axis = 90, period = 90 });
        yield return ("ずっと、回り続ける", () => new PartMotion { trigger = "always", kind = "spin", amount = 360, period = 120 });
        yield return ("プレイヤーが近いと、点滅する", () => new PartMotion { trigger = "near", trigger_param = 160, kind = "blink", amount = 0.7f, period = 16 });
        yield return ("弾を受けると、ブルブル震える", () => new PartMotion { trigger = "hurt", kind = "shake", amount = 3, duration = 20 });
        yield return ("ジャンプしたとき、跳ねる", () => new PartMotion { trigger = "jump", kind = "slide", amount = -8, axis = 90, duration = 24 });
        yield return ("動いている間、首を振る", () => new PartMotion { trigger = "moving", trigger_param = 0.3f, kind = "swing", amount = 15, period = 24 });
        yield return ("プレイヤーのほうを向く", () => new PartMotion { trigger = "always", kind = "face_player", amount = 0 });
    }

    private void AddMotion(PartMotion m)
    {
        if (_motions == null) return;
        _motions.Add(m);
        RefreshList(_motions.Count - 1);
        Changed?.Invoke("motion-add");
    }

    private void DuplicateSelected()
    {
        var m = Cur(); if (m == null || _motions == null) return;
        _motions.Insert(_list.SelectedIndex + 1, m.Clone());
        RefreshList(_list.SelectedIndex + 1);
        Changed?.Invoke("motion-dup");
    }

    private void DeleteSelected()
    {
        if (Cur() == null || _motions == null) return;
        int i = _list.SelectedIndex;
        _motions.RemoveAt(i);
        RefreshList(Math.Min(i, _motions.Count - 1));
        Changed?.Invoke("motion-del");
    }

    private void MoveSelected(int dir)
    {
        if (Cur() == null || _motions == null) return;
        int i = _list.SelectedIndex, j = i + dir;
        if (j < 0 || j >= _motions.Count) return;
        (_motions[i], _motions[j]) = (_motions[j], _motions[i]);
        RefreshList(j);
        Changed?.Invoke("motion-move");
    }
}
