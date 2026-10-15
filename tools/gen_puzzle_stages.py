# -*- coding: utf-8 -*-
"""編集機能ごとの本番ステージ6本（puzzle_1_size 〜 puzzle_6_rewind）を生成する。

使い方（リポジトリのルートで）:
    python tools/gen_puzzle_stages.py            # 全部を書き出す
    python tools/gen_puzzle_stages.py 1 3        # 指定したステージだけ
    python tools/gen_puzzle_stages.py --map 1    # ステージ1の道を文字で表示する（書き出さない）

・assets/gimmicks.json と assets/enemies.json に、ステージが使う部品の定義が無ければ足す（何度流しても同じ）
・assets/stages/puzzle_*.json を書き出す
・assets/game_config.json のステージ一覧は、このスクリプトでは書き換えない（item_total を表示するので、手で合わせる）

ステージの作りの約束（道具は tools/puzzle_lib.py）
  - 説明の窓は出さない。部屋で区切らず、途切れずに続く1本の道。見せ場の手前にチェックポイントを置く
  - 地面は平らではなく、上り下り・段差・浮いた足場・穴・低い天井を混ぜる。敵は普通の敵と、主役の編集に絡む敵を混ぜる
  - ジャンプ力は -12（頂点138px）。穴は5タイル以内、段差は3段以内、を目安にする（検査が物理で確かめる）
  - 重要な物（扉・スイッチ・足場・トゲ）には、解き方に使わない編集操作のロックを付ける
"""
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from puzzle_lib import *  # noqa: E402,F401,F403
import puzzle_lib as L    # noqa: E402

DEFS = Defs()


def ensure_defs():
    """ステージだけで使う部品の定義を、無ければ足す"""
    d = DEFS
    base = d.gim("gim_gate_door")

    def grade(gid, name, type_enum, w, h, ch, lo, hi, se="se_size_change"):
        x = json.loads(json.dumps(base))
        x.update({"id": gid, "name": name, "type_enum": type_enum, "sprite": "", "spriteAlt": "",
                  "hitboxWidth": w, "hitboxHeight": h, "seActivate": se,
                  "gradeChannel": float(ch), "gradeMin": float(lo), "gradeMax": float(hi)})
        d.put_gim(x)

    grade("gim_grade_sw_mono", "補正スイッチ（白黒）", 26, 64, 16, 2, -999, 0.2)
    grade("gim_grade_sw_hue", "補正スイッチ（色相がずれた）", 26, 64, 16, 3, 60, 180)
    grade("gim_grade_sw_warm", "補正スイッチ（暖色）", 26, 64, 16, 4, 0.4, 999)
    grade("gim_grade_sw_red", "補正スイッチ（赤フィルタ）", 26, 64, 16, 5, 1, 1)
    grade("gim_grade_sw_green", "補正スイッチ（緑フィルタ）", 26, 64, 16, 5, 2, 2)
    grade("gim_grade_sw_blue", "補正スイッチ（青フィルタ）", 26, 64, 16, 5, 3, 3)
    grade("gim_grade_sw_dark", "補正スイッチ（暗い）", 26, 64, 16, 0, -999, 0.6)
    grade("gim_grade_sw_bright", "補正スイッチ（明るい）", 26, 64, 16, 0, 1.3, 999)
    grade("gim_grade_lock_zoomin", "拡大中だけ現れる足場", 27, 96, 32, 6, 1.15, 999)
    grade("gim_grade_lock_zoomout", "縮小中だけ現れる足場", 27, 96, 32, 7, -999, 0.95)
    x = json.loads(json.dumps(base))
    x.update({"id": "gim_rewind_gate", "name": "逆行ゲート", "type_enum": 28, "sprite": "img/扉縁のみ.png", "spriteAlt": "",
              "hitboxWidth": 32, "hitboxHeight": 160, "seActivate": "se_gear"})
    d.put_gim(x)
    # 機械芋虫の体当たりでも、1.5倍以上に大きくしないと崩れない壁（地形タイル）。やわらかい壁と同じ絵
    d.put_tile({"id": 17, "name": "ぶ厚い壁（1.5倍以上の体当たりで崩れる）", "color": "#2A3F7A", "collidable": True, "deadly": False,
                "sprite": "img/ｓｆ風タイル２.png", "srcX": 0, "srcY": 0, "srcW": 0, "srcH": 0,
                "breakBySlam": False, "breakByRam": True, "breakByBullet": False, "breakByPlayer": False,
                "breakMinScale": 1.5, "breakSe": "se_hit"})
    # ドッスン：プレイヤーが少し離れていても反応する（近くに立つと、衝撃波が届いてしまうため）
    d.derive_ene("enemy_dossun", "enemy_dossun_wide", "ドッスン（広く反応する）", triggerRange=96.0)
    # 機械芋虫：跳ね返りの乱れを無くし、決めた向きへ正確に往復する
    d.derive_ene("enemy_kikai_imomushi", "enemy_kikai_straight", "機械芋虫（まっすぐ往復）", bounceRandomness=0.0)
    # 色変化の敵：射程内のプレイヤーのTキーの色を、押し付ける色に書き換える。動かない見張り
    d.derive_ene("enemy_iro", "enemy_iro_force", "色の番人（Tキーの色を書き換える）",
                 forcePlayerFilter=1.0, moveSpeed=0.0, effectRange=420.0, triggerRange=420.0)
    d.save()


