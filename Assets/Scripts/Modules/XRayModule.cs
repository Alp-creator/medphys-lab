using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using MedPhys.Core;
using MedPhys.Physics;
using MedPhys.UI;
using static MedPhys.Core.Loc;

namespace MedPhys.Modules
{
    /// <summary>Projection radiography: tube spectrum → filtration → patient → scatter/grid → CsI detector → image + dose.</summary>
    public sealed class XRayModule : ModuleBase
    {
        public override string Id => "xray";
        public override string Title => T("Röntgen", "X-ray Radiography");
        public override string Subtitle => T("Spektrum • Zayıflama • Saçılma • Doz", "Spectrum • Attenuation • Scatter • Dose");

        protected override (string id, string tr, string en)[] Tasks => new[]
        {
            ("kvp", "kVp etkisi: ≤65 kVp ve ≥115 kVp ile çekim yap; kemik kontrastını karşılaştır", "kVp effect: expose at ≤65 kVp and ≥115 kVp; compare bone contrast"),
            ("di", "Doğru pozlama: göğüs grafisinde sapma indeksini (DI) −1 ile +1 arasına getir", "Correct exposure: bring the deviation index (DI) of a chest image within −1…+1"),
            ("hvl", "Filtrasyon: HVL'yi 4 mm Al'ın üzerine çıkar", "Filtration: raise the HVL above 4 mm Al"),
            ("grid", "Saçılma: aynı çekimi gridli ve gridsiz yap", "Scatter: make the same exposure with and without the grid"),
            ("pace", "Kalp pilini göğüs grafisinde görüntüle", "Image the pacemaker on a chest radiograph"),
            ("alara", "ALARA: ESAK < 0.12 mGy iken DI ≥ −1 olan bir göğüs grafisi çek", "ALARA: chest image with ESAK < 0.12 mGy and DI ≥ −1"),
        };

        // settings
        readonly RadiographySettings S = new RadiographySettings();
        int region;        // 0 chest, 1 abdomen, 2 skull, 3 pelvis
        float rotation;    // patient rotation about the vertical axis (deg); 0 = PA
        bool nodule = true, pacemaker, contrast, autoExpose = true;
        int skinMode;
        float sidCm = 180f;

        // scene
        Phantom ph;
        PhantomView view;
        GameObject stand;
        Transform tube, detCenter, detFace;
        MeshRenderer beam;
        Material beamMat;
        Texture2D radTex;
        Material screenMat;
        float flash;

        // job
        RadiographyJob job;
        float pendingTimer = -1;
        bool lastGrid; float lastGridKvp = -1;
        bool kvpLow, kvpHigh;

        // ui
        ImageView image;
        Chart spec;
        Label vMean, vHvl, vEff, vOut, vEsak, vDap, vE, vEi, vDi, vSnr, vCon, vThick, vStatus;
        UIKit.ProgressBar prog;
        Label markL, markR;
        UIKit.SliderRow sKvp, sMas, sAl, sCu, sSid, sFw, sFh, sRot;

        static readonly float[] RegionZ = { 52f, 27f, 87f, 7f };
        static readonly float[] RegionDoseCoeff = { 0.12f, 0.26f, 0.03f, 0.29f };

        protected override void Build()
        {
            Env.Room(Root.transform, new Color(0.45f, 0.55f, 1f));
            var room = Res.Model("XRay_Room", Root.transform);
            tube = room.Find("XR_TubeAssembly");
            detCenter = room.Find("XR_DetectorCenter");
            detFace = room.Find("XR_DetectorFace");
            radTex = UIKit.NewTex(S.Res, S.Res);
            var clear = new Color32[S.Res * S.Res];
            for (int i = 0; i < clear.Length; i++) clear[i] = new Color32(12, 18, 26, 255);
            radTex.SetPixels32(clear); radTex.Apply();
            screenMat = Res.UnlitTex(radTex, new Color(0.9f, 0.95f, 1f));
            if (detFace != null) detFace.GetComponent<Renderer>().sharedMaterial = screenMat;

            beamMat = Res.Additive(new Color(1f, 0.92f, 0.55f, 0.35f), 1f, 0.3f);
            beam = MeshObj("Beam", new Mesh(), beamMat);

            RebuildPhantom();
            App.Rig.Frame(new Vector3(0, 1.3f, -0.9f), 3.6f, 235f, 12f, true);
            App.Rig.MinDistance = 0.8f; App.Rig.MaxDistance = 8f;
            App.Rig.PanBounds = new Bounds(new Vector3(0, 1.3f, -0.5f), new Vector3(6, 2.5f, 6));
            BuildUI();
            SetTip("Parametreleri değiştir → görüntü ve doz anında yeniden hesaplanır (polienerjetik ışın izleme + NIST verileri).",
                   "Change parameters → image and dose are recomputed (polyenergetic ray tracing + NIST data).");
            MachineParts(room);
            ScheduleExpose(0.2f);
        }

