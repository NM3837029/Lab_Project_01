# -*- coding: utf-8 -*-
"""編集機能ごとの本番ステージ6本（puzzle_1_size 〜 puzzle_6_rewind）を生成する。

使い方（リポジトリのルートで）:
    python tools/gen_puzzle_stages.py

・assets/gimmicks.json に、ステージが使う補正スイッチ・ズームのロック足場・逆行ゲートの定義が無ければ足す（何度流しても同じ）
・assets/stages/puzzle_*.json を書き出す
・assets/game_config.json のステージ一覧は、このスクリプトでは書き換えない（item_total を表示するので、手で合わせる）

ステージの作りの約束（チュートリアルの tools/gen_tutorial_stages.py と同じ）
  - 部屋ごとにチェックポイントと説明（ShowMessage）。落ちても、チェックポイントからやり直せる
  - ジャンプ力は -12（頂点138px）。穴・壁は、そのステージで主役の編集機能を使わないと越えられない大きさにする
  - 重要な物（扉・スイッチ・足場・トゲ）には、解き方に使わない編集操作のロックを付ける（動かして素通り、を防ぐ）
  - プレイヤー自身へのロック（player_locks）、封印するツール（allowed_edit_tools）は、ステージごとに決める
"""
import json
import os
import sys

TILE = 32
ROWS = 15
GROUND_ROW = 13
GROUND_Y = GROUND_ROW * TILE        # 地面の上面 y=416
PLAYER_TOP = GROUND_Y - 32
TILE_GRASS, TILE_DIRT, TILE_STONE = 1, 2, 6
SPEAKER = "ディレクター"
NL = chr(10)

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
STAGE_DIR = os.path.join(ROOT, "assets", "stages")
GIMMICK_JSON = os.path.join(ROOT, "assets", "gimmicks.json")

ALL_OPS = ["noScale", "noRotate", "noMove", "noFlip", "noPause", "noRewind", "noSpeed"]


def locks(allow=()):
    """allow に挙げた操作だけを許可し、あとは全部禁止するロック。allow は 'scale','rotate','move','flip','pause','rewind','speed'。"""
    d = {}
    for k in ALL_OPS:
        name = k[2:].lower()
        d[k] = (name not in allow)
    return d


# ---------------------------------------------------------------- gimmicks.json の定義
def ensure_gimmick_defs():
    with open(GIMMICK_JSON, "r", encoding="utf-8-sig") as f:
        defs = json.load(f)
    by_id = {d["id"]: d for d in defs}
    base = by_id["gim_gate_door"]

    def make(gid, name, type_enum, w, h, sprite="", ch=None, lo=-999.0, hi=999.0, se="se_size_change"):
        d = json.loads(json.dumps(base))
        d.update({"id": gid, "name": name, "type_enum": type_enum, "sprite": sprite, "spriteAlt": "",
                  "hitboxWidth": w, "hitboxHeight": h, "seActivate": se})
        if ch is not None:
            d.update({"gradeChannel": float(ch), "gradeMin": float(lo), "gradeMax": float(hi)})
        return d

    wanted = [
        make("gim_grade_sw_mono", "補正スイッチ（白黒）", 26, 64, 16, ch=2, lo=-999, hi=0.2),
        make("gim_grade_sw_hue", "補正スイッチ（色相がずれた）", 26, 64, 16, ch=3, lo=60, hi=180),
        make("gim_grade_sw_warm", "補正スイッチ（暖色）", 26, 64, 16, ch=4, lo=0.4, hi=999),
        make("gim_grade_sw_red", "補正スイッチ（赤フィルタ）", 26, 64, 16, ch=5, lo=1, hi=1),
        make("gim_grade_sw_green", "補正スイッチ（緑フィルタ）", 26, 64, 16, ch=5, lo=2, hi=2),
        make("gim_grade_sw_blue", "補正スイッチ（青フィルタ）", 26, 64, 16, ch=5, lo=3, hi=3),
        make("gim_grade_sw_dark", "補正スイッチ（暗い）", 26, 64, 16, ch=0, lo=-999, hi=0.6),
        make("gim_grade_sw_bright", "補正スイッチ（明るい）", 26, 64, 16, ch=0, lo=1.3, hi=999),
        make("gim_grade_lock_zoomin", "拡大中だけ現れる足場", 27, 96, 32, ch=6, lo=1.15, hi=999),
        make("gim_grade_lock_zoomout", "縮小中だけ現れる足場", 27, 96, 32, ch=7, lo=-999, hi=0.95),
        make("gim_rewind_gate", "逆行ゲート", 28, 32, 160, sprite="img/扉縁のみ.png", se="se_gear"),
    ]
    changed = False
    for w in wanted:
        if w["id"] in by_id:
            if by_id[w["id"]] != w:
                defs[defs.index(by_id[w["id"]])] = w
                changed = True
        else:
            defs.append(w)
            changed = True
    if changed:
        with open(GIMMICK_JSON, "w", encoding="utf-8", newline="\n") as f:
            json.dump(defs, f, ensure_ascii=False, indent=2)
        print("gimmicks.json に定義を足しました")
    return {d["id"]: d for d in defs}


