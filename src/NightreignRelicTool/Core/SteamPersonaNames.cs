using System;
using System.Globalization;
using System.IO;
using System.Security;
using System.Text;
using Microsoft.Win32;

namespace NightreignRelicTool.Core
{
    // Names are optional presentation data. Only the exact local save owner is
    // matched; AccountName, credentials and the currently active user are ignored.
    internal static class SteamPersonaNames
    {
        internal static string Find(string accountDirectory, string loginUsersPath = null)
        {
            ulong account;
            string name = Path.GetFileName(accountDirectory);
            if (!ulong.TryParse(name, NumberStyles.None, CultureInfo.InvariantCulture, out account)
                && !(name.Length == 16 && ulong.TryParse(name, NumberStyles.AllowHexSpecifier,
                    CultureInfo.InvariantCulture, out account))) return null;
            const ulong individualBase = 76561197960265728;
            if (account < individualBase || account > individualBase + uint.MaxValue)
            {
                if (name.Length != 16 || !ulong.TryParse(name, NumberStyles.AllowHexSpecifier,
                    CultureInfo.InvariantCulture, out account) || account < individualBase
                    || account > individualBase + uint.MaxValue) return null;
            }
            try
            {
                if (loginUsersPath == null)
                {
                    using (RegistryKey steam = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam", false))
                    {
                        string root = steam == null ? null : steam.GetValue("SteamPath") as string;
                        if (string.IsNullOrWhiteSpace(root)) return null;
                        loginUsersPath = Path.Combine(root, "config", "loginusers.vdf");
                    }
                }
                if (!File.Exists(loginUsersPath) || new FileInfo(loginUsersPath).Length > 1024 * 1024) return null;
                using (FileStream stream = new FileStream(loginUsersPath, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete))
                using (StreamReader reader = new StreamReader(stream, Encoding.UTF8, true))
                {
                    string expected = account.ToString(CultureInfo.InvariantCulture);
                    int depth = 0;
                    string key = null, owner = null, result = null;
                    bool users = false;
                    string token;
                    bool structural;
                    while ((token = Token(reader, out structural)) != null)
                    {
                        if (structural && token == "{")
                        {
                            if (depth == 0) users = string.Equals(key, "users", StringComparison.OrdinalIgnoreCase);
                            if (depth == 1 && users) owner = key;
                            depth++; key = null;
                        }
                        else if (structural && token == "}")
                        {
                            if (--depth < 0) return null;
                            if (depth == 1) owner = null;
                            key = null;
                        }
                        else if (key == null) key = token;
                        else
                        {
                            if (users && depth == 2 && owner == expected
                                && string.Equals(key, "PersonaName", StringComparison.OrdinalIgnoreCase))
                            {
                                if (result != null && result != token) return null;
                                result = token;
                            }
                            key = null;
                        }
                    }
                    return depth == 0 && !string.IsNullOrWhiteSpace(result) ? result.Trim() : null;
                }
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException
                || error is SecurityException || error is ArgumentException || error is NotSupportedException)
            { return null; }
        }

        private static string Token(TextReader reader, out bool structural)
        {
            structural = false;
            int value;
            while ((value = reader.Read()) >= 0)
            {
                char c = (char)value;
                if (char.IsWhiteSpace(c)) continue;
                if (c == '/' && reader.Peek() == '/') { reader.ReadLine(); continue; }
                if (c == '{' || c == '}') { structural = true; return c.ToString(); }
                if (c != '"') throw new InvalidDataException("Invalid local name data.");
                StringBuilder text = new StringBuilder();
                while ((value = reader.Read()) >= 0)
                {
                    c = (char)value;
                    if (c == '"') return text.ToString();
                    if (c == '\\')
                    {
                        value = reader.Read();
                        if (value < 0) break;
                        c = (char)value;
                        if (c == 'n') c = '\n';
                        else if (c == 't') c = '\t';
                        else if (c == 'r') c = '\r';
                    }
                    if (text.Length >= 4096) throw new InvalidDataException("Invalid local name data.");
                    text.Append(c);
                }
                throw new InvalidDataException("Incomplete local name data.");
            }
            return null;
        }
    }
}
