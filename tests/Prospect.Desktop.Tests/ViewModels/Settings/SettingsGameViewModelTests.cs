using Prospect.Core.Storage;
using Prospect.Desktop.Tests.TestDoubles;
using Prospect.Desktop.ViewModels.Settings;

using Shouldly;

namespace Prospect.Desktop.Tests.ViewModels.Settings;

/// <summary>
/// <see cref="SettingsGameViewModel"/> : affiche la racine réelle des données de Prospect et
/// ouvre ce dossier via l'ouvreur externe (la relocalisation reste hors périmètre, voir la
/// docstring de la classe).
/// </summary>
public sealed class SettingsGameViewModelTests
{
    private static readonly AppPaths Paths = new(new SystemAppEnvironment(), "/data/prospect");

    [Fact]
    public void Constructor_NullArguments_ThrowArgumentNullException()
    {
        var urlOpener = new FakeExternalUrlOpener();

        Should.Throw<ArgumentNullException>(() => new SettingsGameViewModel(null!, urlOpener));
        Should.Throw<ArgumentNullException>(() => new SettingsGameViewModel(Paths, null!));
    }

    [Fact]
    public void Constructor_ExposesTheAppPathsRootDirectory()
    {
        var viewModel = new SettingsGameViewModel(Paths, new FakeExternalUrlOpener());

        viewModel.DataPath.ShouldBe(Paths.RootDirectory);
    }

    [Fact]
    public async Task OpenDataFolderAsync_OpensTheRootDirectory()
    {
        var urlOpener = new FakeExternalUrlOpener();
        var viewModel = new SettingsGameViewModel(Paths, urlOpener);

        await viewModel.OpenDataFolderCommand.ExecuteAsync(null);

        urlOpener.OpenedFolders.ShouldHaveSingleItem();
        urlOpener.OpenedFolders[0].ShouldBe(Paths.RootDirectory);
    }
}