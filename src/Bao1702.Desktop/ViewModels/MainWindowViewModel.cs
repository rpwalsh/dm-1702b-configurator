using System.IO;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text;
using System.Windows;
using Bao1702.Codeplug.Binary;
using Bao1702.Codeplug.Model;
using Bao1702.Desktop.Commands;
using Bao1702.Desktop.Models;
using Bao1702.Desktop.Services;
using Bao1702.Firmware.Analysis;
using Bao1702.Protocol;
using Bao1702.Protocol.Model;
using Bao1702.Protocol.Safety;
using Bao1702.ReverseEngineering.Helpers;
using Bao1702.Transport.Abstractions;
using Bao1702.Transport.Serial;
using Bao1702.Transport.UsbPrinter;

namespace Bao1702.Desktop.ViewModels;

/// <summary>
/// Top-level view model orchestrating all CPS desktop operations:
/// device connection, codeplug read/write, file I/O, backup management,
/// firmware analysis, CSV import/export, and transport diagnostics.
/// </summary>
public sealed class MainWindowViewModel : ObservableObject
{
    private readonly DeviceSessionService _deviceSessionService = new();
    private readonly FirmwareWorkspaceService _firmwareWorkspaceService = new();
    private RadioIdentity? _activeIdentity;
    private CompatibilityResult? _activeCompatibility;
    private string _connectionStatus = "Disconnected";
    private string _selectedRadio = "No device selected";
    private string _radioInfoSummary = "Connect to a radio to begin.";
    private string _safetyDecisionSummary = "No target identified yet. Reads are safe; writes remain blocked until identification and backup complete.";
    private string _safetyReasonsSummary = "Probe a target to see compatibility and safety reasons.";
    private string _statusBarText = "Ready";
    private double _progressValue;
    private string _progressText = string.Empty;
    private bool _isProgressVisible;
    private AsyncRelayCommand? _runningCommand;

    public MainWindowViewModel()
    {
        DevicePicker = new DevicePickerViewModel(_deviceSessionService.EnumerateDevicesAsync);
        CodeplugEditor = new CodeplugEditorViewModel
        {
            Codeplug = CodeplugImage.CreateEmpty(),
        };
        BackupRestore = new BackupRestoreViewModel();
        FirmwareAnalysis = new FirmwareAnalysisViewModel();
        AdvancedTools = new AdvancedToolsViewModel();
        CsvWorkspace = new CsvWorkspaceViewModel();
        TransportDiagnostics = new TransportDiagnosticsViewModel();
        CsvWorkspace.ActiveCodeplug = CodeplugEditor.Codeplug;
        CsvWorkspace.CodeplugImported += OnCsvCodeplugImported;
        DevicePicker.PropertyChanged += OnDevicePickerPropertyChanged;
        CodeplugEditor.PropertyChanged += OnCodeplugEditorPropertyChanged;
        TransportDiagnostics.PropertyChanged += OnTransportDiagnosticsPropertyChanged;

        LogEntries =
        [
            new LogEntry(DateTimeOffset.Now, "INFO", "Bao1702 Desktop initialized — safety-first CPS and reverse-engineering workstation."),
            new LogEntry(DateTimeOffset.Now, "INFO", "Desktop workspace starts empty. Connect to a radio, open a native image, or import CSV to begin."),
            new LogEntry(DateTimeOffset.Now, "INFO", ProtocolAssumptions.Notes),
        ];

        ScanDevicesCommand = new AsyncRelayCommand(ScanDevicesAsync);
        ReadCodeplugCommand = new AsyncRelayCommand(ReadCodeplugAsync);
        WriteCodeplugCommand = new AsyncRelayCommand(WriteCodeplugAsync);
        BackupFirmwareCommand = new AsyncRelayCommand(BackupFirmwareAsync);
        ValidateCommand = new RelayCommand(Validate);
        OpenCodeplugFileCommand = new RelayCommand(OpenCodeplugFile);
        SaveCodeplugFileCommand = new RelayCommand(SaveCodeplugFile);
        OpenFirmwareImageCommand = new RelayCommand(OpenFirmwareImage);
        AnalyzeCaptureFileCommand = new RelayCommand(AnalyzeCaptureFile);
        DiffImagesCommand = new RelayCommand(DiffImages);
        ProbeUsbPrinterCommand = new RelayCommand(ProbeUsbPrinter);
        ExportTraceCommand = new RelayCommand(ExportTrace, () => TransportDiagnostics.Events.Count > 0);
        VerifyImageCommand = new RelayCommand(VerifyImageFile);
        GeneratePnwCodeplugCommand = new RelayCommand(GeneratePnwCodeplugFile);
        InspectCpsFolderCommand = new RelayCommand(InspectCpsFolder);
        AnalyzeCpsFolderCommand = new RelayCommand(AnalyzeCpsFolder);
        NormalizeCaptureCommand = new RelayCommand(NormalizeCaptureFile);
        ExportCaptureRadioJsonCommand = new RelayCommand(ExportCaptureRadioJson);
        ExportCaptureRadioBinCommand = new RelayCommand(ExportCaptureRadioBin);
        AnalyzeWriteSessionCommand = new RelayCommand(AnalyzeWriteSessionFile);
        MapWriteSessionCommand = new RelayCommand(MapWriteSessionFile);
        CancelCommand = new RelayCommand(CancelRunningOperation, () => _runningCommand?.IsRunning == true);
        ClearDiagnosticsCommand = new RelayCommand(() => TransportDiagnostics.Clear());
        ShowAboutCommand = new RelayCommand(ShowAbout);

        WireCommandErrors(ScanDevicesCommand, "Scan failed");
        WireCommandErrors(ReadCodeplugCommand, "Read codeplug failed");
        WireCommandErrors(WriteCodeplugCommand, "Write codeplug failed");
        WireCommandErrors(BackupFirmwareCommand, "Firmware backup failed");

        RefreshCommandStates();
        StatusBarText = "Ready — scan, connect, open a native image, or import CSV.";
    }