GDEFS = ensure_gimmick_defs()


def gsize(gid):
    d = GDEFS[gid]
    return d["hitboxWidth"], d["hitboxHeight"]


# ---------------------------------------------------------------- ステージ
class Stage:
    def __init__(self, name, title, bgm, bg, widths, tools, player_locks, cost):
        self.name, self.title, self.bgm, self.bg = name, title, bgm, bg
        self.widths = widths                      # 部屋の幅（タイル）
        self.cols = sum(widths)
        self.tools = tools                        # allowed_edit_tools
        self.player_locks = player_locks
        self.cost = cost
        self.map = [[0] * self.cols for _ in range(ROWS)]
        for c in range(self.cols):
            self.map[GROUND_ROW][c] = TILE_GRASS
            self.map[GROUND_ROW + 1][c] = TILE_DIRT
            self.map[0][c] = TILE_STONE           # 天井（地図の上へ抜けない）
        self.pits = []                            # 穴にした列 [(c0, c1)]
        self.gimmicks, self.enemies, self.items, self.triggers = [], [], [], []
        self.messages = []
        self.goal = None
        self.starts = []                          # 部屋の開始列
        acc = 0
        for w in widths:
            self.starts.append(acc)
            acc += w
        self.rooms = 0

    # ---- 地形
    def pit(self, c0, c1):
        for c in range(c0, c1 + 1):
            self.map[GROUND_ROW][c] = 0
            self.map[GROUND_ROW + 1][c] = 0
        self.pits.append((c0, c1))

    def wall(self, c0, c1, r0, r1, tile=TILE_STONE):
        for c in range(c0, c1 + 1):
            for r in range(r0, r1 + 1):
                self.map[r][c] = tile

    # ---- 配置
    def gim(self, gid, x, y, param=None, scale=None, scale_y=None, angle=None, lock=None):
        e = {"id": gid, "x": float(x), "y": float(y)}
        if param is not None:
            e["param"] = param
        if scale is not None:
            e["scale"] = scale
        if scale_y is not None:
            e["scaleY"] = scale_y
        if angle is not None:
            e["angle"] = angle
        if lock:
            e.update(lock)
        self.gimmicks.append(e)
        return e

    def enemy(self, eid, x, y, lock=None, left=None, right=None):
        e = {"id": eid, "x": float(x), "y": float(y)}
        if left is not None:
            e["patrol_left"] = float(left)
        if right is not None:
            e["patrol_right"] = float(right)
        if lock:
            e.update(lock)
        self.enemies.append(e)

    def item(self, iid, x, y):
        self.items.append({"id": iid, "x": float(x), "y": float(y)})

    def door(self, col, param, y_off=0):
        return self.gim("gim_gate_door", col * TILE, GROUND_Y - 160 - y_off, param=param, lock=locks())

    def coins(self, room, offs):
        base = self.starts[room]
        for o in offs:
            self.item("item_coin", (base + o) * TILE, GROUND_Y - 64)

    def msg(self, text):
        self.messages.append(text)
        return {"action": "ShowMessage", "param1": text, "param2": SPEAKER, "delay": 0.0}

    def room(self, i, text, checkpoint=True):
        """部屋 i の入口：チェックポイントと、説明（入口の細い帯に入ると出る）"""
        x0 = self.starts[i] * TILE
        if i > 0 and checkpoint:
            self.gim("gimmick_checkpoint", x0 + 8, GROUND_Y - 64)
        self.triggers.append({
            "id": "r%02d_intro" % i, "x": float(x0 + 40), "y": 0.0, "width": 48.0, "height": 480.0,
            "condition": "PlayerEnter", "conditionParam": "", "oneShot": True, "actions": [self.msg(text)],
        })
        self.rooms += 1

    def finish(self, goal_room_text):
        gi = len(self.widths) - 1
        self.room(gi, goal_room_text)
        self.goal = ((self.starts[gi] + 12) * TILE, PLAYER_TOP)

    def to_json(self):
        zeros = [[0] * self.cols for _ in range(ROWS)]
        out = {
            "map_w": self.cols, "map_h": ROWS,
            "player_start": {"x": 64.0, "y": float(PLAYER_TOP)},
            "player_capabilities": {"canDoubleJump": False, "canDash": False, "canShootFireball": True, "canFly": False,
                                    "baseJumpPower": -12, "baseSpeed": 4.0},
            "allowed_edit_tools": self.tools,
            "edit_cost_settings": self.cost,
            "goal": {"x": float(self.goal[0]), "y": float(self.goal[1])},
            "bgm_id": self.bgm,
            "backgrounds": [{"sprite": "", "color": self.bg, "drawOrder": 0, "scrollRate": 0.0, "loop": True,
                             "offsetX": 0.0, "offsetY": 0.0}],
            "map": self.map, "deco_back": zeros, "deco_front": [r[:] for r in zeros],
            "triggers": self.triggers, "enemies": self.enemies, "gimmicks": self.gimmicks, "items": self.items,
            "platforms": [],
        }
        if any(self.player_locks.values()):
            out["player_locks"] = self.player_locks
        return out


