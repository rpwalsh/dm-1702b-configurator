using System.Management;
using System.Diagnostics;
using Bao1702.Transport.Abstractions;

namespace Bao1702.Transport.UsbPrinter;

/// <summary>WMI-captured device descriptor for a USB printer-class interface.</summary>
public sealed record UsbPrinterDeviceInfo(
    string InstanceId,
    string FriendlyName,
    string Service,
    string Manufacturer,
    string Status,
    string ClassName,
    string ClassGuid,
    string LocationInfo,
    IReadOnlyList<string> HardwareIds,
    IReadOnlyList<string> CompatibleIds,
    IReadOnlyList<string> Children);

public sealed record UsbPrinterProbeSnapshot(
    string Vid,
    string ProductId,
    IReadOnlyList<UsbPrinterDeviceInfo> Devices,
    IReadOnlyList<UsbPrinterInterfaceProbeResult> InterfaceProbes,
    IReadOnlyList<string> Notes);

public static class UsbPrinterTransportProbe
{
    public static UsbPrinterProbeSnapshot Capture(string vid = "0483", string productId = "5780")
    {
        if (!OperatingSystem.IsWindows())
        {
            return new UsbPrinterProbeSnapshot(vid, productId, [], [], ["USB printer transport probing is only supported on Windows."]);
        }

        var matchPrefix = $"USB\\VID_{vid}&PID_{productId}";
        var devices = new List<UsbPrinterDeviceInfo>();

        try
        {
            using var searcher = new ManagementObjectSearcher(
                "root\\CIMV2",
                "SELECT * FROM Win32_PnPEntity");

            foreach (ManagementObject entity in searcher.Get())
            {
                var instanceId = ReadString(entity, "PNPDeviceID");
                if (!instanceId.StartsWith(matchPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var compatibleIds = ReadStringArray(entity, "CompatibleID");
                devices.Add(new UsbPrinterDeviceInfo(
                    instanceId,
                    ReadString(entity, "Name"),
                    ReadString(entity, "Service"),
                    ReadString(entity, "Manufacturer"),
                    ReadString(entity, "Status"),
                    ReadString(entity, "PNPClass"),
                    ReadString(entity, "ClassGuid"),
                    ReadString(entity, "LocationInformation"),
                    ReadStringArray(entity, "HardwareID"),
                    compatibleIds,
                    ReadStringArray(entity, "Children")));
            }
        }
        catch (ManagementException ex)
        {
            devices.AddRange(CaptureWithPnPUtil(vid, productId));
            if (devices.Count == 0)
            {
                return new UsbPrinterProbeSnapshot(vid, productId, [], [], [$"WMI query failed: {ex.Message}"]);
            }
        }

        var interfaceProbes = UsbPrinterInterfaceProbe.Capture(vid, productId).ToList();
        foreach (var candidateProbe in UsbPrinterInterfaceProbe.ProbeCandidatePaths(BuildCandidatePaths(devices), vid, productId))
        {
            if (!interfaceProbes.Any(existing => string.Equals(existing.DevicePath, candidateProbe.DevicePath, StringComparison.OrdinalIgnoreCase)))
            {
                interfaceProbes.Add(candidateProbe);
            }
        }
        var notes = new List<string>();
        if (devices.Count == 0)
        {
            notes.Add("No matching USB VID/PID device is currently visible via Win32_PnPEntity.");
        }
        else
        {
            if (devices.Any(static device => string.Equals(device.Service, "usbprint", StringComparison.OrdinalIgnoreCase)))
            {
                notes.Add("At least one matching device is bound to the usbprint service.");
            }

            if (devices.Any(static device => device.CompatibleIds.Any(id => id.Contains("Class_07", StringComparison.OrdinalIgnoreCase))))
            {
                notes.Add("Matching device reports printer-class compatible IDs (USB class 07)." );
            }

            if (interfaceProbes.Any(static probe => probe.MatchesTarget && probe.OpenSucceeded))
            {
                notes.Add("At least one USB printer interface path matches the target VID/PID and can be opened read-only with CreateFile.");
            }
        }

        return new UsbPrinterProbeSnapshot(
            vid,
            productId,
            devices.OrderBy(static device => device.InstanceId, StringComparer.OrdinalIgnoreCase).ToArray(),
            interfaceProbes.OrderBy(static probe => probe.DevicePath, StringComparer.OrdinalIgnoreCase).ToArray(),
            notes);
    }

    public static TransportEndpoint CreateEndpoint(UsbPrinterDeviceInfo device)
    {
        ArgumentNullException.ThrowIfNull(device);

        return new TransportEndpoint(
            $"usbprint://{device.InstanceId}",
            string.IsNullOrWhiteSpace(device.FriendlyName) ? device.InstanceId : device.FriendlyName,
            TransportType.UsbPrinter,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["InstanceId"] = device.InstanceId,
                ["Service"] = device.Service,
                ["Manufacturer"] = device.Manufacturer,
                ["Status"] = device.Status,
                ["Class"] = device.ClassName,
                ["ClassGuid"] = device.ClassGuid,
                ["LocationInfo"] = device.LocationInfo,
                ["HardwareIds"] = string.Join(';', device.HardwareIds),
                ["CompatibleIds"] = string.Join(';', device.CompatibleIds),
                ["Children"] = string.Join(';', device.Children),
                ["ProbeOpenPath"] = BuildPrimaryCandidatePath(device.InstanceId),
            });
    }

    public static string Format(UsbPrinterProbeSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var lines = new List<string>
        {
            "USB printer transport probe",
            "===========================",
            $"VID:PID = {snapshot.Vid}:{snapshot.ProductId}",
            $"Matching devices: {snapshot.Devices.Count}",
        };

        foreach (var note in snapshot.Notes)
        {
            lines.Add($"Note: {note}");
        }

        foreach (var device in snapshot.Devices)
        {
            lines.Add(string.Empty);
            lines.Add($"Device: {device.FriendlyName}");
            lines.Add($"  InstanceId: {device.InstanceId}");
            lines.Add($"  Service: {device.Service}");
            lines.Add($"  Status: {device.Status}");
            lines.Add($"  Manufacturer: {device.Manufacturer}");
            lines.Add($"  Class: {device.ClassName}");
            lines.Add($"  ClassGuid: {device.ClassGuid}");
            lines.Add($"  Location: {device.LocationInfo}");
            lines.Add($"  Hardware IDs: {(device.HardwareIds.Count == 0 ? "<none>" : string.Join(", ", device.HardwareIds))}");
            lines.Add($"  Compatible IDs: {(device.CompatibleIds.Count == 0 ? "<none>" : string.Join(", ", device.CompatibleIds))}");
            lines.Add($"  Children: {(device.Children.Count == 0 ? "<none>" : string.Join(", ", device.Children))}");
        }

        lines.Add(string.Empty);
        lines.Add("Interface probes:");
        foreach (var probe in snapshot.InterfaceProbes)
        {
            lines.Add($"- Match={probe.MatchesTarget} OpenSucceeded={probe.OpenSucceeded} Path={probe.DevicePath}");
            lines.Add($"  {probe.Summary}");
            if (!string.IsNullOrWhiteSpace(probe.Error))
            {
                lines.Add($"  Error: {probe.Error}");
            }
        }

        if (snapshot.InterfaceProbes.Count == 0)
        {
            lines.Add("- none");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string ReadString(ManagementObject entity, string propertyName)
        => entity[propertyName]?.ToString() ?? string.Empty;

    private static IReadOnlyList<string> ReadStringArray(ManagementObject entity, string propertyName)
    {
        return entity[propertyName] switch
        {
            string[] values => values.Where(static value => !string.IsNullOrWhiteSpace(value)).ToArray(),
            Array values => values.Cast<object>().Select(static value => value?.ToString() ?? string.Empty).Where(static value => !string.IsNullOrWhiteSpace(value)).ToArray(),
            string value when !string.IsNullOrWhiteSpace(value) => [value],
            _ => [],
        };
    }

    private static IReadOnlyList<string> BuildCandidatePaths(IReadOnlyList<UsbPrinterDeviceInfo> devices)
    {
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var device in devices)
        {
            if (!string.IsNullOrWhiteSpace(device.InstanceId))
            {
                candidates.Add(BuildPrimaryCandidatePath(device.InstanceId));
            }

            foreach (var child in device.Children)
            {
                var portName = child.Split('&', '#').LastOrDefault(static token => token.StartsWith("USB", StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(portName))
                {
                    candidates.Add($"\\\\.\\{portName}");
                }
            }
        }

        return candidates.ToArray();
    }

    private static string BuildPrimaryCandidatePath(string instanceId)
    {
        var normalized = instanceId.ToLowerInvariant().Replace('\\', '#');
        return $"\\\\?\\{normalized}#{{28d78fad-5a12-11d1-ae5b-0000f803a8c2}}";
    }

    private static IReadOnlyList<UsbPrinterDeviceInfo> CaptureWithPnPUtil(string vid, string productId)
    {
        try
        {
            var startInfo = new ProcessStartInfo("pnputil", "/enum-devices /connected")
            {
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return [];
            }

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(5000);
            return ParsePnPUtilOutput(output, vid, productId);
        }
        catch
        {
            return [];
        }
    }

    private static IReadOnlyList<UsbPrinterDeviceInfo> ParsePnPUtilOutput(string output, string vid, string productId)
    {
        var devices = new List<UsbPrinterDeviceInfo>();
        Dictionary<string, string> current = new(StringComparer.OrdinalIgnoreCase);

        void FlushCurrent()
        {
            if (current.TryGetValue("Instance ID", out var instanceId)
                && instanceId.StartsWith($"USB\\VID_{vid}&PID_{productId}", StringComparison.OrdinalIgnoreCase))
            {
                devices.Add(new UsbPrinterDeviceInfo(
                    instanceId,
                    current.GetValueOrDefault("Device Description", string.Empty),
                    current.GetValueOrDefault("Driver Name", string.Empty),
                    current.GetValueOrDefault("Manufacturer Name", string.Empty),
                    current.GetValueOrDefault("Status", string.Empty),
                    current.GetValueOrDefault("Class Name", string.Empty),
                    current.GetValueOrDefault("Class GUID", string.Empty),
                    string.Empty,
                    [instanceId],
                    [],
                    []));
            }

            current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        using var reader = new StringReader(output);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                FlushCurrent();
                continue;
            }

            var separatorIndex = line.IndexOf(':');
            if (separatorIndex <= 0)
            {
                continue;
            }

            var key = line[..separatorIndex].Trim();
            var value = line[(separatorIndex + 1)..].Trim();
            current[key] = value;
        }

        FlushCurrent();
        return devices;
    }
}
