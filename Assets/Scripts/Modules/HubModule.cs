using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using MedPhys.Core;
using MedPhys.Physics;
using MedPhys.UI;
using static MedPhys.Core.Loc;

namespace MedPhys.Modules
{
    /// <summary>Main menu: a 3D "showroom" of all machines plus module cards.</summary>
    public sealed class HubModule : ModuleBase
    {
        public override string Id => "hub";
        public override string Title => "MedPhys Lab";
        protected override bool HasFrame => false;

        struct Entry { public string id, tag, tr, en, dtr, den, model; public int tasks; public float yawOffset; }

        static readonly Entry[] Entries =
        {
            new Entry { id = "xray", tag = "X", model = "XRay_Room", tasks = 6, tr = "Röntgen (Radyografi)", en = "X-ray Radiography",
                dtr = "Tüp spektrumu, filtrasyon, HVL, saçılma, dedektör ve hasta dozu.", den = "Tube spectrum, filtration, HVL, scatter, detector and patient dose." },
            new Entry { id = "ct", tag = "CT", model = "CT_Scanner", tasks = 6, tr = "Bilgisayarlı Tomografi", en = "Computed Tomography",
                dtr = "Sinogram, filtreli geri izdüşüm, Hounsfield birimleri, artefaktlar.", den = "Sinogram, filtered back-projection, Hounsfield units, artefacts." },
            new Entry { id = "therapy", tag = "RT", model = "Linac", tasks = 7, tr = "Radyoterapi", en = "Radiotherapy",
                dtr = "Monte Carlo derin doz, Bragg piki, tedavi planı, DVH, radyobiyoloji.", den = "Monte Carlo depth dose, Bragg peak, treatment planning, DVH, radiobiology." },
            new Entry { id = "nucmed", tag = "NM", model = "PET_Scanner", tasks = 5, tr = "Nükleer Tıp / PET", en = "Nuclear Medicine / PET",
                dtr = "Radyoaktif bozunma, Mo-99/Tc-99m jeneratörü, PET koinsidans ve MLEM.", den = "Radioactive decay, Mo-99/Tc-99m generator, PET coincidences and MLEM." },
            new Entry { id = "mri", tag = "MR", model = "MRI_Scanner", tasks = 5, tr = "Manyetik Rezonans", en = "Magnetic Resonance",
                dtr = "Larmor presesyonu, Bloch denklemleri, T1/T2, spin eko, k-uzayı.", den = "Larmor precession, Bloch equations, T1/T2, spin echo, k-space." },
            new Entry { id = "protection", tag = "RK", model = "LeadShield", tasks = 5, tr = "Radyasyondan Korunma", en = "Radiation Protection",
                dtr = "Zaman-mesafe-zırh, ters kare yasası, doz hızı haritası, görevler.", den = "Time-distance-shielding, inverse square law, dose-rate map, missions." },
        };

        readonly Dictionary<Collider, string> clickMap = new Dictionary<Collider, string>();
        Transform centerpiece;
        VisualElement hubPanel;
        bool showAllModules;

