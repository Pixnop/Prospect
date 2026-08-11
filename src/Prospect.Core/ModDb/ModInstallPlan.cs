using Prospect.Core.Common;

namespace Prospect.Core.ModDb;

/// <summary>Un mod prêt à être posé dans <c>data/Mods/</c>, avec la release retenue et son nom de fichier cible.</summary>
/// <param name="ModDbModId">Identifiant numérique de la fiche, écrit dans la provenance.</param>
/// <param name="DisplayName">Nom affiché du mod.</param>
/// <param name="Release">Release choisie.</param>
/// <param name="IsApproximateMatch">Vrai si la release a été retenue par élargissement à la série mineure.</param>
/// <param name="TargetFileName">Nom du fichier dans <c>data/Mods/</c>, convention <c>&lt;modid&gt;-&lt;version&gt;.zip</c>.</param>
/// <param name="AnnouncedSizeBytes">Taille annoncée par le CDN, ou <see langword="null"/> s'il n'en annonce pas.</param>
public sealed record ModInstallItem(
    int ModDbModId,
    string DisplayName,
    ModDbRelease Release,
    bool IsApproximateMatch,
    string TargetFileName,
    long? AnnouncedSizeBytes)
{
    /// <summary>Identifiant modinfo.json, celui que porte la release.</summary>
    public string ModIdString => Release.ModIdString;

    /// <summary>Version qui sera installée.</summary>
    public ModVersion Version => Release.Version;
}

/// <summary>
/// Ce que Prospect propose de faire pour installer un mod. Rien n'est encore posé dans l'instance à
/// ce stade : le plan est montré à l'utilisateur, qui coche ce qu'il veut, et
/// <see cref="ModInstallService.ApplyAsync"/> exécute ensuite. Aucune dépendance n'est jamais
/// installée en silence (docs/architecture.md, « Dépendances déclarées »).
/// </summary>
/// <param name="Primary">Le mod demandé.</param>
/// <param name="MissingDependencies">Dépendances déclarées absentes, résolues en releases installables.</param>
/// <param name="Issues">
/// Toutes les dépendances problématiques, y compris celles que la résolution n'a pas su ramener à
/// une release (mod inconnu du ModDB, aucune release compatible) et celles simplement désactivées.
/// </param>
/// <param name="UnresolvedDependencies">Identifiants pour lesquels aucune release installable n'a été trouvée.</param>
/// <param name="GameVersion">Version de jeu de l'instance visée.</param>
public sealed record ModInstallPlan(
    ModInstallItem Primary,
    IReadOnlyList<ModInstallItem> MissingDependencies,
    IReadOnlyList<ModDependencyIssue> Issues,
    IReadOnlyList<string> UnresolvedDependencies,
    GameVersion GameVersion)
{
    /// <summary>Vrai s'il y a quoi que ce soit à montrer avant d'installer.</summary>
    public bool NeedsConfirmation => MissingDependencies.Count > 0 || UnresolvedDependencies.Count > 0 || Primary.IsApproximateMatch;
}

/// <summary>Ce qui a réellement été installé.</summary>
/// <param name="Installed">Mods posés dans <c>data/Mods/</c>, le mod demandé en premier.</param>
/// <param name="SkippedDependencies">Dépendances proposées que l'utilisateur n'a pas cochées.</param>
public sealed record ModInstallOutcome(
    IReadOnlyList<InstalledMod> Installed,
    IReadOnlyList<string> SkippedDependencies);

/// <summary>
/// Ce que retirer un mod impliquerait. La confirmation NOMME les mods qui le déclarent en
/// dépendance : « BetterRuins et Extra Info en dépendent » est actionnable, « des mods en dépendent
/// peut-être » ne l'est pas.
/// </summary>
/// <param name="Target">Le mod visé.</param>
/// <param name="Dependents">Mods installés dont une dépendance déclarée pointe vers lui.</param>
public sealed record ModUninstallImpact(InstalledMod Target, IReadOnlyList<InstalledMod> Dependents)
{
    /// <summary>Vrai si au moins un mod installé casserait.</summary>
    public bool HasDependents => Dependents.Count > 0;

    /// <summary>Noms des mods dépendants, pour le texte de confirmation.</summary>
    public IReadOnlyList<string> DependentNames => Dependents.Select(mod => mod.DisplayName).ToArray();
}