using System.Text.Json;

namespace TrayBridge;
internal sealed class ManagerForm : Form
{
    private readonly TrayApplication app;
    private readonly TableLayoutPanel monitors = new() { Dock = DockStyle.Fill, ColumnCount = 2, AutoScroll = true, AccessibleName = "Connected monitors" };
    private readonly CheckBox selectAll = new() { Text = "Select/deselect all", AutoSize = true, AutoCheck = false };
    private readonly List<MonitorRow> monitorRows = [];
    private readonly Dictionary<string, TrayRule> drafts = [];
    private string? viewedMonitor;
    private string? editorScope;
    private readonly Button applyButton;
    private readonly Button setGlobalButton, revertGlobalButton, resetWindowsButton, enableButton, disableButton;
    private readonly CheckedListBox icons = new() { Dock = DockStyle.Fill, CheckOnClick = true, IntegralHeight = false, AccessibleName = "Application icons" };
    private readonly CheckedListBox systemIcons = new() { Dock = DockStyle.Fill, CheckOnClick = true, IntegralHeight = false, AccessibleName = "Windows system icons" };
    private readonly CheckBox allSystem = new() { Text = "All system icons (including new indicators)", AutoSize = true, Checked = true };
    private readonly CheckBox enabled = new() { Text = "Show tray on marked monitors", AutoSize = true, Checked = true };
    private readonly CheckBox all = new() { Text = "All applications (including new icons)", AutoSize = true, Checked = true };
    private readonly CheckBox quick = new() { Text = "Network, volume and battery / Quick Settings", AutoSize = true, Checked = true };
    private readonly CheckBox clock = new() { Text = "Clock, calendar and notifications", AutoSize = true, Checked = true };
    private readonly CheckBox indicators = new() { Text = "Language, input and privacy indicators", AutoSize = true, Checked = true };
    private readonly CheckBox startup = new() { Text = "Start at sign-in", AutoSize = true };
    private readonly Label status = new() { Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Label target = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Label editorTitle = new() { Dock = DockStyle.Fill };
    private readonly Label notice = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
    private readonly ToolTip tips = new();
    private bool loading;
    private bool updatingIcons;
    private readonly Dictionary<string, IconChoice> catalog = new();
    private readonly Dictionary<string, IconChoice> systemCatalog = new();
    private string topology = "";
    internal ManagerForm(TrayApplication application)
    {
        app = application; Text = "TrayBridge"; MinimumSize = new(960, 740); Size = new(1100, 860);
        StartPosition = FormStartPosition.CenterScreen; Font = new("Segoe UI Variable Text", 10);
        BackColor = UiTheme.Background; ForeColor = UiTheme.Text;
        var layout = Rows(BackColor, 86, -1, 84, 94); layout.Padding = new(28, 20, 28, 24);
        var header = Rows(BackColor, 43, 30);
        header.Controls.Add(Label("TrayBridge", 25, true), 0, 0);
        header.Controls.Add(Label("Make each taskbar yours. Choose where your icons belong.", 10, muted: true), 0, 1);
        layout.Controls.Add(header, 0, 0);

        var content = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = new(0), BackColor = BackColor };
        content.ColumnStyles.Add(new(SizeType.Absolute, 280)); content.ColumnStyles.Add(new(SizeType.Absolute, 20)); content.ColumnStyles.Add(new(SizeType.Percent, 100));
        content.RowStyles.Add(new(SizeType.Percent, 100));
        var displayCard = new SettingsCard { Dock = DockStyle.Fill, Margin = new(0) };
        var left = Rows(UiTheme.Surface, 32, 44, 38, -1);
        left.Controls.Add(Label("Displays", 15, true), 0, 0);
        left.Controls.Add(Label("Click a name to view settings.\nCheck boxes to mark apply targets.", 9, muted: true), 0, 1);
        left.Controls.Add(selectAll, 0, 2); left.Controls.Add(monitors, 0, 3);
        monitors.ColumnStyles.Add(new(SizeType.Absolute, 30)); monitors.ColumnStyles.Add(new(SizeType.Percent, 100));
        monitors.BackColor = UiTheme.Surface; displayCard.Controls.Add(left); content.Controls.Add(displayCard, 0, 0);

        var editorCard = new SettingsCard { Dock = DockStyle.Fill, Margin = new(0) };
        var editor = Rows(UiTheme.Surface, 32, 32, 40, 38, -1);
        editorTitle.Font = new(Font.FontFamily, 15, FontStyle.Bold);
        target.ForeColor = UiTheme.Muted; target.Font = new(Font.FontFamily, 9);
        editor.Controls.Add(editorTitle, 0, 0); editor.Controls.Add(target, 0, 1); editor.Controls.Add(enabled, 0, 2);

        var pages = new Panel { Dock = DockStyle.Fill, Margin = new(0), BackColor = UiTheme.Surface };
        var appsPage = Rows(UiTheme.Surface, 36, -1); appsPage.Controls.Add(all, 0, 0); appsPage.Controls.Add(icons, 0, 1);
        var systemPage = Rows(UiTheme.Surface, 32, 32, 32, 36, -1); systemPage.Visible = false;
        systemPage.Controls.Add(quick, 0, 0); systemPage.Controls.Add(clock, 0, 1); systemPage.Controls.Add(indicators, 0, 2); systemPage.Controls.Add(allSystem, 0, 3); systemPage.Controls.Add(systemIcons, 0, 4);
        pages.Controls.Add(appsPage); pages.Controls.Add(systemPage);
        var tabs = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = new(0), BackColor = UiTheme.Surface };
        var appsTab = Button("Applications", (_, _) => { appsPage.Visible = true; systemPage.Visible = false; appsPage.BringToFront(); });
        var systemTab = Button("Windows controls & indicators", (_, _) => { appsPage.Visible = false; systemPage.Visible = true; systemPage.BringToFront(); }, width: 242);
        ((ModernButton)appsTab).Selected = true;
        appsTab.Click += (_, _) => { ((ModernButton)appsTab).Selected = true; ((ModernButton)systemTab).Selected = false; appsTab.Invalidate(); systemTab.Invalidate(); };
        systemTab.Click += (_, _) => { ((ModernButton)appsTab).Selected = false; ((ModernButton)systemTab).Selected = true; appsTab.Invalidate(); systemTab.Invalidate(); };
        tabs.Controls.Add(appsTab); tabs.Controls.Add(systemTab); editor.Controls.Add(tabs, 0, 3); editor.Controls.Add(pages, 0, 4);
        editorCard.Controls.Add(editor); content.Controls.Add(editorCard, 2, 0); layout.Controls.Add(content, 0, 1);