        protected override void Build()
        {
            if (App.Flag("-mpOnlyUI")) { BuildUI(); return; }
            if (!App.Flag("-mpNoRoom")) Env.Room(Root.transform, new Color(0.2f, 0.75f, 1f));
            Debug.Log("[MP] hub room ok");
            float R = 3.9f;
            for (int i = 0; i < Entries.Length; i++)
            {
                var e = Entries[i];
                float a = (i / (float)Entries.Length) * Mathf.PI * 2f + 0.35f;
                var p = new Vector3(Mathf.Sin(a) * R, 0, Mathf.Cos(a) * R);
                float yaw = Mathf.Atan2(p.x, p.z) * Mathf.Rad2Deg;
                var holder = Child("Station_" + e.id);
                holder.transform.localPosition = p;
                holder.transform.localRotation = Quaternion.Euler(0, yaw, 0);
                GameObject m;
                if (e.id == "protection")
                {
                    m = Res.Model("LeadShield", holder.transform, new Vector3(-0.4f, 0, 0.3f), 0);
                    Res.Model("LeadPig", holder.transform, new Vector3(0.6f, 0, -0.4f), 0);
                    var sm = Res.Model("SurveyMeter", holder.transform, new Vector3(0.62f, 0.32f, -0.4f), 30);
                    sm.transform.localScale = Vector3.one * 1.4f;
                }
                else if (e.id == "xray")
                {
                    m = Res.Model(e.model, holder.transform, new Vector3(0, 0, 0.3f), 180);
                }
                else if (e.id == "therapy")
                {
                    m = Res.Model(e.model, holder.transform, new Vector3(0, 0, 0.6f), 0);
                    m.transform.localScale = Vector3.one * 0.9f;
                }
                else m = Res.Model(e.model, holder.transform, new Vector3(0, 0, 0.5f), 0);
                // pedestal ring
                var ring = MeshObj("Ring", MeshGen.Ring(1.5f, 0.03f), Res.Additive(new Color(0.2f, 0.8f, 1f, 0.6f), 1.5f), holder.transform);
                ring.transform.localRotation = Quaternion.Euler(90, 0, 0);
                ring.transform.localPosition = new Vector3(0, 0.01f, -0.4f);
                // click target
                var bounds = new Bounds(holder.transform.position, Vector3.zero);
                foreach (var r in holder.GetComponentsInChildren<Renderer>()) bounds.Encapsulate(r.bounds);
                var col = holder.AddComponent<BoxCollider>();
                col.center = holder.transform.InverseTransformPoint(bounds.center);
                col.size = new Vector3(bounds.size.x, bounds.size.y, bounds.size.z) * 0.9f;
                clickMap[col] = e.id;
                Label3D(holder.transform.position + Vector3.up * 2.45f, T(e.tr, e.en));
            }

            Debug.Log("[MP] hub stations ok");
            // centrepiece: rotating hologram phantom
            if (App.Flag("-mpNoPhantom")) { BuildUI(); return; }
            var ph = Phantom.BuildAdult(new PhantomOptions { Tumor = true, Pacemaker = true });
            var view = PhantomView.Create(ph, Root.transform, "HoloPhantom");
            view.transform.localPosition = new Vector3(0, 0.55f, 0);
            centerpiece = view.transform;
            var disc = MeshObj("HoloBase", MeshGen.Ring(0.55f, 0.04f), Res.Additive(new Color(0.3f, 0.9f, 1f, 0.8f), 2f));
            disc.transform.localRotation = Quaternion.Euler(90, 0, 0);
            disc.transform.localPosition = new Vector3(0, 0.02f, 0);
            var cone = MeshObj("HoloCone", MeshGen.Cone(0.55f, 0.5f, 48, 0.25f, 0.0f, 0.5f), Res.Additive(new Color(0.2f, 0.7f, 1f, 0.25f), 1f, 0.6f));
            cone.transform.localRotation = Quaternion.Euler(-90, 0, 0);
            cone.transform.localPosition = new Vector3(0, 0.52f, 0);

            App.Rig.Frame(new Vector3(0.6f, 1.0f, 0), 4.9f, 200f, 14f, true);
            App.Rig.AutoRotate = 5f;
            App.Rig.MinDistance = 2.0f; App.Rig.MaxDistance = 5.2f;
            App.Rig.MinPitch = 2f; App.Rig.MaxPitch = 60f;
            App.Rig.PanBounds = new Bounds(new Vector3(0, 1, 0), new Vector3(6, 1.5f, 6));
            App.Rig.OnTap += OnTap;

            Debug.Log("[MP] hub 3d ok");
            if (!App.Flag("-mpNoUI")) BuildUI();
            Debug.Log("[MP] hub ui ok");
        }

        void OnTap(Vector2 screen)
        {
            var ray = App.Cam.ScreenPointToRay(screen);
            if (UnityEngine.Physics.Raycast(ray, out var hit, 50f) && clickMap.TryGetValue(hit.collider, out var id))
                App.Open(id);
        }

