using System.Runtime.InteropServices.WindowsRuntime;
using ArmControlSuite.Models;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;

namespace ArmControlSuite.Services;

/// <summary>
/// 基于 WinRT BluetoothLEDevice / GATT 的低功耗蓝牙 (BLE) 通信传输实现。
/// </summary>
public sealed class BleTransport : ITransport
{
    private BluetoothLEDevice? _device;
    private readonly List<GattDeviceService> _services = [];
    private GattCharacteristic? _writeChar;
    private GattCharacteristic? _notifyChar;
    private GattSession? _gattSession;
    private bool _isDisconnecting;
    private int _disposed;

    public TransportKind Kind => TransportKind.Ble;
    public bool IsConnected => _device is { ConnectionStatus: BluetoothConnectionStatus.Connected } && _writeChar != null && _notifyChar != null;
    public string ConnectionDetail { get; private set; } = string.Empty;

    public event Action<byte[]>? DataReceived;
    public event Action<string>? Disconnected;
    public event Action<string>? LogMessage;

    /// <summary>
    /// 手动选择 BLE 透传特性委托：(可写列表, 可通知列表, 历史TX UUID, 历史RX UUID) -> (TX, RX)。
    /// </summary>
    public delegate Task<(BleCharacteristicItem? WriteChar, BleCharacteristicItem? NotifyChar)> CharSelectionCallback(
        IReadOnlyList<BleCharacteristicItem> writeChars,
        IReadOnlyList<BleCharacteristicItem> notifyChars,
        string? defaultWriteUuid,
        string? defaultNotifyUuid);

