using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using MedPhys.Core;
using MedPhys.Physics;
using MedPhys.UI;
using static MedPhys.Core.Loc;

namespace MedPhys.Modules
{
    /// <summary>Radiation protection: time, distance and shielding around a gamma source, with missions.</summary>
    public sealed class ProtectionModule : ModuleBase
    {
        public override string Id => "protection";
        public override string Title => T("Radyasyondan Korunma", "Radiation Protection");
        public override string Subtitle => T("Zaman • Mesafe • Zırhlama", "Time • Distance • Shielding");

        protected override (string id, string tr, string en)[] Tasks => new[]
        {
            ("isq", "Ters kare yasası: ~1 m, ~2 m ve ~4 m'de ölçüm kaydet", "Inverse square law: record readings at ~1 m, ~2 m and ~4 m"),
            ("desk", "Kontrol masasında (yeşil işaret) doz hızını 1 µSv/h'nin altına indir", "Bring the dose rate at the control desk (green mark) below 1 µSv/h"),
            ("hvl", "Kurşun kalınlığını Cs-137 için HVL'ye (≈ 0.6 cm) ayarla ve yarıya inişi gör", "Set the lead thickness to the Cs-137 HVL (≈ 0.6 cm) and see the halving"),
            ("retrieve", "Kaynağı kurşun kaba yerleştir; toplam doz < 20 µSv", "Put the source into the lead container; total dose < 20 µSv"),
            ("compare", "Co-60 ve Tc-99m'nin 1 m'deki doz hızlarını karşılaştır", "Compare the dose rates of Co-60 and Tc-99m at 1 m"),
        };

        static int isoIdx;
        static float activity = 370;       // MBq
        static readonly string[] IsoNames = { "Cs-137", "Co-60", "Ir-192", "Tc-99m", "F-18", "I-131" };
        static ShieldMat shieldMat = ShieldMat.Lead;
        static float shieldCm = 1f;
        Isotope iso;

        // world (top view uses x,z)
        Vector3 sourcePos = new Vector3(0, 0.92f, 0.8f);
        readonly Vector3 pigPos = new Vector3(-3.4f, 0.0f, 2.6f);
        readonly Vector3 deskPos = new Vector3(3.6f, 0, -2.6f);
        Transform worker, sourceT, meterT;
        Vector3 walkTarget;
        bool placingShield, carrying, inPig, useTongs = true;
        readonly List<Shield> shields = new List<Shield>();
        double totalDose;      // µSv
        double rate;           // µSv/h at the worker
        double missionDose = -1;
        float timeScale = 1f;  // simulated seconds per real second

        Texture2D heatTex;
        LineBatch rays;
        System.Random rng = new System.Random(9);
        AudioSource audio;
        AudioClip click;
        double clickAcc;
        Label vRate, vTotal, vDist, vMission, vHvl, vTvl, vNeeded;
        UIKit.ProgressBar limitBar;
        Chart isqChart;
        readonly List<Vector2> readings = new List<Vector2>();
        bool coRead, tcRead;
        float heatT;

