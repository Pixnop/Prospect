using System.IO.Abstractions.TestingHelpers;

using Prospect.Core.Common;
using Prospect.Core.Storage;
using Prospect.Core.Tests.Storage;

using Shouldly;

namespace Prospect.Core.Tests.Common;

/// <summary>
/// Le journal de diagnostic : le fichier qu'un utilisateur joindra à son prochain rapport.
/// </summary>
public sealed class FileAppLogTests
{
    private static readonly AppPaths Paths = new(new FakeAppEnvironment(), "/data/prospect");
    private static readonly DateTimeOffset Noon = new(2026, 8, 13, 12, 30, 15, TimeSpan.Zero);

    private static (FileAppLog Log, MockFileSystem FileSystem) Create()
    {
        var fileSystem = new MockFileSystem();

        return (new FileAppLog(fileSystem, Paths, new FakeClock(Noon)), fileSystem);
    }

    [Fact]
    public void Write_TimestampsInUtcIso8601AndNamesTheLevel()
    {
        var (log, fileSystem) = Create();

        log.Write(AppLogLevel.Info, "Installeur Vintage Story : /VERYSILENT");

        fileSystem.File.ReadAllText(log.FilePath)
            .ShouldBe($"2026-08-13T12:30:15Z [INFO] Installeur Vintage Story : /VERYSILENT{Environment.NewLine}");
    }

    [Theory]
    [InlineData(AppLogLevel.Info, "INFO")]
    [InlineData(AppLogLevel.Warning, "WARN")]
    [InlineData(AppLogLevel.Error, "ERROR")]
    public void Write_EveryLevel_HasItsOwnLabel(AppLogLevel level, string expected)
    {
        var (log, fileSystem) = Create();

        log.Write(level, "message");

        fileSystem.File.ReadAllText(log.FilePath).ShouldContain($"[{expected}]");
    }

    [Fact]
    public void Write_SeveralLines_AppendsRatherThanReplaces()
    {
        var (log, fileSystem) = Create();

        log.Write(AppLogLevel.Info, "première");
        log.Write(AppLogLevel.Error, "seconde");

        fileSystem.File.ReadAllLines(log.FilePath).Length.ShouldBe(2);
    }

    [Fact]
    public void Write_CreatesTheLogsDirectoryWhenItDoesNotExistYet()
    {
        var (log, fileSystem) = Create();
        fileSystem.Directory.Exists(Paths.LogsDirectory).ShouldBeFalse();

        log.Write(AppLogLevel.Info, "premier démarrage");

        fileSystem.File.Exists(log.FilePath).ShouldBeTrue();
    }

    /// <summary>Un journal qui grossit sans fin finit par être le vrai défaut : au plafond, il repart de zéro.</summary>
    [Fact]
    public void Write_PastTheSizeCap_StartsOver()
    {
        var (log, fileSystem) = Create();
        fileSystem.Directory.CreateDirectory(Paths.LogsDirectory);
        fileSystem.File.WriteAllText(log.FilePath, new string('x', (int)FileAppLog.MaxSizeBytes + 1));

        log.Write(AppLogLevel.Info, "après le plafond");

        var content = fileSystem.File.ReadAllText(log.FilePath);
        content.Length.ShouldBeLessThan((int)FileAppLog.MaxSizeBytes);
        content.ShouldContain("après le plafond");
    }

    /// <summary>
    /// Perdre une ligne de journal est sans conséquence ; faire échouer l'installation qu'elle
    /// décrit ne l'est pas. Un disque qui refuse l'écriture ne remonte donc jamais.
    /// </summary>
    [Fact]
    public void Write_WhenTheDiskRefuses_SwallowsTheFailure()
    {
        var fileSystem = new MockFileSystem();
        var log = new FileAppLog(fileSystem, Paths, new FakeClock(Noon));

        // Un DOSSIER là où le fichier devrait aller : toute écriture y est refusée.
        fileSystem.Directory.CreateDirectory(log.FilePath);

        Should.NotThrow(() => log.Write(AppLogLevel.Info, "sans effet"));
    }

    [Fact]
    public void NullLog_WritesNothingAndNeverThrows()
        => Should.NotThrow(() => NullAppLog.Instance.Write(AppLogLevel.Error, "ignoré"));

    [Fact]
    public void Constructor_NullArguments_ThrowArgumentNullException()
    {
        var fileSystem = new MockFileSystem();
        var clock = new FakeClock(Noon);

        Should.Throw<ArgumentNullException>(() => new FileAppLog(null!, Paths, clock));
        Should.Throw<ArgumentNullException>(() => new FileAppLog(fileSystem, null!, clock));
        Should.Throw<ArgumentNullException>(() => new FileAppLog(fileSystem, Paths, null!));
        Should.Throw<ArgumentNullException>(() => new FileAppLog(fileSystem, Paths, clock).Write(AppLogLevel.Info, null!));
    }
}