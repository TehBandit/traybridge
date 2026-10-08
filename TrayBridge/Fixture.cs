using System.Text.Json;
namespace TrayBridge;
internal sealed class Fixture : ApplicationContext
{
    private readonly NotifyIcon icon;
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 60000 };
    private int clicks;
    internal Fixture()
    {
        icon = new() { Icon = SystemIcons.Information, Text = "Native tray routing test", Visible = true };
        icon.MouseClick += (_, e) => { clicks++; File.WriteAllText(Path.Combine(NativeHost.DataDirectory, "fixture-result.json"), JsonSerializer.Serialize(new { Clicks = clicks, Button = e.Button.ToString() })); };
        var menu = new ContextMenuStrip(); menu.Items.Add("Test menu (safe to dismiss)"); icon.ContextMenuStrip = menu;
        timer.Tick += (_, _) => ExitThread(); timer.Start();
    }
    protected override void ExitThreadCore() { timer.Dispose(); icon.Dispose(); base.ExitThreadCore(); }
}
