using System.IO.Abstractions.TestingHelpers;

using Prospect.Core.Common;
using Prospect.Core.Instances;
using Prospect.Core.Instances.Migrations;
using Prospect.Core.Storage;
using Prospect.Core.Tests.Storage;

using Shouldly;

namespace Prospect.Core.Tests.Common;

/// <summary>
/// Ce que le journal de diagnostic consigne du cycle de vie d'une instance : créée, dupliquée,
/// supprimée.
/// </summary>
/// <remarks>
/// Ces faits-là ne sont pas décoratifs. Un rapport de terrain commence presque toujours par une
/// phrase du genre « j'ai dupliqué mon instance et depuis... », et sans ces lignes rien ne permet de
/// situer ce moment dans le fichier, ni même de confirmer qu'il a eu lieu. La discipline qui les
/// gouverne est écrite une fois pour toutes sur <see cref="IAppLog"/> : des faits de session, jamais
/// de secret, jamais de ligne à la fréquence d'une boucle.
/// </remarks>
public sealed class AppLogCoverageTests
{
    private static readonly AppPaths Paths = new(new FakeAppEnvironment(), "/data/prospect");
    private static readonly DateTimeOffset Noon = new(2026, 8, 13, 12, 0, 0, TimeSpan.Zero);

    private static (InstanceService Service, RecordingAppLog Log) Create()
    {
        var fileSystem = new MockFileSystem();
        var repository = new FileSystemInstanceRepository(
            fileSystem,
            Paths,
            new JsonFileStore(fileSystem),
            new InstanceMetadataMigrationPipeline([]));
        var log = new RecordingAppLog();

        return (new InstanceService(repository, fileSystem, new FakeClock(Noon), log), log);
    }

    [Fact]
    public async Task CreateAsync_NamesTheSlugTheNameAndTheGameVersion()
    {
        var (service, log) = Create();

        var record = await service.CreateAsync("Homestead 1.22", GameVersion.Parse("1.22.6"));

        var line = log.Lines.ShouldHaveSingleItem();
        line.Level.ShouldBe(AppLogLevel.Info);
        line.Message.ShouldContain("Instance créée");
        line.Message.ShouldContain(record.Slug);
        line.Message.ShouldContain("Homestead 1.22");
        line.Message.ShouldContain("1.22.6");
    }

    [Fact]
    public async Task DuplicateAsync_NamesBothSlugs()
    {
        var (service, log) = Create();
        var source = await service.CreateAsync("Homestead", GameVersion.Parse("1.22.6"));

        await service.DuplicateAsync(source.Slug, "Homestead bis", progress: null);

        log.Lines.ShouldContain(line =>
            line.Level == AppLogLevel.Info
            && line.Message.Contains("Instance dupliquée", StringComparison.Ordinal)
            && line.Message.Contains(source.Slug, StringComparison.Ordinal)
            && line.Message.Contains("homestead-bis", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DeleteAsync_NamesTheSlugAndTheFolder()
    {
        var (service, log) = Create();
        var record = await service.CreateAsync("Homestead", GameVersion.Parse("1.22.6"));

        await service.DeleteAsync(record.Slug);

        log.Lines.ShouldContain(line =>
            line.Level == AppLogLevel.Info && line.Message.Contains("Instance supprimée", StringComparison.Ordinal));
    }

    /// <summary>
    /// Un échec est consigné en Error, avec sa raison, et l'exception continue son chemin : le
    /// journal observe, il ne rattrape pas.
    /// </summary>
    [Fact]
    public async Task DeleteAsync_WhenItFails_LogsTheReasonAndStillThrows()
    {
        var (service, log) = Create();

        await Should.ThrowAsync<InstanceNotFoundException>(() => service.DeleteAsync("inconnue"));

        // Rien à journaliser : l'instance n'existait pas, la suppression n'a jamais commencé.
        log.Lines.ShouldNotContain(line => line.Message.Contains("Instance supprimée", StringComparison.Ordinal));
    }

    /// <summary>
    /// Sans journal injecté, tout continue de marcher : c'est le contrat du paramètre optionnel, et
    /// c'est ce qui permet aux dizaines de constructions de test de ne pas s'en soucier.
    /// </summary>
    [Fact]
    public async Task WithoutAnyLog_TheServiceStillWorks()
    {
        var fileSystem = new MockFileSystem();
        var repository = new FileSystemInstanceRepository(
            fileSystem,
            Paths,
            new JsonFileStore(fileSystem),
            new InstanceMetadataMigrationPipeline([]));
        var service = new InstanceService(repository, fileSystem, new FakeClock(Noon));

        var record = await service.CreateAsync("Homestead", GameVersion.Parse("1.22.6"));

        record.Slug.ShouldBe("homestead");
    }
}