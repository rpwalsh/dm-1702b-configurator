using Bao1702.ReverseEngineering.Helpers;

namespace Bao1702.Tests.ReverseEngineering;

[TestClass]
public sealed class UsbPcapRecordParserTests
{
    [TestMethod]
    public void TryParse_ExtractsBulkOutPayloadFromObservedLiveRecord()
    {
        var rawRecord = new byte[]
        {
            0x1B, 0x00, 0x80, 0x09, 0xBE, 0xB2, 0x05, 0xC3, 0xFF, 0xFF, 0x00, 0x00, 0x00, 0x00, 0x09, 0x00,
            0x00, 0x02, 0x00, 0x08, 0x00, 0x01, 0x03, 0x0A, 0x00, 0x00, 0x00,
            0xA5, 0x06, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x07,
        };

        var success = UsbPcapRecordParser.TryParse(rawRecord, out var record, out var error);

        Assert.IsTrue(success, error);
        Assert.IsNotNull(record);
        Assert.AreEqual((ushort)0x001B, record.HeaderLength);
        Assert.AreEqual((byte)0x01, record.EndpointAddress);
        Assert.AreEqual((byte)0x03, record.TransferType);
        Assert.AreEqual((uint)10, record.DataLength);
        Assert.AreEqual(CaptureDirection.HostToDevice, record.Direction);
        CollectionAssert.AreEqual(new byte[] { 0xA5, 0x06, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x07 }, record.Payload);
    }
}
