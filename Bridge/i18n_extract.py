"""Collects every English UI / case / lesson string that reaches Loc.T(tr, en) and writes the translation key list.

Sources
  * C#: pairs of adjacent string literals ("tr", "en") – T(...), SetTip, Part, task tuples, case data, tissue tables …
        and parallel arrays (new[] {tr…}, new[] {en…}); interpolated strings become templates with '#' for numbers.
  * Learning centre: Assets/Resources/Learn/*.txt (the English half of every "tr || en" pair).
Output
  Assets/Resources/Lang/_keys.txt   one key per line (number-masked English, \\n escaped), sorted by area
  Bridge/i18n_keys_review.txt       key + Turkish + source, for translators
usage: python i18n_extract.py
"""
import glob, io, os, re

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ASSETS = os.path.join(ROOT, "Assets")
TURKISH = set("çğıöşüÇĞİÖŞÜ")
NUM = re.compile(r"(?<![A-Za-z_])[-+]?\d+(?:[.,]\d+)?")


def mask(s):
    """Key form shared with Loc.cs: every number becomes '#'."""
    return NUM.sub("#", s)


def literals(src):
    """(start, end, kind, value) of every C# string literal; interpolation holes become '#'."""
    out, i, n = [], 0, len(src)
    while i < n:
        c = src[i]
        if c == "/" and src.startswith("//", i):
            j = src.find("\n", i); i = n if j < 0 else j; continue
        if c == "/" and src.startswith("/*", i):
            j = src.find("*/", i); i = n if j < 0 else j + 2; continue
        if c == "'":
            j = i + 1
            while j < n and src[j] != "'":
                j += 2 if src[j] == "\\" else 1
            i = j + 1; continue
        if c in '$@"':
            k = i; interp = verb = False
            while k < n and src[k] in "$@":
                interp |= src[k] == "$"; verb |= src[k] == "@"; k += 1
            if k >= n or src[k] != '"':
                i += 1; continue
            j = k + 1; buf = []; depth = 0
            while j < n:
                ch = src[j]
                if interp and ch == "{":
                    if src.startswith("{{", j) and depth == 0: buf.append("{"); j += 2; continue
                    depth += 1; j += 1; continue
                if interp and depth > 0:
                    if ch == "}":
                        depth -= 1
                        if depth == 0: buf.append("#")
                    elif ch == '"':
                        # nested literal inside a hole: skip it
                        j += 1
                        while j < n and src[j] != '"':
                            j += 2 if src[j] == "\\" else 1
                    j += 1; continue
                if interp and ch == "}" and src.startswith("}}", j): buf.append("}"); j += 2; continue
                if verb:
                    if ch == '"':
                        if src.startswith('""', j): buf.append('"'); j += 2; continue
                        break
                    buf.append(ch); j += 1; continue
                if ch == "\\":
                    e = src[j + 1]
                    buf.append({"n": "\n", "t": "\t", '"': '"', "\\": "\\", "'": "'", "0": ""}.get(e, e))
                    if e == "u":
                        buf[-1] = chr(int(src[j + 2:j + 6], 16)); j += 6; continue
                    j += 2; continue
                if ch == '"':
                    break
                buf.append(ch); j += 1
            out.append((i, j + 1, "interp" if interp else "plain", "".join(buf)))
            i = j + 1; continue
        i += 1
    return out


def identifier(s):
    t = s.strip()
    if " " in t or not t:
        return " " not in t and not t
    if re.fullmatch(r"[a-z][a-z0-9_\-]*", t): return True          # ids: "linac", "ct-in"
    if re.fullmatch(r"[A-Za-z]+[A-Z][a-z]\w*", t): return True     # CamelCase
    if re.fullmatch(r"[\w]+\.[LR]", t): return True                  # bone names
    if "_" in t or "/" in t and " " not in t and len(t) < 4: return True
    return False


TR_WORDS = {"ve", "bir", "için", "ile", "bu", "da", "de", "değil", "olan", "daha", "çok", "en", "gibi", "ama", "veya", "her", "ise", "kadar", "yok", "var", "mi", "mı"}
EN_WORDS = {"the", "and", "of", "to", "is", "for", "with", "a", "an", "in", "on", "are", "not", "or", "be", "by", "it", "as", "at", "your", "you"}


def turkish(s):
    """True when a string reads as Turkish (Turkish letters / function words outweigh English ones)."""
    words = re.findall(r"[A-Za-zçğıöşüÇĞİÖŞÜ]+", s.lower())
    t = sum(c in TURKISH for c in s) + 2 * sum(w in TR_WORDS for w in words)
    e = 2 * sum(w in EN_WORDS for w in words)
    return t > e and t >= 2


