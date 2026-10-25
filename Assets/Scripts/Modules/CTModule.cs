using System;
using UnityEngine;
using UnityEngine.UIElements;
using MedPhys.Core;
using MedPhys.Physics;
using MedPhys.UI;
using static MedPhys.Core.Loc;

namespace MedPhys.Modules
{
    /// <summary>Computed tomography: acquisition (sinogram), filtered back-projection, HU, windowing, artefacts, dose.</summary>
    public sealed class CTModule : ModuleBase
    {
        public override string Id => "ct";
        public override string Title => T("Bilgisayarlı Tomografi", "Computed Tomography");
        public override string Subtitle => T("Radon dönüşümü • FBP • Hounsfield • Artefaktlar", "Radon transform • FBP • Hounsfield • Artefacts");

        protected override (string id, string tr, string en)[] Tasks => new[]
        {
            ("scan", "İlk taramanı yap: sinogramın nasıl dolduğunu izle", "Run your first scan and watch the sinogram fill"),
            ("hu", "HU ölçümü: akciğer (< −500), yağ (−150…−30) ve kemik (> +300) üzerine dokun", "Measure HU: tap lung (< −500), fat (−150…−30) and bone (> +300)"),
            ("views", "Az projeksiyon (≤ 60) ile tara: çizgi artefaktlarını gör", "Scan with few views (≤ 60): see the streak artefacts"),
            ("bh", "Işın sertleşmesi: BHC kapalı, polienerjetik tarama yap (çanak etkisi)", "Beam hardening: scan polyenergetic with BHC off (cupping)"),
            ("metal", "Metal artefaktı: kalça protezli pelvis kesiti tara", "Metal artefact: scan a pelvis slice with the hip prosthesis"),
            ("window", "Göğüs kesitinde akciğer penceresine geç", "Switch to the lung window on a chest slice"),
        };

        readonly CTSettings S = new CTSettings();
        bool implant, contrast, tumor = true, showInside = true;
        int windowPreset;
        float W = 400, L = 40;
        bool huLung, huFat, huBone;

        Phantom ph;
        PhantomView view;
        Transform rotor, couchTop, cover, iso, focus;
        Vector3 couchTop0;
        Material coverOrig, coverHolo, fanMat, laserMat;
        Renderer coverR;
        MeshRenderer fan;
        float rotorAngle, rotorSpeed;
        float targetRootZ, currentRootZ;

        CTJob job, lastJob;
        float[] huLive;
        Texture2D ctTex, sinoTex, holoTex;
        int lastShownViews;

        ImageView ctImg, sinoImg;
        Label vProbe, vEff, vMuW, vCtdi, vDlp, vE, vNoise, vStatus;
        UIKit.ProgressBar prog;
        UIKit.SliderRow sW, sL, sZ;
        UIKit.Segmented winSeg;
        VisualElement probeMark;

        static readonly (string tr, string en, float z)[] Levels =
        {
            ("Kafa", "Head", 89f), ("Boyun", "Neck", 72f), ("Akciğer", "Lung", 56f), ("Kalp", "Heart", 47f),
            ("Karaciğer", "Liver", 33f), ("Böbrek", "Kidney", 27f), ("Pelvis", "Pelvis", 2f),
        };

