using Prospect.Core.Common;
using Prospect.Core.Instances;

namespace Prospect.Core.ModDb;

/// <summary>
/// Ce que la vérification a établi pour un mod installé, vis-à-vis du ModDB. Cinq états, parce que
/// chacun est un fait différent : l'absence de nouvelle release, l'absence d'information et le
/// refus de compatibilité ne se confondent pas (docs/research/moddb-api.md, piège
/// absence-vs-à-jour de <c>/api/updates</c>).
/// </summary>
public enum ModUpdateStatus
{
    /// <summary>La release installée est la plus récente compatible connue.</summary>
    UpToDate,

    /// <summary>Une release plus récente, compatible avec la version de jeu de l'instance, est disponible.</summary>
    UpdateAvailable,

    /// <summary>
    /// Le ModDB a signalé une release PLUS RÉCENTE, mais aucun de ses tags ne la rattache à la
    /// version de jeu de l'instance.
    /// </summary>
    /// <remarks>
    /// Ce verdict est né d'un défaut de terrain, et son absence était le défaut lui-même : ce cas
    /// était rendu « à jour ». Sur une version de jeu fraîchement sortie, presque aucune release
    /// n'est encore cochée pour elle — les tags sont des cases que l'auteur coche à la main, à
    /// l'upload. Toutes les mises à jour réellement publiées repassaient donc en « à jour », et
    /// « Vérifier les mises à jour » rendait invariablement le même verdict vide : le bouton avait
    /// l'air de ne rien faire. Dire « rien de nouveau » quand le serveur vient de répondre le
    /// contraire est le seul mensonge que cette énumération ne peut pas se permettre.
    /// </remarks>
    UpdateNotDeclaredForThisVersion,

    /// <summary>
    /// Absent de la réponse de <c>/api/updates</c> ET sans provenance ModDB : rien ne permet
    /// d'affirmer que ce <c>modidstr</c> est même connu du ModDB, encore moins qu'il est à jour.
    /// </summary>
    UnknownToModDb,

    /// <summary>Le modinfo.json n'a pas pu être lu, ou n'a livré aucune version exploitable : aucune vérification possible.</summary>
    Unidentifiable,
}

/// <summary>État d'un mod installé à l'issue d'une vérification.</summary>
/// <param name="Mod">Mod tel que vu par le dernier scan.</param>
/// <param name="Status">Verdict.</param>
/// <param name="AvailableRelease">
/// Release signalée par le ModDB. Renseignée pour <see cref="ModUpdateStatus.UpdateAvailable"/>
/// comme pour <see cref="ModUpdateStatus.UpdateNotDeclaredForThisVersion"/> : dans les deux cas il y
/// a une version à nommer, et dans le second ses tags sont précisément ce qu'il faut montrer.
/// </param>
/// <param name="IsApproximateMatch">Vrai si la release n'a été retenue qu'en élargissant à la série mineure.</param>
/// <param name="AnnouncedSizeBytes">Taille annoncée par le CDN pour <paramref name="AvailableRelease"/>, via <c>HEAD</c>.</param>
public sealed record ModUpdateResult(
    InstalledMod Mod,
    ModUpdateStatus Status,
    ModDbRelease? AvailableRelease = null,
    bool IsApproximateMatch = false,
    long? AnnouncedSizeBytes = null)
{
    /// <summary>Raccourci pour le seul état qui appelle une action en un clic.</summary>
    public bool HasUpdate => Status == ModUpdateStatus.UpdateAvailable;

    /// <summary>
    /// Vrai quand une release plus récente existe sans être déclarée pour cette version de jeu.
    /// Actionnable aussi, mais pas d'un clic : elle passe par le dialogue d'installation et son
    /// avertissement.
    /// </summary>
    public bool HasUndeclaredUpdate => Status == ModUpdateStatus.UpdateNotDeclaredForThisVersion;

    /// <summary>Versions de jeu que l'auteur a réellement cochées sur la release signalée.</summary>
    public IReadOnlyList<string> AvailableGameVersions => AvailableRelease?.CompatibleGameVersionTags ?? [];
}

/// <summary>Résultat d'une vérification pour une instance entière, daté.</summary>
/// <param name="Mods">Un résultat par mod scanné, actifs et désactivés confondus.</param>
/// <param name="CheckedUtc">Horodatage de la vérification, pour l'affichage humanisé et le cache par instance.</param>
public sealed record InstanceUpdateReport(IReadOnlyList<ModUpdateResult> Mods, DateTimeOffset CheckedUtc)
{
    /// <summary>Nombre de mods pour lesquels une mise à jour est proposée.</summary>
    public int UpdateCount => Mods.Count(mod => mod.HasUpdate);

    /// <summary>Vrai si au moins un mod a une mise à jour disponible.</summary>
    public bool HasUpdates => UpdateCount > 0;

