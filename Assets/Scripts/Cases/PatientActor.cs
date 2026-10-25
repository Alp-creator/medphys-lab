using System;
using UnityEngine;
using MedPhys.Core;
using static MedPhys.Core.Loc;

namespace MedPhys.Cases
{
    /// <summary>Who the patient is: demographics plus the look of the 3D character.</summary>
    public sealed class PatientInfo
    {
        public string Model;              // Patient_<Model>.fbx
        public string Name;
        public int Age;
        public bool Female;
        public float HeightCm, WeightKg;
        public Color Skin = new Color(0.86f, 0.66f, 0.54f), Hair = new Color(0.12f, 0.08f, 0.06f);
        public Color Top = new Color(0.3f, 0.5f, 0.8f), Bottom = new Color(0.15f, 0.18f, 0.25f), Shoe = new Color(0.92f, 0.93f, 0.95f);
        public Color Blush = new Color(0.95f, 0.58f, 0.55f);
        public float Bmi => WeightKg / (HeightCm * HeightCm * 1e-4f);
        public string AgeSex => Age + T(" yaş, ", " y, ") + (Female ? T("kadın", "female") : (Age < 15 ? T("erkek çocuk", "boy") : T("erkek", "male")));
    }

    /// <summary>
    /// A rigged patient in the scene: colours, poses (standing, sitting, lying, chest PA) and idle motion.
    /// Hands and feet are placed with two-bone IK on real contact points (thigh surface, floor, detector handles)
    /// and <see cref="Settle"/> moves the whole body so that its deformed mesh touches a support surface
    /// without passing through it — nothing interpenetrates.
    /// </summary>
    public sealed class PatientActor
    {
        public enum PoseKind { Stand, Sit, Lie, ChestPA }

        public readonly Transform Pivot;
        public readonly PatientInfo Info;
        public readonly float Height, BackDepth, FrontDepth;
        readonly HumanRig rig;
        readonly SkinnedMeshRenderer body;
        readonly SkinnedMeshRenderer[] skins;    // body + rigidly skinned head / hair / clothing parts
        readonly Renderer[] parts;
        readonly float thighTop, ankleHeight, hipHalf;   // measured on the mesh at rest
        PoseKind pose;
        float t, seed, waveT = -1;
        float floorY;
        Vector3? handL, handR;                   // external hand targets (e.g. handles), world space
        float neckFlex, legDrop;                 // supine: neck flexion / hip extension (deg) chosen by RestOn so head and heels touch
        Mesh bake;
        readonly System.Collections.Generic.List<Vector3> pts = new System.Collections.Generic.List<Vector3>();

        const float HandHalf = 0.026f;          // half thickness of a hand resting on a surface

        PatientActor(PatientInfo info, Transform pivot, HumanRig rig, float height, float back, float front,
                     SkinnedMeshRenderer body, SkinnedMeshRenderer[] skins, Renderer[] parts, float thighTop, float ankleHeight, float hipHalf)
        {
            Info = info; Pivot = pivot; this.rig = rig; Height = height; BackDepth = back; FrontDepth = front;
            this.body = body; this.skins = skins; this.parts = parts; this.thighTop = thighTop; this.ankleHeight = ankleHeight; this.hipHalf = hipHalf;
            seed = (info.Name.GetHashCode() & 1023) * 0.37f;
        }

