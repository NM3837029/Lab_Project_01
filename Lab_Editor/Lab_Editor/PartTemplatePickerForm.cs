using Newtonsoft.Json.Linq;

namespace Lab_Editor;

// テンプレートの選択ダイアログ（本実装は次のコミット）。
public class PartTemplatePickerForm : Form
{
    public List<PartDef> ResultParts { get; private set; } = new();
    public bool ReplaceExisting { get; private set; }
    public string ResultTemplateName { get; private set; } = "";
    public string ResultNote { get; private set; } = "";
    public PartTemplatePickerForm(string projectRoot, string baseSpritePath, float bodyW, float bodyH, int existingCount) { }
}
