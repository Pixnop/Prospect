using System.IO.Abstractions.TestingHelpers;

using Prospect.Core.Backups;
using Prospect.Core.Common;
using Prospect.Core.Instances;
using Prospect.Core.Instances.Migrations;
using Prospect.Core.Storage;
using Prospect.Desktop.Tests.TestDoubles;
using Prospect.Desktop.ViewModels.Dialogs;
using Prospect.Desktop.ViewModels.Instance;

using Shouldly;

namespace Prospect.Desktop.Tests.ViewModels.Instance;

/// <summary>
/// Bloc Sauvegardes de l'onglet Options : toggle auto-avant-lancement, sélecteur de rétention,
/// création avec progression, liste, et ouverture des dialogues de restauration/suppression.
/// </summary>
public sealed class InstanceBackupsSectionViewModelTests
{
    private static readonly AppPaths Paths = new(new SystemAppEnvironment(), "/data/prospect");
    private static readonly DateTimeOffset Now = new(2026, 8, 10, 14, 0, 0, TimeSpan.Zero);

    private sealed record Fixture(
        InstanceBackupsSectionViewModel Section,
        InstanceService Instances,
        IInstanceRepository Repository,
        InstanceBackupService Backups,
        FakeClock Clock,
        RecordingOverlayService Overlay,
        RecordingToastService Toasts,
        MockFileSystem FileSystem,
        string Slug);

    private static async Task<Fixture> CreateAsync(InstanceBackupSettings? settings = null)
    {
        var fileSystem = new MockFileSystem();
        var clock = new FakeClock(Now);
        var repository = new FileSystemInstanceRepository(fileSystem, Paths, new JsonFileStore(fileSystem), new InstanceMetadataMigrationPipeline([]));
        var instanceService = new InstanceService(repository, fileSystem, clock);
        var backupService = new InstanceBackupService(repository, fileSystem, clock);
        var record = await instanceService.CreateAsync("Homestead", GameVersion.Parse("1.21.3"));
        if (settings is not null)
        {
            await instanceService.UpdateBackupSettingsAsync(record.Slug, settings, CancellationToken.None);
        }

        var overlay = new RecordingOverlayService();
        var toasts = new RecordingToastService();
        var section = new InstanceBackupsSectionViewModel(
            record.Slug, "Homestead", settings ?? InstanceBackupSettings.Default, instanceService, backupService, overlay, clock, toasts);

        return new Fixture(section, instanceService, repository, backupService, clock, overlay, toasts, fileSystem, record.Slug);
    }

    [Fact]
    public async Task Constructor_SeedsAutoBeforeLaunchAndKeepCountFromSettings()
    {
        var fixture = await CreateAsync(new InstanceBackupSettings { AutoBeforeLaunch = true, KeepCount = 10 });

        fixture.Section.AutoBeforeLaunch.ShouldBeTrue();
        fixture.Section.KeepCount.ShouldBe(10);
    }

    [Fact]
    public async Task Constructor_DoesNotPersistTheSeedValuesBack()
    {
        // Affecter les champs de réglage à la construction ne doit pas déclencher une écriture :
        // sinon charger la page suffirait à sauvegarder, même sans que l'utilisateur touche rien
        // (même piège que InstanceOptionsTabViewModel avec ses champs texte).
        var fixture = await CreateAsync(new InstanceBackupSettings { AutoBeforeLaunch = true, KeepCount = 10 });

        var reloaded = await fixture.Repository.LoadAsync(fixture.Slug, CancellationToken.None);
        reloaded.Metadata.Backups.AutoBeforeLaunch.ShouldBeTrue();
        reloaded.Metadata.Backups.KeepCount.ShouldBe(10);
    }

    [Fact]
    public async Task RefreshAsync_NoBackups_HasBackupsFalse()
    {
        var fixture = await CreateAsync();

        await fixture.Section.RefreshCommand.ExecuteAsync(null);

        fixture.Section.HasBackups.ShouldBeFalse();
        fixture.Section.Backups.ShouldBeEmpty();
    }

