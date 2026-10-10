using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SysDiag.Core.Windows;

/// <summary>De dónde salió un cambio. Define cómo se deshace y qué se le puede prometer al usuario.</summary>
public enum OrigenCambio
{
    Ajustes,
    Optimizacion,
    Limpieza,
    Arranque,
    PuntoRestauracion,
    Red,
    Otro
}

/// <summary>
/// Un cambio que SysDiag aplicó, con lo necesario para deshacerlo.
/// </summary>
public sealed class CambioAplicado
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTime Fecha { get; set; } = DateTime.Now;
    public OrigenCambio Origen { get; set; } = OrigenCambio.Otro;
    public string Titulo { get; set; } = "";
    public string Detalle { get; set; } = "";

    /// <summary>Con qué vuelve a encontrarse el cambio: id del ajuste, ruta del respaldo…</summary>
    public string Referencia { get; set; } = "";

    /// <summary>
    /// Si se puede deshacer. `false` no es un defecto: reiniciar la pila TCP/IP
    /// no tiene vuelta atrás, y decirlo por escrito antes de aplicarlo es
    /// exactamente lo que separa esto de una herramienta que hace daño.
    /// </summary>
    public bool Reversible { get; set; } = true;

    /// <summary>Por qué no se puede deshacer, o advertencia sobre el alcance de la reversión.</summary>
    public string Nota { get; set; } = "";

    public bool Deshecho { get; set; }
    public DateTime? FechaDeshecho { get; set; }
    public string ResultadoDeshacer { get; set; } = "";

    public string FechaTexto => Fecha.ToString("dd-MM HH:mm", CultureInfo.CurrentCulture);

    public string OrigenTexto => Origen switch
    {
        OrigenCambio.Ajustes => "Ajustes de Windows",
        OrigenCambio.Optimizacion => "Optimización",
        OrigenCambio.Limpieza => "Limpieza",
        OrigenCambio.Arranque => "Arranque y software",
        OrigenCambio.PuntoRestauracion => "Punto de restauración",
        OrigenCambio.Red => "Red",
        _ => "Otro"
    };

    public bool Pendiente => !Deshecho;
    public bool SePuedeDeshacer => Pendiente && Reversible;
}

/// <summary>
/// Registro de lo que SysDiag cambió en el equipo, con deshacer por paso.
///
/// Existía ya un respaldo por módulo —el de los ajustes guarda el valor
/// anterior, el de optimizaciones guarda el estado previo—, pero cada uno
/// vivía en su propia ventana y ninguno respondía la pregunta que se hace
/// quien acaba de tocar algo: «qué hice, en qué orden, y cómo vuelvo atrás
/// solo de este paso».
///
/// Este registro no guarda cómo revertir: cada módulo ya sabe hacerlo, y
/// duplicarlo aquí sería mantener dos reversions que pueden divergir. Guarda
/// qué se hizo y a quién hay que preguntarle para deshacerlo.
///
/// Lo que no se puede revertir se registra igual, marcado. Borrar archivos
/// temporales no tiene vuelta atrás, y un registro que solo anotara lo
/// reversible mentiría por omisión justo en los casos que importan.
/// </summary>
public static class ActionLog
{
    private static readonly object Bloqueo = new();
    private static List<CambioAplicado> _cambios;
    private const int MaximoEntradas = 500;

    public static string RutaArchivo => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SysDiag", "registro-cambios.json");

    /// <summary>
    /// Anota un cambio. Se llama DESPUÉS de aplicarlo y con el resultado ya
    /// conocido: un registro que anota lo que se intentó y no lo que pasó es
    /// peor que no tener registro, porque autoriza a creer que se aplicó.
    /// </summary>
    public static void Registrar(OrigenCambio origen, string titulo, string detalle = "",
        string referencia = "", bool reversible = true, string nota = "")
    {
        var cambio = new CambioAplicado
        {
            Origen = origen,
            Titulo = titulo,
            Detalle = detalle,
            Referencia = referencia,
            Reversible = reversible,
            Nota = nota
        };

        lock (Bloqueo)
        {
            var lista = Cargar();
            // Los no reversibles van primero en el archivo, no por orden de
            // lectura sino porque al recortar por tamaño lo que se pierde es
            // lo más antiguo y lo más antiguo es, con suerte, lo reversible.
            lista.Add(cambio);
            if (lista.Count > MaximoEntradas)
                lista = lista.OrderBy(c => c.Reversible).ThenByDescending(c => c.Fecha).Take(MaximoEntradas).ToList();
            Guardar(lista);
        }

        AppLog.Write($"Cambio registrado: {titulo}{(reversible ? "" : " (no reversible)")}", "STEP");
    }

    public static IReadOnlyList<CambioAplicado> Todos()
    {
        lock (Bloqueo) return Cargar().OrderByDescending(c => c.Fecha).ToList();
    }

    public static IReadOnlyList<CambioAplicado> Pendientes()
    {
        lock (Bloqueo) return Cargar().Where(c => !c.Deshecho).OrderByDescending(c => c.Fecha).ToList();
    }

