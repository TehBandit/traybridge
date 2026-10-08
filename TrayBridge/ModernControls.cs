using System.Drawing.Drawing2D;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace TrayBridge;

internal static class UiTheme
{
    internal static bool Dark => Application.IsDarkModeEnabled;
    internal static Color Background => SystemInformation.HighContrast ? SystemColors.Control : Dark ? Color.FromArgb(32, 32, 32) : Color.FromArgb(243, 243, 243);
    internal static Color Surface => SystemInformation.HighContrast ? SystemColors.Window : Dark ? Color.FromArgb(43, 43, 43) : Color.White;
    internal static Color Hover => SystemInformation.HighContrast ? SystemColors.ControlLight : Dark ? Color.FromArgb(56, 56, 56) : Color.FromArgb(246, 246, 246);
    internal static Color Border => SystemInformation.HighContrast ? SystemColors.WindowText : Dark ? Color.FromArgb(65, 65, 65) : Color.FromArgb(224, 224, 224);
    internal static Color Text => SystemInformation.HighContrast ? SystemColors.WindowText : Dark ? Color.FromArgb(242, 242, 242) : Color.FromArgb(30, 30, 30);
    internal static Color Muted => SystemInformation.HighContrast ? SystemColors.WindowText : Dark ? Color.FromArgb(180, 180, 180) : Color.FromArgb(100, 100, 100);
    internal static Color Accent => SystemColors.Highlight;
    internal static Color Selection => SystemInformation.HighContrast ? SystemColors.Highlight : Dark ? Color.FromArgb(34, 56, 72) : Color.FromArgb(232, 242, 252);
    internal static GraphicsPath Rounded(RectangleF rect, float radius)
    {
        var path = new GraphicsPath(); float d = radius * 2;
        path.AddArc(rect.X, rect.Y, d, d, 180, 90); path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90); path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure(); return path;
    }
    internal static void WindowStyle(IntPtr window)
    {
        int dark = Dark ? 1 : 0, rounded = 2, backdrop = 2;
        _ = DwmSetWindowAttribute(window, 20, ref dark, sizeof(int));
        _ = DwmSetWindowAttribute(window, 33, ref rounded, sizeof(int));
        _ = DwmSetWindowAttribute(window, 38, ref backdrop, sizeof(int));
    }
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
}

internal sealed class SettingsCard : Panel
{
    internal SettingsCard()
    {
        DoubleBuffered = true; BackColor = UiTheme.Surface; Padding = new(20);
        SetStyle(ControlStyles.ResizeRedraw, true);
    }
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent?.BackColor ?? UiTheme.Background);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var outline = UiTheme.Rounded(new(0.5f, 0.5f, Width - 1, Height - 1), 8);
        using var fill = new SolidBrush(UiTheme.Surface); using var border = new Pen(UiTheme.Border);
        e.Graphics.FillPath(fill, outline); e.Graphics.DrawPath(border, outline);
    }
}

internal sealed class ModernButton : Button
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal bool Primary { get; init; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal bool Selected { get; set; }
    private bool hover, pressed;
    internal ModernButton()
    {
        FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0;
        Size = new(150, 38); Margin = new(0, 0, 8, 0); Cursor = Cursors.Hand;
        SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }
    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { pressed = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent?.BackColor ?? UiTheme.Surface); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var fillColor = !Enabled ? UiTheme.Background : Primary ? UiTheme.Accent : Selected ? UiTheme.Selection : hover || pressed ? UiTheme.Hover : UiTheme.Surface;
        using var path = UiTheme.Rounded(new(0.5f, 0.5f, Width - 1, Height - 1), 5);
        using var fill = new SolidBrush(fillColor); using var border = new Pen((Primary || Selected) && Enabled ? UiTheme.Accent : UiTheme.Border);
        e.Graphics.FillPath(fill, path); e.Graphics.DrawPath(border, path);
        var textColor = !Enabled ? UiTheme.Muted : Primary ? SystemColors.HighlightText : UiTheme.Text;
        TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, textColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -4, -4), textColor, fillColor);
    }
}

internal sealed class MonitorSelector : RadioButton
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal string DisplayName { get; init; } = "";
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal string Detail { get; init; } = "";
    private bool hover;
    internal MonitorSelector()
    {
        Appearance = Appearance.Button; AutoCheck = false; FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0;
        Cursor = Cursors.Hand; Dock = DockStyle.Fill; Margin = new(0, 3, 0, 3);
        SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }
    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnCheckedChanged(EventArgs e) { Invalidate(); base.OnCheckedChanged(e); }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent?.BackColor ?? UiTheme.Surface); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = UiTheme.Rounded(new(0.5f, 0.5f, Width - 1, Height - 1), 5);
        using var fill = new SolidBrush(Checked ? UiTheme.Selection : hover ? UiTheme.Hover : UiTheme.Surface);
        using var border = new Pen(Checked ? UiTheme.Accent : UiTheme.Border);
        e.Graphics.FillPath(fill, path); e.Graphics.DrawPath(border, path);
        var textColor = Checked && SystemInformation.HighContrast ? SystemColors.HighlightText : UiTheme.Text;
        TextRenderer.DrawText(e.Graphics, DisplayName, Font, new Rectangle(12, 9, Width - 24, 24), textColor, TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter);
        using var detailFont = new Font(Font.FontFamily, 9);
        TextRenderer.DrawText(e.Graphics, Detail, detailFont, new Rectangle(12, 34, Width - 24, 20), Checked && SystemInformation.HighContrast ? textColor : UiTheme.Muted, TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter);
        if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -4, -4));
    }
}
