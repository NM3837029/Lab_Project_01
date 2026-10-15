# -*- coding: utf-8 -*-
"""編集機能ステージ（puzzle_1〜6）を作るための道具。ステージの中身は tools/gen_puzzle_stages.py にある。

・Stage …… 「列ごとの地面の高さ」から地形を作り、敵・ギミック・アイテムを置いて、ステージJSONへ書き出す
・check_reach …… 歩く・跳ぶ・落ちるを物理どおりに真似て、スタートからゴールへ行けるかを調べる
・preview …… ステージ全体を文字で描く（目で見て、道のかたちを確かめるため）

ステージの作りの約束
  - 説明の窓（ShowMessage）は出さない。部屋で区切らず、途切れずに続く1本の道にする
  - ジャンプ力は -12（頂点138px、重力0.5）。歩く速さは4px/フレーム。タイル1マス=32px、地面の上面は y=416
  - 重要な物（扉・スイッチ・足場・トゲ）には、解き方に使わない編集操作のロックを付ける
"""
import json
import math
import os
import sys

TILE = 32
ROWS = 15
GROUND_ROW = 13
GROUND_Y = GROUND_ROW * TILE        # 平らな地面の上面 y=416
PLAYER_H = 32

T_GRASS, T_DIRT, T_DIRT2, T_DIRT3, T_WOOD, T_STONE = 1, 2, 3, 4, 5, 6
T_SPIKE, T_SPIKEBLOCK, T_THIN, T_BRITTLE_WOOD, T_SOFT_DIRT, T_SOFT_WALL = 11, 12, 13, 14, 15, 16
T_HARD_WALL = 17      # 1.5倍以上に大きくした敵の体当たりでしか崩れない壁（このスクリプトが tiles.json に足す）
# 当たり判定のあるタイル（assets/tiles.json の collidable）
SOLID_TILES = {1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 12, 13, 14, 15, 16, 17}
DEADLY_TILES = {11, 12}

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
STAGE_DIR = os.path.join(ROOT, "assets", "stages")
ASSET_DIR = os.path.join(ROOT, "assets")

# 敵の当たり判定の高さ（assets/enemies.json の hitboxHeight）。地面に置くとき y = 地面 - 高さ にする
ENEMY_H = {
    "enemy_aruku": 34, "enemy_oikake": 37, "enemy_hikou": 46, "enemy_teleport": 37, "enemy_dossun": 46,
    "enemy_yurei": 48, "enemy_taihou": 40, "enemy_houdai": 46, "enemy_akarusa": 50, "enemy_iro": 53,
    "enemy_zoom": 42, "enemy_kaiten": 47, "enemy_size": 68, "enemy_imomushi": 34, "enemy_kikai_imomushi": 34,
}

ALL_OPS = ["noScale", "noRotate", "noMove", "noFlip", "noPause", "noRewind", "noSpeed"]


def locks(allow=()):
    """allow に挙げた操作だけを許可し、あとは全部禁止するロック。allow は 'scale','rotate','move','flip','pause','rewind','speed'。"""
    d = {}
    for k in ALL_OPS:
        d[k] = (k[2:].lower() not in allow)
    return d


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


