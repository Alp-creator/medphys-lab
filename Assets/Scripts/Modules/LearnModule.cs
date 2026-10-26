using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using MedPhys.Core;
using MedPhys.Learn;
using MedPhys.Physics;
using MedPhys.UI;
using static MedPhys.Core.Loc;

namespace MedPhys.Modules
{
    /// <summary>
    /// Learning centre: the full curriculum (topics → lessons) with a reader, interactive figures, lesson quizzes,
    /// a mixed practice exam, formula sheet / glossary and links to the matching lab and clinical case.
    /// </summary>
    public sealed class LearnModule : ModuleBase
    {
        public override string Id => "learn";
        public override string Title => T("Öğrenme Merkezi", "Learning Centre");
        public override string Subtitle => T("Konu anlatımı • etkileşimli şekiller • testler", "Lessons • interactive figures • quizzes");
        protected override string LeftPanelLabel => T("Konular", "Topics");
        protected override string RightPanelLabel => T("Formüller", "Formulas");
        protected override string HelpLabel => T("Nasıl çalışılır", "How to study");
        protected override string LearnTopic => null;

        static string pending;            // "topic:<id>", "lesson:<topic.lesson>", "exam"
        static string lastView;
        static string lastLang;

        public static void OpenTopic(string topicId) { pending = "topic:" + topicId; App.I.Open("learn"); }
        public static void OpenLesson(string key) { pending = "lesson:" + key; App.I.Open("learn"); }

        VisualElement reader;
        ScrollView readerScroll;
        VisualElement navBox;
        Transform holo;
        string view;
        Lesson reading;                   // the lesson on screen; marked as read once scrolled to its end

        protected override void Build()
        {
            Env.Room(Root.transform, new Color(0.55f, 0.6f, 1f));
            var ph = Phantom.BuildAdult(new PhantomOptions { Tumor = true, Pacemaker = false });
            var pv = PhantomView.Create(ph, Root.transform, "LearnHolo");
            pv.transform.localPosition = new Vector3(1.9f, 0.6f, 0.4f);
            holo = pv.transform;
            var disc = MeshObj("HoloBase", MeshGen.Ring(0.55f, 0.04f), Res.Additive(new Color(0.45f, 0.6f, 1f, 0.8f), 2f));
            disc.transform.localRotation = Quaternion.Euler(90, 0, 0);
            disc.transform.localPosition = new Vector3(1.9f, 0.02f, 0.4f);
            App.Rig.Frame(new Vector3(0.6f, 1.15f, 0.2f), 4.6f, 196f, 8f, true);
            App.Rig.AutoRotate = 0;

            // the reader sits under the side panels so they can slide over it
            reader = new VisualElement(); reader.AddToClassList("mp-reader");
            if (App.SmallScreen) reader.style.left = 350;
            readerScroll = new ScrollView(ScrollViewMode.Vertical); readerScroll.AddToClassList("mp-scroll");
            reader.Add(readerScroll);
            readerScroll.schedule.Execute(CheckRead).Every(400);
            App.UIRoot.Insert(App.UIRoot.IndexOf(Left), reader);

            navBox = new VisualElement();
            LeftScroll.contentContainer.Add(navBox);
            BuildNav();

            string target = pending;
            pending = null;
            if (target == null && lastLang != Loc.Code && lastView != null) target = lastView;   // language switch: stay where we were
            lastLang = Loc.Code;
            Show(target ?? "home");
            SetTip("Soldan bir konu ve ders seç. Şekillerdeki kaydırıcılarla oyna, ders sonundaki testle kendini dene.",
                   "Pick a topic and lesson on the left. Play with the figure sliders and test yourself at the end of each lesson.");
        }

        public override void Tick(float dt)
        {
            base.Tick(dt);
            if (holo != null) holo.Rotate(0, 18f * dt, 0, Space.World);
        }

        void Show(string v)
        {
            view = v; lastView = v; reading = null;
            if (v.StartsWith("lesson:")) { var l = Curriculum.FindLesson(v.Substring(7)); if (l != null) { ShowLesson(l); return; } v = "home"; }
            if (v.StartsWith("topic:")) { var t = Curriculum.FindTopic(v.Substring(6)); if (t != null) { ShowTopic(t); return; } v = "home"; }
            if (v == "exam") { StartExam(null); return; }
            if (v.StartsWith("exam:")) { StartExam(Curriculum.FindTopic(v.Substring(5))); return; }
            ShowHome();
        }