ensure_defs()


# ================================================================= ① サイズと角度
def stage1():
    s = Stage("puzzle_1_size.json", "サイズと角度", "bgm_editing_05", "#CFE3F5", 200,
              tools(object=True), ALL_PLAYER_LOCKS, cost(regenPerSec=8.0), DEFS)
    s.embed_ok = ("gim_scalable_ground",)
    # ---- 地形
    s.ramp(18, 22, 13, 11)                       # 丘
    s.hill(23, 29, 11)
    s.ramp(30, 34, 11, 13)
    s.pit(36, 38)                                # 小さな穴（ふつうに跳ぶ）
    s.ramp(110, 114, 13, 11)
    s.hill(115, 121, 11)
    s.ramp(122, 126, 11, 13)
    s.pit(74, 85)                                # 橋の谷（12タイル）
    s.pit(132, 139)                              # 足場を伸ばす穴（8タイル）
    s.hill(140, 145, 12)
    # 浮いた足場
    s.plat(13, 15, 10)
    s.plat(26, 28, 8)
    s.plat(36, 38, 11)
    s.plat(60, 62, 9)
    s.plat(116, 118, 8)
    # ドッスンの広間（146〜171）：床は平ら。162〜163 が、叩き割る壁
    s.block(164, 165, 1, 12, T_SOFT_DIRT)
    # 機械芋虫の小部屋（174〜183）：左の壁は下の2段だけ開いている（入口）。右端が、体当たりで割るブロック
    s.block(173, 173, 1, 10, T_STONE)

    # ---- 見せ場1：箱を広げて重量スイッチへ → 扉
    s.checkpoint(42)
    s.gim("gim_scalable_box", 46 * TILE, GROUND_Y - 50, lock=locks(allow=("scale",)))
    s.gim("gim_weight_switch", 50 * TILE, GROUND_Y - 10, param="s1a", lock=locks())
    s.door(64, "s1a")
    s.solved_free.append((64 * TILE, GROUND_Y - 160, 32, 160))

    # ---- 見せ場2：手動橋を水平に倒して、広げて、谷にかける
    s.checkpoint(70)
    s.gim("gim_manual_bridge", 74 * TILE - 24, GROUND_Y - 8, lock=locks(allow=("scale", "rotate")))
    s.solved_adds.append((74 * TILE - 24, GROUND_Y, 12 * TILE + 48, 16))

    # ---- 見せ場3：トゲの列を、範囲選択して傾けて安全な足場にする
    s.checkpoint(92)
    for k in range(8):
        s.gim("gim_spikes", (100 + k) * TILE, GROUND_Y - 32, lock=locks(allow=("rotate",)))
    s.solved_no_spikes = True

    # ---- 見せ場4：小さな足場を横に伸ばして、穴をふさぐ
    s.checkpoint(127)
    s.gim("gim_scalable_ground", 132 * TILE - 64, GROUND_Y, lock=locks(allow=("scale",)))
    s.solved_adds.append((132 * TILE - 64, GROUND_Y, 8 * TILE + 96, 32))

    # ---- 見せ場5：ドッスンを横に向けて、大きくして、壁を叩き割る
    # ドッスンは空中に浮いたまま、真下に立つと落ちてくる。向きを横に回し、1.3倍以上にして、その下に立つと、
    # 壁（もろい土）へ横から突っ込んで割る。壁までは遠いので、衝撃波は立っている所まで届かない
    s.checkpoint(148)
    s.enemy("enemy_dossun_wide", 154 * TILE, 304, lock=locks(allow=("scale", "rotate")))
    s.solved_cuts.append((164, 165, 9, 11))

    # ---- 見せ場6：機械芋虫を大きくして、ブロックを体当たりで割らせる
    # 機械芋虫は小部屋の中を、まっすぐ往復している。1.5倍以上に大きくすると、右端のブロックを割って突き抜ける。
    # 割れるのは芋虫の高さの段だけなので、下の2段は残る。そこへ跳び乗って、あいた穴をくぐる
    s.checkpoint(170)
    s.enemy("enemy_kikai_straight", 177 * TILE, 292, angle=0, lock=locks(allow=("scale",)))
    s.block(184, 185, 8, 12, T_HARD_WALL)
    s.solved_cuts.append((184, 185, 9, 10))

    # ---- 敵
    s.enemy_on("enemy_aruku", 10, patrol=(8, 16))
    s.enemy_on("enemy_taihou", 25)
    s.enemy_on("enemy_aruku", 33, patrol=(31, 35))
    s.enemy_on("enemy_houdai", 58)
    s.enemy("enemy_hikou", 79 * TILE, 200)
    s.enemy_on("enemy_oikake", 96)
    s.enemy_on("enemy_aruku", 116, patrol=(115, 121))
    s.enemy_on("enemy_taihou", 119)
    s.enemy_on("enemy_imomushi", 124)
    s.enemy_on("enemy_aruku", 142, patrol=(140, 145))
    s.enemy_on("enemy_aruku", 150, patrol=(149, 153))
    s.enemy_on("enemy_taihou", 160)
    s.enemy_on("enemy_oikake", 168)
    s.enemy_on("enemy_aruku", 194, patrol=(192, 198))

    # ---- コイン・回復
    s.coins([5, 20, 31, 55, 67, 90, 126, 146, 157, 169, 196])
    s.coin_at(14, 8)
    s.coin_at(27, 6)
    s.coin_at(37, 9)
    s.coin_at(61, 7)
    s.coin_at(117, 6)
    s.item("item_edit_cost_refill", 44 * TILE, GROUND_Y - 64)
    s.item("item_edit_cost_refill", 126 * TILE, GROUND_Y - 64)
    s.item("item_edit_cost_refill", 170 * TILE + 16, GROUND_Y - 100)

    s.set_goal(195)
    return s


