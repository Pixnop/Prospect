namespace Prospect.Core.Instances;

/// <summary>
/// Levée quand le <c>schemaVersion</c> lu dans un <c>instance.json</c> ne peut pas être ramené au
/// schéma courant (<see cref="InstanceMetadata.CurrentSchemaVersion"/>) : soit le fichier a été
/// écrit par une version plus récente de Prospect (<see cref="FoundSchemaVersion"/> supérieur),
/// soit aucune migration enregistrée ne couvre son schéma d'origine. Dans les deux cas,
/// <see cref="Instances.IInstanceRepository.ScanAsync"/> convertit cette exception en une entrée
/// <see cref="BrokenInstance"/> plutôt que de laisser tout le scan échouer.
/// </summary>
public sealed class InstanceSchemaVersionUnsupportedException : Exception
{
    /// <summary>Constructeur standard, sans contexte de fichier particulier.</summary>
    public InstanceSchemaVersionUnsupportedException()
        : base("La version de schéma de l'instance n'est pas prise en charge.")
    {
        Path = string.Empty;
    }

    /// <summary>Constructeur standard, avec un message personnalisé.</summary>
    public InstanceSchemaVersionUnsupportedException(string message)
        : base(message)
    {
        Path = string.Empty;
    }

    /// <summary>Construit l'exception pour un fichier précis, avec les versions trouvée et courante.</summary>
    public InstanceSchemaVersionUnsupportedException(string path, int foundSchemaVersion, int currentSchemaVersion)
        : base(BuildMessage(path, foundSchemaVersion, currentSchemaVersion))
    {
        Path = path;
        FoundSchemaVersion = foundSchemaVersion;
        CurrentSchemaVersion = currentSchemaVersion;
    }

    /// <summary>Chemin du fichier <c>instance.json</c> fautif.</summary>
    public string Path { get; }

    /// <summary>Version de schéma lue dans le fichier.</summary>
    public int FoundSchemaVersion { get; }

    /// <summary>Version de schéma courante de ce build de Prospect.</summary>
    public int CurrentSchemaVersion { get; }

    private static string BuildMessage(string path, int foundSchemaVersion, int currentSchemaVersion)
        => foundSchemaVersion > currentSchemaVersion
            ? $"L'instance '{path}' a été créée par une version plus récente de Prospect " +
              $"(schéma {foundSchemaVersion}, ce build gère jusqu'au schéma {currentSchemaVersion})."
            : $"L'instance '{path}' est au schéma {foundSchemaVersion} et aucune migration enregistrée " +
              $"ne permet de l'amener au schéma {currentSchemaVersion}.";
}