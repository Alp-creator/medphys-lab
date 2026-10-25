using System.Collections.Generic;
using UnityEngine;

namespace MedPhys.Core
{
    /// <summary>
    /// Procedural posing for the rigged Worker / Patient models (bones named Hips, Spine, UpperLeg.L, LowerLeg.L, ...).
    /// Bones are rotated about the character's own right / up / forward axes relative to their rest pose,
    /// so the bone roll produced by the exporter does not matter.
    /// Sign convention: pitch &lt; 0 swings a limb forward (knee/elbow flexion is pitch &gt; 0 for the lower leg,
    /// pitch &lt; 0 for the forearm); roll is about the forward axis.
    /// </summary>
    public sealed class HumanRig
    {
        struct B { public Transform t; public Quaternion rest; public Vector3 axR, axU, axF; }
        readonly Dictionary<string, B> bones = new Dictionary<string, B>();
        float phase, amount;
        public float StrideLength = 1.3f;

        static readonly string[] Names =
        {
            "Hips", "Spine", "Chest", "Neck", "Head",
            "UpperLeg.L", "LowerLeg.L", "Foot.L", "UpperLeg.R", "LowerLeg.R", "Foot.R",
            "Shoulder.L", "Shoulder.R", "UpperArm.L", "LowerArm.L", "UpperArm.R", "LowerArm.R", "Hand.L", "Hand.R",
        };

        /// <param name="root">Model root (in rest pose).</param>
        /// <param name="rightWorld">The character's right direction in world space at rest.</param>
        public HumanRig(Transform root, Vector3 rightWorld) : this(root, rightWorld, Vector3.up) { }

        public HumanRig(Transform root, Vector3 rightWorld, Vector3 upWorld)
        {
            var fwdWorld = Vector3.Cross(rightWorld, upWorld);
            foreach (var n in Names)
            {
                var t = Res.Find(root, n);
                if (t == null) continue;
                var inv = Quaternion.Inverse(t.rotation);
                bones[n] = new B { t = t, rest = t.localRotation, axR = inv * rightWorld, axU = inv * upWorld, axF = inv * fwdWorld };
            }
            // rest direction of the leaf bones (no child transform to measure them from later)
            foreach (var s in new[] { "L", "R" })
            {
                if (bones.TryGetValue("Hand." + s, out var h) && bones.TryGetValue("LowerArm." + s, out var la))
                    endDir["Hand." + s] = Quaternion.Inverse(h.t.rotation) * (h.t.position - la.t.position).normalized;
                if (bones.TryGetValue("Foot." + s, out var f))
                    endDir["Foot." + s] = Quaternion.Inverse(f.t.rotation) * (fwdWorld - upWorld * 0.25f).normalized;
            }
        }

        readonly Dictionary<string, Vector3> endDir = new Dictionary<string, Vector3>();

        public Transform Bone(string n) => bones.TryGetValue(n, out var b) ? b.t : null;
        public Vector3 Pos(string n) => bones.TryGetValue(n, out var b) ? b.t.position : Vector3.zero;
        public Quaternion Local(string n) => bones.TryGetValue(n, out var b) ? b.t.localRotation : Quaternion.identity;
        public void SetLocal(string n, Quaternion q) { if (bones.TryGetValue(n, out var b)) b.t.localRotation = q; }

