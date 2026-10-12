using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml;
using System.Xml.Serialization;
using Microsoft.Win32;
using TrayBridge.UninstallSupport;

namespace TrayBridge;

public sealed class CleanupReceipt
{
    public string Operation = "", Modulus = "", Exponent = "";
    public RemovalEntry Script = new(), Workspace = new();
}

internal sealed record CleanupOperation(string Name, string? Command, string? ProtectedReceipt, CleanupReceipt? Receipt,
    RemovalPlan? Plan, string Details)
{
    internal bool Verified => Receipt is not null;
    internal bool Retryable => Verified && Plan is not null;
    internal bool BlocksStartup => Verified && Command is not null;
}

internal static class CleanupRecovery
{
    internal const string ReceiptKey = @"Software\TrayBridge\Cleanup";
    internal const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    internal const string Prefix = "TrayBridge.Uninstall.";
    private static readonly byte[] entropy = Encoding.UTF8.GetBytes("TrayBridge cleanup receipt v1");
    [StructLayout(LayoutKind.Sequential)] private struct Blob { public int Length; public IntPtr Data; }
    [DllImport("crypt32.dll", SetLastError = true)] private static extern bool CryptProtectData(ref Blob input, string description, ref Blob entropy, IntPtr reserved, IntPtr prompt, uint flags, out Blob output);
    [DllImport("crypt32.dll", SetLastError = true)] private static extern bool CryptUnprotectData(ref Blob input, IntPtr description, ref Blob entropy, IntPtr reserved, IntPtr prompt, uint flags, out Blob output);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr pointer);
    private static byte[] Protect(byte[] bytes, bool encrypt)
    {
        if (bytes.Length > 65536) throw new IOException("Oversized recovery receipt.");
        var input = new Blob { Length = bytes.Length, Data = Marshal.AllocHGlobal(bytes.Length) };
        var extra = new Blob { Length = entropy.Length, Data = Marshal.AllocHGlobal(entropy.Length) };
        Blob output = default;
        try
        {
            Marshal.Copy(bytes, 0, input.Data, bytes.Length); Marshal.Copy(entropy, 0, extra.Data, entropy.Length);
            var ok = encrypt ? CryptProtectData(ref input, "TrayBridge", ref extra, IntPtr.Zero, IntPtr.Zero, 1, out output)
                : CryptUnprotectData(ref input, IntPtr.Zero, ref extra, IntPtr.Zero, IntPtr.Zero, 1, out output);
            if (!ok) throw new Win32Exception(Marshal.GetLastWin32Error(), "Cleanup recovery receipt could not be verified for this Windows account.");
            var result = new byte[output.Length]; Marshal.Copy(output.Data, result, 0, result.Length); return result;
        }
        finally { Marshal.FreeHGlobal(input.Data); Marshal.FreeHGlobal(extra.Data); if (output.Data != IntPtr.Zero) LocalFree(output.Data); }
    }
    internal static string Seal(CleanupReceipt receipt)
    {
        using var stream = new MemoryStream(); new XmlSerializer(typeof(CleanupReceipt)).Serialize(stream, receipt);
        return Convert.ToBase64String(Protect(stream.ToArray(), true));
    }
    internal static CleanupReceipt ReadReceipt(string sealedReceipt, string name, string local)
    {
        if (sealedReceipt.Length > 90000) throw new IOException("Oversized recovery receipt.");
        using var stream = new MemoryStream(Protect(Convert.FromBase64String(sealedReceipt), false));
        using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 65536 });
        var receipt = (CleanupReceipt)new XmlSerializer(typeof(CleanupReceipt)).Deserialize(reader)!;
        if (!Guid.TryParseExact(receipt.Operation, "N", out _) || name != Prefix + receipt.Operation)
            throw new IOException("Invalid cleanup operation identity.");
        var workspace = SafeFiles.Canonical(Path.Combine(local, name));
        if (receipt.Workspace.Path != workspace || !receipt.Workspace.Directory || receipt.Script.Path != Path.Combine(workspace, "cleanup.ps1") || receipt.Script.Directory)
            throw new IOException("Recovery receipt names unexpected files.");
        return receipt;
    }
    internal static string Command(string script, int attempt = 0) =>
        "\"" + Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe") + "\" -NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"" + script + "\"" + (attempt == 0 ? "" : " -Attempt " + attempt);
    internal static bool MatchesCommand(CleanupReceipt receipt, string? command) => command is null || Enumerable.Range(0, 4).Any(attempt => command == Command(receipt.Script.Path, attempt));
    internal static CleanupOperation Inspect(string name, string? command, string? sealedReceipt, string local)
    {
        CleanupReceipt receipt;
        try
        {
            if (sealedReceipt is null) throw new IOException("This registration has no verified recovery receipt. It will be preserved.");
            receipt = ReadReceipt(sealedReceipt, name, local);
            if (!MatchesCommand(receipt, command)) throw new IOException("The cleanup command was replaced. It will be preserved.");
        }
        catch (Exception error) { return new(name, command, sealedReceipt, null, null, error.Message); }
        try
        {
            using var workspace = SafeFiles.Open(receipt.Workspace.Path, true, false); SafeFiles.Match(workspace, receipt.Workspace);
            using var script = SafeFiles.Open(receipt.Script.Path, false, false); SafeFiles.Match(script, receipt.Script);
            var plan = SafeFiles.ReadApproved(Path.Combine(receipt.Workspace.Path, "plan.xml"), receipt.Modulus, receipt.Exponent);
            SafeFiles.ValidateForProfile(plan, receipt.Workspace.Path, local);
            if (plan.Operation != receipt.Operation || plan.RecoveryReceipt != sealedReceipt) throw new IOException("Cleanup approval does not match its recovery receipt.");
            var approvedScript = plan.Files.Single(entry => entry.Path == receipt.Script.Path && entry.Category == "workspace");
            SafeFiles.Match(script, approvedScript);
            var approvedRoot = plan.Roots.Single(entry => entry.Path == receipt.Workspace.Path && entry.Category == "workspace");
            SafeFiles.Match(workspace, approvedRoot);
            return new(name, command, sealedReceipt, receipt, plan,
                (command is null ? "Automatic cleanup is stopped." : "Cleanup is scheduled for sign-in.") + "\r\n\r\nCleanup folder: " + receipt.Workspace.Path + "\r\n\r\nApproved removal paths (files may already be removed):\r\n" + string.Join("\r\n", plan.Files.Select(entry => entry.Path)));
        }
        catch (Exception error) { return new(name, command, sealedReceipt, receipt, null, "Cleanup cannot run: " + error.Message + "\r\n\r\nCleanup folder: " + receipt.Workspace.Path + "\r\nRemaining files are preserved. You can stop this verified registration and open TrayBridge."); }
    }
    internal static IReadOnlyList<CleanupOperation> Scan()
    {
        using var run = Registry.CurrentUser.OpenSubKey(RunKey);
        using var records = Registry.CurrentUser.OpenSubKey(ReceiptKey);
        var names = (run?.GetValueNames() ?? []).Where(name => name.StartsWith(Prefix, StringComparison.Ordinal))
            .Concat(records?.GetValueNames() ?? []).Distinct().ToArray();
        return names.Select(name => Inspect(name,
            RegistrationCommand(run?.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames)),
            records?.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string,
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData))).ToArray();
    }
    internal static string? RegistrationCommand(object? value) => value is null ? null : value as string ?? "[non-string registration preserved]";
    internal static void RegisterReceipt(string operation, string receipt)
    {
        using var key = Registry.CurrentUser.CreateSubKey(ReceiptKey, true);
        var name = Prefix + operation;
        if (key.GetValue(name) is not null) throw new IOException("Recovery receipt already exists; it was preserved.");
        key.SetValue(name, receipt, RegistryValueKind.String);
        if (key.GetValue(name) as string != receipt) throw new IOException("Recovery receipt could not be saved.");
    }
    internal static void RemoveReceipt(string name, string receipt)
    {
        using var key = Registry.CurrentUser.OpenSubKey(ReceiptKey, true);
        if (key?.GetValue(name) as string == receipt) key.DeleteValue(name, false);
    }
    internal static void Stop(CleanupOperation operation, string receiptKey = ReceiptKey, string runKey = RunKey)
    {
        if (!operation.Verified) throw new IOException("Unverified cleanup registrations are preserved.");
        using var records = Registry.CurrentUser.OpenSubKey(receiptKey);
        if (records?.GetValue(operation.Name) as string != operation.ProtectedReceipt) throw new IOException("Recovery receipt changed; rescan before continuing.");
        using var run = Registry.CurrentUser.OpenSubKey(runKey, true);
        if (RegistrationCommand(run?.GetValue(operation.Name, null, RegistryValueOptions.DoNotExpandEnvironmentNames)) != operation.Command)
            throw new IOException("Cleanup registration changed; rescan before continuing.");
        run?.DeleteValue(operation.Name, false);
        // Keep the verified receipt and files so a stopped operation can still be inspected/retried.
    }
    internal static async Task Retry(CleanupOperation operation)
    {
        if (!operation.Retryable) throw new IOException("Cleanup is not verified or its required files changed.");
        Stop(operation);
        var latest = Inspect(operation.Name, null, operation.ProtectedReceipt, Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
        if (!latest.Retryable) throw new IOException(latest.Details);
        await Uninstaller.Preflight(latest.Plan!);
        // This exact script is pinned against edits and replacement until launch.
        using var parents = new HandleList(SafeFiles.LockParents(latest.Receipt!.Script.Path));
        using var script = SafeFiles.Open(latest.Receipt.Script.Path, false, false); SafeFiles.Match(script, latest.Receipt.Script);
        var start = Uninstaller.CleanupStart(latest.Receipt.Script.Path);
        var pipeName = "TrayBridge.Recovery." + latest.Receipt.Operation;
        using var pipe = new System.IO.Pipes.NamedPipeServerStream(pipeName, System.IO.Pipes.PipeDirection.InOut, 1, System.IO.Pipes.PipeTransmissionMode.Byte,
            System.IO.Pipes.PipeOptions.Asynchronous | System.IO.Pipes.PipeOptions.CurrentUserOnly);
        start.ArgumentList.Add("-RecoveryPipe"); start.ArgumentList.Add(pipeName);
        start.ArgumentList.Add("-Attempt"); start.ArgumentList.Add("1");
        using var parent = Process.GetCurrentProcess();
        start.ArgumentList.Add("-WaitForParent"); start.ArgumentList.Add(parent.Id.ToString());
        start.ArgumentList.Add("-ParentStarted"); start.ArgumentList.Add(parent.StartTime.ToUniversalTime().Ticks.ToString());
        // Register before launch. The worker waits for this controller to exit
        // before acquiring the lifecycle gate or deleting anything.
        using var run = Registry.CurrentUser.CreateSubKey(RunKey, true);
        if (run.GetValue(operation.Name) is not null) throw new IOException("A cleanup registration appeared during retry; it was preserved.");
        run.SetValue(operation.Name, Command(latest.Receipt.Script.Path, 1));
        try
        {
            using var process = Process.Start(start) ?? throw new IOException("Could not launch verified cleanup.");
            await Uninstaller.WaitForCleanupReady(pipe, process);
        }
        catch
        {
            if (run.GetValue(operation.Name) as string == Command(latest.Receipt.Script.Path, 1)) run.DeleteValue(operation.Name, false);
            throw;
        }
    }
    private sealed class HandleList(List<Microsoft.Win32.SafeHandles.SafeFileHandle> handles) : IDisposable
    {
        public void Dispose() { foreach (var handle in handles) handle.Dispose(); }
    }
}
