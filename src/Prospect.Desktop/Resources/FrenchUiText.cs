using System.Globalization;

using Prospect.Core.Diagnostics;
using Prospect.Core.GameVersions;
using Prospect.Core.Instances;
using Prospect.Core.ModDb;
using Prospect.Core.Runtime;
using Prospect.Core.Settings;

namespace Prospect.Desktop.Resources;

/// <summary>
/// Table française, la voix d'origine du produit (design/readme.md, « Content fundamentals » :
/// tutoiement, casse de phrase, registre minier discret, jamais d'emoji). C'est aussi la table de
/// repli : toute langue inconnue relue du disque revient ici (voir
/// <see cref="ProspectSettings.NormalizeLanguage"/>).
/// </summary>
internal sealed class FrenchUiText : UiTextTable
{
    private readonly FrenchModsText _mods = new();

    public FrenchUiText() => Instance = new FrenchInstanceText(new FrenchInstanceBackupsText(), new FrenchDoctorText(_mods));

    internal override string Language => ProspectSettings.French;

    internal override ShellText Shell { get; } = new FrenchShellText();

    internal override WizardText Wizard { get; } = new FrenchWizardText();

    internal override DialogsText Dialogs { get; } = new FrenchDialogsText();

    internal override ToastsText Toasts { get; } = new FrenchToastsText();

    internal override HomeText Home { get; } = new FrenchHomeText();

    internal override DownloadsText Downloads { get; } = new FrenchDownloadsText();

    internal override VersionsText Versions { get; } = new FrenchVersionsText();

    internal override BrokenInstancesText BrokenInstances { get; } = new FrenchBrokenInstancesText();

    internal override InstanceText Instance { get; }

    internal override AccountText Account { get; } = new FrenchAccountText();

    internal override ModsText Mods => _mods;


    internal override MigrationText Migration { get; } = new FrenchMigrationText();

    internal override SettingsText Settings { get; } = new FrenchSettingsText();

    internal override FirstRunText FirstRun { get; } = new FrenchFirstRunText();

    internal override TimeText Time { get; } = new FrenchTimeText();

    internal override LogsText Logs { get; } = new FrenchLogsText();
}

internal sealed class FrenchShellText : ShellText
{
    internal override string NavHome => "Accueil";

    internal override string NavMods => "Mods";

    internal override string NavVersions => "Versions";

    internal override string NavLogs => "Journaux";

    internal override string NavSettings => "Réglages";
}

internal sealed class FrenchWizardText : WizardText
{
    internal override string NameRequired => "Le nom de l'instance ne peut pas être vide.";

    internal override string VersionInstalled => "installée";

    internal override string InstallCanceled => "Installation annulée. L'instance n'a pas été créée.";

    internal override string SummaryNoVersion => "Choisis une version du jeu à l'étape précédente.";

    internal override string NameBeingDeleted
        => "Ce nom est encore pris. Une instance qui le portait est en cours de suppression : attends la fin, ou choisis-en un autre.";

    internal override IReadOnlyList<string> StepLabels { get; } = ["Nom", "Version", "Icône", "Résumé"];

    internal override string IconLabel(string iconChoiceKey) => iconChoiceKey switch
    {
        "package" => "Caisse",
        "star" => "Étoile",
        "hard-drive" => "Disque",
        "image" => "Image",
        _ => "Par défaut",
    };

    internal override string VersionToDownload(string displaySize) => $"{displaySize} à télécharger";

    internal override string SummaryAlreadyInstalled(string version)
        => $"La version {version} est déjà installée, rien à télécharger. L'instance sera prête immédiatement.";

    internal override string SummaryWillDownload(string version)
        => $"La version {version} sera téléchargée et installée avant la création de l'instance.";
}

internal sealed class FrenchDialogsText : DialogsText
{
    internal override string RenameEmptyError => "Le nom de l'instance ne peut pas être vide.";

    internal override string DuplicateEmptyError => "Le nom de la copie ne peut pas être vide.";

    internal override string DeleteBackupMessage
        => "Cette sauvegarde sera supprimée définitivement. Les autres sauvegardes de l'instance ne sont pas concernées.";

    internal override string DuplicateSuggestedName(string sourceName) => $"{sourceName} (copie)";

    internal override string DuplicateProgressLabel(int filesCopied, int totalFiles)
        => totalFiles == 0 ? "Préparation de la copie…" : $"Copie des fichiers ({filesCopied}/{totalFiles})";

    internal override string RenameTitle(string instanceName) => $"Renommer « {instanceName} » ?";

    internal override string DuplicateTitle(string sourceName) => $"Dupliquer « {sourceName} » ?";

    internal override string DeleteTitle(string instanceName) => $"Supprimer « {instanceName} » ?";

    internal override string DeleteMessage(string instanceName)
        => $"Toutes les données de « {instanceName} » seront supprimées définitivement, mondes et mods compris. Cette action est irréversible.";

    internal override string DeleteInProgress => "Suppression en cours… Ça peut prendre un moment sur une instance avec de gros mondes.";

    internal override string DeleteProgress(int deletedFiles, int totalFiles)
        => totalFiles == 0 ? DeleteInProgress : $"Suppression des fichiers ({deletedFiles}/{totalFiles})";

