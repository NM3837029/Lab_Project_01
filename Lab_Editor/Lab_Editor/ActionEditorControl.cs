using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Lab_Editor;

// ───────────────────────────────────────────────
//  ActionEditorControl
//  EventTrigger（イベントの発生条件）や CommonEventDef（複数箇所から呼び出せる共通イベント）が
//  共通で使う「実行内容（アクション）」を編集するためのUI部品。
//  RPGツクールMZのプラグインコマンド風に、リストで選んだアクション種別（ShowMessageやPlaySeなど）
//  に応じて、パラメータ欄のラベル文言・入力候補（プルダウン）・有効/無効の状態が自動で切り替わる。
// ───────────────────────────────────────────────
public class ActionEditorControl : Panel
{
    // このエディタで選択できる全アクション種別の一覧。
    // ComboBox（_cmbActionType）の選択肢としてそのまま使われるほか、
    // 保存データの act.action 文字列としてもこの名前がそのまま使われる。
    public static readonly string[] ActionTypes =
    {
        "ShowMessage", "ChangeBgm", "PlaySe", "ActivateGimmick",
        "OpenDoor", "SpawnEnemy", "SpawnItem", "SetSwitch",
        "MoveCamera", "StageClear", "GoToStage", "CallCommonEvent"
    };

    // Param1/Param2の入力欄が「どんな種類の値」を期待しているかを表す種別。
    // 種別によって、プルダウンに出す候補リストや、入力欄を無効化するかどうかが変わる。
    // None      : このパラメータは使用しない（欄自体を非表示/無効にする）
    // FreeText  : 自由入力のテキスト（候補プルダウンなし）
    // LongText  : 長文（メッセージ本文など）。専用の編集ダイアログ（✎ボタン）を開ける
    // Bgm/Se/Gimmick/Enemy/Item/Stage/CommonEvent : それぞれ対応するアセット一覧からIDを選ぶ
    private enum ParamKind { None, FreeText, LongText, Bgm, Se, Gimmick, Enemy, Item, Stage, CommonEvent }

    // アクション種別ごとに「Param1のラベル文言と種別」「Param2のラベル文言と種別」を定義する対応表。
    // ここに載っていないアクション種別が来た場合は、ApplyFieldMeta/RefreshParam1Choices側で
    // デフォルト値（Param1:/Param2:のFreeText扱い）にフォールバックする。
    private static readonly Dictionary<string, (string label1, ParamKind kind1, string label2, ParamKind kind2)> ActionMeta = new()
    {
        ["ShowMessage"]      = ("メッセージ:",      ParamKind.LongText, "話者名(任意):", ParamKind.FreeText),
        ["ChangeBgm"]        = ("BGM ID:",          ParamKind.Bgm,      "",              ParamKind.None),
        ["PlaySe"]           = ("SE ID:",           ParamKind.Se,       "",              ParamKind.None),
        ["ActivateGimmick"]  = ("ギミックID:",       ParamKind.Gimmick,  "パラメータ(任意):", ParamKind.FreeText),
        ["OpenDoor"]         = ("ギミックID(扉):",   ParamKind.Gimmick,  "",              ParamKind.None),
        ["SpawnEnemy"]       = ("敵ID:",            ParamKind.Enemy,    "座標 X,Y(任意):", ParamKind.FreeText),
        ["SpawnItem"]        = ("アイテムID:",       ParamKind.Item,     "座標 X,Y(任意):", ParamKind.FreeText),
        ["SetSwitch"]        = ("スイッチID:",       ParamKind.FreeText, "ON/OFF(既定ON):", ParamKind.FreeText),
        ["MoveCamera"]       = ("X座標:",           ParamKind.FreeText, "Y座標:",         ParamKind.FreeText),
        ["StageClear"]       = ("(パラメータなし)",  ParamKind.None,     "",              ParamKind.None),
        ["GoToStage"]        = ("ステージファイル:", ParamKind.Stage,    "",              ParamKind.None),
        ["CallCommonEvent"]  = ("コモンイベントID:", ParamKind.CommonEvent, "",           ParamKind.None),
    };

