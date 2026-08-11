using System.IO.Abstractions;

using Prospect.Core.Common;

namespace Prospect.Core.Instances;

/// <summary>
/// Façade applicative du domaine Instances (docs/architecture.md, patterns « Services
/// applicatifs ») : créer, renommer, dupliquer, supprimer. C'est le seul point d'entrée que les
/// ViewModels consomment ; toute la topologie disque reste derrière <see cref="IInstanceRepository"/>.
/// </summary>
/// <remarks>
/// Deux décisions structurantes documentées ici plutôt qu'implicites dans le code : le slug d'une
/// instance ne bouge jamais après sa création (<see cref="RenameAsync"/> ne change que
/// <see cref="InstanceMetadata.Name"/>, jamais le dossier), et une duplication produit une
/// instance neuve au sens des statistiques (<see cref="DuplicateAsync"/> réinitialise
/// <see cref="InstanceMetadata.LastLaunchedUtc"/> et <see cref="InstanceMetadata.TotalPlaytimeSeconds"/>
/// plutôt que de les copier).
/// </remarks>
public sealed class InstanceService
{
    private readonly IInstanceRepository _repository;
    private readonly IFileSystem _fileSystem;
    private readonly IClock _clock;

    public InstanceService(IInstanceRepository repository, IFileSystem fileSystem, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(clock);

        _repository = repository;
        _fileSystem = fileSystem;
        _clock = clock;
    }

    /// <summary>
    /// Crée une instance : slug unique dérivé de <paramref name="name"/>, dossier
    /// <c>data/</c> vide, <c>instance.json</c> au schéma courant avec un identifiant neuf.
    /// </summary>
    /// <exception cref="InstanceNameInvalidException"><paramref name="name"/> est vide ou ne contient que des espaces.</exception>
    public async Task<InstanceRecord> CreateAsync(string name, GameVersion gameVersion, CancellationToken cancellationToken = default)
    {
        EnsureValidName(name);

        var slug = InstanceSlugGenerator.GenerateUnique(name, _repository.Exists);
        var metadata = new InstanceMetadata
        {
            SchemaVersion = InstanceMetadata.CurrentSchemaVersion,
            Id = Guid.NewGuid(),
            Name = name,
            GameVersion = gameVersion,
            CreatedUtc = _clock.UtcNow,
        };
        var record = new InstanceRecord(slug, metadata);

        _fileSystem.Directory.CreateDirectory(_repository.GetDataDirectory(slug));
        await _repository.SaveAsync(record, cancellationToken).ConfigureAwait(false);

        return record;
    }

    /// <summary>
    /// Renomme une instance. Ne change que <see cref="InstanceMetadata.Name"/> : le slug de
    /// dossier, choisi une fois à la création, reste stable. Un renommage d'affichage ne doit pas
    /// déplacer de fichiers ni invalider un chemin déjà ouvert ailleurs (raccourci, log en cours).
    /// </summary>
    /// <exception cref="InstanceNameInvalidException"><paramref name="newName"/> est vide ou ne contient que des espaces.</exception>
    /// <exception cref="InstanceNotFoundException">Aucune instance pour <paramref name="slug"/>.</exception>
    public async Task<InstanceRecord> RenameAsync(string slug, string newName, CancellationToken cancellationToken = default)
    {
        EnsureValidName(newName);

        var current = await _repository.LoadAsync(slug, cancellationToken).ConfigureAwait(false);
        var renamed = current with { Metadata = current.Metadata with { Name = newName } };
        await _repository.SaveAsync(renamed, cancellationToken).ConfigureAwait(false);

        return renamed;
    }

    /// <summary>
    /// Change l'icône d'une instance. Même contrat que <see cref="RenameAsync"/> : seul
    /// <see cref="InstanceMetadata.Icon"/> change, le slug de dossier reste stable. Utilisé par
    /// l'étape « icône » du wizard de création, qui ne connaît son choix qu'après l'appel à
    /// <see cref="CreateAsync"/> (l'instance doit déjà exister pour recevoir son icône).
    /// </summary>
    /// <exception cref="InstanceNotFoundException">Aucune instance pour <paramref name="slug"/>.</exception>
    public async Task<InstanceRecord> SetIconAsync(string slug, string icon, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(icon);

        var current = await _repository.LoadAsync(slug, cancellationToken).ConfigureAwait(false);
        var updated = current with { Metadata = current.Metadata with { Icon = icon } };
        await _repository.SaveAsync(updated, cancellationToken).ConfigureAwait(false);

        return updated;
    }