    internal override string DeletePartialFailure(string directory)
        => $"La suppression est incomplète. Des fichiers restent dans « {directory} ». Ferme le jeu s'il tourne encore, puis réessaie.";

    internal override string RestoreBackupTitle(string instanceName) => $"Restaurer « {instanceName} » ?";

    internal override string RestoreBackupMessage(string instanceName, string dateText)
        => $"« {instanceName} » reviendra à son état du {dateText}. Mondes, configs et mods compris. L'état actuel est sauvegardé avant, par sécurité.";

    internal override string DeleteBackupTitle(string dateText) => $"Supprimer la sauvegarde du {dateText} ?";
}

internal sealed class FrenchToastsText : ToastsText
{
    internal override string InstanceCreatedTitle => "Instance créée";

    internal override string InstanceRenamedTitle => "Instance renommée";

    internal override string InstanceDuplicatedTitle => "Instance dupliquée";

    internal override string InstanceDeletedTitle => "Instance supprimée";

    internal override string VersionInstalledTitle => "Version installée";

    internal override string VersionUninstalledTitle => "Version désinstallée";

    internal override string LaunchSettingsSavedTitle => "Réglages de lancement enregistrés";


    internal override string LogsExportedTitle => "Journaux exportés";

    internal override string BackupCreatedTitle => "Sauvegarde créée";

    internal override string BackupRestoredTitle => "Sauvegarde restaurée";

    internal override string BackupDeletedTitle => "Sauvegarde supprimée";

    internal override string AutoBackupFailedTitle => "Sauvegarde automatique ratée";

    internal override string AutoBackupFailedMessage
        => "Aucune sauvegarde n'a été prise. Le lancement continue quand même, sans filet cette fois. Vérifie l'espace disque disponible.";
}

internal sealed class FrenchHomeText : HomeText
{
    internal override string NoSearchResults(string query) => $"Aucune instance ne correspond à « {query} ».";

    internal override string UpdatesBadge(int count) => count == 1 ? "1 mise à jour" : $"{count} mises à jour";
}

internal sealed class FrenchLogsText : LogsText
{
    internal override string Subtitle(int shownLines, int fileCount)
    {
        var lines = shownLines switch
        {
            0 => "aucune ligne",
            1 => "1 ligne affichée",
            _ => $"{shownLines} dernières lignes",
        };

        var files = fileCount switch
        {
            0 => "aucun journal à exporter",
            1 => "1 journal à exporter",
            _ => $"{fileCount} journaux à exporter",
        };

        return $"{lines} · {files}";
    }

    internal override string ExportPickerTitle => "Exporter les journaux";

    internal override string ExportFileName => "prospect-journaux.zip";

    internal override string ExportedToastDescription(int fileCount) => fileCount switch
    {
        0 => "Aucun journal à emporter : l'archive est vide.",
        1 => "1 journal dans l'archive.",
        _ => $"{fileCount} journaux dans l'archive.",
    };
}

internal sealed class FrenchDownloadsText : DownloadsText
{
    internal override string Queued => "en attente";

    internal override string Verifying => "vérification du fichier";

    internal override string GenericFailure => "Échec du téléchargement.";

    internal override string Summary(int running, int queued) => (running, queued) switch
    {
        (0, 0) => string.Empty,
        (_, 0) => $"{running} en cours",
        (0, _) => $"{queued} en attente",
        _ => $"{running} en cours · {queued} en attente",
    };

    internal override string OutcomeCompleted => "terminé";

    internal override string OutcomeFailed => "échec";

    internal override string OutcomeCanceled => "annulé";
}

internal sealed class FrenchVersionsText : VersionsText
{
    internal override string StaleCatalog
        => "Le catalogue n'a pas pu être actualisé. Les versions affichées viennent du dernier relevé connu.";

    internal override string UnavailableCatalog
        => "Le catalogue est injoignable. Seules les versions déjà installées sont affichées.";

    internal override string InstallLandedElsewhere(string targetDirectory)
        => "L'installation n'a rien laissé au bon endroit. L'installeur s'est terminé sans erreur, mais le jeu "
            + $"n'est pas dans « {targetDirectory} » : il a probablement été posé par-dessus une installation "
            + "existante de Vintage Story. Rien n'a été marqué comme installé, désinstalle l'ancienne copie puis réessaie.";

    internal override string ArchiveMissingExecutable(string targetDirectory)
        => "L'installation n'a pas abouti. L'archive du jeu s'est extraite sans erreur, mais l'exécutable "
            + $"n'est pas là où il devrait être dans « {targetDirectory} ». Rien n'a été marqué comme installé, réessaie.";

    internal override string Subtitle(int installedCount, string totalSize) => installedCount switch
    {
        0 => "Aucune version installée · dossier partagé entre les instances",
        1 => $"1 installée · {totalSize} · dossier partagé entre les instances",
        _ => $"{installedCount} installées · {totalSize} · dossier partagé entre les instances",
    };

    internal override string InstalledOn(DateTimeOffset installedUtc)
        => $"installée le {installedUtc.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}";

    internal override string PhaseLabel(GameInstallPhase phase) => phase switch
    {
        GameInstallPhase.Downloading => "Téléchargement",
        GameInstallPhase.Verifying => "Vérification",
        GameInstallPhase.Installing => "Installation",
        GameInstallPhase.Completed => "Terminé",
        _ => string.Empty,
    };

