# Auditoría 2026-10-10: por qué sobrevivían los pendientes

Esta auditoría responde a una pregunta: por qué los textos de conteo se desincronizaban, por qué
sobrevivió código muerto y por qué había ramas de licencia que nunca se ejecutaban. Lo que sigue
abierto está en `docs/MEJORAS.md §9.2`; aquí solo se registra lo de esta tanda.

**Estado de verificación.** El sandbox no tiene SDK de .NET ni PowerShell. Nada de esta tanda se
ha compilado ni ejecutado. Las pruebas, el gate de 314 y los scripts nuevos quedan pendientes del
CI de Windows. Ver «Lo que no se pudo verificar».

## Causas

### 1. Las cifras se escribían a mano en varios sitios

El umbral de pruebas vive en `Tools/validate_tests.ps1`, en el README, en el CHANGELOG y en
MEJORAS. Cada tanda actualizaba uno o dos y dejaba los demás. El CHANGELOG lo admitió: «los textos
seguían diciendo 114, 148 y 152». La versión sufría lo mismo.

**Corrección:** `Tools/validate_docs.ps1` compara en cada corrida del CI la versión del README con
`SysDiag.csproj`, el umbral del gate con el del README y con la última mención del CHANGELOG, las
referencias `§N` / `§N.M` con los encabezados reales de `docs/`, y los nombres de servicios que el
README menciona con el código. La primera versión del script (prototipo en Python) encontró tres
servicios que el README seguía nombrando aunque ya no existían, y cuatro referencias `§` rotas.
Todo se corrigió.

### 2. El código muerto sobrevivió porque las pruebas lo usaban

`DiagnosticReport.MergeFrom` solo lo ejercitaban pruebas. La ruta real del escaneo es
`ReplaceModuleFrom`. Las pruebas de `MergeFrom` daban verde y nadie veía que el código no se usaba.

Lo mismo con `HistoryService`, `RepairService` y `ReportService`: se construyeron como servicios
paralelos a `ScanService` pero nunca se cablearon, y ninguna prueba exigía que se usaran.

**Corrección:** se borró el código muerto y las pruebas de `MergeFrom` pasaron a `ReplaceModuleFrom`.
Ver la lista completa en el CHANGELOG (sección 2026-10-10).

**Regla para el futuro:** una prueba que solo ejercita una API no demuestra que la API se use.
Antes de dar por bueno un método, buscar sus llamadores fuera de las pruebas.

### 3. La licencia tenía ramas inalcanzables

Dos problemas del mismo origen: `Verificar` devolvía solo texto, y el llamador no podía saber por
qué fallaba.

- «Este código fue emitido para otro equipo» (en `Activar`) nunca se mostraba. `Verificar` ya
  rechazaba un código vinculado a otra máquina con el mensaje genérico, así que la comprobación
  posterior, que dependía de que `Verificar` hubiera tenido éxito, no podía ser verdadera.
  Ahora `Verificar` lo dice (`MotivoRechazo.NoCoincide`, y el texto menciona el equipo). La frase
  no puede ser más precisa: sin la clave del equipo no se distingue «otro equipo» de «mal escrito».
- `Inicializar` no distinguía un código guardado vencido de uno que no verifica. Ahora usa
  `MotivoRechazo.Vencido`.
- `Activar` guardaba el texto tecleado (con espacios o minúsculas). Ahora guarda `CodigoInfo.Canonico`.

**Corrección:** `Verificar` devuelve `MotivoRechazo`. Dos pruebas nuevas cubren vencimiento y firma
alterada en un código vinculado.

### 4. Los `.ps1` con texto no ASCII sin BOM

`Tools/*.ps1` con caracteres no ASCII y sin BOM se leen como ANSI en Windows PowerShell 5.1, que es
el que usa `build.bat`. En `validate_tests.ps1`, el guion largo (`E2 80 94`) cae en `0x94`, que en
cp1252 es una comilla tipográfica, dentro de una cadena entre comillas. Eso puede romper el análisis.
Se añadió BOM a todos los `.ps1` con texto no ASCII. `validate_docs.ps1` se escribió solo en ASCII.

## Qué se cambió

| Área | Cambio |
|---|---|
| Licencia | `MotivoRechazo`, mensaje alcanzable, forma canónica al activar, dos pruebas nuevas |
| Código muerto | `HistoryService`, `RepairService`, `ReportService` y sus interfaces; `MergeFrom`, `LiveSeries`, `LineChart`, `ConfigurarCache`, `RegistrarRegla`, `EsAdmin`, `EsNuevo`, `Vigente`, `AccionTexto`, `VerificarParaEsteEquipo`, extern `GetExtendedUdpTable` |
| Pruebas | `MergeFrom` → `ReplaceModuleFrom`; gate sube de 282 a 284 (+2 neto) y, en la segunda tanda, a 314 (+30) |
| Documentación | Referencias `§` corregidas; README (servicios, licencia, `build.bat test`); cifras de MEJORAS y de la auditoría 1 actualizadas, con los registros históricos conservados |
| CI | `validate_docs.ps1` como paso de `build.yml`. `build.bat test` no lo ejecuta todavía |
| Release | `release.yml`: notas desde la sección `## [X.Y.Z]` del CHANGELOG, con fallback a las generadas por GitHub |
| Configuración | `Nullable` por anotaciones, analizadores en `latest-recommended`, `SupportedOSPlatform("windows")` en lugar de silenciar CA1416, `.editorconfig` nuevo |

