using System.Drawing.Drawing2D;

namespace Lab_Editor;

// ======================================================
// BlockCanvasControl - ブロックツリーを組み立てる作業用キャンバス
// Feature: Puzzle-like Behavior Scripting (M4/M5/M7)
//
// M5でドラッグ&ドロップ・スナップ挿入に対応した。
// ・パレットからドラッグ＝新規ブロックをその位置に生成
// ・キャンバス内の既存ブロックをドラッグ＝そのブロックと後続の連結ブロックをまとめて移動
// ・ドロップ位置の判定は「最も内側で該当するコンテナ（Body/Else/トップレベル）を再帰的に探し、
//   そのリスト内でのY座標から挿入インデックスを決める」という単純な方式にしている
//   （ジオメトリの複雑な判定は行わない）。
//
// M7で引数ソケット（式の差し込み口）の編集を仕上げた。
// ・リテラル欄（数値/文字列/ドロップダウン）はクリックでインライン編集できる
// ・レポーター/真偽値ブロックはパレット/キャンバスからドラッグして数値/真偽値ソケットへ差し込める
//   （埋まっているソケットへは直接上書きできない仕様。先に右クリックかDeleteキーで空にする）
// ・右クリックで埋まったソケットを空にできる
// ======================================================
public class BlockCanvasControl : Panel
{
    private readonly Font _font = new Font("Meiryo UI", 9f);
    public List<BlockInstance> TopLevel = new();

    public event Action? ContentChanged;

    // ── ドラッグ状態（列 = Body/Else/トップレベルからつまんだ場合） ──────
    private List<BlockInstance>? _pickedFromList;  // 掴んだ時点で属していたリスト（キャンバス内移動の場合のみ）
    private int _pickedFromIndex;
    private List<BlockInstance>? _draggingChain;   // 現在ドラッグ中のチェーン本体
    private bool _dropHandled;                     // ドロップが成立したか（成立しなければ元の位置へ戻す）
    private HashSet<BlockInstance>? _excluded;      // ドロップ先として無効な対象（ドラッグ中のチェーン＋その子孫）

    // ── ドラッグ状態（引数ソケットからつまんだ場合。M7） ──────────────
    private BlockInstance? _pickedSocketOwner;
    private string? _pickedSocketArgName;
    private BlockInstance? _draggingSocketBlock;

    // ドロップ位置プレビュー（列への挿入）
    private List<BlockInstance>? _previewList;
    private int _previewIndex = -1;
    private Rectangle _previewRect;

    // ドロップ位置プレビュー（ソケットへの差し込み。M7）
    private BlockInstance? _previewSocketOwner;
    private string? _previewSocketArgName;
    private Rectangle _previewSocketRect;

    // インライン編集中のリテラル欄（M7）
    private TextBox? _editBox;
    private BlockInstance? _editingOwner;
    private BlockArgSpec? _editingArg;

    public BlockInstance? SelectedBlock { get; private set; }

    public BlockCanvasControl()
    {
        DoubleBuffered = true;
        AutoScroll = true;
        BackColor = Color.White;
        AllowDrop = true;
        TabStop = true;
        BuildDemoTree();
    }

    // Feature: Puzzle-like Behavior Scripting (M6) — 特定の敵/ギミックのscriptを編集する際に、
    // デモツリーを破棄して実データ（または空）に差し替える
    public void LoadProgram(List<BlockInstance> topLevel)
    {
        TopLevel = topLevel;
        SelectedBlock = null;
        ClearPreview();
        Invalidate();
    }

    // Forever{ MoveDirection(Toward,2); Wait(30); Shoot(angle=DirectionToPlayer,6,1) } を
    // OnSpawnハットの下にネストした初期サンプル（動作確認・ドラッグ練習用に最初から置いてある）
    private void BuildDemoTree()
    {
        var onSpawn = BlockInstance.Create(BlockCatalog.Find("OnSpawn")!);
        var forever = BlockInstance.Create(BlockCatalog.Find("Forever")!);

        var move = BlockInstance.Create(BlockCatalog.Find("MoveDirection")!);
        move.ArgValues["dir"] = "Toward";
        move.ArgValues["speed"] = 2f;

        var wait = BlockInstance.Create(BlockCatalog.Find("Wait")!);
        wait.ArgValues["frames"] = 30f;

        var shoot = BlockInstance.Create(BlockCatalog.Find("Shoot")!);
        shoot.ArgValues["speed"] = 6f;
        shoot.ArgValues["damage"] = 1f;
        shoot.ArgBlocks["angle"] = BlockInstance.Create(BlockCatalog.Find("DirectionToPlayer")!);

        forever.Body.Add(move);
        forever.Body.Add(wait);
        forever.Body.Add(shoot);
        onSpawn.Body.Add(forever);

        TopLevel.Add(onSpawn);
    }

