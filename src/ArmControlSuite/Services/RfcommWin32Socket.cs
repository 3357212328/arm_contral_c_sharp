using System.IO;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace ArmControlSuite.Services;

/// <summary>
/// 基于 Win32 Winsock2 (AF_BTH / BTHPROTO_RFCOMM) 的经典蓝牙 SPP 原生 Socket 封装。
/// </summary>
public sealed partial class RfcommWin32Socket : IDisposable
{
    private const ushort AfBth = 32;
    private const int SockStream = 1;
    private const int BthProtoRfcomm = 3;
    private const uint FionBio = 0x8004667E;
    private const int WsaWouldBlock = 10035;
    private const int WsaInProgress = 10036;
    private const int SolSocket = 0xFFFF;
    private const int SoError = 0x1007;

    private static readonly IntPtr InvalidSocket = new(-1);
    private static readonly object WsaInitLock = new();
    private static bool _wsaInitialized;

    private IntPtr _socketHandle = InvalidSocket;
    private int _disposed;

    public bool IsConnected => _socketHandle != InvalidSocket && Volatile.Read(ref _disposed) == 0;

    [GeneratedRegex("^([0-9A-F]{2}:){5}[0-9A-F]{2}$", RegexOptions.Compiled)]
    private static partial Regex MacRegex();

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct SockAddrBth
    {
        public ushort AddressFamily;
        public ulong BtAddr;
        public Guid ServiceClassId;
        public uint Port;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct FdSet
    {
        public uint FdCount;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)]
        public IntPtr[] FdArray;

