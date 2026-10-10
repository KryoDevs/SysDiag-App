using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace SysDiag.Core.Licensing;

/// <summary>
/// Información decodificada de un código de activación.
/// </summary>
public class CodigoInfo
{
    public int Version { get; init; }
    public bool VinculadaEquipo { get; init; }
    /// <summary>Fecha de expiración; null = licencia perpetua.</summary>
    public DateTime? Expira { get; init; }
    public uint Serial { get; init; }

    public bool EsPerpetua => Expira == null;

    public bool Vigente(DateTime ahora) => Expira == null || ahora <= Expira.Value;
}

/// <summary>
/// Emisión y verificación offline de códigos de activación tipo
/// <c>SDG7-AAAAA-BBBBB-CCCCC-DDDDD-EEEE</c>.
///
/// Diseño: 8 bytes de carga útil + 10 bytes de HMAC-SHA256 truncado, todo
/// codificado en base32 Crockford (sin I, L, O, U: se teclea sin confusiones).
///
/// Carga útil (8 bytes):
///   [0]     versión del formato (1)
///   [1]     banderas: bit0 = vinculada al equipo, bit1 = perpetua
///   [2..3]  expiración: días desde 2026-01-01 (0 = perpetua), little-endian
///   [4..7]  serial del código (aleatorio), little-endian
///
/// La clave HMAC está embebida en la aplicación a propósito: es un sistema de
/// disuasión para reventa informal, no una caja fuerte. El mismo material está
/// en <c>Tools/New-ActivationCode.ps1</c>, que usa el vendedor para emitir
/// códigos sin conexión.
/// </summary>
public static class LicenseCrypto
{
    public const string Prefijo = "SDG7";
    private const int VersionFormato = 1;
    private const byte BandVinculada = 0b0000_0001;
    private const byte BandPerpetua = 0b0000_0010;

    private const int BytesCarga = 8;
    private const int BytesMac = 10;
    private const int BytesTotal = BytesCarga + BytesMac;

