using System.Diagnostics.CodeAnalysis;
using System.Globalization;

using Prospect.Core.Diagnostics;
using Prospect.Core.GameVersions;
using Prospect.Core.Instances;
using Prospect.Core.ModDb;
using Prospect.Core.Modpacks;
using Prospect.Core.Runtime;

namespace Prospect.Desktop.Resources;

/// <summary>
/// Surface complète des textes UI produits par du code C# (voir <see cref="UiText"/>), déclarée
/// une fois et implémentée une fois par langue (<see cref="FrenchUiText"/>,
/// <see cref="EnglishUiText"/>).
/// </summary>
/// <remarks>
/// Des membres ABSTRAITS plutôt qu'un dictionnaire de clés : la parité des deux langues est alors
/// une erreur de compilation, pas un test à écrire : oublier une phrase en anglais empêche la
/// solution de compiler, et aucune langue ne peut porter un texte que l'autre n'a pas. Les rares
/// formats qui ne contiennent AUCUN mot (« nom · version », une date ISO) sont concrets sur la
/// classe de base : les redéclarer par langue inviterait à les faire diverger sans raison.
/// </remarks>
internal abstract class UiTextTable
{
    /// <summary>Valeur persistée qui sélectionne cette table (voir <c>ProspectSettings.Language</c>).</summary>
    internal abstract string Language { get; }

    internal abstract ShellText Shell { get; }

    internal abstract WizardText Wizard { get; }

    internal abstract DialogsText Dialogs { get; }

    internal abstract ToastsText Toasts { get; }

    internal abstract HomeText Home { get; }

    internal abstract DownloadsText Downloads { get; }

    internal abstract VersionsText Versions { get; }

    internal abstract BrokenInstancesText BrokenInstances { get; }

    internal abstract InstanceText Instance { get; }

    internal abstract AccountText Account { get; }

    internal abstract ModsText Mods { get; }

    internal abstract ModpacksText Modpacks { get; }

    internal abstract MigrationText Migration { get; }

    internal abstract SettingsText Settings { get; }

    internal abstract FirstRunText FirstRun { get; }

    internal abstract TimeText Time { get; }
}

/// <summary>
/// Textes du shell : les quatre entrées de la barre latérale, construites par
/// <c>ViewModels.Shell.ShellViewModel</c> et non liées en XAML (elles portent la page qu'elles
/// activent, voir <c>NavItemViewModel</c>).
/// </summary>
internal abstract class ShellText
{
    internal abstract string NavHome { get; }

    internal abstract string NavMods { get; }

    internal abstract string NavVersions { get; }

    internal abstract string NavSettings { get; }
}

/// <summary>Textes du wizard de création d'instance.</summary>
internal abstract class WizardText
{
    internal abstract string NameRequired { get; }

    internal abstract string VersionInstalled { get; }

    internal abstract string InstallCanceled { get; }

    internal abstract string SummaryNoVersion { get; }

    /// <summary>Les quatre étapes, dans l'ordre.</summary>
    internal abstract IReadOnlyList<string> StepLabels { get; }

    /// <summary>Libellé d'une icône proposée à l'étape 3, par sa clé (voir <c>WizardViewModel</c>).</summary>
    internal abstract string IconLabel(string iconChoiceKey);

    internal abstract string VersionToDownload(string displaySize);

    internal abstract string SummaryAlreadyInstalled(string version);

    internal abstract string SummaryWillDownload(string version);
}

/// <summary>Textes des dialogues de confirmation portant sur une instance ou une sauvegarde.</summary>
internal abstract class DialogsText
{
    internal abstract string RenameEmptyError { get; }

    internal abstract string DuplicateEmptyError { get; }

    internal abstract string DeleteBackupMessage { get; }

    internal abstract string DuplicateSuggestedName(string sourceName);

    internal abstract string DuplicateProgressLabel(int filesCopied, int totalFiles);

    internal abstract string DeleteTitle(string instanceName);

    internal abstract string DeleteMessage(string instanceName);

    internal abstract string RestoreBackupTitle(string instanceName);

    internal abstract string RestoreBackupMessage(string instanceName, string dateText);

    internal abstract string DeleteBackupTitle(string dateText);
}

/// <summary>Titres et messages des toasts.</summary>
internal abstract class ToastsText
{
    internal abstract string InstanceCreatedTitle { get; }

