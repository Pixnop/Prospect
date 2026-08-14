using System.IO.Abstractions.TestingHelpers;

using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;

using Microsoft.Extensions.DependencyInjection;

using Prospect.Core.GameVersions;
using Prospect.Core.ModDb;
using Prospect.Core.Storage;
using Prospect.Desktop.Tests.TestDoubles;
using Prospect.Desktop.ViewModels.Dialogs;
using Prospect.Desktop.ViewModels.Instance;
using Prospect.Desktop.ViewModels.Mods;
using Prospect.Desktop.ViewModels.Shell;
using Prospect.Desktop.ViewModels.Versions;
using Prospect.Desktop.Views.Dialogs;

using Shouldly;

namespace Prospect.Desktop.Tests.Journeys;

/// <summary>
/// PARCOURS 4 — santé d'une instance cassée. Une instance dont la version du jeu est à moitié
/// installée et dont un mod réclame une dépendance absente : le diagnostic doit dire les deux, et
/// CHAQUE action de ligne doit être menée jusqu'au bout (installer la version depuis l'écran
/// Versions, installer la dépendance depuis le plan), jusqu'à ce qu'un second diagnostic soit
/// enfin vert.
/// </summary>
/// <remarks>
/// L'exigence de ce parcours est qu'aucune action ne s'arrête à mi-chemin. Les tests existants du
/// docteur vérifiaient que le clic NAVIGUE ; celui-ci vérifie que l'utilisateur arrive à réparer.
/// Il rend aussi visible ce qu'aucun test ne regardait : le panneau « tout va bien » n'était
/// jamais RENDU, seulement calculé.
/// </remarks>
public sealed class InstanceHealthJourneyTests
{
    [AvaloniaFact]
    public async Task Journey_BrokenInstance_EveryDoctorActionLeadsAllTheWayToAGreenReport()
    {
        using var provider = TestServiceProviderFactory.CreateForJourney(out var fileSystem, out var catalogHandler, out _);
        catalogHandler.ModDb.CatalogJson = FakeModDbHandler.CatalogWith(FakeModDbHandler.CarryOnCatalogEntry);
        catalogHandler.ModDb.CompatibleModIds = [1783, 792, 890, 4687];

        var slug = await provider.SeedTargetInstanceAsync("Homestead", "1.21.3");

        // Le disque du système de fichiers factice n'annonce aucun espace libre, ce qui ferait
        // parler la cinquième vérification d'un problème que la machine n'a pas. On lui donne un
        // volume crédible : c'est un artefact du double, pas un état à diagnostiquer.
        var paths = provider.GetRequiredService<AppPaths>();
        fileSystem.AddDrive(
            fileSystem.Path.GetPathRoot(paths.RootDirectory)!,
            new MockDriveData { AvailableFreeSpace = 200L * 1024 * 1024 * 1024, TotalSize = 500L * 1024 * 1024 * 1024 });

        // Défaut n° 1 : la version est à moitié installée (dossier présent, complétude jamais
        // écrite), exactement ce que laisse une installation interrompue.
        fileSystem.AddFile(
            fileSystem.Path.Combine(paths.VersionsDirectory, "1.21.3", "Vintagestory"),
            new MockFileData("moitié"));

        var window = provider.GetRequiredService<MainWindow>();
        var shell = provider.GetRequiredService<ShellViewModel>();
        window.Show();

        // Défaut n° 2 : le joueur installe Carry On mais DÉCOCHE la dépendance proposée. C'est le
        // seul chemin par lequel une dépendance manque vraiment à un mod que Prospect a posé
        // lui-même, et donc le seul qui laisse une instance réparable de bout en bout.
        shell.ShowModBrowser(slug);
        var browser = shell.CurrentPage.ShouldBeOfType<ModBrowserViewModel>();
        await browser.InitializeCommand.ExecuteAsync(null);
        window.Pump();

        var carryOn = browser.Results.Single(entry => entry.Name == "Carry On");
        await carryOn.InstallCommand.ExecuteAsync(null);
        window.Pump();

        var firstPlan = shell.Overlay.Active.ShouldBeOfType<ModInstallPlanDialogViewModel>();
        firstPlan.Dependencies.ShouldHaveSingleItem().IsSelected = false;
        await firstPlan.ConfirmCommand.ExecuteAsync(null);
        window.Pump();

        shell.ShowInstanceDetail(slug);
        var detail = shell.CurrentPage.ShouldBeOfType<InstanceDetailViewModel>();
        await detail.InitializeCommand.ExecuteAsync(null);
        window.Pump();

        // ── Étape 1 : le diagnostic dit les deux problèmes ───────────────────────────────
        await detail.CheckInstanceCommand.ExecuteAsync(null);
        window.Pump();

        var doctor = shell.Overlay.Active.ShouldBeOfType<InstanceDoctorDialogViewModel>();
        window.GetVisualDescendants().OfType<InstanceDoctorDialogView>().ShouldNotBeEmpty();
        doctor.IsAllClear.ShouldBeFalse();

        var rows = doctor.Groups.SelectMany(group => group.Rows).ToArray();
        rows.ShouldContain(row => row.IsError, "une version à moitié installée est une erreur, pas un détail");
        rows.Where(row => row.HasAction).ShouldNotBeEmpty("un constat sans action laisse l'utilisateur sans issue");
        foreach (var row in rows.Where(row => row.HasAction))
        {
            row.ActionLabel.ShouldNotBeNullOrWhiteSpace("chaque action doit dire ce qu'elle fait");
            row.Message.ShouldNotBeNullOrWhiteSpace();
        }

        // ── Étape 2 : « Installer la version » va jusqu'à l'écran Versions ───────────────
        var versionRow = rows.First(row => row.IsError && row.HasAction);
        versionRow.ActionCommand!.Execute(null);
        window.Pump();

        shell.Overlay.Active.ShouldBeNull("le diagnostic se referme quand on part réparer");
        var versions = shell.CurrentPage.ShouldBeOfType<VersionsViewModel>();
        await versions.RefreshCommand.ExecuteAsync(null);
        window.Pump();

        versions.HasBrokenInstalls.ShouldBeTrue("l'installation interrompue doit être nommée sur l'écran où on la répare");
        var available = versions.Available.Single(row => row.VersionText == "1.21.3");
        await available.InstallCommand.ExecuteAsync(null);
        window.Pump();

        await versions.RefreshCommand.ExecuteAsync(null);
        window.Pump();
        versions.Installed.Select(row => row.VersionText).ShouldContain("1.21.3");

        // ── Étape 3 : retour à l'instance, le diagnostic ne parle plus que du mod ────────
        shell.ShowInstanceDetail(slug);
        detail = shell.CurrentPage.ShouldBeOfType<InstanceDetailViewModel>();
        await detail.InitializeCommand.ExecuteAsync(null);
        await detail.CheckInstanceCommand.ExecuteAsync(null);
        window.Pump();

        doctor = shell.Overlay.Active.ShouldBeOfType<InstanceDoctorDialogViewModel>();
        rows = doctor.Groups.SelectMany(group => group.Rows).ToArray();
        rows.ShouldNotContain(row => row.Message.Contains("1.21.3", StringComparison.Ordinal) && row.IsError);

        // ── Étape 4 : « Installer la dépendance » ouvre le plan, et le plan s'applique ───
        var dependencyRow = rows.Single(row => row.HasAction && row.ActionLabel.Contains("carryonlib", StringComparison.OrdinalIgnoreCase));
        dependencyRow.ActionCommand!.Execute(null);
        window.Pump();

        // La commande est asynchrone : on rejoint la préparation du plan avant d'affirmer.
        await window.WaitUntilAsync(
            () => shell.Overlay.Active is ModInstallPlanDialogViewModel,
            "le plan d'installation de la dépendance doit s'ouvrir");
        var plan = shell.Overlay.Active.ShouldBeOfType<ModInstallPlanDialogViewModel>();
        plan.Title.ShouldContain("CarryOnLib", Case.Insensitive);

        await plan.ConfirmCommand.ExecuteAsync(null);
        window.Pump();

        shell.Overlay.Active.ShouldBeNull();
        detail.ModsTab.Mods.Select(row => row.Name).ShouldContain("CarryOnLib");

        // ── Étape 5 : le diagnostic est enfin vert, ET IL LE DIT À L'ÉCRAN ──────────────
        await detail.CheckInstanceCommand.ExecuteAsync(null);
        window.Pump();

        doctor = shell.Overlay.Active.ShouldBeOfType<InstanceDoctorDialogViewModel>();
        doctor.IsAllClear.ShouldBeTrue(
            "tout est réparé : le rapport doit le dire. Restant : "
            + string.Join(" | ", doctor.Groups.SelectMany(group => group.Rows).Select(row => row.Message)));
        doctor.Groups.ShouldBeEmpty();

        // Le panneau « tout va bien » doit être RENDU. Sans cette assertion, un panneau qui ne se
        // construit pas passait inaperçu : aucun test n'affichait jamais ce dialogue dans une
        // fenêtre.
        window.GetVisualDescendants().OfType<InstanceDoctorDialogView>().ShouldNotBeEmpty();
        window.ShowsText(Prospect.Desktop.Resources.UiText.Instance.Doctor.AllClearTitle)
            .ShouldBeTrue("l'état sain doit se lire, pas seulement se calculer");

        window.Close();
    }

