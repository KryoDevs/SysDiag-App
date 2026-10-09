using System;
using System.Runtime.InteropServices;

namespace SysDiag.Core.Windows;

/// <summary>
/// Punto de restauración real de Windows, vía srclient.dll (la misma API que
/// usa el propio Panel de control cuando instalas un programa). Reemplaza al
/// respaldo por renombrado (.bak) que usaban las acciones de riesgo hasta
/// ahora: un punto de restauración cubre TODO el estado del sistema en ese
/// momento, no solo la carpeta puntual que se va a tocar.
///
/// Requiere administrador. Si la Protección del sistema está desactivada en
/// la unidad — algo común de fábrica en equipos con SSD — la llamada falla
/// de forma predecible y se informa con una recomendación concreta, nunca en
/// silencio.
/// </summary>
public static class RestorePointModule
{
    private const int BEGIN_SYSTEM_CHANGE = 100;
    private const int END_SYSTEM_CHANGE = 101;
    private const int MODIFY_SETTINGS = 12;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct RESTOREPOINTINFO
    {
        public int dwEventType;
        public int dwRestorePtType;
        public long llSequenceNumber;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szDescription;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct STATEMGRSTATUS
    {
        public int nStatus;
        public long llSequenceNumber;
    }

    [DllImport("srclient.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SRSetRestorePointW(ref RESTOREPOINTINFO info, out STATEMGRSTATUS status);

    public class Resultado
    {
        public bool Exito;
        public string Mensaje = "";
        public long NumeroSecuencia;
    }

    /// <summary>
    /// Crea el punto de restauración completo: la API exige un par de
    /// llamadas (inicio y fin del "cambio de sistema") con el mismo número de
    /// secuencia, no una sola. Windows no muestra nada como "creado" hasta
    /// que llega la segunda.
    /// </summary>
    public static Resultado Crear(string descripcion)
    {
        if (!AppEnv.IsAdmin)
            return new Resultado { Exito = false, Mensaje = "Crear un punto de restauración requiere ejecutar SysDiag como administrador." };

        // Windows recorta la descripción a 64 caracteres visibles en la UI
        // de Restaurar sistema; se acorta antes para que no quede truncada
        // a la mitad de una palabra.
        descripcion = "SysDiag: " + (descripcion ?? "Cambio de configuración");
        if (descripcion.Length > 64) descripcion = descripcion.Substring(0, 61) + "...";

        var inicio = new RESTOREPOINTINFO
        {
            dwEventType = BEGIN_SYSTEM_CHANGE,
            dwRestorePtType = MODIFY_SETTINGS,
            llSequenceNumber = 0,
            szDescription = descripcion
        };

        AppLog.Write($"Creando punto de restauración: «{inicio.szDescription}»", "STEP");

        bool started = SRSetRestorePointW(ref inicio, out STATEMGRSTATUS estado);
        if (!started || estado.nStatus != 0 || estado.llSequenceNumber <= 0)
        {
            int err = estado.nStatus != 0 ? estado.nStatus : Marshal.GetLastWin32Error();
            string motivo = Explicar(err);
            AppLog.Write($"No se pudo iniciar el punto de restauración (0x{err:X8}): {motivo}", "ERROR");
            return new Resultado { Exito = false, Mensaje = motivo };
        }

        // Cierre del punto: mismo número de secuencia que devolvió el inicio.
        var fin = new RESTOREPOINTINFO
        {
            dwEventType = END_SYSTEM_CHANGE,
            dwRestorePtType = MODIFY_SETTINGS,
            llSequenceNumber = estado.llSequenceNumber,
            szDescription = inicio.szDescription
        };

        bool ended = SRSetRestorePointW(ref fin, out STATEMGRSTATUS estadoFinal);
        if (!ended || estadoFinal.nStatus != 0)
        {
            int err = estadoFinal.nStatus != 0 ? estadoFinal.nStatus : Marshal.GetLastWin32Error();
            string motivo = Explicar(err);
            AppLog.Write($"El punto de restauración quedó a medio crear (0x{err:X8}): {motivo}", "ERROR");
            return new Resultado { Exito = false, Mensaje = motivo };
        }

        AppLog.Write($"Punto de restauración creado (secuencia {estado.llSequenceNumber}).", "OK");
        return new Resultado
        {
            Exito = true,
            NumeroSecuencia = estado.llSequenceNumber,
            Mensaje = $"Punto de restauración creado: «{inicio.szDescription}».\n\nSi algo sale mal, se revierte desde Panel de control ▸ Recuperación ▸ Abrir Restaurar sistema."
        };
    }

    private static string Explicar(int codigoWin32)
    {
        return codigoWin32 switch
        {
            // ERROR_SERVICE_DISABLED / fallos típicos cuando la Protección
            // del sistema está apagada en la unidad.
            1058 => "La Protección del sistema parece estar desactivada en esta unidad. Actívala en Panel de control ▸ Sistema ▸ Protección del sistema y vuelve a intentarlo.",
            1060 => "El servicio de Restauración del sistema no está instalado o está deshabilitado en este equipo.",
            5 => "Acceso denegado al crear el punto de restauración. Confirma que SysDiag corre como administrador.",
            _ => $"No se pudo crear el punto de restauración (código de Windows {codigoWin32}). Revisa la Protección del sistema de la unidad y vuelve a intentarlo. Las operaciones que lo exigen no se ejecutan sin él."
        };
    }
}
