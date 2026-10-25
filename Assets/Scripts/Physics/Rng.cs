using System;

namespace MedPhys.Physics
{
    /// <summary>Small, fast xorshift128+ generator used by all Monte Carlo codes.</summary>
    public sealed class Rng
    {
        ulong s0, s1;
        bool hasSpare; double spare;

        public Rng(ulong seed = 0x9E3779B97F4A7C15UL)
        {
            if (seed == 0) seed = 0x9E3779B97F4A7C15UL;
            s0 = SplitMix(ref seed); s1 = SplitMix(ref seed);
        }

        static ulong SplitMix(ref ulong x)
        {
            ulong z = (x += 0x9E3779B97F4A7C15UL);
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        public ulong NextULong()
        {
            ulong x = s0, y = s1;
            s0 = y;
            x ^= x << 23;
            s1 = x ^ y ^ (x >> 17) ^ (y >> 26);
            return s1 + y;
        }

        /// <summary>Uniform in (0,1].</summary>
        public double U() => ((NextULong() >> 11) + 1) * (1.0 / 9007199254740992.0);

        public float Uf() => (float)U();

        public double Range(double a, double b) => a + (b - a) * U();

        public double Gauss()
        {
            if (hasSpare) { hasSpare = false; return spare; }
            double u, v, s;
            do { u = 2 * U() - 1; v = 2 * U() - 1; s = u * u + v * v; } while (s >= 1 || s == 0);
            double m = Math.Sqrt(-2 * Math.Log(s) / s);
            spare = v * m; hasSpare = true;
            return u * m;
        }

        public double Exp(double mean) => -mean * Math.Log(U());

        /// <summary>Poisson sample (Knuth for small means, Gaussian approximation for large).</summary>
        public int Poisson(double mean)
        {
            if (mean <= 0) return 0;
            if (mean > 40) return Math.Max(0, (int)Math.Round(mean + Math.Sqrt(mean) * Gauss()));
            double L = Math.Exp(-mean), p = 1; int k = 0;
            do { k++; p *= U(); } while (p > L);
            return k - 1;
        }

        /// <summary>Random unit vector (isotropic).</summary>
        public void Isotropic(out double x, out double y, out double z)
        {
            double cz = 2 * U() - 1;
            double phi = 2 * Math.PI * U();
            double sz = Math.Sqrt(Math.Max(0, 1 - cz * cz));
            x = sz * Math.Cos(phi); y = sz * Math.Sin(phi); z = cz;
        }
    }
}