        void MachineParts(GameObject room)
        {
            Part(room.Find("XR_TubeHousing"), "X-ışını tüpü", "X-ray tube",
                 "Isıtılan katottan çıkan elektronlar kVp ile hızlanıp döner tungsten anoda çarpar. Enerjinin yalnızca ~%1'i X-ışınına (frenleme + karakteristik) dönüşür, gerisi ısıdır.",
                 "Electrons from the heated cathode are accelerated by the kVp into a rotating tungsten anode. Only ~1 % of their energy becomes X-rays (bremsstrahlung + characteristic); the rest is heat.", new Vector2(-0.6f, 1f), 100);
            Part(room.Find("XR_Collimator"), "Kolimatör + ek filtre", "Collimator + added filtration",
                 "Kurşun kepenkler ışın alanını sınırlar: daha az hasta dozu ve saçılma. Al/Cu filtre düşük enerjili fotonları süzer; ışın sertleşir, cilt dozu düşer.",
                 "Lead shutters limit the field: less patient dose and scatter. Al/Cu filtration removes low-energy photons, hardening the beam and lowering the skin dose.", new Vector2(-1f, -0.7f), 100);
            Part(room.Find("XR_BuckyBody"), "Grid (Bucky)", "Anti-scatter grid (Bucky)",
                 "Odaklanmış ince kurşun şeritler hastadan saçılan fotonları soğurur. Kontrast artar ama aynı dedektör dozu için mAs artırılmalıdır (Bucky faktörü 2–5).",
                 "Focused thin lead strips absorb photons scattered in the patient. Contrast improves, but the mAs must rise for the same detector dose (Bucky factor 2–5).", new Vector2(1f, 0.8f), 110);
            Part(detFace, "Düz panel dedektör", "Flat-panel detector",
                 "CsI:Tl sintilatör X-ışınını ışığa, a-Si fotodiyot matrisi elektrik sinyaline çevirir. Pozlama indeksi (EI) dedektöre ulaşan dozu gösterir.",
                 "A CsI:Tl scintillator turns X-rays into light and an a-Si photodiode array into charge. The exposure index (EI) reports the dose reaching the detector.", new Vector2(1f, -0.6f), 110);
            // source-to-image distance, drawn just below the central ray
            Dimension(() => tube.position + Vector3.down * 0.32f, () => detCenter.position + Vector3.down * 0.32f,
                      () => "SID = " + F(Vector3.Distance(tube.position, detCenter.position) * 100f, 0) + " cm");
            var ph = Child("PhantomCallout");
            ph.transform.position = detCenter.position + new Vector3(0, 0.05f, -0.22f);
            Part(ph.transform, "Antropomorfik fantom", "Anthropomorphic phantom",
                 "Akciğer, kemik, yumuşak doku ve kalp NIST zayıflama katsayılarıyla modellenir; her dedektör pikseli için polienerjetik ışın izlenir.",
                 "Lungs, bone, soft tissue and heart are modelled with NIST attenuation coefficients; a polyenergetic ray is traced for every detector pixel.", new Vector2(0.2f, -1f), 140);
        }

        void RebuildPhantom()
        {
            if (view != null) UnityEngine.Object.Destroy(view.gameObject);
            if (stand != null) UnityEngine.Object.Destroy(stand);
            ph = Phantom.BuildAdult(new PhantomOptions { Tumor = nodule, TumorRadius = 1.3f, Pacemaker = pacemaker, Contrast = contrast });
            view = PhantomView.Create(ph, Root.transform);
            view.SetSkinMode(skinMode);
            PlacePhantom();
        }

