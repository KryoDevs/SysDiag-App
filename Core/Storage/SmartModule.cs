using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using SysDiag.Models;

namespace SysDiag.Core.Storage;

/// <summary>Un atributo tal cual sale del bloque SMART del disco.</summary>
public sealed class SmartAtributoCrudo
{
    public int Id { get; init; }
    public ushort Flags { get; init; }

    /// <summary>Valor normalizado, 1..100. Más alto es mejor, por convención.</summary>
    public byte Valor { get; init; }

    /// <summary>El peor valor normalizado que registró el disco.</summary>
    public byte Peor { get; init; }

    /// <summary>Valor crudo de 48 bits. Su significado depende del atributo y del fabricante.</summary>
    public long Crudo { get; init; }
}

/// <summary>
/// Lo que SysDiag concluye de un atributo.
///
/// `Interpretable` es la parte que importa: distingue «está bien» de «no
/// sabemos qué significa esto». Pintar de verde lo segundo es la forma más
/// fácil de que una herramienta de diagnóstico mienta sin mentir.
/// </summary>
public sealed class SmartLectura
{
    public Severity Estado { get; init; }
    public string Texto { get; init; } = "";
    public bool Interpretable { get; init; }
}

/// <summary>Un atributo listo para mostrarse.</summary>
public sealed class SmartAtributo
{
    /// <summary>Número de atributo SMART, o «—» cuando el dato viene de los contadores de fiabilidad.</summary>
    public string Codigo { get; init; } = "—";
    public string Nombre { get; init; } = "";
    public string Valor { get; init; } = "n/d";
    public string Umbral { get; init; } = "n/d";
    public string Crudo { get; init; } = "n/d";
    public string Lectura { get; init; } = "";
    public Severity Estado { get; init; } = Severity.Ok;
    public bool Interpretable { get; init; }
    public int Orden { get; init; }
}

/// <summary>Un disco con sus atributos y —cuando no se pudieron leer— el motivo.</summary>
public sealed class SmartDisco
{
    public string Nombre { get; init; } = "";
    public string Tipo { get; init; } = "desconocido";
    public string Firmware { get; init; } = "";

    /// <summary>De dónde salieron los datos: SMART en crudo o los contadores de fiabilidad de Windows.</summary>
    public string Fuente { get; init; } = "";

    public bool Disponible { get; init; }

    /// <summary>
    /// Por qué no hay datos. El doc «n/d» sin motivo deja al usuario con la
    /// duda de si el disco está sano o si no se pudo medir, que son cosas
    /// distintas y llevan a decisiones opuestas.
    /// </summary>
    public string Motivo { get; init; } = "";

    public List<SmartAtributo> Atributos { get; init; } = new();
    public string Resumen { get; init; } = "";

    public Severity Estado => Atributos.Count == 0
        ? Severity.Warn
        : (Severity)Atributos.Max(a => (int)a.Estado);
}

/// <summary>
/// Salud del disco atributo por atributo, en lugar del semáforo binario de WMI.
///
/// El estado binario dice «OK» hasta que dice «muerto», y entre medio no dice
/// nada. Los atributos que importan son los que avisan antes: sectores
/// reasignados, sectores pendientes, errores de la interfaz y vida útil
/// restante del SSD.
///
/// Dos decisiones que definen si esto sirve o si es ruido con colores:
///
///  1. **Se respeta el umbral del fabricante antes que el nuestro.** Cada
///     atributo trae su propio umbral; si el valor normalizado cae por debajo,
///     es el fabricante —no nosotros— quien dice que falló. Nuestra tabla solo
///     corre cuando el disco no trae umbral usable.
///  2. **Lo que no sabemos interpretar se muestra, pero no se califica.** El
///     crudo de «tasa de errores de lectura» depende de cada marca: compararlo
///     contra un número propio inventaría fallas que el disco no reportó.
/// </summary>
public static class SmartModule
{
    private const string StorageNs = @"root\Microsoft\Windows\Storage";
    private const string WmiNs = @"root\WMI";

