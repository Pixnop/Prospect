using System.IO.Abstractions;
using System.IO.Compression;

using Prospect.Core.Common;
using Prospect.Core.GameVersions;
using Prospect.Core.Http;
using Prospect.Core.Instances;
using Prospect.Core.ModDb;

namespace Prospect.Core.Modpacks;

/// <summary>
/// Façade du domaine Modpacks pour l'import (docs/architecture.md, « 5. Modpacks ») : c'est le
/// test d'intégration naturel du Core, cette classe ne fait QUE composer des services déjà écrits
/// pour les features précédentes (<see cref="InstanceService"/>, <see cref="GameInstallService"/>,
/// <see cref="IModDbClient"/>, <see cref="IDownloadManager"/>, <see cref="IInstalledModRepository"/>)
/// plutôt que de réinventer une installation de mod ou de version de jeu.
/// </summary>
/// <remarks>
/// Deux temps, comme <see cref="ModInstallService"/> : <see cref="PreviewAsync"/> lit et valide le
/// manifest sans rien créer, <see cref="ImportAsync"/> exécute. L'échec d'UN mod (introuvable,
/// version absente, empreinte invalide, réseau) n'interrompt jamais l'import : il est capturé dans
/// le rapport et le mod suivant est tenté. Seuls la création de l'instance et l'installation de la
/// version de jeu peuvent faire échouer tout l'import (rien d'autre n'a de sens sans eux). En cas
/// d'échec ou d'annulation avant la fin de la résolution des mods, l'instance tout juste créée est
/// supprimée : une instance sans sa version de jeu ni son contenu n'a aucun intérêt à survivre. Une
/// fois les mods traités, l'import ne revient plus en arrière : la pose de <c>ModConfig/</c> est un
/// confort tenté en best effort, jamais un motif d'annuler un import par ailleurs réussi.
/// </remarks>
public sealed class ModpackImportService
{
    private readonly IModDbClient _modDb;
    private readonly IDownloadManager _downloads;
    private readonly IInstalledModRepository _installedMods;
    private readonly InstanceService _instanceService;
    private readonly IInstanceRepository _instanceRepository;
    private readonly GameInstallService _gameInstall;
    private readonly IInstalledGameVersionRepository _installedGameVersions;
    private readonly IGameVersionCatalog _gameCatalog;
    private readonly IFileSystem _fileSystem;
    private readonly IClock _clock;

    /// <summary>Construit le service.</summary>
    /// <param name="modDb">Client ModDB, pour résoudre chaque mod du manifest.</param>
    /// <param name="downloads">File de téléchargements partagée avec le reste du Core.</param>
    /// <param name="installedMods">Mods installés de l'instance créée.</param>
    /// <param name="instanceService">Création et suppression (annulation) de l'instance.</param>
    /// <param name="instanceRepository">Topologie disque, pour poser <c>ModConfig/</c>.</param>
    /// <param name="gameInstall">Installation de la version de jeu du manifest si elle manque.</param>
    /// <param name="installedGameVersions">Pour savoir si cette installation est déjà nécessaire.</param>
    /// <param name="gameCatalog">Catalogue distant, pour annoncer le poids du téléchargement dans l'aperçu.</param>
    /// <param name="fileSystem">Système de fichiers abstrait.</param>
    /// <param name="clock">Horloge, pour dater la provenance de chaque mod installé.</param>
    public ModpackImportService(
        IModDbClient modDb,
        IDownloadManager downloads,
        IInstalledModRepository installedMods,
        InstanceService instanceService,
        IInstanceRepository instanceRepository,
        GameInstallService gameInstall,
        IInstalledGameVersionRepository installedGameVersions,
        IGameVersionCatalog gameCatalog,
        IFileSystem fileSystem,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(modDb);
        ArgumentNullException.ThrowIfNull(downloads);
        ArgumentNullException.ThrowIfNull(installedMods);
        ArgumentNullException.ThrowIfNull(instanceService);
        ArgumentNullException.ThrowIfNull(instanceRepository);
        ArgumentNullException.ThrowIfNull(gameInstall);
        ArgumentNullException.ThrowIfNull(installedGameVersions);
        ArgumentNullException.ThrowIfNull(gameCatalog);
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(clock);

        _modDb = modDb;
        _downloads = downloads;
        _installedMods = installedMods;
        _instanceService = instanceService;
        _instanceRepository = instanceRepository;
        _gameInstall = gameInstall;
        _installedGameVersions = installedGameVersions;
        _gameCatalog = gameCatalog;
        _fileSystem = fileSystem;
        _clock = clock;
    }

