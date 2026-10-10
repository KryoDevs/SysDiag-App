# Análisis y plan de mejoras — SysDiag 5.11.0

Fecha: 2026-10-10 · Rama: `arena/e864cf80-sysdiag-app` · Base: `34a2aa1`
Continúa [AUDITORIA.md](AUDITORIA.md) y [AUDITORIA_2026-10-09.md](AUDITORIA_2026-10-09.md).

## 0. Base del análisis y sus límites

Se leyó el proyecto completo: los 93 archivos `.cs`, los doce XAML, el proyecto de
prueba (siete archivos en `Tools/IntegrationTests`) y el `HeadlessRunner`, los tres
workflows, los cinco `validate_*.ps1`, `build.bat`, el instalador y el manifiesto. Los
recuentos de esta sección se volvieron a tomar sobre el árbol de hoy: los de la primera
lectura ya estaban desfasados. Cada afirmación de abajo tiene su evidencia al lado.

El sandbox es Linux: aquí no se compila ni se ejecuta (WPF, WMI y `netsh` solo
existen en Windows, y no hay SDK de .NET instalado). La verificación real la hizo
**el CI de Windows de esta rama**, y hay que leerla como lo que es — la primera
pasada honesta del trabajo:

- `Compilar (Release)`: **en verde**, y `Validar el paquete publicado` (el
  `--self-test` del EXE publicado, que construye las diez ventanas) también. Es
  decir: el tema reescrito, los adjuntos `ui:Motion.*` en las diez ventanas y el
  recorte de tamaño por área de trabajo se cargan en WPF de verdad.
- Suite de regresión: **163/163** en el último empujón de la rama, tras la serie
  114 → 148 → 152 → 160 → 163. El umbral de `validate_tests.ps1` se subió en cada tanda
  para que siga apretando: que la cuenta sea exacta es lo que impide que se cuele una
  prueba eliminada sin reemplazo.
- `Tools/validate_xaml.ps1` pasó los doce XAML reales y **es puerta** en `build.yml` y
  desde hoy también en `release.yml`. Cinco comprobaciones, incluidas las dos que WPF
  traga en silencio: propiedad animada que el tipo del destino no tiene, y
  `RepeatBehavior="Forever"` de `Trigger.EnterActions` sin `StopStoryboard`.
- Nada de lo anterior se pudo ejecutar en el sandbox, que no tiene PowerShell ni .NET.
  Las reglas nuevas se calibraron contra un gemelo en Python (cero hallazgos sobre el
  repo, tres de tres sobre errores sembrados); la sintaxis del `.ps1` solo la certifica
  el CI. Es la misma limitación que el primer empujón ya puso en evidencia.
- El primer empujón **no compilaba**: `Motion.Transicion` pedía `Animatable` y
  recibía `FrameworkElement` en dos de sus siete llamadas (`CS1503`,
  `Ui/Motion.cs:110` y `:351`). Lo detectó el CI en 80 segundos. Ninguna de las
  verificaciones locales —parseo de XAML, árbol tree-sitter, cruce de adjuntos—
  podía verlo: es exactamente el argumento de §5 y de por qué el paso de CI de
  este repo no se puede sustituir por comprobaciones estáticas.

Lo que el CI **no** cubre: el aspecto y el tiempo de cada transición, que siguen
siendo a ojo (ver §7), y todo lo que exige un equipo con hardware o drivers de
terceros.

Las verificaciones mecánicas que sí corrieron en el sandbox:

- Sintaxis C# de los `.cs` (93 en el árbol de hoy) con un parser independiente
  (tree-sitter). Solo
  `Core/WmiHelper.cs` marca 5 nodos de error, que es la limitación ya conocida de
  la gramática con `row?[property]`: C# válido.
- Bien formado de los doce XAML, resolución de los 627 `{StaticResource}` y ámbito
  de cada `TargetName` dentro de su plantilla. Todo pasa. Esos tres chequeos
  quedaron como herramienta: `Tools/validate_xaml.ps1`.
- Contraste WCAG calculado sobre los tokens de la paleta (ver F08).

La compilación real, las pruebas y el autotest del EXE publicado tienen que correr
en el CI de Windows de esta rama antes de dar nada por cerrado.

---

## 1. Arreglar

Severidad: **alta** = afecta a un usuario en su equipo o a algo que el programa
presenta como cierto; **media** = falla en condiciones reales pero se ve;
**baja** = higiene y fricción.

### Ya corregido en esta rama

| ID | Sev. | Defecto | Tratamiento |
|---|---|---|---|
| R01 | Alta | `MainWindow` nace con `Height="860"` y `TweaksWindow` con `MaxHeight="820"`. Sobre un panel de 1366×768 —el equipo típico de una oficina o una universidad— el área de trabajo son ~728 px: el pie con «Generar informe» y «Exportar» queda fuera de la pantalla, y el borde para arrastrar también. Sin salida visible. | `Ui/Ventana.cs` recorta `Height`/`MaxHeight`/`Width`/`MaxWidth` al área de trabajo real respetando los mínimos, llamado desde las diez ventanas antes de cualquier otra cosa. La regla vive separada en `Ventana.Recortar(declarado, piso, disponible)` y tiene once pruebas; al separarla salió que su guarda solo miraba `NaN`, cuando el valor por defecto de `MaxHeight` es `+∞` —recortaba el máximo de las nueve ventanas que no declaran ninguno—. Corregido. |
| R02 | Media | `Ajustes` corregía en silencio. `AppSettings.LeerCampo` (`Models/AppSettings.cs:48`) no falla: devuelve el valor por defecto si el texto no es entero, y recorta al rango. Escribir «3,5» guardaba 5 y 9999 guardaba 90, y la ventana se cerraba con un «Ajustes guardados» que describía otra cosa. | Se vuelca en los campos lo que efectivamente quedó guardado y el aviso lista qué se ajustó y a cuánto. |
| R03 | Media | La barra indeterminada viajaba de −160 a 900 px, un número escrito a mano sobre un riel de ancho variable. En un pie de 1.300 px la barra nunca llegaba al borde y el ciclo reiniciaba con un salto visible; en una ventana angosta pasaba ~0,5 s fuera de escena. | Reescrita para crecer escalando (`ScaleX`) sobre el ancho real: el recorrido es exacto a cualquier ancho, DPI o escala. |
| R04 | Baja | `AccionTile` y `AccionTilePrimary` duplicaban la plantilla de `BtnBase` solo para cambiar el relleno. Consecuencia real: su hover seguía siendo instantáneo mientras el resto del tema respondía. | Se retira la duplicación; las variantes heredan la plantilla animada. |

### Corregido en la segunda tanda (mismo push, sin esperar decisión)