    /// <summary>Entradas del bloque VendorSpecific: 30 como máximo, 12 bytes cada una.</summary>
    private const int EntradaBytes = 12;

    // ---- Parseo ------------------------------------------------------------

    /// <summary>
    /// Interpreta el bloque `VendorSpecific` de `MSStorageDriver_ATAPISmartData`.
    ///
    /// Formato: dos bytes de revisión y hasta 30 entradas de 12 bytes. Cada
    /// entrada es identificador, banderas (16 bits), valor normalizado, peor
    /// valor, seis bytes de valor crudo y uno reservado. Las entradas sin usar
    /// llegan con identificador 0 y se descartan.
    ///
    /// Es parseo, no depuración: no se necesita WinDbg ni un binario externo
    /// para leer esto.
    /// </summary>
    public static List<SmartAtributoCrudo> ParsearAtributos(byte[] bloque)
    {
        var lista = new List<SmartAtributoCrudo>();
        if (bloque == null || bloque.Length < 2 + EntradaBytes) return lista;

        for (int pos = 2; pos + EntradaBytes <= bloque.Length; pos += EntradaBytes)
        {
            int id = bloque[pos];
            if (id == 0) continue;

            ushort flags = (ushort)(bloque[pos + 1] | (bloque[pos + 2] << 8));
            byte valor = bloque[pos + 3];
            byte peor = bloque[pos + 4];

            // El crudo son seis bytes little-endian; se guarda completo porque
            // hay atributos (TBW, LBA escritos) que usan los 48 bits.
            long crudo = 0;
            for (int i = 5; i >= 0; i--) crudo = (crudo << 8) | bloque[pos + 5 + i];

            lista.Add(new SmartAtributoCrudo { Id = id, Flags = flags, Valor = valor, Peor = peor, Crudo = crudo });
        }
        return lista;
    }

    /// <summary>
    /// Interpreta `MSStorageDriver_FailurePredictThresholds`: una entrada de 12
    /// bytes por atributo, con el identificador en el primero y el umbral en el
    /// segundo. Es la tabla que el propio fabricante grabó en el disco.
    /// </summary>
    public static Dictionary<int, byte> ParsearUmbrales(byte[] bloque)
    {
        var mapa = new Dictionary<int, byte>();
        if (bloque == null || bloque.Length < 2 + EntradaBytes) return mapa;

        for (int pos = 2; pos + EntradaBytes <= bloque.Length; pos += EntradaBytes)
        {
            int id = bloque[pos];
            if (id == 0) continue;
            mapa[id] = bloque[pos + 1];
        }
        return mapa;
    }

    // ---- Interpretación ----------------------------------------------------