    internal abstract string InstanceRenamedTitle { get; }

    internal abstract string InstanceDuplicatedTitle { get; }

    internal abstract string InstanceDeletedTitle { get; }

    internal abstract string VersionInstalledTitle { get; }

    internal abstract string VersionUninstalledTitle { get; }

    internal abstract string LaunchSettingsSavedTitle { get; }

    internal abstract string ModpackExportedTitle { get; }

    internal abstract string BackupCreatedTitle { get; }

    internal abstract string BackupRestoredTitle { get; }

    internal abstract string BackupDeletedTitle { get; }

    // Avertissement bien visible (ToastTone.Warning), volontairement distinct d'un simple log :
    // c'est le filet de sécurité du joueur qui a raté, pas un confort accessoire comme
    // l'injection de session (voir GameLauncher.RunAutoBackupBeforeLaunchAsync).
    internal abstract string AutoBackupFailedTitle { get; }

    internal abstract string AutoBackupFailedMessage { get; }

    /// <summary>Concaténation sans mot : identique dans toutes les langues.</summary>
    [SuppressMessage(
        "Performance",
        "CA1822:Mark members as static",
        Justification = "Membre de la surface commune aux deux langues : le rendre statique casserait les appels " +
            "UiText.Section.Membre et interdirait qu'une langue le redéfinisse le jour où une convention diverge.")]
    internal string WithVersion(string name, string version) => $"{name} · {version}";
}

/// <summary>Textes de l'écran d'accueil.</summary>
internal abstract class HomeText
{
    internal abstract string NoSearchResults(string query);

    /// <summary>Pastille discrète de la carte d'instance (feature 4b), quand une vérification récente en a trouvé.</summary>
    internal abstract string UpdatesBadge(int count);
}

/// <summary>Textes du popover Téléchargements.</summary>
internal abstract class DownloadsText
{
    internal abstract string Queued { get; }

    internal abstract string Verifying { get; }

    internal abstract string GenericFailure { get; }

    internal abstract string Summary(int running, int queued);
}

/// <summary>Textes de l'écran Versions du jeu.</summary>
internal abstract class VersionsText
{
    internal abstract string StaleCatalog { get; }

    internal abstract string UnavailableCatalog { get; }

    internal abstract string Subtitle(int installedCount, string totalSize);

    internal abstract string InstalledOn(DateTimeOffset installedUtc);

    internal abstract string PhaseLabel(GameInstallPhase phase);

    /// <summary>
    /// Détail de la phase d'installation, quand la stratégie sait se mesurer : elle nomme le
    /// travail en cours (extraction de l'archive), là où le seul mot « Installation » ne disait pas
    /// si quelque chose avançait encore.
    /// </summary>
    internal abstract string InstallDetail(int percent);

    internal abstract string BrokenReason(GameInstallBrokenReason reason);

    internal abstract string UninstallTitle(string version);

    internal abstract string UninstallMessage(string version);

    internal abstract string UninstallDependents(IReadOnlyList<string> instanceNames);

    /// <summary>Deux valeurs machine séparées par un point médian : aucun mot à traduire.</summary>
    [SuppressMessage(
        "Performance",
        "CA1822:Mark members as static",
        Justification = "Membre de la surface commune aux deux langues : le rendre statique casserait les appels " +
            "UiText.Section.Membre et interdirait qu'une langue le redéfinisse le jour où une convention diverge.")]
    internal string DownloadDetail(string progress, string speed)
        => string.IsNullOrEmpty(speed) ? progress : $"{progress} · {speed}";
}

/// <summary>Raison affichée sous une instance que le scan n'a pas pu charger.</summary>
internal abstract class BrokenInstancesText
{
    internal abstract string Reason(InstanceBrokenReason reason);
}

/// <summary>Textes de la page de détail d'instance (design/ui_kits/launcher/screen-instance.jsx).</summary>
internal abstract class InstanceText(InstanceBackupsText backups, DoctorText doctor)
{
    /// <summary>Textes du bloc Sauvegardes de l'onglet Options.</summary>
    internal InstanceBackupsText Backups { get; } = backups;

    /// <summary>Textes du docteur d'instance.</summary>
    internal DoctorText Doctor { get; } = doctor;

