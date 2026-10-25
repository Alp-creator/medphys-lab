# CT scanner: static housing (+ removable front cover), rotating rotor (tube + detector arc), couch.
# Blender axes: bore axis = Y (patient enters from -Y), Z up. Bore centre at z = ISO_Z.
clear_scene()
ISO_Z = 1.05
SCAN_Y = -0.20          # y of the scan plane (rotor centre)
BORE_R = 0.40
N = 128

root = empty("CT_Scanner", (0, 0, 0))

# ---- housing (back part + front cover), superellipse outer, circular bore
outer = superellipse(1.00, 0.95, 3.2, N, phase=-math.pi / 2)
inner = circle(BORE_R, N, phase=-math.pi / 2)
bm = bmesh.new(); bm_ring_extrude(bm, outer, inner, -0.05, 0.40, ISO_Z)
back = obj_from_bm("CT_Housing", bm, "M_Shell", origin=(0, 0, ISO_Z), parent=root)
bevel(back, 0.05, 4)

outer2 = superellipse(0.99, 0.94, 3.2, N, phase=-math.pi / 2)
bm = bmesh.new(); bm_ring_extrude(bm, outer2, inner, -0.40, -0.06, ISO_Z)
cover = obj_from_bm("CT_Cover", bm, "M_Shell", origin=(0, -0.23, ISO_Z), parent=root)
bevel(cover, 0.09, 5)

# base plinth
bm = bmesh.new(); bm_box(bm, (1.45, 0.86, 0.26), (0, 0.0, 0.13))
base = obj_from_bm("CT_Base", bm, "M_Dark", origin=(0, 0, 0), parent=root)
bevel(base, 0.03, 3)

# ---- front & rear bezels (flared bore entrance) with LED accent ring
def bezel(name, sign, accent):
    prof = [(0.395, -0.30), (0.395, -0.385), (0.405, -0.410), (0.43, -0.428), (0.47, -0.436),
            (0.56, -0.436), (0.585, -0.425), (0.595, -0.40), (0.595, -0.30)]
    prof = [(r, h * sign) for r, h in prof]
    if sign < 0:
        prof = list(reversed(prof))
    bm = bmesh.new()
    bm_lathe(bm, prof + [prof[0]], segs=96)
    bmesh.ops.rotate(bm, verts=bm.verts, cent=(0, 0, 0), matrix=Matrix.Rotation(math.pi / 2, 3, "X"))
    bmesh.ops.translate(bm, vec=(0, 0, ISO_Z), verts=bm.verts)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    o = obj_from_bm(name, bm, "M_ShellGrey", origin=(0, 0.42 * sign, ISO_Z), parent=root, smooth_angle=50)
    if accent:
        bm = bmesh.new()
        prof = [(0.495, -0.437), (0.512, -0.437), (0.512, -0.430), (0.495, -0.430), (0.495, -0.437)]
        prof = [(r, h * sign) for r, h in prof]
        bm_lathe(bm, prof, segs=96)
        bmesh.ops.rotate(bm, verts=bm.verts, cent=(0, 0, 0), matrix=Matrix.Rotation(math.pi / 2, 3, "X"))
        bmesh.ops.translate(bm, vec=(0, 0, ISO_Z), verts=bm.verts)
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        obj_from_bm(name + "LED", bm, "M_Accent", origin=(0, 0.43 * sign, ISO_Z), parent=o)
    return o

bezel("CT_BezelFront", -1, True)
bezel("CT_BezelRear", 1, False)

# accent strip across the top of the cover
bm = bmesh.new(); bm_box(bm, (0.9, 0.012, 0.018), (0, -0.405, ISO_Z + 0.80))
obj_from_bm("CT_TopLED", bm, "M_Accent", origin=(0, -0.405, ISO_Z + 0.80), parent=root)

