using System.Collections.Generic;
using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace MedPhys.Physics
{
    public sealed class RadiographySettings
    {
        public float Kvp = 110f;
        public float Mas = 2.5f;
        public float AlMm = 1.7f;           // added filtration
        public float CuMm = 0f;
        public bool Grid = true;
        public int Res = 256;
        public float DetectorCm = 43f;      // square flat panel
        public float FieldWcm = 35f, FieldHcm = 43f;   // collimated field at the detector
        public float CsICm = 0.06f;         // scintillator thickness (600 µm CsI)
        public float EiTarget = 300f;       // exposure index target (EI = 100 x K_det[µGy])
        public float EffDoseCoeff = 0.18f;  // mSv per Gy·cm² (DAP -> effective dose, region dependent)
        public int Seed = 1;
    }

    /// <summary>
    /// Polyenergetic projection radiography by analytic ray tracing through the phantom.
    /// For every detector pixel: Beer-Lambert attenuation per energy bin using NIST µ(E) of each tissue,
    /// energy-integrating CsI detector with absorption efficiency η(E) = 1 - exp(-µ_CsI t),
    /// inverse-square fluence, quantum (Poisson) noise, a simple scatter model and an anti-scatter grid.
    /// Geometry is given in the phantom's phys frame (cm).
    /// </summary>
    public sealed class RadiographyJob
    {
        // inputs
        public Phantom Ph;
        public RadiographySettings S;
        public Vector3 Source, DetCenter, U, V;     // U, V: unit vectors along detector rows/cols

        // outputs
        public int Res;
        public float[] Primary, Scatter, Signal, Display;
        float[] clean;
        public bool[] InField, InBody;
        public volatile int RowsDone;
        public bool Done, Failed;
        public string Error;
        public XRaySpectrum Spectrum, DetSpectrum;   // spectrum at entrance and (central ray) at detector
        public double EsakMGy, DapGyCm2, EffDoseMSv, Ei, Di, Snr, KdetUGy, BoneContrast, SsdCm, ThicknessCm, MeanEnergyDet;

        float[,] mu;      // [tissue, bin]
        int cores = 4;
        float[] eta, eKeV, phi1;

        public void Start()
        {
            Res = S.Res;
            int n = Res * Res;
            Primary = new float[n]; Scatter = new float[n]; Signal = new float[n]; Display = new float[n]; InField = new bool[n]; InBody = new bool[n];
            cores = Math.Max(1, SystemInfo.processorCount - 1);
            Spectrum = XRaySpectrum.Generate(S.Kvp, S.AlMm, S.CuMm, 2f);
            int nb = Spectrum.Count;
            mu = new float[(int)TissueId.Count, nb];
            eta = new float[nb]; eKeV = new float[nb]; phi1 = new float[nb];
            var csi = Nist.Get(Nist.Id.CsI);
            for (int b = 0; b < nb; b++)
            {
                double eMeV = Spectrum.E[b] / 1000.0;
                eKeV[b] = (float)Spectrum.E[b];
                phi1[b] = (float)Spectrum.N[b];
                eta[b] = (float)(1 - Math.Exp(-csi.Mu(eMeV) * S.CsICm));
                for (int t = 0; t < (int)TissueId.Count; t++) mu[t, b] = (float)Tissues.Get((TissueId)t).Mu(eMeV);
            }
            Task.Run(() =>
            {
                try { Compute(); }
                catch (Exception e) { Error = e.ToString(); Failed = true; }
                Done = true;
            });
        }

        void Compute()
        {
            int nb = eKeV.Length;
            float pix = S.DetectorCm / Res;
            float pixArea = pix * pix;
            float half = S.DetectorCm * 0.5f;
            var po = new ParallelOptions { MaxDegreeOfParallelism = cores };
            float[] varArr = new float[Res * Res];
            float[] eIn = new float[Res * Res];
            Parallel.For(0, Res, po, () => (new PhantomTracer(Ph), new float[(int)TissueId.Count], new float[nb]), (j, st, local) =>
            {
                var (tr, len, ex) = local;
                for (int i = 0; i < Res; i++)
                {
                    int idx = j * Res + i;
                    float x = -half + (i + 0.5f) * pix, y = -half + (j + 0.5f) * pix;
                    bool inField = Mathf.Abs(x) <= S.FieldWcm * 0.5f && Mathf.Abs(y) <= S.FieldHcm * 0.5f;
                    InField[idx] = inField;
                    if (!inField) continue;
                    Vector3 P = DetCenter + U * x + V * y;
                    Vector3 d = P - Source;
                    float dist = d.magnitude;
                    d /= dist;
                    Array.Clear(len, 0, len.Length);
                    float inside = tr.Trace(Source, d, dist, len);
                    InBody[idx] = inside > 0.5f;
                    len[(int)TissueId.Air] += Mathf.Max(0, dist - inside);
                    float inv2 = (100f / dist) * (100f / dist) * S.Mas * pixArea;
                    double sig = 0, var = 0, ein = 0;
                    for (int b = 0; b < nb; b++)
                    {
                        float e = 0;
                        for (int t = 0; t < len.Length; t++) if (len[t] > 0) e += mu[t, b] * len[t];
                        double ph = phi1[b] * inv2;
                        double absq = ph * Math.Exp(-e) * eta[b];
                        sig += absq * eKeV[b];
                        var += absq * eKeV[b] * eKeV[b];
                        ein += ph * eta[b] * eKeV[b];
                    }
                    Primary[idx] = (float)sig;
                    varArr[idx] = (float)var;
                    eIn[idx] = (float)ein;
                }
                Interlocked.Increment(ref RowsDone);
                return local;
            }, _ => { });

            // ---- scatter: blurred fraction of the energy removed from the primary beam
            float fieldArea = S.FieldWcm * S.FieldHcm;
            float kappa = 0.25f * (1f - Mathf.Exp(-fieldArea / 600f)) * Mathf.Clamp(S.Kvp / 90f, 0.7f, 1.3f);
            float[] removed = new float[Res * Res];
            for (int k = 0; k < removed.Length; k++) removed[k] = InField[k] ? Mathf.Max(0, eIn[k] - Primary[k]) : 0;
            float sigmaPx = 7f / pix;   // ~7 cm scatter spread at the detector
            BoxBlur(removed, Res, Mathf.Max(1, (int)(sigmaPx * 1.2f)), 3);
            float tp = S.Grid ? 0.70f : 1f, ts = S.Grid ? 0.12f : 1f;
            var rng = new Rng((ulong)(S.Seed * 7919 + 17));
            double sumSig = 0, sumVar = 0; int cnt = 0;
            clean = new float[Res * Res];
            for (int k = 0; k < removed.Length; k++)
            {
                if (!InField[k]) { Signal[k] = 0; continue; }
                float p = Primary[k] * tp;
                float sc = kappa * removed[k] * ts;
                Scatter[k] = sc;
                clean[k] = p + sc;
                float eEff = Primary[k] > 0 ? varArr[k] / Primary[k] : 50f;
                float v = (p + sc) * eEff;
                float noisy = p + sc + (float)(Math.Sqrt(Math.Max(v, 0)) * rng.Gauss());
                Signal[k] = Mathf.Max(noisy, 1e-3f);
                // SNR statistics from the central region
                int ii = k % Res, jj = k / Res;
                if (Mathf.Abs(ii - Res / 2) < Res / 8 && Mathf.Abs(jj - Res / 2) < Res / 8)
                {
                    sumSig += p + sc; sumVar += v; cnt++;
                }
            }
            Snr = cnt > 0 ? (sumSig / cnt) / Math.Sqrt(sumVar / cnt) : 0;
            MakeDisplay();
            Dosimetry();
        }

        void MakeDisplay()
        {
            int n = Res * Res;
            var logs = new float[n];
            int m = 0;
            var vals = new float[n];
            for (int k = 0; k < n; k++)
            {
                if (!InField[k]) continue;
                logs[k] = Mathf.Log(Signal[k]);
                // like clinical DR processing, the window is set from the anatomy only (direct exposure excluded)
                if (InBody[k]) vals[m++] = logs[k];
            }
            if (m < 50)
            {
                m = 0;
                for (int k = 0; k < n; k++) if (InField[k]) vals[m++] = logs[k];
            }
            if (m == 0) return;
            Array.Sort(vals, 0, m);
            float lo = vals[(int)(m * 0.005f)], hi = vals[Math.Min(m - 1, (int)(m * 0.995f))];
            if (hi - lo < 1e-3f) hi = lo + 1e-3f;
            for (int k = 0; k < n; k++)
            {
                if (!InField[k]) { Display[k] = 0.92f; continue; }
                float t = Mathf.Clamp01((logs[k] - lo) / (hi - lo));
                // radiographic convention: high attenuation (low signal) is bright
                float g = 1f - t;
                Display[k] = Mathf.Clamp01(g * g * (3f - 2f * g) * 0.85f + g * 0.15f);
            }
        }

        void Dosimetry()
        {
            // central ray thickness
            var tr = new PhantomTracer(Ph);
            var len = new float[(int)TissueId.Count];
            Vector3 d = (DetCenter - Source); float dist = d.magnitude; d /= dist;
            ThicknessCm = tr.Trace(Source, d, dist, len);
            SsdCm = Mathf.Max(30f, dist - (float)ThicknessCm - 1f);
            double k1m = Spectrum.AirKermaPerMas();                  // µGy/mAs at 100 cm
            EsakMGy = k1m * S.Mas * Math.Pow(100.0 / SsdCm, 2) * 1.35 / 1000.0;
            // DAP: kerma at 1 m times field area projected to 1 m
            double scale = 100.0 / dist;
            double area1m = S.FieldWcm * scale * S.FieldHcm * scale;
            DapGyCm2 = k1m * S.Mas * 1e-6 * area1m;
            EffDoseMSv = DapGyCm2 * S.EffDoseCoeff;
            // spectrum reaching the detector along the central ray (for the plot + detector kerma)
            DetSpectrum = Spectrum.Clone();
            for (int b = 0; b < DetSpectrum.Count; b++)
            {
                float e = 0;
                for (int t = 0; t < len.Length; t++) if (len[t] > 0) e += mu[t, b] * len[t];
                DetSpectrum.N[b] *= Math.Exp(-e);
            }
            MeanEnergyDet = DetSpectrum.MeanEnergy();
            // Exposure index (IEC 62494-1): detector air kerma from the image "value of interest" — the median of the
            // anatomy pixels — not from a single ray. Signal → kerma via an unattenuated reference pixel.
            double sFree = 0;
            float pix = S.DetectorCm / Res;
            for (int b = 0; b < eKeV.Length; b++) sFree += phi1[b] * Math.Pow(100.0 / dist, 2) * S.Mas * pix * pix * eta[b] * eKeV[b];
            double kFree = Spectrum.AirKermaPerMas() * S.Mas * Math.Pow(100.0 / dist, 2);
            var voi = new List<float>();
            for (int k = 0; k < clean.Length; k++) if (InField[k] && InBody[k]) voi.Add(clean[k]);
            if (voi.Count < 50) { voi.Clear(); for (int k = 0; k < clean.Length; k++) if (InField[k]) voi.Add(clean[k]); }
            double kdet;
            if (voi.Count > 0 && sFree > 0) { voi.Sort(); kdet = voi[voi.Count / 2] * kFree / sFree; }
            else kdet = DetSpectrum.AirKermaPerMas() * S.Mas * Math.Pow(100.0 / dist, 2) * (S.Grid ? 0.7 : 1.0);
            KdetUGy = kdet;
            Ei = 100.0 * kdet;
            Di = 10.0 * Math.Log10(Math.Max(Ei, 1e-6) / S.EiTarget);
            // subject contrast of 1 cm cortical bone in 20 cm soft tissue for this spectrum
            var soft = Tissues.Get(TissueId.Soft); var bone = Tissues.Get(TissueId.CorticalBone);
            double a = 0, b2 = 0;
            for (int b = 0; b < Spectrum.Count; b++)
            {
                double eMeV = Spectrum.E[b] / 1000.0;
                double baseT = Spectrum.N[b] * Math.Exp(-soft.Mu(eMeV) * 19.0) * eta[b] * eKeV[b];
                a += baseT * Math.Exp(-soft.Mu(eMeV) * 1.0);
                b2 += baseT * Math.Exp(-bone.Mu(eMeV) * 1.0);
            }
            BoneContrast = a > 0 ? (a - b2) / a : 0;
        }

        /// <summary>In-place separable box blur, repeated (≈ Gaussian).</summary>
        public static void BoxBlur(float[] img, int res, int r, int passes)
        {
            var tmp = new float[img.Length];
            for (int p = 0; p < passes; p++)
            {
                for (int y = 0; y < res; y++)
                {
                    float acc = 0; int row = y * res;
                    for (int x = -r; x <= r; x++) acc += img[row + Mathf.Clamp(x, 0, res - 1)];
                    for (int x = 0; x < res; x++)
                    {
                        tmp[row + x] = acc / (2 * r + 1);
                        acc += img[row + Mathf.Min(x + r + 1, res - 1)] - img[row + Mathf.Max(x - r, 0)];
                    }
                }
                for (int x = 0; x < res; x++)
                {
                    float acc = 0;
                    for (int y = -r; y <= r; y++) acc += tmp[Mathf.Clamp(y, 0, res - 1) * res + x];
                    for (int y = 0; y < res; y++)
                    {
                        img[y * res + x] = acc / (2 * r + 1);
                        acc += tmp[Mathf.Min(y + r + 1, res - 1) * res + x] - tmp[Mathf.Max(y - r, 0) * res + x];
                    }
                }
            }
        }
    }
}
