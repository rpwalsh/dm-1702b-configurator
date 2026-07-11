# Recovery playbook

## Purpose

Document anti-brick and recovery procedures before any destructive workflow is enabled.

## Immediate rules

- Always capture original codeplug and firmware before experiments.
- Keep hardware-specific backups labeled with serial, model, and date.
- Record firmware/version strings and capture traces for every session.

## Planned recovery content

- Known bootloader entry patterns
- CPS-based rollback paths
- Cable and driver validation checklist
- Symptoms vs likely failure stage
- Safe escalation path for partial brick states

## Immediate operator checklist

1. Stop sending write traffic immediately after unexpected behavior.
2. Preserve the full operation log and any trace session output.
3. Save the exact codeplug and firmware backup files used before the experiment.
4. Record model, serial, firmware, bootloader, cable type, and COM port.
5. Attempt only known-good read-only probe operations until the state is understood.

## Current suite posture

- Managed codeplug restore requires a prior recorded backup.
- `unsafe write-preflight` is validation-only and never writes to hardware.
- Firmware flashing is not implemented.
