using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;
using MedPhys.Core;
using MedPhys.Physics;
using MedPhys.UI;
using static MedPhys.Core.Loc;

namespace MedPhys.Modules
{
    /// <summary>Nuclear medicine: stochastic radioactive decay, Mo-99/Tc-99m generator, PET acquisition and reconstruction.</summary>
    public sealed class NucMedModule : ModuleBase
    {
        public override string Id => "nucmed";
        public override string Title => T("Nükleer Tıp", "Nuclear Medicine");
        public override string Subtitle => tab == 0 ? T("Radyoaktif bozunma (Monte Carlo)", "Radioactive decay (Monte Carlo)")
                                         : tab == 1 ? T("Mo-99 / Tc-99m jeneratörü", "Mo-99 / Tc-99m generator") : T("PET: koinsidans ve rekonstrüksiyon", "PET: coincidences and reconstruction");

        protected override (string id, string tr, string en)[] Tasks => new[]
        {
            ("halflife", "Tc-99m yarı ömrünü Monte Carlo ile %5 doğrulukla ölç", "Measure the Tc-99m half-life by Monte Carlo within 5 %"),
            ("effective", "Biyolojik yarı ömür ekleyip efektif yarı ömrü gör (I-131)", "Add a biological half-life and see the effective half-life (I-131)"),
            ("elute", "Jeneratörü sağımdan 20–26 saat sonra tekrar sağ (maksimum verim)", "Elute the generator 20–26 h after the previous elution (maximum yield)"),
            ("pet", "En az 500 000 koinsidans topla ve MLEM ile görüntü oluştur", "Collect ≥ 500 000 coincidences and reconstruct with MLEM"),
            ("ac", "Zayıflama düzeltmesinin görüntü merkezindeki etkisini karşılaştır", "Compare the effect of attenuation correction at the image centre"),
        };

        static int tab;
        static float autoReconAt = -1;

        protected override void Build()
        {
            Env.Room(Root.transform, new Color(0.4f, 1f, 0.6f));
            var L = LeftScroll.contentContainer;
            var sec = UIKit.Section(L, T("Bölüm", "Section"));
            UIKit.Seg(sec, null, new[] { T("Bozunma", "Decay"), T("Jeneratör", "Generator"), "PET" }, tab, i => { tab = i; App.Open("nucmed"); });
            if (tab == 0) BuildDecay();
            else if (tab == 1) BuildGenerator();
            else BuildPet();
            BuildTasks(L);
        }

        public override void Dispose()
        {
            pet?.Stop();
            base.Dispose();
        }

        public override void Tick(float dt)
        {
            base.Tick(dt);
            if (tab == 0) TickDecay(dt);
            else if (tab == 1) TickGenerator(dt);
            else TickPet(dt);
        }

        // ====================================================================== 1. DECAY
        static int isoIdx;
        static int nAtoms = 400;
        static float bioHalfLifeH = 0;      // 0 = none
        Isotope iso;
        readonly List<(Transform t, Renderer r, int state)> atoms = new List<(Transform, Renderer, int)>();   // 0 parent, 1 decayed, 2 excreted
        Material matParent, matDecayed;
        LineBatch emissions;
        double simT;            // simulated time (s)
        double speed;           // simulated seconds per real second
        bool decayRunning = true;
        Chart decayChart;
        Chart.Series sMc, sAn, sEff;
        Label vLambda, vTau, vSpec, vRemain, vMeasured, vTeff, vTime;
        Vector3 vialPos;
        System.Random drng = new System.Random(5);
        float sampleT;
        double measuredHalf = -1;

