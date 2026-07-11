using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Bao1702.Transport.UsbPrinter;

/// <summary>Result of probing a single USB printer-class interface for DM-1702 compatibility.</summary>
public sealed record UsbPrinterInterfaceProbeResult(
    string DevicePath,
    bool MatchesTarget,
    bool OpenSucceeded,
    string Summary,
    string? Error = null);

/// <summary>
/// Probes Windows USB printer-class interfaces to locate DM-1702 devices by VID/PID.
/// </summary>
public static class UsbPrinterInterfaceProbe
{
    public static IReadOnlyList<UsbPrinterInterfaceProbeResult> Capture(string vid = "0483", string productId = "5780")
    {
        if (!OperatingSystem.IsWindows())
        {
            return
            [
                new UsbPrinterInterfaceProbeResult(
                    string.Empty,
                    false,
                    false,
                    "USB printer interface probing is only supported on Windows.",
                    "PlatformNotSupported")
            ];
        }

        var results = new List<UsbPrinterInterfaceProbeResult>();
        var interfaceGuid = UsbPrinterNativeMethods.GuidDevInterfaceUsbPrint;
        var deviceInfoSet = UsbPrinterNativeMethods.SetupDiGetClassDevs(
            ref interfaceGuid,
            null,
            IntPtr.Zero,
            UsbPrinterNativeMethods.DIGCF_PRESENT | UsbPrinterNativeMethods.DIGCF_DEVICEINTERFACE);

        if (deviceInfoSet == IntPtr.Zero || deviceInfoSet == UsbPrinterNativeMethods.InvalidHandleValue)
        {
            var error = Marshal.GetLastWin32Error();
            return
            [
                new UsbPrinterInterfaceProbeResult(
                    string.Empty,
                    false,
                    false,
                    "SetupDiGetClassDevs failed for GUID_DEVINTERFACE_USBPRINT.",
                    new Win32Exception(error).Message)
            ];
        }

        try
        {
            var memberIndex = 0u;
            while (true)
            {
                var interfaceData = new UsbPrinterNativeMethods.SpDeviceInterfaceData
                {
                    cbSize = Marshal.SizeOf<UsbPrinterNativeMethods.SpDeviceInterfaceData>(),
                };

                if (!UsbPrinterNativeMethods.SetupDiEnumDeviceInterfaces(deviceInfoSet, IntPtr.Zero, ref interfaceGuid, memberIndex, ref interfaceData))
                {
                    var error = Marshal.GetLastWin32Error();
                    if (error == UsbPrinterNativeMethods.ERROR_NO_MORE_ITEMS)
                    {
                        break;
                    }

                    results.Add(new UsbPrinterInterfaceProbeResult(
                        string.Empty,
                        false,
                        false,
                        $"SetupDiEnumDeviceInterfaces failed at index {memberIndex}.",
                        new Win32Exception(error).Message));
                    break;
                }

                if (!TryGetDevicePath(deviceInfoSet, ref interfaceData, out var devicePath, out var detailError))
                {
                    results.Add(new UsbPrinterInterfaceProbeResult(
                        string.Empty,
                        false,
                        false,
                        $"Failed to retrieve device path for interface index {memberIndex}.",
                        detailError));
                    memberIndex++;
                    continue;
                }

                var matchesTarget = devicePath.Contains($"vid_{vid}", StringComparison.OrdinalIgnoreCase)
                    && devicePath.Contains($"pid_{productId}", StringComparison.OrdinalIgnoreCase);
                var openResult = TryOpenPath(devicePath);
                results.Add(new UsbPrinterInterfaceProbeResult(
                    devicePath,
                    matchesTarget,
                    openResult.OpenSucceeded,
                    openResult.OpenSucceeded
                        ? "CreateFile probe succeeded without sending device traffic."
                        : "CreateFile probe failed.",
                    openResult.Error));

                memberIndex++;
            }
        }
        finally
        {
            _ = UsbPrinterNativeMethods.SetupDiDestroyDeviceInfoList(deviceInfoSet);
        }

        if (results.Count == 0)
        {
            results.Add(new UsbPrinterInterfaceProbeResult(
                string.Empty,
                false,
                false,
                "No USB printer interfaces were enumerated.",
                null));
        }

        return results;
    }