def tools(**on):
    base = {"rewindEnabled": False, "pauseEnabled": False, "fastForwardEnabled": False,
            "screenEffectEnabled": False, "objectEditEnabled": False, "cutEnabled": False}
    key = {"rewind": "rewindEnabled", "pause": "pauseEnabled", "ff": "fastForwardEnabled",
           "screen": "screenEffectEnabled", "object": "objectEditEnabled", "cut": "cutEnabled"}
    for k, v in on.items():
        base[key[k]] = v
    return base


def cost(**kw):
    d = {"maxCost": 100.0, "regenPerSec": 10.0, "drainRewindPerSec": 18.0, "drainPausePerSec": 4.0,
         "drainFastForwardPerSec": 10.0, "drainScreenEffectPerSec": 8.0, "flatColorCycle": 5.0,
         "flatMenuToggle": 8.0, "flatSpeedChange": 6.0, "flatDirectionFlip": 4.0, "flatResetAll": 10.0,
         "flatCutCreate": 20.0, "cutCostPerTile": 1.2}
    d.update(kw)
    return d


ALL_PLAYER_LOCKS = {"noMove": True, "noScale": True, "noRotate": True, "noGround": True}
NO_PLAYER_LOCKS = {"noMove": False, "noScale": False, "noRotate": False, "noGround": False}


def px(col):
    return col * TILE


