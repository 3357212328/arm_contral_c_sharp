using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using ArmControlSuite.Models;

namespace ArmControlSuite.Services;

/// <summary>
/// 原子读写本地 JSON 配置文件 (arm_suite_config.json)。
/// </summary>
public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNameCaseInsensitive = false
    };

    private static readonly string[] ExpectedKeys =
    [
        "general/dark_mode",
        "general/sync",
        "ui/last_tab",
        "window/width",
        "window/height",
        "connection/mode",
        "serial/last_port",
        "serial/last_baud",
        "ble/preset",
        "ble/last_device_text",
        "ble/last_device_addr",
        "ble_chars",
        "spp/last_addr",
        "spp/last_port",
        "sequence/speed_multiplier",
        "sequence/last_delay",
        "thresholds/temp",
        "thresholds/humi",
        "servos/default_mask",
        "log/height",
        "log/collapsed",
        "log/auto_scroll"
    ];

    public string SettingsFilePath { get; }
    public string AppDirectory { get; }

    public SettingsService(string? appDirectory = null)
    {
        AppDirectory = string.IsNullOrWhiteSpace(appDirectory)
            ? AppContext.BaseDirectory
            : Path.GetFullPath(appDirectory);
        SettingsFilePath = Path.Combine(AppDirectory, FirmwareConstants.SettingsFileName);
    }

    public AppSettings Load()
    {
        var settings = AppSettings.CreateDefault();
        bool needSave = !File.Exists(SettingsFilePath);

        if (File.Exists(SettingsFilePath))
        {
            try
            {
                string json = File.ReadAllText(SettingsFilePath);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    foreach (string key in ExpectedKeys)
                    {
                        if (!doc.RootElement.TryGetProperty(key, out _))
                        {
                            needSave = true;
                            break;
                        }
                    }

                    var deserialized = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
                    if (deserialized != null)
                    {
                        deserialized.BleChars ??= new Dictionary<string, BleSavedChars>(StringComparer.OrdinalIgnoreCase);
                        settings = deserialized;
                    }
                    else
                    {
                        needSave = true;
                    }
                }
                else
                {
                    needSave = true;
                }
            }
            catch
            {
                needSave = true;
            }
        }

        if (needSave)
        {
            Save(settings);
        }

        return settings;
    }

    public bool Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(AppDirectory);
            string tmpPath = SettingsFilePath + ".tmp";
            string json = JsonSerializer.Serialize(settings, JsonOptions);
            File.WriteAllText(tmpPath, json, System.Text.Encoding.UTF8);
            File.Move(tmpPath, SettingsFilePath, overwrite: true);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public AppSettings ResetToDefault()
    {
        var defaults = AppSettings.CreateDefault();
        Save(defaults);
        return defaults;
    }
}
