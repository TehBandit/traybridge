using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Serialization;
using Microsoft.Win32.SafeHandles;
using TrayBridge.UninstallSupport;

internal static class Program
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool CreateHardLink(string link, string target, IntPtr security);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool DeviceIoControl(SafeFileHandle handle, uint code, byte[] input, uint length, IntPtr output, uint outputLength, out uint returned, IntPtr overlapped);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr LoadLibraryEx(string path, IntPtr file, uint flags);
    [DllImport("kernel32.dll", CharSet = CharSet.Ansi)]
    static extern IntPtr GetProcAddress(IntPtr module, string name);
    [DllImport("kernel32.dll")]
    static extern bool FreeLibrary(IntPtr module);

    static int count;
    static readonly List<string> created = [];
    static readonly List<string> directories = [];
    static string root = "";
    static void Check(bool value, string name) { if (!value) throw new Exception(name); count++; Console.WriteLine("PASS " + name); }
    static void Refuses(Action action, string name)
    {
        try { action(); } catch (Exception error) when (error is IOException or Win32Exception or UnauthorizedAccessException) { Check(true, name); return; }
        throw new Exception("Did not refuse: " + name);
    }
    static string FileAt(string name, string contents = "fixture")
    {
        var path = Path.Combine(root, name); File.WriteAllText(path, contents); created.Add(path); return path;
    }
    static string FolderAt(string name)
    {
        var path = Path.Combine(root, name); Directory.CreateDirectory(path); directories.Add(path); return path;
    }
    static void Junction(string link, string target)
    {
        Directory.CreateDirectory(link);
        var substitute = Encoding.Unicode.GetBytes(@"\??\" + target);
        var print = Encoding.Unicode.GetBytes(target);
        var buffer = new byte[16 + substitute.Length + 2 + print.Length + 2];
        BitConverter.GetBytes(0xA0000003u).CopyTo(buffer, 0);
        BitConverter.GetBytes((ushort)(buffer.Length - 8)).CopyTo(buffer, 4);
        BitConverter.GetBytes((ushort)substitute.Length).CopyTo(buffer, 10);
        BitConverter.GetBytes((ushort)(substitute.Length + 2)).CopyTo(buffer, 12);
        BitConverter.GetBytes((ushort)print.Length).CopyTo(buffer, 14);
        substitute.CopyTo(buffer, 16); print.CopyTo(buffer, 18 + substitute.Length);
        using var handle = CreateFile(link, 0x40000000, 0, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero);
        if (handle.IsInvalid || !DeviceIoControl(handle, 0x000900A4, buffer, (uint)buffer.Length, IntPtr.Zero, 0, out _, IntPtr.Zero)) throw new Win32Exception(Marshal.GetLastWin32Error());
    }
    static string Literal(string value) => "@\"" + value.Replace("\"", "\"\"") + "\"";
    static void WorkerFixture(string project)
    {
        var profile = FolderAt("fixture-profile");
        var release = Path.Combine(profile, "release"); Directory.CreateDirectory(release); directories.Add(release);
        var data = Path.Combine(profile, "TrayBridge"); Directory.CreateDirectory(data); directories.Add(data);
        var operation = Guid.NewGuid().ToString("N");
        var workspace = Path.Combine(profile, "TrayBridge.Uninstall." + operation); SafeFiles.CreatePrivateWorkspace(workspace); directories.Add(workspace);
        Refuses(() => SafeFiles.CreatePrivateWorkspace(workspace), "workspace creation cannot claim an existing directory or change its permissions");
        var plan = new RemovalPlan { Release = release, Data = data, Workspace = workspace, Operation = operation };
        var exe = Path.Combine(release, "TrayBridge.exe"); File.WriteAllText(exe, "fake published app fixture"); created.Add(exe);
        var settings = Path.Combine(data, "settings.json"); File.WriteAllText(settings, "{}"); created.Add(settings);
        var locked = Path.Combine(data, "native-settings.json"); File.WriteAllText(locked, "{}"); created.Add(locked);
        var sentinel = Path.Combine(release, "personal.txt"); File.WriteAllText(sentinel, "keep neighbor"); created.Add(sentinel);
        plan.Files.Add(SafeFiles.Capture(exe, "release", false)); plan.Files.Add(SafeFiles.Capture(settings, "data", false)); plan.Files.Add(SafeFiles.Capture(locked, "data", false));
        plan.Roots.Add(SafeFiles.Capture(release, "release", true)); plan.Roots.Add(SafeFiles.Capture(data, "data", true)); plan.Roots.Add(SafeFiles.Capture(workspace, "workspace", true));
        plan.Preserved.Add(sentinel);
        using var rsa = RSA.Create(2048); var key = rsa.ExportParameters(false);
        var result = Path.Combine(root, "worker-result.txt"); created.Add(result);
        var screenshot = Path.Combine(root, "worker.png"); created.Add(screenshot);
        var support = File.ReadAllText(Path.Combine(project, "TrayBridge", "UninstallSupport.cs"))
            .Replace("Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)", Literal(profile))
            .Replace(@"Global\TrayBridge.Lifecycle.", @"Global\TrayBridge.UninstallFixture." + operation + ".");
        var fixtureKey = @"Software\TrayBridge.Uninstall.Tests\" + operation;
        var worker = File.ReadAllText(Path.Combine(project, "TrayBridge", "CleanupWorker.cs"))
            .Replace(@"Software\Microsoft\Windows\CurrentVersion\Run", fixtureKey)
            .Replace("Process.GetProcessesByName(\"TrayBridge\")", "Process.GetProcessesByName(\"TrayBridgeFixtureUnused\")")
            .Replace("Process.GetProcessesByName(\"TrayBridge.Symbols\")", "Process.GetProcessesByName(\"TrayBridgeFixtureSymbolsUnused\")")
            .Replace("running = false; close.Enabled = true; form.AcceptButton = close; close.Focus();", "running = false; close.Enabled = true; form.AcceptButton = close; close.Focus(); var captureTimer = new System.Windows.Forms.Timer { Interval = 600 }; captureTimer.Tick += delegate { captureTimer.Stop(); System.IO.File.WriteAllText(" + Literal(result) + ", details.Text + \"\\r\\nProgress=\" + progress.Value); using (var bitmap = new System.Drawing.Bitmap(form.Width, form.Height)) { form.DrawToBitmap(bitmap, new System.Drawing.Rectangle(0, 0, form.Width, form.Height)); bitmap.Save(" + Literal(screenshot) + "); } form.Close(); captureTimer.Dispose(); }; captureTimer.Start();");
        var units = new[] { support, worker };
        var usingLines = units.SelectMany(unit => unit.Split('\n').Where(line => line.StartsWith("using "))).Distinct();
        var body = units.Select(unit => string.Join("\n", unit.Split('\n').Where(line => !line.StartsWith("using "))));
        var combined = Convert.ToBase64String(Encoding.UTF8.GetBytes(string.Join("\n", usingLines) + "\n" + string.Join("\n", body)));
        var script = Path.Combine(workspace, "cleanup.ps1"); created.Add(script);
        File.WriteAllText(script, "param([int]$Attempt = 0)\r\n$ErrorActionPreference = 'Stop'\r\n$source = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('" + combined + "'))\r\nAdd-Type -TypeDefinition $source -ReferencedAssemblies System.Windows.Forms, System.Drawing, System.Xml, System.Core, System.Security\r\n[TrayBridge.Cleanup.Worker]::Run($PSCommandPath, '', '" + Convert.ToBase64String(key.Modulus!) + "', '" + Convert.ToBase64String(key.Exponent!) + "', $Attempt)\r\n", new UTF8Encoding(true));
        plan.Files.Add(SafeFiles.Capture(script, "workspace", false));
        using var payload = new MemoryStream(); new XmlSerializer(typeof(RemovalPlan)).Serialize(payload, plan); var bytes = payload.ToArray();
        var state = Path.Combine(workspace, "plan.xml"); created.Add(state);
        SafeFiles.WriteXml(state, new RemovalApproval { Payload = bytes, Signature = rsa.SignData(bytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1) });
        void Run(int attempt)
        {
            var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe")) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true, WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows) };
            foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script, "-Attempt", attempt.ToString() }) start.ArgumentList.Add(argument);
            using var process = Process.Start(start)!;
            var error = process.StandardError.ReadToEndAsync(); var output = process.StandardOutput.ReadToEndAsync();
            if (!process.WaitForExit(45000)) throw new Exception("Worker fixture timed out.");
            if (process.ExitCode != 0) throw new Exception("Worker fixture: " + error.Result + output.Result);
        }
        try
        {
            var validState = File.ReadAllBytes(state);
            var brokenApproval = SafeFiles.ReadXml<RemovalApproval>(state); brokenApproval.Signature[0] ^= 1;
            using (var output = new FileStream(state, FileMode.Create, FileAccess.Write)) new XmlSerializer(typeof(RemovalApproval)).Serialize(output, brokenApproval);
            Run(0);
            Check(File.Exists(exe) && File.Exists(settings) && File.Exists(locked), "worker rejects a tampered approval before deleting any file");
            Check(File.ReadAllText(result).Contains("approval was modified"), "worker UI explains tampered approval failure");
            File.WriteAllBytes(state, validState);
            using (var registry = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(fixtureKey, true))
            {
                var command = "\"" + Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe") + "\" -NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"" + script + "\"";
                registry.SetValue("TrayBridge.Uninstall." + operation, command);
                registry.SetValue("Neighbor", "preserve registry sentinel");
            }
            using (var held = new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                Run(0);
                var text = File.ReadAllText(result);
                Check(text.Contains("A restart may be required") && text.Contains("Pending files:") && text.Contains("Progress=100"), "worker progress window reports locked files and restart guidance");
                Check(!File.Exists(exe) && !File.Exists(settings) && File.Exists(locked), "worker removes unlocked verified files and defers a locked file");
                using var registry = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(fixtureKey);
                Check(registry?.GetValue("TrayBridge.Uninstall." + operation) is string command && command.Contains("-Attempt 1"), "deferred registration is bounded to the next cleanup attempt");
                Check(File.Exists(script) && File.Exists(state), "pending cleanup retains its signed approval and exact script");
            }
            Run(1);
            Check(!File.Exists(locked) && !Directory.Exists(workspace), "retry removes the unlocked file, script, signed state, and empty workspace");
            Check(File.ReadAllText(sentinel) == "keep neighbor", "worker preserves an unrelated neighbor byte-for-byte");
            Check(File.ReadAllText(result).Contains("A restart may be required") && File.ReadAllText(result).Contains(sentinel), "completion UI lists preserved files and restart guidance");
            using (var registry = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(fixtureKey)) Check(registry?.GetValue("TrayBridge.Uninstall." + operation) == null, "successful retry removes only its cleanup registration");
            using (var registry = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(fixtureKey)) Check(registry?.GetValue("Neighbor") as string == "preserve registry sentinel", "cleanup leaves unrelated registry values unchanged");
            var review = Path.Combine(project, "artifacts", "uninstall-validation"); Directory.CreateDirectory(review);
            File.Copy(screenshot, Path.Combine(review, "completion-" + operation + ".png"));
            File.Copy(result, Path.Combine(review, "result-" + operation + ".txt"));
        }
        finally
        {
            using (var registry = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(fixtureKey, true)) { registry?.DeleteValue("TrayBridge.Uninstall." + operation, false); registry?.DeleteValue("Neighbor", false); }
            Microsoft.Win32.Registry.CurrentUser.DeleteSubKey(fixtureKey, false);
        }
    }
    static void Main()
    {
        // Deliberately isolated from LocalAppData and the actual release, controller, and registry.
        var project = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        root = Path.Combine(project, ".tools", "u-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root); directories.Add(root);
        try
        {
            Check(!SafeFiles.Within(root + "-neighbor\\file.txt", root), "path-prefix sibling is outside the root");
            using (var self = Process.GetCurrentProcess()) Check(SafeFiles.CurrentUserProcess(self) == true, "lifecycle guards identify the current Windows account");
            foreach (var bad in new[] { "C:relative", @"\\server\share\file", root + @"\..\sentinel", root + @"\file:stream", root + @"\*.dll", root + @"\file. " })
                Refuses(() => SafeFiles.Canonical(bad), "reject ambiguous path " + bad);
            Check(SafeFiles.DataName(@"symbols\SystemTray.pdb\0123456789ABCDEF0123456789ABCDEF1\SystemTray.pdb"), "recognize the bounded Microsoft symbol cache shape");
            Check(!SafeFiles.DataName(@"symbols\anything\personal.txt"), "unrelated symbol-folder files stay unowned");

            var neighbor = FileAt("personal.txt", "unchanged sentinel");
            var removable = FileAt("owned space ' Ω $() ` file.txt");
            var approved = SafeFiles.Capture(removable, "data", false);
            SafeFiles.Delete(approved);
            Check(!File.Exists(removable) && File.ReadAllText(neighbor) == "unchanged sentinel", "delete only the approved file; metacharacters have no shell meaning");

            var changed = FileAt("changed.txt"); var before = SafeFiles.Capture(changed, "data", false);
            File.WriteAllText(changed, "new contents");
            Refuses(() => SafeFiles.Delete(before), "same-file content replacement is preserved");
            Check(File.ReadAllText(changed) == "new contents", "changed file remains intact");

            var ownedData = FolderAt("owned-data"); TrayBridge.NativeHost.DataDirectory = ownedData;
            var ownedSettings = Path.Combine(ownedData, "settings.json"); created.Add(ownedSettings);
            TrayBridge.FileOwnership.WriteText(ownedSettings, "{}");
            var ownerDirectory = Path.Combine(ownedData, "ownership"); directories.Add(ownerDirectory);
            var ownerRecord = Path.Combine(ownerDirectory, "settings.json.owner"); created.Add(ownerRecord);
            var ownedSnapshot = SafeFiles.Capture(ownedSettings, "data", false);
            Check(TrayBridge.FileOwnership.Matches(ownedSettings, ownedSnapshot), "app writes produce a matching ownership record");
            var oldSettings = ownedSettings + ".old"; File.Move(ownedSettings, oldSettings); created.Add(oldSettings); File.WriteAllText(ownedSettings, "{}");
            Check(!TrayBridge.FileOwnership.Matches(ownedSettings, SafeFiles.Capture(ownedSettings, "data", false)), "ownership records do not authorize replacement files");
            File.WriteAllText(ownerRecord, "unrelated record sentinel");
            TrayBridge.FileOwnership.Record(ownedSettings);
            Check(File.ReadAllText(ownerRecord) == "unrelated record sentinel", "ownership tracking preserves an unrelated record");

            var replaced = FileAt("replaced.txt"); var old = SafeFiles.Capture(replaced, "data", false);
            var backup = replaced + ".original"; File.Move(replaced, backup); created.Add(backup);
            File.WriteAllText(replaced, "fixture");
            Refuses(() => SafeFiles.Delete(old), "same-content replacement with a new file ID is preserved");

            var pinned = FileAt("pinned.txt");
            using (var handle = SafeFiles.Open(pinned, false, false))
            {
                Refuses(() => File.WriteAllText(pinned, "changed"), "verification handle excludes concurrent writes");
                Refuses(() => File.Move(pinned, pinned + ".moved"), "verification handle excludes rename races");
            }
            var folder = FolderAt("not-empty"); var sentinel = Path.Combine(folder, "sentinel.txt"); File.WriteAllText(sentinel, "keep"); created.Add(sentinel);
            Refuses(() => SafeFiles.Delete(SafeFiles.Capture(folder, "data", true)), "nonempty directories cannot be removed");
            Check(File.ReadAllText(sentinel) == "keep", "nonempty-directory sentinel survives");

            var linked = FileAt("hardlink-target.txt"); var link = Path.Combine(root, "hardlink.txt");
            if (!CreateHardLink(link, linked, IntPtr.Zero)) throw new Win32Exception(Marshal.GetLastWin32Error());
            try
            {
                Refuses(() => SafeFiles.Capture(link, "data", false), "hard-linked targets are refused");
                Check(File.ReadAllText(linked) == "fixture", "hard-link target stays unchanged");
            }
            finally { File.Delete(link); }

            var junction = Path.Combine(root, "junction"); Junction(junction, folder);
            try
            {
                Refuses(() => SafeFiles.Capture(junction, "data", true), "junction targets are refused");
                Refuses(() => SafeFiles.Capture(Path.Combine(junction, "sentinel.txt"), "data", false), "junction ancestors are refused");
                Check(File.ReadAllText(sentinel) == "keep", "junction target sentinel survives");
            }
            finally { Directory.Delete(junction, false); }

            var readOnly = FileAt("read-only.txt"); var readOnlyEntry = SafeFiles.Capture(readOnly, "data", false);
            File.SetAttributes(readOnly, FileAttributes.ReadOnly);
            try { Refuses(() => SafeFiles.Delete(readOnlyEntry), "readonly files are preserved without clearing attributes"); }
            finally { File.SetAttributes(readOnly, FileAttributes.Normal); }

            var child = FolderAt("root-identity"); var childFile = Path.Combine(child, "owned.txt"); File.WriteAllText(childFile, "original");
            var rootEntry = SafeFiles.Capture(child, "data", true); var fileEntry = SafeFiles.Capture(childFile, "data", false);
            var priorRoot = child + ".old"; Directory.Move(child, priorRoot); directories.Add(priorRoot); created.Add(Path.Combine(priorRoot, "owned.txt"));
            Directory.CreateDirectory(child); File.WriteAllText(childFile, "other folder"); created.Add(childFile);
            Refuses(() => SafeFiles.DeleteApproved(new RemovalPlan { Data = child, Roots = [rootEntry] }, fileEntry), "replaced root identity cannot authorize a new folder");
            Check(File.ReadAllText(childFile) == "other folder", "replacement-root sentinel remains intact");

            using var rsa = RSA.Create(2048);
            var key = rsa.ExportParameters(false);
            using var payload = new MemoryStream(); new XmlSerializer(typeof(RemovalPlan)).Serialize(payload, new RemovalPlan { Release = root });
            var bytes = payload.ToArray();
            var approval = new RemovalApproval { Payload = bytes, Signature = rsa.SignData(bytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1) };
            var state = Path.Combine(root, "signed.xml"); SafeFiles.WriteXml(state, approval); created.Add(state);
            Check(SafeFiles.ReadApproved(state, Convert.ToBase64String(key.Modulus!), Convert.ToBase64String(key.Exponent!)).Release == root, "signed approval works in the cleanup verifier");
            var tampered = Path.Combine(root, "tampered.xml"); approval.Payload[10] ^= 1; SafeFiles.WriteXml(tampered, approval); created.Add(tampered);
            Refuses(() => SafeFiles.ReadApproved(tampered, Convert.ToBase64String(key.Modulus!), Convert.ToBase64String(key.Exponent!)), "tampered approval cannot expand deletion scope");

            var operation = Guid.NewGuid().ToString("N");
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var plan = new RemovalPlan { Release = root, Data = Path.Combine(local, "TrayBridge"), Operation = operation, Workspace = Path.Combine(local, "TrayBridge.Uninstall." + operation) };
            plan.Roots = [new() { Path = root, Category = "release", Directory = true }, new() { Path = plan.Data, Category = "data", Directory = true }, new() { Path = plan.Workspace, Category = "workspace", Directory = true }];
            plan.Files.Add(new() { Path = Path.Combine(root, "personal.txt"), Category = "release", Hash = new string('A', 64) });
            Refuses(() => SafeFiles.Validate(plan, plan.Workspace), "a release manifest cannot authorize unrelated filenames");
            plan.Files[0].Path = Path.Combine(root + "-neighbor", "TrayBridge.exe");
            Refuses(() => SafeFiles.Validate(plan, plan.Workspace), "a manifest cannot escape its approved root");
            WorkerFixture(project);
            count += RecoveryChecks.Run(root);
            var helperPath = Path.Combine(project, "artifacts", "uninstall-release", "TrayBridge.Native.dll");
            if (!File.Exists(helperPath)) helperPath = Path.Combine(project, "dist", "TrayBridge.Native.dll");
            var helper = LoadLibraryEx(helperPath, IntPtr.Zero, 1);
            if (helper == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            try
            {
                using var self = Process.GetCurrentProcess();
                var expected = GetProcAddress(helper, "TrayBridgeStopForUninstall");
                Check(expected != IntPtr.Zero && TrayBridge.LoadedHelper.Export(self.Handle, helper, "TrayBridgeStopForUninstall") == expected, "resolve uninstall acknowledgment from the loaded helper image");
                Check(TrayBridge.LoadedHelper.Export(self.Handle, helper, "DoesNotExist") == IntPtr.Zero, "missing legacy acknowledgment exports fail safely");
            }
            finally { FreeLibrary(helper); }
            Console.WriteLine("Passed " + count + " uninstall boundary checks.");
        }
        finally
        {
            // Each path was created by this fixture. No recursive deletes, no real app files.
            foreach (var path in created.Distinct())
                if (File.Exists(path)) SafeFiles.Delete(SafeFiles.Capture(path, "fixture", false));
            foreach (var path in directories.Distinct().OrderByDescending(p => p.Length))
                if (Directory.Exists(path)) SafeFiles.Delete(SafeFiles.Capture(path, "fixture", true));
        }
    }
}
