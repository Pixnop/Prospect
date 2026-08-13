using System.IO.Abstractions;
using System.Text.Json;
using System.Text.Json.Nodes;

using Prospect.Core.Common;
using Prospect.Core.Settings.Migrations;
using Prospect.Core.Storage;

namespace Prospect.Core.Settings;

/// <summary>
/// Façade applicative du domaine Settings (docs/architecture.md, patterns « Services
/// applicatifs ») : lit <c>prospect.json</c> au démarrage, expose le réglage courant de façon
/// typée et synchrone (<see cref="Current"/>), et sauvegarde atomiquement chaque changement via
/// <see cref="JsonFileStore"/>. C'est le seul point d'entrée que les ViewModels et
/// <c>Prospect.Desktop.Services.ThemeService</c> consomment.
/// </summary>
/// <remarks>
/// Contrairement au domaine Instances, il n'y a pas d'<c>IRepository</c> séparé ici : Settings est
/// un fichier UNIQUE, pas une collection scannée (pas de dossier à énumérer, pas de notion
/// d'entrée « cassée » isolée du reste). La lecture avec garde de migration (document JSON brut,
/// inspection de <c>schemaVersion</c>, migration puis désérialisation typée) reproduit donc le
/// pattern d'<see cref="Instances.FileSystemInstanceRepository.LoadAsync"/> directement dans ce
/// service plutôt que de l'extraire dans une abstraction supplémentaire que rien d'autre
/// n'utiliserait.
/// </remarks>
public sealed class SettingsService
{
    private readonly IFileSystem _fileSystem;
    private readonly AppPaths _appPaths;
    private readonly JsonFileStore _jsonFileStore;
    private readonly SettingsMigrationPipeline _migrationPipeline;
    private readonly IUiCulture _uiCulture;

    public SettingsService(
        IFileSystem fileSystem,
        AppPaths appPaths,
        JsonFileStore jsonFileStore,
        SettingsMigrationPipeline migrationPipeline,
        IUiCulture uiCulture)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(appPaths);
        ArgumentNullException.ThrowIfNull(jsonFileStore);
        ArgumentNullException.ThrowIfNull(migrationPipeline);
        ArgumentNullException.ThrowIfNull(uiCulture);

        _fileSystem = fileSystem;
        _appPaths = appPaths;
        _jsonFileStore = jsonFileStore;
        _migrationPipeline = migrationPipeline;
        _uiCulture = uiCulture;

