using System;
using System.Collections.Generic;
using UnityEngine;
using MedPhys.Physics;
using MedPhys.UI;
using static MedPhys.Core.Loc;

namespace MedPhys.Cases
{
    /// <summary>The clinical cases of the case mode. Every scored decision rests on a real calculation or guideline.</summary>
    public static class CaseLibrary
    {
        static List<CaseDef> all;
        static bool builtEn;
        /// <summary>All cases, rebuilt when the UI language changes (texts are resolved at build time).</summary>
        public static List<CaseDef> All
        {
            get
            {
                if (all == null || builtEn != En) { builtEn = En; all = new List<CaseDef> { Emir(), Can(), Mehmet(), Elif(), Zeynep(), Ayse(), Hasan() }; }
                return all;
            }
        }
        public static CaseDef Get(string id) => All.Find(c => c.Id == id);

        /// <summary>Forgets the best stars / scores of every case.</summary>
        public static void ClearProgress()
        {
            foreach (var c in All) { PlayerPrefs.DeleteKey("case_stars_" + c.Id); PlayerPrefs.DeleteKey("case_pct_" + c.Id); }
            PlayerPrefs.Save();
        }
        public static void Reset() { all = null; }   // rebuild after a language change

        static ChoiceStep Q(string tr, string en, string ptr, string pen) => new ChoiceStep { Tr = tr, En = en, PromptTr = ptr, PromptEn = pen };
        static Color C(float r, float g, float b) => new Color(r, g, b);
        static string Pm(double v, int d = 0) => (v >= 0 ? "+" : "") + F(v, d);
        static int Lvl(bool good, bool ok) => good ? 0 : ok ? 1 : 2;
        static double Eqd2(double d, int n, double ab) => n * d * (d + ab) / (2 + ab);

        // ============================================================================ 1. Emir — paediatric chest X-ray
        static CaseDef Emir()
        {
            var c = new CaseDef
            {
                Id = "emir", Modality = "xray", Difficulty = 1,
                P = new PatientInfo { Model = "Child", Name = "Emir Kaya", Age = 8, HeightCm = 128, WeightKg = 26,
                    Skin = C(0.87f, 0.69f, 0.56f), Hair = C(0.18f, 0.11f, 0.07f), Top = C(0.86f, 0.22f, 0.20f), Bottom = C(0.25f, 0.40f, 0.62f), Shoe = C(0.95f, 0.95f, 0.97f) },
                TitleTr = "Çocuk göğüs grafisi", TitleEn = "Paediatric chest X-ray",
                ComplaintTr = "3 gündür ateş (39 °C) ve öksürük", ComplaintEn = "Fever (39 °C) and cough for 3 days",
                RequestTr = "Çocuk acil: PA akciğer grafisi — pnömoni? (Dr. Aydın)", RequestEn = "Paediatric ED: PA chest radiograph — pneumonia? (Dr Aydın)",
                IntroTr = "Merhaba! Bu makine acıtır mı? Annem dışarıda bekleyebilir mi?", IntroEn = "Hi! Will the machine hurt? Can my mum wait outside?",
            };
            c.History.Add(("Annesi eşlik ediyor; ilk görüntülemesi", "Accompanied by his mother; first imaging"));
            c.History.Add(("Ayakta durabiliyor, iş birliği iyi", "Can stand, cooperative"));
            c.History.Add(("Kronik hastalığı yok, aşıları tam", "No chronic illness, fully vaccinated"));
            c.Vitals.Add(("Ateş", "Temperature", "38.9 °C"));
            c.Vitals.Add(("Nabız", "Pulse", "112 /dk"));
            c.Vitals.Add(("SpO₂", "SpO₂", "%95"));
            c.Vitals.Add(("Solunum", "Resp. rate", "28 /dk"));

            c.Steps.Add(Q("Projeksiyon ve pozisyon", "Projection and positioning",
                    "Emir ayakta durabiliyor ve iş birliği yapıyor. Hangi tekniği seçersin?",
                    "Emir can stand and cooperates. Which technique do you choose?")
                .Add(10, "PA, ayakta, SID 180 cm", "PA, erect, SID 180 cm",
                     "Doğru. PA'da kalp dedektöre yakındır → daha az büyütme; uzun SID geometrik büyütmeyi ve bulanıklığı azaltır. Önde yer alan meme ve tiroid de PA'da daha az doz alır.",
                     "Correct. In PA the heart is close to the detector → less magnification; a long SID reduces geometric magnification and blur. Anterior organs (breast, thyroid) also receive less dose in PA.")
                .Add(5, "AP, yatarak, SID 100 cm", "AP, supine, SID 100 cm",
                     "Bebekler ve yatağa bağımlı hastalar için uygundur; ancak kalp gölgesi büyür ve öndeki organlar daha çok doz alır.",
                     "Appropriate for infants and bed-bound patients, but the heart shadow is magnified and anterior organs receive more dose.")
                .Add(2, "Önce lateral grafi", "Lateral view first",
                     "Rutin pnömoni değerlendirmesinde ilk tercih değildir; lateral daha yüksek doz ister ve tek başına yetersizdir.",
                     "Not the first choice for suspected pneumonia; a lateral needs more dose and is insufficient on its own.")
                .Add(0, "Toraks BT", "Chest CT",
                     "Gerekçesiz: pediatrik toraks BT ≈ 1–3 mSv, PA grafi ≈ 0.01 mSv. Gerekçelendirme ilkesi (ICRP 103) ihlal edilir.",
                     "Not justified: paediatric chest CT ≈ 1–3 mSv versus ≈ 0.01 mSv for a PA radiograph. Violates the justification principle (ICRP 103)."));

            c.Steps.Add(Q("Koruma ve kolimasyon", "Shielding and collimation",
                    "Anne, Emir'e kurşun önlük takılmasını istiyor. Ne yaparsın?",
                    "The mother asks for a lead apron on Emir. What do you do?")
                .Add(10, "Alanı akciğerlere sıkı kolime ederim; gonad koruyucu kullanmam ve anneye nedenini açıklarım",
                     "Collimate tightly to the lungs; no gonad shield, and explain why to the mother",
                     "Güncel öneri (AAPM 2019, BIR/IPEM 2020): kolimasyon en etkili korumadır. Alan dışındaki gonad dozu zaten çok düşüktür; koruyucu alana kayarsa anatomiyi örter ve otomatik pozlama (AEC) dozu artırır.",
                     "Current guidance (AAPM 2019, BIR/IPEM 2020): collimation is the most effective protection. Out-of-field gonad dose is already tiny; a shield that slips into the field hides anatomy and makes the AEC raise the dose.")
                .Add(4, "Pelvisine kurşun önlük sararım", "Wrap a lead apron around the pelvis",
                     "Zararsız görünür ama alana girebilir → tekrar çekim ve AEC kaynaklı doz artışı riski. Annenin kaygısını açıklamayla gidermek daha iyidir.",
                     "Looks harmless but may enter the field → repeat exposure and AEC-driven dose increase. Better to address the mother's worry with an explanation.")
                .Add(0, "Geniş alan açar, tiroide kurşun koyarım", "Open a wide field and put lead on the thyroid",
                     "Geniş alan saçılmayı ve DAP'ı artırır; asıl koruma alanı daraltmaktır.",
                     "A wide field increases scatter and DAP; the real protection is a narrow field."));

            Phantom ph = null;
            Func<Phantom> getPh = () => ph ?? (ph = Phantom.BuildPatient(new PhantomOptions { Tumor = true, TumorCenter = new Vector3(-8.0f, 1.5f, 45f), TumorRadius = 3.0f }, 0.62f, 0.68f, 0.73f));
            const double drl = 70;   // µGy, game reference close to European paediatric DRLs for a ~30 kg PA chest
            var xs = new ParamStep
            {
                Tr = "Pozlama parametreleri", En = "Exposure factors", Max = 30, Anim = "xray",
                PromptTr = "Emir'in göğsü ≈ 15 cm kalınlığında (erişkin ≈ 22 cm). kVp, mAs, filtre, grid ve alanı ayarla, sonra ÇEK. Hedef: DI −1…+1 ve ESAK ≤ 70 µGy.",
                PromptEn = "Emir's chest is ≈ 15 cm thick (adult ≈ 22 cm). Set kVp, mAs, filter, grid and field, then EXPOSE. Goal: DI −1…+1 and ESAK ≤ 70 µGy.",
                SayTr = "Emir: \"Nefesimi tutayım mı? Tamam, tutuyorum!\"", SayEn = "Emir: \"Should I hold my breath? OK, holding!\"",
                MakeSim = () => new XRayCaseSim(getPh(), 52.5f * 0.73f, 0.24f),
            };
            xs.P(Param.Slider("kvp", "Tüp gerilimi", "Tube voltage", 50, 125, 70, 1, v => F(v, 0) + " kVp"))
              .P(Param.Slider("mas", "Tüp akımı × süre", "Current × time", 0.5f, 10, 4f, 0.1f, v => F(v, 1) + " mAs"))
              .P(Param.Seg("filt", "Ek filtrasyon", "Added filtration", 0, new[] { "Yok", "1 mm Al + 0.1 mm Cu" }, new[] { "None", "1 mm Al + 0.1 mm Cu" }))
              .P(Param.Check("grid", "Saçılma önleyici grid", "Anti-scatter grid", true))
              .P(Param.Slider("fw", "Alan genişliği", "Field width", 10, 43, 35, 1, v => F(v, 0) + " cm"))
              .P(Param.Slider("fh", "Alan yüksekliği", "Field height", 10, 43, 43, 1, v => F(v, 0) + " cm"));
            xs.Live = v =>
            {
                var l = new List<Readout> { new Readout(T("Alan (dedektörde)", "Field (at detector)"), F(v["fw"] * v["fh"], 0) + " cm²", Lvl(v["fw"] * v["fh"] <= 960, v["fw"] * v["fh"] <= 1400)) };
                if (!v.HasSim) { l.Add(new Readout(T("Görüntü", "Image"), T("henüz çekilmedi", "not exposed yet"))); return l; }
                double di = v.S("di"), esak = v.S("esak");
                l.Add(new Readout(T("Sapma indeksi (DI)", "Deviation index (DI)"), Pm(di, 2), Lvl(Math.Abs(di) <= 1, Math.Abs(di) <= 2)));
                l.Add(new Readout("ESAK", F(esak, 1) + " µGy  (DRL " + F(drl, 0) + ")", Lvl(esak <= drl, esak <= 2 * drl)));
                l.Add(new Readout(T("Efektif doz", "Effective dose"), F(v.S("E") * 1000, 1) + " µSv"));
                l.Add(new Readout("SNR", F(v.S("snr"), 0)));
                l.Add(new Readout("HVL", F(v.S("hvl"), 2) + " mm Al"));
                return l;
            };
            xs.Evaluate = (v, run) =>
            {
                var r = new StepResult { Max = 30 };
                double di = v.S("di"), esak = v.S("esak"), area = v["fw"] * v["fh"];
                int pDi = Math.Abs(di) <= 1 ? 10 : Math.Abs(di) <= 2 ? 5 : 0;
                r.Line("DI " + Pm(di, 2) + (pDi == 10 ? " → doğru pozlama." : di > 0 ? " → fazla doz (DI +1 ≈ %26, +3 ≈ 2× doz)." : " → az pozlama: kuantum gürültüsü, tanısal değil."),
                       "DI " + Pm(di, 2) + (pDi == 10 ? " → correct exposure." : di > 0 ? " → over-exposure (DI +1 ≈ 26 %, +3 ≈ 2× dose)." : " → under-exposure: quantum noise, non-diagnostic."));
                int pEs = pDi == 0 && di < -2 ? 0 : esak <= drl ? 10 : esak <= 2 * drl ? 5 : 0;
                r.Line("ESAK " + F(esak, 1) + " µGy" + (esak <= drl ? " ≤ DRL: ALARA." : " > DRL: dozu azalt (daha yüksek kVp + Cu filtre, grid'i çıkar, mAs'ı düşür)."),
                       "ESAK " + F(esak, 1) + " µGy" + (esak <= drl ? " ≤ DRL: ALARA." : " > DRL: reduce dose (higher kVp + Cu filter, remove the grid, lower mAs)."));
                int pGrid = v.B("grid") ? 0 : 5;
                r.Line(v.B("grid") ? "Grid kullanıldı: ~15 cm'lik çocuk göğsünde saçılma azdır; grid (Bucky faktörü 2–4) dozu gereksiz artırır." : "Grid yok: ince çocuk göğsünde doğru seçim.",
                       v.B("grid") ? "Grid used: a ~15 cm child chest produces little scatter; a grid (Bucky factor 2–4) raises the dose needlessly." : "No grid: right choice for a thin paediatric chest.");
                int pF = area <= 960 ? 5 : area <= 1400 ? 2 : 0;
                r.Line("Alan " + F(v["fw"], 0) + "×" + F(v["fh"], 0) + " cm" + (pF == 5 ? ": akciğerlere uygun." : ": göğüs ≈ 24×26 cm; alanı daralt (DAP ve saçılma)."),
                       "Field " + F(v["fw"], 0) + "×" + F(v["fh"], 0) + " cm" + (pF == 5 ? ": fits the lungs." : ": the chest is ≈ 24×26 cm; collimate (DAP and scatter)."));
                if (v.I("filt") == 1) r.Line("Cu filtre düşük enerjili fotonları süzer: aynı DI için cilt dozu belirgin düşer.", "The Cu filter removes low-energy photons: much lower skin dose for the same DI.");
                r.Points = pDi + pEs + pGrid + pF;
                r.Choice = F(v["kvp"], 0) + " kVp, " + F(v["mas"], 1) + " mAs, " + (v.I("filt") == 1 ? "Cu, " : "") + (v.B("grid") ? "grid, " : T("gridsiz, ", "no grid, ")) + F(v["fw"], 0) + "×" + F(v["fh"], 0) + " cm";
                r.DoseMSv = v.S("E");
                r.Quality = pDi / 10f;
                run.Memo["E_uSv"] = v.S("E") * 1000;
                r.G = StepResult.GradeOf(r.Points, r.Max);
                return r;
            };
            c.Steps.Add(xs);

            var talk = Q("Aileyle iletişim", "Talking to the family", null, null);
            talk.PromptFn = r => T("Anne soruyor: \"Bu çekim oğluma zarar verir mi?\" Emir'in efektif dozu ≈ " + F(r.M("E_uSv", 10), 1) + " µSv. Ne dersin?",
                                   "The mother asks: \"Can this X-ray harm my son?\" Emir's effective dose ≈ " + F(r.M("E_uSv", 10), 1) + " µSv. What do you say?");
            talk.Add(10, "", "", "Doğru: sayısal karşılaştırma (doğal radyasyon ≈ 2.4 mSv/yıl ≈ 6.6 µSv/gün), gerekçelendirme ve ALARA birlikte anlatıldı.",
                                 "Right: a numeric comparison (natural background ≈ 2.4 mSv/yr ≈ 6.6 µSv/day), justification and ALARA explained together.")
                .Add(3, "Hiçbir risk yok, sıfır radyasyon.", "There is no risk at all, zero radiation.",
                     "Yanıltıcı. LNT modeline göre çok küçük de olsa bir risk varsayılır; dürüst ama güven veren bir dil kullanılmalı.",
                     "Misleading. The LNT model assumes a tiny but non-zero risk; use honest yet reassuring language.")
                .Add(0, "Evet, tehlikeli; mümkünse çekmeyelim.", "Yes, it is dangerous; let's avoid it if possible.",
                     "Gereksiz kaygı yaratır ve tanıyı geciktirir; gerekçelendirilmiş bir tetkik engellenmemeli.",
                     "Creates needless fear and delays diagnosis; a justified examination should not be blocked.");
            talk.Options[0].TextFn = r =>
            {
                double e = r.M("E_uSv", 10), days = e / 6.6;
                return T("Doz ≈ " + F(e, 1) + " µSv: yaklaşık " + F(days, 1) + " günlük doğal radyasyona eşit. Pnömoni tanısının faydası riskten çok büyük.",
                         "Dose ≈ " + F(e, 1) + " µSv: about " + F(days, 1) + " days of natural background. The benefit of diagnosing pneumonia far outweighs the risk.");
            };
            c.Steps.Add(talk);

            c.Learn.Add(("Çocuklar radyasyona erişkinlerden 2–3 kat daha duyarlıdır ve yaşam beklentileri uzundur → ALARA daha da önemli.", "Children are 2–3× more radiosensitive than adults and have a long life expectancy → ALARA matters even more."));
            c.Learn.Add(("Pediatrik göğüs: gridsiz, ek Cu filtrasyon, yüksek kVp / düşük mAs, sıkı kolimasyon, kısa ekspojur (hareket).", "Paediatric chest: no grid, added Cu filtration, high kVp / low mAs, tight collimation, short exposure (motion)."));
            c.Learn.Add(("DI = 10·log₁₀(EI / EI_hedef): +1 ≈ %26 fazla, −1 ≈ %21 az, +3 ≈ 2× dedektör dozu.", "DI = 10·log₁₀(EI / EI_target): +1 ≈ 26 % more, −1 ≈ 21 % less, +3 ≈ 2× detector dose."));
            c.Learn.Add(("Gonad koruyucuları rutin olarak önerilmez; en iyi koruma kolimasyondur.", "Gonad shields are no longer recommended routinely; collimation is the best protection."));
            c.Refs.Add("ICRP Publication 103 (2007)"); c.Refs.Add("AAPM Position Statement on Patient Gonadal and Fetal Shielding (2019)");
            c.Refs.Add("IEC 62494-1 (EI / DI)"); c.Refs.Add("European Commission RP 185 — Paediatric DRLs (2018)");
            return c;
        }

