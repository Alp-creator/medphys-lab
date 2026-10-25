# MRI scanner and PET scanner (two exports from one script).
def couch(prefix, root, top_z, y_front, length, base_len, base_h):
    bm = bmesh.new(); bm_box(bm, (0.50, base_len, 0.08), (0, y_front - 0.25 - base_len / 2, 0.04))
    f = obj_from_bm(prefix + "_CouchFoot", bm, "M_Dark", origin=(0, y_front - 0.25 - base_len / 2, 0), parent=root)
    bevel(f, 0.02, 2)
    bm = bmesh.new(); bm_box(bm, (0.44, base_len - 0.15, base_h), (0, y_front - 0.25 - base_len / 2, base_h / 2 + 0.06))
    p = obj_from_bm(prefix + "_CouchBase", bm, "M_Shell", origin=(0, y_front - 0.25 - base_len / 2, 0), parent=root)
    bevel(p, 0.06, 4)
    top = empty(prefix + "_CouchTop", (0, y_front, top_z), parent=root)
    bm = bmesh.new(); bm_box(bm, (0.50, length, 0.06), (0, y_front - length / 2, top_z - 0.04))
    t = obj_from_bm(prefix + "_Tabletop", bm, "M_ShellGrey", origin=(0, y_front, top_z), parent=top)
    bevel(t, 0.025, 3)
    bm = bmesh.new(); bm_box(bm, (0.44, length - 0.12, 0.05), (0, y_front - length / 2 - 0.02, top_z + 0.01))
    pad = obj_from_bm(prefix + "_Pad", bm, "M_Pad", origin=(0, y_front, top_z), parent=top)
    bevel(pad, 0.02, 3)
    for sx in (-1, 1):
        bm = bmesh.new(); bm_box(bm, (0.006, length - 0.2, 0.012), (0.253 * sx, y_front - length / 2, top_z - 0.04))
        obj_from_bm(prefix + "_TopLED" + ("L" if sx < 0 else "R"), bm, "M_Accent", origin=(0.253 * sx, y_front - length / 2, top_z), parent=t)
    return top

def flare(name, parent, iso_z, r_in, r_out, y_face, sign, depth=0.14, led=True):
    prof = [(r_in, -depth), (r_in, -0.012), (r_in + 0.012, 0.004), (r_in + 0.05, 0.012),
            (r_out - 0.04, 0.012), (r_out - 0.01, 0.004), (r_out, -0.02), (r_out, -depth)]
    prof = [(r, h * sign) for r, h in prof]
    if sign < 0:
        prof = list(reversed(prof))
    bm = bmesh.new(); bm_lathe(bm, prof + [prof[0]], segs=96)
    bmesh.ops.rotate(bm, verts=bm.verts, cent=(0, 0, 0), matrix=Matrix.Rotation(math.pi / 2, 3, "X"))
    bmesh.ops.translate(bm, vec=(0, y_face, iso_z), verts=bm.verts)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    o = obj_from_bm(name, bm, "M_ShellGrey", origin=(0, y_face, iso_z), parent=parent, smooth_angle=50)
    if led:
        rm = (r_in + r_out) / 2 + 0.03
        bm = bmesh.new()
        p2 = [(rm - 0.009, 0.0125), (rm + 0.009, 0.0125), (rm + 0.009, 0.019), (rm - 0.009, 0.019), (rm - 0.009, 0.0125)]
        p2 = [(r, h * sign) for r, h in p2]
        bm_lathe(bm, p2, segs=96)
        bmesh.ops.rotate(bm, verts=bm.verts, cent=(0, 0, 0), matrix=Matrix.Rotation(math.pi / 2, 3, "X"))
        bmesh.ops.translate(bm, vec=(0, y_face, iso_z), verts=bm.verts)
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        obj_from_bm(name + "LED", bm, "M_Accent", origin=(0, y_face, iso_z), parent=o)
    return o

