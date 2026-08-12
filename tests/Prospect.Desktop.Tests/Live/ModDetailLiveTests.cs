using System.Globalization;

using Avalonia.VisualTree;

using Microsoft.Extensions.DependencyInjection;

using Prospect.Core.Common;
using Prospect.Core.Instances;
using Prospect.Desktop.Tests.Support;
using Prospect.Desktop.Tests.TestDoubles;
using Prospect.Desktop.ViewModels.Mods;
using Prospect.Desktop.ViewModels.Shell;
using Prospect.Desktop.Views.Mods;
using Prospect.Tests.Live;

using Shouldly;

using Xunit.Abstractions;

namespace Prospect.Desktop.Tests.Live;

/// <summary>
/// (c) Le chemin du clic sur une carte, joué avec de VRAIES fiches ModDB : mapping complet par le
/// client de production, puis construction de la fiche en dialog et rendu headless dans la fenêtre
/// réelle, avec une instance cible sélectionnée pour que les badges de compatibilité soient
/// exercés eux aussi.
/// </summary>
/// <remarks>
/// Les fiches sont choisies pour leur diversité de forme, pas pour leur popularité : une
/// bibliothèque à 85 releases (dont certaines déclarent 23 versions de jeu compatibles), le mod le
/// plus téléchargé du dépôt (30 Ko de description et dix captures), un mod sans logo, un outil
/// externe dont toutes les releases ont un <c>modidstr</c> nul, et une fiche à 234 releases. Voir
/// docs/research/moddb-api.md pour ce que chacune de ces formes a de particulier.
/// </remarks>
[Trait("Category", "Live")]
public sealed class ModDetailLiveTests(ITestOutputHelper output)
{
    /// <summary>Fiches réelles couvertes, par identifiant numérique et par ce qu'elles ont de particulier.</summary>
    public static TheoryData<int, string> DiverseRealMods => new()
    {
        { 1783, "configlib, 85 releases, jusqu'à 23 versions de jeu taguées par release" },
        { 792, "BetterRuins, mod le plus téléchargé, description de 30 Ko et 10 captures" },
        { 1065, "Common Lib, aucun logo sur la fiche" },
        { 1403, "ModsUpdater, outil externe dont toutes les releases ont un modidstr nul" },
        { 3805, "Overhaul lib, 234 releases" },
    };

    [LiveAvaloniaTheory]
    [MemberData(nameof(DiverseRealMods))]
    public async Task RealModDetail_MapsThenBuildsAndRendersTheDialog(int modId, string shape)
    {
        output.WriteLine($"Fiche réelle {modId} : {shape}");

        using var live = new LiveModDb();
        var detail = await live.Client.GetModAsync(modId.ToString(CultureInfo.InvariantCulture), CancellationToken.None);

        output.WriteLine(
            $"  mappé : « {detail.Name} », {detail.Releases.Count} releases retenues, "
            + $"description HTML {detail.DescriptionHtml.Length} caractères, logo {(detail.LogoUrl is null ? "absent" : "présent")}.");

        using var provider = TestServiceProviderFactory.Create(out _);
        await provider.SeedTargetInstanceAsync();
        var window = provider.GetRequiredService<MainWindow>();
        var shell = provider.GetRequiredService<ShellViewModel>();
        window.Show();
        shell.ShowModBrowser();
        await shell.ModBrowser.InitializeCommand.ExecuteAsync(null);
        window.Settle();

        // Une instance cible est bien sélectionnée : c'est elle qui allume les badges de
        // compatibilité de la fiche, donc le chemin que le propriétaire voit réellement.
        shell.ModBrowser.SelectedInstance!.GameVersion.ShouldNotBeNull();

        // Exactement ce que fait ModBrowserViewModel.OpenAsync après son appel réseau : construire
        // la fiche puis la poser dans le panneau modal partagé.
        var dialog = new ModDetailDialogViewModel(
            detail,
            shell.ModBrowser.SelectedInstance,
            new FakeExternalUrlOpener(),
            shell.Overlay,
            () => Task.CompletedTask);

        shell.Overlay.Show(dialog);
        window.Settle();

        window.GetVisualDescendants().OfType<ModDetailDialogView>().ShouldNotBeEmpty();
        dialog.Name.ShouldNotBeNullOrWhiteSpace();
        window.ShouldHoldLayoutInvariantsAtEverySize($"fiche ModDB réelle {modId}");

        window.Close();
    }
}