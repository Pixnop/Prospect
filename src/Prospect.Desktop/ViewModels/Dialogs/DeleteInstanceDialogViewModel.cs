using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Prospect.Core.Instances;
using Prospect.Core.Storage;
using Prospect.Desktop.Resources;
using Prospect.Desktop.Services;

namespace Prospect.Desktop.ViewModels.Dialogs;

/// <summary>
/// Dialogue de suppression ouvert depuis le menu d'une carte d'instance : la « double
/// confirmation nommant l'instance » voulue par la voix du produit (design/readme.md) est le
/// clic sur « Supprimer » du menu suivi de ce dialogue, dont le titre et le message nomment
/// explicitement l'instance et dont le bouton dit « Supprimer l'instance », jamais « OK ».
/// </summary>
public sealed partial class DeleteInstanceDialogViewModel : ObservableObject, IProgress<DirectoryDeleteProgress>
{
    private readonly string _slug;
    private readonly InstanceService _instanceService;
    private readonly IOverlayService _overlay;
    private readonly IUiDispatcher _dispatcher;
    private readonly Func<Task> _requestRefresh;

    public DeleteInstanceDialogViewModel(
        string slug,
        string name,
        InstanceService instanceService,
        IOverlayService overlay,
        IUiDispatcher dispatcher,
        Func<Task> requestRefresh)
    {
        ArgumentException.ThrowIfNullOrEmpty(slug);
        ArgumentNullException.ThrowIfNull(instanceService);
        ArgumentNullException.ThrowIfNull(overlay);
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(requestRefresh);

        _slug = slug;
        _instanceService = instanceService;
        _overlay = overlay;
        _dispatcher = dispatcher;
        _requestRefresh = requestRefresh;
        Title = UiText.Dialogs.DeleteTitle(name);
        Message = UiText.Dialogs.DeleteMessage(name);
    }

    public string Title { get; }

    public string Message { get; }

    /// <summary>
    /// Vrai pendant la suppression. Le dialogue RESTE ouvert et vivant : sur une instance dont les
    /// mondes pèsent plusieurs gigaoctets, l'effacement prend des dizaines de secondes, et le
    /// travail est déporté hors du thread d'interface (voir <c>InstanceService.DeleteAsync</c>) donc
    /// l'interface continue de se dessiner. Autant qu'elle dise ce qu'elle fait.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    private bool _isDeleting;

    /// <summary>Avancement de la suppression, de 0 à 100.</summary>
    [ObservableProperty]
    private double _progressPercent;

    /// <summary>
    /// « Suppression des fichiers (128/540) » une fois le premier relevé arrivé, la phrase
    /// d'attente avant. Compter les fichiers est rapide, les effacer ne l'est pas : c'est ce qui
    /// permet une barre déterminée plutôt qu'un rond qui tourne pendant quarante secondes.
    /// </summary>
    [ObservableProperty]
    private string _progressText = UiText.Dialogs.DeleteInProgress;

    /// <summary>Message d'échec, quand il reste des fichiers sur le disque. Le dialogue reste ouvert.</summary>
    [ObservableProperty]
    private string? _errorMessage;

    // Aucune annulation n'est offerte une fois la suppression commencée : une instance à moitié
    // supprimée est pire que les deux états francs. Le bouton disparaît donc plutôt que de mentir.
    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel() => _overlay.Close();

    /// <inheritdoc />
    public void Report(DirectoryDeleteProgress value)
    {
        ArgumentNullException.ThrowIfNull(value);

        _dispatcher.Post(() =>
        {
            ProgressPercent = value.Ratio * 100d;
            ProgressText = UiText.Dialogs.DeleteProgress(value.DeletedFiles, value.TotalFiles);
        });
    }

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private async Task ConfirmAsync()
    {
        IsDeleting = true;
        ErrorMessage = null;
        ProgressPercent = 0d;
        ProgressText = UiText.Dialogs.DeleteInProgress;
        try
        {
            await _instanceService.DeleteAsync(_slug, this, CancellationToken.None).ConfigureAwait(true);
            _overlay.Close();
            await _requestRefresh().ConfigureAwait(true);
        }
        catch (InstanceDeleteFailedException exception)
        {
            // Message honnête plutôt que silence : il reste quelque chose, et l'accueil doit quand
            // même être rafraîchi puisque la suppression a pu emporter une partie du dossier.
            ErrorMessage = UiText.Dialogs.DeletePartialFailure(exception.Directory);
            await _requestRefresh().ConfigureAwait(true);
        }
        catch (InstanceNotFoundException)
        {
            // Déjà supprimée entre-temps : rien à annoncer, on referme comme si c'était fait.
            _overlay.Close();
            await _requestRefresh().ConfigureAwait(true);
        }
        finally
        {
            IsDeleting = false;
        }
    }

    private bool CanConfirm() => !IsDeleting;

    private bool CanCancel() => !IsDeleting;
}