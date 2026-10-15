using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;
using Windows.Foundation;

// Observe only. No BMS command characteristic (FFF2) is ever accessed.
// The only BLE writes are standard FFF1 CCCD Notify/None subscription descriptors.
internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new MonitorContext());
    }
}

internal sealed class Battery
{
    public string Id { get; }
    public bool Selected = true;
    public ulong? Address;
    public int? Soc;
    public DateTime LastSeenUtc;
    public string Status = "Searching";
    public bool Connecting;
    public Battery(string id) => Id = id;
}

internal sealed class MonitorContext : ApplicationContext
{
    private const int MaxBatteries = 128;
    private static readonly string SettingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EcoWorthyCommunityEdition", "batteries.json");
    private static readonly string WindowSettingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EcoWorthyCommunityEdition", "windows.json");
    private System.Drawing.Size selectionSize = new(420, 460);
    private System.Drawing.Size hoverSize = new(250, 265);
    private static readonly Guid ServiceId = Guid.Parse("0000fff0-0000-1000-8000-00805f9b34fb");
    private static readonly Guid DataId = Guid.Parse("0000fff1-0000-1000-8000-00805f9b34fb");
    private static readonly TimeSpan Freshness = TimeSpan.FromSeconds(45);
    private readonly object gate = new();
    private readonly Dictionary<string, Battery> batteries = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> selectedIds = new(StringComparer.OrdinalIgnoreCase);
    private bool paused;
    private int sessionVersion;
    private ToolStripMenuItem pauseItem = null!;
    private readonly CancellationTokenSource stopping = new();
    private readonly BluetoothLEAdvertisementWatcher watcher;
    private readonly NotifyIcon tray;
    private readonly HoverPanel panel;
    private readonly System.Windows.Forms.Timer timer;
    private Icon? icon;
    private DateTime lastHoverUtc;
    private bool quitting;
    private bool selectionOpenedAtStartup;

