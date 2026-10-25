using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using MedPhys.Core;
using MedPhys.UI;
using static MedPhys.Core.Loc;

namespace MedPhys.Modules
{
    /// <summary>
    /// Base class of every simulation module. Provides the standard screen frame (top bar, left control panel,
    /// right results panel, bottom tip line), a "Learn" theory overlay, learning tasks and 3D labels.
    /// </summary>
    public abstract class ModuleBase
    {
        protected App App;
        protected GameObject Root;
        protected VisualElement Top, Left, Right, Bottom;
        protected ScrollView LeftScroll, RightScroll;
        protected Label Tip;
        VisualElement modal;
        VisualElement taskBox;

        public abstract string Id { get; }
        public abstract string Title { get; }
        public virtual string Subtitle => "";
        protected virtual bool HasFrame => true;
        /// <summary>Results are secondary while a learner is choosing inputs, so most labs start with them collapsed.</summary>
        protected virtual bool ShowResultsInitially => false;
        protected virtual string LeftPanelLabel => T("Ayarlar", "Settings");
        protected virtual string RightPanelLabel => T("Sonuçlar", "Results");
        protected virtual string HelpLabel => T("Yardım", "Help");
        protected virtual (string id, string tr, string en)[] Tasks => Array.Empty<(string, string, string)>();
        /// <summary>Learning-centre topic with the full lesson for this module (null = none).</summary>
        protected virtual string LearnTopic => Id == "xray" ? "xray" : Id == "ct" ? "ct" : Id == "therapy" ? "rt" : Id == "nucmed" ? "nm"
                                             : Id == "mri" ? "mri" : Id == "protection" ? "rp" : null;

        readonly List<(Vector3 pos, Transform t, Label l)> labels3d = new List<(Vector3, Transform, Label)>();
        VisualElement labelLayer;

        public void Init(App app)
        {
            App = app;
            Root = new GameObject("Module_" + Id);
            labelLayer = new VisualElement { pickingMode = PickingMode.Ignore };
            labelLayer.style.position = Position.Absolute;
            labelLayer.style.left = 0; labelLayer.style.right = 0; labelLayer.style.top = 0; labelLayer.style.bottom = 0;
            App.UIRoot.Insert(0, labelLayer);
            if (HasFrame) BuildFrame();
            Build();
            if (callouts.Count > 0 && HasFrame) AddPartsButton();
        }

        protected abstract void Build();
        public virtual void Tick(float dt) { UpdateLabels(); UpdateCallouts(); UpdateLegend(); }
        public virtual void AutoAction(string action) { }

        /// <summary>Test hook: generic actions first ("learn"), then module-specific ones.</summary>
        public void RunAutoAction(string action)
        {
            if (action == "learn") { OpenLearn(); return; }
            AutoAction(action);
        }

        public virtual void Dispose()
        {
            if (Root != null) UnityEngine.Object.Destroy(Root);
        }

        public virtual bool HandleBack()
        {
            if (modal != null) { CloseModal(); return true; }
            return false;
        }