## Verificación realizada

- `validate_docs` en Python sobre el repo: OK, versión 5.14.0, umbral 314 (el de la primera tanda era 284).
- Sintaxis C# con tree-sitter sobre 117 archivos: un aviso en `Core/WmiHelper.cs`, que aparece igual
  en `HEAD` y es una limitación de la gramática con `is X { ... }`. No es un error del código.
- Búsqueda de todos los símbolos borrados en código, XAML, csproj y CI: ninguna referencia viva.
  Solo quedan menciones en docs históricos y en el README (corregido).
- `release.yml` parsea con pyyaml. El script de PowerShell del paso de notas no se ha ejecutado.

## Lo que no se pudo verificar

- **Compilación y pruebas.** Depende del CI de Windows. Si el gate de 314 falla, el número es el
  que hay que revisar primero: se calculó como 282 + 2 y no se ha contado en ejecución.
- **`validate_docs.ps1` en PowerShell real.** Solo existe el prototipo en Python.
- **Paso de notas de `release.yml`.** Solo se revisó por lectura.
- **Logs de CI.** La rama no está publicada en origin, así que no hay runs que consultar.
- **Ingeniería de la segunda tanda, también sin verificar:** versión de `coverlet.collector` (6.0.2,
  no consultada en nuget.org), la lista de `Using Remove` para WPF, la instalación de Inno Setup con
  `choco`, y el instalador en el runner (ni siquiera se ha generado todavía con ISCC). Cualquiera de
  ellos puede romper el CI de forma visible; conviene revisar esos jobs primero.
- **Pruebas nuevas.** Son 30 resultados sumados a mano sobre el umbral anterior; el gate de 314 es
  esa suma, no un conteo real.

## Ingeniería pendiente: cerrada salvo tres puntos

Lo que había en la lista se revisó contra el código. Varias entradas estaban desactualizadas:
el timeout por módulo ya existía, y la falta era de pruebas.

| Pendiente | Estado |
|---|---|
| F10 estáticos mutables | **Aplicado**: `OpcionesDiagnostico` inmutable; `SettingsService.Vigentes` solo para textos. |
| Timeout real por módulo (§4.1, ítem 1) | **Probado**: límites inyectables y `ScanTimeoutTests`. |
| `SettingsService` sin ruta de prueba | **Aplicado**: `Cargar`/`Guardar` aceptan ruta; `OpcionesYAjustesTests`. |
| `Converters.cs` sin pruebas | **Aplicado**: `ConvertersTests` (20 casos); proyecto de pruebas con `UseWPF`. |
| Cobertura (§2, ítem 7) | **Aplicado** sin umbral: publicada como artefacto. |
| `validate-fixture` duplica pruebas (§2, ítem 8) | **Aplicado**: el job Debug solo compila. |
| Instalador sin prueba en CI (§2, ítem 11) | **Aplicado** (sin verificar en CI): job `instalador`. |

### Lo que sigue abierto, y por qué

- **F02, licencia en JSON plano.** La decisión que pedía la tabla («aceptar que es un umbral y decirlo»)
  ya está en el README. El endurecimiento (HKCU + copia en ProgramData) es opcional y cambia lo que la
  licencia promete: **necesita tu decisión**. No lo he hecho.
- **F06, accesibilidad, pasos 2 y 3.** Contraste alto y tema claro. Son cambios de color y de
  plantilla que solo se pueden juzgar mirando la interfaz en Windows. Hacerlos a ciegas arriesga
  una regresión visual que el CI no detecta.
- **F11, latido `Forever`.** El propio XAML advierte que el cambio puede romper el arranque y que
  solo se verifica ejecutando la interfaz. Sin esa ejecución, no lo cambio.
- **Fuera de la ingeniería:** rotación de la clave de firma (F01), certificado Authenticode (§2, ítem
  10), i18n (§8.2, ítem 13), winget/scoop (§2, ítem 12), y las funciones de producto de
  `docs/HERRAMIENTAS_NUEVAS.md`. Son decisiones o trabajo de producto, no pendientes técnicos.
- **Decisiones de producto ya documentadas** (no tocar sin el usuario): plan de energía no ligado al
  aviso térmico; restauración que falla por completo si falta un plan respaldado; licencias de drivers
  (A09) pendientes; caché de Windows Update renombrada en `C:\Windows` (A08) solo con mensaje.
