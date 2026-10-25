using System.Collections.Generic;
using UnityEngine;
using MedPhys.Physics;

namespace MedPhys.Core
{
    /// <summary>
    /// 3D rendering of a <see cref="Phantom"/>. Child meshes are baked in the phantom's phys frame (cm,
    /// x = left, y = anterior, z = superior) under a "Frame" transform that maps it into Unity
    /// (rotation 180° about (0,1,1), scale 0.01): phys x → -X, y → +Z, z → +Y. The root object is then
    /// placed/oriented in the scene (standing, supine on a couch ...).
    /// </summary>
    public sealed class PhantomView : MonoBehaviour
    {
        public Phantom Ph;
        public Transform Frame;
        readonly Dictionary<string, List<Renderer>> organs = new Dictionary<string, List<Renderer>>();
        readonly List<Renderer> skin = new List<Renderer>();
        Material skinHolo, skinSolid;
        static readonly Dictionary<string, Material> organMats = new Dictionary<string, Material>();

        public static PhantomView Create(Phantom ph, Transform parent, string name = "Phantom")
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var v = go.AddComponent<PhantomView>();
            v.Ph = ph;
            v.Build();
            return v;
        }

        static Material OrganMat(string organ, TissueId t)
        {
            string key = organ + "_" + t;
            if (organMats.TryGetValue(key, out var m) && m != null) return m;
            switch (organ)
            {
                case "lungL": case "lungR": m = Res.LitTransparent(new Color(0.98f, 0.55f, 0.62f, 0.42f), 0.7f); break;
                case "heart": case "aorta":
                    m = t == TissueId.ContrastBlood ? Res.Emissive(new Color(1f, 0.8f, 0.3f), new Color(0.8f, 0.5f, 0.1f)) : Res.Lit(new Color(0.72f, 0.08f, 0.10f), 0.65f); break;
                case "liver": m = Res.Lit(new Color(0.48f, 0.14f, 0.12f), 0.55f); break;
                case "stomach": m = Res.Lit(new Color(0.86f, 0.52f, 0.45f), 0.5f); break;
                case "spleen": m = Res.Lit(new Color(0.45f, 0.12f, 0.24f), 0.5f); break;
                case "kidney": m = Res.Lit(new Color(0.58f, 0.18f, 0.16f), 0.55f); break;
                case "bladder": m = Res.LitTransparent(new Color(0.95f, 0.85f, 0.35f, 0.55f)); break;
                case "brain": m = Res.Lit(new Color(0.90f, 0.66f, 0.68f), 0.45f); break;
                case "eye": m = Res.Lit(new Color(0.95f, 0.95f, 0.97f), 0.9f); break;
                case "airway": m = Res.LitTransparent(new Color(0.5f, 0.8f, 1f, 0.35f)); break;
                case "cord": m = Res.Emissive(new Color(1f, 0.85f, 0.2f), new Color(0.6f, 0.45f, 0.05f)); break;
                case "tumor": m = Res.Emissive(new Color(0.3f, 1f, 0.4f), new Color(0.3f, 1.6f, 0.45f)); break;
                case "implant": m = Res.Lit(new Color(0.78f, 0.80f, 0.84f), 0.85f, 1f); break;
                default:
                    if (t == TissueId.CorticalBone || t == TissueId.SpongyBone || t == TissueId.MixedBone)
                        m = Res.Lit(new Color(0.93f, 0.89f, 0.79f), 0.35f);
                    else m = Res.Lit(Tissues.Get(t).Color, 0.5f);
                    break;
            }
            organMats[key] = m;
            return m;
        }

        void Build()
        {
            Frame = new GameObject("Frame").transform;
            Frame.SetParent(transform, false);
            Frame.localRotation = Quaternion.AngleAxis(180f, new Vector3(0, 1, 1).normalized);
            Frame.localScale = Vector3.one * 0.01f;
            skinHolo = Res.Hologram(new Color(0.30f, 0.70f, 1.0f, 0.05f), new Color(0.45f, 0.85f, 1.0f, 0.75f), 2.2f);
            skinSolid = Res.Lit(new Color(0.86f, 0.70f, 0.60f), 0.35f);
            foreach (var p in Ph.Prims)
            {
                if (!p.Visible) continue;
                if (p.Organ == "gas" || p.Organ == "csf" || p.Organ == "disc") continue;
                if (p.Organ == "body" && !p.Skin) continue;
                Mesh mesh;
                switch (p.Kind)
                {
                    case PrimKind.Ellipsoid:
                        mesh = MeshGen.Ellipsoid(p.C, p.R, p.Rot, p.Skin ? 30 : 20, p.Skin ? 48 : 32, p.HasClip, p.ClipN, p.ClipD);
                        break;
                    case PrimKind.Cylinder:
                        mesh = MeshGen.Cylinder(p.C, p.R, p.Rot, p.Skin ? 56 : 28, p.Inner, p.HasClip, p.ClipN, p.ClipD);
                        break;
                    default:
                        mesh = MeshGen.Box(p.C, p.R, p.Rot);
                        break;
                }
                var g = new GameObject(p.Name);
                g.transform.SetParent(Frame, false);
                g.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = g.AddComponent<MeshRenderer>();
                if (p.Skin)
                {
                    r.sharedMaterial = skinHolo;
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    skin.Add(r);
                }
                else
                {
                    r.sharedMaterial = OrganMat(p.Organ, p.Tissue);
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
                if (!organs.TryGetValue(p.Organ, out var list)) organs[p.Organ] = list = new List<Renderer>();
                list.Add(r);
            }
        }

        /// <summary>0 = transparent hologram skin, 1 = opaque skin, 2 = no skin.</summary>
        public void SetSkinMode(int mode)
        {
            foreach (var r in skin)
            {
                r.enabled = mode != 2;
                r.sharedMaterial = mode == 1 ? skinSolid : skinHolo;
                r.shadowCastingMode = mode == 1 ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }

        public void SetOrganVisible(string organ, bool v)
        {
            if (organs.TryGetValue(organ, out var list)) foreach (var r in list) r.enabled = v;
        }

        public void SetAllOrgansVisible(bool v)
        {
            foreach (var kv in organs) if (kv.Key != "body") foreach (var r in kv.Value) r.enabled = v;
        }

        /// <summary>Skin hologram clip plane in world space (keeps dot(p, n) &lt;= d).</summary>
        public void SetSkinClip(bool on, Vector3 n = default, float d = 0)
        {
            skinHolo.SetFloat("_UseClip", on ? 1 : 0);
            skinHolo.SetVector("_ClipPlane", new Vector4(n.x, n.y, n.z, d));
        }

        /// <summary>Orient: phantom superior axis along 'superior', anterior along 'anterior' (world directions).</summary>
        public void Orient(Vector3 position, Vector3 superior, Vector3 anterior)
        {
            transform.position = position;
            transform.rotation = Quaternion.LookRotation(anterior, superior);
        }

        public Vector3 PhysToWorld(Vector3 p) => Frame.TransformPoint(p);
        public Vector3 WorldToPhys(Vector3 w) => Frame.InverseTransformPoint(w);
        public Vector3 WorldDirToPhys(Vector3 d) => Frame.InverseTransformDirection(d).normalized;
        public Vector3 PhysDirToWorld(Vector3 d) => Frame.TransformDirection(d).normalized;
    }
}
