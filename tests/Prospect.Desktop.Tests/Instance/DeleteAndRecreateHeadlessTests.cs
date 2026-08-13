using Microsoft.Extensions.DependencyInjection;

using Prospect.Core.Common;
using Prospect.Core.Diagnostics;
using Prospect.Core.Instances;
using Prospect.Core.Launching;
using Prospect.Core.ModDb;
using Prospect.Desktop.Services;
using Prospect.Desktop.Tests.TestDoubles;
using Prospect.Desktop.ViewModels.Dialogs;

using Shouldly;

namespace Prospect.Desktop.Tests.Instance;

/// <summary>
/// « Supprimer puis recréer une instance du même nom cause des problèmes », relevé en test réel.
/// </summary>
/// <remarks>
/// La mécanique du défaut : le slug se déduit du nom et redevient libre dès que le dossier
/// disparaît, donc une instance recréée du même nom reçoit EXACTEMENT le même slug — et hérite de
/// tout ce que le launcher gardait sous cette clé. Ces tests montent le conteneur de production,
/// donc le vrai <c>DeletedInstanceStateCleaner</c>, et vérifient que chacun de ces états repart de
/// zéro.
/// </remarks>
public sealed class DeleteAndRecreateHeadlessTests
{
    private static readonly GameVersion SampleVersion = GameVersion.Parse("1.21.3");

    private static InstanceUpdateReport Report(int updates)
    {
        var mods = Enumerable.Range(0, updates)
            .Select(index => new ModUpdateResult(
                new InstalledMod
                {
                    FilePath = $"/mods/mod{index}-1.0.0.zip",
                    FileName = $"mod{index}-1.0.0.zip",
                    IsEnabled = true,
                },
                ModUpdateStatus.UpdateAvailable))
            .ToArray();

        return new InstanceUpdateReport(mods, DateTimeOffset.UnixEpoch);
    }

    [Fact]
    public async Task DeletingAnInstance_ForgetsItsUpdateReport_AndTheRecreatedOneStartsEmpty()
    {
        using var provider = TestServiceProviderFactory.Create(out _);
        var instances = provider.GetRequiredService<InstanceService>();
        var cache = provider.GetRequiredService<IModUpdateCheckCache>();

        var first = await instances.CreateAsync("Homestead", SampleVersion);
        cache.Store(first.Slug, Report(updates: 3));
        cache.TryGet(first.Slug)!.UpdateCount.ShouldBe(3);

        await instances.DeleteAsync(first.Slug);

        cache.TryGet(first.Slug).ShouldBeNull();

        var second = await instances.CreateAsync("Homestead", SampleVersion);
        second.Slug.ShouldBe(first.Slug);
        cache.TryGet(second.Slug).ShouldBeNull();
    }

    [Fact]
    public async Task DeletingAnInstance_ForgetsItsTrackedProcess_SoTheRecreatedOneIsNotAlreadyRunning()
    {
        using var provider = TestServiceProviderFactory.Create(out _);
        var instances = provider.GetRequiredService<InstanceService>();
        var tracker = provider.GetRequiredService<RunningInstanceTracker>();

        var first = await instances.CreateAsync("Homestead", SampleVersion);
        var process = new FakeRunningProcess();
        await tracker.TrackStartedAsync(first.Slug, process);
        tracker.IsRunning(first.Slug).ShouldBeTrue();

        await instances.DeleteAsync(first.Slug);

        tracker.IsRunning(first.Slug).ShouldBeFalse();
        tracker.GetStatus(first.Slug).ShouldBeNull();

        var second = await instances.CreateAsync("Homestead", SampleVersion);
        tracker.IsRunning(second.Slug).ShouldBeFalse();
        tracker.GetStatus(second.Slug).ShouldBeNull();

        // La sortie du processus de l'ancienne instance ne doit RIEN écrire sur la nouvelle.
        process.CompleteWith(0);
        await Task.Delay(50);
        var reloaded = await provider.GetRequiredService<IInstanceRepository>().LoadAsync(second.Slug);
        reloaded.Metadata.TotalPlaytimeSeconds.ShouldBe(0);
        reloaded.Metadata.LastLaunchedUtc.ShouldBeNull();
    }

    [Fact]
    public async Task DeletingAnInstance_TakesItsLaunchJournalWithIt()
    {
        using var provider = TestServiceProviderFactory.Create(out var fileSystem);
        var instances = provider.GetRequiredService<InstanceService>();
        var launcher = provider.GetRequiredService<GameLauncher>();

        var first = await instances.CreateAsync("Homestead", SampleVersion);
        var logPath = launcher.GetLogFilePath(first.Slug);
        fileSystem.AddFile(logPath, new System.IO.Abstractions.TestingHelpers.MockFileData("=== Homestead ==="));

        await instances.DeleteAsync(first.Slug);

        // Le journal vit sous logs/, DEHORS du dossier de l'instance : rien ne l'emportait avec elle.
        fileSystem.File.Exists(logPath).ShouldBeFalse();

        var second = await instances.CreateAsync("Homestead", SampleVersion);
        fileSystem.File.Exists(launcher.GetLogFilePath(second.Slug)).ShouldBeFalse();
    }

