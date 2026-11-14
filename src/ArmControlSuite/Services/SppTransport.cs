namespace ArmControlSuite.Services;

/// <summary>
/// 经典蓝牙 SPP (RFCOMM) 通信传输实现。
/// </summary>
public sealed class SppTransport : ITransport
{
    private RfcommWin32Socket? _socket;
    private CancellationTokenSource? _cts;
    private Task? _readTask;
    private readonly object _sendLock = new();
    private int _disposed;

    public TransportKind Kind => TransportKind.Spp;
    public bool IsConnected => _socket is { IsConnected: true };
    public string ConnectionDetail { get; private set; } = string.Empty;

    public event Action<byte[]>? DataReceived;
    public event Action<string>? Disconnected;
    public event Action<string>? LogMessage;

    public async Task ConnectAsync(string macAddress, int channel, CancellationToken cancellationToken = default)
    {
        var sock = new RfcommWin32Socket();
        try
        {
            await Task.Run(() => sock.Connect(macAddress, channel, timeoutMs: 6000), cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            sock.Dispose();
            throw;
        }

        _socket = sock;
        ConnectionDetail = $"{macAddress}:{channel}";
        _cts = new CancellationTokenSource();
        _readTask = Task.Factory.StartNew(
            () => ReadLoop(_cts.Token),
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
    }

    public Task SendAsync(byte[] payload, CancellationToken cancellationToken = default)
    {
        if (payload.Length == 0)
            return Task.CompletedTask;

        var sock = _socket;
        if (sock is not { IsConnected: true })
            throw new InvalidOperationException("经典蓝牙 SPP 未连接");

        lock (_sendLock)
        {
            sock.Send(payload);
        }
        return Task.CompletedTask;
    }

    private void ReadLoop(CancellationToken ct)
    {
        byte[] buffer = new byte[1024];
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var sock = _socket;
                if (sock is not { IsConnected: true })
                    break;

                int n = sock.ReceiveWithTimeout(buffer, timeoutMs: 250);
                if (n < 0)
                {
                    if (!ct.IsCancellationRequested)
                    {
                        LogMessage?.Invoke("SPP 链路已由对端关闭");
                        Disconnected?.Invoke("SPP 链路已断开");
                    }
                    break;
                }

                if (n > 0)
                {
                    byte[] copy = new byte[n];
                    Buffer.BlockCopy(buffer, 0, copy, 0, n);
                    DataReceived?.Invoke(copy);
                }
            }
        }
        catch (Exception ex)
        {
            if (!ct.IsCancellationRequested)
            {
                LogMessage?.Invoke($"SPP 读取异常: {ex.Message}");
                Disconnected?.Invoke($"SPP 链路已断开: {ex.Message}");
            }
        }
    }

    public async Task DisconnectAsync()
    {
        var cts = Interlocked.Exchange(ref _cts, null);
        if (cts != null)
        {
            try { await cts.CancelAsync().ConfigureAwait(false); } catch { }
        }

        var sock = Interlocked.Exchange(ref _socket, null);
        sock?.Dispose();

        var readTask = Interlocked.Exchange(ref _readTask, null);
        if (readTask != null)
        {
            try { await readTask.ConfigureAwait(false); } catch { }
        }

        cts?.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            await DisconnectAsync().ConfigureAwait(false);
        }
    }
}
