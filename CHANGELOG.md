# Changelog

Los cambios notables de este proyecto se documentan en este archivo. Las secciones
`[Sin publicar]` son lotes de una rama: se consolidan al cortar el siguiente release.

## [Sin publicar] - 2026-10-10 (SMART por atributos)

Sube la versión a **5.12.0**. Cierra el 2.3 de `docs/HERRAMIENTAS_NUEVAS.md`,
el último del lote 3 («Explicar») que faltaba junto con 2.4 y 2.6.

El estado que reporta Windows llega tarde: dice «correcto» hasta el día que
dice «fallido», y entre medio no dice nada. Los atributos que avisan antes
están en el propio disco y ninguno se veía en la tabla de almacenamiento.

- **`Core/Storage/SmartModule.cs`**. Lee `MSStorageDriver_ATAPISmartData` y sus
  umbrales, y parsea el bloque `VendorSpecific`: hasta 30 entradas de 12 bytes
  con identificador, banderas, valor normalizado, peor valor y seis bytes de
  valor crudo. Sin binario externo. Cuando el bloque no llega —sin
  administrador, o detrás de un puente USB sin paso de comandos— cae sobre los
  contadores de fiabilidad de Windows y lo dice.
- **El umbral del fabricante va antes que el nuestro**, y un valor normalizado
  en 0 no se compara contra él: cero no es una medición, es una entrada que el
  disco no llenó.
- **Lo que no sabemos interpretar se muestra, pero no se califica.** Esas filas
  salen sin semáforo, porque «no hay datos» y «está bien» son cosas distintas y
  llevan a decisiones opuestas.
- **`Ui/SmartWindow`**: una tarjeta por disco, con el motivo cuando no se pudo
  medir, y una nota al pie recordando que estos atributos son una foto del
  momento: lo que dice si un daño avanza es la serie, no la foto.
- Umbral de pruebas del CI: 253.

## [Sin publicar] - 2026-10-10 (comparar dos diagnósticos)

Sube la versión a **5.11.0**. Cierra el lote 3 de `docs/HERRAMIENTAS_NUEVAS.md
§6` («Explicar») en lo que respecta a 4.1, y el punto 5 de `docs/MEJORAS.md
§8.2`.

Los datos del historial ya se archivaban desde hacía versiones, pero solo se
podían mirar de a uno: «¿mejoró o empeoró?» exigía acordarse de lo que decía el
diagnóstico anterior. Después de aplicar un arreglo, esta es la pantalla que
dice si sirvió.

- **`Core/Diagnostics/ReportDiff.cs`**, motor puro con 14 pruebas. Los
  hallazgos se emparejan por **área y mensaje, sin la severidad ni el módulo**:
  si se emparejaran por severidad, un hallazgo que pasó de Aviso a Crítico
  saldría como «uno resuelto y uno nuevo», que es la lectura contraria a lo que
  pasó.
- **Solo se comparan mediciones que son número en el modelo** (espacio libre,
  latencia y pérdida por destino, repeticiones de eventos, volcados). El
  desgaste del SSD viaja dentro de un texto ya formateado y compararlo exigiría
  parsear la presentación: si el formato cambia, la comparación deja de
  encontrar nada y no hay forma de darse cuenta. Queda para el contrato de
  medición de §8.2.1.
- **El ruido no se reporta**: menos de 3 ms de latencia o medio punto de disco
  no aparecen. Un diff que siempre se mueve es un diff que no se lee.
- **`Ui/CompararWindow`**: dos combos con el historial, inversión con un clic y
  carga de los JSON en segundo plano (sesenta diagnósticos son decenas de
  megabytes: leerlos en el hilo de la interfaz congela la ventana).
- **La cobertura se declara antes que el puntaje.** Comparar un «Red» suelto con
  un diagnóstico completo da un puntaje que bajó sin que nada empeorara, y sin
  ese aviso la lectura es exactamente la contraria.
- Umbral de pruebas del CI: 232.

## [Sin publicar] - 2026-10-10 (confiar: ensayo, verificación y deshacer por paso)

Sube la versión a **5.10.0**. Es el lote 2 de `docs/HERRAMIENTAS_NUEVAS.md §6`,
que ese documento declara no negociable: agregar herramientas que tocan el
sistema sin ensayo ni deshacer aumenta la superficie de daño a la misma
velocidad que el valor.

