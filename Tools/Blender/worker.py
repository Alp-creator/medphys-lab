# Rigged human worker (scrubs, survey-meter hand) built with a Skin modifier + Subdivision,
# auto-weighted to a named armature. Facing -Y (Blender front), Z up, height ~1.76 m.
MATS.update({
    "M_Skin":       ((0.86, 0.66, 0.54, 1), 0.0, 0.45, 0),
    "M_Scrubs":     ((0.10, 0.48, 0.55, 1), 0.0, 0.65, 0),
    "M_ScrubsDark": ((0.07, 0.33, 0.40, 1), 0.0, 0.70, 0),
    "M_Hair":       ((0.12, 0.08, 0.06, 1), 0.0, 0.55, 0),
    "M_Eye":        ((0.03, 0.03, 0.04, 1), 0.0, 0.90, 0),
    "M_Shoe":       ((0.92, 0.93, 0.95, 1), 0.0, 0.40, 0),
    "M_EyeWhite":   ((0.97, 0.97, 0.97, 1), 0.0, 0.85, 0),
    "M_Mouth":      ((0.45, 0.08, 0.10, 1), 0.0, 0.60, 0),
    "M_Teeth":      ((0.98, 0.98, 0.96, 1), 0.0, 0.70, 0),
    "M_Blush":      ((0.95, 0.55, 0.52, 1), 0.0, 0.40, 0),
})
clear_scene()

J = {
    "pelvis": (0, 0.0, 0.98), "spine": (0, 0.012, 1.14), "chest": (0, 0.0, 1.31), "neck": (0, 0.012, 1.485),
    "head": (0, -0.005, 1.615), "top": (0, 0.0, 1.735),
}
for s, sx in (("L", 1), ("R", -1)):
    J["hip" + s] = (0.092 * sx, 0.0, 0.94)
    J["knee" + s] = (0.098 * sx, -0.012, 0.52)
    J["ankle" + s] = (0.100 * sx, 0.018, 0.09)
    J["toe" + s] = (0.104 * sx, -0.125, 0.035)
    J["clav" + s] = (0.06 * sx, 0.01, 1.415)
    J["shoulder" + s] = (0.178 * sx, 0.012, 1.405)
    J["elbow" + s] = (0.235 * sx, 0.03, 1.155)
    J["wrist" + s] = (0.262 * sx, 0.0, 0.905)
    J["hand" + s] = (0.272 * sx, -0.012, 0.80)

R = {  # skin radii (x, y) - the subdivision surface shrinks the hull by ~25 %
    "pelvis": (0.195, 0.140), "spine": (0.172, 0.125), "chest": (0.215, 0.140), "neck": (0.066, 0.068),
}
for s in ("L", "R"):
    R.update({"hip" + s: (0.125, 0.125), "knee" + s: (0.080, 0.080), "ankle" + s: (0.054, 0.056), "toe" + s: (0.056, 0.034),
              "clav" + s: (0.070, 0.066), "shoulder" + s: (0.066, 0.064), "elbow" + s: (0.056, 0.056), "wrist" + s: (0.043, 0.034),
              "hand" + s: (0.058, 0.024)})

E = [("pelvis", "spine"), ("spine", "chest"), ("chest", "neck")]
for s in ("L", "R"):
    E += [("pelvis", "hip" + s), ("hip" + s, "knee" + s), ("knee" + s, "ankle" + s), ("ankle" + s, "toe" + s),
          ("chest", "clav" + s), ("clav" + s, "shoulder" + s), ("shoulder" + s, "elbow" + s), ("elbow" + s, "wrist" + s), ("wrist" + s, "hand" + s)]

names = [n for n in J.keys() if n not in ("head", "top")]
me = bpy.data.meshes.new("WorkerBody")
me.from_pydata([J[n] for n in names], [(names.index(a), names.index(b)) for a, b in E], [])
me.update()
body = bpy.data.objects.new("WorkerBody", me)
bpy.context.scene.collection.objects.link(body)
sk = body.modifiers.new("Skin", "SKIN")
sk.use_smooth_shade = True
sk.branch_smoothing = 0.6
for i, n in enumerate(names):
    v = me.skin_vertices[0].data[i]
    v.radius = R[n]
    v.use_root = (n == "pelvis")
