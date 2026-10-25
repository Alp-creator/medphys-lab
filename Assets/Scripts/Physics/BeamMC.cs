using System;
using System.Collections.Concurrent;
using System.Threading;
using UnityEngine;

namespace MedPhys.Physics
{
    public enum BeamKind { Photon, Electron, Proton }

    public sealed class BeamSpec
    {
        public BeamKind Kind = BeamKind.Photon;
        public float Energy = 6f;        // photons: nominal MV (1.25 = Co-60); electrons/protons: kinetic MeV
        public float FieldCm = 10f;      // square field at the surface
        public float SsdCm = 100f;
        public float EnergySpread = 0.01f;  // relative sigma for particle beams

        public string Label => Kind == BeamKind.Photon ? (Energy < 2 ? "Co-60" : Energy.ToString("0") + " MV")
                                                      : Energy.ToString("0") + " MeV " + (Kind == BeamKind.Electron ? "e⁻" : "p⁺");
    }

    /// <summary>
    /// Condensed-history Monte Carlo of photon, electron and proton beams in a water phantom
    /// (x, y lateral, z = depth, cm). Educational but physically grounded:
    ///  • photons: NIST μ(E) of water; Compton with Klein–Nishina (Kahn sampling); pair production above
    ///    1.022 MeV (remainder of μ after Compton); photo-absorption at low energy; positron annihilation;
    ///  • electrons/positrons: continuous slowing down with Bethe (Rohrlich–Carlson) stopping power,
    ///    Highland multiple scattering, stochastic bremsstrahlung emission;
    ///  • protons: Bethe–Bloch energy loss, Bohr energy straggling, Highland MCS, nuclear removal
    ///    (≈1.2 %/cm) with partial local deposition.
    /// Energy deposition is scored on a central-axis depth grid and an x–z plane.
    /// </summary>
    public sealed class WaterMC
    {
        public readonly BeamSpec Beam;
        public float Half = 20f, Depth = 40f;
        public readonly int NZ, NX;
        public readonly float Dz, Dx;
        public float CaxRadius;

        public sealed class Scorer
        {
            public double[] Cax, Map;
            public long Histories;
        }

        public readonly Scorer[] Scorers;
        public readonly ConcurrentQueue<(Vector3 a, Vector3 b, byte type)> Tracks = new ConcurrentQueue<(Vector3, Vector3, byte)>();
        public volatile bool Running;
        public int TrackEvery = 40;           // record tracks of every n-th history of worker 0
        public int MaxQueuedTracks = 6000;
        Thread[] threads;

        // photon spectrum for MV beams (cumulative)
        double[] specE, specCdf;

        public WaterMC(BeamSpec beam, int workers = 0)
        {
            Beam = beam;
            Dz = beam.Kind == BeamKind.Proton ? 0.2f : 0.25f;
            NZ = Mathf.RoundToInt(Depth / Dz);
            Dx = 0.5f; NX = Mathf.RoundToInt(2 * Half / Dx);
            CaxRadius = Mathf.Clamp(beam.FieldCm * 0.3f, 0.6f, 3f);
            if (workers <= 0) workers = Math.Max(1, Math.Min(6, SystemInfo.processorCount - 1));
            Scorers = new Scorer[workers];
            for (int i = 0; i < workers; i++) Scorers[i] = new Scorer { Cax = new double[NZ], Map = new double[NZ * NX] };
            if (beam.Kind == BeamKind.Photon) BuildSpectrum();
            // warm the stopping-power tables on this thread
            StoppingPower.ElectronRange(1); StoppingPower.ProtonRange(100);
        }

