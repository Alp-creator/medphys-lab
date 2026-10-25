using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace MedPhys.Physics
{
    public enum CTFilter { RamLak, SheppLogan, Cosine, Hann }

    public sealed class CTSettings
    {
        public float SliceZ = 52f;      // phys z of the axial slice (cm)
        public float Kvp = 120f;
        public float Mas = 150f;
        public int NProj = 360;          // views over 180°
        public int NDet = 256;           // detector bins
        public float FovCm = 50f;
        public bool Poly = true;         // polyenergetic beam (beam hardening) vs. ideal monoenergetic
        public bool Bhc = true;          // water beam-hardening correction
        public bool Noise = true;
        public CTFilter Filter = CTFilter.SheppLogan;
        public int Res = 256;
        public int Seed = 1;
    }

    /// <summary>
    /// Axial CT acquisition and filtered back-projection.
    /// Projections p = -ln(I/I0) are computed by ray tracing through the analytic phantom with the full
    /// polyenergetic spectrum (energy-integrating detector) or an ideal monoenergetic beam. Quantum noise is
    /// Poisson in the detected counts. Optional water beam-hardening correction linearises p.
    /// Reconstruction: parallel-beam FBP with a band-limited ramp (Ram-Lak) times an apodisation window,
    /// μ → HU with water calibration. Runs progressively in a background task.
    /// </summary>
    public sealed class CTJob
    {
        public Phantom Ph;
        public CTSettings S;

        public float[] Sino;            // [view * NDet + det]
        public float[] Accum;           // back-projection accumulator (μ, unscaled)
        public float[] Hu;              // final HU image (Res x Res, row 0 = posterior)
        public volatile int ViewsDone;
        public bool Done, Failed;
        public string Error;
        public double MuWaterRef, EffKeV, MeanKeV;
        public float SinoMax = 1;

        // tissues: expected HU for this beam (reference table for the probe)
        public float[] ExpectedHu = new float[(int)TissueId.Count];

        XRaySpectrum spec;
        float[] w;                      // detected spectral weights (normalised)
        float[,] mu;                    // [tissue, bin]
        float[] muMono;                 // [tissue] at effective energy
        float[] bhcP, bhcL;             // water calibration table p(L)
        double[] filterH;               // frequency response incl. window
        int nfft;
        int cores = 4;

        public void Start()
        {
            cores = Math.Max(1, SystemInfo.processorCount - 1);
            Sino = new float[S.NProj * S.NDet];
            Accum = new float[S.Res * S.Res];
            Hu = new float[S.Res * S.Res];
            Prepare();
            Task.Run(() =>
            {
                try { Run(); }
                catch (Exception e) { Error = e.ToString(); Failed = true; }
                Done = true;
            });
        }

        void Prepare()
        {
            // CT tubes carry heavy filtration (≈ 6 mm Al + 0.1 mm Cu incl. bow-tie centre)
            spec = XRaySpectrum.Generate(S.Kvp, 5.2f, 0.1f, 2f);
            int nb = spec.Count;
            w = new float[nb];
            double sum = 0;
            for (int b = 0; b < nb; b++) { w[b] = (float)(spec.N[b] * spec.E[b]); sum += w[b]; }
            for (int b = 0; b < nb; b++) w[b] /= (float)sum;
            MeanKeV = spec.MeanEnergy();
            mu = new float[(int)TissueId.Count, nb];
            for (int t = 0; t < (int)TissueId.Count; t++)
                for (int b = 0; b < nb; b++) mu[t, b] = (float)Tissues.Get((TissueId)t).Mu(spec.E[b] / 1000.0);
            // effective energy: monoenergetic energy giving the same attenuation of 20 cm water
            var water = Tissues.Get(TissueId.Water);
            double pw20 = PolyWater(20);
            double muEff = pw20 / 20.0;
            double lo = 20, hi = S.Kvp;
            for (int it = 0; it < 50; it++)
            {
                double mid = 0.5 * (lo + hi);
                if (water.Mu(mid / 1000.0) > muEff) lo = mid; else hi = mid;
            }
            EffKeV = 0.5 * (lo + hi);
            muMono = new float[(int)TissueId.Count];
            for (int t = 0; t < (int)TissueId.Count; t++) muMono[t] = (float)Tissues.Get((TissueId)t).Mu(EffKeV / 1000.0);
            MuWaterRef = water.Mu(EffKeV / 1000.0);
            double muAir = Tissues.Get(TissueId.Air).Mu(EffKeV / 1000.0);
            for (int t = 0; t < (int)TissueId.Count; t++)
                ExpectedHu[t] = (float)(1000.0 * (muMono[t] - MuWaterRef) / (MuWaterRef - muAir));
            // water calibration table for the beam-hardening correction
            int nt = 241;
            bhcL = new float[nt]; bhcP = new float[nt];
            for (int i = 0; i < nt; i++) { bhcL[i] = i * 0.25f; bhcP[i] = (float)PolyWater(bhcL[i]); }
            // filter
            nfft = Fft.NextPow2(2 * S.NDet);
            double tau = S.FovCm / S.NDet;
            var hr = new double[nfft]; var hi2 = new double[nfft];
            for (int k = 0; k < nfft; k++)
            {
                int n = k <= nfft / 2 ? k : k - nfft;
                double v;
                if (n == 0) v = 1.0 / (4 * tau * tau);
                else if ((n & 1) == 1 || (n & 1) == -1) v = -1.0 / (n * n * Math.PI * Math.PI * tau * tau);
                else v = 0;
                hr[k] = v;
            }
            Fft.Transform(hr, hi2, -1);
            filterH = new double[nfft];
            for (int k = 0; k < nfft; k++)
            {
                double f = (k <= nfft / 2 ? k : nfft - k) / (double)nfft;   // 0 .. 0.5 cycles/sample
                double win;
                switch (S.Filter)
                {
                    case CTFilter.SheppLogan: win = f == 0 ? 1 : Math.Sin(Math.PI * f) / (Math.PI * f); break;
                    case CTFilter.Cosine: win = Math.Cos(Math.PI * f); break;
                    case CTFilter.Hann: win = 0.5 * (1 + Math.Cos(2 * Math.PI * f)); break;
                    default: win = 1; break;
                }
                filterH[k] = hr[k] * win * tau;
            }
        }

        double PolyWater(double L)
        {
            var water = Tissues.Get(TissueId.Water);
            double s = 0;
            for (int b = 0; b < w.Length; b++) s += w[b] * Math.Exp(-water.Mu(spec.E[b] / 1000.0) * L);
            return -Math.Log(s);
        }

        float Bhc(float p)
        {
            // invert p(L) of water, return μ_ref * L
            if (p <= 0) return 0;
            int n = bhcP.Length;
            if (p >= bhcP[n - 1]) return (float)(MuWaterRef * bhcL[n - 1] * p / bhcP[n - 1]);
            int lo = 0, hi = n - 1;
            while (hi - lo > 1) { int m = (lo + hi) >> 1; if (bhcP[m] <= p) lo = m; else hi = m; }
            float t = (p - bhcP[lo]) / Mathf.Max(bhcP[hi] - bhcP[lo], 1e-9f);
            return (float)(MuWaterRef * (bhcL[lo] + t * (bhcL[hi] - bhcL[lo])));
        }

        void Run()
        {
            int nP = S.NProj, nD = S.NDet, res = S.Res;
            float half = S.FovCm * 0.5f, ds = S.FovCm / nD;
            const float R = 40f;
            double n0 = 8.8e5 * (S.Mas / 100.0) * (360.0 / nP) * Math.Pow(S.Kvp / 120.0, 2.2);
            int nb = w.Length;
            var po = new ParallelOptions { MaxDegreeOfParallelism = cores };
            float px = S.FovCm / res;
            int batch = Math.Max(4, nP / 45);
            var cosT = new float[nP]; var sinT = new float[nP];
            for (int k = 0; k < nP; k++) { double th = Math.PI * k / nP; cosT[k] = (float)Math.Cos(th); sinT[k] = (float)Math.Sin(th); }
            var filtered = new float[nP * nD];

            for (int k0 = 0; k0 < nP; k0 += batch)
            {
                int k1 = Math.Min(nP, k0 + batch);
                // ---- acquisition (+ filtering) of this batch of views
                Parallel.For(k0, k1, po, () => (new PhantomTracer(Ph), new float[(int)TissueId.Count]),
                    (k, st, local) =>
                    {
                        var (tr, len) = local;
                        var nrm = new Vector3(cosT[k], sinT[k], 0);
                        var dir = new Vector3(-sinT[k], cosT[k], 0);
                        // per-view RNG stream (deterministic regardless of thread)
                        var vr = new Rng((ulong)(S.Seed * 7919 + k * 104729 + 1));
                        for (int j = 0; j < nD; j++)
                        {
                            float s = -half + (j + 0.5f) * ds;
                            var o = nrm * s - dir * R + new Vector3(0, 0, S.SliceZ);
                            Array.Clear(len, 0, len.Length);
                            tr.Trace(o, dir, 2 * R, len);
                            float p;
                            if (S.Poly)
                            {
                                double tsum = 0;
                                for (int b = 0; b < nb; b++)
                                {
                                    float e = 0;
                                    for (int t = 0; t < len.Length; t++) if (len[t] > 0) e += mu[t, b] * len[t];
                                    tsum += w[b] * Math.Exp(-e);
                                }
                                p = (float)-Math.Log(Math.Max(tsum, 1e-30));
                            }
                            else
                            {
                                float e = 0;
                                for (int t = 0; t < len.Length; t++) if (len[t] > 0) e += muMono[t] * len[t];
                                p = e;
                            }
                            if (S.Noise)
                            {
                                double mean = n0 * Math.Exp(-p);
                                double cnt = mean + Math.Sqrt(mean + 25.0) * vr.Gauss();   // quantum + small electronic noise
                                cnt = Math.Max(cnt, 0.5);
                                p = (float)-Math.Log(cnt / n0);
                            }
                            if (S.Poly && S.Bhc) p = Bhc(p);
                            Sino[k * nD + j] = p;
                        }
                        // ramp filtering of this view
                        var re = new double[nfft]; var im = new double[nfft];
                        for (int j = 0; j < nD; j++) re[j] = Sino[k * nD + j];
                        Fft.Transform(re, im, -1);
                        for (int f = 0; f < nfft; f++) { re[f] *= filterH[f]; im[f] *= filterH[f]; }
                        Fft.Transform(re, im, 1);
                        for (int j = 0; j < nD; j++) filtered[k * nD + j] = (float)(re[j] / nfft);
                        return local;
                    }, _ => { });

                // ---- back-projection of this batch into the accumulator
                Parallel.For(0, res, po, iy =>
                {
                    float y = -half + (iy + 0.5f) * px;
                    for (int ix = 0; ix < res; ix++)
                    {
                        float x = -half + (ix + 0.5f) * px;
                        if (x * x + y * y > half * half) continue;
                        float acc = 0;
                        for (int k = k0; k < k1; k++)
                        {
                            float s = x * cosT[k] + y * sinT[k];
                            float u = (s + half) / ds - 0.5f;
                            int i0 = (int)Math.Floor(u);
                            if (i0 < 0 || i0 >= nD - 1) continue;
                            float fr = u - i0;
                            int bse = k * nD;
                            acc += filtered[bse + i0] * (1 - fr) + filtered[bse + i0 + 1] * fr;
                        }
                        Accum[iy * res + ix] += acc;
                    }
                });
                ViewsDone = k1;
            }
            float mx = 0;
            for (int i = 0; i < Sino.Length; i++) mx = Math.Max(mx, Sino[i]);
            SinoMax = Math.Max(mx, 1e-3f);
            ToHu(Hu, nP);
        }

        /// <summary>Converts the accumulator (after 'views' views) into HU.</summary>
        public void ToHu(float[] dst, int views)
        {
            if (views <= 0) return;
            double scale = Math.PI / S.NProj;
            double muAir = Tissues.Get(TissueId.Air).Mu(EffKeV / 1000.0);
            double norm = (double)S.NProj / views;
            float half = S.FovCm * 0.5f, px = S.FovCm / S.Res;
            for (int iy = 0; iy < S.Res; iy++)
                for (int ix = 0; ix < S.Res; ix++)
                {
                    int i = iy * S.Res + ix;
                    float x = -half + (ix + 0.5f) * px, y = -half + (iy + 0.5f) * px;
                    if (x * x + y * y > half * half) { dst[i] = -1024; continue; }
                    double m = Accum[i] * scale * norm;
                    dst[i] = (float)Math.Max(-1024, Math.Min(3071, 1000.0 * (m - MuWaterRef) / (MuWaterRef - muAir)));
                }
        }

        /// <summary>Phys-frame (x, y) of the centre of an image pixel.</summary>
        public Vector2 PixelToPhys(int ix, int iy)
        {
            float half = S.FovCm * 0.5f, px = S.FovCm / S.Res;
            return new Vector2(-half + (ix + 0.5f) * px, -half + (iy + 0.5f) * px);
        }

        /// <summary>Volume CT dose index (mGy) and conversion coefficient by body region.</summary>
        public static void Dose(CTSettings s, float scanLengthCm, out double ctdiVol, out double dlp, out double eff, out bool head)
        {
            head = s.SliceZ > 76f;
            double nCtdiW = (head ? 15.0 : 7.0) * Math.Pow(s.Kvp / 120.0, 2.6);   // mGy per 100 mAs (16 / 32 cm phantoms)
            ctdiVol = nCtdiW * s.Mas / 100.0;                                  // pitch 1
            dlp = ctdiVol * scanLengthCm;
            double k = head ? 0.0021 : s.SliceZ > 40 ? 0.014 : s.SliceZ > 15 ? 0.015 : 0.015;
            eff = dlp * k;
        }
    }
}
