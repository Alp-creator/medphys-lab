"""Run Unity in batch mode on this project and print a filtered log.
usage: python batch.py <Namespace.Class.Method> [--nographics] [--keep] [--timeout S] [-- extra unity args]
"""
import os, re, subprocess, sys, time

UNITY = r"C:\Program Files\Unity\Hub\Editor\6000.6.2f1\Editor\Unity.exe"
ROOT = os.path.dirname(os.path.abspath(__file__))
PROJECT = os.path.dirname(ROOT)

def main():
    args = sys.argv[1:]
    extra = []
    if "--" in args:
        i = args.index("--"); extra = args[i + 1:]; args = args[:i]
    method = args[0] if args and not args[0].startswith("--") else None
    timeout = 3600
    if "--timeout" in args:
        timeout = int(args[args.index("--timeout") + 1])
    log = os.path.join(ROOT, "batch.log")
    cmd = [UNITY, "-batchmode", "-projectPath", PROJECT, "-logFile", log]
    if "--nographics" in args:
        cmd.append("-nographics")
    if method:
        cmd += ["-executeMethod", method]
    if "--keep" not in args:
        cmd.append("-quit")
    cmd += extra
    t0 = time.time()
    try:
        r = subprocess.run(cmd, timeout=timeout)
        code = r.returncode
    except subprocess.TimeoutExpired:
        code = "TIMEOUT"
    dt = time.time() - t0
    with open(log, encoding="utf-8", errors="replace") as f:
        lines = f.read().splitlines()
    keep = []
    pat = re.compile(r"(error|exception|warning CS|\[MP\]|Build|Aborting|failed|Scripts have compiler errors)", re.I)
    skip = re.compile(r"(Curl error|Licensing|UnityConnect|ProfilerConnection|Package Manager|Analytics|DisplayProgressbar|com\.unity\.)", re.I)
    for ln in lines:
        if pat.search(ln) and not skip.search(ln):
            keep.append(ln)
    seen = set(); out = []
    for k in keep:
        if ("warning" in k and "error" not in k.lower()) or k in seen:
            continue
        seen.add(k); out.append(k)
    print("\n".join(out[-150:]))
    print(f"--- exit={code} time={dt:.0f}s log={log}")

if __name__ == "__main__":
    main()
