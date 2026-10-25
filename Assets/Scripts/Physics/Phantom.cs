using System;
using System.Collections.Generic;
using UnityEngine;

namespace MedPhys.Physics
{
    public enum PrimKind { Ellipsoid, Cylinder, Box }

    /// <summary>
    /// One analytic solid of the mathematical phantom (in the spirit of the MIRD / ORNL stylised phantoms).
    /// Frame ("phys frame"): x = patient's left, y = anterior, z = superior, units cm.
    /// Ellipsoid: semi-axes R. Cylinder: elliptic cross-section R.x, R.y, half-length R.z along local z.
    /// Box: half-extents R. Inner &gt; 0 makes a shell/tube (radial scale of the hollow).
    /// Optional clip half-space keeps points with dot(ClipN, p) &lt;= ClipD (phys frame).
    /// </summary>
    public sealed class Prim
    {
        public string Name, Organ;
        public PrimKind Kind;
        public TissueId Tissue;
        public Vector3 C, R = Vector3.one;
        public Quaternion Rot = Quaternion.identity;
        public float Inner;
        public bool HasClip;
        public Vector3 ClipN;
        public float ClipD;
        public bool Visible = true;
        public bool Skin;                    // part of the outer body surface (rendered as hologram skin)

        Quaternion inv;
        Vector3 invR, invRi;
        public Vector3 BMin, BMax;

        public void Prepare()
        {
            inv = Quaternion.Inverse(Rot);
            invR = new Vector3(1f / R.x, 1f / R.y, 1f / R.z);
            float ii = Inner > 0 ? Inner : 1f;
            invRi = new Vector3(invR.x / ii, invR.y / ii, Kind == PrimKind.Cylinder ? invR.z : invR.z / ii);
            // world AABB of the rotated box that encloses the primitive
            Vector3 mn = new Vector3(1e9f, 1e9f, 1e9f), mx = -mn;
            for (int i = 0; i < 8; i++)
            {
                var corner = new Vector3((i & 1) == 0 ? -R.x : R.x, (i & 2) == 0 ? -R.y : R.y, (i & 4) == 0 ? -R.z : R.z);
                var w = Rot * corner + C;
                mn = Vector3.Min(mn, w); mx = Vector3.Max(mx, w);
            }
            BMin = mn; BMax = mx;
        }

        public bool Contains(Vector3 p)
        {
            if (HasClip && Vector3.Dot(ClipN, p) > ClipD) return false;
            Vector3 q = inv * (p - C);
            float x = q.x * invR.x, y = q.y * invR.y, z = q.z * invR.z;
            switch (Kind)
            {
                case PrimKind.Ellipsoid:
                {
                    float s = x * x + y * y + z * z;
                    if (s > 1f) return false;
                    if (Inner > 0) return s > Inner * Inner;
                    return true;
                }
                case PrimKind.Cylinder:
                {
                    if (z < -1f || z > 1f) return false;
                    float s = x * x + y * y;
                    if (s > 1f) return false;
                    if (Inner > 0) return s > Inner * Inner;
                    return true;
                }
                default:
                    return x >= -1f && x <= 1f && y >= -1f && y <= 1f && z >= -1f && z <= 1f;
            }
        }

        /// <summary>Appends every boundary crossing (outer, inner, caps, clip plane) of the ray to ts.</summary>
        public int Crossings(Vector3 o, Vector3 d, float tMax, float[] ts, int n)
        {
            Vector3 oq = inv * (o - C), dq = inv * d;
            n = Surface(oq, dq, invR, tMax, ts, n);
            if (Inner > 0) n = Surface(oq, dq, invRi, tMax, ts, n);
            if (HasClip)
            {
                float dn = Vector3.Dot(ClipN, d);
                if (Mathf.Abs(dn) > 1e-8f)
                {
                    float t = (ClipD - Vector3.Dot(ClipN, o)) / dn;
                    if (t > 0 && t < tMax) ts[n++] = t;
                }
            }
            return n;
        }

