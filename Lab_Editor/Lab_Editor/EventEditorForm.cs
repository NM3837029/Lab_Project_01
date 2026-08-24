using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace Lab_Editor;

// ───────────────────────────────────────────────
//  EventEditorForm
//  EventTrigger の条件とアクションを編集するフォーム (MZ風UI)
//
//  ステージ上に配置する「イベントトリガー」（矩形範囲＋発動条件＋発動時の処理）を
//  1件分編集するためのモーダルダイアログ。ツクールMZのイベントコマンド編集画面を意識した
//  見た目にしており、上から「範囲(矩形)」「実行条件」「実行内容(アクションのコマンドリスト)」の
//  3ブロックを縦に並べている。
// ───────────────────────────────────────────────
public class EventEditorForm : Form
{
    // ── 公開プロパティ ──────────────────────────
    // OKボタンが押されて確定した、編集後のEventTriggerオブジェクト。
    // キャンセル時は更新されず、呼び出し元はDialogResultがOKの場合のみこのプロパティを参照すること。
    public EventTrigger ResultTrigger { get; private set; } = null!;

    // ── 条件型 ──────────────────────────────────
    // Feature: UI改善（提案書 EV-2）— コンボボックスには日本語ラベル(Label)を表示しつつ、
    // 保存されるデータ自体は従来通りKey(英語)を使う。ToString()をオーバーライドすることで、
    // DataSource/DisplayMemberのリフレクション（ValueTupleの要素名は実行時に反映されない）に
    // 頼らずシンプルに表示を切り替えている。
    // コンボボックス(_cmbCondition)の1項目を表す小さなクラス。
    // Key   : 実際に保存されるデータ（英語の内部識別子。EventTrigger.conditionに書き込まれる値）
    // Label : 画面上に表示される日本語の説明文
    // ComboBoxはToString()の戻り値をそのまま表示に使うため、ToString()をLabelにオーバーライドすることで
    // 「保存値は英語のKey、表示は日本語のLabel」という変換をリフレクションを使わずに実現している。
    private class ConditionOption
    {
        public string Key = "";
        public string Label = "";
        public override string ToString() => Label;
    }

    // 選択可能な条件タイプの一覧。コンボボックスにそのまま流し込まれる（Items.AddRange参照）。
    // 各条件の意味：
    //   PlayerEnter         : プレイヤーが矩形範囲に入った瞬間に発動
    //   PlayerExit          : プレイヤーが矩形範囲から出た瞬間に発動
    //   AllEnemiesDefeated  : （おそらく範囲内の）敵を全滅させた時点で発動
    //   SwitchOn            : 指定した名前のスイッチがONになった時に発動（パラメータにスイッチ名を指定）
    //   ItemCollected       : 指定したアイテムを取得した時に発動（パラメータにアイテムIDを指定）
    //   TimerExpired        : タイマーが0になった時に発動
    private static readonly ConditionOption[] ConditionTypes =
    {
        new() { Key = "PlayerEnter", Label = "プレイヤーが範囲に入ったとき" },
        new() { Key = "PlayerExit", Label = "プレイヤーが範囲から出たとき" },
        new() { Key = "AllEnemiesDefeated", Label = "敵を全滅させたとき" },
        new() { Key = "SwitchOn", Label = "スイッチがONになったとき" },
        new() { Key = "ItemCollected", Label = "アイテムを取得したとき" },
        new() { Key = "TimerExpired", Label = "タイマーが切れたとき" },
    };

    // ── コントロール ────────────────────────────
    private TextBox           _txtId          = null!; // トリガーIDの入力欄
    private NumericUpDown     _nudX           = null!; // 判定矩形のX座標入力欄
    private NumericUpDown     _nudY           = null!; // 判定矩形のY座標入力欄
    private NumericUpDown     _nudW           = null!; // 判定矩形の幅入力欄
    private NumericUpDown     _nudH           = null!; // 判定矩形の高さ入力欄

