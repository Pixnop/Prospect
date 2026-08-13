using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Prospect.Core.Common;
using Prospect.Core.Http;
using Prospect.Desktop.Resources;
using Prospect.Desktop.Services;

namespace Prospect.Desktop.ViewModels.Downloads;

/// <summary>
/// Contenu du popover Téléchargements de la barre latérale
/// (design/ui_kits/launcher/app-shell.jsx) : une vue sur la file du
/// <see cref="IDownloadManager"/>, rien d'autre. Aucune logique de téléchargement ici, le
/// ViewModel ne fait que refléter ce que le Core expose.
///
/// La file contient désormais aussi ce qui est TERMINÉ, réussi comme échoué : le popover ne
/// montrait que l'actif, donc un téléchargement disparaissait à la seconde où il aboutissait et
/// plus rien ne disait qu'il avait eu lieu. La rétention et le bornage vivent dans le Core
/// (<see cref="DownloadOptions.HistoryLimit"/>) ; ici on présente. Cet historique est de SESSION :
/// rien n'est écrit sur disque, un historique persisté serait une autre décision.
/// </summary>
public sealed partial class DownloadsViewModel : ObservableObject, IDisposable
{
    private readonly IDownloadManager _downloads;
    private readonly IUiDispatcher _dispatcher;
    private readonly IClock _clock;

    public DownloadsViewModel(IDownloadManager downloads, IUiDispatcher dispatcher, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(downloads);
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(clock);

        _downloads = downloads;
        _dispatcher = dispatcher;
        _clock = clock;
        _downloads.OperationsChanged += OnOperationsChanged;
        Synchronize();
    }

    /// <summary>Lignes affichées : les vivantes d'abord, l'historique ensuite, du plus récent au plus ancien.</summary>
    public ObservableCollection<DownloadItemViewModel> Items { get; } = [];

    [ObservableProperty]
    private bool _hasDownloads;

    /// <summary>Vrai dès qu'une ligne terminée est affichée : c'est ce qui allume « Tout effacer ».</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ClearFinishedCommand))]
    private bool _hasFinished;

    /// <summary>
    /// Pastille de la barre latérale. Compte les téléchargements VIVANTS, pas les lignes affichées :
    /// une pastille qui reste allumée sur un historique dirait qu'il se passe quelque chose alors
    /// que tout est fini.
    /// </summary>
    [ObservableProperty]
    private int _count;

    /// <summary>Compteur affiché en pied de popover, par exemple « 2 actifs · 1 en attente ».</summary>
    [ObservableProperty]
    private string _summaryText = string.Empty;

    /// <summary>
    /// Reprend les heures relatives. Appelé à l'ouverture du popover : le texte « il y a 3 minutes »
    /// n'a pas besoin d'un minuteur qui tourne pour un panneau fermé.
    /// </summary>
    public void RefreshElapsed()
    {
        foreach (var item in Items)
        {
            item.RefreshElapsed();
        }
    }

    public void Dispose()
    {
        _downloads.OperationsChanged -= OnOperationsChanged;
        foreach (var item in Items)
        {
            item.Dispose();
        }

        Items.Clear();
    }

    [RelayCommand(CanExecute = nameof(HasFinished))]
    private void ClearFinished() => _downloads.DismissFinished();

    private void OnOperationsChanged(object? sender, EventArgs e) => _dispatcher.Post(Synchronize);

    // Réconciliation plutôt que reconstruction : une ligne déjà affichée garde son objet, donc son
    // abonnement et son état de progression, au lieu de clignoter à chaque changement de file.
    private void Synchronize()
    {
        var operations = _downloads.Operations;

        for (var index = Items.Count - 1; index >= 0; index--)
        {
            if (!operations.Contains(Items[index].Operation))
            {
                Items[index].Dispose();
                Items.RemoveAt(index);
            }
        }

        foreach (var operation in operations)
        {
            if (!Items.Any(item => item.Operation == operation))
            {
                Items.Add(new DownloadItemViewModel(operation, _dispatcher, _clock, _downloads.Dismiss));
            }
        }

        Reorder();

        HasDownloads = Items.Count > 0;
        HasFinished = Items.Any(item => item.IsFinished);
        Count = operations.Count(operation => !operation.IsFinished);
        SummaryText = UiText.Downloads.Summary(
            operations.Count(operation => operation.State is DownloadState.Running or DownloadState.Verifying),
            operations.Count(operation => operation.State == DownloadState.Queued));
    }

    // Ce qui bouge en haut, ce qui est fini en dessous, le plus récent d'abord : lire l'historique
    // à l'envers du temps est le seul ordre qui ait un sens quand on vient de faire quelque chose.
    // Move plutôt que Clear + Add : la collection est liée à une vue, et tout reconstruire ferait
    // clignoter des lignes qui n'ont pas changé.
    private void Reorder()
    {
        var ordered = Items
            .OrderBy(item => item.IsFinished)
            .ThenByDescending(item => item.Operation.FinishedUtc ?? DateTimeOffset.MinValue)
            .ToArray();

        for (var target = 0; target < ordered.Length; target++)
        {
            var current = Items.IndexOf(ordered[target]);
            if (current != target)
            {
                Items.Move(current, target);
            }
        }
    }
}