        // ------------------------------------------------------------------ MV spectrum
        void BuildSpectrum()
        {
            int n = 120;
            specE = new double[n]; specCdf = new double[n];
            if (Beam.Energy < 2f)
            {
                // Co-60: 1.17 and 1.33 MeV lines plus ~10 % low-energy scattered component from the head
                for (int i = 0; i < n; i++) specE[i] = 0.1 + 1.25 * i / (n - 1);
                double acc = 0;
                for (int i = 0; i < n; i++)
                {
                    double e = specE[i];
                    double w = Math.Abs(e - 1.173) < 0.006 ? 1.0 * n : Math.Abs(e - 1.333) < 0.006 ? 1.0 * n : 0.08;
                    acc += w; specCdf[i] = acc;
                }
                // ensure the lines exist even if the grid misses them
                specE[60] = 1.173; specE[90] = 1.333;
                acc = 0;
                for (int i = 0; i < n; i++) { double w = (i == 60 || i == 90) ? 45 : 0.08; acc += w; specCdf[i] = acc; }
            }
            else
            {
                // thick-target bremsstrahlung (Kramers form) filtered by the target and the flattening filter
                double e0 = Beam.Energy;
                var fe = Nist.Get(Nist.Id.Iron); var w = Nist.Get(Nist.Id.Tungsten);
                double feCm = 2.4 + 0.15 * e0, wCm = 0.08;
                double acc = 0;
                for (int i = 0; i < n; i++)
                {
                    double e = 0.15 + (e0 - 0.15) * (i + 0.5) / n;
                    specE[i] = e;
                    double phi = (e0 - e) / e;
                    phi *= Math.Exp(-fe.Mu(e) * feCm) * Math.Exp(-w.Mu(e) * wCm);
                    acc += Math.Max(phi, 0); specCdf[i] = acc;
                }
            }
            for (int i = 0; i < n; i++) specCdf[i] /= specCdf[n - 1];
        }

        public double MeanSpectrumEnergy()
        {
            if (specE == null) return Beam.Energy;
            double prev = 0, m = 0;
            for (int i = 0; i < specE.Length; i++) { m += (specCdf[i] - prev) * specE[i]; prev = specCdf[i]; }
            return m;
        }

        public (double e, double w)[] SpectrumBins()
        {
            if (specE == null) return Array.Empty<(double, double)>();
            var r = new (double, double)[specE.Length];
            double prev = 0;
            for (int i = 0; i < specE.Length; i++) { r[i] = (specE[i], specCdf[i] - prev); prev = specCdf[i]; }
            return r;
        }

        double SamplePhotonEnergy(Rng r)
        {
            double u = r.U();
            int lo = 0, hi = specCdf.Length - 1;
            while (hi - lo > 1) { int m = (lo + hi) >> 1; if (specCdf[m] < u) lo = m; else hi = m; }
            return specCdf[lo] >= u ? specE[lo] : specE[hi];
        }

        // ------------------------------------------------------------------ threading
        public void Start()
        {
            if (Running) return;
            Running = true;
            threads = new Thread[Scorers.Length];
            for (int i = 0; i < threads.Length; i++)
            {
                int id = i;
                threads[i] = new Thread(() => Worker(id)) { IsBackground = true, Priority = System.Threading.ThreadPriority.BelowNormal };
                threads[i].Start();
            }
        }

        public void Stop()
        {
            Running = false;
            if (threads == null) return;
            foreach (var t in threads) t?.Join(200);
            threads = null;
        }

        void Worker(int id)
        {
            var rng = new Rng((ulong)(Environment.TickCount * 31 + id * 7919 + 1));
            var sc = Scorers[id];
            long h = 0;
            while (Running)
            {
                for (int k = 0; k < 50 && Running; k++)
                {
                    bool rec = id == 0 && (h % TrackEvery == 0) && Tracks.Count < MaxQueuedTracks;
                    History(rng, sc, rec);
                    sc.Histories++;
                    h++;
                }
            }
        }

        /// <summary>Runs histories synchronously on the calling thread (tests / headless use).</summary>
        public void RunSync(long n, ulong seed = 12345)
        {
            var rng = new Rng(seed);
            for (long i = 0; i < n; i++) { History(rng, Scorers[0], false); Scorers[0].Histories++; }
        }

        public long TotalHistories()
        {
            long t = 0; foreach (var s in Scorers) t += s.Histories; return t;
        }

        /// <summary>Sums all workers' central-axis scores (energy per bin per history).</summary>
        public double[] MergedCax()
        {
            var r = new double[NZ];
            foreach (var s in Scorers) for (int i = 0; i < NZ; i++) r[i] += s.Cax[i];
            return r;
        }

        public double[] MergedMap()
        {
            var r = new double[NZ * NX];
            foreach (var s in Scorers) for (int i = 0; i < r.Length; i++) r[i] += s.Map[i];
            return r;
        }

        // ------------------------------------------------------------------ transport
        struct P { public double x, y, z, u, v, w, e; }

