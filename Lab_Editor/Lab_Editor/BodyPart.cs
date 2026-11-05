using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Lab_Editor;

// ===== 本体パーツ（「画像を選ぶ」で選んだ絵を、パーツ編集の一覧に出す仕組み）=====
//
// 敵・ギミック・アイテムは、「画像を選ぶ」で選んだ1枚の絵を本体として持つ。
// これまで本体の絵はパーツ編集から動かせなかったので、一覧の先頭に「★本体」という行として出し、
// 他のパーツと同じ項目（位置・大きさ・倍率・色・動き・画像の切り替え）で編集できるようにする。
//
// 保存の形（assets/*.json の "body"）と、画面の行の形は少し違う：
//   ・保存      … 「親の中心からのずれ」(offsetX/Y)・大きさ(width/height。0なら素材の箱のまま)・scale・色・動き・画像の切り替え
//   ・画面の行  … 他のパーツと同じ「当たり判定の左上からの左上位置」で見せる（一覧・ドラッグ・数値入力を共通にするため）
// この2つを行き来するのがこのクラスの仕事。何も変えていない本体は null（JSON に body を出さない）に戻すので、
// 開いて閉じただけでは既存のデータは1行も変わらない。
public static class BodyPart
{
    // 一覧の本体の行を見分けるための id。ほかのパーツは使わない名前にしてある
    public const string BodyId = "★本体";

    public static bool IsBody(PartDef p) => p.id == BodyId;

    private static PartDef Clone(PartDef p) => JsonConvert.DeserializeObject<PartDef>(JsonConvert.SerializeObject(p))!;

    // 保存された body（無ければ null）から、パーツ編集の一覧に出す本体の行を作る。
    //   sprite       … 「画像を選ぶ」の絵のパス
    //   hitW/H       … 当たり判定の大きさ（パーツの位置の原点は、この判定の左上）
    //   boxW/H       … 何も指定しなかったときの絵の箱（敵は 幅・高さ、ギミックとアイテムは当たり判定と同じ）
    //   stretch      … ギミック・アイテムは箱いっぱいに伸ばして描く（敵は縦横比を保って収める）
    public static PartDef ToEditRow(PartDef? stored, string sprite, float hitW, float hitH, float boxW, float boxH, bool stretch)
    {
        var row = stored != null ? Clone(stored) : new PartDef();
        int w = row.width > 0 ? row.width : Math.Max(1, (int)MathF.Round(boxW));
        int h = row.height > 0 ? row.height : Math.Max(1, (int)MathF.Round(boxH));
        float scale = row.scale > 0f ? row.scale : 1f;
        float devX = stored?.offsetX ?? 0f, devY = stored?.offsetY ?? 0f;
        row.id = BodyId;
        row.sprite = sprite;
        row.width = w; row.height = h; row.scale = scale;
        // 保存は「中心からのずれ」。画面は左上の位置なので、中心 = 判定の中心 + ずれ から左上を逆算する
        row.offsetX = hitW / 2f + devX - w * scale / 2f;
        row.offsetY = hitH / 2f + devY - h * scale / 2f;
        row.stretch = stretch;
        row.hp = 0; row.deadly = false; row.zOrder = 0;
        row.hitboxOffsetX = 0; row.hitboxOffsetY = 0; row.hitboxWidth = 0; row.hitboxHeight = 0; // 本体は当たり判定を持たない
        row.script = new JArray();
        return row;
    }

    // 編集後の一覧から、本体の行と、それ以外のパーツに分ける。
    // 本体の行が何も変えられていなければ body は null（＝JSON に出さず、従来どおりの描画）。
    public static (PartDef? body, List<PartDef> rest) Split(IEnumerable<PartDef> edited, float hitW, float hitH, float boxW, float boxH)
    {
        var rest = new List<PartDef>();
        PartDef? row = null;
        foreach (var p in edited)
        {
            if (IsBody(p)) row = p; else rest.Add(p);
        }
        if (row == null) return (null, rest);

        int defW = Math.Max(1, (int)MathF.Round(boxW)), defH = Math.Max(1, (int)MathF.Round(boxH));
        float scale = row.scale > 0f ? row.scale : 1f;
        int w = row.width > 0 ? row.width : defW, h = row.height > 0 ? row.height : defH;
        float devX = row.offsetX + w * scale / 2f - hitW / 2f;
        float devY = row.offsetY + h * scale / 2f - hitH / 2f;
        devX = MathF.Round(devX * 100f) / 100f;
        devY = MathF.Round(devY * 100f) / 100f;
        if (MathF.Abs(devX) < 0.05f) devX = 0f;
        if (MathF.Abs(devY) < 0.05f) devY = 0f;

        var body = Clone(row);
        body.id = "body";
        body.sprite = "";
        body.offsetX = devX; body.offsetY = devY;
        body.width = (w == defW) ? 0 : w;
        body.height = (h == defH) ? 0 : h;
        body.scale = scale;
        body.stretch = false; // 描き方は種類で決まる（C++ 側が決める）ので、保存しない
        body.hp = 0; body.deadly = false; body.zOrder = 0;
        body.hitboxOffsetX = 0; body.hitboxOffsetY = 0; body.hitboxWidth = 0; body.hitboxHeight = 0;
        body.script = new JArray();

        bool untouched = devX == 0f && devY == 0f && MathF.Abs(scale - 1f) < 0.0001f && body.width == 0 && body.height == 0
                      && body.motions.Count == 0 && body.sprite_rules.Count == 0
                      && string.IsNullOrEmpty(body.color) && string.IsNullOrEmpty(body.blend);
        return (untouched ? null : body, rest);
    }
}
