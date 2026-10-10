# Auditoría de SysDiag y ciclo de corrección

**Fecha:** 2026-10-02 · **Base auditada:** `b1abac1` · **Rama de trabajo:** `arena/01a0fb3f-sysdiag-app`.

El proyecto es una aplicación WPF/.NET 8 para Windows x64. Se revisaron motor, modelo, servicios, interfaz, limpieza, optimización/restauración, instalación de drivers, procesos, persistencia, pruebas y distribución. Las listas de hallazgos describen la base auditada; la columna de tratamiento describe los cambios de esta rama, no problemas que necesariamente siguen presentes.

## Estado de verificación

- El ciclo de base quedó verde en Windows: [Compilar y probar, run 36974887168](https://github.com/KryoDevs/SysDiag-App/actions/runs/36974887168) y [Validate fixture, run 36974887152](https://github.com/KryoDevs/SysDiag-App/actions/runs/36974887152), commit `291922d`. Compilación, suite y publicación/validación anterior finalizaron correctamente.
- **Tanda final de código validada:** commit `8c049c6`, [Compilar y probar, run 36983543742](https://github.com/KryoDevs/SysDiag-App/actions/runs/36983543742) y [Validate fixture/Debug, run 36983543678](https://github.com/KryoDevs/SysDiag-App/actions/runs/36983543678), ambos exitosos.
- **101/101 pruebas aprobadas, sin omisiones ni fallos** (annotation del job `110763312092`). Pasaron ACL administrativa de carpeta/archivo temporal, lectura nativa de energía, firma válida/PE alterado, rollback tipado, procesos, JSON/culturas, clasificación de eventos, CSV/historial y cancelación. No se instalaron drivers ni se aplicaron cambios de red/registro/energía.
- Pasaron además el autotest del **runner compartido**, la publicación autocontenida, el **arranque real del EXE publicado** con recursos WPF/reglas/JSON y la comprobación de versión 5.7.1. ZIP y EXE quedaron como artefactos del run, no como un release público.
- Comprobaciones locales efectuadas: `git diff --check`, XML/XAML bien formado y revisión de llamadas/contratos. No sustituyen un compilador ni pruebas Windows.
- Este sandbox es Linux sin SDK .NET disponible. No se afirma que WPF se haya ejecutado aquí.
- No se instalaron drivers ni se modificaron DNS, registro, servicios o planes de energía de una máquina de usuario. Ningún tag, release o cambio en `main` fue publicado.

## 10 cosas que están bien

| # | Fortaleza observada | Evidencia y qué conservar |
|---|---|---|
| B01 | Separación entre modelo, Core, servicios y WPF | `Models/`, `Core/`, `Services/`, `Ui/`. Permite corregir lógica sin rehacer toda la interfaz; no equivale todavía a desacoplamiento completo. |
| B02 | Contratos de servicios y reglas pequeñas | `Services/Interfaces.cs`, `Diagnostics/DiagnosticRule.cs`. Facilitan dobles de prueba y evaluación de reglas sin consultar hardware. |
| B03 | Medición de CPU por diferencia temporal | `Core/Performance/PerformanceModule.cs`. Usa tiempo consumido entre dos muestras y normaliza por núcleos; no confunde procesos antiguos con procesos activos. |
| B04 | Red con varias medidas, jitter y pérdida | `Core/Network/NetworkModule.cs`. Comparar router y destinos externos aporta más información que un único ping. |
| B05 | Inventario con caché acotada | `Core/Hardware/SystemModule.cs`. Evita repetir consultas costosas de inventario al ejecutar módulos consecutivos. |
| B06 | Inventario de software por registro | `Core/Windows/StartupModule.cs`. No usa `Win32_Product`, evitando su comportamiento de comprobación/reparación de instaladores MSI. |
| B07 | Reporte HTML escapado y autocontenido | `Core/Diagnostics/ReportBuilder.cs`. Escapa texto de equipo y hallazgos y no necesita un servidor ni recursos remotos para visualizarse. |
| B08 | Apartamento COM dedicado para Windows Update | `Core/ComWorker.cs`, `Core/Drivers/DriverUpdateModule.cs`. La propiedad de los objetos COM se mantiene en un STA, en vez de pasarlos libremente entre hilos. |
| B09 | Manifest `asInvoker` y confirmaciones para cambios | `app.manifest`, ventanas de optimización/limpieza. Diagnosticar no exige iniciar siempre con privilegios máximos y las acciones peligrosas tienen confirmación. |
| B10 | Fixture, pruebas y CI de Windows existentes | `Tools/IntegrationTests/FixtureTests.cs`, `Tools/fixtures/`, `.github/workflows/`. Había una base reproducible sobre la que añadir regresiones, no solo comprobación manual. |

## 10 cosas que estaban mal

**Severidad:** crítica = posible pérdida de datos/ejecución privilegiada o restauración insegura; alta = bloqueo importante, datos engañosos o entrega defectuosa. “Implementado” no significa certificado en todo hardware: consultar el estado de verificación y los límites finales.

| # | Severidad | Falla y consecuencia | Tratamiento en la rama |
|---|---|---|---|
| F01 | Crítica | La categoría de miniaturas apuntaba al directorio Explorer entero. Recursión, enlaces y normalización de atributos podían borrar archivos ajenos a la caché, seguir junctions o quitar protección de solo lectura. | Targets internos, solo `thumbcache*.db` sin recursión para Explorer, antigüedad mínima de 24 h, contención y rechazo de reparse points. Borrado por handle verificado; conserva archivos protegidos/en uso. Regresiones temporales/junctions verdes en el ciclo de base. |
| F02 | Crítica | Extraer un certificado y validar su cadena no prueba que los bytes del ejecutable sigan firmados. Ausencia/fallo del antivirus podía interpretarse como aprobación y se permitían instaladores locales sin demostrar compatibilidad. | `WinVerifyTrust`, archivo bloqueado para lectura durante verificación, resultados desconocidos no aprobatorios. Instalación local de EXE/MSI/INF/CAB/ZIP bloqueada. Firma/AV se muestran solo como información. Regresión de PE firmado alterado y host genuino firmados aprobada en Windows. |
| F03 | Crítica | Cada optimización podía sobrescribir la base de restauración. DNS perdía origen DHCP, valores ausentes del registro se recreaban y los índices de energía podían restaurarse sobre otro plan. Además el JSON editable en Documentos guiaba cambios elevados. | Esquema 2 tipado/validado, equipo/SID, primer valor preservado, DNS IPv4 por GUID/origen, existencia de DWORD, índices por plan y activación al final. Nuevo respaldo en ProgramData con propietario Administradores/SYSTEM y ACL protegida, aplicado al temporal antes del reemplazo. No importación automática de respaldos antiguos. |
| F04 | Alta | `ReadToEnd` antes de aplicar timeout y stderr sin drenar podían bloquear indefinidamente. Algunos comandos se anunciaban exitosos sin revisar salida/código. | `ProcessRunner`: stdout/stderr concurrentes, límites de salida, timeout/cancelación y terminación del árbol. Resultados tipados y `RunRequired` para cambios; rutas absolutas para utilidades Windows. Regresiones de procesos verdes en el ciclo de base. |
| F05 | Alta | JSON perdía hallazgos de una colección get-only. La fusión ignoraba listas vacías, dejando resultados viejos, y recomendaciones podían quedarse obsoletas. | Populate/callback de deserialización, normalización de nulos, reemplazo por módulo incluso vacío y recálculo de recomendaciones. Regresiones de round-trip/segunda pasada verdes en el ciclo de base; entradas malformadas adicionales aprobadas. |
| F06 | Alta | Propiedades WMI ausentes se convertían en cero/false. Discos desconocidos podían aparecer sanos y una advertencia de desgaste/temperatura degradaba un estado ya crítico. Un firewall parcialmente leído se daba por activo en tres perfiles. | Lectura `TryNum/TryBool`, salud desconocida explícita y máximo de severidad. Firewall exige tres perfiles. Defender contrasta con Centro de seguridad antes de inferir desprotección; consultas incompletas producen advertencias, no un certificado de salud. |
| F07 | Alta | WPF y runner no compartían la misma evaluación. Había resultados/reglas distintos, datos viejos tras cancelación y operaciones canceladas podían anunciarse o archivarse como completadas. | `ScanService` común, scratch que se publica solo al terminar un paso, metadata de cobertura/estado/fechas, runner con `ProjectReference` e incluido en solución. UI no archiva cancelaciones ni acciones como diagnósticos; conserva estado cancelado/fallido. |
| F08 | Alta | Los EventID no son globalmente únicos: clasificar por número sin proveedor confundía eventos ajenos con BugCheck/disco/WHEA. Fallar al leer el registro podía terminar en “sin eventos críticos”. | Catálogo `(proveedor, ID)`, XML estructurado con Select separados, grouping por proveedor, ventana temporal y máximo de 50.000 eventos. Acceso fallido/truncado se informa como incompleto, nunca como ausencia demostrada. |
| F09 | Alta | Reparaciones e instalación de drivers podían devolver mensajes de error que la UI trataba como éxito. Se elegían drivers por índice sin verificar identidad; se continuaba sin punto de restauración y Windows Update no preservaba estados originales de servicios. | Errores propagados, comprobación de bool/status/secuencia del punto, identidad GUID/índice del candidato y resultado por actualización. No instalación desde la app si falla el punto. Reparación de caché con respaldo y `finally` que reinicia solo servicios originalmente Running. UI serializa acciones y no ofrece cancelación ficticia de COM. |
| F10 | Alta | Dos workflows publicaban releases con reglas incompatibles. Nombre/tag manual y ZIP podían no coincidir; versión/URLs del instalador estaban desfasadas. Validar tamaño del EXE no demostraba que arrancara. | Un solo workflow, etiqueta existente/versión comprobadas, pruebas antes de publicación, borrador y SHA256SUMS. Acciones fijadas a commits; bootstrap de SDK sin ejecutar scripts remotos en un temporal predecible. Versión 5.7.1 desde proyecto para assembly/instalador. `--self-test` real de ejecutable publicado; tanda nueva validada; Inno Setup y publicación de una etiqueta requieren prueba/revisión manual. |

## 10 cosas que se podían mejorar

| # | Mejora | Implementación y estado |
|---|---|---|
| M01 | Un único pipeline de diagnóstico para GUI y CLI | `ScanService` y servicios comunes; headless sin HintPath a un DLL Debug local. Autotest sintético de runner incluido y aprobado en CI. Implementado y validado. |
| M02 | Reglas independientes del separador decimal | `NumericText` y reglas RAM/CPU comunes. Valores `86.5 %` y `86,5 %` funcionan sin reinterpretar miles como decimales. Regresiones multicultura verdes en el ciclo de base. |
| M03 | Contrato de restauración limitado y verificable | Snapshots tipados, validación antes de escribir/cambiar, conservación de primera captura y archivo consumido solo al restaurar con éxito. TCP/IP, cachés e IP/rutas/VPN no se prometen reversibles. Implementado; escritura nativa real requiere matriz manual. |
| M04 | Interfaz responsiva y acciones coherentes | Inventario/netsh fuera del dispatcher, perfiles/restore/puntos en background, selección de navegación reiniciada, bindings de export/cancel, botón de reparación conectado, suscripción/timer liberados al cerrar. Implementado; falta prueba interactiva Windows. |
| M05 | Cancelación y vida de recursos reales | Cancelación de espera de ping/DNS/muestreo; límites de procesos/WMI; generación del monitor evita muestras viejas/solapadas; COM reentrante/disposing sin deadlock. COM síncrono no se anuncia abortable. Implementado parcialmente: WUA/RPC aún requieren validación de fallos/hangs reales. |
| M06 | Exportación e historial confiables | Escrituras atómicas/nombres únicos, CSV neutraliza fórmulas y CR/LF/comillas, JSON acotado, cronología por fecha de medición y comparación solo con cobertura equivalente. Archivo/lectura de historial fuera del dispatcher de la vista principal. Implementado, regresiones nuevas aprobadas en Windows. |
| M07 | Reportes que expliquen alcance y tiempo | HTML recalcula score/recomendaciones, usa Fin y no hora de exportación, muestra cancelación/estado/cobertura, no inventa duración de archivos antiguos. Inventario de discos no cuenta como SMART completado. Implementado. |
| M08 | Regresiones enfocadas en las fallas, no solo un fixture | Pruebas nuevas de borrado real temporal/junctions, salida grande, timeout, firma, ACL, backups, JSON, culturas, cobertura, eventos, CSV, historial, selección de drivers y cancelación. Gate de CI exige ≥80 pruebas, todas aprobadas y sin omisiones. 101/101 pruebas aprobadas en Windows. |
| M09 | Distribución reproducible y seguridad de dependencias de CI | SDK fijado con roll-forward, acciones por SHA, TRX preservado, versionado único, gate de arranque y borrador/checksums. Se evita buscar utilidades/DLL nativas propias en el cwd. Implementado; firma de distribución e instalador todavía no probados. |
| M10 | Matriz real de hardware, permisos y fallos | Plan manual al final para Windows 10/11, UAC, VPN/IP fija, Modern Standby, WUA y recuperación parcial. **Pendiente:** no sería seguro simular que CI prueba instalación/restore en el hardware de los usuarios. |

## Loop realizado

1. **Inspeccionar:** revisar la base, agrupar riesgos por impacto y comprobar caminos reales de uso (incluida interfaz, no solo helpers).
2. **Corregir primero límites de seguridad:** limpieza, ejecutables/AV, procesos, configuración y persistencia atómica. Commit `046b338`.
3. **Validar en Windows:** el primer build detectó imports `System.IO` ausentes en código nuevo; no se ejecutaron los tests en ese build. Se corrigieron imports y una colisión de nombres local del helper de limpieza. No se contabilizó como prueba aprobada.
4. **Corregir integridad del diagnóstico y recuperación:** reglas/pipeline común, snapshots/backup, WMI y modelo/historial. Commits `55942b4` y `291922d`; compilación/suite/paquete anterior verdes.
5. **Volver a inspeccionar y endurecer:** evidencia de seguridad/discos/eventos, handlers/bindings de UI, ACL/procedencia del respaldo, WUA, headless, CSV/reportes y release. Commit enviado `7b5b1e1`, documentación/instalador y correcciones posteriores.
6. **Repetir validación:** la conexión GitHub caducó y se reconectó sin solicitar credenciales en chat. Se preservaron los archivos y se integró el historial existente de la misma rama, sin force-push. CI detectó dos problemas de compilación del código/pruebas nuevos (alcance `pct` y nombre de API de ping); se corrigieron en `4e80bca`/`4fa8164`.
7. **Diagnosticar regresión y volver a ejecutar:** el sandbox no podía descargar el TRX. `3a3c8d9` publica fallos/conteos como annotations aunque la suite falle. La única falla restante era una aserción que buscaba acentos sin decodificar entidades HTML, no una falta de escape del reporte; `8c049c6` la corrigió. Los dos workflows quedaron verdes, incluyendo 101 pruebas y gates del ejecutable. La documentación final puede tener un commit posterior, pero el código validado está identificado expresamente.

## Cambios de comportamiento deliberados

- **No ejecutar instaladores de drivers locales.** Firma y antivirus no prueban que un paquete corresponda al equipo ni que instalarlo sea inocuo. Se conserva verificación informativa y el flujo explícito de Windows Update.
- **Punto de restauración obligatorio** para instalar drivers desde SysDiag y reiniciar TCP/IP. Si no se crea, se aborta, no se “instala igual”. Se puede abrir la configuración de Windows y gestionar desde allí por decisión del usuario.
- **Respaldos administrativos nuevos:** `%ProgramData%\SysDiag-Backups\<SID>\estado-previo.json`. JSON de Documentos de versiones antiguas no se importa/ejecuta automáticamente. No se borra; revisar/restablecer sus valores manualmente con herramientas Windows si se necesita recuperar aquel estado.
- **La restauración no es una transacción de todo Windows.** Revierte solo los valores capturados; restaurar índices se hace en su plan y el plan activo se restaura al final. Si un paso falla, se conserva el respaldo y se informa error; no se pretende que todos los cambios anteriores al fallo se hayan deshecho automáticamente.
- **Limpieza conservadora:** omite temporales recientes/protegidos/en uso y enlaces. Vaciar papelera es otra operación irreversible, seleccionada y confirmada; nunca se inicia desde diagnóstico completo/headless.
- **El score es heurístico y de evidencia disponible.** Cobertura completa significa que se intentaron los módulos, no que todos los sensores estén disponibles. ICMP filtrado, WMI denegado o Security Center desconocido no son demostración de avería ni de salud.

## Cómo reproducir en Windows

```powershell
dotnet restore SysDiag.sln
dotnet build SysDiag.sln -c Release --no-restore
dotnet test SysDiag.sln -c Release --no-build --logger "trx;LogFileName=tests.trx" --results-directory TestResults
.\Tools\validate_tests.ps1
.\Tools\HeadlessRunner\bin\Release\net8.0-windows\win-x64\HeadlessRunner.exe --self-test
dotnet publish SysDiag.csproj -c Release -o publish
.\Tools\validate_release.ps1 -Root .\publish
.\Tools\validate_version.ps1 -Root .\publish
```

Si PowerShell bloquea los `.ps1` (`UnauthorizedAccess` / «la ejecución de scripts está deshabilitada»), es su política de ejecución o la marca de web del ZIP descargado; usa `powershell -NoProfile -ExecutionPolicy Bypass -File ...` o `Set-ExecutionPolicy -Scope Process Bypass`. `build.bat` ya lo hace así, sin cambiar la política del sistema.

La suite de ACL crea/elimina **solo su carpeta temporal protegida**, y la de energía únicamente lee valores; no altera un plan. La de Authenticode copia/modifica un PE en un temporal **sin ejecutarlo**. Para generar instalador, instalar Inno Setup 6 y usar `Tools/build_installer.ps1`; no se ha compilado en este sandbox. Para diagnóstico CLI real (solo lectura): `HeadlessRunner.exe --output <carpeta>`.

## Pendientes y límites honestos

- Probar GUI, cierre/UAC/mutex y cancelación en Windows 10/11 con usuario estándar/administrador, también elevación con una cuenta distinta.
- Probar backup → varios perfiles → restore en VM descartable: dos planes, CPU/Wi-Fi, DHCP y DNS estático con varios servidores, DWORD ausente/presente. Corromper/alterar el respaldo y negar permisos debe abortar **antes** de cambios.
- Probar manualmente Windows Update/COM: offline, WSUS, fallos de licencia/descarga, selección obsoleta, instalación parcial, reboot y restauración de servicios. Las llamadas COM síncronas todavía pueden tardar por causas externas; no se matan a ciegas ni se promete cancelación instantánea.
- Obtener sensores físicos para SMART, térmicas, GPU y Wi-Fi 6 GHz. El parseo de winget/netsh en idiomas no cubiertos sigue siendo una limitación; las salidas no interpretables deben advertirse, no convertirse en “al día”.
- Puntaje/umbrales no son un diagnóstico causal certificado. Evaluar sesgo y doble penalización entre categorías con más fixtures y mediciones reales.
- Los reportes/logs incluyen equipo, procesos y datos de red: revisarlos/redactarlos antes de compartirlos. No se añade telemetría ni subida automática.
- La distribución no tiene firma propia validada. Checksums y CI no sustituyen firma de código ni revisión del usuario.
- El ZIP histórico ya versionado continúa en el repositorio. Sacarlo del historial/reubicar releases requiere una decisión de mantenimiento; no se reescribió Git ni se alteraron releases antiguos.

**Conclusión:** se priorizó evitar daño, falsos éxitos y falsa tranquilidad antes que automatizar más cambios del sistema. Las correcciones tienen compilación y 101 pruebas verdes, runner y ejecutable publicado validados. La GUI interactiva, el instalador y la recuperación/instalación reales sobre hardware siguen pendientes de la matriz manual; no se presenta CI como una certificación universal ni como ausencia de vulnerabilidades.
