using System.IO.Abstractions;

using Prospect.Core.Common;
using Prospect.Core.Http;

namespace Prospect.Core.GameVersions;

/// <summary>
/// Façade du domaine « versions du jeu » pour les ViewModels (docs/architecture.md, « Services
/// applicatifs par domaine ») : elle enchaîne téléchargement, vérification d'empreinte,
/// installation propre à l'OS et écriture du fichier sentinelle, en publiant une progression
/// unique pour ces quatre étapes.
/// </summary>
public sealed class GameInstallService
{
    private readonly IGameVersionCatalog _catalog;
    private readonly IDownloadManager _downloads;
    private readonly IInstalledGameVersionRepository _repository;
    private readonly IGameInstallStrategy _strategy;
    private readonly IFileSystem _fileSystem;
    private readonly IAppLog _log;

    /// <summary>
    /// Construit le service.
    /// </summary>
    /// <param name="catalog">Catalogue distant, pour retrouver le fichier d'une version.</param>
    /// <param name="downloads">File de téléchargements partagée.</param>
    /// <param name="repository">Installations locales.</param>
    /// <param name="strategy">
    /// Stratégie d'installation de l'OS courant, résolue à la composition (voir
    /// <see cref="GameInstallStrategySelector"/>). Le service ignore complètement sur quel système
    /// il tourne.
    /// </param>
    /// <param name="fileSystem">
    /// Système de fichiers abstrait, pour la seule vérification que ce service fait lui-même :
    /// constater qu'un exécutable du jeu est bien arrivé dans le dossier de la version avant d'y
    /// poser la sentinelle de complétude.
    /// </param>
    /// <param name="log">Journal de diagnostic.</param>
    public GameInstallService(
        IGameVersionCatalog catalog,
        IDownloadManager downloads,
        IInstalledGameVersionRepository repository,
        IGameInstallStrategy strategy,
        IFileSystem fileSystem,
        IAppLog log)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(downloads);
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(strategy);
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(log);

