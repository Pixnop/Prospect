using System.IO.Abstractions;

using Prospect.Core.Common;
using Prospect.Core.Http;
using Prospect.Core.Instances;

namespace Prospect.Core.ModDb;

/// <summary>Aucune release du mod ne convient à la version de jeu de l'instance.</summary>
public sealed class ModReleaseNotFoundException : Exception
{
    /// <summary>Construit l'erreur.</summary>
    /// <param name="modName">Nom du mod.</param>
    /// <param name="gameVersion">Version de jeu visée.</param>
    public ModReleaseNotFoundException(string modName, GameVersion gameVersion)
        : base($"Aucune version de « {modName} » n'est publiée pour Vintage Story {gameVersion}.")
    {
        ModName = modName;
        GameVersion = gameVersion;
    }

    /// <summary>Nom du mod concerné.</summary>
    public string ModName { get; }

    /// <summary>Version de jeu pour laquelle rien n'a été trouvé.</summary>
    public GameVersion GameVersion { get; }
}

/// <summary>L'installation a échoué après le téléchargement.</summary>
public sealed class ModInstallFailedException : Exception
{
    /// <summary>Construit l'erreur.</summary>
    public ModInstallFailedException(string message)
        : base(message)
    {
    }

    /// <summary>Construit l'erreur avec sa cause.</summary>
    public ModInstallFailedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Le fichier reçu ne fait pas la taille annoncée par le CDN. Faute de checksum côté ModDB
    /// (l'API n'en expose aucun, la recherche l'a vérifié dans tout <c>lib/api/</c>), c'est le seul
    /// garde-fou d'intégrité disponible.
    /// </summary>
    public static ModInstallFailedException ForSizeMismatch(string fileName, long expected, long actual)
        => new($"Le fichier « {fileName} » fait {actual} octets alors que le ModDB en annonçait {expected}. Téléchargement incomplet ou corrompu.");
}

/// <summary>
/// Façade du domaine ModDb pour les ViewModels (docs/architecture.md, « Services applicatifs par
/// domaine ») : choisir une release, la télécharger, la poser dans l'instance, écrire sa provenance,
/// et proposer les dépendances manquantes sans jamais les installer d'autorité.
/// </summary>
/// <remarks>
/// L'installation se fait en DEUX temps, et ce n'est pas un détail. <see cref="PrepareAsync"/>
/// télécharge l'archive dans le cache partagé et lit son <c>modinfo.json</c> : c'est le seul moyen
/// de connaître les dépendances réelles, elles ne sont exposées nulle part dans l'API. Il en sort
/// un plan, montré à l'utilisateur. <see cref="ApplyAsync"/> ne fait ensuite que copier dans
/// <c>data/Mods/</c> ce qui a été accepté. Une dépendance non cochée n'est jamais installée.
/// </remarks>
public sealed class ModInstallService
{
    private readonly IModDbClient _client;
    private readonly IDownloadManager _downloads;
    private readonly IInstalledModRepository _repository;
    private readonly IInstanceRepository _instances;
    private readonly ModArchiveReader _archiveReader;
    private readonly IFileSystem _fileSystem;
    private readonly IClock _clock;

    /// <summary>Construit le service.</summary>
    /// <param name="client">Client ModDB.</param>
    /// <param name="downloads">File de téléchargements partagée avec les versions du jeu.</param>
    /// <param name="repository">Mods installés de l'instance.</param>
    /// <param name="instances">Instances, pour connaître la version de jeu visée.</param>
    /// <param name="archiveReader">Lecteur de <c>modinfo.json</c>.</param>
    /// <param name="fileSystem">Système de fichiers abstrait.</param>
    /// <param name="clock">Horloge, pour dater la provenance.</param>
    public ModInstallService(
        IModDbClient client,
        IDownloadManager downloads,
        IInstalledModRepository repository,
        IInstanceRepository instances,
        ModArchiveReader archiveReader,
        IFileSystem fileSystem,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(downloads);
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(instances);
        ArgumentNullException.ThrowIfNull(archiveReader);
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(clock);

        _client = client;
        _downloads = downloads;
        _repository = repository;
        _instances = instances;
        _archiveReader = archiveReader;
        _fileSystem = fileSystem;
        _clock = clock;
    }

