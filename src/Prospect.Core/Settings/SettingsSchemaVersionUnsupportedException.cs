namespace Prospect.Core.Settings;

/// <summary>
/// Levée quand le <c>schemaVersion</c> lu dans <c>prospect.json</c> ne peut pas être ramené au
/// schéma courant (<see cref="ProspectSettings.CurrentSchemaVersion"/>) : soit le fichier a été
/// écrit par une version plus récente de Prospect (<see cref="FoundSchemaVersion"/> supérieur), soit
/// aucune migration enregistrée ne couvre son schéma d'origine. Miroir exact
/// d'<see cref="Instances.InstanceSchemaVersionUnsupportedException"/> pour le même besoin côté
/// réglages globaux.
/// </summary>
public sealed class SettingsSchemaVersionUnsupportedException : Exception
{
    /// <summary>Constructeur standard, sans contexte de fichier particulier.</summary>
    public SettingsSchemaVersionUnsupportedException()
        : base("La version de schéma des réglages n'est pas prise en charge.")
    {
        Path = string.Empty;
    }

    /// <summary>Constructeur standard, avec un message personnalisé.</summary>
    public SettingsSchemaVersionUnsupportedException(string message)
        : base(message)
    {
        Path = string.Empty;
    }

    /// <summary>Construit l'exception pour un fichier précis, avec les versions trouvée et courante.</summary>
    public SettingsSchemaVersionUnsupportedException(string path, int foundSchemaVersion, int currentSchemaVersion)
        : base(BuildMessage(path, foundSchemaVersion, currentSchemaVersion))
    {
        Path = path;
        FoundSchemaVersion = foundSchemaVersion;
        CurrentSchemaVersion = currentSchemaVersion;
    }

    /// <summary>Chemin du fichier <c>prospect.json</c> fautif.</summary>
    public string Path { get; }

    /// <summary>Version de schéma lue dans le fichier.</summary>
    public int FoundSchemaVersion { get; }

    /// <summary>Version de schéma courante de ce build de Prospect.</summary>
    public int CurrentSchemaVersion { get; }

    private static string BuildMessage(string path, int foundSchemaVersion, int currentSchemaVersion)
        => foundSchemaVersion > currentSchemaVersion
            ? $"'{path}' a été écrit par une version plus récente de Prospect " +
              $"(schéma {foundSchemaVersion}, ce build gère jusqu'au schéma {currentSchemaVersion})."
            : $"'{path}' est au schéma {foundSchemaVersion} et aucune migration enregistrée " +
              $"ne permet de l'amener au schéma {currentSchemaVersion}.";
}