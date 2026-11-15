# -*- coding: utf-8 -*-
"""あわせ技のステージ puzzle_8_mix.json を生成する。

使い方（リポジトリのルートで）:
    python tools/gen_mix_stage.py            # 検査して assets/stages/puzzle_8_mix.json を書き出す
    python tools/gen_mix_stage.py --map      # 道を文字で表示する（書き出さない）

・使う部品は、すでに assets/*.json にあるものだけ（部品の定義は書き換えない）。
・assets/game_config.json のステージ一覧には、無ければ1件だけ足す（あれば item_total だけ合わせる）。
・道具は tools/puzzle_lib.py。作りの約束（説明の窓を出さない・途切れない1本の道・見せ場の手前にチェックポイント・
  重要な物は、解き方に使わない編集をロックする）は、ほかのパズルと同じ。

【ステージの考え方】
拡大縮小・回転・色の操作と、ドッスン・機械芋虫・もろい土・ぶ厚い壁・色で乗れる足場（色ロック足場）を、
1本の道に5つの見せ場として並べる。後ろへ行くほど、組み合わせる要素が増える。

  見せ場            使うもの                          解き方
  ① 三色の橋        色・色ロック足場                  赤→緑→青の足場が切れ目なく並ぶ。歩きながらTで色を切り替えて渡る
  ② 短い青の足場    拡大・色・色ロック足場            青の足場が3マスしかない。横に伸ばして（ホイール/つまみ）穴の向こうまで届かせる
  ③ ドッスンの谷    ドッスン・回転・拡大・色・もろい土 緑の橋の上でドッスンが落ちてくる。横に向けて1.3倍にすると、
                                                      向こうのもろい土の壁へ突っ込んで割る
  ④ 芋虫の部屋      機械芋虫・回転・拡大・ぶ厚い壁    芋虫は上下に往復している。横に回して1.5倍にすると、ぶ厚い壁を割って抜ける。
                                                      割れるのは回した瞬間の芋虫の高さの段だけ（どの高さでも通れるようにしてある）
  ⑤ 台地の栓        色・ドッスン・拡大・もろい土      赤の橋で台地へ。上の道は石の壁でふさがれている。
                                                      ドッスンを1.3倍にして、もろい土の栓を真下へ叩き割り、下の抜け道へ降りる

もろい土（タイル15）は「1.3倍以上の叩きつけ」、ぶ厚い壁（タイル17）は「1.5倍以上の体当たり」でしか崩れない。
どちらも tiles.json の既存の設定のまま使う（大きさが足りないと、そのマスに「○倍以上で壊せる」と出る）。
"""
import json
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from puzzle_lib import *  # noqa: E402,F401,F403
import puzzle_lib as L    # noqa: E402
from gen_layer_stage import register  # noqa: E402  ステージ一覧への登録は、レイヤーの見本と同じ処理を使い回す

DEFS = Defs()             # 読むだけ。ensure_defs は呼ばない（部品定義を書き換えない）

FILE = "puzzle_8_mix.json"
NAME = "パズル8：あわせ技"
RED, GREEN, BLUE = "1", "2", "3"     # 色ロック足場の param（プレイヤーのTキーの色の番号と同じ）
LOCK_W = 96                          # 色ロック足場1枚の横幅(px)

# 見せ場ごとの「解いたあとの状態」。検査で、見せ場を1つずつ「解かない」状態にして、
# その見せ場を解かないとゴールへ行けないこと（＝編集が必須であること）を確かめるために、名前を付けて分けて持つ。
#   adds … 解いたあとに足場になる矩形 (x, y, w, h) px
#   cuts … 解いたあとに消える地形 (列0, 列1, 行0, 行1)
PIECES = {}


def piece(name):
    """見せ場 name の「解いたあと」の入れ物を作って返す"""
    PIECES[name] = {"adds": [], "cuts": []}
    return PIECES[name]


