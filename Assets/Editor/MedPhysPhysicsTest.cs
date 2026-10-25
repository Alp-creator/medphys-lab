using System.Diagnostics;
using System.Text;
using MedPhys.Physics;

// Headless physics validation: runs the Monte Carlo engines and reports dosimetric quantities.
public static class MedPhysPhysicsTest
{
    public static void RunBeamTests()
    {
        var sb = new StringBuilder();
        sb.AppendLine("== Stopping power / range checks (water)");
        foreach (var (t, r) in StoppingPower.PstarRanges)
            sb.AppendLine($"proton {t,6:F0} MeV  CSDA calc {StoppingPower.ProtonRange(t),8:F3} cm   PSTAR {r,8:F3}   S={StoppingPower.Proton(t):F3}");
        foreach (var (t, r) in StoppingPower.EstarRanges)
            sb.AppendLine($"electron {t,5:F1} MeV  CSDA calc {StoppingPower.ElectronRange(t),8:F4} cm   ESTAR {r,8:F4}   Scol={StoppingPower.ElectronCollision(t):F3} Srad={StoppingPower.ElectronRadiative(t):F4}");
        Run(sb, new BeamSpec { Kind = BeamKind.Photon, Energy = 6, FieldCm = 10 }, 400000);
        Run(sb, new BeamSpec { Kind = BeamKind.Photon, Energy = 18, FieldCm = 10 }, 300000);
        Run(sb, new BeamSpec { Kind = BeamKind.Photon, Energy = 1.25f, FieldCm = 10 }, 400000);
        Run(sb, new BeamSpec { Kind = BeamKind.Electron, Energy = 9, FieldCm = 10 }, 40000);
        Run(sb, new BeamSpec { Kind = BeamKind.Electron, Energy = 16, FieldCm = 10 }, 30000);
        Run(sb, new BeamSpec { Kind = BeamKind.Proton, Energy = 150, FieldCm = 10 }, 20000);
        Run(sb, new BeamSpec { Kind = BeamKind.Proton, Energy = 70, FieldCm = 10 }, 20000);
        MedPhysTools.Report("physics_test.txt", sb.ToString());
    }

    static void Run(StringBuilder sb, BeamSpec b, int n)
    {
        var sw = Stopwatch.StartNew();
        var mc = new WaterMC(b, 1);
        mc.RunSync(n);
        var cax = mc.MergedCax();
        double mx = 0; int imx = 0;
        for (int i = 0; i < cax.Length; i++) if (cax[i] > mx) { mx = cax[i]; imx = i; }
        string Pdd(double d) { int i = (int)(d / mc.Dz); return i < cax.Length ? (100 * cax[i] / mx).ToString("F1") : "-"; }
        double Rx(double frac)
        {
            for (int i = cax.Length - 1; i > imx; i--) if (cax[i] >= frac * mx) return (i + 0.5) * mc.Dz;
            return 0;
        }
        sb.AppendLine($"== {b.Label} field {b.FieldCm} n={n} time={sw.ElapsedMilliseconds} ms  meanE={mc.MeanSpectrumEnergy():F2}");
        sb.AppendLine($"   dmax={(imx + 0.5) * mc.Dz:F2} cm  surface={Pdd(0.1)}%  PDD5={Pdd(5)}  PDD10={Pdd(10)}  PDD20={Pdd(20)}  R80={Rx(0.8):F2}  R50={Rx(0.5):F2}  R10={Rx(0.1):F2}");
        var line = new StringBuilder("   ");
        for (int i = 0; i < cax.Length; i += 2) line.Append((100 * cax[i] / mx).ToString("F0")).Append(' ');
        sb.AppendLine(line.ToString());
    }
}
