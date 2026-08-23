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
    // 「戻る」で復帰するために必要な情報をひとまとめにした記録用の型。
    // Page   : そのページのコントロール本体
    // Label  : パンくずリストに表示する見出し文字列
    // Accept : そのページ用のAcceptButton（Enterキーで実行されるボタン。無ければnull）
    // Cancel : そのページ用のCancelButton（Escキーで実行されるボタン。無ければnull）
    private readonly record struct NavEntry(Control Page, string Label, Button? Accept, Button? Cancel);

    // 現在表示中のページを差し込む領域
    private readonly Panel _content = new() { Dock = DockStyle.Fill };
    // パンくずリスト（現在位置までの経路）を横並びで表示する領域
    private readonly FlowLayoutPanel _breadcrumbRow = new() { AutoSize = true, WrapContents = false, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(12, 6, 0, 0) };
    // 1つ前のページに戻るためのボタン（履歴が無いときは無効化する）
    private readonly Button _btnBack = new() { Text = "← 戻る", AutoSize = true, Padding = new Padding(8, 3, 8, 3), Enabled = false };
    // これまでに辿ってきたページの履歴（戻るボタンで1つずつ復帰する）
    private readonly Stack<NavEntry> _backStack = new();
    // 現在表示中のページの情報（まだどのページも開いていない場合はnull）
    private NavEntry? _current;

    public WorkbenchShellForm()
    {
        Text = "アセット・ワークベンチ";
        Size = new Size(1160, 760);
        MinimumSize = new Size(900, 600);
        StartPosition = FormStartPosition.CenterParent;
        Font = UiTheme.Base;

        // 「戻る」ボタン押下時はGoBack()を呼んで1つ前のページへ戻す
        _btnBack.Click += (s, e) => GoBack();

        // 「戻る」ボタンとパンくずリストを横並びに配置するための行
        var flowBar = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        flowBar.Controls.Add(_btnBack);
        flowBar.Controls.Add(_breadcrumbRow);

        // 画面上部に固定表示するパンくずバー全体を格納するパネル
        var pnlBreadcrumb = new Panel { Dock = DockStyle.Top, Height = 34, Padding = new Padding(8, 4, 8, 4) };
        pnlBreadcrumb.Controls.Add(flowBar);

        // Dock=Fillのcontentを先にAddし、Dock=Topのパンくずを後からAddする
        // （このコードベースの規約：AssetManagerForm.cs のflpTiles/pnlBulkFill と同じ順序）
        Controls.Add(_content);
        Controls.Add(pnlBreadcrumb);
    }

    // 新しいページへ進む。現在のページは非表示のまま保持し（Disposeしない）、戻るボタンで復帰できるようにする。
    // page   : 新しく表示するページのコントロール
    // label  : パンくずリストに表示する見出し文字列
    // accept : このページ用のAcceptButton相当（省略可）
    // cancel : このページ用のCancelButton相当（省略可）
    public void NavigateTo(Control page, string label, Button? accept = null, Button? cancel = null)
    {
        if (_current is NavEntry cur)
        {
            // 表示中のページを画面から外すが、破棄はせず履歴スタックへ積んでおく
            // （後で「戻る」が押されたときにそのまま復元できるようにするため）
            _content.Controls.Remove(cur.Page);
            _backStack.Push(cur);
        }

        // 新しいページを現在ページとして記録し、画面に表示する
        _current = new NavEntry(page, label, accept, cancel);
        page.Dock = DockStyle.Fill;
        _content.Controls.Add(page);
        // シェル自身のAccept/CancelButtonを、新しいページのボタンに張り替える
        AcceptButton = accept;
        CancelButton = cancel;
        // パンくずリストの表示を最新の経路に合わせて再構築する
        RebuildBreadcrumb();
    }

    // 1つ手前のページに戻る。現在のページ（編集が完了/中断したページ）はここでDisposeする
    // ——PartsEditorPageControl等が持つタイマーを確実に止めるため、Controlsから外すだけでは不十分。
    public void GoBack()
    {
        // 履歴が無い、または現在ページが存在しない場合は何もしない
        if (_backStack.Count == 0 || _current is not NavEntry leaving) return;

        // 離脱するページを画面から外し、リソース（タイマー等）を確実に解放する
        _content.Controls.Remove(leaving.Page);
        leaving.Page.Dispose();

        // 履歴から1つ前のページ情報を取り出して復元する
        var prev = _backStack.Pop();
        _current = prev;
        prev.Page.Dock = DockStyle.Fill;
        _content.Controls.Add(prev.Page);
        // シェルのAccept/CancelButtonも、復元したページのものへ戻す
        AcceptButton = prev.Accept;
        CancelButton = prev.Cancel;
        // パンくずリストの表示を最新の経路に合わせて再構築する
        RebuildBreadcrumb();
    }

    // 現在の履歴スタックと現在ページの情報から、画面上部のパンくずリストを作り直す。
    private void RebuildBreadcrumb()
    {
        _breadcrumbRow.Controls.Clear();

        // ルートから現在ページまでの経路（見出し文字列の並び）を組み立てる
        var trail = new List<string>(_backStack.Count + 1);
        foreach (var e in _backStack) trail.Add(e.Label); // Stackは新しい順に列挙されるため
        trail.Reverse();                                   // ルート→現在の順に並べ直す
        if (_current is NavEntry cur) trail.Add(cur.Label);

        for (int i = 0; i < trail.Count; i++)
        {
            if (i > 0)
                // 経路の区切りとして「›」記号を挟む（先頭要素の前には不要）
                _breadcrumbRow.Controls.Add(new Label { Text = "›", AutoSize = true, Margin = new Padding(4, 3, 4, 0), ForeColor = Color.Gray });

            // 一番最後（＝現在表示中のページ）だけ太字・黒文字で強調表示する
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

        // 履歴が1件も無い（ルート画面にいる）場合は戻るボタンを無効化する
        _btnBack.Enabled = _backStack.Count > 0;
    }

    // フォームが閉じられた後処理。表示中・履歴中を問わず、開いていた全ページを確実に破棄する。
    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        // 残っている全ページ（現在+バックスタックに退避中のもの）を確実にDisposeする（タイマー等のリーク防止）
        if (_current is NavEntry cur) cur.Page.Dispose();
        while (_backStack.Count > 0) _backStack.Pop().Page.Dispose();
        base.OnFormClosed(e);
    }
}
