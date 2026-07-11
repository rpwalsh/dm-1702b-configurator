namespace Bao1702.Cli.Commands;

/// <summary>CLI command that validates a codeplug image file against structural and semantic rules.</summary>
internal sealed class VerifyImageCommand
{
    private readonly Infrastructure.CliRuntime _runtime;

    public VerifyImageCommand(Infrastructure.CliRuntime runtime)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
    }

    public string Execute(string imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath))
        {
            throw new ArgumentException("Image path is required.", nameof(imagePath));
        }

        return _runtime.VerifyImage(imagePath);
    }
}