        protected override void Build()
        {
            iso = Isotopes.Get(IsoNames[isoIdx]);
            Env.Room(Root.transform, new Color(1f, 0.75f, 0.2f));
            // source on a stand
            var stand = Env.Stand(Root.transform, new Vector3(sourcePos.x, 0, sourcePos.z), 0.88f);
            var src = MeshObj("Source", MeshGen.Ellipsoid(Vector3.zero, Vector3.one * 0.03f, Quaternion.identity, 12, 16), Res.Emissive(new Color(1f, 0.8f, 0.2f), new Color(4f, 2.6f, 0.4f)));
            sourceT = src.transform; sourceT.position = sourcePos;
            var halo = MeshObj("Halo", MeshGen.Ellipsoid(Vector3.zero, Vector3.one * 0.12f, Quaternion.identity, 12, 16), Res.Additive(new Color(1f, 0.8f, 0.2f, 0.4f), 1f, 0.8f), sourceT);
            var pig = Res.Model("LeadPig", Root.transform, pigPos, 0);
            pig.transform.localScale = Vector3.one * 2.2f;
            var deskMark = MeshObj("DeskMark", MeshGen.Ring(0.35f, 0.04f), Res.Additive(new Color(0.3f, 1f, 0.4f, 0.9f), 2f));
            deskMark.transform.position = deskPos + Vector3.up * 0.02f; deskMark.transform.rotation = Quaternion.Euler(90, 0, 0);
            var desk = GameObject.CreatePrimitive(PrimitiveType.Cube);
            UnityEngine.Object.Destroy(desk.GetComponent<Collider>());
            desk.transform.SetParent(Root.transform, false);
            desk.transform.position = deskPos + new Vector3(0.8f, 0.4f, 0); desk.transform.localScale = new Vector3(0.7f, 0.8f, 1.4f);
            desk.GetComponent<Renderer>().sharedMaterial = Res.Mat("M_ShellGrey");
            Label3D(deskPos + Vector3.up * 1.2f, T("Kontrol masası", "Control desk"));
            Label3D(pigPos + Vector3.up * 0.6f, T("Kurşun kap", "Lead container"));
            Label3D(sourcePos + Vector3.up * 0.25f, iso.Name + " " + F(activity, 0) + " MBq", sourceT);

            BuildWorker();
            Part(pig.transform, "Kurşun kap", "Lead container",
                 "Kaynak buraya konunca birkaç cm kurşun doz hızını yüzlerce kat azaltır (her HVL yarıya, her TVL onda bire indirir).",
                 "Once the source is inside, a few cm of lead cut the dose rate hundreds of times (each HVL halves it, each TVL divides it by ten).", new Vector2(1f, 0.6f), 100);
            Part(stand.transform, "Nokta kaynak", "Point source",
                 "Doz hızı Ḣ = Γ·A/d² ile mesafenin karesiyle azalır. Zemindeki renkler bu doz hızını, beyaz çizgi 1 µSv/h sınırını gösterir.",
                 "The dose rate falls with the square of the distance, Ḣ = Γ·A/d². The floor colours show this dose rate; the white line marks 1 µSv/h.", new Vector2(-1f, 0.5f), 110);
            Part(meterT, "Radyasyon ölçer", "Survey meter",
                 "Çalışanın taşıdığı ölçer bulunduğu noktadaki ortam doz eşdeğeri hızını H*(10) gösterir; birikmiş doz = doz hızı × süre.",
                 "The worker's meter reads the ambient dose-equivalent rate H*(10) where it is; accumulated dose = dose rate × time.", new Vector2(0.4f, 1f), 100);
            // distance source → worker (inverse square law), always shown while the source is out in the room
            Dimension(() => new Vector3(sourcePos.x, 0.03f, sourcePos.z), () => new Vector3(worker.position.x, 0.03f, worker.position.z),
                      () => carrying || inPig ? null : "d = " + F(Vector2.Distance(new Vector2(sourcePos.x, sourcePos.z), new Vector2(worker.position.x, worker.position.z)), 2) + " m", true);
            // floor heat map
            heatTex = UIKit.NewTex(80, 80);
            var floor = MeshObj("Heat", MeshGen.Quad(11f, 11f), Res.UnlitTex(heatTex, Color.white, true));
            // same mapping as RecomputeHeat: t = (log10(Ḣ) + 1) / 4 over 0.1 … 1000 µSv/h
            MapLegend(UIKit.ColorBar(ColorMaps.DoseJet,
                T("Zemin: doz hızı H*(10), µSv/h (log ölçek) • beyaz çizgi = 1 µSv/h", "Floor: dose rate H*(10), µSv/h (log scale) • white line = 1 µSv/h"),
                (0f, "0.1"), (0.25f, "1"), (0.5f, "10"), (0.75f, "100"), (1f, "1000")));
            floor.transform.position = new Vector3(0, 0.012f, 0); floor.transform.rotation = Quaternion.Euler(90, 0, 0);
            rays = new LineBatch();
            MeshObj("GammaRays", rays.Mesh, Res.Additive(Color.white, 1.5f));
            // Geiger clicks
            audio = worker.gameObject.AddComponent<AudioSource>();
            audio.playOnAwake = false; audio.spatialBlend = 0; audio.volume = 0.5f;
            click = MakeClick();

            App.Rig.Frame(worker.position + Vector3.up * 0.8f, 4.2f, 30f, 28f, true);
            App.Rig.MinDistance = 1.5f; App.Rig.MaxDistance = 9.5f;
            App.Rig.OnTap += OnTap;
            BuildUI();
            RecomputeHeat();
            SetTip("Yere dokun → yürü (klavye: WASD). Ölçerin doz hızını izle. Zaman, mesafe ve zırh dozu azaltır.",
                   "Tap the floor → walk (keyboard: WASD). Watch the meter's dose rate. Time, distance and shielding reduce dose.");
        }

        HumanRig rig;
        Transform grip;
        Vector3 lastWorkerPos;

