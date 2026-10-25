using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using MedPhys.Cases;
using MedPhys.Physics;
using MedPhys.UI;
using static MedPhys.Core.Loc;

namespace MedPhys.Learn
{
    /// <summary>
    /// Interactive lesson figures ("%fig name" in the curriculum): a chart driven by the project's physics models,
    /// with sliders / choices and live numeric read-outs.
    /// </summary>
    public static class LessonFigures
    {
        static readonly Color C1 = new Color(0.3f, 0.82f, 1f), C2 = new Color(1f, 0.7f, 0.25f), C3 = new Color(0.45f, 1f, 0.55f),
                              C4 = new Color(1f, 0.42f, 0.55f), C5 = new Color(0.75f, 0.6f, 1f), Grey = new Color(0.62f, 0.66f, 0.75f);

        sealed class Fig
        {
            public VisualElement Root, Controls, Out;
            public Chart Chart;
            public Action Update;
            public readonly List<(string label, Func<string> value)> Reads = new List<(string, Func<string>)>();
            public void Read(string label, Func<string> value) => Reads.Add((label, value));
            public void Refresh()
            {
                Update?.Invoke();
                Chart?.Refresh();
                Out.Clear();
                foreach (var (l, v) in Reads) UIKit.KV(Out, l, v());
            }
        }

        public static readonly string[] Names = { "atten", "murho", "spectrum", "compton", "invsq", "decay", "generator", "pdd", "bragg", "lq", "relax", "hu", "ssde", "noise" };

        public static VisualElement Build(string name)
        {
            var f = new Fig { Root = new VisualElement() };
            f.Root.AddToClassList("mp-figure");
            var cap = new Label(T("ETKİLEŞİMLİ ŞEKİL", "INTERACTIVE FIGURE")); cap.AddToClassList("mp-figure-tag"); f.Root.Add(cap);
            f.Controls = new VisualElement(); f.Controls.AddToClassList("mp-figure-controls");
            f.Out = new VisualElement(); f.Out.AddToClassList("mp-figure-out");
            switch (name)
            {
                case "atten": Atten(f); break;
                case "murho": MuRho(f); break;
                case "spectrum": Spectrum(f); break;
                case "compton": Compton(f); break;
                case "invsq": InvSq(f); break;
                case "decay": Decay(f); break;
                case "generator": Generator(f); break;
                case "pdd": Pdd(f); break;
                case "bragg": Bragg(f); break;
                case "lq": Lq(f); break;
                case "relax": Relax(f); break;
                case "hu": Hu(f); break;
                case "ssde": Ssde(f); break;
                case "noise": Noise(f); break;
                default: UIKit.Note(f.Root, "[" + name + "]"); return f.Root;
            }
            if (f.Chart != null)
            {
                if (f.Chart.AllSeries.Count > 1) f.Root.Add(f.Chart.Legend());
                f.Root.Add(f.Chart);
            }
            f.Root.Add(f.Controls);
            f.Root.Add(f.Out);
            f.Refresh();
            return f.Root;
        }

        static Chart NewChart(Fig f, float h = 230)
        {
            f.Chart = new Chart(h);
            return f.Chart;
        }

        static void Curve(Chart.Series s, double x0, double x1, int n, Func<double, double> y)
        {
            s.Pts.Clear();
            for (int i = 0; i <= n; i++) { double x = x0 + (x1 - x0) * i / n; s.Pts.Add(new Vector2((float)x, (float)y(x))); }
        }

        static float LogE(float t, float lo, float hi) => lo * Mathf.Pow(hi / lo, t);   // slider 0..1 → log scale

