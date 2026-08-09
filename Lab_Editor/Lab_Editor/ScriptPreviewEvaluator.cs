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
    public struct Pose
    {
        public bool HasOffset;
        public float OffsetX, OffsetY;
        public bool HasAngle;
        public float Angle;
    }

    // scriptのOnSpawnハット以下を辿り、時刻time・パーツ番号partIndexにおける姿勢を求める。
    // 対応する動きが無いスクリプト（静的なパーツ等）の場合はHasOffset/HasAngleが両方falseのまま返る。
    public static Pose Evaluate(JArray? script, float time, int partIndex)
    {
        var pose = new Pose();
        if (script == null) return pose;
        try
        {
            foreach (var tok in script)
            {
                if (tok is not JObject hat) continue;
                if (hat["hat"]?.ToString() != "OnSpawn") continue;
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

    private static void WalkSequence(JArray seq, float time, int partIndex, ref Pose pose)
    {
        foreach (var tok in seq)
        {
            if (tok is not JObject node) continue;
            string op = node["op"]?.ToString() ?? "";
            switch (op)
            {
                case "SetLocalOffset":
                    pose.HasOffset = true;
                    pose.OffsetX = EvalNumber(node["dx"], time, partIndex);
                    pose.OffsetY = EvalNumber(node["dy"], time, partIndex);
                    break;
                case "SetLocalOffsetPolar":
                    {
                        float ang = EvalNumber(node["angle"], time, partIndex);
                        float rad = EvalNumber(node["radius"], time, partIndex);
                        pose.HasOffset = true;
                        pose.OffsetX = MathF.Cos(ang) * rad;
                        pose.OffsetY = MathF.Sin(ang) * rad;
                    }
                    break;
                case "SetAngle":
                    pose.HasAngle = true;
                    pose.Angle = EvalNumber(node["angle"], time, partIndex);
                    break;
                case "Forever":
                case "Repeat":
                case "RepeatUntil":
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

    private static float EvalNumber(JToken? node, float time, int partIndex)
    {
        if (node == null) return 0f;
        if (node.Type == JTokenType.Float || node.Type == JTokenType.Integer) return node.Value<float>();
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
                    float b = EvalNumber(obj["b"], time, partIndex);
                    return b == 0f ? 0f : EvalNumber(obj["a"], time, partIndex) / b;
                }
            case "Sin": return MathF.Sin(EvalNumber(obj["a"], time, partIndex));
            case "Cos": return MathF.Cos(EvalNumber(obj["a"], time, partIndex));
            case "Random": return (EvalNumber(obj["min"], time, partIndex) + EvalNumber(obj["max"], time, partIndex)) / 2f;
            default: return 0f;
        }
    }
}
