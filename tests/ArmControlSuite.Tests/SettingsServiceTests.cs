using System.IO;
using System.Text.Json;
using ArmControlSuite.Models;
using ArmControlSuite.Services;

namespace ArmControlSuite.Tests;

[TestClass]
public class SettingsServiceTests
{
    private string _tempDir = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ArmControlSuiteTests_" + Guid.NewGuid().ToString("N"));
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
    public void Load_CreatesDefaultConfigWhenMissing()
    {
        var svc = new SettingsService(_tempDir);
        var loaded = svc.Load();

        Assert.IsTrue(File.Exists(svc.SettingsFilePath));
        Assert.AreEqual(false, loaded.DarkMode);
        Assert.AreEqual(true, loaded.Sync);
        Assert.AreEqual(0x7F, loaded.ServosDefaultMask);
        Assert.AreEqual("JDY-29 (FFE0/FFE1)", loaded.BlePreset);
    }

    [TestMethod]
    public void Load_BackfillsMissingKeysAndPreservesExisting()
    {
        var svc = new SettingsService(_tempDir);
        File.WriteAllText(svc.SettingsFilePath, """
        {
          "general/dark_mode": true,
          "serial/last_port": "COM9",
          "thresholds/temp": 55
        }
        """);

        var loaded = svc.Load();
        Assert.IsTrue(loaded.DarkMode);
        Assert.AreEqual("COM9", loaded.SerialLastPort);
        Assert.AreEqual(55, loaded.ThresholdTemp);
        Assert.AreEqual(FirmwareConstants.DefaultHumiThreshold, loaded.ThresholdHumi);

        // 确认缺失字段已自动回写落盘
        string updatedJson = File.ReadAllText(svc.SettingsFilePath);
        using var doc = JsonDocument.Parse(updatedJson);
        Assert.IsTrue(doc.RootElement.TryGetProperty("thresholds/humi", out _));
        Assert.IsTrue(doc.RootElement.TryGetProperty("ble_chars", out _));
    }

    [TestMethod]
    public void SaveAndLoad_RoundTripsBleCharsAndChineseText()
    {
        var svc = new SettingsService(_tempDir);
        var settings = AppSettings.CreateDefault();
        settings.DarkMode = true;
        settings.BlePreset = "自定义 (手动选择)";
        settings.BleChars["AA:BB:CC:DD:EE:FF"] = new BleSavedChars
        {
            WriteUuid = "0000ffe1-0000-1000-8000-00805f9b34fb",
            NotifyUuid = "0000ffe1-0000-1000-8000-00805f9b34fb"
        };

        Assert.IsTrue(svc.Save(settings));

        string rawJson = File.ReadAllText(svc.SettingsFilePath);
        Assert.IsTrue(rawJson.Contains("自定义 (手动选择)", StringComparison.Ordinal));

        var reloaded = svc.Load();
        Assert.IsTrue(reloaded.DarkMode);
        Assert.AreEqual("自定义 (手动选择)", reloaded.BlePreset);
        Assert.IsTrue(reloaded.BleChars.ContainsKey("AA:BB:CC:DD:EE:FF"));
    }

    [TestMethod]
    public void Load_RecoversFromCorruptedJson()
    {
        var svc = new SettingsService(_tempDir);
        File.WriteAllText(svc.SettingsFilePath, "{ broken json !!!");

        var loaded = svc.Load();
        Assert.AreEqual(FirmwareConstants.DefaultTempThreshold, loaded.ThresholdTemp);
        Assert.AreEqual(FirmwareConstants.DefaultLogHeight, loaded.LogHeight);
    }
}
