using System.Globalization;

using Prospect.Core.Diagnostics;
using Prospect.Core.GameVersions;
using Prospect.Core.Instances;
using Prospect.Core.ModDb;
using Prospect.Core.Modpacks;
using Prospect.Core.Runtime;

namespace Prospect.Desktop.Resources;

/// <summary>
/// Textes UI produits par du code C# (ViewModels) plutôt que lus depuis un binding XAML direct :
/// messages de validation, confirmations qui nomment une instance, textes de toast interpolés.
/// Existe à côté de <c>Strings.axaml</c> (voir son commentaire d'en-tête pour le partage des
/// rôles) : chaque texte UI n'est défini qu'à un seul des deux endroits.
/// </summary>
internal static class UiText
{
    internal static class Wizard
    {
        internal const string NameRequired = "Le nom de l'instance ne peut pas être vide.";
        internal const string VersionInstalled = "installée";
        internal const string InstallCanceled = "Installation annulée. L'instance n'a pas été créée.";
        internal const string SummaryNoVersion = "Choisis une version du jeu à l'étape précédente.";

        internal static readonly IReadOnlyList<string> StepLabels = ["Nom", "Version", "Icône", "Résumé"];

        internal static string VersionToDownload(string displaySize) => $"{displaySize} à télécharger";

        internal static string SummaryAlreadyInstalled(string version)
            => $"La version {version} est déjà installée, rien à télécharger. L'instance sera prête immédiatement.";

        internal static string SummaryWillDownload(string version)
            => $"La version {version} sera téléchargée et installée avant la création de l'instance.";
    }

    internal static class Dialogs
    {
        internal const string RenameEmptyError = "Le nom de l'instance ne peut pas être vide.";
        internal const string DuplicateEmptyError = "Le nom de la copie ne peut pas être vide.";

        internal static string DuplicateSuggestedName(string sourceName) => $"{sourceName} (copie)";

        internal static string DuplicateProgressLabel(int filesCopied, int totalFiles)
            => totalFiles == 0 ? "Préparation de la copie…" : $"Copie des fichiers ({filesCopied}/{totalFiles})";

        internal static string DeleteTitle(string instanceName) => $"Supprimer « {instanceName} » ?";

        internal static string DeleteMessage(string instanceName)
            => $"Toutes les données de « {instanceName} » seront supprimées définitivement, mondes et mods compris. Cette action est irréversible.";

        internal static string RestoreBackupTitle(string instanceName) => $"Restaurer « {instanceName} » ?";

        internal static string RestoreBackupMessage(string instanceName, string dateText)
            => $"L'état actuel de « {instanceName} » sera d'abord sauvegardé par sécurité, puis remplacé par la sauvegarde du {dateText}. Mondes, configs et mods reviendront exactement à cet état.";

        internal static string DeleteBackupTitle(string dateText) => $"Supprimer la sauvegarde du {dateText} ?";

        internal const string DeleteBackupMessage = "Cette sauvegarde sera supprimée définitivement. Les autres sauvegardes de l'instance ne sont pas concernées.";
    }

    internal static class Toasts
    {
        internal const string InstanceCreatedTitle = "Instance créée";
        internal const string InstanceRenamedTitle = "Instance renommée";
        internal const string InstanceDuplicatedTitle = "Instance dupliquée";
        internal const string InstanceDeletedTitle = "Instance supprimée";
        internal const string VersionInstalledTitle = "Version installée";
        internal const string VersionUninstalledTitle = "Version désinstallée";
        internal const string LaunchSettingsSavedTitle = "Réglages de lancement enregistrés";
        internal const string ModpackExportedTitle = "Modpack exporté";

        internal const string BackupCreatedTitle = "Sauvegarde créée";
        internal const string BackupRestoredTitle = "Sauvegarde restaurée";
        internal const string BackupDeletedTitle = "Sauvegarde supprimée";

        // Avertissement bien visible (ToastTone.Warning), volontairement distinct d'un simple log :
        // c'est le filet de sécurité du joueur qui a raté, pas un confort accessoire comme
        // l'injection de session (voir GameLauncher.RunAutoBackupBeforeLaunchAsync).
        internal const string AutoBackupFailedTitle = "Sauvegarde automatique ratée";
        internal const string AutoBackupFailedMessage = "Le lancement continue, mais aucune sauvegarde n'a été prise avant. Vérifie l'espace disque disponible.";

