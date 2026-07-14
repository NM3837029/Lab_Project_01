using Newtonsoft.Json.Linq;

namespace Lab_Editor;

// ======================================================
// BlockScriptSerializer - BlockInstanceツリー ⇔ JSON AST の相互変換
// Feature: Puzzle-like Behavior Scripting (M6)
//
// ここで生成/解釈するJSON形状は、C++側 BehaviorScript.h のインタプリタが直接解釈する
// 形式と完全に一致させること（EnemyDef.script / GimmickDef.script フィールドの中身）。
// 例:
//   [ { "hat": "OnSpawn", "body": [
//         { "op": "Forever", "body": [
//             { "op": "MoveDirection", "dir": "Toward", "speed": 2 },
//             { "op": "Wait", "frames": 30 }
//         ] }
//   ] } ]
// ======================================================
public static class BlockScriptSerializer
{
    // ── シリアライズ（BlockInstance → JSON） ──────────────

    public static JArray Serialize(List<BlockInstance> topLevelHats)
    {
        var arr = new JArray();
        foreach (var hat in topLevelHats)
        {
            var obj = new JObject { ["hat"] = hat.Def.Op };
            if (hat.Def.HasBody) obj["body"] = SerializeSequence(hat.Body);
            arr.Add(obj);
        }
        return arr;
    }

    private static JArray SerializeSequence(List<BlockInstance> seq)
    {
        var arr = new JArray();
        foreach (var b in seq) arr.Add(SerializeBlock(b));
        return arr;
    }

    private static JObject SerializeBlock(BlockInstance b)
    {
        var obj = new JObject { ["op"] = b.Def.Op };

        foreach (var arg in b.Def.Args)
        {
            if (b.ArgBlocks.TryGetValue(arg.Name, out var nested))
            {
                obj[arg.Name] = SerializeBlock(nested);
                continue;
            }
            if (arg.Type == BlockArgType.BoolSlot) continue; // 空ソケットはキーを出さない（C++側はcontains判定でデフォルト動作）
            if (!b.ArgValues.TryGetValue(arg.Name, out var v)) continue;

            obj[arg.Name] = arg.Type == BlockArgType.Number
                ? JToken.FromObject(System.Convert.ToSingle(v))
                : JToken.FromObject(v?.ToString() ?? "");
        }

        if (b.Def.HasBody) obj["body"] = SerializeSequence(b.Body);
        if (b.Def.HasElse) obj["else"] = SerializeSequence(b.Else);
        return obj;
    }

    // ── デシリアライズ（JSON → BlockInstance） ────────────
    // BlockCatalogに存在しない未知のop/hat名は黙って無視する（将来バージョンとの前方互換や、
    // 手編集での軽微なタイプミスでエディタごとクラッシュしないようにするため）。

    public static List<BlockInstance> Deserialize(JArray? program)
    {
        var result = new List<BlockInstance>();
        if (program == null) return result;

        foreach (var tok in program)
        {
            if (tok is not JObject jo) continue;
            string hatName = jo["hat"]?.ToString() ?? "";
            var def = BlockCatalog.Find(hatName);
            if (def == null) continue;

            var inst = BlockInstance.Create(def);
            if (def.HasBody) inst.Body = DeserializeSequence(jo["body"] as JArray);
            result.Add(inst);
        }
        return result;
    }

    private static List<BlockInstance> DeserializeSequence(JArray? arr)
    {
        var list = new List<BlockInstance>();
        if (arr == null) return list;
        foreach (var tok in arr)
        {
            if (tok is not JObject jo) continue;
            var inst = DeserializeBlock(jo);
            if (inst != null) list.Add(inst);
        }
        return list;
    }

    private static BlockInstance? DeserializeBlock(JObject jo)
    {
        string op = jo["op"]?.ToString() ?? "";
        var def = BlockCatalog.Find(op);
        if (def == null) return null;

        var inst = BlockInstance.Create(def);
        foreach (var arg in def.Args)
        {
            if (!jo.TryGetValue(arg.Name, out var tok)) continue;

            if (tok is JObject nestedObj)
            {
                var nested = DeserializeBlock(nestedObj);
                if (nested != null) inst.ArgBlocks[arg.Name] = nested;
            }
            else if (arg.Type == BlockArgType.Number)
            {
                inst.ArgValues[arg.Name] = tok.Value<float>();
            }
            else
            {
                inst.ArgValues[arg.Name] = tok.Value<string>() ?? "";
            }
        }

        if (def.HasBody) inst.Body = DeserializeSequence(jo["body"] as JArray);
        if (def.HasElse) inst.Else = DeserializeSequence(jo["else"] as JArray);
        return inst;
    }
}