        // ============================================================================ 2. Elif — possible pregnancy, appendicitis, low-dose CT
        static CaseDef Elif()
        {
            var c = new CaseDef
            {
                Id = "elif", Modality = "ct", Difficulty = 2,
                P = new PatientInfo { Model = "Woman", Name = "Elif Demir", Age = 29, Female = true, HeightCm = 165, WeightKg = 61,
                    Skin = C(0.88f, 0.70f, 0.58f), Hair = C(0.20f, 0.12f, 0.08f), Top = C(0.62f, 0.52f, 0.80f), Bottom = C(0.12f, 0.12f, 0.15f), Shoe = C(0.85f, 0.85f, 0.88f) },
                TitleTr = "Gebelik şüphesi ve apandisit", TitleEn = "Possible pregnancy and appendicitis",
                ComplaintTr = "Sağ alt karın ağrısı, bulantı (12 saat)", ComplaintEn = "Right lower abdominal pain, nausea (12 h)",
                RequestTr = "Acil cerrahi: Akut apandisit? Kontrastlı karın-pelvis BT (Dr. Şahin)", RequestEn = "Emergency surgery: acute appendicitis? Contrast CT abdomen-pelvis (Dr Şahin)",
                IntroTr = "Çok ağrım var... Bir de hamile olabilir miyim bilmiyorum.", IntroEn = "It hurts a lot... and I don't know whether I might be pregnant.",
            };
            c.History.Add(("Son adet tarihi: 5 hafta önce (düzensiz)", "Last menstrual period: 5 weeks ago (irregular)"));
            c.History.Add(("Doğum kontrolü yok", "No contraception"));
            c.History.Add(("Alerji yok; kreatinin normal", "No allergies; creatinine normal"));
            c.Vitals.Add(("Ateş", "Temperature", "37.9 °C"));
            c.Vitals.Add(("Nabız", "Pulse", "98 /dk"));
            c.Vitals.Add(("Tansiyon", "Blood pressure", "118/76"));
            c.Vitals.Add(("Lökosit", "WBC", "13.8 ×10⁹/L"));

            c.Steps.Add(new ChoiceStep
            {
                Tr = "İlk adım", En = "First step",
                PromptTr = "BT istemi geldi. Elif'in son adeti 5 hafta önce. İyonlaştırıcı tetkikten önce ne yaparsın?",
                PromptEn = "A CT request arrives. Elif's last period was 5 weeks ago. What do you do before any ionising examination?",
                AfterTr = "β-hCG POZİTİF: yaklaşık 5 haftalık gebelik.", AfterEn = "β-hCG POSITIVE: about 5 weeks pregnant.",
            }
                .Add(10, "Gebelik durumunu sorgular, β-hCG testi isterim", "Ask about pregnancy and request a β-hCG test",
                     "Doğru: pelvis ışın alanındaysa doğurganlık çağındaki her kadında gebelik sorgulanmalıdır (ICRP 84, IAEA SSG-46).",
                     "Correct: pregnancy must be ruled out in every woman of childbearing age when the pelvis is in the beam (ICRP 84, IAEA SSG-46).")
                .Add(2, "Kurşun önlük verip hemen çekerim", "Give a lead apron and scan right away",
                     "Karın BT'sinde uterus doğrudan ışın alanındadır; önlük koruyamaz ve görüntüyü bozar.",
                     "In abdominal CT the uterus is in the primary beam; an apron cannot protect and degrades the image.")
                .Add(0, "İstem var, hemen tararım", "There is a request, scan immediately",
                     "Gebelik sorgusu atlandı: fetüs habersizce ışınlanabilirdi.",
                     "Pregnancy screening skipped: the fetus could have been irradiated unknowingly."));

            c.Steps.Add(new ChoiceStep
            {
                Tr = "Görüntüleme yöntemi", En = "Imaging modality",
                PromptTr = "Gebe hastada akut apandisit şüphesi. İlk tercih ne olmalı?",
                PromptEn = "Suspected appendicitis in a pregnant patient. What should come first?",
                AfterTr = "US: apendiks görüntülenemedi. Gece MR yok; cerrah perforasyon riskini vurguluyor. Ortak karar: düşük dozlu BT.",
                AfterEn = "US: appendix not visualised. No MRI at night; the surgeon stresses the risk of perforation. Shared decision: low-dose CT.",
            }
                .Add(10, "Ultrason; yetersizse kontrastsız MR", "Ultrasound; non-contrast MRI if inconclusive",
                     "ACR Appropriateness Criteria ve ACOG: önce US, sonra gadolinyumsuz MR — iyonlaştırıcı radyasyon yok.",
                     "ACR Appropriateness Criteria and ACOG: ultrasound first, then MRI without gadolinium — no ionising radiation.")
                .Add(4, "Gadolinyumlu MR", "MRI with gadolinium",
                     "Gadolinyum plasentayı geçer; gebelikte yalnızca zorunluysa kullanılır. Apandisit için kontrastsız MR yeterlidir.",
                     "Gadolinium crosses the placenta; in pregnancy only if essential. Non-contrast MRI suffices for appendicitis.")
                .Add(3, "Doğrudan kontrastlı BT", "Contrast CT straight away",
                     "Mümkün ama ilk tercih değil; önce US ve MR denenmeli.",
                     "Possible but not first-line; US and MRI should come first.")
                .Add(0, "Ayakta direkt karın grafisi", "Erect abdominal radiograph",
                     "Apandisit için tanısal değeri düşüktür; gereksiz doz.",
                     "Low diagnostic value for appendicitis; unnecessary dose."));

            Phantom ph = null; CTCaseSim simRef = null;
            Func<Phantom> getPh = () => ph ?? (ph = Phantom.BuildPatient(new PhantomOptions { Tumor = false }, 0.92f, 0.92f, 0.94f, 0.8f));
            float sliceZ = 7.5f * 0.94f;
            Func<double> deff = () => { if (simRef != null) return simRef.DEff; getPh().Diameters(sliceZ, out float la, out float ap); return Mathf.Sqrt(la * ap); };
            var ct = new ParamStep
            {
                Tr = "Düşük dozlu BT protokolü", En = "Low-dose CT protocol", Max = 25, Anim = "ct",
                PromptTr = "Fetal doz ≈ SSDE (uterus tam alanda). Tanısal gürültü sınırı ≈ 22 HU (düşük doz apandisit protokolü). kV, mAs, pitch ve tarama uzunluğunu seç, sonra TARA.",
                PromptEn = "Fetal dose ≈ SSDE (uterus fully in the field). Diagnostic noise limit ≈ 22 HU (low-dose appendicitis protocol). Choose kV, mAs, pitch and scan length, then SCAN.",
                SayTr = "Elif: \"Lütfen bebeğim için en az dozu kullanın.\"", SayEn = "Elif: \"Please use the lowest dose for my baby.\"",
                MakeSim = () => simRef = new CTCaseSim(getPh(), sliceZ, "bladder"),
            };
            ct.P(Param.Seg("kv", "Tüp gerilimi", "Tube voltage", 2, new[] { "80 kV", "100 kV", "120 kV", "140 kV" }, null))
              .P(Param.Slider("mas", "Tüp yükü", "Tube load", 20, 300, 250, 5, v => F(v, 0) + " mAs"))
              .P(Param.Slider("pitch", "Pitch", "Pitch", 0.6f, 1.5f, 1.0f, 0.1f, v => F(v, 1)))
              .P(Param.Slider("len", "Tarama uzunluğu", "Scan length", 15, 50, 45, 1, v => F(v, 0) + " cm"));
            Func<Vals, double> ctdi = v => CasePhysics.CtdiVol(CTCaseSim.Kvps[v.I("kv")], v["mas"], v["pitch"]);
            ct.Live = v =>
            {
                double cd = ctdi(v), f = CasePhysics.SsdeFactor(deff()), ssde = cd * f, dlp = cd * v["len"];
                var l = new List<Readout>
                {
                    new Readout("CTDIvol", F(cd, 1) + " mGy"),
                    new Readout(T("Efektif çap → SSDE", "Effective diameter → SSDE"), F(deff(), 1) + " cm → " + F(ssde, 1) + " mGy"),
                    new Readout(T("Tahmini fetal doz", "Estimated fetal dose"), F(ssde, 1) + " mGy", Lvl(ssde <= 15, ssde <= 30)),
                    new Readout("DLP / E", F(dlp, 0) + " mGy·cm / " + F(dlp * 0.015, 1) + " mSv"),
                };
                l.Add(v.HasSim ? new Readout(T("Gürültü (mesane ROI, SD)", "Noise (bladder ROI, SD)"), F(v.S("noise"), 1) + " HU", Lvl(v.S("noise") <= 15, v.S("noise") <= 22))
                               : new Readout(T("Görüntü", "Image"), T("henüz taranmadı", "not scanned yet")));
                return l;
            };
            ct.Evaluate = (v, run) =>
            {
                var r = new StepResult { Max = 25 };
                double cd = ctdi(v), ssde = cd * CasePhysics.SsdeFactor(deff()), noise = v.S("noise"), dlp = cd * v["len"];
                int pN = noise <= 15 ? 10 : noise <= 22 ? 8 : 0;
                r.Line("Gürültü " + F(noise, 1) + " HU" + (noise <= 22 ? ": tanısal." : ": tanısal değil — tekrar çekim gerekir, bu da toplam dozu artırır."),
                       "Noise " + F(noise, 1) + " HU" + (noise <= 22 ? ": diagnostic." : ": non-diagnostic — a repeat would be needed, raising the total dose."));
                int pD = pN == 0 ? 0 : ssde <= 15 ? 10 : ssde <= 30 ? 6 : 2;
                r.Line("Fetal doz ≈ SSDE = " + F(ssde, 1) + " mGy (CTDIvol " + F(cd, 1) + " × f " + F(CasePhysics.SsdeFactor(deff()), 2) + ").",
                       "Fetal dose ≈ SSDE = " + F(ssde, 1) + " mGy (CTDIvol " + F(cd, 1) + " × f " + F(CasePhysics.SsdeFactor(deff()), 2) + ").");
                int pL = v["len"] <= 32 ? 5 : 0;
                r.Line(pL > 0 ? "Tarama uzunluğu sınırlandı (iliak kanattan simfize): DLP azaldı." : "Tarama uzunluğu gereğinden uzun: apandisit için iliak kanat–simfiz yeterli.",
                       pL > 0 ? "Scan length limited (iliac crest to symphysis): lower DLP." : "Scan longer than needed: iliac crest to symphysis suffices for appendicitis.");
                if (v.I("kv") <= 1) r.Line("Düşük kV iyot kontrastını artırır (K-kenarı 33 keV); ince hastada iyi bir seçim.", "Low kV boosts iodine contrast (K-edge 33 keV); a good choice in a slim patient.");
                r.Points = pN + pD + pL;
                r.Choice = CTCaseSim.Kvps[v.I("kv")] + " kV, " + F(v["mas"], 0) + " mAs, pitch " + F(v["pitch"], 1) + ", " + F(v["len"], 0) + " cm";
                r.DoseMSv = dlp * 0.015;
                r.Quality = noise <= 22 ? 1f : 0.2f;
                run.Memo["fetal"] = ssde;
                r.G = StepResult.GradeOf(r.Points, r.Max);
                return r;
            };
            c.Steps.Add(ct);

            var talk = Q("Danışmanlık", "Counselling", null, null);
            talk.PromptFn = r => T("Elif ağlayarak soruyor: \"Bebeğime zarar verdi mi?\" Tahmini fetal doz ≈ " + F(r.M("fetal", 15), 1) + " mGy.",
                                   "Elif asks in tears: \"Did it harm my baby?\" Estimated fetal dose ≈ " + F(r.M("fetal", 15), 1) + " mGy.");
            talk.Add(10, "Doz, malformasyon için kabul edilen ~100 mGy eşiğinin çok altında; çocukluk çağı kanser riskindeki artış çok küçük (≈ %0.06 / 10 mGy). Gebeliği sonlandırmak için tıbbi gerekçe yok.",
                         "The dose is far below the ~100 mGy threshold for malformations; the added childhood-cancer risk is very small (≈ 0.06 % per 10 mGy). There is no medical reason to terminate.",
                         "ICRP 84 ve ACOG: < 50 mGy'de deterministik etki gözlenmez; < 100 mGy için sonlandırma önerilmez. Risk iletişimi sayısal ve sakin olmalı.",
                         "ICRP 84 and ACOG: no deterministic effects below 50 mGy; termination is not recommended below 100 mGy. Communicate risk with numbers and calm.")
                .Add(3, "Hiçbir risk yok.", "There is no risk at all.",
                     "Kısmen güven verici ama yanlış: stokastik risk çok küçüktür ama sıfır değildir.",
                     "Reassuring but inaccurate: the stochastic risk is tiny, not zero.")
                .Add(0, "Riskli; gebeliği sonlandırmayı düşünmelisiniz.", "It is risky; you should consider termination.",
                     "Bilimsel olarak gerekçesiz ve zararlı bir öneri.", "Scientifically unfounded and harmful advice.");
            c.Steps.Add(talk);

            c.Learn.Add(("Doğurganlık çağında pelvis alandaysa önce gebelik sorgusu (gerekirse β-hCG).", "Women of childbearing age with the pelvis in the beam: rule out pregnancy first (β-hCG if needed)."));
            c.Learn.Add(("Gebede apandisit: US → kontrastsız MR → gerekirse düşük dozlu BT.", "Appendicitis in pregnancy: US → non-contrast MRI → low-dose CT if needed."));
            c.Learn.Add(("Fetal doz ≈ SSDE (uterus tam alanda). CTDIvol bir fantom dozudur; hastanın boyutuna göre düzeltilmelidir.", "Fetal dose ≈ SSDE (uterus in the field). CTDIvol is a phantom dose and must be corrected for patient size."));
            c.Learn.Add(("Organogenezde (3–8. hafta) deterministik eşik ~100–200 mGy; tanısal tetkikler bunun çok altındadır.", "During organogenesis (weeks 3–8) the deterministic threshold is ~100–200 mGy; diagnostic exams are far below it."));
            c.Refs.Add("ICRP Publication 84 — Pregnancy and Medical Radiation (2000)"); c.Refs.Add("ACOG Committee Opinion 723 (2017)");
            c.Refs.Add("AAPM Report 204 — Size-Specific Dose Estimates (2011)"); c.Refs.Add("Kim et al., NEJM 366:1596 (2012) — low-dose CT for appendicitis");
            return c;
        }