    public MonitorContext()
    {
        LoadSettings();
        LoadWindowSizes();
        panel = new HoverPanel();
        panel.Size = ClampSize(hoverSize, panel.MinimumSize, Screen.PrimaryScreen?.WorkingArea.Size ?? new System.Drawing.Size(1280, 720));
        panel.ResizeEnd += (_, _) => { hoverSize = panel.Size; SaveWindowSizes(); };
        tray = new NotifyIcon { Visible = false, Text = "ECO-WORTHY: discovering batteries" };
        var menu = new ContextMenuStrip();
        menu.Items.Add("Show battery levels", null, (_, _) => ShowPanel());
        menu.Items.Add("Choose batteries...", null, (_, _) => ShowSelection());
        pauseItem = new ToolStripMenuItem("Pause Bluetooth", null, (_, _) => TogglePause());
        menu.Items.Add(pauseItem);
        menu.Items.Add("Exit", null, (_, _) => ExitThread());
        tray.ContextMenuStrip = menu;
        tray.MouseMove += (_, _) => { lastHoverUtc = DateTime.UtcNow; ShowPanel(); };
        tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) ShowPanel(); };
        watcher = new BluetoothLEAdvertisementWatcher { ScanningMode = BluetoothLEScanningMode.Active };
        watcher.Received += AdvertisementReceived;
        watcher.Stopped += (_, _) => { if (!quitting) BeginScan(); };
        timer = new System.Windows.Forms.Timer { Interval = 100 };
        timer.Tick += (_, _) => RefreshDisplay();
        timer.Start();
        RefreshDisplay();
        tray.Visible = true;
        BeginScan();
        // Give discovery a few seconds, then bring the first-run setup forward.
        if (!File.Exists(SettingsPath))
        {
            var startupTimer = new System.Windows.Forms.Timer { Interval = 6000 };
            startupTimer.Tick += (_, _) =>
            {
                startupTimer.Stop();
                startupTimer.Dispose();
                if (!quitting && !selectionOpenedAtStartup)
                {
                    selectionOpenedAtStartup = true;
                    ShowSelection();
                }
            };
            startupTimer.Start();
        }
    }

    private void BeginScan()
    {
        if (quitting || paused) return;
        try { if (watcher.Status != BluetoothLEAdvertisementWatcherStatus.Started) watcher.Start(); }
        catch { /* Keep tray visible; Bluetooth may be temporarily unavailable. */ }
    }

    private void AdvertisementReceived(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementReceivedEventArgs e)
    {
        var name = e.Advertisement.LocalName ?? "";
        // Name is generally "ECO-WORTHY 0B_2161" (zero-B) or "OB_2161" (letter-O-B).
        if (!name.Contains("ECO-WORTHY", StringComparison.OrdinalIgnoreCase) &&
            !name.Contains("OB_", StringComparison.OrdinalIgnoreCase) &&
            !name.Contains("0B_", StringComparison.OrdinalIgnoreCase)) return;
        var marker = name.LastIndexOf('_');
        if (marker < 0 || marker + 1 >= name.Length) return;
        var id = name[(marker + 1)..].ToUpperInvariant();
        if (id.Length != 4 || !id.All(Uri.IsHexDigit)) return;
        bool connect = false;
        int version;
        lock (gate)
        {
            if (paused) return;
            if (!batteries.TryGetValue(id, out var b))
            {
                if (batteries.Count >= MaxBatteries) return;
                b = new Battery(id) { Selected = selectedIds.Contains(id) };
                batteries[id] = b;
            }
            if (!b.Selected) return;
            version = sessionVersion;
            if (b.Address.HasValue && b.Address.Value != e.BluetoothAddress) return;
            b.Address = e.BluetoothAddress;
            if (!b.Connecting) { b.Connecting = true; connect = true; }
        }
        if (connect) _ = ConnectLoop(id, e.BluetoothAddress, version, stopping.Token);
    }

    private async Task ConnectLoop(string id, ulong address, int version, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && !paused && version == sessionVersion)
        {
            BluetoothLEDevice? device = null;
            GattDeviceService? service = null;
            GattCharacteristic? characteristic = null;
            TypedEventHandler<GattCharacteristic, GattValueChangedEventArgs>? handler = null;
            bool subscribed = false;
            try
            {
                SetStatus(id, "Connecting");
                device = await BluetoothLEDevice.FromBluetoothAddressAsync(address);
                if (device == null) throw new Exception("Device unavailable");
                var services = await device.GetGattServicesForUuidAsync(ServiceId, BluetoothCacheMode.Uncached);
                if (services.Status != GattCommunicationStatus.Success || services.Services.Count == 0)
                    throw new Exception("FFF0 unavailable");
                service = services.Services[0];
                var chars = await service.GetCharacteristicsForUuidAsync(DataId, BluetoothCacheMode.Uncached);
                if (chars.Status != GattCommunicationStatus.Success || chars.Characteristics.Count == 0)
                    throw new Exception("FFF1 unavailable");
                characteristic = chars.Characteristics[0];
                if (!characteristic.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Notify))
                    throw new Exception("Notify unavailable");
                handler = (s, args) =>
                {
                    try
                    {
                        var buffer = args.CharacteristicValue;
                        var bytes = new byte[checked((int)buffer.Length)];
                        using var reader = DataReader.FromBuffer(buffer);
                        reader.ReadBytes(bytes);
                        // The packet begins with its own six-byte BLE address, then A1.
                        if (bytes.Length < 13 || bytes[6] != 0xA1) return;
                        for (int j = 0; j < 6; j++)
                            if (bytes[j] != (byte)(address >> (8 * (5 - j)))) return;
                        int matches = 0, candidate = -1;
                        for (int i = 7; i + 5 < bytes.Length; i++)
                            if (bytes[i] == 0 && bytes[i + 1] == 8 && bytes[i + 2] == 0 &&
                                bytes[i + 4] == 0 && bytes[i + 5] == 100 && bytes[i + 3] <= 100)
                            { matches++; candidate = bytes[i + 3]; }
                        if (matches != 1) return;
                        lock (gate)
                        {
                            if (paused || version != sessionVersion) return;
                            batteries[id].Soc = candidate;
                            batteries[id].LastSeenUtc = DateTime.UtcNow;
                            batteries[id].Status = "Connected";
                        }
                    }
                    catch { /* Malformed notification: ignore, never send a command. */ }
                };
                characteristic.ValueChanged += handler;
                var status = await characteristic.WriteClientCharacteristicConfigurationDescriptorAsync(
                    GattClientCharacteristicConfigurationDescriptorValue.Notify);
                if (status != GattCommunicationStatus.Success) throw new Exception("Notification subscription failed");
                subscribed = true;
                SetStatus(id, "Waiting for data");
                var connectedAtUtc = DateTime.UtcNow;
                // If notifications stop, close and reconnect; no cached SOC is treated as current.
                while (!ct.IsCancellationRequested && !paused && version == sessionVersion)
                {
                    await Task.Delay(5000, ct);
                    DateTime seen;
                    lock (gate) seen = batteries[id].LastSeenUtc;
                    if (seen != default && DateTime.UtcNow - seen > Freshness) break;
                    if (seen == default && (device.ConnectionStatus != BluetoothConnectionStatus.Connected || DateTime.UtcNow - connectedAtUtc > TimeSpan.FromSeconds(40))) break;
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { SetStatus(id, ex.Message); }
            finally
            {
                if (characteristic != null)
                {
                    if (subscribed)
                    {
                        try { await characteristic.WriteClientCharacteristicConfigurationDescriptorAsync(
                            GattClientCharacteristicConfigurationDescriptorValue.None); } catch { }
                    }
                    if (handler != null) characteristic.ValueChanged -= handler;
                }
                service?.Dispose();
                device?.Dispose();
            }
            if (ct.IsCancellationRequested || paused || version != sessionVersion) break;
            try { await Task.Delay(10000, ct); } catch (OperationCanceledException) { break; }
        }
        lock (gate) { if (batteries.TryGetValue(id, out var b)) b.Connecting = false; }
    }

    private void SetStatus(string id, string status)
    {
        lock (gate) batteries[id].Status = status;
    }

    private void RefreshDisplay()
    {
        var lines = new List<string>();
        int sum = 0, current = 0, total = 0;
        lock (gate)
        {
            foreach (var b in batteries.Values.Where(x => x.Selected).OrderBy(x => x.Id))
            {
                total++;
                bool fresh = !paused && b.Soc.HasValue && DateTime.UtcNow - b.LastSeenUtc <= Freshness;
                if (fresh) { sum += b.Soc!.Value; current++; }
                lines.Add($"OB_{b.Id}    {(paused ? "Paused" : fresh ? b.Soc + "%" : b.Status == "Connected" ? "Stale" : b.Status)}");
            }
        }
        if (total == 0) lines.Add("No batteries selected. Right-click tray icon.");
        string overall = total > 0 && current == total ? $"{(int)Math.Round(sum / (double)total, MidpointRounding.AwayFromZero)}%" : "--";
        string heading = paused ? "Battery monitor: paused" : total == 0 ? "Select batteries from tray menu" : current == total ? $"Battery bank: {overall}" : $"Battery bank: {current}/{total} reporting";
        tray.Text = heading.Length <= 63 ? heading : heading[..63];
        using var bitmap = new Bitmap(64, 64);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            int average = total > 0 && current == total
    ? (int)Math.Round(sum / (double)total, MidpointRounding.AwayFromZero)
    : -1;

Color background = average < 0
    ? Color.FromArgb(105, 105, 105)
    : average < 20
        ? Color.FromArgb(180, 45, 45)
        : average < 50
            ? Color.FromArgb(180, 125, 15)
            : Color.FromArgb(30, 115, 66);

using var bg = new SolidBrush(background);
            g.FillRoundedRectangle(bg, new Rectangle(1, 5, 62, 54), 10);
            using var font = new Font("Arial", overall.Length > 3 ? 35 : 39, FontStyle.Bold, GraphicsUnit.Pixel);
            using var fg = new SolidBrush(Color.White);
            using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString(overall.TrimEnd('%'), font, fg, new RectangleF(0, 4, 64, 54), sf);
        }
        IntPtr handle = bitmap.GetHicon();
        try
        {
            var newIcon = (Icon)Icon.FromHandle(handle).Clone();
            var old = icon;
            icon = newIcon;
            tray.Icon = newIcon;
            old?.Dispose();
        }
        finally { Native.DestroyIcon(handle); }
        panel.UpdateContent(heading, lines);
       
        if (panel.Visible &&
            DateTime.UtcNow - lastHoverUtc > TimeSpan.FromMilliseconds(650) &&
            !panel.Bounds.Contains(Cursor.Position))
        {
            panel.Hide();
        }
    }

    private void TogglePause()
    {
        paused = !paused;
        lock (gate)
        {
            sessionVersion++;
            foreach (var b in batteries.Values)
            {
                b.Soc = null;
                b.LastSeenUtc = default;
                b.Status = paused ? "Paused" : "Searching";
                // Existing loops exit and release BLE connections asynchronously.
            }
        }
        if (paused) { try { watcher.Stop(); } catch { } }
        else BeginScan();
        pauseItem.Text = paused ? "Resume Bluetooth" : "Pause Bluetooth";
        RefreshDisplay();
    }

    private void ShowSelection()
    {
        // Pause first so selection changes do not race with BLE connection attempts.
        if (!paused) TogglePause();
        List<string> ids;
        lock (gate) ids = batteries.Keys.OrderBy(x => x).ToList();
        using var dialog = new Form { Text = "Choose ECO-WORTHY batteries",
            StartPosition = FormStartPosition.CenterScreen, FormBorderStyle = FormBorderStyle.Sizable,
            MaximizeBox = true, MinimizeBox = false, TopMost = true, AutoScaleMode = AutoScaleMode.Dpi,
            MinimumSize = new System.Drawing.Size(350, 300) };
        dialog.Size = ClampSize(selectionSize, dialog.MinimumSize, Screen.PrimaryScreen?.WorkingArea.Size ?? new System.Drawing.Size(1280, 720));
        dialog.ResizeEnd += (_, _) => { if (dialog.WindowState == FormWindowState.Normal) { selectionSize = dialog.Size; SaveWindowSizes(); } };
        var hint = new Label { Dock = DockStyle.Top, Height = 100, Padding = new Padding(12), AutoEllipsis = false,
            Text = "Select batteries to include in the bank average. New devices are discovered while monitoring is running. Close and reopen this window to see new devices." };
        var checks = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true };
        foreach (var id in ids) checks.Items.Add("OB_" + id, selectedIds.Contains(id));
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 62, Padding = new Padding(10), FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
        var save = new Button { Text = "Save and resume", Width = 140, Height = 34, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Cancel", Width = 90, Height = 34, DialogResult = DialogResult.Cancel };
        buttons.Controls.Add(save); buttons.Controls.Add(cancel);
        dialog.Controls.Add(checks); dialog.Controls.Add(hint); dialog.Controls.Add(buttons);
        dialog.AcceptButton = save; dialog.CancelButton = cancel;
        dialog.Shown += (_, _) => { dialog.BringToFront(); dialog.Activate(); };
        if (dialog.ShowDialog() == DialogResult.OK)
        {
            lock (gate)
            {
                selectedIds.Clear();
                foreach (int i in checks.CheckedIndices) selectedIds.Add(ids[i]);
                foreach (var b in batteries.Values)
                {
                    b.Selected = selectedIds.Contains(b.Id);
                    b.Soc = null;
                    b.LastSeenUtc = default;
                }
            }
            SaveSettings();
        }
        if (dialog.WindowState == FormWindowState.Normal) { selectionSize = dialog.Size; SaveWindowSizes(); }
        if (paused) TogglePause();
    }

    private static System.Drawing.Size ClampSize(System.Drawing.Size requested, System.Drawing.Size minimum, System.Drawing.Size available)
    {
        int width = Math.Min(Math.Max(requested.Width, minimum.Width), Math.Max(minimum.Width, available.Width));
        int height = Math.Min(Math.Max(requested.Height, minimum.Height), Math.Max(minimum.Height, available.Height));
        return new System.Drawing.Size(width, height);
    }

    private void LoadWindowSizes()
    {
        try
        {
            if (!File.Exists(WindowSettingsPath)) return;
            var sizes = JsonSerializer.Deserialize<int[]>(File.ReadAllText(WindowSettingsPath));
            if (sizes?.Length == 4)
            {
                if (sizes[0] > 0 && sizes[1] > 0) selectionSize = new System.Drawing.Size(sizes[0], sizes[1]);
                if (sizes[2] > 0 && sizes[3] > 0) hoverSize = new System.Drawing.Size(sizes[2], sizes[3]);
            }
        }
        catch { /* Invalid size settings: use safe defaults. */ }
    }

    private void SaveWindowSizes()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(WindowSettingsPath)!);
            File.WriteAllText(WindowSettingsPath, JsonSerializer.Serialize(new[] { selectionSize.Width, selectionSize.Height, hoverSize.Width, hoverSize.Height }));
        }
        catch { /* Size preferences are optional. */ }
    }

    private void LoadSettings()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return;
            var ids = JsonSerializer.Deserialize<string[]>(File.ReadAllText(SettingsPath)) ?? Array.Empty<string>();
            foreach (var id in ids.Where(x => x.Length == 4 && x.All(Uri.IsHexDigit))) selectedIds.Add(id.ToUpperInvariant());
        }
        catch { /* Corrupt settings: start with no selected batteries. */ }
    }

    private void SaveSettings()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(selectedIds.OrderBy(x => x).ToArray()));
        }
        catch { MessageBox.Show("Could not save battery selection.", "ECO-WORTHY Community Edition"); }
    }

    private void ShowPanel()

    {
        if (quitting) return;
        var cursor = Cursor.Position;
        int x = Math.Max(Screen.FromPoint(cursor).WorkingArea.Left, cursor.X - 160);
        int y = cursor.Y - panel.Height - 15;
        var area = Screen.FromPoint(cursor).WorkingArea;
        if (x + panel.Width > area.Right) x = area.Right - panel.Width;
        if (y < area.Top) y = cursor.Y + 15;
        // Keep the popup stationary while visible: moving it on every tray MouseMove
        // causes flicker and makes it difficult to move the cursor onto the panel.
        if (!panel.Visible)
        {
            panel.Location = new System.Drawing.Point(x, y);
            panel.Show();
        }
    }

    protected override void ExitThreadCore()
    {
        quitting = true;
        stopping.Cancel();
        watcher.Stop();
        watcher.Received -= AdvertisementReceived;
        timer.Stop();
        tray.Visible = false;
        tray.Dispose();
        icon?.Dispose();
        panel.Close();
        stopping.Dispose();
        base.ExitThreadCore();
    }
}

