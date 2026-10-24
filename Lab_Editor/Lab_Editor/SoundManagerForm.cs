using System.Media;

namespace Lab_Editor;

// ───────────────────────────────────────────────
//  SoundManagerForm - BGM・SE・UI音を登録して管理する画面
//
//  以前は id / name / file … の英語の列が並んだ表で、行ごとに「📁選択」「▶試聴」「🗑削除」ボタンが付いていた。
//  今回は次の作りに改めた。
//    左 : 登録済みの音の一覧（BGM / SE / UI音ごとにまとめ、検索・絞り込みができる）
//    右 : 選んだ音の設定フォーム（ID・名前・種類・ファイル・ループ・音量）と、
//         その音が「どの敵・ギミック・アイテム・コモンイベントで使われているか」
//  元に戻す／やり直し、削除時の参照チェック、SE と UI音の ID 重複の検査など、以前の機能はそのまま引き継ぐ。
//  呼び出し側との約束（コンストラクタの引数と ResultBgm / ResultSe / ResultUiSe）も以前のまま。
// ───────────────────────────────────────────────
public class SoundManagerForm : EditorFormBase
{
    public List<SoundDef> ResultBgm { get; private set; } = new();
    public List<SoundDef> ResultSe { get; private set; } = new();
    public List<SoundDef> ResultUiSe { get; private set; } = new();

    // 編集用の1件分。履歴（元に戻す）に JSON で積むため、公開プロパティだけで持つ。
    public class SoundRow
    {
        public string Category { get; set; } = "BGM"; // "BGM" | "SE" | "UI"
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string File { get; set; } = "";
        public bool IsLoop { get; set; }
        public float Volume { get; set; } = 1.0f;
    }

    private readonly string _projectRoot;
    private readonly string _soundsDir;
    private readonly List<EnemyDef> _enemies;
    private readonly List<GimmickDef> _gimmicks;
    private readonly List<ItemDef> _items;
    private readonly List<CommonEventDef> _commonEvents;

    private List<SoundRow> _rows = new();
    private SoundRow? _cur;
    private readonly Dictionary<SoundRow, string> _keyOf = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<string, SoundRow> _byKey = new();
    private int _keySeq;

    private readonly HistoryManager<List<SoundRow>> _history = new();
    private readonly System.Windows.Forms.Timer _snapTimer = new() { Interval = 500 };
    private SoundPlayer? _player;
    private bool _sync;

    // 画面の部品
    private readonly TextBox _txtSearch = new() { Dock = DockStyle.Fill, PlaceholderText = "🔍 ID・名前で探す", BorderStyle = BorderStyle.FixedSingle };
    private readonly ChipBar _chips = new();
    private readonly CardListView _list = new() { Dock = DockStyle.Fill, LayoutMode = CardLayout.Rows };
    private readonly FieldStack _detail = new() { Dock = DockStyle.Fill };
    private readonly Label _empty = new()
    {
        Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = UiTheme.TextMuted, BackColor = UiTheme.CardBack,
        Text = "左の一覧から音を選ぶか、\r\n下の「＋ BGM / SE / UI音」で追加してください。",
    };
    private readonly Label _warn = new() { AutoSize = false, Height = 40, ForeColor = Color.Firebrick, Font = UiTheme.Small, Tag = "hint" };
    private readonly TextBox _txtId = new() { Font = UiTheme.Base };
    private readonly TextBox _txtName = new() { Font = UiTheme.Base };
    private readonly ComboBox _cmbCategory = new() { DropDownStyle = ComboBoxStyle.DropDownList, Font = UiTheme.Base };
    private readonly Label _lblFile = new() { AutoSize = true, Font = UiTheme.Base };
    private readonly Label _lblFileInfo = new() { AutoSize = true, Font = UiTheme.Small, ForeColor = UiTheme.TextMuted };
    private readonly CheckBox _chkLoop = new() { Text = "ループ再生する（BGM向け）", AutoSize = true, Font = UiTheme.Base };
    private readonly SliderField _volume = new(0, 1, 0.05, 2, "0 ＝ 無音", "1 ＝ 元の音量");
    private readonly Label _lblRefs = new() { AutoSize = true, Font = UiTheme.Base, MaximumSize = new Size(420, 0) };

    private static readonly string[] CatNames = { "BGM", "SE", "UI" };
    private static readonly string[] CatLabels = { "🎵 BGM（背景音楽）", "🔊 SE（効果音）", "🔔 UI音（ボタンの音など）" };

