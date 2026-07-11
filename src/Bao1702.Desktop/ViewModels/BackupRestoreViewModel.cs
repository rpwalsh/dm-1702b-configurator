using Bao1702.Protocol.Model;
using Bao1702.Protocol.Safety;

namespace Bao1702.Desktop.ViewModels;

/// <summary>
/// View model for the backup and restore status panel in the right sidebar.
/// Tracks backup state, write preflight results, and catalog entries.
/// </summary>
public sealed class BackupRestoreViewModel : ObservableObject
{
    private string _statusMessage = "No backup has been recorded for the active target yet.";
    private string _latestBackupSummary = "No backup catalog entry.";
    private string _latestBackupPath = "N/A";
    private string _writePreflightSummary = "No write preflight has been executed yet.";
    private string _writePreflightDetails = "Preflight details will appear before any write-capable operation.";

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public string LatestBackupSummary
    {
        get => _latestBackupSummary;
        set => SetProperty(ref _latestBackupSummary, value);
    }

    public string LatestBackupPath
    {
        get => _latestBackupPath;
        set => SetProperty(ref _latestBackupPath, value);
    }

    public string WritePreflightSummary
    {
        get => _writePreflightSummary;
        set => SetProperty(ref _writePreflightSummary, value);
    }

    public string WritePreflightDetails
    {
        get => _writePreflightDetails;
        set => SetProperty(ref _writePreflightDetails, value);
    }

    public void UpdateFromBackup(RadioIdentity identity, BackupRecord? backupRecord)
    {
        ArgumentNullException.ThrowIfNull(identity);

        if (backupRecord is null)
        {
            LatestBackupSummary = $"No backup exists yet for {identity.ModelName} ({identity.SerialNumber ?? "no-serial"}).";
            LatestBackupPath = "N/A";
            StatusMessage = "Writes remain blocked until a backup is captured and recorded.";
            return;
        }

        LatestBackupSummary = $"{backupRecord.BackupKind} backup {backupRecord.BackupId} captured {backupRecord.CreatedUtc:yyyy-MM-dd HH:mm:ss} UTC, {backupRecord.ImageLength} bytes.";
        LatestBackupPath = backupRecord.ImagePath;
        StatusMessage = "Backup catalog requirement satisfied for managed codeplug writes.";
    }

    public void UpdatePreflight(WritePreflightReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        WritePreflightSummary = report.Summary;
        WritePreflightDetails = WritePreflightFormatter.FormatText(report);
    }

    public void ClearPreflight(string summary = "No write preflight has been executed yet.")
    {
        WritePreflightSummary = summary;
        WritePreflightDetails = "Preflight details will appear before any write-capable operation.";
    }

    public void SetStatus(string message)
    {
        StatusMessage = message;
    }
}
