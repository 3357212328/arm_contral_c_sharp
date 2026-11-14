using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using ArmControlSuite.Models;
using ArmControlSuite.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArmControlSuite.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly SettingsService _settingsService;
    private readonly DeviceDiscoveryService _discoveryService;
    private readonly ProtocolParser _protocolParser;
    private readonly IUiService _uiService;

    private AppSettings _settings;
    private ArmState _armState;
    private ITransport? _activeTransport;
    private readonly SemaphoreSlim _sendGate = new(1, 1);

    private readonly List<string> _rawLogLines = [];
    private readonly List<string> _cmdHistory = [];
    private int _cmdHistoryIndex = -1;

    private int _toastSeq;
    private int _isShuttingDown;
    private bool _isInitializing = true;
    private bool _hasTelemetryPacket;

    // =================== 窗口与全局外观 ===================
    [ObservableProperty] private bool _isDarkMode;
    [ObservableProperty] private int _selectedTabIndex;
    [ObservableProperty] private double _windowWidth = FirmwareConstants.DefaultWindowWidth;
    [ObservableProperty] private double _windowHeight = FirmwareConstants.DefaultWindowHeight;

    // =================== 连接配置状态 ===================
    [ObservableProperty] private int _selectedConnectionMode; // 0=Serial, 1=BLE, 2=SPP
    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private bool _isConnecting;
    [ObservableProperty] private string _connectButtonText = "建立连接";
    [ObservableProperty] private string _connStatusText = "未连接设备";
    [ObservableProperty] private string _connDotColor = "#94A3B8";

    public bool IsSerialMode => SelectedConnectionMode == 0;
    public bool IsBleMode => SelectedConnectionMode == 1;
    public bool IsSppMode => SelectedConnectionMode == 2;
    public bool CanEditConnectionSettings => !IsConnected && !IsConnecting;
    public bool CanToggleConnection => !IsConnecting;

    public ObservableCollection<SerialPortItem> SerialPorts { get; } = [];
    [ObservableProperty] private SerialPortItem? _selectedSerialPort;
    public IReadOnlyList<string> BaudRates => FirmwareConstants.BaudRates;
    [ObservableProperty] private string _selectedBaudRate = "9600";

    public ObservableCollection<BleDeviceItem> BleDevices { get; } = [];
    [ObservableProperty] private BleDeviceItem? _selectedBleDevice;
    public IReadOnlyList<string> BlePresets => FirmwareConstants.BlePresetNames;
    [ObservableProperty] private string _selectedBlePreset = "JDY-29 (FFE0/FFE1)";
    [ObservableProperty] private bool _isBleScanning;
    [ObservableProperty] private string _bleScanButtonText = "扫描附近蓝牙设备";

    [ObservableProperty] private string _sppMacAddress = string.Empty;
    public IReadOnlyList<string> RfcommChannels => FirmwareConstants.RfcommChannels;
    [ObservableProperty] private string _selectedSppChannel = "1";
    public ObservableCollection<SppDeviceItem> PairedSppDevices { get; } = [];
    [ObservableProperty] private SppDeviceItem? _selectedPairedSppDevice;

    // =================== 实时遥测与同步开关 ===================
    [ObservableProperty] private bool _isSyncEnabled = true;
    [ObservableProperty] private string _syncHintText = "开启后调节舵机将实时下发指令";
    public bool ShowNonSyncControls => !IsSyncEnabled;

    [ObservableProperty] private string _sidebarTempText = "-- °C";
    [ObservableProperty] private string _sidebarHumiText = "-- %";
    [ObservableProperty] private string _modeDisplayText = "未连接";
    [ObservableProperty] private string _playbackStatusDisplayText = "已停止";
    [ObservableProperty] private int _playbackStatusKind; // 0=Stopped, 1=Playing, 2=Paused
    [ObservableProperty] private string _recordedStepsSidebarText = "0 步";

    // =================== 仪表盘 KPI 与 16 路矩阵 ===================
    [ObservableProperty] private string _dashModeText = "主菜单";
    [ObservableProperty] private string _dashStepsText = $"0 / {FirmwareConstants.MaxSteps} 步";
    [ObservableProperty] private string _dashTempText = "0 °C";
    [ObservableProperty] private string _dashHumiText = "0 %";
    [ObservableProperty] private string _dashTempSubText = $"报警上限: > {FirmwareConstants.DefaultTempThreshold} °C";
    [ObservableProperty] private string _dashHumiSubText = $"报警上限: > {FirmwareConstants.DefaultHumiThreshold} %";
    [ObservableProperty] private string _dashActiveCountText = "7 / 16 路";
    [ObservableProperty] private string _dashActiveSubText = "S1, S2, S3, S4, S5, S6, S7";
    [ObservableProperty] private string _dashFocusServoBadge = "固件焦点通道: S01";
    [ObservableProperty] private string _dashPoseMonoText = string.Empty;

    public ObservableCollection<ServoChannelViewModel> ServoChannels { get; } = [];
    public IReadOnlyList<ServoChannelViewModel> LeftColumnServos { get; }
    public IReadOnlyList<ServoChannelViewModel> RightColumnServos { get; }

    [ObservableProperty] private string _activeServoSummaryText = "活动通道: 7 / 16";

    // =================== 序列控制 ===================
    [ObservableProperty] private string _seqStatusText = "当前回放状态: 已停止";
    [ObservableProperty] private string _seqStepsBadgeText = $"已录制: 0 / {FirmwareConstants.MaxSteps} 步";
    [ObservableProperty] private double _currentSpeedMultiplier = 1.0;
    [ObservableProperty] private string _delaySecondsText = "1";
    [ObservableProperty] private string _insertPositionText = "1";

    public bool IsSpeed05x
    {
        get => Math.Abs(CurrentSpeedMultiplier - 0.5) < 0.01;
        set { }
    }

    public bool IsSpeed10x
    {
        get => Math.Abs(CurrentSpeedMultiplier - 1.0) < 0.01;
        set { }
    }

    public bool IsSpeed20x
    {
        get => Math.Abs(CurrentSpeedMultiplier - 2.0) < 0.01;
        set { }
    }

    // =================== 环境监控与阈值 ===================
    [ObservableProperty] private double _tempProgress;
    [ObservableProperty] private double _humiProgress;
    [ObservableProperty] private bool _isTempAlarm;
    [ObservableProperty] private bool _isHumiAlarm;
    [ObservableProperty] private bool _isAnyEnvAlarm;
    [ObservableProperty] private string _monitorAlertTitle = "环境状态正常";
    [ObservableProperty] private string _monitorAlertDesc = "当前温湿度传感器读数均处于设定安全阈值范围内。";
    [ObservableProperty] private string _tempThresholdText = FirmwareConstants.DefaultTempThreshold.ToString();
    [ObservableProperty] private string _humiThresholdText = FirmwareConstants.DefaultHumiThreshold.ToString();

    public string SettingsFilePath => _settingsService.SettingsFilePath;

    // =================== 通信日志面板 ===================
    [ObservableProperty] private double _logHeight = FirmwareConstants.DefaultLogHeight;
    [ObservableProperty] private bool _isLogCollapsed;
    [ObservableProperty] private bool _isAutoScroll = true;
    [ObservableProperty] private string _logCountText = "0 条";
    [ObservableProperty] private string _logPreviewText = "就绪 · 等待设备连接";
    [ObservableProperty] private bool _hasLogs;
    [ObservableProperty] private string _quickCommandText = string.Empty;

    public string CollapseButtonText => IsLogCollapsed ? "展开日志" : "收起日志";

    // =================== 右上角 Toast 通知 ===================
    [ObservableProperty] private bool _isToastVisible;
    [ObservableProperty] private string _toastMessage = string.Empty;
    [ObservableProperty] private bool _isToastError;

    public event Action<LogEntry>? LogAppended;
    public event Action? LogsCleared;

    public MainViewModel(
        SettingsService settingsService,
        DeviceDiscoveryService discoveryService,
        IUiService uiService)
    {
        _settingsService = settingsService;
        _discoveryService = discoveryService;
        _uiService = uiService;

        _settings = _settingsService.Load();
        ushort initialMask = (ushort)(_settings.ServosDefaultMask & 0xFFFF);
        _armState = new ArmState(initialMask);
        _protocolParser = new ProtocolParser(_armState);
        _protocolParser.LogMessage += msg => _uiService.InvokeOnUiThread(() => AppendLog(msg));
        _protocolParser.StateUpdated += snapshot => _uiService.InvokeOnUiThread(() => ApplyFirmwareState(snapshot));

        var leftList = new List<ServoChannelViewModel>(8);
        var rightList = new List<ServoChannelViewModel>(8);
        for (int i = 0; i < FirmwareConstants.MaxServos; i++)
        {
            bool enabled = ((initialMask >> i) & 1) == 1;
            var vm = new ServoChannelViewModel(i, enabled, OnServoAngleCommitted, OnServoEnabledToggled);
            ServoChannels.Add(vm);
            if (i < 8) leftList.Add(vm);
            else rightList.Add(vm);
        }
        LeftColumnServos = leftList;
        RightColumnServos = rightList;

        PopulateSerialPorts(notify: false);
        ApplySettingsToUi(_settings);
        RefreshAllUiFromState();

        _isInitializing = false;
    }

    // =================== 属性变更持久化 ===================
    partial void OnSelectedConnectionModeChanged(int value)
    {
        OnPropertyChanged(nameof(IsSerialMode));
        OnPropertyChanged(nameof(IsBleMode));
        OnPropertyChanged(nameof(IsSppMode));
        if (!_isInitializing)
        {
            _settings.ConnectionMode = value;
            _settingsService.Save(_settings);
        }
    }

    partial void OnSelectedTabIndexChanged(int value)
    {
        if (!_isInitializing && value >= 0 && value < 5)
        {
            _settings.LastTab = value;
            _settingsService.Save(_settings);
        }
    }

    partial void OnSelectedSerialPortChanged(SerialPortItem? value)
    {
        if (!_isInitializing && value != null)
        {
            _settings.SerialLastPort = value.PortName;
            _settingsService.Save(_settings);
        }
    }

    partial void OnSelectedBaudRateChanged(string value)
    {
        if (!_isInitializing && !string.IsNullOrWhiteSpace(value))
        {
            _settings.SerialLastBaud = value;
            _settingsService.Save(_settings);
        }
    }

    partial void OnSelectedBlePresetChanged(string value)
    {
        if (!_isInitializing && !string.IsNullOrWhiteSpace(value))
        {
            _settings.BlePreset = value;
            _settingsService.Save(_settings);
        }
    }

    partial void OnSelectedSppChannelChanged(string value)
    {
        if (!_isInitializing && !string.IsNullOrWhiteSpace(value))
        {
            _settings.SppLastPort = value;
            _settingsService.Save(_settings);
        }
    }

    partial void OnSelectedPairedSppDeviceChanged(SppDeviceItem? value)
    {
        if (value != null && !string.IsNullOrWhiteSpace(value.MacAddress))
        {
            SppMacAddress = value.MacAddress;
        }
    }

    partial void OnIsConnectedChanged(bool value)
    {
        OnPropertyChanged(nameof(CanEditConnectionSettings));
        OnPropertyChanged(nameof(CanToggleConnection));
    }

    partial void OnIsConnectingChanged(bool value)
    {
        OnPropertyChanged(nameof(CanEditConnectionSettings));
        OnPropertyChanged(nameof(CanToggleConnection));
    }

    partial void OnIsSyncEnabledChanged(bool value)
    {
        SyncHintText = value
            ? "开启后调节舵机将实时下发指令"
            : "非同步模式：请点击手动控制页【应用所有角度】";
        OnPropertyChanged(nameof(ShowNonSyncControls));

        if (_isInitializing)
            return;

        SendCommand(value ? "sync on" : "sync off");
        if (value)
        {
            RefreshAllUiFromState();
        }
        _settings.Sync = value;
        _settingsService.Save(_settings);
    }

    partial void OnIsLogCollapsedChanged(bool value)
    {
        OnPropertyChanged(nameof(CollapseButtonText));
        if (!_isInitializing)
        {
            _settings.LogCollapsed = value;
            _settingsService.Save(_settings);
        }
    }

    partial void OnIsAutoScrollChanged(bool value)
    {
        if (!_isInitializing)
        {
            _settings.LogAutoScroll = value;
            _settingsService.Save(_settings);
        }
    }

    partial void OnCurrentSpeedMultiplierChanged(double value)
    {
        OnPropertyChanged(nameof(IsSpeed05x));
        OnPropertyChanged(nameof(IsSpeed10x));
        OnPropertyChanged(nameof(IsSpeed20x));
    }

    // =================== 主题与窗口 ===================
    [RelayCommand]
    private void ToggleTheme()
    {
        IsDarkMode = !IsDarkMode;
        _uiService.ApplyTheme(IsDarkMode);
        _settings.DarkMode = IsDarkMode;
        _settingsService.Save(_settings);
    }

    public void SaveSppAddressOnBlur()
    {
        _settings.SppLastAddr = (SppMacAddress ?? string.Empty).Trim();
        _settingsService.Save(_settings);
    }

    public void SaveLogGeometry(double height, bool collapsed)
    {
        LogHeight = Math.Clamp((int)Math.Round(height), FirmwareConstants.MinLogHeight, 600);
        IsLogCollapsed = collapsed;
        _settings.LogHeight = (int)LogHeight;
        _settings.LogCollapsed = collapsed;
        _settingsService.Save(_settings);
    }

    // =================== 串口 / BLE / SPP 发现与连接 ===================
    [RelayCommand]
    private void RefreshSerialPorts() => PopulateSerialPorts(notify: true);

    private void PopulateSerialPorts(bool notify)
    {
        string? prev = SelectedSerialPort?.PortName;
        var ports = _discoveryService.GetSerialPorts();

        SerialPorts.Clear();
        if (ports.Count == 0)
        {
            var emptyItem = new SerialPortItem(string.Empty, "无可用串口");
            SerialPorts.Add(emptyItem);
            SelectedSerialPort = emptyItem;
        }
        else
        {
            foreach (var p in ports)
                SerialPorts.Add(p);

            SelectedSerialPort = SerialPorts.FirstOrDefault(p => string.Equals(p.PortName, prev, StringComparison.OrdinalIgnoreCase))
                ?? SerialPorts[0];
        }

        if (notify)
        {
            ShowToast($"已刷新串口列表，发现 {ports.Count} 个端口");
        }
    }

    [RelayCommand]
    private async Task ScanBleAsync()
    {
        if (IsBleScanning || IsConnected)
            return;

        BleDevices.Clear();
        SelectedBleDevice = null;
        IsBleScanning = true;
        BleScanButtonText = "正在扫描蓝牙...";
        AppendLog("正在扫描周围 BLE 蓝牙设备 (实时增量发现)...");

        try
        {
            int count = await _discoveryService.ScanBleDevicesAsync(device =>
            {
                _uiService.InvokeOnUiThread(() =>
                {
                    if (!BleDevices.Any(d => string.Equals(d.Address, device.Address, StringComparison.OrdinalIgnoreCase)))
                    {
                        BleDevices.Add(device);
                        SelectedBleDevice ??= device;
                    }
                });
            }, TimeSpan.FromSeconds(6));

            AppendLog($"蓝牙扫描完成，共发现 {count} 个具名 BLE 设备");
            ShowToast($"扫描完成，发现 {count} 个蓝牙设备");
        }
        catch (Exception ex)
        {
            AppendLog($"BLE扫描错误: {ex.Message}");
            ShowToast($"蓝牙扫描失败: {ex.Message}", isError: true);
        }
        finally
        {
            IsBleScanning = false;
            BleScanButtonText = "扫描附近蓝牙设备";
            if (SelectedBleDevice == null && BleDevices.Count > 0)
                SelectedBleDevice = BleDevices[0];
        }
    }

    [RelayCommand]
    private async Task LoadPairedSppDevicesAsync()
    {
        var list = await _discoveryService.GetPairedClassicBluetoothDevicesAsync();
        PairedSppDevices.Clear();
        foreach (var item in list)
            PairedSppDevices.Add(item);

        if (list.Count == 0)
        {
            ShowToast("未发现已在 Windows 配对的经典蓝牙设备", isError: true);
        }
        else
        {
            if (string.IsNullOrWhiteSpace(SppMacAddress))
                SelectedPairedSppDevice = list[0];
            ShowToast($"已加载 {list.Count} 个已配对经典蓝牙设备");
        }
    }

    [RelayCommand]
    private Task ShowSppPairingGuideAsync() => _uiService.ShowSppPairingGuideAsync();

    [RelayCommand]
    private async Task ToggleConnectionAsync()
    {
        if (IsConnecting)
            return;

        if (IsConnected)
        {
            await DisconnectActiveAsync(userInitiated: true);
            return;
        }

        switch (SelectedConnectionMode)
        {
            case 0:
                await ConnectSerialAsync();
                break;
            case 1:
                await ConnectBleAsync();
                break;
            case 2:
                await ConnectSppAsync();
                break;
        }
    }

    private async Task ConnectSerialAsync()
    {
        string portName = SelectedSerialPort?.PortName ?? string.Empty;
        if (string.IsNullOrWhiteSpace(portName))
        {
            ShowToast("请先选择有效的串口端口", isError: true);
            return;
        }

        if (!int.TryParse(SelectedBaudRate, out int baud))
            baud = 9600;

        var transport = new SerialTransport();
        WireTransportEvents(transport);

        try
        {
            IsConnecting = true;
            ConnectButtonText = "正在连接串口...";
            await transport.ConnectAsync(portName, baud);

            _activeTransport = transport;
            _protocolParser.ClearBuffer();
            AppendLog($"串口已连接 -> {portName} @ {baud} bps");
            SetConnectedUiState(TransportKind.Serial, portName);

            _settings.SerialLastPort = portName;
            _settings.SerialLastBaud = baud.ToString();
            _settingsService.Save(_settings);

            SendInitialHandshake();
        }
        catch (Exception ex)
        {
            UnwireTransportEvents(transport);
            await transport.DisposeAsync();
            AppendLog($"串口连接失败: {ex.Message}");
            ShowToast($"串口打开失败: {ex.Message}", isError: true);
            SetDisconnectedUiState();
        }
        finally
        {
            IsConnecting = false;
        }
    }

    private async Task ConnectBleAsync()
    {
        if (SelectedBleDevice == null || string.IsNullOrWhiteSpace(SelectedBleDevice.Address))
        {
            ShowToast("请先扫描并选择一个蓝牙 BLE 设备", isError: true);
            return;
        }

        string? mac = RfcommWin32Socket.NormalizeMacAddress(SelectedBleDevice.Address);
        if (mac == null)
        {
            ShowToast("所选 BLE 设备地址格式无效", isError: true);
            return;
        }

        string preset = string.IsNullOrWhiteSpace(SelectedBlePreset) ? "JDY-29 (FFE0/FFE1)" : SelectedBlePreset;
        _settings.BlePreset = preset;
        _settingsService.Save(_settings);

        var transport = new BleTransport();
        WireTransportEvents(transport);

        try
        {
            IsConnecting = true;
            ConnectButtonText = "正在连接 BLE...";

            _settings.BleChars.TryGetValue(mac, out var savedChars);

            var (connected, newlyBound) = await transport.ConnectAsync(
                mac,
                SelectedBleDevice.RawBluetoothAddress,
                SelectedBleDevice.Name,
                preset,
                savedChars,
                (w, n, defW, defN) => _uiService.ShowBleCharSelectorAsync(w, n, defW, defN));

            if (!connected)
            {
                UnwireTransportEvents(transport);
                await transport.DisposeAsync();
                SetDisconnectedUiState();
                return;
            }

            if (newlyBound != null)
            {
                _settings.BleChars[mac] = newlyBound;
            }

            _activeTransport = transport;
            _protocolParser.ClearBuffer();
            AppendLog($"蓝牙 BLE 已成功连接 {transport.ConnectionDetail}");
            SetConnectedUiState(TransportKind.Ble, transport.ConnectionDetail);

            _settings.BleLastDeviceText = SelectedBleDevice.DisplayText;
            _settings.BleLastDeviceAddr = mac;
            _settingsService.Save(_settings);

            SendInitialHandshake();
        }
        catch (Exception ex)
        {
            UnwireTransportEvents(transport);
            await transport.DisposeAsync();
            AppendLog($"BLE连接错误: {ex.Message}");
            ShowToast($"蓝牙连接失败: {ex.Message}", isError: true);
            SetDisconnectedUiState();
        }
        finally
        {
            IsConnecting = false;
        }
    }

    private async Task ConnectSppAsync()
    {
        string? mac = RfcommWin32Socket.NormalizeMacAddress(SppMacAddress);
        if (mac == null)
        {
            ShowToast("请输入合法的蓝牙 MAC 地址，如 AA:BB:CC:DD:EE:FF", isError: true);
            return;
        }

        SppMacAddress = mac;
        if (!int.TryParse(SelectedSppChannel, out int channel))
            channel = 1;

        var transport = new SppTransport();
        WireTransportEvents(transport);

        try
        {
            IsConnecting = true;
            ConnectButtonText = "正在连接 SPP...";
            AppendLog($"正在通过 RFCOMM 连接经典蓝牙 SPP -> {mac} (通道 {channel})...");

            await transport.ConnectAsync(mac, channel);

            _activeTransport = transport;
            _protocolParser.ClearBuffer();
            _settings.SppLastAddr = mac;
            _settings.SppLastPort = channel.ToString();
            _settingsService.Save(_settings);

            AppendLog($"经典蓝牙 SPP 已连接 -> {mac}:{channel}");
            SetConnectedUiState(TransportKind.Spp, $"{mac}:{channel}");
            SendInitialHandshake();
        }
        catch (Exception ex)
        {
            UnwireTransportEvents(transport);
            await transport.DisposeAsync();
            AppendLog($"SPP 连接失败: {ex.Message}");
            ShowToast("SPP 连接失败：请确认模块已在系统中配对，或改用串口模式连接虚拟 COM 口", isError: true);
            SetDisconnectedUiState();
        }
        finally
        {
            IsConnecting = false;
        }
    }

    private void SendInitialHandshake()
    {
        SendCommand(IsSyncEnabled ? "sync on" : "status");
    }

    private async Task DisconnectActiveAsync(bool userInitiated)
    {
        var transport = Interlocked.Exchange(ref _activeTransport, null);
        if (transport == null)
        {
            SetDisconnectedUiState();
            return;
        }

        if (userInitiated && transport.IsConnected)
        {
            try
            {
                await SendDirectAsync(transport, "sync off");
            }
            catch { }
        }

        UnwireTransportEvents(transport);
        await transport.DisposeAsync();
        SetDisconnectedUiState();

        if (userInitiated)
        {
            string label = transport.Kind switch
            {
                TransportKind.Serial => "串口连接已安全断开",
                TransportKind.Ble => "蓝牙连接已断开",
                TransportKind.Spp => "经典蓝牙 SPP 连接已断开",
                _ => "设备连接已断开"
            };
            AppendLog(label);
        }
    }

    private void WireTransportEvents(ITransport transport)
    {
        transport.DataReceived += OnTransportDataReceived;
        transport.Disconnected += OnTransportDisconnected;
        transport.LogMessage += OnTransportLogMessage;
    }

    private void UnwireTransportEvents(ITransport transport)
    {
        transport.DataReceived -= OnTransportDataReceived;
        transport.Disconnected -= OnTransportDisconnected;
        transport.LogMessage -= OnTransportLogMessage;
    }

    private void OnTransportDataReceived(byte[] data)
    {
        _protocolParser.FeedBytes(data);
    }

    private void OnTransportLogMessage(string message)
    {
        _uiService.InvokeOnUiThread(() => AppendLog(message));
    }

    private void OnTransportDisconnected(string reason)
    {
        _uiService.InvokeOnUiThread(async () =>
        {
            var current = _activeTransport;
            if (current != null)
            {
                Interlocked.Exchange(ref _activeTransport, null);
                UnwireTransportEvents(current);
                await current.DisposeAsync();
            }
            SetDisconnectedUiState();
            AppendLog(reason);
        });
    }

    private void SetConnectedUiState(TransportKind kind, string detail)
    {
        IsConnected = true;
        ConnectButtonText = "断开当前连接";

        switch (kind)
        {
            case TransportKind.Serial:
                ConnDotColor = "#10B981";
                ConnStatusText = $"串口已连接 ({detail})";
                break;
            case TransportKind.Spp:
                ConnDotColor = "#8B5CF6";
                ConnStatusText = $"经典 SPP 已连接 ({detail})";
                break;
            case TransportKind.Ble:
                ConnDotColor = "#3B82F6";
                ConnStatusText = $"蓝牙 BLE 已连接 {detail}".Trim();
                break;
        }
        RefreshModeBadge();
    }

    private void SetDisconnectedUiState()
    {
        IsConnected = false;
        ConnectButtonText = "建立连接";
        ConnDotColor = "#94A3B8";
        ConnStatusText = "未连接设备";
        RefreshModeBadge();
    }

    // =================== 命令下发 ===================
    public void SendCommand(string command)
    {
        string trimmed = (command ?? string.Empty).Trim();
        if (trimmed.Length == 0)
            return;

        var transport = _activeTransport;
        if (transport == null || !transport.IsConnected)
            return;

        _ = SendDirectAsync(transport, trimmed);
    }

    private async Task SendDirectAsync(ITransport transport, string trimmedCommand)
    {
        byte[] payload = ProtocolParser.EncodeCommand(trimmedCommand);
        if (payload.Length == 0)
            return;

        string tag = transport.Kind switch
        {
            TransportKind.Serial => "Serial",
            TransportKind.Ble => "BLE",
            TransportKind.Spp => "SPP",
            _ => "Unknown"
        };

        _uiService.InvokeOnUiThread(() => AppendLog($"PC -> MCU ({tag}): {trimmedCommand}"));

        await _sendGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (transport.IsConnected)
            {
                await transport.SendAsync(payload).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _uiService.InvokeOnUiThread(() => AppendLog($"{tag}写入错误: {ex.Message}"));
        }
        finally
        {
            _sendGate.Release();
        }
    }

    // =================== 状态同步与刷新 ===================
    private void ApplyFirmwareState(ArmState snapshot)
    {
        _hasTelemetryPacket = true;
        _armState = snapshot;
        RefreshAllUiFromState();
    }

    private void RefreshModeBadge()
    {
        string modeName = FirmwareConstants.GetModeName(_armState.Mode);
        ModeDisplayText = IsConnected ? modeName : (_armState.Mode != 0 ? modeName : "未连接");
        DashModeText = modeName;
    }

    private void RefreshAllUiFromState()
    {
        int tVal = _armState.Temp;
        int hVal = _armState.Humi;
        int tLim = ParseClampedInt(TempThresholdText, FirmwareConstants.DefaultTempThreshold, 0, 100);
        int hLim = ParseClampedInt(HumiThresholdText, FirmwareConstants.DefaultHumiThreshold, 0, 100);

        bool tempAlarm = tVal > tLim;
        bool humiAlarm = hVal > hLim;
        IsTempAlarm = tempAlarm;
        IsHumiAlarm = humiAlarm;
        IsAnyEnvAlarm = tempAlarm || humiAlarm;

        SidebarTempText = _hasTelemetryPacket ? $"{tVal} °C" : "-- °C";
        SidebarHumiText = _hasTelemetryPacket ? $"{hVal} %" : "-- %";

        RefreshModeBadge();

        int ps = _armState.PlaybackStatus;
        PlaybackStatusKind = ps;
        PlaybackStatusDisplayText = FirmwareConstants.GetPlaybackStatusName(ps);
        SeqStatusText = ps switch
        {
            1 => "当前回放状态: 播放中…",
            2 => "当前回放状态: 已暂停",
            _ => "当前回放状态: 已停止"
        };

        int steps = _armState.RecordedSteps;
        RecordedStepsSidebarText = $"{steps} / {FirmwareConstants.MaxSteps} 步";
        DashStepsText = $"{steps} / {FirmwareConstants.MaxSteps} 步";
        SeqStepsBadgeText = $"已录制: {steps} / {FirmwareConstants.MaxSteps} 步";

        DashTempText = $"{tVal} °C";
        DashHumiText = $"{hVal} %";
        DashTempSubText = $"报警上限: > {tLim} °C";
        DashHumiSubText = $"报警上限: > {hLim} %";

        TempProgress = Math.Clamp(tVal / 100.0, 0.0, 1.0);
        HumiProgress = Math.Clamp(hVal / 100.0, 0.0, 1.0);

        if (IsAnyEnvAlarm)
        {
            MonitorAlertTitle = $"⚠ 环境超限报警！温度={tVal}°C (上限 {tLim}°C)，湿度={hVal}% (上限 {hLim}%)";
            MonitorAlertDesc = "检测到环境参数超出安全阈值，请检查工作环境或前往「硬件系统设置」调整报警上限。";
        }
        else
        {
            MonitorAlertTitle = "环境状态正常";
            MonitorAlertDesc = $"当前温度 {tVal}°C (≤{tLim}°C)、湿度 {hVal}% (≤{hLim}%)，均在安全阈值范围内。";
        }

        var activeIndices = ArmState.ComputeActiveIndices(_armState.Mask);
        int focusIdx = ArmState.ResolveFocusServoIndex(_armState.Mask, _armState.CurrentServoIndex);

        for (int i = 0; i < FirmwareConstants.MaxServos; i++)
        {
            bool active = ((_armState.Mask >> i) & 1) == 1;
            bool isFocus = active && i == focusIdx;
            ServoChannels[i].SyncFromState(_armState.Angles[i], active, isFocus);
        }

        DashActiveCountText = $"{activeIndices.Count} / {FirmwareConstants.MaxServos} 路";
        DashActiveSubText = activeIndices.Count > 0
            ? string.Join(", ", activeIndices.Select(i => $"S{i + 1}"))
            : "全部通道已禁用";
        DashFocusServoBadge = focusIdx >= 0
            ? $"固件焦点通道: S{focusIdx + 1:D2}"
            : "固件焦点通道: 无";
        ActiveServoSummaryText = $"活动通道: {activeIndices.Count} / {FirmwareConstants.MaxServos}";

        RefreshPoseMonoSnapshot();
    }

    private void RefreshPoseMonoSnapshot()
    {
        var line1 = string.Join("   ", Enumerable.Range(0, 8).Select(i => $"S{i + 1,2}:{_armState.Angles[i],3}°"));
        var line2 = string.Join("   ", Enumerable.Range(8, 8).Select(i => $"S{i + 1,2}:{_armState.Angles[i],3}°"));
        DashPoseMonoText = $"{line1}\n{line2}";
    }

    // =================== 舵机控制 ===================
    private void OnServoAngleCommitted(int index, int angle, bool sendNow)
    {
        _armState.Angles[index] = angle;
        RefreshPoseMonoSnapshot();
        if (sendNow && IsSyncEnabled)
        {
            SendCommand($"angle {index + 1} {angle}");
        }
    }

    private void OnServoEnabledToggled(int index, bool enabled)
    {
        if (enabled)
            _armState.Mask |= (ushort)(1 << index);
        else
            _armState.Mask &= (ushort)~(1 << index);

        _protocolParser.SetMask(_armState.Mask);
        _settings.ServosDefaultMask = _armState.Mask;
        _settingsService.Save(_settings);

        if (IsConnected)
            SendCommand($"servo {(enabled ? "on" : "off")} {index + 1}");

        RefreshAllUiFromState();
    }

    [RelayCommand]
    private async Task ApplyAllAnglesAsync()
    {
        for (int i = 0; i < FirmwareConstants.MaxServos; i++)
        {
            var ch = ServoChannels[i];
            if (!ch.IsEnabled)
                continue;
            _armState.Angles[i] = ch.Angle;
            SendCommand($"angle {i + 1} {ch.Angle}");
            await Task.Delay(20);
        }
        RefreshPoseMonoSnapshot();
    }

    [RelayCommand]
    private async Task ResetAllCenterAsync() => await BatchSetAngleAsync(90);

    [RelayCommand]
    private async Task ResetAllZeroAsync() => await BatchSetAngleAsync(0);

    private async Task BatchSetAngleAsync(int targetAngle)
    {
        for (int i = 0; i < FirmwareConstants.MaxServos; i++)
        {
            var ch = ServoChannels[i];
            if (!ch.IsEnabled)
                continue;
            ch.SetManualAngleSilently(targetAngle);
            _armState.Angles[i] = targetAngle;
            if (IsSyncEnabled)
            {
                SendCommand($"angle {i + 1} {targetAngle}");
                await Task.Delay(20);
            }
        }
        RefreshPoseMonoSnapshot();
    }

    [RelayCommand]
    private void QueryStatus()
    {
        if (!IsConnected)
        {
            ShowToast("请先连接设备", isError: true);
            return;
        }
        SendCommand("status");
    }

    [RelayCommand]
    private void CopyPoseSnapshot()
    {
        if (string.IsNullOrWhiteSpace(DashPoseMonoText))
            return;
        _uiService.CopyTextToClipboard(DashPoseMonoText);
        ShowToast("已复制 16 路舵机姿态快照");
    }

    // =================== 动作序列编排 ===================
    [RelayCommand] private void PlaySequence() => SendCommand("play");
    [RelayCommand] private void PauseSequence() => SendCommand("pause");
    [RelayCommand] private void ResumeSequence() => SendCommand("resume");
    [RelayCommand] private void StopSequence() => SendCommand("stop");
    [RelayCommand] private void RecordStep() => SendCommand("rec_step");
    [RelayCommand] private void ClearSequence() => SendCommand("rec_clear");

    [RelayCommand]
    private void SetSpeed(string multiplierStr)
    {
        if (!double.TryParse(multiplierStr, System.Globalization.CultureInfo.InvariantCulture, out double multiplier))
            return;

        string? cmd = multiplier switch
        {
            0.5 => "speed 5",
            1.0 => "speed 10",
            2.0 => "speed 20",
            _ => null
        };
        if (cmd == null)
            return;

        CurrentSpeedMultiplier = multiplier;
        _settings.SequenceSpeedMultiplier = multiplier;
        _settingsService.Save(_settings);

        SendCommand(cmd);
        if (IsConnected)
            ShowToast($"已切换序列播放速度为 {multiplier:0.0}x ({cmd})");
    }

    [RelayCommand]
    private void AdjustDelay(string? deltaText)
    {
        if (!int.TryParse(deltaText, out int delta))
            return;
        int cur = ParseClampedInt(DelaySecondsText, 0, 0, 600);
        int next = Math.Clamp(cur + delta, 0, 600);
        DelaySecondsText = next.ToString();
        _settings.SequenceLastDelay = next;
        _settingsService.Save(_settings);
    }

    [RelayCommand]
    private void ApplyDelay()
    {
        if (!int.TryParse(DelaySecondsText?.Trim(), out int val))
        {
            ShowToast("请输入有效的整数延时秒数 (0~600)", isError: true);
            return;
        }
        val = Math.Clamp(val, 0, 600);
        DelaySecondsText = val.ToString();
        _settings.SequenceLastDelay = val;
        _settingsService.Save(_settings);
        SendCommand($"rec_delay {val}");
    }

    [RelayCommand]
    private void AdjustInsertPosition(string? deltaText)
    {
        if (!int.TryParse(deltaText, out int delta))
            return;
        int cur = ParseClampedInt(InsertPositionText, 1, 1, FirmwareConstants.MaxSteps);
        InsertPositionText = Math.Clamp(cur + delta, 1, FirmwareConstants.MaxSteps).ToString();
    }

    [RelayCommand]
    private void InsertStep()
    {
        if (!int.TryParse(InsertPositionText?.Trim(), out int idx))
        {
            ShowToast($"请输入有效的插入步骤序号 (1~{FirmwareConstants.MaxSteps})", isError: true);
            return;
        }
        idx = Math.Clamp(idx, 1, FirmwareConstants.MaxSteps);
        InsertPositionText = idx.ToString();
        SendCommand($"rec_insert {idx}");
        if (IsConnected)
            ShowToast($"已发送在第 {idx} 步前插入当前姿态指令");
    }

    // =================== 硬件与本地设置 ===================
    [RelayCommand]
    private async Task EnableAllServosAsync() => await BatchSetServosEnabledAsync(true);

    [RelayCommand]
    private async Task DisableAllServosAsync() => await BatchSetServosEnabledAsync(false);

    private async Task BatchSetServosEnabledAsync(bool enableAll)
    {
        for (int i = 0; i < FirmwareConstants.MaxServos; i++)
        {
            bool cur = ((_armState.Mask >> i) & 1) == 1;
            if (cur == enableAll)
                continue;

            if (enableAll)
                _armState.Mask |= (ushort)(1 << i);
            else
                _armState.Mask &= (ushort)~(1 << i);

            if (IsConnected)
            {
                SendCommand($"servo {(enableAll ? "on" : "off")} {i + 1}");
                await Task.Delay(20);
            }
        }

        _protocolParser.SetMask(_armState.Mask);
        _settings.ServosDefaultMask = _armState.Mask;
        _settingsService.Save(_settings);
        RefreshAllUiFromState();
    }

    [RelayCommand]
    private void SaveServoConfig()
    {
        if (!IsConnected)
        {
            ShowToast("请先连接设备后再保存到硬件", isError: true);
            return;
        }
        SendCommand("servo_save");
        ShowToast("已发送保存舵机配置指令");
    }

    [RelayCommand]
    private void SaveThresholds()
    {
        if (!int.TryParse(TempThresholdText?.Trim(), out int t)
            || !int.TryParse(HumiThresholdText?.Trim(), out int h))
        {
            ShowToast("请输入有效的整数温度与湿度阈值 (0~100)", isError: true);
            return;
        }

        t = Math.Clamp(t, 0, 100);
        h = Math.Clamp(h, 0, 100);
        TempThresholdText = t.ToString();
        HumiThresholdText = h.ToString();

        SendCommand($"set_thresh {t} {h}");
        _settings.ThresholdTemp = t;
        _settings.ThresholdHumi = h;
        _settingsService.Save(_settings);

        RefreshAllUiFromState();
        ShowToast("报警阈值已保存并下发");
    }

    [RelayCommand]
    private void ResetArm()
    {
        if (!IsConnected)
        {
            ShowToast("请先连接设备", isError: true);
            return;
        }
        SendCommand("reset_arm");
        ShowToast("已发送舵机复位到初始位置指令 (reset_arm)");
    }

    [RelayCommand]
    private void SendHelp()
    {
        if (!IsConnected)
        {
            ShowToast("请先连接设备", isError: true);
            return;
        }
        if (IsLogCollapsed)
            IsLogCollapsed = false;
        SendCommand("help");
        ShowToast("已发送 help 指令，请在下方通信日志查看固件命令表");
    }

    [RelayCommand]
    private async Task ResetSystemAsync()
    {
        bool confirmed = await _uiService.ShowConfirmDialogAsync(
            "确认执行系统重置？",
            "此操作会清除 MCU Flash 中的所有配置（初始角度、温湿度报警阈值、已录制的动作序列），并将所有舵机复位到默认状态。\n\n该操作不可撤销，是否确认继续？",
            "确认系统重置",
            isDanger: true);

        if (!confirmed)
            return;

        if (!IsConnected)
        {
            ShowToast("请先连接设备", isError: true);
            return;
        }
        SendCommand("reset_system");
        ShowToast("已发送系统出厂重置指令 (reset_system)");
    }

    [RelayCommand]
    private void OpenConfigDirectory()
    {
        try
        {
            _uiService.OpenFolderInExplorer(_settingsService.AppDirectory);
        }
        catch (Exception ex)
        {
            ShowToast($"打开配置目录失败: {ex.Message}", isError: true);
        }
    }

    [RelayCommand]
    private void SaveAllSettings()
    {
        bool ok = FlushAllSettingsToDisk();
        if (ok)
            ShowToast($"配置已保存至 {FirmwareConstants.SettingsFileName}");
        else
            ShowToast("保存配置文件失败，请检查目录写入权限", isError: true);
    }

    [RelayCommand]
    private async Task ResetLocalConfigAsync()
    {
        bool confirmed = await _uiService.ShowConfirmDialogAsync(
            "恢复上位机本地默认配置？",
            $"此操作仅将本地配置文件 ({FirmwareConstants.SettingsFileName}) 重置为初始默认值，不会清除下位机 Flash 中的数据。",
            "确认重置",
            isDanger: false);

        if (!confirmed)
            return;

        _isInitializing = true;
        _settings = _settingsService.ResetToDefault();
        _armState.Mask = (ushort)(_settings.ServosDefaultMask & 0xFFFF);
        _protocolParser.SetMask(_armState.Mask);
        ApplySettingsToUi(_settings);
        RefreshAllUiFromState();
        _isInitializing = false;

        ShowToast("已恢复上位机本地默认配置");
    }

    private void ApplySettingsToUi(AppSettings s)
    {
        IsDarkMode = s.DarkMode;
        _uiService.ApplyTheme(s.DarkMode);

        IsSyncEnabled = s.Sync;
        SyncHintText = s.Sync
            ? "开启后调节舵机将实时下发指令"
            : "非同步模式：请点击手动控制页【应用所有角度】";
        OnPropertyChanged(nameof(ShowNonSyncControls));

        SelectedTabIndex = s.LastTab is >= 0 and < 5 ? s.LastTab : 0;
        WindowWidth = Math.Max(FirmwareConstants.MinWindowWidth, s.WindowWidth);
        WindowHeight = Math.Max(FirmwareConstants.MinWindowHeight, s.WindowHeight);

        TempThresholdText = Math.Clamp(s.ThresholdTemp, 0, 100).ToString();
        HumiThresholdText = Math.Clamp(s.ThresholdHumi, 0, 100).ToString();
        DelaySecondsText = Math.Clamp(s.SequenceLastDelay, 0, 600).ToString();

        double spd = s.SequenceSpeedMultiplier is 0.5 or 1.0 or 2.0 ? s.SequenceSpeedMultiplier : 1.0;
        CurrentSpeedMultiplier = spd;

        if (!string.IsNullOrWhiteSpace(s.SerialLastPort))
        {
            var match = SerialPorts.FirstOrDefault(p => string.Equals(p.PortName, s.SerialLastPort, StringComparison.OrdinalIgnoreCase));
            if (match != null) SelectedSerialPort = match;
        }
        if (FirmwareConstants.BaudRates.Contains(s.SerialLastBaud))
            SelectedBaudRate = s.SerialLastBaud;

        SelectedBlePreset = FirmwareConstants.BlePresets.ContainsKey(s.BlePreset)
            ? s.BlePreset
            : "JDY-29 (FFE0/FFE1)";

        if (!string.IsNullOrWhiteSpace(s.BleLastDeviceText) && !string.IsNullOrWhiteSpace(s.BleLastDeviceAddr))
        {
            var existing = BleDevices.FirstOrDefault(d => string.Equals(d.Address, s.BleLastDeviceAddr, StringComparison.OrdinalIgnoreCase));
            if (existing == null)
            {
                string displayName = s.BleLastDeviceText;
                int idxBracket = displayName.IndexOf('[');
                if (idxBracket > 0) displayName = displayName[..idxBracket].Trim();
                ulong rawAddr = 0;
                try { rawAddr = RfcommWin32Socket.ParseBluetoothAddress(s.BleLastDeviceAddr); } catch { }
                existing = new BleDeviceItem(s.BleLastDeviceAddr, displayName, 0, rawAddr);
                BleDevices.Add(existing);
            }
            SelectedBleDevice = existing;
        }

        SppMacAddress = s.SppLastAddr ?? string.Empty;
        SelectedSppChannel = FirmwareConstants.RfcommChannels.Contains(s.SppLastPort) ? s.SppLastPort : "1";
        SelectedConnectionMode = s.ConnectionMode is >= 0 and <= 2 ? s.ConnectionMode : 0;

        LogHeight = Math.Clamp(s.LogHeight, FirmwareConstants.MinLogHeight, 600);
        IsLogCollapsed = s.LogCollapsed;
        IsAutoScroll = s.LogAutoScroll;
    }

    public bool FlushAllSettingsToDisk()
    {
        _settings.DarkMode = IsDarkMode;
        _settings.Sync = IsSyncEnabled;
        _settings.LastTab = SelectedTabIndex is >= 0 and < 5 ? SelectedTabIndex : 0;
        _settings.WindowWidth = Math.Max(FirmwareConstants.MinWindowWidth, (int)Math.Round(WindowWidth));
        _settings.WindowHeight = Math.Max(FirmwareConstants.MinWindowHeight, (int)Math.Round(WindowHeight));
        _settings.ConnectionMode = SelectedConnectionMode;

        if (!string.IsNullOrWhiteSpace(SelectedSerialPort?.PortName))
            _settings.SerialLastPort = SelectedSerialPort.PortName;
        if (!string.IsNullOrWhiteSpace(SelectedBaudRate))
            _settings.SerialLastBaud = SelectedBaudRate;

        _settings.BlePreset = string.IsNullOrWhiteSpace(SelectedBlePreset)
            ? "JDY-29 (FFE0/FFE1)"
            : SelectedBlePreset;
        if (SelectedBleDevice != null)
        {
            _settings.BleLastDeviceText = SelectedBleDevice.DisplayText;
            _settings.BleLastDeviceAddr = SelectedBleDevice.Address;
        }

        _settings.SppLastAddr = (SppMacAddress ?? string.Empty).Trim();
        _settings.SppLastPort = string.IsNullOrWhiteSpace(SelectedSppChannel) ? "1" : SelectedSppChannel;
        _settings.SequenceSpeedMultiplier = CurrentSpeedMultiplier;
        if (int.TryParse(DelaySecondsText, out int delay))
            _settings.SequenceLastDelay = Math.Clamp(delay, 0, 600);
        if (int.TryParse(TempThresholdText, out int t))
            _settings.ThresholdTemp = Math.Clamp(t, 0, 100);
        if (int.TryParse(HumiThresholdText, out int h))
            _settings.ThresholdHumi = Math.Clamp(h, 0, 100);

        _settings.ServosDefaultMask = _armState.Mask;
        _settings.LogHeight = Math.Clamp((int)Math.Round(LogHeight), FirmwareConstants.MinLogHeight, 600);
        _settings.LogCollapsed = IsLogCollapsed;
        _settings.LogAutoScroll = IsAutoScroll;

        return _settingsService.Save(_settings);
    }

    // =================== 通信日志与调试终端 ===================
    public void AppendLog(string message)
    {
        string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
        string plainLine = $"[{timestamp}] {message}";
        _rawLogLines.Add(plainLine);
        if (_rawLogLines.Count > FirmwareConstants.MaxRawLogLines)
        {
            _rawLogLines.RemoveRange(0, _rawLogLines.Count - FirmwareConstants.MaxRawLogLines);
        }

        var (category, tagLabel) = ProtocolParser.ClassifyLog(message);
        var entry = new LogEntry(timestamp, category, tagLabel, message, plainLine);

        HasLogs = true;
        LogCountText = $"{_rawLogLines.Count} 条";
        LogPreviewText = plainLine;
        LogAppended?.Invoke(entry);
    }

    [RelayCommand]
    private void SendQuickCommand()
    {
        string cmd = (QuickCommandText ?? string.Empty).Trim();
        if (cmd.Length == 0)
            return;

        if (!IsConnected)
        {
            ShowToast("未连接设备，无法发送指令", isError: true);
            return;
        }

        if (_cmdHistory.Count == 0 || !string.Equals(_cmdHistory[^1], cmd, StringComparison.Ordinal))
            _cmdHistory.Add(cmd);
        _cmdHistoryIndex = _cmdHistory.Count;

        SendCommand(cmd);
        QuickCommandText = string.Empty;
    }

    public void NavigateCommandHistory(int delta)
    {
        if (_cmdHistory.Count == 0)
            return;

        _cmdHistoryIndex = Math.Clamp(_cmdHistoryIndex + delta, 0, _cmdHistory.Count);
        QuickCommandText = _cmdHistoryIndex < _cmdHistory.Count ? _cmdHistory[_cmdHistoryIndex] : string.Empty;
    }

    [RelayCommand]
    private void ClearLogs()
    {
        _rawLogLines.Clear();
        HasLogs = false;
        LogCountText = "0 条";
        LogPreviewText = "日志已清空";
        LogsCleared?.Invoke();
    }

    [RelayCommand]
    private void CopyAllLogs()
    {
        if (_rawLogLines.Count == 0)
        {
            ShowToast("当前没有可复制的通信日志", isError: true);
            return;
        }
        _uiService.CopyTextToClipboard(string.Join(Environment.NewLine, _rawLogLines));
        ShowToast($"已复制全部 {_rawLogLines.Count} 条日志到剪贴板");
    }

    [RelayCommand]
    private async Task ExportLogsAsync()
    {
        if (_rawLogLines.Count == 0)
        {
            ShowToast("当前没有可导出的通信日志", isError: true);
            return;
        }

        string defaultName = $"arm_log_{DateTime.Now:yyyyMMdd_HHmmss}.log";
        string? path = await _uiService.ShowSaveLogFileDialogAsync(defaultName);
        if (string.IsNullOrWhiteSpace(path))
            return;

        try
        {
            await File.WriteAllTextAsync(path, string.Join(Environment.NewLine, _rawLogLines) + Environment.NewLine, Encoding.UTF8);
            ShowToast($"日志已成功导出至: {path}");
        }
        catch (Exception ex)
        {
            ShowToast($"导出日志失败: {ex.Message}", isError: true);
        }
    }

    [RelayCommand]
    private void ToggleLogCollapse()
    {
        IsLogCollapsed = !IsLogCollapsed;
    }

    // =================== Toast 通知 ===================
    public void ShowToast(string message, bool isError = false)
    {
        int seq = Interlocked.Increment(ref _toastSeq);
        ToastMessage = message;
        IsToastError = isError;
        IsToastVisible = true;
        _ = AutoDismissToastAsync(seq);
    }

    [RelayCommand]
    private void DismissToast()
    {
        Interlocked.Increment(ref _toastSeq);
        IsToastVisible = false;
    }

    private async Task AutoDismissToastAsync(int seqId)
    {
        await Task.Delay(2600);
        _uiService.InvokeOnUiThread(() =>
        {
            if (Volatile.Read(ref _toastSeq) == seqId)
                IsToastVisible = false;
        });
    }

    // =================== 关闭清理 ===================
    public async Task ShutdownAsync()
    {
        if (Interlocked.Exchange(ref _isShuttingDown, 1) != 0)
            return;

        FlushAllSettingsToDisk();
        await DisconnectActiveAsync(userInitiated: true);
    }

    public void SimulateTelemetryForTest(string packetLine)
    {
        string normalized = packetLine.StartsWith('$') ? packetLine : $"${packetLine}#\n";
        _protocolParser.FeedBytes(Encoding.UTF8.GetBytes(normalized));
    }

    private static int ParseClampedInt(string? raw, int fallback, int min, int max)
    {
        return int.TryParse(raw?.Trim(), out int v) ? Math.Clamp(v, min, max) : fallback;
    }
}
