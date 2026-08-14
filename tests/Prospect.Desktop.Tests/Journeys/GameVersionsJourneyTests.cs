using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;

using Microsoft.Extensions.DependencyInjection;

using Prospect.Desktop.Services;
using Prospect.Desktop.ViewModels.Dialogs;
using Prospect.Desktop.ViewModels.Shell;
using Prospect.Desktop.ViewModels.Toasts;
using Prospect.Desktop.ViewModels.Versions;
using Prospect.Desktop.Views.Versions;

using Shouldly;

namespace Prospect.Desktop.Tests.Journeys;

/// <summary>
/// PARCOURS 7 — gérer les versions du jeu. Filtrer, montrer et cacher les versions de test,
/// installer une version en suivant sa progression, puis en désinstaller une dont une instance
/// dépend : l'avertissement doit NOMMER cette instance, pas servir une mise en garde vague.
/// </summary>
public sealed class GameVersionsJourneyTests
{
    [AvaloniaFact]
    public async Task Journey_FilterInstallThenUninstallAVersionAnInstanceDependsOn()
    {
        using var provider = TestServiceProviderFactory.CreateForJourney(out var fileSystem, out _, out _);
        provider.SeedInstalledVersion(fileSystem, "1.20.4");
        await provider.SeedTargetInstanceAsync("Homestead", "1.20.4");
        await provider.SeedTargetInstanceAsync("Bac à sable", "1.20.4");

        var window = provider.GetRequiredService<MainWindow>();
        var shell = provider.GetRequiredService<ShellViewModel>();
        var toasts = provider.GetRequiredService<IToastService>();
        window.Show();

        // ── Étape 1 : l'écran, atteint par la barre latérale, se remplit tout seul ───────
        shell.LibraryNavItems.Single(item => ReferenceEquals(item.Page, shell.Versions)).SelectCommand.Execute(null);
        var versions = shell.CurrentPage.ShouldBeOfType<VersionsViewModel>();
        await versions.RefreshCommand.ExecuteAsync(null);
        window.Pump();

        window.GetVisualDescendants().OfType<VersionsView>().ShouldNotBeEmpty();
        versions.Installed.Select(row => row.VersionText).ShouldContain("1.20.4");
        versions.Available.Select(row => row.VersionText).ShouldContain("1.21.3");
        versions.SubtitleText.ShouldNotBeNullOrWhiteSpace("l'écran doit dire combien de versions et quelle place elles prennent");

        // ── Étape 2 : les versions de test se montrent et se cachent ────────────────────
        versions.ShowUnstable.ShouldBeTrue();
        versions.Available.Select(row => row.VersionText).ShouldContain("1.22.0-rc.1");

        versions.ShowUnstable = false;
        window.Pump();
        versions.Available.Select(row => row.VersionText).ShouldNotContain("1.22.0-rc.1");

        versions.ShowUnstable = true;
        window.Pump();

        // ── Étape 3 : le filtre par état ────────────────────────────────────────────────
        versions.FilterIndex = 1; // installées seules
        window.Pump();
        versions.Available.ShouldBeEmpty();
        versions.Installed.ShouldNotBeEmpty();

        versions.FilterIndex = 2; // disponibles seules
        window.Pump();
        versions.Installed.ShouldBeEmpty();
        versions.Available.ShouldNotBeEmpty();

        versions.FilterIndex = 0;
        window.Pump();

        // ── Étape 4 : la recherche ──────────────────────────────────────────────────────
        versions.SearchText = "1.21";
        window.Pump();
        versions.Available.Select(row => row.VersionText).ShouldAllBe(text => text.StartsWith("1.21", StringComparison.Ordinal));

        versions.ClearSearchCommand.Execute(null);
        window.Pump();
        versions.SearchText.ShouldBeEmpty();

        // ── Étape 5 : installer, en suivant l'avancement ────────────────────────────────
        var row = versions.Available.Single(candidate => candidate.VersionText == "1.21.3");
        row.CanInstall.ShouldBeTrue();
        row.SizeText.ShouldNotBeNullOrWhiteSpace("une version à télécharger doit annoncer son poids AVANT le clic");

        await row.InstallCommand.ExecuteAsync(null);
        window.Pump();

        row.ErrorMessage.ShouldBeNull();
        row.IsWorking.ShouldBeFalse("l'installation terminée doit rendre la ligne au repos");
        toasts.ShouldHaveToast(ToastTone.Success);

        await versions.RefreshCommand.ExecuteAsync(null);
        window.Pump();
        versions.Installed.Select(candidate => candidate.VersionText).ShouldContain("1.21.3");

        // ── Étape 6 : désinstaller une version dont DEUX instances dépendent ────────────
        var installed = versions.Installed.Single(candidate => candidate.VersionText == "1.20.4");
        await installed.UninstallCommand.ExecuteAsync(null);
        window.Pump();

        var dialog = shell.Overlay.Active.ShouldBeOfType<UninstallVersionDialogViewModel>();
        dialog.HasDependents.ShouldBeTrue("deux instances utilisent cette version : le dire est le minimum");
        dialog.DependentsMessage.ShouldNotBeNullOrWhiteSpace();
        dialog.DependentsMessage!.ShouldContain("Homestead", Case.Insensitive, "l'avertissement doit NOMMER les instances concernées");
        dialog.DependentsMessage!.ShouldContain("Bac à sable", Case.Insensitive);
        window.ShowsText("Homestead").ShouldBeTrue("et cet avertissement doit être lisible à l'écran");

        await dialog.ConfirmCommand.ExecuteAsync(null);
        window.Pump();

        shell.Overlay.Active.ShouldBeNull();
        toasts.LastToast().ShouldNotBeNull().Title.ShouldNotBeNullOrWhiteSpace("une désinstallation doit le dire");
        await versions.RefreshCommand.ExecuteAsync(null);
        window.Pump();
        versions.Installed.Select(candidate => candidate.VersionText).ShouldNotContain("1.20.4");

        window.Close();
    }
}