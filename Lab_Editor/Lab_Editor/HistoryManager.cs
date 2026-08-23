using System.Collections.Generic;
using Newtonsoft.Json;

namespace Lab_Editor;

// Feature: UI改善（提案書 CUT-1）— 元々StageData専用だったUndo/RedoをT型汎用にし、
// マップ編集以外のエディタ（パーツエディタ等）でも同じ仕組みをそのまま再利用できるようにした。
// JSON往復によるスナップショット方式のため、対象の型は素直なデータクラス（画像等の非シリアライズ
// フィールドを持たない）であれば追加コード無しでそのまま使える。
//
// 仕組みの概要：
// 状態(T)をそのまま保持するのではなく、一度JSON文字列に変換してから履歴リストに積んでいく
// 「スナップショット方式」を採用している。Undo/Redoのたびに文字列からオブジェクトを
// 復元し直すため、参照の使い回しによる意図しない書き換え（片方を直したらもう片方も
// 変わってしまう、といった事故）が起きない安全な設計になっている。
public class HistoryManager<T>
{
    // これまでに記録した状態のスナップショットをJSON文字列のリストとして保持する。
    // 先頭が一番古い状態、末尾が一番新しい状態になる。
    private List<string> history = new List<string>();
    // 現在「どの時点の状態を表示しているか」を指すインデックス。
    // -1は「まだ何も記録されていない」ことを表す。
    private int currentIndex = -1;

    // これより古い状態が存在する（Undoできる）かどうか。currentIndexが0より大きければ、
    // さらに前の要素(history[currentIndex - 1])が存在するのでUndo可能と判断する。
    public bool CanUndo => currentIndex > 0;
    // これより新しい状態が存在する（Redoできる）かどうか。currentIndexが末尾より
    // 手前にあれば、まだ先に進める余地があるということなのでRedo可能と判断する。
    public bool CanRedo => currentIndex < history.Count - 1;

    // 現在の状態を履歴に積む関数。マップ編集などで何か変更が行われるたびに呼び出される想定。
    // state : 記録したい現在の状態（nullの場合は何もしない）
    public void Push(T? state)
    {
        // 記録すべき状態が渡されていない場合は何もせず終了する。
        if (state == null) return;
        // 状態オブジェクトをJSON文字列に変換する。以降はこの文字列を履歴として扱う。
        var json = JsonConvert.SerializeObject(state);

        // 直前に記録した状態と全く同じ内容であれば、重複して履歴に積む必要はないので
        // ここで処理を打ち切る（同じ状態が何個も履歴に並んでしまうのを防ぐため）。
        if (currentIndex >= 0 && history[currentIndex] == json)
            return;

        // 現在位置より後ろ（Undoした後に新しい操作を行った場合の「やり直し先」）に
        // 古いRedo用の履歴が残っていたら、それらは既に無効になるため削除する。
        // これをしないと、Undo後に新しい操作をした際に矛盾した履歴が残ってしまう。
        if (currentIndex < history.Count - 1)
        {
            history.RemoveRange(currentIndex + 1, history.Count - (currentIndex + 1));
        }

        // 新しい状態を履歴の末尾に追加し、現在位置をその末尾に合わせる。
        history.Add(json);
        currentIndex++;

        // 履歴が際限なく増え続けてメモリを圧迫しないよう、上限を50件に制限する。
        // 上限を超えた場合は一番古い履歴を1件削除し、現在位置もそれに合わせてずらす。
        if (history.Count > 50)
        {
            history.RemoveAt(0);
            currentIndex--;
        }
    }

    // ひとつ前の状態に戻す（元に戻す＝Undo）関数。
    // 戻せる状態がない場合はdefault（Tが参照型ならnull）を返す。
    public T? Undo()
    {
        // これ以上戻れない場合は何もせず終了する。
        if (!CanUndo) return default;
        // 現在位置をひとつ手前にずらしてから、その位置のJSON文字列を
        // 元のオブジェクトに復元して呼び出し元に返す。
        currentIndex--;
        return JsonConvert.DeserializeObject<T>(history[currentIndex]);
    }

    // Undoで戻した操作をもう一度やり直す（やり直し＝Redo）関数。
    // やり直せる状態がない場合はdefault（Tが参照型ならnull）を返す。
    public T? Redo()
    {
        // これ以上進められない場合は何もせず終了する。
        if (!CanRedo) return default;
        // 現在位置をひとつ先にずらしてから、その位置のJSON文字列を
        // 元のオブジェクトに復元して呼び出し元に返す。
        currentIndex++;
        return JsonConvert.DeserializeObject<T>(history[currentIndex]);
    }

    // 履歴をすべて消去して、記録前の初期状態に戻す関数。
    // 新しいステージ／データを読み込んだタイミングなどで呼び出される想定。
    public void Clear()
    {
        history.Clear();
        currentIndex = -1;
    }
}

// Form1（ステージ/マップ編集）は従来どおり非ジェネリックの名前で使えるようにするための別名クラス。
// HistoryManager<StageData>をそのまま継承しているだけで、独自の処理は何も追加していない。
// こうしておくことで、既存のコードを書き換えずに`new HistoryManager()`という呼び方を維持できる。
public class HistoryManager : HistoryManager<StageData>
{
}
