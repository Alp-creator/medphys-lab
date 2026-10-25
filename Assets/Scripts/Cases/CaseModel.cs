using System;
using System.Collections.Generic;
using UnityEngine;
using MedPhys.UI;
using static MedPhys.Core.Loc;

namespace MedPhys.Cases
{
    public enum Grade { Good, Ok, Bad }

    /// <summary>Outcome of one decision step.</summary>
    public sealed class StepResult
    {
        public int Points, Max;
        public Grade G;
        public string Choice = "";                      // what the player chose / set (one line)
        public readonly List<string> Lines = new List<string>();   // explanation shown in feedback and debrief
        public double DoseMSv;                          // effective dose delivered to the patient by this step
        public float Quality = -1;                      // diagnostic / treatment quality 0..1 (-1 = n/a)
        public StepResult Line(string tr, string en) { Lines.Add(T(tr, en)); return this; }
        public static Grade GradeOf(int p, int max) => p >= max * 0.85f ? Grade.Good : p >= max * 0.45f ? Grade.Ok : Grade.Bad;
    }

    public abstract class Step
    {
        public string Tr, En, PromptTr, PromptEn;
        public Func<CaseRun, string> PromptFn;          // dynamic prompt (overrides PromptTr/En)
        public string SayTr, SayEn;                     // patient / colleague line when the step opens
        public Func<CaseRun, string> SayFn;
        public string AfterTr, AfterEn;                 // narrative shown after submitting (e.g. test results)
        public Func<CaseRun, string> AfterFn;
        public string Anim;                             // scene action when the step opens / runs ("ct-in", "linac", ...)
        public int Max = 10;
        public string Title => T(Tr, En);
        public string Prompt(CaseRun r) => PromptFn != null ? PromptFn(r) : T(PromptTr, PromptEn);
        public string Say(CaseRun r) => SayFn != null ? SayFn(r) : SayTr != null ? T(SayTr, SayEn) : null;
        public string After(CaseRun r) => AfterFn != null ? AfterFn(r) : AfterTr != null ? T(AfterTr, AfterEn) : null;
    }

    public sealed class Option
    {
        public string Tr, En, FbTr, FbEn;
        public Func<CaseRun, string> TextFn, FbFn;
        public int Points;
        public string Text(CaseRun r) => TextFn != null ? TextFn(r) : T(Tr, En);
        public string Feedback(CaseRun r) => FbFn != null ? FbFn(r) : T(FbTr, FbEn);
        public Option(int points, string tr, string en, string fbTr, string fbEn) { Points = points; Tr = tr; En = en; FbTr = fbTr; FbEn = fbEn; }
    }

    /// <summary>Single answer (or "select all that apply" when Multi) question with per-option points and feedback.</summary>
    public sealed class ChoiceStep : Step
    {
        public readonly List<Option> Options = new List<Option>();
        public bool Multi;
        public Action<CaseRun, List<int>> OnChosen;
        public ChoiceStep Add(int pts, string tr, string en, string fbTr, string fbEn) { Options.Add(new Option(pts, tr, en, fbTr, fbEn)); return this; }

        public StepResult Evaluate(CaseRun run, List<int> picked)
        {
            var r = new StepResult { Max = Max };
            int pts = 0;
            var names = new List<string>();
            foreach (int i in picked) { pts += Options[i].Points; names.Add(Options[i].Text(run)); }
            if (!Multi)
            {
                int best = 0; foreach (var o in Options) best = Math.Max(best, o.Points);
                r.Max = best;
            }
            r.Points = Mathf.Clamp(pts, 0, r.Max);
            r.Choice = string.Join(" • ", names);
            foreach (int i in picked) r.Lines.Add(Options[i].Feedback(run));
            if (Multi)
                for (int i = 0; i < Options.Count; i++)
                    if (!picked.Contains(i) && Options[i].Points > 0) r.Lines.Add(T("Atlandı: ", "Missed: ") + Options[i].Text(run) + " — " + Options[i].Feedback(run));
            r.G = StepResult.GradeOf(r.Points, r.Max);
            OnChosen?.Invoke(run, picked);
            return r;
        }
    }

    public sealed class Param
    {
        public string Key, Tr, En;
        public float Min, Max, Value, Snap;
        public Func<float, string> Fmt;
        public string[] OptTr, OptEn;                   // segmented choice when set
        public bool Toggle;
        public string Label => T(Tr, En);
        public static Param Slider(string key, string tr, string en, float min, float max, float v, float snap, Func<float, string> fmt)
            => new Param { Key = key, Tr = tr, En = en, Min = min, Max = max, Value = v, Snap = snap, Fmt = fmt };
        public static Param Seg(string key, string tr, string en, int v, string[] optTr, string[] optEn)
            => new Param { Key = key, Tr = tr, En = en, Value = v, OptTr = optTr, OptEn = optEn ?? optTr };
        public static Param Check(string key, string tr, string en, bool v)
            => new Param { Key = key, Tr = tr, En = en, Value = v ? 1 : 0, Toggle = true };
    }

