namespace Lab_Editor;

// パーツのモーションを評価する（編集画面のプレビュー用）。
//
// ゲーム本体（DrawPixel.cpp の UpdatePartMotions）と同じ式で作ってある。式を変えるときは必ず両方そろえること。
// ゲーム側との違いは2点だけ（プレビューは過去の状態を持たない、ため）：
//   ・状態系トリガーの助走（8フレームで入り切り）は省き、入る/切れるを即座に切り替える
//   ・回転(spin)は積算ではなく「時計 × 角度/周期」で求める（常時回る場合はゲームと同じ値になる）
public static class PartMotionEvaluator
{
    // プレビューで「いま起きていること」。実際の敵の代わりに、画面の操作で与える。
    public sealed class SimState
    {
        // 起きている状態系トリガーの名前（"always" は常に起きているので入れなくてよい）
        public HashSet<string> Active = new();
        // イベント系トリガー（attack / jump / hurt）を最後に発火した時刻（プレビューの時計）。無ければ未発火
        public Dictionary<string, float> EventClock = new();
        // プレイヤー（プレビューの座標。near・プレイヤーを向く、で使う）
        public PointF PlayerPos = new(220f, 60f);
        // 親の中心（near の距離計算に使う）
        public PointF ParentCenter = new(0f, 0f);
    }

    public struct Result
    {
        public float DX, DY;     // 親のローカル空間での位置の上乗せ(px)
        public float Angle;      // 回転の上乗せ（ラジアン）
        public float Scale;      // 大きさの倍率
        public float Alpha;      // 不透明度
        public static Result Identity => new() { Scale = 1f, Alpha = 1f };
    }

    private const float Deg2Rad = 0.017453293f;
    private const float TwoPi = 6.2831853f;

    public static float Wave(float t, int easing)
    {
        float f = t - MathF.Floor(t);
        switch (easing)
        {
            case 1: { float tri = (f < 0.5f) ? (f * 4f - 1f) : (3f - f * 4f); return -tri; }
            case 2: { float v = MathF.Sin(f * TwoPi) * 3f; return v > 1f ? 1f : (v < -1f ? -1f : v); }
            default: return MathF.Sin(f * TwoPi);
        }
    }

    public static float Bump(float t) { float f = t - MathF.Floor(t); return 0.5f - 0.5f * MathF.Cos(f * TwoPi); }

    public static float Thrust(float t)
    {
        float f = t - MathF.Floor(t);
        if (f < 0.25f) { float u = f / 0.25f; return 1f - (1f - u) * (1f - u); }
        float v = (f - 0.25f) / 0.75f;
        return 1f - v * v * (3f - 2f * v);
    }

    // パーツ1つぶんを評価する。
    //   motions    … そのパーツのモーション一覧
    //   partIndex  … 親のパーツ一覧の中の番号（phase_by_index に掛ける）
    //   clock      … プレビューの時計（フレーム）
    //   partCenter … パーツ自身の中心（プレイヤーを向く、で使う）
    //   baseAngle  … スクリプトなどが決めた、パーツ自身の角度（プレイヤーを向く、が「差」を求めるのに使う）
    public static Result Evaluate(IList<PartMotion> motions, int partIndex, float clock, SimState sim,
                                  PointF partCenter, float baseAngle)
    {
        var r = Result.Identity;
        if (motions == null || motions.Count == 0) return r;
        float idx = partIndex;
        float dPlayer = MathF.Sqrt((sim.PlayerPos.X - sim.ParentCenter.X) * (sim.PlayerPos.X - sim.ParentCenter.X)
                                 + (sim.PlayerPos.Y - sim.ParentCenter.Y) * (sim.PlayerPos.Y - sim.ParentCenter.Y));
        for (int k = 0; k < motions.Count; k++)
        {
            var m = motions[k];
            if (!m.enabled) continue;
            bool isEvent = m.trigger is "attack" or "jump" or "hurt";
            float weight = 0f, t = 0f;
            float period = Math.Max(1f, m.period), duration = Math.Max(1f, m.duration);
            if (isEvent)
            {
                if (!sim.EventClock.TryGetValue(m.trigger, out float evClock)) continue;
                float e = clock - evClock - m.delay;
                if (e < 0f || e > duration) continue;
                float u = e / duration;
                weight = (u < 0.8f) ? 1f : (1f - u) / 0.2f;
                t = u + m.phase + m.phase_by_index * idx;
            }
            else
            {
                bool active = m.trigger switch
                {
                    "always" => true,
                    "near" => dPlayer <= m.trigger_param,
                    _ => sim.Active.Contains(m.trigger),
                };
                weight = (active && clock >= m.delay) ? 1f : 0f;
                t = (clock - m.delay) / period + m.phase + m.phase_by_index * idx;
            }

            if (m.kind == "spin")
            {
                // ゲームは積算。ここでは、有効な間は「時計 × 角度/周期」
                r.Angle += (m.amount / period) * Math.Max(0f, clock - m.delay) * weight * Deg2Rad;
                continue;
            }
            if (weight <= 0.0001f) continue;

            float ax = MathF.Cos(m.axis * Deg2Rad), ay = MathF.Sin(m.axis * Deg2Rad);
            switch (m.kind)
            {
                case "sway": { float v = m.amount * Wave(t, m.easing) * weight; r.DX += ax * v; r.DY += ay * v; break; }
                case "swing": r.Angle += m.amount * Deg2Rad * Wave(t, m.easing) * weight; break;
                case "orbit": { float a = t * TwoPi; r.DX += MathF.Cos(a) * m.amount * weight; r.DY += MathF.Sin(a) * m.amount * weight; break; }
                case "pulse": r.Scale *= 1f + m.amount * Bump(t) * weight; break;
                case "slide": { float v = m.amount * Bump(t) * weight; r.DX += ax * v; r.DY += ay * v; break; }
                case "thrust": { float v = m.amount * Thrust(t) * weight; r.DX += ax * v; r.DY += ay * v; break; }
                case "blink": { float f = t - MathF.Floor(t); if (f >= 0.5f) r.Alpha *= 1f - m.amount * weight; break; }
                case "shake":
                {
                    // ゲームと同じ決定的な乱数（時計・パーツ番号・項目番号から作る）
                    unchecked
                    {
                        uint h = (uint)((int)clock * 73856093) ^ (uint)((int)idx * 19349663) ^ (uint)(k * 83492791);
                        h ^= h >> 13; h *= 0x5bd1e995u; h ^= h >> 15;
                        float rx = ((h & 0xFFFFu) / 65535f) * 2f - 1f;
                        float ry = (((h >> 16) & 0xFFFFu) / 65535f) * 2f - 1f;
                        r.DX += rx * m.amount * weight; r.DY += ry * m.amount * weight;
                    }
                    break;
                }
                case "face_player":
                {
                    float target = MathF.Atan2(sim.PlayerPos.Y - partCenter.Y, sim.PlayerPos.X - partCenter.X) + m.amount * Deg2Rad;
                    float diff = target - (baseAngle + r.Angle);
                    while (diff > MathF.PI) diff -= TwoPi;
                    while (diff < -MathF.PI) diff += TwoPi;
                    r.Angle += diff * weight;
                    break;
                }
            }
        }
        r.Scale = Math.Clamp(r.Scale, 0.05f, 5f);
        r.Alpha = Math.Clamp(r.Alpha, 0f, 1f);
        return r;
    }
}
