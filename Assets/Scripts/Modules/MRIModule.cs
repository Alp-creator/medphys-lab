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
    /// <summary>MRI: Bloch-equation spin physics, sequence contrast (T1/T2/PD/FLAIR/STIR) and k-space.</summary>
    public sealed class MRIModule : ModuleBase
    {
        public override string Id => "mri";
        public override string Title => T("Manyetik Rezonans", "Magnetic Resonance");
        public override string Subtitle => tab == 0 ? T("Bloch denklemleri • presesyon • spin eko", "Bloch equations • precession • spin echo")
                                         : tab == 1 ? T("T1 / T2 / PD kontrastı", "T1 / T2 / PD contrast") : T("k-uzayı ve Fourier dönüşümü", "k-space and the Fourier transform");

        protected override (string id, string tr, string en)[] Tasks => new[]
        {
            ("echo", "90° – τ – 180° uygulayıp spin ekoyu oluştur", "Apply 90° – τ – 180° and form a spin echo"),
            ("larmor", "3 T'da protonun Larmor frekansını bul (≈ 127.7 MHz)", "Find the proton Larmor frequency at 3 T (≈ 127.7 MHz)"),
            ("t2w", "T2 ağırlıklı görüntü: BOS beyaz cevherden parlak olsun", "T2-weighted image: CSF brighter than white matter"),
            ("flair", "FLAIR: BOS sinyalini bastır, lezyon parlak kalsın", "FLAIR: suppress CSF while the lesion stays bright"),
            ("kspace", "k-uzayının yalnız merkezini kullan: kontrast kalır, detay kaybolur", "Use only the centre of k-space: contrast stays, detail is lost"),
        };

        static int tab;
        static float b0 = 1.5f;
        static readonly float[] B0s = { 0.5f, 1.5f, 3f, 7f };

        protected override void Build()
        {
            Env.Room(Root.transform, new Color(0.55f, 0.5f, 1f));
            var scanner = Res.Model("MRI_Scanner", Root.transform);
            var cover = scanner.Find("MRI_Cover");
            if (cover != null && tab == 1) cover.GetComponent<Renderer>().sharedMaterial = Res.Hologram(new Color(0.6f, 0.6f, 1f, 0.04f), new Color(0.6f, 0.6f, 1f, 0.5f), 2f);
            mriIso = scanner.Find("MRI_Iso");
            mriCouch = scanner.Find("MRI_CouchTop");
            var L = LeftScroll.contentContainer;
            var sec = UIKit.Section(L, T("Bölüm", "Section"));
            UIKit.Seg(sec, null, new[] { T("Spin fiziği", "Spin physics"), T("Kontrast", "Contrast"), T("k-uzayı", "k-space") }, tab, i => { tab = i; App.Open("mri"); });
            UIKit.Seg(sec, T("Ana alan B₀", "Main field B₀"), new[] { "0.5 T", "1.5 T", "3 T", "7 T" }, Array.IndexOf(B0s, b0), i =>
            {
                b0 = B0s[i];
                if (Mathf.Abs(b0 - 3f) < 0.01f) CompleteTask("larmor");
                App.Open("mri");
            });
            if (tab == 0) BuildSpin();
            else if (tab == 1) BuildContrast();
            else BuildKspace();
            BuildTasks(L);
            MachineParts(scanner);
        }

        void MachineParts(GameObject scanner)
        {
            if (tab == 0 && holo != null)
            {
                // spin physics: the Bloch sphere is the subject, the magnet is only the backdrop
                Part(holo, "Bloch küresi (dönen çerçeve)", "Bloch sphere (rotating frame)",
                     "Sarı ok net mıknatıslanma M'dir. RF darbesi M'yi B₀ ekseninden saptırır; M boyuna T1, enine T2 ile geri döner. Renkli oklar farklı frekanstaki izokromatlardır (T2*).",
                     "The yellow arrow is the net magnetisation M. An RF pulse tips it away from B₀; it recovers longitudinally with T1 and decays transversely with T2. Coloured arrows are isochromats at different frequencies (T2*).",
                     new Vector2(-1f, -0.8f), 130, holo.position + new Vector3(-0.2f, -0.2f, 0));
                return;
            }
            var c = mriIso.position;
            Part(scanner.Find("MRI_Housing"), "Süperiletken mıknatıs", "Superconducting magnet",
                 "NbTi sargılar sıvı helyumla ~4 K'de soğutulur. B₀ (1,5 / 3 T) her zaman açıktır: ferromanyetik cisimler mermi gibi çekilir.",
                 "NbTi windings cooled by liquid helium to ~4 K. B₀ (1.5 / 3 T) is always on: ferromagnetic objects become projectiles.",
                 new Vector2(1f, 0.6f), 110, c + new Vector3(0.75f, 0.7f, -0.3f));
            Part(scanner.Find("MRI_Cover"), "Gradyan bobinleri", "Gradient coils",
                 "Tüneli saran x, y, z bobinleri alanı konuma göre doğrusal değiştirir (mT/m): kesit seçimi, frekans ve faz kodlama. Hızlı anahtarlama yüksek ses çıkarır.",
                 "Coils around the bore make the field vary linearly with position (mT/m): slice selection, frequency and phase encoding. Fast switching is what makes the noise.",
                 new Vector2(-1f, 0.7f), 110, c + new Vector3(-0.42f, 0.42f, -0.72f));
            Part(scanner.Find("MRI_BoreLiner"), "RF gövde bobini", "RF body coil",
                 "Larmor frekansında (1,5 T'de ~64 MHz) B₁ darbeleri gönderir ve M'yi yatırır. Dokudaki ısınma (SAR) buradan gelir.",
                 "Transmits B₁ pulses at the Larmor frequency (~64 MHz at 1.5 T) that tip M. Tissue heating (SAR) comes from here.",
                 new Vector2(1f, -0.4f), 120, c + new Vector3(0.3f, 0.2f, -0.6f));
            Part(scanner.Find("MRI_HeadCoil"), "Alıcı bobin", "Receive coil",
                 "Dokuya yakın, çok kanallı alıcı bobin sinyal/gürültü oranını (SNR) artırır ve paralel görüntülemeye izin verir.",
                 "A multi-channel receive coil close to the tissue raises SNR and enables parallel imaging.", new Vector2(-1f, -0.6f), 100);
        }

        Transform mriIso, mriCouch;

        public override void Tick(float dt)
        {
            base.Tick(dt);
            if (tab == 0) TickSpin(dt);
        }

        // ====================================================================== 1. SPIN PHYSICS
        static int spinTissue;
        static float simSpeed = 60f;        // ms of simulated time per real second
        static bool labFrame;
        static float customFlip = 30f;
        BlochEnsemble ens;
        Transform holo;
        readonly List<Transform> isoArrows = new List<Transform>();
        Transform netArrow;
        LineBatch trail;
        Chart blochChart;
        Chart.Series sMz, sMxy, sMx;
        Label vF0, vT1, vT2, vT2s, vTime, vMz, vMxy, vEvent;
        float labPhase;
        double echoAt = -1; double pulse90At = -1;
        readonly List<(double t, double deg)> scheduled = new List<(double, double)>();
        float plotT;
        const float HoloR = 0.33f;

        static Vector3 ToWorld(Vector3 m) => new Vector3(m.x, m.z, -m.y);

        void BuildSpin()
        {
            var tis = MriPhysics.Tissues[spinTissue];
            ens = new BlochEnsemble(24, Math.Min(tis.T2s(b0), tis.T2), tis.T2) { T1 = tis.T1(b0) };
            holo = Child("BlochSphere").transform;
            holo.position = new Vector3(0, 1.55f, -1.55f);
            // wire sphere
            var ringMat = Res.Additive(new Color(0.5f, 0.6f, 1f, 0.5f), 1.2f);
            for (int k = 0; k < 3; k++)
            {
                var r = MeshObj("Ring" + k, MeshGen.Ring(HoloR, 0.004f, 96), ringMat, holo);
                r.transform.localRotation = k == 0 ? Quaternion.Euler(90, 0, 0) : k == 1 ? Quaternion.identity : Quaternion.Euler(0, 90, 0);
            }
            var shell = MeshObj("Shell", MeshGen.Ellipsoid(Vector3.zero, Vector3.one * HoloR, Quaternion.identity, 24, 36), Res.Hologram(new Color(0.4f, 0.5f, 1f, 0.03f), new Color(0.5f, 0.6f, 1f, 0.35f), 3f), holo);
            // axes: x red, y green, z (B0) blue
            Axis(Vector3.right, new Color(1f, 0.35f, 0.3f), "x'");
            Axis(Vector3.back, new Color(0.35f, 1f, 0.4f), "y'");
            Axis(Vector3.up, new Color(0.4f, 0.6f, 1f), "z  (B₀ = " + F(b0, 1) + " T)");
            var arrowMesh = MeshGen.Arrow(0.012f, 0.03f, 0.2f);
            for (int i = 0; i < ens.M.Length; i++)
            {
                float hue = i / (float)ens.M.Length;
                var c = Color.HSVToRGB(0.55f + 0.35f * hue, 0.6f, 1f);
                var a = MeshObj("Iso" + i, arrowMesh, Res.Additive(new Color(c.r, c.g, c.b, 0.55f), 1.2f), holo);
                a.transform.localScale = Vector3.one * HoloR;
                isoArrows.Add(a.transform);
            }
            var net = MeshObj("NetM", MeshGen.Arrow(0.03f, 0.075f, 0.2f), Res.Emissive(new Color(1f, 0.8f, 0.2f), new Color(3f, 2.2f, 0.4f)), holo);
            net.transform.localScale = Vector3.one * HoloR;
            netArrow = net.transform;
            trail = new LineBatch();
            MeshObj("Trail", trail.Mesh, Res.Additive(Color.white, 1.5f), holo);
            App.Rig.Frame(holo.position + new Vector3(0, -0.05f, 0), 1.45f, 18f, 12f, true);
            App.Rig.MinDistance = 0.4f; App.Rig.MaxDistance = 6f;

            var Lp = LeftScroll.contentContainer;
            var sec = UIKit.Section(Lp, T("Doku", "Tissue"));
            var names = new string[6];
            for (int i = 0; i < 6; i++) names[i] = T(MriPhysics.Tissues[i].Tr, MriPhysics.Tissues[i].En);
            UIKit.Seg(sec, null, names, spinTissue, i => { spinTissue = i; App.Open("mri"); });
            sec = UIKit.Section(Lp, T("RF darbeleri", "RF pulses"));
            var row = UIKit.ButtonRow(sec);
            UIKit.Button(row, "90°", () => Pulse(90), "mp-btn-primary");
            UIKit.Button(row, "180°", () => Pulse(180), "mp-btn-primary");
            UIKit.Button(row, T("Spin eko", "Spin echo"), SpinEcho);
            row = UIKit.ButtonRow(sec);
            UIKit.Button(row, T("İnversiyon (180°)", "Inversion (180°)"), () => Pulse(180));
            UIKit.Button(row, "α°", () => Pulse(customFlip));
            UIKit.Button(row, T("Sıfırla (M || B₀)", "Reset (M || B₀)"), () => { ens.Reset(); trail.Clear(); scheduled.Clear(); ClearPlot(); });
            UIKit.Slider(sec, T("Özel çevirme açısı α", "Custom flip angle α"), 5, 180, customFlip, v => F(v, 0) + "°", v => customFlip = v, 5);
            sec = UIKit.Section(Lp, T("Görünüm", "View"));
            UIKit.Slider(sec, T("Zaman hızı", "Time speed"), 5, 500, simSpeed, v => F(v, 0) + " ms/s", v => simSpeed = v, 5);
            UIKit.Toggle(sec, T("Laboratuvar çerçevesi (presesyonu göster)", "Laboratory frame (show precession)"), labFrame, v => labFrame = v);
            UIKit.Note(sec, T("Dönen çerçevede (x', y') Larmor frekansıyla birlikte döneriz: yalnızca frekans farkları (Δω) görünür.",
                              "In the rotating frame (x', y') we rotate with the Larmor frequency: only frequency offsets (Δω) remain visible."));

            var R = RightScroll.contentContainer;
            sec = UIKit.Section(R, T("Mıknatıslanma", "Magnetisation"));
            blochChart = new Chart(230) { XMin = 0, XMax = 1000, XLabel = "ms", YLabel = "M / M₀", AutoY = false, YMin = -1.05f, YMax = 1.05f };
            sMz = blochChart.Add("Mz", new Color(0.4f, 0.6f, 1f), 2f);
            sMxy = blochChart.Add("|Mxy|", new Color(1f, 0.8f, 0.2f), 2f);
            sMx = blochChart.Add(T("Mx (sinyal)", "Mx (signal)"), new Color(0.4f, 1f, 0.5f), 1.2f);
            sec.Add(blochChart.Legend()); sec.Add(blochChart);
            vEvent = UIKit.Text(sec, "");
            sec = UIKit.Section(R, T("Parametreler", "Parameters"));
            double f0 = MriPhysics.GammaBar * b0;
            vF0 = UIKit.KV(sec, T("Larmor frekansı f₀ = γ̄·B₀", "Larmor frequency f₀ = γ̄·B₀"), F(f0, 2) + " MHz  (ω₀ = " + (2 * Math.PI * f0 * 1e6).ToString("0.##E+0") + " rad/s)");
            vT1 = UIKit.KV(sec, "T1", F(tis.T1(b0), 0) + " ms");
            vT2 = UIKit.KV(sec, "T2", F(tis.T2, 0) + " ms");
            vT2s = UIKit.KV(sec, "T2*", F(Math.Min(tis.T2s(b0), tis.T2), 0) + " ms");
            vTime = UIKit.KV(sec, "t"); vMz = UIKit.KV(sec, "Mz"); vMxy = UIKit.KV(sec, "|Mxy|");
            blochChart.XMax = (float)Math.Min(5000, Math.Max(500, tis.T1(b0) * 3));
            SetTip("90° darbe M'yi xy düzlemine yatırır. Spinler farklı hızlarda döndükçe dağılır (T2*). 180° darbe onları ters çevirir ve eko oluşur.",
                   "A 90° pulse tips M into the xy-plane. Spins fan out as they precess at different rates (T2*). A 180° pulse flips them and an echo forms.");
        }

        void Axis(Vector3 dir, Color c, string label)
        {
            var a = MeshObj("Axis", MeshGen.Arrow(0.006f, 0.02f, 0.12f), Res.Additive(new Color(c.r, c.g, c.b, 0.9f), 1.5f), holo);
            a.transform.localRotation = Quaternion.FromToRotation(Vector3.up, dir);
            a.transform.localScale = Vector3.one * HoloR * 1.25f;
            Label3D(holo.position + dir * HoloR * 1.35f, label);
        }

        void Pulse(double deg)
        {
            ens.Pulse(deg);
            vEvent.text = F(ens.TimeMs, 0) + " ms: " + F(deg, 0) + "° " + T("darbe", "pulse");
            if (Math.Abs(deg - 90) < 1) pulse90At = ens.TimeMs;
            if (Math.Abs(deg - 180) < 1 && pulse90At >= 0 && ens.TimeMs > pulse90At)
            {
                echoAt = ens.TimeMs + (ens.TimeMs - pulse90At);
                vEvent.text += T("  → eko beklenen zaman: ", "  → echo expected at: ") + F(echoAt, 0) + " ms";
            }
        }

        void SpinEcho()
        {
            ens.Reset(); trail.Clear(); ClearPlot();
            double tau = Math.Min(ens.T2Star * 3, 120);
            scheduled.Clear();
            scheduled.Add((0.01, 90));
            scheduled.Add((tau, 180));
        }

        void ClearPlot() { sMz.Pts.Clear(); sMxy.Pts.Clear(); sMx.Pts.Clear(); plotT = 0; echoAt = -1; pulse90At = -1; }

        void TickSpin(float dt)
        {
            double dtMs = simSpeed * dt;
            int sub = Mathf.Clamp(Mathf.CeilToInt((float)dtMs / 0.5f), 1, 200);
            for (int k = 0; k < sub; k++)
            {
                ens.Step(dtMs / sub);
                for (int s = scheduled.Count - 1; s >= 0; s--)
                    if (ens.TimeMs >= scheduled[s].t) { Pulse(scheduled[s].deg); scheduled.RemoveAt(s); }
            }
            if (labFrame) labPhase += dt * 220f;
            var lab = Quaternion.AngleAxis(labFrame ? labPhase : 0, Vector3.up);
            for (int i = 0; i < ens.M.Length; i++)
            {
                var m = lab * ToWorld(ens.M[i]);
                float len = m.magnitude;
                isoArrows[i].localRotation = len > 1e-4f ? Quaternion.FromToRotation(Vector3.up, m / len) : Quaternion.identity;
                isoArrows[i].localScale = Vector3.one * HoloR * Mathf.Max(0.02f, len);
            }
            var net = ens.Net();
            var nw = lab * ToWorld(net);
            float nl = nw.magnitude;
            netArrow.localRotation = nl > 1e-4f ? Quaternion.FromToRotation(Vector3.up, nw / nl) : Quaternion.identity;
            netArrow.localScale = Vector3.one * HoloR * Mathf.Max(0.02f, nl);
            if (prevTip != Vector3.zero) trail.Add(prevTip * HoloR, nw * HoloR, new Color(1f, 0.8f, 0.2f, 0.9f));
            prevTip = nw;
            trail.Fade(Mathf.Pow(0.15f, dt));
            trail.Apply();

            float mxy = new Vector2(net.x, net.y).magnitude;
            plotT += dt;
            if (plotT > 0.04f)
            {
                plotT = 0;
                float t = (float)(ens.TimeMs % blochChart.XMax);
                if (sMz.Pts.Count > 0 && t < sMz.Pts[sMz.Pts.Count - 1].x) { sMz.Pts.Clear(); sMxy.Pts.Clear(); sMx.Pts.Clear(); }
                sMz.Pts.Add(new Vector2(t, net.z)); sMxy.Pts.Add(new Vector2(t, mxy)); sMx.Pts.Add(new Vector2(t, net.x));
                blochChart.Refresh();
                vTime.text = F(ens.TimeMs, 0) + " ms"; vMz.text = F(net.z, 3); vMxy.text = F(mxy, 3);
            }
            if (echoAt > 0 && Math.Abs(ens.TimeMs - echoAt) < Math.Max(2, simSpeed * 0.05) && mxy > 0.3f)
            {
                CompleteTask("echo");
                vEvent.text = T("Spin eko! t = ", "Spin echo! t = ") + F(ens.TimeMs, 0) + " ms, |Mxy| = " + F(mxy, 2) + T(" (T2 kaybı: e^(−TE/T2))", " (T2 loss: e^(−TE/T2))");
                echoAt = -1;
            }
        }

        Vector3 prevTip;

        // ====================================================================== 2. CONTRAST
        static MriSeq seq = MriSeq.SpinEcho;
        static float tr = 500, te = 15, ti = 2500, flip = 30;
        static bool lesion = true;
        static float nex = 2;
        int[] brain;
        const int BN = 192;
        Texture2D imgTex;
        Chart curves;
        VisualElement sigTable;
        Label vErnst, vNull;
        UIKit.SliderRow sTr, sTe, sTi, sFlip;

        void BuildContrast()
        {
            brain = MriPhysics.BrainPhantom(BN, lesion);
            imgTex = UIKit.NewTex(BN, BN);
            // patient on the couch, head in the bore
            var ph = Phantom.BuildAdult(new PhantomOptions { Tumor = false });
            var view = PhantomView.Create(ph, Root.transform);
            float headZ = 87.5f;
            view.Orient(Vector3.zero, Vector3.forward, Vector3.up);
            var target = mriIso.position + new Vector3(0, -0.05f, 0);
            view.transform.position += target - view.PhysToWorld(new Vector3(0, 0.6f, headZ));
            mriCouch.position += new Vector3(0, 0, view.PhysToWorld(new Vector3(0, 0, 98)).z - (mriCouch.position.z - 0.02f));
            var screen = MeshObj("HoloImage", MeshGen.Quad(0.7f, 0.7f), Res.UnlitTex(imgTex, Color.white));
            screen.transform.position = new Vector3(0, 2.55f, -0.9f);
            screen.transform.rotation = Quaternion.Euler(-8, 0, 0);
            App.Rig.Frame(new Vector3(0, 1.75f, -1.0f), 4.0f, -28f, 10f, true);

            var Lp = LeftScroll.contentContainer;
            var sec = UIKit.Section(Lp, T("Sekans", "Sequence"));
            UIKit.Seg(sec, null, new[] { T("Spin eko", "Spin echo"), T("Gradyan eko", "Gradient echo"), T("İnversiyon geri kazanım", "Inversion recovery") }, (int)seq, i => { seq = (MriSeq)i; App.Open("mri"); });
            UIKit.Seg(sec, T("Hazır ayarlar", "Presets"), new[] { "T1W", "T2W", "PD", "FLAIR", "STIR" }, -1, Preset);
            sTr = UIKit.Slider(sec, "TR", 20, 12000, tr, v => F(v, 0) + " ms", v => { tr = v; Render(); }, 10);
            sTe = UIKit.Slider(sec, "TE", 2, 300, te, v => F(v, 0) + " ms", v => { te = v; Render(); }, 1);
            if (seq == MriSeq.InversionRecovery) sTi = UIKit.Slider(sec, "TI", 20, 4000, ti, v => F(v, 0) + " ms", v => { ti = v; Render(); }, 10);
            if (seq == MriSeq.GradientEcho) sFlip = UIKit.Slider(sec, T("Çevirme açısı α", "Flip angle α"), 2, 90, flip, v => F(v, 0) + "°", v => { flip = v; Render(); }, 1);
            UIKit.Slider(sec, T("Ortalama sayısı (NEX)", "Averages (NEX)"), 1, 8, nex, v => F(v, 0), v => { nex = v; Render(); }, 1);
            UIKit.Toggle(sec, T("Lezyon (ödem / tümör)", "Lesion (oedema / tumour)"), lesion, v => { lesion = v; brain = MriPhysics.BrainPhantom(BN, lesion); Render(); });

            var R = RightScroll.contentContainer;
            sec = UIKit.Section(R, T("Aksiyel beyin kesiti", "Axial brain slice"));
            var iv = new ImageView(App.SmallScreen ? 280 : 360, ""); iv.Set(imgTex); sec.Add(iv);
            sigTable = new VisualElement(); sec.Add(sigTable);
            sec = UIKit.Section(R, seq == MriSeq.InversionRecovery ? T("Mz geri kazanımı (TI)", "Mz recovery (TI)") : T("Sinyal – TE (T2 bozunumu)", "Signal vs TE (T2 decay)"));
            curves = new Chart(200) { XMin = 0, XMax = seq == MriSeq.InversionRecovery ? 5000 : 300, XLabel = "ms", YLabel = T("sinyal", "signal"), AutoY = false, YMin = 0, YMax = 1.05f };
            foreach (var k in new[] { MriPhysics.WM, MriPhysics.GM, MriPhysics.CSF, MriPhysics.FAT, MriPhysics.LESION })
                curves.Add(T(MriPhysics.Tissues[k].Tr, MriPhysics.Tissues[k].En), MriPhysics.Tissues[k].Col, 1.8f);
            sec.Add(curves.Legend()); sec.Add(curves);
            vErnst = UIKit.KV(sec, T("Ernst açısı (BC)", "Ernst angle (WM)"));
            vNull = UIKit.KV(sec, T("Bastırma TI'sı (BOS / yağ)", "Null TI (CSF / fat)"));
            Render();
            SetTip("Kısa TR + kısa TE → T1 ağırlıklı; uzun TR + uzun TE → T2 ağırlıklı; uzun TR + kısa TE → proton yoğunluğu.",
                   "Short TR + short TE → T1-weighted; long TR + long TE → T2-weighted; long TR + short TE → proton density.");
        }

        void Preset(int i)
        {
            if (i == 0) { seq = MriSeq.SpinEcho; tr = 500; te = 15; }
            else if (i == 1) { seq = MriSeq.SpinEcho; tr = 4000; te = 100; }
            else if (i == 2) { seq = MriSeq.SpinEcho; tr = 3000; te = 15; }
            else if (i == 3) { seq = MriSeq.InversionRecovery; tr = 9000; te = 120; ti = (float)MriPhysics.NullTi(MriPhysics.Tissues[MriPhysics.CSF].T1(b0), 9000); }
            else { seq = MriSeq.InversionRecovery; tr = 4000; te = 40; ti = (float)MriPhysics.NullTi(MriPhysics.Tissues[MriPhysics.FAT].T1(b0), 4000); }
            App.Open("mri");
        }

        void Render()
        {
            var img = MriPhysics.Image(brain, b0, seq, tr, te, ti, flip);
            float mx = 1e-6f; foreach (var v in img) mx = Mathf.Max(mx, v);
            // noise: SNR ~ B0 · √NEX
            float sigma = 0.025f / (b0 / 1.5f) / Mathf.Sqrt(nex) * mx;
            var rng = new System.Random(7);
            var px = new Color32[img.Length];
            for (int i = 0; i < img.Length; i++)
            {
                double n1 = Gauss(rng) * sigma, n2 = Gauss(rng) * sigma;
                float m = (float)Math.Sqrt((img[i] + n1) * (img[i] + n1) + n2 * n2);   // Rician magnitude
                byte b = (byte)Mathf.Clamp(m / mx * 255, 0, 255);
                px[i] = new Color32(b, b, b, 255);
            }
            imgTex.SetPixels32(px); imgTex.Apply();
            // signal table
            sigTable.Clear();
            double sWm = 0, sCsf = 0, sLes = 0, sGm = 0;
            foreach (var k in new[] { MriPhysics.WM, MriPhysics.GM, MriPhysics.CSF, MriPhysics.FAT, MriPhysics.LESION })
            {
                double s = MriPhysics.Signal(MriPhysics.Tissues[k], b0, seq, tr, te, ti, flip);
                UIKit.KV(sigTable, T(MriPhysics.Tissues[k].Tr, MriPhysics.Tissues[k].En) + "  (T1 " + F(MriPhysics.Tissues[k].T1(b0), 0) + ", T2 " + F(MriPhysics.Tissues[k].T2, 0) + ")", F(s, 3));
                if (k == MriPhysics.WM) sWm = s; if (k == MriPhysics.CSF) sCsf = s; if (k == MriPhysics.LESION) sLes = s; if (k == MriPhysics.GM) sGm = s;
            }
            if (seq == MriSeq.SpinEcho && sCsf > sWm * 1.3 && te >= 60) CompleteTask("t2w");
            if (seq == MriSeq.InversionRecovery && sCsf < 0.05 && sLes > sWm * 1.2) CompleteTask("flair");
            // curves
            for (int c = 0; c < curves.AllSeries.Count; c++)
            {
                int k = new[] { MriPhysics.WM, MriPhysics.GM, MriPhysics.CSF, MriPhysics.FAT, MriPhysics.LESION }[c];
                var ts = MriPhysics.Tissues[k];
                var s = curves.AllSeries[c]; s.Pts.Clear();
                if (seq == MriSeq.InversionRecovery)
                    for (float t = 0; t <= curves.XMax; t += 25) s.Pts.Add(new Vector2(t, (float)(ts.PD * Math.Abs(1 - 2 * Math.Exp(-t / ts.T1(b0)) + Math.Exp(-tr / ts.T1(b0))))));
                else
                {
                    double e1 = seq == MriSeq.GradientEcho ? Math.Sin(flip * Math.PI / 180) * (1 - Math.Exp(-tr / ts.T1(b0))) / (1 - Math.Cos(flip * Math.PI / 180) * Math.Exp(-tr / ts.T1(b0)))
                                                           : (1 - Math.Exp(-tr / ts.T1(b0)));
                    double t2 = seq == MriSeq.GradientEcho ? ts.T2s(b0) : ts.T2;
                    for (float t = 0; t <= curves.XMax; t += 2) s.Pts.Add(new Vector2(t, (float)(ts.PD * e1 * Math.Exp(-t / t2))));
                }
            }
            curves.VLines.Clear();
            curves.VLines.Add((seq == MriSeq.InversionRecovery ? ti : te, new Color(1, 1, 1, 0.6f), seq == MriSeq.InversionRecovery ? "TI" : "TE"));
            curves.Refresh();
            vErnst.text = F(MriPhysics.Ernst(tr, MriPhysics.Tissues[MriPhysics.WM].T1(b0)), 1) + "°";
            vNull.text = F(MriPhysics.NullTi(MriPhysics.Tissues[MriPhysics.CSF].T1(b0), tr), 0) + " / " + F(MriPhysics.NullTi(MriPhysics.Tissues[MriPhysics.FAT].T1(b0), tr), 0) + " ms";
        }

        static double Gauss(System.Random r)
        {
            double u1 = 1 - r.NextDouble(), u2 = r.NextDouble();
            return Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2);
        }

        // ====================================================================== 3. K-SPACE
        static int matIdx = 2, sampling;
        static bool motion;
        static readonly int[] Mats = { 32, 64, 128, 256 };
        Texture2D kTex, recTex, refTex;

        void BuildKspace()
        {
            const int N = 256;
            var map = MriPhysics.BrainPhantom(N, true);
            var obj = MriPhysics.Image(map, b0, MriSeq.SpinEcho, 4000, 100, 0, 90);   // T2-weighted object
            kTex = UIKit.NewTex(N, N); recTex = UIKit.NewTex(N, N); refTex = UIKit.NewTex(N, N);
            float mx = 0; foreach (var v in obj) mx = Mathf.Max(mx, v);
            var o = new float[obj.Length]; for (int i = 0; i < o.Length; i++) o[i] = obj[i] / mx;
            UIKit.Gray(refTex, o, N, N);
            var screen1 = MeshObj("HoloK", MeshGen.Quad(0.6f, 0.6f), Res.UnlitTex(kTex, Color.white));
            screen1.transform.position = new Vector3(-0.38f, 2.55f, -0.9f); screen1.transform.rotation = Quaternion.Euler(-8, 0, 0);
            var screen2 = MeshObj("HoloImg", MeshGen.Quad(0.6f, 0.6f), Res.UnlitTex(recTex, Color.white));
            screen2.transform.position = new Vector3(0.38f, 2.55f, -0.9f); screen2.transform.rotation = Quaternion.Euler(-8, 0, 0);
            App.Rig.Frame(new Vector3(0, 2.0f, -0.9f), 3.6f, -15f, 6f, true);

            var Lp = LeftScroll.contentContainer;
            var sec = UIKit.Section(Lp, T("Örnekleme", "Sampling"));
            UIKit.Seg(sec, T("Matris (çözünürlük)", "Matrix (resolution)"), new[] { "32", "64", "128", "256" }, matIdx, i => { matIdx = i; Kspace(o); });
            UIKit.Seg(sec, T("k-uzayı örnekleme", "k-space sampling"), new[] { T("Tam", "Full"), T("Sadece merkez", "Centre only"), T("Sadece çevre", "Periphery only"), "R = 2", "R = 3", T("Kısmi Fourier %60", "Partial Fourier 60 %") }, sampling, i => { sampling = i; Kspace(o); });
            UIKit.Toggle(sec, T("Hasta hareketi (faz hataları)", "Patient motion (phase errors)"), motion, v => { motion = v; Kspace(o); });
            UIKit.Note(sec, T("R = hızlandırma faktörü: her R. satır alınır → FOV/R kadar katlanma (aliasing). Paralel görüntüleme (SENSE/GRAPPA) bunu bobin duyarlılıklarıyla düzeltir.",
                              "R = acceleration factor: every R-th line is acquired → wrap-around by FOV/R (aliasing). Parallel imaging (SENSE/GRAPPA) unfolds it with coil sensitivities."));
            var R = RightScroll.contentContainer;
            sec = UIKit.Section(R, T("k-uzayı (log |S(kx, ky)|)", "k-space (log |S(kx, ky)|)"));
            var kv = new ImageView(App.SmallScreen ? 230 : 290, ""); kv.Set(kTex); sec.Add(kv);
            sec = UIKit.Section(R, T("Rekonstrüksiyon (ters 2B FFT)", "Reconstruction (inverse 2D FFT)"));
            var rv = new ImageView(App.SmallScreen ? 230 : 290, ""); rv.Set(recTex); sec.Add(rv);
            Kspace(o);
            SetTip("k-uzayının merkezi görüntünün kontrastını, çevresi ise ince detayları (kenarları) taşır.",
                   "The centre of k-space carries image contrast; the periphery carries fine detail (edges).");
        }

        void Kspace(float[] obj)
        {
            const int N = 256;
            var re = new double[N * N]; var im = new double[N * N];
            for (int i = 0; i < obj.Length; i++) re[i] = obj[i];
            Fft.Transform2D(re, im, N, -1);
            int m = Mats[matIdx];
            var rng = new System.Random(11);
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    // centred frequency indices
                    int kx = x < N / 2 ? x : x - N, ky = y < N / 2 ? y : y - N;
                    bool keep = Math.Abs(kx) < m / 2 && Math.Abs(ky) < m / 2;
                    double r = Math.Sqrt(kx * kx + ky * ky);
                    if (sampling == 1) keep &= r < 12;
                    if (sampling == 2) keep &= r >= 12;
                    if (sampling == 3) keep &= ((ky % 2) + 2) % 2 == 0;
                    if (sampling == 4) keep &= ((ky % 3) + 3) % 3 == 0;
                    if (sampling == 5) keep &= ky > -(int)(0.1 * m);
                    int k = y * N + x;
                    if (!keep) { re[k] = 0; im[k] = 0; continue; }
                }
            if (motion)
            {
                for (int y = 0; y < N; y++)
                {
                    if (rng.NextDouble() > 0.12) continue;
                    double ph = (rng.NextDouble() - 0.5) * 2.5;   // random phase per phase-encode line
                    double c = Math.Cos(ph), s = Math.Sin(ph);
                    for (int x = 0; x < N; x++)
                    {
                        int k = y * N + x;
                        double a = re[k], b = im[k];
                        re[k] = a * c - b * s; im[k] = a * s + b * c;
                    }
                }
            }
            // k-space display (shifted so DC is in the centre)
            var kpx = new Color32[N * N];
            double kmax = 0;
            for (int i = 0; i < N * N; i++) kmax = Math.Max(kmax, Math.Log(1 + Math.Sqrt(re[i] * re[i] + im[i] * im[i])));
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    int sx = (x + N / 2) % N, sy = (y + N / 2) % N;
                    double v = Math.Log(1 + Math.Sqrt(re[sy * N + sx] * re[sy * N + sx] + im[sy * N + sx] * im[sy * N + sx])) / Math.Max(kmax, 1e-9);
                    var col = ColorMaps.Hot((float)v);
                    kpx[y * N + x] = col;
                }
            kTex.SetPixels32(kpx); kTex.Apply();
            Fft.Transform2D(re, im, N, 1);
            var img = new float[N * N];
            float mx = 1e-9f;
            for (int i = 0; i < img.Length; i++) { img[i] = (float)Math.Sqrt(re[i] * re[i] + im[i] * im[i]); mx = Mathf.Max(mx, img[i]); }
            for (int i = 0; i < img.Length; i++) img[i] /= mx;
            UIKit.Gray(recTex, img, N, N);
            if (sampling == 1) CompleteTask("kspace");
        }

        public override void AutoAction(string action)
        {
            if (action == "echo") SpinEcho();
            if (action == "contrast") { tab = 1; App.Open("mri"); }
            if (action == "kspace") { tab = 2; App.Open("mri"); }
            if (action == "spin") { tab = 0; App.Open("mri"); }
        }

        // ====================================================================== theory
        protected override void BuildLearn(VisualElement c)
        {
            UIKit.H(c, T("1. Spin ve presesyon", "1. Spin and precession"));
            UIKit.P(c, T("Hidrojen çekirdekleri (protonlar) bir manyetik moment taşır. B₀ alanında çok küçük bir çoğunluk alanla aynı yöne dizilir ve net mıknatıslanma M₀ oluşur (M₀ ~ B₀). Spinler B₀ etrafında Larmor frekansıyla döner (presesyon).",
                             "Hydrogen nuclei (protons) carry a magnetic moment. In the field B₀ a tiny excess aligns with the field, creating a net magnetisation M₀ (M₀ ~ B₀). Spins precess about B₀ at the Larmor frequency."));
            UIKit.Eq(c, "ω₀ = γ·B₀        f₀ = γ̄·B₀ = 42.58 MHz/T × B₀   (1.5 T → 63.9 MHz, 3 T → 127.7 MHz)");
            UIKit.H(c, T("2. Bloch denklemleri ve gevşeme", "2. Bloch equations and relaxation"));
            UIKit.Eq(c, "dM/dt = γ M × B − (Mx x̂ + My ŷ)/T2 − (Mz − M₀) ẑ/T1");
            UIKit.P(c, T("RF darbesi (B₁, Larmor frekansında) M'yi α açısıyla devirir. Sonra:\n• <b>T1 (spin–örgü)</b>: Mz yeniden M₀'a döner: Mz(t) = M₀ + (Mz(0) − M₀)·e^(−t/T1)\n• <b>T2 (spin–spin)</b>: enine mıknatıslanma faz uyumunu kaybeder: Mxy(t) = Mxy(0)·e^(−t/T2)\n• <b>T2*</b>: alan homojensizliği ek dağılım yaratır (1/T2* = 1/T2 + γΔB) — 180° darbe bu kısmı geri alır: <b>spin eko</b>.",
                             "An RF pulse (B₁ at the Larmor frequency) tips M by the flip angle α. Then:\n• <b>T1 (spin–lattice)</b>: Mz recovers: Mz(t) = M₀ + (Mz(0) − M₀)·e^(−t/T1)\n• <b>T2 (spin–spin)</b>: transverse magnetisation dephases: Mxy(t) = Mxy(0)·e^(−t/T2)\n• <b>T2*</b>: field inhomogeneity adds dephasing (1/T2* = 1/T2 + γΔB) — a 180° pulse reverses this part: the <b>spin echo</b>."));
            UIKit.H(c, T("3. Kontrast", "3. Contrast"));
            UIKit.Eq(c, "SE:  S = PD·(1 − e^(−TR/T1))·e^(−TE/T2)        IR:  S = PD·|1 − 2e^(−TI/T1) + e^(−TR/T1)|·e^(−TE/T2)");
            UIKit.Eq(c, T("GRE:  S = PD·sinα·(1 − E₁)/(1 − cosα·E₁)·e^(−TE/T2*)      Ernst açısı: cos α_E = e^(−TR/T1)",
                          "GRE:  S = PD·sinα·(1 − E₁)/(1 − cosα·E₁)·e^(−TE/T2*)      Ernst angle: cos α_E = e^(−TR/T1)"));
            UIKit.P(c, T("FLAIR: TI = T1·ln2 seçilerek BOS sinyali sıfırlanır (1.5 T'de ≈ 2500 ms). STIR: kısa TI ile yağ bastırılır (≈ 150–180 ms).",
                             "FLAIR: choosing TI = T1·ln2 nulls CSF (≈ 2500 ms at 1.5 T). STIR: a short TI suppresses fat (≈ 150–180 ms)."));
            UIKit.H(c, T("4. k-uzayı", "4. k-space"));
            UIKit.P(c, T("Gradyanlar konumu frekans ve faza kodlar; ölçülen sinyal görüntünün 2B Fourier dönüşümüdür (k-uzayı). Görüntü ters FFT ile elde edilir. Matris boyutu çözünürlüğü, FOV piksel boyutunu belirler; az örnekleme katlanmaya, hareket ise faz kodlama yönünde hayaletlere yol açar.",
                             "Gradients encode position into frequency and phase; the acquired signal is the 2D Fourier transform of the image (k-space). The image follows from an inverse FFT. Matrix size sets resolution, FOV sets pixel size; undersampling causes wrap-around, motion causes ghosts along phase encoding."));
            UIKit.Eq(c, "S(kx, ky) = ∫∫ ρ(x, y)·e^(−i2π(kx·x + ky·y)) dx dy        k = γ̄ ∫ G(t) dt");
        }
    }
}
