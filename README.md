# VMGuard

Automatically suspends every **VMware Workstation** virtual machine when:

- Windows **shuts down, restarts or signs out**.
- The **battery drops to 10 %** (configurable) while the computer is unplugged.

VMware Workstation (tested with 17.6.4) has no built-in option to suspend VMs when the host shuts down: if Windows shuts down with VMs running, they are powered off abruptly or the shutdown hangs. VMGuard fixes that.

VMGuard is a single `VMGuard.exe` written in C#. Nothing else needs to be installed: it uses .NET Framework 4.8, which ships with Windows 10 and 11.

## Requirements

- Windows 10/11.
- VMware Workstation. VMGuard finds `vmrun.exe` on its own: first in the VMware registry keys (`InstallPath`), then on the `PATH`, and finally in the default `Program Files` folders. If it cannot find it, it writes that to the log and exits; in that case, pass the path with `--vmrun` when installing.

## Download

Get `VMGuard.exe` from the latest [release](../../releases/latest). The release also has a `.zip` (exe + README) and `SHA256SUMS.txt` with the file hashes.

The exe is not code-signed, so Windows SmartScreen may say the app is unrecognized. Choose *More info → Run anyway*.

Builds of every commit are available as the `VMGuard` artifact in the [Actions](../../actions/workflows/build.yml) tab.

## Install

From the folder where you downloaded `VMGuard.exe`, in PowerShell or `cmd`:

```powershell
.\VMGuard.exe --install --threshold 10
```

This:

1. Copies `VMGuard.exe` to `%USERPROFILE%\VMGuard`.
2. Adds it to your startup apps, so it starts hidden every time you sign in. It shows up in **Task Manager → Startup apps** (where you can also disable it).
3. Adds it to **Settings → Apps → Installed apps**, so you can uninstall it from there.
4. Starts it right away and shows a confirmation message.

No administrator rights are needed. Reinstalling is safe: it stops the running copy, replaces it and updates the startup and Installed apps entries. To change the threshold, use the tray icon (or install again with another value).

**If you were using an earlier version** (1.0.x or the old PowerShell script), there is no need to uninstall it first: the installer removes the **VMGuard** scheduled task those versions used and stops the old copy. Afterwards you can delete `VMGuard.ps1` from `%USERPROFILE%\VMGuard`.

### Options

| Option | What it does |
|---|---|
| `--threshold N` | Battery % at which VMs are suspended (1 to 100, default 10). Can also be changed from the tray icon. |
| `--interval N` | Seconds between battery checks (default 30). |
| `--vmrun "<path>\vmrun.exe"` | Path to `vmrun.exe`, only needed if it is not detected. |
| `--no-tray` | Do not show the system tray icon. |
| `--dry-run` | Test mode (see below). |
| `--install` / `--uninstall` | Install or uninstall. |
| `--quiet` | With `--install` / `--uninstall`: no confirmation message. |
| `--help` | Show the help. |

The options you pass along with `--install` are the ones used at every sign-in. The threshold is saved in `VMGuard.ini` next to the exe, so you can also change it later from the tray icon; reinstalling without `--threshold` keeps the saved value. A `--threshold` passed when running VMGuard by hand wins over the saved value. The Spanish option names from earlier builds (`--umbral`, `--instalar`, ...) still work.

### Check that it is running

The VMGuard icon (a shield) should appear in the system tray, next to the clock. Hovering over it shows the battery level and the threshold. If you don't see it, look under the hidden icons arrow.

You can also check from PowerShell:

```powershell
Get-Process VMGuard                                      # should list one process
Get-Content $env:USERPROFILE\VMGuard\VMGuard.log -Tail 10
```

### Tray icon

Right-click the icon:

- **Suspend VMs now:** suspends every running VM right away.
- **Battery threshold:** pick 5 % to 50 %, or *Custom...* for any value from 1 to 100. The change applies right away and is saved, so it is kept after restarting.
- **Open log:** opens `VMGuard.log` in Notepad (double-clicking the icon does the same).
- **Exit VMGuard:** closes it until the next sign-in (asks for confirmation).

When VMGuard starts suspending VMs because of low battery, it shows a notification.

## How it works

VMGuard uses `vmrun.exe`:

- `vmrun -T ws list` to see which VMs are running.
- `vmrun -T ws suspend "<path.vmx>"` to suspend each one. It suspends them all in parallel and waits up to 5 minutes.

At startup, the log shows which `vmrun.exe` was found.

### On shutdown, restart or sign-out

VMGuard has an invisible window that receives the Windows shutdown notification (`WM_QUERYENDSESSION`), and asks Windows to send it that notification before most other programs. When it arrives:

1. It holds the shutdown with `ShutdownBlockReasonCreate`. Windows shows the waiting screen with the message *"Suspending VMware virtual machines..."*.
2. It suspends every VM.
3. It releases the shutdown, and Windows continues with the shutdown or restart you asked for.

> ⚠️ **Don't click "Shut down anyway"** on that screen: Windows kills the processes and the VMs are powered off without being suspended.

### On low battery

Every 30 seconds (or whatever `--interval` says) it checks the power status. If the computer is **unplugged** and the battery is **at or below the threshold**, it suspends the VMs. If you start a VM again in that state, it suspends it again on the next check.

This suspend runs in the background, so the invisible window keeps handling Windows notifications. If you shut down the PC while VMs are being suspended because of low battery, Windows shows the waiting screen right away; VMGuard waits for the suspend in progress to finish (without starting another one on the same VMs) and then does a final pass in case a VM is still running.

You can change the threshold from the tray icon at any time. Pick a threshold above the critical battery level of your Windows power plan, so the VMs are suspended before Windows acts.

### Log