        void BuildDecay()
        {
            iso = Isotopes.All[isoIdx];
            vialPos = new Vector3(0, 1.0f, 0);
            // table + vial
            var table = GameObject.CreatePrimitive(PrimitiveType.Cube);
            UnityEngine.Object.Destroy(table.GetComponent<Collider>());
            table.transform.SetParent(Root.transform, false);
            table.transform.position = new Vector3(0, 0.45f, 0); table.transform.localScale = new Vector3(1.4f, 0.9f, 0.8f);
            table.GetComponent<Renderer>().sharedMaterial = Res.Mat("M_ShellGrey");
            var vial = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            UnityEngine.Object.Destroy(vial.GetComponent<Collider>());
            vial.transform.SetParent(Root.transform, false);
            vial.transform.position = vialPos + new Vector3(0, 0.1f, 0); vial.transform.localScale = new Vector3(0.24f, 0.11f, 0.24f);
            vial.GetComponent<Renderer>().sharedMaterial = Res.LitTransparent(new Color(0.7f, 0.9f, 1f, 0.15f), 0.95f);
            var pigD = Res.Model("LeadPig", Root.transform, new Vector3(0.48f, 0.9f, 0.12f), 20);
            var sm = Res.Model("SurveyMeter", Root.transform, new Vector3(-0.45f, 0.99f, 0.05f), -30);
            Part(pigD.transform, "Kurşun kap (pig)", "Lead container (pig)",
                 "Radyofarmasötik flakon taşınırken ve saklanırken gama ışınlarını zırhlar; Tc-99m'in 140 keV fotonları için birkaç mm kurşun yeterlidir.",
                 "Shields the gamma rays while the vial is stored or carried; a few mm of lead is enough for the 140 keV photons of Tc-99m.", new Vector2(1f, 0.6f), 90);
            Part(sm.transform, "Radyasyon ölçer", "Survey meter",
                 "GM sayacı ya da iyon odası; doz hızını µSv/h olarak gösterir. Laboratuvarda kontaminasyon kontrolü için kullanılır.",
                 "A GM counter or ion chamber showing the dose rate in µSv/h; used for contamination checks in the hot lab.", new Vector2(-1f, 0.6f), 90);
            var vialAnchor = Child("VialCallout"); vialAnchor.transform.position = vialPos + new Vector3(0, 0.1f, 0);
            Part(vialAnchor.transform, "Radyoaktif çekirdekler", "Radioactive nuclei",
                 "Her küre bir çekirdektir. Her zaman adımında p = 1 − e^(−λΔt) olasılıkla bozunur ve bir foton çizgisi yayar; az çekirdekle N(t) eğrisi istatistiksel olarak dalgalanır.",
                 "Each sphere is a nucleus. In every time step it decays with probability p = 1 − e^(−λΔt) and emits a photon line; with few nuclei N(t) fluctuates statistically.", new Vector2(0.3f, 1f), 120);

            matParent = Res.Emissive(new Color(0.3f, 1f, 0.5f), new Color(0.5f, 2.5f, 0.8f));
            matDecayed = Res.Lit(new Color(0.25f, 0.27f, 0.3f), 0.4f);
            var mesh = MeshGen.Ellipsoid(Vector3.zero, Vector3.one * 0.5f, Quaternion.identity, 6, 10);
            for (int i = 0; i < nAtoms; i++)
            {
                var g = new GameObject("Atom");
                g.transform.SetParent(Root.transform, false);
                float r = 0.105f * Mathf.Sqrt((float)drng.NextDouble()), a = (float)drng.NextDouble() * Mathf.PI * 2;
                g.transform.position = vialPos + new Vector3(Mathf.Cos(a) * r, 0.01f + 0.19f * (float)drng.NextDouble(), Mathf.Sin(a) * r);
                g.transform.localScale = Vector3.one * 0.011f;
                g.AddComponent<MeshFilter>().sharedMesh = mesh;
                var rr = g.AddComponent<MeshRenderer>(); rr.sharedMaterial = matParent;
                rr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                atoms.Add((g.transform, rr, 0));
            }
            emissions = new LineBatch();
            MeshObj("Emissions", emissions.Mesh, Res.Additive(Color.white, 2f));
            App.Rig.Frame(vialPos + new Vector3(0, 0.1f, 0), 1.2f, 20f, 18f, true);
            App.Rig.MinDistance = 0.3f; App.Rig.MaxDistance = 5f;
            speed = iso.HalfLifeS / 6.0;   // one half-life every 6 s

            var Lp = LeftScroll.contentContainer;
            var sec = UIKit.Section(Lp, T("Radyonüklid", "Radionuclide"));
            var names = new string[Isotopes.All.Length];
            for (int i = 0; i < names.Length; i++) names[i] = Isotopes.All[i].Name;
            UIKit.Seg(sec, null, names, isoIdx, i => { isoIdx = i; App.Open("nucmed"); });
            UIKit.Text(sec, "<b>" + iso.Name + "</b> — T½ = " + iso.HalfLifeText() + " • " + ModeText(iso) + (iso.GammaKeV > 0 ? " • γ " + F(iso.GammaKeV, 0) + " keV" : "") + "\n" + T(iso.UseTr, iso.UseEn));
            UIKit.Slider(sec, T("Başlangıç atom sayısı N₀", "Initial atoms N₀"), 50, 800, nAtoms, v => F(v, 0), v => { nAtoms = Mathf.RoundToInt(v); restart = 0.5f; }, 10);
            UIKit.Slider(sec, T("Biyolojik yarı ömür (0 = yok)", "Biological half-life (0 = none)"), 0, 200, bioHalfLifeH, v => v <= 0 ? T("yok", "none") : F(v, 0) + " h", v => { bioHalfLifeH = v; restart = 0.5f; }, 2);
            UIKit.Slider(sec, T("Zaman hızı (yarı ömür / 6 s)", "Time speed (half-lives / 6 s)"), 0.2f, 5f, 1f, v => F(v, 1) + "×", v => speed = iso.HalfLifeS / 6.0 * v, 0.1f);
            var row = UIKit.ButtonRow(sec);
            UIKit.Button(row, T("Durdur / Devam", "Pause / Run"), () => decayRunning = !decayRunning);
            UIKit.Button(row, T("Yeniden başlat", "Restart"), () => App.Open("nucmed"));

            var R = RightScroll.contentContainer;
            sec = UIKit.Section(R, T("Kalan çekirdek sayısı N(t)", "Remaining nuclei N(t)"));
            decayChart = new Chart(230) { XMin = 0, XMax = 6, XLabel = "t / T½", YLabel = "N", AutoY = false, YMin = 0, YMax = nAtoms * 1.05f };
            sMc = decayChart.Add("Monte Carlo", new Color(0.3f, 1f, 0.5f), 2f);
            sAn = decayChart.Add("N₀·e^(−λt)", new Color(1f, 0.7f, 0.25f), 1.6f); sAn.Dashed = true;
            sEff = decayChart.Add(T("Efektif (fiziksel + biyolojik)", "Effective (physical + biological)"), new Color(0.6f, 0.6f, 1f), 1.6f); sEff.Dashed = true;
            sec.Add(decayChart.Legend()); sec.Add(decayChart);
            vTime = UIKit.KV(sec, T("Geçen süre", "Elapsed time"));
            vRemain = UIKit.KV(sec, T("Kalan / bozunan", "Remaining / decayed"));
            vMeasured = UIKit.KV(sec, T("Ölçülen T½ (MC)", "Measured T½ (MC)"), "–");
            sec = UIKit.Section(R, T("Sabitler", "Constants"));
            vLambda = UIKit.KV(sec, T("Bozunma sabiti λ", "Decay constant λ"), iso.Lambda.ToString("0.###E+0") + " s⁻¹");
            vTau = UIKit.KV(sec, T("Ortalama ömür τ = 1/λ", "Mean life τ = 1/λ"), FormatTime(1 / iso.Lambda));
            vSpec = UIKit.KV(sec, T("Özgül aktivite (saf)", "Specific activity (pure)"), (iso.SpecificActivity / 3.7e10).ToString("0.###E+0") + " Ci/g  (" + iso.SpecificActivity.ToString("0.##E+0") + " Bq/g)");
            double tb = bioHalfLifeH * 3600;
            double teff = tb > 0 ? iso.HalfLifeS * tb / (iso.HalfLifeS + tb) : iso.HalfLifeS;
            vTeff = UIKit.KV(sec, T("Efektif yarı ömür", "Effective half-life"), FormatTime(teff));
            if (tb > 0 && iso.Name == "I-131") CompleteTask("effective");
            SetTip("Her atom her zaman adımında p = 1 − e^(−λΔt) olasılıkla bozunur. Az sayıda atomla eğri istatistiksel olarak dalgalanır.",
                   "Every atom decays in each time step with probability p = 1 − e^(−λΔt). With few atoms the curve fluctuates statistically.");
        }

