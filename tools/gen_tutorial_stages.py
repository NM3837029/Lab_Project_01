# -*- coding: utf-8 -*-
"""チュートリアルステージ（tutorial_1_kihon.json / tutorial_2_hensyu.json）を生成する。

使い方（リポジトリのルートで）:
    python tools/gen_tutorial_stages.py

手書きではなく、このスクリプトから作る。部屋の並びや文言を直したいときは、ここを直して作り直す。
Lab_Editor で保存し直すと、エディタが知らないキーは落ちるので、生成物は既存のキーだけで組んである。

作りの約束（死なないステージ）
  - 敵・トゲ(11/12)・チョンパーを置かない。最下段2行は、最初から最後まで途切れない地面にする（穴が無い）。
  - 各部屋の入口にチェックポイントを置く。
  - ツールは全て有効。解禁アイテムは使わない（解禁は他のステージへ残り、封印を破ってしまうため）。

「操作したら扉が開く」の仕組み
  - ゲームは、プレイヤーが操作した瞬間にイベントのスイッチ op_<名前> を ON にする（DrawPixel.cpp の NoteEditOp）。
  - 部屋の入口（intro）のトリガーが、その部屋の op_ を OFF にしてから説明を出す。
  - 部屋の中の矩形に入っているあいだに op_ が ON になると、ok のトリガーが「できた！」を出して、param が一致する扉を開ける。
    （op_ で始まる SwitchOn は、矩形の中にプレイヤーがいるときだけ成立する。ほかの部屋でやった操作では開かない）
  - 物理ギミックで塞ぐ部屋（ジャンプの段差・重量スイッチ）は、扉を使わないか、重量スイッチと扉の param を合わせる。
"""
import json
import os
import sys

TILE = 32
ROWS = 15
GROUND_ROW = 13            # 地面の上面は y=416
GROUND_Y = GROUND_ROW * TILE
PLAYER_TOP = GROUND_Y - 32  # 立っているときのプレイヤーの上端 y=384
ROOM_W = 20                # 1部屋のタイル数（640px＝画面1枚ぶん）
DOOR_COL = ROOM_W - 2      # 扉は部屋の終わりの2タイル手前
TILE_GRASS, TILE_DIRT, TILE_WOOD = 1, 2, 5
DEADLY_TILES = (11, 12)
SPEAKER = "ディレクター"

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT_DIR = os.path.join(ROOT, "assets", "stages")


class Stage:
    def __init__(self, title):
        self.title = title
        self.rooms = []          # (部屋の種類, 辞書)
        self.cols = 0
        self.gimmicks = []
        self.items = []
        self.triggers = []
        self.walls = []          # (col, row) 追加の固体タイル
        self.goal = None
        self.messages = []       # 検査用
        self.doors = {}          # param -> 部屋番号

    def room_x(self, i):
        return i * ROOM_W * TILE


def msg(stage, text):
    stage.messages.append(text)
    return {"action": "ShowMessage", "param1": text, "param2": SPEAKER, "delay": 0.0}