        void History(Rng r, Scorer sc, bool rec)
        {
            var p = new P();
            float half = Beam.FieldCm * 0.5f;
            double fx = r.Range(-half, half), fy = r.Range(-half, half);
            if (Beam.Kind == BeamKind.Proton)
            {
                // scanned/scattered proton field: nearly parallel
                p.x = fx; p.y = fy; p.z = 0; p.u = r.Gauss() * 0.003; p.v = r.Gauss() * 0.003; p.w = 1;
                Norm(ref p);
                p.e = Beam.Energy * (1 + Beam.EnergySpread * r.Gauss());
                if (rec) Rec(new Vector3((float)p.x, (float)p.y, -12f), new Vector3((float)p.x, (float)p.y, 0), 3);
                Proton(r, sc, p, rec);
                return;
            }
            // divergent beams from the source at SSD
            double L = Math.Sqrt(fx * fx + fy * fy + Beam.SsdCm * Beam.SsdCm);
            p.x = fx; p.y = fy; p.z = 0; p.u = fx / L; p.v = fy / L; p.w = Beam.SsdCm / L;
            if (Beam.Kind == BeamKind.Electron)
            {
                // scattering foil + air: angular spread of a few degrees
                p.u += r.Gauss() * 0.03; p.v += r.Gauss() * 0.03; Norm(ref p);
                p.e = Beam.Energy * (1 + 0.02 * r.Gauss());
                if (rec) Rec(new Vector3((float)(p.x - p.u * 12), (float)(p.y - p.v * 12), -12f), new Vector3((float)p.x, (float)p.y, 0), 1);
                Electron(r, sc, p, rec, false);
            }
            else
            {
                p.e = SamplePhotonEnergy(r);
                if (rec) Rec(new Vector3((float)(p.x - p.u * 12 / p.w), (float)(p.y - p.v * 12 / p.w), -12f), new Vector3((float)p.x, (float)p.y, 0), 0);
                Photon(r, sc, p, rec);
            }
        }

        bool Inside(in P p) => p.z >= 0 && p.z < Depth && p.x > -Half && p.x < Half && p.y > -Half && p.y < Half;

        void Score(Scorer sc, double x, double y, double z, double e)
        {
            if (z < 0 || z >= Depth || e <= 0) return;
            int iz = (int)(z / Dz);
            if (iz < 0 || iz >= NZ) return;
            if (Math.Abs(x) < CaxRadius && Math.Abs(y) < CaxRadius) sc.Cax[iz] += e;
            if (Math.Abs(y) < 1.0)
            {
                int ix = (int)((x + Half) / Dx);
                if (ix >= 0 && ix < NX) sc.Map[iz * NX + ix] += e;
            }
        }

        // distributes the energy of a straight step over the depth bins it crosses
        void ScoreSegment(Scorer sc, double x0, double y0, double z0, double x1, double y1, double z1, double e)
        {
            double len = Math.Abs(z1 - z0);
            int n = Math.Max(1, (int)Math.Ceiling(len / (Dz * 0.5)));
            double de = e / n;
            for (int i = 0; i < n; i++)
            {
                double t = (i + 0.5) / n;
                Score(sc, x0 + (x1 - x0) * t, y0 + (y1 - y0) * t, z0 + (z1 - z0) * t, de);
            }
        }

        void Rec(Vector3 a, Vector3 b, byte t)
        {
            Tracks.Enqueue((a, b, t));
        }

        static void Norm(ref P p)
        {
            double n = Math.Sqrt(p.u * p.u + p.v * p.v + p.w * p.w);
            p.u /= n; p.v /= n; p.w /= n;
        }

        // rotate direction by polar angle (cosine ct) and azimuth phi
        static void Rotate(ref P p, double ct, double phi)
        {
            double st = Math.Sqrt(Math.Max(0, 1 - ct * ct));
            double cp = Math.Cos(phi), sp = Math.Sin(phi);
            double u = p.u, v = p.v, w = p.w;
            if (Math.Abs(w) > 0.99999)
            {
                p.u = st * cp; p.v = st * sp; p.w = ct * Math.Sign(w);
            }
            else
            {
                double sw = Math.Sqrt(1 - w * w);
                p.u = u * ct + st * (u * w * cp - v * sp) / sw;
                p.v = v * ct + st * (v * w * cp + u * sp) / sw;
                p.w = w * ct - st * cp * sw;
            }
            Norm(ref p);
        }

        const double ElectronDensity = 3.3428e23;     // electrons / cm³ in water

