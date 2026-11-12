using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NetworkSpeed.App.Composition;
using NetworkSpeed.Core.Adapters;
using NetworkSpeed.Core.Formatting;
using NetworkSpeed.Core.Input;
using NetworkSpeed.Core.Settings;
using NetworkSpeed.Core.Taskbar;
using NetworkSpeed.Interop;
using ThemeMode = NetworkSpeed.Core.Settings.ThemeMode;

namespace NetworkSpeed.App.Settings;

/// <summary>
/// Jendela Settings: setiap perubahan langsung diterapkan ke overlay (tanpa menulis file);
/// file disimpan saat jendela ditutup (spec §5).
/// </summary>
public partial class SettingsWindow : Window
{
    private const double BytesPerMegabyte = 1024d * 1024;

    private readonly AppHost _host;
    private readonly Func<IReadOnlyList<TaskbarLayout>> _layouts;
    private readonly Func<string?> _hiddenReason;
    private readonly Func<string> _hotkeyStatus;
    private readonly Func<string> _updateStatus;
    // true sampai Load() pertama selesai: event slider sudah terpicu saat InitializeComponent, sebelum semua kontrol ada.
    private bool _loading = true;

    /// <param name="updateFolder">Folder update dari HKLM (read-only, diatur IT); <c>null</c> = tidak dikonfigurasi.</param>
    public SettingsWindow(AppHost host, Func<IReadOnlyList<TaskbarLayout>> layouts, Func<string?> hiddenReason, Func<string> hotkeyStatus, string? updateFolder, Func<string> updateStatus)
    {
        ArgumentNullException.ThrowIfNull(host);
        _host = host;
        _layouts = layouts;
        _hiddenReason = hiddenReason;
        _hotkeyStatus = hotkeyStatus;
        _updateStatus = updateStatus;
        InitializeComponent();
        UpdateFolderText.Text = "Update folder (set by IT): " + (updateFolder ?? "not configured");
        FillChoices();
        Load(host.Settings);
        Closed += (_, _) => _host.UpdateSettings(_host.Settings, persist: true);
    }

    /// <summary>Memperbarui status hotkey/posisi bila berubah saat jendela terbuka.</summary>
    public void RefreshStatus()
    {
        HotkeyHint.Text = _hotkeyStatus();
        UpdateStatusText.Text = _updateStatus();
        StatusText.Text = _hiddenReason() is { } reason ? "Overlay hidden: " + reason : string.Empty;
        var options = SettingsEditor.PositionOptions(_layouts());
        SetPositionOption(LeftRadio, options, OverlayPosition.Left);
        SetPositionOption(CenterRadio, options, OverlayPosition.Center);
        SetPositionOption(RightRadio, options, OverlayPosition.Right);
        PositionHint.Text = string.Join(Environment.NewLine, options.Where(o => !o.IsEnabled).Select(o => $"{o.Position}: {o.DisabledReason}"));
    }

    private static void SetPositionOption(RadioButton radio, IReadOnlyList<PositionOption> options, OverlayPosition position)
    {
        var option = options.Single(o => o.Position == position);
        radio.IsEnabled = option.IsEnabled;
        radio.ToolTip = option.DisabledReason;
    }

    private static T Selected<T>(ComboBox combo, T fallback) => combo.SelectedItem is T value ? value : fallback;

