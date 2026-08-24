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
    // テンプレート1件分の情報を表すクラス。
    // Name        : テンプレート選択リストに表示される名前（絵文字付きの短いラベル）
    // Description : そのテンプレートがどんな動きになるかを説明する文章（選択時に詳細欄へ表示される）
    // Build       : 実際にこのテンプレートのブロック構成をJSON AST（JArray）として組み立てて返す関数。
    //               呼び出すたびに新しいJArrayを生成するため、同じテンプレートを複数回読み込んでも
    //               参照を共有して壊し合うことがない。
    public class TemplateInfo
    {
        public string Name = "";
        public string Description = "";
        public Func<JArray> Build = () => new JArray();
    }

    // 用意されている全テンプレートの一覧。TemplatePickerFormのリストにそのまま表示される。
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

    // 「hatブロック（開始点）+ その中身(body)」という、BlockScriptSerializerが読み込める最上位のJSON AST片を
    // 1つ組み立てる共通ヘルパー。hatName（例："OnSpawn"）と、その中で実行するブロック列(body)を受け取る。
    private static JArray Hat(string hatName, JArray body) => new JArray { new JObject { ["hat"] = hatName, ["body"] = body } };

    // 「⬅➡ 左右パトロール」テンプレート：出現時(OnSpawn)から、右へ2の速さで1秒(60フレーム)進んで止まり、
    // 続けて左へ同じ速さで1秒進んで止まる…という往復動作をForeverの中で無限に繰り返す。
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

    // 「🏃 近づいたら追いかける」テンプレート：Forever内で毎フレーム、プレイヤーとの距離(DistanceToPlayer)が
    // 200px未満かどうかを判定(IfElse)し、近ければプレイヤー方向(Toward)へ速さ2で移動、
    // 遠ければ何もせず1フレームだけ待機する（＝待機状態）を繰り返す。
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

    // 「🎯 一定間隔で狙撃」テンプレート：Forever内で、プレイヤーへ自動照準で弾（速さ6・威力1）を1発撃ち、
    // その後90フレーム（1.5秒）待ってから再び撃つ、を繰り返す。
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

    // 「🌊 上下に振動する床」テンプレート：ForeverでOscillateブロックを1つだけ実行し続ける。
    // Y座標を200(min)〜300(max)の間で90フレーム(1.5秒)周期に往復させる。min/maxは絶対座標なので、
    // このテンプレートを実際に使う際は配置後の実際の座標に合わせて数値を調整する必要がある。
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

    // 「🌀 その場で回転し続ける」テンプレート：Forever内で毎フレーム、経過フレーム数(Time)に0.05を
    // 掛けた値を回転角(SetAngle)として設定し続けることで、時間経過に比例してゆっくり回転させる。
    // 1フレームごとにWaitで待つことで、Foreverが無限ループとして暴走せず1フレームずつ進む。
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

    // 「💢 被弾時に一瞬無敵＋点滅」テンプレート：OnDamaged（ダメージを受けた瞬間）をきっかけに、
    // 無敵状態をONにしてから明るさ演出(brightness, 強度1.5)をかけ、30フレーム(0.5秒)待った後、
    // 無敵状態をOFFに戻す。連続ヒットを防ぎつつ被弾したことを視覚的に分かりやすくする定番パターン。
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
//
// BehaviorScriptTemplates.Allの一覧をリスト表示し、選択した項目の説明文を下に表示するだけの
// シンプルなモーダルダイアログ。OKが押された時点のSelectedTemplateを呼び出し元が読み取り、
// TemplateInfo.Build()を実行してキャンバスへ読み込む、という流れで使われる。
// ======================================================
public class TemplatePickerForm : Form
{
    private ListBox _list = null!;   // テンプレート名の一覧を表示するリストボックス
    private Label _lblDesc = null!;  // 選択中テンプレートの説明文を表示するラベル

    // OKボタンが押された時点で選択されていたテンプレート情報。未選択のままOKされた場合はnullのまま。
    public BehaviorScriptTemplates.TemplateInfo? SelectedTemplate { get; private set; }

    // コンストラクタ：UI一式を構築し、テンプレート一覧をリストボックスへ流し込む。
    public TemplatePickerForm()
    {
        Text = "📋 テンプレートから開始";
        Size = new Size(520, 420);
        MinimumSize = new Size(420, 320); // ウィンドウを小さくしすぎてUIが崩れないよう最小サイズを設定
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Meiryo UI", 9);

        // 上部の説明文（このダイアログが何をするものかの案内）
        var lblHint = new Label
        {
            Dock = DockStyle.Top,
            Height = 40,
            Padding = new Padding(8),
            Text = "よくある動きの完成形を選ぶと、キャンバスの内容を置き換えて読み込みます（現在の内容は失われます）。",
            Font = new Font(Font.FontFamily, 8f),
            ForeColor = Color.DarkSlateGray,
        };

        // テンプレート名の一覧を表示するリストボックス。BehaviorScriptTemplates.Allの各Nameを項目として追加する。
        _list = new ListBox { Dock = DockStyle.Fill, Font = new Font("Meiryo UI", 10f), IntegralHeight = false };
        foreach (var t in BehaviorScriptTemplates.All) _list.Items.Add(t.Name);
        // 選択項目が変わるたびに、対応するテンプレートの説明文(Description)を下部ラベルへ反映する
        _list.SelectedIndexChanged += (s, e) =>
        {
            int idx = _list.SelectedIndex;
            _lblDesc.Text = idx >= 0 ? BehaviorScriptTemplates.All[idx].Description : "";
        };

        // 選択中テンプレートの説明文を表示するラベル（初期状態は空）
        _lblDesc = new Label { Dock = DockStyle.Top, Height = 60, Padding = new Padding(8), ForeColor = Color.DimGray, Text = "" };

        // 下部のOK/キャンセルボタン領域。RightToLeftのFlowLayoutPanelで右詰めに配置する。
        var pnlBottom = new Panel { Dock = DockStyle.Bottom, Height = 46 };
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) };
        var btnCancel = new Button { Text = "キャンセル", DialogResult = DialogResult.Cancel, AutoSize = true, Padding = new Padding(10, 5, 10, 5) };
        var btnOk = new Button { Text = "このテンプレートを読み込む", DialogResult = DialogResult.OK, AutoSize = true, Padding = new Padding(10, 5, 10, 5), BackColor = Color.FromArgb(40, 167, 69), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
        // OKが押された時点でリストの選択項目があれば、それをSelectedTemplateとして確定する
        btnOk.Click += (s, e) =>
        {
            if (_list.SelectedIndex >= 0) SelectedTemplate = BehaviorScriptTemplates.All[_list.SelectedIndex];
        };
        // RightToLeftは追加順が右から並ぶため、OKボタンを一番右に見せたい場合は先にCancelを追加する
        flow.Controls.Add(btnCancel);
        flow.Controls.Add(btnOk);
        pnlBottom.Controls.Add(flow);
        AcceptButton = btnOk;     // Enterキーで確定できるようにする
        CancelButton = btnCancel; // Escキーでキャンセルできるようにする

        Controls.Add(_list);
        Controls.Add(pnlBottom);
        Controls.Add(lblHint);
        Controls.Add(_lblDesc);

        // 初期状態で先頭のテンプレートを選択しておくことで、開いた瞬間から説明文が表示された状態にする
        if (_list.Items.Count > 0) _list.SelectedIndex = 0;
    }
}