### Medición, más corta y más honesta
- **Caché WMI por corrida** (§8.2.3). El inventario que no cambia en segundos se
  consulta una vez. Nunca se cachean contadores de rendimiento: el módulo de
  rendimiento mide por diferencia entre dos muestras, y servirle dos veces la
  misma fila congelada no da un valor viejo, da un cero. Tampoco se cachean las
  consultas que fallaron.
- **Límite de tiempo por módulo** (§8.2.2). Cada paso tiene su plazo. Al vencer,
  el módulo se marca y la corrida sigue, y lo que traiga a medias no se fusiona.
  El módulo omitido es un **hallazgo propio**, no una nota al pie: «no hay
  problemas de red» cuando lo que pasó es que la red no se pudo medir es la
  forma más dañina de estar en lo correcto. Se distingue la cancelación del
  usuario (propaga) del vencimiento (avisa y continúa).
- **Presupuesto de medición propio** (§8.2.8): tiempo de CPU y pico de memoria
  de la corrida, en el pie y en el informe archivado.
- **Puerta de XAML ampliada** (§8.2.10): cada `{Binding X}` tiene que
  corresponder a un miembro real de su clase de datos.

### Lote «Confiar»
- **Verificación post-cambio** (3.3, §8.2.4). `Core/Windows/ChangeVerifier.cs`:
  releer y comparar en vez de suponer. `TweakModule.Aplicar` devuelve un
  resultado que separa «ya estaba así» de «se escribió y no quedó».
- **Ensayo antes de aplicar** (3.1). `TweakModule.Ensayar` y botón «Ensayar (no
  aplica nada)».
- **Deshacer por paso** (3.2, §8.2.6). `Core/Windows/ActionLog.cs` +
  `Ui/CambiosWindow`: un botón por paso, delegando en el módulo que aplicó el
  cambio. Lo no reversible se registra igual y marcado.
- **Restauración selectiva** (3.4). `OptimizeModule.Restore` omite y nombra lo
  que ya no existe, en vez de no restaurar nada por una pieza que borró otro
  programa.

### Herramientas nuevas
- **Informe redactado para soporte** (4.2). Quita equipo, usuario, ruta del
  perfil, series, nombres de red y MAC, sobre una copia.
- **Exportación a Markdown** (4.3) y ventana `ExportarWindow` con los cinco
  formatos (HTML, HTML redactado, Markdown, Markdown redactado, JSON).
- **Decodificador de pantallazos** (2.7). Código de detención, qué significa y
  qué controladores de terceros estaban cargados, sin WinDbg.
- **Paleta de comandos Ctrl+K** (5.1), armada desde las mismas estructuras que
  pintan la interfaz para no mantener dos listas paralelas.

### Documentación
- `docs/MEJORAS.md §9` y `docs/HERRAMIENTAS_NUEVAS.md` actualizado con lo que
  ya está hecho y lo que queda.

## [Sin publicar] - 2026-10-10 (gráficos, mediciones, interfaz por sección y activación)

Sube la versión a **5.9.0**.

### Gráficos
- **Escala real en lugar de barras sueltas.** `BarChart` redondea el techo a un
  peldaño de la escala (`Stats.Techo`), dibuja la rejilla en el propio riel —como
  pincel, para que no agregue 4 × N elementos al árbol visual— y declara unidad,
  rango y agregado en el pie. Antes el máximo del conjunto era el ancho entero y
  no había forma de leer cuánto valía una barra intermedia.
- **Línea de umbral** opcional en las barras (70 ms en latencia, 40 % en CPU): la
  misma cifra con la que el motor califica el dato, así no hay dos criterios que
  aprender para leer una medición.
- **Evolución del puntaje con eje ajustado.** El historial ya no está clavado en
  0-100: ajusta el rango a los datos con margen y paso redondo, sin salirse nunca
  del dominio. Suma rótulos de eje, rejilla, promedio y el último punto marcado.
  Con puntajes entre 78 y 84, la escala completa aplanaba la serie.
- **Anillo de composición** nuevo (`DonutChart`) para el espacio en disco: la
  pregunta ahí no es «cuál es más grande» sino «de qué está hecho el total».
- **Gráfico de líneas** (`LineChart`) y **series en vivo** (`LiveSeries`) con
  percentil 95, jitter RFC 3550 y marcas de pérdida, reutilizables por cualquier
  panel futuro.
- **Eventos críticos en escala logarítmica**: con un tipo que se repite 40 000
  veces y otros que se repiten 3, la escala lineal dibujaba una barra y cinco
  ceros. Los rótulos se escriben a mano porque la barra mide log₁₀(n) y no n.