| ID | Sev. | Qué estaba mal | Qué se hizo |
|---|---|---|---|
| F03 | Alta | `LicenseService.Inicializar` hacía `_archivo.Codigo = null` cuando el código no verificaba: renombrar el PC, entrar con otra cuenta o un perfil roaming **borraba la prueba de que alguien compró** una licencia, y `CodigoActivo` (que la ventana de activación nunca leía) quedaba vacío. | El código se conserva siempre. El archivo de licencia guarda además el equipo donde se activó, así que la ventana puede decir algo cierto («se activó en `PC\ana`») en lugar de fingir que nunca hubo nada; y rellena el campo con el código guardado. Si el equipo vuelve a llamarse como antes, la licencia reaparece sola. |
| F04 | Alta | `DebugType=none` dejaba el registro de cualquier fallo sin números de línea: el log que se pide en un soporte terminaba en `<RunAsync>d__57.MoveNext()`. | `<DebugType>embedded</DebugType>`. El PDB viaja dentro del ensamblado: ningún archivo suelto que olvidarse de subir, `PublishSingleFile` sigue siendo un solo exe, y `validate_release.ps1` no tiene ninguna aserción que esto pueda romper (lo hace el CI). |
| F05 | Media | `CTextMuted` (#6E79A8) no cumplía AA sobre ninguno de los fondos donde se usa (4,47 / 4,61 / 4,08 / 3,65) y es el color de los rótulos de 9,5–10,5 px. | Subido a #808CC2 (5,81 / 5,99 / 5,30 / 4,74): mismo matiz frío, mismo peso, cumple en los cuatro fundos. Y ahora hay una prueba que lo mide — ver abajo. |
| F06.1 | Media | Cinco botones sin nombre accesible. | `AutomationProperties.Name` en los tres de la cabecera de `MainWindow` y en los dos de cierre propio (`HistoryWindow`, `PingMonitorWindow`). |
| F07 | Media | El manejador global de errores escribía un `Dialog.Error` idéntico para todo y seguía adelante: si la excepción caía a mitad de una operación que escribe, el usuario veía «El detalle quedó guardado en el registro» y los mismos botones. | El diálogo distingue si había una corrida en curso (`MainViewModel.Ocupado`), dice que la operación quedó a medias, muestra la ruta del registro y ofrece **Reiniciar SysDiag** con `AppEnv.Reiniciar()`, que reutiliza `--wait-for-parent` para no chocar contra el mutex de instancia única. Todo el bloque va en `try` con `MessageBox` de último recurso: si lo que rompió fue un recurso del tema, construir el diálogo también puede fallar. |
| F09 | Baja | `int.Parse` sin cultura sobre el substring de la fecha WMI en `DriverModule`. Hoy está cubierto porque el arranque fuerza `es-CL`, y un fallo se tragaba en el `catch` como «fecha ilegible». | `CultureInfo.InvariantCulture` en los tres parses, con la razón escrita al lado: son dígitos ASCII de un protocolo, no texto localizable. |
| F13 | Baja | Nada fijaba las reglas de contraste del tema: el defecto de F05 pudo entrar porque ninguna prueba lo miraba. | `Tools/IntegrationTests/ThemeRegressionTests.cs` mide los ocho tintes de texto contra los cuatro fundos del tema sobre el propio `Theme.xaml` (32 combinaciones) y deja constancia de la restricción real: sobre la superficie elevada solo puede ir `CText`, que es lo que hace la plantilla del ToolTip. Y fija el embudo de procesos con `Core_OnlyProcessRunnerCreatesAProcess`. |

### Cerrado en la tanda de 2026-10-10 (CI verde: 163/163)

| # | Qué | Evidencia |
|---|---|---|
| R01+ | La regla de recorte es una función y está probada: `Ventana.Recortar(declarado, piso, disponible)`, once casos que fijan la frontera del contrato —un recorte **solo achica**: ni un mínimo de 900 empuja hacia arriba un alto de 860, ni un `MaxHeight` de 660 se estira hasta los 704 disponibles—. | `Tools/IntegrationTests/WindowSizingTests.cs`. El CI rechazó dos expectativas que había escrito mal quien hizo el arreglo, y quedó dicho dentro del propio archivo. |
| §4.1.1 | El pie dice **cuánto lleva** el módulo: `3 de 5 · Red y latencia · 14 s`, y a los 90 s deja una línea `WARN` única en el registro. Con WMI colgado, «midiendo» y «no va a terminar» eran el mismo fotograma. | `Ui/MainViewModel.cs`; el reloj es un `DispatcherTimer` que solo vive durante la corrida. El `Timeout` que cancele un módulo sigue sin existir: esto nombra el problema, no lo corta. |
| — | `validate_xaml.ps1` gana dos reglas y deja de ser advertencia: es puerta en `build.yml` y ahora también en `release.yml`, antes de que un tag pueda publicarse. | Calibrado contra un gemelo en Python: cero hallazgos en el XAML real, tres de tres en errores sembrados. |
| F12 | `sysdiag-1.0.0.sha256` fuera del repositorio, con sus dos sumas trasladadas a la entrada `[1.0.0]` del CHANGELOG. | `git rm` + bloque «Sumas de control del paquete». |
| — | La maqueta `docs/preview/index.html` vuelve a ser espejo del tema. | Tiempos y curvas como variables, hover con transición, elevación, latido con curva, barra del pie escalando y rótulo de avance. |

### Pendientes

| ID | Sev. | Defecto | Evidencia y tratamiento propuesto |
|---|---|---|---|
| F01 | **Alta** | La clave con la que se firman los códigos de activación está en el repositorio público, en los dos lados. `LicenseCrypto.Clave()` es `SHA256("SysDiag/2026/clave-privada-v1")` y `Tools/New-ActivationCode.ps1:53` contiene literal idéntico. No es «una copia del secreto en el binario» (eso es inherente a la verificación offline): es el **emisor completo** publicado. Con esos 40 caracteres cualquiera genera licencias Pro válidas y ilimitadas, y puede redistribuir el script. | El comentario del archivo lo presenta como disuasión, y lo sería si el emisor no estuviera a la vista. Tratar: mover `New-ActivationCode.ps1` a un repositorio privado del emisor (o a un secreto de máquina), dejar en el repo público solo `-Verificar`, y **rotar la clave** — la actual ya es pública y está en el historial de Git, así que rota sin reescribir historia. El formato admite la rotación: el byte de versión del código permite aceptar v1 y v2 en paralelo durante una transición. |
| F02 | Alta | La prueba de 14 días se autogobierna con un JSON plano: `%LocalAppData%\SysDiag\licencia.json` con `PrimerInicio` en texto plano (`LicenseService.cs:46,98`). Borrar el archivo reinicia la prueba indefinidamente; copiarlo a otro equipo la traslada. | Aceptar explícitamente que es un umbral, no una barrera, y decirlo en el README (hoy se lee como un control real). Si se quiere endurecer: marcar el primer arranque en un lugar que no se borra con la app (HKCU con un valor opaco + una segunda copia en ProgramData con ACL propia como ya hace `SecureBackupDirectory`), y comparar contra la fecha de creación del propio binario para detectar relojes atrasados. No prometer anti-tampering: prometer que no se resetea con un `del`. |
| F06 | Media | Accesibilidad: cero `AutomationProperties` en el repo, y cinco botones son un glifo de *Segoe Fluent Icons* sin texto —el lector de pantalla anuncia «Button» y nada—; no hay contraste alto ni tema claro. | **Paso 1 aplicado**: `AutomationProperties.Name` en los cinco botones de solo glifo (minimizar, maximizar, cerrar ×3). Pasos siguientes: honrar `SystemParameters.HighContrast` con un `Style` que suba los bordes a 2 px y lleve el texto a blanco puro; y un tema claro, que con la paleta ya tokenizada son 17 `Color` más, no una reescritura. |
| F08 | — | **Corregido: esto no era un defecto.** Aquí afirmé que había 15 sitios creando procesos fuera de `ProcessRunner`. Al contarlo bien, los 15 golpes del grep eran `new ProcessStartInfo` para abrir una URL, un archivo o una página de `ms-settings` —el API correcto para eso, sin nada que esperar ni drenar—. `new Process` aparece **una sola vez en todo el repo**, en `Core/ProcessRunner.cs:58`. | Queda un detalle de forma: `AbrirDeviceManager_Click` (`Ui/MainWindow.xaml.cs:415`) arma el `StartInfo` con el helper del runner y luego llama a `Process.Start` a mano, que es lo correcto escrito dos veces. En vez de migrarlo, se fijó la regla con una prueba: `Core_OnlyProcessRunnerCreatesAProcess` falla si algún día aparece un `new Process` fuera del embudo. |
| F10 | Baja | `AppEnv.LogsMaximo`, `SettingsService.Aplicar` y `AppSettings` se comunican por **estáticos mutables** leídos desde los recolectores de `Core/`. Funciona, pero ninguna prueba puede variar un umbral sin pisar al resto de la suite (el orden de ejecución importa), y un módulo puede leer un valor que otra ventana cambió a media corrida. | Pasar un `OpcionesDiagnostico` inmutable como argumento a los módulos que lo necesitan (son cuatro: muestreo, pings, ventana de días, retención). Deja de ser necesario `Aplicar` antes de usar, y las pruebas de umbrales se escriben solas. |
| F11 | Baja | El latido del punto de la barra superior es un reloj `Forever` atado a `Loaded`: sigue corriendo en reposo bajo una pastilla `Collapsed`. Es un coste menor (una animación de opacidad sobre un elemento no visible), y **no lo cambié a propósito**: gobernarlo por estado exige `StopStoryboard` sobre un `BeginStoryboard` con nombre dentro de un `Style`, que vive en otro ámbito de nombres que dentro de una plantilla y no se puede verificar aquí compilando. Queda documentado en el propio estilo. | Mover el punto a un control con plantilla (`ControlTrafico` con su `ControlTemplate`) y atar el latido al estado. Ahí el `StopStoryboard` es seguro y el reloj se apaga de verdad. |

---

## 2. Mejorar (calidad, pruebas, CI, distribución)

**Compilación y reglas del código**

1. `<Nullable>disable</Nullable>` en un proyecto que toca disco, registro y COM. Es
   el tipo de bug que la auditoría 1 y la 2 persiguen a mano. Habilitarlo de golpe
   sobre los 93 archivos de hoy es un lote entero; el camino corto es `<Nullable>annotations</Nullable>`
   primero (anotaciones sin avisos), y luego activar por carpeta: `Models/` y
   `Diagnostics/` son candidatos directos, `Core/` después.
2. Cero analizadores. `EnableNETAnalyzers` + `AnalysisLevel=latest-recommended` no
   cuestan nada y son exactamente los que cazan `CA1305` (cultura al formatear),
   `CA2000` (objetos descartables — F08), `CA1416` (plataforma, ya silenciado a
   mano con `NoWarn`) y `CA1848` (patrones de logging). Empezar con
   `TreatWarningsAsErrors=false` y el ratchet de `validate_tests.ps1` aplicado a
   los avisos: «no más avisos nuevos que los N de hoy».
3. Sin `.editorconfig` ni `dotnet format`. Hay estilo de casa muy marcado (camelCase
   en campos, `PascalCase` en miembros, `ref Set` para notificar) que solo vive en el
   ejemplo de los vecinos. Un `.editorconfig` con `dotnet format style --verify-no-changes`
   como paso de CI lo vuelve comprobable, y `dotnet format whitespace --verify-no-changes`
   quita de los diffs la sangría que no se ve.
4. `NoWarn=CA1416` global: mejor un `[SupportedOSPlatform("windows")]` en el
   ensamblado, que es la respuesta correcta para una app que solo corre en Windows y
   además le dice al analizador que no vuelva a preguntar.

**Pruebas**

0. ~~El gate de `validate_tests.ps1` sigue diciendo «mínimo 114»~~ — **subido y
   verificado**: 163 hoy (114 auditadas + 34 del sistema visual + 4 de licencia + 11 del
   recorte de ventanas). El número se sube en el mismo commit que añade pruebas, y el
   mensaje del script repite el desglose para que se sepa qué lo compone.

5. Las 114 pruebas originales (163 hoy) son de lógica pura (`Exporter`, parsers, umbrales, fusiones) y son
   buenas; ningún binding, conversor ni template está cubierto más allá de «la ventana
   se construye`. El hueco concreto: `Converters.cs` (seis convertidores con ramas de
   `switch` y un `Clamp`) se puede probar en 20 minutos sin WPF... pero `ScoreArc.Geometria`
   sí necesita STA; conviene marcar el test `[STATest]` como hacen los de `Ui`.
6. El `--self-test` del EXE publicado construye las diez ventanas (A25). Falta un paso
   que **dispare los triggers** de las plantillas: un `Trigger.IsMouseOver` roto no
   aparece al construirlas. Aprobar esto en un `ICommand` interno es un fin en sí mismo;
   lo simple es aplicar `Style`/`Template` con `FrameworkElement.ApplyTemplate()` y
   forzar `BeginAnimation` sobre cada nombre declarado en el XAML.
   `Tools/validate_xaml.ps1` cubre hoy la parte estática de eso, y desde esta tanda también
   dos cosas que el parseo no veía: que la propiedad animada exista en el tipo del destino
   y que ningún `Forever` de `Trigger` se quede sin `StopStoryboard`. Sigue sin haber
   disparo real de los triggers, que es lo que falta aquí.
7. Sin cobertura medida. `coverlet.collector` + `--collect:"XPlat Code Coverage"` en el
   `dotnet test` del CI y publicar el informe como artefacto; sin umbral al principio,
   con umbral de no-regresión cuando se sepa dónde está (los números de `Core/Storage`
   y `Core/Windows` son los que importan).
8. `validate-fixture.yml` corre la suite también en Debug. Dos ejecuciones de la misma
   suite para un fixture JSON: se puede reducir a compilar en Debug y no repetir las
   pruebas, o dejar un subconjunto (`--filter`).
9. ~~Prueba de contraste del tema~~ — **aplicada**: `ThemeRegressionTests` mide los
   ocho tintes de texto contra los cuatro fundos del tema leyendo `Ui/Theme.xaml`, y
   verifica además que el embudo de ejecución de procesos siga siendo uno solo. Cuesta
   40 líneas de aritmética y es lo que evita que el próximo color nuevo se cuele sin
   pasar (F05 es precisamente eso: se detectó midiendo, no con una prueba).
   ~~y esta brecha quedó cerrada en la tanda siguiente~~: `Inicializar` acepta una
   ruta, `LicenseRegressionTests` prueba el comportamiento corregido, y el umbral del
   gate subió a 152 (163 hoy). Queda la misma limitación para `SettingsService`, que escribe el
   registro al leer y sigue sin camino de prueba. Nano-deuda recién vista: `Activar`
   guarda el texto tal como se tecleó (con espacios en vez de guiones) en lugar de la
   forma canónica; la lectura lo tolera porque `Normalizar` los descarta, pero el
   archivo podría guardarse limpio.

   Y una brecha que esta tanda dejó a la vista, para que no se lea como
   «arreglado y ya»: el cambio de F03 (no borrarle al comprador la prueba de su
   licencia) entró **sin prueba unitaria**. `LicenseService` tiene la ruta del
   archivo fijada a `LocalAppData`, y apuntarlo a un directorio temporal sin tocar
   el equipo de quien prueba no se puede. Pasar una `RutaBase` opcional —con el
   valor actual por defecto— lo vuelve testeable en cinco líneas y abre el mismo
   camino para `SettingsService`, que escribe el registro al leer. Ese es F10, y
   esta es su consecuencia más incómoda: sin dependencias inyectables hay arreglos
   que solo se pueden verificar leyendo.

**Distribución y operación**

10. Firma de código y notoriedad: sigue pendiente desde la auditoría 1 y es lo único que
    quita el «Windows protegió su PC» del README. Alternativa sin certificado de pago:
    `Azure Trusted Signing` (cuesta centavos al mes) y deja de ser un problema.
11. El instalador no se prueba en CI (`build_installer.ps1` produce, nadie instala).
    Un job con `innosetup` en silencio + `--self-test` contra la ruta instalada cubre
    lo que hoy se comprueba a mano.
12. Reparto por `winget` (manifest en `winget-pkgs`) y `scoop`: actualiza solo, quita el
    SmartScreen por reputación de la fuente, y encaja con que la app ya use winget para
    otros paquetes.
13. `release.yml` crea un borrador, bien; falta que el cuerpo del release se genere del
    `CHANGELOG.md` (el bloque «Sin publicar» de la versión etiquetada), para que las
    notas no se escriban a mano y se olviden.
14. `build.bat` sigue siendo el camino de entrada del README, pero no corre `dotnet test`:
    compila y publica. Para alguien que reporta un fallo, `build.bat test` (un
    `dotnet test` con el validador al final) es más útil que un .exe.

---

## 3. Interfaz y movimiento

### Hecho en esta rama

Ver CHANGELOG y `docs/DISENO.md § 4`. Resumen: un sistema (tres tiempos, cuatro
curvas, adjuntos reutilizables), hover/pulsado/foco/selección interpolados en todas
las plantillas del tema, puntaje que se cuenta y arco que barre, cascadas en
listas, barras de desplazamiento que se atenúan, y el arreglo de la barra
indeterminada. También `BitmapCache` durante las transiciones (una vista entera con
tarjetas sombreadas recomponía y re-sombreada cada cuadro) y el respeto de la
preferencia «no animar controles».

### Lo que sigue, en orden de valor

1. **Números de las tarjetas de medición**: el `MetricCard.Valor` llega como texto ya
   formateado, así que no se puede interpolar sin partir la propiedad. Separar
   `Valor` en `Numero` + `Unidad` + `Formato` en `MetricCard.Create` permite que CPU,
   RAM, latencia y temperatura también suban y bajen, no solo el puntaje. Es el
   cambio que más «vida» añade por línea tocada, y el texto del informe no cambia.
2. **Línea de historial**: el `Polyline` de `GraficoHistorial` aparece dibujado.
   `StrokeDashArray` + animación del `DashOffset` recorre la línea de izquierda a
   derecha (es el mismo truco que la palomita) y explica que el eje es tiempo.
3. **Los cuatro contadores del monitor de ping** (último, promedio, máximo, pérdida)
   pueden usar `ui:Motion.Number` tal cual está: ya son `TextBlock` con `FMono`.
4. **Banda de sección**: cuando cambia el módulo, el icono puede entrar con un
   `ScaleTransform` de 0,9 a 1 mientras el color del tile se cruza. Marca que
   «esto es otra cosa» sin mover el resto.
5. **Reemplazar el `Setter` instantáneo de `Foreground` del hover** en `BtnQuiet` y en
   `MetricCard` por la misma técnica de dos capas cruzadas que ya usa `Segment`. Se
   puede, pero hay que duplicar el presentador: hacerlo en un solo sitio primero y
   medir si molesta.
6. **Transición de las ventanas secundarias al cerrar**: solo tienen entrada. Cerrar un
   diálogo a seco es correcto (confirma la acción); cerrar una ventana de resultados
   sin salida es lo que hace dudar de si se guardó algo. Decidir por tipo, no por
   simetría.
7. ~~La maqueta estaba desactualizada~~ — **sincronizada**: `docs/preview/index.html`
   toma los tres tiempos y las curvas como variables (`--t1/--t2/--t3`,
   `--ease-out/--ease-inout`), tiene transición en los hovers que no la tenían, elevación
   en las tarjetas, el latido con curva en vez de rampa lineal, la barra del pie escalando
   sobre el ancho real como `Pulse` y el rótulo de avance con segundos. Cumple la condición
   que ella misma se puso: o es espejo del tema, o se borra. Lo que **no** sustituye es el
   `--self-test`: sigue sin haber un autotest visual que dispare los triggers.

---

## 4. Catálogo de herramientas y módulos nuevos

Coste: **c** (días), **m** (semanas), **a** (meses o más). Beneficio entre corchetes.

### 4.1 Medir mejor (donde el programa ya es útil)

1. ~~Progreso real por módulo~~ — **aplicado**: el pie muestra `3 de 5 · Red y
   latencia` con la barra determinista, y mantiene el modo indeterminado más el nombre
   del módulo cuando solo hay un paso. Cero cambios en `ScanService`: el conteo vive en
   el envoltorio por paso que la UI ya construía. Lo que sigue de aquí es marcar
   también *qué* paso está fallando cuando uno tarda (hoy el rótulo no distingue
   «midiendo» de «colgado»), que ya no es cosmético: es un `Timeout` por módulo.
2. **Línea de base y «qué cambió desde la última vez»** [m/bajo]. Ya hay
   `Exporter.PuntajeAnterior` y un historial comparable por cobertura. Un `diff` de dos
   diagnósticos (hallazgos nuevos, desaparecidos, umbrales cruzados, drivers viejos) es
   la pantalla que un técnico quiere, y el 90 % de los datos ya están archivados.
3. **SMART de verdad** [m/alto]. `StorageModule` lee el estado de salud WMI, que es
   binario y a veces optimista. Los atributos que importan (reallocated sectors,
   pending sectors, Wear Leveling, Media Errors, temperatura) se pueden leer por
   `MSFT_StorageReliabilityInformation` en Windows 10+ sin binario externo. Reportar
   «quedan 340 de 1000 ciclos de escritura» en un SSD es la medición más accionable de
   toda la aplicación.
4. **Historial térmico** [c/alto]. `ThermalModule` toma una muestra. Un muestreo
   continuo durante 60 s mientras se corre un juego pesado (o durante el diagnóstico de
   rendimiento) convierte «85 °C» en «llegó a 97 °C y le bajó el reloj al núcleo a
   2,1 GHz durante 12 s», que es la respuesta a la pregunta real: ¿me rinde o no?
5. **Cuellos verificados por proceso**: CPU por proceso ya existe; añadir **I/O de
   disco y red por proceso** (`IOCTL`/`Get-Process` no alcanza, pero sí
   `Process.IoReadWriteBytes` vía `GetProcessIoCounters` o la clase CIM
   `ProcessIO`) explica «quién tiene el disco al 100 %» sin abrir el Monitor de
   recursos.
6. **Uso de GPU por proceso** [m]. `GpuModule` mide por motor 3D de Windows;
   `PDH`/DXKQMT por proceso permitiría nombrar el juego o navegador que se está
   comiendo la GPU. Cuidado: depende del runtime del juego (no todo lo reporta).
7. **Decodificador WHEA por código MCA** [c/medio]. El catálogo ya agrupa por
   proveedor e ID; traducir el `MCACOD` (tipo de error, unidad de cache, bus vs.
   kernel) convierte una lista de eventos en un diagnóstico. Es tabla + parser, sin
   privilegios nuevos.
8. **Minidumps: leer el encabezado** [c/medio]. `StabilityModule` ya los detecta. El
   `BUGCHECK` code + los 4 parámetros del `dump header` se pueden leer sin WinDbg, y
   `0x116` con el offset del driver ya apunta al culpable. Se puede sumar el nombre del
   módulo desde el `MODULES_LIST` sin parsear el stack.
9. **Modo de suspensión: S3 vs. Modern Standby** y sus consecuencias [c/bajo]. `s0_lowpower`
   es la causa frecuente de «se calentó en la mochila» y de desconexiones. Se lee en
   `powercfg /a`; encaja en Térmicas y energía.
10. **Audio: dispositivo activo, formato, latencia del modo exclusivo** [c/medio] y
    **pantalla**: tasa real de refresco por salida (no la declarada), HDR, escala,
    G-Sync/FreeSync por `Win32_PnPEntity`/`D3DKMTEnumBuildVersion` o `QueryDisplayConfig`.
11. **Cadena de alimentación: planes por batería, degradación, ciclos, voltaje de
    reposo** [bajo/c]. El desgaste ya está; los ciclos y el voltaje de diseño vs. actual
    salen de `MSFT_Battery`/WMI y explican por qué el portátil se apaga al 12 %.

### 4.2 Arreglar mejor

12. **Reparaciones con «ensayo»** [c/alto]. `Remediation` ofrece acciones; un modo
    «mostrar qué se tocaría, sin tocar» (que además alimenta el informe para soporte)
    cambia el riesgo percibido de la app. El respaldo de `OptimizationBackupStore` ya
    guarda el estado previo; falta solo el paso de presentación.
13. **Restauración selectiva** [m/medio]. Hoy el respaldo de planes de energía falla
    completo si un plan ya no existe (decisión registrada en la auditoría 2). Omitir el
    que falta con aviso explícito es mejor que no restaurar nada, y deja de contradecir
    el «restaurar» del botón.
14. **Registro de acciones con deshacer** [m/alto]. Un JSON de «qué hice y en qué orden»
    (fecha, módulo, claves de registro con valor anterior, planes tocados) con un botón
    «deshacer el paso 3». Es lo que convierte una herramienta que asusta en una que se
    usa.
15. **Verificación post-cambio**: después de aplicar un ajuste, releer y comparar; si el
    valor no coincide (política de grupo, antivirus, `Winlogon` protegido), decir que no
    se aplicó en lugar de reportar éxito. El patrón ya existe en A09/A10 de la
    auditoría 2.
16. **Diagnóstico de red con ruta completa**: `tracert` existe; añadir `pathping`/
    `paping`-like por salto con pérdida y jitter por tramo localiza «la pérdida está en
    el segundo salto» y no en el ISP. Cuidado: `pathping` tarda minutos (progreso real,
    ver 1).
17. **Wi-Fi: canales vecinos y cointerferencia** [c/medio]. El escaneo ya trae BSSID,
    canal y señal; dibujar el histograma de canales con los AP del vecino encima y
    recomendar canal libre (y decir por qué) es un diagnóstico con respuesta.
18. **Perfil de energía: comprobación de que el plan elegido está realmente vigente**
    (subplanos, `LAPTOP` vs. red, máxima frecuencia en batería), no solo el GUID activo.

### 4.3 Nuevas superficies y herramientas

19. **Paleta de comandos (Ctrl+K)** [c/alto]. Con 18 destinos de navegación, módulos,
    acciones rápidas y ajustes, un campo que filtra y ejecuta es el atajo más barato
    para un usuario que sabe lo que quiere. Reutiliza la lista de navegación como
    fuente.
20. **Programador de chequeos** [m/alto]. Diagnóstico completo diario/semanal, con
    umbral de alerta y notificación solo si algo empeoró. Es lo que convierte la app en
    algo abierto siempre; el `HistoryService` y el score ya existen.
21. **Bandeja + widget de estado** [m/medio]. Punto en la bandeja con el color del score
    y tres acciones. Cuidado con F19: la app que mide consume CPU midiendo; el widget
    tiene que ser barato por defecto (refresco lento, solo red/temperatura).
22. **Informe «para soporte»** con un botón de redacción automática [c/alto]. Nombre de
    equipo, usuario, rutas y SSID redactados; ya se advierte en el README que hay que
    editar a mano. Que sea una opción del exportador, no un consejo.
23. **Exportar a Markdown y a PDF** [c/bajo]. El HTML existe; Markdown para pegarlo en un
    foro o en un ticket, PDF por impresión (sin dependencias) para adjuntar.
24. **Comparar dos diagnósticos archivados** (cualesquiera, del historial) [m/medio].
25. **Editor de umbrales** [m/medio]. El score y las reglas tienen números fijos; un
    panel «qué considero alto consumo de disco» con valores por defecto y exportables.
    Convierte un juicio del autor en una preferencia del usuario.
26. **Reglas como datos** [a/alto]. El motor (`DiagnosticEngine`, `IDiagnosticRule`) ya
    está; portar los ~40 hallazgos de `Core/` a clases de regla era caro y sin
    beneficio (decisión tomada en la 5.0), pero un **formato JSON declarativo** de regla
    (`campo`, `operador`, `umbral`, `severidad`, `mensaje`, `remediación`) permitiría
    reglas nuevas sin compilar, y es el mismo suelo que necesita un plugin system.
    Empezar por exprimir las 6 reglas existentes, no por 40 portadas.
27. **Recetas / perfiles compuestos** [m/medio]. `ProfilesWindow` tiene tres fijos. Un
    perfil editable (lista de tweaks + plan + prioridad) con import/export JSON y
    respaldo, es lo que hace que la función se use: hoy tres perfiles no cubren a nadie.
28. **Modo técnico / «un informe por equipo»** [c/medio]: arrastrar un .txt con un
    `SysDiag.json`, o un `--informe-equipos carpeta\*.json` que produzca una tabla
    comparativa. Encaja con el `HeadlessRunner` que ya existe.
29. **Contador de consumo de la propia app** [bajo/bajo] (RAM, CPU media de la corrida)
    en el pie: una herramienta que mide tiene que decir lo que cuesta.
30. **i18n real** [m/medio]: el arranque fija `es-CL` (`App.xaml.cs:57`) y
    `SatelliteResourceLanguages es`. Un `en-US` sería posible con recursos, y es un
    cambio de producto (los textos son la interfaz). Decidir: o soporte multilingüe, o
    decir en el README que el `es-CL` es intencional (afecta separadores numéricos y
    formatos de fecha).

### 4.4 Ingeniería de la aplicación

31. **Arranque medido y presupuesto** [c/medio]: `PublishReadyToRun` + self-extract
    (`IncludeNativeLibrariesForSelfExtract`) = el .NET se extrae en cada arranque.
    Medir el tiempo real a la primera ventana y comparar contra
    `<PublishSingleFile>` sin extracción o contra una variante de framework-dependent
    en el mismo directorio; el README ya ofrece el modo de 300 KB, pero no hay datos.
32. `EnableCompressionInSingleFile` como opción de build (tamaño contra arranque), con
    el número medido en las dos modalidades.
33. **`SourceRevisionInformationalVersion`**: está en `false`, así que la etiqueta
    visible no lleva SHA. En una app que se reparte como .exe, poder leer `v5.8.0+34a2aa1`
    desde «Acerca de» ahorra media hora de soporte. El commit ya se valida en release.
34. **CI**: caché de NuGet (`actions/cache`) para el restore, `matrix` con
    `windows-2022` además de `windows-latest` (una imagen cambia sin avisar y rompe el
    CI un viernes), y `timeout-minutes` en todos los jobs (un `netsh` que cuelga hoy
    cuelga seis horas).
35. **Dependabot** para los tres `PackageReference` y las tres acciones pinneadas
    (`action-pins.json` existe; que el bot lo use).
36. **Publicar la suite de reglas y umbrales** como documento versionado (`docs/REGLAS.md`)
    generado desde el código, para que «por qué me dijo Warn» tenga una respuesta
    buscable.

---

## 5. Orden sugerido

Seis lotes, cada uno compile y CI-verificable por separado.

| Lote | Contenido | Por qué en ese orden |
|---|---|---|
| 1 | F01 (sacar el emisor del repo y rotar clave) + F02/F03 (licencia honesta: no borrar el código del usuario) | Es lo único que puede destruir el negocio de distribuir la app; el resto se puede arreglar con la versión ya fuera. |
| 2 | F04 (`DebugType=embedded`) + F06 paso 1 (`AutomationProperties`) + F05 (contraste) + F07 (errores) | Una línea cada uno, todos visibles, ninguno toca lógica de medición. |
| 3 | §2.1–3 (nullable por capas, analizadores, `.editorconfig` + `dotnet format` en CI) + F08/F09 | Pagar deuda con la red de seguridad puesta: los analizadores tienen que estar antes de tocar 15 sitios de `Process`, no después. |
| 4 | Movimiento 1–3 (números de tarjetas, línea del historial, contadores del ping) + §3.5 | Terminan el sistema que esta rama empezó y requieren tocar poco: `MetricCard.Create` y dos `TextBlock`. |
| 5 | §4.1.1 (progreso real), #3 (SMART por atributos), #4 (histórico térmico), #2 (diff con la última corrida) | El salto de «informe» a «herramienta de diagnóstico». Cuatro funciones que un usuario nota el primer día. |
| 6 | Firma (Trusted Signing), #11 instalador en CI, #12 winget, #13 notas desde el CHANGELOG | Distribución: es lo que hace que todo lo anterior llegue a otra máquina sin el aviso de SmartScreen. |

**Estado al 2026-10-10.** El lote 2 está cerrado (F04, F06 paso 1, F05, F07, y además
F03, F09, F13 y las pruebas de licencia). Del lote 5 entró §4.1.1 completo —progreso real
por módulo, con cronómetro y aviso a los 90 s—, que no era lo que ese lote prometía: se
adelantó porque era verificable desde la rama. Siguen abiertos el 1 (F01 y F02, que son
decisiones de negocio y no técnicas), el 3 (`.editorconfig`, analizadores, nullable por
capas: hoy el `.csproj` dice `Nullable=disable`), el 4 en su mayor parte y el 6 entero. Ninguno
se cerró «a medias sin decir»: el punto 0 de §2 y esta línea son el estado real.

## 6. Lo que no conviene hacer

- **No** poner el catálogo de drivers a instalar sin confirmación y sin punto de
  restauración (la política de la 5.7.1 es correcta y está documentada: no se
  ejecutan EXE/MSI/INF locales; `WinVerifyTrust` no demuestra compatibilidad).
- **No** prometer transacción global en `Optimizar`/`Restaurar`: un respaldo parcial con
  aviso vale más que una promesa de atomicidad que el registro no puede cumplir.
- **No** reescribir el `Core/` completo para inyectar dependencias: `ScanService`
  ya es compartido por GUI y CLI, que era el objetivo de la 5.0.
- **No** «gamificar» el puntaje (confeti, insignias, racha de días). La regla del tema
  es que el color semántico califica datos y no decora; el movimiento recién
  añadido no cambia eso, lo refuerza.
- **No** añadir más animación continua: la aplicación ya corre reloj por segundo en la
  cabecera y latido mientras mide. Todo lo que se agregue tiene que señalar un cambio,
  y `RepeatBehavior="Forever"` sin forma de detenerse es una deuda (ver F11).
- **No** usar `Get-Counter` ni contadores por nombre, ni `netsh` sin el mapa de claves
  localizadas: es la razón por la que el proyecto usa CIM y BSSID. Cualquier módulo
  nuevo de red o rendimiento tiene que pasar por ahí.

## 7. Cómo verificar esto

La parte de compilación y pruebas corre en el CI de esta rama en cada empujón; la última
foto es `0a0a0a9`: build Release verde, **163/163**, puerta de XAML, `--self-test` sobre el
EXE publicado y validación del paquete. En una máquina propia, lo mismo:

```powershell
dotnet restore SysDiag.sln
dotnet build SysDiag.sln -c Release --no-restore
dotnet test  SysDiag.sln -c Release --no-build --logger "trx;LogFileName=tests.trx" --results-directory TestResults
.\Tools\validate_tests.ps1
.\Tools\validate_xaml.ps1 -Root .
dotnet publish SysDiag.csproj -c Release -o publish
.\Tools\validate_release.ps1 -Root .\publish
```

Queda lo que ninguna prueba cubre — el movimiento, a ojo, en el orden en que se
piensó:

1. Abrir la app: el rail, la cabecera y el pie tienen que aparecer en una cascada de
   ~340 ms, no de golpe.
2. Correr «Diagnóstico completo»: el contenido se atenúa **suave** (0,34 s), la barra
   del pie crece escalando sobre el ancho real y se desvanece **sin saltar** al reiniciar
   el ciclo, el rótulo del pie nombra el módulo y cuenta los segundos, y al terminar el
   puntaje **sube contando** mientras el arco barre.
3. Pasar el cursor por el rail: velo + 2 px de empuje; al seleccionar, el filete crece.
4. Conmutar Resumen/Hallazgos/Datos/Registro: la pastilla se rellena y la etiqueta se
   cruzan; la vista entra subiendo 12 px.
5. Cambiar `Configuración del sistema > Accesibilidad > Efectos visuales >
   Mostrar animaciones en Windows` a desactivado y volver: la interfaz tiene que
   quedar idéntica, sin movimiento, y con el mismo valor final en el puntaje.
6. Apretar `Tab` hasta un botón del pie: tiene que aparecer un anillo cian alrededor.
7. Ajustes: escribir `3,5` en «muestreo» y guardar → el aviso debe decir que quedó en 5.

---

## 8. Lote 5.9 — mejoras generales propuestas (2026-10-10)

Esta sección se escribió junto con el lote de gráficos, mediciones, interfaz por
sección y el módulo de activación. No reemplaza las secciones 1 a 4: las da por
hechas y se concentra en lo que apareció al tocar esas cuatro cosas. Coste:
**c** (días), **m** (semanas), **a** (meses o más).

### 8.1 Aplicado en esta rama

- **Gráficos con escala real** (`Ui/Charts.cs`): el riel de las barras lleva la
  rejilla dibujada en el pincel, el techo se redondea a un peldaño de la escala
  (`Stats.Techo`) y cada gráfico declara su unidad, su rango y su agregado. El
  historial dejó de estar clavado en 0-100 y ajusta el eje a los datos.
- **Estadística en `Core/Stats.cs`**: percentiles, desviación, jitter RFC 3550 y
  escalas, como funciones puras y probadas (`Tools/IntegrationTests/StatsTests.cs`).
  Antes esa aritmética vivía solo en la interfaz, así que un recolector no podía usarla.
- **Mediciones nuevas**: núcleo más cargado, RAM disponible, compromiso de memoria,
  páginas por segundo, caudal de disco y de red, subprocesos y tiempo encendido;
  `LatencyResult` suma **p95** y **dispersión**; los procesos reportan RAM como
  porcentaje del equipo.
- **Filtros en las tres vistas**: severidad en Hallazgos, búsqueda en todas las
  columnas de Datos, nivel y texto en Registro con copiado al portapapeles.
- **Monitor de ping**: franjas rojas por paquete perdido, umbral de 100 ms y una
  línea de resumen con mínima, media, p95, máxima y jitter.
- **Activación de Windows** (`Core/Windows/ActivationModule.cs` +
  `Ui/ActivacionWindowsWindow`): estado real de la licencia y activación por los
  canales oficiales. Ver `docs/HERRAMIENTAS_NUEVAS.md §1` para el límite explícito
  de lo que **no** hace.

### 8.2 Pendientes, en orden de valor

1. **Un contrato de medición con unidad** [m/alto]. `RendimientoResumen` sigue
   siendo `List<KeyValueRow>`: el número viaja dentro de un texto (`"78 % (12,3 GB
   de 16 GB)"`) y las reglas lo recuperan con `NumericText.TryRead`, que es un
   parser de expresiones regular sobre una cadena ya formateada. Un tipo
   `Medicion(decimal Valor, string Unidad, string Texto)` deja de mezclar dato y
   presentación: las reglas comparan, el informe elige el formato y la interfaz
   puede ordenar sin adivinar. Es el cambio que más barato vuelve todo lo demás.