        // ------------------------------------------------------------------ photon attenuation
        static void Atten(Fig f)
        {
            var mats = new[] { Nist.Id.Water, Nist.Id.Bone, Nist.Id.Lead };
            int m = 0; float t = 0.45f;
            var ch = NewChart(f);
            ch.XLabel = T("kalınlık (cm)", "thickness (cm)"); ch.YLabel = "I / I₀"; ch.AutoY = false; ch.YMin = 0; ch.YMax = 1.02f;
            var s = ch.Add(T("dar ışın geçirgenliği", "narrow-beam transmission"), C1, 3f); s.Fill = true;
            Func<double> E = () => LogE(t, 20, 1000) / 1000.0;     // MeV
            Func<double> mu = () => Nist.Get(mats[m]).Mu(E());
            UIKit.Seg(f.Controls, T("Malzeme", "Material"), new[] { T("Su", "Water"), T("Kemik", "Bone"), T("Kurşun", "Lead") }, 0, i => { m = i; f.Refresh(); });
            UIKit.Slider(f.Controls, T("Foton enerjisi", "Photon energy"), 0, 1, t, v => F(LogE(v, 20, 1000), 0) + " keV", v => { t = v; f.Refresh(); });
            f.Update = () =>
            {
                double u = mu(), hvl = Math.Log(2) / u;
                double xmax = Math.Max(0.05, Math.Min(m == 2 ? 3 : 40, 5 * hvl));
                ch.XMin = 0; ch.XMax = (float)xmax;
                Curve(s, 0, xmax, 120, x => Math.Exp(-u * x));
                ch.VLines.Clear(); ch.VLines.Add(((float)hvl, C2, "HVL")); ch.VLines.Add(((float)(Math.Log(10) / u), C4, "TVL"));
            };
            f.Read("μ", () => G(mu()) + " cm⁻¹   (μ/ρ " + G(Nist.Get(mats[m]).MuRho(E())) + " cm²/g)");
            f.Read("HVL = ln2 / μ", () => G(Math.Log(2) / mu()) + " cm");
            f.Read("TVL = ln10 / μ", () => G(Math.Log(10) / mu()) + " cm");
            f.Read(T("Ortalama serbest yol 1/μ", "Mean free path 1/μ"), () => G(1 / mu()) + " cm");
        }

        static void MuRho(Fig f)
        {
            var ch = NewChart(f, 250);
            ch.XLabel = T("foton enerjisi", "photon energy"); ch.YLabel = "μ/ρ (cm²/g)"; ch.LogY = true; ch.AutoY = false; ch.YMin = 0.01f; ch.YMax = 1000f;
            ch.XMin = -2; ch.XMax = 1; ch.XFmt = x => { double e = Math.Pow(10, x); return e < 1 ? F(e * 1000, 0) + " keV" : F(e, 0) + " MeV"; };
            var mats = new[] { (Nist.Id.Water, T("Su", "Water"), C1), (Nist.Id.Bone, T("Kemik", "Bone"), C2), (Nist.Id.Lead, T("Kurşun", "Lead"), C4) };
            foreach (var (id, nm, col) in mats) { var s = ch.Add(nm, col, 2.5f); Curve(s, -2, 1, 160, x => Nist.Get(id).MuRho(Math.Pow(10, x))); }
            var en = ch.Add(T("su μen/ρ", "water μen/ρ"), Grey, 1.5f); en.Dashed = true; Curve(en, -2, 1, 160, x => Nist.Get(Nist.Id.Water).MuEnRho(Math.Pow(10, x)));
            float t = 0.33f;
            UIKit.Slider(f.Controls, T("Okuma enerjisi", "Read-out energy"), 0, 1, t, v => G(Math.Pow(10, -2 + 3 * v) * 1000) + " keV", v => { t = v; f.Refresh(); });
            f.Update = () => { ch.VLines.Clear(); ch.VLines.Add((-2 + 3 * t, Color.white, "")); };
            Func<double> e = () => Math.Pow(10, -2 + 3 * t);
            f.Read(T("Su / kemik / kurşun", "Water / bone / lead"), () => G(Nist.Get(Nist.Id.Water).MuRho(e())) + " / " + G(Nist.Get(Nist.Id.Bone).MuRho(e())) + " / " + G(Nist.Get(Nist.Id.Lead).MuRho(e())) + " cm²/g");
            f.Read(T("Kemik / su oranı (kontrast)", "Bone / water ratio (contrast)"), () => F(Nist.Get(Nist.Id.Bone).MuRho(e()) / Nist.Get(Nist.Id.Water).MuRho(e()), 2));
        }

