using Bao1702.Cli.Commands;

namespace Bao1702.Tests.Cli;

[TestClass]
public sealed class RestoreCodeplugCommandTests
{
    [TestMethod]
    public async Task ExecuteAsync_WithoutAcknowledgement_NeverInvokesRestore()
    {
        var restoreCalls = 0;
        var command = new RestoreCodeplugCommand(_ =>
        {
            restoreCalls++;
            return Task.FromResult("written");
        });

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => command.ExecuteAsync("synthetic.data", acknowledgement: null));

        Assert.AreEqual(0, restoreCalls);
    }

    [TestMethod]
    public async Task ExecuteAsync_WithExactAcknowledgement_InvokesRestoreOnce()
    {
        var restoreCalls = 0;
        var command = new RestoreCodeplugCommand(path =>
        {
            restoreCalls++;
            return Task.FromResult(path);
        });

        var result = await command.ExecuteAsync(
            "synthetic.data",
            RestoreCodeplugCommand.RequiredAcknowledgement);

        Assert.AreEqual("synthetic.data", result);
        Assert.AreEqual(1, restoreCalls);
    }
}