# ================================================================= ① サイズと角度
def stage1():
    s = Stage("puzzle_1_size.json", "サイズと角度", "bgm_editing_05", "#CFE3F5", [24, 24, 24, 24, 24, 24],
              tools(object=True), ALL_PLAYER_LOCKS, cost())
    # R0：箱を広げて重量スイッチへ（ホイール＝拡大縮小）。スイッチ・扉は何も動かせない
    x0 = s.starts[0]
    s.room(0, "ブロックを右クリックで選んで、ホイールで\n横に広げましょう。広いブロックが重量スイッチ\nに乗ると、この先の扉が開きます。")
    s.gim("gim_scalable_box", px(x0 + 6), GROUND_Y - 50, lock=locks(allow=("scale",)))
    s.gim("gim_weight_switch", px(x0 + 10), GROUND_Y - 10, param="s1a", lock=locks())
    s.door(x0 + 22, "s1a")
    s.coins(0, [4, 15])
    # R1：手動橋を水平に倒して、広げて、14タイルの谷にかける
    x0 = s.starts[1]
    s.room(1, "橋は、立っていて渡れません。Ctrl を押しながら\nホイールで水平に倒し、ホイールだけで長く\n伸ばして、谷にかけましょう。")
    # 橋の足場は「中心の高さ」にある（水平にしたとき、上面ではなく中心線の高さで立てる）ので、中心を地面の高さに合わせる
    s.pit(x0 + 8, x0 + 19)
    s.gim("gim_manual_bridge", px(x0 + 8) - 24, GROUND_Y - 8, lock=locks(allow=("scale", "rotate")))
    s.coins(1, [3, 22])
    # R2：トゲの列を、範囲選択して傾けて安全な足場にする
    x0 = s.starts[2]
    s.room(2, "トゲに触れるとやり直しです。何もない所を\nドラッグしてトゲを全部囲み、Ctrl+ホイールで\n45度以上傾けると、安全になります。")
    for k in range(9):
        s.gim("gim_spikes", px(x0 + 8 + k), GROUND_Y - 32, lock=locks(allow=("rotate",)))
    s.coins(2, [3, 21])
    # R3：小さな足場を横に伸ばして、穴をふさぐ
    x0 = s.starts[3]
    s.room(3, "穴に落ちるとやり直しです。足場を選んで、\nホイールで横に伸ばし、穴の向こう側まで\n届かせましょう。")
    s.pit(x0 + 8, x0 + 15)
    s.gim("gim_scalable_ground", px(x0 + 8) - 64, GROUND_Y, lock=locks(allow=("scale",)))
    s.coins(3, [3, 20])
    # R4：高い扉（壁）を、倒して通れるようにする
    x0 = s.starts[4]
    s.room(4, "高い壁はジャンプで越えられません。扉を選んで\nCtrl+ホイールで45度以上倒すと、壁ではなく\n床になって、下をくぐれます。")
    s.gim("gim_gate_door", px(x0 + 10), GROUND_Y - 160, param="tipS1", lock=locks(allow=("rotate",)))
    s.coins(4, [3, 18])
    s.finish("サイズと角度の編集は、ここまで！\nゴールして、次のステージへ。")
    return s


# ================================================================= ② 色
def stage2():
    s = Stage("puzzle_2_color.json", "色", "bgm_editing_06", "#F1E1EA", [36, 24, 24, 28, 24],
              tools(screen=True), NO_PLAYER_LOCKS, cost(regenPerSec=10.0))
    # R0：赤・緑・青の補正ロック足場の橋（色フィルタは、島で切り替える）
    x0 = s.starts[0]
    s.room(0, "色の足場は、T で合わせた色のときだけ現れます。\n島で T を押して、赤→緑→青の順に色を変え、\n同じ色の橋を渡りましょう。")
    # 島 A：0〜5、赤の穴：6〜11、島 B：12〜15、緑：16〜21、島 C：22〜25、青：26〜31、島 D：32〜35
    for (c0, c1, param) in ((6, 11, "1"), (16, 21, "2"), (26, 31, "3")):
        s.pit(x0 + c0, x0 + c1)
        s.gim("gim_color_lock", px(x0 + c0), GROUND_Y, param=param, lock=locks())
        s.gim("gim_color_lock", px(x0 + c0 + 3), GROUND_Y, param=param, lock=locks())
    s.coins(0, [2, 14, 24])
    # R1：彩度ドア（色の輪の右のパネルで、彩度を下げて白黒にする）
    x0 = s.starts[1]
    s.room(1, "右のパネルの「彩度」を下げて、画面を白黒に\nすると、この先の扉が開きます。")
    s.gim("gim_grade_sw_mono", px(x0 + 11), GROUND_Y - 16, param="c1", lock=locks())
    s.door(x0 + 14, "c1")
    s.coins(1, [4, 19])
    # R2：色相・色温度の扉
    x0 = s.starts[2]
    s.room(2, "「色相」をずらすと1枚目、「色温度」を暖色に\nすると2枚目の扉が開きます。（元に戻すと閉まる\nので、つけたまま通りましょう）")
    s.gim("gim_grade_sw_hue", px(x0 + 5), GROUND_Y - 16, param="c2a", lock=locks())
    s.door(x0 + 8, "c2a")
    s.gim("gim_grade_sw_warm", px(x0 + 13), GROUND_Y - 16, param="c2b", lock=locks())
    s.door(x0 + 16, "c2b")
    s.coins(2, [3, 12, 21])
    # R3：赤・緑・青の補正スイッチ扉を3枚
    x0 = s.starts[3]
    s.room(3, "扉の手前の板が、開ける色です。T で色を\n赤→緑→青と変えながら、3枚の扉を順に\n通りましょう。")
    for k, (swid, par) in enumerate((("gim_grade_sw_red", "c3a"), ("gim_grade_sw_green", "c3b"), ("gim_grade_sw_blue", "c3c"))):
        s.gim(swid, px(x0 + 5 + k * 6), GROUND_Y - 16, param=par, lock=locks())
        s.door(x0 + 8 + k * 6, par)
    s.coins(3, [3, 25])
    s.finish("色の編集は、ここまで！\nゴールして、次のステージへ。")
    return s