        float restart = -1;

        static string ModeText(Isotope i) => i.Mode == DecayMode.BetaPlus ? "β⁺" : i.Mode == DecayMode.BetaMinus ? "β⁻" : i.Mode == DecayMode.Alpha ? "α" : T("izomerik geçiş (IT)", "isomeric transition (IT)");

        static string FormatTime(double s)
        {
            if (s < 3600) return F(s / 60, 1) + " min";
            if (s < 86400 * 3) return F(s / 3600, 2) + " h";
            if (s < 86400 * 730) return F(s / 86400, 2) + T(" gün", " d");
            return F(s / (86400 * 365.25), 2) + T(" yıl", " y");
        }

        void TickDecay(float dt)
        {
            if (restart > 0) { restart -= dt; if (restart <= 0) { App.Open("nucmed"); return; } }
            if (decayRunning)
            {
                double dts = speed * dt;
                simT += dts;
                double pDecay = 1 - Math.Exp(-iso.Lambda * dts);
                double lb = bioHalfLifeH > 0 ? Math.Log(2) / (bioHalfLifeH * 3600) : 0;
                double pBio = 1 - Math.Exp(-lb * dts);
                for (int i = 0; i < atoms.Count; i++)
                {
                    var a = atoms[i];
                    if (a.state != 0) continue;
                    if (drng.NextDouble() < pDecay)
                    {
                        a.state = 1; a.r.sharedMaterial = matDecayed; a.t.localScale *= 0.8f;
                        Emit(a.t.position);
                    }
                    else if (lb > 0 && drng.NextDouble() < pBio)
                    {
                        a.state = 2; a.r.enabled = false;
                    }
                    atoms[i] = a;
                }
            }
            emissions.Fade(Mathf.Pow(0.02f, dt));
            emissions.Apply();
            sampleT -= dt;
            if (sampleT <= 0)
            {
                sampleT = 0.1f;
                int alive = 0, dec = 0;
                foreach (var a in atoms) { if (a.state == 0) alive++; else if (a.state == 1) dec++; }
                double tHalf = simT / iso.HalfLifeS;
                if (tHalf <= decayChart.XMax) sMc.Pts.Add(new Vector2((float)tHalf, alive));
                sAn.Pts.Clear(); sEff.Pts.Clear();
                double lb = bioHalfLifeH > 0 ? Math.Log(2) / (bioHalfLifeH * 3600) : 0;
                for (float x = 0; x <= decayChart.XMax; x += 0.05f)
                {
                    sAn.Pts.Add(new Vector2(x, (float)(atoms.Count * Math.Exp(-Math.Log(2) * x))));
                    if (lb > 0) sEff.Pts.Add(new Vector2(x, (float)(atoms.Count * Math.Exp(-(Math.Log(2) + lb * iso.HalfLifeS) * x))));
                }
                decayChart.Refresh();
                vTime.text = FormatTime(simT) + "  (" + F(tHalf, 2) + " T½)";
                vRemain.text = alive + " / " + dec + (bioHalfLifeH > 0 ? "  (" + T("atılan ", "excreted ") + (atoms.Count - alive - dec) + ")" : "");
                if (measuredHalf < 0 && alive <= atoms.Count / 2 && bioHalfLifeH <= 0)
                {
                    measuredHalf = simT;
                    double err = Math.Abs(measuredHalf - iso.HalfLifeS) / iso.HalfLifeS;
                    vMeasured.text = FormatTime(measuredHalf) + "  (" + T("gerçek ", "true ") + iso.HalfLifeText() + ", " + T("hata ", "error ") + F(err * 100, 1) + " %)";
                    if (iso.Name == "Tc-99m" && err < 0.05) CompleteTask("halflife");
                }
            }
        }

        void Emit(Vector3 p)
        {
            float rx = (float)drng.NextDouble() * 2 - 1, ry = (float)drng.NextDouble() * 2 - 1, rz = (float)drng.NextDouble() * 2 - 1;
            var d = new Vector3(rx, ry, rz).normalized;
            switch (iso.Mode)
            {
                case DecayMode.BetaPlus:
                    // positron annihilates within ~1 mm → two back-to-back 511 keV photons
                    emissions.Add(p - d * 0.6f, p + d * 0.6f, new Color(1f, 0.85f, 0.2f, 0.9f), new Color(1f, 0.85f, 0.2f, 0.9f));
                    break;
                case DecayMode.Alpha:
                    emissions.Add(p, p + d * 0.03f, new Color(1f, 0.25f, 0.2f, 1f));
                    break;
                case DecayMode.BetaMinus:
                {
                    var q = p;
                    for (int k = 0; k < 5; k++)
                    {
                        var nq = q + (d + new Vector3((float)drng.NextDouble() - 0.5f, (float)drng.NextDouble() - 0.5f, (float)drng.NextDouble() - 0.5f) * 0.8f).normalized * 0.012f;
                        emissions.Add(q, nq, new Color(0.3f, 0.75f, 1f, 1f)); q = nq;
                    }
                    if (iso.GammaKeV > 0 && drng.NextDouble() < Math.Min(1, iso.GammaYield)) emissions.Add(p, p - d * 0.5f, new Color(1f, 0.85f, 0.2f, 0.7f));
                    break;
                }
                default:
                    emissions.Add(p, p + d * 0.5f, new Color(1f, 0.85f, 0.2f, 0.8f));
                    break;
            }
        }

        // ====================================================================== 2. GENERATOR
        double genT;              // hours since start
        double lastElution;       // time of last elution (h)
        double tcAtElution;       // Tc activity in the column right after the last elution
        static double amoStart = 37000;   // MBq (1 Ci)
        bool genRunning = true;
        float genSpeed = 4f;      // hours per second
        Chart genChart;
        Chart.Series sMo, sTc, sEl;
        Label vMo, vTc, vRatio, vSince, vLastYield;
        readonly List<Vector2> moPts = new List<Vector2>(), tcPts = new List<Vector2>(), elPts = new List<Vector2>();
        MeshRenderer column; Material columnMat; MeshRenderer vialR; Material vialMat; float vialGlow;

