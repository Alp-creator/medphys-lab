using System;

namespace MedPhys.Physics
{
    /// <summary>
    /// Photon interaction data (NIST XCOM / Hubbell-Seltzer tables) with log-log interpolation.
    /// Energies in MeV, mass coefficients in cm^2/g, linear coefficients in 1/cm.
    /// </summary>
    public static partial class Nist
    {
        public sealed class Material
        {
            public readonly Id Id;
            public readonly float Density;          // g/cm^3 (NIST reference density; lung uses inflated 0.26)
            readonly double[] lnE, lnMu, lnMuEn;

            internal Material(Id id, float rho, double[] t)
            {
                Id = id;
                Density = rho;
                int n = t.Length / 3;
                lnE = new double[n]; lnMu = new double[n]; lnMuEn = new double[n];
                for (int i = 0; i < n; i++)
                {
                    lnE[i] = Math.Log(t[3 * i]);
                    lnMu[i] = Math.Log(t[3 * i + 1]);
                    lnMuEn[i] = Math.Log(Math.Max(t[3 * i + 2], 1e-30));
                }
            }

            // index i such that E_i <= E < E_{i+1}; absorption edges appear as duplicate energies
            int Find(double le)
            {
                int lo = 0, hi = lnE.Length - 1;
                if (le <= lnE[0]) return 0;
                if (le >= lnE[hi]) return hi - 1;
                while (hi - lo > 1)
                {
                    int mid = (lo + hi) >> 1;
                    if (lnE[mid] <= le) lo = mid; else hi = mid;
                }
                return lo;
            }

            static double Interp(double[] x, double[] y, int i, double lx)
            {
                double dx = x[i + 1] - x[i];
                if (dx <= 0) return y[i + 1];
                return y[i] + (y[i + 1] - y[i]) * (lx - x[i]) / dx;
            }

            /// <summary>Mass attenuation coefficient mu/rho (cm^2/g).</summary>
            public double MuRho(double eMeV)
            {
                double le = Math.Log(eMeV);
                return Math.Exp(Interp(lnE, lnMu, Find(le), le));
            }

            /// <summary>Mass energy-absorption coefficient mu_en/rho (cm^2/g).</summary>
            public double MuEnRho(double eMeV)
            {
                double le = Math.Log(eMeV);
                return Math.Exp(Interp(lnE, lnMuEn, Find(le), le));
            }

            /// <summary>Linear attenuation coefficient (1/cm) at the reference density.</summary>
            public double Mu(double eMeV) => MuRho(eMeV) * Density;
            public double Mu(double eMeV, double density) => MuRho(eMeV) * density;
        }

        static Material[] _mats;

        public static Material Get(Id id)
        {
            if (_mats == null)
            {
                var arr = new Material[Tables.Length];
                for (int i = 0; i < Tables.Length; i++) arr[i] = new Material((Id)i, Densities[i], Tables[i]);
                _mats = arr;
            }
            return _mats[(int)id];
        }

        /// <summary>Half-value layer (cm) of a narrow monoenergetic beam.</summary>
        public static double Hvl(Id id, double eMeV, double density = -1)
        {
            var m = Get(id);
            double rho = density > 0 ? density : m.Density;
            return Math.Log(2) / (m.MuRho(eMeV) * rho);
        }

        /// <summary>Electron density of water (electrons per gram).</summary>
        public const double WaterElectronsPerGram = 3.3428e23;

        /// <summary>Klein-Nishina total cross section per electron (cm^2) for photon energy in MeV.</summary>
        public static double KleinNishina(double eMeV)
        {
            const double re2 = 7.9407877e-26; // classical electron radius squared (cm^2)
            double k = eMeV / 0.51099895;
            double a = 1 + k;
            double l = Math.Log(1 + 2 * k);
            double t1 = a / (k * k) * (2 * a / (1 + 2 * k) - l / k);
            double t2 = l / (2 * k);
            double t3 = (1 + 3 * k) / ((1 + 2 * k) * (1 + 2 * k));
            return 2 * Math.PI * re2 * (t1 + t2 - t3);
        }
    }
}