        void Photon(Rng r, Scorer sc, P p, bool rec)
        {
            var water = Nist.Get(Nist.Id.Water);
            int guard = 0;
            while (guard++ < 200)
            {
                if (p.e < 0.01) { Score(sc, p.x, p.y, p.z, p.e); return; }
                double mu = water.MuRho(p.e);
                double s = -Math.Log(r.U()) / mu;
                var a = new Vector3((float)p.x, (float)p.y, (float)p.z);
                p.x += p.u * s; p.y += p.v * s; p.z += p.w * s;
                if (!Inside(p))
                {
                    if (rec) Rec(a, a + new Vector3((float)p.u, (float)p.v, (float)p.w) * 10f, 0);
                    return;
                }
                if (rec) Rec(a, new Vector3((float)p.x, (float)p.y, (float)p.z), 0);
                double muC = Nist.KleinNishina(p.e) * ElectronDensity;
                double xi = r.U() * mu;
                if (xi < muC)
                {
                    // Compton scattering (Kahn's sampling of Klein–Nishina)
                    double k = p.e / 0.51099895, eta, ct;
                    while (true)
                    {
                        double r1 = r.U(), r2 = r.U(), r3 = r.U();
                        if (r1 <= (1 + 2 * k) / (9 + 2 * k))
                        {
                            eta = 1 + 2 * k * r2;
                            if (r3 <= 4 * (1 / eta - 1 / (eta * eta))) break;
                        }
                        else
                        {
                            eta = (1 + 2 * k) / (1 + 2 * k * r2);
                            double c = 1 - (eta - 1) / k;
                            if (r3 <= 0.5 * (c * c + 1 / eta)) break;
                        }
                    }
                    ct = 1 - (eta - 1) / k;
                    double e1 = p.e / eta;
                    double te = p.e - e1;
                    double phi = 2 * Math.PI * r.U();
                    // recoil electron: cot(φe) = (1 + k) tan(θ/2)
                    double th = Math.Acos(Math.Max(-1, Math.Min(1, ct)));
                    double tanHalf = Math.Tan(th / 2);
                    double phiE = tanHalf > 1e-12 ? Math.Atan(1.0 / ((1 + k) * tanHalf)) : Math.PI / 2;
                    var el = p; el.e = te;
                    Rotate(ref el, Math.Cos(phiE), phi + Math.PI);
                    if (te > 0.01) Electron(r, sc, el, rec, false); else Score(sc, p.x, p.y, p.z, te);
                    p.e = e1;
                    Rotate(ref p, ct, phi);
                }
                else if (p.e > 1.022)
                {
                    // pair production: share the kinetic energy, both leptons nearly forward
                    double tk = p.e - 1.022;
                    double f = 0.15 + 0.7 * r.U();
                    var e1p = p; e1p.e = tk * f;
                    var e2p = p; e2p.e = tk * (1 - f);
                    double ang = 0.51099895 / Math.Max(p.e, 1.1);
                    Rotate(ref e1p, Math.Cos(ang), 2 * Math.PI * r.U());
                    Rotate(ref e2p, Math.Cos(ang), 2 * Math.PI * r.U());
                    Electron(r, sc, e1p, rec, false);
                    Electron(r, sc, e2p, rec, true);
                    return;
                }
                else
                {
                    // photo-absorption: local deposition (photoelectron range is negligible)
                    Score(sc, p.x, p.y, p.z, p.e);
                    return;
                }
            }
        }

