using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using Microsoft.Win32;

namespace SysDiag.Core.Windows;

/// <summary>
/// Aplica y revierte los ajustes de <see cref="TweakCatalog"/>. Garantías:
///
///  1. Nunca escribe sin guardar antes el valor anterior (respaldo en
///     LocalAppData, con la misma filosofía que las optimizaciones).
///  2. Todo ajuste es reversible uno a uno o todos juntos.
///  3. Si un ajuste requiere admin y no lo hay, no se aplica ninguno:
///     primero se avisa, nunca se queda a medias.
/// </summary>
/// <summary>
/// Una línea del ensayo: qué se tocaría, de qué valor a cuál, sin tocar nada.
/// </summary>
public sealed class LineaEnsayo
{
    public string Ajuste { get; init; } = "";
    public string Ruta { get; init; } = "";
    public string Nombre { get; init; } = "";
    public string Actual { get; init; } = "(sin valor)";
    public string Objetivo { get; init; } = "";
    public bool YaAplicado { get; init; }
    public bool RequiereAdmin { get; init; }
    public bool FaltanPermisos { get; init; }

    /// <summary>Frase de una línea para el ensayo y para el informe de soporte.</summary>
    public string Texto => YaAplicado
        ? $"{Ajuste} · ya está aplicado"
        : $"{Ajuste} · {Ruta}\\{Nombre}: {Actual} → {Objetivo}" +
          (FaltanPermisos ? " (necesita administrador, que ahora mismo no tiene)" : "");
}

/// <summary>
/// Resultado de aplicar ajustes, con cada valor releído.
///
/// Separar «ya estaba así» de «se escribió y no quedó» es el punto de esta
/// clase: los dos dejan el sistema igual, pero el primero es una respuesta y el
/// segundo es un problema. Mezclarlos en un único contador de «aplicados»
/// hacía que el segundo pasara por éxito.
/// </summary>
public sealed class ResultadoAplicacion
{
    /// <summary>
    /// Ajustes que sí quedaron aplicados, con su id. Sin el id, quien quiera
    /// deshacer tendría que volver a adivinar a qué entrada del catálogo
    /// corresponde cada línea.
    /// </summary>
    public List<(string Id, string Titulo)> AjustesAplicados { get; } = new();

    public List<string> Aplicados { get; } = new();
    public List<string> SinCambio { get; } = new();
    public List<string> NoAplicados { get; } = new();

    public int Total => Aplicados.Count + SinCambio.Count + NoAplicados.Count;
    public bool HayFallos => NoAplicados.Count > 0;

    public string Resumen()
    {
        var partes = new List<string>();
        if (Aplicados.Count > 0) partes.Add($"{Aplicados.Count} aplicado(s) y comprobado(s)");
        if (SinCambio.Count > 0) partes.Add($"{SinCambio.Count} ya estaban así");
        if (NoAplicados.Count > 0) partes.Add($"{NoAplicados.Count} NO se aplicaron: se escribieron y Windows no los conservó");
        return string.Join(" · ", partes);
    }

    /// <summary>
    /// Texto completo para el diálogo y para el informe de soporte. Los que no
    /// se aplicaron van primero y con su motivo: son los únicos que exigen una
    /// acción, y al final de una lista de veinte líneas nadie los ve.
    /// </summary>
    public string Detalle()
    {
        var lineas = new List<string>();
        if (NoAplicados.Count > 0)
        {
            lineas.Add("No se aplicaron:");
            foreach (var linea in NoAplicados) lineas.Add("  · " + linea);
        }
        if (Aplicados.Count > 0)
        {
            if (lineas.Count > 0) lineas.Add("");
            lineas.Add("Aplicados y comprobados:");
            foreach (var linea in Aplicados) lineas.Add("  · " + linea);
        }
        if (SinCambio.Count > 0)
        {
            if (lineas.Count > 0) lineas.Add("");
            lineas.Add("Ya tenían ese valor:");
            foreach (var linea in SinCambio) lineas.Add("  · " + linea);
        }
        return string.Join(Environment.NewLine, lineas);
    }
}

public static class TweakModule
{
    private class EntradaRespaldo
    {
        public string Id { get; set; }
        public string Hive { get; set; }
        public string Ruta { get; set; }
        public string Nombre { get; set; }
        public string Tipo { get; set; }
        public string Valor { get; set; }
        public bool Existia { get; set; }
    }