        // ------------------------------------------------------------------ X-ray spectrum
        static void Spectrum(Fig f)
        {
            float kvp = 100, al = 2.5f, cu = 0;
            var ch = NewChart(f);
            ch.XLabel = "keV"; ch.YLabel = T("bağıl foton sayısı", "relative photons"); ch.AutoY = false; ch.YMin = 0; ch.YMax = 1.05f;
            var su = ch.Add(T("filtresiz", "unfiltered"), Grey, 1.5f);
            var sf = ch.Add(T("filtreli", "filtered"), C1, 2.5f); sf.Fill = true;
            XRaySpectrum spec = null;
            UIKit.Slider(f.Controls, T("Tüp gerilimi", "Tube voltage"), 40, 150, kvp, v => F(v, 0) + " kVp", v => { kvp = v; f.Refresh(); }, 1);
            UIKit.Slider(f.Controls, T("Al filtre (toplam ek)", "Al filter (added)"), 0, 6, al, v => F(v, 1) + " mm", v => { al = v; f.Refresh(); }, 0.1f);
            UIKit.Slider(f.Controls, T("Cu filtre", "Cu filter"), 0, 0.3f, cu, v => F(v, 2) + " mm", v => { cu = v; f.Refresh(); }, 0.05f);
            f.Update = () =>
            {
                var un = XRaySpectrum.Unfiltered(kvp, 1f);
                spec = XRaySpectrum.Generate(kvp, al, cu, 1f);
                double mx = 1e-30; for (int i = 0; i < un.Count; i++) mx = Math.Max(mx, un.N[i]);
                double mf = 1e-30; for (int i = 0; i < spec.Count; i++) mf = Math.Max(mf, spec.N[i]);
                su.Pts.Clear(); sf.Pts.Clear();
                for (int i = 0; i < un.Count; i++) su.Pts.Add(new Vector2((float)un.E[i], (float)(un.N[i] / mx)));
                for (int i = 0; i < spec.Count; i++) sf.Pts.Add(new Vector2((float)spec.E[i], (float)(spec.N[i] / mf)));
                ch.XMin = 0; ch.XMax = Mathf.Max(60, Mathf.Ceil(kvp / 10f) * 10);
            };
            f.Read(T("Ortalama enerji", "Mean energy"), () => F(spec.MeanEnergy(), 1) + " keV  (≈ " + F(spec.MeanEnergy() / kvp * 100, 0) + T(" % kVp)", " % of kVp)"));
            f.Read("HVL", () => F(spec.HvlAlMm(), 2) + " mm Al");
            f.Read(T("Tüp çıkışı (1 m)", "Tube output (1 m)"), () => F(spec.AirKermaPerMas(), 1) + " µGy/mAs");
        }

        // ------------------------------------------------------------------ Compton kinematics
        static void Compton(Fig f)
        {
            float t = 0.45f;
            var ch = NewChart(f);
            ch.XLabel = T("saçılma açısı θ (°)", "scattering angle θ (°)"); ch.YLabel = "keV"; ch.XMin = 0; ch.XMax = 180;
            var sp = ch.Add(T("saçılan foton E′", "scattered photon E′"), C1, 3f);
            var se = ch.Add(T("geri tepen elektron T", "recoil electron T"), C2, 2.5f);
            Func<double> E = () => LogE(t, 20, 3000);
            Func<double, double> Ep = th => E() / (1 + E() / 511.0 * (1 - Math.Cos(th * Math.PI / 180)));
            UIKit.Slider(f.Controls, T("Gelen foton enerjisi", "Incident photon energy"), 0, 1, t, v => F(LogE(v, 20, 3000), 0) + " keV", v => { t = v; f.Refresh(); });
            f.Update = () => { Curve(sp, 0, 180, 90, Ep); Curve(se, 0, 180, 90, th => E() - Ep(th)); };
            f.Read("E′(90°)", () => F(Ep(90), 1) + " keV");
            f.Read(T("Geri saçılma E′(180°)", "Backscatter E′(180°)"), () => F(Ep(180), 1) + " keV");
            f.Read(T("Compton kenarı T_max", "Compton edge T_max"), () => F(E() - Ep(180), 1) + " keV");
        }

