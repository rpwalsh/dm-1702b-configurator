# Interoperability Methodology

Bao1702 is independently implemented for interoperability. Public evidence is limited to observed binary layout, black-box behavior on user-owned hardware, independently generated configuration differences, deterministic serialization, and round-trip validation.

No manufacturer executable, firmware, resource, decompiled source, function map, or exact implementation reconstruction is part of the project. Public tests generate their own images or use explicitly synthetic text fixtures.

Confidence labels should distinguish independently verified fields, compatibility assumptions, unverified fields, and reserved regions. A passing self-round-trip is necessary but is not proof of device compatibility. Uncertain behavior must remain documented and must not enable a hardware write by default.
