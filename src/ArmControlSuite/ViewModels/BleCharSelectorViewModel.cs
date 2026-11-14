using ArmControlSuite.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ArmControlSuite.ViewModels;

public partial class BleCharSelectorViewModel : ObservableObject
{
    public IReadOnlyList<BleCharacteristicItem> WriteCharacteristics { get; }
    public IReadOnlyList<BleCharacteristicItem> NotifyCharacteristics { get; }

    [ObservableProperty]
    private BleCharacteristicItem? _selectedWriteCharacteristic;

    [ObservableProperty]
    private BleCharacteristicItem? _selectedNotifyCharacteristic;

    [ObservableProperty]
    private string? _errorMessage;

    public BleCharSelectorViewModel(
        IReadOnlyList<BleCharacteristicItem> writeChars,
        IReadOnlyList<BleCharacteristicItem> notifyChars,
        string? defaultWriteUuid,
        string? defaultNotifyUuid)
    {
        WriteCharacteristics = writeChars;
        NotifyCharacteristics = notifyChars;

        SelectedWriteCharacteristic = writeChars.FirstOrDefault(c => FirmwareConstants.IsSameUuid(c.Uuid, defaultWriteUuid))
            ?? writeChars.FirstOrDefault();
        SelectedNotifyCharacteristic = notifyChars.FirstOrDefault(c => FirmwareConstants.IsSameUuid(c.Uuid, defaultNotifyUuid))
            ?? notifyChars.FirstOrDefault();
    }

    public bool ValidateSelection()
    {
        if (SelectedWriteCharacteristic == null || SelectedNotifyCharacteristic == null)
        {
            ErrorMessage = "请同时选择有效的 TX (写入) 与 RX (通知) 特性。";
            return false;
        }
        ErrorMessage = null;
        return true;
    }
}