        void PlacePhantom()
        {
            if (view == null) return;
            float detZ = detCenter != null ? detCenter.position.z : 0.0125f;
            float detY = detCenter != null ? detCenter.position.y : 1.45f;
            float rootY = detY - RegionZ[region] * 0.01f;
            // anterior towards the detector for rotation 0 (PA)
            var ant = Quaternion.Euler(0, rotation, 0) * Vector3.forward;
            // half thickness of the trunk along the beam direction (ellipse 19 x 10.5 cm)
            float a = 0.19f, b = 0.105f;
            float th = Mathf.Abs(Mathf.Cos(rotation * Mathf.Deg2Rad)), sh = Mathf.Abs(Mathf.Sin(rotation * Mathf.Deg2Rad));
            float halfDepth = Mathf.Sqrt(b * b * th * th + a * a * sh * sh);
            if (region == 2) halfDepth = 0.10f;
            var pos = new Vector3(0, rootY, detZ - halfDepth - 0.025f);
            view.Orient(pos, Vector3.up, ant);
            if (stand != null) UnityEngine.Object.Destroy(stand);
            stand = Env.Stand(Root.transform, new Vector3(pos.x, 0, pos.z), Mathf.Max(0.1f, rootY - 0.22f));
            UpdateBeam();
        }

        void UpdateTubePosition()
        {
            if (tube == null || detCenter == null) return;
            var p = tube.position;
            tube.position = new Vector3(p.x, p.y, detCenter.position.z - sidCm * 0.01f);
            UpdateBeam();
        }

        void UpdateBeam()
        {
            if (tube == null || detCenter == null) return;
            var d = detCenter.position;
            float fw = S.FieldWcm * 0.005f, fh = S.FieldHcm * 0.005f;
            var corners = new[] {
                d + new Vector3(-fw, -fh, 0), d + new Vector3(fw, -fh, 0), d + new Vector3(fw, fh, 0), d + new Vector3(-fw, fh, 0) };
            var local = new Vector3[4];
            for (int i = 0; i < 4; i++) local[i] = beam.transform.InverseTransformPoint(corners[i]);
            var m = MeshGen.Beam(beam.transform.InverseTransformPoint(tube.position), local, 0.15f, 1f);
            beam.GetComponent<MeshFilter>().sharedMesh = m;
        }