    internal abstract string VersionNotInstalledTitle { get; }

    internal abstract string RuntimeMissingTitle { get; }

    internal abstract string MacNotSupportedTitle { get; }

    internal abstract string AlreadyRunningTitle { get; }

    internal abstract string GenericLaunchErrorTitle { get; }

    internal abstract string EnvVarsInvalidLine { get; }

    internal abstract string StopConfirmTitle(string instanceName);

    internal abstract string StopConfirmMessage(string instanceName);
}

/// <summary>Textes du bloc Sauvegardes (chantier Sauvegardes d'instance).</summary>
internal abstract class InstanceBackupsText
{
    internal abstract string CreateFailedTitle { get; }

    internal abstract string KeepCountChoiceLabel(int count);

    internal abstract string CreateProgress(int filesProcessed, int totalFiles);

    internal abstract string AutoBackupProgress(int filesProcessed, int totalFiles);
}

/// <summary>
/// Textes du docteur d'instance (diagnostic local hors ligne, voir
/// <see cref="Prospect.Core.Diagnostics.InstanceDoctor"/>). Un message par vérification EN DÉFAUT
/// seulement : un verdict sain ne produit jamais de ligne, le rapport ne liste que ce qui mérite
/// l'attention (voir <c>ViewModels.Dialogs.InstanceDoctorDialogViewModel</c>).
/// </summary>
/// <param name="mods">
/// Section Mods de la MÊME langue : la vérification 3 réutilise sa raison d'échec d'identification
/// plutôt que d'en tenir une deuxième copie qui pourrait diverger.
/// </param>
internal abstract class DoctorText(ModsText mods)
{
    /// <summary>Section Mods de la même table, injectée à la construction.</summary>
    protected ModsText Mods { get; } = mods;

    internal abstract string InstallAction { get; }

    internal abstract string ReinstallAction { get; }

    internal abstract string OpenModsAction { get; }

    internal abstract string AllClearTitle { get; }

    internal abstract string AllClearDescription { get; }

    internal abstract string CompatibilityUnknown { get; }

    internal abstract string RuntimeIndeterminate { get; }

    internal abstract string ErrorsGroupTitle(int count);

    internal abstract string WarningsGroupTitle(int count);

    /// <summary>Vérification 1 : chaîne vide pour <see cref="GameVersionDoctorStatus.Installed"/>, jamais affichée (verdict sain).</summary>
    internal abstract string GameVersionMessage(GameVersionDoctorResult result);

    /// <summary>Vérification 2 : chaîne vide pour <see cref="RuntimeAvailability.Present"/>, jamais affichée (verdict sain).</summary>
    internal abstract string RuntimeMessage(RuntimeCheckResult runtime);

    /// <summary>
    /// Vérification 4 : le cas spécial « rien de local ne permet de juger » d'abord (jamais
    /// inventé), sinon le décompte de ce qui reste incertain (rapprochement approximatif ou
    /// entièrement inconnu). Chaîne vide pour un verdict sain.
    /// </summary>
    internal abstract string CompatibilityMessage(ModCompatibilityDoctorResult compatibility, string gameVersionText);

    /// <summary>Vérification 5 : <paramref name="availableText"/> déjà mis en forme (voir <c>ByteSizeFormatter</c>, Core n'en a pas la charge).</summary>
    internal abstract string DiskSpaceLow(string availableText);

    /// <summary>Vérification 3 : une ligne par <see cref="ModDoctorIssue"/>, dépendance non satisfaite ou mod non identifié.</summary>
    internal string ModIssueMessage(ModDoctorIssue issue)
    {
        ArgumentNullException.ThrowIfNull(issue);

        return issue.Kind switch
        {
            ModDoctorIssueKind.Unidentified => UnidentifiedMessage(issue.ModDisplayName, issue.Problem),
            _ when issue.Dependency is { } dependency => DependencyIssueMessage(issue.ModDisplayName, dependency),
            _ => string.Empty,
        };
    }

    protected abstract string UnidentifiedMessage(string modDisplayName, ModInfoProblem problem);