        // ------------------------------------------------------------------ frame
        void BuildFrame()
        {
            var ui = App.UIRoot;
            Top = new VisualElement(); Top.AddToClassList("mp-topbar");
            var back = UIKit.Button(Top, "←", () => App.Open("hub"), "mp-btn-icon");
            back.tooltip = T("Ana menü", "Main menu");
            var title = new Label(Title); title.AddToClassList("mp-title"); Top.Add(title);
            var sub = new Label(Subtitle); sub.AddToClassList("mp-subtitle"); Top.Add(sub);
            if (App.SmallScreen) sub.style.display = DisplayStyle.None;
            var sp = new VisualElement(); sp.AddToClassList("mp-spacer"); Top.Add(sp);
            UIKit.Button(Top, LeftPanelLabel, () => TogglePanel(Left));
            UIKit.Button(Top, RightPanelLabel, () => TogglePanel(Right));
            UIKit.Button(Top, HelpLabel, OpenLearn, "mp-btn-primary");
            var langBtn = UIKit.Button(Top, Loc.Code.ToUpperInvariant(), LanguagePicker.Open, "mp-btn-icon");
            langBtn.tooltip = T("Dil", "Language");
            ui.Add(Top);

            Left = new VisualElement(); Left.AddToClassList("mp-side"); Left.AddToClassList("mp-left");
            LeftScroll = new ScrollView(ScrollViewMode.Vertical); LeftScroll.AddToClassList("mp-scroll");
            Left.Add(LeftScroll); ui.Add(Left);

            Right = new VisualElement(); Right.AddToClassList("mp-side"); Right.AddToClassList("mp-right");
            RightScroll = new ScrollView(ScrollViewMode.Vertical); RightScroll.AddToClassList("mp-scroll");
            Right.Add(RightScroll); ui.Add(Right);
            if (!ShowResultsInitially) Right.AddToClassList("mp-hidden");

            Bottom = new VisualElement(); Bottom.AddToClassList("mp-bottombar");
            Tip = new Label(""); Tip.AddToClassList("mp-tip"); Tip.enableRichText = true;
            Bottom.Add(Tip); ui.Add(Bottom);
            Bottom.pickingMode = PickingMode.Ignore; Tip.pickingMode = PickingMode.Ignore;

            if (App.SmallScreen)
            {
                Left.style.width = 330; Right.style.width = 360;
                Right.AddToClassList("mp-hidden");
            }
        }

        void TogglePanel(VisualElement p)
        {
            bool hidden = p.ClassListContains("mp-hidden");
            p.EnableInClassList("mp-hidden", !hidden);
            if (App.SmallScreen && hidden)
            {
                // on phones only one side panel at a time
                var other = p == Left ? Right : Left;
                other.EnableInClassList("mp-hidden", true);
            }
        }

        protected void SetTip(string tr, string en) { if (Tip != null) Tip.text = T(tr, en); }

        // ------------------------------------------------------------------ tasks
        protected void BuildTasks(VisualElement parent)
        {
            if (Tasks.Length == 0) return;
            var sec = UIKit.Section(parent, T("Görevler", "Tasks"));
            taskBox = new VisualElement();
            sec.Add(taskBox);
            RefreshTasks();
        }

        void RefreshTasks()
        {
            if (taskBox == null) return;
            taskBox.Clear();
            foreach (var t in Tasks)
            {
                bool done = Progress.IsDone(Id, t.id);
                var row = new VisualElement(); row.AddToClassList("mp-task");
                var box = new Label(done ? "✓" : ""); box.AddToClassList("mp-task-box");
                if (done) box.AddToClassList("mp-task-done");
                var tx = new Label(T(t.tr, t.en)); tx.AddToClassList("mp-task-text");
                row.Add(box); row.Add(tx);
                taskBox.Add(row);
            }
        }

        protected void CompleteTask(string id)
        {
            if (!Progress.Complete(Id, id)) return;
            foreach (var t in Tasks)
                if (t.id == id) App.Toast("✓ " + T("Görev tamamlandı: ", "Task complete: ") + T(t.tr, t.en));
            RefreshTasks();
        }

        // ------------------------------------------------------------------ learn overlay
        protected virtual void BuildLearn(VisualElement c) { UIKit.P(c, T("Bu modül için teori yakında.", "Theory coming soon.")); }

        protected void OpenLearn()
        {
            if (modal != null) { CloseModal(); return; }
            modal = new VisualElement(); modal.AddToClassList("mp-modal-bg");
            var box = new VisualElement(); box.AddToClassList("mp-modal");
            var head = new VisualElement(); head.style.flexDirection = FlexDirection.Row; head.style.alignItems = Align.Center;
            var t = new Label(Title + " — " + T("Teori ve formüller", "Theory and formulas")); t.AddToClassList("mp-modal-title");
            t.style.flexGrow = 1;
            head.Add(t);
            if (LearnTopic != null)
                UIKit.Button(head, T("Ayrıntılı ders  →", "Full lesson  →"), () => LearnModule.OpenTopic(LearnTopic), "mp-btn-primary");
            UIKit.Button(head, "×", CloseModal, "mp-btn-icon");
            box.Add(head);
            var sv = new ScrollView(ScrollViewMode.Vertical); sv.style.flexGrow = 1;
            BuildLearn(sv.contentContainer);
            box.Add(sv);
            modal.Add(box);
            modal.RegisterCallback<PointerDownEvent>(e => { if (e.target == modal) CloseModal(); });
            App.UIRoot.Add(modal);
        }

