using System;
using System.Collections.Generic;
using UnityEngine;
using MedPhys.Physics;
using MedPhys.UI;
using static MedPhys.Core.Loc;

namespace MedPhys.Cases
{
    /// <summary>Small physics helpers used by the case scoring.</summary>
    public static class CasePhysics
    {
        /// <summary>CTDIvol (mGy, 32 cm body phantom) for a helical scan: nCTDIw(kVp) · mAs / 100 / pitch.</summary>
        public static double CtdiVol(double kvp, double mas, double pitch) => 7.0 * Math.Pow(kvp / 120.0, 2.6) * mas / 100.0 / Math.Max(pitch, 0.3);

        /// <summary>AAPM Report 204 size conversion factor (32 cm phantom) from the effective diameter (cm).</summary>
        public static double SsdeFactor(double dEffCm) => 3.704369 * Math.Exp(-0.03671937 * dEffCm);

        /// <summary>
        /// Electron depth dose for a clinical beam of nominal energy E (MeV), shaped on typical linac data:
        /// R50 ≈ E/2.55, Rp ≈ E/2.03 (6 MeV: R90 ≈ 1.7, R50 ≈ 2.35, Rp ≈ 2.95 cm), surface dose 72 + 1.1·E %.
        /// </summary>
        public static double ElectronPdd(double e, double d)
        {
            double r50 = e / 2.55, rp = e / 2.03;
            double surface = Math.Min(93, 72 + 1.1 * e);
            double tail = Math.Max(0.5, 0.18 * e);
            double dmax = Math.Min(0.6 * r50, 0.15 + 0.2 * e);
            double sigma = 2 * (rp - r50) / Math.Sqrt(2 * Math.PI);
            Func<double, double> fall = x => tail + (100 - tail) * 0.5 * DepthDose.Erfc((x - r50) / (sigma * Math.Sqrt(2)));
            if (d < dmax) return surface + (100 - surface) * Math.Sin(d / dmax * Math.PI / 2);
            return Math.Max(tail, 100 * fall(d) / fall(dmax));
        }

        public static double Decay(double a0, double halfLife, double t) => a0 * Math.Exp(-Math.Log(2) * t / halfLife);

        /// <summary>Spin-echo signal S = PD · (1 − e^(−TR/T1)) · e^(−TE/T2).</summary>
        public static double SeSignal(double pd, double t1, double t2, double tr, double te) => pd * (1 - Math.Exp(-tr / t1)) * Math.Exp(-te / t2);
    }

    // ====================================================================== X-ray
    /// <summary>PA chest radiograph of a case patient (scaled phantom): ray-traced image, EI/DI and dose.</summary>
    public sealed class XRayCaseSim : CaseSim
    {
        readonly Phantom ph;
        readonly float centerZ, detY, effCoeff;
        RadiographyJob job;
        int seed;
        public override string ButtonTr => "ÇEK (ışınla)";
        public override string ButtonEn => "EXPOSE";

        public XRayCaseSim(Phantom ph, float centerZ, float effCoeffMSvPerGyCm2)
        {
            this.ph = ph; this.centerZ = centerZ; effCoeff = effCoeffMSvPerGyCm2;
            ph.Diameters(centerZ, out _, out float ap);
            detY = ap * 0.5f + 3f;
            Tex = UIKit.NewTex(256, 256);
            var c = new Color32[256 * 256];
            for (int i = 0; i < c.Length; i++) c[i] = new Color32(10, 16, 24, 255);
            Tex.SetPixels32(c); Tex.Apply();
        }