    // 左側：登録済みアクションを一覧表示するリストボックス
    private ListBox       _lstActions    = null!;
    // アクションを新規追加するボタン
    private Button        _btnAddAction  = null!;
    // 選択中のアクションを削除するボタン
    private Button        _btnDelAction  = null!;
    // Feature: UI改善（提案書 EV-1）— 実行順が意味を持つコマンド列を、削除して再追加せずに並び替えられるようにする
    // 選択中のアクションを1つ上へ移動するボタン
    private Button        _btnMoveUp     = null!;
    // 選択中のアクションを1つ下へ移動するボタン
    private Button        _btnMoveDown   = null!;

    // 右側：選択中アクションの詳細（種別・パラメータ・遅延時間）を編集するパネル一式
    private Panel         _pnlParams     = null!;
    // アクション種別を選ぶプルダウン
    private ComboBox      _cmbActionType = null!;
    // Param1欄のラベル（アクション種別に応じて文言が変わる）
    private Label         _lblP1         = null!;
    // Param2欄のラベル（アクション種別に応じて文言が変わる。不要なアクションでは非表示になる）
    private Label         _lblP2         = null!;
    // Param1の入力欄。アクション種別によって候補プルダウンの中身が切り替わる
    private ComboBox      _cmbParam1     = null!;
    // Param1がLongText（長文メッセージ等）のときだけ表示される、専用編集ダイアログを開くボタン
    private Button        _btnEditText   = null!;
    // Param2の入力欄（自由入力のテキストボックス）
    private TextBox       _txtParam2     = null!;
    // アクション実行までの遅延時間（秒）を指定する数値入力欄
    private NumericUpDown _nudDelay      = null!;

    // 現在編集中のアクション一覧の実データ。_lstActions.Itemsの表示文字列と対応するインデックスで同期している
    private List<EventActionEntry> _actions = new();
    // 選択行の切り替え等でUIの値をプログラム側からセットしている最中はtrueにし、
    // Param_Changedが「ユーザーの入力」と誤認して不要な書き戻しをしないようにするためのフラグ
    private bool _isUpdatingParams = false;

    // プルダウン候補（BGM/SE/敵/アイテム等のID一覧）を作るために参照するアセット定義データ
    private AssetDefinitions? _assets;
    // GoToStageアクションのステージファイル候補として使うステージファイル名一覧
    private List<string> _stageFiles = new();

    public ActionEditorControl()
    {
        BuildUI();
    }

    /// <summary>プルダウン候補生成に必要な参照データを設定する</summary>
    public void SetContext(AssetDefinitions assets, List<string> stageFiles)
    {
        _assets = assets;
        _stageFiles = stageFiles;
        // 現在選択中の行があれば候補を更新
        if (_cmbActionType.SelectedItem is string act) RefreshParam1Choices(act);
    }

    // 外部（呼び出し元の編集画面）から読み込んだアクション一覧をこのコントロールに反映する。
    // 既存の一覧はいったんすべてクリアしてから、渡された内容をコピーして積み直す
    // （参照をそのまま持たず新しいEventActionEntryとして複製することで、呼び出し元のデータを
    // 誤って直接書き換えてしまわないようにしている）。
    public void LoadActions(List<EventActionEntry>? actions)
    {
        _actions.Clear();
        _lstActions.Items.Clear();
        foreach (var act in actions ?? new List<EventActionEntry>())
            AddActionRow(new EventActionEntry { action = act.action, param1 = act.param1, param2 = act.param2, delay = act.delay });

        // 1件でも読み込めていれば先頭行を選択状態にし、詳細パネルを編集可能にする。
        // 1件もない場合は選択できる行がないのでパラメータパネルを無効化しておく。
        if (_lstActions.Items.Count > 0) _lstActions.SelectedIndex = 0;
        else _pnlParams.Enabled = false;
    }

