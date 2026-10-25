using System.IO;
using UnityEditor;
using UnityEngine;

// Procedural, seamlessly tiling detail textures (terrazzo vinyl floor, painted walls, acoustic ceiling tiles, wood,
// brushed steel, woven fabric) written as PNG assets and wired into the material assets. Material assets (not runtime
// materials) carry the texture keywords, so the matching URP shader variants survive build stripping.
// Run through the bridge: MedPhysLook.Generate
public static class MedPhysLook
{
    const string TexDir = "Assets/Resources/Textures";
    const string MatDir = "Assets/Resources/Materials";

    public static void Generate()
    {
        Directory.CreateDirectory(TexDir);
        int n = 512;
        Save("Floor_Albedo", Floor(n, out var hFloor), false);
        Save("Floor_Normal", Normals(hFloor, n, 2.5f), true);
        Save("Wall_Albedo", Wall(256, out var hWall), false);
        Save("Wall_Normal", Normals(hWall, 256, 1.6f), true);
        Save("Ceiling_Albedo", Ceiling(n, out var hCeil), false);
        Save("Ceiling_Normal", Normals(hCeil, n, 3f), true);
        Save("Wood_Albedo", Wood(n, out var hWood), false);
        Save("Wood_Normal", Normals(hWood, n, 1.2f), true);
        Save("Steel_Albedo", Steel(256, out var hSteel), false);
        Save("Steel_Normal", Normals(hSteel, 256, 1.5f), true);
        Save("Fabric_Albedo", Fabric(256, out var hFab), false);
        Save("Fabric_Normal", Normals(hFab, 256, 4f), true);
        AssetDatabase.Refresh();
        foreach (var name in new[] { "Floor_Normal", "Wall_Normal", "Ceiling_Normal", "Wood_Normal", "Steel_Normal", "Fabric_Normal" })
            Import(name, true);
        foreach (var name in new[] { "Floor_Albedo", "Wall_Albedo", "Ceiling_Albedo", "Wood_Albedo", "Steel_Albedo", "Fabric_Albedo" })
            Import(name, false);
        Materials();
        AssetDatabase.SaveAssets();
        Debug.Log("[MP] look textures + materials generated");
    }

    // ------------------------------------------------------------------ materials
    static void Materials()
    {
        // terrazzo vinyl: texture is a ~0.5 multiplier with light / dark chips, so the tint is raised accordingly
        Textured("M_Floor", "Floor", new Color(0.29f, 0.33f, 0.39f), 1f, 0.45f, 1f / 1.5f, Vector2.zero, aniso: true);
        Textured("M_Wall", "Wall", new Color(0.80f, 0.82f, 0.86f), 0.55f, 0.1f, 1f, Vector2.zero);
        // 0.6 m ceiling tiles; the half-tile offset lines the grid up with the 1.2 x 0.6 m light panels
        Textured("M_Ceiling", "Ceiling", new Color(0.86f, 0.87f, 0.89f), 0.35f, 0.6f, 1f / 1.2f, new Vector2(0, 0.25f));
        Textured("M_Wood", "Wood", new Color(1f, 1f, 1f), 0.85f, 0.5f, 1f, Vector2.zero);
        var steel = Textured("M_Steel", "Steel", new Color(0.86f, 0.87f, 0.88f), 0.8f, 0.35f, 2f, Vector2.zero);
        steel.SetFloat("_Metallic", 1f);
        Textured("M_Fabric", "Fabric", Color.white, 0.22f, 0.5f, 6f, Vector2.zero);
        Textured("M_ApronBlue", "Fabric", new Color(0.13f, 0.22f, 0.48f), 0.55f, 0.5f, 6f, Vector2.zero);
        Textured("M_ApronTeal", "Fabric", new Color(0.05f, 0.42f, 0.44f), 0.55f, 0.5f, 6f, Vector2.zero);
        Textured("M_ApronPurple", "Fabric", new Color(0.36f, 0.2f, 0.52f), 0.55f, 0.5f, 6f, Vector2.zero);
        var lamp = Plain("M_Lamp", new Color(0.6f, 0.08f, 0.06f), 0.7f);
        lamp.EnableKeyword("_EMISSION");
        lamp.SetColor("_EmissionColor", new Color(3.2f, 0.25f, 0.12f));
        lamp.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        Plain("M_Paper", new Color(0.93f, 0.93f, 0.9f), 0.15f);
        Plain("M_Plant", new Color(0.16f, 0.45f, 0.22f), 0.35f);
        foreach (var n in new[] { "M_Lamp" }) EditorUtility.SetDirty(Load(n));
    }

