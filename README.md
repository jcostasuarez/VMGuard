# VMGuard

Suspende automáticamente todas las máquinas virtuales de **VMware Workstation** cuando:

- Windows se **apaga, reinicia o cierra sesión**.
- La **batería baja del 10 %** (configurable) con el equipo desenchufado.

VMware Workstation (probado con 17.6.4) no tiene una opción nativa para suspender las VMs al apagar el host: si Windows se apaga con VMs encendidas, se cierran de golpe o el apagado queda trabado. VMGuard soluciona eso.

VMGuard es un único `VMGuard.exe` escrito en C#. No necesita instalar nada más: usa .NET Framework 4.8, que ya viene con Windows 10 y 11.

## Requisitos

- Windows 10/11.
- VMware Workstation instalado. VMGuard busca `vmrun.exe` solo: primero en el registro de VMware (`InstallPath`), después en el `PATH` y por último en las carpetas por defecto de `Program Files`. Si no lo encuentra, lo anota en el log y se cierra; en ese caso indicá la ruta con `--vmrun` al instalar.

## Descargar

- **Versión publicada:** en la página de [Releases](../../releases) del repo, descargá `VMGuard.exe`.
- **Última compilación:** en la pestaña [Actions](../../actions/workflows/build.yml), abrí la última ejecución correcta y descargá el artefacto `VMGuard` (es un `.zip` con el `.exe`).

Si Windows SmartScreen avisa que el programa no es conocido (el `.exe` no está firmado), elegí *Más información → Ejecutar de todas formas*.

## Instalación

Desde la carpeta donde descargaste `VMGuard.exe`, en PowerShell o en `cmd`:

```powershell
.\VMGuard.exe --instalar --umbral 10
```

Eso:

1. Copia `VMGuard.exe` a `%USERPROFILE%\VMGuard`.
2. Crea la tarea programada **VMGuard**, que lo arranca oculto cada vez que iniciás sesión.
3. Lo arranca ya mismo y muestra un mensaje de confirmación.

No necesita permisos de administrador. Reinstalar es seguro: detiene la copia en ejecución, la reemplaza y actualiza la tarea. Para cambiar el umbral, volvé a instalar con otro valor.

**Si tenías la versión anterior en PowerShell**, no hace falta desinstalarla: el instalador reemplaza la tarea **VMGuard** y detiene el `VMGuard.ps1` que estuviera corriendo. Después podés borrar `VMGuard.ps1` de `%USERPROFILE%\VMGuard`.

### Opciones

| Opción | Para qué sirve |
|---|---|
| `--umbral N` | % de batería para suspender (1 a 100, por defecto 10). |
| `--intervalo N` | Segundos entre revisiones de la batería (por defecto 30). |
| `--vmrun "<ruta>\vmrun.exe"` | Ruta a `vmrun.exe`, solo si no se detecta sola. |
| `--sin-icono` | No mostrar el icono en la bandeja del sistema. |
| `--dryrun` | Modo de prueba (ver más abajo). |
| `--instalar` / `--desinstalar` | Instala o desinstala. |
| `--ayuda` | Muestra la ayuda. |

Las opciones que pases junto a `--instalar` son las que usa la tarea programada.

### Comprobar que está corriendo

Debería aparecer el icono de VMGuard (un escudo) en la bandeja del sistema, junto al reloj. Al pasar el mouse muestra la batería y el umbral. Si no lo ves, revisá en la flecha de iconos ocultos.

También podés comprobarlo desde PowerShell:

```powershell
Get-ScheduledTask VMGuard | Select-Object State          # debe decir Running
Get-Content $env:USERPROFILE\VMGuard\VMGuard.log -Tail 10
```

### Icono de la bandeja

Clic derecho sobre el icono:

- **Suspender VMs ahora:** suspende todas las VMs encendidas en el momento.
- **Abrir log:** abre `VMGuard.log` en el Bloc de notas (también con doble clic).
- **Salir de VMGuard:** lo cierra hasta el próximo inicio de sesión (pide confirmación).

Cuando VMGuard empieza a suspender VMs por batería baja, muestra una notificación.

## Cómo funciona

VMGuard usa `vmrun.exe`:

- `vmrun -T ws list` para ver qué VMs están encendidas.
- `vmrun -T ws suspend "<ruta.vmx>"` para suspender cada una. Las suspende todas en paralelo y espera hasta 5 minutos.

Al arrancar, el log muestra qué `vmrun.exe` encontró.

### Al apagar, reiniciar o cerrar sesión

VMGuard tiene una ventana invisible que recibe el aviso de apagado de Windows (`WM_QUERYENDSESSION`), y le pide a Windows recibir ese aviso antes que la mayoría de los programas. Cuando llega:

1. Retiene el apagado con `ShutdownBlockReasonCreate`. Windows muestra la pantalla de espera con el mensaje *"Suspendiendo maquinas virtuales VMware..."*.
2. Suspende todas las VMs.
3. Libera el apagado, y Windows continúa con el apagado o reinicio que habías pedido.

> ⚠️ **No toques "Apagar de todos modos"** en esa pantalla, porque Windows mata los procesos y las VMs se cierran sin suspenderse.

### Con batería baja

Cada 30 segundos (o lo que indique `--intervalo`) revisa el estado de la energía. Si el equipo está **desenchufado** y la batería está **en el umbral o por debajo**, suspende las VMs. Si encendés una VM de nuevo en esa situación, la vuelve a suspender en la siguiente revisión.