    internal override string InstallDetail(int percent)
        => $"extraction · {percent.ToString(CultureInfo.InvariantCulture)} %";

    internal override string InstallEstimateDetail(int percent)
        => $"installation · ~{percent.ToString(CultureInfo.InvariantCulture)} %";

    internal override string InstallerPromptNotice
        => "Réponds non si une fenêtre propose de désinstaller une ancienne version : c'est l'installeur du jeu qui l'ouvre, pas Prospect, et un oui désinstallerait une version déjà installée. Prospect, lui, installe dans son propre dossier sans toucher au reste.";

    internal override string BrokenReason(GameInstallBrokenReason reason) => reason switch
    {
        GameInstallBrokenReason.MissingCompletionMarker => "installation interrompue, à réinstaller",
        GameInstallBrokenReason.UnreadableVersionName => "nom de dossier illisible",
        _ => "raison inconnue",
    };

    internal override string UninstallTitle(string version) => $"Désinstaller la version {version} ?";

    internal override string UninstallMessage(string version)
        => $"Les fichiers de la version {version} seront supprimés du dossier partagé. Tu pourras la réinstaller depuis le catalogue.";

    internal override string UninstallInProgress => "Désinstallation en cours…";

    internal override string UninstallProgress(int deletedFiles, int totalFiles)
        => totalFiles == 0 ? UninstallInProgress : $"Suppression des fichiers ({deletedFiles}/{totalFiles})";

    internal override string UninstallPartialFailure(string directory)
        => $"La désinstallation est incomplète. Des fichiers restent dans « {directory} ». Ferme le jeu s'il tourne encore, puis réessaie.";

    internal override string UninstallDependents(IReadOnlyList<string> instanceNames)
    {
        var quoted = instanceNames.Select(name => $"« {name} »").ToArray();
        var joined = quoted.Length == 1
            ? quoted[0]
            : $"{string.Join(", ", quoted[..^1])} et {quoted[^1]}";

        return quoted.Length == 1
            ? $"L'instance {joined} utilise cette version et ne pourra plus être lancée."
            : $"Les instances {joined} utilisent cette version et ne pourront plus être lancées.";
    }
}

internal sealed class FrenchBrokenInstancesText : BrokenInstancesText
{
    internal override string Reason(InstanceBrokenReason reason) => reason switch
    {
        InstanceBrokenReason.MissingMetadataFile => "fichier instance.json manquant",
        InstanceBrokenReason.CorruptedMetadataFile => "fichier instance.json illisible",
        InstanceBrokenReason.UnsupportedSchemaVersion => "créée par une version plus récente de Prospect",
        _ => "raison inconnue",
    };
}

internal sealed class FrenchInstanceText(InstanceBackupsText backups, DoctorText doctor) : InstanceText(backups, doctor)
{
    internal override string VersionNotInstalledTitle => "Version non installée";

    internal override string RuntimeMissingTitle => "Composant .NET manquant";

    internal override string MacNotSupportedTitle => "macOS non pris en charge";

    internal override string AlreadyRunningTitle => "Session déjà en cours";

    internal override string GenericLaunchErrorTitle => "Lancement impossible";

    internal override string EnvVarsInvalidLine => "Chaque ligne doit être au format CLE=valeur.";

    internal override string StopConfirmTitle(string instanceName) => $"Arrêter « {instanceName} » ?";

    internal override string StopConfirmMessage(string instanceName)
        => $"Le jeu de « {instanceName} » va s'arrêter immédiatement. Toute progression non sauvegardée sera perdue.";
}

internal sealed class FrenchInstanceBackupsText : InstanceBackupsText
{
    internal override string CreateFailedTitle => "Sauvegarde impossible";

    internal override string KeepCountChoiceLabel(int count)
        => count == 1 ? "1 sauvegarde conservée" : $"{count} sauvegardes conservées";

    internal override string CreateProgress(int filesProcessed, int totalFiles)
        => totalFiles == 0 ? "Préparation de la sauvegarde…" : $"Sauvegarde en cours ({filesProcessed}/{totalFiles})";

    internal override string AutoBackupProgress(int filesProcessed, int totalFiles)
        => totalFiles == 0 ? "Sauvegarde automatique…" : $"Sauvegarde automatique ({filesProcessed}/{totalFiles})…";
}

internal sealed class FrenchDoctorText(ModsText mods) : DoctorText(mods)
{
    internal override string InstallAction => "Installer";

    internal override string ReinstallAction => "Réinstaller";

    internal override string OpenModsAction => "Voir les mods";

    internal override string CheckUpdatesAction => "Vérifier les mises à jour";

    internal override string InstallDependencyAction(string modIdString) => $"Installer « {modIdString} »…";

    internal override string UpdateDependencyAction(string modIdString) => $"Mettre à jour « {modIdString} »…";

    internal override string AllClearTitle => "Tout est en ordre";

    internal override string AllClearDescription => "Aucun problème détecté sur les cinq vérifications locales.";

    internal override string CompatibilityUnknown
        => "Compatibilité inconnue. Aucun mod de l'instance n'a été confronté à sa version du jeu. Lance une vérification des mises à jour pour le savoir.";

