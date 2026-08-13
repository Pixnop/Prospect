using Prospect.Core.Common;

namespace Prospect.Core.GameVersions;

/// <summary>
/// Persistance scannée des installations du jeu (docs/architecture.md, pattern « Repository ») :
/// le seul composant à connaître la topologie de <c>versions/</c> et l'existence du fichier
/// sentinelle qui distingue une installation terminée d'une installation interrompue.
/// </summary>
public interface IInstalledGameVersionRepository
{
    /// <summary>Dossier d'une version, qu'il existe ou non.</summary>
    string GetVersionDirectory(GameVersion version);

    /// <summary>Vrai si la version est installée et complète (dossier présent et sentinelle écrite).</summary>
    bool IsInstalled(GameVersion version);

    /// <summary>
    /// L'installation d'une version, ou <see langword="null"/> si elle n'est pas installée. Une
    /// absence normale, donc un retour nullable et non une exception (docs/architecture.md).
    /// </summary>
    InstalledGameVersion? Find(GameVersion version);

    /// <summary>
    /// Scanne <c>versions/</c>. Un dossier sans sentinelle ou au nom illisible ne fait jamais
    /// échouer le scan : il remonte dans <see cref="GameInstallScanResult.Broken"/>.
    /// </summary>
    Task<GameInstallScanResult> ScanAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Vide puis recrée le dossier de la version, pour repartir d'une base saine avant une
    /// installation (un reliquat d'installation interrompue traîne peut-être).
    /// </summary>
    void PrepareDirectory(GameVersion version);

    /// <summary>
    /// Écrit le fichier sentinelle. À n'appeler qu'en toute fin d'installation : c'est lui, et
    /// lui seul, qui fait passer un dossier de « interrompu » à « installé ».
    /// </summary>
    Task MarkCompleteAsync(GameVersion version, CancellationToken cancellationToken = default);

    /// <summary>
    /// Supprime récursivement le dossier d'une version, sur le thread appelant. Sans effet si elle
    /// n'existe pas. Réservé au nettoyage d'une installation avortée, où le dossier est encore
    /// quasi vide : pour une désinstallation demandée par l'utilisateur, voir
    /// <see cref="RemoveAsync"/>, qui déporte le travail et rend compte de son avancement.
    /// </summary>
    void Remove(GameVersion version);

    /// <summary>
    /// Désinstalle une version : suppression hors du thread appelant, avec un avancement chiffré.
    /// Rien n'est annulable une fois commencé — une version à moitié supprimée est pire que les
    /// deux états francs.
    /// </summary>
    /// <exception cref="Storage.DirectoryDeleteFailedException">Il reste des fichiers sur le disque.</exception>
    Task RemoveAsync(GameVersion version, IProgress<Storage.DirectoryDeleteProgress>? progress = null, CancellationToken cancellationToken = default);
}