    public async Task<(bool Connected, BleSavedChars? NewlyBoundChars)> ConnectAsync(
        string formattedMac,
        ulong rawBluetoothAddress,
        string deviceLabel,
        string presetName,
        BleSavedChars? savedChars,
        CharSelectionCallback selectCharsCallback,
        CancellationToken cancellationToken = default)
    {
        _isDisconnecting = false;

        if (rawBluetoothAddress == 0)
            rawBluetoothAddress = RfcommWin32Socket.ParseBluetoothAddress(formattedMac);

        var device = await BluetoothLEDevice.FromBluetoothAddressAsync(rawBluetoothAddress).AsTask(cancellationToken);
        if (device == null)
            throw new InvalidOperationException($"无法找到地址为 {formattedMac} 的 BLE 设备");

        _device = device;
        device.ConnectionStatusChanged += OnDeviceConnectionStatusChanged;

        try
        {
            _gattSession = await GattSession.FromDeviceIdAsync(device.BluetoothDeviceId).AsTask(cancellationToken);
            if (_gattSession != null)
                _gattSession.MaintainConnection = true;
        }
        catch
        {
            // GattSession 非强制
        }

        var servicesResult = await device.GetGattServicesAsync(BluetoothCacheMode.Uncached).AsTask(cancellationToken);
        if (servicesResult.Status != GattCommunicationStatus.Success || servicesResult.Services.Count == 0)
        {
            servicesResult = await device.GetGattServicesAsync(BluetoothCacheMode.Cached).AsTask(cancellationToken);
        }

        if (servicesResult.Status != GattCommunicationStatus.Success)
            throw new InvalidOperationException($"发现 GATT 服务失败 (状态: {servicesResult.Status})");

        var charByUuid = new Dictionary<string, GattCharacteristic>(StringComparer.OrdinalIgnoreCase);
        var writeItems = new List<BleCharacteristicItem>();
        var notifyItems = new List<BleCharacteristicItem>();

        foreach (var service in servicesResult.Services)
        {
            _services.Add(service);
            var charResult = await service.GetCharacteristicsAsync(BluetoothCacheMode.Uncached).AsTask(cancellationToken);
            if (charResult.Status != GattCommunicationStatus.Success)
                charResult = await service.GetCharacteristicsAsync(BluetoothCacheMode.Cached).AsTask(cancellationToken);

            if (charResult.Status != GattCommunicationStatus.Success)
                continue;

            foreach (var ch in charResult.Characteristics)
            {
                string uuidStr = ch.Uuid.ToString();
                charByUuid.TryAdd(uuidStr, ch);

                var props = ch.CharacteristicProperties;
                bool canWrite = props.HasFlag(GattCharacteristicProperties.Write);
                bool canWriteNoResp = props.HasFlag(GattCharacteristicProperties.WriteWithoutResponse);
                bool canNotify = props.HasFlag(GattCharacteristicProperties.Notify);
                bool canIndicate = props.HasFlag(GattCharacteristicProperties.Indicate);

                string propsText = FormatProperties(props);
                var item = new BleCharacteristicItem(
                    uuidStr,
                    service.Uuid.ToString(),
                    propsText,
                    canWrite,
                    canWriteNoResp,
                    canNotify,
                    canIndicate);

                if (canWrite || canWriteNoResp)
                    writeItems.Add(item);
                if (canNotify || canIndicate)
                    notifyItems.Add(item);
            }
        }

        GattCharacteristic? selectedWrite = null;
        GattCharacteristic? selectedNotify = null;
        BleSavedChars? newlyBound = null;

        // 1) 优先按模块预设 UUID 自动匹配
        if (FirmwareConstants.BlePresets.TryGetValue(presetName, out var preset)
            && !string.IsNullOrEmpty(preset.WriteUuid)
            && !string.IsNullOrEmpty(preset.NotifyUuid))
        {
            selectedWrite = FindCharacteristic(charByUuid, preset.WriteUuid);
            selectedNotify = FindCharacteristic(charByUuid, preset.NotifyUuid);
            if (selectedWrite != null && selectedNotify != null)
            {
                LogMessage?.Invoke($"已按模块预设 [{presetName}] 自动匹配透传特性");
            }
            else
            {
                selectedWrite = null;
                selectedNotify = null;
                LogMessage?.Invoke($"当前设备未找到预设 [{presetName}] 对应特性，切换至自定义/历史匹配...");
            }
        }

        // 2) 非「自定义 (手动选择)」时复用历史绑定特性
        if ((selectedWrite == null || selectedNotify == null)
            && presetName != "自定义 (手动选择)"
            && savedChars != null
            && !string.IsNullOrEmpty(savedChars.WriteUuid)
            && !string.IsNullOrEmpty(savedChars.NotifyUuid))
        {
            var wc = FindCharacteristic(charByUuid, savedChars.WriteUuid);
            var nc = FindCharacteristic(charByUuid, savedChars.NotifyUuid);
            if (wc != null && nc != null)
            {
                selectedWrite = wc;
                selectedNotify = nc;
                LogMessage?.Invoke("已自动复用该设备上次绑定的透传特性");
            }
        }

        // 3) 弹出手动特性选择窗口
        if (selectedWrite == null || selectedNotify == null)
        {
            if (writeItems.Count == 0 || notifyItems.Count == 0)
                throw new InvalidOperationException("未在设备中发现同时支持写入与通知的 GATT 透传特性");

            LogMessage?.Invoke("请在弹窗中手动选择蓝牙透传 TX (写入) 与 RX (通知) 特性...");
            var (wItem, nItem) = await selectCharsCallback(
                writeItems,
                notifyItems,
                savedChars?.WriteUuid,
                savedChars?.NotifyUuid);

            if (wItem == null || nItem == null)
            {
                LogMessage?.Invoke("已取消蓝牙特性配置，正在断开连接...");
                await DisconnectAsync().ConfigureAwait(false);
                return (false, null);
            }

            selectedWrite = FindCharacteristic(charByUuid, wItem.Uuid);
            selectedNotify = FindCharacteristic(charByUuid, nItem.Uuid);
            if (selectedWrite == null || selectedNotify == null)
                throw new InvalidOperationException("所选 GATT 特性无效");

            newlyBound = new BleSavedChars
            {
                WriteUuid = selectedWrite.Uuid.ToString(),
                NotifyUuid = selectedNotify.Uuid.ToString()
            };
        }

        _writeChar = selectedWrite;
        _notifyChar = selectedNotify;
        selectedNotify.ValueChanged += OnCharacteristicValueChanged;

        var cccdValue = selectedNotify.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Notify)
            ? GattClientCharacteristicConfigurationDescriptorValue.Notify
            : GattClientCharacteristicConfigurationDescriptorValue.Indicate;

        var cccdStatus = await selectedNotify.WriteClientCharacteristicConfigurationDescriptorAsync(cccdValue)
            .AsTask(cancellationToken);

