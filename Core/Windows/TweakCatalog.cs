using Microsoft.Win32;

namespace SysDiag.Core.Windows;

/// <summary>
/// Un valor del registro que un ajuste lleva a su estado «activo».
/// El valor se expresa siempre como texto y se convierte según
/// <see cref="Tipo"/> al escribir; así el catálogo es declarativo y el
/// respaldo puede serializarse a JSON sin polimorfismo.
/// </summary>
public class RegValor
{
    public RegistryHive Hive { get; init; }
    public string Ruta { get; init; } = "";
    /// <summary>Nombre del valor. La cadena vacía es el valor predeterminado de la clave.</summary>
    public string Nombre { get; init; } = "";
    public RegistryValueKind Tipo { get; init; } = RegistryValueKind.DWord;
    /// <summary>Valor objetivo cuando el ajuste está activo (texto; DWord en decimal).</summary>
    public string Valor { get; init; } = "";
}

/// <summary>
/// Un ajuste reversible de Windows 10/11. Al aplicar se guarda el estado
/// anterior de cada valor tocado (ver <see cref="TweakModule"/>); al revertir
/// se restaura exactamente ese estado.
/// </summary>
public class TweakAjuste
{
    public string Id { get; init; } = "";
    public string Titulo { get; init; } = "";
    public string Descripcion { get; init; } = "";
    public string Categoria { get; init; } = "";
    /// <summary>Bajo · Medio · Alto: qué tan probable es que quieras deshacerlo.</summary>
    public string Riesgo { get; init; } = "Bajo";
    public bool RequiereAdmin { get; init; }
    /// <summary>True si el ajuste solo tiene efecto en Windows 11 (en 10 se aplica igual, sin efecto visible).</summary>
    public bool SoloWin11 { get; init; }
    /// <summary>Qué puede requerirse tras aplicar: cerrar sesión, reiniciar, etc.</summary>
    public string NotaAplicacion { get; init; } = "";
    public RegValor[] Registros { get; init; } = System.Array.Empty<RegValor>();
    /// <summary>Claves que se crean al aplicar y se eliminan al revertir (para ajustes cuyo estado «inactivo» es la clave ausente).</summary>
    public string[] BorrarAlRevertir { get; init; } = System.Array.Empty<string>();
}

/// <summary>
/// Catálogo de ajustes de Windows 10/11 agrupados por intención.
/// Todo es reversible y cada ajuste declara su riesgo y sus requisitos.
/// Los valores son los mismos que usa el panel de control de Windows o las
/// directivas de grupo oficiales; no hay valores inventados.
/// </summary>
public static class TweakCatalog
{
    private const string Advanced = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";

