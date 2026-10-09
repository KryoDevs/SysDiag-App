# Changelog

All notable changes to this project will be documented in this file.

## [Sin publicar] - 2026-10-09 (auditoría 2)

### Corregido
- **Térmicas:** una zona ACPI sin lectura aparecía como «0 °C». Ahora solo cuentan lecturas plausibles.
- **Winget:** sin actualizaciones disponibles, un código de salida distinto de cero se reportaba como fallo.
- **Wi-Fi:** señal y canal de cada punto de acceso se asignaban por orden de línea. Ahora se leen por BSSID.
- **Drivers:** un origen que falla ya no se presenta como «no se pudo consultar» si otro origen sí respondió.
- **Historial:** la tendencia solo compara diagnósticos de la misma cobertura; cada entrada indica si fue completa o parcial.
- **Limpieza:** cancelar a mitad de una fila deja la tabla igual que el disco; aviso cuando no hay categorías que analizar.
- **Navegación:** pulsar un módulo durante otra operación ya no deja el ítem marcado.
- **Discos:** el espacio libre salía de una caché de 3 minutos; ahora se lee en cada diagnóstico (un aviso de disco bajo ya no persiste tras limpiar).
- **WHEA:** se guardan hasta 2.000 filas (antes hasta 50.000, unos 10 MB por diagnóstico archivado).
- **Recursos:** los `Process` lanzados desde la interfaz se liberan.
- **Textos:** los mensajes de punto de restauración ya no sugieren continuar sin él; el estado de ejecución tiene redacción correcta; la caché de Windows Update renombrada indica su ubicación y que puede borrarse; la instalación de drivers avisa que acepta licencias; corregida una frase de arranque y el encabezado «Nombre interno» de servicios.

### Repositorio y calidad
- Dejan de versionarse `sysdiag-1.0.0.zip` (66 MB) e `installer/Output/sysdiag-1.0.0.exe` (50 MB), que ya estaban excluidos por `.gitignore`. El historial no se reescribe.
- Eliminados `OptimizeModule.ReadWlanAutoconfig` y `OptimizeModule.SaveState`, sin llamadores.
- `validate_tests.ps1` sube el mínimo de pruebas de 80 a 113 (la suite auditada).
- README actualizado con la estructura real y los módulos de Monitor de ping, Historial, Perfiles y Ajustes.

### Pruebas
- Nuevo `AuditRound2RegressionTests` (12 casos): temperatura ACPI, detección de catálogo vacío de winget, parser Wi-Fi por BSSID y tendencia por cobertura.

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