        void BuildUI()
        {
            hubPanel?.RemoveFromHierarchy();
            var panel = new VisualElement();
            hubPanel = panel;
            panel.style.position = Position.Absolute;
            panel.style.left = 28; panel.style.top = 22;
            panel.style.width = App.SmallScreen ? 860 : (showAllModules ? 1000 : 680);
            panel.pickingMode = PickingMode.Ignore;
            App.UIRoot.Add(panel);

            var title = new Label(showAllModules ? T("Laboratuvarları keşfet", "Explore the labs") : "MedPhys Lab");
            title.AddToClassList("mp-hub-title"); panel.Add(title);
            var sub = new Label(showAllModules
                ? T("İstediğin konuyu seçebilirsin. Yeniysen önce kısa başlangıç vakasını dene.",
                    "Choose any topic. If you are new, try the short starter case first.")
                : T("Medikal fiziği küçük adımlarla ve gerçek hesaplamalarla keşfet.",
                    "Explore medical physics in small steps with real calculations."));
            sub.AddToClassList("mp-hub-sub"); panel.Add(sub);

            if (showAllModules) BuildExplorer(panel);
            else BuildWelcome(panel);
            BuildFooter(panel);
            LabelOccluders.Clear();
            LabelOccluders.AddRange(panel.Children());
        }

        void BuildWelcome(VisualElement panel)
        {
            var welcome = new VisualElement(); welcome.AddToClassList("mp-hub-welcome"); panel.Add(welcome);
            var kicker = new Label(T("YENİ BAŞLAYANLAR İÇİN", "FOR FIRST-TIME LEARNERS")); kicker.AddToClassList("mp-hub-kicker"); welcome.Add(kicker);
            var heading = new Label(T("İlk vakayla başla", "Start with your first case")); heading.AddToClassList("mp-hub-heading"); welcome.Add(heading);
            var copy = new Label(T("Emir'in çocuk göğüs grafisi vakasında karar ver, sonucu gör ve her adımın nedenini öğren. Tüm teknik ayarlar daha sonra seni bekliyor.",
                                   "Make decisions in Emir's paediatric chest X-ray case, see the outcome, and learn the reason behind every step. The technical labs will be there when you are ready."));
            copy.AddToClassList("mp-hub-copy"); welcome.Add(copy);
            var actions = UIKit.ButtonRow(welcome);
            UIKit.Button(actions, T("İlk vakayı başlat  →", "Start the first case  →"), () => CasesModule.Launch("emir"), "mp-btn-primary");
            UIKit.Button(actions, T("Öğrenme Merkezi", "Learning Centre"), () => App.Open("learn"));
            UIKit.Button(actions, T("Tüm konuları keşfet", "Explore all topics"), () => { showAllModules = true; BuildUI(); });

            var guide = new VisualElement(); guide.AddToClassList("mp-hub-steps"); panel.Add(guide);
            AddStartStep(guide, "1", T("Vakayı aç", "Open the case"), T("Sana en uygun ilk adımı gösteririz.", "We show you the best first step."));
            AddStartStep(guide, "2", T("Kararını ver", "Make a choice"), T("Tek seferde yalnızca bir karar alırsın.", "You make just one decision at a time."));
            AddStartStep(guide, "3", T("Nedenini öğren", "Learn why"), T("Her yanıtın ardından açıklama ve geri bildirim gelir.", "Each answer comes with an explanation and feedback."));

            var hint = new Label(T("İpucu: Arka plandaki cihazları istediğinde tıklayabilirsin; başlangıç için buna ihtiyacın yok.",
                                   "Tip: You can click a machine in the background whenever you want; you do not need to do that to get started."));
            hint.AddToClassList("mp-hub-hint"); panel.Add(hint);
        }

        void AddStartStep(VisualElement parent, string number, string heading, string body)
        {
            var step = new VisualElement(); step.AddToClassList("mp-hub-step"); parent.Add(step);
            var no = new Label(number); no.AddToClassList("mp-hub-step-no"); step.Add(no);
            var title = new Label(heading); title.AddToClassList("mp-hub-step-title"); step.Add(title);
            var copy = new Label(body); copy.AddToClassList("mp-hub-step-copy"); step.Add(copy);
        }

        void BuildExplorer(VisualElement panel)
        {
            var nav = new VisualElement(); nav.AddToClassList("mp-hub-nav"); panel.Add(nav);
            UIKit.Button(nav, T("← Basit ana menü", "← Simple home"), () => { showAllModules = false; BuildUI(); }, "mp-btn-primary");
            UIKit.Button(nav, T("İlk vakayı başlat", "Start the first case"), () => CasesModule.Launch("emir"));

            var cards = new VisualElement(); cards.AddToClassList("mp-cards"); panel.Add(cards);
            AddCasesCard(cards);
            AddLearnCard(cards);
            foreach (var e in Entries) AddModuleCard(cards, e);
        }

