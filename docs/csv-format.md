# CSV format

## Supported CSV sets

- `channels.csv`
- `contacts.csv`
- `zones.csv`
- `rxgroups.csv`
- `scanlists.csv`
- `grouplists.csv`

## Channel columns

- Name
- ChannelType (`Analog` or `Digital`)
- RxFrequencyMHz
- TxFrequencyMHz
- Power
- Bandwidth
- AdmitCriteria
- ReceiveOnly (`True` or `False`)
- ZoneNames
- RxTone
- TxTone
- ColorCode
- TimeSlot
- ContactName
- RxGroupName

## Import rules

- Frequencies are decimal MHz and converted to Hz internally.
- `ReceiveOnly=True` sets the native transmit-inhibit property. A populated TX frequency does not override it.
- Tone values support blank, CTCSS (`67.0`), and DCS (`D023N`, `D023I`).
- Zone names are semicolon-delimited.
- Digital-only fields on analog rows are validation errors.
- Analog-only tone fields on digital rows are preserved only when blank.
- `ColorCode` must be in the range `0..15`.
- `TimeSlot` must be `1` or `2`.
- `contacts.csv` `CallId` values must be positive integers.
- `rxgroups.csv` stores `Name,ContactNames` with semicolon-delimited contact names.
- `scanlists.csv` stores `Name,ChannelNames` with semicolon-delimited channel names.
- `grouplists.csv` stores `Name,ContactNames` with semicolon-delimited contact names.
- Import errors report row and column context so that CSV cleanup is deterministic.

## Optional operator templates

Region-labeled examples are available under [`examples/`](../examples/README.md). Templates are manually selected and editable; the application never applies them during decoding or infers frequencies from their channel names.

Copy one selected template into a separate working directory as `channels.csv`, edit it for the intended use, and review validation results before applying it. Repeater and DMR rows labeled `EXAMPLE` demonstrate the schema only and are not asserted to be valid local configurations.

## Cross-reference rules

- Zone channel names must refer to known channels.
- RX group contact names must refer to known contacts.
- Digital channel `ContactName` and `RxGroupName` values must refer to imported contacts and RX groups.
- Scan list channel names must refer to known channels.
- Group list contact names must refer to known contacts.

## Current limitation

The CSV layer is modeled against the typed host-side codeplug domain model, not a final hardware-verified native DM-1702 binary layout. CSV workflows are therefore useful immediately for clean CPS replacement work even while binary field mapping continues.
