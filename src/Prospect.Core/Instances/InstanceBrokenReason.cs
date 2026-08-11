namespace Prospect.Core.Instances;

/// <summary>
/// Raison pour laquelle <see cref="IInstanceRepository.ScanAsync"/> n'a pas pu charger un dossier
/// candidat comme instance valide.
/// </summary>
public enum InstanceBrokenReason
{
    /// <summary><c>instance.json</c> est absent du dossier.</summary>
    MissingMetadataFile,

    /// <summary><c>instance.json</c> existe mais son contenu n'est pas un JSON exploitable.</summary>
    CorruptedMetadataFile,

    /// <summary>
    /// Le <c>schemaVersion</c> lu ne peut pas être ramené au schéma courant (trop récent, ou trop
    /// ancien sans migration enregistrée pour combler l'écart).
    /// </summary>
    UnsupportedSchemaVersion,
}