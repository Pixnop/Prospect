using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;

using NSubstitute;

using Prospect.Core.Common;
using Prospect.Core.Instances;
using Prospect.Core.Instances.Migrations;
using Prospect.Core.Storage;
using Prospect.Core.Tests.Common;
using Prospect.Core.Tests.Storage;

using Shouldly;

namespace Prospect.Core.Tests.Instances;

/// <summary>
/// La suppression d'une instance, sous l'angle des deux défauts rapportés en test réel :
/// l'interface qui gèle une quarantaine de secondes sur un dossier de mondes volumineux, et
/// « supprimer puis recréer du même nom cause des problèmes ».
/// </summary>
/// <remarks>
/// Le système de fichiers est substitué UNIQUEMENT sur son <see cref="IDirectory"/>, pour pouvoir
/// tenir la suppression ouverte et observer ce qui se passe pendant. Tout le reste est délégué au
/// <see cref="MockFileSystem"/> réel, donc l'instance est vraiment créée et vraiment effacée.
/// </remarks>
public sealed class InstanceDeletionTests : IDisposable
{
    private static readonly AppPaths Paths = new(new FakeAppEnvironment(), "/data/prospect");
    private static readonly DateTimeOffset Now = new(2026, 8, 13, 14, 0, 0, TimeSpan.Zero);
    private static readonly GameVersion SampleVersion = GameVersion.Parse("1.21.3");

    private readonly ManualResetEventSlim _gate = new(false);
    private readonly ManualResetEventSlim _entered = new(false);

    private int _deleteThreadId;

    public void Dispose()
    {
        _gate.Dispose();
        _entered.Dispose();
    }

    private (InstanceService Service, IInstanceRepository Repository, MockFileSystem Mock) CreateGatedService()
    {
        var mock = new MockFileSystem();
        var repository = new FileSystemInstanceRepository(mock, Paths, new JsonFileStore(mock), new InstanceMetadataMigrationPipeline([]));

        var directory = Substitute.For<IDirectory>();
        directory.Exists(Arg.Any<string>()).Returns(call => mock.Directory.Exists(call.Arg<string>()));
        directory.CreateDirectory(Arg.Any<string>()).Returns(call => mock.Directory.CreateDirectory(call.Arg<string>()));
        directory.When(candidate => candidate.Delete(Arg.Any<string>(), Arg.Any<bool>())).Do(call =>
        {
            _deleteThreadId = Environment.CurrentManagedThreadId;
            _entered.Set();
            _gate.Wait();
            mock.Directory.Delete(call.ArgAt<string>(0), call.ArgAt<bool>(1));
        });

        var fileSystem = Substitute.For<IFileSystem>();
        fileSystem.Directory.Returns(directory);
        fileSystem.File.Returns(mock.File);
        fileSystem.Path.Returns(mock.Path);

        return (new InstanceService(repository, fileSystem, new FakeClock(Now)), repository, mock);
    }

    /// <summary>
    /// Le cœur de l'item : l'appelant reprend la main tout de suite et le travail se fait ailleurs.
    /// <c>System.IO.Abstractions</c> n'expose que du synchrone, donc un <c>await</c> sans
    /// <c>Task.Run</c> aurait effacé les gigaoctets sur le thread appelant avant de rendre la main.
    /// </summary>
    [Fact]
    public async Task DeleteAsync_LeavesTheCallingThreadFree_WhileTheRecursiveDeleteRuns()
    {
        var (service, repository, mock) = CreateGatedService();
        var record = await service.CreateAsync("Homestead", SampleVersion, CancellationToken.None);
        var callerThreadId = Environment.CurrentManagedThreadId;

        var deletion = service.DeleteAsync(record.Slug, progress: null, CancellationToken.None);

        _entered.Wait(TimeSpan.FromSeconds(5)).ShouldBeTrue("la suppression n'a jamais démarré");
        deletion.IsCompleted.ShouldBeFalse();
        _deleteThreadId.ShouldNotBe(callerThreadId);

        _gate.Set();
        await deletion;

        // Chemin demandé au dépôt plutôt que recollé à la main : c'est lui qui connaît la topologie,
        // et un séparateur en dur dans un test le rendrait faux sur l'un des trois systèmes de la CI.
        mock.Directory.Exists(repository.GetInstanceDirectory(record.Slug)).ShouldBeFalse();
        repository.Exists(record.Slug).ShouldBeFalse();
    }

