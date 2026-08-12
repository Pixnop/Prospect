using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Prospect.Core.Backups;
using Prospect.Core.Common;
using Prospect.Core.Instances;
using Prospect.Desktop.Formatting;
using Prospect.Desktop.Resources;
using Prospect.Desktop.Services;
using Prospect.Desktop.ViewModels.Dialogs;
using Prospect.Desktop.ViewModels.Toasts;

namespace Prospect.Desktop.ViewModels.Instance;

/// <summary>
/// Bloc Sauvegardes de l'onglet Options (composé par <see cref="InstanceOptionsTabViewModel"/>,
/// même principe que <c>InstanceModsTabViewModel</c> pour l'onglet Mods) : toggle auto-avant-
/// lancement, sélecteur de rétention, création avec progression, liste avec restauration et
/// suppression.
/// </summary>
public sealed partial class InstanceBackupsSectionViewModel : ObservableObject
{
    private readonly string _slug;
    private readonly string _instanceName;
    private readonly InstanceService _instanceService;
    private readonly InstanceBackupService _backupService;
    private readonly IOverlayService _overlay;
    private readonly IClock _clock;
    private readonly IToastService _toasts;

    public InstanceBackupsSectionViewModel(
        string slug,
        string instanceName,
        InstanceBackupSettings settings,
        InstanceService instanceService,
        InstanceBackupService backupService,
        IOverlayService overlay,
        IClock clock,
        IToastService toasts)
    {
        ArgumentException.ThrowIfNullOrEmpty(slug);
        ArgumentException.ThrowIfNullOrEmpty(instanceName);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(instanceService);
        ArgumentNullException.ThrowIfNull(backupService);
        ArgumentNullException.ThrowIfNull(overlay);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(toasts);

        _slug = slug;
        _instanceName = instanceName;
        _instanceService = instanceService;
        _backupService = backupService;
        _overlay = overlay;
        _clock = clock;
        _toasts = toasts;

        // Champs de réglage seuls (pas les propriétés générées) : les affecter ici ne doit pas
        // déclencher les On...Changed ci-dessous, qui persisteraient un réglage identique à celui
        // qu'on vient de charger (même piège que InstanceOptionsTabViewModel avec ses champs texte).
        _autoBeforeLaunch = settings.AutoBeforeLaunch;
        _keepCount = settings.KeepCount;
    }

    /// <summary>Les seuls choix proposés par le sélecteur (design : « garder N sauvegardes »).</summary>
    [SuppressMessage(
        "Performance",
        "CA1822:Mark members as static",
        Justification = "Propriété liée en XAML depuis l'instance du DataContext (x:DataType) : rester une " +
            "propriété d'instance est le contrat attendu par le binding, même si la valeur ne dépend d'aucun " +
            "champ aujourd'hui.")]
    public IReadOnlyList<int> AllowedKeepCounts => InstanceBackupSettings.AllowedKeepCounts;

    /// <summary>Sauvegardes existantes, les plus récentes d'abord.</summary>
    public ObservableCollection<InstanceBackupRowViewModel> Backups { get; } = [];

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _hasBackups;

    [ObservableProperty]
    private bool _autoBeforeLaunch;