        VisualElement Page()
        {
            var c = readerScroll.contentContainer;
            c.Clear();
            readerScroll.scrollOffset = Vector2.zero;
            return c;
        }

        // ================================================================== navigation (left panel)
        void BuildNav()
        {
            navBox.Clear();
            var sec = UIKit.Section(navBox, T("İlerleme", "Progress"));
            float all = LearnProgress.Overall();
            UIKit.KV(sec, T("Tamamlanan", "Completed"), F(all * 100, 0) + " %");
            UIKit.Progress(sec).Set(all);
            var row = UIKit.ButtonRow(sec);
            UIKit.Button(row, T("Genel bakış", "Overview"), () => Show("home"));
            UIKit.Button(row, T("Deneme sınavı", "Practice exam"), () => Show("exam"), "mp-btn-primary");
            foreach (var t in Curriculum.Topics)
            {
                var topic = t;
                var s = UIKit.Section(navBox, "");
                var hb = new Button(() => Show("topic:" + topic.Id)); hb.AddToClassList("mp-btn"); hb.AddToClassList("mp-navtopic");
                var hr = new VisualElement(); hr.style.flexDirection = FlexDirection.Row; hr.style.alignItems = Align.Center;
                var tag = new Label(topic.Tag); tag.AddToClassList("mp-navtag"); hr.Add(tag);
                var tt = new Label(topic.Title); tt.AddToClassList("mp-navtitle"); tt.style.flexGrow = 1; tt.style.flexShrink = 1; hr.Add(tt);
                var pc = new Label(F(LearnProgress.Of(topic) * 100, 0) + "%"); pc.AddToClassList("mp-navpct"); hr.Add(pc);
                hb.Add(hr);
                s.Add(hb);
                bool open = view != null && (view == "topic:" + topic.Id || view.StartsWith("lesson:" + topic.Id + "."));
                if (!open) continue;
                foreach (var l in topic.Lessons)
                {
                    var lesson = l;
                    string mark = LearnProgress.Mastered(l) ? "★" : LearnProgress.Read(l) ? "✓" : "○";
                    var lb = UIKit.Button(s, mark + "  " + l.Title, () => Show("lesson:" + lesson.Key), "mp-navlesson");
                    if (view == "lesson:" + l.Key) lb.AddToClassList("mp-navlesson-on");
                }
            }
        }

        void RefreshNav() => BuildNav();

