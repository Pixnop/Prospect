using System.IO.Abstractions;

using Prospect.Core.Common;
using Prospect.Core.Storage;

namespace Prospect.Core.Auth;

/// <summary>
/// Implémentation fichier d'<see cref="ISecretStore"/> : un <c>session.json</c> à la racine des
/// données de Prospect, lisible et modifiable par son seul propriétaire (mode <c>0600</c>).
/// </summary>
/// <remarks>
/// <para>
/// Sur Windows, <see cref="IUnixFilePermissions"/> ne fait rien et le fichier hérite des ACL du
/// profil utilisateur, qui interdisent déjà l'accès aux autres comptes de la machine : suffisant à
/// ce stade, et documenté comme tel. Le trousseau de l'OS (DPAPI, Secret Service, Keychain) reste
/// le chantier suivant, derrière <see cref="ISecretStore"/> justement pour n'avoir rien d'autre à
/// changer le jour venu.
/// </para>
/// <para>
/// L'ordre des opérations d'écriture n'est pas anodin. <see cref="JsonFileStore"/> écrit d'abord
/// dans un fichier temporaire puis le déplace sur la cible ; ce temporaire porte donc le secret
/// complet avant d'exister sous son nom final. Il est créé vide et restreint AVANT l'écriture,
/// puisque <c>File.Create</c> tronque un fichier existant sans toucher à son mode et que le
/// déplacement emporte ensuite ces permissions sur la cible. La restriction posée à la fin sur la
/// cible elle-même est la ceinture qui va avec les bretelles, pour le cas où le temporaire aurait
/// été balayé entre-temps.
/// </para>
/// </remarks>
public sealed class FileSecretStore : ISecretStore
{
    private const UnixFileMode OwnerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    private readonly IFileSystem _fileSystem;
    private readonly AppPaths _appPaths;
    private readonly JsonFileStore _jsonFileStore;
    private readonly IUnixFilePermissions _permissions;

    public FileSecretStore(
        IFileSystem fileSystem,
        AppPaths appPaths,
        JsonFileStore jsonFileStore,
        IUnixFilePermissions permissions)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(appPaths);
        ArgumentNullException.ThrowIfNull(jsonFileStore);
        ArgumentNullException.ThrowIfNull(permissions);

        _fileSystem = fileSystem;
        _appPaths = appPaths;
        _jsonFileStore = jsonFileStore;
        _permissions = permissions;
    }

    /// <summary>Chemin du fichier de session.</summary>
    public string SessionFilePath => _appPaths.SessionFilePath;

    /// <inheritdoc />
    public async Task<VsSession?> LoadAsync(CancellationToken cancellationToken = default)
    {
        VsSession? session;
        try
        {
            session = await _jsonFileStore
                .ReadAsync(SessionFilePath, VsAuthJsonContext.Default.VsSession, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (CorruptedFileException)
        {
            // Un secret illisible n'est pas une erreur à remonter à l'utilisateur au démarrage :
            // c'est une session perdue, il se reconnectera.
            return null;
        }

        return session is { IsUsable: true } ? session : null;
    }

    /// <inheritdoc />
    public async Task SaveAsync(VsSession session, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        PrepareRestrictedTemporaryFile();

        await _jsonFileStore
            .WriteAsync(SessionFilePath, session, VsAuthJsonContext.Default.VsSession, cancellationToken)
            .ConfigureAwait(false);

        _permissions.SetMode(SessionFilePath, OwnerOnly);
    }

    /// <inheritdoc />
    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_fileSystem.File.Exists(SessionFilePath))
        {
            _fileSystem.File.Delete(SessionFilePath);
        }

        return Task.CompletedTask;
    }

    // Voir la remarque de classe : le fichier temporaire naît vide et déjà restreint, pour que le
    // secret n'existe jamais sur le disque avec des permissions larges, même une fraction de
    // seconde.
    private void PrepareRestrictedTemporaryFile()
    {
        var temporaryPath = SessionFilePath + JsonFileStore.TempFileSuffix;
        var directory = _fileSystem.Path.GetDirectoryName(temporaryPath);
        if (!string.IsNullOrEmpty(directory))
        {
            _fileSystem.Directory.CreateDirectory(directory);
        }

        _fileSystem.File.Create(temporaryPath).Dispose();
        _permissions.SetMode(temporaryPath, OwnerOnly);
    }
}