        internal static string WithVersion(string name, string version) => $"{name} · {version}";
    }

    internal static class Home
    {
        internal static string NoSearchResults(string query) => $"Aucune instance ne correspond à « {query} ».";

        /// <summary>Pastille discrète de la carte d'instance (feature 4b), quand une vérification récente en a trouvé.</summary>
        internal static string UpdatesBadge(int count) => count == 1 ? "1 mise à jour" : $"{count} mises à jour";
    }

    internal static class Downloads
    {
        internal const string Queued = "en attente";
        internal const string Verifying = "vérification de l'empreinte";
        internal const string GenericFailure = "Échec du téléchargement.";

        internal static string Summary(int running, int queued) => (running, queued) switch
        {
            (0, 0) => string.Empty,
            (_, 0) => $"{running} en cours",
            (0, _) => $"{queued} en attente",
            _ => $"{running} en cours · {queued} en attente",
        };
    }

    internal static class Versions
    {
        internal const string StaleCatalog = "Le catalogue n'a pas pu être actualisé. Les versions affichées viennent du dernier relevé connu.";
        internal const string UnavailableCatalog = "Le catalogue est injoignable. Seules les versions déjà installées sont affichées.";

        internal static string Subtitle(int installedCount, string totalSize) => installedCount switch
        {
            0 => "Aucune version installée · dossier partagé entre les instances",
            1 => $"1 installée · {totalSize} · dossier partagé entre les instances",
            _ => $"{installedCount} installées · {totalSize} · dossier partagé entre les instances",
        };

        internal static string InstalledOn(DateTimeOffset installedUtc)
            => $"installée le {installedUtc.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}";

        internal static string PhaseLabel(GameInstallPhase phase) => phase switch
        {
            GameInstallPhase.Downloading => "Téléchargement",
            GameInstallPhase.Verifying => "Vérification",
            GameInstallPhase.Installing => "Installation",
            GameInstallPhase.Completed => "Terminé",
            _ => string.Empty,
        };

        internal static string DownloadDetail(string progress, string speed)
            => string.IsNullOrEmpty(speed) ? progress : $"{progress} · {speed}";

        internal static string BrokenReason(GameInstallBrokenReason reason) => reason switch
        {
            GameInstallBrokenReason.MissingCompletionMarker => "installation interrompue, à réinstaller",
            GameInstallBrokenReason.UnreadableVersionName => "nom de dossier illisible",
            _ => "raison inconnue",
        };

        internal static string UninstallTitle(string version) => $"Désinstaller la version {version} ?";

        internal static string UninstallMessage(string version)
            => $"Les fichiers de la version {version} seront supprimés du dossier partagé. Tu pourras la réinstaller depuis le catalogue.";

        internal static string UninstallDependents(IReadOnlyList<string> instanceNames)
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

    internal static class BrokenInstances
    {
        internal static string Reason(InstanceBrokenReason reason) => reason switch
        {
            InstanceBrokenReason.MissingMetadataFile => "fichier instance.json manquant",
            InstanceBrokenReason.CorruptedMetadataFile => "fichier instance.json illisible",
            InstanceBrokenReason.UnsupportedSchemaVersion => "version de schéma non prise en charge",
            _ => "raison inconnue",
        };
    }

    /// <summary>Textes de la page de détail d'instance (design/ui_kits/launcher/screen-instance.jsx).</summary>
    internal static class Instance
    {
        internal const string VersionNotInstalledTitle = "Version non installée";
        internal const string RuntimeMissingTitle = "Runtime .NET manquant";
        internal const string MacNotSupportedTitle = "macOS non pris en charge";
        internal const string AlreadyRunningTitle = "Session déjà en cours";
        internal const string GenericLaunchErrorTitle = "Lancement impossible";

        internal static string StopConfirmTitle(string instanceName) => $"Arrêter « {instanceName} » ?";

        internal static string StopConfirmMessage(string instanceName)
            => $"Le jeu de « {instanceName} » va s'arrêter immédiatement. Toute progression non sauvegardée sera perdue.";

        internal const string EnvVarsInvalidLine = "Chaque ligne doit être au format CLE=valeur.";

        /// <summary>Textes du bloc Sauvegardes de l'onglet Options (chantier Sauvegardes d'instance).</summary>
        internal static class Backups
        {
            internal const string CreateFailedTitle = "Sauvegarde impossible";

