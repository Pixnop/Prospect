using Prospect.Core.Common;

namespace Prospect.Core.ModDb;

/// <summary>Dans quel état se trouve une dépendance déclarée par rapport à ce qui est installé.</summary>
public enum ModDependencyStatus
{
    /// <summary>Un mod actif et assez récent la satisfait.</summary>
    Satisfied,

    /// <summary>Aucun mod installé ne porte cet identifiant.</summary>
    Missing,

    /// <summary>Le mod est installé mais désactivé : le jeu ne le chargera pas.</summary>
    Disabled,

    /// <summary>Le mod est installé et actif, mais sa version est inférieure à la borne minimale.</summary>
    TooOld,
}

/// <summary>Une dépendance déclarée et son état vis-à-vis de l'instance.</summary>
/// <param name="ModIdString">Identifiant modinfo.json de la dépendance.</param>
/// <param name="Requirement">Borne minimale déclarée.</param>
/// <param name="Status">Ce qui manque, ou <see cref="ModDependencyStatus.Satisfied"/>.</param>
/// <param name="InstalledVersion">Version présente sur le disque, ou <see langword="null"/>.</param>
/// <param name="ReportedByModDb">Vrai si <c>resolve-deps</c> a aussi signalé cet identifiant.</param>
public sealed record ModDependencyIssue(
    string ModIdString,
    VersionRequirement Requirement,
    ModDependencyStatus Status,
    ModVersion? InstalledVersion,
    bool ReportedByModDb)
{
    /// <summary>Vrai si installer ce mod ferait avancer les choses (le réinstaller ne sert à rien s'il est juste désactivé).</summary>
    public bool NeedsInstall => Status is ModDependencyStatus.Missing or ModDependencyStatus.TooOld;
}