    /// <summary>
    /// Prépare l'installation d'un mod dans une instance : choix de la release, téléchargement dans
    /// le cache, lecture des dépendances déclarées, croisement avec <c>resolve-deps</c> et
    /// résolution des manquantes en releases installables.
    /// </summary>
    /// <param name="slug">Instance cible.</param>
    /// <param name="modDbModId">Identifiant numérique de la fiche ModDB.</param>
    /// <param name="mode">Strict, ou élargi à la série mineure.</param>
    /// <param name="releaseId">
    /// Release explicitement choisie dans le dialogue d'installation, ou <see langword="null"/>
    /// pour la meilleure compatible. Le choix explicite porte sur TOUTES les releases publiées, y
    /// compris celles qu'aucun tag ne déclare compatibles : l'utilisateur a le droit d'installer
    /// ce que l'auteur n'a pas coché, il n'a simplement pas le droit de le faire sans le savoir
    /// (voir <see cref="ModReleaseCompatibility"/>). Un identifiant introuvable retombe sur la
    /// meilleure compatible plutôt que d'échouer.
    /// </param>
    /// <param name="progress">Avancement du téléchargement.</param>
    /// <param name="cancellationToken">Annulation.</param>
    /// <exception cref="ModReleaseNotFoundException">Aucune release compatible avec la version de l'instance.</exception>
    /// <exception cref="ModDbApiException">Mod inconnu du ModDB.</exception>
    /// <exception cref="ModDbUnavailableException">ModDB injoignable.</exception>
    /// <exception cref="DownloadFailedException">Téléchargement impossible.</exception>
    public async Task<ModInstallPlan> PrepareAsync(
        string slug,
        int modDbModId,
        ModCompatibilityMode mode = ModCompatibilityMode.ExactGameVersion,
        int? releaseId = null,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var instance = await _instances.LoadAsync(slug, cancellationToken).ConfigureAwait(false);
        var gameVersion = instance.Metadata.GameVersion;

        var detail = await _client.GetModAsync(modDbModId.ToString(System.Globalization.CultureInfo.InvariantCulture), cancellationToken).ConfigureAwait(false);

        // La liste porte TOUTES les releases, chacune avec son verdict : le dialogue n'en montre
        // que les compatibles tant qu'on ne lui demande pas le contraire, mais le choix explicite
        // doit pouvoir tomber sur n'importe laquelle.
        var candidates = ModReleaseSelector.SelectAll(detail.Releases, gameVersion, mode, includeIncompatible: true);
        var automatic = ModReleaseSelector.Automatic(candidates, mode);
        var choice = (releaseId is { } wanted ? candidates.FirstOrDefault(candidate => candidate.Release.ReleaseId == wanted) : null)
            ?? (automatic.Count > 0 ? automatic[0] : null)
            ?? throw new ModReleaseNotFoundException(detail.Name, gameVersion);

        var primary = await BuildItemAsync(detail.ModId, detail.Name, choice, cancellationToken).ConfigureAwait(false);

        var archivePath = await DownloadAsync(primary, progress, cancellationToken).ConfigureAwait(false);
        var content = _archiveReader.Read(archivePath);

        var installed = await _repository.ScanAsync(slug, cancellationToken).ConfigureAwait(false);
        var remote = await ResolveRemoteDependenciesAsync(primary.ModIdString, gameVersion, cancellationToken).ConfigureAwait(false);
        var issues = ModDependencyResolver.FindUnsatisfied(content.Result.Info, installed, remote);

        var (dependencies, unresolved) = await ResolveDependencyItemsAsync(issues, gameVersion, mode, cancellationToken).ConfigureAwait(false);

        return new ModInstallPlan(primary, dependencies, issues, unresolved, gameVersion)
        {
            AvailableReleases = candidates,
            Existing = FindExisting(installed, primary),
        };
    }