out = {}
# ======================================================================= MRI
clear_scene()
ISO = 1.00
BORE = 0.35
root = empty("MRI_Scanner", (0, 0, 0))
outer = superellipse(1.12, 1.05, 3.0, 128, phase=-math.pi / 2)
inner = circle(BORE, 128, phase=-math.pi / 2)
bm = bmesh.new(); bm_ring_extrude(bm, outer, inner, -0.55, 0.62, ISO)
body = obj_from_bm("MRI_Housing", bm, "M_Shell", origin=(0, 0, ISO), parent=root)
bevel(body, 0.06, 4)
outer2 = superellipse(1.10, 1.03, 3.0, 128, phase=-math.pi / 2)
bm = bmesh.new(); bm_ring_extrude(bm, outer2, inner, -0.86, -0.56, ISO)
cov = obj_from_bm("MRI_Cover", bm, "M_Shell", origin=(0, -0.71, ISO), parent=root)
bevel(cov, 0.12, 6)
bm = bmesh.new(); bm_ring_extrude(bm, outer2, inner, 0.63, 0.80, ISO)
rc = obj_from_bm("MRI_RearCover", bm, "M_ShellGrey", origin=(0, 0.71, ISO), parent=root)
bevel(rc, 0.08, 4)
flare("MRI_Flare", root, ISO, BORE, 0.66, -0.86, -1)
bm = bmesh.new(); bm_box(bm, (1.70, 1.40, 0.10), (0, -0.02, 0.05))
b = obj_from_bm("MRI_Base", bm, "M_Dark", origin=(0, 0, 0), parent=root)
bevel(b, 0.03, 2)
# bore liner (inside surface, slightly brighter)
bm = bmesh.new(); bm_ring_extrude(bm, circle(BORE + 0.004, 96), circle(BORE - 0.002, 96), -0.80, 0.78, ISO)
obj_from_bm("MRI_BoreLiner", bm, "M_ShellGrey", origin=(0, 0, ISO), parent=root)
# bore light strip on the top inside
bm = bmesh.new(); bm_box(bm, (0.12, 1.4, 0.006), (0, -0.02, ISO + BORE - 0.006))
obj_from_bm("MRI_BoreLight", bm, "M_Light", origin=(0, 0, ISO + BORE), parent=root)
# front display + top LED band
bm = bmesh.new(); bm_box(bm, (0.30, 0.03, 0.18), (0.72, -0.875, ISO + 0.52))
d = obj_from_bm("MRI_Panel", bm, "M_Dark", origin=(0.72, -0.875, ISO + 0.52), parent=root)
bevel(d, 0.01, 2)
bm = bmesh.new(); bm_box(bm, (0.26, 0.004, 0.14), (0.72, -0.892, ISO + 0.52))
obj_from_bm("MRI_Screen", bm, "M_Screen", origin=(0.72, -0.892, ISO + 0.52), parent=d)
bm = bmesh.new(); bm_box(bm, (1.2, 0.012, 0.02), (0, -0.865, ISO + 0.90))
obj_from_bm("MRI_TopLED", bm, "M_Accent", origin=(0, -0.865, ISO + 0.90), parent=root)
empty("MRI_Iso", (0, 0, ISO), parent=root)
top = couch("MRI", root, ISO - 0.20, -0.75, 2.10, 1.40, 0.62)
# head coil (half shell with windows) at head end
bm = bmesh.new()
prof = []
for i in range(25):
    a = math.pi * i / 24
    prof.append((0.16 * math.cos(a), 0.16 * math.sin(a)))
inner_c = [(0.13 * math.cos(math.pi * i / 24), 0.13 * math.sin(math.pi * i / 24)) for i in range(25)]
poly = prof + list(reversed(inner_c))
f = [bm.verts.new((p[0], -0.82, ISO - 0.16 + p[1])) for p in poly]
bk = [bm.verts.new((p[0], -1.10, ISO - 0.16 + p[1])) for p in poly]
n = len(poly)
for i in range(n):
    j = (i + 1) % n
    bm.faces.new((f[j], f[i], bk[i], bk[j]))
for i in range(24):
    bm.faces.new((f[i], f[i + 1], f[n - 2 - i], f[n - 1 - i]))
    bm.faces.new((bk[n - 1 - i], bk[n - 2 - i], bk[i + 1], bk[i]))
bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
hc = obj_from_bm("MRI_HeadCoil", bm, "M_ShellGrey", origin=(0, -0.96, ISO - 0.16), parent=top)
bevel(hc, 0.01, 2)
objs = model_objects(root); finalize(objs)
for o in objs:
    if o.type == "MESH": auto_uv(o)
out["mri"] = export_fbx(root, "MRI_Scanner.fbx")

# ======================================================================= PET
clear_scene()
ISO = 1.02
BORE = 0.38
root = empty("PET_Scanner", (0, 0, 0))
outer = superellipse(0.98, 0.94, 3.4, 128, phase=-math.pi / 2)
inner = circle(BORE, 128, phase=-math.pi / 2)
bm = bmesh.new(); bm_ring_extrude(bm, outer, inner, 0.0, 0.50, ISO)
h = obj_from_bm("PET_Housing", bm, "M_Shell", origin=(0, 0.25, ISO), parent=root)
bevel(h, 0.06, 4)
bm = bmesh.new(); bm_ring_extrude(bm, superellipse(0.97, 0.93, 3.4, 128, phase=-math.pi / 2), inner, -0.50, -0.01, ISO)
c = obj_from_bm("PET_Cover", bm, "M_Shell", origin=(0, -0.25, ISO), parent=root)
bevel(c, 0.10, 5)
flare("PET_Flare", root, ISO, BORE, 0.62, -0.50, -1)
flare("PET_FlareRear", root, ISO, BORE, 0.62, 0.50, 1, led=False)
bm = bmesh.new(); bm_box(bm, (1.40, 1.05, 0.14), (0, 0, 0.07))
b = obj_from_bm("PET_Base", bm, "M_Dark", origin=(0, 0, 0), parent=root)
bevel(b, 0.03, 2)
bm = bmesh.new(); bm_box(bm, (0.012, 0.02, 1.2), (-0.86, -0.505, ISO))
obj_from_bm("PET_SideLED_L", bm, "M_Accent", origin=(-0.86, -0.505, ISO), parent=root)
bm = bmesh.new(); bm_box(bm, (0.012, 0.02, 1.2), (0.86, -0.505, ISO))
obj_from_bm("PET_SideLED_R", bm, "M_Accent", origin=(0.86, -0.505, ISO), parent=root)
# detector ring support (crystal blocks are generated in Unity)
bm = bmesh.new(); bm_ring_extrude(bm, circle(0.62, 96), circle(0.56, 96), -0.30, -0.02, ISO)
obj_from_bm("PET_RingFrame", bm, "M_Metal", origin=(0, -0.16, ISO), parent=root)
empty("PET_Iso", (0, -0.16, ISO), parent=root)
couch("PET", root, ISO - 0.16, -0.55, 2.0, 1.30, 0.60)
objs = model_objects(root); finalize(objs)
for o in objs:
    if o.type == "MESH": auto_uv(o)
out["pet"] = export_fbx(root, "PET_Scanner.fbx")
result = out