            internal static string KeepCountChoiceLabel(int count) => count == 1 ? "1 sauvegarde conservée" : $"{count} sauvegardes conservées";

            internal static string CreateProgress(int filesProcessed, int totalFiles)
                => totalFiles == 0 ? "Préparation de la sauvegarde…" : $"Sauvegarde en cours ({filesProcessed}/{totalFiles})";

            internal static string AutoBackupProgress(int filesProcessed, int totalFiles)
                => totalFiles == 0 ? "Sauvegarde automatique…" : $"Sauvegarde automatique ({filesProcessed}/{totalFiles})…";
        }

        /// <summary>
        /// Textes du docteur d'instance (diagnostic local hors ligne, voir
        /// <see cref="Prospect.Core.Diagnostics.InstanceDoctor"/>). Un message par vérification EN
        /// DÉFAUT seulement : un verdict sain ne produit jamais de ligne, le rapport ne liste que ce
        /// qui mérite l'attention (voir <c>ViewModels.Dialogs.InstanceDoctorDialogViewModel</c>).
        /// </summary>
        internal static class Doctor
        {
            internal const string InstallAction = "Installer";
            internal const string ReinstallAction = "Réinstaller";
            internal const string OpenModsAction = "Voir les mods";

            internal const string AllClearTitle = "Tout est en ordre";
            internal const string AllClearDescription = "Aucun problème détecté sur les cinq vérifications locales.";

            internal const string CompatibilityUnknown =
                "Compatibilité de version de jeu inconnue : lance une vérification des mises à jour pour en savoir plus.";

            internal const string RuntimeIndeterminate =
                "Le runtime requis par cette version n'a pas pu être déterminé. Le lancement le confirmera si besoin.";

            internal static string ErrorsGroupTitle(int count) => count == 1 ? "1 point à corriger" : $"{count} points à corriger";

            internal static string WarningsGroupTitle(int count) => count == 1 ? "1 point à surveiller" : $"{count} points à surveiller";

            /// <summary>Vérification 1 : chaîne vide pour <see cref="GameVersionDoctorStatus.Installed"/>, jamais affichée (verdict sain).</summary>
            internal static string GameVersionMessage(GameVersionDoctorResult result) => result.Status switch
            {
                GameVersionDoctorStatus.Missing => $"La version {result.Version} n'est pas installée.",
                GameVersionDoctorStatus.Incomplete
                    => $"L'installation de la version {result.Version} est incomplète, probablement interrompue en cours de route.",
                _ => string.Empty,
            };

            /// <summary>Vérification 2 : chaîne vide pour <see cref="RuntimeAvailability.Present"/>, jamais affichée (verdict sain).</summary>
            internal static string RuntimeMessage(RuntimeCheckResult runtime) => runtime.Availability switch
            {
                RuntimeAvailability.Missing
                    => $"Le runtime .NET {runtime.Requirement.FrameworkName} {runtime.Requirement.Version} est requis mais n'est pas installé.",
                RuntimeAvailability.Indeterminate => RuntimeIndeterminate,
                _ => string.Empty,
            };

            /// <summary>Vérification 3 : une ligne par <see cref="ModDoctorIssue"/>, dépendance non satisfaite ou mod non identifié.</summary>
            internal static string ModIssueMessage(ModDoctorIssue issue) => issue.Kind switch
            {
                ModDoctorIssueKind.Unidentified => $"« {issue.ModDisplayName} » n'a pas pu être identifié ({Mods.UnidentifiedReason(issue.Problem)}).",
                _ when issue.Dependency is { } dependency => DependencyIssueMessage(issue.ModDisplayName, dependency),
                _ => string.Empty,
            };

            // Contrairement à Mods.DependencyReason (qui laisse le cas Disabled à un bloc à part,
            // DisabledDependencies), ce docteur rend une seule ligne par dépendance quel que soit le
            // statut : les trois cas de ModDependencyStatus qui restent une fois Satisfied écarté par
            // ModDependencyResolver.FindUnsatisfied.
            private static string DependencyIssueMessage(string modDisplayName, ModDependencyIssue dependency) => dependency.Status switch
            {
                ModDependencyStatus.Missing => $"« {modDisplayName} » a besoin de {dependency.ModIdString}, absent de l'instance.",
                ModDependencyStatus.Disabled => $"« {modDisplayName} » a besoin de {dependency.ModIdString}, présent mais désactivé.",
                ModDependencyStatus.TooOld
                    => $"« {modDisplayName} » a besoin de {dependency.ModIdString} {dependency.Requirement} au minimum, version installée trop ancienne.",
                _ => $"« {modDisplayName} » a besoin de {dependency.ModIdString}.",
            };