# ================================================================= ③ 明るさ・暗さ
def stage3():
    s = Stage("puzzle_3_bright.json", "明るさと暗さ", "bgm_editing_07", "#BCC9E0", [24, 24, 30, 24, 30, 24],
              tools(screen=True), NO_PLAYER_LOCKS, cost(regenPerSec=10.0))
    x0 = s.starts[0]
    s.room(0, "暗いときだけ現れる足場です。X を押している間、\n画面が暗くなります。押しながら、谷を渡りましょう。")
    s.pit(x0 + 8, x0 + 13)
    s.gim("gim_brightness_lock", px(x0 + 8), GROUND_Y, param="dark", lock=locks())
    s.gim("gim_brightness_lock", px(x0 + 11), GROUND_Y, param="dark", lock=locks())
    s.coins(0, [3, 19])
    x0 = s.starts[1]
    s.room(1, "明るいときだけ現れる足場です。C を押している間、\n画面が明るくなります。押しながら、谷を渡りましょう。")
    s.pit(x0 + 8, x0 + 13)
    s.gim("gim_brightness_lock", px(x0 + 8), GROUND_Y, param="bright", lock=locks())
    s.gim("gim_brightness_lock", px(x0 + 11), GROUND_Y, param="bright", lock=locks())
    s.coins(1, [3, 19])
    # R2：暗闇の幽霊の谷（暗い領域は、Cで明るくすると幽霊も消える）
    x0 = s.starts[2]
    s.room(2, "ここは暗い領域で、幽霊が実体化しています。\nC で明るくすると、幽霊は消え、明るいときだけの\n足場が現れます。押しながら渡りましょう。")
    s.pit(x0 + 8, x0 + 19)
    for k in range(4):
        s.gim("gim_brightness_lock", px(x0 + 8) + k * 96, GROUND_Y, param="bright", lock=locks())
    # 領域（200x200 を 4.8倍 × 2.4倍 = 960x480）
    s.gim("gim_brightness_zone", px(x0), 0, scale=4.8, scale_y=2.4, lock=locks())
    s.enemy("enemy_yurei", px(x0 + 11), 230, left=px(x0 + 8), right=px(x0 + 17), lock=locks())
    s.enemy("enemy_yurei", px(x0 + 17), 280, left=px(x0 + 10), right=px(x0 + 20), lock=locks())
    s.coins(2, [3, 25])
    # R3：明るさの敵（近づくと暗くする。Cで打ち消す）
    x0 = s.starts[3]
    s.room(3, "明るさの敵は、近づくと画面を暗くします。\nC で明るくすると打ち消せるので、明るいときだけの\n足場を渡りましょう。")
    s.pit(x0 + 8, x0 + 15)
    for k in range(3):
        s.gim("gim_brightness_lock", px(x0 + 8) - 16 + k * 96, GROUND_Y, param="bright", lock=locks())
    s.enemy("enemy_akarusa", px(x0 + 4), GROUND_Y - 50, lock=locks())
    s.coins(3, [2, 20])
    # R4：暗いと開く扉 → 明るいと開く扉
    x0 = s.starts[4]
    s.room(4, "1枚目の扉は、暗いと開きます（X）。\n2枚目の扉は、明るいと開きます（C）。\n押しながら、順に通りましょう。")
    s.gim("gim_grade_sw_dark", px(x0 + 5), GROUND_Y - 16, param="b1", lock=locks())
    s.door(x0 + 9, "b1")
    s.gim("gim_grade_sw_bright", px(x0 + 15), GROUND_Y - 16, param="b2", lock=locks())
    s.door(x0 + 19, "b2")
    s.coins(4, [3, 13, 26])
    s.finish("明るさの編集は、ここまで！\nゴールして、次のステージへ。")
    return s