        // ------------------------------------------------------------------ inverse square + shielding
        static void InvSq(Fig f)
        {
            var isoNames = new[] { "Cs-137", "Co-60", "Tc-99m", "F-18", "I-131" };
            int k = 0; float a = 370, x = 0;
            var ch = NewChart(f);
            ch.XLabel = T("mesafe (m)", "distance (m)"); ch.YLabel = "µSv/h"; ch.LogY = true; ch.AutoY = false; ch.YMin = 0.01f; ch.YMax = 10000; ch.XMin = 0.3f; ch.XMax = 5;
            var s = ch.Add(T("zırhsız", "unshielded"), Grey, 1.5f); s.Dashed = true;
            var sz = ch.Add(T("kurşun zırhlı", "with lead"), C1, 3f);
            UIKit.Seg(f.Controls, T("Kaynak", "Source"), isoNames, 0, i => { k = i; f.Refresh(); });
            UIKit.Slider(f.Controls, T("Aktivite", "Activity"), 10, 4000, a, v => F(v, 0) + " MBq", v => { a = v; f.Refresh(); }, 10);
            UIKit.Slider(f.Controls, T("Kurşun kalınlığı", "Lead thickness"), 0, 5, x, v => F(v, 1) + " cm", v => { x = v; f.Refresh(); }, 0.1f);
            Func<Isotope> iso = () => Isotopes.Get(isoNames[k]);
            Func<double> tr = () => Shielding.Transmission(ShieldMat.Lead, iso().GammaKeV, x);
            f.Update = () =>
            {
                Curve(s, 0.3, 5, 80, d => Shielding.DoseRate(iso(), a, d));
                Curve(sz, 0.3, 5, 80, d => Math.Max(1e-3, Shielding.DoseRate(iso(), a, d) * tr()));
            };
            f.Read(T("1 m'de (zırhlı)", "At 1 m (shielded)"), () => G(Shielding.DoseRate(iso(), a, 1) * tr()) + " µSv/h");
            f.Read(T("2 m'de (zırhlı)", "At 2 m (shielded)"), () => G(Shielding.DoseRate(iso(), a, 2) * tr()) + " µSv/h  (÷4)");
            f.Read(T("Zırh geçirgenliği (build-up dahil)", "Shield transmission (incl. build-up)"), () => G(tr()));
            f.Read("HVL (" + T("kurşun", "lead") + ")", () => G(Shielding.Hvl(ShieldMat.Lead, iso().GammaKeV)) + " cm @ " + F(iso().GammaKeV, 0) + " keV");
        }

        // ------------------------------------------------------------------ radioactive decay
        static void Decay(Fig f)
        {
            var isoNames = new[] { "Tc-99m", "F-18", "Ga-68", "I-131", "Lu-177" };
            int k = 0;
            var ch = NewChart(f);
            ch.YLabel = "MBq"; ch.AutoY = false; ch.YMin = 0; ch.YMax = 1050;
            var s = ch.Add(T("aktivite A(t)", "activity A(t)"), C3, 3f); s.Fill = true;
            UIKit.Seg(f.Controls, T("Radyonüklid", "Radionuclide"), isoNames, 0, i => { k = i; f.Refresh(); });
            Func<Isotope> iso = () => Isotopes.Get(isoNames[k]);
            Func<double> thH = () => iso().HalfLifeS / 3600.0;
            f.Update = () =>
            {
                double th = thH(); bool days = th > 48;
                double scale = days ? 24 : 1;
                ch.XLabel = days ? T("zaman (gün)", "time (days)") : T("zaman (saat)", "time (h)");
                ch.XMin = 0; ch.XMax = (float)(5 * th / scale);
                Curve(s, 0, 5 * th / scale, 120, tt => 1000 * Math.Pow(0.5, tt * scale / th));
                ch.VLines.Clear();
                for (int n = 1; n <= 4; n++) ch.VLines.Add(((float)(n * th / scale), Grey, n + "T½"));
            };
            f.Read("T½", () => iso().HalfLifeText());
            f.Read("λ = ln2 / T½", () => G(Math.Log(2) / thH()) + " h⁻¹");
            f.Read(T("Ortalama ömür τ = 1/λ", "Mean life τ = 1/λ"), () => G(thH() / Math.Log(2)) + " h");
            f.Read(T("24 saat sonra (1000 MBq'den)", "After 24 h (from 1000 MBq)"), () => G(1000 * Math.Pow(0.5, 24 / thH())) + " MBq");
            f.Read(T("Bozunma türü", "Decay mode"), () => (iso().Mode == DecayMode.BetaPlus ? "β⁺" : iso().Mode == DecayMode.BetaMinus ? "β⁻" : iso().Mode == DecayMode.Alpha ? "α" : T("izomerik geçiş", "isomeric transition")) + (iso().GammaKeV > 0 ? "  •  γ " + F(iso().GammaKeV, 0) + " keV" : ""));
        }