    private static readonly DateTime Origen = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);

    private const string Alfabeto = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    // ---- Clave compartida con el emisor de códigos --------------------------

    private static byte[] Clave()
    {
        // Literal idéntico al de Tools/New-ActivationCode.ps1. Ver el resumen de
        // la clase para el alcance real de esta protección.
        return SHA256.HashData(Encoding.UTF8.GetBytes("SysDiag/2026/clave-privada-v1"));
    }

    private static byte[] EtiquetaEquipo()
    {
        string crudo = (Environment.MachineName + "|" + Environment.UserName).ToUpperInvariant();
        return SHA256.HashData(Encoding.UTF8.GetBytes(crudo));
    }

    private static byte[] CalcularMac(byte[] carga, bool vinculada)
    {
        var entrada = new List<byte>(carga);
        if (vinculada) entrada.AddRange(EtiquetaEquipo());
        using var hmac = new HMACSHA256(Clave());
        byte[] resumen = hmac.ComputeHash(entrada.ToArray());
        var mac = new byte[BytesMac];
        Array.Copy(resumen, mac, BytesMac);
        return mac;
    }

    // ---- Emisión -----------------------------------------------------------

    /// <summary>
    /// Genera un código de activación.
    /// </summary>
    /// <param name="dias">Días de vigencia desde hoy; 0 = perpetua.</param>
    /// <param name="vinculadaEquipo">Si es true, el código solo vale en el equipo donde se emitió.</param>
    /// <param name="serial">Serial interno; si se omite, uno aleatorio.</param>
    public static string Generar(int dias, bool vinculadaEquipo = false, uint? serial = null)
    {
        if (dias < 0 || dias > 65534) throw new ArgumentOutOfRangeException(nameof(dias), "La vigencia va de 0 a 65534 días.");

        // Se valida en int ANTES del casteo a ushort: si no, un desbordamiento
        // envolvería en silencio y el código saldría con una fecha cualquiera.
        int expiraCalculado = dias == 0 ? 0 : (DateTime.Today - Origen).Days + dias;
        if (expiraCalculado > 65534) throw new ArgumentOutOfRangeException(nameof(dias), "La fecha de expiración excede el formato.");
        ushort expiracion = (ushort)expiraCalculado;

        uint ser = serial ?? (uint)RandomNumberGenerator.GetInt32(1, int.MaxValue);

        var carga = new byte[BytesCarga];
        carga[0] = VersionFormato;
        carga[1] = (byte)((vinculadaEquipo ? BandVinculada : 0) | (dias == 0 ? BandPerpetua : 0));
        carga[2] = (byte)(expiracion & 0xFF);
        carga[3] = (byte)(expiracion >> 8);
        carga[4] = (byte)(ser & 0xFF);
        carga[5] = (byte)((ser >> 8) & 0xFF);
        carga[6] = (byte)((ser >> 16) & 0xFF);
        carga[7] = (byte)((ser >> 24) & 0xFF);

        var paquete = new byte[BytesTotal];
        Array.Copy(carga, paquete, BytesCarga);
        Array.Copy(CalcularMac(carga, vinculadaEquipo), 0, paquete, BytesCarga, BytesMac);

        return Formatear(Codificar(paquete));
    }

    // ---- Verificación ------------------------------------------------------

    /// <summary>
    /// Verifica un código escrito por el usuario. Tolera espacios, guiones,
    /// minúsculas y el prefijo SDG7 opcional.
    /// </summary>
    public static bool Verificar(string codigo, out CodigoInfo info, out string error)
    {
        info = null;
        error = null;
        try
        {
            string limpio = Normalizar(codigo);
            byte[] paquete = Decodificar(limpio, BytesTotal);

            var carga = new byte[BytesCarga];
            Array.Copy(paquete, carga, BytesCarga);
            var mac = new byte[BytesMac];
            Array.Copy(paquete, BytesCarga, mac, 0, BytesMac);

            if (carga[0] != VersionFormato)
            {
                error = "Código de una versión no compatible con esta aplicación.";
                return false;
            }

            bool vinculada = (carga[1] & BandVinculada) != 0;
            bool perpetua = (carga[1] & BandPerpetua) != 0;

            byte[] esperado = CalcularMac(carga, vinculada);
            if (!CryptographicOperations.FixedTimeEquals(mac, esperado))
            {
                error = "El código no es válido. Revisa que esté completo y bien escrito.";
                return false;
            }

            ushort dias = (ushort)(carga[2] | (carga[3] << 8));
            uint ser = (uint)(carga[4] | (carga[5] << 8) | (carga[6] << 16) | (carga[7] << 24));

            DateTime? expira = null;
            if (!perpetua && dias != 0)
            {
                expira = Origen.AddDays(dias);
                if (expira.Value < DateTime.Today)
                {
                    error = "El código existe pero ya venció.";
                    return false;
                }
            }

            info = new CodigoInfo
            {
                Version = carga[0],
                VinculadaEquipo = vinculada,
                Expira = expira,
                Serial = ser
            };
            return true;
        }
        catch (FormatException ex)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>True si el código es válido y corresponde a este equipo cuando está vinculado.</summary>
    public static bool VerificarParaEsteEquipo(string codigo, out CodigoInfo info, out string error)
    {
        if (!Verificar(codigo, out info, out error)) return false;
        if (info.VinculadaEquipo)
        {
            // Verificar ya solo acepta la MAC si coincide la etiqueta de este
            // equipo: un código vinculado emitido en otro equipo falla antes.
            // La rama queda explícita por si el formato cambia más adelante.
            return true;
        }
        return true;
    }

    // ---- Codificación ------------------------------------------------------

    private static string Normalizar(string codigo)
    {
        if (string.IsNullOrWhiteSpace(codigo))
            throw new FormatException("Escribe un código de activación.");
        var sb = new StringBuilder(codigo.Length);
        foreach (char c in codigo.Trim())
        {
            char u = char.ToUpperInvariant(c);
            if (u == ' ' || u == '-' || u == '\t') continue;
            if (u == 'O') u = '0';
            else if (u == 'I' || u == 'L') u = '1';
            sb.Append(u);
        }
        string texto = sb.ToString();
        if (texto.StartsWith(Prefijo, StringComparison.Ordinal)) texto = texto.Substring(Prefijo.Length);
        if (texto.Length == 0) throw new FormatException("Escribe un código de activación.");
        return texto;
    }

    private static string Codificar(byte[] datos)
    {
        var sb = new StringBuilder((datos.Length * 8 + 4) / 5);
        int buffer = 0, bits = 0;
        foreach (byte b in datos)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                bits -= 5;
                sb.Append(Alfabeto[(buffer >> bits) & 31]);
            }
        }
        if (bits > 0) sb.Append(Alfabeto[(buffer << (5 - bits)) & 31]);
        return sb.ToString();
    }

    private static byte[] Decodificar(string texto, int longitud)
    {
        int bits = 0, buffer = 0;
        var salida = new List<byte>(longitud + 1);
        foreach (char c in texto)
        {
            int idx = Alfabeto.IndexOf(c);
            if (idx < 0) throw new FormatException("El código contiene caracteres no válidos.");
            buffer = (buffer << 5) | idx;
            bits += 5;
            if (bits >= 8)
            {
                bits -= 8;
                salida.Add((byte)((buffer >> bits) & 0xFF));
            }
        }
        if (salida.Count != longitud) throw new FormatException("El código está incompleto.");
        return salida.ToArray();
    }

    private static string Formatear(string base32)
    {
        var sb = new StringBuilder(Prefijo.Length + 1 + base32.Length + base32.Length / 4);
        sb.Append(Prefijo);
        for (int i = 0; i < base32.Length; i++)
        {
            if (i % 5 == 0) sb.Append('-');
            sb.Append(base32[i]);
        }
        return sb.ToString();
    }
}
