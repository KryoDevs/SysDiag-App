// Los atributos de versión y producto se generan desde SysDiag.csproj.
// Mantener una sola fuente evita discrepancias entre UI, instalador y release.

using System.Runtime.InteropServices;
using System.Runtime.Versioning;
[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
// La aplicación solo corre en Windows: el analizador deja de preguntar por cada API de plataforma
// (docs/MEJORAS.md §2, ítem 4) en lugar de silenciar CA1416 en todo el proyecto.
[assembly: SupportedOSPlatform("windows")]
