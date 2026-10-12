using System.Text;
using TrayBridge.UninstallSupport;

namespace TrayBridge;

internal static class FileOwnership
{
    internal static void WriteText(string path, string contents)
    {
        File.WriteAllText(path, contents); Record(path);
    }
    internal static void AppendText(string path, string contents)
    {
        File.AppendAllText(path, contents); Record(path);
    }
    internal static void Record(string path)
    {
        try
        {
            var root = SafeFiles.Canonical(NativeHost.DataDirectory);
            if (!SafeFiles.Within(path, root)) return;
            var relative = SafeFiles.Canonical(path)[(root.Length + 1)..];
            if (!SafeFiles.DataName(relative)) return;
            var entry = SafeFiles.Capture(path, "data", false);
            var record = Path.Combine(root, "ownership", relative + ".owner");
            // Check existing ancestors before creating missing record directories.
            var missing = new Stack<string>();
            for (var directory = Path.GetDirectoryName(record)!; !Directory.Exists(directory); directory = Path.GetDirectoryName(directory)!) missing.Push(directory);
            while (missing.Count > 0)
            {
                var directory = missing.Pop();
                var parents = SafeFiles.LockParents(directory);
                try { Directory.CreateDirectory(directory); using var check = SafeFiles.Open(directory, true, false); }
                finally { foreach (var handle in parents) handle.Dispose(); }
            }
            var locks = SafeFiles.LockParents(record);
            try
            {
                if (File.Exists(record))
                {
                    using var verified = SafeFiles.Open(record, false, false);
                    // An unrelated record is never overwritten.
                    using var copy = new Microsoft.Win32.SafeHandles.SafeFileHandle(verified.DangerousGetHandle(), false);
                    using var reader = new BinaryReader(new FileStream(copy, FileAccess.Read));
                    if (reader.BaseStream.Length != 16 || reader.ReadUInt32() != 0x314F4254) return;
                }
                using var stream = new FileStream(record, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                SafeFiles.CheckPath(stream.SafeFileHandle, record);
                if (stream.Length != 0)
                {
                    using var reader = new BinaryReader(stream, Encoding.UTF8, true);
                    if (stream.Length != 16 || reader.ReadUInt32() != 0x314F4254) return;
                    stream.Position = 0;
                }
                using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
                writer.Write(0x314F4254u); writer.Write(entry.Volume); writer.Write(entry.High); writer.Write(entry.Low);
                stream.SetLength(16); stream.Flush(true);
            }
            finally { foreach (var handle in locks) handle.Dispose(); }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception) { /* Untracked files are preserved by uninstall. */ }
    }

    internal static bool Matches(string path, RemovalEntry entry)
    {
        var relative = SafeFiles.Canonical(path)[(SafeFiles.Canonical(NativeHost.DataDirectory).Length + 1)..];
        var record = Path.Combine(NativeHost.DataDirectory, "ownership", relative + ".owner");
        var parents = SafeFiles.LockParents(record);
        try
        {
            using var handle = SafeFiles.Open(record, false, false);
            using var copy = new Microsoft.Win32.SafeHandles.SafeFileHandle(handle.DangerousGetHandle(), false);
            using var reader = new BinaryReader(new FileStream(copy, FileAccess.Read));
            return reader.BaseStream.Length == 16 && reader.ReadUInt32() == 0x314F4254 && reader.ReadUInt32() == entry.Volume && reader.ReadUInt32() == entry.High && reader.ReadUInt32() == entry.Low;
        }
        finally { foreach (var handle in parents) handle.Dispose(); }
    }
}
