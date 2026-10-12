// Also compiled by the independent Windows PowerShell cleanup host. Keep C# 5 compatible.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Xml;
using System.Xml.Serialization;
using Microsoft.Win32.SafeHandles;

namespace TrayBridge.UninstallSupport
{
    public class RemovalEntry
    {
        public string Path = "", Hash = "", Category = "";
        public uint Volume, High, Low;
        public bool Directory;
    }
    public class RemovalPlan
    {
        public int Version = 1;
        public string Release = "", Data = "", Runtime = "", Workspace = "", Operation = "";
        public int ParentId;
        public long ParentStart;
        public string RecoveryReceipt = "";
        public List<RemovalEntry> Files = new List<RemovalEntry>();
        public List<RemovalEntry> Roots = new List<RemovalEntry>();
        public List<string> Preserved = new List<string>();
    }
    public class ReleaseFile
    {
        public string Name = "", Hash = "";
        public bool Runtime;
    }
    public class ReleaseInventory
    {
        public string SourceHash = "", SourceRevision = "";
        public List<ReleaseFile> Files = new List<ReleaseFile>();
    }
    public class RemovalApproval
    {
        public byte[] Payload = new byte[0], Signature = new byte[0];
    }

    public static class SafeFiles
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct Info
        {
            public uint Attributes;
            public System.Runtime.InteropServices.ComTypes.FILETIME Creation, Access, Write;
            public uint Volume, SizeHigh, SizeLow, Links, High, Low;
        }
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool GetFileInformationByHandle(SafeFileHandle handle, out Info info);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern uint GetFinalPathNameByHandle(SafeFileHandle handle, StringBuilder name, uint size, uint flags);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool GetVolumeInformationByHandleW(SafeFileHandle handle, IntPtr volumeName, uint volumeNameSize, IntPtr serial, IntPtr maximumComponent, out uint flags, StringBuilder filesystem, uint filesystemSize);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool SetFileInformationByHandle(SafeFileHandle handle, int type, ref byte disposition, uint size);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr OpenProcess(uint access, bool inherit, int id);
        [DllImport("kernel32.dll")]
        static extern bool CloseHandle(IntPtr handle);
        [DllImport("advapi32.dll", SetLastError = true)]
        static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
        [DllImport("advapi32.dll", SetLastError = true)]
        static extern bool GetTokenInformation(IntPtr token, int type, IntPtr buffer, uint size, out uint needed);
        [StructLayout(LayoutKind.Sequential)]
        struct SecurityAttributes { public uint Length; public IntPtr Descriptor; public int Inherit; }
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool ConvertStringSecurityDescriptorToSecurityDescriptor(string descriptor, uint revision, out IntPtr security, out uint size);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool CreateDirectory(string path, ref SecurityAttributes security);
        [DllImport("kernel32.dll")]
        static extern IntPtr LocalFree(IntPtr allocation);

        public static void CreatePrivateWorkspace(string path)
        {
            var sid = (WindowsIdentity.GetCurrent().User ?? throwSid()).Value;
            IntPtr descriptor; uint size;
            // A new directory only: protected inheritance grants access to this user and SYSTEM.
            if (!ConvertStringSecurityDescriptorToSecurityDescriptor("O:" + sid + "D:P(A;OICI;FA;;;SY)(A;OICI;FA;;;" + sid + ")", 1, out descriptor, out size)) throw new Win32Exception(Marshal.GetLastWin32Error());
            try
            {
                var attributes = new SecurityAttributes { Length = (uint)Marshal.SizeOf(typeof(SecurityAttributes)), Descriptor = descriptor };
                if (!CreateDirectory(Canonical(path), ref attributes)) throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not create a new private cleanup workspace.");
            }
            finally { LocalFree(descriptor); }
        }

