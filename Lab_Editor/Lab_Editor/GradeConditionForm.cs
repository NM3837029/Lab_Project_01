namespace Lab_Editor;

// ======================================================
// GradeConditionForm - ステージに置いた物の「出る条件…」ダイアログ
//
// ステージ画面で置いた物を右クリック →「出る条件…」で開く。中身は共通の条件エディタ（GradeConditionEditor）。
//   ・色ロック足場(22) … 赤・緑・青を選ぶ → 置いた物の param に "1"/"2"/"3" を書く
//   ・明暗ロック足場(23) … 暗い・明るいを選ぶ → param に "dark"/"bright" を書く
//   ・補正スイッチ(26)・補正ロック足場(27) … 「アセットの設定に従う」を外すと、この1つだけ条件を上書きできる
//                                           （上書きは置いた物の gradeChannel・gradeMin・gradeMax）
// 以前は「パラメータ」欄に "1" や "dark" を手で打つしかなく、打ち間違えても気づけなかった。
// ======================================================
public sealed class GradeConditionForm : Form
{
    public GradeConditionEditor Editor { get; } = new();
    private readonly CheckBox _chkFollow = new() { Text = "アセットの設定に従う（この1つだけ変えるときは外す）", AutoSize = true, Font = UiTheme.Base, Margin = new Padding(8, 8, 8, 4) };
    private readonly int _typeEnum;
    private readonly int _assetCh;
    private readonly float _assetLo, _assetHi;

    // 「アセットの設定に従う」か（26・27 だけ意味がある）
    public bool FollowAsset { get => _chkFollow.Checked; set => _chkFollow.Checked = value; }

    // title … 見出しに出す名前（アセットの名前）
    // typeEnum … ギミックのタイプ（22・23・26・27）
    // pg … 置いた物（今の param・上書きを読む）
    // assetCh/assetLo/assetHi … アセットの条件（「従う」のときに見せる値）
    public GradeConditionForm(string title, int typeEnum, PlacedGimmick pg, int assetCh, float assetLo, float assetHi)
    {
        _typeEnum = typeEnum;
        _assetCh = assetCh; _assetLo = assetLo; _assetHi = assetHi;
        Text = $"出る条件 — {title}";
        Font = UiTheme.Base;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;

        bool range = typeEnum == 26 || typeEnum == 27;
        ClientSize = new Size(440, range ? 420 : 200);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(6) };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var hint = new Label
        {
            AutoSize = true, MaximumSize = new Size(420, 0), ForeColor = Color.DimGray, Margin = new Padding(8, 6, 8, 2),
            Text = typeEnum switch
            {
                22 => "この足場が現れる色フィルタを選びます（カラー補正パネルの色の輪で選ぶ色）。",
                23 => "この足場が現れる明るさを選びます（暗い＝明るさ 60% 未満、明るい＝130% より上）。",
                26 => "このスイッチが入る条件です。同じ param のゲート扉が開きます。",
                _ => "この足場が現れる条件です。",
            },
        };
        layout.Controls.Add(hint, 0, 0);

        // 26・27：アセットの条件に従うか、この1つだけ上書きするか
        _chkFollow.Visible = range;
        layout.Controls.Add(_chkFollow, 0, 1);

        Editor.Dock = DockStyle.Fill;
        switch (typeEnum)
        {
            case 22: Editor.Kind = GradeKind.ColorLock; Editor.SetColor(GradeCondition.ColorFromParam(pg.Param)); break;
            case 23: Editor.Kind = GradeKind.BrightLock; Editor.SetBright(GradeCondition.BrightFromParam(pg.Param)); break;
            default:
                Editor.Kind = GradeKind.Range;
                if (pg.HasGradeOverride)
                {
                    _chkFollow.Checked = false;
                    Editor.SetRange(GradeCondition.ChannelOf(pg.GradeChannelOv!.Value), pg.GradeMinOv!.Value, pg.GradeMaxOv!.Value);
                }
                else
                {
                    _chkFollow.Checked = true;
                    Editor.SetRange(assetCh, assetLo, assetHi);
                }
                break;
        }
        layout.Controls.Add(Editor, 0, 2);
        // 「従う」の間は、アセットの条件を見せるだけで触れない。外すと、そこから変えられる
        Editor.Enabled = !(range && _chkFollow.Checked);
        _chkFollow.CheckedChanged += (s, e) =>
        {
            Editor.Enabled = !_chkFollow.Checked;
            if (_chkFollow.Checked) Editor.SetRange(_assetCh, _assetLo, _assetHi); // 戻したら、アセットの条件を見せ直す
        };

        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Fill, Padding = new Padding(4) };
        var ok = new Button { Text = "決定", DialogResult = DialogResult.OK, Width = 90, Height = 30 };
        var cancel = new Button { Text = "キャンセル", DialogResult = DialogResult.Cancel, Width = 90, Height = 30 };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);
        layout.Controls.Add(buttons, 0, 3);
        AcceptButton = ok; CancelButton = cancel;
        Controls.Add(layout);
    }

    // ダイアログの結果を置いた物へ書く（決定を押したあとに呼ぶ）
    public void ApplyTo(PlacedGimmick pg)
    {
        switch (_typeEnum)
        {
            case 22: pg.Param = GradeCondition.ParamFromColor(Editor.ColorValue); break;
            case 23: pg.Param = GradeCondition.ParamFromBright(Editor.BrightValue); break;
            default:
                if (FollowAsset) { pg.GradeChannelOv = null; pg.GradeMinOv = null; pg.GradeMaxOv = null; }
                else { pg.GradeChannelOv = Editor.ChannelValue; pg.GradeMinOv = Editor.Lo; pg.GradeMaxOv = Editor.Hi; }
                break;
        }
    }
}