    // 現在の編集結果を呼び出し元へ返す。内部リストのコピーを返すことで、
    // 呼び出し元が受け取ったリストをいじってもこのコントロールの内部状態には影響しないようにしている。
    public List<EventActionEntry> GetActions() => new(_actions);

    // ── UI 構築 ────────────────────────────────
    private void BuildUI()
    {
        Size = new Size(680, 210);

        // 左側：登録済みアクションの一覧を表示するリストボックス
        _lstActions = new ListBox
        {
            Location = new Point(0, 0),
            Size = new Size(380, 210),
            Font = new Font("Meiryo UI", 10f)
        };
        _lstActions.SelectedIndexChanged += LstActions_SelectedIndexChanged;

        // 右側：選択中アクションの詳細を表示・編集するパネル（枠線付き、背景をやや灰色にして区別する）
        _pnlParams = new Panel
        {
            Location = new Point(390, 0),
            Size = new Size(290, 210),
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.FromArgb(245, 245, 250)
        };

        var lblTitle = new Label { Text = "コマンド詳細", Location = new Point(10, 8), Font = new Font("Meiryo UI", 9f, FontStyle.Bold), AutoSize = true };

        // アクション種別を選ぶプルダウン。選択が変わるたびにParam1/Param2の意味も切り替える
        var lblAct = new Label { Text = "アクション:", Location = new Point(10, 38), AutoSize = true };
        _cmbActionType = new ComboBox { Location = new Point(90, 36), Width = 180, DropDownStyle = ComboBoxStyle.DropDownList };
        _cmbActionType.Items.AddRange(ActionTypes);
        _cmbActionType.SelectedIndexChanged += ActionType_Changed;

        // Param1入力欄。プルダウンとテキスト入力の両方を受け付けるDropDownスタイル
        // （候補にない値も手入力できるようにしている）
        _lblP1 = new Label { Text = "Param 1:", Location = new Point(10, 73), AutoSize = true };
        _cmbParam1 = new ComboBox { Location = new Point(10, 90), Width = 200, DropDownStyle = ComboBoxStyle.DropDown };
        _cmbParam1.TextChanged += Param_Changed;
        // 長文メッセージ編集用のボタン（ShowMessageのときだけApplyFieldMetaで表示される）
        _btnEditText = new Button { Text = "✎", Location = new Point(216, 89), Size = new Size(28, 24), Visible = false };
        _btnEditText.Click += BtnEditText_Click;

        // Param2入力欄（自由入力のテキストボックス）
        _lblP2 = new Label { Text = "Param 2:", Location = new Point(10, 122), AutoSize = true };
        _txtParam2 = new TextBox { Location = new Point(10, 139), Width = 234 };
        _txtParam2.TextChanged += Param_Changed;

        // アクション実行までの遅延時間（秒）を指定する数値入力欄。0.1秒刻み、最大100秒まで
        var lblDly = new Label { Text = "Delay(秒):", Location = new Point(10, 172), AutoSize = true };
        _nudDelay = new NumericUpDown { Location = new Point(90, 170), Width = 90, DecimalPlaces = 2, Increment = 0.1m, Maximum = 100 };
        _nudDelay.ValueChanged += Param_Changed;

        _pnlParams.Controls.AddRange(new Control[]
        {
            lblTitle, lblAct, _cmbActionType,
            _lblP1, _cmbParam1, _btnEditText,
            _lblP2, _txtParam2,
            lblDly, _nudDelay
        });
        // 何も選択されていない初期状態では編集できないよう無効化しておく
        _pnlParams.Enabled = false;

        // 下部：アクションの追加・削除・並び替え用ボタン群
        _btnAddAction = new Button { Text = "＋追加", Location = new Point(0, 216), Size = new Size(100, 30) };
        _btnDelAction = new Button { Text = "🗑 削除", Location = new Point(110, 216), Size = new Size(100, 30) };
        _btnMoveUp = new Button { Text = "▲ 上へ", Location = new Point(220, 216), Size = new Size(100, 30) };
        _btnMoveDown = new Button { Text = "▼ 下へ", Location = new Point(330, 216), Size = new Size(100, 30) };
        // 新規追加は初期値（空のEventActionEntry）で1件追加する
        _btnAddAction.Click += (_, _) => AddActionRow(new EventActionEntry());
        _btnDelAction.Click += BtnDelAction_Click;
        // 上へ(-1)/下へ(+1)は同じMoveSelectedActionに方向だけ渡して共通処理させる
        _btnMoveUp.Click += (_, _) => MoveSelectedAction(-1);
        _btnMoveDown.Click += (_, _) => MoveSelectedAction(1);

        Controls.AddRange(new Control[] { _lstActions, _pnlParams, _btnAddAction, _btnDelAction, _btnMoveUp, _btnMoveDown });
        Height = 250;
    }

