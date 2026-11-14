using System.IO.Ports;
using System.Management;
using System.Text.RegularExpressions;
using ArmControlSuite.Models;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Enumeration;

namespace ArmControlSuite.Services;

/// <summary>
/// 串口枚举、BLE 广播实时增量扫描、已配对经典蓝牙枚举服务。
/// </summary>
public sealed partial class DeviceDiscoveryService
{
    [GeneratedRegex(@"\((COM\d+)\)", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex ComPortInNameRegex();

    public static string FormatBluetoothMac(ulong rawAddress)
    {
        Span<byte> bytes = stackalloc byte[6];
        for (int i = 5; i >= 0; i--)
        {
            bytes[i] = (byte)(rawAddress & 0xFF);
            rawAddress >>= 8;
        }
        return $"{bytes[0]:X2}:{bytes[1]:X2}:{bytes[2]:X2}:{bytes[3]:X2}:{bytes[4]:X2}:{bytes[5]:X2}";
    }

    /// <summary>
    /// 枚举当前系统可用串口及 WMI 设备友好名称。
    /// </summary>
    public List<SerialPortItem> GetSerialPorts()
    {
        var descriptionMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name FROM Win32_PnPEntity WHERE Name LIKE '%(COM%'");
            foreach (var obj in searcher.Get())
            {
                string? name = obj["Name"]?.ToString();
                if (string.IsNullOrWhiteSpace(name))
                    continue;
                var match = ComPortInNameRegex().Match(name);
                if (match.Success)
                {
                    string port = match.Groups[1].Value.ToUpperInvariant();
                    string cleanDesc = name.Replace($"({port})", string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
                    descriptionMap.TryAdd(port, string.IsNullOrWhiteSpace(cleanDesc) ? name : cleanDesc);
                }
            }
        }
        catch
        {
            // WMI 失败时降级使用纯 SerialPort.GetPortNames
        }

        string[] portNames;
        try
        {
            portNames = SerialPort.GetPortNames();
        }
        catch
        {
            portNames = [];
        }

        return portNames
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => int.TryParse(p.AsSpan(3), out int n) ? n : int.MaxValue)
            .ThenBy(p => p, StringComparer.OrdinalIgnoreCase)
            .Select(p =>
            {
                string upper = p.ToUpperInvariant();
                descriptionMap.TryGetValue(upper, out string? desc);
                return new SerialPortItem(upper, desc ?? upper);
            })
            .ToList();
    }

    /// <summary>
    /// 增量扫描周围具名 BLE 设备 (6 秒超时或外部取消)。
    /// </summary>
    public async Task<int> ScanBleDevicesAsync(
        Action<BleDeviceItem> onDeviceDiscovered,
        TimeSpan? duration = null,
        CancellationToken cancellationToken = default)
    {
        TimeSpan scanTime = duration ?? TimeSpan.FromSeconds(6);
        var seenAddresses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var nameCache = new Dictionary<ulong, string>();
        int discoveredCount = 0;

        var watcher = new BluetoothLEAdvertisementWatcher
        {
            ScanningMode = BluetoothLEScanningMode.Active
        };

        void HandleAdvertisement(BluetoothLEAdvertisementWatcher _, BluetoothLEAdvertisementReceivedEventArgs args)
        {
            ulong rawAddr = args.BluetoothAddress;
            string mac = FormatBluetoothMac(rawAddr);
            string localName = args.Advertisement?.LocalName ?? string.Empty;

            if (!string.IsNullOrWhiteSpace(localName))
                nameCache[rawAddr] = localName;
            else if (nameCache.TryGetValue(rawAddr, out string? cached))
                localName = cached;

            if (string.IsNullOrWhiteSpace(localName))
                return;

            bool isNew;
            lock (seenAddresses)
            {
                isNew = seenAddresses.Add(mac);
            }

            if (isNew)
            {
                Interlocked.Increment(ref discoveredCount);
                onDeviceDiscovered(new BleDeviceItem(mac, localName, args.RawSignalStrengthInDBm, rawAddr));
            }
        }

        watcher.Received += HandleAdvertisement;
        try
        {
            watcher.Start();
            await Task.Delay(scanTime, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // 允许提前停止扫描
        }
        finally
        {
            watcher.Received -= HandleAdvertisement;
            try { watcher.Stop(); } catch { }
        }

        return discoveredCount;
    }

    /// <summary>
    /// 获取 Windows 中已配对的经典蓝牙设备 (用于快速填充 SPP MAC 地址)。
    /// </summary>
    public async Task<List<SppDeviceItem>> GetPairedClassicBluetoothDevicesAsync()
    {
        var result = new List<SppDeviceItem>();
        try
        {
            string selector = BluetoothDevice.GetDeviceSelectorFromPairingState(true);
            var devices = await DeviceInformation.FindAllAsync(selector);
            foreach (var info in devices)
            {
                try
                {
                    using var btDev = await BluetoothDevice.FromIdAsync(info.Id);
                    if (btDev == null)
                        continue;
                    string mac = FormatBluetoothMac(btDev.BluetoothAddress);
                    string name = string.IsNullOrWhiteSpace(btDev.Name) ? info.Name : btDev.Name;
                    result.Add(new SppDeviceItem(name, mac));
                }
                catch
                {
                    // 忽略单个无法打开的设备
                }
            }
        }
        catch
        {
            // 蓝牙适配器关闭或不可用时返回空列表
        }

        return result;
    }
}