            /// <summary>
            /// Vérification 4 : le cas spécial « rien de local ne permet de juger » d'abord (jamais
            /// inventé), sinon le décompte de ce qui reste incertain (rapprochement approximatif ou
            /// entièrement inconnu). Chaîne vide pour un verdict sain.
            /// </summary>
            internal static string CompatibilityMessage(ModCompatibilityDoctorResult compatibility, string gameVersionText)
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
                    ? $"1 mod dont la compatibilité avec {gameVersionText} n'est pas confirmée. Lance une vérification des mises à jour pour en savoir plus."
                    : $"{uncertain} mods dont la compatibilité avec {gameVersionText} n'est pas confirmée. Lance une vérification des mises à jour pour en savoir plus.";
            }

            /// <summary>Vérification 5 : <paramref name="availableText"/> déjà mis en forme (voir <c>ByteSizeFormatter</c>, Core n'en a pas la charge).</summary>
            internal static string DiskSpaceLow(string availableText) => $"Espace disque faible : {availableText} restants sur le volume de Prospect.";
        }
    }

    /// <summary>
    /// Textes du compte Vintage Story (Réglages, section Comptes, et checklist de premier
    /// lancement). Règle tenue pour tous : aucun ne reprend un champ d'API, un code HTTP ou le
    /// message d'une exception. Un joueur qui se trompe de mot de passe lit une phrase, pas
    /// « invalidemailorpassword ».
    /// </summary>
    internal static class Account
    {
        internal const string InvalidCredentials = "Adresse ou mot de passe incorrect.";

        internal const string InvalidTwoFactorCode =
            "Ce code n'est pas le bon. Il change toutes les trente secondes, retape le dernier affiché.";

        internal const string Refused =
            "La connexion a été refusée. Vérifie ton compte sur vintagestory.at, puis réessaie.";

        internal const string ServiceUnavailable =
            "Le service de compte de Vintage Story ne répond pas. Vérifie ta connexion et réessaie.";

        internal const string UnknownPlayerName = "ce compte";

        internal const string SignOutConfirmMessage =
            "Ta session sera effacée de cet ordinateur. Le jeu démarrera sans multijoueur tant que tu ne te reconnecteras pas.";

        internal static string SignOutConfirmTitle(string playerName) => $"Déconnecter « {playerName} » ?";

        internal static string SignedInSubtitle(string playerName) => $"Connecté en tant que {playerName}.";
    }

    /// <summary>
    /// Textes du navigateur de mods et de l'onglet Mods d'une instance
    /// (design/ui_kits/launcher/screen-mods.jsx et components/launcher/ModRow.jsx).
    /// </summary>
    internal static class Mods
    {
        internal const string AllVersions = "Toutes les versions";
        internal const string UnknownVersion = "version inconnue";
        internal const string ProvenanceModDb = "ModDB";
        internal const string ProvenanceManual = "manuel";
        internal const string EmptyResultsTitle = "Aucun mod ne correspond";
        internal const string EnabledTitle = "Mod activé";
        internal const string DisabledTitle = "Mod désactivé";
        internal const string UninstalledTitle = "Mod retiré";
        internal const string FileGoneTitle = "Fichier introuvable";
        internal const string InstallFailedTitle = "Installation impossible";
        internal const string NoCompatibleReleaseTitle = "Aucune version compatible";
        internal const string DetailUnavailableTitle = "Fiche indisponible";
        internal const string PickInstanceTitle = "Choisis une instance";
        internal const string PickInstanceMessage = "Sélectionne l'instance de destination avant d'installer un mod.";
        internal const string StaleCatalog = "L'index n'a pas pu être actualisé. Les mods affichés viennent du dernier relevé connu.";