/// <summary>
/// Croise les dépendances DÉCLARÉES d'un mod avec ce qui est réellement installé dans l'instance.
/// Pur calcul, sans réseau ni disque : le service d'installation lui fournit d'un côté le
/// <c>modinfo.json</c> du zip téléchargé, de l'autre le scan de <c>data/Mods/</c>.
/// </summary>
/// <remarks>
/// Deux sources alimentent la détection, et l'ordre d'autorité compte. La VÉRIFICATION LOCALE fait
/// foi : elle lit le <c>modinfo.json</c> réellement téléchargé, donc ce que le jeu lira. Le
/// <c>resolve-deps</c> de <c>/api/v2/mods/install-information</c> vient en complément : il voit les
/// dépendances transitives, mais la recherche n'a pas pu observer sa forme exacte en direct, et
/// rien ne garantit qu'il connaisse les mods déposés à la main. Un identifiant signalé par lui et
/// absent en local remonte quand même, marqué comme tel.
/// </remarks>
public static class ModDependencyResolver
{
    /// <summary>
    /// L'état de chaque dépendance déclarée par <paramref name="candidate"/>. Les identifiants
    /// spéciaux <c>game</c>, <c>survival</c> et <c>creative</c> sont écartés, comme le fait le
    /// ModDB lui-même : le premier alimente la compatibilité de version de jeu, les deux autres
    /// désignent du contenu livré avec le jeu.
    /// </summary>
    /// <param name="candidate">Métadonnées du mod à installer, ou <see langword="null"/> si son archive était illisible.</param>
    /// <param name="installed">Mods déjà présents dans l'instance, actifs et désactivés.</param>
    /// <param name="reportedByModDb">Identifiants remontés par <c>resolve-deps</c>, éventuellement vide.</param>
    public static IReadOnlyList<ModDependencyIssue> Analyze(
        ModInfo? candidate,
        IReadOnlyList<InstalledMod> installed,
        IReadOnlyCollection<string>? reportedByModDb = null)
    {
        ArgumentNullException.ThrowIfNull(installed);

        var remote = new HashSet<string>(reportedByModDb ?? [], StringComparer.OrdinalIgnoreCase);
        var byIdentity = IndexByIdentity(installed);
        var issues = new List<ModDependencyIssue>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var declared = candidate?.ModDependencies ?? new Dictionary<string, VersionRequirement>(StringComparer.OrdinalIgnoreCase);
        foreach (var (identifier, requirement) in declared)
        {
            seen.Add(identifier);
            issues.Add(Evaluate(identifier, requirement, byIdentity, remote.Contains(identifier)));
        }

        // Ce que le serveur a résolu en plus : dépendances transitives, ou dépendances d'un mod
        // dont l'archive n'a pas pu être lue.
        foreach (var identifier in remote.Where(identifier => !seen.Contains(identifier) && !ModInfoParser.IsSpecialDependencyId(identifier)))
        {
            issues.Add(Evaluate(identifier, VersionRequirement.Parse(null), byIdentity, reportedByModDb: true));
        }

        return issues
            .OrderBy(issue => issue.ModIdString, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>Les seules dépendances qui posent problème, dans l'ordre d'affichage.</summary>
    public static IReadOnlyList<ModDependencyIssue> FindUnsatisfied(
        ModInfo? candidate,
        IReadOnlyList<InstalledMod> installed,
        IReadOnlyCollection<string>? reportedByModDb = null)
        => Analyze(candidate, installed, reportedByModDb)
            .Where(issue => issue.Status != ModDependencyStatus.Satisfied)
            .ToArray();

    /// <summary>
    /// Vérification INVERSE, pour la désinstallation : les mods installés dont une dépendance
    /// déclarée pointe vers <paramref name="target"/>. La confirmation les NOMME plutôt que de
    /// servir un avertissement générique, exactement comme le fait déjà la désinstallation d'une
    /// version du jeu.
    /// </summary>
    public static IReadOnlyList<InstalledMod> FindDependents(InstalledMod target, IReadOnlyList<InstalledMod> installed)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(installed);

        var identity = target.Identity;

        return installed
            .Where(candidate => !ReferenceEquals(candidate, target)
                && !string.Equals(candidate.FilePath, target.FilePath, StringComparison.Ordinal)
                && candidate.Info is { } info
                && info.ModDependencies.ContainsKey(identity))
            .OrderBy(candidate => candidate.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static ModDependencyIssue Evaluate(
        string identifier,
        VersionRequirement requirement,
        Dictionary<string, InstalledMod> byIdentity,
        bool reportedByModDb)
    {
        if (!byIdentity.TryGetValue(identifier, out var installed))
        {
            return new ModDependencyIssue(identifier, requirement, ModDependencyStatus.Missing, null, reportedByModDb);
        }

        var version = installed.Version;

        // L'ordre compte : un mod désactivé ne sera pas chargé par le jeu, sa version n'a donc
        // aucune importance tant qu'il n'est pas réactivé.
        if (!installed.IsEnabled)
        {
            return new ModDependencyIssue(identifier, requirement, ModDependencyStatus.Disabled, version, reportedByModDb);
        }

        // Version illisible : on ne peut ni affirmer ni infirmer la contrainte. Le mod est là et
        // actif, on ne va pas inventer une incompatibilité, sauf si une borne est explicitement
        // exigée, auquel cas on ne peut pas la déclarer satisfaite non plus.
        if (version is null)
        {
            return new ModDependencyIssue(
                identifier,
                requirement,
                requirement.IsAny ? ModDependencyStatus.Satisfied : ModDependencyStatus.TooOld,
                null,
                reportedByModDb);
        }

        return new ModDependencyIssue(
            identifier,
            requirement,
            requirement.IsSatisfiedBy(version.Value) ? ModDependencyStatus.Satisfied : ModDependencyStatus.TooOld,
            version,
            reportedByModDb);
    }

    // Un mod peut être connu sous son modid de modinfo.json ou, s'il n'a pas pu être lu, sous son
    // identifiant de provenance : les deux sont indexés pour que la résolution fonctionne même sur
    // une archive dont le modinfo est illisible.
    private static Dictionary<string, InstalledMod> IndexByIdentity(IReadOnlyList<InstalledMod> installed)
    {
        var index = new Dictionary<string, InstalledMod>(StringComparer.OrdinalIgnoreCase);

        foreach (var mod in installed)
        {
            index.TryAdd(mod.Identity, mod);

            if (mod.Provenance is { } provenance)
            {
                index.TryAdd(provenance.ModIdString, mod);
            }
        }

        return index;
    }
}