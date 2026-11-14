using System.Text;
using ArmControlSuite.Models;
using ArmControlSuite.Services;

namespace ArmControlSuite.Tests;

[TestClass]
public class ProtocolParserTests
{
    [TestMethod]
    public void FeedBytes_ParsesCompleteStatusPacketAndUpdatesState()
    {
        var parser = new ProtocolParser();
        ArmState? latest = null;
        List<string> logs = [];
        parser.StateUpdated += s => latest = s;
        parser.LogMessage += logs.Add;

        string payload = "$MODE:3;CUR_S_IDX:2;ANGS:0,15,30,45,60,75,90,105,120,135,150,165,180,200,-10,95;MASK:255;TEMP:31;HUMI:62;R_STEPS:9;P_STAT:1#";
        parser.FeedBytes(Encoding.Latin1.GetBytes(payload));

        Assert.IsNotNull(latest);
        Assert.AreEqual(3, latest.Mode);
        Assert.AreEqual(2, latest.CurrentServoIndex);
        Assert.AreEqual((ushort)255, latest.Mask);
        Assert.AreEqual(31, latest.Temp);
        Assert.AreEqual(62, latest.Humi);
        Assert.AreEqual(9, latest.RecordedSteps);
        Assert.AreEqual(1, latest.PlaybackStatus);

        // 验证 200 -> 180, -10 -> 0 的限幅保护
        Assert.AreEqual(0, latest.Angles[0]);
        Assert.AreEqual(180, latest.Angles[12]);
        Assert.AreEqual(180, latest.Angles[13]);
        Assert.AreEqual(0, latest.Angles[14]);
        Assert.AreEqual(95, latest.Angles[15]);

        Assert.AreEqual(1, logs.Count);
        Assert.IsTrue(logs[0].StartsWith("MCU -> PC (Packet):", StringComparison.Ordinal));
    }

    [TestMethod]
    public void FeedBytes_HandlesFragmentedChunksAndNestedDollar()
    {
        var parser = new ProtocolParser();
        List<ArmState> states = [];
        List<string> logs = [];
        parser.StateUpdated += states.Add;
        parser.LogMessage += logs.Add;

        string chunk1 = "garbage$stale_prefix$MODE:5;TEMP:";
        string chunk2 = "27;HUMI:48;R_STEPS:-3#\r\nOK READY\r\n$partial_only\n";

        parser.FeedBytes(Encoding.Latin1.GetBytes(chunk1));
        Assert.AreEqual(0, states.Count);

        parser.FeedBytes(Encoding.Latin1.GetBytes(chunk2));
        Assert.AreEqual(1, states.Count);
        Assert.AreEqual(5, states[0].Mode);
        Assert.AreEqual(27, states[0].Temp);
        Assert.AreEqual(48, states[0].Humi);
        Assert.AreEqual(0, states[0].RecordedSteps);

        // 应包含 1 条 Packet (使用最后一个 $ 起算) 与 1 条 Response ("OK READY")，忽略 "$partial_only"
        Assert.AreEqual(2, logs.Count);
        Assert.IsTrue(logs[0].Contains("MODE:5;TEMP:27;HUMI:48;R_STEPS:-3", StringComparison.Ordinal));
        Assert.AreEqual("MCU -> PC (Response): OK READY", logs[1]);
    }

    [TestMethod]
    public void FeedBytes_IgnoresMalformedAngleArrayCount()
    {
        var parser = new ProtocolParser();
        ArmState? latest = null;
        parser.StateUpdated += s => latest = s;

        parser.FeedBytes(Encoding.Latin1.GetBytes("$ANGS:10,20,30#"));

        Assert.IsNotNull(latest);
        // 长度不为 16 时保持默认 90°
        foreach (int ang in latest.Angles)
        {
            Assert.AreEqual(90, ang);
        }
    }

    [TestMethod]
    public void EncodeCommand_FormatsTrimmedAsciiWithCrlf()
    {
        byte[] bytes = ProtocolParser.EncodeCommand("  angle 1 120  ");
        Assert.AreEqual("angle 1 120\r\n", Encoding.ASCII.GetString(bytes));
        Assert.AreEqual(0, ProtocolParser.EncodeCommand("   ").Length);
    }

    [TestMethod]
    public void ClassifyLog_CategorizesCorrectly()
    {
        Assert.AreEqual((LogCategory.Tx, "TX "), ProtocolParser.ClassifyLog("PC -> MCU (Serial): status"));
        Assert.AreEqual((LogCategory.Packet, "PKT"), ProtocolParser.ClassifyLog("MCU -> PC (Packet): MODE:0"));
        Assert.AreEqual((LogCategory.Rx, "RX "), ProtocolParser.ClassifyLog("MCU -> PC (Response): OK"));
        Assert.AreEqual((LogCategory.Error, "ERR"), ProtocolParser.ClassifyLog("串口写入错误: Port closed"));
        Assert.AreEqual((LogCategory.System, "SYS"), ProtocolParser.ClassifyLog("串口已连接 -> COM7"));
    }
}