def stage8():
    s = Stage(FILE, "あわせ技", "bgm_editing_08", "#E2EAD6", 200,
              tools(screen=True, object=True), ALL_PLAYER_LOCKS, cost(regenPerSec=10.0), DEFS)
    s.start = (2 * TILE, 0.0)

    def lock_row(c0, colors, **lock_kw):
        """列 c0 から、地面の高さに色ロック足場を横へ並べる。colors は1枚ずつの色。
        穴の両端の地面と上面がそろうので、乗ったまま歩いて渡れる"""
        out = []
        for k, col in enumerate(colors):
            out.append(s.gim("gim_color_lock", c0 * TILE + LOCK_W * k, GROUND_Y, param=col, lock=lock_kw.get("lock", locks())))
        return out

    # ================================================================ 序盤：ふつうの道
    s.ramp(12, 16, 13, 11)                       # 丘
    s.hill(17, 21, 11)
    s.ramp(22, 26, 11, 13)
    s.plat(14, 16, 8)
    s.pit(29, 31)                                # 小さな穴（ふつうに跳ぶ）

    # ================================================================ ① 三色の橋
    # 穴（9マス）に、赤・緑・青の足場が切れ目なく並ぶ。T を押すたびに 赤→緑→青 と変わるので、
    # 足場の継ぎ目の上で T を押せば、乗ったまま次の色の足場へ移れる（空中で押しても間に合う）。
    s.checkpoint(33)
    s.pit(36, 44)
    p = piece("color_bridge")
    lock_row(36, [RED, GREEN, BLUE])
    p["adds"].append((36 * TILE, GROUND_Y, LOCK_W * 3, 32))

    # ---- つなぎ：少し上り下り
    s.ramp(48, 51, 13, 12)
    s.hill(52, 55, 12)
    s.ramp(56, 58, 12, 13)
    s.plat(51, 53, 9)

    # ================================================================ ② 短い青の足場を伸ばす
    # 穴は9マス（跳び越せない）。青の足場は3マスしかないので、選んでホイール（または右辺のつまみ）で横へ伸ばす。
    # 左端を固定して右へ伸び、向こう岸の地面に当たったところで自然に止まる（ちょうど穴をふさぐ幅になる）。
    # 伸ばすのは色を合わせる前でもよい（色が合っていない間も、薄い影として見えていて選べる）。
    s.checkpoint(60)
    s.pit(63, 71)
    p = piece("stretch")
    lock_row(63, [BLUE], lock=locks(allow=("scale",)))
    p["adds"].append((63 * TILE, GROUND_Y, 9 * TILE, 32))

    # ---- つなぎ
    s.ramp(76, 79, 13, 11)
    s.hill(80, 83, 11)
    s.ramp(84, 86, 11, 13)
    s.plat(78, 80, 8)

    # ================================================================ ③ ドッスンの谷
    # 穴（8マス）に緑の橋。橋の真ん中の上にドッスンが浮いていて、近くに来ると落ちてくる。
    # 何もしないと、真下（緑の橋の上）へ落ちて衝撃波を起こすので、橋の上にいると巻き込まれる。
    # 向きを横（Ctrl+ホイールで -90°）にして、1.3倍以上に大きくしてから橋に乗ると、
    # 右のもろい土の壁（全高。跳び越せない）へ真横に突っ込んで、壁を割る。壁までは6マスあるので、衝撃波は橋まで届かない。
    # 壁をこれより遠くに置かないこと：敵は画面に映っていない間は止まるので、遠いと突っ込む途中で画面の外へ出て止まり、
    # プレイヤーが歩いて近づいたときに（衝撃波の届く距離で）壁を割ることになる（実機で確かめた）。
    # 割れるのはドッスンの高さの段（9〜11行目）。一番下の段は残るので、それに跳び乗って、あいた穴をくぐる。
    s.checkpoint(89)
    s.pit(92, 99)
    lock_row(92, [GREEN, GREEN, GREEN])          # 3枚目は向こう岸の地面に少し載る（上面は同じ高さ）
    s.enemy("enemy_dossun_wide", 96 * TILE, 304, lock=locks(allow=("scale", "rotate")))
    s.block(102, 103, 1, 12, T_SOFT_DIRT)
    p = piece("dossun_wall")
    p["adds"].append((92 * TILE, GROUND_Y, LOCK_W * 3, 32))   # 緑の橋（この見せ場の中で色も使う）
    p["cuts"].append((102, 103, 9, 11))

    # ================================================================ ④ 機械芋虫の部屋
    # 天井の低い部屋（9〜12行目の4段ぶんの高さ）。左の入口（10〜12行目）は一方通行の壁（右へだけ通れる）。
    # プレイヤーは右へ入れるが、芋虫は左へ抜けられない（左へ回されても、逃げずに部屋の中で跳ね返って壁へ向かう）。
    # 入口を素通しにしておくと、左へ回した芋虫が部屋から逃げて、二度と壁を割らせられなくなる（実機で確かめた）。
    # 機械芋虫は部屋の中ほどで、床と天井のあいだを上下にまっすぐ往復している（下向きで置く）。
    # 横に回す（Ctrl+ホイールで ±90°）と、回した向きへまっすぐ飛ぶ。1.5倍以上に大きくしてあれば、
    # 右端のぶ厚い壁を体当たりで割って突き抜ける（左へ向けても、左の壁で跳ね返って右へ向かう）。
    # 割れるのは、そのときの芋虫の高さの段だけ。どの高さで回しても、残った段を足場にしてくぐれる高さにしてある。
    s.checkpoint(112)
    s.ceil(116, 131, 8)                          # 部屋の天井（0〜8行目）
    s.block(116, 116, 9, 9, T_STONE)             # 左の壁。その下（10〜12行目）が入口
    s.gim("gim_oneway_wall", 116 * TILE, GROUND_Y - 96, lock=locks())   # 入口。絵の矢印（右）の向きにだけ通れる
    s.block(128, 129, 9, 12, T_HARD_WALL)        # ぶ厚い壁
    # 角度はラジアン（ステージJSONの "angle" はラジアン）。π/2 ＝ 下向き
    s.enemy("enemy_kikai_straight", 123 * TILE, 320, angle=round(math.pi / 2, 5), lock=locks(allow=("scale", "rotate")))
    piece("worm_wall")                           # 割れる段は回した瞬間の高さで決まるので、検査では全通りを試す
    PIECES["worm_wall"]["cuts"].append((128, 129, 11, 12))

    # ================================================================ ⑤ 台地の栓
    # 赤の橋（6マス）を渡って、階段で台地（上面9行目）へ上がる。台地の上の道は、石の壁（天井まで）でふさがれている。
    # 台地の中には、9〜12行目の抜け道（下の2段）が通っていて、その入口を、もろい土の栓（2マス×2段）がふさいでいる。
    # 栓の真上にドッスンが浮いている。そのまま落とすと「1.3倍以上で壊せる」と出て、割れない。
    # 1.3倍以上にしてから落とすと、栓を叩き割る。あいた穴から抜け道へ降りて、壁の下をくぐる。
    # ドッスンの真横に近づきすぎると衝撃波に当たる（反応する距離のぎりぎりから落とすと安全）。
    s.checkpoint(134)
    s.pit(138, 143)
    p = piece("plug")
    lock_row(138, [RED, RED])
    p["adds"].append((138 * TILE, GROUND_Y, LOCK_W * 2, 32))
    s.ramp(144, 148, 13, 9)
    s.hill(149, 162, 9)
    for c in range(154, 163):                    # 台地の中の抜け道（11〜12行目）。床は13行目の地面
        s.over[(11, c)] = 0
        s.over[(12, c)] = 0
    s.block(156, 157, 9, 10, T_SOFT_DIRT)        # もろい土の栓
    s.block(160, 161, 1, 8, T_STONE)             # 台地の上の道をふさぐ石の壁（天井まで）
    s.enemy("enemy_dossun_wide", 156 * TILE + 8, 9 * TILE - 46 - 2 - 110, lock=locks(allow=("scale",)))
    p["cuts"].append((156, 157, 9, 10))

    # ---- 仕上げの道
    s.ramp(168, 171, 13, 12)
    s.hill(172, 176, 12)
    s.ramp(177, 179, 12, 13)
    s.plat(181, 183, 10)

    # ================================================================ ふつうの敵（見せ場の邪魔にならない所）
    s.enemy_on("enemy_aruku", 8, patrol=(5, 11))
    s.enemy_on("enemy_taihou", 19)
    s.enemy_on("enemy_oikake", 47)
    s.enemy("enemy_hikou", 54 * TILE, 200)
    s.enemy_on("enemy_aruku", 75, patrol=(73, 79))
    s.enemy_on("enemy_houdai", 82)
    s.enemy_on("enemy_imomushi", 106)
    s.enemy_on("enemy_aruku", 166, patrol=(164, 170))
    s.enemy_on("enemy_taihou", 174)
    s.enemy_on("enemy_oikake", 186)

    # ================================================================ コイン・回復
    s.coins([6, 24, 34, 46, 57, 61, 74, 87, 105, 113, 135, 150, 165, 178, 190])
    s.coin_at(15, 6)
    s.coin_at(40, 11)                            # ①の橋の上（穴の上なので、床からではなく行で置く）
    s.coin_at(67, 11)                            # ②の橋の上
    s.coin_at(52, 7)
    s.coin_at(79, 6)
    s.coin_at(120, 11)                           # ④の部屋の中
    s.coin_at(158, 11)                           # ⑤の抜け道の中
    s.coin_at(182, 8)
    s.item("item_edit_cost_refill", 58 * TILE, GROUND_Y - 64)
    s.item("item_edit_cost_refill", 110 * TILE, GROUND_Y - 64)
    s.item("item_edit_cost_refill", 133 * TILE, GROUND_Y - 64)

    s.set_goal(195)
    return s


