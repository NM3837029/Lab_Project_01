using System.Drawing.Drawing2D;

namespace Lab_Editor;

// ======================================================
// GradeConditionEditor - 「出る条件」を決める共通の部品
//
// アセット画面（補正スイッチ・補正ロック足場）と、ステージの右クリック「出る条件…」の両方で使う。
// どこで開いても同じ見た目・同じ言葉で条件を決められるようにするのが目的。
//
// モード（Kind）は3つ：
//   Range      … 見る値を選び、目盛りつきの帯の上の2つのつまみで「下限〜上限」を決める
//                 （見る値が「色フィルタ」のときだけ、つまみの代わりに赤・緑・青のボタン）
//   ColorLock  … 赤・緑・青のボタンだけ（色ロック足場）
//   BrightLock … 暗い・明るいのボタンだけ（明暗ロック足場）
//
// 値は、ゲームと同じ「内部の値」で持つ（明るさなら 1.0＝100%）。画面には％や度に直して見せる。
// 下限・上限の「未指定」はゲームと同じく -999 / 999。両方とも未指定なら、見る値ごとの標準の範囲になる。
//
// 値を変えたのが人の操作なら ConditionChanged を出す。Set○○ で外から入れたときは出さない
// （開いた瞬間に「変更あり」になって、何もしていないのに保存されるのを防ぐため）。
// ======================================================
public sealed class GradeConditionEditor : UserControl
{
    // ---- 値 ----
    public GradeKind Kind { get => _kind; set { _kind = value; Relayout(); } }
    public int ChannelValue { get; private set; }     // 見る値（0〜7）
    public float Lo { get; private set; } = GradeCondition.NoLo; // 下限（内部の値。-999＝未指定）
    public float Hi { get; private set; } = GradeCondition.NoHi; // 上限（内部の値。999＝未指定）
    public int ColorValue { get; private set; } = 1;  // 色ロック足場の色（1赤 2緑 3青）
    public bool BrightValue { get; private set; }     // 明暗ロック足場：true＝明るいとき

    // 人の操作で条件が変わったとき
    public event EventHandler? ConditionChanged;

    // ---- 確認用（検証プログラムから見る）----
    public string SentenceText => _sentence.Text;
    public bool LowerUnbounded => _chkNoLo.Checked;
    public bool UpperUnbounded => _chkNoHi.Checked;
    public bool RangeBarVisible => _bar.Visible;
    public bool FilterButtonsVisible => _colorRow.Visible;

    private GradeKind _kind = GradeKind.Range;
    private bool _setting; // 外から値を入れている最中（イベントを出さない）

