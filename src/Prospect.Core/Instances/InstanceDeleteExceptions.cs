namespace Prospect.Core.Instances;

/// <summary>
/// La suppression d'une instance n'a pas pu aller jusqu'au bout : il reste quelque chose sur le
/// disque.
/// </summary>
/// <remarks>
/// Les causes réelles sont nombreuses et dépendent de l'OS (fichier de monde encore ouvert par le
/// jeu, dossier synchronisé, antivirus qui tient une archive), mais le seul fait utile à
/// l'utilisateur est le même partout : ce n'est pas fini, et le dossier est nommé pour qu'il puisse
/// aller voir. Un type à part de <see cref="InstanceNotFoundException"/>, parce que celui-là dit
/// « il n'y avait rien à supprimer » et celui-ci « il en reste ».
/// </remarks>
public sealed class InstanceDeleteFailedException : Exception
{
    public InstanceDeleteFailedException()
        : base("La suppression de l'instance n'a pas pu aller jusqu'au bout.")
    {
        Slug = string.Empty;
        Directory = string.Empty;
    }

    public InstanceDeleteFailedException(string message)
        : base(message)
    {
        Slug = string.Empty;
        Directory = string.Empty;
    }

    public InstanceDeleteFailedException(string message, Exception innerException)
        : base(message, innerException)
    {
        Slug = string.Empty;
        Directory = string.Empty;
    }

    private InstanceDeleteFailedException(string message, string slug, string directory, Exception innerException)
        : base(message, innerException)
    {
        Slug = slug;
        Directory = directory;
    }

    /// <summary>Slug de l'instance concernée.</summary>
    public string Slug { get; }

    /// <summary>Dossier dont il reste quelque chose.</summary>
    public string Directory { get; }

    /// <summary>La suppression récursive a échoué en route.</summary>
    public static InstanceDeleteFailedException For(string slug, string directory, Exception innerException)
        => new(
            $"La suppression de l'instance « {slug} » n'a pas pu aller jusqu'au bout : il reste des fichiers dans « {directory} ».",
            slug,
            directory,
            innerException);
}

/// <summary>
/// Une instance portant exactement ce nom est en cours de suppression : la créer maintenant
/// reviendrait soit à écrire dans un dossier qu'on est en train d'effacer, soit à donner
/// silencieusement un autre nom de dossier que celui demandé.
/// </summary>
public sealed class InstanceDeletionInProgressException : Exception
{
    public InstanceDeletionInProgressException()
        : base("Une instance de ce nom est en cours de suppression.")
        => Slug = string.Empty;

    public InstanceDeletionInProgressException(string slug)
        : base($"L'instance « {slug} » est en cours de suppression : son nom se libèrera dès que ce sera terminé.")
        => Slug = slug;

    public InstanceDeletionInProgressException(string message, Exception innerException)
        : base(message, innerException)
        => Slug = string.Empty;

    /// <summary>Slug encore occupé par la suppression en cours.</summary>
    public string Slug { get; }
}