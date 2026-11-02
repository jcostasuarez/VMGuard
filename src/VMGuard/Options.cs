using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace VMGuard
{
    internal enum Command
    {
        Run,
        Install,
        Uninstall,
        Help
    }

    /// <summary>Command-line options.</summary>
    internal sealed class Options
    {
        public const int DefaultThreshold = 10;
        public const int DefaultInterval = 30;

        public Command Command { get; private set; } = Command.Run;
        public int BatteryThreshold { get; set; } = DefaultThreshold;
        /// <summary>The threshold was given on the command line (it then wins over the saved setting).</summary>
        public bool ThresholdGiven { get; private set; }
        public int IntervalSeconds { get; private set; } = DefaultInterval;
        public string VmrunPath { get; private set; } = "";
        public bool DryRun { get; private set; }
        public bool NoTrayIcon { get; private set; }
        /// <summary>Install/uninstall without showing a message (errors are still shown).</summary>
        public bool Quiet { get; private set; }

        public const string Usage =
            "VMGuard - suspends VMware Workstation VMs on shutdown or low battery\n" +
            "\n" +
            "Usage:\n" +
            "  VMGuard.exe [options]               run VMGuard (hidden)\n" +
            "  VMGuard.exe --install [options]     install it to start at sign-in\n" +
            "  VMGuard.exe --uninstall             uninstall and stop it\n" +
            "\n" +
            "Options:\n" +
            "  --threshold N    battery % at which VMs are suspended (default 10)\n" +
            "  --interval N     seconds between battery checks (default 30)\n" +
            "  --vmrun PATH     path to vmrun.exe (detected automatically by default)\n" +
            "  --dry-run        test mode: log what would happen, never suspend\n" +
            "  --no-tray        do not show the system tray icon\n" +
            "  --quiet          install/uninstall without the confirmation message\n" +
            "  --help           show this help";

        /// <summary>
        /// Parses the arguments. Throws ArgumentException if something is invalid.
        /// The Spanish names from earlier builds (--umbral, --instalar, ...) are still accepted.
        /// </summary>
        public static Options Parse(IList<string> args)
        {
            var o = new Options();
            for (int i = 0; i < args.Count; i++)
            {
                string raw = args[i];
                string name = Normalize(raw);
                string value = null;

                // Accepts both "--threshold 15" and "--threshold=15"
                int eq = name.IndexOf('=');
                if (eq > 0)
                {
                    value = raw.Substring(raw.IndexOf('=') + 1);
                    name = name.Substring(0, eq);
                }

                switch (name)
                {
                    case "install":
                    case "instalar":
                        o.Command = Command.Install;
                        break;
                    case "uninstall":
                    case "desinstalar":
                        o.Command = Command.Uninstall;
                        break;
                    case "help":
                    case "ayuda":
                    case "h":
                    case "?":
                        o.Command = Command.Help;
                        break;
                    case "dry-run":
                    case "dryrun":
                    case "prueba":
                        o.DryRun = true;
                        break;
                    case "quiet":
                    case "silent":
                        o.Quiet = true;
                        break;
                    case "no-tray":
                    case "sin-icono":
                    case "sinicono":
                        o.NoTrayIcon = true;
                        break;
                    case "threshold":
                    case "umbral":
                    case "umbralbateria":
                        o.BatteryThreshold = ParseInt(raw, value ?? Next(args, ref i, raw), 1, 100);
                        o.ThresholdGiven = true;
                        break;
                    case "interval":
                    case "intervalo":
                    case "intervaloseg":
                        o.IntervalSeconds = ParseInt(raw, value ?? Next(args, ref i, raw), 1, 3600);
                        break;
                    case "vmrun":
                    case "vmrunpath":
                        o.VmrunPath = (value ?? Next(args, ref i, raw)).Trim().Trim('"');
                        break;
                    default:
                        throw new ArgumentException("Unknown option: " + raw);
                }
            }
            return o;
        }

        /// <summary>
        /// Arguments for the scheduled task (without the command). The threshold is not
        /// included: it lives in the settings file so it can be changed from the tray icon.
        /// </summary>
        public string ToRunArguments()
        {
            var sb = new StringBuilder();
            sb.Append("--interval ").Append(IntervalSeconds.ToString(CultureInfo.InvariantCulture));
            if (!string.IsNullOrEmpty(VmrunPath)) sb.Append(" --vmrun ").Append(Quote(VmrunPath));
            if (NoTrayIcon) sb.Append(" --no-tray");
            return sb.ToString();
        }

        /// <summary>Quotes an argument for the Windows command line.</summary>
        public static string Quote(string arg)
        {
            if (arg.Length > 0 && arg.IndexOfAny(new[] { ' ', '\t', '"' }) < 0) return arg;
            var sb = new StringBuilder("\"");
            int backslashes = 0;
            foreach (char c in arg)
            {
                if (c == '\\') { backslashes++; continue; }
                if (c == '"') sb.Append('\\', backslashes * 2 + 1);
                else sb.Append('\\', backslashes);
                backslashes = 0;
                sb.Append(c);
            }
            sb.Append('\\', backslashes * 2);
            sb.Append('"');
            return sb.ToString();
        }

        private static string Normalize(string arg)
        {
            string a = arg.Trim();
            if (a.StartsWith("--", StringComparison.Ordinal)) a = a.Substring(2);
            else if (a.StartsWith("-", StringComparison.Ordinal) || a.StartsWith("/", StringComparison.Ordinal)) a = a.Substring(1);
            else throw new ArgumentException("Unexpected argument: " + arg);
            return a.ToLowerInvariant();
        }

        private static string Next(IList<string> args, ref int i, string name)
        {
            if (i + 1 >= args.Count) throw new ArgumentException("Missing value for " + name);
            return args[++i];
        }

        private static int ParseInt(string name, string value, int min, int max)
        {
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) || n < min || n > max)
                throw new ArgumentException(string.Format(CultureInfo.InvariantCulture,
                    "Invalid value for {0}: '{1}' (must be a number between {2} and {3})", name, value, min, max));
            return n;
        }
    }
}
