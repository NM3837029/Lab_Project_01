# -*- coding: utf-8 -*-
"""レイヤー（層）の見本ステージ puzzle_7_layer.json を生成する。

使い方（リポジトリのルートで）:
    python tools/gen_layer_stage.py            # 検査して assets/stages/puzzle_7_layer.json を書き出す
    python tools/gen_layer_stage.py --map      # 道を文字で表示する（書き出さない）

・assets/gimmicks.json などの部品定義は書き換えない（既存の部品だけで作れるため）。
・assets/game_config.json のステージ一覧には、無ければ1件だけ足す（あれば item_total だけ合わせる）。
・道具は tools/puzzle_lib.py。ステージの作りの約束（説明の窓を出さない・途切れない1本の道・チェックポイント・
  重要な物は編集ロック）は他のパズルと同じ。

【ステージの考え方】
層は 奥(-1) / 中(0) / 手前(+1)。「同じ層の物どうしだけが当たる」ので、道をふさぐ物が居る層を避ければ、すり抜けられる。
円形メニューの「レイヤー」は 中 → 手前 → 奥 → 中 の順に1つずつ進むので、各区間が求める層を
  手前(1) → 奥(-1) → 中(0) → 手前(1) → 奥(-1) → 中(0)
の順にしておく。つまりどの区間も「レイヤーを1回押す」だけで次の道が開く（戻すと2回かかる）。
どの区間も「居てはいけない層」を2つ持たせて、通れる層が1つに決まるようにしてある
（チェックポイントは同じ層でないと触れないので、区間の終わりで自分の層が決まっている必要がある）。

  区間          ふさぐ物（置いた層）                    通れる層
  ① 棘の帯      棘が 中・奥                              手前
  ② 棘の帯      棘が 中・手前                            奥
  ③ 棘の帯      棘が 手前・奥                            中
  ④ 岩の壁      岩が 中・奥（5段積み。跳び越せない）      手前
  ⑤ 穴          橋（足場）が 奥にだけある                奥
  ⑥ 棘の帯＋砲台 棘が 奥、砲台（弾）が 手前               中
"""
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from puzzle_lib import *  # noqa: E402,F401,F403
import puzzle_lib as L    # noqa: E402

DEFS = Defs()             # 読むだけ。ensure_defs は呼ばない（部品定義を書き換えない）

BACK, MID, FRONT = -1, 0, 1
ALL_LAYERS = (BACK, MID, FRONT)

# 区間ごとの「通れる層」。検査（層ごとの到達）と、チェックポイントの層に使う
SEG_PASS = {"band1": FRONT, "band2": BACK, "band3": MID, "wall": FRONT, "bridge": BACK, "finale": MID}


