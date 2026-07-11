# Operator templates

These optional templates are starting points for operator-controlled CSV imports. They are never loaded automatically, used during decoding, or treated as proof that a configuration is authorized.

Template policy:

- Select a template manually and copy it into a separate working directory as `channels.csv`.
- Review and edit every row before import. Use the import preview and validation results before applying changes.
- Choose a template matching the relevant country or regulatory region; `us-amateur` is not a universal band plan.
- Do not add personal callsigns, DMR IDs, repeater access tones, or local operational data to this repository.
- Treat repeater and DMR rows labeled `EXAMPLE` as schema demonstrations, not valid local configurations.
- Keep receive-only services marked `ReceiveOnly=True` and do not populate transmit-capable replacements without understanding the consequences.
- Structural validation and warnings assist the operator; they do not determine authorization or regulatory compliance.

The operator and station control operator remain responsible for frequencies, privileges, identification, equipment settings, and local rules. The application does not infer a license class, claim that a channel is legal, or silently alter frequencies.