    internal override string RuntimeIndeterminate
        => "Composant .NET indéterminé. Prospect n'a pas su lequel cette version du jeu réclame. Le lancement le dira si ça bloque.";

    internal override string ErrorsGroupTitle(int count) => count == 1 ? "1 point à corriger" : $"{count} points à corriger";

    internal override string WarningsGroupTitle(int count) => count == 1 ? "1 point à surveiller" : $"{count} points à surveiller";

    internal override string GameVersionMessage(GameVersionDoctorResult result) => result.Status switch
    {
        GameVersionDoctorStatus.Missing => $"La version {result.Version} n'est pas installée.",
        GameVersionDoctorStatus.Incomplete
            => $"L'installation de la version {result.Version} est incomplète, probablement interrompue en cours de route.",
        _ => string.Empty,
    };

    internal override string RuntimeMessage(RuntimeCheckResult runtime) => runtime.Availability switch
    {
        RuntimeAvailability.Missing
            => $"Cette version du jeu a besoin de .NET {runtime.Requirement.Version}, qui n'est pas installé sur cet ordinateur.",
        RuntimeAvailability.Indeterminate => RuntimeIndeterminate,
        _ => string.Empty,
    };

    internal override string CompatibilityMessage(ModCompatibilityDoctorResult compatibility, string gameVersionText)
    {
        if (compatibility.Severity != InstanceDoctorSeverity.Warning)
        {
            return string.Empty;
        }

        if (compatibility.IsWhollyUnknown)
        {
            return CompatibilityUnknown;
        }

        var uncertain = compatibility.ApproximateCount + compatibility.UnknownCount;

        return uncertain == 1
            ? $"1 mod à la compatibilité non confirmée. Son auteur ne l'a pas déclaré pour {gameVersionText}. Lance une vérification des mises à jour pour en savoir plus."
            : $"{uncertain} mods à la compatibilité non confirmée. Leurs auteurs ne les ont pas déclarés pour {gameVersionText}. Lance une vérification des mises à jour pour en savoir plus.";
    }

    internal override string DiskSpaceLow(string availableText)
        => $"Espace disque faible : {availableText} restants sur le volume de Prospect.";

    protected override string UnidentifiedMessage(string modDisplayName, ModInfoProblem problem)
        => $"« {modDisplayName} » n'a pas pu être identifié ({Mods.UnidentifiedReason(problem)}).";

    protected override string DependencyIssueMessage(string modDisplayName, ModDependencyIssue dependency) => dependency.Status switch
    {
        ModDependencyStatus.Missing => $"« {modDisplayName} » a besoin de {dependency.ModIdString}, absent de l'instance.",
        ModDependencyStatus.Disabled => $"« {modDisplayName} » a besoin de {dependency.ModIdString}, présent mais désactivé.",
        ModDependencyStatus.TooOld
            => $"« {modDisplayName} » a besoin de {dependency.ModIdString} {dependency.Requirement} au minimum, version installée trop ancienne.",
        _ => $"« {modDisplayName} » a besoin de {dependency.ModIdString}.",
    };
}

internal sealed class FrenchAccountText : AccountText
{
    internal override string InvalidCredentials => "Adresse ou mot de passe incorrect.";

    internal override string InvalidTwoFactorCode
        => "Ce code n'est pas le bon. Il change toutes les trente secondes, retape le dernier affiché.";

    internal override string Refused
        => "La connexion a été refusée. Vérifie ton compte sur vintagestory.at, puis réessaie.";

    internal override string ServiceUnavailable
        => "Le service de compte de Vintage Story ne répond pas. Vérifie ta connexion et réessaie.";

    internal override string UnknownPlayerName => "ce compte";

    internal override string SignOutConfirmMessage
        => "Ta session sera effacée de cet ordinateur. Le jeu se lancera sans multijoueur tant que tu ne t'es pas connecté à nouveau.";

    internal override string SignOutConfirmTitle(string playerName) => $"Déconnecter « {playerName} » ?";

    internal override string SignedInSubtitle(string playerName) => $"Connecté en tant que {playerName}.";
}

internal sealed class FrenchModsText : ModsText
{
    internal override string AllVersions => "Toutes les versions";

    internal override string UnknownVersion => "version inconnue";

    internal override string ProvenanceManual => "manuel";

    internal override string EmptyResultsTitle => "Aucun mod ne correspond";

    internal override string EnabledTitle => "Mod activé";

    internal override string DisabledTitle => "Mod désactivé";

    internal override string UninstalledTitle => "Mod retiré";

    internal override string FileGoneTitle => "Fichier introuvable";

    internal override string InstallFailedTitle => "Installation impossible";

    internal override string NoCompatibleReleaseTitle => "Aucune version compatible";

    internal override string DetailUnavailableTitle => "Fiche indisponible";

    internal override string PickInstanceTitle => "Choisis une instance";

    internal override string PickInstanceMessage => "Choisis l'instance de destination avant d'installer un mod.";

    internal override string InstallAction => "Installer";

    internal override string ManageAction => "Gérer";

    internal override string InstalledBadge(string? version)
        => string.IsNullOrWhiteSpace(version) ? "Installé" : $"Installé · {version}";