def turkishness(s):
    words = re.findall(r"[A-Za-zçğıöşüÇĞİÖŞÜ]+", s.lower())
    return sum(c in TURKISH for c in s) + 2 * sum(w in TR_WORDS for w in words) - 2 * sum(w in EN_WORDS for w in words)


def natural(tr, en):
    if tr == en or not re.search(r"[A-Za-z]", en): return False
    if turkish(en) and turkishness(en) >= turkishness(tr): return False   # "…harm her?" / "(Dr Kılıç)" stay English
    if tr.lower() == en.lower(): return False
    letters = len(re.findall(r"[A-Za-z]", en))
    if letters < 3: return False
    if " " not in en and en.isupper(): return False
    has_tr = any(c in TURKISH for c in tr)
    if identifier(en) and not has_tr: return False
    if identifier(tr) and not has_tr: return False
    return True


def label(tr, en):
    """Upper-case button / badge words natural() skips as ids: ("ONAYLA", "CONFIRM"), ("TARA  (Scan)", "SCAN"), ("uygun", "OK")."""
    if tr == en or " " in en.strip() or not en.isupper(): return False
    n = len(re.findall(r"[A-Z]", en))
    return n >= 4 or (n >= 2 and tr.islower() and len(tr) >= 4)


def word(tr, en):
    """Short words and units inside T(): ("erkek", "male"), (" sa", " h"), ("BT", "CT"), ("yan", "lat")."""
    return tr != en and en.strip() not in ("en", "") and re.search(r"[A-Za-z]", en) is not None


def call_args(src, open_paren):
    """Top-level argument spans of the call whose '(' is at open_paren, or None if unbalanced."""
    spans, depth, start, i, n = [], 0, open_paren + 1, open_paren, len(src)
    while i < n:
        c = src[i]
        if c == '"' or (c in "$@" and i + 1 < n and src[i + 1] in '"$@'):
            j = i
            while src[j] in "$@": j += 1
            j += 1
            while j < n and src[j] != '"':
                j += 2 if src[j] == "\\" else 1
            i = j + 1; continue
        if c in "([{": depth += 1
        elif c in ")]}":
            depth -= 1
            if depth == 0: spans.append((start, i)); return spans
        elif c == "," and depth == 1: spans.append((start, i)); start = i + 1
        i += 1
    return None


def template(src, lits, a, b):
    """'"x" + n + "y"' → 'x#y' when the argument is literals joined by '+' with plain expressions; else None."""
    inner = [l for l in lits if a <= l[0] and l[1] <= b]
    if not inner: return None
    out, pos, holes = [], a, 0
    for s, e, _, v in inner:
        gap = src[pos:s]
        if gap.count("(") != gap.count(")"): return None
        expr = gap.strip().strip("+").strip()
        if expr:
            if "?" in expr or '"' in expr: return None
            out.append("#"); holes += 1
        out.append(v); pos = e
    tail = src[pos:b].strip().strip("+").strip()
    if tail:
        if "?" in tail: return None
        out.append("#"); holes += 1
    return "".join(out) if holes else None


def concatenations(src, lits):
    """T("tr" + n + "…", "en" + n + "…"): numbers concatenated into the strings become '#' (Loc masks them too)."""
    res = []
    for m in re.finditer(r"\bT\(", src):
        spans = call_args(src, m.end() - 1)
        if not spans or len(spans) != 2: continue
        a, b = (template(src, lits, *spans[0]), template(src, lits, *spans[1]))
        if a and b and natural(a, b): res.append((b, a))
    return res


def scan_top(src, a, b, lits_at):
    """Yield (pos, char) of the characters in src[a:b] that sit outside literals and brackets."""
    depth, i = 0, a
    while i < b:
        if i in lits_at: i = lits_at[i][1]; continue
        c = src[i]
        if c in "([{": depth += 1
        elif c in ")]}": depth -= 1
        elif depth == 0: yield i, c
        i += 1


