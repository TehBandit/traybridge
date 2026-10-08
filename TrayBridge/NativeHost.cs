using System.ComponentModel;
using System.Diagnostics;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace TrayBridge;

internal static class NativeHost
{
    public static string DataDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TrayBridge");
    public static string Distribution => File.Exists(Path.Combine(AppContext.BaseDirectory, "TrayBridge.Native.dll")) ? AppContext.BaseDirectory : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "dist"));
    private static Process Shell => Process.GetProcessesByName("explorer").First(p => p.SessionId == Process.GetCurrentProcess().SessionId);

    public static async Task PrepareSymbols(Action<string>? progress = null, CancellationToken cancellation = default)
    {
        Directory.CreateDirectory(Path.Combine(DataDirectory, "symbols"));
        using var shell = Shell;
        var paths = shell.Modules.Cast<ProcessModule>().Where(m => m.ModuleName.Equals("taskbar.dll", StringComparison.OrdinalIgnoreCase) || m.ModuleName.Equals("SystemTray.dll", StringComparison.OrdinalIgnoreCase) || m.ModuleName.Equals("twinui.pcshell.dll", StringComparison.OrdinalIgnoreCase)).Select(m => m.FileName).ToArray();
        if (paths.Length < 2) throw new InvalidOperationException("The expected native Windows taskbar components are not loaded.");
        using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        foreach (var path in paths)
        {
            cancellation.ThrowIfCancellationRequested();
            using var file = File.OpenRead(path);
            using var pe = new PEReader(file);
            var entries = pe.ReadDebugDirectory();
            var entry = entries.First(e => e.Type == DebugDirectoryEntryType.CodeView);
            var codeView = pe.ReadCodeViewDebugDirectoryData(entry);
            var pdbName = Path.GetFileName(codeView.Path);
            var identity = codeView.Guid.ToString("N").ToUpperInvariant() + codeView.Age.ToString("X");
            var pdbDirectory = Path.Combine(DataDirectory, "symbols", pdbName, identity);
            Directory.CreateDirectory(pdbDirectory);
            var pdb = Path.Combine(pdbDirectory, pdbName);
            if (!File.Exists(pdb))
            {
                progress?.Invoke("Downloading Microsoft symbols for " + Path.GetFileName(path));
                using var response = await client.GetAsync($"https://msdl.microsoft.com/download/symbols/{pdbName}/{identity}/{pdbName}", HttpCompletionOption.ResponseHeadersRead, cancellation);
                response.EnsureSuccessStatusCode();
                var temp = pdb + ".tmp";
                try { await using (var output = File.Create(temp)) await response.Content.CopyToAsync(output, cancellation); File.Move(temp, pdb, true); }
                finally { if (File.Exists(temp)) File.Delete(temp); }
            }
            progress?.Invoke("Indexing " + Path.GetFileName(path));
            var mapPath = Path.Combine(DataDirectory, "symbols", Path.GetFileName(path) + ".symmap");
            var start = new ProcessStartInfo(Path.Combine(Distribution, "TrayBridge.Symbols.exe")) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
            start.ArgumentList.Add(path); start.ArgumentList.Add(pdbDirectory); start.ArgumentList.Add(mapPath + ".tmp");
            using var helper = Process.Start(start) ?? throw new InvalidOperationException("Could not start the symbol indexer.");
            var error = helper.StandardError.ReadToEndAsync(cancellation);
            var stdout = helper.StandardOutput.ReadToEndAsync(cancellation);
            await helper.WaitForExitAsync(cancellation);
            if (helper.ExitCode != 0) throw new InvalidOperationException("Symbol indexing failed: " + await error);
            _ = await stdout;
            File.Move(mapPath + ".tmp", mapPath, true);
        }
        progress?.Invoke("Matching Windows symbols are ready.");
    }

    public static uint Probe() { Directory.CreateDirectory(DataDirectory); using var process = Shell; return Invoke(process, "TrayBridgeInspect", DataDirectory); }
    public static uint Start()
    {
        using var process = Shell;
        var code = Invoke(process, "TrayBridgeInitialize", DataDirectory);
        if (code != 0) return code;
        foreach (var host in Process.GetProcessesByName("ShellHost")) using (host)
            if (host.SessionId == Process.GetCurrentProcess().SessionId) Invoke(host, "TrayBridgeInitialize", DataDirectory);
        return code;
    }
    public static void Stop()
    {
        using var process = Shell;
        var code = Invoke(process, "TrayBridgeStop");
        if (code != 0) throw new InvalidOperationException("The shell is still restoring its taskbars. Try Disable again.");
        foreach (var host in Process.GetProcessesByName("ShellHost")) using (host)
            if (host.SessionId == Process.GetCurrentProcess().SessionId) Invoke(host, "TrayBridgeStop");
    }
    public static uint Invoke(Process process, string export, string? argument = null)
    {
        if (process.SessionId != Process.GetCurrentProcess().SessionId || process.ProcessName is not ("explorer" or "ShellHost")) throw new InvalidOperationException("Only the current user's desktop shell is supported.");
        var library = Path.Combine(Distribution, "TrayBridge.Native.dll");
        var fingerprint = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(library)))[..16];
        var loadedPath = Path.Combine(DataDirectory, "native", $"TrayBridge.Native.{fingerprint}.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(loadedPath)!);
        if (!File.Exists(loadedPath)) File.Copy(library, loadedPath);
        var handle = OpenProcess(0x043A, false, process.Id);
        if (handle == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            process.Refresh();
            var remote = process.Modules.Cast<ProcessModule>().FirstOrDefault(m => string.Equals(m.FileName, loadedPath, StringComparison.OrdinalIgnoreCase));
            if (remote is null)
            {
                var localKernel = GetModuleHandle("kernel32.dll");
                var loadAddress = GetProcAddress(localKernel, "LoadLibraryW");
                if (!GetModuleHandleEx(6, loadAddress, out var containingModule)) throw new Win32Exception(Marshal.GetLastWin32Error());
                var moduleName = new StringBuilder(32768); GetModuleFileName(containingModule, moduleName, moduleName.Capacity);
                var remoteModule = process.Modules.Cast<ProcessModule>().First(m => m.ModuleName.Equals(Path.GetFileName(moduleName.ToString()), StringComparison.OrdinalIgnoreCase));
                var loader = remoteModule.BaseAddress + (loadAddress - containingModule);
                RunRemote(handle, loader, loadedPath);
                process.Refresh();
                remote = process.Modules.Cast<ProcessModule>().FirstOrDefault(m => string.Equals(m.FileName, loadedPath, StringComparison.OrdinalIgnoreCase)) ?? throw new InvalidOperationException("The shell could not load the native helper.");
            }
            var local = LoadLibraryEx(loadedPath, IntPtr.Zero, 1);
            if (local == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            try
            {
                var function = GetProcAddress(local, export);
                if (function == IntPtr.Zero) throw new InvalidOperationException("Native entry point is missing.");
                return RunRemote(handle, remote.BaseAddress + (function - local), argument);
            }
            finally { FreeLibrary(local); }
        }
        finally { CloseHandle(handle); }
    }
    private static uint RunRemote(IntPtr process, IntPtr function, string? value)
    {
        var bytes = value is null ? null : Encoding.Unicode.GetBytes(value + '\0');
        IntPtr argument = IntPtr.Zero;
        bool completed = false, started = false;
        try
        {
            if (bytes is not null)
            {
                argument = VirtualAllocEx(process, IntPtr.Zero, (nuint)bytes.Length, 0x3000, 0x04);
                if (argument == IntPtr.Zero || !WriteProcessMemory(process, argument, bytes, (nuint)bytes.Length, out var written) || written != (nuint)bytes.Length) throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            var thread = CreateRemoteThread(process, IntPtr.Zero, 0, function, argument, 0, out _);
            if (thread == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            started = true;
            try
            {
                var wait = WaitForSingleObject(thread, 30000);
                if (wait != 0) throw new TimeoutException("The native helper did not finish within 30 seconds; it may still be completing shell work.");
                completed = true;
                if (!GetExitCodeThread(thread, out uint code)) throw new Win32Exception(Marshal.GetLastWin32Error());
                return code;
            }
            finally { CloseHandle(thread); }
        }
        finally { if (argument != IntPtr.Zero && (completed || !started)) VirtualFreeEx(process, argument, 0, 0x8000); }
    }
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr LoadLibraryEx(string file, IntPtr reserved, uint flags);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string module);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool GetModuleHandleEx(uint flags, IntPtr address, out IntPtr module);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern uint GetModuleFileName(IntPtr module, StringBuilder name, int capacity);
    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true)] private static extern IntPtr GetProcAddress(IntPtr module, string name);
    [DllImport("kernel32.dll")] private static extern bool FreeLibrary(IntPtr module);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr VirtualAllocEx(IntPtr process, IntPtr address, nuint size, uint allocation, uint protection);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool WriteProcessMemory(IntPtr process, IntPtr address, byte[] data, nuint size, out nuint written);
    [DllImport("kernel32.dll")] private static extern bool VirtualFreeEx(IntPtr process, IntPtr address, nuint size, uint type);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr CreateRemoteThread(IntPtr process, IntPtr attributes, nuint stackSize, IntPtr start, IntPtr argument, uint flags, out uint id);
    [DllImport("kernel32.dll")] private static extern uint WaitForSingleObject(IntPtr handle, uint timeout);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetExitCodeThread(IntPtr thread, out uint code);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
}