    public string Title => CodeplugEditor.IsDirty
        ? "Bao1702 Desktop — Safety-First CPS & RE Workstation *"
        : "Bao1702 Desktop — Safety-First CPS & RE Workstation";

    public DevicePickerViewModel DevicePicker { get; }

    public CodeplugEditorViewModel CodeplugEditor { get; }

    public BackupRestoreViewModel BackupRestore { get; }

    public FirmwareAnalysisViewModel FirmwareAnalysis { get; }

    public AdvancedToolsViewModel AdvancedTools { get; }

    public CsvWorkspaceViewModel CsvWorkspace { get; }

    public TransportDiagnosticsViewModel TransportDiagnostics { get; }

    public ObservableCollection<LogEntry> LogEntries { get; }

    private void OnDevicePickerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DevicePickerViewModel.SelectedEndpoint)
            || e.PropertyName == nameof(DevicePickerViewModel.IsScanning))
        {
            RefreshCommandStates();
        }
    }

    private void OnCodeplugEditorPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CodeplugEditorViewModel.IsDirty))
        {
            OnPropertyChanged(nameof(Title));
        }
    }

    private void OnTransportDiagnosticsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TransportDiagnosticsViewModel.TotalEvents))
        {
            ExportTraceCommand.RaiseCanExecuteChanged();
        }
    }

    private void WireCommandErrors(AsyncRelayCommand command, string prefix)
    {
        command.OnError += ex =>
        {
            HideProgress();
            Log("ERROR", $"{prefix}: {ex.Message}");
            MessageBox.Show(ex.Message, prefix, MessageBoxButton.OK, MessageBoxImage.Error);
        };
    }

    #region Connection state

    public string ConnectionStatus
    {
        get => _connectionStatus;
        set
        {
            SetProperty(ref _connectionStatus, value);
            OnPropertyChanged(nameof(IsConnected));
        }
    }

    public bool IsConnected => _activeIdentity is not null;

    public string SelectedRadio
    {
        get => _selectedRadio;
        set => SetProperty(ref _selectedRadio, value);
    }

    public string RadioInfoSummary
    {
        get => _radioInfoSummary;
        set => SetProperty(ref _radioInfoSummary, value);
    }

    public string SafetyDecisionSummary
    {
        get => _safetyDecisionSummary;
        set => SetProperty(ref _safetyDecisionSummary, value);
    }

    public string SafetyReasonsSummary
    {
        get => _safetyReasonsSummary;
        set => SetProperty(ref _safetyReasonsSummary, value);
    }

    #endregion

    #region Progress

    public double ProgressValue
    {
        get => _progressValue;
        set => SetProperty(ref _progressValue, value);
    }

    public string ProgressText
    {
        get => _progressText;
        set => SetProperty(ref _progressText, value);
    }

    public bool IsProgressVisible
    {
        get => _isProgressVisible;
        set => SetProperty(ref _isProgressVisible, value);
    }

    public string StatusBarText
    {
        get => _statusBarText;
        set => SetProperty(ref _statusBarText, value);
    }

    #endregion

    #region Commands

    public AsyncRelayCommand ScanDevicesCommand { get; }
    public AsyncRelayCommand ReadCodeplugCommand { get; }
    public AsyncRelayCommand WriteCodeplugCommand { get; }
    public AsyncRelayCommand BackupFirmwareCommand { get; }
    public RelayCommand ValidateCommand { get; }
    public RelayCommand OpenCodeplugFileCommand { get; }
    public RelayCommand SaveCodeplugFileCommand { get; }
    public RelayCommand OpenFirmwareImageCommand { get; }
    public RelayCommand AnalyzeCaptureFileCommand { get; }
    public RelayCommand DiffImagesCommand { get; }
    public RelayCommand ProbeUsbPrinterCommand { get; }
    public RelayCommand ExportTraceCommand { get; }
    public RelayCommand VerifyImageCommand { get; }
    public RelayCommand GeneratePnwCodeplugCommand { get; }
    public RelayCommand InspectCpsFolderCommand { get; }
    public RelayCommand AnalyzeCpsFolderCommand { get; }
    public RelayCommand NormalizeCaptureCommand { get; }
    public RelayCommand ExportCaptureRadioJsonCommand { get; }
    public RelayCommand ExportCaptureRadioBinCommand { get; }
    public RelayCommand AnalyzeWriteSessionCommand { get; }
    public RelayCommand MapWriteSessionCommand { get; }
    public RelayCommand CancelCommand { get; }
    public RelayCommand ClearDiagnosticsCommand { get; }
    public RelayCommand ShowAboutCommand { get; }

    #endregion

    private void Log(string level, string message)
    {
        LogEntries.Add(new LogEntry(DateTimeOffset.Now, level, message));
        StatusBarText = message;
    }

    private void ResetActiveConnectionState(string selectedRadio = "No device selected")
    {
        _activeIdentity = null;
        _activeCompatibility = null;
        SelectedRadio = selectedRadio;
        OnPropertyChanged(nameof(IsConnected));
        RefreshCommandStates();
    }

    private void VerifyImageFile()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Verify Image",
            Filter = "Images (*.data;*.bin;*.img;*.json)|*.data;*.bin;*.img;*.json|All files (*.*)|*.*",
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var bytes = File.ReadAllBytes(dialog.FileName);
            string result;

            if (bytes.Length == Dm1702NativeImageAssumptions.ImageLength)
            {
                var codeplug = Dm1702NativeImageReader.ReadFromNative(bytes);
                result = $"Native DM-1702 codeplug image OK. Channels: {codeplug.Channels.Count}, Zones: {codeplug.Zones.Count}, Contacts: {codeplug.Contacts.Count}, RxGroups: {codeplug.RxGroups.Count}, ScanLists: {codeplug.ScanLists.Count}.";
            }
            else
            {
                try
                {
                    var codeplug = CodeplugBinarySerializer.Deserialize(bytes);
                    result = $"Codeplug image OK. Channels: {codeplug.Channels.Count}, Zones: {codeplug.Zones.Count}, Contacts: {codeplug.Contacts.Count}.";
                }
                catch (InvalidDataException)
                {
                    var firmware = FirmwareImageParser.Analyze(bytes);
                    result = string.Join(Environment.NewLine,
                        $"Firmware-like image signature: {firmware.Image.Header.Signature}",
                        $"Declared length: {firmware.Image.Header.DeclaredLength}",
                        $"Checksums: {string.Join(", ", firmware.Checksums.Select(checksum => $"{checksum.Algorithm}={checksum.Value}"))}",
                        $"Warnings: {string.Join("; ", firmware.Warnings.DefaultIfEmpty("none"))}");
                }
            }

            AdvancedTools.HexDiffSummary = result;
            Log("INFO", $"Image verification complete: {dialog.FileName}");
        }
        catch (Exception ex)
        {
            Log("ERROR", $"Image verification failed: {ex.Message}");
            MessageBox.Show(ex.Message, "Image verification failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void GeneratePnwCodeplugFile()
    {
        MessageBox.Show("Regional channel-plan generation is not distributed. Import a synthetic or user-authorized CSV instead.", "Feature unavailable", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void InspectCpsFolder()
    {
        MessageBox.Show("Manufacturer-software inspection is not part of Bao1702.", "Feature unavailable", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void AnalyzeCpsFolder()
    {
        MessageBox.Show("Manufacturer executable analysis is not part of Bao1702.", "Feature unavailable", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void NormalizeCaptureFile()
    {
        var open = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Select capture transcript to normalize",
            Filter = "Text files (*.txt;*.log;*.cap)|*.txt;*.log;*.cap|All files (*.*)|*.*",
        };

        if (open.ShowDialog() != true)
        {
            return;
        }

        var save = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Save normalized transcript",
            Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
            FileName = $"{Path.GetFileNameWithoutExtension(open.FileName)}.normalized.txt",
        };

        if (save.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var transcriptText = File.ReadAllText(open.FileName);
            var transcript = CaptureTranscriptParser.Parse(transcriptText);
            var normalized = CaptureTranscriptFormatter.Normalize(transcript);
            File.WriteAllText(save.FileName, normalized);
            Log("INFO", $"Normalized capture transcript written: {save.FileName} ({transcript.Records.Count} records)");
        }
        catch (Exception ex)
        {
            Log("ERROR", $"Capture normalization failed: {ex.Message}");
            MessageBox.Show(ex.Message, "Capture normalization failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ExportCaptureRadioJson()
    {
        var open = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Select capture for radio JSON export",
            Filter = "Capture files (*.pcap;*.txt;*.log;*.cap)|*.pcap;*.txt;*.log;*.cap|All files (*.*)|*.*",
        };

        if (open.ShowDialog() != true)
        {
            return;
        }

        var save = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Save radio data JSON",
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
            FileName = $"{Path.GetFileNameWithoutExtension(open.FileName)}.radio.json",
        };

        if (save.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var analysis = LoadCaptureAnalysis(open.FileName);
            var dump = StockCpsRadioDataExporter.BuildDump(open.FileName, analysis);
            var json = StockCpsRadioDataExporter.SerializeToJson(dump);
            File.WriteAllText(save.FileName, json);
            Log("INFO", $"Exported radio JSON: {save.FileName}");
        }
        catch (Exception ex)
        {
            Log("ERROR", $"Capture JSON export failed: {ex.Message}");
            MessageBox.Show(ex.Message, "Capture JSON export failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ExportCaptureRadioBin()
    {
        var open = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Select capture for radio binary export",
            Filter = "Capture files (*.pcap;*.txt;*.log;*.cap)|*.pcap;*.txt;*.log;*.cap|All files (*.*)|*.*",
        };

        if (open.ShowDialog() != true)
        {
            return;
        }

        var save = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Save reassembled radio binary",
            Filter = "Binary files (*.bin)|*.bin|All files (*.*)|*.*",
            FileName = $"{Path.GetFileNameWithoutExtension(open.FileName)}.radio.bin",
        };

        if (save.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var analysis = LoadCaptureAnalysis(open.FileName);
            var dump = StockCpsRadioDataExporter.BuildDump(open.FileName, analysis);
            if (dump.ReassembledImage is null)
            {
                throw new InvalidOperationException("No reassembled radio image could be derived from the capture.");
            }

            var imageBytes = Convert.FromHexString(dump.ReassembledImage.ImageHex);
            File.WriteAllBytes(save.FileName, imageBytes);
            Log("INFO", $"Exported reassembled radio binary: {save.FileName} ({imageBytes.Length:N0} bytes)");
        }
        catch (Exception ex)
        {
            Log("ERROR", $"Capture binary export failed: {ex.Message}");
            MessageBox.Show(ex.Message, "Capture binary export failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void AnalyzeWriteSessionFile()
    {
        var open = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Select tshark write-session field file",
            Filter = "Field text files (*.txt;*.log)|*.txt;*.log|All files (*.*)|*.*",
        };

        if (open.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var lines = File.ReadAllLines(open.FileName);
            var analysis = WriteSessionAnalyzer.AnalyzeTsharkFieldLines(lines);

            var output = new List<string>
            {
                $"Write blocks: {analysis.Blocks.Count}",
                "Window write counts:",
            };
            output.AddRange(analysis.WindowWriteCounts
                .OrderBy(pair => pair.Key)
                .Select(pair => $"- 0x{pair.Key:X2}: {pair.Value}"));
            output.Add(string.Empty);
            output.Add("First write blocks:");
            output.AddRange(analysis.Blocks.Take(24).Select(block =>
                $"- frame={block.FrameNumber} address=0x{block.Address:X6} window=0x{block.WindowOffset:X2} ascii='{block.AsciiPreview}'"));

            AdvancedTools.CaptureAnalysisSummary = string.Join(Environment.NewLine, output);
            Log("INFO", $"Write-session analysis complete: {open.FileName}");
        }
        catch (Exception ex)
        {
            Log("ERROR", $"Write-session analysis failed: {ex.Message}");
            MessageBox.Show(ex.Message, "Write-session analysis failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void MapWriteSessionFile()
    {
        var writeFile = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Select tshark write-session field file",
            Filter = "Field text files (*.txt;*.log)|*.txt;*.log|All files (*.*)|*.*",
        };

        if (writeFile.ShowDialog() != true)
        {
            return;
        }

        var codeplugFile = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Select saved codeplug/data file",
            Filter = "Codeplug files (*.data;*.bin;*.img)|*.data;*.bin;*.img|All files (*.*)|*.*",
        };

        if (codeplugFile.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var lines = File.ReadAllLines(writeFile.FileName);
            var analysis = WriteSessionAnalyzer.AnalyzeTsharkFieldLines(lines);
            var codeplug = File.ReadAllBytes(codeplugFile.FileName);
            var mappings = WriteBlockMapper.MapToSavedCodeplug(analysis.Blocks, codeplug);

            var output = new List<string>
            {
                $"Write blocks: {analysis.Blocks.Count}",
                $"Unique file mappings: {mappings.Count(mapping => mapping.FileOffset.HasValue)}",
                "First mapped write blocks:",
            };
            output.AddRange(mappings
                .Where(mapping => mapping.FileOffset.HasValue)
                .Take(32)
                .Select(mapping => $"- frame={mapping.FrameNumber} writeAddress=0x{mapping.WriteAddress:X6} fileOffset=0x{mapping.FileOffset!.Value:X6} ascii='{mapping.AsciiPreview}'"));

            AdvancedTools.HexDiffSummary = string.Join(Environment.NewLine, output);
            Log("INFO", $"Write-map analysis complete: {writeFile.FileName} against {codeplugFile.FileName}");
        }
        catch (Exception ex)
        {
            Log("ERROR", $"Write-map analysis failed: {ex.Message}");
            MessageBox.Show(ex.Message, "Write-map analysis failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static CaptureSessionAnalysis LoadCaptureAnalysis(string capturePath)
    {
        var isPcap = string.Equals(Path.GetExtension(capturePath), ".pcap", StringComparison.OrdinalIgnoreCase);
        if (isPcap)
        {
            var records = PcapFileParser.ParseRecords(File.ReadAllBytes(capturePath))
                .Select((record, index) => new CaptureRecord(index, CaptureDirection.Unknown, record, []))
                .ToArray();
            return CaptureSessionAnalyzer.AnalyzeRecords(records);
        }

        var transcriptText = File.ReadAllText(capturePath);
        var transcript = CaptureTranscriptParser.Parse(transcriptText);
        return CaptureSessionAnalyzer.AnalyzeRecords(transcript.Records);
    }

    private IProgress<ProtocolProgress> CreateProgressReporter()
    {
        IsProgressVisible = true;
        ProgressValue = 0;
        CancelCommand.RaiseCanExecuteChanged();
        return new Progress<ProtocolProgress>(p =>
        {
            ProgressValue = p.PercentComplete;
            ProgressText = p.ToString();
        });
    }

    private void HideProgress()
    {
        IsProgressVisible = false;
        ProgressText = string.Empty;
        _runningCommand = null;
        CancelCommand.RaiseCanExecuteChanged();
    }

    private void CancelRunningOperation()
    {
        if (_runningCommand is { IsRunning: true })
        {
            _runningCommand.Cancel();
            Log("WARN", "Cancellation requested for running operation.");
        }
    }

    private void RefreshCommandStates()
    {
        ReadCodeplugCommand.RaiseCanExecuteChanged();
        WriteCodeplugCommand.RaiseCanExecuteChanged();
        BackupFirmwareCommand.RaiseCanExecuteChanged();
        ExportTraceCommand.RaiseCanExecuteChanged();
        CancelCommand.RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(Title));
    }

    #region Command implementations

    private async Task ScanDevicesAsync(CancellationToken ct)
    {
        _runningCommand = ScanDevicesCommand;
        Log("INFO", "Scanning for radio transport devices...");
        await DevicePicker.ScanAsync(ct).ConfigureAwait(true);
        Log("INFO", $"Found {DevicePicker.Endpoints.Count} device(s).");
        RefreshCommandStates();
    }

    private async Task ConnectAsync(CancellationToken ct)
    {
        try
        {
            _runningCommand = ReadCodeplugCommand;

            // Auto-scan when no endpoint is selected so the user doesn't have to
            // manually click Scan before Connect.
            if (DevicePicker.SelectedEndpoint is null)
            {
                Log("INFO", "No endpoint selected — scanning for devices...");
                await DevicePicker.ScanAsync(ct).ConfigureAwait(true);
                RefreshCommandStates();
                if (DevicePicker.SelectedEndpoint is null)
                {
                    ConnectionStatus = "No devices found";
                    Log("WARN", "Scan completed but no radio transport endpoints were found.");
                    MessageBox.Show(
                        "No radio devices were found.\n\nMake sure the DM-1702 is connected via USB and powered on.",
                        "No devices found",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }
            }

            Log("INFO", "Connecting and reading radio info...");
            var probe = await _deviceSessionService.ConnectAndReadInfoAsync(DevicePicker.SelectedEndpoint, ct).ConfigureAwait(true);

            // Feed transport traces into diagnostics viewer
            if (_deviceSessionService.LastTraceCollector is { Events.Count: > 0 } collector)
            {
                TransportDiagnostics.AppendBatch(collector.Events);
            }

            if (probe.RadioInfo is null)
            {
                ResetActiveConnectionState(probe.Endpoint.DisplayName);
                ConnectionStatus = "Probe failed";
                RadioInfoSummary = probe.Summary;
                SafetyDecisionSummary = "Write operations blocked — target could not be identified.";
                SafetyReasonsSummary = string.Join(Environment.NewLine, probe.Notes.DefaultIfEmpty("No compatibility evidence available from failed probe."));
                BackupRestore.ClearPreflight("Write preflight unavailable — target probe failed.");
                Log("ERROR", probe.Summary);
                var errorDetail = probe.Error is not null ? $"\n\n{probe.Error.GetType().Name}: {probe.Error.Message}" : string.Empty;
                MessageBox.Show($"{probe.Summary}{errorDetail}", "Connection failed", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            ConnectionStatus = probe.Endpoint.TransportType switch
            {
                Bao1702.Transport.Abstractions.TransportType.UsbPrinter => "Connected (USB printer)",
                Bao1702.Transport.Abstractions.TransportType.Serial => "Connected (serial)",
                Bao1702.Transport.Abstractions.TransportType.Mock => "Connected (mock)",
                _ => $"Connected ({probe.Endpoint.TransportType})",
            };
            _activeIdentity = probe.RadioInfo.Identity;
            OnPropertyChanged(nameof(IsConnected));
            _activeCompatibility = probe.RadioInfo.Compatibility;
            SelectedRadio = probe.Endpoint.DisplayName;
            RadioInfoSummary = string.Join(Environment.NewLine,
                $"Model: {probe.RadioInfo.Identity.ModelName}",
                $"Transport: {probe.Endpoint.TransportType}",
                $"Firmware: {probe.RadioInfo.Identity.FirmwareVersion}",
                $"Bootloader: {probe.RadioInfo.Identity.BootloaderVersion}",
                $"Serial: {probe.RadioInfo.Identity.SerialNumber ?? "N/A"}",
                $"Compatibility: {probe.RadioInfo.Compatibility.Summary}");
            SafetyDecisionSummary = probe.RadioInfo.Compatibility.IsWriteSafeByDefault
                ? "Target is write-capable by default, but backup-before-write remains mandatory."
                : "Target is read-only by default. Writes remain blocked unless policy and backup requirements are explicitly satisfied.";
            SafetyReasonsSummary = string.Join(Environment.NewLine, probe.RadioInfo.Compatibility.Reasons.DefaultIfEmpty("No compatibility reasons were returned."));
            BackupRestore.UpdateFromBackup(_activeIdentity, _deviceSessionService.GetLatestCodeplugBackup(_activeIdentity));
            BackupRestore.ClearPreflight("No write preflight has been executed since the latest target connect.");
            RefreshCommandStates();
            Log("INFO", $"Connected to {_activeIdentity.ModelName} — {probe.RadioInfo.Compatibility.Summary}");
        }
        catch (OperationCanceledException)
        {
            ResetActiveConnectionState();
            Log("WARN", "Connect operation was cancelled.");
        }
        catch (Exception ex)
        {
            ResetActiveConnectionState();
            ConnectionStatus = "Connection failed";
            RadioInfoSummary = ex.Message;
            SafetyDecisionSummary = "Write operations blocked — connection failed.";
            SafetyReasonsSummary = ex.Message;
            BackupRestore.ClearPreflight("Write preflight unavailable — connection failed.");
            BackupRestore.SetStatus("Unable to query backup status — target probing failed.");
            Log("ERROR", ex.Message);
        }
    }

    private void ShowAbout()
    {
        var about = new AboutWindow { Owner = Application.Current.MainWindow };
        about.ShowDialog();
    }

    private async Task ReadCodeplugAsync(CancellationToken ct)
    {
        if (!await EnsureConnectedForOperationAsync(ct, "read codeplug").ConfigureAwait(true))
        {
            return;
        }

        try
        {
            _runningCommand = ReadCodeplugCommand;
            Log("INFO", "Reading codeplug from radio...");
            var progress = CreateProgressReporter();
            var outputDirectory = Path.Combine(AppContext.BaseDirectory, "backups");
            var backupPath = await _deviceSessionService.BackupCodeplugAsync(outputDirectory, progress, ct).ConfigureAwait(true);
            HideProgress();
            if (_activeIdentity is not null)
            {
                BackupRestore.UpdateFromBackup(_activeIdentity, _deviceSessionService.GetLatestCodeplugBackup(_activeIdentity));
            }

            // Load the native image into the editor so the user sees the radio's data
            var nativeBytes = File.ReadAllBytes(backupPath);
            if (nativeBytes.Length == Dm1702NativeImageAssumptions.ImageLength)
            {
                var image = Dm1702NativeImageReader.ReadFromNative(nativeBytes);
                CodeplugEditor.Codeplug = image;
                CsvWorkspace.ActiveCodeplug = image;
                Log("INFO", $"Codeplug read complete: {backupPath} ({image.Channels.Count} channels loaded into editor)");
            }
            else
            {
                Log("WARN", $"Codeplug read complete but image size ({nativeBytes.Length}) does not match expected native size. File saved: {backupPath}");
            }
        }
        catch (OperationCanceledException)
        {
            HideProgress();
            ResetActiveConnectionState();
            Log("WARN", "Codeplug read was cancelled.");
        }
        catch (Exception ex)
        {
            HideProgress();
            ResetActiveConnectionState();
            ConnectionStatus = "Read failed";
            Log("ERROR", $"Codeplug read failed: {ex.Message}");
        }
    }

    private async Task WriteCodeplugAsync(CancellationToken ct)
    {
        if (!await EnsureConnectedForOperationAsync(ct, "write codeplug").ConfigureAwait(true))
        {
            return;
        }

        // Rebuild model from editor state before write
        CodeplugImage rebuilt;
        try
        {
            rebuilt = CodeplugEditor.RebuildCodeplug();
        }
        catch (Exception ex)
        {
            Log("ERROR", $"Failed to rebuild codeplug model: {ex.Message}");
            return;
        }

        // Validate before write
        var issues = Bao1702.Codeplug.Validation.CodeplugValidator.Validate(rebuilt);
        var errors = issues.Count(i => i.Severity == Bao1702.Codeplug.Validation.ValidationSeverity.Error);
        if (errors > 0)
        {
            CodeplugEditor.Validate();
            Log("ERROR", $"Write blocked — codeplug has {errors} validation error(s). Fix them before writing.");
            return;
        }

        // Build native image from model — the model is the sole source of truth
        // for every byte in the config section.
        byte[] nativeImage;
        try
        {
            nativeImage = Dm1702NativeImageBuilder.Build(rebuilt);
        }
        catch (Exception ex)
        {
            Log("ERROR", $"Failed to build native image: {ex.Message}");
            return;
        }

        var imageValidation = Bao1702.Codeplug.Validation.CodeplugWriteValidator.ValidateImage(nativeImage, _activeIdentity.Capabilities.AssumedCodeplugSize);
        var preflight = new WriteIntentValidator().BuildPreflight(
            new WriteIntentValidationRequest(
                _activeIdentity,
                RadioOperation.WriteCodeplug,
                DeviceSessionService.GetBackupRootDirectory(),
                SafetyPolicyOptions.Default),
            isImageValidationAvailable: true,
            isImageValid: imageValidation.IsValid,
            expectedImageSize: imageValidation.ExpectedImageSize,
            actualImageSize: imageValidation.ActualImageSize,
            imageValidationMessages: imageValidation.Issues.Select(static issue => issue.Message).ToArray());

        BackupRestore.UpdatePreflight(preflight);
        if (!preflight.IsAllowed)
        {
            Log("ERROR", preflight.Summary);
            Log("WARN", WritePreflightFormatter.FormatText(preflight));
            return;
        }

        var result = MessageBox.Show(
            WritePreflightFormatter.FormatText(preflight) + Environment.NewLine + Environment.NewLine +
            $"Writing the codeplug ({rebuilt.Channels.Count} channels) will overwrite the radio's current configuration.\n\nContinue?",
            "Confirm Codeplug Write",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            _runningCommand = WriteCodeplugCommand;
            Log("INFO", $"Writing codeplug to radio ({nativeImage.Length:N0} bytes, {rebuilt.Channels.Count} channels)...");
            var writeProgress = CreateProgressReporter();
            var restoreResult = await _deviceSessionService.RestoreCodeplugAsync(nativeImage, writeProgress, ct).ConfigureAwait(true);
            HideProgress();
            CodeplugEditor.IsDirty = false;
            Log("INFO", restoreResult);
        }
        catch (OperationCanceledException)
        {
            HideProgress();
            ResetActiveConnectionState();
            Log("WARN", "Codeplug write was cancelled.");
        }
        catch (Exception ex)
        {
            HideProgress();
            ResetActiveConnectionState();
            ConnectionStatus = "Write failed";
            Log("ERROR", $"Codeplug write failed: {ex.Message}");
        }
    }

    private async Task BackupFirmwareAsync(CancellationToken ct)
    {
        if (!await EnsureConnectedForOperationAsync(ct, "backup firmware").ConfigureAwait(true))
        {
            return;
        }

        try
        {
            _runningCommand = BackupFirmwareCommand;
            Log("INFO", "Backing up firmware...");
            var progress = CreateProgressReporter();
            var outputDirectory = Path.Combine(AppContext.BaseDirectory, "firmware");
            var firmwarePath = await _deviceSessionService.BackupFirmwareAsync(outputDirectory, progress, ct).ConfigureAwait(true);
            HideProgress();
            var analysis = _firmwareWorkspaceService.AnalyzeFile(firmwarePath, _activeIdentity);
            FirmwareAnalysis.LoadAnalysis(analysis);
            Log("INFO", $"Firmware backup created and analyzed: {firmwarePath}");
        }
        catch (OperationCanceledException)
        {
            HideProgress();
            ResetActiveConnectionState();
            Log("WARN", "Firmware backup was cancelled.");
        }
        catch (Exception ex)
        {
            HideProgress();
            ResetActiveConnectionState();
            ConnectionStatus = "Firmware backup failed";
            Log("ERROR", $"Firmware backup failed: {ex.Message}");
            FirmwareAnalysis.Summary = ex.Message;
        }
    }

    private async Task<bool> EnsureConnectedForOperationAsync(CancellationToken ct, string operationName)
    {
        if (_activeIdentity is not null)
        {
            return true;
        }

        // Auto-scan when no endpoint is known, then auto-connect.
        if (DevicePicker.SelectedEndpoint is null)
        {
            Log("INFO", $"No endpoint selected — scanning before {operationName}...");
            await DevicePicker.ScanAsync(ct).ConfigureAwait(true);
            RefreshCommandStates();
            if (DevicePicker.SelectedEndpoint is null)
            {
                Log("WARN", $"Cannot {operationName} — no radio transport endpoints were found after scan.");
                MessageBox.Show("No radio transport endpoints were found. Ensure the radio is connected and powered on, then try again.",
                    "No devices found", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
        }

        Log("INFO", $"No active radio identity. Connecting before {operationName}...");
        await ConnectAsync(ct).ConfigureAwait(true);

        if (_activeIdentity is null)
        {
            Log("ERROR", $"Cannot {operationName} — radio identify/connect did not succeed.");
            return false;
        }

        return true;
    }

    private void Validate()
    {
        CodeplugEditor.Validate();
        Log("INFO", CodeplugEditor.ValidationSummary);
    }

    private void OpenCodeplugFile()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Open Codeplug File",
            Filter = "All Supported (*.json;*.data;*.bin;*.img)|*.json;*.data;*.bin;*.img|Codeplug JSON (*.json)|*.json|Native Image (*.data;*.bin;*.img)|*.data;*.bin;*.img|All files (*.*)|*.*",
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var bytes = File.ReadAllBytes(dialog.FileName);

            // Native 245K images are loaded via the native image builder's preserved raw path
            if (bytes.Length == Bao1702.Codeplug.Model.Dm1702NativeImageAssumptions.ImageLength)
            {
                var image = Dm1702NativeImageReader.ReadFromNative(bytes);
                CodeplugEditor.Codeplug = image;
                CsvWorkspace.ActiveCodeplug = image;
                Log("INFO", $"Opened native image: {dialog.FileName} ({image.Channels.Count} channels)");
                return;
            }

            // Otherwise try JSON container format
            var jsonImage = Bao1702.Codeplug.Binary.CodeplugBinarySerializer.Deserialize(bytes);
            CodeplugEditor.Codeplug = jsonImage;
            CsvWorkspace.ActiveCodeplug = jsonImage;
            Log("INFO", $"Opened codeplug: {dialog.FileName} ({jsonImage.Channels.Count} channels)");
        }
        catch (Exception ex)
        {
            Log("ERROR", $"Failed to open codeplug: {ex.Message}");
        }
    }

    private void SaveCodeplugFile()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Save Codeplug File",
            Filter = "Native Image (*.data;*.img;*.bin)|*.data;*.img;*.bin|Codeplug JSON (*.json)|*.json|All files (*.*)|*.*",
            FileName = "codeplug.data",
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var rebuilt = CodeplugEditor.RebuildCodeplug();
            var ext = Path.GetExtension(dialog.FileName).ToLowerInvariant();

            if (ext is ".data" or ".img" or ".bin")
            {
                var nativeBytes = Dm1702NativeImageBuilder.Build(rebuilt);
                File.WriteAllBytes(dialog.FileName, nativeBytes);
                CodeplugEditor.IsDirty = false;
                Log("INFO", $"Saved native image ({nativeBytes.Length:N0} bytes): {dialog.FileName}");
            }
            else
            {
                var bytes = Bao1702.Codeplug.Binary.CodeplugBinarySerializer.Serialize(rebuilt);
                File.WriteAllBytes(dialog.FileName, bytes);
                CodeplugEditor.IsDirty = false;
                Log("INFO", $"Saved codeplug JSON: {dialog.FileName}");
            }
        }
        catch (Exception ex)
        {
            Log("ERROR", $"Failed to save codeplug: {ex.Message}");
        }
    }

    private void OnCsvCodeplugImported(CodeplugImage image)
    {
        CodeplugEditor.Codeplug = image;
        CsvWorkspace.ActiveCodeplug = image;
        Log("INFO", $"CSV import applied — {image.Channels.Count} channels loaded into editor.");
    }

    private void OpenFirmwareImage()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Open Firmware Image",
            Filter = "Firmware/Binary Images (*.bin;*.img;*.fw)|*.bin;*.img;*.fw|All files (*.*)|*.*",
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var bytes = File.ReadAllBytes(dialog.FileName);
            var analysis = _firmwareWorkspaceService.AnalyzeBytes(Path.GetFileName(dialog.FileName), bytes, _activeIdentity);
            FirmwareAnalysis.LoadAnalysis(analysis);
            Log("INFO", $"Loaded firmware image for analysis: {dialog.FileName} ({bytes.Length:N0} bytes)");
        }
        catch (Exception ex)
        {
            Log("ERROR", $"Failed to analyze firmware image: {ex.Message}");
            MessageBox.Show(ex.Message, "Firmware analysis failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void AnalyzeCaptureFile()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Open Capture Transcript / Hex Dump",
            Filter = "Text/Capture Files (*.txt;*.log;*.cap)|*.txt;*.log;*.cap|All files (*.*)|*.*",
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var text = File.ReadAllText(dialog.FileName);
            CaptureSessionAnalysis analysis;

            try
            {
                analysis = CaptureSessionAnalyzer.AnalyzeTranscript(text);
            }
            catch
            {
                analysis = CaptureSessionAnalyzer.AnalyzeHexLines(text);
            }

            var builder = new StringBuilder();
            builder.AppendLine($"Source: {Path.GetFileName(dialog.FileName)}");
            builder.AppendLine($"Frames: {analysis.TotalFrames}");
            builder.AppendLine($"Valid: {analysis.ValidFrames}");
            builder.AppendLine($"Host→Device: {analysis.HostToDeviceFrames}");
            builder.AppendLine($"Device→Host: {analysis.DeviceToHostFrames}");
            builder.AppendLine($"Commands: {(analysis.CommandNames.Count == 0 ? "none" : string.Join(", ", analysis.CommandNames))}");
            builder.AppendLine();
            foreach (var frame in analysis.Frames.Take(32))
            {
                builder.AppendLine(frame.Summary);
                if (!string.IsNullOrWhiteSpace(frame.DecodeError))
                {
                    builder.AppendLine($"  Error: {frame.DecodeError}");
                }
            }

            AdvancedTools.CaptureAnalysisSummary = builder.ToString().TrimEnd();
            Log("INFO", $"Capture analysis loaded: {dialog.FileName} ({analysis.ValidFrames}/{analysis.TotalFrames} valid frames)");
        }
        catch (Exception ex)
        {
            Log("ERROR", $"Failed to analyze capture file: {ex.Message}");
            MessageBox.Show(ex.Message, "Capture analysis failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void DiffImages()
    {
        var leftDialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Select first image for diff",
            Filter = "Binary Images (*.bin;*.img;*.data)|*.bin;*.img;*.data|All files (*.*)|*.*",
        };

        if (leftDialog.ShowDialog() != true)
        {
            return;
        }

        var rightDialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Select second image for diff",
            Filter = "Binary Images (*.bin;*.img;*.data)|*.bin;*.img;*.data|All files (*.*)|*.*",
        };

        if (rightDialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var left = File.ReadAllBytes(leftDialog.FileName);
            var right = File.ReadAllBytes(rightDialog.FileName);
            var diff = HexDiffHelper.DescribeDiff(left, right);
            AdvancedTools.HexDiffSummary =
                $"Left: {Path.GetFileName(leftDialog.FileName)} ({left.Length:N0} bytes){Environment.NewLine}" +
                $"Right: {Path.GetFileName(rightDialog.FileName)} ({right.Length:N0} bytes){Environment.NewLine}{Environment.NewLine}" +
                diff;
            Log("INFO", $"Hex diff complete: {leftDialog.FileName} vs {rightDialog.FileName}");
        }
        catch (Exception ex)
        {
            Log("ERROR", $"Hex diff failed: {ex.Message}");
            MessageBox.Show(ex.Message, "Hex diff failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ProbeUsbPrinter()
    {
        try
        {
            var snapshot = UsbPrinterTransportProbe.Capture();
            AdvancedTools.UsbPrinterProbeSummary = UsbPrinterTransportProbe.Format(snapshot);
            Log("INFO", $"USB printer probe complete: {snapshot.Devices.Count} matching device(s), {snapshot.InterfaceProbes.Count} interface probe(s)");
        }
        catch (Exception ex)
        {
            Log("ERROR", $"USB printer probe failed: {ex.Message}");
            MessageBox.Show(ex.Message, "USB printer probe failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ExportTrace()
    {
        if (TransportDiagnostics.Events.Count == 0)
        {
            Log("WARN", "No trace events available to export.");
            return;
        }

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export Protocol Trace",
            Filter = "Trace Text (*.txt)|*.txt|All files (*.*)|*.*",
            FileName = $"trace-{DateTime.Now:yyyyMMdd-HHmmss}.txt",
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var lines = TransportDiagnostics.Events.Select(evt =>
                $"{evt.Timestamp:O} [{evt.Level}] {evt.Direction} {evt.Message}" +
                (string.IsNullOrWhiteSpace(evt.Payload) ? string.Empty : $" | {evt.Payload}"));
            File.WriteAllLines(dialog.FileName, lines);
            Log("INFO", $"Exported {TransportDiagnostics.Events.Count} trace event(s) to {dialog.FileName}");
        }
        catch (Exception ex)
        {
            Log("ERROR", $"Trace export failed: {ex.Message}");
            MessageBox.Show(ex.Message, "Trace export failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    #endregion
}
