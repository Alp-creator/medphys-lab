# Shared modelling helpers for the MedPhys Lab equipment models.
# Executed inside a headless Blender by run_bg.py before each model script.
import bpy, bmesh, math, os
from mathutils import Vector, Matrix

# MEDPHYS_ROOT is set by run_bg.py (repository root); MEDPHYS_MODELS_DIR overrides the export folder.
EXPORT_DIR = os.environ.get("MEDPHYS_MODELS_DIR") or os.path.join(MEDPHYS_ROOT, "Assets", "Resources", "Models")

# name -> (base colour RGBA, metallic, roughness, emission strength)
MATS = {
    "M_Shell":     ((0.92, 0.93, 0.95, 1), 0.0, 0.25, 0),
    "M_ShellGrey": ((0.60, 0.63, 0.68, 1), 0.0, 0.35, 0),
    "M_Dark":      ((0.07, 0.08, 0.10, 1), 0.0, 0.45, 0),
    "M_Accent":    ((0.10, 0.80, 1.00, 1), 0.0, 0.20, 4),
    "M_Metal":     ((0.70, 0.72, 0.75, 1), 1.0, 0.30, 0),
    "M_Glass":     ((0.60, 0.80, 0.90, 0.25), 0.0, 0.05, 0),
    "M_Pad":       ((0.10, 0.18, 0.30, 1), 0.0, 0.80, 0),
    "M_Screen":    ((0.02, 0.05, 0.08, 1), 0.0, 0.10, 0),
    "M_Lead":      ((0.35, 0.36, 0.38, 1), 0.6, 0.55, 0),
    "M_Warning":   ((1.00, 0.78, 0.05, 1), 0.0, 0.40, 0),
    "M_Floor":     ((0.30, 0.33, 0.37, 1), 0.0, 0.35, 0),
    "M_Wall":      ((0.80, 0.82, 0.85, 1), 0.0, 0.60, 0),
    "M_Light":     ((1.00, 0.98, 0.94, 1), 0.0, 0.50, 6),
}


def mat(name):
    m = bpy.data.materials.get(name)
    if m is None:
        m = bpy.data.materials.new(name)
        col, metal, rough, emit = MATS[name]
        m.diffuse_color = col
        m.metallic = metal
        m.roughness = rough
        try:
            m.use_nodes = True
            bsdf = m.node_tree.nodes.get("Principled BSDF")
            if bsdf:
                bsdf.inputs["Base Color"].default_value = col
                bsdf.inputs["Metallic"].default_value = metal
                bsdf.inputs["Roughness"].default_value = rough
                if col[3] < 1:
                    bsdf.inputs["Alpha"].default_value = col[3]
                if emit:
                    bsdf.inputs["Emission Color"].default_value = col
                    bsdf.inputs["Emission Strength"].default_value = emit
        except Exception:
            pass
    return m


def clear_scene():
    for o in list(bpy.data.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    for coll in (bpy.data.meshes, bpy.data.curves, bpy.data.materials):
        for d in list(coll):
            if d.users == 0:
                coll.remove(d)


# ---------------------------------------------------------------- bmesh builders
def bm_box(bm, size, center=(0, 0, 0)):
    r = bmesh.ops.create_cube(bm, size=1.0)
    vs = r["verts"]
    bmesh.ops.scale(bm, vec=Vector(size), verts=vs)
    bmesh.ops.translate(bm, vec=Vector(center), verts=vs)
    return vs


def bm_cyl(bm, r, depth, segs=48, center=(0, 0, 0), axis="Z", r2=None):
    res = bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=segs,
                                radius1=r, radius2=(r if r2 is None else r2), depth=depth)
    vs = res["verts"]
    if axis == "X":
        bmesh.ops.rotate(bm, verts=vs, cent=(0, 0, 0), matrix=Matrix.Rotation(math.pi / 2, 3, "Y"))
    elif axis == "Y":
        bmesh.ops.rotate(bm, verts=vs, cent=(0, 0, 0), matrix=Matrix.Rotation(math.pi / 2, 3, "X"))
    bmesh.ops.translate(bm, vec=Vector(center), verts=vs)
    return vs


