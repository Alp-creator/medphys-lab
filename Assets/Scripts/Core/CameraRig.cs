using System;
using UnityEngine;
using UnityEngine.InputSystem;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;

namespace MedPhys.Core
{
    /// <summary>
    /// Orbit camera for mouse and touch: drag = rotate, right/middle drag or two fingers = pan,
    /// wheel / pinch = zoom. Pointer interactions starting over UI are ignored. Short taps raise OnTap.
    /// </summary>
    public sealed class CameraRig : MonoBehaviour
    {
        public Vector3 Target = new Vector3(0, 1, 0);
        public float Yaw = 30, Pitch = 18, Distance = 5;
        public float MinDistance = 0.4f, MaxDistance = 14f;
        public float MinPitch = -10f, MaxPitch = 85f;
        public float MaxCamY = 3.05f;            // keep the camera below the room ceiling
        public bool InputEnabled = true;
        public float AutoRotate;                 // deg/s when idle
        public Bounds? PanBounds;
        public event Action<Vector2> OnTap;      // screen position of a tap / click on the 3D view

        Vector3 tTarget; float tYaw, tPitch, tDist;
        bool dragging, startedOverUI, moved;
        Vector2 downPos, lastPos;
        float lastPinch; Vector2 lastMid;
        float idle;

        void Awake() { tTarget = Target; tYaw = Yaw; tPitch = Pitch; tDist = Distance; }

        public void Frame(Vector3 target, float distance, float yaw, float pitch, bool instant = false)
        {
            tTarget = target; tDist = distance; tYaw = yaw; tPitch = pitch;
            if (instant) { Target = target; Distance = distance; Yaw = yaw; Pitch = pitch; Apply(); }
        }

        /// <summary>Moves only the orbit target (keeps the user's yaw/pitch/zoom).</summary>
        public void SetTarget(Vector3 target) { tTarget = target; }

        public void ResetInput()
        {
            dragging = false; AutoRotate = 0; PanBounds = null; OnTap = null;
            MinDistance = 0.4f; MaxDistance = 14f; MinPitch = -10f; MaxPitch = 85f; InputEnabled = true; MaxCamY = 3.05f;
        }

        void LateUpdate()
        {
            if (InputEnabled) HandleInput();
            idle += Time.unscaledDeltaTime;
            if (AutoRotate != 0 && idle > 3f) tYaw += AutoRotate * Time.unscaledDeltaTime;
            float k = 1f - Mathf.Exp(-Time.unscaledDeltaTime * 10f);
            Target = Vector3.Lerp(Target, tTarget, k);
            Yaw = Mathf.LerpAngle(Yaw, tYaw, k);
            Pitch = Mathf.Lerp(Pitch, tPitch, k);
            Distance = Mathf.Lerp(Distance, tDist, k);
            Apply();
        }

        void Apply()
        {
            var rot = Quaternion.Euler(Pitch, Yaw, 0);
            var off = rot * new Vector3(0, 0, -Distance);
            if (Target.y + off.y > MaxCamY && off.y > 1e-3f) off *= Mathf.Max(0.05f, (MaxCamY - Target.y) / off.y);
            transform.position = Target + off;
            transform.rotation = rot;
        }

        void HandleInput()
        {
            var app = App.I;
            var touches = Touch.activeTouches;
            if (touches.Count >= 2)
            {
                var a = touches[0].screenPosition; var b = touches[1].screenPosition;
                float pinch = Vector2.Distance(a, b);
                Vector2 mid = (a + b) * 0.5f;
                bool began = touches[0].phase == UnityEngine.InputSystem.TouchPhase.Began || touches[1].phase == UnityEngine.InputSystem.TouchPhase.Began;
                if (began || lastPinch <= 0)
                {
                    startedOverUI = app != null && (app.IsOverUI(a) || app.IsOverUI(b));
                }
                else if (!startedOverUI)
                {
                    tDist = Mathf.Clamp(tDist * lastPinch / Mathf.Max(pinch, 1f), MinDistance, MaxDistance);
                    Pan(mid - lastMid);
                }
                lastPinch = pinch; lastMid = mid; dragging = false; moved = true; idle = 0;
                return;
            }
            lastPinch = 0;

            Vector2 pos; bool down, held, up; bool panButton = false; float scroll = 0;
            if (touches.Count == 1)
            {
                var t = touches[0];
                pos = t.screenPosition;
                down = t.phase == UnityEngine.InputSystem.TouchPhase.Began;
                up = t.phase == UnityEngine.InputSystem.TouchPhase.Ended || t.phase == UnityEngine.InputSystem.TouchPhase.Canceled;
                held = !up;
            }
            else
            {
                var m = Mouse.current;
                if (m == null) return;
                pos = m.position.ReadValue();
                bool l = m.leftButton.isPressed, r = m.rightButton.isPressed || m.middleButton.isPressed;
                down = m.leftButton.wasPressedThisFrame || m.rightButton.wasPressedThisFrame || m.middleButton.wasPressedThisFrame;
                up = m.leftButton.wasReleasedThisFrame || m.rightButton.wasReleasedThisFrame || m.middleButton.wasReleasedThisFrame;
                held = l || r;
                panButton = r;
                scroll = m.scroll.ReadValue().y;
                if (Mathf.Abs(scroll) > 0.01f && app != null && !app.IsOverUI(pos))
                {
                    float s = scroll > 0 ? 0.88f : 1.0f / 0.88f;
                    tDist = Mathf.Clamp(tDist * s, MinDistance, MaxDistance);
                    idle = 0;
                }
            }

            if (down)
            {
                startedOverUI = app != null && app.IsOverUI(pos);
                dragging = !startedOverUI;
                downPos = lastPos = pos; moved = false;
            }
            if (dragging && held)
            {
                Vector2 d = pos - lastPos;
                if ((pos - downPos).sqrMagnitude > 64f) moved = true;
                float scale = 180f / Mathf.Max(Screen.height, 1);
                if (panButton) Pan(d);
                else
                {
                    tYaw += d.x * scale * 1.2f;
                    tPitch = Mathf.Clamp(tPitch - d.y * scale, MinPitch, MaxPitch);
                }
                if (d.sqrMagnitude > 0) idle = 0;
                lastPos = pos;
            }
            if (up)
            {
                if (dragging && !moved) OnTap?.Invoke(pos);
                dragging = false;
            }
        }

        void Pan(Vector2 d)
        {
            float s = tDist * 0.0016f;
            var right = transform.right; var up = transform.up;
            tTarget -= (right * d.x + up * d.y) * s;
            if (PanBounds.HasValue)
            {
                var b = PanBounds.Value;
                tTarget = new Vector3(Mathf.Clamp(tTarget.x, b.min.x, b.max.x), Mathf.Clamp(tTarget.y, b.min.y, b.max.y), Mathf.Clamp(tTarget.z, b.min.z, b.max.z));
            }
        }
    }
}
