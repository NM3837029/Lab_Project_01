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

    // 画面上に組み立てられたブロック（帽子型ブロックのリスト＝スクリプト全体）をJSON配列へ変換する。
    // topLevelHats : パレット上に置かれている、最上位（帽子型）のブロック一覧
    public static JArray Serialize(List<BlockInstance> topLevelHats)
    {
        var arr = new JArray();
        foreach (var hat in topLevelHats)
        {
            // 帽子型ブロックは "hat" キーにイベント名（OnSpawn等）を持たせる
            var obj = new JObject { ["hat"] = hat.Def.Op };
            // 本体を持つ帽子型ブロックであれば、その中身も再帰的にシリアライズする
            if (hat.Def.HasBody) obj["body"] = SerializeSequence(hat.Body);
            arr.Add(obj);
        }
        return arr;
    }

    // 縦に連結されたブロック列（本体やelse節の中身）をJSON配列へ変換する。
    private static JArray SerializeSequence(List<BlockInstance> seq)
    {
        var arr = new JArray();
        foreach (var b in seq) arr.Add(SerializeBlock(b));
        return arr;
    }

    // ブロック1つ分をJSONオブジェクトへ変換する（引数・本体・else節も含めて再帰的に処理する）。
    private static JObject SerializeBlock(BlockInstance b)
    {
        // "op" キーにこのブロックの命令名（MoveDirection等）を持たせる
        var obj = new JObject { ["op"] = b.Def.Op };

        foreach (var arg in b.Def.Args)
        {
            if (b.ArgBlocks.TryGetValue(arg.Name, out var nested))
            {
                // このソケットに別のブロック（レポーター/真偽値等）が差し込まれている場合は、
                // それ自体を再帰的にシリアライズしてネストしたJSONオブジェクトとして格納する
                obj[arg.Name] = SerializeBlock(nested);
                continue;
            }
            // 空ソケットはキーを出さない（C++側はcontains判定でデフォルト動作）
            if (arg.Type == BlockArgType.BoolSlot) continue;
            // 値が設定されていない引数はそもそもキー自体を出力しない
            if (!b.ArgValues.TryGetValue(arg.Name, out var v)) continue;

            // 数値型の引数はfloatとして、それ以外（文字列選択等）は文字列として出力する
            obj[arg.Name] = arg.Type == BlockArgType.Number
                ? JToken.FromObject(System.Convert.ToSingle(v))
                : JToken.FromObject(v?.ToString() ?? "");
        }

        // 本体・else節を持つブロック（C型ブロック等）であれば、それらも再帰的にシリアライズする
        if (b.Def.HasBody) obj["body"] = SerializeSequence(b.Body);
        if (b.Def.HasElse) obj["else"] = SerializeSequence(b.Else);
        return obj;
    }

    // ── デシリアライズ（JSON → BlockInstance） ────────────
    // BlockCatalogに存在しない未知のop/hat名は黙って無視する（将来バージョンとの前方互換や、
    // 手編集での軽微なタイプミスでエディタごとクラッシュしないようにするため）。

    // JSON配列（スクリプト全体）を画面表示用のBlockInstanceリストへ変換する。
    // program : パース済みのJSON配列。nullの場合は空リストを返す。
    public static List<BlockInstance> Deserialize(JArray? program)
    {
        var result = new List<BlockInstance>();
        if (program == null) return result;

        foreach (var tok in program)
        {
            // 配列内の要素がオブジェクトでない場合（想定外の形式）はスキップする
            if (tok is not JObject jo) continue;
            string hatName = jo["hat"]?.ToString() ?? "";
            // カタログに存在しない帽子型ブロック名は無視する（前方互換のため）
            var def = BlockCatalog.Find(hatName);
            if (def == null) continue;

            var inst = BlockInstance.Create(def);
            // 本体を持つ定義であれば、"body"配列を再帰的に読み込む
            if (def.HasBody) inst.Body = DeserializeSequence(jo["body"] as JArray);
            result.Add(inst);
        }
        return result;
    }

    // 縦に連結されたブロック列（JSON配列）をBlockInstanceのリストへ変換する。
    private static List<BlockInstance> DeserializeSequence(JArray? arr)
    {
        var list = new List<BlockInstance>();
        if (arr == null) return list;
        foreach (var tok in arr)
        {
            if (tok is not JObject jo) continue;
            var inst = DeserializeBlock(jo);
            // 未知のop名等でnullが返ってきた場合はそのブロックだけをスキップする
            if (inst != null) list.Add(inst);
        }
        return list;
    }

    // ブロック1つ分のJSONオブジェクトをBlockInstanceへ変換する（引数・本体・else節も再帰的に処理）。
    // 該当する定義がカタログに見つからない場合はnullを返す（呼び出し元でスキップされる）。
    private static BlockInstance? DeserializeBlock(JObject jo)
    {
        string op = jo["op"]?.ToString() ?? "";
        var def = BlockCatalog.Find(op);
        if (def == null) return null;

        var inst = BlockInstance.Create(def);
        foreach (var arg in def.Args)
        {
            // JSON側にこの引数名のキーが存在しない場合はスキップ（未設定のまま）
            if (!jo.TryGetValue(arg.Name, out var tok)) continue;

            if (tok is JObject nestedObj)
            {
                // 引数の値がオブジェクトの場合は、別のブロックが差し込まれているということなので
                // 再帰的にデシリアライズしてArgBlocksへ格納する
                var nested = DeserializeBlock(nestedObj);
                if (nested != null) inst.ArgBlocks[arg.Name] = nested;
            }
            else if (arg.Type == BlockArgType.Number)
            {
                // 数値型の引数はfloatとして読み込む
                inst.ArgValues[arg.Name] = tok.Value<float>();
            }
            else
            {
                // それ以外（文字列選択等）は文字列として読み込む
                inst.ArgValues[arg.Name] = tok.Value<string>() ?? "";
            }
        }

        // 本体・else節があれば、それぞれ再帰的にデシリアライズする
        if (def.HasBody) inst.Body = DeserializeSequence(jo["body"] as JArray);
        if (def.HasElse) inst.Else = DeserializeSequence(jo["else"] as JArray);
        return inst;
    }
}
