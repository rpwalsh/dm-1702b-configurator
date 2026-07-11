using Bao1702.Protocol;
using Bao1702.Protocol.Model;
using Bao1702.Protocol.Safety;
using Bao1702.Transport.Abstractions;
using Bao1702.Transport.Mock;

namespace Bao1702.Tests.Protocol;

[TestClass]
public sealed class ProtocolSessionIntegrationTests
{
    [TestMethod]
    public async Task ReadFullCodeplug_ReturnsExpectedImage()
    {
        var device = new MockRadioDevice();
        var factory = new MockTransportFactory(device.Handle);
        var endpoint = (await factory.EnumerateAsync().ConfigureAwait(false)).Single();
        await using var connection = await factory.OpenAsync(endpoint).ConfigureAwait(false);
        await connection.ConnectAsync().ConfigureAwait(false);

        var safetyOptions = SafetyPolicyOptions.Default with { AllowUnknownReadOnly = true };
        var sessionOptions = ProtocolSessionOptions.Default with { SafetyPolicy = safetyOptions };
        await using var session = new Bao1702ProtocolSession(connection, options: sessionOptions);

        var image = await session.ReadFullCodeplugAsync().ConfigureAwait(false);

        Assert.AreEqual(ProtocolAssumptions.AssumedDefaultCodeplugSize, image.Length);
        Assert.AreEqual((byte)(0 % 251), image[0]);
        Assert.AreEqual((byte)(63 % 251), image[63]);
    }

    [TestMethod]
    public async Task ReadFullCodeplug_ReportsProgressAccurately()
    {
        var device = new MockRadioDevice();
        var factory = new MockTransportFactory(device.Handle);
        var endpoint = (await factory.EnumerateAsync().ConfigureAwait(false)).Single();
        await using var connection = await factory.OpenAsync(endpoint).ConfigureAwait(false);
        await connection.ConnectAsync().ConfigureAwait(false);

        var sessionOptions = ProtocolSessionOptions.Default;
        await using var session = new Bao1702ProtocolSession(connection, options: sessionOptions);

        var progressReports = new List<ProtocolProgress>();
        var progress = new SynchronousProgress<ProtocolProgress>(progressReports);

        var image = await session.ReadFullCodeplugAsync(progress).ConfigureAwait(false);

        Assert.AreEqual(ProtocolAssumptions.AssumedDefaultCodeplugSize, image.Length);
        Assert.IsTrue(progressReports.Count > 0, "Expected at least one progress report.");

        var lastReport = progressReports[^1];
        Assert.AreEqual("ReadCodeplug", lastReport.OperationName);
        Assert.AreEqual(lastReport.TotalBlocks, lastReport.CurrentBlock);
        Assert.AreEqual(100.0, lastReport.PercentComplete, 0.1);
    }

    [TestMethod]
    public async Task WriteFullCodeplug_RoundTripsCorrectly()
    {
        var device = new MockRadioDevice();
        var factory = new MockTransportFactory(device.Handle);
        var endpoint = (await factory.EnumerateAsync().ConfigureAwait(false)).Single();
        await using var connection = await factory.OpenAsync(endpoint).ConfigureAwait(false);
        await connection.ConnectAsync().ConfigureAwait(false);

        var safetyOptions = SafetyPolicyOptions.Default with { BackupCompleted = true };
        var sessionOptions = new ProtocolSessionOptions(safetyOptions, ProtocolAssumptions.AssumedDefaultBlockSize);
        await using var session = new Bao1702ProtocolSession(connection, options: sessionOptions);

        var originalImage = await session.ReadFullCodeplugAsync().ConfigureAwait(false);

        var modifiedImage = originalImage.ToArray();
        modifiedImage[0] = 0xAA;
        modifiedImage[1] = 0xBB;

        await session.WriteFullCodeplugAsync(modifiedImage).ConfigureAwait(false);

        var readBack = await session.ReadFullCodeplugAsync().ConfigureAwait(false);

        Assert.AreEqual(0xAA, readBack[0]);
        Assert.AreEqual(0xBB, readBack[1]);
        Assert.AreEqual(modifiedImage.Length, readBack.Length);
    }

    [TestMethod]
    public async Task WriteFullCodeplug_RejectsWrongSizeImage()
    {
        var device = new MockRadioDevice();
        var factory = new MockTransportFactory(device.Handle);
        var endpoint = (await factory.EnumerateAsync().ConfigureAwait(false)).Single();
        await using var connection = await factory.OpenAsync(endpoint).ConfigureAwait(false);
        await connection.ConnectAsync().ConfigureAwait(false);

        var safetyOptions = SafetyPolicyOptions.Default with { BackupCompleted = true };
        var sessionOptions = new ProtocolSessionOptions(safetyOptions, ProtocolAssumptions.AssumedDefaultBlockSize);
        await using var session = new Bao1702ProtocolSession(connection, options: sessionOptions);

        var wrongSize = new byte[100];
        var threw = false;

        try
        {
            await session.WriteFullCodeplugAsync(wrongSize).ConfigureAwait(false);
        }
        catch (SafetyException)
        {
            threw = true;
        }

        Assert.IsTrue(threw, "Expected SafetyException for wrong-size codeplug image.");
    }

