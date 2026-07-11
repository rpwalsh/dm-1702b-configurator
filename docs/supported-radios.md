# Supported radios

## Focus target

- Baofeng 1702B (orange keypad, DMR) - primary optimization target

## Planned family support

- DM-1702 family variants sharing protocol or codeplug lineage

## Support levels

- **Research** - offline parsing only
- **Read-only** - connect, identify, and backup paths
- **Managed write** - codeplug writes with safety gates
- **Experimental firmware** - guarded research-only mode

## Current level

- 1702B: **Experimental managed write** - guarded by target identification, exact-size validation, and an identity-bound backup. Manual validation on each hardware revision remains required.
- DM-1702: **Managed write** — same protocol and binary format as 1702B. Same capture evidence applies.
- DM-1702 peer variants: readable/research-only unless explicitly promoted in the compatibility matrix

## Explicitly blocked today

- Managed firmware flashing on all variants
- Casual writes to unknown or partially identified targets
- Variant-agnostic force writes