# ================================================================= ② 色
def stage2():
    s = Stage("puzzle_2_color.json", "色", "bgm_editing_06", "#F1E1EA", 212,
              tools(screen=True, object=True), ALL_PLAYER_LOCKS, cost(regenPerSec=10.0), DEFS)
    # ---- 地形
    s.ramp(14, 18, 13, 11)
    s.hill(19, 25, 11)
    s.ramp(26, 30, 11, 13)
    s.pit(34, 36)
    s.plat(15, 17, 9)
    s.plat(21, 24, 7)
    s.plat(34, 36, 10)
    # 色の橋：穴3つと、その間の島
    s.pit(42, 47)
    s.pit(54, 59)
    s.pit(66, 71)
    s.plat(50, 51, 10)
    # 彩度の扉のあたり
    s.ramp(76, 79, 13, 12)
    s.hill(80, 84, 12)
    s.ramp(85, 88, 12, 13)
    s.hill(97, 100, 12)
    # 色の番人の橋
    s.pit(108, 116)
    s.plat(111, 113, 7)                          # 橋の真上に浮かぶ、番人の足場
    s.hill(122, 126, 12)
    # 色相・色温度の扉
    s.ramp(128, 131, 13, 11)
    s.hill(132, 135, 11)
    s.ramp(136, 139, 11, 13)
    s.plat(143, 146, 9)
    # 色の番人の扉
    s.plat(158, 161, 9)
    s.hill(168, 172, 12)
    s.pit(173, 175)
    s.plat(173, 175, 10)

    # ---- 見せ場1：赤・緑・青の足場の橋。Tで色を合わせて、順に渡る
    s.checkpoint(38)
    for (c0, param) in ((42, "1"), (54, "2"), (66, "3")):
        s.gim("gim_color_lock", c0 * TILE, GROUND_Y, param=param, lock=locks())
        s.gim("gim_color_lock", (c0 + 3) * TILE, GROUND_Y, param=param, lock=locks())
        s.solved_adds.append((c0 * TILE, GROUND_Y, 96, 32))
        s.solved_adds.append(((c0 + 3) * TILE, GROUND_Y, 96, 32))

    # ---- 見せ場2：彩度を下げて白黒にすると開く扉（右のカラー補正パネル）
    s.checkpoint(76)
    s.gim("gim_grade_sw_mono", 82 * TILE, s.floor_y(82) - 16, param="c1", lock=locks())
    s.door(90, "c1")
    s.solved_free.append((90 * TILE, GROUND_Y - 160, 32, 160))

    # ---- 見せ場3：色の番人の橋。緑の足場が出るはずが、番人が色を赤に書き換えている
    # 番人を選んで傾けると、押し付ける色が 赤→緑 に変わる。倒しても解ける（番人は高い足場の上にいて、弾が届きにくい）
    s.checkpoint(104)
    for c0 in (108, 111, 114):
        s.gim("gim_color_lock", c0 * TILE, GROUND_Y, param="2", lock=locks())
        s.solved_adds.append((c0 * TILE, GROUND_Y, 96, 32))
    s.enemy_at("enemy_iro_force", 112, 7, lock=locks(allow=("rotate",)))

    # ---- 見せ場4：色相・色温度の扉（パネルで色相をずらす／色温度を暖色にする）
    s.checkpoint(127)
    s.gim("gim_grade_sw_hue", 133 * TILE, s.floor_y(133) - 16, param="c2a", lock=locks())
    s.door(140, "c2a")
    s.gim("gim_grade_sw_warm", 148 * TILE, s.floor_y(148) - 16, param="c2b", lock=locks())
    s.door(152, "c2b")
    s.solved_free.append((140 * TILE, GROUND_Y - 160, 32, 160))
    s.solved_free.append((152 * TILE, GROUND_Y - 160, 32, 160))

    # ---- 見せ場5：色の番人の扉。青のスイッチ。番人が赤に書き換えている → 傾けて青にする
    s.checkpoint(154)
    s.gim("gim_grade_sw_blue", 163 * TILE, s.floor_y(163) - 16, param="c3", lock=locks())
    s.door(166, "c3")
    s.solved_free.append((166 * TILE, GROUND_Y - 160, 32, 160))
    s.enemy_at("enemy_iro_force", 159, 9, lock=locks(allow=("rotate",)))

    # ---- 見せ場6：赤・緑・青のスイッチ扉を3枚、Tで色を変えながら通る
    s.checkpoint(180)
    for k, (swid, par) in enumerate((("gim_grade_sw_red", "c4a"), ("gim_grade_sw_green", "c4b"), ("gim_grade_sw_blue", "c4c"))):
        s.gim(swid, (182 + k * 8) * TILE, GROUND_Y - 16, param=par, lock=locks())
        s.door(186 + k * 8, par)
        s.solved_free.append(((186 + k * 8) * TILE, GROUND_Y - 160, 32, 160))

    # ---- 敵
    s.enemy_on("enemy_aruku", 10, patrol=(8, 14))
    s.enemy_on("enemy_taihou", 22)
    s.enemy_on("enemy_oikake", 31)
    s.enemy_on("enemy_iro", 40)
    s.enemy("enemy_hikou", 58 * TILE, 180)
    s.enemy("enemy_hikou", 68 * TILE, 190)
    s.enemy_on("enemy_aruku", 74, patrol=(72, 79))
    s.enemy_on("enemy_taihou", 82)
    s.enemy_on("enemy_imomushi", 94)
    s.enemy_on("enemy_aruku", 99, patrol=(97, 100))
    s.enemy_on("enemy_oikake", 124)
    s.enemy_on("enemy_houdai", 134)
    s.enemy_on("enemy_aruku", 145, patrol=(142, 147))
    s.enemy_on("enemy_taihou", 156)
    s.enemy_on("enemy_dossun", 170, dy=100)
    s.enemy_on("enemy_oikake", 178)
    s.enemy_on("enemy_iro", 206)
    s.enemy_on("enemy_aruku", 204, patrol=(202, 209))

    # ---- コイン・回復
    s.coins([6, 20, 29, 38, 62, 74, 86, 120, 126, 138, 150, 169, 190, 209])
    s.coin_at(16, 7)
    s.coin_at(22, 5)
    s.coin_at(35, 8)
    s.coin_at(50, 8)
    s.coin_at(144, 7)
    s.coin_at(174, 8)
    s.item("item_edit_cost_refill", 78 * TILE, GROUND_Y - 80)
    s.item("item_edit_cost_refill", 129 * TILE, GROUND_Y - 80)

    s.set_goal(209 - 4)
    return s


