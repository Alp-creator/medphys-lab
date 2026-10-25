"""Run a model script in a headless Blender:
   blender -b --factory-startup --python run_bg.py -- patient.py [--preview name cx,cy,cz tx,ty,tz lens]
FBX files are written to Assets/Resources/Models of this repository (override with MEDPHYS_MODELS_DIR).
"""
import os, sys, json, traceback
HERE = os.path.dirname(os.path.abspath(__file__))
argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
script = argv[0]
g = {"__name__": "__bg__", "MEDPHYS_ROOT": os.path.dirname(os.path.dirname(HERE))}
src = open(os.path.join(HERE, "common.py"), encoding="utf-8").read() + "\n\n" + \
      open(os.path.join(HERE, "preview.py"), encoding="utf-8").read() + "\n\n" + \
      open(os.path.join(HERE, script), encoding="utf-8").read()
try:
    exec(compile(src, script, "exec"), g)
    res = g.get("result")
    if "--preview" in argv:
        i = argv.index("--preview")
        name, cam, tgt = argv[i + 1], argv[i + 2], argv[i + 3]
        lens = float(argv[i + 4]) if len(argv) > i + 4 else 35
        cam_t = tuple(float(x) for x in cam.split(","))
        tgt_t = tuple(float(x) for x in tgt.split(","))
        res = dict(res or {}, preview=g["render_preview"](name, cam_t, tgt_t, lens=lens))
    print("RESULT " + json.dumps(res))
except Exception:
    traceback.print_exc()
    print("RESULT FAILED")
