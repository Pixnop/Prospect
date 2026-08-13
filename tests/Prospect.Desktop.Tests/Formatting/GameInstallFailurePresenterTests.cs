using Prospect.Core.Settings;
using Prospect.Core.Storage;
using Prospect.Desktop.Formatting;
using Prospect.Desktop.Resources;

using Shouldly;

namespace Prospect.Desktop.Tests.Formatting;

/// <summary>
/// Le récit de l'échec « installation terminée, aucun exécutable » selon l'OS. Le fait rapporté est
/// le même partout, la cause plausible non : sous Windows un installeur a pu écrire ailleurs, sous
/// Linux et macOS il n'y a qu'une archive extraite.
/// </summary>
public sealed class GameInstallFailurePresenterTests
{
    private const string TargetDirectory = "/home/jean/.local/share/prospect/versions/1.22.6";

    [Fact]
    public void OnWindows_TheStoryIsTheInstaller()
    {
        var message = GameInstallFailurePresenter.IncompleteInstallMessage(AppOperatingSystem.Windows, TargetDirectory);

        message.ShouldBe(UiText.Versions.InstallLandedElsewhere(TargetDirectory));
        message.ShouldContain("installeur");
        message.ShouldContain(TargetDirectory);
    }

    /// <summary>
    /// La correction du terrain : parler d'une « installation existante de Vintage Story » à
    /// quelqu'un qui vient d'extraire un tar.gz n'a aucun sens et ne l'oriente vers rien.
    /// </summary>
    [Theory]
    [InlineData(AppOperatingSystem.Linux)]
    [InlineData(AppOperatingSystem.MacOs)]
    public void OnLinuxAndMacOs_TheStoryIsTheArchive(AppOperatingSystem operatingSystem)
    {
        var message = GameInstallFailurePresenter.IncompleteInstallMessage(operatingSystem, TargetDirectory);

        message.ShouldBe(UiText.Versions.ArchiveMissingExecutable(TargetDirectory));
        message.ShouldContain("archive");
        message.ShouldNotContain("installation existante");
        message.ShouldContain(TargetDirectory);
    }

    /// <summary>
    /// Les deux récits existent aussi en anglais, et ils y disent bien deux choses différentes. La
    /// table anglaise est interrogée directement, sans toucher la façade statique : voir
    /// <c>EnglishUiTextTests</c>.
    /// </summary>
    [Fact]
    public void BothStoriesExistInEnglishToo()
    {
        var english = UiText.TableFor(ProspectSettings.English).Versions;

        english.InstallLandedElsewhere(TargetDirectory).ShouldContain("installer");
        english.ArchiveMissingExecutable(TargetDirectory).ShouldContain("archive");
        english.ArchiveMissingExecutable(TargetDirectory).ShouldNotContain("existing Vintage Story");
    }
}