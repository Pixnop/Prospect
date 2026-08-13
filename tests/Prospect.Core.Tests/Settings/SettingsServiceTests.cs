using System.IO.Abstractions.TestingHelpers;

using Prospect.Core.Common;
using Prospect.Core.Settings;
using Prospect.Core.Settings.Migrations;
using Prospect.Core.Storage;
using Prospect.Core.Tests.Common;

using Shouldly;

namespace Prospect.Core.Tests.Settings;

/// <summary>
/// <see cref="SettingsService"/> : défauts avant tout chargement, lecture au démarrage, round-trip
/// atomique, garde de migration (schéma futur refusé, schéma ancien migré et repersisté), et
/// évènement de changement pour les consommateurs (le thème notamment, voir
/// <c>Prospect.Desktop.Services.ThemeService</c>).
/// </summary>
public class SettingsServiceTests
{
    private static readonly AppPaths Paths = new(new SystemAppEnvironment(), "/data/prospect");

    // La culture est épinglée sur le français par défaut, indépendamment de la machine qui exécute
    // la suite : sans cela, chaque test « fichier absent » lirait la langue de l'OS (la CI tourne
    // en en-US) et les assertions sur les valeurs par défaut deviendraient dépendantes du poste.
    // Les tests qui portent SUR la détection passent explicitement une autre culture.
    private static SettingsService Create(
        MockFileSystem fileSystem,
        IEnumerable<ISettingsMigration>? migrations = null,
        IUiCulture? uiCulture = null)
        => new(
            fileSystem,
            Paths,
            new JsonFileStore(fileSystem),
            new SettingsMigrationPipeline(migrations ?? []),
            uiCulture ?? FakeUiCulture.French);

    [Fact]
    public void Constructor_NullArguments_ThrowArgumentNullException()
    {
        var fileSystem = new MockFileSystem();
        var store = new JsonFileStore(fileSystem);
        var pipeline = new SettingsMigrationPipeline([]);
        var culture = FakeUiCulture.French;

        Should.Throw<ArgumentNullException>(() => new SettingsService(null!, Paths, store, pipeline, culture));
        Should.Throw<ArgumentNullException>(() => new SettingsService(fileSystem, null!, store, pipeline, culture));
        Should.Throw<ArgumentNullException>(() => new SettingsService(fileSystem, Paths, null!, pipeline, culture));
        Should.Throw<ArgumentNullException>(() => new SettingsService(fileSystem, Paths, store, null!, culture));
        Should.Throw<ArgumentNullException>(() => new SettingsService(fileSystem, Paths, store, pipeline, null!));
    }

    [Fact]
    public void Current_BeforeAnyLoad_HasDefaultValues()
    {
        // Un ViewModel construit avant que LoadAsync n'ait fini (ou un test qui ne charge jamais)
        // doit toujours lire un réglage exploitable.
        var service = Create(new MockFileSystem());

        service.Current.ShouldBe(ProspectSettings.CreateDefault());
    }

    [Fact]
    public async Task LoadAsync_FileDoesNotExist_KeepsDefaultsAndDoesNotRaiseChanged()
    {
        var service = Create(new MockFileSystem());
        var changedRaised = false;
        service.Changed += (_, _) => changedRaised = true;

        await service.LoadAsync();

        service.Current.ShouldBe(ProspectSettings.CreateDefault());
        changedRaised.ShouldBeFalse();
    }

    [Fact]
    public async Task LoadAsync_ThenUpdateAsync_ThenFreshServiceLoadAsync_RoundTripsEveryField()
    {
        // Round-trip atomique de bout en bout : un SECOND service (sur le même fichier factice)
        // doit relire exactement ce que le premier a persisté, thème et parallélisme compris.
        var fileSystem = new MockFileSystem();
        var writer = Create(fileSystem);
        await writer.LoadAsync();

        await writer.UpdateAsync(current => current with
        {
            Theme = ThemePreference.Light,
            Downloads = current.Downloads with { MaxParallelDownloads = 4 },
        });

        var reader = Create(fileSystem);
        await reader.LoadAsync();

        reader.Current.Theme.ShouldBe(ThemePreference.Light);
        reader.Current.Downloads.MaxParallelDownloads.ShouldBe(4);
        reader.Current.SchemaVersion.ShouldBe(ProspectSettings.CurrentSchemaVersion);
    }

    [Fact]
    public async Task LoadAsync_ThenUpdateAsync_ThenFreshServiceLoadAsync_RoundTripsHasSeenFirstRun()
    {
        var fileSystem = new MockFileSystem();
        var writer = Create(fileSystem);
        await writer.LoadAsync();

        await writer.UpdateAsync(current => current with { HasSeenFirstRun = true });

        var reader = Create(fileSystem);
        await reader.LoadAsync();

        reader.Current.HasSeenFirstRun.ShouldBeTrue();
    }

    [Fact]
    public async Task LoadAsync_WritesToTheDocumentedSettingsFilePath()
    {
        var fileSystem = new MockFileSystem();
        var service = Create(fileSystem);

        await service.UpdateAsync(current => current with { Theme = ThemePreference.System });

        fileSystem.File.Exists(Paths.SettingsFilePath).ShouldBeTrue();
    }