    /// <summary>
    /// La copie déjà installée du mod demandé, s'il y en a une.
    /// </summary>
    /// <remarks>
    /// Deux clés, dans cet ordre. La PROVENANCE d'abord (<c>prospect-mods.json</c>), qui porte
    /// l'identifiant ModDB exact de ce que Prospect a installé. Le <c>modid</c> du
    /// <c>modinfo.json</c> ensuite, qui rattrape les zips déposés à la main par le joueur, pour qui
    /// aucune provenance n'existe — c'est le principe du dépôt : le disque est la vérité, le fichier
    /// de provenance n'est qu'un cache.
    /// </remarks>
    private static InstalledMod? FindExisting(IReadOnlyList<InstalledMod> installed, ModInstallItem primary)
        => installed.FirstOrDefault(mod => Matches(mod.Provenance?.ModIdString, primary.ModIdString))
            ?? installed.FirstOrDefault(mod => Matches(mod.Info?.ModId, primary.ModIdString));

    private static bool Matches(string? candidate, string modIdString)
        => !string.IsNullOrEmpty(candidate) && string.Equals(candidate, modIdString, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Exécute un plan : installe le mod demandé, plus les dépendances explicitement retenues.
    /// </summary>
    /// <param name="slug">Instance cible.</param>
    /// <param name="plan">Plan produit par <see cref="PrepareAsync"/>.</param>
    /// <param name="selectedDependencies">
    /// Identifiants des dépendances à installer. <see langword="null"/> ou vide n'en installe
    /// aucune : c'est un choix explicite de l'utilisateur, jamais un défaut implicite. Les
    /// dépendances de <see cref="ModInstallPlan.InstallableAnyway"/> obéissent à la même liste,
    /// mais l'écran ne les coche jamais d'avance.
    /// </param>
    /// <param name="progress">Avancement du téléchargement.</param>
    /// <param name="cancellationToken">Annulation.</param>
    public async Task<ModInstallOutcome> ApplyAsync(
        string slug,
        ModInstallPlan plan,
        IReadOnlyCollection<string>? selectedDependencies = null,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var selected = new HashSet<string>(selectedDependencies ?? [], StringComparer.OrdinalIgnoreCase);
        var installed = new List<InstalledMod> { await InstallPrimaryAsync(slug, plan, progress, cancellationToken).ConfigureAwait(false) };
        var skipped = new List<string>();

        foreach (var dependency in plan.MissingDependencies)
        {
            if (selected.Contains(dependency.ModIdString))
            {
                installed.Add(await InstallItemAsync(slug, dependency, progress, cancellationToken).ConfigureAwait(false));
            }
            else
            {
                skipped.Add(dependency.ModIdString);
            }
        }

        // Dépendances sans release déclarée compatible : installées seulement si l'utilisateur a
        // coché leur case, qui part décochée. Elles passent par le MÊME chemin d'installation, donc
        // par la même écriture de provenance, qui gardera trace du verdict.
        foreach (var dependency in plan.InstallableAnyway)
        {
            if (selected.Contains(dependency.ModIdString))
            {
                installed.Add(await InstallItemAsync(slug, dependency.BestAvailable!, progress, cancellationToken).ConfigureAwait(false));
            }
            else
            {
                skipped.Add(dependency.ModIdString);
            }
        }

        return new ModInstallOutcome(installed, skipped);
    }

    /// <summary>
    /// Pose le mod demandé. Quand une copie est déjà installée, c'est un REMPLACEMENT, mené avec
    /// exactement la discipline de la mise à jour (voir <see cref="ApplyUpdateAsync"/>) : le nouveau
    /// fichier d'abord, l'état activé/désactivé reporté, l'ancien retiré seulement ensuite.
    /// </summary>
    /// <remarks>
    /// L'ordre n'est pas cosmétique. Retirer d'abord laisserait l'instance sans le mod si le
    /// téléchargement se coupait ; empiler sans retirer, ce que faisait le code précédent, laisse
    /// deux versions du même modid dans <c>Mods/</c>, ce dont le comportement au chargement du jeu
    /// n'est défini nulle part. Le retrait est sauté quand le nouveau fichier porte le même nom que
    /// l'ancien (réinstallation de la même version) : il vient d'être écrasé, le supprimer
    /// reviendrait à retirer ce qu'on vient de poser.
    /// </remarks>
    private async Task<InstalledMod> InstallPrimaryAsync(
        string slug,
        ModInstallPlan plan,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (plan.Existing is not { } previous)
        {
            return await InstallItemAsync(slug, plan.Primary, progress, cancellationToken).ConfigureAwait(false);
        }

        var wasEnabled = previous.IsEnabled;
        var installed = await InstallItemAsync(slug, plan.Primary, progress, cancellationToken).ConfigureAwait(false);
        if (!wasEnabled)
        {
            installed = await _repository.SetEnabledAsync(slug, installed, enabled: false, cancellationToken).ConfigureAwait(false);
        }

        if (!string.Equals(previous.FileName, installed.FileName, StringComparison.OrdinalIgnoreCase))
        {
            await _repository.RemoveAsync(slug, previous, cancellationToken).ConfigureAwait(false);
        }

        return installed;
    }

    /// <summary>
    /// Ce que retirer <paramref name="installedMod"/> casserait : la vérification INVERSE, qui nomme
    /// les mods installés dont une dépendance déclarée pointe vers lui.
    /// </summary>
    public async Task<ModUninstallImpact> PrepareUninstallAsync(string slug, InstalledMod installedMod, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(installedMod);

        var installed = await _repository.ScanAsync(slug, cancellationToken).ConfigureAwait(false);

        return new ModUninstallImpact(installedMod, ModDependencyResolver.FindDependents(installedMod, installed));
    }

    /// <summary>Retire un mod de l'instance : son archive et son entrée de provenance.</summary>
    public Task UninstallAsync(string slug, InstalledMod installedMod, CancellationToken cancellationToken = default)
        => _repository.RemoveAsync(slug, installedMod, cancellationToken);

    /// <summary>Active ou désactive un mod, selon la convention en vigueur (voir <see cref="IModStateConvention"/>).</summary>
    public Task<InstalledMod> SetEnabledAsync(string slug, InstalledMod installedMod, bool enabled, CancellationToken cancellationToken = default)
        => _repository.SetEnabledAsync(slug, installedMod, enabled, cancellationToken);

    /// <summary>
    /// Prépare la mise à jour d'un mod déjà installé vers la release trouvée par
    /// <see cref="ModUpdateChecker"/> : téléchargement dans le cache, lecture des dépendances de la
    /// NOUVELLE version (jamais supposées identiques à celles de l'ancienne), et vérification
    /// INFORMATIVE des mods installés qui déclarent dépendre de celui qu'on met à jour.
    /// </summary>
    /// <param name="slug">Instance cible.</param>
    /// <param name="updateResult">
    /// Résultat de <see cref="ModUpdateChecker.CheckAsync"/> pour ce mod, avec
    /// <see cref="ModUpdateResult.HasUpdate"/> vrai.
    /// </param>
    /// <param name="mode">Strict, ou élargi à la série mineure pour la résolution des dépendances manquantes.</param>
    /// <param name="progress">Avancement du téléchargement.</param>
    /// <param name="cancellationToken">Annulation.</param>
    /// <exception cref="ArgumentException"><paramref name="updateResult"/> ne propose pas de mise à jour.</exception>
    public async Task<ModUpdatePlan> PrepareUpdateAsync(
        string slug,
        ModUpdateResult updateResult,
        ModCompatibilityMode mode = ModCompatibilityMode.ExactGameVersion,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(updateResult);
        if (updateResult.AvailableRelease is not { } release)
        {
            throw new ArgumentException("Ce résultat de vérification ne propose pas de mise à jour.", nameof(updateResult));
        }

        var instance = await _instances.LoadAsync(slug, cancellationToken).ConfigureAwait(false);
        var gameVersion = instance.Metadata.GameVersion;
        var previous = updateResult.Mod;

        var modDbModId = previous.Provenance?.ModId
            ?? await ResolveModDbIdAsync(release.ModIdString, cancellationToken).ConfigureAwait(false);
        var size = updateResult.AnnouncedSizeBytes
            ?? await _client.GetFileSizeAsync(release.DownloadUrl, cancellationToken).ConfigureAwait(false);

        var updated = new ModInstallItem(
            modDbModId,
            previous.DisplayName,
            release,
            updateResult.IsApproximateMatch,
            BuildFileName(release.ModIdString, release.Version),
            size);

        var archivePath = await DownloadAsync(updated, progress, cancellationToken).ConfigureAwait(false);
        var content = _archiveReader.Read(archivePath);

        var installed = await _repository.ScanAsync(slug, cancellationToken).ConfigureAwait(false);
        var remote = await ResolveRemoteDependenciesAsync(release.ModIdString, gameVersion, cancellationToken).ConfigureAwait(false);
        var issues = ModDependencyResolver.FindUnsatisfied(content.Result.Info, installed, remote);

        var (dependencies, unresolved) = await ResolveDependencyItemsAsync(issues, gameVersion, mode, cancellationToken).ConfigureAwait(false);
        var dependents = ModDependencyResolver.FindDependents(previous, installed);

        return new ModUpdatePlan(previous, updated, dependencies, issues, unresolved, dependents, gameVersion);
    }

    /// <summary>
    /// Exécute un plan de mise à jour : pose la nouvelle version, préserve l'état activé/désactivé
    /// de l'ancienne, PUIS retire l'ancien fichier (jamais l'inverse : une panne pendant le
    /// téléchargement laisse l'ancienne version en place plutôt que de casser l'instance). Les
    /// nouvelles dépendances cochées sont installées comme pour <see cref="ApplyAsync"/> : rien
    /// n'est jamais installé en silence.
    /// </summary>
    /// <param name="slug">Instance cible.</param>
    /// <param name="plan">Plan produit par <see cref="PrepareUpdateAsync"/>.</param>
    /// <param name="selectedDependencies">
    /// Identifiants des dépendances à installer. <see langword="null"/> ou vide n'en installe
    /// aucune : c'est un choix explicite de l'utilisateur, jamais un défaut implicite.
    /// </param>
    /// <param name="progress">Avancement du téléchargement.</param>
    /// <param name="cancellationToken">Annulation.</param>
    public async Task<ModInstallOutcome> ApplyUpdateAsync(
        string slug,
        ModUpdatePlan plan,
        IReadOnlyCollection<string>? selectedDependencies = null,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var wasEnabled = plan.Previous.IsEnabled;
        var updated = await InstallItemAsync(slug, plan.Updated, progress, cancellationToken).ConfigureAwait(false);
        if (!wasEnabled)
        {
            updated = await _repository.SetEnabledAsync(slug, updated, enabled: false, cancellationToken).ConfigureAwait(false);
        }

        // L'ancien fichier n'est retiré qu'APRÈS le succès du nouveau : si InstallItemAsync avait
        // échoué (téléchargement coupé, disque plein), on n'atteint jamais cette ligne et l'ancienne
        // version reste utilisable telle quelle.
        await _repository.RemoveAsync(slug, plan.Previous, cancellationToken).ConfigureAwait(false);

        var installed = new List<InstalledMod> { updated };
        var skipped = new List<string>();
        var selected = new HashSet<string>(selectedDependencies ?? [], StringComparer.OrdinalIgnoreCase);

        foreach (var dependency in plan.MissingDependencies)
        {
            if (selected.Contains(dependency.ModIdString))
            {
                installed.Add(await InstallItemAsync(slug, dependency, progress, cancellationToken).ConfigureAwait(false));
            }
            else
            {
                skipped.Add(dependency.ModIdString);
            }
        }

        return new ModInstallOutcome(installed, skipped);
    }

    /// <summary>
    /// Applique séquentiellement toutes les mises à jour disponibles d'une instance (bouton « Tout
    /// mettre à jour »). N'installe jamais de nouvelle dépendance : une dépendance nouvellement
    /// requise par une mise à jour est comptée comme non résolue plutôt qu'installée d'autorité
    /// (docs/architecture.md, « jamais en silence »), à traiter ensuite depuis la mise à jour
    /// individuelle de ce mod, qui montre le plan complet.
    /// </summary>
    /// <param name="slug">Instance cible.</param>
    /// <param name="updates">
    /// Résultats de <see cref="ModUpdateChecker.CheckAsync"/> ; seuls ceux avec
    /// <see cref="ModUpdateResult.HasUpdate"/> vrai sont traités, les autres sont ignorés.
    /// </param>
    /// <param name="progress">Avancement agrégé, un cran par mod traité.</param>
    /// <param name="cancellationToken">
    /// Annulation honorée dès la phase de PRÉPARATION (téléchargement, voir
    /// <see cref="PrepareUpdateAsync"/>) du mod en cours : le lot s'arrête proprement sans
    /// attendre le mod suivant, et le nettoyage du téléchargement interrompu est déjà géré par
    /// <see cref="Http.IDownloadManager"/>. La phase d'APPLICATION (remplacement de l'ancien
    /// fichier par le nouveau, voir <see cref="ApplyUpdateAsync"/>), elle, reste toujours menée
    /// jusqu'à son terme une fois entamée : jamais coupée au milieu d'un remplacement.
    /// </param>
    public async Task<BulkUpdateOutcome> ApplyAllUpdatesAsync(
        string slug,
        IReadOnlyList<ModUpdateResult> updates,
        IProgress<BulkUpdateProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(updates);

        var pending = updates.Where(result => result.HasUpdate).ToArray();
        var updated = new List<InstalledMod>();
        var failed = new List<BulkUpdateFailure>();

        for (var index = 0; index < pending.Length; index++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            var result = pending[index];
            progress?.Report(new BulkUpdateProgress(index, pending.Length, result.Mod.DisplayName));

            try
            {
                // Le jeton du lot atteint maintenant le téléchargement : une annulation peut
                // interrompre proprement le mod EN COURS, pas seulement celui d'après (c'était le
                // défaut corrigé ici — CancellationToken.None ne laissait jamais rien interrompre
                // avant ce point). La phase d'application reste volontairement sur
                // CancellationToken.None : une fois le nouveau fichier téléchargé et vérifié, le
                // remplacement va jusqu'au bout, jamais coupé en plein échange des deux fichiers.
                var plan = await PrepareUpdateAsync(slug, result, ModCompatibilityMode.ExactGameVersion, progress: null, cancellationToken).ConfigureAwait(false);
                var outcome = await ApplyUpdateAsync(slug, plan, selectedDependencies: null, progress: null, CancellationToken.None).ConfigureAwait(false);

                updated.Add(outcome.Installed[0]);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Annulation authentique du lot (pas un délai interne, voir la distinction faite
                // par ModDbClient.IsNetworkFailure) : le mod en préparation n'a pas été appliqué,
                // rien n'est à moitié posé sur le disque. Un arrêt propre, donc, jamais un échec à
                // consigner dans failed.
                break;
            }
            catch (Exception exception) when (exception is ModDbApiException or ModDbUnavailableException or DownloadFailedException or ModInstallFailedException)
            {
                failed.Add(new BulkUpdateFailure(result.Mod.DisplayName, exception.Message));
            }
        }

        progress?.Report(new BulkUpdateProgress(pending.Length, pending.Length, string.Empty));

        return new BulkUpdateOutcome(updated, failed);
    }

    /// <summary>
    /// Nom du fichier posé dans <c>data/Mods/</c> : <c>&lt;modid&gt;-&lt;version&gt;.zip</c>, la
    /// convention de VS Launcher, lisible et stable. Le nom publié sur le ModDB n'est jamais
    /// réutilisé tel quel : il est libre côté auteur, parfois préfixé (<c>ExtraInfo-v2.2.1.zip</c>
    /// pour une version qui vaut <c>2.2.1</c>), et vient d'une source distante.
    /// </summary>
    public static string BuildFileName(string modIdString, ModVersion version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modIdString);

        var safe = new string(modIdString.Where(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_').ToArray());

        return $"{(safe.Length == 0 ? "mod" : safe)}-{version}.zip";
    }

    private async Task<ModInstallItem?> BuildItemAsync(
        ModDbModDetail detail,
        GameVersion gameVersion,
        ModCompatibilityMode mode,
        CancellationToken cancellationToken)
    {
        var choice = ModReleaseSelector.Select(detail.Releases, gameVersion, mode);

        return choice is null ? null : await BuildItemAsync(detail.ModId, detail.Name, choice, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// La meilleure release publiée d'une fiche dont AUCUNE n'est déclarée compatible : la plus
    /// récente, marquée <see cref="ModReleaseCompatibility.NotDeclared"/> pour que rien, ni l'écran
    /// ni la provenance écrite ensuite, ne la fasse passer pour confirmée.
    /// </summary>
    private async Task<ModInstallItem?> BuildFallbackItemAsync(
        ModDbModDetail detail,
        GameVersion gameVersion,
        CancellationToken cancellationToken)
    {
        var candidates = ModReleaseSelector.SelectAll(
            detail.Releases,
            gameVersion,
            ModCompatibilityMode.WidenToMinorSeries,
            includeIncompatible: true);

        return candidates.Count == 0
            ? null
            : await BuildItemAsync(detail.ModId, detail.Name, candidates[0], cancellationToken).ConfigureAwait(false);
    }

    private async Task<ModInstallItem> BuildItemAsync(
        int modDbModId,
        string displayName,
        ModReleaseChoice choice,
        CancellationToken cancellationToken)
    {
        var size = await _client.GetFileSizeAsync(choice.Release.DownloadUrl, cancellationToken).ConfigureAwait(false);

        return new ModInstallItem(
            modDbModId,
            displayName,
            choice.Release,
            choice.IsApproximate,
            BuildFileName(choice.Release.ModIdString, choice.Release.Version),
            size)
        {
            Compatibility = choice.Compatibility,
        };
    }

    // Chemin de secours pour un mod mis à jour sans provenance préalable (déposé à la main, jamais
    // installé par Prospect) : /api/updates ne renvoie qu'un modidstr, jamais l'identifiant
    // numérique de la fiche. Best effort — cet identifiant n'est relu nulle part ailleurs dans le
    // domaine (voir ModProvenance.ModId), son absence ne doit donc jamais bloquer une mise à jour
    // par ailleurs prête.
    private async Task<int> ResolveModDbIdAsync(string modIdString, CancellationToken cancellationToken)
    {
        try
        {
            var detail = await _client.GetModAsync(modIdString, cancellationToken).ConfigureAwait(false);

            return detail.ModId;
        }
        catch (Exception exception) when (exception is ModDbApiException or ModDbUnavailableException)
        {
            return 0;
        }
    }

    private async Task<IReadOnlyList<string>> ResolveRemoteDependenciesAsync(string modIdString, GameVersion gameVersion, CancellationToken cancellationToken)
    {
        var information = await _client
            .GetInstallInformationAsync([modIdString], gameVersion, cancellationToken)
            .ConfigureAwait(false);

        return information
            .SelectMany(entry => entry.Value.ResolvedDependencies.Append(entry.Key))
            .Where(identifier => !string.Equals(identifier, modIdString, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <remarks>
    /// Les deux façons d'échouer sont RAPPORTÉES SÉPARÉMENT, et c'est tout l'objet de cette
    /// méthode. Elles étaient auparavant versées dans une même liste d'identifiants, que l'interface
    /// annonçait invariablement comme « introuvable sur le ModDB » — un mensonge dès que la fiche
    /// existait. Le cas qui l'a révélé : installer <c>carryon</c> 2.0.0-pre.8 (tagué jusqu'à 1.22.6)
    /// sur une instance en 1.22.6 déclarait <c>carryonlib</c> introuvable, alors que sa fiche existe
    /// (modid 4687) et que seules ses RELEASES s'arrêtent à 1.22.4. Vérifié en direct, et gardé par
    /// le test live du même nom.
    /// </remarks>
    private async Task<(IReadOnlyList<ModInstallItem> Items, IReadOnlyList<UnresolvedModDependency> Unresolved)> ResolveDependencyItemsAsync(
        IReadOnlyList<ModDependencyIssue> issues,
        GameVersion gameVersion,
        ModCompatibilityMode mode,
        CancellationToken cancellationToken)
    {
        var items = new List<ModInstallItem>();
        var unresolved = new List<UnresolvedModDependency>();

        foreach (var issue in issues.Where(issue => issue.NeedsInstall))
        {
            ModDbModDetail? detail = null;
            try
            {
                // L'endpoint détail résout indifféremment l'identifiant numérique et le modidstr
                // d'un modinfo.json, ce qui permet de partir d'une dépendance déclarée sans avoir
                // à deviner d'identifiant numérique.
                detail = await _client.GetModAsync(issue.ModIdString, cancellationToken).ConfigureAwait(false);
            }
            catch (ModDbApiException)
            {
                // Dépendance inconnue du ModDB : elle existe peut-être ailleurs, ou l'auteur s'est
                // trompé d'identifiant. Rien à installer, mais l'utilisateur doit le savoir.
            }

            if (detail is null)
            {
                unresolved.Add(new UnresolvedModDependency(issue.ModIdString, ModDependencyResolution.NotOnModDb));
                continue;
            }

            // La fiche existe. Si rien n'en sort, ce n'est donc PAS qu'elle est introuvable : c'est
            // qu'aucune de ses releases ne porte le tag de cette version de jeu. Verdict distinct,
            // et on sait même nommer le mod dont il s'agit.
            var item = await BuildItemAsync(detail, gameVersion, mode, cancellationToken).ConfigureAwait(false);
            if (item is not null)
            {
                items.Add(item);

                continue;
            }

            // Et la fiche a beau n'avoir aucune release déclarée compatible, elle a des releases :
            // celle du haut est proposée en dernier recours, décochée, avec ses vrais tags. Sans
            // ça, une dépendance dont l'auteur a juste oublié de cocher la dernière version du jeu
            // rendrait le mod qui en dépend impossible à installer.
            var fallback = await BuildFallbackItemAsync(detail, gameVersion, cancellationToken).ConfigureAwait(false);
            unresolved.Add(new UnresolvedModDependency(issue.ModIdString, ModDependencyResolution.NoCompatibleRelease, detail.Name)
            {
                BestAvailable = fallback,
            });
        }

        return (items, unresolved);
    }

    private async Task<InstalledMod> InstallItemAsync(
        string slug,
        ModInstallItem item,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        var archivePath = await DownloadAsync(item, progress, cancellationToken).ConfigureAwait(false);
        var modsDirectory = _repository.GetModsDirectory(slug);
        _fileSystem.Directory.CreateDirectory(modsDirectory);

        var target = _fileSystem.Path.Combine(modsDirectory, item.TargetFileName);
        _fileSystem.File.Copy(archivePath, target, overwrite: true);

        await _repository.SaveProvenanceAsync(
            slug,
            new ModProvenance
            {
                FileName = item.TargetFileName,
                ModId = item.ModDbModId,
                ModIdString = item.ModIdString,
                ReleaseId = item.Release.ReleaseId,
                FileId = item.Release.FileId,
                Version = item.Version,
                InstalledUtc = _clock.UtcNow,
                ApproximateMatch = item.IsApproximateMatch,
                DeclaredIncompatible = item.IsDeclaredIncompatible,
            },
            cancellationToken).ConfigureAwait(false);

        var installed = await _repository.ScanAsync(slug, cancellationToken).ConfigureAwait(false);

        return installed.FirstOrDefault(mod => string.Equals(mod.FileName, item.TargetFileName, StringComparison.OrdinalIgnoreCase))
            ?? throw new ModInstallFailedException($"Le fichier « {item.TargetFileName} » n'a pas été retrouvé après son installation.");
    }

    private async Task<string> DownloadAsync(ModInstallItem item, IProgress<DownloadProgress>? progress, CancellationToken cancellationToken)
    {
        var request = new DownloadRequest(item.DisplayName, item.TargetFileName, [item.Release.DownloadUrl]);
        var path = await _downloads.DownloadAsync(request, progress, cancellationToken).ConfigureAwait(false);

        VerifySize(item, path);

        return path;
    }

    // Le ModDB n'expose ni empreinte ni taille dans son JSON : comparer ce qu'a annoncé le CDN sur
    // un HEAD à ce qu'on a réellement écrit est tout ce dont on dispose pour ne pas installer une
    // archive tronquée.
    private void VerifySize(ModInstallItem item, string path)
    {
        if (item.AnnouncedSizeBytes is not { } expected || expected <= 0)
        {
            return;
        }

        var actual = _fileSystem.FileInfo.New(path).Length;
        if (actual == expected)
        {
            return;
        }

        try
        {
            _fileSystem.File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Le fichier suspect n'a pas pu être effacé : il sera de toute façon retéléchargé et
            // recontrôlé à la prochaine tentative.
        }

        throw ModInstallFailedException.ForSizeMismatch(item.TargetFileName, expected, actual);
    }
}