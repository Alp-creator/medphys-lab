# Radiography room equipment (ceiling-mounted tube + chest wall stand) and small props.
out = {}
# ======================================================================= X-RAY
clear_scene()
FZ = 1.45          # focal spot / detector centre height
FY = -1.80         # focal spot y (SID 180 cm to detector plane at y = 0)
root = empty("XRay_Room", (0, 0, 0))

# --- wall stand + detector holder
bm = bmesh.new(); bm_box(bm, (0.62, 0.50, 0.03), (0, 0.30, 0.015))
p = obj_from_bm("XR_StandBase", bm, "M_Dark", origin=(0, 0.30, 0), parent=root); bevel(p, 0.01, 2)
bm = bmesh.new(); bm_box(bm, (0.26, 0.14, 2.25), (0, 0.32, 1.13))
col = obj_from_bm("XR_StandColumn", bm, "M_Shell", origin=(0, 0.32, 0), parent=root); bevel(col, 0.04, 4)
bm = bmesh.new(); bm_box(bm, (0.08, 0.02, 1.9), (0, 0.245, 1.10))
obj_from_bm("XR_StandRail", bm, "M_ShellGrey", origin=(0, 0.245, 1.10), parent=col)
bm = bmesh.new(); bm_box(bm, (0.012, 0.012, 1.8), (0.125, 0.248, 1.10))
obj_from_bm("XR_StandLED", bm, "M_Accent", origin=(0.125, 0.248, 1.10), parent=col)

bucky = empty("XR_Bucky", (0, 0, FZ), parent=root)
bm = bmesh.new(); bm_box(bm, (0.64, 0.13, 0.64), (0, 0.085, FZ))
bb = obj_from_bm("XR_BuckyBody", bm, "M_Shell", origin=(0, 0.085, FZ), parent=bucky); bevel(bb, 0.035, 4)
bm = bmesh.new(); bm_box(bm, (0.50, 0.006, 0.50), (0, 0.019, FZ))
obj_from_bm("XR_DetectorFrame", bm, "M_Dark", origin=(0, 0.019, FZ), parent=bucky)
bm = bmesh.new(); bm_box(bm, (0.43, 0.004, 0.43), (0, 0.0145, FZ))
face = obj_from_bm("XR_DetectorFace", bm, "M_Screen", origin=(0, 0.0145, FZ), parent=bucky)
for sx in (-1, 1):
    bm = bmesh.new(); bm_box(bm, (0.035, 0.05, 0.22), (0.345 * sx, 0.07, FZ + 0.05))
    h = obj_from_bm("XR_BuckyHandle" + ("L" if sx < 0 else "R"), bm, "M_Dark", origin=(0.345 * sx, 0.07, FZ), parent=bucky)
    bevel(h, 0.012, 2)
empty("XR_DetectorCenter", (0, 0.0125, FZ), parent=bucky)

# --- ceiling rails (static)
for sx in (-1, 1):
    bm = bmesh.new(); bm_box(bm, (0.10, 4.6, 0.08), (0.40 * sx, -1.4, 3.12))
    r = obj_from_bm("XR_Rail" + ("L" if sx < 0 else "R"), bm, "M_Metal", origin=(0.40 * sx, -1.4, 3.12), parent=root)
    bevel(r, 0.01, 2)

# --- tube assembly (origin = focal spot, moves along Y for SID)
tube = empty("XR_TubeAssembly", (0, FY, FZ), parent=root)
bm = bmesh.new(); bm_box(bm, (0.95, 0.34, 0.10), (0, FY - 0.05, 3.03))
car = obj_from_bm("XR_Carriage", bm, "M_ShellGrey", origin=(0, FY, 3.03), parent=tube); bevel(car, 0.02, 2)
bm = bmesh.new(); bm_box(bm, (0.22, 0.22, 0.70), (0, FY - 0.05, 2.63))
c1 = obj_from_bm("XR_ColumnOuter", bm, "M_Shell", origin=(0, FY, 2.6), parent=tube); bevel(c1, 0.03, 3)
bm = bmesh.new(); bm_box(bm, (0.17, 0.17, 0.62), (0, FY - 0.05, 2.02))
c2 = obj_from_bm("XR_ColumnInner", bm, "M_ShellGrey", origin=(0, FY, 2.0), parent=tube); bevel(c2, 0.02, 3)
bm = bmesh.new(); bm_box(bm, (0.13, 0.13, 0.22), (0, FY - 0.05, 1.68))
yk = obj_from_bm("XR_Yoke", bm, "M_Dark", origin=(0, FY, 1.68), parent=tube); bevel(yk, 0.02, 2)
bm = bmesh.new()
bm_cyl(bm, 0.105, 0.42, 48, center=(0, FY - 0.03, FZ), axis="X")
th = obj_from_bm("XR_TubeHousing", bm, "M_ShellGrey", origin=(0, FY, FZ), parent=tube); bevel(th, 0.012, 3)
for sx in (-1, 1):
    bm = bmesh.new(); bm_cyl(bm, 0.115, 0.05, 48, center=(0.235 * sx, FY - 0.03, FZ), axis="X")
    cap = obj_from_bm("XR_TubeCap" + ("L" if sx < 0 else "R"), bm, "M_Dark", origin=(0.235 * sx, FY, FZ), parent=th)
    bevel(cap, 0.01, 2)
