namespace StandDesk.Core;

public sealed record MotionOptions
{
    public TimeSpan RefreshPeriod { get; init; } = TimeSpan.FromMilliseconds(150);
    public TimeSpan ProgressTimeout { get; init; } = TimeSpan.FromSeconds(2);
    public TimeSpan TravelTimeout { get; init; } = TimeSpan.FromSeconds(45);
    public int ArrivalToleranceRaw { get; init; } = 30;
}

public sealed record ControllerState(string Status, bool Busy, DeskReading? Reading = null);

public sealed class MotionController : IDisposable
{
    private readonly IBleTransport _ble;
    private readonly MotionOptions _options;
    private readonly TimeProvider _clock;
    private CancellationTokenSource? _operation;
    private readonly object _cancellationSync = new();
    private int _busy;
    private int? _target;
    private string? _fault;
    public ControllerState State { get; private set; } = new("Disconnected", false);
    public event Action<ControllerState>? Changed;

    public MotionController(IBleTransport ble, MotionOptions? options = null, TimeProvider? clock = null)
    {
        _ble = ble;
        _options = options ?? new();
        _clock = clock ?? TimeProvider.System;
        _ble.Faulted += OnFault;
    }

    private void CancelCurrent()
    {
        lock (_cancellationSync) _operation?.Cancel();
    }
    private void SetOperation(CancellationTokenSource? operation)
    {
        lock (_cancellationSync) _operation = operation;
    }
    private void OnFault(string fault) { _fault = fault; CancelCurrent(); }
    private void Publish(string text, DeskReading? reading = null)
    {
        State = new(text, Volatile.Read(ref _busy) != 0, reading ?? State.Reading);
        Changed?.Invoke(State);
    }

    private async Task<DeskReading> PrepareAsync(DeskSettings settings, CancellationToken ct)
    {
        settings.Validate();
        Publish("Connecting…");
        await _ble.ConnectAsync(settings.Address, ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        var calibration = await _ble.ReadBaseAsync(ct).ConfigureAwait(false);
        if (calibration != settings.BaseRaw) throw new InvalidOperationException("Desk calibration changed. Check the height offset before moving.");
        ct.ThrowIfCancellationRequested();
        var value = await _ble.ReadAsync(ct).ConfigureAwait(false);
        ValidateReading(value);
        return value;
    }

    private void ValidateReading(DeskReading reading)
    {
        if (reading.ExtensionRaw is < 0 or > ushort.MaxValue || (_clock.GetUtcNow() - reading.At).Duration() > TimeSpan.FromSeconds(1))
            throw new InvalidOperationException("Desk height feedback is invalid or stale.");
    }

    public async Task RefreshAsync(DeskSettings settings)
    {
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0) return;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        SetOperation(cts);
        _fault = null;
        string result = "Connected";
        try { var value = await PrepareAsync(settings, cts.Token); cts.Token.ThrowIfCancellationRequested(); Publish("Connected", value); }
        catch (Exception ex) { result = ex is OperationCanceledException ? _fault ?? "Connection canceled or timed out" : ex.Message; }
        finally { SetOperation(null); Interlocked.Exchange(ref _busy, 0); Publish(result); }
    }

    public async Task MoveAsync(DeskSettings settings, int targetRaw)
    {
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
        {
            if (_target.HasValue && _target != targetRaw) await StopAsync();
            return;
        }
        _target = targetRaw;
        _fault = null;
        using var cts = new CancellationTokenSource(_options.TravelTimeout);
        SetOperation(cts);
        var ct = cts.Token;
        var wrote = false;
        var outcome = "Ready";
        try
        {
            settings.Validate();
            if (targetRaw < settings.MinRaw || targetRaw > settings.MaxRaw) throw new ArgumentException("Target is outside the verified operating range.");
            var reading = await PrepareAsync(settings, ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            if (reading.SpeedRaw != 0) throw new InvalidOperationException("Desk is already moving. Release the paddle and try again.");
            var direction = Math.Sign(targetRaw - reading.ExtensionRaw);
            if (Math.Abs(targetRaw - reading.ExtensionRaw) <= _options.ArrivalToleranceRaw) { Publish("Already at target", reading); return; }
            await _ble.WakeAsync(ct).ConfigureAwait(false);
            await _ble.StopAsync(ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            var bestDistance = Math.Abs(targetRaw - reading.ExtensionRaw);
            var lastProgress = _clock.GetTimestamp();
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                if (_fault is not null) throw new InvalidOperationException(_fault);
                ValidateReading(reading);
                var distance = Math.Abs(targetRaw - reading.ExtensionRaw);
                if (distance <= _options.ArrivalToleranceRaw) break;
                if (Math.Sign(targetRaw - reading.ExtensionRaw) != direction) throw new InvalidOperationException("Desk passed the target; stopped.");
                if (Math.Abs(reading.SpeedRaw) > 100 && Math.Sign(reading.SpeedRaw) != direction) throw new InvalidOperationException("Desk moved in the wrong direction; stopped.");
                if (distance < bestDistance - 2) { bestDistance = distance; lastProgress = _clock.GetTimestamp(); }
                if (_clock.GetElapsedTime(lastProgress) > _options.ProgressTimeout) throw new InvalidOperationException("No progress detected; stopped. Check the desk and try again.");
                Publish(direction > 0 ? "Raising desk…" : "Lowering desk…", reading);
                ct.ThrowIfCancellationRequested();
                wrote = true;
                await _ble.WriteTargetAsync(targetRaw, ct).ConfigureAwait(false);
                await Task.Delay(_options.RefreshPeriod, _clock, ct).ConfigureAwait(false);
                reading = await _ble.ReadAsync(ct).ConfigureAwait(false);
            }
        }
        catch (Exception ex) { outcome = ex is OperationCanceledException ? _fault ?? "Movement canceled or timed out" : ex.Message; }
        finally
        {
            if (wrote)
            {
                try
                {
                    using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                    await _ble.StopAsync(stop.Token).ConfigureAwait(false);
                    DeskReading final;
                    do
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(100), _clock, stop.Token).ConfigureAwait(false);
                        final = await _ble.ReadAsync(stop.Token).ConfigureAwait(false);
                    } while (final.SpeedRaw != 0);
                    ValidateReading(final);
                    Publish(outcome, final);
                    if (outcome == "Ready" && (final.SpeedRaw != 0 || Math.Abs(final.ExtensionRaw - targetRaw) > _options.ArrivalToleranceRaw))
                        outcome = "Stopped; target arrival was not confirmed.";
                }
                catch { outcome = "Stopped sending commands; physical stop could not be confirmed. Use the desk paddle."; }
            }
            SetOperation(null);
            _target = null;
            Interlocked.Exchange(ref _busy, 0);
            Publish(outcome);
        }
    }

    public async Task StopAsync()
    {
        CancelCurrent();
        Publish("Stopping…");
        if (_ble.IsConnected)
        {
            try { using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3)); await _ble.StopAsync(cts.Token).ConfigureAwait(false); }
            catch { Publish("Stop could not be confirmed. Use the desk paddle."); return; }
        }
        Publish("Stop requested");
    }
    public void Dispose() { _ble.Faulted -= OnFault; CancelCurrent(); }
}