        var actions = Rows(BackColor, 36, 38); actions.Padding = new(0, 8, 0, 0);
        notice.ForeColor = UiTheme.Muted; notice.Font = new(Font.FontFamily, 9);
        notice.Text = "Apply changes to marked displays, or save your complete setup as a global config.";
        actions.Controls.Add(notice, 0, 0);
        var actionRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new(0), BackColor = BackColor };
        actionRow.ColumnStyles.Add(new(SizeType.Percent, 100)); actionRow.ColumnStyles.Add(new(SizeType.Absolute, 108));
        actionRow.RowStyles.Add(new(SizeType.Percent, 100));
        var configButtons = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = new(0), BackColor = BackColor };
        setGlobalButton = Button("Set global config", (_, _) => { app.Settings.CaptureGlobalConfiguration(); app.Save(); notice.Text = "Global config saved from the applied setup. Pending edits are kept."; });
        revertGlobalButton = Button("Revert to global config", async (_, _) => { if (await app.RevertGlobalConfiguration()) { drafts.Clear(); LoadRule(); notice.Text = "Saved global config restored across all displays."; } else notice.Text = app.Status; }, width: 180);
        resetWindowsButton = Button("Reset to Windows", async (_, _) => { if (await app.ResetWindows()) { drafts.Clear(); LoadRule(); notice.Text = "Windows taskbars restored. Your saved global config is kept."; } else notice.Text = app.Status; }, width: 160);
        configButtons.Controls.Add(setGlobalButton); configButtons.Controls.Add(revertGlobalButton); configButtons.Controls.Add(resetWindowsButton);
        applyButton = Button("Apply", (_, _) => Apply(), primary: true, width: 108); applyButton.Dock = DockStyle.Fill; applyButton.Margin = new(0);
        actionRow.Controls.Add(configButtons, 0, 0); actionRow.Controls.Add(applyButton, 1, 0); actions.Controls.Add(actionRow, 0, 1); layout.Controls.Add(actions, 0, 2);
        tips.SetToolTip(setGlobalButton, "Save the applied setup across all displays. Apply pending edits first to include them.");
        tips.SetToolTip(revertGlobalButton, "Restore the saved setup across all displays, including its enabled or disabled state.");
        tips.SetToolTip(resetWindowsButton, "Restore native Windows taskbars and clear current assignments. Keep your saved global config and sign-in preference.");

        var serviceCard = new SettingsCard { Dock = DockStyle.Fill, Margin = new(0, 14, 0, 0), Padding = new(18, 12, 18, 12) };
        var service = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new(0), BackColor = UiTheme.Surface };
        service.ColumnStyles.Add(new(SizeType.Percent, 100)); service.ColumnStyles.Add(new(SizeType.Absolute, 348));
        service.RowStyles.Add(new(SizeType.Percent, 100));
        var serviceState = Rows(UiTheme.Surface, 25, -1); serviceState.Controls.Add(Label("Tray service", 11, true), 0, 0);
        status.Font = new(Font.FontFamily, 9); status.ForeColor = UiTheme.Muted; serviceState.Controls.Add(status, 0, 1);
        var serviceButtons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Margin = new(0), Padding = new(0, 6, 0, 0), BackColor = UiTheme.Surface };
        disableButton = Button("Disable", async (_, _) => await app.Disable(), width: 88);
        enableButton = Button("Enable", async (_, _) => await app.Enable(), width: 88);
        startup.Margin = new(4, 9, 14, 0); serviceButtons.Controls.Add(disableButton); serviceButtons.Controls.Add(enableButton); serviceButtons.Controls.Add(startup);
        service.Controls.Add(serviceState, 0, 0); service.Controls.Add(serviceButtons, 1, 0); serviceCard.Controls.Add(service); layout.Controls.Add(serviceCard, 0, 3);
        Controls.Add(layout); AcceptButton = applyButton;
        foreach (var list in new[] { icons, systemIcons }) { list.BackColor = UiTheme.Surface; list.ForeColor = ForeColor; list.BorderStyle = BorderStyle.None; list.Font = new(Font.FontFamily, 11); }
        foreach (var control in new[] { selectAll, enabled, all, quick, clock, indicators, allSystem, startup }) { control.BackColor = UiTheme.Surface; control.ForeColor = ForeColor; }
        ConnectSelection(icons, all);
        ConnectSelection(systemIcons, allSystem);
        selectAll.Click += (_, _) => MarkAll(selectAll.CheckState != CheckState.Checked);
        selectAll.CheckStateChanged += (_, _) => { if (!loading) MarkAll(selectAll.CheckState == CheckState.Checked); };
        startup.Checked = app.Settings.StartWithWindows;
        startup.CheckedChanged += (_, _) => { app.Settings.StartWithWindows = startup.Checked; app.Save(); };
        FormClosing += (_, e) => { if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); } };
        app.Updated += RefreshState; Disposed += (_, _) => { app.Updated -= RefreshState; tips.Dispose(); };
        RefreshState(); LoadRule();
    }
    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); UiTheme.WindowStyle(Handle); }
    private static TableLayoutPanel Rows(Color color, params int[] heights)
    {
        var result = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = heights.Length, Margin = new(0), BackColor = color };
        result.ColumnStyles.Add(new(SizeType.Percent, 100));
        foreach (var height in heights) result.RowStyles.Add(height < 0 ? new(SizeType.Percent, 100) : new(SizeType.Absolute, height));
        return result;
    }
    private Label Label(string text, float size, bool bold = false, bool muted = false) => new() { Text = text, Dock = DockStyle.Fill, Margin = new(0), TextAlign = ContentAlignment.MiddleLeft, Font = new(Font.FontFamily, size, bold ? FontStyle.Bold : FontStyle.Regular), ForeColor = muted ? UiTheme.Muted : UiTheme.Text };
    private static Button Button(string text, EventHandler click, bool primary = false, int width = 150) { var button = new ModernButton { Text = text.Replace("&", "&&"), AccessibleName = text, Primary = primary, Width = width }; button.Click += click; return button; }
    private void SaveDraft() { if (editorScope is not null) drafts[editorScope] = ReadRule(); }
    private void ViewMonitor(string id)
    {
        if (viewedMonitor == id) return;
        SaveDraft(); viewedMonitor = id;
        foreach (var row in monitorRows) row.View.Checked = row.Display.Id == id;
        LoadRule();
    }
    private void UpdateTargets()
    {
        int count = monitorRows.Count(row => row.Mark.Checked);
        bool previous = loading; loading = true;
        try { selectAll.CheckState = count == 0 ? CheckState.Unchecked : count == monitorRows.Count ? CheckState.Checked : CheckState.Indeterminate; }
        finally { loading = previous; }
        var display = monitorRows.FirstOrDefault(row => row.Display.Id == viewedMonitor)?.Display;
        editorTitle.Text = display?.Name ?? "Tray settings";
        target.Text = $"{display?.Device.Replace(@"\\.\", "") ?? "No monitor"}  ·  Changes apply to {count} marked display{(count == 1 ? "" : "s")}";
        applyButton.Enabled = count > 0;
        setGlobalButton.Enabled = !app.Busy;
        revertGlobalButton.Enabled = app.Settings.GlobalConfiguration is not null && !app.Busy;
        resetWindowsButton.Enabled = !app.Busy;
        enableButton.Enabled = !app.Settings.Active && !app.Busy;
        disableButton.Enabled = app.Settings.Active && !app.Busy;
    }
    private void MarkAll(bool check)
    {
        loading = true;
        try { foreach (var row in monitorRows) row.Mark.Checked = check; }
        finally { loading = false; }
        UpdateTargets();
    }
    private void RefreshMonitors(string signature)
    {
        SaveDraft();
        bool initial = topology.Length == 0;
        var marked = monitorRows.Where(row => row.Mark.Checked).Select(row => row.Display.Id).ToHashSet();
        loading = true;
        try
        {
            foreach (Control control in monitors.Controls.Cast<Control>().ToArray()) control.Dispose();
            monitors.Controls.Clear(); monitors.RowStyles.Clear(); monitors.RowCount = 0; monitorRows.Clear();
            if (!app.Monitors.Any(display => display.Id == viewedMonitor)) viewedMonitor = app.Monitors.FirstOrDefault()?.Id;
            foreach (var display in app.Monitors)
            {
                var label = $"{display.Device.Replace(@"\\.\", "")} · {display.Name}{(display.Primary ? " (primary)" : "")}";
                var mark = new CheckBox { Dock = DockStyle.Fill, Margin = new(0), AccessibleName = "Apply to " + label, Checked = initial ? display.Primary : marked.Contains(display.Id) };
                mark.BackColor = UiTheme.Surface;
                var view = new MonitorSelector { Text = label, AccessibleName = "View " + label, DisplayName = display.Name, Detail = display.Device.Replace(@"\\.\", "") + (display.Primary ? " · Primary" : " · Secondary"), Checked = display.Id == viewedMonitor, BackColor = UiTheme.Surface, ForeColor = ForeColor };
                int index = monitors.RowCount++; monitors.RowStyles.Add(new(SizeType.Absolute, 74));
                monitors.Controls.Add(mark, 0, index); monitors.Controls.Add(view, 1, index);
                monitorRows.Add(new(display, mark, view));
                mark.CheckedChanged += (_, _) => { if (!loading) UpdateTargets(); };
                view.Click += (_, _) => ViewMonitor(display.Id);
                view.CheckedChanged += (_, _) => { if (!loading && view.Checked) ViewMonitor(display.Id); };
            }
            monitors.RowCount++; monitors.RowStyles.Add(new(SizeType.Percent, 100)); topology = signature;
        }
        finally { loading = false; }
        LoadRule();
    }
    private void UpdateIcons(Action update)
    {
        var previous = updatingIcons; updatingIcons = true;
        try { update(); } finally { updatingIcons = previous; }
    }
    private void ConnectSelection(CheckedListBox list, CheckBox includeAll)
    {
        includeAll.CheckedChanged += (_, _) =>
        {
            if (updatingIcons || !includeAll.Checked) return;
            UpdateIcons(() => { for (int i = 0; i < list.Items.Count; i++) list.SetItemChecked(i, true); });
        };
        list.ItemCheck += (_, e) =>
        {
            // ItemCheck runs before the clicked item's state changes. Preserve all
            // other selections when the user first excludes an icon from all mode.
            if (!updatingIcons && includeAll.Checked && e.NewValue == CheckState.Unchecked)
                includeAll.Checked = false;
        };
    }
    private void AddChoice(CheckedListBox list, IconChoice choice, bool selected) => UpdateIcons(() => list.Items.Add(choice, selected));
    private void RefreshState()
    {
        if (IsDisposed) return;
        var seenApps = new HashSet<string>(); var seenSystems = new HashSet<string>();
        status.Text = app.Status;
        UpdateTargets();
        var signature = string.Join("|", app.Monitors.Select(d => d.Id + d.Device));
        if (signature != topology) RefreshMonitors(signature);
        try
        {
            using var snapshot = JsonDocument.Parse(File.ReadAllText(Path.Combine(NativeHost.DataDirectory, "inventory.json")));
            foreach (var taskbar in snapshot.RootElement.GetProperty("taskbars").EnumerateArray())
                foreach (var element in taskbar.GetProperty("elements").EnumerateArray())
                    if (element.GetProperty("class").GetString() == "SystemTray.NotifyIconView" && element.TryGetProperty("identity", out var id) && !string.IsNullOrWhiteSpace(id.GetString()))
                    {
                        var text = element.GetProperty("text").GetString() ?? "Application icon";
                        if (text.StartsWith("TrayBridge", StringComparison.Ordinal)) continue;
                        var label = text.Split('\n')[0].Trim();
                        if (string.IsNullOrWhiteSpace(label)) label = element.TryGetProperty("appName", out var appName) ? appName.GetString() ?? "Application icon" : "Application icon";
                        if (label.Length > 85) label = label[..85] + "…";
                        var key = id.GetString()!;
                        seenApps.Add(key);
                        app.Settings.IconLabels[key] = label;
                        if (!catalog.ContainsKey(key)) { var choice = new IconChoice(key, label); catalog[key] = choice; AddChoice(icons, choice, all.Checked || CurrentRule().Icons.Contains(key)); }
                    }
                    else if (element.GetProperty("class").GetString() == "SystemTray.IconView" && element.TryGetProperty("identity", out var systemId) && !string.IsNullOrWhiteSpace(systemId.GetString()))
                    {
                        var key = systemId.GetString()!;
                        seenSystems.Add(key);
                        var text = element.GetProperty("text").GetString() ?? "Windows indicator";
                        // Avoid listing time-of-day or network SSIDs as icon identities.
                        var label = text.StartsWith("Clock") ? "Clock" : text.StartsWith("Notifications") ? "Notifications" : text.StartsWith("Network") ? "Network" : text.StartsWith("Volume") ? "Volume" : text.StartsWith("Power") || text.StartsWith("Battery") ? "Battery / power" : text.Split('\n')[0];
                        if (label.Length > 85) label = label[..85] + "…";
                        app.Settings.IconLabels[key] = label;
                        if (!systemCatalog.ContainsKey(key)) { var choice = new IconChoice(key, label); systemCatalog[key] = choice; AddChoice(systemIcons, choice, allSystem.Checked || CurrentRule().SystemIcons.Contains(key)); }
                    }
        }
        catch (IOException) { } catch (JsonException) { }
        try
        {
            using var snapshot = JsonDocument.Parse(File.ReadAllText(Path.Combine(NativeHost.DataDirectory, "hidden-inventory.json")));
            foreach (var element in snapshot.RootElement.GetProperty("icons").EnumerateArray())
            {
                var id = element.GetProperty("identity").GetString(); var text = element.GetProperty("text").GetString() ?? "";
                if (string.IsNullOrWhiteSpace(id) || text.StartsWith("TrayBridge")) continue;
                seenApps.Add(id);
                if (catalog.ContainsKey(id)) continue;
                var label = text.Split('\n')[0];
                if (string.IsNullOrWhiteSpace(label)) label = element.GetProperty("appName").GetString() ?? "Application icon";
                if (label.Length > 85) label = label[..85] + "…";
                app.Settings.IconLabels[id] = label;
                var choice = new IconChoice(id, label + " (hidden icons)"); catalog[id] = choice; AddChoice(icons, choice, all.Checked || CurrentRule().Icons.Contains(id));
            }
        }
        catch (IOException) { } catch (JsonException) { }
        var rules = app.Settings.Overrides.Values.Concat([app.Settings.Defaults]).Concat(drafts.Values).Append(ReadRule()).ToArray();
        var savedApps = rules.SelectMany(r => r.Icons.Concat(r.ExcludedIcons)).ToHashSet();
        var savedSystems = rules.SelectMany(r => r.SystemIcons).ToHashSet();
        UpdateIcons(() => Prune(icons, catalog, seenApps, savedApps, all.Checked));
        UpdateIcons(() => Prune(systemIcons, systemCatalog, seenSystems, savedSystems, allSystem.Checked));
        foreach (var id in savedApps.Where(id => !id.StartsWith("app:" + Environment.ProcessPath + "#", StringComparison.OrdinalIgnoreCase)))
            if (!catalog.ContainsKey(id)) { var choice = new IconChoice(id, app.Settings.IconLabels.GetValueOrDefault(id, "Application icon") + " (offline)"); catalog[id] = choice; AddChoice(icons, choice, all.Checked || CurrentRule().Icons.Contains(id)); }
    }
    private static void Prune(CheckedListBox list, Dictionary<string, IconChoice> choices, HashSet<string> current, HashSet<string> saved, bool includeAll)
    {
        for (int i = list.Items.Count - 1; i >= 0; i--)
        {
            var choice = (IconChoice)list.Items[i];
            if (!current.Contains(choice.Id) && !saved.Contains(choice.Id) && (includeAll || !list.GetItemChecked(i))) { choices.Remove(choice.Id); list.Items.RemoveAt(i); }
        }
    }
    private TrayRule CurrentRule()
    {
        var scope = viewedMonitor;
        if (scope is not null && drafts.TryGetValue(scope, out var draft)) return draft;
        var display = app.Monitors.FirstOrDefault(display => display.Id == viewedMonitor);
        return display is null ? app.Settings.Defaults : app.Settings.For(display);
    }
    private void LoadRule()
    {
        UpdateIcons(() =>
        {
            editorScope = viewedMonitor;
            var rule = CurrentRule(); enabled.Checked = rule.Enabled; all.Checked = rule.AllApplications; quick.Checked = rule.QuickSettings; clock.Checked = rule.Clock; indicators.Checked = rule.SystemIndicators; allSystem.Checked = rule.AllSystemIcons;
            for (int i = 0; i < icons.Items.Count; i++) icons.SetItemChecked(i, rule.AllApplications || rule.Icons.Contains(((IconChoice)icons.Items[i]).Id));
            for (int i = 0; i < systemIcons.Items.Count; i++) systemIcons.SetItemChecked(i, rule.AllSystemIcons || rule.SystemIcons.Contains(((IconChoice)systemIcons.Items[i]).Id));
        });
        UpdateTargets();
    }
    private TrayRule ReadRule() => new() { Enabled = enabled.Checked, AllApplications = all.Checked, Icons = all.Checked ? new() : icons.CheckedItems.Cast<IconChoice>().Select(i => i.Id).ToHashSet(), ExcludedIcons = all.Checked ? new() : icons.Items.Cast<IconChoice>().Where((_, index) => !icons.GetItemChecked(index)).Select(i => i.Id).ToHashSet(), AllSystemIcons = allSystem.Checked, SystemIcons = allSystem.Checked ? new() : systemIcons.CheckedItems.Cast<IconChoice>().Select(i => i.Id).ToHashSet(), QuickSettings = quick.Checked, Clock = clock.Checked, SystemIndicators = indicators.Checked };
    private void Apply()
    {
        var targets = monitorRows.Where(row => row.Mark.Checked).Select(row => row.Display).ToArray();
        if (targets.Length == 0) return;
        SaveDraft(); var rule = ReadRule();
        app.Settings.ApplyTo(targets, rule);
        foreach (var display in targets) drafts.Remove(display.Id);
        app.Save(); LoadRule();
        notice.Text = $"Applied to {targets.Length} marked display{(targets.Length == 1 ? "" : "s")}.";
    }
    private sealed record MonitorRow(DisplayInfo Display, CheckBox Mark, RadioButton View);
    private sealed record IconChoice(string Id, string Label) { public override string ToString() => Label; }
}
