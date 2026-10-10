using System;
using System.IO;
using System.Text.Json;

namespace SysDiag.Core.Licensing;

public enum EstadoLicencia
{
    /// <summary>Prueba gratuita vigente: todo funciona.</summary>
    Prueba,
    /// <summary>Código de activación válido.</summary>
    Pro,
    /// <summary>La prueba terminó sin código: lectura sí, acciones no.</summary>
    Vencida
}

/// <summary>
/// Estado de licencia de la aplicación. Tres estados:
///
///  - Prueba (14 días desde el primer arranque): todo habilitado.
///  - Pro: código de activación válido guardado en disco.
///  - Vencida: el diagnóstico y la lectura siguen libres, pero las acciones
///    que modifican el equipo (optimizar, instalar, limpiar, reparar) exigen
///    un código de activación.
///
/// La verificación del código es offline (ver <see cref="LicenseCrypto"/>);
/// no se contacta ningún servidor.
/// </summary>
public static class LicenseService
{
    public const int DiasPrueba = 14;

    private class ArchivoLicencia
    {
        public string PrimerInicio { get; set; }
        public string Codigo { get; set; }
        public string Activacion { get; set; }

        /// <summary>
        /// Equipo (máquina\usuario) donde se activó el código guardado. Se
        /// escribe desde esta versión: los archivos anteriores no lo tienen y se
        /// les aplica el trato genérico. Sirve para poder decir algo cierto
        /// cuando el código deja de verificar.
        /// </summary>
        public string Equipo { get; set; }
    }

    /// <summary>
    /// Huella legible del criterio con el que <see cref="LicenseCrypto"/> ata un
    /// código al equipo. Coincide con su composición interna sin depender de
    /// ella: si un día la etiqueta cambia, esto sigue describiendo el equipo.
    /// </summary>
    public static string EquipoActual => Environment.MachineName + @"\" + Environment.UserName;

    private static readonly object Bloqueo = new();
    private static ArchivoLicencia _archivo = new();
    private static EstadoLicencia _estado = EstadoLicencia.Prueba;
    private static string _error = "";