2. **Timeout por módulo y marca de «colgado»** [c/alto]. El rótulo del pie dice
   qué módulo corre, pero no distingue «midiendo» de «esperando a WMI para
   siempre». Un `CancellationTokenSource` con plazo por paso, un aviso y la
   opción de saltar ese módulo convierten un cuelgue en una decisión del usuario.
3. **Caché de consultas WMI con vigencia** [c/medio]. `SystemModule` ya cachea el
   inventario; `Térmicas`, `Almacenamiento` y `Rendimiento` repiten consultas
   dentro de la misma corrida. Una caché por corrida con TTL acorta el
   diagnóstico completo sin cambiar ningún contrato.
4. **Verificación post-cambio generalizada** [c/alto]. El patrón ya existe en
   algunos ajustes: releer y comparar, y decir «no se aplicó» cuando el valor no
   cambió (política de grupo, antivirus, servicio protegido). Extenderlo a todos
   los tweaks y a la limpieza elimina la clase de bug más dañina: la app que
   reporta éxito donde no lo hubo.
5. **Línea de base y «qué cambió desde la última vez»** [m/alto]. Con
   `Exporter.PuntajeAnterior` y el historial comparable por cobertura, un `diff`
   entre dos diagnósticos —hallazgos nuevos, desaparecidos, umbrales cruzados,
   drivers que envejecieron— es la pantalla que un técnico abre primero. Los datos
   ya están archivados; falta compararlos.

   **Hecho en 5.11.** `Core/Diagnostics/ReportDiff.cs` + `Ui/CompararWindow`.
   Ver `docs/HERRAMIENTAS_NUEVAS.md §4.1` para las dos decisiones de diseño
   (emparejar sin la severidad y comparar solo mediciones estructuradas).
