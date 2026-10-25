"""Translation file tool for Assets/Resources/Lang.

  python i18n.py merge <code> <file>   merge "N|translation" lines (N = line number in _keys.txt, 1-based)
  python i18n.py status                coverage of every language
  python i18n.py missing <code> [area] list key line numbers without a translation (area: ui / learn)

Language files hold "hhhhhhhh<TAB>text" lines; hhhhhhhh is the FNV-1a hash of the (number-masked) English key, so
re-extracting keys never misaligns existing translations. Checks on merge: '#' placeholders, <b>/<i>/<color> tags,
\\n escapes and the key's leading/trailing spaces are kept.
"""
import io, os, re, sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
LANG = os.path.join(ROOT, "Assets", "Resources", "Lang")
REVIEW = os.path.join(ROOT, "Bridge", "i18n_keys_review.txt")
CODES = ["de", "fr", "es", "pt", "it", "nl", "pl", "cs", "ro", "hu", "sv", "da", "nb", "fi", "el", "ru", "uk", "az",
         "id", "vi", "ar", "fa", "hi", "bn", "th", "zh", "ja", "ko"]
TAG = re.compile(r"</?(?:b|i|color(?:=[^>]*)?|size(?:=[^>]*)?)>")


def unescape(s):
    out, i = [], 0
    while i < len(s):
        if s[i] == "\\" and i + 1 < len(s):
            e = s[i + 1]; out.append({"n": "\n", "t": "\t"}.get(e, e)); i += 2
        else:
            out.append(s[i]); i += 1
    return "".join(out)


def escape(s): return s.replace("\\", "\\\\").replace("\n", "\\n").replace("\t", "\\t")


def fnv(s):
    h = 2166136261
    for b in s.encode("utf-8"):
        h = ((h ^ b) * 16777619) & 0xFFFFFFFF
    return h


def keys():
    return [l.rstrip("\n") for l in io.open(os.path.join(LANG, "_keys.txt"), encoding="utf-8")]


def areas():
    a = {}
    for l in io.open(REVIEW, encoding="utf-8"):
        f = l.rstrip("\n").split("\t")
        a[int(f[0]) + 1] = f[1]
    return a


def load(code):
    d = {}
    p = os.path.join(LANG, code + ".txt")
    if os.path.exists(p):
        for l in io.open(p, encoding="utf-8"):
            l = l.rstrip("\n")
            if len(l) > 9 and l[8] == "\t": d[l[:8]] = l[9:]
    return d


def save(code, d):
    with io.open(os.path.join(LANG, code + ".txt"), "w", encoding="utf-8", newline="\n") as f:
        for h in sorted(d): f.write(h + "\t" + d[h] + "\n")


def check(key, tr):
    """Returns (fixed translation, problem or None)."""
    k = unescape(key)
    t = unescape(tr.strip())
    # spaces at the ends belong to the surrounding code ("Score: " + n)
    lead = k[:len(k) - len(k.lstrip(" "))]; trail = k[len(k.rstrip(" ")):]
    t = lead + t.strip(" ") + trail
    if k.count("#") != t.count("#"): return t, "placeholders %d→%d" % (k.count("#"), t.count("#"))
    if sorted(TAG.findall(k)) != sorted(TAG.findall(t)): return t, "tags"
    if k.count("\n") != t.count("\n"): return t, "newlines"
    return t, None


def merge(code, path):
    ks = keys(); d = load(code); bad = 0; n = 0
    for raw in io.open(path, encoding="utf-8"):
        raw = raw.rstrip("\n")
        if not raw.strip(): continue
        m = re.match(r"^(\d+)\|(.*)$", raw)
        if not m: print("unparsed:", raw[:80]); bad += 1; continue
        idx = int(m.group(1))
        if not 1 <= idx <= len(ks): print("bad index", idx); bad += 1; continue
        t, prob = check(ks[idx - 1], m.group(2))
        if prob:
            print(f"{code} {idx}: {prob} | {ks[idx-1][:70]} | {m.group(2)[:70]}"); bad += 1; continue
        d["%08x" % fnv(unescape(ks[idx - 1]))] = escape(t); n += 1
    save(code, d)
    print(f"{code}: merged {n}, rejected {bad}, total {len(d)}/{len(ks)}")


def status():
    ks = keys(); ar = areas()
    hs = {("%08x" % fnv(unescape(k))): i + 1 for i, k in enumerate(ks)}
    for code in CODES:
        d = load(code)
        ui = sum(1 for h in d if h in hs and ar.get(hs[h]) == "ui"); le = sum(1 for h in d if h in hs and ar.get(hs[h]) == "learn")
        tot_ui = sum(1 for v in ar.values() if v == "ui"); tot_le = len(ks) - tot_ui
        print(f"{code}: ui {ui}/{tot_ui}  learn {le}/{tot_le}")


def missing(code, area=None):
    ks = keys(); ar = areas(); d = load(code)
    out = [i + 1 for i, k in enumerate(ks) if ("%08x" % fnv(unescape(k))) not in d and (area is None or ar.get(i + 1) == area)]
    print(len(out), out[:400])


if __name__ == "__main__":
    cmd = sys.argv[1]
    if cmd == "merge": merge(sys.argv[2], sys.argv[3])
    elif cmd == "status": status()
    elif cmd == "missing": missing(sys.argv[2], sys.argv[3] if len(sys.argv) > 3 else None)
