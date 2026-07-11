# Architecture

## Solution Overview

Bao1702 Desktop is a .NET 10 WPF application organized as a layered solution with strict dependency direction: upper layers depend on lower layers, never the reverse.

```
┌─────────────────────────────────────────┐
│            Bao1702.Desktop              │  WPF UI, ViewModels, Services
│            Bao1702.Cli                  │  Command-line tool
├─────────────────────────────────────────┤
│       Bao1702.ReverseEngineering        │  Native image serializers & decoders
├─────────────────────────────────────────┤
│           Bao1702.Protocol              │  Session management, safety, packets
├──────────────────┬──────────────────────┤
│  Bao1702.Codeplug│   Bao1702.Firmware   │  Data model & CSV  │  Firmware analysis
├──────────────────┴──────────────────────┤
│           Bao1702.Transport             │  USB, serial, mock transports
└─────────────────────────────────────────┘
```

## Project Roles

### Bao1702.Transport

The lowest layer — provides transport abstractions for communicating with the radio hardware.

- **Abstractions** — `ITransportFactory`, `ITransportConnection`, `IRadioTransport`, `TransportEndpoint`, timeout and retry policies.
- **Framing** — `TransportFrameCodec` encodes/decodes the DM-1702 wire frame format (header + length + payload + checksum). `Checksums` provides Sum8 and CRC-16/CCITT.
- **USB Printer** — `UsbPrinterTransportFactory` discovers DM-1702 radios via WMI and Win32 `SetupDi` APIs (VID 0483 / PID 5780). `UsbPrinterTransportConnection` uses overlapped file I/O for framed exchanges.
- **Serial** — `SerialTransportFactory` enumerates COM ports.
- **Mock** — `MockTransportFactory` and `MockRadioTransport` for deterministic testing.
- **Diagnostics** — `HexDump`, `TransportTraceCollector`, and trace event infrastructure.

### Bao1702.Codeplug

The codeplug data model and I/O layer — no protocol or transport dependency.

- **Model** — Immutable record types: `CodeplugImage`, `Channel`, `Zone`, `ScanList`, `Contact`, `RxGroup`, `GroupList`, `GeneralSettings`, `DisplaySettings`, `PowerSettings`, `ToneValue`, etc.
- **Binary** — `BinaryCodeplugSerializer` for round-trip serialization of the model.
- **CSV** — Per-entity CSV importers and exporters (`ChannelCsvImporter`, `ContactCsvExporter`, etc.) with schema validation.
- **Validation** — `CodeplugWriteValidator` checks image size, duplicate names, broken references, and structural integrity.
- **Constants** — `Dm1702NativeImageAssumptions` defines binary layout constants (offsets, strides, record counts).

### Bao1702.Firmware

Firmware image analysis. This project references `Bao1702.Protocol` for shared radio-identity compatibility evaluation, but performs no transport I/O.

- `FirmwareImageParser` — parses raw firmware bytes into header, segments, and metadata.
- `FirmwareChecksumService` — SHA-256, SUM16, XOR8, and header-declared checksum validation.
- `FirmwareStringExtractor` — ASCII and UTF-16LE string extraction.
- `FirmwareMapDumper` — entropy-annotated memory map generation.
- `FirmwareCompatibilityValidator` — validates firmware images against radio identity.

### Bao1702.Protocol

Radio communication protocol and safety infrastructure.

- **Sessions** — `StockCpsSession` manages the full read/write lifecycle using the stock CPS protocol (PSEARCH → PASSSTA → SYSINFO → G/V queries → R/W commands).
- **Packets** — `Bao1702Packet`, `Bao1702CommandCatalog`, and command ID constants.
- **Discovery** — `RadioInfoProbe` identifies connected radios and builds `RadioIdentity`.
- **Safety** — `SafetyPolicyEngine` enforces backup-before-write, blocks writes to unknown radios, and validates write intent. `BackupLedger` tracks codeplug and firmware backups with SHA-256 manifests.
- **Compatibility** — `CompatibilityMatrix` maps radio families to read/write capability levels.
- **Mock** — `MockRadioDevice` provides a fully scripted test double.

### Bao1702.ReverseEngineering

Native binary image serialization — the bridge between the abstract codeplug model and the DM-1702's on-radio format.

- **Image Builder** - `Dm1702NativeImageBuilder.Build()` produces a deterministic 245,760-byte image from the typed `CodeplugImage` model. Layout assumptions are independently verified through controlled configuration differences and black-box round-trip testing.
- **Section Serializers** — one per section: `Dm1702NativeChannelRecordSerializer`, `Dm1702NativeContactSerializer`, `Dm1702NativeZoneSerializer`, `Dm1702NativeScanListSerializer`, `Dm1702NativeRxGroupSerializer`, `Dm1702NativeGpsSerializer`, `Dm1702NativeConfigSerializer`, `Dm1702NativeSystemSectionSerializer`, `Dm1702NativeSectorSerializer`.
- **Image Reader** — `Dm1702NativeImageReader` decodes a raw image back into a `CodeplugImage`.
- **Patcher** — `NativeDataPatcher` applies in-place edits to individual channel records.
- **Analysis Tools** — capture parsers, protocol analyzers, hex diff, and heuristic decoders used during reverse engineering.

### Bao1702.Desktop

WPF desktop CPS application.

- **MainWindow** — tabbed interface with codeplug editor, firmware analysis, advanced tools, and settings. Primary toolbar: Open / Save (file I/O) and Read / Write (radio I/O). Radio connections are established automatically when Read or Write is invoked.
- **CodeplugEditorViewModel** — manages all six entity collections with add/delete/edit support, plus parameter settings (squelch, VOX, backlight, power, battery saver, CTCSS tail revert, keypad lock, DTMF codes, startup text) and 7 programmable key assignments (short/long press). Parameter behavior is independently verified through controlled configuration differences and black-box testing on user-owned hardware.
- **DeviceSessionService** — orchestrates radio connections, reads, writes, and backup creation.
- **Services** — `DesignTimeWorkspace` provides design-time sample data for the XAML designer.

### Bao1702.Cli

Command-line interface for automation.

- **CliRuntime** — core orchestrator for all CLI operations.
- **Commands** — `RestoreCodeplugCommand`, `VerifyImageCommand`, `DiffFirmwareCommand`, `UnsafeForceWriteCommand`.
- **CliSessionFactory** — transport and session creation for CLI context.

### Bao1702.Tests

Deterministic tests organized by subsystem:

- Protocol round-trips and session replay
- Binary serializer fidelity (clean build and read-back)
- CSV import/export round-trips
- Safety policy enforcement
- Transport framing and checksum
- Firmware analysis
- Native image section serializers verified through controlled configuration differences and black-box testing on user-owned hardware

## Design Decisions

- **Immutable records** — all model types are `sealed record` for value equality and thread safety.
- **No ORM or database** — the codeplug is a single binary image; all persistence is file-based.
- **Safety by default** — writes are blocked unless preconditions (backup, known radio) are met.
- **Clean-write architecture** - generation is deterministic and auditable. Reserved and unverified regions remain explicitly documented and writes fail closed when required assumptions are not satisfied.
- **WPF over WinUI** — chosen for mature tooling, fast iteration, and dense data-binding UI.
- **Auto-connect** — radio connections are established on demand when the user clicks Read or Write, rather than requiring a separate Connect step. Transport discovery scans USB printer-class interfaces (VID `0483` / PID `5780`) automatically.
