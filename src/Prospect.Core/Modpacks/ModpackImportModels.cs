using Prospect.Core.Common;
using Prospect.Core.GameVersions;
using Prospect.Core.Instances;

namespace Prospect.Core.Modpacks;

/// <summary>Forme de la source lue par <see cref="ModpackImportService.PreviewAsync"/>.</summary>
public enum ModpackSourceFormat
{
    /// <summary>Un fichier <c>.json</c> autonome.</summary>
    ManifestOnly,

    /// <summary>Une archive <c>.zip</c> contenant le manifest et, en option, <c>ModConfig/</c>.</summary>
    Archive,
}

/// <summary>
/// Aperçu d'un import avant exécution : ce que <see cref="ModpackImportService.PreviewAsync"/>
/// rend pour que l'UI montre le contenu du pack et son coût avant de demander confirmation. Rien
/// n'est encore créé sur le disque à ce stade, exactement comme <see cref="ModDb.ModInstallPlan"/>
/// pour l'installation d'un mod.
/// </summary>
/// <param name="SourcePath">Fichier choisi par l'utilisateur (manifest ou archive).</param>
/// <param name="Manifest">Manifest lu et déjà validé.</param>
/// <param name="SourceFormat">Manifest seul, ou archive.</param>
/// <param name="GameVersionInstalled">Vrai si la version de jeu du manifest est déjà installée.</param>
/// <param name="HasModConfig">Vrai si l'archive contient un dossier <c>ModConfig/</c> à poser.</param>
/// <param name="GameVersionDownloadSize">
/// Taille annoncée par le catalogue pour la version de jeu, telle qu'écrite par celui-ci (ex.
/// « 590.5 MB »), quand <paramref name="GameVersionInstalled"/> est faux. <see langword="null"/>
/// si la version est déjà installée, ou si le catalogue n'a pas pu être consulté : un confort
/// d'affichage, jamais un motif de bloquer l'aperçu.
/// </param>
public sealed record ModpackImportPreview(
    string SourcePath,
    ModpackManifest Manifest,
    ModpackSourceFormat SourceFormat,
    bool GameVersionInstalled,
    bool HasModConfig,
    string? GameVersionDownloadSize = null);

/// <summary>Étape courante d'un import de modpack.</summary>
public enum ModpackImportPhase
{
    /// <summary>Installation de la version de jeu manquante.</summary>
    InstallingGameVersion,

    /// <summary>Résolution et téléchargement des mods, un par un.</summary>
    InstallingMods,
}

/// <summary>Avancement d'un import, phase courante plus compteurs de mods.</summary>
/// <param name="Phase">Étape courante.</param>
/// <param name="CompletedMods">Mods déjà traités (installés ou en échec), celui en cours exclu.</param>
/// <param name="TotalMods">Nombre total de mods du manifest.</param>
/// <param name="CurrentModId">Identifiant du mod en cours de traitement, ou <see langword="null"/>.</param>
/// <param name="GameVersionProgress">Avancement détaillé pendant <see cref="ModpackImportPhase.InstallingGameVersion"/>.</param>
public sealed record ModpackImportProgress(
    ModpackImportPhase Phase,
    int CompletedMods,
    int TotalMods,
    string? CurrentModId,
    GameInstallProgress? GameVersionProgress);

/// <summary>Ce qui est arrivé à un mod du manifest pendant l'import.</summary>
public enum ModpackModImportStatus
{
    /// <summary>Téléchargé, vérifié et posé dans l'instance.</summary>
    Installed,

    /// <summary>Le <c>modId</c> du manifest est inconnu du ModDB.</summary>
    NotFound,

    /// <summary>Le mod est connu, mais aucune release ne correspond ni au <c>fileId</c> ni à la version exacte.</summary>
    VersionMissing,

    /// <summary>Le fichier téléchargé ne correspond pas au <c>sha256</c> porté par le manifest.</summary>
    Sha256Mismatch,

    /// <summary>Le ModDB ou le téléchargement n'a pas répondu.</summary>
    NetworkFailure,
}