        _catalog = catalog;
        _downloads = downloads;
        _repository = repository;
        _strategy = strategy;
        _fileSystem = fileSystem;
        _log = log;
    }

    /// <summary>
    /// Installe une version du jeu côte à côte des autres, sous <c>versions/&lt;version&gt;</c>.
    /// </summary>
    /// <param name="version">Version à installer.</param>
    /// <param name="progress">Observateur des quatre phases.</param>
    /// <param name="cancellationToken">
    /// Annulation. Le fichier partiel du téléchargement et le dossier d'installation incomplet
    /// sont nettoyés avant que l'annulation ne remonte : aucune trace ne subsiste d'une
    /// installation abandonnée.
    /// </param>
    /// <exception cref="GameVersionNotAvailableException">Version inconnue du catalogue, ou sans build pour cette plateforme.</exception>
    /// <exception cref="DownloadFailedException">Téléchargement impossible ou empreinte incorrecte.</exception>
    /// <exception cref="GameInstallFailedException">Extraction ou installeur en échec.</exception>
    /// <exception cref="GameInstallIncompleteException">Installation terminée sans erreur mais sans exécutable dans la cible.</exception>
    public async Task<InstalledGameVersion> InstallAsync(
        GameVersion version,
        IProgress<GameInstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var asset = await ResolveAssetAsync(version, cancellationToken).ConfigureAwait(false);

        var request = new DownloadRequest($"Vintage Story {version}", asset.FileName, asset.Mirrors, asset.Md5);
        var downloadProgress = progress is null
            ? null
            : new DownloadProgressAdapter(progress);

        var archivePath = await _downloads.DownloadAsync(request, downloadProgress, cancellationToken).ConfigureAwait(false);

        // Annonce d'entrée dans la phase, indéterminée : la stratégie prendra le relais avec un
        // avancement chiffré si elle sait se mesurer (extraction), et restera muette sinon
        // (installeur Inno silencieux), auquel cas c'est ce rapport-ci qui tient la barre.
        progress?.Report(GameInstallProgress.ForPhase(GameInstallPhase.Installing));
        await InstallArchiveAsync(version, archivePath, progress, cancellationToken).ConfigureAwait(false);
        progress?.Report(GameInstallProgress.ForPhase(GameInstallPhase.Completed));

        return _repository.Find(version)
            ?? throw new GameInstallFailedException($"L'installation de la version {version} s'est terminée sans laisser de dossier exploitable.");
    }

    /// <summary>
    /// Supprime une version installée, hors du thread appelant et en rendant compte de son
    /// avancement. Sans effet si elle ne l'est pas.
    /// </summary>
    /// <remarks>
    /// Une installation pèse plusieurs centaines de mégaoctets : la version synchrone gelait
    /// l'interface le temps d'un effacement récursif, exactement comme le faisait la suppression
    /// d'instance avant sa correction. L'avancement est DÉTERMINÉ, parce qu'on peut compter les
    /// fichiers avant de les effacer (voir <see cref="Storage.DirectoryDeleter"/>).
    /// </remarks>
    /// <exception cref="Storage.DirectoryDeleteFailedException">Il reste des fichiers sur le disque.</exception>
    public Task UninstallAsync(
        GameVersion version,
        IProgress<Storage.DirectoryDeleteProgress>? progress = null,
        CancellationToken cancellationToken = default)
        => _repository.RemoveAsync(version, progress, cancellationToken);

    /// <summary>
    /// Le fichier que cette machine installerait pour cette entrée du catalogue, ou
    /// <see langword="null"/> si aucun n'est publié pour sa plateforme. Les ViewModels s'en
    /// servent pour ne proposer que ce qui est réellement installable, sans avoir à savoir sur
    /// quel système ils tournent ni comment le catalogue nomme ses plateformes.
    /// </summary>
    public GameVersionAsset? FindInstallableAsset(GameVersionCatalogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return entry.FindAsset(_strategy.PlatformKeys);
    }

    private async Task<GameVersionAsset> ResolveAssetAsync(GameVersion version, CancellationToken cancellationToken)
    {
        var catalog = await _catalog.GetAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        var entry = catalog.Versions.FirstOrDefault(candidate => candidate.Version == version)
            ?? throw GameVersionNotAvailableException.ForUnknownVersion(version);

        return FindInstallableAsset(entry)
            ?? throw GameVersionNotAvailableException.ForUnsupportedPlatform(version, _strategy.PlatformKeys);
    }

    // Le fichier sentinelle est écrit en dernier, et le dossier est effacé si quoi que ce soit
    // échoue en route : c'est ce qui garantit qu'un dossier de versions/ porteur de la sentinelle
    // est toujours une installation complète. Entre les deux, une vérification de RÉSULTAT : la
    // stratégie a rendu la main sans erreur, encore faut-il qu'un exécutable du jeu soit vraiment
    // là. Sans elle, une installation partie ailleurs (installeur Windows retombé sur une
    // installation système) ou une extraction vide se marquait complète, et la fausse réussite ne
    // se découvrait qu'au premier « Jouer ».
    private async Task InstallArchiveAsync(
        GameVersion version,
        string archivePath,
        IProgress<GameInstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        _repository.PrepareDirectory(version);

        var targetDirectory = _repository.GetVersionDirectory(version);
        var completed = false;
        try
        {
            await _strategy.InstallAsync(archivePath, targetDirectory, progress, cancellationToken).ConfigureAwait(false);
            VerifyExecutablePresent(version, targetDirectory);
            await _repository.MarkCompleteAsync(version, cancellationToken).ConfigureAwait(false);
            completed = true;
        }
        finally
        {
            if (!completed)
            {
                _repository.Remove(version);
            }
        }
    }

    private void VerifyExecutablePresent(GameVersion version, string targetDirectory)
    {
        var expected = _strategy.ExpectedExecutables;
        var found = expected.FirstOrDefault(location => _fileSystem.File.Exists(location.ResolveIn(_fileSystem, targetDirectory)));

        if (found is null)
        {
            _log.Write(
                AppLogLevel.Error,
                $"Version {version} : aucun exécutable attendu ({string.Join(", ", expected)}) dans « {targetDirectory} » après installation.");

            throw GameInstallIncompleteException.For(version, targetDirectory, expected);
        }

        _log.Write(AppLogLevel.Info, $"Version {version} installée : « {found.ResolveIn(_fileSystem, targetDirectory)} » présent.");
    }

    private sealed class DownloadProgressAdapter : IProgress<DownloadProgress>
    {
        private readonly IProgress<GameInstallProgress> _target;

        public DownloadProgressAdapter(IProgress<GameInstallProgress> target) => _target = target;

        public void Report(DownloadProgress value) => _target.Report(GameInstallProgress.FromDownload(value));
    }
}