    private static readonly object Bloqueo = new();
    private static List<EntradaRespaldo> _respaldo;

    public static IReadOnlyList<TweakAjuste> Catalogo { get; } = TweakCatalog.Crear();

    public static string RutaRespaldo => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SysDiag", "ajustes-respaldo.json");

    // ---- Estado ------------------------------------------------------------

    /// <summary>True si todos los valores del ajuste están en su estado activo.</summary>
    public static bool EstaAplicado(TweakAjuste ajuste)
    {
        foreach (var reg in ajuste.Registros)
        {
            if (!Coincide(LeerValor(reg), reg.Valor)) return false;
        }
        return ajuste.Registros.Length > 0;
    }

    /// <summary>Ids de los ajustes del catálogo cuyo estado actual es «aplicado».</summary>
    public static List<string> Aplicados() =>
        Catalogo.Where(EstaAplicado).Select(a => a.Id).ToList();

    /// <summary>Ids que se tocaron alguna vez desde SysDiag y aún no se revirtieron.</summary>
    public static List<string> ConRespaldo()
    {
        lock (Bloqueo) return CargarRespaldo().Select(e => e.Id).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    // ---- Aplicar -----------------------------------------------------------

    /// <summary>
    /// Ensaya sin escribir: recorre la selección y dice exactamente qué valor
    /// cambiaría en cada clave.
    ///
    /// Es la diferencia entre una herramienta que se prueba y una que da miedo.
    /// Antes, la única forma de saber qué iba a tocar SysDiag era aplicarlo y
    /// mirar el registro después; ahora se puede leer el plan —incluido qué
    /// ajustes ya están aplicados y cuáles fallarían por falta de permisos—
    /// antes de que exista algo que deshacer.
    /// </summary>
    public static List<LineaEnsayo> Ensayar(IEnumerable<TweakAjuste> seleccion)
    {
        var lineas = new List<LineaEnsayo>();
        foreach (var ajuste in seleccion ?? Enumerable.Empty<TweakAjuste>())
        {
            bool faltan = ajuste.RequiereAdmin && !AppEnv.IsAdmin;
            foreach (var reg in ajuste.Registros)
            {
                object actual = LeerValor(reg);
                bool ya = ChangeVerifier.Coincide(actual, reg.Valor);
                lineas.Add(new LineaEnsayo
                {
                    Ajuste = ajuste.Titulo,
                    Ruta = reg.Ruta,
                    Nombre = reg.Nombre,
                    Actual = actual?.ToString() ?? "(sin valor)",
                    Objetivo = reg.Valor,
                    YaAplicado = ya,
                    RequiereAdmin = ajuste.RequiereAdmin,
                    FaltanPermisos = faltan
                });
            }
        }
        return lineas;
    }

    public static ResultadoAplicacion Aplicar(IEnumerable<TweakAjuste> seleccion, CancellationToken token = default)
    {
        var lista = seleccion?.ToList() ?? throw new ArgumentNullException(nameof(seleccion));
        if (lista.Count == 0) throw new InvalidOperationException("No hay ajustes seleccionados para aplicar.");
        if (lista.Any(a => a.RequiereAdmin) && !AppEnv.IsAdmin)
            throw new InvalidOperationException("Algunos ajustes seleccionados necesitan permisos de administrador. Reinicia SysDiag como administrador para aplicarlos.");

        var resultado = new ResultadoAplicacion();
        AppLog.Write($"Aplicando {lista.Count} ajuste(s) de Windows", "STEP");
        foreach (var ajuste in lista)
        {
            token.ThrowIfCancellationRequested();
            foreach (var reg in ajuste.Registros)
            {
                object actual = LeerValor(reg);
                if (ChangeVerifier.Coincide(actual, reg.Valor))
                {
                    resultado.SinCambio.Add($"{ajuste.Titulo}: ya valía {reg.Valor}");
                    continue; // ya estaba: no tocar ni respaldar
                }

                // El respaldo se guarda ANTES de escribir: si esta línea no se
                // guardara y el disco fallara a mitad de escritura, el ajuste
                // quedaría sin forma de deshacer.
                GuardarRespaldo(ajuste.Id, reg, actual);
                EscribirValor(reg, reg.Valor);

                // Se relee. `EscribirValor` no devuelve nada: si algo repone el
                // valor, la escritura devuelve sin error igual. Decir «aplicado»
                // sin esta comprobación es afirmar algo que no se verificó.
                var verificacion = ChangeVerifier.Verificar($"{ajuste.Titulo}", () => LeerValor(reg), reg.Valor, ajuste.RequiereAdmin);
                if (verificacion.Aplicado)
                {
                    resultado.Aplicados.Add($"{ajuste.Titulo}: {verificacion.Leido}");
                }
                else
                {
                    resultado.NoAplicados.Add(verificacion.ToString());
                    AppLog.Write(verificacion.ToString(), "WARN");
                }
            }
            if (!resultado.NoAplicados.Any(l => l.StartsWith(ajuste.Titulo, StringComparison.Ordinal)))
            {
                resultado.AjustesAplicados.Add((ajuste.Id, ajuste.Titulo));
                AppLog.Write($"Ajuste «{ajuste.Titulo}» aplicado.{(string.IsNullOrEmpty(ajuste.NotaAplicacion) ? "" : " " + ajuste.NotaAplicacion)}", "OK");
            }
        }

        AppLog.Write($"Ajustes: {resultado.Resumen()}", resultado.HayFallos ? "WARN" : "OK");
        return resultado;
    }

    // ---- Revertir ----------------------------------------------------------

    /// <summary>Revierte un ajuste concreto. Devuelve un resumen para el usuario.</summary>
    public static string Revertir(string id)
    {
        if (string.IsNullOrEmpty(id)) throw new ArgumentException(nameof(id));
        var ajuste = Catalogo.FirstOrDefault(a => string.Equals(a.Id, id, StringComparison.OrdinalIgnoreCase));
        int restaurados = 0;

        lock (Bloqueo)
        {
            var respaldo = CargarRespaldo();
            var entradas = respaldo.Where(e => string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase)).ToList();
            foreach (var entrada in entradas)
            {
                RestaurarEntrada(entrada);
                respaldo.Remove(entrada);
                restaurados++;
            }
            GuardarRespaldoInterno(respaldo);
        }

        // Las claves que el ajuste creó se eliminan al final: es el estado
        // «inactivo» de ajustes como el menú contextual clásico.
        foreach (var ruta in ajuste?.BorrarAlRevertir ?? Array.Empty<string>())
        {
            try
            {
                using var baseKey = AbrirBase(RegistryHive.CurrentUser, writable: true);
                baseKey.DeleteSubKeyTree(ruta, throwOnMissingSubKey: false);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or ArgumentException)
            {
                AppLog.Write($"No se pudo eliminar la clave {ruta}: {ex.Message}", "WARN");
            }
        }

        string resumen = restaurados > 0
            ? $"Se restauró «{ajuste?.Titulo ?? id}» a su estado anterior ({restaurados} valor(es))."
            : $"«{ajuste?.Titulo ?? id}» no tenía cambios guardados desde SysDiag; se comprobó su estado actual.";
        AppLog.Write(resumen, "OK");
        return resumen;
    }

