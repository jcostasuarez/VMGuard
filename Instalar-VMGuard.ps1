# Copia VMGuard.ps1 a %USERPROFILE%\VMGuard y crea una tarea programada
# que lo arranca oculto cada vez que inicias sesion. No requiere admin.
param([int]$UmbralBateria = 10)

$dest = Join-Path $env:USERPROFILE 'VMGuard'
New-Item -ItemType Directory -Force $dest | Out-Null
Copy-Item (Join-Path $PSScriptRoot 'VMGuard.ps1') $dest -Force
$script = Join-Path $dest 'VMGuard.ps1'

$action   = New-ScheduledTaskAction -Execute 'powershell.exe' `
            -Argument "-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File `"$script`" -UmbralBateria $UmbralBateria"
$trigger  = New-ScheduledTaskTrigger -AtLogOn -User "$env:USERDOMAIN\$env:USERNAME"
$settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
            -ExecutionTimeLimit ([TimeSpan]::Zero) -MultipleInstances IgnoreNew
$principal = New-ScheduledTaskPrincipal -UserId "$env:USERDOMAIN\$env:USERNAME" -LogonType Interactive -RunLevel Limited

Register-ScheduledTask -TaskName 'VMGuard' -Action $action -Trigger $trigger -Settings $settings `
    -Principal $principal -Description 'Suspende las VMs de VMware al apagar o con bateria baja' -Force | Out-Null
Start-ScheduledTask -TaskName 'VMGuard'
Write-Host "VMGuard instalado y en ejecucion. Log: $dest\VMGuard.log"
