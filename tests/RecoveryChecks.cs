using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Xml.Serialization;
using TrayBridge;
using TrayBridge.UninstallSupport;

internal static class RecoveryChecks
{
    internal static int Run(string fixtureRoot)
    {
        int passed = 0;
        void Check(bool value, string name) { if (!value) throw new Exception(name); passed++; Console.WriteLine("PASS " + name); }
        Check(PlatformSupport.Supports(new Version(10, 0, 26200), 9457, Architecture.X64), "validated x64 build is supported");
        foreach (var configuration in new[] { (new Version(10, 0, 26200), 9458, Architecture.X64), (new Version(10, 0, 26100), 9457, Architecture.X64), (new Version(10, 0, 26200), 9457, Architecture.Arm64), (new Version(10, 0, 26200), 9457, Architecture.X86) })
            Check(!PlatformSupport.Supports(configuration.Item1, configuration.Item2, configuration.Item3), "untested build/architecture is rejected " + configuration);
        var local = Path.Combine(fixtureRoot, "recovery-profile"); Directory.CreateDirectory(local);
        var operation = Guid.NewGuid().ToString("N"); var name = CleanupRecovery.Prefix + operation;
        var workspace = Path.Combine(local, name); SafeFiles.CreatePrivateWorkspace(workspace);
        var scriptPath = Path.Combine(workspace, "cleanup.ps1"); File.WriteAllText(scriptPath, "verified inert fixture");
        var script = SafeFiles.Capture(scriptPath, "workspace", false);
        var workspaceEntry = SafeFiles.Capture(workspace, "workspace", true);
        using var rsa = RSA.Create(2048); var key = rsa.ExportParameters(false);
        var receipt = new CleanupReceipt { Operation = operation, Script = script, Workspace = workspaceEntry, Modulus = Convert.ToBase64String(key.Modulus!), Exponent = Convert.ToBase64String(key.Exponent!) };
        var sealedReceipt = CleanupRecovery.Seal(receipt);
        Check(CleanupRecovery.ReadReceipt(sealedReceipt, name, local).Script.Hash == script.Hash, "DPAPI recovery receipt round-trips for this account");
        var protectedBytes = Convert.FromBase64String(sealedReceipt); protectedBytes[^5] ^= 1;
        var tampered = CleanupRecovery.Inspect(name, CleanupRecovery.Command(scriptPath), Convert.ToBase64String(protectedBytes), local);
        Check(!tampered.Verified && !tampered.BlocksStartup && !tampered.Retryable, "tampered receipt cannot authorize retry or block startup");
        var unknown = CleanupRecovery.Inspect(name, "unrelated-command", null, local);
        Check(!unknown.Verified && !unknown.BlocksStartup && !unknown.Retryable, "prefix-only registration cannot permanently block app startup");
        var replaced = CleanupRecovery.Inspect(name, "replaced-command", sealedReceipt, local);
        Check(!replaced.Verified && !replaced.Retryable, "replaced registry command is preserved and cannot be retried");
        Check(Enumerable.Range(0, 4).All(attempt => CleanupRecovery.MatchesCommand(receipt, CleanupRecovery.Command(scriptPath, attempt))), "only the four exact cleanup retry commands are recognized");
        Check(!CleanupRecovery.MatchesCommand(receipt, CleanupRecovery.Command(scriptPath, 4)), "out-of-range retry command cannot become verified");
        var mismatched = CleanupRecovery.Inspect(CleanupRecovery.Prefix + Guid.NewGuid().ToString("N"), null, sealedReceipt, local);
        Check(!mismatched.Verified, "receipt cannot be replayed under a different operation");
        var release = Path.Combine(local, "release"); Directory.CreateDirectory(release);
        var data = Path.Combine(local, "TrayBridge"); Directory.CreateDirectory(data);
        var plan = new RemovalPlan { Release = release, Data = data, Workspace = workspace, Operation = operation, RecoveryReceipt = sealedReceipt };
        plan.Files.Add(script);
        plan.Roots.AddRange([SafeFiles.Capture(release, "release", true), SafeFiles.Capture(data, "data", true), workspaceEntry]);
        var state = Path.Combine(workspace, "plan.xml");
        using var payload = new MemoryStream(); new XmlSerializer(typeof(RemovalPlan)).Serialize(payload, plan); var bytes = payload.ToArray();
        SafeFiles.WriteXml(state, new RemovalApproval { Payload = bytes, Signature = rsa.SignData(bytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1) });
        var valid = CleanupRecovery.Inspect(name, CleanupRecovery.Command(scriptPath), sealedReceipt, local);
        Check(valid.Verified && valid.Retryable && valid.BlocksStartup && valid.Details.Contains(scriptPath), "valid receipt and signed plan expose approved paths and permit retry");
        var validStopped = CleanupRecovery.Inspect(name, null, sealedReceipt, local);
        Check(validStopped.Retryable && !validStopped.BlocksStartup, "stopped verified operation remains retryable");
        var approvedBytes = File.ReadAllBytes(state);
        var badApproval = SafeFiles.ReadXml<RemovalApproval>(state); badApproval.Signature[0] ^= 1;
        using (var file = File.Create(state)) new XmlSerializer(typeof(RemovalApproval)).Serialize(file, badApproval);
        var badPlan = CleanupRecovery.Inspect(name, CleanupRecovery.Command(scriptPath), sealedReceipt, local);
        Check(badPlan.Verified && !badPlan.Retryable && badPlan.BlocksStartup, "tampered plan cannot authorize recovery deletion");
        File.WriteAllBytes(state, approvedBytes);
        Check(!CleanupRecovery.MatchesCommand(receipt, CleanupRecovery.RegistrationCommand(123)), "non-string registry replacement cannot be treated as an absent command");
        var fixtureKey = @"Software\TrayBridge.Uninstall.Tests\" + Guid.NewGuid().ToString("N");
        var receiptKey = fixtureKey + @"\Receipts"; var runKey = fixtureKey + @"\Run";
        try
        {
            using (var records = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(receiptKey)) records.SetValue(name, sealedReceipt);
            using (var run = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(runKey))
            {
                run.SetValue(name, CleanupRecovery.Command(scriptPath)); run.SetValue("Neighbor", "registry sentinel");
            }
            CleanupRecovery.Stop(valid, receiptKey, runKey);
            using (var run = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(runKey))
                Check(run!.GetValue(name) is null && (string)run.GetValue("Neighbor")! == "registry sentinel", "recovery stops only the exact verified registration and preserves neighbors");
            using (var records = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(receiptKey))
                Check((string)records!.GetValue(name)! == sealedReceipt, "stopping cleanup preserves its receipt for inspection and retry");
            using (var run = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(runKey, true)) run!.SetValue(name, "changed command");
            try { CleanupRecovery.Stop(valid, receiptKey, runKey); throw new Exception("Replaced command accepted"); }
            catch (IOException) { Check(true, "registration replacement between inspection and stop is refused"); }
            using (var run = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(runKey))
                Check((string)run!.GetValue(name)! == "changed command", "recovery leaves a changed registration untouched");
        }
        finally
        {
            using (var run = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(runKey, true)) { run?.DeleteValue(name, false); run?.DeleteValue("Neighbor", false); }
            using (var records = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(receiptKey, true)) records?.DeleteValue(name, false);
            Microsoft.Win32.Registry.CurrentUser.DeleteSubKey(runKey, false); Microsoft.Win32.Registry.CurrentUser.DeleteSubKey(receiptKey, false);
            Microsoft.Win32.Registry.CurrentUser.DeleteSubKey(fixtureKey, false);
        }
        File.WriteAllText(scriptPath, "edited script");
        var edited = CleanupRecovery.Inspect(name, CleanupRecovery.Command(scriptPath), sealedReceipt, local);
        Check(edited.Verified && edited.BlocksStartup && !edited.Retryable, "edited script retains verified recovery identity but cannot execute");
        File.Delete(scriptPath);
        var missing = CleanupRecovery.Inspect(name, CleanupRecovery.Command(scriptPath), sealedReceipt, local);
        Check(missing.Verified && missing.BlocksStartup && !missing.Retryable, "missing script can be identified for explicit registration recovery");
        var stopped = CleanupRecovery.Inspect(name, null, sealedReceipt, local);
        Check(stopped.Verified && !stopped.BlocksStartup && !stopped.Retryable, "stopped cleanup allows startup while preserving recovery information");
        // Use the actual source/host capability check without invoking any cleanup.
        var shortPlan = new RemovalPlan { Workspace = Path.Combine(fixtureRoot, "p") };
        Uninstaller.Preflight(shortPlan).GetAwaiter().GetResult();
        Check(true, "actual embedded cleanup source compiles in Windows PowerShell before uninstall");
        using (var failurePipe = new System.IO.Pipes.NamedPipeServerStream("TrayBridge.ReadinessFixture." + Guid.NewGuid().ToString("N"), System.IO.Pipes.PipeDirection.InOut, 1,
            System.IO.Pipes.PipeTransmissionMode.Byte, System.IO.Pipes.PipeOptions.Asynchronous | System.IO.Pipes.PipeOptions.CurrentUserOnly))
        {
            var start = Uninstaller.CleanupStart(""); start.ArgumentList.Clear();
            foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-Command", "[Console]::Error.WriteLine('fixture compilation failure'); exit 7" }) start.ArgumentList.Add(argument);
            using var child = Process.Start(start)!;
            try { Uninstaller.WaitForCleanupReady(failurePipe, child).GetAwaiter().GetResult(); throw new Exception("Failed cleanup host accepted"); }
            catch (IOException error) { Check(error.Message.Contains("fixture compilation failure"), "readiness check surfaces the actual host failure before commit"); }
        }
        var readyName = "TrayBridge.ReadinessFixture." + Guid.NewGuid().ToString("N");
        using (var readyPipe = new System.IO.Pipes.NamedPipeServerStream(readyName, System.IO.Pipes.PipeDirection.InOut, 1,
            System.IO.Pipes.PipeTransmissionMode.Byte, System.IO.Pipes.PipeOptions.Asynchronous | System.IO.Pipes.PipeOptions.CurrentUserOnly))
        {
            var start = Uninstaller.CleanupStart(""); start.ArgumentList.Clear();
            // The only interpolated text is our hexadecimal test GUID/name.
            foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-Command", "$p=[IO.Pipes.NamedPipeClientStream]::new('.', '" + readyName + "', [IO.Pipes.PipeDirection]::InOut); $p.Connect(10000); $p.WriteByte(1); $p.Flush(); $null=$p.ReadByte(); $p.Dispose()" }) start.ArgumentList.Add(argument);
            using var child = Process.Start(start)!;
            Uninstaller.WaitForCleanupReady(readyPipe, child).GetAwaiter().GetResult();
            readyPipe.WriteByte(1); readyPipe.Flush();
            Check(child.WaitForExit(10000) && child.ExitCode == 0, "readiness checks the actual child identity and acknowledgment");
        }
        try { Uninstaller.Preflight(new RemovalPlan { Workspace = new string('x', 300) }).GetAwaiter().GetResult(); throw new Exception("Long command accepted"); }
        catch (IOException error) { Check(error.Message.Contains("has not changed"), "long profile command is refused before changing taskbars/startup"); }
        File.Delete(state); Directory.Delete(workspace); Directory.Delete(data); Directory.Delete(release); Directory.Delete(local);
        return passed;
    }
}
