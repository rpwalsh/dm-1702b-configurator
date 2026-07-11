Place USBPcap or serial trace artifacts here.

Recommended workflow:

1. Save the raw capture (`.pcap`, `.pcapng`) under `captures/`.
2. Export a text hex dump with `tools/capture-analyzers/Export-UsbPcapTranscript.ps1`.
3. Normalize the text with `bao1702 capture normalize`.
4. Analyze the normalized transcript with `bao1702 capture analyze`.