        protected override void Build()
        {
            Env.Room(Root.transform, new Color(0.25f, 0.85f, 0.9f));
            var ct = Res.Model("CT_Scanner", Root.transform);
            rotor = ct.Find("CT_Rotor"); couchTop = ct.Find("CT_CouchTop"); cover = ct.Find("CT_Cover");
            iso = ct.Find("CT_Iso"); focus = ct.Find("CT_Focus");
            couchTop0 = couchTop.localPosition;
            coverR = cover.GetComponent<Renderer>();
            coverOrig = coverR.sharedMaterial;
            coverHolo = Res.Hologram(new Color(0.6f, 0.85f, 1f, 0.05f), new Color(0.5f, 0.85f, 1f, 0.55f), 2.0f);

            // fan beam (child of the rotor) and positioning lasers
            fanMat = Res.Additive(new Color(0.45f, 0.65f, 1f, 0.0f), 1.4f, 0.2f);
            fan = MeshObj("FanBeam", FanMesh(), fanMat, rotor);
            laserMat = Res.Additive(new Color(0.2f, 1f, 0.35f, 0.9f), 2f);
            var lz = iso.position.z;
            var l1 = MeshObj("LaserH", MeshGen.Quad(1.6f, 0.004f), laserMat);
            l1.transform.position = new Vector3(0, iso.position.y + 0.42f, lz); l1.transform.rotation = Quaternion.Euler(90, 0, 0);
            var l2 = MeshObj("LaserV", MeshGen.Quad(0.004f, 0.6f), laserMat);
            l2.transform.position = new Vector3(0, iso.position.y + 0.25f, lz - 0.001f);

            ctTex = UIKit.NewTex(S.Res, S.Res);
            holoTex = ctTex;
            sinoTex = UIKit.NewTex(S.NDet, S.NProj);
            huLive = new float[S.Res * S.Res];
            // floating holographic monitor with the live slice
            var holo = MeshObj("HoloMonitor", MeshGen.Quad(0.62f, 0.62f), Res.UnlitTex(holoTex, new Color(0.9f, 1f, 1f), false));
            holo.transform.position = new Vector3(0f, 2.55f, -0.45f);
            holo.transform.rotation = Quaternion.Euler(-8, 0, 0);
            var frame = MeshObj("HoloFrame", MeshGen.Ring(0.46f, 0.008f, 4), Res.Additive(new Color(0.3f, 0.9f, 1f, 0.8f), 2f));
            frame.transform.SetParent(holo.transform, false);
            frame.transform.localRotation = Quaternion.Euler(0, 0, 45);

            RebuildPhantom();
            App.Rig.Frame(new Vector3(0.2f, 1.25f, -0.7f), 4.0f, -38f, 14f, true);
            App.Rig.MinDistance = 0.8f; App.Rig.MaxDistance = 8f;
            App.Rig.PanBounds = new Bounds(new Vector3(0, 1.2f, -0.5f), new Vector3(6, 2.5f, 6));
            BuildUI();
            SetTip("Kesit seviyesini seç ve TARA'ya bas. Görüntüye dokunarak HU değerini ölç.", "Choose the slice level and press SCAN. Tap the image to measure HU.");
            MachineParts(ct);
            Scan();
        }

        void MachineParts(GameObject ct)
        {
            Part(ct.Find("CT_XrayTube"), "X-ışını tüpü", "X-ray tube",
                 "Gantriyle birlikte saniyede 2–4 tur döner ve her açıdan hastanın içinden yelpaze biçimli bir ışın gönderir.",
                 "Spins with the gantry 2–4 times a second and sends a fan beam through the patient from every angle.", new Vector2(-1f, 0.5f), 110);
            Part(ct.Find("CT_Collimator"), "Bowtie filtre + kolimatör", "Bowtie filter + collimator",
                 "Ortası ince, kenarları kalın filtre hastanın ince kenarlarına giden dozu azaltır ve dedektördeki sinyal aralığını dengeler. Kolimatör kesit kalınlığını belirler.",
                 "Thin in the middle and thick at the edges, it cuts the dose to the thin edges of the patient and evens out the detector signal. The collimator sets the slice width.", new Vector2(1f, 0.6f), 120);
            Part(ct.Find("CT_Detector"), "Dedektör yayı", "Detector arc",
                 "Yüzlerce kanal × çok sıra seramik sintilatör. Her açıda bir projeksiyon ölçer; bütün açılar birlikte sinogramı oluşturur.",
                 "Hundreds of channels × many rows of ceramic scintillator. It measures one projection per angle; together they form the sinogram.", new Vector2(1f, -0.5f), 110);
            Part(ct.Find("CT_RotorRing"), "Döner halka (slip ring)", "Slip ring",
                 "Güç ve veri kablosuz aktarılır; tüp durmadan dönebilir, bu da helikal taramayı mümkün kılar.",
                 "Power and data cross without cables, so the tube can spin continuously, which makes helical scanning possible.", new Vector2(-1f, -0.3f), 110,
                 iso.position + new Vector3(-0.6f, -0.6f, 0));
            Part(couchTop, "Hasta masası", "Patient couch",
                 "Helikal taramada sabit hızla ilerler. Pitch = tur başına masa ilerlemesi / toplam ışın genişliği; pitch > 1 dozu azaltır.",
                 "Moves at constant speed during a helical scan. Pitch = table feed per rotation / total beam width; pitch > 1 lowers the dose.", new Vector2(-1f, -1f), 90);
            Part(iso, "İzomerkez", "Isocentre",
                 "Dönme ekseni. Hastanın buraya ortalanması önemlidir: kayık ortalama bowtie filtreyi işlevsiz kılar, gürültüyü ve dozu artırır.",
                 "The rotation axis. Centring the patient here matters: off-centring defeats the bowtie filter and raises both noise and dose.", new Vector2(0.3f, 1f), 150);
        }

