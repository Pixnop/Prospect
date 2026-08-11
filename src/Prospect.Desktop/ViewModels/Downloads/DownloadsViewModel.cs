using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;

using Prospect.Core.Http;
using Prospect.Desktop.Resources;
using Prospect.Desktop.Services;

namespace Prospect.Desktop.ViewModels.Downloads;

/// <summary>
/// Contenu du popover Téléchargements de la barre latérale
/// (design/ui_kits/launcher/app-shell.jsx) : une vue sur la file du
/// <see cref="IDownloadManager"/>, rien d'autre. Aucune logique de téléchargement ici, le
/// ViewModel ne fait que refléter ce que le Core expose.
/// </summary>
public sealed partial class DownloadsViewModel : ObservableObject, IDisposable
{
    private readonly IDownloadManager _downloads;
    private readonly IUiDispatcher _dispatcher;

    public DownloadsViewModel(IDownloadManager downloads, IUiDispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(downloads);
        ArgumentNullException.ThrowIfNull(dispatcher);

        _downloads = downloads;
        _dispatcher = dispatcher;
        _downloads.OperationsChanged += OnOperationsChanged;
        Synchronize();
    }

    /// <summary>Lignes affichées, dans l'ordre de la file.</summary>
    public ObservableCollection<DownloadItemViewModel> Items { get; } = [];

    [ObservableProperty]
    private bool _hasDownloads;

    [ObservableProperty]
    private int _count;

    /// <summary>Compteur affiché en pied de popover, par exemple « 2 actifs · 1 en attente ».</summary>
    [ObservableProperty]
    private string _summaryText = string.Empty;

    public void Dispose()
    {
        _downloads.OperationsChanged -= OnOperationsChanged;
        foreach (var item in Items)
        {
            item.Dispose();
        }

        Items.Clear();
    }

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
                Items.Add(new DownloadItemViewModel(operation, _dispatcher));
            }
        }

        Count = Items.Count;
        HasDownloads = Count > 0;
        SummaryText = UiText.Downloads.Summary(
            operations.Count(operation => operation.State is DownloadState.Running or DownloadState.Verifying),
            operations.Count(operation => operation.State == DownloadState.Queued));
    }
}