        void BuildGenerator()
        {
            var table = GameObject.CreatePrimitive(PrimitiveType.Cube);
            UnityEngine.Object.Destroy(table.GetComponent<Collider>());
            table.transform.SetParent(Root.transform, false);
            table.transform.position = new Vector3(0, 0.45f, 0); table.transform.localScale = new Vector3(1.4f, 0.9f, 0.8f);
            table.GetComponent<Renderer>().sharedMaterial = Res.Mat("M_ShellGrey");
            var gen = Res.Model("LeadPig", Root.transform, new Vector3(-0.15f, 0.9f, 0), 0);
            gen.transform.localScale = new Vector3(2.4f, 2.6f, 2.4f);
            Part(gen.transform, "Mo-99/Tc-99m jeneratörü", "Mo-99/Tc-99m generator",
                 "Kurşunla zırhlı alümina kolonda Mo-99 (T½ 66 sa) bozunarak Tc-99m (T½ 6 sa) üretir. Serum fizyolojik kolondan geçirilince yalnızca Tc-99m perteknetat yıkanır (elüsyon).",
                 "In a lead-shielded alumina column Mo-99 (T½ 66 h) decays to Tc-99m (T½ 6 h). Saline passed through the column washes out only the Tc-99m pertechnetate (elution).", new Vector2(-1f, 0.7f), 100);
            columnMat = Res.Additive(new Color(0.3f, 1f, 0.6f, 0.6f), 1.5f, 0.3f);
            column = MeshObj("Column", MeshGen.Cylinder(Vector3.zero, new Vector3(0.025f, 0.025f, 0.13f), Quaternion.Euler(-90, 0, 0), 20), columnMat);
            column.transform.position = new Vector3(-0.15f, 1.32f, 0);
            vialMat = Res.Additive(new Color(0.4f, 0.9f, 1f, 0.2f), 1.5f, 0.3f);
            vialR = MeshObj("Eluate", MeshGen.Cylinder(Vector3.zero, new Vector3(0.03f, 0.03f, 0.05f), Quaternion.Euler(-90, 0, 0), 20), vialMat);
            vialR.transform.position = new Vector3(0.35f, 0.96f, 0);
            Part(vialR.transform, "Eluat flakonu", "Eluate vial",
                 "Kurşun kap içindeki vakumlu flakon, kolondan geçen serum fizyolojiği ve Tc-99m'i çeker. Mo-99 sızıntısı (breakthrough) < 0,15 kBq Mo / MBq Tc olmalıdır.",
                 "An evacuated vial in a lead pot draws the saline and Tc-99m through the column. Mo-99 breakthrough must stay < 0.15 kBq Mo per MBq Tc.", new Vector2(1f, 0.7f), 100);
            Res.Model("LeadPig", Root.transform, new Vector3(0.35f, 0.9f, 0), 0);
            App.Rig.Frame(new Vector3(0.05f, 1.1f, 0), 1.6f, 15f, 15f, true);

            var Lp = LeftScroll.contentContainer;
            var sec = UIKit.Section(Lp, T("Jeneratör", "Generator"));
            UIKit.Text(sec, T("Mo-99 (T½ 65.9 h) → %87.6 → Tc-99m (T½ 6.01 h). Alumina kolonda tutulan Mo-99 bozunurken Tc-99m birikir; serum fizyolojik ile 'sağılır'.",
                              "Mo-99 (T½ 65.9 h) → 87.6 % → Tc-99m (T½ 6.01 h). Mo-99 is held on an alumina column; Tc-99m builds up as it decays and is 'eluted' with saline."));
            UIKit.Slider(sec, T("Başlangıç Mo-99 aktivitesi", "Initial Mo-99 activity"), 3700, 74000, (float)amoStart, v => F(v / 1000, 1) + " GBq", v => amoStart = v, 100);
            UIKit.Slider(sec, T("Zaman hızı", "Time speed"), 0.5f, 12f, genSpeed, v => F(v, 1) + " h/s", v => genSpeed = v, 0.5f);
            var row = UIKit.ButtonRow(sec);
            UIKit.Button(row, T("SAĞ (elüsyon)", "ELUTE"), Elute, "mp-btn-primary");
            UIKit.Button(row, T("Durdur / Devam", "Pause / Run"), () => genRunning = !genRunning);
            UIKit.Button(row, T("Yeniden başlat", "Restart"), () => App.Open("nucmed"));
            vLastYield = UIKit.KV(sec, T("Son sağım verimi", "Last elution yield"), "–");

            var R = RightScroll.contentContainer;
            sec = UIKit.Section(R, T("Aktiviteler (geçici denge)", "Activities (transient equilibrium)"));
            genChart = new Chart(240) { XMin = 0, XMax = 168, XLabel = T("saat", "hours"), YLabel = "GBq", AutoY = true };
            sMo = genChart.Add("Mo-99", new Color(1f, 0.6f, 0.25f), 2f);
            sTc = genChart.Add("Tc-99m", new Color(0.3f, 1f, 0.6f), 2f);
            sEl = genChart.Add(T("Sağılan", "Eluted"), new Color(0.4f, 0.8f, 1f), 3f); sEl.Bars = true;
            sec.Add(genChart.Legend()); sec.Add(genChart);
            vSince = UIKit.KV(sec, T("Son sağımdan beri", "Since last elution"));
            vMo = UIKit.KV(sec, "A(Mo-99)"); vTc = UIKit.KV(sec, "A(Tc-99m)"); vRatio = UIKit.KV(sec, "A(Tc)/A(Mo)");
            UIKit.Note(sec, T("Maksimum Tc-99m, sağımdan t_max = ln(λTc/λMo)/(λTc − λMo) ≈ 22.9 saat sonra. Dengede A_Tc/A_Mo → 0.876·λTc/(λTc − λMo) ≈ 0.96.",
                              "Tc-99m peaks t_max = ln(λTc/λMo)/(λTc − λMo) ≈ 22.9 h after elution. At equilibrium A_Tc/A_Mo → 0.876·λTc/(λTc − λMo) ≈ 0.96."));
            SetTip("SAĞ butonuna basarak Tc-99m'i al. En yüksek verim için önceki sağımdan yaklaşık 23 saat bekle.",
                   "Press ELUTE to extract Tc-99m. For the highest yield wait ≈ 23 h after the previous elution.");
        }

