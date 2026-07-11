using System.IO;
using Bao1702.Codeplug.Validation;
using Bao1702.Protocol;
using Bao1702.Protocol.Discovery;
using Bao1702.Protocol.Model;
using Bao1702.Protocol.Safety;
using Bao1702.Protocol.Stock;
using Bao1702.Transport.Abstractions;
using Bao1702.Transport.Mock;
using Bao1702.Transport.Serial;
using Bao1702.Transport.UsbPrinter;

namespace Bao1702.Desktop.Services;

/// <summary>
/// Orchestrates radio connections, codeplug reads/writes, backup creation,
/// and firmware operations for the Desktop CPS application.
/// </summary>
public sealed class DeviceSessionService
{
    private readonly BackupCatalogService _backupCatalogService = new();
    private readonly MockRadioDevice _mockDevice = new();
    private readonly SerialTransportFactory _serialTransportFactory = new();
    private readonly UsbPrinterTransportFactory _usbPrinterTransportFactory = new();
    private ITransportConnection? _activeConnection;
    private StockCpsSession? _activeStockSession;
    private Bao1702ProtocolSession? _activeManagedSession;

    /// <summary>The most recently probed identity, if any.</summary>
    public RadioIdentity? ActiveIdentity { get; private set; }

    /// <summary>The most recently used endpoint.</summary>
    public TransportEndpoint? ActiveEndpoint { get; private set; }

    /// <summary>The trace collector from the most recent operation. Replaced on each connect.</summary>
    public TransportTraceCollector? LastTraceCollector { get; private set; }

