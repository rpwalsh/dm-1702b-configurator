using Bao1702.Codeplug.Binary;
using Bao1702.Codeplug.Csv;
using Bao1702.Codeplug.Model;
using Bao1702.Codeplug.Validation;
using Bao1702.Firmware.Analysis;
using Bao1702.Protocol;
using Bao1702.Protocol.Discovery;
using Bao1702.Protocol.Model;
using Bao1702.Protocol.Safety;
using Bao1702.Protocol.Stock;
using Bao1702.ReverseEngineering.Helpers;
using Bao1702.Transport.Abstractions;
using Bao1702.Transport.Serial;
using Bao1702.Transport.UsbPrinter;

namespace Bao1702.Cli.Infrastructure;

/// <summary>
/// Core runtime for the CLI tool — manages transport factories, safety policy,
/// backup catalog, and codeplug read/write/export orchestration.
/// </summary>
internal sealed class CliRuntime
{
    private readonly MockRadioDevice _device = new();
    private readonly CliSessionFactory _sessionFactory;

    public CliRuntime()
    {
        _sessionFactory = new CliSessionFactory(_device);
    }

    public async Task<IReadOnlyList<string>> ListDevicesAsync()
    {
        var endpoints = await _sessionFactory.EnumerateAllAsync().ConfigureAwait(false);
        return endpoints
            .Select(item => $"{item.Endpoint.DisplayName} [{item.Endpoint.Id}] via {item.Factory.Name}")
            .ToList();
    }

    public async Task<string> ReadRadioInfoAsync(string? endpointId = null, bool includeTrace = false)
    {
        if (includeTrace)
        {
            var probeContext = await _sessionFactory.ProbePreferredWithTraceAsync(endpointId).ConfigureAwait(false);
            return FormatProbeReport(probeContext.Probe, probeContext.TraceCollector.Events);
        }

        var probe = await ProbePreferredAsync(endpointId).ConfigureAwait(false);
        if (probe.RadioInfo is null)
        {
            throw new InvalidOperationException(probe.Summary);
        }

        var info = probe.RadioInfo;
        var lines = new List<string>
        {
            $"Endpoint: {probe.Endpoint.DisplayName}",
            $"EndpointId: {probe.Endpoint.Id}",
            $"Transport: {probe.Endpoint.TransportType}",
            $"Model: {info.Identity.ModelName}",
            $"Variant: {info.Identity.Variant}",
            $"Firmware: {info.Identity.FirmwareVersion}",
            $"Bootloader: {info.Identity.BootloaderVersion}",
            $"Serial: {info.Identity.SerialNumber}",
            $"Compatibility: {info.Compatibility.Summary}",
            $"WriteSafeByDefault: {info.Compatibility.IsWriteSafeByDefault}",
        };

        if (info.Compatibility.Reasons.Count > 0)
        {
            lines.Add("Compatibility reasons:");
            lines.AddRange(info.Compatibility.Reasons.Select(static reason => $"- {reason}"));
        }

        if (includeTrace)
        {
            lines.Add(string.Empty);
            lines.Add("Notes:");
            lines.AddRange(probe.Notes.Select(static note => $"- {note}"));
        }

        return string.Join(Environment.NewLine, lines);
    }

    public async Task<string> ReadStockRadioInfoAsync(string? endpointId = null)
    {
        await using var transport = await _sessionFactory.OpenTransportAsync(endpointId).ConfigureAwait(false);
        await using var session = new StockCpsSession(transport.Connection);
        var info = await session.ReadRadioInfoAsync().ConfigureAwait(false);

        var lines = new List<string>
        {
            $"Endpoint: {transport.Endpoint.DisplayName}",
            $"EndpointId: {transport.Endpoint.Id}",
            $"Transport: {transport.Endpoint.TransportType}",
            $"Model: {info.Identity.ModelName}",
            $"Variant: {info.Identity.Variant}",
            $"Firmware: {info.Identity.FirmwareVersion}",
            $"Bootloader: {info.Identity.BootloaderVersion}",
            $"Serial: {info.Identity.SerialNumber}",
            $"Compatibility: {info.Compatibility.Summary}",
            string.Empty,
            "Trace:",
        };

        lines.AddRange(transport.TraceCollector.Events.Select(trace =>
            $"[{trace.Timestamp:O}] {trace.Level} {trace.Direction}{Environment.NewLine}{trace.Message}"));

        return string.Join(Environment.NewLine, lines);
    }