        // ------------------------------------------------------------------ UI
        void BuildUI()
        {
            var L = LeftScroll.contentContainer;
            var sec = UIKit.Section(L, T("Bölge ve projeksiyon", "Region and projection"));
            UIKit.Seg(sec, null, new[] { T("Göğüs", "Chest"), T("Karın", "Abdomen"), T("Kafa", "Skull"), T("Pelvis", "Pelvis") }, region, i =>
            {
                region = i; S.EffDoseCoeff = RegionDoseCoeff[i];
                ApplyPreset(i);
                PlacePhantom(); ScheduleExpose();
            });
            UIKit.Seg(sec, null, new[] { "PA", "AP", "LAT", "OBL 45°" }, 0, i =>
            {
                rotation = i == 0 ? 0 : i == 1 ? 180 : i == 2 ? 90 : 45;
                sRot.Set(rotation);
                PlacePhantom(); ScheduleExpose();
            });
            sRot = UIKit.Slider(sec, T("Hasta dönüşü", "Patient rotation"), 0, 359, rotation, v => F(v, 0) + "°", v => { rotation = v; PlacePhantom(); ScheduleExpose(); }, 1);

            sec = UIKit.Section(L, T("Röntgen tüpü (jeneratör)", "X-ray tube (generator)"));
            sKvp = UIKit.Slider(sec, T("Tüp gerilimi", "Tube voltage"), 40, 150, S.Kvp, v => F(v, 0) + " kVp", v => { S.Kvp = v; ScheduleExpose(); }, 1);
            sMas = UIKit.Slider(sec, T("Tüp akımı × süre", "Tube current × time"), 0, 1, MasToT(S.Mas), v => F(TToMas(v), TToMas(v) < 10 ? 2 : 1) + " mAs", v => { S.Mas = TToMas(v); ScheduleExpose(); });
            sAl = UIKit.Slider(sec, T("Ek Al filtre", "Added Al filter"), 0, 6, S.AlMm, v => F(v, 1) + " mm", v => { S.AlMm = v; ScheduleExpose(); }, 0.1f);
            sCu = UIKit.Slider(sec, T("Ek Cu filtre", "Added Cu filter"), 0, 0.3f, S.CuMm, v => F(v, 2) + " mm", v => { S.CuMm = v; ScheduleExpose(); }, 0.05f);
            UIKit.Note(sec, T("Toplam filtrasyon = 0.8 mm Al (doğal) + ek filtre. Yönetmelik: ≥ 2.5 mm Al (>70 kVp).", "Total filtration = 0.8 mm Al (inherent) + added. Regulation: ≥ 2.5 mm Al (>70 kVp)."));

            sec = UIKit.Section(L, T("Geometri ve dedektör", "Geometry and detector"));
            sSid = UIKit.Slider(sec, T("Kaynak–görüntü mesafesi (SID)", "Source–image distance (SID)"), 100, 200, sidCm, v => F(v, 0) + " cm", v => { sidCm = v; UpdateTubePosition(); ScheduleExpose(); }, 5);
            sFw = UIKit.Slider(sec, T("Alan genişliği", "Field width"), 8, 43, S.FieldWcm, v => F(v, 0) + " cm", v => { S.FieldWcm = v; UpdateBeam(); ScheduleExpose(); }, 1);
            sFh = UIKit.Slider(sec, T("Alan yüksekliği", "Field height"), 8, 43, S.FieldHcm, v => F(v, 0) + " cm", v => { S.FieldHcm = v; UpdateBeam(); ScheduleExpose(); }, 1);
            UIKit.Toggle(sec, T("Saçılma önleyici grid (10:1)", "Anti-scatter grid (10:1)"), S.Grid, v => { S.Grid = v; ScheduleExpose(); });

            sec = UIKit.Section(L, T("Fantom", "Phantom"));
            UIKit.Toggle(sec, T("Akciğerde nodül (tümör)", "Lung nodule (tumour)"), nodule, v => { nodule = v; RebuildPhantom(); ScheduleExpose(); });
            UIKit.Toggle(sec, T("Kalp pili (titanyum)", "Pacemaker (titanium)"), pacemaker, v => { pacemaker = v; RebuildPhantom(); ScheduleExpose(); });
            UIKit.Toggle(sec, T("İyotlu kontrast (kalp/aorta)", "Iodine contrast (heart/aorta)"), contrast, v => { contrast = v; RebuildPhantom(); ScheduleExpose(); });
            UIKit.Seg(sec, T("Cilt görünümü", "Skin view"), new[] { T("Hologram", "Hologram"), T("Opak", "Opaque"), T("Gizli", "Hidden") }, skinMode, i => { skinMode = i; view.SetSkinMode(i); });

            sec = UIKit.Section(L, T("Çekim", "Exposure"));
            var row = UIKit.ButtonRow(sec);
            UIKit.Button(row, T("ÇEK  (Expose)", "EXPOSE"), () => Expose(), "mp-btn-primary");
            UIKit.Toggle(sec, T("Parametre değişince otomatik çek", "Auto-expose on change"), autoExpose, v => autoExpose = v);
            prog = UIKit.Progress(sec);
            vStatus = UIKit.Text(sec, "");
            BuildTasks(L);

            // ---- results
            var R = RightScroll.contentContainer;
            sec = UIKit.Section(R, T("Dijital radyografi (CsI dedektör)", "Digital radiograph (CsI detector)"));
            image = new ImageView(App.SmallScreen ? 300 : 400, "");
            markL = new Label("") { pickingMode = PickingMode.Ignore }; markR = new Label("") { pickingMode = PickingMode.Ignore };
            foreach (var mk in new[] { markL, markR })
            {
                mk.style.position = Position.Absolute; mk.style.top = 6; mk.style.fontSize = 18; mk.style.color = new Color(1, 0.85f, 0.3f);
                mk.style.unityFontStyleAndWeight = FontStyle.Bold;
                image.Overlay.Add(mk);
            }
            markL.style.left = 10; markR.style.right = 10;
            image.Set(radTex);
            sec.Add(image);
            UIKit.Note(sec, T("Parlak = yüksek zayıflama (kemik, metal). Görüntü radyolojik kurala göre gösterilir: hastanın sağı solda. Pencere otomatik ayarlanır.",
                              "Bright = high attenuation (bone, metal). Shown in radiological convention: patient's right on the left. Window is set automatically."));

            sec = UIKit.Section(R, T("Spektrum", "Spectrum"));
            spec = new Chart(200) { XMin = 0, XMax = 150, XLabel = "keV", YLabel = T("bağıl foton sayısı", "relative photons") };
            spec.Add(T("Filtresiz", "Unfiltered"), new Color(0.6f, 0.65f, 0.75f, 0.8f), 1.5f);
            var f2 = spec.Add(T("Filtreli (hastaya giren)", "Filtered (entrance)"), new Color(0.3f, 0.8f, 1f), 2f); f2.Fill = true;
            spec.Add(T("Dedektöre ulaşan (merkez)", "At detector (centre)"), new Color(1f, 0.7f, 0.25f), 2f);
            sec.Add(spec.Legend()); sec.Add(spec);
            vMean = UIKit.KV(sec, T("Ortalama enerji (giriş / dedektör)", "Mean energy (entrance / detector)"));
            vHvl = UIKit.KV(sec, T("Yarı değer kalınlığı (HVL)", "Half-value layer (HVL)"));
            vEff = UIKit.KV(sec, T("Efektif enerji", "Effective energy"));
            vOut = UIKit.KV(sec, T("Tüp çıkışı (1 m)", "Tube output (1 m)"));

            sec = UIKit.Section(R, T("Görüntü kalitesi", "Image quality"));
            vEi = UIKit.KV(sec, T("Pozlama indeksi (EI)", "Exposure index (EI)"));
            vDi = UIKit.KV(sec, T("Sapma indeksi (DI)", "Deviation index (DI)"));
            vSnr = UIKit.KV(sec, T("SNR (merkez)", "SNR (centre)"));
            vCon = UIKit.KV(sec, T("Kemik kontrastı (1 cm / 20 cm doku)", "Bone contrast (1 cm in 20 cm tissue)"));

            sec = UIKit.Section(R, T("Hasta dozu", "Patient dose"));
            vThick = UIKit.KV(sec, T("Hasta kalınlığı / SSD", "Patient thickness / SSD"));
            vEsak = UIKit.KV(sec, T("Giriş yüzey hava kerması (ESAK)", "Entrance surface air kerma (ESAK)"));
            vDap = UIKit.KV(sec, T("Doz-alan çarpımı (DAP)", "Dose-area product (DAP)"));
            vE = UIKit.KV(sec, T("Efektif doz (yaklaşık)", "Effective dose (approx.)"));
            UIKit.Note(sec, T("Efektif doz = DAP × dönüşüm katsayısı (bölgeye göre). Doğal radyasyon ≈ 2.4 mSv/yıl.", "Effective dose = DAP × conversion coefficient (by region). Natural background ≈ 2.4 mSv/yr."));
            ApplyPreset(0);
        }

