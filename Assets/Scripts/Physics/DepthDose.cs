using System;
using System.Collections.Generic;

namespace MedPhys.Physics
{
    /// <summary>
    /// Analytic / reference depth-dose curves in water.
    ///  • Photons: typical measured PDD for a 10×10 cm² field (SSD 100 cm; Co-60 at SSD 80 cm), BJR-25-like values.
    ///  • Electrons: empirical rules R50 = E0/2.33, Rp = 0.52·E0 − 0.3, surface dose and bremsstrahlung tail.
    ///  • Protons: CSDA (Bethe–Bloch) pristine curve, nuclear fluence loss and Gaussian range straggling
    ///    σ_R ≈ 0.012·R^0.951 (Bortfeld 1997) combined with the beam energy spread; SOBP weight optimisation.
    /// Depth in cm, dose in % (max = 100).
    /// </summary>
    public static class DepthDose
    {
        static readonly double[] Dep6 = { 0, 0.5, 1, 1.5, 2, 3, 4, 5, 6, 7, 8, 9, 10, 12, 14, 16, 18, 20, 22, 25, 30 };
        static readonly double[] Pdd6 = { 16, 72, 94, 100, 99.1, 95.8, 91.4, 86.6, 82.5, 78.4, 74.6, 70.9, 67.2, 61.0, 54.9, 49.5, 44.3, 39.5, 35.5, 30.0, 22.8 };
        static readonly double[] Dep10 = { 0, 0.5, 1, 2, 2.3, 3, 4, 5, 6, 8, 10, 12, 15, 20, 25, 30 };
        static readonly double[] Pdd10 = { 12, 58, 82, 99, 100, 99.0, 96.0, 92.5, 88.7, 81.0, 73.5, 66.6, 57.5, 45.0, 35.0, 27.5 };
        static readonly double[] Dep18 = { 0, 0.5, 1, 2, 3, 3.3, 4, 5, 6, 8, 10, 12, 15, 20, 25, 30 };
        static readonly double[] Pdd18 = { 10, 45, 67, 88, 98, 100, 99.6, 97.5, 94.5, 87.7, 79.6, 72.6, 63.4, 50.8, 40.6, 32.5 };
        static readonly double[] DepCo = { 0, 0.5, 1, 2, 3, 4, 5, 6, 8, 10, 12, 15, 20, 25, 30 };
        static readonly double[] PddCo = { 30, 100, 98.0, 93.8, 89.3, 84.6, 79.9, 75.3, 66.7, 58.7, 51.4, 42.0, 29.6, 20.8, 14.6 };

        static double Lerp(double[] x, double[] y, double v)
        {
            if (v <= x[0]) return y[0];
            if (v >= x[x.Length - 1])
            {
                // extrapolate with the slope of the last decade (exponential)
                int n = x.Length;
                double k = Math.Log(y[n - 1] / y[n - 2]) / (x[n - 1] - x[n - 2]);
                return y[n - 1] * Math.Exp(k * (v - x[n - 1]));
            }
            int i = 1;
            while (x[i] < v) i++;
            double t = (v - x[i - 1]) / (x[i] - x[i - 1]);
            return y[i - 1] + t * (y[i] - y[i - 1]);
        }

        /// <summary>Reference photon PDD (10×10 cm²). mv: 1.25 (Co-60), 6, 10, 18.</summary>
        public static double PhotonPdd(double mv, double d)
        {
            if (mv < 2) return Lerp(DepCo, PddCo, d);
            if (mv < 8) return Lerp(Dep6, Pdd6, d);
            if (mv < 14) return Lerp(Dep10, Pdd10, d);
            return Lerp(Dep18, Pdd18, d);
        }

        public static double PhotonDmax(double mv) => mv < 2 ? 0.5 : mv < 8 ? 1.5 : mv < 14 ? 2.3 : 3.3;

        /// <summary>
        /// Tissue-maximum-ratio-like curve used for planning: PDD with the inverse-square factor removed,
        /// corrected for field size via a simple scatter term.
        /// </summary>
        public static double PhotonTmr(double mv, double d, double fieldCm)
        {
            double dmax = PhotonDmax(mv);
            double ssd = mv < 2 ? 80 : 100;
            double pdd = PhotonPdd(mv, d) / 100.0;
            double isq = Math.Pow((ssd + d) / (ssd + dmax), 2);
            double tmr = pdd * isq;
            // more scatter in larger fields: ~ +0.35 % per cm depth per (log field / 10)
            double fs = Math.Log(Math.Max(fieldCm, 2) / 10.0);
            if (d > dmax) tmr *= 1 + 0.0035 * (d - dmax) * fs * 10 / Math.Max(mv, 4);
            return tmr;
        }

        // ------------------------------------------------------------------ electrons
        public static double ElectronR50(double e) => e / 2.33;
        public static double ElectronRp(double e) => 0.52 * e - 0.3;

        public static double ElectronPdd(double e, double d)
        {
            double r50 = ElectronR50(e), rp = ElectronRp(e);
            double surface = Math.Min(93, 72 + 1.5 * e);
            double tail = Math.Max(0.5, 0.18 * e);
            double dmax = Math.Min(0.6 * r50, 0.15 + 0.22 * e);
            double sigma = Math.Max(0.15, 2 * (rp - r50) / Math.Sqrt(2 * Math.PI));
            Func<double, double> fall = x => tail + (100 - tail) * 0.5 * Erfc((x - r50) / (sigma * Math.Sqrt(2)));
            if (d < dmax) return surface + (100 - surface) * Math.Sin(d / dmax * Math.PI / 2);
            return Math.Max(tail, 100 * fall(d) / fall(dmax));
        }

