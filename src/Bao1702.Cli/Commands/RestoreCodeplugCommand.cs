namespace Bao1702.Cli.Commands;

/// <summary>CLI command that restores a codeplug image from a backup file to the connected radio.</summary>
internal sealed class RestoreCodeplugCommand
{
    internal const string RequiredAcknowledgement = "I_CONFIRM_THIS_TARGET_AND_BACKUP";

    private readonly Func<string, Task<string>> _restoreCodeplug;

    internal RestoreCodeplugCommand(Infrastructure.CliRuntime runtime)
        : this((runtime ?? throw new ArgumentNullException(nameof(runtime))).RestoreCodeplugAsync)
    {
    }

    internal RestoreCodeplugCommand(Func<string, Task<string>> restoreCodeplug)
    {
        _restoreCodeplug = restoreCodeplug ?? throw new ArgumentNullException(nameof(restoreCodeplug));
    }

    internal Task<string> ExecuteAsync(string imagePath, string? acknowledgement)
    {
        if (string.IsNullOrWhiteSpace(imagePath))
        {
            throw new ArgumentException("Input image path is required.", nameof(imagePath));
        }

        if (!string.Equals(acknowledgement, RequiredAcknowledgement, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Hardware write blocked. Re-run with --ack {RequiredAcknowledgement} after reviewing preflight.");
        }

        return _restoreCodeplug(imagePath);
    }
}
