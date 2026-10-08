using System.Text.Json;

namespace TrayBridge;
internal sealed class TrayRule
{
    public bool Enabled { get; set; } = true;
    public bool AllApplications { get; set; } = true;
    public HashSet<string> Icons { get; set; } = [];
    public HashSet<string> ExcludedIcons { get; set; } = [];
    public bool AllSystemIcons { get; set; } = true;
    public HashSet<string> SystemIcons { get; set; } = [];
    public bool QuickSettings { get; set; } = true;
    public bool Clock { get; set; } = true;
    public bool SystemIndicators { get; set; } = true;
    public TrayRule Copy() => new() { Enabled = Enabled, AllApplications = AllApplications, Icons = new(Icons), ExcludedIcons = new(ExcludedIcons), AllSystemIcons = AllSystemIcons, SystemIcons = new(SystemIcons), QuickSettings = QuickSettings, Clock = Clock, SystemIndicators = SystemIndicators };
}
internal sealed class AppSettings
{
    public int Version { get; set; } = 2;
    public bool Active { get; set; }
    public bool StartWithWindows { get; set; }
    public string Appearance { get; set; } = "System";
    public TrayRule Defaults { get; set; } = new();
    public Dictionary<string, TrayRule> Overrides { get; set; } = [];
    public Dictionary<string, string> IconLabels { get; set; } = [];
    public GlobalTrayConfiguration? GlobalConfiguration { get; set; }
    public void CaptureGlobalConfiguration() => GlobalConfiguration = new() { Active = Active, Defaults = Defaults.Copy(), Overrides = Overrides.ToDictionary(pair => pair.Key, pair => pair.Value.Copy()) };
    public void RestoreGlobalConfiguration()
    {
        if (GlobalConfiguration is not { } configuration) return;
        Defaults = configuration.Defaults.Copy();
        Overrides = configuration.Overrides.ToDictionary(pair => pair.Key, pair => pair.Value.Copy());
    }
    public void ResetToWindows() { Active = false; Defaults = new(); Overrides.Clear(); }
    public TrayRule For(DisplayInfo display) => Overrides.TryGetValue(display.Id, out var value) ? value : Defaults;
    public void ApplyTo(IEnumerable<DisplayInfo> displays, TrayRule rule) { foreach (var display in displays) Overrides[display.Id] = rule.Copy(); }
    public object Resolve(IReadOnlyList<DisplayInfo> connected)
    {
        var primary = connected.FirstOrDefault(d => d.Primary);
        var missing = Overrides.Where(pair => pair.Value.Enabled && !connected.Any(d => d.Id == pair.Key)).Select(pair => pair.Value).ToArray();
        var fallback = missing.SelectMany(rule => rule.Icons).ToHashSet();
        var systemFallback = missing.SelectMany(rule => rule.SystemIcons).ToHashSet();
        return new
        {
            schema = Version,
            enabled = Active,
            controllerPid = Environment.ProcessId,
            monitors = connected.Select(display =>
            {
                var rule = For(display);
                bool recovering = display == primary && missing.Length != 0;
                return new { device = display.Device, primary = display.Primary, enabled = rule.Enabled || recovering,
                    allApps = rule.AllApplications || (recovering && missing.Any(r => r.AllApplications)), icons = rule.Icons.Concat(display == primary ? fallback : []).Distinct().ToArray(),
                    excludedIcons = rule.ExcludedIcons.ToArray(),
                    allSystemIcons = rule.AllSystemIcons || (recovering && missing.Any(r => r.AllSystemIcons)), systemIcons = rule.SystemIcons.Concat(display == primary ? systemFallback : []).Distinct().ToArray(),
                    quickSettings = rule.QuickSettings || (recovering && missing.Any(r => r.QuickSettings)), clock = rule.Clock || (recovering && missing.Any(r => r.Clock)), indicators = rule.SystemIndicators || (recovering && missing.Any(r => r.SystemIndicators)) };
            }).ToArray()
        };
    }
    public static AppSettings Load()
    {
        var path = Path.Combine(NativeHost.DataDirectory, "settings.json");
        try
        {
            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) ?? new();
            if (settings.Version < 2)
            {
                var known = settings.IconLabels.Keys.Where(id => id.StartsWith("app:") || id.StartsWith("guid:")).ToHashSet();
                ReadKnownApplications("inventory.json", known);
                ReadKnownApplications("hidden-inventory.json", known);
                settings.UpgradeExclusions(known);
            }
            return settings;
        }
        catch (FileNotFoundException) { return new(); }
        catch (JsonException) { File.Copy(path, path + ".invalid-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss"), true); return new(); }
    }
    internal void UpgradeExclusions(IEnumerable<string> knownApplications)
    {
        var known = knownApplications.ToHashSet();
        foreach (var rule in Overrides.Values.Append(Defaults))
            if (!rule.AllApplications) rule.ExcludedIcons.UnionWith(known.Except(rule.Icons));
        Version = 2;
    }
    private static void ReadKnownApplications(string name, HashSet<string> known)
    {
        try
        {
            using var snapshot = JsonDocument.Parse(File.ReadAllText(Path.Combine(NativeHost.DataDirectory, name)));
            var root = snapshot.RootElement;
            var elements = root.TryGetProperty("taskbars", out var taskbars)
                ? taskbars.EnumerateArray().SelectMany(bar => bar.GetProperty("elements").EnumerateArray())
                : root.GetProperty("icons").EnumerateArray().AsEnumerable();
            foreach (var element in elements)
                if (element.GetProperty("class").GetString() == "SystemTray.NotifyIconView" && !(element.TryGetProperty("text", out var text) && (text.GetString() ?? "").StartsWith("TrayBridge")) && element.TryGetProperty("identity", out var identity) && !string.IsNullOrWhiteSpace(identity.GetString())) known.Add(identity.GetString()!);
        }
        catch (IOException) { } catch (JsonException) { } catch (KeyNotFoundException) { }
    }
    public void Save(IReadOnlyList<DisplayInfo> connected)
    {
        Directory.CreateDirectory(NativeHost.DataDirectory);
        AtomicWrite("settings.json", this);
        AtomicWrite("native-settings.json", Resolve(connected));
    }
    private static void AtomicWrite(string name, object value)
    {
        var path = Path.Combine(NativeHost.DataDirectory, name);
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(path + ".tmp", path, true);
    }
    internal static void WriteNative(object value) => AtomicWrite("native-settings.json", value);
}
internal sealed class GlobalTrayConfiguration
{
    public bool Active { get; set; }
    public TrayRule Defaults { get; set; } = new();
    public Dictionary<string, TrayRule> Overrides { get; set; } = [];
}
