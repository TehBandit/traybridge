using Microsoft.Win32;
using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Serialization;
using TrayBridge.UninstallSupport;

namespace TrayBridge;

internal static class Uninstaller
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private static string ResumeCommand(string script) => CleanupRecovery.Command(script);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetNamedPipeClientProcessId(Microsoft.Win32.SafeHandles.SafePipeHandle pipe, out uint id);

    internal static string Resource(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("TrayBridge." + name) ?? throw new InvalidOperationException("Uninstall is available in releases published with build.ps1 -Publish.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
    internal static RemovalPlan Preview()
    {
        using var reader = XmlReader.Create(new StringReader(Resource("UninstallRelease.xml")), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        var inventory = (ReleaseInventory)new XmlSerializer(typeof(ReleaseInventory)).Deserialize(reader)!;
        var executable = SafeFiles.Canonical(Environment.ProcessPath!);
        var release = Path.GetDirectoryName(executable)!;
        if (Path.GetFileName(executable) != "TrayBridge.exe" || !File.Exists(Path.Combine(release, "TrayBridge.Native.dll"))
            || Directory.Exists(Path.Combine(release, ".git")) || File.Exists(Path.Combine(release, "TrayBridge.csproj"))) throw new InvalidOperationException("Program removal is supported only for a published TrayBridge release.");
        var plan = new RemovalPlan { Release = release, Data = SafeFiles.Canonical(NativeHost.DataDirectory), Operation = Guid.NewGuid().ToString("N"), ParentId = Environment.ProcessId, ParentStart = Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks };
        plan.Workspace = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TrayBridge.Uninstall." + plan.Operation);
        plan.Roots.Add(SafeFiles.Capture(release, "release", true));
        plan.Files.Add(SafeFiles.Capture(executable, "release", false));
        foreach (var file in inventory.Files.Where(f => !f.Runtime))
        {
            if (!SafeFiles.ReleaseNames.Contains(file.Name, StringComparer.OrdinalIgnoreCase) || file.Name == "TrayBridge.exe") throw new IOException("Invalid compiled release inventory.");
            AddVerified(plan, Path.Combine(release, file.Name), "release", e => e.Hash == file.Hash);
        }
        // Do not infer ownership of neighbors from their name or location.
        foreach (var path in Directory.EnumerateFileSystemEntries(release))
            if (!plan.Files.Any(e => String.Equals(e.Path, path, StringComparison.OrdinalIgnoreCase))) plan.Preserved.Add(path);
        plan.Roots.Add(SafeFiles.Capture(plan.Data, "data", true));
        VisitData(plan, plan.Data);
        InspectOwnership(plan, Path.Combine(plan.Data, "ownership"));
        using var current = Process.GetCurrentProcess();
        var clr = current.Modules.Cast<ProcessModule>().FirstOrDefault(m => String.Equals(m.ModuleName, "coreclr.dll", StringComparison.OrdinalIgnoreCase));
        if (clr is not null)
        {
            var runtime = Path.GetDirectoryName(clr.FileName)!;
            var cache = Path.Combine(Path.GetTempPath(), ".net", "TrayBridge");
            if (SafeFiles.Within(runtime, cache))
            {
                plan.Runtime = SafeFiles.Canonical(runtime);
                plan.Roots.Add(SafeFiles.Capture(runtime, "runtime", true));
                foreach (var path in Directory.EnumerateFileSystemEntries(runtime))
                {
                    var expected = inventory.Files.FirstOrDefault(f => f.Runtime && f.Name.Equals(Path.GetFileName(path), StringComparison.OrdinalIgnoreCase));
                    if (expected is null) plan.Preserved.Add(path);
                    else AddVerified(plan, path, "runtime", e => e.Hash == expected.Hash);
                }
            }
            else if (SafeFiles.Within(runtime, Path.GetTempPath())) plan.Preserved.Add(runtime + " (unverified runtime extraction location)");
        }
        return plan;
    }
    private static bool IsMissing(Exception error) => error is Win32Exception { NativeErrorCode: 2 or 3 };
    private static void AddVerified(RemovalPlan plan, string path, string category, Func<RemovalEntry, bool> owns)
    {
        try
        {
            var entry = SafeFiles.Capture(path, category, false);
            if (owns(entry)) plan.Files.Add(entry); else plan.Preserved.Add(path);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or Win32Exception)
        {
            if (!IsMissing(error)) plan.Preserved.Add(path + " (" + error.Message + ")");
        }
    }
    private static void VisitData(RemovalPlan plan, string directory, int depth = 0)
    {
        if (depth > 8 || plan.Files.Count + plan.Preserved.Count > 10000) throw new IOException("Uninstall inventory limit reached; this folder was preserved.");
        // Pin each directory while enumerating it. Never recurse through a link.
        var parents = SafeFiles.LockParents(directory);
        try
        {
            using var handle = SafeFiles.Open(directory, true, false);
            foreach (var path in Directory.EnumerateFileSystemEntries(directory))
            {
                var relative = path[(plan.Data.Length + 1)..];
                if (relative.Equals("ownership", StringComparison.OrdinalIgnoreCase)) continue;
                var attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReparsePoint) != 0) { plan.Preserved.Add(path + " (link preserved)"); continue; }
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    if (!relative.Equals("native", StringComparison.OrdinalIgnoreCase) && !relative.Equals("symbols", StringComparison.OrdinalIgnoreCase) && !relative.StartsWith("symbols\\", StringComparison.OrdinalIgnoreCase)) { plan.Preserved.Add(path); continue; }
                    try { var before = plan.Files.Count; VisitData(plan, path, depth + 1); if (plan.Files.Count == before) plan.Preserved.Add(path + " (unverified folder)"); }
                    catch (Exception error) when (error is IOException or Win32Exception or UnauthorizedAccessException) { plan.Preserved.Add(path + " (" + error.Message + ")"); }
                }
                else if (!SafeFiles.DataName(relative)) plan.Preserved.Add(path);
                else
                {
                    var before = plan.Files.Count;
                    AddVerified(plan, path, "data", entry => FileOwnership.Matches(path, entry));
                    if (plan.Files.Count > before)
                    {
                        var record = Path.Combine(plan.Data, "ownership", relative + ".owner");
                        plan.Files.Add(SafeFiles.Capture(record, "ownership", false));
                    }
                }
            }
        }
        finally { foreach (var handle in parents) handle.Dispose(); }
    }
    private static void AddDirectories(RemovalPlan plan)
    {
        // Only ancestors of verified files are eligible for empty-directory removal.
        foreach (var file in plan.Files.Where(e => e.Category is "data" or "ownership").ToArray())
            for (var directory = Path.GetDirectoryName(file.Path)!; !directory.Equals(plan.Data, StringComparison.OrdinalIgnoreCase); directory = Path.GetDirectoryName(directory)!)
                if (!plan.Roots.Any(e => e.Path.Equals(directory, StringComparison.OrdinalIgnoreCase))) plan.Roots.Add(SafeFiles.Capture(directory, file.Category, true));
    }
    private static void InspectOwnership(RemovalPlan plan, string directory, int depth = 0)
    {
        try
        {
            if (depth > 8 || plan.Files.Count + plan.Preserved.Count > 10000) throw new IOException("Uninstall inventory limit reached; this folder was preserved.");
            var parents = SafeFiles.LockParents(directory);
            try
            {
                using var handle = SafeFiles.Open(directory, true, false);
                foreach (var path in Directory.EnumerateFileSystemEntries(directory))
                {
                    var attributes = File.GetAttributes(path);
                    if ((attributes & FileAttributes.ReparsePoint) != 0) plan.Preserved.Add(path + " (link preserved)");
                    else if ((attributes & FileAttributes.Directory) != 0) InspectOwnership(plan, path, depth + 1);
                    else if (!plan.Files.Any(e => e.Path.Equals(path, StringComparison.OrdinalIgnoreCase))) plan.Preserved.Add(path);
                }
            }
            finally { foreach (var handle in parents) handle.Dispose(); }
        }
        catch (Exception error) when (error is IOException or Win32Exception or UnauthorizedAccessException)
        {
            if (!IsMissing(error)) plan.Preserved.Add(directory + " (" + error.Message + ")");
        }
    }
    internal static ProcessStartInfo CleanupStart(string script)
    {
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe"))
        { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows), RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script }) start.ArgumentList.Add(argument);
        return start;
    }
    private static string CleanupSource()
    {
        var units = new[] { Resource("UninstallSupport.cs"), Resource("CleanupWorker.cs") };
        var usings = units.SelectMany(unit => unit.Split('\n').Where(line => line.StartsWith("using "))).Distinct();
        var bodies = units.Select(unit => string.Join("\n", unit.Split('\n').Where(line => !line.StartsWith("using "))));
        return string.Join("\n", usings) + "\n" + string.Join("\n", bodies);
    }
    internal static async Task WaitForCleanupReady(NamedPipeServerStream pipe, Process process)
    {
        var errors = process.StandardError.ReadToEndAsync(); var stdout = process.StandardOutput.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            var connection = pipe.WaitForConnectionAsync(timeout.Token);
            var exit = process.WaitForExitAsync(timeout.Token);
            if (await Task.WhenAny(connection, exit) == exit && !pipe.IsConnected)
                throw new IOException("Cleanup host exited before it was ready: " + (await errors).Trim());
            await connection;
            if (!GetNamedPipeClientProcessId(pipe.SafePipeHandle, out var client) || client != process.Id) throw new IOException("Cleanup process identity did not match.");
            var ready = new byte[1];
            if (await pipe.ReadAsync(ready, timeout.Token) != 1 || ready[0] != 1) throw new IOException("Cleanup did not acknowledge readiness.");
        }
        catch (OperationCanceledException) { throw new IOException("The cleanup host did not become ready within 30 seconds. Files were preserved."); }
        finally { timeout.Cancel(); }
    }
    internal static async Task Preflight(RemovalPlan plan)
    {
        if (ResumeCommand(Path.Combine(plan.Workspace, "cleanup.ps1")).Length + " -Attempt 3".Length > 260)
            throw new IOException("This profile's cleanup startup command exceeds Windows' supported length. Uninstall has not changed your taskbars or sign-in preference.");
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe"))
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-Command", "$ErrorActionPreference='Stop'; try { if ($ExecutionContext.SessionState.LanguageMode -ne 'FullLanguage') { throw 'FullLanguage is required for TrayBridge cleanup.' }; $source=[Console]::In.ReadToEnd(); Add-Type -TypeDefinition $source -ReferencedAssemblies System.Windows.Forms, System.Drawing, System.Xml, System.Core, System.Security; exit 0 } catch { [Console]::Error.WriteLine($_.Exception.Message); exit 1 }" }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new IOException("The Windows cleanup host could not be started.");
        var errors = process.StandardError.ReadToEndAsync(); var output = process.StandardOutput.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            await process.StandardInput.WriteAsync(CleanupSource().AsMemory(), timeout.Token); process.StandardInput.Close();
            await process.WaitForExitAsync(timeout.Token);
            if (process.ExitCode != 0) throw new IOException("Windows PowerShell cannot run TrayBridge cleanup: " + (await errors).Trim() + " Uninstall has not changed your taskbars or sign-in preference.");
            _ = await output;
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(true);
            throw new IOException("The cleanup capability check timed out. Uninstall has not changed your taskbars or sign-in preference.");
        }
    }
    internal static string StartupWarning()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        var command = key?.GetValue("TrayBridge", null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
        return command is null || MatchesStartup(command) ? "" : "\r\nA sign-in entry targeting a different copy will be preserved.";
    }
    private static bool MatchesStartup(string command) => String.Equals(command, $"\"{Environment.ProcessPath}\" --hidden", StringComparison.OrdinalIgnoreCase);
    internal static void RemoveStartup()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, true);
        if (key?.GetValue("TrayBridge", null, RegistryValueOptions.DoNotExpandEnvironmentNames) is string command && MatchesStartup(command))
        {
            key.DeleteValue("TrayBridge", false);
            if (key.GetValue("TrayBridge") is not null) throw new IOException("TrayBridge's sign-in entry could not be removed.");
        }
    }
    internal static async Task Launch(RemovalPlan preview)
    {
        // Rebuild mutable app-data snapshots after restoration, while keeping the approved release identities.
        var plan = Preview();
        plan.Operation = preview.Operation; plan.Workspace = preview.Workspace;
        plan.Files.RemoveAll(e => e.Category == "release");
        plan.Files.AddRange(preview.Files.Where(e => e.Category == "release"));
        plan.Roots.RemoveAll(e => e.Category == "release");
        plan.Roots.AddRange(preview.Roots.Where(e => e.Category == "release"));
        var locks = SafeFiles.LockParents(plan.Workspace);
        var rollback = new List<RemovalEntry>();
        string? registeredCommand = null;
        string? registeredReceipt = null;
        try
        {
            if (Directory.Exists(plan.Workspace)) throw new IOException("Uninstall workspace already exists.");
            SafeFiles.CreatePrivateWorkspace(plan.Workspace);
            plan.Roots.Add(SafeFiles.Capture(plan.Workspace, "workspace", true));
            rollback.Add(plan.Roots[^1]);
            using var workspacePin = SafeFiles.Open(plan.Workspace, true, false);
            SafeFiles.Match(workspacePin, plan.Roots[^1]);
            var script = Path.Combine(plan.Workspace, "cleanup.ps1");
            using var signingKey = RSA.Create(2048);
            var publicKey = signingKey.ExportParameters(false);
            var modulus = Convert.ToBase64String(publicKey.Modulus!);
            var exponent = Convert.ToBase64String(publicKey.Exponent!);
            // Source is compiled into the app. Paths/commands are passed as arguments, never interpolated into code.
            // Put all using directives first, followed by both namespaces, for the in-memory compiler.
            var combined = Convert.ToBase64String(Encoding.UTF8.GetBytes(CleanupSource()));
            var contents = "param([string]$PipeName = '', [int]$Attempt = 0, [int]$WaitForParent = 0, [long]$ParentStarted = 0, [string]$RecoveryPipe = '')\r\n$ErrorActionPreference = 'Stop'\r\n" +
                "$source = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('" + combined + "'))\r\n" +
                "Add-Type -TypeDefinition $source -ReferencedAssemblies System.Windows.Forms, System.Drawing, System.Xml, System.Core, System.Security\r\n" +
                "[TrayBridge.Cleanup.Worker]::Run($PSCommandPath, $PipeName, '" + modulus + "', '" + exponent + "', $Attempt, $WaitForParent, $ParentStarted, $RecoveryPipe)\r\n";
            using (var output = new FileStream(script, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(output, new UTF8Encoding(true))) writer.Write(contents);
            plan.Files.Add(SafeFiles.Capture(script, "workspace", false));
            rollback.Add(plan.Files[^1]);
            plan.RecoveryReceipt = CleanupRecovery.Seal(new CleanupReceipt { Operation = plan.Operation, Modulus = modulus, Exponent = exponent, Script = plan.Files[^1], Workspace = plan.Roots.Last(entry => entry.Category == "workspace") });
            AddDirectories(plan);
            SafeFiles.Validate(plan, plan.Workspace);
            var state = Path.Combine(plan.Workspace, "plan.xml");
            using var payload = new MemoryStream();
            new XmlSerializer(typeof(RemovalPlan)).Serialize(payload, plan);
            var bytes = payload.ToArray();
            SafeFiles.WriteXml(state, new RemovalApproval { Payload = bytes, Signature = signingKey.SignData(bytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1) });
            var stateEntry = SafeFiles.Capture(state, "workspace", false);
            rollback.Add(stateEntry);
            var pipeName = "TrayBridge.Uninstall." + plan.Operation;
            using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            var start = CleanupStart(script);
            start.ArgumentList.Add("-PipeName"); start.ArgumentList.Add(pipeName);
            using var process = Process.Start(start) ?? throw new IOException("Could not start the uninstall progress window.");
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                await WaitForCleanupReady(pipe, process);
                RemoveStartup();
                var command = ResumeCommand(script);
                if (command.Length + " -Attempt 3".Length > 260) throw new IOException("The cleanup startup command exceeds Windows' supported length. Program files were preserved.");
                CleanupRecovery.RegisterReceipt(plan.Operation, plan.RecoveryReceipt);
                registeredReceipt = plan.RecoveryReceipt;
                using (var key = Registry.CurrentUser.CreateSubKey(RunKey, true))
                {
                    var registration = "TrayBridge.Uninstall." + plan.Operation;
                    if (key.GetValue(registration) is not null) throw new IOException("This cleanup registration already exists; it was preserved.");
                    key.SetValue(registration, command, RegistryValueKind.String);
                    registeredCommand = command;
                    if (!String.Equals(key.GetValue(registration) as string, command, StringComparison.Ordinal)) throw new IOException("Cleanup registration could not be verified.");
                }
                await pipe.WriteAsync(new byte[] { 1 }, timeout.Token); await pipe.FlushAsync(timeout.Token);
            }
            catch
            {
                // Disconnect without committing. The worker's protocol prevents deletion.
                pipe.Dispose();
                throw;
            }
        }
        catch
        {
            if (registeredCommand is not null)
            {
                try
                {
                    using var key = Registry.CurrentUser.OpenSubKey(RunKey, true);
                    var name = "TrayBridge.Uninstall." + plan.Operation;
                    if (String.Equals(key?.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string, registeredCommand, StringComparison.Ordinal)) key?.DeleteValue(name, false);
                }
                catch (Exception) { /* Preserve inaccessible/replaced registration, never broaden rollback. */ }
            }
            if (registeredReceipt is not null)
                try { CleanupRecovery.RemoveReceipt(CleanupRecovery.Prefix + plan.Operation, registeredReceipt); } catch (Exception) { }
            // Clean only exact artifacts created by this attempt; no recursive workspace deletion.
            foreach (var entry in rollback.OrderBy(e => e.Directory))
                try { SafeFiles.Delete(entry); } catch (Exception) { }
            throw;
        }
        finally { foreach (var handle in locks) handle.Dispose(); }
    }
}
