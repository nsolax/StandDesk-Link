using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using StandDesk.Core;
using StandDesk.App.Services;
using StandDesk.App.ViewModels;

namespace StandDesk.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm = new();
    private readonly WinRtBleTransport _ble = new();
    private readonly MotionController _motion;
    private readonly SettingsStore _store;
    private DeskSettings _settings;
    private HotkeyService? _hotkeys;
    private HwndSource? _windowSource;
    private TrayService? _tray;
    private readonly DispatcherTimer _idle = new() { Interval = TimeSpan.FromSeconds(30) };
    private int _min, _max;
    private bool _exiting, _loaded;
    private bool _saving;
    private readonly string _logFolder;

    public MainWindow(DeskSettings settings, SettingsStore store)
    {
        InitializeComponent();
        _settings = settings; _store = store;
        _logFolder = Path.GetDirectoryName(store.FilePath)!;
        DataContext = _vm;
        _motion = new(_ble);
        _motion.Changed += MotionStateChanged;
        _idle.Tick += Idle_Tick;
        Loaded += LoadedAsync;
        Closing += ClosingAsync;
        SystemEvents.SessionSwitch += SessionChanged;
        SystemEvents.PowerModeChanged += PowerChanged;
        SystemEvents.UserPreferenceChanged += ThemeChanged;
        ApplyTheme();
        LoadInputs();
    }

    private async void LoadedAsync(object sender, RoutedEventArgs e)
    {
        if (_loaded) return; _loaded = true;
        try
        {
            _hotkeys = new(new WindowInteropHelper(this).Handle);
            _windowSource = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            _windowSource.AddHook(AppMessage);
            _hotkeys.Pressed += RunAction;
            _tray = new(() => Dispatcher.Invoke(OpenWindow), RunAction, () => Dispatcher.BeginInvoke(async () => await ExitAsync()));
            if (File.Exists(_store.FilePath))
            {
                _settings = await _store.LoadAsync(requireExisting: true);
                LoadInputs();
            }
            if (_settings.HotkeysEnabled && _settings.MotionValidated) _hotkeys.Apply(_settings);
            await RefreshAsync();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private void Log(string message)
    {
        try
        {
            Directory.CreateDirectory(_logFolder);
            var file = Path.Combine(_logFolder, "activity.log");
            if (File.Exists(file) && new FileInfo(file).Length > 256_000) File.Move(file, file + ".previous", true);
            File.AppendAllText(file, $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private void MotionStateChanged(ControllerState state) => Dispatcher.BeginInvoke(() =>
    {
        if (_exiting) return;
        var changed = _vm.Status != state.Status;
        _vm.Status = state.Status;
        _vm.CanEdit = !state.Busy;
        _vm.CanMove = _settings.MotionValidated && !state.Busy;
        _vm.Connection = _ble.IsConnected ? "Connected · Bluetooth" : "Disconnected · reconnects on action";
        if (state.Reading is { } reading)
        {
            var cm = DeskProtocol.ToCm(reading.ExtensionRaw, _settings.BaseRaw);
            _vm.Height = cm.ToString("0.00", CultureInfo.CurrentCulture);
            var lift = Math.Clamp((double)(cm - 62.5m), 0, 55);
            Canvas.SetBottom(DeskTop, 50 + lift);
            LeftLeg.Height = RightLeg.Height = 46 + lift;
            _tray?.Update(cm, _vm.CanMove);
        }
        else _tray?.Update(null, _vm.CanMove);
        if (changed) Log(state.Status);
        _idle.Stop(); if (!state.Busy && _ble.IsConnected) _idle.Start();
    });

    private void LoadInputs()
    {
        _min = _settings.MinRaw; _max = _settings.MaxRaw;
        SitInput.Text = _vm.Sit = _settings.SitCm.ToString("0.00", CultureInfo.CurrentCulture);
        StandInput.Text = _vm.Stand = _settings.StandCm.ToString("0.00", CultureInfo.CurrentCulture);
        SitKeyInput.Text = _settings.SitKey; StandKeyInput.Text = _settings.StandKey; StopKeyInput.Text = _settings.StopKey;
        EnableHotkeys.IsChecked = _settings.HotkeysEnabled; StartAtLogin.IsChecked = _settings.StartAtLogin;
        _vm.SitKey = _settings.HotkeysEnabled ? _settings.SitKey : "Keyboard shortcut off";
        _vm.StandKey = _settings.HotkeysEnabled ? _settings.StandKey : "Keyboard shortcut off";
        _vm.CanMove = _settings.MotionValidated && !_motion.State.Busy;
        ValidationNotice.Visibility = _settings.MotionValidated ? Visibility.Collapsed : Visibility.Visible;
        UpdateRange();
    }

    private void UpdateRange() => RangeText.Text = $"Verified range: {DeskProtocol.ToCm(_min, _settings.BaseRaw):0.00}–{DeskProtocol.ToCm(_max, _settings.BaseRaw):0.00} cm. Capture a physical position to extend it.";
    private async Task RefreshAsync() { _idle.Stop(); await _motion.RefreshAsync(_settings); }
    private async void Refresh_Click(object sender, RoutedEventArgs e) { try { await RefreshAsync(); } catch (Exception ex) { ShowError(ex); } }
    private void Sit_Click(object sender, RoutedEventArgs e) => RunAction("sit");
    private void Stand_Click(object sender, RoutedEventArgs e) => RunAction("stand");
    private void Stop_Click(object sender, RoutedEventArgs e) => RunAction("stop");
    private async void RunAction(string action)
    {
        if (!Dispatcher.CheckAccess()) { _ = Dispatcher.BeginInvoke(() => RunAction(action)); return; }
        try
        {
            _idle.Stop();
            if (action == "stop") { await _motion.StopAsync(); return; }
            if (_saving) return;
            if (!_settings.MotionValidated) { _vm.Status = "Complete the supervised movement validation first."; return; }
            if (_exiting) return;
            await _motion.MoveAsync(_settings, action == "sit" ? _settings.SitRaw : _settings.StandRaw);
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private static decimal ParseCm(string value)
    {
        if (!decimal.TryParse(value, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.CurrentCulture, out var cm))
            throw new ArgumentException("Enter a valid height in centimeters using your Windows decimal separator.");
        return cm;
    }
    private void Step_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var parts = ((string)((Button)sender).Tag).Split(':');
            var input = parts[0] == "sit" ? SitInput : StandInput;
            input.Text = (ParseCm(input.Text) + decimal.Parse(parts[1], CultureInfo.InvariantCulture)).ToString("0.00", CultureInfo.CurrentCulture);
        }
        catch (Exception ex) { HeightMessage.Text = ex.Message; }
    }
    private async void Capture_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_motion.State.Busy) return;
            var kind = (string)((Button)sender).Tag;
            await RefreshAsync();
            var reading = _motion.State.Reading;
            if (!_ble.IsConnected || reading is null || reading.SpeedRaw != 0 || (DateTimeOffset.UtcNow - reading.At).Duration() > TimeSpan.FromSeconds(1) || _motion.State.Status != "Connected")
                throw new InvalidOperationException("A fresh stationary height is required. Release the paddle, then try again.");
            if (kind == "lower") _min = reading.ExtensionRaw;
            else if (kind == "upper") _max = reading.ExtensionRaw;
            else
            {
                (kind == "sit" ? SitInput : StandInput).Text = DeskProtocol.ToCm(reading.ExtensionRaw, _settings.BaseRaw).ToString("0.00", CultureInfo.CurrentCulture);
                _min = Math.Min(_min, reading.ExtensionRaw); _max = Math.Max(_max, reading.ExtensionRaw);
            }
            UpdateRange(); HeightMessage.Text = "Position captured. Save heights to keep these changes.";
        }
        catch (Exception ex) { HeightMessage.Text = ex.Message; }
    }
    private async void SaveHeights_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_motion.State.Busy) return;
            _saving = true;
            _vm.CanMove = _vm.CanEdit = false;
            var next = _settings with { SitRaw = DeskProtocol.FromCm(ParseCm(SitInput.Text), _settings.BaseRaw), StandRaw = DeskProtocol.FromCm(ParseCm(StandInput.Text), _settings.BaseRaw), MinRaw = _min, MaxRaw = _max };
            next.Validate(); await _store.SaveAsync(next); _settings = next; LoadInputs();
            HeightMessage.Text = "Heights saved. The desk has not moved."; Log("Saved local preset heights");
        }
        catch (Exception ex) { HeightMessage.Text = ex.Message; }
        finally { _saving = false; _vm.CanEdit = !_motion.State.Busy; _vm.CanMove = _settings.MotionValidated && _vm.CanEdit; }
    }
    private void CancelHeights_Click(object sender, RoutedEventArgs e) { LoadInputs(); HeightMessage.Text = "Unsaved changes discarded."; }

    private static void SetStartup(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        if (enabled)
        {
            var path = Environment.ProcessPath ?? throw new InvalidOperationException("Executable path is unavailable.");
            if (Path.GetFileNameWithoutExtension(path).Equals("dotnet", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Enable startup from the published StandDesk.exe.");
            key.SetValue("StandDesk", $"\"{path}\" --background");
        }
        else key.DeleteValue("StandDesk", false);
    }
    private async void SaveSettings_Click(object sender, RoutedEventArgs e)
    {
        var old = _settings;
        try
        {
            if (_motion.State.Busy) return;
            _saving = true;
            _vm.CanMove = _vm.CanEdit = false;
            var next = old with { SitKey = SitKeyInput.Text.Trim(), StandKey = StandKeyInput.Text.Trim(), StopKey = StopKeyInput.Text.Trim(), HotkeysEnabled = EnableHotkeys.IsChecked == true, StartAtLogin = StartAtLogin.IsChecked == true };
            next.Validate(); HotkeySpec.Parse(next.SitKey); HotkeySpec.Parse(next.StandKey); HotkeySpec.Parse(next.StopKey);
            if (next.HotkeysEnabled && !next.MotionValidated) throw new InvalidOperationException("Movement validation is required before enabling hotkeys.");
            _hotkeys?.Apply(next);
            try { if (old.StartAtLogin != next.StartAtLogin) SetStartup(next.StartAtLogin); await _store.SaveAsync(next); }
            catch { _hotkeys?.Apply(old); if (old.StartAtLogin != next.StartAtLogin) SetStartup(old.StartAtLogin); throw; }
            _settings = next; LoadInputs(); SettingsMessage.Text = next.HotkeysEnabled ? "Settings saved. Shortcuts are available while StandDesk runs." : "Settings saved. Keyboard shortcuts are off.";
        }
        catch (Exception ex) { SettingsMessage.Text = ex.Message; }
        finally { _saving = false; _vm.CanEdit = !_motion.State.Busy; _vm.CanMove = _settings.MotionValidated && _vm.CanEdit; }
    }

    private async void Idle_Tick(object? sender, EventArgs e)
    {
        _idle.Stop(); if (_motion.State.Busy) return;
        try { await _ble.DisconnectAsync(); _vm.Connection = "Idle · last read height"; }
        catch (Exception ex) { ShowError(ex); }
    }
    public async void OpenWindow()
    {
        Show(); WindowState = WindowState.Normal; Activate();
        try
        {
            // Restoring a startup/tray window must reflect the persisted validation result, not an old session snapshot.
            if (_motion.State.Busy || _saving) return;
            var saved = await _store.LoadAsync(requireExisting: true);
            if (_motion.State.Busy || _saving) return;
            if (saved == _settings) return;
            _hotkeys?.Apply(saved.MotionValidated ? saved : saved with { HotkeysEnabled = false });
            _settings = saved;
            LoadInputs();
        }
        catch (Exception ex) { ShowError(ex); }
    }
    private IntPtr AppMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if ((uint)message == App.ShowWindowMessage) { handled = true; OpenWindow(); }
        return IntPtr.Zero;
    }
    private async void ClosingAsync(object? sender, CancelEventArgs e)
    {
        if (_exiting) return;
        e.Cancel = true;
        if (_tray is null) { await ExitAsync(); return; }
        try { await _motion.StopAsync(); Hide(); } catch (Exception ex) { ShowError(ex); }
    }
    private async void Exit_Click(object sender, RoutedEventArgs e) => await ExitAsync();
    private async Task ExitAsync()
    {
        if (_exiting) return; _exiting = true;
        _idle.Stop();
        try
        {
            await _motion.StopAsync();
            var deadline = DateTime.UtcNow.AddSeconds(4);
            while (_motion.State.Busy && DateTime.UtcNow < deadline) await Task.Delay(50);
            _hotkeys?.Dispose(); _tray?.Dispose(); _motion.Dispose();
            _windowSource?.RemoveHook(AppMessage);
            SystemEvents.SessionSwitch -= SessionChanged; SystemEvents.PowerModeChanged -= PowerChanged; SystemEvents.UserPreferenceChanged -= ThemeChanged;
            await _ble.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
        }
        catch (Exception ex) { Log("Shutdown: " + ex.Message); }
        finally { System.Windows.Application.Current.Shutdown(); }
    }
    private void SessionChanged(object sender, SessionSwitchEventArgs e) { if (e.Reason == SessionSwitchReason.SessionLock) RunAction("stop"); }
    private void PowerChanged(object sender, PowerModeChangedEventArgs e) { if (e.Mode == PowerModes.Suspend) RunAction("stop"); }
    private void ThemeChanged(object sender, UserPreferenceChangedEventArgs e) => Dispatcher.BeginInvoke(ApplyTheme);
    private void ApplyTheme()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        var dark = key?.GetValue("AppsUseLightTheme") is int v && v == 0;
        var colors = dark ? new[] { "#202020", "#2B2B2B", "#F5F5F5", "#BCBCBC", "#444444", "#60B7FF", "#071C2C", "#FFB4AB" }
            : new[] { "#F5F5F5", "#FFFFFF", "#202020", "#606060", "#DEDEDE", "#0067C0", "#FFFFFF", "#B42318" };
        var names = new[] { "Surface", "Panel", "Ink", "Muted", "Line", "Accent", "OnAccent", "Danger" };
        for (var i = 0; i < names.Length; i++) System.Windows.Application.Current.Resources[names[i]] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors[i]));
    }
    private void ShowError(Exception ex) { _vm.Status = ex.Message; Log("Error: " + ex); }
}
