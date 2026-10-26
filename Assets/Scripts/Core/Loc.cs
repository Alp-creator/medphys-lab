using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace MedPhys.Core
{
    /// <summary>
    /// Multi-language text helper. Every UI string is written as T("tr", "en"); Turkish and English come straight from
    /// the code, every other language from Resources/Lang/&lt;code&gt;.txt, keyed by the English text (numbers masked as '#',
    /// so "Score: 3 / 5" uses the translation of "Score: # / #"). Missing translations fall back to English.
    /// Keys are produced by Bridge/i18n_extract.py.
    /// </summary>
    public static class Loc
    {
        public sealed class Language
        {
            public string Code, Native;
            public bool Rtl, Complex;     // right-to-left script / needs the shaping text generator
            public SystemLanguage System;
        }

        public static readonly Language[] Languages =
        {
            L("tr", "Türkçe", SystemLanguage.Turkish), L("en", "English", SystemLanguage.English),
            L("de", "Deutsch", SystemLanguage.German), L("fr", "Français", SystemLanguage.French),
            L("es", "Español", SystemLanguage.Spanish), L("pt", "Português", SystemLanguage.Portuguese),
            L("it", "Italiano", SystemLanguage.Italian), L("nl", "Nederlands", SystemLanguage.Dutch),
            L("pl", "Polski", SystemLanguage.Polish), L("cs", "Čeština", SystemLanguage.Czech),
            L("ro", "Română", SystemLanguage.Romanian), L("hu", "Magyar", SystemLanguage.Hungarian),
            L("sv", "Svenska", SystemLanguage.Swedish), L("da", "Dansk", SystemLanguage.Danish),
            L("nb", "Norsk", SystemLanguage.Norwegian), L("fi", "Suomi", SystemLanguage.Finnish),
            L("el", "Ελληνικά", SystemLanguage.Greek), L("ru", "Русский", SystemLanguage.Russian),
            L("uk", "Українська", SystemLanguage.Ukrainian), L("az", "Azərbaycanca", SystemLanguage.Unknown),
            L("id", "Bahasa Indonesia", SystemLanguage.Indonesian), L("vi", "Tiếng Việt", SystemLanguage.Vietnamese),
            L("ar", "العربية", SystemLanguage.Arabic, rtl: true, complex: true), L("fa", "فارسی", SystemLanguage.Unknown, rtl: true, complex: true),
            L("hi", "हिन्दी", SystemLanguage.Hindi, complex: true),
            L("bn", "বাংলা", SystemLanguage.Unknown, complex: true), L("th", "ไทย", SystemLanguage.Thai, complex: true),
            L("zh", "简体中文", SystemLanguage.ChineseSimplified), L("ja", "日本語", SystemLanguage.Japanese),
            L("ko", "한국어", SystemLanguage.Korean),
        };

        static Language L(string code, string native, SystemLanguage sys, bool rtl = false, bool complex = false)
            => new Language { Code = code, Native = native, System = sys, Rtl = rtl, Complex = complex };

        public static string Code { get; private set; } = "en";
        public static Language Current => Find(Code) ?? Languages[1];
        /// <summary>Every language except Turkish is built on the English text.</summary>
        public static bool En => Code != "tr";
        public static event Action Changed;

        public static Language Find(string code) => Array.Find(Languages, l => l.Code == code);

        /// <summary>-mpLogMissing &lt;file&gt;: English keys looked up without a translation, written on quit (coverage testing).</summary>
        static HashSet<string> missing;

        public static void Init(bool turkishRegion = false)
        {
            var args = Environment.GetCommandLineArgs();
            int m = Array.IndexOf(args, "-mpLogMissing");
            if (m >= 0 && m + 1 < args.Length && missing == null)
            {
                missing = new HashSet<string>();
                string path = args[m + 1];
                Application.quitting += () => System.IO.File.WriteAllLines(path, missing);
            }
            int i = Array.IndexOf(args, "-mpLang");
            string code = null;
            if (Array.IndexOf(args, "-mpTR") >= 0) code = "tr";
            else if (Array.IndexOf(args, "-mpEN") >= 0) code = "en";
            else if (i >= 0 && i + 1 < args.Length) code = args[i + 1];
            else if (PlayerPrefs.HasKey("lang")) code = PlayerPrefs.GetString("lang");
            else if (PlayerPrefs.HasKey("lang_en")) code = PlayerPrefs.GetInt("lang_en") == 1 ? "en" : "tr";   // older versions
            else
            {
                if (turkishRegion) code = "tr";
                else
                {
                    var sys = Application.systemLanguage;
                    var hit = sys == SystemLanguage.ChineseTraditional || sys == SystemLanguage.Chinese ? Find("zh") : Array.Find(Languages, l => l.System == sys && sys != SystemLanguage.Unknown);
                    code = hit != null ? hit.Code : "en";
                }
            }
            Apply(Find(code) != null ? code : "en");
        }

        public static void Set(string code)
        {
            if (code == Code || Find(code) == null) return;
            Apply(code);
            PlayerPrefs.SetString("lang", code);
            PlayerPrefs.Save();
            Changed?.Invoke();
        }

        static void Apply(string code)
        {
            Code = code;
            table = null;
            if (code != "tr" && code != "en") table = Load(code);
        }

        public static string T(string tr, string en)
        {
            if (Code == "tr") return tr;
            if (Code == "en" || table == null) return en;
            return Translate(en);
        }

        /// <summary>T for formulas. Right-to-left languages keep the English (Latin) notation: inside a left-to-right
        /// formula the bidi algorithm moves numbers next to the wrong words ("CTDI_2/3 + مركز").</summary>
        public static string TEq(string tr, string en) => Current.Rtl ? en : T(tr, en);

        // ------------------------------------------------------------------ translation tables
        static Dictionary<uint, string> table;
        static readonly Regex Num = new Regex(@"(?<![A-Za-z_])[-+]?\d+(?:[.,]\d+)?", RegexOptions.CultureInvariant);
        static readonly Dictionary<string, string> cache = new Dictionary<string, string>();

        /// <summary>True when the current language shows its own text for this English string (always for TR / EN).</summary>
        public static bool Translated(string en) => table == null || Translate(en) != en;

        /// <summary>English → current language (identity for Turkish / English or when no translation exists).</summary>
        public static string Translate(string en)
        {
            if (table == null || string.IsNullOrEmpty(en)) return en;
            if (cache.TryGetValue(en, out var hit)) return hit;
            string result = en;
            var nums = new List<string>();
            string key = Num.Replace(en, m => { nums.Add(m.Value); return "#"; });
            if (table.TryGetValue(Hash(key), out var t))
            {
                if (nums.Count == 0) result = t;
                else
                {
                    var sb = new StringBuilder(t.Length + 16);
                    int k = 0;
                    foreach (char c in t)
                        if (c == '#' && k < nums.Count) sb.Append(nums[k++]); else sb.Append(c);
                    result = k == nums.Count ? sb.ToString() : en;     // numbers lost in translation: keep English
                }
            }
            else missing?.Add(key);
            if (cache.Count > 20000) cache.Clear();
            cache[en] = result;
            return result;
        }

        static Dictionary<uint, string> Load(string code)
        {
            cache.Clear();
            var ta = Resources.Load<TextAsset>("Lang/" + code);
            if (ta == null) { Debug.LogWarning("[MP] no translation file for " + code); return null; }
            var d = new Dictionary<uint, string>(4096);
            foreach (var raw in ta.text.Split('\n'))
            {
                var line = raw.TrimEnd('\r');
                int tab = line.IndexOf('\t');
                if (tab != 8) continue;
                if (!uint.TryParse(line.Substring(0, 8), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint h)) continue;
                d[h] = Unescape(line.Substring(9));
            }
            Debug.Log("[MP] language " + code + ": " + d.Count + " strings");
            return d;
        }

        static string Unescape(string s)
        {
            if (s.IndexOf('\\') < 0) return s;
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '\\' && i + 1 < s.Length)
                {
                    char e = s[++i];
                    sb.Append(e == 'n' ? '\n' : e == 't' ? '\t' : e);
                }
                else sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>FNV-1a over the UTF-8 bytes of the number-masked English text (same as i18n_extract.py).</summary>
        public static uint Hash(string key)
        {
            uint h = 2166136261;
            foreach (byte b in Encoding.UTF8.GetBytes(key)) { h ^= b; h *= 16777619; }
            return h;
        }

        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// <summary>Formats a number with the given number of significant-looking decimals.</summary>
        public static string F(double v, int decimals = 2) => v.ToString("F" + decimals, Inv);

        /// <summary>Engineering style: picks decimals from magnitude (3 significant digits).</summary>
        public static string G(double v)
        {
            double a = Math.Abs(v);
            if (a == 0) return "0";
            if (a >= 1e5 || a < 1e-3) return v.ToString("0.00E+0", Inv);
            if (a >= 100) return v.ToString("F0", Inv);
            if (a >= 10) return v.ToString("F1", Inv);
            if (a >= 1) return v.ToString("F2", Inv);
            return v.ToString("F3", Inv);
        }
    }
}