    public string NormalizeCapture(string capturePath, string? outputPath = null)
    {
        if (string.IsNullOrWhiteSpace(capturePath))
        {
            throw new ArgumentException("Capture path is required.", nameof(capturePath));
        }

        var transcriptText = File.ReadAllText(capturePath);
        var transcript = CaptureTranscriptParser.Parse(transcriptText);
        var normalized = CaptureTranscriptFormatter.Normalize(transcript);

        if (!string.IsNullOrWhiteSpace(outputPath))
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(outputPath, normalized);
            return $"Normalized transcript written to {outputPath}. Records: {transcript.Records.Count}.";
        }

        return normalized;
    }

    public string ExportRadioDataJson(string capturePath, string outputPath)
    {
        if (string.IsNullOrWhiteSpace(capturePath))
        {
            throw new ArgumentException("Capture path is required.", nameof(capturePath));
        }

        if (string.IsNullOrWhiteSpace(outputPath))
        {
            throw new ArgumentException("Output path is required.", nameof(outputPath));
        }

        var analysis = LoadCaptureAnalysis(capturePath);
        var dump = StockCpsRadioDataExporter.BuildDump(capturePath, analysis);
        var json = StockCpsRadioDataExporter.SerializeToJson(dump);

        var directory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(outputPath, json);
        return $"Exported stock CPS radio data JSON to {outputPath}. Transactions: {dump.Transactions.Count}, InfoBlocks: {dump.InfoBlocks.Count}, DataBlocks: {dump.DataBlocks.Count}.";
    }

    public string ExportRadioBinary(string capturePath, string outputPath)
    {
        if (string.IsNullOrWhiteSpace(capturePath))
        {
            throw new ArgumentException("Capture path is required.", nameof(capturePath));
        }

        if (string.IsNullOrWhiteSpace(outputPath))
        {
            throw new ArgumentException("Output path is required.", nameof(outputPath));
        }

        var analysis = LoadCaptureAnalysis(capturePath);
        var dump = StockCpsRadioDataExporter.BuildDump(capturePath, analysis);
        if (dump.ReassembledImage is null)
        {
            throw new InvalidOperationException("No reassembled radio image could be derived from the capture.");
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var imageBytes = Convert.FromHexString(dump.ReassembledImage.ImageHex);
        File.WriteAllBytes(outputPath, imageBytes);
        return $"Exported reassembled radio image to {outputPath}. BaseAddress=0x{dump.ReassembledImage.BaseAddress:X6}, Length={imageBytes.Length}, Gaps={dump.ReassembledImage.Gaps.Count}.";
    }

    public string ExportSavedCodeplugJson(string inputPath, string outputPath, string? baselinePath = null)
    {
        if (string.IsNullOrWhiteSpace(inputPath))
        {
            throw new ArgumentException("Input path is required.", nameof(inputPath));
        }

        if (string.IsNullOrWhiteSpace(outputPath))
        {
            throw new ArgumentException("Output path is required.", nameof(outputPath));
        }

        var bytes = File.ReadAllBytes(inputPath);
        var baselineBytes = string.IsNullOrWhiteSpace(baselinePath)
            ? null
            : File.ReadAllBytes(baselinePath);
        var dump = SavedCodeplugDataExporter.BuildDump(inputPath, bytes, baselinePath, baselineBytes);
        var json = SavedCodeplugDataExporter.SerializeToJson(dump);

        var directory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(outputPath, json);
        return $"Exported saved codeplug heuristic JSON to {outputPath}. Strings={dump.HeuristicDecodedCodeplug.Strings.Count}, FrequencyPairs={dump.HeuristicDecodedCodeplug.FrequencyPairs.Count}, ChannelCandidates={dump.HeuristicDecodedCodeplug.ChannelCandidates.Count}, InferredChannelTables={dump.InferredLayout.Tables.Count(static table => table.Kind == InferredStringTableKind.ChannelName)}, CandidateBinaryChannelTables={dump.CandidateChannelRecordTables.Count}, DirectChannelRecords={dump.DirectChannelRecords.Count}, ObservedChannelRecords={dump.ObservedChannelRecords.Count}, StructuredChannels={dump.StructuredChannelCandidates.Count}, StructuredContacts={dump.StructuredContactCandidates.Count}, StructuredNamedLists={dump.StructuredNamedListCandidates.Count}, DiffBytes={(dump.ChangeAnalysis?.ByteChanges.Count ?? 0)}.";
    }

    public string ExportNativeCsvFromSavedCodeplug(string inputPath, string outputDirectory, string? baselinePath = null)
    {
        if (string.IsNullOrWhiteSpace(inputPath))
        {
            throw new ArgumentException("Input path is required.", nameof(inputPath));
        }

        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            throw new ArgumentException("Output directory is required.", nameof(outputDirectory));
        }

        var bytes = File.ReadAllBytes(inputPath);
        var baselineBytes = string.IsNullOrWhiteSpace(baselinePath)
            ? null
            : File.ReadAllBytes(baselinePath);
        var dump = SavedCodeplugDataExporter.BuildDump(inputPath, bytes, baselinePath, baselineBytes);
        var image = NativeCodeplugRebuilder.BuildCodeplugImage(dump);
        var csv = new CodeplugCsvService().Export(image);

        Directory.CreateDirectory(outputDirectory);
        foreach (var file in csv)
        {
            File.WriteAllText(Path.Combine(outputDirectory, file.Key), file.Value);
        }

        return $"Exported native-style CSV from saved codeplug to {outputDirectory}. Channels={image.Channels.Count}, Zones={image.Zones.Count}, Contacts={image.Contacts.Count}, GroupLists={image.GroupLists.Count}, RxGroups={image.RxGroups.Count}, ScanLists={image.ScanLists.Count}.";
    }

    public string WriteRecoveredNativeDataFile(string inputPath, string outputPath, string? baselinePath = null)
    {
        if (string.IsNullOrWhiteSpace(inputPath))
        {
            throw new ArgumentException("Input path is required.", nameof(inputPath));
        }

        if (string.IsNullOrWhiteSpace(outputPath))
        {
            throw new ArgumentException("Output path is required.", nameof(outputPath));
        }

        var bytes = File.ReadAllBytes(inputPath);
        var baselineBytes = string.IsNullOrWhiteSpace(baselinePath)
            ? null
            : File.ReadAllBytes(baselinePath);
        var dump = SavedCodeplugDataExporter.BuildDump(inputPath, bytes, baselinePath, baselineBytes);
        var recovered = NativeDataPatcher.BuildRecoveredNativeCodeplug(dump);
        var patched = NativeDataPatcher.PatchFromRecoveredCodeplug(bytes, recovered);
        File.WriteAllBytes(outputPath, patched);
        return $"Wrote recovered native .data file to {outputPath}. ChannelsPatched={recovered.Channels.Count}.";
    }

    public string ImportCsvToNativeDataFile(string inputDirectory, string? baseDataPath, string outputPath, string? baselinePath = null)
    {
        if (string.IsNullOrWhiteSpace(inputDirectory))
        {
            throw new ArgumentException("Input directory is required.", nameof(inputDirectory));
        }

        if (string.IsNullOrWhiteSpace(outputPath))
        {
            throw new ArgumentException("Output path is required.", nameof(outputPath));
        }

        baseDataPath = string.IsNullOrWhiteSpace(baseDataPath)
            ? ResolveDefaultNativeTemplatePath()
            : baseDataPath;

        var csvFiles = Directory.GetFiles(inputDirectory, "*.csv")
            .ToDictionary(Path.GetFileName, File.ReadAllText, StringComparer.OrdinalIgnoreCase)!;
        var importResult = new CodeplugCsvService().Import(csvFiles);
        if (!importResult.Success || importResult.Value is null)
        {
            var errors = string.Join(Environment.NewLine, importResult.Issues.Select(issue => issue.Message));
            throw new InvalidOperationException($"CSV import failed.{Environment.NewLine}{errors}");
        }

        var bytes = File.ReadAllBytes(baseDataPath);
        var baselineBytes = string.IsNullOrWhiteSpace(baselinePath)
            ? null
            : File.ReadAllBytes(baselinePath);
        var baseDump = SavedCodeplugDataExporter.BuildDump(baseDataPath, bytes, baselinePath, baselineBytes);
        var recovered = ImportedCsvNativeCodeplugBuilder.Build(importResult.Value, baseDump);
        var patched = NativeDataPatcher.PatchFromRecoveredCodeplug(bytes, recovered);
        File.WriteAllBytes(outputPath, patched);
        return $"Imported CSV from {inputDirectory} and wrote native .data file to {outputPath}. ChannelsPatched={recovered.Channels.Count}.";
    }

    private static string ResolveDefaultNativeTemplatePath()
    {
        var current = new DirectoryInfo(Environment.CurrentDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "D1.00.01.001.data");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            candidate = Path.Combine(current.FullName, "artifacts", "cps", "BIN", "D1.00.01.001.data");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new FileNotFoundException("Could not locate a default native template .data file. Expected D1.00.01.001.data in the workspace root or artifacts\\cps\\BIN.");
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

    public string AnalyzeWriteSession(string tsharkFieldPath)
    {
        if (string.IsNullOrWhiteSpace(tsharkFieldPath))
        {
            throw new ArgumentException("Write-session field path is required.", nameof(tsharkFieldPath));
        }

        var lines = File.ReadAllLines(tsharkFieldPath);
        var analysis = WriteSessionAnalyzer.AnalyzeTsharkFieldLines(lines);
        var output = new List<string>
        {
            $"Write blocks: {analysis.Blocks.Count}",
            "Window write counts:",
        };

        output.AddRange(analysis.WindowWriteCounts
            .OrderBy(static pair => pair.Key)
            .Select(static pair => $"- 0x{pair.Key:X2}: {pair.Value}"));

        output.Add(string.Empty);
        output.Add("First write blocks:");
        output.AddRange(analysis.Blocks.Take(24).Select(block =>
            $"- frame={block.FrameNumber} address=0x{block.Address:X6} window=0x{block.WindowOffset:X2} ascii='{block.AsciiPreview}'"));

        return string.Join(Environment.NewLine, output);
    }

    public string AnalyzeWriteSessionAgainstCodeplug(string tsharkFieldPath, string codeplugPath)
    {
        if (string.IsNullOrWhiteSpace(tsharkFieldPath))
        {
            throw new ArgumentException("Write-session field path is required.", nameof(tsharkFieldPath));
        }

        if (string.IsNullOrWhiteSpace(codeplugPath))
        {
            throw new ArgumentException("Codeplug path is required.", nameof(codeplugPath));
        }

        var lines = File.ReadAllLines(tsharkFieldPath);
        var analysis = WriteSessionAnalyzer.AnalyzeTsharkFieldLines(lines);
        var codeplug = File.ReadAllBytes(codeplugPath);
        var mappings = WriteBlockMapper.MapToSavedCodeplug(analysis.Blocks, codeplug);

        var output = new List<string>
        {
            $"Write blocks: {analysis.Blocks.Count}",
            $"Unique file mappings: {mappings.Count(static mapping => mapping.FileOffset.HasValue)}",
            "First mapped write blocks:",
        };

        output.AddRange(mappings
            .Where(static mapping => mapping.FileOffset.HasValue)
            .Take(32)
            .Select(mapping => $"- frame={mapping.FrameNumber} writeAddress=0x{mapping.WriteAddress:X6} fileOffset=0x{mapping.FileOffset!.Value:X6} ascii='{mapping.AsciiPreview}'"));

        return string.Join(Environment.NewLine, output);
    }

    public async Task BackupCodeplugAsync(string outputPath)
    {
        await using var transport = await _sessionFactory.OpenTransportAsync().ConfigureAwait(false);

        RadioInfoResult radioInfo;
        byte[] bytes;
        if (transport.Endpoint.TransportType == TransportType.UsbPrinter)
        {
            await using var stockSession = new StockCpsSession(transport.Connection);
            radioInfo = await stockSession.ReadRadioInfoAsync().ConfigureAwait(false);
            bytes = await stockSession.ReadCodeplugAsync(cancellationToken: default).ConfigureAwait(false);
        }
        else
        {
            await transport.Connection.ResetAsync().ConfigureAwait(false);
            await using var session = new Bao1702ProtocolSession(transport.Connection);
            radioInfo = await session.ReadRadioInfoAsync().ConfigureAwait(false);
            bytes = await session.ReadFullCodeplugAsync().ConfigureAwait(false);
        }

        await File.WriteAllBytesAsync(outputPath, bytes).ConfigureAwait(false);
        _ = await new BackupLedger(GetBackupRootDirectory())
            .RecordCodeplugBackupAsync(radioInfo.Identity, bytes, $"CLI backup to {outputPath}")
            .ConfigureAwait(false);
    }

    public async Task BackupFirmwareAsync(string outputPath)
    {
        await using var session = await _sessionFactory.OpenPreferredAsync().ConfigureAwait(false);
        var bytes = await session.Session.BackupFirmwareAsync().ConfigureAwait(false);
        await File.WriteAllBytesAsync(outputPath, bytes).ConfigureAwait(false);
    }

    public async Task<string> TraceSessionAsync(string? endpointId = null)
    {
        await using var session = await _sessionFactory.OpenPreferredAsync(endpointId).ConfigureAwait(false);
        _ = await session.Session.ReadRadioInfoAsync().ConfigureAwait(false);
        _ = await session.Session.ReadCodeplugBlockAsync(0, ProtocolAssumptions.AssumedDefaultBlockSize).ConfigureAwait(false);

        return string.Join(
            Environment.NewLine + Environment.NewLine,
            session.TraceCollector.Events.Select(trace =>
                $"[{trace.Timestamp:O}] {trace.Level} {trace.Direction}{Environment.NewLine}{trace.Message}"));
    }

    public string InspectCpsInstallation(string cpsRootPath)
    {
        throw new NotSupportedException("Manufacturer-software inspection is not part of Bao1702.");
    }

    public string ProbeUsbPrinterTransport()
    {
        var snapshot = UsbPrinterTransportProbe.Capture();
        return UsbPrinterTransportProbe.Format(snapshot);
    }

    public async Task<string> OpenUsbPrinterTransportAsync()
    {
        var factory = new UsbPrinterTransportFactory();
        var endpoints = await factory.EnumerateAsync().ConfigureAwait(false);
        var endpoint = endpoints.FirstOrDefault()
            ?? throw new InvalidOperationException("No USB printer-class endpoints were found for the target VID/PID.");

        await using var connection = await factory.OpenAsync(endpoint).ConfigureAwait(false);
        var traces = new TransportTraceCollector();
        connection.TraceSink = traces;
        await connection.ConnectAsync().ConfigureAwait(false);
        await connection.DisconnectAsync().ConfigureAwait(false);

        return string.Join(Environment.NewLine,
            $"USB printer transport handle opened successfully for {endpoint.DisplayName}.",
            string.Join(Environment.NewLine, traces.Events.Select(trace => $"[{trace.Timestamp:O}] {trace.Level} {trace.Direction} {trace.Message}")));
    }

    public string AnalyzeCpsFolder(string cpsRootPath)
    {
        throw new NotSupportedException("Manufacturer executable analysis is not part of Bao1702.");
    }

    public string AnalyzeCapture(string capturePath)
    {
        if (string.IsNullOrWhiteSpace(capturePath))
        {
            throw new ArgumentException("Capture path is required.", nameof(capturePath));
        }

        var isPcap = string.Equals(Path.GetExtension(capturePath), ".pcap", StringComparison.OrdinalIgnoreCase);
        var transcript = isPcap
            ? null
            : CaptureTranscriptParser.Parse(File.ReadAllText(capturePath));
        var records = isPcap
            ? PcapFileParser.ParseRecords(File.ReadAllBytes(capturePath))
                .Select((record, index) => new CaptureRecord(index, CaptureDirection.Unknown, record, []))
                .ToArray()
            : transcript!.Records;
        var analysis = CaptureSessionAnalyzer.AnalyzeRecords(records);
        var heuristics = isPcap ? null : FrameHeuristicAnalyzer.Analyze(transcript!);
        var stockProtocol = StockCpsProtocolAnalyzer.Analyze(analysis);

        var lines = new List<string>
        {
            $"Capture file: {capturePath}",
            $"Parsed records: {records.Count}",
            $"Host->Device records: {analysis.HostToDeviceFrames}",
            $"Device->Host records: {analysis.DeviceToHostFrames}",
            $"Unknown-direction records: {analysis.TotalFrames - analysis.HostToDeviceFrames - analysis.DeviceToHostFrames}",
            $"Records matching current provisional framing: {analysis.ValidFrames}",
        };

        if (transcript is not null && transcript.Notes.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add("Parser notes:");
            lines.AddRange(transcript.Notes.Select(static note => $"- {note}"));
        }

        if (heuristics is not null)
        {
            lines.Add(string.Empty);
            lines.Add("First-byte histogram:");
            if (heuristics.FirstByteHistogram.Count == 0)
            {
                lines.Add("- none");
            }
            else
            {
                lines.AddRange(heuristics.FirstByteHistogram.Take(8).Select(static pair => $"- 0x{pair.Key:X2}: {pair.Value} record(s)"));
            }

            lines.Add(string.Empty);
            lines.Add("Framing heuristic candidates:");
            var candidateLines = heuristics.Candidates
                .Where(static candidate => candidate.MatchingLengthCount > 0)
                .Take(8)
                .Select(candidate => $"- {candidate.Summary} records=[{string.Join(',', candidate.MatchingRecordIndices)}]")
                .ToArray();
            lines.AddRange(candidateLines.Length == 0 ? ["- no sync/length candidates matched the current Sum8 framing heuristic"] : candidateLines);
        }

        if (stockProtocol.TotalUsbPayloads > 0)
        {
            lines.Add(string.Empty);
            lines.Add("Observed stock CPS payloads:");
            lines.Add($"- host payloads: {stockProtocol.HostPayloads}");
            lines.Add($"- device payloads: {stockProtocol.DevicePayloads}");
            lines.AddRange(stockProtocol.PayloadNameCounts
                .OrderByDescending(static pair => pair.Value)
                .ThenBy(static pair => pair.Key, StringComparer.Ordinal)
                .Take(12)
                .Select(static pair => $"- {pair.Key}: {pair.Value}"));

            lines.Add(string.Empty);
            lines.Add("First request/response exchanges:");
            lines.AddRange(stockProtocol.Exchanges.Take(16).Select(static exchange => $"- {exchange.Summary}"));
        }

        lines.Add(string.Empty);
        lines.Add("Per-record summary:");
        lines.AddRange(analysis.Frames.Take(24).Select(frame =>
        {
            var usbPcapSuffix = frame.UsbPcapRecord is null
                ? string.Empty
                : $" [usbpcap ep=0x{frame.UsbPcapRecord.EndpointAddress:X2} type=0x{frame.UsbPcapRecord.TransferType:X2} payload={frame.UsbPcapRecord.DataLength}]";
            return $"- [{frame.Index}] {frame.Direction} {frame.RawBytes.Length} byte(s){usbPcapSuffix} :: {frame.Summary}{(string.IsNullOrWhiteSpace(frame.DecodeError) ? string.Empty : $" :: {frame.DecodeError}")}";
        }));

        if (analysis.CommandNames.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add("Decoded protocol commands:");
            lines.AddRange(analysis.CommandNames.Select(static commandName => $"- {commandName}"));
        }

        return string.Join(Environment.NewLine, lines);
    }

    public async Task<string> RestoreCodeplugAsync(string imagePath)
    {
        var imageBytes = await File.ReadAllBytesAsync(imagePath).ConfigureAwait(false);
        await using var transport = await OpenTransportAsync().ConfigureAwait(false);
        await using var session = new StockCpsSession(transport.Connection);

        Console.WriteLine("Connecting to radio...");
        var radioInfo = await session.ReadRadioInfoAsync().ConfigureAwait(false);

        Console.WriteLine($"  Model:      {radioInfo.Identity.ModelName}");
        Console.WriteLine($"  Firmware:   {radioInfo.Identity.FirmwareVersion}");

        var imageValidation = CodeplugWriteValidator.ValidateImage(imageBytes, radioInfo.Identity.Capabilities.AssumedCodeplugSize);
        var intentValidator = new WriteIntentValidator();
        var preflight = intentValidator.BuildPreflight(new WriteIntentValidationRequest(
            radioInfo.Identity,
            RadioOperation.WriteCodeplug,
            GetBackupRootDirectory(),
            SafetyPolicyOptions.Default),
            isImageValidationAvailable: true,
            isImageValid: imageValidation.IsValid,
            expectedImageSize: imageValidation.ExpectedImageSize,
            actualImageSize: imageValidation.ActualImageSize,
            imageValidationMessages: imageValidation.Issues.Select(static issue => issue.Message).ToArray());

        if (!preflight.IsAllowed)
        {
            throw new InvalidOperationException(WritePreflightFormatter.FormatText(preflight));
        }

        Console.WriteLine("Writing codeplug...");
        var progress = new ConsoleWriteProgress();
        await session.WriteCodeplugAsync(imageBytes, progress).ConfigureAwait(false);

        Console.WriteLine();
        return string.Join(Environment.NewLine,
            WritePreflightFormatter.FormatText(preflight),
            $"Codeplug restore completed for {radioInfo.Identity.ModelName}.");
    }

    public async Task<WritePreflightReport> GetWriteCodeplugPreflightAsync(string imagePath, bool forceUnsafe = false)
    {
        var imageBytes = await File.ReadAllBytesAsync(imagePath).ConfigureAwait(false);
        await using var transport = await OpenTransportAsync().ConfigureAwait(false);
        await using var session = new StockCpsSession(transport.Connection);
        var radioInfo = await session.ReadRadioInfoAsync().ConfigureAwait(false);
        var imageValidation = CodeplugWriteValidator.ValidateImage(imageBytes, radioInfo.Identity.Capabilities.AssumedCodeplugSize);
        return new WriteIntentValidator().BuildPreflight(
            new WriteIntentValidationRequest(
                radioInfo.Identity,
                RadioOperation.WriteCodeplug,
                GetBackupRootDirectory(),
                SafetyPolicyOptions.Default with { ForceUnsafe = forceUnsafe }),
            isImageValidationAvailable: true,
            isImageValid: imageValidation.IsValid,
            expectedImageSize: imageValidation.ExpectedImageSize,
            actualImageSize: imageValidation.ActualImageSize,
            imageValidationMessages: imageValidation.Issues.Select(static issue => issue.Message).ToArray());
    }

    public string ExportCsv(string imagePath, string outputDirectory)
    {
        var imageBytes = File.ReadAllBytes(imagePath);
        var parsedImage = TryReadCodeplug(imageBytes);
        new CodeplugCsvService().ExportToDirectory(parsedImage, outputDirectory);

        return $"Exported CSV files to {outputDirectory}.";
    }

    public string ImportCsv(string inputDirectory, string outputPath)
    {
        var result = new CodeplugCsvService().ImportFromDirectory(inputDirectory);
        if (!result.Success || result.Value is null)
        {
            var errors = string.Join(Environment.NewLine, result.Issues.Select(issue => issue.Message));
            throw new InvalidOperationException($"CSV import failed.{Environment.NewLine}{errors}");
        }

        var bytes = CodeplugBinarySerializer.Serialize(result.Value);
        File.WriteAllBytes(outputPath, bytes);
        return $"Wrote provisional codeplug image to {outputPath}.";
    }

    public string ImportCsvToNativeImageFromScratch(string inputDirectory, string outputPath)
    {
        var result = new CodeplugCsvService().ImportFromDirectory(inputDirectory);
        if (!result.Success || result.Value is null)
        {
            var errors = string.Join(Environment.NewLine, result.Issues.Select(issue => issue.Message));
            throw new InvalidOperationException($"CSV import failed.{Environment.NewLine}{errors}");
        }

        var bytes = Dm1702NativeImageBuilder.Build(result.Value);
        File.WriteAllBytes(outputPath, bytes);
        return $"Wrote native DM1702 codeplug image to {outputPath}.";
    }

    public string VerifyImage(string imagePath)
    {
        var bytes = File.ReadAllBytes(imagePath);

        if (bytes.Length == Dm1702NativeImageAssumptions.ImageLength)
        {
            var codeplug = Dm1702NativeImageReader.ReadFromNative(bytes);
            return $"Native DM-1702 codeplug image OK. Channels: {codeplug.Channels.Count}, Zones: {codeplug.Zones.Count}, Contacts: {codeplug.Contacts.Count}, RxGroups: {codeplug.RxGroups.Count}, ScanLists: {codeplug.ScanLists.Count}.";
        }

        try
        {
            var codeplug = CodeplugBinarySerializer.Deserialize(bytes);
            return $"Codeplug image OK. Channels: {codeplug.Channels.Count}, Zones: {codeplug.Zones.Count}, Contacts: {codeplug.Contacts.Count}.";
        }
        catch (InvalidDataException)
        {
            var firmware = FirmwareImageParser.Analyze(bytes);
            return string.Join(Environment.NewLine,
                $"Firmware-like image signature: {firmware.Image.Header.Signature}",
                $"Declared length: {firmware.Image.Header.DeclaredLength}",
                $"Checksums: {string.Join(", ", firmware.Checksums.Select(checksum => $"{checksum.Algorithm}={checksum.Value}"))}",
                $"Warnings: {string.Join("; ", firmware.Warnings.DefaultIfEmpty("none"))}");
        }
    }

    public string DiffFirmware(string leftPath, string rightPath)
    {
        var left = File.ReadAllBytes(leftPath);
        var right = File.ReadAllBytes(rightPath);
        var diff = FirmwareImageParser.Diff(left, right);
        var preview = string.Join(Environment.NewLine, diff.Differences.Take(16).Select(entry => $"0x{entry.Offset:X8}: 0x{entry.Left:X2} -> 0x{entry.Right:X2}"));
        return string.Join(Environment.NewLine,
            $"Firmware diff complete. Differences: {diff.TotalDifferences}.",
            string.IsNullOrWhiteSpace(preview) ? "No byte-level differences." : preview);
    }

    private static CodeplugImage TryReadCodeplug(byte[] imageBytes)
    {
        if (imageBytes.Length == Dm1702NativeImageAssumptions.ImageLength)
        {
            return Dm1702NativeImageReader.ReadFromNative(imageBytes);
        }

        try
        {
            return CodeplugBinarySerializer.Deserialize(imageBytes);
        }
        catch (InvalidDataException)
        {
            return CodeplugImage.CreateEmpty() with { PreservedRawImage = imageBytes };
        }
    }

    public Task<RadioProbeResult> ProbePreferredAsync(string? endpointId = null, CancellationToken cancellationToken = default)
    {
        return _sessionFactory.ProbePreferredAsync(endpointId, cancellationToken);
    }

    private static string FormatProbeReport(RadioProbeResult probe, IReadOnlyList<TransportTraceEvent> traces)
    {
        var lines = new List<string>
        {
            $"Endpoint: {probe.Endpoint.DisplayName}",
            $"EndpointId: {probe.Endpoint.Id}",
            $"Transport: {probe.Endpoint.TransportType}",
            $"Reachable: {probe.IsReachable}",
            $"Summary: {probe.Summary}",
        };

        if (probe.RadioInfo is not null)
        {
            lines.Add($"Model: {probe.RadioInfo.Identity.ModelName}");
            lines.Add($"Variant: {probe.RadioInfo.Identity.Variant}");
            lines.Add($"Firmware: {probe.RadioInfo.Identity.FirmwareVersion}");
            lines.Add($"Bootloader: {probe.RadioInfo.Identity.BootloaderVersion}");
            lines.Add($"Serial: {probe.RadioInfo.Identity.SerialNumber}");
            lines.Add($"Compatibility: {probe.RadioInfo.Compatibility.Summary}");
            lines.Add($"WriteSafeByDefault: {probe.RadioInfo.Compatibility.IsWriteSafeByDefault}");
        }

        if (probe.Notes.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add("Notes:");
            lines.AddRange(probe.Notes.Select(static note => $"- {note}"));
        }

        if (probe.Error is not null)
        {
            lines.Add(string.Empty);
            lines.Add($"ErrorType: {probe.Error.GetType().Name}");
            lines.Add($"Error: {probe.Error.Message}");
        }

        lines.Add(string.Empty);
        lines.Add("Trace:");
        if (traces.Count == 0)
        {
            lines.Add("- none");
        }
        else
        {
            lines.AddRange(traces.Select(trace => $"[{trace.Timestamp:O}] {trace.Level} {trace.Direction}{Environment.NewLine}{trace.Message}"));
        }

        return string.Join(Environment.NewLine, lines);
    }

    internal async Task<CliTransportContext> OpenTransportAsync(string? endpointId = null, CancellationToken cancellationToken = default)
    {
        return await _sessionFactory.OpenTransportAsync(endpointId, cancellationToken).ConfigureAwait(false);
    }

    internal static string GetBackupRootDirectory()
    {
        return Path.Combine(AppContext.BaseDirectory, "backups", "ledger");
    }

    private sealed class ConsoleWriteProgress : IProgress<ProtocolProgress>
    {
        private int _lastPercent = -1;

        public void Report(ProtocolProgress value)
        {
            var percent = (int)value.PercentComplete;
            if (percent == _lastPercent)
            {
                return;
            }

            _lastPercent = percent;
            Console.Write($"\r  Progress: {value.CurrentBlock}/{value.TotalBlocks} blocks ({percent}%)");
            if (percent >= 100)
            {
                Console.WriteLine();
            }
        }
    }
}