bm = bmesh.new(); bm_box(bm, (0.24, 0.20, 0.25), (0, FY + 0.17, FZ))
cl = obj_from_bm("XR_Collimator", bm, "M_Shell", origin=(0, FY + 0.17, FZ), parent=tube); bevel(cl, 0.03, 4)
bm = bmesh.new(); bm_box(bm, (0.12, 0.006, 0.12), (0, FY + 0.272, FZ))
obj_from_bm("XR_CollimatorWindow", bm, "M_Glass", origin=(0, FY + 0.272, FZ), parent=cl)
bm = bmesh.new(); bm_box(bm, (0.16, 0.004, 0.035), (0, FY + 0.272, FZ + 0.095))
obj_from_bm("XR_CollimatorScreen", bm, "M_Screen", origin=(0, FY + 0.272, FZ + 0.095), parent=cl)
bm = bmesh.new(); bm_box(bm, (0.14, 0.006, 0.006), (0, FY + 0.273, FZ - 0.085))
obj_from_bm("XR_CollimatorLED", bm, "M_Accent", origin=(0, FY + 0.273, FZ - 0.085), parent=cl)
for sx in (-1, 1):
    bm = bmesh.new()
    bm_box(bm, (0.03, 0.03, 0.20), (0.15 * sx, FY + 0.30, FZ))
    bm_box(bm, (0.03, 0.06, 0.03), (0.15 * sx, FY + 0.27, FZ + 0.085))
    bm_box(bm, (0.03, 0.06, 0.03), (0.15 * sx, FY + 0.27, FZ - 0.085))
    hd = obj_from_bm("XR_Handle" + ("L" if sx < 0 else "R"), bm, "M_Dark", origin=(0.15 * sx, FY + 0.28, FZ), parent=cl)
    bevel(hd, 0.008, 2)
objs = model_objects(root); finalize(objs)
uv_planar(face, "Y")
for o in objs:
    if o.type == "MESH": auto_uv(o)
out["xray"] = export_fbx(root, "XRay_Room.fbx")

# ======================================================================= PROPS
def export_single(root, fname, planar=()):
    objs = model_objects(root); finalize(objs)
    for o, ax in planar:
        uv_planar(o, ax)
    for o in objs:
        if o.type == "MESH": auto_uv(o)
    return export_fbx(root, fname)