6. **Registro de acciones con deshacer por paso** [m/alto]. Un JSON con «qué hice,
   en qué orden y con qué valor anterior» y un botón por paso. Es lo que separa
   una herramienta que asusta de una que se usa.
7. **Accesibilidad más allá del foco** [m/medio]. El anillo de foco cian ya está en
   los botones del tema. Faltan: navegación por teclado dentro de la rejilla de
   datos y del selector de tablas, soporte de tema de alto contraste del sistema
   (`SystemParameters.HighContrast`) y respeto de `TextScaleFactor`. Una app de
   diagnóstico se usa en equipos ajenos, muchas veces con configuraciones ajenas.
8. **Presupuesto de medición propio** [c/bajo]. Mostrar en el pie cuánta CPU y RAM
   costó la corrida. Una herramienta que mide tiene que declarar lo que perturba
   aquello que mide, sobre todo ahora que el muestreo de rendimiento toma
   segundos.
9. **Contrato de unidades en el informe HTML** [c/bajo]. Hoy el HTML imprime el
   mismo texto que la tabla. Con `Medicion` (punto 1) puede elegir precisión y
   separador decimal por destino, y dejar de repetir «n/d» donde falta el dato.
10. **Puerta de XAML ampliada** [c/medio]. `Tools/validate_xaml.ps1` ya comprueba
    `TargetName` de triggers. Añadir: que todo `StaticResource` usado exista en
    `Theme.xaml`, que todo `x:Name` referenciado desde el code-behind exista, y
    que ningún `Binding` use un miembro inexistente. Las tres cosas fallan en
    tiempo de ejecución y no de compilación, que es el peor momento.