        public static bool? CurrentUserProcess(Process process)
        {
            var handle = OpenProcess(0x1000, false, process.Id);
            if (handle == IntPtr.Zero) return null;
            try
            {
                IntPtr token;
                if (!OpenProcessToken(handle, 8, out token)) return null;
                try
                {
                    uint size;
                    GetTokenInformation(token, 1, IntPtr.Zero, 0, out size);
                    if (size == 0 || size > 65536) return null;
                    var buffer = Marshal.AllocHGlobal((int)size);
                    try
                    {
                        if (!GetTokenInformation(token, 1, buffer, size, out size)) return null;
                        var sid = new SecurityIdentifier(Marshal.ReadIntPtr(buffer));
                        var current = WindowsIdentity.GetCurrent().User;
                        return current == null ? (bool?)null : sid.Equals(current);
                    }
                    finally { Marshal.FreeHGlobal(buffer); }
                }
                finally { CloseHandle(token); }
            }
            finally { CloseHandle(handle); }
        }

        public static string LifecycleName
        {
            get { return @"Global\TrayBridge.Lifecycle." + (WindowsIdentity.GetCurrent().User ?? throwSid()).Value; }
        }
        static SecurityIdentifier throwSid() { throw new IOException("Could not identify this Windows account."); }
        public static string Canonical(string path)
        {
            if (String.IsNullOrWhiteSpace(path) || path.Length < 3 || !Char.IsLetter(path[0]) || path[1] != ':' || path[2] != '\\' || path.IndexOf(':', 2) >= 0 || path.IndexOfAny(new[] { '*', '?', '\0', '/' }) >= 0)
                throw new IOException("Unsupported uninstall path: " + path);
            if (path.Split('\\').Skip(1).Any(p => p == "." || p == ".." || p.EndsWith(".") || p.EndsWith(" ")))
                throw new IOException("Ambiguous uninstall path: " + path);
            var full = System.IO.Path.GetFullPath(path);
            return full.Length == 3 ? full : full.TrimEnd('\\');
        }
        public static bool Within(string path, string root)
        {
            return Canonical(path).StartsWith(Canonical(root).TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);
        }
        public static Info Identify(SafeFileHandle handle)
        {
            Info info;
            if (!GetFileInformationByHandle(handle, out info)) throw new Win32Exception(Marshal.GetLastWin32Error());
            if ((info.Attributes & 0x400) != 0) throw new IOException("Linked files and folders are preserved.");
            if ((info.Attributes & 0x10) == 0 && info.Links != 1) throw new IOException("Hard-linked files are preserved.");
            return info;
        }
        public static SafeFileHandle Open(string path, bool directory, bool delete)
        {
            path = Canonical(path);
            // Deny write and delete sharing: identity/content cannot change under this handle.
            var handle = CreateFile(path, (directory ? 0x80u : 0x80000000u) | (delete ? 0x10000u : 0u), 1, IntPtr.Zero, 3, 0x00200000u | (directory ? 0x02000000u : 0u), IntPtr.Zero);
            if (handle.IsInvalid) { handle.Dispose(); throw new Win32Exception(Marshal.GetLastWin32Error(), path); }
            try
            {
                var info = Identify(handle);
                if (((info.Attributes & 0x10) != 0) != directory) throw new IOException("File type changed: " + path);
                var filesystem = new StringBuilder(32); uint flags;
                if (!GetVolumeInformationByHandleW(handle, IntPtr.Zero, 0, IntPtr.Zero, IntPtr.Zero, out flags, filesystem, (uint)filesystem.Capacity) || filesystem.ToString() != "NTFS")
                    throw new IOException("Safe uninstall currently requires local NTFS storage; this location was preserved: " + path);
                CheckPath(handle, path);
                return handle;
            }
            catch { handle.Dispose(); throw; }
        }
        public static void CheckPath(SafeFileHandle handle, string path)
        {
            Identify(handle);
            var final = new StringBuilder(32768);
            var length = GetFinalPathNameByHandle(handle, final, (uint)final.Capacity, 0);
            if (length == 0 || length >= final.Capacity || !String.Equals(final.ToString(), @"\\?\" + Canonical(path), StringComparison.OrdinalIgnoreCase))
                throw new IOException("Redirected uninstall path: " + path);
        }
        public static List<SafeFileHandle> LockParents(string path)
        {
            path = Canonical(path);
            var parents = new List<string>();
            for (var parent = System.IO.Path.GetDirectoryName(path); !String.IsNullOrEmpty(parent) && parent.Length > 3; parent = System.IO.Path.GetDirectoryName(parent)) parents.Add(parent);
            parents.Reverse();
            var result = new List<SafeFileHandle>();
            try { foreach (var parent in parents) result.Add(Open(parent, true, false)); return result; }
            catch { foreach (var handle in result) handle.Dispose(); throw; }
        }
        public static string Hash(SafeFileHandle handle)
        {
            // Non-owning duplicate wrapper; FileStream must never close the caller's handle.
            using (var borrowed = new SafeFileHandle(handle.DangerousGetHandle(), false))
            using (var stream = new FileStream(borrowed, FileAccess.Read))
            using (var hash = SHA256.Create()) { stream.Position = 0; return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", ""); }
        }
        public static RemovalEntry Capture(string path, string category, bool directory)
        {
            var parents = LockParents(path);
            try
            {
                using (var handle = Open(path, directory, false))
                {
                    var info = Identify(handle);
                    return new RemovalEntry { Path = Canonical(path), Category = category, Directory = directory, Volume = info.Volume, High = info.High, Low = info.Low, Hash = directory ? "" : Hash(handle) };
                }
            }
            finally { foreach (var handle in parents) handle.Dispose(); }
        }
        public static void Match(SafeFileHandle handle, RemovalEntry entry)
        {
            var info = Identify(handle);
            if (info.Volume != entry.Volume || info.High != entry.High || info.Low != entry.Low || (!entry.Directory && !String.Equals(Hash(handle), entry.Hash, StringComparison.Ordinal)))
                throw new IOException("File changed since it was approved; preserved: " + entry.Path);
        }
        public static void Delete(RemovalEntry entry)
        {
            var parents = LockParents(entry.Path);
            try
            {
                using (var handle = Open(entry.Path, entry.Directory, true))
                {
                    Match(handle, entry);
                    if (!entry.Directory && (Identify(handle).Attributes & 1) != 0) throw new IOException("Read-only file preserved without changing attributes: " + entry.Path);
                    byte remove = 1;
                    if (!SetFileInformationByHandle(handle, 4, ref remove, 1)) throw new Win32Exception(Marshal.GetLastWin32Error(), entry.Path);
                }
            }
            finally { foreach (var handle in parents) handle.Dispose(); }
        }
        public static void DeleteApproved(RemovalPlan plan, RemovalEntry entry)
        {
            var rootPath = entry.Category == "release" ? plan.Release : entry.Category == "runtime" ? plan.Runtime : entry.Category == "workspace" ? plan.Workspace : plan.Data;
            var root = plan.Roots.FirstOrDefault(r => String.Equals(r.Path, rootPath, StringComparison.OrdinalIgnoreCase));
            if (root == null) throw new IOException("Missing root identity.");
            if (String.Equals(root.Path, entry.Path, StringComparison.OrdinalIgnoreCase)) { Delete(entry); return; }
            var parents = LockParents(root.Path);
            try
            {
                using (var handle = Open(root.Path, true, false)) { Match(handle, root); Delete(entry); }
            }
            finally { foreach (var handle in parents) handle.Dispose(); }
        }
        public static bool DataName(string name)
        {
            if (String.IsNullOrEmpty(name) || name.Split('\\').Any(p => p == "" || p == "." || p == "..")) return false;
            var fixedNames = new[] { "settings.json", "native-settings.json", "inventory.json", "hidden-inventory.json", "status-explorer.json", "status-ShellHost.json", "policy-report.json", "test-report.json", "fixture-result.json", "probe-result.json", "prepare.log", "error.txt" };
            if (fixedNames.Contains(name, StringComparer.OrdinalIgnoreCase)) return true;
            if (name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)) return DataName(name.Substring(0, name.Length - 4));
            if (System.Text.RegularExpressions.Regex.IsMatch(name, @"^settings\.json\.invalid-\d{14}$", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) return true;
            if (System.Text.RegularExpressions.Regex.IsMatch(name, @"^native-\d+\.log$", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) return true;
            if (System.Text.RegularExpressions.Regex.IsMatch(name, @"^native\\TrayBridge\.Native\.[A-F0-9]{16}\.dll$", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) return true;
            return System.Text.RegularExpressions.Regex.IsMatch(name, @"^symbols\\(taskbar\.dll|SystemTray\.dll|twinui\.pcshell\.dll)\.symmap$", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
                || System.Text.RegularExpressions.Regex.IsMatch(name, @"^symbols\\(taskbar|SystemTray|twinui\.pcshell)\.pdb\\[A-F0-9]{33,40}\\\1\.pdb$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }
        public static readonly string[] ReleaseNames = { "TrayBridge.exe", "TrayBridge.Native.dll", "TrayBridge.Symbols.exe", "LICENSE", "THIRD-PARTY-NOTICES.md", "README.md", "SECURITY.md", "ReleaseNotes.md", "MinHook-LICENSE.txt", "Taskbar-Multi-Tray-LICENSE.txt", "Dotnet-Runtime-LICENSE.txt", "Dotnet-Runtime-THIRD-PARTY-NOTICES.txt", "Dotnet-Desktop-LICENSE.txt" };
        public static void Validate(RemovalPlan plan, string workspace)
        {
            ValidateForProfile(plan, workspace, Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
        }
        // Read-only validation shared with recovery. Callers choose the profile;
        // the cleanup worker always uses Validate's Windows known-folder root.
        public static void ValidateForProfile(RemovalPlan plan, string workspace, string local)
        {
            if (plan.Version != 1 || plan.Files.Count > 10000 || plan.Roots.Count > 10000 || plan.Preserved.Count > 10000) throw new IOException("Invalid removal plan.");
            Guid id;
            if (!Guid.TryParseExact(plan.Operation, "N", out id)) throw new IOException("Invalid operation ID.");
            if (!String.Equals(Canonical(plan.Data), Canonical(System.IO.Path.Combine(local, "TrayBridge")), StringComparison.OrdinalIgnoreCase)
                || !String.Equals(Canonical(workspace), Canonical(System.IO.Path.Combine(local, "TrayBridge.Uninstall." + plan.Operation)), StringComparison.OrdinalIgnoreCase)
                || !String.Equals(Canonical(plan.Workspace), Canonical(workspace), StringComparison.OrdinalIgnoreCase)) throw new IOException("Removal roots do not belong to TrayBridge.");
            var forbidden = new[] { System.IO.Path.GetPathRoot(plan.Release), Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), local, System.IO.Path.GetTempPath().TrimEnd('\\'), Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), Environment.GetFolderPath(Environment.SpecialFolder.Startup), Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads") };
            if (forbidden.Any(p => !String.IsNullOrEmpty(p) && String.Equals(Canonical(p), Canonical(plan.Release), StringComparison.OrdinalIgnoreCase))
                || Within(plan.Release, Environment.GetFolderPath(Environment.SpecialFolder.Windows))) throw new IOException("Unsafe release root.");
            if (plan.Runtime != "" && !Within(plan.Runtime, System.IO.Path.Combine(System.IO.Path.GetTempPath(), ".net", "TrayBridge"))) throw new IOException("Unsupported runtime cache root.");
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in plan.Files.Concat(plan.Roots))
            {
                var path = Canonical(entry.Path);
                if (!seen.Add(path)) throw new IOException("Duplicate deletion target.");
                var root = entry.Category == "release" ? plan.Release : entry.Category == "runtime" ? plan.Runtime : entry.Category == "workspace" ? workspace : entry.Category == "data" || entry.Category == "ownership" ? plan.Data : "";
                if (root == "" || (!String.Equals(path, Canonical(root), StringComparison.OrdinalIgnoreCase) && !Within(path, root))) throw new IOException("Deletion target outside its root.");
                var relative = path.Length == Canonical(root).Length ? "" : path.Substring(Canonical(root).Length + 1);
                if (!entry.Directory)
                {
                    if (entry.Hash.Length != 64 || entry.Hash.Any(c => !Uri.IsHexDigit(c))) throw new IOException("Missing content identity.");
                    if (entry.Category == "release" && !ReleaseNames.Contains(relative, StringComparer.OrdinalIgnoreCase)) throw new IOException("Unknown release file.");
                    if (entry.Category == "data" && !DataName(relative)) throw new IOException("Unknown data file.");
                    if (entry.Category == "ownership" && (!relative.StartsWith("ownership\\", StringComparison.OrdinalIgnoreCase) || !relative.EndsWith(".owner", StringComparison.OrdinalIgnoreCase) || !DataName(relative.Substring(10, relative.Length - 16)))) throw new IOException("Unknown ownership record.");
                    if (entry.Category == "runtime" && (relative.Contains("\\") || !relative.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))) throw new IOException("Unknown runtime file.");
                    if (entry.Category == "workspace" && relative != "cleanup.ps1" && relative != "plan.xml") throw new IOException("Unknown cleanup artifact.");
                }
                else if (relative != "" && !plan.Files.Any(f => f.Category == entry.Category && Within(f.Path, path))) throw new IOException("Unowned empty directory.");
            }
            foreach (var root in new[] { plan.Release, plan.Data, workspace }.Concat(plan.Runtime == "" ? new string[0] : new[] { plan.Runtime }))
                if (!plan.Roots.Any(r => r.Directory && String.Equals(r.Path, root, StringComparison.OrdinalIgnoreCase))) throw new IOException("Missing root identity.");
        }
        public static T ReadXml<T>(string path)
        {
            var parents = LockParents(path);
            try
            {
                using (var handle = Open(path, false, false))
                {
                    var info = Identify(handle);
                    if (info.SizeHigh != 0 || info.SizeLow > 8 * 1024 * 1024) throw new IOException("Oversized uninstall metadata.");
                    using (var borrowed = new SafeFileHandle(handle.DangerousGetHandle(), false))
                    using (var stream = new FileStream(borrowed, FileAccess.Read))
                    using (var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 8 * 1024 * 1024 }))
                    {
                        var value = new XmlSerializer(typeof(T)).Deserialize(reader);
                        if (value is T) return (T)value;
                        throw new IOException("Missing uninstall metadata.");
                    }
                }
            }
            finally { foreach (var handle in parents) handle.Dispose(); }
        }
        public static void WriteXml<T>(string path, T value)
        {
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None)) new XmlSerializer(typeof(T)).Serialize(stream, value);
        }
        public static RemovalPlan ReadApproved(string path, string modulus, string exponent)
        {
            var approval = ReadXml<RemovalApproval>(path);
            if (approval.Payload.Length > 4 * 1024 * 1024) throw new IOException("Oversized approval.");
            using (var rsa = new RSACryptoServiceProvider())
            {
                rsa.PersistKeyInCsp = false;
                rsa.ImportParameters(new RSAParameters { Modulus = Convert.FromBase64String(modulus), Exponent = Convert.FromBase64String(exponent) });
                if (!rsa.VerifyData(approval.Payload, "2.16.840.1.101.3.4.2.1", approval.Signature)) throw new IOException("Uninstall approval was modified. No files will be deleted.");
            }
            using (var stream = new MemoryStream(approval.Payload))
            using (var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 4 * 1024 * 1024 }))
            {
                var value = new XmlSerializer(typeof(RemovalPlan)).Deserialize(reader) as RemovalPlan;
                if (value == null) throw new IOException("Missing uninstall approval.");
                return value;
            }
        }
    }
}
