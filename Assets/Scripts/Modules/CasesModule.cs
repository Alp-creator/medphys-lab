using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using MedPhys.Cases;
using MedPhys.Core;
using MedPhys.UI;
using static MedPhys.Core.Loc;

namespace MedPhys.Modules
{
    /// <summary>
    /// Case mode: a waiting room of patients, each one a clinical case. A case is a sequence of decisions
    /// (questions, parameter choices backed by real physics, simulated images) with feedback, a running
    /// score / dose tally and a debrief with learning points and references.
    /// </summary>
    public sealed class CasesModule : ModuleBase
    {
        public override string Id => "cases";
        public override string Title => Def != null ? Def.Title : T("Klinik Vakalar", "Clinical Cases");
        public override string Subtitle => Def != null ? Def.P.Name + " • " + Def.P.AgeSex : T("Bekleme salonu — bir hasta seç", "Waiting room — choose a patient");
        protected override bool ShowResultsInitially => pending != null || App.Arg("-mpCase") != null;
        protected override string LeftPanelLabel => T("Hasta dosyası", "Patient file");
        protected override string RightPanelLabel => T("Aktif adım", "Current step");

        static CaseDef pending;          // case to open when the module is (re)created
        static bool argUsed;
        CaseDef Def
        {
            get
            {
                if (run != null) return run.Def;
                if (pending == null && !argUsed)
                {
                    argUsed = true;
                    string a = App.Arg("-mpCase");
                    if (a != null) pending = CaseLibrary.Get(a);
                }
                return pending;
            }
        }

        /// <summary>Opens the case mode directly in a case (restart / next patient).</summary>
        public static void Launch(string caseId)
        {
            pending = CaseLibrary.Get(caseId);
            App.I.Open("cases");
        }

        CaseRun run;

        // ---- lobby
        readonly List<(CaseDef def, PatientActor actor)> seated = new List<(CaseDef, PatientActor)>();
        readonly Dictionary<Collider, string> clickMap = new Dictionary<Collider, string>();
        readonly Dictionary<string, Button> cards = new Dictionary<string, Button>();
        string selectedId;
        readonly List<Label> nameLabels = new List<Label>();
        Label bubble;
        Transform bubbleAnchor;

        // ---- case scene
        PatientActor patient;
        WorkerActor staff;
        Transform couch, isoT, rotor, gantry, bucky, tube, detCenter;
        Vector3 beamCenter;
        Quaternion gantry0;
        Vector3 couchHome, patientHome;
        float couchDelta, couchTarget, couchSpeed = 0.45f;
        readonly Queue<(float delta, float dwell)> couchPath = new Queue<(float, float)>();
        float couchDwell;
        float rotorSpeed, rotorAngle;
        float gantryBase, gantryArc = -1;
        MeshRenderer beam; Material beamMat; Color beamColor; float beamOn;
        float scanFrac = 0.5f;

        // ---- step UI
        Label scoreChip, timeChip;
        VisualElement leftSteps, meterBox;
        Label doseVal, qualVal; UIKit.ProgressBar qualBar;
        Button submitBtn, simBtn;
        ImageView simImg; Label markL, markR, simStatus; UIKit.ProgressBar simProg;
        VisualElement readoutBox, controls, stepRoot;
        Chart chart;
        Vals vals;
        CaseSim sim; bool simAwait, simStale;
        readonly List<int> picked = new List<int>();
        readonly List<Button> optBtns = new List<Button>();
        readonly Dictionary<string, Action<float>> setters = new Dictionary<string, Action<float>>();
        bool submitted;
        VisualElement debrief;
        float timerAcc;

        // ---- automation
        readonly Queue<string> actions = new Queue<string>();
        float actionWait;

        protected override void Build()
        {
            var _ = Def;
            if (pending != null)
            {
                Right.RemoveFromClassList("mp-hidden");
                var d = CaseLibrary.Get(pending.Id) ?? pending;
                pending = null;
                BuildCase(d);
            }
            else BuildLobby();
        }

        public override void Dispose()
        {
            App.Rig.OnTap -= LobbyTap;
            debrief?.RemoveFromHierarchy();
            base.Dispose();
        }

        public override bool HandleBack()
        {
            if (debrief != null) { debrief.RemoveFromHierarchy(); debrief = null; return true; }
            if (run != null) { App.Open("cases"); return true; }
            return base.HandleBack();
        }

        // =================================================================== lobby
        void BuildLobby()
        {
            Env.Room(Root.transform, new Color(1f, 0.72f, 0.38f));
            var cases = CaseLibrary.All;
            float sp = 0.78f, z = 1.75f, seat = 0.46f;
            var seatMat = Res.Lit(new Color(0.16f, 0.45f, 0.58f), 0.35f);
            var frameMat = Res.Mat("M_Metal");
            for (int i = 0; i < cases.Count; i++)
            {
                var def = cases[i];
                float x = (i - (cases.Count - 1) * 0.5f) * sp;
                Chair(new Vector3(x, 0, z), seat, seatMat, frameMat);
                var a = PatientActor.Create(def.P, Root.transform);
                a.Sit(new Vector3(x, 0, z), 180f, seat);
                // rest on the seat cushion: the lowest skin point over the seat touches its top surface
                a.Settle(Vector3.down, -seat, new Bounds(new Vector3(x, seat - 0.03f, z + 0.05f), new Vector3(0.48f, 0.06f, 0.46f)));
                var col = a.Pivot.gameObject.AddComponent<BoxCollider>();
                col.center = new Vector3(0, a.Height * 0.62f, 0.12f);
                col.size = new Vector3(0.6f, a.Height * 0.8f, 0.85f);
                clickMap[col] = def.Id;
                seated.Add((def, a));
                nameLabels.Add(Label3D(new Vector3(x, a.Pivot.position.y + a.Height + 0.16f, z), def.P.Name.Split(' ')[0]));
            }
            Decor();
            bubbleAnchor = Child("BubbleAnchor").transform;
            bubbleAnchor.position = new Vector3(0, -50, 0);
            bubble = Label3D(Vector3.zero, "", bubbleAnchor);
            bubble.AddToClassList("mp-bubble");
            bubble.style.display = DisplayStyle.None;

            App.Rig.Frame(new Vector3(0, 0.95f, 1.4f), 6.3f, 0f, 9f, true);
            App.Rig.MinDistance = 1.2f; App.Rig.MaxDistance = 7.5f;
            App.Rig.MinPitch = -2f; App.Rig.MaxPitch = 55f;
            App.Rig.PanBounds = new Bounds(new Vector3(0, 1, 0.8f), new Vector3(8, 1.6f, 5));
            App.Rig.OnTap += LobbyTap;
            BuildLobbyUI();
            SetTip("Bir hastaya dokun ya da soldaki listeden seç. Her hasta gerçek fizik hesaplarıyla çözülen bir klinik vaka.",
                   "Tap a patient or pick one from the list. Each patient is a clinical case solved with real physics.");
        }