        protected void CloseModal()
        {
            modal?.RemoveFromHierarchy();
            modal = null;
        }

        // ------------------------------------------------------------------ 3D labels
        /// <summary>UI elements a 3D label must not show through (it is hidden while it overlaps one).</summary>
        protected readonly List<VisualElement> LabelOccluders = new List<VisualElement>();

        protected Label Label3D(Vector3 worldPos, string text, Transform follow = null)
        {
            var l = new Label(text); l.AddToClassList("mp-label3d");
            l.pickingMode = PickingMode.Ignore;
            labelLayer.Add(l);
            labels3d.Add((worldPos, follow, l));
            return l;
        }

        protected void ClearLabels3D()
        {
            foreach (var x in labels3d) x.l.RemoveFromHierarchy();
            labels3d.Clear();
        }

        protected void UpdateLabels()
        {
            if (labels3d.Count == 0 || App.Cam == null) return;
            var panel = App.UIRoot.panel;
            if (panel == null) return;
            foreach (var (pos, t, l) in labels3d)
            {
                var w = t != null ? t.position : pos;
                var sp = App.Cam.WorldToScreenPoint(w);
                if (sp.z < 0.1f || l.style.display == DisplayStyle.None && false) { l.style.visibility = Visibility.Hidden; continue; }
                var pp = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(sp.x, Screen.height - sp.y));
                var r = new Rect(pp.x + 6, pp.y - 10, l.resolvedStyle.width, l.resolvedStyle.height);
                bool covered = false;
                foreach (var o in LabelOccluders) covered |= o.panel != null && o.worldBound.Overlaps(r);
                l.style.visibility = covered ? Visibility.Hidden : Visibility.Visible;
                l.style.left = pp.x + 6; l.style.top = pp.y - 10;
            }
        }

        // ------------------------------------------------------------------ part callouts ("what is this part?")
        sealed class Callout
        {
            public Transform Anchor; public Vector3 Local; public Vector2 Dir;
            public VisualElement Box; public Label Body;
            public Vector2 A, B; public bool Visible;
            public Rect R;
        }
        readonly List<Callout> callouts = new List<Callout>();
        VisualElement calloutLayer;
        Button partsBtn;
        bool partsOn;