    // ── 行操作 ────────────────────────────────
    // 新しいアクションを一覧の末尾に追加し、追加した行を選択状態にする。
    private void AddActionRow(EventActionEntry act)
    {
        // 保存データが壊れていたり未知のアクション名だった場合は、先頭のアクション種別にフォールバックする
        if (!ActionTypes.Contains(act.action)) act.action = ActionTypes[0];
        _actions.Add(act);
        _lstActions.Items.Add(GetActionDisplayText(act));
        _lstActions.SelectedIndex = _lstActions.Items.Count - 1;
    }

    // リストボックスに表示する1行分のテキストを組み立てる。
    // 「◆ アクション名 パラメータ概要 [Delay: n秒]」という形式にまとめ、
    // 一覧を見ただけでおおよその内容が把握できるようにする。
    private string GetActionDisplayText(EventActionEntry act)
    {
        string p = "";
        // Param1が設定されていれば、長すぎる場合は省略しつつ表示に含める
        if (!string.IsNullOrEmpty(act.param1)) p += $" {Truncate(act.param1, 24)}";
        // Param2が設定されていれば、カンマ区切りで続けて表示する
        if (!string.IsNullOrEmpty(act.param2)) p += $", {act.param2}";
        // 遅延時間が設定されていれば末尾に注記する
        if (act.delay > 0) p += $" [Delay: {act.delay}s]";
        return $"◆ {act.action}{p}";
    }

    // 文字列が指定文字数を超える場合、末尾を「…」に置き換えて切り詰める（一覧表示が長くなりすぎないようにする）
    private static string Truncate(string s, int len) => s.Length <= len ? s : s.Substring(0, len) + "…";

    // リストボックスの選択行が変わったときに呼ばれる。選択されたアクションの内容を
    // 右側の詳細パネル（種別プルダウン・Param1/Param2・遅延時間）に反映する。
    private void LstActions_SelectedIndexChanged(object? sender, EventArgs e)
    {
        int idx = _lstActions.SelectedIndex;
        if (idx >= 0 && idx < _actions.Count)
        {
            _pnlParams.Enabled = true;
            // ここから先の値のセットはプログラムによる反映であり、ユーザー入力ではないため
            // Param_Changedが誤って発火・上書きしないようにフラグを立てておく
            _isUpdatingParams = true;

            var act = _actions[idx];
            // 保存されているアクション名からComboBox上のインデックスを探し、見つからなければ先頭を選択
            int aIdx = Array.IndexOf(ActionTypes, act.action);
            _cmbActionType.SelectedIndex = aIdx >= 0 ? aIdx : 0;
            // アクション種別に応じたプルダウン候補・ラベル文言を先に整えてから、実際の値を流し込む
            RefreshParam1Choices(act.action);
            _cmbParam1.Text = act.param1;
            _txtParam2.Text = act.param2;
            _nudDelay.Value = (decimal)act.delay;

            ApplyFieldMeta(act.action);
            _isUpdatingParams = false;
        }
        else
        {
            // 選択が外れた（何も選ばれていない）場合は詳細パネルを編集不可にする
            _pnlParams.Enabled = false;
        }
    }