        Mesh FanMesh()
        {
            // fan from the focal spot to the detector arc, in rotor-local coordinates
            var f = rotor.InverseTransformPoint(focus.position);
            var m = new Mesh();
            int n = 24; float r = 1.06f, half = 26f * Mathf.Deg2Rad;
            var v = new System.Collections.Generic.List<Vector3> { f };
            var c = new System.Collections.Generic.List<Color> { new Color(1, 1, 1, 0.9f) };
            var tri = new System.Collections.Generic.List<int>();
            for (int i = 0; i <= n; i++)
            {
                float a = -half + 2 * half * i / n;
                // rotor-local frame is the Blender frame (x lateral, y = bore axis, z up); detector below the focus
                v.Add(f + new Vector3(Mathf.Sin(a) * r, 0, -Mathf.Cos(a) * r));
                c.Add(new Color(1, 1, 1, 0.25f));
                if (i > 0) { tri.Add(0); tri.Add(i); tri.Add(i + 1); }
            }
            m.SetVertices(v); m.SetColors(c); m.SetTriangles(tri, 0);
            m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        void RebuildPhantom()
        {
            if (view != null) UnityEngine.Object.Destroy(view.gameObject);
            ph = Phantom.BuildAdult(new PhantomOptions { Tumor = tumor, HipImplant = implant, Contrast = contrast });
            view = PhantomView.Create(ph, Root.transform);
            PlacePhantom(true);
        }

        void PlacePhantom(bool instant)
        {
            float scanZ = iso.position.z;
            targetRootZ = scanZ - S.SliceZ * 0.01f;
            if (instant) currentRootZ = targetRootZ;
            ApplyCouch();
        }

        void ApplyCouch()
        {
            float rootY = 0.975f + 0.105f;
            view.Orient(new Vector3(0, rootY, currentRootZ), Vector3.forward, Vector3.up);
            // couch top follows: head end of the pad stays ~5 cm beyond the head
            float headZ = currentRootZ + 0.99f;
            float t = headZ - (-0.5f) - 0.05f;
            couchTop.localPosition = couchTop0;
            couchTop.position = couchTop.position + new Vector3(0, 0, t);
        }

        // ------------------------------------------------------------------ UI
        void BuildUI()
        {
            var Lp = LeftScroll.contentContainer;
            var sec = UIKit.Section(Lp, T("Kesit seviyesi", "Slice level"));
            var names = new string[Levels.Length];
            for (int i = 0; i < Levels.Length; i++) names[i] = T(Levels[i].tr, Levels[i].en);
            UIKit.Seg(sec, null, names, 2, i => { S.SliceZ = Levels[i].z; sZ.Set(S.SliceZ); PlacePhantom(false); AutoWindow(); });
            sZ = UIKit.Slider(sec, T("Masa pozisyonu (z)", "Table position (z)"), -15, 96, S.SliceZ, v => F(v, 0) + " cm", v => { S.SliceZ = v; PlacePhantom(false); }, 1);

            sec = UIKit.Section(Lp, T("Tarama parametreleri", "Scan parameters"));
            UIKit.Seg(sec, T("Tüp gerilimi", "Tube voltage"), new[] { "80", "100", "120", "140 kVp" }, 2, i => S.Kvp = new[] { 80f, 100f, 120f, 140f }[i]);
            UIKit.Slider(sec, T("Tüp yükü", "Tube load"), 20, 400, S.Mas, v => F(v, 0) + " mAs", v => S.Mas = v, 5);
            UIKit.Slider(sec, T("Projeksiyon sayısı (180°)", "Number of views (180°)"), 18, 720, S.NProj, v => F(v, 0), v => S.NProj = Mathf.RoundToInt(v), 6);
            UIKit.Seg(sec, T("Dedektör kanalı", "Detector channels"), new[] { "128", "256", "384" }, 1, i => S.NDet = new[] { 128, 256, 384 }[i]);

            sec = UIKit.Section(Lp, T("Rekonstrüksiyon", "Reconstruction"));
            UIKit.Seg(sec, T("Filtre", "Filter"), new[] { "Ram-Lak", "Shepp-Logan", T("Kosinüs", "Cosine"), "Hann" }, 1, i => S.Filter = (CTFilter)i);
            UIKit.Toggle(sec, T("Polienerjetik ışın (gerçekçi)", "Polyenergetic beam (realistic)"), S.Poly, v => S.Poly = v);
            UIKit.Toggle(sec, T("Su ışın sertleşmesi düzeltmesi (BHC)", "Water beam-hardening correction (BHC)"), S.Bhc, v => S.Bhc = v);
            UIKit.Toggle(sec, T("Kuantum gürültüsü", "Quantum noise"), S.Noise, v => S.Noise = v);

            sec = UIKit.Section(Lp, T("Hasta / fantom", "Patient / phantom"));
            UIKit.Toggle(sec, T("Akciğer tümörü", "Lung tumour"), tumor, v => { tumor = v; RebuildPhantom(); });
            UIKit.Toggle(sec, T("Titanyum kalça protezi", "Titanium hip prosthesis"), implant, v => { implant = v; RebuildPhantom(); });
            UIKit.Toggle(sec, T("İyotlu kontrast (kalp/aorta)", "Iodine contrast (heart/aorta)"), contrast, v => { contrast = v; RebuildPhantom(); });
            UIKit.Toggle(sec, T("Gantri kapağını şeffaf göster", "Show gantry cover transparent"), showInside, v => { showInside = v; });

            sec = UIKit.Section(Lp, T("Tarama", "Scan"));
            UIKit.Button(UIKit.ButtonRow(sec), T("TARA  (Scan)", "SCAN"), Scan, "mp-btn-primary");
            prog = UIKit.Progress(sec);
            vStatus = UIKit.Text(sec, "");
            BuildTasks(Lp);

            var R = RightScroll.contentContainer;
            sec = UIKit.Section(R, T("Aksiyel kesit (HU)", "Axial slice (HU)"));
            ctImg = new ImageView(App.SmallScreen ? 300 : 400, "");
            ctImg.Set(ctTex);
            ctImg.OnPick = Probe;
            probeMark = new VisualElement { pickingMode = PickingMode.Ignore };
            probeMark.style.position = Position.Absolute; probeMark.style.width = 12; probeMark.style.height = 12;
            probeMark.style.borderTopWidth = 2; probeMark.style.borderBottomWidth = 2; probeMark.style.borderLeftWidth = 2; probeMark.style.borderRightWidth = 2;
            var pc = new Color(1f, 0.8f, 0.2f);
            probeMark.style.borderTopColor = pc; probeMark.style.borderBottomColor = pc; probeMark.style.borderLeftColor = pc; probeMark.style.borderRightColor = pc;
            probeMark.style.borderTopLeftRadius = 6; probeMark.style.borderTopRightRadius = 6; probeMark.style.borderBottomLeftRadius = 6; probeMark.style.borderBottomRightRadius = 6;
            probeMark.style.display = DisplayStyle.None;
            ctImg.Overlay.Add(probeMark);
            var rl = new Label("R") { pickingMode = PickingMode.Ignore }; rl.style.position = Position.Absolute; rl.style.left = 8; rl.style.bottom = 4; rl.style.color = new Color(1, 0.85f, 0.3f);
            var al = new Label("A") { pickingMode = PickingMode.Ignore }; al.style.position = Position.Absolute; al.style.right = 8; al.style.top = 4; al.style.color = new Color(1, 0.85f, 0.3f);
            ctImg.Overlay.Add(rl); ctImg.Overlay.Add(al);
            sec.Add(ctImg);
            vProbe = UIKit.KV(sec, T("Ölçüm (dokun)", "Probe (tap)"), T("görüntüye dokun", "tap the image"));
            winSeg = UIKit.Seg(sec, T("Pencere", "Window"), new[] { T("Yumuşak doku", "Soft tissue"), T("Akciğer", "Lung"), T("Kemik", "Bone"), T("Beyin", "Brain") }, 0, SetWindow);
            sW = UIKit.Slider(sec, T("Pencere genişliği (W)", "Window width (W)"), 1, 4000, W, v => F(v, 0) + " HU", v => { W = v; Redraw(); }, 1);
            sL = UIKit.Slider(sec, T("Pencere seviyesi (L)", "Window level (L)"), -1000, 1500, L, v => F(v, 0) + " HU", v => { L = v; Redraw(); }, 1);

            sec = UIKit.Section(R, T("Sinogram  p(θ, s)", "Sinogram  p(θ, s)"));
            sinoImg = new ImageView(170, T("yatay: dedektör s  •  dikey: açı θ (0→180°)", "horizontal: detector s  •  vertical: angle θ (0→180°)"));
            sinoImg.Set(sinoTex);
            sec.Add(sinoImg);

            sec = UIKit.Section(R, T("Işın ve kalibrasyon", "Beam and calibration"));
            vEff = UIKit.KV(sec, T("Ortalama / efektif enerji", "Mean / effective energy"));
            vMuW = UIKit.KV(sec, T("μ(su) referansı", "μ(water) reference"));
            vNoise = UIKit.KV(sec, T("Gürültü (σ, homojen bölge)", "Noise (σ, uniform region)"));
            UIKit.Note(sec, "HU = 1000 · (μ − μ_su) / (μ_su − μ_hava)");

            sec = UIKit.Section(R, T("Doz", "Dose"));
            vCtdi = UIKit.KV(sec, "CTDIvol");
            vDlp = UIKit.KV(sec, T("DLP (30 cm tarama)", "DLP (30 cm scan)"));
            vE = UIKit.KV(sec, T("Efektif doz", "Effective dose"));
            UIKit.Note(sec, T("CTDIw: 32 cm gövde (16 cm baş) fantomu; E = DLP × k (göğüs 0.014, karın 0.015, baş 0.0021 mSv/mGy·cm).", "CTDIw: 32 cm body (16 cm head) phantom; E = DLP × k (chest 0.014, abdomen 0.015, head 0.0021 mSv/mGy·cm)."));
        }

        void SetWindow(int i)
        {
            windowPreset = i;
            (W, L) = i == 0 ? (400f, 40f) : i == 1 ? (1500f, -600f) : i == 2 ? (2000f, 400f) : (80f, 35f);
            sW.Set(W); sL.Set(L);
            if (i == 1 && S.SliceZ > 40 && S.SliceZ < 68) CompleteTask("window");
            Redraw();
        }

        void AutoWindow()
        {
            int preset = S.SliceZ > 78 ? 3 : 0;
            winSeg.Select(preset, false);
            SetWindow(preset);
        }

        void Scan()
        {
            if (job != null && !job.Done) return;
            S.Seed++;
            var s = new CTSettings
            {
                SliceZ = S.SliceZ, Kvp = S.Kvp, Mas = S.Mas, NProj = S.NProj, NDet = S.NDet, FovCm = S.FovCm, Poly = S.Poly, Bhc = S.Bhc,
                Noise = S.Noise, Filter = S.Filter, Res = S.Res, Seed = S.Seed
            };
            if (sinoTex.width != s.NDet || sinoTex.height != s.NProj)
            {
                UnityEngine.Object.Destroy(sinoTex);
                sinoTex = UIKit.NewTex(s.NDet, s.NProj);
                sinoImg.Set(sinoTex);
            }
            var clear = new Color32[s.NDet * s.NProj];
            for (int i = 0; i < clear.Length; i++) clear[i] = new Color32(0, 0, 0, 255);
            sinoTex.SetPixels32(clear); sinoTex.Apply();
            job = new CTJob { Ph = ph, S = s };
            job.Start();
            lastShownViews = 0;
            rotorSpeed = 1;
            vStatus.text = T("Taranıyor…", "Scanning…");
        }

        public override void Tick(float dt)
        {
            base.Tick(dt);
            // couch motion
            if (Mathf.Abs(currentRootZ - targetRootZ) > 1e-4f)
            {
                currentRootZ = Mathf.MoveTowards(currentRootZ, targetRootZ, dt * 0.35f);
                ApplyCouch();
            }
            // rotor spin-up / spin-down
            bool scanning = job != null && !job.Done;
            rotorSpeed = Mathf.MoveTowards(rotorSpeed, scanning ? 1f : PartsShown ? 0f : 0.15f, dt * 0.6f);
            rotorAngle += rotorSpeed * 540f * dt;
            // while the parts are labelled the idle rotor parks with the tube at the top
            if (PartsShown && !scanning) rotorAngle = Mathf.MoveTowardsAngle(rotorAngle, 0f, 120f * dt);
            rotor.localRotation = Quaternion.AngleAxis(rotorAngle, Vector3.up);
            fanMat.SetColor("_Color", new Color(0.45f, 0.65f, 1f, scanning ? 0.55f : 0f));
            coverR.sharedMaterial = (showInside || scanning) ? coverHolo : coverOrig;

            if (job != null)
            {
                int v = job.ViewsDone;
                prog.Set(job.Done ? 1 : v / (float)job.S.NProj);
                if (v != lastShownViews && v > 0)
                {
                    lastShownViews = v;
                    UpdateSino(job, v);
                    job.ToHu(huLive, v);
                    Redraw();
                }
                if (job.Done)
                {
                    var j = job; job = null;
                    if (j.Failed) { vStatus.text = "Error: " + j.Error; Debug.LogError(j.Error); }
                    else Finish(j);
                }
            }
        }

        void UpdateSino(CTJob j, int views)
        {
            int nD = j.S.NDet;
            var px = sinoTex.GetPixels32();
            float mx = 0;
            for (int i = 0; i < views * nD; i++) mx = Mathf.Max(mx, j.Sino[i]);
            mx = Mathf.Max(mx, 1e-3f);
            for (int k = 0; k < views; k++)
                for (int d = 0; d < nD; d++)
                {
                    float t = Mathf.Clamp01(j.Sino[k * nD + d] / mx);
                    var c = ColorMaps.Hot(t);
                    px[(j.S.NProj - 1 - k) * nD + d] = c;
                }
            sinoTex.SetPixels32(px); sinoTex.Apply();
        }

        void Redraw()
        {
            if (huLive == null) return;
            int n = S.Res;
            var px = new Color32[n * n];
            float lo = L - W / 2, inv = 1f / Mathf.Max(W, 1);
            for (int i = 0; i < px.Length; i++)
            {
                byte b = (byte)(Mathf.Clamp01((huLive[i] - lo) * inv) * 255);
                px[i] = new Color32(b, b, b, 255);
            }
            ctTex.SetPixels32(px); ctTex.Apply();
        }

        void Finish(CTJob j)
        {
            lastJob = j;
            Array.Copy(j.Hu, huLive, huLive.Length);
            UpdateSino(j, j.S.NProj);
            Redraw();
            vEff.text = F(j.MeanKeV, 1) + " / " + F(j.EffKeV, 1) + " keV";
            vMuW.text = F(j.MuWaterRef, 4) + " cm⁻¹";
            CTJob.Dose(j.S, 30f, out var ctdi, out var dlp, out var eff, out var head);
            vCtdi.text = F(ctdi, 1) + " mGy" + (head ? T(" (baş)", " (head)") : "");
            vDlp.text = F(dlp, 0) + " mGy·cm";
            vE.text = F(eff, 2) + " mSv";
            vNoise.text = F(MeasureNoise(j), 1) + " HU";
            LogStats(j);
            vStatus.text = T("Tamamlandı: ", "Done: ") + j.S.NProj + T(" projeksiyon, ", " views, ") + j.S.NDet + T(" kanal", " channels");
            CompleteTask("scan");
            if (j.S.NProj <= 60) CompleteTask("views");
            if (j.S.Poly && !j.S.Bhc) CompleteTask("bh");
            if (implant && j.S.SliceZ < 5) CompleteTask("metal");
        }

        // standard deviation in a small ROI that lies inside a uniform organ (liver / brain / heart / soft tissue)
        float MeasureNoise(CTJob j)
        {
            int n = j.S.Res;
            double s = 0, s2 = 0; int c = 0;
            for (int iy = 0; iy < n; iy += 2)
                for (int ix = 0; ix < n; ix += 2)
                {
                    var p = j.PixelToPhys(ix, iy);
                    var pr = ph.PrimAt(new Vector3(p.x, p.y, j.S.SliceZ));
                    if (pr == null) continue;
                    if (pr.Organ != "liver" && pr.Organ != "brain" && pr.Organ != "heart" && !(pr.Name == "TrunkInner")) continue;
                    float v = j.Hu[iy * n + ix];
                    s += v; s2 += v * v; c++;
                }
            if (c < 10) return 0;
            double m = s / c;
            return (float)Math.Sqrt(Math.Max(0, s2 / c - m * m));
        }

        void LogStats(CTJob j)
        {
            int n = j.S.Res;
            var sum = new double[(int)TissueId.Count]; var cnt = new int[(int)TissueId.Count];
            for (int iy = 0; iy < n; iy += 2)
                for (int ix = 0; ix < n; ix += 2)
                {
                    var p = j.PixelToPhys(ix, iy);
                    int t = (int)ph.TissueAt(new Vector3(p.x, p.y, j.S.SliceZ));
                    sum[t] += j.Hu[iy * n + ix]; cnt[t]++;
                }
            var sb = new System.Text.StringBuilder("[MP] CT HU z=" + j.S.SliceZ + " kVp=" + j.S.Kvp + " eff=" + j.EffKeV.ToString("F1") + ": ");
            for (int t = 0; t < sum.Length; t++)
                if (cnt[t] > 5) sb.Append(((TissueId)t) + "=" + (sum[t] / cnt[t]).ToString("F0") + "(exp " + j.ExpectedHu[t].ToString("F0") + ") ");
            Debug.Log(sb.ToString());
        }

        void Probe(Vector2 uv)
        {
            if (lastJob == null) return;
            int n = lastJob.S.Res;
            int ix = Mathf.Clamp((int)(uv.x * n), 0, n - 1), iy = Mathf.Clamp((int)(uv.y * n), 0, n - 1);
            // average 3x3 pixels
            float s = 0; int c = 0;
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int x = Mathf.Clamp(ix + dx, 0, n - 1), y = Mathf.Clamp(iy + dy, 0, n - 1);
                    s += huLive[y * n + x]; c++;
                }
            float hu = s / c;
            var p = lastJob.PixelToPhys(ix, iy);
            var t = ph.TissueAt(new Vector3(p.x, p.y, lastJob.S.SliceZ));
            var tis = Tissues.Get(t);
            vProbe.text = F(hu, 0) + " HU  •  " + tis.Name + " (" + T("beklenen ", "expected ") + F(lastJob.ExpectedHu[(int)t], 0) + ")";
            var ir = ctImg.ImageRect();
            probeMark.style.display = DisplayStyle.Flex;
            probeMark.style.left = ir.x + uv.x * ir.width - 6;
            probeMark.style.top = ir.y + (1 - uv.y) * ir.height - 6;
            if (hu < -500 && t == TissueId.Lung) huLung = true;
            if (hu > -150 && hu < -30 && t == TissueId.Adipose) huFat = true;
            if (hu > 300 && (t == TissueId.CorticalBone || t == TissueId.SpongyBone || t == TissueId.MixedBone)) huBone = true;
            if (huLung && huFat && huBone) CompleteTask("hu");
        }

