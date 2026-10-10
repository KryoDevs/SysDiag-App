# Changelog

All notable changes to this project will be documented in this file.

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
