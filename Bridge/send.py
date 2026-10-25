"""Send a command to the Unity editor bridge and wait for its reply.
usage: python send.py <cmd> [arg] [arg2] [--timeout N]
"""
import json, os, sys, time, uuid

ROOT = os.path.dirname(os.path.abspath(__file__))

def send(cmd, arg="", arg2="", timeout=60.0):
    cid = time.strftime("%H%M%S") + "_" + uuid.uuid4().hex[:6]
    tmp = os.path.join(ROOT, "inbox", cid + ".tmp")
    with open(tmp, "w", encoding="utf-8") as f:
        json.dump({"id": cid, "cmd": cmd, "arg": arg, "arg2": arg2}, f)
    os.replace(tmp, os.path.join(ROOT, "inbox", cid + ".json"))
    out = os.path.join(ROOT, "outbox", cid + ".txt")
    t0 = time.time()
    while time.time() - t0 < timeout:
        if os.path.exists(out):
            time.sleep(0.05)
            with open(out, encoding="utf-8") as f:
                r = f.read()
            os.remove(out)
            return r
        time.sleep(0.1)
    return "TIMEOUT (bridge not responding)"

if __name__ == "__main__":
    args = sys.argv[1:]
    timeout = 60.0
    if "--timeout" in args:
        i = args.index("--timeout"); timeout = float(args[i + 1]); del args[i:i + 2]
    print(send(args[0], args[1] if len(args) > 1 else "", args[2] if len(args) > 2 else "", timeout))
