namespace Prospect.Core.Storage;

/// <summary>
/// Calcule l'arborescence disque de Prospect (docs/architecture.md, section « Arborescence
/// disque ») à partir d'une racine, par défaut dépendante de l'OS et surchargeable pour un
/// réglage utilisateur futur. Pur calcul de chemins : cette classe ne crée jamais de dossier,
/// c'est la responsabilité des services appelants (elle ne reçoit d'ailleurs aucune abstraction
/// de système de fichiers, seulement <see cref="IAppEnvironment"/>, ce qui la rend structurellement
/// incapable de toucher au disque).
/// </summary>
public sealed class AppPaths
{
    private const string AppFolderNameLinux = "prospect";
    private const string AppFolderNameOther = "Prospect";

    /// <summary>
    /// Construit les chemins de l'application.
    /// </summary>
    /// <param name="environment">Source des variables d'environnement, dossiers spéciaux et OS courant.</param>
    /// <param name="rootOverride">
    /// Racine explicite, prioritaire sur le calcul par défaut. Destinée au réglage utilisateur
    /// « déplacer les données de Prospect » ; <see langword="null"/> pour le comportement par défaut.
    /// </param>
    public AppPaths(IAppEnvironment environment, string? rootOverride = null)
    {
        ArgumentNullException.ThrowIfNull(environment);

        RootDirectory = string.IsNullOrEmpty(rootOverride) ? ComputeDefaultRoot(environment) : rootOverride;
    }

    /// <summary>Racine de toutes les données de Prospect.</summary>
    public string RootDirectory { get; }

    /// <summary>Dossier des instances (un sous-dossier par instance).</summary>
    public string InstancesDirectory => Path.Combine(RootDirectory, "instances");

    /// <summary>Dossier des installations du jeu (un sous-dossier par version).</summary>
    public string VersionsDirectory => Path.Combine(RootDirectory, "versions");

    /// <summary>Dossier des archives téléchargées (jeu et mods), en cours ou terminées.</summary>
    public string DownloadsCacheDirectory => Path.Combine(RootDirectory, "cache", "downloads");

    /// <summary>Dossier du cache léger des réponses ModDB.</summary>
    public string HttpCacheDirectory => Path.Combine(RootDirectory, "cache", "http");

    /// <summary>Dossier des journaux (un fichier par instance lancée).</summary>
    public string LogsDirectory => Path.Combine(RootDirectory, "logs");

    /// <summary>Chemin du fichier de réglages globaux (<c>prospect.json</c>).</summary>
    public string SettingsFilePath => Path.Combine(RootDirectory, "prospect.json");

    /// <summary>
    /// Chemin du secret de session du compte Vintage Story (<c>session.json</c>). Un fichier à
    /// part, en permissions restrictives (voir <see cref="Auth.FileSecretStore"/>) : une session
    /// n'est pas un réglage et n'a rien à faire dans <see cref="SettingsFilePath"/>, que
    /// l'utilisateur est justement invité à ouvrir à la main.
    /// </summary>
    public string SessionFilePath => Path.Combine(RootDirectory, "session.json");

    private static string ComputeDefaultRoot(IAppEnvironment environment) => environment.CurrentOperatingSystem switch
    {
        AppOperatingSystem.Windows => ComputeWindowsRoot(environment),
        AppOperatingSystem.MacOs => ComputeMacRoot(environment),
        _ => ComputeLinuxRoot(environment),
    };

    // Linux : $XDG_DATA_HOME/prospect, repli ~/.local/share/prospect si XDG_DATA_HOME est absent.
    private static string ComputeLinuxRoot(IAppEnvironment environment)
    {
        var xdgDataHome = environment.GetEnvironmentVariable("XDG_DATA_HOME");
        var baseDirectory = string.IsNullOrEmpty(xdgDataHome)
            ? Path.Combine(environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share")
            : xdgDataHome;

        return Path.Combine(baseDirectory, AppFolderNameLinux);
    }

    // Windows : %APPDATA%\Prospect, repli sur le dossier spécial ApplicationData si APPDATA est absent.
    private static string ComputeWindowsRoot(IAppEnvironment environment)
    {
        var appData = environment.GetEnvironmentVariable("APPDATA");
        var baseDirectory = string.IsNullOrEmpty(appData)
            ? environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
            : appData;

        return Path.Combine(baseDirectory, AppFolderNameOther);
    }

    // macOS : ~/Library/Application Support/Prospect, pas de variable d'environnement équivalente à XDG.
    private static string ComputeMacRoot(IAppEnvironment environment)
    {
        var home = environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        return Path.Combine(home, "Library", "Application Support", AppFolderNameOther);
    }
}