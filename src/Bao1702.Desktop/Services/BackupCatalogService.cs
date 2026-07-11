using System.IO;
using Bao1702.Protocol.Model;
using Bao1702.Protocol.Safety;

namespace Bao1702.Desktop.Services;

/// <summary>
/// Desktop service for querying the codeplug and firmware backup ledger.
/// </summary>
public sealed class BackupCatalogService
{
    public string BackupRootDirectory { get; } = Path.Combine(AppContext.BaseDirectory, "backups", "ledger");

    public IReadOnlyList<BackupRecord> ListCodeplugBackups(RadioIdentity? identity = null)
    {
        var ledger = new BackupLedger(BackupRootDirectory);
        return ledger.ListBackups(identity, backupKind: "codeplug");
    }

    public BackupRecord? GetLatestCodeplugBackup(RadioIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        var ledger = new BackupLedger(BackupRootDirectory);
        return ledger.FindLatestCodeplugBackup(identity);
    }
}