def bm_lathe(bm, profile, segs=64, axis="Z", center=(0, 0, 0), angle=2 * math.pi):
    """Revolve a closed or open (r, h) profile around the given axis."""
    verts = [bm.verts.new((r, 0.0, h)) for (r, h) in profile]
    edges = [bm.edges.new((verts[i], verts[i + 1])) for i in range(len(verts) - 1)]
    full = abs(angle - 2 * math.pi) < 1e-6
    res = bmesh.ops.spin(bm, geom=verts + edges, cent=(0, 0, 0), axis=(0, 0, 1),
                         angle=angle, steps=segs, use_merge=full, use_duplicate=False)
    allv = list(set(verts) | {e for e in res["geom_last"] if isinstance(e, bmesh.types.BMVert)})
    allv = [v for v in bm.verts if v.is_valid]
    return allv


def superellipse(a, b, n, count, phase=0.0):
    pts = []
    for i in range(count):
        t = phase + 2 * math.pi * i / count
        c, s = math.cos(t), math.sin(t)
        x = a * math.copysign(abs(c) ** (2.0 / n), c)
        y = b * math.copysign(abs(s) ** (2.0 / n), s)
        pts.append((x, y))
    return pts


def circle(r, count, phase=0.0):
    return [(r * math.cos(phase + 2 * math.pi * i / count), r * math.sin(phase + 2 * math.pi * i / count)) for i in range(count)]


def bm_ring_extrude(bm, outer, inner, y0, y1, zc=0.0):
    """Extrude a 2D ring (outer & inner loops in the XZ plane, same count) along Y from y0 to y1."""
    n = len(outer)
    def v(p, y): return bm.verts.new((p[0], y, p[1] + zc))
    of = [v(p, y0) for p in outer]; ob = [v(p, y1) for p in outer]
    inf = [v(p, y0) for p in inner]; inb = [v(p, y1) for p in inner]
    for i in range(n):
        j = (i + 1) % n
        bm.faces.new((of[i], of[j], inf[j], inf[i]))       # front face (y0)
        bm.faces.new((ob[j], ob[i], inb[i], inb[j]))       # back face (y1)
        bm.faces.new((of[j], of[i], ob[i], ob[j]))         # outer wall
        bm.faces.new((inf[i], inf[j], inb[j], inb[i]))     # inner wall
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)