        public static PatientActor Create(PatientInfo p, Transform parent)
        {
            var pivot = new GameObject("Patient_" + p.Model).transform;
            pivot.SetParent(parent, false);
            var model = Res.Model("Patient_" + p.Model, pivot, Vector3.zero, 180f);
            var skin = Res.Lit(p.Skin, 0.42f);
            var hair = Res.Lit(p.Hair, 0.45f);
            var top = Res.Fabric(p.Top);
            var bottom = Res.Fabric(p.Bottom);
            var shoe = Res.Lit(p.Shoe, 0.5f);
            var eye = Res.Lit(new Color(0.05f, 0.04f, 0.04f), 0.9f);
            var eyeWhite = Res.Lit(new Color(0.97f, 0.97f, 0.97f), 0.85f);
            var mouth = Res.Lit(new Color(0.55f, 0.12f, 0.14f), 0.6f);
            var teeth = Res.Lit(new Color(0.98f, 0.98f, 0.96f), 0.7f);
            var blush = Res.Lit(Color.Lerp(p.Skin, p.Blush, 0.75f), 0.4f);
            var frame = Res.Lit(new Color(0.12f, 0.12f, 0.14f), 0.7f, 0.6f);
            var band = Res.Lit(new Color(0.97f, 0.97f, 1f), 0.5f);
            SkinnedMeshRenderer smrBody = null;
            var others = new System.Collections.Generic.List<Renderer>();
            var skinned = new System.Collections.Generic.List<SkinnedMeshRenderer>();
            foreach (var r in model.GetComponentsInChildren<Renderer>(true))
            {
                var sm = r.sharedMaterials;
                for (int i = 0; i < sm.Length; i++)
                {
                    string n = sm[i] != null ? sm[i].name : "";
                    sm[i] = n.StartsWith("M_Skin") ? skin : n.StartsWith("M_Hair") ? hair : n.StartsWith("M_Top") ? top
                          : n.StartsWith("M_Bottom") ? bottom : n.StartsWith("M_Shoe") ? shoe : n.StartsWith("M_EyeWhite") ? eyeWhite
                          : n.StartsWith("M_Eye") ? eye : n.StartsWith("M_Mouth") ? mouth : n.StartsWith("M_Teeth") ? teeth
                          : n.StartsWith("M_Blush") ? blush : n.StartsWith("M_Frame") ? frame : n.StartsWith("M_Band") ? band : sm[i];
                }
                r.sharedMaterials = sm;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                if (r is SkinnedMeshRenderer smr) { smr.updateWhenOffscreen = true; if (smrBody == null) smrBody = smr; skinned.Add(smr); }
                else others.Add(r);
            }
            // rest-pose extent in pivot space (standing, facing +Z)
            float h = p.HeightCm * 0.01f, back = 0.12f, front = 0.12f;
            var bb = new Bounds(); bool any = false;
            foreach (var r in model.GetComponentsInChildren<Renderer>(true))
            {
                var b = r.bounds;
                var lo = pivot.InverseTransformPoint(b.min); var hi = pivot.InverseTransformPoint(b.max);
                var lb = new Bounds((lo + hi) * 0.5f, new Vector3(Mathf.Abs(hi.x - lo.x), Mathf.Abs(hi.y - lo.y), Mathf.Abs(hi.z - lo.z)));
                if (!any) { bb = lb; any = true; } else bb.Encapsulate(lb);
            }
            if (any && bb.max.y > 0.5f) { h = bb.max.y; back = Mathf.Clamp(-bb.min.z, 0.06f, 0.35f); front = Mathf.Clamp(bb.max.z, 0.06f, 0.4f); }
            var rig = new HumanRig(model.transform, pivot.right, pivot.up);
            if (!rig.Valid) Debug.LogWarning("[MP] patient rig bones not found: " + p.Model);

            // measure the thigh's anterior surface and the ankle height on the skinned mesh (rest pose)
            float thighTop = 0.08f * h / 1.75f, ankle = 0.05f * h, hipHalf = 0.16f * h / 1.75f;
            if (smrBody != null)
            {
                var m = new Mesh();
                smrBody.BakeMesh(m);
                var v = m.vertices;
                var w = smrBody.transform.localToWorldMatrix;
                Vector3 hip = pivot.InverseTransformPoint(rig.Pos("UpperLeg.L")), knee = pivot.InverseTransformPoint(rig.Pos("LowerLeg.L"));
                Vector3 ank = pivot.InverseTransformPoint(rig.Pos("Foot.L"));
                float handX = Mathf.Abs(pivot.InverseTransformPoint(rig.Pos("Hand.L")).x);
                float best = 0, minFoot = 1e9f, hipW = 0;
                for (int i = 0; i < v.Length; i++)
                {
                    var q = pivot.InverseTransformPoint(w.MultiplyPoint3x4(v[i]));
                    float tt = (hip.y - q.y) / Mathf.Max(1e-3f, hip.y - knee.y);
                    if (tt > 0.25f && tt < 0.75f)
                    {
                        float ax = Mathf.Lerp(hip.x, knee.x, tt), az = Mathf.Lerp(hip.z, knee.z, tt);
                        if (Mathf.Abs(q.x - ax) < 0.03f && q.z > az) best = Mathf.Max(best, q.z - az);
                    }
                    if (Mathf.Abs(q.x - ank.x) < 0.08f) minFoot = Mathf.Min(minFoot, q.y);
                    if (q.y > hip.y - 0.02f && q.y < hip.y + 0.07f && Mathf.Abs(q.x) < 0.6f * handX) hipW = Mathf.Max(hipW, Mathf.Abs(q.x));
                }
                if (best > 0.03f) thighTop = best;
                if (minFoot < 1e8f) ankle = Mathf.Max(0.02f, ank.y - minFoot);
                if (hipW > 0.05f) hipHalf = hipW;
                UnityEngine.Object.Destroy(m);
            }
            return new PatientActor(p, pivot, rig, h, back, front, smrBody, skinned.ToArray(), others.ToArray(), thighTop, ankle, hipHalf);
        }

