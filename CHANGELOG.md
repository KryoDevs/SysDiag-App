# Changelog

All notable changes to this project will be documented in this file.

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