    /// <summary>
    /// Duplique une instance : nouvel identifiant, nouveau slug dérivé de
    /// <paramref name="newName"/>, copie fichier par fichier de <c>data/</c>. Une copie est une
    /// instance neuve du point de vue des statistiques : <see cref="InstanceMetadata.LastLaunchedUtc"/>
    /// repart à <see langword="null"/> et <see cref="InstanceMetadata.TotalPlaytimeSeconds"/> à
    /// zéro plutôt que d'être copiés depuis la source. En cas d'échec ou d'annulation pendant la
    /// copie, le dossier cible partiellement écrit est supprimé : aucune instance à moitié née ne
    /// doit apparaître au prochain scan.
    /// </summary>
    /// <exception cref="InstanceNameInvalidException"><paramref name="newName"/> est vide ou ne contient que des espaces.</exception>
    /// <exception cref="InstanceNotFoundException">Aucune instance pour <paramref name="sourceSlug"/>.</exception>
    public async Task<InstanceRecord> DuplicateAsync(
        string sourceSlug,
        string newName,
        IProgress<InstanceCopyProgress>? progress,
        CancellationToken cancellationToken = default)
    {
        EnsureValidName(newName);

        var source = await _repository.LoadAsync(sourceSlug, cancellationToken).ConfigureAwait(false);
        var targetSlug = InstanceSlugGenerator.GenerateUnique(newName, _repository.Exists);

        try
        {
            await CopyDirectoryAsync(
                _repository.GetDataDirectory(sourceSlug),
                _repository.GetDataDirectory(targetSlug),
                progress,
                cancellationToken).ConfigureAwait(false);

            var duplicated = new InstanceRecord(
                targetSlug,
                source.Metadata with
                {
                    Id = Guid.NewGuid(),
                    Name = newName,
                    CreatedUtc = _clock.UtcNow,
                    LastLaunchedUtc = null,
                    TotalPlaytimeSeconds = 0,
                });

            await _repository.SaveAsync(duplicated, cancellationToken).ConfigureAwait(false);

            return duplicated;
        }
        catch
        {
            RemoveDirectoryIfExists(_repository.GetInstanceDirectory(targetSlug));
            throw;
        }
    }

    /// <summary>
    /// Remplace les réglages de lancement (arguments supplémentaires et variables
    /// d'environnement) d'une instance. Même contrat que <see cref="RenameAsync"/> et
    /// <see cref="SetIconAsync"/> : un seul champ change, consommé par l'onglet Options de la page
    /// de détail.
    /// </summary>
    /// <exception cref="InstanceNotFoundException">Aucune instance pour <paramref name="slug"/>.</exception>
    public async Task<InstanceRecord> UpdateLaunchSettingsAsync(string slug, InstanceLaunchSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var current = await _repository.LoadAsync(slug, cancellationToken).ConfigureAwait(false);
        var updated = current with { Metadata = current.Metadata with { Launch = settings } };
        await _repository.SaveAsync(updated, cancellationToken).ConfigureAwait(false);

        return updated;
    }

    /// <summary>
    /// Marque l'instance comme lancée à l'instant courant (<see cref="InstanceMetadata.LastLaunchedUtc"/>).
    /// Appelée par <c>RunningInstanceTracker</c> au DÉMARRAGE du processus de jeu, pas à sa sortie :
    /// « dernier lancement » décrit le moment où l'utilisateur a cliqué sur Jouer, et une session
    /// encore en cours doit immédiatement remonter en tête du tri par défaut de l'Accueil
    /// (<c>HomeSortMode.LastLaunched</c>) sans attendre que le joueur quitte le jeu, ce qui peut
    /// prendre des heures.
    /// </summary>
    /// <exception cref="InstanceNotFoundException">Aucune instance pour <paramref name="slug"/>.</exception>
    public async Task<InstanceRecord> MarkLaunchedAsync(string slug, CancellationToken cancellationToken = default)
    {
        var current = await _repository.LoadAsync(slug, cancellationToken).ConfigureAwait(false);
        var updated = current with { Metadata = current.Metadata with { LastLaunchedUtc = _clock.UtcNow } };
        await _repository.SaveAsync(updated, cancellationToken).ConfigureAwait(false);

        return updated;
    }

