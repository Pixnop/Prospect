using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Prospect.Core.ModDb;
using Prospect.Desktop.Resources;
using Prospect.Desktop.Services;

namespace Prospect.Desktop.ViewModels.Mods;

/// <summary>
/// Confirmation d'installation : le mod demandé, et la liste COCHABLE de ses dépendances
/// déclarées manquantes.
/// </summary>
/// <remarks>
/// Ce dialogue est le seul chemin par lequel une dépendance peut être installée. Les cases sont
/// cochées d'avance parce que c'est presque toujours ce qu'on veut, mais elles restent décochables
/// et rien ne part sans que ce bouton soit cliqué : le contrat dit « proposées à l'installation en
/// un clic, jamais installées en silence », et une case cochée que l'utilisateur voit et peut
/// décocher n'est pas du silence.
/// </remarks>
public sealed partial class ModInstallPlanDialogViewModel : ObservableObject
{
    private readonly ModInstallPlan _plan;
    private readonly Func<IReadOnlyCollection<string>, Task> _confirm;
    private readonly IOverlayService _overlay;

    /// <summary>Construit le dialogue.</summary>
    /// <param name="plan">Plan produit par <see cref="ModInstallService.PrepareAsync"/>.</param>
    /// <param name="instanceName">Nom de l'instance cible, montré dans le titre.</param>
    /// <param name="confirm">Applique le plan avec les dépendances retenues.</param>
    /// <param name="overlay">Panneau modal, pour se refermer.</param>
    public ModInstallPlanDialogViewModel(
        ModInstallPlan plan,
        string instanceName,
        Func<IReadOnlyCollection<string>, Task> confirm,
        IOverlayService overlay)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(confirm);
        ArgumentNullException.ThrowIfNull(overlay);

        _plan = plan;
        _confirm = confirm;
        _overlay = overlay;

        Title = UiText.Mods.PlanTitle(plan.Primary.DisplayName);
        Message = UiText.Mods.PlanMessage(plan.Primary.Version.ToString(), instanceName);
        FileNameText = plan.Primary.TargetFileName;

        ShowApproximateWarning = plan.Primary.IsApproximateMatch;
        ApproximateWarning = UiText.Mods.ApproximateWarning(plan.GameVersion.ToString());

        Dependencies = plan.MissingDependencies
            .Select(item => new ModDependencyChoiceViewModel(item, plan.Issues))
            .ToArray();
        HasDependencies = Dependencies.Count > 0;

        UnresolvedMessage = UiText.Mods.UnresolvedDependencies(plan.UnresolvedDependencies);
        HasUnresolved = plan.UnresolvedDependencies.Count > 0;

        var disabled = plan.Issues.Where(issue => issue.Status == ModDependencyStatus.Disabled).Select(issue => issue.ModIdString).ToArray();
        DisabledMessage = UiText.Mods.DisabledDependencies(disabled);
        HasDisabled = disabled.Length > 0;
    }

    public string Title { get; }

    public string Message { get; }

    /// <summary>Nom du fichier qui sera écrit, en monospace comme toute valeur machine.</summary>
    public string FileNameText { get; }

    public bool ShowApproximateWarning { get; }

    public string ApproximateWarning { get; }

    public IReadOnlyList<ModDependencyChoiceViewModel> Dependencies { get; }

    public bool HasDependencies { get; }

    public string UnresolvedMessage { get; }

    public bool HasUnresolved { get; }

    public string DisabledMessage { get; }

    public bool HasDisabled { get; }

    [ObservableProperty]
    private bool _isBusy;

    [RelayCommand]
    private void Cancel() => _overlay.Close();

    [RelayCommand]
    private async Task ConfirmAsync()
    {
        IsBusy = true;
        try
        {
            var selected = Dependencies
                .Where(dependency => dependency.IsSelected)
                .Select(dependency => dependency.ModIdString)
                .ToArray();

            await _confirm(selected).ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Plan sous-jacent, exposé pour les tests et pour le rappel de la version cible.</summary>
    public ModInstallPlan Plan => _plan;
}

/// <summary>Une dépendance manquante, cochable, dans le dialogue de confirmation.</summary>
public sealed partial class ModDependencyChoiceViewModel : ObservableObject
{
    /// <summary>Construit la ligne.</summary>
    /// <param name="item">Release résolue pour cette dépendance.</param>
    /// <param name="issues">Diagnostic complet, pour expliquer pourquoi elle est proposée.</param>
    public ModDependencyChoiceViewModel(ModInstallItem item, IReadOnlyList<ModDependencyIssue> issues)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(issues);

        ModIdString = item.ModIdString;
        DisplayName = item.DisplayName;
        VersionText = item.Version.ToString();

        var issue = issues.FirstOrDefault(candidate => string.Equals(candidate.ModIdString, item.ModIdString, StringComparison.OrdinalIgnoreCase));
        ReasonText = UiText.Mods.DependencyReason(issue);
    }

    public string ModIdString { get; }

    public string DisplayName { get; }

    public string VersionText { get; }

    /// <summary>Pourquoi cette dépendance est proposée : absente, trop ancienne, signalée par le ModDB.</summary>
    public string ReasonText { get; }

    [ObservableProperty]
    private bool _isSelected = true;
}