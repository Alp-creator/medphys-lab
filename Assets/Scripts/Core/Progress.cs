using System.Collections.Generic;
using UnityEngine;

namespace MedPhys.Core
{
    /// <summary>Completed learning tasks, stored in PlayerPrefs ("module.task" keys).</summary>
    public static class Progress
    {
        static readonly HashSet<string> done = new HashSet<string>();

        public static void Load()
        {
            done.Clear();
            var s = PlayerPrefs.GetString("mp_done", "");
            foreach (var k in s.Split('|')) if (k.Length > 0) done.Add(k);
        }

        public static bool IsDone(string module, string task) => done.Contains(module + "." + task);

        /// <summary>Marks a task complete. Returns true the first time.</summary>
        public static bool Complete(string module, string task)
        {
            if (!done.Add(module + "." + task)) return false;
            PlayerPrefs.SetString("mp_done", string.Join("|", done));
            PlayerPrefs.Save();
            return true;
        }

        public static int Count(string module)
        {
            int n = 0;
            foreach (var k in done) if (k.StartsWith(module + ".")) n++;
            return n;
        }

        public static void ResetAll()
        {
            done.Clear();
            PlayerPrefs.DeleteKey("mp_done");
        }
    }
}
