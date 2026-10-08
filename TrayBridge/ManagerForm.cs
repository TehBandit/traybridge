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
    private const string GlobalScope = "global";
    private readonly Button applyButton;
    private readonly Button inheritButton;
    private readonly CheckedListBox icons = new() { Dock = DockStyle.Fill, CheckOnClick = true, IntegralHeight = false, AccessibleName = "Application icons" };
    private readonly CheckedListBox systemIcons = new() { Dock = DockStyle.Fill, CheckOnClick = true, IntegralHeight = false, AccessibleName = "Windows system icons" };
    private readonly CheckBox allSystem = new() { Text = "All system icons (including newly appearing indicators)", Dock = DockStyle.Top, Height = 35, Checked = true };
    private readonly CheckBox global = new() { Text = "Edit global defaults", AutoSize = true };
    private readonly CheckBox enabled = new() { Text = "Show a tray on selected monitors", AutoSize = true, Checked = true };
    private readonly CheckBox all = new() { Text = "All applications (including new icons)", AutoSize = true, Checked = true };
    private readonly CheckBox quick = new() { Text = "Network, volume and battery / Quick Settings", AutoSize = true, Checked = true };
    private readonly CheckBox clock = new() { Text = "Clock, calendar and notifications", AutoSize = true, Checked = true };
    private readonly CheckBox indicators = new() { Text = "Language, input and privacy indicators", AutoSize = true, Checked = true };
    private readonly CheckBox startup = new() { Text = "Start at sign-in", AutoSize = true };
    private readonly Label status = new() { Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Label target = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
    private bool loading;
    private bool updatingIcons;
    private readonly Dictionary<string, IconChoice> catalog = new();
    private readonly Dictionary<string, IconChoice> systemCatalog = new();
    private string topology = "";
    internal ManagerForm(TrayApplication application)
    {
        app = application; Text = "TrayBridge"; MinimumSize = new(800, 560); Size = new(960, 680);
        StartPosition = FormStartPosition.CenterScreen; Font = new("Segoe UI", 10);
        BackColor = Color.FromArgb(24, 28, 36); ForeColor = Color.FromArgb(231, 235, 241);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new(24), ColumnCount = 2, RowCount = 5 };
        layout.ColumnStyles.Add(new(SizeType.Absolute, 285)); layout.ColumnStyles.Add(new(SizeType.Percent, 100));
        layout.RowStyles.Add(new(SizeType.Absolute, 68)); layout.RowStyles.Add(new(SizeType.Absolute, 48)); layout.RowStyles.Add(new(SizeType.Percent, 100)); layout.RowStyles.Add(new(SizeType.Absolute, 66)); layout.RowStyles.Add(new(SizeType.Absolute, 52));
        var title = new Label { Text = "Your trays, across your displays", Font = new("Segoe UI Semibold", 20), Dock = DockStyle.Fill };
        layout.Controls.Add(title, 0, 0); layout.SetColumnSpan(title, 2);
        layout.Controls.Add(new Label { Text = "CONNECTED MONITORS", AutoSize = true }, 0, 1);
        layout.Controls.Add(target, 1, 1);
        var left = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1, Padding = new(0, 0, 20, 0) };
        left.RowStyles.Add(new(SizeType.Absolute, 34)); left.RowStyles.Add(new(SizeType.Percent, 100)); left.RowStyles.Add(new(SizeType.Absolute, 44)); left.RowStyles.Add(new(SizeType.Absolute, 44));
        left.Controls.Add(selectAll, 0, 0); left.Controls.Add(monitors, 0, 1); left.Controls.Add(global, 0, 2); left.Controls.Add(startup, 0, 3);
        monitors.ColumnStyles.Add(new(SizeType.Absolute, 28)); monitors.ColumnStyles.Add(new(SizeType.Percent, 100));
        layout.Controls.Add(left, 0, 2);
        var right = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 7 };
        foreach (var control in new Control[] { enabled, all, quick, clock, indicators }) { right.RowStyles.Add(new(SizeType.Absolute, 32)); right.Controls.Add(control, 0, right.Controls.Count); }
        right.RowStyles.Add(new(SizeType.Absolute, 30)); right.Controls.Add(new Label { Text = "Uncheck an icon to customize; then Apply settings.", AutoSize = true }, 0, 5);
        var tabs = new TabControl { Dock = DockStyle.Fill };
        var appsPage = new TabPage("Applications"); appsPage.Controls.Add(icons);
        var systemPage = new TabPage("Windows controls & indicators"); systemPage.Controls.Add(systemIcons); systemPage.Controls.Add(allSystem);
        tabs.TabPages.Add(appsPage); tabs.TabPages.Add(systemPage);
        right.RowStyles.Add(new(SizeType.Percent, 100)); right.Controls.Add(tabs, 0, 6); layout.Controls.Add(right, 1, 2);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, Padding = new(0, 14, 0, 0) };
        applyButton = Button("Apply settings", (_, _) => Apply());
        inheritButton = Button("Use global defaults", (_, _) => UseDefaults());
        buttons.Controls.Add(applyButton); buttons.Controls.Add(inheritButton);
        buttons.Controls.Add(Button("Enable", async (_, _) => await app.Enable()));
        buttons.Controls.Add(Button("Disable", async (_, _) => await app.Disable()));
        layout.Controls.Add(buttons, 0, 3); layout.SetColumnSpan(buttons, 2);
        layout.Controls.Add(status, 0, 4); layout.SetColumnSpan(status, 2); Controls.Add(layout);
        foreach (var list in new[] { icons, systemIcons }) { list.BackColor = Color.FromArgb(34, 39, 49); list.ForeColor = ForeColor; list.BorderStyle = BorderStyle.None; }
        allSystem.BackColor = BackColor; allSystem.ForeColor = ForeColor;
        ConnectSelection(icons, all);
        ConnectSelection(systemIcons, allSystem);
        selectAll.Click += (_, _) => MarkAll(selectAll.CheckState != CheckState.Checked);
        selectAll.CheckStateChanged += (_, _) => { if (!loading) MarkAll(selectAll.CheckState == CheckState.Checked); };
        global.CheckedChanged += (_, _) => { SaveDraft(); monitors.Enabled = selectAll.Enabled = !global.Checked; LoadRule(); };
        startup.Checked = app.Settings.StartWithWindows;
        startup.CheckedChanged += (_, _) => { app.Settings.StartWithWindows = startup.Checked; app.Save(); };
        FormClosing += (_, e) => { if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); } };
        app.Updated += RefreshState; Disposed += (_, _) => app.Updated -= RefreshState;
        RefreshState(); LoadRule();
    }
    private static Button Button(string text, EventHandler click) { var button = new Button { Text = text, AutoSize = true, Height = 34, Padding = new(10, 3, 10, 3), ForeColor = Color.Black, BackColor = Color.WhiteSmoke, FlatStyle = FlatStyle.Flat }; button.Click += click; return button; }
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
        target.Text = global.Checked ? "Viewing global defaults" : $"Viewing {display?.Device.Replace(@"\\.\", "") ?? "no monitor"}\nApply to {count} marked monitor{(count == 1 ? "" : "s")}";
        applyButton.Enabled = global.Checked || count > 0;
        inheritButton.Enabled = !global.Checked && count > 0;
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
                var view = new RadioButton { Text = label, AccessibleName = "View " + label, Dock = DockStyle.Fill, Appearance = Appearance.Button, AutoCheck = false, Checked = display.Id == viewedMonitor, FlatStyle = FlatStyle.Flat, TextAlign = ContentAlignment.MiddleLeft, Padding = new(5), BackColor = Color.FromArgb(34, 39, 49), ForeColor = ForeColor };
                view.FlatAppearance.CheckedBackColor = Color.FromArgb(35, 88, 100);
                int index = monitors.RowCount++; monitors.RowStyles.Add(new(SizeType.Absolute, 64));
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
        var scope = global.Checked ? GlobalScope : viewedMonitor;
        if (scope is not null && drafts.TryGetValue(scope, out var draft)) return draft;
        var display = app.Monitors.FirstOrDefault(display => display.Id == viewedMonitor);
        return global.Checked || display is null ? app.Settings.Defaults : app.Settings.For(display);
    }
    private void LoadRule()
    {
        UpdateIcons(() =>
        {
            editorScope = global.Checked ? GlobalScope : viewedMonitor;
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
        if (!global.Checked && targets.Length == 0) return;
        SaveDraft(); var rule = ReadRule();
        if (global.Checked)
        {
            app.Settings.Defaults = rule;
            drafts.Remove(GlobalScope);
            foreach (var display in app.Monitors.Where(display => !app.Settings.Overrides.ContainsKey(display.Id))) drafts.Remove(display.Id);
        }
        else
        {
            app.Settings.ApplyTo(targets, rule);
            foreach (var display in targets) drafts.Remove(display.Id);
        }
        app.Save(); LoadRule();
    }
    private void UseDefaults()
    {
        SaveDraft();
        foreach (var row in monitorRows.Where(row => row.Mark.Checked)) { app.Settings.Overrides.Remove(row.Display.Id); drafts.Remove(row.Display.Id); }
        app.Save(); LoadRule();
    }
    private sealed record MonitorRow(DisplayInfo Display, CheckBox Mark, RadioButton View);
    private sealed record IconChoice(string Id, string Label) { public override string ToString() => Label; }
}
