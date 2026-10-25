using System.Text;
using System.Threading;
using UnityEditor;
using UnityEngine;
using MedPhys.Cases;
using MedPhys.Physics;

// Calibration of the case-mode scoring: runs the case simulations over a grid of settings (editor, via the bridge).
public static class MedPhysCaseTest
{
    static void Wait(CaseSim s, Vals v) { int n = 0; while (!s.Poll(v) && n++ < 6000) Thread.Sleep(5); }

    public static string Calibrate()
    {
        var sb = new StringBuilder();
        // Emir: paediatric chest
        var ph = Phantom.BuildPatient(new PhantomOptions { Tumor = true, TumorCenter = new Vector3(-8.0f, 1.5f, 45f), TumorRadius = 3.0f }, 0.62f, 0.68f, 0.73f);
        ph.Diameters(52.5f * 0.73f, out float lat, out float ap);
        sb.AppendLine($"EMIR chest lat={lat:F1} ap={ap:F1}");
        var xs = new XRayCaseSim(ph, 52.5f * 0.73f, 0.24f);
        foreach (int filt in new[] { 0, 1 })
            foreach (bool grid in new[] { true, false })
                foreach (float kvp in new[] { 60f, 70f, 85f, 100f, 115f })
                    foreach (float mas in new[] { 0.8f, 1.6f, 3.2f })
                    {
                        var v = new Vals();
                        v.V["kvp"] = kvp; v.V["mas"] = mas; v.V["filt"] = filt; v.V["grid"] = grid ? 1 : 0; v.V["fw"] = 26; v.V["fh"] = 30;
                        xs.Start(v); Wait(xs, v);
                        sb.AppendLine($"  filt={filt} grid={(grid ? 1 : 0)} kvp={kvp,3} mas={mas,4:F1}  DI={v.S("di"),6:F2} ESAK={v.S("esak"),7:F1}uGy E={v.S("E") * 1000,6:F2}uSv SNR={v.S("snr"),5:F0}");
                    }
        // Elif: pelvis, bladder ROI
        var pe = Phantom.BuildPatient(new PhantomOptions { Tumor = false }, 0.92f, 0.92f, 0.94f, 0.8f);
        var cs = new CTCaseSim(pe, 7.5f * 0.94f, "bladder");
        sb.AppendLine($"ELIF Deff={cs.DEff:F1} lat={cs.Lat:F1} ap={cs.Ap:F1} f={CasePhysics.SsdeFactor(cs.DEff):F2}");
        foreach (int kv in new[] { 0, 1, 2 })
            foreach (float mas in new[] { 40f, 80f, 150f, 250f })
            {
                var v = new Vals(); v.V["kv"] = kv; v.V["mas"] = mas; v.V["pitch"] = 1;
                cs.Start(v); Wait(cs, v);
                sb.AppendLine($"  kv={CTCaseSim.Kvps[kv]} mas={mas,4}  noise={v.S("noise"),6:F1}HU mean={v.S("mean"),6:F1} ctdi={v.S("ctdi"),5:F1} ssde={v.S("ssde"),5:F1}");
            }
        // Mehmet: liver ROI, obese
        var pm = Phantom.BuildPatient(new PhantomOptions { Tumor = false }, 1.04f, 1.02f, 1.0f, 6.5f);
        var cm = new CTCaseSim(pm, 30f, "liver", 62f);
        sb.AppendLine($"MEHMET Deff={cm.DEff:F1} lat={cm.Lat:F1} ap={cm.Ap:F1} f={CasePhysics.SsdeFactor(cm.DEff):F2}");
        foreach (int kv in new[] { 0, 2, 3 })
            foreach (float mas in new[] { 150f, 300f, 450f, 650f })
            {
                var v = new Vals(); v.V["kv"] = kv; v.V["mas"] = mas;
                cm.Start(v); Wait(cm, v);
                sb.AppendLine($"  kv={CTCaseSim.Kvps[kv]} mas={mas,4}  noise={v.S("noise"),6:F1}HU mean={v.S("mean"),6:F1} ctdi={v.S("ctdi"),5:F1} ssde={v.S("ssde"),5:F1}");
            }
        // standard adult for reference
        var pa = Phantom.BuildAdult(new PhantomOptions { Tumor = false });
        var ca = new CTCaseSim(pa, 30f, "liver");
        foreach (float mas in new[] { 150f, 250f })
        {
            var v = new Vals(); v.V["kv"] = 2; v.V["mas"] = mas;
            ca.Start(v); Wait(ca, v);
            sb.AppendLine($"ADULT Deff={ca.DEff:F1} kv=120 mas={mas} noise={v.S("noise"):F1} ssde={v.S("ssde"):F1}");
        }
        MedPhysTools.Report("case_calib.txt", sb.ToString());
        return "ok";
    }

    public static string Ray()
    {
        var sb = new StringBuilder();
        foreach (var (name, ph, z) in new[] {
            ("adult", Phantom.BuildAdult(new PhantomOptions { Tumor = false }), 52.5f),
            ("child", Phantom.BuildPatient(new PhantomOptions { Tumor = false }, 0.62f, 0.68f, 0.73f), 52.5f * 0.73f) })
        {
            ph.Diameters(z, out float lat, out float ap);
            float detY = ap * 0.5f + 3f;
            var tr = new PhantomTracer(ph);
            foreach (float x in new[] { 0f, 4f, 6f })
            {
                var len = new float[(int)TissueId.Count];
                var src = new Vector3(x, detY - 180, z); var det = new Vector3(x, detY, z);
                var d = det - src; float dist = d.magnitude; d /= dist;
                float th = tr.Trace(src, d, dist, len);
                sb.Append($"{name} x={x} th={th:F1}: ");
                for (int t = 0; t < len.Length; t++) if (len[t] > 0.01f) sb.Append($"{(TissueId)t}={len[t]:F2} ");
                sb.AppendLine();
            }
            var xs = new XRayCaseSim(ph, z, 0.12f);
            var v = new Vals(); v.V["kvp"] = 120; v.V["mas"] = 2; v.V["filt"] = 0; v.V["grid"] = 1; v.V["fw"] = 35; v.V["fh"] = 43;
            xs.Start(v); Wait(xs, v);
            sb.AppendLine($"  {name} 120kVp 2mAs grid: DI={v.S("di"):F2} ESAK={v.S("esak"):F1} thick={v.S("thick"):F1}");
        }
        return sb.ToString();
    }

    public static string Reimport()
    {
        foreach (var n in new[] { "Child", "Woman", "Obese", "OldMan", "OldWoman", "Lady", "Man" })
            AssetDatabase.ImportAsset("Assets/Resources/Models/Patient_" + n + ".fbx", ImportAssetOptions.ForceUpdate);
        return Patients();
    }

    public static string Patients()
    {
        var sb = new StringBuilder();
        foreach (var n in new[] { "Child", "Woman", "Obese", "OldMan", "OldWoman", "Lady", "Man" })
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Models/Patient_" + n + ".fbx");
            if (go == null) { sb.AppendLine(n + ": MISSING"); continue; }
            var smr = go.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var mats = new System.Collections.Generic.HashSet<string>();
            foreach (var r in go.GetComponentsInChildren<Renderer>(true)) foreach (var m in r.sharedMaterials) if (m) mats.Add(m.name);
            sb.AppendLine(n + ": skinned=" + smr.Length + " bones=" + (smr.Length > 0 ? smr[0].bones.Length : 0) + " mats=" + string.Join(",", mats));
        }
        return sb.ToString();
    }
}