internal sealed class HoverPanel : Form
{
    private readonly TextBox content;
    public HoverPanel()
    {
        FormBorderStyle = FormBorderStyle.SizableToolWindow;
        MinimumSize = new System.Drawing.Size(220, 180);
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        Width = 250;
        Height = 265;
        BackColor = Color.FromArgb(32, 34, 37);
        content = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            WordWrap = true,
            ScrollBars = ScrollBars.Vertical,
            BorderStyle = BorderStyle.None,
            BackColor = BackColor,
            ForeColor = Color.White,
            Font = new Font("Consolas", 10),
            TabStop = false
        };
        var padding = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12), BackColor = BackColor };
        padding.Controls.Add(content);
        Controls.Add(padding);
    }
    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams
    {
        get { var cp = base.CreateParams; cp.ExStyle |= 0x08000000; return cp; }
    }
    public void UpdateContent(string heading, List<string> lines)
    {
        content.Text = heading + Environment.NewLine + Environment.NewLine + string.Join(Environment.NewLine, lines);

    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        base.OnFormClosing(e);
    }
}

internal static class Native
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    public static extern bool DestroyIcon(IntPtr handle);
}

internal static class GraphicsExtensions
{
    public static void FillRoundedRectangle(this Graphics g, Brush brush, Rectangle rect, int radius)
    {
        using var path = new GraphicsPath();
        int d = radius * 2;
        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        g.FillPath(brush, path);
    }
}