    /// <summary>
    /// Traduce un atributo a una conclusión. `umbral` es el que trae el disco;
    /// cuando no se conoce, se pasa `null` y rige la tabla propia.
    /// </summary>
    public static SmartLectura Interpretar(SmartAtributoCrudo a, byte? umbral)
    {
        // El umbral del propio disco va primero: no depende de nuestra tabla.
        // «Valor 0» no es un dato, es una entrada que el disco no llenó, y
        // compararla contra el umbral daría una falla que no existe.
        if (umbral.HasValue && umbral.Value > 0 && a.Valor > 0 && a.Valor <= umbral.Value)
            return new SmartLectura
            {
                Estado = Severity.Bad,
                Texto = $"El fabricante marca este atributo como fallido: valor {a.Valor} contra un umbral de {umbral.Value}.",
                Interpretable = true
            };

        switch (a.Id)
        {
            case 5:
                return a.Crudo == 0
                    ? Correcto("Sin sectores reasignados.")
                    : a.Crudo <= 10
                        ? Atencion($"{a.Crudo:N0} sectores reasignados. Un puñado no significa que el disco se esté muriendo: lo que importa es si el número sigue subiendo, y eso solo se ve volviendo a medir con el tiempo.")
                        : Critico($"{a.Crudo:N0} sectores reasignados. El disco ya tuvo que reemplazar muchas áreas: respalda y planifica el cambio.");

            case 9:
                {
                    long horas = a.Crudo & 0xFFFFFFFF;
                    if (horas > 200000) return SinInterpretar($"El crudo ({a.Crudo:N0}) no es una cantidad de horas reconocible.");
                    // La antigüedad no es una falla: un disco de cuatro años
                    // sano está sano. Se informa y no se enciende ninguna luz.
                    return Correcto($"{horas:N0} horas encendido ({horas / 8760.0:0.0} años).");
                }

            case 177:   // nivelación de desgaste (SSD)
            case 202:   // porcentaje de vida restante
            case 231:   // vida restante del SSD
                {
                    // En estos tres la convención es que el valor normalizado
                    // es el porcentaje restante. El crudo, en cambio, cambia
                    // de significado entre marcas, así que no se usa.
                    int resto = a.Valor;
                    if (resto <= 0) return SinInterpretar("El disco no reporta el valor normalizado de este atributo.");
                    if (resto <= 10) return Critico($"Queda el {resto} % de la vida útil de escritura. Respalda y reemplaza el disco.");
                    if (resto <= 25) return Atencion($"Queda el {resto} % de la vida útil de escritura. Empieza a planificar el reemplazo.");
                    return Correcto($"Queda el {resto} % de la vida útil de escritura.");
                }

            case 184:
                return a.Crudo == 0
                    ? Correcto("Sin errores de extremo a extremo.")
                    : Atencion($"{a.Crudo:N0} errores de extremo a extremo: el dato se corrompió entre la memoria y el plato o las celdas. Revisa cable y controladora antes de dar el disco por muerto.");

            case 187:
                return a.Crudo == 0
                    ? Correcto("Sin errores no corregibles reportados al sistema.")
                    : Critico($"{a.Crudo:N0} errores no corregibles. Hay datos que no se pudieron recuperar: respalda lo que se pueda y reemplaza la unidad.");

            case 188:
                return a.Crudo == 0
                    ? Correcto("Sin tiempos de espera de comando.")
                    : Atencion($"{a.Crudo:N0} tiempos de espera. Suele ser la controladora, el cable o la alimentación, no el disco.");

            case 194:
                {
                    // El byte bajo es la temperatura actual; algunos discos
                    // codifican mínimos y máximos en los bytes altos.
                    int temperatura = (int)(a.Crudo & 0xFF);
                    if (temperatura <= 0 || temperatura > 100)
                        return SinInterpretar($"El crudo ({a.Crudo:N0}) no es una temperatura reconocible; puede traer mínimos y máximos codificados por el fabricante.");
                    return temperatura >= 70
                        ? Atencion($"{temperatura} °C. Por encima de 70 °C el SSD reduce velocidad para protegerse.")
                        : Correcto($"{temperatura} °C.");
                }

            case 196:
                return a.Crudo == 0
                    ? Correcto("Sin eventos de reasignación.")
                    : Atencion($"{a.Crudo:N0} eventos de reasignación. Contrástalo con la cantidad de sectores reasignados (atributo 5) para saber si el problema crece o quedó atrás.");

            case 197:
                return a.Crudo == 0
                    ? Correcto("Sin sectores esperando reasignación.")
                    : a.Crudo <= 5
                        ? Atencion($"{a.Crudo:N0} sectores pendientes de reasignar. El disco todavía no pudo leerlos: respalda y vuelve a medir para ver si el número crece.")
                        : Critico($"{a.Crudo:N0} sectores pendientes de reasignar. El disco está perdiendo datos en este momento.");

            case 198:
                return a.Crudo == 0
                    ? Correcto("Sin sectores incorregibles.")
                    : Critico($"{a.Crudo:N0} sectores que el disco no pudo corregir. Hay datos perdidos: respalda lo que se pueda y reemplaza la unidad.");

            case 199:
                return a.Crudo == 0
                    ? Correcto("Sin errores de CRC en la interfaz.")
                    : Atencion($"{a.Crudo:N0} errores de CRC en UDMA. Casi siempre es el cable o el conector SATA, no el disco: cámbialo antes de dar la unidad por muerta.");

            // El crudo de estos tres lo define cada fabricante (cuenta de
            // errores sobre un total que nadie publica). Compararlo contra un
            // número nuestro inventaría fallas que el disco no reportó.
            case 1:
            case 7:
            case 200:
                return SinInterpretar($"Valor normalizado {a.Valor} contra un umbral de {TextoUmbral(umbral)}. El valor crudo de este atributo depende del fabricante y no se puede comparar entre marcas: por sí solo no enciende ninguna luz.");

            default:
                return SinInterpretar($"Valor normalizado {a.Valor}, peor registrado {a.Peor}, umbral {TextoUmbral(umbral)}. SysDiag no interpreta este atributo; se muestra porque el disco lo reporta.");
        }
    }