        void Chair(Vector3 hip, float seat, Material seatMat, Material frameMat)
        {
            // the patient faces -Z: the seat extends behind the hip joints (+Z)
            var c = hip + new Vector3(0, 0, 0.05f);
            MeshObj("Seat", MeshGen.Box(c + new Vector3(0, seat - 0.03f, 0), new Vector3(0.24f, 0.03f, 0.23f), Quaternion.identity), seatMat).shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            MeshObj("Back", MeshGen.Box(c + new Vector3(0, seat + 0.26f, 0.24f), new Vector3(0.24f, 0.22f, 0.025f), Quaternion.Euler(-8, 0, 0)), seatMat).shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            for (int k = 0; k < 4; k++)
            {
                float lx = (k & 1) == 0 ? -0.21f : 0.21f, lz = (k & 2) == 0 ? -0.2f : 0.2f;
                MeshObj("Leg", MeshGen.Box(c + new Vector3(lx, (seat - 0.06f) * 0.5f, lz), new Vector3(0.015f, (seat - 0.06f) * 0.5f, 0.015f), Quaternion.identity), frameMat);
            }
        }

        void Decor()
        {
            var desk = Res.Lit(new Color(0.85f, 0.86f, 0.9f), 0.4f);
            var wood = Res.Lit(new Color(0.55f, 0.38f, 0.24f), 0.35f);
            MeshObj("Desk", MeshGen.Box(new Vector3(-3.9f, 0.55f, -0.4f), new Vector3(0.35f, 0.55f, 1.0f), Quaternion.identity), desk).shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            MeshObj("DeskTop", MeshGen.Box(new Vector3(-3.9f, 1.12f, -0.4f), new Vector3(0.42f, 0.025f, 1.08f), Quaternion.identity), wood);
            var scr = MeshObj("Monitor", MeshGen.Box(new Vector3(-3.95f, 1.36f, -0.6f), new Vector3(0.02f, 0.17f, 0.26f), Quaternion.Euler(0, 0, 0)), Res.Emissive(new Color(0.1f, 0.3f, 0.4f), new Color(0.2f, 0.8f, 1.1f)));
            for (int s = -1; s <= 1; s += 2)
            {
                var pot = new Vector3(3.6f * s, 0, 2.2f);
                MeshObj("Pot", MeshGen.Cylinder(pot + new Vector3(0, 0.22f, 0), new Vector3(0.2f, 0.2f, 0.22f), Quaternion.Euler(90, 0, 0), 24), Res.Lit(new Color(0.9f, 0.9f, 0.92f), 0.3f));
                MeshObj("Plant", MeshGen.Ellipsoid(pot + new Vector3(0, 0.85f, 0), new Vector3(0.38f, 0.5f, 0.38f), Quaternion.identity, 16, 24), Res.Lit(new Color(0.18f, 0.5f, 0.25f), 0.25f)).shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            }
            staff = WorkerActor.Create(Root.transform, new Vector3(-3.3f, 0, -0.4f), 90f);
            Label3D(new Vector3(-3.9f, 2.0f, -0.4f), T("Kabul", "Reception"));
            Label3D(new Vector3(0, 2.75f, 2.4f), T("BEKLEME SALONU", "WAITING ROOM"));
        }

        void BuildLobbyUI()
        {
            var L = LeftScroll.contentContainer;
            int totalStars = 0;
            foreach (var d in CaseLibrary.All) totalStars += d.BestStars;
            var sec = UIKit.Section(L, T("Bekleyen hastalar", "Waiting patients") + "  ★ " + totalStars + "/" + CaseLibrary.All.Count * 3);
            foreach (var d in CaseLibrary.All)
            {
                var def = d;
                var b = new Button(() => Select(def.Id));
                b.AddToClassList("mp-btn"); b.AddToClassList("mp-casecard");
                var top = new VisualElement(); top.style.flexDirection = FlexDirection.Row; top.style.alignItems = Align.Center;
                var nm = new Label(def.P.Name); nm.AddToClassList("mp-case-name"); nm.style.flexGrow = 1; top.Add(nm);
                var st = new Label(Stars(def.BestStars)); st.AddToClassList("mp-stars"); top.Add(st);
                b.Add(top);
                var sub = new Label(def.P.AgeSex + " • " + def.Complaint); sub.AddToClassList("mp-case-sub"); b.Add(sub);
                var chips = new VisualElement(); chips.style.flexDirection = FlexDirection.Row; chips.style.marginTop = 4;
                Chip(chips, ModalityName(def.Modality));
                Chip(chips, T("Zorluk ", "Level ") + new string('●', def.Difficulty) + new string('○', 3 - def.Difficulty));
                if (def.BestPct >= 0) Chip(chips, T("En iyi ", "Best ") + Pct(def.BestPct));
                b.Add(chips);
                sec.Add(b);
                cards[def.Id] = b;
            }
            var R = RightScroll.contentContainer;
            sec = UIKit.Section(R, T("Klinik vaka modu", "Clinical case mode"));
            UIKit.Text(sec, T("Hastanede bir medikal fizikçisin. Her hasta bir vaka: dosyayı oku, kararlarını ver, gerçek fizik modelleriyle hesaplanan sonuçları gör. Her adımın sonunda neden doğru/yanlış olduğunu öğrenirsin.",
                              "You are the hospital's medical physicist. Each patient is a case: read the file, make your decisions and see results computed with real physics models. After every step you learn why it was right or wrong."));
            UIKit.Text(sec, T("Puanlama: tanısal kalite / tedavi etkinliği, hasta dozu (ALARA), güvenlik ve iletişim. ★★★ için ≥ %85.",
                              "Scoring: diagnostic quality / treatment efficacy, patient dose (ALARA), safety and communication. ★★★ needs ≥ 85 %."));
            UIKit.Note(sec, T("İpucu: kararsız kalırsan üst çubuktaki 'Teori' sayfasında temel formüller var.", "Tip: the 'Theory' page in the top bar lists the key formulas."));
        }

        static string Pct(int p) => En ? p + "%" : "%" + p;      // Turkish writes the percent sign first

        static void Chip(VisualElement parent, string text) { var c = new Label(text); c.AddToClassList("mp-chip"); parent.Add(c); }
        static string Stars(int n) => "<color=#FFB547>" + new string('★', n) + "</color><color=#3E4A5A>" + new string('★', 3 - n) + "</color>";

        static string ModalityName(string m) => m == "xray" ? T("Röntgen", "X-ray") : m == "ct" ? T("BT", "CT") : m == "therapy" ? T("Radyoterapi", "Radiotherapy")
                                              : m == "nucmed" ? T("Nükleer tıp", "Nuclear med.") : m == "mri" ? "MR" : m;

        void LobbyTap(Vector2 screen)
        {
            var ray = App.Cam.ScreenPointToRay(screen);
            if (UnityEngine.Physics.Raycast(ray, out var hit, 50f) && clickMap.TryGetValue(hit.collider, out var id)) Select(id);
        }

