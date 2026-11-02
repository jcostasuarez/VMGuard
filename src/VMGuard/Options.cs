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

    /// <summary>Opciones de linea de comandos.</summary>
    internal sealed class Options
    {
        public const int DefaultThreshold = 10;
        public const int DefaultInterval = 30;

        public Command Command { get; private set; } = Command.Run;
        public int BatteryThreshold { get; private set; } = DefaultThreshold;
        public int IntervalSeconds { get; private set; } = DefaultInterval;
        public string VmrunPath { get; private set; } = "";
        public bool DryRun { get; private set; }
        public bool NoTrayIcon { get; private set; }

        public const string Usage =
            "VMGuard - suspende las VMs de VMware Workstation al apagar o con bateria baja\n" +
            "\n" +
            "Uso:\n" +
            "  VMGuard.exe [opciones]               ejecuta VMGuard (oculto)\n" +
            "  VMGuard.exe --instalar [opciones]    lo instala para que arranque al iniciar sesion\n" +
            "  VMGuard.exe --desinstalar            lo desinstala y lo detiene\n" +
            "\n" +
            "Opciones:\n" +
            "  --umbral N       % de bateria para suspender (por defecto 10)\n" +
            "  --intervalo N    segundos entre revisiones de bateria (por defecto 30)\n" +
            "  --vmrun RUTA     ruta a vmrun.exe (por defecto se detecta sola)\n" +
            "  --dryrun         modo de prueba: registra lo que haria, nunca suspende\n" +
            "  --sin-icono      no mostrar el icono en la bandeja del sistema\n" +
            "  --ayuda          muestra esta ayuda";

        /// <summary>Interpreta los argumentos. Lanza ArgumentException si algo no es valido.</summary>
        public static Options Parse(IList<string> args)
        {
            var o = new Options();
            for (int i = 0; i < args.Count; i++)
            {
                string raw = args[i];
                string name = Normalize(raw);
                string value = null;

                // Permite tanto "--umbral 15" como "--umbral=15"
                int eq = name.IndexOf('=');
                if (eq > 0)
                {
                    value = raw.Substring(raw.IndexOf('=') + 1);
                    name = name.Substring(0, eq);
                }

                switch (name)
                {
                    case "instalar":
                    case "install":
                        o.Command = Command.Install;
                        break;
                    case "desinstalar":
                    case "uninstall":
                        o.Command = Command.Uninstall;
                        break;
                    case "ayuda":
                    case "help":
                    case "h":
                    case "?":
                        o.Command = Command.Help;
                        break;
                    case "dryrun":
                    case "dry-run":
                    case "prueba":
                        o.DryRun = true;
                        break;
                    case "sin-icono":
                    case "sinicono":
                    case "no-tray":
                        o.NoTrayIcon = true;
                        break;
                    case "umbral":
                    case "umbralbateria":
                    case "threshold":
                        o.BatteryThreshold = ParseInt(raw, value ?? Next(args, ref i, raw), 1, 100);
                        break;
                    case "intervalo":
                    case "intervaloseg":
                    case "interval":
                        o.IntervalSeconds = ParseInt(raw, value ?? Next(args, ref i, raw), 1, 3600);
                        break;
                    case "vmrun":
                    case "vmrunpath":
                        o.VmrunPath = (value ?? Next(args, ref i, raw)).Trim().Trim('"');
                        break;
                    default:
                        throw new ArgumentException("Opcion desconocida: " + raw);
                }
            }
            return o;
        }

        /// <summary>Argumentos para relanzar VMGuard con estas mismas opciones (sin el comando).</summary>
        public string ToRunArguments()
        {
            var sb = new StringBuilder();
            sb.Append("--umbral ").Append(BatteryThreshold.ToString(CultureInfo.InvariantCulture));
            sb.Append(" --intervalo ").Append(IntervalSeconds.ToString(CultureInfo.InvariantCulture));
            if (!string.IsNullOrEmpty(VmrunPath)) sb.Append(" --vmrun ").Append(Quote(VmrunPath));
            if (NoTrayIcon) sb.Append(" --sin-icono");
            return sb.ToString();
        }

        /// <summary>Entrecomilla un argumento para la linea de comandos de Windows.</summary>
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
            else throw new ArgumentException("Argumento inesperado: " + arg);
            return a.ToLowerInvariant();
        }

        private static string Next(IList<string> args, ref int i, string name)
        {
            if (i + 1 >= args.Count) throw new ArgumentException("Falta el valor de " + name);
            return args[++i];
        }

        private static int ParseInt(string name, string value, int min, int max)
        {
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) || n < min || n > max)
                throw new ArgumentException(string.Format(CultureInfo.InvariantCulture,
                    "Valor invalido para {0}: '{1}' (debe ser un numero entre {2} y {3})", name, value, min, max));
            return n;
        }
    }
}
