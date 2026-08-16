using Newtonsoft.Json.Linq;

namespace Lab_Editor;

// UI改善（構造改修フェーズ5e）— ページ間の「編集を委ねて結果を受け取る」やり取りを表す共通デリゲート型。
// ShowDialog()のような同期的な戻り値ではなく、コールバックで結果を受け取る形にすることで、
// 呼び出し先が「モーダルFormを開いて結果を受け取る」(既存の薄いラッパー経由の動作)か
// 「WorkbenchShellFormの中でページを切り替えて後でSavedイベントを受け取る」(新しいシェル埋め込み動作)か
// を問わず、PageControl側は同じイベントを発火するだけで済む。
public delegate void HitboxEditRequestHandler(string fullSpritePath, int offsetX, int offsetY, int width, int height, Action<int, int, int, int> onSaved);
public delegate void SizeEditRequestHandler(string fullSpritePath, float initialScale, Action<float> onSaved);
public delegate void BehaviorScriptEditRequestHandler(string label, JArray initialScript, Action<JArray> onSaved);
public delegate void PartsEditRequestHandler(string label, List<PartDef> initialParts, string baseSpritePath, Action<List<PartDef>> onSaved);
public delegate void CommonEventEditRequestHandler(CommonEventDef ev, Action<CommonEventDef> onSaved);
