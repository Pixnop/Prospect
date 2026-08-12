namespace Prospect.Core.Backups;

/// <summary>
/// Levée quand <see cref="InstanceBackupService.DeleteAsync"/> ou
/// <see cref="InstanceBackupService.RestoreAsync"/> référence un fichier de sauvegarde qui n'existe
/// pas (déjà supprimé, nom erroné). Même principe que
/// <see cref="Instances.InstanceNotFoundException"/> : un appelant qui demande CE fichier précis
/// mérite une erreur typée, à la différence d'un scan qui listerait simplement ce qui existe.
/// </summary>
public sealed class InstanceBackupNotFoundException : Exception
{
    /// <summary>Constructeur standard, sans contexte particulier.</summary>
    public InstanceBackupNotFoundException()
        : base("Aucune sauvegarde trouvée pour le fichier demandé.")
    {
        Slug = string.Empty;
        FileName = string.Empty;
    }

    /// <summary>Constructeur standard, avec un message personnalisé.</summary>
    public InstanceBackupNotFoundException(string message)
        : base(message)
    {
        Slug = string.Empty;
        FileName = string.Empty;
    }

    /// <summary>Construit l'exception pour une instance et un fichier précis, sans cause interne.</summary>
    public InstanceBackupNotFoundException(string slug, string fileName)
        : base($"Aucune sauvegarde '{fileName}' pour l'instance '{slug}'.")
    {
        Slug = slug;
        FileName = fileName;
    }

    /// <summary>Construit l'exception pour une instance et un fichier précis, avec une cause interne.</summary>
    public InstanceBackupNotFoundException(string slug, string fileName, Exception innerException)
        : base($"Aucune sauvegarde '{fileName}' pour l'instance '{slug}'.", innerException)
    {
        Slug = slug;
        FileName = fileName;
    }

    /// <summary>Slug de l'instance concernée.</summary>
    public string Slug { get; }

    /// <summary>Nom du fichier de sauvegarde recherché qui n'existe pas.</summary>
    public string FileName { get; }
}