        void BuildWorker()
        {
            worker = Child("Worker").transform;
            worker.position = new Vector3(2.5f, 0, -1.2f);
            walkTarget = worker.position;
            lastWorkerPos = worker.position;
            // Blender model faces -Z after import; the wrapper turns it to face the worker's +Z
            var model = Res.Model("Worker", worker, Vector3.zero, 180f);
            var skin = Res.Lit(new Color(0.86f, 0.66f, 0.54f), 0.45f);
            var top = Res.Fabric(new Color(0.10f, 0.48f, 0.55f));
            var pants = Res.Fabric(new Color(0.07f, 0.33f, 0.40f));
            var hair = Res.Lit(new Color(0.12f, 0.08f, 0.06f), 0.45f);
            var eye = Res.Lit(new Color(0.03f, 0.03f, 0.04f), 0.9f);
            var shoe = Res.Lit(new Color(0.92f, 0.93f, 0.95f), 0.6f);
            var badge = Res.Emissive(new Color(1f, 0.85f, 0.2f), new Color(1.5f, 1.1f, 0.2f));
            var eyeWhite = Res.Lit(new Color(0.97f, 0.97f, 0.97f), 0.85f);
            var mouth = Res.Lit(new Color(0.55f, 0.10f, 0.12f), 0.6f);
            var teeth = Res.Lit(new Color(0.98f, 0.98f, 0.96f), 0.7f);
            var blush = Res.Lit(new Color(0.95f, 0.55f, 0.52f), 0.4f);
            foreach (var r in model.GetComponentsInChildren<Renderer>(true))
            {
                var sm = r.sharedMaterials;
                for (int i = 0; i < sm.Length; i++)
                {
                    string n = sm[i] != null ? sm[i].name : "";
                    sm[i] = n.StartsWith("M_ScrubsDark") ? pants : n.StartsWith("M_Scrubs") ? top : n.StartsWith("M_Skin") ? skin
                          : n.StartsWith("M_Hair") ? hair : n.StartsWith("M_EyeWhite") ? eyeWhite : n.StartsWith("M_Eye") ? eye
                          : n.StartsWith("M_Shoe") ? shoe : n.StartsWith("M_Mouth") ? mouth : n.StartsWith("M_Teeth") ? teeth
                          : n.StartsWith("M_Blush") ? blush : n.StartsWith("M_Warning") ? badge : sm[i];
                }
                r.sharedMaterials = sm;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                if (r is SkinnedMeshRenderer smr) smr.updateWhenOffscreen = true;
            }
            rig = new HumanRig(model.transform, worker.right);
            grip = Res.Find(model.transform, "Grip.R");
            var meter = Res.Model("SurveyMeter", worker, new Vector3(-0.27f, 0.98f, 0.32f), 0);
            meter.transform.localScale = Vector3.one * 1.15f;
            meterT = meter.transform;
            if (!rig.Valid) Debug.LogWarning("[MP] worker rig bones not found");
        }

        AudioClip MakeClick()
        {
            int sr = 44100, n = 900;
            var c = AudioClip.Create("click", n, 1, sr, false);
            var d = new float[n];
            var r = new System.Random(1);
            for (int i = 0; i < n; i++) d[i] = (float)((r.NextDouble() * 2 - 1) * Math.Exp(-i / 90.0));
            c.SetData(d, 0);
            return c;
        }

