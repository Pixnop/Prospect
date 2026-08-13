using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;

using Microsoft.Extensions.DependencyInjection;

using Prospect.Desktop.ViewModels.Dialogs;
using Prospect.Desktop.ViewModels.Instance;
using Prospect.Desktop.ViewModels.Shell;
using Prospect.Desktop.ViewModels.Versions;
using Prospect.Desktop.Views.Versions;

using Shouldly;

namespace Prospect.Desktop.Tests.Versions;

/// <summary>
/// L'écran Versions s'instancie dans le shell réel (DI complet, système de fichiers en mémoire,
/// réseau factice) et réagit aux filtres ; le popover Téléchargements se branche bien sur la file
/// du DownloadManager (docs/architecture.md, exigences de test headless).
/// </summary>
public class VersionsHeadlessTests
{
    /// <summary>
    /// L'angle mort réel comblé ici, jumeau de celui de l'Accueil
    /// (<c>HomeAndWizardHeadlessTests.RealStartup_ExistingInstances_AreVisibleWithoutManualRefresh</c>) :
    /// aucune des trois entrées de l'écran Versions n'en déclenchait le chargement, et la seule façon
    /// de le remplir était de cliquer « Vérifier les nouveautés ». Tous les tests de cette classe
    /// naviguaient réellement PUIS appelaient <c>RefreshCommand</c> à la main, ce qui masquait
    /// exactement l'absence de déclencheur. Ce test-ci navigue et n'appelle rien : il rejoint
    /// l'exécution que la navigation a lancée toute seule (<c>ExecutionTask</c> n'est non nul que si
    /// quelqu'un a déjà exécuté la commande), donc si le déclencheur régresse, il n'y a rien à
    /// rejoindre et l'assertion tombe.
    /// </summary>
    [AvaloniaFact]
    public async Task RealNavigation_FromTheSidebar_PopulatesTheScreenWithoutManualRefresh()
    {
        using var provider = TestServiceProviderFactory.Create(out var fileSystem);
        provider.SeedInstalledVersion(fileSystem, "1.20.4");
        var window = provider.GetRequiredService<MainWindow>();
        var shell = provider.GetRequiredService<ShellViewModel>();
        var versions = provider.GetRequiredService<VersionsViewModel>();

        window.Show();
        shell.LibraryNavItems.First(item => item.Label == "Versions").SelectCommand.Execute(null);

        var navigationLoad = versions.RefreshCommand.ExecutionTask.ShouldNotBeNull(
            "entrer sur l'écran Versions doit déclencher son chargement, sans quoi il s'ouvre vide");
        await navigationLoad;
        window.Settle();

        window.GetVisualDescendants().OfType<VersionsView>().ShouldNotBeEmpty();
        versions.Installed.Select(row => row.VersionText).ShouldBe(["1.20.4"]);
        versions.Available.ShouldNotBeEmpty();
        window.GetVisualDescendants().OfType<TextBlock>().Any(block => block.Text == "1.20.4").ShouldBeTrue();

        window.Close();
    }

    /// <summary>
    /// La deuxième entrée de l'écran : l'action « Installer » du docteur d'instance. Elle traverse
    /// le détail d'instance et son évènement, un chemin distinct de celui de la sidebar, donc une
    /// garde distincte — c'est précisément le chemin qu'a emprunté la session de test Windows.
    /// </summary>
    [AvaloniaFact]
    public async Task InstanceDoctorInstallAction_LandsOnAPopulatedVersionsScreen()
    {
        using var provider = TestServiceProviderFactory.Create(out _);
        var slug = await provider.SeedTargetInstanceAsync(gameVersion: "1.21.3");
        var shell = provider.GetRequiredService<ShellViewModel>();
        var versions = provider.GetRequiredService<VersionsViewModel>();

        shell.ShowInstanceDetail(slug);
        var detail = shell.CurrentPage.ShouldBeOfType<InstanceDetailViewModel>();
        await detail.CheckInstanceCommand.ExecuteAsync(null);

        var dialog = shell.Overlay.Active.ShouldBeOfType<InstanceDoctorDialogViewModel>();
        var row = dialog.Groups
            .SelectMany(group => group.Rows)
            .Single(candidate => candidate.ActionLabel == "Installer" && candidate.Message.Contains("1.21.3", StringComparison.Ordinal));

        row.ActionCommand!.Execute(null);

        shell.CurrentPage.ShouldBeOfType<VersionsViewModel>();
        await versions.RefreshCommand.ExecutionTask.ShouldNotBeNull(
            "arriver sur Versions par le docteur doit charger l'écran comme n'importe quelle autre entrée");

        versions.Available.Select(row2 => row2.VersionText).ShouldContain("1.21.3");
    }

    /// <summary>
    /// Le chargement déclenché par une entrée sur la page ne doit pas pouvoir s'entrelacer avec
    /// celui que provoque une mutation : deux chargements concurrents mêleraient leurs
    /// <c>Clear()</c>/<c>Add()</c>. Ils se mettent en file, chacun voyant bien son propre scan.
    /// </summary>
    [AvaloniaFact]
    public async Task EnteringTheScreenTwiceInARow_QueuesTheLoadsInsteadOfInterleavingThem()
    {
        using var provider = TestServiceProviderFactory.Create(out var fileSystem);
        provider.SeedInstalledVersion(fileSystem, "1.20.4");
        var shell = provider.GetRequiredService<ShellViewModel>();
        var versions = provider.GetRequiredService<VersionsViewModel>();

        shell.ShowVersions();
        shell.ShowHome();
        shell.ShowVersions();
        await versions.RefreshCommand.ExecutionTask.ShouldNotBeNull();
        await versions.RefreshCommand.ExecuteAsync(null);

        versions.Installed.Select(row => row.VersionText).ShouldBe(["1.20.4"]);
        versions.IsLoading.ShouldBeFalse();
    }