    internal override string StaleCatalog
        => "La liste des mods n'a pas pu être actualisée. Ceux affichés viennent du dernier relevé connu.";

    internal override string OfflineEmptyTitle => "Aucun résultat hors ligne";

    internal override string OfflineEmptyDescription
        => "Le ModDB est injoignable. La liste gardée en mémoire est trop ancienne pour servir. Vérifie ta connexion, puis réessaie.";

    internal override string CheckUpdatesFailedTitle => "Vérification impossible";

    internal override string UpdateFailedTitle => "Mise à jour impossible";

    internal override string LinkSourceCode => "Code source";

    internal override string LinkIssues => "Tickets";

    internal override string LinkWiki => "Wiki";

    internal override string LinkHomepage => "Site du mod";

    internal override string ApproximateReleaseTag => "compatibilité supposée";

    internal override string IncompatibleReleaseTag => "non déclarée compatible";

    internal override string ShowAllReleases => "Montrer toutes les versions";

    internal override string ShowCompatibleReleasesOnly => "Ne montrer que les compatibles";

    internal override string ReleaseChoiceCount(int count) => count switch
    {
        0 => "aucune version compatible",
        1 => "1 version compatible",
        _ => $"{FormatCount(count)} versions compatibles",
    };

    internal override string IncompatibleReleaseWarning(string gameVersion, IReadOnlyList<string> taggedVersions)
        => taggedVersions.Count == 0
            ? $"Rien ne garantit que cette version fonctionne. Son auteur ne l'a déclarée pour aucune version du jeu. Elle peut très bien tourner en {gameVersion}, à tes risques."
            : $"Cette version n'est pas déclarée pour {gameVersion}. Son auteur l'a publiée pour {CompatibleVersions(taggedVersions)}. Les compatibilités sont cochées à la main et prennent du retard : elle fonctionnera peut-être, à tes risques.";

    internal override string InstallAnywayReason(IReadOnlyList<string> taggedVersions)
        => taggedVersions.Count == 0
            ? "aucune version de jeu déclarée"
            : $"déclarée pour {CompatibleVersions(taggedVersions)}";

    protected override CultureInfo NumberCulture { get; } = CultureInfo.GetCultureInfo("fr-FR");

    internal override string Subtitle(int indexedCount) => indexedCount switch
    {
        0 => "ModDB officiel",
        1 => "ModDB officiel · 1 mod disponible",
        _ => $"ModDB officiel · {FormatCount(indexedCount)} mods disponibles",
    };

    internal override string ShownCount(int shown, int total)
        => shown >= total
            ? total switch
            {
                0 => string.Empty,
                1 => "1 mod",
                _ => $"{FormatCount(total)} mods",
            }
            : $"{FormatCount(shown)} sur {FormatCount(total)} mods affichés";

    internal override string ShownCountCapped(int shown, int total)
        => $"{FormatCount(shown)} sur {FormatCount(total)} mods affichés · affine la recherche pour voir les autres";

    internal override string ByAuthor(string author)
        => string.IsNullOrWhiteSpace(author) ? "auteur inconnu" : $"par {author}";

    internal override string EmptyResultsDescription(string query)
        => string.IsNullOrWhiteSpace(query)
            ? "Aucun mod ne correspond aux filtres actifs."
            : $"Aucun mod ne correspond à « {query.Trim()} ».";

    internal override string DetailMeta(string author, int downloads)
        => $"{ByAuthor(author)} · {FormatCount(downloads)} téléchargements";

    internal override string CompatibleVersions(IReadOnlyList<string> tags) => tags.Count switch
    {
        0 => "aucune version de jeu déclarée",
        1 => tags[0],
        <= 3 => string.Join(", ", tags),
        _ => $"{string.Join(", ", tags.Take(3))} et {tags.Count - 3} autres",
    };

    internal override string SideLabel(ModDbSide side) => side switch
    {
        ModDbSide.Client => "client",
        ModDbSide.Server => "serveur",
        ModDbSide.Both => "client et serveur",
        _ => string.Empty,
    };

    // DÉRIVE CONNUE, LAISSÉE EN PLACE FAUTE DE PLACE, au sens propre. Le glossaire tranche pour
    // « client et serveur » (c'est déjà ce que dit SideLabel(ModDbSide) sur la carte du navigateur),
    // donc le même mod se décrit de deux façons selon l'écran. La correction a été essayée et
    // annulée : le libellé long ajoute une quarantaine de points à la colonne Auto de droite de la
    // rangée de mods, ce qui écrase la colonne * du milieu jusqu'à faire déborder l'auteur et les
    // pastilles de journal (deux gardes d'invariants de boîtes le montrent à 960 points). Corriger
    // le mot demande d'abord de donner un budget de largeur à cette rangée, ce qui est un chantier
    // à part. À reprendre avec lui.
    internal override string SideLabel(ModSide? side) => side switch
    {
        ModSide.Client => "client",
        ModSide.Server => "serveur",
        ModSide.Universal => "universel",
        _ => string.Empty,
    };

    internal override string RowAuthor(IReadOnlyList<string> authors) => authors.Count switch
    {
        0 => "auteur inconnu",
        1 => $"par {authors[0]}",
        _ => $"par {authors[0]} et {authors.Count - 1} autre{(authors.Count > 2 ? "s" : string.Empty)}",
    };