        void Select(string id)
        {
            var def = CaseLibrary.Get(id);
            if (def == null) return;
            selectedId = id;
            foreach (var kv in cards) kv.Value.EnableInClassList("mp-casecard-on", kv.Key == id);
            foreach (var l in nameLabels) l.style.display = DisplayStyle.None;   // close-up: only the speech bubble
            foreach (var (d, a) in seated)
            {
                if (d.Id != id) continue;
                a.Wave();
                var head = a.Pivot.position + Vector3.up * (a.Height - 0.05f);
                App.Rig.Frame(head + new Vector3(0, -0.15f, 0), 2.1f, 8f, 6f);
                bubbleAnchor.position = head + new Vector3(0.18f, 0.32f, 0);
                bubble.text = T(def.IntroTr, def.IntroEn);
                bubble.style.display = DisplayStyle.Flex;
            }
            var R = RightScroll.contentContainer;
            R.Clear();
            BuildFile(R, def, true);
            Right.RemoveFromClassList("mp-hidden");
            if (App.SmallScreen) Left.AddToClassList("mp-hidden");
            SetTip("<b>" + def.P.Name.Split(' ')[0] + ":</b> \"" + T(def.IntroTr, def.IntroEn) + "\"", "<b>" + def.P.Name.Split(' ')[0] + ":</b> \"" + T(def.IntroTr, def.IntroEn) + "\"");
        }

        /// <summary>The patient file: demographics, complaint, request, history, findings.</summary>
        void BuildFile(VisualElement parent, CaseDef def, bool withStart)
        {
            var p = def.P;
            var sec = UIKit.Section(parent, T("Hasta dosyası", "Patient file"));
            var nm = new Label(p.Name); nm.AddToClassList("mp-case-name"); nm.style.fontSize = 20; sec.Add(nm);
            UIKit.KV(sec, T("Yaş / cinsiyet", "Age / sex"), p.AgeSex);
            UIKit.KV(sec, T("Boy / kilo", "Height / weight"), F(p.HeightCm, 0) + " cm / " + F(p.WeightKg, 0) + " kg");
            var bmi = UIKit.KV(sec, "BMI", F(p.Bmi, 1) + " kg/m²");
            UIKit.SetStatus(bmi, p.Bmi >= 35 ? 2 : p.Bmi >= 30 ? 1 : -1);
            sec = UIKit.Section(parent, T("Şikâyet ve istem", "Complaint and request"));
            UIKit.Text(sec, "<b>" + def.Complaint + "</b>");
            UIKit.Text(sec, T(def.RequestTr, def.RequestEn));
            sec = UIKit.Section(parent, T("Öykü", "History"));
            foreach (var h in def.History) UIKit.Text(sec, "• " + T(h.tr, h.en));
            sec = UIKit.Section(parent, T("Bulgular", "Findings"));
            foreach (var v in def.Vitals) UIKit.KV(sec, T(v.tr, v.en), v.v);
            if (!withStart) return;
            sec = UIKit.Section(parent, def.Title);
            UIKit.Text(sec, T("Adım sayısı: ", "Steps: ") + def.Steps.Count + " • " + ModalityName(def.Modality) + " • " + T("en çok ", "max ") + def.MaxPoints + T(" puan", " points"));
            if (def.BestStars > 0 || def.BestPct >= 0) UIKit.Text(sec, T("En iyi sonucun: ", "Your best: ") + Stars(def.BestStars) + "  " + Pct(def.BestPct));
            var row = UIKit.ButtonRow(sec);
            UIKit.Button(row, T("VAKAYI BAŞLAT  →", "START CASE  →"), () => Launch(def.Id), "mp-btn-primary");
        }

        // =================================================================== case scene
        void BuildCase(CaseDef def)
        {
            run = new CaseRun(def);
            patient = PatientActor.Create(def.P, Root.transform);
            switch (def.Modality)
            {
                case "xray": RoomXRay(); break;
                case "ct": RoomCT(def); break;
                case "therapy": RoomLinac(def); break;
                case "nucmed": RoomPet(); break;
                case "mri": RoomMri(); break;
            }
            couchHome = couch != null ? couch.position : Vector3.zero;
            patientHome = patient.Pivot.position;
            App.Rig.MinDistance = 0.8f; App.Rig.MaxDistance = 7f;
            App.Rig.PanBounds = new Bounds(new Vector3(0, 1.2f, -0.5f), new Vector3(6, 2.5f, 6));
            BuildCaseUI();
            OpenStep(0);
            SetTip("<b>" + def.P.Name.Split(' ')[0] + ":</b> \"" + T(def.IntroTr, def.IntroEn) + "\"", "<b>" + def.P.Name.Split(' ')[0] + ":</b> \"" + T(def.IntroTr, def.IntroEn) + "\"");
        }

        static Bounds RBounds(Transform t)
        {
            var rs = t.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return new Bounds(t.position, Vector3.zero);
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }

        void RoomXRay()
        {
            Env.Room(Root.transform, new Color(0.45f, 0.55f, 1f));
            var room = Res.Model("XRay_Room", Root.transform);
            bucky = room.Find("XR_Bucky"); tube = room.Find("XR_TubeAssembly"); detCenter = room.Find("XR_DetectorCenter");
            float chestY = patient.Height * 0.72f;
            tube.position += Vector3.up * (chestY - detCenter.position.y);   // central ray through the lungs
            patient.Stand(new Vector3(0, 0, detCenter.position.z - patient.FrontDepth - 0.05f), 0f, PatientActor.PoseKind.ChestPA);
            // PA chest: the top of the detector sits ~5 cm above the shoulders so the chin rests on it and the chest is pressed flat
            var buckyBody = room.Find("XR_BuckyBody");
            var face = room.Find("XR_DetectorFace");
            var bb = RBounds(buckyBody != null ? buckyBody : bucky);
            bucky.position += Vector3.up * (patient.ShoulderY + 0.05f - bb.max.y);
            bb = RBounds(buckyBody != null ? buckyBody : bucky);
            beamCenter = new Vector3(detCenter.position.x, chestY, detCenter.position.z);
            float faceZ = face != null ? Mathf.Min(RBounds(face).min.z, bb.min.z) : bb.min.z;
            patient.Settle(Vector3.forward, faceZ, bb);
            beamMat = Res.Additive(new Color(1f, 0.92f, 0.55f, 0f), 1f, 0.3f);
            beamColor = new Color(0.6f, 0.65f, 1f, 1.4f);
            beam = MeshObj("Beam", new Mesh(), beamMat);
            UpdateXrayBeam(35, 43);
            staff = WorkerActor.Create(Root.transform, new Vector3(-1.6f, 0, -1.2f), 60f);
            App.Rig.Frame(new Vector3(0, chestY, detCenter.position.z - 0.7f), 3.0f, 235f, 8f, true);
        }

