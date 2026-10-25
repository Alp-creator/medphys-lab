using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace MedPhys.UI
{
    /// <summary>Line / area / bar chart drawn with the UI Toolkit vector API (Painter2D).</summary>
    public sealed class Chart : VisualElement
    {
        public sealed class Series
        {
            public string Name;
            public Color Color = Color.white;
            public float Width = 2f;
            public bool Fill, Bars, Dashed, Dots, Visible = true;
            public readonly List<Vector2> Pts = new List<Vector2>();
            public void Set(IList<Vector2> p) { Pts.Clear(); Pts.AddRange(p); }
        }

        public readonly List<Series> AllSeries = new List<Series>();
        public float XMin, XMax = 1, YMin, YMax = 1;
        public bool AutoY = true, LogY, ZeroY = true;
        public float YPadTop = 0.08f;
        public string XLabel = "", YLabel = "";
        public readonly List<(float x, Color c, string label)> VLines = new List<(float, Color, string)>();
        public readonly List<(float y, Color c, string label)> HLines = new List<(float, Color, string)>();
        public Func<float, string> XFmt, YFmt;

        const float L = 50, B = 32, T = 10, R = 14;
        readonly VisualElement labelLayer;
        VisualElement legend;

        public Chart(float height = 210)
        {
            AddToClassList("mp-chart");
            style.height = height;
            generateVisualContent += Draw;
            labelLayer = new VisualElement { pickingMode = PickingMode.Ignore };
            labelLayer.style.position = Position.Absolute;
            labelLayer.style.left = 0; labelLayer.style.top = 0; labelLayer.style.right = 0; labelLayer.style.bottom = 0;
            Add(labelLayer);
            RegisterCallback<GeometryChangedEvent>(_ => RebuildLabels());
        }

        public Series Add(string name, Color c, float width = 2f)
        {
            var s = new Series { Name = name, Color = c, Width = width };
            AllSeries.Add(s);
            return s;
        }

        /// <summary>Creates a legend element (call after adding series) to be placed above/below the chart.</summary>
        public VisualElement Legend()
        {
            legend = new VisualElement();
            legend.AddToClassList("mp-legend");
            RebuildLegend();
            return legend;
        }

        void RebuildLegend()
        {
            if (legend == null) return;
            legend.Clear();
            foreach (var s in AllSeries)
            {
                if (string.IsNullOrEmpty(s.Name)) continue;
                var it = new VisualElement(); it.AddToClassList("mp-legend-item");
                var sw = new VisualElement(); sw.AddToClassList("mp-legend-swatch"); sw.style.backgroundColor = s.Color;
                var tx = new Label(s.Name); tx.AddToClassList("mp-legend-text");
                it.Add(sw); it.Add(tx); legend.Add(it);
            }
        }

        public void Refresh()
        {
            if (AutoY)
            {
                float mn = float.MaxValue, mx = float.MinValue;
                foreach (var s in AllSeries)
                {
                    if (!s.Visible) continue;
                    foreach (var p in s.Pts)
                    {
                        if (p.x < XMin || p.x > XMax || float.IsNaN(p.y) || float.IsInfinity(p.y)) continue;
                        if (LogY && p.y <= 0) continue;
                        mn = Mathf.Min(mn, p.y); mx = Mathf.Max(mx, p.y);
                    }
                }
                if (mn > mx) { mn = 0; mx = 1; }
                if (LogY)
                {
                    YMin = Mathf.Pow(10, Mathf.Floor(Mathf.Log10(Mathf.Max(mn, 1e-12f))));
                    YMax = Mathf.Pow(10, Mathf.Ceil(Mathf.Log10(Mathf.Max(mx, YMin * 10))));
                }
                else
                {
                    if (ZeroY) mn = Mathf.Min(0, mn);
                    if (mx - mn < 1e-9f) mx = mn + 1;
                    YMin = mn; YMax = mx + (mx - mn) * YPadTop;
                }
            }
            RebuildLabels();
            RebuildLegend();
            MarkDirtyRepaint();
        }

        float W => contentRect.width;
        float Hh => contentRect.height;

        public Vector2 Map(float x, float y)
        {
            float nx = (x - XMin) / Mathf.Max(XMax - XMin, 1e-12f);
            float ny;
            if (LogY)
            {
                float ly = Mathf.Log10(Mathf.Max(y, YMin * 1e-3f));
                ny = (ly - Mathf.Log10(YMin)) / Mathf.Max(Mathf.Log10(YMax) - Mathf.Log10(YMin), 1e-6f);
            }
            else ny = (y - YMin) / Mathf.Max(YMax - YMin, 1e-12f);
            return new Vector2(L + nx * (W - L - R), T + (1 - ny) * (Hh - T - B));
        }

        public static float NiceStep(float range, int target = 5)
        {
            if (range <= 0) return 1;
            float raw = range / target;
            float mag = Mathf.Pow(10, Mathf.Floor(Mathf.Log10(raw)));
            float r = raw / mag;
            float nice = r < 1.5f ? 1 : r < 3.5f ? 2 : r < 7.5f ? 5 : 10;
            return nice * mag;
        }

        static string Fmt(float v, float step)
        {
            float a = Mathf.Abs(step);
            if (a >= 1) return v.ToString("0", System.Globalization.CultureInfo.InvariantCulture);
            if (a >= 0.1f) return v.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
            if (a >= 0.01f) return v.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
            return v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        }

        void RebuildLabels()
        {
            labelLayer.Clear();
            if (W < 10 || Hh < 10) return;
            float xs = NiceStep(XMax - XMin);
            for (float x = Mathf.Ceil(XMin / xs) * xs; x <= XMax + xs * 0.001f; x += xs)
            {
                var p = Map(x, LogY ? YMin : YMin);
                var l = new Label(XFmt != null ? XFmt(x) : Fmt(x, xs)); l.AddToClassList("mp-chart-tick");
                l.style.left = p.x - 14; l.style.top = Hh - B + 3;
                labelLayer.Add(l);
            }
            if (LogY)
            {
                for (float y = YMin; y <= YMax * 1.001f; y *= 10)
                {
                    var p = Map(XMin, y);
                    var l = new Label(y.ToString("0.##E0", System.Globalization.CultureInfo.InvariantCulture)); l.AddToClassList("mp-chart-tick");
                    l.style.left = 2; l.style.top = p.y - 8;
                    labelLayer.Add(l);
                }
            }
            else
            {
                float ys = NiceStep(YMax - YMin);
                for (float y = Mathf.Ceil(YMin / ys) * ys; y <= YMax + ys * 0.001f; y += ys)
                {
                    var p = Map(XMin, y);
                    var l = new Label(YFmt != null ? YFmt(y) : Fmt(y, ys)); l.AddToClassList("mp-chart-tick");
                    l.style.left = 2; l.style.top = p.y - 8;
                    labelLayer.Add(l);
                }
            }
            if (!string.IsNullOrEmpty(XLabel))
            {
                var xl = new Label(XLabel); xl.AddToClassList("mp-chart-axis");
                xl.style.right = R; xl.style.top = Hh - 18;
                labelLayer.Add(xl);
            }
            if (!string.IsNullOrEmpty(YLabel))
            {
                var yl = new Label(YLabel); yl.AddToClassList("mp-chart-axis");
                yl.style.left = L + 4; yl.style.top = T - 2;
                labelLayer.Add(yl);
            }
            foreach (var v in VLines)
            {
                if (string.IsNullOrEmpty(v.label)) continue;
                var p = Map(v.x, YMax);
                var l = new Label(v.label); l.AddToClassList("mp-chart-tick");
                l.style.left = p.x + 3; l.style.top = T + 12; l.style.color = v.c;
                labelLayer.Add(l);
            }
            foreach (var hl in HLines)
            {
                if (string.IsNullOrEmpty(hl.label)) continue;
                var p = Map(XMax, hl.y);
                var l = new Label(hl.label); l.AddToClassList("mp-chart-tick");
                l.style.right = R + 2; l.style.top = p.y - 15; l.style.color = hl.c;
                labelLayer.Add(l);
            }
        }

        void Draw(MeshGenerationContext ctx)
        {
            if (W < 10 || Hh < 10) return;
            var p = ctx.painter2D;
            // grid
            p.lineWidth = 1f;
            p.strokeColor = new Color(1, 1, 1, 0.06f);
            float xs = NiceStep(XMax - XMin);
            p.BeginPath();
            for (float x = Mathf.Ceil(XMin / xs) * xs; x <= XMax + xs * 0.001f; x += xs)
            {
                var a = Map(x, YMin); p.MoveTo(new Vector2(a.x, T)); p.LineTo(new Vector2(a.x, Hh - B));
            }
            if (LogY)
            {
                for (float y = YMin; y <= YMax * 1.001f; y *= 10) { var a = Map(XMin, y); p.MoveTo(new Vector2(L, a.y)); p.LineTo(new Vector2(W - R, a.y)); }
            }
            else
            {
                float ys = NiceStep(YMax - YMin);
                for (float y = Mathf.Ceil(YMin / ys) * ys; y <= YMax + ys * 0.001f; y += ys) { var a = Map(XMin, y); p.MoveTo(new Vector2(L, a.y)); p.LineTo(new Vector2(W - R, a.y)); }
            }
            p.Stroke();
            // axes
            p.strokeColor = new Color(1, 1, 1, 0.25f);
            p.BeginPath(); p.MoveTo(new Vector2(L, T)); p.LineTo(new Vector2(L, Hh - B)); p.LineTo(new Vector2(W - R, Hh - B)); p.Stroke();

            foreach (var hl in HLines)
            {
                if (hl.y < YMin || hl.y > YMax) continue;
                var a = Map(XMin, hl.y);
                DashedLine(p, new Vector2(L, a.y), new Vector2(W - R, a.y), hl.c, 1.2f);
            }
            foreach (var v in VLines)
            {
                if (v.x < XMin || v.x > XMax) continue;
                var a = Map(v.x, YMin);
                DashedLine(p, new Vector2(a.x, T), new Vector2(a.x, Hh - B), v.c, 1.2f);
            }

            foreach (var s in AllSeries)
            {
                if (!s.Visible || s.Pts.Count == 0) continue;
                if (s.Bars)
                {
                    p.fillColor = new Color(s.Color.r, s.Color.g, s.Color.b, s.Color.a * 0.75f);
                    float bw = s.Pts.Count > 1 ? Mathf.Abs(Map(s.Pts[1].x, 0).x - Map(s.Pts[0].x, 0).x) * 0.85f : 4;
                    bw = Mathf.Max(bw, 1f);
                    float y0 = LogY ? YMin : Mathf.Max(YMin, 0);
                    foreach (var pt in s.Pts)
                    {
                        if (pt.x < XMin || pt.x > XMax) continue;
                        var top = Map(pt.x, Mathf.Clamp(pt.y, YMin, YMax)); var bot = Map(pt.x, y0);
                        if (bot.y - top.y < 0.5f) continue;
                        p.BeginPath();
                        p.MoveTo(new Vector2(top.x - bw / 2, bot.y)); p.LineTo(new Vector2(top.x - bw / 2, top.y));
                        p.LineTo(new Vector2(top.x + bw / 2, top.y)); p.LineTo(new Vector2(top.x + bw / 2, bot.y));
                        p.ClosePath(); p.Fill();
                    }
                    continue;
                }
                if (s.Fill && s.Pts.Count > 1)
                {
                    float y0 = LogY ? YMin : Mathf.Max(YMin, 0);
                    p.fillColor = new Color(s.Color.r, s.Color.g, s.Color.b, 0.18f);
                    p.BeginPath();
                    var first = Map(Mathf.Clamp(s.Pts[0].x, XMin, XMax), y0);
                    p.MoveTo(first);
                    foreach (var pt in s.Pts)
                    {
                        if (pt.x < XMin || pt.x > XMax) continue;
                        p.LineTo(Map(pt.x, Mathf.Clamp(pt.y, YMin, YMax)));
                    }
                    p.LineTo(Map(Mathf.Clamp(s.Pts[s.Pts.Count - 1].x, XMin, XMax), y0));
                    p.ClosePath();
                    p.Fill();
                }
                if (s.Dots)
                {
                    p.fillColor = s.Color;
                    foreach (var pt in s.Pts)
                    {
                        if (pt.x < XMin || pt.x > XMax || pt.y < YMin || pt.y > YMax) continue;
                        var a = Map(pt.x, pt.y);
                        p.BeginPath(); p.Arc(a, s.Width * 1.6f, 0, 360); p.Fill();
                    }
                    continue;
                }
                p.strokeColor = s.Color;
                p.lineWidth = s.Width;
                p.lineJoin = LineJoin.Round;
                if (s.Dashed)
                {
                    for (int i = 1; i < s.Pts.Count; i++)
                    {
                        if (s.Pts[i].x < XMin || s.Pts[i - 1].x > XMax) continue;
                        DashedLine(p, Map(s.Pts[i - 1].x, Mathf.Clamp(s.Pts[i - 1].y, YMin, YMax)), Map(s.Pts[i].x, Mathf.Clamp(s.Pts[i].y, YMin, YMax)), s.Color, s.Width);
                    }
                    continue;
                }
                p.BeginPath();
                bool started = false;
                foreach (var pt in s.Pts)
                {
                    if (pt.x < XMin || pt.x > XMax || float.IsNaN(pt.y)) continue;
                    var a = Map(pt.x, Mathf.Clamp(pt.y, YMin, YMax));
                    if (!started) { p.MoveTo(a); started = true; } else p.LineTo(a);
                }
                if (started) p.Stroke();
            }
        }

        static void DashedLine(Painter2D p, Vector2 a, Vector2 b, Color c, float w)
        {
            p.strokeColor = c; p.lineWidth = w;
            float len = Vector2.Distance(a, b);
            if (len < 1) return;
            Vector2 d = (b - a) / len;
            p.BeginPath();
            for (float s = 0; s < len; s += 8f)
            {
                p.MoveTo(a + d * s);
                p.LineTo(a + d * Mathf.Min(s + 4.5f, len));
            }
            p.Stroke();
        }
    }

    /// <summary>Displays a texture with "contain" fitting; reports picks in normalised texture coordinates.</summary>
    public sealed class ImageView : VisualElement
    {
        public Texture2D Tex { get; private set; }
        public Action<Vector2> OnPick;
        public readonly Label Caption;
        public readonly VisualElement Overlay;

        public ImageView(float height, string caption = null)
        {
            AddToClassList("mp-image");
            style.height = height;
            style.backgroundSize = new BackgroundSize(BackgroundSizeType.Contain);
            Overlay = new VisualElement { pickingMode = PickingMode.Ignore };
            Overlay.style.position = Position.Absolute;
            Overlay.style.left = 0; Overlay.style.right = 0; Overlay.style.top = 0; Overlay.style.bottom = 0;
            Add(Overlay);
            Caption = new Label(caption ?? "");
            Caption.AddToClassList("mp-image-label");
            Caption.pickingMode = PickingMode.Ignore;
            Add(Caption);
            RegisterCallback<PointerDownEvent>(e => Pick(e.localPosition));
            RegisterCallback<PointerMoveEvent>(e => { if (e.pressedButtons != 0) Pick(e.localPosition); });
        }

        public void Set(Texture2D t)
        {
            Tex = t;
            style.backgroundImage = t != null ? Background.FromTexture2D(t) : new StyleBackground(StyleKeyword.None);
            MarkDirtyRepaint();
        }

        /// <summary>Rect (local coordinates) that the texture occupies.</summary>
        public Rect ImageRect()
        {
            var r = contentRect;
            if (Tex == null) return r;
            float ar = (float)Tex.width / Tex.height;
            float w = r.width, h = r.height;
            if (w / h > ar) { float nw = h * ar; return new Rect(r.x + (w - nw) / 2, r.y, nw, h); }
            float nh = w / ar; return new Rect(r.x, r.y + (h - nh) / 2, w, nh);
        }

        void Pick(Vector3 local)
        {
            if (Tex == null || OnPick == null) return;
            var ir = ImageRect();
            float u = (local.x - ir.x) / ir.width, v = 1f - (local.y - ir.y) / ir.height;
            if (u < 0 || u > 1 || v < 0 || v > 1) return;
            OnPick(new Vector2(u, v));
        }
    }
}
