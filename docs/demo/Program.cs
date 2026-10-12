using System.Drawing.Imaging;
using System.Reflection;
using System.Text.Json;
using TrayBridge;

internal static class Demo
{
    static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;
    static void Call(object owner, string name, params object[] args) => owner.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(owner, args);
    static readonly List<Bitmap> Frames = [];
    [STAThread]
    static void Main(string[] args)
    {
        if (args.Length != 2) throw new ArgumentException("Pass output GIF and isolated data directory.");
        NativeHost.DataDirectory = Path.GetFullPath(args[1]);
        Directory.CreateDirectory(NativeHost.DataDirectory);
        File.WriteAllText(Path.Combine(NativeHost.DataDirectory, "inventory.json"), JsonSerializer.Serialize(new { taskbars = new[] { new { elements = new[] {
            new { @class = "SystemTray.NotifyIconView", identity = "app:chat", text = "Chat" },
            new { @class = "SystemTray.NotifyIconView", identity = "app:music", text = "Music" },
            new { @class = "SystemTray.NotifyIconView", identity = "app:sync", text = "Cloud sync" },
            new { @class = "SystemTray.IconView", identity = "system:volume", text = "Volume" },
            new { @class = "SystemTray.IconView", identity = "system:network", text = "Network" }
        } } } }));
        ApplicationConfiguration.Initialize(); UiTheme.SetAppearance("Light");
        var app = new TrayApplication();
        using var form = new ManagerForm(app) { Size = new(780, 540), ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = new(-2000, -2000) };
        form.Show(); Application.DoEvents();
        Capture(form, "Your tray, on every monitor", "Before enabling: application icons live on the main taskbar.", false, false);
        app.Enable().GetAwaiter().GetResult();
        Capture(form, "1. Enable TrayBridge", "Notification areas appear on both taskbars.", true, false);
        Call(form, "MarkAll", false); Call(form, "ViewMonitor", "monitor-2");
        var rows = Field<FlowLayoutPanel>(form, "monitors");
        rows.Controls[1].Controls.OfType<CheckBox>().Single().Checked = true;
        Capture(form, "2. Choose a monitor", "View Display 2 and mark it as the target for Apply.", true, false);
        var apps = Field<CheckedListBox>(form, "appResults");
        for (int i = 0; i < apps.Items.Count; i++) if (apps.Items[i]!.ToString() == "Cloud sync") apps.SetItemChecked(i, false);
        Capture(form, "3. Pick the icons you want", "Keep Chat and Music on Display 2; leave Cloud sync on main.", true, false);
        Field<Button>(form, "applyButton").PerformClick();
        Capture(form, "4. Apply to the marked monitor", "Each monitor keeps its own icon selection.", true, true);
        Field<Button>(form, "setGlobalButton").PerformClick();
        Capture(form, "Save your setup", "Set global config saves the applied rules across all monitors.", true, true);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[0]))!);
        AnimatedGif.Write(args[0], Frames, 240);
        Frames[4].Save(Path.ChangeExtension(args[0], ".png"), ImageFormat.Png);
        foreach (var frame in Frames) frame.Dispose();
        Console.WriteLine("Created six-frame UI walkthrough. Taskbar illustrations and sample data are simulated.");
    }
    static void Capture(Form form, string title, string detail, bool enabled, bool routed)
    {
        Application.DoEvents(); form.Refresh();
        var frame = new Bitmap(980, 880);
        using var graphics = Graphics.FromImage(frame);
        graphics.Clear(Color.FromArgb(239, 244, 250));
        using var titleFont = new Font("Segoe UI", 26, FontStyle.Bold, GraphicsUnit.Pixel);
        using var bodyFont = new Font("Segoe UI", 17, FontStyle.Regular, GraphicsUnit.Pixel);
        using var smallFont = new Font("Segoe UI", 13, FontStyle.Regular, GraphicsUnit.Pixel);
        using var ink = new SolidBrush(Color.FromArgb(20, 38, 62));
        using var muted = new SolidBrush(Color.FromArgb(75, 91, 110));
        graphics.DrawString("TrayBridge", smallFont, muted, 100, 24);
        graphics.DrawString(title, titleFont, ink, 100, 49);
        graphics.DrawString(detail, bodyFont, muted, 100, 94);
        using var ui = new Bitmap(form.Width, form.Height); form.DrawToBitmap(ui, new Rectangle(Point.Empty, ui.Size));
        graphics.DrawImageUnscaled(ui, 100, 136);
        Taskbar(graphics, 100, "Main display", new[] { "Chat", "Music", "Sync" }, smallFont);
        Taskbar(graphics, 500, "Display 2", enabled ? routed ? new[] { "Chat", "Music" } : new[] { "Chat", "Music", "Sync" } : [], smallFont);
        graphics.DrawString("Actual settings UI with sample data. Taskbars below are illustrations.", smallFont, muted, 100, 832);
        Frames.Add(frame);
    }
    static void Taskbar(Graphics graphics, int x, string label, string[] icons, Font font)
    {
        using var ink = new SolidBrush(Color.FromArgb(35, 48, 64));
        using var surface = new SolidBrush(Color.White);
        using var accent = new SolidBrush(Color.FromArgb(0, 110, 210));
        graphics.DrawString(label, font, ink, x, 701);
        graphics.FillRectangle(surface, x, 729, 380, 72);
        graphics.DrawString("Windows taskbar", font, ink, x + 12, 745);
        for (int i = 0; i < icons.Length; i++) {
            int position = x + 182 + i * 61;
            graphics.FillEllipse(accent, position + 9, 740, 18, 18);
            graphics.DrawString(icons[i], font, ink, position, 766);
        }
    }
}

