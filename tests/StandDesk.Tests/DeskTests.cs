using StandDesk.Core;

namespace StandDesk.Tests;

public sealed class DeskTests
{
    [Theory]
    [InlineData(1162, 74.12)]
    [InlineData(5437, 116.87)]
    public void HeightUsesRawExtensionAndCalibratedBase(int raw, double cm) =>
        Assert.Equal((decimal)cm, DeskProtocol.ToCm(raw, 6250));

    [Fact]
    public void HeightPacketRejectsTruncation() =>
        Assert.Throws<InvalidDataException>(() => DeskProtocol.Decode([0, 0, 1]));

    [Fact]
    public void HeightPacketDecodesSignedSpeed()
    {
        var value = DeskProtocol.Decode([0x8a, 0x04, 0xff, 0xff]);
        Assert.Equal(1162, value.ExtensionRaw);
        Assert.Equal(-1, value.SpeedRaw);
    }

    [Fact]
    public void DefaultPresetsPreserveCapturedPrecision()
    {
        var settings = new DeskSettings();
        Assert.Equal(74.12m, settings.SitCm);
        Assert.Equal(116.87m, settings.StandCm);
        settings.Validate();
    }

    [Fact]
    public void InvalidPresetOrderAndUnverifiedRangeAreRejected()
    {
        Assert.Throws<ArgumentException>(() => (new DeskSettings() with { SitRaw = 6000 }).Validate());
        Assert.Throws<ArgumentException>(() => (new DeskSettings() with { StandRaw = 5500 }).Validate());
    }