    public SoundManagerForm(string projectRoot, List<SoundDef> bgm, List<SoundDef> se, List<SoundDef> uiSe,
        List<EnemyDef> enemies, List<GimmickDef> gimmicks, List<ItemDef> items, List<CommonEventDef> commonEvents)
        : base("サウンド管理", "BGM・効果音・UI音を登録します。ここで登録した音を、「サウンド割り当て」で敵やギミックに結びつけます。",
               new Size(1100, 700), new Size(900, 580))
    {
        _projectRoot = projectRoot;
        _soundsDir = Path.Combine(projectRoot, "sound");
        _enemies = enemies; _gimmicks = gimmicks; _items = items; _commonEvents = commonEvents;

        _rows.AddRange(bgm.Select(d => ToRow("BGM", d)));
        _rows.AddRange(se.Select(d => ToRow("SE", d)));
        _rows.AddRange(uiSe.Select(d => ToRow("UI", d)));

        BuildUi();
        _history.Push(Snapshot());
        UpdateUndo();
        InitUndo(Undo, Redo);
        UpdateUndo();
        _snapTimer.Tick += (s, e) => { _snapTimer.Stop(); CommitNow(); };

        RebuildList();
        if (_rows.Count > 0) Select(_rows[0]); else ShowDetail(null);
    }

    private static SoundRow ToRow(string category, SoundDef d) => new()
    {
        Category = category, Id = d.id, Name = d.name, File = d.file, IsLoop = d.isLoop, Volume = d.volume,
    };

    private static SoundDef ToDef(SoundRow r) => new() { id = r.Id, name = r.Name, file = r.File, isLoop = r.IsLoop, volume = r.Volume };

    // ── 画面の組み立て ───────────────────────────
    private void BuildUi()
    {
        var left = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.PanelBack };
        var searchHost = new Panel { Dock = DockStyle.Top, Height = UiTheme.S(this, 36), Padding = new Padding(8, 8, 8, 4) };
        searchHost.Controls.Add(_txtSearch);
        _txtSearch.TextChanged += (s, e) => RebuildList();
        _chips.SelectionChanged += (s, e) => RebuildList();
        left.Controls.Add(_list);
        left.Controls.Add(_chips);
        left.Controls.Add(searchHost);
        _list.ItemClicked += (s, item) => { if (_byKey.TryGetValue(item.Key, out var r)) { _cur = r; ShowDetail(r); } };
        _list.DeletePressed += (s, item) => DeleteCurrent();

