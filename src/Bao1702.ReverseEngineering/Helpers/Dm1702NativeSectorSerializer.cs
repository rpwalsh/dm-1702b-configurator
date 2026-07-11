using Bao1702.Codeplug.Model;

namespace Bao1702.ReverseEngineering.Helpers;

/// <summary>
/// Writes sector marker bytes at the end of each 4K sector in the DM-1702 native image.
/// </summary>
public static class Dm1702NativeSectorSerializer
{
    private static readonly byte[] SectorMarkers =
    [
        0x00, 0x00, 0x02, 0x16, 0x24, 0x04, 0x45, 0x0B, 0x11, 0x01,
        0x0A, 0x13, 0x12, 0x03, 0x06, 0x17, 0x18, 0x19, 0x1A, 0x1B,
        0x1C, 0x1D, 0x1E, 0x1F, 0x20, 0x21, 0x22, 0x23, 0x25, 0x26,
        0x27, 0x28, 0x29, 0x2A, 0x2B, 0x2C, 0x00, 0x3F, 0x40, 0x41,
        0x42, 0x43, 0x44, 0x46, 0x47, 0x48, 0x49, 0x4A, 0x4B, 0x4C,
        0x4D, 0x4E, 0x4F, 0x50, 0x51, 0x52, 0x53, 0x54, 0x55, 0x56,
    ];

    public static void Initialize(Span<byte> image)
    {
        if (image.Length != Dm1702NativeImageAssumptions.ImageLength)
        {
            throw new ArgumentException("Native DM1702 image has an unexpected length.", nameof(image));
        }

        image.Fill(0xFF);
        ZeroSector(image, 0);
        ZeroSector(image, 1);
        ZeroSector(image, 36);

        for (var sectorIndex = 0; sectorIndex < SectorMarkers.Length; sectorIndex++)
        {
            image[((sectorIndex + 1) * Dm1702NativeImageAssumptions.SectorSize) - 1] = SectorMarkers[sectorIndex];
        }
    }

    private static void ZeroSector(Span<byte> image, int sectorIndex)
    {
        var start = sectorIndex * Dm1702NativeImageAssumptions.SectorSize;
        image.Slice(start, Dm1702NativeImageAssumptions.SectorSize).Fill(0x00);
    }
}
