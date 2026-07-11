namespace Bao1702.Transport.UsbPrinter;

/// <summary>Quick diagnostic helper — call from immediate window or test harness.</summary>
public static class DiagnosticProbe
{
    public static string RunDiagnostic()
    {
        var lines = new List<string> { "=== USB Printer Transport Diagnostic ===" };

        try
        {
            var snapshot = UsbPrinterTransportProbe.Capture();
            lines.Add($"WMI devices found: {snapshot.Devices.Count}");
            foreach (var d in snapshot.Devices)
            {
                lines.Add($"  Device: {d.FriendlyName}");
                lines.Add($"    InstanceId: {d.InstanceId}");
                lines.Add($"    Service: {d.Service}");
                lines.Add($"    Status: {d.Status}");
                var syntheticPath = $"\\\\?\\{d.InstanceId.ToLowerInvariant().Replace('\\', '#')}#{{28d78fad-5a12-11d1-ae5b-0000f803a8c2}}";
                lines.Add($"    Synthetic path: {syntheticPath}");
            }

            lines.Add($"Interface probes: {snapshot.InterfaceProbes.Count}");
            foreach (var p in snapshot.InterfaceProbes)
            {
                lines.Add($"  Path: {p.DevicePath}");
                lines.Add($"    MatchesTarget: {p.MatchesTarget}");
                lines.Add($"    OpenSucceeded: {p.OpenSucceeded}");
                lines.Add($"    Summary: {p.Summary}");
                if (p.Error is not null) lines.Add($"    Error: {p.Error}");
            }

            lines.Add("");
            lines.Add("--- Factory Enumeration ---");
            var factory = new UsbPrinterTransportFactory();
            var endpoints = factory.EnumerateAsync().GetAwaiter().GetResult();
            lines.Add($"Endpoints returned: {endpoints.Count}");
            foreach (var ep in endpoints)
            {
                lines.Add($"  Endpoint: {ep.DisplayName}");
                lines.Add($"    Id: {ep.Id}");
                lines.Add($"    Type: {ep.TransportType}");
                foreach (var prop in ep.Properties)
                {
                    lines.Add($"    [{prop.Key}] = {prop.Value}");
                }
            }

            if (endpoints.Count > 0)
            {
                lines.Add("");
                lines.Add("--- Connection Test ---");
                var conn = factory.OpenAsync(endpoints[0]).GetAwaiter().GetResult();
                try
                {
                    conn.ConnectAsync().GetAwaiter().GetResult();
                    lines.Add($"ConnectAsync succeeded! IsOpen={conn.IsOpen}");
                    conn.DisposeAsync().AsTask().GetAwaiter().GetResult();
                    lines.Add("Disposed OK.");
                }
                catch (Exception ex)
                {
                    lines.Add($"ConnectAsync FAILED: {ex.GetType().Name}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            lines.Add($"DIAGNOSTIC EXCEPTION: {ex}");
        }

        return string.Join(Environment.NewLine, lines);
    }
}
