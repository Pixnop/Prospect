using Avalonia.Controls;
using Avalonia.Platform.Storage;

using Prospect.Desktop;

namespace Prospect.Desktop.Services;

/// <summary>
/// Sélecteur de fichiers du système, pour les flux qui ont besoin d'un chemin choisi par
/// l'utilisateur (export des journaux, choix de dossier). N'expose que des chemins simples :
/// aucun type Avalonia ne fuit au-delà de son implémentation, ce qui garde les ViewModels qui en
/// dépendent testables sans fenêtre réelle.
/// </summary>
public interface IFilePickerService
{
    /// <summary>
    /// Ouvre un sélecteur « Enregistrer sous ».
    /// </summary>
    /// <param name="title">Titre de la fenêtre du sélecteur.</param>
    /// <param name="suggestedFileName">Nom de fichier proposé, extension comprise.</param>
    /// <param name="extension">Extension attendue (sans le point), pour le filtre de type.</param>
    /// <param name="cancellationToken">Annulation.</param>
    /// <returns>Le chemin local choisi, ou <see langword="null"/> si l'utilisateur a annulé.</returns>
    Task<string?> PickSaveFileAsync(string title, string suggestedFileName, string extension, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ouvre un sélecteur de DOSSIER (adoption VS Launcher, quand la détection automatique ne
    /// trouve rien à l'emplacement par défaut de l'OS : l'utilisateur pointe alors une racine
    /// <c>appData</c> non standard à la main).
    /// </summary>
    /// <param name="title">Titre de la fenêtre du sélecteur.</param>
    /// <param name="cancellationToken">Annulation.</param>
    /// <returns>Le chemin local choisi, ou <see langword="null"/> si l'utilisateur a annulé.</returns>
    Task<string?> PickFolderAsync(string title, CancellationToken cancellationToken = default);
}

/// <summary>
/// Implémentation d'<see cref="IFilePickerService"/> par le <c>IStorageProvider</c> d'Avalonia,
/// résolu depuis la fenêtre principale. Miroir Desktop d'<see cref="Prospect.Core.Common.IExternalUrlOpener"/> :
/// même esprit de petit port dédié à un seul effet de bord système, mais celui-ci ne peut pas
/// vivre dans <c>Prospect.Core</c> puisque <c>IStorageProvider</c> est un type Avalonia.
/// </summary>
/// <remarks>
/// Prend une fabrique plutôt que la fenêtre elle-même : <see cref="MainWindow"/> dépend de
/// <c>ShellViewModel</c>, qui dépend de <c>HomeViewModel</c>, qui dépend de ce service pour
/// des journaux. Résoudre <see cref="MainWindow"/> à la construction créerait un cycle ; la
/// fabrique (même pattern que <c>Func&lt;WizardViewModel&gt;</c> dans <see cref="CompositionRoot"/>)
/// diffère la résolution jusqu'au premier appel, quand la fenêtre existe déjà.
/// </remarks>
public sealed class AvaloniaFilePickerService : IFilePickerService
{
    private readonly Func<MainWindow> _windowFactory;

    /// <summary>Construit le service.</summary>
    /// <param name="windowFactory">Fabrique vers la fenêtre principale, résolue au premier usage.</param>
    public AvaloniaFilePickerService(Func<MainWindow> windowFactory)
    {
        ArgumentNullException.ThrowIfNull(windowFactory);

        _windowFactory = windowFactory;
    }

    /// <inheritdoc />
    public async Task<string?> PickSaveFileAsync(string title, string suggestedFileName, string extension, CancellationToken cancellationToken = default)
    {
        var provider = TopLevel.GetTopLevel(_windowFactory())?.StorageProvider;
        if (provider is null)
        {
            return null;
        }

        var file = await provider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedFileName,
            DefaultExtension = extension,
            FileTypeChoices = [FileType(extension)],
        }).ConfigureAwait(true);

        return file?.TryGetLocalPath();
    }

    /// <inheritdoc />
    public async Task<string?> PickFolderAsync(string title, CancellationToken cancellationToken = default)
    {
        var provider = TopLevel.GetTopLevel(_windowFactory())?.StorageProvider;
        if (provider is null)
        {
            return null;
        }

        var folders = await provider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
        }).ConfigureAwait(true);

        return folders.Count == 0 ? null : folders[0].TryGetLocalPath();
    }

    private static FilePickerFileType FileType(string extension) => new(extension)
    {
        Patterns = [$"*.{extension}"],
    };
}