using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace VMGuard
{
    /// <summary>
    /// VMGuard: suspende todas las VMs de VMware Workstation cuando
    ///  - Windows se apaga / reinicia / cierra sesion (intercepta WM_QUERYENDSESSION)
    ///  - la bateria baja del umbral estando desenchufado
    /// Corre oculto en la sesion del usuario (tarea programada al iniciar sesion).
    /// </summary>
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            Options opt;
            try
            {
                opt = Options.Parse(args);
            }
            catch (ArgumentException ex)
            {
                MessageBox.Show(ex.Message + "\n\n" + Options.Usage, "VMGuard", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 2;
            }

            switch (opt.Command)
            {
                case Command.Help:
                    MessageBox.Show(Options.Usage, "VMGuard", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return 0;
                case Command.Install:
                    return RunInstaller(() => Installer.Install(opt), "instalar");
                case Command.Uninstall:
                    return RunInstaller(Installer.Uninstall, "desinstalar");
                default:
                    return RunGuard(opt);
            }
        }

        private static int RunInstaller(Func<string> action, string verb)
        {
            try
            {
                string msg = action();
                MessageBox.Show(msg, "VMGuard", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return 0;
            }
            catch (Exception ex)
            {
                MessageBox.Show("No se pudo " + verb + " VMGuard:\n\n" + ex.Message, "VMGuard", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }

        private static int RunGuard(Options opt)
        {
            string exeDir = Path.GetDirectoryName(Application.ExecutablePath);
            var log = new Logger(Path.Combine(exeDir, "VMGuard.log"), opt.DryRun);

            if (opt.DryRun)
            {
                // Modo prueba: consola propia para ver los mensajes. Cerrarla termina VMGuard.
                if (NativeMethods.AllocConsole())
                {
                    Console.Title = "VMGuard - modo prueba";
                    log.Echo = Console.WriteLine;
                }
            }

            Application.ThreadException += (s, e) => log.Write("ERROR: " + e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (s, e) => log.Write("ERROR fatal: " + e.ExceptionObject);

            // Una sola instancia (el modo prueba usa otro nombre para poder correr
            // junto a la copia instalada sin chocar con ella)
            using (var mutex = new Mutex(false, opt.DryRun ? @"Local\VMGuard-DryRun" : @"Local\VMGuard"))
            {
                bool owned;
                try { owned = mutex.WaitOne(0); }
                catch (AbandonedMutexException) { owned = true; }
                if (!owned)
                {
                    if (opt.DryRun) log.Write("Ya hay otra instancia en modo prueba corriendo. Saliendo.");
                    return 0;
                }

                string vmrun = VmrunLocator.Find(opt.VmrunPath);
                if (vmrun == null)
                {
                    log.Write(string.IsNullOrEmpty(opt.VmrunPath)
                        ? "ERROR: no se encontro vmrun.exe. Instala VMware Workstation o indica la ruta con --vmrun."
                        : "ERROR: no existe vmrun.exe en la ruta indicada: " + opt.VmrunPath);
                    if (opt.DryRun) WaitForKey();
                    return 1;
                }

                // Recibir el aviso de apagado antes que la mayoria de las aplicaciones
                NativeMethods.SetProcessShutdownParameters(0x3FF, 0);

                var suspender = new VmSuspender(vmrun, log, opt.DryRun);
                if (opt.DryRun)
                    log.Write("VMGuard " + Version + " iniciado en MODO PRUEBA (umbral bateria " + opt.BatteryThreshold +
                              "%, intervalo " + opt.IntervalSeconds + " s, vmrun: " + vmrun +
                              "). No se suspendera ninguna VM. Cerra esta ventana para salir.");
                else
                    log.Write("VMGuard " + Version + " iniciado (umbral bateria " + opt.BatteryThreshold +
                              "%, intervalo " + opt.IntervalSeconds + " s, vmrun: " + vmrun + ")");

                using (var form = new GuardForm(opt, log, suspender))
                {
                    Application.Run(form);
                }
                mutex.ReleaseMutex();
            }
            return 0;
        }

        private static string Version =>
            typeof(Program).Assembly.GetName().Version.ToString(3);

        private static void WaitForKey()
        {
            try
            {
                Console.WriteLine("Presiona una tecla para salir...");
                Console.ReadKey(true);
            }
            catch { }
        }
    }
}
