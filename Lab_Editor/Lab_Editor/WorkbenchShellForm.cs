using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace Lab_Editor;

// UI改善（構造改修フェーズ5）— Form1 → AssetManagerForm → PartsEditorForm →
// BehaviorScriptEditorForm/HitboxEditorForm という最大3重のShowDialog()ネストを、
// パンくず表示＋戻るボタン付きの単一ウィンドウ内でページを切り替える方式に置き換える。
// 対象はAssetManager/PartsEditor/BehaviorScriptEditor/HitboxEditor/SizeEditor/
// CommonEventEditorの6画面のみ（他のエディタは元々Form1から1階層で開いており対象外）。
//
// シェル自体はForm1からShowDialog()で開く（Form1側と同時にAssetDefinitionsを書き換える
// 競合を避けるため。非モーダル化は今回のスコープ外）。
//
// 各ページはUserControlで、DialogResultの代わりにSaved/Cancelledイベントで結果を返す。
// ページ側がAcceptButton/CancelButton相当を必要とする場合はNavigateTo呼び出し側が
// accept/cancelパラメータで渡し、シェルが自身のAcceptButton/CancelButtonへ張り替える。
public class WorkbenchShellForm : Form
{
    private readonly record struct NavEntry(Control Page, string Label, Button? Accept, Button? Cancel);

    private readonly Panel _content = new() { Dock = DockStyle.Fill };
    private readonly FlowLayoutPanel _breadcrumbRow = new() { AutoSize = true, WrapContents = false, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(12, 6, 0, 0) };
    private readonly Button _btnBack = new() { Text = "← 戻る", AutoSize = true, Padding = new Padding(8, 3, 8, 3), Enabled = false };
    private readonly Stack<NavEntry> _backStack = new();
    private NavEntry? _current;

    public WorkbenchShellForm()
    {
        Text = "アセット・ワークベンチ";
        Size = new Size(1160, 760);
        MinimumSize = new Size(900, 600);
        StartPosition = FormStartPosition.CenterParent;
        Font = UiTheme.Base;

        _btnBack.Click += (s, e) => GoBack();

        var flowBar = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        flowBar.Controls.Add(_btnBack);
        flowBar.Controls.Add(_breadcrumbRow);

        var pnlBreadcrumb = new Panel { Dock = DockStyle.Top, Height = 34, Padding = new Padding(8, 4, 8, 4) };
        pnlBreadcrumb.Controls.Add(flowBar);

        // Dock=Fillのcontentを先にAddし、Dock=Topのパンくずを後からAddする
        // （このコードベースの規約：AssetManagerForm.cs のflpTiles/pnlBulkFill と同じ順序）
        Controls.Add(_content);
        Controls.Add(pnlBreadcrumb);
    }

    // 新しいページへ進む。現在のページは非表示のまま保持し（Disposeしない）、戻るボタンで復帰できるようにする。
    public void NavigateTo(Control page, string label, Button? accept = null, Button? cancel = null)
    {
        if (_current is NavEntry cur)
        {
            _content.Controls.Remove(cur.Page);
            _backStack.Push(cur);
        }

        _current = new NavEntry(page, label, accept, cancel);
        page.Dock = DockStyle.Fill;
        _content.Controls.Add(page);
        AcceptButton = accept;
        CancelButton = cancel;
        RebuildBreadcrumb();
    }

    // 1つ手前のページに戻る。現在のページ（編集が完了/中断したページ）はここでDisposeする
    // ——PartsEditorPageControl等が持つタイマーを確実に止めるため、Controlsから外すだけでは不十分。
    public void GoBack()
    {
        if (_backStack.Count == 0 || _current is not NavEntry leaving) return;

        _content.Controls.Remove(leaving.Page);
        leaving.Page.Dispose();

        var prev = _backStack.Pop();
        _current = prev;
        prev.Page.Dock = DockStyle.Fill;
        _content.Controls.Add(prev.Page);
        AcceptButton = prev.Accept;
        CancelButton = prev.Cancel;
        RebuildBreadcrumb();
    }

    private void RebuildBreadcrumb()
    {
        _breadcrumbRow.Controls.Clear();

        var trail = new List<string>(_backStack.Count + 1);
        foreach (var e in _backStack) trail.Add(e.Label); // Stackは新しい順に列挙されるため
        trail.Reverse();                                   // ルート→現在の順に並べ直す
        if (_current is NavEntry cur) trail.Add(cur.Label);

        for (int i = 0; i < trail.Count; i++)
        {
            if (i > 0)
                _breadcrumbRow.Controls.Add(new Label { Text = "›", AutoSize = true, Margin = new Padding(4, 3, 4, 0), ForeColor = Color.Gray });

            bool isLast = i == trail.Count - 1;
            _breadcrumbRow.Controls.Add(new Label
            {
                Text = trail[i],
                AutoSize = true,
                Margin = new Padding(0, 3, 0, 0),
                Font = isLast ? UiTheme.Bold : UiTheme.Base,
                ForeColor = isLast ? Color.Black : Color.DimGray,
            });
        }

        _btnBack.Enabled = _backStack.Count > 0;
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        // 残っている全ページ（現在+バックスタックに退避中のもの）を確実にDisposeする（タイマー等のリーク防止）
        if (_current is NavEntry cur) cur.Page.Dispose();
        while (_backStack.Count > 0) _backStack.Pop().Page.Dispose();
        base.OnFormClosed(e);
    }
}
