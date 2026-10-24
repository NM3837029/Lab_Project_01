using System.Drawing.Drawing2D;

namespace Lab_Editor;

// ======================================================
// AssetThumbs - 一覧に出す「小さな絵」を作って覚えておく係
//
// 敵・ギミック・アイテム・タイル・トリガーなどを、右パネルの一覧（CardListView）に絵つきで出すために、
// 定義から正方形の小さな絵（サムネイル）を作る。
//   ・敵／ギミック／アイテム : 本体の絵に、パーツ（黒目・翼・砲身…）があれば重ねて、全体が収まるように縮小する
//   ・タイル               : 絵（タイルセットなら切り出し範囲だけ）を正方形に収める
//   ・絵が無いもの          : 種類の絵文字を、種類の色の四角の上に描く
// 作った絵は覚えておき、同じ物は二度作らない。アセットを読み直したら Clear() で捨てる。
// ======================================================
public sealed class AssetThumbs : IDisposable
{
    // 元の絵ファイルを読み込んだもの（パス → 絵）。読めなかったことも覚えて、何度も探しに行かない。
    private readonly Dictionary<string, Image?> _raw = new();
    // 作ったサムネイル（"種類|ID|大きさ" → 絵）。
    private readonly Dictionary<string, Image> _thumbs = new();

    private readonly string _assetsPath;

    public AssetThumbs(string assetsPath) { _assetsPath = assetsPath; }

    // 覚えている絵をすべて捨てる（アセット定義や絵ファイルを読み直すとき）。
    public void Clear()
    {
        foreach (var i in _thumbs.Values) i.Dispose();
        _thumbs.Clear();
        foreach (var i in _raw.Values) i?.Dispose();
        _raw.Clear();
    }

    // 作ったサムネイルだけを捨てる（元の絵ファイルの記憶は残す）。
    // 編集中の定義の見た目（画像・大きさ・パーツ）が変わったあと、一覧の絵を作り直すときに使う。
    public void ClearThumbs()
    {
        foreach (var i in _thumbs.Values) i.Dispose();
        _thumbs.Clear();
    }

    // 画像ファイルの記憶を1つだけ捨てる（同じ名前で絵を差し替えたとき、次に読み直させる）。
    public void Forget(string? spritePath)
    {
        if (string.IsNullOrEmpty(spritePath)) return;
        if (_raw.Remove(spritePath, out var img)) img?.Dispose();
    }

    public void Dispose() => Clear();

