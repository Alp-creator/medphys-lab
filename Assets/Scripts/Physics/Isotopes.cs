using System;

namespace MedPhys.Physics
{
    public enum DecayMode { BetaPlus, BetaMinus, IsomericTransition, Alpha }

    /// <summary>Radionuclide data (NNDC / ICRP 107 values, rounded).</summary>
    public sealed class Isotope
    {
        public string Name;
        public double HalfLifeS;          // seconds
        public DecayMode Mode;
        public double MassNumber;
        public double GammaKeV;           // principal photon energy (0 = none)
        public double GammaYield;
        public double BetaMaxMeV;
        public double PositronRangeMm;    // mean positron range in water (β+ emitters)
        public double GammaConstant;      // air-kerma rate constant, µGy·m²/(MBq·h)
        public string UseTr, UseEn;

        public double Lambda => Math.Log(2) / HalfLifeS;
        /// <summary>Specific activity (Bq/g) of the pure nuclide.</summary>
        public double SpecificActivity => Lambda * 6.02214076e23 / MassNumber;

        public string HalfLifeText()
        {
            double s = HalfLifeS;
            if (s < 3600) return (s / 60).ToString("0.0#") + " min";
            if (s < 86400 * 3) return (s / 3600).ToString("0.0#") + " h";
            if (s < 86400 * 365 * 2) return (s / 86400).ToString("0.0#") + " d";
            return (s / (86400 * 365.25)).ToString("0.0#") + " y";
        }
    }

    public static class Isotopes
    {
        const double M = 60, H = 3600, D = 86400, Y = 365.25 * 86400;

        public static readonly Isotope[] All =
        {
            new Isotope { Name = "Tc-99m", HalfLifeS = 6.0067 * H, Mode = DecayMode.IsomericTransition, MassNumber = 99, GammaKeV = 140.5, GammaYield = 0.89, GammaConstant = 0.0141,
                          UseTr = "SPECT görüntüleme (kemik, miyokard perfüzyon)", UseEn = "SPECT imaging (bone, myocardial perfusion)" },
            new Isotope { Name = "F-18", HalfLifeS = 109.77 * M, Mode = DecayMode.BetaPlus, MassNumber = 18, GammaKeV = 511, GammaYield = 1.934, BetaMaxMeV = 0.634, PositronRangeMm = 0.6, GammaConstant = 0.143,
                          UseTr = "PET (FDG ile glukoz metabolizması)", UseEn = "PET (glucose metabolism with FDG)" },
            new Isotope { Name = "Ga-68", HalfLifeS = 67.71 * M, Mode = DecayMode.BetaPlus, MassNumber = 68, GammaKeV = 511, GammaYield = 1.78, BetaMaxMeV = 1.899, PositronRangeMm = 2.9, GammaConstant = 0.134,
                          UseTr = "PET (PSMA, DOTATATE)", UseEn = "PET (PSMA, DOTATATE)" },
            new Isotope { Name = "I-131", HalfLifeS = 8.0252 * D, Mode = DecayMode.BetaMinus, MassNumber = 131, GammaKeV = 364.5, GammaYield = 0.815, BetaMaxMeV = 0.606, GammaConstant = 0.0595,
                          UseTr = "Tiroid kanseri ve hipertiroidi tedavisi", UseEn = "Thyroid cancer and hyperthyroidism therapy" },
            new Isotope { Name = "Lu-177", HalfLifeS = 6.647 * D, Mode = DecayMode.BetaMinus, MassNumber = 177, GammaKeV = 208, GammaYield = 0.104, BetaMaxMeV = 0.498, GammaConstant = 0.0052,
                          UseTr = "Radyoligand tedavisi (PSMA, DOTATATE)", UseEn = "Radioligand therapy (PSMA, DOTATATE)" },
            new Isotope { Name = "Y-90", HalfLifeS = 64.05 * H, Mode = DecayMode.BetaMinus, MassNumber = 90, BetaMaxMeV = 2.28, GammaConstant = 0,
                          UseTr = "Karaciğer radyoembolizasyonu (SIRT)", UseEn = "Liver radioembolisation (SIRT)" },
            new Isotope { Name = "Ra-223", HalfLifeS = 11.43 * D, Mode = DecayMode.Alpha, MassNumber = 223, GammaKeV = 82, GammaYield = 0.25, GammaConstant = 0.0045,
                          UseTr = "Kemik metastazı (alfa tedavisi)", UseEn = "Bone metastases (alpha therapy)" },
            new Isotope { Name = "Co-60", HalfLifeS = 5.2714 * Y, Mode = DecayMode.BetaMinus, MassNumber = 60, GammaKeV = 1250, GammaYield = 2.0, BetaMaxMeV = 0.318, GammaConstant = 0.305,
                          UseTr = "Teleterapi, Gamma Knife", UseEn = "Teletherapy, Gamma Knife" },
            new Isotope { Name = "Cs-137", HalfLifeS = 30.08 * Y, Mode = DecayMode.BetaMinus, MassNumber = 137, GammaKeV = 661.7, GammaYield = 0.851, BetaMaxMeV = 0.514, GammaConstant = 0.0771,
                          UseTr = "Kalibrasyon kaynağı, brakiterapi (tarihsel)", UseEn = "Calibration source, brachytherapy (historic)" },
            new Isotope { Name = "Ir-192", HalfLifeS = 73.83 * D, Mode = DecayMode.BetaMinus, MassNumber = 192, GammaKeV = 380, GammaYield = 2.3, BetaMaxMeV = 0.672, GammaConstant = 0.111,
                          UseTr = "HDR brakiterapi", UseEn = "HDR brachytherapy" },
        };

        public static Isotope Get(string name)
        {
            foreach (var i in All) if (i.Name == name) return i;
            return All[0];
        }

        // ---------------------------------------------------------------- Mo-99 / Tc-99m generator (Bateman)
        public const double MoHalfLifeH = 65.94, TcHalfLifeH = 6.0067, BranchingTc = 0.876;

        /// <summary>Tc-99m activity at time t (h) after an elution at t0 = 0, for a generator with Mo activity Amo0 at t0.</summary>
        public static double TcActivity(double amo0, double tc0, double tH)
        {
            double lm = Math.Log(2) / MoHalfLifeH, lt = Math.Log(2) / TcHalfLifeH;
            return BranchingTc * amo0 * lt / (lt - lm) * (Math.Exp(-lm * tH) - Math.Exp(-lt * tH)) + tc0 * Math.Exp(-lt * tH);
        }

        /// <summary>Time of maximum Tc-99m activity after elution (h).</summary>
        public static double TcMaxTimeH()
        {
            double lm = Math.Log(2) / MoHalfLifeH, lt = Math.Log(2) / TcHalfLifeH;
            return Math.Log(lt / lm) / (lt - lm);
        }
    }
}