        int Surface(Vector3 oq, Vector3 dq, Vector3 s, float tMax, float[] ts, int n)
        {
            float ox = oq.x * s.x, oy = oq.y * s.y, oz = oq.z * s.z;
            float dx = dq.x * s.x, dy = dq.y * s.y, dz = dq.z * s.z;
            switch (Kind)
            {
                case PrimKind.Ellipsoid:
                {
                    float a = dx * dx + dy * dy + dz * dz;
                    float b = 2f * (ox * dx + oy * dy + oz * dz);
                    float c = ox * ox + oy * oy + oz * oz - 1f;
                    n = Quad(a, b, c, tMax, ts, n);
                    break;
                }
                case PrimKind.Cylinder:
                {
                    float a = dx * dx + dy * dy;
                    float b = 2f * (ox * dx + oy * dy);
                    float c = ox * ox + oy * oy - 1f;
                    if (a > 1e-12f) n = Quad(a, b, c, tMax, ts, n);
                    if (Mathf.Abs(dz) > 1e-9f)
                    {
                        float t1 = (1f - oz) / dz, t2 = (-1f - oz) / dz;
                        if (t1 > 0 && t1 < tMax) ts[n++] = t1;
                        if (t2 > 0 && t2 < tMax) ts[n++] = t2;
                    }
                    break;
                }
                default:
                {
                    if (Mathf.Abs(dx) > 1e-9f) { n = Add((1f - ox) / dx, tMax, ts, n); n = Add((-1f - ox) / dx, tMax, ts, n); }
                    if (Mathf.Abs(dy) > 1e-9f) { n = Add((1f - oy) / dy, tMax, ts, n); n = Add((-1f - oy) / dy, tMax, ts, n); }
                    if (Mathf.Abs(dz) > 1e-9f) { n = Add((1f - oz) / dz, tMax, ts, n); n = Add((-1f - oz) / dz, tMax, ts, n); }
                    break;
                }
            }
            return n;
        }

        static int Add(float t, float tMax, float[] ts, int n)
        {
            if (t > 0 && t < tMax) ts[n++] = t;
            return n;
        }

        static int Quad(float a, float b, float c, float tMax, float[] ts, int n)
        {
            float disc = b * b - 4f * a * c;
            if (disc <= 0) return n;
            float sq = Mathf.Sqrt(disc);
            float t1 = (-b - sq) / (2f * a), t2 = (-b + sq) / (2f * a);
            if (t1 > 0 && t1 < tMax) ts[n++] = t1;
            if (t2 > 0 && t2 < tMax) ts[n++] = t2;
            return n;
        }

        public bool RayHitsBox(Vector3 o, Vector3 invD, float tMax)
        {
            float t0 = 0, t1 = tMax;
            for (int a = 0; a < 3; a++)
            {
                float tn = (BMin[a] - o[a]) * invD[a];
                float tf = (BMax[a] - o[a]) * invD[a];
                if (tn > tf) { float tmp = tn; tn = tf; tf = tmp; }
                if (tn > t0) t0 = tn;
                if (tf < t1) t1 = tf;
                if (t0 > t1) return false;
            }
            return true;
        }
    }

    public sealed class PhantomOptions
    {
        public bool Tumor = true;
        public Vector3 TumorCenter = new Vector3(7.8f, 1.5f, 56f);
        public float TumorRadius = 1.8f;
        public bool Pacemaker;
        public bool HipImplant;
        public bool Contrast;
    }

    /// <summary>Stylised adult anthropomorphic phantom (head, trunk, upper thighs - like the RANDO phantom).</summary>
    public sealed class Phantom
    {
        public readonly List<Prim> Prims = new List<Prim>();   // ascending priority
        public Vector3 BMin, BMax;
        public PhantomOptions Options;

        public Prim Add(string name, string organ, PrimKind k, TissueId t, Vector3 c, Vector3 r, float inner = 0, bool visible = true)
        {
            var p = new Prim { Name = name, Organ = organ, Kind = k, Tissue = t, C = c, R = r, Inner = inner, Visible = visible };
            Prims.Add(p);
            return p;
        }

        public void Prepare()
        {
            BMin = new Vector3(1e9f, 1e9f, 1e9f); BMax = -BMin;
            foreach (var p in Prims)
            {
                p.Prepare();
                BMin = Vector3.Min(BMin, p.BMin); BMax = Vector3.Max(BMax, p.BMax);
            }
        }

        public TissueId TissueAt(Vector3 p)
        {
            for (int i = Prims.Count - 1; i >= 0; i--)
                if (Prims[i].Contains(p)) return Prims[i].Tissue;
            return TissueId.Air;
        }

        public Prim PrimAt(Vector3 p)
        {
            for (int i = Prims.Count - 1; i >= 0; i--)
                if (Prims[i].Contains(p)) return Prims[i];
            return null;
        }