    private static string TextoUmbral(byte? umbral) =>
        umbral.HasValue ? umbral.Value.ToString(CultureInfo.InvariantCulture) : "n/d";

    private static SmartLectura Correcto(string texto) => new() { Estado = Severity.Ok, Texto = texto, Interpretable = true };
    private static SmartLectura Atencion(string texto) => new() { Estado = Severity.Warn, Texto = texto, Interpretable = true };
    private static SmartLectura Critico(string texto) => new() { Estado = Severity.Bad, Texto = texto, Interpretable = true };

    /// <summary>Se muestra, pero no se califica: «sin datos» no es «está bien».</summary>
    private static SmartLectura SinInterpretar(string texto) => new() { Estado = Severity.Ok, Texto = texto, Interpretable = false };

    // ---- Nombres -----------------------------------------------------------

    /// <summary>
    /// Nombres en español de los atributos que aparecen en la práctica. Los que
    /// no están en la tabla se muestran con su número: inventar un nombre para
    /// un atributo no documentado es peor que mostrar el número.
    /// </summary>
    public static string Nombre(int id) => id switch
    {
        1 => "Tasa de errores de lectura",
        3 => "Tiempo de puesta en marcha",
        4 => "Ciclos de arranque y parada",
        5 => "Sectores reasignados",
        7 => "Tasa de errores de búsqueda",
        9 => "Horas encendido",
        10 => "Reintentos de giro",
        12 => "Ciclos de encendido",
        170 => "Bloques reservados disponibles",
        171 => "Bloques de respaldo programados",
        173 => "Bloques desgastados",
        174 => "Bloques perdidos inesperadamente",
        175 => "Fallas de programación",
        177 => "Nivelación de desgaste",
        179 => "Bloques sobre-reservados usados",
        180 => "Bloques reservados usados",
        183 => "Interfaz SATA degradada",
        184 => "Error de extremo a extremo",
        187 => "Errores no corregibles",
        188 => "Tiempos de espera de comando",
        192 => "Apagados bruscos",
        193 => "Ciclos de carga y descarga",
        194 => "Temperatura",
        196 => "Eventos de reasignación",
        197 => "Sectores pendientes de reasignar",
        198 => "Sectores incorregibles",
        199 => "Errores de CRC en UDMA",
        200 => "Tasa de errores de escritura",
        202 => "Vida útil restante",
        231 => "Vida restante del SSD",
        232 => "Reserva de vida útil usada",
        233 => "Horas de escritura",
        241 => "Total escrito",
        242 => "Total leído",
        _ => $"Atributo {id}"
    };

    // ---- Lectura -----------------------------------------------------------