        void Electron(Rng r, Scorer sc, P p, bool rec, bool positron)
        {
            const double ecut = 0.15;
            int guard = 0;
            byte type = positron ? (byte)2 : (byte)1;
            while (p.e > ecut && guard++ < 600)
            {
                double range = StoppingPower.ElectronRange(p.e);
                double step = Math.Max(0.01, Math.Min(0.12 * range, 0.2));
                double sCol = StoppingPower.ElectronCollision(p.e);
                double sRad = StoppingPower.ElectronRadiative(p.e);
                // collision loss with Gaussian (Bohr) straggling
                double dEcol = sCol * step + Math.Sqrt(0.1569 * 0.5551 * step) * r.Gauss() * 0.6;
                dEcol = Math.Max(0, Math.Min(dEcol, p.e));
                // radiative loss: discrete bremsstrahlung photons with a 1/k spectrum (k from 1 % of E to E)
                double kmin = 0.01 * p.e;
                double meanK = (p.e - kmin) / Math.Log(p.e / kmin);
                double emitted = 0;
                if (r.U() < sRad * step / meanK)
                {
                    emitted = kmin * Math.Exp(r.U() * Math.Log(p.e / kmin));
                    emitted = Math.Min(emitted, p.e - dEcol);
                }
                var a = new Vector3((float)p.x, (float)p.y, (float)p.z);
                // random-hinge: straight part ζs, deflection, then the rest of the step
                double zeta = r.U();
                double ox = p.x, oy = p.y, oz = p.z;
                p.x += p.u * step * zeta; p.y += p.v * step * zeta; p.z += p.w * step * zeta;
                double hx = p.x, hy = p.y, hz = p.z;
                double th0 = StoppingPower.Highland(Math.Max(p.e - 0.5 * dEcol, ecut), 0.51099895, step);
                double tx = th0 * r.Gauss(), ty = th0 * r.Gauss();
                double theta = Math.Sqrt(tx * tx + ty * ty);
                if (theta > Math.PI) theta = Math.PI * r.U();
                Rotate(ref p, Math.Cos(theta), 2 * Math.PI * r.U());
                p.x += p.u * step * (1 - zeta); p.y += p.v * step * (1 - zeta); p.z += p.w * step * (1 - zeta);
                if (rec) Rec(a, new Vector3((float)p.x, (float)p.y, (float)p.z), type);
                ScoreSegment(sc, ox, oy, oz, hx, hy, hz, dEcol * zeta);
                ScoreSegment(sc, hx, hy, hz, p.x, p.y, p.z, dEcol * (1 - zeta));
                if (emitted > 0.05)
                {
                    var g = p; g.e = emitted;
                    double ang = 0.51099895 / (p.e + 0.511);
                    Rotate(ref g, Math.Cos(ang), 2 * Math.PI * r.U());
                    Photon(r, sc, g, rec && r.U() < 0.3);
                }
                else if (emitted > 0) Score(sc, p.x, p.y, p.z, emitted);
                p.e -= dEcol + emitted;
                if (!Inside(p)) return;
            }
            Score(sc, p.x, p.y, p.z, Math.Max(p.e, 0));
            if (positron)
            {
                // annihilation at rest: two back-to-back 511 keV photons
                double ux, uy, uz; r.Isotropic(out ux, out uy, out uz);
                var g1 = p; g1.e = 0.511; g1.u = ux; g1.v = uy; g1.w = uz;
                var g2 = p; g2.e = 0.511; g2.u = -ux; g2.v = -uy; g2.w = -uz;
                Photon(r, sc, g1, false); Photon(r, sc, g2, false);
            }
        }

        void Proton(Rng r, Scorer sc, P p, bool rec)
        {
            const double Mp = 938.272;
            const double sigmaNuc = 0.0118;    // nuclear removal per cm (≈1.2 %/cm)
            int guard = 0;
            while (p.e > 0.5 && guard++ < 3000)
            {
                double range = StoppingPower.ProtonRange(p.e);
                double step = Math.Max(0.01, Math.Min(0.2, 0.04 * range));
                var a = new Vector3((float)p.x, (float)p.y, (float)p.z);
                // nuclear interaction?
                if (r.U() < sigmaNuc * step * Math.Min(1.0, p.e / 20.0))
                {
                    // ~40 % of the energy deposited locally by short-range secondaries; rest escapes (n, γ)
                    double loc = 0.4 * p.e;
                    for (int k = 0; k < 4; k++)
                        Score(sc, p.x + p.u * k * 0.4, p.y + p.v * k * 0.4, p.z + p.w * k * 0.4, loc / 4);
                    if (rec) Rec(a, a + new Vector3((float)r.Gauss(), (float)r.Gauss(), (float)Math.Abs(r.Gauss())) * 3f, 4);
                    return;
                }
                double S = StoppingPower.Proton(p.e);
                double dE = S * step;
                // Bohr energy straggling
                double gamma = 1 + p.e / Mp; double beta2 = 1 - 1 / (gamma * gamma);
                double var = 0.1569 * 0.5551 * step * (1 - beta2 / 2) / (1 - beta2);
                dE += Math.Sqrt(var) * r.Gauss();
                dE = Math.Max(0, Math.Min(dE, p.e));
                double ox = p.x, oy = p.y, oz = p.z;
                p.x += p.u * step; p.y += p.v * step; p.z += p.w * step;
                ScoreSegment(sc, ox, oy, oz, p.x, p.y, p.z, dE);
                if (rec) Rec(a, new Vector3((float)p.x, (float)p.y, (float)p.z), 3);
                p.e -= dE;
                if (!Inside(p)) return;
                double th0 = StoppingPower.Highland(p.e + dE * 0.5, Mp, step);
                double tx = th0 * r.Gauss(), ty = th0 * r.Gauss();
                Rotate(ref p, Math.Cos(Math.Sqrt(tx * tx + ty * ty)), 2 * Math.PI * r.U());
            }
            Score(sc, p.x, p.y, p.z, Math.Max(0, p.e));
        }
    }
}