        static void Generator(Fig f)
        {
            float elute = 24;
            var ch = NewChart(f);
            ch.XLabel = T("elüsyondan sonra (saat)", "after elution (h)"); ch.YLabel = "GBq"; ch.XMin = 0; ch.XMax = 120; ch.AutoY = false; ch.YMin = 0; ch.YMax = 10.5f;
            var mo = ch.Add("Mo-99 (T½ 66 h)", C2, 2.5f);
            var tc = ch.Add("Tc-99m", C3, 3f); tc.Fill = true;
            UIKit.Slider(f.Controls, T("Bir sonraki elüsyon zamanı", "Next elution time"), 2, 72, elute, v => F(v, 0) + T(" sa", " h"), v => { elute = v; f.Refresh(); }, 1);
            f.Update = () =>
            {
                Curve(mo, 0, 120, 120, t => 10 * Math.Exp(-Math.Log(2) * t / Isotopes.MoHalfLifeH));
                Curve(tc, 0, 120, 240, t => Isotopes.TcActivity(10, 0, t));
                ch.VLines.Clear();
                ch.VLines.Add(((float)Isotopes.TcMaxTimeH(), Color.white, "t_max"));
                ch.VLines.Add((elute, C1, T("elüsyon", "elute")));
            };
            f.Read(T("En yüksek Tc-99m zamanı", "Time of maximum Tc-99m"), () => F(Isotopes.TcMaxTimeH(), 1) + T(" saat", " h"));
            f.Read(T("Elüsyonda Tc-99m", "Tc-99m at elution"), () => F(Isotopes.TcActivity(10, 0, elute), 2) + " GBq");
            f.Read(T("Tc/Mo oranı (geçici denge)", "Tc/Mo ratio (transient equilibrium)"), () => F(Isotopes.TcActivity(10, 0, 100) / (10 * Math.Exp(-Math.Log(2) * 100 / Isotopes.MoHalfLifeH)), 2));
        }

        // ------------------------------------------------------------------ depth dose
        static void Pdd(Fig f)
        {
            string[] names = { "Co-60", "6 MV", "10 MV", "18 MV", "6 MeV e⁻", "12 MeV e⁻" };
            double[] e = { 1.25, 6, 10, 18, -6, -12 };
            Color[] cols = { Grey, C1, C3, C2, C5, C4 };
            int sel = 1;
            var ch = NewChart(f, 240);
            ch.XLabel = T("su derinliği (cm)", "depth in water (cm)"); ch.YLabel = "PDD %"; ch.XMin = 0; ch.XMax = 30; ch.AutoY = false; ch.YMin = 0; ch.YMax = 105;
            var ser = new Chart.Series[names.Length];
            Func<int, double, double> pdd = (i, d) => e[i] < 0 ? CasePhysics.ElectronPdd(-e[i], d) : DepthDose.PhotonPdd(e[i], d);
            for (int i = 0; i < names.Length; i++) { ser[i] = ch.Add(names[i], cols[i], 1.5f); int k = i; Curve(ser[i], 0, 30, 150, d => pdd(k, d)); }
            UIKit.Seg(f.Controls, T("Vurgula", "Highlight"), names, sel, i => { sel = i; f.Refresh(); });
            f.Update = () => { for (int i = 0; i < ser.Length; i++) ser[i].Width = i == sel ? 4f : 1.2f; ch.VLines.Clear(); ch.VLines.Add((10, Color.white, "10 cm")); };
            Func<double> dmax = () => { double best = 0, at = 0; for (double d = 0; d < 6; d += 0.05) { double v = pdd(sel, d); if (v > best) { best = v; at = d; } } return at; };
            f.Read("d_max", () => F(dmax(), 1) + " cm");
            f.Read(T("Yüzey dozu", "Surface dose"), () => F(pdd(sel, 0), 0) + " %");
            f.Read("PDD(10 cm)", () => F(pdd(sel, 10), 1) + " %");
        }

        static void Bragg(Fig f)
        {
            float energy = 150; bool sobp = false;
            var ch = NewChart(f, 240);
            ch.XLabel = T("su derinliği (cm)", "depth in water (cm)"); ch.YLabel = T("bağıl doz", "relative dose"); ch.AutoY = false; ch.YMin = 0; ch.YMax = 1.15f;
            var ph = ch.Add("6 MV " + T("foton", "photons"), Grey, 1.5f); ph.Dashed = true;
            var pr = ch.Add(T("proton", "protons"), C4, 3f); pr.Fill = true;
            DepthDose.Bragg b = null;
            UIKit.Slider(f.Controls, T("Proton enerjisi", "Proton energy"), 70, 230, energy, v => F(v, 0) + " MeV", v => { energy = v; f.Refresh(); }, 5);
            UIKit.Toggle(f.Controls, T("Yayılmış Bragg piki (SOBP, 5 cm)", "Spread-out Bragg peak (SOBP, 5 cm)"), false, v => { sobp = v; f.Refresh(); });
            f.Update = () =>
            {
                b = DepthDose.GetBragg(energy);
                double xmax = System.Math.Ceiling(b.R0 * 1.25 + 2);
                ch.XMin = 0; ch.XMax = (float)xmax;
                Curve(ph, 0, xmax, 120, d => DepthDose.PhotonPdd(6, d) / 100.0);
                if (sobp)
                {
                    var s = DepthDose.BuildSobp(Math.Max(1, b.R0 - 5), b.R0);
                    double mx = 1e-9; for (double z = 0; z < xmax; z += 0.1) mx = Math.Max(mx, s.At(z));
                    Curve(pr, 0, xmax, 300, z => s.At(z) / mx);
                }
                else { double mx = b.Max(); Curve(pr, 0, xmax, 300, z => b.At(z) / mx); }
                ch.VLines.Clear(); ch.VLines.Add(((float)b.R0, C2, "R₀"));
            };
            f.Read(T("Menzil R₀ (CSDA)", "Range R₀ (CSDA)"), () => F(b.R0, 2) + " cm");
            f.Read(T("Pik / giriş oranı", "Peak / entrance ratio"), () => F(b.Max() / Math.Max(1e-9, b.At(0.2)), 1));
            f.Read(T("Kural: R ≈ 0.0022·E^1.77", "Rule: R ≈ 0.0022·E^1.77"), () => F(0.0022 * Math.Pow(energy, 1.77), 2) + " cm");
        }