        // ------------------------------------------------------------------ placement
        public void Stand(Vector3 floorPos, float yaw, PoseKind kind = PoseKind.Stand)
        {
            Pivot.position = floorPos;
            Pivot.rotation = Quaternion.Euler(0, yaw, 0);
            floorY = floorPos.y;
            SetPose(kind);
        }

        /// <summary>Sits on a seat; floorPos = point on the floor under the hip joints. Call <see cref="Settle"/> afterwards.</summary>
        public void Sit(Vector3 floorPos, float yaw, float seatHeight)
        {
            Pivot.rotation = Quaternion.Euler(0, yaw, 0);
            Pivot.position = floorPos + Vector3.up * (seatHeight + thighTop - Height * 0.535f);
            floorY = floorPos.y;
            SetPose(PoseKind.Sit);
        }

        /// <summary>Lies supine; head towards <paramref name="superior"/>, back on a surface at height surfaceY.</summary>
        public void LieSupine(Vector3 headTop, Vector3 superior, float surfaceY)
        {
            superior = new Vector3(superior.x, 0, superior.z).normalized;
            Pivot.rotation = Quaternion.LookRotation(Vector3.up, superior);
            var feet = headTop - superior * Height;
            Pivot.position = new Vector3(feet.x, surfaceY + BackDepth + 0.01f, feet.z);
            floorY = surfaceY;
            SetPose(PoseKind.Lie);
        }

        /// <summary>World targets for the wrists (null = default placement for the pose).</summary>
        public void SetHandTargets(Vector3? left, Vector3? right) { handL = left; handR = right; }

        /// <summary>World position of a point along the body axis, as a fraction of height from the feet (0) to the head (1).</summary>
        /// <summary>World height of the shoulder joints in the current pose.</summary>
        public float ShoulderY => rig.Pos("UpperArm.L").y;

        public Vector3 BodyPoint(float frac, float anterior = 0) => Pivot.position + Pivot.up * (Height * frac) + Pivot.forward * anterior;

        public void SetPose(PoseKind k) { pose = k; ApplyPose(0); }

        /// <summary>Friendly wave with the right hand (overlays the current pose for ~2.4 s).</summary>
        public void Wave() { waveT = 0; }

