using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace TrayBridge;

internal static class PlatformSupport
{
    internal const string Supported = "Windows 11 25H2 x64, build 26200.9457";
    internal static bool Supports(Version version, int revision, Architecture architecture) =>
        architecture == Architecture.X64 && version.Major == 10 && version.Minor == 0 && version.Build == 26200 && revision == 9457;

    internal static void EnsureSupported()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
        var revision = key?.GetValue("UBR") is int value ? value : -1;
        var version = Environment.OSVersion.Version;
        var architecture = RuntimeInformation.OSArchitecture;
        if (!Supports(version, revision, architecture))
            throw new InvalidOperationException($"TrayBridge supports {Supported}. This device is build {version.Build}.{(revision < 0 ? "unknown" : revision)}, {architecture}. Tray management is disabled on this build. Check for a compatible TrayBridge release after Windows updates.");
    }
}
