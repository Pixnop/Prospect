using Prospect.Core.Instances;
using Prospect.Core.Storage;

namespace Prospect.Core.Migration;

/// <summary>Ce qui est arrivé à UNE installation VSL sélectionnée pendant l'adoption.</summary>
public enum VslInstallationAdoptionStatus
{
    /// <summary>Instance Prospect créée, données copiées, métadonnées reprises.</summary>
    Adopted,

    /// <summary>Non tentée : version illisible, dossier source introuvable, ou nom inexploitable.</summary>
    Skipped,

    /// <summary>Tentée, mais la copie ou la sauvegarde des métadonnées a échoué (E/S).</summary>
    Failed,
}

/// <summary>
/// Ce qui est arrivé à UNE installation VSL du lot soumis à <see cref="VslAdoptionService.AdoptAsync"/>,
/// jamais une exception : même philosophie que l'import de modpack
/// (<see cref="Prospect.Core.Modpacks.ModpackImportModReport"/>), un élément en échec n'interrompt
/// jamais le traitement des suivants.
/// </summary>
public sealed record VslInstallationAdoptionReport
{
    /// <summary>Nom d'origine côté VS Launcher (ou son identifiant, si le nom était vide), pour l'affichage même en cas d'échec.</summary>
    public required string SourceName { get; init; }

    /// <summary>Catégorie du résultat.</summary>
    public required VslInstallationAdoptionStatus Status { get; init; }

    /// <summary>Instance créée, renseignée uniquement pour <see cref="VslInstallationAdoptionStatus.Adopted"/>.</summary>
    public InstanceRecord? Instance { get; init; }

    /// <summary>Raison courte de l'ignorance ou de l'échec, à destination de l'utilisateur.</summary>
    public string? Detail { get; init; }

    /// <summary>Construit une entrée de succès.</summary>
    public static VslInstallationAdoptionReport Adopted(string sourceName, InstanceRecord instance)
        => new() { SourceName = sourceName, Status = VslInstallationAdoptionStatus.Adopted, Instance = instance };

    /// <summary>Construit une entrée « non tentée ».</summary>
    public static VslInstallationAdoptionReport Skipped(string sourceName, string reason)
        => new() { SourceName = sourceName, Status = VslInstallationAdoptionStatus.Skipped, Detail = reason };

    /// <summary>Construit une entrée « échec d'E/S ».</summary>
    public static VslInstallationAdoptionReport Failed(string sourceName, string reason)
        => new() { SourceName = sourceName, Status = VslInstallationAdoptionStatus.Failed, Detail = reason };
}

/// <summary>Ce qui est arrivé à UN moteur VSL sélectionné pendant l'adoption.</summary>
public enum VslEngineAdoptionStatus
{
    /// <summary>Fichiers copiés, sentinelle posée, provenance marquée.</summary>
    Adopted,

    /// <summary>Non tentée : version illisible, déjà installée côté Prospect, ou dossier source introuvable.</summary>
    Skipped,

    /// <summary>Tentée, mais la copie a échoué (E/S).</summary>
    Failed,
}

/// <summary>Ce qui est arrivé à UN moteur du lot soumis à <see cref="VslAdoptionService.AdoptAsync"/>.</summary>
public sealed record VslEngineAdoptionReport
{
    /// <summary>Version d'origine côté VS Launcher, pour l'affichage même en cas d'échec.</summary>
    public required string SourceVersion { get; init; }

    /// <summary>Catégorie du résultat.</summary>
    public required VslEngineAdoptionStatus Status { get; init; }

    /// <summary>Raison courte de l'ignorance ou de l'échec, à destination de l'utilisateur.</summary>
    public string? Detail { get; init; }

    /// <summary>Construit une entrée de succès.</summary>
    public static VslEngineAdoptionReport Adopted(string sourceVersion)
        => new() { SourceVersion = sourceVersion, Status = VslEngineAdoptionStatus.Adopted };

    /// <summary>Construit une entrée « non tentée ».</summary>
    public static VslEngineAdoptionReport Skipped(string sourceVersion, string reason)
        => new() { SourceVersion = sourceVersion, Status = VslEngineAdoptionStatus.Skipped, Detail = reason };

    /// <summary>Construit une entrée « échec d'E/S ».</summary>
    public static VslEngineAdoptionReport Failed(string sourceVersion, string reason)
        => new() { SourceVersion = sourceVersion, Status = VslEngineAdoptionStatus.Failed, Detail = reason };
}

/// <summary>Résultat final d'une adoption : un rapport par installation, un rapport par moteur.</summary>
/// <param name="Installations">Une entrée par installation soumise, dans l'ordre soumis.</param>
/// <param name="Engines">Une entrée par moteur soumis, dans l'ordre soumis.</param>
public sealed record VslAdoptionOutcome(
    IReadOnlyList<VslInstallationAdoptionReport> Installations,
    IReadOnlyList<VslEngineAdoptionReport> Engines)
{
    /// <summary>Nombre d'instances réellement créées.</summary>
    public int AdoptedInstallationCount => Installations.Count(report => report.Status == VslInstallationAdoptionStatus.Adopted);

    /// <summary>Nombre de moteurs réellement copiés.</summary>
    public int AdoptedEngineCount => Engines.Count(report => report.Status == VslEngineAdoptionStatus.Adopted);

    /// <summary>Vrai si au moins un élément (installation ou moteur) n'a pas été adopté.</summary>
    public bool HasIssues
        => Installations.Any(report => report.Status != VslInstallationAdoptionStatus.Adopted)
            || Engines.Any(report => report.Status != VslEngineAdoptionStatus.Adopted);
}

/// <summary>Étape courante d'une adoption.</summary>
public enum VslAdoptionPhase
{
    /// <summary>Copie des installations sélectionnées, une par une.</summary>
    AdoptingInstallations,

    /// <summary>Copie des moteurs sélectionnés, un par un.</summary>
    AdoptingEngines,
}

/// <summary>Avancement d'une adoption, phase courante plus compteurs et détail fichier par fichier de l'élément en cours.</summary>
/// <param name="Phase">Étape courante.</param>
/// <param name="CompletedItems">Éléments déjà traités (adoptés, ignorés ou en échec) dans la phase courante, celui en cours exclu.</param>
/// <param name="TotalItems">Nombre total d'éléments de la phase courante.</param>
/// <param name="CurrentItemLabel">Nom ou version de l'élément en cours de traitement, ou <see langword="null"/>.</param>
/// <param name="FileProgress">Avancement fichier par fichier de la copie en cours, quand disponible.</param>
public sealed record VslAdoptionProgress(
    VslAdoptionPhase Phase,
    int CompletedItems,
    int TotalItems,
    string? CurrentItemLabel,
    DirectoryCopyProgress? FileProgress);