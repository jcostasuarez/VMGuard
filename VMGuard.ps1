# VMGuard: suspende todas las VMs de VMware Workstation cuando
#  - Windows se apaga / reinicia / cierra sesion (intercepta WM_QUERYENDSESSION)
#  - la bateria baja del umbral estando desenchufado
# Se ejecuta oculto en la sesion del usuario (tarea programada al iniciar sesion).
param(
    [int]$UmbralBateria = 10,   # % de bateria para suspender
    [int]$IntervaloSeg  = 30,   # cada cuanto se revisa la bateria
    [string]$VmrunPath  = ''    # ruta a vmrun.exe; vacio = detectar automaticamente
)

$log = Join-Path $PSScriptRoot 'VMGuard.log'

# Funciones compartidas entre el hilo principal y el hilo de suspension en segundo plano.
# Usan $vmrun y $log del ambito que las carga.
$core = {
    function Write-Log([string]$msg) {
        # El log se escribe desde dos hilos: un mutex evita que se pisen
        $m = New-Object System.Threading.Mutex($false, 'Local\VMGuardLog')
        try {
            try { [void]$m.WaitOne(5000) } catch [System.Threading.AbandonedMutexException] { }
            "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')  $msg" | Add-Content -Path $log
        } finally {
            try { $m.ReleaseMutex() } catch { }
            $m.Dispose()
        }
    }

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
}
. $core

trap { Write-Log "ERROR: $_"; continue }

# Busca vmrun.exe: parametro, registro de VMware, PATH y rutas por defecto
function Find-Vmrun([string]$explicita) {
    $candidatos = New-Object System.Collections.Generic.List[string]
    if ($explicita) { $candidatos.Add($explicita) }

    $claves = @(
        'HKLM:\SOFTWARE\WOW6432Node\VMware, Inc.\VMware Workstation',
        'HKLM:\SOFTWARE\VMware, Inc.\VMware Workstation',
        'HKLM:\SOFTWARE\WOW6432Node\VMware, Inc.\VMware VIX',
        'HKLM:\SOFTWARE\VMware, Inc.\VMware VIX'
    )
    foreach ($k in $claves) {
        $dir = (Get-ItemProperty -Path $k -Name InstallPath -ErrorAction SilentlyContinue).InstallPath
        if ($dir) { $candidatos.Add((Join-Path $dir 'vmrun.exe')) }
    }

    $cmd = Get-Command vmrun.exe -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($cmd) { $candidatos.Add($cmd.Source) }

    foreach ($pf in @(${env:ProgramFiles(x86)}, $env:ProgramFiles)) {
        if ($pf) { $candidatos.Add((Join-Path $pf 'VMware\VMware Workstation\vmrun.exe')) }
    }

    foreach ($c in $candidatos) {
        if ($c -and (Test-Path -LiteralPath $c -PathType Leaf)) { return $c }
    }
    return $null
}

# Una sola instancia
$mutex = New-Object System.Threading.Mutex($false, 'Local\VMGuard')
if (-not $mutex.WaitOne(0)) { exit }

$vmrun = Find-Vmrun $VmrunPath
if (-not $vmrun) {
    Write-Log 'ERROR: no se encontro vmrun.exe. Instala VMware Workstation o indica la ruta con -VmrunPath.'
    exit 1
}

# --- Suspension en segundo plano --------------------------------------------
# La suspension por bateria corre en otro runspace para que el hilo de la ventana
# siga atendiendo mensajes. Asi, si Windows se apaga mientras tanto, el aviso de
# apagado se procesa enseguida en lugar de esperar a que termine la suspension.
$script:bg = $null

function Test-SuspensionEnCurso {
    if ($null -eq $script:bg) { return $false }
    if (-not $script:bg.Handle.IsCompleted) { return $true }
    # Termino: recoger errores y liberar recursos
    try {
        $script:bg.PS.EndInvoke($script:bg.Handle) | Out-Null
        foreach ($e in $script:bg.PS.Streams.Error) { Write-Log "ERROR (segundo plano): $e" }
    } catch {
        Write-Log "ERROR (segundo plano): $_"
    } finally {
        $script:bg.PS.Dispose()
        $script:bg.RS.Dispose()
        $script:bg = $null
    }
    return $false
}

function Start-SuspensionEnSegundoPlano([string]$motivo) {
    if (Test-SuspensionEnCurso) { return }
    $rs = [runspacefactory]::CreateRunspace()
    $rs.Open()
    $ps = [PowerShell]::Create()
    $ps.Runspace = $rs
    $codigo = "param(`$vmrun, `$log, `$motivo)`n" + $core.ToString() + "`nSuspend-AllVMs `$motivo"
    [void]$ps.AddScript($codigo).AddArgument($vmrun).AddArgument($log).AddArgument($motivo)
    $script:bg = @{ PS = $ps; RS = $rs; Handle = $ps.BeginInvoke() }
}

function Invoke-SuspensionPorApagado {
    # Si hay una suspension por bateria en marcha, se espera a que termine
    # en lugar de lanzar otra sobre las mismas VMs.
    if (Test-SuspensionEnCurso) {
        Write-Log 'Apagado/cierre de sesion durante una suspension en curso -> esperando'
        [void]$script:bg.Handle.AsyncWaitHandle.WaitOne([TimeSpan]::FromSeconds(330))
        [void](Test-SuspensionEnCurso)
    }
    # Pasada final por si quedo alguna VM encendida (o no habia nada en curso)
    Suspend-AllVMs 'Apagado/cierre de sesion'
}

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
$win.OnQueryEndSession = [Action]{ Invoke-SuspensionPorApagado }

$timer = New-Object System.Windows.Forms.Timer
$timer.Interval = $IntervaloSeg * 1000
$timer.add_Tick({
    if (Test-SuspensionEnCurso) { return }
    $ps = [System.Windows.Forms.SystemInformation]::PowerStatus
    $pct = [int]($ps.BatteryLifePercent * 100)
    if ($ps.PowerLineStatus -eq 'Offline' -and $pct -le $UmbralBateria) {
        Start-SuspensionEnSegundoPlano "Bateria baja ($pct%)"
    }
})
$timer.Start()

Write-Log "VMGuard iniciado (umbral bateria $UmbralBateria%, vmrun: $vmrun)"
[System.Windows.Forms.Application]::Run($win)
