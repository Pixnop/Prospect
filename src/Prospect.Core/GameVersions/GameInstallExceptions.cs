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