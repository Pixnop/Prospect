namespace Prospect.Core.Http;

/// <summary>
/// Un téléchargement a échoué définitivement : tous les miroirs et toutes les tentatives sont
/// épuisés, ou le disque a refusé l'écriture. Erreur attendue du domaine, donc exception typée
/// du projet (docs/architecture.md).
/// </summary>
public class DownloadFailedException : Exception
{
    public DownloadFailedException()
        : base("Le téléchargement a échoué.")
    {
    }

    public DownloadFailedException(string message)
        : base(message)
    {
    }

    public DownloadFailedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Construit l'échec pour un fichier donné, en conservant la panne d'origine.</summary>
    public static DownloadFailedException ForFile(string fileName, Exception innerException)
        => new($"Le téléchargement de « {fileName} » a échoué sur tous les miroirs.", innerException);
}

/// <summary>
/// Le fichier téléchargé ne correspond pas à l'empreinte MD5 annoncée par le catalogue. Le
/// fichier partiel est supprimé avant que cette exception ne soit levée : un binaire de jeu
/// corrompu ne doit jamais rester à portée d'une installation.
/// </summary>
/// <remarks>
/// VS Launcher publiait ce champ <c>md5</c> sans jamais le vérifier
/// (docs/research/vslauncher-et-distribution.md, implication 4).
/// </remarks>
public sealed class DownloadChecksumMismatchException : DownloadFailedException
{
    public DownloadChecksumMismatchException()
        : base("Le fichier téléchargé ne correspond pas à l'empreinte annoncée.")
    {
    }

    public DownloadChecksumMismatchException(string message)
        : base(message)
    {
    }

    public DownloadChecksumMismatchException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    private DownloadChecksumMismatchException(string message, string fileName, string expectedMd5, string actualMd5)
        : base(message)
    {
        FileName = fileName;
        ExpectedMd5 = expectedMd5;
        ActualMd5 = actualMd5;
    }

    /// <summary>Fichier concerné.</summary>
    public string? FileName { get; }

    /// <summary>Empreinte annoncée par le catalogue.</summary>
    public string? ExpectedMd5 { get; }

    /// <summary>Empreinte réellement calculée sur le fichier reçu.</summary>
    public string? ActualMd5 { get; }

    /// <summary>Construit l'exception en conservant les deux empreintes, utiles au diagnostic.</summary>
    public static DownloadChecksumMismatchException Create(string fileName, string expectedMd5, string actualMd5)
        => new(
            $"Le fichier « {fileName} » ne correspond pas à l'empreinte annoncée (attendu {expectedMd5}, obtenu {actualMd5}).",
            fileName,
            expectedMd5,
            actualMd5);
}