using System.Collections.Generic;
using Newtonsoft.Json;

namespace Lab_Editor;

public class HistoryManager
{
    private List<string> history = new List<string>();
    private int currentIndex = -1;

    public bool CanUndo => currentIndex > 0;
    public bool CanRedo => currentIndex < history.Count - 1;

    public void Push(StageData stage)
    {
        if (stage == null) return;
        var json = JsonConvert.SerializeObject(stage);
        
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

    public StageData? Undo()
    {
        if (!CanUndo) return null;
        currentIndex--;
        return JsonConvert.DeserializeObject<StageData>(history[currentIndex]);
    }

    public StageData? Redo()
    {
        if (!CanRedo) return null;
        currentIndex++;
        return JsonConvert.DeserializeObject<StageData>(history[currentIndex]);
    }

    public void Clear()
    {
        history.Clear();
        currentIndex = -1;
    }
}