// GDI+ supplies each frame's GIF palette and compressed pixels. This joins those
// static images with local palettes and frame delays, avoiding an extra tool dependency.
internal static class AnimatedGif
{
    static Bitmap Quantize(Bitmap frame)
    {
        // Preserve the UI's dominant colors without the default GIF encoder's
        // web-palette dithering, which makes small text and pale surfaces noisy.
        var rectangle = new Rectangle(Point.Empty, frame.Size);
        var data = frame.LockBits(rectangle, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var pixels = new byte[data.Stride * frame.Height];
        System.Runtime.InteropServices.Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
        int sourceStride = data.Stride; frame.UnlockBits(data);
        var counts = new Dictionary<int, int>();
        for (int y = 0; y < frame.Height; y++) for (int x = 0; x < frame.Width; x++) {
            int p = y * sourceStride + x * 4, color = pixels[p] | pixels[p + 1] << 8 | pixels[p + 2] << 16;
            counts[color] = counts.GetValueOrDefault(color) + 1;
        }
        var colors = counts.OrderByDescending(pair => pair.Value).Take(256).Select(pair => pair.Key).ToArray();
        var indices = new Dictionary<int, byte>();
        foreach (int color in counts.Keys) {
            int best = 0, distance = int.MaxValue;
            for (int i = 0; i < colors.Length; i++) {
                int red = (color >> 16 & 255) - (colors[i] >> 16 & 255);
                int green = (color >> 8 & 255) - (colors[i] >> 8 & 255);
                int blue = (color & 255) - (colors[i] & 255);
                int candidate = red * red + green * green + blue * blue;
                if (candidate < distance) { distance = candidate; best = i; }
            }
            indices[color] = (byte)best;
        }
        var result = new Bitmap(frame.Width, frame.Height, PixelFormat.Format8bppIndexed);
        var palette = result.Palette;
        for (int i = 0; i < colors.Length; i++) palette.Entries[i] = Color.FromArgb(255, colors[i] >> 16 & 255, colors[i] >> 8 & 255, colors[i] & 255);
        result.Palette = palette;
        var indexedData = result.LockBits(rectangle, ImageLockMode.WriteOnly, PixelFormat.Format8bppIndexed);
        var indexed = new byte[indexedData.Stride * frame.Height];
        for (int y = 0; y < frame.Height; y++) for (int x = 0; x < frame.Width; x++) {
            int p = y * sourceStride + x * 4;
            indexed[y * indexedData.Stride + x] = indices[pixels[p] | pixels[p + 1] << 8 | pixels[p + 2] << 16];
        }
        System.Runtime.InteropServices.Marshal.Copy(indexed, 0, indexedData.Scan0, indexed.Length);
        result.UnlockBits(indexedData); return result;
    }
    internal static void Write(string path, IReadOnlyList<Bitmap> frames, ushort delay)
    {
        using var output = new BinaryWriter(File.Create(path));
        output.Write(System.Text.Encoding.ASCII.GetBytes("GIF89a"));
        output.Write((ushort)frames[0].Width); output.Write((ushort)frames[0].Height);
        output.Write(new byte[] { 0x70, 0, 0 });
        output.Write(new byte[] { 0x21, 0xff, 11 }); output.Write(System.Text.Encoding.ASCII.GetBytes("NETSCAPE2.0"));
        output.Write(new byte[] { 3, 1, 0, 0, 0 });
        foreach (var frame in frames) {
            using var encoded = new MemoryStream(); using var indexedFrame = Quantize(frame); indexedFrame.Save(encoded, ImageFormat.Gif);
            var bytes = encoded.ToArray(); int offset = 13;
            byte packed = bytes[10]; int paletteSize = (packed & 0x80) != 0 ? 3 * (1 << ((packed & 7) + 1)) : 0;
            var palette = bytes.AsSpan(offset, paletteSize).ToArray(); offset += paletteSize;
            while (bytes[offset] == 0x21) { offset += 2; while (bytes[offset] != 0) offset += bytes[offset] + 1; offset++; }
            if (bytes[offset] != 0x2c) throw new InvalidDataException("Missing GIF image descriptor.");
            output.Write(new byte[] { 0x21, 0xf9, 4, 4 }); output.Write(delay); output.Write(new byte[] { 0, 0 });
            output.Write(bytes, offset, 9); byte descriptor = bytes[offset + 9]; offset += 10;
            if ((descriptor & 0x80) != 0) {
                output.Write(descriptor); int localSize = 3 * (1 << ((descriptor & 7) + 1));
                output.Write(bytes, offset, localSize); offset += localSize;
            } else {
                if (palette.Length == 0) throw new InvalidDataException("Missing GIF palette.");
                output.Write((byte)((descriptor & 0x78) | 0x80 | (packed & 7))); output.Write(palette);
            }
            output.Write(bytes[offset++]);
            while (bytes[offset] != 0) { int count = bytes[offset] + 1; output.Write(bytes, offset, count); offset += count; }
            output.Write(bytes[offset++]);
        }
        output.Write((byte)0x3b);
    }
}

namespace TrayBridge
{
    internal static class NativeHost { internal static string DataDirectory = ""; }
    internal static class FileOwnership { internal static void Record(string path) { } }
    internal sealed record DisplayInfo(string Id, string Device, string Name, bool Primary, Rectangle Bounds);
    internal sealed class UninstallForm : Form { internal UninstallForm(TrayApplication app) { } }
    internal sealed class TrayApplication
    {
        internal AppSettings Settings { get; } = new() { Appearance = "Light" };
        internal IReadOnlyList<DisplayInfo> Monitors { get; } = new[] {
            new DisplayInfo("monitor-1", @"\\.\DISPLAY1", "Main display", true, new(0, 0, 1920, 1080)),
            new DisplayInfo("monitor-2", @"\\.\DISPLAY2", "Display 2", false, new(1920, 0, 1920, 1080))
        };
        internal bool Busy => false;
        internal string Status { get; private set; } = "Disabled — Windows' original taskbars are active.";
        internal event Action? Updated;
        internal void Save() => Updated?.Invoke();
        internal void SavePreferences() => Updated?.Invoke();
        internal Task Enable() { Settings.Active = true; Status = "Active · 2 connected monitors"; Save(); return Task.CompletedTask; }
        internal Task Disable() { Settings.Active = false; Save(); return Task.CompletedTask; }
        internal Task<bool> RevertGlobalConfiguration() => Task.FromResult(false);
        internal Task<bool> ResetWindows() => Task.FromResult(false);
    }
}
