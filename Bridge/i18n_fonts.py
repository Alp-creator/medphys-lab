"""Static, subsetted Noto fonts for the non-Latin scripts (OFL fonts shipped with Blender).

Without arguments every font is converted in full; with --subset only the characters used by the translation files
(Assets/Resources/Lang/*.txt) plus basic punctuation/digits are kept, which keeps the CJK font small.
usage: python i18n_fonts.py [--subset]
"""
import glob, io, os, sys, unicodedata
from fontTools.ttLib import TTFont
from fontTools import subset as ftsubset
from fontTools.varLib import instancer

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC = r"C:\Program Files\Blender Foundation\Blender 5.2\5.2\datafiles\fonts"
DST = os.path.join(ROOT, "Assets", "Resources", "Fonts", "Noto")
FONTS = {   # output name: (blender file, languages)
    "NotoSansArabic": ("NotoSansArabic-VariableFont_wdth,wght.woff2", ["ar", "fa"]),
    "NotoSansDevanagari": ("NotoSansDevanagari-Regular.woff2", ["hi"]),
    "NotoSansBengali": ("NotoSansBengali-VariableFont_wdth,wght.woff2", ["bn"]),
    "NotoSansThai": ("NotoSansThai-VariableFont_wdth,wght.woff2", ["th"]),
    "NotoSansCJK": ("Noto Sans CJK Regular.woff2", ["zh", "ja", "ko"]),
}


def used_text(langs):
    s = set()
    for code in langs:
        p = os.path.join(ROOT, "Assets", "Resources", "Lang", code + ".txt")
        if os.path.exists(p):
            t = io.open(p, encoding="utf-8").read()
            # the shaper decomposes some characters (Thai SARA AM → NIKHAHIT + SARA AA, Bengali AU → E + AU length
            # mark, ...), so the glyphs of the canonical and compatibility decompositions must stay in the subset
            s.update(t, unicodedata.normalize("NFD", t), unicodedata.normalize("NFKD", t))
    return s


def main():
    os.makedirs(DST, exist_ok=True)
    do_subset = "--subset" in sys.argv
    for name, (fn, langs) in FONTS.items():
        f = TTFont(os.path.join(SRC, fn))
        f.flavor = None
        if "fvar" in f:
            axes = {a.axisTag: (400 if a.axisTag == "wght" else a.defaultValue) for a in f["fvar"].axes}
            f = instancer.instantiateVariableFont(f, axes)
        if do_subset:
            chars = used_text(langs) | set(" 0123456789.,:;!?%()[]{}-–—+=/×·•…'\"«»“”‘’「」『』（）、。，：；？！")
            opts = ftsubset.Options()
            opts.layout_features = ["*"]          # keep shaping (Arabic joining, Indic conjuncts, Thai marks)
            opts.name_IDs = ["*"]; opts.notdef_outline = True; opts.glyph_names = False
            sub = ftsubset.Subsetter(opts)
            sub.populate(unicodes=[ord(c) for c in chars if ord(c) > 31])
            sub.subset(f)
        out = os.path.join(DST, name + ".ttf")
        f.save(out)
        print(name, os.path.getsize(out) // 1024, "KB")


if __name__ == "__main__":
    main()
