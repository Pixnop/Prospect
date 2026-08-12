using Prospect.Core.Common;

namespace Prospect.Core.Instances;

/// <summary>
/// Métadonnées persistées d'une instance (fichier <c>instance.json</c>, docs/architecture.md
/// section « instance.json (schéma v1) »). Ce type représente toujours le schéma courant
/// (<see cref="CurrentSchemaVersion"/>) : un document lu à un schéma plus ancien passe par le
/// pipeline de migrations avant d'être désérialisé ici, il n'existe donc jamais de variante
/// « historique » de ce record dans le code.
/// </summary>
/// <remarks>
/// <c>Id</c> est un GUID immuable : renommer une instance change <see cref="Name"/>, jamais
/// l'identité ni le dossier sur disque (voir <see cref="InstanceService.RenameAsync"/>). Type
/// délibérément une classe (<c>sealed record</c>) plutôt qu'un <c>record struct</c> comme
/// <see cref="GameVersion"/> : c'est un document composite à plusieurs champs, pas une petite
/// valeur comparée en boucle, la copie par référence est le bon défaut ici.
/// </remarks>
public sealed record InstanceMetadata
{
    /// <summary>Icône par défaut d'une instance nouvellement créée.</summary>
    public const string DefaultIcon = "builtin:default";

    /// <summary>
    /// Version courante du schéma d'<c>instance.json</c>. Un document au schéma inférieur passe
    /// par le pipeline de migrations au chargement ; un document au schéma supérieur (écrit par
    /// un Prospect plus récent) fait lever <see cref="InstanceSchemaVersionUnsupportedException"/>.
    /// Passée à 2 par le chantier Sauvegardes (<see cref="Backups"/>) : voir
    /// <see cref="Migrations.InstanceMetadataV1ToV2Migration"/>, la première vraie migration du
    /// pipeline.
    /// </summary>
    public const int CurrentSchemaVersion = 2;

    /// <summary>Version de schéma à laquelle ce document est conforme.</summary>
    public required int SchemaVersion { get; init; }

    /// <summary>Identifiant immuable de l'instance, indépendant de son nom ou de son slug de dossier.</summary>
    public required Guid Id { get; init; }

    /// <summary>Nom affiché, modifiable librement.</summary>
    public required string Name { get; init; }

    /// <summary>Version du jeu associée à cette instance.</summary>
    public required GameVersion GameVersion { get; init; }

    /// <summary>
    /// Icône de l'instance : <c>builtin:&lt;nom&gt;</c> pour une icône embarquée, ou
    /// <c>file:&lt;nom&gt;</c> pour un fichier copié dans le dossier de l'instance. Chaîne opaque
    /// pour l'instant ; son typage riche arrive avec l'UI.
    /// </summary>
    public string Icon { get; init; } = DefaultIcon;

    /// <summary>Date de création, posée une seule fois à la construction via <see cref="IClock"/>.</summary>
    public required DateTimeOffset CreatedUtc { get; init; }

    /// <summary>Date du dernier lancement, <see langword="null"/> si l'instance n'a jamais tourné.</summary>
    public DateTimeOffset? LastLaunchedUtc { get; init; }

    /// <summary>Temps de jeu cumulé, en secondes.</summary>
    public long TotalPlaytimeSeconds { get; init; }

    /// <summary>Réglages de lancement propres à cette instance.</summary>
    public InstanceLaunchSettings Launch { get; init; } = InstanceLaunchSettings.Empty;

    /// <summary>Réglages de sauvegarde propres à cette instance (schéma v2, voir <see cref="CurrentSchemaVersion"/>).</summary>
    public InstanceBackupSettings Backups { get; init; } = InstanceBackupSettings.Default;

    /// <summary>Notes libres de l'utilisateur.</summary>
    public string Notes { get; init; } = string.Empty;

    /// <summary>
    /// Copie avec ses champs de référence sans initialiseur ramenés à leur défaut documenté quand
    /// ils sont absents plutôt que laissés à <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// Même piège que <see cref="Settings.ProspectSettings.Normalized"/> (voir sa docstring pour le
    /// mécanisme complet), retrouvé ici en auditant les modèles persistés du Core après le chantier
    /// Réglages : ce type porte des membres <c>required</c> (<see cref="SchemaVersion"/>,
    /// <see cref="Id"/>, <see cref="Name"/>, <see cref="GameVersion"/>), ce qui fait que
    /// <c>System.Text.Json</c> construit l'objet SANS passer par son constructeur implicite et donc
    /// SANS rejouer les initialiseurs <c>= DefaultIcon</c>/<c>= InstanceLaunchSettings.Empty</c>/
    /// <c>= string.Empty</c> ci-dessus quand le champ JSON correspondant est absent. Un
    /// <c>instance.json</c> partiel (édité à la main, ou écrit par un schéma antérieur qui ne
    /// portait pas encore ce champ) désérialise donc <see cref="Icon"/>, <see cref="Launch"/>,
    /// <see cref="Backups"/> ou <see cref="Notes"/> à <see langword="null"/> malgré leurs défauts
    /// affichés juste au-dessus, avec un risque de <see cref="NullReferenceException"/> en aval
    /// (par exemple <c>Launch.ExtraArgs</c> à la construction de la ligne de commande). Appelée
    /// après toute lecture disque par <see cref="FileSystemInstanceRepository.LoadAsync"/> — y
    /// compris juste après la migration v1 → v2, en filet derrière
    /// <see cref="Migrations.InstanceMetadataV1ToV2Migration"/> qui pose déjà <see cref="Backups"/>
    /// explicitement.
    /// </remarks>
    public InstanceMetadata Normalized() => this with
    {
        Icon = Icon ?? DefaultIcon,
        Launch = Launch ?? InstanceLaunchSettings.Empty,
        Backups = (Backups ?? InstanceBackupSettings.Default).Clamped(),
        Notes = Notes ?? string.Empty,
    };
}