using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Security.Principal;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace VMGuard
{
    /// <summary>
    /// Instala VMGuard en %USERPROFILE%\VMGuard y crea la tarea programada "VMGuard"
    /// que lo arranca al iniciar sesion. No requiere permisos de administrador.
    /// </summary>
    internal static class Installer
    {
        public const string TaskName = "VMGuard";
        private const string TaskNs = "http://schemas.microsoft.com/windows/2004/02/mit/task";

        public static string InstallDir =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "VMGuard");

        public static string InstalledExe => Path.Combine(InstallDir, "VMGuard.exe");

        /// <summary>Instala o reinstala. Devuelve un texto para mostrar al usuario.</summary>
        public static string Install(Options opt)
        {
            string self = Process.GetCurrentProcess().MainModule.FileName;
            Directory.CreateDirectory(InstallDir);

            // Detener la version anterior (exe o script de PowerShell) antes de reemplazarla
            RunSchtasks("/End /TN " + TaskName, ignoreErrors: true);
            StopRunningInstances();

            if (!SamePath(self, InstalledExe))
                CopyWithRetry(self, InstalledExe);

            string user = WindowsIdentity.GetCurrent().Name;
            string xmlPath = Path.Combine(Path.GetTempPath(), "VMGuard-task-" + Guid.NewGuid().ToString("N") + ".xml");
            try
            {
                using (var w = XmlWriter.Create(xmlPath, new XmlWriterSettings { Encoding = Encoding.Unicode, Indent = true }))
                    BuildTaskXml(user, InstalledExe, opt.ToRunArguments(), InstallDir).Save(w);
                RunSchtasks("/Create /F /TN " + TaskName + " /XML " + Options.Quote(xmlPath), ignoreErrors: false);
            }
            finally
            {
                try { File.Delete(xmlPath); } catch { }
            }
            RunSchtasks("/Run /TN " + TaskName, ignoreErrors: false);

            return "VMGuard instalado y en ejecucion.\n\n" +
                   "Umbral de bateria: " + opt.BatteryThreshold + "%\n" +
                   "Programa: " + InstalledExe + "\n" +
                   "Log: " + Path.Combine(InstallDir, "VMGuard.log");
        }

        /// <summary>Desinstala: borra la tarea y detiene VMGuard. La carpeta y el log se conservan.</summary>
        public static string Uninstall()
        {
            RunSchtasks("/End /TN " + TaskName, ignoreErrors: true);
            RunSchtasks("/Delete /F /TN " + TaskName, ignoreErrors: true);
            StopRunningInstances();
            return "VMGuard desinstalado.\n\nLa carpeta " + InstallDir + " (programa y log) no se borra; podes eliminarla a mano.";
        }

        /// <summary>Definicion de la tarea programada (formato XML del Programador de tareas).</summary>
        internal static XDocument BuildTaskXml(string user, string exe, string arguments, string workingDir)
        {
            XNamespace ns = TaskNs;
            return new XDocument(
                new XElement(ns + "Task", new XAttribute("version", "1.2"),
                    new XElement(ns + "RegistrationInfo",
                        new XElement(ns + "Description", "Suspende las VMs de VMware al apagar o con bateria baja")),
                    new XElement(ns + "Triggers",
                        new XElement(ns + "LogonTrigger",
                            new XElement(ns + "Enabled", "true"),
                            new XElement(ns + "UserId", user))),
                    new XElement(ns + "Principals",
                        new XElement(ns + "Principal", new XAttribute("id", "Author"),
                            new XElement(ns + "UserId", user),
                            new XElement(ns + "LogonType", "InteractiveToken"),
                            new XElement(ns + "RunLevel", "LeastPrivilege"))),
                    new XElement(ns + "Settings",
                        new XElement(ns + "MultipleInstancesPolicy", "IgnoreNew"),
                        new XElement(ns + "DisallowStartIfOnBatteries", "false"),
                        new XElement(ns + "StopIfGoingOnBatteries", "false"),
                        new XElement(ns + "AllowHardTerminate", "true"),
                        new XElement(ns + "StartWhenAvailable", "false"),
                        new XElement(ns + "AllowStartOnDemand", "true"),
                        new XElement(ns + "Enabled", "true"),
                        new XElement(ns + "Hidden", "false"),
                        new XElement(ns + "ExecutionTimeLimit", "PT0S"),
                        new XElement(ns + "Priority", "7")),
                    new XElement(ns + "Actions", new XAttribute("Context", "Author"),
                        new XElement(ns + "Exec",
                            new XElement(ns + "Command", exe),
                            new XElement(ns + "Arguments", arguments),
                            new XElement(ns + "WorkingDirectory", workingDir)))));
        }

        /// <summary>
        /// Detiene otras instancias de VMGuard.exe del usuario y el VMGuard.ps1 de la
        /// version anterior en PowerShell, si sigue corriendo.
        /// </summary>
        private static void StopRunningInstances()
        {
            int self = Process.GetCurrentProcess().Id;
            int session = Process.GetCurrentProcess().SessionId;
            var toWait = new List<Process>();

            foreach (Process p in Process.GetProcessesByName("VMGuard"))
            {
                if (p.Id == self || p.SessionId != session) { p.Dispose(); continue; }
                try { p.Kill(); toWait.Add(p); } catch { p.Dispose(); }
            }

            try
            {
                using (var searcher = new ManagementObjectSearcher(
                    "SELECT ProcessId, CommandLine FROM Win32_Process WHERE Name = 'powershell.exe' OR Name = 'pwsh.exe'"))
                {
                    foreach (ManagementObject mo in searcher.Get().Cast<ManagementObject>())
                    {
                        using (mo)
                        {
                            string cmd = mo["CommandLine"] as string;
                            if (cmd == null || cmd.IndexOf(@"\VMGuard\VMGuard.ps1", StringComparison.OrdinalIgnoreCase) < 0) continue;
                            try
                            {
                                var p = Process.GetProcessById(Convert.ToInt32(mo["ProcessId"]));
                                if (p.SessionId != session) { p.Dispose(); continue; }
                                p.Kill();
                                toWait.Add(p);
                            }
                            catch { }
                        }
                    }
                }
            }
            catch
            {
                // WMI no disponible: no es critico
            }

            foreach (Process p in toWait)
            {
                try { p.WaitForExit(10000); } catch { }
                p.Dispose();
            }
        }

        private static void CopyWithRetry(string from, string to)
        {
            for (int i = 0; ; i++)
            {
                try
                {
                    File.Copy(from, to, overwrite: true);
                    return;
                }
                catch (IOException) when (i < 10)
                {
                    // El proceso anterior puede tardar un poco en soltar el archivo
                    System.Threading.Thread.Sleep(500);
                }
            }
        }

        private static bool SamePath(string a, string b) =>
            string.Equals(Path.GetFullPath(a).TrimEnd('\\'), Path.GetFullPath(b).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);

        private static void RunSchtasks(string arguments, bool ignoreErrors)
        {
            var psi = new ProcessStartInfo("schtasks.exe", arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using (var p = Process.Start(psi))
            {
                var err = p.StandardError.ReadToEndAsync();
                string output = p.StandardOutput.ReadToEnd();
                p.WaitForExit();
                if (p.ExitCode != 0 && !ignoreErrors)
                    throw new InvalidOperationException("schtasks " + arguments.Split(' ')[0] + " fallo (" + p.ExitCode + "): " +
                                                        (err.Result + " " + output).Trim());
            }
        }
    }
}