        /// <summary>
        /// Moves the whole body along <paramref name="axis"/> until the geometry (deformed skin + attached parts) that lies
        /// over <paramref name="region"/> just touches the plane dot(p, axis) = plane — e.g. axis = down for a seat or a couch,
        /// axis = forward for a detector in front of the chest. Uses the current pose.
        /// </summary>
        public void Settle(Vector3 axis, float plane, Bounds? region = null, float gap = 0.004f)
        {
            axis.Normalize();
            ApplyPose(0);
            float max = float.NegativeInfinity;
            Func<Vector3, bool> inside = q =>
            {
                if (region == null) return true;
                var r = region.Value; var d = q - r.center; var e = r.extents;
                bool ok = true;
                if (Mathf.Abs(axis.x) < 0.7f) ok &= Mathf.Abs(d.x) <= e.x;
                if (Mathf.Abs(axis.y) < 0.7f) ok &= Mathf.Abs(d.y) <= e.y;
                if (Mathf.Abs(axis.z) < 0.7f) ok &= Mathf.Abs(d.z) <= e.z;
                return ok;
            };
            if (bake == null) bake = new Mesh();
            foreach (var sm in skins)
            {
                sm.BakeMesh(bake);
                var v = bake.vertices;
                var w = sm.transform.localToWorldMatrix;
                for (int i = 0; i < v.Length; i++)
                {
                    var q = w.MultiplyPoint3x4(v[i]);
                    if (inside(q)) max = Mathf.Max(max, Vector3.Dot(q, axis));
                }
            }
            foreach (var r in parts)
            {
                var b = r.bounds;
                for (int k = 0; k < 8; k++)
                {
                    var c = new Vector3((k & 1) == 0 ? b.min.x : b.max.x, (k & 2) == 0 ? b.min.y : b.max.y, (k & 4) == 0 ? b.min.z : b.max.z);
                    if (inside(b.center)) max = Mathf.Max(max, Vector3.Dot(c, axis));
                }
            }
            if (float.IsNegativeInfinity(max)) return;
            Pivot.position += axis * (plane - gap - max);
            ApplyPose(0);
        }

        /// <summary>
        /// Lays a supine body onto a couch like a solid object: every surface point is tested against the support boxes
        /// directly under it (mattress, tabletop, head rest...), so the body rests on what it really touches. The trunk is
        /// settled first; then the neck is flexed (raised head rest) or extended (flat table) until the head just rests on
        /// its support. Renderers whose name contains one of <paramref name="ignore"/> are not supports.
        /// </summary>
        public void RestOn(Transform couch, params string[] ignore)
        {
            var sup = new System.Collections.Generic.List<Bounds>();
            foreach (var r in couch.GetComponentsInChildren<Renderer>())
            {
                bool skip = r.name.Contains("LED");
                foreach (var n in ignore) skip |= r.name.Contains(n);
                if (!skip) sup.Add(r.bounds);
            }
            if (sup.Count == 0) return;
            neckFlex = 0; legDrop = 0;
            Gaps(sup, out float legs, out float trunk, out _);
            float low = Mathf.Min(legs, trunk);
            if (low < float.MaxValue) Pivot.position += Vector3.down * (low - 0.004f);
            // heels down: extend the hips until the legs touch the couch too
            for (float a = 0.5f; a <= 14f && legs > 0.008f && legs < float.MaxValue; a += 0.5f)
            {
                legDrop = a;
                Gaps(sup, out legs, out _, out _);
                if (legs < 0.002f) { legDrop = a - 0.5f; break; }
            }
            float head = float.MaxValue;
            for (float a = -14f; a <= 40f; a += 2f)
            {
                neckFlex = a;
                Gaps(sup, out _, out _, out head);
                if (head >= 0.002f) break;
            }
            if (head < 0 && head > -1f) Pivot.position += Vector3.up * (0.002f - head);   // head rest too high even when flexed
            Gaps(sup, out legs, out trunk, out head);
            Debug.Log("[MP] rest " + Info.Model + ": legs " + (legs * 100).ToString("0.0") + " cm, trunk " + (trunk * 100).ToString("0.0") +
                      " cm, head " + (head * 100).ToString("0.0") + " cm, neck " + neckFlex + " deg, hip " + legDrop + " deg");
        }

