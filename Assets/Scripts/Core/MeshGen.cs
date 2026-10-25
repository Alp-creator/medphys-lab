using System.Collections.Generic;
using UnityEngine;

namespace MedPhys.Core
{
    /// <summary>Procedural meshes (phantom primitives, beams, arrows, lines).</summary>
    public static class MeshGen
    {
        // ------------------------------------------------------------------ phantom primitives (baked in "phys" coordinates)
        public static Mesh Ellipsoid(Vector3 c, Vector3 r, Quaternion rot, int lat = 22, int lon = 36, bool clip = false, Vector3 clipN = default, float clipD = 0)
        {
            var v = new List<Vector3>(); var n = new List<Vector3>(); var tri = new List<int>();
            for (int i = 0; i <= lat; i++)
            {
                float th = Mathf.PI * i / lat;
                for (int j = 0; j <= lon; j++)
                {
                    float ph = 2 * Mathf.PI * j / lon;
                    var u = new Vector3(Mathf.Sin(th) * Mathf.Cos(ph), Mathf.Sin(th) * Mathf.Sin(ph), Mathf.Cos(th));
                    var p = rot * Vector3.Scale(u, r) + c;
                    var nn = (rot * new Vector3(u.x / r.x, u.y / r.y, u.z / r.z)).normalized;
                    if (clip)
                    {
                        float dd = Vector3.Dot(clipN, p) - clipD;
                        if (dd > 0) { p -= clipN * dd; nn = clipN; }
                    }
                    v.Add(p); n.Add(nn);
                }
            }
            for (int i = 0; i < lat; i++)
                for (int j = 0; j < lon; j++)
                {
                    int a = i * (lon + 1) + j, b = a + lon + 1;
                    tri.Add(a); tri.Add(b); tri.Add(a + 1);
                    tri.Add(a + 1); tri.Add(b); tri.Add(b + 1);
                }
            var mesh = Build(v, n, tri);
            FixWinding(mesh);
            return mesh;
        }

        /// <summary>Elliptic cylinder along local z (half length r.z); inner &gt; 0 makes a tube.</summary>
        public static Mesh Cylinder(Vector3 c, Vector3 r, Quaternion rot, int seg = 40, float inner = 0, bool clip = false, Vector3 clipN = default, float clipD = 0)
        {
            var v = new List<Vector3>(); var n = new List<Vector3>(); var tri = new List<int>();
            void Wall(float s, bool outward)
            {
                int start = v.Count;
                for (int j = 0; j <= seg; j++)
                {
                    float ph = 2 * Mathf.PI * j / seg;
                    float cx = Mathf.Cos(ph), sy = Mathf.Sin(ph);
                    var nl = new Vector3(cx / r.x, sy / r.y, 0).normalized * (outward ? 1 : -1);
                    for (int k = 0; k < 2; k++)
                    {
                        var p = rot * new Vector3(cx * r.x * s, sy * r.y * s, k == 0 ? -r.z : r.z) + c;
                        v.Add(p); n.Add(rot * nl);
                    }
                }
                for (int j = 0; j < seg; j++)
                {
                    int a = start + j * 2;
                    if (outward) { tri.Add(a); tri.Add(a + 2); tri.Add(a + 1); tri.Add(a + 1); tri.Add(a + 2); tri.Add(a + 3); }
                    else { tri.Add(a); tri.Add(a + 1); tri.Add(a + 2); tri.Add(a + 1); tri.Add(a + 3); tri.Add(a + 2); }
                }
            }
            Wall(1f, true);
            if (inner > 0) Wall(inner, false);
            for (int k = 0; k < 2; k++)
            {
                float z = k == 0 ? -r.z : r.z;
                var nn = rot * new Vector3(0, 0, k == 0 ? -1 : 1);
                int start = v.Count;
                if (inner > 0)
                {
                    for (int j = 0; j <= seg; j++)
                    {
                        float ph = 2 * Mathf.PI * j / seg;
                        v.Add(rot * new Vector3(Mathf.Cos(ph) * r.x, Mathf.Sin(ph) * r.y, z) + c); n.Add(nn);
                        v.Add(rot * new Vector3(Mathf.Cos(ph) * r.x * inner, Mathf.Sin(ph) * r.y * inner, z) + c); n.Add(nn);
                    }
                    for (int j = 0; j < seg; j++)
                    {
                        int a = start + j * 2;
                        if (k == 1) { tri.Add(a); tri.Add(a + 2); tri.Add(a + 1); tri.Add(a + 1); tri.Add(a + 2); tri.Add(a + 3); }
                        else { tri.Add(a); tri.Add(a + 1); tri.Add(a + 2); tri.Add(a + 1); tri.Add(a + 3); tri.Add(a + 2); }
                    }
                }
                else
                {
                    v.Add(rot * new Vector3(0, 0, z) + c); n.Add(nn);
                    for (int j = 0; j <= seg; j++)
                    {
                        float ph = 2 * Mathf.PI * j / seg;
                        v.Add(rot * new Vector3(Mathf.Cos(ph) * r.x, Mathf.Sin(ph) * r.y, z) + c); n.Add(nn);
                    }
                    for (int j = 0; j < seg; j++)
                    {
                        if (k == 1) { tri.Add(start); tri.Add(start + j + 1); tri.Add(start + j + 2); }
                        else { tri.Add(start); tri.Add(start + j + 2); tri.Add(start + j + 1); }
                    }
                }
            }
            if (clip)
            {
                for (int i = 0; i < v.Count; i++)
                {
                    float dd = Vector3.Dot(clipN, v[i]) - clipD;
                    if (dd > 0) { v[i] -= clipN * dd; }
                }
            }
            var mesh = Build(v, n, tri);
            FixWinding(mesh);
            return mesh;
        }

