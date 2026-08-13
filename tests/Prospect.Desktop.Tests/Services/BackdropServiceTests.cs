using System.ComponentModel;
using System.IO.Abstractions.TestingHelpers;

using Avalonia.Headless.XUnit;

using Prospect.Core.Settings;
using Prospect.Core.Settings.Migrations;
using Prospect.Core.Storage;
using Prospect.Desktop.Services;
using Prospect.Desktop.Tests.TestDoubles;

using Shouldly;

namespace Prospect.Desktop.Tests.Services;

/// <summary>
/// <see cref="BackdropService"/> : source par défaut, changement notifié à chaud, repli sur le fond
/// par défaut pour une clé inconnue, et l'invariant de facture partagé avec
/// <c>ThemeService</c> — construire le service ne doit rien décoder ni rien appliquer.
/// </summary>
public sealed class BackdropServiceTests
{
    private static readonly AppPaths Paths = new(new SystemAppEnvironment(), "/data/prospect");

    private static SettingsService CreateSettings(MockFileSystem? fileSystem = null)
    {
        var system = fileSystem ?? new MockFileSystem();

        return new SettingsService(system, Paths, new JsonFileStore(system), new SettingsMigrationPipeline([]), new FakeUiCulture());
    }

    [Fact]
    public void Constructor_NullSettings_ThrowsArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() => new BackdropService(null!));
    }

    [Fact]
    public void Constructor_DecodesNothing_AndNeedsNoGraphicalApplication()
    {
        // Un [Fact] ordinaire, PAS un [AvaloniaFact] : c'est l'assertion elle-même. Construire ce
        // service traverse le graphe DI de tout test qui résout MainWindow ou ShellViewModel, et
        // ne doit donc coûter aucun décodage d'image (voir la remarque de la classe). S'il
        // décodait à la construction, ce test lèverait faute de plateforme de rendu.
        var service = new BackdropService(CreateSettings());

        service.Key.ShouldBe(BackdropCatalog.Default);
    }

    [AvaloniaFact]
    public void Source_ByDefault_IsTheDefaultBackdropAsset()
    {
        var service = new BackdropService(CreateSettings());

        var source = service.Source;

        source.ShouldNotBeNull();
        // Les fonds sont tous pré-composés au même gabarit (voir MainWindow.axaml) : une source qui
        // n'aurait pas ces dimensions ne serait pas passée par le pipeline.
        source.Size.Width.ShouldBe(1920);
        source.Size.Height.ShouldBe(1080);
    }

    [AvaloniaFact]
    public void Source_ReadTwiceWithoutChange_IsTheSameInstance()
    {
        // Le fond n'est décodé qu'une fois : chaque passe de mise en page relit Image.Source.
        var service = new BackdropService(CreateSettings());

        service.Source.ShouldBeSameAs(service.Source);
    }

    [AvaloniaFact]
    public async Task SettingsChanged_AfterConstruction_SwitchesTheSourceAndNotifies()
    {
        // Le cycle complet côté bascule à chaud, exactement comme ThemeService : Réglages appelle
        // SettingsService.UpdateAsync, le service (déjà construit et abonné) réagit tout seul.
        var settings = CreateSettings();
        var service = new BackdropService(settings);
        var before = service.Source;
        var notified = new List<string>();
        ((INotifyPropertyChanged)service).PropertyChanged += (_, args) => notified.Add(args.PropertyName ?? string.Empty);

        await settings.UpdateAsync(current => current with { Backdrop = "village-lane" });

        service.Key.ShouldBe("village-lane");
        notified.ShouldContain(nameof(BackdropService.Key));
        notified.ShouldContain(nameof(BackdropService.Source));
        service.Source.ShouldNotBeSameAs(before);
    }

    [AvaloniaFact]
    public async Task SettingsChanged_ToTheSameBackdrop_NotifiesNothing()
    {
        // Sans cette garde, chaque écriture de réglage (thème, parallélisme, premier lancement vu)
        // redécoderait 1920x1080 points pour rien.
        var settings = CreateSettings();
        var service = new BackdropService(settings);
        var before = service.Source;
        var notified = new List<string>();
        ((INotifyPropertyChanged)service).PropertyChanged += (_, args) => notified.Add(args.PropertyName ?? string.Empty);

        await settings.UpdateAsync(current => current with { Theme = ThemePreference.Light });

        notified.ShouldBeEmpty();
        service.Source.ShouldBeSameAs(before);
    }

    [AvaloniaFact]
    public async Task UnknownBackdropInTheSettings_FallsBackToTheDefaultSource()
    {
        var fileSystem = new MockFileSystem();
        fileSystem.AddFile(Paths.SettingsFilePath, new MockFileData("""
        { "schemaVersion": 1, "theme": "Dark", "language": "fr", "backdrop": "un-fond-qui-n-existe-pas" }
        """));
        var settings = CreateSettings(fileSystem);
        await settings.LoadAsync();

        var service = new BackdropService(settings);

        service.Key.ShouldBe(BackdropCatalog.Default);
        service.Source.ShouldNotBeNull();
    }

    [AvaloniaFact]
    public void EveryCatalogueKey_ResolvesToAnEmbeddedAsset()
    {
        // La garde qui relie les deux moitiés : une clé ajoutée au catalogue du Core sans son
        // fichier dans Assets/Backdrops/ ne se verrait qu'à l'exécution, sur l'écran Réglages.
        var service = new BackdropService(CreateSettings());

        foreach (var key in BackdropCatalog.Keys)
        {
            var thumbnail = service.Thumbnail(key);

            thumbnail.ShouldNotBeNull();
            thumbnail.Size.Width.ShouldBe(BackdropService.ThumbnailDecodeWidth);
            // 16/9 conservé par le décodeur : les vignettes de la grille ne se déformeront pas.
            thumbnail.Size.Height.ShouldBe(BackdropService.ThumbnailDecodeWidth * 9d / 16d, tolerance: 1d);
        }
    }

    [AvaloniaFact]
    public void Thumbnail_AskedTwice_IsMemoised()
    {
        var service = new BackdropService(CreateSettings());

        service.Thumbnail("lake-sail").ShouldBeSameAs(service.Thumbnail("lake-sail"));
    }

    [AvaloniaFact]
    public void Thumbnail_UnknownKey_FallsBackToTheDefaultRatherThanThrowing()
    {
        var service = new BackdropService(CreateSettings());

        service.Thumbnail("nope").ShouldBeSameAs(service.Thumbnail(BackdropCatalog.Default));
    }

    [Fact]
    public void AssetUriFor_MapsAKeyToItsEmbeddedFile_AndRepairsUnknownKeys()
    {
        BackdropService.AssetUriFor("misty-yard").OriginalString
            .ShouldBe($"{BackdropService.AssetDirectory}/misty-yard.jpg");
        BackdropService.AssetUriFor("nope").OriginalString
            .ShouldBe($"{BackdropService.AssetDirectory}/{BackdropCatalog.Default}.jpg");
        BackdropService.AssetUriFor(null).OriginalString
            .ShouldBe($"{BackdropService.AssetDirectory}/{BackdropCatalog.Default}.jpg");
    }
}