        /// <summary>Smallest clearance between the body (legs / trunk / head separately) and the supports under it.</summary>
        void Gaps(System.Collections.Generic.List<Bounds> sup, out float legs, out float trunk, out float head)
        {
            ApplyPose(0);
            pts.Clear();
            if (bake == null) bake = new Mesh();
            foreach (var sm in skins)
            {
                sm.BakeMesh(bake);
                var w = sm.transform.localToWorldMatrix;
                foreach (var v in bake.vertices) pts.Add(w.MultiplyPoint3x4(v));
            }
            foreach (var r in parts)
            {
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                var w = r.transform.localToWorldMatrix;
                foreach (var v in mf.sharedMesh.vertices) pts.Add(w.MultiplyPoint3x4(v));
            }
            float neck = Vector3.Dot(rig.Pos("Neck") - Pivot.position, Pivot.up) + 0.03f;
            float thigh = Vector3.Dot(rig.Pos("UpperLeg.L") - Pivot.position, Pivot.up) - 0.12f;
            legs = trunk = head = float.MaxValue;
            foreach (var q in pts)
            {
                float top = float.NegativeInfinity;
                foreach (var b in sup)
                    if (Mathf.Abs(q.x - b.center.x) <= b.extents.x && Mathf.Abs(q.z - b.center.z) <= b.extents.z && b.max.y < q.y + 0.3f)
                        top = Mathf.Max(top, b.max.y);
                if (float.IsNegativeInfinity(top)) continue;
                float g = q.y - top;
                float h = Vector3.Dot(q - Pivot.position, Pivot.up);
                if (h > neck) head = Mathf.Min(head, g); else if (h < thigh) legs = Mathf.Min(legs, g); else trunk = Mathf.Min(trunk, g);
            }
        }

        // ------------------------------------------------------------------ animation
        public void Tick(float dt) => ApplyPose(dt);

        void ApplyPose(float dt)
        {
            t += dt;
            float breath = Mathf.Sin((t + seed) * 1.7f);
            float look = Mathf.Sin((t + seed) * 0.31f) + 0.5f * Mathf.Sin((t + seed) * 0.83f);
            bool child = Info.Age < 14;
            rig.ResetPose();
            switch (pose)
            {
                case PoseKind.Sit:
                {
                    rig.Rot("UpperLeg.L", -88f); rig.Rot("UpperLeg.R", -88f);
                    rig.Rot("Spine", -3f + 0.6f * breath);
                    rig.Rot("Head", 4f, 0, 14f * look);
                    float swing = child ? 0.07f * Mathf.Sin((t + seed) * 3.2f) : 0f;
                    FootOnFloor("L", swing); FootOnFloor("R", -swing);
                    HandOnThigh("L", 0.62f, pole: -Pivot.forward * 0.6f - Vector3.up * 0.3f);
                    HandOnThigh("R", 0.62f, pole: -Pivot.forward * 0.6f - Vector3.up * 0.3f);
                    break;
                }
                case PoseKind.Lie:
                    rig.Rot("Chest", 0.8f * breath);
                    rig.Rot("Neck", neckFlex * 0.5f);
                    rig.Rot("UpperLeg.L", legDrop); rig.Rot("UpperLeg.R", legDrop);
                    rig.Rot("Head", neckFlex * 0.5f, 0, 5f * look);
                    // supine: Pivot.forward points to the ceiling, so the elbows go out sideways (not down into the couch)
                    HandOnThigh("L", 0.18f, pole: Pivot.forward * 0.15f - Pivot.up * 0.2f);
                    HandOnThigh("R", 0.18f, pole: Pivot.forward * 0.15f - Pivot.up * 0.2f);
                    break;
                case PoseKind.ChestPA:
                    rig.Rot("Chest", 0.8f * breath);
                    rig.Rot("Head", -6f);
                    // classic PA chest positioning: hands on the hips, elbows rolled outwards (scapulae out of the lungs)
                    if (handL.HasValue) HandTo("L", handL.Value); else HandOnHip("L");
                    if (handR.HasValue) HandTo("R", handR.Value); else HandOnHip("R");
                    break;
                default:
                    rig.Arm("L", 0, 3f, -10f); rig.Arm("R", 0, 3f, -10f);
                    rig.Rot("Chest", 0.8f * breath);
                    rig.Rot("Head", 0, 0, 16f * look);
                    break;
            }
            if (waveT >= 0)
            {
                waveT += dt;
                float wv = Mathf.Sin(waveT * 9f);
                float up = Mathf.Clamp01(waveT * 4f) * Mathf.Clamp01((2.4f - waveT) * 4f);
                Quaternion u0 = rig.Local("UpperArm.R"), l0 = rig.Local("LowerArm.R"), h0 = rig.Local("Hand.R");
                rig.Arm("R", -12f, 135f, -25f + 22f * wv);
                rig.SetLocal("Hand.R", h0);
                rig.SetLocal("UpperArm.R", Quaternion.Slerp(u0, rig.Local("UpperArm.R"), up));
                rig.SetLocal("LowerArm.R", Quaternion.Slerp(l0, rig.Local("LowerArm.R"), up));
                if (waveT > 2.4f) waveT = -1;
            }
        }

