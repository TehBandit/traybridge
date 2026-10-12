using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace TrayBridge;

internal static class LoadedHelper
{
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadProcessMemory(IntPtr process, IntPtr address, byte[] buffer, nuint size, out nuint read);

    // Resolve from the loaded image, never from a possibly replaced DLL on disk.
    internal static IntPtr Export(IntPtr process, IntPtr image, string name)
    {
        byte[] Read(uint offset, int size)
        {
            var buffer = new byte[size];
            if (!ReadProcessMemory(process, image + (int)offset, buffer, (nuint)size, out var read) || read != (nuint)size) throw new Win32Exception(Marshal.GetLastWin32Error());
            return buffer;
        }
        var dos = Read(0, 64);
        var ntOffset = BitConverter.ToUInt32(dos, 60);
        if (BitConverter.ToUInt16(dos, 0) != 0x5A4D || ntOffset > 1024 * 1024) throw new IOException("The loaded helper has an invalid image header.");
        var nt = Read(ntOffset, 144);
        if (BitConverter.ToUInt32(nt, 0) != 0x4550 || BitConverter.ToUInt16(nt, 4) != 0x8664 || BitConverter.ToUInt16(nt, 24) != 0x20B) throw new IOException("Only the validated x64 helper image is supported.");
        var imageSize = BitConverter.ToUInt32(nt, 80);
        var exportRva = BitConverter.ToUInt32(nt, 136); var exportSize = BitConverter.ToUInt32(nt, 140);
        byte[] ImageRead(uint offset, int size)
        {
            if (size < 0 || imageSize > int.MaxValue || offset >= imageSize || (ulong)offset + (uint)size > imageSize) throw new IOException("The loaded helper's export table is invalid.");
            return Read(offset, size);
        }
        if (exportRva == 0) return IntPtr.Zero;
        var table = ImageRead(exportRva, 40);
        var functionCount = BitConverter.ToUInt32(table, 20); var nameCount = BitConverter.ToUInt32(table, 24);
        if (nameCount > 16384 || functionCount > 16384) throw new IOException("The loaded helper's export table is too large.");
        var functions = ImageRead(BitConverter.ToUInt32(table, 28), checked((int)functionCount * 4));
        var names = ImageRead(BitConverter.ToUInt32(table, 32), checked((int)nameCount * 4));
        var ordinals = ImageRead(BitConverter.ToUInt32(table, 36), checked((int)nameCount * 2));
        for (var index = 0; index < nameCount; index++)
        {
            var rva = BitConverter.ToUInt32(names, index * 4);
            if (rva >= imageSize) throw new IOException("The loaded helper has an invalid export name.");
            var text = ImageRead(rva, (int)Math.Min(128, imageSize - rva));
            var end = Array.IndexOf(text, (byte)0);
            if (end < 0 || Encoding.ASCII.GetString(text, 0, end) != name) continue;
            var ordinal = BitConverter.ToUInt16(ordinals, index * 2);
            if (ordinal >= functionCount) throw new IOException("The loaded helper has an invalid export ordinal.");
            var function = BitConverter.ToUInt32(functions, ordinal * 4);
            if (function == 0 || function >= imageSize || (function >= exportRva && (ulong)function < (ulong)exportRva + exportSize)) throw new IOException("Forwarded helper exports are unsupported.");
            return image + (int)function;
        }
        return IntPtr.Zero;
    }
}