11. **Pruebas de las reglas sobre datos puros** [c/medio]. `StatsTests` y
    `ActivationTests` muestran el camino: todo lo que sea función pura se prueba
    sin Windows. Elevar el umbral de pruebas en el CI obliga a que el código nuevo
    nazca separable.
12. **`docs/REGLAS.md` generado** [c/bajo]. Un documento versionado con cada
    regla, su umbral y su remediación, generado desde `DiagnosticEngine` y
    `Remediation`. «¿Por qué me dijo Aviso?» tiene que tener respuesta buscable.
13. **Internacionalización decidida** [m/medio]. El arranque fija `es-CL` y
    `SatelliteResourceLanguages es`. O se soporta `en-US` con recursos, o se dice
    en el README que el español es intencional. Lo que no puede pasar es que
    nadie lo haya decidido: afecta separadores numéricos y formatos de fecha en
    los informes que se comparten.
14. **Actualizaciones y firma** [m/alto, y requiere decisión comercial].
    Un canal de actualización (WinGet, o un manifiesto firmado) y **firma
    Authenticode** del ejecutable. Hoy repartir un .exe sin firmar provoca
    SmartScreen y enseña al usuario a ignorar las advertencias de Windows, que es
    exactamente la conducta que una herramienta de diagnóstico no debería
    fomentar. El costo es un certificado, no código.
