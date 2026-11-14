using ArmControlSuite.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArmControlSuite.ViewModels;

/// <summary>
/// 单个舵机通道 (S01 ~ S16) 的交互视图模型。
/// </summary>
public partial class ServoChannelViewModel : ObservableObject
{
    private readonly Action<int, int, bool> _onCommitAngle;
    private readonly Action<int, bool> _onToggleEnabled;
    private bool _suppressCallbacks;

    public int Index { get; }
    public int ChannelNumber => Index + 1;
    public string Code { get; }

    [ObservableProperty]
    private int _angle = 90;

    [ObservableProperty]
    private string _angleText = "90";

    [ObservableProperty]
    private double _sliderValue = 90.0;

    [ObservableProperty]
    private double _progress = 0.5;

    [ObservableProperty]
    private bool _isEnabled = true;

    [ObservableProperty]
    private bool _isCurrentFocus;

    public string AngleBadgeText => $"{Angle}°";

    public ServoChannelViewModel(
        int index,
        bool initialEnabled,
        Action<int, int, bool> onCommitAngle,
        Action<int, bool> onToggleEnabled)
    {
        Index = index;
        Code = $"S{index + 1:D2}";
        _isEnabled = initialEnabled;
        _onCommitAngle = onCommitAngle;
        _onToggleEnabled = onToggleEnabled;
    }

    partial void OnSliderValueChanged(double value)
    {
        int rounded = Math.Clamp((int)Math.Round(value), 0, 180);
        if (_suppressCallbacks)
            return;

        _suppressCallbacks = true;
        Angle = rounded;
        AngleText = rounded.ToString();
        Progress = rounded / 180.0;
        OnPropertyChanged(nameof(AngleBadgeText));
        _suppressCallbacks = false;
    }

    partial void OnIsEnabledChanged(bool value)
    {
        if (_suppressCallbacks)
            return;
        _onToggleEnabled(Index, value);
    }

    /// <summary>
    /// 从 MCU 状态帧或批量归位静默更新 UI 状态，不反向下发指令。
    /// </summary>
    public void SyncFromState(int angle, bool isEnabled, bool isFocus)
    {
        int clamped = Math.Clamp(angle, 0, 180);
        _suppressCallbacks = true;
        Angle = clamped;
        SliderValue = clamped;
        AngleText = clamped.ToString();
        Progress = clamped / 180.0;
        IsEnabled = isEnabled;
        IsCurrentFocus = isFocus;
        OnPropertyChanged(nameof(AngleBadgeText));
        _suppressCallbacks = false;
    }

    /// <summary>
    /// 用户结束滑块拖拽、点击轨道或按方向键后提交当前角度。
    /// </summary>
    public void CommitSliderAngle()
    {
        if (!IsEnabled)
            return;
        int val = Math.Clamp((int)Math.Round(SliderValue), 0, 180);
        SetAngleInternal(val, sendNow: true);
    }

    /// <summary>
    /// 解析文本框输入并提交角度，不合法则回滚到当前合法角度。
    /// </summary>
    public void CommitTextAngle()
    {
        if (!IsEnabled)
        {
            AngleText = Angle.ToString();
            return;
        }

        if (int.TryParse(AngleText?.Trim(), out int parsed))
        {
            SetAngleInternal(Math.Clamp(parsed, 0, 180), sendNow: true);
        }
        else
        {
            _suppressCallbacks = true;
            AngleText = Angle.ToString();
            _suppressCallbacks = false;
        }
    }

    public void SetManualAngleSilently(int angle)
    {
        int clamped = Math.Clamp(angle, 0, 180);
        _suppressCallbacks = true;
        Angle = clamped;
        SliderValue = clamped;
        AngleText = clamped.ToString();
        Progress = clamped / 180.0;
        OnPropertyChanged(nameof(AngleBadgeText));
        _suppressCallbacks = false;
    }

    private void SetAngleInternal(int val, bool sendNow)
    {
        SetManualAngleSilently(val);
        _onCommitAngle(Index, val, sendNow);
    }

    [RelayCommand]
    private void StepDown()
    {
        if (!IsEnabled)
            return;
        SetAngleInternal(Math.Clamp(Angle - 1, 0, 180), sendNow: true);
    }

    [RelayCommand]
    private void StepUp()
    {
        if (!IsEnabled)
            return;
        SetAngleInternal(Math.Clamp(Angle + 1, 0, 180), sendNow: true);
    }
}
