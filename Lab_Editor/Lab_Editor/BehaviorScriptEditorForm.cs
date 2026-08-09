using Newtonsoft.Json.Linq;

namespace Lab_Editor;

// ======================================================
// BehaviorScriptEditorForm - Scratch風ブロックエディタ
// Feature: Puzzle-like Behavior Scripting (M4/M5/M6)
//
// M6で特定の敵/ギミックのscriptフィールドを読み込み・編集・書き戻しできるようにした。
// 編集対象を指定せずに開いた場合はレイアウト確認用のデモツリーを表示する（従来のプレビュー動作）。
// ==================
// ドラッグ&ドロップでの組み立て（M5）・JSON保存/読み込み（M6）に対応済み。
// 未対応: レポーター/真偽値ブロックをソケットへ直接ドラッグして差し込む操作（現状は括弧書きテキスト表示のみ）。
// ======================================================
public class BehaviorScriptEditorForm : Form
{
    private readonly BlockCanvasControl _canvas;
    private readonly bool _isEditingSpecificScript;

    // OKで閉じたときに呼び出し側が読み取る、シリアライズ済みのJSON AST
    public JArray ResultScript { get; private set; } = new JArray();

    // 編集対象を指定しない場合（プレビュー用途）
    public BehaviorScriptEditorForm() : this(null, null) { }

    // subjectLabel: 「敵: enemy_script_patrol」のような編集対象の表示名。nullならプレビューモード（デモツリー）。
    // initialScript: 既存のscript(JSON AST)。null/空なら空のキャンバスから開始する。
    public BehaviorScriptEditorForm(string? subjectLabel, JArray? initialScript)
    {
        _isEditingSpecificScript = subjectLabel != null;

        Text = _isEditingSpecificScript
            ? $"🧩 挙動スクリプトエディタ - {subjectLabel}"
            : "🧩 挙動スクリプトエディタ（プレビュー版）";
        Size = new Size(1000, 650);
        MinimumSize = new Size(700, 450);
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Meiryo UI", 9);

        var lblNotice = new Label
        {
            Dock = DockStyle.Top,
            Height = 34,
            Text = _isEditingSpecificScript
                ? "パレットからブロックをキャンバスへドラッグして組み立てます。キャンバス内のブロックはドラッグで並べ替え・ネストでき、選択してDeleteキーで削除できます。"
                : "⚠ プレビュー版：編集対象未指定のため、レイアウト確認用のデモツリーを表示しています。",
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(8, 0, 0, 0),
            BackColor = Color.FromArgb(255, 244, 214),
            ForeColor = Color.FromArgb(120, 80, 0),
        };

        var lblPaletteTitle = new Label { Dock = DockStyle.Top, Height = 22, Text = "📦 ブロックパレット（ドラッグしてキャンバスへ）", Font = new Font(Font, FontStyle.Bold), Padding = new Padding(6, 4, 0, 0) };
        // Feature: UI改善（提案書 CUT-6）— 約55種のブロックを名前で絞り込める検索欄
        var txtPaletteSearch = new TextBox { Dock = DockStyle.Top, PlaceholderText = "🔍 ブロックを検索..." };
        var palette = new BlockPaletteControl { Dock = DockStyle.Fill, Width = 260 };
        txtPaletteSearch.TextChanged += (s, e) => palette.SetFilter(txtPaletteSearch.Text);
        var pnlPalette = new Panel { Dock = DockStyle.Left, Width = 260 };
        pnlPalette.Controls.Add(palette);
        pnlPalette.Controls.Add(lblPaletteTitle);
        pnlPalette.Controls.Add(txtPaletteSearch);

        var lblCanvasTitle = new Label { Dock = DockStyle.Top, Height = 22, Text = "🖼 キャンバス（Ctrl+ホイールでズーム）", Font = new Font(Font, FontStyle.Bold), Padding = new Padding(6, 4, 0, 0) };
        _canvas = new BlockCanvasControl { Dock = DockStyle.Fill };

        // Feature: UI改善（提案書 BS-3）— 空のキャンバスとブロックパレットだけでは何から組み立てればよいか
        // 分かりにくいため、よくある動きの完成形を選んで読み込めるテンプレートピッカーを追加する。
        var pnlTemplateBar = new Panel { Dock = DockStyle.Top, Height = 30 };
        var flowTemplateBar = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(4, 2, 4, 2) };
        var btnTemplate = new Button { Text = "📋 テンプレートから開始...", AutoSize = true, Padding = new Padding(8, 2, 8, 2), BackColor = Color.FromArgb(255, 244, 214) };
        btnTemplate.Click += (s, e) => OpenTemplatePicker();
        flowTemplateBar.Controls.Add(btnTemplate);
        pnlTemplateBar.Controls.Add(flowTemplateBar);

