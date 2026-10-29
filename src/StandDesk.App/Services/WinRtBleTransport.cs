using System.Globalization;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;
using StandDesk.Core;

namespace StandDesk.App.Services;

public sealed class WinRtBleTransport : IBleTransport
{
    private readonly SemaphoreSlim _io = new(1, 1);
    private BluetoothLEDevice? _device;
    private readonly List<GattDeviceService> _services = [];
    private GattCharacteristic? _height, _target, _control, _dpg, _error;
    public bool IsConnected => _device?.ConnectionStatus == BluetoothConnectionStatus.Connected && _height is not null;
    public event Action<string>? Faulted;
    public event Action<string>? Trace;

    private static Guid Id(string suffix) => Guid.Parse($"99fa{suffix}-338a-1024-8a49-009c0215f78a");
    private static void Check(GattCommunicationStatus status)
    {
        if (status != GattCommunicationStatus.Success) throw new IOException($"Bluetooth: {status}. Check Bluetooth, pairing, and DESK 2079.");
    }

    private async Task<T> LockedAsync<T>(Func<Task<T>> work, CancellationToken ct)
    {
        await _io.WaitAsync(ct);
        try { ct.ThrowIfCancellationRequested(); return await work(); }
        finally { _io.Release(); }
    }

    public async Task ConnectAsync(string address, CancellationToken ct)
    {
        await LockedAsync(async () =>
        {
            if (IsConnected) return true;
            Cleanup();
            var numeric = ulong.Parse(address.Replace(":", ""), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            try
            {
                _device = await BluetoothLEDevice.FromBluetoothAddressAsync(numeric).AsTask(ct)
                    ?? throw new IOException("DESK 2079 is unavailable. Enable Bluetooth and pair the desk in Windows.");
                ct.ThrowIfCancellationRequested();
                if (!string.Equals(_device.Name, "Desk 2079", StringComparison.OrdinalIgnoreCase)) throw new IOException("Connected device name does not match DESK 2079.");
                _device.ConnectionStatusChanged += ConnectionChanged;
                var result = await _device.GetGattServicesAsync(BluetoothCacheMode.Uncached).AsTask(ct);
                Check(result.Status);
                _services.AddRange(result.Services);
                foreach (var service in _services.Where(s => new[] { Id("0001"), Id("0010"), Id("0020"), Id("0030") }.Contains(s.Uuid)))
                {
                    var chars = await service.GetCharacteristicsAsync(BluetoothCacheMode.Uncached).AsTask(ct);
                    Check(chars.Status);
                    foreach (var item in chars.Characteristics)
                    {
                        if (item.Uuid == Id("0021")) _height = item;
                        if (item.Uuid == Id("0031")) _target = item;
                        if (item.Uuid == Id("0002")) _control = item;
                        if (item.Uuid == Id("0011")) _dpg = item;
                        if (item.Uuid == Id("0003")) _error = item;
                    }
                }
                if (_height is null || _target is null || _control is null || _dpg is null || _error is null)
                    throw new IOException("Desk is missing a required Bluetooth control interface.");
                _error.ValueChanged += ErrorChanged;
                Check(await _error.WriteClientCharacteristicConfigurationDescriptorAsync(GattClientCharacteristicConfigurationDescriptorValue.Notify).AsTask(ct));
                ct.ThrowIfCancellationRequested();
                return true;
            }
            catch { Cleanup(); throw; }
        }, ct);
    }

    private void ConnectionChanged(BluetoothLEDevice sender, object args)
    {
        if (sender.ConnectionStatus == BluetoothConnectionStatus.Disconnected) Faulted?.Invoke("Desk disconnected. Movement canceled.");
    }

    private static byte[] Bytes(IBuffer buffer)
    {
        using var reader = DataReader.FromBuffer(buffer);
        var bytes = new byte[buffer.Length];
        reader.ReadBytes(bytes);
        return bytes;
    }

    private void ErrorChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
    {
        var bytes = Bytes(args.CharacteristicValue);
        Trace?.Invoke("status " + Convert.ToHexString(bytes));
        // Idle can emit an empty status. Any nonzero status stops the operation; never retry it.
        if (bytes.Any(b => b != 0)) Faulted?.Invoke($"Desk interruption/error ({Convert.ToHexString(bytes)}). Check the physical paddle and desk.");
    }

    private async Task WriteAsync(GattCharacteristic characteristic, byte[] bytes, CancellationToken ct)
    {
        using var writer = new DataWriter();
        writer.WriteBytes(bytes);
        var option = characteristic.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Write)
            ? GattWriteOption.WriteWithResponse : GattWriteOption.WriteWithoutResponse;
        Trace?.Invoke("write " + characteristic.Uuid.ToString()[..8] + " " + Convert.ToHexString(bytes));
        Check(await characteristic.WriteValueAsync(writer.DetachBuffer(), option).AsTask(ct));
    }

