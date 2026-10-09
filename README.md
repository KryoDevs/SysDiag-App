# SysDiag 5.7.1

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
│  ├─ Hardware/                  SystemModule, ThermalModule, GpuModule
│  ├─ Performance/                PerformanceModule
│  ├─ Network/                   NetworkModule (latencia, Wi-Fi, canales, traceroute)
│  ├─ Storage/                   StorageModule (SMART), CleanupModule
│  ├─ Security/                  SecurityModule (Defender, Firewall, BitLocker, TPM, Secure Boot, UAC)
│  ├─ Drivers/                   DriverModule, DriverUpdateModule, DriverVerifier, AuthenticodeVerifier
│  ├─ Windows/                   OptimizeModule, PowerSettings, OptimizationBackupStore, SecureBackupDirectory,
│  │                            RestorePointModule, SettingsService, StartupModule, UpdateModule (winget)
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
   ├─ Theme.xaml                  Tokens de color y tipografía, plantillas de control
   ├─ MainWindow.xaml             Ventana con chrome propio
   ├─ MainViewModel.cs            Estado observable y orquestación de sesión
   ├─ Charts.cs / Converters.cs
   ├─ Dialog.xaml                 Diálogos propios (no MessageBox)
   ├─ CleanupWindow / OptimizeWindow / ProfilesWindow   acciones con confirmación
   └─ HistoryWindow / SettingsWindow / PingMonitorWindow   historial, ajustes y monitor de latencia
```

## Módulos

| Módulo | Qué mide | Admin |
|---|---|---|
| Diagnóstico completo | Todos los de abajo en una pasada | recomendado |
| Red y latencia | RTT, jitter y pérdida contra router, internet y chat regional de Riot (LAS/LAN); señal, banda y canal Wi-Fi; traceroute hacia el destino más relevante | no |
| Rendimiento | CPU por proceso con muestreo real, RAM, cola de disco | no |
| Térmicas y energía | Temperatura ACPI, frecuencia actual vs. nominal, throttling, plan de energía, desgaste de batería (capacidad de diseño vs. actual) | no |
| Estabilidad | Kernel-Power 41, BugCheck, WHEA (catálogo general + escaneo dedicado por proveedor), errores de disco, minidumps | recomendado |
| Limpieza | Calcula, confirma y borra temporales reportando lo liberado | parcial |
| Drivers | Inventario de drivers con antigüedad, foco en almacenamiento/chipset/red | no |
| Optimizar | DNS, reparación de WLAN, plan de energía, reinicio de pila TCP/IP | sí |
| Restaurar | Revierte los valores capturados antes del primer ajuste pendiente; no TCP/IP/IP fija/VPN | sí |
| Monitor de ping | Latencia en vivo hacia el router o internet: último valor, promedio, máximo y pérdida | no |
| Historial | Diagnósticos archivados con su puntaje y cobertura; la tendencia compara solo cobertura equivalente | no |
| Perfiles | Combinaciones de optimización (universidad, trabajo, juego) con respaldo previo | sí |
| Ajustes | Muestreo, ventanas de eventos, retención de historial y registros | no |

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

## CI, pruebas y distribución

- Windows compila la solución, ejecuta regresiones y conserva TRX. Se incluyen ramas
  `arena/**`; no se publica un release por trabajar en una rama.
- `Tools/validate_tests.ps1` exige ≥113 pruebas (el mínimo de la suite auditada), todas aprobadas y sin omisiones.
- Headless está en la solución y comparte `ScanService`; `--self-test` usa un doble
  sintético explícito, nunca sustituye mediciones de un diagnóstico real.
- `Tools/validate_release.ps1` arranca el **EXE publicado** con `--self-test` y comprueba
  recursos WPF, reglas y JSON, sin modificar hardware, red ni ajustes.
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

Consulta [la auditoría y sus tres listas de diez](docs/AUDITORIA.md) para los fallos,
correcciones, evidencia de CI y límites pendientes. Un CI anterior no certifica cambios
posteriores; el informe identifica expresamente el SHA validado.

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

- **Paleta azul-pizarra**, no negro puro, con acento índigo `#6C7BF7`. La profundidad
  viene de la elevación de superficie, no de bordes marcados.
- **Tres roles tipográficos**: Bahnschrift (un DIN, la letra del dibujo técnico) para
  cifras y rótulos, Segoe UI Variable para texto corrido, y Cascadia Mono con cifras
  tabulares para todo dato numérico, así las columnas no bailan.
- **La regla de escala**: cada métrica del Resumen lleva debajo una serie de marcas que
  se llenan según su magnitud, como la escala de un instrumento. Es el elemento que da
  identidad a la interfaz y codifica lo que la aplicación hace: medir.
- **Barra de título propia**: la del sistema no se puede tematizar.
- **Diálogos propios**: `MessageBox` se dibuja en claro y rompe el conjunto.
- **La navegación es una lista con selección**, no botones sueltos: el módulo activo
  queda marcado sin estado que sincronizar a mano.

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