    // アクション種別プルダウンの選択が変わったときに呼ばれる。
    // 新しい種別に合わせて候補一覧とラベル文言を更新したうえで、値の変更として保存処理も走らせる。
    private void ActionType_Changed(object? sender, EventArgs e)
    {
        if (_cmbActionType.SelectedItem is not string action) return;
        RefreshParam1Choices(action);
        ApplyFieldMeta(action);
        Param_Changed(sender, e);
    }

    // アクション種別に応じてラベル文言・有効/無効・✎ボタンの表示を切り替える（MZのプラグインコマンドUI風）。
    // ActionMetaに定義がない未知の種別が来た場合は、Param1/Param2ともに汎用の自由入力欄として扱う。
    private void ApplyFieldMeta(string action)
    {
        if (!ActionMeta.TryGetValue(action, out var meta)) meta = ("Param 1:", ParamKind.FreeText, "Param 2:", ParamKind.FreeText);

        // Param1側のラベル文言を差し替え、種別がNoneなら入力自体を無効化する
        _lblP1.Text = meta.label1;
        _cmbParam1.Enabled = meta.kind1 != ParamKind.None;
        // 長文（LongText）のときだけ、専用ダイアログを開く✎ボタンを表示する
        _btnEditText.Visible = meta.kind1 == ParamKind.LongText;

        // Param2はそもそも使わないアクションも多いため、種別がNoneならラベルごと非表示にする
        bool hasP2 = meta.kind2 != ParamKind.None;
        _lblP2.Text = meta.label2;
        _lblP2.Visible = hasP2;
        _txtParam2.Visible = hasP2;
    }

    // BGM/SE/敵/ギミック/アイテム/ステージ/コモンイベントのID候補をプルダウンに反映する。
    // Param1が何のIDを期待しているか（ParamKind）に応じて、参照すべきアセット一覧を切り替える。
    private void RefreshParam1Choices(string action)
    {
        if (!ActionMeta.TryGetValue(action, out var meta)) return;
        // 候補を入れ替える前に現在の入力内容を退避しておき、更新後も選択中の値を維持する
        string current = _cmbParam1.Text;
        _cmbParam1.Items.Clear();

        // ParamKindごとに参照するアセット一覧のIDだけを取り出す。
        // SeはPlaySe用の効果音カタログとUI用効果音カタログの両方を候補に含める。
        IEnumerable<string>? ids = meta.kind1 switch
        {
            ParamKind.Bgm => _assets?.Bgm.Select(x => x.id),
            ParamKind.Se => _assets?.Se.Select(x => x.id).Concat(_assets.UiSe.Select(x => x.id)),
            ParamKind.Gimmick => _assets?.Gimmicks.Select(x => x.id),
            ParamKind.Enemy => _assets?.Enemies.Select(x => x.id),
            ParamKind.Item => _assets?.Items.Select(x => x.id),
            ParamKind.Stage => _stageFiles,
            ParamKind.CommonEvent => _assets?.CommonEvents.Select(x => x.id),
            _ => null
        };
        if (ids != null) _cmbParam1.Items.AddRange(ids.ToArray<object>());
        // 退避しておいた値を書き戻し、候補更新によってユーザーの入力内容が消えないようにする
        _cmbParam1.Text = current;
    }

    // Param1のLongText用✎ボタンが押されたときの処理。専用の長文編集ダイアログを開き、
    // OKで確定された場合のみ結果をParam1欄へ反映する（キャンセル時は何もしない）。
    private void BtnEditText_Click(object? sender, EventArgs e)
    {
        using var dlg = new LongTextEditForm(_cmbParam1.Text);
        if (dlg.ShowDialog() == DialogResult.OK)
            _cmbParam1.Text = dlg.ResultText;
    }