        // ------------------------------------------------------------------ UI
        void BuildUI()
        {
            var L = LeftScroll.contentContainer;
            var sec = UIKit.Section(L, T("Kaynak", "Source"));
            UIKit.Seg(sec, null, IsoNames, isoIdx, i => { isoIdx = i; App.Open("protection"); });
            UIKit.Text(sec, iso.Name + ": γ " + F(iso.GammaKeV, 0) + " keV • Γ = " + F(iso.GammaConstant, 4) + " µGy·m²/(MBq·h) • T½ " + iso.HalfLifeText());
            UIKit.Slider(sec, T("Aktivite", "Activity"), 10, 4000, activity, v => F(v, 0) + " MBq (" + F(v / 37, 1) + " mCi)", v => { activity = v; RecomputeHeat(); }, 10);
            sec = UIKit.Section(L, T("Zırh", "Shielding"));
            UIKit.Seg(sec, T("Malzeme", "Material"), new[] { T("Kurşun", "Lead"), T("Çelik", "Steel"), T("Beton", "Concrete"), T("Su", "Water") }, (int)shieldMat, i => { shieldMat = (ShieldMat)i; ApplyShieldSettings(); });
            UIKit.Slider(sec, T("Kalınlık", "Thickness"), 0.1f, 30, shieldCm, v => F(v, 1) + " cm", v => { shieldCm = v; ApplyShieldSettings(); }, 0.1f);
            var row = UIKit.ButtonRow(sec);
            UIKit.Button(row, T("+ Zırh yerleştir", "+ Place shield"), () => { placingShield = true; SetTip("Zırhı koymak için yere dokun.", "Tap the floor to place the shield."); });
            UIKit.Button(row, T("Hepsini kaldır", "Remove all"), () => { foreach (var s in shields) UnityEngine.Object.Destroy(s.View); shields.Clear(); RecomputeHeat(); });
            vHvl = UIKit.KV(sec, "HVL"); vTvl = UIKit.KV(sec, "TVL"); vNeeded = UIKit.KV(sec, T("Masada < 1 µSv/h için gereken", "Needed for < 1 µSv/h at desk"));

            sec = UIKit.Section(L, T("Görev: kaynağı topla", "Mission: retrieve the source"));
            UIKit.Toggle(sec, T("Uzun maşa kullan (kaynak gövdeden 60 cm)", "Use long tongs (source 60 cm from body)"), useTongs, v => useTongs = v);
            row = UIKit.ButtonRow(sec);
            UIKit.Button(row, T("Kaynağı al", "Pick up source"), PickUp, "mp-btn-primary");
            UIKit.Button(row, T("Kaba koy", "Put in container"), PutInPig, "mp-btn-primary");
            UIKit.Button(row, T("Sıfırla", "Reset"), () => App.Open("protection"));
            vMission = UIKit.Text(sec, T("Kaynağa yürü (< 0.8 m), al, kurşun kaba götür.", "Walk to the source (< 0.8 m), pick it up, carry it to the lead container."));
            UIKit.Slider(sec, T("Zaman hızı", "Time speed"), 1, 30, timeScale, v => F(v, 0) + "×", v => timeScale = v, 1);
            BuildTasks(L);

            var R = RightScroll.contentContainer;
            sec = UIKit.Section(R, T("Radyasyon ölçer", "Survey meter"));
            vRate = UIKit.KV(sec, T("Doz hızı H*(10)", "Dose rate H*(10)"));
            vRate.style.fontSize = 22;
            vDist = UIKit.KV(sec, T("Kaynağa uzaklık", "Distance to source"));
            vTotal = UIKit.KV(sec, T("Birikmiş doz", "Accumulated dose"));
            limitBar = UIKit.Progress(sec);
            UIKit.Note(sec, T("Çubuk: çalışan için günlük pay (20 mSv/yıl ÷ 250 gün = 80 µSv).", "Bar: daily share for a worker (20 mSv/yr ÷ 250 days = 80 µSv)."));
            var rowM = UIKit.ButtonRow(sec);
            UIKit.Button(rowM, T("Ölçümü kaydet", "Record reading"), Record);
            UIKit.Button(rowM, T("Ölçümleri sil", "Clear readings"), () => { readings.Clear(); UpdateIsq(); });
            sec = UIKit.Section(R, T("Ters kare yasası", "Inverse square law"));
            isqChart = new Chart(200) { XMin = 0, XMax = 6, XLabel = "m", YLabel = "µSv/h", LogY = true, AutoY = true };
            var pts = isqChart.Add(T("Ölçümler", "Readings"), new Color(1f, 0.8f, 0.2f), 3f); pts.Dots = true;
            isqChart.Add("Γ·A/d²", new Color(0.4f, 0.8f, 1f), 1.6f);
            sec.Add(isqChart.Legend()); sec.Add(isqChart);
            sec = UIKit.Section(R, T("Doz limitleri (ICRP 103)", "Dose limits (ICRP 103)"));
            UIKit.KV(sec, T("Çalışan (efektif)", "Worker (effective)"), "20 mSv/" + T("yıl", "yr"));
            UIKit.KV(sec, T("Halk", "Public"), "1 mSv/" + T("yıl", "yr"));
            UIKit.KV(sec, T("Göz merceği", "Eye lens"), "20 mSv/" + T("yıl", "yr"));
            UIKit.KV(sec, T("Cilt / eller", "Skin / hands"), "500 mSv/" + T("yıl", "yr"));
            UIKit.KV(sec, T("Gebe çalışan (fetüs)", "Pregnant worker (fetus)"), "1 mSv");
            UIKit.KV(sec, T("Doğal radyasyon (dünya ort.)", "Natural background (world avg.)"), "2.4 mSv/" + T("yıl", "yr"));
            ApplyShieldSettings();
            UpdateIsq();
        }

        void ApplyShieldSettings()
        {
            double keV = iso.GammaKeV > 0 ? Math.Min(iso.GammaKeV, 1500) : 500;
            vHvl.text = F(Shielding.Hvl(shieldMat, keV), 2) + " cm (" + F(iso.GammaKeV, 0) + " keV)";
            vTvl.text = F(Shielding.Tvl(shieldMat, keV), 2) + " cm";
            // thickness for < 1 µSv/h at the desk (bisection with build-up)
            double r0 = Shielding.DoseRate(iso, activity, Vector2.Distance(new Vector2(sourcePos.x, sourcePos.z), new Vector2(deskPos.x, deskPos.z)));
            double lo = 0, hi = 200;
            if (r0 <= 1) vNeeded.text = T("zırh gerekmez", "no shielding needed");
            else
            {
                for (int i = 0; i < 50; i++) { double mid = 0.5 * (lo + hi); if (r0 * Shielding.Transmission(shieldMat, keV, mid) > 1) lo = mid; else hi = mid; }
                vNeeded.text = F(hi, 1) + " cm";
            }
            foreach (var s in shields) { s.Mat = shieldMat; s.ThicknessCm = shieldCm; }
            RecomputeHeat();
            if (iso.Name == "Cs-137" && shieldMat == ShieldMat.Lead && Mathf.Abs(shieldCm - 0.6f) <= 0.06f && shields.Count > 0) hvlArmed = true;
        }