    /// <summary>
    /// Deshace un cambio por su id, delegando en el módulo que lo aplicó.
    /// Devuelve el resumen que hay que mostrarle al usuario.
    /// </summary>
    public static string Deshacer(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Falta el identificador del cambio.", nameof(id));

        CambioAplicado cambio;
        lock (Bloqueo) cambio = Cargar().FirstOrDefault(c => c.Id == id);
        if (cambio == null) throw new InvalidOperationException("Ese cambio ya no está en el registro.");
        if (cambio.Deshecho) return "Este cambio ya estaba deshecho.";
        if (!cambio.Reversible) throw new InvalidOperationException(
            $"«{cambio.Titulo}» no se puede deshacer: {cambio.Nota}");

        string resultado;
        try
        {
            resultado = cambio.Origen switch
            {
                OrigenCambio.Ajustes => TweakModule.Revertir(cambio.Referencia),
                OrigenCambio.Optimizacion => OptimizeModule.Restore(),
                _ => throw new InvalidOperationException(
                    $"SysDiag no sabe cómo deshacer «{cambio.Titulo}» automáticamente. {cambio.Nota}")
            };
        }
        catch (Exception ex)
        {
            // No se marca como deshecho si la reversión falló: marcarlo sería
            // borrar la única pista de que el equipo sigue modificado.
            AppLog.Write($"No se pudo deshacer «{cambio.Titulo}»: {ex.Message}", "ERROR");
            throw;
        }

        lock (Bloqueo)
        {
            var lista = Cargar();
            var entrada = lista.FirstOrDefault(c => c.Id == id);
            if (entrada != null)
            {
                entrada.Deshecho = true;
                entrada.FechaDeshecho = DateTime.Now;
                entrada.ResultadoDeshacer = resultado;
                Guardar(lista);
            }
        }
        return resultado;
    }

    /// <summary>
    /// Marca un cambio como deshecho SIN aplicar ninguna reversión. Existe
    /// porque la reversión también se puede ejecutar desde fuera del historial
    /// («Restaurar estado», o revertir un ajuste desde su propia ventana): si
    /// esas entradas quedaran pendientes, el botón «Deshacer» del historial
    /// ofrecería revertir otra vez algo ya revertido.
    /// </summary>
    public static void MarcarDeshecho(string id, string resultado = "Deshecho por otra vía.")
    {
        lock (Bloqueo)
        {
            var lista = Cargar();
            var entrada = lista.FirstOrDefault(c => c.Id == id);
            if (entrada == null || entrada.Deshecho) return;
            entrada.Deshecho = true;
            entrada.FechaDeshecho = DateTime.Now;
            entrada.ResultadoDeshacer = resultado;
            Guardar(lista);
        }
    }

    /// <summary>Marca como deshechas todas las entradas de un origen que siguen pendientes.</summary>
    public static int MarcarDeshechos(OrigenCambio origen, string resultado = "Deshecho por otra vía.")
    {
        int cuenta = 0;
        foreach (var entrada in Pendientes().Where(c => c.Origen == origen).ToList())
        {
            MarcarDeshecho(entrada.Id, resultado);
            cuenta++;
        }
        return cuenta;
    }

    /// <summary>Vacía el historial. No toca el equipo: solo deja de recordar.</summary>
    public static void Limpiar()
    {
        lock (Bloqueo) Guardar(new List<CambioAplicado>());
        AppLog.Write("Historial de cambios vaciado. Los cambios aplicados siguen aplicados: esto no revierte nada.", "WARN");
    }

    // ---- Persistencia ------------------------------------------------------

    private static List<CambioAplicado> Cargar()
    {
        if (_cambios != null) return _cambios;
        try
        {
            _cambios = File.Exists(RutaArchivo)
                ? JsonSerializer.Deserialize<List<CambioAplicado>>(File.ReadAllText(RutaArchivo)) ?? new List<CambioAplicado>()
                : new List<CambioAplicado>();
            _cambios.RemoveAll(c => c == null);
            foreach (var c in _cambios)
            {
                c.Titulo ??= ""; c.Detalle ??= ""; c.Referencia ??= ""; c.Nota ??= ""; c.ResultadoDeshacer ??= "";
                if (string.IsNullOrEmpty(c.Id)) c.Id = Guid.NewGuid().ToString("N");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // Un historial ilegible no puede impedir aplicar ni deshacer: se
            // empieza de cero y se dice, porque perder el registro de un
            // cambio irreversible es perder información del equipo.
            AppLog.Write($"Historial de cambios ilegible ({ex.Message}); se empieza uno nuevo.", "WARN");
            _cambios = new List<CambioAplicado>();
        }
        return _cambios;
    }

    private static void Guardar(List<CambioAplicado> lista)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(RutaArchivo));
            AtomicFile.WriteAllText(RutaArchivo,
                JsonSerializer.Serialize(lista, new JsonSerializerOptions { WriteIndented = true }));
            _cambios = lista;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Write($"No se pudo guardar el historial de cambios ({ex.Message}).", "WARN");
        }
    }
}
