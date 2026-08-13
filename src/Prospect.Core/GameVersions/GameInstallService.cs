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
    public GameInstallService(
        IGameVersionCatalog catalog,
        IDownloadManager downloads,
        IInstalledGameVersionRepository repository,
        IGameInstallStrategy strategy)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(downloads);
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(strategy);

        _catalog = catalog;
        _downloads = downloads;
        _repository = repository;
        _strategy = strategy;
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

    /// <summary>Supprime une version installée. Sans effet si elle ne l'est pas.</summary>
    public void Uninstall(GameVersion version) => _repository.Remove(version);

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
    // est toujours une installation complète.
    private async Task InstallArchiveAsync(
        GameVersion version,
        string archivePath,
        IProgress<GameInstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        _repository.PrepareDirectory(version);

        var completed = false;
        try
        {
            await _strategy.InstallAsync(archivePath, _repository.GetVersionDirectory(version), progress, cancellationToken).ConfigureAwait(false);
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

    private sealed class DownloadProgressAdapter : IProgress<DownloadProgress>
    {
        private readonly IProgress<GameInstallProgress> _target;

        public DownloadProgressAdapter(IProgress<GameInstallProgress> target) => _target = target;

        public void Report(DownloadProgress value) => _target.Report(GameInstallProgress.FromDownload(value));
    }
}