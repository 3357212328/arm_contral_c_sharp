namespace ArmControlSuite.Services;

public enum TransportKind
{
    Serial,
    Ble,
    Spp
}

public interface ITransport : IAsyncDisposable
{
    TransportKind Kind { get; }
    bool IsConnected { get; }
    string ConnectionDetail { get; }

    event Action<byte[]>? DataReceived;
    event Action<string>? Disconnected;
    event Action<string>? LogMessage;

    Task SendAsync(byte[] payload, CancellationToken cancellationToken = default);
    Task DisconnectAsync();
}