        /// <summary>
        /// Analytic two-bone IK (law of cosines): turns upper and lower so that the end joint reaches the target
        /// (clamped to the limb's reach); the middle joint bends towards <paramref name="pole"/> (world direction).
        /// </summary>
        public void LimbIK(string upper, string lower, string end, Vector3 target, Vector3 pole)
        {
            if (!bones.TryGetValue(upper, out var u) || !bones.TryGetValue(lower, out var l) || !bones.TryGetValue(end, out var e)) return;
            Vector3 s = u.t.position, m = l.t.position, h = e.t.position;
            float a = (m - s).magnitude, b = (h - m).magnitude;
            if (a < 1e-4f || b < 1e-4f) return;
            Vector3 toT = target - s;
            float d = Mathf.Clamp(toT.magnitude, Mathf.Abs(a - b) + 1e-3f, a + b - 1e-3f);
            Vector3 dir = toT.sqrMagnitude > 1e-8f ? toT.normalized : (h - s).normalized;
            Vector3 bend = Vector3.ProjectOnPlane(pole, dir);
            if (bend.sqrMagnitude < 1e-6f) bend = Vector3.ProjectOnPlane(m - s, dir);
            bend.Normalize();
            float cosA = Mathf.Clamp((a * a + d * d - b * b) / (2 * a * d), -1f, 1f);
            Vector3 mid = s + dir * (a * cosA) + bend * (a * Mathf.Sqrt(1 - cosA * cosA));
            u.t.rotation = Quaternion.FromToRotation(m - s, mid - s) * u.t.rotation;
            m = l.t.position; h = e.t.position;
            l.t.rotation = Quaternion.FromToRotation(h - m, s + dir * d - m) * l.t.rotation;
        }

        /// <summary>Points a single bone so that the next joint lies towards the target (no stretching).</summary>
        public void Aim(string bone, string next, Vector3 target)
        {
            if (!bones.TryGetValue(bone, out var b) || !bones.TryGetValue(next, out var n)) return;
            Vector3 p = b.t.position;
            b.t.rotation = Quaternion.FromToRotation(n.t.position - p, target - p) * b.t.rotation;
        }

        /// <summary>Turns a hand / foot so that it points along the given world direction (lies flat on a surface).</summary>
        public void AlignEnd(string bone, Vector3 dirWorld)
        {
            if (!bones.TryGetValue(bone, out var b) || !endDir.TryGetValue(bone, out var local) || dirWorld.sqrMagnitude < 1e-8f) return;
            b.t.rotation = Quaternion.FromToRotation(b.t.rotation * local, dirWorld.normalized) * b.t.rotation;
        }

        public bool Valid => bones.Count >= 10;

        void Set(string n, float deg) => Rot(n, deg);

        /// <summary>Rotates a bone from its rest pose: pitch about right, roll about forward, yaw about up (degrees).</summary>
        public void Rot(string n, float pitch, float roll = 0, float yaw = 0)
        {
            if (!bones.TryGetValue(n, out var b)) return;
            b.t.localRotation = b.rest * Quaternion.AngleAxis(pitch, b.axR) * Quaternion.AngleAxis(roll, b.axF) * Quaternion.AngleAxis(yaw, b.axU);
        }

        /// <summary>Moves the arm away from the body (side "L" or "R").</summary>
        public void Arm(string side, float upperPitch, float abduct, float elbow)
        {
            Rot("UpperArm." + side, upperPitch, side == "L" ? -abduct : abduct);
            Rot("LowerArm." + side, elbow);
        }

        public void ResetPose()
        {
            foreach (var b in bones.Values) b.t.localRotation = b.rest;
        }

        /// <summary>Advance the gait. speed in m/s; holdRight keeps the right forearm raised (instrument in hand).</summary>
        public void Tick(float dt, float speed, bool holdRight)
        {
            amount = Mathf.MoveTowards(amount, speed > 0.05f ? 1f : 0f, dt * 4f);
            phase += dt * 2f * Mathf.PI * speed / StrideLength;
            float s = Mathf.Sin(phase), c = Mathf.Cos(phase), k = amount;
            // negative angle = swing forward
            Set("UpperLeg.L", -27f * s * k);
            Set("UpperLeg.R", 27f * s * k);
            Set("LowerLeg.L", 48f * Mathf.Max(0, c) * k);
            Set("LowerLeg.R", 48f * Mathf.Max(0, -c) * k);
            Set("Foot.L", -10f * Mathf.Max(0, -c) * k);
            Set("Foot.R", -10f * Mathf.Max(0, c) * k);
            Set("UpperArm.L", 20f * s * k);
            Set("LowerArm.L", -12f - 10f * k);
            Set("UpperArm.R", holdRight ? -32f : -20f * s * k);
            Set("LowerArm.R", holdRight ? -62f : -12f - 10f * k);
            Set("Spine", 3f * k);
            Set("Hips", 1.5f * Mathf.Sin(2 * phase) * k);
        }
    }
}
