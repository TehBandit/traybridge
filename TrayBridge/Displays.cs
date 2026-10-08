using System.Runtime.InteropServices;

namespace TrayBridge;
internal sealed record DisplayInfo(string Id, string Device, string Name, bool Primary, Rectangle Bounds);
internal static class Displays
{
    public static IReadOnlyList<DisplayInfo> Read()
    {
        var names = ReadNames();
        return Screen.AllScreens.Select(screen => names.TryGetValue(screen.DeviceName, out var name)
            ? new DisplayInfo(name.Id, screen.DeviceName, name.Name, screen.Primary, screen.Bounds)
            : new DisplayInfo(screen.DeviceName, screen.DeviceName, screen.Primary ? "Primary display" : "Display", screen.Primary, screen.Bounds)).OrderByDescending(d => d.Primary).ThenBy(d => d.Bounds.X).ToArray();
    }
    private static Dictionary<string, (string Id, string Name)> ReadNames()
    {
        var result = new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase);
        for (var attempt = 0; attempt < 3; attempt++)
        {
            if (GetDisplayConfigBufferSizes(2, out uint paths, out uint modes) != 0) break;
            var pathMemory = Marshal.AllocHGlobal(checked((int)paths * 72));
            var modeMemory = Marshal.AllocHGlobal(checked((int)modes * 64));
            try
            {
                int error = QueryDisplayConfig(2, ref paths, pathMemory, ref modes, modeMemory, IntPtr.Zero);
                if (error == 122) continue;
                if (error != 0) break;
                for (int i = 0; i < paths; i++)
                {
                    var path = pathMemory + i * 72;
                    var source = new SourceName { Header = new DeviceHeader { Type = 1, Size = 84, Adapter = Marshal.ReadInt64(path), Id = (uint)Marshal.ReadInt32(path, 8) } };
                    var target = new TargetName { Header = new DeviceHeader { Type = 2, Size = 420, Adapter = Marshal.ReadInt64(path, 20), Id = (uint)Marshal.ReadInt32(path, 28) } };
                    if (DisplayConfigGetDeviceInfo(ref source) == 0 && DisplayConfigGetDeviceInfo(ref target) == 0 && !string.IsNullOrEmpty(target.DevicePath))
                        result[source.Device] = (target.DevicePath.ToUpperInvariant(), string.IsNullOrWhiteSpace(target.FriendlyName) ? "Display" : target.FriendlyName);
                }
                break;
            }
            finally { Marshal.FreeHGlobal(pathMemory); Marshal.FreeHGlobal(modeMemory); }
        }
        return result;
    }
    [StructLayout(LayoutKind.Sequential, Pack = 4)] private struct DeviceHeader { public uint Type, Size; public long Adapter; public uint Id; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode, Pack = 4)] private struct SourceName { public DeviceHeader Header; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Device; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode, Pack = 4)] private struct TargetName
    {
        public DeviceHeader Header;
        public uint Flags, Technology;
        public ushort Manufacturer, Product;
        public uint Connector;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string FriendlyName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DevicePath;
    }
    [DllImport("user32.dll")] private static extern int GetDisplayConfigBufferSizes(uint flags, out uint paths, out uint modes);
    [DllImport("user32.dll")] private static extern int QueryDisplayConfig(uint flags, ref uint pathCount, IntPtr paths, ref uint modeCount, IntPtr modes, IntPtr topology);
    [DllImport("user32.dll")] private static extern int DisplayConfigGetDeviceInfo(ref SourceName source);
    [DllImport("user32.dll")] private static extern int DisplayConfigGetDeviceInfo(ref TargetName target);
}
