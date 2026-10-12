namespace TrayBridge;

// Test-only replacement for the application's real known-folder data root.
internal static class NativeHost
{
    internal static string DataDirectory { get; set; } = "";
}