        bool hvlArmed;

        void OnTap(Vector2 screen)
        {
            var ray = App.Cam.ScreenPointToRay(screen);
            if (Mathf.Abs(ray.direction.y) < 1e-4f) return;
            float t = -ray.origin.y / ray.direction.y;
            if (t <= 0) return;
            var p = ray.origin + ray.direction * t;
            p.x = Mathf.Clamp(p.x, -5.2f, 5.2f); p.z = Mathf.Clamp(p.z, -5.2f, 5.2f);
            if (placingShield)
            {
                placingShield = false;
                PlaceShield(p);
                SetTip("Zırh yerleştirildi. Kalınlığı ve malzemeyi sol panelden değiştir.", "Shield placed. Change thickness and material in the left panel.");
                return;
            }
            walkTarget = new Vector3(p.x, 0, p.z);
        }

        // ------------------------------------------------------------------ solid obstacles (the worker cannot walk through them)
        const float WorkerRadius = 0.32f;   // body + arms
        const float ProbeReach = 0.52f, ProbeRadius = 0.05f;   // tip of the survey meter held in front

        void ResolveCollisions()
        {
            var p = new Vector2(worker.position.x, worker.position.z);
            var fwd = new Vector2(worker.forward.x, worker.forward.z).normalized;
            for (int it = 0; it < 3; it++)
            {
                p = PushAll(p, WorkerRadius);
                var tip = p + fwd * ProbeReach;         // the meter must not poke into the desk / stand / shields either
                p += PushAll(tip, ProbeRadius, true) - tip;
            }
            worker.position = new Vector3(p.x, 0, p.y);
        }

        /// <param name="probe">The meter is held at chest height: it clears the low lead pig and the stand's foot, only the rod and source head count.</param>
        Vector2 PushAll(Vector2 p, float rad, bool probe = false)
        {
            p = PushCircle(p, new Vector2(sourcePos.x, sourcePos.z), probe ? 0.06f : 0.27f, rad);    // source stand stays even after pick-up
            if (!probe) p = PushCircle(p, new Vector2(pigPos.x, pigPos.z), 0.34f, rad);
            p = PushBox(p, new Vector2(deskPos.x + 0.8f, deskPos.z), new Vector2(0.35f, 0.7f), rad);
            foreach (var sh in shields) p = PushSegment(p, sh.A, sh.B, 0.22f, rad);
            return p;
        }

        static Vector2 PushCircle(Vector2 p, Vector2 c, float r, float rad)
        {
            var d = p - c; float min = r + rad;
            if (d.sqrMagnitude >= min * min) return p;
            if (d.sqrMagnitude < 1e-8f) d = Vector2.right;
            return c + d.normalized * min;
        }

        static Vector2 PushBox(Vector2 p, Vector2 c, Vector2 half, float rad)
        {
            var q = new Vector2(Mathf.Clamp(p.x, c.x - half.x, c.x + half.x), Mathf.Clamp(p.y, c.y - half.y, c.y + half.y));
            var d = p - q;
            if (d.sqrMagnitude > 1e-8f) return d.sqrMagnitude >= rad * rad ? p : q + d.normalized * rad;
            // centre inside the box: leave through the nearest face
            float dx = half.x - Mathf.Abs(p.x - c.x), dz = half.y - Mathf.Abs(p.y - c.y);
            if (dx < dz) return new Vector2(c.x + Mathf.Sign(p.x - c.x) * (half.x + rad), p.y);
            return new Vector2(p.x, c.y + Mathf.Sign(p.y - c.y) * (half.y + rad));
        }

        static Vector2 PushSegment(Vector2 p, Vector2 a, Vector2 b, float r, float rad)
        {
            var ab = b - a; float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
            return PushCircle(p, a + ab * t, r, rad);
        }

        void PlaceShield(Vector3 p)
        {
            var toSrc = new Vector2(sourcePos.x - p.x, sourcePos.z - p.z).normalized;
            var along = new Vector2(-toSrc.y, toSrc.x);
            var c = new Vector2(p.x, p.z);
            var s = new Shield { A = c - along * 0.43f, B = c + along * 0.43f, Mat = shieldMat, ThicknessCm = shieldCm };
            float yaw = Mathf.Atan2(along.x, along.y) * Mathf.Rad2Deg + 90f;
            s.View = Res.Model("LeadShield", Root.transform, p, yaw);
            shields.Add(s);
            RecomputeHeat();
        }