    /// <summary>
    /// Ruta del archivo de licencia en LocalAppData (no sincroniza con Documentos
    /// a propósito). Es escribible solo desde <see cref="Inicializar"/> para que
    /// las pruebas puedan señalarlo a un directorio temporal: sin eso, comprobar
    /// que una licencia no se borra implicaría tocar la del equipo de quien
    /// prueba, que es justamente lo que la corrección intentaba evitar.
    /// </summary>
    public static string RutaArchivo { get; private set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SysDiag", "licencia.json");

    public static EstadoLicencia Estado { get { lock (Bloqueo) return _estado; } }

    public static bool EsPro => Estado == EstadoLicencia.Pro;

    /// <summary>True mientras la prueba corre o hay código activo: permite acciones que modifican el equipo.</summary>
    public static bool PuedeModificar => Estado != EstadoLicencia.Vencida;

    /// <summary>Días restantes de prueba (0 si vencida o si hay código Pro).</summary>
    public static int DiasPruebaRestantes { get; private set; } = DiasPrueba;

    public static string CodigoActivo { get { lock (Bloqueo) return _archivo?.Codigo ?? ""; } }

    /// <summary>Último error de activación, para la ventana de licencia.</summary>
    public static string UltimoError => _error;

    /// <summary>
    /// Aviso pendiente de mostrar en la ventana de licencia: lo que hay que
    /// decirle al usuario sobre un código guardado que no verifica. No es un
    /// error de activación (nadie tecleó nada), por eso no comparte el campo.
    /// </summary>
    public static string Aviso { get; private set; } = "";

    /// <summary>Etiqueta corta para la barra superior: «Pro», «Prueba · 12 días», «Sin licencia».</summary>
    public static string EtiquetaCorta
    {
        get
        {
            return Estado switch
            {
                EstadoLicencia.Pro => "Pro",
                EstadoLicencia.Prueba => $"Prueba · {DiasPruebaRestantes} día(s)",
                _ => "Sin licencia"
            };
        }
    }

    /// <summary>Se avisa cuando el estado cambia (activación), para refrescar la interfaz.</summary>
    public static event Action EstadoCambiado;

    /// <summary>
    /// Crea o lee el archivo de licencia y calcula el estado. Idempotente;
    /// nunca lanza: si el archivo está corrupto se ignora y empieza la prueba.
    /// </summary>
    public static void Inicializar(string rutaArchivo = null)
    {
        lock (Bloqueo)
        {
            if (!string.IsNullOrEmpty(rutaArchivo)) RutaArchivo = rutaArchivo;
            try
            {
                if (File.Exists(RutaArchivo))
                {
                    _archivo = JsonSerializer.Deserialize<ArchivoLicencia>(File.ReadAllText(RutaArchivo)) ?? new ArchivoLicencia();
                }
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(RutaArchivo));
                    _archivo = new ArchivoLicencia { PrimerInicio = DateTime.Now.ToString("o") };
                    File.WriteAllText(RutaArchivo, JsonSerializer.Serialize(_archivo));
                }

                if (string.IsNullOrEmpty(_archivo.PrimerInicio))
                {
                    _archivo.PrimerInicio = DateTime.Now.ToString("o");
                    GuardarInterno();
                }

                if (!string.IsNullOrEmpty(_archivo.Codigo) &&
                    LicenseCrypto.Verificar(_archivo.Codigo, out _, out _))
                {
                    _estado = EstadoLicencia.Pro;
                }
                else
                {
                    _estado = EstadoLicencia.Prueba;

                    // Antes este ramo hacía `_archivo.Codigo = null` y guardaba,
                    // o sea: borraba la prueba de que alguien compró una
                    // licencia. Como el código vinculado se deriva de
                    // máquina\usuario, renombrar el PC, entrar con otra cuenta o
                    // un perfil roaming apagaban la licencia del comprador y le
                    // quitaban hasta el código para recuperarla.
                    //
                    // Se deja de borrar. El estado igual es de prueba —no se
                    // concede nada—, pero el dato del usuario sobrevive, y si el
                    // equipo vuelve a llamarse como antes la licencia reaparece
                    // sola. El aviso explica qué pasó en lugar de callarlo.
                    if (!string.IsNullOrEmpty(_archivo.Codigo))
                    {
                        // Tres casos y tres frases distintas. El archivo sin
                        // `Equipo` es el de cualquier licencia activada antes de
                        // esta versión: decir «se activó en «»» sería peor que no
                        // decir nada, así que la ausencia de dato tiene su propia
                        // rama en lugar de compartir la del equipo distinto.
                        Aviso = string.IsNullOrEmpty(_archivo.Equipo)
                            ? "El código guardado no verifica en este equipo: puede estar mal escrito, ser de otra versión o haberse editado el archivo. Se conserva para que no lo pierdas."
                            : string.Equals(_archivo.Equipo, EquipoActual, StringComparison.Ordinal)
                                ? "El código guardado no verifica en este equipo aunque se activó aquí: revisa que no se haya editado el archivo de licencia."
                                : $"El código guardado se activó en «{_archivo.Equipo}» y en «{EquipoActual}» no verifica. Sigue guardado: si el equipo recupera su nombre anterior, la licencia vuelve a aparecer.";
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
            {
                // Sin archivo utilizable no hay prueba que perder: se abre la app
                // y el usuario puede activar cuando quiera. Mejor arrancar con
                // estado de prueba que no arrancar.
                _archivo = new ArchivoLicencia { PrimerInicio = DateTime.Now.ToString("o") };
                _estado = EstadoLicencia.Prueba;
            }

            CalcularPrueba();
        }
    }

    private static void CalcularPrueba()
    {
        if (!DateTime.TryParse(_archivo.PrimerInicio, out DateTime inicio)) inicio = DateTime.Now;
        int usados = (int)(DateTime.Now - inicio).TotalDays;
        DiasPruebaRestantes = Math.Max(0, DiasPrueba - usados);
        if (_estado == EstadoLicencia.Prueba && DiasPruebaRestantes == 0)
            _estado = EstadoLicencia.Vencida;
    }

    /// <summary>
    /// Intenta activar con el código escrito por el usuario.
    /// Devuelve false y deja el mensaje en <see cref="UltimoError"/> si no sirve.
    /// </summary>
    public static bool Activar(string codigo)
    {
        lock (Bloqueo)
        {
            _error = "";
            if (!LicenseCrypto.Verificar(codigo, out CodigoInfo info, out string error))
            {
                _error = error ?? "El código no es válido.";
                return false;
            }
            if (info.VinculadaEquipo && !LicenseCrypto.VerificarParaEsteEquipo(codigo, out _, out _))
            {
                _error = "Este código fue emitido para otro equipo.";
                return false;
            }

            string normalizado = (codigo ?? "").Trim();
            _archivo.Codigo = normalizado;
            _archivo.Activacion = DateTime.Now.ToString("o");
            _archivo.Equipo = EquipoActual;
            _error = "";
            Aviso = "";
            try { GuardarInterno(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _error = "No se pudo guardar la licencia: " + ex.Message;
                return false;
            }
            _estado = EstadoLicencia.Pro;
        }
        EstadoCambiado?.Invoke();
        return true;
    }

    private static void GuardarInterno()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(RutaArchivo));
        File.WriteAllText(RutaArchivo, JsonSerializer.Serialize(_archivo));
    }
}
