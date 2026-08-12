using System.IO.Abstractions;

using Prospect.Core.Common;
using Prospect.Core.GameVersions;
using Prospect.Core.Instances;
using Prospect.Core.Storage;

namespace Prospect.Core.Migration;

/// <summary>
/// Façade applicative du domaine Migration (docs/architecture.md, patterns « Services applicatifs
/// par domaine ») : adopte des installations et des moteurs VS Launcher préalablement détectés par
/// <see cref="VslDetector"/> et sélectionnés par l'utilisateur. Même philosophie de rapport par
/// élément que <see cref="Prospect.Core.Modpacks.ModpackImportService"/> : l'échec ou l'absence
/// d'UN élément n'interrompt jamais le traitement des suivants, seule une annulation arrête le lot.
/// </summary>
/// <remarks>
/// <para>
/// <b>Adoption non destructive.</b> Les données d'une installation sont toujours COPIÉES, jamais
/// déplacées : VS Launcher reste intact après une adoption, l'utilisateur peut continuer à s'en
/// servir ou revenir en arrière sans rien avoir perdu. C'est un choix délibéré plutôt qu'une
/// prudence par défaut — un déplacement économiserait de l'espace disque, mais transformerait un
/// essai de Prospect en migration à sens unique.
/// </para>
/// <para>
/// <b>Adoption optionnelle des moteurs, pari assumé.</b> Copier un moteur déjà présent sous VS
/// Launcher (<c>VSLGameVersions/&lt;version&gt;</c>) épargne un retéléchargement pouvant peser 600
/// Mo, mais ces fichiers n'ont jamais été vérifiés par Prospect (pas de contrôle md5 comme au
/// téléchargement, voir <see cref="GameInstallService"/>) : c'est un pari délibéré que des fichiers
/// locaux existants valent mieux qu'un octet supplémentaire téléchargé, pas une garantie
/// d'intégrité. La sentinelle <see cref="IInstalledGameVersionRepository.MarkCompleteAsync"/> est
/// posée en fin de copie comme pour toute installation, et un marqueur additif
/// (<see cref="VslEngineProvenance"/>) documente l'origine. En cas de doute, l'utilisateur peut
/// toujours désinstaller puis retélécharger depuis l'écran Versions : rien n'est irréversible.
/// </para>
/// <para>
/// <b>Ce qui n'est jamais adopté.</b> Les sauvegardes VS Launcher (<c>VSLBackups/</c> : le système
/// de sauvegardes de Prospect est un chantier séparé) et les réglages globaux de VS Launcher
/// (compte, fenêtre, mods favoris, icônes personnalisées) ne sont ni lus ni copiés par ce service.
/// </para>
/// </remarks>
public sealed class VslAdoptionService
{
    private readonly InstanceService _instanceService;
    private readonly IInstanceRepository _instanceRepository;
    private readonly IInstalledGameVersionRepository _installedGameVersions;
    private readonly JsonFileStore _jsonFileStore;
    private readonly IFileSystem _fileSystem;
    private readonly IClock _clock;
    private readonly DirectoryCopier _copier;

    /// <summary>Construit le service.</summary>
    /// <param name="instanceService">Création (et nettoyage sur échec) des instances adoptées.</param>
    /// <param name="instanceRepository">Topologie disque des instances, pour la cible de copie et la sauvegarde des métadonnées reprises.</param>
    /// <param name="installedGameVersions">Installations locales de versions du jeu, pour l'adoption optionnelle des moteurs.</param>
    /// <param name="jsonFileStore">Écriture du marqueur de provenance d'un moteur adopté.</param>
    /// <param name="fileSystem">Système de fichiers abstrait, pour vérifier l'existence des dossiers source.</param>
    /// <param name="clock">Horloge, pour dater le marqueur de provenance.</param>
    public VslAdoptionService(
        InstanceService instanceService,
        IInstanceRepository instanceRepository,
        IInstalledGameVersionRepository installedGameVersions,
        JsonFileStore jsonFileStore,
        IFileSystem fileSystem,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(instanceService);
        ArgumentNullException.ThrowIfNull(instanceRepository);
        ArgumentNullException.ThrowIfNull(installedGameVersions);
        ArgumentNullException.ThrowIfNull(jsonFileStore);
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(clock);

        _instanceService = instanceService;
        _instanceRepository = instanceRepository;
        _installedGameVersions = installedGameVersions;
        _jsonFileStore = jsonFileStore;
        _fileSystem = fileSystem;
        _clock = clock;
        _copier = new DirectoryCopier(fileSystem);
    }