    // Contrairement à ModsText.DependencyReason (qui laisse le cas Disabled à un bloc à part,
    // DisabledDependencies), ce docteur rend une seule ligne par dépendance quel que soit le
    // statut : les trois cas de ModDependencyStatus qui restent une fois Satisfied écarté par
    // ModDependencyResolver.FindUnsatisfied.
    protected abstract string DependencyIssueMessage(string modDisplayName, ModDependencyIssue dependency);
}

/// <summary>
/// Textes du compte Vintage Story (Réglages, section Comptes, et checklist de premier
/// lancement). Règle tenue pour tous : aucun ne reprend un champ d'API, un code HTTP ou le
/// message d'une exception. Un joueur qui se trompe de mot de passe lit une phrase, pas
/// « invalidemailorpassword ».
/// </summary>
internal abstract class AccountText
{
    internal abstract string InvalidCredentials { get; }

    internal abstract string InvalidTwoFactorCode { get; }

    internal abstract string Refused { get; }

    internal abstract string ServiceUnavailable { get; }

    internal abstract string UnknownPlayerName { get; }

    internal abstract string SignOutConfirmMessage { get; }

    internal abstract string SignOutConfirmTitle(string playerName);

    internal abstract string SignedInSubtitle(string playerName);
}

/// <summary>
/// Textes du navigateur de mods et de l'onglet Mods d'une instance
/// (design/ui_kits/launcher/screen-mods.jsx et components/launcher/ModRow.jsx).
/// </summary>
internal abstract class ModsText
{
    internal abstract string AllVersions { get; }

    internal abstract string UnknownVersion { get; }

    /// <summary>Nom propre : identique dans toutes les langues.</summary>
    [SuppressMessage(
        "Performance",
        "CA1822:Mark members as static",
        Justification = "Membre de la surface commune aux deux langues : le rendre statique casserait les appels " +
            "UiText.Section.Membre et interdirait qu'une langue le redéfinisse le jour où une convention diverge.")]
    internal string ProvenanceModDb => "ModDB";

    internal abstract string ProvenanceManual { get; }

    internal abstract string EmptyResultsTitle { get; }

    internal abstract string EnabledTitle { get; }

    internal abstract string DisabledTitle { get; }

    internal abstract string UninstalledTitle { get; }

    internal abstract string FileGoneTitle { get; }

    internal abstract string InstallFailedTitle { get; }

    internal abstract string NoCompatibleReleaseTitle { get; }

    internal abstract string DetailUnavailableTitle { get; }

    internal abstract string PickInstanceTitle { get; }

    internal abstract string PickInstanceMessage { get; }

    internal abstract string StaleCatalog { get; }

    internal abstract string OfflineEmptyTitle { get; }

    internal abstract string OfflineEmptyDescription { get; }

    internal abstract string CheckUpdatesFailedTitle { get; }

    internal abstract string UpdateFailedTitle { get; }

    /// <summary>Culture de mise en forme des grands nombres (séparateur de milliers).</summary>
    protected abstract CultureInfo NumberCulture { get; }

    internal abstract string Subtitle(int indexedCount);

    /// <summary>
    /// Compteur sous la grille. La grille ne rend qu'une fenêtre de cartes à la fois (voir
    /// ModBrowserViewModel) : ce compteur est ce qui rend cette paresse visible plutôt que
    /// trompeuse, en disant combien de mods correspondent réellement à la recherche.
    /// </summary>
    internal abstract string ShownCount(int shown, int total);

    internal abstract string ByAuthor(string author);

    internal abstract string EmptyResultsDescription(string query);

    internal abstract string DetailMeta(string author, int downloads);

    internal abstract string CompatibleVersions(IReadOnlyList<string> tags);

    internal abstract string SideLabel(ModDbSide side);

    internal abstract string SideLabel(ModSide? side);

    internal abstract string RowAuthor(IReadOnlyList<string> authors);

    internal abstract string UnidentifiedReason(ModInfoProblem problem);

    internal abstract string InstalledSummary(int total, int enabled);

    internal abstract string PlanTitle(string modName);

    internal abstract string PlanMessage(string version, string instanceName);

    internal abstract string ApproximateWarning(string gameVersion);

    internal abstract string DependencyReason(ModDependencyIssue? issue);

    /// <summary>
    /// Dépendances dont le ModDB ne publie AUCUNE fiche. Le seul cas où « introuvable » est vrai.
    /// </summary>
    internal abstract string DependenciesNotOnModDb(IReadOnlyList<string> identifiers);

