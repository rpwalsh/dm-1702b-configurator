using System.IO.Ports;
using Bao1702.Transport.Abstractions;

namespace Bao1702.Transport.Serial;

/// <summary>
/// Enumerates Windows COM ports for radio transport probing.
/// Enumeration is intentionally conservative and does not claim that a port is a Bao1702-family radio.
/// </summary>
public static class SerialPortEnumerator
{
    public const int DefaultBaudRate = 115200;

    public static IReadOnlyList<TransportEndpoint> EnumeratePorts()
    {
        return SerialPort.GetPortNames()
            .OrderBy(static portName => portName, StringComparer.OrdinalIgnoreCase)
            .Select(CreateEndpoint)
            .ToArray();
    }

    public static TransportEndpoint CreateEndpoint(string portName)
    {
        if (string.IsNullOrWhiteSpace(portName))
        {
            throw new ArgumentException("Port name is required.", nameof(portName));
        }

        var normalizedPortName = portName.Trim();
        return new TransportEndpoint(
            $"serial://{normalizedPortName}",
            $"Serial Port {normalizedPortName}",
            TransportType.Serial,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["PortName"] = normalizedPortName,
                ["BaudRate"] = DefaultBaudRate.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["Parity"] = Parity.None.ToString(),
                ["DataBits"] = "8",
                ["StopBits"] = StopBits.One.ToString(),
            });
    }
}