        public static Mesh Box(Vector3 c, Vector3 r, Quaternion rot)
        {
            var v = new List<Vector3>(); var n = new List<Vector3>(); var tri = new List<int>();
            Vector3[] dirs = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
            foreach (var d in dirs)
            {
                Vector3 a = d.x != 0 ? Vector3.up : Vector3.right;
                Vector3 b = Vector3.Cross(d, a);
                int s = v.Count;
                for (int k = 0; k < 4; k++)
                {
                    float su = (k == 0 || k == 3) ? -1 : 1, sv = (k < 2) ? -1 : 1;
                    var local = Vector3.Scale(d + a * su + b * sv, r);
                    v.Add(rot * local + c); n.Add(rot * d);
                }
                tri.Add(s); tri.Add(s + 2); tri.Add(s + 1); tri.Add(s); tri.Add(s + 3); tri.Add(s + 2);
            }
            var m = Build(v, n, tri);
            FixWinding(m);
            return m;
        }

        // makes triangle winding agree with the stored normals (outward facing)
        public static void FixWinding(Mesh m)
        {
            var v = m.vertices; var n = m.normals; var t = m.triangles;
            for (int i = 0; i < t.Length; i += 3)
            {
                var fn = Vector3.Cross(v[t[i + 1]] - v[t[i]], v[t[i + 2]] - v[t[i]]);
                if (Vector3.Dot(fn, n[t[i]]) < 0) { int tmp = t[i + 1]; t[i + 1] = t[i + 2]; t[i + 2] = tmp; }
            }
            m.triangles = t;
        }