        public bool InOrgan(Vector3 p, string organ)
        {
            var pr = PrimAt(p);
            return pr != null && pr.Organ == organ;
        }

        static Quaternion Axis(Vector3 dir) => Quaternion.FromToRotation(Vector3.forward, dir.normalized);

        public static Phantom BuildAdult(PhantomOptions opt = null)
        {
            opt = opt ?? new PhantomOptions();
            var ph = new Phantom { Options = opt };
            var E = PrimKind.Ellipsoid; var Cy = PrimKind.Cylinder; var Bx = PrimKind.Box;

            // ---- outer body (adipose skin layer) and inner soft tissue
            for (int s = -1; s <= 1; s += 2)
            {
                ph.Add("Thigh" + (s < 0 ? "R" : "L"), "body", Cy, TissueId.Adipose, new Vector3(9.6f * s, 0.6f, -11f), new Vector3(7.6f, 8.0f, 11f)).Skin = true;
                ph.Add("ThighMuscle", "body", Cy, TissueId.Muscle, new Vector3(9.6f * s, 0.6f, -11f), new Vector3(6.4f, 6.8f, 11f), 0, false);
            }
            ph.Add("Trunk", "body", Cy, TissueId.Adipose, new Vector3(0, 0, 33f), new Vector3(19f, 10.5f, 33f)).Skin = true;
            var sh = ph.Add("Shoulders", "body", E, TissueId.Adipose, new Vector3(0, 0, 66f), new Vector3(19f, 10.5f, 7f));
            sh.Skin = true; sh.HasClip = true; sh.ClipN = new Vector3(0, 0, -1); sh.ClipD = -66f;
            ph.Add("TrunkInner", "body", Cy, TissueId.Soft, new Vector3(0, 0, 33f), new Vector3(17.8f, 9.3f, 33f), 0, false);
            var shi = ph.Add("ShouldersInner", "body", E, TissueId.Soft, new Vector3(0, 0, 66f), new Vector3(17.8f, 9.3f, 5.8f), 0, false);
            shi.HasClip = true; shi.ClipN = new Vector3(0, 0, -1); shi.ClipD = -66f;
            ph.Add("Neck", "body", Cy, TissueId.Soft, new Vector3(0, -0.5f, 73f), new Vector3(5.6f, 5.6f, 6.5f)).Skin = true;
            ph.Add("Head", "body", E, TissueId.Soft, new Vector3(0, 0.6f, 87.5f), new Vector3(7.8f, 9.8f, 11.2f)).Skin = true;

            // ---- organs first (bones and the cord get higher priority afterwards)
            var brain = ph.Add("Brain", "brain", E, TissueId.Brain, new Vector3(0, 0.4f, 88f), new Vector3(6.65f, 8.45f, 9.7f));
            brain.HasClip = true; brain.ClipN = new Vector3(0, 0, -1); brain.ClipD = -81.5f;
            for (int s = -1; s <= 1; s += 2)
                ph.Add("Ventricle", "csf", E, TissueId.Water, new Vector3(1.3f * s, 0.6f, 89.5f), new Vector3(0.7f, 3.0f, 1.3f));
            ph.Add("Sinus", "airway", E, TissueId.Air, new Vector3(0, 7.2f, 84f), new Vector3(1.6f, 1.2f, 1.5f));
            for (int s = -1; s <= 1; s += 2)
            {
                var lu = ph.Add("Lung" + (s < 0 ? "R" : "L"), s < 0 ? "lungR" : "lungL", E, TissueId.Lung, new Vector3(8.6f * s, 0.2f, 52.5f), new Vector3(5.9f, 7.6f, 14.5f));
                lu.HasClip = true; lu.ClipN = new Vector3(0, 0, -1); lu.ClipD = -40.5f;
            }
            ph.Add("Trachea", "airway", Cy, TissueId.Air, new Vector3(0, 1.0f, 69.5f), new Vector3(1.0f, 1.0f, 9.5f));
            for (int s = -1; s <= 1; s += 2)
            {
                var b = ph.Add("Bronchus", "airway", Cy, TissueId.Air, new Vector3(2.0f * s, 1.0f, 57.6f), new Vector3(0.6f, 0.6f, 3.2f));
                b.Rot = Axis(new Vector3(s * 0.62f, 0, -0.78f));
            }
            TissueId bloodT = opt.Contrast ? TissueId.ContrastBlood : TissueId.Blood;
            var heart = ph.Add("Heart", "heart", E, bloodT, new Vector3(1.6f, 3.0f, 46.5f), new Vector3(5.4f, 4.6f, 6.6f));
            heart.Rot = Quaternion.AngleAxis(-40f, Vector3.up);
            ph.Add("Aorta", "aorta", Cy, bloodT, new Vector3(1.6f, -2.6f, 40f), new Vector3(1.2f, 1.2f, 14f));
            var liver = ph.Add("Liver", "liver", E, TissueId.Soft, new Vector3(-5.5f, 1.0f, 33.5f), new Vector3(10.5f, 8.0f, 7.5f));
            liver.HasClip = true; liver.ClipN = new Vector3(0, 0, 1); liver.ClipD = 40f;
            ph.Add("Stomach", "stomach", E, TissueId.Soft, new Vector3(7.5f, 2.5f, 33.5f), new Vector3(4.5f, 3.5f, 6.0f));
            ph.Add("GastricBubble", "gas", E, TissueId.Air, new Vector3(8.5f, 1.5f, 37.8f), new Vector3(3.0f, 2.3f, 1.8f));
            ph.Add("Spleen", "spleen", E, TissueId.Soft, new Vector3(11.0f, -3.5f, 36.0f), new Vector3(3.0f, 2.0f, 5.5f));
            for (int s = -1; s <= 1; s += 2)
            {
                var k = ph.Add("Kidney" + (s < 0 ? "R" : "L"), "kidney", E, TissueId.Soft, new Vector3(6.2f * s, -5.6f, 28.5f), new Vector3(4.0f, 1.6f, 5.5f));
                k.Rot = Quaternion.AngleAxis(15f * s, Vector3.forward);
            }
            ph.Add("BowelGas", "gas", E, TissueId.Air, new Vector3(-9f, 2.5f, 18f), new Vector3(2.2f, 1.5f, 1.5f));
            ph.Add("BowelGas", "gas", E, TissueId.Air, new Vector3(9.5f, 2.0f, 22f), new Vector3(2.0f, 1.5f, 1.4f));
            ph.Add("BowelGas", "gas", E, TissueId.Air, new Vector3(0.5f, 4.0f, 15f), new Vector3(3.0f, 1.4f, 1.2f));
            ph.Add("Bladder", "bladder", E, TissueId.Water, new Vector3(0, 4.0f, 6.5f), new Vector3(4.0f, 3.5f, 3.5f));

            // ---- skeleton
            ph.Add("Skull", "skull", E, TissueId.CorticalBone, new Vector3(0, 0.6f, 87.5f), new Vector3(7.35f, 9.35f, 10.75f), 0.91f);
            for (int s = -1; s <= 1; s += 2)
                ph.Add("Eye" + (s < 0 ? "R" : "L"), "eye", E, TissueId.Water, new Vector3(3.2f * s, 7.9f, 87.6f), new Vector3(1.2f, 1.2f, 1.2f));
            ph.Add("Lumbar", "spine", Cy, TissueId.SpongyBone, new Vector3(0, -4.4f, 13f), new Vector3(2.3f, 1.8f, 12f));
            ph.Add("Thoracic", "spine", Cy, TissueId.SpongyBone, new Vector3(0, -4.6f, 44.5f), new Vector3(1.9f, 1.6f, 19.5f));
            ph.Add("Cervical", "spine", Cy, TissueId.SpongyBone, new Vector3(0, -2.0f, 71.5f), new Vector3(1.4f, 1.2f, 7.5f));
            ph.Add("SpineArch", "spine", Cy, TissueId.CorticalBone, new Vector3(0, -8.4f, 33f), new Vector3(2.2f, 1.25f, 31f));
            for (int i = 0; i < 12; i++)
            {
                // ribs slope downwards anteriorly (~20°), so anterior and posterior arcs project at different heights
                var rib = ph.Add("Rib", "ribs", Cy, TissueId.MixedBone, new Vector3(0, -1.0f, 40.5f + i * 2.4f), new Vector3(16.2f, 9.0f, 0.45f), 0.92f);
                rib.Rot = Quaternion.AngleAxis(-27f, Vector3.right);
            }
            ph.Add("Sternum", "sternum", Bx, TissueId.MixedBone, new Vector3(0, 8.3f, 56f), new Vector3(1.6f, 0.6f, 8f));
            for (int s = -1; s <= 1; s += 2)
            {
                var cl = ph.Add("Clavicle", "clavicle", Cy, TissueId.CorticalBone, new Vector3(8.6f * s, 5.2f, 67.2f), new Vector3(0.75f, 0.75f, 7.6f));
                cl.Rot = Axis(new Vector3(s * 0.98f, -0.1f, 0.17f));
                var sc = ph.Add("Scapula", "scapula", Bx, TissueId.MixedBone, new Vector3(10.6f * s, -7.6f, 57f), new Vector3(4.5f, 0.35f, 6.5f));
                sc.Rot = Quaternion.AngleAxis(-22f * s, Vector3.forward);
            }
            var pel = ph.Add("Pelvis", "pelvis", Cy, TissueId.MixedBone, new Vector3(0, -1.0f, 11f), new Vector3(13.5f, 9.0f, 9f), 0.9f);
            pel.HasClip = true; pel.ClipN = new Vector3(0, 1, 0); pel.ClipD = 1.5f;
            ph.Add("Sacrum", "pelvis", Bx, TissueId.SpongyBone, new Vector3(0, -6.8f, 7f), new Vector3(3.8f, 1.4f, 5.5f));
            for (int s = -1; s <= 1; s += 2)
            {
                ph.Add("FemurHead", "femur", E, TissueId.SpongyBone, new Vector3(9.2f * s, 0.2f, 1.6f), new Vector3(2.4f, 2.4f, 2.4f));
                ph.Add("Femur", "femur", Cy, TissueId.CorticalBone, new Vector3(9.8f * s, 0.6f, -11f), new Vector3(1.45f, 1.45f, 11f), 0.6f);
            }
            // soft structures inside the skeleton
            for (int i = 0; i < 6; i++)
                ph.Add("Disc", "disc", Cy, TissueId.Soft, new Vector3(0, -4.4f, 4.2f + i * 4f), new Vector3(2.35f, 1.85f, 0.32f), 0, false);
            for (int i = 0; i < 12; i++)
                ph.Add("Disc", "disc", Cy, TissueId.Soft, new Vector3(0, -4.6f, 27.6f + i * 3.15f), new Vector3(1.95f, 1.65f, 0.28f), 0, false);
            for (int i = 0; i < 6; i++)
                ph.Add("Disc", "disc", Cy, TissueId.Soft, new Vector3(0, -2.0f, 66f + i * 2.1f), new Vector3(1.45f, 1.25f, 0.22f), 0, false);
            ph.Add("SpinalCord", "cord", Cy, TissueId.Soft, new Vector3(0, -6.9f, 40f), new Vector3(0.75f, 0.75f, 38f));
            if (opt.HipImplant)
            {
                ph.Add("ImplantHead", "implant", E, TissueId.Titanium, new Vector3(-9.2f, 0.2f, 1.6f), new Vector3(2.1f, 2.1f, 2.1f));
                ph.Add("ImplantStem", "implant", Cy, TissueId.Titanium, new Vector3(-9.7f, 0.5f, -8f), new Vector3(0.75f, 0.75f, 9f));
            }

            // ---- options
            if (opt.Tumor)
                ph.Add("Tumor", "tumor", E, TissueId.Tumor, opt.TumorCenter, Vector3.one * opt.TumorRadius);
            if (opt.Pacemaker)
            {
                ph.Add("Pacemaker", "implant", Bx, TissueId.Titanium, new Vector3(7.5f, 8.5f, 61.5f), new Vector3(2.4f, 0.4f, 2.0f));
                var a = new Vector3(6.0f, 8.2f, 60.5f); var b = new Vector3(2.0f, 3.6f, 47.5f);
                var lead = ph.Add("PacemakerLead", "implant", Cy, TissueId.Titanium, (a + b) * 0.5f, new Vector3(0.12f, 0.12f, (a - b).magnitude * 0.5f));
                lead.Rot = Axis(a - b);
            }
            ph.Prepare();
            return ph;
        }

