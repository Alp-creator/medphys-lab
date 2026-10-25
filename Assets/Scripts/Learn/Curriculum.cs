using System;
using System.Collections.Generic;
using UnityEngine;
using static MedPhys.Core.Loc;

namespace MedPhys.Learn
{
    public enum BlockKind { Heading, Para, Equation, Key, Example, Bullet, Figure, Warning }

    /// <summary>One piece of lesson content. Text is bilingual; equations and figure names are language-neutral.</summary>
    public sealed class Block
    {
        public BlockKind Kind;
        public string Tr, En;
        public string Text => T(Tr, En);
    }

    public sealed class Question
    {
        public string Tr, En, WhyTr, WhyEn;
        public readonly List<(string tr, string en, bool ok)> Options = new List<(string, string, bool)>();
        public string Text => T(Tr, En);
        public string Why => T(WhyTr, WhyEn);
        public string TopicId, LessonId;
    }

    public sealed class Lesson
    {
        public string Id, Tr, En, Sim, Case;
        public int Minutes = 8;
        public readonly List<Block> Blocks = new List<Block>();
        public readonly List<Question> Quiz = new List<Question>();
        public Topic Topic;
        public string Title => T(Tr, En);
        public string Key => Topic.Id + "." + Id;
    }

    public sealed class Topic
    {
        public string Id, Tag, Tr, En, DescTr, DescEn, Sim;
        public readonly List<Lesson> Lessons = new List<Lesson>();
        public readonly List<(string tr, string en)> Formulas = new List<(string, string)>();
        public readonly List<(string termTr, string termEn, string defTr, string defEn)> Glossary = new List<(string, string, string, string)>();
        public string Title => T(Tr, En);
        public string Desc => T(DescTr, DescEn);
    }

    /// <summary>
    /// The learning-centre curriculum, read from Resources/Learn/*.txt. Line format (TR || EN on one line):
    /// <code>
    /// @topic id | TAG | Türkçe başlık | English title | simModuleId
    /// @desc açıklama || description
    /// @lesson id | Türkçe | English | minutes
    /// @sim moduleId      @case caseId
    /// ## başlık || heading         plain line = paragraph
    /// $ equation                    &gt; key point      ! worked example      * bullet      !! warning
    /// %fig figureName
    /// ? soru || question   + doğru || correct   - yanlış || wrong   = açıklama || explanation
    /// @formula açıklama || label :: equation
    /// @term terim || term :: tanım || definition
    /// </code>
    /// </summary>
    public static class Curriculum
    {
        static List<Topic> topics;
        public static List<Topic> Topics => topics ?? (topics = Load());

        public static Topic FindTopic(string id) => Topics.Find(t => t.Id == id);

        public static Lesson FindLesson(string key)
        {
            var parts = key.Split('.');
            if (parts.Length != 2) return null;
            return FindTopic(parts[0])?.Lessons.Find(l => l.Id == parts[1]);
        }

        public static IEnumerable<Question> AllQuestions()
        {
            foreach (var t in Topics) foreach (var l in t.Lessons) foreach (var q in l.Quiz) yield return q;
        }

        static (string, string) Pair(string s)
        {
            int i = s.IndexOf("||", StringComparison.Ordinal);
            if (i < 0) return (s.Trim(), s.Trim());
            return (s.Substring(0, i).Trim(), s.Substring(i + 2).Trim());
        }

        static List<Topic> Load()
        {
            var list = new List<Topic>();
            var assets = Resources.LoadAll<TextAsset>("Learn");
            Array.Sort(assets, (a, b) => string.CompareOrdinal(a.name, b.name));
            foreach (var a in assets)
            {
                try { Parse(a.text, list); }
                catch (Exception e) { Debug.LogError("[MP] curriculum " + a.name + ": " + e.Message); }
            }
            return list;
        }