    [TestMethod]
    public async Task BackupFirmware_ReturnsExpectedImage()
    {
        var device = new MockRadioDevice();
        var factory = new MockTransportFactory(device.Handle);
        var endpoint = (await factory.EnumerateAsync().ConfigureAwait(false)).Single();
        await using var connection = await factory.OpenAsync(endpoint).ConfigureAwait(false);
        await connection.ConnectAsync().ConfigureAwait(false);

        await using var session = new Bao1702ProtocolSession(connection);

        var firmware = await session.BackupFirmwareAsync().ConfigureAwait(false);

        Assert.AreEqual(ProtocolAssumptions.AssumedDefaultFirmwareSize, firmware.Length);
        Assert.AreEqual((byte)(255 - (0 % 251)), firmware[0]);
    }

    [TestMethod]
    public async Task BackupFirmware_ReportsProgress()
    {
        var device = new MockRadioDevice();
        var factory = new MockTransportFactory(device.Handle);
        var endpoint = (await factory.EnumerateAsync().ConfigureAwait(false)).Single();
        await using var connection = await factory.OpenAsync(endpoint).ConfigureAwait(false);
        await connection.ConnectAsync().ConfigureAwait(false);

        await using var session = new Bao1702ProtocolSession(connection);

        var progressReports = new List<ProtocolProgress>();
        var progress = new SynchronousProgress<ProtocolProgress>(progressReports);

        var firmware = await session.BackupFirmwareAsync(progress).ConfigureAwait(false);

        Assert.AreEqual(ProtocolAssumptions.AssumedDefaultFirmwareSize, firmware.Length);
        Assert.IsTrue(progressReports.Count > 0, "Expected at least one progress report.");
        Assert.AreEqual("BackupFirmware", progressReports[^1].OperationName);
    }

    [TestMethod]
    public async Task ReadRtc_ReturnsExpectedValue()
    {
        var device = new MockRadioDevice();
        var factory = new MockTransportFactory(device.Handle);
        var endpoint = (await factory.EnumerateAsync().ConfigureAwait(false)).Single();
        await using var connection = await factory.OpenAsync(endpoint).ConfigureAwait(false);
        await connection.ConnectAsync().ConfigureAwait(false);

        await using var session = new Bao1702ProtocolSession(connection);

        var rtc = await session.ReadRtcAsync().ConfigureAwait(false);

        Assert.AreEqual(2025, rtc.Year);
        Assert.AreEqual(1, rtc.Month);
        Assert.AreEqual(1, rtc.Day);
    }

    [TestMethod]
    public async Task WriteRtc_RoundTrips()
    {
        var device = new MockRadioDevice();
        var factory = new MockTransportFactory(device.Handle);
        var endpoint = (await factory.EnumerateAsync().ConfigureAwait(false)).Single();
        await using var connection = await factory.OpenAsync(endpoint).ConfigureAwait(false);
        await connection.ConnectAsync().ConfigureAwait(false);

        var safetyOptions = SafetyPolicyOptions.Default with { BackupCompleted = true };
        var sessionOptions = new ProtocolSessionOptions(safetyOptions, ProtocolAssumptions.AssumedDefaultBlockSize);
        await using var session = new Bao1702ProtocolSession(connection, options: sessionOptions);

        var newTime = DateTimeOffset.Parse("2025-07-04T12:00:00+00:00", null, System.Globalization.DateTimeStyles.RoundtripKind);
        await session.WriteRtcAsync(newTime).ConfigureAwait(false);

        var readBack = await session.ReadRtcAsync().ConfigureAwait(false);

        Assert.AreEqual(newTime.Year, readBack.Year);
        Assert.AreEqual(newTime.Month, readBack.Month);
        Assert.AreEqual(newTime.Day, readBack.Day);
    }

    [TestMethod]
    public async Task ValidateTargetCompatibility_ReturnsKnownFamily()
    {
        var device = new MockRadioDevice();
        var factory = new MockTransportFactory(device.Handle);
        var endpoint = (await factory.EnumerateAsync().ConfigureAwait(false)).Single();
        await using var connection = await factory.OpenAsync(endpoint).ConfigureAwait(false);
        await connection.ConnectAsync().ConfigureAwait(false);

        await using var session = new Bao1702ProtocolSession(connection);

        var compatibility = await session.ValidateTargetCompatibilityAsync().ConfigureAwait(false);

        Assert.IsTrue(compatibility.IsKnownFamily);
    }

    [TestMethod]
    public async Task ReadFullCodeplug_AllowsNullTraceSink()
    {
        var device = new MockRadioDevice();
        var factory = new MockTransportFactory(device.Handle);
        var endpoint = (await factory.EnumerateAsync().ConfigureAwait(false)).Single();
        await using var connection = await factory.OpenAsync(endpoint).ConfigureAwait(false);
        connection.TraceSink = null;
        await connection.ConnectAsync().ConfigureAwait(false);

        await using var session = new Bao1702ProtocolSession(connection);

        var image = await session.ReadFullCodeplugAsync().ConfigureAwait(false);

        Assert.AreEqual(ProtocolAssumptions.AssumedDefaultCodeplugSize, image.Length);
    }

    private sealed class SynchronousProgress<T>(List<T> reports) : IProgress<T>
    {
        public void Report(T value) => reports.Add(value);
    }
}