        void UpdateXrayBeam(float fw, float fh)
        {
            if (beam == null || tube == null) return;
            var d = beamCenter;
            float hw = fw * 0.005f, hh = fh * 0.005f;
            var q = new[] { d + new Vector3(-hw, -hh, 0), d + new Vector3(hw, -hh, 0), d + new Vector3(hw, hh, 0), d + new Vector3(-hw, hh, 0) };
            var l = new Vector3[4];
            for (int i = 0; i < 4; i++) l[i] = beam.transform.InverseTransformPoint(q[i]);
            beam.GetComponent<MeshFilter>().sharedMesh = MeshGen.Beam(beam.transform.InverseTransformPoint(tube.position), l, 0.15f, 1f);
        }

        void RoomCT(CaseDef def)
        {
            Env.Room(Root.transform, new Color(0.25f, 0.85f, 0.9f));
            var ct = Res.Model("CT_Scanner", Root.transform);
            rotor = ct.Find("CT_Rotor"); couch = ct.Find("CT_CouchTop"); isoT = ct.Find("CT_Iso");
            var tb = RBounds(couch);
            patient.LieSupine(new Vector3(0, 0, tb.max.z - 0.04f), Vector3.forward, tb.max.y);
            patient.RestOn(couch);
            scanFrac = def.Id == "elif" ? 0.50f : 0.60f;
            staff = WorkerActor.Create(Root.transform, new Vector3(-0.85f, 0, -1.5f), 80f);
            App.Rig.Frame(new Vector3(0.2f, 1.15f, -0.9f), 4.0f, -38f, 14f, true);
        }

        void RoomLinac(CaseDef def)
        {
            Env.Room(Root.transform, new Color(0.35f, 0.65f, 1f));
            var linac = Res.Model("Linac", Root.transform);
            gantry = linac.Find("Linac_Gantry"); couch = linac.Find("Linac_CouchTop"); isoT = linac.Find("Linac_Iso");
            var src = linac.Find("Linac_Source");
            gantry0 = gantry.rotation;
            bool face = def.Id == "ayse";
            float frac = face ? 0.935f : 0.50f;
            var tb = RBounds(couch);
            float surfaceY = isoT.position.y - patient.BackDepth;
            float headZ = isoT.position.z + patient.Height * (1 - frac);
            patient.LieSupine(new Vector3(0, 0, headZ), Vector3.forward, surfaceY);
            if (face) patient.Pivot.position += patient.Pivot.right * 0.05f;   // left cheek on the central axis
            couch.position += new Vector3(0, surfaceY - tb.max.y, Mathf.Max(0, headZ + 0.08f - tb.max.z));
            patient.RestOn(couch);
            gantryBase = face ? -28f : 0f;
            SetGantry(gantryBase);
            // treatment beam, child of the gantry so it follows the arc
            float half = face ? 0.0225f : 0.05f;
            var iso = isoT.position;
            var axis = (iso - src.position).normalized;
            var u = Vector3.Cross(axis, Vector3.forward).normalized; var w = Vector3.Cross(axis, u).normalized;
            var q = new[] { iso - u * half - w * half, iso + u * half - w * half, iso + u * half + w * half, iso - u * half + w * half };
            beamMat = Res.Additive(new Color(1f, 0.9f, 0.4f, 0f), 1f, 0.4f);
            beamColor = face ? new Color(0.35f, 0.7f, 1f, 0.55f) : new Color(1f, 0.9f, 0.45f, 0.4f);
            beam = MeshObj("TreatBeam", new Mesh(), beamMat, gantry);
            var l = new Vector3[4];
            for (int i = 0; i < 4; i++) l[i] = beam.transform.InverseTransformPoint(q[i]);
            beam.GetComponent<MeshFilter>().sharedMesh = MeshGen.Beam(beam.transform.InverseTransformPoint(src.position), l, 0.2f, 0.9f);
            staff = WorkerActor.Create(Root.transform, new Vector3(0.85f, 0, -0.9f), -95f);
            App.Rig.Frame(iso + new Vector3(0, -0.15f, -0.3f), 3.4f, 38f, 16f, true);
        }

        void SetGantry(float a) { if (gantry != null) gantry.rotation = Quaternion.AngleAxis(-a, Vector3.forward) * gantry0; }

        void RoomPet()
        {
            Env.Room(Root.transform, new Color(0.35f, 1f, 0.6f));
            var pet = Res.Model("PET_Scanner", Root.transform);
            couch = pet.Find("PET_CouchTop"); isoT = pet.Find("PET_Iso");
            var tb = RBounds(couch);
            patient.LieSupine(new Vector3(0, 0, tb.max.z - 0.04f), Vector3.forward, tb.max.y);
            patient.RestOn(couch);
            staff = WorkerActor.Create(Root.transform, new Vector3(-0.85f, 0, -1.6f), 80f);
            App.Rig.Frame(isoT.position + new Vector3(0, -0.1f, -1.0f), 3.6f, -32f, 13f, true);
        }

        void RoomMri()
        {
            Env.Room(Root.transform, new Color(0.6f, 0.55f, 1f));
            var mri = Res.Model("MRI_Scanner", Root.transform);
            couch = mri.Find("MRI_CouchTop"); isoT = mri.Find("MRI_Iso");
            // knee exam, feet first: the head coil at the magnet end is not used (it would sit under the shins)
            foreach (var t in mri.GetComponentsInChildren<Transform>(true))
                if (t.name == "MRI_HeadCoil") t.gameObject.SetActive(false);
            var tb = RBounds(couch);
            patient.LieSupine(new Vector3(0, 0, tb.max.z - 0.06f - patient.Height), Vector3.back, tb.max.y);
            patient.RestOn(couch);
            staff = WorkerActor.Create(Root.transform, new Vector3(-1.1f, 0, -1.9f), 70f);
            App.Rig.Frame(new Vector3(0, 1.1f, -1.2f), 4.2f, -28f, 12f, true);
        }

        // =================================================================== case UI
        void BuildCaseUI()
        {
            var sp = Top.Q(className: "mp-spacer");
            int at = sp != null ? Top.IndexOf(sp) : Top.childCount;
            scoreChip = new Label(); scoreChip.AddToClassList("mp-chip"); scoreChip.style.fontSize = 15; scoreChip.style.marginLeft = 12;
            timeChip = new Label(); timeChip.AddToClassList("mp-chip"); timeChip.style.fontSize = 15;
            Top.Insert(at, timeChip); Top.Insert(at, scoreChip);

            var L = LeftScroll.contentContainer;
            BuildFile(L, run.Def, false);
            var sec = UIKit.Section(L, T("Adımlar", "Steps"));
            leftSteps = new VisualElement(); sec.Add(leftSteps);
            meterBox = UIKit.Section(L, T("Durum", "Status"));
            doseVal = UIKit.KV(meterBox, T("Hasta dozu (efektif)", "Patient dose (effective)"), "0 mSv");
            qualVal = UIKit.KV(meterBox, T("Tanı / tedavi kalitesi", "Diagnostic / treatment quality"), "–");
            qualBar = UIKit.Progress(meterBox);
            qualBar.Set(0);
            // The question is the immediate task. Keep the patient file one tap away so it does not compete for attention.
            Left.AddToClassList("mp-hidden");
            RefreshStatus();
        }

