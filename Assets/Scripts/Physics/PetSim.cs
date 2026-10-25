using System;
using System.Collections.Concurrent;
using System.Threading;
using UnityEngine;

namespace MedPhys.Physics
{
    /// <summary>
    /// 2D PET acquisition of a NEMA-IEC-like body phantom slice (water cylinder, hot spheres, cold lung insert).
    /// Each event: emission point sampled from the activity map → positron range → annihilation with
    /// photon non-collinearity → both 511 keV photons tracked to a detector ring with attenuation (μ = 0.096 /cm
    /// in water), single Compton scattering (Klein–Nishina angle, energy window), crystal efficiency and
    /// energy resolution. Random coincidences are added at a rate ~ activity². Coincidences are binned into a
    /// sinogram; images are reconstructed by FBP or MLEM (with optional attenuation correction).
    /// </summary>
    public sealed class PetSim
    {
        public const float RingR = 40f;          // cm
        public const int NCrystal = 384;
        public const int NAng = 128, NRad = 128;
        public const float Fov = 30f;            // cm (sinogram radial range ±15)
        public const int ImgN = 96;

        // phantom
        public float BodyR = 11f;
        public readonly (Vector2 c, float r)[] Spheres;
        public Vector2 LungC = new Vector2(0, 0); public float LungR = 2.5f;
        public float Contrast = 4f;
        public double PositronRangeMm = 0.6;
        public bool Scatter = true, Randoms = true;
        public double ActivityMBq = 200;

        public readonly int[] Sino = new int[NAng * NRad];
        public long Trues, Scatters, RandomsCount, Emitted;
        public volatile bool Running;
        public readonly ConcurrentQueue<(Vector2 a, Vector2 b, byte type, Vector2 origin)> Lors = new ConcurrentQueue<(Vector2, Vector2, byte, Vector2)>();
        public readonly ConcurrentQueue<int> Hits = new ConcurrentQueue<int>();
        Thread thread;
        readonly object sinoLock = new object();
        const double MuWater = 0.096, MuLung = 0.03;

        public PetSim()
        {
            // 6 hot spheres (10–37 mm diameter) on a 5.7 cm ring, like the NEMA NU-2 IEC phantom
            float[] d = { 1.0f, 1.3f, 1.7f, 2.2f, 2.8f, 3.7f };
            Spheres = new (Vector2, float)[d.Length];
            for (int i = 0; i < d.Length; i++)
            {
                float a = Mathf.PI * 2 * i / d.Length + 0.3f;
                Spheres[i] = (new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 5.7f, d[i] / 2);
            }
        }

        public float ActivityAt(Vector2 p)
        {
            if (p.sqrMagnitude > BodyR * BodyR) return 0;
            if ((p - LungC).sqrMagnitude < LungR * LungR) return 0;
            foreach (var s in Spheres) if ((p - s.c).sqrMagnitude < s.r * s.r) return Contrast;
            return 1;
        }

        public double MuAt(Vector2 p)
        {
            if (p.sqrMagnitude > BodyR * BodyR) return 0;
            if ((p - LungC).sqrMagnitude < LungR * LungR) return MuLung;
            return MuWater;
        }

        /// <summary>Line integral of μ along the full line through p with direction d (both directions to the ring).</summary>
        public double MuIntegral(Vector2 p, Vector2 d)
        {
            // chord through the body circle, minus the lung part
            double total = 0;
            total += Chord(p, d, Vector2.zero, BodyR) * MuWater;
            total -= Chord(p, d, LungC, LungR) * (MuWater - MuLung);
            return total;
        }

        static double Chord(Vector2 p, Vector2 d, Vector2 c, float r)
        {
            Vector2 o = p - c;
            float b = Vector2.Dot(o, d), cc = o.sqrMagnitude - r * r;
            float disc = b * b - cc;
            if (disc <= 0) return 0;
            return 2 * Mathf.Sqrt(disc);
        }

        public void Start()
        {
            if (Running) return;
            Running = true;
            thread = new Thread(Loop) { IsBackground = true, Priority = System.Threading.ThreadPriority.BelowNormal };
            thread.Start();
        }

        public void Stop() { Running = false; thread?.Join(300); thread = null; }

        public void Clear()
        {
            lock (sinoLock) Array.Clear(Sino, 0, Sino.Length);
            Trues = Scatters = RandomsCount = Emitted = 0;
        }

        void Loop()
        {
            var r = new Rng((ulong)Environment.TickCount + 77);
            int vis = 0;
            while (Running)
            {
                for (int k = 0; k < 2000 && Running; k++) Event(r, (vis++ % 60) == 0);
                Thread.Sleep(1);
            }
        }