    [Fact]
    public async Task SavingSettingsSurvivesRestartWithoutMovingAnything()
    {
        var folder = Path.Combine(Path.GetTempPath(), "StandDeskTests", Guid.NewGuid().ToString());
        try
        {
            var store = new SettingsStore(Path.Combine(folder, "settings.json"));
            await store.SaveAsync(new DeskSettings() with { SitRaw = 1250 });
            Assert.Equal(75m, (await new SettingsStore(store.FilePath).LoadAsync()).SitCm);
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }

    [Fact]
    public async Task InvalidStoredSettingsAreNotSilentlyOverwritten()
    {
        var folder = Path.Combine(Path.GetTempPath(), "StandDeskTests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "settings.json");
        try
        {
            await File.WriteAllTextAsync(path, "{broken");
            await Assert.ThrowsAnyAsync<Exception>(() => new SettingsStore(path).LoadAsync());
            Assert.Equal("{broken", await File.ReadAllTextAsync(path));
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public async Task AlreadyAtTargetDoesNotWriteMotion()
    {
        var ble = new FakeBle { Raw = 1162 };
        var motion = new MotionController(ble);
        await motion.MoveAsync(new DeskSettings(), 1162);
        Assert.Empty(ble.Targets);
    }

    [Fact]
    public async Task ArrivesAndStopsAtTarget()
    {
        var ble = new FakeBle { Raw = 1162, Step = 20 };
        var motion = new MotionController(ble, FastOptions());
        await motion.MoveAsync(new DeskSettings(), 1202);
        Assert.True(ble.Stops > 0);
        Assert.InRange(ble.Raw, 1192, 1212);
        Assert.Equal("Ready", motion.State.Status);
    }

    [Fact]
    public async Task StopDuringConnectCannotStartMovementLater()
    {
        var ble = new FakeBle { ConnectGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously) };
        var motion = new MotionController(ble, FastOptions());
        var task = motion.MoveAsync(new DeskSettings(), 5437);
        await ble.Connecting.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await motion.StopAsync();
        ble.ConnectGate.SetResult();
        await task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Empty(ble.Targets);
    }

    [Fact]
    public async Task RepeatedActionDoesNotQueueAnotherMove()
    {
        var ble = new FakeBle { ConnectGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously) };
        var motion = new MotionController(ble, FastOptions());
        var first = motion.MoveAsync(new DeskSettings(), 5437);
        await ble.Connecting.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await motion.MoveAsync(new DeskSettings(), 5437);
        await motion.StopAsync();
        ble.ConnectGate.SetResult();
        await first;
        Assert.Equal(1, ble.Connects);
        Assert.Empty(ble.Targets);
    }

    [Fact]
    public async Task StationaryFarFromTargetFaultsRatherThanClaimingArrival()
    {
        var ble = new FakeBle { Raw = 1162, Step = 0 };
        var motion = new MotionController(ble, FastOptions());
        await motion.MoveAsync(new DeskSettings(), 5437);
        Assert.Contains("progress", motion.State.Status, StringComparison.OrdinalIgnoreCase);
        Assert.True(ble.Stops > 0);
    }

    [Fact]
    public async Task ProtocolErrorHaltsFutureRefreshes()
    {
        var ble = new FakeBle { Raw = 1162, Step = 0, ErrorAfterWrite = true };
        var motion = new MotionController(ble, FastOptions());
        await motion.MoveAsync(new DeskSettings(), 5437);
        Assert.Single(ble.Targets);
        Assert.Contains("collision", motion.State.Status);
    }

    [Fact]
    public async Task CalibrationMismatchBlocksMotion()
    {
        var ble = new FakeBle { Base = 6200 };
        var motion = new MotionController(ble, FastOptions());
        await motion.MoveAsync(new DeskSettings(), 5437);
        Assert.Empty(ble.Targets);
        Assert.Contains("calibration", motion.State.Status, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StaleFeedbackBlocksMotion()
    {
        var ble = new FakeBle { ReadingAge = TimeSpan.FromSeconds(5) };
        using var motion = new MotionController(ble, FastOptions());
        await motion.MoveAsync(new DeskSettings(), 5437);
        Assert.Empty(ble.Targets);
        Assert.Contains("stale", motion.State.Status);
    }

    [Fact]
    public async Task StopDuringInFlightWriteCannotScheduleAnotherTarget()
    {
        var ble = new FakeBle { WriteGate = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        using var motion = new MotionController(ble, FastOptions());
        var move = motion.MoveAsync(new DeskSettings(), 5437);
        await ble.Writing.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await motion.StopAsync();
        ble.WriteGate.SetResult();
        await move.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Single(ble.Targets);
        Assert.False(motion.State.Busy);
    }

    [Fact]
    public async Task FailedWriteAbortsWithoutRetry()
    {
        var ble = new FakeBle { FailWrite = true };
        using var motion = new MotionController(ble, FastOptions());
        await motion.MoveAsync(new DeskSettings(), 5437);
        Assert.Single(ble.Targets);
        Assert.Contains("write failure", motion.State.Status);
        Assert.True(ble.Stops > 0);
    }

    [Fact]
    public async Task WrongDirectionFeedbackStopsBeforeNextTargetWrite()
    {
        var ble = new FakeBle { WriteSpeed = -1000, Step = 0 };
        using var motion = new MotionController(ble, FastOptions());
        await motion.MoveAsync(new DeskSettings(), 5437);
        Assert.Single(ble.Targets);
        Assert.Contains("wrong direction", motion.State.Status);
    }

    private static MotionOptions FastOptions() => new() { RefreshPeriod = TimeSpan.FromMilliseconds(1), ProgressTimeout = TimeSpan.FromMilliseconds(30), ArrivalToleranceRaw = 10 };

    [Fact]
    public async Task ConcurrentFaultsAndCompletionNeverThrowFromCallbacks()
    {
        for (var round = 0; round < 10; round++)
        {
            var ble = new FakeBle { Raw = 1162, Step = 50 };
            using var motion = new MotionController(ble, FastOptions());
            var move = motion.MoveAsync(new DeskSettings(), 1212);
            var faults = Task.Run(() => { for (var i = 0; i < 50; i++) ble.RaiseFault(); });
            await Task.WhenAll(move, faults);
        }
    }
}

internal sealed class FakeBle : IBleTransport
{
    public int Raw { get; set; } = 1162;
    public int Base { get; set; } = 6250;
    public int Step { get; set; }
    public int Stops { get; private set; }
    public int Connects { get; private set; }
    public bool ErrorAfterWrite { get; set; }
    public TaskCompletionSource? ConnectGate { get; set; }
    public TaskCompletionSource? WriteGate { get; set; }
    public TaskCompletionSource Writing { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TimeSpan ReadingAge { get; set; }
    public bool FailWrite { get; set; }
    public short WriteSpeed { get; set; }
    private short _speed;
    public TaskCompletionSource Connecting { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public List<int> Targets { get; } = [];
    public bool IsConnected { get; private set; }
    public event Action<string>? Faulted;
    public void RaiseFault() => Faulted?.Invoke("injected fault");
    public async Task ConnectAsync(string address, CancellationToken ct)
    {
        Connects++;
        Connecting.TrySetResult();
        if (ConnectGate is not null) await ConnectGate.Task; // Deliberately ignores cancellation: late completion.
        IsConnected = true;
    }
    public Task<int> ReadBaseAsync(CancellationToken ct) => Task.FromResult(Base);
    public Task<DeskReading> ReadAsync(CancellationToken ct) => Task.FromResult(new DeskReading(Raw, _speed, DateTimeOffset.UtcNow - ReadingAge));
    public Task WakeAsync(CancellationToken ct) => Task.CompletedTask;
    public async Task WriteTargetAsync(int raw, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Targets.Add(raw);
        Writing.TrySetResult();
        if (WriteGate is not null) await WriteGate.Task;
        if (FailWrite) throw new IOException("injected write failure");
        _speed = WriteSpeed;
        Raw += Math.Sign(raw - Raw) * Math.Min(Step, Math.Abs(raw - Raw));
        if (ErrorAfterWrite) Faulted?.Invoke("collision");
    }
    public Task StopAsync(CancellationToken ct) { Stops++; _speed = 0; return Task.CompletedTask; }
    public Task DisconnectAsync() { IsConnected = false; return Task.CompletedTask; }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