        public override void AutoAction(string action)
        {
            if (action == "pelvis") { S.SliceZ = 2; implant = true; RebuildPhantom(); Scan(); }
            if (action == "head") { S.SliceZ = 89; PlacePhantom(true); AutoWindow(); Scan(); }
            if (action == "lung") { SetWindow(1); }
        }

        // ------------------------------------------------------------------ theory
        protected override void BuildLearn(VisualElement c)
        {
            UIKit.H(c, T("1. Projeksiyon ve Radon dönüşümü", "1. Projections and the Radon transform"));
            UIKit.P(c, T("Tüp ve dedektör hasta etrafında döner. Her açıda θ, dedektör konumu s için ölçülen zayıflama, μ(x,y)'nin bir doğru boyunca integralidir. Tüm açılardaki projeksiyonların kümesi <b>sinogram</b>dır: noktasal bir yapı sinogramda sinüs eğrisi çizer.",
                             "The tube and detector rotate around the patient. For each angle θ and detector position s, the measured attenuation is a line integral of μ(x,y). The set of all projections is the <b>sinogram</b>: a point object traces a sine curve."));
            UIKit.Eq(c, "p(θ, s) = −ln(I / I₀) = ∫ μ(x, y) dl          x·cosθ + y·sinθ = s");
            UIKit.H(c, T("2. Filtreli geri izdüşüm (FBP)", "2. Filtered back-projection (FBP)"));
            UIKit.P(c, T("Fourier dilim teoremi: bir projeksiyonun 1B Fourier dönüşümü, görüntünün 2B Fourier dönüşümünün aynı açıdaki bir dilimidir. Bu yüzden projeksiyonlar önce |f| rampa filtresiyle filtrelenir, sonra geri izdüşürülür. Filtresiz geri izdüşüm 1/r bulanıklığı üretir.",
                             "Fourier slice theorem: the 1D Fourier transform of a projection is a slice through the 2D Fourier transform of the image. So projections are first filtered with the |f| ramp, then back-projected. Unfiltered back-projection gives 1/r blur."));
            UIKit.Eq(c, "μ(x, y) = ∫₀^π [ p(θ, ·) * h ](x·cosθ + y·sinθ) dθ       H(f) = |f| · W(f)");
            UIKit.P(c, T("Pencere W(f): Ram-Lak (keskin, gürültülü), Shepp-Logan (sinc), Kosinüs, Hann (yumuşak, düşük gürültü). Keskinlik ↔ gürültü dengesi.",
                             "Window W(f): Ram-Lak (sharp, noisy), Shepp-Logan (sinc), Cosine, Hann (smooth, low noise). Sharpness ↔ noise trade-off."));
            UIKit.H(c, T("3. Hounsfield birimi ve pencereleme", "3. Hounsfield units and windowing"));
            UIKit.Eq(c, "HU = 1000 · (μ − μ_su) / (μ_su − μ_hava)");
            UIKit.P(c, T("Hava −1000, su 0, yağ ≈ −100, yumuşak doku +20…+60, kemik +300…+1500 HU. Göz yalnızca ~40–60 gri tonu ayırt edebildiği için görüntü bir pencerede (W genişlik, L seviye) gösterilir: akciğer W1500/L−600, yumuşak doku W400/L40, kemik W2000/L400, beyin W80/L35.",
                             "Air −1000, water 0, fat ≈ −100, soft tissue +20…+60, bone +300…+1500 HU. Because the eye separates only ~40–60 grey levels, images are shown through a window (width W, level L): lung W1500/L−600, soft tissue W400/L40, bone W2000/L400, brain W80/L35."));
            UIKit.H(c, T("4. Artefaktlar", "4. Artefacts"));
            UIKit.P(c, T("• <b>Işın sertleşmesi:</b> polienerjetik ışında düşük enerjiler önce soğurulur; uzun yollarda etkin μ düşer → merkezde 'çanak' (cupping) ve yoğun kemikler arasında koyu çizgiler. Su kalibrasyonu (BHC) çanağı düzeltir, kemik çizgilerini düzeltemez.\n• <b>Metal:</b> fotonların neredeyse tamamı soğurulur (foton açlığı) → parlak/koyu ışınsal çizgiler.\n• <b>Az projeksiyon:</b> açısal örnekleme yetersiz → çizgi (aliasing) artefaktı.\n• <b>Gürültü:</b> σ ~ 1/√(mAs); doz ile gürültü arasındaki temel denge.",
                             "• <b>Beam hardening:</b> low energies are absorbed first; along long paths the effective μ drops → 'cupping' in the centre and dark streaks between dense bones. Water calibration (BHC) fixes cupping but not bone streaks.\n• <b>Metal:</b> almost all photons are absorbed (photon starvation) → bright/dark radial streaks.\n• <b>Few views:</b> insufficient angular sampling → streak (aliasing) artefacts.\n• <b>Noise:</b> σ ~ 1/√(mAs); the fundamental dose–noise trade-off."));
            UIKit.H(c, T("5. Doz", "5. Dose"));
            UIKit.Eq(c, "CTDIw = ⅓·CTDI_merkez + 2/3·CTDI_çevre     CTDIvol = CTDIw / pitch     DLP = CTDIvol · L     E ≈ k · DLP");
            UIKit.Note(c, T("Model: paralel ışın geometrisi (fan ışın verisinin yeniden gruplanmış hali), enerji-integre eden dedektör, saçılma ihmal.", "Model: parallel-beam geometry (rebinned fan-beam data), energy-integrating detector, scatter neglected."));
        }
    }
}
