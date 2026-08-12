namespace Prospect.Core.Instances;

/// <summary>
/// Réglages de sauvegarde d'une instance (bloc <c>backups</c> d'<c>instance.json</c>, schéma v2 —
/// voir <see cref="InstanceMetadata.CurrentSchemaVersion"/> et
/// <see cref="Migrations.InstanceMetadataV1ToV2Migration"/>, la première vraie migration du
/// pipeline). Vit dans le domaine Instances plutôt que dans <c>Backups/</c>, exactement comme
/// <see cref="InstanceLaunchSettings"/> : c'est un champ d'<see cref="InstanceMetadata"/>, consommé
/// par un autre domaine (<c>Backups.InstanceBackupService</c>, <c>Launching.GameLauncher</c>) sans
/// que la dépendance ne remonte jamais dans l'autre sens.
/// </summary>
/// <remarks>
/// Pas de niveau de compression configurable ici, à la différence de VS Launcher
/// (<c>compressionLevel</c> par installation, docs/research/vslauncher-et-distribution.md section
/// f) : <c>Backups.InstanceBackupService</c> fixe un niveau raisonnable une fois pour toutes. Un
/// joueur qui veut protéger son monde n'a pas à choisir entre « rapide » et « petit », c'est de la
/// sur-configuration pour un réglage que personne ne va ajuster deux fois.
/// </remarks>
public sealed record InstanceBackupSettings
{
    /// <summary>Borne basse : conserver moins d'une sauvegarde reviendrait à ne pas en garder du tout.</summary>
    public const int MinKeepCount = 1;

    /// <summary>
    /// Borne haute raisonnable : au-delà, l'espace disque consommé par des sauvegardes qu'on ne
    /// rejouera jamais l'emporte sur le confort de les garder.
    /// </summary>
    public const int MaxKeepCount = 20;

    /// <summary>Les seuls choix proposés par le sélecteur du bloc Sauvegardes (design : « garder N sauvegardes »).</summary>
    public static readonly IReadOnlyList<int> AllowedKeepCounts = [1, 3, 5, 10, 20];

    /// <summary>
    /// Déclenchement automatique avant chaque lancement (voir <c>Launching.GameLauncher</c>).
    /// Désactivé par défaut : sauvegarder tout <c>data/</c> à chaque lancement a un coût (temps,
    /// espace disque) que tous les joueurs ne veulent pas payer systématiquement, contrairement à
    /// VS Launcher qui n'imposait pas ce choix par défaut non plus (<c>backupsAuto</c>).
    /// </summary>
    public bool AutoBeforeLaunch { get; init; }

    /// <summary>
    /// Nombre de sauvegardes conservées par instance. Après chaque création,
    /// <c>Backups.InstanceBackupService</c> élague les plus anciennes au-delà de cette limite.
    /// </summary>
    public int KeepCount { get; init; } = 5;

    /// <summary>Réglages par défaut d'une instance neuve, et ceux posés par la migration v1 → v2 pour les instances existantes.</summary>
    public static InstanceBackupSettings Default { get; } = new();

    /// <summary>
    /// Copie bornée à <see cref="MinKeepCount"/>–<see cref="MaxKeepCount"/>, même principe que
    /// <see cref="Settings.DownloadPreferences.Clamped"/> : le sélecteur ne propose que des choix
    /// déjà valides (<see cref="AllowedKeepCounts"/>), mais le modèle se protège aussi lui-même
    /// contre un <c>instance.json</c> modifié à la main ou écrit par une version future.
    /// </summary>
    public InstanceBackupSettings Clamped() => this with
    {
        KeepCount = Math.Clamp(KeepCount, MinKeepCount, MaxKeepCount),
    };
}