        // ------------------------------------------------------------------ radiobiology
        static void Lq(Fig f)
        {
            float d = 2; int n = 30;
            var ch = NewChart(f, 230);
            ch.XLabel = T("toplam doz (Gy)", "total dose (Gy)"); ch.YLabel = T("yaşayan kesir", "surviving fraction"); ch.LogY = true; ch.AutoY = false; ch.YMin = 1e-6f; ch.YMax = 1; ch.XMin = 0; ch.XMax = 20;
            var a10 = ch.Add(T("tek doz, α/β = 10 (erken / tümör)", "single dose, α/β = 10 (early / tumour)"), C4, 2f);
            var a3 = ch.Add(T("tek doz, α/β = 3 (geç)", "single dose, α/β = 3 (late)"), C1, 2f);
            var f10 = ch.Add(T("fraksiyonlu, α/β = 10", "fractionated, α/β = 10"), C4, 2f); f10.Dashed = true;
            var f3 = ch.Add(T("fraksiyonlu, α/β = 3", "fractionated, α/β = 3"), C1, 2f); f3.Dashed = true;
            const double alpha = 0.3;
            UIKit.Slider(f.Controls, T("Fraksiyon dozu d", "Dose per fraction d"), 1, 10, d, v => F(v, 1) + " Gy", v => { d = v; f.Refresh(); }, 0.1f);
            UIKit.Slider(f.Controls, T("Fraksiyon sayısı n", "Number of fractions n"), 1, 40, n, v => F(v, 0), v => { n = Mathf.RoundToInt(v); f.Refresh(); }, 1);
            f.Update = () =>
            {
                Curve(a10, 0, 20, 100, D => Math.Exp(-alpha * D - alpha / 10 * D * D));
                Curve(a3, 0, 20, 100, D => Math.Exp(-alpha * D - alpha / 3 * D * D));
                Curve(f10, 0, 20, 100, D => Math.Exp(-alpha * D - alpha / 10 * d * D));
                Curve(f3, 0, 20, 100, D => Math.Exp(-alpha * D - alpha / 3 * d * D));
                ch.VLines.Clear(); ch.VLines.Add((Mathf.Min(20, d * n), Color.white, "n·d"));
            };
            Func<double, double> bed = ab => n * d * (1 + d / ab);
            f.Read(T("Toplam doz", "Total dose"), () => F(n * d, 1) + " Gy");
            f.Read("BED / EQD2 (α/β 10)", () => F(bed(10), 1) + " / " + F(bed(10) / 1.2, 1) + " Gy");
            f.Read("BED / EQD2 (α/β 3)", () => F(bed(3), 1) + " / " + F(bed(3) / (1 + 2.0 / 3), 1) + " Gy");
        }