### Mediciones
- **`Core/Stats.cs`**: percentiles, desviación estándar muestral, jitter RFC 3550 y
  escalas de eje como funciones puras, con `Tools/IntegrationTests/StatsTests.cs`.
  Antes esa aritmética vivía solo en la capa de interfaz, así que un recolector no
  podía usarla; y `ChartMath` ahora delega en ella en lugar de duplicarla.
- **Rendimiento**: núcleo más cargado (y cuántos pasan de 90 %), RAM disponible,
  compromiso de memoria, páginas por segundo, caudal de disco y de red, subprocesos
  y tiempo encendido. Tres avisos nuevos: núcleo saturado con total bajo, paginación
  sostenida y cola de disco.
- **Red**: `LatencyResult` suma **p95** y **dispersión**. Sin esas dos columnas, una
  red con media de 25 ms y p95 de 180 ms aparecía como sana.
- **Procesos**: la RAM se reporta también como porcentaje de la memoria instalada.
  «1 200 MB» no dice si es mucho; en 8 GB y en 64 GB significa cosas opuestas.

### Interfaz por sección
- **Hallazgos**: filtro por severidad (todos / críticos / avisos / correctos) con el
  contador a la vista. Al cambiar el filtro, la selección salta al primer hallazgo
  visible: sin eso, el panel de la derecha seguía mostrando la recomendación de algo
  que ya no estaba en la lista.
- **Datos**: búsqueda sobre todas las columnas visibles de la tabla activa, contador
  «7 de 312 filas» y un estado propio para «la búsqueda no devuelve nada», que antes
  se veía igual que «la tabla no cargó».
- **Registro**: filtro por nivel (todo / avisos y errores / solo errores), búsqueda
  por texto y **Copiar**, que copia lo que se está viendo y no todo el registro.
- **Monitor de ping**: franja roja por cada paquete perdido —el arreglo que rellena
  los huecos para que la línea no se corte hacía invisible justo el dato que
  importa—, umbral de 100 ms y una línea de resumen con mínima, media, p95, máxima,
  jitter y pérdida.

### Herramientas
- **Activación de Windows 10/11** (`Core/Windows/ActivationModule.cs` +
  `Ui/ActivacionWindowsWindow`), en el panel Sistema. No es un activador: lee el
  estado real de la licencia por WMI e instala claves **que el usuario ya tiene**
  o apunta a un host KMS **propio** de su organización, todo por `slmgr.vbs`, la
  utilidad del propio Windows, con los argumentos en lista y la clave enmascarada en
  el registro. El aviso de qué hace y qué no va arriba de la ventana, no escondido.
  Ver el README y `docs/HERRAMIENTAS_NUEVAS.md §1`.

### Documentación
- `docs/HERRAMIENTAS_NUEVAS.md`, nuevo: catálogo de herramientas nuevas ordenadas
  para poder elegir, con el límite explícito de lo que no entra.
- `docs/MEJORAS.md §8`: mejoras generales del lote, lo aplicado y lo pendiente
  ordenado por valor.

### Pruebas
- `StatsTests` (escalas, percentiles, desviación, jitter y formato) y
  `ActivationTests` (normalización, validación y enmascarado de claves), ambos sobre
  funciones puras, sin Windows ni WPF.

## [Sin publicar] - 2026-10-10 (la documentación y las puertas, al día)

### Herramientas
- `validate_xaml.ps1` corría solo en `build.yml`. Ahora `release.yml` lo ejecuta antes de
  restaurar: un `Trigger` con un `TargetName` mal escrito compila, pasa las pruebas y
  construye la ventana, y solo se rompe al pasar el cursor —no debería poder llegar a un
  `v*` publicado. No necesita compilar, así que falla en segundos.
- Sale del repositorio `sysdiag-1.0.0.sha256`, huérfano desde la auditoría 2 (F12). Sus dos
  sumas se trasladan a la entrada `[1.0.0]` de este archivo: era el único registro que había.
- La maqueta `docs/preview/index.html` deja de mostrar lo que el tema ya no hace: la barra
  indeterminada del pie crece escalando sobre el ancho real del riel, como `Pulse`, en lugar
  del recorrido de 160 px escritos a mano que era el defecto original; y el pie estrena el
  rótulo con módulo y segundos (`3 de 5 · Red y latencia · 14 s`).

### Documentación
- README, `docs/MEJORAS.md` y `docs/DISENO.md` vuelven a describir el árbol actual: el umbral
  de pruebas es 163 (los textos seguían diciendo 114, 148 y 152), el validador de XAML son
  cinco comprobaciones y es puerta, y el progreso por módulo incluye el cronómetro.