    /// <summary>
    /// Dépendances dont la fiche existe, mais dont aucune release n'est déclarée compatible avec la
    /// version de jeu de l'instance. Verdict distinct du précédent : dire « introuvable » ici
    /// envoyait l'utilisateur chercher sur le ModDB un mod qui y est bel et bien publié.
    /// </summary>
    internal abstract string DependenciesWithoutCompatibleRelease(IReadOnlyList<string> names, string gameVersion);

    internal abstract string DisabledDependencies(IReadOnlyList<string> identifiers);

    internal abstract string InstalledTitle(string modName);

    internal abstract string InstalledMessage(int count, string instanceName);

    internal abstract string UninstallTitle(string modName);

    internal abstract string UninstallMessage(string fileName);

    internal abstract string UninstallDependents(IReadOnlyList<string> modNames);

    internal abstract string LastCheckedLabel(string relativeCheckedText);

    internal abstract string UpdatesAvailableTitle(int count);

    internal abstract string UpdatePlanTitle(string modName);

    internal abstract string UpdatePlanMessage(string currentVersion, string targetVersion);

    internal abstract string UpdateDependentsNote(IReadOnlyList<string> modNames);

    internal abstract string UpdatedTitle(string modName);

    internal abstract string UpdatedMessage(string targetVersion);

    internal abstract string BulkUpdateDoneTitle(int count);

    internal abstract string BulkUpdateFailures(IReadOnlyList<BulkUpdateFailure> failures);

    /// <summary>Grand nombre séparé selon la convention de la langue (1 234 en français, 1,234 en anglais).</summary>
    internal string FormatCount(int value) => value.ToString("N0", NumberCulture);

    /// <summary>Nom d'instance et version de jeu : aucun mot à traduire.</summary>
    [SuppressMessage(
        "Performance",
        "CA1822:Mark members as static",
        Justification = "Membre de la surface commune aux deux langues : le rendre statique casserait les appels " +
            "UiText.Section.Membre et interdirait qu'une langue le redéfinisse le jour où une convention diverge.")]
    internal string InstanceLabel(string name, string gameVersion) => $"{name} · {gameVersion}";

    /// <summary>Date ISO, format technique identique dans toutes les langues.</summary>
    [SuppressMessage(
        "Performance",
        "CA1822:Mark members as static",
        Justification = "Membre de la surface commune aux deux langues : le rendre statique casserait les appels " +
            "UiText.Section.Membre et interdirait qu'une langue le redéfinisse le jour où une convention diverge.")]
    internal string ReleaseDate(DateTimeOffset? createdUtc)
        => createdUtc is null
            ? string.Empty
            : createdUtc.Value.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>Avancement d'une mise à jour en lot : deux compteurs et un nom de mod, aucun mot.</summary>
    [SuppressMessage(
        "Performance",
        "CA1822:Mark members as static",
        Justification = "Membre de la surface commune aux deux langues : le rendre statique casserait les appels " +
            "UiText.Section.Membre et interdirait qu'une langue le redéfinisse le jour où une convention diverge.")]
    internal string BulkUpdateProgress(int completedCount, int totalCount, string modName)
        => $"{completedCount + 1}/{totalCount} · {modName}";
}

/// <summary>
/// Textes de l'export et de l'import de modpacks (feature 5, docs/architecture.md « 5.
/// Modpacks »). Voix produit pour le rapport final : jamais de trace technique, des phrases
/// qui nomment ce qui a manqué plutôt qu'un code d'erreur.
/// </summary>
internal abstract class ModpacksText
{
    internal abstract string ExportPickerTitle { get; }

    internal abstract string ImportPickerTitle { get; }

    internal abstract string ImportModConfigNotice { get; }

    internal abstract string InstallingGameVersionPhase { get; }

    internal abstract string ExportTitle(string instanceName);

    internal abstract string ExportedToastDescription(int modsExported);

    internal abstract string ExportSkippedSectionTitle(int count);

    internal abstract string ExportSkipReason(ModpackExportSkipReason reason);

    internal abstract string ImportPreviewSubtitle(string gameVersion, int modCount);

    internal abstract string ImportGameVersionWarning(string gameVersion, string displaySize);

    internal abstract string InstallingModsPhase(int completedMods, int totalMods, string? currentModId);

