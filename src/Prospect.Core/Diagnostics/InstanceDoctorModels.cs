using Prospect.Core.Common;
using Prospect.Core.ModDb;
using Prospect.Core.Runtime;

namespace Prospect.Core.Diagnostics;

/// <summary>
/// Verdict d'une vérification du docteur d'instance (<see cref="InstanceDoctor"/>), du plus au
/// moins grave dans l'ordre de déclaration : l'ordre naturel de l'énumération sert directement de
/// clé de tri pour <see cref="InstanceDoctorReport.WorstSeverity"/> et pour le regroupement du
/// dialogue (erreurs d'abord, docs de la mission).
/// </summary>
public enum InstanceDoctorSeverity
{
    Ok,
    Warning,
    Error,
}

/// <summary>Laquelle des cinq vérifications a produit un <see cref="InstanceDoctorFinding"/>.</summary>
public enum InstanceDoctorCheck
{
    /// <summary>1. La version du jeu de l'instance est-elle installée et complète ?</summary>
    GameVersion,

    /// <summary>2. Le runtime .NET requis par cette version est-il présent ?</summary>
    Runtime,

    /// <summary>3. Les dépendances déclarées des mods installés sont-elles satisfaites ?</summary>
    ModDependencies,

    /// <summary>4. Les mods installés sont-ils compatibles avec la version de jeu de l'instance ?</summary>
    ModCompatibility,

    /// <summary>5. Le volume qui porte la racine Prospect a-t-il assez d'espace libre ?</summary>
    DiskSpace,
}

/// <summary>Une ligne agrégée du rapport : sa vérification d'origine et son verdict. Sans message :
/// la mise en mots reste au Desktop (docs/architecture.md, séparation logique/interface), ce record
/// n'existe que pour permettre l'agrégation et le tri par sévérité, testables sans UI.</summary>
public sealed record InstanceDoctorFinding(InstanceDoctorCheck Check, InstanceDoctorSeverity Severity);

/// <summary>Résultat de la vérification 1 (version du jeu).</summary>
public enum GameVersionDoctorStatus
{
    /// <summary>Installée et complète (sentinelle <c>.prospect-complete</c> présente).</summary>
    Installed,

    /// <summary>Un dossier existe mais la sentinelle manque : installation interrompue.</summary>
    Incomplete,

    /// <summary>Aucun dossier pour cette version.</summary>
    Missing,
}

/// <summary>Verdict de la vérification 1, pour la version de jeu réclamée par l'instance.</summary>
/// <param name="Status">Installée, incomplète, ou absente.</param>
/// <param name="Version">Version du jeu de l'instance, quel que soit le verdict.</param>
public sealed record GameVersionDoctorResult(GameVersionDoctorStatus Status, GameVersion Version)
{
    /// <summary>Seule <see cref="GameVersionDoctorStatus.Installed"/> est un verdict sain : les deux
    /// autres empêchent le lancement (même logique que <c>GameLauncher.LaunchAsync</c>).</summary>
    public InstanceDoctorSeverity Severity
        => Status == GameVersionDoctorStatus.Installed ? InstanceDoctorSeverity.Ok : InstanceDoctorSeverity.Error;
}

/// <summary>Ce qui cloche dans un mod installé, pour la vérification 3.</summary>
public enum ModDoctorIssueKind
{
    /// <summary>Une dépendance déclarée par ce mod n'est pas satisfaite par les mods présents.</summary>
    UnsatisfiedDependency,

    /// <summary>L'archive n'a pas pu être identifiée (voir <see cref="Prospect.Core.ModDb.ModInfoProblem"/>).</summary>
    Unidentified,
}

/// <summary>
/// Une ligne de la vérification 3 : soit une dépendance déclarée non satisfaite par un mod
/// autrement identifié, soit un mod dont l'archive n'a pas pu être lue.
/// </summary>
/// <param name="Kind">Nature du problème.</param>
/// <param name="ModDisplayName">Nom affichable du mod concerné.</param>
/// <param name="Dependency">Détail de la dépendance en cause, pour <see cref="ModDoctorIssueKind.UnsatisfiedDependency"/>.</param>
/// <param name="Problem">Raison de la non-identification, pour <see cref="ModDoctorIssueKind.Unidentified"/>.</param>
public sealed record ModDoctorIssue(
    ModDoctorIssueKind Kind,
    string ModDisplayName,
    ModDependencyIssue? Dependency = null,
    ModInfoProblem Problem = ModInfoProblem.None)
{
    /// <summary>
    /// Une dépendance déclarée absente, désactivée ou trop ancienne empêchera vraisemblablement le
    /// mod de fonctionner : erreur. Un mod non identifié est un avertissement plus doux — Prospect
    /// ne sait rien de lui, mais rien ne prouve qu'il pose un problème au jeu.
    /// </summary>
    public InstanceDoctorSeverity Severity
        => Kind == ModDoctorIssueKind.UnsatisfiedDependency ? InstanceDoctorSeverity.Error : InstanceDoctorSeverity.Warning;
}