    // ---- 部品 ----
    private readonly FlowLayoutPanel _root = new() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(4) };
    private readonly Panel _channelRow = new() { Height = 28 };
    private readonly ComboBox _cboChannel = new() { DropDownStyle = ComboBoxStyle.DropDownList, Font = UiTheme.Base, Width = 200 };
    private readonly RangeBar _bar;
    private readonly Panel _limitRow = new() { Height = 26 };
    private readonly CheckBox _chkNoLo = new() { Text = "下限なし", AutoSize = true, Font = UiTheme.Base };
    private readonly CheckBox _chkNoHi = new() { Text = "上限なし", AutoSize = true, Font = UiTheme.Base };
    private readonly Label _limitText = new() { AutoSize = true, Font = UiTheme.Small, ForeColor = Color.DimGray };
    private readonly FlowLayoutPanel _colorRow = new() { AutoSize = true, WrapContents = false, Margin = new Padding(0, 4, 0, 4) };
    private readonly FlowLayoutPanel _brightRow = new() { AutoSize = true, WrapContents = false, Margin = new Padding(0, 4, 0, 4) };
    private readonly Label _sentence = new() { AutoSize = false, Height = 30, Font = new Font(UiTheme.Bold.FontFamily, 10.5f, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(26, 0, 0, 0) };
    private readonly Label _quickLabel = new() { Text = "よく使う条件", AutoSize = true, Font = UiTheme.Small, ForeColor = Color.DimGray, Margin = new Padding(0, 6, 0, 0) };
    private readonly FlowLayoutPanel _quickRow = new() { AutoSize = true, WrapContents = true, MaximumSize = new Size(380, 0) };
    private readonly Button[] _colorButtons = new Button[3];
    private readonly Button[] _brightButtons = new Button[2];

    // よく使う条件（ボタン1つ＝見る値・下限・上限）。GimmickPresets の補正スイッチ・ロック足場の条件から選んだ。
    private static readonly (string Label, int Ch, float Lo, float Hi)[] Quick =
    {
        ("暗いとき",     0, GradeCondition.NoLo, 0.6f),
        ("明るいとき",   0, 1.3f, GradeCondition.NoHi),
        ("白黒のとき",   2, GradeCondition.NoLo, 0.2f),
        ("あざやかなとき", 2, 1.3f, GradeCondition.NoHi),
        ("暖色のとき",   4, 0.3f, GradeCondition.NoHi),
        ("寒色のとき",   4, GradeCondition.NoLo, -0.3f),
        ("コントラスト高", 1, 0.3f, GradeCondition.NoHi),
        ("拡大中",       6, 1.15f, GradeCondition.NoHi),
        ("縮小中",       7, GradeCondition.NoLo, 0.95f),
    };

    public GradeConditionEditor()
    {
        Font = UiTheme.Base;
        BackColor = SystemColors.Window;

        // 見る値の選択
        var chLabel = new Label { Text = "見る値", AutoSize = true, Font = UiTheme.Bold, Location = new Point(0, 5) };
        _cboChannel.Location = new Point(56, 2);
        for (int i = 0; i < GradeCondition.ChannelCount; i++) _cboChannel.Items.Add(GradeCondition.Channel(i).Name);
        _cboChannel.SelectedIndexChanged += (s, e) => { if (!_setting) SetChannelFromUser(_cboChannel.SelectedIndex); };
        _channelRow.Controls.Add(chLabel);
        _channelRow.Controls.Add(_cboChannel);

        // 目盛りつきの帯
        _bar = new RangeBar(this) { Height = 62, Margin = new Padding(0, 4, 0, 0) };

        // 下限なし・上限なし
        _chkNoLo.Location = new Point(0, 3);
        _chkNoHi.Location = new Point(96, 3);
        _limitText.Location = new Point(192, 6);
        _chkNoLo.CheckedChanged += (s, e) => { if (!_setting) SetBoundFromCheck(lower: true, unbounded: _chkNoLo.Checked); };
        _chkNoHi.CheckedChanged += (s, e) => { if (!_setting) SetBoundFromCheck(lower: false, unbounded: _chkNoHi.Checked); };
        _limitRow.Controls.Add(_chkNoLo);
        _limitRow.Controls.Add(_chkNoHi);
        _limitRow.Controls.Add(_limitText);

        // 色のボタン（色フィルタの条件・色ロック足場）
        for (int i = 0; i < 3; i++)
        {
            int c = i + 1;
            var b = MakeChoiceButton(GradeCondition.FilterName(c), GradeCondition.FilterColor(c));
            b.Click += (s, e) => ChooseColor(c);
            _colorButtons[i] = b;
            _colorRow.Controls.Add(b);
        }
        // 暗い・明るいのボタン（明暗ロック足場）
        for (int i = 0; i < 2; i++)
        {
            bool bright = i == 1;
            var b = MakeChoiceButton(bright ? "明るい" : "暗い", GradeCondition.BrightLockColor(bright));
            b.Click += (s, e) => ChooseBright(bright);
            _brightButtons[i] = b;
            _brightRow.Controls.Add(b);
        }

        // よく使う条件
        foreach (var q in Quick)
        {
            var b = new Button { Text = q.Label, AutoSize = true, Font = UiTheme.Small, Margin = new Padding(0, 2, 4, 2), FlatStyle = FlatStyle.System };
            var qq = q;
            b.Click += (s, e) => { ApplyRange(qq.Ch, qq.Lo, qq.Hi); Fire(); };
            _quickRow.Controls.Add(b);
        }

        // 文章の左に、条件の色の四角を描く
        _sentence.Paint += (s, e) =>
        {
            using var br = new SolidBrush(CurrentColor());
            e.Graphics.FillRectangle(br, 4, (_sentence.Height - 16) / 2, 16, 16);
            e.Graphics.DrawRectangle(Pens.Gray, 4, (_sentence.Height - 16) / 2, 16, 16);
        };

        _root.Controls.AddRange(new Control[] { _channelRow, _bar, _limitRow, _colorRow, _brightRow, _sentence, _quickLabel, _quickRow });
        Controls.Add(_root);
        _root.SizeChanged += (s, e) => FitWidths();
        Relayout();
    }

    private static Button MakeChoiceButton(string text, Color color)
    {
        var b = new Button
        {
            Text = text, Width = 84, Height = 34, Font = UiTheme.Bold, FlatStyle = FlatStyle.Flat,
            BackColor = color, ForeColor = color.GetBrightness() < 0.45f ? Color.White : Color.Black, Margin = new Padding(0, 0, 8, 0),
        };
        b.FlatAppearance.BorderSize = 1;
        return b;
    }

    // 部品の幅に合わせて、横に伸ばす部品の幅をそろえる
    private void FitWidths()
    {
        int w = Math.Max(200, _root.ClientSize.Width - _root.Padding.Horizontal - 4);
        foreach (Control c in new Control[] { _channelRow, _bar, _limitRow, _sentence }) c.Width = w;
        _quickRow.MaximumSize = new Size(w, 0);
    }

    // ---- 外から値を入れる（イベントは出さない）----
    public void SetRange(int ch, float lo, float hi) { _setting = true; try { ApplyRange(ch, lo, hi); } finally { _setting = false; } }
    public void SetColor(int c) { _setting = true; try { ColorValue = (c >= 1 && c <= 3) ? c : 1; RefreshView(); } finally { _setting = false; } }
    public void SetBright(bool b) { _setting = true; try { BrightValue = b; RefreshView(); } finally { _setting = false; } }

    // ---- 人の操作（イベントを出す）----
    // 色のボタン。Range モード（見る値＝色フィルタ）なら下限＝上限＝色番号、ColorLock モードなら ColorValue
    public void ChooseColor(int c)
    {
        if (_kind == GradeKind.ColorLock) ColorValue = c;
        else { ChannelValue = 5; Lo = c; Hi = c; }
        RefreshView();
        Fire();
    }
    public void ChooseBright(bool bright) { BrightValue = bright; RefreshView(); Fire(); }

    // 見る値を人が選び直した。前の範囲は単位が違って意味を持たないので、標準（両方未指定）へ戻す。
    // ただし色フィルタは「未指定」の標準が赤なので、はじめから赤を選んだ状態にする。
    public void SetChannelFromUser(int ch)
    {
        ch = GradeCondition.Channel(ch).Id;
        if (ch == 5) ApplyRange(5, 1, 1);
        else ApplyRange(ch, GradeCondition.NoLo, GradeCondition.NoHi);
        Fire();
    }

    // つまみを表示の値で動かした（下限）。両方とも未指定（標準）だった場合、もう片方は標準の範囲の値で確定させる
    // （つまみを動かした瞬間に、動かしていない側まで端へ飛んでしまわないように）。
    public void SetLowerFromView(float view) => SetBoundFromView(lower: true, view);
    public void SetUpperFromView(float view) => SetBoundFromView(lower: false, view);

    private void SetBoundFromView(bool lower, float view)
    {
        var info = GradeCondition.Channel(ChannelValue);
        GradeCondition.EffectiveRange(ChannelValue, Lo, Hi, out float elo, out float ehi);
        float v = info.FromView(Math.Clamp(view, info.ViewMin, info.ViewMax));
        if (lower)
        {
            // 上限より上へは行かない
            if (!GradeCondition.IsNoHi(ehi) && v > ehi) v = ehi;
            Lo = v; Hi = ehi;
        }
        else
        {
            if (!GradeCondition.IsNoLo(elo) && v < elo) v = elo;
            Hi = v; Lo = elo;
        }
        RefreshView();
        Fire();
    }

    // 「下限なし」「上限なし」を切り替えた。外したときは、今のつまみの位置（帯の端）で確定させる。
    private void SetBoundFromCheck(bool lower, bool unbounded)
    {
        var info = GradeCondition.Channel(ChannelValue);
        GradeCondition.EffectiveRange(ChannelValue, Lo, Hi, out float elo, out float ehi);
        if (lower) { Lo = unbounded ? GradeCondition.NoLo : (GradeCondition.IsNoLo(elo) ? info.FromView(info.ViewMin) : elo); Hi = ehi; }
        else { Hi = unbounded ? GradeCondition.NoHi : (GradeCondition.IsNoHi(ehi) ? info.FromView(info.ViewMax) : ehi); Lo = elo; }
        // 両方「なし」になったら、それは「標準の範囲」という意味になる（ゲームと同じ約束）
        RefreshView();
        Fire();
    }

    private void ApplyRange(int ch, float lo, float hi)
    {
        ChannelValue = GradeCondition.Channel(ch).Id;
        Lo = lo; Hi = hi;
        RefreshView();
    }

    private void Fire() { if (!_setting) ConditionChanged?.Invoke(this, EventArgs.Empty); }

    // 今の条件の色（文章の左の四角・帯の範囲の縁）
    internal Color CurrentColor() => _kind switch
    {
        GradeKind.ColorLock => GradeCondition.ColorLockColor(ColorValue),
        GradeKind.BrightLock => GradeCondition.BrightLockColor(BrightValue),
        _ => GradeCondition.ConditionColor(ChannelValue, Lo, Hi),
    };

    // モードに合わせて、出す部品を切り替える
    private void Relayout()
    {
        bool range = _kind == GradeKind.Range;
        _channelRow.Visible = range;
        _quickLabel.Visible = range;
        _quickRow.Visible = range;
        RefreshView();
    }

    // 値に合わせて、帯・チェック・ボタン・文章を描き直す
    private void RefreshView()
    {
        bool prev = _setting;
        _setting = true;
        try
        {
            bool range = _kind == GradeKind.Range;
            bool filter = range && ChannelValue == 5;
            _bar.Visible = range && !filter;
            _limitRow.Visible = range && !filter;
            _colorRow.Visible = _kind == GradeKind.ColorLock || filter;
            _brightRow.Visible = _kind == GradeKind.BrightLock;
            if (range && _cboChannel.SelectedIndex != ChannelValue) _cboChannel.SelectedIndex = ChannelValue;

            // 片方だけでも指定されていれば、指定の無い側は「なし」。両方未指定（標準）なら、標準の範囲の端の有無に従う
            GradeCondition.EffectiveRange(ChannelValue, Lo, Hi, out float elo, out float ehi);
            _chkNoLo.Checked = GradeCondition.IsNoLo(elo);
            _chkNoHi.Checked = GradeCondition.IsNoHi(ehi);
            var info = GradeCondition.Channel(ChannelValue);
            string Fmt(float v) => info.ToView(v).ToString(info.Unit == "倍" ? "0.00" : "0") + info.Unit;
            bool standard = GradeCondition.IsNoLo(Lo) && GradeCondition.IsNoHi(Hi);
            _limitText.Text = $"下限 {(GradeCondition.IsNoLo(elo) ? "なし" : Fmt(elo))}　上限 {(GradeCondition.IsNoHi(ehi) ? "なし" : Fmt(ehi))}"
                              + (standard ? "（標準）" : "");

            // 選ばれている色・明暗のボタンを太枠で示す
            int selColor = _kind == GradeKind.ColorLock ? ColorValue : (int)(Lo + 0.5f);
            for (int i = 0; i < 3; i++) _colorButtons[i].FlatAppearance.BorderSize = (i + 1 == selColor) ? 4 : 1;
            _brightButtons[0].FlatAppearance.BorderSize = BrightValue ? 1 : 4;
            _brightButtons[1].FlatAppearance.BorderSize = BrightValue ? 4 : 1;

            _sentence.Text = _kind switch
            {
                GradeKind.ColorLock => GradeCondition.ColorSentence(ColorValue),
                GradeKind.BrightLock => GradeCondition.BrightSentence(BrightValue),
                _ => GradeCondition.Sentence(ChannelValue, Lo, Hi),
            };
            _sentence.Invalidate();
            _bar.Invalidate();
        }
        finally { _setting = prev; }
    }

    // ======================================================
    // 目盛りつきの帯。見る値に合ったグラデーションの上に、出る範囲を明るく、出ない範囲を暗く描く。
    // つまみ（▲）を左右にドラッグして下限・上限を動かす。「なし」の側のつまみは帯の端に白抜きで出し、
    // つかんで動かすと「なし」が外れてその位置で決まる。
    // ======================================================
    private sealed class RangeBar : Control
    {
        private readonly GradeConditionEditor _owner;
        private int _drag = -1; // 0＝下限のつまみ、1＝上限のつまみ、-1＝つかんでいない
        private const int Pad = 14;   // 帯の左右の余白（つまみが端で切れないように）
        private const int TrackTop = 6, TrackH = 22;

        public RangeBar(GradeConditionEditor owner)
        {
            _owner = owner;
            DoubleBuffered = true;
            Cursor = Cursors.Hand;
        }

        private RectangleF Track => new(Pad, TrackTop, Math.Max(10, Width - Pad * 2), TrackH);

        private float ViewToX(float view)
        {
            var info = GradeCondition.Channel(_owner.ChannelValue);
            var t = Track;
            float k = (view - info.ViewMin) / (info.ViewMax - info.ViewMin);
            return t.Left + Math.Clamp(k, 0f, 1f) * t.Width;
        }
        private float XToView(float x)
        {
            var info = GradeCondition.Channel(_owner.ChannelValue);
            var t = Track;
            float k = Math.Clamp((x - t.Left) / t.Width, 0f, 1f);
            float v = info.ViewMin + k * (info.ViewMax - info.ViewMin);
            // 目盛りに吸い付ける（倍は 0.05 刻み、それ以外は 5 刻み）
            float step = info.Unit == "倍" ? 0.05f : 5f;
            return (float)Math.Round(v / step) * step;
        }

        // 今の下限・上限のつまみの位置（x）。「なし」なら帯の端
        private (float lx, float hx, bool noLo, bool noHi) KnobXs()
        {
            var info = GradeCondition.Channel(_owner.ChannelValue);
            GradeCondition.EffectiveRange(_owner.ChannelValue, _owner.Lo, _owner.Hi, out float elo, out float ehi);
            bool noLo = GradeCondition.IsNoLo(elo), noHi = GradeCondition.IsNoHi(ehi);
            var t = Track;
            float lx = noLo ? t.Left : ViewToX(info.ToView(elo));
            float hx = noHi ? t.Right : ViewToX(info.ToView(ehi));
            return (lx, hx, noLo, noHi);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var info = GradeCondition.Channel(_owner.ChannelValue);
            var t = Track;

            // 1) 見る値のグラデーション（色相は虹）
            using (var br = new LinearGradientBrush(t, info.Low, info.High, LinearGradientMode.Horizontal))
            {
                if (info.Id == 3)
                {
                    var blend = new ColorBlend
                    {
                        Colors = new[] { Color.Cyan, Color.Blue, Color.Magenta, Color.Red, Color.Yellow, Color.Lime, Color.Cyan },
                        Positions = new[] { 0f, 1f / 6, 2f / 6, 3f / 6, 4f / 6, 5f / 6, 1f },
                    };
                    br.InterpolationColors = blend;
                }
                g.FillRectangle(br, t);
            }

            // 2) 出ない範囲を暗く覆う
            var (lx, hx, noLo, noHi) = KnobXs();
            using (var shade = new SolidBrush(Color.FromArgb(170, 30, 30, 40)))
            {
                if (lx > t.Left) g.FillRectangle(shade, t.Left, t.Top, lx - t.Left, t.Height);
                if (hx < t.Right) g.FillRectangle(shade, hx, t.Top, t.Right - hx, t.Height);
            }
            // 出る範囲の縁
            using (var pen = new Pen(Color.FromArgb(255, 255, 255), 2f)) g.DrawRectangle(pen, lx, t.Top, Math.Max(1, hx - lx), t.Height);
            g.DrawRectangle(Pens.Gray, t.X, t.Y, t.Width, t.Height);

            // 3) つまみ（▲）。「なし」の側は白抜き
            DrawKnob(g, lx, t.Bottom, hollow: noLo);
            DrawKnob(g, hx, t.Bottom, hollow: noHi);

            // 4) 端と中央の目盛りの数字
            string F(float v) => v.ToString(info.Unit == "倍" ? "0.0" : "0") + info.Unit;
            using var small = new Font(UiTheme.Small.FontFamily, 7.5f);
            float mid = (info.ViewMin + info.ViewMax) / 2f;
            g.DrawString(F(info.ViewMin), small, Brushes.DimGray, t.Left - 8, t.Bottom + 14);
            var ms = g.MeasureString(F(mid), small);
            g.DrawString(F(mid), small, Brushes.DimGray, (t.Left + t.Right) / 2 - ms.Width / 2, t.Bottom + 14);
            var me = g.MeasureString(F(info.ViewMax), small);
            g.DrawString(F(info.ViewMax), small, Brushes.DimGray, t.Right - me.Width + 8, t.Bottom + 14);
        }

        private static void DrawKnob(Graphics g, float x, float top, bool hollow)
        {
            var pts = new[] { new PointF(x, top), new PointF(x - 7, top + 12), new PointF(x + 7, top + 12) };
            if (hollow) { g.FillPolygon(Brushes.White, pts); g.DrawPolygon(Pens.Gray, pts); }
            else { g.FillPolygon(Brushes.Black, pts); g.DrawPolygon(Pens.White, pts); }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            var (lx, hx, _, _) = KnobXs();
            // 近いほうのつまみをつかむ（重なっているときは、押した位置が右寄りなら上限）
            _drag = Math.Abs(e.X - lx) < Math.Abs(e.X - hx) ? 0 : (Math.Abs(e.X - lx) == Math.Abs(e.X - hx) && e.X < lx ? 0 : 1);
            Move(e.X);
        }
        protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if (_drag >= 0) Move(e.X); }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); _drag = -1; }

        private void Move(float x)
        {
            float v = XToView(x);
            if (_drag == 0) _owner.SetLowerFromView(v);
            else if (_drag == 1) _owner.SetUpperFromView(v);
        }
    }
}