        /// <summary>
        /// The adult phantom rescaled to another body habitus: (sx, sy, sz) scale lateral, AP and vertical sizes,
        /// extraFatCm thickens the outer adipose layer of the trunk / thighs (anterior a bit more: abdominal fat).
        /// </summary>
        public static Phantom BuildPatient(PhantomOptions opt, float sx, float sy, float sz, float extraFatCm = 0)
        {
            var ph = BuildAdult(opt);
            var k = new Vector3(sx, sy, sz);
            foreach (var p in ph.Prims)
            {
                p.C = Vector3.Scale(p.C, k);
                p.R = Vector3.Scale(p.R, k);
                if (p.HasClip)
                {
                    var n = p.ClipN;
                    p.ClipD *= Mathf.Abs(n.x) * sx + Mathf.Abs(n.y) * sy + Mathf.Abs(n.z) * sz;
                }
                if (extraFatCm > 0 && p.Skin && p.Tissue == TissueId.Adipose)
                {
                    bool trunk = p.Name == "Trunk" || p.Name == "Shoulders";
                    p.R.x += extraFatCm * (trunk ? 1f : 0.55f);
                    p.R.y += extraFatCm * (trunk ? 1.15f : 0.55f);
                    if (p.Name == "Trunk") p.C.y += extraFatCm * 0.25f;
                }
            }
            ph.Prepare();
            return ph;
        }