Esta suspensión corre en segundo plano, así que la ventana invisible sigue atendiendo los avisos de Windows. Si apagás la PC mientras se están suspendiendo las VMs por batería, Windows muestra enseguida la pantalla de espera; VMGuard espera a que termine la suspensión en curso (sin lanzar otra sobre las mismas VMs) y después hace una pasada final por si quedó alguna encendida.

Elegí un umbral por encima del nivel de batería crítica de tu plan de energía de Windows, para que las VMs se suspendan antes de que Windows actúe.

### Log

Todo queda registrado en `VMGuard.log`, junto al `VMGuard.exe` que se ejecuta (`%USERPROFILE%\VMGuard\VMGuard.log` en la copia instalada): el inicio, cada suspensión con el código de salida de `vmrun` (`exit 0` = OK) y los errores.

## Probarlo

Con una VM encendida, reiniciá la PC. Al volver a entrar, la VM debería estar suspendida y el log debería mostrar:

```
... Apagado/cierre de sesion -> suspendiendo 1 VM(s)
...   <ruta-a-tu-vm>.vmx -> exit 0
```

### Modo de prueba (`--dryrun`)

Para ver qué haría VMGuard sin suspender nada:

```powershell
.\VMGuard.exe --dryrun
```

En modo de prueba:

- Se abre una consola donde se ven los mensajes. Para salir, cerrá esa consola.
- **Nunca** ejecuta `vmrun suspend`. Solo usa `vmrun -T ws list` para ver qué VMs están encendidas.
- Cada línea del log lleva el prefijo `[DRYRUN]`.
- Al arrancar registra el estado actual: porcentaje de batería, si está enchufado y qué VMs están encendidas.
- Cuando se cumple una condición (batería baja, aviso de apagado o *Suspender VMs ahora* en el icono), registra el motivo y cada VM que se suspendería.
- Puede correr al mismo tiempo que la copia instalada sin chocar con ella.

Para probar la batería baja sin esperar a que se descargue, subí el umbral y desenchufá el equipo:

```powershell
.\VMGuard.exe --dryrun --umbral 100 --intervalo 5
```

La consola debería mostrar algo así:

```
... [DRYRUN] Bateria baja (87%, desenchufado, umbral 100%) -> se suspenderian 1 VM(s)
... [DRYRUN]   <ruta-a-tu-vm>.vmx -> (no suspendida, modo prueba)
```

El log se escribe en `VMGuard.log`, junto al `VMGuard.exe` que ejecutaste.

## Desinstalación

```powershell
.\VMGuard.exe --desinstalar
```

Borra la tarea programada y detiene VMGuard. La carpeta `%USERPROFILE%\VMGuard` (programa y log) no se borra; podés eliminarla a mano.

## Respaldo recomendado: hibernación

Si la batería se agota sin que VMGuard llegue a actuar, la hibernación es la red de seguridad: la PC hiberna con las VMs tal como estaban. Revisá si está disponible con `powercfg /a` y, si no lo está, activala en una consola **como administrador**:

```powershell
powercfg /h on
```

Después configurá **"Hibernar"** como acción de batería crítica en las opciones avanzadas del plan de energía.

## Limitaciones

- Un **apagado forzado** (mantener el botón de encendido, corte de luz, batería agotada sin hibernación) no le da tiempo a ningún programa.
- Si alguien pulsa **"Apagar de todos modos"**, las VMs se cierran sin suspenderse.
- Solo ve las VMs del **usuario que inició sesión** (`vmrun -T ws`), que es lo normal en VMware Workstation.

## Compilar desde el código

Hace falta el [SDK de .NET 8](https://dotnet.microsoft.com/download) (solo para compilar; el `.exe` resultante usa .NET Framework 4.8).

```powershell
dotnet build VMGuard.sln -c Release
dotnet test VMGuard.sln -c Release
```

El ejecutable queda en `src\VMGuard\bin\Release\net48\VMGuard.exe`. GitHub Actions compila y corre los tests en cada push y pull request; al subir un tag `v*` publica el `.exe` en Releases.

| Archivo | Para qué sirve |
|---|---|
| `src/VMGuard/Program.cs` | Punto de entrada: opciones, instancia única, arranque. |
| `src/VMGuard/GuardForm.cs` | Ventana invisible (aviso de apagado), temporizador de batería e icono de la bandeja. |
| `src/VMGuard/VmSuspender.cs` | Lista y suspende las VMs con `vmrun`, también en segundo plano. |
| `src/VMGuard/VmrunLocator.cs` | Detección de `vmrun.exe`. |
| `src/VMGuard/Installer.cs` | Instalación y desinstalación (tarea programada). |
| `tests/VMGuard.Tests` | Tests unitarios. |

## Notas

- La copia que se ejecuta queda en `%USERPROFILE%\VMGuard`. Conviene que esté fuera de carpetas sincronizadas (OneDrive, Dropbox, etc.) para que la sincronización o los "archivos a petición" no interfieran. Por el mismo motivo no se instala en `AppData`: las apps empaquetadas (MSIX) ven una copia aislada de esa carpeta que el Programador de tareas no ve.
- `0x41301` en el resultado de la tarea significa "en ejecución": es lo esperado.