        void AddLearnCard(VisualElement cards)
        {
            var topics = MedPhys.Learn.Curriculum.Topics;
            int lessons = 0;
            foreach (var tp in topics) lessons += tp.Lessons.Count;
            var c = new Button(() => App.Open("learn"));
            c.AddToClassList("mp-card"); c.AddToClassList("mp-card-wide");
            if (App.SmallScreen) { c.style.width = 544; c.style.height = 150; }
            var ic = new Label("Aa"); ic.AddToClassList("mp-card-icon"); c.Add(ic);
            var t = new Label(T("Öğrenme Merkezi — " + topics.Count + " konu, " + lessons + " ders", "Learning Centre — " + topics.Count + " topics, " + lessons + " lessons"));
            t.AddToClassList("mp-card-title"); c.Add(t);
            var d = new Label(T("Atomdan radyoterapiye adım adım dersler, etkileşimli grafikler, çözümlü örnekler, kısa sınavlar ve deneme sınavı.",
                                "Step-by-step lessons from the atom to radiotherapy, interactive figures, worked examples, quizzes and a practice exam."));
            d.AddToClassList("mp-card-text"); c.Add(d);
            var b = new Label("%" + Mathf.RoundToInt(MedPhys.Learn.LearnProgress.Overall() * 100)); b.AddToClassList("mp-card-badge"); c.Add(b);
            cards.Add(c);
        }

        void AddCasesCard(VisualElement cards)
        {
            int stars = 0, n = MedPhys.Cases.CaseLibrary.All.Count;
            foreach (var d in MedPhys.Cases.CaseLibrary.All) stars += d.BestStars;
            var c = new Button(() => App.Open("cases"));
            c.AddToClassList("mp-card"); c.AddToClassList("mp-card-wide");
            if (App.SmallScreen) { c.style.width = 544; c.style.height = 150; }
            var ic = new Label("★"); ic.AddToClassList("mp-card-icon"); c.Add(ic);
            var t = new Label(T("Klinik Vakalar — " + n + " hasta bekliyor", "Clinical Cases — " + n + " patients waiting")); t.AddToClassList("mp-card-title"); c.Add(t);
            var d2 = new Label(T("Gerçek hastalar, gerçek kararlar: çocuk göğüs grafisi, gebe hastada BT, obez hasta, prostat RT, cilt kanseri, PET ve MR güvenliği.",
                                 "Real patients, real decisions: paediatric chest X-ray, CT in pregnancy, a large patient, prostate RT, skin cancer, PET and MR safety."));
            d2.AddToClassList("mp-card-text"); c.Add(d2);
            var b = new Label("★ " + stars + "/" + n * 3); b.AddToClassList("mp-card-badge"); c.Add(b);
            cards.Add(c);
        }

        void AddModuleCard(VisualElement cards, Entry e)
        {
            var c = new Button(() => App.Open(e.id));
            c.AddToClassList("mp-card");
            if (App.SmallScreen) { c.style.width = 265; c.style.height = 150; }
            var ic = new Label(e.tag); ic.AddToClassList("mp-card-icon"); c.Add(ic);
            var t = new Label(T(e.tr, e.en)); t.AddToClassList("mp-card-title"); c.Add(t);
            var d = new Label(T(e.dtr, e.den)); d.AddToClassList("mp-card-text"); c.Add(d);
            int done = Progress.Count(e.id);
            var b = new Label(done + "/" + e.tasks); b.AddToClassList("mp-card-badge");
            if (done == 0) b.style.color = new Color(0.55f, 0.62f, 0.7f);
            c.Add(b);
            cards.Add(c);
        }

        void BuildFooter(VisualElement panel)
        {
            var row = new VisualElement(); row.AddToClassList("mp-hub-footer"); panel.Add(row);
            UIKit.Button(row, T("Dil", "Language") + ": " + Loc.Current.Native, LanguagePicker.Open);
            UIKit.Button(row, T("Yazıyı büyüt", "Larger text") + ": " + Mathf.RoundToInt(PlayerPrefs.GetFloat("ui_scale", 1f) * 100) + "%", () =>
            {
                float s = PlayerPrefs.GetFloat("ui_scale", 1f);
                s = s >= 1.29f ? 0.8f : s + 0.1f;
                App.SetUiScale(s);
                BuildUI();
            });
            if (showAllModules)
            {
                UIKit.Button(row, T("Hakkında", "About"), OpenLearn);
                UIKit.Button(row, T("İlerlemeyi sıfırla", "Reset progress"), ConfirmResetProgress, "mp-btn-danger");
            }
            var credit = new Label(T("Geliştirici", "Developer") + ": " + Author + "  •  " + RepoUrl);
            credit.AddToClassList("mp-hub-credit"); panel.Add(credit);
        }