15. **Persistencia de la ventana y del estado de la interfaz** [c/bajo]. Tamaño,
    posición, última pestaña y último filtro entre sesiones. Es de las cosas que
    nadie pide y todos notan cuando faltan.

### 8.3 Lo que sigue sin convenir

- **Medir desde el kernel o con un controlador propio** para temperatura por
  núcleo o latencia DPC. Convertiría a SysDiag en un binario con privilegios
  altos y en una superficie de ataque, para un dato que HWiNFO y
  LibreHardwareMonitor ya dan mejor.
- **Telemetría remota**. Ni anónima: la app toca datos de licencia, red y
  estabilidad de un equipo ajeno. El registro local y el informe redactable
  (§4.3 punto 22) cubren la necesidad sin sacar nada del equipo.
- **Activación que no sea por un canal oficial**. Ver
  `docs/HERRAMIENTAS_NUEVAS.md §1.9`.

---

## 9. Lote 5.10 — «Confiar» y primeras herramientas nuevas (2026-10-10)

Esta sección cierra parte de §8.2 y arranca el lote 2 de
`docs/HERRAMIENTAS_NUEVAS.md §6`, que ese documento declara **no negociable**:
agregar herramientas que tocan el sistema sin ensayo ni deshacer aumenta la
superficie de daño a la misma velocidad que el valor.