        void ApplyPreset(int r)
        {
            // typical DR techniques
            float kvp = r == 0 ? 120 : r == 1 ? 80 : r == 2 ? 70 : 80;
            float mas = r == 0 ? 2.0f : r == 1 ? 20f : r == 2 ? 16f : 22f;
            float sid = r == 0 ? 180 : 110;
            float fw = r == 0 ? 35 : r == 2 ? 24 : 35, fh = r == 0 ? 43 : r == 2 ? 30 : 30;
            S.Kvp = kvp; S.Mas = mas; sidCm = sid; S.FieldWcm = fw; S.FieldHcm = fh;
            S.EffDoseCoeff = RegionDoseCoeff[r];
            sKvp?.Set(kvp); sMas?.Set(MasToT(mas)); sSid?.Set(sid); sFw?.Set(fw); sFh?.Set(fh);
            UpdateTubePosition();
        }

        static float TToMas(float t) => 0.5f * Mathf.Pow(400f, t);
        static float MasToT(float m) => Mathf.Log(m / 0.5f) / Mathf.Log(400f);

        void ScheduleExpose(float delay = 0.35f)
        {
            if (!autoExpose && delay > 0.25f) return;
            pendingTimer = delay;
        }

        void Expose()
        {
            if (job != null && !job.Done) { pendingTimer = 0.1f; return; }
            S.Seed++;
            job = new RadiographyJob
            {
                Ph = ph, S = new RadiographySettings
                {
                    Kvp = S.Kvp, Mas = S.Mas, AlMm = S.AlMm, CuMm = S.CuMm, Grid = S.Grid, Res = S.Res, DetectorCm = S.DetectorCm,
                    FieldWcm = S.FieldWcm, FieldHcm = S.FieldHcm, CsICm = S.CsICm, EiTarget = S.EiTarget, EffDoseCoeff = S.EffDoseCoeff, Seed = S.Seed
                },
                Source = view.WorldToPhys(tube.position),
                DetCenter = view.WorldToPhys(detCenter.position),
                U = view.WorldDirToPhys(Vector3.right),
                V = view.WorldDirToPhys(Vector3.up),
            };
            job.Start();
            flash = 1f;
            vStatus.text = T("Hesaplanıyor… (" + S.Res + "×" + S.Res + " piksel ışın izleme)", "Computing… (" + S.Res + "×" + S.Res + " pixel ray tracing)");
        }