    /// <summary>
    /// La cinquième ligne du rapport, celle qui ne peut apparaître que sur un mod déposé à la main :
    /// son message demande une vérification de mises à jour, donc son bouton doit LA lancer.
    /// </summary>
    /// <remarks>
    /// Défaut relevé par le parcours ci-dessus et corrigé ici : la ligne portait « Voir les mods »,
    /// qui renvoyait vers une liste incapable d'en dire plus que le rapport qu'on venait de lire —
    /// exactement le faux pas déjà corrigé sur les dépendances manquantes.
    /// </remarks>
    [AvaloniaFact]
    public async Task Journey_CompatibilityUnknown_TheRowRunsTheCheckItAsksFor()
    {
        using var provider = TestServiceProviderFactory.CreateForJourney(out var fileSystem, out _, out _);
        provider.SeedInstalledVersion(fileSystem, "1.21.3");
        var slug = await provider.SeedTargetInstanceAsync("Homestead", "1.21.3");

        var mods = provider.GetRequiredService<IInstalledModRepository>();
        ModDbDoubles.SeedMod(
            fileSystem,
            mods.GetModsDirectory(slug),
            "configlib-1.11.1.zip",
            ModDbDoubles.ModInfo("configlib", "Config lib", "1.11.1"));

        var window = provider.GetRequiredService<MainWindow>();
        var shell = provider.GetRequiredService<ShellViewModel>();
        window.Show();

        shell.ShowInstanceDetail(slug);
        var detail = shell.CurrentPage.ShouldBeOfType<InstanceDetailViewModel>();
        await detail.InitializeCommand.ExecuteAsync(null);
        await detail.CheckInstanceCommand.ExecuteAsync(null);
        window.Pump();

        var doctor = shell.Overlay.Active.ShouldBeOfType<InstanceDoctorDialogViewModel>();
        var compatibility = doctor.Groups
            .SelectMany(group => group.Rows)
            .Single(row => row.HasAction && !row.IsError);

        compatibility.Message.ShouldContain("mises à jour", Case.Insensitive);
        compatibility.ActionLabel.ShouldBe(Prospect.Desktop.Resources.UiText.Instance.Doctor.CheckUpdatesAction);

        compatibility.ActionCommand!.Execute(null);
        await window.WaitUntilAsync(() => detail.ModsTab.HasCheckVerdict, "la vérification lancée par le rapport doit rendre un verdict");

        shell.Overlay.Active.ShouldBeNull();
        detail.SelectedTab.ShouldBe(InstanceDetailTab.Mods);
        detail.ModsTab.HasCheckVerdict.ShouldBeTrue("l'action doit produire un verdict lisible, pas seulement changer d'onglet");

        window.Close();
    }
}