    [Fact]
    public async Task UpdateAsync_PersistsAtomicallyWithoutLeavingATempFile()
    {
        var fileSystem = new MockFileSystem();
        var service = Create(fileSystem);

        await service.UpdateAsync(current => current with { Theme = ThemePreference.Light });

        fileSystem.File.Exists(Paths.SettingsFilePath + ".tmp").ShouldBeFalse();
    }

    [Fact]
    public async Task UpdateAsync_RaisesChangedWithTheNewValue()
    {
        var service = Create(new MockFileSystem());
        ProspectSettings? observed = null;
        service.Changed += (_, settings) => observed = settings;

        await service.UpdateAsync(current => current with { Theme = ThemePreference.Light });

        observed.ShouldNotBeNull();
        observed.Theme.ShouldBe(ThemePreference.Light);
        service.Current.Theme.ShouldBe(ThemePreference.Light);
    }

    [Fact]
    public async Task UpdateAsync_NullMutator_ThrowsArgumentNullException()
    {
        var service = Create(new MockFileSystem());

        await Should.ThrowAsync<ArgumentNullException>(() => service.UpdateAsync(null!));
    }

    [Fact]
    public async Task UpdateAsync_OutOfRangeDownloadValue_IsClampedBeforePersisting()
    {
        var service = Create(new MockFileSystem());

        await service.UpdateAsync(current => current with { Downloads = current.Downloads with { MaxParallelDownloads = 999 } });

        service.Current.Downloads.MaxParallelDownloads.ShouldBe(DownloadPreferences.MaxParallelDownloadsCeiling);
    }

    [Fact]
    public async Task LoadAsync_HandEditedFileWithOutOfRangeDownloads_IsClampedOnRead()
    {
        var fileSystem = new MockFileSystem();
        fileSystem.AddFile(Paths.SettingsFilePath, new MockFileData("""
        { "schemaVersion": 1, "theme": "Dark", "language": "fr", "downloads": { "maxParallelDownloads": 500 } }
        """));
        var service = Create(fileSystem);

        await service.LoadAsync();

        service.Current.Downloads.MaxParallelDownloads.ShouldBe(DownloadPreferences.MaxParallelDownloadsCeiling);
    }

    [Fact]
    public async Task LoadAsync_SchemaV1FileWithoutHasSeenFirstRunField_DefaultsToFalse()
    {
        // Un prospect.json écrit par une version de Prospect antérieure à ce champ (même schéma v1,
        // pas de migration : voir la docstring de ProspectSettings.HasSeenFirstRun) ne doit pas
        // planter, et retombe sur false (bool : c'est à la fois le défaut CLR et le défaut voulu).
        var fileSystem = new MockFileSystem();
        fileSystem.AddFile(Paths.SettingsFilePath, new MockFileData("""
        { "schemaVersion": 1, "theme": "Dark", "language": "fr" }
        """));
        var service = Create(fileSystem);

        await service.LoadAsync();

        service.Current.HasSeenFirstRun.ShouldBeFalse();
    }

    [Theory]
    [InlineData("fr-FR", ProspectSettings.French)]
    [InlineData("fr", ProspectSettings.French)]
    [InlineData("fr-CA", ProspectSettings.French)]
    [InlineData("en-US", ProspectSettings.English)]
    [InlineData("de-DE", ProspectSettings.English)]
    [InlineData("", ProspectSettings.English)]
    public async Task LoadAsync_FileDoesNotExist_TakesTheDefaultLanguageFromTheSystemCulture(string cultureName, string expectedLanguage)
    {
        // Premier lancement : rien sur disque, donc la seule information disponible sur la langue
        // attendue est la culture d'interface du système.
        var service = Create(new MockFileSystem(), uiCulture: new FakeUiCulture(cultureName));

        await service.LoadAsync();

        service.Current.Language.ShouldBe(expectedLanguage);
    }

    [Fact]
    public async Task LoadAsync_FileDoesNotExist_WritesNothingToDisk()
    {
        // Une déduction n'est pas une décision : tant que l'utilisateur n'a rien réglé, aucun
        // prospect.json n'apparaît (voir la remarque de LoadAsync).
        var fileSystem = new MockFileSystem();
        var service = Create(fileSystem, uiCulture: new FakeUiCulture("en-US"));

        await service.LoadAsync();

        fileSystem.File.Exists(Paths.SettingsFilePath).ShouldBeFalse();
    }

    [Fact]
    public async Task LoadAsync_ExistingFile_IsNeverOverriddenByCultureDetection()
    {
        // Le point qui compte pour un utilisateur français sur une machine anglaise (ou l'inverse) :
        // un choix déjà persisté gagne toujours contre la culture de l'OS.
        var fileSystem = new MockFileSystem();
        fileSystem.AddFile(Paths.SettingsFilePath, new MockFileData("""
        { "schemaVersion": 1, "theme": "Dark", "language": "fr" }
        """));
        var service = Create(fileSystem, uiCulture: new FakeUiCulture("en-US"));

        await service.LoadAsync();

        service.Current.Language.ShouldBe(ProspectSettings.French);
    }

