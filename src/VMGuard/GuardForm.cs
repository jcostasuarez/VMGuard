using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace VMGuard
{
    /// <summary>
    /// Ventana invisible que recibe el aviso de apagado de Windows (WM_QUERYENDSESSION)
    /// y revisa la bateria con un temporizador. Opcionalmente muestra un icono en la bandeja.
    ///
    /// La ventana se "muestra" con opacidad 0 en lugar de quedar oculta: Windows solo
    /// respeta ShutdownBlockReasonCreate en ventanas visibles de primer nivel.
    /// </summary>
    internal sealed class GuardForm : Form
    {
        /// <summary>Lo maximo que se espera a una suspension por bateria en curso al apagar.</summary>
        private static readonly TimeSpan ShutdownWaitForBackground = TimeSpan.FromSeconds(330);

        private readonly Options _opt;
        private readonly Logger _log;
        private readonly VmSuspender _suspender;
        private readonly Timer _timer;
        private readonly NotifyIcon _tray;

        public GuardForm(Options opt, Logger log, VmSuspender suspender)
        {
            _opt = opt;
            _log = log;
            _suspender = suspender;

            Text = "VMGuard";
            ShowInTaskbar = false;
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            StartPosition = FormStartPosition.Manual;
            Location = new Point(-32000, -32000);
            Size = new Size(1, 1);
            Opacity = 0;

            _timer = new Timer { Interval = opt.IntervalSeconds * 1000 };
            _timer.Tick += (s, e) => CheckBattery(false);

            if (!opt.NoTrayIcon) _tray = CreateTrayIcon();
            _suspender.SuspendStarting += OnSuspendStarting;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _timer.Start();
            if (_opt.DryRun) CheckBattery(true);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == NativeMethods.WM_QUERYENDSESSION)
            {
                // Retiene el apagado hasta que las VMs queden suspendidas
                NativeMethods.ShutdownBlockReasonCreate(Handle, "Suspendiendo maquinas virtuales VMware...");
                try { SuspendForShutdown(); }
                catch (Exception ex) { _log.Write("ERROR: " + ex.Message); }
                NativeMethods.ShutdownBlockReasonDestroy(Handle);
                m.Result = (IntPtr)1;
                return;
            }
            base.WndProc(ref m);
        }

        private void SuspendForShutdown()
        {
            _timer.Stop();
            // Si hay una suspension por bateria en marcha, se espera a que termine
            // en lugar de lanzar otra sobre las mismas VMs.
            if (_suspender.IsBusy)
            {
                _log.Write("Apagado/cierre de sesion durante una suspension en curso -> esperando");
                _suspender.WaitBackground(ShutdownWaitForBackground);
            }
            // Pasada final por si quedo alguna VM encendida (o no habia nada en curso)
            _suspender.SuspendAll("Apagado/cierre de sesion");
            // Si al final no se apaga (alguien cancelo), se sigue vigilando
            _timer.Start();
        }

        private void CheckBattery(bool report)
        {
            try
            {
                if (_suspender.IsBusy) return;

                PowerStatus ps = SystemInformation.PowerStatus;
                int pct = (int)Math.Round(ps.BatteryLifePercent * 100);
                bool hasBattery = (ps.BatteryChargeStatus & BatteryChargeStatus.NoSystemBattery) == 0
                                  && ps.BatteryChargeStatus != BatteryChargeStatus.Unknown;
                UpdateTrayText(ps, pct, hasBattery);

                if (ps.PowerLineStatus == PowerLineStatus.Offline && hasBattery && pct <= _opt.BatteryThreshold)
                {
                    string reason = "Bateria baja (" + pct + "%, desenchufado, umbral " + _opt.BatteryThreshold + "%)";
                    // En modo prueba solo se listan VMs (rapido): corre aqui mismo
                    if (_opt.DryRun) _suspender.SuspendAll(reason);
                    else _suspender.StartBackground(reason);
                }
                else if (report)
                {
                    _log.Write("Bateria " + (hasBattery ? pct + "%" : "no detectada") + ", energia " + PowerLineText(ps.PowerLineStatus) +
                               ", umbral " + _opt.BatteryThreshold + "% -> no se suspenderia por bateria");
                    var vms = _suspender.GetRunningVMs();
                    _log.Write("VMs encendidas ahora: " + vms.Count);
                    foreach (string vmx in vms) _log.Write("  " + vmx);
                }
            }
            catch (Exception ex)
            {
                _log.Write("ERROR: " + ex.Message);
            }
        }

        private static string PowerLineText(PowerLineStatus s)
        {
            switch (s)
            {
                case PowerLineStatus.Online: return "enchufado";
                case PowerLineStatus.Offline: return "desenchufado";
                default: return "desconocida";
            }
        }

        // --- Icono de la bandeja ---------------------------------------------

        private NotifyIcon CreateTrayIcon()
        {
            var menu = new ContextMenuStrip();
            menu.Items.Add("Suspender VMs ahora", null, (s, e) =>
            {
                if (_opt.DryRun) _suspender.SuspendAll("Manual (menu)");
                else if (!_suspender.StartBackground("Manual (menu)"))
                    _tray.ShowBalloonTip(3000, "VMGuard", "Ya hay una suspension en curso.", ToolTipIcon.Info);
            });
            menu.Items.Add("Abrir log", null, (s, e) => OpenLog());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Salir de VMGuard", null, (s, e) =>
            {
                var r = MessageBox.Show(
                    "Si salis, las VMs no se suspenderan al apagar ni con bateria baja hasta que vuelvas a iniciar sesion.\n\nSalir de VMGuard?",
                    "VMGuard", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
                if (r == DialogResult.Yes)
                {
                    _log.Write("VMGuard cerrado desde el icono de la bandeja");
                    Close();
                }
            });

            var icon = new NotifyIcon
            {
                Icon = SystemIcons.Shield,
                Text = "VMGuard",
                ContextMenuStrip = menu,
                Visible = true,
            };
            icon.DoubleClick += (s, e) => OpenLog();
            return icon;
        }

        private void UpdateTrayText(PowerStatus ps, int pct, bool hasBattery)
        {
            if (_tray == null) return;
            string text = "VMGuard" + (_opt.DryRun ? " [PRUEBA]" : "") + "\n" +
                          (hasBattery ? "Bateria " + pct + "% " + PowerLineText(ps.PowerLineStatus) : "Sin bateria") +
                          "\nUmbral " + _opt.BatteryThreshold + "%";
            // NotifyIcon.Text admite como maximo 63 caracteres
            _tray.Text = text.Length > 63 ? text.Substring(0, 63) : text;
        }

        private void OnSuspendStarting(string reason, int count)
        {
            if (_tray == null) return;
            try
            {
                BeginInvoke((Action)(() =>
                    _tray.ShowBalloonTip(5000, "VMGuard", reason + ": suspendiendo " + count + " VM(s)", ToolTipIcon.Warning)));
            }
            catch
            {
                // La ventana puede estar cerrandose
            }
        }

        private void OpenLog()
        {
            try
            {
                if (!File.Exists(_log.Path)) File.AppendAllText(_log.Path, "");
                Process.Start(new ProcessStartInfo("notepad.exe", Options.Quote(_log.Path)) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                _log.Write("ERROR al abrir el log: " + ex.Message);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _timer.Dispose();
                if (_tray != null)
                {
                    _tray.Visible = false;
                    _tray.Dispose();
                }
            }
            base.Dispose(disposing);
        }
    }
}
