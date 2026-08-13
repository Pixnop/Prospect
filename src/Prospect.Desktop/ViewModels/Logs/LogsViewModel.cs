using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Prospect.Core.Diagnostics;
using Prospect.Desktop.Resources;
using Prospect.Desktop.Services;
using Prospect.Desktop.ViewModels.Toasts;

namespace Prospect.Desktop.ViewModels.Logs;

/// <summary>
/// Page « Journaux » : la fin de <c>logs/prospect.log</c> lisible depuis l'application, et un
/// export zip de tous les journaux à joindre à un rapport.
/// </summary>
/// <remarks>
/// <para>
/// Elle existe parce que le journal était jusqu'ici invisible depuis l'application : le seul moyen
/// de le lire était de connaître son chemin et d'ouvrir un explorateur de fichiers, ce qui en fait
/// une pièce que personne ne joint jamais à un rapport. Le défaut d'installation Linux corrigé dans
/// cette même branche a été diagnostiqué à partir d'UNE ligne de ce fichier.
/// </para>
/// <para>
/// La page se RELIT TOUTE SEULE tant qu'elle est affichée, à intervalle court, et cesse dès qu'on
/// la quitte (voir <see cref="ILivePage"/>). Le bouton Rafraîchir reste : il ne coûte rien et sert
/// encore à qui veut forcer une relecture à la seconde près.
/// </para>
/// <para>
/// C'est un revirement, et il vaut mieux dire lequel : cette page annonçait un INSTANTANÉ, au motif
/// qu'un vrai <c>tail -f</c> demanderait un observateur de système de fichiers, une politique de
/// débit et une gestion de la troncature. L'argument tenait pour un <c>tail -f</c> ; il ne tenait
/// pas pour l'usage réel, qui est de regarder cette page PENDANT qu'on reproduit un défaut dans une
/// autre fenêtre. Relire un fichier plafonné à un mégaoctet toutes les deux secondes ne demande
/// aucune de ces trois choses, et l'instantané obligeait à cliquer entre chaque tentative — ce qui,
/// dans un rapport de terrain, s'écrit « les journaux ne se mettent pas à jour ».
/// </para>
/// <para>
/// Deux précautions rendent la relecture périodique inoffensive. Elle ne tourne QUE sur la page
/// courante : quitter les Journaux arrête la boucle, et rien ne relit un fichier que personne ne
/// regarde. Et elle ne reconstruit la liste QUE si le contenu a changé (voir <see cref="Refresh"/>),
/// sans quoi une page ouverte sur un journal calme reconstruirait cinq cents contrôles toutes les
/// deux secondes pour afficher très exactement la même chose.
/// </para>
/// </remarks>
public sealed partial class LogsViewModel : ObservableObject, ILivePage, IDisposable
{
    /// <summary>
    /// Battement de la relecture. Assez court pour qu'une ligne apparaisse pendant qu'on regarde,
    /// assez long pour que la lecture d'un fichier plafonné ne pèse rien.
    /// </summary>
    public static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(2);

    private readonly AppLogService _logs;
    private readonly IFilePickerService _filePicker;
    private readonly IToastService _toasts;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    private CancellationTokenSource? _liveCancellation;

    /// <summary>Construit la page.</summary>
    /// <param name="logs">Lecture et export du dossier <c>logs/</c>.</param>
    /// <param name="filePicker">Sélecteur de destination de l'export.</param>
    /// <param name="toasts">Pile de toasts, pour rendre compte d'un export abouti.</param>
    /// <param name="delay">
    /// Attente entre deux relectures. Paramètre injectable plutôt qu'un appel direct à
    /// <see cref="Task.Delay(TimeSpan, CancellationToken)"/>, même idiome que
    /// <c>WindowsGameInstallStrategy</c> et <c>RetryPolicy</c> : <c>IClock</c> ne rend que l'heure,
    /// pas un battement, et un test qui attendrait de vraies secondes ne serait pas un test.
    /// </param>
    public LogsViewModel(
        AppLogService logs,
        IFilePickerService filePicker,
        IToastService toasts,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        ArgumentNullException.ThrowIfNull(logs);
        ArgumentNullException.ThrowIfNull(filePicker);
        ArgumentNullException.ThrowIfNull(toasts);

        _logs = logs;
        _filePicker = filePicker;
        _toasts = toasts;
        _delay = delay ?? Task.Delay;
        LogPathText = logs.AppLogPath;
    }

    /// <summary>Vrai tant que la relecture périodique tourne. Exposé pour les tests et pour le libellé.</summary>
    [ObservableProperty]
    private bool _isLive;