    /// <summary>
    /// Lit et valide <paramref name="sourcePath"/> (manifest <c>.json</c> ou archive <c>.zip</c>,
    /// détecté par contenu plutôt que par extension) sans rien créer : ce que l'UI montre avant de
    /// demander confirmation (nom, version de jeu, nombre de mods, coût d'une éventuelle
    /// installation de version).
    /// </summary>
    /// <exception cref="ModpackSourceInvalidException">Fichier absent, ni JSON ni zip exploitable.</exception>
    /// <exception cref="ModpackManifestInvalidException">Manifest invalide (voir <see cref="ModpackManifestSerializer"/>).</exception>
    public async Task<ModpackImportPreview> PreviewAsync(string sourcePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);

        var (manifest, format, hasModConfig) = await ReadSourceAsync(sourcePath, cancellationToken).ConfigureAwait(false);
        var installed = _installedGameVersions.IsInstalled(manifest.GameVersion);
        var downloadSize = installed ? null : await TryResolveDownloadSizeAsync(manifest.GameVersion, cancellationToken).ConfigureAwait(false);

        return new ModpackImportPreview(sourcePath, manifest, format, installed, hasModConfig, downloadSize);
    }

    // Confort d'affichage seulement (docs/architecture.md : « avertissement ... avec son coût de
    // téléchargement ») : un catalogue injoignable ne doit jamais empêcher l'aperçu, juste priver
    // l'avertissement de sa taille annoncée.
    private async Task<string?> TryResolveDownloadSizeAsync(GameVersion version, CancellationToken cancellationToken)
    {
        try
        {
            var catalog = await _gameCatalog.GetAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
            var entry = catalog.Versions.FirstOrDefault(candidate => candidate.Version == version);

            return entry is null ? null : _gameInstall.FindInstallableAsset(entry)?.DisplaySize;
        }
        catch (GameCatalogUnavailableException)
        {
            return null;
        }
    }

    /// <summary>
    /// Exécute l'import : crée l'instance, installe la version de jeu si besoin, résout et
    /// télécharge chaque mod, pose <c>ModConfig/</c> si l'archive en porte un.
    /// </summary>
    /// <param name="preview">Aperçu produit par <see cref="PreviewAsync"/>.</param>
    /// <param name="instanceName">
    /// Nom de l'instance, ou <see langword="null"/>/vide pour reprendre <c>preview.Manifest.Name</c>.
    /// </param>
    /// <param name="progress">Avancement par phases (version de jeu, puis mods).</param>
    /// <param name="cancellationToken">
    /// Annulation. Avant la fin de la résolution des mods, l'instance tout juste créée est
    /// supprimée avant que l'annulation ne remonte (voir la remarque de la classe). Une fois cette
    /// phase terminée, l'import ne revient plus en arrière.
    /// </param>
    /// <exception cref="InstanceNameInvalidException">Nom d'instance vide.</exception>
    /// <exception cref="GameVersionNotAvailableException">Version de jeu introuvable au catalogue.</exception>
    /// <exception cref="DownloadFailedException">Téléchargement de la version de jeu impossible.</exception>
    /// <exception cref="GameInstallFailedException">Installation de la version de jeu en échec.</exception>
    public async Task<ModpackImportOutcome> ImportAsync(
        ModpackImportPreview preview,
        string? instanceName = null,
        IProgress<ModpackImportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preview);

        var manifest = preview.Manifest;
        var name = string.IsNullOrWhiteSpace(instanceName) ? manifest.Name : instanceName;

        var record = await _instanceService.CreateAsync(name, manifest.GameVersion, cancellationToken).ConfigureAwait(false);

        IReadOnlyList<ModpackImportModReport> reports;
        try
        {
            if (!_installedGameVersions.IsInstalled(manifest.GameVersion))
            {
                var adapter = progress is null ? null : new GameVersionProgressAdapter(progress);
                await _gameInstall.InstallAsync(manifest.GameVersion, adapter, cancellationToken).ConfigureAwait(false);
            }

            reports = await ImportModsAsync(record.Slug, manifest, progress, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Instance et version de jeu forment un couple indissociable : sans la bonne version,
            // l'instance ne sait pas se lancer, donc autant ne pas la laisser traîner. Nettoyage en
            // best effort avec un jeton neuf : celui de l'appelant est probablement déjà annulé ici,
            // et InstanceService.DeleteAsync lève immédiatement sur un jeton déjà annulé.
            await DeleteInstanceQuietlyAsync(record.Slug).ConfigureAwait(false);
            throw;
        }

        if (preview.SourceFormat == ModpackSourceFormat.Archive)
        {
            await ImportModConfigAsync(record.Slug, preview.SourcePath, cancellationToken).ConfigureAwait(false);
        }

        return new ModpackImportOutcome(record, reports);
    }

    private async Task DeleteInstanceQuietlyAsync(string slug)
    {
        try
        {
            await _instanceService.DeleteAsync(slug, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InstanceNotFoundException)
        {
            // L'exception d'origine (annulation, échec de version de jeu) est celle qui compte
            // pour l'appelant, pas un éventuel second échec pendant le nettoyage.
        }
    }

    private async Task<IReadOnlyList<ModpackImportModReport>> ImportModsAsync(
        string slug,
        ModpackManifest manifest,
        IProgress<ModpackImportProgress>? progress,
        CancellationToken cancellationToken)
    {
        var reports = new List<ModpackImportModReport>();
        var total = manifest.Mods.Count;

        for (var index = 0; index < total; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var entry = manifest.Mods[index];
            progress?.Report(new ModpackImportProgress(ModpackImportPhase.InstallingMods, index, total, entry.ModId, null));

            reports.Add(await ImportModAsync(slug, entry, manifest.GameVersion, cancellationToken).ConfigureAwait(false));
        }

        progress?.Report(new ModpackImportProgress(ModpackImportPhase.InstallingMods, total, total, null, null));

        return reports;
    }

    // Le cœur du rapport par mod : chaque branche d'échec attendue rend une entrée plutôt que de
    // laisser une exception remonter, pour que le mod suivant soit quand même tenté
    // (docs/architecture.md, « L'import n'échoue en bloc que si... »). Seule OperationCanceledException
    // n'est délibérément pas absorbée ici : elle doit interrompre la boucle et déclencher le
    // nettoyage de ImportAsync.
    private async Task<ModpackImportModReport> ImportModAsync(
        string slug,
        ModpackManifestMod entry,
        GameVersion gameVersion,
        CancellationToken cancellationToken)
    {
        ModDbModDetail detail;
        try
        {
            detail = await _modDb.GetModAsync(entry.ModId, cancellationToken).ConfigureAwait(false);
        }
        catch (ModDbApiException exception) when (exception.IsNotFound)
        {
            return ModpackImportModReport.NotFound(entry.ModId, entry.Version);
        }
        // ModDbApiException/ModDbUnavailableException sont le contrat documenté de GetModAsync,
        // mais l'implémentation actuelle ne retraduit pas systématiquement une panne de transport
        // brute (HttpRequestException/IOException/TimeoutException) en ModDbUnavailableException :
        // les capturer ici aussi est ce qui garantit qu'un ModDB injoignable isole CE mod plutôt
        // que de faire remonter une exception non prévue jusqu'à l'appelant de tout l'import.
        catch (Exception exception) when (exception is ModDbApiException or ModDbUnavailableException or HttpRequestException or IOException or TimeoutException)
        {
            return ModpackImportModReport.NetworkFailure(entry.ModId, entry.Version, exception.Message);
        }

        var release = ResolveRelease(detail, entry);
        if (release is null)
        {
            var suggestion = ModReleaseSelector.Select(detail.Releases, gameVersion, ModCompatibilityMode.ExactGameVersion);

            return ModpackImportModReport.VersionMissing(entry.ModId, entry.Version, suggestion?.Release.Version);
        }

        string archivePath;
        try
        {
            var request = new DownloadRequest(detail.Name, ModInstallService.BuildFileName(release.ModIdString, release.Version), [release.DownloadUrl]);
            archivePath = await _downloads.DownloadAsync(request, progress: null, cancellationToken).ConfigureAwait(false);
        }
        catch (DownloadFailedException exception)
        {
            return ModpackImportModReport.NetworkFailure(entry.ModId, entry.Version, exception.Message);
        }

        if (entry.Sha256 is { Length: > 0 } expectedSha256)
        {
            var actualSha256 = await Sha256Checksum.ComputeAsync(_fileSystem, archivePath, cancellationToken).ConfigureAwait(false);
            if (!Sha256Checksum.Matches(expectedSha256, actualSha256))
            {
                return ModpackImportModReport.Sha256Mismatch(entry.ModId, entry.Version);
            }
        }

        await InstallDownloadedModAsync(slug, detail, release, entry, archivePath, cancellationToken).ConfigureAwait(false);

        return ModpackImportModReport.Installed(entry.ModId, entry.Version, release.Version);
    }

    // fileId d'abord (raccourci qui survit à un modId réutilisé ou à un doublon de version), version
    // exacte ensuite. Jamais « la plus récente compatible » ici : contrairement à l'installation
    // depuis le navigateur de mods, l'import doit reproduire EXACTEMENT ce que le manifest décrit.
    private static ModDbRelease? ResolveRelease(ModDbModDetail detail, ModpackManifestMod entry)
    {
        if (entry.FileId is { } fileId)
        {
            var byFileId = detail.Releases.FirstOrDefault(release => release.FileId == fileId);
            if (byFileId is not null)
            {
                return byFileId;
            }
        }

        return detail.Releases.FirstOrDefault(release => release.Version == entry.Version);
    }

    private async Task InstallDownloadedModAsync(
        string slug,
        ModDbModDetail detail,
        ModDbRelease release,
        ModpackManifestMod entry,
        string archivePath,
        CancellationToken cancellationToken)
    {
        var fileName = ModInstallService.BuildFileName(release.ModIdString, release.Version);
        var modsDirectory = _installedMods.GetModsDirectory(slug);
        _fileSystem.Directory.CreateDirectory(modsDirectory);

        var target = _fileSystem.Path.Combine(modsDirectory, fileName);
        _fileSystem.File.Copy(archivePath, target, overwrite: true);

        await _installedMods.SaveProvenanceAsync(
            slug,
            new ModProvenance
            {
                FileName = fileName,
                ModId = detail.ModId,
                ModIdString = release.ModIdString,
                ReleaseId = release.ReleaseId,
                FileId = release.FileId,
                Version = release.Version,
                InstalledUtc = _clock.UtcNow,
                ApproximateMatch = false,
            },
            cancellationToken).ConfigureAwait(false);

        if (entry.IsEnabled)
        {
            return;
        }

        var scanned = await _installedMods.ScanAsync(slug, cancellationToken).ConfigureAwait(false);
        var installedMod = scanned.FirstOrDefault(mod => string.Equals(mod.FileName, fileName, StringComparison.OrdinalIgnoreCase));
        if (installedMod is not null)
        {
            await _installedMods.SetEnabledAsync(slug, installedMod, enabled: false, cancellationToken).ConfigureAwait(false);
        }
    }

    // ── Lecture de la source (manifest seul ou archive) ────────────────────────────

    private async Task<(ModpackManifest Manifest, ModpackSourceFormat Format, bool HasModConfig)> ReadSourceAsync(
        string sourcePath,
        CancellationToken cancellationToken)
    {
        if (!_fileSystem.File.Exists(sourcePath))
        {
            throw new ModpackSourceInvalidException($"Le fichier « {sourcePath} » n'existe pas.");
        }

        if (IsZipArchive(sourcePath))
        {
            return await ReadArchiveAsync(sourcePath, cancellationToken).ConfigureAwait(false);
        }

        var stream = _fileSystem.File.OpenRead(sourcePath);
        await using (stream.ConfigureAwait(false))
        {
            var manifest = await ModpackManifestSerializer.ReadAsync(stream, cancellationToken).ConfigureAwait(false);

            return (manifest, ModpackSourceFormat.ManifestOnly, false);
        }
    }

    // Détection par contenu (signature locale de fichier zip "PK") plutôt que par extension : un
    // fichier renommé par l'utilisateur doit rester importable.
    private bool IsZipArchive(string path)
    {
        try
        {
            var stream = _fileSystem.File.OpenRead(path);
            using (stream)
            {
                return stream.ReadByte() == 'P' && stream.ReadByte() == 'K';
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private async Task<(ModpackManifest Manifest, ModpackSourceFormat Format, bool HasModConfig)> ReadArchiveAsync(
        string sourcePath,
        CancellationToken cancellationToken)
    {
        var stream = _fileSystem.File.OpenRead(sourcePath);
        await using (stream.ConfigureAwait(false))
        {
            ZipArchive archive;
            try
            {
                archive = new ZipArchive(stream, ZipArchiveMode.Read);
            }
            catch (InvalidDataException exception)
            {
                throw new ModpackSourceInvalidException(
                    "Le fichier sélectionné n'est ni un manifest de modpack ni une archive .zip valide.",
                    exception);
            }

            using (archive)
            {
                var manifestEntry = archive.GetEntry(ModpackArchiveLayout.ManifestFileName)
                    ?? throw new ModpackSourceInvalidException($"L'archive ne contient pas de {ModpackArchiveLayout.ManifestFileName}.");

                ModpackManifest manifest;
                var manifestStream = manifestEntry.Open();
                await using (manifestStream.ConfigureAwait(false))
                {
                    manifest = await ModpackManifestSerializer.ReadAsync(manifestStream, cancellationToken).ConfigureAwait(false);
                }

                var hasModConfig = archive.Entries.Any(candidate
                    => candidate.FullName.StartsWith(ModpackArchiveLayout.ModConfigEntryPrefix, StringComparison.OrdinalIgnoreCase));

                return (manifest, ModpackSourceFormat.Archive, hasModConfig);
            }
        }
    }

    // ── ModConfig/ : posé en best effort, jamais un motif d'échec de l'import ─────

    private async Task ImportModConfigAsync(string slug, string sourcePath, CancellationToken cancellationToken)
    {
        try
        {
            var stream = _fileSystem.File.OpenRead(sourcePath);
            await using (stream.ConfigureAwait(false))
            {
                using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
                var targetRoot = _fileSystem.Path.Combine(_instanceRepository.GetDataDirectory(slug), ModpackArchiveLayout.ModConfigFolderName);

                foreach (var entry in archive.Entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await ImportModConfigEntryAsync(entry, targetRoot, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            // ModConfig/ est un confort : à ce stade, les mods sont déjà installés avec succès. Un
            // dossier de réglages non recopié ne doit jamais faire échouer un import par ailleurs
            // terminé.
        }
    }

    private async Task ImportModConfigEntryAsync(ZipArchiveEntry entry, string targetRoot, CancellationToken cancellationToken)
    {
        if (!entry.FullName.StartsWith(ModpackArchiveLayout.ModConfigEntryPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var relative = entry.FullName[ModpackArchiveLayout.ModConfigEntryPrefix.Length..];
        if (relative.Length == 0)
        {
            // L'entrée de dossier elle-même (certains compresseurs en écrivent une) : rien à copier.
            return;
        }

        var destination = ResolveSafeDestination(targetRoot, relative);
        if (destination is null)
        {
            // Entrée qui tenterait d'écrire hors de ModConfig/ (../.. ou chemin absolu dans le nom
            // d'entrée) : ignorée plutôt que suivie, même défense que ModArchiveReader contre les
            // archives hostiles.
            return;
        }

        var destinationDirectory = _fileSystem.Path.GetDirectoryName(destination);
        if (!string.IsNullOrEmpty(destinationDirectory))
        {
            _fileSystem.Directory.CreateDirectory(destinationDirectory);
        }

        var source = entry.Open();
        await using (source.ConfigureAwait(false))
        {
            var target = _fileSystem.File.Create(destination);
            await using (target.ConfigureAwait(false))
            {
                await source.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private string? ResolveSafeDestination(string targetRoot, string relative)
    {
        var normalizedRelative = relative.Replace('\\', '/');
        var root = _fileSystem.Path.GetFullPath(targetRoot);
        var rootPrefix = root.EndsWith(_fileSystem.Path.DirectorySeparatorChar) ? root : root + _fileSystem.Path.DirectorySeparatorChar;
        var combined = _fileSystem.Path.GetFullPath(_fileSystem.Path.Combine(targetRoot, normalizedRelative));

        return combined.StartsWith(rootPrefix, StringComparison.Ordinal) ? combined : null;
    }

    private sealed class GameVersionProgressAdapter : IProgress<GameInstallProgress>
    {
        private readonly IProgress<ModpackImportProgress> _target;

        public GameVersionProgressAdapter(IProgress<ModpackImportProgress> target) => _target = target;

        public void Report(GameInstallProgress value)
            => _target.Report(new ModpackImportProgress(ModpackImportPhase.InstallingGameVersion, 0, 0, null, value));
    }
}