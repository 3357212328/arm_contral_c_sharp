using System.Text;
using ArmControlSuite.Models;

namespace ArmControlSuite.Services;

/// <summary>
/// 固件 V4.12 协议解析与命令编码器。
/// </summary>
public sealed class ProtocolParser
{
    private const int MaxBufferSize = 65536;
    private readonly List<byte> _buffer = new(1024);
    private readonly object _syncLock = new();
    private readonly ArmState _state;

    public ProtocolParser(ArmState? initialState = null)
    {
        _state = initialState ?? new ArmState();
    }

    public event Action<string>? LogMessage;
    public event Action<ArmState>? StateUpdated;

    public ArmState GetStateSnapshot()
    {
        lock (_syncLock)
        {
            return _state.Clone();
        }
    }

    public void SetMask(ushort mask)
    {
        lock (_syncLock)
        {
            _state.Mask = mask;
        }
    }

    public void ClearBuffer()
    {
        lock (_syncLock)
        {
            _buffer.Clear();
        }
    }

    /// <summary>
    /// 将待发送指令编码为 ASCII + CRLF 字节数组。
    /// </summary>
    public static byte[] EncodeCommand(string command)
    {
        string trimmed = (command ?? string.Empty).Trim();
        if (trimmed.Length == 0)
            return [];
        return Encoding.ASCII.GetBytes($"{trimmed}\r\n");
    }

    /// <summary>
    /// 写入接收字节流并解析其中的 $...# 状态帧与普通行响应。
    /// </summary>
    public void FeedBytes(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
            return;

        List<string> logsToEmit = [];
        List<ArmState> statesToEmit = [];

        lock (_syncLock)
        {
            if (_buffer.Count + data.Length > MaxBufferSize)
            {
                int drop = Math.Min(_buffer.Count, (_buffer.Count + data.Length) - MaxBufferSize);
                _buffer.RemoveRange(0, drop);
            }

            foreach (byte b in data)
            {
                _buffer.Add(b);
            }

            // 1) 解析 $...# 状态数据包
            while (true)
            {
                int endIndex = _buffer.IndexOf((byte)'#');
                if (endIndex < 0)
                    break;

                int startIndex = -1;
                for (int i = endIndex - 1; i >= 0; i--)
                {
                    if (_buffer[i] == (byte)'$')
                    {
                        startIndex = i;
                        break;
                    }
                }

                if (startIndex != -1)
                {
                    int len = endIndex - startIndex - 1;
                    byte[] packetBytes = new byte[len];
                    _buffer.CopyTo(startIndex + 1, packetBytes, 0, len);
                    string decodedPacket = Encoding.Latin1.GetString(packetBytes);
                    logsToEmit.Add($"MCU -> PC (Packet): {decodedPacket}");
                    ParseStatusPacketLocked(decodedPacket);
                    statesToEmit.Add(_state.Clone());
                }

                _buffer.RemoveRange(0, endIndex + 1);
            }

            // 2) 解析普通文本行响应 (\r / \n 分隔)
            while (true)
            {
                int nEnd = _buffer.IndexOf((byte)'\n');
                int rEnd = _buffer.IndexOf((byte)'\r');
                int lineEnd = nEnd;
                if (rEnd != -1 && (lineEnd == -1 || rEnd < lineEnd))
                    lineEnd = rEnd;
                if (lineEnd == -1)
                    break;

                byte[] lineBytes = new byte[lineEnd];
                if (lineEnd > 0)
                    _buffer.CopyTo(0, lineBytes, 0, lineEnd);

                string decodedResponse = Encoding.Latin1.GetString(lineBytes).Trim();
                if (!string.IsNullOrEmpty(decodedResponse) && !decodedResponse.StartsWith('$'))
                {
                    logsToEmit.Add($"MCU -> PC (Response): {decodedResponse}");
                }

                _buffer.RemoveRange(0, lineEnd + 1);
            }
        }

        foreach (string msg in logsToEmit)
            LogMessage?.Invoke(msg);

        foreach (ArmState snapshot in statesToEmit)
            StateUpdated?.Invoke(snapshot);
    }

    private void ParseStatusPacketLocked(string packetStr)
    {
        string[] parts = packetStr.Trim().Split(';');
        foreach (string part in parts)
        {
            int colonIdx = part.IndexOf(':');
            if (colonIdx <= 0)
                continue;

            string key = part[..colonIdx];
            string value = part[(colonIdx + 1)..];

            switch (key)
            {
                case "MODE":
                    if (int.TryParse(value, out int mode))
                        _state.Mode = mode;
                    break;
                case "CUR_S_IDX":
                    if (int.TryParse(value, out int curIdx))
                        _state.CurrentServoIndex = curIdx;
                    break;
                case "ANGS":
                {
                    string[] tokens = value.Split(',');
                    if (tokens.Length == FirmwareConstants.MaxServos)
                    {
                        int[] parsed = new int[FirmwareConstants.MaxServos];
                        bool allOk = true;
                        for (int i = 0; i < FirmwareConstants.MaxServos; i++)
                        {
                            if (int.TryParse(tokens[i], out int ang))
                                parsed[i] = Math.Clamp(ang, 0, 180);
                            else
                            {
                                allOk = false;
                                break;
                            }
                        }
                        if (allOk)
                            Array.Copy(parsed, _state.Angles, FirmwareConstants.MaxServos);
                    }
                    break;
                }
                case "MASK":
                    if (int.TryParse(value, out int mask))
                        _state.Mask = (ushort)(mask & 0xFFFF);
                    break;
                case "TEMP":
                    if (int.TryParse(value, out int temp))
                        _state.Temp = temp;
                    break;
                case "HUMI":
                    if (int.TryParse(value, out int humi))
                        _state.Humi = humi;
                    break;
                case "R_STEPS":
                    if (int.TryParse(value, out int steps))
                        _state.RecordedSteps = Math.Max(0, steps);
                    break;
                case "P_STAT":
                    if (int.TryParse(value, out int pstat))
                        _state.PlaybackStatus = pstat;
                    break;
            }
        }
    }

    /// <summary>
    /// 按日志正文分类返回日志级别与标签。
    /// </summary>
    public static (LogCategory Category, string TagLabel) ClassifyLog(string message)
    {
        if (message.StartsWith("PC -> MCU", StringComparison.Ordinal))
            return (LogCategory.Tx, "TX ");
        if (message.Contains("MCU -> PC (Packet)", StringComparison.Ordinal))
            return (LogCategory.Packet, "PKT");
        if (message.Contains("ERROR", StringComparison.OrdinalIgnoreCase)
            || message.Contains("错误", StringComparison.Ordinal)
            || message.Contains("失败", StringComparison.Ordinal)
            || message.Contains("异常", StringComparison.Ordinal)
            || message.Contains("不可用", StringComparison.Ordinal))
            return (LogCategory.Error, "ERR");
        if (message.Contains("MCU -> PC", StringComparison.Ordinal))
            return (LogCategory.Rx, "RX ");
        return (LogCategory.System, "SYS");
    }
}
