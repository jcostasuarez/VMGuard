# VMGuard

Suspende automáticamente todas las máquinas virtuales de **VMware Workstation** cuando:

- Windows se **apaga, reinicia o cierra sesión**.
- La **batería baja del 10 %** (configurable) con el equipo desenchufado.

VMware Workstation (probado con 17.6.4) no tiene una opción nativa para suspender las VMs al apagar el host: si Windows se apaga con VMs encendidas, se cierran de golpe o el apagado queda trabado. VMGuard soluciona eso.

## Requisitos

- Windows 10/11 con Windows PowerShell 5.1.
- VMware Workstation instalado en la ruta por defecto (`C:\Program Files (x86)\VMware\VMware Workstation\vmrun.exe`). Si está en otra ruta, cambiá la variable `$vmrun` al principio de `VMGuard.ps1`.

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

### Al apagar, reiniciar o cerrar sesión

El script abre una ventana invisible que recibe el aviso de apagado de Windows (`WM_QUERYENDSESSION`). Cuando llega ese aviso:

1. Retiene el apagado con `ShutdownBlockReasonCreate`. Windows muestra la pantalla de espera con el mensaje *"Suspendiendo maquinas virtuales VMware..."*.
2. Suspende todas las VMs.
3. Libera el apagado, y Windows continúa con el apagado o reinicio que habías pedido.

> ⚠️ **No toques "Apagar de todos modos"** en esa pantalla, porque Windows mata los procesos y las VMs se cierran sin suspenderse.

### Con batería baja

Cada 30 segundos revisa el estado de la energía. Si el equipo está **desenchufado** y la batería está **en el umbral o por debajo** (10 % por defecto), suspende las VMs. Si encendés una VM de nuevo en esa situación, la vuelve a suspender en la siguiente revisión.

Elegí un umbral por encima del nivel de batería crítica de tu plan de energía de Windows, para que las VMs se suspendan antes de que Windows actúe.

### Log

Todo queda registrado en `%USERPROFILE%\VMGuard\VMGuard.log`: el inicio del script, cada suspensión con el código de salida de `vmrun` (`exit 0` = OK) y los errores.

## Instalación

Desde la carpeta donde descargaste los scripts:

```powershell
powershell -ExecutionPolicy Bypass -File .\Instalar-VMGuard.ps1 -UmbralBateria 10
```

`-UmbralBateria` es opcional (por defecto 10). Reinstalar es seguro: sobrescribe la copia y la tarea.

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