        // ------------------------------------------------------------------ MR relaxation
        static void Relax(Fig f)
        {
            int mode = 0; float marker = 500, tr = 3000; int b0 = 0;
            int[] tis = { MriPhysics.WM, MriPhysics.GM, MriPhysics.CSF, MriPhysics.FAT };
            Color[] cols = { C1, Grey, C5, C2 };
            var ch = NewChart(f, 230);
            ch.AutoY = false; ch.YMin = 0; ch.YMax = 1.02f;
            var ser = new Chart.Series[tis.Length];
            for (int i = 0; i < tis.Length; i++) ser[i] = ch.Add(T(MriPhysics.Tissues[tis[i]].Tr, MriPhysics.Tissues[tis[i]].En).Split('(')[0].Trim(), cols[i], 2.5f);
            UIKit.Seg(f.Controls, T("Grafik", "Plot"), new[] { T("T1 geri kazanım", "T1 recovery"), T("T2 sönüm", "T2 decay"), T("SE sinyali (TE'ye göre)", "SE signal vs TE") }, 0, i => { mode = i; f.Refresh(); });
            UIKit.Seg(f.Controls, "B₀", new[] { "1.5 T", "3 T" }, 0, i => { b0 = i; f.Refresh(); });
            UIKit.Slider(f.Controls, T("İşaretçi (TR veya TE)", "Marker (TR or TE)"), 5, 5000, marker, v => F(v, 0) + " ms", v => { marker = v; f.Refresh(); }, 5);
            UIKit.Slider(f.Controls, T("TR (SE grafiği için)", "TR (for the SE plot)"), 200, 5000, tr, v => F(v, 0) + " ms", v => { tr = v; f.Refresh(); }, 50);
            double B() => b0 == 1 ? 3.0 : 1.5;
            f.Update = () =>
            {
                double xmax = mode == 0 ? 5000 : 300;
                ch.XMin = 0; ch.XMax = (float)xmax; ch.XLabel = mode == 0 ? "TR (ms)" : "TE (ms)";
                ch.YLabel = mode == 0 ? "Mz / M₀" : mode == 1 ? "Mxy / M₀" : T("sinyal", "signal");
                for (int i = 0; i < tis.Length; i++)
                {
                    var x = MriPhysics.Tissues[tis[i]];
                    double t1 = x.T1(B()), t2 = x.T2;
                    if (mode == 0) Curve(ser[i], 0, xmax, 150, t => 1 - Math.Exp(-t / t1));
                    else if (mode == 1) Curve(ser[i], 0, xmax, 150, t => Math.Exp(-t / t2));
                    else Curve(ser[i], 0, xmax, 150, t => x.PD * (1 - Math.Exp(-tr / t1)) * Math.Exp(-t / t2));
                }
                ch.VLines.Clear(); ch.VLines.Add((Mathf.Min(marker, (float)xmax), Color.white, mode == 0 ? "TR" : "TE"));
            };
            f.Read(T("Beyaz / gri cevher kontrastı (işaretçide)", "White / grey matter contrast (at marker)"), () =>
            {
                var w = MriPhysics.Tissues[MriPhysics.WM]; var g = MriPhysics.Tissues[MriPhysics.GM];
                double m = marker;
                double sw = mode == 0 ? 1 - Math.Exp(-m / w.T1(B())) : mode == 1 ? Math.Exp(-m / w.T2) : w.PD * (1 - Math.Exp(-tr / w.T1(B()))) * Math.Exp(-m / w.T2);
                double sg = mode == 0 ? 1 - Math.Exp(-m / g.T1(B())) : mode == 1 ? Math.Exp(-m / g.T2) : g.PD * (1 - Math.Exp(-tr / g.T1(B()))) * Math.Exp(-m / g.T2);
                return F(sw - sg, 3);
            });
            f.Read(T("Larmor frekansı", "Larmor frequency"), () => F(MriPhysics.GammaBar * B(), 1) + " MHz");
        }

        // ------------------------------------------------------------------ CT numbers and dose
        static void Hu(Fig f)
        {
            var ids = new[] { TissueId.Lung, TissueId.Adipose, TissueId.Water, TissueId.Blood, TissueId.Muscle, TissueId.SpongyBone, TissueId.CorticalBone, TissueId.ContrastBlood };
            string[] nmTr = { "Akciğer", "Yağ", "Su", "Kan", "Kas", "Süngerimsi kemik", "Kortikal kemik", "Kontrastlı kan" };
            string[] nmEn = { "Lung", "Fat", "Water", "Blood", "Muscle", "Spongy bone", "Cortical bone", "Contrast blood" };
            float e = 70;
            var ch = NewChart(f, 230);
            ch.XMin = -0.5f; ch.XMax = ids.Length - 0.5f; ch.YLabel = "HU"; ch.AutoY = false; ch.YMin = -1000; ch.YMax = 2000;
            ch.XFmt = x => { int i = Mathf.RoundToInt(x); return i >= 0 && i < ids.Length ? T(nmTr[i], nmEn[i]).Split(' ')[0] : ""; };
            var s = ch.Add("HU", C1, 14f); s.Bars = true;
            UIKit.Slider(f.Controls, T("Efektif (monoenerjetik) enerji", "Effective (monoenergetic) energy"), 40, 150, e, v => F(v, 0) + " keV", v => { e = v; f.Refresh(); }, 1);
            Func<int, double> hu = i =>
            {
                double mw = Tissues.Get(TissueId.Water).Mu(e / 1000.0), ma = Tissues.Get(TissueId.Air).Mu(e / 1000.0);
                return 1000 * (Tissues.Get(ids[i]).Mu(e / 1000.0) - mw) / (mw - ma);
            };
            f.Update = () => { s.Pts.Clear(); for (int i = 0; i < ids.Length; i++) s.Pts.Add(new Vector2(i, (float)hu(i))); ch.HLines.Clear(); ch.HLines.Add((0, Grey, "0")); };
            f.Read(T("Yağ / kas", "Fat / muscle"), () => F(hu(1), 0) + " / " + F(hu(4), 0) + " HU");
            f.Read(T("Kortikal kemik / kontrastlı kan", "Cortical bone / contrast blood"), () => F(hu(6), 0) + " / " + F(hu(7), 0) + " HU");
        }