        static int CrystalAt(Vector2 p) => ((int)Mathf.Floor((Mathf.Atan2(p.y, p.x) / (2 * Mathf.PI) + 1f) * NCrystal)) % NCrystal;
        public static Vector2 CrystalPos(int i) { float a = (i + 0.5f) * 2 * Mathf.PI / NCrystal; return new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * RingR; }

        // intersection of a ray with the ring (from inside)
        static Vector2 ToRing(Vector2 p, Vector2 d)
        {
            float b = Vector2.Dot(p, d), c = p.sqrMagnitude - RingR * RingR;
            float t = -b + Mathf.Sqrt(Mathf.Max(0, b * b - c));
            return p + d * t;
        }

        void Event(Rng r, bool visual)
        {
            // emission point by rejection sampling
            Vector2 p;
            while (true)
            {
                p = new Vector2((float)r.Range(-BodyR, BodyR), (float)r.Range(-BodyR, BodyR));
                if (r.U() * Contrast < ActivityAt(p)) break;
            }
            Emitted++;
            Vector2 origin = p;
            // positron range: isotropic displacement, exponential with the mean range (mm → cm)
            double rr = r.Exp(PositronRangeMm / 10.0);
            double ang0 = r.U() * Math.PI * 2;
            p += new Vector2((float)(Math.Cos(ang0) * rr), (float)(Math.Sin(ang0) * rr));
            // annihilation direction with non-collinearity (σ ≈ 0.21°)
            double th = r.U() * Math.PI;
            var d1 = new Vector2((float)Math.Cos(th), (float)Math.Sin(th));
            double nc = r.Gauss() * 0.0037;
            var d2 = new Vector2((float)-Math.Cos(th + nc), (float)-Math.Sin(th + nc));
            byte type = 0;
            // attenuation: survival of both photons
            double l1 = PathMu(p, d1), l2 = PathMu(p, d2);
            double s1 = Math.Exp(-l1), s2 = Math.Exp(-l2);
            Vector2 e1 = ToRing(p, d1), e2 = ToRing(p, d2);
            double en1 = 511, en2 = 511;
            bool ok1 = r.U() < s1, ok2 = r.U() < s2;
            if (!ok1 || !ok2)
            {
                if (!Scatter) return;
                // one photon interacted: Compton scatter it (single scatter approximation)
                bool first = !ok1;
                if (!ok1 && !ok2) return;
                double tsc = r.U() * Math.Max(0.5, Chord(p, first ? d1 : d2, Vector2.zero, BodyR) * 0.5);
                var dd = first ? d1 : d2;
                var sp = p + dd * (float)tsc;
                double ct = SampleKN(r, 0.511);
                double e = 511 / (1 + (1 - ct));
                double phi = Math.Acos(Math.Max(-1, Math.Min(1, ct))) * (r.U() < 0.5 ? 1 : -1);
                var nd = new Vector2((float)(dd.x * Math.Cos(phi) - dd.y * Math.Sin(phi)), (float)(dd.x * Math.Sin(phi) + dd.y * Math.Cos(phi)));
                if (r.U() > Math.Exp(-PathMu(sp, nd))) return;
                if (first) { e1 = ToRing(sp, nd); en1 = e; } else { e2 = ToRing(sp, nd); en2 = e; }
                type = 1;
            }
            // detection efficiency and energy window (12 % FWHM resolution, 425–650 keV)
            if (r.U() > 0.75 || r.U() > 0.75) return;
            double m1 = en1 * (1 + 0.051 * r.Gauss()), m2 = en2 * (1 + 0.051 * r.Gauss());
            if (m1 < 425 || m1 > 650 || m2 < 425 || m2 > 650) return;
            int c1 = CrystalAt(e1), c2 = CrystalAt(e2);
            // the event is positioned anywhere across the hit crystal's face (finite crystal width)
            Bin(Jitter(c1, r), Jitter(c2, r));
            if (type == 0) Trues++; else Scatters++;
            if (visual) { Lors.Enqueue((CrystalPos(c1), CrystalPos(c2), type, origin)); Hits.Enqueue(c1); Hits.Enqueue(c2); }
            // randoms: rate ~ singles² ~ activity²
            if (Randoms && r.U() < 0.08 * (ActivityMBq / 200.0))
            {
                int a = (int)(r.U() * NCrystal) % NCrystal, b = (int)(r.U() * NCrystal) % NCrystal;
                if (Math.Abs(a - b) > 40)
                {
                    Bin(Jitter(a, r), Jitter(b, r)); RandomsCount++;
                    if (visual) Lors.Enqueue((CrystalPos(a), CrystalPos(b), 2, Vector2.zero));
                }
            }
        }

        double PathMu(Vector2 p, Vector2 d)
        {
            // μ integral from p to the body edge along d (body circle + lung)
            double t = ExitT(p, d, Vector2.zero, BodyR);
            double lungT = SegInCircle(p, d, LungC, LungR, t);
            return MuWater * (t - lungT) + MuLung * lungT;
        }