        // Message exact de la maquette pour l'état « ModDB injoignable ».
        internal const string OfflineTitle = "ModDB injoignable";
        internal const string OfflineDescription = "La connexion au dépôt officiel a échoué. Les mods déjà installés restent utilisables hors ligne.";
        internal const string OfflineEmptyTitle = "Aucun résultat hors ligne";
        internal const string OfflineEmptyDescription = "L'index des mods est mis en cache localement mais il a expiré. Reconnecte-toi pour parcourir ModDB.";

        internal static string Subtitle(int indexedCount) => indexedCount switch
        {
            0 => "ModDB officiel",
            1 => "ModDB officiel · 1 mod indexé",
            _ => $"ModDB officiel · {indexedCount.ToString("N0", CultureInfo.GetCultureInfo("fr-FR"))} mods indexés",
        };

        /// <summary>
        /// Compteur sous la grille. La grille ne rend qu'une fenêtre de cartes à la fois (voir
        /// ModBrowserViewModel) : ce compteur est ce qui rend cette paresse visible plutôt que
        /// trompeuse, en disant combien de mods correspondent réellement à la recherche.
        /// </summary>
        internal static string ShownCount(int shown, int total)
            => shown >= total
                ? total switch
                {
                    0 => string.Empty,
                    1 => "1 mod",
                    _ => $"{FormatCount(total)} mods",
                }
                : $"{FormatCount(shown)} sur {FormatCount(total)} mods affichés";

        internal static string ByAuthor(string author) => string.IsNullOrWhiteSpace(author) ? "auteur inconnu" : $"par {author}";

        internal static string FormatCount(int value) => value.ToString("N0", CultureInfo.GetCultureInfo("fr-FR"));

        internal static string InstanceLabel(string name, string gameVersion) => $"{name} · {gameVersion}";

        internal static string EmptyResultsDescription(string query)
            => string.IsNullOrWhiteSpace(query)
                ? "Aucun mod ne correspond aux filtres actifs."
                : $"Aucun mod ne correspond à « {query.Trim()} ».";

        internal static string DetailMeta(string author, int downloads)
            => $"{ByAuthor(author)} · {FormatCount(downloads)} téléchargements";

        internal static string CompatibleVersions(IReadOnlyList<string> tags) => tags.Count switch
        {
            0 => "aucune version de jeu déclarée",
            1 => tags[0],
            <= 3 => string.Join(", ", tags),
            _ => $"{string.Join(", ", tags.Take(3))} et {tags.Count - 3} autres",
        };

        internal static string ReleaseDate(DateTimeOffset? createdUtc)
            => createdUtc is null
                ? string.Empty
                : createdUtc.Value.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        internal static string SideLabel(ModDbSide side) => side switch
        {
            ModDbSide.Client => "client",
            ModDbSide.Server => "serveur",
            ModDbSide.Both => "client et serveur",
            _ => string.Empty,
        };

        internal static string SideLabel(ModSide? side) => side switch
        {
            ModSide.Client => "client",
            ModSide.Server => "serveur",
            ModSide.Universal => "universel",
            _ => string.Empty,
        };

        internal static string RowAuthor(IReadOnlyList<string> authors) => authors.Count switch
        {
            0 => "auteur inconnu",
            1 => $"par {authors[0]}",
            _ => $"par {authors[0]} et {authors.Count - 1} autre{(authors.Count > 2 ? "s" : string.Empty)}",
        };

        internal static string UnidentifiedReason(ModInfoProblem problem) => problem switch
        {
            ModInfoProblem.MissingModInfo => "aucun modinfo.json dans l'archive",
            ModInfoProblem.MalformedJson => "modinfo.json illisible",
            ModInfoProblem.MissingIdentity => "modinfo.json sans identifiant ni nom",
            ModInfoProblem.UnreadableArchive => "archive illisible",
            _ => string.Empty,
        };

        internal static string InstalledSummary(int total, int enabled) => total switch
        {
            0 => string.Empty,
            1 => enabled == 1 ? "1 mod installé" : "1 mod installé, désactivé",
            _ => enabled == total ? $"{total} mods installés" : $"{total} mods installés · {total - enabled} désactivés",
        };

        internal static string PlanTitle(string modName) => $"Installer « {modName} » ?";

        internal static string PlanMessage(string version, string instanceName)
            => $"La version {version} sera ajoutée à « {instanceName} ».";

        internal static string ApproximateWarning(string gameVersion)
            => $"Aucune version n'est déclarée compatible avec {gameVersion} : celle-ci est proposée parce qu'elle cible la même série. L'auteur ne l'a pas confirmée, elle peut ne pas fonctionner.";

