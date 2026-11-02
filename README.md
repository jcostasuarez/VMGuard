# VMGuard

Suspende automáticamente todas las máquinas virtuales de **VMware Workstation** cuando:

- Windows se **apaga, reinicia o cierra sesión**.
- La **batería baja del 10 %** (configurable) con el equipo desenchufado.

VMware Workstation (probado con 17.6.4) no tiene una opción nativa para suspender las VMs al apagar el host: si Windows se apaga con VMs encendidas, se cierran de golpe o el apagado queda trabado. VMGuard soluciona eso.

## Requisitos

- Windows 10/11 con Windows PowerShell 5.1.
- VMware Workstation instalado. `VMGuard.ps1` busca `vmrun.exe` solo: primero en el registro de VMware (`InstallPath`), después en el `PATH` y por último en las carpetas por defecto de `Program Files`. Si no lo encuentra, lo anota en el log y se cierra; en ese caso indicá la ruta con `-VmrunPath` al instalar.

## Archivos

| Archivo | Para qué sirve |
|---|---|
| `VMGuard.ps1` | Script principal. Corre oculto en tu sesión de usuario. |
| `Instalar-VMGuard.ps1` | Copia `VMGuard.ps1` a `%USERPROFILE%\VMGuard` y crea la tarea programada **VMGuard**, que lo arranca al iniciar sesión. No necesita permisos de administrador. |
| `Desinstalar-VMGuard.ps1` | Borra la tarea programada y detiene el script. |

La copia que se ejecuta queda en `%USERPROFILE%\VMGuard`. Conviene que esté fuera de carpetas sincronizadas (OneDrive, Dropbox, etc.) para que la sincronización o los "archivos a petición" no interfieran.

## Cómo funciona

El script usa `vmrun.exe`:

- `vmrun -T ws list` para ver qué VMs están encendidas.
- `vmrun -T ws suspend "<ruta.vmx>"` para suspender cada una. Las suspende todas en paralelo y espera hasta 5 minutos.

Al arrancar, el log muestra qué `vmrun.exe` encontró.

### Al apagar, reiniciar o cerrar sesión

El script abre una ventana invisible que recibe el aviso de apagado de Windows (`WM_QUERYENDSESSION`). Cuando llega ese aviso:

1. Retiene el apagado con `ShutdownBlockReasonCreate`. Windows muestra la pantalla de espera con el mensaje *"Suspendiendo maquinas virtuales VMware..."*.
2. Suspende todas las VMs.
3. Libera el apagado, y Windows continúa con el apagado o reinicio que habías pedido.

> ⚠️ **No toques "Apagar de todos modos"** en esa pantalla, porque Windows mata los procesos y las VMs se cierran sin suspenderse.

### Con batería baja

Cada 30 segundos revisa el estado de la energía. Si el equipo está **desenchufado** y la batería está **en el umbral o por debajo** (10 % por defecto), suspende las VMs. Si encendés una VM de nuevo en esa situación, la vuelve a suspender en la siguiente revisión.

Esta suspensión corre en segundo plano, así que la ventana invisible sigue atendiendo los avisos de Windows. Si apagás la PC mientras se están suspendiendo las VMs por batería, Windows muestra enseguida la pantalla de espera; VMGuard espera a que termine la suspensión en curso (sin lanzar otra sobre las mismas VMs) y después hace una pasada final por si quedó alguna encendida.

Elegí un umbral por encima del nivel de batería crítica de tu plan de energía de Windows, para que las VMs se suspendan antes de que Windows actúe.

### Log

Todo queda registrado en `%USERPROFILE%\VMGuard\VMGuard.log`: el inicio del script, cada suspensión con el código de salida de `vmrun` (`exit 0` = OK) y los errores.

## Instalación

Desde la carpeta donde descargaste los scripts:

```powershell
powershell -ExecutionPolicy Bypass -File .\Instalar-VMGuard.ps1 -UmbralBateria 10
```

