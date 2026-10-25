# Rigged patients for the case mode: one parametric human (skin modifier + subdivision, auto-weighted armature)
# exported as Patient_<id>.fbx per person. Same bone names as the Worker, so HumanRig poses them.
# Colours (skin tone, clothes, hair) are assigned in Unity per patient, the FBX only carries region materials.
import os
from mathutils.bvhtree import BVHTree

MATS.update({
    "M_Skin":     ((0.86, 0.66, 0.54, 1), 0.0, 0.45, 0),
    "M_Top":      ((0.20, 0.45, 0.75, 1), 0.0, 0.65, 0),
    "M_Bottom":   ((0.12, 0.16, 0.25, 1), 0.0, 0.70, 0),
    "M_Shoe":     ((0.92, 0.93, 0.95, 1), 0.0, 0.40, 0),
    "M_Hair":     ((0.12, 0.08, 0.06, 1), 0.0, 0.55, 0),
    "M_Eye":      ((0.03, 0.03, 0.04, 1), 0.0, 0.90, 0),
    "M_EyeWhite": ((0.97, 0.97, 0.97, 1), 0.0, 0.85, 0),
    "M_Mouth":    ((0.45, 0.08, 0.10, 1), 0.0, 0.60, 0),
    "M_Teeth":    ((0.98, 0.98, 0.96, 1), 0.0, 0.70, 0),
    "M_Blush":    ((0.95, 0.55, 0.52, 1), 0.0, 0.40, 0),
    "M_Frame":    ((0.10, 0.10, 0.12, 1), 0.3, 0.30, 0),
    "M_Band":     ((0.97, 0.97, 0.99, 1), 0.0, 0.50, 0),
})

# sz/sw: vertical / horizontal skeleton scale, hs: head scale, rt/rl: torso / limb girth,
# belly: front belly ellipsoid size (m), armout: extra arm abduction (m)
PATIENTS = [
    dict(id="Child",    sz=0.74, sw=0.78, hs=0.88, rt=0.74, rl=0.74, hair="kid",      sleeve="short", legs="shorts", mouth="open"),
    dict(id="Woman",    sz=0.94, sw=0.92, hs=0.96, rt=0.92, rl=0.90, hair="ponytail", sleeve="long",  female=True, mouth="gentle"),
    dict(id="Obese",    sz=1.01, sw=1.05, hs=1.00, rt=1.36, rl=1.22, hair="short",    sleeve="short", belly=0.115, armout=0.07, mustache=True, mouth="gentle"),
    dict(id="OldMan",   sz=0.98, sw=0.98, hs=1.00, rt=1.04, rl=0.94, hair="bald",     sleeve="long",  belly=0.05, glasses=True, mustache=True, mouth="gentle"),
    dict(id="OldWoman", sz=0.90, sw=0.90, hs=0.96, rt=1.02, rl=0.94, hair="bun",      sleeve="long",  female=True, belly=0.035, glasses=True, mouth="gentle"),
    dict(id="Lady",     sz=0.955, sw=0.92, hs=0.97, rt=0.95, rl=0.92, hair="bob",     sleeve="short", female=True, mouth="open"),
    dict(id="Man",      sz=1.04, sw=1.04, hs=1.01, rt=1.08, rl=1.06, hair="short",    sleeve="short", beard=True, mouth="open"),
]
ONLY = os.environ.get("MP_PATIENT", "")

J0 = {
    "pelvis": (0, 0.0, 0.98), "spine": (0, 0.012, 1.14), "chest": (0, 0.0, 1.31), "neck": (0, 0.012, 1.485),
    "head": (0, -0.005, 1.615), "top": (0, 0.0, 1.735),
}
for s, sx in (("L", 1), ("R", -1)):
    J0["hip" + s] = (0.092 * sx, 0.0, 0.94)
    J0["knee" + s] = (0.098 * sx, -0.012, 0.52)
    J0["ankle" + s] = (0.100 * sx, 0.018, 0.09)
    J0["toe" + s] = (0.104 * sx, -0.125, 0.035)
    J0["clav" + s] = (0.06 * sx, 0.01, 1.415)
    J0["shoulder" + s] = (0.178 * sx, 0.012, 1.405)
    J0["elbow" + s] = (0.235 * sx, 0.03, 1.155)
    J0["wrist" + s] = (0.262 * sx, 0.0, 0.905)
    J0["hand" + s] = (0.272 * sx, -0.012, 0.80)