# ================================================================= ③ 明るさ・暗さ
def stage3():
    s = Stage("puzzle_3_bright.json", "明るさと暗さ", "bgm_editing_07", "#BCC9E0", 218,
              tools(screen=True), NO_PLAYER_LOCKS, cost(regenPerSec=10.0), DEFS)
    s.embed_ok = ("gim_brightness_zone",)
    # ---- 地形
    s.ramp(12, 16, 13, 11)
    s.hill(17, 23, 11)
    s.ramp(24, 28, 11, 13)
    s.pit(33, 35)
    s.plat(13, 15, 9)
    s.plat(18, 22, 7)
    s.plat(33, 35, 10)
    s.pit(44, 52)                                # 暗い橋
    s.pit(60, 68)                                # 明るい橋
    s.plat(56, 58, 10)
    # 洞窟（74〜110）：黒い石の床、低い天井
    s.hill(74, 110, 13, surf=T_STONE, fill=T_STONE)
    s.ceil(74, 110, 4)
    s.hill(80, 83, 12, surf=T_STONE, fill=T_STONE)
    s.pit(86, 97)
    s.hill(100, 104, 12, surf=T_STONE, fill=T_STONE)
    # 明るさの敵の橋
    s.hill(112, 114, 12)
    s.pit(118, 125)
    s.plat(120, 123, 8)
    # 扉
    s.ramp(134, 137, 13, 11)
    s.hill(138, 141, 11)
    s.ramp(142, 145, 11, 13)
    s.plat(150, 153, 9)
    # 島をはさんだ、暗い橋と明るい橋
    s.pit(172, 180)
    s.pit(189, 197)
    s.plat(183, 186, 10)
    s.hill(200, 204, 12)

    # ---- 見せ場1：暗いときだけ現れる足場（X）
    s.checkpoint(40)
    for c0 in (44, 47, 50):
        s.gim("gim_brightness_lock", c0 * TILE, GROUND_Y, param="dark", lock=locks())
        s.solved_adds.append((c0 * TILE, GROUND_Y, 96, 32))

    # ---- 見せ場2：明るいときだけ現れる足場（C）
    s.checkpoint(56)
    for c0 in (60, 63, 66):
        s.gim("gim_brightness_lock", c0 * TILE, GROUND_Y, param="bright", lock=locks())
        s.solved_adds.append((c0 * TILE, GROUND_Y, 96, 32))

    # ---- 見せ場3：暗い洞窟。暗い領域は幽霊を実体化させる。Cで明るくすると、幽霊が消え、明るいときだけの足場が現れる
    s.checkpoint(76)
    s.gim("gim_brightness_zone", 76 * TILE, 0, scale=4.8, scale_y=2.4, lock=locks())
    for k in range(4):
        s.gim("gim_brightness_lock", (86 + 3 * k) * TILE, GROUND_Y, param="bright", lock=locks())
        s.solved_adds.append(((86 + 3 * k) * TILE, GROUND_Y, 96, 32))
    s.enemy("enemy_yurei", 88 * TILE, 230, left=84 * TILE, right=94 * TILE, lock=locks())
    s.enemy("enemy_yurei", 94 * TILE, 280, left=88 * TILE, right=99 * TILE, lock=locks())

    # ---- 見せ場4：明るさの敵。近づくと画面を暗くする。Cで明るくして打ち消す
    s.checkpoint(110)
    for k in range(3):
        s.gim("gim_brightness_lock", 118 * TILE - 16 + k * 96, GROUND_Y, param="bright", lock=locks())
        s.solved_adds.append((118 * TILE - 16 + k * 96, GROUND_Y, 96, 32))
    s.enemy_on("enemy_akarusa", 113, lock=locks())

    # ---- 見せ場5：暗いと開く扉 → 明るいと開く扉
    s.checkpoint(132)
    s.gim("gim_grade_sw_dark", 139 * TILE, s.floor_y(139) - 16, param="b1", lock=locks())
    s.door(146, "b1")
    s.gim("gim_grade_sw_bright", 155 * TILE, GROUND_Y - 16, param="b2", lock=locks())
    s.door(160, "b2")
    s.solved_free.append((146 * TILE, GROUND_Y - 160, 32, 160))
    s.solved_free.append((160 * TILE, GROUND_Y - 160, 32, 160))

    # ---- 見せ場6：暗い橋 → 島 → 明るい橋
    s.checkpoint(168)
    for c0 in (172, 175, 178):
        s.gim("gim_brightness_lock", c0 * TILE, GROUND_Y, param="dark", lock=locks())
        s.solved_adds.append((c0 * TILE, GROUND_Y, 96, 32))
    for c0 in (189, 192, 195):
        s.gim("gim_brightness_lock", c0 * TILE, GROUND_Y, param="bright", lock=locks())
        s.solved_adds.append((c0 * TILE, GROUND_Y, 96, 32))

    # ---- 敵
    s.enemy_on("enemy_aruku", 8, patrol=(6, 12))
    s.enemy_on("enemy_taihou", 20)
    s.enemy_on("enemy_oikake", 29)
    s.enemy("enemy_hikou", 46 * TILE, 200)
    s.enemy_on("enemy_aruku", 57, patrol=(55, 59))
    s.enemy("enemy_hikou", 64 * TILE, 190)
    s.enemy_on("enemy_taihou", 82)
    s.enemy_on("enemy_aruku", 104, patrol=(100, 108))
    s.enemy_on("enemy_oikake", 130)
    s.enemy_on("enemy_houdai", 139)
    s.enemy_on("enemy_aruku", 150, patrol=(148, 158))
    s.enemy_on("enemy_imomushi", 164)
    s.enemy_on("enemy_taihou", 202)
    s.enemy_on("enemy_oikake", 208)
    s.enemy_on("enemy_aruku", 212, patrol=(209, 216))

    # ---- コイン・回復
    s.coins([5, 18, 27, 41, 57, 72, 100, 108, 130, 142, 165, 185, 199, 207, 214])
    s.coin_at(14, 8)
    s.coin_at(20, 5)
    s.coin_at(34, 8)
    s.coin_at(57, 8)
    s.coin_at(121, 6)
    s.coin_at(151, 7)
    s.item("item_edit_cost_refill", 42 * TILE, GROUND_Y - 80)
    s.item("item_edit_cost_refill", 166 * TILE, GROUND_Y - 80)

    s.set_goal(211)
    return s