def stage7():
    s = Stage("puzzle_7_layer.json", "レイヤー", "bgm_editing_05", "#DCD3F2", 128,
              tools(object=True), ALL_PLAYER_LOCKS, cost(regenPerSec=8.0), DEFS)
    s.embed_ok = ("gim_scalable_ground",)
    s.start = (2 * TILE, 0.0)
    s.seg = {}                                   # 区間名 -> (列0, 列1)。検査が、その区間にある物を調べるのに使う

    def layered(g, layer):
        if layer != MID:
            g["layer"] = layer
        return g

    def cp(col, layer):
        g = s.gim("gimmick_checkpoint", col * TILE + 8, s.floor_y(col) - 64)
        return layered(g, layer)

    def spike_band(name, c0, c1, passable):
        """c0〜c1 の床いっぱいに棘を敷く。通れる層 passable 以外の層ごとに、1列に1本ずつ置く"""
        s.seg[name] = (c0, c1)
        for c in range(c0, c1 + 1):
            for lay in ALL_LAYERS:
                if lay != passable:
                    layered(s.gim("gim_spikes", c * TILE, GROUND_Y - 32, lock=locks()), lay)

    # ---- 地形：ほぼ平ら。⑤の穴だけ
    s.pit(72, 81)

    # ---- スタート：違う層の敵が、すり抜けて歩いている（半透明＝「別の層」の見せ方を最初に見せる）
    cp(12, MID)
    walker = s.enemy_on("enemy_aruku", 6, patrol=(3, 11))
    layered(walker, FRONT)

    # ---- ① 棘の帯（8マス＝跳び越せない）。ここで初めて自分の層を変える。中のままだと刺さる
    spike_band("band1", 18, 25, SEG_PASS["band1"])
    cp(29, SEG_PASS["band1"])

    # ---- ② 次の層（奥）。帯に刺さる層が変わる
    spike_band("band2", 32, 39, SEG_PASS["band2"])
    cp(43, SEG_PASS["band2"])

    # ---- ③ もう一度進めると、中に戻る
    spike_band("band3", 46, 53, SEG_PASS["band3"])
    cp(57, SEG_PASS["band3"])

    # ---- ④ 岩の壁（5段積み。跳び越せない）。通れる層に居れば、壁の中を歩いて抜けられる
    s.seg["wall"] = (62, 63)
    for k in range(5):
        for lay in ALL_LAYERS:
            if lay != SEG_PASS["wall"]:
                layered(s.gim("gim_rock", 62 * TILE, GROUND_Y - 48 * (k + 1), lock=locks()), lay)
    cp(67, SEG_PASS["wall"])

    # ---- ⑤ 穴に、奥の層にだけある橋（半透明に見えているのは別の層の足場）。奥に居ないと落ちる
    # 足場は 128px の部品を3枚つなぐ（1つを拡大すると、Lab_Editor で開いて保存したときに縦まで伸びてしまうため）
    s.seg["bridge"] = (72, 81)
    for k in range(3):
        layered(s.gim("gim_scalable_ground", 71 * TILE + 128 * k, GROUND_Y, lock=locks()), SEG_PASS["bridge"])
    s.solved_adds.append((71 * TILE, GROUND_Y, 384, 32))
    cp(86, SEG_PASS["bridge"])

    # ---- ⑥ 仕上げ：棘は奥、砲台の弾は手前。どちらにも当たらないのは中だけ
    spike_band("finale", 92, 99, SEG_PASS["finale"])
    cannon = s.enemy_on("enemy_taihou", 108, lock=locks())
    layered(cannon, FRONT)

    # ---- コイン（アイテムは層に関係なく取れる）
    s.coins([5, 9, 21, 29, 36, 43, 50, 57, 66, 86, 96, 112, 118], dy=40)
    s.coin_at(77, 12)                            # 橋の上（穴の上なので、床からの高さではなく行で置く）

    s.set_goal(120)
    return s


