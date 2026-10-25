using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// One-click project setup: URP materials for the Blender models, model re-import.
public static class MedPhysSetup
{
    const string MatDir = "Assets/Resources/Materials";

    struct MatDef
    {
        public string name; public Color col; public float metal, smooth; public Color emit; public bool transparent;
        public MatDef(string n, Color c, float m, float s, Color e = default, bool t = false)
        { name = n; col = c; metal = m; smooth = s; emit = e; transparent = t; }
    }

    static readonly MatDef[] Defs =
    {
        new MatDef("M_Shell",     new Color(0.93f, 0.94f, 0.96f), 0f, 0.78f),
        new MatDef("M_ShellGrey", new Color(0.56f, 0.60f, 0.66f), 0f, 0.62f),
        new MatDef("M_Dark",      new Color(0.055f, 0.06f, 0.075f), 0f, 0.55f),
        new MatDef("M_Accent",    new Color(0.05f, 0.55f, 0.85f), 0f, 0.8f, new Color(0.25f, 1.6f, 2.6f)),
        new MatDef("M_Metal",     new Color(0.78f, 0.80f, 0.83f), 1f, 0.72f),
        new MatDef("M_Glass",     new Color(0.60f, 0.85f, 1.00f, 0.16f), 0f, 0.96f, default, true),
        new MatDef("M_Pad",       new Color(0.07f, 0.14f, 0.27f), 0f, 0.25f),
        new MatDef("M_Screen",    new Color(0.01f, 0.02f, 0.03f), 0f, 0.92f, new Color(0.01f, 0.05f, 0.08f)),
        new MatDef("M_Lead",      new Color(0.33f, 0.34f, 0.36f), 0.7f, 0.35f),
        new MatDef("M_Warning",   new Color(1.00f, 0.74f, 0.05f), 0f, 0.5f),
        new MatDef("M_Floor",     new Color(0.15f, 0.17f, 0.20f), 0f, 0.82f),
        new MatDef("M_Wall",      new Color(0.78f, 0.80f, 0.84f), 0f, 0.30f),
        new MatDef("M_Light",     new Color(1.00f, 0.97f, 0.92f), 0f, 0.5f, new Color(4f, 3.9f, 3.7f)),
        new MatDef("M_ShellFade", new Color(0.93f, 0.94f, 0.96f, 0.12f), 0f, 0.9f, default, true),
    };

    public static void Setup()
    {
        CreateMaterials();
        if (AssetDatabase.IsValidFolder("Assets/Resources/Models/Materials"))
            AssetDatabase.DeleteAsset("Assets/Resources/Models/Materials");
        foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { "Assets/Resources/Models" }))
            AssetDatabase.ImportAsset(AssetDatabase.GUIDToAssetPath(guid), ImportAssetOptions.ForceUpdate);
        AssetDatabase.SaveAssets();
        MedPhysTools.ReportModels();
    }

    public static void CreateMaterials()
    {
        Directory.CreateDirectory(MatDir);
        AssetDatabase.Refresh();
        var lit = Shader.Find("Universal Render Pipeline/Lit");
        foreach (var d in Defs)
        {
            string path = MatDir + "/" + d.name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool isNew = m == null;
            if (isNew) m = new Material(lit);
            m.shader = lit;
            m.SetColor("_BaseColor", d.col);
            m.SetFloat("_Metallic", d.metal);
            m.SetFloat("_Smoothness", d.smooth);
            if (d.emit.maxColorComponent > 0)
            {
                m.SetColor("_EmissionColor", d.emit);
                m.EnableKeyword("_EMISSION");
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            else
            {
                m.SetColor("_EmissionColor", Color.black);
                m.DisableKeyword("_EMISSION");
            }
            if (d.transparent)
            {
                m.SetFloat("_Surface", 1f);
                m.SetFloat("_Blend", 0f);
                m.SetFloat("_ZWrite", 0f);
                m.SetFloat("_Cull", (float)CullMode.Off);
                m.SetFloat("_BlendModePreserveSpecular", 0f);
            }
            else
            {
                m.SetFloat("_Surface", 0f);
            }
            BaseShaderGUI.SetMaterialKeywords(m);
            if (isNew) AssetDatabase.CreateAsset(m, path);
            else EditorUtility.SetDirty(m);
        }
        AssetDatabase.SaveAssets();
        Debug.Log("[MP] materials ready");
    }
}