R0 = {"pelvis": (0.195, 0.140), "spine": (0.172, 0.125), "chest": (0.215, 0.140), "neck": (0.066, 0.068)}
for s in ("L", "R"):
    R0.update({"hip" + s: (0.125, 0.125), "knee" + s: (0.080, 0.080), "ankle" + s: (0.054, 0.056), "toe" + s: (0.056, 0.034),
               "clav" + s: (0.070, 0.066), "shoulder" + s: (0.066, 0.064), "elbow" + s: (0.056, 0.056), "wrist" + s: (0.043, 0.034),
               "hand" + s: (0.058, 0.024)})
EDGES = [("pelvis", "spine"), ("spine", "chest"), ("chest", "neck")]
for s in ("L", "R"):
    EDGES += [("pelvis", "hip" + s), ("hip" + s, "knee" + s), ("knee" + s, "ankle" + s), ("ankle" + s, "toe" + s),
              ("chest", "clav" + s), ("clav" + s, "shoulder" + s), ("shoulder" + s, "elbow" + s), ("elbow" + s, "wrist" + s), ("wrist" + s, "hand" + s)]
N0 = Vector((0, 0.012, 1.485))      # adult neck joint: the head is modelled around it, then scaled / moved


def bm_torus(bm, center, axis, R, r, seg=28, seg2=8):
    axis = Vector(axis).normalized()
    a = axis.orthogonal().normalized(); b = axis.cross(a)
    rings = []
    for i in range(seg):
        t = 2 * math.pi * i / seg
        d = a * math.cos(t) + b * math.sin(t)
        rings.append([bm.verts.new(Vector(center) + d * (R + r * math.cos(2 * math.pi * j / seg2)) + axis * (r * math.sin(2 * math.pi * j / seg2)))
                      for j in range(seg2)])
    for i in range(seg):
        for j in range(seg2):
            i2, j2 = (i + 1) % seg, (j + 1) % seg2
            bm.faces.new((rings[i][j], rings[i2][j], rings[i2][j2], rings[i][j2]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)


def smoothstep(a, b, x):
    t = max(0.0, min(1.0, (x - a) / (b - a)))
    return t * t * (3 - 2 * t)


def build(P):
    pid = P["id"]
    sz, sw, hs, rt, rl = P["sz"], P["sw"], P["hs"], P["rt"], P["rl"]
    fem = P.get("female", False)
    armout = P.get("armout", 0.0)
    nm = lambda n: pid + "_" + n

    # ---------------- skeleton
    J = {k: Vector((x * sw, y * sw, z * sz)) for k, (x, y, z) in J0.items()}
    for s, sx in (("L", 1), ("R", -1)):
        if fem:
            J["hip" + s].x *= 1.07
            for k in ("clav", "shoulder", "elbow", "wrist", "hand"): J[k + s].x *= 0.93
        J["elbow" + s].x += armout * 0.55 * sx
        J["wrist" + s].x += armout * sx
        J["hand" + s].x += armout * 1.05 * sx
    def HT(p): return Vector(J["neck"]) + (Vector(p) - N0) * hs
    J["head"] = HT(J0["head"]); J["top"] = HT(J0["top"])
    R = {}
    for k, (rx, ry) in R0.items():
        f = rt if k in ("pelvis", "spine", "chest") else (rt + 1) * 0.5 if k == "neck" else rl
        R[k] = (rx * f, ry * f)
    if fem:
        R["pelvis"] = (R["pelvis"][0] * 1.08, R["pelvis"][1] * 1.02)
        for s in ("L", "R"): R["hip" + s] = (R["hip" + s][0] * 1.05, R["hip" + s][1] * 1.05)

    names = [n for n in J0.keys() if n not in ("head", "top")]
    me = bpy.data.meshes.new(nm("Body"))
    me.from_pydata([J[n] for n in names], [(names.index(a), names.index(b)) for a, b in EDGES], [])
    me.update()
    body = bpy.data.objects.new(nm("Body"), me)
    bpy.context.scene.collection.objects.link(body)
    sk = body.modifiers.new("Skin", "SKIN"); sk.use_smooth_shade = True; sk.branch_smoothing = 0.6
    for i, n in enumerate(names):
        v = me.skin_vertices[0].data[i]
        v.radius = R[n]; v.use_root = (n == "pelvis")
    sub = body.modifiers.new("Sub", "SUBSURF"); sub.levels = 3; sub.render_levels = 3
    apply_modifiers(body)

    # soft-tissue shape: belly and bust are smooth forward bulges of the front surface (no separate meshes)
    belly = P.get("belly", 0.0)
    zbel = (J["spine"].z + J["pelvis"].z) * 0.5 - 0.01 * sz
    zbust = J["chest"].z - 0.03 * sz
    for v in body.data.vertices:
        co = v.co
        if co.y > 0.03: continue
        wf = smoothstep(0.03, -0.05, co.y)
        side = "L" if co.x > 0 else "R"
        sh, hd = J["shoulder" + side], J["hand" + side]
        seg = hd - sh
        t = max(0.0, min(1.0, (co - sh).dot(seg) / seg.length_squared))
        pt = sh + seg * t
        if (co - pt).length < 0.085 * max(rl, 0.8) and abs(co.x) > abs(pt.x) - 0.035 * sw: continue
        d = 0.0
        if belly > 0:
            wz = 0.19 * sz if co.z < zbel else 0.13 * sz
            d += belly * math.exp(-(co.x / (0.15 * rt)) ** 2 - ((co.z - zbel) / wz) ** 2)
        if fem:
            for sx in (-1, 1):
                d += 0.036 * rt * math.exp(-((co.x - 0.06 * sw * sx) / (0.046 * rt)) ** 2 - ((co.z - zbust) / (0.05 * sz)) ** 2)
        co.y -= d * wf
    body.data.update()

    # ---------------- clothing regions
    zw, zs, zc = 0.99 * sz, 0.115 * sz, 1.47 * sz
    zk = J["kneeL"].z + 0.07 * sz
    bm = bmesh.new(); bm.from_mesh(body.data)
    def cut(co, no):
        geom = bm.verts[:] + bm.edges[:] + bm.faces[:]
        bmesh.ops.bisect_plane(bm, geom=geom, dist=1e-5, plane_co=Vector(co), plane_no=Vector(no))
    cut((0, 0, zw), (0, 0, 1)); cut((0, 0, zs), (0, 0, 1)); cut((0, 0, zc), (0, 0, 1))
    if P.get("legs") == "shorts": cut((0, 0, zk), (0, 0, 1))
    sleeve = {}
    for s in ("L", "R"):
        if P.get("sleeve") == "long":
            a, b = J["elbow" + s], J["wrist" + s]; co = a + (b - a) * 0.86
        else:
            a, b = J["shoulder" + s], J["elbow" + s]; co = a + (b - a) * 0.55
        no = (b - a).normalized()
        sleeve[s] = (co, no); cut(co, no)
    bm.to_mesh(body.data); bm.free()
    for mn in ("M_Top", "M_Bottom", "M_Skin", "M_Shoe"):
        body.data.materials.append(mat(mn))
    arm_r = 0.075 * max(rl, 0.8)
    for p in body.data.polygons:
        c = p.center
        idx = 0
        if c.z < zw: idx = 1
        if P.get("legs") == "shorts" and c.z < zk: idx = 2
        if c.z > zc and abs(c.x) < 0.11 * sw: idx = 2
        side = "L" if c.x > 0 else "R"
        pt, inner = None, None
        for a_, b_ in (("shoulder", "elbow"), ("elbow", "hand")):   # distance to the bent arm (two segments)
            sa, sb = J[a_ + side], J[b_ + side]
            seg = sb - sa
            t = max(0.0, min(1.0, (c - sa).dot(seg) / seg.length_squared))
            q = sa + seg * t
            if pt is None or (c - q).length < (c - pt).length:
                pt = q
                # the hand's end cap sits on the end joint: judge its faces from a point a little inside the hand
                inner = q - seg.normalized() * 0.035 if b_ == "hand" and t > 0.95 else q
        # an arm face lies near the arm axis AND faces away from it; thigh / flank faces that pass through the hanging
        # hand or forearm face towards the arm axis, so they keep the clothing colour when the arm moves away (sitting)
        rad = c - inner
        if (c - pt).length < arm_r and rad.length > 1e-6 and p.normal.dot(rad.normalized()) > 0.2:
            co, no = sleeve[side]
            idx = 2 if (c - co).dot(no) > 0 else 0
        if c.z < zs: idx = 3
        p.material_index = idx
        p.use_smooth = True
    body["smooth_angle"] = 80
    bpy.context.view_layer.update()
    body_bvh = BVHTree.FromObject(body, bpy.context.evaluated_depsgraph_get())
    def front_y(x, z):
        hit = body_bvh.ray_cast(Vector((x, -1.0, z)), Vector((0, 1, 0)))
        return hit[0].y if hit[0] is not None else -0.1

    def ell(name, c, r, m, rot=(0, 0, 0), seg=(24, 14)):
        b = bmesh.new()
        bmesh.ops.create_uvsphere(b, u_segments=seg[0], v_segments=seg[1], radius=1.0)
        bmesh.ops.scale(b, vec=Vector(r), verts=b.verts)
        bmesh.ops.rotate(b, verts=b.verts, cent=(0, 0, 0), matrix=Matrix.Rotation(rot[0], 3, "X") @ Matrix.Rotation(rot[2], 3, "Z"))
        bmesh.ops.translate(b, vec=Vector(c), verts=b.verts)
        return obj_from_bm(nm(name), b, m, origin=c, smooth_angle=80)

    chest_parts, spine_parts = [], []
    # ---------------- head (modelled at adult size around N0, transformed at the end)
    def egg_head():
        b = bmesh.new()
        bmesh.ops.create_uvsphere(b, u_segments=40, v_segments=28, radius=1.0)
        for v in b.verts:
            x, y, z = v.co
            low = max(0.0, -z)
            sx_ = 0.082 * (1 - 0.22 * low ** 1.5)
            sy_ = 0.098 * (1 - 0.10 * low ** 2)
            v.co = Vector((x * sx_, y * sy_ - 0.012 * low ** 2 * (1 if y < 0 else 0.3), z * 0.115))
        bmesh.ops.translate(b, vec=(0, 0, 1.600), verts=b.verts)
        return obj_from_bm(nm("HeadMesh"), b, "M_Skin", origin=(0, 0, 1.60), smooth_angle=80)
    head = egg_head()
    bpy.context.view_layer.update()
    head_bvh = BVHTree.FromObject(head, bpy.context.evaluated_depsgraph_get())
    b = bmesh.new(); bm_cyl(b, 0.056, 0.11, 24, center=(0, 0.012, 1.49))
    neckm = obj_from_bm(nm("NeckMesh"), b, "M_Skin", origin=(0, 0.01, 1.50), smooth_angle=80)
    head_parts = [head]

    def surf_y(x, z, off=0.0):
        loc = head.matrix_world.inverted() @ Vector((x, -1.0, z))
        hit = head_bvh.ray_cast(loc, Vector((0, 1, 0)))
        if hit[0] is None: return -0.09 - off
        return (head.matrix_world @ hit[0]).y - off

    # hair
    style = P.get("hair", "short")
    b = bmesh.new()
    bmesh.ops.create_uvsphere(b, u_segments=48, v_segments=32, radius=1.0)
    for v in b.verts:
        x, y, z = v.co
        if style == "bob" and z < 0:
            rr = math.hypot(x, y)
            if rr > 1e-6: x, y = x / rr * (1 - 0.06 * -z), y / rr * (1 - 0.06 * -z)
        v.co = Vector((x * 0.088, y * 0.104 + 0.004, z * 0.118 + 1.606))
    def phi_of(x, y): return math.atan2(abs(x), -y)
    def hairline(x, y):
        phi = phi_of(x, y)
        c4 = math.cos(phi / 2) ** 4
        if style == "bob":
            t = smoothstep(0.80, 1.25, phi)
            return 1.652 * (1 - t) + 1.522 * t
        if style == "kid":
            return 1.567 + 0.090 * c4
        if style == "ponytail":
            return 1.560 + 0.108 * c4
        return 1.567 + 0.104 * c4 + 0.0036 * math.cos(phi / 2) ** 2 * math.cos(6 * x / 0.088)
    below = {v: v.co.z < hairline(v.co.x, v.co.y) for v in b.verts}
    dead = [f for f in b.faces if all(below[v] for v in f.verts)]
    if style == "bald":
        for f in b.faces:
            cc = f.calc_center_median()
            if cc.z > 1.640 or phi_of(cc.x, cc.y) < 1.02: dead.append(f)
    bmesh.ops.delete(b, geom=list(set(dead)), context="FACES")
    for v in b.verts:
        if v.is_valid and below.get(v, False):
            v.co.z = hairline(v.co.x, v.co.y)
            v.co.x *= 0.985; v.co.y *= 0.985
    head_parts.append(obj_from_bm(nm("Hair"), b, "M_Hair", origin=(0, 0, 1.62), smooth_angle=80))
    if style == "ponytail":
        head_parts.append(ell("Ponytail", (0, 0.118, 1.560), (0.030, 0.032, 0.080), "M_Hair", rot=(0.35, 0, 0)))
        b = bmesh.new(); bm_torus(b, (0, 0.108, 1.618), (0, 0.45, -1), 0.020, 0.005)
        head_parts.append(obj_from_bm(nm("HairTie"), b, "M_Top", origin=(0, 0.108, 1.618), smooth_angle=80))
    if style == "bun":
        head_parts.append(ell("Bun", (0, 0.078, 1.708), (0.044, 0.042, 0.036), "M_Hair"))

    # face
    smile_open = P.get("mouth", "open") == "open"
    for sx in (-1, 1):
        side = "L" if sx > 0 else "R"
        ex, ez = 0.030 * sx, 1.624
        ey = surf_y(ex, ez)
        head_parts.append(ell("EyeWhite" + side, (ex, ey + 0.002, ez), (0.0125, 0.005, 0.0105), "M_EyeWhite"))
        head_parts.append(ell("Eye" + side, (ex + 0.001 * sx, ey - 0.0022, ez - 0.0005), (0.0068, 0.0028, 0.0074), "M_Eye"))
        head_parts.append(ell("Glint" + side, (ex + 0.0025 * sx, ey - 0.0045, ez + 0.0028), (0.0019, 0.0012, 0.0019), "M_Teeth"))
        head_parts.append(ell("Ear" + side, (0.081 * sx, 0.004, 1.608), (0.011, 0.020, 0.030), "M_Skin"))
        head_parts.append(ell("Cheek" + side, (0.046 * sx, surf_y(0.046 * sx, 1.592) + 0.0025, 1.592), (0.014, 0.004, 0.0095), "M_Blush"))
        pts = []
        for k in range(7):
            t = k / 6
            x = (0.016 + 0.032 * t) * sx
            z = 1.646 + 0.006 * math.sin(math.pi * t) - 0.004 * t
            pts.append((x, surf_y(x, z, 0.002), z))
        head_parts.append(curve_mesh(nm("Brow" + side), pts, 0.0028, "M_Hair"))
        if P.get("glasses"):
            gy = ey - 0.011
            b = bmesh.new(); bm_torus(b, (ex, gy, ez + 0.001), (0, 1, 0), 0.0175, 0.0016)
            head_parts.append(obj_from_bm(nm("Lens" + side), b, "M_Frame", origin=(ex, gy, ez), smooth_angle=80))
            b = bmesh.new()
            tx = 0.0838 * sx
            bm_box(b, (0.003, abs(gy - 0.004), 0.0032), (tx, (gy + 0.004) * 0.5, 1.628))
            bm_box(b, (abs(tx - (ex + 0.019 * sx)) + 0.002, 0.003, 0.0032), ((tx + ex + 0.019 * sx) * 0.5, gy, 1.628))
            head_parts.append(obj_from_bm(nm("Temple" + side), b, "M_Frame", origin=(tx, gy, 1.628), smooth_angle=30))
    if P.get("glasses"):
        gy = surf_y(0.0, 1.628) - 0.006
        b = bmesh.new(); bm_box(b, (0.026, 0.003, 0.003), (0, gy, 1.628))
        head_parts.append(obj_from_bm(nm("Bridge"), b, "M_Frame", origin=(0, gy, 1.628), smooth_angle=30))

    def face_poly(name, outline, m, off):
        b = bmesh.new()
        vs = [b.verts.new((x, surf_y(x, z, off), z)) for x, z in outline]
        b.faces.new(vs)
        bmesh.ops.triangulate(b, faces=b.faces[:], quad_method="BEAUTY", ngon_method="EAR_CLIP")
        ext = bmesh.ops.extrude_face_region(b, geom=b.faces[:])
        nv = [e for e in ext["geom"] if isinstance(e, bmesh.types.BMVert)]
        bmesh.ops.translate(b, vec=(0, 0.0015, 0), verts=nv)
        bmesh.ops.recalc_face_normals(b, faces=b.faces)
        head_parts.append(obj_from_bm(nm(name), b, m, origin=(0, surf_y(0, 1.56), 1.56), smooth_angle=80))
    if smile_open:
        W, top, depth = 0.024, 1.566, 0.011
        mouth = [(-W + 2 * W * k / 20, top + 0.0045 * (abs(-W + 2 * W * k / 20) / W) ** 2.2) for k in range(21)]
        for k in range(1, 20):
            a = math.pi * k / 20; x = W * math.cos(a)
            mouth.append((x, top - depth * math.sin(a) + 0.0035 * (abs(x) / W) ** 2))
        face_poly("Mouth", mouth, "M_Mouth", 0.0012)
        Wt = W * 0.70
        teeth = [(-Wt + 2 * Wt * k / 14, top - 0.0008 + 0.0045 * (abs(-Wt + 2 * Wt * k / 14) / W) ** 2.2) for k in range(15)]
        for k in range(14, -1, -1):
            x = -Wt + 2 * Wt * k / 14; e = abs(x) / Wt
            teeth.append((x, top - 0.0053 + 0.0045 * (abs(x) / W) ** 2.2 + 0.0035 * e ** 6))
        face_poly("Teeth", teeth, "M_Teeth", 0.0019)
    else:
        pts = []
        for k in range(11):
            x = -0.019 + 0.038 * k / 10
            z = 1.561 + 0.0055 * (abs(x) / 0.019) ** 2
            pts.append((x, surf_y(x, z, 0.0012), z))
        head_parts.append(curve_mesh(nm("Mouth"), pts, 0.0024, "M_Mouth"))
    head_parts.append(ell("Nose", (0, surf_y(0, 1.598) - 0.006, 1.598), (0.0105, 0.013, 0.017), "M_Skin", rot=(0.35, 0, 0)))
    if P.get("mustache"):
        head_parts.append(ell("Mustache", (0, surf_y(0, 1.5775) - 0.0035, 1.5775), (0.022, 0.0065, 0.0058), "M_Hair", rot=(0.25, 0, 0)))
    if P.get("beard"):
        b = bmesh.new(); b.from_mesh(head.data)
        b.transform(head.matrix_world)
        bmesh.ops.subdivide_edges(b, edges=b.edges[:], cuts=1, use_grid_fill=True)
        keep = []
        for f in b.faces:
            cc = f.calc_center_median()
            ax = abs(cc.x)
            chin = cc.z < 1.551 and cc.y < 0.03 and cc.z > 1.47
            jaw = ax > 0.050 and cc.z < 1.575 - 0.25 * max(0.0, 0.07 - ax) and cc.y < 0.03 and cc.z > 1.47
            burn = ax > 0.068 and cc.z < 1.606 and -0.035 < cc.y < 0.018
            if chin or jaw or burn: keep.append(f)
        bmesh.ops.delete(b, geom=[f for f in b.faces if f not in set(keep)], context="FACES")
        loose = [v for v in b.verts if not v.link_faces]
        bmesh.ops.delete(b, geom=loose, context="VERTS")
        for it in range(10):                          # relax the stair-stepped outline along the boundary
            moves = {}
            for v in b.verts:
                nb = [e.other_vert(v) for e in v.link_edges if e.is_boundary]
                if len(nb) == 2: moves[v] = v.co * 0.4 + (nb[0].co + nb[1].co) * 0.3
            for v, q in moves.items(): v.co = q
        inv = head.matrix_world.inverted()
        for v in b.verts:                             # back onto the face, 2.6 mm proud
            loc, nrm, _, _ = head_bvh.find_nearest(inv @ v.co)
            if loc is not None: v.co = head.matrix_world @ loc + nrm.normalized() * 0.0026
        head_parts.append(obj_from_bm(nm("Beard"), b, "M_Hair", origin=(0, 0, 1.56), smooth_angle=80))

    # head transform: scale about the adult neck joint and move onto this skeleton
    bpy.context.view_layer.update()      # matrix_world of freshly created objects is only valid after an update
    M = Matrix.Translation(J["neck"]) @ Matrix.Scale(hs, 4) @ Matrix.Translation(-N0)
    for o in head_parts + [neckm]:
        org = M @ o.matrix_world.translation
        o.data.transform(o.matrix_world)
        o.matrix_world = Matrix.Identity(4)
        o.data.transform(M)
        set_origin(o, org)

    # hospital wristband (left wrist)
    wa, wb = J["wristL"], J["elbowL"]
    wc = wa + (wb - wa) * 0.10
    b = bmesh.new(); bm_torus(b, wc, wb - wa, 0.036 * rl, 0.0065, 28, 8)
    band = obj_from_bm(nm("Wristband"), b, "M_Band", origin=wc, smooth_angle=80)

    # ---------------- armature
    arm_data = bpy.data.armatures.new(nm("Rig"))
    rig = bpy.data.objects.new("Patient_" + pid, arm_data)
    bpy.context.scene.collection.objects.link(rig)
    bpy.ops.object.select_all(action="DESELECT")
    bpy.context.view_layer.objects.active = rig
    rig.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    eb = arm_data.edit_bones
    def bone(name, a, b_, parent=None, connect=False):
        bn = eb.new(name)
        bn.head = Vector(J[a]) if isinstance(a, str) else Vector(a)
        bn.tail = Vector(J[b_]) if isinstance(b_, str) else Vector(b_)
        if parent: bn.parent = eb[parent]; bn.use_connect = connect
        return bn
    bone("Hips", (0, 0, 0.90 * sz), "spine")
    bone("Spine", "spine", "chest", "Hips", True)
    bone("Chest", "chest", "neck", "Spine", True)
    bone("Neck", "neck", "head", "Chest", True)
    bone("Head", "head", HT((0, 0, 1.80)), "Neck", True)
    for s in ("L", "R"):
        bone("UpperLeg." + s, "hip" + s, "knee" + s, "Hips")
        bone("LowerLeg." + s, "knee" + s, "ankle" + s, "UpperLeg." + s, True)
        bone("Foot." + s, "ankle" + s, "toe" + s, "LowerLeg." + s, True)
        bone("Shoulder." + s, "clav" + s, "shoulder" + s, "Chest")
        bone("UpperArm." + s, "shoulder" + s, "elbow" + s, "Shoulder." + s, True)
        bone("LowerArm." + s, "elbow" + s, "wrist" + s, "UpperArm." + s, True)
        bone("Hand." + s, "wrist" + s, "hand" + s, "LowerArm." + s, True)
    bpy.ops.object.mode_set(mode="OBJECT")
    bpy.ops.object.select_all(action="DESELECT")
    body.select_set(True); rig.select_set(True)
    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.parent_set(type="ARMATURE_AUTO")

    def attach(o, bone_name):
        bpy.context.view_layer.update()
        mw = o.matrix_world.copy()
        o.parent = rig; o.parent_type = "BONE"; o.parent_bone = bone_name
        bpy.context.view_layer.update()
        o.matrix_world = mw
    for o in head_parts: attach(o, "Head")
    attach(neckm, "Neck")
    for o in chest_parts: attach(o, "Chest")
    for o in spine_parts: attach(o, "Spine")
    attach(band, "LowerArm.L")

    objs = [rig, body, neckm, band] + head_parts + chest_parts + spine_parts
    for o in objs:
        if o.type == "MESH" and o is not body:
            apply_modifiers(o); shade(o)
    for o in objs:
        if o.type == "MESH": auto_uv(o)

    path = os.path.join(EXPORT_DIR, "Patient_%s.fbx" % pid)
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs: o.select_set(True)
    bpy.context.view_layer.objects.active = rig
    bpy.ops.export_scene.fbx(
        filepath=path, use_selection=True, object_types={"ARMATURE", "MESH", "EMPTY"},
        apply_unit_scale=True, apply_scale_options="FBX_SCALE_ALL",
        axis_forward="-Z", axis_up="Y", bake_space_transform=False,
        mesh_smooth_type="OFF", use_mesh_modifiers=True, add_leaf_bones=False,
        primary_bone_axis="Y", secondary_bone_axis="X", armature_nodetype="NULL",
        bake_anim=False, use_armature_deform_only=True)
    tris = sum(len(o.data.polygons) for o in objs if o.type == "MESH")
    height = max(J["top"].z, max((o.matrix_world @ Vector(v.co)).z for o in head_parts if o.type == "MESH" for v in o.data.vertices[:200]))
    return rig, {"file": path, "faces": tris, "height": round(height + 0.005, 3)}


def curve_mesh(name, pts, radius, m):
    cu = bpy.data.curves.new(name, "CURVE"); cu.dimensions = "3D"
    sp = cu.splines.new("POLY")
    sp.points.add(len(pts) - 1)
    for k, pt in enumerate(pts): sp.points[k].co = (pt[0], pt[1], pt[2], 1)
    cu.bevel_depth = radius; cu.bevel_resolution = 2
    bo = bpy.data.objects.new(name + "_c", cu); bpy.context.scene.collection.objects.link(bo)
    bpy.context.view_layer.update()
    me = bpy.data.meshes.new_from_object(bo.evaluated_get(bpy.context.evaluated_depsgraph_get()))
    bpy.data.objects.remove(bo, do_unlink=True)
    me.materials.append(mat(m))
    o = bpy.data.objects.new(name, me); bpy.context.scene.collection.objects.link(o)
    o["smooth_angle"] = 80
    return o


clear_scene()
result = {}
k = 0
for P in PATIENTS:
    if ONLY and P["id"] not in ONLY.split(","): continue
    rig, info = build(P)
    result[P["id"]] = info
    rig.location.x += 0.75 * k - 2.2     # line them up for the preview render
    k += 1