class Defs:
    """assets/gimmicks.json と enemies.json の定義（大きさの参照と、足りない定義の追加）"""

    def __init__(self):
        self.gim_path = os.path.join(ASSET_DIR, "gimmicks.json")
        self.ene_path = os.path.join(ASSET_DIR, "enemies.json")
        with open(self.gim_path, "r", encoding="utf-8-sig") as f:
            self.gims = json.load(f)
        with open(self.ene_path, "r", encoding="utf-8-sig") as f:
            self.enes = json.load(f)
        self.tile_path = os.path.join(ASSET_DIR, "tiles.json")
        with open(self.tile_path, "r", encoding="utf-8-sig") as f:
            self.tiles = json.load(f)
        self.dirty_g = False
        self.dirty_e = False
        self.dirty_t = False

    def gim(self, gid):
        for d in self.gims:
            if d["id"] == gid:
                return d
        raise KeyError(gid)

    def ene(self, eid):
        for d in self.enes:
            if d["id"] == eid:
                return d
        raise KeyError(eid)

    def put_gim(self, d):
        for i, o in enumerate(self.gims):
            if o["id"] == d["id"]:
                if o != d:
                    self.gims[i] = d
                    self.dirty_g = True
                return
        self.gims.append(d)
        self.dirty_g = True

    def put_ene(self, d):
        for i, o in enumerate(self.enes):
            if o["id"] == d["id"]:
                if o != d:
                    self.enes[i] = d
                    self.dirty_e = True
                return
        self.enes.append(d)
        self.dirty_e = True

    def put_tile(self, d):
        for i, o in enumerate(self.tiles):
            if o["id"] == d["id"]:
                if o != d:
                    self.tiles[i] = d
                    self.dirty_t = True
                return
        self.tiles.append(d)
        self.dirty_t = True

    def derive_gim(self, base_id, new_id, name, **kw):
        d = json.loads(json.dumps(self.gim(base_id)))
        d.update({"id": new_id, "name": name})
        d.update(kw)
        self.put_gim(d)

    def derive_ene(self, base_id, new_id, name, **kw):
        d = json.loads(json.dumps(self.ene(base_id)))
        d.update({"id": new_id, "name": name})
        d.update(kw)
        self.put_ene(d)

    def save(self):
        if self.dirty_g:
            with open(self.gim_path, "w", encoding="utf-8", newline="\n") as f:
                json.dump(self.gims, f, ensure_ascii=False, indent=2)
            print("gimmicks.json を更新しました")
        if self.dirty_t:
            with open(self.tile_path, "w", encoding="utf-8", newline=chr(10)) as f:
                json.dump(self.tiles, f, ensure_ascii=False, indent=2)
            print("tiles.json を更新しました")
        if self.dirty_e:
            with open(self.ene_path, "w", encoding="utf-8", newline="\n") as f:
                json.dump(self.enes, f, ensure_ascii=False, indent=2)
            print("enemies.json を更新しました")


