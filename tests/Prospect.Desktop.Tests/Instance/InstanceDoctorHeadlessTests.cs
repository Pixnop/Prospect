using Avalonia.Headless.XUnit;
using Avalonia.Threading;

using Microsoft.Extensions.DependencyInjection;

using Prospect.Core.Diagnostics;
using Prospect.Core.ModDb;
using Prospect.Desktop.Tests.TestDoubles;
using Prospect.Desktop.ViewModels.Dialogs;
using Prospect.Desktop.ViewModels.Instance;
using Prospect.Desktop.ViewModels.Mods;
using Prospect.Desktop.ViewModels.Shell;
using Prospect.Desktop.ViewModels.Versions;

using Shouldly;

namespace Prospect.Desktop.Tests.Instance;

/// <summary>
/// « Vérifier l'instance » câblé de bout en bout sur le shell réel (docs/architecture.md, exigence
/// de test headless) : le dialogue s'ouvre avec le bon contenu et ses actions naviguent réellement.
/// Le point central de cette classe est <see cref="CheckInstance_NetworkUnreachable_StillProducesAReport"/>,
/// la preuve « hors ligne » demandée par la mission — un gestionnaire HTTP factice qui échoue
/// systématiquement s'il est appelé, câblé dans le MÊME conteneur que le vrai <c>IModDbClient</c>
/// utilisé par l'onglet Mods juste à côté.
/// </summary>
public sealed class InstanceDoctorHeadlessTests
{
    [AvaloniaFact]
    public async Task CheckInstance_NetworkUnreachable_StillProducesAReport()
    {
        // Le gestionnaire factice fait échouer TOUT appel HTTP (catalogue de versions ET ModDB, voir
        // FakeCatalogHandler.IsOnline) : si InstanceDoctor tentait ne serait-ce qu'une requête, cette
        // vérification lèverait au lieu de rendre un rapport.
        using var provider = TestServiceProviderFactory.Create(out _, out var catalogHandler);
        catalogHandler.IsOnline = false;
        var slug = await provider.SeedTargetInstanceAsync();
        var shell = provider.GetRequiredService<ShellViewModel>();

        shell.ShowInstanceDetail(slug);
        var detail = shell.CurrentPage.ShouldBeOfType<InstanceDetailViewModel>();

        await detail.CheckInstanceCommand.ExecuteAsync(null);

        shell.Overlay.Active.ShouldBeOfType<InstanceDoctorDialogViewModel>();
    }

    [AvaloniaFact]
    public async Task CheckInstance_GameVersionNotInstalled_ErrorRowNavigatesToVersionsOnInstall()
    {
        using var provider = TestServiceProviderFactory.Create(out _);
        var slug = await provider.SeedTargetInstanceAsync(gameVersion: "1.21.3");
        var shell = provider.GetRequiredService<ShellViewModel>();
        shell.ShowInstanceDetail(slug);
        var detail = shell.CurrentPage.ShouldBeOfType<InstanceDetailViewModel>();

        await detail.CheckInstanceCommand.ExecuteAsync(null);
        var dialog = shell.Overlay.Active.ShouldBeOfType<InstanceDoctorDialogViewModel>();

        dialog.IsAllClear.ShouldBeFalse();
        var errorGroup = dialog.Groups.Single(group => group.Rows.Any(row => row.Severity == InstanceDoctorSeverity.Error));
        var row = errorGroup.Rows.Single(candidate => candidate.Message.Contains("1.21.3"));
        row.ActionLabel.ShouldBe("Installer");

        row.ActionCommand!.Execute(null);

        shell.Overlay.Active.ShouldBeNull();
        shell.CurrentPage.ShouldBeOfType<VersionsViewModel>();
    }

    [AvaloniaFact]
    public async Task CheckInstance_UnidentifiedMod_OpenModsActionSwitchesTheSelectedTab()
    {
        using var provider = TestServiceProviderFactory.Create(out var fileSystem);
        provider.SeedInstalledVersion(fileSystem, "1.21.3");
        var slug = await provider.SeedTargetInstanceAsync(gameVersion: "1.21.3");
        var mods = provider.GetRequiredService<IInstalledModRepository>();
        fileSystem.AddFile(
            fileSystem.Path.Combine(mods.GetModsDirectory(slug), "mystere.zip"),
            new System.IO.Abstractions.TestingHelpers.MockFileData(ModDbDoubles.BuildArchive(null)));

        var shell = provider.GetRequiredService<ShellViewModel>();
        shell.ShowInstanceDetail(slug);
        var detail = shell.CurrentPage.ShouldBeOfType<InstanceDetailViewModel>();
        detail.SelectTabCommand.Execute(InstanceDetailTab.Options);
        detail.SelectedTab.ShouldBe(InstanceDetailTab.Options);

        await detail.CheckInstanceCommand.ExecuteAsync(null);
        var dialog = shell.Overlay.Active.ShouldBeOfType<InstanceDoctorDialogViewModel>();
        var row = dialog.Groups.SelectMany(group => group.Rows).Single(candidate => candidate.Message.Contains("mystere.zip"));
        row.ActionLabel.ShouldBe("Voir les mods");

        row.ActionCommand!.Execute(null);

        shell.Overlay.Active.ShouldBeNull();
        detail.SelectedTab.ShouldBe(InstanceDetailTab.Mods);
    }

