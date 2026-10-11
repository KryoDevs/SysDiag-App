# Auditoría 2026-10-11: por qué el CI quedó en rojo y qué cierra el lote 5.15

Esta auditoría responde a una pregunta: por qué después del merge del lote 5.14.0 las dos
corridas de `main` fallaron en tres jobs distintos, con un build que no compilaba, una puerta de
documentación que se negaba a pasar y un instalador que sí se compilaba pero «no aparecía».
Lo que sigue abierto está en `docs/MEJORAS.md §8.2` y `docs/HERRAMIENTAS_NUEVAS.md §6`; acá se
registra lo de esta tanda.

**Estado de verificación.** El sandbox donde se escribió esto no tiene SDK de .NET ni PowerShell:
nada de esta tanda se compiló ni ejecutó localmente, todo lo certifica el CI de Windows. **Corrida
verde que valida esto: `8155229`** (la primera verde, `f7a7ae8`, confirmó las causas 1 a 3; el commit final agrega la puerta nueva) — job `build` completo (puerta de scripts, XAML, documentación,
compilación Release, **345/345 pruebas**, umbral del gate, autotest del EXE publicado) y job
`instalador` (publicar, compilar el setup, instalar en silencio, autotestear y desinstalar), más
`validate-fixture` y `compile-debug` en verde. El detalle de lo que se pudo comprobar —y de cómo—
está en «Verificación realizada» y «Lo que no se pudo verificar».

**Las cuatro causas, una línea cada una:** (1) `System.IO` no es un `using` implícito cuando el
proyecto usa `UseWPF`; (2) el script de la puerta de documentación no analizaba en PowerShell 7 —un
signo de dos puntos pegado a un nombre de variable— y por eso no comprobaba nada ni podía decir por
qué; (3) una cita de sección con tres niveles en `release.yml`, que la puerta habría señalado si
corriera; (4) la ruta donde Inno deja el setup, que el job `instalador` buscaba en otro lado. Las
causas 1, 3 y 4 se podían arreglar leyendo; la 2 solo apareció cuando la puerta empezó a publicar su
diagnóstico como anotación.

## Causas

### 1. La lista de `using` implícitos cambia con `UseWPF`

El job `compile-debug` (compila la solución en Debug, sin ejecutar la suite) falló con 11 errores
`CS0103`/`CS0246` en `Tools/IntegrationTests/AuditRegressionTests.cs`: `Path`, `File`, `Directory`
e `InvalidDataException` «no existen en el contexto actual».

La causa no era la ambigüedad que se suponía —`System.Windows.Shapes.Path` contra
`System.IO.Path`, que sería `CS0104`— sino la ausencia: con `UseWPF`, el conjunto de `using`
implícitos que aporta el SDK de escritorio **no incluye `System.IO`**, mientras que el genérico sí.
`CS0103` es «no existe», no «es ambiguo», y eso descarta el otro diagnóstico.

**Corrección:** `<Using Include="System.IO" />` en `Tools/IntegrationTests/IntegrationTests.csproj`
y `using System.IO;` explícito en los archivos afectados, igual que lo hacen los archivos de la
aplicación. Un escaneo del repositorio entero encontró exactamente cuatro archivos de pruebas que
usan `System.IO` sin importarlo (`AuditRegressionTests`, `LicenseRegressionTests`,
`SafetyRegressionTests` y `ThemeRegressionTests`); ninguno de `Core/`, `Ui/`, `Services/` o
`Models/` estaba afectado. El bloque que quita los `using` de WPF se conserva —sigue siendo el que
evita que `Path` se vuelva ambiguo el día que alguien importe WPF en las pruebas— y su comentario
ahora describe lo que pasa de verdad.

### 2. Una cita de sección con tres niveles que no existe

Entre las comprobaciones que la puerta de documentación hace está la de cada cita de sección: tiene
que apuntar a un encabezado que exista. `release.yml` citaba la sección 2 de `docs/MEJORAS.md` con
tres niveles (`§2.13`), y esa sección es una lista numerada, no un conjunto de encabezados: para lo
que no tiene encabezado la forma comprobable es «N.M, ítem K».

Este defecto era real, pero **no era el motivo del paso rojo** —la puerta no llegaba a ejecutarse, ver
la causa 4—: estaba esperando a que la puerta corriera, y la puerta llevaba tres corridas sin correr.
Es la diferencia entre un error que una puerta encuentra y un error que una puerta *hubiera*
encontrado.