        static void Parse(string text, List<Topic> list)
        {
            Topic topic = null; Lesson lesson = null; Question q = null;
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.TrimEnd('\r').Trim();
                if (line.Length == 0 || line.StartsWith("//")) continue;
                if (line.StartsWith("@topic "))
                {
                    var f = line.Substring(7).Split('|');
                    topic = new Topic { Id = f[0].Trim(), Tag = f[1].Trim(), Tr = f[2].Trim(), En = f[3].Trim(), Sim = f.Length > 4 ? f[4].Trim() : null };
                    list.Add(topic); lesson = null; q = null; continue;
                }
                if (topic == null) continue;
                if (line.StartsWith("@desc ")) { var (tr, en) = Pair(line.Substring(6)); topic.DescTr = tr; topic.DescEn = en; continue; }
                if (line.StartsWith("@formula "))
                {
                    var body = line.Substring(9); int k = body.IndexOf("::", StringComparison.Ordinal);
                    var (tr, en) = Pair(k >= 0 ? body.Substring(0, k) : body);
                    string eq = k >= 0 ? body.Substring(k + 2).Trim() : "";
                    topic.Formulas.Add((tr + "\n" + eq, en + "\n" + eq));
                    continue;
                }
                if (line.StartsWith("@term "))
                {
                    var body = line.Substring(6); int k = body.IndexOf("::", StringComparison.Ordinal);
                    if (k < 0) continue;
                    var (ttr, ten) = Pair(body.Substring(0, k)); var (dtr, den) = Pair(body.Substring(k + 2));
                    topic.Glossary.Add((ttr, ten, dtr, den));
                    continue;
                }
                if (line.StartsWith("@lesson "))
                {
                    var f = line.Substring(8).Split('|');
                    lesson = new Lesson { Id = f[0].Trim(), Tr = f[1].Trim(), En = f[2].Trim(), Topic = topic, Sim = topic.Sim };
                    if (f.Length > 3 && int.TryParse(f[3].Trim(), out int m)) lesson.Minutes = m;
                    topic.Lessons.Add(lesson); q = null; continue;
                }
                if (lesson == null) continue;
                if (line.StartsWith("@sim ")) { lesson.Sim = line.Substring(5).Trim(); continue; }
                if (line.StartsWith("@case ")) { lesson.Case = line.Substring(6).Trim(); continue; }
                char c = line[0];
                if (c == '?') { var (tr, en) = Pair(line.Substring(1)); q = new Question { Tr = tr, En = en, TopicId = topic.Id, LessonId = lesson.Id }; lesson.Quiz.Add(q); continue; }
                if ((c == '+' || c == '-') && q != null && line.Length > 1 && line[1] == ' ') { var (tr, en) = Pair(line.Substring(1)); q.Options.Add((tr, en, c == '+')); continue; }
                if (c == '=' && q != null) { var (tr, en) = Pair(line.Substring(1)); q.WhyTr = tr; q.WhyEn = en; continue; }
                q = null;
                Block b;
                if (line.StartsWith("## ")) { var (tr, en) = Pair(line.Substring(3)); b = new Block { Kind = BlockKind.Heading, Tr = tr, En = en }; }
                else if (line.StartsWith("%fig ")) { var n = line.Substring(5).Trim(); b = new Block { Kind = BlockKind.Figure, Tr = n, En = n }; }
                else if (c == '$') { var eq = line.Substring(1).Trim(); b = new Block { Kind = BlockKind.Equation, Tr = eq, En = eq }; }
                else if (line.StartsWith("!!")) { var (tr, en) = Pair(line.Substring(2)); b = new Block { Kind = BlockKind.Warning, Tr = tr, En = en }; }
                else if (c == '!') { var (tr, en) = Pair(line.Substring(1)); b = new Block { Kind = BlockKind.Example, Tr = tr, En = en }; }
                else if (c == '>') { var (tr, en) = Pair(line.Substring(1)); b = new Block { Kind = BlockKind.Key, Tr = tr, En = en }; }
                else if (c == '*') { var (tr, en) = Pair(line.Substring(1)); b = new Block { Kind = BlockKind.Bullet, Tr = tr, En = en }; }
                else { var (tr, en) = Pair(line); b = new Block { Kind = BlockKind.Para, Tr = tr, En = en }; }
                lesson.Blocks.Add(b);
            }
        }
    }

    /// <summary>Learning progress (PlayerPrefs): lessons read, best quiz score per lesson, best exam score.</summary>
    public static class LearnProgress
    {
        public static bool Read(Lesson l) => PlayerPrefs.GetInt("learn_read_" + l.Key, 0) == 1;
        public static void MarkRead(Lesson l) { if (!Read(l)) { PlayerPrefs.SetInt("learn_read_" + l.Key, 1); PlayerPrefs.Save(); } }
        public static int QuizBest(Lesson l) => PlayerPrefs.GetInt("learn_quiz_" + l.Key, -1);
        public static bool Mastered(Lesson l) => l.Quiz.Count > 0 && QuizBest(l) >= l.Quiz.Count;
        public static void SaveQuiz(Lesson l, int correct) { if (correct > QuizBest(l)) { PlayerPrefs.SetInt("learn_quiz_" + l.Key, correct); PlayerPrefs.Save(); } }
        public static int ExamBest { get => PlayerPrefs.GetInt("learn_exam_best", -1); set { if (value > ExamBest) { PlayerPrefs.SetInt("learn_exam_best", value); PlayerPrefs.Save(); } } }

        /// <summary>0..1: half for reading, half for passing the lesson quiz.</summary>
        public static float Of(Topic t)
        {
            if (t.Lessons.Count == 0) return 0;
            float s = 0;
            foreach (var l in t.Lessons) s += (Read(l) ? 0.5f : 0) + (Mastered(l) || l.Quiz.Count == 0 && Read(l) ? 0.5f : 0);
            return s / t.Lessons.Count;
        }

        public static float Overall()
        {
            float s = 0; int n = 0;
            foreach (var t in Curriculum.Topics) { s += Of(t) * t.Lessons.Count; n += t.Lessons.Count; }
            return n == 0 ? 0 : s / n;
        }

        public static void ResetAll()
        {
            foreach (var t in Curriculum.Topics)
                foreach (var l in t.Lessons) { PlayerPrefs.DeleteKey("learn_read_" + l.Key); PlayerPrefs.DeleteKey("learn_quiz_" + l.Key); }
            PlayerPrefs.DeleteKey("learn_exam_best");
            PlayerPrefs.Save();
        }
    }
}
