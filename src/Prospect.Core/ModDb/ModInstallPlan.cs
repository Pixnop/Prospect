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
    /// <summary>
    /// Ce que l'auteur a déclaré de la compatibilité de cette release. Plus fin que
    /// <see cref="IsApproximateMatch"/>, qui ne distingue pas « supposée par la série mineure » de
    /// « pas déclarée du tout » — or seule la seconde exige un dévoilement et un avertissement.
    /// </summary>
    public ModReleaseCompatibility Compatibility { get; init; } =
        IsApproximateMatch ? ModReleaseCompatibility.SameMinorSeries : ModReleaseCompatibility.Declared;

    /// <summary>Vrai quand l'auteur n'a coché aucune version de cette série de jeu.</summary>
    public bool IsDeclaredIncompatible => Compatibility == ModReleaseCompatibility.NotDeclared;

    /// <summary>Identifiant modinfo.json, celui que porte la release.</summary>
    public string ModIdString => Release.ModIdString;

    /// <summary>Version qui sera installée.</summary>
    public ModVersion Version => Release.Version;
}

/// <summary>
/// Pourquoi une dépendance manquante n'a pas pu être ramenée à une release installable. Les deux
/// verdicts sont des faits DIFFÉRENTS et l'utilisateur n'en fait pas la même chose : les confondre
/// revenait à accuser le ModDB de ne pas connaître un mod qu'il publie.
/// </summary>
public enum ModDependencyResolution
{
    /// <summary>Le ModDB ne publie aucune fiche sous cet identifiant. Rien à en attendre.</summary>
    NotOnModDb,

    /// <summary>
    /// La fiche existe, mais aucune de ses releases n'est déclarée compatible avec la version de
    /// jeu de l'instance. Cas réel qui a motivé la distinction : <c>carryonlib</c> (fiche 4687)
    /// s'arrête à 1.22.4 alors que <c>carryon</c> 2.0.0-pre.8, qui en dépend, est tagué jusqu'à
    /// 1.22.6 ; installer ce dernier sur une instance en 1.22.6 annonçait « Introuvable sur le
    /// ModDB : carryonlib », ce qui est faux.
    /// </summary>
    NoCompatibleRelease,
}

/// <summary>Une dépendance manquante que la résolution n'a pas su ramener à une release.</summary>
/// <param name="ModIdString">Identifiant déclaré dans le <c>modinfo.json</c> du mod demandeur.</param>
/// <param name="Reason">Fiche absente, ou fiche présente sans release compatible.</param>
/// <param name="ModName">
/// Nom publié de la fiche quand elle existe, <see langword="null"/> quand elle n'existe pas — on ne
/// peut pas nommer ce qu'on n'a pas trouvé.
/// </param>
public sealed record UnresolvedModDependency(string ModIdString, ModDependencyResolution Reason, string? ModName = null)
{
    /// <summary>Comment la nommer à l'écran : le nom de la fiche si on l'a, son identifiant sinon.</summary>
    public string DisplayName => string.IsNullOrWhiteSpace(ModName) ? ModIdString : ModName;

    /// <summary>
    /// Meilleure release publiée de la fiche, quand elle existe : celle qu'on peut proposer
    /// d'installer QUAND MÊME. Toujours <see langword="null"/> pour
    /// <see cref="ModDependencyResolution.NotOnModDb"/>, où il n'y a rien à proposer.
    /// </summary>
    /// <remarks>
    /// Refuser tout net serait plus faux qu'utile : les tags de compatibilité sont des cases
    /// cochées à la main et prennent du retard. Le cas qui a motivé ceci est réel : CarryOnLib
    /// 1.0.0-pre.8 s'arrête à 1.22.4 alors que Carry On 2.0.0-pre.8, qui en dépend, est tagué
    /// jusqu'à 1.22.6. La ligne devient donc actionnable, et elle est proposée COCHÉE d'office. Le
    /// défaut se justifie par ce qu'est une dépendance déclarée : une librairie qu'un modinfo.json
    /// nomme est NÉCESSAIRE au fonctionnement du mod, donc la laisser de côté produit une
    /// installation qui a l'air d'avoir réussi et un jeu qui ne démarre pas. Le défaut doit être
    /// « ça marchera ». Le consentement reste entier pour autant : la case se décoche,
    /// l'avertissement qui nomme les versions réellement taguées reste attaché à la ligne, et
    /// l'installation marque toujours la provenance <see cref="ModProvenance.DeclaredIncompatible"/>.
    /// </remarks>
    public ModInstallItem? BestAvailable { get; init; }

