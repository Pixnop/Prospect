namespace Prospect.Core.Instances;

/// <summary>
/// Persistance scannée des instances (docs/architecture.md, patterns « Repository ») : le seul
/// composant du Core à connaître la topologie disque du dossier <c>instances/</c>
/// (<c>instances/&lt;slug&gt;/instance.json</c> + <c>instances/&lt;slug&gt;/data/</c>). Tout code
/// appelant obtient ses chemins via <see cref="GetInstanceDirectory"/>/<see cref="GetDataDirectory"/>
/// plutôt que de les reconstruire lui-même.
/// </summary>
public interface IInstanceRepository
{
    /// <summary>Chemin du dossier de l'instance (<c>instances/&lt;slug&gt;</c>), qu'il existe ou non.</summary>
    string GetInstanceDirectory(string slug);

    /// <summary>Chemin du dossier de données de l'instance (cible de <c>--dataPath</c>), qu'il existe ou non.</summary>
    string GetDataDirectory(string slug);

    /// <summary>Vrai si un dossier existe déjà pour ce slug, indépendamment de la validité de son contenu.</summary>
    bool Exists(string slug);

    /// <summary>
    /// Scanne <c>instances/*/instance.json</c>. Un dossier illisible (fichier absent, JSON
    /// corrompu, schéma non pris en charge) ne fait jamais échouer le scan : il remonte dans
    /// <see cref="InstanceScanResult.BrokenInstances"/> avec sa raison.
    /// </summary>
    Task<InstanceScanResult> ScanAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Charge une instance par son slug. À la différence de <see cref="ScanAsync"/>, une
    /// anomalie est ici une vraie erreur pour l'appelant, qui a demandé ce slug précis.
    /// </summary>
    /// <exception cref="InstanceNotFoundException">Aucun dossier, ou <c>instance.json</c> absent.</exception>
    /// <exception cref="Prospect.Core.Storage.CorruptedFileException"><c>instance.json</c> existe mais n'est pas un JSON exploitable.</exception>
    /// <exception cref="InstanceSchemaVersionUnsupportedException">Le schéma lu ne peut pas être ramené au schéma courant.</exception>
    Task<InstanceRecord> LoadAsync(string slug, CancellationToken cancellationToken = default);

    /// <summary>Sauvegarde les métadonnées d'une instance, en écriture atomique via <see cref="Prospect.Core.Storage.JsonFileStore"/>.</summary>
    Task SaveAsync(InstanceRecord instance, CancellationToken cancellationToken = default);

    /// <summary>
    /// Liste les fichiers de <c>data/Saves/</c> (non récursif), sans lire leur contenu. Un
    /// dossier <c>Saves/</c> absent (aucun monde créé) est une absence normale : la liste
    /// résultante est simplement vide.
    /// </summary>
    Task<IReadOnlyList<InstanceWorldFile>> ListWorldsAsync(string slug, CancellationToken cancellationToken = default);
}