- El README enlaza por fin los tres documentos de `docs/` y la maqueta; hasta ahora solo
  existía el enlace a la primera auditoría.
- Se corrigen los recuentos con los que arranca `docs/MEJORAS.md`: son 93 archivos `.cs`,
  12 XAML y **un** proyecto de prueba con siete archivos, no cuatro.

### Formato
- La línea de cabecera del CHANGELOG estaba en inglés en un repositorio en español.

## [Sin publicar] - 2026-10-10 (movimiento, legibilidad y licencia)

### Corregido
- **Una licencia ya no se autodestruye.** `LicenseService` borraba el código guardado
  cuando no verificaba; como el código vinculado se deriva de `máquina\usuario`,
  renombrar el PC o entrar con otra cuenta apagaba la licencia del comprador y le
  quitaba hasta el código para recuperarla. Ahora el código se conserva, el archivo
  recuerda en qué equipo se activó y la ventana de activación lo muestra con el
  motivo. Si el equipo vuelve a su nombre anterior, la licencia reaparece sola.
- **Contraste del texto tenue**: `CTextMuted` estaba por debajo del mínimo de WCAG AA
  en los cuatro fondos donde se usa (4,47 / 4,61 / 4,08 / 3,65) y es el color de los
  rótulos de 9,5–10,5 px. Subido a `#808CC2` (5,81 / 5,99 / 5,30 / 4,74).
- **El error inesperado deja de ser un «Entendido»**: el diálogo distingue si había una
  operación en curso (dice que quedó a medias), muestra la ruta del registro y ofrece
  **Reiniciar SysDiag** con el mismo mecanismo de `--wait-for-parent` que usa la
  elevación, para no chocar contra el mutex de instancia única. Si el propio diálogo no
  se puede construir, queda `MessageBox` como último recurso.
- **Trazas legibles**: `DebugType` pasa de `none` a `embedded`. El registro de un fallo
  ya no termina en `<RunAsync>d__57.MoveNext()`; el PDB viaja dentro del ensamblado, así
  que el paquete publicado sigue siendo un solo archivo.
- **Parseo de la fecha WMI con cultura invariante** (antigüedad de drivers). Estaba
  tapado por la cultura forzada del arranque; el fallo se tragaba como «fecha ilegible».
- **`Core/ProcessRunner` como único embudo de procesos**, ahora con una prueba que lo
  exige. Se corrigió de paso una afirmación de la auditoría: los «15 sitios que creaban
  procesos fuera del runner» eran `ProcessStartInfo` para abrir URLs, archivos y páginas
  de *ms-settings*, que es el API correcto ahí.

### Funcionalidad
- **Un módulo que tarda se nota**. El rótulo del pie pasa a `2 de 5 · Red y latencia ·
  14 s`, y a los 90 s del paso se anota una vez en el registro (`el módulo «Drivers»
  lleva 92 s.`). Con WMI colgado, «midiendo» y «no va a terminar» eran
  indistinguibles: ahora el tiempo transcurrido está en la pantalla y en el log, que es
  lo que hace falta para decidir si esperar o reiniciar. El reloj es un `DispatcherTimer`
  que solo existe durante la corrida y se apaga en el `finally` y en `Dispose`: no suma
  un temporizador permanente al de la cabecera.
- **La regla de recorte de ventanas es una función y tiene pruebas**:
  `Ventana.Recortar(declarado, piso, disponible)` separa la aritmética del contacto con
  WPF, y `WindowSizingTests` (11 casos) fija lo que importa —lo que cabe no se
  toca; el `NaN` de una ventana con `SizeToContent` no se convierte en número; el mínimo
  declarado le gana al área disponible; y un área inválida no deja la ventana en 0, que
  lo evita la salida temprana de `AjustarAPantalla`, no el recorte—). Y al separarla
  salió a la vista un defecto que estaba dentro del propio arreglo: <c>MaxHeight</c> sin
  declarar es <c>+∞</c>, no <c>NaN</c>, así que comprobar solo el <c>NaN</c> recortaba el
  máximo de las nueve ventanas que no declaran ninguno. Se trata a los dos centinelas.
- **La barra del pie ahora dice cuánto falta.** Era indeterminada incluso cuando
  el propio `MainViewModel` tiene la lista de pasos: ahora muestra `3 de 5 · Red y
  latencia` y rellena la barra con el conteo real. Sigue indeterminada (y el rótulo
  pasa a `midiendo: Drivers`) en las acciones de un solo paso, donde un `0 de 1` no
  informa nada. El conteo se lleva en el envoltorio que la UI ya armaba por paso, así
  que `ScanService` —compartido con el runner sin interfaz— no cambió de firma.
