using System;

namespace MedPhys.Physics
{
    /// <summary>In-place iterative radix-2 complex FFT (used for CT filtering and MRI k-space).</summary>
    public static class Fft
    {
        public static int NextPow2(int n)
        {
            int p = 1;
            while (p < n) p <<= 1;
            return p;
        }

        /// <summary>Forward (sign = -1) or inverse (sign = +1, unscaled) transform of length 2^k.</summary>
        public static void Transform(double[] re, double[] im, int sign)
        {
            int n = re.Length;
            for (int i = 1, j = 0; i < n; i++)
            {
                int bit = n >> 1;
                for (; (j & bit) != 0; bit >>= 1) j ^= bit;
                j ^= bit;
                if (i < j)
                {
                    double t = re[i]; re[i] = re[j]; re[j] = t;
                    t = im[i]; im[i] = im[j]; im[j] = t;
                }
            }
            for (int len = 2; len <= n; len <<= 1)
            {
                double ang = sign * 2 * Math.PI / len;
                double wr = Math.Cos(ang), wi = Math.Sin(ang);
                for (int i = 0; i < n; i += len)
                {
                    double cr = 1, ci = 0;
                    int half = len >> 1;
                    for (int k = 0; k < half; k++)
                    {
                        int a = i + k, b = a + half;
                        double xr = re[b] * cr - im[b] * ci;
                        double xi = re[b] * ci + im[b] * cr;
                        re[b] = re[a] - xr; im[b] = im[a] - xi;
                        re[a] += xr; im[a] += xi;
                        double ncr = cr * wr - ci * wi;
                        ci = cr * wi + ci * wr; cr = ncr;
                    }
                }
            }
        }

        /// <summary>2D FFT of an n x n array stored row-major (n power of two).</summary>
        public static void Transform2D(double[] re, double[] im, int n, int sign)
        {
            var rr = new double[n]; var ri = new double[n];
            for (int y = 0; y < n; y++)
            {
                Array.Copy(re, y * n, rr, 0, n); Array.Copy(im, y * n, ri, 0, n);
                Transform(rr, ri, sign);
                Array.Copy(rr, 0, re, y * n, n); Array.Copy(ri, 0, im, y * n, n);
            }
            for (int x = 0; x < n; x++)
            {
                for (int y = 0; y < n; y++) { rr[y] = re[y * n + x]; ri[y] = im[y * n + x]; }
                Transform(rr, ri, sign);
                for (int y = 0; y < n; y++) { re[y * n + x] = rr[y]; im[y * n + x] = ri[y]; }
            }
        }
    }
}
