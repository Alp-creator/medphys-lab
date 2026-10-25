# Medical linear accelerator (C-arm type) + treatment couch.
# Isocentre at (0, 0, ISO). Gantry rotates about the Y axis; the drive stand is at +Y.
clear_scene()
ISO = 1.30
root = empty("Linac", (0, 0, 0))

# ---- drive stand
bm = bmesh.new(); bm_box(bm, (1.45, 0.85, 2.55), (0, 1.67, 1.275))
stand = obj_from_bm("Linac_Stand", bm, "M_Shell", origin=(0, 1.67, 0), parent=root)
bevel(stand, 0.09, 5)
bm = bmesh.new(); bm_box(bm, (1.50, 0.90, 0.10), (0, 1.67, 0.05))
pl = obj_from_bm("Linac_StandBase", bm, "M_Dark", origin=(0, 1.67, 0), parent=root)
bevel(pl, 0.02, 2)
for sx in (-1, 1):
    bm = bmesh.new(); bm_box(bm, (0.02, 0.012, 1.9), (0.66 * sx, 1.238, 1.25))
    obj_from_bm("Linac_StandLED" + ("L" if sx < 0 else "R"), bm, "M_Accent", origin=(0.66 * sx, 1.238, 1.25), parent=stand)

# ---- gantry (rotating group, pivot on the rotation axis at isocentre)
gantry = empty("Linac_Gantry", (0, 0, ISO), parent=root)
bm = bmesh.new(); bm_ring_extrude(bm, circle(1.02, 96), circle(0.30, 96), 1.08, 1.245, ISO)
drum = obj_from_bm("Linac_Drum", bm, "M_ShellGrey", origin=(0, 1.16, ISO), parent=gantry)
bevel(drum, 0.02, 3)
bm = bmesh.new(); bm_ring_extrude(bm, circle(0.97, 96), circle(0.955, 96), 1.075, 1.08, ISO)
obj_from_bm("Linac_DrumLED", bm, "M_Accent", origin=(0, 1.078, ISO), parent=drum)

# arm + head: side profile (y, z-ISO) extruded across X
prof = [(1.10, 0.52), (1.10, 1.44), (0.30, 1.44), (-0.22, 1.40), (-0.38, 1.30), (-0.42, 1.12), (-0.42, 0.70),
        (-0.34, 0.62), (0.32, 0.62), (0.44, 0.70), (0.56, 0.86), (0.98, 0.86), (1.06, 0.74)]
bm = bmesh.new()
f = [bm.verts.new((-0.40, p[0], ISO + p[1])) for p in prof]
b = [bm.verts.new((0.40, p[0], ISO + p[1])) for p in prof]
n = len(prof)
bm.faces.new(f); bm.faces.new(list(reversed(b)))
for i in range(n):
    j = (i + 1) % n
    bm.faces.new((f[i], f[j], b[j], b[i]))
bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
arm = obj_from_bm("Linac_Head", bm, "M_Shell", origin=(0, 0, ISO + 1.0), parent=gantry)
bevel(arm, 0.10, 6, angle=30)
# accent stripe along the arm side
for sx in (-1, 1):
    bm = bmesh.new(); bm_box(bm, (0.012, 1.0, 0.025), (0.405 * sx, 0.52, ISO + 1.22))
    obj_from_bm("Linac_HeadLED" + ("L" if sx < 0 else "R"), bm, "M_Accent", origin=(0.405 * sx, 0.52, ISO + 1.22), parent=arm)

# collimator (rotates about the beam axis)
coll = empty("Linac_Collimator", (0, 0, ISO + 0.52), parent=gantry)
bm = bmesh.new()
bm_lathe(bm, [(0.0, 0.43), (0.30, 0.43), (0.345, 0.45), (0.36, 0.49), (0.36, 0.63), (0.0, 0.63)], segs=72)
bmesh.ops.translate(bm, vec=(0, 0, ISO), verts=bm.verts)
bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
cb = obj_from_bm("Linac_CollimatorBody", bm, "M_ShellGrey", origin=(0, 0, ISO + 0.52), parent=coll, smooth_angle=50)
bm = bmesh.new()
bm_lathe(bm, [(0.318, 0.437), (0.330, 0.437), (0.330, 0.444), (0.318, 0.444), (0.318, 0.437)], segs=72)
bmesh.ops.translate(bm, vec=(0, 0, ISO), verts=bm.verts)
obj_from_bm("Linac_CollimatorLED", bm, "M_Accent", origin=(0, 0, ISO + 0.44), parent=coll)
bm = bmesh.new(); bm_box(bm, (0.20, 0.20, 0.006), (0, 0, ISO + 0.428))
obj_from_bm("Linac_CollimatorWindow", bm, "M_Glass", origin=(0, 0, ISO + 0.428), parent=coll)
empty("Linac_Source", (0, 0, ISO + 1.0), parent=gantry)

