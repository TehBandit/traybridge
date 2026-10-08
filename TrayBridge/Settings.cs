using System.Text.Json;

namespace TrayBridge;
internal sealed class TrayRule
{
    public bool Enabled { get; set; } = true;
    public bool AllApplications { get; set; } = true;
    public HashSet<string> Icons { get; set; } = [];
    public bool AllSystemIcons { get; set; } = true;
    public HashSet<string> SystemIcons { get; set; } = [];
    public bool QuickSettings { get; set; } = true;
    public bool Clock { get; set; } = true;
    public bool SystemIndicators { get; set; } = true;
    public TrayRule Copy() => new() { Enabled = Enabled, AllApplications = AllApplications, Icons = new(Icons), AllSystemIcons = AllSystemIcons, SystemIcons = new(SystemIcons), QuickSettings = QuickSettings, Clock = Clock, SystemIndicators = SystemIndicators };
}
internal sealed class AppSettings
{
    public int Version { get; set; } = 1;
    public bool Active { get; set; }
    public bool StartWithWindows { get; set; }
    public TrayRule Defaults { get; set; } = new();
    public Dictionary<string, TrayRule> Overrides { get; set; } = [];
    public Dictionary<string, string> IconLabels { get; set; } = [];
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
                    allSystemIcons = rule.AllSystemIcons || (recovering && missing.Any(r => r.AllSystemIcons)), systemIcons = rule.SystemIcons.Concat(display == primary ? systemFallback : []).Distinct().ToArray(),
                    quickSettings = rule.QuickSettings || (recovering && missing.Any(r => r.QuickSettings)), clock = rule.Clock || (recovering && missing.Any(r => r.Clock)), indicators = rule.SystemIndicators || (recovering && missing.Any(r => r.SystemIndicators)) };
            }).ToArray()
        };
    }
    public static AppSettings Load()
    {
        var path = Path.Combine(NativeHost.DataDirectory, "settings.json");
        try { return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) ?? new(); }
        catch (FileNotFoundException) { return new(); }
        catch (JsonException) { File.Copy(path, path + ".invalid-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss"), true); return new(); }
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
