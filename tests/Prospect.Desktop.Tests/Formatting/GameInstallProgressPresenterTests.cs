using Prospect.Core.GameVersions;
using Prospect.Core.Storage;
using Prospect.Desktop.Formatting;

using Shouldly;

namespace Prospect.Desktop.Tests.Formatting;

/// <summary>
/// Ce que la ligne sous la barre d'installation raconte, et quand la notice de la boîte Inno
/// apparaît. Les deux règles sont écrites une seule fois ici, parce que trois écrans les consomment
/// (Versions, wizard, import de modpack) et qu'une règle recopiée trois fois diverge.
/// </summary>
public sealed class GameInstallProgressPresenterTests
{
    [Fact]
    public void AMeasuredInstall_ReadsAsAnExactCount()
        => GameInstallProgressPresenter.DetailText(GameInstallProgress.ForInstalling(0.42d))
            .ShouldBe("extraction · 42 %");

    /// <summary>
    /// L'installeur Windows ne mesure rien, il est estimé. Le tilde est la seule chose qui sépare
    /// « je compte » de « j'estime », et il n'y a aucune raison de le taire.
    /// </summary>
    [Fact]
    public void AnEstimatedInstall_SaysItIsAnEstimate()
        => GameInstallProgressPresenter.DetailText(GameInstallProgress.ForInstalling(0.42d, isEstimated: true))
            .ShouldBe("installation · ~42 %");

    [Fact]
    public void AnInstallThatCannotMeasureItself_SaysNothingRatherThanGuessing()
        => GameInstallProgressPresenter.DetailText(GameInstallProgress.ForPhase(GameInstallPhase.Installing))
            .ShouldBeEmpty();

    [Theory]
    [InlineData(AppOperatingSystem.Windows, true)]
    [InlineData(AppOperatingSystem.Linux, false)]
    [InlineData(AppOperatingSystem.MacOs, false)]
    public void TheInstallerPromptNotice_IsAWindowsOnlyAffair(AppOperatingSystem operatingSystem, bool expected)
        => GameInstallProgressPresenter
            .ShowsInstallerPromptNotice(GameInstallProgress.ForVendorInstaller(), operatingSystem)
            .ShouldBe(expected);

    /// <summary>
    /// Et seulement quand l'installeur du jeu tourne vraiment. La voie normale sous Windows est
    /// l'extraction, qui n'exécute rien : annoncer là une boîte qui ne viendra pas apprendrait à
    /// ignorer un avertissement dont la réponse par défaut désinstalle le jeu.
    /// </summary>
    [Fact]
    public void TheInstallerPromptNotice_StaysAwayFromAnExtraction()
        => GameInstallProgressPresenter
            .ShowsInstallerPromptNotice(GameInstallProgress.ForInstalling(0.42d), AppOperatingSystem.Windows)
            .ShouldBeFalse();

    /// <summary>
    /// Et seulement pendant la phase où la boîte peut s'ouvrir : la prévenir pendant le
    /// téléchargement serait du bruit posé cinq minutes trop tôt.
    /// </summary>
    [Theory]
    [InlineData(GameInstallPhase.Downloading)]
    [InlineData(GameInstallPhase.Verifying)]
    [InlineData(GameInstallPhase.Completed)]
    public void TheInstallerPromptNotice_StaysAwayFromEveryOtherPhase(GameInstallPhase phase)
        => GameInstallProgressPresenter
            .ShowsInstallerPromptNotice(
                GameInstallProgress.ForVendorInstaller() with { Phase = phase },
                AppOperatingSystem.Windows)
            .ShouldBeFalse();
}