    internal abstract string ReportGroupTitle(ModpackModImportStatus status, int count);

    internal abstract string ReportRowDetail(ModpackImportModReport report);

    internal abstract string ImportedToastTitle(string instanceName);

    internal abstract string ImportedToastDescription(int installedCount, int totalCount);
}

/// <summary>
/// Textes de l'adoption des installations VS Launcher (chantier « migration »,
/// docs/research/vslauncher-et-distribution.md). Voix produit pour le rapport final, même
/// principe que <see cref="ModpacksText"/> : jamais de trace technique quand une raison courte
/// suffit.
/// </summary>
internal abstract class MigrationText
{
    internal abstract string Starting { get; }

    internal abstract string CompletedToastTitle { get; }

    internal abstract string ModCount(int count);

    internal abstract string DetectionSummary(int installationCount, int gameVersionCount);

    internal abstract string AdoptingInstallationsPhase(int completedItems, int totalItems, string? currentItemLabel);

    internal abstract string AdoptingEnginesPhase(int completedItems, int totalItems, string? currentItemLabel);

    internal abstract string FilesCopied(int filesCopied, int totalFiles);

    internal abstract string CompletedToastDescription(int adoptedInstallations, int adoptedEngines);

    internal abstract string InstallationsAdoptedGroupTitle(int count);

    internal abstract string InstallationsSkippedGroupTitle(int count);

    internal abstract string InstallationsFailedGroupTitle(int count);

    internal abstract string EnginesAdoptedGroupTitle(int count);

    internal abstract string EnginesSkippedGroupTitle(int count);

    internal abstract string EnginesFailedGroupTitle(int count);
}

/// <summary>Textes de l'écran Réglages produits par du code C#.</summary>
internal abstract class SettingsText
{
    internal abstract string PickFolderTitle { get; }

    internal abstract string VslNotDetected { get; }

    /// <summary>Libellé d'un choix du sélecteur de téléchargements simultanés (section Réseau).</summary>
    internal abstract string ConcurrencyChoiceLabel(int count);
}

/// <summary>
/// Textes des lignes de la checklist de l'écran de premier lancement
/// (design/ui_kits/launcher/screen-firstrun.jsx, <see cref="Prospect.Desktop.ViewModels.FirstRun.FirstRunScreenViewModel"/>).
/// Les libellés statiques (titre, description, boutons Commencer/Passer) restent dans les
/// dictionnaires Strings comme le reste de l'app ; ceux-ci sont calculés à partir de l'état réel
/// des services, donc du code C#, pas d'un binding XAML direct.
/// </summary>
internal abstract class FirstRunText
{
    internal abstract string DataFolderTitle { get; }

    internal abstract string GameVersionTitle { get; }

    internal abstract string VslDetectedTitle { get; }

    internal abstract string AccountTitle { get; }

    internal abstract string AccountSignedOut { get; }

    internal abstract string AccountSignInAction { get; }

    internal abstract string InstallVersionAction { get; }

    internal abstract string AdoptAction { get; }

    internal abstract string NoVersionInstalled { get; }

    /// <summary>Sous-titre de la ligne « Version du jeu » une fois au moins une version installée.</summary>
    internal abstract string InstalledVersionsSummary(int count, string mostRecentVersion);
}

/// <summary>
/// Temps écrit à la façon humaine (design/readme.md, « Content fundamentals » :
/// « Human time is written the human way ») : dates relatives et temps de jeu cumulé. Consommé
/// par <see cref="Prospect.Desktop.Formatting.RelativeDateFormatter"/> et
/// <see cref="Prospect.Desktop.Formatting.PlaytimeFormatter"/>, qui restent des fonctions pures
/// recevant l'instant courant en paramètre.
/// </summary>
internal abstract class TimeText
{
    internal abstract string Never { get; }

    internal abstract string Today { get; }

    internal abstract string Yesterday { get; }

    internal abstract string NeverPlayed { get; }

    internal abstract string PlayedUnderAnHour { get; }

    internal abstract string DaysAgo(int days);

    /// <summary>Date absolue, au-delà de la fenêtre où le relatif reste lisible.</summary>
    internal abstract string AbsoluteDate(DateTime utcValue);

    internal abstract string PlayedHours(long hours);
}