def alternatives(src, a, b, lits_at, limit=16):
    """Every string an argument expression can produce: literals joined by '+', holes → '#', and
    both branches of '?:' (conditions ignored). None when a part is not literal text or a plain value."""
    while a < b and src[a].isspace(): a += 1
    while b > a and src[b - 1].isspace(): b -= 1
    if a >= b: return None
    if a in lits_at and lits_at[a][1] == b: return [lits_at[a][2]]
    if src[a] == "(" and call_args(src, a) and call_args(src, a)[-1][1] == b - 1:
        return alternatives(src, a + 1, b - 1, lits_at, limit)
    top = list(scan_top(src, a, b, lits_at))
    q = next((p for p, c in top if c == "?" and src[p + 1] not in "?." and src[p - 1] != "?"), None)
    if q is not None:      # cond ? x : y  (nested ternaries in the false branch)
        nest, colon = 0, None
        for p, c in top:
            if p <= q: continue
            if c == "?" and src[p + 1] not in "?." and src[p - 1] != "?": nest += 1
            elif c == ":":
                if nest == 0: colon = p; break
                nest -= 1
        if colon is None: return None
        x, y = alternatives(src, q + 1, colon, lits_at, limit), alternatives(src, colon + 1, b, lits_at, limit)
        return x + y if x is not None and y is not None and len(x) + len(y) <= limit else None
    cuts = [p for p, c in top if c == "+"]
    if cuts:
        out = [""]
        for s, e in zip([a] + [p + 1 for p in cuts], cuts + [b]):
            part = alternatives(src, s, e, lits_at, limit)
            if part is None or len(out) * len(part) > limit: return None
            out = [o + p for o in out for p in part]
        return out
    if re.match(r"\s*T\(", src[a:b]) or any(c in "=<>&|" for _, c in top): return None
    return ["#"]           # a plain value (number formatting, variable …)


def branch_pairs(src, lits):
    """Adjacent call arguments that expand to parallel tr / en alternatives, e.g.
    r.Line("DI " + Pm(di) + (ok ? " → doğru." : " → fazla."), "DI " + Pm(di) + (ok ? " → correct." : " → over."))."""
    lits_at = {s: (s, e, v) for s, e, _, v in lits}
    pairs = []
    for m in re.finditer(r"\b(\w+)\s*\(", src):
        spans = call_args(src, m.end() - 1)
        if not spans or len(spans) < 2: continue
        tr_en_api = m.group(1) in TR_EN_CALLS
        for (s1, e1), (s2, e2) in zip(spans, spans[1:]):
            if tr_en_api or ("?" in src[s1:e1] and "?" in src[s2:e2]): pairs.append((s1, e1, s2, e2))
    # string fbTr = …;  string fbEn = …;
    for m in re.finditer(r"\bstring\s+(\w*)Tr\s*=\s*", src):
        end = statement_end(src, m.end(), lits_at)
        m2 = re.compile(r"\s*string\s+" + m.group(1) + r"En\s*=\s*").match(src, end + 1) if end else None
        if m2: pairs.append((m.end(), end, m2.end(), statement_end(src, m2.end(), lits_at) or m2.end()))
    res = []
    for s1, e1, s2, e2 in pairs:
        x, y = alternatives(src, s1, e1, lits_at), alternatives(src, s2, e2, lits_at)
        if not x or not y or len(x) != len(y): continue
        if len(x) == 1 and s1 in lits_at and s2 in lits_at: continue      # plain literal pair: handled above
        res += [(b, a) for a, b in zip(x, y) if natural(a, b) and all(re.search(r"[^\W\d_]{3}", t) for t in (a, b))]
    return res


TR_EN_CALLS = {"T", "Line", "SetTip", "Q", "Add", "Option", "Slider", "Seg", "Check", "Part", "C"}


def statement_end(src, a, lits_at):
    """Position of the ';' that ends the statement starting at a (outside literals and brackets)."""
    for p, c in scan_top(src, a, len(src), lits_at):
        if c == ";": return p
        if c in ")]}": return None
    return None


def named_pair(src, s1, e1, s2):
    """'TitleTr = "…", TitleEn = "…"' / 'tr = …, en = …' / 'ButtonTr => "…"; … ButtonEn => "…"': fields named as a tr/en pair."""
    m1 = re.search(r"(\w*)(Tr|tr)\s*=>?\s*$", src[max(0, s1 - 60):s1])
    if not m1: return False
    en = m1.group(1) + ("En" if m1.group(2) == "Tr" else "en")
    sep = r"\s*,\s*" + en + r"\s*=\s*" if "=>" not in m1.group(0) else r"\s*;\s*(?:public\s+)?(?:override\s+)?string\s+" + en + r"\s*=>\s*"
    return re.fullmatch(sep, src[e1:s2]) is not None


