using Newtonsoft.Json.Linq;

namespace Lab_Editor;

// ======================================================
// BehaviorScriptEditorPageControl - Scratch風ブロックエディタ
// Feature: Puzzle-like Behavior Scripting (M4/M5/M6)
// 構造改修フェーズ5dでForm(BehaviorScriptEditorForm)からUserControlへ抽出。
//
// M6で特定の敵/ギミックのscriptフィールドを読み込み・編集・書き戻しできるようにした。
// 編集対象を指定せずに開いた場合はレイアウト確認用のデモツリーを表示する（従来のプレビュー動作）。
// ==================
// ドラッグ&ドロップでの組み立て（M5）・JSON保存/読み込み（M6）に対応済み。
// 未対応: レポーター/真偽値ブロックをソケットへ直接ドラッグして差し込む操作（現状は括弧書きテキスト表示のみ）。
// ======================================================
public class BehaviorScriptEditorPageControl : UserControl
{
    // 保存（OKボタン押下かつ警告確認済み）が確定した際に発火するイベント。引数はシリアライズ後のJSON AST。
    public event EventHandler<JArray>? Saved;
    // キャンセルボタンが押された際に発火するイベント。
    public event EventHandler? Cancelled;
    // 呼び出し元（ホストフォーム側）が、OK/キャンセルボタンを外部のボタン領域と差し替えたり、
    // 有効/無効を制御したりできるように、内部ボタンへの参照を公開しているプロパティ。
    public Button PrimaryActionButton => _btnOk;
    public Button SecondaryActionButton => _btnCancel;
    private Button _btnOk = null!, _btnCancel = null!;

    private readonly BlockCanvasControl _canvas; // ブロックを実際に配置・編集するキャンバス本体
    private readonly bool _isEditingSpecificScript; // 特定のscriptを編集中か、デモ表示のプレビューモードかのフラグ

    // Saved発火時に渡す、シリアライズ済みのJSON AST
    public JArray ResultScript { get; private set; } = new JArray();

    // 編集対象を指定しない場合（プレビュー用途）
    public BehaviorScriptEditorPageControl() : this(null, null) { }

    // subjectLabel: 「敵: enemy_script_patrol」のような編集対象の表示名。nullならプレビューモード（デモツリー）。
    // initialScript: 既存のscript(JSON AST)。null/空なら空のキャンバスから開始する。
    public BehaviorScriptEditorPageControl(string? subjectLabel, JArray? initialScript)
    {
        // subjectLabelが指定されていれば「特定のscriptを編集するモード」、nullなら「プレビュー専用モード」
        _isEditingSpecificScript = subjectLabel != null;

        Dock = DockStyle.Fill;
        Font = UiTheme.Base;

        // 画面上部に表示する案内文。編集モードでは操作方法（ドラッグ組み立て・並べ替え・削除）を説明し、
        // プレビューモードでは「デモ表示中である」ことを警告色（オレンジ系）で目立たせる。
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

        // 左側：ブロックパレット領域のタイトルラベル
        var lblPaletteTitle = new Label { Dock = DockStyle.Top, Height = 22, Text = "📦 ブロックパレット（ドラッグしてキャンバスへ）", Font = new Font(Font, FontStyle.Bold), Padding = new Padding(6, 4, 0, 0) };
        // Feature: UI改善（提案書 CUT-6）— 約55種のブロックを名前で絞り込める検索欄
        var txtPaletteSearch = new TextBox { Dock = DockStyle.Top, PlaceholderText = "🔍 ブロックを検索..." };
        var palette = new BlockPaletteControl { Dock = DockStyle.Fill, Width = 260 };
        // 検索欄の入力が変わるたびにパレット側のフィルタへ反映し、即座に絞り込み結果を再描画させる
        txtPaletteSearch.TextChanged += (s, e) => palette.SetFilter(txtPaletteSearch.Text);
        var pnlPalette = new Panel { Dock = DockStyle.Left, Width = 260 };
        pnlPalette.Controls.Add(palette);
        pnlPalette.Controls.Add(lblPaletteTitle);
        pnlPalette.Controls.Add(txtPaletteSearch);

        // 右側：キャンバス（実際にブロックを組み立てる領域）のタイトルラベルと本体
        var lblCanvasTitle = new Label { Dock = DockStyle.Top, Height = 22, Text = "🖼 キャンバス（Ctrl+ホイールでズーム）", Font = new Font(Font, FontStyle.Bold), Padding = new Padding(6, 4, 0, 0) };
        _canvas = new BlockCanvasControl { Dock = DockStyle.Fill };

        // Feature: UI改善（提案書 BS-3）— 空のキャンバスとブロックパレットだけでは何から組み立てればよいか
        // 分かりにくいため、よくある動きの完成形を選んで読み込めるテンプレートピッカーを追加する。
        var pnlTemplateBar = new Panel { Dock = DockStyle.Top, Height = 30 };
        var flowTemplateBar = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(4, 2, 4, 2) };
        var btnTemplate = new Button { Text = "📋 テンプレートから開始...", AutoSize = true, Padding = new Padding(8, 2, 8, 2), BackColor = Color.FromArgb(255, 244, 214) };
        btnTemplate.Click += (s, e) => OpenTemplatePicker(); // テンプレート選択ダイアログを開く
        flowTemplateBar.Controls.Add(btnTemplate);
        pnlTemplateBar.Controls.Add(flowTemplateBar);

