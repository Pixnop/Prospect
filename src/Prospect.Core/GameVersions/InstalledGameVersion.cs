using Prospect.Core.Common;

namespace Prospect.Core.GameVersions;

/// <summary>
/// Une version du jeu installée sous <c>versions/</c> et complète (fichier sentinelle présent).
/// </summary>
/// <param name="Version">Version du jeu, déduite du nom de dossier.</param>
/// <param name="Directory">Dossier de l'installation.</param>
/// <param name="SizeBytes">Taille sur disque, somme des fichiers du dossier.</param>
/// <param name="InstalledUtc">Date d'écriture du fichier sentinelle, donc de fin d'installation.</param>
public sealed record InstalledGameVersion(GameVersion Version, string Directory, long SizeBytes, DateTimeOffset InstalledUtc);

/// <summary>Pourquoi un dossier de <c>versions/</c> n'a pas pu être compté comme installation valide.</summary>
public enum GameInstallBrokenReason
{
    /// <summary>Le fichier sentinelle manque : installation interrompue, ou dossier créé à la main.</summary>
    MissingCompletionMarker,

    /// <summary>Le nom du dossier n'est pas une version du jeu lisible.</summary>
    UnreadableVersionName,
}

/// <summary>
/// Un dossier de <c>versions/</c> qui n'est pas une installation exploitable. Même philosophie que
/// les instances cassées : le scan ne lève pas, il remonte des valeurs que l'interface peut
/// montrer.
/// </summary>
/// <param name="Directory">Chemin complet du dossier.</param>
/// <param name="FolderName">Nom du dossier, tel qu'affiché à l'utilisateur.</param>
/// <param name="Reason">Nature du problème.</param>
public sealed record BrokenGameInstall(string Directory, string FolderName, GameInstallBrokenReason Reason);

/// <summary>Résultat d'un scan de <c>versions/</c>.</summary>
/// <param name="Installed">Installations complètes, triées par version décroissante.</param>
/// <param name="Broken">Dossiers non exploitables.</param>
public sealed record GameInstallScanResult(IReadOnlyList<InstalledGameVersion> Installed, IReadOnlyList<BrokenGameInstall> Broken);