        public override void Start(Vals v)
        {
            bool cu = v.I("filt") == 1;
            float sid = v["sid"] > 0 ? v["sid"] : 180f;
            job = new RadiographyJob
            {
                Ph = ph,
                S = new RadiographySettings
                {
                    Kvp = v["kvp"], Mas = v["mas"], AlMm = cu ? 2.7f : 1.7f, CuMm = cu ? 0.1f : 0f, Grid = v.B("grid"), Res = 256,
                    FieldWcm = v["fw"], FieldHcm = v["fh"], EffDoseCoeff = effCoeff, Seed = ++seed,
                },
                Source = new Vector3(0, detY - sid, centerZ),
                DetCenter = new Vector3(0, detY, centerZ),
                U = new Vector3(-1, 0, 0), V = new Vector3(0, 0, 1),
            };
            job.Start();
            Running = true; Progress = 0;
        }

        public override bool Poll(Vals v)
        {
            if (job == null) return false;
            Progress = job.Done ? 1 : job.RowsDone / 256f;
            if (!job.Done) return false;
            var j = job; job = null; Running = false;
            if (j.Failed) { Debug.LogError(j.Error); return false; }
            // radiological convention: the patient's right on the viewer's left (U points to the patient's right)
            var disp = new float[j.Display.Length];
            for (int y = 0; y < j.Res; y++)
                for (int x = 0; x < j.Res; x++) disp[y * j.Res + x] = j.Display[y * j.Res + (j.Res - 1 - x)];
            UIKit.Gray(Tex, disp, j.Res, j.Res);
            MarkL = "R"; MarkR = "L";
            v.Sim["esak"] = j.EsakMGy * 1000.0;
            v.Sim["dap"] = j.DapGyCm2;
            v.Sim["E"] = j.EffDoseMSv;
            v.Sim["di"] = j.Di;
            v.Sim["ei"] = j.Ei;
            v.Sim["snr"] = j.Snr;
            v.Sim["thick"] = j.ThicknessCm;
            v.Sim["hvl"] = j.Spectrum.HvlAlMm();
            v.HasSim = true;
            return true;
        }
    }

    // ====================================================================== CT
    /// <summary>Axial CT slice of a case patient: noisy FBP image, ROI noise in an organ, CTDIvol / SSDE.</summary>
    public sealed class CTCaseSim : CaseSim
    {
        readonly Phantom ph;
        readonly float sliceZ, fov;
        readonly string roiOrgan;
        public readonly float DEff, Lat, Ap;
        CTJob job;
        int seed;
        public static readonly float[] Kvps = { 80, 100, 120, 140 };
        public override string ButtonTr => "TARA";
        public override string ButtonEn => "SCAN";

        public CTCaseSim(Phantom ph, float sliceZ, string roiOrgan, float fov = 50)
        {
            this.ph = ph; this.sliceZ = sliceZ; this.roiOrgan = roiOrgan; this.fov = fov;
            ph.Diameters(sliceZ, out Lat, out Ap);
            DEff = Mathf.Sqrt(Lat * Ap);
            Tex = UIKit.NewTex(256, 256);
            var c = new Color32[256 * 256];
            for (int i = 0; i < c.Length; i++) c[i] = new Color32(0, 0, 0, 255);
            Tex.SetPixels32(c); Tex.Apply();
        }

        public override void Start(Vals v)
        {
            float kvp = Kvps[Mathf.Clamp(v.I("kv"), 0, 3)];
            float pitch = Mathf.Max(0.3f, v["pitch"] > 0 ? v["pitch"] : 1f);
            job = new CTJob
            {
                Ph = ph,
                S = new CTSettings { SliceZ = sliceZ, Kvp = kvp, Mas = v["mas"] / pitch, NProj = 360, NDet = 256, FovCm = fov, Res = 256, Filter = CTFilter.SheppLogan, Seed = ++seed },
            };
            job.Start();
            Running = true; Progress = 0;
        }