        void RefreshStatus()
        {
            if (run == null) return;
            scoreChip.text = T("Puan ", "Score ") + run.Points + " / " + run.MaxSoFar;
            leftSteps.Clear();
            for (int i = 0; i < run.Def.Steps.Count; i++)
            {
                var st = run.Def.Steps[i];
                var row = new VisualElement(); row.AddToClassList("mp-task");
                string mark = i < run.Results.Count ? (run.Results[i].G == Grade.Good ? "✓" : run.Results[i].G == Grade.Ok ? "≈" : "✗") : i == run.Index ? "●" : "";
                var box = new Label(mark); box.AddToClassList("mp-task-box");
                if (i < run.Results.Count) box.AddToClassList(run.Results[i].G == Grade.Good ? "mp-task-done" : run.Results[i].G == Grade.Ok ? "mp-warn" : "mp-bad");
                var tx = new Label(st.Title + (i < run.Results.Count ? "  (" + run.Results[i].Points + "/" + run.Results[i].Max + ")" : ""));
                tx.AddToClassList("mp-task-text");
                if (i == run.Index) tx.style.color = new Color(1f, 0.85f, 0.4f);
                row.Add(box); row.Add(tx);
                leftSteps.Add(row);
            }
            doseVal.text = run.DoseMSv <= 0 ? "0 mSv" : run.DoseMSv < 0.1 ? F(run.DoseMSv * 1000, 1) + " µSv" : F(run.DoseMSv, 2) + " mSv";
            if (run.Quality >= 0)
            {
                qualVal.text = F(run.Quality * 100, 0) + " %";
                UIKit.SetStatus(qualVal, run.Quality >= 0.85f ? 0 : run.Quality >= 0.45f ? 1 : 2);
                qualBar.Set(run.Quality);
            }
        }

        void OpenStep(int i)
        {
            submitted = false; picked.Clear(); optBtns.Clear(); setters.Clear();
            sim = null; simAwait = false; simStale = false; chart = null; vals = null; simImg = null; simBtn = null; readoutBox = null;
            couchTarget = 0; couchPath.Clear();
            var R = RightScroll.contentContainer;
            R.Clear();
            RefreshStatus();
            var st = run.Current;
            if (st == null) { ShowDebrief(); return; }
            string say = st.Say(run);
            if (say != null) Tip.text = say;
            stepRoot = UIKit.Section(R, T("Adım ", "Step ") + (i + 1) + "/" + run.Def.Steps.Count + " — " + st.Title);
            var pr = new Label(st.Prompt(run)); pr.AddToClassList("mp-prompt"); pr.enableRichText = true;
            stepRoot.Add(pr);
            if (st is ChoiceStep cs) BuildChoice(cs);
            else if (st is ParamStep ps) BuildParam(ps);
            var row = UIKit.ButtonRow(stepRoot);
            submitBtn = UIKit.Button(row, T("ONAYLA", "CONFIRM"), Submit, "mp-btn-primary");
            UpdateSubmit();
            RightScroll.scrollOffset = Vector2.zero;
        }

        void BuildChoice(ChoiceStep cs)
        {
            if (cs.Multi) UIKit.Note(stepRoot, T("Birden fazla seçenek işaretleyebilirsin.", "You may tick more than one option."));
            for (int k = 0; k < cs.Options.Count; k++)
            {
                int idx = k;
                var b = new Button(() => Pick(cs, idx)) { text = (cs.Multi ? "○  " : "") + cs.Options[k].Text(run) };
                b.AddToClassList("mp-btn"); b.AddToClassList("mp-opt");
                stepRoot.Add(b);
                optBtns.Add(b);
            }
        }

        void Pick(ChoiceStep cs, int i)
        {
            if (submitted) return;
            if (cs.Multi) { if (picked.Contains(i)) picked.Remove(i); else picked.Add(i); }
            else { picked.Clear(); picked.Add(i); }
            for (int k = 0; k < optBtns.Count; k++)
            {
                bool on = picked.Contains(k);
                optBtns[k].EnableInClassList("mp-opt-on", on);
                if (cs.Multi) optBtns[k].text = (on ? "●  " : "○  ") + cs.Options[k].Text(run);
            }
            UpdateSubmit();
        }

        void BuildParam(ParamStep ps)
        {
            vals = new Vals();
            controls = new VisualElement();
            stepRoot.Add(controls);
            foreach (var p in ps.Params)
            {
                var par = p;
                vals.V[p.Key] = p.Value;
                if (p.OptTr != null)
                {
                    var seg = UIKit.Seg(controls, p.Label, En ? System.Array.ConvertAll(p.OptEn, Loc.Translate) : p.OptTr, (int)p.Value, k => { vals.V[par.Key] = k; OnParamChanged(); });
                    setters[p.Key] = v => { seg.Select(Mathf.RoundToInt(v)); };
                }
                else if (p.Toggle)
                {
                    var tg = UIKit.Toggle(controls, p.Label, p.Value > 0.5f, b => { vals.V[par.Key] = b ? 1 : 0; OnParamChanged(); });
                    setters[p.Key] = v => tg.value = v > 0.5f;
                }
                else
                {
                    var sl = UIKit.Slider(controls, p.Label, p.Min, p.Max, p.Value, p.Fmt, v => { vals.V[par.Key] = v; OnParamChanged(); }, p.Snap);
                    setters[p.Key] = v => sl.Set(v, true);
                }
            }
            if (ps.ChartInit != null)
            {
                chart = new Chart(App.SmallScreen ? ps.ChartHeight * 0.8f : ps.ChartHeight);
                ps.ChartInit(chart);
                if (chart.AllSeries.Count > 1) stepRoot.Add(chart.Legend());
                stepRoot.Add(chart);
            }
            if (ps.MakeSim != null)
            {
                sim = ps.MakeSim();
                simImg = new ImageView(App.SmallScreen ? 250 : 330, "");
                markL = new Label("") { pickingMode = PickingMode.Ignore }; markR = new Label("") { pickingMode = PickingMode.Ignore };
                foreach (var mk in new[] { markL, markR })
                {
                    mk.style.position = Position.Absolute; mk.style.top = 6; mk.style.fontSize = 17; mk.style.color = new Color(1, 0.85f, 0.3f);
                    mk.style.unityFontStyleAndWeight = FontStyle.Bold;
                    simImg.Overlay.Add(mk);
                }
                markL.style.left = 10; markR.style.right = 10;
                simImg.Set(sim.Tex);
                stepRoot.Add(simImg);
                var row = UIKit.ButtonRow(stepRoot);
                simBtn = UIKit.Button(row, T(sim.ButtonTr, sim.ButtonEn), RunSim, "mp-btn-primary");
                simProg = UIKit.Progress(stepRoot);
                simStatus = UIKit.Text(stepRoot, "");
            }
            readoutBox = UIKit.Section(stepRoot, T("Ölçümler", "Read-outs"));
            OnParamChanged();
        }

