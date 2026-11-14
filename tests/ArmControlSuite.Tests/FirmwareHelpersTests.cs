using System.Runtime.InteropServices;
using ArmControlSuite.Models;
using ArmControlSuite.Services;

namespace ArmControlSuite.Tests;

[TestClass]
public class FirmwareHelpersTests
{
    [TestMethod]
    public void ArmState_ComputesActiveAndFocusServoModuloCorrectly()
    {
        // Mask = 0b0001_0101 -> 启用通道索引 0 (S01), 2 (S03), 4 (S05)
        ushort mask = 0b10101;
        var active = ArmState.ComputeActiveIndices(mask);
        CollectionAssert.AreEqual(new[] { 0, 2, 4 }, active);

        Assert.AreEqual(0, ArmState.ResolveFocusServoIndex(mask, 0));
        Assert.AreEqual(2, ArmState.ResolveFocusServoIndex(mask, 1));
        Assert.AreEqual(4, ArmState.ResolveFocusServoIndex(mask, 2));
        Assert.AreEqual(0, ArmState.ResolveFocusServoIndex(mask, 3));
        Assert.AreEqual(2, ArmState.ResolveFocusServoIndex(mask, 4));
        Assert.AreEqual(-1, ArmState.ResolveFocusServoIndex(0, 0));
    }

    [TestMethod]
    public void IsSameUuid_IgnoresCaseAndHyphens()
    {
        Assert.IsTrue(FirmwareConstants.IsSameUuid(
            "0000FFE1-0000-1000-8000-00805F9B34FB",
            "0000ffe100001000800000805f9b34fb"));
        Assert.IsFalse(FirmwareConstants.IsSameUuid(
            "0000ffe1-0000-1000-8000-00805f9b34fb",
            "0000ffe2-0000-1000-8000-00805f9b34fb"));
        Assert.IsFalse(FirmwareConstants.IsSameUuid("", "0000ffe1"));
    }

    [TestMethod]
    public void RfcommSocket_MacParsingAndStructSizeAreExact()
    {
        Assert.AreEqual("AA:BB:CC:DD:EE:FF", RfcommWin32Socket.NormalizeMacAddress("aa-bb-cc-dd-ee-ff"));
        Assert.IsNull(RfcommWin32Socket.NormalizeMacAddress("AA:BB:CC:DD:EE"));
        Assert.IsNull(RfcommWin32Socket.NormalizeMacAddress("ZZ:BB:CC:DD:EE:FF"));

        ulong parsed = RfcommWin32Socket.ParseBluetoothAddress("00:11:22:33:44:55");
        Assert.AreEqual(0x0000001122334455UL, parsed);

        // Windows SOCKADDR_BTH (Pack=1): 2 (family) + 8 (btAddr) + 16 (GUID) + 4 (port) = 30 bytes
        Assert.AreEqual(30, Marshal.SizeOf<RfcommWin32Socket.SockAddrBth>());
    }
}
