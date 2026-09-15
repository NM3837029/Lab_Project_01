using Newtonsoft.Json.Linq;

namespace Lab_Editor;

// UI改善（構造改修フェーズ5e）— ページ間の「編集を委ねて結果を受け取る」やり取りを表す共通デリゲート型。
// ShowDialog()のような同期的な戻り値ではなく、コールバックで結果を受け取る形にすることで、
// 呼び出し先が「モーダルFormを開いて結果を受け取る」(既存の薄いラッパー経由の動作)か
// 「WorkbenchShellFormの中でページを切り替えて後でSavedイベントを受け取る」(新しいシェル埋め込み動作)か
// を問わず、PageControl側は同じイベントを発火するだけで済む。
//
// 以下、各編集要求に対応するデリゲート型の定義。
// いずれも「編集対象の情報」＋「編集完了時に結果を受け取るコールバック(onSaved)」という
// 同じ形のシグネチャになっている。

// 当たり判定(Hitbox)編集要求
// fullSpritePath : 対象画像のパス
// offsetX/offsetY/width/height : 編集開始時点でのオフセット・サイズ
// onSaved : 編集確定時に新しいoffsetX/offsetY/width/heightを受け取るコールバック
public delegate void HitboxEditRequestHandler(string fullSpritePath, int offsetX, int offsetY, int width, int height, Action<int, int, int, int> onSaved);

// 表示サイズ編集要求
// fullSpritePath : 対象画像のパス
// initialScale   : 編集開始時点でのスケール値
// logicalW/H     : 論理サイズ（敵の def.width/height）。ゲーム内の見た目の大きさは
//                  「論理サイズ × スケール」で決まるため、正しい px を出すのに要る。
//                  ギミック・アイテムには論理サイズが無いので当たり判定と同じ値を渡す。
// hitboxW/H      : 編集開始時点の当たり判定サイズ
// hasScale       : 倍率(scale)を持つ型か。敵=true、ギミック・アイテム=false
//                  （後者は当たり判定の大きさがそのまま表示サイズを兼ねている）
// onSaved        : 編集確定時に (新しいスケール, 新しい当たり判定幅, 新しい当たり判定高さ) を受け取る。
//                  大きさだけ変えて判定が置き去りになると、見た目とズレた当たり判定ができてしまうため、
//                  当たり判定もまとめて返す形にしてある。
public delegate void SizeEditRequestHandler(string fullSpritePath, float initialScale,
                                            int logicalW, int logicalH,
                                            int hitboxW, int hitboxH, bool hasScale,
                                            Action<float, int, int> onSaved);

// 挙動スクリプト編集要求
// label         : 編集対象を示すラベル文字列
// initialScript : 編集開始時点でのスクリプト(JSON配列)
// onSaved       : 編集確定時に新しいスクリプトを受け取るコールバック
public delegate void BehaviorScriptEditRequestHandler(string label, JArray initialScript, Action<JArray> onSaved);

// パーツ構成編集要求
// label          : 編集対象を示すラベル文字列
// initialParts   : 編集開始時点でのパーツ一覧
// baseSpritePath : ベースとなるスプライト画像のパス
// baseLogicalW/H : 本体の論理サイズ(当たり判定の幅・高さ)。パーツのoffsetやwidth/heightは
//                  この論理座標で解釈されるため、プレビューの倍率計算に必要。0なら画像の原寸で代用する
// onSaved        : 編集確定時に新しいパーツ一覧を受け取るコールバック
public delegate void PartsEditRequestHandler(string label, List<PartDef> initialParts, string baseSpritePath, float baseLogicalW, float baseLogicalH, Action<List<PartDef>> onSaved);

// コモンイベント編集要求
// ev      : 編集対象のコモンイベント定義
// onSaved : 編集確定時に新しい定義を受け取るコールバック
public delegate void CommonEventEditRequestHandler(CommonEventDef ev, Action<CommonEventDef> onSaved);
