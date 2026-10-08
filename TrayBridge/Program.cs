using System.Text.Json;

namespace TrayBridge;
internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        Directory.CreateDirectory(NativeHost.DataDirectory);
        try
        {
            if (args.Contains("--self-test")) Checks.Policy();
            else if (args.Contains("--fixture")) { ApplicationConfiguration.Initialize(); Application.Run(new Fixture()); }
            else if (args.Contains("--prepare")) NativeHost.PrepareSymbols(message => File.AppendAllText(Path.Combine(NativeHost.DataDirectory, "prepare.log"), message + Environment.NewLine)).GetAwaiter().GetResult();
            else if (args.Contains("--probe")) File.WriteAllText(Path.Combine(NativeHost.DataDirectory, "probe-result.json"), JsonSerializer.Serialize(new { ExitCode = NativeHost.Probe() }));
            else if (args.Contains("--test-run") || args.Contains("--test-route"))
            {
                using var single = new Mutex(true, @"Local\TrayBridge", out bool first);
                if (!first) throw new InvalidOperationException("Exit the running TrayBridge controller before running a live test.");
                ApplicationConfiguration.Initialize();
                Checks.Live(args.Contains("--test-route"));
            }
            else
            {
                ApplicationConfiguration.Initialize();
                using var single = new Mutex(true, @"Local\TrayBridge", out bool first);
                if (!first) { using var signal = EventWaitHandle.OpenExisting(@"Local\TrayBridge.Show"); signal.Set(); return; }
                Application.Run(new TrayApplication(args.Contains("--hidden")));
            }
        }
        catch (Exception error) { File.WriteAllText(Path.Combine(NativeHost.DataDirectory, "error.txt"), error.ToString()); Environment.ExitCode = 1; }
    }
}