    /// <summary>Revierte todos los ajustes que SysDiag aplicó alguna vez.</summary>
    public static string RevertirTodo()
    {
        List<string> ids;
        lock (Bloqueo) ids = CargarRespaldo().Select(e => e.Id).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (ids.Count == 0) return "No hay ajustes aplicados desde SysDiag que revertir.";

        var resumenes = new List<string>();
        foreach (string id in ids) resumenes.Add(Revertir(id));
        return string.Join(Environment.NewLine, resumenes);
    }

    // ---- Registro: lectura/escritura ---------------------------------------

    private static RegistryKey AbrirBase(RegistryHive hive, bool writable) =>
        RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);

    private static object LeerValor(RegValor reg)
    {
        using var baseKey = AbrirBase(reg.Hive, writable: false);
        using var clave = baseKey.OpenSubKey(reg.Ruta, writable: false);
        return clave?.GetValue(reg.Nombre, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
    }

    private static void EscribirValor(RegValor reg, string valorTexto)
    {
        using var baseKey = AbrirBase(reg.Hive, writable: true);
        using var clave = baseKey.CreateSubKey(reg.Ruta, writable: true)
            ?? throw new IOException($"No se pudo abrir o crear la clave {reg.Ruta}.");
        clave.SetValue(reg.Nombre, DesdeTexto(reg.Tipo, valorTexto), reg.Tipo);
    }

    private static void RestaurarEntrada(EntradaRespaldo entrada)
    {
        var hive = (RegistryHive)Enum.Parse(typeof(RegistryHive), entrada.Hive);
        using var baseKey = AbrirBase(hive, writable: true);

        if (entrada.Existia)
        {
            using var clave = baseKey.CreateSubKey(entrada.Ruta, writable: true)
                ?? throw new IOException($"No se pudo abrir o crear la clave {entrada.Ruta}.");
            var tipo = (RegistryValueKind)Enum.Parse(typeof(RegistryValueKind), entrada.Tipo);
            clave.SetValue(entrada.Nombre, DesdeTexto(tipo, entrada.Valor), tipo);
        }
        else if (entrada.Nombre != "")
        {
            using var clave = baseKey.OpenSubKey(entrada.Ruta, writable: true);
            clave?.DeleteValue(entrada.Nombre, throwOnMissingValue: false);
        }
        // Si no existía y es el valor predeterminado (Nombre == ""), no se
        // puede «borrar»: queda cubierto por BorrarAlRevertir del ajuste.
    }

    private static bool Coincide(object actual, string objetivo) => ChangeVerifier.Coincide(actual, objetivo);

    private static object DesdeTexto(RegistryValueKind tipo, string valor) => tipo switch
    {
        RegistryValueKind.DWord => (object)int.Parse(valor, CultureInfo.InvariantCulture),
        RegistryValueKind.QWord => long.Parse(valor, CultureInfo.InvariantCulture),
        RegistryValueKind.Binary => Convert.FromBase64String(valor),
        RegistryValueKind.MultiString => valor.Split('\n'),
        _ => valor
    };

    private static string HaciaTexto(RegistryValueKind tipo, object valor) => tipo switch
    {
        RegistryValueKind.DWord => Convert.ToInt32(valor, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
        RegistryValueKind.QWord => Convert.ToInt64(valor, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
        RegistryValueKind.Binary => Convert.ToBase64String((byte[])valor),
        RegistryValueKind.MultiString => string.Join("\n", (string[])valor),
        _ => valor?.ToString() ?? ""
    };

    // ---- Resaldo ------------------------------------------------------------

    private static List<EntradaRespaldo> CargarRespaldo()
    {
        if (_respaldo != null) return _respaldo;
        try
        {
            _respaldo = File.Exists(RutaRespaldo)
                ? JsonSerializer.Deserialize<List<EntradaRespaldo>>(File.ReadAllText(RutaRespaldo)) ?? new List<EntradaRespaldo>()
                : new List<EntradaRespaldo>();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // Un respaldo ilegible no debe impedir aplicar: se empieza uno nuevo.
            // El riesgo es perder la reversión de lo aplicado antes; se avisa.
            AppLog.Write($"Respaldo de ajustes ilegible ({ex.Message}); se empieza uno nuevo.", "WARN");
            _respaldo = new List<EntradaRespaldo>();
        }
        return _respaldo;
    }

    private static void GuardarRespaldo(string id, RegValor reg, object valorAnterior)
    {
        lock (Bloqueo)
        {
            var respaldo = CargarRespaldo();
            respaldo.Add(new EntradaRespaldo
            {
                Id = id,
                Hive = reg.Hive.ToString(),
                Ruta = reg.Ruta,
                Nombre = reg.Nombre,
                Tipo = (valorAnterior != null ? LeerTipo(reg) : reg.Tipo).ToString(),
                Valor = valorAnterior != null ? HaciaTexto(LeerTipo(reg), valorAnterior) : "",
                Existia = valorAnterior != null
            });
            GuardarRespaldoInterno(respaldo);
        }
    }

    private static RegistryValueKind LeerTipo(RegValor reg)
    {
        try
        {
            using var baseKey = AbrirBase(reg.Hive, writable: false);
            using var clave = baseKey.OpenSubKey(reg.Ruta, writable: false);
            return clave?.GetValueKind(reg.Nombre) ?? reg.Tipo;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return reg.Tipo;
        }
    }

    private static void GuardarRespaldoInterno(List<EntradaRespaldo> respaldo)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(RutaRespaldo));
        File.WriteAllText(RutaRespaldo, JsonSerializer.Serialize(respaldo, new JsonSerializerOptions { WriteIndented = true }));
        _respaldo = respaldo;
    }
}
