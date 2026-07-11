using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Bao1702.Desktop.ViewModels;

namespace Bao1702.Desktop;

/// <summary>Primary application window hosting the codeplug editor, device tools, and settings tabs.</summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        var vm = new MainWindowViewModel();
        DataContext = vm;
        Loaded += async (_, _) =>
        {
            ApplyDarkTitleBar();

            // Auto-scan for devices on startup
            try
            {
                await vm.DevicePicker.ScanAsync();
            }
            catch
            {
                // Scan failures are logged in the VM; don't crash startup.
            }
        };
    }

    /// <summary>
    /// Sets the Windows title bar to dark green (#0E130E) via DWM to match the app theme.
    /// Falls back silently on older Windows versions that don't support DWMWA_CAPTION_COLOR.
    /// </summary>
    private void ApplyDarkTitleBar()
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == nint.Zero) return;

            // DWMWA_USE_IMMERSIVE_DARK_MODE = 20 (dark title bar text)
            uint useDarkMode = 1;
            DwmSetWindowAttribute(hwnd, 20, ref useDarkMode, sizeof(uint));

            // DWMWA_CAPTION_COLOR = 35 (Win11 22H2+): COLORREF is 0x00BBGGRR
            // Dark forest green #0E130E → R=0x0E, G=0x13, B=0x0E → COLORREF 0x000E130E
            uint captionColor = 0x000E130E;
            DwmSetWindowAttribute(hwnd, 35, ref captionColor, sizeof(uint));

            // DWMWA_BORDER_COLOR = 34: dark border to match
            // Forest border #1F2E1F → R=0x1F, G=0x2E, B=0x1F → COLORREF 0x001F2E1F
            uint borderColor = 0x001F2E1F;
            DwmSetWindowAttribute(hwnd, 34, ref borderColor, sizeof(uint));
        }
        catch
        {
            // DWM attributes unavailable on older OS; non-critical.
        }
    }

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(nint hwnd, int attribute, ref uint value, int size);
}