    /// <summary>
    /// La LECTURE du journal se garde en mémoire pour la session (voir
    /// <see cref="IGameLogInsightsCache"/>) : elle doit partir avec l'instance, sans quoi une
    /// instance recréée du même nom afficherait les pastilles d'erreurs de la précédente sur des
    /// mods qui, eux, viennent d'être installés.
    /// </summary>
    [Fact]
    public async Task DeletingAnInstance_ForgetsWhatItsJournalSaidAboutItsMods()
    {
        using var provider = TestServiceProviderFactory.Create(out _);
        var instances = provider.GetRequiredService<InstanceService>();
        var cache = provider.GetRequiredService<IGameLogInsightsCache>();

        var first = await instances.CreateAsync("Homestead", SampleVersion);
        cache.Store(first.Slug, new InstanceLogInsights(
            DateTimeOffset.UnixEpoch,
            HasLog: true,
            [new ModLogInsight("carryon", 2, 0, ["une erreur"])],
            []));
        cache.TryGet(first.Slug).ShouldNotBeNull();

        await instances.DeleteAsync(first.Slug);

        cache.TryGet(first.Slug).ShouldBeNull();

        var second = await instances.CreateAsync("Homestead", SampleVersion);
        cache.TryGet(second.Slug).ShouldBeNull();
    }

    /// <summary>
    /// Le dialogue de suppression reste vivant et honnête pendant l'opération : état « en cours »,
    /// et les DEUX boutons hors service, parce qu'aucune annulation n'est offerte une fois la
    /// suppression commencée.
    /// </summary>
    [Fact]
    public async Task DeleteDialog_WhileDeleting_DisablesBothButtonsAndSaysWhatItIsDoing()
    {
        using var provider = TestServiceProviderFactory.Create(out _);
        var instances = provider.GetRequiredService<InstanceService>();
        var overlay = new RecordingOverlayService();
        var record = await instances.CreateAsync("Homestead", SampleVersion);

        var dialog = new DeleteInstanceDialogViewModel(record.Slug, "Homestead", instances, overlay, new ImmediateUiDispatcher(), () => Task.CompletedTask)
        {
            IsDeleting = true,
        };

        dialog.ConfirmCommand.CanExecute(null).ShouldBeFalse();
        dialog.CancelCommand.CanExecute(null).ShouldBeFalse();
        dialog.ProgressText.ShouldNotBeNullOrWhiteSpace();

        dialog.IsDeleting = false;
        dialog.ConfirmCommand.CanExecute(null).ShouldBeTrue();
        dialog.CancelCommand.CanExecute(null).ShouldBeTrue();

        await dialog.ConfirmCommand.ExecuteAsync(null);

        overlay.Active.ShouldBeNull();
        dialog.ErrorMessage.ShouldBeNull();
        dialog.IsDeleting.ShouldBeFalse();
    }

    /// <summary>
    /// La barre du dialogue de suppression est DÉTERMINÉE : elle suit les fichiers relevés par le
    /// service. Elle n'avait qu'un état « en cours », c'est-à-dire un rond qui tourne pendant
    /// quarante secondes sur une instance à gros mondes.
    /// </summary>
    [Fact]
    public async Task DeleteDialog_WhileDeleting_CountsTheFilesItRemoves()
    {
        using var provider = TestServiceProviderFactory.Create(out var fileSystem);
        var instances = provider.GetRequiredService<InstanceService>();
        var repository = provider.GetRequiredService<IInstanceRepository>();
        var overlay = new RecordingOverlayService();
        var record = await instances.CreateAsync("Homestead", SampleVersion);

        var saves = fileSystem.Path.Combine(repository.GetDataDirectory(record.Slug), "Saves");
        for (var index = 0; index < 50; index++)
        {
            fileSystem.AddFile(
                fileSystem.Path.Combine(saves, $"monde-{index}.vcdbs"),
                new System.IO.Abstractions.TestingHelpers.MockFileData(new byte[64]));
        }

        var seen = new List<double>();
        var dialog = new DeleteInstanceDialogViewModel(record.Slug, "Homestead", instances, overlay, new ImmediateUiDispatcher(), () => Task.CompletedTask);
        dialog.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(DeleteInstanceDialogViewModel.ProgressPercent))
            {
                seen.Add(dialog.ProgressPercent);
            }
        };
        overlay.Show(dialog);

        await dialog.ConfirmCommand.ExecuteAsync(null);

        seen.ShouldNotBeEmpty();
        seen.ShouldBe(seen.Order().ToArray());
        seen[^1].ShouldBe(100d);
        dialog.ProgressText.ShouldStartWith("Suppression des fichiers");
        overlay.Active.ShouldBeNull();
    }
}