        void PickUp()
        {
            if (inPig) return;
            float d = Flat(worker.position, sourceT.position);
            if (d > 0.8f) { vMission.text = T("Kaynağa daha yakın ol (< 0.8 m).", "Get closer to the source (< 0.8 m)."); return; }
            carrying = true;
            if (missionDose < 0) missionDose = totalDose;
            vMission.text = T("Kaynak elinde! Hızlıca kurşun kaba götür.", "Source in hand! Carry it quickly to the lead container.");
        }

        void PutInPig()
        {
            if (!carrying) return;
            if (Flat(worker.position, pigPos) > 1.0f) { vMission.text = T("Kaba daha yakın ol (< 1 m).", "Get closer to the container (< 1 m)."); return; }
            carrying = false; inPig = true;
            sourceT.position = pigPos + new Vector3(0, 0.25f, 0);
            double used = totalDose - Math.Max(0, missionDose);
            vMission.text = T("Kaynak güvende! Görev sırasında alınan doz: ", "Source secured! Dose during the mission: ") + F(used, 1) + " µSv";
            if (used < 20) CompleteTask("retrieve");
            RecomputeHeat();
        }

        static float Flat(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;

        void Record()
        {
            float d = Flat(worker.position, sourceT.position);
            readings.Add(new Vector2(d, (float)rate));
            UpdateIsq();
            bool r1 = false, r2 = false, r4 = false;
            foreach (var r in readings) { if (Mathf.Abs(r.x - 1) < 0.2f) r1 = true; if (Mathf.Abs(r.x - 2) < 0.25f) r2 = true; if (Mathf.Abs(r.x - 4) < 0.35f) r4 = true; }
            if (r1 && r2 && r4) CompleteTask("isq");
            if (Mathf.Abs(d - 1) < 0.2f && iso.Name == "Co-60") coRead = true;
            if (Mathf.Abs(d - 1) < 0.2f && iso.Name == "Tc-99m") tcRead = true;
            if (coRead && tcRead) CompleteTask("compare");
            if (hvlArmed && shields.Count > 0)
            {
                // compare with the unshielded rate at the same point
                double open = Shielding.DoseRate(iso, activity, d) * (inPig ? 0 : 1);
                if (open > 0 && rate / open > 0.4 && rate / open < 0.75) CompleteTask("hvl");
            }
        }

        void UpdateIsq()
        {
            var s0 = isqChart.AllSeries[0]; s0.Set(readings);
            var s1 = isqChart.AllSeries[1]; s1.Pts.Clear();
            for (float d = 0.3f; d <= 6; d += 0.05f) s1.Pts.Add(new Vector2(d, (float)Shielding.DoseRate(iso, activity, d)));
            isqChart.Refresh();
        }

        /// <summary>Shielded dose rate from the source at its current position (inside the pig: +5 cm lead).</summary>
        double SourceRateAt(Vector3 p)
        {
            var src2 = new Vector2(sourceT.position.x, sourceT.position.z);
            return Shielding.DoseRateAt(iso, activity, src2, new Vector2(p.x, p.z), shields, inPig ? 5.0 : 0);
        }

        /// <summary>Dose rate at the worker's meter: when carrying, the source is at hand / tong distance.</summary>
        double RateAt(Vector3 p)
        {
            if (carrying) return Shielding.DoseRate(iso, activity, useTongs ? 0.6 : 0.3);
            return SourceRateAt(p);
        }

        void RecomputeHeat()
        {
            if (heatTex == null) return;
            int n = heatTex.width;
            var px = new Color32[n * n];
            for (int j = 0; j < n; j++)
                for (int i = 0; i < n; i++)
                {
                    var p = new Vector3(-5.5f + (i + 0.5f) * 11f / n, 0, -5.5f + (j + 0.5f) * 11f / n);
                    double r = SourceRateAt(p);
                    float t = Mathf.Clamp01((float)((Math.Log10(Math.Max(r, 1e-3)) + 1) / 4.0));    // 0.1 … 1000 µSv/h
                    var c = ColorMaps.DoseJet(t);
                    c.a = r < 0.1 ? 0 : 0.35f + 0.25f * t;
                    // contour at 1 µSv/h
                    if (Math.Abs(Math.Log10(Math.Max(r, 1e-6))) < 0.03) c = new Color(1, 1, 1, 0.9f);
                    px[j * n + i] = c;
                }
            heatTex.SetPixels32(px); heatTex.Apply();
        }

        public override void Tick(float dt)
        {
            base.Tick(dt);
            // keyboard movement
            var kb = Keyboard.current;
            if (kb != null)
            {
                Vector3 mv = Vector3.zero;
                var fwd = App.Cam.transform.forward; fwd.y = 0; fwd.Normalize();
                var right = App.Cam.transform.right; right.y = 0; right.Normalize();
                if (kb.wKey.isPressed || kb.upArrowKey.isPressed) mv += fwd;
                if (kb.sKey.isPressed || kb.downArrowKey.isPressed) mv -= fwd;
                if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) mv += right;
                if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) mv -= right;
                if (mv.sqrMagnitude > 0) walkTarget = worker.position + mv.normalized * 0.5f;
            }
            var to = walkTarget - worker.position; to.y = 0;
            if (to.magnitude > 0.03f)
            {
                var step = to.normalized * Mathf.Min(to.magnitude, 1.4f * dt);
                worker.position += step;
                worker.rotation = Quaternion.Slerp(worker.rotation, Quaternion.LookRotation(to.normalized), dt * 8f);
            }
            worker.position = new Vector3(Mathf.Clamp(worker.position.x, -5.2f, 5.2f), 0, Mathf.Clamp(worker.position.z, -5.2f, 5.2f));
            ResolveCollisions();
            float speed = dt > 0 ? (worker.position - lastWorkerPos).magnitude / dt : 0;
            lastWorkerPos = worker.position;
            rig?.Tick(dt, speed, true);
            if (grip != null)
            {
                // the survey meter follows the right hand
                meterT.position = grip.position + worker.forward * 0.02f;
                meterT.rotation = worker.rotation;
            }
            App.Rig.SetTarget(worker.position + Vector3.up * 0.8f);
            if (carrying)
            {
                sourceT.position = worker.position + worker.forward * (useTongs ? 0.6f : 0.3f) + Vector3.up * 1.0f;
                heatT -= dt;
                if (heatT <= 0) { heatT = 0.4f; RecomputeHeat(); }
            }

