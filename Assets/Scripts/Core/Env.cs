using UnityEngine;
using UnityEngine.Rendering;

namespace MedPhys.Core
{
    /// <summary>Room, lighting and reflections shared by all modules.</summary>
    public static class Env
    {
        /// <summary>Builds the clinical room (Blender model) with lights and a reflection probe under parent.</summary>
        public static GameObject Room(Transform parent, Color accent, float yaw = 0, bool lights = true)
        {
            var room = Res.Model("Room", parent, Vector3.zero, yaw);
            var acc = Res.Emissive(accent * 0.4f, accent * 2.2f);
            Res.Swap(room, "M_Accent", acc);
            // the room shell must not shadow its own interior from the key light
            foreach (var r in room.GetComponentsInChildren<Renderer>())
                r.shadowCastingMode = ShadowCastingMode.Off;
            if (lights) Lights(parent, new Vector3(0, 1, 0));
            Ambient(new Color(0.55f, 0.60f, 0.70f), new Color(0.36f, 0.39f, 0.44f), new Color(0.16f, 0.17f, 0.19f));
            if (!App.Flag("-mpNoProbe")) Probe(parent, new Vector3(0, 1.6f, 0), new Vector3(11f, 3.3f, 11f));
            if (App.Flag("-mpRoomUnlit"))
                foreach (var r in room.GetComponentsInChildren<Renderer>()) r.sharedMaterial = Res.UnlitTex(Texture2D.whiteTexture, new Color(0.5f, 0.5f, 0.5f));
            if (App.Flag("-mpRoomShell"))
                foreach (var r in room.GetComponentsInChildren<Renderer>()) r.sharedMaterial = Res.Mat("M_Shell");
            if (App.Flag("-mpRoomNewLit"))
                foreach (var r in room.GetComponentsInChildren<Renderer>()) r.sharedMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            if (App.Flag("-mpDebug"))
            {
                foreach (var r in room.GetComponentsInChildren<Renderer>())
                    Debug.Log("[MP] room renderer " + r.name + " enabled=" + r.enabled + " mat=" + r.sharedMaterial.name + " layer=" + r.gameObject.layer + " shadows=" + r.shadowCastingMode + " recv=" + r.receiveShadows + " bounds=" + r.bounds + " lightProbe=" + r.lightProbeUsage + " lm=" + r.lightmapIndex + " rl=" + r.renderingLayerMask);
                foreach (var l in Object.FindObjectsByType<Light>()) Debug.Log("[MP] light " + l.name + " type=" + l.type + " int=" + l.intensity + " mask=" + l.cullingMask + " rl=" + l.renderingLayerMask + " enabled=" + l.enabled);
            }
            RenderSettings.fog = !App.Flag("-mpNoFog");
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = new Color(0.04f, 0.05f, 0.08f);
            RenderSettings.fogDensity = 0.025f;
            return room;
        }

        public static void Lights(Transform parent, Vector3 center, float key = 1.25f)
        {
            var go = new GameObject("KeyLight");
            go.transform.SetParent(parent, false);
            var l = go.AddComponent<Light>();
            l.type = LightType.Directional;
            l.color = new Color(1f, 0.97f, 0.93f);
            l.intensity = key;
            l.shadows = App.Flag("-mpNoShadows") ? LightShadows.None : LightShadows.Soft;
            l.shadowStrength = 0.75f;
            l.shadowBias = 0.04f; l.shadowNormalBias = 0.3f;
            go.transform.rotation = Quaternion.Euler(58f, -35f, 0);

            var f = new GameObject("FillLight");
            f.transform.SetParent(parent, false);
            var fl = f.AddComponent<Light>();
            fl.type = LightType.Directional;
            fl.color = new Color(0.65f, 0.80f, 1f);
            fl.intensity = 0.35f;
            fl.shadows = LightShadows.None;
            f.transform.rotation = Quaternion.Euler(25f, 150f, 0);

            for (int i = 0; i < 4; i++)
            {
                var p = new GameObject("CeilingLight" + i);
                p.transform.SetParent(parent, false);
                p.transform.localPosition = center + new Vector3((i % 2 == 0 ? -2.4f : 2.4f), 2.0f, (i < 2 ? -2.4f : 2.4f));   // under the ceiling panels
                var pl = p.AddComponent<Light>();
                pl.type = LightType.Point;
                pl.range = 8f;
                pl.intensity = 2.2f;
                pl.color = new Color(1f, 0.96f, 0.9f);
                pl.shadows = LightShadows.None;
            }
        }

        public static void Ambient(Color sky, Color equator, Color ground)
        {
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = sky;
            RenderSettings.ambientEquatorColor = equator;
            RenderSettings.ambientGroundColor = ground;
            RenderSettings.ambientIntensity = 1f;
            DynamicGI.UpdateEnvironment();
        }

        public static ReflectionProbe Probe(Transform parent, Vector3 pos, Vector3 size)
        {
            var go = new GameObject("ReflectionProbe");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            var rp = go.AddComponent<ReflectionProbe>();
            rp.mode = ReflectionProbeMode.Realtime;
            rp.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
            rp.timeSlicingMode = ReflectionProbeTimeSlicingMode.NoTimeSlicing;
            rp.size = size;
            rp.boxProjection = true;
            rp.resolution = Application.isMobilePlatform ? 64 : 256;
            rp.hdr = true;
            rp.intensity = 1f;
            rp.clearFlags = ReflectionProbeClearFlags.SolidColor;
            rp.backgroundColor = new Color(0.05f, 0.06f, 0.08f);
            App.I.StartCoroutine(RenderLater(rp));
            return rp;
        }

        static System.Collections.IEnumerator RenderLater(ReflectionProbe rp)
        {
            yield return null;
            yield return null;
            if (rp != null) rp.RenderProbe();
        }

        /// <summary>Simple pedestal / stand used to hold the phantom upright.</summary>
        public static GameObject Stand(Transform parent, Vector3 pos, float height)
        {
            var g = new GameObject("PhantomStand");
            g.transform.SetParent(parent, false);
            g.transform.localPosition = pos;
            var basePl = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Object.Destroy(basePl.GetComponent<Collider>());
            basePl.transform.SetParent(g.transform, false);
            basePl.transform.localScale = new Vector3(0.5f, 0.02f, 0.5f);
            basePl.transform.localPosition = new Vector3(0, 0.02f, 0);
            basePl.GetComponent<Renderer>().sharedMaterial = Res.Mat("M_Dark");
            var pole = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Object.Destroy(pole.GetComponent<Collider>());
            pole.transform.SetParent(g.transform, false);
            pole.transform.localScale = new Vector3(0.05f, height * 0.5f, 0.05f);
            pole.transform.localPosition = new Vector3(0, height * 0.5f, 0);
            pole.GetComponent<Renderer>().sharedMaterial = Res.Mat("M_Metal");
            return g;
        }
    }
}