        double MoAt(double t) => amoStart * Math.Exp(-Math.Log(2) / Isotopes.MoHalfLifeH * t);
        double TcAt(double t) => Isotopes.TcActivity(MoAt(lastElution), tcAtElution, t - lastElution);

        void Elute()
        {
            double tc = TcAt(genT);
            double since = genT - lastElution;
            double yield = 0.9 * tc;
            elPts.Add(new Vector2((float)genT, (float)(yield / 1000)));
            vLastYield.text = F(yield / 1000, 2) + " GBq  (" + F(since, 1) + T(" saat sonra", " h after") + ")";
            if (lastElution > 0 || genT > 0) if (since >= 20 && since <= 26) CompleteTask("elute");
            lastElution = genT;
            tcAtElution = 0.1 * tc;
            vialGlow = 1f;
        }

        void TickGenerator(float dt)
        {
            if (genRunning && genT < 168) genT += genSpeed * dt;
            genT = Math.Min(genT, 168);
            double mo = MoAt(genT), tc = TcAt(genT);
            if (moPts.Count == 0 || genT - moPts[moPts.Count - 1].x > 0.25)
            {
                moPts.Add(new Vector2((float)genT, (float)(mo / 1000)));
                tcPts.Add(new Vector2((float)genT, (float)(tc / 1000)));
                sMo.Set(moPts); sTc.Set(tcPts); sEl.Set(elPts);
                genChart.Refresh();
            }
            vSince.text = F(genT - lastElution, 1) + T(" saat", " h") + "  (t = " + F(genT, 1) + " h)";
            vMo.text = F(mo / 1000, 2) + " GBq"; vTc.text = F(tc / 1000, 2) + " GBq"; vRatio.text = F(tc / Math.Max(mo, 1e-9), 3);
            columnMat.SetColor("_Color", new Color(0.3f, 1f, 0.6f, (float)(0.15 + 0.8 * tc / Math.Max(mo, 1))));
            vialGlow = Mathf.Max(0.15f, vialGlow - dt * 0.15f);
            vialMat.SetColor("_Color", new Color(0.4f, 0.9f, 1f, vialGlow));
        }

        // ====================================================================== 3. PET
        PetSim pet;
        static bool petPositronGa, petAc = true, petScatter = true, petRandoms = true, petMlem = true;
        static float petActivity = 200, petContrast = 4;
        static int petIter = 15;
        LineBatch lors;
        Transform ringT;
        Mesh glowMesh; Color[] glowCols; float[] crystalHeat;
        const float VisScale = 0.45f / PetSim.RingR;
        Texture2D sinoTex, imgTex;
        ImageView sinoImg, recImg;
        Label vTrue, vScat, vRand, vNecr, vRecStat, vRc;
        Task<float[]> recTask;
        bool recAc;
        float sinoT;
        float centerAc = -1, centerNoAc = -1;

