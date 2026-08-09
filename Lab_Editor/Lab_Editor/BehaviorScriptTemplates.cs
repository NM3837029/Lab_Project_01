using Newtonsoft.Json.Linq;

namespace Lab_Editor;

// ======================================================
// BehaviorScriptTemplates - 挙動スクリプトエディタの「テンプレートから開始」用の定型パターン集
// Feature: UI改善（提案書 BS-3）
//
// プログラミング未経験者が空のキャンバスとブロックパレットだけを渡されても
// 何から組み立てればよいか分からない、という問題に対応するため、よくある動きの完成形を
// あらかじめ用意し、選ぶだけでキャンバスに読み込めるようにする。
// 生成するJSONの形はBlockScriptSerializerが読み書きする形式と完全に一致させ、
// BlockScriptSerializer.Deserializeにそのまま渡してBlockInstanceツリー化する。
// ======================================================
public static class BehaviorScriptTemplates
{
    public class TemplateInfo
    {
        public string Name = "";
        public string Description = "";
        public Func<JArray> Build = () => new JArray();
    }

    public static readonly List<TemplateInfo> All = new()
    {
        new TemplateInfo
        {
            Name = "⬅➡ 左右パトロール",
            Description = "右へ1秒進んで止まり、左へ1秒進んで止まる…を繰り返します。巡回する敵の基本形です。",
            Build = BuildPatrol,
        },
        new TemplateInfo
        {
            Name = "🏃 近づいたら追いかける",
            Description = "プレイヤーが200px以内に近づいたらその方向へ移動し、離れたら待機します。",
            Build = BuildChase,
        },
        new TemplateInfo
        {
            Name = "🎯 一定間隔で狙撃",
            Description = "1.5秒おきに、プレイヤーへ自動照準で弾を1発撃ちます。",
            Build = BuildShootAtPlayer,
        },
        new TemplateInfo
        {
            Name = "🌊 上下に振動する床",
            Description = "Y座標200〜300の間を1.5秒周期で往復します（min/maxは配置後の実際の座標に合わせて調整してください）。",
            Build = BuildOscillatingFloor,
        },
        new TemplateInfo
        {
            Name = "🌀 その場で回転し続ける",
            Description = "見た目をゆっくり回転させ続けます（当たり判定の形そのものは変わりません）。",
            Build = BuildSelfRotate,
        },
        new TemplateInfo
        {
            Name = "💢 被弾時に一瞬無敵＋点滅",
            Description = "ダメージを受けた瞬間に0.5秒だけ無敵になり、明るく点滅して被弾したことを分かりやすくします。",
            Build = BuildDamagedFlash,
        },
    };

    private static JArray Hat(string hatName, JArray body) => new JArray { new JObject { ["hat"] = hatName, ["body"] = body } };

    private static JArray BuildPatrol() => Hat("OnSpawn", new JArray
    {
        new JObject
        {
            ["op"] = "Forever",
            ["body"] = new JArray
            {
                new JObject { ["op"] = "MoveDirection", ["dir"] = "Right", ["speed"] = 2 },
                new JObject { ["op"] = "Wait", ["frames"] = 60 },
                new JObject { ["op"] = "MoveDirection", ["dir"] = "Left", ["speed"] = 2 },
                new JObject { ["op"] = "Wait", ["frames"] = 60 },
            }
        }
    });

    private static JArray BuildChase() => Hat("OnSpawn", new JArray
    {
        new JObject
        {
            ["op"] = "Forever",
            ["body"] = new JArray
            {
                new JObject
                {
                    ["op"] = "IfElse",
                    ["cond"] = new JObject { ["op"] = "Lt", ["a"] = new JObject { ["op"] = "DistanceToPlayer" }, ["b"] = 200 },
                    ["body"] = new JArray { new JObject { ["op"] = "MoveDirection", ["dir"] = "Toward", ["speed"] = 2 } },
                    ["else"] = new JArray { new JObject { ["op"] = "Wait", ["frames"] = 1 } },
                }
            }
        }
    });

