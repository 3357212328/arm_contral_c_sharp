namespace ArmControlSuite.Models;

/// <summary>
/// 下位机机械臂实时状态快照。
/// </summary>
public sealed class ArmState
{
    public int Mode { get; set; }
    public int CurrentServoIndex { get; set; }
    public int[] Angles { get; }
    public ushort Mask { get; set; }
    public int Temp { get; set; }
    public int Humi { get; set; }
    public int RecordedSteps { get; set; }
    public int PlaybackStatus { get; set; }

    public ArmState(ushort initialMask = FirmwareConstants.DefaultServoMask)
    {
        Angles = new int[FirmwareConstants.MaxServos];
        Array.Fill(Angles, 90);
        Mask = initialMask;
    }

    public ArmState Clone()
    {
        var copy = new ArmState(Mask)
        {
            Mode = Mode,
            CurrentServoIndex = CurrentServoIndex,
            Temp = Temp,
            Humi = Humi,
            RecordedSteps = RecordedSteps,
            PlaybackStatus = PlaybackStatus
        };
        Array.Copy(Angles, copy.Angles, FirmwareConstants.MaxServos);
        return copy;
    }

    /// <summary>
    /// 返回所有已启用舵机通道的 0-based 索引列表。
    /// </summary>
    public List<int> GetActiveIndices() => ComputeActiveIndices(Mask);

    public static List<int> ComputeActiveIndices(ushort mask)
    {
        var indices = new List<int>(FirmwareConstants.MaxServos);
        for (int i = 0; i < FirmwareConstants.MaxServos; i++)
        {
            if (((mask >> i) & 1) == 1)
                indices.Add(i);
        }
        return indices;
    }

    /// <summary>
    /// 按固件 CUR_S_IDX 取模规则解析当前焦点舵机通道 (0-based，无启用通道则返回 -1)。
    /// </summary>
    public static int ResolveFocusServoIndex(ushort mask, int currentServoIndex)
    {
        var active = ComputeActiveIndices(mask);
        if (active.Count == 0)
            return -1;
        int mod = ((currentServoIndex % active.Count) + active.Count) % active.Count;
        return active[mod];
    }
}