        // ================================================================== home
        void ShowHome()
        {
            var c = Page();
            var h = new Label(T("Öğrenme Merkezi", "Learning Centre")); h.AddToClassList("mp-read-title"); c.Add(h);
            UIKit.Text(c, T("Medikal fiziğin temelinden kliniğe 8 konu. Her ders: açıklama, formüller, çözümlü örnekler, gerçek fizik modelleriyle çalışan etkileşimli şekiller ve kısa bir test. Dersin sonunda ilgili laboratuvara ve klinik vakaya geçebilirsin.",
                            "Eight topics from the foundations of medical physics to the clinic. Every lesson has explanations, formulas, worked examples, interactive figures driven by real physics models and a short quiz. At the end you can jump to the matching lab and clinical case."), "mp-read-lead");
            // continue where you left off
            Lesson next = null;
            foreach (var t in Curriculum.Topics) { foreach (var l in t.Lessons) if (!LearnProgress.Read(l)) { next = l; break; } if (next != null) break; }
            if (next != null)
            {
                var cont = new Button(() => Show("lesson:" + next.Key)); cont.AddToClassList("mp-btn"); cont.AddToClassList("mp-continue");
                var k = new Label(T("SIRADAKİ DERS", "NEXT LESSON")); k.AddToClassList("mp-hub-kicker"); cont.Add(k);
                var nt = new Label(next.Topic.Title + "  ›  " + next.Title); nt.AddToClassList("mp-continue-title"); cont.Add(nt);
                c.Add(cont);
            }
            var grid = new VisualElement(); grid.AddToClassList("mp-topicgrid"); c.Add(grid);
            int i = 0;
            foreach (var t in Curriculum.Topics)
            {
                var topic = t; i++;
                var b = new Button(() => Show("topic:" + topic.Id)); b.AddToClassList("mp-btn"); b.AddToClassList("mp-topiccard");
                var top = new VisualElement(); top.style.flexDirection = FlexDirection.Row; top.style.alignItems = Align.Center;
                var n = new Label(i.ToString()); n.AddToClassList("mp-topicno"); top.Add(n);
                var tag = new Label(topic.Tag); tag.AddToClassList("mp-navtag"); top.Add(tag);
                b.Add(top);
                var tt = new Label(topic.Title); tt.AddToClassList("mp-topictitle"); b.Add(tt);
                var dd = new Label(topic.Desc); dd.AddToClassList("mp-topicdesc"); b.Add(dd);
                var meta = new Label(topic.Lessons.Count + T(" ders • ", " lessons • ") + F(LearnProgress.Of(topic) * 100, 0) + T(" % tamam", " % done")); meta.AddToClassList("mp-topicmeta"); b.Add(meta);
                var bar = UIKit.Progress(b); bar.Set(LearnProgress.Of(topic));
                grid.Add(b);
            }
            var ex = UIKit.Section(c, T("Deneme sınavı", "Practice exam"));
            UIKit.Text(ex, T("Tüm konulardan rastgele 15 soru. Yüksek lisans mülakatına ve sınavlara hazırlık için idealdir.", "15 random questions from all topics — good preparation for master's interviews and exams."));
            if (LearnProgress.ExamBest >= 0) UIKit.KV(ex, T("En iyi sonucun", "Your best"), LearnProgress.ExamBest + " / 15");
            UIKit.Button(UIKit.ButtonRow(ex), T("Sınavı başlat  →", "Start the exam  →"), () => Show("exam"), "mp-btn-primary");
            RefreshNav();
        }

        // ================================================================== topic
        void ShowTopic(Topic t)
        {
            var c = Page();
            var crumb = new Label(T("Öğrenme Merkezi", "Learning Centre") + "  ›  " + t.Title); crumb.AddToClassList("mp-crumb"); c.Add(crumb);
            var h = new Label(t.Title); h.AddToClassList("mp-read-title"); c.Add(h);
            UIKit.Text(c, t.Desc, "mp-read-lead");
            foreach (var l in t.Lessons)
            {
                var lesson = l;
                var b = new Button(() => Show("lesson:" + lesson.Key)); b.AddToClassList("mp-btn"); b.AddToClassList("mp-lessoncard");
                var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row; row.style.alignItems = Align.Center;
                string mark = LearnProgress.Mastered(l) ? "★" : LearnProgress.Read(l) ? "✓" : "○";
                var m = new Label(mark); m.AddToClassList("mp-lessonmark"); if (LearnProgress.Mastered(l)) m.style.color = new Color(1f, 0.72f, 0.28f); row.Add(m);
                var tt = new Label(l.Title); tt.AddToClassList("mp-lessontitle"); tt.style.flexGrow = 1; tt.style.flexShrink = 1; row.Add(tt);
                var meta = new Label("≈ " + l.Minutes + T(" dk", " min") + (l.Quiz.Count > 0 ? "  •  " + l.Quiz.Count + T(" soru", " questions") : "")); meta.AddToClassList("mp-topicmeta"); row.Add(meta);
                b.Add(row);
                c.Add(b);
            }
            var row2 = UIKit.ButtonRow(c);
            if (t.Lessons.Count > 0) UIKit.Button(row2, T("İlk dersle başla  →", "Start with lesson 1  →"), () => Show("lesson:" + t.Lessons[0].Key), "mp-btn-primary");
            UIKit.Button(row2, T("Bu konudan test", "Quiz on this topic"), () => Show("exam:" + t.Id));
            if (!string.IsNullOrEmpty(t.Sim)) UIKit.Button(row2, T("Laboratuvarı aç", "Open the lab"), () => App.Open(t.Sim));
            BuildFormulas(t);
            RefreshNav();
        }