    internal override string UnidentifiedReason(ModInfoProblem problem) => problem switch
    {
        ModInfoProblem.MissingModInfo => "aucun modinfo.json dans l'archive",
        ModInfoProblem.MalformedJson => "modinfo.json illisible",
        ModInfoProblem.MissingIdentity => "modinfo.json sans identifiant ni nom",
        ModInfoProblem.UnreadableArchive => "archive illisible",
        _ => string.Empty,
    };

    internal override string InstalledSummary(int total, int enabled) => total switch
    {
        0 => string.Empty,
        1 => enabled == 1 ? "1 mod installé" : "1 mod installé, désactivé",
        _ => enabled == total ? $"{total} mods installés" : $"{total} mods installés · {total - enabled} désactivés",
    };

    internal override string PlanTitle(string modName) => $"Installer « {modName} » ?";

    internal override string PlanMessage(string version, string instanceName)
        => $"La version {version} sera installée dans « {instanceName} ».";

    internal override string ReplacePlanTitle(string modName) => $"Remplacer « {modName} » ?";

    internal override string ReplacePlanMessage(string currentVersion, string version, string instanceName)
        => string.IsNullOrEmpty(currentVersion)
            ? $"La copie déjà installée dans « {instanceName} » sera remplacée par la version {version}. Son état activé ou désactivé est conservé."
            : $"La version {currentVersion} installée dans « {instanceName} » sera remplacée par la version {version}. Son état activé ou désactivé est conservé.";

    internal override string ApproximateWarning(string gameVersion)
        => $"Aucune version n'est déclarée pour {gameVersion}. Celle-ci est proposée parce qu'elle cible la même série. Son auteur ne l'a pas confirmée, elle peut ne pas fonctionner.";

    internal override string DependencyReason(ModDependencyIssue? issue) => issue?.Status switch
    {
        ModDependencyStatus.Missing when issue.ReportedByModDb && issue.Requirement.IsAny => "signalée par le ModDB",
        ModDependencyStatus.Missing => "absente de l'instance",
        ModDependencyStatus.TooOld => $"version installée trop ancienne, {issue.Requirement} au minimum",
        _ => string.Empty,
    };

    internal override string DependenciesNotOnModDb(IReadOnlyList<string> identifiers)
        => identifiers.Count == 0
            ? string.Empty
            : $"Introuvable{(identifiers.Count > 1 ? "s" : string.Empty)} sur le ModDB : {string.Join(", ", identifiers)}. À installer à la main si le mod en a besoin.";

    internal override string DependenciesWithoutCompatibleRelease(IReadOnlyList<string> names, string gameVersion)
        => names.Count == 0
            ? string.Empty
            : $"Aucune version pour Vintage Story {gameVersion} : {string.Join(", ", names)}. "
                + $"{(names.Count > 1 ? "Ces mods existent bien sur le ModDB, mais leurs auteurs n'ont" : "Ce mod existe bien sur le ModDB, mais son auteur n'a")} "
                + "rien publié pour cette version du jeu, et les compatibilités sont cochées à la main, donc elles prennent du retard. "
                + "Tu peux quand même installer la dernière version publiée, en connaissance de cause.";

    internal override string DisabledDependencies(IReadOnlyList<string> identifiers)
        => identifiers.Count == 0
            ? string.Empty
            : $"Présent{(identifiers.Count > 1 ? "s" : string.Empty)} mais désactivé{(identifiers.Count > 1 ? "s" : string.Empty)} : {string.Join(", ", identifiers)}. Réactive-les depuis l'onglet Mods de l'instance.";

    internal override string InstalledTitle(string modName) => $"{modName} installé";

    internal override string InstalledMessage(int count, string instanceName)
        => count > 1 ? $"{count} mods installés dans « {instanceName} »" : $"Installé dans « {instanceName} »";

    internal override string UninstallTitle(string modName) => $"Retirer « {modName} » ?";

    internal override string UninstallMessage(string fileName)
        => $"Le fichier {fileName} sera retiré du dossier Mods de l'instance. Tu pourras le réinstaller depuis le ModDB.";

    internal override string UninstallDependents(IReadOnlyList<string> modNames)
    {
        if (modNames.Count == 0)
        {
            return string.Empty;
        }

        var quoted = modNames.Select(name => $"« {name} »").ToArray();
        var joined = quoted.Length == 1
            ? quoted[0]
            : $"{string.Join(", ", quoted[..^1])} et {quoted[^1]}";

        return quoted.Length == 1
            ? $"Le mod {joined} en dépend et risque de ne plus fonctionner."
            : $"Les mods {joined} en dépendent et risquent de ne plus fonctionner.";
    }

    internal override string LastCheckedLabel(string relativeCheckedText) => $"Dernière vérification : {relativeCheckedText}";

    internal override string UpdatesAvailableTitle(int count) => count switch
    {
        0 => string.Empty,
        1 => "1 mise à jour disponible",
        _ => $"{count} mises à jour disponibles",
    };

