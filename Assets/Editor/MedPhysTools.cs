using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class MedPhysTools
{
    public static string BridgeDir => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Bridge"));

    public static void Report(string file, string text)
    {
        Directory.CreateDirectory(BridgeDir);
        File.WriteAllText(Path.Combine(BridgeDir, file), text);
        Debug.Log("[MP] wrote " + file);
    }

    // Prints hierarchy + world-space renderer bounds for each model, to verify axes/scale/pivots.
    public static void ReportModels()
    {
        var sb = new StringBuilder();
        foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { "Assets/Resources/Models" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (go == null) continue;
            sb.AppendLine("=== " + path);
            var inst = Object.Instantiate(go);
            Dump(inst.transform, 0, sb);
            Object.DestroyImmediate(inst);
        }
        Report("models_report.txt", sb.ToString());
    }

    static void Dump(Transform t, int depth, StringBuilder sb)
    {
        string pad = new string(' ', depth * 2);
        string info = "";
        var mf = t.GetComponent<MeshFilter>();
        var mr = t.GetComponent<MeshRenderer>();
        if (mr != null && mf != null && mf.sharedMesh != null)
        {
            var b = mr.bounds;
            var lb = mf.sharedMesh.bounds;
            info = $" world c={b.center.ToString("F2")} s={b.size.ToString("F2")} mesh c={lb.center.ToString("F2")} s={lb.size.ToString("F2")} mats=";
            foreach (var m in mr.sharedMaterials) info += (m ? m.name + "(" + m.shader.name + ")" : "null") + ",";
        }
        sb.AppendLine($"{pad}{t.name} wp={t.position.ToString("F3")} lp={t.localPosition.ToString("F3")} lr={t.localEulerAngles.ToString("F1")} ls={t.localScale.ToString("F2")}{info}");
        foreach (Transform c in t) Dump(c, depth + 1, sb);
    }
}

public static class MedPhysProbe
{
    public static void Normals()
    {
        var sb = new System.Text.StringBuilder();
        foreach (var name in new[] { "Room", "CT_Scanner" })
        {
            var go = Object.Instantiate(Resources.Load<GameObject>("Models/" + name));
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
            {
                if (!(mf.name == "Room_Floor" || mf.name == "Room_WallN" || mf.name == "CT_Base" || mf.name == "CT_TopLED")) continue;
                var m = mf.sharedMesh;
                var v = m.vertices; var n = m.normals; var t = m.triangles;
                sb.AppendLine($"{mf.name} verts={v.Length} normals={n.Length} tris={t.Length / 3} readable={m.isReadable}");
                for (int i = 0; i < Mathf.Min(t.Length, 36); i += 3)
                {
                    var wa = mf.transform.TransformPoint(v[t[i]]); var wb = mf.transform.TransformPoint(v[t[i + 1]]); var wc = mf.transform.TransformPoint(v[t[i + 2]]);
                    var fn = Vector3.Cross(wb - wa, wc - wa).normalized;
                    var nn = mf.transform.TransformDirection(n[t[i]]).normalized;
                    sb.AppendLine($"  tri c={((wa + wb + wc) / 3).ToString("F2")} faceN={fn.ToString("F2")} vertN={nn.ToString("F2")}");
                }
            }
            Object.DestroyImmediate(go);
        }
        MedPhysTools.Report("normals.txt", sb.ToString());
    }
}

public static class MedPhysWorker
{
    public static string Reimport()
    {
        AssetDatabase.ImportAsset("Assets/Resources/Models/Worker.fbx", ImportAssetOptions.ForceUpdate);
        var go = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Models/Worker.fbx");
        if (go == null) return "worker not found";
        var smr = go.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        var names = new System.Text.StringBuilder();
        foreach (var t in go.GetComponentsInChildren<Transform>(true)) names.Append(t.name).Append(' ');
        return "skinned=" + smr.Length + " bones=" + (smr.Length > 0 ? smr[0].bones.Length : 0) + " :: " + names;
    }
}
