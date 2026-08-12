using Prospect.Core.Storage;

namespace Prospect.Core.Migration;

/// <summary>
/// Calcule la topologie disque de VS Launcher, le launcher communautaire archivé dont Prospect
/// recueille les orphelins (docs/research/vslauncher-et-distribution.md, section f). Pur calcul de
/// chemins, sur le même modèle qu'<see cref="AppPaths"/> : ne touche jamais le disque, ne reçoit
/// donc aucune abstraction de système de fichiers.
/// </summary>
/// <remarks>
/// VS Launcher est une application Electron : sa racine par défaut est <c>app.getPath("appData")</c>,
/// qui n'est PAS l'équivalent du <c>XDG_DATA_HOME</c> qu'utilise <see cref="AppPaths"/> pour
/// Prospect lui-même, mais son cousin « configuration » — <c>XDG_CONFIG_HOME</c> (repli
/// <c>~/.config</c>) sous Linux, <c>%APPDATA%</c> sous Windows (la même variable que Prospect,
/// simple coïncidence de convention Windows), <c>~/Library/Application Support</c> sous macOS
/// (bare, sans sous-dossier d'application, à la différence d'<c>app.getPath("userData")</c>). Sous
/// ce toit vivent trois dossiers par convention (<see cref="InstallationsDirectory"/>,
/// <see cref="GameVersionsDirectory"/>, <see cref="BackupsDirectory"/>) et un sous-dossier
/// nommé d'après l'application, <c>VSLauncher/</c>, qui porte <see cref="ConfigFilePath"/>.
///
/// Seul <see cref="ConfigFilePath"/> fait réellement foi pour localiser les installations et les
/// moteurs : chaque entrée de <c>config.json</c> porte son propre champ <c>path</c>, potentiellement
/// ailleurs que sous <see cref="InstallationsDirectory"/>/<see cref="GameVersionsDirectory"/> (VS
/// Launcher laisse l'utilisateur choisir un dossier arbitraire à la création). Les deux dossiers de
/// convention ne servent ici que de PROBE de présence pour <c>VslDetector</c> : leur existence
/// suffit à soupçonner une installation de VS Launcher sur la machine, même si <c>config.json</c>
/// est absent ou illisible.
/// </remarks>
public sealed class VslPaths
{
    private const string InstallationsFolderName = "VSLInstallations";
    private const string GameVersionsFolderName = "VSLGameVersions";
    private const string BackupsFolderName = "VSLBackups";
    private const string ConfigFolderName = "VSLauncher";
    private const string ConfigFileName = "config.json";

    /// <summary>
    /// Construit les chemins de VS Launcher.
    /// </summary>
    /// <param name="environment">Source des variables d'environnement, dossiers spéciaux et OS courant.</param>
    /// <param name="rootOverride">
    /// Racine explicite, prioritaire sur le calcul par défaut. Destinée au dossier manuel proposé
    /// par les réglages quand la détection automatique ne trouve rien ; <see langword="null"/>
    /// pour le comportement par défaut.
    /// </param>
    public VslPaths(IAppEnvironment environment, string? rootOverride = null)
    {
        ArgumentNullException.ThrowIfNull(environment);

        RootDirectory = string.IsNullOrEmpty(rootOverride) ? ComputeDefaultRoot(environment) : rootOverride;
    }

    /// <summary>
    /// Racine <c>appData</c> de VS Launcher (voir la remarque de la classe : distincte de la racine
    /// de données de Prospect elle-même).
    /// </summary>
    public string RootDirectory { get; }

    /// <summary>Dossier des profils (« Installations ») par convention. Probe de présence uniquement, voir la remarque de la classe.</summary>
    public string InstallationsDirectory => Path.Combine(RootDirectory, InstallationsFolderName);

    /// <summary>Dossier des moteurs (« Game Versions ») par convention. Probe de présence uniquement, voir la remarque de la classe.</summary>
    public string GameVersionsDirectory => Path.Combine(RootDirectory, GameVersionsFolderName);

    /// <summary>
    /// Dossier des sauvegardes compressées de VS Launcher. Jamais lu par Prospect : le système de
    /// sauvegardes de Prospect est un chantier séparé (docs/architecture.md, roadmap), et une
    /// sauvegarde VS Launcher est un format compressé propre à son propre outil, pas un dossier
    /// <c>data/</c> exploitable tel quel.
    /// </summary>
    public string BackupsDirectory => Path.Combine(RootDirectory, BackupsFolderName);

    /// <summary>
    /// Fichier <c>config.json</c> de VS Launcher (<c>app.getPath("userData")</c>, donc
    /// <c>&lt;RootDirectory&gt;/VSLauncher/config.json</c>), seule source de vérité pour la liste
    /// des installations et des moteurs.
    /// </summary>
    public string ConfigFilePath => Path.Combine(RootDirectory, ConfigFolderName, ConfigFileName);

    private static string ComputeDefaultRoot(IAppEnvironment environment) => environment.CurrentOperatingSystem switch
    {
        AppOperatingSystem.Windows => ComputeWindowsRoot(environment),
        AppOperatingSystem.MacOs => ComputeMacRoot(environment),
        _ => ComputeLinuxRoot(environment),
    };

    // Linux : $XDG_CONFIG_HOME/, repli ~/.config si absent. À NE PAS confondre avec
    // AppPaths.ComputeLinuxRoot, qui lit XDG_DATA_HOME : Electron sépare les deux, VS Launcher vit
    // dans le second.
    private static string ComputeLinuxRoot(IAppEnvironment environment)
    {
        var xdgConfigHome = environment.GetEnvironmentVariable("XDG_CONFIG_HOME");

        return string.IsNullOrEmpty(xdgConfigHome)
            ? Path.Combine(environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config")
            : xdgConfigHome;
    }

    // Windows : %APPDATA%\, repli sur le dossier spécial ApplicationData si absent — même calcul
    // qu'AppPaths.ComputeWindowsRoot, la coïncidence s'arrête à la variable d'environnement : VS
    // Launcher n'ajoute pas de sous-dossier "Prospect" ici, ses propres dossiers de convention sont
    // posés directement à cette racine.
    private static string ComputeWindowsRoot(IAppEnvironment environment)
    {
        var appData = environment.GetEnvironmentVariable("APPDATA");

        return string.IsNullOrEmpty(appData)
            ? environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
            : appData;
    }

    // macOS : ~/Library/Application Support, SANS sous-dossier d'application (à la différence de la
    // racine Prospect elle-même) : c'est app.getPath("appData") au sens Electron, pas "userData".
    // Non vérifié en live par la recherche (qui n'a documenté que Linux et Windows), mais c'est la
    // convention Electron standard et constante d'un OS à l'autre pour ce même appel.
    private static string ComputeMacRoot(IAppEnvironment environment)
    {
        var home = environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        return Path.Combine(home, "Library", "Application Support");
    }
}