    // ==== レイアウト ====================================================

    private void EnsureLayout(Graphics g)
    {
        int x = 20;
        foreach (var root in TopLevel)
        {
            BlockLayout.Measure(root, g, _font);
            BlockLayout.ArrangeSequence(new List<BlockInstance> { root }, x, 20);
            x += root.Width + 40;
        }
    }

    // ==== 描画 ==========================================================

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TranslateTransform(AutoScrollPosition.X, AutoScrollPosition.Y);
        BlockRenderer.CanvasBackColor = BackColor;

        EnsureLayout(g);

        foreach (var root in TopLevel)
            BlockRenderer.Draw(g, root, _font);

        if (SelectedBlock != null)
        {
            bool isSocketShape = SelectedBlock.Def.Shape == BlockShape.Reporter || SelectedBlock.Def.Shape == BlockShape.Boolean;
            int hlHeight = isSocketShape ? SelectedBlock.Height : BlockLayout.HeaderHeight;
            using var selPen = new Pen(Color.Gold, 2.5f) { DashStyle = DashStyle.Dash };
            g.DrawRectangle(selPen, SelectedBlock.X - 2, SelectedBlock.Y - 2, SelectedBlock.Width + 4, hlHeight + 4);
        }

        if (_previewList != null && _previewIndex >= 0)
        {
            using var previewPen = new Pen(Color.DeepPink, 3f);
            int py = _previewIndex < _previewList.Count ? _previewList[_previewIndex].Y : _previewRect.Y;
            int px = _previewRect.X;
            int pw = System.Math.Max(_previewRect.Width, 60);
            g.DrawLine(previewPen, px, py, px + pw, py);
        }