def add_room(stage, spec):
    """spec: kind=walk/jump/op/scale/goal, text=説明, op=スイッチ名, ok=成功時の文言, box=飾りブロックを置くか"""
    i = len(stage.rooms)
    stage.rooms.append(spec)
    x0 = stage.room_x(i)
    kind = spec["kind"]
    # チェックポイント（部屋の入口）
    if i > 0:
        stage.gimmicks.append({"id": "gimmick_checkpoint", "x": float(x0 + 8), "y": float(GROUND_Y - 64)})
    # コイン（ごほうび。2枚）
    for k, cx in enumerate((x0 + 6 * TILE, x0 + 12 * TILE)):
        stage.items.append({"id": "item_coin", "x": float(cx), "y": float(GROUND_Y - 64)})
    intro_actions = []
    if kind == "op":
        # この部屋の操作スイッチを入口で OFF にしてから、説明を出す（手前の部屋でやった操作を持ち越さない）
        intro_actions.append({"action": "SetSwitch", "param1": "op_" + spec["op"], "param2": "off", "delay": 0.0})
    intro_actions.append(msg(stage, spec["text"]))
    stage.triggers.append({
        "id": "r%02d_intro" % i, "x": float(x0 + 40), "y": 0.0, "width": 48.0, "height": 480.0,
        "condition": "PlayerEnter", "conditionParam": "", "oneShot": True, "actions": intro_actions,
    })
    if kind == "op":
        param = "d_" + spec["op"]
        stage.doors[param] = i
        door_x = x0 + DOOR_COL * TILE
        stage.gimmicks.append({"id": "gim_gate_door", "x": float(door_x), "y": float(GROUND_Y - 160), "param": param})
        stage.triggers.append({
            "id": "r%02d_ok" % i, "x": float(x0 + 104), "y": 0.0, "width": float(door_x - (x0 + 104)), "height": 480.0,
            "condition": "SwitchOn", "conditionParam": "op_" + spec["op"], "oneShot": True,
            "actions": [
                {"action": "OpenDoor", "param1": "gim_gate_door", "param2": param, "delay": 0.0},
                msg(stage, spec["ok"]),
            ],
        })
    if kind == "jump":
        # 2タイルと3タイルの段差。ジャンプの高さは 144px（4.5タイル）なので、どちらも越えられる
        for c0, h in ((x0 // TILE + 8, 2), (x0 // TILE + 14, 3)):
            for c in (c0, c0 + 1):
                for r in range(GROUND_ROW - h, GROUND_ROW):
                    stage.walls.append((c, r))
    if kind == "scale":
        # 箱を広げて重量スイッチへ乗せる。箱(50x50)の右端が、広げる前は届かず、3ノッチ(+96px)広げると届く位置
        stage.gimmicks.append({"id": "gim_scalable_box", "x": float(x0 + 8 * TILE), "y": float(GROUND_Y - 50)})
        stage.gimmicks.append({"id": "gim_weight_switch", "x": float(x0 + 10 * TILE), "y": float(GROUND_Y - 10), "param": "gateT"})
        door_x = x0 + DOOR_COL * TILE
        stage.doors["gateT"] = i
        stage.gimmicks.append({"id": "gim_gate_door", "x": float(door_x), "y": float(GROUND_Y - 160), "param": "gateT"})
    if kind == "op" and spec.get("box"):
        stage.gimmicks.append({"id": "gim_scalable_box", "x": float(x0 + 8 * TILE), "y": float(GROUND_Y - 50)})
    if kind == "goal":
        stage.goal = (x0 + 12 * TILE, PLAYER_TOP)
    stage.cols = (i + 1) * ROOM_W


def build(stage):
    cols = stage.cols
    m = [[0] * cols for _ in range(ROWS)]
    for c in range(cols):
        m[GROUND_ROW][c] = TILE_GRASS
        m[GROUND_ROW + 1][c] = TILE_DIRT
    for (c, r) in stage.walls:
        m[r][c] = TILE_WOOD
    zeros = [[0] * cols for _ in range(ROWS)]
    items = stage.items
    return {
        "map_w": cols, "map_h": ROWS,
        "player_start": {"x": 64.0, "y": float(PLAYER_TOP)},
        "player_capabilities": {"canDoubleJump": False, "canDash": True, "canShootFireball": True, "canFly": False,
                                "baseJumpPower": -12, "baseSpeed": 4.0},
        "allowed_edit_tools": {"rewindEnabled": True, "pauseEnabled": True, "fastForwardEnabled": True,
                               "screenEffectEnabled": True, "objectEditEnabled": True, "cutEnabled": True},
        # チュートリアルなので、コストはたっぷり・消費は小さく
        "edit_cost_settings": {"maxCost": 200.0, "regenPerSec": 15.0, "drainRewindPerSec": 2.0, "drainPausePerSec": 1.0,
                               "drainFastForwardPerSec": 2.0, "drainScreenEffectPerSec": 2.0, "flatColorCycle": 2.0,
                               "flatMenuToggle": 2.0, "flatSpeedChange": 2.0, "flatDirectionFlip": 2.0,
                               "flatResetAll": 2.0, "flatCutCreate": 5.0, "cutCostPerTile": 0.1},
        "goal": {"x": float(stage.goal[0]), "y": float(stage.goal[1])},
        "bgm_id": "bgm_editing_05",
        "backgrounds": [{"sprite": "", "color": "#B4D2F0", "drawOrder": 0, "scrollRate": 0.0, "loop": True,
                         "offsetX": 0.0, "offsetY": 0.0}],
        "map": m, "deco_back": zeros, "deco_front": [row[:] for row in zeros],
        "triggers": stage.triggers, "enemies": [], "gimmicks": stage.gimmicks, "items": items, "platforms": [],
    }


def display_width(line):
    """全角を1、半角を0.5として数える（メッセージ窓は全角28文字ぶん）"""
    w = 0.0
    for ch in line:
        w += 0.5 if ord(ch) < 0x80 or 0xFF61 <= ord(ch) <= 0xFF9F else 1.0
    return w


def validate(stage, data, name):
    errs = []
    m = data["map"]
    cols = data["map_w"]
    for r in range(ROWS):
        if len(m[r]) != cols:
            errs.append("行%d の長さが違う" % r)
        for c, t in enumerate(m[r]):
            if t in DEADLY_TILES:
                errs.append("致死タイル (%d,%d)" % (c, r))
    for c in range(cols):
        if m[GROUND_ROW][c] == 0 or m[GROUND_ROW + 1][c] == 0:
            errs.append("地面に穴 col=%d" % c)
    for t in stage.messages:
        lines = t.split("\n")
        if len(lines) > 3:
            errs.append("メッセージが4行以上: " + t[:20])
        for ln in lines:
            if display_width(ln) > 28:
                errs.append("メッセージの行が長い(%.1f): %s" % (display_width(ln), ln))
    # ギミックが地形に埋まっていないか（座標は左上、幅・高さはギミック定義から）
    sizes = {"gim_gate_door": (32, 160), "gim_scalable_box": (50, 50), "gim_weight_switch": (120, 10),
             "gimmick_checkpoint": (32, 64)}
    for g in data["gimmicks"]:
        w, h = sizes[g["id"]]
        x1, y1 = g["x"], g["y"]
        for r in range(ROWS):
            for c in range(cols):
                if m[r][c] and x1 < (c + 1) * TILE and x1 + w > c * TILE and y1 < (r + 1) * TILE and y1 + h > r * TILE:
                    # 地面（行13以降）の上面に接しているのは許す（y1+h == 地面の上面）
                    if r >= GROUND_ROW and y1 + h <= GROUND_Y + 0.01:
                        continue
                    errs.append("ギミックが地形に埋まっている %s (%g,%g) tile(%d,%d)" % (g["id"], x1, y1, c, r))
    # 扉の param と、それを開けるトリガー／重量スイッチが対応しているか
    door_params = {g["param"] for g in data["gimmicks"] if g["id"] == "gim_gate_door"}
    opened = {a["param2"] for t in data["triggers"] for a in t["actions"] if a["action"] == "OpenDoor"}
    switches = {g["param"] for g in data["gimmicks"] if g["id"] == "gim_weight_switch"}
    for p in door_params:
        if p not in opened and p not in switches:
            errs.append("開ける手段の無い扉 param=%s" % p)
    # 段差はジャンプ(144px)で越えられる高さか（2〜3タイル）
    # 壁はジャンプの部屋にしか置かない（上の walls は 2・3 タイルのみ）
    for (c, r) in stage.walls:
        if GROUND_ROW - r > 3:
            errs.append("段差が高すぎる col=%d row=%d" % (c, r))
    if errs:
        print("【検査NG】", name)
        for e in errs:
            print("  -", e)
        sys.exit(1)
    print("検査OK %s: %d列 x %d行, 部屋 %d, 扉 %d, トリガー %d, ギミック %d, アイテム %d" % (
        name, cols, ROWS, len(stage.rooms), len(door_params), len(data["triggers"]), len(data["gimmicks"]), len(data["items"])))


def stage1():
    s = Stage("きほんと時間")
    add_room(s, dict(kind="walk", text="A / D（← / →）で左右に動けます。\nまずは右へ進んでみましょう！"))
    add_room(s, dict(kind="jump", text="W（↑）でジャンプ！\n目の前の段差を飛び越えましょう。"))
    add_room(s, dict(kind="op", op="dash", text="Shift を押しながら走るとダッシュ！\nダッシュすると、この先の扉が開きます。",
                     ok="ダッシュできました！\n扉が開きました。先へ進みましょう。"))
    add_room(s, dict(kind="op", op="shoot", text="Enter か、画面のクリックで弾を撃てます。\n撃つと、この先の扉が開きます。",
                     ok="弾を撃てました！\n扉が開きました。"))
    add_room(s, dict(kind="op", op="step", text="Space で時間が止まります。止まっている間に\n← → を押すと、1コマずつ進みます。\nコマ送りすると、この先の扉が開きます。",
                     ok="コマ送りできました！\n扉が開きました。もう一度 Space で再開。"))
    add_room(s, dict(kind="op", op="rewind", text="R を押し続けると、時間が巻き戻ります。\n巻き戻すと、この先の扉が開きます。",
                     ok="巻き戻せました！\n扉が開きました。"))
    add_room(s, dict(kind="op", op="ff", text="F で早送り（もう一度 F で元の速さ）。\n早送りすると、この先の扉が開きます。",
                     ok="早送りできました！\n扉が開きました。F でもとの速さへ。"))
    add_room(s, dict(kind="goal", text="基本操作はここまで！\nゴールして、次は「編集」を学びましょう。"))
    return s


def stage2():
    s = Stage("画面と物の編集")
    add_room(s, dict(kind="op", op="color", text="T で、画面に色フィルタをかけられます。\n押すたびに 赤 → 緑 → 青 → なし。\n色を付けると、この先の扉が開きます。",
                     ok="色フィルタをかけられました！\n扉が開きました。"))
    add_room(s, dict(kind="op", op="dark", text="X を押している間、画面が暗くなります。\n暗くすると、この先の扉が開きます。",
                     ok="暗くできました！\n扉が開きました。"))
    add_room(s, dict(kind="op", op="bright", text="C を押している間、画面が明るくなります。\n明るくすると、この先の扉が開きます。",
                     ok="明るくできました！\n扉が開きました。"))
    add_room(s, dict(kind="op", op="grade", text="右下のパネルで、彩度・色相・コントラストを\n動かせます。彩度を下げてモノクロにするなど、\n色合いを変えると、扉が開きます。",
                     ok="色を調整できました！\n扉が開きました。"))
    add_room(s, dict(kind="op", op="zoomin", text="Z を押している間、カーソルの位置を拡大します。\nZ を押したままホイールで倍率を変えられます。\n拡大すると、この先の扉が開きます。",
                     ok="拡大できました！\n扉が開きました。"))
    add_room(s, dict(kind="op", op="zoomout", text="V を押している間、広い範囲を見渡せます。\nV を押したままホイールで倍率を変えられます。\n縮小すると、この先の扉が開きます。",
                     ok="縮小できました！\n扉が開きました。"))
    add_room(s, dict(kind="op", op="menu", box=True, text="物を右クリックすると選べて、円形メニューが\n出ます（巻き戻し・停止・反転など）。\nブロックを右クリックすると、扉が開きます。",
                     ok="選べました！\n扉が開きました。"))
    add_room(s, dict(kind="op", op="move", box=True, text="選んだ物は、ドラッグで動かせます。\nブロックをドラッグして動かすと、\nこの先の扉が開きます。",
                     ok="動かせました！\n扉が開きました。"))
    add_room(s, dict(kind="scale", text="ブロックを右クリックで選び、ホイールで広げます。\n横に広げて重量スイッチに乗せると、\nこの先の扉が開きます。"))
    add_room(s, dict(kind="op", op="rotate", box=True, text="Ctrl を押しながらホイールで、選んだ物を回せます。\nブロックを選んで回すと、\nこの先の扉が開きます。",
                     ok="回せました！\n扉が開きました。"))
    add_room(s, dict(kind="op", op="flip", box=True, text="物をダブルクリックすると、向きが反転します。\nブロックをダブルクリックすると、\nこの先の扉が開きます。",
                     ok="反転できました！\n扉が開きました。"))
    add_room(s, dict(kind="op", op="undo", box=True, text="Ctrl + Z で、直前の編集を取り消せます。\n（Ctrl + Y でやり直し）\n編集してから取り消すと、扉が開きます。",
                     ok="取り消せました！\n扉が開きました。"))
    add_room(s, dict(kind="op", op="areaselect", box=True, text="何もない所をドラッグして四角で囲むと、\n中の物をまとめて選べます。\n範囲を選ぶと、この先の扉が開きます。",
                     ok="まとめて選べました！\n扉が開きました。"))
    add_room(s, dict(kind="op", op="cut", text="画面下のタイムラインを、Ctrl を押しながら\n2回クリックすると、その区間を飛ばせます。\nカットを作ると、この先の扉が開きます。",
                     ok="カットを作れました！\n扉が開きました。"))
    add_room(s, dict(kind="op", op="ground", text="G を押すと、カーソルの位置に地面を置けます。\n地面を置くと、この先の扉が開きます。",
                     ok="地面を置けました！\n扉が開きました。"))
    add_room(s, dict(kind="goal", text="ここまで、全部できました！\nゴールして、本番のステージへ進みましょう。"))
    return s


def main():
    results = []
    for name, st in (("tutorial_1_kihon.json", stage1()), ("tutorial_2_hensyu.json", stage2())):
        data = build(st)
        validate(st, data, name)
        path = os.path.join(OUT_DIR, name)
        with open(path, "w", encoding="utf-8", newline="\n") as f:
            json.dump(data, f, ensure_ascii=False, indent=2)
        results.append((name, st.title, len(data["items"])))
    for name, title, n in results:
        print("書き出し:", name, "「%s」" % title, "item_total=%d" % n)


if __name__ == "__main__":
    main()
