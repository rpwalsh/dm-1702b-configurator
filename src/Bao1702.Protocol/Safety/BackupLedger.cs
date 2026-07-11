using System.Security.Cryptography;
using System.Text.Json;
using Bao1702.Protocol.Model;

namespace Bao1702.Protocol.Safety;

/// <summary>
/// Immutable record of a codeplug or firmware backup, including integrity hash.
/// </summary>
public sealed record BackupRecord(
    string BackupId,
    string BackupKind,
    DateTimeOffset CreatedUtc,
    string ModelName,
    RadioVariant Variant,
    string? SerialNumber,
    string FirmwareVersion,
    string BootloaderVersion,
    int ImageLength,
    string Sha256,
    string ImagePath,
    string ManifestPath,
    string Source);

public sealed class BackupLedger
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    public BackupLedger(string rootDirectory)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory))
        {
            throw new ArgumentException("Backup root directory is required.", nameof(rootDirectory));
        }

        RootDirectory = rootDirectory;
    }

    public string RootDirectory { get; }

    public async Task<BackupRecord> RecordCodeplugBackupAsync(
        RadioIdentity identity,
        ReadOnlyMemory<byte> image,
        string source,
        CancellationToken cancellationToken = default)
    {
        return await RecordAsync(identity, image, backupKind: "codeplug", ".data", source, cancellationToken).ConfigureAwait(false);
    }

    public async Task<BackupRecord> RecordFirmwareBackupAsync(
        RadioIdentity identity,
        ReadOnlyMemory<byte> image,
        string source,
        CancellationToken cancellationToken = default)
    {
        return await RecordAsync(identity, image, backupKind: "firmware", ".fw", source, cancellationToken).ConfigureAwait(false);
    }

    public bool HasCodeplugBackup(RadioIdentity identity)
    {
        return FindLatestCodeplugBackup(identity) is not null;
    }

    public BackupRecord? FindLatestCodeplugBackup(RadioIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        return ListBackups(identity, backupKind: "codeplug")
            .OrderByDescending(record => record.CreatedUtc)
            .FirstOrDefault();
    }

    public bool HasFirmwareBackup(RadioIdentity identity)
    {
        return FindLatestFirmwareBackup(identity) is not null;
    }

    public BackupRecord? FindLatestFirmwareBackup(RadioIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        return ListBackups(identity, backupKind: "firmware")
            .OrderByDescending(record => record.CreatedUtc)
            .FirstOrDefault();
    }

    public IReadOnlyList<BackupRecord> ListBackups(RadioIdentity? identity = null, string? backupKind = null)
    {
        Directory.CreateDirectory(RootDirectory);
        var manifests = Directory.GetFiles(RootDirectory, "*.json", SearchOption.TopDirectoryOnly);
        var records = new List<BackupRecord>();
        foreach (var manifestPath in manifests)
        {
            if (TryLoadManifest(manifestPath, identity, backupKind) is { } record)
            {
                records.Add(record);
            }
        }

        return records.OrderByDescending(record => record.CreatedUtc).ToArray();
    }

    public async Task<IReadOnlyList<BackupRecord>> ListBackupsAsync(RadioIdentity? identity = null, string? backupKind = null, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(RootDirectory);
        var manifests = Directory.GetFiles(RootDirectory, "*.json", SearchOption.TopDirectoryOnly);
        var records = new List<BackupRecord>();
        foreach (var manifestPath in manifests)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var manifestJson = await File.ReadAllTextAsync(manifestPath, cancellationToken).ConfigureAwait(false);
                var record = JsonSerializer.Deserialize<BackupRecord>(manifestJson, JsonOptions);
                if (record is null)
                {
                    continue;
                }

                if (!MatchesFilter(record, identity, backupKind))
                {
                    continue;
                }

                records.Add(record);
            }
            catch (JsonException)
            {
            }
            catch (IOException)
            {
            }
        }

        return records.OrderByDescending(record => record.CreatedUtc).ToArray();
    }

    private static BackupRecord? TryLoadManifest(string manifestPath, RadioIdentity? identity, string? backupKind)
    {
        try
        {
            var manifestJson = File.ReadAllText(manifestPath);
            var record = JsonSerializer.Deserialize<BackupRecord>(manifestJson, JsonOptions);
            if (record is null || !MatchesFilter(record, identity, backupKind))
            {
                return null;
            }

            return record;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static bool MatchesFilter(BackupRecord record, RadioIdentity? identity, string? backupKind)
    {
        if (identity is not null)
        {
            var identityMatches = record.Variant == identity.Variant
                && string.Equals(record.SerialNumber ?? string.Empty, identity.SerialNumber ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            if (!identityMatches)
            {
                return false;
            }
        }

        if (!string.IsNullOrWhiteSpace(backupKind)
            && !string.Equals(record.BackupKind, backupKind, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    private async Task<BackupRecord> RecordAsync(
        RadioIdentity identity,
        ReadOnlyMemory<byte> image,
        string backupKind,
        string extension,
        string source,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(identity);
        if (string.IsNullOrWhiteSpace(backupKind))
        {
            throw new ArgumentException("Backup kind is required.", nameof(backupKind));
        }

        Directory.CreateDirectory(RootDirectory);
        var createdUtc = DateTimeOffset.UtcNow;
        var backupId = $"{backupKind}-{createdUtc:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}";
        var imagePath = Path.Combine(RootDirectory, backupId + extension);
        var manifestPath = Path.Combine(RootDirectory, backupId + ".json");
        var imageBytes = image.ToArray();
        await File.WriteAllBytesAsync(imagePath, imageBytes, cancellationToken).ConfigureAwait(false);

        var sha256 = Convert.ToHexString(SHA256.HashData(imageBytes));
        var record = new BackupRecord(
            backupId,
            backupKind,
            createdUtc,
            identity.ModelName,
            identity.Variant,
            identity.SerialNumber,
            identity.FirmwareVersion.RawValue,
            identity.BootloaderVersion.RawValue,
            imageBytes.Length,
            sha256,
            imagePath,
            manifestPath,
            source);

        var manifestJson = JsonSerializer.Serialize(record, JsonOptions);
        await File.WriteAllTextAsync(manifestPath, manifestJson, cancellationToken).ConfigureAwait(false);
        return record;
    }
}