        void BuildPet()
        {
            var scanner = Res.Model("PET_Scanner", Root.transform);
            var isoT = scanner.Find("PET_Iso");
            var cover = scanner.Find("PET_Cover");
            if (cover != null) cover.GetComponent<Renderer>().sharedMaterial = Res.Hologram(new Color(0.5f, 1f, 0.7f, 0.04f), new Color(0.4f, 1f, 0.6f, 0.5f), 2f);
            var flare = scanner.Find("PET_Flare");
            if (flare != null) flare.gameObject.SetActive(false);
            ringT = Child("Ring").transform;
            ringT.position = isoT.position;
            BuildRingMesh();
            Part(ringT, "Dedektör halkası (LYSO + SiPM)", "Detector ring (LYSO + SiPM)",
                 "Pozitron bir elektronla yok olunca zıt yönde iki 511 keV foton çıkar. Karşılıklı iki dedektör bunları birkaç ns içinde yakalarsa bir koinsidans (yanıt çizgisi, LOR) kaydedilir.",
                 "A positron annihilates with an electron into two opposite 511 keV photons. If two opposing detectors catch them within a few ns, a coincidence (line of response, LOR) is recorded.", new Vector2(1f, 0.6f), 90,
                 ringT.position + new Vector3(0.32f, 0.32f, 0));   // on the crystal ring (r = 0.45)
            var nema = Child("NemaCallout"); nema.transform.position = ringT.position + new Vector3(-0.08f, -0.06f, 0);
            Part(nema.transform, "NEMA görüntü kalitesi fantomu", "NEMA image-quality phantom",
                 "Su dolu silindirde farklı boyutlarda sıcak küreler ve bir akciğer eki: kontrast geri kazanımı (RC), kısmi hacim etkisi ve zayıflama düzeltmesi bununla sınanır.",
                 "Hot spheres of several sizes and a lung insert in a water-filled cylinder: recovery coefficients, the partial-volume effect and attenuation correction are tested with it.", new Vector2(-1f, -0.7f), 120);
            Part(scanner.Find("PET_CouchTop"), "Hasta masası", "Patient couch",
                 "Tüm vücut taramasında hasta yataklar (bed) halinde ilerletilir; eksenel görüş alanı 15–26 cm, uzun eksenli sistemlerde 1–2 m.",
                 "For a whole-body scan the patient is stepped through bed positions; the axial field of view is 15–26 cm, 1–2 m in long-axial systems.", new Vector2(-1f, -0.8f), 100);
            // phantom: water cylinder with hot spheres and a lung insert
            pet = new PetSim { Contrast = petContrast, ActivityMBq = petActivity, Scatter = petScatter, Randoms = petRandoms, PositronRangeMm = petPositronGa ? 2.9 : 0.6 };
            var body = MeshObj("NemaBody", MeshGen.Cylinder(Vector3.zero, new Vector3(pet.BodyR * VisScale, pet.BodyR * VisScale, 0.09f), Quaternion.identity, 48),
                               Res.LitTransparent(new Color(0.5f, 0.75f, 1f, 0.22f)), ringT);
            foreach (var s in pet.Spheres)
            {
                var sp = MeshObj("HotSphere", MeshGen.Ellipsoid(Vector3.zero, Vector3.one * s.r * VisScale, Quaternion.identity, 12, 16),
                                 Res.Emissive(new Color(1f, 0.4f, 0.2f), new Color(2.5f, 0.8f, 0.3f)), ringT);
                sp.transform.localPosition = new Vector3(s.c.x, s.c.y, 0) * VisScale;
            }
            var lung = MeshObj("LungInsert", MeshGen.Cylinder(Vector3.zero, new Vector3(pet.LungR * VisScale, pet.LungR * VisScale, 0.09f), Quaternion.identity, 32),
                               Res.Lit(new Color(0.15f, 0.15f, 0.2f), 0.3f), ringT);
            lors = new LineBatch();
            MeshObj("LORs", lors.Mesh, Res.Additive(Color.white, 1.6f), ringT);
            sinoTex = UIKit.NewTex(PetSim.NRad, PetSim.NAng);
            imgTex = UIKit.NewTex(PetSim.ImgN, PetSim.ImgN);
            var black = new Color32[PetSim.ImgN * PetSim.ImgN];
            for (int i = 0; i < black.Length; i++) black[i] = new Color32(0, 0, 0, 255);
            imgTex.SetPixels32(black); imgTex.Apply();
            App.Rig.Frame(isoT.position + new Vector3(0, 0.05f, 0), 2.6f, -30f, 10f, true);
            App.Rig.MinDistance = 0.5f; App.Rig.MaxDistance = 6f;
            pet.Start();

            var Lp = LeftScroll.contentContainer;
            var sec = UIKit.Section(Lp, T("Radyofarmasötik", "Radiopharmaceutical"));
            UIKit.Seg(sec, T("İzotop", "Isotope"), new[] { "F-18 (0.6 mm)", "Ga-68 (2.9 mm)" }, petPositronGa ? 1 : 0, i => { petPositronGa = i == 1; App.Open("nucmed"); });
            UIKit.Slider(sec, T("Aktivite", "Activity"), 20, 600, petActivity, v => F(v, 0) + " MBq", v => { petActivity = v; pet.ActivityMBq = v; }, 10);
            UIKit.Slider(sec, T("Küre / arka plan oranı", "Sphere / background ratio"), 1, 10, petContrast, v => F(v, 1) + ":1", v => { petContrast = v; pet.Contrast = v; }, 0.5f);
            sec = UIKit.Section(Lp, T("Fizik etkileri", "Physical effects"));
            UIKit.Toggle(sec, T("Compton saçılması", "Compton scatter"), petScatter, v => { petScatter = v; pet.Scatter = v; });
            UIKit.Toggle(sec, T("Rastgele (random) koinsidanslar", "Random coincidences"), petRandoms, v => { petRandoms = v; pet.Randoms = v; });
            sec = UIKit.Section(Lp, T("Rekonstrüksiyon", "Reconstruction"));
            UIKit.Seg(sec, T("Algoritma", "Algorithm"), new[] { "FBP (Hann)", "MLEM" }, petMlem ? 1 : 0, i => petMlem = i == 1);
            UIKit.Slider(sec, T("MLEM iterasyon", "MLEM iterations"), 1, 60, petIter, v => F(v, 0), v => petIter = Mathf.RoundToInt(v), 1);
            UIKit.Toggle(sec, T("Zayıflama düzeltmesi (AC)", "Attenuation correction (AC)"), petAc, v => petAc = v);
            var row = UIKit.ButtonRow(sec);
            UIKit.Button(row, T("Rekonstrükte et", "Reconstruct"), Reconstruct, "mp-btn-primary");
            UIKit.Button(row, T("Durdur / Devam", "Pause / Run"), () => { if (pet.Running) pet.Stop(); else pet.Start(); });
            UIKit.Button(row, T("Sıfırla", "Reset"), () => pet.Clear());
            vRecStat = UIKit.Text(sec, "");

            var R = RightScroll.contentContainer;
            sec = UIKit.Section(R, T("Sayımlar", "Counts"));
            vTrue = UIKit.KV(sec, T("Gerçek (true)", "Trues")); vScat = UIKit.KV(sec, T("Saçılmış", "Scattered")); vRand = UIKit.KV(sec, T("Rastgele", "Randoms"));
            vNecr = UIKit.KV(sec, "NEC = T² / (T + S + R)");
            sec = UIKit.Section(R, T("Sinogram", "Sinogram"));
            sinoImg = new ImageView(170, T("yatay: radyal s • dikey: açı φ", "horizontal: radial s • vertical: angle φ")); sinoImg.Set(sinoTex); sec.Add(sinoImg);
            sec = UIKit.Section(R, T("Rekonstrükte görüntü", "Reconstructed image"));
            recImg = new ImageView(App.SmallScreen ? 260 : 330, ""); recImg.Set(imgTex); sec.Add(recImg);
            sec.Add(UIKit.ColorBar(ColorMaps.Hot, T("Bağıl aktivite konsantrasyonu (görüntü maksimumuna göre)", "Relative activity concentration (to the image maximum)"),
                (0f, "0"), (0.25f, "0.25"), (0.5f, "0.5"), (0.75f, "0.75"), (1f, "1")));
            vRc = UIKit.KV(sec, T("Kurtarma katsayısı (en büyük / en küçük küre)", "Recovery coefficient (largest / smallest sphere)"));
            UIKit.Note(sec, T("Küçük kürelerde kısmi hacim etkisi nedeniyle ölçülen aktivite düşüktür (RC < 1).", "Small spheres show lower measured activity due to the partial-volume effect (RC < 1)."));
            SetTip("Her çizgi bir koinsidans (LOR): yeşil = gerçek, sarı = saçılmış, kırmızı = rastgele. Yeterli sayım topladıktan sonra 'Rekonstrükte et'.",
                   "Each line is a coincidence (LOR): green = true, yellow = scattered, red = random. Collect enough counts, then 'Reconstruct'.");
        }