    /// <summary>
    /// Consulta los discos y sus atributos. Devuelve una entrada por disco
    /// conocido, y una sola entrada con el motivo cuando no se pudo leer nada:
    /// una lista vacía no distingue «no hay discos» de «no se pudo medir».
    /// </summary>
    public static List<SmartDisco> Consultar(CancellationToken token = default)
    {
        AppLog.Write("SMART por atributos", "STEP");

        var bloques = LeerBloques(token);
        var contadores = LeerContadores(token);

        var fisicos = Wmi.Query(
            "SELECT FriendlyName, DeviceId, MediaType, FirmwareVersion FROM MSFT_PhysicalDisk",
            StorageNs, token);

        var conocidos = new List<string>();
        var discos = new List<SmartDisco>();

        foreach (var d in fisicos)
        {
            string nombre = Wmi.Str(d, "FriendlyName");
            if (string.IsNullOrWhiteSpace(nombre)) continue;

            string id = Wmi.Str(d, "DeviceId");
            var bloque = Emparejar(bloques, nombre);

            var fila = new SmartDisco
            {
                Nombre = nombre,
                Tipo = Wmi.Num(d, "MediaType") switch { 3 => "HDD", 4 => "SSD", 5 => "SCM", _ => "desconocido" },
                Firmware = Wmi.Str(d, "FirmwareVersion")
            };

            discos.Add(bloque != null
                ? DesdeSmart(fila, bloque)
                : DesdeContadores(fila, contadores.ContainsKey(id) ? contadores[id] : null));
            if (bloque != null) conocidos.Add(bloque.Instancia);
        }

        // El bloque SMART puede existir aunque `MSFT_PhysicalDisk` no responda
        // (servicio de almacenamiento detenido, o sin permisos sobre esa clase).
        // Esos datos valen igual y se muestran con el nombre que trae la instancia.
        foreach (var bloque in bloques)
            if (!conocidos.Contains(bloque.Instancia))
                discos.Add(DesdeSmart(new SmartDisco { Nombre = LimpiarInstancia(bloque.Instancia), Tipo = "desconocido" }, bloque));

        if (discos.Count == 0)
            discos.Add(new SmartDisco
            {
                Nombre = "Sin discos con SMART legible",
                Disponible = false,
                Motivo = MotivoSinDatos()
            });

        foreach (var d in discos)
            AppLog.Write($"{d.Nombre,-38} {(d.Disponible ? d.Atributos.Count + " atributos · " + d.Fuente : "sin datos: " + d.Motivo)}");

        return discos;
    }

    private static SmartDisco DesdeSmart(SmartDisco base_, BloqueSmart bloque)
    {
        var umbrales = bloque.Umbrales ?? new Dictionary<int, byte>();

        var filas = new List<SmartAtributo>();
        foreach (var a in bloque.Atributos.OrderBy(x => x.Id))
        {
            umbrales.TryGetValue(a.Id, out byte umbral);
            var lectura = Interpretar(a, umbrales.ContainsKey(a.Id) ? umbral : (byte?)null);

            filas.Add(new SmartAtributo
            {
                Codigo = a.Id.ToString(CultureInfo.InvariantCulture),
                Nombre = Nombre(a.Id),
                Valor = a.Valor.ToString(CultureInfo.InvariantCulture),
                Umbral = umbrales.ContainsKey(a.Id) ? umbral.ToString(CultureInfo.InvariantCulture) : "n/d",
                Crudo = a.Crudo.ToString("N0", CultureInfo.CurrentCulture),
                Lectura = lectura.Texto,
                Estado = lectura.Estado,
                Interpretable = lectura.Interpretable,
                Orden = a.Id
            });
        }

        return new SmartDisco
        {
            Nombre = base_.Nombre,
            Tipo = base_.Tipo,
            Firmware = base_.Firmware,
            Disponible = filas.Count > 0,
            Fuente = "SMART en crudo (root\\WMI)",
            Motivo = filas.Count > 0 ? "" : "El disco respondió la consulta pero no devolvió ningún atributo.",
            Atributos = filas,
            Resumen = Resumir(filas)
        };
    }