    /// <summary>Current parameter values plus the outputs of the last simulation run.</summary>
    public sealed class Vals
    {
        public readonly Dictionary<string, float> V = new Dictionary<string, float>();
        public readonly Dictionary<string, double> Sim = new Dictionary<string, double>();
        public bool HasSim;
        public float this[string k] => V.TryGetValue(k, out var x) ? x : 0f;
        public int I(string k) => Mathf.RoundToInt(this[k]);
        public bool B(string k) => this[k] > 0.5f;
        public double S(string k) => Sim.TryGetValue(k, out var x) ? x : double.NaN;
        public Vals Clone()
        {
            var c = new Vals { HasSim = HasSim };
            foreach (var kv in V) c.V[kv.Key] = kv.Value;
            foreach (var kv in Sim) c.Sim[kv.Key] = kv.Value;
            return c;
        }
    }

    public struct Readout
    {
        public string Label, Value; public int Level;
        public Readout(string label, string value, int level = -1) { Label = label; Value = value; Level = level; }
    }

    /// <summary>A long-running simulation behind a parameter step (radiograph, CT slice, MR image).</summary>
    public abstract class CaseSim
    {
        public bool Running;
        public float Progress;
        public Texture2D Tex;
        public string MarkL = "", MarkR = "";
        public abstract string ButtonTr { get; }
        public abstract string ButtonEn { get; }
        public abstract void Start(Vals v);
        /// <summary>Returns true once finished; then the outputs are in v.Sim and Tex is updated.</summary>
        public abstract bool Poll(Vals v);
    }

    /// <summary>Sliders / segmented choices with live physics read-outs, an optional chart and an optional image simulation.</summary>
    public sealed class ParamStep : Step
    {
        public readonly List<Param> Params = new List<Param>();
        public Func<Vals, List<Readout>> Live;
        public Func<Vals, CaseRun, StepResult> Evaluate;
        public Action<Chart> ChartInit;
        public Action<Vals, Chart> Plot;
        public float ChartHeight = 220;
        public Func<CaseSim> MakeSim;
        public bool RequireSim = true;                  // with a sim: the current settings must have been run before submitting
        public ParamStep P(Param p) { Params.Add(p); return this; }
    }

    /// <summary>A text-only beat (results arriving, a colleague explaining) without points.</summary>
    public sealed class InfoStep : Step
    {
        public InfoStep() { Max = 0; }
    }

    public sealed class CaseDef
    {
        public string Id, Modality;
        public PatientInfo P;
        public string TitleTr, TitleEn, ComplaintTr, ComplaintEn, RequestTr, RequestEn, IntroTr, IntroEn;
        public readonly List<(string tr, string en)> History = new List<(string, string)>();
        public readonly List<(string tr, string en, string v)> Vitals = new List<(string, string, string)>();
        public readonly List<(string tr, string en)> Learn = new List<(string, string)>();
        public readonly List<string> Refs = new List<string>();
        public readonly List<Step> Steps = new List<Step>();
        public int Difficulty = 1;
        public string Title => T(TitleTr, TitleEn);
        public string Complaint => T(ComplaintTr, ComplaintEn);
        public int MaxPoints { get { int m = 0; foreach (var s in Steps) m += s.Max; return m; } }

        public int BestStars => PlayerPrefs.GetInt("case_stars_" + Id, 0);
        public int BestPct => PlayerPrefs.GetInt("case_pct_" + Id, -1);
        public void SaveBest(int stars, int pct)
        {
            if (stars > BestStars) PlayerPrefs.SetInt("case_stars_" + Id, stars);
            if (pct > BestPct) PlayerPrefs.SetInt("case_pct_" + Id, pct);
            PlayerPrefs.Save();
        }
    }

    /// <summary>State of one play-through of a case.</summary>
    public sealed class CaseRun
    {
        public readonly CaseDef Def;
        public int Index;
        public readonly List<StepResult> Results = new List<StepResult>();
        public readonly Dictionary<string, double> Memo = new Dictionary<string, double>();
        public double DoseMSv;
        public float Quality = -1;
        public float StartTime;
        public CaseRun(CaseDef d) { Def = d; StartTime = Time.realtimeSinceStartup; }
        public Step Current => Index < Def.Steps.Count ? Def.Steps[Index] : null;
        public double M(string k, double fallback = 0) => Memo.TryGetValue(k, out var v) ? v : fallback;

        public int Points { get { int p = 0; foreach (var r in Results) p += r.Points; return p; } }
        public int MaxSoFar { get { int p = 0; foreach (var r in Results) p += r.Max; return p; } }
        public int Percent { get { int m = 0; foreach (var r in Results) m += r.Max; return m == 0 ? 0 : Mathf.RoundToInt(100f * Points / m); } }
        public int Stars => Percent >= 85 ? 3 : Percent >= 65 ? 2 : Percent >= 40 ? 1 : 0;

        public void Add(StepResult r)
        {
            Results.Add(r);
            DoseMSv += r.DoseMSv;
            if (r.Quality >= 0) Quality = r.Quality;
        }
    }
}
