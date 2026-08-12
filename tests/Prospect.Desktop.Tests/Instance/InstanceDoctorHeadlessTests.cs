using Avalonia.Headless.XUnit;

using Microsoft.Extensions.DependencyInjection;

using Prospect.Core.Diagnostics;
using Prospect.Core.ModDb;
using Prospect.Desktop.Tests.TestDoubles;
using Prospect.Desktop.ViewModels.Dialogs;
using Prospect.Desktop.ViewModels.Instance;
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
}