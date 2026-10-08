using Microsoft.Win32;
using System.Diagnostics;
using System.Text.Json;

namespace TrayBridge;
internal sealed class TrayApplication : ApplicationContext
{
    internal AppSettings Settings { get; } = AppSettings.Load();
    internal IReadOnlyList<DisplayInfo> Monitors { get; private set; } = Displays.Read();
    internal event Action? Updated;
    internal string Status { get; private set; } = "Disabled — Windows' original taskbars are active.";
    private readonly NotifyIcon tray;
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 2000 };
    private readonly EventWaitHandle showRequest = new(false, EventResetMode.AutoReset, @"Local\TrayBridge.Show");
    private ManagerForm? window;
    private bool busy, attached, exiting, recovery;
    internal TrayApplication(bool hidden)
    {
        tray = new NotifyIcon { Icon = MakeIcon(), Text = "TrayBridge — manage your trays", Visible = true };
        tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) ShowManager(); };
        var menu = new ContextMenuStrip();
        menu.Items.Add("Manage trays…", null, (_, _) => ShowManager());
        menu.Items.Add("Show all icons on all monitors", null, async (_, _) => { recovery = true; await Enable(); });
        menu.Items.Add("Restore saved assignments", null, async (_, _) => { recovery = false; await Enable(); });
        menu.Items.Add("Disable and restore Windows taskbars", null, async (_, _) => await Disable());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, async (_, _) => { exiting = true; await Disable(false); ExitThread(); });
        tray.ContextMenuStrip = menu;
        timer.Tick += async (_, _) => await Tick(); timer.Start();
        Settings.Save(Monitors);
        if (!hidden) ShowManager();
        if (Settings.Active) _ = Enable();
    }
    private void ShowManager()
    {
        window ??= new ManagerForm(this);
        window.Show(); window.WindowState = FormWindowState.Normal; window.Activate();
    }
    internal async Task Enable()
    {
        if (busy || exiting) return;
        busy = true;
        try
        {
            Status = "Preparing matching Windows symbols…"; Updated?.Invoke();
            await NativeHost.PrepareSymbols(message => { Status = message; Updated?.Invoke(); });
            Settings.Active = true; Persist();
            var code = await Task.Run(NativeHost.Start);
            if (code != 0) throw new InvalidOperationException($"The taskbar helper returned {code}.");
            attached = true;
            Status = "Starting trays…";
        }
        catch (Exception error) { Settings.Active = false; Settings.Save(Monitors); Status = error.Message; }
        finally { busy = false; Updated?.Invoke(); }
    }
    internal async Task Disable(bool savePreference = true)
    {
        if (savePreference) Settings.Active = false;
        if (savePreference) Settings.Save(Monitors);
        try { if (attached) await Task.Run(NativeHost.Stop); }
        catch (Exception error) { Status = error.Message; Updated?.Invoke(); return; }
        attached = false;
        Status = "Disabled — Windows' original taskbars are active."; Updated?.Invoke();
    }
    internal void Save()
    {
        recovery = false; Persist();
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
        if (Settings.StartWithWindows) key?.SetValue("TrayBridge", $"\"{Environment.ProcessPath}\" --hidden");
        else key?.DeleteValue("TrayBridge", false);
        Updated?.Invoke();
    }
    private void Persist()
    {
        Settings.Save(Monitors);
        if (recovery) AppSettings.WriteNative(new AppSettings { Active = Settings.Active }.Resolve(Monitors));
    }
    private async Task Tick()
    {
        if (showRequest.WaitOne(0)) ShowManager();
        if (busy || exiting) return;
        var current = Displays.Read();
        if (!current.SequenceEqual(Monitors)) { Monitors = current; Persist(); }
        if (Settings.Active)
        {
            try
            {
                var path = Path.Combine(NativeHost.DataDirectory, "status-explorer.json");
                using var status = JsonDocument.Parse(File.ReadAllText(path));
                using var shell = Process.GetProcessesByName("explorer").FirstOrDefault(p => p.SessionId == Process.GetCurrentProcess().SessionId);
                if (shell is null) { attached = false; Status = "Waiting for the desktop shell…"; Updated?.Invoke(); return; }
                if (status.RootElement.GetProperty("pid").GetInt32() != shell.Id) { attached = false; await Enable(); }
                else
                {
                    var state = status.RootElement.GetProperty("state").GetString();
                    Status = state == "active" ? $"Active · {Monitors.Count} connected monitors" : state == "error" ? status.RootElement.GetProperty("detail").GetString() ?? "Native helper failed." : state ?? "Starting…";
                }
            }
            catch (IOException) { if (!attached) await Enable(); }
            catch (JsonException) { }
            catch (InvalidOperationException) { attached = false; Status = "Waiting for the desktop shell…"; }
        }
        Updated?.Invoke();
    }
    protected override void ExitThreadCore()
    {
        timer.Stop(); timer.Dispose(); showRequest.Dispose(); tray.Visible = false; tray.Icon?.Dispose(); tray.Dispose();
        window?.Dispose(); base.ExitThreadCore();
    }
    private static Icon MakeIcon()
    {
        using var bitmap = new Bitmap(32, 32);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        graphics.Clear(Color.Transparent);
        using var brush = new SolidBrush(Color.FromArgb(38, 190, 185));
        graphics.FillEllipse(brush, 1, 1, 30, 30);
        using var pen = new Pen(Color.White, 2.5f);
        graphics.DrawRectangle(pen, 7, 8, 12, 9); graphics.DrawLine(pen, 13, 18, 13, 22);
        graphics.DrawLine(pen, 9, 22, 17, 22); graphics.DrawLine(pen, 21, 13, 25, 13); graphics.DrawLine(pen, 23, 11, 23, 15);
        var handle = bitmap.GetHicon();
        try { using var icon = Icon.FromHandle(handle); return (Icon)icon.Clone(); }
        finally { DestroyIcon(handle); }
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
}
