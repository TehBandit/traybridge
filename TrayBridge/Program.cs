using System.Text.Json;

namespace TrayBridge;
internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.SequenceEqual(new[] { "--release-info" }))
        {
            using var resource = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("TrayBridge.UninstallRelease.xml") ?? throw new InvalidOperationException("Not a prepared release.");
            var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(resource)); resource.Position = 0;
            var inventory = (TrayBridge.UninstallSupport.ReleaseInventory)new System.Xml.Serialization.XmlSerializer(typeof(TrayBridge.UninstallSupport.ReleaseInventory)).Deserialize(resource)!;
            Console.WriteLine(JsonSerializer.Serialize(new { version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version!.ToString(3), inventoryHash = hash, sourceHash = inventory.SourceHash, sourceRevision = inventory.SourceRevision, files = inventory.Files }, new JsonSerializerOptions { IncludeFields = true }));
            return;
        }
        ApplicationConfiguration.Initialize();
        using var lifecycle = new Mutex(false, TrayBridge.UninstallSupport.SafeFiles.LifecycleName);
        bool acquired;
        try { acquired = lifecycle.WaitOne(0); } catch (AbandonedMutexException) { acquired = true; }
        if (!acquired)
        {
            try { using var show = EventWaitHandle.OpenExisting(@"Local\TrayBridge.Show"); show.Set(); }
            catch (WaitHandleCannotBeOpenedException) { MessageBox.Show("TrayBridge is busy in another session or is being uninstalled. Wait for it to finish before starting another copy.", "TrayBridge"); }
            return;
        }
        try
        {
            if (CleanupRecovery.Scan().Count > 0)
            {
                using var recovery = new CleanupRecoveryForm();
                if (recovery.ShowDialog() != DialogResult.OK) return;
            }
            Directory.CreateDirectory(NativeHost.DataDirectory);
            UiTheme.SetAppearance(AppSettings.Load().Appearance);
            if (args.Contains("--self-test")) Checks.Policy();
            else if (args.Contains("--fixture")) { Application.Run(new Fixture()); }
            else if (args.Contains("--prepare")) NativeHost.PrepareSymbols(message => FileOwnership.AppendText(Path.Combine(NativeHost.DataDirectory, "prepare.log"), message + Environment.NewLine)).GetAwaiter().GetResult();
            else if (args.Contains("--probe")) FileOwnership.WriteText(Path.Combine(NativeHost.DataDirectory, "probe-result.json"), JsonSerializer.Serialize(new { ExitCode = NativeHost.Probe() }));
            else if (args.Contains("--test-run") || args.Contains("--test-route"))
            {
                using var single = new Mutex(true, @"Local\TrayBridge", out bool first);
                if (!first) throw new InvalidOperationException("Exit the running TrayBridge controller before running a live test.");
                Checks.Live(args.Contains("--test-route"));
            }
            else
            {
                using var single = new Mutex(true, @"Local\TrayBridge", out bool first);
                if (!first) { using var signal = EventWaitHandle.OpenExisting(@"Local\TrayBridge.Show"); signal.Set(); return; }
                Application.Run(new TrayApplication(args.Contains("--hidden")));
            }
        }
        catch (Exception error)
        {
            if (Directory.Exists(NativeHost.DataDirectory))
            {
                var report = Path.Combine(NativeHost.DataDirectory, "error.txt");
                File.WriteAllText(report, error.ToString()); FileOwnership.Record(report);
            }
            else MessageBox.Show(error.Message, "TrayBridge");
            Environment.ExitCode = 1;
        }
        finally { lifecycle.ReleaseMutex(); }
    }
}