        // ============================================================================ 3. Mehmet — obese patient abdominal CT
        static CaseDef Mehmet()
        {
            var c = new CaseDef
            {
                Id = "mehmet", Modality = "ct", Difficulty = 2,
                P = new PatientInfo { Model = "Obese", Name = "Mehmet Yıldız", Age = 52, HeightCm = 178, WeightKg = 128,
                    Skin = C(0.80f, 0.60f, 0.47f), Hair = C(0.10f, 0.08f, 0.07f), Top = C(0.32f, 0.44f, 0.30f), Bottom = C(0.20f, 0.20f, 0.23f), Shoe = C(0.16f, 0.12f, 0.10f) },
                TitleTr = "İri hastada karın BT", TitleEn = "Abdominal CT in a large patient",
                ComplaintTr = "Sol alt karın ağrısı, ateş", ComplaintEn = "Left lower abdominal pain, fever",
                RequestTr = "Genel cerrahi: Divertikülit / apse? Kontrastlı karın BT (Dr. Tunç)", RequestEn = "General surgery: diverticulitis / abscess? Contrast CT abdomen (Dr Tunç)",
                IntroTr = "Geçen sefer çok uzun sürdü... Bu makineye sığar mıyım?", IntroEn = "Last time it took ages... Will I fit in the machine?",
            };
            c.History.Add(("Tip 2 diyabet (metformin)", "Type 2 diabetes (metformin)"));
            c.History.Add(("eGFR 74 mL/dk/1.73 m²", "eGFR 74 mL/min/1.73 m²"));
            c.History.Add(("Masa ağırlık sınırı 227 kg, gantri açıklığı 70 cm", "Table limit 227 kg, gantry bore 70 cm"));
            c.Vitals.Add(("Ateş", "Temperature", "38.4 °C"));
            c.Vitals.Add(("Nabız", "Pulse", "102 /dk"));
            c.Vitals.Add(("Tansiyon", "Blood pressure", "142/88"));
            c.Vitals.Add(("CRP", "CRP", "186 mg/L"));

            c.Steps.Add(Q("Doz ölçütü", "Dose metric",
                    "Konsolda CTDIvol görünüyor. Bu iri hastada hasta dozunu en iyi hangisi temsil eder?",
                    "The console shows CTDIvol. Which quantity best represents the dose to this large patient?")
                .Add(10, "SSDE — efektif çapa göre düzeltilmiş CTDIvol", "SSDE — CTDIvol corrected for the effective diameter",
                     "CTDIvol 32 cm PMMA fantomdaki dozdur, hastanın dozu değildir. AAPM 204/220: SSDE = f(D_eff)·CTDIvol; iri hastada f < 1.",
                     "CTDIvol is the dose in a 32 cm PMMA phantom, not the patient's. AAPM 204/220: SSDE = f(D_eff)·CTDIvol; in large patients f < 1.")
                .Add(4, "CTDIvol'ün kendisi", "CTDIvol itself",
                     "Tarayıcı çıktısını gösterir, hastanın boyutunu hesaba katmaz.", "It describes scanner output and ignores patient size.")
                .Add(2, "DLP", "DLP",
                     "DLP = CTDIvol × uzunluk; toplam ışınlamayı gösterir, organ dozunu değil.", "DLP = CTDIvol × length; it describes total irradiation, not organ dose."));

            Phantom ph = null; CTCaseSim simRef = null;
            Func<Phantom> getPh = () => ph ?? (ph = Phantom.BuildPatient(new PhantomOptions { Tumor = false }, 1.04f, 1.02f, 1.0f, 6.5f));
            float sliceZ = 30f;
            Func<(float lat, float ap)> dims = () => { getPh().Diameters(sliceZ, out float la, out float ap); return (la, ap); };
            var q2 = Q("Fizik: neden gürültü artar?", "Physics: why does noise rise?", null, null);
            q2.PromptFn = r => { var d = dims(); return T("Mehmet'in karın çapı " + F(d.lat, 0) + " × " + F(d.ap, 0) + " cm (ortalama erişkin ≈ 34 × 24). Su için μ(70 keV) ≈ 0.19 cm⁻¹. Fazladan 12 cm doku, dedektöre ulaşan fotonları ne kadar azaltır?",
                                                                "Mehmet's abdomen is " + F(d.lat, 0) + " × " + F(d.ap, 0) + " cm (average adult ≈ 34 × 24). For water μ(70 keV) ≈ 0.19 cm⁻¹. By how much do 12 extra cm of tissue reduce the photons reaching the detector?"); };
            q2.Add(10, "≈ 10 kat (e^(−0.19·12) ≈ 0.10)", "≈ 10× (e^(−0.19·12) ≈ 0.10)",
                   "Doğru: I/I₀ = e^(−μx). Gürültü σ ~ 1/√N olduğundan aynı mAs ile σ ≈ √10 ≈ 3.2 kat artar.",
                   "Correct: I/I₀ = e^(−μx). Since σ ~ 1/√N, the noise rises ≈ √10 ≈ 3.2× at the same mAs.")
              .Add(3, "≈ 2 kat", "≈ 2×", "Bu yalnızca ~3.6 cm (bir HVL) için geçerli.", "That holds for only ~3.6 cm (one HVL).")
              .Add(0, "Değişmez; mAs aynı", "No change; same mAs", "Zayıflama üstel olarak kalınlığa bağlıdır.", "Attenuation depends exponentially on thickness.");
            c.Steps.Add(q2);

            var ct = new ParamStep
            {
                Tr = "Boyuta uygun protokol", En = "Size-adapted protocol", Max = 25, Anim = "ct",
                PromptTr = "İri hastada kabul edilen gürültü: karaciğer ROI'sinde ≤ 22 HU (ortalama erişkinde ≈ 12 HU). kV ve mAs seç, TARA. SSDE'yi 40 mGy'nin altında tut.",
                PromptEn = "Accepted noise in a large patient: ≤ 22 HU in the liver ROI (≈ 12 HU in an average adult). Choose kV and mAs, SCAN. Keep SSDE below 40 mGy.",
                SayTr = "Mehmet: \"Kollarımı başımın üstüne mi koyayım? Tamam.\"", SayEn = "Mehmet: \"Arms above my head? OK.\"",
                MakeSim = () => simRef = new CTCaseSim(getPh(), sliceZ, "liver", 62f),
            };
            ct.P(Param.Seg("kv", "Tüp gerilimi", "Tube voltage", 2, new[] { "80 kV", "100 kV", "120 kV", "140 kV" }, null))
              .P(Param.Slider("mas", "Tüp yükü (pitch 1)", "Tube load (pitch 1)", 50, 700, 200, 10, v => F(v, 0) + " mAs"));
            Func<double> deff = () => { var d = dims(); return Math.Sqrt(d.lat * d.ap); };
            ct.Live = v =>
            {
                double cd = CasePhysics.CtdiVol(CTCaseSim.Kvps[v.I("kv")], v["mas"], 1), f = CasePhysics.SsdeFactor(deff());
                var l = new List<Readout>
                {
                    new Readout("CTDIvol", F(cd, 1) + " mGy"),
                    new Readout(T("Efektif çap / f", "Effective diameter / f"), F(deff(), 1) + " cm / " + F(f, 2)),
                    new Readout("SSDE", F(cd * f, 1) + " mGy", Lvl(cd * f <= 40, cd * f <= 55)),
                    new Readout(T("Efektif doz (40 cm)", "Effective dose (40 cm)"), F(cd * 40 * 0.015, 1) + " mSv"),
                };
                l.Add(v.HasSim ? new Readout(T("Gürültü (karaciğer ROI, SD)", "Noise (liver ROI, SD)"), F(v.S("noise"), 1) + " HU", Lvl(v.S("noise") <= 22, v.S("noise") <= 30))
                               : new Readout(T("Görüntü", "Image"), T("henüz taranmadı", "not scanned yet")));
                return l;
            };
            ct.Evaluate = (v, run) =>
            {
                var r = new StepResult { Max = 25 };
                int kv = v.I("kv");
                double cd = CasePhysics.CtdiVol(CTCaseSim.Kvps[kv], v["mas"], 1), ssde = cd * CasePhysics.SsdeFactor(deff()), noise = v.S("noise");
                int pN = noise <= 22 ? 15 : noise <= 30 ? 8 : 0;
                r.Line("Karaciğer gürültüsü " + F(noise, 1) + " HU" + (noise <= 22 ? ": apse ve yağ dokusu ayırt edilebilir." : ": düşük kontrastlı apse gürültüde kaybolabilir."),
                       "Liver noise " + F(noise, 1) + " HU" + (noise <= 22 ? ": abscess and fat are distinguishable." : ": a low-contrast abscess may be lost in the noise."));
                int pD = ssde <= 40 ? 10 : ssde <= 55 ? 5 : 0;
                r.Line("SSDE " + F(ssde, 1) + " mGy (CTDIvol " + F(cd, 1) + " mGy × f " + F(CasePhysics.SsdeFactor(deff()), 2) + ").",
                       "SSDE " + F(ssde, 1) + " mGy (CTDIvol " + F(cd, 1) + " mGy × f " + F(CasePhysics.SsdeFactor(deff()), 2) + ").");
                if (kv == 0) r.Line("80 kV iri hastada 'foton yetersizliği' (photon starvation) çizgilerine ve aşırı gürültüye yol açar.", "80 kV in a large patient causes photon-starvation streaks and excessive noise.");
                if (kv >= 2) r.Line("120–140 kV iri hastada daha fazla foton geçirir: aynı gürültü için daha az mAs gerekir.", "120–140 kV penetrates large patients better: less mAs for the same noise.");
                r.Points = pN + pD;
                r.Choice = CTCaseSim.Kvps[kv] + " kV, " + F(v["mas"], 0) + " mAs";
                r.DoseMSv = cd * 40 * 0.015;
                r.Quality = noise <= 22 ? 1f : noise <= 30 ? 0.6f : 0.2f;
                r.G = StepResult.GradeOf(r.Points, r.Max);
                return r;
            };
            c.Steps.Add(ct);

            c.Steps.Add(Q("Kontrast ve böbrek", "Contrast and kidneys",
                    "eGFR 74 mL/dk/1.73 m², metformin kullanıyor. İyotlu kontrast için ne yaparsın?",
                    "eGFR 74 mL/min/1.73 m², on metformin. What do you do about iodinated contrast?")
                .Add(10, "Kontrast veririm; eGFR ≥ 30 ve akut böbrek hasarı yoksa metformin kesilmez", "Give contrast; with eGFR ≥ 30 and no AKI, metformin is not stopped",
                     "ACR (2023) ve ESUR 10.0: eGFR ≥ 30'da metformin sürdürülür; apse tanısında IV kontrast gereklidir.",
                     "ACR (2023) and ESUR 10.0: metformin continues with eGFR ≥ 30; IV contrast is needed to detect an abscess.")
                .Add(3, "Metformini 48 saat kesip sonra çekerim", "Stop metformin for 48 h, then scan",
                     "Eski bir öneri; eGFR ≥ 30'da gerekmez ve tanıyı geciktirir.", "Outdated advice; unnecessary with eGFR ≥ 30 and delays diagnosis.")
                .Add(0, "Kontrastsız çekerim", "Scan without contrast",
                     "Apse duvarı kontrastla belirginleşir; kontrastsız BT tanıyı kaçırabilir.", "The abscess wall enhances with contrast; non-contrast CT may miss it."));

            c.Learn.Add(("CTDIvol fantom dozudur; hastaya özgü doz için SSDE = f(D_eff)·CTDIvol kullan (AAPM 204/220).", "CTDIvol is a phantom dose; use SSDE = f(D_eff)·CTDIvol for patient-specific dose (AAPM 204/220)."));
            c.Learn.Add(("Gürültü σ ~ 1/√(mAs) ve kalınlıkla üstel artar; iri hastada 120–140 kV + yüksek mAs (ya da AEC) gerekir.", "Noise σ ~ 1/√(mAs) and grows exponentially with thickness; large patients need 120–140 kV + more mAs (or AEC)."));
            c.Learn.Add(("Düşük kV iyot kontrastını artırır ama iri hastada foton yetersizliğine yol açar.", "Low kV increases iodine contrast but causes photon starvation in large patients."));
            c.Refs.Add("AAPM Report 204 (2011), AAPM Report 220 (2014)"); c.Refs.Add("ACR Manual on Contrast Media (2023)"); c.Refs.Add("ICRP Publication 135 — DRLs (2017)");
            return c;
        }

