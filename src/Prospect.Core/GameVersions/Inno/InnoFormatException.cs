namespace Prospect.Core.GameVersions.Inno;

/// <summary>
/// L'installeur n'est pas un Inno Setup que Prospect sait ouvrir, ou son contenu ne correspond pas
/// à ce qu'il déclare.
/// </summary>
/// <remarks>
/// Cette exception n'est pas un échec d'installation : c'est le signal convenu qui fait retomber
/// <see cref="WindowsGameInstallStrategy"/> sur l'exécution de l'installeur officiel. Toutes les
/// raisons de renoncer passent donc par elle, du format inconnu à l'empreinte qui ne tombe pas
/// juste, pour qu'il n'existe qu'un seul chemin de repli.
/// </remarks>
public sealed class InnoFormatException : Exception
{
    /// <summary>Construit l'exception.</summary>
    /// <param name="message">Ce qui n'a pas pu être lu, en clair.</param>
    public InnoFormatException(string message)
        : base(message)
    {
    }

    /// <summary>Format ou version que Prospect ne prétend pas savoir lire.</summary>
    public static InnoFormatException Unsupported(string reason)
        => new($"Installeur Inno Setup non pris en charge par l'extraction : {reason}.");

    /// <summary>Le fichier déclare une chose et en contient une autre.</summary>
    public static InnoFormatException Corrupt(string reason)
        => new($"Installeur Inno Setup illisible : {reason}.");
}