sub = body.modifiers.new("Sub", "SUBSURF")
sub.levels = 3; sub.render_levels = 3
apply_modifiers(body)

# materials by region: first cut the mesh along clean planes, then classify faces
bm = bmesh.new(); bm.from_mesh(body.data)
def cut(co, no):
    geom = bm.verts[:] + bm.edges[:] + bm.faces[:]
    bmesh.ops.bisect_plane(bm, geom=geom, dist=1e-5, plane_co=Vector(co), plane_no=Vector(no))
cut((0, 0, 0.99), (0, 0, 1))      # waist
cut((0, 0, 0.115), (0, 0, 1))     # shoes
cut((0, 0, 1.47), (0, 0, 1))      # collar
sleeve = {}
for s_, sx in (("L", 1), ("R", -1)):
    a = Vector(J["shoulder" + s_]); b = Vector(J["elbow" + s_])
    co = a + (b - a) * 0.55; no = (b - a).normalized()
    sleeve[s_] = (co, no)
    # only cut the arm: temporarily restrict by deleting nothing - plane cut is global but harmless
    cut(co, no)
bm.to_mesh(body.data); bm.free()
for mn in ("M_Scrubs", "M_ScrubsDark", "M_Skin", "M_Shoe"):
    body.data.materials.append(mat(mn))
for p in body.data.polygons:
    c = p.center
    ax = abs(c.x)
    idx = 0                                         # scrub top
    if c.z < 0.99: idx = 1                          # trousers
    if c.z > 1.47 and ax < 0.11: idx = 2            # neck
    side = "L" if c.x > 0 else "R"
    best = None
    for a_, b_ in (("shoulder", "elbow"), ("elbow", "hand")):   # distance to the bent arm (two segments)
        sa, sb = Vector(J[a_ + side]), Vector(J[b_ + side])
        seg = sb - sa
        t = max(0.0, min(1.0, (c - sa).dot(seg) / seg.length_squared))
        q = sa + seg * t
        if best is None or (c - q).length < (c - best).length: best = q
    pt = best
    if (c - pt).length < 0.075 and (c.z < J["shoulder" + side][2] - 0.10 or abs(c.x) > abs(pt.x) - 0.03):   # arm: skin beyond the short sleeve
        co, no = sleeve[side]
        if (c - co).dot(no) > 0: idx = 2
    if c.z < 0.115: idx = 3                         # shoes
    p.material_index = idx
    p.use_smooth = True
body["smooth_angle"] = 80

# details: hair, eyes, nose, ears, badge, collar
def ell(name, c, r, m, rot=(0, 0, 0)):
    bm = bmesh.new()
    res = bmesh.ops.create_uvsphere(bm, u_segments=24, v_segments=14, radius=1.0)
    bmesh.ops.scale(bm, vec=Vector(r), verts=bm.verts)
    bmesh.ops.rotate(bm, verts=bm.verts, cent=(0, 0, 0), matrix=Matrix.Rotation(rot[0], 3, "X") @ Matrix.Rotation(rot[2], 3, "Z"))
    bmesh.ops.translate(bm, vec=Vector(c), verts=bm.verts)
    o = obj_from_bm(name, bm, m, origin=c, smooth_angle=80)
    return o

def egg_head():
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=40, v_segments=28, radius=1.0)
    for v in bm.verts:
        x, y, z = v.co
        low = max(0.0, -z)                      # 0 at the equator, 1 at the chin
        sx = 0.082 * (1 - 0.22 * low ** 1.5)
        sy = 0.098 * (1 - 0.10 * low ** 2)
        v.co = Vector((x * sx, y * sy - 0.012 * low ** 2 * (1 if y < 0 else 0.3), z * 0.115))
    bmesh.ops.translate(bm, vec=(0, 0, 1.600), verts=bm.verts)
    return obj_from_bm("HeadMesh", bm, "M_Skin", origin=(0, 0, 1.60), smooth_angle=80)