# ================================================================= ④ ズーム
def stage4():
    s = Stage("puzzle_4_zoom.json", "ズーム", "bgm_editing_08", "#D8E4CF", [24, 24, 30, 36, 24],
              tools(screen=True), NO_PLAYER_LOCKS, cost(regenPerSec=10.0))
    x0 = s.starts[0]
    s.room(0, "拡大中だけ現れる足場です。Z を押している間、\n画面が拡大されます。押したまま、谷を渡りましょう。")
    s.pit(x0 + 8, x0 + 13)
    s.gim("gim_grade_lock_zoomin", px(x0 + 8), GROUND_Y, lock=locks())
    s.gim("gim_grade_lock_zoomin", px(x0 + 11), GROUND_Y, lock=locks())
    s.coins(0, [3, 19])
    x0 = s.starts[1]
    s.room(1, "縮小中だけ現れる足場です。V を押している間、\n画面が広く映ります。押したまま、谷を渡りましょう。")
    s.pit(x0 + 8, x0 + 13)
    s.gim("gim_grade_lock_zoomout", px(x0 + 8), GROUND_Y, lock=locks())
    s.gim("gim_grade_lock_zoomout", px(x0 + 11), GROUND_Y, lock=locks())
    s.coins(1, [3, 19])
    # R2：ズーム固定の敵（倒さないと、縮小が使えない）
    x0 = s.starts[2]
    s.room(2, "ズーム固定の敵に近づくと、画面が寄って固定され、\n縮小も使えません。弾（Enter）で倒してから、\n縮小中だけの足場を渡りましょう。")
    s.enemy("enemy_zoom", px(x0 + 6), GROUND_Y - 42, lock=locks())
    s.pit(x0 + 14, x0 + 19)
    s.gim("gim_grade_lock_zoomout", px(x0 + 14), GROUND_Y, lock=locks())
    s.gim("gim_grade_lock_zoomout", px(x0 + 17), GROUND_Y, lock=locks())
    s.coins(2, [3, 25])
    # R3：拡大の橋 → 島 → 縮小の橋
    x0 = s.starts[3]
    s.room(3, "最初の橋は、Z（拡大）で現れます。島で Z を離し、\n次の橋は V（縮小）で現れます。押し分けて渡りましょう。")
    s.pit(x0 + 6, x0 + 11)
    s.gim("gim_grade_lock_zoomin", px(x0 + 6), GROUND_Y, lock=locks())
    s.gim("gim_grade_lock_zoomin", px(x0 + 9), GROUND_Y, lock=locks())
    s.pit(x0 + 18, x0 + 23)
    s.gim("gim_grade_lock_zoomout", px(x0 + 18), GROUND_Y, lock=locks())
    s.gim("gim_grade_lock_zoomout", px(x0 + 21), GROUND_Y, lock=locks())
    s.coins(3, [3, 14, 30])
    s.finish("ズームの編集は、ここまで！\nゴールして、次のステージへ。")
    return s


# ================================================================= ⑤ カット
def stage5():
    s = Stage("puzzle_5_cut.json", "カット", "bgm_editing_09", "#E6D9C9", [36, 30, 36, 44, 40, 24],
              tools(cut=True), NO_PLAYER_LOCKS,
              cost(maxCost=100.0, regenPerSec=3.0, flatCutCreate=10.0, cutCostPerTile=4.0))
    x0 = s.starts[0]
    s.room(0, "トゲは12タイルもあって、跳べません。画面下の\nタイムラインを Ctrl+クリックで2回打つと、その\n区間を飛ばせます。トゲの前から後ろまで。")
    for k in range(12):
        s.gim("gim_spikes", px(x0 + 12 + k), GROUND_Y - 32)
    s.coins(0, [4, 30])
    x0 = s.starts[1]
    s.room(1, "高い壁は、ジャンプで越えられません。\nカットで、壁の前から後ろまでを飛ばしましょう。")
    s.wall(x0 + 12, x0 + 17, 1, 12)
    s.coins(1, [4, 25])
    x0 = s.starts[2]
    s.room(2, "大きな穴です。カットのコストは、飛ばす長さで\n決まります。ゲージが足りるように、穴の前から\n後ろまで、ぎりぎりで打ちましょう。")
    s.pit(x0 + 8, x0 + 25)
    s.coins(2, [3, 31])
    x0 = s.starts[3]
    s.room(3, "壁が2枚あります。1回で両方を飛ばすか、\n2回に分けて飛ばしましょう（ゲージは時間で\n回復します）。")
    s.wall(x0 + 10, x0 + 15, 1, 12)
    s.wall(x0 + 24, x0 + 29, 1, 12)
    s.coins(3, [4, 20, 38])
    x0 = s.starts[4]
    s.room(4, "壁の向こうに鍵があります。カットで壁を越えて\n鍵を取ると、奥の扉が開きます。")
    s.wall(x0 + 8, x0 + 13, 1, 12)
    s.item("item_edit_keyreel", px(x0 + 20), GROUND_Y - 64)
    s.door(x0 + 30, "k5")
    s.triggers.append({
        "id": "r04_key", "x": 0.0, "y": 0.0, "width": 64.0, "height": 480.0,
        "condition": "ItemCollected", "conditionParam": "item_edit_keyreel", "oneShot": True,
        "actions": [{"action": "OpenDoor", "param1": "gim_gate_door", "param2": "k5", "delay": 0.0},
                    s.msg("鍵を取りました！\n奥の扉が開きました。")],
    })
    s.coins(4, [4, 26, 36])
    s.finish("カットの編集は、ここまで！\nゴールして、次のステージへ。")
    return s