        public static FdSet FromSocket(IntPtr socket)
        {
            var array = new IntPtr[64];
            array[0] = socket;
            return new FdSet { FdCount = 1, FdArray = array };
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct TimeVal
    {
        public int TvSec;
        public int TvUsec;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    private struct WsaData
    {
        public ushort wVersion;
        public ushort wHighVersion;
        public ushort iMaxSockets;
        public ushort iMaxUdpDg;
        public IntPtr lpVendorInfo;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 257)]
        public string szDescription;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 129)]
        public string szSystemStatus;
    }

    [DllImport("ws2_32.dll", SetLastError = true)]
    private static extern int WSAStartup(ushort wVersionRequested, out WsaData lpWSAData);

    [DllImport("ws2_32.dll", SetLastError = true)]
    private static extern int WSAGetLastError();

    [DllImport("ws2_32.dll", SetLastError = true)]
    private static extern IntPtr socket(int af, int type, int protocol);

    [DllImport("ws2_32.dll", SetLastError = true)]
    private static extern int ioctlsocket(IntPtr s, uint cmd, ref uint argp);

    [DllImport("ws2_32.dll", SetLastError = true)]
    private static extern int connect(IntPtr s, ref SockAddrBth name, int namelen);

    [DllImport("ws2_32.dll", SetLastError = true)]
    private static extern int select(
        int nfds,
        ref FdSet readfds,
        ref FdSet writefds,
        ref FdSet exceptfds,
        ref TimeVal timeout);

    [DllImport("ws2_32.dll", SetLastError = true)]
    private static extern int select(
        int nfds,
        IntPtr readfds,
        ref FdSet writefds,
        ref FdSet exceptfds,
        ref TimeVal timeout);

    [DllImport("ws2_32.dll", SetLastError = true)]
    private static extern int select(
        int nfds,
        ref FdSet readfds,
        IntPtr writefds,
        IntPtr exceptfds,
        ref TimeVal timeout);

    [DllImport("ws2_32.dll", SetLastError = true)]
    private static extern int getsockopt(IntPtr s, int level, int optname, out int optval, ref int optlen);

    [DllImport("ws2_32.dll", SetLastError = true)]
    private static extern int send(IntPtr s, byte[] buf, int len, int flags);

    [DllImport("ws2_32.dll", SetLastError = true)]
    private static extern int recv(IntPtr s, byte[] buf, int len, int flags);

    [DllImport("ws2_32.dll", SetLastError = true)]
    private static extern int closesocket(IntPtr s);

    /// <summary>
    /// 规范化蓝牙 MAC 地址 (支持冒号或短横线分隔)，校验不通过返回 null。
    /// </summary>
    public static string? NormalizeMacAddress(string? rawAddress)
    {
        if (string.IsNullOrWhiteSpace(rawAddress))
            return null;
        string normalized = rawAddress.Trim().Replace('-', ':').ToUpperInvariant();
        return MacRegex().IsMatch(normalized) ? normalized : null;
    }

    /// <summary>
    /// 将标准 AA:BB:CC:DD:EE:FF 蓝牙地址解析为 Windows BTH_ADDR (ulong)。
    /// </summary>
    public static ulong ParseBluetoothAddress(string macAddress)
    {
        string? normalized = NormalizeMacAddress(macAddress);
        if (normalized == null)
            throw new FormatException($"无效的蓝牙 MAC 地址: {macAddress}");
        string hex = normalized.Replace(":", string.Empty);
        return Convert.ToUInt64(hex, 16);
    }

    private static void EnsureWsaStarted()
    {
        if (_wsaInitialized)
            return;
        lock (WsaInitLock)
        {
            if (_wsaInitialized)
                return;
            int res = WSAStartup(0x0202, out _);
            if (res != 0)
                throw new SocketException(res);
            _wsaInitialized = true;
        }
    }

    public void Connect(string macAddress, int channel, int timeoutMs = 6000)
    {
        if (channel is < 1 or > 30)
            throw new ArgumentOutOfRangeException(nameof(channel), "RFCOMM 通道范围必须为 1~30");

        EnsureWsaStarted();
        ulong btAddr = ParseBluetoothAddress(macAddress);

        IntPtr handle = socket(AfBth, SockStream, BthProtoRfcomm);
        if (handle == InvalidSocket)
        {
            int err = WSAGetLastError();
            throw new SocketException(err);
        }

        try
        {
            // 使用非阻塞连接 + select 精确控制连接超时
            uint nonBlocking = 1;
            if (ioctlsocket(handle, FionBio, ref nonBlocking) != 0)
                throw new SocketException(WSAGetLastError());

            var addr = new SockAddrBth
            {
                AddressFamily = AfBth,
                BtAddr = btAddr,
                ServiceClassId = Guid.Empty,
                Port = (uint)channel
            };

            int addrSize = Marshal.SizeOf<SockAddrBth>();
            int connRes = connect(handle, ref addr, addrSize);
            if (connRes != 0)
            {
                int err = WSAGetLastError();
                if (err != WsaWouldBlock && err != WsaInProgress)
                    throw new SocketException(err);

                var writeSet = FdSet.FromSocket(handle);
                var exceptSet = FdSet.FromSocket(handle);
                var tv = new TimeVal
                {
                    TvSec = timeoutMs / 1000,
                    TvUsec = (timeoutMs % 1000) * 1000
                };

                int sel = select(0, IntPtr.Zero, ref writeSet, ref exceptSet, ref tv);
                if (sel == 0)
                    throw new TimeoutException($"连接蓝牙设备 {macAddress} (通道 {channel}) 超时");
                if (sel < 0)
                    throw new SocketException(WSAGetLastError());

                int soErr = 0;
                int optLen = sizeof(int);
                getsockopt(handle, SolSocket, SoError, out soErr, ref optLen);
                if (soErr != 0)
                    throw new SocketException(soErr);
                if (exceptSet.FdCount > 0)
                    throw new SocketException(10061); // WSAECONNREFUSED
            }

            _socketHandle = handle;
        }
        catch
        {
            closesocket(handle);
            throw;
        }
    }

    public void Send(byte[] data)
    {
        IntPtr handle = _socketHandle;
        if (handle == InvalidSocket)
            throw new InvalidOperationException("SPP Socket 未连接");

        int offset = 0;
        while (offset < data.Length)
        {
            byte[] slice = offset == 0 ? data : data[offset..];
            int sent = send(handle, slice, slice.Length, 0);
            if (sent < 0)
            {
                int err = WSAGetLastError();
                if (err == WsaWouldBlock)
                {
                    Thread.Sleep(5);
                    continue;
                }
                throw new SocketException(err);
            }
            if (sent == 0)
                throw new IOException("SPP 连接已关闭");
            offset += sent;
        }
    }

    /// <summary>
    /// 轮询等待接收数据；超时无数据返回 0，远端正常关闭返回 -1，收到数据返回字节数。
    /// </summary>
    public int ReceiveWithTimeout(byte[] buffer, int timeoutMs = 250)
    {
        IntPtr handle = _socketHandle;
        if (handle == InvalidSocket)
            return -1;

        var readSet = FdSet.FromSocket(handle);
        var tv = new TimeVal
        {
            TvSec = timeoutMs / 1000,
            TvUsec = (timeoutMs % 1000) * 1000
        };

        int sel = select(0, ref readSet, IntPtr.Zero, IntPtr.Zero, ref tv);
        if (sel == 0)
            return 0;
        if (sel < 0)
        {
            int err = WSAGetLastError();
            if (Volatile.Read(ref _disposed) == 1 || _socketHandle == InvalidSocket)
                return -1;
            throw new SocketException(err);
        }

        int received = recv(handle, buffer, buffer.Length, 0);
        if (received == 0)
            return -1; // 对端断开
        if (received < 0)
        {
            int err = WSAGetLastError();
            if (err == WsaWouldBlock)
                return 0;
            if (Volatile.Read(ref _disposed) == 1 || _socketHandle == InvalidSocket)
                return -1;
            throw new SocketException(err);
        }
        return received;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            IntPtr handle = Interlocked.Exchange(ref _socketHandle, InvalidSocket);
            if (handle != InvalidSocket)
            {
                try { closesocket(handle); } catch { }
            }
        }
    }
}