head = egg_head()
from mathutils.bvhtree import BVHTree
bpy.context.view_layer.update()
head_bvh = BVHTree.FromObject(head, bpy.context.evaluated_depsgraph_get())
bm = bmesh.new(); bm_cyl(bm, 0.056, 0.11, 24, center=(0, 0.012, 1.49))
neckm = obj_from_bm("NeckMesh", bm, "M_Skin", origin=(0, 0.01, 1.50), smooth_angle=80)
# hair: an egg-shaped cap trimmed along a smooth hairline (high on the forehead, low at the nape)
bm = bmesh.new()
bmesh.ops.create_uvsphere(bm, u_segments=48, v_segments=32, radius=1.0)
for v in bm.verts:
    x, y, z = v.co
    v.co = Vector((x * 0.088, y * 0.104 + 0.004, z * 0.118 + 1.606))
def hairline(x, y):
    phi = math.atan2(abs(x), -y)                    # 0 = front, π = back
    return 1.567 + 0.104 * math.cos(phi / 2) ** 4 + 0.012 * math.cos(phi / 2) ** 2 * math.cos(6 * x / 0.088) * 0.3
# smooth trim: faces entirely below the hairline are removed, the remaining vertices below it are lifted onto it
below = {v: v.co.z < hairline(v.co.x, v.co.y) for v in bm.verts}
dead = [f for f in bm.faces if all(below[v] for v in f.verts)]
bmesh.ops.delete(bm, geom=dead, context="FACES")
for v in bm.verts:
    if v.is_valid and below.get(v, False):
        v.co.z = hairline(v.co.x, v.co.y)
        v.co.x *= 0.985; v.co.y *= 0.985           # tuck the edge onto the scalp
hair = obj_from_bm("Hair", bm, "M_Hair", origin=(0, 0, 1.62), smooth_angle=80)
bm = bmesh.new(); bm.from_mesh(hair.data)
bm.to_mesh(hair.data); bm.free()
# ---- friendly face: eyes with whites + pupils + highlights, raised eyebrows, smile, rosy cheeks
def surf_y(x, z, off=0.0):
    """front surface y of the head at (x, z) (ray cast against the head mesh), pushed outwards by off"""
    loc = head.matrix_world.inverted() @ Vector((x, -1.0, z))
    hit = head_bvh.ray_cast(loc, Vector((0, 1, 0)))
    if hit[0] is None: return -0.09 - off
    return (head.matrix_world @ hit[0]).y - off
face_parts = []
for sx in (-1, 1):
    side = "L" if sx > 0 else "R"
    ex, ez = 0.030 * sx, 1.624
    ey = surf_y(ex, ez)
    face_parts.append(ell("EyeWhite" + side, (ex, ey + 0.002, ez), (0.0125, 0.005, 0.0105), "M_EyeWhite"))
    face_parts.append(ell("Eye" + side, (ex + 0.001 * sx, ey - 0.0022, ez - 0.0005), (0.0068, 0.0028, 0.0074), "M_Eye"))
    face_parts.append(ell("Glint" + side, (ex + 0.0025 * sx, ey - 0.0045, ez + 0.0028), (0.0019, 0.0012, 0.0019), "M_Teeth"))
    face_parts.append(ell("Ear" + side, (0.081 * sx, 0.004, 1.608), (0.011, 0.020, 0.030), "M_Skin"))
    face_parts.append(ell("Cheek" + side, (0.046 * sx, surf_y(0.046 * sx, 1.592) + 0.0025, 1.592), (0.014, 0.004, 0.0095), "M_Blush"))
    # eyebrow: a bevelled curve arching up (friendly, relaxed)
    cu = bpy.data.curves.new("Brow" + side, "CURVE"); cu.dimensions = "3D"
    sp = cu.splines.new("POLY"); pts = []
    for k in range(7):
        t = k / 6
        x = (0.016 + 0.032 * t) * sx
        z = 1.646 + 0.006 * math.sin(math.pi * t) - 0.004 * t
        pts.append((x, surf_y(x, z, 0.002), z))
    sp.points.add(len(pts) - 1)
    for k, pt in enumerate(pts): sp.points[k].co = (pt[0], pt[1], pt[2], 1)
    cu.bevel_depth = 0.0028; cu.bevel_resolution = 2
    bo = bpy.data.objects.new("Brow" + side, cu); bpy.context.scene.collection.objects.link(bo)
    bpy.context.view_layer.update()
    bme = bpy.data.meshes.new_from_object(bo.evaluated_get(bpy.context.evaluated_depsgraph_get()))
    bpy.data.objects.remove(bo, do_unlink=True)
    bme.materials.append(mat("M_Hair"))
    brow = bpy.data.objects.new("Brow" + side, bme); bpy.context.scene.collection.objects.link(brow)
    brow["smooth_angle"] = 80
    face_parts.append(brow)