class Stage:
    def __init__(self, file, title, bgm, bg, cols, tool_flags, player_locks, cost_settings, defs, bg2=None):
        self.file, self.title, self.bgm, self.bg, self.bg2 = file, title, bgm, bg, bg2
        self.cols = cols
        self.tools = tool_flags
        self.player_locks = player_locks
        self.cost = cost_settings
        self.defs = defs
        self.top = [GROUND_ROW] * cols          # 列ごとの地面の上面の行（None = 穴）
        self.surf = [T_GRASS] * cols            # 地面の一番上のタイル
        self.fill = [T_DIRT] * cols             # その下を埋めるタイル
        self.over = {}                          # (行, 列) -> タイル。地面を描いたあとに重ねる（足場・壁・天井）
        self.gimmicks, self.enemies, self.items, self.triggers = [], [], [], []
        self.goal = None
        self.start = (64.0, 0.0)
        self.solved_adds = []                   # 解いたあとに足場になる矩形 (x,y,w,h) px
        self.solved_cuts = []                   # 解いたあとに消える地形 (列0,列1,行0,行1)
        self.solved_free = []                   # 解いたあとに通れる（遮らない）ギミックの矩形
        self.notes = []

    # ------------------------------------------------------------ 地形
    def hill(self, c0, c1, top, surf=T_GRASS, fill=T_DIRT):
        for c in range(c0, c1 + 1):
            self.top[c], self.surf[c], self.fill[c] = top, surf, fill

    def pit(self, c0, c1):
        for c in range(c0, c1 + 1):
            self.top[c] = None

    def ramp(self, c0, c1, top0, top1, surf=T_GRASS, fill=T_DIRT):
        """c0〜c1 を階段で top0 から top1 へつなぐ（1列ごとに最大1段）"""
        n = c1 - c0
        for i in range(n + 1):
            t = int(round(top0 + (top1 - top0) * (i / n))) if n else top1
            self.top[c0 + i], self.surf[c0 + i], self.fill[c0 + i] = t, surf, fill

    def plat(self, c0, c1, row, tile=T_WOOD):
        for c in range(c0, c1 + 1):
            self.over[(row, c)] = tile

    def block(self, c0, c1, r0, r1, tile=T_STONE):
        for c in range(c0, c1 + 1):
            for r in range(r0, r1 + 1):
                self.over[(r, c)] = tile

    def ceil(self, c0, c1, r1, tile=T_STONE):
        """c0〜c1 の天井を、行0〜r1 まで下げる"""
        self.block(c0, c1, 0, r1, tile)

    def floor_y(self, col):
        t = self.top[col]
        return None if t is None else t * TILE

    # ------------------------------------------------------------ 配置
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

    def gim_on(self, gid, col, dy=0, **kw):
        """列 col の地面の上に、ギミックの下辺をそろえて置く（dy だけ浮かせる）"""
        h = self.defs.gim(gid)["hitboxHeight"]
        sc = kw.get("scale_y") or kw.get("scale") or 1.0
        return self.gim(gid, col * TILE, self.floor_y(col) - h * sc - dy, **kw)

    def enemy(self, eid, x, y, lock=None, left=None, right=None, angle=None, scale=None):
        e = {"id": eid, "x": float(x), "y": float(y)}
        if left is not None:
            e["patrol_left"] = float(left)
        if right is not None:
            e["patrol_right"] = float(right)
        if angle is not None:
            e["angle"] = angle
        if scale is not None:
            e["scale"] = scale
        if lock:
            e.update(lock)
        self.enemies.append(e)
        return e

    def enemy_on(self, eid, col, patrol=None, dy=0, **kw):
        """列 col の地面の上に敵を置く。patrol=(左列, 右列) で見回る範囲"""
        h = ENEMY_H.get(eid)
        if h is None:
            h = self.defs.ene(eid)["hitboxHeight"]
        y = self.floor_y(col) - h - 2 - dy
        left = right = None
        if patrol:
            left, right = patrol[0] * TILE, patrol[1] * TILE
        return self.enemy(eid, col * TILE + 4, y, left=left, right=right, **kw)

    def enemy_at(self, eid, col, floor_row, patrol=None, dy=0, **kw):
        """行 floor_row（浮いた足場の上面の行）の上に敵を置く"""
        h = ENEMY_H.get(eid)
        if h is None:
            h = self.defs.ene(eid)["hitboxHeight"]
        left = right = None
        if patrol:
            left, right = patrol[0] * TILE, patrol[1] * TILE
        return self.enemy(eid, col * TILE + 4, floor_row * TILE - h - 2 - dy, left=left, right=right, **kw)

    def item(self, iid, x, y):
        self.items.append({"id": iid, "x": float(x), "y": float(y)})

    def coin(self, col, dy=64):
        self.item("item_coin", col * TILE, self.floor_y(col) - dy)

    def coins(self, cols, dy=64):
        for c in cols:
            self.coin(c, dy)

    def coin_at(self, col, row):
        """列 col・行 row（上端）の位置にコインを置く（浮いた足場の上など）"""
        self.item("item_coin", col * TILE, row * TILE)

    def checkpoint(self, col):
        self.gim("gimmick_checkpoint", col * TILE + 8, self.floor_y(col) - 64)

    def door(self, col, param, lock=None):
        return self.gim("gim_gate_door", col * TILE, self.floor_y(col) - 160, param=param, lock=lock or locks())

    def set_goal(self, col):
        self.goal = (col * TILE, self.floor_y(col) - 32)

    # ------------------------------------------------------------ 書き出し
    def build_map(self):
        m = [[0] * self.cols for _ in range(ROWS)]
        for c in range(self.cols):
            t = self.top[c]
            m[0][c] = T_STONE                    # 天井（地図の上へ抜けない）
            if t is None:
                continue
            m[t][c] = self.surf[c]
            for r in range(t + 1, ROWS):
                fill = self.fill[c]
                if fill == T_DIRT and (r + c) % 3 == 1:
                    fill = T_DIRT2
                elif fill == T_DIRT and (r + c) % 3 == 2:
                    fill = T_DIRT3
                m[r][c] = fill
        for (r, c), tile in self.over.items():
            m[r][c] = tile
        return m

    def to_json(self):
        zeros = [[0] * self.cols for _ in range(ROWS)]
        sx, sy = self.start
        if sy == 0.0:
            sy = self.floor_y(int(sx // TILE)) - PLAYER_H
        bgs = [{"sprite": "", "color": self.bg, "drawOrder": 0, "scrollRate": 0.0, "loop": True,
                "offsetX": 0.0, "offsetY": 0.0}]
        out = {
            "map_w": self.cols, "map_h": ROWS,
            "player_start": {"x": float(sx), "y": float(sy)},
            "player_capabilities": {"canDoubleJump": False, "canDash": False, "canShootFireball": True, "canFly": False,
                                    "baseJumpPower": -12, "baseSpeed": 4.0},
            "allowed_edit_tools": self.tools,
            "edit_cost_settings": self.cost,
            "goal": {"x": float(self.goal[0]), "y": float(self.goal[1])},
            "bgm_id": self.bgm,
            "backgrounds": bgs,
            "map": self.build_map(), "deco_back": zeros, "deco_front": [r[:] for r in zeros],
            "triggers": self.triggers, "enemies": self.enemies, "gimmicks": self.gimmicks, "items": self.items,
            "platforms": [],
        }
        if any(self.player_locks.values()):
            out["player_locks"] = self.player_locks
        return out


# ====================================================================== 検査
class Reach:
    """歩く・跳ぶ・落ちるを真似て、スタートからゴールへ行けるかを調べる。

    ・箱は 32x32。重力0.5、ジャンプ -12、歩き4px/フレーム、落下の最高速15
    ・ジャンプは「押し続ける」だけを使う（天井が低いと頭をぶつけて落ちる）
    ・扉などの「閉じている物」は壁として扱い、解いたあとの状態（solved_adds / solved_cuts）を別に足す
    """

    def __init__(self, s, data, solved=True):
        self.s = s
        self.cols = s.cols
        self.m = [row[:] for row in data["map"]]
        if solved:
            for (c0, c1, r0, r1) in s.solved_cuts:
                for r in range(r0, r1 + 1):
                    for c in range(c0, c1 + 1):
                        self.m[r][c] = 0
        self.rects = []                      # 壁として扱う矩形 (x, y, w, h)
        self.hazards = []                    # 触れたら死ぬ矩形
        for g in data["gimmicks"]:
            gid = g["id"]
            d = s.defs.gim(gid)
            w, h = d["hitboxWidth"], d["hitboxHeight"]
            sc = g.get("scale", 1.0)
            r = (g["x"], g["y"], w * sc, h * g.get("scaleY", sc))
            if gid == "gim_spikes":
                if solved and s.solved_no_spikes:
                    continue                 # 解いたあと（傾けて安全にした）は、トゲを無いものとして扱う
                self.hazards.append((r[0] + 4, r[1] + 8, r[2] - 8, r[3] - 8))
            elif gid in ("gim_gate_door", "gim_ff_gate", "gim_rewind_gate", "gim_rock", "gim_scalable_box",
                         "gim_breakable", "gim_breakable_heavy", "gim_chikuwa") and not solved:
                self.rects.append(r)
            elif gid in ("gim_gate_door", "gim_ff_gate", "gim_rewind_gate", "gim_rock", "gim_scalable_box",
                         "gim_breakable", "gim_breakable_heavy", "gim_chikuwa"):
                if not any(abs(r[0] - fr[0]) < 1 and abs(r[1] - fr[1]) < 1 for fr in s.solved_free):
                    self.rects.append(r)
        if solved:
            self.rects.extend(s.solved_adds)
        self.goal = (data["goal"]["x"], data["goal"]["y"], 32, 32)

    def solid_at(self, c, r):
        if c < 0 or c >= self.cols:
            return True
        if r < 0:
            return True
        if r >= ROWS:
            return False
        return self.m[r][c] in SOLID_TILES

    def hits(self, x, y):
        """箱 (x, y, 32, 32) が壁に重なるか"""
        c0, c1 = int(math.floor(x / TILE)), int(math.floor((x + 31.0) / TILE))
        r0, r1 = int(math.floor(y / TILE)), int(math.floor((y + 31.0) / TILE))
        for r in range(r0, r1 + 1):
            for c in range(c0, c1 + 1):
                if self.solid_at(c, r):
                    return True
        for (rx, ry, rw, rh) in self.rects:
            if x < rx + rw and x + 32 > rx and y < ry + rh and y + 32 > ry:
                return True
        return False

    def deadly(self, x, y):
        c0, c1 = int(math.floor(x / TILE)), int(math.floor((x + 31.0) / TILE))
        r0, r1 = int(math.floor(y / TILE)), int(math.floor((y + 31.0) / TILE))
        for r in range(max(r0, 0), min(r1, ROWS - 1) + 1):
            for c in range(max(c0, 0), min(c1, self.cols - 1) + 1):
                if self.m[r][c] in DEADLY_TILES:
                    return True
        for (rx, ry, rw, rh) in self.hazards:
            if x < rx + rw and x + 32 > rx and y < ry + rh and y + 32 > ry:
                return True
        return False

    def standing(self, x, y):
        return (not self.hits(x, y)) and self.hits(x, y + 1)

    def fly(self, x, y, vx, vy, plan):
        """空中を進める。plan(t) -> その時刻の水平速度。着地した位置か、None（死亡・画面外）を返す"""
        for t in range(400):
            vx = plan(t)
            nx = x + vx
            if not self.hits(nx, y):
                x = nx
            vy = min(vy + 0.5, 15.0)
            ny = y + vy
            if vy > 0:
                # 落下：1pxずつ進めて、重なったらその手前で止める
                step = 0.0
                landed = False
                while step < vy:
                    d = min(1.0, vy - step)
                    if self.hits(x, y + d):
                        landed = True
                        break
                    y += d
                    step += d
                if landed:
                    y = math.floor(y + 1e-6)
                    while self.hits(x, y + 1) is False and y < ROWS * TILE:
                        y += 1
                    return (x, y)
            else:
                if self.hits(x, ny):
                    vy = 0.0
                else:
                    y = ny
            if self.deadly(x, y):
                return None
            if y > ROWS * TILE + 64:
                return None
        return None

    def run(self, verbose=False):
        s = self.s
        sx, sy = s.start
        if sy == 0.0:
            sy = s.floor_y(int(sx // TILE)) - PLAYER_H
        start = (float(sx), float(sy))
        # 開始位置が空中なら、落として地面に置く
        if not self.standing(*start):
            r = self.fly(start[0], start[1], 0, 0, lambda t: 0)
            if r is None:
                return None, start
            start = r
        q = lambda p: (round(p[0] / 4.0) * 4.0, p[1])
        seen = {q(start): None}
        queue = [start]
        best = start
        gx, gy, gw, gh = self.goal
        while queue:
            x, y = queue.pop()
            if x > best[0]:
                best = (x, y)
            if x + 32 > gx and x < gx + gw and y + 32 > gy and y < gy + gh:
                return True, (x, y)
            nxt = []
            # 歩く（端から落ちる場合も含む）
            for d in (-1, 1):
                nx = x + 4 * d
                if self.hits(nx, y):
                    continue
                if self.deadly(nx, y):
                    continue
                if self.standing(nx, y):
                    nxt.append((nx, y))
                else:
                    for plan in (lambda t, d=d: 4 * d, lambda t: 0):
                        r = self.fly(nx, y, 4 * d, 0, plan)
                        if r:
                            nxt.append(r)
            # 跳ぶ
            for d in (-1, 0, 1):
                for hold in (999, 4, 8, 14, 20, 28, 36):
                    for back in (False, True):
                        if back and (d == 0 or hold == 999):
                            continue
                        def plan(t, d=d, hold=hold, back=back):
                            if t < hold:
                                return 4 * d
                            return -4 * d if back else 0
                        r = self.fly(x, y, 0, -12.0, plan)
                        if r:
                            nxt.append(r)
            for p in nxt:
                k = q(p)
                if k in seen:
                    continue
                seen[k] = (x, y)
                queue.append(p)
        return False, best


def reach_report(s, data):
    ok, pos = Reach(s, data, solved=True).run()
    return ok, pos


# ====================================================================== 表示
def display_width(line):
    w = 0.0
    for ch in line:
        w += 0.5 if ord(ch) < 0x80 or 0xFF61 <= ord(ch) <= 0xFF9F else 1.0
    return w


GLYPH = {0: " ", 1: "=", 2: "#", 3: "#", 4: "#", 5: "w", 6: "S", 11: "^", 12: "X", 13: "-", 14: "b", 15: "d", 16: "r", 17: "R"}
GIM_GLYPH = {
    "gimmick_checkpoint": "C", "gim_gate_door": "D", "gim_weight_switch": "_", "gim_spikes": "^", "gim_scalable_box": "B",
    "gim_manual_bridge": "~", "gim_scalable_ground": "G", "gim_rock": "O", "gim_rewind_gate": "R", "gim_ff_gate": "F",
}


def preview(s, data, c0=0, c1=None, width=100):
    c1 = s.cols if c1 is None else c1
    m = data["map"]
    lines = []
    for a in range(c0, c1, width):
        b = min(a + width, c1)
        grid = [[GLYPH.get(m[r][c], "?") for c in range(a, b)] for r in range(ROWS)]
        for g in data["gimmicks"]:
            ch = GIM_GLYPH.get(g["id"], "g")
            d = s.defs.gim(g["id"])
            c = int(g["x"] // TILE)
            r = int((g["y"] + d["hitboxHeight"] * g.get("scaleY", g.get("scale", 1.0)) - 1) // TILE)
            if a <= c < b and 0 <= r < ROWS:
                grid[r][c - a] = ch
        for e in data["enemies"]:
            c = int(e["x"] // TILE)
            r = int((e["y"] + 8) // TILE)
            if a <= c < b and 0 <= r < ROWS:
                grid[r][c - a] = "E"
        for i in data["items"]:
            c = int(i["x"] // TILE)
            r = int(i["y"] // TILE)
            if a <= c < b and 0 <= r < ROWS:
                grid[r][c - a] = "o" if i["id"] == "item_coin" else "I"
        gc = int(data["goal"]["x"] // TILE)
        if a <= gc < b:
            grid[int(data["goal"]["y"] // TILE)][gc - a] = "Z"
        lines.append("   col %d-%d" % (a, b - 1))
        ruler = "".join(str((a + i) // 10 % 10) if (a + i) % 10 == 0 else " " for i in range(b - a))
        lines.append("    " + ruler)
        for r in range(ROWS):
            lines.append("%2d  " % r + "".join(grid[r]))
    return "\n".join(lines)


def validate(s, data):
    errs = []
    m = data["map"]
    for r in range(ROWS):
        for c, t in enumerate(m[r]):
            if t in DEADLY_TILES:
                errs.append("致死タイルがある (%d,%d)" % (c, r))
    # 開始・ゴール・チェックポイントの足元
    def floor_ok(x, y):
        c = int(x // TILE)
        if not (0 <= c < s.cols):
            return False
        r = int((y + 32) // TILE)
        return 0 <= r < ROWS and m[r][c] in SOLID_TILES
    sx, sy = data["player_start"]["x"], data["player_start"]["y"]
    if not floor_ok(sx, sy):
        errs.append("開始位置の足元に地面が無い")
    if not floor_ok(data["goal"]["x"], data["goal"]["y"]):
        errs.append("ゴールの足元に地面が無い")
    for g in data["gimmicks"]:
        if g["id"] == "gimmick_checkpoint":
            if not floor_ok(g["x"], g["y"] + 32):
                errs.append("チェックポイントの足元に地面が無い x=%g" % g["x"])
    # 敵・アイテムが壁に埋まっていないか
    def inside_solid(x, y, w, h):
        for r in range(int(y // TILE), int((y + h - 1) // TILE) + 1):
            for c in range(int(x // TILE), int((x + w - 1) // TILE) + 1):
                if 0 <= r < ROWS and 0 <= c < s.cols and m[r][c] in SOLID_TILES:
                    return True
        return False
    for e in data["enemies"]:
        if inside_solid(e["x"] + 4, e["y"] + 4, 24, 24):
            errs.append("敵が地形に埋まっている %s (%g,%g)" % (e["id"], e["x"], e["y"]))
    for i in data["items"]:
        if inside_solid(i["x"] + 4, i["y"] + 4, 24, 24):
            errs.append("アイテムが地形に埋まっている %s (%g,%g)" % (i["id"], i["x"], i["y"]))
    # ギミックが壁・天井タイルに埋まっていないか（足場の上面が地面と重なるものは許す）
    free_ids = ("gim_brightness_zone", "gim_color_zone", "gim_zoom_lens", "gim_slowmo_field", "gim_time_field",
                "gim_firebar", "gim_pendulum", "gim_spike_ring", "gim_manual_bridge")
    for g in data["gimmicks"]:
        if g["id"] in free_ids or g["id"] in s.embed_ok:
            continue
        d = s.defs.gim(g["id"])
        w = d["hitboxWidth"] * g.get("scale", 1.0)
        h = d["hitboxHeight"] * g.get("scaleY", g.get("scale", 1.0))
        x1, y1 = g["x"], g["y"]
        bad = None
        for r in range(max(0, int(y1 // TILE)), min(ROWS, int((y1 + h - 0.01) // TILE) + 1)):
            for c in range(max(0, int(x1 // TILE)), min(s.cols, int((x1 + w - 0.01) // TILE) + 1)):
                if m[r][c] == 0:
                    continue
                # 重なる量が小さい（上面が地面と接する）ものは許す
                ox = min(x1 + w, (c + 1) * TILE) - max(x1, c * TILE)
                oy = min(y1 + h, (r + 1) * TILE) - max(y1, r * TILE)
                if ox > 0.5 and oy > 0.5:
                    # 足場は、谷の縁で地面に少し重なってよい（上面が地面の上面と同じ高さなら、載っているのと同じ）
                    if g["id"].startswith(("gim_color_lock", "gim_brightness_lock", "gim_grade_lock", "gim_scalable_ground"))                             and abs(y1 - r * TILE) < 0.5:
                        continue
                    bad = (c, r)
        if bad:
            errs.append("ギミックが地形に埋まっている %s (%g,%g) tile%s" % (g["id"], x1, y1, bad))
    # 扉は、開ける手段が対応しているか。倒して通る扉は "tip" で始まる
    switch_params = {g.get("param") for g in data["gimmicks"]
                     if g["id"] == "gim_weight_switch" or g["id"].startswith("gim_grade_sw_")}
    opened = {a["param2"] for t in data["triggers"] for a in t["actions"] if a["action"] == "OpenDoor"}
    for g in data["gimmicks"]:
        if g["id"] == "gim_gate_door":
            p = g.get("param", "")
            if p not in switch_params and p not in opened and not p.startswith("tip"):
                errs.append("開ける手段の無い扉 param=%s" % p)
    # 説明の窓は出さない
    for t in data["triggers"]:
        for a in t["actions"]:
            if a["action"] == "ShowMessage":
                errs.append("説明の窓がある（出さない約束）: %s" % t["id"])
    ok, pos = reach_report(s, data)
    if not ok:
        errs.append("解いたあとの状態で、ゴールへ行けない（最も右まで行けた位置 x=%.0f y=%.0f）" % pos)
    if errs:
        print("【検査NG】", s.file)
        for e in errs:
            print("  -", e)
        return False
    print("検査OK %s: %d列(%dpx), ギミック %d, 敵 %d, アイテム %d, 最右到達 x=%.0f" % (
        s.file, s.cols, s.cols * TILE, len(data["gimmicks"]), len(data["enemies"]), len(data["items"]), pos[0]))
    return True


Stage.embed_ok = ()
Stage.solved_no_spikes = False