        // ================================================================== lesson
        void ShowLesson(Lesson l)
        {
            var c = Page();
            reading = l;
            var crumb = new Label(l.Topic.Title + "  ›  " + T("Ders ", "Lesson ") + (l.Topic.Lessons.IndexOf(l) + 1) + "/" + l.Topic.Lessons.Count); crumb.AddToClassList("mp-crumb"); c.Add(crumb);
            var h = new Label(l.Title); h.AddToClassList("mp-read-title"); c.Add(h);
            var chips = new VisualElement(); chips.style.flexDirection = FlexDirection.Row; chips.style.marginBottom = 8; c.Add(chips);
            Chip(chips, "≈ " + l.Minutes + T(" dk okuma", " min read"));
            if (l.Quiz.Count > 0) Chip(chips, l.Quiz.Count + T(" soruluk test", " quiz questions"));
            foreach (var b in l.Blocks) c.Add(Render(b));
            if (l.Quiz.Count > 0) BuildQuiz(c, l);
            // links and navigation
            var links = UIKit.Section(c, T("Uygula", "Apply it"));
            var lr = UIKit.ButtonRow(links);
            if (!string.IsNullOrEmpty(l.Sim)) UIKit.Button(lr, T("Laboratuvarda dene  →", "Try it in the lab  →"), () => App.Open(l.Sim), "mp-btn-primary");
            if (!string.IsNullOrEmpty(l.Case)) UIKit.Button(lr, T("İlgili klinik vaka  →", "Related clinical case  →"), () => CasesModule.Launch(l.Case));
            var nav = UIKit.ButtonRow(c);
            var all = new List<Lesson>(); foreach (var t in Curriculum.Topics) all.AddRange(t.Lessons);
            int i = all.IndexOf(l);
            if (i > 0) { var p = all[i - 1]; UIKit.Button(nav, "←  " + p.Title, () => Show("lesson:" + p.Key)); }
            if (i < all.Count - 1) { var n = all[i + 1]; UIKit.Button(nav, n.Title + "  →", () => Show("lesson:" + n.Key), "mp-btn-primary"); }
            BuildFormulas(l.Topic);
            RefreshNav();
        }

        void CheckRead()
        {
            if (reading == null || LearnProgress.Read(reading)) return;
            var sc = readerScroll.verticalScroller;
            if (readerScroll.contentContainer.layout.height <= 0) return;
            if (sc.highValue < 1f || sc.value >= sc.highValue - 60f) { LearnProgress.MarkRead(reading); RefreshNav(); }
        }

        static void Chip(VisualElement parent, string text) { var c = new Label(text); c.AddToClassList("mp-chip"); parent.Add(c); }

        static VisualElement Render(Block b)
        {
            var e = RenderBlock(b);
            // right-to-left languages: formulas, and text still waiting for its translation, keep the English direction
            if (Loc.Current.Rtl && (b.Kind == BlockKind.Equation || b.Kind == BlockKind.Figure || !Loc.Translated(b.En)))
                e.languageDirection = LanguageDirection.LTR;
            return e;
        }

        static VisualElement RenderBlock(Block b)
        {
            switch (b.Kind)
            {
                case BlockKind.Heading: { var l = new Label(b.Text); l.AddToClassList("mp-learn-h"); l.enableRichText = true; return l; }
                case BlockKind.Equation: { var l = new Label(b.Text); l.AddToClassList("mp-eq"); return l; }
                case BlockKind.Bullet: { var l = new Label("•  " + b.Text); l.AddToClassList("mp-learn"); l.AddToClassList("mp-bullet"); l.enableRichText = true; return l; }
                case BlockKind.Figure: return LessonFigures.Build(b.Tr);
                case BlockKind.Key: return Box("mp-keybox", T("ÖNEMLİ", "KEY POINT"), b.Text);
                case BlockKind.Example: return Box("mp-examplebox", T("ÇÖZÜMLÜ ÖRNEK", "WORKED EXAMPLE"), b.Text);
                case BlockKind.Warning: return Box("mp-warnbox", T("DİKKAT", "CAUTION"), b.Text);
                default: { var l = new Label(b.Text); l.AddToClassList("mp-learn"); l.enableRichText = true; return l; }
            }
        }