    private ComboBox          _cmbCondition   = null!; // 発動条件タイプを選ぶドロップダウン
    private TextBox           _txtCondParam   = null!; // 条件タイプに応じた追加パラメータ（スイッチ名やアイテムIDなど）の入力欄
    private CheckBox          _chkOneShot     = null!; // 「一度だけ実行」フラグのチェックボックス

    private ActionEditorControl _actionEditor = null!; // 発動時に実行するアクション（コマンド列）を編集するサブコントロール

    private Button            _btnOk          = null!; // 保存して閉じるボタン
    private Button            _btnCancel      = null!; // 変更を破棄して閉じるボタン

    // ── コンストラクタ ─────────────────────────
    // trigger    : 編集対象となる既存のEventTrigger（新規作成の場合は呼び出し元が空のインスタンスを渡す想定）
    // assets     : アクション編集（スポーン対象の選択など）で参照するアセット定義一式
    // stageFiles : 「ステージ遷移」系アクションの遷移先候補として使うステージファイル名の一覧
    public EventEditorForm(EventTrigger trigger, AssetDefinitions assets, List<string> stageFiles)
    {
        InitializeComponent(); // まずUIコントロール一式を配置する
        _actionEditor.SetContext(assets, stageFiles); // アクションエディタにアセット/ステージ一覧の参照を渡す
        LoadTrigger(trigger); // 渡されたEventTriggerの現在値を各コントロールへ反映する
    }

    // ── UI 構築 ────────────────────────────────
    // フォーム上の全コントロールをコードから直接生成・配置する（デザイナファイルは使わない方式）。
    // yを上から順に加算していくことで、各セクションを縦に積み重ねて配置している。
    private void InitializeComponent()
    {
        Text            = "イベント・トリガー編集";
        Size            = new Size(720, 600);
        Font            = UiTheme.Base;
        StartPosition   = FormStartPosition.CenterParent; // 親フォームの中央に表示する
        UiTheme.ApplyResizableChrome(this); // リサイズ可能な枠・共通の見た目テーマを適用する

        int y = 10; // 現在の配置Y座標。各セクションを追加するたびに下へ進めていく

        // ── Trigger ID ──────────────────────────
        // トリガーを一意に識別するためのID文字列の入力欄
        AddLabel("トリガー ID:", 10, y);
        _txtId = new TextBox { Location = new Point(110, y), Width = 200 };
        Controls.Add(_txtId);
        y += 30;

        // ── 矩形 X, Y, W, H ─────────────────────
        // イベントが発動する判定範囲（矩形）の座標とサイズを個別のNumericUpDownで入力させる
        AddLabel("X:", 10, y);
        _nudX = MakeNud(60, y);

        AddLabel("Y:", 150, y);
        _nudY = MakeNud(200, y);

        AddLabel("Width:", 300, y);
        _nudW = MakeNud(360, y);

        AddLabel("Height:", 470, y);
        _nudH = MakeNud(530, y);

        Controls.AddRange(new Control[] { _nudX, _nudY, _nudW, _nudH });
        y += 36;

        // ── 仕切り線 ────────────────────────────
        AddSeparator(y); y += 14;

        // ── 条件セクション ───────────────────────
        AddLabel("■ 実行条件", 10, y, bold: true); y += 24;

        // 発動条件の種類を選ぶドロップダウン。ConditionTypes配列の各要素（表示は日本語Label、
        // 値はKey）がそのまま項目になる。DropDownListにすることで直接入力を禁止し、一覧からのみ選ばせる。
        AddLabel("条件タイプ:", 10, y);
        _cmbCondition = new ComboBox
        {
            Location     = new Point(100, y),
            Width        = 180,
            DropDownStyle = ComboBoxStyle.DropDownList,
        };
        _cmbCondition.Items.AddRange(ConditionTypes);
        Controls.Add(_cmbCondition);

        // 条件タイプによっては追加の文字列パラメータが必要になる（例：SwitchOnならスイッチ名、
        // ItemCollectedならアイテムID）。どの条件でも共通の1つのテキスト欄で受け付ける。
        AddLabel("パラメータ:", 300, y);
        _txtCondParam = new TextBox { Location = new Point(380, y), Width = 200 };
        Controls.Add(_txtCondParam);
        y += 30;

        // 「一度だけ実行」チェックボックス。ONにすると、このトリガーは条件成立後1回だけ発動し、
        // 以後は再発動しなくなる（oneShotフラグとして保存される）。
        _chkOneShot = new CheckBox
        {
            Text     = "一度だけ実行 (oneShot)",
            Location = new Point(10, y),
            Width    = 220,
        };
        Controls.Add(_chkOneShot);
        y += 34;

        // ── 仕切り線 ────────────────────────────
        AddSeparator(y); y += 14;

        // ── アクションセクション (MZ風コマンドリスト) ────
        AddLabel("■ 実行内容", 10, y, bold: true); y += 24;

        // 条件成立時に実行される処理（コマンド列）を編集するサブコントロール。
        // 実際のコマンド追加/削除/並び替えUIはActionEditorControl側に実装されている。
        _actionEditor = new ActionEditorControl { Location = new Point(10, y) };
        Controls.Add(_actionEditor);

        y += _actionEditor.Height + 14;

        // ── 下部ボタン ──────────────────────────
        AddSeparator(y); y += 10;

        _btnOk     = MakeButton("💾 OK",    480, y, 100);
        _btnCancel = MakeButton("キャンセル", 590, y, 100);

        UiTheme.StylePrimaryButton(_btnOk);     // OKボタンを目立つ配色にする（プライマリアクション）
        UiTheme.StyleSecondaryButton(_btnCancel); // キャンセルボタンは控えめな配色にする

        _btnOk.Click     += BtnOk_Click;
        // キャンセル時は入力内容を破棄し、DialogResult.Cancelを設定してフォームを閉じるだけでよい
        _btnCancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        Controls.AddRange(new Control[] { _btnOk, _btnCancel });
    }

