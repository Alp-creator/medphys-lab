using System;
using UnityEngine;

namespace MedPhys.Physics
{
    public enum MriSeq { SpinEcho, GradientEcho, InversionRecovery }

    /// <summary>MRI tissue parameters, signal equations, a 2D brain phantom and Bloch isochromats.</summary>
    public static class MriPhysics
    {
        public const double GammaBar = 42.577478;   // MHz/T for ¹H

        public sealed class MrTissue
        {
            public string Tr, En;
            public double T1_15, T2, PD, T1_3;      // ms (T1 at 1.5 T and 3 T), PD relative
            public Color Col;
            public double T1(double b0)
            {
                // interpolate on log(B0) between 1.5 T and 3 T values (≈ power law T1 ~ B0^k)
                double k = Math.Log(T1_3 / T1_15) / Math.Log(2.0);
                return T1_15 * Math.Pow(b0 / 1.5, k);
            }
            /// <summary>T2* including field inhomogeneity: 1/T2* = 1/T2 + γΔB (ΔB ~ B0).</summary>
            public double T2s(double b0) => 1.0 / (1.0 / T2 + 0.012 * b0);
        }

        // literature values (Stanisz 2005, Bojorquez 2017, Wansapura 1999), rounded
        public static readonly MrTissue[] Tissues =
        {
            new MrTissue { Tr = "Beyaz cevher", En = "White matter", T1_15 = 790, T1_3 = 1080, T2 = 75, PD = 0.70, Col = new Color(0.9f, 0.9f, 0.85f) },
            new MrTissue { Tr = "Gri cevher", En = "Grey matter", T1_15 = 1150, T1_3 = 1600, T2 = 95, PD = 0.82, Col = new Color(0.6f, 0.6f, 0.62f) },
            new MrTissue { Tr = "BOS (beyin-omurilik sıvısı)", En = "CSF", T1_15 = 3800, T1_3 = 4200, T2 = 1800, PD = 1.00, Col = new Color(0.3f, 0.5f, 0.9f) },
            new MrTissue { Tr = "Yağ", En = "Fat", T1_15 = 260, T1_3 = 380, T2 = 70, PD = 0.90, Col = new Color(1f, 0.85f, 0.4f) },
            new MrTissue { Tr = "Kas", En = "Muscle", T1_15 = 1000, T1_3 = 1400, T2 = 40, PD = 0.75, Col = new Color(0.8f, 0.3f, 0.3f) },
            new MrTissue { Tr = "Kan", En = "Blood", T1_15 = 1450, T1_3 = 1930, T2 = 250, PD = 0.85, Col = new Color(0.8f, 0.1f, 0.15f) },
            new MrTissue { Tr = "Lezyon (ödem / tümör)", En = "Lesion (oedema / tumour)", T1_15 = 1400, T1_3 = 1900, T2 = 160, PD = 0.88, Col = new Color(0.4f, 1f, 0.5f) },
            new MrTissue { Tr = "Kemik / hava", En = "Bone / air", T1_15 = 1000, T1_3 = 1000, T2 = 1, PD = 0.02, Col = new Color(0.1f, 0.1f, 0.1f) },
        };

        public const int WM = 0, GM = 1, CSF = 2, FAT = 3, MUSCLE = 4, BLOOD = 5, LESION = 6, BONE = 7;

        public static double Signal(MrTissue t, double b0, MriSeq seq, double tr, double te, double ti, double flipDeg)
        {
            double t1 = t.T1(b0), t2 = t.T2;
            double e1 = Math.Exp(-tr / t1);
            switch (seq)
            {
                case MriSeq.SpinEcho:
                    return t.PD * (1 - 2 * Math.Exp(-(tr - te / 2) / t1) + e1) * Math.Exp(-te / t2);
                case MriSeq.GradientEcho:
                {
                    double a = flipDeg * Math.PI / 180;
                    return t.PD * Math.Sin(a) * (1 - e1) / (1 - Math.Cos(a) * e1) * Math.Exp(-te / t.T2s(b0));
                }
                default:
                    return t.PD * Math.Abs(1 - 2 * Math.Exp(-ti / t1) + e1) * Math.Exp(-te / t2);
            }
        }

        /// <summary>Ernst angle (deg) for maximum spoiled-GRE signal.</summary>
        public static double Ernst(double tr, double t1) => Math.Acos(Math.Exp(-tr / t1)) * 180 / Math.PI;

        /// <summary>Null TI (ms) for inversion recovery: TI = T1·ln2 (TR >> T1).</summary>
        public static double NullTi(double t1, double tr) => t1 * Math.Log(2 / (1 + Math.Exp(-tr / t1)));

