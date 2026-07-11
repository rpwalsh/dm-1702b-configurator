using Bao1702.Protocol.Stock;

namespace Bao1702.Protocol;

/// <summary>
/// Default constants for protocol block sizes and transfer parameters.
/// </summary>
/// <remarks>
/// The <c>AssumedDefault*</c> constants are provisional values used by the synthetic
/// protocol scaffolding (<see cref="Bao1702ProtocolSession"/>). Real OEM-observed
/// sizes are exposed through <see cref="StockCpsSessionBootstrap"/> and are significantly
/// larger (e.g. codeplug read = 233,472 bytes across 14 segments, write = 229,376 bytes).
/// </remarks>
public static class ProtocolAssumptions
{
    /// <summary>Provisional synthetic codeplug size. Real observed read size: <see cref="StockCpsSessionBootstrap.ObservedReadImageSize"/>.</summary>
    public const int AssumedDefaultCodeplugSize = 4096;

    /// <summary>Provisional synthetic firmware size. Not yet validated against hardware captures.</summary>
    public const int AssumedDefaultFirmwareSize = 16384;

    public const int AssumedDefaultBlockSize = 64;

    public const string Notes = "Command IDs, framing, and block semantics are currently isolated assumptions for synthetic testing and early protocol scaffolding. Replace from captures as discoveries are confirmed. Real OEM-observed sizes live in StockCpsSessionBootstrap.";
}
