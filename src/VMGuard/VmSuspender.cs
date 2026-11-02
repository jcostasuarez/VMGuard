using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace VMGuard
{
    /// <summary>Lista y suspende las VMs encendidas usando vmrun.</summary>
    internal sealed class VmSuspender
    {
        /// <summary>Tiempo maximo que se espera a que terminen las suspensiones.</summary>
        public static readonly TimeSpan SuspendTimeout = TimeSpan.FromMinutes(5);

        private readonly string _vmrun;
        private readonly Logger _log;
        private readonly bool _dryRun;
        private readonly object _bgLock = new object();
        private Task _background;

        public VmSuspender(string vmrun, Logger log, bool dryRun)
        {
            _vmrun = vmrun;
            _log = log;
            _dryRun = dryRun;
        }

        /// <summary>Se dispara (en otro hilo) cuando empieza una suspension real: motivo y cantidad de VMs.</summary>
        public event Action<string, int> SuspendStarting;

        /// <summary>Hay una suspension en segundo plano en curso.</summary>
        public bool IsBusy
        {
            get { lock (_bgLock) return _background != null && !_background.IsCompleted; }
        }

        /// <summary>VMs encendidas segun "vmrun -T ws list".</summary>
        public List<string> GetRunningVMs()
        {
            var psi = NewStartInfo("-T ws list");
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            using (var p = Process.Start(psi))
            {
                // Se lee stderr en paralelo para que no se bloquee si se llena
                var err = p.StandardError.ReadToEndAsync();
                string output = p.StandardOutput.ReadToEnd();
                if (!p.WaitForExit(60000))
                {
                    try { p.Kill(); } catch { }
                    throw new TimeoutException("vmrun list no respondio en 60 s");
                }
                if (p.ExitCode != 0)
                {
                    string msg = (output + " " + err.Result).Trim();
                    throw new InvalidOperationException("vmrun list devolvio " + p.ExitCode + ": " + msg);
                }
                return ParseList(output);
            }
        }

        /// <summary>Extrae las rutas .vmx de la salida de "vmrun list".</summary>
        public static List<string> ParseList(string output)
        {
            return (output ?? "")
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim())
                .Where(l => l.EndsWith(".vmx", StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        /// <summary>Suspende todas las VMs en paralelo y espera (bloqueante).</summary>
        public void SuspendAll(string reason)
        {
            List<string> vms = GetRunningVMs();
            if (vms.Count == 0)
            {
                if (_dryRun) _log.Write(reason + " -> no hay VMs encendidas, no se suspenderia nada");
                return;
            }

            if (_dryRun)
            {
                _log.Write(reason + " -> se suspenderian " + vms.Count + " VM(s)");
                foreach (string vmx in vms) _log.Write("  " + vmx + " -> (no suspendida, modo prueba)");
                return;
            }

            _log.Write(reason + " -> suspendiendo " + vms.Count + " VM(s)");
            try { SuspendStarting?.Invoke(reason, vms.Count); } catch { }

            // Todas en paralelo para que tarde lo menos posible
            var procs = new List<Process>();
            foreach (string vmx in vms)
            {
                try
                {
                    procs.Add(Process.Start(NewStartInfo("-T ws suspend " + Options.Quote(vmx))));
                }
                catch (Exception ex)
                {
                    procs.Add(null);
                    _log.Write("  " + vmx + " -> ERROR al lanzar vmrun: " + ex.Message);
                }
            }

            DateTime deadline = DateTime.UtcNow + SuspendTimeout;
            for (int i = 0; i < vms.Count; i++)
            {
                Process p = procs[i];
                if (p == null) continue;
                using (p)
                {
                    int remaining = (int)Math.Max(0, (deadline - DateTime.UtcNow).TotalMilliseconds);
                    if (p.WaitForExit(remaining))
                        _log.Write("  " + vms[i] + " -> exit " + p.ExitCode);
                    else
                        _log.Write("  " + vms[i] + " -> sin respuesta tras " + SuspendTimeout.TotalMinutes + " min");
                }
            }
        }

        /// <summary>
        /// Lanza SuspendAll en otro hilo para que la ventana siga atendiendo los
        /// mensajes de Windows (por ejemplo, el aviso de apagado).
        /// Devuelve false si ya habia una en curso.
        /// </summary>
        public bool StartBackground(string reason)
        {
            lock (_bgLock)
            {
                if (_background != null && !_background.IsCompleted) return false;
                _background = Task.Factory.StartNew(() =>
                {
                    try { SuspendAll(reason); }
                    catch (Exception ex) { _log.Write("ERROR (segundo plano): " + ex.Message); }
                }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
                return true;
            }
        }

        /// <summary>Espera a que termine la suspension en segundo plano, si hay una.</summary>
        public void WaitBackground(TimeSpan timeout)
        {
            Task t;
            lock (_bgLock) t = _background;
            if (t == null) return;
            try { t.Wait(timeout); } catch { }
        }

        private ProcessStartInfo NewStartInfo(string arguments)
        {
            return new ProcessStartInfo(_vmrun, arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            };
        }
    }
}