# ================================================================= ④ ズーム
def stage4():
    s = Stage("puzzle_4_zoom.json", "ズーム", "bgm_editing_08", "#D8E4CF", 214,
              tools(screen=True), NO_PLAYER_LOCKS, cost(regenPerSec=10.0), DEFS)
    s.embed_ok = ("gim_zoom_lens",)
    # ---- 地形
    s.ramp(13, 17, 13, 10)
    s.hill(18, 22, 10)
    s.ramp(23, 27, 10, 13)
    s.pit(32, 34)
    s.plat(14, 16, 8)
    s.plat(19, 21, 6)
    s.plat(32, 34, 10)
    s.pit(44, 52)                                # 拡大の橋
    s.pit(60, 68)                                # 縮小の橋
    s.plat(55, 57, 10)
    # ズーム固定の敵（85）。縮小の橋は92〜100
    s.pit(92, 100)
    s.hill(104, 110, 12)
    # 低い天井のトンネル（112〜132）
    s.ceil(112, 132, 5)
    s.hill(112, 132, 13, surf=T_STONE, fill=T_STONE)
    s.hill(120, 123, 12, surf=T_STONE, fill=T_STONE)
    # 拡大の橋 → 島 → 縮小の橋
    s.pit(138, 146)
    s.pit(155, 163)
    s.plat(149, 152, 10)
    # ズームレンズの橋
    s.hill(168, 171, 12)
    s.pit(176, 184)
    s.hill(188, 193, 12)

    # ---- 見せ場1：拡大中だけ現れる足場（Z）
    s.checkpoint(40)
    for c0 in (44, 47, 50):
        s.gim("gim_grade_lock_zoomin", c0 * TILE, GROUND_Y, lock=locks())
        s.solved_adds.append((c0 * TILE, GROUND_Y, 96, 32))

    # ---- 見せ場2：縮小中だけ現れる足場（V）
    s.checkpoint(56)
    for c0 in (60, 63, 66):
        s.gim("gim_grade_lock_zoomout", c0 * TILE, GROUND_Y, lock=locks())
        s.solved_adds.append((c0 * TILE, GROUND_Y, 96, 32))

    # ---- 見せ場3：ズーム固定の敵。近づくと画面が寄って固定され、縮小が使えない。倒してから縮小の橋を渡る
    s.checkpoint(74)
    s.enemy_on("enemy_zoom", 85, lock=locks())
    for c0 in (92, 95, 98):
        s.gim("gim_grade_lock_zoomout", c0 * TILE, GROUND_Y, lock=locks())
        s.solved_adds.append((c0 * TILE, GROUND_Y, 96, 32))

    # ---- 見せ場4：低いトンネルは、ふつうに進む（芋虫・歩く敵）
    s.checkpoint(112)

    # ---- 見せ場5：拡大の橋 → 島 → 縮小の橋
    s.checkpoint(134)
    for c0 in (138, 141, 144):
        s.gim("gim_grade_lock_zoomin", c0 * TILE, GROUND_Y, lock=locks())
        s.solved_adds.append((c0 * TILE, GROUND_Y, 96, 32))
    for c0 in (155, 158, 161):
        s.gim("gim_grade_lock_zoomout", c0 * TILE, GROUND_Y, lock=locks())
        s.solved_adds.append((c0 * TILE, GROUND_Y, 96, 32))

    # ---- 見せ場6：ズームレンズの中では、画面が自動で寄るので、拡大中だけの足場が出っぱなしになる
    s.checkpoint(166)
    s.gim("gim_zoom_lens", 170 * TILE, 0, scale=4.8, scale_y=2.4, lock=locks())
    for c0 in (176, 179, 182):
        s.gim("gim_grade_lock_zoomin", c0 * TILE, GROUND_Y, lock=locks())
        s.solved_adds.append((c0 * TILE, GROUND_Y, 96, 32))

    # ---- 敵
    s.enemy_on("enemy_aruku", 9, patrol=(7, 13))
    s.enemy_on("enemy_taihou", 19)
    s.enemy_on("enemy_oikake", 28)
    s.enemy("enemy_hikou", 46 * TILE, 200)
    s.enemy_on("enemy_aruku", 56, patrol=(54, 58))
    s.enemy("enemy_hikou", 64 * TILE, 190)
    s.enemy_on("enemy_aruku", 78, patrol=(76, 80))
    s.enemy_on("enemy_taihou", 106)
    s.enemy_on("enemy_imomushi", 116)
    s.enemy_on("enemy_aruku", 119, patrol=(115, 120))
    s.enemy_on("enemy_imomushi", 126)
    s.enemy_on("enemy_oikake", 131)
    s.enemy_on("enemy_houdai", 170)
    s.enemy_on("enemy_taihou", 190)
    s.enemy_on("enemy_aruku", 202, patrol=(198, 206))

    # ---- コイン・回復
    s.coins([5, 18, 29, 41, 56, 73, 87, 106, 114, 128, 136, 148, 167, 191, 204])
    s.coin_at(15, 6)
    s.coin_at(20, 4)
    s.coin_at(33, 8)
    s.coin_at(56, 8)
    s.coin_at(150, 8)
    s.item("item_edit_cost_refill", 42 * TILE, GROUND_Y - 80)
    s.item("item_edit_cost_refill", 134 * TILE, GROUND_Y - 80)

    s.set_goal(208)
    return s


