# VMGuard: suspende todas las VMs de VMware Workstation cuando
#  - Windows se apaga / reinicia / cierra sesion (intercepta WM_QUERYENDSESSION)
#  - la bateria baja del umbral estando desenchufado
# Se ejecuta oculto en la sesion del usuario (tarea programada al iniciar sesion).
param(
    [int]$UmbralBateria = 10,   # % de bateria para suspender
    [int]$IntervaloSeg  = 30    # cada cuanto se revisa la bateria
)

$vmrun = 'C:\Program Files (x86)\VMware\VMware Workstation\vmrun.exe'
$log   = Join-Path $PSScriptRoot 'VMGuard.log'

function Write-Log([string]$msg) {
    "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')  $msg" | Add-Content -Path $log
}

trap { Write-Log "ERROR: $_"; continue }

function Get-RunningVMs {
    & $vmrun -T ws list | Where-Object { $_ -match '\.vmx\s*$' } | ForEach-Object { $_.Trim() }
}

function Suspend-AllVMs([string]$motivo) {
    $vms = @(Get-RunningVMs)
    if ($vms.Count -eq 0) { return }
    Write-Log "$motivo -> suspendiendo $($vms.Count) VM(s)"
    # Todas en paralelo para que tarde lo menos posible
    $procs = foreach ($vmx in $vms) {
        Start-Process -FilePath $vmrun -ArgumentList '-T', 'ws', 'suspend', "`"$vmx`"" -WindowStyle Hidden -PassThru
    }
    $procs | Wait-Process -Timeout 300 -ErrorAction SilentlyContinue
    for ($i = 0; $i -lt $vms.Count; $i++) {
        Write-Log "  $($vms[$i]) -> exit $($procs[$i].ExitCode)"
    }
}

# Una sola instancia
$mutex = New-Object System.Threading.Mutex($false, 'Local\VMGuard')
if (-not $mutex.WaitOne(0)) { exit }

Add-Type -AssemblyName System.Windows.Forms
Add-Type -ReferencedAssemblies System.Windows.Forms -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

public class VMGuardWindow : Form {
    [DllImport("user32.dll")] static extern bool ShutdownBlockReasonCreate(IntPtr hWnd, [MarshalAs(UnmanagedType.LPWStr)] string reason);
    [DllImport("user32.dll")] static extern bool ShutdownBlockReasonDestroy(IntPtr hWnd);
    const int WM_QUERYENDSESSION = 0x11;

    public Action OnQueryEndSession;

    public VMGuardWindow() {
        ShowInTaskbar = false;
        FormBorderStyle = FormBorderStyle.FixedToolWindow;
        Opacity = 0;
        Text = "VMGuard";
    }

    protected override void WndProc(ref Message m) {
        if (m.Msg == WM_QUERYENDSESSION && OnQueryEndSession != null) {
            // Retiene el apagado hasta que las VMs queden suspendidas
            ShutdownBlockReasonCreate(Handle, "Suspendiendo maquinas virtuales VMware...");
            try { OnQueryEndSession(); } catch { }
            ShutdownBlockReasonDestroy(Handle);
            m.Result = (IntPtr)1;
            return;
        }
        base.WndProc(ref m);
    }
}
'@

$win = New-Object VMGuardWindow
$win.OnQueryEndSession = [Action]{ Suspend-AllVMs 'Apagado/cierre de sesion' }

$timer = New-Object System.Windows.Forms.Timer
$timer.Interval = $IntervaloSeg * 1000
$timer.add_Tick({
    $ps = [System.Windows.Forms.SystemInformation]::PowerStatus
    $pct = [int]($ps.BatteryLifePercent * 100)
    if ($ps.PowerLineStatus -eq 'Offline' -and $pct -le $UmbralBateria) {
        Suspend-AllVMs "Bateria baja ($pct%)"
    }
})
$timer.Start()

Write-Log "VMGuard iniciado (umbral bateria $UmbralBateria%)"
[System.Windows.Forms.Application]::Run($win)