# ================================================================= ⑥ 巻き戻し
def stage6():
    s = Stage("puzzle_6_rewind.json", "巻き戻し", "bgm_editing_05", "#D5CFE8", [24, 30, 24, 40, 24],
              tools(rewind=True, object=True), ALL_PLAYER_LOCKS,
              cost(maxCost=100.0, regenPerSec=12.0, drainRewindPerSec=18.0))
    rw = locks(allow=("rewind",))
    x0 = s.starts[0]
    s.room(0, "逆行ゲートは、どれかの物が巻き戻し中のあいだ\nだけ通れます。ブロックを選んで、R を押し\nながら（時間は戻らず）ゲートを通りましょう。")
    s.gim("gim_scalable_box", px(x0 + 6), GROUND_Y - 50, lock=rw)
    s.gim("gim_rewind_gate", px(x0 + 12), GROUND_Y - 160, lock=locks())
    s.coins(0, [3, 18])
    x0 = s.starts[1]
    s.room(1, "ゲートが3枚あります。右クリックのメニューで\n物の巻き戻しを入れておくと、押し続けなくても、\n3枚を続けて通れます。")
    s.gim("gim_scalable_box", px(x0 + 3), GROUND_Y - 50, lock=rw)
    for c in (9, 15, 21):
        s.gim("gim_rewind_gate", px(x0 + c), GROUND_Y - 160, lock=locks())
    s.coins(1, [5, 27])
    x0 = s.starts[2]
    s.room(2, "敵も巻き戻せます。歩く敵を選んで巻き戻しを\n入れると、ゲートが通れます。敵は倒すと選べなく\nなるので、ブロックも使えます。")
    s.enemy("enemy_aruku", px(x0 + 6), GROUND_Y - 34, left=px(x0 + 4), right=px(x0 + 9), lock=rw)
    s.gim("gim_scalable_box", px(x0 + 3), GROUND_Y - 50, lock=rw)
    s.gim("gim_rewind_gate", px(x0 + 14), GROUND_Y - 160, lock=locks())
    s.coins(2, [2, 19])
    x0 = s.starts[3]
    s.room(3, "ゲートが4枚並んでいます。巻き戻しはゲージを\n使い続けるので、ゲートの前で入れて、通ったら\n切りましょう。")
    s.gim("gim_scalable_box", px(x0 + 3), GROUND_Y - 50, lock=rw)
    for c in (9, 15, 21, 27):
        s.gim("gim_rewind_gate", px(x0 + c), GROUND_Y - 160, lock=locks())
    s.coins(3, [5, 18, 33])
    s.finish("巻き戻しの編集は、ここまで！\nお疲れさまでした。")
    return s


# ---------------------------------------------------------------- 検査
def display_width(line):
    w = 0.0
    for ch in line:
        w += 0.5 if ord(ch) < 0x80 or 0xFF61 <= ord(ch) <= 0xFF9F else 1.0
    return w


