using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

// Dynamic SDF font assets for the non-Latin scripts (Noto, see Bridge/i18n_fonts.py) registered as fallbacks of the
// UI panel, so any glyph missing from Inter (Arabic, Devanagari, Bengali, Thai, CJK) is found automatically.
// Run through the bridge (MedPhysFonts.Setup) or the MedPhys → Fonts menu.
public static class MedPhysFonts
{
    const string Dir = "Assets/Resources/Fonts/Noto";
    static readonly string[] Names = { "NotoSansArabic", "NotoSansDevanagari", "NotoSansBengali", "NotoSansThai", "NotoSansCJK" };

    [MenuItem("MedPhys/Fonts")]
    public static void Setup()
    {
        var list = new List<FontAsset>();
        foreach (var n in Names)
        {
            var font = AssetDatabase.LoadAssetAtPath<Font>(Dir + "/" + n + ".ttf");
            if (font == null) { Debug.LogWarning("[MP] missing font " + n); continue; }
            string path = Dir + "/" + n + " SDF.asset";
            var fa = AssetDatabase.LoadAssetAtPath<FontAsset>(path);
            if (fa == null)
            {
                fa = FontAsset.CreateFontAsset(font, 64, 6, GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic, true);
                fa.name = n + " SDF";
                AssetDatabase.CreateAsset(fa, path);
                foreach (var t in fa.atlasTextures) { t.name = n + " Atlas"; AssetDatabase.AddObjectToAsset(t, fa); }
                fa.material.name = n + " Material";
                AssetDatabase.AddObjectToAsset(fa.material, fa);
            }
            list.Add(fa);
        }
        string tsPath = "Assets/Resources/UI/PanelTextSettings.asset";
        var ts = AssetDatabase.LoadAssetAtPath<PanelTextSettings>(tsPath);
        if (ts == null) { ts = ScriptableObject.CreateInstance<PanelTextSettings>(); AssetDatabase.CreateAsset(ts, tsPath); }
        ts.fallbackFontAssets = list;
        EditorUtility.SetDirty(ts);
        var ps = AssetDatabase.LoadAssetAtPath<PanelSettings>("Assets/Resources/UI/PanelSettings.asset");
        ps.textSettings = ts;
        EditorUtility.SetDirty(ps);
        AssetDatabase.SaveAssets();
        Debug.Log("[MP] fonts: " + list.Count + " fallback font assets");
    }
}
