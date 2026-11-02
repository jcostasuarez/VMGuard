using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace VMGuard
{
    /// <summary>
    /// Settings saved next to the exe in VMGuard.ini (plain "key=value" lines),
    /// so changes made from the tray icon survive restarts.
    /// </summary>
    internal sealed class Settings
    {
        public const string FileName = "VMGuard.ini";

        private Settings(string path)
        {
            Path = path;
        }

        public string Path { get; }

        /// <summary>Saved battery threshold, or null if none was saved.</summary>
        public int? BatteryThreshold { get; set; }

        public static Settings Load(string directory)
        {
            var s = new Settings(System.IO.Path.Combine(directory, FileName));
            try
            {
                if (File.Exists(s.Path)) s.Parse(File.ReadAllLines(s.Path));
            }
            catch
            {
                // Unreadable settings: use the defaults
            }
            return s;
        }

        internal void Parse(IEnumerable<string> lines)
        {
            foreach (string line in lines)
            {
                string l = line.Trim();
                if (l.Length == 0 || l.StartsWith("#", StringComparison.Ordinal) || l.StartsWith(";", StringComparison.Ordinal)) continue;
                int eq = l.IndexOf('=');
                if (eq <= 0) continue;
                string key = l.Substring(0, eq).Trim().ToLowerInvariant();
                string value = l.Substring(eq + 1).Trim();
                if (key == "threshold" &&
                    int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) && n >= 1 && n <= 100)
                {
                    BatteryThreshold = n;
                }
            }
        }

        internal string Serialize()
        {
            var sb = new StringBuilder();
            sb.AppendLine("# VMGuard settings. You can also change the threshold from the tray icon.");
            if (BatteryThreshold.HasValue)
                sb.Append("threshold=").AppendLine(BatteryThreshold.Value.ToString(CultureInfo.InvariantCulture));
            return sb.ToString();
        }

        public void Save()
        {
            File.WriteAllText(Path, Serialize(), new UTF8Encoding(false));
        }
    }
}