- **El pie y el progreso se limpian al empezar, no al terminar**: con `Maximum` en 0
  y `Visibility=Hidden`, un `ProgressBar` sigue midiendo y dividir 0/0 deja un ancho
  NaN en la pasada de layout siguiente.

### Pruebas
- `LicenseRegressionTests` (4) fija que un código que no verifica **no se borra**, que
  un archivo corrupto no deja a nadie sin prueba y que un código emitido por el propio
  emisor sigue activando y sobreviviendo a la releída. Para poder escribirlas,
  `LicenseService.Inicializar` acepta ahora una ruta (por defecto, `LocalAppData`):
  hasta acá, probar la licencia habría significado tocar la licencia real de quien
  corre la suite. Umbral de `validate_tests.ps1`: 114 → **152**.

### Interfaz
- **Sistema de movimiento con reglas propias** (`Ui/Motion.cs` y el bloque
  MOVIMIENTO de `Ui/Theme.xaml`): tres tiempos —0,11 s el puntero, 0,19 s lo que
  entra y sale, 0,34 s lo que acompaña un resultado— y cuatro curvas compartidas.
  Antes había **dos** animaciones en todo el programa (el latido y la barra
  indeterminada) y cuarenta y tantos `Setter` instantáneos: cada hover, cada
  pulsado, cada selección y cada foco cambiaban de estado en un solo cuadro.
- **Puntaje animado**: el número se cuenta y el arco barre hasta el valor nuevo.
  Se enlaza el valor *objetivo* (`ui:Motion.Number`, `ui:Motion.Sweep`) y el
  animador interpola un adjunto aparte, para que re-apuntar a mitad de una
  corrida funcione solo. La trigonometría del anillo pasa a `ScoreArc`, fuente
  única del convertidor y de la animación.
- **Cruce de vistas**: resumen, hallazgos, datos y registro entran con
  desvanecimiento y un empujón de 12 px; la ventana se arma en cascada corta
  (rail → cabecera → pie) con `ui:Motion.Enter` y `EnterDelay`.
- **Cascada en las mediciones**: las tarjetas entran escalonadas 24 ms por fila,
  con tope de 12 filas, y se elevan 1,4 % bajo el puntero (`ui:Motion.Lift`).
  Las barras de los gráficos se estiran desde la etiqueta hasta su longitud.
- **Pastillas de vista**: el relleno violeta y la etiqueta se cruzan (dos
  presentadores que se pasan el testigo) en vez de conmutarse, para no dar un
  parpadeo de tinta oscura sobre fondo oscuro.
- **Casillas y hallazgos**: la palomita se dibuja con `StrokeDashOffset`; el
  filete del módulo activo y el de severidad crecen con un ligero exceso
  (`BackEase`), que es lo que los hace ver encajados.
- **Foco por teclado**: los botones ganan un anillo cian exterior animado. Antes
  el foco solo recoloría el borde, que se perdía contra el color del estado.
- **Barras de desplazamiento** al 35 % y opacas al entrar en su carril.
- **La palomita, el riel y el hover usan capas superpuestas**, no cambios de
  color: los pinceles del tema son compartidos y animar su `Color` repintaría
  cada superficie de la aplicación a la vez.

### Corregido
- **Las ventanas se cortaban en pantallas de 768 px**: `MainWindow` nacía con
  alto 860 y `TweaksWindow` con `MaxHeight` 820. En un portátil de oficina o de
  universidad —el equipo típico de esta aplicación— el área de trabajo son ~728
  px: el pie con «Generar informe» quedaba fuera y el borde de arrastre también.
  `Ui/Ventana.cs` recorta alto/ancho y `MaxHeight`/`MaxWidth` al área real,
  respetando los mínimos, en las diez ventanas.
- **Barra indeterminada mal dimensionada**: viajaba de −160 a 900 px, un número
  escrito a mano. En un pie de 1.300 px nunca llegaba al borde y el reinicio se
  veía como un salto; en una ventana angosta quedaba medio segundo fuera de
  escena. Ahora crece escalando sobre el ancho real del riel.
- **Ajustes corregía en silencio**: `AppSettings.LeerCampo` no falla, corrige —
  «3,5» guardaba 5 y 9999 guardaba 90 — y la ventana se cerraba con un
  «guardado» que describía otra cosa. Los campos se vuelcan con lo que quedó
  guardado y el aviso lista lo ajustado.
