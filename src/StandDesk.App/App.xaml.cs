using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using StandDesk.Core;
using StandDesk.App.Services;

namespace StandDesk.App;

public partial class App : System.Windows.Application
{
    private Mutex? _instance;
    private bool _ownsMutex;
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string? className, string title);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string message);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
    internal static readonly uint ShowWindowMessage = RegisterWindowMessage("StandDesk.Desk2079.ShowWindow.v1");

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, args) =>
        {
            System.Windows.MessageBox.Show(args.Exception.Message, "StandDesk error", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
        _instance = new Mutex(true, @"Local\StandDesk-Desk2079", out _ownsMutex);
        if (!_ownsMutex)
        {
            var window = FindWindow(null, "StandDesk");
            if (window != IntPtr.Zero) PostMessage(window, ShowWindowMessage, IntPtr.Zero, IntPtr.Zero);
            Shutdown(); return;
        }
        try
        {
            var store = new SettingsStore(SettingsStore.ResolvePath(Environment.GetEnvironmentVariable("USERPROFILE"),
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile, Environment.SpecialFolderOption.DoNotVerify)));
            var background = e.Args.Contains("--background");
            DeskSettings settings;
            for (var attempt = 0; ; attempt++)
            {
                try { settings = await store.LoadAsync(requireExisting: background); break; }
                catch (IOException) when (background && attempt < 4) { await Task.Delay(500); }
            }
            WriteStartupDiagnostic(store.FilePath, settings, background);
            var probe = e.Args.FirstOrDefault(x => x.StartsWith("--probe=", StringComparison.Ordinal));
            if (probe is not null) { await ProbeAsync(probe[8..], settings); Shutdown(); return; }
            var test = e.Args.FirstOrDefault(x => x.StartsWith("--test-motion=", StringComparison.Ordinal));
            if (test is not null) { await TestMotionAsync(test[14..], settings, store); Shutdown(); return; }
            var window = new MainWindow(settings, store);
            MainWindow = window;
            window.Show();
            if (background) window.Hide();
        }
        catch (Exception ex)
        {
            var target = e.Args.FirstOrDefault(x => x.StartsWith("--probe=") || x.StartsWith("--test-motion="));
            if (target is not null)
            {
                var path = target[(target.IndexOf('=') + 1)..];
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
                await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new { error = ex.ToString() }, new JsonSerializerOptions { WriteIndented = true }));
            }
            else System.Windows.MessageBox.Show($"StandDesk could not start. Your saved settings were preserved.\n\n{ex.Message}", "StandDesk", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private static void WriteStartupDiagnostic(string path, DeskSettings settings, bool background)
    {
        try
        {
            var report = new { utc = DateTimeOffset.UtcNow, settingsPath = Path.GetFullPath(path), background,
                settings.MotionValidated, settings.HotkeysEnabled, settings.StartAtLogin,
                workingDirectory = Environment.CurrentDirectory, executable = Environment.ProcessPath };
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(path)!, "startup-diagnostic.json"),
                JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static async Task ProbeAsync(string path, DeskSettings settings)
    {
        await using var ble = new WinRtBleTransport();
        using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await ble.ConnectAsync(settings.Address, ct.Token);
        var offset = await ble.ReadBaseAsync(ct.Token);
        var reading = await ble.ReadAsync(ct.Token);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new { connected = ble.IsConnected, offsetRaw = offset, reading, heightCm = DeskProtocol.ToCm(reading.ExtensionRaw, offset), movementCommandsSent = false }, new JsonSerializerOptions { WriteIndented = true }));
    }

    // Explicit diagnostic mode only, invoked for the user's attended short test. Never runs at ordinary startup.
    private static async Task TestMotionAsync(string path, DeskSettings settings, SettingsStore store)
    {
        await using var ble = new WinRtBleTransport();
        using var motion = new MotionController(ble);
        var events = new List<ControllerState>();
        var traces = new List<string>();
        var sync = new object();
        ble.Trace += line => { lock (sync) traces.Add($"{DateTimeOffset.UtcNow:O} {line}"); };
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        motion.Changed += state => { lock (sync) events.Add(state); if (state.Status is "Raising desk…" or "Lowering desk…") started.TrySetResult(); };
        await motion.RefreshAsync(settings);
        var before = motion.State.Reading ?? throw new IOException(motion.State.Status);
        if (motion.State.Status != "Connected" || before.SpeedRaw != 0) throw new IOException("Desk must be connected and stationary before a motion test.");
        // First prove an automatic 1 cm arrival, then test interruption on a separate 2 cm target.
        var target = before.ExtensionRaw + 100 <= settings.MaxRaw ? before.ExtensionRaw + 100 : before.ExtensionRaw - 100;
        if (target < settings.MinRaw || target > settings.MaxRaw) throw new IOException("No short test target fits the verified range.");
        await motion.MoveAsync(settings, target).WaitAsync(TimeSpan.FromSeconds(12));
        using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var arrival = await ble.ReadAsync(ct.Token);
        var arrivalPassed = HardwareValidation.SuccessfulArrival(before.ExtensionRaw, arrival.ExtensionRaw, target, arrival.SpeedRaw, motion.State.Status != "Ready");
        var stopPassed = false;
        var after = arrival;
        if (arrivalPassed)
        {
            started = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var stopTarget = arrival.ExtensionRaw + 200 <= settings.MaxRaw ? arrival.ExtensionRaw + 200 : arrival.ExtensionRaw - 200;
            var movement = motion.MoveAsync(settings, stopTarget);
            await Task.WhenAny(started.Task, movement).WaitAsync(TimeSpan.FromSeconds(10));
            await Task.Delay(350);
            await motion.StopAsync();
            await movement.WaitAsync(TimeSpan.FromSeconds(10));
            using var stopRead = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            after = await ble.ReadAsync(stopRead.Token);
            var traveled = after.ExtensionRaw - arrival.ExtensionRaw;
            stopPassed = motion.State.Status is "Movement canceled or timed out" or "Ready"
                && Math.Abs(traveled) >= 20 && Math.Abs(traveled) <= 250 && after.SpeedRaw == 0
                && Math.Sign(traveled) == Math.Sign(stopTarget - arrival.ExtensionRaw);
        }
        var passed = arrivalPassed && stopPassed;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        ControllerState[] recorded; string[] trace; lock (sync) { recorded = events.ToArray(); trace = traces.ToArray(); }
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new { before, arrival, after, targetRaw = target, arrivalPassed, stopPassed, passed, events = recorded, trace }, new JsonSerializerOptions { WriteIndented = true }));
        // A short test validates only the short travel and stop path. Full preset journeys are recorded separately.
        await store.SaveAsync(settings with { MotionValidated = passed });
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_ownsMutex) _instance?.ReleaseMutex();
        _instance?.Dispose(); base.OnExit(e);
    }
}
