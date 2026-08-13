using Prospect.Core.Storage;
using Prospect.Desktop.Resources;

namespace Prospect.Desktop.Formatting;

/// <summary>
/// Traduit un échec d'installation dont le message du domaine ne suffit pas, en fonction de l'OS
/// qui l'a produit.
/// </summary>
/// <remarks>
/// <c>GameInstallIncompleteException</c> rapporte le même FAIT sur les trois systèmes — aucun
/// exécutable attendu dans le dossier de version — mais les causes plausibles n'ont rien à voir.
/// Sous Windows, l'installeur Inno peut retomber sur une installation système préexistante et
/// écrire ailleurs, et c'est ce que l'utilisateur doit aller vérifier. Sous Linux et macOS il n'y a
/// pas d'installeur du tout, seulement une archive extraite : parler d'une « installation
/// existante » y était un contresens hérité du terrain Windows, et n'orientait vers rien.
/// </remarks>
internal static class GameInstallFailurePresenter
{
    /// <summary>Message affiché quand l'installation s'est terminée sans erreur mais sans exécutable.</summary>
    /// <param name="operatingSystem">Système courant, qui décide du récit.</param>
    /// <param name="targetDirectory">Dossier de version, NOMMÉ dans les deux cas : c'est ce qui rend le message vérifiable.</param>
    public static string IncompleteInstallMessage(AppOperatingSystem operatingSystem, string targetDirectory)
        => operatingSystem == AppOperatingSystem.Windows
            ? UiText.Versions.InstallLandedElsewhere(targetDirectory)
            : UiText.Versions.ArchiveMissingExecutable(targetDirectory);
}