- **`AccionTile` y `AccionTilePrimary`** duplicaban la plantilla de `BtnBase` y
  por eso su hover seguía instantáneo. Se retira la duplicación.
- **El latido de la barra superior** era lineal (rampa de 1 a 0,25): ahora
  respira con una curva `EaseInOut`.

### Accesibilidad y rendimiento
- `Ui/Motion.cs` respeta la preferencia del sistema de no animar controles
  (`SystemParameters.ClientAreaAnimation`) y aplica el valor final en seco: la
  información nunca depende de la animación.
- Las entradas usan `BitmapCache` durante la transición y lo sueltan al terminar:
  una vista entera con tarjetas sombreadas se recomponía y re-sombreaba por
  cuadro sin él.
- Ninguna animación toca layout (`Opacity` y transformaciones; nunca `Margin` ni
  `Width`), que es lo que impedía que una cascada de tarjetas corriera a las
  vecinas.

### Herramientas
- `Tools/validate_xaml.ps1`: bien formado, resolución de `{StaticResource}` y
  ámbito de cada `TargetName` dentro de su plantilla. Cubre el hueco que deja tocar
  plantillas: los `Storyboard` de un trigger solo se materializan al pasar el cursor, y
  el autotest del EXE no los mira. Pasó de advertencia a **puerta** en `build.yml` después
  de dos corridas verdes sobre el tema reescrito.
- El validador suma dos reglas que **WPF no avisa nunca**: (4) la propiedad animada tiene
  que existir en el tipo del elemento destino —animar `ScaleX` sobre un
  `TranslateTransform`, u `Opacity` sobre un `ScaleTransform` o sobre un pincel, no lanza
  excepción: simplemente no anima—, y (5) un `RepeatBehavior="Forever"` nacido en
  `Trigger.EnterActions` tiene que tener su `StopStoryboard` con nombre. Se escribieron
  contra una réplica en Python y se verificó por separado que no dicen nada sobre el XAML
  actual y que sí disparan sobre tres errores sembrados.
- La maqueta `docs/preview/index.html` vuelve a decir la verdad: adopta `#808CC2`, los
  tres tiempos del sistema de movimiento como variables, transición en los hovers que no
  tenían, elevación en las tarjetas y el latido con curva en vez de rampa lineal.
- `docs/AUDITORIA_2026-10-09.md` lleva una nota de corrección: A25 decía «ocho ventanas»
  y el `--self-test` construye diez.

## [Sin publicar] - 2026-10-10 (secciones propias, licencia y ajustes de Windows)

### Interfaz
- **Banda de sección en cada módulo**: icono, color por familia (análisis cian,
  mantenimiento violeta, sistema ámbar, datos verde), descripción propia y
  acciones rápidas del módulo — cada sección se presenta a su manera y ofrece
  sus herramientas sin salir de la pantalla.
- **Pie corregido**: la ruta de salida y los botones ya no se superponen; el
  texto vive en su propia columna con elipsis y muestra la ruta real.
- **Monitor de ping**: gráfico con relleno degradado, punto vivo en la última
  muestra y etiquetas de escala en las franjas de referencia.
- **Ajustes**: nueva fila de licencia con el estado actual y acceso a la activación.

### Funcionalidad
- **Códigos de activación** (`SDG7-…`): verificación local por HMAC-SHA256 con
  formato base32 Crockford, prueba de 14 días, licencia Pro por código y modo
  lectura al vencer (el diagnóstico nunca se bloquea). Incluye la herramienta
  `Tools/New-ActivationCode.ps1` para emitir y verificar códigos.
- **Ajustes de Windows 10/11** (`Ui/TweaksWindow`): ~25 ajustes reversibles
  agrupados en Privacidad, Rendimiento, Explorador y Sistema — telemetría,
  anuncios, efectos visuales, programación de GPU por hardware, modo Juego,
  arranque rápido, prioridad para juegos, extensiones de archivo, menú
  contextual clásico, barra de tareas, Widgets/Chat, servicios (SysMain,
  WSearch, DiagTrack), drivers de Windows Update y más. Cada ajuste declara su
  riesgo, guarda el estado anterior antes de escribir y se revierte de uno en
  uno o todos a la vez.
- **Actualizaciones**: botón «Buscar actualizaciones de Windows» junto a las
  acciones de winget en la vista Datos.

## [Sin publicar] - 2026-10-10 (rediseño de la interfaz)

