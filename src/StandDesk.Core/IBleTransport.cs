namespace StandDesk.Core;

public interface IBleTransport : IAsyncDisposable
{
    bool IsConnected { get; }
    event Action<string>? Faulted;
    Task ConnectAsync(string address, CancellationToken ct);
    Task<int> ReadBaseAsync(CancellationToken ct);
    Task<DeskReading> ReadAsync(CancellationToken ct);
    Task WakeAsync(CancellationToken ct);
    Task WriteTargetAsync(int raw, CancellationToken ct);
    Task StopAsync(CancellationToken ct);
    Task DisconnectAsync();
}