    // ── データ連携 ──────────────────────
    // 引数で渡されたEventTriggerの現在の値を、各入力コントロールへ反映する（画面初期表示用）。
    private void LoadTrigger(EventTrigger t)
    {
        _txtId.Text     = t.id ?? "";
        // float型のx/y/width/heightをNumericUpDown.Valueのdecimal型へキャストして設定する
        _nudX.Value     = (decimal)(t.x);
        _nudY.Value     = (decimal)(t.y);
        _nudW.Value     = (decimal)(t.width);
        _nudH.Value     = (decimal)(t.height);
        _txtCondParam.Text = t.conditionParam ?? "";
        _chkOneShot.Checked = t.oneShot;

        // 保存されている条件Key(英語)から、ConditionTypes配列内の対応する項目のインデックスを検索する。
        // 見つからない場合（データ不整合や未設定など）は先頭(0番目)の項目を仮に選択しておく。
        int condIdx = Array.FindIndex(ConditionTypes, o => o.Key == (t.condition ?? ""));
        _cmbCondition.SelectedIndex = condIdx >= 0 ? condIdx : 0;

        _actionEditor.LoadActions(t.actions); // アクション一覧もアクションエディタ側へ読み込ませる
    }

    // Feature: UI改善（提案書 CUT-3）— 保存前チェックをBehaviorScriptEditorFormと同様の考え方で
    // トリガー編集にも広げる。発動しても意味を持たない/正しく判定できない設定を検知して警告する。
    // 戻り値は警告メッセージの一覧（空リストなら問題なし）。実際に保存を止めるかどうかは呼び出し元が判断する。
    private List<string> ValidateTrigger()
    {
        var warnings = new List<string>();
        // トリガーIDが空だと、どのトリガーか判別できず管理上問題があるため警告する
        if (string.IsNullOrWhiteSpace(_txtId.Text)) warnings.Add("トリガーIDが未入力です。");
        // 判定範囲の幅または高さが0以下だと、そもそも当たり判定として成立せず絶対に発動しない
        if (_nudW.Value <= 0 || _nudH.Value <= 0) warnings.Add("Width/Heightが0以下です。判定範囲が存在しないため発動しません。");

        // SwitchOn/ItemCollected条件は、対象を特定するためのパラメータ（スイッチ名やアイテムID）が
        // 必須。未入力のままだと「何のスイッチ/アイテムか」を判定できず、正しく発動しない恐れがある。
        var selectedCond = _cmbCondition.SelectedItem as ConditionOption;
        if ((selectedCond?.Key == "SwitchOn" || selectedCond?.Key == "ItemCollected") && string.IsNullOrWhiteSpace(_txtCondParam.Text))
            warnings.Add($"条件「{selectedCond.Label}」にはパラメータの指定が必要です（未入力のままだと正しく判定できません）。");

        // アクションが1つも設定されていないと、条件が成立しても実行される処理が無く何も起こらない
        if (_actionEditor.GetActions().Count == 0)
            warnings.Add("実行内容（アクション）が1つも設定されていません。トリガーが発動しても何も起きません。");

        return warnings;
    }