            // dose
            rate = RateAt(meterT.position);
            double simDt = dt * timeScale;
            totalDose += rate * simDt / 3600.0;
            vRate.text = rate < 1 ? F(rate, 3) + " µSv/h" : F(rate, rate < 100 ? 1 : 0) + " µSv/h";
            UIKit.SetStatus(vRate, rate < 1 ? 0 : rate < 20 ? 1 : 2);
            vDist.text = F(Flat(worker.position, sourceT.position), 2) + " m";
            vTotal.text = F(totalDose, 2) + " µSv  (" + T("zaman ", "time ") + timeScale + "×)";
            limitBar.Set((float)(totalDose / 80.0));
            var deskRate = SourceRateAt(deskPos);
            if (deskRate < 1 && shields.Count > 0 && !inPig) CompleteTask("desk");

            // Geiger clicks (≈ 3 cps per µSv/h, dead-time limited)
            double cps = Math.Min(rate * 3.0, 250);
            clickAcc += cps * dt;
            while (clickAcc >= 1) { clickAcc -= 1; if (rng.NextDouble() < 0.9) audio.PlayOneShot(click, 0.6f); }

            // gamma rays visual
            if (!inPig)
            {
                int nr = Mathf.Clamp((int)(activity / 40f), 3, 30);
                var src2 = new Vector2(sourceT.position.x, sourceT.position.z);
                for (int k = 0; k < nr; k++)
                {
                    double a = rng.NextDouble() * Math.PI * 2, el = (rng.NextDouble() - 0.5) * 0.6;
                    var dir = new Vector3((float)(Math.Cos(a) * Math.Cos(el)), (float)Math.Sin(el), (float)(Math.Sin(a) * Math.Cos(el)));
                    float len = 6f;
                    var end2 = src2 + new Vector2(dir.x, dir.z) * len;
                    // stop at the first shield with probability 1 − T
                    float stop = len;
                    foreach (var s in shields)
                    {
                        if (SegHit(src2, end2, s.A, s.B, out float tt))
                        {
                            double tr = Shielding.Transmission(s.Mat, iso.GammaKeV, s.ThicknessCm);
                            if (rng.NextDouble() > tr) stop = Mathf.Min(stop, tt * len);
                        }
                    }
                    var p0 = sourceT.position;
                    rays.Add(p0, p0 + dir * stop, new Color(1f, 0.85f, 0.3f, 0.35f), new Color(1f, 0.6f, 0.2f, 0.0f));
                }
            }
            rays.Fade(Mathf.Pow(0.002f, dt));
            rays.Apply();
        }

