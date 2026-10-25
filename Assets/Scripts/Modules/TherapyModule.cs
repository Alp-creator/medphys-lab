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
    /// <summary>Radiotherapy: beam physics (Monte Carlo in water), treatment planning (DVH), radiobiology (LQ).</summary>
    public sealed class TherapyModule : ModuleBase
    {
        public override string Id => "therapy";
        public override string Title => T("Radyoterapi", "Radiotherapy");
        public override string Subtitle => tab == 0 ? T("Monte Carlo derin doz • Bragg piki", "Monte Carlo depth dose • Bragg peak")
                                         : tab == 1 ? T("Tedavi planlama • DVH", "Treatment planning • DVH")
                                         : T("Lineer-kuadratik model • BED", "Linear-quadratic model • BED");

        protected override (string id, string tr, string en)[] Tasks => new[]
        {
            ("mc6", "6 MV foton derin dozunda dmax ve PDD10'u ölç (≥ 50 000 parçacık)", "Measure dmax and PDD10 of 6 MV photons (≥ 50 000 histories)"),
            ("bragg", "150 MeV protonun Bragg pikini NIST CSDA menziliyle karşılaştır", "Compare the 150 MeV proton Bragg peak with the NIST CSDA range"),
            ("sobp", "5 cm genişliğinde bir SOBP oluştur", "Build a 5 cm wide SOBP"),
            ("electron", "Bir elektron demetinin R50 değerinin E/2.33 kuralına uyduğunu gör", "See that an electron beam's R50 follows the E/2.33 rule"),
            ("plan", "Tüm doz kısıtlarını sağlayan bir foton planı yap", "Make a photon plan that meets all dose constraints"),
            ("proton", "Proton planında omurilik dozunu foton planına göre azalt", "Reduce the cord dose with a proton plan"),
            ("bed", "SBRT (5×10 Gy) ve konvansiyonel (30×2 Gy) şemalarının tümör BED'ini karşılaştır", "Compare tumour BED of SBRT (5×10 Gy) and conventional (30×2 Gy)"),
        };

        static int tab;

        // shared scene
        Transform gantry, collimator, source, iso, couchTop;
        Quaternion gantry0;
        GameObject linac;

        protected override void Build()
        {
            Env.Room(Root.transform, new Color(0.35f, 0.65f, 1f));
            linac = Res.Model("Linac", Root.transform);
            gantry = linac.Find("Linac_Gantry"); collimator = linac.Find("Linac_Collimator");
            source = linac.Find("Linac_Source"); iso = linac.Find("Linac_Iso"); couchTop = linac.Find("Linac_CouchTop");
            gantry0 = gantry.rotation;

            var L = LeftScroll.contentContainer;
            var sec = UIKit.Section(L, T("Bölüm", "Section"));
            UIKit.Seg(sec, null, new[] { T("Işın fiziği", "Beam physics"), T("Tedavi planı", "Planning"), T("Radyobiyoloji", "Radiobiology") }, tab, i =>
            {
                tab = i; App.Open("therapy");
            });
            if (tab == 0) BuildBeamLab();
            else if (tab == 1) BuildPlanning();
            else BuildRadiobio();
            BuildTasks(L);
            MachineParts();
        }

        void MachineParts()
        {
            Part(linac.Find("Linac_Head"), "Tedavi kafası", "Treatment head",
                 "Dalga kılavuzunda MeV'e hızlanan elektronlar bükme mıknatısıyla hedefe çarptırılır (foton) ya da saçıcı folyodan geçirilir (elektron). Düzleştirici filtre ve monitör odaları (MU) da buradadır.",
                 "Electrons accelerated to MeV in the waveguide are bent onto a target (photons) or through a scattering foil (electrons). The flattening filter and the monitor chambers (MU) sit here too.", new Vector2(1f, 0.7f), 110);
            Part(collimator, "Çeneler + MLC", "Jaws + MLC",
                 "Çok yapraklı kolimatör (5–10 mm yapraklar) alanı tümörün şekline uydurur; IMRT ve VMAT'ta ışınlama sırasında hareket eder.",
                 "The multileaf collimator (5–10 mm leaves) shapes the field to the tumour and moves during the beam in IMRT and VMAT.", new Vector2(-1f, 0.4f), 120);
            Part(linac.Find("Linac_Drum"), "Gantri", "Gantry",
                 "Tedavi kafasını hastanın çevresinde 360° döndürür. RF güç kaynağı (magnetron/klistron) ve dalga kılavuzu gantri ile standın içindedir.",
                 "Rotates the head 360° around the patient. The RF source (magnetron/klystron) and the waveguide are inside the gantry and its stand.", new Vector2(-1f, 0.9f), 120);
            Part(linac.Find("Linac_EPID"), "EPID (MV görüntüleme)", "EPID (MV imaging)",
                 "Tedavi ışınının hastadan çıkan kısmıyla portal görüntü alır; alan şekli ve doz doğrulamasında kullanılır.",
                 "Images the exit treatment beam (portal image); used to verify the field shape and the delivered dose.", new Vector2(1f, -0.6f), 110);
            Part(linac.Find("Linac_kVSource"), "kV X-ışını kaynağı", "kV X-ray source",
                 "IGRT: tedaviden önce kV görüntü ya da konik ışınlı BT (CBCT) çekilir ve hasta konumu milimetre düzeyinde düzeltilir.",
                 "IGRT: before treatment a kV image or cone-beam CT (CBCT) is taken and the patient position is corrected to the millimetre.", new Vector2(1f, 0.3f), 100);
            Part(linac.Find("Linac_kVPanel"), "kV dedektörü", "kV detector",
                 "kV kaynağının karşısındaki düz panel; CBCT için gantri dönerken yüzlerce projeksiyon toplar.",
                 "The flat panel opposite the kV source; it collects hundreds of projections for CBCT while the gantry rotates.", new Vector2(-1f, -0.3f), 100);
            // source-to-axis (or, with the water surface at the isocentre, source-to-surface) distance beside the beam
            Dimension(() => source.position + Side() * 0.42f, () => iso.position + Side() * 0.42f,
                      () => (tab == 0 ? "SSD = " : "SAD = ") + F(Vector3.Distance(source.position, iso.position) * 100f, 0) + " cm");
            Part(iso, "İzomerkez", "Isocentre",
                 "Gantri, kolimatör ve masa dönme eksenlerinin kesiştiği nokta (±1 mm). Kaynak–izomerkez mesafesi (SAD) 100 cm'dir.",
                 "Where the gantry, collimator and couch rotation axes meet (±1 mm). The source-to-axis distance (SAD) is 100 cm.", new Vector2(-0.4f, -1f), 140);
        }

        public override void Dispose()
        {
            mc?.Stop();
            base.Dispose();
        }

        Vector3 Side() => Vector3.Cross((iso.position - source.position).normalized, Vector3.forward).normalized;

        void SetGantry(float angle)
        {
            gantry.rotation = Quaternion.AngleAxis(-angle, Vector3.forward) * gantry0;
        }

        public override void Tick(float dt)
        {
            base.Tick(dt);
            if (tab == 0) TickBeamLab(dt);
            else if (tab == 1) TickPlanning(dt);
            else TickRadiobio(dt);
        }

        // ====================================================================== 1. BEAM LAB (Monte Carlo)
        WaterMC mc;
        readonly BeamSpec beam = new BeamSpec();
        static int modality, photonIdx = 1, electronIdx = 1;
        static float protonE = 150f, fieldCm = 10f;
        static bool sobpOn; static float sobpDistal = 15f, sobpWidth = 5f;
        bool running = true, showTracks = true, showMap = true;
        Transform mcRoot;
        LineBatch tracks;
        MeshRenderer trackR, beamR, mapR;
        Material beamMat;
        Texture2D mapTex;
        Chart pddChart, specChart;
        Label vHist, vRate, vM1, vM2, vM3, vM4;
        ImageView mapImg;
        float refreshT;
        long lastHist; float rateT; double rate;
        static readonly float[] PhotonMV = { 1.25f, 6f, 10f, 18f };
        static readonly float[] ElectronMeV = { 6f, 9f, 12f, 16f, 20f };

        void BuildBeamLab()
        {
            foreach (var n in new[] { "Linac_CouchTop", "Linac_CouchBase", "Linac_Turntable" })
            { var t = linac.Find(n); if (t != null) t.gameObject.SetActive(false); }
            SetGantry(0);
            // water tank with its surface at the isocentre (SSD 100 cm)
            var tank = Res.Model("WaterTank", Root.transform, new Vector3(iso.position.x, 0.06f, iso.position.z));
            Part(tank.transform, "Su fantomu", "Water phantom",
                 "Referans dozimetri (IAEA TRS-398) ve PDD/profil ölçümleri burada yapılır: iyon odası suyun içinde taranır. Burada her iz Monte Carlo ile izlenen bir parçacıktır.",
                 "Reference dosimetry (IAEA TRS-398) and PDD/profile measurements: an ion chamber is scanned through the water. Here each track is a particle followed by Monte Carlo.", new Vector2(1f, -0.8f), 120);
            var water = GameObject.CreatePrimitive(PrimitiveType.Cube);
            UnityEngine.Object.Destroy(water.GetComponent<Collider>());
            water.transform.SetParent(Root.transform, false);
            float bottom = 0.06f + 0.712f, top = iso.position.y;
            water.transform.position = new Vector3(iso.position.x, (bottom + top) / 2, iso.position.z);
            water.transform.localScale = new Vector3(0.612f, top - bottom, 0.612f);
            water.GetComponent<Renderer>().sharedMaterial = Res.LitTransparent(new Color(0.15f, 0.45f, 0.9f, 0.18f), 0.95f);
            water.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            mcRoot = Child("MCFrame").transform;
            mcRoot.position = iso.position;
            mcRoot.rotation = Quaternion.Euler(90, 0, 0);
            mcRoot.localScale = Vector3.one * 0.01f;
            tracks = new LineBatch();
            trackR = MeshObj("Tracks", tracks.Mesh, Res.Additive(Color.white, 1.4f), mcRoot);
            // dose map plane (x–depth) in MC coordinates
            mapTex = UIKit.NewTex(80, 200);
            var qm = new Mesh();
            qm.vertices = new[] { new Vector3(-20, 0, 0), new Vector3(20, 0, 0), new Vector3(20, 0, 40), new Vector3(-20, 0, 40) };
            qm.uv = new[] { new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0), new Vector2(0, 0) };
            qm.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            qm.RecalculateNormals(); qm.RecalculateBounds();
            mapR = MeshObj("DoseMap", qm, Res.UnlitTex(mapTex, Color.white, true), mcRoot);
            beamMat = Res.Additive(new Color(1f, 0.9f, 0.4f, 0.25f), 1f, 0.4f);
            beamR = MeshObj("BeamCone", new Mesh(), beamMat, Root.transform);

            App.Rig.Frame(iso.position + new Vector3(0, -0.12f, 0), 2.5f, 32f, 8f, true);
            App.Rig.MinDistance = 0.5f; App.Rig.MaxDistance = 7f;
            App.Rig.PanBounds = new Bounds(iso.position, new Vector3(5, 3, 5));

            var Lp = LeftScroll.contentContainer;
            var sec = UIKit.Section(Lp, T("Işın", "Beam"));
            UIKit.Seg(sec, T("Parçacık", "Particle"), new[] { T("Foton", "Photon"), T("Elektron", "Electron"), T("Proton", "Proton") }, modality, i => { modality = i; App.Open("therapy"); });
            if (modality == 0)
                UIKit.Seg(sec, T("Enerji", "Energy"), new[] { "Co-60", "6 MV", "10 MV", "18 MV" }, photonIdx, i => { photonIdx = i; Restart(); });
            else if (modality == 1)
                UIKit.Seg(sec, T("Enerji", "Energy"), new[] { "6", "9", "12", "16", "20 MeV" }, electronIdx, i => { electronIdx = i; Restart(); });
            else
                UIKit.Slider(sec, T("Proton enerjisi", "Proton energy"), 70, 230, protonE, v => F(v, 0) + " MeV", v => { protonE = v; restartPending = 0.4f; }, 5);
            UIKit.Slider(sec, T("Alan boyutu (yüzeyde)", "Field size (at surface)"), 3, 20, fieldCm, v => F(v, 0) + " × " + F(v, 0) + " cm²", v => { fieldCm = v; restartPending = 0.4f; }, 1);
            var row = UIKit.ButtonRow(sec);
            UIKit.Button(row, T("Durdur / Devam", "Pause / Run"), () => { running = !running; if (running) mc.Start(); else mc.Stop(); });
            UIKit.Button(row, T("Sıfırla", "Reset"), Restart);
            UIKit.Toggle(sec, T("Parçacık izlerini göster", "Show particle tracks"), showTracks, v => { showTracks = v; trackR.enabled = v; });
            // the map colour is DoseJet(d^0.8), so a relative dose d sits at d^0.8 along the scale
            var legend = MapLegend(UIKit.ColorBar(ColorMaps.DoseJet,
                T("Doz haritası (x–derinlik): maksimuma göre bağıl doz, %", "Dose map (x–depth): dose relative to the maximum, %"),
                (0f, "0"), (Mathf.Pow(0.2f, 0.8f), "20"), (Mathf.Pow(0.5f, 0.8f), "50"), (Mathf.Pow(0.8f, 0.8f), "80"), (1f, "100")));
            legend.style.display = showMap ? DisplayStyle.Flex : DisplayStyle.None;
            UIKit.Toggle(sec, T("Doz haritasını göster (x–z)", "Show dose map (x–z)"), showMap, v =>
            {
                showMap = v; mapR.enabled = v;
                legend.style.display = v ? DisplayStyle.Flex : DisplayStyle.None;
            });
            UIKit.Note(sec, T("İz renkleri: sarı = foton, mavi = elektron, mor = pozitron, turuncu = proton, yeşil = nükleer etkileşim.",
                              "Track colours: yellow = photon, blue = electron, magenta = positron, orange = proton, green = nuclear interaction."));
            if (modality == 2)
            {
                sec = UIKit.Section(Lp, T("Yayılmış Bragg piki (SOBP)", "Spread-out Bragg peak (SOBP)"));
                UIKit.Toggle(sec, T("SOBP göster (analitik)", "Show SOBP (analytic)"), sobpOn, v => { sobpOn = v; RefreshAnalytic(); });
                UIKit.Slider(sec, T("Distal derinlik", "Distal depth"), 4, 30, sobpDistal, v => F(v, 1) + " cm", v => { sobpDistal = v; RefreshAnalytic(); }, 0.5f);
                UIKit.Slider(sec, T("Modülasyon genişliği", "Modulation width"), 1, 12, sobpWidth, v => F(v, 1) + " cm", v => { sobpWidth = v; RefreshAnalytic(); }, 0.5f);
            }
            sec = UIKit.Section(Lp, T("Simülasyon", "Simulation"));
            vHist = UIKit.KV(sec, T("Parçacık geçmişi", "Histories"));
            vRate = UIKit.KV(sec, T("Hız", "Rate"));

            var R = RightScroll.contentContainer;
            sec = UIKit.Section(R, T("Merkezi eksen derin doz", "Central-axis depth dose"));
            pddChart = new Chart(230) { XMin = 0, XMax = 30, XLabel = T("derinlik (cm)", "depth (cm)"), YLabel = "%", AutoY = false, YMin = 0, YMax = 110 };
            pddChart.Add("Monte Carlo", new Color(0.3f, 0.85f, 1f), 2.2f);
            var a = pddChart.Add(T("Referans / analitik", "Reference / analytic"), new Color(1f, 0.7f, 0.25f), 1.6f); a.Dashed = true;
            var sp = pddChart.Add("SOBP", new Color(0.4f, 1f, 0.5f), 2f);
            sec.Add(pddChart.Legend()); sec.Add(pddChart);
            vM1 = UIKit.KV(sec, ""); vM2 = UIKit.KV(sec, ""); vM3 = UIKit.KV(sec, ""); vM4 = UIKit.KV(sec, "");
            sec = UIKit.Section(R, T("Doz dağılımı (x–z düzlemi)", "Dose distribution (x–z plane)"));
            mapImg = new ImageView(200, T("yatay: x (−20…20 cm) • dikey: derinlik 0→40 cm", "horizontal: x (−20…20 cm) • vertical: depth 0→40 cm"));
            mapImg.Set(mapTex);
            sec.Add(mapImg);
            if (modality == 0)
            {
                sec = UIKit.Section(R, T("Foton spektrumu (hedef + düzleştirici filtre)", "Photon spectrum (target + flattening filter)"));
                specChart = new Chart(150) { XMin = 0, XMax = 20, XLabel = "MeV", YLabel = T("bağıl", "relative") };
                var s1 = specChart.Add("", new Color(1f, 0.85f, 0.3f), 1.8f); s1.Fill = true;
                sec.Add(specChart);
            }
            SetTip("Gerçek zamanlı Monte Carlo: her iz bir parçacıktır. Daha çok geçmiş = daha az istatistiksel gürültü (σ ~ 1/√N).",
                   "Real-time Monte Carlo: each track is a particle. More histories = less statistical noise (σ ~ 1/√N).");
            Restart();
        }

        float restartPending = -1;

        void Restart()
        {
            mc?.Stop();
            beam.Kind = (BeamKind)modality;
            beam.Energy = modality == 0 ? PhotonMV[photonIdx] : modality == 1 ? ElectronMeV[electronIdx] : protonE;
            beam.FieldCm = fieldCm;
            beam.SsdCm = (modality == 0 && photonIdx == 0) ? 80 : 100;
            mc = new WaterMC(beam);
            mc.TrackEvery = modality == 2 ? 6 : modality == 1 ? 10 : 25;
            tracks.Clear(); tracks.Apply();
            lastHist = 0;
            if (running) mc.Start();
            // beam cone from the source to the field at the surface
            Vector3 surf = iso.position;
            float h = beam.FieldCm * 0.005f;
            var corners = new[] { surf + new Vector3(-h, 0, -h), surf + new Vector3(h, 0, -h), surf + new Vector3(h, 0, h), surf + new Vector3(-h, 0, h) };
            Vector3 apex = modality == 2 ? surf + Vector3.up * 0.55f : source.position;
            beamR.GetComponent<MeshFilter>().sharedMesh = MeshGen.Beam(apex, corners, modality == 2 ? 0.5f : 0.25f, 0.9f);
            beamMat.SetColor("_Color", modality == 0 ? new Color(1f, 0.9f, 0.4f, 0.22f) : modality == 1 ? new Color(0.3f, 0.7f, 1f, 0.25f) : new Color(1f, 0.5f, 0.2f, 0.3f));
            pddChart.XMax = modality == 0 ? 30 : modality == 1 ? Mathf.Ceil(ElectronMeV[electronIdx] * 0.6f + 2) : Mathf.Ceil((float)StoppingPower.ProtonRange(protonE) + 4);
            if (specChart != null)
            {
                var bins = mc.SpectrumBins();
                var pts = new List<Vector2>();
                double mx = 0; foreach (var b in bins) mx = Math.Max(mx, b.w);
                foreach (var b in bins) pts.Add(new Vector2((float)b.e, (float)(b.w / Math.Max(mx, 1e-9))));
                specChart.AllSeries[0].Set(pts);
                specChart.XMax = Mathf.Max(2, beam.Energy < 2 ? 1.5f : beam.Energy);
                specChart.Refresh();
            }
            RefreshAnalytic();
        }

        void RefreshAnalytic()
        {
            if (pddChart == null) return;
            var ref1 = pddChart.AllSeries[1];
            var sob = pddChart.AllSeries[2];
            ref1.Pts.Clear(); sob.Pts.Clear();
            float xmax = pddChart.XMax;
            if (modality == 0)
            {
                for (float d = 0; d <= xmax; d += 0.25f) ref1.Pts.Add(new Vector2(d, (float)DepthDose.PhotonPdd(beam.Energy, d)));
                ref1.Visible = Mathf.Abs(fieldCm - 10) < 0.5f;
            }
            else if (modality == 1)
            {
                for (float d = 0; d <= xmax; d += 0.1f) ref1.Pts.Add(new Vector2(d, (float)DepthDose.ElectronPdd(beam.Energy, d)));
                ref1.Visible = true;
            }
            else
            {
                var b = DepthDose.GetBragg(protonE);
                double mx = b.Max();
                for (float d = 0; d <= xmax; d += 0.05f) ref1.Pts.Add(new Vector2(d, (float)(100 * b.At(d) / mx)));
                ref1.Visible = true;
                sob.Visible = sobpOn;
                if (sobpOn)
                {
                    var s = DepthDose.BuildSobp(sobpDistal - sobpWidth, sobpDistal);
                    pddChart.XMax = Mathf.Max(pddChart.XMax, sobpDistal + 4);
                    for (float d = 0; d <= pddChart.XMax; d += 0.05f) sob.Pts.Add(new Vector2(d, (float)(100 * s.At(d))));
                    if (sobpWidth >= 4.9f && sobpWidth <= 5.1f) CompleteTask("sobp");
                }
            }
            pddChart.Refresh();
        }

        void TickBeamLab(float dt)
        {
            if (restartPending > 0) { restartPending -= dt; if (restartPending <= 0) { restartPending = -1; Restart(); } }
            if (mc == null) return;
            // drain tracks
            int n = 0;
            while (n < 2500 && mc.Tracks.TryDequeue(out var t))
            {
                Color c = t.type == 0 ? new Color(1f, 0.85f, 0.25f, 0.55f) : t.type == 1 ? new Color(0.25f, 0.7f, 1f, 0.8f)
                        : t.type == 2 ? new Color(1f, 0.3f, 0.9f, 0.9f) : t.type == 3 ? new Color(1f, 0.45f, 0.15f, 0.9f) : new Color(0.3f, 1f, 0.4f, 0.9f);
                tracks.Add(t.a, t.b, c);
                n++;
            }
            tracks.Fade(Mathf.Pow(0.25f, dt));
            if (tracks.Count > 30000) tracks.Fade(0.5f);
            tracks.Apply();

            refreshT -= dt;
            rateT += dt;
            if (refreshT > 0) return;
            refreshT = 0.35f;
            long h = mc.TotalHistories();
            if (rateT > 1f) { rate = (h - lastHist) / rateT; lastHist = h; rateT = 0; }
            vHist.text = h.ToString("N0");
            vRate.text = rate.ToString("N0") + " /s";
            var cax = mc.MergedCax();
            double mx = 0; int imx = 0;
            for (int i = 0; i < cax.Length; i++) if (cax[i] > mx) { mx = cax[i]; imx = i; }
            if (mx <= 0) return;
            // light smoothing (3-bin) for display
            var s = pddChart.AllSeries[0];
            s.Pts.Clear();
            for (int i = 0; i < cax.Length; i++)
            {
                double v = cax[i];
                if (modality != 2 && i > 0 && i < cax.Length - 1) v = 0.25 * cax[i - 1] + 0.5 * cax[i] + 0.25 * cax[i + 1];
                s.Pts.Add(new Vector2((i + 0.5f) * mc.Dz, (float)(100 * v / mx)));
            }
            pddChart.Refresh();
            Metrics(cax, mx, imx, h);
            UpdateMap();
        }

        void Metrics(double[] cax, double mx, int imx, long hist)
        {
            float dz = mc.Dz;
            double Pdd(double d) { int i = Mathf.Clamp((int)(d / dz), 1, cax.Length - 2); return 100 * (0.25 * cax[i - 1] + 0.5 * cax[i] + 0.25 * cax[i + 1]) / mx; }
            double Rx(double f)
            {
                for (int i = imx; i < cax.Length - 1; i++)
                    if (cax[i] >= f * mx && cax[i + 1] < f * mx)
                        return (i + 0.5 + (cax[i] - f * mx) / Math.Max(cax[i] - cax[i + 1], 1e-12)) * dz;
                return 0;
            }
            float dmax = (imx + 0.5f) * dz;
            if (modality == 0)
            {
                vM1.parent.Q<Label>(className: "mp-kv-label").text = "dmax (MC / ref)";
                vM1.text = F(dmax, 2) + " / " + F(DepthDose.PhotonDmax(beam.Energy), 1) + " cm";
                vM2.parent.Q<Label>(className: "mp-kv-label").text = "PDD(10 cm) (MC / ref)";
                vM2.text = F(Pdd(10), 1) + " / " + F(DepthDose.PhotonPdd(beam.Energy, 10), 1) + " %";
                vM3.parent.Q<Label>(className: "mp-kv-label").text = "PDD(20 cm)";
                vM3.text = F(Pdd(20), 1) + " / " + F(DepthDose.PhotonPdd(beam.Energy, 20), 1) + " %";
                vM4.parent.Q<Label>(className: "mp-kv-label").text = T("Ortalama foton enerjisi", "Mean photon energy");
                vM4.text = F(mc.MeanSpectrumEnergy(), 2) + " MeV";
                if (Mathf.Abs(beam.Energy - 6) < 0.1f && hist > 50000) CompleteTask("mc6");
            }
            else if (modality == 1)
            {
                double r50 = Rx(0.5), rp = Rx(0.1);
                vM1.parent.Q<Label>(className: "mp-kv-label").text = "R50 (MC / E₀/2.33)";
                vM1.text = F(r50, 2) + " / " + F(DepthDose.ElectronR50(beam.Energy), 2) + " cm";
                vM2.parent.Q<Label>(className: "mp-kv-label").text = T("R80 (≈ terapötik derinlik)", "R80 (≈ therapeutic depth)");
                vM2.text = F(Rx(0.8), 2) + " cm";
                vM3.parent.Q<Label>(className: "mp-kv-label").text = T("Pratik menzil Rp (ref)", "Practical range Rp (ref)");
                vM3.text = "≈ " + F(DepthDose.ElectronRp(beam.Energy), 2) + " cm";
                vM4.parent.Q<Label>(className: "mp-kv-label").text = T("Yüzey dozu", "Surface dose");
                vM4.text = F(Pdd(0.15), 1) + " %";
                if (hist > 3000 && r50 > 0 && Math.Abs(r50 - DepthDose.ElectronR50(beam.Energy)) / DepthDose.ElectronR50(beam.Energy) < 0.12) CompleteTask("electron");
            }
            else
            {
                double r80 = Rx(0.8);
                double csda = StoppingPower.ProtonRange(protonE);
                double pstar = Pstar(protonE);
                vM1.parent.Q<Label>(className: "mp-kv-label").text = T("Bragg piki derinliği", "Bragg peak depth");
                vM1.text = F(dmax, 2) + " cm";
                vM2.parent.Q<Label>(className: "mp-kv-label").text = T("R80 distal (MC)", "R80 distal (MC)");
                vM2.text = F(r80, 2) + " cm";
                vM3.parent.Q<Label>(className: "mp-kv-label").text = T("CSDA menzili (Bethe / NIST)", "CSDA range (Bethe / NIST)");
                vM3.text = F(csda, 2) + " / " + (pstar > 0 ? F(pstar, 2) : "–") + " cm";
                vM4.parent.Q<Label>(className: "mp-kv-label").text = T("Giriş / pik oranı", "Entrance / peak ratio");
                vM4.text = F(Pdd(1.0), 0) + " %";
                if (Mathf.Abs(protonE - 150) < 0.1f && hist > 3000 && Math.Abs(r80 - 15.77) < 0.5) CompleteTask("bragg");
            }
        }

        static double Pstar(double e)
        {
            var t = StoppingPower.PstarRanges;
            for (int i = 0; i < t.Length - 1; i++)
                if (e >= t[i].T && e <= t[i + 1].T)
                {
                    double f = Math.Log(e / t[i].T) / Math.Log(t[i + 1].T / t[i].T);
                    return Math.Exp(Math.Log(t[i].R) + f * Math.Log(t[i + 1].R / t[i].R));
                }
            return 0;
        }

        void UpdateMap()
        {
            var map = mc.MergedMap();
            int nx = mc.NX, nz = mc.NZ;
            double mx = 0; foreach (var v in map) mx = Math.Max(mx, v);
            if (mx <= 0) return;
            if (mapTex.width != nx || mapTex.height != nz)
            {
                UnityEngine.Object.Destroy(mapTex);
                mapTex = UIKit.NewTex(nx, nz);
                mapR.sharedMaterial.SetTexture("_MainTex", mapTex);
                mapImg.Set(mapTex);
            }
            var px = new Color32[nx * nz];
            for (int iz = 0; iz < nz; iz++)
                for (int ix = 0; ix < nx; ix++)
                {
                    float t = (float)(map[iz * nx + ix] / mx);
                    var c = ColorMaps.DoseJet(Mathf.Pow(t, 0.8f));
                    c.a = Mathf.Clamp01(t * 3f) * 0.85f;
                    px[(nz - 1 - iz) * nx + ix] = c;
                }
            mapTex.SetPixels32(px); mapTex.Apply();
        }

        // ====================================================================== 2. PLANNING
        Phantom planPh;
        PhantomView planView;
        PlanGrid grid;
        static readonly List<PlanBeam> beams = new List<PlanBeam>();
        static int selBeam;
        static PlanModality planMod = PlanModality.Photon6;
        Task planTask;
        bool planDirty = true;
        float planDelay;
        Texture2D planTex, washTex;
        ImageView planImg;
        Chart dvh;
        VisualElement beamList, constraintsBox;
        Label vSel;
        UIKit.SliderRow sAng, sW, sWid;
        readonly List<MeshRenderer> beamShafts = new List<MeshRenderer>();
        MeshRenderer washR;
        float gantryShown;
        static float cordPhotonBest = -1;

        void BuildPlanning()
        {
            planPh = Phantom.BuildAdult(new PhantomOptions { Tumor = true, TumorRadius = 2.0f });
            var tumor = planPh.Options.TumorCenter;
            planView = PhantomView.Create(planPh, Root.transform);
            planView.Orient(Vector3.zero, Vector3.forward, Vector3.up);
            planView.transform.position += iso.position - planView.PhysToWorld(tumor);
            // couch top under the patient's back
            var back = planView.PhysToWorld(new Vector3(0, -10.6f, tumor.z));
            couchTop.position = new Vector3(couchTop.position.x, back.y, couchTop.position.z);
            grid = PlanGrid.Build(planPh, tumor.z, tumor, 2.0f, 0.8f);
            if (beams.Count == 0) Preset(2);

            // dose colour-wash in the slice plane
            washTex = UIKit.NewTex(PlanGrid.N, PlanGrid.N);
            planTex = UIKit.NewTex(PlanGrid.N, PlanGrid.N);
            washR = MeshObj("DoseWash", MeshGen.Quad(PlanGrid.Size * 0.01f, PlanGrid.Size * 0.01f), Res.UnlitTex(washTex, Color.white, true));
            washR.transform.position = planView.PhysToWorld(new Vector3(0, 0, tumor.z));
            washR.transform.rotation = Quaternion.identity;

            App.Rig.Frame(iso.position + new Vector3(0, 0.05f, -0.1f), 3.0f, 38f, 24f, true);
            App.Rig.MinDistance = 0.8f; App.Rig.MaxDistance = 8f;
            App.Rig.PanBounds = new Bounds(iso.position, new Vector3(5, 3, 5));

            var Lp = LeftScroll.contentContainer;
            var sec = UIKit.Section(Lp, T("Hazır planlar", "Plan presets"));
            UIKit.Seg(sec, null, new[] { "AP", "AP-PA", T("3 alan", "3-field"), T("5 alan", "5-field"), T("Ark (VMAT benzeri)", "Arc (VMAT-like)") }, -1, i => { Preset(i); RebuildBeamList(); planDirty = true; });
            UIKit.Seg(sec, T("Tüm ışınların modalitesi", "Modality of all beams"), new[] { "6 MV", "18 MV", T("Proton", "Proton") }, (int)planMod, i =>
            {
                planMod = (PlanModality)i; foreach (var b in beams) b.Modality = planMod; planDirty = true;
            });
            sec = UIKit.Section(Lp, T("Işınlar", "Beams"));
            beamList = new VisualElement(); beamList.style.flexDirection = FlexDirection.Row; beamList.style.flexWrap = Wrap.Wrap;
            sec.Add(beamList);
            var row = UIKit.ButtonRow(sec);
            UIKit.Button(row, T("+ Işın ekle", "+ Add beam"), () => { beams.Add(new PlanBeam { Angle = 0, Modality = planMod }); selBeam = beams.Count - 1; RebuildBeamList(); planDirty = true; });
            UIKit.Button(row, T("− Sil", "− Remove"), () => { if (beams.Count > 1) { beams.RemoveAt(selBeam); selBeam = Mathf.Clamp(selBeam, 0, beams.Count - 1); RebuildBeamList(); planDirty = true; } });
            vSel = UIKit.Text(sec, "");
            sAng = UIKit.Slider(sec, T("Gantri açısı", "Gantry angle"), 0, 359, 0, v => F(v, 0) + "°", v => { if (Sel != null) { Sel.Angle = v; planDirty = true; RebuildBeamList(); } }, 1);
            sW = UIKit.Slider(sec, T("Ağırlık", "Weight"), 0, 2, 1, v => F(v, 2), v => { if (Sel != null) { Sel.Weight = v; planDirty = true; } }, 0.05f);
            sWid = UIKit.Slider(sec, T("Alan genişliği (izomerkez)", "Field width (isocentre)"), 2, 15, 6, v => F(v, 1) + " cm", v => { if (Sel != null) { Sel.WidthCm = v; planDirty = true; } }, 0.5f);
            UIKit.Note(sec, T("Açı: 0° = ön (AP), 90° = hastanın solu, 180° = arka (PA), 270° = sağ.", "Angle: 0° = anterior (AP), 90° = patient's left, 180° = posterior (PA), 270° = right."));
            sec = UIKit.Section(Lp, T("Reçete", "Prescription"));
            UIKit.Text(sec, T("PTV (GTV + 8 mm) ortalaması = 60 Gy / 30 fraksiyon (2 Gy)", "PTV (GTV + 8 mm) mean = 60 Gy / 30 fractions (2 Gy)"));

            var R = RightScroll.contentContainer;
            sec = UIKit.Section(R, T("Aksiyel doz dağılımı", "Axial dose distribution"));
            planImg = new ImageView(App.SmallScreen ? 300 : 380, "");
            planImg.Set(planTex);
            sec.Add(planImg);
            UIKit.Note(sec, T("Renk: 107 %·95 %·80 %·50 %·30 % izodoz. Kontur: yeşil PTV, sarı omurilik, kırmızı kalp, pembe akciğer.",
                              "Colour: 107%·95%·80%·50%·30% isodose. Contours: green PTV, yellow cord, red heart, pink lungs."));
            sec = UIKit.Section(R, T("Doz-hacim histogramı (DVH)", "Dose-volume histogram (DVH)"));
            dvh = new Chart(220) { XMin = 0, XMax = 70, XLabel = "Gy", YLabel = T("hacim %", "volume %"), AutoY = false, YMin = 0, YMax = 105 };
            dvh.Add("PTV", new Color(0.3f, 1f, 0.4f), 2.2f);
            dvh.Add(T("Omurilik", "Cord"), new Color(1f, 0.85f, 0.2f), 2f);
            dvh.Add(T("Kalp", "Heart"), new Color(1f, 0.3f, 0.3f), 2f);
            dvh.Add(T("Sol akciğer", "Left lung"), new Color(1f, 0.55f, 0.75f), 2f);
            dvh.Add(T("Sağ akciğer", "Right lung"), new Color(0.7f, 0.5f, 1f), 2f);
            sec.Add(dvh.Legend()); sec.Add(dvh);
            sec = UIKit.Section(R, T("Doz kısıtları (QUANTEC / RTOG)", "Dose constraints (QUANTEC / RTOG)"));
            constraintsBox = new VisualElement(); sec.Add(constraintsBox);
            RebuildBeamList();
            SetTip("Işın açılarını ve ağırlıklarını değiştir: hedefi kapsarken omurilik, kalp ve akciğerleri koru.",
                   "Change beam angles and weights: cover the target while sparing cord, heart and lungs.");
        }

        PlanBeam Sel => selBeam >= 0 && selBeam < beams.Count ? beams[selBeam] : null;

        void Preset(int i)
        {
            beams.Clear();
            float[] angles = i == 0 ? new[] { 0f } : i == 1 ? new[] { 0f, 180f } : i == 2 ? new[] { 0f, 90f, 200f }
                           : i == 3 ? new[] { 0f, 50f, 110f, 160f, 330f } : null;
            if (angles == null) { angles = new float[24]; for (int k = 0; k < 24; k++) angles[k] = 300 + k * (240f / 23f); }
            foreach (var a in angles) beams.Add(new PlanBeam { Angle = a % 360f, Modality = planMod, WidthCm = 6, Weight = i == 4 ? 0.3f : 1f });
            selBeam = 0;
        }

        void RebuildBeamList()
        {
            if (beamList == null) return;
            beamList.Clear();
            for (int k = 0; k < beams.Count; k++)
            {
                int idx = k;
                var b = new Button(() => { selBeam = idx; RebuildBeamList(); }) { text = (k + 1) + ": " + F(beams[k].Angle, 0) + "°" };
                b.AddToClassList("mp-seg-btn");
                if (k == selBeam) b.AddToClassList("mp-seg-on");
                b.style.flexGrow = 0; b.style.minWidth = 70;
                beamList.Add(b);
            }
            var s = Sel;
            if (s != null)
            {
                vSel.text = T("Seçili ışın: ", "Selected beam: ") + (selBeam + 1);
                sAng.Set(s.Angle); sW.Set(s.Weight); sWid.Set(s.WidthCm);
            }
            BuildShafts();
        }

        void BuildShafts()
        {
            foreach (var r in beamShafts) if (r != null) UnityEngine.Object.Destroy(r.gameObject);
            beamShafts.Clear();
            for (int k = 0; k < beams.Count; k++)
            {
                var b = beams[k];
                float a = b.Angle * Mathf.Deg2Rad;
                // source direction in world: angle 0 = up (+Y), 90 = patient's left (+X)
                var sdir = new Vector3(Mathf.Sin(a), Mathf.Cos(a), 0);
                var t = new Vector3(Mathf.Cos(a), -Mathf.Sin(a), 0);
                var src = iso.position + sdir * 1.0f;
                float w = b.WidthCm * 0.01f * 0.5f, len = 0.04f;
                Vector3 far = iso.position - sdir * 0.25f;
                float sc = 1.25f;
                var corners = new[] { far + t * w * sc + Vector3.forward * len * sc, far - t * w * sc + Vector3.forward * len * sc,
                                      far - t * w * sc - Vector3.forward * len * sc, far + t * w * sc - Vector3.forward * len * sc };
                var col = b.Modality == PlanModality.Proton ? new Color(1f, 0.5f, 0.2f, k == selBeam ? 0.35f : 0.12f) : new Color(0.3f, 0.8f, 1f, k == selBeam ? 0.35f : 0.12f);
                var r = MeshObj("Shaft" + k, MeshGen.Beam(src, corners, 0.15f, 0.9f), Res.Additive(col, 1f, 0.3f));
                beamShafts.Add(r);
            }
        }

        void TickPlanning(float dt)
        {
            if (Sel != null)
            {
                gantryShown = Mathf.MoveTowardsAngle(gantryShown, Sel.Angle, dt * 120f);
                SetGantry(gantryShown);
            }
            if (planDirty && (planTask == null || planTask.IsCompleted))
            {
                planDirty = false;
                var copy = new List<PlanBeam>();
                foreach (var b in beams) copy.Add(b.Clone());
                planTask = Task.Run(() => grid.Compute(copy));
                waitingPlan = true;
            }
            if (waitingPlan && planTask != null && planTask.IsCompleted)
            {
                waitingPlan = false;
                if (planTask.Exception != null) Debug.LogException(planTask.Exception);
                ShowPlan();
            }
        }

        bool waitingPlan;

        void ShowPlan()
        {
            int n = PlanGrid.N;
            var px = new Color32[n * n];
            var wash = new Color32[n * n];
            float rx = grid.RxGy;
            for (int k = 0; k < px.Length; k++)
            {
                float hu = grid.Hu[k];
                float g = Mathf.Clamp01((hu + 160f) / 400f);
                Color c = new Color(g, g, g);
                float d = grid.Dose[k] / rx;
                Color dc = Color.clear;
                if (d >= 0.3f)
                {
                    dc = d >= 1.07f ? new Color(1f, 0.1f, 0.6f) : d >= 0.95f ? new Color(1f, 0.25f, 0.1f) : d >= 0.8f ? new Color(1f, 0.75f, 0.1f)
                       : d >= 0.5f ? new Color(0.2f, 0.9f, 0.3f) : new Color(0.2f, 0.5f, 1f);
                    c = Color.Lerp(c, dc, 0.45f);
                }
                // contours
                byte m = grid.Mask[k];
                if (Edge(k, (byte)Struct.Ptv)) c = new Color(0.3f, 1f, 0.4f);
                else if (Edge(k, (byte)Struct.Cord)) c = new Color(1f, 0.9f, 0.2f);
                else if (Edge(k, (byte)Struct.Heart)) c = new Color(1f, 0.25f, 0.25f);
                else if (Edge(k, (byte)(Struct.LungL | Struct.LungR))) c = new Color(1f, 0.55f, 0.8f);
                px[k] = c;
                wash[k] = d >= 0.3f ? new Color(dc.r, dc.g, dc.b, 0.55f) : new Color(0, 0, 0, 0);
            }
            planTex.SetPixels32(px); planTex.Apply();
            washTex.SetPixels32(wash); washTex.Apply();

            var ptv = grid.Values(Struct.Ptv); var cord = grid.Values(Struct.Cord); var heart = grid.Values(Struct.Heart);
            var ll = grid.Values(Struct.LungL); var lr = grid.Values(Struct.LungR); var lungs = grid.Values(Struct.LungL | Struct.LungR);
            float maxD = 0; foreach (var v in grid.Dose) maxD = Mathf.Max(maxD, v);
            dvh.XMax = Mathf.Max(70, Mathf.Ceil(maxD / 10) * 10);
            dvh.AllSeries[0].Set(PlanGrid.Dvh(ptv, dvh.XMax)); dvh.AllSeries[1].Set(PlanGrid.Dvh(cord, dvh.XMax));
            dvh.AllSeries[2].Set(PlanGrid.Dvh(heart, dvh.XMax)); dvh.AllSeries[3].Set(PlanGrid.Dvh(ll, dvh.XMax)); dvh.AllSeries[4].Set(PlanGrid.Dvh(lr, dvh.XMax));
            dvh.VLines.Clear(); dvh.VLines.Add((60f, new Color(1, 1, 1, 0.4f), "Rx"));
            dvh.Refresh();

            float d95 = PlanGrid.Dx(ptv, 95), d2 = PlanGrid.Dx(ptv, 2), cordMax = cord.Length > 0 ? cord[cord.Length - 1] : 0;
            float heartMean = PlanGrid.Mean(heart), v20 = PlanGrid.Vx(lungs, 20), mld = PlanGrid.Mean(lungs), ci = grid.Conformity(0.95f * rx);
            constraintsBox.Clear();
            int ok = 0;
            void C(string tr, string en, string val, bool pass) { var l = UIKit.KV(constraintsBox, T(tr, en), val + (pass ? "  ✓" : "  ×")); UIKit.SetStatus(l, pass ? 0 : 2); if (pass) ok++; }
            C("PTV D95 ≥ 57 Gy (95 %)", "PTV D95 ≥ 57 Gy (95 %)", F(d95, 1) + " Gy", d95 >= 57f);
            C("PTV D2 ≤ 64 Gy (107 %)", "PTV D2 ≤ 64 Gy (107 %)", F(d2, 1) + " Gy", d2 <= 64.2f);
            C("Omurilik Dmax < 45 Gy", "Spinal cord Dmax < 45 Gy", F(cordMax, 1) + " Gy", cordMax < 45f);
            C("Akciğer V20 < %35", "Lung V20 < 35 %", F(v20, 1) + " %", v20 < 35f);
            C("Ortalama akciğer dozu < 20 Gy", "Mean lung dose < 20 Gy", F(mld, 1) + " Gy", mld < 20f);
            C("Kalp ortalama < 26 Gy", "Heart mean < 26 Gy", F(heartMean, 1) + " Gy", heartMean < 26f);
            UIKit.KV(constraintsBox, T("Konformite indeksi (Paddick)", "Conformity index (Paddick)"), F(ci, 2));
            var score = UIKit.KV(constraintsBox, T("Plan puanı", "Plan score"), ok + " / 6  " + new string('★', ok) + new string('☆', 6 - ok));
            UIKit.SetStatus(score, ok == 6 ? 0 : ok >= 4 ? 1 : 2);
            bool photon = beams.Count > 0 && beams[0].Modality != PlanModality.Proton;
            if (ok == 6 && photon) CompleteTask("plan");
            if (photon && d95 >= 55) cordPhotonBest = cordPhotonBest < 0 ? cordMax : Mathf.Min(cordPhotonBest, cordMax);
            if (!photon && cordPhotonBest > 0 && d95 >= 55 && cordMax < cordPhotonBest - 1f) CompleteTask("proton");
        }

        bool Edge(int k, byte mask)
        {
            int n = PlanGrid.N;
            if ((grid.Mask[k] & mask) == 0) return false;
            int i = k % n, j = k / n;
            if (i == 0 || j == 0 || i == n - 1 || j == n - 1) return true;
            return (grid.Mask[k - 1] & mask) == 0 || (grid.Mask[k + 1] & mask) == 0 || (grid.Mask[k - n] & mask) == 0 || (grid.Mask[k + n] & mask) == 0;
        }

        // ====================================================================== 3. RADIOBIOLOGY
        static float alpha = 0.3f, abTumor = 10f, abLate = 3f, dosePerFx = 2f;
        static int nFx = 30;
        static float tpot = 3f;
        Chart survChart, tcpChart;
        Label vBedT, vBedL, vEqdT, vEqdL, vDays, vSurv, vTcp, vFxDone;
        readonly List<(Transform t, Renderer r, bool alive)> cells = new List<(Transform, Renderer, bool)>();
        Material cellAlive, cellDead;
        int fxDelivered;
        double survivingFrac = 1;
        float beamFlash;
        MeshRenderer rbBeam;
        Material rbBeamMat;
        bool autoDeliver;
        float autoT;
        static bool bedConv, bedSbrt;

        void BuildRadiobio()
        {
            SetGantry(0);
            // petri dish on the couch at the isocentre
            var dishPos = iso.position + new Vector3(0, -0.11f, 0);
            couchTop.position = new Vector3(couchTop.position.x, dishPos.y - 0.012f, couchTop.position.z);
            var dish = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            UnityEngine.Object.Destroy(dish.GetComponent<Collider>());
            dish.transform.SetParent(Root.transform, false);
            dish.transform.position = dishPos; dish.transform.localScale = new Vector3(0.22f, 0.006f, 0.22f);
            dish.GetComponent<Renderer>().sharedMaterial = Res.LitTransparent(new Color(0.7f, 0.9f, 1f, 0.3f), 0.95f);
            cellAlive = Res.Lit(new Color(0.35f, 0.95f, 0.5f), 0.6f);
            cellDead = Res.Lit(new Color(0.35f, 0.35f, 0.38f), 0.2f);
            var mesh = MeshGen.Ellipsoid(Vector3.zero, Vector3.one * 0.5f, Quaternion.identity, 8, 12);
            var rng = new System.Random(3);
            for (int i = 0; i < 360; i++)
            {
                float r = 0.1f * Mathf.Sqrt((float)rng.NextDouble()), a = (float)rng.NextDouble() * Mathf.PI * 2;
                var g = new GameObject("Cell");
                g.transform.SetParent(Root.transform, false);
                g.transform.position = dishPos + new Vector3(Mathf.Cos(a) * r, 0.006f, Mathf.Sin(a) * r);
                g.transform.localScale = Vector3.one * (0.007f + 0.003f * (float)rng.NextDouble());
                g.AddComponent<MeshFilter>().sharedMesh = mesh;
                var rr = g.AddComponent<MeshRenderer>(); rr.sharedMaterial = cellAlive;
                rr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                cells.Add((g.transform, rr, true));
            }
            rbBeamMat = Res.Additive(new Color(0.6f, 0.75f, 1f, 0f), 1.5f, 0.4f);
            float h = 0.12f;
            var corners = new[] { dishPos + new Vector3(-h, 0, -h), dishPos + new Vector3(h, 0, -h), dishPos + new Vector3(h, 0, h), dishPos + new Vector3(-h, 0, h) };
            rbBeam = MeshObj("FxBeam", MeshGen.Beam(source.position, corners, 0.3f, 1f), rbBeamMat);
            App.Rig.Frame(dishPos + new Vector3(0, 0.08f, 0), 1.2f, 28f, 38f, true);
            App.Rig.MinDistance = 0.3f; App.Rig.MaxDistance = 6f;

            var Lp = LeftScroll.contentContainer;
            var sec = UIKit.Section(Lp, T("Fraksiyonasyon şeması", "Fractionation schedule"));
            UIKit.Seg(sec, null, new[] { "30×2 Gy", "20×2.75", "15×2.67", "5×10 Gy (SBRT)", "3×18 Gy", "1×20 Gy" }, -1, i =>
            {
                (nFx, dosePerFx) = i == 0 ? (30, 2f) : i == 1 ? (20, 2.75f) : i == 2 ? (15, 2.67f) : i == 3 ? (5, 10f) : i == 4 ? (3, 18f) : (1, 20f);
                App.Open("therapy");
            });
            UIKit.Slider(sec, T("Fraksiyon dozu d", "Dose per fraction d"), 1, 20, dosePerFx, v => F(v, 2) + " Gy", v => { dosePerFx = v; RefreshRb(); }, 0.25f);
            UIKit.Slider(sec, T("Fraksiyon sayısı n", "Number of fractions n"), 1, 40, nFx, v => F(v, 0), v => { nFx = Mathf.RoundToInt(v); RefreshRb(); }, 1);
            sec = UIKit.Section(Lp, T("Doku parametreleri", "Tissue parameters"));
            UIKit.Slider(sec, "α", 0.05f, 0.6f, alpha, v => F(v, 2) + " Gy⁻¹", v => { alpha = v; RefreshRb(); }, 0.01f);
            UIKit.Slider(sec, T("α/β tümör", "α/β tumour"), 1, 20, abTumor, v => F(v, 1) + " Gy", v => { abTumor = v; RefreshRb(); }, 0.5f);
            UIKit.Slider(sec, T("α/β geç yanıt dokusu", "α/β late-responding tissue"), 1, 10, abLate, v => F(v, 1) + " Gy", v => { abLate = v; RefreshRb(); }, 0.5f);
            UIKit.Slider(sec, T("Potansiyel ikilenme süresi Tpot", "Potential doubling time Tpot"), 1, 30, tpot, v => F(v, 0) + T(" gün", " days"), v => { tpot = v; RefreshRb(); }, 1);
            sec = UIKit.Section(Lp, T("Hücre kültürü (3B)", "Cell culture (3D)"));
            var row = UIKit.ButtonRow(sec);
            UIKit.Button(row, T("1 fraksiyon ver", "Deliver 1 fraction"), DeliverFraction, "mp-btn-primary");
            UIKit.Button(row, T("Tümünü ver", "Deliver all"), () => autoDeliver = true);
            UIKit.Button(row, T("Sıfırla", "Reset"), () => App.Open("therapy"));
            vFxDone = UIKit.KV(sec, T("Verilen fraksiyon", "Fractions delivered"), "0 / " + nFx);
            vSurv = UIKit.KV(sec, T("Hayatta kalan oran S", "Surviving fraction S"), "1");

            var R = RightScroll.contentContainer;
            sec = UIKit.Section(R, T("Hücre sağkalım eğrisi (LQ)", "Cell survival curve (LQ)"));
            survChart = new Chart(220) { XMin = 0, XMax = 20, LogY = true, AutoY = false, YMin = 1e-6f, YMax = 1, XLabel = "Gy", YLabel = "S" };
            survChart.Add(T("Tek doz, α/β=" , "Single dose, α/β=") + F(abTumor, 0), new Color(0.3f, 0.9f, 1f), 2f);
            survChart.Add(T("Tek doz, α/β=", "Single dose, α/β=") + F(abLate, 0), new Color(1f, 0.6f, 0.3f), 2f);
            var fx = survChart.Add(T("Fraksiyonlu (d sabit)", "Fractionated (fixed d)"), new Color(0.4f, 1f, 0.5f), 2f); fx.Dashed = true;
            sec.Add(survChart.Legend()); sec.Add(survChart);
            sec = UIKit.Section(R, T("Biyolojik etkin doz", "Biologically effective dose"));
            vDays = UIKit.KV(sec, T("Toplam doz / süre", "Total dose / time"));
            vBedT = UIKit.KV(sec, T("BED tümör", "BED tumour")); vEqdT = UIKit.KV(sec, T("EQD2 tümör", "EQD2 tumour"));
            vBedL = UIKit.KV(sec, T("BED geç doku", "BED late tissue")); vEqdL = UIKit.KV(sec, T("EQD2 geç doku", "EQD2 late tissue"));
            sec = UIKit.Section(R, T("Tümör kontrol olasılığı", "Tumour control probability"));
            tcpChart = new Chart(170) { XMin = 0, XMax = 100, AutoY = false, YMin = 0, YMax = 1.05f, XLabel = T("toplam doz (Gy)", "total dose (Gy)"), YLabel = "TCP" };
            tcpChart.Add("TCP", new Color(0.4f, 1f, 0.5f), 2f);
            sec.Add(tcpChart);
            vTcp = UIKit.KV(sec, T("Bu şema için TCP (N₀ = 10⁹)", "TCP for this schedule (N₀ = 10⁹)"));
            RefreshRb();
            SetTip("Her hücre her fraksiyonda S(d) = exp(−αd − βd²) olasılıkla hayatta kalır. Düşük α/β'lı dokular fraksiyon dozuna daha duyarlıdır.",
                   "Each cell survives a fraction with probability S(d) = exp(−αd − βd²). Low α/β tissues are more sensitive to dose per fraction.");
        }

        void RefreshRb()
        {
            if (survChart == null) return;
            var a = survChart.AllSeries[0]; var b = survChart.AllSeries[1]; var c = survChart.AllSeries[2];
            a.Pts.Clear(); b.Pts.Clear(); c.Pts.Clear();
            for (float d = 0; d <= 20; d += 0.25f)
            {
                a.Pts.Add(new Vector2(d, (float)Radiobiology.Survival(alpha, abTumor, d)));
                b.Pts.Add(new Vector2(d, (float)Radiobiology.Survival(alpha, abLate, d)));
            }
            for (int k = 0; k * dosePerFx <= 20.01f; k++)
                c.Pts.Add(new Vector2(k * dosePerFx, (float)Math.Pow(Radiobiology.Survival(alpha, abTumor, dosePerFx), k)));
            survChart.Refresh();
            double days = Radiobiology.TreatmentDays(nFx);
            double bedT = Radiobiology.Bed(dosePerFx, nFx, abTumor, alpha, days, 28, tpot);
            double bedL = Radiobiology.Bed(dosePerFx, nFx, abLate);
            vDays.text = F(dosePerFx * nFx, 1) + " Gy / " + F(days, 0) + T(" gün", " days");
            vBedT.text = F(bedT, 1) + " Gy" + (days > 28 ? T(" (repopülasyon dahil)", " (incl. repopulation)") : "");
            vEqdT.text = F(Radiobiology.Eqd2(bedT, abTumor), 1) + " Gy";
            vBedL.text = F(bedL, 1) + " Gy";
            vEqdL.text = F(Radiobiology.Eqd2(bedL, abLate), 1) + " Gy";
            var tc = tcpChart.AllSeries[0]; tc.Pts.Clear();
            double sd = Radiobiology.Survival(alpha, abTumor, dosePerFx);
            for (float D = 0; D <= 100; D += 1f)
            {
                int n = Mathf.Max(1, Mathf.RoundToInt(D / dosePerFx));
                double s = Math.Pow(sd, D / dosePerFx);
                double dd = Radiobiology.TreatmentDays(n);
                if (dd > 28) s *= Math.Pow(2, (dd - 28) / tpot);
                tc.Pts.Add(new Vector2(D, (float)Radiobiology.Tcp(1e9, s)));
            }
            tcpChart.VLines.Clear(); tcpChart.VLines.Add((dosePerFx * nFx, new Color(1, 1, 1, 0.5f), ""));
            tcpChart.Refresh();
            double sTot = Radiobiology.SurvivalFractionated(alpha, abTumor, dosePerFx, nFx, days, 28, tpot);
            vTcp.text = F(Radiobiology.Tcp(1e9, sTot) * 100, 1) + " %";
            if (nFx == 30 && Mathf.Abs(dosePerFx - 2) < 0.01f) bedConv = true;
            if (nFx == 5 && Mathf.Abs(dosePerFx - 10) < 0.01f) bedSbrt = true;
            if (bedConv && bedSbrt) CompleteTask("bed");
            vFxDone.text = fxDelivered + " / " + nFx;
        }

        void DeliverFraction()
        {
            if (fxDelivered >= nFx) return;
            fxDelivered++;
            beamFlash = 1f;
            double s = Radiobiology.Survival(alpha, abTumor, dosePerFx);
            survivingFrac *= s;
            var rng = new System.Random(fxDelivered * 977);
            for (int i = 0; i < cells.Count; i++)
            {
                var c = cells[i];
                if (!c.alive) continue;
                if (rng.NextDouble() > s) { c.alive = false; c.r.sharedMaterial = cellDead; c.t.localScale *= 0.7f; cells[i] = c; }
            }
            vFxDone.text = fxDelivered + " / " + nFx;
            vSurv.text = survivingFrac.ToString("0.###E+0") + "  (" + T("klonojen: ", "clonogens: ") + (1e9 * survivingFrac).ToString("0.##E+0") + ")";
        }

        void TickRadiobio(float dt)
        {
            beamFlash = Mathf.Max(0, beamFlash - dt * 2f);
            rbBeamMat.SetColor("_Color", new Color(0.6f, 0.75f, 1f, beamFlash * 0.8f));
            if (autoDeliver)
            {
                autoT -= dt;
                if (autoT <= 0) { autoT = 0.45f; DeliverFraction(); if (fxDelivered >= nFx) autoDeliver = false; }
            }
        }

        public override void AutoAction(string action)
        {
            if (action == "proton") { modality = 2; App.Open("therapy"); }
            if (action == "plan") { tab = 1; App.Open("therapy"); }
            if (action == "rb") { tab = 2; App.Open("therapy"); }
            if (action == "beam") { tab = 0; modality = 0; App.Open("therapy"); }
        }

        // ====================================================================== theory
        protected override void BuildLearn(VisualElement c)
        {
            UIKit.H(c, T("1. Foton ışınları ve derin doz", "1. Photon beams and depth dose"));
            UIKit.P(c, T("MV fotonlar suda çoğunlukla Compton saçılması yapar; enerji, ikincil elektronlar tarafından birkaç cm boyunca bırakılır. Bu yüzden doz yüzeyde düşüktür ve dmax'a kadar yükselir (<b>build-up</b>; cilt koruma). Sonra doz, zayıflama ve ters kare nedeniyle düşer.",
                             "MV photons interact in water mainly by Compton scattering; energy is deposited by secondary electrons over several cm. Hence the dose is low at the surface and rises to dmax (<b>build-up</b>; skin sparing). It then falls due to attenuation and the inverse square law."));
            UIKit.Eq(c, "PDD(d) = 100 · D(d) / D(dmax)        6 MV: dmax ≈ 1.5 cm, PDD₁₀ ≈ 67 %   •   18 MV: dmax ≈ 3.3 cm, PDD₁₀ ≈ 79 %");
            UIKit.P(c, T("Klein–Nishina tesir kesiti Compton saçılma açısını ve geri tepen elektronun enerjisini belirler. Monte Carlo simülasyonu her fotonu ve elektronu tek tek izler; doz istatistiksel olarak σ ~ 1/√N belirsizlikle yakınsar.",
                             "The Klein–Nishina cross section sets the Compton scattering angle and recoil-electron energy. The Monte Carlo simulation follows every photon and electron; the dose converges with statistical uncertainty σ ~ 1/√N."));
            UIKit.H(c, T("2. Elektron ışınları", "2. Electron beams"));
            UIKit.P(c, T("Elektronların sınırlı bir menzili vardır: yüzeysel tümörler için idealdir. Pratik kurallar: R50 ≈ E₀/2.33 cm, Rp ≈ 0.5·E₀ cm, terapötik derinlik R80 ≈ E/3 cm. Derindeki küçük doz kuyruğu frenleme (bremsstrahlung) fotonlarından gelir.",
                             "Electrons have a finite range: ideal for superficial tumours. Rules of thumb: R50 ≈ E₀/2.33 cm, Rp ≈ 0.5·E₀ cm, therapeutic depth R80 ≈ E/3 cm. The small tail at depth comes from bremsstrahlung photons."));
            UIKit.H(c, T("3. Protonlar ve Bragg piki", "3. Protons and the Bragg peak"));
            UIKit.Eq(c, "−dE/dx = K · (Z/A) · (z²/β²) · [ ½·ln(2mₑc²β²γ²T_max / I²) − β² − δ/2 ]     (Bethe–Bloch)");
            UIKit.P(c, T("Durdurma gücü yavaşlayan protonda 1/β² ile artar; enerjinin büyük kısmı menzilin sonunda bırakılır (Bragg piki). Menzil saçılımı (≈ %1.2·R) ve enerji yayılımı piki genişletir. Farklı enerjili piklerin ağırlıklı toplamı tümörü kaplayan SOBP'yi oluşturur. Pik sonrası doz ~0'dır: arkadaki organlar korunur.",
                             "The stopping power rises as 1/β² while the proton slows; most energy is deposited at the end of range (Bragg peak). Range straggling (≈ 1.2 %·R) and energy spread broaden the peak. A weighted sum of peaks of different energies forms the SOBP covering the tumour. Beyond the peak the dose is ~0: distal organs are spared."));
            UIKit.H(c, T("4. Tedavi planlama", "4. Treatment planning"));
            UIKit.P(c, T("Doz, eşdeğer yol uzunluğu (radyolojik derinlik = Σ ρₑ·Δl) ile heterojenlik düzeltmesi yapılarak hesaplanır: akciğerden geçen ışın daha az zayıflar. Plan kalitesi DVH ile değerlendirilir: PTV D95 ≥ %95, omurilik Dmax < 45 Gy, akciğer V20 < %35, ortalama akciğer dozu < 20 Gy, kalp ortalama < 26 Gy (QUANTEC).",
                             "Dose is calculated with equivalent path length heterogeneity correction (radiological depth = Σ ρₑ·Δl): a beam through lung is attenuated less. Plan quality is judged by the DVH: PTV D95 ≥ 95 %, cord Dmax < 45 Gy, lung V20 < 35 %, mean lung dose < 20 Gy, heart mean < 26 Gy (QUANTEC)."));
            UIKit.H(c, T("5. Radyobiyoloji", "5. Radiobiology"));
            UIKit.Eq(c, "S = e^(−αd − βd²)      BED = n·d·(1 + d/(α/β)) − ln2·(T − Tk)/(α·Tpot)      EQD2 = BED / (1 + 2/(α/β))");
            UIKit.P(c, T("α/β ≈ 10 Gy (tümör, erken yanıt) ve ≈ 3 Gy (geç yanıt veren normal dokular, ör. omurilik). Küçük fraksiyon dozları geç dokuları korur; SBRT gibi yüksek fraksiyon dozları tümörde çok yüksek BED sağlar ama hassas hedefleme gerektirir. TCP = exp(−N₀·S).",
                             "α/β ≈ 10 Gy (tumour, early-responding) and ≈ 3 Gy (late-responding normal tissue, e.g. spinal cord). Small doses per fraction spare late tissues; high doses per fraction such as SBRT give very high tumour BED but need precise targeting. TCP = exp(−N₀·S)."));
            UIKit.Note(c, T("Model notları: MC yoğunlaştırılmış geçmiş yöntemi (Highland çoklu saçılma) kullanır; planlama 2B'dir ve yanal saçılma ihmal edilir.",
                            "Model notes: MC uses condensed history (Highland multiple scattering); planning is 2D and neglects lateral scatter."));
        }
    }
}