        void OnParamChanged()
        {
            if (!(run?.Current is ParamStep ps) || vals == null) return;
            if (sim != null && vals.HasSim) simStale = true;
            if (run.Def.Modality == "xray" && vals.V.ContainsKey("fw")) UpdateXrayBeam(vals["fw"], vals["fh"]);
            if (chart != null && ps.Plot != null) { ps.Plot(vals, chart); chart.Refresh(); }
            RefreshReadouts(ps);
            if (simStale && simStatus != null) simStatus.text = T("Ayarlar değişti — onaylamadan önce yeniden çalıştır.", "Settings changed — run again before confirming.");
            UpdateSubmit();
        }

        void RefreshReadouts(ParamStep ps)
        {
            if (readoutBox == null) return;
            while (readoutBox.childCount > 1) readoutBox.RemoveAt(1);
            if (ps.Live == null) return;
            foreach (var r in ps.Live(vals))
            {
                var v = UIKit.KV(readoutBox, r.Label, r.Value);
                if (r.Level >= 0) UIKit.SetStatus(v, r.Level);
            }
        }

        void RunSim()
        {
            if (sim == null || sim.Running || submitted) return;
            sim.Start(vals);
            simAwait = true;
            simStatus.text = T("Hesaplanıyor…", "Computing…");
            StartAnim(run.Current.Anim, true);
        }

        void UpdateSubmit()
        {
            if (submitBtn == null || submitted) return;
            bool ok = true;
            var st = run.Current;
            if (st is ChoiceStep) ok = picked.Count > 0;
            else if (st is ParamStep ps && ps.MakeSim != null && ps.RequireSim) ok = vals != null && vals.HasSim && !simStale && !simAwait;
            submitBtn.SetEnabled(ok);
        }

        void Submit()
        {
            if (submitted) return;
            var st = run.Current;
            StepResult r = null;
            if (st is ChoiceStep cs)
            {
                if (picked.Count == 0) return;
                r = cs.Evaluate(run, picked);
                int best = 0; foreach (var o in cs.Options) best = Math.Max(best, o.Points);
                for (int k = 0; k < optBtns.Count; k++)
                {
                    var o = cs.Options[k];
                    bool good = cs.Multi ? o.Points > 0 : o.Points == best;
                    optBtns[k].EnableInClassList("mp-opt-good", good);
                    optBtns[k].EnableInClassList("mp-opt-bad", picked.Contains(k) && !good);
                    optBtns[k].SetEnabled(false);
                }
                if (!cs.Multi && cs.Options[picked[0]].Points < best)
                {
                    var bo = cs.Options.Find(o => o.Points == best);
                    r.Lines.Add(T("En iyi yanıt: ", "Best answer: ") + bo.Text(run) + " — " + bo.Feedback(run));
                }
            }
            else if (st is ParamStep ps)
            {
                r = ps.Evaluate(vals.Clone(), run);
                controls?.SetEnabled(false);
                simBtn?.SetEnabled(false);
                if (ps.MakeSim == null) StartAnim(ps.Anim, false);
            }
            submitted = true;
            if (r != null) run.Add(r);
            submitBtn.parent.RemoveFromHierarchy();
            var fb = new VisualElement(); fb.AddToClassList("mp-fb");
            if (r != null)
            {
                fb.AddToClassList(r.G == Grade.Good ? "mp-fb-good" : r.G == Grade.Ok ? "mp-fb-ok" : "mp-fb-bad");
                var h = new Label((r.G == Grade.Good ? T("✓ Çok iyi", "✓ Excellent") : r.G == Grade.Ok ? T("≈ Kısmen doğru", "≈ Partly right") : T("✗ Hatalı", "✗ Wrong")) + "   +" + r.Points + " / " + r.Max);
                h.AddToClassList("mp-fb-title");
                fb.Add(h);
                foreach (var line in r.Lines) { var l = new Label("• " + line); l.AddToClassList("mp-learn"); l.enableRichText = true; l.style.fontSize = 15; fb.Add(l); }
            }
            string after = st.After(run);
            if (after != null) { var a = new Label(after); a.AddToClassList("mp-after"); a.enableRichText = true; fb.Add(a); }
            stepRoot.Add(fb);
            var row = UIKit.ButtonRow(stepRoot);
            bool last = run.Index >= run.Def.Steps.Count - 1;
            UIKit.Button(row, last ? T("DEĞERLENDİRMEYİ GÖR  →", "SEE DEBRIEF  →") : T("DEVAM  →", "CONTINUE  →"), Next, "mp-btn-primary");
            RefreshStatus();
            fb.schedule.Execute(() => RightScroll.ScrollTo(row)).ExecuteLater(60);
            if (r != null) App.Toast((r.G == Grade.Good ? "✓ " : r.G == Grade.Ok ? "≈ " : "✗ ") + st.Title + "  +" + r.Points, 2.2f);
        }

        void Next()
        {
            run.Index++;
            OpenStep(run.Index);
        }