        internal static string DependencyReason(ModDependencyIssue? issue) => issue?.Status switch
        {
            ModDependencyStatus.Missing when issue.ReportedByModDb && issue.Requirement.IsAny => "signalée par le ModDB",
            ModDependencyStatus.Missing => "absente de l'instance",
            ModDependencyStatus.TooOld => $"version installée trop ancienne, {issue.Requirement} au minimum",
            _ => string.Empty,
        };

        internal static string UnresolvedDependencies(IReadOnlyList<string> identifiers)
            => identifiers.Count == 0
                ? string.Empty
                : $"Introuvable{(identifiers.Count > 1 ? "s" : string.Empty)} sur le ModDB : {string.Join(", ", identifiers)}. À installer à la main si le mod en a besoin.";

        internal static string DisabledDependencies(IReadOnlyList<string> identifiers)
            => identifiers.Count == 0
                ? string.Empty
                : $"Présent{(identifiers.Count > 1 ? "s" : string.Empty)} mais désactivé{(identifiers.Count > 1 ? "s" : string.Empty)} : {string.Join(", ", identifiers)}. Réactive-les depuis l'onglet Mods de l'instance.";

        internal static string InstalledTitle(string modName) => $"{modName} installé";

        internal static string InstalledMessage(int count, string instanceName)
            => count > 1 ? $"{count} mods ajoutés à « {instanceName} »" : $"Ajouté à « {instanceName} »";

        internal static string UninstallTitle(string modName) => $"Retirer « {modName} » ?";

        internal static string UninstallMessage(string fileName)
            => $"Le fichier {fileName} sera supprimé du dossier Mods de l'instance. Tu pourras le réinstaller depuis le ModDB.";

        internal static string UninstallDependents(IReadOnlyList<string> modNames)
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

        // ── Détection et application des mises à jour (feature 4b) ─────────────────

        internal const string CheckUpdatesFailedTitle = "Vérification impossible";
        internal const string UpdateFailedTitle = "Mise à jour impossible";

        internal static string LastCheckedLabel(string relativeCheckedText) => $"Dernière vérification : {relativeCheckedText}";

        internal static string UpdatesAvailableTitle(int count) => count switch
        {
            0 => string.Empty,
            1 => "1 mise à jour disponible",
            _ => $"{count} mises à jour disponibles",
        };

        internal static string UpdatePlanTitle(string modName) => $"Mettre à jour « {modName} » ?";

        internal static string UpdatePlanMessage(string currentVersion, string targetVersion)
            => $"La version {currentVersion} sera remplacée par la {targetVersion}.";

        internal static string UpdateDependentsNote(IReadOnlyList<string> modNames)
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

        internal static string UpdatedTitle(string modName) => $"{modName} mis à jour";

        internal static string UpdatedMessage(string targetVersion) => $"Version {targetVersion} installée.";

        internal static string BulkUpdateProgress(int completedCount, int totalCount, string modName)
            => $"{completedCount + 1}/{totalCount} · {modName}";

        internal static string BulkUpdateDoneTitle(int count) => count switch
        {
            0 => "Aucune mise à jour appliquée",
            1 => "1 mod mis à jour",
            _ => $"{count} mods mis à jour",
        };

        internal static string BulkUpdateFailures(IReadOnlyList<BulkUpdateFailure> failures)
            => failures.Count == 0
                ? string.Empty
                : $"Échec pour {string.Join(", ", failures.Select(failure => failure.ModName))}.";
    }

    /// <summary>
    /// Textes de l'export et de l'import de modpacks (feature 5, docs/architecture.md « 5.
    /// Modpacks »). Voix produit pour le rapport final : jamais de trace technique, des phrases
    /// qui nomment ce qui a manqué plutôt qu'un code d'erreur.
    /// </summary>
    internal static class Modpacks
    {
        internal const string ExportPickerTitle = "Exporter le modpack";
        internal const string ImportPickerTitle = "Choisir un modpack à importer";

        internal static string ExportTitle(string instanceName) => $"Exporter « {instanceName} »";

        internal static string ExportedToastDescription(int modsExported) => modsExported switch
        {
            0 => "Aucun mod dans le manifest.",
            1 => "1 mod dans le manifest.",
            _ => $"{modsExported} mods dans le manifest.",
        };

