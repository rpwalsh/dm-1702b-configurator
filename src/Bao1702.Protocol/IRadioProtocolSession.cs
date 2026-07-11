using Bao1702.Protocol.Model;

namespace Bao1702.Protocol;

/// <summary>
/// Async interface for communicating with a DM-1702 radio over USB.
/// Supports radio identification, codeplug read/write, and firmware backup.
/// </summary>
public interface IRadioProtocolSession : IAsyncDisposable
{
    Task<RadioInfoResult> ReadRadioInfoAsync(CancellationToken cancellationToken = default);

    Task EnterProgrammingModeAsync(CancellationToken cancellationToken = default);

    Task ExitProgrammingModeAsync(CancellationToken cancellationToken = default);

    Task<byte[]> ReadCodeplugBlockAsync(int address, int length, CancellationToken cancellationToken = default);

    Task WriteCodeplugBlockAsync(int address, ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default);

    Task<byte[]> ReadFirmwareBlockAsync(int address, int length, CancellationToken cancellationToken = default);

    Task WriteFirmwareBlockAsync(int address, ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default);

    Task<byte[]> ReadFullCodeplugAsync(CancellationToken cancellationToken = default);

    Task<byte[]> ReadFullCodeplugAsync(IProgress<ProtocolProgress>? progress, CancellationToken cancellationToken = default);

    Task WriteFullCodeplugAsync(ReadOnlyMemory<byte> image, CancellationToken cancellationToken = default);

    Task WriteFullCodeplugAsync(ReadOnlyMemory<byte> image, IProgress<ProtocolProgress>? progress, CancellationToken cancellationToken = default);

    Task<byte[]> BackupFirmwareAsync(CancellationToken cancellationToken = default);

    Task<byte[]> BackupFirmwareAsync(IProgress<ProtocolProgress>? progress, CancellationToken cancellationToken = default);

    Task<DateTimeOffset> ReadRtcAsync(CancellationToken cancellationToken = default);

    Task WriteRtcAsync(DateTimeOffset value, CancellationToken cancellationToken = default);

    Task<CompatibilityResult> ValidateTargetCompatibilityAsync(CancellationToken cancellationToken = default);
}