    static Material Load(string name) => AssetDatabase.LoadAssetAtPath<Material>(MatDir + "/" + name + ".mat");

    static Material Ensure(string name)
    {
        var m = Load(name);
        if (m != null) return m;
        m = new Material(Load("M_Shell")) { name = name };
        AssetDatabase.CreateAsset(m, MatDir + "/" + name + ".mat");
        return m;
    }

    static Material Plain(string name, Color c, float smooth)
    {
        var m = Ensure(name);
        m.SetColor("_BaseColor", c);
        m.SetFloat("_Smoothness", smooth);
        m.SetFloat("_Metallic", 0f);
        EditorUtility.SetDirty(m);
        return m;
    }

    /// <param name="smooth">Smoothness multiplier (the albedo alpha holds the per-pixel smoothness).</param>
    static Material Textured(string name, string tex, Color tint, float smooth, float bump, float tiling, Vector2 offset, bool aniso = false)
    {
        var m = Ensure(name);
        var albedo = AssetDatabase.LoadAssetAtPath<Texture2D>(TexDir + "/" + tex + "_Albedo.png");
        var normal = AssetDatabase.LoadAssetAtPath<Texture2D>(TexDir + "/" + tex + "_Normal.png");
        m.SetTexture("_BaseMap", albedo);
        m.SetTexture("_MainTex", albedo);
        m.SetTexture("_BumpMap", normal);
        m.SetFloat("_BumpScale", bump);
        m.SetColor("_BaseColor", tint);
        m.SetColor("_Color", tint);
        m.SetFloat("_Smoothness", smooth);
        m.SetFloat("_Metallic", 0f);
        m.SetFloat("_SmoothnessTextureChannel", 1f);
        m.EnableKeyword("_NORMALMAP");
        m.EnableKeyword("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A");
        m.SetTextureScale("_BaseMap", new Vector2(tiling, tiling));
        m.SetTextureOffset("_BaseMap", offset);
        m.SetTextureScale("_MainTex", new Vector2(tiling, tiling));
        m.SetTextureOffset("_MainTex", offset);
        EditorUtility.SetDirty(m);
        if (aniso)
        {
            var ti = (TextureImporter)AssetImporter.GetAtPath(TexDir + "/" + tex + "_Albedo.png");
            if (ti.anisoLevel != 8) { ti.anisoLevel = 8; ti.SaveAndReimport(); }
        }
        return m;
    }

    // ------------------------------------------------------------------ texture io
    static void Save(string name, Color[] px, bool normal)
    {
        int n = (int)Mathf.Sqrt(px.Length);
        var t = new Texture2D(n, n, TextureFormat.RGBA32, false, normal);
        t.SetPixels(px);
        t.Apply(false);
        File.WriteAllBytes(TexDir + "/" + name + ".png", t.EncodeToPNG());
        Object.DestroyImmediate(t);
    }

    static void Import(string name, bool normal)
    {
        string path = TexDir + "/" + name + ".png";
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        var ti = (TextureImporter)AssetImporter.GetAtPath(path);
        ti.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
        ti.sRGBTexture = !normal;
        ti.alphaSource = normal ? TextureImporterAlphaSource.None : TextureImporterAlphaSource.FromInput;
        ti.alphaIsTransparency = false;
        ti.wrapMode = TextureWrapMode.Repeat;
        ti.mipmapEnabled = true;
        ti.filterMode = FilterMode.Trilinear;
        ti.anisoLevel = Mathf.Max(ti.anisoLevel, 4);
        ti.maxTextureSize = 512;
        ti.SaveAndReimport();
    }

    // ------------------------------------------------------------------ periodic noise
    static float Hash(int x, int y, int seed)
    {
        unchecked
        {
            uint h = (uint)(x * 374761393) + (uint)(y * 668265263) + (uint)seed * 2246822519u;
            h = (h ^ (h >> 13)) * 1274126177u;
            return ((h ^ (h >> 16)) & 0xffffff) / 16777215f;
        }
    }

    static int Mod(int a, int p) => ((a % p) + p) % p;