    // 詳細パネル内の各入力欄（種別・Param1・Param2・遅延）のいずれかが変更されたときに呼ばれ、
    // 現在選択中のアクションデータへ即座に書き戻す（保存ボタン等を介さず常に同期する方式）。
    private void Param_Changed(object? sender, EventArgs e)
    {
        // プログラムによる値のセット中（行切り替え等）、または何も選択されていない場合は何もしない
        if (_isUpdatingParams || _lstActions.SelectedIndex < 0) return;

        int idx = _lstActions.SelectedIndex;
        var act = _actions[idx];

        act.action = _cmbActionType.SelectedItem?.ToString() ?? ActionTypes[0];
        act.param1 = _cmbParam1.Text;
        act.param2 = _txtParam2.Text;
        act.delay = (float)_nudDelay.Value;

        // データを書き換えたら、一覧側の表示テキストも最新の内容に合わせて更新する
        _lstActions.Items[idx] = GetActionDisplayText(act);
    }

    // 選択中のコマンドを1つ上/下(dir=-1/+1)へ入れ替える。ShowMessage→OpenDoorのように
    // 実行順が結果を左右するコマンド列を、一度削除して末尾に再追加し直さずに調整できるようにする。
    private void MoveSelectedAction(int dir)
    {
        int idx = _lstActions.SelectedIndex;
        if (idx < 0) return;
        int newIdx = idx + dir;
        // 移動先が範囲外（先頭より上、末尾より下）になる場合は何もしない
        if (newIdx < 0 || newIdx >= _actions.Count) return;
        // データリストと表示リストの両方で、現在位置と移動先の要素を入れ替える
        (_actions[idx], _actions[newIdx]) = (_actions[newIdx], _actions[idx]);
        _lstActions.Items[idx] = GetActionDisplayText(_actions[idx]);
        _lstActions.Items[newIdx] = GetActionDisplayText(_actions[newIdx]);
        // 選択状態を移動後の位置に追従させる（ユーザーがどのアクションを動かしたか見失わないように）
        _lstActions.SelectedIndex = newIdx;
    }

    // 選択中のアクションを一覧・データの両方から削除する。
    private void BtnDelAction_Click(object? sender, EventArgs e)
    {
        int idx = _lstActions.SelectedIndex;
        if (idx < 0) return;
        _actions.RemoveAt(idx);
        _lstActions.Items.RemoveAt(idx);
        // 削除後もまだ行が残っていれば、できるだけ同じ位置（末尾を超えないように）の行を選択する。
        // 1件も残っていなければ選択できる行がないので詳細パネルを無効化する。
        if (_lstActions.Items.Count > 0)
            _lstActions.SelectedIndex = Math.Min(idx, _lstActions.Items.Count - 1);
        else
            _pnlParams.Enabled = false;
    }
}

// 長文（メッセージ等）を快適に編集するための簡易ダイアログ。
// 単純なテキスト入力欄に加えて、実際のゲーム画面でどう表示されるかのプレビューを描画する。
internal class LongTextEditForm : Form
{
    // 確定（OK）されたテキストの結果。キャンセル時は初期値のまま変化しない
    public string ResultText { get; private set; }
    private readonly TextBox txt;
    private Panel previewPanel = null!;

    // Feature: UI改善（提案書 EV-4）— ゲーム内メッセージボックスの実寸・実際の描画方式に合わせたプレビュー。
    // DrawPixel.cpp の isShowingMessage 描画ブロックを参照：boxX=20,boxY=SCREEN_HEIGHT-110,boxW=SCREEN_WIDTH-40,boxH=90、
    // かつ DrawString は自動改行を一切行わない単一行描画のため、長い文章は折り返されずボックスからはみ出す。
    // ここでは「折り返されて見える」という誤った期待を与えないよう、あえて折り返さずに実機同様1行で表示し、
    // 収まりきらない場合は明示的に警告する。
    // GameBoxW/GameBoxH: ゲーム内メッセージボックスの想定サイズ（プレビュー描画のスケール計算に使用）
    private const int GameBoxW = 600, GameBoxH = 90;
    private const int EstCharPx = 16; // ShowMessage表示時はSetFontSizeが呼ばれないため、DxLib既定フォントの概算幅(px)を使用

