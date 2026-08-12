namespace Prospect.Core.Migration;

/// <summary>
/// État riche renvoyé par <see cref="VslDetector.DetectAsync"/> : jamais un simple booléen,
/// toujours de quoi afficher un état précis à l'utilisateur (absent, présent avec un décompte,
/// présent mais illisible).
/// </summary>
public sealed record VslDetectionResult
{
    /// <summary>
    /// Vrai si quoi que ce soit qui ressemble à VS Launcher a été trouvé (dossier de convention ou
    /// <c>config.json</c>). Ne garantit pas à lui seul qu'une installation soit adoptable : voir
    /// <see cref="Installations"/> et <see cref="ConfigError"/>.
    /// </summary>
    public required bool IsDetected { get; init; }

    /// <summary>Racine <c>appData</c> calculée ou fournie (voir <see cref="VslPaths"/>), à des fins d'affichage/diagnostic.</summary>
    public required string RootDirectory { get; init; }

    /// <summary>Vrai si <c>config.json</c> existe. Sans lui, aucune installation ni aucun moteur n'est exploitable.</summary>
    public required bool HasConfigFile { get; init; }

    /// <summary>
    /// Message si <c>config.json</c> existe mais n'a pas pu être lu (JSON invalide). L'utilisateur
    /// peut alors éditer VS Launcher lui-même pour le régénérer, ou rien n'est adoptable ici.
    /// </summary>
    public string? ConfigError { get; init; }

    /// <summary>Installations exploitables trouvées dans <c>config.json</c>.</summary>
    public IReadOnlyList<VslInstallation> Installations { get; init; } = [];

    /// <summary>Moteurs exploitables trouvés dans <c>config.json</c>.</summary>
    public IReadOnlyList<VslGameVersionEntry> GameVersions { get; init; } = [];

    /// <summary>Entrées de <c>config.json</c> ignorées (signalées, jamais bloquantes) — voir <see cref="VslConfigParser"/>.</summary>
    public IReadOnlyList<VslConfigIssue> Issues { get; init; } = [];

    /// <summary>Nombre d'installations détectées, pour l'affichage (« 3 installations et 2 moteurs détectés »).</summary>
    public int InstallationCount => Installations.Count;

    /// <summary>Nombre de moteurs détectés.</summary>
    public int GameVersionCount => GameVersions.Count;

    /// <summary>Vrai s'il y a au moins une installation ou un moteur à proposer à l'adoption.</summary>
    public bool HasAnyContent => InstallationCount > 0 || GameVersionCount > 0;

    /// <summary>Rien qui ressemble à VS Launcher sur cette machine (ou à ce chemin, si surchargé).</summary>
    internal static VslDetectionResult NotDetected(string root) => new()
    {
        IsDetected = false,
        RootDirectory = root,
        HasConfigFile = false,
    };

    /// <summary>Au moins un dossier de convention existe, mais <c>config.json</c> est absent : rien à lister.</summary>
    internal static VslDetectionResult DetectedWithoutConfig(string root) => new()
    {
        IsDetected = true,
        RootDirectory = root,
        HasConfigFile = false,
    };

    /// <summary><c>config.json</c> existe mais son JSON est invalide : détecté, rien d'exploitable.</summary>
    internal static VslDetectionResult DetectedWithCorruptedConfig(string root, string error) => new()
    {
        IsDetected = true,
        RootDirectory = root,
        HasConfigFile = true,
        ConfigError = error,
    };

    /// <summary><c>config.json</c> lu avec succès (entrées individuellement corrompues tolérées, voir <see cref="Issues"/>).</summary>
    internal static VslDetectionResult Detected(string root, VslConfigParseResult parsed) => new()
    {
        IsDetected = true,
        RootDirectory = root,
        HasConfigFile = true,
        Installations = parsed.Installations,
        GameVersions = parsed.GameVersions,
        Issues = parsed.Issues,
    };
}