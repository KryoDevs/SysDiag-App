# SysDiag 5.13.0

[![Compilar y probar](https://github.com/KryoDevs/SysDiag-App/actions/workflows/build.yml/badge.svg)](https://github.com/KryoDevs/SysDiag-App/actions/workflows/build.yml) [![Validar fixture](https://github.com/KryoDevs/SysDiag-App/actions/workflows/validate-fixture.yml/badge.svg)](https://github.com/KryoDevs/SysDiag-App/actions/workflows/validate-fixture.yml)

Aplicación de escritorio para Windows que diagnostica red, rendimiento, térmicas y
estabilidad, limpia temporales bajo confirmación y aplica ajustes con respaldo limitado. Interfaz gráfica
en WPF, registro de actividad e informe HTML exportable.

## Cómo obtener el .exe

El proyecto se entrega como código fuente. Para compilarlo:

1. Descomprime la carpeta `SysDiag` donde quieras.
2. Doble clic en **`build.bat`**.
3. El ejecutable queda en `publish\SysDiag.exe`.

Instala primero el **SDK .NET 8** desde <https://dotnet.microsoft.com/download/dotnet/8.0>.
El script usa `global.json` para seleccionar ese SDK y puede reutilizar una copia propia
ya instalada en `%LOCALAPPDATA%\SysDiag\dotnet-sdk`. No descarga ni ejecuta scripts
remotos automáticamente. Se necesita internet para restaurar paquetes NuGet la primera vez.

El `.exe` resultante es **autocontenido**: incluye el runtime de .NET y WPF; su tamaño depende del SDK y del bundle.
Está dirigido a Windows 10/11 x64 compatibles con .NET 8. La compatibilidad con cada
hardware/configuración requiere pruebas reales, no se garantiza por el tamaño del EXE. Se puede copiar a un pendrive y ejecutar en otro equipo.

Si prefieres un archivo pequeño (unos 300 KB) a cambio de exigir .NET 8 instalado en la
máquina destino, edita `SysDiag.csproj` y cambia `<SelfContained>true</SelfContained>`
por `false`.

## Estructura

```
SysDiag/
├─ SysDiag.csproj
├─ App.xaml / App.xaml.cs        Arranque, cultura, instancia única, captura de errores
│
├─ Models/                       DTOs y reglas de fusión/normalización. Namespace SysDiag.Models
│  ├─ DiagnosticModels.cs        Severity, Finding, DiagnosticReport (+ MergeFrom)
│  ├─ HardwareModels.cs          KeyValueRow, DiskRow, MemoryRow, GpuInfo
│  ├─ NetworkModels.cs           LatencyResult, TraceHop, WifiNetworkRow
│  ├─ StorageModels.cs           StorageRow, CleanupRow
│  ├─ DriverModels.cs            DriverRow, DriverUpdateRow, UpdateRow
│  ├─ SecurityModels.cs          SecurityCheckRow
│  ├─ ProcessModels.cs / EventModels.cs / SoftwareModels.cs
│
├─ Core/                         Recolección de datos. Un namespace por subcarpeta
│  ├─ AppEnv.cs, ComWorker.cs, WmiHelper.cs     infraestructura transversal (SysDiag.Core)
│  ├─ Stats.cs                   percentiles, desviación, jitter y escalas (funciones puras)
│  ├─ Hardware/                  SystemModule, ThermalModule, GpuModule
│  ├─ Performance/                PerformanceModule
│  ├─ Network/                   NetworkModule (latencia, Wi-Fi, canales, traceroute)
│  ├─ Storage/                   StorageModule (SMART), CleanupModule
│  ├─ Security/                  SecurityModule (Defender, Firewall, BitLocker, TPM, Secure Boot, UAC)
│  ├─ Drivers/                   DriverModule, DriverUpdateModule, DriverVerifier, AuthenticodeVerifier
│  ├─ Windows/                   OptimizeModule, PowerSettings, OptimizationBackupStore, SecureBackupDirectory,
│  │                            RestorePointModule, SettingsService, StartupModule, UpdateModule (winget),
│  │                            ActivationModule (licencia de Windows por canales oficiales)
│  └─ Diagnostics/               HealthScore, Remediation, ReportBuilder, Exporter, StabilityModule
│
├─ Services/                     Contrato hacia la UI. Namespace SysDiag.Services
│  ├─ Interfaces.cs               IDiagnosticService y las 8 interfaces de dominio
│  ├─ HardwareService, NetworkService, SecurityService, DriverService, StorageService
│  ├─ PerformanceService, StabilityService, StartupService   (sin interfaz dedicada)
│  └─ ScanService, RepairService, ReportService, HistoryService
│
├─ Diagnostics/                  Motor de reglas. Namespace SysDiag.Diagnostics
│  ├─ DiagnosticRule.cs           Interfaz IDiagnosticRule
│  ├─ DiagnosticEngine.cs         Corre las reglas registradas sobre un reporte ya recolectado
│  └─ CpuRules.cs, MemoryRules.cs Ejemplos reales, ya conectados (ver nota abajo)
│
└─ Ui/                            Capa visual en WPF
   ├─ Theme.xaml                  Sistema de diseño: paleta, tipografía, escalas y plantillas
   ├─ MainWindow.xaml             Barra superior + rail de navegación y cuatro vistas
   ├─ MainViewModel.cs            Estado observable y orquestación de sesión
   ├─ Charts.cs / Converters.cs   gráficos: barras con rejilla, líneas, anillos y series en vivo
   ├─ Dialog.xaml                 Diálogos propios (no MessageBox)
   ├─ CleanupWindow / OptimizeWindow / ProfilesWindow   acciones con confirmación
   ├─ HistoryWindow / CompararWindow / SettingsWindow      historial y diff entre dos diagnósticos
   ├─ SmartWindow                           atributos SMART del disco, uno por uno
   ├─ ConsumoWindow                         quién mueve el disco y quién tiene la red abierta
   ├─ PingMonitorWindow                     monitor de latencia en vivo
   └─ ActivacionWindowsWindow     licencia de Windows 10/11 (solo canales oficiales)
```

## Módulos

| Módulo | Qué mide | Admin |
|---|---|---|
| Diagnóstico completo | Todos los de abajo en una pasada | recomendado |
| Red y latencia | RTT, jitter y pérdida contra router, internet y chat regional de Riot (LAS/LAN); señal, banda y canal Wi-Fi; traceroute hacia el destino más relevante | no |
| Rendimiento | CPU por proceso con muestreo real, RAM, cola de disco | no |
| Consumo por proceso | Lectura y escritura de disco por proceso, y conexiones de red por PID con su destino | no |
| Térmicas y energía | Temperatura ACPI, frecuencia actual vs. nominal, throttling, plan de energía, desgaste de batería (capacidad de diseño vs. actual) | no |
| Estabilidad | Kernel-Power 41, BugCheck, WHEA (catálogo general + escaneo dedicado por proveedor), errores de disco, minidumps | recomendado |
| Limpieza | Calcula, confirma y borra temporales reportando lo liberado | parcial |
| Drivers | Inventario de drivers con antigüedad, foco en almacenamiento/chipset/red | no |
| Optimizar | DNS, reparación de WLAN, plan de energía, reinicio de pila TCP/IP | sí |
| Restaurar | Revierte los valores capturados antes del primer ajuste pendiente; no TCP/IP/IP fija/VPN | sí |
| Monitor de ping | Latencia en vivo hacia el router o internet: último valor, promedio, máximo y pérdida | no |
| SMART del disco | Atributos SMART: sectores reasignados y pendientes, errores de la interfaz, vida útil restante del SSD | recomendado |
| Comparar | Diferencia entre dos diagnósticos: hallazgos nuevos, resueltos, los que empeoraron y los que mejoraron | no |
| Perfiles | Combinaciones de optimización (universidad, trabajo, juego) con respaldo previo | sí |
| Ajustes | Muestreo, ventanas de eventos, retención de historial y registros | no |
| Activación de Windows | Estado de la licencia y activación por canales oficiales de Microsoft | sí* |

\*La consulta no pide permisos; instalar una clave o cambiar el host KMS sí los pide,
porque los aplica `slmgr.vbs`, la utilidad del propio Windows.

### Sobre el módulo de Drivers

El inventario de drivers es de **solo lectura**. La búsqueda e instalación opcional
usa el Agente de Windows Update, requiere confirmación, permisos y un punto de
restauración exitoso; puede requerir reiniciar y no se prueba automáticamente en hardware
de usuarios. También se puede abrir Windows Update o el soporte oficial del fabricante.

**Instaladores locales EXE/MSI/INF/CAB/ZIP no se ejecutan desde SysDiag.** La verificación
con `WinVerifyTrust` y antivirus es informativa: no demuestra compatibilidad de hardware.
Si no se puede comprobar firma/integridad/antivirus, el estado es desconocido, no aprobado.

La pestaña **Resumen** muestra el estado como tarjetas (dashboard): estado general, equipo,
latencia de referencia, CPU/RAM, desgaste de batería y conteo de errores WHEA/eventos
críticos — se arma sola con lo que haya en el último diagnóstico corrido.

La app arranca sin pedir UAC. Cuando eliges un módulo que lo necesita, ofrece reiniciarse
elevada; también hay un enlace permanente en la cabecera.

## Salidas

Los reportes, CSV/JSON, historial y logs se guardan en `Documentos\SysDiag\`;
si esa carpeta está bloqueada se usa `LocalAppData\SysDiag`. Los nombres incluyen fecha,
fracciones y un ID para no sobrescribir exportaciones. El HTML muestra estado, alcance y
duración registrada; un archivo antiguo sin Fin no inventa duración.

El respaldo que guía cambios elevados se guarda separado:
`%ProgramData%\SysDiag-Backups\<SID>\estado-previo.json`, con propietario
Administradores/SYSTEM y ACL que impide escritura sin elevar. Se conserva la primera
captura hasta restaurar. Los respaldos antiguos de Documentos no se importan automáticamente
ni se borran; revisar sus valores manualmente si se necesita recuperar aquel estado.

## Licencia y activación

SysDiag incluye **prueba de 14 días** con todas las funciones y, al terminar,
un modelo de lectura libre: el diagnóstico y la consulta nunca se bloquean, y
las acciones que modifican el equipo (optimizar, instalar, limpiar, reparar)
piden un **código de activación**.

- Los códigos tienen el formato `SDG7-AAAAA-BBBBB-CCCCC-DDDDD-EEEE` y se
  verifican **en el equipo, sin conexión** (HMAC-SHA256, base32 Crockford).
- La activación se gestiona desde la barra superior (etiqueta de licencia),
  desde `Ajustes ▸ Licencia` o al intentar una acción protegida.
- El emisor de códigos es `Tools/New-ActivationCode.ps1` (para quien vende las
  licencias): `-Dias 365 -Cantidad 10` emite; `-Verificar <código>` comprueba
  y muestra vigencia, serial y si está vinculado al equipo.

## Activación de Windows 10 y 11

**SysDiag no es un activador y no activa equipos sin licencia.** El módulo
`Activación de Windows` (panel izquierdo ▸ Sistema) muestra el estado real de la
licencia y ofrece los caminos previstos por Microsoft:

- **Leer el estado**: edición, clave parcial, canal (Retail, OEM, Volumen KMS o
  MAK), vencimiento, host KMS y días de gracia, desde
  `SoftwareLicensingProduct` por WMI. No requiere permisos elevados.
- **Instalar una clave que ya tengas**: `slmgr.vbs /ipk` + `/ato`. Pide
  administrador y pide confirmación antes de escribir.
- **Activación por volumen**: apuntar al host KMS **propio** de tu organización
  (`slmgr.vbs /skms` + `/ato`). Es el mecanismo corporativo previsto; apuntarlo
  a un host ajeno activaría Windows sin licencia, y eso es justo lo que este
  módulo no hace.
- **Canales oficiales**: Ajustes ▸ Activación, Microsoft Store, el solucionador
  de problemas de Microsoft y la activación telefónica con el id. de instalación.

Lo que **no** hace: no emula servidores KMS, no inyecta licencias digitales
(HWID/KMS38), no genera ni valida claves ajenas y no modifica el servicio de
licencias. Además de ser una infracción de los términos de Microsoft, los
«activadores» que circulan son hoy una de las formas más habituales de
distribuir malware.

La clave nunca se registra completa en el log: se guardan solo los últimos cinco
caracteres, porque los registros se comparten cuando se pide soporte.

## Ajustes de Windows 10 y 11

`Ajustes de Windows` (panel izquierdo ▸ Mantenimiento) reúne ajustes
reversibles agrupados en Privacidad, Rendimiento, Explorador y Sistema:
telemetría, ID de publicidad, sugerencias y anuncios del menú Inicio, búsqueda
local, efectos visuales, programación de GPU por hardware, modo Juego, arranque
rápido, prioridad para juegos, extensiones de archivo, menú contextual clásico
(Windows 11), alineación de la barra de tareas, Widgets/Chat, servicios
(SysMain, WSearch, DiagTrack), exclusión de drivers en Windows Update y más.

Cada ajuste muestra su riesgo y sus requisitos (admin, cerrar sesión,
reiniciar). Antes de escribir se guarda el valor anterior en
`LocalAppData\SysDiag\ajustes-respaldo.json`: cada ajuste se revierte de uno
en uno o todos a la vez, y también desde `Restaurar estado`. Si alguna opción
necesita administrador, SysDiag lo dice antes de tocar nada y ofrece
reiniciarse elevado.

## Antes y después de tocar el equipo

SysDiag puede cambiar configuración, así que todo lo que escribe pasa por las
mismas cuatro garantías:

- **Ensayo.** El botón «Ensayar (no aplica nada)» de `Ajustes de Windows`
  muestra exactamente qué claves y qué valores cambiarían —y cuáles fallarían
  por falta de permisos— sin escribir nada.
- **Verificación.** Después de escribir, SysDiag **relee** y compara. Un ajuste
  se escribe y la llamada devuelve sin error tanto si se aplicó como si una
  directiva de grupo lo repuso enseguida: los dos casos se ven igual desde el
  código que escribe, y solo volviendo a leer se distinguen. El resultado separa
  «ya estaba así» de «se escribió y no quedó».
- **Deshacer por paso.** `Cambios aplicados` (panel izquierdo ▸ Sistema) lista
  todo lo que SysDiag cambió, en orden, con un botón por paso. Lo que no tiene
  vuelta atrás —borrar temporales, reiniciar la pila TCP/IP— se registra igual y
  marcado, con el motivo: un historial que solo anotara lo reversible mentiría
  por omisión justo en los casos que importan.
- **Restauración selectiva.** Si un plan de energía del respaldo ya no existe,
  `Restaurar estado` omite lo que falta y lo nombra, en vez de no restaurar
  nada por una pieza que borró otro programa.

Además, cada corrida declara lo que costó: el pie muestra el tiempo de CPU y el
pico de memoria de la propia medición. Una herramienta que mide perturba aquello
que mide, y conviene que eso se vea.

## Informes y exportación

El botón `Generar informe` abre un diálogo con cinco formatos, porque no es lo
mismo quedárselo que mandarlo:

| Formato | Para qué |
|---|---|
| **HTML** | Abrirlo en el navegador o archivarlo. Todos los datos. |
| **HTML redactado** | Mandarlo a soporte o publicarlo en un foro. |
| **Markdown** | Pegarlo en un foro o un ticket. |
| **Markdown redactado** | Lo mismo, sin datos identificables. |
| **JSON** | Todos los datos, para procesarlos con otro programa. |

La versión **redactada** quita el nombre del equipo, el del usuario, la ruta del
perfil, los números de serie, los nombres de red Wi-Fi y las direcciones MAC, y
lo aplica sobre una **copia**: el informe que queda en tu carpeta sigue
completo, porque es tuyo. Las redes se numeran, no se sustituyen todas por el
mismo texto, así que dos redes distintas siguen siendo distinguibles y un
problema de solapamiento de canales se sigue pudiendo ver. Y el propio informe
dice qué se le quitó: uno censurado en silencio confunde a quien lo recibe.

## Paleta de comandos

`Ctrl+K` (o el botón `Buscar` de la barra superior) abre un campo que filtra y
ejecuta módulos, acciones de la sección activa, exportaciones y vistas. Busca
por relevancia, no por orden alfabético: «red» pone `Red y latencia` primero, y
«disco» encuentra `Almacenamiento`. Cada entrada ejecuta exactamente lo mismo
que ejecutaría el botón o el ítem del rail, porque llama al mismo método.

## Pantallazos azules

Si hay volcados en `C:\Windows\Minidump`, la tabla **Pantallazos** de la vista
Datos dice el código de detención, qué significa en palabras llanas y qué
controladores de terceros estaban cargados en ese momento.

Dos límites, dichos de frente: el código de detención **no** está en el
minidump —se lee del informe de errores de Windows, que es de donde lo sacan
también las herramientas de terceros— y los módulos se listan como **candidatos**,
no como culpables. Señalar cuál falló exige análisis de pila (WinDbg); acá se
lista lo accionable sin instalar nada.

## CI, pruebas y distribución

- Windows compila la solución, ejecuta regresiones y conserva TRX. Se incluyen ramas
  `arena/**`; no se publica un release por trabajar en una rama.
- `Tools/validate_tests.ps1` exige ≥266 pruebas (114 de la suite auditada + 34 que miden el sistema visual + 4 de licencia + 11 del recorte de ventanas + 40 sobre funciones puras del lote 5.9 + 15 del lote «Confiar» + 14 del diff entre diagnósticos + 21 de SMART + 13 de consumo por proceso), todas aprobadas y sin omisiones.
- `Tools/validate_xaml.ps1` comprueba seis cosas sobre los XAML: bien formado, resolución de
  `{StaticResource}`, ámbito de cada `TargetName` dentro de su plantilla, que la propiedad animada exista
  en el tipo del elemento destino, que todo `RepeatBehavior="Forever"` nacido en un `Trigger` tenga su
  `StopStoryboard`, y que cada `{Binding X}` corresponda a un miembro real de su clase de datos. Es
  puerta en `build.yml` y también en `release.yml`. Las tres últimas cazan lo que WPF no reporta:
  animar una propiedad que el destino no tiene (`ScaleX` sobre un `TranslateTransform`, `Opacity`
  sobre un pincel) no lanza excepción, simplemente no anima y la vista queda muerta en el primer
  hover; un `Forever` sin `StopStoryboard` tampoco falla —sigue corriendo—; y un binding a un nombre
  inexistente no rompe la compilación, rompe en tiempo de ejecución y en silencio.
- Headless está en la solución y comparte `ScanService`; `--self-test` usa un doble
  sintético explícito, nunca sustituye mediciones de un diagnóstico real.
- `Tools/validate_release.ps1` arranca el **EXE publicado** con `--self-test` y comprueba
  recursos WPF, reglas y JSON, y construye todas las ventanas sin mostrarlas; no modifica hardware, red ni ajustes.
- Solo `release.yml` gestiona etiquetas existentes coincidentes con la versión del
  proyecto. Después de tests y gates prepara ZIP/checksums y crea un **borrador**.
- `Tools/build_installer.ps1` entrega la versión del proyecto a Inno Setup 6; la firma
  propia de distribución y la prueba real de instalador todavía son tareas manuales.

```powershell
dotnet restore SysDiag.sln
dotnet build SysDiag.sln -c Release --no-restore
dotnet test SysDiag.sln -c Release --no-build --logger "trx;LogFileName=tests.trx" --results-directory TestResults
.\Tools\validate_tests.ps1
dotnet publish SysDiag.csproj -c Release -o publish
.\Tools\validate_release.ps1 -Root .\publish
.\Tools\validate_version.ps1 -Root .\publish
```

> **Política de ejecución de PowerShell:** si al correr un `.ps1` aparece
> `UnauthorizedAccess` o «la ejecución de scripts está deshabilitada en este sistema»,
> es la política de ejecución de Windows (o la marca de web del ZIP descargado), no un
> fallo del proyecto. Ejecuta los scripts con
> `powershell -NoProfile -ExecutionPolicy Bypass -File .\Tools\validate_release.ps1 ...`
> o usa `Set-ExecutionPolicy -Scope Process Bypass` en tu sesión. `build.bat` ya invoca
> la validación con `-ExecutionPolicy Bypass`: solo afecta a esa sesión y no cambia la
> política del sistema.

Consulta [la auditoría y sus tres listas de diez](docs/AUDITORIA.md) para los fallos,
correcciones, evidencia de CI y límites pendientes, y [la segunda auditoría](docs/AUDITORIA_2026-10-09.md)
para la tanda posterior con su propia evidencia. Un CI anterior no certifica cambios posteriores;
cada informe identifica expresamente el SHA que validó.

El resto del conocimiento vive en tres archivos: [el análisis y el plan](docs/MEJORAS.md),
que es donde está qué se arregló, qué no y por qué; [la guía de diseño](docs/DISENO.md)
(paleta, tipografía, escalas, movimiento, componentes y lo que no se hace); y
`docs/preview/index.html`, una maqueta en HTML que reproduce el tema —ábrela en el navegador
para ver el movimiento y el contraste sin compilar nada. La maqueta usa los mismos tokens que
`Ui/Theme.xaml` por convención: si se cambia un tiempo o un color allá y no acá, la maqueta
pasa a mentir.

## Sobre la arquitectura (5.0)

La versión 5.0 reestructura el proyecto en capas físicas separadas:
`Models/` (datos puros) → `Core/` (recolección, un namespace por dominio de hardware)
→ `Services/` (el contrato hacia la UI, con interfaces) → `Diagnostics/` (motor de
reglas) → `Ui/` (WPF).

Dos decisiones que vale la pena explicar:

- **El motor de reglas convive con las reglas existentes, no las reemplaza.**
  `DiagnosticEngine` corre reglas registradas (`CpuRules`, `MemoryRules` por ahora)
  sobre el reporte ya recolectado. Portar automáticamente los ~40 hallazgos que ya
  generan los módulos de `Core/` a clases de regla individuales habría sido un
  refactor grande sin beneficio funcional — esa lógica ya está probada donde está.
  Los dos ejemplos SÍ reemplazaron su versión inline (se retiró de
  `PerformanceModule` para no duplicar el hallazgo), como muestra real del patrón
  para lo que se agregue de aquí en más.
- **Los namespaces de `Core/` no calzan 1:1 con dónde vive `AppEnv`/`ComWorker`/`Wmi`.**
  Quedaron en la raíz de `SysDiag.Core` porque son infraestructura transversal
  (registro, WMI, hilo COM) usada por todos los dominios; C# hace visibles los
  namespaces padre a los hijos sin necesidad de `using`, así que
  `SysDiag.Core.Network` ve `AppEnv` sin declarar nada extra.

Dos módulos nuevos, ambos reales — nada de datos de ejemplo:

- **Seguridad** (`Core/Security/SecurityModule.cs`): Defender vía
  `MSFT_MpComputerStatus`, Firewall vía `netsh`, BitLocker y TPM vía WMI, Secure
  Boot vía registro, UAC vía registro. Todo de solo lectura.
- **GPU** (`Core/Hardware/GpuModule.cs`): modelo y driver por
  `Win32_VideoController`, uso por clases CIM estables de motores 3D de Windows,
  no por nombres de contadores traducidos. Si la medición no está disponible,
  se indica falta de datos en vez de completar con un cero inventado.

## Sobre la interfaz

La capa visual está en **WPF**, no en WinForms. WinForms dibuja con GDI+ en píxeles
fijos y sus controles nativos (`TabControl`, `ComboBox`, `ListView`, `ProgressBar`,
barras de desplazamiento, barra de título) no aceptan tema: en modo oscuro quedaban
islas blancas imposibles de corregir. WPF es vectorial, escala solo por DPI y permite
retemplar cualquier control. Viene incluido en el SDK de .NET 8, así que no agrega
ninguna dependencia de NuGet.

El motor `Core/` no cambió ni una línea con la migración: esa separación estricta entre
medición y presentación fue justamente lo que la hizo barata.

Decisiones de diseño:

- **Paleta «medianoche»**: azul `#0B1020` de base, violeta `#8B7CFF` como acento de
  marca y cian `#38D6F0` como acento secundario. El cian es el color del logo y se
  reserva para lo que está vivo en ese instante: operación en curso, traza del
  monitor de ping, puntos del gráfico de evolución. La profundidad la da la
  elevación de la superficie, no el grosor del borde.
- **Un solo acento por pantalla.** El color semántico (verde, ámbar, rojo) califica
  datos y nunca decora: si todo compite por atención, nada la recibe.
- **Tres roles tipográficos**: Segoe UI Variable Display para titulares, Segoe UI
  Variable Text para el cuerpo, y Cascadia Mono con cifras tabulares para todo dato
  numérico, así las columnas no bailan al actualizarse.
- **El movimiento señala cambios de estado, no decora**: tres tiempos (0,11 s el
  puntero, 0,19 s lo que entra y sale, 0,34 s lo que acompaña un resultado) y
  cuatro curvas. El puntaje se cuenta y el arco barre; las vistas se cruzan con
  un desvanecimiento; la cascada de tarjetas tiene tope. Se anima solo `Opacity`
  y transformaciones —nunca layout ni el color de un pincel compartido—, y
  `Ui/Motion.cs` respeta la preferencia del sistema de no animar controles.
- **La regla de escala**: cada métrica del Resumen lleva debajo una serie de marcas
  que se llenan según su magnitud, como la escala de un instrumento. Es el elemento
  que da identidad a la interfaz y codifica lo que la aplicación hace: medir.
- **Barra de título propia**: la del sistema no se puede tematizar. Todas las
  ventanas, incluidas las secundarias, llevan la marca.
- **Diálogos propios**: `MessageBox` se dibuja en claro y rompe el conjunto.
- **Navegación en rail con la acción principal separada**: el diagnóstico completo
  es un botón arriba porque es lo que se hace al abrir la aplicación; el resto son
  módulos sueltos agrupados por lo que hacen, dentro de una lista con selección
  (el módulo activo queda marcado sin estado que sincronizar a mano).

El sistema completo —paleta, escalas, sombras, plantillas de control y reglas de
uso del color— está documentado en `docs/DISENO.md`.

## Decisiones técnicas

- **CPU medida por diferencia.** Se toman dos lecturas de `TotalProcessorTime` separadas
  por N segundos y se normaliza por número de núcleos. Leer el acumulado del proceso,
  como hace la mayoría de los scripts, solo premia a los procesos más antiguos.
- **Sin `Get-Counter` ni contadores por nombre.** Los nombres de contador vienen traducidos
  en un Windows en español y rompen el código. Se usan clases CIM, que son estables.
- **La configuración automática de WLAN nunca se desactiva.** Si se detecta apagada, se
  ofrece encenderla: dejarla así impide reconectarse solo a las redes guardadas.
- **Respaldo limitado y validado antes de cambiar.** DNS IPv4/origen, valores de
  registro y energía por plan se capturan; cachés, borrado y reset de TCP/IP no se
  revierten con ese JSON. Un error puede dejar cambios parciales: se informa y se
  conserva el respaldo, no se declara una transacción global exitosa.
- **El reinicio de la pila TCP/IP exige doble confirmación** y advierte de que borra IP fija,
  DNS personalizados y configuración de VPN. Exige un punto de restauración y no
  promete recuperar IP fija, rutas ni VPN desde el respaldo de SysDiag.
- **Jitter en vez de solo ping medio.** Es la métrica que explica los tirones en juego.

## Nota sobre antivirus

Un ejecutable recién compilado y sin firma digital puede activar SmartScreen la primera
vez ("Windows protegió su PC" → *Más información* → *Ejecutar de todas formas"). Es normal
en binarios propios. Para distribuirlo a terceros haría falta un certificado de firma de
código.

## Documentos

- `docs/AUDITORIA.md` y `docs/AUDITORIA_2026-10-09.md`: auditorías, con su evidencia de CI.
- `docs/MEJORAS.md`: análisis y plan de mejoras. La §8 es el lote 5.9 (gráficos,
  mediciones, interfaz por sección y activación) y la §9 el lote 5.10 (ensayo,
  verificación, deshacer por paso y primeras herramientas nuevas).
- `docs/HERRAMIENTAS_NUEVAS.md`: catálogo de herramientas nuevas, su límite explícito
  y el orden sugerido para encararlas.
- `docs/DISENO.md`: sistema visual y de movimiento.
- `docs/preview/index.html`: maqueta de la interfaz.

## Siguientes pasos

- Matriz manual Windows 10/11, UAC, GUI, Modern Standby, VPN/IP fija, WUA y restore.
- Más sensores/fixtures físicos para SMART, térmicas, GPU y Wi-Fi 6 GHz.
- Desacoplar más backends nativos de los servicios y evaluar umbrales/confianza del score.
- Firmar y probar el instalador; redactar datos de red/equipo antes de compartir informes.

## Fixtures para desarrollo y CI

Se incluye un diagnóstico real que sirve como fixture para pruebas de integración y para desarrollar la UI sin ejecutar WMI/COM. Está en:

- Tools\fixtures\diagnostico_fixture.json

Para validar localmente que el fixture contiene hallazgos relevantes, ejecutar (PowerShell):

PS> .\Tools\validate_fixture.ps1

El script devuelve código de salida 0 si la comprobación pasa. Usar este archivo como entrada para pruebas o para poblar vistas en desarrollo. Si se desea integrar pruebas xUnit en el repo, se puede añadir un proyecto de tests, pero puede requerir ajustar las propiedades de ensamblado del proyecto principal para evitar duplicidad de atributos en la compilación (GenerateAssemblyInfo).

