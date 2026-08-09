using System.Collections.Generic;
using Newtonsoft.Json;

namespace Lab_Editor;

// Feature: UI改善（提案書 CUT-1）— 元々StageData専用だったUndo/RedoをT型汎用にし、
// マップ編集以外のエディタ（パーツエディタ等）でも同じ仕組みをそのまま再利用できるようにした。
// JSON往復によるスナップショット方式のため、対象の型は素直なデータクラス（画像等の非シリアライズ
// フィールドを持たない）であれば追加コード無しでそのまま使える。
public class HistoryManager<T>
{
    private List<string> history = new List<string>();
    private int currentIndex = -1;

    public bool CanUndo => currentIndex > 0;
    public bool CanRedo => currentIndex < history.Count - 1;

    public void Push(T? state)
    {
        if (state == null) return;
        var json = JsonConvert.SerializeObject(state);

        // If the state is identical to the current one, do nothing
        if (currentIndex >= 0 && history[currentIndex] == json)
            return;

        if (currentIndex < history.Count - 1)
        {
            history.RemoveRange(currentIndex + 1, history.Count - (currentIndex + 1));
        }

        history.Add(json);
        currentIndex++;

        if (history.Count > 50)
        {
            history.RemoveAt(0);
            currentIndex--;
        }
    }

    public T? Undo()
    {
        if (!CanUndo) return default;
        currentIndex--;
        return JsonConvert.DeserializeObject<T>(history[currentIndex]);
    }

    public T? Redo()
    {
        if (!CanRedo) return default;
        currentIndex++;
        return JsonConvert.DeserializeObject<T>(history[currentIndex]);
    }

    public void Clear()
    {
        history.Clear();
        currentIndex = -1;
    }
}

// Form1（ステージ/マップ編集）は従来どおり非ジェネリックの名前で使えるようにするための別名クラス。
public class HistoryManager : HistoryManager<StageData>
{
}