    /// <summary>
    /// Le cas réel remonté : Carry On installé, carryonlib absent, le docteur ne proposait que
    /// « Voir les mods ». L'action installe désormais la dépendance nommée, et le plan qui s'ouvre
    /// est bien celui de CE mod, dans CETTE instance.
    ///
    /// La séquence importe autant que le résultat : le diagnostic est produit hors ligne, le
    /// dialogue se referme, et le réseau n'entre en jeu qu'ensuite, dans la machinerie
    /// d'installation partagée avec le navigateur de mods.
    /// </summary>
    [AvaloniaFact]
    public async Task CheckInstance_MissingDependency_InstallActionOpensThePlanForThatMod()
    {
        using var provider = TestServiceProviderFactory.Create(out var fileSystem, out var catalogHandler);
        provider.SeedInstalledVersion(fileSystem, "1.21.3");
        var slug = await provider.SeedTargetInstanceAsync(gameVersion: "1.21.3");
        var mods = provider.GetRequiredService<IInstalledModRepository>();
        ModDbDoubles.SeedMod(
            fileSystem,
            mods.GetModsDirectory(slug),
            "carryon-1.14.3.zip",
            ModDbDoubles.ModInfo("carryon", "Carry On", "1.14.3", dependency: "carryonlib"));

        var shell = provider.GetRequiredService<ShellViewModel>();
        shell.ShowInstanceDetail(slug);
        var detail = shell.CurrentPage.ShouldBeOfType<InstanceDetailViewModel>();

        // Le diagnostic lui-même se fait sans réseau : le gestionnaire est coupé pendant qu'il court.
        catalogHandler.IsOnline = false;
        await detail.CheckInstanceCommand.ExecuteAsync(null);
        var dialog = shell.Overlay.Active.ShouldBeOfType<InstanceDoctorDialogViewModel>();

        var row = dialog.Groups.SelectMany(group => group.Rows).Single(candidate => candidate.Message.Contains("carryonlib"));
        row.ActionLabel.ShouldBe("Installer « carryonlib »…");

        // Le réseau ne revient qu'AU CLIC, c'est-à-dire dans la machinerie d'installation.
        catalogHandler.IsOnline = true;
        row.ActionCommand!.Execute(null);
        await WaitForOverlayAsync<ModInstallPlanDialogViewModel>(shell);

        var plan = shell.Overlay.Active.ShouldBeOfType<ModInstallPlanDialogViewModel>();
        plan.Plan.Primary.ModIdString.ShouldBe("carryonlib");
        plan.FileNameText.ShouldBe("carryonlib-1.2.0.zip");
        detail.SelectedTab.ShouldBe(InstanceDetailTab.Mods);
    }

    /// <summary>
    /// ModDB injoignable au moment du clic : message honnête, aucun plan ouvert, et surtout rien qui
    /// tombe. Le rapport, lui, a déjà été rendu — il n'a jamais eu besoin du réseau.
    /// </summary>
    [AvaloniaFact]
    public async Task CheckInstance_InstallActionWhileOffline_ReportsItWithoutCrashing()
    {
        using var provider = TestServiceProviderFactory.Create(out var fileSystem, out var catalogHandler);
        provider.SeedInstalledVersion(fileSystem, "1.21.3");
        var slug = await provider.SeedTargetInstanceAsync(gameVersion: "1.21.3");
        var mods = provider.GetRequiredService<IInstalledModRepository>();
        ModDbDoubles.SeedMod(
            fileSystem,
            mods.GetModsDirectory(slug),
            "carryon-1.14.3.zip",
            ModDbDoubles.ModInfo("carryon", "Carry On", "1.14.3", dependency: "carryonlib"));

        var shell = provider.GetRequiredService<ShellViewModel>();
        shell.ShowInstanceDetail(slug);
        var detail = shell.CurrentPage.ShouldBeOfType<InstanceDetailViewModel>();

        catalogHandler.IsOnline = false;
        await detail.CheckInstanceCommand.ExecuteAsync(null);
        var dialog = shell.Overlay.Active.ShouldBeOfType<InstanceDoctorDialogViewModel>();
        var row = dialog.Groups.SelectMany(group => group.Rows).Single(candidate => candidate.Message.Contains("carryonlib"));

        row.ActionCommand!.Execute(null);
        await WaitForAsync(() => shell.Toasts.Toasts.Count > 0);

        shell.Overlay.Active.ShouldBeNull();
        shell.Toasts.Toasts.ShouldNotBeEmpty();
    }

    private static Task WaitForOverlayAsync<T>(ShellViewModel shell)
        => WaitForAsync(() => shell.Overlay.Active is T);

    // Généreux exprès : ce conteneur est celui de PRODUCTION, donc le client ModDB y porte la vraie
    // politique de réessai (trois tentatives espacées de 1 s puis 2 s). Un échec réseau met donc
    // quelques secondes à devenir un verdict, et c'est le comportement voulu.
    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 1000 && !condition(); attempt++)
        {
            await Task.Delay(10);
            Dispatcher.UIThread.RunJobs();
        }

        condition().ShouldBeTrue("La condition attendue n'est jamais survenue.");
    }
}