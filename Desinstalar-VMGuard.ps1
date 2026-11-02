Stop-ScheduledTask -TaskName 'VMGuard' -ErrorAction SilentlyContinue
Unregister-ScheduledTask -TaskName 'VMGuard' -Confirm:$false -ErrorAction SilentlyContinue
Get-CimInstance Win32_Process -Filter "Name='powershell.exe'" |
    Where-Object { $_.ProcessId -ne $PID -and $_.CommandLine -like '*\VMGuard\VMGuard.ps1*' } |
    ForEach-Object { Stop-Process -Id $_.ProcessId -Force }
Write-Host 'VMGuard desinstalado.'
