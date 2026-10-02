using System.ComponentModel;
using System.Runtime.InteropServices;

namespace SysDiag.Core.Windows;

/// <summary>API de energía con GUID/índices, independiente del texto localizado de powercfg.</summary>
public static class PowerSettings
{
    public static readonly Guid WirelessSubgroup = new("19cbb8fa-5279-450e-9fac-8a3d5fedd0c1");
    public static readonly Guid WirelessSaving = new("12bbebe6-58d6-4636-95bb-3217ef867c1a");
    public static readonly Guid ProcessorSubgroup = new("54533251-82be-4824-96c1-47b60b740d00");
    public static readonly Guid ProcessorMaximum = new("bc5038f7-23e0-4960-96da-33abaf5935ec");
    public static readonly Guid HighPerformance = new("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");

    public static Guid ActivePlan()
    {
        Check(PowerGetActiveScheme(IntPtr.Zero, out IntPtr pointer));
        try { return Marshal.PtrToStructure<Guid>(pointer); }
        finally { LocalFree(pointer); }
    }

    public static uint ReadAc(Guid plan, Guid subgroup, Guid setting)
    {
        Check(PowerReadACValueIndex(IntPtr.Zero, ref plan, ref subgroup, ref setting, out uint value));
        return value;
    }

    public static void WriteAc(Guid plan, Guid subgroup, Guid setting, uint value) =>
        Check(PowerWriteACValueIndex(IntPtr.Zero, ref plan, ref subgroup, ref setting, value));
    public static void Activate(Guid plan) => Check(PowerSetActiveScheme(IntPtr.Zero, ref plan));

    public static IReadOnlyList<Guid> Plans()
    {
        var plans = new List<Guid>();
        IntPtr buffer = Marshal.AllocHGlobal(16);
        try
        {
            for (uint index = 0; ; index++)
            {
                uint size = 16;
                uint result = PowerEnumerate(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 16 /* ACCESS_SCHEME */,
                    index, buffer, ref size);
                if (result == 259 /* ERROR_NO_MORE_ITEMS */) break;
                Check(result);
                plans.Add(Marshal.PtrToStructure<Guid>(buffer));
            }
        }
        finally { Marshal.FreeHGlobal(buffer); }
        return plans;
    }

    private static void Check(uint error)
    {
        if (error != 0) throw new Win32Exception((int)error, $"La API de energía de Windows devolvió {error}.");
    }

    [DllImport("powrprof.dll")]
    private static extern uint PowerGetActiveScheme(IntPtr root, out IntPtr plan);
    [DllImport("powrprof.dll")]
    private static extern uint PowerReadACValueIndex(IntPtr root, ref Guid plan, ref Guid subgroup, ref Guid setting, out uint value);
    [DllImport("powrprof.dll")]
    private static extern uint PowerWriteACValueIndex(IntPtr root, ref Guid plan, ref Guid subgroup, ref Guid setting, uint value);
    [DllImport("powrprof.dll")]
    private static extern uint PowerSetActiveScheme(IntPtr root, ref Guid plan);
    [DllImport("powrprof.dll")]
    private static extern uint PowerEnumerate(IntPtr root, IntPtr plan, IntPtr subgroup, uint access,
        uint index, IntPtr buffer, ref uint size);
    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr pointer);
}
