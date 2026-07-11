using System.Security.Cryptography;
using Bao1702.Cli.Infrastructure;
using Bao1702.Protocol.Model;
using Bao1702.Protocol.Safety;
using Bao1702.Protocol.Stock;

namespace Bao1702.Cli.Commands;

/// <summary>
/// CLI command that connects to a radio via the stock CPS protocol,
/// reads the full codeplug image with live progress, and saves
/// the raw binary to disk with a backup ledger entry.
/// </summary>
internal sealed class ReadCodeplugCommand
{
    private readonly CliRuntime _runtime;

    public ReadCodeplugCommand(CliRuntime runtime)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
    }

    public async Task<string> ExecuteAsync(string outputPath, string? endpointId = null, bool includeTrace = false)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            throw new ArgumentException("Output path is required.", nameof(outputPath));
        }

        if (!outputPath.EndsWith(".data", StringComparison.OrdinalIgnoreCase))
        {
            outputPath = Path.ChangeExtension(outputPath, ".data");
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var transport = await _runtime.OpenTransportAsync(endpointId).ConfigureAwait(false);

        await using var session = new StockCpsSession(transport.Connection);

        Console.WriteLine("Connecting to radio...");
        var radioInfo = await session.ReadRadioInfoAsync().ConfigureAwait(false);

        Console.WriteLine($"  Model:      {radioInfo.Identity.ModelName}");
        Console.WriteLine($"  Variant:    {radioInfo.Identity.Variant}");
        Console.WriteLine($"  Firmware:   {radioInfo.Identity.FirmwareVersion}");
        Console.WriteLine($"  Bootloader: {radioInfo.Identity.BootloaderVersion}");
        Console.WriteLine($"  Serial:     {radioInfo.Identity.SerialNumber ?? "(none)"}");
        Console.WriteLine($"  Compatible: {radioInfo.Compatibility.Summary}");
        Console.WriteLine();

        Console.WriteLine("Reading codeplug...");
        var progress = new ConsoleProgress();
        var image = await session.ReadCodeplugAsync(progress).ConfigureAwait(false);

        Console.WriteLine();
        Console.WriteLine($"  Image size: {image.Length:N0} bytes");

        var sha256 = Convert.ToHexString(SHA256.HashData(image));
        Console.WriteLine($"  SHA-256:    {sha256}");

        await File.WriteAllBytesAsync(outputPath, image).ConfigureAwait(false);
        Console.WriteLine($"  Saved to:   {Path.GetFullPath(outputPath)}");

        var ledger = new BackupLedger(CliRuntime.GetBackupRootDirectory());
        var record = await ledger.RecordCodeplugBackupAsync(
            radioInfo.Identity,
            image,
            $"CLI read-codeplug to {outputPath}").ConfigureAwait(false);
        Console.WriteLine($"  Backup ID:  {record.BackupId}");

        if (includeTrace)
        {
            Console.WriteLine();
            Console.WriteLine("Transport trace:");
            foreach (var trace in transport.TraceCollector.Events)
            {
                Console.WriteLine($"  [{trace.Timestamp:O}] {trace.Level} {trace.Direction}");
                Console.WriteLine($"  {trace.Message}");
            }
        }

        return $"Codeplug read complete. {image.Length:N0} bytes saved to {Path.GetFullPath(outputPath)}.";
    }

    private sealed class ConsoleProgress : IProgress<ProtocolProgress>
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