    [Fact]
    public async Task LoadAsync_ExistingFileWithoutLanguageField_FallsBackToFrenchNotToTheCulture()
    {
        // Fichier écrit à la main, champ language absent : le repli documenté de Normalized()
        // s'applique, pas la détection — ce fichier EXISTE, donc il décide.
        var fileSystem = new MockFileSystem();
        fileSystem.AddFile(Paths.SettingsFilePath, new MockFileData("""
        { "schemaVersion": 1, "theme": "Dark" }
        """));
        var service = Create(fileSystem, uiCulture: new FakeUiCulture("en-US"));

        await service.LoadAsync();

        service.Current.Language.ShouldBe(ProspectSettings.French);
    }

    [Fact]
    public async Task LoadAsync_ThenUpdateAsync_ThenFreshServiceLoadAsync_RoundTripsEnglish()
    {
        var fileSystem = new MockFileSystem();
        var writer = Create(fileSystem);
        await writer.LoadAsync();

        await writer.UpdateAsync(current => current with { Language = ProspectSettings.English });

        var reader = Create(fileSystem);
        await reader.LoadAsync();

        reader.Current.Language.ShouldBe(ProspectSettings.English);
    }

    [Fact]
    public async Task LoadAsync_UnknownLanguageInTheFile_FallsBackToFrenchWithoutThrowing()
    {
        var fileSystem = new MockFileSystem();
        fileSystem.AddFile(Paths.SettingsFilePath, new MockFileData("""
        { "schemaVersion": 1, "theme": "Dark", "language": "kl" }
        """));
        var service = Create(fileSystem);

        await service.LoadAsync();

        service.Current.Language.ShouldBe(ProspectSettings.French);
    }

    [Fact]
    public async Task LoadAsync_CorruptedJson_ThrowsCorruptedFileExceptionWithPath()
    {
        var fileSystem = new MockFileSystem();
        fileSystem.AddFile(Paths.SettingsFilePath, new MockFileData("{ ceci n'est pas du JSON"));
        var service = Create(fileSystem);

        var exception = await Should.ThrowAsync<CorruptedFileException>(() => service.LoadAsync());

        exception.Path.ShouldBe(Paths.SettingsFilePath);
    }

    [Fact]
    public async Task LoadAsync_SchemaVersionNewerThanSupported_ThrowsSchemaVersionUnsupported()
    {
        var fileSystem = new MockFileSystem();
        fileSystem.AddFile(Paths.SettingsFilePath, new MockFileData("""{ "schemaVersion": 99 }"""));
        var service = Create(fileSystem);

        var exception = await Should.ThrowAsync<SettingsSchemaVersionUnsupportedException>(() => service.LoadAsync());

        exception.FoundSchemaVersion.ShouldBe(99);
        exception.CurrentSchemaVersion.ShouldBe(ProspectSettings.CurrentSchemaVersion);
    }

    [Fact]
    public async Task LoadAsync_OlderSchemaWithRegisteredMigration_MigratesAndPersistsTheUpgradedDocument()
    {
        var fileSystem = new MockFileSystem();
        fileSystem.AddFile(Paths.SettingsFilePath, new MockFileData("""{ "schemaVersion": 0, "theme": "Light" }"""));
        var migration = new FakeZeroToOneSettingsMigration();
        var service = Create(fileSystem, [migration]);

        await service.LoadAsync();

        service.Current.SchemaVersion.ShouldBe(1);
        // Le document sur disque a bien été réécrit au schéma courant, pas seulement la valeur en mémoire.
        var persisted = fileSystem.File.ReadAllText(Paths.SettingsFilePath);
        persisted.ShouldContain("\"schemaVersion\": 1");
    }

    [Fact]
    public async Task LoadAsync_OlderSchemaWithoutRegisteredMigration_ThrowsSchemaVersionUnsupported()
    {
        var fileSystem = new MockFileSystem();
        fileSystem.AddFile(Paths.SettingsFilePath, new MockFileData("""{ "schemaVersion": 0 }"""));
        var service = Create(fileSystem);

        await Should.ThrowAsync<SettingsSchemaVersionUnsupportedException>(() => service.LoadAsync());
    }

    // Migration factice v0 -> v1 : pose schemaVersion=1 par la seule action du pipeline (le champ
    // schemaVersion lui-même est géré par SettingsMigrationPipeline.Apply, jamais par la migration).
    // Le v0 imaginé ici n'a jamais existé réellement (v1 est le premier schéma) : ce test prouve
    // uniquement que LoadAsync invoque bien le pipeline puis repersiste, comme documenté.
    private sealed class FakeZeroToOneSettingsMigration : ISettingsMigration
    {
        public int FromSchemaVersion => 0;

        public System.Text.Json.Nodes.JsonObject Migrate(System.Text.Json.Nodes.JsonObject document) => document;
    }
}