# MV portal imager (EPID) below isocentre
bm = bmesh.new()
for sx in (-1, 1):
    bm_box(bm, (0.07, 1.00, 0.06), (0.26 * sx, 0.62, ISO - 0.66))
epid_arm = obj_from_bm("Linac_EPIDArm", bm, "M_ShellGrey", origin=(0, 0.62, ISO - 0.66), parent=gantry)
bevel(epid_arm, 0.015, 2)
bm = bmesh.new(); bm_box(bm, (0.56, 0.56, 0.07), (0, 0, ISO - 0.60))
epid = obj_from_bm("Linac_EPID", bm, "M_Shell", origin=(0, 0, ISO - 0.60), parent=gantry)
bevel(epid, 0.025, 3)
bm = bmesh.new(); bm_box(bm, (0.43, 0.43, 0.004), (0, 0, ISO - 0.564))
obj_from_bm("Linac_EPIDFace", bm, "M_Dark", origin=(0, 0, ISO - 0.564), parent=epid)

# kV imaging (perpendicular to the MV beam)
bm = bmesh.new(); bm_box(bm, (0.30, 0.34, 0.30), (1.00, 0.0, ISO))
kvs = obj_from_bm("Linac_kVSource", bm, "M_Shell", origin=(1.0, 0, ISO), parent=gantry)
bevel(kvs, 0.05, 3)
bm = bmesh.new(); bm_box(bm, (0.08, 1.05, 0.08), (1.00, 0.62, ISO + 0.18))
kva = obj_from_bm("Linac_kVSourceArm", bm, "M_ShellGrey", origin=(1.0, 0.62, ISO), parent=kvs)
bevel(kva, 0.015, 2)
bm = bmesh.new(); bm_box(bm, (0.07, 0.50, 0.50), (-0.62, 0.0, ISO))
kvp = obj_from_bm("Linac_kVPanel", bm, "M_Shell", origin=(-0.62, 0, ISO), parent=gantry)
bevel(kvp, 0.02, 3)
bm = bmesh.new(); bm_box(bm, (0.004, 0.40, 0.40), (-0.584, 0.0, ISO))
obj_from_bm("Linac_kVPanelFace", bm, "M_Dark", origin=(-0.584, 0, ISO), parent=kvp)
bm = bmesh.new(); bm_box(bm, (0.08, 1.05, 0.08), (-0.62, 0.62, ISO + 0.18))
kpa = obj_from_bm("Linac_kVPanelArm", bm, "M_ShellGrey", origin=(-0.62, 0.62, ISO), parent=kvp)
bevel(kpa, 0.015, 2)

empty("Linac_Iso", (0, 0, ISO), parent=root)

# ---- treatment couch (pedestal + carbon tabletop)
bm = bmesh.new(); bm_cyl(bm, 0.70, 0.02, 72, center=(0, -1.15, 0.01))
obj_from_bm("Linac_Turntable", bm, "M_ShellGrey", origin=(0, -1.15, 0), parent=root)
bm = bmesh.new(); bm_box(bm, (0.52, 0.80, 0.86), (0, -1.35, 0.45))
ped = obj_from_bm("Linac_CouchBase", bm, "M_Shell", origin=(0, -1.35, 0), parent=root)
bevel(ped, 0.07, 4)
TOP = ISO - 0.12
couch = empty("Linac_CouchTop", (0, 0.45, TOP), parent=root)
bm = bmesh.new(); bm_box(bm, (0.40, 1.30, 0.22), (0, -0.95, TOP - 0.17))
rail = obj_from_bm("Linac_CouchRail", bm, "M_ShellGrey", origin=(0, 0.45, TOP), parent=couch)
bevel(rail, 0.04, 3)
bm = bmesh.new(); bm_box(bm, (0.53, 2.10, 0.05), (0, -0.60, TOP - 0.025))
top = obj_from_bm("Linac_Tabletop", bm, "M_Dark", origin=(0, 0.45, TOP), parent=couch)
bevel(top, 0.02, 3)
for sx in (-1, 1):
    bm = bmesh.new(); bm_box(bm, (0.006, 1.9, 0.012), (0.266 * sx, -0.60, TOP - 0.02))
    obj_from_bm("Linac_TopLED" + ("L" if sx < 0 else "R"), bm, "M_Accent", origin=(0.266 * sx, -0.60, TOP), parent=top)

objs = model_objects(root)
finalize(objs)
for o in objs:
    if o.type == "MESH":
        auto_uv(o)
result = export_fbx(root, "Linac.fbx")
