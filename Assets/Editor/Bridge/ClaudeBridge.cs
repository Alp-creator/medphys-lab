// Development helper: lets an external process drive the Unity Editor by dropping JSON
// command files into <project>/Bridge/inbox. Results are written to <project>/Bridge/outbox.
// Editor-only; never included in player builds.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

[InitializeOnLoad]
public static class ClaudeBridge
{
    static readonly string Root = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Bridge"));
    static readonly string Inbox = Path.Combine(Root, "inbox");
    static readonly string Outbox = Path.Combine(Root, "outbox");
    static readonly string LogFile = Path.Combine(Root, "unity_log.txt");
    static readonly string CompileFile = Path.Combine(Root, "compile.txt");
    static double _nextPoll;
    static System.Threading.Timer _kick;

    [Serializable] class Cmd { public string id; public string cmd; public string arg; public string arg2; }

    static ClaudeBridge()
    {
        if (Application.isBatchMode) return;
        Directory.CreateDirectory(Inbox);
        Directory.CreateDirectory(Outbox);
        EditorApplication.update += Poll;
        Application.logMessageReceivedThreaded -= OnLog;
        Application.logMessageReceivedThreaded += OnLog;
        CompilationPipeline.compilationStarted -= OnCompileStarted;
        CompilationPipeline.compilationStarted += OnCompileStarted;
        CompilationPipeline.assemblyCompilationFinished -= OnAssemblyCompiled;
        CompilationPipeline.assemblyCompilationFinished += OnAssemblyCompiled;
        File.WriteAllText(Path.Combine(Root, "status.txt"), "loaded " + DateTime.Now.ToString("o"));
        // Keep the editor ticking while it is in the background so commands are processed.
        _kick?.Dispose();
        _kick = new System.Threading.Timer(_ => { try { EditorApplication.QueuePlayerLoopUpdate(); } catch { } }, null, 500, 500);
        AssemblyReloadEvents.beforeAssemblyReload += () => { _kick?.Dispose(); _kick = null; };
    }

    static void OnLog(string condition, string stackTrace, LogType type)
    {
        try
        {
            var sb = new StringBuilder();
            sb.Append('[').Append(DateTime.Now.ToString("HH:mm:ss")).Append("][").Append(type).Append("] ").AppendLine(condition);
            if (type == LogType.Exception || type == LogType.Error)
                sb.AppendLine(stackTrace);
            lock (LogFile) File.AppendAllText(LogFile, sb.ToString());
        }
        catch { }
    }

    static void OnCompileStarted(object o)
    {
        try { File.WriteAllText(CompileFile, "compiling " + DateTime.Now.ToString("o") + "\n"); } catch { }
    }

    static void OnAssemblyCompiled(string asm, CompilerMessage[] msgs)
    {
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine("assembly " + Path.GetFileName(asm));
            foreach (var m in msgs)
                if (m.type == CompilerMessageType.Error)
                    sb.AppendLine("ERROR " + m.file + ":" + m.line + " " + m.message);
            foreach (var m in msgs)
                if (m.type == CompilerMessageType.Warning && !m.message.Contains("CS0649") && !m.message.Contains("CS0414"))
                    sb.AppendLine("WARN " + m.file + ":" + m.line + " " + m.message);
            File.AppendAllText(CompileFile, sb.ToString());
        }
        catch { }
    }

    static void Poll()
    {
        if (EditorApplication.timeSinceStartup < _nextPoll) return;
        _nextPoll = EditorApplication.timeSinceStartup + 0.25;
        string[] files;
        try { files = Directory.GetFiles(Inbox, "*.json"); } catch { return; }
        foreach (var f in files.OrderBy(x => x))
        {
            string text;
            try { text = File.ReadAllText(f); File.Delete(f); } catch { continue; }
            var cmd = JsonUtility.FromJson<Cmd>(text);
            string result;
            try { result = Execute(cmd); }
            catch (Exception e) { result = "EXCEPTION " + e; }
            try { File.WriteAllText(Path.Combine(Outbox, (cmd.id ?? "noid") + ".txt"), result ?? "null"); } catch { }
        }
    }

    static string Execute(Cmd c)
    {
        switch (c.cmd)
        {
            case "ping":
                return "pong compiling=" + EditorApplication.isCompiling + " playing=" + EditorApplication.isPlaying + " updating=" + EditorApplication.isUpdating;
            case "refresh":
                AssetDatabase.Refresh();
                return "refreshed compiling=" + EditorApplication.isCompiling;
            case "recompile":
                CompilationPipeline.RequestScriptCompilation();
                return "requested";
            case "play":
                EditorApplication.isPlaying = true;
                return "play requested";
            case "stop":
                EditorApplication.isPlaying = false;
                return "stop requested";
            case "invoke":
                return Invoke(c.arg, c.arg2);
            case "screenshot":
                ScreenCapture.CaptureScreenshot(c.arg);
                return "requested " + c.arg;
            case "clearlog":
                File.WriteAllText(LogFile, "");
                return "cleared";
            default:
                return "unknown command " + c.cmd;
        }
    }

    // arg = "Namespace.Type.Method", arg2 = optional single string argument
    static string Invoke(string fullName, string argument)
    {
        int dot = fullName.LastIndexOf('.');
        string typeName = fullName.Substring(0, dot), methodName = fullName.Substring(dot + 1);
        Type t = AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => a.GetType(typeName, false))
            .FirstOrDefault(x => x != null);
        if (t == null) return "type not found " + typeName;
        var m = t.GetMethod(methodName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        if (m == null) return "method not found " + methodName;
        object[] args = m.GetParameters().Length == 0 ? null : new object[] { argument };
        var r = m.Invoke(null, args);
        return r == null ? "ok" : r.ToString();
    }
}