        // ============================================================================ 4. Hasan — prostate EBRT
        static CaseDef Hasan()
        {
            var c = new CaseDef
            {
                Id = "hasan", Modality = "therapy", Difficulty = 3,
                P = new PatientInfo { Model = "OldMan", Name = "Hasan Çelik", Age = 68, HeightCm = 172, WeightKg = 81,
                    Skin = C(0.82f, 0.62f, 0.50f), Hair = C(0.80f, 0.80f, 0.82f), Top = C(0.80f, 0.72f, 0.58f), Bottom = C(0.35f, 0.36f, 0.40f), Shoe = C(0.25f, 0.18f, 0.12f) },
                TitleTr = "Prostat kanseri — dış ışın tedavisi", TitleEn = "Prostate cancer — external beam RT",
                ComplaintTr = "Prostat adenokarsinomu, orta risk (Gleason 3+4, PSA 8.6)", ComplaintEn = "Prostate adenocarcinoma, intermediate risk (Gleason 3+4, PSA 8.6)",
                RequestTr = "Radyasyon onkolojisi: küratif EBRT, prostat ± seminal vezikül (Dr. Kılıç)", RequestEn = "Radiation oncology: curative EBRT, prostate ± seminal vesicles (Dr Kılıç)",
                IntroTr = "Ameliyat istemiyorum. Bu ışın tedavisi nasıl bir şey, acır mı?", IntroEn = "I don't want surgery. What is this radiation treatment like, does it hurt?",
            };
            c.History.Add(("Hipertansiyon (kontrol altında)", "Hypertension (controlled)"));
            c.History.Add(("Rektum ön duvarı prostata komşu", "Anterior rectal wall abuts the prostate"));
            c.History.Add(("3 altın fidusyal işaretçi yerleştirildi", "3 gold fiducial markers implanted"));
            c.Vitals.Add(("KPS", "KPS", "90"));
            c.Vitals.Add(("PSA", "PSA", "8.6 ng/mL"));
            c.Vitals.Add(("Evre", "Stage", "cT2b N0 M0"));

            string[] en = { "Co-60", "6 MV", "10 MV", "18 MV", "12 MeV e⁻" };
            double[] mv = { 1.25, 6, 10, 18, -12 };
            var beam = new ParamStep
            {
                Tr = "Işın enerjisi", En = "Beam energy", Max = 10, RequireSim = false, ChartHeight = 230,
                PromptTr = "Prostat, önden ≈ 11 cm, yandan ≈ 20 cm derinlikte. Derin dozu (PDD) karşılaştır ve VMAT için bir enerji seç.",
                PromptEn = "The prostate lies ≈ 11 cm deep from the front and ≈ 20 cm from the side. Compare the depth doses (PDD) and choose an energy for VMAT.",
                SayTr = "Hasan: \"Bu kadar derin bir yere nasıl ulaşıyorsunuz?\"", SayEn = "Hasan: \"How do you reach something that deep?\"",
            };
            beam.P(Param.Seg("e", "Işın", "Beam", 1, en, en));
            Func<int, double, double> pdd = (i, d) => mv[i] < 0 ? DepthDose.ElectronPdd(-mv[i], d) : DepthDose.PhotonPdd(mv[i], d);
            beam.ChartInit = ch =>
            {
                ch.XMin = 0; ch.XMax = 30; ch.YMin = 0; ch.YMax = 105; ch.AutoY = false; ch.XLabel = T("derinlik (cm)", "depth (cm)"); ch.YLabel = "PDD %";
                var cols = new[] { new Color(0.6f, 0.6f, 0.7f), new Color(0.3f, 0.8f, 1f), new Color(0.4f, 1f, 0.5f), new Color(1f, 0.6f, 0.3f), new Color(1f, 0.4f, 0.8f) };
                for (int i = 0; i < en.Length; i++) ch.Add(en[i], cols[i], 1.5f);
                ch.VLines.Add((11, new Color(1, 0.85f, 0.3f), T("ön", "AP")));
                ch.VLines.Add((20, new Color(1, 0.85f, 0.3f), T("yan", "lat")));
            };
            beam.Plot = (v, ch) =>
            {
                for (int i = 0; i < en.Length; i++)
                {
                    var s = ch.AllSeries[i]; s.Pts.Clear(); s.Width = i == v.I("e") ? 3.5f : 1.2f;
                    for (float d = 0; d <= 30.01f; d += 0.25f) s.Pts.Add(new Vector2(d, (float)pdd(i, d)));
                }
            };
            beam.Live = v =>
            {
                int i = v.I("e");
                var l = new List<Readout> { new Readout(T("PDD @ 11 cm / 20 cm", "PDD @ 11 cm / 20 cm"), F(pdd(i, 11), 0) + " % / " + F(pdd(i, 20), 0) + " %", Lvl(pdd(i, 20) >= 35, pdd(i, 20) >= 25)) };
                l.Add(new Readout(T("Yüzey dozu (cilt koruması)", "Surface dose (skin sparing)"), F(pdd(i, 0), 0) + " %"));
                if (mv[i] >= 15) l.Add(new Readout(T("Fotonötron", "Photoneutrons"), T("var (> 10 MV)", "yes (> 10 MV)"), 1));
                return l;
            };
            beam.Evaluate = (v, run) =>
            {
                int i = v.I("e");
                var r = new StepResult { Max = 10, Choice = en[i] };
                r.Points = i == 1 || i == 2 ? 10 : i == 3 ? 6 : i == 0 ? 4 : 0;
                if (i == 1 || i == 2) r.Line("6–10 MV ile VMAT, çok sayıda açıdan derin hedefe yeterli doz verir; cilt korunur, nötron yok denecek kadar azdır.", "6–10 MV VMAT gives enough dose to the deep target from many angles; skin is spared and neutrons are negligible.");
                if (i == 3) r.Line("18 MV derine iyi gider ama (γ,n) reaksiyonuyla fotonötron üretir → ek tüm vücut dozu ve oda zırhı gereksinimi.", "18 MV penetrates well but produces photoneutrons via (γ,n) → extra whole-body dose and shielding needs.");
                if (i == 0) r.Line("Co-60'ın PDD'si düşük ve yarı gölgesi geniştir; modern küratif prostat tedavisinde tercih edilmez.", "Co-60 has a low PDD and wide penumbra; not used for modern curative prostate RT.");
                if (i == 4) r.Line("12 MeV elektronun menzili ≈ 6 cm (Rp ≈ E/2): prostata ulaşamaz.", "12 MeV electrons reach only ≈ 6 cm (Rp ≈ E/2): they cannot reach the prostate.");
                r.Quality = r.Points / 10f;
                r.G = StepResult.GradeOf(r.Points, r.Max);
                return r;
            };
            c.Steps.Add(beam);

            var frac = new ParamStep
            {
                Tr = "Fraksiyonasyon (LQ modeli)", En = "Fractionation (LQ model)", Max = 25, RequireSim = false, ChartHeight = 210, Anim = "linac",
                PromptTr = "Prostat α/β ≈ 1.5 Gy, rektum (geç etki) α/β ≈ 3 Gy. Hedef: tümör EQD2 74–95 Gy, rektum ön duvarı (%100 doz alır varsay) EQD2 ≤ 80 Gy.",
                PromptEn = "Prostate α/β ≈ 1.5 Gy, rectum (late effects) α/β ≈ 3 Gy. Goal: tumour EQD2 74–95 Gy, anterior rectal wall (assume it gets 100 %) EQD2 ≤ 80 Gy.",
                SayTr = "Hasan: \"Kaç gün gelmem gerekecek? Köyden geliyorum.\"", SayEn = "Hasan: \"How many days will I need to come? I travel in from the village.\"",
            };
            frac.P(Param.Slider("d", "Fraksiyon dozu", "Dose per fraction", 1.8f, 8f, 2f, 0.05f, v => F(v, 2) + " Gy"))
                .P(Param.Slider("n", "Fraksiyon sayısı", "Number of fractions", 5, 45, 39, 1, v => F(v, 0)));
            frac.ChartInit = ch =>
            {
                ch.XMin = 1; ch.XMax = 10; ch.YMin = 40; ch.YMax = 110; ch.AutoY = false; ch.XLabel = "α/β (Gy)"; ch.YLabel = "EQD2 (Gy)";
                ch.Add(T("Seçilen şema", "Chosen schedule"), new Color(0.3f, 0.85f, 1f), 3f);
                ch.Add("78 Gy / 39 fx", new Color(0.7f, 0.7f, 0.8f), 1.5f).Dashed = true;
                ch.VLines.Add((1.5f, new Color(1f, 0.6f, 0.3f), T("prostat", "prostate")));
                ch.VLines.Add((3f, new Color(1f, 0.35f, 0.4f), T("rektum", "rectum")));
            };
            frac.Plot = (v, ch) =>
            {
                ch.AllSeries[0].Pts.Clear(); ch.AllSeries[1].Pts.Clear();
                for (float ab = 1; ab <= 10.01f; ab += 0.25f)
                {
                    ch.AllSeries[0].Pts.Add(new Vector2(ab, (float)Eqd2(v["d"], v.I("n"), ab)));
                    ch.AllSeries[1].Pts.Add(new Vector2(ab, (float)Eqd2(2, 39, ab)));
                }
            };
            frac.Live = v =>
            {
                double d = v["d"]; int n = v.I("n");
                double tum = Eqd2(d, n, 1.5), rec = Eqd2(d, n, 3);
                return new List<Readout>
                {
                    new Readout(T("Toplam doz", "Total dose"), F(d * n, 1) + " Gy"),
                    new Readout(T("Tümör EQD2 (α/β 1.5)", "Tumour EQD2 (α/β 1.5)"), F(tum, 1) + " Gy", Lvl(tum >= 74 && tum <= 95, tum >= 70 && tum <= 105)),
                    new Readout(T("Rektum EQD2 (α/β 3)", "Rectum EQD2 (α/β 3)"), F(rec, 1) + " Gy", Lvl(rec <= 80, rec <= 86)),
                    new Readout(T("Tedavi süresi", "Overall time"), F(Radiobiology.TreatmentDays(n) / 7.0, 1) + T(" hafta", " weeks")),
                };
            };
            frac.Evaluate = (v, run) =>
            {
                double d = v["d"]; int n = v.I("n");
                double tum = Eqd2(d, n, 1.5), rec = Eqd2(d, n, 3);
                var r = new StepResult { Max = 25, Choice = F(d * n, 1) + " Gy / " + n + " fx (" + F(d, 2) + " Gy)" };
                int pT = tum >= 74 && tum <= 95 ? 15 : (tum >= 70 && tum <= 105) ? 8 : 0;
                int pR = rec <= 80 ? 10 : rec <= 86 ? 5 : 0;
                r.Line("Tümör EQD2 = n·d·(d + 1.5)/(2 + 1.5) = " + F(tum, 1) + " Gy" + (pT == 15 ? " — küratif aralıkta." : tum < 74 ? " — yetersiz doz, biyokimyasal nüks riski." : " — gereğinden yüksek."),
                       "Tumour EQD2 = n·d·(d + 1.5)/(2 + 1.5) = " + F(tum, 1) + " Gy" + (pT == 15 ? " — curative range." : tum < 74 ? " — underdosed, risk of biochemical failure." : " — higher than needed."));
                r.Line("Rektum EQD2 (α/β 3) = " + F(rec, 1) + " Gy" + (pR == 10 ? " — kabul edilebilir." : " — geç rektal toksisite (kanama, proktit) riski."),
                       "Rectum EQD2 (α/β 3) = " + F(rec, 1) + " Gy" + (pR == 10 ? " — acceptable." : " — risk of late rectal toxicity (bleeding, proctitis)."));
                if (n <= 20 && pT > 0) r.Line("Hipofraksiyon: prostatın α/β'sı rektumdan düşük olduğundan büyük fraksiyonlar tümöre avantaj sağlar (CHHiP 60/20, PACE-B 36.25/5) ve hasta daha az gelir.",
                                              "Hypofractionation: the prostate's α/β is lower than the rectum's, so large fractions favour the tumour (CHHiP 60/20, PACE-B 36.25/5) and the patient travels less.");
                r.Points = pT + pR;
                r.Quality = r.Points / 25f;
                r.G = StepResult.GradeOf(r.Points, r.Max);
                return r;
            };
            c.Steps.Add(frac);

            c.Steps.Add(Q("Görüntü kılavuzluğu (IGRT)", "Image guidance (IGRT)",
                    "Prostat, mesane ve rektum dolumuna göre günden güne yer değiştirir. Her fraksiyonda nasıl doğrularsın?",
                    "The prostate moves from day to day with bladder and rectal filling. How do you verify each fraction?")
                .Add(10, "Her fraksiyonda kV görüntüleme / KIBT ile altın işaretçileri eşleştiririm", "Daily kV imaging / CBCT matching the gold markers",
                     "Günlük IGRT 5–10 mm'lik organ hareketini düzeltir ve PTV marjını ~5 mm'ye indirmeye izin verir (van Herk: M = 2.5Σ + 0.7σ).",
                     "Daily IGRT corrects 5–10 mm organ motion and allows ~5 mm PTV margins (van Herk: M = 2.5Σ + 0.7σ).")
                .Add(4, "Haftada bir portal görüntü", "Weekly portal image",
                     "Sistematik kurulum hatasını yakalar, günlük organ hareketini yakalamaz → daha büyük marj ve rektum dozu.",
                     "Catches systematic set-up error but not daily organ motion → larger margins and rectal dose.")
                .Add(0, "Cilt işaretleri ve lazerler yeterli", "Skin marks and lasers are enough",
                     "Cilt işaretleri iç organ hareketini göstermez; hedefi kaçırma riski yüksektir.",
                     "Skin marks do not show internal motion; high risk of missing the target."));

            c.Steps.Add(Q("Hastanın sorusu", "The patient's question",
                    "Hasan: \"Tedaviden sonra torunlarımı kucaklayabilir miyim? Radyoaktif olur muyum?\"",
                    "Hasan: \"Can I hug my grandchildren after treatment? Will I be radioactive?\"")
                .Add(10, "Evet; dış ışın tedavisi sizi radyoaktif yapmaz, cihaz kapanınca radyasyon kalmaz", "Yes; external beam RT does not make you radioactive — nothing remains once the machine is off",
                     "Doğru. (Kalıcı I-125 tohum implantında ise kısa süreli yakın temas kısıtlamaları olurdu.)",
                     "Correct. (A permanent I-125 seed implant would involve short-term close-contact restrictions.)")
                .Add(4, "Sadece hamile kadınlardan uzak durun", "Just keep away from pregnant women",
                     "Dış ışında böyle bir kısıtlama yoktur; bu brakiterapi tohumları için geçerli olabilir.", "No such restriction for external beam; that may apply to brachytherapy seeds.")
                .Add(0, "Bir hafta kimseyle temas etmeyin", "Avoid all contact for a week",
                     "Dış ışın tedavisinde gereksiz ve kaygı yaratan yanlış bilgi.", "Wrong and anxiety-provoking for external beam therapy."));

            c.Learn.Add(("EQD2 = n·d·(d + α/β)/(2 + α/β). Düşük α/β'lı tümörler (prostat ≈ 1.5) hipofraksiyondan yararlanır.", "EQD2 = n·d·(d + α/β)/(2 + α/β). Low-α/β tumours (prostate ≈ 1.5) benefit from hypofractionation."));
            c.Learn.Add(("> 10 MV'de fotonötron üretimi başlar; VMAT ile 6–10 MV derin hedefler için yeterlidir.", "Photoneutron production starts above ~10 MV; with VMAT, 6–10 MV suffices for deep targets."));
            c.Learn.Add(("Günlük IGRT, organ hareketini düzelterek marjı ve rektum dozunu azaltır.", "Daily IGRT corrects organ motion, shrinking margins and rectal dose."));
            c.Refs.Add("Dearnaley et al., Lancet Oncol 17:1047 (2016) — CHHiP"); c.Refs.Add("Tree et al., Lancet Oncol 23:1308 (2022) — PACE-B");
            c.Refs.Add("Marks et al., IJROBP 76(3) Suppl (2010) — QUANTEC"); c.Refs.Add("van Herk et al., IJROBP 47:1121 (2000)");
            return c;
        }

