# Auditoría 2026-10-11: por qué el CI quedó en rojo y qué cierra el lote 5.15

Esta auditoría responde a una pregunta: por qué después del merge del lote 5.14.0 las dos
corridas de `main` fallaron en tres jobs distintos, con un build que no compilaba, una puerta de
documentación que se negaba a pasar y un instalador que sí se compilaba pero «no aparecía».
Lo que sigue abierto está en `docs/MEJORAS.md §8.2` y `docs/HERRAMIENTAS_NUEVAS.md §6`; acá se
registra lo de esta tanda.

**Estado de verificación.** El sandbox donde se escribió esto no tiene SDK de .NET ni PowerShell.
Nada de esta tanda se ha compilado ni ejecutado localmente: las pruebas, el umbral y las puertas
quedan pendientes del CI de Windows. El detalle de lo que sí se pudo comprobar —y de cómo— está en
«Verificación realizada» y «Lo que no se pudo verificar». **SHA de la corrida que valida esto:**
pendiente; se anota al cerrar el PR con la primera corrida verde.

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

### 2. Un número inventado en una cita de documento

El job `build` falló en el paso 5, «Comprobar la coherencia de la documentación». El único error
era una cita de `release.yml`: apuntaba a la sección 2 de `docs/MEJORAS.md` con tres niveles
(`§2.13`), y esa sección es una lista numerada, no un conjunto de encabezados. La propia puerta
explica la salida en su mensaje: para lo que no tiene encabezado se escribe «N.M, ítem K».

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

## Qué se cambió

- **CI:** los tres arreglos de arriba (`IntegrationTests.csproj`, cuatro archivos de pruebas,
  `.github/workflows/release.yml`, `installer/SysDiag.iss`, `Tools/validate_installer.ps1`,
  `Tools/validate_docs.ps1`) y, en `build.yml`, un `try/catch` que vuelca el motivo del fallo del
  instalador como anotación de error antes de terminar en 1: los logs de un job fallido no siempre
  se pueden leer, y un «exit code 1» a secas no dice nada.
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
  antes del arreglo, para confirmar que el único error era la cita; después, para confirmar que no
  queda ninguno. Es la misma comprobación que corre `Tools/validate_docs.ps1` en el CI.
- Las citas «§» nuevas y viejas se contrastaron una por una contra los encabezados reales de
  `docs/`.
- El valor por defecto de `OutputDir` se comprobó en la documentación de Inno Setup, no por
  memoria: es la causa del fallo del job `instalador`.
- La lista de archivos que usan `System.IO` sin importarlo salió de un escaneo del árbol completo
  (solo `Core/`, `Ui/`, `Services/`, `Models/`, `Diagnostics/` y `Tools/IntegrationTests/`), no de
  leer un archivo y suponer el resto.

## Lo que no se pudo verificar

- **Compilación y pruebas.** No hay SDK de .NET ni PowerShell en el entorno de escritura: el build,
  las 345 pruebas y las puertas `validate_xaml`, `validate_docs`, `validate_tests`,
  `validate_release`, `validate_version`, `validate_installer` y `validate_fixture` se verifican
  por primera vez en la corrida de Windows.
- **El instalador.** El arreglo de la ruta es una hipótesis con evidencia documental (el valor por
  defecto de Inno) y con el contexto de la corrida (el job tarda dos minutos y el paso del setup
  falla antes de instalar nada), pero la cadena completa —compilar, instalar, autotestear,
  desinstalar— no se pudo ejecutar acá.
- **El mapa de canales con hardware real.** Las pruebas cubren el análisis puro; que un adaptador
  concreto devuelva vecinos depende del equipo y del servicio de WLAN, y eso solo lo mide un
  Windows con Wi-Fi.