    public static IReadOnlyList<TweakAjuste> Crear() => new[]
    {
        // ------------------------------- Privacidad -------------------------------
        new TweakAjuste
        {
            Id = "telemetria", Titulo = "Reducir la telemetría al mínimo",
            Descripcion = "Limita los datos de diagnóstico que Windows envía a Microsoft al nivel básico permitido. Requiere Windows 10/11 Pro o Enterprise para ser total.",
            Categoria = "Privacidad", Riesgo = "Bajo", RequiereAdmin = true,
            Registros = new[]
            {
                new RegValor { Hive = RegistryHive.LocalMachine, Ruta = @"SOFTWARE\Policies\Microsoft\Windows\DataCollection", Nombre = "AllowTelemetry", Valor = "0" }
            }
        },
        new TweakAjuste
        {
            Id = "id-publicidad", Titulo = "Desactivar el ID de publicidad",
            Descripcion = "Impide que las apps usen un identificador de publicidad para perfilar tus intereses.",
            Categoria = "Privacidad", Riesgo = "Bajo",
            Registros = new[]
            {
                new RegValor { Hive = RegistryHive.CurrentUser, Ruta = @"Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo", Nombre = "Enabled", Valor = "0" }
            }
        },
        new TweakAjuste
        {
            Id = "historial-actividades", Titulo = "Desactivar el historial de actividades",
            Descripcion = "Windows deja de recopilar y subir el resumen de lo que haces en el equipo (la línea de tiempo y la sincronización entre dispositivos).",
            Categoria = "Privacidad", Riesgo = "Bajo", RequiereAdmin = true,
            Registros = new[]
            {
                new RegValor { Hive = RegistryHive.LocalMachine, Ruta = @"SOFTWARE\Policies\Microsoft\Windows\System", Nombre = "EnableActivityFeed", Valor = "0" },
                new RegValor { Hive = RegistryHive.LocalMachine, Ruta = @"SOFTWARE\Policies\Microsoft\Windows\System", Nombre = "PublishUserActivities", Valor = "0" },
                new RegValor { Hive = RegistryHive.LocalMachine, Ruta = @"SOFTWARE\Policies\Microsoft\Windows\System", Nombre = "UploadUserActivities", Valor = "0" }
            }
        },
        new TweakAjuste
        {
            Id = "sugerencias", Titulo = "Quitar sugerencias y anuncios del menú Inicio",
            Descripcion = "Elimina las recomendaciones de apps, consejos y contenido promocional del menú Inicio y del explorador.",
            Categoria = "Privacidad", Riesgo = "Bajo",
            Registros = new[]
            {
                new RegValor { Hive = RegistryHive.CurrentUser, Ruta = @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", Nombre = "SystemPaneSuggestionsEnabled", Valor = "0" },
                new RegValor { Hive = RegistryHive.CurrentUser, Ruta = @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", Nombre = "SubscribedContent-338388Enabled", Valor = "0" },
                new RegValor { Hive = RegistryHive.CurrentUser, Ruta = @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", Nombre = "SubscribedContent-338389Enabled", Valor = "0" },
                new RegValor { Hive = RegistryHive.CurrentUser, Ruta = @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", Nombre = "SoftLandingEnabled", Valor = "0" }
            }
        },
        new TweakAjuste
        {
            Id = "busqueda-web", Titulo = "Búsqueda de Windows solo local",
            Descripcion = "El buscador del menú Inicio deja de mostrar resultados web y sugerencias de Bing: busca solo en el equipo.",
            Categoria = "Privacidad", Riesgo = "Bajo", SoloWin11 = true,
            Registros = new[]
            {
                new RegValor { Hive = RegistryHive.CurrentUser, Ruta = @"Software\Policies\Microsoft\Windows\Explorer", Nombre = "DisableSearchBoxSuggestions", Valor = "1" },
                new RegValor { Hive = RegistryHive.CurrentUser, Ruta = @"Software\Microsoft\Windows\CurrentVersion\Search", Nombre = "BingSearchEnabled", Valor = "0" }
            }
        },
        new TweakAjuste
        {
            Id = "cortana", Titulo = "Desactivar Cortana",
            Descripcion = "Apaga el asistente de voz integrado (en donde Windows aún lo incluye).",
            Categoria = "Privacidad", Riesgo = "Bajo", RequiereAdmin = true,
            Registros = new[]
            {
                new RegValor { Hive = RegistryHive.LocalMachine, Ruta = @"SOFTWARE\Policies\Microsoft\Windows\Windows Search", Nombre = "AllowCortana", Valor = "0" }
            }
        },

        // ------------------------------- Rendimiento -------------------------------
        new TweakAjuste
        {
            Id = "efectos-rapidos", Titulo = "Efectos visuales a rendimiento",
            Descripcion = "Quita animaciones, sombras y transparencias de la interfaz. Se nota en equipos justos y portátiles en batería.",
            Categoria = "Rendimiento", Riesgo = "Medio", NotaAplicacion = "Cierra sesión para ver el efecto.",
            Registros = new[]
            {
                new RegValor { Hive = RegistryHive.CurrentUser, Ruta = @"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects", Nombre = "VisualFXSetting", Valor = "2" }
            }
        },
        new TweakAjuste
        {
            Id = "gpu-scheduling", Titulo = "Programación de GPU por hardware",
            Descripcion = "Deja que la GPU gestione su propia memoria de vídeo (Windows 10 2004 o más nuevo). Reduce la latencia en juegos y carga la CPU menos.",
            Categoria = "Rendimiento", Riesgo = "Medio", RequiereAdmin = true, NotaAplicacion = "Reinicia el equipo para activarlo.",
            Registros = new[]
            {
                new RegValor { Hive = RegistryHive.LocalMachine, Ruta = @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", Nombre = "HwSchMode", Valor = "2" }
            }
        },
        new TweakAjuste
        {
            Id = "game-mode", Titulo = "Modo Juego activado",
            Descripcion = "Windows prioriza los juegos y pausa tareas en segundo plano mientras juegas.",
            Categoria = "Rendimiento", Riesgo = "Bajo",
            Registros = new[]
            {
                new RegValor { Hive = RegistryHive.CurrentUser, Ruta = @"Software\Microsoft\GameBar", Nombre = "AutoGameModeEnabled", Valor = "1" }
            }
        },
        new TweakAjuste
        {
            Id = "arranque-rapido", Titulo = "Arranque rápido (inicio híbrido)",
            Descripcion = "Windows guarda el núcleo al apagar para arrancar más rápido. En discos modernos el efecto es pequeño; con disco mecánico se nota.",
            Categoria = "Rendimiento", Riesgo = "Medio", RequiereAdmin = true,
            Registros = new[]
            {
                new RegValor { Hive = RegistryHive.LocalMachine, Ruta = @"SYSTEM\CurrentControlSet\Control\Session Manager\Power", Nombre = "HiberbootEnabled", Valor = "1" }
            }
        },
        new TweakAjuste
        {
            Id = "juegos-prioridad", Titulo = "Prioridad alta para juegos (Multimedia)",
            Descripcion = "Sube la prioridad de CPU, GPU y E/S que Windows asigna a los juegos del perfil «Games» clásico.",
            Categoria = "Rendimiento", Riesgo = "Medio", RequiereAdmin = true, NotaAplicacion = "Cierra sesión para ver el efecto.",
            Registros = new[]
            {
                new RegValor { Hive = RegistryHive.LocalMachine, Ruta = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games", Nombre = "GPU Priority", Valor = "8" },
                new RegValor { Hive = RegistryHive.LocalMachine, Ruta = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games", Nombre = "Priority", Valor = "6" },
                new RegValor { Hive = RegistryHive.LocalMachine, Ruta = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games", Nombre = "Scheduling Category", Tipo = RegistryValueKind.String, Valor = "High" },
                new RegValor { Hive = RegistryHive.LocalMachine, Ruta = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games", Nombre = "SFIO Priority", Tipo = RegistryValueKind.String, Valor = "High" }
            }
        },

        // ------------------------------- Explorador -------------------------------
        new TweakAjuste
        {
            Id = "extensiones", Titulo = "Mostrar extensiones de archivo",
            Descripcion = "Los nombres de archivo muestran su extensión (.exe, .pdf…). Es además una medida de seguridad básica frente a archivos disfrazados.",
            Categoria = "Explorador", Riesgo = "Bajo",
            Registros = new[]
            {
                new RegValor { Hive = RegistryHive.CurrentUser, Ruta = Advanced, Nombre = "HideFileExt", Valor = "0" }
            }
        },
        new TweakAjuste
        {
            Id = "ocultos", Titulo = "Mostrar archivos y carpetas ocultos",
            Descripcion = "El explorador muestra lo oculto (los archivos protegidos del sistema siguen ocultos).",
            Categoria = "Explorador", Riesgo = "Bajo",
            Registros = new[]
            {
                new RegValor { Hive = RegistryHive.CurrentUser, Ruta = Advanced, Nombre = "Hidden", Valor = "1" }
            }
        },
        new TweakAjuste
        {
            Id = "compacto", Titulo = "Explorador en vista compacta",
            Descripcion = "Reduce el espaciado entre filas del explorador de Windows 11: entra más información en pantalla.",
            Categoria = "Explorador", Riesgo = "Bajo", SoloWin11 = true,
            Registros = new[]
            {
                new RegValor { Hive = RegistryHive.CurrentUser, Ruta = Advanced, Nombre = "UseCompactMode", Valor = "1" }
            }
        },
        new TweakAjuste
        {
            Id = "menu-clasico", Titulo = "Menú contextual clásico (Windows 11)",
            Descripcion = "El botón derecho vuelve al menú completo de siempre, sin el paso intermedio de Windows 11. Usa la clave estándar de directivas; al revertir se elimina la clave creada.",
            Categoria = "Explorador", Riesgo = "Medio", SoloWin11 = true, NotaAplicacion = "Reinicia el Explorador o cierra sesión.",
            Registros = new[]
            {
                new RegValor { Hive = RegistryHive.CurrentUser, Ruta = @"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32", Nombre = "", Tipo = RegistryValueKind.String, Valor = "" }
            },
            BorrarAlRevertir = new[] { @"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}" }
        },
        new TweakAjuste
        {
            Id = "taskbar-izquierda", Titulo = "Barra de tareas alineada a la izquierda",
            Descripcion = "Los iconos de la barra de tareas de Windows 11 vuelven a la izquierda, como en Windows 10.",
            Categoria = "Explorador", Riesgo = "Bajo", SoloWin11 = true,
            Registros = new[]
            {
                new RegValor { Hive = RegistryHive.CurrentUser, Ruta = Advanced, Nombre = "TaskbarAl", Valor = "0" }
            }
        },
        new TweakAjuste
        {
            Id = "sin-widgets", Titulo = "Quitar Widgets de la barra de tareas",
            Descripcion = "Elimina el botón de noticias y widgets (el panel de tarjetas de Microsoft Start).",
            Categoria = "Explorador", Riesgo = "Bajo", SoloWin11 = true,
            Registros = new[]
            {
                new RegValor { Hive = RegistryHive.CurrentUser, Ruta = Advanced, Nombre = "TaskbarDa", Valor = "0" }
            }
        },
        new TweakAjuste
        {
            Id = "sin-chat", Titulo = "Quitar Chat/Teams de la barra de tareas",
            Descripcion = "Elimina el icono de la conversación de Teams integrado en Windows 11.",
            Categoria = "Explorador", Riesgo = "Bajo", SoloWin11 = true,
            Registros = new[]
            {
                new RegValor { Hive = RegistryHive.CurrentUser, Ruta = Advanced, Nombre = "TaskbarMn", Valor = "0" }
            }
        },
        new TweakAjuste
        {
            Id = "menu-rapido", Titulo = "Menús más ágiles",
            Descripcion = "Acorta la espera antes de abrir los menús desplegables (de 400 ms a 200 ms).",
            Categoria = "Explorador", Riesgo = "Bajo",
            Registros = new[]
            {
                new RegValor { Hive = RegistryHive.CurrentUser, Ruta = @"Control Panel\Desktop", Nombre = "MenuShowDelay", Tipo = RegistryValueKind.String, Valor = "200" }
            }
        },

        // ------------------------------- Sistema -------------------------------
        new TweakAjuste
        {
            Id = "sin-sysmain", Titulo = "Desactivar SysMain (Superfetch)",
            Descripcion = "Detiene el precargado de aplicaciones. En discos SSD modernos ahorra trabajo de fondo; en equipos con poca RAM puede cambiar el tiempo de arranque de apps.",
            Categoria = "Sistema", Riesgo = "Alto", RequiereAdmin = true, NotaAplicacion = "Reinicia el equipo.",
            Registros = new[]
            {
                new RegValor { Hive = RegistryHive.LocalMachine, Ruta = @"SYSTEM\CurrentControlSet\Services\SysMain", Nombre = "Start", Valor = "4" }
            }
        },
        new TweakAjuste
        {
            Id = "sin-indice", Titulo = "Desactivar la indexación de búsqueda (WSearch)",
            Descripcion = "Libera CPU y disco a cambio de que las búsquedas del explorador sean más lentas. Útil en equipos de escritorio con discos llenos.",
            Categoria = "Sistema", Riesgo = "Alto", RequiereAdmin = true, NotaAplicacion = "Reinicia el equipo.",
            Registros = new[]
            {
                new RegValor { Hive = RegistryHive.LocalMachine, Ruta = @"SYSTEM\CurrentControlSet\Services\WSearch", Nombre = "Start", Valor = "4" }
            }
        },
        new TweakAjuste
        {
            Id = "sin-diagtrack", Titulo = "Desactivar el servicio de telemetría (DiagTrack)",
            Descripcion = "Detiene «Experiencias y diagnósticos del usuario conectado», el servicio que recopila y envía datos de uso.",
            Categoria = "Sistema", Riesgo = "Medio", RequiereAdmin = true, NotaAplicacion = "Reinicia el equipo.",
            Registros = new[]
            {
                new RegValor { Hive = RegistryHive.LocalMachine, Ruta = @"SYSTEM\CurrentControlSet\Services\DiagTrack", Nombre = "Start", Valor = "4" }
            }
        },
        new TweakAjuste
        {
            Id = "sin-drivers-wu", Titulo = "Que Windows Update no actualice drivers",
            Descripcion = "Las actualizaciones de Windows dejan de incluir controladores: los drivers solo se actualizan desde el fabricante. Evita que una actualización cambie un driver estable.",
            Categoria = "Sistema", Riesgo = "Medio", RequiereAdmin = true,
            Registros = new[]
            {
                new RegValor { Hive = RegistryHive.LocalMachine, Ruta = @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate", Nombre = "ExcludeWUDriversInQualityUpdate", Valor = "1" }
            }
        },
        new TweakAjuste
        {
            Id = "sin-noticias", Titulo = "Desactivar el feed de noticias de Windows",
            Descripcion = "Oculta el contenido de Microsoft Start del sistema de tarjetas (junto a «Quitar Widgets» deja la barra limpia).",
            Categoria = "Sistema", Riesgo = "Bajo", SoloWin11 = true,
            Registros = new[]
            {
                new RegValor { Hive = RegistryHive.CurrentUser, Ruta = @"Software\Microsoft\Windows\CurrentVersion\Feeds", Nombre = "ShellFeedsTaskbarViewMode", Valor = "2" }
            }
        }
    };
}
