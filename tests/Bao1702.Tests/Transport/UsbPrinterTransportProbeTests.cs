using Bao1702.Transport.Abstractions;
using Bao1702.Transport.UsbPrinter;

namespace Bao1702.Tests.Transport;

[TestClass]
public sealed class UsbPrinterTransportProbeTests
{
    [TestMethod]
    public void CreateEndpoint_MapsUsbPrinterDeviceProperties()
    {
        var device = new UsbPrinterDeviceInfo(
            "USB\\VID_0483&PID_5780\\00000000050C",
            "STM32 Virtual ComPort in FS Mode",
            "usbprint",
            "Microsoft",
            "OK",
            "USB",
            "{36FC9E60-C465-11CF-8056-444553540000}",
            "Port_#0003.Hub_#0002",
            ["USB\\VID_0483&PID_5780"],
            ["USB\\Class_07&SubClass_01&Prot_02"],
            ["USBPRINT\\UNKNOWNPRINTER\\6&33619F94&0&USB001"]);

        var endpoint = UsbPrinterTransportProbe.CreateEndpoint(device);

        Assert.AreEqual(TransportType.UsbPrinter, endpoint.TransportType);
        Assert.AreEqual("usbprint", endpoint.Properties["Service"]);
        Assert.AreEqual("Port_#0003.Hub_#0002", endpoint.Properties["LocationInfo"]);
    }

    [TestMethod]
    public void Format_IncludesUsbPrintEvidence()
    {
        var snapshot = new UsbPrinterProbeSnapshot(
            "0483",
            "5780",
            [
                new UsbPrinterDeviceInfo(
                    "USB\\VID_0483&PID_5780\\00000000050C",
                    "STM32 Virtual ComPort in FS Mode",
                    "usbprint",
                    "Microsoft",
                    "OK",
                    "USB",
                    "{36FC9E60-C465-11CF-8056-444553540000}",
                    "Port_#0003.Hub_#0002",
                    ["USB\\VID_0483&PID_5780"],
                    ["USB\\Class_07&SubClass_01&Prot_02"],
                    ["USBPRINT\\UNKNOWNPRINTER\\6&33619F94&0&USB001"]),
            ],
            [
                new UsbPrinterInterfaceProbeResult(
                    "\\\\?\\usb#vid_0483&pid_5780#00000000050c#{28d78fad-5a12-11d1-ae5b-0000f803a8c2}",
                    true,
                    true,
                    "CreateFile probe succeeded without sending device traffic.",
                    null),
            ],
            ["At least one matching device is bound to the usbprint service."]);

        var text = UsbPrinterTransportProbe.Format(snapshot);

        StringAssert.Contains(text, "usbprint");
        StringAssert.Contains(text, "USB printer transport probe");
        StringAssert.Contains(text, "USBPRINT\\UNKNOWNPRINTER");
        StringAssert.Contains(text, "OpenSucceeded=True");
    }
}