    /// <summary>
    /// Adopte les installations et moteurs sélectionnés, les installations d'abord puis les
    /// moteurs. Un élément en échec ou ignoré n'interrompt pas les suivants ; une annulation
    /// nettoie l'élément en cours (instance partiellement créée, ou dossier de moteur
    /// partiellement copié) puis arrête le lot — ce qui a déjà été adopté avant l'annulation le
    /// reste, comme pour l'import de modpack.
    /// </summary>
    /// <param name="installations">Installations sélectionnées, telles que détectées par <see cref="VslDetector"/>.</param>
    /// <param name="engines">Moteurs sélectionnés.</param>
    /// <param name="progress">Avancement par élément, avec le détail fichier par fichier de la copie en cours.</param>
    /// <param name="cancellationToken">Annulation.</param>
    public async Task<VslAdoptionOutcome> AdoptAsync(
        IReadOnlyList<VslInstallation> installations,
        IReadOnlyList<VslGameVersionEntry> engines,
        IProgress<VslAdoptionProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(installations);
        ArgumentNullException.ThrowIfNull(engines);

        var installationReports = new List<VslInstallationAdoptionReport>();
        for (var index = 0; index < installations.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var installation = installations[index];
            var displayName = DisplayNameOf(installation);
            progress?.Report(new VslAdoptionProgress(VslAdoptionPhase.AdoptingInstallations, index, installations.Count, displayName, null));

            installationReports.Add(await AdoptInstallationAsync(
                installation,
                displayName,
                progress,
                index,
                installations.Count,
                cancellationToken).ConfigureAwait(false));
        }

        if (installations.Count > 0)
        {
            progress?.Report(new VslAdoptionProgress(VslAdoptionPhase.AdoptingInstallations, installations.Count, installations.Count, null, null));
        }

        var engineReports = new List<VslEngineAdoptionReport>();
        for (var index = 0; index < engines.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var engine = engines[index];
            progress?.Report(new VslAdoptionProgress(VslAdoptionPhase.AdoptingEngines, index, engines.Count, engine.Version, null));

            engineReports.Add(await AdoptEngineAsync(engine, cancellationToken).ConfigureAwait(false));
        }

        if (engines.Count > 0)
        {
            progress?.Report(new VslAdoptionProgress(VslAdoptionPhase.AdoptingEngines, engines.Count, engines.Count, null, null));
        }

        return new VslAdoptionOutcome(installationReports, engineReports);
    }

    private static string DisplayNameOf(VslInstallation installation)
        => string.IsNullOrWhiteSpace(installation.Name) ? installation.Id : installation.Name;

    private async Task<VslInstallationAdoptionReport> AdoptInstallationAsync(
        VslInstallation installation,
        string displayName,
        IProgress<VslAdoptionProgress>? progress,
        int index,
        int total,
        CancellationToken cancellationToken)
    {
        if (!GameVersion.TryParse(installation.Version, out var gameVersion))
        {
            return VslInstallationAdoptionReport.Skipped(displayName, $"version « {installation.Version} » illisible");
        }

        if (!_fileSystem.Directory.Exists(installation.Path))
        {
            return VslInstallationAdoptionReport.Skipped(displayName, "dossier source introuvable");
        }

        InstanceRecord created;
        try
        {
            created = await _instanceService.CreateAsync(displayName, gameVersion, cancellationToken).ConfigureAwait(false);
        }
        catch (InstanceNameInvalidException)
        {
            return VslInstallationAdoptionReport.Skipped(displayName, "nom d'instance inexploitable");
        }

        var completed = false;
        try
        {
            var fileProgress = progress is null ? null : new CopyProgressAdapter(progress, index, total, displayName);
            await _copier.CopyAsync(installation.Path, _instanceRepository.GetDataDirectory(created.Slug), fileProgress, cancellationToken)
                .ConfigureAwait(false);

            var launch = VslInstanceMapper.ToLaunchSettings(installation);
            var adopted = created with
            {
                Metadata = created.Metadata with
                {
                    Launch = launch,
                    LastLaunchedUtc = VslInstanceMapper.ToLastLaunchedUtc(installation.LastTimePlayedMs),
                    TotalPlaytimeSeconds = VslInstanceMapper.ToTotalPlaytimeSeconds(installation.TotalTimePlayedMs),
                },
            };
            await _instanceRepository.SaveAsync(adopted, cancellationToken).ConfigureAwait(false);
            completed = true;

            return VslInstallationAdoptionReport.Adopted(displayName, adopted);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return VslInstallationAdoptionReport.Failed(displayName, exception.Message);
        }
        finally
        {
            if (!completed)
            {
                await DeleteInstanceQuietlyAsync(created.Slug).ConfigureAwait(false);
            }
        }
    }