        static double ExitT(Vector2 p, Vector2 d, Vector2 c, float r)
        {
            Vector2 o = p - c;
            double b = Vector2.Dot(o, d), cc = o.sqrMagnitude - r * r;
            double disc = b * b - cc;
            if (disc <= 0) return 0;
            return Math.Max(0, -b + Math.Sqrt(disc));
        }

        static double SegInCircle(Vector2 p, Vector2 d, Vector2 c, float r, double tMax)
        {
            Vector2 o = p - c;
            double b = Vector2.Dot(o, d), cc = o.sqrMagnitude - r * r;
            double disc = b * b - cc;
            if (disc <= 0) return 0;
            double t0 = -b - Math.Sqrt(disc), t1 = -b + Math.Sqrt(disc);
            t0 = Math.Max(t0, 0); t1 = Math.Min(t1, tMax);
            return Math.Max(0, t1 - t0);
        }

        static double SampleKN(Rng r, double eMeV)
        {
            double k = eMeV / 0.51099895;
            while (true)
            {
                double r1 = r.U(), r2 = r.U(), r3 = r.U();
                double eta;
                if (r1 <= (1 + 2 * k) / (9 + 2 * k))
                {
                    eta = 1 + 2 * k * r2;
                    if (r3 <= 4 * (1 / eta - 1 / (eta * eta))) return 1 - (eta - 1) / k;
                }
                else
                {
                    eta = (1 + 2 * k) / (1 + 2 * k * r2);
                    double c = 1 - (eta - 1) / k;
                    if (r3 <= 0.5 * (c * c + 1 / eta)) return c;
                }
            }
        }

        static Vector2 Jitter(int c, Rng r)
        {
            float a = (float)((c + r.U()) * 2 * Math.PI / NCrystal);
            return new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * RingR;
        }

        void Bin(Vector2 a, Vector2 b)
        {
            Vector2 d = (b - a).normalized;
            // LOR normal angle φ in [0, π) and signed distance s
            Vector2 n = new Vector2(-d.y, d.x);
            float s = Vector2.Dot(a, n);
            float phi = Mathf.Atan2(n.y, n.x);
            if (phi < 0) { phi += Mathf.PI; s = -s; }
            if (phi >= Mathf.PI) { phi -= Mathf.PI; s = -s; }
            int ia = Mathf.Clamp((int)(phi / Mathf.PI * NAng), 0, NAng - 1);
            int ir = (int)((s + Fov / 2) / Fov * NRad);
            if (ir < 0 || ir >= NRad) return;
            lock (sinoLock) Sino[ia * NRad + ir]++;
        }

        public int[] SnapshotSino()
        {
            lock (sinoLock) return (int[])Sino.Clone();
        }

        // ------------------------------------------------------------------ reconstruction
        /// <summary>Attenuation correction factors for each sinogram bin, exp(+∫μ).</summary>
        public float[] Acf()
        {
            var acf = new float[NAng * NRad];
            for (int ia = 0; ia < NAng; ia++)
            {
                float phi = (ia + 0.5f) / NAng * Mathf.PI;
                var n = new Vector2(Mathf.Cos(phi), Mathf.Sin(phi));
                var d = new Vector2(-n.y, n.x);
                for (int ir = 0; ir < NRad; ir++)
                {
                    float s = -Fov / 2 + (ir + 0.5f) * Fov / NRad;
                    acf[ia * NRad + ir] = (float)Math.Exp(MuIntegral(n * s, d));
                }
            }
            return acf;
        }

        /// <summary>MLEM reconstruction (ray-driven projector, 2 samples per pixel).</summary>
        public float[] Mlem(int[] sino, int iterations, bool attenuationCorr, Action<int, float[]> progress = null)
        {
            int N = ImgN;
            float px = Fov / N;
            var img = new float[N * N];
            for (int i = 0; i < img.Length; i++) img[i] = 1;
            var acf = attenuationCorr ? Acf() : null;
            // attenuation factors enter the system model as a_i = 1/ACF
            var sens = new float[N * N];
            Project(null, sens, acf, true);
            for (int it = 0; it < iterations; it++)
            {
                var fp = new float[NAng * NRad];
                Project(img, fp, acf, false);
                var ratio = new float[NAng * NRad];
                for (int i = 0; i < ratio.Length; i++) ratio[i] = fp[i] > 1e-6f ? sino[i] / fp[i] : 0;
                var bp = new float[N * N];
                BackProject(ratio, bp, acf);
                for (int i = 0; i < img.Length; i++) img[i] = sens[i] > 1e-6f ? img[i] * bp[i] / sens[i] : 0;
                progress?.Invoke(it + 1, img);
            }
            return img;
        }

