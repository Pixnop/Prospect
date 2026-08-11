using Prospect.Core.Common;

namespace Prospect.Core.ModDb;

/// <summary>
/// Un mod présent dans <c>data/Mods/</c> d'une instance, tel que l'a vu le dernier scan. Le disque
/// est la source de vérité (docs/architecture.md, deuxième principe) : ce record décrit un fichier
/// observé, pas une entrée de base.
/// </summary>
public sealed record InstalledMod
{
    /// <summary>Chemin complet du fichier, suffixe de désactivation compris s'il y en a un.</summary>
    public required string FilePath { get; init; }

    /// <summary>
    /// Nom de fichier stable, indépendant de l'état d'activation : désactiver un mod ne change
    /// jamais cette valeur, qui sert de clé de correspondance avec la provenance.
    /// </summary>
    public required string FileName { get; init; }

    /// <summary>Vrai si le jeu chargera ce mod au prochain lancement.</summary>
    public required bool IsEnabled { get; init; }

    /// <summary>Taille du fichier en octets.</summary>
    public long SizeBytes { get; init; }

    /// <summary>Métadonnées lues dans l'archive, ou <see langword="null"/> si elle n'a pas pu être identifiée.</summary>
    public ModInfo? Info { get; init; }

    /// <summary>Raison de la non-identification, ou <see cref="ModInfoProblem.None"/>.</summary>
    public ModInfoProblem Problem { get; init; } = ModInfoProblem.None;

    /// <summary>Icône extraite de l'archive, ou <see langword="null"/>.</summary>
    public byte[]? Icon { get; init; }

    /// <summary>Provenance ModDB si Prospect a installé ce mod, ou <see langword="null"/> (dépôt manuel).</summary>
    public ModProvenance? Provenance { get; init; }

    /// <summary>Vrai si l'archive a livré des métadonnées exploitables.</summary>
    public bool IsIdentified => Info is not null;

    /// <summary>
    /// Identifiant du mod : celui du <c>modinfo.json</c> quand il est lisible, celui de la
    /// provenance sinon, et à défaut le nom de fichier, qui reste une clé utilisable pour agir sur
    /// le fichier même quand plus rien d'autre n'est identifiable.
    /// </summary>
    public string Identity => Info?.ModId ?? Provenance?.ModIdString ?? FileName;

    /// <summary>Nom affichable, avec les mêmes replis que <see cref="Identity"/>.</summary>
    public string DisplayName => Info?.Name ?? FileName;

    /// <summary>Version installée : celle du fichier si elle est lisible, celle de la provenance sinon.</summary>
    public ModVersion? Version => Info?.Version ?? Provenance?.Version;
}