        public override bool Poll(Vals v)
        {
            if (job == null) return false;
            Progress = job.Done ? 1 : job.ViewsDone / 360f;
            if (!job.Done) return false;
            var j = job; job = null; Running = false;
            if (j.Failed) { Debug.LogError(j.Error); return false; }
            int n = j.S.Res;
            // soft-tissue window W 400 / L 40
            var img = new float[n * n];
            for (int i = 0; i < img.Length; i++) img[i] = Mathf.Clamp01((j.Hu[i] - (40 - 200)) / 400f);
            UIKit.Gray(Tex, img, n, n);
            MarkL = "R"; MarkR = "L";
            // ROI: organ pixels at least 1 cm inside the organ
            double s = 0, s2 = 0; int cnt = 0;
            for (int iy = 0; iy < n; iy++)
                for (int ix = 0; ix < n; ix++)
                {
                    var p = j.PixelToPhys(ix, iy);
                    var q = new Vector3(p.x, p.y, sliceZ);
                    if (!ph.InOrgan(q, roiOrgan)) continue;
                    if (!ph.InOrgan(q + new Vector3(1, 0, 0), roiOrgan) || !ph.InOrgan(q - new Vector3(1, 0, 0), roiOrgan) ||
                        !ph.InOrgan(q + new Vector3(0, 1, 0), roiOrgan) || !ph.InOrgan(q - new Vector3(0, 1, 0), roiOrgan)) continue;
                    double h = j.Hu[iy * n + ix];
                    s += h; s2 += h * h; cnt++;
                }
            double mean = cnt > 0 ? s / cnt : 0;
            double sd = cnt > 1 ? Math.Sqrt(Math.Max(0, s2 / cnt - mean * mean)) : 0;
            float kvp = j.S.Kvp;
            float pitch = Mathf.Max(0.3f, v["pitch"] > 0 ? v["pitch"] : 1f);
            double ctdi = CasePhysics.CtdiVol(kvp, v["mas"], pitch);
            v.Sim["noise"] = sd;
            v.Sim["mean"] = mean;
            v.Sim["ctdi"] = ctdi;
            v.Sim["ssde"] = ctdi * CasePhysics.SsdeFactor(DEff);
            v.HasSim = true;
            return true;
        }
    }

    // ====================================================================== MRI
    /// <summary>Sagittal knee MR image from spin-echo contrast (TR, TE, fat saturation, B0) with a meniscal tear and bone-marrow oedema.</summary>
    public sealed class KneeMriSim : CaseSim
    {
        public const int N = 160;
        // PD, T1 (ms, 1.5 T), T2 (ms)
        public static readonly (string tr, string en, double pd, double t1, double t2)[] Tis =
        {
            ("Hava", "Air", 0, 1, 1),
            ("Yağ", "Fat", 0.90, 260, 80),
            ("Kas", "Muscle", 0.75, 1000, 35),
            ("Kortikal kemik", "Cortical bone", 0.02, 1000, 1),
            ("Kıkırdak", "Cartilage", 0.80, 1050, 40),
            ("Menisküs", "Meniscus", 0.45, 900, 12),
            ("Eklem sıvısı", "Joint fluid", 1.00, 3000, 700),
            ("Menisküs yırtığı", "Meniscal tear", 0.95, 2400, 350),
            ("İlik ödemi", "Marrow oedema", 0.95, 1100, 85),
            ("Tendon / bağ", "Tendon / ligament", 0.30, 600, 8),
            ("Kemik iliği", "Bone marrow", 0.90, 290, 75),
        };
        public const int AIR = 0, FAT = 1, MUS = 2, COR = 3, CART = 4, MEN = 5, FLU = 6, TEAR = 7, EDEMA = 8, TEN = 9, MARROW = 10;
        static int[] map;
        int seed;
        public override string ButtonTr => "SEKANSI ÇALIŞTIR";
        public override string ButtonEn => "RUN SEQUENCE";

        public KneeMriSim()
        {
            if (map == null) map = BuildMap();
            Tex = UIKit.NewTex(N, N);
            var c = new Color32[N * N];
            for (int i = 0; i < c.Length; i++) c[i] = new Color32(0, 0, 0, 255);
            Tex.SetPixels32(c); Tex.Apply();
        }