        static VisualElement Box(string cls, string tag, string text)
        {
            var v = new VisualElement(); v.AddToClassList("mp-callout"); v.AddToClassList(cls);
            var t = new Label(tag); t.AddToClassList("mp-callout-tag"); v.Add(t);
            var l = new Label(text); l.AddToClassList("mp-learn"); l.enableRichText = true; l.style.marginBottom = 0; v.Add(l);
            return v;
        }

        // ================================================================== quizzes
        void BuildQuiz(VisualElement c, Lesson l)
        {
            var sec = UIKit.Section(c, T("Kendini test et", "Test yourself"));
            int answered = 0, correct = 0;
            var score = new Label(""); score.AddToClassList("mp-fb-title");
            for (int qi = 0; qi < l.Quiz.Count; qi++)
            {
                var q = l.Quiz[qi];
                var ql = new Label((qi + 1) + ". " + q.Text); ql.AddToClassList("mp-prompt"); ql.enableRichText = true; sec.Add(ql);
                var btns = new List<Button>();
                var fb = new Label(""); fb.AddToClassList("mp-learn"); fb.style.display = DisplayStyle.None; fb.enableRichText = true;
                var order = Shuffled(q.Options.Count, q.Text.GetHashCode());
                bool done = false;
                foreach (int oi in order)
                {
                    var o = q.Options[oi];
                    Button b = null;
                    b = new Button(() =>
                    {
                        if (done) return;
                        done = true; answered++;
                        if (o.ok) correct++;
                        foreach (var x in btns) x.SetEnabled(false);
                        for (int k = 0; k < btns.Count; k++)
                        {
                            var ok = q.Options[order[k]].ok;
                            btns[k].EnableInClassList("mp-opt-good", ok);
                            if (btns[k] == b && !ok) btns[k].AddToClassList("mp-opt-bad");
                        }
                        fb.text = (o.ok ? "<color=#3DDC97>✓ " + T("Doğru. ", "Correct. ") : "<color=#FF5A5F>✗ " + T("Yanlış. ", "Not quite. ")) + "</color>" + q.Why;
                        fb.style.display = DisplayStyle.Flex;
                        if (answered == l.Quiz.Count)
                        {
                            LearnProgress.SaveQuiz(l, correct);
                            LearnProgress.MarkRead(l);
                            score.text = (correct == l.Quiz.Count ? "★ " : "") + T("Sonuç: ", "Score: ") + correct + " / " + l.Quiz.Count +
                                         (correct == l.Quiz.Count ? T("  — ders tamamlandı!", "  — lesson mastered!") : T("  — açıklamaları okuyup tekrar dene.", "  — read the explanations and try again."));
                            RefreshNav();
                        }
                    }) { text = T(o.tr, o.en) };
                    b.AddToClassList("mp-btn"); b.AddToClassList("mp-opt");
                    btns.Add(b); sec.Add(b);
                }
                sec.Add(fb);
            }
            sec.Add(score);
            var again = UIKit.ButtonRow(sec);
            UIKit.Button(again, T("Testi sıfırla", "Reset quiz"), () => ShowLesson(l));
        }

        static int[] Shuffled(int n, int seed)
        {
            var a = new int[n]; for (int i = 0; i < n; i++) a[i] = i;
            var r = new System.Random(seed);
            for (int i = n - 1; i > 0; i--) { int j = r.Next(i + 1); (a[i], a[j]) = (a[j], a[i]); }
            return a;
        }

        // ---------------------------------------------------------------- practice exam
        List<Question> exam;
        int examIdx, examScore;
        readonly List<(Question q, bool ok)> examLog = new List<(Question, bool)>();
        Topic examTopic;

        void StartExam(Topic only)
        {
            examTopic = only;
            var pool = new List<Question>();
            foreach (var q in Curriculum.AllQuestions()) if (only == null || q.TopicId == only.Id) pool.Add(q);
            var r = new System.Random(Environment.TickCount);
            for (int i = pool.Count - 1; i > 0; i--) { int j = r.Next(i + 1); (pool[i], pool[j]) = (pool[j], pool[i]); }
            exam = pool.GetRange(0, Math.Min(only == null ? 15 : 10, pool.Count));
            examIdx = 0; examScore = 0; examLog.Clear();
            ExamQuestion();
        }

