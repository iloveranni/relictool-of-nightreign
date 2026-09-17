using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace NightreignRelicTool.Localization
{
    public sealed class UiTextRecord
    {
        public string key { get; set; }
        public string zh { get; set; }
        public string en { get; set; }
        public bool template { get; set; }
    }
    public sealed class GameTextRecord
    {
        public string key { get; set; }
        public string zh { get; set; }
        public string en { get; set; }
        public string status { get; set; }
        public string version { get; set; }
        public string fmgTable { get; set; }
        public int? textId { get; set; }
    }
    public sealed class GameTextCatalog
    {
        private readonly Dictionary<string, GameTextRecord> records;
        public GameTextCatalog(IEnumerable<GameTextRecord> rows, IDictionary<string, string> references = null)
        {
            records = new Dictionary<string, GameTextRecord>(StringComparer.Ordinal);
            foreach (var row in rows)
            {
                if (row == null || string.IsNullOrEmpty(row.key) || records.ContainsKey(row.key))
                    throw new InvalidDataException("Duplicate or empty localization key.");
                if (!Regex.IsMatch(row.key, @"^(effect|preset|category|vessel):[0-9]+$|^character:[a-z]+$|^selector:(filter:[0-9]+|entry:.+)$|^primary:.+$"))
                    throw new InvalidDataException("Invalid localization identity.");
                if (row.version != "1.03.5") throw new InvalidDataException("Unsupported text version.");
                string expected;
                if (references != null && (!references.TryGetValue(row.key, out expected)
                    || expected != row.fmgTable + ":" + row.textId))
                    throw new InvalidDataException("Text reference does not match the audited Param binding.");
                if (row.status == "verified_fmg" && (string.IsNullOrWhiteSpace(row.en) || !row.textId.HasValue || string.IsNullOrEmpty(row.fmgTable)))
                    throw new InvalidDataException("Confirmed text must have a nonempty FMG reference and English value.");
                records.Add(row.key, row);
            }
        }
        public string Get(string key, string fallback, bool english)
        {
            GameTextRecord record;
            return english && records.TryGetValue(key, out record) && record.status == "verified_fmg"
                && !string.IsNullOrWhiteSpace(record.en) ? record.en : fallback ?? string.Empty;
        }
        public int Count { get { return records.Count; } }
    }
    public sealed class LanguagePreference
    {
        public string ManualLanguage { get; private set; }
        public string Language { get; private set; }
        private readonly string path;
        public LanguagePreference(string path, Func<string> detectDisplayLanguage)
        {
            this.path = path;
            try
            {
                if (File.Exists(path))
                {
                    string saved = File.ReadAllText(path, Encoding.UTF8).Trim();
                    if (saved == "en" || saved == "zh-CN") ManualLanguage = saved;
                }
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { }
            Language = ManualLanguage ?? DefaultFor(detectDisplayLanguage());
        }
        public static string DefaultFor(string displayLanguage)
        {
            return (displayLanguage ?? "").Equals("zh", StringComparison.OrdinalIgnoreCase)
                || (displayLanguage ?? "").StartsWith("zh-", StringComparison.OrdinalIgnoreCase) ? "zh-CN" : "en";
        }
        public void Choose(string language)
        { Choose(language, null); }
        internal void Choose(string language, Action<string, string> publish)
        {
            if (language != "en" && language != "zh-CN") throw new ArgumentOutOfRangeException("language");
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            bool preserve = false;
            try
            {
                File.WriteAllText(temp, language + "\n", new UTF8Encoding(false));
                NightreignRelicTool.Core.LocalPreferencePublication.Publish(temp, path, publish);
                ManualLanguage = Language = language;
            }
            catch (IOException error) { preserve = NightreignRelicTool.Core.LocalPreferencePublication.IsUncertain(error); throw; }
            finally
            {
                try { if (!preserve && File.Exists(temp)) File.Delete(temp); }
                catch (Exception error) when (error is IOException || error is UnauthorizedAccessException) { }
            }
        }
    }
    public static class WindowsDisplayLanguage
    {
        // User UI preferences are independent of region, time zone, keyboard and our thread cultures.
        public static string Read()
        {
            uint count = 0, size = 0;
            if (GetUserPreferredUILanguages(8, out count, null, ref size) && size > 1)
            {
                var buffer = new StringBuilder((int)size);
                if (GetUserPreferredUILanguages(8, out count, buffer, ref size)) return buffer.ToString().Split('\0')[0];
            }
            try { return CultureInfo.GetCultureInfo(GetUserDefaultUILanguage()).Name; }
            catch (CultureNotFoundException) { return "en"; }
        }
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetUserPreferredUILanguages(uint flags, out uint count, StringBuilder buffer, ref uint size);
        [DllImport("kernel32.dll")] private static extern ushort GetUserDefaultUILanguage();
    }
    public sealed class LocalizationService : INotifyPropertyChanged
    {
        public static readonly LocalizationService Current = new LocalizationService();
        private readonly Dictionary<string, UiTextRecord> ui;
        private readonly Dictionary<string, UiTextRecord> byChinese;
        private readonly List<Tuple<Regex, UiTextRecord>> templates;
        public readonly GameTextCatalog Game;
        private LanguagePreference preference;
        private string language = "zh-CN"; // explicit initialization at the executable boundary; deterministic test default.
        public string Language { get { return language; } }
        public bool IsEnglish { get { return language == "en"; } }
        public int Revision { get; private set; }
        public event PropertyChangedEventHandler PropertyChanged;
        public event EventHandler Changed;
        private LocalizationService()
        {
            var rows = Read<UiTextRecord[]>("NightreignRelicTool.Localization.Ui.json");
            ui = rows.ToDictionary(x => x.key, StringComparer.Ordinal);
            byChinese = rows.Where(x => !x.template).ToDictionary(x => x.zh, StringComparer.Ordinal);
            templates = new List<Tuple<Regex, UiTextRecord>>();
            foreach (var row in rows)
            {
                ValidatePlaceholders(row.zh, row.en);
                if (row.template)
                {
                    string pattern = Regex.Escape(row.zh);
                    pattern = Regex.Replace(pattern, @"\\\{([0-9]+)}", m => "(?<p" + m.Groups[1].Value + ">.*?)");
                    templates.Add(Tuple.Create(new Regex("\\A" + pattern + "\\z", RegexOptions.Singleline | RegexOptions.CultureInvariant), row));
                }
            }
            Game = new GameTextCatalog(Read<GameTextRecord[]>("NightreignRelicTool.Localization.Game.json"),
                Read<Dictionary<string,string>>("NightreignRelicTool.Localization.Bindings.json"));
        }
        private static T Read<T>(string resource)
        {
            using (var stream = typeof(LocalizationService).Assembly.GetManifestResourceStream(resource))
            using (var decoded = DecodeResource(stream, resource))
            using (var reader = new StreamReader(decoded, new UTF8Encoding(false, true)))
                return new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Deserialize<T>(reader.ReadToEnd());
        }
        internal static MemoryStream DecodeResource(Stream stream, string resource)
        {
            if (stream == null) throw new InvalidDataException("Missing embedded localization resource: " + resource);
            int expectedLength = ReleaseResourceIntegrity.ExpectedLength(resource);
            var output = new MemoryStream(expectedLength);
            try
            {
                using (var gzip = new GZipStream(stream, CompressionMode.Decompress, true))
                {
                    byte[] buffer = new byte[8192]; int count;
                    while ((count = gzip.Read(buffer, 0, buffer.Length)) != 0)
                    {
                        if (output.Length + count > expectedLength) throw new InvalidDataException("Invalid embedded localization resource length.");
                        output.Write(buffer, 0, count);
                    }
                }
                if (output.Length != expectedLength) throw new InvalidDataException("Incomplete embedded localization resource.");
                output.Position = 0;
                using (var hash = SHA256.Create())
                    if (BitConverter.ToString(hash.ComputeHash(output)).Replace("-", "") != ReleaseResourceIntegrity.ExpectedHash(resource))
                        throw new InvalidDataException("Embedded localization resource integrity check failed.");
                output.Position = 0; return output;
            }
            catch { output.Dispose(); throw; }
        }
        public void Initialize(string preferenceFile, Func<string> detector = null)
        {
            preference = new LanguagePreference(preferenceFile, detector ?? WindowsDisplayLanguage.Read);
            SetLanguage(preference.Language);
        }
        public void Choose(string value)
        {
            if (preference == null) throw new InvalidOperationException("Language preference is not initialized.");
            preference.Choose(value); SetLanguage(value);
        }
        public void SetLanguage(string value)
        {
            if (value != "en" && value != "zh-CN") throw new ArgumentOutOfRangeException("value");
            if (language == value) return;
            language = value; Revision++;
            if (Changed != null) Changed(this, EventArgs.Empty);
            if (PropertyChanged != null) PropertyChanged(this, new PropertyChangedEventArgs(string.Empty));
        }
        public string this[string key] { get { UiTextRecord row; return ui.TryGetValue(key, out row) ? IsEnglish ? row.en : row.zh : key; } }
        public string Text(string chinese)
        {
            if (!IsEnglish || string.IsNullOrEmpty(chinese)) return chinese ?? "";
            UiTextRecord row;
            return byChinese.TryGetValue(chinese, out row) ? row.en : chinese;
        }
        public string Format(string key, params object[] args) { return string.Format(CultureInfo.InvariantCulture, this[key], args); }
        public string Message(string chinese)
        {
            string direct = Text(chinese);
            if (!IsEnglish || direct != chinese || string.IsNullOrEmpty(chinese)) return direct;
            foreach (var item in templates)
            {
                Match match = item.Item1.Match(chinese);
                if (!match.Success) continue;
                int count = Regex.Matches(item.Item2.zh, @"\{[0-9]+}").Count;
                object[] args = Enumerable.Range(0, count).Select(i => (object)match.Groups["p" + i].Value).ToArray();
                return string.Format(CultureInfo.InvariantCulture, item.Item2.en, args);
            }
            return chinese;
        }
        public static void ValidatePlaceholders(string zh, string en)
        {
            string a = string.Join(",", Regex.Matches(zh ?? "", @"\{[0-9]+}").Cast<Match>().Select(x => x.Value).OrderBy(x => x, StringComparer.Ordinal));
            string b = string.Join(",", Regex.Matches(en ?? "", @"\{[0-9]+}").Cast<Match>().Select(x => x.Value).OrderBy(x => x, StringComparer.Ordinal));
            if (string.IsNullOrWhiteSpace(en) || a != b) throw new InvalidDataException("Missing English or mismatched placeholders.");
        }
    }
    public static class L
    {
        public static string U(string text) { return LocalizationService.Current.Message(text); }
        public static string G(string kind, object id, string fallback)
        { return LocalizationService.Current.Game.Get(kind + ":" + Convert.ToString(id, CultureInfo.InvariantCulture), fallback, LocalizationService.Current.IsEnglish); }
        public static string F(string key, params object[] args) { return LocalizationService.Current.Format(key, args); }
        // Presentation-only units for existing numeric values; never rewrite a game sentence or a stored value.
        public static string Numeric(string text)
        {
            if (!LocalizationService.Current.IsEnglish || string.IsNullOrEmpty(text)) return text ?? "";
            return string.Join(" / ", text.Split(new[] { " / " }, StringSplitOptions.None).Select(part =>
            {
                Match points = Regex.Match(part, @"\A([+−-]?[0-9]+(?:\.[0-9]+)?) 点\z", RegexOptions.CultureInvariant);
                return points.Success ? F("ui.numeric.points", points.Groups[1].Value) : U(part);
            }));
        }
    }
}
