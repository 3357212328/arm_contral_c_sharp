using System.Text.Json;
using System.Text.Json.Serialization;

namespace ArmControlSuite.Models;

/// <summary>
/// 已绑定的蓝牙 BLE 透传特性 UUID 对。
/// </summary>
public sealed class BleSavedChars
{
    [JsonPropertyName("write_uuid")]
    public string WriteUuid { get; set; } = string.Empty;

    [JsonPropertyName("notify_uuid")]
    public string NotifyUuid { get; set; } = string.Empty;

    public BleSavedChars Clone() => new()
    {
        WriteUuid = WriteUuid,
        NotifyUuid = NotifyUuid
    };
}

/// <summary>
/// 与 Python 版 arm_suite_config.json 完全兼容的配置文件结构。
/// </summary>
public sealed class AppSettings
{
    [JsonPropertyName("general/dark_mode")]
    public bool DarkMode { get; set; } = false;

    [JsonPropertyName("general/sync")]
    public bool Sync { get; set; } = true;

    [JsonPropertyName("ui/last_tab")]
    public int LastTab { get; set; } = 0;

    [JsonPropertyName("window/width")]
    public int WindowWidth { get; set; } = FirmwareConstants.DefaultWindowWidth;

    [JsonPropertyName("window/height")]
    public int WindowHeight { get; set; } = FirmwareConstants.DefaultWindowHeight;

    [JsonPropertyName("connection/mode")]
    public int ConnectionMode { get; set; } = 0;

    [JsonPropertyName("serial/last_port")]
    public string SerialLastPort { get; set; } = string.Empty;

    [JsonPropertyName("serial/last_baud")]
    public string SerialLastBaud { get; set; } = "9600";

    [JsonPropertyName("ble/preset")]
    public string BlePreset { get; set; } = "JDY-29 (FFE0/FFE1)";

    [JsonPropertyName("ble/last_device_text")]
    public string BleLastDeviceText { get; set; } = string.Empty;

    [JsonPropertyName("ble/last_device_addr")]
    public string BleLastDeviceAddr { get; set; } = string.Empty;

    [JsonPropertyName("ble_chars")]
    public Dictionary<string, BleSavedChars> BleChars { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonPropertyName("spp/last_addr")]
    public string SppLastAddr { get; set; } = string.Empty;

    [JsonPropertyName("spp/last_port")]
    public string SppLastPort { get; set; } = "1";

    [JsonPropertyName("sequence/speed_multiplier")]
    public double SequenceSpeedMultiplier { get; set; } = 1.0;

    [JsonPropertyName("sequence/last_delay")]
    public int SequenceLastDelay { get; set; } = 1;

    [JsonPropertyName("thresholds/temp")]
    public int ThresholdTemp { get; set; } = FirmwareConstants.DefaultTempThreshold;

    [JsonPropertyName("thresholds/humi")]
    public int ThresholdHumi { get; set; } = FirmwareConstants.DefaultHumiThreshold;

    [JsonPropertyName("servos/default_mask")]
    public int ServosDefaultMask { get; set; } = FirmwareConstants.DefaultServoMask;

    [JsonPropertyName("log/height")]
    public int LogHeight { get; set; } = FirmwareConstants.DefaultLogHeight;

    [JsonPropertyName("log/collapsed")]
    public bool LogCollapsed { get; set; } = false;

    [JsonPropertyName("log/auto_scroll")]
    public bool LogAutoScroll { get; set; } = true;

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }

    public static AppSettings CreateDefault() => new();

    public AppSettings Clone()
    {
        var copy = new AppSettings
        {
            DarkMode = DarkMode,
            Sync = Sync,
            LastTab = LastTab,
            WindowWidth = WindowWidth,
            WindowHeight = WindowHeight,
            ConnectionMode = ConnectionMode,
            SerialLastPort = SerialLastPort,
            SerialLastBaud = SerialLastBaud,
            BlePreset = BlePreset,
            BleLastDeviceText = BleLastDeviceText,
            BleLastDeviceAddr = BleLastDeviceAddr,
            BleChars = new Dictionary<string, BleSavedChars>(StringComparer.OrdinalIgnoreCase),
            SppLastAddr = SppLastAddr,
            SppLastPort = SppLastPort,
            SequenceSpeedMultiplier = SequenceSpeedMultiplier,
            SequenceLastDelay = SequenceLastDelay,
            ThresholdTemp = ThresholdTemp,
            ThresholdHumi = ThresholdHumi,
            ServosDefaultMask = ServosDefaultMask,
            LogHeight = LogHeight,
            LogCollapsed = LogCollapsed,
            LogAutoScroll = LogAutoScroll
        };

        if (BleChars != null)
        {
            foreach (var kv in BleChars)
                copy.BleChars[kv.Key] = kv.Value.Clone();
        }

        if (ExtensionData != null)
        {
            copy.ExtensionData = new Dictionary<string, JsonElement>(ExtensionData);
        }

        return copy;
    }
}