### 9.1 Aplicado

**Infraestructura de medición**
- **§8.2.3 Caché WMI con vigencia.** Inventario que no cambia en segundos se
  consulta una vez por corrida. Nunca se cachean contadores de rendimiento: el
  módulo de rendimiento mide por diferencia entre dos muestras, y servirle dos
  veces la misma fila congelada no da un valor viejo, da un cero. Tampoco se
  cachean las consultas que fallaron.
- **§8.2.2 Límite de tiempo por módulo.** Cada paso tiene su plazo (4 min para
  red, que hace traceroute; 1 min para térmicas). Al vencer, el módulo se marca
  y la corrida sigue. Lo que el módulo traiga a medias no se fusiona: se
  descarta y queda la corrida anterior de ese módulo, que es más útil que un
  «Red y latencia» vacío. El módulo omitido es un hallazgo propio, no una nota
  al pie. Se distingue la cancelación del usuario (propaga) del vencimiento
  (avisa y continúa).
- **§8.2.8 Presupuesto de medición propio.** Tiempo de CPU del proceso y pico
  de memoria de la corrida, en el pie y en el informe archivado. Una
  herramienta que mide perturba aquello que mide.
- **§8.2.10 Puerta de XAML ampliada.** Comprobación 6: todo `{Binding X}` tiene
  que corresponder a un miembro de su clase de datos. Un binding a un nombre
  que no existe no rompe la compilación: rompe en tiempo de ejecución y en
  silencio.