    /// <summary>Nombre de mods dont la release plus récente n'est pas déclarée pour cette version de jeu.</summary>
    public int UndeclaredUpdateCount => Mods.Count(mod => mod.HasUndeclaredUpdate);

    /// <summary>Vrai si au moins un mod est dans ce cas.</summary>
    public bool HasUndeclaredUpdates => UndeclaredUpdateCount > 0;

    /// <summary>
    /// Résumé d'une ligne, pour le journal de diagnostic : ce qu'une vérification a réellement
    /// conclu, verdict par verdict. C'est ce que « ça ne marche pas » doit pouvoir devenir dans un
    /// rapport de terrain.
    /// </summary>
    public string Summary => string.Create(
        System.Globalization.CultureInfo.InvariantCulture,
        $"{Mods.Count} mod(s) : {UpdateCount} à jour disponible, {UndeclaredUpdateCount} plus récent non déclaré, "
        + $"{Mods.Count(mod => mod.Status == ModUpdateStatus.UpToDate)} à jour, "
        + $"{Mods.Count(mod => mod.Status == ModUpdateStatus.UnknownToModDb)} inconnu du ModDB, "
        + $"{Mods.Count(mod => mod.Status == ModUpdateStatus.Unidentifiable)} illisible");
}

/// <summary>
/// Détecte les mises à jour disponibles pour les mods installés d'une instance
/// (docs/architecture.md, « 4. Client ModDB », section détection de mises à jour).
/// </summary>
/// <remarks>
/// <para>
/// Un seul appel à <c>/api/updates</c> pour toute l'instance : la liste <c>modidstr@version</c> ne
/// couvre que les mods IDENTIFIABLES (modinfo.json lisible, version exploitable) ; les autres sont
/// exclus de la requête et remontés directement en <see cref="ModUpdateStatus.Unidentifiable"/>.
/// </para>
/// <para>
/// L'endpoint ne renvoie qu'UNE release par <c>modidstr</c> (la plus récente toutes versions de jeu
/// confondues, jamais filtrée par version cible côté serveur) : le filtrage par compatibilité
/// (tags exacts, ou série mineure élargie) est donc fait ICI, en rejouant
/// <see cref="ModReleaseSelector"/> sur cette unique candidate. Une conséquence assumée : si la
/// release la plus récente d'un mod n'est pas tagée pour la version de jeu de l'instance alors
/// qu'une release intermédiaire l'aurait été, cette release intermédiaire n'est pas visible depuis
/// <c>/api/updates</c> et le mod ressort « à jour » plutôt que « mise à jour disponible mais plus
/// ancienne ». Aller la chercher demanderait un appel <c>/api/mod/{id}</c> par mod, ce qui perdrait
/// exactement l'intérêt du seul appel groupé ; documenté ici plutôt que traité en silence.
/// </para>
/// </remarks>
public sealed class ModUpdateChecker
{
    private readonly IModDbClient _client;
    private readonly IInstalledModRepository _repository;
    private readonly IInstanceRepository _instances;
    private readonly IClock _clock;
    private readonly IAppLog _log;

    /// <summary>Construit le vérificateur.</summary>
    /// <param name="client">Client ModDB.</param>
    /// <param name="repository">Mods installés de l'instance.</param>
    /// <param name="instances">Instances, pour connaître la version de jeu visée.</param>
    /// <param name="clock">Horloge, pour horodater le résultat.</param>
    /// <param name="log">
    /// Journal de diagnostic. Une vérification consigne son verdict COMPTÉ et, en cas d'échec, sa
    /// raison : « ça ne marche pas » est un rapport que rien ne permettait d'instruire.
    /// </param>
    public ModUpdateChecker(
        IModDbClient client,
        IInstalledModRepository repository,
        IInstanceRepository instances,
        IClock clock,
        IAppLog log)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(instances);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(log);

