// Languages beyond English, Portuguese and French: launcher\lang\<code>.json, embedded in the program by
// build-launcher.cmd ("lang.<code>.json"), or a file of the same name in a "lang" folder beside the program
// (a new translation tried without a build). Each file is one JSON object: "_name" is the language's own
// name for the picker, and every other key is an English text exactly as the code passes it to L(); its
// value is the translation. A text missing from a file shows in English. Portuguese and French stay as
// they are (L's pt argument and French.cs).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Web.Script.Serialization;

namespace VIctoryLauncher
{
    partial class LauncherForm
    {
        /// The language the launcher shows ("en", "pt", "fr" or a file's code).
        static string Lang = "en";
        /// The texts of a file language, by their English; null for en, pt and fr.
        static Dictionary<string, string> LangText;

        /// Every language the picker offers: code and the language's own name, English first.
        static List<KeyValuePair<string, string>> Languages()
        {
            var list = new List<KeyValuePair<string, string>> {
                new KeyValuePair<string, string>("en", "English"),
                new KeyValuePair<string, string>("pt", "Português"),
                new KeyValuePair<string, string>("fr", "Français"),
            };
            foreach (var code in LanguageFiles().Keys.OrderBy(c => c))
            {
                var name = LanguageName(code);
                if (name != null && !list.Any(l => l.Key == code)) list.Add(new KeyValuePair<string, string>(code, name));
            }
            return list;
        }

        /// The language files there are: code -> where the text comes from (a file path, or "res:" and a resource name).
        static Dictionary<string, string> LanguageFiles()
        {
            var found = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var res in Assembly.GetExecutingAssembly().GetManifestResourceNames())
                    if (res.StartsWith("lang.") && res.EndsWith(".json"))
                        found[res.Substring(5, res.Length - 10)] = "res:" + res;
            }
            catch (Exception) { }
            try
            {
                var dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lang");
                if (Directory.Exists(dir))
                    foreach (var f in Directory.GetFiles(dir, "*.json"))
                        found[Path.GetFileNameWithoutExtension(f)] = f;
            }
            catch (Exception) { }
            found.Remove("en"); found.Remove("pt"); found.Remove("fr");
            return found;
        }

        static Dictionary<string, object> ReadLanguage(string code)
        {
            string where;
            if (!LanguageFiles().TryGetValue(code, out where)) return null;
            try
            {
                string json;
                if (where.StartsWith("res:"))
                    using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(where.Substring(4)))
                    using (var r = new StreamReader(s, Encoding.UTF8)) json = r.ReadToEnd();
                else json = File.ReadAllText(where, Encoding.UTF8);
                return new JavaScriptSerializer { MaxJsonLength = 16 << 20 }.Deserialize<Dictionary<string, object>>(json);
            }
            catch (Exception) { return null; }
        }

        static string LanguageName(string code)
        {
            var d = ReadLanguage(code);
            object name;
            return d != null && d.TryGetValue("_name", out name) ? name as string : null;
        }

        /// Sets the language from launcher.ini's lang= (none yet: Windows' own language when there is a
        /// translation of it, else English).
        static void SetLanguage(string asked)
        {
            var code = (asked ?? "").Trim().ToLowerInvariant();
            if (code.Length == 0)
            {
                var windows = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.ToLowerInvariant();
                code = Languages().Any(l => l.Key == windows) ? windows : "en";
            }
            Pt = code == "pt";
            Fr = code == "fr";
            LangText = null;
            if (!Pt && !Fr && code != "en")
            {
                var d = ReadLanguage(code);
                if (d == null) code = "en";
                else LangText = d.Where(kv => !kv.Key.StartsWith("_") && kv.Value is string)
                    .ToDictionary(kv => kv.Key, kv => (string)kv.Value);
            }
            Lang = code;
        }

        /// A file language's text for this English one, or the English.
        static string FromFile(string en)
        {
            string t;
            return LangText != null && en != null && LangText.TryGetValue(en, out t) && t.Length > 0 ? t : en;
        }
    }
}