# ====================================================================== 検査
def apply_pieces(s, solved_names, worm_rows=None):
    """名前が solved_names に入っている見せ場だけを「解いた」状態にして、Stage の solved_adds / solved_cuts を組み直す。
    worm_rows を渡すと、④で割れる段を (行0, 行1) に差し替える（芋虫を回した高さごとに試すため）"""
    s.solved_adds, s.solved_cuts = [], []
    for name, p in PIECES.items():
        if name not in solved_names:
            continue
        s.solved_adds.extend(p["adds"])
        cuts = p["cuts"]
        if name == "worm_wall" and worm_rows is not None:
            cuts = [(128, 129, worm_rows[0], worm_rows[1])]
        s.solved_cuts.extend(cuts)


def check_pieces(s, data):
    """編集が必須の作りになっているかを、物理で調べる。ミスがあれば文字列の一覧を返す。

    ① 全部を解けばゴールへ行ける（④は、芋虫が割る段が 9-10 / 10-11 / 11-12 のどれでも行ける）
    ② どの見せ場も、そこだけ解かないと、その見せ場より先へは行けない
    """
    errs = []
    names = list(PIECES)
    for rows in ((9, 10), (10, 11), (11, 12)):
        apply_pieces(s, names, worm_rows=rows)
        ok, pos = L.Reach(s, data, solved=True).run()
        if not ok:
            errs.append("全部を解いても、ゴールへ行けない（④で割れる段 %s、最も右 x=%.0f）" % (rows, pos[0]))

    # 見せ場の右端（ここより右へ行けたら「解かずに抜けた」とみなす）。
    # ⑤は栓の上を歩いて石の壁の手前（161列）までは行けるので、壁の右端を見る
    right_edge = {"color_bridge": 44, "stretch": 71, "dossun_wall": 103, "worm_wall": 129, "plug": 161}
    for name in names:
        apply_pieces(s, [n for n in names if n != name])
        ok, pos = L.Reach(s, data, solved=True).run()
        if ok or pos[0] > (right_edge[name] + 1) * TILE:
            errs.append("見せ場 %s を解かなくても先へ進めてしまう（x=%.0f まで行ける）" % (name, pos[0]))
    apply_pieces(s, names)
    return errs


def main():
    want_map = "--map" in sys.argv
    s = stage8()
    apply_pieces(s, list(PIECES))
    data = s.to_json()
    if want_map:
        print(L.preview(s, data))
        return 0

    ok = L.validate(s, data)                     # 足元・埋まり・説明の窓・解いたあとの到達
    errs = check_pieces(s, data)
    for e in errs:
        print("  -", e)
    if not ok or errs:
        print("【検査NG】")
        return 1

    path = os.path.join(STAGE_DIR, s.file)
    with open(path, "w", encoding="utf-8", newline=chr(10)) as f:
        json.dump(data, f, ensure_ascii=False, indent=2)
    n = len(data["items"])
    print("書き出し:", s.file, "「%s」" % NAME, "item_total=%d" % n)
    register(s.file, NAME, n)
    return 0


if __name__ == "__main__":
    sys.exit(main())
