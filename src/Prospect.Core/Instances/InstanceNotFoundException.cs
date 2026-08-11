namespace Prospect.Core.Instances;

/// <summary>
/// Levée quand une opération référence une instance par son slug alors qu'aucun dossier
/// <c>instances/&lt;slug&gt;</c> valide n'existe (dossier absent, ou <c>instance.json</c> manquant
/// à l'intérieur). Distincte d'une <see cref="Prospect.Core.Storage.CorruptedFileException"/> :
/// ici il n'y a rien à lire, le dossier ou le fichier n'existe simplement pas.
/// </summary>
public sealed class InstanceNotFoundException : Exception
{
    /// <summary>Constructeur standard, sans slug particulier.</summary>
    public InstanceNotFoundException()
        : base("Aucune instance trouvée pour le slug demandé.")
    {
        Slug = string.Empty;
    }

    /// <summary>Construit l'exception pour un slug précis, sans cause interne.</summary>
    public InstanceNotFoundException(string slug)
        : base($"Aucune instance trouvée pour le slug '{slug}'.")
    {
        Slug = slug;
    }

    /// <summary>Construit l'exception pour un slug précis, avec une cause interne.</summary>
    public InstanceNotFoundException(string slug, Exception innerException)
        : base($"Aucune instance trouvée pour le slug '{slug}'.", innerException)
    {
        Slug = slug;
    }

    /// <summary>Slug recherché qui ne correspond à aucune instance valide.</summary>
    public string Slug { get; }
}