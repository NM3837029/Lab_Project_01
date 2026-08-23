using Newtonsoft.Json.Linq;

namespace Lab_Editor;

// ======================================================
// ScriptPreviewEvaluator - パーツの挙動スクリプトを「時刻t」における姿勢に近似計算する
// Feature: UI改善（提案書 PT-1）— 保存してゲームを起動しなくても、パーツ編集画面の
// 合成プレビュー上で回転・振動などの動きをその場で確認できるようにするための簡易評価器。
//
// C++側 BehaviorInterpreter の完全な再実装ではなく、OnSpawn→Forever（およびIf/Repeat等の
// 単純なネスト）の中にある SetLocalOffset / SetLocalOffsetPolar / SetAngle だけを対象に、
// 「その式を時刻tで評価したら今どこにいるか」を毎回ゼロから計算する“純粋関数”として扱う。
// 条件分岐(If/IfElse)は条件そのものは評価せず、常にthen側（真の場合の中身）を辿る近似とする。
// ======================================================
public static class ScriptPreviewEvaluator
{
    // ある時刻におけるパーツの姿勢（オフセット位置と回転角度）を表す構造体。
    // HasOffset/HasAngleがfalseの場合は、スクリプト側でその要素が指定されていないことを意味する
    // （＝呼び出し側は元のデフォルト値をそのまま使うべき、という合図）。
    public struct Pose
    {
        public bool HasOffset;
        public float OffsetX, OffsetY;
        public bool HasAngle;
        public float Angle;
    }

    // scriptのOnSpawnハット以下を辿り、時刻time・パーツ番号partIndexにおける姿勢を求める。
    // 対応する動きが無いスクリプト（静的なパーツ等）の場合はHasOffset/HasAngleが両方falseのまま返る。
    // script    : パーツに割り当てられた挙動スクリプト（JSON配列。無ければnull）
    // time      : プレビュー上の経過時間（秒）
    // partIndex : このパーツの番号（PartIndexノードの評価に使う）
    public static Pose Evaluate(JArray? script, float time, int partIndex)
    {
        var pose = new Pose();
        if (script == null) return pose;
        try
        {
            foreach (var tok in script)
            {
                // 配列内の要素がオブジェクトでない、または"OnSpawn"ハットでない場合は対象外なのでスキップ
                if (tok is not JObject hat) continue;
                if (hat["hat"]?.ToString() != "OnSpawn") continue;
                // OnSpawnの本体があれば、その中身を辿って姿勢を積み上げていく
                if (hat["body"] is JArray body) WalkSequence(body, time, partIndex, ref pose);
            }
        }
        catch
        {
            // プレビュー用の近似計算なので、想定外の形のスクリプトに遭遇しても例外を投げず
            // 「動きなし」として扱う（本番の実行には一切影響しない）。
        }
        return pose;
    }

    // ブロック列（本体やForever/Repeat等の中身）を先頭から順に辿り、poseを更新していく。
    // seq       : 辿る対象のブロック列（JSON配列）
    // time      : 評価時刻
    // partIndex : パーツ番号
    // pose      : 更新対象の姿勢（参照渡しで直接書き換える）
    private static void WalkSequence(JArray seq, float time, int partIndex, ref Pose pose)
    {
        foreach (var tok in seq)
        {
            if (tok is not JObject node) continue;
            string op = node["op"]?.ToString() ?? "";
            switch (op)
            {
                case "SetLocalOffset":
                    // 直交座標（dx, dy）でオフセットを指定するブロック
                    pose.HasOffset = true;
                    pose.OffsetX = EvalNumber(node["dx"], time, partIndex);
                    pose.OffsetY = EvalNumber(node["dy"], time, partIndex);
                    break;
                case "SetLocalOffsetPolar":
                    {
                        // 極座標（角度＋半径）でオフセットを指定するブロック。直交座標に変換してから適用する。
                        float ang = EvalNumber(node["angle"], time, partIndex);
                        float rad = EvalNumber(node["radius"], time, partIndex);
                        pose.HasOffset = true;
                        pose.OffsetX = MathF.Cos(ang) * rad;
                        pose.OffsetY = MathF.Sin(ang) * rad;
                    }
                    break;
                case "SetAngle":
                    // パーツ自体の回転角度を指定するブロック
                    pose.HasAngle = true;
                    pose.Angle = EvalNumber(node["angle"], time, partIndex);
                    break;
                case "Forever":
                case "Repeat":
                case "RepeatUntil":
                    // 繰り返し系のブロックは、プレビューでは繰り返し回数を気にせず本体を1回だけ辿ればよい
                    // （時刻tにおける瞬間の姿勢を求めたいだけなので、ループの反復自体は意味を持たない）
                    if (node["body"] is JArray innerBody) WalkSequence(innerBody, time, partIndex, ref pose);
                    break;
                case "If":
                case "IfElse":
                    // 条件は評価せず、プレビューの近似として「真の場合」の中身だけを辿る
                    if (node["body"] is JArray thenBody) WalkSequence(thenBody, time, partIndex, ref pose);
                    break;
            }
        }
    }

    // 数値を返す式ノード（JSON）を時刻time・パーツ番号partIndexのもとで評価し、float値として返す。
    // 演算子ノード（Add/Sub等）は再帰的にオペランドを評価してから計算する。
    // node      : 評価対象の式ノード（数値リテラル or 演算子オブジェクト。無ければnull）
    // time      : 評価時刻
    // partIndex : パーツ番号
    private static float EvalNumber(JToken? node, float time, int partIndex)
    {
        if (node == null) return 0f;
        // 数値リテラルであればそのまま返す
        if (node.Type == JTokenType.Float || node.Type == JTokenType.Integer) return node.Value<float>();
        // オブジェクトでない（想定外の型）場合は0として扱う
        if (node is not JObject obj) return 0f;

        string op = obj["op"]?.ToString() ?? "";
        switch (op)
        {
            case "Time": return time;
            case "PartIndex": return partIndex;
            // 親/自分/プレイヤー座標はプレビュー単体では意味を持たないため0として扱う
            case "ParentX": case "ParentY": case "SelfX": case "SelfY":
            case "PlayerX": case "PlayerY": case "DistanceToPlayer": case "DirectionToPlayer":
            case "GetVar":
                return 0f;
            case "Const": return EvalNumber(obj["value"], time, partIndex);
            case "Add": return EvalNumber(obj["a"], time, partIndex) + EvalNumber(obj["b"], time, partIndex);
            case "Sub": return EvalNumber(obj["a"], time, partIndex) - EvalNumber(obj["b"], time, partIndex);
            case "Mul": return EvalNumber(obj["a"], time, partIndex) * EvalNumber(obj["b"], time, partIndex);
            case "Div":
                {
                    // ゼロ除算を避けるため、除数が0の場合は結果を0として扱う
                    float b = EvalNumber(obj["b"], time, partIndex);
                    return b == 0f ? 0f : EvalNumber(obj["a"], time, partIndex) / b;
                }
            case "Sin": return MathF.Sin(EvalNumber(obj["a"], time, partIndex));
            case "Cos": return MathF.Cos(EvalNumber(obj["a"], time, partIndex));
            // 乱数ブロックはプレビューでは毎回結果が変わると分かりづらいため、min/maxの中間値で近似する
            case "Random": return (EvalNumber(obj["min"], time, partIndex) + EvalNumber(obj["max"], time, partIndex)) / 2f;
            // 未対応の演算子ノードは0として扱う（例外は投げない）
            default: return 0f;
        }
    }
}