Everything is logged to `VMGuard.log`, next to the `VMGuard.exe` that is running (`%USERPROFILE%\VMGuard\VMGuard.log` for the installed copy): startup, each suspend with the `vmrun` exit code (`exit 0` = OK) and any errors.

## Testing it

With a VM running, restart the PC. When you sign back in, the VM should be suspended and the log should show:

```
... Shutdown/sign-out -> suspending 1 VM(s)
...   <path-to-your-vm>.vmx -> exit 0
```

### Dry-run mode (`--dry-run`)

To see what VMGuard would do without suspending anything:

```powershell
.\VMGuard.exe --dry-run
```

In dry-run mode:

- A console opens where you can see the messages. To exit, close that console.
- It **never** runs `vmrun suspend`. It only uses `vmrun -T ws list` to see which VMs are running.
- Every log line is prefixed with `[DRYRUN]`.
- At startup it logs the current state: battery percentage, whether it is plugged in, and which VMs are running.
- When a condition is met (low battery, shutdown notification or *Suspend VMs now* in the tray), it logs the reason and each VM that would be suspended.
- It can run alongside the installed copy without clashing with it.

To test low battery without waiting for it to drain, raise the threshold and unplug the computer:

```powershell
.\VMGuard.exe --dry-run --threshold 100 --interval 5
```

The console should show something like:

```
... [DRYRUN] Low battery (87%, unplugged, threshold 100%) -> would suspend 1 VM(s)
... [DRYRUN]   <path-to-your-vm>.vmx -> (not suspended, dry run)
```

The log is written to `VMGuard.log`, next to the `VMGuard.exe` you ran.

## Uninstall

```powershell
.\VMGuard.exe --uninstall
```

Or uninstall it from **Settings → Apps → Installed apps → VMGuard → Uninstall**. Either way it removes VMGuard from the startup apps and the Installed apps list, and stops it. The `%USERPROFILE%\VMGuard` folder (program, settings and log) is not deleted; you can remove it by hand.

## Antivirus false positives

VMGuard is a small, unsigned program that starts at sign-in and holds back Windows shutdown for a moment, which is also what some malware does. Because of that, antivirus heuristics (for example Microsoft Defender's machine-learning detections ending in `!ml`, such as `Trojan:Win32/Bearfoos.A!ml`) can flag it by mistake. Release 1.0.1 avoids the patterns most likely to trigger this: it no longer kills processes or calls `schtasks.exe`, and the exe has an icon and full version information.

If Defender still flags it:

1. Check that the file is the one from the release: `Get-FileHash .\VMGuard.exe` must match `SHA256SUMS.txt`.
2. Report it as a false positive at the [Microsoft file submission portal](https://www.microsoft.com/en-us/wdsi/filesubmission): sign in, choose *Home customer* (or *Software developer*), product *Microsoft Defender Antivirus*, upload `VMGuard.exe`, select *Incorrectly detected as malware/malicious* and enter the detection name. Microsoft usually updates its definitions within a few days.
3. Meanwhile, you can restore it from *Windows Security → Virus & threat protection → Protection history* and choose *Allow on device*.

Code-signing the exe would make these detections much less likely; it needs a code-signing certificate (for example through Azure Trusted Signing or SignPath's free plan for open-source projects).

## Recommended fallback: hibernation

If the battery runs out before VMGuard can act, hibernation is the safety net: the PC hibernates with the VMs as they were. Check whether it is available with `powercfg /a` and, if it isn't, enable it from an **administrator** console:

```powershell
powercfg /h on
```

Then set **"Hibernate"** as the critical battery action in the power plan's advanced settings.

## Limitations

- A **forced power-off** (holding the power button, a power cut, the battery running out without hibernation) gives no program any time.
- If someone clicks **"Shut down anyway"**, the VMs are powered off without being suspended.
- It only sees the VMs of the **signed-in user** (`vmrun -T ws`), which is the normal case with VMware Workstation.

## Building from source

You need the [.NET 8 SDK](https://dotnet.microsoft.com/download) (only to build; the resulting `.exe` uses .NET Framework 4.8).

```powershell
dotnet build VMGuard.sln -c Release
dotnet test VMGuard.sln -c Release
```

The executable ends up in `src\VMGuard\bin\Release\net48\VMGuard.exe`. GitHub Actions builds and runs the tests on every push and pull request. To publish a release with the Windows assets, push a `v*` tag or run the **Build** workflow by hand with the tag name (e.g. `v1.0.1`) in the `release` field.

| File | Purpose |
|---|---|
| `src/VMGuard/Program.cs` | Entry point: options, single instance, startup. |
| `src/VMGuard/GuardForm.cs` | Invisible window (shutdown notification), battery timer and tray icon. |
| `src/VMGuard/VmSuspender.cs` | Lists and suspends VMs with `vmrun`, also in the background. |
| `src/VMGuard/VmrunLocator.cs` | `vmrun.exe` detection. |
| `src/VMGuard/Installer.cs` | Install and uninstall. |
| `src/VMGuard/Registration.cs` | Startup (`Run` key) and Installed apps entries, per user. |
| `tests/VMGuard.Tests` | Unit tests. |

## Notes

- The running copy lives in `%USERPROFILE%\VMGuard`. It is best kept outside synced folders (OneDrive, Dropbox, etc.) so syncing or "files on demand" don't interfere. For the same reason it is not installed under `AppData`: packaged (MSIX) apps see an isolated copy of that folder.
- Everything is per user (`HKEY_CURRENT_USER`): the startup entry is the `VMGuard` value under `Software\Microsoft\Windows\CurrentVersion\Run`, and the Installed apps entry is `Software\Microsoft\Windows\CurrentVersion\Uninstall\VMGuard`.