    /// <summary>Value noise on a lattice that repeats every px × py cells.</summary>
    static float Noise(float x, float y, int px, int py, int seed)
    {
        int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
        float fx = x - x0, fy = y - y0;
        fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
        int a = Mod(x0, px), b = Mod(x0 + 1, px), c = Mod(y0, py), d = Mod(y0 + 1, py);
        float v00 = Hash(a, c, seed), v10 = Hash(b, c, seed), v01 = Hash(a, d, seed), v11 = Hash(b, d, seed);
        return Mathf.Lerp(Mathf.Lerp(v00, v10, fx), Mathf.Lerp(v01, v11, fx), fy);
    }

    /// <summary>Tileable fBm over the unit square (u, v in [0, 1)).</summary>
    static float Fbm(float u, float v, int fx, int fy, int octaves, int seed)
    {
        float s = 0, a = 0.5f, norm = 0;
        for (int o = 0; o < octaves; o++)
        {
            s += a * Noise(u * fx, v * fy, fx, fy, seed + o * 17);
            norm += a; a *= 0.5f; fx *= 2; fy *= 2;
        }
        return s / norm;
    }

    static Color[] Normals(float[] h, int n, float strength)
    {
        var px = new Color[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = (h[y * n + (x + 1) % n] - h[y * n + (x - 1 + n) % n]) * 0.5f * strength;
                float dy = (h[((y + 1) % n) * n + x] - h[((y - 1 + n) % n) * n + x]) * 0.5f * strength;
                var nv = new Vector3(-dx, -dy, 1f).normalized;
                px[y * n + x] = new Color(nv.x * 0.5f + 0.5f, nv.y * 0.5f + 0.5f, nv.z * 0.5f + 0.5f, 1f);
            }
        return px;
    }