    private static SmartDisco DesdeContadores(SmartDisco base_, ContadoresFiabilidad c)
    {
        if (c == null)
            return new SmartDisco
            {
                Nombre = base_.Nombre,
                Tipo = base_.Tipo,
                Firmware = base_.Firmware,
                Disponible = false,
                Motivo = MotivoSinDatos()
            };

        var filas = new List<SmartAtributo>();
        Agregar(filas, "—", "Horas encendido", c.Horas.HasValue
            ? $"{c.Horas:N0} h ({c.Horas / 8760.0:0.0} años)" : "n/d", Severity.Ok, true, 9);

        if (c.Desgaste.HasValue)
        {
            Severity estado = c.Desgaste >= 80 ? Severity.Bad : c.Desgaste >= 50 ? Severity.Warn : Severity.Ok;
            string texto = estado == Severity.Bad
                ? $"{c.Desgaste:0} % de desgaste de celdas. Queda el {100 - c.Desgaste:0} % de la vida útil: reemplaza el disco."
                : estado == Severity.Warn
                    ? $"{c.Desgaste:0} % de desgaste. Queda el {100 - c.Desgaste:0} % de la vida útil."
                    : $"{c.Desgaste:0} % de desgaste. Queda el {100 - c.Desgaste:0} % de la vida útil.";
            Agregar(filas, "—", "Desgaste de celdas (SSD)", texto, estado, true, 177);
        }

        if (c.Temperatura.HasValue)
        {
            Severity estado = c.Temperatura >= 70 ? Severity.Warn : Severity.Ok;
            Agregar(filas, "—", "Temperatura",
                estado == Severity.Warn
                    ? $"{c.Temperatura:0} °C. Por encima de 70 °C el SSD reduce velocidad para protegerse."
                    : $"{c.Temperatura:0} °C.", estado, true, 194);
        }

        if (c.Errores.HasValue)
            Agregar(filas, "—", "Errores no corregidos",
                c.Errores > 0
                    ? $"{c.Errores:0} errores de lectura o escritura que el disco no pudo corregir. Respalda y reemplaza."
                    : "Sin errores de lectura ni escritura sin corregir.",
                c.Errores > 0 ? Severity.Bad : Severity.Ok, true, 187);

        return new SmartDisco
        {
            Nombre = base_.Nombre,
            Tipo = base_.Tipo,
            Firmware = base_.Firmware,
            Disponible = filas.Count > 0,
            Fuente = "Contadores de fiabilidad de Windows",
            Motivo = filas.Count > 0
                ? "El bloque SMART en crudo no estuvo disponible; estos valores vienen del servicio de almacenamiento de Windows y cubren menos atributos."
                : "El servicio de almacenamiento respondió sin valores para este disco.",
            Atributos = filas,
            Resumen = Resumir(filas)
        };
    }

    private static void Agregar(List<SmartAtributo> filas, string codigo, string nombre, string lectura,
        Severity estado, bool interpretable, int orden) =>
        filas.Add(new SmartAtributo
        {
            Codigo = codigo,
            Nombre = nombre,
            Valor = "—",
            Umbral = "—",
            Crudo = "—",
            Lectura = lectura,
            Estado = estado,
            Interpretable = interpretable,
            Orden = orden
        });

    /// <summary>
    /// Una frase por disco con lo que decide una acción. Se arma acá y no en la
    /// vista porque depende de qué atributos vinieron, y eso cambia por disco.
    /// </summary>
    private static string Resumir(List<SmartAtributo> filas)
    {
        if (filas.Count == 0) return "";

        var criticos = filas.Where(f => f.Estado == Severity.Bad).ToList();
        var avisos = filas.Where(f => f.Estado == Severity.Warn).ToList();

        var partes = new List<string>();
        var vida = filas.FirstOrDefault(f => f.Orden is 177 or 202 or 231 && f.Interpretable);
        var horas = filas.FirstOrDefault(f => f.Orden == 9 && f.Interpretable);
        var temperatura = filas.FirstOrDefault(f => f.Orden == 194 && f.Interpretable);

        if (vida != null) partes.Add(vida.Lectura);
        if (horas != null) partes.Add(horas.Lectura);
        if (temperatura != null) partes.Add(temperatura.Lectura);

        if (criticos.Count > 0)
            partes.Insert(0, criticos.Count == 1
                ? $"1 atributo en estado crítico: {criticos[0].Nombre}."
                : $"{criticos.Count} atributos en estado crítico: {string.Join(", ", criticos.Select(c => c.Nombre))}.");

        if (avisos.Count > 0 && criticos.Count == 0)
            partes.Insert(0, avisos.Count == 1
                ? $"1 atributo en atención: {avisos[0].Nombre}."
                : $"{avisos.Count} atributos en atención: {string.Join(", ", avisos.Select(c => c.Nombre))}.");

        return string.Join(" ", partes);
    }