# ================================================================= ⑤ カット
def stage5():
    s = Stage("puzzle_5_cut.json", "カット", "bgm_editing_09", "#E6D9C9", 220,
              tools(cut=True), NO_PLAYER_LOCKS,
              cost(maxCost=100.0, regenPerSec=6.0, flatCutCreate=10.0, cutCostPerTile=4.0), DEFS)
    # カットは高さを変えない。カットの始点・終点の足元は、すべて平らな地面（行13）にそろえる
    # ---- 地形
    s.ramp(12, 16, 13, 11)
    s.hill(17, 23, 11)
    s.ramp(24, 28, 11, 13)
    s.pit(33, 35)
    s.plat(13, 15, 9)
    s.plat(33, 35, 10)
    s.hill(37, 40, 12)
    # トゲの谷（44〜72）。トゲは53〜64
    s.hill(72, 73, 12)
    s.plat(56, 61, 8)
    # 高い壁（76〜）
    s.ramp(96, 99, 13, 11)
    s.hill(100, 104, 11)
    s.ramp(105, 107, 11, 13)
    # 大きな穴
    s.pit(112, 126)
    s.plat(117, 121, 8)
    # 壁2枚のあいだの丘
    s.hill(154, 156, 12)
    s.hill(157, 160, 11)
    s.hill(161, 161, 12)
    s.plat(155, 160, 8)
    # 鍵の部屋の前後
    s.ramp(176, 179, 13, 12)
    s.hill(180, 184, 12)
    s.ramp(185, 186, 12, 13)

    # ---- 見せ場1：トゲの谷。ジャンプでは越えられない12マス。タイムラインを Ctrl+クリックで2回打って飛ばす
    s.checkpoint(46)
    for c in range(53, 65):
        s.gim("gim_spikes", c * TILE, GROUND_Y - 32, lock=locks())
    s.solved_no_spikes = True

    # ---- 見せ場2：高い壁。壁の前から後ろまでを飛ばす
    s.checkpoint(78)
    s.block(86, 91, 1, 12, T_STONE)
    s.solved_cuts.append((86, 91, 1, 12))

    # ---- 見せ場3：大きな穴。ゲージが足りるように、穴の前から後ろまでを、ぎりぎりで打つ
    s.checkpoint(108)
    s.solved_adds.append((112 * TILE, GROUND_Y, 15 * TILE, 64))    # カットで穴を飛ばしたあとは、穴が無いのと同じ

    # ---- 見せ場4：壁が2枚。1回ずつ打つ（ゲージは時間で回復する）
    s.checkpoint(142)
    s.block(148, 151, 1, 12, T_STONE)
    s.block(164, 167, 1, 12, T_STONE)
    s.solved_cuts.append((148, 151, 1, 12))
    s.solved_cuts.append((164, 167, 1, 12))

    # ---- 見せ場5：壁の向こうに鍵。カットで壁を越えて鍵を取ると、奥の扉が開く
    s.checkpoint(182)
    s.block(188, 191, 1, 12, T_STONE)
    s.solved_cuts.append((188, 191, 1, 12))
    s.item("item_edit_keyreel", 196 * TILE, GROUND_Y - 64)
    s.door(204, "k5")
    s.solved_free.append((204 * TILE, GROUND_Y - 160, 32, 160))
    s.triggers.append({
        "id": "p5_key", "x": 0.0, "y": 0.0, "width": 64.0, "height": 480.0,
        "condition": "ItemCollected", "conditionParam": "item_edit_keyreel", "oneShot": True,
        "actions": [{"action": "OpenDoor", "param1": "gim_gate_door", "param2": "k5", "delay": 0.0}],
    })

    # ---- 敵
    s.enemy_on("enemy_aruku", 9, patrol=(7, 13))
    s.enemy_on("enemy_taihou", 20)
    s.enemy_on("enemy_oikake", 30)
    s.enemy_on("enemy_taihou", 40)
    s.enemy_on("enemy_houdai", 68)
    s.enemy("enemy_hikou", 58 * TILE, 150)
    s.enemy_on("enemy_aruku", 80, patrol=(78, 84))
    s.enemy_on("enemy_imomushi", 96)
    s.enemy_on("enemy_taihou", 102)
    s.enemy_on("enemy_oikake", 134)
    s.enemy_on("enemy_houdai", 136)
    s.enemy_on("enemy_aruku", 140, patrol=(137, 145))
    s.enemy_at("enemy_taihou", 158, 11)
    s.enemy_on("enemy_oikake", 172)
    s.enemy_on("enemy_kikai_imomushi", 175, dy=120)
    s.enemy_on("enemy_aruku", 198, patrol=(194, 203))
    s.enemy_on("enemy_taihou", 208)

    # ---- コイン・回復
    s.coins([6, 20, 31, 48, 70, 80, 94, 110, 132, 144, 170, 180, 198, 210])
    s.coin_at(14, 7)
    s.coin_at(34, 8)
    s.coin_at(58, 6)
    s.coin_at(119, 6)
    s.coin_at(157, 6)
    s.item("item_edit_cost_refill", 72 * TILE, GROUND_Y - 100)
    s.item("item_edit_cost_refill", 130 * TILE, GROUND_Y - 80)
    s.item("item_edit_cost_refill", 184 * TILE, GROUND_Y - 100)

    s.set_goal(214)
    return s