        // ------------------------------------------------------------------ 2D brain phantom (axial)
        /// <summary>Tissue index map (n × n) of an axial brain slice made of ellipses.</summary>
        public static int[] BrainPhantom(int n, bool lesion = true)
        {
            var map = new int[n * n];
            for (int j = 0; j < n; j++)
                for (int i = 0; i < n; i++)
                {
                    double x = (i + 0.5) / n * 2 - 1, y = (j + 0.5) / n * 2 - 1;   // −1..1, y up = anterior
                    int t = -1;
                    if (E(x, y, 0, 0, 0.74, 0.92)) t = FAT;                 // scalp
                    if (E(x, y, 0, 0, 0.69, 0.87)) t = BONE;                // skull
                    if (E(x, y, 0, 0, 0.64, 0.82)) t = CSF;                 // subarachnoid CSF
                    if (E(x, y, 0, 0, 0.61, 0.79)) t = GM;                  // cortex
                    if (E(x, y, 0, -0.02, 0.50, 0.68)) t = WM;              // white matter
                    // deep grey nuclei
                    if (E(x, y, -0.20, -0.05, 0.08, 0.14) || E(x, y, 0.20, -0.05, 0.08, 0.14)) t = GM;
                    // lateral ventricles (butterfly) + third ventricle
                    if (E(x, y, -0.12, 0.12, 0.07, 0.26, -0.25) || E(x, y, 0.12, 0.12, 0.07, 0.26, 0.25)) t = CSF;
                    if (E(x, y, 0, -0.12, 0.02, 0.10)) t = CSF;
                    // interhemispheric fissure
                    if (Math.Abs(x) < 0.012 && (y > 0.40 || y < -0.55) && E(x, y, 0, 0, 0.64, 0.82)) t = CSF;
                    // sagittal sinus & a vessel
                    if (E(x, y, 0, 0.80, 0.025, 0.025) || E(x, y, 0, -0.79, 0.03, 0.03)) t = BLOOD;
                    if (lesion && E(x, y, 0.30, 0.30, 0.08, 0.07)) t = LESION;
                    map[j * n + i] = t;   // −1 = air outside the head
                }
            return map;
        }

        static bool E(double x, double y, double cx, double cy, double a, double b, double rot = 0)
        {
            double dx = x - cx, dy = y - cy;
            if (rot != 0)
            {
                double c = Math.Cos(rot), s = Math.Sin(rot);
                double rx = c * dx + s * dy, ry = -s * dx + c * dy; dx = rx; dy = ry;
            }
            return dx * dx / (a * a) + dy * dy / (b * b) <= 1;
        }

        /// <summary>Magnitude image for a sequence (no noise), from a tissue map.</summary>
        public static float[] Image(int[] map, double b0, MriSeq seq, double tr, double te, double ti, double flip)
        {
            var sig = new double[Tissues.Length];
            for (int t = 0; t < sig.Length; t++) sig[t] = Signal(Tissues[t], b0, seq, tr, te, ti, flip);
            var img = new float[map.Length];
            for (int k = 0; k < map.Length; k++) img[k] = map[k] < 0 ? 0 : (float)sig[map[k]];
            return img;
        }
    }

    /// <summary>Ensemble of spin isochromats evolving under the Bloch equations (rotating frame).</summary>
    public sealed class BlochEnsemble
    {
        public readonly Vector3[] M;      // per isochromat magnetisation (Mx, My, Mz), M0 = 1
        public readonly float[] DOmega;   // off-resonance (rad/ms)
        public double T1 = 800, T2 = 80, T2Star = 40;   // ms
        public double TimeMs;

        public BlochEnsemble(int n, double t2star, double t2, int seed = 1)
        {
            M = new Vector3[n];
            DOmega = new float[n];
            T2 = t2; T2Star = t2star;
            Reset(seed);
        }

        public void Reset(int seed = 1)
        {
            // Lorentzian spread of frequencies gives an exponential FID decay with 1/T2' = 1/T2* − 1/T2
            double r2p = Math.Max(1e-6, 1.0 / T2Star - 1.0 / T2);
            var rng = new System.Random(seed);
            for (int i = 0; i < M.Length; i++)
            {
                M[i] = new Vector3(0, 0, 1);
                double u = (i + 0.5) / M.Length;
                DOmega[i] = (float)Math.Max(-8 * r2p, Math.Min(8 * r2p, r2p * Math.Tan(Math.PI * (u - 0.5))));
            }
            TimeMs = 0;
        }

        /// <summary>Instantaneous RF rotation by 'deg' about the x' axis (rotating frame).</summary>
        public void Pulse(double deg, double phaseDeg = 0)
        {
            var q = Quaternion.AngleAxis((float)deg, Quaternion.AngleAxis((float)phaseDeg, Vector3.forward) * Vector3.right);
            for (int i = 0; i < M.Length; i++) M[i] = q * M[i];
        }

        public void Step(double dtMs)
        {
            float e2 = (float)Math.Exp(-dtMs / T2), e1 = (float)Math.Exp(-dtMs / T1);
            for (int i = 0; i < M.Length; i++)
            {
                var m = M[i];
                float a = (float)(DOmega[i] * dtMs);
                float c = Mathf.Cos(a), s = Mathf.Sin(a);
                float x = m.x * c - m.y * s, y = m.x * s + m.y * c;
                M[i] = new Vector3(x * e2, y * e2, 1 + (m.z - 1) * e1);
            }
            TimeMs += dtMs;
        }

        public Vector3 Net()
        {
            Vector3 s = Vector3.zero;
            foreach (var m in M) s += m;
            return s / M.Length;
        }
    }
}