    [AvaloniaFact]
    public async Task VersionsScreen_ShowsInstalledAndAvailableVersionsFromTheCatalog()
    {
        using var provider = TestServiceProviderFactory.Create(out var fileSystem);
        provider.SeedInstalledVersion(fileSystem, "1.20.4");
        var window = provider.GetRequiredService<MainWindow>();
        var shell = provider.GetRequiredService<ShellViewModel>();
        var versions = provider.GetRequiredService<VersionsViewModel>();

        window.Show();
        shell.LibraryNavItems.First(item => item.Label == "Versions").SelectCommand.Execute(null);
        await versions.RefreshCommand.ExecuteAsync(null);
        window.Settle();

        window.GetVisualDescendants().OfType<VersionsView>().ShouldNotBeEmpty();
        versions.Installed.Select(row => row.VersionText).ShouldBe(["1.20.4"]);
        versions.Available.Select(row => row.VersionText).ShouldBe(["1.22.0-rc.1", "1.21.3"]);
        versions.SubtitleText.ShouldStartWith("1 installée");

        window.Close();
    }

    [AvaloniaFact]
    public async Task VersionsScreen_HidingUnstable_RemovesTheReleaseCandidateFromTheTree()
    {
        using var provider = TestServiceProviderFactory.Create(out _);
        var window = provider.GetRequiredService<MainWindow>();
        var shell = provider.GetRequiredService<ShellViewModel>();
        var versions = provider.GetRequiredService<VersionsViewModel>();

        window.Show();
        shell.LibraryNavItems.First(item => item.Label == "Versions").SelectCommand.Execute(null);
        await versions.RefreshCommand.ExecuteAsync(null);
        window.Settle();

        window.GetVisualDescendants().OfType<TextBlock>()
            .Any(block => block.Text == "1.22.0-rc.1").ShouldBeTrue();

        versions.ShowUnstable = false;
        window.Settle();

        window.GetVisualDescendants().OfType<TextBlock>()
            .Any(block => block.Text == "1.22.0-rc.1").ShouldBeFalse();

        window.Close();
    }

    [AvaloniaFact]
    public async Task VersionsScreen_OfflineCatalog_StillShowsWhatIsInstalledAndWarns()
    {
        using var provider = TestServiceProviderFactory.Create(out var fileSystem, out var catalogHandler);
        provider.SeedInstalledVersion(fileSystem, "1.20.4");
        catalogHandler.IsOnline = false;
        var window = provider.GetRequiredService<MainWindow>();
        var versions = provider.GetRequiredService<VersionsViewModel>();

        window.Show();
        await versions.RefreshCommand.ExecuteAsync(null);
        window.Settle();

        versions.Installed.Count.ShouldBe(1);
        versions.Available.ShouldBeEmpty();
        versions.CatalogWarning.ShouldNotBeNull();

        window.Close();
    }

    /// <summary>
    /// La barre de progression de la rangée en cours d'installation occupe sa colonne au lieu de se
    /// réduire à la largeur du libellé qui vit sous elle. Relevé en test réel : « la barre de
    /// progression est minuscule », une soixantaine de points au lieu des 420 du design.
    /// </summary>
    [AvaloniaTheory]
    [InlineData(1280d)]
    [InlineData(900d)]
    public async Task VersionRow_WhileInstalling_StretchesItsProgressBarAcrossTheColumn(double windowWidth)
    {
        using var provider = TestServiceProviderFactory.Create(out _);
        var window = provider.GetRequiredService<MainWindow>();
        var shell = provider.GetRequiredService<ShellViewModel>();
        var versions = provider.GetRequiredService<VersionsViewModel>();
        window.Width = windowWidth;

        window.Show();
        shell.LibraryNavItems.First(item => item.Label == "Versions").SelectCommand.Execute(null);
        await versions.RefreshCommand.ExecuteAsync(null);
        window.Settle();

        var row = versions.Available.First();
        row.IsWorking = true;
        row.PhaseText = "Installation";
        window.Settle();

        var view = window.GetVisualDescendants().OfType<VersionsView>().Single();
        var bar = view.GetVisualDescendants()
            .OfType<ProgressBar>()
            .Single(candidate => ReferenceEquals(candidate.DataContext, row));

        // Le libellé « Installation » fait une soixantaine de points : la barre doit être
        // franchement plus large que lui, et plafonnée par le MaxWidth du design.
        bar.Bounds.Width.ShouldBeGreaterThan(200d);
        bar.Bounds.Width.ShouldBeLessThanOrEqualTo(420d);

        window.Close();
    }

    [AvaloniaFact]
    public void DownloadsPopover_WithAnEmptyQueue_KeepsItsEmptyState()
    {
        using var provider = TestServiceProviderFactory.Create(out _);
        var window = provider.GetRequiredService<MainWindow>();
        var shell = provider.GetRequiredService<ShellViewModel>();

        window.Show();
        shell.ToggleDownloadsPopoverCommand.Execute(null);
        window.Settle();

        shell.IsDownloadsPopoverOpen.ShouldBeTrue();
        shell.Downloads.HasDownloads.ShouldBeFalse();
        window.GetVisualDescendants().OfType<TextBlock>()
            .Any(block => block.Text == "Aucun téléchargement").ShouldBeTrue();

        window.Close();
    }
}