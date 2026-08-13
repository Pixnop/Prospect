using Prospect.Core.Common;

namespace Prospect.Core.GameVersions;

/// <summary>
/// L'installation d'une version du jeu a échoué après le téléchargement : archive illisible,
/// installeur Windows en erreur, écriture refusée.
/// </summary>
public sealed class GameInstallFailedException : Exception
{
    public GameInstallFailedException()
        : base("L'installation de la version du jeu a échoué.")
    {
    }

    public GameInstallFailedException(string message)
        : base(message)
    {
    }

    public GameInstallFailedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Archive corrompue ou illisible.</summary>
    public static GameInstallFailedException ForArchive(string archivePath, Exception innerException)
        => new($"L'archive « {archivePath} » n'a pas pu être extraite.", innerException);

    /// <summary>Installeur Windows terminé sur un code de sortie non nul.</summary>
    public static GameInstallFailedException ForInstallerExitCode(string installerPath, int exitCode, string standardError)
    {
        var detail = string.IsNullOrWhiteSpace(standardError) ? string.Empty : $" : {standardError.Trim()}";

        return new GameInstallFailedException($"L'installeur « {installerPath} » s'est terminé avec le code {exitCode}{detail}.");
    }
}

/// <summary>
/// L'installation s'est terminée sans erreur mais le dossier de la version ne contient aucun des
/// exécutables attendus : le jeu n'est pas là où Prospect l'attendait.
/// </summary>
/// <remarks>
/// Un type à part de <see cref="GameInstallFailedException"/>, parce que le fait rapporté n'est pas
/// le même et que l'interface a un message différent à en tirer. Le cas est né d'un test réel sous
/// Windows, où l'installeur Inno peut retomber sur le dossier d'une installation système
/// préexistante ; il vaut pour les trois OS (une extraction qui n'a produit qu'un dossier vide se
/// détecte exactement pareil). Tant qu'elle est levée, la sentinelle de complétude n'est PAS
/// écrite et le dossier est nettoyé : un dossier de <c>versions/</c> porteur de la sentinelle
/// reste, comme documenté, une installation complète.
/// </remarks>
public sealed class GameInstallIncompleteException : Exception
{
    public GameInstallIncompleteException()
        : base("L'installation de la version du jeu n'a laissé aucun exécutable dans son dossier.")
    {
        TargetDirectory = string.Empty;
        ExpectedExecutables = [];
    }

    public GameInstallIncompleteException(string message)
        : base(message)
    {
        TargetDirectory = string.Empty;
        ExpectedExecutables = [];
    }

    public GameInstallIncompleteException(string message, Exception innerException)
        : base(message, innerException)
    {
        TargetDirectory = string.Empty;
        ExpectedExecutables = [];
    }

    private GameInstallIncompleteException(string message, string targetDirectory, IReadOnlyList<string> expectedExecutables)
        : base(message)
    {
        TargetDirectory = targetDirectory;
        ExpectedExecutables = expectedExecutables;
    }

    /// <summary>Dossier où l'installation était attendue.</summary>
    public string TargetDirectory { get; }

    /// <summary>Noms des exécutables cherchés, en chemins relatifs lisibles.</summary>
    public IReadOnlyList<string> ExpectedExecutables { get; }

    /// <summary>Aucun des exécutables attendus n'est présent après une installation pourtant terminée sans erreur.</summary>
    public static GameInstallIncompleteException For(
        GameVersion version,
        string targetDirectory,
        IReadOnlyList<GameExecutableLocation> expectedExecutables)
    {
        ArgumentNullException.ThrowIfNull(expectedExecutables);

        var names = expectedExecutables.Select(location => location.ToString()).ToArray();

        return new GameInstallIncompleteException(
            $"L'installation de la version {version} s'est terminée sans erreur, mais « {targetDirectory} » "
            + $"ne contient aucun des exécutables attendus ({string.Join(", ", names)}). "
            + "Le jeu a probablement été installé ailleurs.",
            targetDirectory,
            names);
    }
}

/// <summary>
/// Le catalogue ne propose pas cette version, ou ne propose aucun fichier pour la plateforme
/// courante (par exemple une version trop ancienne pour laquelle il n'existe pas de build mac).
/// </summary>
public sealed class GameVersionNotAvailableException : Exception
{
    public GameVersionNotAvailableException()
        : base("Cette version du jeu n'est pas disponible au téléchargement.")
    {
    }

    public GameVersionNotAvailableException(string message)
        : base(message)
    {
    }

    public GameVersionNotAvailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>La version n'apparaît pas dans le catalogue fusionné.</summary>
    public static GameVersionNotAvailableException ForUnknownVersion(GameVersion version)
        => new($"La version {version} n'existe pas dans le catalogue officiel.");

    /// <summary>La version existe mais aucune des plateformes attendues n'est publiée.</summary>
    public static GameVersionNotAvailableException ForUnsupportedPlatform(GameVersion version, IReadOnlyList<string> platformKeys)
        => new($"La version {version} n'est publiée pour aucune des plateformes attendues ({string.Join(", ", platformKeys)}).");
}