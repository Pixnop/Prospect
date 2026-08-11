namespace Prospect.Core.ModDb;

/// <summary>
/// Persistance scannée des mods d'une instance (docs/architecture.md, patterns « Repository ») : le
/// seul composant à connaître la topologie de <c>instances/&lt;slug&gt;/data/Mods/</c> et le
/// fichier de provenance qui l'accompagne.
/// </summary>
public interface IInstalledModRepository
{
    /// <summary>Chemin du dossier <c>data/Mods/</c> de l'instance, qu'il existe ou non.</summary>
    string GetModsDirectory(string slug);

    /// <summary>Chemin du fichier de provenance de l'instance, qu'il existe ou non.</summary>
    string GetProvenanceFilePath(string slug);

    /// <summary>
    /// Scanne <c>data/Mods/</c> : archives actives et désactivées, métadonnées lues dans chaque
    /// zip, provenance rattachée quand elle existe. Une archive illisible ne fait jamais échouer le
    /// scan, elle remonte non identifiée avec sa raison.
    /// </summary>
    Task<IReadOnlyList<InstalledMod>> ScanAsync(string slug, CancellationToken cancellationToken = default);

    /// <summary>
    /// Active ou désactive un mod. Sans effet s'il est déjà dans l'état demandé.
    /// </summary>
    /// <returns>Le mod tel qu'il existe après l'opération.</returns>
    /// <exception cref="ModFileNotFoundException">Le fichier a disparu entre le scan et l'appel.</exception>
    Task<InstalledMod> SetEnabledAsync(string slug, InstalledMod installedMod, bool enabled, CancellationToken cancellationToken = default);

    /// <summary>
    /// Supprime le fichier d'un mod et son entrée de provenance. Sans effet si le fichier a déjà
    /// disparu : l'utilisateur peut très bien l'avoir retiré à la main entre deux scans.
    /// </summary>
    Task RemoveAsync(string slug, InstalledMod installedMod, CancellationToken cancellationToken = default);

    /// <summary>Enregistre ou remplace l'entrée de provenance d'un fichier.</summary>
    Task SaveProvenanceAsync(string slug, ModProvenance provenance, CancellationToken cancellationToken = default);

    /// <summary>Lit la provenance connue, indexée par nom de fichier. Un fichier absent donne une table vide.</summary>
    Task<IReadOnlyDictionary<string, ModProvenance>> LoadProvenanceAsync(string slug, CancellationToken cancellationToken = default);
}

/// <summary>Le fichier d'un mod n'existe plus là où le scan l'avait vu.</summary>
public sealed class ModFileNotFoundException : Exception
{
    /// <summary>Construit l'erreur pour un chemin donné.</summary>
    public ModFileNotFoundException(string filePath)
        : base($"Le fichier de mod '{filePath}' n'existe plus.") => FilePath = filePath;

    /// <summary>Chemin attendu.</summary>
    public string FilePath { get; }
}