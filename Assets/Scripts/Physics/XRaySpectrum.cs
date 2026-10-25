using System;

namespace MedPhys.Physics
{
    /// <summary>
    /// Diagnostic X-ray tube spectrum (tungsten anode).
    ///  - Bremsstrahlung: Kramers' thick-target law  N(E) dE ~ Z (E0 - E) / E dE
    ///  - Characteristic K lines of W (Kα2 57.98, Kα1 59.32, Kβ1 67.24, Kβ2 69.07 keV), produced only when
    ///    kVp exceeds the K-edge (69.525 keV); intensity ~ (U-1)^1.63 (Green &amp; Cosslett), U = kVp / E_K
    ///  - Self-absorption in the anode (~3 µm W), inherent filtration (0.8 mm Al eq.) and added Al / Cu filters,
    ///    all attenuated with NIST µ/ρ data
    ///  - Absolute output normalised to 45 µGy/mAs air kerma at 1 m for 80 kVp, 2.5 mm Al (typical modern
    ///    high-frequency generator); everything else follows from the physics above.
    /// Units: energy keV, fluence photons / (cm² · mAs) at 100 cm.
    /// </summary>
    public sealed class XRaySpectrum
    {
        public float Kvp;
        public float BinKeV;
        public double[] E;      // bin centre energies (keV)
        public double[] N;      // photons / cm² / mAs at 100 cm, per bin
        public float AddedAlMm, AddedCuMm;

        public const float InherentAlMm = 0.8f;
        const double EK = 69.525;

        public int Count => E.Length;

        public static XRaySpectrum Generate(float kvp, float addedAlMm, float addedCuMm, float binKeV = 1f)
        {
            var s = Raw(kvp, binKeV);
            s.AddedAlMm = addedAlMm; s.AddedCuMm = addedCuMm;
            s.Attenuate(Nist.Id.Aluminum, (InherentAlMm + addedAlMm) * 0.1, -1);
            if (addedCuMm > 0) s.Attenuate(Nist.Id.Copper, addedCuMm * 0.1, -1);
            s.Normalise();
            return s;
        }

        /// <summary>Spectrum leaving the anode (self-absorption only) - for the "before filtration" plot.</summary>
        public static XRaySpectrum Unfiltered(float kvp, float binKeV = 1f)
        {
            var s = Raw(kvp, binKeV);
            s.Normalise();
            return s;
        }

        static XRaySpectrum Raw(float kvp, float bin)
        {
            int n = Math.Max(1, (int)Math.Floor((kvp - 1f) / bin));
            var s = new XRaySpectrum { Kvp = kvp, BinKeV = bin, E = new double[n], N = new double[n] };
            double brems = 0;
            for (int i = 0; i < n; i++)
            {
                double e = 1.0 + (i + 0.5) * bin;
                s.E[i] = e;
                double v = 74.0 * (kvp - e) / e * bin;      // Kramers, Z(W) = 74
                s.N[i] = Math.Max(0, v);
                if (e > 20) brems += s.N[i];
            }
            // characteristic lines (photon numbers relative to bremsstrahlung above 20 keV)
            if (kvp > EK)
            {
                double U = kvp / EK, U0 = 100.0 / EK;
                double total = 0.10 * brems * Math.Pow(U - 1, 1.63) / Math.Pow(U0 - 1, 1.63);
                double[] le = { 57.98, 59.32, 67.24, 69.07 };
                double[] rel = { 0.58, 1.0, 0.33, 0.08 };
                double rs = 0; foreach (var r in rel) rs += r;
                for (int k = 0; k < le.Length; k++)
                {
                    int idx = (int)Math.Floor((le[k] - 1.0) / bin);
                    if (idx >= 0 && idx < n) s.N[idx] += total * rel[k] / rs;
                }
            }
            // anode self absorption (heel side average), ~3 µm tungsten
            s.Attenuate(Nist.Id.Tungsten, 3e-4, -1);
            return s;
        }

        /// <summary>Attenuate through a slab: thickness in cm (density override optional).</summary>
        public void Attenuate(Nist.Id mat, double thicknessCm, double density)
        {
            var m = Nist.Get(mat);
            double rho = density > 0 ? density : m.Density;
            for (int i = 0; i < Count; i++)
                N[i] *= Math.Exp(-m.MuRho(E[i] / 1000.0) * rho * thicknessCm);
        }

        public XRaySpectrum Clone()
        {
            return new XRaySpectrum { Kvp = Kvp, BinKeV = BinKeV, E = (double[])E.Clone(), N = (double[])N.Clone(), AddedAlMm = AddedAlMm, AddedCuMm = AddedCuMm };
        }

        // Air kerma (µGy) per mAs at 100 cm for the current fluence (not normalised).
        public double AirKermaPerMas()
        {
            var air = Nist.Get(Nist.Id.Air);
            double k = 0;
            for (int i = 0; i < Count; i++)
            {
                double eMeV = E[i] / 1000.0;
                k += N[i] * eMeV * air.MuEnRho(eMeV);          // MeV/g
            }
            return k * 1.602176634e-13 * 1000.0 * 1e6;          // -> J/kg -> µGy
        }

        static double _norm = -1;

        void Normalise()
        {
            if (_norm < 0)
            {
                var r = Raw(80f, 1f);
                r.Attenuate(Nist.Id.Aluminum, 0.25, -1);
                _norm = 45.0 / r.AirKermaPerMas();
            }
            for (int i = 0; i < Count; i++) N[i] *= _norm;
        }

        public double TotalPhotons()
        {
            double t = 0; for (int i = 0; i < Count; i++) t += N[i]; return t;
        }

        /// <summary>Fluence-weighted mean energy (keV).</summary>
        public double MeanEnergy()
        {
            double a = 0, b = 0;
            for (int i = 0; i < Count; i++) { a += N[i] * E[i]; b += N[i]; }
            return b > 0 ? a / b : 0;
        }

        /// <summary>Half value layer in mm Al, from air kerma attenuation (bisection).</summary>
        public double HvlAlMm()
        {
            double k0 = AirKermaPerMas();
            double lo = 0, hi = 40;
            for (int it = 0; it < 40; it++)
            {
                double mid = 0.5 * (lo + hi);
                var c = Clone(); c.Attenuate(Nist.Id.Aluminum, mid * 0.1, -1);
                if (c.AirKermaPerMas() > 0.5 * k0) lo = mid; else hi = mid;
            }
            return 0.5 * (lo + hi);
        }

        /// <summary>Effective energy (keV): monoenergetic beam with the same HVL in Al.</summary>
        public double EffectiveEnergy()
        {
            double hvlCm = HvlAlMm() * 0.1;
            double mu = Math.Log(2) / hvlCm / Nist.Get(Nist.Id.Aluminum).Density;
            double lo = 10, hi = Kvp;
            for (int it = 0; it < 40; it++)
            {
                double mid = 0.5 * (lo + hi);
                if (Nist.Get(Nist.Id.Aluminum).MuRho(mid / 1000.0) > mu) lo = mid; else hi = mid;
            }
            return 0.5 * (lo + hi);
        }
    }
}
