using System.Collections.Generic;
using UnityEngine;

namespace MedPhys.Core
{
    /// <summary>Resource loading (Blender models, URP materials, custom shaders) and material factories.</summary>
    public static class Res
    {
        static readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();
        static readonly Dictionary<string, GameObject> models = new Dictionary<string, GameObject>();

        public static Material Mat(string name)
        {
            if (!mats.TryGetValue(name, out var m) || m == null)
            {
                m = Resources.Load<Material>("Materials/" + name);
                if (m == null) Debug.LogWarning("[MP] missing material " + name);
                mats[name] = m;
            }
            return m;
        }

        public static GameObject Model(string name, Transform parent, Vector3 pos = default, float yaw = 0)
        {
            if (!models.TryGetValue(name, out var prefab) || prefab == null)
            {
                prefab = Resources.Load<GameObject>("Models/" + name);
                models[name] = prefab;
            }
            if (prefab == null) { Debug.LogWarning("[MP] missing model " + name); return new GameObject(name); }
            // the imported root carries the Blender→Unity axis conversion, so it is wrapped instead of re-oriented
            var holder = new GameObject(name);
            holder.transform.SetParent(parent, false);
            holder.transform.localPosition = pos;
            holder.transform.localRotation = Quaternion.Euler(0, yaw, 0);
            var go = Object.Instantiate(prefab, holder.transform, false);
            go.name = name + "_Mesh";
            return holder;
        }

        public static Transform Find(this Transform t, string name)
        {
            if (t.name == name) return t;
            foreach (Transform c in t)
            {
                var r = Find(c, name);
                if (r != null) return r;
            }
            return null;
        }

        public static Transform Find(this GameObject g, string name) => Find(g.transform, name);

        // ---------------------------------------------------------------- material factories
        public static Material Lit(Color c, float smooth = 0.55f, float metal = 0f)
        {
            var m = new Material(Mat("M_Shell"));
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Smoothness", smooth);
            m.SetFloat("_Metallic", metal);
            return m;
        }

        /// <summary>Woven cloth (clothes, aprons): weave normal map, low sheen.</summary>
        public static Material Fabric(Color c)
        {
            var m = new Material(Mat("M_Fabric"));
            m.SetColor("_BaseColor", c);
            return m;
        }

        public static Material LitTransparent(Color c, float smooth = 0.8f)
        {
            var m = new Material(Mat("M_Glass"));
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Smoothness", smooth);
            return m;
        }

        public static Material Emissive(Color baseCol, Color emission)
        {
            var m = new Material(Mat("M_Accent"));
            m.SetColor("_BaseColor", baseCol);
            m.SetColor("_EmissionColor", emission);
            return m;
        }

        static Shader Sh(string n)
        {
            var s = Shader.Find(n);
            if (s == null) Debug.LogWarning("[MP] missing shader " + n);
            return s;
        }

        /// <summary>Additive glow (beams, particles, tracks). Colour alpha scales brightness.</summary>
        public static Material Additive(Color c, float intensity = 1f, float fresnel = 0f, Texture tex = null)
        {
            var m = new Material(Sh("MedPhys/Additive"));
            m.SetColor("_Color", c);
            m.SetFloat("_Intensity", intensity);
            m.SetFloat("_Fresnel", fresnel);
            if (tex != null) m.SetTexture("_MainTex", tex);
            return m;
        }

        /// <summary>Transparent fresnel "hologram" surface (phantom skin, ghosted housings).</summary>
        public static Material Hologram(Color body, Color rim, float rimPower = 2.5f)
        {
            var m = new Material(Sh("MedPhys/Hologram"));
            m.SetColor("_Color", body);
            m.SetColor("_RimColor", rim);
            m.SetFloat("_RimPower", rimPower);
            return m;
        }

        /// <summary>Unlit texture, optionally alpha blended (detector screens, dose maps, image planes).</summary>
        public static Material UnlitTex(Texture tex, Color tint, bool transparent = false)
        {
            var m = new Material(Sh("MedPhys/UnlitTex"));
            m.SetTexture("_MainTex", tex);
            m.SetColor("_Color", tint);
            if (transparent)
            {
                m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                m.SetFloat("_ZWrite", 0f);
                m.renderQueue = 3000;
            }
            else
            {
                m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
                m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.Zero);
                m.SetFloat("_ZWrite", 1f);
                m.renderQueue = 2000;
            }
            return m;
        }

        /// <summary>Replaces every use of a named shared material under root by the given material.</summary>
        public static void Swap(GameObject root, string matName, Material with)
        {
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                var sm = r.sharedMaterials;
                bool ch = false;
                for (int i = 0; i < sm.Length; i++)
                    if (sm[i] != null && sm[i].name.StartsWith(matName)) { sm[i] = with; ch = true; }
                if (ch) r.sharedMaterials = sm;
            }
        }

        public static void SetLayerShadows(GameObject root, bool cast)
        {
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                r.shadowCastingMode = cast ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
        }
    }
}
