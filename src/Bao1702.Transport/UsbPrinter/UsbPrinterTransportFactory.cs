using Bao1702.Transport.Abstractions;

namespace Bao1702.Transport.UsbPrinter;

/// <summary>
/// Transport factory that discovers and connects to DM-1702 radios using
/// the USB printer-class interface (VID 0483 / PID 5780).
/// </summary>
public sealed class UsbPrinterTransportFactory(string vid = "0483", string productId = "5780") : ITransportFactory
{
    private readonly string _vid = vid;
    private readonly string _productId = productId;

    public string Name => "USB printer-class transport probe";

    public TransportType TransportType => TransportType.UsbPrinter;

    public TransportCapabilities Capabilities => TransportCapabilities.Enumerate | TransportCapabilities.Connect | TransportCapabilities.PacketCapture;

    public Task<IReadOnlyList<TransportEndpoint>> EnumerateAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var snapshot = UsbPrinterTransportProbe.Capture(_vid, _productId);

        // Find the best openable interface path discovered by the SetupDi probe.
        var bestInterfacePath = snapshot.InterfaceProbes
            .Where(static probe => probe.MatchesTarget && probe.OpenSucceeded)
            .Select(static probe => probe.DevicePath)
            .FirstOrDefault();

        if (snapshot.Devices.Count > 0)
        {
            // WMI found devices — create endpoints, but prefer the real SetupDi path
            // over the synthetic candidate path when available.
            var endpoints = snapshot.Devices.Select(device =>
            {
                var endpoint = UsbPrinterTransportProbe.CreateEndpoint(device);
                if (bestInterfacePath is not null)
                {
                    var upgraded = new Dictionary<string, string>(endpoint.Properties, StringComparer.OrdinalIgnoreCase)
                    {
                        ["ProbeOpenPath"] = bestInterfacePath,
                    };
                    return endpoint with { Properties = upgraded };
                }
                return endpoint;
            }).ToArray();

            return Task.FromResult<IReadOnlyList<TransportEndpoint>>(endpoints);
        }

        // WMI found nothing — fall back to interface probe results.
        if (bestInterfacePath is not null)
        {
            var fallbackEndpoint = new TransportEndpoint(
                $"usbprint://interface/{_vid}:{_productId}",
                $"DM-1702 (USB printer interface VID {_vid} PID {_productId})",
                TransportType.UsbPrinter,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["ProbeOpenPath"] = bestInterfacePath,
                    ["Source"] = "SetupDi interface probe (WMI fallback)",
                });
            return Task.FromResult<IReadOnlyList<TransportEndpoint>>(new[] { fallbackEndpoint });
        }

        return Task.FromResult<IReadOnlyList<TransportEndpoint>>(Array.Empty<TransportEndpoint>());
    }

    public Task<ITransportConnection> OpenAsync(TransportEndpoint endpoint, TransportTimeouts? timeouts = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(endpoint);
        ITransportConnection connection = new UsbPrinterTransportConnection(endpoint, timeouts ?? TransportTimeouts.Default);
        return Task.FromResult(connection);
    }
}