        void BuildRingMesh()
        {
            // crystals as small inward-facing quads on a ring (vertex colours = glow)
            int n = PetSim.NCrystal;
            var v = new List<Vector3>(); var tri = new List<int>(); var c = new List<Color>();
            float r = 0.45f, w = 2 * Mathf.PI * r / n * 0.42f, h = 0.08f;
            for (int i = 0; i < n; i++)
            {
                float a = (i + 0.5f) * 2 * Mathf.PI / n;
                var p = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0) * r;
                var t = new Vector3(-Mathf.Sin(a), Mathf.Cos(a), 0) * w;
                int s = v.Count;
                v.Add(p - t + Vector3.forward * -h); v.Add(p + t + Vector3.forward * -h); v.Add(p + t + Vector3.forward * h); v.Add(p - t + Vector3.forward * h);
                for (int k = 0; k < 4; k++) c.Add(new Color(0.1f, 0.4f, 0.25f, 0.5f));
                tri.Add(s); tri.Add(s + 1); tri.Add(s + 2); tri.Add(s); tri.Add(s + 2); tri.Add(s + 3);
            }
            glowMesh = new Mesh();
            glowMesh.SetVertices(v); glowMesh.SetColors(c); glowMesh.SetTriangles(tri, 0); glowMesh.RecalculateBounds();
            glowMesh.MarkDynamic();
            glowCols = c.ToArray();
            crystalHeat = new float[n];
            MeshObj("Crystals", glowMesh, Res.Additive(Color.white, 2f), ringT);
            var blocks = MeshObj("DetectorRingBack", MeshGen.Cylinder(Vector3.zero, new Vector3(0.5f, 0.5f, 0.09f), Quaternion.identity, 96, 0.92f), Res.Lit(new Color(0.12f, 0.14f, 0.18f), 0.6f), ringT);
        }

        void TickPet(float dt)
        {
            int n = 0;
            while (n < 400 && pet.Lors.TryDequeue(out var l))
            {
                var col = l.type == 0 ? new Color(0.3f, 1f, 0.5f, 0.6f) : l.type == 1 ? new Color(1f, 0.85f, 0.2f, 0.6f) : new Color(1f, 0.25f, 0.25f, 0.6f);
                lors.Add(new Vector3(l.a.x, l.a.y, 0) * VisScale * (0.45f / 0.45f), new Vector3(l.b.x, l.b.y, 0) * VisScale, col);
                n++;
            }
            while (pet.Hits.TryDequeue(out int c)) crystalHeat[c] = 1f;
            lors.Fade(Mathf.Pow(0.01f, dt));
            lors.Apply();
            for (int i = 0; i < crystalHeat.Length; i++)
            {
                crystalHeat[i] = Mathf.Max(0, crystalHeat[i] - dt * 2.5f);
                var col = Color.Lerp(new Color(0.1f, 0.4f, 0.25f, 0.4f), new Color(0.6f, 1f, 0.7f, 1f), crystalHeat[i]);
                for (int k = 0; k < 4; k++) glowCols[i * 4 + k] = col;
            }
            glowMesh.SetColors(glowCols);

            sinoT -= dt;
            if (sinoT <= 0)
            {
                sinoT = 0.5f;
                vTrue.text = pet.Trues.ToString("N0"); vScat.text = pet.Scatters.ToString("N0"); vRand.text = pet.RandomsCount.ToString("N0");
                double T = pet.Trues, S = pet.Scatters, Rr = pet.RandomsCount;
                vNecr.text = (T * T / Math.Max(1, T + S + Rr)).ToString("N0");
                var s = pet.SnapshotSino();
                int mx = 1; foreach (var v in s) mx = Math.Max(mx, v);
                var px = new Color32[s.Length];
                for (int i = 0; i < s.Length; i++) { var c = ColorMaps.Hot(Mathf.Pow(s[i] / (float)mx, 0.7f)); px[i] = c; }
                sinoTex.SetPixels32(px); sinoTex.Apply();
            }
            if (autoReconAt > 0) { autoReconAt -= dt; if (autoReconAt <= 0) { autoReconAt = -1; Reconstruct(); } }
            if (recTask != null && recTask.IsCompleted)
            {
                var img = recTask.Result; recTask = null;
                ShowImage(img);
            }
        }

        void Reconstruct()
        {
            if (recTask != null) return;
            var s = pet.SnapshotSino();
            long total = pet.Trues + pet.Scatters + pet.RandomsCount;
            bool mlem = petMlem; int it = petIter; recAc = petAc;
            vRecStat.text = T("Rekonstrüksiyon…", "Reconstructing…");
            recTask = Task.Run(() => mlem ? pet.Mlem(s, it, recAc) : pet.Fbp(s, recAc));
            if (mlem && total >= 500000) CompleteTask("pet");
        }

        void ShowImage(float[] img)
        {
            int N = PetSim.ImgN;
            float mx = 1e-9f; foreach (var v in img) mx = Mathf.Max(mx, v);
            var px = new Color32[N * N];
            for (int i = 0; i < img.Length; i++) px[i] = ColorMaps.Hot(img[i] / mx);
            imgTex.SetPixels32(px); imgTex.Apply();
            // recovery coefficients: mean in sphere / background mean / true ratio
            float half = PetSim.Fov / 2, pxs = PetSim.Fov / N;
            float bg = 0; int nb = 0; float center = 0; int nc = 0;
            var sums = new float[pet.Spheres.Length]; var cnts = new int[pet.Spheres.Length];
            for (int iy = 0; iy < N; iy++)
                for (int ix = 0; ix < N; ix++)
                {
                    var p = new Vector2(-half + (ix + 0.5f) * pxs, -half + (iy + 0.5f) * pxs);
                    float v = img[iy * N + ix];
                    bool inSphere = false;
                    for (int k = 0; k < pet.Spheres.Length; k++)
                        if ((p - pet.Spheres[k].c).magnitude < pet.Spheres[k].r * 0.8f) { sums[k] += v; cnts[k]++; inSphere = true; }
                    float rr = p.magnitude;
                    if (!inSphere && rr > 3.5f && rr < 9f && (p - pet.Spheres[0].c).magnitude > 3) { bg += v; nb++; }
                    if (rr > 3f && rr < 4.2f) { center += v; nc++; }
                }
            bg /= Mathf.Max(1, nb);
            float rcBig = cnts[5] > 0 ? (sums[5] / cnts[5] / bg - 1) / (pet.Contrast - 1) : 0;
            float rcSmall = cnts[0] > 0 ? (sums[0] / cnts[0] / bg - 1) / (pet.Contrast - 1) : 0;
            vRc.text = F(rcBig, 2) + " / " + F(rcSmall, 2);
            // centre/edge ratio (AC check)
            float inner = center / Mathf.Max(1, nc) / Mathf.Max(bg, 1e-9f);
            if (recAc) centerAc = inner; else centerNoAc = inner;
            if (centerAc > 0 && centerNoAc > 0) CompleteTask("ac");
            vRecStat.text = T("Tamamlandı. Merkez/arka plan oranı: ", "Done. Centre/background ratio: ") + F(inner, 2) + (recAc ? " (AC)" : T(" (AC yok)", " (no AC)"));
        }

