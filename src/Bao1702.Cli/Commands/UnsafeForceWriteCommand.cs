using Bao1702.Codeplug.Validation;
using Bao1702.Protocol;
using Bao1702.Protocol.Model;
using Bao1702.Protocol.Safety;

namespace Bao1702.Cli.Commands;

/// <summary>
/// CLI command that writes a codeplug image to the radio, bypassing safety checks.
/// Requires an explicit risk-acknowledgement string.
/// </summary>
internal sealed class UnsafeForceWriteCommand
{
    private const string RequiredAcknowledgement = "I_ACCEPT_THE_RISK_OF_BRICKING_THE_RADIO";

    private readonly Infrastructure.CliRuntime _runtime;

    public UnsafeForceWriteCommand(Infrastructure.CliRuntime runtime)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
    }

    public async Task<string> ExecuteAsync(IReadOnlyList<string> args)
    {
        var imagePath = GetRequiredOption(args, "--image");
        var dryRun = HasOption(args, "--dry-run");
        var acknowledgement = GetOptionalOption(args, "--ack");

        if (!dryRun)
        {
            throw new InvalidOperationException("This command is preflight-only. Use --dry-run; live writes are not supported.");
        }

        if (!string.Equals(acknowledgement, RequiredAcknowledgement, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Write preflight requires --ack {RequiredAcknowledgement}");
        }

        var preflight = await _runtime.GetWriteCodeplugPreflightAsync(imagePath, forceUnsafe: true).ConfigureAwait(false);

        return string.Join(Environment.NewLine,
            "WRITE PREFLIGHT - NO HARDWARE WRITE",
            WritePreflightFormatter.FormatText(preflight),
            "No write was attempted.");
    }

    private static bool HasOption(IReadOnlyList<string> args, string optionName)
        => args.Any(argument => string.Equals(argument, optionName, StringComparison.OrdinalIgnoreCase));

    private static string GetRequiredOption(IReadOnlyList<string> args, string optionName)
    {
        var value = GetOptionalOption(args, optionName);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"Missing required option '{optionName}'.");
        }

        return value;
    }

    private static string? GetOptionalOption(IReadOnlyList<string> args, string optionName)
    {
        for (var index = 0; index < args.Count - 1; index++)
        {
            if (string.Equals(args[index], optionName, StringComparison.OrdinalIgnoreCase))
            {
                return args[index + 1];
            }
        }

        return null;
    }
}