# open smile: a half-moon mouth wrapped onto the face, with a band of teeth
def face_poly(name, outline, m, off):
    bm = bmesh.new()
    vs = [bm.verts.new((x, surf_y(x, z, off), z)) for x, z in outline]
    bm.faces.new(vs)
    bmesh.ops.triangulate(bm, faces=bm.faces[:], quad_method="BEAUTY", ngon_method="EAR_CLIP")
    # give it a little thickness so it renders from slight side angles
    ext = bmesh.ops.extrude_face_region(bm, geom=bm.faces[:])
    nv = [e for e in ext["geom"] if isinstance(e, bmesh.types.BMVert)]
    bmesh.ops.translate(bm, vec=(0, 0.0015, 0), verts=nv)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    o = obj_from_bm(name, bm, m, origin=(0, surf_y(0, 1.56), 1.56), smooth_angle=80)
    face_parts.append(o)
    return o
mouth = []
W, top, depth = 0.025, 1.566, 0.0125
for k in range(21):                                   # upper lip line, corners turned up
    x = -W + 2 * W * k / 20
    mouth.append((x, top + 0.0045 * (abs(x) / W) ** 2.2))
for k in range(1, 20):                                # lower arc
    a = math.pi * k / 20
    x = W * math.cos(a)
    mouth.append((x, top - depth * math.sin(a) + 0.0035 * (abs(x) / W) ** 2))
face_poly("Mouth", mouth, "M_Mouth", 0.0012)
teeth = []
Wt = W * 0.70
for k in range(15):                                   # upper edge follows the lip line
    x = -Wt + 2 * Wt * k / 14
    teeth.append((x, top - 0.0008 + 0.0045 * (abs(x) / W) ** 2.2))
for k in range(14, -1, -1):                           # lower edge: same curve, 4.5 mm lower, rounded ends
    x = -Wt + 2 * Wt * k / 14
    e = abs(x) / Wt
    teeth.append((x, top - 0.0053 + 0.0045 * (abs(x) / W) ** 2.2 + 0.0035 * e ** 6))
face_poly("Teeth", teeth, "M_Teeth", 0.0019)
ell("Nose", (0, surf_y(0, 1.598) - 0.006, 1.598), (0.0105, 0.013, 0.017), "M_Skin", rot=(0.35, 0, 0))
bm = bmesh.new(); bm_box(bm, (0.04, 0.006, 0.055), (0.08, -0.137, 1.33))
obj_from_bm("Badge", bm, "M_Warning", origin=(0.08, -0.137, 1.33))