    [Fact]
    public async Task RefreshAsync_ListsBackupsNewestFirstWithHumanizedDateAndSize()
    {
        var fixture = await CreateAsync();
        await fixture.Backups.CreateAsync(fixture.Slug, progress: null, CancellationToken.None);
        fixture.Clock.UtcNow = Now.AddDays(1);
        await fixture.Backups.CreateAsync(fixture.Slug, progress: null, CancellationToken.None);

        await fixture.Section.RefreshCommand.ExecuteAsync(null);

        fixture.Section.HasBackups.ShouldBeTrue();
        fixture.Section.Backups.Count.ShouldBe(2);
        fixture.Section.Backups[0].DateText.ShouldBe("aujourd'hui"); // le plus récent (Now+1j), en premier.
        fixture.Section.Backups[1].DateText.ShouldBe("hier");
        fixture.Section.Backups[0].SizeText.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task CreateNowAsync_CreatesABackupRefreshesTheListAndShowsASuccessToast()
    {
        var fixture = await CreateAsync();

        await fixture.Section.CreateNowCommand.ExecuteAsync(null);

        fixture.Section.Backups.ShouldHaveSingleItem();
        fixture.Section.IsCreating.ShouldBeFalse();
        fixture.Section.CreateProgressText.ShouldBeEmpty();
        fixture.Toasts.Shown.ShouldContain(t => t.Title == "Sauvegarde créée");
    }

    [Fact]
    public async Task CreateNowAsync_Failure_ShowsAnErrorToastWithoutCrashing()
    {
        var fixture = await CreateAsync();
        // Un fichier occupe déjà l'emplacement du dossier backups/ : la création échoue proprement.
        fixture.FileSystem.AddFile(fixture.Backups.GetBackupsDirectory(fixture.Slug), new MockFileData("obstacle"));

        await fixture.Section.CreateNowCommand.ExecuteAsync(null);

        fixture.Section.IsCreating.ShouldBeFalse();
        fixture.Toasts.Shown.ShouldContain(t => t.Title == "Sauvegarde impossible");
    }

    [Fact]
    public async Task AutoBeforeLaunchChanged_PersistsToInstanceService()
    {
        var fixture = await CreateAsync();

        fixture.Section.AutoBeforeLaunch = true;

        var reloaded = await fixture.Repository.LoadAsync(fixture.Slug, CancellationToken.None);
        reloaded.Metadata.Backups.AutoBeforeLaunch.ShouldBeTrue();
    }

    [Fact]
    public async Task KeepCountChanged_PersistsToInstanceService()
    {
        var fixture = await CreateAsync();

        fixture.Section.KeepCount = 10;

        var reloaded = await fixture.Repository.LoadAsync(fixture.Slug, CancellationToken.None);
        reloaded.Metadata.Backups.KeepCount.ShouldBe(10);
    }

    [Fact]
    public async Task AllowedKeepCounts_MatchesTheModelConstant()
    {
        var fixture = await CreateAsync();

        fixture.Section.AllowedKeepCounts.ShouldBe(InstanceBackupSettings.AllowedKeepCounts);
    }

    [Fact]
    public async Task RequestRestore_OpensRestoreDialogNamingInstanceAndDate()
    {
        var fixture = await CreateAsync();
        await fixture.Backups.CreateAsync(fixture.Slug, progress: null, CancellationToken.None);
        await fixture.Section.RefreshCommand.ExecuteAsync(null);
        var row = fixture.Section.Backups.ShouldHaveSingleItem();

        await row.RestoreCommand.ExecuteAsync(null);

        var dialog = fixture.Overlay.Shown.ShouldHaveSingleItem().ShouldBeOfType<RestoreInstanceBackupDialogViewModel>();
        dialog.Title.ShouldContain("Homestead");
    }

    [Fact]
    public async Task ConfirmRestore_RefreshesTheListAndShowsASuccessToast()
    {
        var fixture = await CreateAsync();
        await fixture.Backups.CreateAsync(fixture.Slug, progress: null, CancellationToken.None);
        await fixture.Section.RefreshCommand.ExecuteAsync(null);
        var row = fixture.Section.Backups.ShouldHaveSingleItem();

        await row.RestoreCommand.ExecuteAsync(null);
        var dialog = (RestoreInstanceBackupDialogViewModel)fixture.Overlay.Shown[0];
        await dialog.ConfirmCommand.ExecuteAsync(null);

        fixture.Toasts.Shown.ShouldContain(t => t.Title == "Sauvegarde restaurée");
        fixture.Overlay.Active.ShouldBeNull();
        // La restauration a pris sa propre sauvegarde de sécurité : la liste en montre donc deux.
        fixture.Section.Backups.Count.ShouldBe(2);
    }

    [Fact]
    public async Task ConfirmRestore_BackupGoneBeforeConfirm_ShowsFailureMessageAndKeepsTheDialogOpen()
    {
        var fixture = await CreateAsync();
        var info = await fixture.Backups.CreateAsync(fixture.Slug, progress: null, CancellationToken.None);
        await fixture.Section.RefreshCommand.ExecuteAsync(null);
        var row = fixture.Section.Backups.ShouldHaveSingleItem();
        await row.RestoreCommand.ExecuteAsync(null);
        var dialog = (RestoreInstanceBackupDialogViewModel)fixture.Overlay.Shown[0];
        // Disparue entre l'ouverture du dialogue et la confirmation (supprimée depuis une autre
        // fenêtre, par exemple) : ConfirmAsync ne doit ni planter ni fermer le panneau en silence.
        await fixture.Backups.DeleteAsync(fixture.Slug, info.FileName, CancellationToken.None);

        await dialog.ConfirmCommand.ExecuteAsync(null);

        dialog.FailureMessage.ShouldNotBeNull();
        fixture.Overlay.Active.ShouldNotBeNull();
    }

    [Fact]
    public async Task RequestDelete_OpensDeleteDialogNamingTheDate()
    {
        var fixture = await CreateAsync();
        await fixture.Backups.CreateAsync(fixture.Slug, progress: null, CancellationToken.None);
        await fixture.Section.RefreshCommand.ExecuteAsync(null);
        var row = fixture.Section.Backups.ShouldHaveSingleItem();

        await row.DeleteCommand.ExecuteAsync(null);

        fixture.Overlay.Shown.ShouldHaveSingleItem().ShouldBeOfType<DeleteInstanceBackupDialogViewModel>();
    }

    [Fact]
    public async Task ConfirmDelete_RemovesTheBackupAndShowsAToast()
    {
        var fixture = await CreateAsync();
        await fixture.Backups.CreateAsync(fixture.Slug, progress: null, CancellationToken.None);
        await fixture.Section.RefreshCommand.ExecuteAsync(null);
        var row = fixture.Section.Backups.ShouldHaveSingleItem();

        await row.DeleteCommand.ExecuteAsync(null);
        var dialog = (DeleteInstanceBackupDialogViewModel)fixture.Overlay.Shown[0];
        await dialog.ConfirmCommand.ExecuteAsync(null);

        fixture.Section.Backups.ShouldBeEmpty();
        fixture.Toasts.Shown.ShouldContain(t => t.Title == "Sauvegarde supprimée");
        fixture.Overlay.Active.ShouldBeNull();
    }

    [Fact]
    public async Task CancelDelete_KeepsTheBackup()
    {
        var fixture = await CreateAsync();
        await fixture.Backups.CreateAsync(fixture.Slug, progress: null, CancellationToken.None);
        await fixture.Section.RefreshCommand.ExecuteAsync(null);
        var row = fixture.Section.Backups.ShouldHaveSingleItem();

        await row.DeleteCommand.ExecuteAsync(null);
        ((DeleteInstanceBackupDialogViewModel)fixture.Overlay.Shown[0]).CancelCommand.Execute(null);

        (await fixture.Backups.ListAsync(fixture.Slug, CancellationToken.None)).ShouldHaveSingleItem();
    }
}