    // ---- WMI ---------------------------------------------------------------

    /// <summary>Un disco tal como lo devolvió WMI: atributos y sus umbrales, ya emparejados.</summary>
    private sealed class BloqueSmart
    {
        public string Instancia = "";
        public List<SmartAtributoCrudo> Atributos = new();
        public Dictionary<int, byte> Umbrales = new();
    }

    private sealed class ContadoresFiabilidad
    {
        public double? Horas;
        public double? Desgaste;
        public double? Temperatura;
        public double? Errores;
    }

    private static List<BloqueSmart> LeerBloques(CancellationToken token)
    {
        // Los umbrales se leen aparte y se emparejan por instancia, no se
        // mezclan en un solo mapa: dos discos de modelos distintos pueden
        // compartir número de atributo con umbrales distintos, y tomar el
        // primero le daría a uno el criterio del otro.
        var umbrales = new Dictionary<string, Dictionary<int, byte>>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var fila in Wmi.Query("SELECT InstanceName, VendorSpecific FROM MSStorageDriver_FailurePredictThresholds", WmiNs, token))
            {
                string instancia = Wmi.Str(fila, "InstanceName");
                if (string.IsNullOrWhiteSpace(instancia)) continue;
                umbrales[instancia] = ParsearUmbrales(Bytes(fila, "VendorSpecific"));
            }
        }
        catch (Exception ex)
        {
            AppLog.Write("No se pudieron leer los umbrales SMART: " + ex.Message, "WARN");
        }

        var bloques = new List<BloqueSmart>();
        try
        {
            foreach (var fila in Wmi.Query("SELECT InstanceName, VendorSpecific FROM MSStorageDriver_ATAPISmartData", WmiNs, token))
            {
                string instancia = Wmi.Str(fila, "InstanceName");
                if (string.IsNullOrWhiteSpace(instancia)) continue;

                var atributos = ParsearAtributos(Bytes(fila, "VendorSpecific"));
                if (atributos.Count == 0) continue;

                umbrales.TryGetValue(instancia, out var propios);
                bloques.Add(new BloqueSmart
                {
                    Instancia = instancia,
                    Atributos = atributos,
                    Umbrales = propios ?? new Dictionary<int, byte>()
                });
            }
        }
        catch (Exception ex)
        {
            AppLog.Write("No se pudo leer el bloque SMART: " + ex.Message, "WARN");
        }
        return bloques;
    }

    /// <summary>
    /// Contadores de fiabilidad del servicio de almacenamiento. Son el respaldo
    /// cuando el bloque SMART no llega: cubren menos, pero existen en equipos
    /// donde el paso de comandos al disco está bloqueado.
    /// </summary>
    private static Dictionary<string, ContadoresFiabilidad> LeerContadores(CancellationToken token)
    {
        var mapa = new Dictionary<string, ContadoresFiabilidad>(StringComparer.OrdinalIgnoreCase);

        foreach (string clase in new[] { "MSFT_StorageReliabilityInformation", "MSFT_StorageReliabilityCounter" })
        {
            try
            {
                foreach (var fila in Wmi.Query($"SELECT * FROM {clase}", StorageNs, token))
                {
                    string id = Wmi.Str(fila, "DeviceId");
                    if (string.IsNullOrEmpty(id) || mapa.ContainsKey(id)) continue;

                    double? lectura = Num(fila, "ReadErrorsUncorrected"), escritura = Num(fila, "WriteErrorsUncorrected");
                    double errores = (lectura ?? 0) + (escritura ?? 0);

                    mapa[id] = new ContadoresFiabilidad
                    {
                        Horas = Num(fila, "PowerOnHours"),
                        Desgaste = Num(fila, "Wear"),
                        Temperatura = Num(fila, "Temperature"),
                        // Sin los dos contadores no hay forma de afirmar que hay
                        // cero errores: «nulo» y «cero» no son lo mismo.
                        Errores = lectura.HasValue && escritura.HasValue ? errores : (double?)null
                    };
                }
            }
            catch (Exception ex)
            {
                AppLog.Write($"No se pudo leer {clase}: {ex.Message}", "WARN");
            }
            if (mapa.Count > 0) break;
        }
        return mapa;
    }

    private static double? Num(WmiRow fila, string propiedad) =>
        Wmi.TryNum(fila, propiedad, out double valor) ? valor : (double?)null;

    /// <summary>
    /// WMI entrega las matrices de bytes como `byte[]`, pero el tipo exacto
    /// depende de la consulta; se acepta cualquier matriz y se convierte.
    /// </summary>
    private static byte[] Bytes(WmiRow fila, string propiedad)
    {
        object valor = fila[propiedad];
        if (valor is byte[] directo) return directo;
        if (valor is Array matriz)
        {
            var salida = new byte[matriz.Length];
            for (int i = 0; i < matriz.Length; i++)
            {
                object elemento = matriz.GetValue(i);
                salida[i] = elemento == null ? (byte)0 : Convert.ToByte(elemento);
            }
            return salida;
        }
        return null;
    }

    /// <summary>
    /// Une una instancia de `MSStorageDriver_ATAPISmartData` con un disco
    /// físico. El nombre de la instancia trae el modelo con guiones bajos en
    /// lugar de espacios; sin normalizar, nada coincidiría.
    /// </summary>
    private static BloqueSmart Emparejar(List<BloqueSmart> bloques, string nombreDisco)
    {
        string buscado = Normalizar(nombreDisco);
        if (buscado.Length >= 4)
            foreach (var bloque in bloques)
                if (Normalizar(bloque.Instancia).Contains(buscado)) return bloque;

        // Con un solo bloque SMART no hay ambigüedad posible: adjudicarlo es
        // mejor que descartar el dato.
        if (bloques.Count == 1) return bloques[0];

        return null;
    }

    private static string Normalizar(string texto)
    {
        var chars = new List<char>(texto.Length);
        foreach (char c in (texto ?? "").ToUpperInvariant())
            if (!char.IsWhiteSpace(c) && c != '_' && c != '\\') chars.Add(c);
        return new string(chars.ToArray());
    }

    private static string LimpiarInstancia(string instancia)
    {
        string[] partes = (instancia ?? "").Split('\\');
        string texto = (partes.Length >= 2 ? partes[1] : partes[0]).Replace('_', ' ').Trim();

        // El nombre de instancia trae el modelo y, separado por una tanda de
        // guiones bajos, el número de serie. Se muestra el modelo: el número de
        // serie no ayuda a reconocer el disco y ensucia la lectura.
        int serie = texto.IndexOf("   ", StringComparison.Ordinal);
        if (serie > 0) texto = texto.Substring(0, serie).Trim();
        return texto;
    }

    /// <summary>
    /// El motivo cuando no hay datos. Distingue el caso común —sin
    /// administrador, o puente USB sin paso de comandos— del caso opaco, porque
    /// el primero se arregla y el segundo no.
    /// </summary>
    private static string MotivoSinDatos()
    {
        if (!AppEnv.IsAdmin)
            return "Windows no entrega el bloque SMART sin permisos de administrador. Cerrá SysDiag y abrilo como administrador para ver los atributos.";

        return "Este disco no expone atributos SMART. Es habitual en unidades detrás de un puente USB o sin paso de comandos; en ese caso no hay forma de leerlos desde Windows.";
    }
}