        var pnlCanvas = new Panel { Dock = DockStyle.Fill };
        pnlCanvas.Controls.Add(_canvas);
        pnlCanvas.Controls.Add(lblCanvasTitle);
        pnlCanvas.Controls.Add(pnlTemplateBar);

        if (_isEditingSpecificScript)
        {
            var loaded = BlockScriptSerializer.Deserialize(initialScript);
            _canvas.LoadProgram(loaded);
        }

        // Feature: UI改善 — 固定座標(Location)のボタンはウィンドウを縮小すると画面外にはみ出してしまうため、
        // 他フォームと同様のDock=Fill+RightToLeftのFlowLayoutPanelへ変更し、幅に追従するようにする。
        var pnlBottom = new Panel { Dock = DockStyle.Bottom, Height = 42 };
        var flowBottom = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) };
        var btnCancel = new Button { Text = "キャンセル", DialogResult = DialogResult.Cancel, AutoSize = true, Padding = new Padding(10, 5, 10, 5) };
        // Feature: UI改善（提案書 BS-4）— 保存前に「実行すると何もしない/意図しない既定動作になる」パターンを
        // 検知して警告する。DialogResultはボタンに持たせず、検証を通ってから手動でCloseする。
        var btnOk = new Button { Text = "💾 OK", AutoSize = true, Padding = new Padding(10, 5, 10, 5), BackColor = Color.FromArgb(40, 167, 69), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
        btnOk.Click += (s, e) =>
        {
            var warnings = ValidateScript();
            if (warnings.Count > 0)
            {
                string msg = "保存前に確認してください:\n\n" +
                    string.Join("\n", warnings.Take(8)) +
                    (warnings.Count > 8 ? $"\n…他{warnings.Count - 8}件" : "") +
                    "\n\nこのまま保存しますか？";
                if (MessageBox.Show(msg, "確認", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            }
            ResultScript = BlockScriptSerializer.Serialize(_canvas.TopLevel);
            DialogResult = DialogResult.OK;
            Close();
        };
        // RightToLeftは追加順が右から並ぶため、OKを一番右にしたい場合は先にCancelを追加する
        flowBottom.Controls.Add(btnCancel);
        flowBottom.Controls.Add(btnOk);
        pnlBottom.Controls.Add(flowBottom);
        AcceptButton = btnOk;
        CancelButton = btnCancel;

        Controls.Add(pnlCanvas);
        Controls.Add(pnlPalette);
        Controls.Add(pnlBottom);
        Controls.Add(lblNotice);
    }

    // Feature: UI改善（提案書 BS-4）— C型ブロックの中身が空、または条件ソケットが未設定のまま保存すると
    // 「実行しても何も起きない」「常に既定の動作になる」状態になり、ゲーム内デバッグ表示でしか気づけない。
    // 保存前にツリー全体を辿ってこれらを検出し、具体的な箇所を教える。
    private List<string> ValidateScript()
    {
        var warnings = new List<string>();

        void Walk(List<BlockInstance> seq, string context)
        {
            foreach (var b in seq)
            {
                if (b.Def.HasBody && b.Body.Count == 0)
                    warnings.Add($"「{b.Def.DisplayName}」の中身が空です（{context}の中）。何も実行されません。");
                if (b.Def.HasElse && b.Else.Count == 0 && b.Body.Count > 0)
                    warnings.Add($"「{b.Def.DisplayName}」の「でなければ」側が空です（{context}の中）。");

                foreach (var arg in b.Def.Args)
                {
                    if (arg.Type == BlockArgType.BoolSlot && !b.ArgBlocks.ContainsKey(arg.Name))
                        warnings.Add($"「{b.Def.DisplayName}」の条件（{arg.Label}）が未設定です（{context}の中）。既定の動作になります。");
                }

                if (b.Def.HasBody) Walk(b.Body, b.Def.DisplayName);
                if (b.Def.HasElse) Walk(b.Else, b.Def.DisplayName);
            }
        }

        foreach (var hat in _canvas.TopLevel)
        {
            if (hat.Def.HasBody && hat.Body.Count == 0)
                warnings.Add($"「{hat.Def.DisplayName}」の中身が空です。何も実行されません。");
            else if (hat.Def.HasBody)
                Walk(hat.Body, hat.Def.DisplayName);
        }
        return warnings;
    }

    private void OpenTemplatePicker()
    {
        if (_canvas.TopLevel.Count > 0)
        {
            if (MessageBox.Show("テンプレートを読み込むと、現在キャンバスにある内容は失われます。よろしいですか？", "確認",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        }
        using var picker = new TemplatePickerForm();
        if (picker.ShowDialog() != DialogResult.OK || picker.SelectedTemplate == null) return;
        _canvas.LoadProgram(BlockScriptSerializer.Deserialize(picker.SelectedTemplate.Build()));
    }
}