    // ── 保存 ──────────────────────────────────
    // OKボタン押下時の処理。保存前チェックで警告が出た場合はユーザーに確認を取った上で、
    // 問題なければ（またはユーザーが続行を選んだ場合）現在の入力内容からResultTriggerを組み立てて閉じる。
    private void BtnOk_Click(object? sender, EventArgs e)
    {
        var warnings = ValidateTrigger();
        if (warnings.Count > 0)
        {
            // 警告内容を一覧表示し、そのまま保存してよいかをユーザーに確認する（強制的なブロックはしない）
            string msg = "保存前に確認してください:\n\n" + string.Join("\n", warnings) + "\n\nこのまま保存しますか？";
            if (MessageBox.Show(msg, "確認", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        }

        // 各コントロールの現在値からEventTriggerを新規構築する（LoadTriggerの逆変換に相当）
        ResultTrigger = new EventTrigger
        {
            id             = _txtId.Text,
            x              = (float)_nudX.Value,
            y              = (float)_nudY.Value,
            width          = (float)_nudW.Value,
            height         = (float)_nudH.Value,
            condition      = (_cmbCondition.SelectedItem as ConditionOption)?.Key ?? "",
            conditionParam = _txtCondParam.Text,
            oneShot        = _chkOneShot.Checked,
            actions        = _actionEditor.GetActions(),
        };

        DialogResult = DialogResult.OK; // 呼び出し元へ「OKで確定した」ことを伝える
        Close();
    }

    // ── ヘルパー ──────────────────────────────
    // ラベルを生成してフォームへ追加する共通処理。UiTheme.CreateLabelでフォント等の見た目を統一する。
    // yに+3しているのは、ラベルとテキストボックス等を並べたときに縦位置の見た目を揃えるための微調整。
    private void AddLabel(string text, int x, int y, bool bold = false)
    {
        Controls.Add(UiTheme.CreateLabel(text, new Point(x, y + 3), bold));
    }

    // 横幅680pxの区切り線をフォームへ追加する（セクション間の視覚的な境目として使用）。
    private void AddSeparator(int y)
    {
        Controls.Add(UiTheme.CreateSeparator(new Point(10, y), 680));
    }

    // 数値入力欄(NumericUpDown)を指定座標に生成するショートカット。見た目の共通化はUiTheme側で行う。
    private NumericUpDown MakeNud(int x, int y) =>
        UiTheme.CreateNumericUpDown(new Point(x, y));

    // ボタンを指定座標・幅（高さは30固定）で生成するショートカット。
    private static Button MakeButton(string text, int x, int y, int w) =>
        UiTheme.CreateButton(text, new Point(x, y), new Size(w, 30));
}