# front control panels left/right of bore
for sx in (-1, 1):
    bm = bmesh.new(); bm_box(bm, (0.24, 0.035, 0.15), (0.70 * sx, -0.415, ISO_Z + 0.42))
    p = obj_from_bm("CT_Panel" + ("L" if sx < 0 else "R"), bm, "M_Dark", origin=(0.70 * sx, -0.415, ISO_Z + 0.42), parent=root)
    bevel(p, 0.01, 2)
    bm = bmesh.new(); bm_box(bm, (0.20, 0.006, 0.11), (0.70 * sx, -0.434, ISO_Z + 0.42))
    s = obj_from_bm("CT_Screen" + ("L" if sx < 0 else "R"), bm, "M_Screen", origin=(0.70 * sx, -0.434, ISO_Z + 0.42), parent=p)

# ---- rotor (rotates about Y through bore centre)
rotor = empty("CT_Rotor", (0, SCAN_Y, ISO_Z), parent=root)
ring_o = circle(0.86, N); ring_i = circle(0.47, N)
bm = bmesh.new(); bm_ring_extrude(bm, ring_o, ring_i, SCAN_Y - 0.08, SCAN_Y + 0.08, ISO_Z)
ring = obj_from_bm("CT_RotorRing", bm, "M_Metal", origin=(0, SCAN_Y, ISO_Z), parent=rotor)
bevel(ring, 0.01, 2)

FOCUS = 0.60   # focus-to-isocentre distance
bm = bmesh.new()
bm_cyl(bm, 0.085, 0.38, 40, center=(0, SCAN_Y, ISO_Z + FOCUS + 0.10), axis="X")
bm_box(bm, (0.16, 0.14, 0.12), (0, SCAN_Y, ISO_Z + FOCUS + 0.02))
tube = obj_from_bm("CT_XrayTube", bm, "M_ShellGrey", origin=(0, SCAN_Y, ISO_Z + FOCUS + 0.06), parent=rotor)
bevel(tube, 0.01, 2)
bm = bmesh.new(); bm_box(bm, (0.12, 0.10, 0.05), (0, SCAN_Y, ISO_Z + FOCUS - 0.065))
obj_from_bm("CT_Collimator", bm, "M_Dark", origin=(0, SCAN_Y, ISO_Z + FOCUS - 0.065), parent=tube)
empty("CT_Focus", (0, SCAN_Y, ISO_Z + FOCUS), parent=rotor)

# detector arc centred on the focal spot
R_DET = 1.06; HALF = math.radians(26); STEPS = 40
def arc_pts(rad):
    pts = []
    for i in range(STEPS + 1):
        a = -HALF + 2 * HALF * i / STEPS
        pts.append((rad * math.sin(a), FOCUS - rad * math.cos(a)))
    return pts
inner_arc = arc_pts(R_DET); outer_arc = arc_pts(R_DET + 0.09)
poly = outer_arc + list(reversed(inner_arc))
bm = bmesh.new()
f = [bm.verts.new((p[0], SCAN_Y - 0.075, p[1] + ISO_Z)) for p in poly]
b = [bm.verts.new((p[0], SCAN_Y + 0.075, p[1] + ISO_Z)) for p in poly]
n = len(poly)
for i in range(n):
    j = (i + 1) % n
    bm.faces.new((f[j], f[i], b[i], b[j]))
# caps as quad strips (arc polygon is non-convex)
for i in range(STEPS):
    a0, a1 = i, i + 1
    c0, c1 = n - 1 - i, n - 2 - i
    bm.faces.new((f[a0], f[a1], f[c1], f[c0]))
    bm.faces.new((b[c0], b[c1], b[a1], b[a0]))
bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
det = obj_from_bm("CT_Detector", bm, "M_Dark", origin=(0, SCAN_Y, ISO_Z - 0.46), parent=rotor)
bevel(det, 0.008, 2)
# glowing detector face strip
inner2 = arc_pts(R_DET - 0.004); inner3 = arc_pts(R_DET + 0.002)
poly = inner3 + list(reversed(inner2))
bm = bmesh.new()
f = [bm.verts.new((p[0], SCAN_Y - 0.05, p[1] + ISO_Z)) for p in poly]
b = [bm.verts.new((p[0], SCAN_Y + 0.05, p[1] + ISO_Z)) for p in poly]
n = len(poly)
for i in range(n):
    j = (i + 1) % n
    bm.faces.new((f[j], f[i], b[i], b[j]))
bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
obj_from_bm("CT_DetectorFace", bm, "M_Accent", origin=(0, SCAN_Y, ISO_Z - 0.46), parent=det)

# electronics / counterweights on the rotor sides
for sx in (-1, 1):
    bm = bmesh.new(); bm_box(bm, (0.14, 0.13, 0.30), (0.68 * sx, SCAN_Y, ISO_Z + 0.05))
    e = obj_from_bm("CT_RotorBox" + ("L" if sx < 0 else "R"), bm, "M_ShellGrey", origin=(0.68 * sx, SCAN_Y, ISO_Z), parent=rotor)
    bevel(e, 0.01, 2)

empty("CT_Iso", (0, SCAN_Y, ISO_Z), parent=root)

# ---- patient couch
bm = bmesh.new()
bm_box(bm, (0.56, 1.50, 0.08), (0, -1.55, 0.04))
base_c = obj_from_bm("CT_CouchFoot", bm, "M_Dark", origin=(0, -1.55, 0), parent=root)
bevel(base_c, 0.02, 2)
bm = bmesh.new(); bm_box(bm, (0.46, 1.30, 0.62), (0, -1.55, 0.39))
ped = obj_from_bm("CT_CouchBase", bm, "M_Shell", origin=(0, -1.55, 0), parent=root)
bevel(ped, 0.06, 4)
bm = bmesh.new(); bm_box(bm, (0.38, 1.10, 0.20), (0, -1.55, 0.78))
lift = obj_from_bm("CT_CouchLift", bm, "M_ShellGrey", origin=(0, -1.55, 0.70), parent=root)
bevel(lift, 0.04, 3)

TOP_Z = 0.93
top = empty("CT_CouchTop", (0, -0.45, TOP_Z), parent=root)
# shallow curved carbon tabletop cross-section
prof = []
W = 0.23
for i in range(17):
    x = -W + 2 * W * i / 16
    prof.append((x, TOP_Z - 0.045 + 0.04 * (x / W) ** 2))
for i in range(17):
    x = W - 2 * W * i / 16
    prof.append((x, TOP_Z - 0.010 + 0.04 * (x / W) ** 2 - 0.035 * (1 - (x / W) ** 2) * 0))
bm = bmesh.new()
pts = [(p[0], p[1]) for p in prof]
# extrude along Y
fr = [bm.verts.new((p[0], -2.40, p[1])) for p in pts]
bk = [bm.verts.new((p[0], -0.45, p[1])) for p in pts]
n = len(pts)
for i in range(n):
    j = (i + 1) % n
    bm.faces.new((fr[j], fr[i], bk[i], bk[j]))
bm.faces.new(fr); bm.faces.new(list(reversed(bk)))
bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
tt = obj_from_bm("CT_Tabletop", bm, "M_Dark", origin=(0, -0.45, TOP_Z), parent=top)
bevel(tt, 0.008, 2)
bm = bmesh.new(); bm_box(bm, (0.40, 1.85, 0.045), (0, -1.40, TOP_Z + 0.018))
pad = obj_from_bm("CT_Pad", bm, "M_Pad", origin=(0, -0.45, TOP_Z), parent=top)
bevel(pad, 0.018, 3)
bm = bmesh.new(); bm_box(bm, (0.30, 0.22, 0.07), (0, -0.62, TOP_Z + 0.07))
head = obj_from_bm("CT_HeadRest", bm, "M_Pad", origin=(0, -0.45, TOP_Z), parent=top)
bevel(head, 0.03, 3)

objs = model_objects(root)
finalize(objs)
for o in objs:
    if o.type == "MESH":
        auto_uv(o)
result = export_fbx(root, "CT_Scanner.fbx")