        // =================================================================== debrief
        void ShowDebrief()
        {
            var def = run.Def;
            int pct = run.Percent, stars = run.Stars;
            def.SaveBest(stars, pct);
            float secs = Time.realtimeSinceStartup - run.StartTime;
            debrief = new VisualElement(); debrief.AddToClassList("mp-modal-bg");
            var box = new VisualElement(); box.AddToClassList("mp-modal");
            var t = new Label(T("Vaka tamamlandı — ", "Case complete — ") + def.Title); t.AddToClassList("mp-modal-title");
            box.Add(t);
            var sv = new ScrollView(ScrollViewMode.Vertical); sv.style.flexGrow = 1;
            var c = sv.contentContainer;
            var st = new Label(Stars(stars)); st.AddToClassList("mp-bigstars"); c.Add(st);
            string verdict = stars == 3 ? T("Mükemmel — tam bir medikal fizikçi gibi!", "Excellent — a true medical physicist!")
                           : stars == 2 ? T("İyi iş — birkaç noktayı geliştirebilirsin.", "Good work — a few points to improve.")
                           : stars == 1 ? T("Geçti — geri bildirimleri dikkatle oku.", "Passed — read the feedback carefully.")
                           : T("Tekrar dene — açıklamalar sana yol gösterecek.", "Try again — the explanations will guide you.");
            var vl = new Label(Pct(pct) + "  •  " + verdict); vl.AddToClassList("mp-verdict"); c.Add(vl);
            var sum = UIKit.Section(c, T("Özet", "Summary"));
            UIKit.KV(sum, T("Hasta", "Patient"), def.P.Name + " (" + def.P.AgeSex + ")");
            UIKit.KV(sum, T("Puan", "Score"), run.Points + " / " + run.MaxSoFar);
            UIKit.KV(sum, T("Hastaya verilen efektif doz", "Effective dose to the patient"), run.DoseMSv <= 0 ? T("iyonlaştırıcı radyasyon yok", "no ionising radiation") : run.DoseMSv < 0.1 ? F(run.DoseMSv * 1000, 1) + " µSv" : F(run.DoseMSv, 2) + " mSv");
            if (run.DoseMSv > 0) UIKit.KV(sum, T("Doğal radyasyon eşdeğeri", "Natural background equivalent"), F(run.DoseMSv / 2.4 * 365, run.DoseMSv < 0.1 ? 1 : 0) + T(" gün", " days"));
            UIKit.KV(sum, T("Süre", "Time"), ((int)(secs / 60)) + ":" + ((int)secs % 60).ToString("00"));
            for (int i = 0; i < run.Results.Count; i++)
            {
                var r = run.Results[i];
                var sec = UIKit.Section(c, (i + 1) + ". " + def.Steps[i].Title);
                var pv = UIKit.KV(sec, T("Kararın", "Your decision"), r.Choice);
                var pp = UIKit.KV(sec, T("Puan", "Points"), r.Points + " / " + r.Max);
                UIKit.SetStatus(pp, r.G == Grade.Good ? 0 : r.G == Grade.Ok ? 1 : 2);
                foreach (var line in r.Lines) UIKit.Text(sec, "• " + line);
            }
            UIKit.H(c, T("Öğrenme noktaları", "Learning points"));
            foreach (var l in def.Learn) UIKit.P(c, "• " + T(l.tr, l.en));
            UIKit.H(c, T("Kaynaklar", "References"));
            foreach (var rf in def.Refs) UIKit.Note(c, rf);
            box.Add(sv);
            var row = UIKit.ButtonRow(box);
            UIKit.Button(row, T("↻ Tekrar dene", "↻ Try again"), () => Launch(def.Id));
            UIKit.Button(row, T("Bekleme salonu", "Waiting room"), () => App.Open("cases"));
            var all = CaseLibrary.All;
            int ix = all.FindIndex(x => x.Id == def.Id);
            if (ix >= 0 && ix < all.Count - 1)
            {
                var nx = all[ix + 1];
                UIKit.Button(row, T("Sonraki hasta: ", "Next patient: ") + nx.P.Name.Split(' ')[0] + "  →", () => Launch(nx.Id), "mp-btn-primary");
            }
            debrief.Add(box);
            App.UIRoot.Add(debrief);
            var R = RightScroll.contentContainer;
            R.Clear();
            var s2 = UIKit.Section(R, T("Vaka tamamlandı", "Case complete"));
            var st2 = new Label(Stars(stars) + "  " + Pct(pct)); st2.AddToClassList("mp-stars"); s2.Add(st2);
            UIKit.Button(UIKit.ButtonRow(s2), T("Değerlendirmeyi aç", "Open debrief"), () => { if (debrief != null && debrief.parent == null) App.UIRoot.Add(debrief); }, "mp-btn-primary");
            UIKit.Button(UIKit.ButtonRow(s2), T("Bekleme salonu", "Waiting room"), () => App.Open("cases"));
            Tip.text = "<b>" + def.P.Name.Split(' ')[0] + ":</b> " + T("\"Teşekkür ederim, çok ilgiliydiniz!\"", "\"Thank you, you were very caring!\"");
            patient?.Wave();
        }

        // =================================================================== scene animation
        void StartAnim(string anim, bool forSim)
        {
            if (string.IsNullOrEmpty(anim)) return;
            switch (anim)
            {
                case "xray": beamOn = 1.6f; break;
                case "ct":
                    couchSpeed = 0.5f;
                    couchTarget = isoT.position.z - (patientHome + (patient.BodyPoint(scanFrac) - patient.Pivot.position)).z;
                    break;
                case "mri":
                    couchSpeed = 0.55f;
                    couchTarget = isoT.position.z - (patientHome + (patient.BodyPoint(0.285f) - patient.Pivot.position)).z;
                    break;
                case "pet":
                    couchSpeed = 0.6f; couchPath.Clear();
                    for (int k = 0; k < 7; k++)
                    {
                        float frac = 0.97f - k * 0.1f;
                        float target = isoT.position.z - (patientHome + (patient.BodyPoint(frac) - patient.Pivot.position)).z;
                        couchPath.Enqueue((target, 0.7f));
                    }
                    couchPath.Enqueue((0f, 0f));
                    break;
                case "linac": gantryArc = 0; beamOn = 7f; break;
                case "linac-e": beamOn = 3f; break;
            }
        }

        public override void Tick(float dt)
        {
            base.Tick(dt);
            foreach (var (_, a) in seated) a.Tick(dt);
            patient?.Tick(dt);
            staff?.Tick(dt);

            // couch (and the patient on it)
            if (couch != null)
            {
                if (couchPath.Count > 0 && Mathf.Abs(couchDelta - couchTarget) < 1e-3f)
                {
                    couchDwell -= dt;
                    if (couchDwell <= 0) { var (d, w) = couchPath.Dequeue(); couchTarget = d; couchDwell = w; }
                }
                couchDelta = Mathf.MoveTowards(couchDelta, couchTarget, dt * couchSpeed);
                couch.position = couchHome + Vector3.forward * couchDelta;
                patient.Pivot.position = patientHome + Vector3.forward * couchDelta;
            }
            if (rotor != null)
            {
                bool scanning = sim != null && (sim.Running || simAwait);
                rotorSpeed = Mathf.MoveTowards(rotorSpeed, scanning ? 1f : 0.12f, dt * 0.6f);
                rotorAngle += rotorSpeed * 540f * dt;
                rotor.localRotation = Quaternion.AngleAxis(rotorAngle, Vector3.up);
            }
            if (gantryArc >= 0)
            {
                gantryArc += dt * 55f;
                SetGantry(gantryBase + Mathf.Min(gantryArc, 360f));
                if (gantryArc >= 360f) { gantryArc = -1; SetGantry(gantryBase); }
            }
            if (beamMat != null)
            {
                beamOn = Mathf.Max(0, beamOn - dt);
                float k = Mathf.Clamp01(beamOn * 3f);
                var cc = beamColor; cc.a *= k;
                beamMat.SetColor("_Color", cc);
            }

            // simulation polling
            if (sim != null && simAwait)
            {
                simProg?.Set(sim.Progress);
                if (sim.Poll(vals))
                {
                    simAwait = false; simStale = false;
                    simImg?.Set(sim.Tex); simImg?.MarkDirtyRepaint();
                    if (markL != null) { markL.text = sim.MarkL; markR.text = sim.MarkR; }
                    if (simStatus != null) simStatus.text = T("Tamamlandı. Sonuçları incele, gerekirse ayarları değiştirip tekrar çalıştır.", "Done. Review the results; change settings and run again if needed.");
                    if (run?.Current is ParamStep ps) RefreshReadouts(ps);
                    UpdateSubmit();
                }
            }

            // clock
            if (run != null && timeChip != null && debrief == null)
            {
                timerAcc += dt;
                if (timerAcc > 0.5f)
                {
                    timerAcc = 0;
                    float s = Time.realtimeSinceStartup - run.StartTime;
                    timeChip.text = T("Süre ", "Time ") + ((int)(s / 60)) + ":" + ((int)s % 60).ToString("00");
                }
            }

            // automation queue (screenshots / tests)
            if (actions.Count > 0)
            {
                if (actionWait > 0) actionWait -= dt;
                else if (!(sim != null && simAwait)) RunAction(actions.Dequeue());
            }
        }