        if (_previewSocketOwner != null)
        {
            using var socketPen = new Pen(Color.DeepPink, 3f);
            var r = _previewSocketRect;
            r.Inflate(3, 3);
            g.DrawRectangle(socketPen, r);
        }
    }

    // ==== 既存ブロックの選択・ドラッグ開始（キャンバス内） ==============

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        if (e.Button != MouseButtons.Left && e.Button != MouseButtons.Right) return;

        var pt = ToContentPoint(e.Location);

        // 引数ソケット（リテラル欄 or 差し込み済みブロック）にヒットしたか、最優先で調べる
        var argHit = FindArgSocketAt(TopLevel, pt);
        if (argHit != null)
        {
            var (owner, argName, filled) = argHit.Value;

            if (e.Button == MouseButtons.Right)
            {
                // 右クリック：埋まったソケットを空にする（M7）
                if (filled)
                {
                    var removed = owner.ArgBlocks[argName];
                    owner.ArgBlocks.Remove(argName);
                    if (SelectedBlock == removed) SelectedBlock = null;
                    Invalidate();
                    ContentChanged?.Invoke();
                }
                return;
            }

            // 左クリック
            if (filled)
            {
                // 差し込まれているブロックをつまんで再ドラッグする（他のソケットへ移動、またはDelete/右クリックでの削除に備える）
                var nested = owner.ArgBlocks[argName];
                owner.ArgBlocks.Remove(argName);
                SelectedBlock = nested;
                Invalidate();

                _pickedSocketOwner = owner;
                _pickedSocketArgName = argName;
                _draggingSocketBlock = nested;
                _dropHandled = false;
                _excluded = new HashSet<BlockInstance>();
                CollectDescendants(nested, _excluded);

                DoDragDrop(new BlockDragPayload(new List<BlockInstance> { nested }), DragDropEffects.Move);

                if (!_dropHandled && _pickedSocketOwner != null && _pickedSocketArgName != null && _draggingSocketBlock != null)
                {
                    _pickedSocketOwner.ArgBlocks[_pickedSocketArgName] = _draggingSocketBlock;
                }
                _pickedSocketOwner = null;
                _pickedSocketArgName = null;
                _draggingSocketBlock = null;
                _excluded = null;
                ClearPreview();
                Invalidate();
                ContentChanged?.Invoke();
            }
            else
            {
                // 空のリテラル欄：インライン編集を開く（真偽値ソケットは差し込み専用なので何もしない）
                var arg = System.Array.Find(owner.Def.Args, a => a.Name == argName);
                if (arg != null && arg.Type != BlockArgType.BoolSlot)
                {
                    OpenLiteralEditor(owner, arg, owner.ArgSocketRects[argName]);
                }
            }
            return;
        }

        if (e.Button != MouseButtons.Left) return;

        var hit = FindChainAt(TopLevel, pt);
        if (hit == null) { SelectedBlock = null; Invalidate(); return; }

        var (list, index) = hit.Value;
        SelectedBlock = list[index];
        Invalidate();

        // 掴んだブロック以降（下に連結されている分）をまとめてチェーンとして切り離す
        var chain = list.GetRange(index, list.Count - index);
        list.RemoveRange(index, list.Count - index);

        _pickedFromList = list;
        _pickedFromIndex = index;
        _draggingChain = chain;
        _dropHandled = false;
        _excluded = new HashSet<BlockInstance>();
        foreach (var c in chain) CollectDescendants(c, _excluded);

        Invalidate();
        DoDragDrop(new BlockDragPayload(chain), DragDropEffects.Move);

        // ドロップが成立しなかった場合は元の位置へ戻す（データを失わないようにする安全策）
        if (!_dropHandled && _pickedFromList != null)
        {
            _pickedFromList.InsertRange(System.Math.Min(_pickedFromIndex, _pickedFromList.Count), chain);
        }
        _pickedFromList = null;
        _draggingChain = null;
        _excluded = null;
        ClearPreview();
        Invalidate();
        ContentChanged?.Invoke();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Delete && SelectedBlock != null)
        {
            if (RemoveFromTree(TopLevel, SelectedBlock))
            {
                SelectedBlock = null;
                Invalidate();
                ContentChanged?.Invoke();
            }
        }
    }

    // ==== インライン編集（リテラル欄。M7） ================================

    private void OpenLiteralEditor(BlockInstance owner, BlockArgSpec arg, Rectangle contentRect)
    {
        CloseLiteralEditor(commit: false);

        if (arg.Type == BlockArgType.Dropdown)
        {
            using var menu = new ContextMenuStrip();
            foreach (var opt in arg.DropdownOptions ?? System.Array.Empty<string>())
            {
                string capturedOpt = opt;
                var item = new ToolStripMenuItem(opt);
                item.Click += (s, e) =>
                {
                    owner.ArgValues[arg.Name] = capturedOpt;
                    Invalidate();
                    ContentChanged?.Invoke();
                };
                menu.Items.Add(item);
            }
            var screenPt = PointToScreen(new Point(contentRect.X + AutoScrollPosition.X, contentRect.Bottom + AutoScrollPosition.Y));
            menu.Show(screenPt);
            return;
        }

        var rect = new Rectangle(contentRect.X + AutoScrollPosition.X, contentRect.Y + AutoScrollPosition.Y,
            System.Math.Max(contentRect.Width, 40), contentRect.Height);
        _editBox = new TextBox
        {
            Location = rect.Location,
            Size = rect.Size,
            Text = owner.ArgValues.TryGetValue(arg.Name, out var v) ? v?.ToString() ?? "" : "",
            BorderStyle = BorderStyle.FixedSingle,
        };
        _editingOwner = owner;
        _editingArg = arg;
        _editBox.KeyDown += (s, e) =>
        {
            if (e.KeyCode == Keys.Enter) { CloseLiteralEditor(true); e.Handled = true; e.SuppressKeyPress = true; }
            else if (e.KeyCode == Keys.Escape) { CloseLiteralEditor(false); e.Handled = true; }
        };
        _editBox.LostFocus += (s, e) => CloseLiteralEditor(true);
        Controls.Add(_editBox);
        _editBox.BringToFront();
        _editBox.Focus();
        _editBox.SelectAll();
    }

    private void CloseLiteralEditor(bool commit)
    {
        if (_editBox == null) return;
        var box = _editBox;
        _editBox = null; // LostFocus再入防止のため先にnull化してからControls.Removeする

        if (commit && _editingOwner != null && _editingArg != null)
        {
            string text = box.Text;
            if (_editingArg.Type == BlockArgType.Number)
            {
                if (float.TryParse(text, out var f)) _editingOwner.ArgValues[_editingArg.Name] = f;
            }
            else
            {
                _editingOwner.ArgValues[_editingArg.Name] = text;
            }
            ContentChanged?.Invoke();
        }

        Controls.Remove(box);
        box.Dispose();
        _editingOwner = null;
        _editingArg = null;
        Invalidate();
    }

    // ==== ドロップ受け入れ（パレットからの新規、またはキャンバス内移動） ====

    protected override void OnDragEnter(DragEventArgs drgevent)
    {
        base.OnDragEnter(drgevent);
        if (drgevent.Data?.GetDataPresent(typeof(BlockDragPayload)) == true)
            drgevent.Effect = DragDropEffects.Move | DragDropEffects.Copy;
    }

    protected override void OnDragOver(DragEventArgs drgevent)
    {
        base.OnDragOver(drgevent);
        if (drgevent.Data?.GetData(typeof(BlockDragPayload)) is not BlockDragPayload payload) { return; }
        drgevent.Effect = payload.NewFromDef != null ? DragDropEffects.Copy : DragDropEffects.Move;

        var pt = ToContentPoint(PointToClient(new Point(drgevent.X, drgevent.Y)));
        using var g = CreateGraphics();
        EnsureLayout(g);

        var draggedShape = PayloadShape(payload);
        if (draggedShape == BlockShape.Reporter || draggedShape == BlockShape.Boolean)
        {
            var target = FindSocketTargetDeep(TopLevel, pt, _excluded ?? new HashSet<BlockInstance>(), draggedShape.Value);
            _previewSocketOwner = target?.owner;
            _previewSocketArgName = target?.argName;
            _previewSocketRect = target?.rect ?? Rectangle.Empty;
            ClearSequencePreview();
        }
        else
        {
            var (list, index, containerX, containerWidth) = FindDropTargetDeep(TopLevel, pt, _excluded ?? new HashSet<BlockInstance>(),
                new Rectangle(20, 20, System.Math.Max(ClientSize.Width - 40, 200), int.MaxValue / 2));
            _previewList = list;
            _previewIndex = index;
            _previewRect = new Rectangle(containerX, 0, containerWidth, 0);
            ClearSocketPreview();
        }
        Invalidate();
    }

    protected override void OnDragLeave(EventArgs e)
    {
        base.OnDragLeave(e);
        ClearPreview();
        Invalidate();
    }

    protected override void OnDragDrop(DragEventArgs drgevent)
    {
        base.OnDragDrop(drgevent);
        if (drgevent.Data?.GetData(typeof(BlockDragPayload)) is not BlockDragPayload payload) return;

        var draggedShape = PayloadShape(payload);
        if (draggedShape == BlockShape.Reporter || draggedShape == BlockShape.Boolean)
        {
            if (_previewSocketOwner != null && _previewSocketArgName != null)
            {
                BlockInstance toPlace;
                if (payload.NewFromDef != null)
                {
                    toPlace = BlockInstance.Create(payload.NewFromDef);
                }
                else if (payload.ExistingChain != null)
                {
                    toPlace = payload.ExistingChain[0];
                    _dropHandled = true;
                }
                else { ClearPreview(); return; }

                _previewSocketOwner.ArgBlocks[_previewSocketArgName] = toPlace;
                SelectedBlock = toPlace;
            }
            ClearPreview();
            Invalidate();
            ContentChanged?.Invoke();
            return;
        }

        if (_previewList == null || _previewIndex < 0) { ClearPreview(); return; }

        if (payload.NewFromDef != null)
        {
            var fresh = BlockInstance.Create(payload.NewFromDef);
            _previewList.Insert(System.Math.Min(_previewIndex, _previewList.Count), fresh);
            SelectedBlock = fresh;
        }
        else if (payload.ExistingChain != null)
        {
            _previewList.InsertRange(System.Math.Min(_previewIndex, _previewList.Count), payload.ExistingChain);
            _dropHandled = true;
            SelectedBlock = payload.ExistingChain[0];
        }

        ClearPreview();
        Invalidate();
        ContentChanged?.Invoke();
    }

    private static BlockShape? PayloadShape(BlockDragPayload payload)
        => payload.NewFromDef?.Shape ?? payload.ExistingChain?[0].Def.Shape;

    private void ClearPreview()
    {
        ClearSequencePreview();
        ClearSocketPreview();
    }

    private void ClearSequencePreview()
    {
        _previewList = null;
        _previewIndex = -1;
    }

    private void ClearSocketPreview()
    {
        _previewSocketOwner = null;
        _previewSocketArgName = null;
    }

    // ==== ヒットテスト ====================================================

    private Point ToContentPoint(Point clientPt) => new(clientPt.X - AutoScrollPosition.X, clientPt.Y - AutoScrollPosition.Y);

    // キャンバス内の既存ブロックをMouseDownでつまむための検索（Body/Elseも再帰的に見る）
    private (List<BlockInstance> list, int index)? FindChainAt(List<BlockInstance> list, Point pt)
    {
        for (int i = 0; i < list.Count; i++)
        {
            var b = list[i];
            if (b.Def.HasBody)
            {
                var inner = FindChainAt(b.Body, pt);
                if (inner != null) return inner;
            }
            if (b.Def.HasElse)
            {
                var inner = FindChainAt(b.Else, pt);
                if (inner != null) return inner;
            }
            var headerRect = new Rectangle(b.X, b.Y, b.Width, BlockLayout.HeaderHeight);
            if (headerRect.Contains(pt)) return (list, i);
        }
        return null;
    }

    // 引数ソケット（リテラル欄 or 差し込み済みブロック）のヒットテスト。深いネストほど優先して返す（M7）
    private (BlockInstance owner, string argName, bool filled)? FindArgSocketAt(List<BlockInstance> list, Point pt)
    {
        foreach (var b in list)
        {
            var hit = FindArgSocketAtBlock(b, pt);
            if (hit != null) return hit;
        }
        return null;
    }

    private (BlockInstance owner, string argName, bool filled)? FindArgSocketAtBlock(BlockInstance b, Point pt)
    {
        foreach (var c in b.Body)
        {
            var inner = FindArgSocketAtBlock(c, pt);
            if (inner != null) return inner;
        }
        foreach (var c in b.Else)
        {
            var inner = FindArgSocketAtBlock(c, pt);
            if (inner != null) return inner;
        }

        foreach (var arg in b.Def.Args)
        {
            if (!b.ArgSocketRects.TryGetValue(arg.Name, out var rect)) continue;
            bool filled = b.ArgBlocks.TryGetValue(arg.Name, out var nested);
            if (filled)
            {
                var inner = FindArgSocketAtBlock(nested!, pt);
                if (inner != null) return inner;
            }
            if (rect.Contains(pt)) return (b, arg.Name, filled);
        }
        return null;
    }

    // ドロップ可能な「空いている」引数ソケットの検索。型（Number⇔Reporter、BoolSlot⇔Boolean）が
    // 一致するソケットのみを対象にする。埋まっているソケットへは直接ドロップできない（M7）
    private (BlockInstance owner, string argName, Rectangle rect)? FindSocketTargetDeep(
        List<BlockInstance> list, Point pt, HashSet<BlockInstance> excluded, BlockShape draggedShape)
    {
        foreach (var b in list)
        {
            var hit = FindSocketTargetDeepBlock(b, pt, excluded, draggedShape);
            if (hit != null) return hit;
        }
        return null;
    }

    private (BlockInstance owner, string argName, Rectangle rect)? FindSocketTargetDeepBlock(
        BlockInstance b, Point pt, HashSet<BlockInstance> excluded, BlockShape draggedShape)
    {
        if (excluded.Contains(b)) return null;

        foreach (var c in b.Body)
        {
            var inner = FindSocketTargetDeepBlock(c, pt, excluded, draggedShape);
            if (inner != null) return inner;
        }
        foreach (var c in b.Else)
        {
            var inner = FindSocketTargetDeepBlock(c, pt, excluded, draggedShape);
            if (inner != null) return inner;
        }

        foreach (var arg in b.Def.Args)
        {
            if (!b.ArgSocketRects.TryGetValue(arg.Name, out var rect)) continue;
            bool filled = b.ArgBlocks.TryGetValue(arg.Name, out var nested);
            if (filled)
            {
                if (excluded.Contains(nested!)) continue;
                var inner = FindSocketTargetDeepBlock(nested!, pt, excluded, draggedShape);
                if (inner != null) return inner;
                continue; // 埋まっているソケット自体は差し込み先にならない
            }

            bool compatible = (arg.Type == BlockArgType.Number && draggedShape == BlockShape.Reporter)
                            || (arg.Type == BlockArgType.BoolSlot && draggedShape == BlockShape.Boolean);
            if (compatible && rect.Contains(pt)) return (b, arg.Name, rect);
        }
        return null;
    }

    // 子孫（Body/Else/引数ソケットの中身すべて）を再帰的に集める。ドラッグ中のチェーン/ブロックを
    // 自分自身の中にドロップできないようにするための除外集合づくりに使う
    private static void CollectDescendants(BlockInstance b, HashSet<BlockInstance> set)
    {
        set.Add(b);
        foreach (var c in b.Body) CollectDescendants(c, set);
        foreach (var c in b.Else) CollectDescendants(c, set);
        foreach (var kv in b.ArgBlocks) CollectDescendants(kv.Value, set);
    }

    // 最も内側で該当するコンテナ（Body/Else/トップレベル）を再帰的に探し、その中でのY座標から
    // 挿入インデックスを決める。深い階層のコンテナが優先される。
    private (List<BlockInstance> list, int index, int containerX, int containerWidth) FindDropTargetDeep(
        List<BlockInstance> list, Point pt, HashSet<BlockInstance> excluded, Rectangle containerBounds)
    {
        foreach (var b in list)
        {
            if (excluded.Contains(b)) continue;

            if (b.Def.HasBody)
            {
                var bodyRect = new Rectangle(b.X + BlockLayout.CIndent, b.Y + BlockLayout.HeaderHeight,
                    System.Math.Max(b.Width - BlockLayout.CIndent, 40), b.BodyHeight);
                if (bodyRect.Contains(pt))
                    return FindDropTargetDeep(b.Body, pt, excluded, bodyRect);
            }
            if (b.Def.HasElse)
            {
                int elseY = b.Y + BlockLayout.HeaderHeight + b.BodyHeight + BlockLayout.ElseLabelHeight;
                var elseRect = new Rectangle(b.X + BlockLayout.CIndent, elseY,
                    System.Math.Max(b.Width - BlockLayout.CIndent, 40), b.ElseHeight);
                if (elseRect.Contains(pt))
                    return FindDropTargetDeep(b.Else, pt, excluded, elseRect);
            }
        }

        // このコンテナ自身が挿入先。Y座標に最も近い挿入位置（既存ブロックの中央より上か下か）を求める
        int index = list.Count;
        for (int i = 0; i < list.Count; i++)
        {
            if (excluded.Contains(list[i])) continue;
            int mid = list[i].Y + list[i].Height / 2;
            if (pt.Y < mid) { index = i; break; }
        }
        return (list, index, containerBounds.X, containerBounds.Width);
    }

    // ドラッグ操作を伴わない、外部（保存処理等）からの木構造からの除去、およびDeleteキーに使う。
    // Body/Elseのシーケンスだけでなく、引数ソケット（ArgBlocks）に差し込まれたブロックも対象にする（M7）
    private static bool RemoveFromTree(List<BlockInstance> list, BlockInstance target)
    {
        if (list.Remove(target)) return true;
        foreach (var b in list)
        {
            if (RemoveFromTree(b.Body, target)) return true;
            if (RemoveFromTree(b.Else, target)) return true;
            if (RemoveFromArgBlocks(b, target)) return true;
        }
        return false;
    }

    private static bool RemoveFromArgBlocks(BlockInstance owner, BlockInstance target)
    {
        string? foundKey = null;
        foreach (var kv in owner.ArgBlocks)
        {
            if (kv.Value == target) { foundKey = kv.Key; break; }
        }
        if (foundKey != null) { owner.ArgBlocks.Remove(foundKey); return true; }

        foreach (var kv in owner.ArgBlocks)
        {
            if (RemoveFromTree(kv.Value.Body, target)) return true;
            if (RemoveFromTree(kv.Value.Else, target)) return true;
            if (RemoveFromArgBlocks(kv.Value, target)) return true;
        }
        return false;
    }
}
