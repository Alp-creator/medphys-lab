# Clinical room shell: floor, panelled walls with accent LED band, ceiling with light panels,
# control-room window (+X wall) and a shielded door (-X wall). Inner size 11 x 11 m, height 3.3 m.
clear_scene()
MATS.update({"M_Ceiling": ((0.86, 0.87, 0.89, 1), 0.0, 0.70, 0)})
W = 5.5      # half size
H = 3.30
root = empty("Room", (0, 0, 0))

bm = bmesh.new(); bm_box(bm, (2 * W + 0.4, 2 * W + 0.4, 0.10), (0, 0, -0.05))
obj_from_bm("Room_Floor", bm, "M_Floor", origin=(0, 0, 0), parent=root)
# floor inlay ring (accent) under the machine
bm = bmesh.new()
bm_lathe(bm, [(1.95, 0.001), (2.0, 0.001), (2.0, 0.004), (1.95, 0.004), (1.95, 0.001)], segs=96)
obj_from_bm("Room_FloorRing", bm, "M_Accent", origin=(0, 0, 0), parent=root)
bm = bmesh.new(); bm_box(bm, (2 * W + 0.4, 2 * W + 0.4, 0.10), (0, 0, H + 0.05))
obj_from_bm("Room_Ceiling", bm, "M_Ceiling", origin=(0, 0, H), parent=root)

def wall(name, axis, sign, window=False, door=False):
    # local frame: u along the wall, wall plane at n = sign*W
    parts_wall, parts_dark, parts_led, parts_glass, parts_grey = bmesh.new(), bmesh.new(), bmesh.new(), bmesh.new(), bmesh.new()
    def P(u, n, z):
        return (u, n, z) if axis == "Y" else (n, u, z)
    def S(su, sn, sz):
        return (su, sn, sz) if axis == "Y" else (sn, su, sz)
    n0 = sign * (W + 0.1)
    # structural wall slab
    if window:
        # slab with an opening for the control-room window (u in [-1.5, 1.5], z in [1.1, 2.2])
        L = 2 * W + 0.4
        side = (L / 2 - 1.5)
        bm_box(parts_wall, S(side, 0.2, H), P(-1.5 - side / 2, n0, H / 2))
        bm_box(parts_wall, S(side, 0.2, H), P(1.5 + side / 2, n0, H / 2))
        bm_box(parts_wall, S(3.0, 0.2, 1.1), P(0, n0, 0.55))
        bm_box(parts_wall, S(3.0, 0.2, H - 2.2), P(0, n0, 2.2 + (H - 2.2) / 2))
    else:
        bm_box(parts_wall, S(2 * W + 0.4, 0.2, H), P(0, n0, H / 2))
    # skirting and LED band
    bm_box(parts_dark, S(2 * W, 0.03, 0.12), P(0, sign * (W - 0.005), 0.06))
    bm_box(parts_led, S(2 * W, 0.02, 0.025), P(0, sign * (W - 0.01), 2.55))
    # panels
    pw, gap = 1.30, 0.04
    count = int((2 * W) / (pw + gap))
    start = -(count * (pw + gap)) / 2 + (pw + gap) / 2
    for i in range(count):
        u = start + i * (pw + gap)
        if window and abs(u) < 1.6:
            continue
        if door and -2.2 < u < -0.4:
            continue
        bm_box(parts_wall, S(pw, 0.03, 2.30), P(u, sign * (W - 0.015), 0.12 + 0.02 + 1.15))
    if window:
        bm_box(parts_glass, S(3.0, 0.03, 1.10), P(0, sign * (W + 0.02), 1.65))
        bm_box(parts_grey, S(3.2, 0.08, 0.10), P(0, sign * (W - 0.02), 1.05))
        bm_box(parts_grey, S(3.2, 0.08, 0.10), P(0, sign * (W - 0.02), 2.25))
        bm_box(parts_grey, S(0.10, 0.08, 1.30), P(-1.55, sign * (W - 0.02), 1.65))
        bm_box(parts_grey, S(0.10, 0.08, 1.30), P(1.55, sign * (W - 0.02), 1.65))
        # dim control room behind the glass: back wall + console with screens
        bm_box(parts_dark, S(3.4, 0.05, 2.6), P(0, sign * (W + 1.8), 1.3))
        bm_box(parts_dark, S(0.05, 1.8, 2.6), P(-1.7, sign * (W + 0.9), 1.3))
        bm_box(parts_dark, S(0.05, 1.8, 2.6), P(1.7, sign * (W + 0.9), 1.3))
        bm_box(parts_dark, S(3.4, 1.8, 0.05), P(0, sign * (W + 0.9), 2.6))
        bm_box(parts_dark, S(2.6, 0.6, 0.8), P(0, sign * (W + 0.75), 0.4))
        for k in (-0.8, 0.0, 0.8):
            bm_box(parts_led, S(0.55, 0.03, 0.32), P(k, sign * (W + 0.95), 1.15))
    if door:
        bm_box(parts_grey, S(1.9, 0.10, 2.35), P(-1.3, sign * (W - 0.03), 1.175))
        bm_box(parts_dark, S(0.06, 0.06, 0.30), P(-0.55, sign * (W - 0.10), 1.10))
    objs = []
    for bm, m, suf in ((parts_wall, "M_Wall", ""), (parts_dark, "M_Dark", "Dark"), (parts_led, "M_Accent", "LED"),
                       (parts_glass, "M_Glass", "Glass"), (parts_grey, "M_ShellGrey", "Trim")):
        if len(bm.verts) == 0:
            bm.free(); continue
        o = obj_from_bm(name + suf, bm, m, origin=P(0, sign * W, 0), parent=root)
        objs.append(o)
    return objs

wall("Room_WallN", "Y", 1)
wall("Room_WallS", "Y", -1)
wall("Room_WallE", "X", 1, window=True)
wall("Room_WallW", "X", -1, door=True)

# warning sign above the door
bm = bmesh.new(); bm_box(bm, (0.02, 0.40, 0.28), (-W + 0.02, -1.3, 2.62))
obj_from_bm("Room_Sign", bm, "M_Warning", origin=(-W + 0.02, -1.3, 2.62), parent=root)

# ceiling light panels (1.2 x 0.6 m, on the 0.6 m tile grid of M_Ceiling)
bm = bmesh.new()
for x in (-2.4, 0.0, 2.4):
    for y in (-2.4, 0.0, 2.4):
        bm_box(bm, (1.2, 0.6, 0.012), (x, y, H - 0.012))
obj_from_bm("Room_Lights", bm, "M_Light", origin=(0, 0, H), parent=root)
bm = bmesh.new()
for x in (-2.4, 0.0, 2.4):
    for y in (-2.4, 0.0, 2.4):
        bm_box(bm, (1.24, 0.64, 0.024), (x, y, H))
obj_from_bm("Room_LightFrames", bm, "M_ShellGrey", origin=(0, 0, H), parent=root)

objs = model_objects(root); finalize(objs)
for o in objs:
    if o.type == "MESH": auto_uv(o)
result = export_fbx(root, "Room.fbx")
