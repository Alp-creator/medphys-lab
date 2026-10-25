using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using static MedPhys.Core.Loc;

namespace MedPhys.UI
{
    /// <summary>Small builder API for the runtime UI (UI Toolkit).</summary>
    public static class UIKit
    {
        public static VisualElement Section(VisualElement parent, string title)
        {
            var s = new VisualElement();
            s.AddToClassList("mp-section");
            if (!string.IsNullOrEmpty(title))
            {
                var t = new Label(title.ToUpperInvariant());
                t.AddToClassList("mp-section-title");
                s.Add(t);
            }
            parent.Add(s);
            return s;
        }

        public static Label Text(VisualElement parent, string text, string cls = "mp-text")
        {
            var l = new Label(text);
            l.AddToClassList(cls);
            l.enableRichText = true;
            parent.Add(l);
            return l;
        }

        public static Label Note(VisualElement parent, string text) => Text(parent, text, "mp-note");

        public sealed class SliderRow
        {
            public Slider Slider;
            public Label ValueLabel, NameLabel;
            public Func<float, string> Format;
            public float Value => Slider.value;
            public void Set(float v, bool notify = false)
            {
                if (notify) Slider.value = v; else Slider.SetValueWithoutNotify(v);
                ValueLabel.text = Format(Slider.value);
            }
            public void SetEnabled(bool e) { Slider.SetEnabled(e); }
        }

        /// <summary>Labelled slider with a live value read-out. step &gt; 0 snaps values.</summary>
        public static SliderRow Slider(VisualElement parent, string label, float min, float max, float value,
            Func<float, string> fmt, Action<float> onChange, float step = 0)
        {
            var row = new VisualElement(); row.AddToClassList("mp-row");
            var name = new Label(label); name.AddToClassList("mp-label");
            var val = new Label(); val.AddToClassList("mp-value");
            row.Add(name); row.Add(val);
            parent.Add(row);
            var s = new Slider(min, max) { value = value };
            s.AddToClassList("mp-slider");
            parent.Add(s);
            var sr = new SliderRow { Slider = s, ValueLabel = val, NameLabel = name, Format = fmt };
            val.text = fmt(value);
            s.RegisterValueChangedCallback(e =>
            {
                float v = e.newValue;
                if (step > 0)
                {
                    float q = Mathf.Round(v / step) * step;
                    if (Mathf.Abs(q - v) > 1e-5f) { s.SetValueWithoutNotify(q); v = q; }
                }
                val.text = fmt(v);
                onChange?.Invoke(v);
            });
            return sr;
        }

        public static Toggle Toggle(VisualElement parent, string label, bool value, Action<bool> onChange)
        {
            var t = new Toggle(label) { value = value };
            t.AddToClassList("mp-toggle");
            t.RegisterValueChangedCallback(e => onChange?.Invoke(e.newValue));
            parent.Add(t);
            return t;
        }

        public static Button Button(VisualElement parent, string label, Action onClick, string extraClass = null)
        {
            var b = new Button(onClick) { text = label };
            b.AddToClassList("mp-btn");
            if (extraClass != null) b.AddToClassList(extraClass);
            parent.Add(b);
            return b;
        }

        public static VisualElement ButtonRow(VisualElement parent)
        {
            var r = new VisualElement();
            r.AddToClassList("mp-btn-row");
            parent.Add(r);
            return r;
        }

        public sealed class Segmented
        {
            public readonly List<Button> Buttons = new List<Button>();
            public int Index;
            public Action<int> OnChange;
            public void Select(int i, bool notify = true)
            {
                Index = i;
                for (int k = 0; k < Buttons.Count; k++) Buttons[k].EnableInClassList("mp-seg-on", k == i);
                if (notify) OnChange?.Invoke(i);
            }
        }

        /// <summary>Row of mutually exclusive buttons (touch friendly alternative to a dropdown).</summary>
        public static Segmented Seg(VisualElement parent, string label, string[] options, int index, Action<int> onChange)
        {
            if (!string.IsNullOrEmpty(label))
            {
                var l = new Label(label); l.AddToClassList("mp-label"); parent.Add(l);
            }
            var row = new VisualElement(); row.AddToClassList("mp-seg");
            parent.Add(row);
            var seg = new Segmented { OnChange = onChange };
            for (int i = 0; i < options.Length; i++)
            {
                int k = i;
                var b = new Button(() => seg.Select(k)) { text = options[i] };
                b.AddToClassList("mp-seg-btn");
                row.Add(b);
                seg.Buttons.Add(b);
            }
            seg.Select(index, false);
            return seg;
        }

        public static Label KV(VisualElement parent, string label, string value = "–")
        {
            var r = new VisualElement(); r.AddToClassList("mp-kv");
            var l = new Label(label); l.AddToClassList("mp-kv-label");
            var v = new Label(value); v.AddToClassList("mp-kv-value");
            v.enableRichText = true;
            r.Add(l); r.Add(v);
            parent.Add(r);
            return v;
        }