### Interfaz
- **Nueva paleta «medianoche»**: base azul `#0B1020`, acento violeta `#8B7CFF` y
  acento secundario cian `#38D6F0`. El cian viene del logo y se reserva para lo
  que está vivo en ese instante (operación en curso, traza del monitor de ping,
  puntos del gráfico de evolución).
- **Barra superior propia** de 52 px: logo, equipo, indicador de estado de la
  corrida con punto que late mientras mide, reloj y controles de ventana.
- **Rail de navegación** de 182 px (antes 252). El diagnóstico completo deja la
  lista y pasa a botón principal: es lo que se hace al abrir la aplicación.
- **Navegación reagrupada por intención** en cuatro secciones con rótulo y
  filete: Análisis, Mantenimiento, Sistema y Datos.
- **Resumen reordenado**: puntaje, siguiente paso, mediciones y gráficos.
  «Siguiente paso» sube porque es la única parte que dice qué hacer.
- **Hallazgos en dos paneles**: lista a la izquierda, recomendación y reparación
  fijas a la derecha, en vez de apiladas con desplazamiento.
- **Vista Datos**: las acciones de la tabla se agrupan en una barra que solo
  aparece cuando la tabla elegida tiene acciones que ofrecer.
- **Pie con marco propio** y barra de progreso en sitio fijo: la interfaz ya no
  salta al empezar y terminar una corrida.
- **Tipografía**: Segoe UI Variable Display para titulares, Segoe UI Variable
  Text para el cuerpo y Cascadia Mono solo para cifras.
- **Gráfico de barras con riel de fondo**, que hace visible la escala del
  conjunto, y tarjetas de medición con el módulo de origen.

### Ventanas secundarias
- Diálogos, limpieza, optimización, perfiles y ajustes comparten un armazón
  común (`PanelShell`) definido una sola vez en el tema.
- Monitor de ping e Historial estrenan barra de título propia con la marca y
  botón de cierre; antes usaban la del sistema, que no se puede tematizar.
- Los estilos `CampoNumero` y `FilaAjuste` se mudan al tema, y el tema queda
  como única fuente de tokens de color, tipografía, radios y sombras.

### Corregido
- El arco del puntaje y el aro de fondo del anillo quedaban descentrados al
  cambiar de tamaño: las medidas del aro se atan a las del convertidor
  (`ScoreArcConverter`, lienzo de 120×120, radio 50).
- `build.bat` invocaba `Tools\validate_release.ps1` sin `-ExecutionPolicy Bypass` y
  abortaba en equipos con política de ejecución restrictiva o con archivos marcados
  por MOTW (ZIP descargado de GitHub): la compilación y la publicación sí terminaban
  y solo fallaba la validación. Ahora usa bypass de sesión (sin cambiar la política
  del sistema) y distingue «falló la compilación» de «falló solo la validación».

### Documentación
- `docs/DISENO.md`: sistema de diseño (paleta, roles tipográficos, escalas,
  estructura de la ventana y reglas de uso del color).

## [Sin publicar] - 2026-10-09 (auditoría 2)

### Corregido
- **Térmicas:** una zona ACPI sin lectura aparecía como «0 °C». Ahora solo cuentan lecturas plausibles.
- **Winget:** sin actualizaciones disponibles, un código de salida distinto de cero se reportaba como fallo.
- **Wi-Fi:** señal y canal de cada punto de acceso se asignaban por orden de línea. Ahora se leen por BSSID.
- **Drivers:** un origen que falla ya no se presenta como «no se pudo consultar» si otro origen sí respondió.
- **Historial:** la tendencia solo compara diagnósticos de la misma cobertura; cada entrada indica si fue completa o parcial, y declara solo los módulos medidos en esa corrida (no la cobertura acumulada del reporte).
- **Limpieza:** cancelar a mitad de una fila deja la tabla igual que el disco; aviso cuando no hay categorías que analizar.
- **Navegación:** pulsar un módulo durante otra operación ya no deja el ítem marcado.
- **Discos:** el espacio libre salía de una caché de 3 minutos; ahora se lee en cada diagnóstico (un aviso de disco bajo ya no persiste tras limpiar).
- **WHEA:** se guardan hasta 2.000 filas (antes hasta 50.000, unos 10 MB por diagnóstico archivado).
- **Recursos:** los `Process` lanzados desde la interfaz se liberan. La ventana de historial se abre sin esperar a leer todos los diagnósticos archivados.
- **Textos:** los mensajes de punto de restauración ya no sugieren continuar sin él; el estado de ejecución tiene redacción correcta; la caché de Windows Update renombrada indica su ubicación y que puede borrarse; la instalación de drivers avisa que acepta licencias; corregida una frase de arranque y el encabezado «Nombre interno» de servicios.

