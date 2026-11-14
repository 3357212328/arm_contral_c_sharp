using System.IO;
using ArmControlSuite.Models;
using ArmControlSuite.Services;
using ArmControlSuite.ViewModels;

namespace ArmControlSuite.Tests;

[TestClass]
public class MainViewModelTests
{
    private sealed class FakeUiService : IUiService
    {
        public bool? LastThemeDark { get; private set; }
        public string? LastCopiedText { get; private set; }

        public void InvokeOnUiThread(Action action) => action();
        public void ApplyTheme(bool isDark) => LastThemeDark = isDark;
        public void CopyTextToClipboard(string text) => LastCopiedText = text;
        public void OpenFolderInExplorer(string path) { }
        public Task<(BleCharacteristicItem? WriteChar, BleCharacteristicItem? NotifyChar)> ShowBleCharSelectorAsync(
            IReadOnlyList<BleCharacteristicItem> writeChars,
            IReadOnlyList<BleCharacteristicItem> notifyChars,
            string? defaultWriteUuid,
            string? defaultNotifyUuid) => Task.FromResult<(BleCharacteristicItem?, BleCharacteristicItem?)>((null, null));
        public Task ShowSppPairingGuideAsync() => Task.CompletedTask;
        public Task<bool> ShowConfirmDialogAsync(string title, string message, string confirmText, bool isDanger)
            => Task.FromResult(true);
        public Task<string?> ShowSaveLogFileDialogAsync(string defaultFileName)
            => Task.FromResult<string?>(null);
    }

    private string _tempDir = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ArmVmTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }
        catch { }
    }

    [TestMethod]
    public void ViewModel_Initializes16ChannelsAndPersistsThresholdChanges()
    {
        var settingsSvc = new SettingsService(_tempDir);
        var ui = new FakeUiService();
        var vm = new MainViewModel(settingsSvc, new DeviceDiscoveryService(), ui);

        Assert.AreEqual(16, vm.ServoChannels.Count);
        Assert.AreEqual(8, vm.LeftColumnServos.Count);
        Assert.AreEqual(8, vm.RightColumnServos.Count);
        Assert.AreEqual("活动通道: 7 / 16", vm.ActiveServoSummaryText);

        vm.TempThresholdText = "25";
        vm.HumiThresholdText = "70";
        vm.SaveThresholdsCommand.Execute(null);

        var reloaded = settingsSvc.Load();
        Assert.AreEqual(25, reloaded.ThresholdTemp);
        Assert.AreEqual(70, reloaded.ThresholdHumi);
    }

    [TestMethod]
    public void ViewModel_AdjustDelayAndInsertPositionClampCorrectly()
    {
        var settingsSvc = new SettingsService(_tempDir);
        var vm = new MainViewModel(settingsSvc, new DeviceDiscoveryService(), new FakeUiService());

        vm.DelaySecondsText = "0";
        vm.AdjustDelayCommand.Execute("-5");
        Assert.AreEqual("0", vm.DelaySecondsText);

        vm.DelaySecondsText = "599";
        vm.AdjustDelayCommand.Execute("10");
        Assert.AreEqual("600", vm.DelaySecondsText);

        vm.InsertPositionText = "1";
        vm.AdjustInsertPositionCommand.Execute("-1");
        Assert.AreEqual("1", vm.InsertPositionText);

        vm.InsertPositionText = "50";
        vm.AdjustInsertPositionCommand.Execute("5");
        Assert.AreEqual("50", vm.InsertPositionText);
    }

    [TestMethod]
    public void ViewModel_ThemeAndSpeedMultiplierPersist()
    {
        var settingsSvc = new SettingsService(_tempDir);
        var ui = new FakeUiService();
        var vm = new MainViewModel(settingsSvc, new DeviceDiscoveryService(), ui);

        vm.ToggleThemeCommand.Execute(null);
        Assert.IsTrue(vm.IsDarkMode);
        Assert.AreEqual(true, ui.LastThemeDark);

        vm.SetSpeedCommand.Execute("2.0");
        Assert.IsTrue(vm.IsSpeed20x);

        var reloaded = settingsSvc.Load();
        Assert.IsTrue(reloaded.DarkMode);
        Assert.AreEqual(2.0, reloaded.SequenceSpeedMultiplier, 0.001);
    }

    [TestMethod]
    public void ViewModel_ServoMaskToggleUpdatesActiveCountAndPersists()
    {
        var settingsSvc = new SettingsService(_tempDir);
        var vm = new MainViewModel(settingsSvc, new DeviceDiscoveryService(), new FakeUiService());

        // 默认前 7 路启用 (0x7F)，再启用第 8 路 (S08)
        vm.ServoChannels[7].IsEnabled = true;
        Assert.AreEqual("活动通道: 8 / 16", vm.ActiveServoSummaryText);
        Assert.AreEqual("8 / 16 路", vm.DashActiveCountText);

        var reloaded = settingsSvc.Load();
        Assert.AreEqual(0xFF, reloaded.ServosDefaultMask);
    }
}