        public override void Tick(float dt)
        {
            base.Tick(dt);
            if (pendingTimer >= 0)
            {
                pendingTimer -= dt;
                if (pendingTimer < 0) Expose();
            }
            if (job != null)
            {
                prog.Set(job.Done ? 1f : job.RowsDone / (float)Mathf.Max(1, job.Res));
                if (job.Done)
                {
                    var j = job; job = null;
                    if (j.Failed) { vStatus.text = "Error: " + j.Error; Debug.LogError(j.Error); }
                    else ShowResult(j);
                }
            }
            // beam glow: light field (yellow) at rest, X-ray flash during exposure
            flash = Mathf.Max(0, flash - dt * 1.8f);
            var c = Color.Lerp(new Color(1f, 0.92f, 0.55f, 0.22f), new Color(0.55f, 0.6f, 1f, 1.6f), flash);
            beamMat.SetColor("_Color", c);
        }

        void ShowResult(RadiographyJob j)
        {
            // radiological display convention: the patient's right appears on the viewer's left
            var rightW = view.PhysDirToWorld(new Vector3(-1, 0, 0));
            bool rightOnPlusX = Vector3.Dot(rightW, Vector3.right) > 0;
            float lateral = Mathf.Abs(Vector3.Dot(rightW, Vector3.right));
            bool flip = rightOnPlusX && lateral >= 0.3f;
            var disp = j.Display;
            if (flip)
            {
                disp = new float[j.Display.Length];
                for (int y = 0; y < j.Res; y++)
                    for (int x = 0; x < j.Res; x++) disp[y * j.Res + x] = j.Display[y * j.Res + (j.Res - 1 - x)];
            }
            UIKit.Gray(radTex, disp, j.Res, j.Res);
            markL.text = lateral < 0.3f ? "" : "R";
            markR.text = lateral < 0.3f ? "" : "L";

            var unf = XRaySpectrum.Unfiltered(j.S.Kvp, 2f);
            SetSpectrum(spec.AllSeries[0], unf);
            SetSpectrum(spec.AllSeries[1], j.Spectrum);
            SetSpectrum(spec.AllSeries[2], j.DetSpectrum);
            spec.XMax = Mathf.Max(60, Mathf.Ceil(j.S.Kvp / 10f) * 10);
            spec.Refresh();

            double hvl = j.Spectrum.HvlAlMm();
            vMean.text = F(j.Spectrum.MeanEnergy(), 1) + " / " + F(j.MeanEnergyDet, 1) + " keV";
            vHvl.text = F(hvl, 2) + " mm Al";
            vEff.text = F(j.Spectrum.EffectiveEnergy(), 1) + " keV";
            vOut.text = F(j.Spectrum.AirKermaPerMas(), 1) + " µGy/mAs";
            vEi.text = F(j.Ei, 0) + "  (" + T("hedef ", "target ") + F(j.S.EiTarget, 0) + ")";
            vDi.text = (j.Di >= 0 ? "+" : "") + F(j.Di, 2) + "  " + (Math.Abs(j.Di) <= 1 ? T("uygun", "OK") : j.Di > 0 ? T("fazla doz", "over-exposed") : T("az pozlanmış / gürültülü", "under-exposed / noisy"));
            UIKit.SetStatus(vDi, Math.Abs(j.Di) <= 1 ? 0 : Math.Abs(j.Di) <= 3 ? 1 : 2);
            vSnr.text = F(j.Snr, 0);
            vCon.text = F(j.BoneContrast * 100, 1) + " %";
            vThick.text = F(j.ThicknessCm, 1) + " cm / " + F(j.SsdCm, 0) + " cm";
            vEsak.text = Format(j.EsakMGy * 1000, "µGy", "mGy");
            vDap.text = F(j.DapGyCm2, 3) + " Gy·cm²";
            double days = j.EffDoseMSv / 2.4 * 365.0;
            vE.text = F(j.EffDoseMSv * 1000, 1) + " µSv  (≈ " + F(days, 1) + T(" gün doğal radyasyon)", " days background)");
            vStatus.text = T("Tamamlandı. ", "Done. ") + F(j.S.Kvp, 0) + " kVp, " + F(j.S.Mas, 1) + " mAs";
            // tasks
            if (j.S.Kvp <= 65) kvpLow = true;
            if (j.S.Kvp >= 115) kvpHigh = true;
            if (kvpLow && kvpHigh) CompleteTask("kvp");
            if (region == 0 && Math.Abs(j.Di) <= 1) CompleteTask("di");
            if (hvl > 4) CompleteTask("hvl");
            if (lastGridKvp >= 0 && Math.Abs(lastGridKvp - j.S.Kvp) < 0.5f && lastGrid != j.S.Grid) CompleteTask("grid");
            lastGrid = j.S.Grid; lastGridKvp = j.S.Kvp;
            if (pacemaker && region == 0) CompleteTask("pace");
            if (region == 0 && j.EsakMGy < 0.12 && j.Di >= -1) CompleteTask("alara");
        }