    // Nettoyage en best effort avec un jeton neuf : celui de l'appelant est probablement déjà
    // annulé ici (cas de l'annulation), et InstanceService.DeleteAsync lève immédiatement sur un
    // jeton déjà annulé (même précédent que ModpackImportService.DeleteInstanceQuietlyAsync).
    private async Task DeleteInstanceQuietlyAsync(string slug)
    {
        try
        {
            await _instanceService.DeleteAsync(slug, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InstanceNotFoundException)
        {
            // L'exception d'origine (annulation, échec de copie) est celle qui compte pour
            // l'appelant, pas un éventuel second échec pendant le nettoyage.
        }
    }

    private async Task<VslEngineAdoptionReport> AdoptEngineAsync(VslGameVersionEntry engine, CancellationToken cancellationToken)
    {
        if (!GameVersion.TryParse(engine.Version, out var gameVersion))
        {
            return VslEngineAdoptionReport.Skipped(engine.Version, "version illisible");
        }

        var label = gameVersion.ToString();

        if (_installedGameVersions.IsInstalled(gameVersion))
        {
            return VslEngineAdoptionReport.Skipped(label, "déjà installée");
        }

        if (!_fileSystem.Directory.Exists(engine.Path))
        {
            return VslEngineAdoptionReport.Skipped(label, "dossier source introuvable");
        }

        // Même séquence que GameInstallService.InstallArchiveAsync : la sentinelle est posée en
        // dernier, et le dossier est effacé si quoi que ce soit échoue en route, pour qu'un dossier
        // de versions/ porteur de la sentinelle soit toujours une installation complète.
        _installedGameVersions.PrepareDirectory(gameVersion);
        var completed = false;
        try
        {
            await _copier.CopyAsync(engine.Path, _installedGameVersions.GetVersionDirectory(gameVersion), progress: null, cancellationToken)
                .ConfigureAwait(false);
            await _installedGameVersions.MarkCompleteAsync(gameVersion, cancellationToken).ConfigureAwait(false);
            await WriteProvenanceAsync(gameVersion, engine.Path, cancellationToken).ConfigureAwait(false);
            completed = true;

            return VslEngineAdoptionReport.Adopted(label);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return VslEngineAdoptionReport.Failed(label, exception.Message);
        }
        finally
        {
            if (!completed)
            {
                _installedGameVersions.Remove(gameVersion);
            }
        }
    }

    private Task WriteProvenanceAsync(GameVersion version, string sourcePath, CancellationToken cancellationToken)
    {
        var path = _fileSystem.Path.Combine(_installedGameVersions.GetVersionDirectory(version), VslEngineProvenance.FileName);
        var provenance = new VslEngineProvenance { SourcePath = sourcePath, AdoptedUtc = _clock.UtcNow };

        return _jsonFileStore.WriteAsync(path, provenance, VslJsonContext.Default.VslEngineProvenance, cancellationToken);
    }

    // Même pattern que GameInstallService.DownloadProgressAdapter / ModpackImportService.GameVersionProgressAdapter :
    // un adaptateur dédié plutôt qu'une lambda, pour rester explicite sur ce qu'IProgress<T> encapsule ici.
    private sealed class CopyProgressAdapter : IProgress<DirectoryCopyProgress>
    {
        private readonly IProgress<VslAdoptionProgress> _target;
        private readonly int _index;
        private readonly int _total;
        private readonly string _label;

        public CopyProgressAdapter(IProgress<VslAdoptionProgress> target, int index, int total, string label)
        {
            _target = target;
            _index = index;
            _total = total;
            _label = label;
        }

        public void Report(DirectoryCopyProgress value)
            => _target.Report(new VslAdoptionProgress(VslAdoptionPhase.AdoptingInstallations, _index, _total, _label, value));
    }
}