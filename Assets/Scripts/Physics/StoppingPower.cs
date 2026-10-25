using System;

namespace MedPhys.Physics
{
    /// <summary>
    /// Charged-particle stopping powers and ranges in liquid water (I = 75 eV, Z/A = 0.5551).
    ///  - Electrons: Rohrlich–Carlson collision formula with the Sternheimer density-effect correction,
    ///    radiative stopping from the approximation S_rad / S_col ≈ (T + mc²)·Z / 800 MeV.
    ///  - Protons: Bethe–Bloch with the exact maximum energy transfer and density effect.
    /// Results are mass stopping powers in MeV·cm²/g (= MeV/cm in water). Ranges are CSDA ranges in cm.
    /// </summary>
    public static class StoppingPower
    {
        const double Me = 0.51099895;            // MeV
        const double Mp = 938.27208816;          // MeV
        const double K = 0.307075;               // MeV cm²/mol  (4π N_A r_e² m_e c²)
        const double ZoverA = 0.555087;          // water
        const double I = 75e-6;                  // MeV
        const double Zeff = 7.42;

        // Sternheimer parameters for water
        const double Cst = 3.5017, X0 = 0.2400, X1 = 2.8004, Ast = 0.09116, Mst = 3.4773;

        static double Delta(double betaGamma)
        {
            double x = Math.Log10(betaGamma);
            if (x < X0) return 0;
            if (x < X1) return 4.6052 * x - Cst + Ast * Math.Pow(X1 - x, Mst);
            return 4.6052 * x - Cst;
        }

        /// <summary>Electron collision stopping power (MeV cm²/g).</summary>
        public static double ElectronCollision(double T)
        {
            T = Math.Max(T, 1e-3);
            double tau = T / Me;
            double gamma = tau + 1;
            double beta2 = 1 - 1 / (gamma * gamma);
            double F = 1 - beta2 + (tau * tau / 8 - (2 * tau + 1) * Math.Log(2)) / ((tau + 1) * (tau + 1));
            double bg = Math.Sqrt(beta2) * gamma;
            double arg = tau * tau * (tau + 2) / (2 * (I / Me) * (I / Me));
            double s = 0.5 * K * ZoverA / beta2 * (Math.Log(arg) + F - Delta(bg));
            return Math.Max(s, 0.05);
        }

        public static double ElectronRadiative(double T) => ElectronCollision(T) * (T + Me) * Zeff / 800.0;

        public static double ElectronTotal(double T) => ElectronCollision(T) + ElectronRadiative(T);

        /// <summary>Proton electronic stopping power (MeV cm²/g).</summary>
        public static double Proton(double T)
        {
            T = Math.Max(T, 0.3);   // Bethe is invalid below ~0.3 MeV (protons are stopped locally there)
            double gamma = 1 + T / Mp;
            double beta2 = 1 - 1 / (gamma * gamma);
            double bg2 = beta2 * gamma * gamma;
            double r = Me / Mp;
            double tmax = 2 * Me * bg2 / (1 + 2 * gamma * r + r * r);
            double L = 0.5 * Math.Log(2 * Me * bg2 * tmax / (I * I)) - beta2 - Delta(Math.Sqrt(bg2)) / 2;
            double s = K * ZoverA / beta2 * L;
            return Math.Max(s, 1.0);
        }

        // ---------------------------------------------------------------- tabulated CSDA ranges
        static double[] eGrid, eRange, pGrid, pRange;
        const int NG = 400;

        static void Build(ref double[] grid, ref double[] range, Func<double, double> S, double eMin, double eMax)
        {
            grid = new double[NG]; range = new double[NG];
            double l0 = Math.Log(eMin), l1 = Math.Log(eMax);
            for (int i = 0; i < NG; i++) grid[i] = Math.Exp(l0 + (l1 - l0) * i / (NG - 1));
            // range at the lowest energy: R ≈ T / S
            range[0] = grid[0] / S(grid[0]);
            for (int i = 1; i < NG; i++)
            {
                // trapezoid in energy of 1/S
                double a = grid[i - 1], b = grid[i];
                int sub = 8; double acc = 0;
                for (int k = 0; k < sub; k++)
                {
                    double e0 = a + (b - a) * k / sub, e1 = a + (b - a) * (k + 1) / sub;
                    acc += 0.5 * (1 / S(e0) + 1 / S(e1)) * (e1 - e0);
                }
                range[i] = range[i - 1] + acc;
            }
        }

        static double Interp(double[] x, double[] y, double v)
        {
            if (v <= x[0]) return y[0] * v / x[0];
            if (v >= x[x.Length - 1]) return y[y.Length - 1];
            int lo = 0, hi = x.Length - 1;
            while (hi - lo > 1) { int m = (lo + hi) >> 1; if (x[m] <= v) lo = m; else hi = m; }
            double t = (v - x[lo]) / (x[hi] - x[lo]);
            return y[lo] + t * (y[hi] - y[lo]);
        }

        public static double ElectronRange(double T)
        {
            if (eGrid == null) Build(ref eGrid, ref eRange, ElectronTotal, 1e-3, 60);
            return Interp(eGrid, eRange, T);
        }

        public static double ElectronEnergyAtRange(double R)
        {
            if (eGrid == null) Build(ref eGrid, ref eRange, ElectronTotal, 1e-3, 60);
            return Interp(eRange, eGrid, R);
        }

        public static double ProtonRange(double T)
        {
            if (pGrid == null) Build(ref pGrid, ref pRange, Proton, 0.1, 300);
            return Interp(pGrid, pRange, T);
        }

        public static double ProtonEnergyAtRange(double R)
        {
            if (pGrid == null) Build(ref pGrid, ref pRange, Proton, 0.1, 300);
            return Interp(pRange, pGrid, R);
        }

        /// <summary>NIST PSTAR CSDA ranges in water (g/cm²) for comparison.</summary>
        public static readonly (double T, double R)[] PstarRanges =
        {
            (10, 0.1230), (20, 0.4260), (50, 2.227), (70, 4.080), (100, 7.718), (150, 15.77), (200, 25.96), (250, 37.94),
        };

        /// <summary>NIST ESTAR CSDA ranges in water (g/cm²) for comparison.</summary>
        public static readonly (double T, double R)[] EstarRanges =
        {
            (0.1, 0.01431), (0.5, 0.1766), (1, 0.4367), (2, 0.9785), (5, 2.550), (10, 4.975), (20, 9.320),
        };

        /// <summary>Highland characteristic multiple-scattering angle (rad) for a step s (cm) in water.</summary>
        public static double Highland(double T, double mass, double s)
        {
            const double X0 = 36.08;  // radiation length of water (cm)
            double E = T + mass;
            double p = Math.Sqrt(E * E - mass * mass);
            double beta = p / E;
            double x = Math.Max(s / X0, 1e-6);
            double th = 13.6 / (beta * p) * Math.Sqrt(x) * (1 + 0.038 * Math.Log(x));
            return Math.Max(th, 0);
        }
    }
}