        public static double Erfc(double x)
        {
            // Abramowitz–Stegun 7.1.26
            double z = Math.Abs(x);
            double t = 1 / (1 + 0.5 * z);
            double r = t * Math.Exp(-z * z - 1.26551223 + t * (1.00002368 + t * (0.37409196 + t * (0.09678418 + t * (-0.18628806 +
                       t * (0.27886807 + t * (-1.13520398 + t * (1.48851587 + t * (-0.82215223 + t * 0.17087277)))))))));
            return x >= 0 ? r : 2 - r;
        }

        // ------------------------------------------------------------------ protons
        /// <summary>Pristine Bragg curve (relative dose, un-normalised) at depth d for a beam of energy E (MeV).</summary>
        public sealed class Bragg
        {
            public readonly double Energy, R0, Sigma;
            readonly double[] zs, ds;
            public const double Dz = 0.05;

            public Bragg(double energy, double energySpread = 0.01)
            {
                Energy = energy;
                R0 = StoppingPower.ProtonRange(energy);
                double sStrag = 0.012 * Math.Pow(R0, 0.951);
                // beam energy spread → range spread through the range-energy slope dR/dE
                double dRdE = (StoppingPower.ProtonRange(energy * 1.01) - StoppingPower.ProtonRange(energy * 0.99)) / (0.02 * energy);
                double sE = energySpread * energy * dRdE;
                Sigma = Math.Sqrt(sStrag * sStrag + sE * sE);
                int n = (int)((R0 + 4 * Sigma + 1) / Dz) + 2;
                zs = new double[n]; ds = new double[n];
                const int K = 31;
                for (int i = 0; i < n; i++)
                {
                    double z = i * Dz; zs[i] = z;
                    double acc = 0, wsum = 0;
                    for (int k = 0; k < K; k++)
                    {
                        double u = -3 + 6.0 * k / (K - 1);
                        double w = Math.Exp(-0.5 * u * u);
                        double r = R0 + u * Sigma;
                        wsum += w;
                        if (z >= r) continue;
                        double e = StoppingPower.ProtonEnergyAtRange(r - z);
                        acc += w * StoppingPower.Proton(Math.Max(e, 0.5));
                    }
                    double fluence = Math.Max(0, 1 - 0.0118 * z);
                    // energy of nuclear secondaries deposited near the interaction (adds a small build-up)
                    double nuc = 0.4 * 0.0118 * (Energy * (1 - z / Math.Max(R0, 1))) * (z < R0 ? 1 : 0);
                    ds[i] = acc / wsum * fluence + nuc;
                }
            }

            public double At(double z)
            {
                if (z < 0) return 0;
                int i = (int)(z / Dz);
                if (i >= zs.Length - 1) return 0;
                double t = (z - zs[i]) / Dz;
                return ds[i] + t * (ds[i + 1] - ds[i]);
            }

            public double Max()
            {
                double m = 0; foreach (var v in ds) m = Math.Max(m, v); return m;
            }
        }

        static readonly Dictionary<int, Bragg> braggCache = new Dictionary<int, Bragg>();

        public static Bragg GetBragg(double energy)
        {
            int key = (int)Math.Round(energy * 10);
            lock (braggCache)
            {
                if (!braggCache.TryGetValue(key, out var b)) { b = new Bragg(key / 10.0); braggCache[key] = b; }
                return b;
            }
        }

        public sealed class Sobp
        {
            public readonly List<(Bragg peak, double w)> Peaks = new List<(Bragg, double)>();
            public double Proximal, Distal;

            public double At(double z)
            {
                double s = 0;
                foreach (var (p, w) in Peaks) s += w * p.At(z);
                return s;
            }
        }

        /// <summary>Spread-out Bragg peak covering [proximal, distal] depth (cm) by weighting pristine peaks.</summary>
        public static Sobp BuildSobp(double proximal, double distal, double step = 0.5)
        {
            var s = new Sobp { Proximal = proximal, Distal = distal };
            proximal = Math.Max(0.5, Math.Min(proximal, distal - 0.5));
            // peak ranges from the distal edge back to the proximal edge
            var ranges = new List<double>();
            for (double r = distal; r >= proximal - 1e-6; r -= step) ranges.Add(r);
            foreach (var r in ranges)
            {
                double e = StoppingPower.ProtonEnergyAtRange(r);
                var b = GetBragg(e);
                s.Peaks.Add((b, 1.0));
            }
            // iterative re-weighting to flatten dose at the peak positions
            int n = s.Peaks.Count;
            var w = new double[n];
            for (int i = 0; i < n; i++) w[i] = 1.0 / s.Peaks[i].peak.Max();
            for (int it = 0; it < 60; it++)
            {
                for (int i = 0; i < n; i++)
                {
                    double z = s.Peaks[i].peak.R0 - 0.1;
                    double d = 0;
                    for (int j = 0; j < n; j++) d += w[j] * s.Peaks[j].peak.At(z);
                    w[i] *= Math.Pow(1.0 / Math.Max(d, 1e-9), 0.7);
                    if (w[i] < 0) w[i] = 0;
                }
            }
            double mx = 0;
            for (int i = 0; i < n; i++) s.Peaks[i] = (s.Peaks[i].peak, w[i]);
            for (double z = proximal; z <= distal; z += 0.1) mx = Math.Max(mx, s.At(z));
            for (int i = 0; i < n; i++) s.Peaks[i] = (s.Peaks[i].peak, w[i] / mx);
            return s;
        }
    }
}
