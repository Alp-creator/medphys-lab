using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;
using MedPhys.Modules;

namespace MedPhys.Core
{
    /// <summary>Application root: camera, post-processing, UI document and module switching.</summary>
    public sealed class App : MonoBehaviour
    {
        public static App I { get; private set; }

        public Camera Cam { get; private set; }
        public CameraRig Rig { get; private set; }
        public UIDocument Doc { get; private set; }
        public VisualElement UIRoot { get; private set; }
        public Volume PostVolume { get; private set; }
        public bool SmallScreen { get; private set; }

        ModuleBase current;
        string currentId;
        readonly Dictionary<string, Func<ModuleBase>> registry = new Dictionary<string, Func<ModuleBase>>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Flag("-mpNoApp")) { Debug.Log("[MP] no app"); return; }
            if (FindAnyObjectByType<App>() == null)
                new GameObject("MedPhysApp").AddComponent<App>();
        }

        void Awake()
        {
            I = this;
            Debug.Log("[MP] App.Awake");
            bool turkishRegion = CultureInfo.CurrentCulture.Name.StartsWith("tr") || CultureInfo.CurrentUICulture.Name.StartsWith("tr");
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;
            Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
            Application.targetFrameRate = 60;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            Loc.Init(turkishRegion);
            if (Flag("-mpResetProgress")) { Progress.ResetAll(); MedPhys.Cases.CaseLibrary.ClearProgress(); MedPhys.Learn.LearnProgress.ResetAll(); }
            Progress.Load();
            if (!UnityEngine.InputSystem.EnhancedTouch.EnhancedTouchSupport.enabled)
                UnityEngine.InputSystem.EnhancedTouch.EnhancedTouchSupport.Enable();

            // remove any camera / light the bootstrap scene might contain
            foreach (var c in FindObjectsByType<Camera>()) if (c.gameObject != gameObject) Destroy(c.gameObject);
            foreach (var l in FindObjectsByType<Light>()) Destroy(l.gameObject);
            foreach (var v in FindObjectsByType<Volume>()) Destroy(v.gameObject);

            SetupCamera(); Debug.Log("[MP] camera ok");
            if (!Flag("-mpNoPost")) SetupPost(); Debug.Log("[MP] post ok");
            if (!Flag("-mpNoDoc")) SetupUI(); else { var g = new GameObject("x"); UIRoot = new UnityEngine.UIElements.VisualElement(); }
            Debug.Log("[MP] ui ok");

            registry["hub"] = () => new HubModule();
            registry["xray"] = () => new XRayModule();
            registry["ct"] = () => new CTModule();
            registry["therapy"] = () => new TherapyModule();
            registry["nucmed"] = () => new NucMedModule();
            registry["mri"] = () => new MRIModule();
            registry["protection"] = () => new ProtectionModule();
            registry["cases"] = () => new CasesModule();
            registry["learn"] = () => new LearnModule();

            ApplyLanguage();
            Loc.Changed += () => { ApplyLanguage(); Open(currentId ?? "hub"); };
        }

        void Start()
        {
            string startModule = Arg("-mpModule") ?? "hub";
            Debug.Log("[MP] Start -> " + startModule);
            if (!Flag("-mpEmpty")) Open(startModule);
            if (Arg("-mpShot") != null) StartCoroutine(AutoShot());
        }

        /// <summary>Right-to-left layout of text for Arabic / Persian / Hebrew; shaping text generator for complex scripts.</summary>
        void ApplyLanguage()
        {
            if (UIRoot == null) return;
            var l = Loc.Current;
            UIRoot.languageDirection = l.Rtl ? UnityEngine.UIElements.LanguageDirection.RTL : UnityEngine.UIElements.LanguageDirection.LTR;
            UIRoot.EnableInClassList("mp-lang-complex", l.Complex);
        }

        void SetupCamera()
        {
            var go = new GameObject("MainCamera");
            go.tag = "MainCamera";
            Cam = go.AddComponent<Camera>();
            Cam.nearClipPlane = 0.05f;
            Cam.farClipPlane = 200f;
            Cam.fieldOfView = 45f;
            Cam.clearFlags = CameraClearFlags.SolidColor;
            Cam.backgroundColor = new Color(0.02f, 0.03f, 0.05f);
            Cam.allowHDR = true;
            var data = Cam.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            data.antialiasingQuality = AntialiasingQuality.Medium;
            go.AddComponent<AudioListener>();
            Rig = go.AddComponent<CameraRig>();
        }

        void SetupPost()
        {
            var go = new GameObject("PostFX");
            go.transform.SetParent(transform);
            PostVolume = go.AddComponent<Volume>();
            PostVolume.isGlobal = true;
            var p = ScriptableObject.CreateInstance<VolumeProfile>();
            var bloom = p.Add<Bloom>(true);
            bloom.threshold.Override(0.95f);
            bloom.intensity.Override(0.75f);
            bloom.scatter.Override(0.65f);
            var tm = p.Add<Tonemapping>(true);
            tm.mode.Override(TonemappingMode.ACES);
            var ca = p.Add<ColorAdjustments>(true);
            ca.postExposure.Override(0.15f);
            ca.contrast.Override(12f);
            ca.saturation.Override(6f);
            var vig = p.Add<Vignette>(true);
            vig.intensity.Override(0.28f);
            vig.smoothness.Override(0.45f);
            PostVolume.profile = p;
        }

        void SetupUI()
        {
            var go = new GameObject("UI");
            go.transform.SetParent(transform);
            Doc = go.AddComponent<UIDocument>();
            var ps = Resources.Load<PanelSettings>("UI/PanelSettings");
            if (ps == null)
            {
                ps = ScriptableObject.CreateInstance<PanelSettings>();
                ps.themeStyleSheet = Resources.Load<ThemeStyleSheet>("UI/RuntimeTheme");
            }
            ps = Instantiate(ps);
            ps.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            ps.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            ps.match = 1f;
            // phones get a smaller logical resolution, i.e. larger widgets
            float dpi = Screen.dpi > 0 ? Screen.dpi : 160f;
            float inches = Mathf.Sqrt(Screen.width * Screen.width + Screen.height * Screen.height) / dpi;
            SmallScreen = Application.isMobilePlatform && inches < 8.5f;
            float uiScale = PlayerPrefs.GetFloat("ui_scale", 1f);
            int refH = Mathf.RoundToInt((SmallScreen ? 760 : 1080) / uiScale);
            ps.referenceResolution = new Vector2Int(Mathf.RoundToInt(refH * 16f / 9f), refH);
            ps.sortingOrder = 10;
            Doc.panelSettings = ps;
            UIRoot = Doc.rootVisualElement;
            var theme = Resources.Load<StyleSheet>("UI/Theme");
            if (theme != null) UIRoot.styleSheets.Add(theme);
            UIRoot.AddToClassList("mp-root");
            UIRoot.pickingMode = PickingMode.Ignore;
        }

        public void SetUiScale(float s)
        {
            PlayerPrefs.SetFloat("ui_scale", s);
            int refH = Mathf.RoundToInt((SmallScreen ? 760 : 1080) / s);
            Doc.panelSettings.referenceResolution = new Vector2Int(Mathf.RoundToInt(refH * 16f / 9f), refH);
        }

        public void Open(string id)
        {
            if (!registry.ContainsKey(id)) id = "hub";
            if (current != null)
            {
                try { current.Dispose(); } catch (Exception e) { Debug.LogException(e); }
                current = null;
            }
            UIRoot.Clear();
            Rig.ResetInput();
            currentId = id;
            current = registry[id]();
            Debug.Log("[MP] Open " + id);
            try { current.Init(this); Debug.Log("[MP] Opened " + id); }
            catch (Exception e) { Debug.LogException(e); }
        }

        public string CurrentId => currentId;
        public ModuleBase Current => current;

        void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame)
            {
                if (current != null && current.HandleBack()) { }
                else if (currentId != "hub") Open("hub");
            }
            current?.Tick(Time.deltaTime);
        }

        /// <summary>True when the screen point (Input System coordinates, origin bottom-left) is over UI.</summary>
        public bool IsOverUI(Vector2 screenPos)
        {
            var panel = UIRoot?.panel;
            if (panel == null) return false;
            var p = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(screenPos.x, Screen.height - screenPos.y));
            var e = panel.Pick(p);
            return e != null && e != UIRoot;
        }

        // ------------------------------------------------------------------ toast
        public void Toast(string msg, float seconds = 3.5f)
        {
            var t = new Label(msg);
            t.AddToClassList("mp-toast");
            t.pickingMode = PickingMode.Ignore;
            UIRoot.Add(t);
            StartCoroutine(RemoveLater(t, seconds));
        }

        IEnumerator RemoveLater(VisualElement e, float s)
        {
            yield return new WaitForSeconds(s);
            e.RemoveFromHierarchy();
        }

        // ------------------------------------------------------------------ automation (screenshots for testing)
        public static bool Flag(string name) => Array.IndexOf(Environment.GetCommandLineArgs(), name) >= 0;

        public static string Arg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == name) return args[i + 1];
            return null;
        }

        IEnumerator AutoShot()
        {
            float delay = float.Parse(Arg("-mpDelay") ?? "5", CultureInfo.InvariantCulture);
            string action = Arg("-mpAction");
            bool offscreen = Application.isBatchMode || Flag("-mpOffscreen");
            int w = int.Parse(Arg("-mpW") ?? "1600"), h = int.Parse(Arg("-mpH") ?? "900");
            RenderTexture sceneRT = null, uiRT = null;
            if (offscreen)
            {
                sceneRT = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
                uiRT = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
                Cam.targetTexture = sceneRT;
                Doc.panelSettings.targetTexture = uiRT;
                Doc.panelSettings.clearColor = true;
                Doc.panelSettings.colorClearValue = new Color(0, 0, 0, 0);
            }
            Debug.Log("[MP] AutoShot waiting offscreen=" + offscreen);
            yield return new WaitForSecondsRealtime(1.0f);
            if (!string.IsNullOrEmpty(action)) current?.RunAutoAction(action);
            yield return new WaitForSecondsRealtime(delay);
            string path = Arg("-mpShot");
            if (offscreen)
            {
                yield return null;
                Cam.Render();
                var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
                RenderTexture.active = sceneRT;
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                var scene = tex.GetPixels32();
                RenderTexture.active = uiRT;
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                var ui = tex.GetPixels32();
                RenderTexture.active = null;
                for (int i = 0; i < scene.Length; i++)
                {
                    float a = ui[i].a / 255f;
                    scene[i] = new Color32((byte)(scene[i].r * (1 - a) + ui[i].r * a), (byte)(scene[i].g * (1 - a) + ui[i].g * a), (byte)(scene[i].b * (1 - a) + ui[i].b * a), 255);
                }
                tex.SetPixels32(scene);
                System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
            }
            else
            {
                yield return new WaitForEndOfFrame();
                ScreenCapture.CaptureScreenshot(path);
            }
            Debug.Log("[MP] screenshot " + path);
            yield return new WaitForSecondsRealtime(1.0f);
            if (Arg("-mpQuit") != null) Application.Quit();
        }
    }
}