        internal static string ExportSkippedSectionTitle(int count) => count == 1
            ? "1 mod laissé de côté"
            : $"{count} mods laissés de côté";

        internal static string ExportSkipReason(ModpackExportSkipReason reason) => reason switch
        {
            ModpackExportSkipReason.UnreadableModInfo => "aucun modinfo.json lisible dans l'archive",
            ModpackExportSkipReason.MissingVersion => "version illisible",
            _ => "raison inconnue",
        };

        internal static string ImportPreviewSubtitle(string gameVersion, int modCount) => modCount switch
        {
            0 => gameVersion,
            1 => $"{gameVersion} · 1 mod",
            _ => $"{gameVersion} · {modCount} mods",
        };

        internal static string ImportGameVersionWarning(string gameVersion, string displaySize)
            => string.IsNullOrEmpty(displaySize)
                ? $"La version {gameVersion} du jeu n'est pas installée. Elle sera téléchargée avant l'import."
                : $"La version {gameVersion} du jeu n'est pas installée : {displaySize} à télécharger avant l'import.";

        internal const string ImportModConfigNotice = "Les réglages des mods (ModConfig) de ce pack seront repris dans la nouvelle instance.";

        internal const string InstallingGameVersionPhase = "Installation de la version du jeu";

        internal static string InstallingModsPhase(int completedMods, int totalMods, string? currentModId)
            => string.IsNullOrEmpty(currentModId)
                ? $"Mods {completedMods}/{totalMods}"
                : $"Mods {completedMods}/{totalMods} · {currentModId}";

        internal static string ReportGroupTitle(ModpackModImportStatus status, int count) => status switch
        {
            ModpackModImportStatus.Installed => count == 1 ? "1 mod installé" : $"{count} mods installés",
            ModpackModImportStatus.NotFound => count == 1 ? "1 mod introuvable sur le ModDB" : $"{count} mods introuvables sur le ModDB",
            ModpackModImportStatus.VersionMissing => count == 1 ? "1 version indisponible" : $"{count} versions indisponibles",
            ModpackModImportStatus.Sha256Mismatch => count == 1 ? "1 empreinte invalide" : $"{count} empreintes invalides",
            ModpackModImportStatus.NetworkFailure => count == 1 ? "1 échec réseau" : $"{count} échecs réseau",
            _ => string.Empty,
        };

        internal static string ReportRowDetail(ModpackImportModReport report) => report.Status switch
        {
            ModpackModImportStatus.Installed => report.InstalledVersion?.ToString() ?? string.Empty,
            ModpackModImportStatus.VersionMissing when report.SuggestedVersion is { } suggestion
                => $"demandé {report.RequestedVersion} · plus proche compatible : {suggestion}",
            ModpackModImportStatus.VersionMissing => $"demandé {report.RequestedVersion}",
            ModpackModImportStatus.NetworkFailure => report.Detail ?? string.Empty,
            _ => string.Empty,
        };

        internal static string ImportedToastTitle(string instanceName) => $"« {instanceName} » importée";

        internal static string ImportedToastDescription(int installedCount, int totalCount) => totalCount switch
        {
            0 => "Aucun mod dans ce pack.",
            _ when installedCount == totalCount => $"{installedCount}/{totalCount} mods installés.",
            _ => $"{installedCount}/{totalCount} mods installés, voir le rapport pour le reste.",
        };
    }

    /// <summary>
    /// Textes de l'adoption des installations VS Launcher (chantier « migration »,
    /// docs/research/vslauncher-et-distribution.md). Voix produit pour le rapport final, même
    /// principe que <see cref="Modpacks"/> : jamais de trace technique quand une raison courte
    /// suffit.
    /// </summary>
    internal static class Migration
    {
        internal const string Starting = "Préparation…";

        internal const string CompletedToastTitle = "Adoption terminée";

        internal static string ModCount(int count) => count switch
        {
            0 => "aucun mod",
            1 => "1 mod",
            _ => $"{count} mods",
        };

        internal static string DetectionSummary(int installationCount, int gameVersionCount)
        {
            var installations = installationCount switch
            {
                0 => "aucune installation",
                1 => "1 installation",
                _ => $"{installationCount} installations",
            };

            var engines = gameVersionCount switch
            {
                0 => "aucun moteur",
                1 => "1 moteur",
                _ => $"{gameVersionCount} moteurs",
            };

            return $"{installations} et {engines} détectés";
        }