        void ExamQuestion()
        {
            var c = Page();
            var crumb = new Label(T("Deneme sınavı", "Practice exam") + (examTopic != null ? "  ›  " + examTopic.Title : "")); crumb.AddToClassList("mp-crumb"); c.Add(crumb);
            if (exam.Count == 0) { UIKit.Text(c, T("Soru bulunamadı.", "No questions found.")); return; }
            if (examIdx >= exam.Count) { ExamResult(c); return; }
            var q = exam[examIdx];
            var h = new Label(T("Soru ", "Question ") + (examIdx + 1) + " / " + exam.Count); h.AddToClassList("mp-read-title"); c.Add(h);
            var bar = UIKit.Progress(c); bar.Set(examIdx / (float)exam.Count);
            var topic = Curriculum.FindTopic(q.TopicId);
            if (topic != null) Chip(c, topic.Title);
            var ql = new Label(q.Text); ql.AddToClassList("mp-prompt"); ql.style.fontSize = 19; ql.enableRichText = true; c.Add(ql);
            var btns = new List<Button>();
            var order = Shuffled(q.Options.Count, Environment.TickCount);
            var fb = new Label(""); fb.AddToClassList("mp-learn"); fb.enableRichText = true; fb.style.display = DisplayStyle.None;
            var next = new VisualElement();
            bool done = false;
            for (int k = 0; k < order.Length; k++)
            {
                var o = q.Options[order[k]];
                Button b = null;
                b = new Button(() =>
                {
                    if (done) return; done = true;
                    if (o.ok) examScore++;
                    examLog.Add((q, o.ok));
                    for (int m = 0; m < btns.Count; m++)
                    {
                        btns[m].SetEnabled(false);
                        bool ok = q.Options[order[m]].ok;
                        btns[m].EnableInClassList("mp-opt-good", ok);
                        if (btns[m] == b && !ok) btns[m].AddToClassList("mp-opt-bad");
                    }
                    fb.text = (o.ok ? "<color=#3DDC97>✓ " + T("Doğru. ", "Correct. ") : "<color=#FF5A5F>✗ " + T("Yanlış. ", "Not quite. ")) + "</color>" + q.Why;
                    fb.style.display = DisplayStyle.Flex;
                    UIKit.Button(UIKit.ButtonRow(next), examIdx < exam.Count - 1 ? T("Sonraki soru  →", "Next question  →") : T("Sonucu gör  →", "See the result  →"),
                        () => { examIdx++; ExamQuestion(); }, "mp-btn-primary");
                }) { text = T(o.tr, o.en) };
                b.AddToClassList("mp-btn"); b.AddToClassList("mp-opt");
                btns.Add(b); c.Add(b);
            }
            c.Add(fb); c.Add(next);
            RefreshNav();
        }

        void ExamResult(VisualElement c)
        {
            int n = exam.Count;
            if (examTopic == null && n == 15) LearnProgress.ExamBest = examScore;
            var h = new Label(T("Sonuç: ", "Result: ") + examScore + " / " + n); h.AddToClassList("mp-read-title"); c.Add(h);
            float p = examScore / (float)Math.Max(1, n);
            UIKit.Text(c, p >= 0.85f ? T("Mükemmel! Konulara hâkimsin.", "Excellent! You have mastered the material.")
                        : p >= 0.6f ? T("İyi. Yanlış yaptığın konuların derslerine tekrar göz at.", "Good. Revisit the lessons of the topics you missed.")
                        : T("Biraz daha çalışma gerekli. Aşağıdaki dersler yardımcı olacak.", "More study needed — the lessons below will help."), "mp-read-lead");
            var sec = UIKit.Section(c, T("Gözden geçir", "Review"));
            foreach (var (q, ok) in examLog)
            {
                var l = new Label((ok ? "<color=#3DDC97>✓</color> " : "<color=#FF5A5F>✗</color> ") + q.Text); l.AddToClassList("mp-learn"); l.enableRichText = true; sec.Add(l);
                if (!ok)
                {
                    var why = new Label(q.Why); why.AddToClassList("mp-note"); sec.Add(why);
                    var lesson = Curriculum.FindLesson(q.TopicId + "." + q.LessonId);
                    if (lesson != null) UIKit.Button(sec, T("Dersi aç: ", "Open lesson: ") + lesson.Title, () => Show("lesson:" + lesson.Key), "mp-navlesson");
                }
            }
            var row = UIKit.ButtonRow(c);
            UIKit.Button(row, T("Yeni sınav", "New exam"), () => StartExam(examTopic), "mp-btn-primary");
            UIKit.Button(row, T("Genel bakış", "Overview"), () => Show("home"));
        }

