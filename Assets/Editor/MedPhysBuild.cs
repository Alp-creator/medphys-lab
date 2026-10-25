using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

// Project configuration and player builds (Windows + Android). Usable from the menu or in batch mode.
public static class MedPhysBuild
{
    const string ScenePath = "Assets/Scenes/Main.unity";
    static string BuildRoot => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Builds"));

    [MenuItem("MedPhys/Setup Project")]
    public static void SetupProject()
    {
        MedPhysSetup.CreateMaterials();
        CreatePanelSettings();
        CreateMainScene();
        PlayerSettings.productName = "MedPhys Lab";
        PlayerSettings.companyName = "MedPhysLab";
        PlayerSettings.bundleVersion = "1.0.0";
        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.medphyslab.simulator");
        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Standalone, "com.medphyslab.simulator");
        PlayerSettings.runInBackground = true;
        PlayerSettings.windowsGamepadBackendHint = WindowsGamepadBackendHint.WindowsGamepadBackendHintXInput;
        PlayerSettings.defaultScreenWidth = 1600;
        PlayerSettings.defaultScreenHeight = 900;
        PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
        PlayerSettings.resizableWindow = true;
        PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
        PlayerSettings.allowedAutorotateToPortrait = false;
        PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
        PlayerSettings.allowedAutorotateToLandscapeLeft = true;
        PlayerSettings.allowedAutorotateToLandscapeRight = true;
        PlayerSettings.SplashScreen.show = false;
        PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
        PlayerSettings.Android.bundleVersionCode = 1;
        // Unity 6.6 crashes while patching the Windows .exe icon, so the default icon is only set for Android builds
        PlayerSettings.SetIcons(NamedBuildTarget.Unknown, new Texture2D[0], IconKind.Any);
        AssetDatabase.SaveAssets();
        Debug.Log("[MP] project configured");
    }

    static void CreatePanelSettings()
    {
        const string path = "Assets/Resources/UI/PanelSettings.asset";
        var ps = AssetDatabase.LoadAssetAtPath<PanelSettings>(path);
        if (ps == null)
        {
            ps = ScriptableObject.CreateInstance<PanelSettings>();
            AssetDatabase.CreateAsset(ps, path);
        }
        ps.themeStyleSheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>("Assets/Resources/UI/RuntimeTheme.tss");
        ps.scaleMode = PanelScaleMode.ScaleWithScreenSize;
        ps.referenceResolution = new Vector2Int(1920, 1080);
        ps.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
        ps.match = 1f;
        EditorUtility.SetDirty(ps);
        AssetDatabase.SaveAssets();
    }

    static void CreateMainScene()
    {
        Directory.CreateDirectory("Assets/Scenes");
        if (!File.Exists(ScenePath))
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorSceneManager.SaveScene(scene, ScenePath);
        }
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
    }

    [MenuItem("MedPhys/Build Windows")]
    public static void BuildWindows()
    {
        // URP strips renderer-feature resources (e.g. SSAO) using the active platform's quality levels
        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.StandaloneWindows64)
            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows64);
        SetupProject();
        string dir = Path.Combine(BuildRoot, "Windows");
        Directory.CreateDirectory(dir);
        var opts = new BuildPlayerOptions
        {
            scenes = new[] { ScenePath },
            locationPathName = Path.Combine(dir, "MedPhysLab.exe"),
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None,
        };
        Report(BuildPipeline.BuildPlayer(opts));
    }

    public static string SwitchToAndroid() => EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android) ? "android" : "failed";
    public static string SwitchToWindows() => EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows64) ? "windows" : "failed";

    [MenuItem("MedPhys/Build Android APK")]
    public static void BuildAndroid()
    {
        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
        SetupProject();
        // IL2CPP compiles the generated C++ with one clang per build thread; on a machine with little free
        // memory 16 parallel compilers exhaust the page file, so the native build is throttled.
        string threads = System.Environment.GetEnvironmentVariable("MP_BUILD_THREADS") ?? "2";
        System.Environment.SetEnvironmentVariable("BEE_BUILD_THREADS", threads);
        PlayerSettings.SetIl2CppCodeGeneration(NamedBuildTarget.Android, Il2CppCodeGeneration.OptimizeSize);
        // Gradle runs with a 4 GB heap by default, which does not fit next to an open editor on this machine
        int heap = int.TryParse(System.Environment.GetEnvironmentVariable("MP_GRADLE_HEAP_MB"), out var h) ? h : 1280;
        UnityEditor.Android.AndroidExternalToolsSettings.maxJvmHeapSize = heap;
        EditorUtility.UnloadUnusedAssetsImmediate();
        System.GC.Collect();
        var icon = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Icon/AppIcon.png");
        if (icon != null) PlayerSettings.SetIcons(NamedBuildTarget.Unknown, new[] { icon }, IconKind.Any);
        EditorUserBuildSettings.buildAppBundle = false;
        // the side-loaded itch.io APK is signed with the local debug key; the Play upload key (AAB build) needs
        // passwords Unity does not keep, and without them the editor stops the build with a modal dialog
        bool customKey = PlayerSettings.Android.useCustomKeystore;
        PlayerSettings.Android.useCustomKeystore = false;
        string dir = Path.Combine(BuildRoot, "Android");
        Directory.CreateDirectory(dir);
        var opts = new BuildPlayerOptions
        {
            scenes = new[] { ScenePath },
            locationPathName = Path.Combine(dir, "MedPhysLab.apk"),
            target = BuildTarget.Android,
            options = BuildOptions.None,
        };
        try { Report(BuildPipeline.BuildPlayer(opts)); }
        finally { PlayerSettings.Android.useCustomKeystore = customKey; }
    }

    // Builds the Play Console artifact. Credentials are deliberately supplied at build time
    // through environment variables so no signing secret is committed to the project.
    [MenuItem("MedPhys/Build Android App Bundle (Play)")]
    public static void BuildAndroidAppBundle()
    {
        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
        SetupProject();

        string keystore = System.Environment.GetEnvironmentVariable("MP_ANDROID_KEYSTORE");
        string storePass = System.Environment.GetEnvironmentVariable("MP_ANDROID_KEYSTORE_PASS");
        string alias = System.Environment.GetEnvironmentVariable("MP_ANDROID_KEY_ALIAS") ?? "medphyslab-upload";
        string keyPass = System.Environment.GetEnvironmentVariable("MP_ANDROID_KEY_PASS") ?? storePass;
        if (string.IsNullOrWhiteSpace(keystore) || string.IsNullOrWhiteSpace(storePass) || string.IsNullOrWhiteSpace(keyPass))
            throw new BuildFailedException("Play bundle requires MP_ANDROID_KEYSTORE, MP_ANDROID_KEYSTORE_PASS and MP_ANDROID_KEY_PASS.");

        string threads = System.Environment.GetEnvironmentVariable("MP_BUILD_THREADS") ?? "2";
        System.Environment.SetEnvironmentVariable("BEE_BUILD_THREADS", threads);
        PlayerSettings.SetIl2CppCodeGeneration(NamedBuildTarget.Android, Il2CppCodeGeneration.OptimizeSize);
        int heap = int.TryParse(System.Environment.GetEnvironmentVariable("MP_GRADLE_HEAP_MB"), out var h) ? h : 1280;
        UnityEditor.Android.AndroidExternalToolsSettings.maxJvmHeapSize = heap;
        PlayerSettings.Android.useCustomKeystore = true;
        PlayerSettings.Android.keystoreName = keystore;
        PlayerSettings.Android.keystorePass = storePass;
        PlayerSettings.Android.keyaliasName = alias;
        PlayerSettings.Android.keyaliasPass = keyPass;
        EditorUserBuildSettings.buildAppBundle = true;
        EditorUtility.UnloadUnusedAssetsImmediate();
        System.GC.Collect();

        string dir = Path.Combine(BuildRoot, "Android");
        Directory.CreateDirectory(dir);
        var opts = new BuildPlayerOptions
        {
            scenes = new[] { ScenePath },
            locationPathName = Path.Combine(dir, "MedPhysLab.aab"),
            target = BuildTarget.Android,
            options = BuildOptions.None,
        };
        Report(BuildPipeline.BuildPlayer(opts));
    }

    static void Report(BuildReport r)
    {
        var s = r.summary;
        Debug.Log($"[MP] Build {s.result} -> {s.outputPath} size={s.totalSize / (1024 * 1024)} MB time={s.totalTime} errors={s.totalErrors} warnings={s.totalWarnings}");
        if (s.result != BuildResult.Succeeded)
        {
            foreach (var step in r.steps)
                foreach (var m in step.messages.Where(m => m.type == LogType.Error || m.type == LogType.Exception))
                    Debug.Log("[MP] BUILD ERROR: " + m.content);
            if (Application.isBatchMode) EditorApplication.Exit(1);
        }
    }
}

public static class MedPhysIconFix
{
    public static void Reimport()
    {
        AssetDatabase.ImportAsset("Assets/Icon/AppIcon.png", ImportAssetOptions.ForceUpdate);
        UnityEngine.Debug.Log("[MP] icon reimported");
    }
}

public static class MedPhysModelFix
{
    /// <summary>Re-imports the patient models (e.g. after changing their import settings).</summary>
    public static void ReimportPatients()
    {
        foreach (var g in AssetDatabase.FindAssets("Patient_ t:Model", new[] { "Assets/Resources/Models" }))
            AssetDatabase.ImportAsset(AssetDatabase.GUIDToAssetPath(g), ImportAssetOptions.ForceUpdate);
        UnityEngine.Debug.Log("[MP] patients reimported");
    }
}