def bm_profile_extrude(bm, pts, y0, y1, zc=0.0):
    """Extrude a closed convex-ish 2D polygon (XZ plane) along Y."""
    f = [bm.verts.new((p[0], y0, p[1] + zc)) for p in pts]
    b = [bm.verts.new((p[0], y1, p[1] + zc)) for p in pts]
    n = len(pts)
    bm.faces.new(f)
    bm.faces.new(list(reversed(b)))
    for i in range(n):
        j = (i + 1) % n
        bm.faces.new((f[j], f[i], b[i], b[j]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)


# ---------------------------------------------------------------- objects
def obj_from_bm(name, bm, material, origin=(0, 0, 0), parent=None, smooth_angle=40):
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    o = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(o)
    if isinstance(material, (list, tuple)):
        for mn in material:
            me.materials.append(mat(mn))
    else:
        me.materials.append(mat(material))
    set_origin(o, origin)
    if parent is not None:
        set_parent(o, parent)
    o["smooth_angle"] = smooth_angle
    return o


def set_origin(o, origin):
    origin = Vector(origin)
    o.data.transform(Matrix.Translation(-origin))
    o.location = origin


def set_parent(child, parent):
    bpy.context.view_layer.update()
    mw = child.matrix_world.copy()
    child.parent = parent
    child.matrix_parent_inverse = parent.matrix_world.inverted()
    child.matrix_world = mw


def empty(name, loc=(0, 0, 0), parent=None):
    e = bpy.data.objects.new(name, None)
    e.empty_display_size = 0.1
    bpy.context.scene.collection.objects.link(e)
    e.location = Vector(loc)
    if parent is not None:
        set_parent(e, parent)
    return e


def bevel(o, width, segs=3, angle=35, clamp=True):
    m = o.modifiers.new("Bevel", "BEVEL")
    m.width = width
    m.segments = segs
    m.limit_method = "ANGLE"
    m.angle_limit = math.radians(angle)
    m.use_clamp_overlap = clamp
    return m


def boolean(o, cutter, op="DIFFERENCE"):
    m = o.modifiers.new("Bool", "BOOLEAN")
    m.operation = op
    m.object = cutter
    try:
        m.solver = "EXACT"
    except Exception:
        pass
    cutter.hide_viewport = True
    cutter.hide_render = True
    return m


def apply_modifiers(o):
    if not o.modifiers:
        return
    dg = bpy.context.evaluated_depsgraph_get()
    ev = o.evaluated_get(dg)
    me = bpy.data.meshes.new_from_object(ev)
    old = o.data
    o.modifiers.clear()
    o.data = me
    me.name = o.name
    if old.users == 0:
        bpy.data.meshes.remove(old)


def shade(o, angle_deg=None):
    if o.type != "MESH":
        return
    if angle_deg is None:
        angle_deg = o.get("smooth_angle", 40)
    bm = bmesh.new()
    bm.from_mesh(o.data)
    lim = math.radians(angle_deg)
    for f in bm.faces:
        f.smooth = True
    for e in bm.edges:
        if len(e.link_faces) != 2 or e.calc_face_angle(0) > lim:
            e.smooth = False
        else:
            e.smooth = True
    bm.to_mesh(o.data)
    bm.free()


def triangulate_ngons(o):
    bm = bmesh.new()
    bm.from_mesh(o.data)
    ng = [f for f in bm.faces if len(f.verts) > 4]
    if ng:
        bmesh.ops.triangulate(bm, faces=ng, quad_method="BEAUTY", ngon_method="EAR_CLIP")
    bm.to_mesh(o.data)
    bm.free()


def finalize(objs):
    for o in objs:
        if o.type == "MESH":
            apply_modifiers(o)
            triangulate_ngons(o)
    for o in objs:
        if o.type == "MESH":
            shade(o)
            o.data.update()


def model_objects(root):
    out = [root]
    for c in root.children_recursive:
        out.append(c)
    return out


def export_fbx(root, filename):
    import os
    os.makedirs(EXPORT_DIR, exist_ok=True)
    objs = model_objects(root)
    # cutters are hidden helper objects: never export them
    objs = [o for o in objs if not o.hide_render]
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs:
        o.hide_viewport = False
        o.select_set(True)
    bpy.context.view_layer.objects.active = root
    path = os.path.join(EXPORT_DIR, filename)
    bpy.ops.export_scene.fbx(
        filepath=path, use_selection=True, object_types={"MESH", "EMPTY"},
        apply_unit_scale=True, apply_scale_options="FBX_SCALE_ALL",
        axis_forward="-Z", axis_up="Y", bake_space_transform=False,
        mesh_smooth_type="OFF", use_mesh_modifiers=True, add_leaf_bones=False,
        use_tspace=False, path_mode="AUTO", embed_textures=False)
    tris = 0
    for o in objs:
        if o.type == "MESH":
            o.data.calc_loop_triangles()
            tris += len(o.data.loop_triangles)
    return {"file": path, "objects": len(objs), "tris": tris}


def delete_cutters():
    for o in list(bpy.data.objects):
        if o.name.startswith("CUT_"):
            bpy.data.objects.remove(o, do_unlink=True)


def uv_planar(o, axis="Y"):
    """Simple planar UV projection (0..1 over the object's bounds) - used for screen faces."""
    me = o.data
    if not me.uv_layers:
        me.uv_layers.new(name="UVMap")
    uv = me.uv_layers.active.data
    xs = [v.co.x for v in me.vertices]; ys = [v.co.y for v in me.vertices]; zs = [v.co.z for v in me.vertices]
    def nrm(val, lo, hi): return 0.0 if hi - lo < 1e-9 else (val - lo) / (hi - lo)
    for poly in me.polygons:
        for li in poly.loop_indices:
            co = me.vertices[me.loops[li].vertex_index].co
            if axis == "Y":
                u, v = nrm(co.x, min(xs), max(xs)), nrm(co.z, min(zs), max(zs))
            elif axis == "Z":
                u, v = nrm(co.x, min(xs), max(xs)), nrm(co.y, min(ys), max(ys))
            else:
                u, v = nrm(co.y, min(ys), max(ys)), nrm(co.z, min(zs), max(zs))
            uv[li].uv = (u, v)


def auto_uv(o):
    """Cube-projection UVs for everything (needed for tangent-free lighting/detail textures)."""
    me = o.data
    if me.uv_layers:
        return
    me.uv_layers.new(name="UVMap")
    uv = me.uv_layers.active.data
    for poly in me.polygons:
        n = poly.normal
        ax = max(range(3), key=lambda k: abs(n[k]))
        for li in poly.loop_indices:
            co = me.vertices[me.loops[li].vertex_index].co
            if ax == 0:
                uv[li].uv = (co.y, co.z)
            elif ax == 1:
                uv[li].uv = (co.x, co.z)
            else:
                uv[li].uv = (co.x, co.y)
