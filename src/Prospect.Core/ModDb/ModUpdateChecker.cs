using Prospect.Core.Common;
using Prospect.Core.Instances;

namespace Prospect.Core.ModDb;

/// <summary>
/// Ce que la vérification a établi pour un mod installé, vis-à-vis du ModDB. Quatre états, pas
/// trois : l'absence de nouvelle release et l'absence d'information ne sont pas la même chose
/// (docs/research/moddb-api.md, piège absence-vs-à-jour de <c>/api/updates</c>).
/// </summary>
public enum ModUpdateStatus
{
    /// <summary>La release installée est la plus récente compatible connue.</summary>
    UpToDate,

    /// <summary>Une release plus récente, compatible avec la version de jeu de l'instance, est disponible.</summary>
    UpdateAvailable,

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
/// <param name="AvailableRelease">Release proposée si <paramref name="Status"/> vaut <see cref="ModUpdateStatus.UpdateAvailable"/>.</param>
/// <param name="IsApproximateMatch">Vrai si la release n'a été retenue qu'en élargissant à la série mineure.</param>
/// <param name="AnnouncedSizeBytes">Taille annoncée par le CDN pour <paramref name="AvailableRelease"/>, via <c>HEAD</c>.</param>
public sealed record ModUpdateResult(
    InstalledMod Mod,
    ModUpdateStatus Status,
    ModDbRelease? AvailableRelease = null,
    bool IsApproximateMatch = false,
    long? AnnouncedSizeBytes = null)
{
    /// <summary>Raccourci pour le seul état qui appelle une action.</summary>
    public bool HasUpdate => Status == ModUpdateStatus.UpdateAvailable;
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

    /// <summary>Construit le vérificateur.</summary>
    /// <param name="client">Client ModDB.</param>
    /// <param name="repository">Mods installés de l'instance.</param>
    /// <param name="instances">Instances, pour connaître la version de jeu visée.</param>
    /// <param name="clock">Horloge, pour horodater le résultat.</param>
    public ModUpdateChecker(
        IModDbClient client,
        IInstalledModRepository repository,
        IInstanceRepository instances,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(instances);
        ArgumentNullException.ThrowIfNull(clock);

        _client = client;
        _repository = repository;
        _instances = instances;
        _clock = clock;
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

        return new InstanceUpdateReport(results, _clock.UtcNow);
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
        if (choice is null || choice.Release.Version <= version)
        {
            return new ModUpdateResult(mod, ModUpdateStatus.UpToDate);
        }

        var size = await _client.GetFileSizeAsync(choice.Release.DownloadUrl, cancellationToken).ConfigureAwait(false);

        return new ModUpdateResult(mod, ModUpdateStatus.UpdateAvailable, choice.Release, choice.IsApproximate, size);
    }
}