        // ============================================================================ 5. Ayşe — basal cell carcinoma, electrons
        static CaseDef Ayse()
        {
            var c = new CaseDef
            {
                Id = "ayse", Modality = "therapy", Difficulty = 3,
                P = new PatientInfo { Model = "OldWoman", Name = "Ayşe Arslan", Age = 74, Female = true, HeightCm = 158, WeightKg = 66,
                    Skin = C(0.90f, 0.74f, 0.64f), Hair = C(0.86f, 0.86f, 0.88f), Top = C(0.55f, 0.20f, 0.30f), Bottom = C(0.25f, 0.25f, 0.30f), Shoe = C(0.20f, 0.18f, 0.18f) },
                TitleTr = "Yanakta cilt kanseri — elektron tedavisi", TitleEn = "Skin cancer on the cheek — electron therapy",
                ComplaintTr = "Sol yanakta 2.5 cm bazal hücreli karsinom (biyopsi ile)", ComplaintEn = "2.5 cm basal cell carcinoma on the left cheek (biopsy-proven)",
                RequestTr = "Radyasyon onkolojisi: elektron tedavisi, sol yanak BCC; US derinliği 1.0 cm (Dr. Öz)", RequestEn = "Radiation oncology: electron therapy, left cheek BCC; ultrasound depth 1.0 cm (Dr Öz)",
                IntroTr = "Kızım, yüzümde iz kalır mı? Bir de gözüme bir şey olmasın.", IntroEn = "Dear, will it leave a scar on my face? And please don't hurt my eye.",
            };
            c.History.Add(("Ameliyatı reddediyor (antikoagülan kullanıyor)", "Declines surgery (on anticoagulants)"));
            c.History.Add(("Lezyonun altında maksilla kemiği ≈ 2 cm derinlikte", "Maxillary bone ≈ 2 cm beneath the lesion"));
            c.History.Add(("Lezyon sol göze 3 cm uzaklıkta", "Lesion 3 cm from the left eye"));
            c.Vitals.Add(("KPS", "KPS", "80"));
            c.Vitals.Add(("Lezyon", "Lesion", "25 × 22 mm"));
            c.Vitals.Add(("Derinlik", "Depth", "10 mm"));

            int[] mev = { 6, 9, 12, 15 };
            float[] bol = { 0, 0.5f, 1.0f, 1.5f };
            var el = new ParamStep
            {
                Tr = "Elektron enerjisi ve bolus", En = "Electron energy and bolus", Max = 25, RequireSim = false, ChartHeight = 230, Anim = "linac-e",
                PromptTr = "Hedef: ciltten 1.0 cm derinliğe kadar her yerde ≥ %90 doz; 2 cm'deki kemikte ≤ %60. Enerji ve bolus kalınlığı seç.",
                PromptEn = "Goal: ≥ 90 % dose everywhere from the skin down to 1.0 cm; ≤ 60 % at the bone at 2 cm. Choose energy and bolus thickness.",
                SayTr = "Ayşe: \"Bu jöle gibi şey ne? Yüzüme mi koyacaksınız?\"", SayEn = "Ayşe: \"What is this jelly-like thing? Will you put it on my face?\"",
            };
            el.P(Param.Seg("e", "Elektron enerjisi", "Electron energy", 1, new[] { "6 MeV", "9 MeV", "12 MeV", "15 MeV" }, null))
              .P(Param.Seg("b", "Bolus", "Bolus", 0, new[] { "0", "0.5 cm", "1.0 cm", "1.5 cm" }, null));
            Func<Vals, double, double> dose = (v, depth) => CasePhysics.ElectronPdd(mev[v.I("e")], depth + bol[v.I("b")]);
            Func<Vals, double> cover = v => { double m = 200; for (double d = 0; d <= 1.0001; d += 0.05) m = Math.Min(m, dose(v, d)); return m; };
            el.ChartInit = ch =>
            {
                ch.XMin = 0; ch.XMax = 6; ch.YMin = 0; ch.YMax = 105; ch.AutoY = false; ch.XLabel = T("hasta derinliği (cm)", "patient depth (cm)"); ch.YLabel = "%";
                ch.Add(T("Derin doz", "Depth dose"), new Color(0.3f, 0.85f, 1f), 3f).Fill = true;
                ch.VLines.Add((1.0f, new Color(1f, 0.6f, 0.3f), T("lezyon", "lesion")));
                ch.VLines.Add((2.0f, new Color(0.9f, 0.9f, 0.95f), T("kemik", "bone")));
                ch.HLines.Add((90, new Color(0.4f, 1f, 0.5f), "90 %"));
                ch.HLines.Add((60, new Color(1f, 0.4f, 0.4f), "60 %"));
            };
            el.Plot = (v, ch) =>
            {
                var s = ch.AllSeries[0]; s.Pts.Clear();
                for (float d = 0; d <= 6.01f; d += 0.05f) s.Pts.Add(new Vector2(d, (float)dose(v, d)));
            };
            el.Live = v =>
            {
                double cv = cover(v), bone = dose(v, 2.0);
                double r90 = 0; for (double d = 0; d < 10; d += 0.02) { if (dose(v, d) >= 90) r90 = d; else if (d > 0.5) break; }
                return new List<Readout>
                {
                    new Readout(T("Cilt yüzeyi dozu", "Skin surface dose"), F(dose(v, 0), 0) + " %"),
                    new Readout(T("Lezyondaki en düşük doz (0–1 cm)", "Minimum dose in lesion (0–1 cm)"), F(cv, 0) + " %", Lvl(cv >= 90, cv >= 85)),
                    new Readout(T("R90 (hasta derinliği)", "R90 (patient depth)"), F(r90, 1) + " cm"),
                    new Readout(T("Kemik dozu (2 cm)", "Bone dose (2 cm)"), F(bone, 0) + " %", Lvl(bone <= 60, bone <= 80)),
                };
            };
            el.Evaluate = (v, run) =>
            {
                double cv = cover(v), bone = dose(v, 2.0);
                var r = new StepResult { Max = 25, Choice = mev[v.I("e")] + " MeV, bolus " + F(bol[v.I("b")], 1) + " cm" };
                int pC = cv >= 90 ? 15 : cv >= 85 ? 8 : 0;
                int pB = bone <= 60 ? 10 : bone <= 80 ? 5 : 0;
                r.Line("Lezyon kapsaması: en düşük %" + F(cv, 0) + (pC == 15 ? " — tam." : dose(v, 0) < 90 ? " — cilt yüzeyi az doz alıyor: bolus elektron dozunu yüzeye çeker." : " — derin kenar %90 altında: enerji yetersiz."),
                       "Lesion coverage: minimum " + F(cv, 0) + " %" + (pC == 15 ? " — complete." : dose(v, 0) < 90 ? " — the skin surface is underdosed: bolus pulls the electron dose up to the surface." : " — the deep edge is below 90 %: energy too low."));
                r.Line("Kemik (2 cm) %" + F(bone, 0) + (pB == 10 ? " — korunuyor." : " — gereksiz yüksek: osteoradyonekroz riski. Daha düşük enerji seç."),
                       "Bone (2 cm) " + F(bone, 0) + " %" + (pB == 10 ? " — spared." : " — unnecessarily high: risk of osteoradionecrosis. Choose a lower energy."));
                r.Line("Kural: R90 ≈ E/3.3 cm, R50 ≈ E/2.5 cm, Rp ≈ E/2 cm. Elektronlar keskin bir derinlikte durur — yüzeysel tümörler için ideal.", "Rule of thumb: R90 ≈ E/3.3 cm, R50 ≈ E/2.5 cm, Rp ≈ E/2 cm. Electrons stop at a sharp depth — ideal for superficial tumours.");
                r.Points = pC + pB;
                r.Quality = r.Points / 25f;
                r.G = StepResult.GradeOf(r.Points, r.Max);
                return r;
            };
            c.Steps.Add(el);

            c.Steps.Add(Q("Alan ve göz koruması", "Field and eye protection",
                    "Lezyon 2.5 cm. Elektron kesiği (cutout) boyutu ve göz koruması için ne seçersin?",
                    "The lesion is 2.5 cm. What cutout size and eye protection do you choose?")
                .Add(10, "Lezyon + ~1 cm kenar (≈ 4.5 cm kurşun kesik); göz alanın dışında, gerekirse iç göz kalkanı", "Lesion + ~1 cm margin (≈ 4.5 cm lead cutout); eye outside the field, internal eye shield if needed",
                     "Elektron alanında %90 izodoz derinde daralır; ~0.5–1 cm kenar gerekir. Küçük alanlarda çıktı faktörü ölçülmelidir (TG-25).",
                     "In electron fields the 90 % isodose narrows with depth; a ~0.5–1 cm margin is needed. Output factors must be measured for small fields (TG-25).")
                .Add(3, "10×10 cm aplikatör, kesik yok", "10×10 cm applicator, no cutout",
                     "Göze ve sağlam yüz derisine gereksiz doz.", "Needless dose to the eye and healthy facial skin.")
                .Add(2, "Tam lezyon boyutunda kesik (2.5 cm)", "Cutout exactly the lesion size (2.5 cm)",
                     "Kenarlarda düşük doz → nüks riski; ayrıca çok küçük alanda R90 sığlaşır.", "Underdosed edges → recurrence risk; R90 also gets shallower in very small fields."));

            var fx = Q("Fraksiyonasyon (yaşlı hasta)", "Fractionation (elderly patient)",
                    "Tümör α/β ≈ 10 Gy, geç cilt / kıkırdak etkileri α/β ≈ 3 Gy. 74 yaşında, her gün gelmekte zorlanan bir hasta için hangisi?",
                    "Tumour α/β ≈ 10 Gy, late skin / cartilage effects α/β ≈ 3 Gy. Which suits a 74-year-old who struggles to come daily?");
            (double d, int n, int pts)[] sch = { (2.5, 20, 10), (7, 5, 7), (2, 10, 2), (20, 1, 0) };
            foreach (var s in sch)
            {
                double t10 = Eqd2(s.d, s.n, 10), l3 = Eqd2(s.d, s.n, 3);
                string lbl = F(s.d * s.n, 0) + " Gy / " + s.n + " fx (" + F(s.d, 1) + " Gy)";
                string fbTr = "EQD2 tümör (α/β 10) = " + F(t10, 1) + " Gy, geç cilt (α/β 3) = " + F(l3, 1) + " Gy. " +
                              (s.pts == 10 ? "Kontrol ve kozmetik arasında iyi denge." : s.pts == 7 ? "Kabul edilebilir: daha az ziyaret, ama geç etki (atrofi, telenjiektazi) riski daha yüksek." : s.pts == 2 ? "Yetersiz doz: nüks riski yüksek." : "Tek büyük fraksiyon geç nekroz ve kıkırdak hasarı riski taşır.");
                string fbEn = "EQD2 tumour (α/β 10) = " + F(t10, 1) + " Gy, late skin (α/β 3) = " + F(l3, 1) + " Gy. " +
                              (s.pts == 10 ? "A good balance of control and cosmesis." : s.pts == 7 ? "Acceptable: fewer visits but higher late-effect risk (atrophy, telangiectasia)." : s.pts == 2 ? "Underdosed: high recurrence risk." : "One large fraction risks late necrosis and cartilage damage.");
                fx.Add(s.pts, lbl, lbl, fbTr, fbEn);
            }
            c.Steps.Add(fx);

            c.Learn.Add(("Elektron PDD: yüzey dozu enerjiyle artar (6 MeV ≈ %80, 15 MeV ≈ %95); bolus yüzeyi yükseltir ve menzili kısaltır.", "Electron PDD: surface dose rises with energy (6 MeV ≈ 80 %, 15 MeV ≈ 95 %); bolus raises the surface dose and shortens the range."));
            c.Learn.Add(("Hızlı kural (nominal enerji): R90 ≈ E/3.3, R80 ≈ E/2.9, R50 ≈ E/2.5, Rp ≈ E/2 (cm).", "Quick rules (nominal energy): R90 ≈ E/3.3, R80 ≈ E/2.9, R50 ≈ E/2.5, Rp ≈ E/2 (cm)."));
            c.Learn.Add(("Elektron alanlarında %90 izodoz derinde daralır: kesik lezyondan ~1 cm büyük olmalı.", "The electron 90 % isodose narrows at depth: the cutout should be ~1 cm larger than the lesion."));
            c.Refs.Add("AAPM TG-25 (1991) and TG-70 (2009) — clinical electron beams"); c.Refs.Add("ICRU Report 71 (2004)"); c.Refs.Add("Khan & Gibbons, The Physics of Radiation Therapy");
            return c;
        }

