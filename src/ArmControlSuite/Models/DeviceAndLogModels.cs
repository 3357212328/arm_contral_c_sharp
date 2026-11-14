namespace ArmControlSuite.Models;

public sealed record SerialPortItem(string PortName, string Description)
{
    public string DisplayText =>
        !string.IsNullOrWhiteSpace(Description) && !string.Equals(Description, PortName, StringComparison.OrdinalIgnoreCase)
            ? $"{PortName} ({Description})"
            : PortName;

    public override string ToString() => DisplayText;
}

public sealed record BleDeviceItem(string Address, string Name, int Rssi, ulong RawBluetoothAddress)
{
    public string DisplayText
    {
        get
        {
            if (string.IsNullOrEmpty(Address))
                return Name;
            string rssiSuffix = Rssi != 0 ? $" ({Rssi} dBm)" : string.Empty;
            return $"{Name} [{Address}]{rssiSuffix}";
        }
    }

    public override string ToString() => DisplayText;
}

public sealed record BleCharacteristicItem(
    string Uuid,
    string ServiceUuid,
    string PropertiesText,
    bool CanWrite,
    bool CanWriteWithoutResponse,
    bool CanNotify,
    bool CanIndicate)
{
    public string ShortUuid => Uuid.Length >= 8 ? Uuid[..8] : Uuid;
    public string DisplayText => $"{ShortUuid}... [{PropertiesText}]  ({Uuid})";
    public override string ToString() => DisplayText;
}

public sealed record SppDeviceItem(string Name, string MacAddress)
{
    public string DisplayText => $"{Name} [{MacAddress}]";
    public override string ToString() => DisplayText;
}

public enum LogCategory
{
    Tx,
    Packet,
    Rx,
    Error,
    System
}

public sealed record LogEntry(
    string Timestamp,
    LogCategory Category,
    string TagLabel,
    string Message,
    string PlainLine);
