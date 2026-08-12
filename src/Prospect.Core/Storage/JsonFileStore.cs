using System.IO.Abstractions;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Prospect.Core.Storage;

/// <summary>
/// Lit et écrit des documents JSON sur le système de fichiers abstrait
/// (<see cref="IFileSystem"/>), pour tous les schémas persistants du Core (prospect.json,
/// instance.json, prospect-mods.json...). Prend délibérément un <see cref="JsonTypeInfo{T}"/>
/// plutôt que des <see cref="JsonSerializerOptions"/> : aucun appelant ne peut se rabattre sur
/// la réflexion, chaque schéma apporte son propre contexte source-gen.
/// </summary>
public sealed class JsonFileStore
{
    /// <summary>
    /// Suffixe du fichier temporaire de l'écriture atomique (voir <see cref="WriteAsync{T}"/>).
    /// Public parce qu'un appelant qui écrit un secret doit pouvoir restreindre les permissions de
    /// ce fichier AVANT qu'il ne porte quoi que ce soit (voir <c>Auth.FileSecretStore</c>) : le
    /// contenu passe par là avant d'exister sous son nom définitif.
    /// </summary>
    public const string TempFileSuffix = ".tmp";

    private static readonly JsonWriterOptions IndentedWriterOptions = new() { Indented = true };

    private readonly IFileSystem _fileSystem;

    public JsonFileStore(IFileSystem fileSystem)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);

        _fileSystem = fileSystem;
    }

    /// <summary>
    /// Lit et désérialise <paramref name="path"/>. Renvoie <see langword="null"/> si le fichier
    /// n'existe pas encore : c'est une absence normale (première utilisation, réglages jamais
    /// enregistrés), pas une erreur. Un fichier présent mais illisible comme JSON lève en
    /// revanche une <see cref="CorruptedFileException"/> dédiée plutôt qu'une
    /// <see cref="JsonException"/> nue, pour que l'appelant sache quel fichier est en cause.
    /// </summary>
    public async Task<T?> ReadAsync<T>(string path, JsonTypeInfo<T> typeInfo, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(typeInfo);

        if (!_fileSystem.File.Exists(path))
        {
            return default;
        }

        var stream = _fileSystem.File.OpenRead(path);
        await using (stream.ConfigureAwait(false))
        {
            try
            {
                return await JsonSerializer.DeserializeAsync(stream, typeInfo, cancellationToken).ConfigureAwait(false);
            }
            catch (JsonException ex)
            {
                throw new CorruptedFileException(path, ex);
            }
        }
    }

    /// <summary>
    /// Sérialise <paramref name="value"/> et l'écrit à <paramref name="path"/> de façon
    /// atomique : le dossier parent est créé au besoin, le contenu est d'abord écrit dans
    /// <c>&lt;path&gt;.tmp</c> (UTF-8 sans BOM, indenté, indépendamment de la configuration du
    /// contexte source-gen appelant), puis ce fichier temporaire remplace la cible en un seul
    /// déplacement. Cette indirection existe pour une raison précise : une coupure (crash,
    /// perte d'alimentation) en plein milieu d'une écriture directe laisserait un fichier à
    /// moitié écrit, donc un JSON corrompu au prochain démarrage. En écrivant à côté puis en
    /// déplaçant, soit l'ancien contenu reste intact, soit le nouveau est complet : jamais
    /// d'état intermédiaire visible.
    /// </summary>
    public async Task WriteAsync<T>(string path, T value, JsonTypeInfo<T> typeInfo, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(typeInfo);

        var directory = _fileSystem.Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            _fileSystem.Directory.CreateDirectory(directory);
        }

        var tempPath = path + TempFileSuffix;
        try
        {
            var stream = _fileSystem.File.Create(tempPath);
            await using (stream.ConfigureAwait(false))
            {
                using var writer = new Utf8JsonWriter(stream, IndentedWriterOptions);
                JsonSerializer.Serialize(writer, value, typeInfo);
                await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch
        {
            // Le fichier temporaire ne doit jamais rester après un échec : la cible d'origine,
            // elle, n'a pas encore été touchée à ce stade.
            if (_fileSystem.File.Exists(tempPath))
            {
                _fileSystem.File.Delete(tempPath);
            }

            throw;
        }

        _fileSystem.File.Move(tempPath, path, overwrite: true);
    }
}