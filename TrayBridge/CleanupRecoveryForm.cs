namespace TrayBridge;

internal sealed class CleanupRecoveryForm : Form
{
    private readonly ListBox operations = new() { Dock = DockStyle.Fill, DisplayMember = "Name", AccessibleName = "Cleanup operations" };
    private readonly TextBox details = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, AccessibleName = "Cleanup recovery details" };
    private readonly Button retry = new() { Text = "Retry cleanup", AutoSize = true };
    private readonly Button stop = new() { Text = "Stop automatic cleanup", AutoSize = true };
    private readonly Button open = new() { Text = "Open TrayBridge", AutoSize = true };
    internal CleanupRecoveryForm()
    {
        Text = "TrayBridge cleanup recovery"; ClientSize = new(740, 470); MinimumSize = new(640, 420);
        StartPosition = FormStartPosition.CenterScreen; Font = new("Segoe UI", 10);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new(16), ColumnCount = 1, RowCount = 4 };
        layout.RowStyles.Add(new(SizeType.Absolute, 56)); layout.RowStyles.Add(new(SizeType.Absolute, 84));
        layout.RowStyles.Add(new(SizeType.Percent, 100)); layout.RowStyles.Add(new(SizeType.Absolute, 48));
        layout.Controls.Add(new Label { Dock = DockStyle.Fill, Text = "An earlier cleanup operation needs attention. Inspect its details, retry verified cleanup, or stop its automatic registration. Remaining files are preserved when you stop cleanup." });
        layout.Controls.Add(operations); layout.Controls.Add(details);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var close = new Button { Text = "Close", AutoSize = true, DialogResult = DialogResult.Cancel };
        buttons.Controls.Add(close); buttons.Controls.Add(open); buttons.Controls.Add(stop); buttons.Controls.Add(retry);
        layout.Controls.Add(buttons); Controls.Add(layout); CancelButton = close; AcceptButton = close;
        operations.SelectedIndexChanged += (_, _) => RefreshSelection();
        stop.Click += (_, _) =>
        {
            if (operations.SelectedItem is not CleanupOperation operation) return;
            if (MessageBox.Show(this, "Stop automatic cleanup for this operation? Remaining files and recovery information will be kept. You can then open TrayBridge.", "Stop cleanup", MessageBoxButtons.OKCancel, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.OK) return;
            try { CleanupRecovery.Stop(operation); Reload(); } catch (Exception error) { details.Text = error.Message; }
        };
        retry.Click += async (_, _) =>
        {
            if (operations.SelectedItem is not CleanupOperation operation) return;
            if (MessageBox.Show(this, "Retry removal of the approved files listed above? This finishes the earlier uninstall and cannot be undone.", "Retry cleanup", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.OK) return;
            buttons.Enabled = false;
            try { await CleanupRecovery.Retry(operation); DialogResult = DialogResult.Cancel; Close(); }
            catch (Exception error) { details.Text = error.Message; buttons.Enabled = true; }
        };
        open.Click += (_, _) => { if (!CleanupRecovery.Scan().Any(operation => operation.BlocksStartup)) { DialogResult = DialogResult.OK; Close(); } else Reload(); };
        Reload();
    }
    private void Reload()
    {
        operations.DataSource = CleanupRecovery.Scan().ToList(); RefreshSelection();
        open.Enabled = !((List<CleanupOperation>)operations.DataSource).Any(operation => operation.BlocksStartup);
    }
    private void RefreshSelection()
    {
        var operation = operations.SelectedItem as CleanupOperation;
        details.Text = operation?.Details ?? "No pending cleanup operations.";
        retry.Enabled = operation?.Retryable == true;
        stop.Enabled = operation?.Verified == true && operation.Command is not null;
    }
}