### Repositorio y calidad
- Dejan de versionarse `sysdiag-1.0.0.zip` (66 MB) e `installer/Output/sysdiag-1.0.0.exe` (50 MB), que ya estaban excluidos por `.gitignore`. El historial no se reescribe.
- Eliminados `OptimizeModule.ReadWlanAutoconfig` y `OptimizeModule.SaveState`, sin llamadores.
- `validate_tests.ps1` sube el mínimo de pruebas de 80 a 114 (la suite auditada).
- README actualizado con la estructura real y los módulos de Monitor de ping, Historial, Perfiles y Ajustes.

### Pruebas
- El autotest del EXE publicado (`--self-test`) construye las ocho ventanas, para detectar errores de XAML o de code-behind antes de que el usuario las abra.
- Nuevo `AuditRound2RegressionTests` (13 casos): temperatura ACPI, detección de catálogo vacío de winget, parser Wi-Fi por BSSID y tendencia por cobertura.

See `docs/AUDITORIA_2026-10-09.md` para el detalle, la evidencia de CI y los pendientes.

## [5.7.1] - 2026-10-02 (rama de auditoría; no publicado)

### Seguridad y correcciones
- Limpieza conservadora por handle, antigüedad/targets y rechazo de enlaces/archivos protegidos.
- Authenticode real; instaladores de drivers locales deshabilitados por política.
- Procesos con timeout/cancelación y drenaje concurrente; errores no se anuncian como éxito.
- Backups esquema 2, primera captura preservada y carpeta/archivo administrativos protegidos.
- Reglas/pipeline comunes GUI/CLI, JSON sin perder hallazgos y reemplazo de módulos vacíos.
- Estados desconocidos no saludables, eventos por proveedor+ID y estado de cancelación honesto.
- Historial comparable, CSV seguro, exportes atómicos/únicos y HTML con alcance/Fin.
- Un solo workflow de release, versión coherente y gate de arranque del EXE publicado.

### Compatibilidad y validación
- Respaldos antiguos de Documentos no se restauran automáticamente; no se borran.
- TCP/IP, cachés, IP fija/rutas/VPN no se prometen reversibles por el JSON.
- Instalación WUA/reset exige punto exitoso; falta matriz física/manual de Windows 10/11.
- Consulta `docs/AUDITORIA.md` para el SHA efectivamente probado y la validación pendiente.

## [1.0.0] - 2026-08-13

### Added
- Full system diagnostic workflow for Windows systems
- Network and Wi‑Fi diagnostics with latency, jitter, packet loss and traceroute
- Performance monitoring with CPU and RAM usage snapshots
- Storage health checks with SMART-like health reporting and disk warnings
- Startup and installed software inventory
- Export of diagnostics in HTML and JSON formats
- Actionable recommendations derived from real findings
- CI and validation workflow for fixture integrity and project health

### Improved
- Better handling of partial diagnostics and empty-data states
- Clear messaging when WMI or admin permissions block certain modules
- Deduplication of findings when merging partial reports
- More robust report summaries and user-facing guidance
- Release packaging and validation improvements for deployment readiness

### Fixed
- Duplicate findings when merging reports from multiple diagnostic passes
- Incomplete or misleading empty-state UI messaging
- Packaging and restore/build issues in the release pipeline
- Project/test configuration issues affecting CI stability

### Requirements
- Windows 10 / 11 x64
- Administrator rights recommended for full WMI and registry coverage
- .NET 8 runtime if not using a self-contained deployment

### Known issues
- Some modules depend on WMI or registry access and may return partial data without admin privileges
- Full code signing requires an Authenticode certificate and a valid timestamp service

### Sumas de control del paquete
Estas dos líneas vivían en `sysdiag-1.0.0.sha256` en la raíz del repositorio, un archivo que
ningún script ni workflow leía y que apuntaba a los dos binarios que la auditoría 2 dejó de
versionar (A20). Eran el único registro de esas sumas, así que se trasladan acá en lugar de
borrarse; el archivo sale del repositorio.

```
3AD3B07B22794E6435D6E1EA2CE0057BCC32C7F421D94E4C8D4CB6AFDCD454D6  sysdiag-1.0.0.zip
421A80EFA727F92C009A19330980DC650839E331002D9FBCE6C9A3E62E6A1AF8  sysdiag-1.0.0.exe
```