        // ============================================================================ 6. Zeynep — FDG PET, breastfeeding
        static CaseDef Zeynep()
        {
            var c = new CaseDef
            {
                Id = "zeynep", Modality = "nucmed", Difficulty = 2,
                P = new PatientInfo { Model = "Lady", Name = "Zeynep Koç", Age = 41, Female = true, HeightCm = 168, WeightKg = 74,
                    Skin = C(0.93f, 0.78f, 0.68f), Hair = C(0.45f, 0.20f, 0.10f), Top = C(0.15f, 0.55f, 0.55f), Bottom = C(0.35f, 0.35f, 0.38f), Shoe = C(0.90f, 0.90f, 0.92f) },
                TitleTr = "FDG PET/BT ve emziren anne", TitleEn = "FDG PET/CT in a breastfeeding mother",
                ComplaintTr = "Hodgkin lenfoma — evreleme", ComplaintEn = "Hodgkin lymphoma — staging",
                RequestTr = "Nükleer tıp: F-18 FDG PET/BT, evreleme (Dr. Er)", RequestEn = "Nuclear medicine: F-18 FDG PET/CT, staging (Dr Er)",
                IntroTr = "Bebeğimi emziriyorum. Bu tetkik ona zarar verir mi?", IntroEn = "I'm breastfeeding my baby. Could this scan harm her?",
            };
            c.History.Add(("7 aylık bebeğini emziriyor", "Breastfeeding a 7-month-old"));
            c.History.Add(("Kahvaltı 2 saat önce", "Breakfast 2 hours ago"));
            c.History.Add(("Diyabet yok", "No diabetes"));
            c.Vitals.Add(("Kan şekeri", "Blood glucose", "212 mg/dL"));
            c.Vitals.Add(("Kilo / boy", "Weight / height", "74 kg / 168 cm"));

            c.Steps.Add(new ChoiceStep
            {
                Tr = "Hasta hazırlığı", En = "Patient preparation",
                PromptTr = "Kahvaltı 2 saat önce, kan şekeri 212 mg/dL. Ne yaparsın?", PromptEn = "Breakfast 2 hours ago, blood glucose 212 mg/dL. What do you do?",
                AfterTr = "Ertesi sabah: 6 saat açlık, kan şekeri 94 mg/dL.", AfterEn = "Next morning: fasted 6 h, blood glucose 94 mg/dL.",
            }
                .Add(10, "Ertelerim: ≥ 4–6 saat açlık, glukoz < 200 mg/dL olmalı", "Reschedule: ≥ 4–6 h fasting, glucose must be < 200 mg/dL",
                     "EANM: glukoz < 11 mmol/L (≈ 200 mg/dL). Hiperglisemi FDG ile yarışır ve tümör tutulumunu düşürür.",
                     "EANM: glucose < 11 mmol/L (≈ 200 mg/dL). Hyperglycaemia competes with FDG and lowers tumour uptake.")
                .Add(3, "Kısa etkili insülin verip hemen enjekte ederim", "Give rapid-acting insulin and inject right away",
                     "İnsülin FDG'yi kas ve yağa yönlendirir; sonrasında ≥ 4 saat beklenmelidir.", "Insulin drives FDG into muscle and fat; wait ≥ 4 h afterwards.")
                .Add(0, "Hemen enjekte ederim", "Inject immediately",
                     "Düşük tümör/arka plan oranı → evreleme hatası riski.", "Low tumour-to-background ratio → risk of staging errors."));

            const double Tf = 109.8;  // F-18 half-life (min)
            const double w = 74;
            var act = new ParamStep
            {
                Tr = "Aktivite, süre ve bekleme", En = "Activity, time and uptake", Max = 25, RequireSim = false, ChartHeight = 200, Anim = "pet",
                PromptTr = "EANM (yatak örtüşmesi > %30): A·t ≥ 7 MBq·dk·kg⁻¹ × kilo. Az aktivite + uzun süre aynı görüntüyü daha düşük dozla verir. Tutulum süresi 60 ± 5 dk önerilir.",
                PromptEn = "EANM (bed overlap > 30 %): A·t ≥ 7 MBq·min·kg⁻¹ × weight. Less activity + longer time gives the same image at lower dose. Uptake time 60 ± 5 min is recommended.",
                SayTr = "Zeynep: \"Enjeksiyondan sonra sessizce dinlenmem mi gerekiyor?\"", SayEn = "Zeynep: \"Do I need to rest quietly after the injection?\"",
            };
            act.P(Param.Slider("a", "Enjekte edilen aktivite", "Injected activity", 100, 600, 400, 5, v => F(v, 0) + " MBq"))
               .P(Param.Slider("t", "Yatak başına süre", "Time per bed", 1, 4, 2, 0.5f, v => F(v, 1) + T(" dk", " min")))
               .P(Param.Slider("u", "Tutulum süresi", "Uptake time", 30, 120, 45, 5, v => F(v, 0) + T(" dk", " min")));
            act.ChartInit = ch =>
            {
                ch.XMin = 0; ch.XMax = 240; ch.YMin = 0; ch.YMax = 620; ch.AutoY = false; ch.XLabel = T("enjeksiyondan sonra (dk)", "after injection (min)"); ch.YLabel = "MBq";
                ch.Add("F-18 (T½ 109.8 dk)", new Color(0.4f, 1f, 0.6f), 3f).Fill = true;
            };
            act.Plot = (v, ch) =>
            {
                var s = ch.AllSeries[0]; s.Pts.Clear();
                for (float t = 0; t <= 240.1f; t += 4) s.Pts.Add(new Vector2(t, (float)CasePhysics.Decay(v["a"], Tf, t)));
                ch.VLines.Clear();
                ch.VLines.Add((v["u"], new Color(1f, 0.85f, 0.3f), T("tarama", "scan")));
            };
            act.Live = v =>
            {
                double need = 7 * w / v["t"], q = v["a"] * v["t"] / (7 * w), e = 0.019 * v["a"];
                return new List<Readout>
                {
                    new Readout(T("EANM önerisi", "EANM recommendation"), F(need, 0) + " MBq"),
                    new Readout(T("Sayım yeterliliği A·t / (7·kg)", "Count adequacy A·t / (7·kg)"), F(q, 2), Lvl(q >= 0.9 && q <= 1.5, q >= 0.75 && q <= 2)),
                    new Readout(T("Efektif doz (0.019 mSv/MBq)", "Effective dose (0.019 mSv/MBq)"), F(e, 1) + " mSv", Lvl(e <= 6, e <= 8)),
                    new Readout(T("Tarama başında aktivite", "Activity at scan start"), F(CasePhysics.Decay(v["a"], Tf, v["u"]), 0) + " MBq"),
                    new Readout(T("Toplam tarama (7 yatak)", "Total scan (7 beds)"), F(7 * v["t"], 0) + T(" dk", " min")),
                };
            };
            act.Evaluate = (v, run) =>
            {
                double q = v["a"] * v["t"] / (7 * w), e = 0.019 * v["a"], u = v["u"];
                var r = new StepResult { Max = 25, Choice = F(v["a"], 0) + " MBq, " + F(v["t"], 1) + T(" dk/yatak, tutulum ", " min/bed, uptake ") + F(u, 0) + T(" dk", " min") };
                int pQ = q >= 0.9 && q <= 1.5 ? 10 : (q >= 0.75 && q <= 2) ? 5 : 0;
                int pE = pQ == 0 ? 0 : e <= 6 ? 5 : e <= 8 ? 3 : 0;
                int pU = u >= 55 && u <= 75 ? 10 : u >= 45 && u <= 90 ? 5 : 0;
                r.Line("Sayım yeterliliği " + F(q, 2) + (pQ == 10 ? " — görüntü kalitesi EANM düzeyinde." : q < 0.9 ? " — az sayım: gürültülü görüntü, küçük lenf nodları kaçabilir." : " — gereğinden fazla aktivite; doz artışı tanıya katkı sağlamıyor."),
                       "Count adequacy " + F(q, 2) + (pQ == 10 ? " — EANM-level image quality." : q < 0.9 ? " — too few counts: noisy image, small nodes may be missed." : " — more activity than needed; extra dose adds no diagnostic value."));
                r.Line("Efektif doz ≈ 0.019 × " + F(v["a"], 0) + " = " + F(e, 1) + " mSv (ICRP 128).", "Effective dose ≈ 0.019 × " + F(v["a"], 0) + " = " + F(e, 1) + " mSv (ICRP 128).");
                r.Line(pU == 10 ? "Tutulum süresi uygun: tümör/arka plan oranı standart (SUV karşılaştırılabilir)." : "Tutulum süresi standart dışı: SUV değerleri önceki/sonraki taramalarla karşılaştırılamaz.",
                       pU == 10 ? "Uptake time appropriate: standard tumour/background ratio (comparable SUVs)." : "Non-standard uptake time: SUVs are not comparable with previous/later scans.");
                r.Points = pQ + pE + pU;
                r.DoseMSv = e;
                r.Quality = pQ / 10f;
                run.Memo["A"] = v["a"]; run.Memo["U"] = u; run.Memo["T"] = v["t"];
                r.G = StepResult.GradeOf(r.Points, r.Max);
                return r;
            };
            c.Steps.Add(act);

            // infant dose from close contact: point source, patient-attenuated dose-rate constant, 20 % voided
            const double gamma = 0.092;   // µSv·m²/(MBq·h) at 1 m from an FDG patient (body attenuation included, approx.)
            Func<CaseRun, double> aEnd = run => 0.8 * CasePhysics.Decay(run.M("A", 300), Tf, run.M("U", 60) + 7 * run.M("T", 2) + 10);
            Func<CaseRun, double, double> infant = (run, waitH) =>
            {
                double tau = Tf / 60.0 / Math.Log(2);   // mean life (h)
                return 0.5 * gamma * aEnd(run) * Math.Exp(-waitH / tau) * tau / (0.25 * 0.25);
            };
            CaseRun runRef = null;
            var bf = new ParamStep
            {
                Tr = "Emzirme ve yakın temas", En = "Breastfeeding and close contact", Max = 15, RequireSim = false, ChartHeight = 190,
                PromptTr = "Bebeğin dozu esas olarak annenin vücudundan gelen 511 keV fotonlarla (dış ışınlama) olur; süte geçen FDG çok azdır. Yakın temasa (kucak, emzirme ~25 cm) ne zaman dönülsün? Oyun hedefi: bebek dozu ≤ 100 µSv.",
                PromptEn = "The baby's dose comes mainly from 511 keV photons from the mother's body (external exposure); very little FDG enters the milk. When may close contact (holding, breastfeeding ~25 cm) resume? Game target: infant dose ≤ 100 µSv.",
                SayFn = r => { runRef = r; return T("Zeynep: \"Sütümü önceden sağdım. Eşim bebeğe biberonla verebilir.\"", "Zeynep: \"I pumped milk beforehand. My husband can bottle-feed her.\""); },
            };
            bf.P(Param.Slider("wait", "Yakın temas öncesi bekleme", "Wait before close contact", 0, 12, 0, 0.5f, v => F(v, 1) + T(" sa", " h")));
            bf.ChartInit = ch =>
            {
                ch.XMin = 0; ch.XMax = 12; ch.YMin = 0; ch.AutoY = true; ch.XLabel = T("bekleme (saat)", "wait (h)"); ch.YLabel = "µSv";
                ch.Add(T("Bebek dozu", "Infant dose"), new Color(1f, 0.7f, 0.3f), 3f);
                ch.HLines.Add((100, new Color(0.4f, 1f, 0.5f), "100 µSv"));
            };
            bf.Plot = (v, ch) =>
            {
                var s = ch.AllSeries[0]; s.Pts.Clear();
                var run = runRef;
                if (run == null) return;
                for (float h = 0; h <= 12.01f; h += 0.25f) s.Pts.Add(new Vector2(h, (float)infant(run, h)));
                ch.VLines.Clear(); ch.VLines.Add((v["wait"], new Color(0.4f, 0.85f, 1f), ""));
            };
            bf.Live = v =>
            {
                var run = runRef;
                if (run == null) return new List<Readout>();
                double dI = infant(run, v["wait"]);
                return new List<Readout>
                {
                    new Readout(T("Tarama sonunda vücuttaki aktivite", "Activity in body after scan"), F(aEnd(run), 0) + " MBq"),
                    new Readout(T("1 m'de doz hızı", "Dose rate at 1 m"), F(gamma * aEnd(run), 1) + " µSv/h"),
                    new Readout(T("25 cm'de doz hızı", "Dose rate at 25 cm"), F(gamma * aEnd(run) / 0.0625, 0) + " µSv/h"),
                    new Readout(T("Bebek dozu (%50 yakın temas)", "Infant dose (50 % close contact)"), F(dI, 0) + " µSv", Lvl(dI <= 100, dI <= 300)),
                };
            };
            bf.Evaluate = (v, run) =>
            {
                double dI = infant(run, v["wait"]);
                var r = new StepResult { Max = 15, Choice = F(v["wait"], 1) + T(" saat bekleme", " h wait") };
                r.Points = (dI <= 100 ? 15 : dI <= 300 ? 7 : 0) - (v["wait"] > 8 ? 5 : 0);
                r.Points = Math.Max(0, r.Points);
                r.Line("Bebek dozu ≈ " + F(dI, 0) + " µSv (ters kare: 25 cm'de doz hızı 1 m'dekinin 16 katı; F-18 ortalama ömrü τ = T½/ln2 ≈ 2.6 sa).",
                       "Infant dose ≈ " + F(dI, 0) + " µSv (inverse square: the dose rate at 25 cm is 16× that at 1 m; F-18 mean life τ = T½/ln2 ≈ 2.6 h).");
                r.Line("Kılavuzlar (ICRP 128, IAEA) FDG için emzirmeye ara vermeyi zorunlu görmez; birkaç saat yakın temasın sınırlanması ve sağılmış sütün kullanılması önerilir.",
                       "Guidelines (ICRP 128, IAEA) do not require interrupting breastfeeding for FDG; limiting close contact for a few hours and using expressed milk is advised.");
                if (v["wait"] > 8) r.Line("12 saate varan bekleme gereksizdir: F-18 neredeyse tamamen bozunmuştur ve emzirme ilişkisi gereksiz bozulur.", "Waiting much longer is unnecessary: F-18 has almost fully decayed, and breastfeeding is disrupted needlessly.");
                r.G = StepResult.GradeOf(r.Points, r.Max);
                return r;
            };
            c.Steps.Add(bf);

            c.Steps.Add(Q("PET fiziği", "PET physics",
                    "FDG'den çıkan pozitron yok olduğunda ne oluşur ve PET bunu nasıl algılar?",
                    "What is produced when the positron from FDG annihilates, and how does PET detect it?")
                .Add(10, "İki adet 511 keV foton ~180° zıt yönde; iki dedektörde birkaç ns'lik koinsidans penceresinde algılanır → yanıt hattı (LOR)", "Two 511 keV photons ~180° apart, detected by two detectors within a few-ns coincidence window → line of response (LOR)",
                     "Doğru: e⁺ + e⁻ → 2γ (2 × m₀c² = 2 × 511 keV). Rastgele ve saçılmış koinsidanslar düzeltilir, zayıflama BT ile düzeltilir.",
                     "Correct: e⁺ + e⁻ → 2γ (2 × m₀c² = 2 × 511 keV). Random and scattered coincidences are corrected; attenuation is corrected with CT.")
                .Add(3, "Tek bir 1022 keV foton", "A single 1022 keV photon",
                     "Momentum korunumu iki foton gerektirir.", "Momentum conservation requires two photons.")
                .Add(0, "Dedektörler pozitronun kendisini ölçer", "The detectors measure the positron itself",
                     "Pozitron dokuda ~1 mm içinde yok olur; dışarı yalnızca 511 keV fotonlar çıkar.", "The positron annihilates within ~1 mm of tissue; only 511 keV photons escape."));

            c.Learn.Add(("FDG için glukoz < 200 mg/dL ve ≥ 4–6 saat açlık; tutulum süresi 60 ± 5 dk.", "For FDG: glucose < 200 mg/dL and ≥ 4–6 h fasting; uptake time 60 ± 5 min."));
            c.Learn.Add(("EANM: A·t ~ kilo. Uzun tarama süresi aktiviteyi ve dozu düşürmeye izin verir.", "EANM: A·t ~ weight. A longer scan allows lower activity and dose."));
            c.Learn.Add(("Hastadan doz hızı ~ A/d²; F-18 T½ = 109.8 dk. Yakın temas birkaç saat ertelenirse bebek dozu hızla düşer.", "Dose rate from the patient ~ A/d²; F-18 T½ = 109.8 min. Delaying close contact by a few hours cuts the infant dose sharply."));
            c.Refs.Add("Boellaard et al., EJNMMI 42:328 (2015) — EANM FDG PET/CT guideline v2.0"); c.Refs.Add("ICRP Publication 128 (2015)");
            c.Refs.Add("IAEA SSG-46 (2018) — Radiation Protection and Safety in Medical Uses of Ionizing Radiation");
            return c;
        }

