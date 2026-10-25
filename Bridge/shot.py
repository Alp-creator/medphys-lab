"""Run the Windows build, open a module, optionally trigger an action, save a screenshot and quit.
usage: python shot.py <module> <name> [action] [delay] [w] [h]
"""
import os, subprocess, sys, time

ROOT = os.path.dirname(os.path.abspath(__file__))
EXE = os.path.join(os.path.dirname(ROOT), "Builds", "Windows", "MedPhysLab.exe")
OUT = os.path.join(ROOT, "shots")
os.makedirs(OUT, exist_ok=True)

module = sys.argv[1]
name = sys.argv[2]
action = sys.argv[3] if len(sys.argv) > 3 and sys.argv[3] != "-" else ""
delay = sys.argv[4] if len(sys.argv) > 4 else "6"
w = sys.argv[5] if len(sys.argv) > 5 else "1600"
h = sys.argv[6] if len(sys.argv) > 6 else "900"
shot = os.path.join(OUT, name + ".png")
if os.path.exists(shot):
    os.remove(shot)
log = os.path.join(OUT, name + ".log")
args = [EXE, "-mpModule", module, "-mpShot", shot, "-mpDelay", delay, "-mpQuit", "1",
        "-screen-fullscreen", "0", "-screen-width", w, "-screen-height", h, "-logFile", log, "-mpW", w, "-mpH", h]
if os.environ.get("MPBATCH"):
    args += ["-batchmode"]
if action:
    args += ["-mpAction", action]
args += [a for a in os.environ.get("MPFLAGS", "").split() if a]
t0 = time.time()
p = subprocess.Popen(args)
try:
    p.wait(timeout=float(delay) + 22)
except subprocess.TimeoutExpired:
    p.kill()
print("exit", p.returncode, "time %.1fs" % (time.time() - t0), "shot exists:", os.path.exists(shot))
with open(log, encoding="utf-8", errors="replace") as f:
    lines = f.read().splitlines()
for ln in lines:
    if any(k in ln for k in ("Exception", "Error", "error", "[MP]", "NullReference", "missing")):
        print(ln)
