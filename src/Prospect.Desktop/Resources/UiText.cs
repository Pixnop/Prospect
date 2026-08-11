using System.Globalization;

using Prospect.Core.GameVersions;
using Prospect.Core.Instances;

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
    }

    internal static class Toasts
    {
        internal const string InstanceCreatedTitle = "Instance créée";
        internal const string InstanceRenamedTitle = "Instance renommée";
        internal const string InstanceDuplicatedTitle = "Instance dupliquée";
        internal const string InstanceDeletedTitle = "Instance supprimée";
        internal const string VersionInstalledTitle = "Version installée";
        internal const string VersionUninstalledTitle = "Version désinstallée";

        internal static string WithVersion(string name, string version) => $"{name} · {version}";
    }

    internal static class Home
    {
        internal static string NoSearchResults(string query) => $"Aucune instance ne correspond à « {query} ».";
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
}