        public static double Signal(int t, double tr, double te, bool fatSat, double b0)
        {
            double k1 = b0 > 2 ? 1.25 : 1.0;   // T1 lengthens at 3 T
            if (t == MARROW)
            {
                // yellow marrow: ~80 % fat + 20 % water; fat saturation removes only the fat part
                double fat = Signal(FAT, tr, te, fatSat, b0);
                double water = CasePhysics.SeSignal(0.85, 900 * k1, 40, tr, te);
                return 0.8 * fat + 0.2 * water;
            }
            var x = Tis[t];
            double s = CasePhysics.SeSignal(x.pd, x.t1 * k1, x.t2, tr, te);
            if (fatSat && t == FAT) s *= 0.08;
            return s;
        }

        public static double Sigma(double b0) => 0.022 / (b0 > 2 ? 1.8 : 1.0);

        static int[] BuildMap()
        {
            // 3× supersampled tissue map, majority vote per pixel (smoother outlines)
            var m = new int[N * N];
            var votes = new int[Tis.Length];
            for (int j = 0; j < N; j++)
                for (int i = 0; i < N; i++)
                {
                    Array.Clear(votes, 0, votes.Length);
                    for (int sj = 0; sj < 3; sj++)
                        for (int si = 0; si < 3; si++)
                            votes[Classify((i + (si + 0.5f) / 3f) / N, (j + (sj + 0.5f) / 3f) / N)]++;
                    int best = 0; for (int t = 1; t < votes.Length; t++) if (votes[t] > votes[best]) best = t;
                    m[j * N + i] = best;
                }
            return m;
        }

        static float Ell(float u, float v, float cu, float cv, float ru, float rv) { float a = (u - cu) / ru, b = (v - cv) / rv; return a * a + b * b; }

        /// <summary>Schematic sagittal knee (u: anterior 0 → posterior 1, v: inferior 0 → superior 1).</summary>
        static int Classify(float u, float v)
        {
            // limb outline with subcutaneous fat
            float front = 0.13f + 0.035f * Mathf.Sin(v * 3.1f + 0.4f), back = 0.88f - 0.03f * Mathf.Cos(v * 2.6f);
            if (u < front || u > back) return AIR;
            bool subq = u < front + 0.035f || u > back - 0.03f;
            // patella, quadriceps and patellar tendons
            float pe = Ell(u, v, 0.245f, 0.585f, 0.042f, 0.085f);
            if (pe < 1) return pe > 0.6f ? COR : MARROW;
            if (Seg(u, v, 0.255f, 0.665f, 0.335f, 0.98f) < 0.012f) return TEN;
            if (Seg(u, v, 0.255f, 0.505f, 0.345f, 0.305f) < 0.011f) return TEN;
            // femur: shaft + condyles
            float ce = Ell(u, v, 0.53f, 0.555f, 0.165f, 0.14f);
            bool shaft = v > 0.56f && Mathf.Abs(u - 0.475f) < 0.078f;
            if (ce < 1 || shaft)
            {
                bool inner = ce < 0.80f || (v > 0.56f && Mathf.Abs(u - 0.475f) < 0.060f);   // cortex = outer shell of the union
                return inner ? MARROW : COR;
            }
            if (ce < 1.16f && v < 0.60f) return CART;
            // cruciate ligament (PCL) in the notch region
            if (Seg(u, v, 0.60f, 0.405f, 0.68f, 0.50f) < 0.012f) return TEN;
            // tibia: plateau + shaft
            float pl = Ell(u, v, 0.535f, 0.345f, 0.20f, 0.065f);
            bool plateau = v < 0.40f && pl < 1;
            bool tshaft = v <= 0.30f && Mathf.Abs(u - 0.535f) < 0.085f + 0.02f * (v / 0.30f);
            if (plateau || tshaft)
            {
                bool rim = plateau && v > 0.30f ? (v > 0.388f || pl > 0.80f) : Mathf.Abs(u - 0.535f) > 0.068f + 0.02f * (v / 0.30f);
                if (rim) return COR;
                if (Ell(u, v, 0.57f, 0.34f, 0.07f, 0.045f) < 1) return EDEMA;   // bone bruise under the plateau
                return MARROW;
            }
            if (v >= 0.40f && v < 0.412f && u > 0.37f && u < 0.71f) return CART;
            // menisci: wedges, thick at the periphery; tear through the posterior horn
            if (v >= 0.40f && v < 0.45f)
            {
                float h = v - 0.40f;
                if (u > 0.32f && u < 0.43f && h < 0.048f * (0.43f - u) / 0.11f) return MEN;
                if (u > 0.62f && u < 0.75f && h < 0.048f * (u - 0.62f) / 0.13f)
                    return Seg(u, v, 0.648f, 0.402f, 0.715f, 0.437f) < 0.0045f ? TEAR : MEN;
            }
            // effusion: suprapatellar recess and posterior recess
            if (Ell(u, v, 0.37f, 0.725f, 0.033f, 0.095f) < 1) return FLU;
            if (Ell(u, v, 0.715f, 0.475f, 0.03f, 0.028f) < 1) return FLU;
            // popliteal vessels (flow void)
            if (Ell(u, v, 0.77f, 0.58f, 0.016f, 0.016f) < 1) return AIR;
            if (subq) return FAT;
            if (Ell(u, v, 0.31f, 0.445f, 0.055f, 0.065f) < 1) return FAT;    // Hoffa fat pad
            if (Ell(u, v, 0.755f, 0.56f, 0.05f, 0.15f) < 1) return FAT;      // popliteal fat
            return MUS;
        }