        // ============================================================================ 7. Can — MRI knee, safety screening
        static CaseDef Can()
        {
            var c = new CaseDef
            {
                Id = "can", Modality = "mri", Difficulty = 2,
                P = new PatientInfo { Model = "Man", Name = "Can Öztürk", Age = 35, HeightCm = 182, WeightKg = 84,
                    Skin = C(0.78f, 0.58f, 0.44f), Hair = C(0.08f, 0.06f, 0.05f), Top = C(0.20f, 0.42f, 0.26f), Bottom = C(0.20f, 0.30f, 0.52f), Shoe = C(0.45f, 0.30f, 0.18f) },
                TitleTr = "Diz MR ve MR güvenliği", TitleEn = "Knee MRI and MR safety",
                ComplaintTr = "Futbolda sağ diz burkulması; kilitlenme, şişlik", ComplaintEn = "Right knee twisted playing football; locking, swelling",
                RequestTr = "Ortopedi: sağ diz MR — menisküs yırtığı? kemik kontüzyonu? (Dr. Polat)", RequestEn = "Orthopaedics: right knee MRI — meniscal tear? bone bruise? (Dr Polat)",
                IntroTr = "Çabuk biter mi? Yarın işe dönmem lazım, atölyede taşlama yapıyorum.", IntroEn = "Will it be quick? I need to be back at work tomorrow — I do grinding in the workshop.",
            };
            c.History.Add(("Meslek: kaynakçı / taşlama (12 yıl)", "Occupation: welder / grinding (12 years)"));
            c.History.Add(("Sol kolda dövme (2019)", "Tattoo on the left arm (2019)"));
            c.History.Add(("Dental dolgular", "Dental fillings"));
            c.History.Add(("2016 omuz artroskopisi: titanyum ankorlar", "2016 shoulder arthroscopy: titanium anchors"));
            c.Vitals.Add(("Kilo", "Weight", "84 kg"));
            c.Vitals.Add(("Klostrofobi", "Claustrophobia", T("yok", "none")));
            c.Vitals.Add(("Kalp pili", "Pacemaker", T("yok", "none")));

            c.Steps.Add(new ChoiceStep
            {
                Tr = "MR güvenlik taraması", En = "MR safety screening", Multi = true, Max = 13,
                PromptTr = "Tarama formundaki maddelerden hangileri MR öncesi EK İŞLEM gerektirir? (birden fazla seçilebilir)",
                PromptEn = "Which items on the screening form need ADDITIONAL ACTION before MRI? (select all that apply)",
                AfterTr = "Orbita grafisi: metal yabancı cisim YOK. Taramaya devam ediliyor.", AfterEn = "Orbit radiographs: NO metallic foreign body. The scan proceeds.",
            }
                .Add(10, "Kaynakçı / taşlama: göz içi metal parça şüphesi", "Welder / grinding: possible intra-ocular metal",
                     "Ferromanyetik göz içi parça B₀ içinde hareket edip körlüğe yol açabilir → önce orbita grafisi (veya BT).",
                     "A ferromagnetic intra-ocular fragment can move in B₀ and cause blindness → orbit radiographs (or CT) first.")
                .Add(3, "Dövme", "Tattoo",
                     "Demir oksitli pigmentler ısınabilir: hasta bilgilendirilir, gerekirse soğuk kompres. Kontrendikasyon değildir.",
                     "Iron-oxide pigments may heat: inform the patient, cold compress if needed. Not a contraindication.")
                .Add(-4, "Dental dolgular", "Dental fillings",
                     "Amalgam ve kompozit dolgular güvenlidir; yalnız artefakt yapabilir.", "Amalgam and composite fillings are safe; they may only cause artefacts.")
                .Add(-3, "Titanyum ankorlar", "Titanium anchors",
                     "Titanyum ferromanyetik değildir (MR güvenli / koşullu); ek işlem gerekmez.", "Titanium is not ferromagnetic (MR safe / conditional); no action needed."));

            var mr = new ParamStep
            {
                Tr = "Sekans tasarımı", En = "Sequence design", Max = 25, Anim = "mri",
                PromptTr = "Hedef: menisküs yırtığı ile kemik iliği ödemini görmek. Spin eko: S = PD·(1 − e^(−TR/T1))·e^(−TE/T2). TR, TE, yağ baskılama ve B₀'ı seç, sonra SEKANSI ÇALIŞTIR.",
                PromptEn = "Goal: show the meniscal tear and the bone-marrow oedema. Spin echo: S = PD·(1 − e^(−TR/T1))·e^(−TE/T2). Choose TR, TE, fat saturation and B₀, then RUN SEQUENCE.",
                SayTr = "Can: \"Kulaklık verdiniz, iyi oldu. Sesler başladı!\"", SayEn = "Can: \"Good thing you gave me headphones. Here come the noises!\"",
                MakeSim = () => new KneeMriSim(),
            };
            mr.P(Param.Slider("tr", "TR (tekrarlama süresi)", "TR (repetition time)", 300, 5000, 500, 50, v => F(v, 0) + " ms"))
              .P(Param.Slider("te", "TE (eko süresi)", "TE (echo time)", 8, 120, 15, 1, v => F(v, 0) + " ms"))
              .P(Param.Check("fs", "Yağ baskılama (FS)", "Fat saturation (FS)", false))
              .P(Param.Seg("b0", "Alan gücü", "Field strength", 0, new[] { "1.5 T", "3 T" }, null));
            mr.ChartInit = ch =>
            {
                ch.XMin = -0.5f; ch.XMax = KneeMriSim.Tis.Length - 0.5f; ch.YMin = 0; ch.YMax = 1; ch.AutoY = false; ch.YLabel = T("sinyal", "signal");
                ch.XFmt = x => { int i = Mathf.RoundToInt(x); return i >= 1 && i < KneeMriSim.Tis.Length ? T(KneeMriSim.Tis[i].tr, KneeMriSim.Tis[i].en).Split(' ')[0] : ""; };
                ch.Add(T("Doku sinyali", "Tissue signal"), new Color(0.5f, 0.8f, 1f), 10f).Bars = true;
            };
            mr.Plot = (v, ch) =>
            {
                var s = ch.AllSeries[0]; s.Pts.Clear();
                double b0 = v.I("b0") == 1 ? 3 : 1.5;
                for (int i = 1; i < KneeMriSim.Tis.Length; i++) s.Pts.Add(new Vector2(i, (float)KneeMriSim.Signal(i, v["tr"], v["te"], v.B("fs"), b0)));
            };
            mr.Live = v =>
            {
                double tr = v["tr"], te = v["te"];
                string wt = tr < 900 && te < 30 ? T("T1 ağırlıklı", "T1-weighted") : tr >= 1800 && te < 50 ? T("PD ağırlıklı", "PD-weighted") : tr >= 1800 && te >= 50 ? T("T2 ağırlıklı", "T2-weighted") : T("karışık", "mixed");
                var l = new List<Readout>
                {
                    new Readout(T("Ağırlık", "Weighting"), wt + (v.B("fs") ? " + FS" : "")),
                    new Readout(T("Çekim süresi (256 faz, ETL 8)", "Scan time (256 phase, ETL 8)"), F(tr * 256 / (tr >= 1200 ? 8 : 1) / 1000, 0) + " s"),
                    new Readout("Larmor", F(42.58 * (v.I("b0") == 1 ? 3 : 1.5), 1) + " MHz"),
                };
                if (v.I("b0") == 1) l.Add(new Readout("SAR", T("≈ 4× (~ B₀²)", "≈ 4× (~ B₀²)"), 1));
                if (v.HasSim)
                {
                    l.Add(new Readout(T("CNR yırtık / menisküs", "CNR tear / meniscus"), F(v.S("cnrTear"), 1), Lvl(v.S("cnrTear") >= 15, v.S("cnrTear") >= 8)));
                    l.Add(new Readout(T("CNR ödem / ilik", "CNR oedema / marrow"), F(v.S("cnrEdema"), 1), Lvl(v.S("cnrEdema") >= 10, v.S("cnrEdema") >= 5)));
                }
                else l.Add(new Readout(T("Görüntü", "Image"), T("henüz çalıştırılmadı", "not run yet")));
                return l;
            };
            mr.Evaluate = (v, run) =>
            {
                var r = new StepResult { Max = 25, Choice = "TR " + F(v["tr"], 0) + " / TE " + F(v["te"], 0) + " ms" + (v.B("fs") ? " FS" : "") + ", " + (v.I("b0") == 1 ? "3 T" : "1.5 T") };
                double ct = v.S("cnrTear"), ce = v.S("cnrEdema"), ta = v.S("ta");
                int pT = ct >= 15 ? 10 : ct >= 8 ? 5 : 0, pE = ce >= 10 ? 10 : ce >= 5 ? 5 : 0, pA = ta <= 300 ? 5 : 0;
                r.Line("Yırtık/menisküs CNR " + F(ct, 1) + (pT == 10 ? ": yırtık belirgin (PD/T2'de sıvı parlak, menisküs karanlık)." : ": yırtık seçilemiyor — uzun TR (PD/T2) gerekir."),
                       "Tear/meniscus CNR " + F(ct, 1) + (pT == 10 ? ": tear conspicuous (fluid bright, meniscus dark on PD/T2)." : ": tear not visible — a long TR (PD/T2) is needed."));
                r.Line("Ödem/ilik CNR " + F(ce, 1) + (pE == 10 ? ": kemik kontüzyonu görülüyor." : v.B("fs") ? ": kontrast zayıf." : ": yağlı ilik ödemi maskeliyor — yağ baskılama (FS) kullan."),
                       "Oedema/marrow CNR " + F(ce, 1) + (pE == 10 ? ": bone bruise visible." : v.B("fs") ? ": weak contrast." : ": fatty marrow masks the oedema — use fat saturation (FS)."));
                r.Line("Süre " + F(ta, 0) + " s" + (pA > 0 ? " — hareketsiz kalabilir." : " — çok uzun, hareket artefaktı riski; TR'yi kısalt veya turbo faktörü artır."),
                       "Time " + F(ta, 0) + " s" + (pA > 0 ? " — the patient can keep still." : " — too long, motion artefacts likely; shorten TR or raise the turbo factor."));
                r.Points = pT + pE + pA;
                r.Quality = r.Points / 25f;
                r.G = StepResult.GradeOf(r.Points, r.Max);
                return r;
            };
            c.Steps.Add(mr);

            c.Steps.Add(Q("Akustik gürültü", "Acoustic noise",
                    "Tarama sırasında yüksek 'tak-tak' sesleri duyuluyor. Nedeni ve önlem?",
                    "Loud knocking sounds are heard during the scan. Cause and precaution?")
                .Add(10, "Gradyan bobinlerindeki hızlı akım değişimleri B₀ içinde Lorentz kuvveti yaratır; ses > 100 dB olabilir → kulak tıkacı / kulaklık zorunlu", "Rapidly switched gradient currents feel Lorentz forces in B₀; noise can exceed 100 dB → earplugs / headphones are mandatory",
                     "Doğru: F = I·L × B. Gürültü B₀ ile artar (3 T'de daha yüksek); IEC 60601-2-33 kulak koruması ister.",
                     "Correct: F = I·L × B. Noise grows with B₀ (louder at 3 T); IEC 60601-2-33 requires hearing protection.")
                .Add(3, "RF pulsları ses çıkarır; önlem gerekmez", "RF pulses make the noise; no precaution needed",
                     "RF duyulmaz; ses gradyanlardan gelir ve işitme hasarı yapabilir.", "RF is inaudible; the noise comes from the gradients and can damage hearing.")
                .Add(0, "Soğutma sıvısından gelir; tehlikesiz", "It comes from the cryogen; harmless",
                     "Helyum pompası hafif ses yapar ama vuruntular gradyanlardandır.", "The helium pump hums softly, but the knocking comes from the gradients."));

            c.Steps.Add(Q("Zon IV kuralı", "Zone IV rule",
                    "Bir hemşire acil durumda çelik oksijen tüpüyle tarama odasına (Zon IV) girmek istiyor. Ne yaparsın?",
                    "A nurse wants to bring a steel oxygen cylinder into the scanner room (Zone IV) in an emergency. What do you do?")
                .Add(10, "Durdururum: süperiletken mıknatıs hep açıktır, ferromanyetik cisimler füze gibi çekilir; yalnız MR-güvenli ekipman", "Stop them: the superconducting magnet is always on and ferromagnetic objects become projectiles; MR-safe equipment only",
                     "Doğru: çekim kuvveti ~ B·dB/dz; giriş yakınında en büyüktür. Hasta gerekirse Zon III'e çıkarılıp orada müdahale edilir.",
                     "Correct: the attractive force ~ B·dB/dz and peaks near the bore entrance. If needed, move the patient out to Zone III for care.")
                .Add(2, "Mıknatısı kapatırız, sonra girsin", "Switch the magnet off, then let them in",
                     "Süperiletken mıknatıs kapatılamaz; 'quench' helyumu boşaltır, pahalı ve tehlikelidir — yalnız hayati tehlikede.",
                     "A superconducting magnet cannot simply be switched off; a quench vents the helium, is costly and hazardous — life-threatening situations only.")
                .Add(0, "Hızlı girip çıkarsa sorun olmaz", "Fine if they are quick",
                     "Çekim anlıktır; ciddi yaralanma ve ölümler bildirilmiştir.", "The pull is instantaneous; serious injuries and deaths have been reported."));

            c.Learn.Add(("Kısa TR/TE → T1; uzun TR, kısa TE → PD; uzun TR/TE → T2 ağırlık.", "Short TR/TE → T1; long TR, short TE → PD; long TR/TE → T2 weighting."));
            c.Learn.Add(("Kemik iliği ödemi yağlı ilikte ancak yağ baskılama (veya STIR) ile görünür.", "Bone-marrow oedema in fatty marrow becomes visible only with fat suppression (or STIR)."));
            c.Learn.Add(("Larmor frekansı f = γ̄·B₀ (42.58 MHz/T). SAR ~ B₀²: 3 T'de RF ısınması ~4 kat.", "Larmor frequency f = γ̄·B₀ (42.58 MHz/T). SAR ~ B₀²: ~4× RF heating at 3 T."));
            c.Learn.Add(("MR güvenliği: tarama formu, orbita grafisi (metal işçileri), Zon IV kuralları, kulak koruması.", "MR safety: screening form, orbit radiographs (metal workers), Zone IV rules, hearing protection."));
            c.Refs.Add("ACR Manual on MR Safety (2020)"); c.Refs.Add("Kanal et al., JMRI 37:501 (2013)"); c.Refs.Add("IEC 60601-2-33"); c.Refs.Add("Westbrook, MRI in Practice (5th ed.)");
            return c;
        }
    }
}