        _client = client;
        _repository = repository;
        _instances = instances;
        _clock = clock;
        _log = log;
    }

    /// <summary>
    /// Vérifie tous les mods installés d'une instance en un seul appel réseau.
    /// </summary>
    /// <param name="slug">Instance cible.</param>
    /// <param name="mode">Strict (tags exacts), ou élargi à la série mineure du jeu.</param>
    /// <param name="cancellationToken">Annulation.</param>
    /// <exception cref="ModDbApiException">Requête rejetée par le ModDB.</exception>
    /// <exception cref="ModDbUnavailableException">ModDB injoignable.</exception>
    public async Task<InstanceUpdateReport> CheckAsync(
        string slug,
        ModCompatibilityMode mode = ModCompatibilityMode.ExactGameVersion,
        CancellationToken cancellationToken = default)
    {
        _log.Write(AppLogLevel.Info, $"Mises à jour : vérification de « {slug} » demandée.");

        try
        {
            var instance = await _instances.LoadAsync(slug, cancellationToken).ConfigureAwait(false);
            var gameVersion = instance.Metadata.GameVersion;
            var installed = await _repository.ScanAsync(slug, cancellationToken).ConfigureAwait(false);

            var query = BuildQuery(installed);
            var updates = await _client.GetUpdatesAsync(query, cancellationToken).ConfigureAwait(false);

            var results = new List<ModUpdateResult>(installed.Count);
            foreach (var mod in installed)
            {
                results.Add(await EvaluateAsync(mod, updates, gameVersion, mode, cancellationToken).ConfigureAwait(false));
            }

            var report = new InstanceUpdateReport(results, _clock.UtcNow);
            _log.Write(AppLogLevel.Info, $"Mises à jour : « {slug} » en {gameVersion}, {report.Summary}.");

            return report;
        }
        catch (OperationCanceledException)
        {
            // Une annulation n'est pas un échec : elle vient de l'utilisateur ou de la fermeture
            // d'un écran, et elle n'a rien à faire dans un journal de diagnostic.
            throw;
        }
        catch (Exception exception)
        {
            // Large et RELANCÉ : le journal n'attrape rien, il ne fait que noter la raison au
            // passage. C'est ce qui manquait pour instruire un « ça ne marche pas », y compris quand
            // l'échec vient d'un chemin que l'appelant ne rattrape pas.
            _log.Write(AppLogLevel.Error, $"Mises à jour : échec de la vérification de « {slug} » ({exception.GetType().Name}) : {exception.Message}");

            throw;
        }
    }

    // Pessimiste à dessein : si deux fichiers installés partagent le même modidstr (typiquement un
    // actif et un désactivé laissé en place après une mise à jour manuelle), envoyer la version la
    // PLUS ANCIENNE des deux maximise la chance que le serveur signale ce modidstr comme en retard.
    // Ça ne fausse aucun résultat individuel : la comparaison qui décide du statut de CHAQUE mod
    // reste locale, dans EvaluateAsync, contre la release réellement reçue.
    private static Dictionary<string, ModVersion> BuildQuery(IReadOnlyList<InstalledMod> installed)
    {
        var query = new Dictionary<string, ModVersion>(StringComparer.OrdinalIgnoreCase);
        foreach (var mod in installed)
        {
            if (!mod.IsIdentified || mod.Version is not { } version)
            {
                continue;
            }

            if (!query.TryGetValue(mod.Identity, out var existing) || version < existing)
            {
                query[mod.Identity] = version;
            }
        }

        return query;
    }

    private async Task<ModUpdateResult> EvaluateAsync(
        InstalledMod mod,
        IReadOnlyDictionary<string, ModDbRelease> updates,
        GameVersion gameVersion,
        ModCompatibilityMode mode,
        CancellationToken cancellationToken)
    {
        if (!mod.IsIdentified || mod.Version is not { } version)
        {
            return new ModUpdateResult(mod, ModUpdateStatus.Unidentifiable);
        }

        if (!updates.TryGetValue(mod.Identity, out var candidate))
        {
            // Le piège documenté par la recherche : l'absence dans la réponse ne veut PAS dire « à
            // jour », seulement « pas signalé en retard ». La provenance est le seul fait qui
            // confirme que ce modidstr est un vrai mod du ModDB (Prospect l'a lui-même installé
            // depuis là) ; sans elle, l'absence reste indécidable.
            return new ModUpdateResult(
                mod,
                mod.Provenance is not null ? ModUpdateStatus.UpToDate : ModUpdateStatus.UnknownToModDb);
        }

        var choice = ModReleaseSelector.Select([candidate], gameVersion, mode);
        if (choice is null)
        {
            // Aucun tag ne rattache la candidate à cette version de jeu. Deux situations très
            // différentes se cachaient ici sous le même verdict « à jour », et c'est la première qui
            // faisait passer la vérification pour inopérante : le serveur vient de signaler une
            // release PLUS RÉCENTE que celle installée. La compatibilité manque, la mise à jour non.
            return candidate.Version > version
                ? new ModUpdateResult(mod, ModUpdateStatus.UpdateNotDeclaredForThisVersion, candidate)
                : new ModUpdateResult(mod, ModUpdateStatus.UpToDate);
        }

        if (choice.Release.Version <= version)
        {
            return new ModUpdateResult(mod, ModUpdateStatus.UpToDate);
        }

        var size = await _client.GetFileSizeAsync(choice.Release.DownloadUrl, cancellationToken).ConfigureAwait(false);

        return new ModUpdateResult(mod, ModUpdateStatus.UpdateAvailable, choice.Release, choice.IsApproximate, size);
    }
}