        var right = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.CardBack };
        right.Controls.Add(_detail);
        right.Controls.Add(_empty);

        var split = new SplitContainer { Dock = DockStyle.Fill, SplitterWidth = 6, FixedPanel = FixedPanel.Panel1, BackColor = UiTheme.CardBorder };
        split.Panel1.Controls.Add(left);
        split.Panel2.Controls.Add(right);
        Body.Controls.Add(split);
        SplitWhenReady(split, 280, 480, 360);

        BuildDetail();

        LeftBar.Controls.Add(Kit.Btn("＋ BGM", () => AddRow("BGM")));
        LeftBar.Controls.Add(Kit.Btn("＋ SE", () => AddRow("SE")));
        LeftBar.Controls.Add(Kit.Btn("＋ UI音", () => AddRow("UI")));
        LeftBar.Controls.Add(Kit.Btn("⧉ 複製", Duplicate));
        LeftBar.Controls.Add(Kit.Btn("🗑 削除", DeleteCurrent, danger: true));
    }

    private void BuildDetail()
    {
        _cmbCategory.Items.AddRange(CatLabels);
        _detail.AddItem(_warn);
        _detail.AddItem(Kit.Field("ID（ゲーム内でこの音を呼ぶための名前。重複不可）", _txtId));
        _detail.AddItem(Kit.Field("表示名", _txtName));
        _detail.AddItem(Kit.Field("種類", _cmbCategory, "SE と UI音は、ゲーム内では同じ名前の置き場を共有します。同じ ID は使えません。"));

        _detail.AddItem(Kit.Heading("🎧 音声ファイル"));
        _detail.AddItem(_lblFile);
        _detail.AddItem(_lblFileInfo);
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 6, 0, 0) };
        row.Controls.Add(Kit.Btn("📁 ファイルを選ぶ", PickFile, primary: true));
        row.Controls.Add(Kit.Btn("▶ 試聴", Play));
        row.Controls.Add(Kit.Btn("■ 止める", () => { _player?.Stop(); }));
        _detail.AddItem(row);
        _detail.AddItem(Kit.Hint("試聴できるのは WAV ファイルだけです。mp3 / ogg もゲームでは鳴りますが、ここでは聞けません。"));

        _detail.AddItem(Kit.Heading("🔊 鳴らし方"));
        _detail.AddItem(_chkLoop);
        _detail.AddItem(Kit.Field("音量", _volume, "試聴には音量は反映されません。ゲームで鳴らしたときに反映されます。"));

        _detail.AddItem(Kit.Heading("📌 この音が使われている場所"));
        _detail.AddItem(_lblRefs);

        _txtId.TextChanged += (s, e) => Edit(r => r.Id = _txtId.Text.Trim());
        _txtName.TextChanged += (s, e) => Edit(r => r.Name = _txtName.Text);
        _cmbCategory.SelectedIndexChanged += (s, e) =>
        {
            if (_sync || _cur == null || _cmbCategory.SelectedIndex < 0) return;
            _cur.Category = CatNames[_cmbCategory.SelectedIndex];
            AfterEdit(immediate: true);
        };
        _chkLoop.CheckedChanged += (s, e) => Edit(r => r.IsLoop = _chkLoop.Checked, immediate: true);
        _volume.ValueChanged += (s, e) => Edit(r => r.Volume = (float)_volume.Value);
    }

    // 入力欄の変更を、選択中の音へ書き戻す。
    private void Edit(Action<SoundRow> apply, bool immediate = false)
    {
        if (_sync || _cur == null) return;
        apply(_cur);
        AfterEdit(immediate);
    }

    private void AfterEdit(bool immediate)
    {
        MarkDirty();
        RebuildList();
        UpdateWarning();
        if (immediate) CommitNow(); else { _snapTimer.Stop(); _snapTimer.Start(); }
    }

    // ── 一覧 ────────────────────────────────────
    private string KeyFor(SoundRow r)
    {
        if (!_keyOf.TryGetValue(r, out var k)) { k = "s" + _keySeq++; _keyOf[r] = k; _byKey[k] = r; }
        return k;
    }

    private static string Emoji(string cat) => cat switch { "BGM" => "🎵", "UI" => "🔔", _ => "🔊" };
    private static Color Tint(string cat) => cat switch { "BGM" => UiTheme.KindTrigger, "UI" => UiTheme.KindItem, _ => UiTheme.KindGimmick };
    private static string GroupName(string cat) => cat switch { "BGM" => "🎵 BGM", "UI" => "🔔 UI音", _ => "🔊 SE（効果音）" };

    private void RebuildList()
    {
        string q = _txtSearch.Text.Trim();
        string chip = _chips.SelectedValue;
        // 絞り込みボタンの件数は、検索に関係なく全体の数にする。
        _chips.SetChips(new[]
        {
            ("all", "すべて"), ("BGM", $"🎵 BGM {_rows.Count(r => r.Category == "BGM")}"),
            ("SE", $"🔊 SE {_rows.Count(r => r.Category == "SE")}"), ("UI", $"🔔 UI音 {_rows.Count(r => r.Category == "UI")}"),
        }, chip.Length > 0 ? chip : "all");
        chip = _chips.SelectedValue;

        var items = new List<CardItem>();
        foreach (var cat in CatNames)
        {
            if (chip != "all" && chip != cat) continue;
            foreach (var r in _rows.Where(x => x.Category == cat))
            {
                if (q.Length > 0 && !r.Id.Contains(q, StringComparison.OrdinalIgnoreCase) && !r.Name.Contains(q, StringComparison.OrdinalIgnoreCase)) continue;
                bool bad = string.IsNullOrWhiteSpace(r.Id) || string.IsNullOrWhiteSpace(r.File);
                items.Add(new CardItem
                {
                    Key = KeyFor(r),
                    Title = (bad ? "⚠ " : "") + (r.Name.Length > 0 ? r.Name : r.Id.Length > 0 ? r.Id : "（名前なし）"),
                    Subtitle = (r.Id.Length > 0 ? r.Id : "IDなし") + " ・ " + (r.File.Length > 0 ? Path.GetFileName(r.File) : "ファイルなし"),
                    Group = GroupName(cat), Accent = Tint(cat), Icon = Kit.Emoji(Emoji(cat), Tint(cat), 40),
                    Tip = $"ID: {r.Id}\nファイル: {r.File}",
                });
            }
        }
        _list.EmptyText = q.Length > 0 ? $"「{q}」に合う音がありません" : "この種類の音はまだありません";
        _list.SetItems(items);
        if (_cur != null) _list.SetSelectedKey(KeyFor(_cur), scrollIntoView: false);
    }

    private void Select(SoundRow r)
    {
        _cur = r;
        _list.SetSelectedKey(KeyFor(r));
        ShowDetail(r);
    }

    private void ShowDetail(SoundRow? r)
    {
        _empty.Visible = r == null;
        _detail.Visible = r != null;
        if (r == null) return;
        _sync = true;
        _txtId.Text = r.Id;
        _txtName.Text = r.Name;
        _cmbCategory.SelectedIndex = Math.Max(0, Array.IndexOf(CatNames, r.Category));
        _chkLoop.Checked = r.IsLoop;
        _volume.Value = r.Volume;
        _sync = false;
        UpdateFileInfo();
        UpdateRefs();
        UpdateWarning();
        _detail.ScrollToTop();
    }

    private void UpdateFileInfo()
    {
        if (_cur == null) return;
        if (string.IsNullOrEmpty(_cur.File)) { _lblFile.Text = "（ファイルが選ばれていません）"; _lblFile.ForeColor = Color.Firebrick; _lblFileInfo.Text = ""; return; }
        string full = Path.Combine(_projectRoot, _cur.File.Replace('/', '\\'));
        bool exists = File.Exists(full);
        _lblFile.Text = Path.GetFileName(_cur.File);
        _lblFile.ForeColor = exists ? UiTheme.TextMain : Color.Firebrick;
        var secs = Kit.WavSeconds(full);
        _lblFileInfo.Text = !exists ? $"⚠ ファイルが見つかりません（{_cur.File}）"
            : $"{_cur.File}" + (secs != null ? $"  ・ 長さ {secs:0.0#}秒" : "") + $"  ・ {new FileInfo(full).Length / 1024.0:0.#} KB";
    }

    private void UpdateRefs()
    {
        if (_cur == null || string.IsNullOrWhiteSpace(_cur.Id)) { _lblRefs.Text = "（IDを入れると、使われている場所が出ます）"; _lblRefs.ForeColor = UiTheme.TextMuted; return; }
        var refs = FindReferences(_cur.Id);
        _lblRefs.Text = refs.Count == 0 ? "どこにも割り当てられていません。\r\n「サウンド割り当て」で敵やギミックに結びつけると、ゲームで鳴ります。"
            : string.Join("\r\n", refs.Take(14).Select(x => "・" + x)) + (refs.Count > 14 ? $"\r\n…ほか {refs.Count - 14} 件" : "");
        _lblRefs.ForeColor = refs.Count == 0 ? UiTheme.TextMuted : UiTheme.TextMain;
    }

    // 入力の問題（未入力・重複）を、フォームの上に赤字で出す。
    private void UpdateWarning()
    {
        if (_cur == null) return;
        var msgs = new List<string>();
        if (string.IsNullOrWhiteSpace(_cur.Id)) msgs.Add("⚠ ID が空です。ID がないと、ゲームからこの音を呼べません。");
        else if (_rows.Count(r => r != _cur && r.Id == _cur.Id && (r.Category == _cur.Category || (r.Category != "BGM" && _cur.Category != "BGM"))) > 0)
            msgs.Add("⚠ 同じ ID の音がほかにもあります。ID は一意にしてください。");
        if (string.IsNullOrWhiteSpace(_cur.File)) msgs.Add("⚠ ファイルが選ばれていません。");
        _warn.Text = string.Join("\r\n", msgs);
        _warn.Visible = msgs.Count > 0;
        _warn.Height = msgs.Count == 0 ? 0 : 20 * msgs.Count + 4;
    }

    // ── 操作 ────────────────────────────────────
    private void AddRow(string category)
    {
        var r = new SoundRow { Category = category, IsLoop = category == "BGM", Volume = 1f };
        _rows.Add(r);
        MarkDirty();
        RebuildList();
        Select(r);
        CommitNow();
        _txtId.Focus();
    }

    // 複製。ID が重ならないよう「_copy」を付け、それでも重なるときは連番にする。
    private void Duplicate()
    {
        if (_cur == null) { MessageBox.Show("複製したい音を、左の一覧から選んでください。", "未選択", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        string baseId = _cur.Id + "_copy";
        var ids = new HashSet<string>(_rows.Select(r => r.Id));
        string id = baseId; int n = 2;
        while (ids.Contains(id)) id = $"{baseId}{n++}";
        var c = new SoundRow { Category = _cur.Category, Id = id, Name = _cur.Name, File = _cur.File, IsLoop = _cur.IsLoop, Volume = _cur.Volume };
        _rows.Insert(_rows.IndexOf(_cur) + 1, c);
        MarkDirty();
        RebuildList();
        Select(c);
        CommitNow();
    }

    // 削除。ほかの場所から使われているときは、先に警告して確認する（鳴るはずの音が無音になるのを防ぐ）。
    private void DeleteCurrent()
    {
        if (_cur == null) return;
        if (!string.IsNullOrWhiteSpace(_cur.Id))
        {
            var refs = FindReferences(_cur.Id);
            if (refs.Count > 0)
            {
                string msg = $"ID「{_cur.Id}」は以下から使われています。削除すると無音になります:\n\n"
                    + string.Join("\n", refs.Take(10)) + (refs.Count > 10 ? $"\n…ほか{refs.Count - 10}件" : "") + "\n\nこのまま削除しますか？";
                if (MessageBox.Show(msg, "確認", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            }
        }
        int i = _rows.IndexOf(_cur);
        _rows.Remove(_cur);
        _cur = null;
        MarkDirty();
        RebuildList();
        if (_rows.Count > 0) Select(_rows[Math.Min(i, _rows.Count - 1)]); else ShowDetail(null);
        CommitNow();
    }

    // 音声ファイルを選び、プロジェクトの sound フォルダへコピーして、この音に結びつける。
    private void PickFile()
    {
        if (_cur == null) return;
        using var dlg = new OpenFileDialog { Title = "音声ファイルを選ぶ", Filter = "音声ファイル|*.wav;*.ogg;*.mp3|すべて|*.*" };
        if (dlg.ShowDialog() != DialogResult.OK) return;
        try
        {
            Directory.CreateDirectory(_soundsDir);
            string dest = Path.Combine(_soundsDir, Path.GetFileName(dlg.FileName));
            if (!string.Equals(dlg.FileName, dest, StringComparison.OrdinalIgnoreCase)) File.Copy(dlg.FileName, dest, overwrite: true);
            _cur.File = "sound/" + Path.GetFileName(dlg.FileName);
            // ID・名前がまだ空なら、ファイル名から仮に入れておく（あとで直せる）。
            string stem = Path.GetFileNameWithoutExtension(dlg.FileName);
            if (string.IsNullOrWhiteSpace(_cur.Id)) _cur.Id = stem;
            if (string.IsNullOrWhiteSpace(_cur.Name)) _cur.Name = stem;
            MarkDirty();
            ShowDetail(_cur);
            RebuildList();
            CommitNow();
        }
        catch (Exception ex)
        {
            MessageBox.Show("ファイルのコピーに失敗しました:\n" + ex.Message, "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // 試聴は WAV のみ（Windows 標準の SoundPlayer の制約）。mp3 / ogg を聞くには外部の音声ライブラリが要るため、今回は見送り。
    private void Play()
    {
        if (_cur == null || string.IsNullOrEmpty(_cur.File)) return;
        string full = Path.Combine(_projectRoot, _cur.File.Replace('/', '\\'));
        if (!File.Exists(full)) { MessageBox.Show("ファイルが見つかりません:\n" + full, "エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        if (!full.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)) { MessageBox.Show("試聴できるのは WAV ファイルだけです。", "試聴できません", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        try
        {
            _player?.Stop(); _player?.Dispose();
            _player = new SoundPlayer(full);
            _player.Play();
        }
        catch (Exception ex) { MessageBox.Show("再生に失敗しました:\n" + ex.Message, "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    // 指定IDの音が、敵・ギミック・アイテム・コモンイベントのどこで使われているか。
    // ※ステージごとのイベントトリガーは対象外（ステージを個別に読む必要があるため）。
    private List<string> FindReferences(string id)
    {
        var refs = new List<string>();
        foreach (var en in _enemies)
        {
            string nm = en.name.Length > 0 ? en.name : en.id;
            if (en.seSpawn == id) refs.Add($"敵「{nm}」の出現音");
            if (en.seAttack == id) refs.Add($"敵「{nm}」の攻撃音");
            if (en.seDamage == id) refs.Add($"敵「{nm}」の被弾音");
            if (en.seDeath == id) refs.Add($"敵「{nm}」の死亡音");
        }
        foreach (var gi in _gimmicks) if (gi.seActivate == id) refs.Add($"ギミック「{(gi.name.Length > 0 ? gi.name : gi.id)}」の起動音");
        foreach (var it in _items) if (it.seCollect == id) refs.Add($"アイテム「{(it.name.Length > 0 ? it.name : it.id)}」の取得音");
        foreach (var ce in _commonEvents)
            foreach (var act in ce.actions)
                if ((act.action == "ChangeBgm" || act.action == "PlaySe") && act.param1 == id)
                    refs.Add($"コモンイベント「{ce.id}」の{(act.action == "ChangeBgm" ? "BGM切り替え" : "SE再生")}");
        return refs;
    }

    // ── 元に戻す／やり直し ───────────────────────
    private List<SoundRow> Snapshot() => _rows.Select(r => new SoundRow
    {
        Category = r.Category, Id = r.Id, Name = r.Name, File = r.File, IsLoop = r.IsLoop, Volume = r.Volume,
    }).ToList();

    private void CommitNow()
    {
        _snapTimer.Stop();
        _history.Push(Snapshot());
        UpdateUndo();
        if (_cur != null) UpdateRefs();
    }

    private void UpdateUndo() => UpdateUndoState(_history.CanUndo, _history.CanRedo);

    private void Restore(List<SoundRow>? rows)
    {
        if (rows == null) return;
        int idx = _cur == null ? 0 : _rows.IndexOf(_cur);
        _rows = rows;
        _keyOf.Clear(); _byKey.Clear();
        _cur = _rows.Count > 0 ? _rows[Math.Clamp(idx, 0, _rows.Count - 1)] : null;
        RebuildList();
        if (_cur != null) Select(_cur); else ShowDetail(null);
        UpdateUndo();
    }

    private void Undo() { _snapTimer.Stop(); CommitNowIfPending(); if (_history.CanUndo) Restore(_history.Undo()); }
    private void Redo() { if (_history.CanRedo) Restore(_history.Redo()); }
    private void CommitNowIfPending() { _history.Push(Snapshot()); }

    // ── 保存 ────────────────────────────────────
    protected override void OnSave()
    {
        // 使っていない空の行（IDもファイルも無い）は保存しない。
        var rows = _rows.Where(r => !(string.IsNullOrWhiteSpace(r.Id) && string.IsNullOrWhiteSpace(r.File))).ToList();
        var bgm = rows.Where(r => r.Category == "BGM").ToList();
        var se = rows.Where(r => r.Category == "SE").ToList();
        var ui = rows.Where(r => r.Category == "UI").ToList();

        string? error = Validate("BGM", bgm) ?? Validate("SE", se) ?? Validate("UI音", ui);
        if (error == null)
        {
            // SE と UI音は、ゲーム側では同じ辞書（seMap）に入る。カテゴリをまたぐ重複があると片方が上書きされて鳴らなくなる。
            var seIds = new HashSet<string>(se.Where(d => !string.IsNullOrWhiteSpace(d.Id)).Select(d => d.Id));
            var cross = ui.Select(d => d.Id).FirstOrDefault(id => !string.IsNullOrWhiteSpace(id) && seIds.Contains(id));
            if (cross != null) error = $"ID「{cross}」が SE と UI音の両方にあります。\nゲーム内では同じ名前の置き場を共有するため、どちらかの ID を変えてください。";
        }
        if (error != null) { MessageBox.Show(error, "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }

        ResultBgm = bgm.Select(ToDef).ToList();
        ResultSe = se.Select(ToDef).ToList();
        ResultUiSe = ui.Select(ToDef).ToList();
        DialogResult = DialogResult.OK;
        Close();
    }

    private static string? Validate(string label, List<SoundRow> defs)
    {
        var seen = new HashSet<string>();
        foreach (var d in defs)
        {
            if (string.IsNullOrWhiteSpace(d.Id) && !string.IsNullOrWhiteSpace(d.File))
                return $"[{label}] ファイル「{d.File}」に ID がありません。\nID がないと、ゲームから呼べない無効なデータになります。";
            if (!string.IsNullOrWhiteSpace(d.Id) && string.IsNullOrWhiteSpace(d.File))
                return $"[{label}] ID「{d.Id}」にファイルが選ばれていません。\n先に「📁 ファイルを選ぶ」で指定してください。";
            if (!string.IsNullOrWhiteSpace(d.Id) && !seen.Add(d.Id))
                return $"[{label}] ID「{d.Id}」が重複しています。ID は一意にしてください。";
        }
        return null;
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _snapTimer.Dispose();
        _player?.Stop(); _player?.Dispose();
        base.OnFormClosed(e);
    }
}