def from_cs():
    found, concat, named, branches = {}, [], [], []
    for fn in glob.glob(os.path.join(ASSETS, "Scripts", "**", "*.cs"), recursive=True):
        src = io.open(fn, encoding="utf-8").read()
        lits = literals(src)
        tag = os.path.basename(fn)
        concat += [(b, a, tag) for b, a in concatenations(src, lits)]
        branches += [(b, a, tag) for b, a in branch_pairs(src, lits)]
        for (s1, e1, k1, a), (s2, e2, k2, b) in zip(lits, lits[1:]):
            if re.fullmatch(r"\s*,\s*", src[e1:s2]) and natural(a, b):
                found.setdefault(b, (a, tag))
            elif named_pair(src, s1, e1, s2) and (natural(a, b) or label(a, b)):
                named.append((b, a, tag))
            elif re.fullmatch(r"\s*,\s*", src[e1:s2]) and re.search(r"\bT\(\s*$", src[max(0, s1 - 10):s1]) and (label(a, b) or word(a, b)):
                named.append((b, a, tag))
            elif a == b and re.fullmatch(r"\s*,\s*", src[e1:s2]) and re.search(r"[a-z]{3}", b) and \
                    re.search(r"(?:\bT\(|\b(?:Seg|Slider|Check)\(\s*\"\w+\"\s*,|(?<![\w.])\()\s*$", src[max(0, s1 - 40):s1]):
                named.append((b, a, tag))      # same in Turkish and English, not in other languages: "Pelvis", "Hologram", "Bolus"
        # parallel arrays: new[] { tr… }, new[] { en… }  or  xxTr = { … }; xxEn = { … }
        for m in re.finditer(r"\{([^{}]*)\}", src):
            body = m.group(1)
            inner = [l for l in lits if m.start(1) <= l[0] and l[1] <= m.end(1)]
            if len(inner) < 2: continue
            nxt = src.find("{", m.end())
            if nxt < 0: continue
            between = src[m.end():nxt]
            if not re.fullmatch(r"\s*[;,]?\s*(?:new\s*(?:string)?\s*\[\s*\]\s*|string\[\]\s*\w+\s*=\s*|\w+En\s*=\s*)?", between): continue
            close = src.find("}", nxt)
            inner2 = [l for l in lits if nxt < l[0] and l[1] <= close]
            if len(inner2) != len(inner): continue
            for (_, _, _, a), (_, _, _, b) in zip(inner, inner2):
                if natural(a, b): found.setdefault(b, (a, tag))
    for b, a, tag in concat + named + branches: found.setdefault(b, (a, tag))   # appended last so earlier key lines stay put
    return found


def pair(s):
    i = s.find("||")
    return (s.strip(), s.strip()) if i < 0 else (s[:i].strip(), s[i + 2:].strip())


def from_learn():
    found = {}
    for fn in sorted(glob.glob(os.path.join(ASSETS, "Resources", "Learn", "*.txt"))):
        tag = os.path.basename(fn)
        for raw in io.open(fn, encoding="utf-8"):
            line = raw.strip()
            if not line or line.startswith("//"): continue
            if line.startswith("@topic ") or line.startswith("@lesson "):
                f = [x.strip() for x in line.split("|")]
                tr, en = (f[2], f[3]) if line.startswith("@topic ") else (f[1], f[2])
                found.setdefault(en, (tr, tag)); continue
            if line.startswith(("@sim", "@case", "%fig")) or line.startswith("$"): continue
            for pre in ("@desc ", "@formula ", "@term ", "## ", "!!", "!", ">", "*", "?", "+ ", "- ", "="):
                if line.startswith(pre):
                    line = line[len(pre):]; break
            if line.startswith(("@formula", "@term")): line = line.split(" ", 1)[1]
            parts = line.split("::")
            for p in parts:
                if "||" in p:
                    tr, en = pair(p)
                    if en: found.setdefault(en, (tr, tag))
    return found


def esc(s): return s.replace("\\", "\\\\").replace("\n", "\\n").replace("\t", "\\t")


def main():
    cs, learn = from_cs(), from_learn()
    keys, seen = [], set()
    for area, src in (("ui", cs), ("learn", learn)):
        for en, (tr, tag) in src.items():
            k = esc(mask(en))
            if k in seen or not k.strip(): continue
            seen.add(k); keys.append((k, esc(tr), tag, area))
    os.makedirs(os.path.join(ASSETS, "Resources", "Lang"), exist_ok=True)
    with io.open(os.path.join(ASSETS, "Resources", "Lang", "_keys.txt"), "w", encoding="utf-8", newline="\n") as f:
        for k, _, _, _ in keys: f.write(k + "\n")
    with io.open(os.path.join(ROOT, "Bridge", "i18n_keys_review.txt"), "w", encoding="utf-8", newline="\n") as f:
        for i, (k, tr, tag, area) in enumerate(keys): f.write(f"{i}\t{area}\t{tag}\t{k}\t{tr}\n")
    ui = [k for k in keys if k[3] == "ui"]; le = [k for k in keys if k[3] == "learn"]
    print("keys", len(keys), "ui", len(ui), "learn", len(le),
          "words ui", sum(len(k[0].split()) for k in ui), "learn", sum(len(k[0].split()) for k in le))


if __name__ == "__main__":
    main()