        // ================================================================== formula sheet and glossary (right panel)
        void BuildFormulas(Topic t)
        {
            var r = RightScroll.contentContainer;
            r.Clear();
            var sec = UIKit.Section(r, T("Formül kartı — ", "Formula sheet — ") + t.Title);
            foreach (var (tr, en) in t.Formulas)
            {
                // label and equation are separate translation keys
                var pt = tr.Split('\n'); var pe = en.Split('\n');
                UIKit.Text(sec, "<b>" + T(pt[0], pe[0]) + "</b>");
                if (pe.Length > 1 && pe[1].Length > 0)
                {
                    var e = new Label(T(pt[1], pe[1])); e.AddToClassList("mp-eq");
                    if (Loc.Current.Rtl) e.languageDirection = LanguageDirection.LTR;   // formulas read left to right, as in the lessons
                    sec.Add(e);
                }
            }
            if (t.Glossary.Count > 0)
            {
                var g = UIKit.Section(r, T("Sözlük", "Glossary"));
                foreach (var (ttr, ten, dtr, den) in t.Glossary) UIKit.Text(g, "<b>" + T(ttr, ten) + "</b> — " + T(dtr, den));
            }
        }

        // ================================================================== help
        protected override void BuildLearn(VisualElement c)
        {
            UIKit.H(c, T("Bu bölüm nasıl kullanılır?", "How to use this section"));
            UIKit.P(c, T("1) Soldaki listeden bir konu seç; konular temelden kliniğe doğru sıralıdır. 2) Dersi oku; ÖNEMLİ kutuları ve çözümlü örnekleri atlama. 3) Etkileşimli şekillerde kaydırıcıları oynatarak formüllerin ne söylediğini gör. 4) Ders sonundaki testi çöz: hepsi doğruysa ders ★ ile işaretlenir. 5) 'Laboratuvarda dene' ve 'İlgili klinik vaka' ile öğrendiğini uygula.",
                         "1) Choose a topic on the left; topics are ordered from foundations to the clinic. 2) Read the lesson; don't skip KEY POINT boxes and worked examples. 3) Move the figure sliders to see what the formulas say. 4) Take the end-of-lesson quiz: all correct marks the lesson with ★. 5) Use 'Try it in the lab' and 'Related clinical case' to apply what you learnt."));
            UIKit.H(c, T("Çalışma ipuçları", "Study tips"));
            UIKit.P(c, T("• Her formülde birimleri kontrol et (cm⁻¹, Gy, Sv, MBq). • Sayısal örnekleri önce kendin çöz, sonra kontrol et. • Deneme sınavında yanlış yaptığın soruların derslerine geri dön. • Mülakat için: ICRP ilkeleri, HVL, ters kare yasası, LQ/EQD2, CTDIvol/SSDE, T1/T2, PDD ve Bragg piki en sık sorulanlardır.",
                         "• Check the units in every formula (cm⁻¹, Gy, Sv, MBq). • Solve worked examples yourself first, then check. • After the practice exam, revisit the lessons of the questions you missed. • For interviews: ICRP principles, HVL, inverse square law, LQ/EQD2, CTDIvol/SSDE, T1/T2, PDD and the Bragg peak come up most often."));
            UIKit.Note(c, T("İçerik eğitim amaçlıdır; klinik karar için kullanılmaz.", "Educational content only — not for clinical decisions."));
        }

        public override void AutoAction(string action)
        {
            if (action.Contains(";")) { foreach (var a in action.Split(';')) AutoAction(a.Trim()); return; }
            if (action.StartsWith("show:")) Show(action.Substring(5));
            else if (action == "exam") Show("exam");
            else if (action == "formulas") Right.RemoveFromClassList("mp-hidden");
            else if (action.StartsWith("scroll:") && float.TryParse(action.Substring(7), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float f))
                readerScroll.schedule.Execute(() => readerScroll.verticalScroller.value = f * readerScroll.verticalScroller.highValue).StartingIn(300);
        }
    }
}
