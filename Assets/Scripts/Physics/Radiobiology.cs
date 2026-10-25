using System;

namespace MedPhys.Physics
{
    /// <summary>Linear-quadratic model, BED/EQD2 with repopulation, Poisson TCP.</summary>
    public static class Radiobiology
    {
        /// <summary>Surviving fraction after one fraction of dose d (Gy).</summary>
        public static double Survival(double alpha, double alphaBeta, double d)
        {
            double beta = alpha / alphaBeta;
            return Math.Exp(-alpha * d - beta * d * d);
        }

        /// <summary>Surviving fraction after n fractions of d, with repopulation after Tk days (doubling time Tpot).</summary>
        public static double SurvivalFractionated(double alpha, double alphaBeta, double d, int n, double days = 0, double tk = 28, double tpot = 3)
        {
            double lnS = n * Math.Log(Survival(alpha, alphaBeta, d));
            if (days > tk && tpot > 0) lnS += Math.Log(2) * (days - tk) / tpot;
            return Math.Exp(lnS);
        }

        public static double Bed(double d, int n, double alphaBeta, double alpha = 0.3, double days = 0, double tk = 28, double tpot = 3)
        {
            double bed = n * d * (1 + d / alphaBeta);
            if (days > tk && tpot > 0 && alpha > 0) bed -= Math.Log(2) * (days - tk) / (alpha * tpot);
            return bed;
        }

        public static double Eqd2(double bed, double alphaBeta) => bed / (1 + 2.0 / alphaBeta);

        /// <summary>Poisson tumour control probability for N0 clonogens.</summary>
        public static double Tcp(double n0, double survival) => Math.Exp(-n0 * survival);

        /// <summary>Overall treatment time (days) for n fractions given 5 fractions per week.</summary>
        public static double TreatmentDays(int n, int perWeek = 5)
        {
            if (n <= 1) return 0;
            int weeks = (n - 1) / perWeek;
            int rem = (n - 1) % perWeek;
            return weeks * 7 + rem;
        }
    }
}
