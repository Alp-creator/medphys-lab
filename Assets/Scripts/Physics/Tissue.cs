using UnityEngine;

namespace MedPhys.Physics
{
    public enum TissueId
    {
        Air, Soft, Lung, CorticalBone, SpongyBone, MixedBone, Adipose, Blood, Brain, Muscle,
        Water, Titanium, ContrastBlood, Tumor, Count
    }

    /// <summary>
    /// Tissue = NIST composition at a given density (optionally mixed with a contrast element by mass fraction),
    /// plus the properties other modalities need (MRI relaxation, display colour).
    /// </summary>
    public sealed class Tissue
    {
        public TissueId Id;
        public string Tr, En;
        public Nist.Id Base;
        public float Density;
        public Nist.Id Additive = Nist.Id.Iodine;
        public float AdditiveMassFraction;
        public Color Color;
        // MRI (1.5 T): T1, T2 in ms, proton density relative to water
        public float T1, T2, PD;

        public string Name => Core.Loc.T(Tr, En);

        public double MuRho(double eMeV)
        {
            double m = Nist.Get(Base).MuRho(eMeV);
            if (AdditiveMassFraction > 0)
                m = (1 - AdditiveMassFraction) * m + AdditiveMassFraction * Nist.Get(Additive).MuRho(eMeV);
            return m;
        }

        public double MuEnRho(double eMeV)
        {
            double m = Nist.Get(Base).MuEnRho(eMeV);
            if (AdditiveMassFraction > 0)
                m = (1 - AdditiveMassFraction) * m + AdditiveMassFraction * Nist.Get(Additive).MuEnRho(eMeV);
            return m;
        }

        /// <summary>Linear attenuation coefficient (1/cm).</summary>
        public double Mu(double eMeV) => Id == TissueId.Air ? Nist.Get(Nist.Id.Air).Mu(eMeV) : MuRho(eMeV) * Density;
    }

    public static class Tissues
    {
        static Tissue[] _all;

        public static Tissue Get(TissueId id) => All[(int)id];

        public static Tissue[] All
        {
            get
            {
                if (_all != null) return _all;
                var a = new Tissue[(int)TissueId.Count];
                void Add(TissueId id, string tr, string en, Nist.Id b, float rho, Color c, float t1 = 0, float t2 = 0, float pd = 0, float iodine = 0)
                {
                    a[(int)id] = new Tissue { Id = id, Tr = tr, En = en, Base = b, Density = rho, Color = c, T1 = t1, T2 = t2, PD = pd, AdditiveMassFraction = iodine };
                }
                Add(TissueId.Air, "Hava", "Air", Nist.Id.Air, 0.001205f, new Color(0.1f, 0.1f, 0.1f));
                Add(TissueId.Soft, "Yumuşak doku", "Soft tissue", Nist.Id.SoftTissue, 1.06f, new Color(0.85f, 0.55f, 0.50f), 870, 47, 0.75f);
                Add(TissueId.Lung, "Akciğer", "Lung", Nist.Id.Lung, 0.26f, new Color(0.95f, 0.62f, 0.68f), 1200, 30, 0.2f);
                Add(TissueId.CorticalBone, "Kortikal kemik", "Cortical bone", Nist.Id.Bone, 1.92f, new Color(0.96f, 0.93f, 0.84f), 1000, 1, 0.05f);
                Add(TissueId.SpongyBone, "Süngerimsi kemik", "Spongy bone", Nist.Id.Bone, 1.18f, new Color(0.93f, 0.88f, 0.76f), 500, 40, 0.5f);
                Add(TissueId.MixedBone, "Kemik (kaburga/pelvis)", "Bone (rib/pelvis)", Nist.Id.Bone, 1.45f, new Color(0.95f, 0.91f, 0.80f), 800, 20, 0.2f);
                Add(TissueId.Adipose, "Yağ dokusu", "Adipose", Nist.Id.Adipose, 0.95f, new Color(0.98f, 0.86f, 0.55f), 260, 80, 0.9f);
                Add(TissueId.Blood, "Kan / kalp", "Blood / heart", Nist.Id.Blood, 1.06f, new Color(0.80f, 0.12f, 0.15f), 1200, 200, 0.85f);
                Add(TissueId.Brain, "Beyin", "Brain", Nist.Id.Brain, 1.04f, new Color(0.92f, 0.70f, 0.72f), 920, 100, 0.8f);
                Add(TissueId.Muscle, "Kas", "Muscle", Nist.Id.Muscle, 1.05f, new Color(0.75f, 0.30f, 0.30f), 870, 47, 0.8f);
                Add(TissueId.Water, "Su / BOS / idrar", "Water / CSF / urine", Nist.Id.Water, 1.0f, new Color(0.55f, 0.75f, 0.95f), 4000, 2000, 1.0f);
                Add(TissueId.Titanium, "Titanyum implant", "Titanium implant", Nist.Id.Titanium, 4.54f, new Color(0.75f, 0.78f, 0.82f));
                Add(TissueId.ContrastBlood, "Kan + iyotlu kontrast", "Blood + iodine contrast", Nist.Id.Blood, 1.07f, new Color(1f, 0.85f, 0.3f), 400, 150, 0.85f, 0.012f);
                Add(TissueId.Tumor, "Tümör", "Tumour", Nist.Id.SoftTissue, 1.07f, new Color(0.35f, 1.0f, 0.45f), 1100, 90, 0.85f);
                _all = a;
                return a;
            }
        }
    }
}
