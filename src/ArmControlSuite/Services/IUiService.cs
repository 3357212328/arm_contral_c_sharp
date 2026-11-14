using ArmControlSuite.Models;

namespace ArmControlSuite.Services;

public interface IUiService
{
    void InvokeOnUiThread(Action action);
    void ApplyTheme(bool isDark);
    void CopyTextToClipboard(string text);
    void OpenFolderInExplorer(string path);
    Task<(BleCharacteristicItem? WriteChar, BleCharacteristicItem? NotifyChar)> ShowBleCharSelectorAsync(
        IReadOnlyList<BleCharacteristicItem> writeChars,
        IReadOnlyList<BleCharacteristicItem> notifyChars,
        string? defaultWriteUuid,
        string? defaultNotifyUuid);
    Task ShowSppPairingGuideAsync();
    Task<bool> ShowConfirmDialogAsync(string title, string message, string confirmText, bool isDanger);
    Task<string?> ShowSaveLogFileDialogAsync(string defaultFileName);
}