# ====================================================================== 層ごとの検査
def seg_of(s, g):
    """ギミック g がどの区間（band1 など）に居るか。区間の外（チェックポイントなど）は None"""
    cx = int(g["x"] // TILE)
    return next((n for n, (a, b) in s.seg.items() if a - 1 <= cx <= b + 1), None)


def solved_view(s, data):
    """全区間で「通れる層」に居るときに、当たる物だけを残したコピー（棘・岩は残らず、橋だけが残る）"""
    v = dict(data)
    v["gimmicks"] = [g for g in data["gimmicks"]
                     if seg_of(s, g) is None or g.get("layer", 0) == SEG_PASS[seg_of(s, g)]]
    return v


def check_layers(s, data):
    """層を使った遊びになっているかを、物理で調べる。ミスがあれば文字列の一覧を返す。

    ① 全区間で「通れる層」に居る前提なら、ゴールへ行ける（＝解ける）
    ② 層を一度も変えない（ずっと中）なら、①の棘の帯より先へは行けない（＝層を使わないと解けない）
    ③ どの区間も、通れる層以外の2層では、その区間を越えられない（＝通れる層は1つに決まる）
    """
    errs = []
    ok, pos = L.Reach(s, solved_view(s, data), solved=True).run()
    if not ok:
        errs.append("通れる層に居ても、ゴールへ行けない（最も右 x=%.0f）" % pos[0])

    saved = s.solved_adds
    s.solved_adds = []                                   # 橋は奥の層にしか無いので、以降の検査では足場に数えない
    try:
        v0 = dict(data)
        v0["gimmicks"] = [g for g in data["gimmicks"] if g.get("layer", 0) == MID]
        ok0, p0 = L.Reach(s, v0, solved=True).run()
        if ok0 or p0[0] > s.seg["band1"][0] * TILE + 4:
            errs.append("層を変えなくても進めてしまう（中のままで x=%.0f まで行ける）" % p0[0])

        for name, (c0, c1) in s.seg.items():
            for lay in ALL_LAYERS:
                if lay == SEG_PASS[name]:
                    continue
                v = dict(data)
                v["gimmicks"] = [g for g in data["gimmicks"] if seg_of(s, g) == name and g.get("layer", 0) == lay]
                okk, pk = L.Reach(s, v, solved=True).run()
                if okk or pk[0] > (c1 + 1) * TILE + 4:
                    errs.append("区間 %s が、通れる層（%d）以外の層 %d でも通れてしまう（x=%.0f まで行ける）"
                                % (name, SEG_PASS[name], lay, pk[0]))
    finally:
        s.solved_adds = saved
    return errs


def main():
    want_map = "--map" in sys.argv
    s = stage7()
    data = s.to_json()
    # 他のパズルと同じく、飛び道具の使い方に頼らない（弾で敵を倒して層を避けて通る抜け道を作らない）
    data["player_capabilities"]["canShootFireball"] = False
    if want_map:
        print(L.preview(s, data))
        return 0

    # 既存の検査（足元・埋まり・説明の窓など）。到達の検査は層を知らないので、層に対応したものに差し替える
    L.reach_report = lambda st, d: L.Reach(st, solved_view(st, d), solved=True).run()
    ok = L.validate(s, data)
    errs = check_layers(s, data)
    for e in errs:
        print("  -", e)
    if not ok or errs:
        print("【検査NG】")
        return 1

    path = os.path.join(STAGE_DIR, s.file)
    with open(path, "w", encoding="utf-8", newline=chr(10)) as f:
        json.dump(data, f, ensure_ascii=False, indent=2)
    n = len(data["items"])
    print("書き出し:", s.file, "「パズル7：レイヤー」", "item_total=%d" % n)
    register(s.file, "パズル7：レイヤー", n)
    return 0


def register(file, name, item_total):
    """assets/game_config.json のステージ一覧に、無ければ末尾へ1件足す。あれば item_total だけ合わせる。
    ファイル全体を書き直すと他の整形が変わるので、文字列の差し込みだけで済ませる"""
    path = os.path.join(ASSET_DIR, "game_config.json")
    with open(path, "r", encoding="utf-8", newline="") as f:
        text = f.read()
    cfg = json.loads(text)
    stages = cfg["stages"]
    if any(st["file"] == file for st in stages):
        for st in stages:
            if st["file"] == file and st["item_total"] != item_total:
                old = '"file": "%s",' % file
                i = text.index(old)
                j = text.index('"item_total":', i)
                k = j + len('"item_total":')
                while text[k] in " 0123456789":              # 数字の終わりまで（改行コードに依らず、値だけを差し替える）
                    k += 1
                text = text[:j] + '"item_total": %d' % item_total + text[k:]
                with open(path, "w", encoding="utf-8", newline="") as f:
                    f.write(text)
                print("game_config.json の item_total を合わせました")
        return
    nl = chr(13) + chr(10) if chr(13) in text else chr(10)    # ファイルの改行（CRLF のことがある）に合わせる
    last = stages[-1]
    anchor = '"file": "%s",' % last["file"]
    i = text.index(anchor)
    tail = nl + "    }" + nl + "  ],"
    close = text.index(tail, i)                              # ステージ配列の最後の要素の閉じ
    entry = nl.join([
        ",", "    {", '      "file": "%s",' % file, '      "name": "%s",' % name, '      "thumbnail": "",',
        '      "order": %d,' % (max(st["order"] for st in stages) + 1), '      "unlock": "always",',
        '      "require": [],', '      "item_total": %d' % item_total, "    }"])
    cut = close + len(nl + "    }")
    text = text[:cut] + entry + text[cut:]
    json.loads(text)                                         # 差し込み後も正しいJSONか
    with open(path, "w", encoding="utf-8", newline="") as f:
        f.write(text)
    print("game_config.json のステージ一覧に足しました")


if __name__ == "__main__":
    sys.exit(main())
