using Bao1702.Protocol.Model;
using Bao1702.Protocol.Safety;

namespace Bao1702.Tests.Protocol;

[TestClass]
public sealed class BackupLedgerTests
{
    [TestMethod]
    public async Task RecordCodeplugBackup_CreatesManifestAndImage()
    {
        var identity = CreateIdentity();
        var backupRoot = CreateTemporaryDirectory();

        try
        {
            var ledger = new BackupLedger(backupRoot);
            var imageData = new byte[4096];
            imageData[0] = 0xAA;

            var record = await ledger.RecordCodeplugBackupAsync(identity, imageData, "test").ConfigureAwait(false);

            Assert.IsNotNull(record);
            Assert.AreEqual("codeplug", record.BackupKind);
            Assert.AreEqual(4096, record.ImageLength);
            Assert.IsTrue(File.Exists(record.ImagePath));
            Assert.IsTrue(File.Exists(record.ManifestPath));
            Assert.IsTrue(record.ImagePath.EndsWith(".data", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(backupRoot, recursive: true);
        }
    }

    [TestMethod]
    public async Task RecordFirmwareBackup_CreatesManifestWithFwExtension()
    {
        var identity = CreateIdentity();
        var backupRoot = CreateTemporaryDirectory();

        try
        {
            var ledger = new BackupLedger(backupRoot);
            var firmwareData = new byte[8192];
            firmwareData[0] = 0xBB;

            var record = await ledger.RecordFirmwareBackupAsync(identity, firmwareData, "test").ConfigureAwait(false);

            Assert.IsNotNull(record);
            Assert.AreEqual("firmware", record.BackupKind);
            Assert.AreEqual(8192, record.ImageLength);
            Assert.IsTrue(File.Exists(record.ImagePath));
            Assert.IsTrue(File.Exists(record.ManifestPath));
            Assert.IsTrue(record.ImagePath.EndsWith(".fw", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(backupRoot, recursive: true);
        }
    }

    [TestMethod]
    public async Task HasCodeplugBackup_ReturnsTrueAfterRecording()
    {
        var identity = CreateIdentity();
        var backupRoot = CreateTemporaryDirectory();

        try
        {
            var ledger = new BackupLedger(backupRoot);

            Assert.IsFalse(ledger.HasCodeplugBackup(identity));

            await ledger.RecordCodeplugBackupAsync(identity, new byte[4096], "test").ConfigureAwait(false);

            Assert.IsTrue(ledger.HasCodeplugBackup(identity));
        }
        finally
        {
            Directory.Delete(backupRoot, recursive: true);
        }
    }

    [TestMethod]
    public async Task HasFirmwareBackup_ReturnsTrueAfterRecording()
    {
        var identity = CreateIdentity();
        var backupRoot = CreateTemporaryDirectory();

        try
        {
            var ledger = new BackupLedger(backupRoot);

            Assert.IsFalse(ledger.HasFirmwareBackup(identity));

            await ledger.RecordFirmwareBackupAsync(identity, new byte[8192], "test").ConfigureAwait(false);

            Assert.IsTrue(ledger.HasFirmwareBackup(identity));
        }
        finally
        {
            Directory.Delete(backupRoot, recursive: true);
        }
    }

    [TestMethod]
    public async Task ListBackups_FiltersCorrectlyByKind()
    {
        var identity = CreateIdentity();
        var backupRoot = CreateTemporaryDirectory();

        try
        {
            var ledger = new BackupLedger(backupRoot);
            await ledger.RecordCodeplugBackupAsync(identity, new byte[4096], "test").ConfigureAwait(false);
            await ledger.RecordFirmwareBackupAsync(identity, new byte[8192], "test").ConfigureAwait(false);
            await ledger.RecordCodeplugBackupAsync(identity, new byte[4096], "test 2").ConfigureAwait(false);

            var allBackups = ledger.ListBackups(identity);
            var codeplugOnly = ledger.ListBackups(identity, "codeplug");
            var firmwareOnly = ledger.ListBackups(identity, "firmware");

            Assert.AreEqual(3, allBackups.Count);
            Assert.AreEqual(2, codeplugOnly.Count);
            Assert.AreEqual(1, firmwareOnly.Count);
        }
        finally
        {
            Directory.Delete(backupRoot, recursive: true);
        }
    }

    [TestMethod]
    public async Task BackupRecord_ContainsCorrectSha256()
    {
        var identity = CreateIdentity();
        var backupRoot = CreateTemporaryDirectory();

        try
        {
            var ledger = new BackupLedger(backupRoot);
            var imageData = new byte[256];
            imageData[0] = 0xDE;
            imageData[1] = 0xAD;

            var record = await ledger.RecordCodeplugBackupAsync(identity, imageData, "sha test").ConfigureAwait(false);

            var expectedSha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(imageData));
            Assert.AreEqual(expectedSha256, record.Sha256);
        }
        finally
        {
            Directory.Delete(backupRoot, recursive: true);
        }
    }

    private static RadioIdentity CreateIdentity()
    {
        return new RadioIdentity(
            RadioFamily.Bao1702,
            RadioVariant.Bao1702B,
            "Baofeng 1702B",
            new FirmwareVersion("V02.07.001"),
            new BootloaderVersion("BL01.03"),
            "1702B-TEST-LEDGER",
            new RadioCapabilities(true, true, true, false, true, 4096, 8192, ConfidenceLevel.Inferred),
            ConfidenceLevel.Inferred);
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "Bao1702Suite.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
