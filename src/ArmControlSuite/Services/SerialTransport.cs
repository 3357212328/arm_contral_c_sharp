using System.IO.Ports;

namespace ArmControlSuite.Services;

/// <summary>
/// 基于 System.IO.Ports.SerialPort 的异步串口通信通道。
/// </summary>
public sealed class SerialTransport : ITransport
{
    private SerialPort? _serialPort;
    private CancellationTokenSource? _cts;
    private Task? _readTask;
    private int _disposed;

    public TransportKind Kind => TransportKind.Serial;
    public bool IsConnected => _serialPort is { IsOpen: true };
    public string ConnectionDetail { get; private set; } = string.Empty;

    public event Action<byte[]>? DataReceived;
    public event Action<string>? Disconnected;
    public event Action<string>? LogMessage;

    public Task ConnectAsync(string portName, int baudRate, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var port = new SerialPort(portName, baudRate)
        {
            ReadTimeout = 100,
            WriteTimeout = 1000
        };
        port.Open();

        _serialPort = port;
        ConnectionDetail = portName;
        _cts = new CancellationTokenSource();
        _readTask = Task.Run(() => ReadLoopAsync(_cts.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    public Task SendAsync(byte[] payload, CancellationToken cancellationToken = default)
    {
        if (payload.Length == 0)
            return Task.CompletedTask;

        var port = _serialPort;
        if (port is not { IsOpen: true })
            throw new InvalidOperationException("串口未连接");

        port.Write(payload, 0, payload.Length);
        return Task.CompletedTask;
    }

    private async Task ReadLoopAsync(CancellationToken ct)
    {
        byte[] chunkBuffer = new byte[4096];
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var port = _serialPort;
                if (port is not { IsOpen: true })
                    break;

                int waiting = port.BytesToRead;
                if (waiting > 0)
                {
                    int toRead = Math.Min(waiting, chunkBuffer.Length);
                    int read = port.Read(chunkBuffer, 0, toRead);
                    if (read > 0)
                    {
                        byte[] copy = new byte[read];
                        Buffer.BlockCopy(chunkBuffer, 0, copy, 0, read);
                        DataReceived?.Invoke(copy);
                    }
                }
                else
                {
                    await Task.Delay(30, ct).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 正常取消
        }
        catch (Exception ex)
        {
            if (!ct.IsCancellationRequested)
            {
                LogMessage?.Invoke($"串口读取异常: {ex.Message}");
                Disconnected?.Invoke($"串口连接异常断开: {ex.Message}");
            }
        }
    }

    public async Task DisconnectAsync()
    {
        var cts = Interlocked.Exchange(ref _cts, null);
        if (cts != null)
        {
            try { await cts.CancelAsync().ConfigureAwait(false); } catch { }
            cts.Dispose();
        }

        var port = Interlocked.Exchange(ref _serialPort, null);
        if (port != null)
        {
            try
            {
                if (port.IsOpen)
                    port.Close();
            }
            catch { }
            try { port.Dispose(); } catch { }
        }

        var readTask = Interlocked.Exchange(ref _readTask, null);
        if (readTask != null)
        {
            try { await readTask.ConfigureAwait(false); } catch { }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            await DisconnectAsync().ConfigureAwait(false);
        }
    }
}