        // forward projection (or sensitivity image when 'sensitivity' is true)
        void Project(float[] img, float[] outArr, float[] acf, bool sensitivity)
        {
            int N = ImgN; float px = Fov / N, half = Fov / 2;
            float step = px * 0.5f;
            for (int ia = 0; ia < NAng; ia++)
            {
                float phi = (ia + 0.5f) / NAng * Mathf.PI;
                var n = new Vector2(Mathf.Cos(phi), Mathf.Sin(phi));
                var d = new Vector2(-n.y, n.x);
                for (int ir = 0; ir < NRad; ir++)
                {
                    float s = -half + (ir + 0.5f) * Fov / NRad;
                    float a = acf != null ? 1f / acf[ia * NRad + ir] : 1f;
                    float acc = 0;
                    for (float t = -half; t <= half; t += step)
                    {
                        var p = n * s + d * t;
                        int ix = (int)((p.x + half) / px), iy = (int)((p.y + half) / px);
                        if (ix < 0 || iy < 0 || ix >= N || iy >= N) continue;
                        if (sensitivity) outArr[iy * N + ix] += a * step;
                        else acc += img[iy * N + ix] * step;
                    }
                    if (!sensitivity) outArr[ia * NRad + ir] = acc * a;
                }
            }
        }

        void BackProject(float[] sino, float[] img, float[] acf)
        {
            int N = ImgN; float px = Fov / N, half = Fov / 2;
            float step = px * 0.5f;
            for (int ia = 0; ia < NAng; ia++)
            {
                float phi = (ia + 0.5f) / NAng * Mathf.PI;
                var n = new Vector2(Mathf.Cos(phi), Mathf.Sin(phi));
                var d = new Vector2(-n.y, n.x);
                for (int ir = 0; ir < NRad; ir++)
                {
                    float v = sino[ia * NRad + ir];
                    if (v == 0) continue;
                    float a = acf != null ? 1f / acf[ia * NRad + ir] : 1f;
                    float s = -half + (ir + 0.5f) * Fov / NRad;
                    for (float t = -half; t <= half; t += step)
                    {
                        var p = n * s + d * t;
                        int ix = (int)((p.x + half) / px), iy = (int)((p.y + half) / px);
                        if (ix < 0 || iy < 0 || ix >= N || iy >= N) continue;
                        img[iy * N + ix] += v * a * step;
                    }
                }
            }
        }

        /// <summary>Filtered back-projection (Hann-windowed ramp) with optional attenuation correction.</summary>
        public float[] Fbp(int[] sino, bool attenuationCorr)
        {
            int N = ImgN; float half = Fov / 2, px = Fov / N, ds = Fov / NRad;
            var acf = attenuationCorr ? Acf() : null;
            int nfft = Fft.NextPow2(2 * NRad);
            var h = new double[nfft]; var hi = new double[nfft];
            for (int k = 0; k < nfft; k++)
            {
                int n = k <= nfft / 2 ? k : k - nfft;
                h[k] = n == 0 ? 1.0 / (4 * ds * ds) : (n % 2 != 0 ? -1.0 / (n * n * Math.PI * Math.PI * ds * ds) : 0);
            }
            Fft.Transform(h, hi, -1);
            var img = new float[N * N];
            var re = new double[nfft]; var im = new double[nfft];
            for (int ia = 0; ia < NAng; ia++)
            {
                Array.Clear(re, 0, nfft); Array.Clear(im, 0, nfft);
                for (int ir = 0; ir < NRad; ir++) re[ir] = sino[ia * NRad + ir] * (acf != null ? acf[ia * NRad + ir] : 1f);
                Fft.Transform(re, im, -1);
                for (int k = 0; k < nfft; k++)
                {
                    double f = (k <= nfft / 2 ? k : nfft - k) / (double)nfft;
                    double w = 0.5 * (1 + Math.Cos(2 * Math.PI * f)) * h[k] * ds;
                    re[k] *= w; im[k] *= w;
                }
                Fft.Transform(re, im, 1);
                float phi = (ia + 0.5f) / NAng * Mathf.PI;
                float c = Mathf.Cos(phi), sn = Mathf.Sin(phi);
                for (int iy = 0; iy < N; iy++)
                    for (int ix = 0; ix < N; ix++)
                    {
                        float x = -half + (ix + 0.5f) * px, y = -half + (iy + 0.5f) * px;
                        float s = x * c + y * sn;
                        float u = (s + half) / ds - 0.5f;
                        int i0 = (int)Mathf.Floor(u);
                        if (i0 < 0 || i0 >= NRad - 1) continue;
                        float fr = u - i0;
                        img[iy * N + ix] += (float)(re[i0] * (1 - fr) + re[i0 + 1] * fr) / nfft;
                    }
            }
            for (int i = 0; i < img.Length; i++) img[i] = Mathf.Max(0, img[i]);
            return img;
        }
    }
}
