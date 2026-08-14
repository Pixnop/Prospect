using System.IO.Abstractions.TestingHelpers;

using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;

using Microsoft.Extensions.DependencyInjection;

using Prospect.Core.Instances;
using Prospect.Desktop.Services;
using Prospect.Desktop.ViewModels.Dialogs;
using Prospect.Desktop.ViewModels.Home;
using Prospect.Desktop.ViewModels.Instance;
using Prospect.Desktop.ViewModels.Logs;
using Prospect.Desktop.ViewModels.Shell;
using Prospect.Desktop.ViewModels.Toasts;
using Prospect.Desktop.ViewModels.Wizard;
using Prospect.Desktop.Views.Logs;

using Shouldly;

namespace Prospect.Desktop.Tests.Journeys;

/// <summary>
/// PARCOURS 6 — entretenir sa bibliothèque. Renommer, dupliquer, sauvegarder à la main, RESTAURER
/// cette sauvegarde, supprimer l'instance, en recréer une du même nom, puis lire les journaux et
/// les exporter.
/// </summary>
/// <remarks>
/// Le point dur du parcours est la restauration, que rien n'exerçait de bout en bout : elle
/// remplace le dossier de données, donc elle doit rendre visible ce qu'elle a fait ET laisser
/// l'écran cohérent après coup. Le second point dur est la recréation d'un nom tout juste libéré,
/// qui est le cas où un slug fantôme se ferait sentir.
/// </remarks>
public sealed class MaintenanceJourneyTests
{
    [AvaloniaFact]
    public async Task Journey_RenameDuplicateBackupRestoreDeleteRecreateAndExportLogs()
    {
        using var provider = TestServiceProviderFactory.CreateForJourney(out var fileSystem, out _, out var seams);
        provider.SeedInstalledVersion(fileSystem, "1.20.4");
        var slug = await provider.SeedTargetInstanceAsync("Bac à sable", "1.20.4");

        // Un monde dans le dossier de données : c'est ce que la sauvegarde emporte et ce que la
        // restauration doit ramener.
        var repository = provider.GetRequiredService<IInstanceRepository>();
        var savesDirectory = fileSystem.Path.Combine(repository.GetDataDirectory(slug), "Saves");
        fileSystem.AddFile(fileSystem.Path.Combine(savesDirectory, "monde.vcdbs"), new MockFileData("état initial"));

        var window = provider.GetRequiredService<MainWindow>();
        var shell = provider.GetRequiredService<ShellViewModel>();
        var home = provider.GetRequiredService<HomeViewModel>();
        var toasts = provider.GetRequiredService<IToastService>();
        window.Show();
        await home.RefreshCommand.ExecuteAsync(null);

        shell.ShowInstanceDetail(slug);
        var detail = shell.CurrentPage.ShouldBeOfType<InstanceDetailViewModel>();
        await detail.InitializeCommand.ExecuteAsync(null);
        window.Pump();

        // ── Étape 1 : renommer ──────────────────────────────────────────────────────────
        detail.RenameCommand.Execute(null);
        window.Pump();

        var rename = shell.Overlay.Active.ShouldBeOfType<RenameDialogViewModel>();
        rename.Name = string.Empty;
        rename.ConfirmCommand.CanExecute(null).ShouldBeFalse("un nom vide ne doit pas pouvoir être validé");
        rename.Error.ShouldNotBeNullOrWhiteSpace("et l'écran doit dire pourquoi");

        rename.Name = "Homestead";
        await rename.ConfirmCommand.ExecuteAsync(null);
        window.Pump();

        shell.Overlay.Active.ShouldBeNull();
        detail.Name.ShouldBe("Homestead", "l'en-tête doit refléter le nouveau nom sans rechargement");

        // ── Étape 2 : dupliquer ─────────────────────────────────────────────────────────
        detail.DuplicateCommand.Execute(null);
        window.Pump();

        var duplicate = shell.Overlay.Active.ShouldBeOfType<DuplicateDialogViewModel>();
        duplicate.Name.ShouldNotBeNullOrWhiteSpace("le nom de la copie doit être proposé, pas à inventer");
        duplicate.Name = "Homestead (test)";
        await duplicate.ConfirmCommand.ExecuteAsync(null);
        window.Pump();

        shell.Overlay.Active.ShouldBeNull();
        toasts.ShouldHaveToast(ToastTone.Success);
        await home.RefreshCommand.ExecuteAsync(null);
        home.Instances.Select(instance => instance.Name).ShouldContain("Homestead (test)");

        // ── Étape 3 : sauvegarder à la main ─────────────────────────────────────────────
        var backups = detail.OptionsTab.Backups;
        backups.HasBackups.ShouldBeFalse();
        await backups.CreateNowCommand.ExecuteAsync(null);
        window.Pump();

        backups.HasBackups.ShouldBeTrue();
        backups.Backups.ShouldHaveSingleItem().SizeText.ShouldNotBeNullOrWhiteSpace("une sauvegarde doit annoncer sa taille");
        toasts.ShouldHaveToast(ToastTone.Success);

        // ── Étape 4 : restaurer cette sauvegarde ────────────────────────────────────────
        var worldPath = fileSystem.Path.Combine(savesDirectory, "monde.vcdbs");
        fileSystem.File.WriteAllText(worldPath, "état abîmé");

        await backups.Backups[0].RestoreCommand.ExecuteAsync(null);
        window.Pump();

        var restore = shell.Overlay.Active.ShouldBeOfType<RestoreInstanceBackupDialogViewModel>();
        restore.Message.ShouldContain("Homestead", Case.Insensitive, "la confirmation doit nommer ce qu'elle remplace");
        await restore.ConfirmCommand.ExecuteAsync(null);
        window.Pump();

        shell.Overlay.Active.ShouldBeNull();
        toasts.ShouldHaveToast(ToastTone.Success);
        fileSystem.File.ReadAllText(worldPath).ShouldBe("état initial", "la restauration doit vraiment ramener le contenu sauvegardé");

        // ── Étape 5 : supprimer l'instance ──────────────────────────────────────────────
        detail.DeleteCommand.Execute(null);
        window.Pump();

        var delete = shell.Overlay.Active.ShouldBeOfType<DeleteInstanceDialogViewModel>();
        delete.Title.ShouldContain("Homestead", Case.Insensitive);
        delete.Message.ShouldNotBeNullOrWhiteSpace("une suppression définitive doit dire ce qu'elle emporte");
        await delete.ConfirmCommand.ExecuteAsync(null);
        window.Pump();

        shell.Overlay.Active.ShouldBeNull();
        shell.CurrentPage.ShouldBeOfType<HomeViewModel>("supprimer l'instance affichée doit ramener à l'Accueil");
        await home.RefreshCommand.ExecuteAsync(null);
        home.Instances.Select(instance => instance.Name).ShouldNotContain("Homestead");

        // ── Étape 6 : recréer une instance saine du MÊME nom ────────────────────────────
        home.NewInstanceCommand.Execute(null);
        window.Pump();
        var wizard = shell.Overlay.Active.ShouldBeOfType<WizardViewModel>();
        await wizard.LoadVersionsCommand.ExecuteAsync(null);
        wizard.Name = "Homestead";
        wizard.NextCommand.Execute(null);
        wizard.VersionChoices.First(choice => choice.VersionText == "1.20.4").SelectCommand.Execute(null);
        wizard.NextCommand.Execute(null);
        wizard.NextCommand.Execute(null);
        await wizard.CreateCommand.ExecuteAsync(null);
        window.Pump();

        wizard.CreateError.ShouldBeNull("le nom vient d'être libéré : rien ne doit s'y opposer");
        var recreated = home.Instances.Single(instance => instance.Name == "Homestead");

        shell.ShowInstanceDetail(recreated.Slug);
        var fresh = shell.CurrentPage.ShouldBeOfType<InstanceDetailViewModel>();
        await fresh.InitializeCommand.ExecuteAsync(null);
        window.Pump();

        fresh.ModsTab.HasMods.ShouldBeFalse("l'instance recréée part vierge, sans rien hériter de l'ancienne");
        fresh.OptionsTab.Backups.HasBackups.ShouldBeFalse();
        fresh.HasEverLaunched.ShouldBeFalse();

        // ── Étape 7 : la page Journaux, avec du contenu ─────────────────────────────────
        shell.LogsNavItem.SelectCommand.Execute(null);
        window.Pump();

        var logs = shell.CurrentPage.ShouldBeOfType<LogsViewModel>();
        window.GetVisualDescendants().OfType<LogsView>().ShouldNotBeEmpty();
        logs.HasLines.ShouldBeTrue("après tout ce qui précède, le journal ne peut pas être vide");
        logs.SubtitleText.ShouldNotBeNullOrWhiteSpace();
        logs.CanExport.ShouldBeTrue();

        // ── Étape 8 : exporter les journaux ─────────────────────────────────────────────
        seams.FilePicker.NextSavePath = fileSystem.Path.Combine("/export", "journaux.zip");
        await logs.ExportCommand.ExecuteAsync(null);
        window.Pump();

        seams.FilePicker.SaveRequests.ShouldHaveSingleItem().Extension.ShouldBe("zip");
        logs.ErrorMessage.ShouldBeNull();
        fileSystem.File.Exists(seams.FilePicker.NextSavePath).ShouldBeTrue();
        toasts.ShouldHaveToast(ToastTone.Success).Description
            .ShouldNotBeNullOrWhiteSpace("l'export doit dire combien de fichiers il a emportés");

        window.Close();
    }
}