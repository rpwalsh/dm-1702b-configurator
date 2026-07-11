namespace Bao1702.Cli.Commands;

/// <summary>CLI command that computes a byte-level diff between two firmware image files.</summary>
internal sealed class DiffFirmwareCommand
{
    private readonly Infrastructure.CliRuntime _runtime;

    public DiffFirmwareCommand(Infrastructure.CliRuntime runtime)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
    }

    public string Execute(string leftPath, string rightPath)
    {
        if (string.IsNullOrWhiteSpace(leftPath))
        {
            throw new ArgumentException("Left firmware path is required.", nameof(leftPath));
        }

        if (string.IsNullOrWhiteSpace(rightPath))
        {
            throw new ArgumentException("Right firmware path is required.", nameof(rightPath));
        }

        return _runtime.DiffFirmware(leftPath, rightPath);
    }
}