    internal override string CheckVerdict(int updateCount, int undeclaredCount, int modCount)
    {
        if (modCount == 0)
        {
            return "Aucun mod à vérifier.";
        }

        var found = (updateCount, undeclaredCount) switch
        {
            (0, 0) => "tout est à jour",
            (0, 1) => "1 version plus récente existe, non déclarée pour ta version du jeu",
            (0, _) => $"{undeclaredCount} versions plus récentes existent, non déclarées pour ta version du jeu",
            (1, 0) => "1 mise à jour disponible",
            (_, 0) => $"{updateCount} mises à jour disponibles",
            (1, 1) => "1 mise à jour disponible, et 1 version plus récente non déclarée",
            (1, _) => $"1 mise à jour disponible, et {undeclaredCount} versions plus récentes non déclarées",
            (_, 1) => $"{updateCount} mises à jour disponibles, et 1 version plus récente non déclarée",
            _ => $"{updateCount} mises à jour disponibles, et {undeclaredCount} versions plus récentes non déclarées",
        };

        return modCount == 1 ? $"1 mod vérifié : {found}." : $"{modCount} mods vérifiés : {found}.";
    }

    internal override string UndeclaredUpdateReason(string version, IReadOnlyList<string> taggedVersions)
        => $"{version} est publiée, déclarée pour {CompatibleVersions(taggedVersions)}";

    internal override string UpdatePlanTitle(string modName) => $"Mettre à jour « {modName} » ?";

    internal override string UpdatePlanMessage(string currentVersion, string targetVersion)
        => $"La version {currentVersion} sera remplacée par la {targetVersion}.";

    internal override string UpdateDependentsNote(IReadOnlyList<string> modNames)
    {
        if (modNames.Count == 0)
        {
            return string.Empty;
        }

        var quoted = modNames.Select(name => $"« {name} »").ToArray();
        var joined = quoted.Length == 1
            ? quoted[0]
            : $"{string.Join(", ", quoted[..^1])} et {quoted[^1]}";

        return quoted.Length == 1
            ? $"{joined} dépend de ce mod."
            : $"{joined} dépendent de ce mod.";
    }

    internal override string UpdatedTitle(string modName) => $"{modName} mis à jour";

    internal override string UpdatedMessage(string targetVersion) => $"Version {targetVersion} installée.";

    internal override string BulkUpdateDoneTitle(int count) => count switch
    {
        0 => "Aucune mise à jour appliquée",
        1 => "1 mod mis à jour",
        _ => $"{count} mods mis à jour",
    };

    internal override string BulkUpdateFailures(IReadOnlyList<BulkUpdateFailure> failures)
        => failures.Count == 0
            ? string.Empty
            : $"Échec pour {string.Join(", ", failures.Select(failure => failure.ModName))}.";

    internal override string LogErrorsBadge(int count) => count == 1
        ? "1 erreur au dernier lancement"
        : $"{count} erreurs au dernier lancement";

    internal override string LogWarningsBadge(int count) => count == 1
        ? "1 avertissement"
        : $"{count} avertissements";

    internal override string LogProblemTooltip(IReadOnlyList<string> samples) => samples.Count == 0
        ? string.Empty
        : "Ce que le jeu a écrit au dernier lancement :" + Environment.NewLine + string.Join(Environment.NewLine, samples);

    internal override string WorksWithBadge(string modName, int others) => others <= 0
        ? $"fonctionne avec {modName}"
        : $"fonctionne avec {modName} et {others} autre{(others == 1 ? string.Empty : "s")}";

    internal override string ExpectsContentBadge(string modName, int others) => others <= 0
        ? $"attend du contenu de {modName}"
        : $"attend du contenu de {modName} et {others} autre{(others == 1 ? string.Empty : "s")}";

    internal override string IntegrationTooltipLine(string modName, bool resolved) => resolved
        ? $"Référence le contenu de {modName}, présent."
        : $"Référence du contenu de {modName}, absent au dernier lancement.";

    internal override string IntegrationTooltip(IReadOnlyList<string> lines) => lines.Count == 0
        ? string.Empty
        : string.Join(Environment.NewLine, lines) + Environment.NewLine + "Relevé à la lecture du journal et des archives : indicatif, jamais bloquant.";
}


internal sealed class FrenchMigrationText : MigrationText
{
    internal override string Starting => "Préparation…";

    internal override string CompletedToastTitle => "Import terminé";

    internal override string ModCount(int count) => count switch
    {
        0 => "aucun mod",
        1 => "1 mod",
        _ => $"{count} mods",
    };

    internal override string DetectionSummary(int installationCount, int gameVersionCount)
    {
        var installations = installationCount switch
        {
            0 => "aucune installation",
            1 => "1 installation",
            _ => $"{installationCount} installations",
        };

        var gameVersions = gameVersionCount switch
        {
            0 => "aucune version du jeu",
            1 => "1 version du jeu",
            _ => $"{gameVersionCount} versions du jeu",
        };

        // « Trouvé : … » plutôt qu'une phrase à participe accordé. Le tour précédent donnait
        // « 1 installation et aucun moteur détectés », dont l'accord ne marche avec aucun des deux
        // décomptes possibles ; la forme en deux-points s'en passe et met le verdict devant.
        return $"Trouvé : {installations}, {gameVersions}";
    }