    // ------------------------------------------------------------------ patterns (height in pixels of relief)
    static Color[] Floor(int n, out float[] h)
    {
        var px = new Color[n * n]; h = new float[n * n];
        var chip = new float[n * n]; var cover = new float[n * n];
        // marble chips of three tones, stamped with wrap-around
        var rnd = new System.Random(7);
        for (int k = 0; k < 1800; k++)
        {
            float cx = (float)rnd.NextDouble() * n, cy = (float)rnd.NextDouble() * n;
            float r = 0.7f + 2.2f * (float)System.Math.Pow(rnd.NextDouble(), 2.2);
            double t = rnd.NextDouble();
            float tone = t < 0.55 ? 0.95f : t < 0.8 ? 0.72f : 0.22f;
            int ri = Mathf.CeilToInt(r + 1);
            for (int dy = -ri; dy <= ri; dy++)
                for (int dx = -ri; dx <= ri; dx++)
                {
                    float d = Mathf.Sqrt(dx * dx + dy * dy) - r;
                    if (d > 0.5f) continue;
                    int x = Mod(Mathf.RoundToInt(cx) + dx, n), y = Mod(Mathf.RoundToInt(cy) + dy, n);
                    float cov = Mathf.Clamp01(0.5f - d);
                    int i = y * n + x;
                    if (cov <= 0) continue;
                    chip[i] = cover[i] > 0 ? Mathf.Lerp(chip[i], tone, cov) : tone;   // later chips lie on top
                    cover[i] = Mathf.Max(cover[i], cov);
                }
        }
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float u = x / (float)n, v = y / (float)n;
                float mottle = Fbm(u, v, 4, 4, 4, 11);
                float fine = Fbm(u, v, 64, 64, 2, 23);
                float b = 0.50f + 0.10f * (mottle - 0.5f) + 0.05f * (fine - 0.5f);
                float c = chip[y * n + x], cov = cover[y * n + x];
                b = Mathf.Lerp(b, c, 0.7f * cov);
                float g = 0.86f + 0.10f * (Fbm(u, v, 8, 8, 3, 31) - 0.5f) - 0.06f * fine;
                h[y * n + x] = 0.35f * Fbm(u, v, 32, 32, 3, 41) + 0.08f * cov;
                px[y * n + x] = new Color(b, b, b, Mathf.Clamp01(g));
            }
        return px;
    }

    static Color[] Wall(int n, out float[] h)
    {
        var px = new Color[n * n]; h = new float[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float u = x / (float)n, v = y / (float)n;
                float peel = Fbm(u, v, 24, 24, 4, 5);
                float b = 0.95f + 0.05f * (Fbm(u, v, 3, 3, 3, 9) - 0.5f) + 0.02f * (peel - 0.5f);
                h[y * n + x] = peel;
                px[y * n + x] = new Color(b, b, b, 0.75f + 0.15f * peel);
            }
        return px;
    }

    static Color[] Ceiling(int n, out float[] h)
    {
        var px = new Color[n * n]; h = new float[n * n];
        int tile = n / 2;                      // 2 x 2 tiles per texture
        float bar = n * 0.014f;                // ~1.7 cm T-bar on a 1.2 m texture
        var rnd = new System.Random(3);
        var pit = new float[n * n];
        for (int k = 0; k < 9000; k++)
        {
            int cx = rnd.Next(n), cy = rnd.Next(n);
            pit[cy * n + cx] = 1; if (rnd.NextDouble() < 0.4) pit[cy * n + (cx + 1) % n] = 1;
        }
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float u = x / (float)n, v = y / (float)n;
                float ex = Mathf.Min(x % tile, tile - x % tile), ey = Mathf.Min(y % tile, tile - y % tile);
                float e = Mathf.Min(ex, ey);
                bool onBar = e < bar * 0.5f;
                float fiss = Fbm(u, v, 40, 40, 3, 13);
                float b, hh, g;
                if (onBar) { b = 0.97f; hh = 0.6f; g = 0.55f; }
                else
                {
                    float bevel = Mathf.Clamp01((e - bar * 0.5f) / 3f);
                    b = 0.93f - 0.06f * Mathf.Clamp01((fiss - 0.62f) * 6f) - 0.18f * pit[y * n + x];
                    hh = 0.45f * bevel - 0.25f * pit[y * n + x] - 0.12f * Mathf.Clamp01((fiss - 0.62f) * 6f) - 0.1f;
                    g = 0.2f;
                }
                h[y * n + x] = hh;
                px[y * n + x] = new Color(b, b, b, g);
            }
        return px;
    }

    static Color[] Wood(int n, out float[] h)
    {
        var px = new Color[n * n]; h = new float[n * n];
        var dark = new Color(0.42f, 0.27f, 0.15f); var light = new Color(0.70f, 0.52f, 0.33f);
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float u = x / (float)n, v = y / (float)n;
                float warp = Fbm(u, v, 2, 8, 4, 77);
                float ring = 0.5f + 0.5f * Mathf.Sin((v * 18f + warp * 3f) * Mathf.PI * 2f);
                ring = Mathf.Pow(ring, 3f);
                float streak = Fbm(u, v, 4, 128, 2, 91);
                var c = Color.Lerp(light, dark, 0.65f * ring + 0.35f * streak);
                h[y * n + x] = 0.6f * streak + 0.3f * ring;
                px[y * n + x] = new Color(c.r, c.g, c.b, 0.55f + 0.1f * (1 - ring));
            }
        return px;
    }

    static Color[] Steel(int n, out float[] h)
    {
        var px = new Color[n * n]; h = new float[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float u = x / (float)n, v = y / (float)n;
                float s = Fbm(u, v, 2, 128, 3, 55);          // long streaks along u
                float b = 0.82f + 0.16f * (s - 0.5f);
                h[y * n + x] = s;
                px[y * n + x] = new Color(b, b, b, 0.75f + 0.2f * (s - 0.5f));
            }
        return px;
    }

    static Color[] Fabric(int n, out float[] h)
    {
        var px = new Color[n * n]; h = new float[n * n];
        int threads = 32;
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float cu = x * threads / (float)n, cv = y * threads / (float)n;
                int i = Mathf.FloorToInt(cu), j = Mathf.FloorToInt(cv);
                float fu = cu - i, fv = cv - j;
                bool warp = ((i + j) & 1) == 0;
                float across = warp ? Mathf.Sin(Mathf.PI * fu) : Mathf.Sin(Mathf.PI * fv);
                float along = warp ? Mathf.Sin(Mathf.PI * fv) : Mathf.Sin(Mathf.PI * fu);
                float fuzz = Fbm(x / (float)n, y / (float)n, 64, 64, 2, 19);
                float hh = Mathf.Sqrt(across) * (0.7f + 0.3f * along) + 0.15f * fuzz;
                h[y * n + x] = hh;
                float b = 0.80f + 0.20f * hh;
                px[y * n + x] = new Color(b, b, b, 0.35f + 0.3f * hh);
            }
        return px;
    }
}
