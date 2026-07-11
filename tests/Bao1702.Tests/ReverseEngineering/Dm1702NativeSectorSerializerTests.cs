using Bao1702.Codeplug.Model;
using Bao1702.ReverseEngineering.Helpers;

namespace Bao1702.Tests.ReverseEngineering;

[TestClass]
public sealed class Dm1702NativeSectorSerializerTests
{
    [TestMethod]
    public void Initialize_WritesExpectedSectorMarkersAndZeroSectors()
    {
        var image = new byte[Dm1702NativeImageAssumptions.ImageLength];

        Dm1702NativeSectorSerializer.Initialize(image);

        Assert.AreEqual(0x00, image[0x0FFF]);
        Assert.AreEqual(0x16, image[(3 * Dm1702NativeImageAssumptions.SectorSize) + 0x0FFF]);
        Assert.AreEqual(0x24, image[(4 * Dm1702NativeImageAssumptions.SectorSize) + 0x0FFF]);
        Assert.AreEqual(0x27, image[(30 * Dm1702NativeImageAssumptions.SectorSize) + 0x0FFF]);
        Assert.AreEqual(0x56, image[(60 * Dm1702NativeImageAssumptions.SectorSize) - 1]);
        Assert.AreEqual(0x00, image[0]);
        Assert.AreEqual(0x00, image[Dm1702NativeImageAssumptions.SectorSize]);
        Assert.AreEqual(0x00, image[36 * Dm1702NativeImageAssumptions.SectorSize]);
    }
}