        Vector3 SideDir(string side) => side == "L" ? -Pivot.right : Pivot.right;   // the patient's left is -right

        /// <summary>Rests the hand flat on the anterior (upper) surface of the thigh at fraction f from hip to knee.</summary>
        void HandOnThigh(string side, float f, Vector3 pole)
        {
            Vector3 hip = rig.Pos("UpperLeg." + side), knee = rig.Pos("LowerLeg." + side);
            Vector3 axis = knee - hip, ax = axis.normalized;
            Vector3 n = Vector3.ProjectOnPlane(Vector3.up, ax);
            if (n.sqrMagnitude < 1e-6f) n = Pivot.forward;
            n.Normalize();
            Vector3 contact = hip + axis * f + n * (thighTop + HandHalf) + SideDir(side) * 0.012f;
            Vector3 along = Vector3.ProjectOnPlane(ax, n).normalized;
            Vector3 wrist = contact - along * 0.06f;
            rig.LimbIK("UpperArm." + side, "LowerArm." + side, "Hand." + side, wrist, pole + SideDir(side));
            rig.AlignEnd("Hand." + side, along);
        }

        void HandOnHip(string side)
        {
            Vector3 hip = rig.Pos("UpperLeg." + side);
            Vector3 c = Pivot.position + Vector3.Project(hip - Pivot.position, Pivot.up);   // body axis at hip height
            Vector3 wrist = c + Pivot.up * 0.07f + SideDir(side) * (hipHalf + HandHalf + 0.012f) - Pivot.forward * 0.03f;
            rig.LimbIK("UpperArm." + side, "LowerArm." + side, "Hand." + side, wrist, SideDir(side) + Pivot.forward * 0.15f);
            rig.AlignEnd("Hand." + side, (Pivot.forward - Pivot.up * 0.8f).normalized);
        }

        void HandTo(string side, Vector3 target)
        {
            rig.LimbIK("UpperArm." + side, "LowerArm." + side, "Hand." + side, target, SideDir(side) - Pivot.forward * 0.4f - Vector3.up * 0.4f);
        }

        /// <summary>Lower leg hangs from the knee; the foot is put flat on the floor when the shin is long enough, otherwise it dangles.</summary>
        void FootOnFloor(string side, float swing)
        {
            Vector3 knee = rig.Pos("LowerLeg." + side), ank = rig.Pos("Foot." + side);
            float shin = (ank - knee).magnitude;
            float drop = knee.y - (floorY + ankleHeight);
            var fwd = Vector3.ProjectOnPlane(Pivot.forward, Vector3.up).normalized;
            Vector3 target;
            if (shin > drop && drop > 0) target = knee - Vector3.up * drop + fwd * Mathf.Sqrt(shin * shin - drop * drop);
            else target = knee - Vector3.up * shin * 0.97f + fwd * (0.05f + swing) * shin;
            rig.Aim("LowerLeg." + side, "Foot." + side, target);
            rig.AlignEnd("Foot." + side, fwd - Vector3.up * (shin > drop ? 0f : 0.35f));
        }
    }
}
