namespace ArmControlSuite.Models;

/// <summary>
/// 固件 V4.12 协议常量与默认值定义。
/// </summary>
public static class FirmwareConstants
{
    public const int DefaultTempThreshold = 40;
    public const int DefaultHumiThreshold = 80;
    public const int MaxServos = 16;
    public const int MaxSteps = 50;
    public const ushort DefaultServoMask = 0x007F; // S1 ~ S7 默认启用

    public const int DefaultWindowWidth = 1380;
    public const int DefaultWindowHeight = 880;
    public const int MinWindowWidth = 1080;
    public const int MinWindowHeight = 660;

    public const int DefaultLogHeight = 230;
    public const int MinLogHeight = 125;
    public const int CollapsedLogHeight = 46;
    public const int MaxUiLogItems = 400;
    public const int MaxRawLogLines = 5000;

    public const string SettingsFileName = "arm_suite_config.json";

    /// <summary>
    /// 固件 V4.12 全部 11 种 UI 模式 (MODE 0~10)。
    /// </summary>
    public static readonly IReadOnlyList<string> ModeNames =
    [
        "主菜单",
        "手动控制",
        "设初始角",
        "录制角度",
        "录制延时",
        "播放序列",
        "环境监控",
        "设阈值",
        "舵机设置",
        "编辑序列",
        "播放速度"
    ];

    /// <summary>
    /// 回放状态机名称 (P_STAT 0~2)。
    /// </summary>
    public static readonly IReadOnlyList<string> PlaybackStatusNames =
    [
        "已停止",
        "播放中",
        "已暂停"
    ];

    /// <summary>
    /// 支持的串口波特率列表。
    /// </summary>
    public static readonly IReadOnlyList<string> BaudRates =
    [
        "9600",
        "19200",
        "38400",
        "57600",
        "115200"
    ];

    /// <summary>
    /// 经典蓝牙 SPP RFCOMM 通道选项。
    /// </summary>
    public static readonly IReadOnlyList<string> RfcommChannels =
    [
        "1",
        "2",
        "3",
        "4"
    ];

    /// <summary>
    /// 常用 BLE 透传模块 (WriteUuid, NotifyUuid) 预设。
    /// </summary>
    public static readonly IReadOnlyDictionary<string, (string WriteUuid, string NotifyUuid)> BlePresets =
        new Dictionary<string, (string WriteUuid, string NotifyUuid)>
        {
            ["JDY-29 (FFE0/FFE1)"] = (
                "0000ffe1-0000-1000-8000-00805f9b34fb",
                "0000ffe1-0000-1000-8000-00805f9b34fb"
            ),
            ["HM-10 (FFE0/FFE1)"] = (
                "0000ffe1-0000-1000-8000-00805f9b34fb",
                "0000ffe1-0000-1000-8000-00805f9b34fb"
            ),
            ["Nordic UART (NUS)"] = (
                "6e400002-b5a3-f393-e0a9-e50e24dcca9e",
                "6e400003-b5a3-f393-e0a9-e50e24dcca9e"
            ),
            ["HC-08 / 微信 AirSync"] = (
                "0000ffe2-0000-1000-8000-00805f9b34fb",
                "0000ffe1-0000-1000-8000-00805f9b34fb"
            ),
            ["TI CC2540 (SimpleBLE)"] = (
                "0000ffe1-0000-1000-8000-00805f9b34fb",
                "0000ffe1-0000-1000-8000-00805f9b34fb"
            ),
            ["自定义 (手动选择)"] = ("", "")
        };

    public static readonly IReadOnlyList<string> BlePresetNames = BlePresets.Keys.ToList();

    /// <summary>
    /// 忽略大小写与连字符比较两个蓝牙 UUID。
    /// </summary>
    public static bool IsSameUuid(string? u1, string? u2)
    {
        if (string.IsNullOrWhiteSpace(u1) || string.IsNullOrWhiteSpace(u2))
            return false;
        return string.Equals(
            u1.Replace("-", string.Empty),
            u2.Replace("-", string.Empty),
            StringComparison.OrdinalIgnoreCase);
    }

    public static string GetModeName(int mode) =>
        mode >= 0 && mode < ModeNames.Count ? ModeNames[mode] : $"未知({mode})";

    public static string GetPlaybackStatusName(int status) =>
        status >= 0 && status < PlaybackStatusNames.Count ? PlaybackStatusNames[status] : "未知";
}
