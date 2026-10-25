"""Flags translations whose numbers would be substituted in the wrong order.

  python i18n_order.py [code ...]

Numbers are filled into '#' placeholders in order, so a translation that reorders "the # keV photons of Tc-#m" into
"Tc-#m's # keV photons" silently swaps 140 and 99. Each '#' is labelled by a language-neutral neighbour (an isotope
prefix such as Tc- or a unit such as keV, MBq, %) and the label sequences of key and translation are compared.
"""
import re, sys
import i18n

PREFIX = re.compile(r"(?:\b(Tc|Mo|F|I|Co|Cs|Ir|Lu|Ga|Ge|Rb|Sr|Y|Ra|Tl|In|Xe|Kr|C|N|O)-|\b(ICRP|IEC|ISO|AAPM|TG|TRS)\s?)$")
UNIT = re.compile(r"^[\s\-]?(keV|MeV|MV|MHz|kHz|GBq|MBq|kBq|mGy|cGy|Gy|mSv|µSv|Sv|kVp|kV|mAs|mA|cm|mm|nm|µm|%|°|ms|dB|HU|×|(?:T|K|h|min|s)(?!\w))")


def labels(s):
    out = []
    for m in re.finditer("#", s):
        p = PREFIX.search(s[:m.start()])
        u = UNIT.match(s[m.end():])
        out.append((p.group(1) or p.group(2)) if p else u.group(1) if u else None)
    return out


def misplaced(a, b):
    """True when a label sits at different '#' positions in key and translation."""
    if any(x and y and x != y for x, y in zip(a, b)): return True
    for lab in set(filter(None, a)):
        ia = [i for i, x in enumerate(a) if x == lab]
        ib = [i for i, x in enumerate(b) if x == lab]
        if len(ia) == len(ib) and ia != ib: return True
    return False


def main():
    codes = sys.argv[1:] or i18n.CODES
    keys = i18n.keys()
    bad = 0
    for code in codes:
        tr = i18n.load(code)
        for n, k in enumerate(keys, 1):
            if k.count("#") < 2: continue
            t = tr.get("%08x" % i18n.fnv(k))
            if t is None: continue
            a, b = labels(i18n.unescape(k)), labels(i18n.unescape(t))
            if len(a) != len(b): continue
            if misplaced(a, b):
                bad += 1
                print("%s %d\n  en: %s\n  %s: %s" % (code, n, k, code, t))
    print("suspicious:", bad)


if __name__ == "__main__":
    main()