        if (cccdStatus != GattCommunicationStatus.Success)
        {
            selectedNotify.ValueChanged -= OnCharacteristicValueChanged;
            _writeChar = null;
            _notifyChar = null;
            throw new InvalidOperationException($"蓝牙订阅通知失败 (状态: {cccdStatus})");
        }

        string resolvedName = !string.IsNullOrWhiteSpace(device.Name) ? device.Name : deviceLabel;
        ConnectionDetail = !string.IsNullOrWhiteSpace(resolvedName) ? $"({resolvedName})" : formattedMac;
        return (true, newlyBound);
    }

    public async Task SendAsync(byte[] payload, CancellationToken cancellationToken = default)
    {
        if (payload.Length == 0)
            return;

        var writeChar = _writeChar;
        if (writeChar == null)
            throw new InvalidOperationException("BLE 写入特性未就绪");

        var props = writeChar.CharacteristicProperties;
        var writeOption = props.HasFlag(GattCharacteristicProperties.Write)
            ? GattWriteOption.WriteWithResponse
            : GattWriteOption.WriteWithoutResponse;

        int maxChunk = _gattSession != null && _gattSession.MaxPduSize > 3
            ? Math.Max(20, _gattSession.MaxPduSize - 3)
            : 20;

        for (int offset = 0; offset < payload.Length; offset += maxChunk)
        {
            int count = Math.Min(maxChunk, payload.Length - offset);
            byte[] slice = new byte[count];
            Buffer.BlockCopy(payload, offset, slice, 0, count);
            var status = await writeChar.WriteValueAsync(slice.AsBuffer(), writeOption).AsTask(cancellationToken);
            if (status != GattCommunicationStatus.Success)
            {
                throw new InvalidOperationException($"BLE 写入失败 (状态: {status})");
            }
        }
    }

    private void OnCharacteristicValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
    {
        if (args.CharacteristicValue == null || args.CharacteristicValue.Length == 0)
            return;
        byte[] bytes = args.CharacteristicValue.ToArray();
        DataReceived?.Invoke(bytes);
    }

    private void OnDeviceConnectionStatusChanged(BluetoothLEDevice sender, object args)
    {
        if (_isDisconnecting)
            return;
        if (sender.ConnectionStatus == BluetoothConnectionStatus.Disconnected)
        {
            Disconnected?.Invoke("蓝牙设备连接已中断");
        }
    }

    private static GattCharacteristic? FindCharacteristic(
        Dictionary<string, GattCharacteristic> map,
        string targetUuid)
    {
        foreach (var kv in map)
        {
            if (FirmwareConstants.IsSameUuid(kv.Key, targetUuid))
                return kv.Value;
        }
        return null;
    }

    private static string FormatProperties(GattCharacteristicProperties props)
    {
        List<string> list = [];
        if (props.HasFlag(GattCharacteristicProperties.Read)) list.Add("read");
        if (props.HasFlag(GattCharacteristicProperties.Write)) list.Add("write");
        if (props.HasFlag(GattCharacteristicProperties.WriteWithoutResponse)) list.Add("write-without-response");
        if (props.HasFlag(GattCharacteristicProperties.Notify)) list.Add("notify");
        if (props.HasFlag(GattCharacteristicProperties.Indicate)) list.Add("indicate");
        return string.Join(", ", list);
    }

    public async Task DisconnectAsync()
    {
        _isDisconnecting = true;

        var notifyChar = Interlocked.Exchange(ref _notifyChar, null);
        if (notifyChar != null)
        {
            notifyChar.ValueChanged -= OnCharacteristicValueChanged;
            try
            {
                await notifyChar.WriteClientCharacteristicConfigurationDescriptorAsync(
                    GattClientCharacteristicConfigurationDescriptorValue.None);
            }
            catch { }
        }

        Interlocked.Exchange(ref _writeChar, null);

        foreach (var svc in _services)
        {
            try { svc.Dispose(); } catch { }
        }
        _services.Clear();

        var session = Interlocked.Exchange(ref _gattSession, null);
        session?.Dispose();

        var device = Interlocked.Exchange(ref _device, null);
        if (device != null)
        {
            device.ConnectionStatusChanged -= OnDeviceConnectionStatusChanged;
            device.Dispose();
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
