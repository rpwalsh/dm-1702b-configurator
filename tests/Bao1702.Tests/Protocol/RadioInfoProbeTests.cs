using Bao1702.Protocol.Discovery;
using Bao1702.Protocol.Model;
using Bao1702.Transport.Abstractions;
using Bao1702.Transport.Mock;

namespace Bao1702.Tests.Protocol;

[TestClass]
public sealed class RadioInfoProbeTests
{
    [TestMethod]
    public async Task ProbeAsync_ReturnsRadioIdentityForReachableMockDevice()
    {
        var device = new Bao1702.Protocol.MockRadioDevice();
        var factory = new MockTransportFactory(device.Handle);
        var endpoint = (await factory.EnumerateAsync().ConfigureAwait(false)).Single();
        var traces = new TransportTraceCollector();

        var result = await RadioInfoProbe.ProbeAsync(factory, endpoint, traces).ConfigureAwait(false);

        Assert.IsTrue(result.IsReachable);
        Assert.IsNotNull(result.RadioInfo);
        Assert.AreEqual(RadioVariant.Bao1702B, result.RadioInfo!.Identity.Variant);
        Assert.IsTrue(traces.Events.Count > 0);
        Assert.IsTrue(result.Notes.Any(note => note.Contains("synthetic", StringComparison.OrdinalIgnoreCase)));
    }
}