`-UmbralBateria` es opcional (por defecto 10). `-VmrunPath "<ruta>\vmrun.exe"` también es opcional y solo hace falta si VMware está en una ruta que no se detecta sola. Reinstalar es seguro: sobrescribe la copia y la tarea.

**Si modificás `VMGuard.ps1`**, volvé a correr el instalador para que la copia en ejecución se actualice:

```powershell
.\Desinstalar-VMGuard.ps1; .\Instalar-VMGuard.ps1
```

### Comprobar que está corriendo

```powershell
Get-ScheduledTask VMGuard | Select-Object State          # debe decir Running
Get-Content $env:USERPROFILE\VMGuard\VMGuard.log -Tail 10
```

### Probarlo

Con una VM encendida, reiniciá la PC. Al volver a entrar, la VM debería estar suspendida y el log debería mostrar:

```
... Apagado/cierre de sesion -> suspendiendo 1 VM(s)
...   <ruta-a-tu-vm>.vmx -> exit 0
```

### Modo de prueba (`-DryRun`)

Para ver qué haría VMGuard sin suspender nada, corré el script a mano con `-DryRun`:

```powershell
powershell -ExecutionPolicy Bypass -File .\VMGuard.ps1 -DryRun
```

En modo de prueba:

- **Nunca** ejecuta `vmrun suspend`. Solo usa `vmrun -T ws list` para ver qué VMs están encendidas.
- Cada línea del log lleva el prefijo `[DRYRUN]` y además se muestra en la consola.
- Al arrancar registra el estado actual: porcentaje de batería, si está enchufado y qué VMs están encendidas.
- Cuando se cumple una condición (batería baja o aviso de apagado), registra el motivo y cada VM que se suspendería.
- Puede correr al mismo tiempo que la copia instalada sin chocar con ella.

Para salir, cerrá la ventana de PowerShell.

Para probar la batería baja sin esperar a que se descargue, subí el umbral y desenchufá el equipo:

```powershell
powershell -ExecutionPolicy Bypass -File .\VMGuard.ps1 -DryRun -UmbralBateria 100 -IntervaloSeg 5
```

El log debería mostrar algo así:

```
... [DRYRUN] Bateria baja (87%, desenchufado, umbral 100%) -> se suspenderian 1 VM(s)
... [DRYRUN]   <ruta-a-tu-vm>.vmx -> (no suspendida, modo prueba)
```

El log se escribe en `VMGuard.log`, junto al `VMGuard.ps1` que ejecutaste.

## Desinstalación

```powershell
powershell -ExecutionPolicy Bypass -File .\Desinstalar-VMGuard.ps1
```

La carpeta `%USERPROFILE%\VMGuard` (script y log) no se borra. Podés eliminarla a mano.

## Respaldo recomendado: hibernación

Si la batería se agota sin que VMGuard llegue a actuar, la hibernación es la red de seguridad: la PC hiberna con las VMs tal como estaban. Revisá si está disponible con `powercfg /a` y, si no lo está, activala en una consola **como administrador**:

```powershell
powercfg /h on
```

Después configurá **"Hibernar"** como acción de batería crítica en las opciones avanzadas del plan de energía.

## Limitaciones

- Un **apagado forzado** (mantener el botón de encendido, corte de luz, batería agotada sin hibernación) no le da tiempo a ningún script.
- Si alguien pulsa **"Apagar de todos modos"**, las VMs se cierran sin suspenderse.
- Solo ve las VMs del **usuario que inició sesión** (`vmrun -T ws`), que es lo normal en VMware Workstation.

## Notas

- **No instalar en `AppData` desde una app empaquetada (MSIX).** Esas apps escriben en una copia aislada de `%LOCALAPPDATA%` que el Programador de tareas no ve, y la tarea falla con `0xFFFD0000` (no encuentra el archivo de `-File`). Por eso se instala en `%USERPROFILE%\VMGuard`.
- `0x41301` en el resultado de la tarea significa "en ejecución": es lo esperado.
- Los scripts no llevan tildes en el código a propósito. Windows PowerShell 5.1 lee los `.ps1` sin BOM como ANSI, y los caracteres acentuados se rompen.
