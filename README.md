# Bao1702

Bao1702 is an independently written Windows configuration and diagnostics toolkit for selected DM-1702-series radios. It includes a WPF desktop application, command-line tooling, binary codeplug modeling, transport abstractions, validation, backup and restore workflows, CSV interchange, and automated round-trip tests.

> **Independent-project disclaimer:** Bao1702 is independent, unofficial third-party software intended for interoperability with selected DM-1702-series radios. It is not affiliated with, sponsored by, endorsed by, or supported by Baofeng, Pofung, or their manufacturers, distributors, or service organizations. Product and model names are used only to identify compatible hardware and remain the property of their respective owners.

The implementation is independently written. Manufacturer software, firmware, executable code, icons, proprietary resources, and decompiled source are not distributed with this project.

## Supported hardware

The current compatibility target is selected Baofeng DM-1702 and BF-1702B radios. Device support is deliberately conservative: an identified device must match the compatibility policy before writes are enabled. Unknown devices are read-only. Hardware behavior can vary by model revision and firmware, so manual validation on user-owned hardware remains necessary.

## Engineering highlights

- Strongly typed binary-format models and deterministic serialization
- Defensive bounds, image-size, field-range, and compatibility validation
- Mockable USB/serial transport and protocol-session abstractions
- Backup-before-write, preflight reporting, explicit confirmation, and restore workflows
- CSV import/export with validation and spreadsheet-formula neutralization
- Dry-run image generation, self-parse checks, and round-trip tests
- Checksum, diagnostics, timeout, cancellation, and malformed-input handling

## Safety model

Writes are treated as destructive operations. Bao1702 validates the model before transport use, requires an exact-size generated image, parses the generated image again, requires a target-specific backup, blocks unknown targets, and asks for explicit confirmation before the first write. A force path must never bypass memory-safety, image-size, or target-identification checks. No operation writes merely because the application starts or a file is opened.

See [Safety model](docs/safety-model.md) and [Recovery playbook](docs/recovery-playbook.md).

## Architecture

- `Bao1702.Codeplug` — domain model, binary serialization, validation, CSV
- `Bao1702.Transport` — serial/USB abstractions, framing, diagnostics, mocks
- `Bao1702.Protocol` — device discovery, sessions, safety policy, backups
- `Bao1702.Desktop` — WPF operator workflow
- `Bao1702.Cli` — automation and diagnostics
- `Bao1702.Firmware` — local-file compatibility inspection; no firmware is bundled
- `Bao1702.ReverseEngineering` — generic and independently authored interoperability utilities; no decompiler output is bundled
- `Bao1702.Tests` — unit and integration tests using generated data and mocks

The observed binary layout is documented as an interoperability model, with uncertainty retained for unverified fields. See [Interoperability methodology](docs/INTEROPERABILITY.md).

## Build instructions

Requirements: Windows 10 or later and the .NET 10 SDK with the Windows Desktop workload.

```powershell
dotnet restore Bao1702Suite.slnx
dotnet build Bao1702Suite.slnx --configuration Release --no-restore
```

## Test instructions

```powershell
dotnet test Bao1702Suite.slnx --configuration Release --no-build
```

Hardware tests are not run automatically and must use user-owned equipment with a recovery plan.

## CLI examples

```powershell
dotnet run --project src/Bao1702.Cli -- help
dotnet run --project src/Bao1702.Cli -- verify image --image synthetic.data
dotnet run --project src/Bao1702.Cli -- import csv --indir samples/csv-imports --output synthetic.data
dotnet run --project src/Bao1702.Cli -- read codeplug --output private-backup.data
dotnet run --project src/Bao1702.Cli -- restore codeplug --input private-backup.data --preflight
```

Keep real backups outside source control. Review `help` for the exact commands supported by the current build.

## Optional operator templates

The [`examples/`](examples/README.md) directory contains manually selected, region-labeled CSV starting points, including US 2 m and 70 cm simplex, repeater and DMR schema examples, and a receive-only weather template. They are editable inputs, never decoding rules or automatically applied channel plans.

Repeater and DMR entries labeled `EXAMPLE` are illustrative and must be replaced with independently obtained local information. Structural validation and warnings assist the operator but do not determine privileges, authorization, or regulatory compliance.

## Synthetic fixture statement

All committed examples and test fixtures use generic labels and public or illustrative frequencies. They are intended for testing or as operator-selected starting points, not as local operational channel plans or authorization to transmit. The project does not publish user codeplugs, real contacts, captures, device serial numbers, or private location data.

## Known limitations

- Compatibility is limited to specifically recognized device identities; model variants are not assumed equivalent.
- Some reserved regions and field meanings remain unverified. Uncertainty is preserved and unsafe writes are blocked.
- Passing mock and round-trip tests does not prove behavior on every hardware/firmware revision.
- Firmware tools inspect user-supplied local files only; they do not unlock, modify, or redistribute firmware.
- The project has not received a formal security audit or manufacturer certification.

## Radio-operation notice

Users are responsible for complying with all applicable licensing, frequency-allocation, equipment, power, emission, identification, and operating requirements. Compatibility with a radio does not authorize transmission on any frequency or radio service. This software cannot determine every jurisdictional or service-specific requirement.

Do not program public-safety, aviation, marine, government, military, commercial, business, or emergency-service systems for unauthorized transmission. Receive capability does not imply transmit authority. Bao1702 does not convert uncertified or noncompliant hardware into compliant hardware, and acceptance of a frequency is not a determination that its use is lawful.

## Third-party notices

See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md). Manufacturer resources are not dependencies and are not included.

## Security reporting

Do not attach real codeplugs, captures, credentials, device identifiers, or location data to public issues. Report security concerns privately to the repository owner through GitHub's private vulnerability-reporting feature when available.

## Current maturity

Bao1702 is an experimental interoperability project. Its architecture and automated tests demonstrate defensive engineering, but manual device validation and recovery readiness are still required before hardware writes. It is not an official or universally compatible programming application.

## License and permitted access

Copyright (c) 2026 Sarah Walsh. All rights reserved. This proprietary codebase is published solely for public viewing and professional reference; it is not open source and no permission is granted to use, copy, modify, distribute, sell, deploy, or create derivative works. See [LICENSE](LICENSE). Third-party components retain their own licenses.