        const string Author = "Alp-creator";
        const string RepoUrl = "github.com/Alp-creator/medphys-lab";

        void ConfirmResetProgress()
        {
            var overlay = new VisualElement(); overlay.AddToClassList("mp-modal-bg");
            var box = new VisualElement(); box.AddToClassList("mp-modal"); box.AddToClassList("mp-confirm");
            var title = new Label(T("İlerlemeyi sıfırlamak istiyor musun?", "Reset all progress?")); title.AddToClassList("mp-modal-title"); box.Add(title);
            UIKit.P(box, T("Tamamlanan görevler, vaka yıldızları ve ders ilerlemesi silinir. Bu işlem geri alınamaz.",
                            "Completed tasks, case stars and lesson progress will be deleted. This cannot be undone."));
            var row = UIKit.ButtonRow(box);
            UIKit.Button(row, T("Vazgeç", "Cancel"), () => overlay.RemoveFromHierarchy());
            UIKit.Button(row, T("İlerlemeyi sıfırla", "Reset progress"), () =>
            {
                Progress.ResetAll();
                MedPhys.Cases.CaseLibrary.ClearProgress();
                MedPhys.Learn.LearnProgress.ResetAll();
                overlay.RemoveFromHierarchy();
                App.Open("hub");
            }, "mp-btn-danger");
            overlay.Add(box);
            App.UIRoot.Add(overlay);
        }

        public override void Tick(float dt)
        {
            base.Tick(dt);
            if (centerpiece != null) centerpiece.Rotate(0, 20f * dt, 0, Space.World);
        }

        protected override void BuildLearn(VisualElement c)
        {
            UIKit.H(c, "MedPhys Lab");
            UIKit.P(c, T("Bu uygulama medikal fiziğin temel konularını etkileşimli 3B simülasyonlarla öğretmek için hazırlandı. Tüm hesaplar gerçek fizik modellerine ve referans verilere dayanır; ancak klinik kullanım için DEĞİLDİR.",
                         "This app teaches the core topics of medical physics through interactive 3D simulations. All calculations are based on real physical models and reference data, but it is NOT for clinical use."));
            UIKit.H(c, T("Geliştirici", "Developer"));
            UIKit.P(c, Author + " — " + RepoUrl);
            UIKit.H(c, T("Veri kaynakları", "Data sources"));
            UIKit.P(c, "• NIST XCOM / Hubbell & Seltzer — " + T("foton zayıflama ve enerji soğurma katsayıları (20 malzeme)", "photon attenuation and energy-absorption coefficients (20 materials)"));
            UIKit.P(c, "• NIST PSTAR / ESTAR — " + T("proton ve elektron durdurma gücü / menzil referansları", "proton and electron stopping power / range references"));
            UIKit.P(c, "• ICRP 103, ICRU 44 — " + T("doku bileşimleri, doz büyüklükleri, ağırlık faktörleri", "tissue compositions, dose quantities, weighting factors"));
            UIKit.P(c, "• Kramers (1923), Bortfeld (1997), Bethe–Bloch, Klein–Nishina, Bloch (1946)");
            UIKit.H(c, T("Modüller", "Modules"));
            foreach (var e in Entries) UIKit.P(c, "• <b>" + T(e.tr, e.en) + "</b> — " + T(e.dtr, e.den));
            UIKit.H(c, T("Kontroller", "Controls"));
            UIKit.P(c, T("Fare: sol sürükle = döndür, sağ sürükle = kaydır, tekerlek = yakınlaştır. Dokunmatik: tek parmak = döndür, iki parmak = yakınlaştır/kaydır.",
                         "Mouse: left drag = orbit, right drag = pan, wheel = zoom. Touch: one finger = orbit, two fingers = zoom/pan."));
        }
    }
}