        // =================================================================== automation
        public override void AutoAction(string action)
        {
            foreach (var a in action.Split(';')) if (a.Trim().Length > 0) actions.Enqueue(a.Trim());
        }

        void RunAction(string a)
        {
            actionWait = 0.4f;
            var kv = a.Split(new[] { ':' }, 2);
            string cmd = kv[0], arg = kv.Length > 1 ? kv[1] : "";
            switch (cmd)
            {
                case "sel": Select(arg); break;
                case "goto": if (run != null) { run.Index = int.Parse(arg); OpenStep(run.Index); } break;
                case "pick": if (run?.Current is ChoiceStep cs) Pick(cs, int.Parse(arg)); break;
                case "best":
                    if (run?.Current is ChoiceStep cb)
                    {
                        int best = 0; foreach (var o in cb.Options) best = Math.Max(best, o.Points);
                        for (int k = 0; k < cb.Options.Count; k++) if (cb.Multi ? cb.Options[k].Points > 0 : cb.Options[k].Points == best) { Pick(cb, k); if (!cb.Multi) break; }
                    }
                    break;
                case "set":
                {
                    var p = arg.Split('=');
                    if (setters.TryGetValue(p[0], out var s)) s(float.Parse(p[1], System.Globalization.CultureInfo.InvariantCulture));
                    break;
                }
                case "sim": RunSim(); break;
                case "submit": Submit(); break;
                case "next": Next(); break;
                case "wait": actionWait = float.Parse(arg, System.Globalization.CultureInfo.InvariantCulture); break;
                case "cam":
                {
                    var p = arg.Split(',');
                    var tgt = patient != null ? patient.BodyPoint(float.Parse(p[3], System.Globalization.CultureInfo.InvariantCulture)) : new Vector3(0, 1, 1);
                    App.Rig.Frame(tgt, float.Parse(p[2], System.Globalization.CultureInfo.InvariantCulture), float.Parse(p[0], System.Globalization.CultureInfo.InvariantCulture), float.Parse(p[1], System.Globalization.CultureInfo.InvariantCulture), true);
                    break;
                }
                case "hideui": App.UIRoot.style.display = DisplayStyle.None; break;
                case "auto":
                {
                    // play the remaining steps with the best choice / current settings (tests)
                    if (run == null || run.Current == null) break;
                    var seq = new List<string>();
                    if (run.Current is ChoiceStep) seq.Add("best");
                    else if (run.Current is ParamStep ps2 && ps2.MakeSim != null) seq.Add("sim");
                    seq.Add("submit"); seq.Add("next"); seq.Add("auto");
                    seq.AddRange(actions); actions.Clear();
                    foreach (var x in seq) actions.Enqueue(x);
                    break;
                }
            }
        }

        // =================================================================== theory
        protected override void BuildLearn(VisualElement c)
        {
            UIKit.H(c, T("Vaka modu nasıl oynanır?", "How to play the case mode"));
            UIKit.P(c, T("Bekleme salonundan bir hasta seç, dosyasını oku ve vakayı başlat. Her adımda bir karar verirsin: soru yanıtla, cihaz parametrelerini ayarla ya da görüntüyü üret. Fizik motoru sonucu hesaplar; ONAYLA'ya basınca puan ve açıklama gelir.",
                         "Pick a patient in the waiting room, read the file and start the case. At each step you decide: answer a question, set device parameters or produce an image. The physics engine computes the outcome; press CONFIRM to get points and an explanation."));
            UIKit.H(c, T("Radyasyondan korunmanın üç ilkesi (ICRP 103)", "The three principles of radiation protection (ICRP 103)"));
            UIKit.P(c, T("1) Gerekçelendirme: tetkikin faydası riskinden büyük olmalı. 2) Optimizasyon (ALARA): tanısal kaliteyi koruyarak dozu mümkün olan en düşük düzeyde tut. 3) Doz limitleri: çalışan ve halk için (hastaya limit uygulanmaz; tanısal referans düzeyleri kullanılır).",
                         "1) Justification: the benefit must outweigh the risk. 2) Optimisation (ALARA): keep the dose as low as reasonably achievable while preserving diagnostic quality. 3) Dose limits: for workers and the public (not for patients; diagnostic reference levels are used instead)."));
            UIKit.H(c, T("Hızlı formüller", "Quick formulas"));
            UIKit.Eq(c, "I = I₀·e^(−μx)      HVL = ln2 / μ");
            UIKit.Eq(c, "DI = 10·log₁₀(EI / EI_T)");
            UIKit.Eq(c, "CTDIvol = CTDIw / pitch      SSDE = f(D_eff)·CTDIvol");
            UIKit.Eq(c, "EQD2 = n·d·(d + α/β) / (2 + α/β)");
            UIKit.Eq(c, "A(t) = A₀·e^(−ln2·t/T½)      Ḣ = Γ·A / d²");
            UIKit.Eq(c, "S = PD·(1 − e^(−TR/T1))·e^(−TE/T2)      f = 42.58 MHz/T · B₀");
            UIKit.H(c, T("Puanlama", "Scoring"));
            UIKit.P(c, T("★★★ ≥ %85, ★★ ≥ %65, ★ ≥ %40. En iyi sonucun kaydedilir; tekrar oynayarak geliştirebilirsin.", "★★★ ≥ 85 %, ★★ ≥ 65 %, ★ ≥ 40 %. Your best result is saved; replay to improve it."));
            UIKit.Note(c, T("Vakalar eğitim amaçlıdır; klinik karar için kullanılmaz.", "Cases are for education only, not for clinical decisions."));
        }
    }

    /// <summary>The technologist / physicist in scrubs (Worker model) standing idle in a room.</summary>
    public sealed class WorkerActor
    {
        readonly HumanRig rig; float t;
        WorkerActor(HumanRig rig) { this.rig = rig; }

        public static WorkerActor Create(Transform parent, Vector3 pos, float yaw)
        {
            var pivot = new GameObject("Staff").transform;
            pivot.SetParent(parent, false);
            pivot.position = pos; pivot.rotation = Quaternion.Euler(0, yaw, 0);
            var model = Res.Model("Worker", pivot, Vector3.zero, 180f);
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
            return new WorkerActor(new HumanRig(model.transform, pivot.right, pivot.up));
        }

        public void Tick(float dt)
        {
            t += dt;
            rig.ResetPose();
            rig.Arm("L", 0, 3f, -12f); rig.Arm("R", -8f, 3f, -20f);
            rig.Rot("Chest", 0.8f * Mathf.Sin(t * 1.6f));
            rig.Rot("Head", 3f, 0, 12f * Mathf.Sin(t * 0.35f));
        }
    }
}
