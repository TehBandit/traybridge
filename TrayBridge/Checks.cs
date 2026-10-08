using System.Text.Json;

namespace TrayBridge;
internal static class Checks
{
    internal static void Policy()
    {
        var primary = new DisplayInfo("physical-A", @"\\.\DISPLAY1", "Primary", true, new(0, 0, 1920, 1080));
        var secondary = new DisplayInfo("physical-B", @"\\.\DISPLAY2", "Secondary", false, new(1920, 0, 1920, 1080));
        var settings = new AppSettings { Defaults = new() { AllApplications = false, Icons = ["common"] } };
        if (!ReferenceEquals(settings.For(secondary), settings.Defaults)) throw new Exception("Global inheritance failed.");
        settings.ApplyTo([primary, secondary], new TrayRule { AllApplications = false, Icons = ["one"], SystemIcons = ["volume"] });
        settings.Overrides[primary.Id].Icons.Add("two");
        if (settings.Overrides[secondary.Id].Icons.Contains("two")) throw new Exception("Bulk configuration shares mutable rules.");
        settings.Overrides[secondary.Id].Icons = ["secondary-only"];
        using var disconnected = JsonDocument.Parse(JsonSerializer.Serialize(settings.Resolve([primary])));
        var fallback = disconnected.RootElement.GetProperty("monitors")[0];
        if (!fallback.GetProperty("icons").EnumerateArray().Any(v => v.GetString() == "secondary-only")) throw new Exception("Disconnected icon recovery failed.");
        settings.Overrides[primary.Id].Enabled = false; settings.Overrides[primary.Id].QuickSettings = false;
        using var hiddenPrimary = JsonDocument.Parse(JsonSerializer.Serialize(settings.Resolve([primary])));
        var recovered = hiddenPrimary.RootElement.GetProperty("monitors")[0];
        if (!recovered.GetProperty("enabled").GetBoolean() || !recovered.GetProperty("quickSettings").GetBoolean() || !recovered.GetProperty("systemIcons").EnumerateArray().Any(v => v.GetString() == "volume")) throw new Exception("Recovery left assigned system icons inside a disabled tray.");
        using var reconnected = JsonDocument.Parse(JsonSerializer.Serialize(settings.Resolve([primary, secondary])));
        if (reconnected.RootElement.GetProperty("monitors")[0].GetProperty("icons").EnumerateArray().Any(v => v.GetString() == "secondary-only")) throw new Exception("Reconnect did not restore the exclusive assignment.");
        settings.Overrides[secondary.Id].Enabled = false;
        using var disabled = JsonDocument.Parse(JsonSerializer.Serialize(settings.Resolve([primary])));
        if (disabled.RootElement.GetProperty("monitors")[0].GetProperty("icons").EnumerateArray().Any(v => v.GetString() == "secondary-only")) throw new Exception("Disabled rules unexpectedly create fallback assignments.");
        settings.Overrides.Remove(secondary.Id);
        if (!ReferenceEquals(settings.For(secondary), settings.Defaults)) throw new Exception("Reset to global defaults failed.");
        File.WriteAllText(Path.Combine(NativeHost.DataDirectory, "policy-report.json"), JsonSerializer.Serialize(new { Passed = true, Checks = new[] { "global inheritance", "independent bulk rules", "disconnect recovery", "reconnect routing", "disabled assignments", "reset overrides" } }));
    }
    internal static void Live(bool route)
    {
        var monitors = Displays.Read();
        var settings = new AppSettings { Active = true };
        string path = Path.Combine(NativeHost.DataDirectory, "native-settings.json");
        var prior = File.Exists(path) ? File.ReadAllText(path) : null;
        var report = new List<object>();
        try
        {
            Write(settings, monitors, path);
            if (NativeHost.Start() != 0) throw new Exception("Native startup rejected.");
            Thread.Sleep(5000);
            if (!route) Thread.Sleep(20000);
            using var inventory = ReadInventory();
            var primary = inventory.RootElement.GetProperty("taskbars").EnumerateArray().First(t => t.GetProperty("primary").GetBoolean());
            var icons = primary.GetProperty("elements").EnumerateArray().Where(e => e.GetProperty("class").GetString() == "SystemTray.NotifyIconView" && e.GetProperty("text").GetString()!.StartsWith("Stoplight")).Select(e => (Id: e.GetProperty("identity").GetString()!, Name: e.GetProperty("text").GetString()!.Split('\n')[0])).ToArray();
            report.Add(new { Stage = "clone", Counts = Counts(inventory) });
            if (route && monitors.Count > 1 && icons.Length > 0)
            {
                var running = icons.FirstOrDefault(i => i.Name.StartsWith("Stoplight Running"));
                if (running == default) running = icons[0];
                settings.ApplyTo(monitors.Where(m => m.Primary), new TrayRule { AllApplications = false, Icons = icons.Where(i => i.Id != running.Id).Select(i => i.Id).ToHashSet() });
                settings.ApplyTo(monitors.Where(m => !m.Primary), new TrayRule { AllApplications = false, Icons = [running.Id] });
                Write(settings, monitors, path);
                Thread.Sleep(3000);
                using var routed = ReadInventory();
                foreach (var taskbar in routed.RootElement.GetProperty("taskbars").EnumerateArray())
                {
                    var isPrimary = taskbar.GetProperty("primary").GetBoolean();
                    foreach (var icon in taskbar.GetProperty("elements").EnumerateArray().Where(e => e.GetProperty("class").GetString() == "SystemTray.NotifyIconView" && e.GetProperty("text").GetString()!.StartsWith("Stoplight")))
                    {
                        bool expected = isPrimary ? icon.GetProperty("identity").GetString() != running.Id : icon.GetProperty("identity").GetString() == running.Id;
                        if (icon.GetProperty("visible").GetBoolean() != expected) throw new Exception("An icon appeared on the wrong monitor.");
                    }
                }
                report.Add(new { Stage = "route", Counts = Counts(routed), Passed = true });
                var system = primary.GetProperty("elements").EnumerateArray().Where(e => e.GetProperty("class").GetString() == "SystemTray.IconView" && e.TryGetProperty("identity", out var id) && !string.IsNullOrWhiteSpace(id.GetString())).Select(e => (Id: e.GetProperty("identity").GetString()!, Label: e.GetProperty("text").GetString() ?? "")).ToArray();
                var volume = system.FirstOrDefault(e => e.Label.StartsWith("Volume"));
                var network = system.FirstOrDefault(e => e.Label.StartsWith("Network"));
                if (volume != default && network != default)
                {
                    foreach (var display in monitors)
                    {
                        var rule = settings.For(display).Copy(); rule.AllSystemIcons = false;
                        rule.SystemIcons = system.Where(i => display.Primary ? i.Id != volume.Id : i.Id != network.Id).Select(i => i.Id).ToHashSet();
                        settings.ApplyTo([display], rule);
                    }
                    Write(settings, monitors, path); Thread.Sleep(2000);
                    using var systems = ReadInventory();
                    foreach (var taskbar in systems.RootElement.GetProperty("taskbars").EnumerateArray())
                        foreach (var element in taskbar.GetProperty("elements").EnumerateArray().Where(e => e.GetProperty("class").GetString() == "SystemTray.IconView"))
                        {
                            var id = element.GetProperty("identity").GetString(); var isPrimary = taskbar.GetProperty("primary").GetBoolean();
                            if (id == volume.Id && element.GetProperty("visible").GetBoolean() != !isPrimary) throw new Exception("Volume routing failed.");
                            if (id == network.Id && element.GetProperty("visible").GetBoolean() != isPrimary) throw new Exception("Network routing failed.");
                        }
                    report.Add(new { Stage = "system-controls", Passed = true });
                }
            }
            NativeHost.Stop(); NativeHost.Probe();
            using var restored = ReadInventory(); report.Add(new { Stage = "restore", Counts = Counts(restored) });
            // Exercise the same resident DLL's enable/disable lifecycle again.
            settings = new() { Active = true }; Write(settings, monitors, path);
            NativeHost.Start(); Thread.Sleep(2500);
            using var restarted = ReadInventory(); report.Add(new { Stage = "restart", Counts = Counts(restarted) });
        }
        finally
        {
            NativeHost.Stop();
            if (prior is not null) File.WriteAllText(path, prior);
            else if (File.Exists(path)) File.Delete(path);
            File.WriteAllText(Path.Combine(NativeHost.DataDirectory, "test-report.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
    private static void Write(AppSettings settings, IReadOnlyList<DisplayInfo> monitors, string path) { File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(settings.Resolve(monitors))); File.Move(path + ".tmp", path, true); }
    private static JsonDocument ReadInventory() => JsonDocument.Parse(File.ReadAllText(Path.Combine(NativeHost.DataDirectory, "inventory.json")));
    private static object Counts(JsonDocument inventory) => inventory.RootElement.GetProperty("taskbars").EnumerateArray().Select(t => new { Device = t.GetProperty("device").GetString(), Visible = t.GetProperty("elements").EnumerateArray().Count(e => e.GetProperty("class").GetString() == "SystemTray.NotifyIconView" && e.GetProperty("visible").GetBoolean()) }).ToArray();
}
