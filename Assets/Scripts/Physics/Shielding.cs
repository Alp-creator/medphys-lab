using System;
using System.Collections.Generic;
using UnityEngine;

namespace MedPhys.Physics
{
    public enum ShieldMat { Lead, Steel, Concrete, Water }

    public sealed class Shield
    {
        public Vector2 A, B;          // end points of the slab centre line (top view, metres)
        public ShieldMat Mat = ShieldMat.Lead;
        public float ThicknessCm = 1f;
        public GameObject View;
    }

    /// <summary>
    /// Point-source gamma dose rate with inverse-square law, narrow-beam attenuation from NIST μ(E)
    /// and a simple linear build-up factor B = 1 + k·μx for broad beams.
    /// Ambient dose equivalent H*(10) ≈ 1.2 × air kerma for 0.3–1.3 MeV photons.
    /// </summary>
    public static class Shielding
    {
        public static Nist.Id NistOf(ShieldMat m) => m == ShieldMat.Lead ? Nist.Id.Lead : m == ShieldMat.Steel ? Nist.Id.Iron : m == ShieldMat.Concrete ? Nist.Id.Concrete : Nist.Id.Water;

        static double BuildupK(ShieldMat m) => m == ShieldMat.Lead ? 0.4 : m == ShieldMat.Steel ? 0.8 : 1.0;

        /// <summary>Linear attenuation coefficient (1/cm) at the photon energy (keV).</summary>
        public static double Mu(ShieldMat m, double keV) => Nist.Get(NistOf(m)).Mu(keV / 1000.0);

        public static double Hvl(ShieldMat m, double keV) => Math.Log(2) / Mu(m, keV);
        public static double Tvl(ShieldMat m, double keV) => Math.Log(10) / Mu(m, keV);

        /// <summary>Broad-beam transmission through x cm.</summary>
        public static double Transmission(ShieldMat m, double keV, double xCm)
        {
            double mux = Mu(m, keV) * xCm;
            return Math.Min(1, (1 + BuildupK(m) * mux) * Math.Exp(-mux));
        }

        /// <summary>Unshielded H*(10) rate (µSv/h) at distance d (m) from a source of activity A (MBq).</summary>
        public static double DoseRate(Isotope iso, double aMBq, double dM)
        {
            dM = Math.Max(dM, 0.05);
            return 1.2 * iso.GammaConstant * aMBq / (dM * dM);
        }

        /// <summary>Dose rate at point p (top view, m) including all shields between source and p.</summary>
        public static double DoseRateAt(Isotope iso, double aMBq, Vector2 src, Vector2 p, IList<Shield> shields, double extraLeadCm = 0)
        {
            double r = DoseRate(iso, aMBq, Vector2.Distance(src, p));
            double keV = iso.GammaKeV > 0 ? Math.Min(iso.GammaKeV, 1500) : 500;
            if (extraLeadCm > 0) r *= Transmission(ShieldMat.Lead, keV, extraLeadCm);
            if (shields == null) return r;
            foreach (var s in shields)
            {
                if (!Intersect(src, p, s.A, s.B, out float t)) continue;
                // oblique path through the slab
                Vector2 d = (p - src).normalized, ab = (s.B - s.A).normalized;
                Vector2 n = new Vector2(-ab.y, ab.x);
                float cos = Mathf.Max(0.15f, Mathf.Abs(Vector2.Dot(d, n)));
                r *= Transmission(s.Mat, keV, s.ThicknessCm / cos);
            }
            return r;
        }

        static bool Intersect(Vector2 p1, Vector2 p2, Vector2 q1, Vector2 q2, out float t)
        {
            t = 0;
            Vector2 r = p2 - p1, s = q2 - q1;
            float den = r.x * s.y - r.y * s.x;
            if (Mathf.Abs(den) < 1e-6f) return false;
            Vector2 qp = q1 - p1;
            t = (qp.x * s.y - qp.y * s.x) / den;
            float u = (qp.x * r.y - qp.y * r.x) / den;
            return t > 0 && t < 1 && u >= 0 && u <= 1;
        }

        /// <summary>ICRP 103 effective dose limits (mSv per year).</summary>
        public const double OccupationalLimit = 20, PublicLimit = 1, LensLimit = 20, SkinLimit = 500;
    }
}
