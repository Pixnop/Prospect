using System.Text.Json.Serialization;

using Prospect.Core.Common;

namespace Prospect.Core.Modpacks;

/// <summary>
/// Une entrée <c>mods[]</c> du manifest (docs/architecture.md, « Manifest de modpack »). Décrit un
/// mod par son identifiant modinfo.json et sa version exacte, jamais par un chemin ou une URL : le
/// manifest doit rester portable d'une machine à l'autre.
/// </summary>
public sealed record ModpackManifestMod
{
    /// <summary>Identifiant modinfo.json du mod (le <c>modid</c>, pas un nom affiché).</summary>
    public required string ModId { get; init; }

    /// <summary>Version exacte attendue à l'import : jamais « la plus récente », toujours celle-ci.</summary>
    public required ModVersion Version { get; init; }

    /// <summary>
    /// Identifiant de fichier ModDB, raccourci optionnel : l'import l'essaie en premier et ne
    /// retombe sur la résolution par <see cref="ModId"/> + <see cref="Version"/> que s'il n'est
    /// plus valide (release retirée ou remplacée depuis l'export).
    /// </summary>
    public int? FileId { get; init; }

    /// <summary>
    /// Empreinte SHA-256 hexadécimale minuscule du zip, calculée par Prospect à l'export : le
    /// ModDB n'expose aucune somme de contrôle (docs/research/moddb-api.md), c'est donc notre
    /// manifest qui la porte. <see langword="null"/> pour un manifest qui n'en fournit pas
    /// (écrit à la main, ou par un export plus ancien) : l'import saute alors la vérification
    /// plutôt que d'échouer.
    /// </summary>
    public string? Sha256 { get; init; }

    /// <summary>
    /// État activé/désactivé tel que sérialisé : <see langword="null"/> quand le mod est activé
    /// (valeur par défaut, omise du JSON pour garder le manifest lisible), <see langword="false"/>
    /// quand il est désactivé. Ne jamais lire directement : passer par <see cref="IsEnabled"/>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Enabled { get; init; }

    /// <summary>Vrai si le mod doit être chargé par le jeu, qu'<see cref="Enabled"/> soit explicite ou absent.</summary>
    [JsonIgnore]
    public bool IsEnabled => Enabled ?? true;
}

/// <summary>
/// Manifest de modpack <c>prospect-pack.json</c>, schéma v1 (docs/architecture.md, « Manifest de
/// modpack »). Produit par l'export, consommé par l'import : la SEULE donnée qui doit voyager entre
/// deux installations de Prospect pour reconstruire une instance équivalente. Portable par
/// construction : aucun chemin absolu, aucune URL signée, uniquement des identifiants et des
/// versions que le ModDB peut résoudre à nouveau à l'import.
/// </summary>
public sealed record ModpackManifest
{
    /// <summary>Version de schéma courante, la seule que cette version de Prospect sait lire.</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>Version du schéma du document.</summary>
    public required int SchemaVersion { get; init; }

    /// <summary>Nom du pack, repris comme nom proposé pour l'instance créée à l'import.</summary>
    public required string Name { get; init; }

    /// <summary>Auteur du pack, optionnel.</summary>
    public string? Author { get; init; }

    /// <summary>Version du jeu ciblée par le pack.</summary>
    public required GameVersion GameVersion { get; init; }

    /// <summary>Mods du pack. Peut être vide (un pack qui ne fixe qu'une version de jeu).</summary>
    public IReadOnlyList<ModpackManifestMod> Mods { get; init; } = [];
}