        public override void AutoAction(string action)
        {
            if (action == "pet") { tab = 2; App.Open("nucmed"); }
            if (action == "gen") { tab = 1; App.Open("nucmed"); }
            if (action == "decay") { tab = 0; App.Open("nucmed"); }
            if (action == "recon") Reconstruct();
            if (action == "petrecon") { tab = 2; autoReconAt = 6f; App.Open("nucmed"); }
        }

        // ====================================================================== theory
        protected override void BuildLearn(VisualElement c)
        {
            UIKit.H(c, T("1. Radyoaktif bozunma", "1. Radioactive decay"));
            UIKit.Eq(c, "dN/dt = −λN    N(t) = N₀·e^(−λt)    A = λN    T½ = ln2/λ    τ = 1/λ");
            UIKit.P(c, T("Bozunma rastgeledir: tek bir çekirdeğin ne zaman bozunacağı bilinemez, ama çok sayıda çekirdeğin davranışı üstel yasaya uyar. Aktivite birimi Becquerel (1 Bq = 1 bozunma/s), eski birim Curie (1 Ci = 3.7·10¹⁰ Bq).",
                             "Decay is random: one cannot predict when a single nucleus decays, but many nuclei follow the exponential law. Activity is measured in becquerel (1 Bq = 1 decay/s); the old unit is the curie (1 Ci = 3.7·10¹⁰ Bq)."));
            UIKit.Eq(c, TEq("1/T_eff = 1/T_fiziksel + 1/T_biyolojik", "1/T_eff = 1/T_physical + 1/T_biological"));
            UIKit.H(c, T("2. Jeneratör ve geçici denge", "2. Generator and transient equilibrium"));
            UIKit.Eq(c, "A₂(t) = BR · A₁(0) · λ₂/(λ₂ − λ₁) · (e^(−λ₁t) − e^(−λ₂t))");
            UIKit.P(c, T("Ana çekirdek (Mo-99) kız çekirdekten (Tc-99m) daha uzun ömürlü olduğunda zamanla A₂/A₁ sabit bir değere ulaşır: geçici denge. Ana çekirdek çok uzun ömürlüyse (ör. Sr-90/Y-90) seküler denge oluşur: A₂ = A₁.",
                             "When the parent (Mo-99) lives longer than the daughter (Tc-99m), A₂/A₁ tends to a constant: transient equilibrium. If the parent is much longer-lived (e.g. Sr-90/Y-90) secular equilibrium arises: A₂ = A₁."));
            UIKit.H(c, T("3. PET", "3. PET"));
            UIKit.P(c, T("β⁺ bozunmasında çıkan pozitron birkaç mm içinde bir elektronla birleşir (pozitron menzili → çözünürlük kaybı) ve neredeyse zıt yönlü iki adet 511 keV foton oluşur (±0.25° sapma). Halka dedektördeki iki kristal zaman penceresi içinde (~4 ns) sinyal verirse bir koinsidans ve bir LOR (yanıt hattı) kaydedilir.",
                             "A positron from β⁺ decay annihilates with an electron within a few mm (positron range → resolution loss), producing two 511 keV photons almost back-to-back (±0.25°). If two crystals of the ring fire within the timing window (~4 ns), a coincidence and a line of response (LOR) are recorded."));
            UIKit.P(c, T("• <b>Zayıflama:</b> iki fotonun birlikte kaçma olasılığı LOR boyunca exp(−∫μ dl)'dir — yayılma noktasından bağımsız! Bu yüzden PET'te zayıflama düzeltmesi CT ile doğrudan yapılır.\n• <b>Saçılma:</b> Compton saçılmış fotonlar yanlış LOR oluşturur; enerji penceresi (425–650 keV) bir kısmını eler.\n• <b>Rastgele:</b> iki ilgisiz foton aynı pencereye düşer; oran ~ aktivite².\n• <b>NEC</b> = T²/(T+S+R): görüntü kalitesinin ölçüsü.",
                             "• <b>Attenuation:</b> the probability that both photons escape is exp(−∫μ dl) along the LOR — independent of the emission point! That is why PET attenuation correction is done directly from CT.\n• <b>Scatter:</b> Compton-scattered photons create wrong LORs; the energy window (425–650 keV) rejects some.\n• <b>Randoms:</b> two unrelated photons fall into the same window; rate ~ activity².\n• <b>NEC</b> = T²/(T+S+R): a measure of image quality."));
            UIKit.Eq(c, "MLEM:  λⱼ^(k+1) = λⱼ^k / Σᵢ aᵢⱼ · Σᵢ aᵢⱼ · yᵢ / Σₗ aᵢₗ λₗ^k");
            UIKit.P(c, T("MLEM, Poisson istatistiğini doğrudan modelleyen iteratif bir yöntemdir; FBP'den daha az çizgi artefaktı üretir ama çok iterasyonda gürültü artar.",
                             "MLEM is an iterative method that models Poisson statistics directly; it produces fewer streaks than FBP but noise grows with many iterations."));
        }
    }
}