/// <summary>
/// Verdict agrégé de la vérification 4 (compatibilité de version de jeu), à partir des seules
/// données locales : provenance ModDB (<see cref="ModProvenance.ApproximateMatch"/>) et dernier
/// résultat de vérification de mises à jour connu, s'il existe. Compte plutôt que liste : cette
/// vérification est heuristique par nature (aucun appel réseau ne vient la confirmer), le détail par
/// mod resterait un chiffre qu'on ne peut pas garantir.
/// </summary>
/// <param name="ConfirmedCount">Mods dont la compatibilité avec la version de l'instance est confirmée localement.</param>
/// <param name="ApproximateCount">Mods dont l'installation ne s'appuie que sur un rapprochement de série mineure.</param>
/// <param name="UnknownCount">Mods sans aucun signal local exploitable.</param>
/// <param name="TotalChecked">Mods actifs et identifiés pris en compte (les autres relèvent de la vérification 3).</param>
public sealed record ModCompatibilityDoctorResult(int ConfirmedCount, int ApproximateCount, int UnknownCount, int TotalChecked)
{
    /// <summary>
    /// Jamais une erreur : sans appel réseau, rien ne permet d'affirmer une incompatibilité, la
    /// pire chose que ce docteur puisse dire est « incertain ». <see cref="TotalChecked"/> à zéro
    /// (aucun mod actif identifié) est vacuément sain.
    /// </summary>
    public InstanceDoctorSeverity Severity
        => TotalChecked == 0 || ConfirmedCount == TotalChecked ? InstanceDoctorSeverity.Ok : InstanceDoctorSeverity.Warning;

    /// <summary>Vrai si aucun mod, même celui qui a une provenance, ne permet de juger : le cas « inconnu, lance une vérification ».</summary>
    public bool IsWhollyUnknown => TotalChecked > 0 && UnknownCount == TotalChecked;
}

/// <summary>
/// Verdict de la vérification 5 (espace disque) : espace libre du volume qui porte la racine
/// Prospect, comparé à un seuil bas raisonnable.
/// </summary>
/// <param name="AvailableBytes">Octets libres rapportés par <see cref="System.IO.Abstractions.IDriveInfo.AvailableFreeSpace"/>.</param>
/// <param name="ThresholdBytes">Seuil sous lequel l'espace est jugé faible (<see cref="InstanceDoctor.LowDiskSpaceThresholdBytes"/>).</param>
public sealed record DiskSpaceDoctorResult(long AvailableBytes, long ThresholdBytes)
{
    public bool IsLow => AvailableBytes < ThresholdBytes;

    public InstanceDoctorSeverity Severity => IsLow ? InstanceDoctorSeverity.Warning : InstanceDoctorSeverity.Ok;
}

/// <summary>
/// Rapport complet du docteur d'instance (<see cref="InstanceDoctor.DiagnoseAsync"/>) : un verdict
/// typé par vérification, plus <see cref="Findings"/>, leur projection uniforme pour l'agrégation et
/// le tri par sévérité côté Desktop.
/// </summary>
public sealed record InstanceDoctorReport(
    GameVersionDoctorResult GameVersion,
    RuntimeCheckResult Runtime,
    IReadOnlyList<ModDoctorIssue> ModIssues,
    ModCompatibilityDoctorResult ModCompatibility,
    DiskSpaceDoctorResult DiskSpace)
{
    /// <summary>
    /// Une entrée par vérification 1, 2, 4 et 5, plus une par ligne de la vérification 3 : c'est
    /// cette dernière qui rend le compte variable d'un rapport à l'autre.
    /// </summary>
    public IReadOnlyList<InstanceDoctorFinding> Findings
    {
        get
        {
            var findings = new List<InstanceDoctorFinding>(4 + ModIssues.Count)
            {
                new(InstanceDoctorCheck.GameVersion, GameVersion.Severity),
                new(InstanceDoctorCheck.Runtime, RuntimeSeverity),
            };

            findings.AddRange(ModIssues.Select(issue => new InstanceDoctorFinding(InstanceDoctorCheck.ModDependencies, issue.Severity)));
            findings.Add(new InstanceDoctorFinding(InstanceDoctorCheck.ModCompatibility, ModCompatibility.Severity));
            findings.Add(new InstanceDoctorFinding(InstanceDoctorCheck.DiskSpace, DiskSpace.Severity));

            return findings;
        }
    }

    /// <summary>Le plus grave des verdicts du rapport, pour décider si l'état « tout va bien » s'affiche.</summary>
    public InstanceDoctorSeverity WorstSeverity => Findings.Max(finding => finding.Severity);

    /// <summary>Vrai si toutes les vérifications sont saines : l'état gratifiant du dialogue.</summary>
    public bool IsAllClear => WorstSeverity == InstanceDoctorSeverity.Ok;

    // Un runtime indéterminable ne bloque jamais un lancement (voir GameRuntimeRequirement.Unknown)
    // mais n'est pas non plus confirmé : avertissement doux, ni la fausse assurance d'un « ok » ni
    // la gravité d'une « erreur » qui ne bloque pourtant rien.
    private InstanceDoctorSeverity RuntimeSeverity => Runtime.Availability switch
    {
        RuntimeAvailability.Present => InstanceDoctorSeverity.Ok,
        RuntimeAvailability.Missing => InstanceDoctorSeverity.Error,
        _ => InstanceDoctorSeverity.Warning,
    };
}