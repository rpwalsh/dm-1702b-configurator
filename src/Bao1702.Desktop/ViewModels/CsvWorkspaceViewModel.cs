using System.Collections.ObjectModel;
using System.IO;
using Bao1702.Codeplug.Csv;
using Bao1702.Codeplug.Model;
using Bao1702.Codeplug.Validation;
using Bao1702.Desktop.Commands;

namespace Bao1702.Desktop.ViewModels;

/// <summary>View model for CSV import/export operations on the active codeplug.</summary>
public sealed class CsvWorkspaceViewModel : ObservableObject
{
    private readonly CodeplugCsvService _csvService = new();
    private string _importSummary = "No CSV import performed yet.";
    private string _exportSummary = "No CSV export performed yet.";
    private string _lastExportDirectory = string.Empty;
    private string _lastImportDirectory = string.Empty;

    public CsvWorkspaceViewModel()
    {
        ImportCommand = new RelayCommand(ImportFromFolder);
        ExportCommand = new RelayCommand(ExportToFolder);
    }

    public string ImportSummary
    {
        get => _importSummary;
        set => SetProperty(ref _importSummary, value);
    }

    public string ExportSummary
    {
        get => _exportSummary;
        set => SetProperty(ref _exportSummary, value);
    }

    public string LastExportDirectory
    {
        get => _lastExportDirectory;
        set => SetProperty(ref _lastExportDirectory, value);
    }

    public string LastImportDirectory
    {
        get => _lastImportDirectory;
        set => SetProperty(ref _lastImportDirectory, value);
    }

    public ObservableCollection<string> ImportIssues { get; } = [];

    public RelayCommand ImportCommand { get; }
    public RelayCommand ExportCommand { get; }

    /// <summary>Raised when a CSV import produces a new <see cref="CodeplugImage"/>.</summary>
    public event Action<CodeplugImage>? CodeplugImported;

    /// <summary>The current codeplug to export. Set by the parent view model.</summary>
    public CodeplugImage? ActiveCodeplug { get; set; }

    public void ImportFromFolder()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Select CSV folder to import",
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var folder = dialog.FolderName;
        LastImportDirectory = folder;
        ImportIssues.Clear();

        try
        {
            var csvFileCount = Directory.GetFiles(folder, "*.csv").Length;
            if (csvFileCount == 0)
            {
                ImportSummary = $"No .csv files found in {folder}.";
                return;
            }

            var result = _csvService.ImportFromDirectory(folder);
            foreach (var issue in result.Issues)
            {
                ImportIssues.Add($"[{issue.Severity}] {issue.Message}");
            }

            var errorCount = result.Issues.Count(i => i.Severity == ValidationSeverity.Error);
            var warnCount = result.Issues.Count(i => i.Severity == ValidationSeverity.Warning);
            var image = result.Value!;

            ImportSummary = $"Imported {csvFileCount} CSV file(s): " +
                $"{image.Channels.Count} channels, {image.Contacts.Count} contacts, " +
                $"{image.Zones.Count} zones, {image.GroupLists.Count} group lists, " +
                $"{image.ScanLists.Count} scan lists, {image.RxGroups.Count} RX groups. " +
                $"Issues: {errorCount} error(s), {warnCount} warning(s).";

            CodeplugImported?.Invoke(image);
        }
        catch (Exception ex)
        {
            ImportSummary = $"CSV import failed: {ex.Message}";
        }
    }

    public void ExportToFolder()
    {
        if (ActiveCodeplug is null)
        {
            ExportSummary = "No active codeplug to export.";
            return;
        }

        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Select folder for CSV export",
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var folder = dialog.FolderName;
        LastExportDirectory = folder;

        try
        {
            _csvService.ExportToDirectory(ActiveCodeplug, folder);

            ExportSummary = $"Exported CSV files to {folder}.";
        }
        catch (Exception ex)
        {
            ExportSummary = $"CSV export failed: {ex.Message}";
        }
    }
}
