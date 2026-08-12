namespace Prospect.Core.Settings;

/// <summary>
/// Réglages globaux persistés (fichier <c>prospect.json</c>, <see cref="Storage.AppPaths.SettingsFilePath"/>,
/// docs/architecture.md section « prospect.json (réglages globaux, v1 minimale) »). Comme
/// <see cref="Instances.InstanceMetadata"/>, ce type représente toujours le schéma courant
/// (<see cref="CurrentSchemaVersion"/>) : un document lu à un schéma plus ancien passe par
/// <see cref="Migrations.SettingsMigrationPipeline"/> avant d'être désérialisé ici.
/// </summary>
/// <remarks>
/// Périmètre volontairement minimal (v1) : seuls les champs réellement consommés aujourd'hui —
/// thème, langue, préférences de téléchargement. Ni chemin racine relocalisé (la relocalisation
/// est hors périmètre, voir <c>Prospect.Desktop.ViewModels.Settings.SettingsGameViewModel</c>), ni
/// session de compte vintagestory.at (post-MVP, docs/architecture.md) : les ajouter maintenant
/// serait de la spéculation sur un schéma qu'aucun code ne lit encore.
/// </remarks>
public sealed record ProspectSettings
{
    /// <summary>Seule langue dont l'interface dispose réellement aujourd'hui.</summary>
    public const string French = "fr";

    /// <summary>
    /// Version courante du schéma de <c>prospect.json</c>. Un document au schéma inférieur passe
    /// par le pipeline de migrations au chargement ; un document au schéma supérieur (écrit par un
    /// Prospect plus récent) fait lever <see cref="SettingsSchemaVersionUnsupportedException"/>.
    /// </summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>Version de schéma à laquelle ce document est conforme.</summary>
    public required int SchemaVersion { get; init; }

    /// <summary>
    /// Thème choisi. Dark est le défaut historique de l'app (App.axaml le codait en dur avant ce
    /// chantier) : un fichier absent doit donc continuer à démarrer en sombre, pas basculer
    /// silencieusement l'utilisateur existant vers un autre rendu.
    /// </summary>
    public ThemePreference Theme { get; init; } = ThemePreference.Dark;

    /// <summary>
    /// Langue de l'UI. Seul <see cref="French"/> est une valeur active aujourd'hui (l'anglais
    /// arrivera avec la traduction de l'app) ; le champ existe déjà, structure prête, pour ne pas
    /// avoir à migrer le schéma le jour où une deuxième langue existera.
    /// </summary>
    public string Language { get; init; } = French;

    /// <summary>Préférences de téléchargement (parallélisme).</summary>
    public DownloadPreferences Downloads { get; init; } = DownloadPreferences.Default;

    /// <summary>
    /// Vrai une fois l'écran de premier lancement vu (bouton « Commencer »/« Passer », ou toute
    /// action de sa checklist, voir <c>Prospect.Desktop.ViewModels.FirstRun.FirstRunScreenViewModel</c>).
    /// Un <c>bool</c> plutôt qu'un ajout au pipeline de migrations : <see langword="false"/> est à la
    /// fois le défaut CLR ET le défaut voulu (« jamais vu » pour une installation neuve), donc le
    /// piège de <see cref="Normalized"/> ne s'applique pas ici (rien à distinguer entre « absent du
    /// JSON » et « explicitement false », les deux étant la même valeur exploitable) — voir le point
    /// ouvert traité dans la PR qui a introduit ce champ.
    /// </summary>
    public bool HasSeenFirstRun { get; init; }

    /// <summary>Réglages par défaut d'une installation neuve (fichier absent au premier lancement).</summary>
    public static ProspectSettings CreateDefault() => new() { SchemaVersion = CurrentSchemaVersion };

    /// <summary>
    /// Copie avec ses valeurs réparées : bornes de <see cref="Downloads"/> respectées (voir
    /// <see cref="DownloadPreferences.Clamped"/>), et tout champ de référence absent du JSON
    /// d'origine ramené à son défaut documenté plutôt que laissé à <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// Cette seconde réparation existe pour une raison précise, découverte en écrivant les tests de
    /// <see cref="SettingsService"/> : <c>System.Text.Json</c> ne rejoue JAMAIS l'initialiseur d'une
    /// propriété <c>init</c> (<c>= DownloadPreferences.Default</c> ci-dessus, par exemple) quand le
    /// champ JSON correspondant est absent — y compris via le pipeline de migrations, où un document
    /// migré d'un schéma plus ancien ne porte souvent pas encore les champs ajoutés depuis. Sans ce
    /// filet, un <c>prospect.json</c> partiel (schéma ancien, ou simplement modifié à la main) se
    /// désérialise avec <see cref="Downloads"/> ou <see cref="Language"/> à <see langword="null"/>
    /// malgré leur défaut affiché juste au-dessus, un piège général à ce mécanisme de sérialisation
    /// et non spécifique à ce type. Appelée après toute lecture disque ou mutation
    /// (<see cref="SettingsService"/>).
    /// </remarks>
    public ProspectSettings Normalized() => this with
    {
        Language = Language ?? French,
        Downloads = (Downloads ?? DownloadPreferences.Default).Clamped(),
    };
}