        static string Format(double micro, string u1, string u2) => micro < 1000 ? F(micro, 1) + " " + u1 : F(micro / 1000, 3) + " " + u2;

        static void SetSpectrum(Chart.Series s, XRaySpectrum sp)
        {
            s.Pts.Clear();
            double mx = 0;
            for (int i = 0; i < sp.Count; i++) mx = Math.Max(mx, sp.N[i]);
            if (mx <= 0) mx = 1;
            for (int i = 0; i < sp.Count; i++) s.Pts.Add(new Vector2((float)sp.E[i], (float)(sp.N[i] / mx)));
        }

        public override void AutoAction(string action)
        {
            if (action == "expose") Expose();
            if (action == "lateral") { rotation = 90; PlacePhantom(); Expose(); }
            if (action == "skull") { region = 2; ApplyPreset(2); PlacePhantom(); Expose(); }
        }

        // ------------------------------------------------------------------ theory
        protected override void BuildLearn(VisualElement c)
        {
            UIKit.H(c, T("1. X-ışını üretimi", "1. X-ray production"));
            UIKit.P(c, T("Katottan çıkan elektronlar tüp gerilimiyle (kVp) hızlanır ve tungsten anoda çarpar. Enerjinin ~%99'u ısıya dönüşür; ~%1'i X-ışınıdır. Elektronlar çekirdek alanında yavaşlarken <b>frenleme (bremsstrahlung)</b> ışıması sürekli bir spektrum oluşturur; maksimum foton enerjisi kVp'ye eşittir.",
                             "Electrons from the cathode are accelerated by the tube voltage (kVp) onto a tungsten anode. ~99% of the energy becomes heat, ~1% X-rays. Electrons decelerating in nuclear fields emit <b>bremsstrahlung</b> with a continuous spectrum up to E_max = kVp."));
            UIKit.Eq(c, "Kramers:   N(E) dE ~ Z · (E₀ − E) / E · dE");
            UIKit.P(c, T("kVp, W'nin K-kabuk bağlanma enerjisini (69.5 keV) aştığında <b>karakteristik</b> çizgiler oluşur: Kα ≈ 58–59 keV, Kβ ≈ 67–69 keV. Şiddet ~ (U − 1)^1.63, U = kVp / E_K.",
                             "When kVp exceeds the W K-shell binding energy (69.5 keV), <b>characteristic</b> lines appear: Kα ≈ 58–59 keV, Kβ ≈ 67–69 keV. Intensity ~ (U − 1)^1.63, U = kVp / E_K."));
            UIKit.P(c, T("Tüp çıkışı yaklaşık olarak kVp² ve mAs ile orantılıdır: kVp'yi %15 artırmak, görüntü dedektördeki sinyali kabaca iki katına çıkarır (15% kuralı).",
                             "Tube output scales roughly with kVp² and linearly with mAs: +15% kVp roughly doubles the detector signal (15% rule)."));

            UIKit.H(c, T("2. Zayıflama ve filtrasyon", "2. Attenuation and filtration"));
            UIKit.Eq(c, "I(x) = I₀ · e^(−μ(E)·x)      μ = (μ/ρ) · ρ");
            UIKit.P(c, T("Tanı enerjilerinde iki etkileşim baskındır: <b>fotoelektrik olay</b> (~ Z³/E³, kontrastın kaynağı) ve <b>Compton saçılması</b> (elektron yoğunluğuyla orantılı, saçılmanın kaynağı). Simülasyon her malzeme için NIST μ/ρ(E) tablolarını kullanır.",
                             "Two interactions dominate at diagnostic energies: the <b>photoelectric effect</b> (~ Z³/E³, source of contrast) and <b>Compton scattering</b> (~ electron density, source of scatter). The simulation uses NIST μ/ρ(E) tables for every material."));
            UIKit.P(c, T("Filtre düşük enerjili fotonları seçici olarak soğurur (<b>ışın sertleşmesi</b>): bu fotonlar görüntüye katkı yapmadan sadece cilt dozunu artırırdı. Filtre arttıkça ortalama enerji ve HVL büyür, çıkış düşer.",
                             "A filter preferentially removes low-energy photons (<b>beam hardening</b>) that would only add skin dose. More filtration raises mean energy and HVL and lowers the output."));
            UIKit.Eq(c, "HVL: K(x = HVL) = K₀ / 2      (" + T("hava kermasında", "in air kerma") + ")");

            UIKit.H(c, T("3. Görüntü oluşumu", "3. Image formation"));
            UIKit.P(c, T("Her piksel için kaynaktan dedektöre ışın izlenir; ışının her dokuda aldığı yol (cm) hesaplanır ve her enerji aralığında Beer–Lambert uygulanır. CsI dedektörün soğurma verimi η(E) = 1 − e^(−μ_CsI·t). Sinyal enerji ağırlıklıdır (enerji integre eden dedektör).",
                             "For every pixel a ray is traced from the focal spot to the detector; the path length in each tissue is computed and Beer–Lambert is applied per energy bin. CsI absorption efficiency η(E) = 1 − e^(−μ_CsI·t). The signal is energy-weighted (energy-integrating detector)."));
            UIKit.Eq(c, "S = Σ_E Φ(E) · e^(−Σ μ_i(E) L_i) · η(E) · E      σ² = Σ_E Φ T η E²");
            UIKit.P(c, T("<b>Saçılma</b> kontrastı düşürür: S/P oranı alan boyutu ve hasta kalınlığıyla artar. Grid birincil ışının ~%70'ini, saçılmanın ~%12'sini geçirir. Kolimasyon (küçük alan) hem saçılmayı hem dozu azaltır.",
                             "<b>Scatter</b> reduces contrast: the scatter-to-primary ratio grows with field size and patient thickness. A grid transmits ~70% of primary and ~12% of scatter. Collimation reduces both scatter and dose."));
            UIKit.P(c, T("<b>Pozlama indeksi</b> EI = 100 × K_dedektör [µGy] (IEC 62494-1). Sapma indeksi DI = 10·log₁₀(EI / EI_hedef); |DI| ≤ 1 uygun pozlamadır. DI = +3 iki kat fazla doz demektir.",
                             "<b>Exposure index</b> EI = 100 × K_detector [µGy] (IEC 62494-1). Deviation index DI = 10·log₁₀(EI / EI_target); |DI| ≤ 1 is appropriate. DI = +3 means twice the needed dose."));

            UIKit.H(c, T("4. Dozimetri", "4. Dosimetry"));
            UIKit.Eq(c, "K_air = Σ Φ(E) · E · (μ_en/ρ)_air        ESAK = K(1 m) · mAs · (100/SSD)² · BSF");
            UIKit.P(c, T("Geri saçılma faktörü BSF ≈ 1.35. DAP (Gy·cm²) mesafeden bağımsızdır ve efektif doza dönüşüm katsayılarıyla çevrilir: göğüs PA ≈ 0.12, karın ≈ 0.26, pelvis ≈ 0.29, kafa ≈ 0.03 mSv/(Gy·cm²). Tipik göğüs PA: ESAK ~0.1–0.2 mGy, E ~0.02 mSv.",
                             "Backscatter factor BSF ≈ 1.35. DAP (Gy·cm²) is independent of distance and is converted to effective dose with coefficients: chest PA ≈ 0.12, abdomen ≈ 0.26, pelvis ≈ 0.29, skull ≈ 0.03 mSv/(Gy·cm²). Typical chest PA: ESAK ~0.1–0.2 mGy, E ~0.02 mSv."));
            UIKit.P(c, T("<b>Ters kare yasası:</b> şiddet ~ 1/d². SID 100 → 180 cm olduğunda aynı dedektör dozu için mAs'ın ~3.2 kat artması gerekir; ancak cilt dozu daha az artar ve geometrik büyütme azalır.",
                             "<b>Inverse square law:</b> intensity ~ 1/d². Going from SID 100 to 180 cm needs ~3.2× mAs for the same detector dose, but skin dose rises less and magnification drops."));
            UIKit.Note(c, T("Model notu: saçılma, alan ve kalınlığa bağlı basitleştirilmiş bir çekirdekle hesaplanır; detektör DQE ve odak noktası bulanıklığı ihmal edilmiştir.", "Model note: scatter uses a simplified kernel depending on field and thickness; detector DQE and focal-spot blur are neglected."));
        }
    }
}
