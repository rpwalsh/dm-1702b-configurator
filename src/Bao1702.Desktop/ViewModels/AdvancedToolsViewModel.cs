namespace Bao1702.Desktop.ViewModels;

/// <summary>
/// View model for the Advanced reverse-engineering tools tab.
/// Displays capture analysis, hex diffs, and USB printer probe results.
/// </summary>
public sealed class AdvancedToolsViewModel : ObservableObject
{
    private string _captureAnalysisSummary = "No capture analysis loaded yet. Click 'Analyze Capture...' to load transcript/hex text and decode frames.";
    private string _hexDiffSummary = "No image diff loaded yet. Click 'Diff Images...' to compare two binary images.";
    private string _guidance = "Use the action buttons to load real capture files, diff native/firmware images, and probe USB printer interfaces. Protocol trace data also appears in the Trace tab during live operations.";
    private string _usbPrinterProbeSummary = "No USB printer-class probe executed yet. Click 'Probe USB' to inspect VID:PID 0483:5780 interfaces.";

    public string CaptureAnalysisSummary
    {
        get => _captureAnalysisSummary;
        set => SetProperty(ref _captureAnalysisSummary, value);
    }

    public string HexDiffSummary
    {
        get => _hexDiffSummary;
        set => SetProperty(ref _hexDiffSummary, value);
    }

    public string Guidance
    {
        get => _guidance;
        set => SetProperty(ref _guidance, value);
    }

    public string UsbPrinterProbeSummary
    {
        get => _usbPrinterProbeSummary;
        set => SetProperty(ref _usbPrinterProbeSummary, value);
    }

    public void LoadSyntheticPreview()
    {
        CaptureAnalysisSummary = "No capture analysis loaded yet. Click 'Analyze Capture...' to load transcript/hex text and decode frames.";
        HexDiffSummary = "No image diff loaded yet. Click 'Diff Images...' to compare two binary images.";
        UsbPrinterProbeSummary = "No USB printer-class probe executed yet. Click 'Probe USB' to inspect VID:PID 0483:5780 interfaces.";
    }
}