    [ObservableProperty]
    private int _keepCount;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateNowCommand))]
    private bool _isCreating;

    /// <summary>« Sauvegarde en cours (3/12)… », vide en dehors d'une création.</summary>
    [ObservableProperty]
    private string _createProgressText = string.Empty;

    partial void OnAutoBeforeLaunchChanged(bool value) => _ = PersistSettingsAsync();

    partial void OnKeepCountChanged(int value) => _ = PersistSettingsAsync();

    /// <summary>Relit <c>backups/</c> : le disque est la source de vérité, jamais un état gardé en mémoire.</summary>
    [RelayCommand]
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        IsLoading = true;
        try
        {
            var backups = await _backupService.ListAsync(_slug, cancellationToken).ConfigureAwait(true);

            Backups.Clear();
            foreach (var backup in backups)
            {
                Backups.Add(new InstanceBackupRowViewModel(backup, _clock.UtcNow, RequestRestoreAsync, RequestDeleteAsync));
            }

            HasBackups = Backups.Count > 0;
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanCreate))]
    private async Task CreateNowAsync()
    {
        IsCreating = true;
        CreateProgressText = UiText.Instance.Backups.CreateProgress(0, 0);
        var progress = new Progress<InstanceBackupProgress>(report
            => CreateProgressText = UiText.Instance.Backups.CreateProgress(report.FilesProcessed, report.TotalFiles));

        try
        {
            await _backupService.CreateAsync(_slug, progress, CancellationToken.None).ConfigureAwait(true);
            await RefreshAsync(CancellationToken.None).ConfigureAwait(true);
            _toasts.Show(ToastTone.Success, UiText.Toasts.BackupCreatedTitle);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _toasts.Show(ToastTone.Error, UiText.Instance.Backups.CreateFailedTitle, exception.Message);
        }
        finally
        {
            CreateProgressText = string.Empty;
            IsCreating = false;
        }
    }

    private bool CanCreate() => !IsCreating;

    private Task RequestRestoreAsync(InstanceBackupRowViewModel row)
    {
        _overlay.Show(new RestoreInstanceBackupDialogViewModel(
            _slug, _instanceName, row.FileName, row.DateText, _backupService, _overlay, OnRestoredAsync));

        return Task.CompletedTask;
    }

    private async Task OnRestoredAsync()
    {
        await RefreshAsync(CancellationToken.None).ConfigureAwait(true);
        _toasts.Show(ToastTone.Success, UiText.Toasts.BackupRestoredTitle);
    }

    private Task RequestDeleteAsync(InstanceBackupRowViewModel row)
    {
        _overlay.Show(new DeleteInstanceBackupDialogViewModel(
            _slug, row.FileName, row.DateText, _backupService, _overlay, OnDeletedAsync));

        return Task.CompletedTask;
    }

    private async Task OnDeletedAsync()
    {
        await RefreshAsync(CancellationToken.None).ConfigureAwait(true);
        _toasts.Show(ToastTone.Info, UiText.Toasts.BackupDeletedTitle);
    }

    private async Task PersistSettingsAsync()
    {
        var settings = new InstanceBackupSettings { AutoBeforeLaunch = AutoBeforeLaunch, KeepCount = KeepCount };
        await _instanceService.UpdateBackupSettingsAsync(_slug, settings, CancellationToken.None).ConfigureAwait(true);
    }
}

/// <summary>Une ligne de sauvegarde (date humanisée, taille en monospace) avec ses deux actions.</summary>
public sealed partial class InstanceBackupRowViewModel : ObservableObject
{
    private readonly Func<InstanceBackupRowViewModel, Task> _restore;
    private readonly Func<InstanceBackupRowViewModel, Task> _delete;

    public InstanceBackupRowViewModel(
        InstanceBackupInfo info,
        DateTimeOffset now,
        Func<InstanceBackupRowViewModel, Task> restore,
        Func<InstanceBackupRowViewModel, Task> delete)
    {
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(restore);
        ArgumentNullException.ThrowIfNull(delete);

        Info = info;
        _restore = restore;
        _delete = delete;

        FileName = info.FileName;
        SizeText = ByteSizeFormatter.Format(info.SizeInBytes);
        DateText = RelativeDateFormatter.Format(info.CreatedUtc, now);
    }

    /// <summary>Sauvegarde sous-jacente, telle que rendue par <see cref="InstanceBackupService.ListAsync"/>.</summary>
    public InstanceBackupInfo Info { get; }

    public string FileName { get; }

    /// <summary>Valeur machine (design/readme.md, « Content fundamentals ») : liée en FontMono côté vue.</summary>
    public string SizeText { get; }

    public string DateText { get; }

    [RelayCommand]
    private Task RestoreAsync() => _restore(this);

    [RelayCommand]
    private Task DeleteAsync() => _delete(this);
}