        /// <summary>Lateral and AP outer diameters (cm) of the body at height z (phys frame).</summary>
        public void Diameters(float z, out float lat, out float ap)
        {
            float x0 = 0, x1 = 0, y0 = 0, y1 = 0;
            for (float x = -40; x <= 40; x += 0.25f) if (TissueAt(new Vector3(x, 0, z)) != TissueId.Air) { if (x0 == 0 && x1 == 0) x0 = x; x1 = x; }
            for (float y = -40; y <= 40; y += 0.25f) if (TissueAt(new Vector3(0, y, z)) != TissueId.Air) { if (y0 == 0 && y1 == 0) y0 = y; y1 = y; }
            lat = x1 - x0; ap = y1 - y0;
        }
    }

    /// <summary>Per-thread ray tracer: path length through each tissue along a ray.</summary>
    public sealed class PhantomTracer
    {
        readonly Phantom ph;
        readonly float[] ts = new float[512];
        readonly int[] cand = new int[256];

        public PhantomTracer(Phantom p) { ph = p; }

        /// <summary>
        /// Accumulates path length (cm) per TissueId into len (length TissueId.Count, not cleared).
        /// o, d in phys frame (cm), d normalised. Returns total path inside the body.
        /// </summary>
        public float Trace(Vector3 o, Vector3 d, float tMax, float[] len)
        {
            var invD = new Vector3(1f / (Mathf.Abs(d.x) < 1e-9f ? 1e-9f : d.x),
                                   1f / (Mathf.Abs(d.y) < 1e-9f ? 1e-9f : d.y),
                                   1f / (Mathf.Abs(d.z) < 1e-9f ? 1e-9f : d.z));
            // quick reject with the phantom bounds
            if (!BoxHit(ph.BMin, ph.BMax, o, invD, tMax)) return 0;
            var prims = ph.Prims;
            int nc = 0, nt = 0;
            for (int i = 0; i < prims.Count; i++)
            {
                if (!prims[i].RayHitsBox(o, invD, tMax)) continue;
                cand[nc++] = i;
                nt = prims[i].Crossings(o, d, tMax, ts, nt);
                if (nt > ts.Length - 16) break;
            }
            if (nt < 2) return 0;
            Array.Sort(ts, 0, nt);
            float inside = 0;
            for (int k = 0; k < nt - 1; k++)
            {
                float a = ts[k], b = ts[k + 1];
                float seg = b - a;
                if (seg < 1e-5f) continue;
                var mid = o + d * (0.5f * (a + b));
                int tissue = -1;
                for (int c = nc - 1; c >= 0; c--)
                {
                    if (prims[cand[c]].Contains(mid)) { tissue = (int)prims[cand[c]].Tissue; break; }
                }
                if (tissue < 0) continue;
                len[tissue] += seg;
                inside += seg;
            }
            return inside;
        }

        static bool BoxHit(Vector3 mn, Vector3 mx, Vector3 o, Vector3 invD, float tMax)
        {
            float t0 = 0, t1 = tMax;
            for (int a = 0; a < 3; a++)
            {
                float tn = (mn[a] - o[a]) * invD[a];
                float tf = (mx[a] - o[a]) * invD[a];
                if (tn > tf) { float tmp = tn; tn = tf; tf = tmp; }
                if (tn > t0) t0 = tn;
                if (tf < t1) t1 = tf;
                if (t0 > t1) return false;
            }
            return true;
        }
    }
}