/// <summary>
/// Ce qui est arrivé à UN mod du manifest, jamais une exception : le rapport groupe ces entrées par
/// catégorie plutôt que de faire échouer tout l'import pour un seul mod introuvable
/// (docs/architecture.md, feature 5).
/// </summary>
public sealed record ModpackImportModReport
{
    /// <summary>Identifiant demandé par le manifest.</summary>
    public required string ModId { get; init; }

    /// <summary>Version demandée par le manifest.</summary>
    public required ModVersion RequestedVersion { get; init; }

    /// <summary>Catégorie du résultat.</summary>
    public required ModpackModImportStatus Status { get; init; }

    /// <summary>
    /// Version réellement installée (renseignée seulement pour <see cref="ModpackModImportStatus.Installed"/>).
    /// Peut différer de <see cref="RequestedVersion"/> quand la résolution par <c>fileId</c> a
    /// mené vers une release dont la version a changé depuis l'export.
    /// </summary>
    public ModVersion? InstalledVersion { get; init; }

    /// <summary>
    /// Version la plus récente compatible avec la version de jeu du pack, à titre indicatif
    /// seulement : jamais installée automatiquement. Renseignée uniquement pour
    /// <see cref="ModpackModImportStatus.VersionMissing"/>.
    /// </summary>
    public ModVersion? SuggestedVersion { get; init; }

    /// <summary>Détail technique bref (message d'exception), pour le diagnostic plutôt que pour la voix produit.</summary>
    public string? Detail { get; init; }

    /// <summary>Construit une entrée de succès.</summary>
    public static ModpackImportModReport Installed(string modId, ModVersion requestedVersion, ModVersion installedVersion)
        => new()
        {
            ModId = modId,
            RequestedVersion = requestedVersion,
            Status = ModpackModImportStatus.Installed,
            InstalledVersion = installedVersion,
        };

    /// <summary>Construit une entrée « modId inconnu du ModDB ».</summary>
    public static ModpackImportModReport NotFound(string modId, ModVersion requestedVersion)
        => new() { ModId = modId, RequestedVersion = requestedVersion, Status = ModpackModImportStatus.NotFound };

    /// <summary>Construit une entrée « aucune release ne correspond », avec la suggestion la plus proche si elle existe.</summary>
    public static ModpackImportModReport VersionMissing(string modId, ModVersion requestedVersion, ModVersion? suggestedVersion)
        => new()
        {
            ModId = modId,
            RequestedVersion = requestedVersion,
            Status = ModpackModImportStatus.VersionMissing,
            SuggestedVersion = suggestedVersion,
        };

    /// <summary>Construit une entrée « empreinte invalide ».</summary>
    public static ModpackImportModReport Sha256Mismatch(string modId, ModVersion requestedVersion)
        => new() { ModId = modId, RequestedVersion = requestedVersion, Status = ModpackModImportStatus.Sha256Mismatch };

    /// <summary>Construit une entrée « échec réseau ».</summary>
    public static ModpackImportModReport NetworkFailure(string modId, ModVersion requestedVersion, string? detail)
        => new() { ModId = modId, RequestedVersion = requestedVersion, Status = ModpackModImportStatus.NetworkFailure, Detail = detail };
}

/// <summary>
/// Résultat final d'un import : l'instance créée (utilisable même si certains mods ont échoué) et
/// le rapport par mod.
/// </summary>
/// <param name="Instance">Instance créée.</param>
/// <param name="Mods">Une entrée par mod du manifest, dans l'ordre du manifest.</param>
public sealed record ModpackImportOutcome(InstanceRecord Instance, IReadOnlyList<ModpackImportModReport> Mods)
{
    /// <summary>Nombre de mods réellement installés.</summary>
    public int InstalledCount => Mods.Count(mod => mod.Status == ModpackModImportStatus.Installed);

    /// <summary>Vrai si au moins un mod n'a pas pu être installé.</summary>
    public bool HasIssues => Mods.Any(mod => mod.Status != ModpackModImportStatus.Installed);
}