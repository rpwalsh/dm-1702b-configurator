# User Guide

## Desktop CPS

### Connecting to the Radio

The radio connection is established automatically when you click **Read from Radio** or **Write to Radio**. There is no separate Connect step.

1. Connect the DM-1702 / DM-1702B to a USB port using the programming cable.
2. Launch the application (`Bao1702.Desktop`).
3. Click **Read from Radio** or **Write to Radio**. The application scans USB printer-class interfaces (VID `0483` / PID `5780`) to locate the radio automatically.
4. On successful connection, the radio's model name, firmware version, and DMR ID are displayed in the status bar.

> USB discovery uses WMI and Win32 `SetupDi` APIs. Device identity must still pass the compatibility policy before writing.

### Reading a Codeplug

1. Click **Read from Radio**. The radio is auto-detected and the 245,760-byte native image is downloaded block-by-block with a progress bar.
2. The codeplug is decoded and all entity tabs are populated: Channels, Zones, Scan Lists, Contacts, RX Groups, and Group Lists.
3. Parameter settings (squelch, VOX, backlight, power, DTMF, keys, startup text) are populated in the Settings tab.
4. A pre-read backup is automatically created in the backup catalog.

### Editing Entities

Each entity tab provides a data grid with inline editing and a detail panel for the selected item.

#### Channels
- Fields: Name, RX Frequency, TX Frequency, Mode (Analog/Digital), Power, Bandwidth, CTCSS/DCS Tones, Color Code, Time Slot, Contact, RX Group, Scan List, GPS System.
- Use **Add Channel** / **Delete Channel** on the toolbar.
- Select a row and edit fields in the detail panel on the right.

#### Zones
- Each zone contains an ordered list of channel references.
- Use **Add Zone** / **Delete Zone** to manage zones.

#### Scan Lists
- Each scan list references one or more channels by index.
- Priority channels are configurable.

#### Contacts
- Fields: Name, Call ID, Call Type (Private/Group/All Call).
- Contacts are referenced by channel digital settings and RX groups.

#### RX Groups
- Each RX group contains a list of contact references for DMR receive filtering.

#### Group Lists
- Each group list contains a set of contact references.

### Writing a Codeplug

1. After editing, click **Write to Radio**. The radio is auto-detected if not already connected.
2. The application validates the codeplug — any errors are displayed in the validation panel.
3. If no backup exists for this radio, the write is blocked. Use **Read from Radio** first to create a baseline backup.
4. On confirmation, `Dm1702NativeImageBuilder.Build()` generates the entire 245,760-byte native image from the codeplug model (the model is the sole source of truth — no preserved-image overlay). The image is written block-by-block.

> Protocol behavior is independently implemented from black-box observations on user-owned hardware. Compatibility remains experimental.

### Saving and Opening Files

**Open** and **Save** are the primary toolbar actions for offline codeplug editing:

- **Open** (📂) loads a codeplug from a `.codeplug` file (native binary format) or a raw `.data` / `.bin` image.
- **Save** (💾) writes the current codeplug to a `.codeplug` file.
- **Export CSV** creates one CSV file per entity type in a chosen directory.
- **Import CSV** loads CSV files from a directory, validates references, and replaces the current codeplug.

The typical workflow is: **Read from Radio** → edit → **Save** to file → iterate offline → **Open** → **Write to Radio**.

### Backup and Restore

Backups are created automatically — there is no standalone Backup button:

- A **codeplug backup** is created automatically each time you read from the radio or before a write operation.
- **Firmware backups** are created via the Firmware Analysis tab.
- Each backup includes a SHA-256 manifest for integrity verification.
- The backup catalog is used by the safety policy engine to enforce backup-before-write.

### Firmware Analysis

The **Firmware Analysis** tab provides:

- Firmware image read from the connected radio.
- SHA-256, SUM16, and XOR8 checksums.
- Header-declared checksum validation.
- ASCII and UTF-16LE string extraction.
- Per-region entropy analysis.
- Firmware compatibility validation against the connected radio's identity.

### Settings

The **Settings** tab exposes radio parameters independently verified through controlled configuration differences and black-box testing on user-owned hardware:

- **General** — radio name, DMR ID, language, TX timeout, TX preamble duration, mic gain.
- **Display** — backlight duration (5s/10s/15s/20s/25s/Always), show channel number, show clock.
- **Power** — default power level (Low/Medium/High), battery saver.
- **Audio** — squelch level (analog + digital), VOX enable/level, CTCSS/DCS tail revert.
- **Security** — keypad lock.
- **DTMF** — PTT ID, kill code, revive code.
- **Startup** — intro line 1, intro line 2.
- **Key assignments** — 7 programmable keys (Side Key 1, Side Key 2, Top Key Short, Top Key Long, P1, P2, P3, P4) with short/long press function codes.

> Parameter behavior is covered by deterministic serializer and round-trip tests.

### About

**Help → About** displays the version number, copyright, and build information.

---

## CLI

The CLI provides headless operations for scripting and automation.

### Commands

| Command | Description |
|---------|-------------|
| `read codeplug --output <file> [--endpoint <id>] [--trace]` | Read codeplug from radio and save to file |
| `restore codeplug --input <file> --preflight` | Run restore preflight without writing |
| `restore codeplug --input <file> --ack I_CONFIRM_THIS_TARGET_AND_BACKUP` | Restore after explicit acknowledgement |
| `verify image --image <file>` | Validate a codeplug image file |
| `diff firmware --left <a> --right <b>` | Byte-level diff of two firmware images |
| `unsafe write-preflight --image <file> --dry-run --ack I_ACCEPT_THE_RISK_OF_BRICKING_THE_RADIO` | Validate an image and acknowledgement without writing to hardware |

### Examples

```bash
# Read codeplug to file
bao1702 read codeplug --output my_radio.bin

# Validate before writing
bao1702 verify image --image my_radio.bin

# Review preflight, then explicitly acknowledge the target and backup
bao1702 restore codeplug --input my_radio.bin --preflight
bao1702 restore codeplug --input my_radio.bin --ack I_CONFIRM_THIS_TARGET_AND_BACKUP

# Compare two firmware dumps
bao1702 diff firmware --left fw_old.bin --right fw_new.bin
```