        var pnlCanvas = new Panel { Dock = DockStyle.Fill };
        pnlCanvas.Controls.Add(_canvas);
        pnlCanvas.Controls.Add(lblCanvasTitle);
        pnlCanvas.Controls.Add(pnlTemplateBar);

        // 特定のscriptを編集するモードの場合のみ、渡された既存のJSON ASTをBlockInstanceツリーへ
        // 変換してキャンバスへ読み込む。プレビューモードの場合はBlockCanvasControl自身が持つ
        // デモツリーがそのまま表示される（ここでは何もしない）。
        if (_isEditingSpecificScript)
        {
            var loaded = BlockScriptSerializer.Deserialize(initialScript);
            _canvas.LoadProgram(loaded);
        }

        // Feature: UI改善 — 固定座標(Location)のボタンはウィンドウを縮小すると画面外にはみ出してしまうため、
        // 他フォームと同様のDock=Fill+RightToLeftのFlowLayoutPanelへ変更し、幅に追従するようにする。
        var pnlBottom = new Panel { Dock = DockStyle.Bottom, Height = 42 };
        var flowBottom = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) };
        _btnCancel = new Button { Text = "キャンセル", AutoSize = true, Padding = new Padding(10, 5, 10, 5) };
        // キャンセル時は保存処理を一切行わず、Cancelledイベントだけを外部へ通知する
        _btnCancel.Click += (s, e) => Cancelled?.Invoke(this, EventArgs.Empty);
        // Feature: UI改善（提案書 BS-4）— 保存前に「実行すると何もしない/意図しない既定動作になる」パターンを検知して警告する。
        _btnOk = new Button { Text = "💾 OK", AutoSize = true, Padding = new Padding(10, 5, 10, 5), BackColor = Color.FromArgb(40, 167, 69), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
        _btnOk.Click += (s, e) =>
        {
            // 保存前にキャンバス全体を検査し、実行しても意味を持たない設定（空のC字ブロック等）がないか確認する
            var warnings = ValidateScript();
            if (warnings.Count > 0)
            {
                // 警告が多すぎると読みにくくなるため、最初の8件のみ表示し、残りは件数のみを案内する
                string msg = "保存前に確認してください:\n\n" +
                    string.Join("\n", warnings.Take(8)) +
                    (warnings.Count > 8 ? $"\n…他{warnings.Count - 8}件" : "") +
                    "\n\nこのまま保存しますか？";
                // 警告を無視して保存するかはユーザーの判断に委ねる（強制ブロックはしない）。
                // Noが選ばれた場合はここで処理を中断し、保存もSavedイベントの発火も行わない。
                if (MessageBox.Show(msg, "確認", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            }
            // キャンバス上のBlockInstanceツリーをJSON AST（JArray）へシリアライズし、外部へ保存完了を通知する
            ResultScript = BlockScriptSerializer.Serialize(_canvas.TopLevel);
            Saved?.Invoke(this, ResultScript);
        };
        // RightToLeftは追加順が右から並ぶため、OKを一番右にしたい場合は先にCancelを追加する
        flowBottom.Controls.Add(_btnCancel);
        flowBottom.Controls.Add(_btnOk);
        pnlBottom.Controls.Add(flowBottom);

        Controls.Add(pnlCanvas);
        Controls.Add(pnlPalette);
        Controls.Add(pnlBottom);
        Controls.Add(lblNotice);
    }

    // Feature: UI改善（提案書 BS-4）— C型ブロックの中身が空、または条件ソケットが未設定のまま保存すると
    // 「実行しても何も起きない」「常に既定の動作になる」状態になり、ゲーム内デバッグ表示でしか気づけない。
    // 保存前にツリー全体を辿ってこれらを検出し、具体的な箇所を教える。
    // 戻り値は警告メッセージの一覧（空なら問題なし）。保存自体を強制的にブロックするものではなく、
    // 呼び出し元（_btnOk.Click）でユーザーに確認を取るための材料として使われる。
    private List<string> ValidateScript()
    {
        var warnings = new List<string>();

        // ブロック列(seq)を再帰的に辿って警告を集めるローカル関数。
        // context : 警告メッセージに添える「どのブロックの中か」を示す親ブロック名（トップレベルならhat名）。
        void Walk(List<BlockInstance> seq, string context)
        {
            foreach (var b in seq)
            {
                // C型ブロック（Forever/If等、HasBody=true）なのに中身(Body)が空 → 実行しても何もしないことになる
                if (b.Def.HasBody && b.Body.Count == 0)
                    warnings.Add($"「{b.Def.DisplayName}」の中身が空です（{context}の中）。何も実行されません。");
                // IfElseのように「でなければ」側(Else)を持てるブロックで、本体(Body)には中身があるのにElse側だけ
                // 空の場合 → 意図的に空にしている可能性もあるが、設定漏れの可能性が高いため注意喚起する
                if (b.Def.HasElse && b.Else.Count == 0 && b.Body.Count > 0)
                    warnings.Add($"「{b.Def.DisplayName}」の「でなければ」側が空です（{context}の中）。");

                // 真偽値を受け取るはずの引数ソケット(BoolSlot)に、何もブロックが差し込まれていない場合、
                // 実行時にはエンジン側の既定値（既定の真偽）で判定されてしまうため、意図しない挙動になりやすい
                foreach (var arg in b.Def.Args)
                {
                    if (arg.Type == BlockArgType.BoolSlot && !b.ArgBlocks.ContainsKey(arg.Name))
                        warnings.Add($"「{b.Def.DisplayName}」の条件（{arg.Label}）が未設定です（{context}の中）。既定の動作になります。");
                }

                // 自分自身がBody/Elseを持つ場合は、その中身に対しても再帰的に同じチェックを行う
                if (b.Def.HasBody) Walk(b.Body, b.Def.DisplayName);
                if (b.Def.HasElse) Walk(b.Else, b.Def.DisplayName);
            }
        }

        // キャンバス上の最上位（Hatブロック＝OnSpawn等の開始点）それぞれについて、
        // 中身が空でないかを確認しつつ、空でなければその中身をWalkで再帰的にチェックする
        foreach (var hat in _canvas.TopLevel)
        {
            if (hat.Def.HasBody && hat.Body.Count == 0)
                warnings.Add($"「{hat.Def.DisplayName}」の中身が空です。何も実行されません。");
            else if (hat.Def.HasBody)
                Walk(hat.Body, hat.Def.DisplayName);
        }
        return warnings;
    }

    // 「テンプレートから開始...」ボタン押下時の処理。TemplatePickerFormを開き、選ばれたテンプレートを
    // キャンバスへ読み込む。テンプレート読み込みはキャンバスの内容を丸ごと置き換えてしまうため、
    // 既に何か組み立て済みの内容がある場合は事前に確認を取る。
    private void OpenTemplatePicker()
    {
        if (_canvas.TopLevel.Count > 0)
        {
            // 現在のキャンバスに1つでもトップレベルブロック（Hat）があれば、上書きの確認を挟む。
            // Noが選ばれた場合はここで処理を中断し、ピッカー自体を開かない。
            if (MessageBox.Show("テンプレートを読み込むと、現在キャンバスにある内容は失われます。よろしいですか？", "確認",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        }
        using var picker = new TemplatePickerForm();
        // キャンセルされた場合、またはOKだが何も選択されていなかった場合は何もせず終了する
        if (picker.ShowDialog() != DialogResult.OK || picker.SelectedTemplate == null) return;
        // 選択されたテンプレートのJSON ASTを組み立て(Build)、BlockInstanceツリーへ変換してキャンバスへ読み込む
        _canvas.LoadProgram(BlockScriptSerializer.Deserialize(picker.SelectedTemplate.Build()));
    }
}