    internal override string AdoptingInstallationsPhase(int completedItems, int totalItems, string? currentItemLabel)
        => string.IsNullOrEmpty(currentItemLabel)
            ? $"Installations {completedItems}/{totalItems}"
            : $"Installations {completedItems}/{totalItems} · {currentItemLabel}";

    internal override string AdoptingEnginesPhase(int completedItems, int totalItems, string? currentItemLabel)
        => string.IsNullOrEmpty(currentItemLabel)
            ? $"Versions du jeu {completedItems}/{totalItems}"
            : $"Versions du jeu {completedItems}/{totalItems} · {currentItemLabel}";

    internal override string FilesCopied(int filesCopied, int totalFiles) => $"{filesCopied}/{totalFiles} fichiers";

    internal override string CompletedToastDescription(int adoptedInstallations, int adoptedEngines)
        => (adoptedInstallations, adoptedEngines) switch
        {
            (0, 0) => "Rien n'a été importé. Le rapport dit pourquoi, ligne par ligne.",
            (_, 0) => adoptedInstallations == 1 ? "1 instance créée." : $"{adoptedInstallations} instances créées.",
            (0, _) => adoptedEngines == 1 ? "1 version du jeu importée." : $"{adoptedEngines} versions du jeu importées.",
            _ => $"{adoptedInstallations} instance(s) créée(s), {adoptedEngines} version(s) du jeu importée(s).",
        };

    internal override string InstallationsAdoptedGroupTitle(int count)
        => count == 1 ? "1 instance créée" : $"{count} instances créées";

    internal override string InstallationsSkippedGroupTitle(int count)
        => count == 1 ? "1 installation ignorée" : $"{count} installations ignorées";

    internal override string InstallationsFailedGroupTitle(int count)
        => count == 1 ? "1 installation en échec" : $"{count} installations en échec";

    internal override string EnginesAdoptedGroupTitle(int count)
        => count == 1 ? "1 version du jeu importée" : $"{count} versions du jeu importées";

    internal override string EnginesSkippedGroupTitle(int count)
        => count == 1 ? "1 version du jeu ignorée" : $"{count} versions du jeu ignorées";

    internal override string EnginesFailedGroupTitle(int count)
        => count == 1 ? "1 version du jeu en échec" : $"{count} versions du jeu en échec";
}

internal sealed class FrenchSettingsText : SettingsText
{
    internal override string PickFolderTitle => "Dossier de VS Launcher";

    internal override string VslNotDetected => "Rien d'exploitable n'a été trouvé à cet emplacement.";

    internal override string ConcurrencyChoiceLabel(int count)
        => count == 1 ? "1 téléchargement à la fois" : $"{count} téléchargements simultanés";

    internal override string BackdropLabel(string backdropKey) => backdropKey switch
    {
        "ruins-clearing" => "Clairière aux ruines",
        "sunlit-hills" => "Collines ensoleillées",
        "village-lane" => "Ruelle du village",
        "lake-sail" => "Voile sur le lac",
        "dusk-reeds" => "Roseaux au couchant",
        "misty-yard" => "Cour embrumée",
        "reading-room" => "Cabinet de lecture",
        "stone-cellar" => "Cave de pierre",
        "overgrown-ruin" => "Ruine envahie",
        "crystal-vein" => "Veine de cristaux",
        // Le fond livré avec le thème verre, et le repli de toute clé inconnue : deux bassins
        // turquoise vus d'en haut.
        _ => "Bassins turquoise",
    };
}

internal sealed class FrenchFirstRunText : FirstRunText
{
    internal override string DataFolderTitle => "Dossier de données";

    internal override string GameVersionTitle => "Version du jeu";

    internal override string VslDetectedTitle => "Installations VS Launcher détectées";

    internal override string AccountTitle => "Compte Vintage Story";

    internal override string AccountSignedOut => "Facultatif : utile seulement pour jouer en multijoueur.";

    internal override string AccountSignInAction => "Se connecter";

    internal override string InstallVersionAction => "Installer";

    internal override string AdoptAction => "Importer";

    internal override string NoVersionInstalled => "aucune installée";

    internal override string InstalledVersionsSummary(int count, string mostRecentVersion) => count switch
    {
        1 => $"{mostRecentVersion} installée",
        _ => $"{count} versions installées, dont {mostRecentVersion}",
    };
}

internal sealed class FrenchTimeText : TimeText
{
    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");

    internal override string Never => "jamais";

    internal override string Today => "aujourd'hui";

    internal override string Yesterday => "hier";

    internal override string NeverPlayed => "jamais joué";

    internal override string PlayedUnderAnHour => "joué < 1 h";

    internal override string DaysAgo(int days) => $"il y a {days} jours";

    internal override string JustNow => "à l'instant";

    // Le singulier compte : « il y a 1 minutes » se remarque tout de suite.
    internal override string MinutesAgo(int minutes) => minutes <= 1 ? "il y a 1 minute" : $"il y a {minutes} minutes";

    internal override string HoursAgo(int hours) => hours <= 1 ? "il y a 1 heure" : $"il y a {hours} heures";

    internal override string AbsoluteDate(DateTime utcValue) => utcValue.ToString("d MMMM yyyy", French);

    internal override string PlayedHours(long hours) => $"joué {hours} h";
}