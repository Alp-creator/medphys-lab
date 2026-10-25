using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace MedPhys.Physics
{
    public enum PlanModality { Photon6, Photon18, Proton }

    public sealed class PlanBeam
    {
        public float Angle;          // IEC-like gantry angle: 0 = anterior, 90 = patient's left, 180 = posterior, 270 = right
        public float Weight = 1f;
        public float WidthCm = 6f;   // field width at the isocentre (in the slice plane)
        public PlanModality Modality = PlanModality.Photon6;
        public PlanBeam Clone() => (PlanBeam)MemberwiseClone();
    }

    [Flags]
    public enum Struct : byte { None = 0, Body = 1, Ptv = 2, Gtv = 4, Cord = 8, Heart = 16, LungL = 32, LungR = 64 }

    /// <summary>
    /// 2D treatment-planning dose engine on an axial slice of the phantom.
    /// Photons: ray tracing with equivalent path length (radiological depth through the relative electron
    /// density map) × tissue-maximum ratio × inverse square × penumbra profile.
    /// Protons: spread-out Bragg peak fitted to the PTV along each beam (range margin 3.5 % + 1 mm).
    /// Dose is normalised so that the mean PTV dose equals the prescription.
    /// </summary>
    public sealed class PlanGrid
    {
        public const int N = 160;
        public const float Size = 40f;
        public const float H = Size / N;
        public const float Sad = 100f;

        public float SliceZ;
        public Vector2 Iso;
        public float PtvMargin;
        public readonly float[] Rho = new float[N * N];
        public readonly byte[] Mask = new byte[N * N];
        public readonly float[] Dose = new float[N * N];
        public readonly float[] Hu = new float[N * N];
        public float RxGy = 60f;
        public int Fractions = 30;

        public Vector2 Pos(int i, int j) => new Vector2(-Size / 2 + (i + 0.5f) * H, -Size / 2 + (j + 0.5f) * H);

        public static PlanGrid Build(Phantom ph, float z, Vector3 tumorCenter, float tumorRadius, float margin)
        {
            var g = new PlanGrid { SliceZ = z, Iso = new Vector2(tumorCenter.x, tumorCenter.y), PtvMargin = margin };
            // relative electron density ≈ ρ · (μ/ρ at 2 MeV, Compton-dominated) / water
            var water = Nist.Get(Nist.Id.Water);
            var rel = new float[(int)TissueId.Count];
            var hu = new float[(int)TissueId.Count];
            double muW = water.Mu(0.07);
            for (int t = 0; t < rel.Length; t++)
            {
                var ts = Tissues.Get((TissueId)t);
                rel[t] = (float)(ts.Mu(2.0) / water.Mu(2.0));
                hu[t] = (float)Math.Max(-1000, Math.Min(3000, 1000 * (ts.Mu(0.07) - muW) / muW));
            }
            float rPtv = tumorRadius + margin;
            for (int j = 0; j < N; j++)
                for (int i = 0; i < N; i++)
                {
                    var p2 = g.Pos(i, j);
                    var p = new Vector3(p2.x, p2.y, z);
                    var pr = ph.PrimAt(p);
                    int k = j * N + i;
                    if (pr == null) { g.Rho[k] = 0.001f; g.Hu[k] = -1000; continue; }
                    g.Rho[k] = rel[(int)pr.Tissue];
                    g.Hu[k] = hu[(int)pr.Tissue];
                    Struct m = Struct.Body;
                    float dt = Vector3.Distance(p, tumorCenter);
                    if (dt <= tumorRadius) m |= Struct.Gtv;
                    if (dt <= rPtv) m |= Struct.Ptv;
                    if (pr.Organ == "cord") m |= Struct.Cord;
                    if (pr.Organ == "heart") m |= Struct.Heart;
                    if ((m & Struct.Gtv) == 0)
                    {
                        var lungPrim = ph.Prims.Find(q => q.Organ == "lungL" && q.Contains(p));
                        if (lungPrim != null && pr.Tissue == TissueId.Lung) m |= Struct.LungL;
                        var lungR = ph.Prims.Find(q => q.Organ == "lungR" && q.Contains(p));
                        if (lungR != null && pr.Tissue == TissueId.Lung) m |= Struct.LungR;
                    }
                    g.Mask[k] = (byte)m;
                }
            return g;
        }

        static Vector2 SourceDir(float angleDeg)
        {
            float a = angleDeg * Mathf.Deg2Rad;
            return new Vector2(Mathf.Sin(a), Mathf.Cos(a));
        }

        /// <summary>Radiological depth (cm water-equivalent) from the body surface to point p along a ray from src.</summary>
        float RadDepth(Vector2 src, Vector2 p)
        {
            Vector2 dir = (src - p);
            float len = dir.magnitude;
            dir /= len;
            float step = 0.2f, acc = 0;
            float maxL = Mathf.Min(len, Size * 0.75f);
            for (float s = step * 0.5f; s < maxL; s += step)
            {
                var q = p + dir * s;
                int i = (int)((q.x + Size / 2) / H), j = (int)((q.y + Size / 2) / H);
                if (i < 0 || j < 0 || i >= N || j >= N) break;
                acc += Rho[j * N + i] * step;
            }
            return acc;
        }

        static float Penumbra(float l, float w, float sigma)
        {
            float a = (w * 0.5f - l) / (sigma * 1.41421f), b = (w * 0.5f + l) / (sigma * 1.41421f);
            return 0.5f * (Erf(a) + Erf(b));
        }

        static float Erf(float x) => 1f - (float)DepthDose.Erfc(x);

        readonly Dictionary<string, DepthDose.Sobp> sobpCache = new Dictionary<string, DepthDose.Sobp>();

        public void Compute(IList<PlanBeam> beams)
        {
            Array.Clear(Dose, 0, Dose.Length);
            if (beams.Count == 0) return;
            var tmp = new float[N * N];
            foreach (var b in beams)
            {
                if (b.Weight <= 0) continue;
                var sdir = SourceDir(b.Angle);
                var src = Iso + sdir * Sad;
                var t = new Vector2(sdir.y, -sdir.x);
                DepthDose.Sobp sobp = null;
                if (b.Modality == PlanModality.Proton)
                {
                    float dmin = float.MaxValue, dmax = 0;
                    for (int k = 0; k < Mask.Length; k += 1)
                    {
                        if ((Mask[k] & (byte)Struct.Ptv) == 0) continue;
                        var p = Pos(k % N, k / N);
                        float l = Vector2.Dot(p - src, t) * Sad / Mathf.Max(Vector2.Dot(p - src, -sdir), 1);
                        if (Mathf.Abs(l) > b.WidthCm / 2) continue;
                        float d = RadDepth(src, p);
                        dmin = Mathf.Min(dmin, d); dmax = Mathf.Max(dmax, d);
                    }
                    if (dmax <= 0) continue;
                    float distal = dmax * 1.035f + 0.1f, proximal = Mathf.Max(0.5f, dmin * 0.965f - 0.1f);
                    string key = Mathf.Round(proximal * 4) + "_" + Mathf.Round(distal * 4);
                    if (!sobpCache.TryGetValue(key, out sobp))
                    {
                        sobp = DepthDose.BuildSobp(Mathf.Round(proximal * 4) / 4f, Mathf.Round(distal * 4) / 4f);
                        sobpCache[key] = sobp;
                    }
                }
                double mv = b.Modality == PlanModality.Photon18 ? 18 : 6;
                float sigma = b.Modality == PlanModality.Proton ? 0.45f : 0.35f;
                Parallel.For(0, N, j =>
                {
                    for (int i = 0; i < N; i++)
                    {
                        int k = j * N + i;
                        if ((Mask[k] & (byte)Struct.Body) == 0) { tmp[k] = 0; continue; }
                        var p = Pos(i, j);
                        var v = p - src;
                        float a = Vector2.Dot(v, -sdir);
                        float l = Vector2.Dot(v, t);
                        float lIso = l * Sad / Mathf.Max(a, 1f);
                        float f = Penumbra(lIso, b.WidthCm, sigma);
                        if (f < 1e-3f) { tmp[k] = 0; continue; }
                        float d = RadDepth(src, p);
                        float dose;
                        if (sobp != null) dose = (float)sobp.At(d) * f;
                        else
                        {
                            double tmr = DepthDose.PhotonTmr(mv, d, b.WidthCm);
                            dose = (float)(tmr * (Sad * Sad) / (a * a)) * f;
                            // head scatter / transmission outside the field ≈ 1.5 %
                            dose += 0.015f * (float)DepthDose.PhotonTmr(mv, d, b.WidthCm) * (1 - f);
                        }
                        tmp[k] = dose * b.Weight;
                    }
                });
                for (int k = 0; k < Dose.Length; k++) Dose[k] += tmp[k];
            }
            // normalise: mean PTV dose = prescription
            double sum = 0; int n = 0;
            for (int k = 0; k < Dose.Length; k++) if ((Mask[k] & (byte)Struct.Ptv) != 0) { sum += Dose[k]; n++; }
            if (n == 0 || sum <= 0) return;
            float scale = (float)(RxGy / (sum / n));
            for (int k = 0; k < Dose.Length; k++) Dose[k] *= scale;
        }

        // ------------------------------------------------------------------ evaluation
        public float[] Values(Struct s)
        {
            var list = new List<float>();
            for (int k = 0; k < Dose.Length; k++) if ((Mask[k] & (byte)s) != 0) list.Add(Dose[k]);
            list.Sort();
            return list.ToArray();
        }

        /// <summary>Dose received by at least 'pct' % of the structure volume (D_pct).</summary>
        public static float Dx(float[] sorted, float pct)
        {
            if (sorted.Length == 0) return 0;
            int idx = Mathf.Clamp((int)((1 - pct / 100f) * sorted.Length), 0, sorted.Length - 1);
            return sorted[idx];
        }

        /// <summary>Percent volume receiving at least d Gy (V_d).</summary>
        public static float Vx(float[] sorted, float d)
        {
            if (sorted.Length == 0) return 0;
            int c = 0; foreach (var v in sorted) if (v >= d) c++;
            return 100f * c / sorted.Length;
        }

        public static float Mean(float[] v)
        {
            if (v.Length == 0) return 0;
            double s = 0; foreach (var x in v) s += x; return (float)(s / v.Length);
        }

        /// <summary>Cumulative DVH: (dose Gy, volume %) pairs.</summary>
        public static List<Vector2> Dvh(float[] sorted, float maxDose, int bins = 80)
        {
            var r = new List<Vector2>();
            if (sorted.Length == 0) return r;
            for (int b = 0; b <= bins; b++)
            {
                float d = maxDose * b / bins;
                r.Add(new Vector2(d, Vx(sorted, d)));
            }
            return r;
        }

        /// <summary>Paddick conformity index at the given isodose (Gy).</summary>
        public float Conformity(float isodose)
        {
            int tv = 0, piv = 0, tvpiv = 0;
            for (int k = 0; k < Dose.Length; k++)
            {
                bool inT = (Mask[k] & (byte)Struct.Ptv) != 0, inP = Dose[k] >= isodose;
                if (inT) tv++;
                if (inP) piv++;
                if (inT && inP) tvpiv++;
            }
            if (tv == 0 || piv == 0) return 0;
            return (float)tvpiv * tvpiv / ((float)tv * piv);
        }
    }
}
