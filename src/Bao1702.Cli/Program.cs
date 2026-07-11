using Bao1702.Cli.Infrastructure;
using Bao1702.Cli.Commands;

namespace Bao1702.Cli;

/// <summary>CLI entry point — dispatches commands by verb (read, write, verify, diff, restore, etc.).</summary>
internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var runtime = new CliRuntime();

        if (args.Length == 0 || args[0] is "help" or "--help" or "-h")
        {
            PrintHelp();
            return 0;
        }

        if (args[0] is "diag")
        {
            Console.WriteLine(Bao1702.Transport.UsbPrinter.DiagnosticProbe.RunDiagnostic());
            return 0;
        }

        try
        {
            switch (string.Join(' ', args.Take(2)).Trim())
            {
                case "devices list":
                    foreach (var device in await runtime.ListDevicesAsync().ConfigureAwait(false))
                    {
                        Console.WriteLine(device);
                    }

                    return 0;

                case "radio info":
                    if (HasFlag(args, "--stock"))
                    {
                        Console.WriteLine(await runtime.ReadStockRadioInfoAsync(GetOptionalOption(args, "--endpoint")).ConfigureAwait(false));
                    }
                    else
                    {
                        Console.WriteLine(await runtime.ReadRadioInfoAsync(GetOptionalOption(args, "--endpoint"), HasFlag(args, "--trace")).ConfigureAwait(false));
                    }
                    return 0;

                case "backup codeplug":
                    await runtime.BackupCodeplugAsync(GetRequiredOption(args, "--output")).ConfigureAwait(false);
                    Console.WriteLine("Codeplug backup complete.");
                    return 0;

                case "read codeplug":
                    Console.WriteLine(await new ReadCodeplugCommand(runtime).ExecuteAsync(
                        GetRequiredOption(args, "--output"),
                        GetOptionalOption(args, "--endpoint"),
                        HasFlag(args, "--trace")).ConfigureAwait(false));
                    return 0;

                case "backup firmware":
                    await runtime.BackupFirmwareAsync(GetRequiredOption(args, "--output")).ConfigureAwait(false);
                    Console.WriteLine("Firmware backup complete.");
                    return 0;

                case "restore codeplug":
                    Console.WriteLine("Managed codeplug restore is safety-gated. A prior backup must exist for the exact target before any write proceeds.");
                    var inputPath = GetRequiredOption(args, "--input");
                    var acknowledgement = GetOptionalOption(args, "--ack");
                    if (HasFlag(args, "--preflight") || !string.Equals(
                            acknowledgement,
                            RestoreCodeplugCommand.RequiredAcknowledgement,
                            StringComparison.Ordinal))
                    {
                        var preflight = await runtime.GetWriteCodeplugPreflightAsync(inputPath).ConfigureAwait(false);
                        Console.WriteLine(Bao1702.Protocol.Safety.WritePreflightFormatter.FormatText(preflight));
                        if (!HasFlag(args, "--preflight"))
                        {
                            Console.Error.WriteLine(
                                $"Hardware write blocked. Re-run with --ack {RestoreCodeplugCommand.RequiredAcknowledgement} after reviewing preflight.");
                            return 2;
                        }
                    }
                    else
                    {
                        Console.WriteLine(await new RestoreCodeplugCommand(runtime).ExecuteAsync(inputPath, acknowledgement).ConfigureAwait(false));
                    }
                    return 0;

                case "export csv":
                    Console.WriteLine(runtime.ExportCsv(GetRequiredOption(args, "--image"), GetRequiredOption(args, "--outdir")));
                    return 0;

                case "import csv":
                    Console.WriteLine(runtime.ImportCsv(GetRequiredOption(args, "--indir"), GetRequiredOption(args, "--output")));
                    return 0;

                case "codeplug build-native":
                    Console.WriteLine(runtime.ImportCsvToNativeImageFromScratch(GetRequiredOption(args, "--indir"), GetRequiredOption(args, "--output")));
                    return 0;

                case "verify image":
                    Console.WriteLine(new VerifyImageCommand(runtime).Execute(GetRequiredOption(args, "--image")));
                    return 0;

                case "diff firmware":
                    Console.WriteLine(new DiffFirmwareCommand(runtime).Execute(GetRequiredOption(args, "--left"), GetRequiredOption(args, "--right")));
                    return 0;

                case "trace session":
                    Console.WriteLine(await runtime.TraceSessionAsync(GetOptionalOption(args, "--endpoint")).ConfigureAwait(false));
                    return 0;

                case "capture analyze":
                    Console.WriteLine(runtime.AnalyzeCapture(GetRequiredOption(args, "--path")));
                    return 0;

                case "capture normalize":
                    Console.WriteLine(runtime.NormalizeCapture(GetRequiredOption(args, "--path"), GetOptionalOption(args, "--output")));
                    return 0;

                case "capture export-radio-json":
                    Console.WriteLine(runtime.ExportRadioDataJson(GetRequiredOption(args, "--path"), GetRequiredOption(args, "--output")));
                    return 0;

                case "capture export-radio-bin":
                    Console.WriteLine(runtime.ExportRadioBinary(GetRequiredOption(args, "--path"), GetRequiredOption(args, "--output")));
                    return 0;

                case "codeplug export-saved-json":
                    Console.WriteLine(runtime.ExportSavedCodeplugJson(GetRequiredOption(args, "--path"), GetRequiredOption(args, "--output"), GetOptionalOption(args, "--baseline")));
                    return 0;

                case "codeplug export-native-csv":
                    Console.WriteLine(runtime.ExportNativeCsvFromSavedCodeplug(GetRequiredOption(args, "--path"), GetRequiredOption(args, "--outdir"), GetOptionalOption(args, "--baseline")));
                    return 0;

                case "codeplug write-native-data":
                    Console.WriteLine(runtime.WriteRecoveredNativeDataFile(GetRequiredOption(args, "--path"), GetRequiredOption(args, "--output"), GetOptionalOption(args, "--baseline")));
                    return 0;

                case "codeplug import-native-csv":
                    Console.WriteLine(runtime.ImportCsvToNativeDataFile(GetRequiredOption(args, "--indir"), GetOptionalOption(args, "--base-data"), GetRequiredOption(args, "--output"), GetOptionalOption(args, "--baseline")));
                    return 0;

                case "capture analyze-write":
                    Console.WriteLine(runtime.AnalyzeWriteSession(GetRequiredOption(args, "--path")));
                    return 0;

                case "capture map-write":
                    Console.WriteLine(runtime.AnalyzeWriteSessionAgainstCodeplug(GetRequiredOption(args, "--path"), GetRequiredOption(args, "--codeplug")));
                    return 0;

                case "probe usbprint":
                    Console.WriteLine(runtime.ProbeUsbPrinterTransport());
                    return 0;

                case "open usbprint":
                    Console.WriteLine(await runtime.OpenUsbPrinterTransportAsync().ConfigureAwait(false));
                    return 0;

                case "unsafe write-preflight":
                    Console.WriteLine(await new UnsafeForceWriteCommand(runtime).ExecuteAsync(args).ConfigureAwait(false));
                    return 0;

                default:
                    Console.Error.WriteLine("Unknown command.");
                    PrintHelp();
                    return 1;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static string GetRequiredOption(IReadOnlyList<string> args, string optionName)
    {
        var index = args.IndexOf(optionName);
        if (index < 0 || index + 1 >= args.Count)
        {
            throw new ArgumentException($"Missing required option '{optionName}'.");
        }

        return args[index + 1];
    }

    private static string? GetOptionalOption(IReadOnlyList<string> args, string optionName)
    {
        var index = args.IndexOf(optionName);
        return index < 0 || index + 1 >= args.Count
            ? null
            : args[index + 1];
    }

    private static bool HasFlag(IReadOnlyList<string> args, string flagName)
        => args.IndexOf(flagName) >= 0;

    private static void PrintHelp()
    {
        Console.WriteLine("bao1702 - Baofeng DM-1702 codeplug & radio toolkit");
        Console.WriteLine();

        WriteSection("Device & Radio",
            ("devices list",                                       "List connected radios"),
            ("radio info [--endpoint <id>] [--trace]",             "Query radio identity"),
            ("radio info --stock [--endpoint <id>]",               "Query via stock CPS protocol"),
            ("probe usbprint",                                     "Detect USB printer transport"),
            ("open usbprint",                                      "Open USB printer transport"));

        WriteSection("Backup & Restore",
            ("backup codeplug --output <file>",                    "Back up codeplug to file"),
            ("backup firmware --output <file>",                    "Back up firmware to file"),
            ("read codeplug --output <file> [--endpoint <id>] [--trace]", "Read codeplug from radio"),
            ("restore codeplug --input <file> --preflight",       "Run restore preflight without writing"),
            ($"restore codeplug --input <file> --ack {RestoreCodeplugCommand.RequiredAcknowledgement}", "Restore codeplug to radio"));

        WriteSection("Codeplug Generation",
            ("codeplug build-native --indir <dir> --output <file>", "Build native .data from CSV"));

        WriteSection("Import / Export",
            ("export csv --image <file> --outdir <dir>",           "Export .data to CSV files"),
            ("import csv --indir <dir> --output <file>",           "Import CSV files to codeplug"),
            ("codeplug export-saved-json --path <file> --output <file> [--baseline <file>]",  "Export saved codeplug as JSON"),
            ("codeplug export-native-csv --path <file> --outdir <dir> [--baseline <file>]",   "Export native .data to CSV"),
            ("codeplug write-native-data --path <file> --output <file> [--baseline <file>]",  "Write recovered native .data"),
            ("codeplug import-native-csv --indir <dir> --output <file> [--base-data <file>] [--baseline <file>]", "Import CSV to native .data"));

        WriteSection("Analysis & Diagnostics",
            ("verify image --image <file>",                        "Validate a .data image"),
            ("diff firmware --left <file> --right <file>",         "Diff two firmware images"),
            ("trace session [--endpoint <id>]",                    "Trace a live CPS session"),
            ("cps inspect --path <dir>",                           "Inspect CPS installation"),
            ("cps analyze --path <dir>",                           "Analyze CPS folder"));

        WriteSection("Capture Processing",
            ("capture analyze --path <file>",                      "Analyze USB capture"),
            ("capture normalize --path <file> [--output <file>]",  "Normalize capture format"),
            ("capture export-radio-json --path <file> --output <file>", "Export capture as JSON"),
            ("capture export-radio-bin --path <file> --output <file>",  "Export capture as binary"),
            ("capture analyze-write --path <file>",                "Analyze write session"),
            ("capture map-write --path <file> --codeplug <file>",  "Map write blocks to codeplug"));

        WriteSection("⚠ Dangerous",
            ("unsafe write-preflight --image <file> --dry-run --ack I_ACCEPT_THE_RISK_OF_BRICKING_THE_RADIO", "Validate an image without writing to hardware"));
    }

    private static void WriteSection(string title, params (string Syntax, string Description)[] commands)
    {
        Console.WriteLine($"  {title}:");
        foreach (var (syntax, description) in commands)
        {
            Console.WriteLine($"    bao1702 {syntax}");
            Console.WriteLine($"      {description}");
        }
        Console.WriteLine();
    }

    private static int IndexOf(this IReadOnlyList<string> args, string optionName)
    {
        for (var index = 0; index < args.Count; index++)
        {
            if (string.Equals(args[index], optionName, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return -1;
    }
}
