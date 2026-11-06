using System.Runtime.InteropServices;
using System.Text;

namespace Hive.Infrastructure;

public sealed record FileProcessOwner(int ProcessId, DateTime StartedUtc);

public static class RestartManagerFileOwners
{
    [StructLayout(LayoutKind.Sequential)]
    private struct UniqueProcess { public int ProcessId; public System.Runtime.InteropServices.ComTypes.FILETIME StartTime; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessInfo
    {
        public UniqueProcess Process;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string AppName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string ServiceName;
        public int AppType; public uint AppStatus; public uint SessionId;
        [MarshalAs(UnmanagedType.Bool)] public bool Restartable;
    }
    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)] private static extern int RmStartSession(out uint handle, int flags, StringBuilder key);
    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)] private static extern int RmRegisterResources(uint handle, uint count, string[] files, uint apps, IntPtr app, uint services, IntPtr service);
    [DllImport("rstrtmgr.dll")] private static extern int RmGetList(uint handle, out uint needed, ref uint count, [In, Out] ProcessInfo[]? info, ref uint reason);
    [DllImport("rstrtmgr.dll")] private static extern int RmEndSession(uint handle);

    public static IReadOnlyList<FileProcessOwner> GetOwners(string path)
    {
        if (RmStartSession(out var handle, 0, new StringBuilder(33)) != 0) return [];
        try
        {
            if (RmRegisterResources(handle, 1, [Path.GetFullPath(path)], 0, IntPtr.Zero, 0, IntPtr.Zero) != 0) return [];
            uint count = 0, reason = 0;
            var result = RmGetList(handle, out var needed, ref count, null, ref reason);
            if (result == 0) return [];
            if (result != 234 || needed == 0 || needed > 1024) return [];
            for (var attempt = 0; attempt < 3; attempt++)
            {
                var info = new ProcessInfo[needed]; count = needed;
                result = RmGetList(handle, out needed, ref count, info, ref reason);
                if (result == 234 && needed <= 1024) continue;
                if (result != 0) return [];
                var owners = new List<FileProcessOwner>();
                foreach (var item in info.Take((int)count))
                {
                    var ticks = ((long)(uint)item.Process.StartTime.dwHighDateTime << 32) | (uint)item.Process.StartTime.dwLowDateTime;
                    try { owners.Add(new(item.Process.ProcessId, DateTime.FromFileTimeUtc(ticks))); } catch (ArgumentOutOfRangeException) { }
                }
                return owners;
            }
            return [];
        }
        finally { RmEndSession(handle); }
    }
}