        static void Ssde(Fig f)
        {
            float ap = 24, lat = 34, ctdi = 12;
            var ch = NewChart(f, 210);
            ch.XLabel = T("efektif çap D_eff (cm)", "effective diameter D_eff (cm)"); ch.YLabel = "f = SSDE / CTDIvol"; ch.XMin = 8; ch.XMax = 50; ch.AutoY = false; ch.YMin = 0; ch.YMax = 3;
            var s = ch.Add("AAPM 204 (32 cm)", C1, 3f);
            Curve(s, 8, 50, 100, CasePhysics.SsdeFactor);
            UIKit.Slider(f.Controls, T("Ön-arka çap (AP)", "Anterior-posterior (AP)"), 8, 45, ap, v => F(v, 0) + " cm", v => { ap = v; f.Refresh(); }, 1);
            UIKit.Slider(f.Controls, T("Yan çap (LAT)", "Lateral (LAT)"), 10, 60, lat, v => F(v, 0) + " cm", v => { lat = v; f.Refresh(); }, 1);
            UIKit.Slider(f.Controls, "CTDIvol", 1, 40, ctdi, v => F(v, 1) + " mGy", v => { ctdi = v; f.Refresh(); }, 0.5f);
            Func<double> de = () => Math.Sqrt(ap * lat);
            f.Update = () => { ch.VLines.Clear(); ch.VLines.Add(((float)de(), C2, "D_eff")); };
            f.Read("D_eff = √(AP·LAT)", () => F(de(), 1) + " cm");
            f.Read("f", () => F(CasePhysics.SsdeFactor(de()), 2));
            f.Read("SSDE", () => F(ctdi * CasePhysics.SsdeFactor(de()), 1) + " mGy");
        }

        static void Noise(Fig f)
        {
            float d = 30; float mas = 200;
            var ch = NewChart(f, 210);
            ch.XLabel = "mAs"; ch.YLabel = T("gürültü σ (HU)", "noise σ (HU)"); ch.XMin = 25; ch.XMax = 600; ch.AutoY = false; ch.YMin = 0; ch.YMax = 60;
            var s = ch.Add(T("seçilen çap", "chosen diameter"), C1, 3f);
            var r = ch.Add(T("ortalama erişkin (30 cm)", "average adult (30 cm)"), Grey, 1.5f); r.Dashed = true;
            // σ ∝ 1/√(N), N ∝ mAs · e^(−μ·D);  μ_eff ≈ 0.19 cm⁻¹ at ~70 keV, reference 12 HU at 250 mAs / 30 cm
            Func<double, double, double> sigma = (m, dia) => 12.0 * Math.Sqrt(250.0 / m) * Math.Exp(0.5 * 0.19 * (dia - 30));
            UIKit.Slider(f.Controls, T("Hasta çapı", "Patient diameter"), 15, 50, d, v => F(v, 0) + " cm", v => { d = v; f.Refresh(); }, 1);
            UIKit.Slider(f.Controls, "mAs", 25, 600, mas, v => F(v, 0), v => { mas = v; f.Refresh(); }, 5);
            f.Update = () => { Curve(s, 25, 600, 100, m => sigma(m, d)); Curve(r, 25, 600, 100, m => sigma(m, 30)); ch.VLines.Clear(); ch.VLines.Add((mas, C2, "")); };
            f.Read("σ", () => F(sigma(mas, d), 1) + " HU");
            f.Read(T("Aynı σ için gereken mAs (30 cm'e göre)", "mAs for the same σ (vs 30 cm)"), () => "× " + F(Math.Exp(0.19 * (d - 30)), 2));
        }
    }
}
