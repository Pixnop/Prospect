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
        internal const string VersionRequired = "La version du jeu ne peut pas être vide.";
        internal const string VersionInvalidFormat = "Format attendu : Major.Minor.Patch, par exemple 1.21.3.";

        internal static readonly IReadOnlyList<string> StepLabels = ["Nom", "Version", "Icône", "Résumé"];
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

        internal static string WithVersion(string name, string version) => $"{name} · {version}";
    }

    internal static class Home
    {
        internal static string NoSearchResults(string query) => $"Aucune instance ne correspond à « {query} ».";
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