    public static IReadOnlyList<UsbPrinterInterfaceProbeResult> ProbeCandidatePaths(IEnumerable<string> candidatePaths, string vid = "0483", string productId = "5780")
    {
        ArgumentNullException.ThrowIfNull(candidatePaths);

        var results = new List<UsbPrinterInterfaceProbeResult>();
        foreach (var candidatePath in candidatePaths.Where(static path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var matchesTarget = candidatePath.Contains($"vid_{vid}", StringComparison.OrdinalIgnoreCase)
                && candidatePath.Contains($"pid_{productId}", StringComparison.OrdinalIgnoreCase);
            var openResult = TryOpenPath(candidatePath);
            results.Add(new UsbPrinterInterfaceProbeResult(
                candidatePath,
                matchesTarget,
                openResult.OpenSucceeded,
                openResult.OpenSucceeded
                    ? "CreateFile candidate probe succeeded without sending device traffic."
                    : "CreateFile candidate probe failed.",
                openResult.Error));
        }

        return results;
    }

    private static (bool OpenSucceeded, string? Error) TryOpenPath(string devicePath)
    {
        // Validate path format only — do NOT open a real handle during enumeration.
        // Opening and immediately closing the USB device handle can reset the STM32
        // controller, causing the radio to reboot when the real connect follows.
        if (string.IsNullOrWhiteSpace(devicePath) || !devicePath.StartsWith("\\\\?\\", StringComparison.OrdinalIgnoreCase))
        {
            return (false, "Invalid device path format.");
        }

        return (true, null);
    }

    private static bool TryGetDevicePath(IntPtr deviceInfoSet, ref UsbPrinterNativeMethods.SpDeviceInterfaceData interfaceData, out string devicePath, out string? error)
    {
        devicePath = string.Empty;
        error = null;

        uint requiredSize = 0;
        _ = UsbPrinterNativeMethods.SetupDiGetDeviceInterfaceDetail(deviceInfoSet, ref interfaceData, IntPtr.Zero, 0, out requiredSize, IntPtr.Zero);
        var lastError = Marshal.GetLastWin32Error();
        if (requiredSize == 0 || lastError != UsbPrinterNativeMethods.ERROR_INSUFFICIENT_BUFFER)
        {
            error = new Win32Exception(lastError).Message;
            return false;
        }

        var detailBuffer = Marshal.AllocHGlobal((int)requiredSize);
        try
        {
            Marshal.WriteInt32(detailBuffer, IntPtr.Size == 8 ? 8 : 6);
            if (!UsbPrinterNativeMethods.SetupDiGetDeviceInterfaceDetail(deviceInfoSet, ref interfaceData, detailBuffer, requiredSize, out _, IntPtr.Zero))
            {
                lastError = Marshal.GetLastWin32Error();
                error = new Win32Exception(lastError).Message;
                return false;
            }

            var pathPointer = detailBuffer + (IntPtr.Size == 8 ? 8 : 4);
            devicePath = Marshal.PtrToStringUni(pathPointer) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(devicePath) || !devicePath.StartsWith("\\\\?\\", StringComparison.OrdinalIgnoreCase))
            {
                error = "SetupDi returned an interface path that could not be decoded into a valid Win32 device path.";
                devicePath = string.Empty;
                return false;
            }

            return true;
        }
        finally
        {
            Marshal.FreeHGlobal(detailBuffer);
        }
    }

    private static class UsbPrinterNativeMethods
    {
        internal static readonly Guid GuidDevInterfaceUsbPrint = new("28D78FAD-5A12-11D1-AE5B-0000F803A8C2");
        internal const int ERROR_NO_MORE_ITEMS = 259;
        internal const int ERROR_INSUFFICIENT_BUFFER = 122;
        internal const int DIGCF_PRESENT = 0x00000002;
        internal const int DIGCF_DEVICEINTERFACE = 0x00000010;
        internal static readonly IntPtr InvalidHandleValue = new(-1);

        [StructLayout(LayoutKind.Sequential)]
        internal struct SpDeviceInterfaceData
        {
            public int cbSize;
            public Guid InterfaceClassGuid;
            public int Flags;
            public IntPtr Reserved;
        }

        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        internal static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, string? enumerator, IntPtr hwndParent, int flags);

        [DllImport("setupapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetupDiEnumDeviceInterfaces(IntPtr deviceInfoSet, IntPtr deviceInfoData, ref Guid interfaceClassGuid, uint memberIndex, ref SpDeviceInterfaceData deviceInterfaceData);

        [DllImport("setupapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr deviceInfoSet, ref SpDeviceInterfaceData deviceInterfaceData, IntPtr deviceInterfaceDetailData, uint deviceInterfaceDetailDataSize, out uint requiredSize, IntPtr deviceInfoData);

        [DllImport("setupapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        internal static extern SafeFileHandle CreateFile(string lpFileName, uint dwDesiredAccess, FileShare dwShareMode, IntPtr lpSecurityAttributes, FileMode dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);
    }
}
