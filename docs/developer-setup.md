# Developer setup

## Requirements

- Visual Studio 2026 Insiders
- .NET 10 SDK
- Windows Desktop workload for WPF
- Optional: protocol logging and user-owned hardware diagnostics tools

## Build

- `dotnet restore Bao1702Suite.slnx`
- `dotnet build Bao1702Suite.slnx`
- `dotnet test Bao1702Suite.slnx`

## Recommended workflow

1. Work on offline parsers and tests first.
2. Validate protocol changes with captures before touching hardware.
3. Keep assumptions isolated and documented.
4. Preserve every raw artifact under `captures/`, `firmware/`, and `codeplugs/`.

## Practical desktop workflow

1. Launch `Bao1702.Desktop` and click **Read from Radio** (auto-connects via USB printer-class, VID `0483` / PID `5780`).
2. Edit entities, parameters, and key assignments in the tabbed editor.
3. Click **Save** to persist the codeplug to a `.codeplug` file for offline iteration.
4. Click **Open** to reload a saved codeplug.
5. Click **Write to Radio** when ready to program the radio (safety policy enforces backup-before-write).
6. Use **Export CSV** / **Import CSV** for bulk channel management.

## Practical CLI workflow

- `bao1702 devices list`
- `bao1702 radio info`
- `bao1702 backup codeplug --output <file>`
- `bao1702 restore codeplug --input <file> --preflight`
- `bao1702 restore codeplug --input <file> --ack I_CONFIRM_THIS_TARGET_AND_BACKUP`
- `bao1702 backup firmware --output <file>`
- `bao1702 verify image --image <file>`
- `bao1702 diff firmware --left <file> --right <file>`