        internal static string AdoptingInstallationsPhase(int completedItems, int totalItems, string? currentItemLabel)
            => string.IsNullOrEmpty(currentItemLabel)
                ? $"Installations {completedItems}/{totalItems}"
                : $"Installations {completedItems}/{totalItems} · {currentItemLabel}";

        internal static string AdoptingEnginesPhase(int completedItems, int totalItems, string? currentItemLabel)
            => string.IsNullOrEmpty(currentItemLabel)
                ? $"Moteurs {completedItems}/{totalItems}"
                : $"Moteurs {completedItems}/{totalItems} · {currentItemLabel}";

        internal static string FilesCopied(int filesCopied, int totalFiles) => $"{filesCopied}/{totalFiles} fichiers";

        internal static string CompletedToastDescription(int adoptedInstallations, int adoptedEngines) => (adoptedInstallations, adoptedEngines) switch
        {
            (0, 0) => "Rien n'a été adopté, voir le rapport.",
            (_, 0) => adoptedInstallations == 1 ? "1 instance créée." : $"{adoptedInstallations} instances créées.",
            (0, _) => adoptedEngines == 1 ? "1 moteur adopté." : $"{adoptedEngines} moteurs adoptés.",
            _ => $"{adoptedInstallations} instance(s) créée(s), {adoptedEngines} moteur(s) adopté(s).",
        };

        internal static string InstallationsAdoptedGroupTitle(int count) => count == 1 ? "1 instance créée" : $"{count} instances créées";

        internal static string InstallationsSkippedGroupTitle(int count) => count == 1 ? "1 installation ignorée" : $"{count} installations ignorées";

        internal static string InstallationsFailedGroupTitle(int count) => count == 1 ? "1 installation en échec" : $"{count} installations en échec";

        internal static string EnginesAdoptedGroupTitle(int count) => count == 1 ? "1 moteur adopté" : $"{count} moteurs adoptés";

        internal static string EnginesSkippedGroupTitle(int count) => count == 1 ? "1 moteur ignoré" : $"{count} moteurs ignorés";

        internal static string EnginesFailedGroupTitle(int count) => count == 1 ? "1 moteur en échec" : $"{count} moteurs en échec";
    }

    /// <summary>Textes de l'écran Réglages.</summary>
    internal static class Settings
    {
        internal const string PickFolderTitle = "Dossier de VS Launcher";
        internal const string VslNotDetected = "Rien d'exploitable n'a été trouvé à cet emplacement.";

        /// <summary>Libellé d'un choix du sélecteur de téléchargements simultanés (section Réseau).</summary>
        internal static string ConcurrencyChoiceLabel(int count)
            => count == 1 ? "1 téléchargement à la fois" : $"{count} téléchargements simultanés";
    }

    /// <summary>
    /// Textes des lignes de la checklist de l'écran de premier lancement
    /// (design/ui_kits/launcher/screen-firstrun.jsx, <see cref="Prospect.Desktop.ViewModels.FirstRun.FirstRunScreenViewModel"/>).
    /// Les libellés statiques (titre, description, boutons Commencer/Passer) restent dans
    /// Strings.axaml comme le reste de l'app ; ceux-ci sont calculés à partir de l'état réel des
    /// services, donc du code C#, pas d'un binding XAML direct.
    /// </summary>
    internal static class FirstRun
    {
        internal const string DataFolderTitle = "Dossier de données";
        internal const string GameVersionTitle = "Version du jeu";
        internal const string VslDetectedTitle = "Installations VS Launcher détectées";
        internal const string AccountTitle = "Compte Vintage Story";
        internal const string AccountSignedOut = "Facultatif : utile seulement pour jouer en multijoueur.";
        internal const string AccountSignInAction = "Se connecter";
        internal const string InstallVersionAction = "Installer";
        internal const string AdoptAction = "Adopter";
        internal const string NoVersionInstalled = "aucune installée";

        /// <summary>Sous-titre de la ligne « Version du jeu » une fois au moins une version installée.</summary>
        internal static string InstalledVersionsSummary(int count, string mostRecentVersion) => count switch
        {
            1 => $"{mostRecentVersion} installée",
            _ => $"{count} versions installées, dont {mostRecentVersion}",
        };
    }
}