# ================================================================= ⑥ 巻き戻し
def stage6():
    s = Stage("puzzle_6_rewind.json", "巻き戻し", "bgm_editing_05", "#D5CFE8", 214,
              tools(rewind=True, object=True), ALL_PLAYER_LOCKS,
              cost(maxCost=100.0, regenPerSec=12.0, drainRewindPerSec=18.0), DEFS)
    rw = locks(allow=("rewind",))
    # 巻き戻しのエネルギー（item_energy / item_edit_cost_refill）は置かない。巻き戻しでアイテムを取り直して、ゲージを稼げてしまうため
    # ---- 地形
    s.ramp(12, 16, 13, 11)
    s.hill(17, 23, 11)
    s.ramp(24, 28, 11, 13)
    s.pit(33, 35)
    s.plat(13, 15, 9)
    s.plat(33, 35, 10)
    s.plat(62, 64, 9)
    s.hill(68, 72, 12)
    s.hill(76, 79, 12)
    s.pit(80, 91)                                # 崩れる床の橋
    s.plat(84, 88, 8)
    s.hill(92, 96, 12)
    s.ramp(100, 103, 13, 11)
    s.hill(104, 108, 11)
    s.ramp(109, 112, 11, 13)
    s.plat(126, 130, 8)
    s.plat(142, 146, 9)
    s.hill(150, 153, 12)
    s.plat(166, 170, 8)
    s.plat(180, 184, 8)
    s.hill(192, 197, 12)

    # ---- 見せ場1：逆行ゲート。ゲートは、どれかの物が巻き戻し中のあいだだけ通れる。箱を選んで、Rを押しながら通る
    # （箱だけが巻き戻る。プレイヤーの時間は戻らない）
    s.checkpoint(44)
    s.gim("gim_scalable_box", 50 * TILE, GROUND_Y - 50, lock=rw)
    s.gim("gim_rewind_gate", 58 * TILE, GROUND_Y - 160, lock=locks())
    s.solved_free.append((58 * TILE, GROUND_Y - 160, 32, 160))

    # ---- 見せ場2：崩れる床の橋。乗ると落ちる床を、渡りきる。落ちたら、Rで時間ごと巻き戻してやり直せる
    s.checkpoint(74)
    for c in range(80, 92):
        s.gim("gim_chikuwa", c * TILE, GROUND_Y)

    # ---- 見せ場3：ゲート3枚。箱を選んで、Rを押し続けて、続けて通る
    s.checkpoint(114)
    s.gim("gim_scalable_box", 118 * TILE, GROUND_Y - 50, lock=rw)
    for c in (124, 132, 140):
        s.gim("gim_rewind_gate", c * TILE, GROUND_Y - 160, lock=locks())
        s.solved_free.append((c * TILE, GROUND_Y - 160, 32, 160))

    # ---- 見せ場4：ゲート4枚。巻き戻しはゲージを使い続けるので、ゲートの前で入れて、通ったら切る
    s.checkpoint(156)
    s.gim("gim_scalable_box", 158 * TILE, GROUND_Y - 50, lock=rw)
    for c in (164, 171, 178, 185):
        s.gim("gim_rewind_gate", c * TILE, GROUND_Y - 160, lock=locks())
        s.solved_free.append((c * TILE, GROUND_Y - 160, 32, 160))

    # ---- 敵
    s.enemy_on("enemy_aruku", 9, patrol=(7, 13))
    s.enemy_on("enemy_taihou", 20)
    s.enemy_on("enemy_oikake", 30)
    s.enemy_on("enemy_aruku", 54, patrol=(52, 57))
    s.enemy_on("enemy_taihou", 70)
    s.enemy("enemy_hikou", 84 * TILE, 170)
    s.enemy_on("enemy_oikake", 94)
    s.enemy_on("enemy_dossun", 106, dy=100)
    s.enemy_on("enemy_imomushi", 98)
    s.enemy_on("enemy_taihou", 120)
    s.enemy_on("enemy_aruku", 128, patrol=(125, 131))
    s.enemy_on("enemy_houdai", 136)
    s.enemy_on("enemy_oikake", 146)
    s.enemy_on("enemy_taihou", 152)
    s.enemy_on("enemy_aruku", 168, patrol=(166, 170))
    s.enemy_on("enemy_houdai", 175)
    s.enemy_on("enemy_imomushi", 182)
    s.enemy_on("enemy_taihou", 194)
    s.enemy_on("enemy_aruku", 202, patrol=(199, 207))

    # ---- コイン
    s.coins([6, 20, 31, 48, 66, 74, 94, 110, 116, 134, 148, 160, 176, 190, 196, 208])
    s.coin_at(14, 7)
    s.coin_at(34, 8)
    s.coin_at(63, 7)
    s.coin_at(86, 6)
    s.coin_at(128, 6)
    s.coin_at(144, 7)
    s.coin_at(168, 6)
    s.coin_at(182, 6)

    s.set_goal(206)
    return s


STAGES = [stage1, stage2, stage3, stage4, stage5, stage6]


def main():
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    want_map = "--map" in sys.argv
    pick = [int(a) for a in args] if args else list(range(1, len(STAGES) + 1))
    results = []
    bad = False
    for i in pick:
        s = STAGES[i - 1]()
        data = s.to_json()
        if want_map:
            print(L.preview(s, data))
        ok = validate(s, data)
        if not ok:
            bad = True
            continue
        if want_map:
            continue
        path = os.path.join(STAGE_DIR, s.file)
        with open(path, "w", encoding="utf-8", newline="\n") as f:
            json.dump(data, f, ensure_ascii=False, indent=2)
        results.append((s.file, s.title, len(data["items"])))
    for name, title, n in results:
        print("書き出し:", name, "「%s」" % title, "item_total=%d" % n)
    if bad:
        sys.exit(1)


if __name__ == "__main__":
    main()