        static float Seg(float u, float v, float au, float av, float bu, float bv)
        {
            var p = new Vector2(u, v); var a = new Vector2(au, av); var b = new Vector2(bu, bv);
            var ab = b - a; float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            return (p - (a + ab * t)).magnitude;
        }

        public override void Start(Vals v)
        {
            double tr = v["tr"], te = v["te"], b0 = v.I("b0") == 1 ? 3.0 : 1.5;
            bool fs = v.B("fs");
            var sig = new double[Tis.Length];
            for (int t = 0; t < Tis.Length; t++) sig[t] = Signal(t, tr, te, fs, b0);
            double sigma = Sigma(b0);
            var rnd = new System.Random(++seed);
            double mx = 1e-6;
            for (int t = 0; t < Tis.Length; t++) mx = Math.Max(mx, sig[t]);
            var img = new float[N * N];
            for (int k = 0; k < img.Length; k++)
            {
                // magnitude image: Rician noise
                double re = sig[map[k]] + sigma * Gauss(rnd), im = sigma * Gauss(rnd);
                img[k] = (float)(Math.Sqrt(re * re + im * im) / (mx * 1.05));
            }
            UIKit.Gray(Tex, img, N, N);
            MarkL = T("Ön", "Ant"); MarkR = T("Arka", "Post");
            v.Sim["cnrTear"] = Math.Abs(sig[TEAR] - sig[MEN]) / sigma;
            v.Sim["cnrEdema"] = Math.Abs(sig[EDEMA] - sig[MARROW]) / sigma;
            v.Sim["cnrFluid"] = Math.Abs(sig[FLU] - sig[CART]) / sigma;
            v.Sim["snrFluid"] = sig[FLU] / sigma;
            int etl = tr >= 1200 ? 8 : 1;
            v.Sim["ta"] = tr * 256 / etl / 1000.0;
            v.HasSim = true;
            Progress = 1;
        }

        public override bool Poll(Vals v) => true;

        static double Gauss(System.Random r)
        {
            double u1 = 1.0 - r.NextDouble(), u2 = r.NextDouble();
            return Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2);
        }
    }
}