        static bool SegHit(Vector2 p1, Vector2 p2, Vector2 q1, Vector2 q2, out float t)
        {
            t = 0;
            Vector2 r = p2 - p1, s = q2 - q1;
            float den = r.x * s.y - r.y * s.x;
            if (Mathf.Abs(den) < 1e-6f) return false;
            Vector2 qp = q1 - p1;
            t = (qp.x * s.y - qp.y * s.x) / den;
            float u = (qp.x * r.y - qp.y * r.x) / den;
            return t > 0 && t < 1 && u >= 0 && u <= 1;
        }

        public override void AutoAction(string action)
        {
            if (action == "shield") { PlaceShield(new Vector3(2.0f, 0, -1.0f)); walkTarget = new Vector3(3.2f, 0, -2.2f); }
            if (action == "walk") walkTarget = new Vector3(0.6f, 0, 0.2f);
            if (action == "face") { App.Rig.Frame(worker.position + Vector3.up * 1.45f, 1.1f, 180f, 8f, true); }
            if (action == "walkview") { walkTarget = worker.position + new Vector3(-3f, 0, 0.5f); App.Rig.Frame(worker.position + Vector3.up * 0.9f, 2.6f, 120f, 10f, true); }
        }

        // ------------------------------------------------------------------ theory
        protected override void BuildLearn(VisualElement c)
        {
            UIKit.H(c, T("1. Üç temel ilke: zaman, mesafe, zırhlama", "1. Three principles: time, distance, shielding"));
            UIKit.Eq(c, T("Doz = Doz hızı × zaman          Ḣ(d) = Γ · A / d²          Ḣ_zırhlı = Ḣ₀ · B · e^(−μx)",
                          "Dose = dose rate × time          Ḣ(d) = Γ · A / d²          Ḣ_shielded = Ḣ₀ · B · e^(−μx)"));
            UIKit.P(c, T("• <b>Zaman:</b> doz, kaynak yakınında geçirilen süreyle doğru orantılıdır.\n• <b>Mesafe:</b> nokta kaynakta doz hızı 1/d² ile düşer: mesafeyi iki katına çıkarmak dozu dörtte birine indirir. Maşa kullanmak bu yüzden çok etkilidir.\n• <b>Zırhlama:</b> yoğun ve yüksek Z'li malzemeler (kurşun) fotonları en etkili zayıflatır. HVL dozu yarıya, TVL onda bire indirir.",
                             "• <b>Time:</b> dose is proportional to the time spent near the source.\n• <b>Distance:</b> for a point source the dose rate falls as 1/d²: doubling the distance quarters the dose. That is why tongs are so effective.\n• <b>Shielding:</b> dense, high-Z materials (lead) attenuate photons best. One HVL halves, one TVL reduces the dose rate to one tenth."));
            UIKit.Eq(c, "HVL = ln2 / μ        TVL = ln10 / μ ≈ 3.32 · HVL");
            UIKit.P(c, T("Geniş ışında saçılmış fotonlar da dedektöre ulaşır; bunu birikim (build-up) faktörü B ≥ 1 hesaba katar. Burada B ≈ 1 + k·μx kullanılmıştır.",
                             "In broad beams scattered photons also reach the detector; the build-up factor B ≥ 1 accounts for this. Here B ≈ 1 + k·μx is used."));
            UIKit.H(c, T("2. Doz büyüklükleri", "2. Dose quantities"));
            UIKit.P(c, T("• Soğurulan doz D (Gy = J/kg)\n• Eşdeğer doz H_T = w_R · D_T (Sv); fotonlar ve elektronlar için w_R = 1, alfa için 20, protonlar için 2\n• Efektif doz E = Σ w_T · H_T (Sv); doku ağırlık faktörleri ICRP 103\n• Ölçerler ortam doz eşdeğeri H*(10) gösterir (≈ 1.2 × hava kerması, Cs-137 için).",
                             "• Absorbed dose D (Gy = J/kg)\n• Equivalent dose H_T = w_R · D_T (Sv); w_R = 1 for photons and electrons, 20 for alpha, 2 for protons\n• Effective dose E = Σ w_T · H_T (Sv); tissue weighting factors from ICRP 103\n• Survey meters show the ambient dose equivalent H*(10) (≈ 1.2 × air kerma for Cs-137)."));
            UIKit.H(c, T("3. ALARA ve limitler", "3. ALARA and limits"));
            UIKit.P(c, T("Radyasyondan korunmanın üç ilkesi: gerekçelendirme, optimizasyon (ALARA: makul olarak ulaşılabilecek en düşük) ve doz limitleri. Çalışan: 20 mSv/yıl (5 yıl ortalaması, tek yılda en çok 50), halk: 1 mSv/yıl, göz merceği: 20 mSv/yıl.",
                             "The three principles of radiation protection: justification, optimisation (ALARA: as low as reasonably achievable) and dose limits. Worker: 20 mSv/yr (averaged over 5 years, max 50 in one year), public: 1 mSv/yr, eye lens: 20 mSv/yr."));
        }
    }
}
