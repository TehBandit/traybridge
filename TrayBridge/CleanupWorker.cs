// Embedded source executed in Windows PowerShell's independent process; C# 5 compatible.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;
using TrayBridge.UninstallSupport;

namespace TrayBridge.Cleanup
{
    public static class Worker
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        public static string Command(string script, int attempt)
        {
            return "\"" + Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe") + "\" -NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"" + script + "\"" + (attempt == 0 ? "" : " -Attempt " + attempt);
        }
        public static void Run(string script, string pipeName, string modulus, string exponent, int attempt, int waitForParent = 0, long parentStarted = 0, string recoveryPipe = "")
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            var form = new Form { Text = "Uninstall TrayBridge", ClientSize = new Size(560, 350), MinimumSize = new Size(500, 330), StartPosition = FormStartPosition.CenterScreen, Font = new Font("Segoe UI", 10), MaximizeBox = false };
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), ColumnCount = 1, RowCount = 4 };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            var label = new Label { Dock = DockStyle.Fill, Text = "Preparing to remove TrayBridge..." };
            var progress = new ProgressBar { Dock = DockStyle.Fill, Style = ProgressBarStyle.Continuous, Maximum = 100, AccessibleName = "Uninstall progress" };
            var details = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BorderStyle = BorderStyle.None, BackColor = SystemColors.Control, AccessibleName = "Uninstall results", Text = "Windows taskbars have been restored. Waiting for the app to close." };
            var close = new Button { Text = "Close", Enabled = false, Anchor = AnchorStyles.Right, Width = 90 };
            layout.Controls.Add(label, 0, 0); layout.Controls.Add(progress, 0, 1); layout.Controls.Add(details, 0, 2); layout.Controls.Add(close, 0, 3); form.Controls.Add(layout);
            bool running = true, shuttingDown = false;
            form.FormClosing += delegate(object sender, FormClosingEventArgs args) { if (running && args.CloseReason == CloseReason.UserClosing) args.Cancel = true; else if (args.CloseReason == CloseReason.WindowsShutDown) shuttingDown = true; };
            close.Click += delegate { form.Close(); };
            Action<int, string> report = delegate(int value, string text) { if (!form.IsDisposed && form.IsHandleCreated) form.BeginInvoke((Action)delegate { progress.Value = Math.Max(0, Math.Min(100, value)); label.Text = text; }); };
            form.Shown += async delegate
            {
                try
                {
                    string result = await Task.Run(delegate { return Remove(script, pipeName, modulus, exponent, attempt, report, waitForParent, parentStarted, recoveryPipe); });
                    label.Text = "TrayBridge removal finished"; progress.Value = 100; details.Text = result;
                }
                catch (Exception error)
                {
                    // Initial interruption can resume using the registration committed by the controller.
                    // A failed sign-in retry stops automatically; system shutdown keeps its recovery state.
                    if (pipeName == "" && !shuttingDown) StopResume(script, attempt);
                    label.Text = "Uninstall could not finish"; details.Text = error.Message + "\r\n\r\nAny remaining files have been preserved. A restart may be required to finish uninstallation.\r\n\r\nCleanup files retained at: " + Path.GetDirectoryName(script);
                }
                running = false; close.Enabled = true; form.AcceptButton = close; close.Focus();
            };
            Application.Run(form);
        }
        static void StopResume(string script, int attempt)
        {
            try
            {
                var id = Path.GetFileName(Path.GetDirectoryName(script)).Replace("TrayBridge.Uninstall.", "");
                Guid parsed;
                if (!Guid.TryParseExact(id, "N", out parsed)) return;
                using (var key = Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    var name = "TrayBridge.Uninstall." + id;
                    if (key != null && String.Equals(key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string, Command(script, attempt), StringComparison.Ordinal)) key.DeleteValue(name, false);
                }
            }
            catch (Exception) { /* Inaccessible or replaced values are preserved. */ }
        }
        static bool Missing(Exception error)
        {
            var native = error as Win32Exception;
            return native != null && (native.NativeErrorCode == 2 || native.NativeErrorCode == 3);
        }
        static string Remove(string script, string pipeName, string modulus, string exponent, int attempt, Action<int, string> report, int waitForParent, long parentStarted, string recoveryPipe)
        {
            script = SafeFiles.Canonical(script);
            var workspace = Path.GetDirectoryName(script);
            var statePath = Path.Combine(workspace, "plan.xml");
            if (attempt < 0 || attempt > 3) throw new IOException("Invalid cleanup attempt.");
            var plan = SafeFiles.ReadApproved(statePath, modulus, exponent);
            SafeFiles.Validate(plan, workspace);
            var scriptEntry = plan.Files.Single(e => e.Category == "workspace" && e.Path == script);
            using (var check = SafeFiles.Open(script, false, false)) SafeFiles.Match(check, scriptEntry);
            var stateEntry = SafeFiles.Capture(statePath, "workspace", false);
            if (waitForParent != 0)
            {
                Process parent = null;
                try { parent = Process.GetProcessById(waitForParent); } catch (ArgumentException) { /* Recovery already exited. */ }
                if (parent != null) using (parent)
                {
                    try
                    {
                        if (parent.StartTime.ToUniversalTime().Ticks != parentStarted || SafeFiles.CurrentUserProcess(parent) != true)
                            throw new IOException("Recovery parent identity changed; files were preserved.");
                        if (recoveryPipe != "")
                        {
                            using (var ready = new NamedPipeClientStream(".", recoveryPipe, PipeDirection.InOut))
                            {
                                ready.Connect(30000); ready.WriteByte(1); ready.Flush();
                            }
                        }
                        if (!parent.WaitForExit(30000)) throw new IOException("Close TrayBridge recovery before retrying cleanup.");
                    }
                    catch (InvalidOperationException) { if (!parent.HasExited) throw; }
                }
            }
            if (pipeName != "")
            {
                // Hold the actual parent process, verify its image and creation time before acknowledging readiness.
                using (var parent = Process.GetProcessById(plan.ParentId))
                {
                    if (parent.StartTime.ToUniversalTime().Ticks != plan.ParentStart || !String.Equals(parent.MainModule.FileName, Path.Combine(plan.Release, "TrayBridge.exe"), StringComparison.OrdinalIgnoreCase)) throw new IOException("Cleanup parent identity changed.");
                    using (var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut))
                    {
                        pipe.Connect(30000);
                        pipe.WriteByte(1); pipe.Flush();
                        var receive = Task.Run(delegate { return pipe.ReadByte(); });
                        if (!receive.Wait(45000) || receive.Result != 1) throw new IOException("Uninstall was not committed; app files were preserved.");
                    }
                    if (!parent.WaitForExit(30000)) throw new IOException("TrayBridge has not exited; app files were preserved.");
                }
            }
            using (var gate = new Mutex(false, SafeFiles.LifecycleName))
            {
                bool acquired;
                try { acquired = gate.WaitOne(0); } catch (AbandonedMutexException) { acquired = true; }
                if (!acquired) throw new IOException("Another TrayBridge process is using its files. Close it before retrying cleanup.");
                try
                {
                    foreach (var other in Process.GetProcessesByName("TrayBridge").Concat(Process.GetProcessesByName("TrayBridge.Symbols"))) using (other)
                        if (SafeFiles.CurrentUserProcess(other) != false) throw new IOException("Another TrayBridge process is still running. Its files were preserved; close it before retrying cleanup.");
                    return DeleteFiles(plan, script, statePath, stateEntry, attempt, report);
                }
                finally { gate.ReleaseMutex(); }
            }
        }
        static string DeleteFiles(RemovalPlan plan, string script, string statePath, RemovalEntry stateEntry, int attempt, Action<int, string> report)
        {
            var remaining = new List<RemovalEntry>();
            var failures = new List<string>();
            var preserved = new List<string>(plan.Preserved);
            var targets = plan.Files.Where(e => e.Category != "workspace").OrderBy(e => e.Category == "ownership" ? 1 : 0).ToList();
            var count = 0;
            foreach (var entry in targets)
            {
                report(10 + (++count * 75 / Math.Max(1, targets.Count)), "Removing verified TrayBridge files (" + count + "/" + targets.Count + ")...");
                try { SafeFiles.DeleteApproved(plan, entry); }
                catch (Exception error)
                {
                    if (Missing(error)) continue;
                    var native = error as Win32Exception;
                    if (native != null && (native.NativeErrorCode == 32 || native.NativeErrorCode == 33 || native.NativeErrorCode == 5)) remaining.Add(entry);
                    else { failures.Add(entry.Path + ": " + error.Message); preserved.Add(entry.Path); }
                }
            }
            // Only recorded directories are eligible, and Windows' empty-directory delete is the final guard.
            report(88, "Removing empty app folders...");
            foreach (var entry in plan.Roots.Where(e => e.Category != "workspace").OrderByDescending(e => e.Path.Length))
            {
                try { SafeFiles.DeleteApproved(plan, entry); }
                catch (Exception error)
                {
                    if (Missing(error)) continue;
                    var native = error as Win32Exception;
                    if (native != null && (native.NativeErrorCode == 32 || native.NativeErrorCode == 33 || native.NativeErrorCode == 5)) remaining.Add(entry);
                    else if (native != null && native.NativeErrorCode == 145)
                    {
                        if (!remaining.Any(file => SafeFiles.Within(file.Path, entry.Path))) preserved.Add(entry.Path + " (folder retained because it contains other files)");
                    }
                    else failures.Add(entry.Path + ": " + error.Message);
                }
            }
            report(92, "Verifying removal...");
            var registration = "TrayBridge.Uninstall." + plan.Operation;
            var command = Command(script, attempt);
            var nextCommand = Command(script, attempt + 1);
            if (remaining.Count > 0 && attempt < 3 && nextCommand.Length <= 260)
            {
                // Approval stays signed and immutable. A retry cannot add paths or accept replaced files.
                using (var handle = SafeFiles.Open(statePath, false, false)) SafeFiles.Match(handle, stateEntry);
                using (var key = Registry.CurrentUser.CreateSubKey(RunKey, true))
                {
                    var existing = key.GetValue(registration, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                    if (existing != null && !String.Equals(existing as string, command, StringComparison.Ordinal)) throw new IOException("The cleanup startup registration changed; it was preserved.");
                    key.SetValue(registration, nextCommand, RegistryValueKind.String);
                }
                return "TrayBridge is disabled. Some files or folders are still in use, or Windows denied removal.\r\n\r\nA restart may be required to finish uninstallation. Cleanup will retry at your next sign-in (up to three retries); it will not restart TrayBridge.\r\n\r\nPending files:\r\n" + String.Join("\r\n", remaining.Select(e => e.Path)) + DescribePreserved(preserved, failures);
            }
            foreach (var entry in remaining) failures.Add(entry.Path + ": still in use or access denied; preserved. Restart, then remove only this listed app file if necessary.");
            using (var key = Registry.CurrentUser.OpenSubKey(RunKey, true))
            {
                if (key != null)
                {
                    var existing = key.GetValue(registration, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                    if (existing != null && String.Equals(existing as string, command, StringComparison.Ordinal)) key.DeleteValue(registration, false);
                    else if (existing != null) failures.Add("Cleanup startup registration was replaced and preserved: " + registration);
                }
            }
            report(96, "Removing temporary cleanup files...");
            // PowerShell loads the script into memory. Unlike a mapped executable it can remove its own source safely.
            foreach (var entry in new[] { scriptEntry(plan, script), stateEntry })
            {
                try { SafeFiles.DeleteApproved(plan, entry); }
                catch (Exception error) { if (!Missing(error)) failures.Add(entry.Path + ": " + error.Message); }
            }
            var workspaceRoot = plan.Roots.Single(e => e.Category == "workspace");
            try { SafeFiles.DeleteApproved(plan, workspaceRoot); }
            catch (Exception error) { if (!Missing(error)) failures.Add(workspaceRoot.Path + ": " + error.Message); }
            if (plan.RecoveryReceipt != "")
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\TrayBridge\Cleanup", true))
                {
                    if (key != null && String.Equals(key.GetValue(registration) as string, plan.RecoveryReceipt, StringComparison.Ordinal)) key.DeleteValue(registration, false);
                }
            }
            return (failures.Count == 0 && preserved.Count == 0 ? "TrayBridge has been uninstalled." : "Uninstall finished with some files preserved. Review the paths below.") + "\r\n\r\nA restart may be required to finish uninstallation. You can restart when convenient." + DescribePreserved(preserved, failures);
        }
        static RemovalEntry scriptEntry(RemovalPlan plan, string script) { return plan.Files.Single(e => e.Category == "workspace" && e.Path == script); }
        static string DescribePreserved(List<string> preserved, List<string> failures)
        {
            return (preserved.Count == 0 ? "" : "\r\n\r\nUnverified files preserved:\r\n" + String.Join("\r\n", preserved.Distinct())) + (failures.Count == 0 ? "" : "\r\n\r\nCould not remove:\r\n" + String.Join("\r\n", failures));
        }
    }
}