def validate(s, data):
    errs = []
    m = data["map"]
    for r in range(ROWS):
        for c, t in enumerate(m[r]):
            if t in (11, 12):
                errs.append("致死タイル (%d,%d)" % (c, r))
    pit_cols = set()
    for (c0, c1) in s.pits:
        pit_cols.update(range(c0, c1 + 1))
    for c in range(s.cols):
        has = m[GROUND_ROW][c] != 0 and m[GROUND_ROW + 1][c] != 0
        if not has and c not in pit_cols:
            errs.append("宣言していない穴 col=%d" % c)
    for t in s.messages:
        lines = t.split("\n")
        if len(lines) > 3:
            errs.append("メッセージが4行以上: " + t[:20])
        for ln in lines:
            if display_width(ln) > 28:
                errs.append("メッセージの行が長い(%.1f): %s" % (display_width(ln), ln))
    # チェックポイント・開始位置・ゴールの足元が地面か
    def has_floor(x):
        c = int(x // TILE)
        return 0 <= c < s.cols and m[GROUND_ROW][c] != 0
    if not has_floor(64):
        errs.append("開始位置の足元に地面が無い")
    for g in data["gimmicks"]:
        if g["id"] == "gimmick_checkpoint" and not has_floor(g["x"]):
            errs.append("チェックポイントの足元が穴 x=%g" % g["x"])
    if not has_floor(data["goal"]["x"]):
        errs.append("ゴールの足元が穴")
    # ギミックが壁・天井タイルに埋まっていないか
    for g in data["gimmicks"]:
        if g["id"] == "gim_brightness_zone":
            continue                      # 領域は、地形と重なってよい（当たり判定の無い範囲）
        w, h = gsize(g["id"])
        w = w * g.get("scale", 1.0)
        h = h * g.get("scaleY", g.get("scale", 1.0))
        x1, y1 = g["x"], g["y"]
        for r in range(ROWS):
            for c in range(s.cols):
                if m[r][c] == 0:
                    continue
                if x1 < (c + 1) * TILE and x1 + w > c * TILE and y1 < (r + 1) * TILE and y1 + h > r * TILE:
                    if r >= GROUND_ROW and y1 + h <= GROUND_Y + 0.01:
                        continue          # 地面の上に載っている
                    if r >= GROUND_ROW and abs(y1 - GROUND_Y) < 0.01:
                        continue          # 上面を地面に合わせた足場（谷の両端で、地面に少し重なる）
                    if r >= GROUND_ROW and abs(y1 + h / 2 - GROUND_Y) < 0.01:
                        continue          # 中心の高さを地面に合わせた足場（手動橋）
                    errs.append("ギミックが地形に埋まっている %s (%g,%g) tile(%d,%d)" % (g["id"], x1, y1, c, r))
                    break
    # 扉は、開ける手段（スイッチ・補正スイッチ・OpenDoor）が対応しているか。倒して通る扉は "tip" で始まる
    switch_params = {g.get("param") for g in data["gimmicks"]
                     if g["id"] == "gim_weight_switch" or g["id"].startswith("gim_grade_sw_")}
    opened = {a["param2"] for t in data["triggers"] for a in t["actions"] if a["action"] == "OpenDoor"}
    for g in data["gimmicks"]:
        if g["id"] == "gim_gate_door":
            p = g.get("param", "")
            if p not in switch_params and p not in opened and not p.startswith("tip"):
                errs.append("開ける手段の無い扉 param=%s" % p)
    if errs:
        print("【検査NG】", s.name)
        for e in errs:
            print("  -", e)
        sys.exit(1)
    print("検査OK %s: %d列, 部屋 %d, ギミック %d, 敵 %d, アイテム %d" % (
        s.name, s.cols, s.rooms, len(data["gimmicks"]), len(data["enemies"]), len(data["items"])))


def main():
    results = []
    for build in (stage1, stage2, stage3, stage4, stage5, stage6):
        s = build()
        data = s.to_json()
        validate(s, data)
        path = os.path.join(STAGE_DIR, s.name)
        with open(path, "w", encoding="utf-8", newline="\n") as f:
            json.dump(data, f, ensure_ascii=False, indent=2)
        results.append((s.name, s.title, len(data["items"])))
    for name, title, n in results:
        print("書き出し:", name, "「%s」" % title, "item_total=%d" % n)


if __name__ == "__main__":
    main()