        // Valeurs saines avant même le premier LoadAsync : un ViewModel construit avant que
        // App.OnFrameworkInitializationCompleted n'ait fini de charger (ou un test qui ne charge
        // jamais) lit toujours un réglage exploitable plutôt qu'un null à vérifier partout.
        Current = ProspectSettings.CreateDefault();
    }

    /// <summary>Réglages actuellement en vigueur. Jamais <see langword="null"/> (voir le constructeur).</summary>
    public ProspectSettings Current { get; private set; }

    /// <summary>
    /// Levé après un chargement ou une sauvegarde qui change effectivement <see cref="Current"/> :
    /// c'est ce qu'écoute <c>ThemeService</c> pour appliquer un changement de thème à chaud.
    /// </summary>
    public event EventHandler<ProspectSettings>? Changed;

    /// <summary>
    /// Lit <c>prospect.json</c> et remplace <see cref="Current"/>. Absence de fichier = premier
    /// lancement, une absence normale (docs/architecture.md, docstring de
    /// <see cref="JsonFileStore.ReadAsync{T}"/>) : les valeurs par défaut posées au constructeur
    /// restent en vigueur, rien à migrer ni à notifier. À appeler une seule fois, tôt, avant que
    /// quoi que ce soit qui dépend du thème ne construise sa première fenêtre (voir
    /// <c>Prospect.Desktop.App.OnFrameworkInitializationCompleted</c>).
    /// </summary>
    /// <remarks>
    /// L'absence de fichier est aussi le SEUL moment où la langue par défaut se déduit de la
    /// culture du système (<see cref="ProspectSettings.LanguageForCulture"/>) : dès qu'un
    /// <c>prospect.json</c> existe, c'est lui qui décide, la détection ne repasse jamais par-dessus
    /// un choix persisté. Rien n'est écrit sur disque pour autant — persister une déduction la
    /// transformerait en décision que l'utilisateur n'a pas prise, alors que la première vraie
    /// modification de réglage l'écrira de toute façon.
    /// </remarks>
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        var path = _appPaths.SettingsFilePath;
        if (!_fileSystem.File.Exists(path))
        {
            Current = Current with { Language = ProspectSettings.LanguageForCulture(_uiCulture.Name) };

            return;
        }

        var document = await ReadJsonObjectAsync(path, cancellationToken).ConfigureAwait(false);
        var schemaVersion = ReadSchemaVersion(document, path);

        if (schemaVersion > ProspectSettings.CurrentSchemaVersion)
        {
            throw new SettingsSchemaVersionUnsupportedException(path, schemaVersion, ProspectSettings.CurrentSchemaVersion);
        }

        var upToDate = schemaVersion == ProspectSettings.CurrentSchemaVersion
            ? document
            : _migrationPipeline.Apply(document, schemaVersion, ProspectSettings.CurrentSchemaVersion, path);

        var settings = upToDate.Deserialize(ProspectSettingsJsonContext.Default.ProspectSettings)
            ?? throw new CorruptedFileException(path, new JsonException($"'{path}' s'est désérialisé en un réglage nul."));

        Current = settings.Normalized();

        // Une migration a changé la forme du document sur disque : on la persiste tout de suite,
        // même règle qu'InstanceRepository.LoadAsync, pour ne pas refaire le même travail de
        // migration à chaque démarrage tant que le fichier n'est pas réécrit pour une autre raison.
        if (schemaVersion != ProspectSettings.CurrentSchemaVersion)
        {
            await SaveCurrentAsync(cancellationToken).ConfigureAwait(false);
        }

        Changed?.Invoke(this, Current);
    }

    /// <summary>
    /// Applique <paramref name="mutate"/> à <see cref="Current"/>, sauvegarde le résultat (écriture
    /// atomique via <see cref="JsonFileStore"/>) puis lève <see cref="Changed"/>. Point d'entrée
    /// unique de toute mutation : le thème, le parallélisme des téléchargements, et tout futur
    /// réglage passent tous par ici.
    /// </summary>
    public Task UpdateAsync(Func<ProspectSettings, ProspectSettings> mutate, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mutate);

        Current = mutate(Current).Normalized();

        return SaveAndNotifyAsync(cancellationToken);
    }

    private async Task SaveAndNotifyAsync(CancellationToken cancellationToken)
    {
        await SaveCurrentAsync(cancellationToken).ConfigureAwait(false);
        Changed?.Invoke(this, Current);
    }

    private Task SaveCurrentAsync(CancellationToken cancellationToken)
        => _jsonFileStore.WriteAsync(_appPaths.SettingsFilePath, Current, ProspectSettingsJsonContext.Default.ProspectSettings, cancellationToken);

    // Duplique volontairement la petite lecture JsonObject + garde de schéma de
    // FileSystemInstanceRepository.ReadJsonObjectAsync plutôt que de l'extraire en abstraction
    // partagée (voir la remarque de la classe) : Settings n'a qu'UN document à lire, l'indirection
    // supplémentaire n'apporterait rien qu'un niveau de lecture en plus.
    private async Task<JsonObject> ReadJsonObjectAsync(string path, CancellationToken cancellationToken)
    {
        var stream = _fileSystem.File.OpenRead(path);
        await using (stream.ConfigureAwait(false))
        {
            JsonNode? node;
            try
            {
                node = await JsonNode.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            catch (JsonException ex)
            {
                throw new CorruptedFileException(path, ex);
            }

            return node as JsonObject
                ?? throw new CorruptedFileException(path, new JsonException($"'{path}' n'est pas un document JSON objet."));
        }
    }

    private static int ReadSchemaVersion(JsonObject document, string path)
    {
        if (document.TryGetPropertyValue("schemaVersion", out var node)
            && node is JsonValue value
            && value.TryGetValue(out int schemaVersion))
        {
            return schemaVersion;
        }

        throw new CorruptedFileException(path, new JsonException($"'{path}' ne contient pas de champ schemaVersion entier exploitable."));
    }
}