# Safety model

Bao1702 treats every hardware write as destructive. USB-printer and serial transports share one `WriteIntentValidator` authorization path.

A codeplug write is allowed only when all of the following are true:

- the device was explicitly identified;
- the compatibility matrix recognizes the target family and marks it write-safe by default;
- an identity-bound backup exists in the backup ledger;
- the image has the exact expected size and passes structural validation;
- generated content has completed its preflight checks;
- the operator explicitly confirms the write before transport activity begins.

Unknown devices remain read-only even when a backup exists. A backup is necessary but never sufficient authorization. The preflight-only CLI command cannot perform a live write and cannot bypass target, size, or memory-safety validation.

Receive-only channels carry an explicit transmit-inhibit property. The serializer emits the observed RX-only flag; it does not infer transmit authorization from frequency equality, channel names, or radio-service labels.

Compatibility assumptions and unverified fields must fail closed. Automated tests use mocks and synthetic images; manual validation on user-owned hardware remains required.
