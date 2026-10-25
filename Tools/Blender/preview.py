# Render a quick preview of the current scene (appended after common.py by run_preview()).
import os
PREVIEW_DIR = os.environ.get("MEDPHYS_PREVIEW_DIR") or os.path.join(MEDPHYS_ROOT, "Tools", "Blender", "previews")
os.makedirs(PREVIEW_DIR, exist_ok=True)

def look_at(obj, target):
    d = Vector(target) - obj.location
    obj.rotation_euler = d.to_track_quat("-Z", "Y").to_euler()

def render_preview(name, cam_loc, target, lens=35, res=(1100, 700)):
    sc = bpy.context.scene
    for o in list(bpy.data.objects):
        if o.name.startswith("PV_"):
            bpy.data.objects.remove(o, do_unlink=True)
    cam_data = bpy.data.cameras.new("PV_Cam"); cam_data.lens = lens
    cam = bpy.data.objects.new("PV_Cam", cam_data); sc.collection.objects.link(cam)
    cam.location = Vector(cam_loc); look_at(cam, target)
    sc.camera = cam
    for i, (loc, energy) in enumerate([((3, -4, 5), 900), ((-4, -2, 3), 400), ((0, 4, 4), 500)]):
        ld = bpy.data.lights.new("PV_L%d" % i, "AREA"); ld.energy = energy; ld.size = 3
        lo = bpy.data.objects.new("PV_L%d" % i, ld); sc.collection.objects.link(lo)
        lo.location = Vector(loc); look_at(lo, target)
    # floor
    if "PV_Floor" not in bpy.data.objects:
        bm = bmesh.new(); bm_box(bm, (12, 12, 0.02), (0, 0, -0.011))
        f = obj_from_bm("PV_Floor", bm, "M_Floor")
    world = sc.world or bpy.data.worlds.new("World")
    sc.world = world
    try:
        world.use_nodes = True
        bg = world.node_tree.nodes.get("Background")
        bg.inputs[0].default_value = (0.05, 0.06, 0.08, 1); bg.inputs[1].default_value = 1.0
    except Exception:
        pass
    sc.render.engine = "BLENDER_EEVEE"
    sc.render.resolution_x, sc.render.resolution_y = res
    sc.render.resolution_percentage = 100
    sc.render.film_transparent = False
    try:
        sc.view_settings.view_transform = "AgX"
    except Exception:
        pass
    path = os.path.join(PREVIEW_DIR, name + ".png")
    sc.render.filepath = path
    bpy.ops.render.render(write_still=True)
    for o in list(bpy.data.objects):
        if o.name.startswith("PV_"):
            bpy.data.objects.remove(o, do_unlink=True)
    return path