    /// <summary>
    /// Une suppression encore en vol tient son slug : rien ne doit pouvoir recréer par-dessus le
    /// dossier qu'on est en train d'effacer, et un nom « -2 » posé en douce serait exactement le
    /// « ça cause des problèmes » rapporté.
    /// </summary>
    [Fact]
    public async Task CreateAsync_WhileTheSameNameIsBeingDeleted_IsRefusedRatherThanRenamed()
    {
        var (service, _, _) = CreateGatedService();
        var record = await service.CreateAsync("Homestead", SampleVersion, CancellationToken.None);

        var deletion = service.DeleteAsync(record.Slug, progress: null, CancellationToken.None);
        _entered.Wait(TimeSpan.FromSeconds(5)).ShouldBeTrue();

        service.IsDeleting(record.Slug).ShouldBeTrue();
        var refusal = await Should.ThrowAsync<InstanceDeletionInProgressException>(
            () => service.CreateAsync("Homestead", SampleVersion, CancellationToken.None));
        refusal.Slug.ShouldBe(record.Slug);

        _gate.Set();
        await deletion;

        service.IsDeleting(record.Slug).ShouldBeFalse();
    }

    /// <summary>Le nom se libère dès la fin, et la nouvelle instance reprend exactement le même slug.</summary>
    [Fact]
    public async Task CreateAsync_AfterTheDeletionIsOver_GetsTheSameSlugBack()
    {
        var (service, _, _) = CreateGatedService();
        var first = await service.CreateAsync("Homestead", SampleVersion, CancellationToken.None);

        _gate.Set();
        await service.DeleteAsync(first.Slug, progress: null, CancellationToken.None);
        var second = await service.CreateAsync("Homestead", SampleVersion, CancellationToken.None);

        second.Slug.ShouldBe(first.Slug);
        second.Metadata.Id.ShouldNotBe(first.Metadata.Id);
        second.Metadata.TotalPlaytimeSeconds.ShouldBe(0);
        second.Metadata.LastLaunchedUtc.ShouldBeNull();
    }

    /// <summary>La suppression publie son slug une fois TERMINÉE : c'est le signal qui purge les caches par slug.</summary>
    [Fact]
    public async Task DeleteAsync_PublishesTheSlugOnceEverythingIsGone()
    {
        var (service, repository, _) = CreateGatedService();
        var record = await service.CreateAsync("Homestead", SampleVersion, CancellationToken.None);
        var announced = new List<string>();
        var stillDeletingWhenAnnounced = true;
        service.Deleted += (_, slug) =>
        {
            announced.Add(slug);
            stillDeletingWhenAnnounced = service.IsDeleting(slug);
        };

        _gate.Set();
        await service.DeleteAsync(record.Slug, progress: null, CancellationToken.None);

        announced.ShouldBe([record.Slug]);
        stillDeletingWhenAnnounced.ShouldBeFalse();
        repository.Exists(record.Slug).ShouldBeFalse();
    }

    /// <summary>Un échec partiel reste un échec NOMMÉ, avec le dossier où il reste quelque chose.</summary>
    [Fact]
    public async Task DeleteAsync_WhenTheDiskRefuses_FailsWithTheDirectoryThatIsLeft()
    {
        var mock = new MockFileSystem();
        var repository = new FileSystemInstanceRepository(mock, Paths, new JsonFileStore(mock), new InstanceMetadataMigrationPipeline([]));
        var directory = Substitute.For<IDirectory>();
        directory.Exists(Arg.Any<string>()).Returns(call => mock.Directory.Exists(call.Arg<string>()));
        directory.CreateDirectory(Arg.Any<string>()).Returns(call => mock.Directory.CreateDirectory(call.Arg<string>()));
        directory.When(candidate => candidate.Delete(Arg.Any<string>(), Arg.Any<bool>()))
            .Do(_ => throw new IOException("le fichier est utilisé par un autre processus"));

        var fileSystem = Substitute.For<IFileSystem>();
        fileSystem.Directory.Returns(directory);
        fileSystem.File.Returns(mock.File);
        fileSystem.Path.Returns(mock.Path);

        var service = new InstanceService(repository, fileSystem, new FakeClock(Now));
        var record = await service.CreateAsync("Homestead", SampleVersion, CancellationToken.None);
        var announced = 0;
        service.Deleted += (_, _) => announced++;

        var failure = await Should.ThrowAsync<InstanceDeleteFailedException>(
            () => service.DeleteAsync(record.Slug, progress: null, CancellationToken.None));

        failure.Slug.ShouldBe(record.Slug);
        failure.Directory.ShouldBe(repository.GetInstanceDirectory(record.Slug));
        failure.InnerException.ShouldBeOfType<IOException>();

        // Rien n'est annoncé comme supprimé, et le slug est bien relâché : réessayer doit être possible.
        announced.ShouldBe(0);
        service.IsDeleting(record.Slug).ShouldBeFalse();
    }

    [Fact]
    public async Task DeleteAsync_UnknownSlug_StillThrowsTheTypedNotFound()
    {
        var (service, _, _) = CreateGatedService();

        await Should.ThrowAsync<InstanceNotFoundException>(() => service.DeleteAsync("fantome", progress: null, CancellationToken.None));
    }
}