    // 絵ファイルを読み込む。ゲーム本体が同じ絵を読めなくならないよう、メモリへ読み切ってからファイルを離す。
    // ルート基準（"img/xxx.png"）を優先し、無ければ assets 配下も探す（MapCanvas と同じ規則）。
    public Image? Raw(string? spritePath)
    {
        if (string.IsNullOrEmpty(spritePath)) return null;
        if (_raw.TryGetValue(spritePath, out var cached)) return cached;
        Image? img = null;
        string p = Path.Combine(AppPaths.ProjectRoot, spritePath.Replace('/', '\\'));
        if (!File.Exists(p) && !string.IsNullOrEmpty(_assetsPath))
            p = Path.Combine(_assetsPath, spritePath.Replace('/', '\\'));
        if (File.Exists(p))
        {
            try
            {
                using var fs = new FileStream(p, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var src = Image.FromStream(fs);
                img = new Bitmap(src);
            }
            catch { img = null; }
        }
        _raw[spritePath] = img;
        return img;
    }

    // ── 種類ごとの入口 ─────────────────────────────

    public Image ForEnemy(EnemyDef? d, int px)
    {
        if (d == null) return Missing("e?", px);
        return Cached($"e|{d.id}|{px}", () =>
        {
            float s = d.scale > 0 ? d.scale : 1f;
            var body = new SizeF((d.width > 0 ? d.width : 32) * s, (d.height > 0 ? d.height : 32) * s);
            return Compose(d.sprite, body, d.parts, s, AssetIcons.ForEnemy(d.type_enum), UiTheme.KindEnemy, px);
        });
    }

    public Image ForGimmick(GimmickDef? d, int px)
    {
        if (d == null) return Missing("g?", px);
        return Cached($"g|{d.id}|{px}", () =>
        {
            // ギミックは当たり判定の大きさが表示の大きさを兼ねる（MapCanvas と同じ）。
            var body = new SizeF(d.hitboxWidth > 0 ? d.hitboxWidth : 32, d.hitboxHeight > 0 ? d.hitboxHeight : 32);
            return Compose(d.sprite, body, d.parts, 1f, AssetIcons.ForGimmick(d.type_enum), UiTheme.KindGimmick, px);
        });
    }

    public Image ForItem(ItemDef? d, int px)
    {
        if (d == null) return Missing("i?", px);
        return Cached($"i|{d.id}|{px}", () =>
        {
            var body = new SizeF(d.hitboxWidth > 0 ? d.hitboxWidth : 32, d.hitboxHeight > 0 ? d.hitboxHeight : 32);
            return Compose(d.sprite, body, d.parts, 1f, AssetIcons.ForItem(d.type_enum), UiTheme.KindItem, px);
        });
    }

    public Image ForTile(TileDef t, int px)
    {
        return Cached($"t|{t.id}|{px}", () =>
        {
            var bmp = NewCanvas(px);
            using var g = Graphics.FromImage(bmp);
            var raw = Raw(t.sprite);
            if (raw == null)
            {
                // 絵が無いタイルは、タイルの色で塗りつぶす（ゲームの代用表示と同じ）。
                Color c; try { c = ColorTranslator.FromHtml(t.color); } catch { c = Color.Gray; }
                using var br = new SolidBrush(c);
                g.FillRectangle(br, 1, 1, px - 2, px - 2);
                return bmp;
            }
            ApplyPixelArt(g);
            // タイルセット画像なら、指定された切り出し範囲だけを使う。
            var src = (t.srcW > 0 && t.srcH > 0)
                ? new Rectangle(t.srcX, t.srcY, t.srcW, t.srcH)
                : new Rectangle(0, 0, raw.Width, raw.Height);
            src.Intersect(new Rectangle(0, 0, raw.Width, raw.Height));
            if (src.Width <= 0 || src.Height <= 0) src = new Rectangle(0, 0, raw.Width, raw.Height);
            var dst = FitRect(src.Width, src.Height, new RectangleF(1, 1, px - 2, px - 2));
            g.DrawImage(raw, dst, src, GraphicsUnit.Pixel);
            return bmp;
        });
    }

    // 絵文字だけのアイコン（トリガー・スタート・ゴールなど、絵ファイルを持たない物）。
    public Image ForEmoji(string emoji, Color tint, int px)
        => Cached($"x|{emoji}|{tint.ToArgb()}|{px}", () => EmojiTile(emoji, tint, px));

    // 定義が見つからない（削除済みのIDが配置に残っている）ときの目印。
    private Image Missing(string key, int px)
        => Cached($"{key}|{px}", () => EmojiTile("❓", Color.Gray, px));

    // ── 作り方 ──────────────────────────────────

    private Image Cached(string key, Func<Image> make)
    {
        if (_thumbs.TryGetValue(key, out var img)) return img;
        img = make();
        _thumbs[key] = img;
        return img;
    }

    private static Bitmap NewCanvas(int px) => new(px, px, System.Drawing.Imaging.PixelFormat.Format32bppArgb);

    private static void ApplyPixelArt(Graphics g)
    {
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
    }

    // 幅w・高さhの絵を、枠の中へ縦横比を保って中央に収めたときの矩形。
    private static RectangleF FitRect(float w, float h, RectangleF box)
    {
        float k = Math.Min(box.Width / w, box.Height / h);
        return new RectangleF(box.X + (box.Width - w * k) / 2f, box.Y + (box.Height - h * k) / 2f, w * k, h * k);
    }

    // 本体の絵＋パーツを、全体が収まる大きさに縮めて1枚にする。
    // パーツの置き方は MapCanvas.DrawPlacedParts と同じ（本体の左上を原点に、オフセットを倍率ぶん拡大）。
    private Image Compose(string? sprite, SizeF body, List<PartDef>? parts, float partScale, string emoji, Color tint, int px)
    {
        var bodyImg = Raw(sprite);
        var partRects = new List<(PartDef def, Image img, RectangleF rect)>();
        if (parts != null)
        {
            foreach (var p in parts)
            {
                var pi = Raw(p.sprite);
                if (pi == null) continue;
                float pw = (p.width > 0 ? p.width : pi.Width) * p.scale * partScale;
                float ph = (p.height > 0 ? p.height : pi.Height) * p.scale * partScale;
                partRects.Add((p, pi, new RectangleF(p.offsetX * partScale, p.offsetY * partScale, pw, ph)));
            }
        }

        // 本体の絵もパーツの絵も1枚も読めなければ、絵文字の代用表示にする。
        if (bodyImg == null && partRects.Count == 0) return EmojiTile(emoji, tint, px);

        // 全体の外枠（本体＋パーツ）を求め、その外枠が正方形の中へ収まる倍率にする。
        RectangleF union = RectangleF.Empty; bool first = true;
        void Add(RectangleF r) { union = first ? r : RectangleF.Union(union, r); first = false; }
        if (bodyImg != null) Add(new RectangleF(0, 0, body.Width, body.Height));
        foreach (var pr in partRects) Add(pr.rect);
        if (union.Width <= 0 || union.Height <= 0) return EmojiTile(emoji, tint, px);

        float margin = Math.Max(1f, px * 0.04f);
        float k = Math.Min((px - margin * 2) / union.Width, (px - margin * 2) / union.Height);
        float ox = (px - union.Width * k) / 2f - union.X * k;
        float oy = (px - union.Height * k) / 2f - union.Y * k;
        RectangleF Map(RectangleF r) => new(ox + r.X * k, oy + r.Y * k, Math.Max(1f, r.Width * k), Math.Max(1f, r.Height * k));

        var bmp = NewCanvas(px);
        using var g = Graphics.FromImage(bmp);
        ApplyPixelArt(g);

        void DrawParts(bool behind)
        {
            foreach (var (def, img, rect) in partRects)
            {
                if ((def.zOrder < 0) != behind) continue;
                PartColor.TryParse(def.color, out float pr, out float pg, out float pb);
                using var ia = PartColor.MakeAttributes(pr, pg, pb, def.blend == "add" ? 0.8f : 1f);
                var dst = Map(rect);
                if (ia != null) g.DrawImage(img, Rectangle.Round(dst), 0, 0, img.Width, img.Height, GraphicsUnit.Pixel, ia);
                else g.DrawImage(img, dst);
            }
        }

        DrawParts(behind: true);
        if (bodyImg != null) g.DrawImage(bodyImg, Map(new RectangleF(0, 0, body.Width, body.Height)));
        DrawParts(behind: false);
        return bmp;
    }

    // 種類の色の薄い四角の上に、絵文字を1つ描いた絵。
    private static Image EmojiTile(string emoji, Color tint, int px)
    {
        var bmp = NewCanvas(px);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
        using (var br = new SolidBrush(Color.FromArgb(36, tint))) g.FillEllipse(br, 1, 1, px - 2, px - 2);
        using var f = new Font("Segoe UI Emoji", Math.Max(6f, px * 0.42f), FontStyle.Regular, GraphicsUnit.Pixel);
        using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString(emoji, f, Brushes.Black, new RectangleF(0, 0, px, px + 1), sf);
        return bmp;
    }
}