    /// <inheritdoc />
    /// <remarks>
    /// Relit TOUT DE SUITE, puis à chaque battement : c'est cette première lecture qui remplace
    /// l'appel explicite que le shell faisait à l'entrée sur la page.
    /// </remarks>
    public void StartLiveRefresh()
    {
        StopLiveRefresh();

        Refresh();

        _liveCancellation = new CancellationTokenSource();
        IsLive = true;
        _ = RunLiveLoopAsync(_liveCancellation.Token);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Rien d'autre que l'arrêt de la boucle : la page reste parfaitement utilisable après, ce qui
    /// est indispensable puisque le conteneur n'en construit qu'un exemplaire et le rend à chaque
    /// visite. C'est aussi tout ce que fait <see cref="Dispose"/>.
    /// </remarks>
    public void Dispose() => StopLiveRefresh();

    /// <inheritdoc />
    public void StopLiveRefresh()
    {
        if (_liveCancellation is not { } cancellation)
        {
            return;
        }

        _liveCancellation = null;
        IsLive = false;
        cancellation.Cancel();
        cancellation.Dispose();
    }

    /// <remarks>
    /// La boucle vit sur le thread d'interface : <see cref="Refresh"/> écrit dans une
    /// <see cref="ObservableCollection{T}"/> liée, et <see cref="Task.Delay(TimeSpan, CancellationToken)"/>
    /// reprend sur le contexte capturé, celui du dispatcher. Rien à marshaller, donc, tant que
    /// <see cref="StartLiveRefresh"/> est appelée depuis ce thread — ce que le shell garantit,
    /// puisqu'il navigue depuis un clic.
    /// </remarks>
    private async Task RunLiveLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await _delay(RefreshInterval, cancellationToken).ConfigureAwait(true);
                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                Refresh();
            }
        }
        catch (OperationCanceledException)
        {
            // Sortie normale : on a quitté la page.
        }
    }

    /// <summary>Lignes affichées, de la plus ancienne à la plus récente.</summary>
    public ObservableCollection<LogLineViewModel> Lines { get; } = [];

    /// <summary>Chemin du journal d'application, affiché en monospace sous la liste.</summary>
    public string LogPathText { get; }

    /// <summary>Vrai dès qu'il y a quelque chose à lire.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private bool _hasLines;

    /// <summary>Vrai quand aucun journal n'a encore été écrit : la page affiche son état vide.</summary>
    public bool IsEmpty => !HasLines;

    /// <summary>Nombre de fichiers que l'export emporterait, zéro quand il n'y en a aucun.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanExport))]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
    private int _exportableFileCount;

    /// <summary>Message d'échec de l'export, <see langword="null"/> le reste du temps.</summary>
    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanExport))]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
    private bool _isExporting;

    /// <summary>Exporter n'a de sens qu'avec au moins un journal, et pas deux fois à la fois.</summary>
    public bool CanExport => ExportableFileCount > 0 && !IsExporting;

    /// <summary>Résumé sous le titre : ce qui est affiché et ce qui serait exporté.</summary>
    [ObservableProperty]
    private string _subtitleText = string.Empty;

    /// <summary>
    /// Relit la fin du journal. Appelée à l'entrée sur la page, à chaque battement de la relecture
    /// périodique, et par le bouton.
    /// </summary>
    /// <remarks>
    /// La liste n'est reconstruite QUE si le texte lu diffère de celui déjà affiché. Sans cette
    /// garde, un battement de deux secondes viderait et repeuplerait cinq cents contrôles en
    /// permanence, y compris sur un journal qui ne bouge pas : la page perdrait sa position de
    /// défilement et toute sélection de texte à chaque tour, ce qui la rendrait inutilisable
    /// précisément quand on l'utilise, c'est-à-dire en lisant.
    /// </remarks>
    [RelayCommand]
    private void Refresh()
    {
        var entries = _logs.ReadTail();

        if (!MatchesDisplayedLines(entries))
        {
            Lines.Clear();
            foreach (var entry in entries)
            {
                Lines.Add(new LogLineViewModel(entry));
            }
        }

        HasLines = Lines.Count > 0;
        ExportableFileCount = _logs.FindLogFiles().Count;
        SubtitleText = UiText.Logs.Subtitle(Lines.Count, ExportableFileCount);
    }

    private bool MatchesDisplayedLines(IReadOnlyList<AppLogEntry> entries)
    {
        if (entries.Count != Lines.Count)
        {
            return false;
        }

        for (var index = 0; index < entries.Count; index++)
        {
            if (!string.Equals(entries[index].Text, Lines[index].Text, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task ExportAsync()
    {
        var destination = await _filePicker
            .PickSaveFileAsync(UiText.Logs.ExportPickerTitle, UiText.Logs.ExportFileName, "zip")
            .ConfigureAwait(true);

        if (destination is null)
        {
            // Sélecteur annulé : retour silencieux, ce n'est pas une erreur.
            return;
        }

        ErrorMessage = null;
        IsExporting = true;
        try
        {
            var written = await _logs.ExportAsync(destination, CancellationToken.None).ConfigureAwait(true);
            _toasts.Show(ToastTone.Success, UiText.Toasts.LogsExportedTitle, UiText.Logs.ExportedToastDescription(written));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ErrorMessage = exception.Message;
        }
        finally
        {
            IsExporting = false;
        }
    }
}