        /// <summary>
        /// Registers a labelled callout for a machine part: a title box joined to the part's centre by a leader line.
        /// Tapping the title shows what the part does. <paramref name="dir"/> is where the box sits on screen relative to
        /// the part (x right, y up). Callouts follow moving parts and are toggled with the "Parts" button.
        /// </summary>
        /// <param name="at">World point to point at instead of the centre of the part (e.g. the rim of a ring).</param>
        protected void Part(Transform t, string tr, string en, string infoTr, string infoEn, Vector2 dir, float reach = 90f, Vector3? at = null)
        {
            if (t == null) return;
            var rs = t.GetComponentsInChildren<Renderer>();
            Vector3 c = t.position;
            if (rs.Length > 0) { var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds); c = b.center; }
            if (at.HasValue) c = at.Value;
            var co = new Callout { Anchor = t, Local = t.InverseTransformPoint(c), Dir = dir.normalized * reach };
            var box = new VisualElement(); box.AddToClassList("mp-part");
            var title = new Label(T(tr, en)); title.AddToClassList("mp-part-title"); box.Add(title);
            var body = new Label(T(infoTr, infoEn)); body.AddToClassList("mp-part-body"); body.enableRichText = true;
            body.style.display = DisplayStyle.None; box.Add(body);
            co.Box = box; co.Body = body;
            box.RegisterCallback<ClickEvent>(e =>
            {
                bool open = co.Body.style.display == DisplayStyle.None;
                foreach (var o in callouts) { o.Body.style.display = DisplayStyle.None; o.Box.RemoveFromClassList("mp-part-open"); }
                if (open) { co.Body.style.display = DisplayStyle.Flex; co.Box.AddToClassList("mp-part-open"); co.Box.BringToFront(); }
                e.StopPropagation();
            });
            callouts.Add(co);
            CalloutLayer().Add(box);
        }

        VisualElement CalloutLayer()
        {
            if (calloutLayer != null) return calloutLayer;
            calloutLayer = new VisualElement { pickingMode = PickingMode.Ignore };
            calloutLayer.style.position = Position.Absolute;
            calloutLayer.style.left = 0; calloutLayer.style.right = 0; calloutLayer.style.top = 0; calloutLayer.style.bottom = 0;
            calloutLayer.generateVisualContent += DrawLeaders;
            App.UIRoot.Insert(App.UIRoot.IndexOf(labelLayer) + 1, calloutLayer);
            return calloutLayer;
        }

        void AddPartsButton()
        {
            partsBtn = new Button(() => SetParts(!partsOn)) { text = T("Parçalar", "Parts") };
            partsBtn.AddToClassList("mp-btn");
            var sp = Top.Q(className: "mp-spacer");
            Top.Insert(sp != null ? Top.IndexOf(sp) + 1 : Top.childCount, partsBtn);
            // shown the first time a learner opens the lab, afterwards as they left it
            string key = "parts_" + Id;
            SetParts(PlayerPrefs.GetInt(key, 1) == 1);
        }

        protected bool PartsShown => partsOn;

        protected void SetParts(bool on)
        {
            partsOn = on;
            PlayerPrefs.SetInt("parts_" + Id, on ? 1 : 0);
            if (!on) foreach (var c in callouts) { c.Visible = false; c.Box.style.display = DisplayStyle.None; }
            calloutLayer?.MarkDirtyRepaint();
            partsBtn?.EnableInClassList("mp-btn-on", on);
            OnPartsToggled(on);
        }

        /// <summary>Lets a module open up its machine (e.g. fade a cover) while the part labels are shown.</summary>
        protected virtual void OnPartsToggled(bool on) { }

        // ------------------------------------------------------------------ dimension lines (SID, SAD, distance...)
        sealed class Dim
        {
            public Func<Vector3> A, B; public Func<string> Text; public bool Always;
            public Label L; public Vector2 PA, PB, CA, CB; public bool Visible;
        }
        readonly List<Dim> dims = new List<Dim>();

        /// <summary>Liang–Barsky clipping of segment a–b to a rectangle; false when nothing is inside.</summary>
        static bool Clip(Vector2 a, Vector2 b, float x0, float x1, float y0, float y1, out Vector2 ca, out Vector2 cb)
        {
            float t0 = 0, t1 = 1; var d = b - a;
            float[] p = { -d.x, d.x, -d.y, d.y }, q = { a.x - x0, x1 - a.x, a.y - y0, y1 - a.y };
            for (int i = 0; i < 4; i++)
            {
                if (Mathf.Abs(p[i]) < 1e-6f) { if (q[i] < 0) { ca = cb = a; return false; } continue; }
                float r = q[i] / p[i];
                if (p[i] < 0) t0 = Mathf.Max(t0, r); else t1 = Mathf.Min(t1, r);
            }
            ca = a + d * t0; cb = a + d * t1;
            return t0 < t1;
        }

        /// <summary>
        /// A dimension line between two (moving) 3D points with a live label, e.g. "SID = 180 cm". Shown with the part
        /// labels, or all the time when <paramref name="always"/> is set. A null text hides it.
        /// </summary>
        protected void Dimension(Func<Vector3> a, Func<Vector3> b, Func<string> text, bool always = false)
        {
            var d = new Dim { A = a, B = b, Text = text, Always = always, L = new Label() };
            d.L.AddToClassList("mp-dim"); d.L.pickingMode = PickingMode.Ignore; d.L.style.display = DisplayStyle.None;
            dims.Add(d);
            CalloutLayer().Add(d.L);
        }

        void UpdateDims(IPanel panel, float minX, float maxX, float minY, float maxY)
        {
            foreach (var d in dims)
            {
                string text = (partsOn || d.Always) ? d.Text() : null;
                d.Visible = text != null;
                Vector3 sa = Vector3.zero, sb = Vector3.zero;
                if (d.Visible) { sa = App.Cam.WorldToScreenPoint(d.A()); sb = App.Cam.WorldToScreenPoint(d.B()); d.Visible = sa.z > 0.1f && sb.z > 0.1f; }
                if (d.Visible)
                {
                    d.PA = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(sa.x, Screen.height - sa.y));
                    d.PB = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(sb.x, Screen.height - sb.y));
                    d.Visible = (d.PB - d.PA).magnitude > 30f && Clip(d.PA, d.PB, minX, maxX, minY, maxY, out d.CA, out d.CB) && (d.CB - d.CA).magnitude > 24f;
                }
                d.L.style.display = d.Visible ? DisplayStyle.Flex : DisplayStyle.None;
                if (!d.Visible) continue;
                d.L.text = text;
                var m = (d.CA + d.CB) * 0.5f;      // the label sits on the part of the line that is on screen
                var n = (d.PB - d.PA).normalized; n = new Vector2(-n.y, n.x); if (n.y > 0) n = -n;   // label above the line
                float w = d.L.resolvedStyle.width, h = d.L.resolvedStyle.height;
                if (float.IsNaN(w) || w <= 0) w = 90; if (float.IsNaN(h) || h <= 0) h = 18;
                var c = m + n * (h * 0.5f + 6f);
                d.L.style.left = c.x - w * 0.5f; d.L.style.top = c.y - h * 0.5f;
            }
        }

        void UpdateCallouts()
        {
            if (calloutLayer == null || App.Cam == null) return;
            var panel = App.UIRoot.panel;
            if (panel == null) return;
            var size = App.UIRoot.layout.size;
            // usable area: between the open side panels, below the top bar, above the tip line
            float minX = 4, maxX = size.x - 4, minY = 62, maxY = size.y - 50;
            if (Left != null && !Left.ClassListContains("mp-hidden")) minX = Left.layout.xMax + 8;
            if (Right != null && !Right.ClassListContains("mp-hidden")) maxX = Right.layout.xMin - 8;
            UpdateDims(panel, minX, maxX, minY, maxY);
            if (!partsOn) { calloutLayer.MarkDirtyRepaint(); return; }
            var shown = new List<Callout>();
            foreach (var c in callouts)
            {
                bool live = c.Anchor != null && c.Anchor.gameObject.activeInHierarchy;
                var sp = live ? App.Cam.WorldToScreenPoint(c.Anchor.TransformPoint(c.Local)) : Vector3.zero;
                c.Visible = live && sp.z > 0.1f;
                if (c.Visible)
                {
                    c.A = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(sp.x, Screen.height - sp.y));
                    // a part hidden under a side panel gets no label
                    c.Visible = c.A.x > minX && c.A.x < maxX && c.A.y > minY - 20 && c.A.y < maxY + 20;
                }
                c.Box.style.display = c.Visible ? DisplayStyle.Flex : DisplayStyle.None;
                if (!c.Visible) continue;
                var b = c.A + new Vector2(c.Dir.x, -c.Dir.y);
                float w = c.Box.resolvedStyle.width, h = c.Box.resolvedStyle.height;
                if (float.IsNaN(w) || w <= 0) w = 140; if (float.IsNaN(h) || h <= 0) h = 24;
                float left, top;
                if (Mathf.Abs(c.Dir.x) >= Mathf.Abs(c.Dir.y)) { left = c.Dir.x >= 0 ? b.x : b.x - w; top = b.y - h * 0.5f; }
                else { left = b.x - w * 0.5f; top = c.Dir.y >= 0 ? b.y - h : b.y; }
                c.R = new Rect(left, top, w, h);
                shown.Add(c);
            }
            // keep the boxes inside the free area and push overlapping ones apart vertically
            for (int it = 0; it < 6; it++)
            {
                foreach (var c in shown)
                    c.R.position = new Vector2(Mathf.Clamp(c.R.x, minX, Mathf.Max(minX, maxX - c.R.width)), Mathf.Clamp(c.R.y, minY, Mathf.Max(minY, maxY - c.R.height)));
                bool moved = false;
                for (int i = 0; i < shown.Count; i++)
                    for (int j = i + 1; j < shown.Count; j++)
                    {
                        Rect p = shown[i].R, q = shown[j].R;
                        if (!p.Overlaps(new Rect(q.x - 3, q.y - 3, q.width + 6, q.height + 6))) continue;
                        float push = (Mathf.Min(p.yMax, q.yMax) - Mathf.Max(p.y, q.y)) * 0.5f + 4f;
                        bool qBelow = q.center.y >= p.center.y;
                        shown[i].R.y += qBelow ? -push : push;
                        shown[j].R.y += qBelow ? push : -push;
                        moved = true;
                    }
                if (!moved) break;
            }
            foreach (var c in shown)
            {
                c.Box.style.left = c.R.x; c.Box.style.top = c.R.y;
                // the leader ends on the box edge nearest to the part
                c.B = new Vector2(Mathf.Clamp(c.A.x, c.R.xMin, c.R.xMax), Mathf.Clamp(c.A.y, c.R.yMin, c.R.yMax));
            }
            calloutLayer.MarkDirtyRepaint();
        }

        void DrawLeaders(MeshGenerationContext ctx)
        {
            var p = ctx.painter2D;
            foreach (var d in dims)
            {
                if (!d.Visible) continue;
                var u = (d.PB - d.PA).normalized; var n = new Vector2(-u.y, u.x) * 7f;
                p.strokeColor = new Color(1f, 0.78f, 0.35f, 0.95f);
                p.lineWidth = 1.5f;
                p.BeginPath(); p.MoveTo(d.PA); p.LineTo(d.PB); p.Stroke();
                p.BeginPath(); p.MoveTo(d.PA - n); p.LineTo(d.PA + n); p.Stroke();
                p.BeginPath(); p.MoveTo(d.PB - n); p.LineTo(d.PB + n); p.Stroke();
            }
            if (!partsOn) return;
            foreach (var c in callouts)
            {
                if (!c.Visible) continue;
                p.strokeColor = new Color(0.55f, 0.86f, 1f, 0.85f);
                p.lineWidth = 1.6f;
                p.BeginPath(); p.MoveTo(c.A); p.LineTo(c.B); p.Stroke();
                p.fillColor = new Color(0.02f, 0.06f, 0.12f, 0.9f);
                p.BeginPath(); p.Arc(c.A, 5f, Angle.Degrees(0), Angle.Degrees(360)); p.Fill();
                p.fillColor = new Color(0.55f, 0.9f, 1f, 1f);
                p.BeginPath(); p.Arc(c.A, 3f, Angle.Degrees(0), Angle.Degrees(360)); p.Fill();
            }
        }

        // ------------------------------------------------------------------ colour scale of the 3D map
        VisualElement mapLegend;

        /// <summary>Shows a colour scale for the map drawn in the 3D view (bottom right, clear of the results panel).</summary>
        protected VisualElement MapLegend(VisualElement colorBar)
        {
            mapLegend?.RemoveFromHierarchy();
            mapLegend = colorBar;
            mapLegend.AddToClassList("mp-maplegend");
            mapLegend.pickingMode = PickingMode.Ignore;
            App.UIRoot.Insert(App.UIRoot.IndexOf(Left), mapLegend);
            return mapLegend;
        }

        void UpdateLegend()
        {
            if (mapLegend == null || Right == null) return;
            mapLegend.style.right = Right.ClassListContains("mp-hidden") ? 14 : (Right.resolvedStyle.width + 24);
        }

        // ------------------------------------------------------------------ helpers
        protected GameObject Child(string name, Transform parent = null)
        {
            var g = new GameObject(name);
            g.transform.SetParent(parent != null ? parent : Root.transform, false);
            return g;
        }

        protected MeshRenderer MeshObj(string name, Mesh mesh, Material mat, Transform parent = null)
        {
            var g = Child(name, parent);
            g.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = g.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return r;
        }
    }
}
