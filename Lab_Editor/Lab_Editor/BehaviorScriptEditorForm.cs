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
        var palette = new BlockPaletteControl { Dock = DockStyle.Fill, Width = 260 };
        var pnlPalette = new Panel { Dock = DockStyle.Left, Width = 260 };
        pnlPalette.Controls.Add(palette);
        pnlPalette.Controls.Add(lblPaletteTitle);

        var lblCanvasTitle = new Label { Dock = DockStyle.Top, Height = 22, Text = "🖼 キャンバス", Font = new Font(Font, FontStyle.Bold), Padding = new Padding(6, 4, 0, 0) };
        _canvas = new BlockCanvasControl { Dock = DockStyle.Fill };
        var pnlCanvas = new Panel { Dock = DockStyle.Fill };
        pnlCanvas.Controls.Add(_canvas);
        pnlCanvas.Controls.Add(lblCanvasTitle);

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
        var btnOk = new Button { Text = "💾 OK", DialogResult = DialogResult.OK, AutoSize = true, Padding = new Padding(10, 5, 10, 5), BackColor = Color.FromArgb(40, 167, 69), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
        btnOk.Click += (s, e) => { ResultScript = BlockScriptSerializer.Serialize(_canvas.TopLevel); };
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
}
