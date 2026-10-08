using System.Text.Json;

namespace TrayBridge;
internal sealed class ManagerForm : Form
{
    private readonly TrayApplication app;
    private readonly FlowLayoutPanel monitors = new() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, AutoScroll = true, AccessibleName = "Connected monitors", Margin = new(0) };
    private readonly CheckBox selectAll = new() { Text = "Select/deselect all", AutoSize = true, AutoCheck = false };
    private readonly List<MonitorRow> monitorRows = [];
    private readonly Dictionary<string, TrayRule> drafts = [];
    private string? viewedMonitor;
    private string? editorScope;
    private readonly Button applyButton;
    private readonly Button setGlobalButton, revertGlobalButton, resetWindowsButton, enableButton, disableButton;
    // These complete lists hold selections independently of the filtered views.
    private readonly CheckedListBox icons = new();
    private readonly CheckedListBox systemIcons = new();
    private readonly CheckedListBox appResults = new IconSelectionList() { Dock = DockStyle.Fill, CheckOnClick = true, IntegralHeight = false, HorizontalScrollbar = true, AccessibleName = "Application icons", Margin = new(0) };
    private readonly CheckedListBox systemResults = new IconSelectionList() { Dock = DockStyle.Fill, CheckOnClick = true, IntegralHeight = false, HorizontalScrollbar = true, AccessibleName = "Windows system icons", Margin = new(0) };
    private readonly TextBox appSearch = new() { Dock = DockStyle.Fill, PlaceholderText = "Search applications", AccessibleName = "Search applications", Margin = new(0, 2, 0, 2) };
    private readonly TextBox systemSearch = new() { Dock = DockStyle.Fill, PlaceholderText = "Search Windows controls", AccessibleName = "Search Windows controls", Margin = new(0, 2, 0, 2) };
    private readonly Label appCount = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Margin = new(0) };
    private readonly Label systemCount = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Margin = new(0) };
    private readonly CheckBox allSystem = new() { Text = "All system icons (including new icons)", AutoSize = true, Checked = true };
    private readonly CheckBox enabled = new() { Text = "Show tray on marked monitors", AutoSize = true, Checked = true };
    private readonly CheckBox all = new() { Text = "All applications (including new icons)", AutoSize = true, Checked = true };
    private readonly CheckBox quick = new() { Text = "Quick Settings", AutoSize = true, Checked = true };
    private readonly CheckBox clock = new() { Text = "Clock", AutoSize = true, Checked = true };
    private readonly CheckBox indicators = new() { Text = "Input && privacy", AutoSize = true, Checked = true };
    private readonly Label status = new() { Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Label target = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Label editorTitle = new() { Dock = DockStyle.Fill };
    private readonly Label notice = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
    private readonly ToolTip tips = new();
    private readonly ContextMenuStrip settingsMenu = new();
    private Color appliedBackground = UiTheme.Background, appliedSurface = UiTheme.Surface, appliedText = UiTheme.Text, appliedMuted = UiTheme.Muted;
    private bool loading;
    private bool updatingIcons;
    private bool filtering;
    private readonly Dictionary<string, IconChoice> catalog = new();
    private readonly Dictionary<string, IconChoice> systemCatalog = new();
    private string topology = "";
    internal ManagerForm(TrayApplication application)
    {
        app = application; Text = "TrayBridge"; MinimumSize = new(860, 570); Size = new(980, 650);
        StartPosition = FormStartPosition.CenterScreen; Font = new("Segoe UI Variable Text", 10);
        BackColor = UiTheme.Background; ForeColor = UiTheme.Text;
        var layout = Rows(BackColor, 42, 114, 48, -1, 44, 68); layout.Padding = new(16, 10, 16, 12);
        var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new(0), BackColor = BackColor };
        header.RowStyles.Add(new(SizeType.Percent, 100));
        header.ColumnStyles.Add(new(SizeType.Percent, 100)); header.ColumnStyles.Add(new(SizeType.Absolute, 88));
        header.Controls.Add(Label("TrayBridge", 18, true), 0, 0);
        var settingsButton = Button("Settings", (_, _) => { settingsMenu.Show(header, new Point(header.Width - settingsMenu.Width, header.Height)); }, width: 88);
        header.Controls.Add(settingsButton, 1, 0); layout.Controls.Add(header, 0, 0);

        var displayCard = new SettingsCard { Dock = DockStyle.Fill, Margin = new(0, 0, 0, 6), Padding = new(12, 6, 12, 6) };
        var displayLayout = Rows(UiTheme.Surface, 28, -1);
        var displayHeader = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = new(0), BackColor = UiTheme.Surface };
        var displayTitle = Label("Displays", 11, true); displayTitle.Dock = DockStyle.None; displayTitle.Size = new(86, 26);
        selectAll.Margin = new(0, 3, 0, 0); displayHeader.Controls.Add(displayTitle); displayHeader.Controls.Add(selectAll);
        displayLayout.Controls.Add(displayHeader, 0, 0); displayLayout.Controls.Add(monitors, 0, 1);
        monitors.BackColor = UiTheme.Surface; displayCard.Controls.Add(displayLayout); layout.Controls.Add(displayCard, 0, 1);
        tips.SetToolTip(selectAll, "Check boxes mark Apply targets. Click a monitor name to view its settings.");

        var scope = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new(0), BackColor = BackColor };
        scope.RowStyles.Add(new(SizeType.Percent, 100));
        scope.ColumnStyles.Add(new(SizeType.Percent, 100)); scope.ColumnStyles.Add(new(SizeType.Absolute, 248));
        var scopeText = Rows(BackColor, 24, 22);
        editorTitle.Font = new(Font.FontFamily, 11, FontStyle.Bold); target.ForeColor = UiTheme.Muted; target.Font = new(Font.FontFamily, 9);
        scopeText.Controls.Add(editorTitle, 0, 0); scopeText.Controls.Add(target, 0, 1);
        enabled.Anchor = AnchorStyles.Right; scope.Controls.Add(scopeText, 0, 0); scope.Controls.Add(enabled, 1, 0); layout.Controls.Add(scope, 0, 2);

        var sections = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new(0), BackColor = BackColor };
        sections.ColumnStyles.Add(new(SizeType.Percent, 50)); sections.ColumnStyles.Add(new(SizeType.Percent, 50)); sections.RowStyles.Add(new(SizeType.Percent, 100));
        var appCard = new SettingsCard { Dock = DockStyle.Fill, Margin = new(0, 0, 5, 0) };
        var apps = Rows(UiTheme.Surface, 28, 28, 32, 22, -1);
        apps.Controls.Add(Label("Applications", 12, true), 0, 0); apps.Controls.Add(all, 0, 1);
        apps.Controls.Add(appSearch, 0, 2); apps.Controls.Add(appCount, 0, 3); apps.Controls.Add(appResults, 0, 4);
        appCard.Controls.Add(apps); sections.Controls.Add(appCard, 0, 0);
        var systemCard = new SettingsCard { Dock = DockStyle.Fill, Margin = new(5, 0, 0, 0) };
        var systems = Rows(UiTheme.Surface, 28, 28, 32, 32, 22, -1);
        systems.Controls.Add(Label("Windows controls", 12, true), 0, 0); systems.Controls.Add(allSystem, 0, 1);
        var groups = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = new(0), BackColor = UiTheme.Surface };
        foreach (var check in new[] { quick, clock, indicators }) { check.Margin = new(0, 4, 10, 0); groups.Controls.Add(check); }
        systems.Controls.Add(groups, 0, 2); systems.Controls.Add(systemSearch, 0, 3); systems.Controls.Add(systemCount, 0, 4); systems.Controls.Add(systemResults, 0, 5);
        tips.SetToolTip(clock, "Clock and notification controls"); tips.SetToolTip(indicators, "Input, language, camera and microphone indicators");
        systemCard.Controls.Add(systems); sections.Controls.Add(systemCard, 1, 0); layout.Controls.Add(sections, 0, 3);

        var actions = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new(0), Padding = new(0, 6, 0, 6), BackColor = BackColor };
        actions.ColumnStyles.Add(new(SizeType.Percent, 100)); actions.ColumnStyles.Add(new(SizeType.Absolute, 100)); actions.RowStyles.Add(new(SizeType.Percent, 100));
        var configButtons = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = new(0), BackColor = BackColor };
        setGlobalButton = Button("Set global config", (_, _) => { app.Settings.CaptureGlobalConfiguration(); app.Save(); notice.Text = "Global config saved. Pending edits are kept."; });
        revertGlobalButton = Button("Revert to global config", async (_, _) => { if (await app.RevertGlobalConfiguration()) { drafts.Clear(); LoadRule(); notice.Text = "Global config restored across all displays."; } else notice.Text = app.Status; }, width: 180);
        resetWindowsButton = Button("Reset to Windows", async (_, _) => { if (await app.ResetWindows()) { drafts.Clear(); LoadRule(); notice.Text = "Windows restored. Saved global config is kept."; } else notice.Text = app.Status; }, width: 160);
        configButtons.Controls.Add(setGlobalButton); configButtons.Controls.Add(revertGlobalButton); configButtons.Controls.Add(resetWindowsButton);
        applyButton = Button("Apply", (_, _) => Apply(), primary: true, width: 100); applyButton.Dock = DockStyle.Fill; applyButton.Margin = new(0);
        actions.Controls.Add(configButtons, 0, 0); actions.Controls.Add(applyButton, 1, 0); layout.Controls.Add(actions, 0, 4);
        tips.SetToolTip(setGlobalButton, "Save the applied setup across all displays. Apply pending edits first to include them.");
        tips.SetToolTip(revertGlobalButton, "Restore the saved setup across all displays, including its enabled or disabled state.");
        tips.SetToolTip(resetWindowsButton, "Restore native Windows taskbars and clear assignments. Keep the saved global config and preferences.");

        var serviceCard = new SettingsCard { Dock = DockStyle.Fill, Margin = new(0), Padding = new(12, 8, 12, 8) };
        var service = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new(0), BackColor = UiTheme.Surface };
        service.RowStyles.Add(new(SizeType.Percent, 100));
        service.ColumnStyles.Add(new(SizeType.Percent, 100)); service.ColumnStyles.Add(new(SizeType.Absolute, 192));
        var serviceText = Rows(UiTheme.Surface, 23, 23);
        status.Font = notice.Font = new(Font.FontFamily, 9); status.ForeColor = UiTheme.Text; notice.ForeColor = UiTheme.Muted;
        notice.Text = "Apply copies the viewed settings to marked displays.";
        serviceText.Controls.Add(status, 0, 0); serviceText.Controls.Add(notice, 0, 1);
        var serviceButtons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Margin = new(0), Padding = new(0, 7, 0, 0), BackColor = UiTheme.Surface };
        disableButton = Button("Disable", async (_, _) => await app.Disable(), width: 88); enableButton = Button("Enable", async (_, _) => await app.Enable(), width: 88);
        serviceButtons.Controls.Add(disableButton); serviceButtons.Controls.Add(enableButton);
        service.Controls.Add(serviceText, 0, 0); service.Controls.Add(serviceButtons, 1, 0); serviceCard.Controls.Add(service); layout.Controls.Add(serviceCard, 0, 5);
        Controls.Add(layout); AcceptButton = applyButton;
        foreach (var list in new[] { icons, systemIcons, appResults, systemResults }) { list.BackColor = UiTheme.Surface; list.ForeColor = ForeColor; list.BorderStyle = BorderStyle.None; list.Font = Font; }
        foreach (var count in new[] { appCount, systemCount }) { count.ForeColor = UiTheme.Muted; count.Font = new(Font.FontFamily, 9); }
        foreach (var control in new[] { selectAll, all, quick, clock, indicators, allSystem }) { control.BackColor = UiTheme.Surface; control.ForeColor = ForeColor; }
        enabled.BackColor = BackColor; enabled.ForeColor = ForeColor;
        ConnectSelection(icons, all); ConnectSelection(systemIcons, allSystem);
        ConnectFilter(icons, appResults, appSearch); ConnectFilter(systemIcons, systemResults, systemSearch);
        selectAll.Click += (_, _) => MarkAll(selectAll.CheckState != CheckState.Checked);
        selectAll.CheckStateChanged += (_, _) => { if (!loading) MarkAll(selectAll.CheckState == CheckState.Checked); };
        var appearance = new ToolStripMenuItem("Appearance");
        foreach (var mode in new[] { "System", "Light", "Dark" })
        {
            var option = new ToolStripMenuItem(mode == "System" ? "System (default)" : mode) { Tag = mode, CheckOnClick = false };
            option.Click += (_, _) => { app.Settings.Appearance = mode; UiTheme.SetAppearance(mode); RefreshTheme(); app.SavePreferences(); };
            appearance.DropDownItems.Add(option);
        }
        var startOption = new ToolStripMenuItem("Start at sign-in") { CheckOnClick = true, Checked = app.Settings.StartWithWindows };
        startOption.CheckedChanged += (_, _) => { app.Settings.StartWithWindows = startOption.Checked; app.SavePreferences(); };
        settingsMenu.Items.Add(appearance); settingsMenu.Items.Add(new ToolStripSeparator()); settingsMenu.Items.Add(startOption);
        settingsMenu.Opening += (_, _) => { foreach (ToolStripMenuItem item in appearance.DropDownItems) item.Checked = (string)item.Tag! == app.Settings.Appearance || (string)item.Tag! == "System" && app.Settings.Appearance is not ("Light" or "Dark"); };
        FormClosing += (_, e) => { if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); } };
        app.Updated += RefreshState; Disposed += (_, _) => { app.Updated -= RefreshState; tips.Dispose(); settingsMenu.Dispose(); icons.Dispose(); systemIcons.Dispose(); };
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
            monitors.Controls.Clear(); monitorRows.Clear();
            if (!app.Monitors.Any(display => display.Id == viewedMonitor)) viewedMonitor = app.Monitors.FirstOrDefault()?.Id;
            foreach (var display in app.Monitors)
            {
                var label = $"{display.Device.Replace(@"\\.\", "")} · {display.Name}{(display.Primary ? " (primary)" : "")}";
                var mark = new CheckBox { Dock = DockStyle.Left, Width = 26, Margin = new(0), AccessibleName = "Apply to " + label, Checked = initial ? display.Primary : marked.Contains(display.Id) };
                mark.BackColor = UiTheme.Surface;
                var view = new MonitorSelector { Text = label, AccessibleName = "View " + label, DisplayName = display.Name, Detail = display.Device.Replace(@"\\.\", "") + (display.Primary ? " · Primary" : " · Secondary"), Checked = display.Id == viewedMonitor, BackColor = UiTheme.Surface, ForeColor = ForeColor };
                var row = new Panel { Size = new(236, 50), Margin = new(0, 0, 12, 0), BackColor = UiTheme.Surface };
                row.Controls.Add(view); row.Controls.Add(mark); monitors.Controls.Add(row);
                tips.SetToolTip(view, label); tips.SetToolTip(mark, "Mark this display for Apply");
                monitorRows.Add(new(display, mark, view));
                mark.CheckedChanged += (_, _) => { if (!loading) UpdateTargets(); };
                view.Click += (_, _) => ViewMonitor(display.Id);
                view.CheckedChanged += (_, _) => { if (!loading && view.Checked) ViewMonitor(display.Id); };
            }
            topology = signature;
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
            RefreshResults();
        };
        list.ItemCheck += (_, e) =>
        {
            // ItemCheck runs before the clicked item's state changes. Preserve all
            // other selections when the user first excludes an icon from all mode.
            if (!updatingIcons && includeAll.Checked && e.NewValue == CheckState.Unchecked)
                includeAll.Checked = false;
        };
    }
    private void ConnectFilter(CheckedListBox source, CheckedListBox results, TextBox search)
    {
        search.TextChanged += (_, _) => RefreshResults();
        results.ItemCheck += (_, e) =>
        {
            if (filtering) return;
            var choice = (IconChoice)results.Items[e.Index];
            int index = source.Items.IndexOf(choice);
            if (index >= 0) source.SetItemCheckState(index, e.NewValue);
            // ItemCheck fires before the visible row has committed its new state.
            BeginInvoke((Action)(() => { if (!IsDisposed) RefreshResults(); }));
        };
    }
    private void RefreshResults()
    {
        filtering = true;
        try
        {
            Filter(icons, appResults, appSearch.Text, appCount);
            Filter(systemIcons, systemResults, systemSearch.Text, systemCount);
        }
        finally { filtering = false; }
    }
    private static void Filter(CheckedListBox source, CheckedListBox results, string query, Label count)
    {
        query = query.Trim();
        var matches = source.Items.Cast<IconChoice>().Where(choice => choice.Label.Contains(query, StringComparison.OrdinalIgnoreCase) || choice.Id.Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
        var selected = results.SelectedItem as IconChoice;
        int top = results.Items.Count > 0 ? results.TopIndex : 0;
        results.BeginUpdate();
        try
        {
            if (!matches.SequenceEqual(results.Items.Cast<IconChoice>()))
            {
                results.Items.Clear(); results.Items.AddRange(matches);
                if (selected is not null) results.SelectedIndex = Array.IndexOf(matches, selected);
                if (matches.Length > 0) results.TopIndex = Math.Min(top, matches.Length - 1);
            }
            for (int i = 0; i < matches.Length; i++) results.SetItemCheckState(i, source.GetItemCheckState(source.Items.IndexOf(matches[i])));
        }
        finally { results.EndUpdate(); }
        count.Text = matches.Length == 0 ? source.Items.Count == 0 && query.Length == 0 ? "No icons discovered yet" : "No matching icons" : query.Length == 0 ? $"{matches.Length} icons" : $"{matches.Length} of {source.Items.Count} icons";
    }
    private void RefreshTheme()
    {
        if (appliedBackground == UiTheme.Background && appliedSurface == UiTheme.Surface && appliedText == UiTheme.Text && appliedMuted == UiTheme.Muted) return;
        UiTheme.SetAppearance(app.Settings.Appearance);
        void Recolor(Control control)
        {
            if (control.BackColor == appliedBackground) control.BackColor = UiTheme.Background;
            else if (control.BackColor == appliedSurface) control.BackColor = UiTheme.Surface;
            if (control.ForeColor == appliedText) control.ForeColor = UiTheme.Text;
            else if (control.ForeColor == appliedMuted) control.ForeColor = UiTheme.Muted;
            foreach (Control child in control.Controls) Recolor(child);
            control.Invalidate();
        }
        Recolor(this); Recolor(icons); Recolor(systemIcons);
        settingsMenu.BackColor = UiTheme.Surface; settingsMenu.ForeColor = UiTheme.Text;
        appliedBackground = UiTheme.Background; appliedSurface = UiTheme.Surface; appliedText = UiTheme.Text; appliedMuted = UiTheme.Muted;
        if (IsHandleCreated) UiTheme.WindowStyle(Handle);
    }
    private void AddChoice(CheckedListBox list, IconChoice choice, bool selected) => UpdateIcons(() => list.Items.Add(choice, selected));
    private void RefreshState()
    {
        if (IsDisposed) return;
        RefreshTheme();
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
        RefreshResults();
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
        RefreshResults(); UpdateTargets();
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