**Corrección:** la cita pasó a `MEJORAS §2, ítem 13`, que es lo que describe —el cuerpo del release
se genera del `CHANGELOG.md`— y es la forma que la puerta puede comprobar. No se creó el
encabezado que faltaba: el documento no tiene ítems 2.x y escribir uno para contentar al validador
habría sido maquillar la puerta.

### 3. Dos scripts que no se pusieron de acuerdo sobre una ruta

El job `instalador` compilaba el setup con Inno Setup sin errores y, en el paso siguiente,
`Tools/validate_installer.ps1` fallaba con «se esperaba un instalador y hay 0». La causa es un
valor por defecto: Inno Setup deja el resultado en `Output\`, relativo al script, cuando el `.iss`
no declara `OutputDir` —así lo dice su documentación—, y la puerta lo buscaba en la carpeta del
`.iss`.

**Corrección:** `installer/SysDiag.iss` declara `OutputDir={#SourcePath}\Output` (explícito y
ASCII), `Tools/validate_installer.ps1` busca ahí primero y acepta la ubicación anterior como
alternativa, y `Tools/validate_docs.ps1` añade una comprobación cruzada: la carpeta declarada en el
`.iss` tiene que ser la que la puerta busca. Un desajuste que solo se ve en el CI, después de
compilar el setup, no puede depender de que alguien se acuerde de mirar los dos archivos.

### 4. La puerta de documentación no analizaba, y el CI no podía decirlo

El paso 5 del job `build` —«Comprobar la coherencia de la documentación»— estuvo rojo en **todas** las
corridas desde que se agregó (`13f8cab`, `be3c675`, `9cb3617`, `dfaaf3b`), siempre con el mismo
resumen: «Process completed with exit code 1» y ninguna anotación. Ninguna de las comprobaciones había
fallado: la puerta no escribía nada, ni un error, ni el aviso final.

La causa estaba en una sola línea del script:

```powershell
$errores.Add("$relativo cita $simboloSeccion$seccion de $doc: usa $simboloSeccion" + ...)
```

En PowerShell, dentro de comillas dobles, un signo de dos puntos pegado a un nombre de variable se lee
como **variable con ámbito** (`$doc:` = la variable `doc` en el ámbito `:`). PowerShell 7 lo rechaza
en el análisis sintáctico con `ParserError: Variable reference is not valid`; el archivo no se llega a
cargar, así que no ejecuta la primera comprobación y el `try/catch` que el script ya tenía no puede
atrapar lo que impide cargarlo. El mensaje del analizador existía, pero vivía en el log, y el log de esas corridas **no se pudo
abrir ni descargar** desde la API de GitHub. El diagnóstico apareció recién cuando la puerta empezó
a repetir su salida como anotaciones de la corrida —que sí se leen desde la API—: la causa y el
arreglo de la visibilidad son el mismo episodio.

Los scripts de este repositorio viven en dos PowerShell: Windows PowerShell 5.1 es el de `build.bat` y
PowerShell 7 es el del CI. Un archivo puede estar roto para uno y no para el otro, y hasta ahora nada
lo miraba.

**Corrección:** `${doc}` en lugar de `$doc:` —el delimitador explícito corta la ambigüedad—, una
puerta nueva (`Tools/validate_scripts.ps1`) que analiza **todos** los `.ps1` con el analizador real de
PowerShell 7 antes de que corra cualquier otra puerta y que exige la codificación que 5.1 necesita
para leer acentos (ASCII sin BOM, o BOM), y `Tools/run_gate.ps1`, que publica la salida de una puerta
como anotaciones de la corrida (y como comentario del PR) para que el próximo fallo no dependa de un
log alcanzable. `Tools/validate_installer.ps1` tenía acentos en los comentarios y no llevaba BOM: se le
agregó.

## Qué se cambió

- **CI:** los cuatro arreglos de arriba (`IntegrationTests.csproj`, cuatro archivos de pruebas,
  `.github/workflows/release.yml`, `installer/SysDiag.iss`, `Tools/validate_installer.ps1`,
  `Tools/validate_docs.ps1`) y, en `build.yml`, un `try/catch` que vuelca el motivo del fallo del
  instalador como anotación de error antes de terminar en 1: los logs de un job fallido no siempre
  se pueden leer, y un «exit code 1» a secas no dice nada.
