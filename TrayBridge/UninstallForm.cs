using TrayBridge.UninstallSupport;

namespace TrayBridge;

internal sealed class UninstallForm : Form
{
    private readonly Label heading = new() { Dock = DockStyle.Fill, Text = "Checking TrayBridge files…", AutoEllipsis = true };
    private readonly ProgressBar progress = new() { Dock = DockStyle.Fill, Style = ProgressBarStyle.Marquee };
    private readonly TextBox details = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical };
    private readonly Button uninstall = new() { Text = "Uninstall", Enabled = false, Width = 100 };
    private readonly Button cancel = new() { Text = "Cancel", Width = 100, DialogResult = DialogResult.Cancel };
    private readonly TrayApplication app;
    private RemovalPlan? plan;
    private bool running;

    internal UninstallForm(TrayApplication app)
    {
        this.app = app;
        Text = "Uninstall TrayBridge"; ClientSize = new(570, 390); MinimumSize = new(520, 350);
        StartPosition = FormStartPosition.CenterParent; MaximizeBox = false; MinimizeBox = false;
        Font = new("Segoe UI", 10); BackColor = UiTheme.Background; ForeColor = UiTheme.Text;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new(18), ColumnCount = 1, RowCount = 4 };
        foreach (var style in new[] { new RowStyle(SizeType.Absolute, 52), new RowStyle(SizeType.Absolute, 28), new RowStyle(SizeType.Percent, 100), new RowStyle(SizeType.Absolute, 46) }) layout.RowStyles.Add(style);
        details.BackColor = UiTheme.Surface; details.ForeColor = UiTheme.Text;
        progress.AccessibleName = "Uninstall progress"; details.AccessibleName = "Files and settings affected by uninstall";
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        buttons.Controls.Add(cancel); buttons.Controls.Add(uninstall);
        layout.Controls.Add(heading, 0, 0); layout.Controls.Add(progress, 0, 1); layout.Controls.Add(details, 0, 2); layout.Controls.Add(buttons, 0, 3); Controls.Add(layout);
        CancelButton = cancel; AcceptButton = cancel;
        FormClosing += (_, e) => { if (running && e.CloseReason == CloseReason.UserClosing) e.Cancel = true; };
        Shown += async (_, _) =>
        {
            try
            {
                plan = await Task.Run(Uninstaller.Preview);
                if (IsDisposed) return;
                heading.Text = "Remove TrayBridge and its verified local data?";
                details.Text = "This will restore Windows taskbars and remove this copy's program files, saved assignments, global configuration, preferences, inventories and verified caches. This cannot be undone.\r\n\r\nProgram: " + plan.Release + "\r\nLocal data: " + plan.Data +
                    (plan.Runtime == "" ? "" : "\r\nRuntime cache: " + plan.Runtime) + Uninstaller.StartupWarning() + "\r\n\r\nA restart may be required to finish uninstallation. Locked files can be cleaned up at your next sign-in; TrayBridge will stay disabled.\r\n\r\n" +
                    "Verified files to remove:\r\n" + string.Join("\r\n", plan.Files.Select(e => e.Path)) +
                    (plan.Preserved.Count == 0 ? "" : "\r\n\r\nThese unverified items will be preserved:\r\n" + string.Join("\r\n", plan.Preserved));
                progress.Style = ProgressBarStyle.Continuous; progress.Value = 0; uninstall.Enabled = !app.Busy;
            }
            catch (Exception error)
            {
                if (IsDisposed) return;
                heading.Text = "Uninstall is unavailable"; details.Text = error.Message; progress.Style = ProgressBarStyle.Continuous;
            }
        };
        uninstall.Click += async (_, _) =>
        {
            if (plan is null || app.Busy) return;
            running = true; uninstall.Enabled = cancel.Enabled = false;
            progress.Style = ProgressBarStyle.Marquee; heading.Text = "Restoring Windows taskbars…";
            try
            {
                heading.Text = "Checking cleanup capabilities…";
                await Uninstaller.Preflight(plan);
                heading.Text = "Restoring Windows taskbars…";
                await app.PrepareUninstall();
                heading.Text = "Starting cleanup…";
                await Uninstaller.Launch(plan);
                running = false;
                app.FinishUninstall();
                Close();
            }
            catch (Exception error)
            {
                if (app.Uninstalling) app.CancelUninstall(); running = false;
                heading.Text = "Uninstall stopped before program-file removal";
                details.Text = error.Message + "\r\n\r\nTrayBridge's program files have been preserved. Close this dialog, resolve the issue, and try again.";
                progress.Style = ProgressBarStyle.Continuous; progress.Value = 0; cancel.Enabled = true; cancel.Text = "Close";
            }
        };
    }
}