    public LongTextEditForm(string initial)
    {
        ResultText = initial;
        Text = "メッセージ編集";
        Size = new Size(420, 340);
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        FormBorderStyle = FormBorderStyle.FixedDialog;

        // 本文を入力する複数行テキストボックス
        txt = new TextBox
        {
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            Location = new Point(10, 10),
            Size = new Size(384, 140),
            Text = initial,
            Font = new Font("Meiryo UI", 10f)
        };
        // 入力内容が変わるたびにプレビューを再描画し、常に最新の見た目を確認できるようにする
        txt.TextChanged += (s, e) => previewPanel.Invalidate();

        var lblPreviewTitle = new Label
        {
            Text = "🎮 ゲーム内での見え方（実機は自動改行されないため、はみ出す場合があります）",
            Location = new Point(10, 155),
            Size = new Size(384, 16),
            Font = new Font("Meiryo UI", 7.5f),
            ForeColor = Color.DimGray,
        };

        // ゲーム内メッセージボックスを模したプレビュー領域（黒背景でゲーム画面の見た目に近づける）
        previewPanel = new Panel { Location = new Point(10, 173), Size = new Size(384, 96), BorderStyle = BorderStyle.FixedSingle, BackColor = Color.FromArgb(30, 30, 30) };
        previewPanel.Paint += PreviewPanel_Paint;

        var btnOk = new Button { Text = "OK", Location = new Point(214, 280), Size = new Size(90, 30), DialogResult = DialogResult.OK };
        var btnCancel = new Button { Text = "キャンセル", Location = new Point(310, 280), Size = new Size(90, 30), DialogResult = DialogResult.Cancel };
        // OKが押されたときだけ、テキストボックスの内容を結果として確定する
        btnOk.Click += (_, _) => { ResultText = txt.Text; };

        Controls.AddRange(new Control[] { txt, lblPreviewTitle, previewPanel, btnOk, btnCancel });
        AcceptButton = btnOk;
        CancelButton = btnCancel;
    }

    // プレビュー領域の描画処理。実機（DrawPixel.cpp）のメッセージボックスの寸法・描画方式を
    // できるだけ忠実に再現し、折り返しなしの1行表示ではみ出すかどうかを視覚的に確認できるようにする。
    private void PreviewPanel_Paint(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        // プレビュー領域の実際の幅から、ゲーム内ボックス寸法(GameBoxW)に対する拡大縮小率を求める
        float scale = (float)previewPanel.Width / GameBoxW;
        var boxRect = new RectangleF(2, 2, GameBoxW * scale - 4, GameBoxH * scale - 4);
        // ゲーム内と同様、半透明の黒背景に白枠のメッセージボックスを描画する
        using (var bg = new SolidBrush(Color.FromArgb(220, 0, 0, 0))) g.FillRectangle(bg, boxRect);
        g.DrawRectangle(Pens.White, boxRect.X, boxRect.Y, boxRect.Width, boxRect.Height);

        // 改行文字を除去し、実機のDrawStringと同じく改行を無視した1行のテキストとして扱う
        string text = txt.Text.Replace("\r", "").Replace("\n", " ");
        // ボックス内に収まるおおよその文字数を概算し、それを超える場合は「はみ出す」と判定する
        int usableChars = Math.Max(1, (int)((GameBoxW - 24) / (float)EstCharPx));
        bool overflow = text.Length > usableChars;
        // 収まりきらない分は「…」で省略して表示する（実際のゲームでは省略されず切れて見えるが、
        // ここではプレビュー上の可読性を優先して省略表示にしている）
        string shown = overflow ? text.Substring(0, usableChars) + "…" : text;

        using var font = new Font("MS Gothic", 9f);
        g.DrawString(shown, font, Brushes.White, boxRect.X + 8 * scale, boxRect.Y + 8 * scale);

        // はみ出す場合は、想定文字数と現在の文字数を明示した警告文をボックス下部に表示する
        if (overflow)
        {
            using var warnFont = new Font("Meiryo UI", 7.5f, FontStyle.Bold);
            g.DrawString($"⚠ 約{usableChars}文字を超えるとボックスからはみ出します（現在{text.Length}文字・改行は無視されます）",
                warnFont, Brushes.OrangeRed, 2, previewPanel.Height - 16);
        }
    }
}