# ---- armature
arm_data = bpy.data.armatures.new("WorkerRig")
rig = bpy.data.objects.new("Worker", arm_data)
bpy.context.scene.collection.objects.link(rig)
bpy.context.view_layer.objects.active = rig
rig.select_set(True)
bpy.ops.object.mode_set(mode="EDIT")
eb = arm_data.edit_bones
def bone(name, a, b, parent=None, connect=False):
    bn = eb.new(name)
    bn.head = Vector(J[a]) if isinstance(a, str) else Vector(a)
    bn.tail = Vector(J[b]) if isinstance(b, str) else Vector(b)
    if parent: bn.parent = eb[parent]; bn.use_connect = connect
    return bn
bone("Hips", (0, 0, 0.90), "spine")
bone("Spine", "spine", "chest", "Hips", True)
bone("Chest", "chest", "neck", "Spine", True)
bone("Neck", "neck", "head", "Chest", True)
bone("Head", "head", (0, 0, 1.80), "Neck", True)
for s in ("L", "R"):
    bone("UpperLeg." + s, "hip" + s, "knee" + s, "Hips")
    bone("LowerLeg." + s, "knee" + s, "ankle" + s, "UpperLeg." + s, True)
    bone("Foot." + s, "ankle" + s, "toe" + s, "LowerLeg." + s, True)
    bone("Shoulder." + s, "clav" + s, "shoulder" + s, "Chest")
    bone("UpperArm." + s, "shoulder" + s, "elbow" + s, "Shoulder." + s, True)
    bone("LowerArm." + s, "elbow" + s, "wrist" + s, "UpperArm." + s, True)
    bone("Hand." + s, "wrist" + s, "hand" + s, "LowerArm." + s, True)
bpy.ops.object.mode_set(mode="OBJECT")

# bind body with automatic (bone heat) weights
bpy.ops.object.select_all(action="DESELECT")
body.select_set(True); rig.select_set(True)
bpy.context.view_layer.objects.active = rig
bpy.ops.object.parent_set(type="ARMATURE_AUTO")

# rigid attachments: head details follow the head bone, badge the chest
def attach(obj, bone_name):
    bpy.context.view_layer.update()
    mw = obj.matrix_world.copy()
    obj.parent = rig
    obj.parent_type = "BONE"
    obj.parent_bone = bone_name
    bpy.context.view_layer.update()
    obj.matrix_world = mw
for o in [bpy.data.objects[n] for n in ("HeadMesh", "Hair", "Nose")] + face_parts:
    attach(o, "Head")
attach(bpy.data.objects["NeckMesh"], "Neck")
attach(bpy.data.objects["Badge"], "Chest")
# grip point for the survey meter
grip = bpy.data.objects.new("Grip.R", None)
bpy.context.scene.collection.objects.link(grip)
grip.location = Vector(J["handR"]) + Vector((0, -0.02, 0.02))
attach(grip, "Hand.R")

objs = [rig, body] + [bpy.data.objects[n] for n in ("NeckMesh", "HeadMesh", "Hair", "Nose", "Badge")] + face_parts
for o in objs:
    if o.type == "MESH" and o is not body:
        apply_modifiers(o); shade(o)
for o in objs:
    if o.type == "MESH": auto_uv(o)

import os
path = os.path.join(EXPORT_DIR, "Worker.fbx")
bpy.ops.object.select_all(action="DESELECT")
for o in objs + [grip]:
    o.select_set(True)
bpy.context.view_layer.objects.active = rig
bpy.ops.export_scene.fbx(
    filepath=path, use_selection=True, object_types={"ARMATURE", "MESH", "EMPTY"},
    apply_unit_scale=True, apply_scale_options="FBX_SCALE_ALL",
    axis_forward="-Z", axis_up="Y", bake_space_transform=False,
    mesh_smooth_type="OFF", use_mesh_modifiers=True, add_leaf_bones=False,
    primary_bone_axis="Y", secondary_bone_axis="X", armature_nodetype="NULL",
    bake_anim=False, use_armature_deform_only=True)
body.data.calc_loop_triangles()
result = {"file": path, "tris": len(body.data.loop_triangles), "bones": len(arm_data.bones)}