    /// <summary>
    /// Ajoute <paramref name="additionalSeconds"/> au temps de jeu cumulé de l'instance
    /// (<see cref="InstanceMetadata.TotalPlaytimeSeconds"/>). Appelée par
    /// <c>RunningInstanceTracker</c> à la SORTIE du processus de jeu, avec la durée réellement
    /// écoulée depuis le lancement : contrairement à <see cref="MarkLaunchedAsync"/>, cette
    /// donnée n'a de sens qu'une fois la session terminée.
    /// </summary>
    /// <exception cref="InstanceNotFoundException">Aucune instance pour <paramref name="slug"/>.</exception>
    public async Task<InstanceRecord> AddPlaytimeAsync(string slug, long additionalSeconds, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(additionalSeconds);

        var current = await _repository.LoadAsync(slug, cancellationToken).ConfigureAwait(false);
        var updated = current with
        {
            Metadata = current.Metadata with { TotalPlaytimeSeconds = current.Metadata.TotalPlaytimeSeconds + additionalSeconds },
        };
        await _repository.SaveAsync(updated, cancellationToken).ConfigureAwait(false);

        return updated;
    }

    /// <summary>
    /// Supprime définitivement une instance (dossier <c>instances/&lt;slug&gt;</c> entier, dont
    /// <c>data/</c>). La double confirmation face à l'utilisateur est la responsabilité de l'UI,
    /// pas de ce service.
    /// </summary>
    /// <exception cref="InstanceNotFoundException">Aucune instance pour <paramref name="slug"/>.</exception>
    public Task DeleteAsync(string slug, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_repository.Exists(slug))
        {
            throw new InstanceNotFoundException(slug);
        }

        _fileSystem.Directory.Delete(_repository.GetInstanceDirectory(slug), recursive: true);

        return Task.CompletedTask;
    }

    private static void EnsureValidName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InstanceNameInvalidException(name ?? string.Empty);
        }
    }

    private void RemoveDirectoryIfExists(string directory)
    {
        if (_fileSystem.Directory.Exists(directory))
        {
            _fileSystem.Directory.Delete(directory, recursive: true);
        }
    }

    private async Task CopyDirectoryAsync(
        string sourceDirectory,
        string targetDirectory,
        IProgress<InstanceCopyProgress>? progress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _fileSystem.Directory.CreateDirectory(targetDirectory);

        var files = _fileSystem.Directory.Exists(sourceDirectory)
            ? _fileSystem.Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories)
            : [];

        for (var index = 0; index < files.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await CopyFileAsync(sourceDirectory, targetDirectory, files[index], cancellationToken).ConfigureAwait(false);
            progress?.Report(new InstanceCopyProgress(index + 1, files.Length));
        }
    }

    private async Task CopyFileAsync(string sourceDirectory, string targetDirectory, string sourceFile, CancellationToken cancellationToken)
    {
        var relativePath = _fileSystem.Path.GetRelativePath(sourceDirectory, sourceFile);
        var destinationFile = _fileSystem.Path.Combine(targetDirectory, relativePath);
        var destinationDirectory = _fileSystem.Path.GetDirectoryName(destinationFile);
        if (!string.IsNullOrEmpty(destinationDirectory))
        {
            _fileSystem.Directory.CreateDirectory(destinationDirectory);
        }

        var sourceStream = _fileSystem.File.OpenRead(sourceFile);
        await using (sourceStream.ConfigureAwait(false))
        {
            var destinationStream = _fileSystem.File.Create(destinationFile);
            await using (destinationStream.ConfigureAwait(false))
            {
                await sourceStream.CopyToAsync(destinationStream, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}