    /// <summary>Versions de jeu que l'auteur a réellement cochées sur <see cref="BestAvailable"/>.</summary>
    public IReadOnlyList<string> BestAvailableGameVersions => BestAvailable?.Release.CompatibleGameVersionTags ?? [];
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
/// <param name="UnresolvedDependencies">
/// Dépendances pour lesquelles aucune release installable n'a été trouvée, chacune avec la RAISON
/// exacte (voir <see cref="ModDependencyResolution"/>).
/// </param>
/// <param name="GameVersion">Version de jeu de l'instance visée.</param>
public sealed record ModInstallPlan(
    ModInstallItem Primary,
    IReadOnlyList<ModInstallItem> MissingDependencies,
    IReadOnlyList<ModDependencyIssue> Issues,
    IReadOnlyList<UnresolvedModDependency> UnresolvedDependencies,
    GameVersion GameVersion)
{
    /// <summary>
    /// Toutes les releases installables dans cette instance, de la plus récente à la plus ancienne
    /// et exactes avant approximatives (voir <see cref="ModReleaseSelector.SelectAll"/>). Celle de
    /// <see cref="Primary"/> en fait partie ; c'est la première tant que l'utilisateur n'a rien
    /// choisi d'autre.
    /// </summary>
    /// <remarks>
    /// Le plan les porte plutôt que le dialogue parce que le filtre de compatibilité est du calcul
    /// de domaine : le dialogue n'a pas à savoir qu'un tag de version de jeu est une case cochée à
    /// la main par un auteur, ni ce que « élargir à la série mineure » veut dire.
    /// </remarks>
    public IReadOnlyList<ModReleaseChoice> AvailableReleases { get; init; } = [];

    /// <summary>
    /// La copie DÉJÀ installée du mod demandé, s'il y en a une, ou <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// Sa présence transforme l'installation en REMPLACEMENT, avec la discipline de la mise à jour
    /// (voir <see cref="ModInstallService.ApplyAsync"/>). Sans ce champ, réinstaller un mod ou en
    /// changer la version depuis le navigateur empilait un second zip à côté du premier :
    /// <c>configlib-1.10.0.zip</c> et <c>configlib-1.11.1.zip</c> dans le même <c>Mods/</c>, deux
    /// fois le même modid au chargement du jeu, comportement indéfini.
    /// </remarks>
    public InstalledMod? Existing { get; init; }

    /// <summary>Vrai quand appliquer ce plan remplacera une copie déjà installée.</summary>
    public bool IsReplacement => Existing is not null;

    /// <summary>
    /// Version actuellement installée du mod demandé, quand elle est lisible. <see langword="null"/>
    /// si aucune copie n'est installée ou si son <c>modinfo.json</c> n'a pas pu être lu.
    /// </summary>
    public ModVersion? ExistingVersion => Existing?.Info?.Version;

    /// <summary>
    /// Dépendances qu'on peut proposer d'installer malgré l'absence de compatibilité déclarée,
    /// chacune avec sa meilleure release publiée. Proposées COCHÉES d'office, pour la raison
    /// détaillée sur <see cref="UnresolvedModDependency.BestAvailable"/> : une dépendance déclarée
    /// est nécessaire au fonctionnement du mod, donc le défaut doit être « ça marchera ». Décochable
    /// ligne à ligne, avertissement compris.
    /// </summary>
    public IReadOnlyList<UnresolvedModDependency> InstallableAnyway
        => [.. UnresolvedDependencies.Where(dependency => dependency.BestAvailable is not null)];

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