**Lote 2, «Confiar» (`HERRAMIENTAS §3`)**
- **3.3 Verificación post-cambio.** `Core/Windows/ChangeVerifier.cs`: releer y
  comparar en vez de suponer. Un ajuste se escribe y la llamada devuelve sin
  error en tres casos muy distintos, y solo volviendo a leer se distinguen.
  `TweakModule.Aplicar` devuelve un resultado que separa «ya estaba así» de «se
  escribió y no quedó».
- **3.1 Ensayo antes de aplicar.** `TweakModule.Ensayar` y el botón «Ensayar (no
  aplica nada)». Antes, la única forma de saber qué iba a tocar SysDiag era
  aplicarlo y mirar el registro después.
- **3.2 Deshacer por paso.** `Core/Windows/ActionLog.cs` + `Ui/CambiosWindow`:
  fecha, origen, alcance y un botón por paso. No duplica la lógica de
  reversión, delega en el módulo que aplicó el cambio. Lo no reversible se
  registra igual y marcado.
- **3.4 Restauración selectiva.** `OptimizeModule.Restore` ya no aborta entero
  cuando un plan de energía del respaldo desapareció: lo que falta se omite y
  se nombra. Sigue sin tocar nada si no queda nada restaurable.

**Herramientas nuevas**
- **4.2 Informe redactado.** Quita nombre del equipo, del usuario, ruta del
  perfil, números de serie, nombres de red Wi-Fi y MAC, sobre una copia. Con
  límite de palabra: un usuario llamado «Ana» no puede hacer desaparecer la
  «Ana» de «Analytics» en el nombre de un driver.
- **4.3 Markdown.** Para pegar en un foro o un ticket, que es donde casi
  siempre se pide ayuda. `Ui/ExportarWindow` ofrece los cinco formatos.
- **2.7 Decodificador de pantallazos.** Lee cabecera, módulos y versión de
  Windows del minidump, y cruza el código de detención con el informe de
  errores de Windows. El código **no** está en el minidump: todas las
  herramientas que lo muestran lo leen del registro de eventos, y esta hace lo
  mismo en vez de fingir lo contrario. Códigos sin traducción local se dicen
  sin traducir.
- **5.1 Paleta de comandos (Ctrl+K).** Filtra y ejecuta sobre módulos, acciones
  de la sección activa, exportaciones y vistas, armada desde las mismas
  estructuras que pintan la interfaz.

### 9.2 Lo que sigue, actualizado

De §8.2 quedan pendientes: **1** contrato de medición con unidad, **7**
accesibilidad más allá del foco, **9**
unidades en el HTML, **11** más pruebas de funciones puras, **12**
`docs/REGLAS.md` generado, **13** decisión de i18n, **14** actualizaciones y
firma, **15** persistencia de la ventana.

De `HERRAMIENTAS_NUEVAS.md` quedan: 2.1 monitor en vivo, 2.2 línea térmica,
2.3 SMART por atributos, 2.4 pérdida por salto, 2.5 mapa de canales, 2.6 I/O
por proceso, 2.8 batería, 3.5 desinstalación asistida, 4.4 modo equipos,
5.2 editor de umbrales, 5.3 perfiles editables, 5.4 programador, 5.5
actualizaciones y firma, 5.6 tema claro y alto contraste.