    public Task<DeskReading> ReadAsync(CancellationToken ct) => LockedAsync(async () =>
    {
        var result = await (_height ?? throw new IOException("Desk is disconnected.")).ReadValueAsync(BluetoothCacheMode.Uncached).AsTask(ct);
        Check(result.Status);
        var bytes = Bytes(result.Value);
        Trace?.Invoke("height " + Convert.ToHexString(bytes));
        return DeskProtocol.Decode(bytes);
    }, ct);

    public Task<int> ReadBaseAsync(CancellationToken ct) => LockedAsync(async () =>
    {
        var characteristic = _dpg ?? throw new IOException("Desk is disconnected.");
        var response = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Handler(GattCharacteristic _, GattValueChangedEventArgs args) => response.TrySetResult(Bytes(args.CharacteristicValue));
        characteristic.ValueChanged += Handler;
        try
        {
            Check(await characteristic.WriteClientCharacteristicConfigurationDescriptorAsync(GattClientCharacteristicConfigurationDescriptorValue.Notify).AsTask(ct));
            await WriteAsync(characteristic, [0x7f, 0x81, 0], ct);
            var bytes = await response.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);
            if (bytes.Length != 5 || bytes[0] != 1 || bytes[1] != 3 || bytes[2] != 1) throw new IOException("Unexpected desk calibration response.");
            return bytes[3] | (bytes[4] << 8);
        }
        finally { characteristic.ValueChanged -= Handler; }
    }, ct);

    private async Task ControlAsync(byte command, CancellationToken ct) =>
        await LockedAsync(async () => { await WriteAsync(_control ?? throw new IOException("Desk is disconnected."), [command, 0], ct); return true; }, ct);

    public Task WakeAsync(CancellationToken ct) => ControlAsync(0xfe, ct);
    public async Task StopAsync(CancellationToken ct) => await LockedAsync(async () =>
    {
        // Reference-input travel has its own abort command; command-channel Stop alone does not cancel it.
        await WriteAsync(_target ?? throw new IOException("Desk is disconnected."), [0x01, 0x80], ct);
        await WriteAsync(_control ?? throw new IOException("Desk is disconnected."), [0xff, 0], ct);
        return true;
    }, ct);
    public async Task WriteTargetAsync(int raw, CancellationToken ct) =>
        await LockedAsync(async () => { await WriteAsync(_target ?? throw new IOException("Desk is disconnected."), DeskProtocol.EncodeTarget(raw), ct); return true; }, ct);

    private void Cleanup()
    {
        if (_error is not null) _error.ValueChanged -= ErrorChanged;
        if (_device is not null) _device.ConnectionStatusChanged -= ConnectionChanged;
        _height = _target = _control = _dpg = _error = null;
        foreach (var service in _services) service.Dispose();
        _services.Clear();
        _device?.Dispose();
        _device = null;
    }
    public async Task DisconnectAsync()
    {
        await _io.WaitAsync();
        try { Cleanup(); } finally { _io.Release(); }
    }
    public async ValueTask DisposeAsync() { await DisconnectAsync(); _io.Dispose(); }
}