# --- survey meter (Geiger counter); origin at the grip
clear_scene()
root = empty("SurveyMeter", (0, 0, 0))
bm = bmesh.new(); bm_box(bm, (0.10, 0.22, 0.09), (0, 0.0, -0.065))
body = obj_from_bm("SM_Body", bm, "M_Warning", origin=(0, 0, -0.065), parent=root); bevel(body, 0.018, 4)
bm = bmesh.new()
bm_box(bm, (0.035, 0.15, 0.03), (0, 0.0, 0.035))
bm_box(bm, (0.035, 0.03, 0.06), (0, 0.06, 0.005))
bm_box(bm, (0.035, 0.03, 0.06), (0, -0.06, 0.005))
hnd = obj_from_bm("SM_Handle", bm, "M_Dark", origin=(0, 0, 0.02), parent=root); bevel(hnd, 0.01, 3)
bm = bmesh.new(); bm_box(bm, (0.075, 0.10, 0.004), (0, -0.035, -0.0185))
disp = obj_from_bm("SM_Display", bm, "M_Screen", origin=(0, -0.035, -0.0185), parent=root)
bm = bmesh.new(); bm_cyl(bm, 0.022, 0.14, 32, center=(0, 0.17, -0.07), axis="Y")
pr = obj_from_bm("SM_Probe", bm, "M_Metal", origin=(0, 0.17, -0.07), parent=root); bevel(pr, 0.004, 2)
bm = bmesh.new(); bm_cyl(bm, 0.024, 0.008, 32, center=(0, 0.243, -0.07), axis="Y")
obj_from_bm("SM_ProbeWindow", bm, "M_Dark", origin=(0, 0.243, -0.07), parent=pr)
for i, sx in enumerate((-0.025, 0.0, 0.025)):
    bm = bmesh.new(); bm_cyl(bm, 0.008, 0.008, 16, center=(sx, 0.055, -0.0185))
    obj_from_bm("SM_Button%d" % i, bm, "M_Dark", origin=(sx, 0.055, -0.0185), parent=root)
out["survey"] = export_single(root, "SurveyMeter.fbx", [(disp, "Z")])

# --- mobile lead shield; origin at floor centre
clear_scene()
root = empty("LeadShield", (0, 0, 0))
bm = bmesh.new(); bm_box(bm, (0.86, 0.06, 1.30), (0, 0, 0.82))
pnl = obj_from_bm("LS_Panel", bm, "M_ShellGrey", origin=(0, 0, 0.82), parent=root); bevel(pnl, 0.02, 3)
bm = bmesh.new(); bm_box(bm, (0.86, 0.07, 0.06), (0, 0, 1.50))
obj_from_bm("LS_Rail", bm, "M_Metal", origin=(0, 0, 1.50), parent=root)
bm = bmesh.new(); bm_box(bm, (0.70, 0.02, 0.42), (0, 0, 1.75))
obj_from_bm("LS_Window", bm, "M_Glass", origin=(0, 0, 1.75), parent=root)
bm = bmesh.new()
bm_box(bm, (0.04, 0.06, 0.48), (-0.37, 0, 1.75)); bm_box(bm, (0.04, 0.06, 0.48), (0.37, 0, 1.75))
bm_box(bm, (0.78, 0.06, 0.04), (0, 0, 1.97))
fr = obj_from_bm("LS_Frame", bm, "M_Metal", origin=(0, 0, 1.75), parent=root); bevel(fr, 0.006, 2)
bm = bmesh.new(); bm_box(bm, (0.30, 0.004, 0.26), (0, -0.032, 1.05))
obj_from_bm("LS_Sign", bm, "M_Warning", origin=(0, -0.032, 1.05), parent=pnl)
bm = bmesh.new()
bm_box(bm, (0.90, 0.10, 0.05), (0, 0, 0.12)); bm_box(bm, (0.08, 0.60, 0.05), (-0.40, 0, 0.10)); bm_box(bm, (0.08, 0.60, 0.05), (0.40, 0, 0.10))
bs = obj_from_bm("LS_Base", bm, "M_Dark", origin=(0, 0, 0.1), parent=root); bevel(bs, 0.01, 2)
for i, (x, y) in enumerate([(-0.40, -0.27), (-0.40, 0.27), (0.40, -0.27), (0.40, 0.27)]):
    bm = bmesh.new(); bm_cyl(bm, 0.035, 0.03, 24, center=(x, y, 0.035), axis="X")
    obj_from_bm("LS_Caster%d" % i, bm, "M_Dark", origin=(x, y, 0.035), parent=root)
out["shield"] = export_single(root, "LeadShield.fbx")

# --- lead pig (source container); origin at bottom centre
clear_scene()
root = empty("LeadPig", (0, 0, 0))
bm = bmesh.new(); bm_cyl(bm, 0.07, 0.13, 40, center=(0, 0, 0.065))
b = obj_from_bm("LP_Body", bm, "M_Lead", origin=(0, 0, 0), parent=root); bevel(b, 0.008, 3)
bm = bmesh.new(); bm_cyl(bm, 0.0712, 0.025, 40, center=(0, 0, 0.09))
obj_from_bm("LP_Band", bm, "M_Warning", origin=(0, 0, 0.09), parent=b)
lid = empty("LP_LidPivot", (0, 0, 0.13), parent=root)
bm = bmesh.new(); bm_cyl(bm, 0.074, 0.035, 40, center=(0, 0, 0.1475))
l = obj_from_bm("LP_Lid", bm, "M_Lead", origin=(0, 0, 0.13), parent=lid); bevel(l, 0.008, 3)
bm = bmesh.new(); bm_cyl(bm, 0.018, 0.03, 24, center=(0, 0, 0.18))
k = obj_from_bm("LP_Knob", bm, "M_Metal", origin=(0, 0, 0.18), parent=l); bevel(k, 0.005, 2)
out["pig"] = export_single(root, "LeadPig.fbx")