        static Mesh Build(List<Vector3> v, List<Vector3> n, List<int> tri)
        {
            var m = new Mesh();
            if (v.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.SetVertices(v); m.SetNormals(n); m.SetTriangles(tri, 0);
            m.RecalculateBounds();
            return m;
        }

        // ------------------------------------------------------------------ generic shapes (Unity space)
        /// <summary>Pyramid frustum from an apex to a quad (beam): side faces only, vertex alpha fades from apex.</summary>
        public static Mesh Beam(Vector3 apex, Vector3[] quad, float apexAlpha = 0.25f, float baseAlpha = 1f)
        {
            var v = new List<Vector3>(); var c = new List<Color>(); var tri = new List<int>(); var n = new List<Vector3>(); var uv = new List<Vector2>();
            for (int i = 0; i < 4; i++)
            {
                var a = quad[i]; var b = quad[(i + 1) % 4];
                int s = v.Count;
                v.Add(apex); v.Add(a); v.Add(b);
                var fn = Vector3.Cross(a - apex, b - apex).normalized;
                n.Add(fn); n.Add(fn); n.Add(fn);
                c.Add(new Color(1, 1, 1, apexAlpha)); c.Add(new Color(1, 1, 1, baseAlpha)); c.Add(new Color(1, 1, 1, baseAlpha));
                uv.Add(new Vector2(0.5f, 0)); uv.Add(new Vector2(0, 1)); uv.Add(new Vector2(1, 1));
                tri.Add(s); tri.Add(s + 1); tri.Add(s + 2);
            }
            var m = new Mesh();
            m.SetVertices(v); m.SetNormals(n); m.SetColors(c); m.SetUVs(0, uv); m.SetTriangles(tri, 0);
            m.RecalculateBounds();
            return m;
        }

        /// <summary>Cone along +z from origin, length h, radius r at the end (beam / arrow head).</summary>
        public static Mesh Cone(float r, float h, int seg = 24, float r0 = 0f, float alpha0 = 1f, float alpha1 = 1f)
        {
            var v = new List<Vector3>(); var n = new List<Vector3>(); var c = new List<Color>(); var tri = new List<int>();
            for (int j = 0; j <= seg; j++)
            {
                float ph = 2 * Mathf.PI * j / seg;
                var dir = new Vector3(Mathf.Cos(ph), Mathf.Sin(ph), 0);
                v.Add(dir * r0); v.Add(dir * r + new Vector3(0, 0, h));
                var nn = (dir * h + new Vector3(0, 0, -(r - r0))).normalized;
                n.Add(nn); n.Add(nn);
                c.Add(new Color(1, 1, 1, alpha0)); c.Add(new Color(1, 1, 1, alpha1));
            }
            for (int j = 0; j < seg; j++)
            {
                int a = j * 2;
                tri.Add(a); tri.Add(a + 1); tri.Add(a + 2);
                tri.Add(a + 2); tri.Add(a + 1); tri.Add(a + 3);
            }
            var m = new Mesh();
            m.SetVertices(v); m.SetNormals(n); m.SetColors(c); m.SetTriangles(tri, 0);
            m.RecalculateBounds();
            return m;
        }

        /// <summary>Arrow along +y: shaft radius rs, head radius rh, total length 1.</summary>
        public static Mesh Arrow(float rs = 0.02f, float rh = 0.06f, float headLen = 0.18f, int seg = 20)
        {
            var parts = new List<CombineInstance>();
            var shaft = Cylinder(new Vector3(0, 0, (1 - headLen) * 0.5f), new Vector3(rs, rs, (1 - headLen) * 0.5f), Quaternion.identity, seg);
            var head = Cone(rh, headLen, seg);
            parts.Add(new CombineInstance { mesh = shaft, transform = Matrix4x4.Rotate(Quaternion.Euler(-90, 0, 0)) });
            parts.Add(new CombineInstance { mesh = head, transform = Matrix4x4.TRS(new Vector3(0, 1 - headLen, 0), Quaternion.Euler(-90, 0, 0), Vector3.one) });
            var m = new Mesh();
            m.CombineMeshes(parts.ToArray(), true, true);
            m.RecalculateNormals();
            return m;
        }

        public static Mesh Quad(float w, float h)
        {
            var m = new Mesh();
            m.vertices = new[] { new Vector3(-w / 2, -h / 2, 0), new Vector3(w / 2, -h / 2, 0), new Vector3(w / 2, h / 2, 0), new Vector3(-w / 2, h / 2, 0) };
            m.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            m.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
            m.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            m.RecalculateBounds();
            return m;
        }

        /// <summary>A unit circle line loop (radius 1) in the xy plane.</summary>
        public static Mesh Ring(float r, float width, int seg = 64)
        {
            var v = new List<Vector3>(); var tri = new List<int>(); var c = new List<Color>();
            for (int j = 0; j <= seg; j++)
            {
                float ph = 2 * Mathf.PI * j / seg;
                var d = new Vector3(Mathf.Cos(ph), Mathf.Sin(ph), 0);
                v.Add(d * (r - width / 2)); v.Add(d * (r + width / 2));
                c.Add(Color.white); c.Add(Color.white);
            }
            for (int j = 0; j < seg; j++)
            {
                int a = j * 2;
                tri.Add(a); tri.Add(a + 1); tri.Add(a + 2); tri.Add(a + 2); tri.Add(a + 1); tri.Add(a + 3);
            }
            var m = new Mesh();
            m.SetVertices(v); m.SetColors(c); m.SetTriangles(tri, 0);
            m.RecalculateBounds();
            return m;
        }
    }

    /// <summary>Dynamic line-segment mesh (particle tracks, LORs) with per-vertex colours.</summary>
    public sealed class LineBatch
    {
        public readonly Mesh Mesh = new Mesh();
        readonly List<Vector3> v = new List<Vector3>();
        readonly List<Color> c = new List<Color>();
        readonly List<int> idx = new List<int>();
        public int Count => v.Count / 2;

        public LineBatch() { Mesh.MarkDynamic(); Mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32; }

        public void Clear() { v.Clear(); c.Clear(); idx.Clear(); }

        public void Add(Vector3 a, Vector3 b, Color ca, Color cb)
        {
            idx.Add(v.Count); v.Add(a); c.Add(ca);
            idx.Add(v.Count); v.Add(b); c.Add(cb);
        }

        public void Add(Vector3 a, Vector3 b, Color col) => Add(a, b, col, col);

        /// <summary>Multiplies every vertex colour alpha by f and drops segments that became invisible (fading trails).</summary>
        public void Fade(float f, float minAlpha = 0.02f)
        {
            int w = 0;
            for (int i = 0; i < v.Count; i += 2)
            {
                var a = c[i]; a.a *= f; var b = c[i + 1]; b.a *= f;
                if (a.a < minAlpha && b.a < minAlpha) continue;
                v[w] = v[i]; v[w + 1] = v[i + 1]; c[w] = a; c[w + 1] = b; w += 2;
            }
            if (w < v.Count) { v.RemoveRange(w, v.Count - w); c.RemoveRange(w, c.Count - w); }
            idx.Clear();
            for (int i = 0; i < v.Count; i++) idx.Add(i);
        }

        public void Apply()
        {
            Mesh.Clear();
            Mesh.SetVertices(v);
            Mesh.SetColors(c);
            Mesh.SetIndices(idx, MeshTopology.Lines, 0);
            Mesh.RecalculateBounds();
            var b = Mesh.bounds; b.Expand(0.01f); Mesh.bounds = b;
        }
    }
}