        public static void SetStatus(Label l, int level)
        {
            l.EnableInClassList("mp-good", level == 0);
            l.EnableInClassList("mp-warn", level == 1);
            l.EnableInClassList("mp-bad", level == 2);
        }

        public sealed class ProgressBar
        {
            public VisualElement Root, Fill;
            public void Set(float f) { Fill.style.width = Length.Percent(Mathf.Clamp01(f) * 100f); }
        }

        public static ProgressBar Progress(VisualElement parent)
        {
            var r = new VisualElement(); r.AddToClassList("mp-progress");
            var f = new VisualElement(); f.AddToClassList("mp-progress-fill");
            r.Add(f); parent.Add(r);
            return new ProgressBar { Root = r, Fill = f };
        }

        // ---------------------------------------------------------------- learn-page helpers
        public static void H(VisualElement p, string t) { var l = new Label(t); l.AddToClassList("mp-learn-h"); l.enableRichText = true; p.Add(l); }
        public static void P(VisualElement p, string t) { var l = new Label(t); l.AddToClassList("mp-learn"); l.enableRichText = true; p.Add(l); }
        public static void Eq(VisualElement p, string t)
        {
            var l = new Label(t); l.AddToClassList("mp-eq"); l.enableRichText = true;
            l.languageDirection = LanguageDirection.LTR;        // formulas read left to right in every language
            p.Add(l);
        }

        // ---------------------------------------------------------------- textures
        /// <summary>
        /// Horizontal colour scale for scientific maps (dose, dose rate, activity): the gradient of <paramref name="map"/>
        /// over 0..1 with tick labels at the given positions (0..1 along the bar).
        /// </summary>
        public static VisualElement ColorBar(Func<float, Color> map, string title, params (float pos, string label)[] ticks)
        {
            var root = new VisualElement(); root.AddToClassList("mp-colorbar");
            var t = new Label(title); t.AddToClassList("mp-colorbar-title"); t.enableRichText = true; root.Add(t);
            var tex = NewTex(256, 1);
            var px = new Color[256];
            for (int i = 0; i < 256; i++) { var c = map(i / 255f); c.a = 1; px[i] = c; }
            tex.SetPixels(px); tex.Apply();
            var bar = new VisualElement(); bar.AddToClassList("mp-colorbar-bar");
            bar.style.backgroundImage = new StyleBackground(tex);
            root.Add(bar);
            var row = new VisualElement(); row.AddToClassList("mp-colorbar-ticks"); root.Add(row);
            foreach (var (pos, label) in ticks)
            {
                var mark = new VisualElement(); mark.AddToClassList("mp-colorbar-mark"); mark.style.left = Length.Percent(pos * 100); bar.Add(mark);
                var l = new Label(label); l.AddToClassList("mp-colorbar-tick");
                l.style.left = Length.Percent(pos * 100);
                l.style.translate = new Translate(Length.Percent(pos <= 0.001f ? 0 : pos >= 0.999f ? -100 : -50), 0);
                row.Add(l);
            }
            return root;
        }

        public static Texture2D NewTex(int w, int h, bool linear = false)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false, linear);
            t.wrapMode = TextureWrapMode.Clamp;
            t.filterMode = FilterMode.Bilinear;
            return t;
        }

        /// <summary>Writes a grayscale float image (0..1, row-major, row 0 = bottom) into a texture.</summary>
        public static void Gray(Texture2D t, float[] img, int w, int h)
        {
            var px = new Color32[w * h];
            for (int i = 0; i < px.Length; i++)
            {
                byte b = (byte)Mathf.Clamp(Mathf.RoundToInt(img[i] * 255f), 0, 255);
                px[i] = new Color32(b, b, b, 255);
            }
            t.SetPixels32(px);
            t.Apply(false);
        }
    }

    /// <summary>Colour maps for dose / signal images.</summary>
    public static class ColorMaps
    {
        static readonly Color[] Jet = {
            new Color(0.0f, 0.0f, 0.5f), new Color(0.0f, 0.2f, 1.0f), new Color(0.0f, 0.9f, 1.0f),
            new Color(0.4f, 1.0f, 0.4f), new Color(1.0f, 0.9f, 0.0f), new Color(1.0f, 0.3f, 0.0f), new Color(0.6f, 0.0f, 0.0f) };
        static readonly Color[] Inferno = {
            new Color(0.0f, 0.0f, 0.02f), new Color(0.26f, 0.04f, 0.41f), new Color(0.58f, 0.15f, 0.40f),
            new Color(0.87f, 0.32f, 0.23f), new Color(0.99f, 0.65f, 0.04f), new Color(0.99f, 1.0f, 0.64f) };

        static Color Sample(Color[] map, float t)
        {
            t = Mathf.Clamp01(t) * (map.Length - 1);
            int i = Mathf.Min((int)t, map.Length - 2);
            return Color.Lerp(map[i], map[i + 1], t - i);
        }

        public static Color DoseJet(float t) => Sample(Jet, t);
        public static Color Hot(float t) => Sample(Inferno, t);
    }
}