# --- water phantom tank with scanning ion chamber; origin at floor centre
clear_scene()
root = empty("WaterTank", (0, 0, 0))
bm = bmesh.new(); bm_box(bm, (0.80, 0.80, 0.70), (0, 0, 0.35))
cart = obj_from_bm("WT_Cart", bm, "M_Dark", origin=(0, 0, 0), parent=root); bevel(cart, 0.03, 3)
TB = 0.70; TS = 0.64; TH = 0.56
bm = bmesh.new()
bm_ring_extrude(bm, [(-TS / 2, -TS / 2), (TS / 2, -TS / 2), (TS / 2, TS / 2), (-TS / 2, TS / 2)],
                [(-TS / 2 + 0.012, -TS / 2 + 0.012), (TS / 2 - 0.012, -TS / 2 + 0.012), (TS / 2 - 0.012, TS / 2 - 0.012), (-TS / 2 + 0.012, TS / 2 - 0.012)],
                TB, TB + TH, 0)
# ring extrude builds along Y; rotate so it stands upright (walls along Z)
bmesh.ops.rotate(bm, verts=bm.verts, cent=(0, 0, 0), matrix=Matrix.Rotation(math.pi / 2, 3, "X"))
bm_box(bm, (TS, TS, 0.012), (0, 0, TB + 0.006))
w = obj_from_bm("WT_Walls", bm, "M_Glass", origin=(0, 0, TB), parent=root)
bm = bmesh.new()
for sx in (-1, 1):
    for sy in (-1, 1):
        bm_box(bm, (0.025, 0.025, TH), (sx * TS / 2, sy * TS / 2, TB + TH / 2))
    bm_box(bm, (0.025, TS, 0.025), (sx * TS / 2, 0, TB + TH))
    bm_box(bm, (TS, 0.025, 0.025), (0, sx * TS / 2, TB + TH))
fr = obj_from_bm("WT_Frame", bm, "M_Metal", origin=(0, 0, TB), parent=root); bevel(fr, 0.004, 2)
# scanning gantry: a bridge across the top that moves in x, carriage moves in y, rod moves in z
bridge = empty("WT_Bridge", (0, 0, TB + TH + 0.04), parent=root)
bm = bmesh.new(); bm_box(bm, (0.06, TS + 0.06, 0.05), (0, 0, TB + TH + 0.045))
br = obj_from_bm("WT_BridgeBar", bm, "M_ShellGrey", origin=(0, 0, TB + TH + 0.04), parent=bridge); bevel(br, 0.008, 2)
carriage = empty("WT_Carriage", (0, 0, TB + TH + 0.04), parent=bridge)
bm = bmesh.new(); bm_box(bm, (0.09, 0.09, 0.07), (0, 0, TB + TH + 0.06))
cg = obj_from_bm("WT_CarriageBox", bm, "M_Shell", origin=(0, 0, TB + TH + 0.06), parent=carriage); bevel(cg, 0.01, 2)
probe = empty("WT_Probe", (0, 0, TB + 0.30), parent=carriage)
bm = bmesh.new(); bm_cyl(bm, 0.006, 0.40, 16, center=(0, 0, TB + 0.30 + 0.21))
obj_from_bm("WT_Rod", bm, "M_Metal", origin=(0, 0, TB + 0.30), parent=probe)
bm = bmesh.new(); bm_cyl(bm, 0.0035, 0.024, 16, center=(0, 0, TB + 0.30), axis="X", r2=None)
bm_box(bm, (0.012, 0.012, 0.012), (0.015, 0, TB + 0.30))
ch = obj_from_bm("WT_Chamber", bm, "M_Accent", origin=(0, 0, TB + 0.30), parent=probe)
out["tank"] = export_single(root, "WaterTank.fbx")
result = out