    private static JArray BuildShootAtPlayer() => Hat("OnSpawn", new JArray
    {
        new JObject
        {
            ["op"] = "Forever",
            ["body"] = new JArray
            {
                new JObject { ["op"] = "ShootAtPlayer", ["speed"] = 6, ["damage"] = 1 },
                new JObject { ["op"] = "Wait", ["frames"] = 90 },
            }
        }
    });

    private static JArray BuildOscillatingFloor() => Hat("OnSpawn", new JArray
    {
        new JObject
        {
            ["op"] = "Forever",
            ["body"] = new JArray
            {
                new JObject { ["op"] = "Oscillate", ["min"] = 200, ["max"] = 300, ["periodFrames"] = 90 },
            }
        }
    });

    private static JArray BuildSelfRotate() => Hat("OnSpawn", new JArray
    {
        new JObject
        {
            ["op"] = "Forever",
            ["body"] = new JArray
            {
                new JObject { ["op"] = "SetAngle", ["angle"] = new JObject { ["op"] = "Mul", ["a"] = new JObject { ["op"] = "Time" }, ["b"] = 0.05 } },
                new JObject { ["op"] = "Wait", ["frames"] = 1 },
            }
        }
    });

    private static JArray BuildDamagedFlash() => Hat("OnDamaged", new JArray
    {
        new JObject { ["op"] = "SetInvincible", ["on"] = "true" },
        new JObject { ["op"] = "SetVisualEffect", ["kind"] = "brightness", ["intensity"] = 1.5 },
        new JObject { ["op"] = "Wait", ["frames"] = 30 },
        new JObject { ["op"] = "SetInvincible", ["on"] = "false" },
    });
}

// ======================================================
// TemplatePickerForm - テンプレート選択ダイアログ
// Feature: UI改善（提案書 BS-3）
// ======================================================
public class TemplatePickerForm : Form
{
    private ListBox _list = null!;
    private Label _lblDesc = null!;

    public BehaviorScriptTemplates.TemplateInfo? SelectedTemplate { get; private set; }

    public TemplatePickerForm()
    {
        Text = "📋 テンプレートから開始";
        Size = new Size(520, 420);
        MinimumSize = new Size(420, 320);
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Meiryo UI", 9);

        var lblHint = new Label
        {
            Dock = DockStyle.Top,
            Height = 40,
            Padding = new Padding(8),
            Text = "よくある動きの完成形を選ぶと、キャンバスの内容を置き換えて読み込みます（現在の内容は失われます）。",
            Font = new Font(Font.FontFamily, 8f),
            ForeColor = Color.DarkSlateGray,
        };

        _list = new ListBox { Dock = DockStyle.Fill, Font = new Font("Meiryo UI", 10f), IntegralHeight = false };
        foreach (var t in BehaviorScriptTemplates.All) _list.Items.Add(t.Name);
        _list.SelectedIndexChanged += (s, e) =>
        {
            int idx = _list.SelectedIndex;
            _lblDesc.Text = idx >= 0 ? BehaviorScriptTemplates.All[idx].Description : "";
        };

        _lblDesc = new Label { Dock = DockStyle.Top, Height = 60, Padding = new Padding(8), ForeColor = Color.DimGray, Text = "" };

        var pnlBottom = new Panel { Dock = DockStyle.Bottom, Height = 46 };
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) };
        var btnCancel = new Button { Text = "キャンセル", DialogResult = DialogResult.Cancel, AutoSize = true, Padding = new Padding(10, 5, 10, 5) };
        var btnOk = new Button { Text = "このテンプレートを読み込む", DialogResult = DialogResult.OK, AutoSize = true, Padding = new Padding(10, 5, 10, 5), BackColor = Color.FromArgb(40, 167, 69), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
        btnOk.Click += (s, e) =>
        {
            if (_list.SelectedIndex >= 0) SelectedTemplate = BehaviorScriptTemplates.All[_list.SelectedIndex];
        };
        flow.Controls.Add(btnCancel);
        flow.Controls.Add(btnOk);
        pnlBottom.Controls.Add(flow);
        AcceptButton = btnOk;
        CancelButton = btnCancel;

        Controls.Add(_list);
        Controls.Add(pnlBottom);
        Controls.Add(lblHint);
        Controls.Add(_lblDesc);

        if (_list.Items.Count > 0) _list.SelectedIndex = 0;
    }
}