    // Kosong = kuota mati; angka tidak valid ditandai dan nilai lama dipertahankan.
    private static double? ParseGb(TextBox box, double? current)
    {
        var text = box.Text.Trim();
        if (text.Length == 0)
        {
            box.BorderBrush = SystemColors.ControlDarkBrush;
            return null;
        }

        var valid = double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var gb) && gb > 0;
        box.BorderBrush = valid ? SystemColors.ControlDarkBrush : Brushes.Red;
        return valid ? gb : current;
    }

    private static string? EmptyToNull(string text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private static int ParseInt(string text, int fallback) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : fallback;

    private void FillChoices()
    {
        UnitCombo.ItemsSource = Enum.GetValues<SpeedUnitMode>();
        AdapterModeCombo.ItemsSource = Enum.GetValues<AdapterSelectionMode>();
        LayoutCombo.ItemsSource = Enum.GetValues<OverlayLayout>();
        ThemeCombo.ItemsSource = Enum.GetValues<ThemeMode>();
        MonitorModeCombo.ItemsSource = Enum.GetValues<MonitorMode>();
        QuotaStartCombo.ItemsSource = Enumerable.Range(1, 31).ToList();
        FontCombo.ItemsSource = Fonts.SystemFontFamilies.Select(f => f.Source).Order(StringComparer.OrdinalIgnoreCase).ToList();
        MonitorCombo.ItemsSource = _layouts().Select(l => l.MonitorId).ToList();
        try
        {
            AdapterCombo.ItemsSource = new IfTableCounterSource().ReadAdapters()
                .Where(a => a.Kind != AdapterKind.Loopback)
                .Select(a => new AdapterChoice(a.Id, $"{a.Name} ({a.Kind}{(a.IsUp ? string.Empty : ", down")})"))
                .ToList();
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            _host.Log.Error("Listing adapters for settings failed.", ex);
            AdapterCombo.ItemsSource = Array.Empty<AdapterChoice>();
        }
    }

    private void Load(UserSettings s)
    {
        _loading = true;
        UnitCombo.SelectedItem = s.UnitMode;
        RefreshSlider.Value = s.RefreshIntervalMs;
        AutostartCheck.IsChecked = s.AutostartEnabled;
        AdapterModeCombo.SelectedItem = s.AdapterMode;
        AdapterCombo.SelectedItem = (AdapterCombo.ItemsSource as IEnumerable<AdapterChoice>)?.FirstOrDefault(a => a.Id == s.SpecificAdapterId);
        VpnCheck.IsChecked = s.IncludeVpnInAll;
        LeftRadio.IsChecked = s.Position == OverlayPosition.Left;
        CenterRadio.IsChecked = s.Position == OverlayPosition.Center;
        RightRadio.IsChecked = s.Position == OverlayPosition.Right;
        var offset = s.OffsetFor(s.Position);
        OffsetXBox.Text = offset.X.ToString(CultureInfo.InvariantCulture);
        OffsetYBox.Text = offset.Y.ToString(CultureInfo.InvariantCulture);
        LayoutCombo.SelectedItem = s.Layout;
        FontCombo.SelectedItem = s.FontFamily;
        FontSizeSlider.Value = s.FontSize;
        UpColorBox.Text = s.UpColor ?? string.Empty;
        DownColorBox.Text = s.DownColor ?? string.Empty;
        ThemeCombo.SelectedItem = s.Theme;
        OpacitySlider.Value = s.BackgroundOpacity;
        ThresholdBox.Text = s.AlertThresholdBytesPerSecond is { } t ? (t / BytesPerMegabyte).ToString("0.##", CultureInfo.InvariantCulture) : string.Empty;
        AlertColorBox.Text = s.AlertColor;
        FullscreenCheck.IsChecked = s.HideOnFullscreen;
        MonitorModeCombo.SelectedItem = s.MonitorMode;
        MonitorCombo.SelectedItem = s.SpecificMonitorId;
        PingCheck.IsChecked = s.PingEnabled;
        PingHostBox.Text = s.PingHost ?? string.Empty;
        PingIntervalSlider.Value = s.PingIntervalMs;
        LatencyOverlayCheck.IsChecked = s.ShowLatencyOnOverlay;
        ConnectivityCheck.IsChecked = s.ConnectivityNotifications;
        CheckUpdatesCheck.IsChecked = s.CheckForUpdates;
        HotkeyBox.Text = s.Hotkey;
        QuotaDailyBox.Text = s.QuotaDailyGb?.ToString("R", CultureInfo.InvariantCulture) ?? string.Empty;
        QuotaMonthlyBox.Text = s.QuotaMonthlyGb?.ToString("R", CultureInfo.InvariantCulture) ?? string.Empty;
        QuotaStartCombo.SelectedItem = s.QuotaMonthStartDay;
        _loading = false;
        UpdateDependentControls(s);
        RefreshStatus();
    }

    private UserSettings Read()
    {
        var current = _host.Settings;
        var position = LeftRadio.IsChecked == true ? OverlayPosition.Left
            : CenterRadio.IsChecked == true ? OverlayPosition.Center
            : OverlayPosition.Right;
        var oldOffset = current.OffsetFor(position);
        var offset = new PixelOffset(ParseInt(OffsetXBox.Text, oldOffset.X), ParseInt(OffsetYBox.Text, oldOffset.Y));
        var threshold = double.TryParse(ThresholdBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var mb) && mb > 0
            ? mb * BytesPerMegabyte
            : (double?)null;

        return current with
        {
            UnitMode = Selected(UnitCombo, current.UnitMode),
            RefreshIntervalMs = (int)RefreshSlider.Value,
            AutostartEnabled = AutostartCheck.IsChecked == true,
            AdapterMode = Selected(AdapterModeCombo, current.AdapterMode),
            SpecificAdapterId = (AdapterCombo.SelectedItem as AdapterChoice)?.Id ?? current.SpecificAdapterId,
            IncludeVpnInAll = VpnCheck.IsChecked == true,
            Position = position,
            OffsetLeft = position == OverlayPosition.Left ? offset : current.OffsetLeft,
            OffsetCenter = position == OverlayPosition.Center ? offset : current.OffsetCenter,
            OffsetRight = position == OverlayPosition.Right ? offset : current.OffsetRight,
            Layout = Selected(LayoutCombo, current.Layout),
            FontFamily = Selected(FontCombo, current.FontFamily),
            FontSize = FontSizeSlider.Value,
            UpColor = ValidColorOrKeep(UpColorBox, current.UpColor, allowEmpty: true),
            DownColor = ValidColorOrKeep(DownColorBox, current.DownColor, allowEmpty: true),
            Theme = Selected(ThemeCombo, current.Theme),
            BackgroundOpacity = OpacitySlider.Value,
            AlertThresholdBytesPerSecond = threshold,
            AlertColor = ValidColorOrKeep(AlertColorBox, current.AlertColor, allowEmpty: false) ?? current.AlertColor,
            HideOnFullscreen = FullscreenCheck.IsChecked == true,
            MonitorMode = Selected(MonitorModeCombo, current.MonitorMode),
            SpecificMonitorId = MonitorCombo.SelectedItem as string ?? current.SpecificMonitorId,
            PingEnabled = PingCheck.IsChecked == true,
            PingHost = EmptyToNull(PingHostBox.Text),
            PingIntervalMs = (int)PingIntervalSlider.Value,
            ShowLatencyOnOverlay = LatencyOverlayCheck.IsChecked == true,
            ConnectivityNotifications = ConnectivityCheck.IsChecked == true,
            CheckForUpdates = CheckUpdatesCheck.IsChecked == true,
            Hotkey = HotkeyGesture.TryParse(HotkeyBox.Text, out var g) ? g.ToString() : current.Hotkey,
            QuotaDailyGb = ParseGb(QuotaDailyBox, current.QuotaDailyGb),
            QuotaMonthlyGb = ParseGb(QuotaMonthlyBox, current.QuotaMonthlyGb),
            QuotaMonthStartDay = QuotaStartCombo.SelectedItem is int day ? day : current.QuotaMonthStartDay,
        };
    }

    // Warna tidak valid ditandai dan tidak diterapkan; nilai lama dipertahankan.
    private static string? ValidColorOrKeep(TextBox box, string? current, bool allowEmpty)
    {
        var text = box.Text.Trim();
        var valid = (allowEmpty && text.Length == 0) || SettingsValidator.IsValidColor(text);
        box.BorderBrush = valid ? SystemColors.ControlDarkBrush : Brushes.Red;
        return valid ? EmptyToNull(text) : current;
    }

    private void Apply()
    {
        if (_loading)
        {
            return;
        }

        var updated = Read();
        _host.UpdateSettings(updated, persist: false);
        HotkeyBox.BorderBrush = HotkeyGesture.TryParse(HotkeyBox.Text, out _) ? SystemColors.ControlDarkBrush : Brushes.Red;
        UpdateDependentControls(_host.Settings);
        RefreshStatus();
    }

    private void UpdateDependentControls(UserSettings s)
    {
        AdapterCombo.IsEnabled = s.AdapterMode == AdapterSelectionMode.Specific;
        VpnCheck.IsEnabled = s.AdapterMode == AdapterSelectionMode.All;
        MonitorCombo.IsEnabled = s.MonitorMode == MonitorMode.Specific;
        PingHostBox.IsEnabled = PingIntervalSlider.IsEnabled = s.PingEnabled;
        var latencyAvailable = SettingsEditor.IsLatencyOnOverlayAvailable(s);
        LatencyOverlayCheck.IsEnabled = s.PingEnabled && latencyAvailable;
        LatencyOverlayHint.Text = latencyAvailable ? string.Empty : "Latency is shown on the overlay only with the one-line layout.";
        RefreshValue.Text = $"{s.RefreshIntervalMs} ms";
        FontSizeValue.Text = s.FontSize.ToString("0.#", CultureInfo.InvariantCulture) + " pt";
        OpacityValue.Text = s.BackgroundOpacity.ToString("P0", CultureInfo.InvariantCulture);
        PingIntervalValue.Text = $"{s.PingIntervalMs / 1000} s";
    }

    private void OnChanged(object sender, RoutedEventArgs e)
    {
        // Ganti posisi: tampilkan offset milik posisi baru.
        if (!_loading && sender is RadioButton)
        {
            _loading = true;
            var offset = _host.Settings.OffsetFor(LeftRadio.IsChecked == true ? OverlayPosition.Left : CenterRadio.IsChecked == true ? OverlayPosition.Center : OverlayPosition.Right);
            OffsetXBox.Text = offset.X.ToString(CultureInfo.InvariantCulture);
            OffsetYBox.Text = offset.Y.ToString(CultureInfo.InvariantCulture);
            _loading = false;
        }

        Apply();
    }

    private void OnSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => Apply();

    private void OnReset(object sender, RoutedEventArgs e)
    {
        _host.UpdateSettings(new UserSettings(), persist: false);
        Load(_host.Settings);
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private sealed record AdapterChoice(Guid Id, string Label);
}
