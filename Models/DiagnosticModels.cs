using System;
using System.Collections.Generic;
using System.Linq;
using SysDiag.Core;
using SysDiag.Core.Recommendations;
using System.Text.Json.Serialization;

namespace SysDiag.Models;

public enum Severity
{
    Ok,
    Warn,
    Bad
}
public class Finding
{
    public Severity Severity { get; set; }
    public string Area { get; set; } = "";
    public string Message { get; set; } = "";
    public string Action { get; set; } = "";
    /// <summary>Qué módulo lo generó. Lo usa la UI para fusionar corridas parciales sin duplicar.</summary>
    public string Modulo { get; set; } = "";

    /// <summary>
    /// Acción concreta que corrige este hallazgo, si existe una que sea segura
    /// y reversible. Muchos hallazgos no la tienen a propósito: un disco con
    /// errores o un jitter del proveedor no se arreglan desde el equipo, y
    /// ofrecer un botón ahí sería mentirle al usuario.
    /// </summary>
    public string AccionId { get; set; } = "";
    public string AccionTexto { get; set; } = "";

    public string Etiqueta => Severity switch
    {
        Severity.Bad => "Crítico",
        Severity.Warn => "Atención",
        _ => "Correcto"
    };
}
/// <summary>Contenedor de todo lo que produce una ejecución.</summary>
public class DiagnosticReport : IJsonOnDeserialized
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime Inicio { get; set; } = DateTime.Now;
    public DateTime? Fin { get; set; }
    public string EstadoEjecucion { get; set; } = "Sin registro de ejecución";
    public Dictionary<string, DateTime> ModulosCompletados { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Módulos que no respondieron antes de su límite de tiempo. Se guardan
    /// aparte de los datos porque lo que el usuario necesita no es el número
    /// de segundos sino saber que ese módulo no se midió: un «Red y latencia»
    /// ausente sin aviso se lee como «la red está bien».
    /// </summary>
    public List<string> ModulosColgados { get; set; } = new();

    /// <summary>Segundos que tardó cada módulo, para poder decir cuál fue el lento.</summary>
    public Dictionary<string, double> DuracionesModulo { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Lo que costó la corrida en CPU y memoria. Una herramienta que mide
    /// tiene que declarar lo que perturba aquello que mide: si el diagnóstico
    /// de rendimiento se come el 30 % de un núcleo mientras mide el
    /// rendimiento, sus propios números valen menos, y el usuario merece
    /// saberlo.
    /// </summary>
    public string CosteMedicion { get; set; } = "";

    public void MarcarColgado(string clave)
    {
        ModulosColgados ??= new List<string>();
        if (!ModulosColgados.Contains(clave, StringComparer.OrdinalIgnoreCase)) ModulosColgados.Add(clave);
    }

    public void RegistrarDuracion(string clave, TimeSpan transcurrido)
    {
        DuracionesModulo ??= new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        DuracionesModulo[clave] = Math.Round(transcurrido.TotalSeconds, 1);
    }

    /// <summary>Marca con la que se reconocen los avisos de módulo omitido, para poder retirarlos.</summary>
    public const string MarcaColgado = "modulo-colgado";

    /// <summary>
    /// Los avisos de módulos colgados de una corrida anterior se retiran al
    /// empezar la siguiente. Si no, un módulo que se colgó una vez y a la
    /// siguiente respondió bien dejaría su aviso pegado para siempre, y un
    /// aviso que ya no es cierto es peor que no avisar.
    /// </summary>
    public void LimpiarAvisosColgados() =>
        Hallazgos.RemoveAll(f => string.Equals(f.AccionId, MarcaColgado, StringComparison.Ordinal));

    /// <summary>
    /// Un módulo omitido es un hallazgo, no una nota al pie: aparece en la
    /// lista, en el informe y en el puntaje de cobertura. Decir «no hay
    /// problemas de red» cuando lo que pasó es que la red no se pudo medir es
    /// la forma más dañina de estar en lo correcto.
    /// </summary>
    public void AddColgado(string clave, TimeSpan limite)
    {
        string nombre = NombresModulos.TryGetValue(clave, out var etiqueta) ? etiqueta : clave;
        Add(Severity.Warn, "Diagnóstico",
            $"El módulo «{nombre}» no se pudo medir: no respondió en {limite.TotalSeconds:0} s.",
            "Repite solo ese módulo. Si se repite, reinicia el equipo o revisa el servicio de Instrumental de administración de Windows (winmgmt).",
            MarcaColgado, modulo: "");
    }

    /// <summary>Frase corta para el pie, o cadena vacía si la corrida salió completa.</summary>
    public string ResumenColgados()
    {
        var nombres = (ModulosColgados ?? new List<string>())
            .Select(c => NombresModulos.TryGetValue(c, out var nombre) ? nombre : c)
            .ToList();
        return nombres.Count == 0 ? ""
            : nombres.Count == 1 ? $"{nombres[0]} no respondió a tiempo y se omitió"
            : $"{string.Join(", ", nombres)} no respondieron a tiempo y se omitieron";
    }

    public string Equipo { get; set; } = Environment.MachineName;

    [JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
    public List<Finding> Hallazgos { get; } = new();
    public List<KeyValueRow> Sistema { get; set; } = new();
    public List<DiskRow> Discos { get; set; } = new();
    public List<MemoryRow> Memoria { get; set; } = new();
    public List<KeyValueRow> WiFi { get; set; } = new();
    public List<LatencyResult> Red { get; set; } = new();
    public List<KeyValueRow> RendimientoResumen { get; set; } = new();
    public List<ProcessRow> TopCpu { get; set; } = new();
    public List<ProcessRow> TopRam { get; set; } = new();
    public List<KeyValueRow> Termicas { get; set; } = new();
    public List<EventSummaryRow> EventosResumen { get; set; } = new();
    public List<EventRow> EventosDetalle { get; set; } = new();
    public List<EventRow> Whea { get; set; } = new();
    public List<DumpRow> Minidumps { get; set; } = new();
    public List<TraceHop> Traceroute { get; set; } = new();
    public string TracerouteDestino { get; set; } = "";
    public List<KeyValueRow> Bateria { get; set; } = new();
    public List<CleanupRow> Limpieza { get; set; } = new();
    public string EspacioLiberado { get; set; } = "";
    public List<DriverRow> Drivers { get; set; } = new();
    public List<SecurityCheckRow> Seguridad { get; set; } = new();
    public List<GpuInfo> Gpus { get; set; } = new();
    public List<UpdateRow> Actualizaciones { get; set; } = new();
    public List<DriverUpdateRow> DriversDisponibles { get; set; } = new();
    public List<StorageRow> Almacenamiento { get; set; } = new();
    public List<StartupRow> Arranque { get; set; } = new();
    public List<ServiceRow> Servicios { get; set; } = new();
    public List<ProgramRow> Programas { get; set; } = new();
    public List<WifiNetworkRow> RedesCercanas { get; set; } = new();
    public List<Recommendation> Recomendaciones { get; set; } = new();
    public int Puntaje { get; set; } = -1;

    public void ActualizarRecomendaciones() => Recomendaciones = RecommendationEngine.Generate(this);

    public bool TieneDatosRelevantes() =>
        Hallazgos.Count > 0 || Sistema.Count > 0 || Discos.Count > 0 || Memoria.Count > 0 ||
        WiFi.Count > 0 || Red.Count > 0 || RendimientoResumen.Count > 0 || TopCpu.Count > 0 ||
        TopRam.Count > 0 || Termicas.Count > 0 || EventosResumen.Count > 0 || EventosDetalle.Count > 0 ||
        Whea.Count > 0 || Minidumps.Count > 0 || Traceroute.Count > 0 || Bateria.Count > 0 ||
        Limpieza.Count > 0 || Drivers.Count > 0 || Seguridad.Count > 0 || Gpus.Count > 0 ||
        Actualizaciones.Count > 0 || DriversDisponibles.Count > 0 || Almacenamiento.Count > 0 ||
        Arranque.Count > 0 || Servicios.Count > 0 || Programas.Count > 0 || RedesCercanas.Count > 0;

    public string ResumenEstado()
    {
        string colgados = ResumenColgados();
        if (EstadoEjecucion == "Cancelado")
            return "La ejecución fue cancelada. Los datos visibles pueden incluir mediciones anteriores; no es un diagnóstico completado.";
        if (EstadoEjecucion == "Falló o incompleto")
            return "La ejecución falló o quedó incompleta. Los datos visibles pueden incluir mediciones anteriores; no es un diagnóstico completado.";
        if (TieneDatosRelevantes())
        {
            string avisoColgados = colgados.Length > 0 ? $" {colgados}; esos datos son de una corrida anterior." : "";
            return Hallazgos.Count == 0
                ? $"La comprobación se completó, pero no se detectaron problemas relevantes en los datos disponibles.{avisoColgados}"
                : $"La comprobación se completó con los datos disponibles del equipo.{avisoColgados}";
        }

        var faltantes = ModulosFaltantes();
        var lista = faltantes.Count > 0 ? $" Módulos pendientes: {string.Join(", ", faltantes.Take(3))}." : "";
        var bloqueoWmi = Wmi.LastAccessDenied
            ? " El sistema respondió con acceso denegado a WMI o al registro; repetir como administrador suele completar los módulos faltantes."
            : "";

        if (!AppEnv.IsAdmin)
            return $"No se obtuvieron datos útiles. Repite la comprobación como administrador para completar WMI, registro y contadores del sistema.{lista}{bloqueoWmi}";

        return $"No se obtuvieron datos útiles. Si la comprobación se repite como administrador, normalmente se completan WMI, registro y contadores del sistema.{lista}{bloqueoWmi}";
    }

    public List<string> ModulosConDatos()
    {
        var modulos = new List<string>();
        if (Red.Count > 0) modulos.Add("Red y latencia");
        if (RendimientoResumen.Count > 0 || TopCpu.Count > 0 || TopRam.Count > 0) modulos.Add("Rendimiento");
        if (Termicas.Count > 0 || Bateria.Count > 0 || Gpus.Count > 0) modulos.Add("Térmicas y energía");
        if (Almacenamiento.Count > 0) modulos.Add("Almacenamiento");
        if (EventosResumen.Count > 0 || Whea.Count > 0 || Minidumps.Count > 0 || EventosDetalle.Count > 0) modulos.Add("Estabilidad");
        if (Seguridad.Count > 0) modulos.Add("Seguridad");
        if (Drivers.Count > 0 || DriversDisponibles.Count > 0) modulos.Add("Drivers");
        if (Actualizaciones.Count > 0) modulos.Add("Actualizaciones");
        if (Limpieza.Count > 0) modulos.Add("Limpieza");
        if (Arranque.Count > 0 || Servicios.Count > 0 || Programas.Count > 0) modulos.Add("Arranque y software");
        if (Sistema.Count > 0) modulos.Add("Equipo");
        return modulos;
    }

    public List<string> ModulosFaltantes()
    {
        var todos = new List<string>
        {
            "Red y latencia",
            "Rendimiento",
            "Térmicas y energía",
            "Almacenamiento",
            "Estabilidad",
            "Seguridad",
            "Drivers",
            "Actualizaciones",
            "Limpieza",
            "Arranque y software"
        };

        var existentes = ModulosCompletados.Count > 0
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(ModulosConDatos(), StringComparer.OrdinalIgnoreCase);
        foreach (var key in ModulosCompletados.Keys)
            if (NombresModulos.TryGetValue(key, out var nombre)) existentes.Add(nombre);
        return todos.Where(m => !existentes.Contains(m)).ToList();
    }

    public void Add(Severity severity, string area, string message, string action = "",
                    string accionId = "", string modulo = "")
    {
        var finding = new Finding
        {
            Severity = severity,
            Area = area,
            Message = message,
            Action = action,
            AccionId = accionId,
            Modulo = modulo
        };

        if (!ContainsFinding(finding))
            Hallazgos.Add(finding);
    }

    private static bool SameFinding(Finding a, Finding b)
    {
        if (ReferenceEquals(a, b)) return true;
        return string.Equals(a.Area, b.Area, StringComparison.OrdinalIgnoreCase)
            && a.Severity == b.Severity
            && string.Equals(a.Message, b.Message, StringComparison.OrdinalIgnoreCase)
            && string.Equals(a.Modulo, b.Modulo, StringComparison.OrdinalIgnoreCase)
            && string.Equals(a.AccionId, b.AccionId, StringComparison.OrdinalIgnoreCase);
    }

    private bool ContainsFinding(Finding candidate)
    {
        return Hallazgos.Any(existing => SameFinding(existing, candidate));
    }

    /// <summary>
    /// Copia los datos de otro reporte (de un módulo que sí corrió) sin tocar los
    /// campos que ese módulo no toca. Así, correr un módulo suelto no borra lo
    /// que ya se sabía de los demás. El inventario (Sistema/Discos/Memoria) se
    /// maneja aparte porque todos los módulos lo refrescan de paso.
    /// </summary>
    public void MergeFrom(DiagnosticReport other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (other.Sistema.Count > 0)
        {
            Equipo = other.Equipo;
            Sistema = other.Sistema;
            Discos = other.Discos;
            Memoria = other.Memoria;
        }
        foreach (var (module, date) in other.ModulosCompletados) ModulosCompletados[module] = date;
        if (other.WiFi.Count > 0) WiFi = other.WiFi;
        if (other.Red.Count > 0) Red = other.Red;
        if (other.Traceroute.Count > 0) { Traceroute = other.Traceroute; TracerouteDestino = other.TracerouteDestino; }
        if (other.RendimientoResumen.Count > 0) RendimientoResumen = other.RendimientoResumen;
        if (other.TopCpu.Count > 0) TopCpu = other.TopCpu;
        if (other.TopRam.Count > 0) TopRam = other.TopRam;
        if (other.Termicas.Count > 0) Termicas = other.Termicas;
        if (other.Bateria.Count > 0) Bateria = other.Bateria;
        if (other.EventosResumen.Count > 0) EventosResumen = other.EventosResumen;
        if (other.EventosDetalle.Count > 0) EventosDetalle = other.EventosDetalle;
        if (other.Whea.Count > 0) Whea = other.Whea;
        if (other.Minidumps.Count > 0) Minidumps = other.Minidumps;
        if (other.Limpieza.Count > 0) { Limpieza = other.Limpieza; EspacioLiberado = other.EspacioLiberado; }
        if (other.Drivers.Count > 0) Drivers = other.Drivers;
        if (other.Seguridad.Count > 0) Seguridad = other.Seguridad;
        if (other.Gpus.Count > 0) Gpus = other.Gpus;
        if (other.Actualizaciones.Count > 0) Actualizaciones = other.Actualizaciones;
        if (other.DriversDisponibles.Count > 0) DriversDisponibles = other.DriversDisponibles;
        if (other.Almacenamiento.Count > 0) Almacenamiento = other.Almacenamiento;
        if (other.Arranque.Count > 0) Arranque = other.Arranque;
        if (other.Servicios.Count > 0) Servicios = other.Servicios;
        if (other.Programas.Count > 0) Programas = other.Programas;
        if (other.RedesCercanas.Count > 0) RedesCercanas = other.RedesCercanas;

        foreach (var hallazgo in other.Hallazgos)
        {
            if (!ContainsFinding(hallazgo))
                Hallazgos.Add(hallazgo);
        }

        ActualizarRecomendaciones();
    }

    public static IReadOnlyDictionary<string, string> NombresModulos { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["red"] = "Red y latencia", ["rendimiento"] = "Rendimiento", ["termicas"] = "Térmicas y energía",
        ["almacenamiento"] = "Almacenamiento", ["estabilidad"] = "Estabilidad", ["seguridad"] = "Seguridad",
        ["drivers"] = "Drivers", ["actualizaciones"] = "Actualizaciones", ["limpieza"] = "Limpieza",
        ["arranque"] = "Arranque y software"
    };

    /// <summary>Un módulo terminado sustituye también sus resultados vacíos; no deja datos obsoletos.</summary>
    public void ReplaceModuleFrom(DiagnosticReport other, string module)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (other.Sistema.Count > 0)
        {
            Hallazgos.RemoveAll(f => f.Modulo == "equipo");
            Equipo = other.Equipo; Sistema = other.Sistema; Discos = other.Discos; Memoria = other.Memoria;
        }
        Hallazgos.RemoveAll(f => string.Equals(f.Modulo, module, StringComparison.OrdinalIgnoreCase));
        switch (module)
        {
            case "red": WiFi = other.WiFi; Red = other.Red; RedesCercanas = other.RedesCercanas;
                Traceroute = other.Traceroute; TracerouteDestino = other.TracerouteDestino; break;
            case "rendimiento": RendimientoResumen = other.RendimientoResumen; TopCpu = other.TopCpu; TopRam = other.TopRam; break;
            case "termicas": Termicas = other.Termicas; Bateria = other.Bateria; Gpus = other.Gpus; break;
            case "estabilidad": EventosResumen = other.EventosResumen; EventosDetalle = other.EventosDetalle;
                Whea = other.Whea; Minidumps = other.Minidumps; break;
            case "almacenamiento": Almacenamiento = other.Almacenamiento; break;
            case "seguridad": Seguridad = other.Seguridad; break;
            case "drivers": Drivers = other.Drivers; DriversDisponibles = other.DriversDisponibles; break;
            case "actualizaciones": Actualizaciones = other.Actualizaciones; break;
            case "arranque": Arranque = other.Arranque; Servicios = other.Servicios; Programas = other.Programas; break;
            case "limpieza": Limpieza = other.Limpieza; EspacioLiberado = other.EspacioLiberado; break;
        }
        foreach (var finding in other.Hallazgos)
        {
            if (string.IsNullOrWhiteSpace(finding.Modulo)) finding.Modulo = module;
            if (!ContainsFinding(finding)) Hallazgos.Add(finding);
        }
        if (NombresModulos.ContainsKey(module)) ModulosCompletados[module] = DateTime.Now;
        ActualizarRecomendaciones();
    }

    /// <summary>
    /// Copia para archivar. Conserva los datos del reporte fusionado, pero declara como cobertura solo los módulos
    /// medidos en la corrida que se archiva: ModulosCompletados acumula módulos de corridas anteriores, y sin esto
    /// un «Red» suelto hecho después de un diagnóstico completo quedaría etiquetado como completo.
    /// </summary>
    public DiagnosticReport ParaArchivo(IEnumerable<string> modulosDeLaCorrida)
    {
        var medidos = new HashSet<string>(modulosDeLaCorrida ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        var copia = (DiagnosticReport)MemberwiseClone();
        copia.ModulosCompletados = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        foreach (var (modulo, fecha) in ModulosCompletados)
            if (medidos.Contains(modulo)) copia.ModulosCompletados[modulo] = fecha;
        return copia;
    }

    void IJsonOnDeserialized.OnDeserialized()
    {
        // Un archivo antiguo o un JSON con null no debe tumbar la vista de historial.
        Sistema ??= new(); Discos ??= new(); Memoria ??= new(); WiFi ??= new(); Red ??= new();
        RendimientoResumen ??= new(); TopCpu ??= new(); TopRam ??= new(); Termicas ??= new(); Bateria ??= new();
        EventosResumen ??= new(); EventosDetalle ??= new(); Whea ??= new(); Minidumps ??= new(); Traceroute ??= new();
        Limpieza ??= new(); Drivers ??= new(); Seguridad ??= new(); Gpus ??= new(); Actualizaciones ??= new();
        DriversDisponibles ??= new(); Almacenamiento ??= new(); Arranque ??= new(); Servicios ??= new();
        Programas ??= new(); RedesCercanas ??= new(); Recomendaciones ??= new(); ModulosCompletados ??= new();
        ModulosColgados ??= new(); DuracionesModulo ??= new(); CosteMedicion ??= "";
        Hallazgos.RemoveAll(f => f == null);
        foreach (var finding in Hallazgos)
        {
            finding.Area ??= ""; finding.Message ??= ""; finding.Action ??= "";
            finding.Modulo ??= ""; finding.AccionId ??= "";
            if (!Enum.IsDefined(finding.Severity)) finding.Severity = Severity.Warn;
        }
        // null dentro de una lista JSON tampoco debe derribar las reglas/plantillas.
        Sistema.RemoveAll(x => x == null); Discos.RemoveAll(x => x == null); Memoria.RemoveAll(x => x == null);
        WiFi.RemoveAll(x => x == null); Red.RemoveAll(x => x == null); RendimientoResumen.RemoveAll(x => x == null);
        TopCpu.RemoveAll(x => x == null); TopRam.RemoveAll(x => x == null); Termicas.RemoveAll(x => x == null);
        Bateria.RemoveAll(x => x == null); EventosResumen.RemoveAll(x => x == null); EventosDetalle.RemoveAll(x => x == null);
        Whea.RemoveAll(x => x == null); Minidumps.RemoveAll(x => x == null); Traceroute.RemoveAll(x => x == null);
        Limpieza.RemoveAll(x => x == null); Drivers.RemoveAll(x => x == null); Seguridad.RemoveAll(x => x == null);
        Gpus.RemoveAll(x => x == null); Actualizaciones.RemoveAll(x => x == null); DriversDisponibles.RemoveAll(x => x == null);
        Almacenamiento.RemoveAll(x => x == null); Arranque.RemoveAll(x => x == null); Servicios.RemoveAll(x => x == null);
        Programas.RemoveAll(x => x == null); RedesCercanas.RemoveAll(x => x == null);
        ActualizarRecomendaciones();
    }
}