    public async Task<IReadOnlyList<TransportEndpoint>> EnumerateDevicesAsync(CancellationToken cancellationToken = default)
    {
        var devices = new List<TransportEndpoint>();
        devices.AddRange(await _usbPrinterTransportFactory.EnumerateAsync(cancellationToken).ConfigureAwait(false));
        devices.AddRange(await _serialTransportFactory.EnumerateAsync(cancellationToken).ConfigureAwait(false));
        devices.AddRange(await CreateMockFactory().EnumerateAsync(cancellationToken).ConfigureAwait(false));
        return devices
            .OrderBy(static endpoint => endpoint.TransportType switch
            {
                TransportType.UsbPrinter => 0,
                TransportType.Serial => 1,
                TransportType.Mock => 2,
                _ => 3,
            })
            .ThenBy(static endpoint => endpoint.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task ResetTransportAsync(CancellationToken cancellationToken = default)
    {
        if (_activeConnection is not null)
        {
            await _activeConnection.ResetAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        var endpoint = ActiveEndpoint;
        if (endpoint is null)
        {
            return;
        }

        var factory = ResolveFactory(endpoint);
        await using var connection = await factory.OpenAsync(endpoint, TransportTimeouts.Default, cancellationToken).ConfigureAwait(false);
        await connection.ConnectAsync(cancellationToken).ConfigureAwait(false);
        await connection.ResetAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<RadioProbeResult> ConnectAndReadInfoAsync(TransportEndpoint? preferredEndpoint = null, CancellationToken cancellationToken = default)
    {
        var endpoint = preferredEndpoint
            ?? (await EnumerateDevicesAsync(cancellationToken).ConfigureAwait(false)).FirstOrDefault()
            ?? throw new InvalidOperationException("No radio transport endpoints were found.");

        await DisposeActiveSessionAsync().ConfigureAwait(false);

        ActiveIdentity = null;
        ActiveEndpoint = endpoint;
        var factory = ResolveFactory(endpoint);
        var traceCollector = CreateTraceCollector();

        try
        {
            _activeConnection = await factory.OpenAsync(endpoint, TransportTimeouts.Default, cancellationToken).ConfigureAwait(false);
            _activeConnection.TraceSink = traceCollector;
            await _activeConnection.ConnectAsync(cancellationToken).ConfigureAwait(false);

            RadioInfoResult info;
            if (endpoint.TransportType == TransportType.UsbPrinter)
            {
                _activeStockSession = new StockCpsSession(_activeConnection);
                info = await _activeStockSession.ReadRadioInfoAsync(cancellationToken).ConfigureAwait(false);
            }
            else
            {
                _activeManagedSession = new Bao1702ProtocolSession(
                    _activeConnection,
                    options: new ProtocolSessionOptions(SafetyPolicyOptions.Default with { BackupCompleted = true }, ProtocolAssumptions.AssumedDefaultBlockSize));
                await _activeManagedSession.EnterProgrammingModeAsync(cancellationToken).ConfigureAwait(false);
                info = await _activeManagedSession.ReadRadioInfoAsync(cancellationToken).ConfigureAwait(false);
            }

            ActiveIdentity = info.Identity;
            var notes = new List<string>
            {
                $"Compatibility: {info.Compatibility.Summary}",
                $"Identity confidence: {info.Identity.Confidence}",
                $"Endpoint transport: {endpoint.TransportType}",
            };
            notes.AddRange(info.Compatibility.Reasons);

            return new RadioProbeResult(
                endpoint,
                info,
                IsReachable: true,
                Summary: $"Detected {info.Identity.ModelName} via {endpoint.DisplayName}.",
                Notes: notes);
        }
        catch (OperationCanceledException)
        {
            await DisposeActiveSessionAsync().ConfigureAwait(false);
            ActiveEndpoint = endpoint;
            throw;
        }
        catch (Exception ex)
        {
            await DisposeActiveSessionAsync().ConfigureAwait(false);
            ActiveEndpoint = endpoint;
            return new RadioProbeResult(
                endpoint,
                RadioInfo: null,
                IsReachable: false,
                Summary: $"Probe failed on {endpoint.DisplayName}: {ex.Message}",
                Notes: ["No write-capable decision should be made from a failed probe."],
                Error: ex);
        }
    }

    public async Task<string> BackupCodeplugAsync(
        string outputDirectory,
        IProgress<ProtocolProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            throw new ArgumentException("Output directory is required.", nameof(outputDirectory));
        }

        Directory.CreateDirectory(outputDirectory);
        var endpoint = ActiveEndpoint
            ?? (await EnumerateDevicesAsync(cancellationToken).ConfigureAwait(false)).FirstOrDefault()
            ?? throw new InvalidOperationException("No radio transport endpoints were found.");

        await EnsureActiveSessionAsync(endpoint, cancellationToken).ConfigureAwait(false);

        // Use the identity cached from the Connect handshake — do NOT re-send
        // PSEARCH/G/V commands. The OEM CPS performs the handshake exactly once
        // per session; re-probing confuses the STM32 and resets the radio.
        var identity = ActiveIdentity
            ?? throw new InvalidOperationException("No radio identity is available. Connect to the radio first.");

        byte[] codeplug;
        if (endpoint.TransportType == TransportType.UsbPrinter)
        {
            if (_activeStockSession is null)
            {
                throw new InvalidOperationException("Stock CPS session is not active.");
            }

            codeplug = await _activeStockSession.ReadCodeplugAsync(progress, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            if (_activeManagedSession is null)
            {
                throw new InvalidOperationException("Managed protocol session is not active.");
            }

            codeplug = await _activeManagedSession.ReadFullCodeplugAsync(progress, cancellationToken).ConfigureAwait(false);
        }

        var ledger = new BackupLedger(_backupCatalogService.BackupRootDirectory);
        var record = await ledger.RecordCodeplugBackupAsync(identity, codeplug, "Desktop backup workflow", cancellationToken).ConfigureAwait(false);

        var filePath = Path.Combine(outputDirectory, Path.GetFileName(record.ImagePath));
        File.Copy(record.ImagePath, filePath, overwrite: true);
        return filePath;
    }

    public async Task<string> BackupFirmwareAsync(
        string outputDirectory,
        IProgress<ProtocolProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            throw new ArgumentException("Output directory is required.", nameof(outputDirectory));
        }

        Directory.CreateDirectory(outputDirectory);
        var endpoint = ActiveEndpoint
            ?? (await EnumerateDevicesAsync(cancellationToken).ConfigureAwait(false)).FirstOrDefault()
            ?? throw new InvalidOperationException("No radio transport endpoints were found.");

        await EnsureActiveSessionAsync(endpoint, cancellationToken).ConfigureAwait(false);

        if (endpoint.TransportType == TransportType.UsbPrinter)
        {
            throw new InvalidOperationException("Managed firmware backup is not implemented for the stock USB-printer CPS path. Capture-driven mapping is still required.");
        }

        if (_activeManagedSession is null)
        {
            throw new InvalidOperationException("Managed protocol session is not active.");
        }

        var firmware = await _activeManagedSession.BackupFirmwareAsync(progress, cancellationToken).ConfigureAwait(false);
        var filePath = Path.Combine(outputDirectory, $"firmware-backup-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.bin");
        await File.WriteAllBytesAsync(filePath, firmware, cancellationToken).ConfigureAwait(false);
        return filePath;
    }

    public async Task<string> RestoreCodeplugAsync(
        string imagePath,
        IProgress<ProtocolProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(imagePath))
        {
            throw new ArgumentException("Codeplug image path is required.", nameof(imagePath));
        }

        var imageBytes = await File.ReadAllBytesAsync(imagePath, cancellationToken).ConfigureAwait(false);
        return await RestoreCodeplugAsync(imageBytes, progress, cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> RestoreCodeplugAsync(
        byte[] imageBytes,
        IProgress<ProtocolProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(imageBytes);

        var endpoint = ActiveEndpoint
            ?? (await EnumerateDevicesAsync(cancellationToken).ConfigureAwait(false)).FirstOrDefault()
            ?? throw new InvalidOperationException("No radio transport endpoints were found.");

        await EnsureActiveSessionAsync(endpoint, cancellationToken).ConfigureAwait(false);

        // Use cached identity — do NOT re-send PSEARCH/G/V handshake commands.
        var identity = ActiveIdentity
            ?? throw new InvalidOperationException("No radio identity is available. Connect to the radio first.");

        var imageValidation = CodeplugWriteValidator.ValidateImage(imageBytes, identity.Capabilities.AssumedCodeplugSize);
        if (!imageValidation.IsValid)
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine, imageValidation.Issues.Select(issue => issue.Message)));
        }

        var intent = new WriteIntentValidator().Validate(new WriteIntentValidationRequest(
            identity,
            RadioOperation.WriteCodeplug,
            _backupCatalogService.BackupRootDirectory,
            SafetyPolicyOptions.Default));

        if (!intent.IsAllowed)
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine, new[] { intent.Summary }.Concat(intent.Reasons)));
        }

        if (endpoint.TransportType == TransportType.UsbPrinter)
        {
            if (_activeStockSession is null)
            {
                throw new InvalidOperationException("Stock CPS session is not active.");
            }

            await _activeStockSession.WriteCodeplugAsync(imageBytes, progress, cancellationToken).ConfigureAwait(false);
            return $"USB write completed for {identity.ModelName} (backup reference: {intent.LatestBackup!.BackupId}).";
        }

        if (_activeManagedSession is null)
        {
            throw new InvalidOperationException("Managed protocol session is not active.");
        }

        await _activeManagedSession.WriteFullCodeplugAsync(imageBytes, progress, cancellationToken).ConfigureAwait(false);
        return $"Managed restore completed using backup reference {intent.LatestBackup?.BackupId ?? "none"}.";
    }

    public BackupRecord? GetLatestCodeplugBackup(RadioIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        return _backupCatalogService.GetLatestCodeplugBackup(identity);
    }

    internal static string GetBackupRootDirectory()
    {
        return Path.Combine(AppContext.BaseDirectory, "backups", "ledger");
    }

    private ITransportFactory ResolveFactory(TransportEndpoint endpoint)
        => endpoint.TransportType switch
        {
            TransportType.UsbPrinter => _usbPrinterTransportFactory,
            TransportType.Serial => _serialTransportFactory,
            _ => CreateMockFactory(),
        };

    private TransportTraceCollector CreateTraceCollector()
    {
        var collector = new TransportTraceCollector();
        LastTraceCollector = collector;
        return collector;
    }

    private async Task EnsureActiveSessionAsync(TransportEndpoint endpoint, CancellationToken cancellationToken)
    {
        if (_activeConnection is not null
            && _activeConnection.IsOpen
            && ActiveEndpoint is not null
            && string.Equals(ActiveEndpoint.Id, endpoint.Id, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // Connection is missing, closed, or targeting a different endpoint — reconnect.
        await ConnectAndReadInfoAsync(endpoint, cancellationToken).ConfigureAwait(false);
    }

    private async Task DisposeActiveSessionAsync()
    {
        if (_activeManagedSession is not null)
        {
            await _activeManagedSession.DisposeAsync().ConfigureAwait(false);
            _activeManagedSession = null;
            _activeConnection = null;
        }

        if (_activeStockSession is not null)
        {
            await _activeStockSession.DisposeAsync().ConfigureAwait(false);
            _activeStockSession = null;
            _activeConnection = null;
        }

        if (_activeConnection is not null)
        {
            await _activeConnection.DisposeAsync().ConfigureAwait(false);
            _activeConnection = null;
        }

        ActiveIdentity = null;
    }

    private MockTransportFactory CreateMockFactory() => new(_mockDevice.Handle);
}