- **Puerta nueva (`Tools/validate_scripts.ps1`):** analiza cada `.ps1` con el analizador real de
  PowerShell 7 —el que ejecuta el CI— y exige que los scripts sean ASCII sin BOM o lleven BOM, que es
  lo que necesita Windows PowerShell 5.1 para leer los acentos. Corre antes que las demás puertas,
  justamente porque comprueba que las demás puedan siquiera ejecutarse.
- **Lanzador de puertas (`Tools/run_gate.ps1`):** las puertas de `build.yml` (XAML y documentación por
  ahora) corren en un proceso hijo con la salida capturada; el lanzador la imprime, la repite como
  anotaciones de la corrida y, si la corrida es de un PR, la deja como comentario. Cuando el comentario
  no se puede publicar —por ejemplo si el token del job no tuviera permiso— lo dice en una anotación,
  en vez de quedarse callado.
- **Canales Wi-Fi (`docs/HERRAMIENTAS_NUEVAS.md §2.5`):** `Core/Network/WifiChannelModule.cs`,
  `Core/Network/NetworkText.cs`, la ventana `Ui/CanalesWindow`, su entrada en el rail, en el
  diccionario de contextos, en la paleta de comandos y en `--self-test`.
- **Estado de la ventana (`docs/MEJORAS.md §8.2`, ítem 15):** `Ui/EstadoVentana.cs`, los campos
  nuevos de `AppSettings` y el enganche en `MainWindow`.
- **Congestión Wi-Fi coherente:** el hallazgo del diagnóstico completo y la ventana de canales
  usan la misma regla de solapamiento (`WifiChannelModule.SeSolapan`).
- **Pruebas:** `CanalesWifiTests` y `EstadoVentanaTests`, 31 pruebas sobre las partes puras de lo
  nuevo, y el umbral del CI de 314 a 345.

## Verificación realizada

- La puerta de documentación se reimplementó en Python sobre el repositorio y se ejecutó dos veces:
  antes del arreglo, para confirmar que la cita era un defecto real; después, para confirmar que no
  queda ninguna. Esa reimplementación es **aproximada**: no ve lo que ve PowerShell, y de hecho daba
  verde mientras la puerta real no analizaba. Lo que se verificó de verdad, en Windows, es la puerta
  real corriendo en el CI.
- El error de análisis de la causa 4 se diagnosticó leyendo las **anotaciones** de la corrida. Antes se
  comprobó que ese canal funcionaba con una anotación de control, que se retiró al cerrar el diagnóstico.
- La sintaxis de los `.ps1` nuevos y modificados se contrastó con un parser de PowerShell independiente
  (tree-sitter) antes de subirlos, y con el analizador real de PowerShell 7 en el CI después.
- Las pruebas y el umbral ya no se estiman: el CI verde dice **345/345 aprobadas, sin omisiones ni
  fallos**, y el job `instalador` pasó la cadena completa (compilar, instalar, autotestear, desinstalar).
- Las citas «§» nuevas y viejas se contrastaron una por una contra los encabezados reales de
  `docs/`.
- El valor por defecto de `OutputDir` se comprobó en la documentación de Inno Setup, no por
  memoria: es la causa del fallo del job `instalador`.
- La lista de archivos que usan `System.IO` sin importarlo salió de un escaneo del árbol completo
  (solo `Core/`, `Ui/`, `Services/`, `Models/`, `Diagnostics/` y `Tools/IntegrationTests/`), no de
  leer un archivo y suponer el resto.

## Lo que no se pudo verificar

- **Compilación y pruebas.** No hay SDK de .NET ni PowerShell en el entorno de escritura: la
  compilación, las pruebas y las puertas las certifica el CI de Windows (corridas `f7a7ae8` y
  `8155229`), no este
  análisis. Antes de esa corrida todo lo de arriba era una hipótesis con evidencia, y las dos
  hipótesis grandes —la lista de `using` y la ruta del instalador— las confirmó el resultado, no el
  razonamiento.
- **El comentario del PR del lanzador.** El canal de anotaciones se verificó con una corrida roja
  real; el comentario, no: la corrida que lo habría publicado es la verde. Si el token del job no
  tuviera permiso, el propio lanzador lo diría en una anotación.
- **El mapa de canales con hardware real.** Las pruebas cubren el análisis puro; que un adaptador
  concreto devuelva vecinos depende del equipo y del servicio de WLAN, y eso solo lo mide un
  Windows con Wi-Fi.
