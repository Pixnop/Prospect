using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Prospect.Core.ModDb;
using Prospect.Desktop.Resources;
using Prospect.Desktop.Services;

namespace Prospect.Desktop.ViewModels.Mods;

/// <summary>
/// Confirmation de mise à jour d'un mod déjà installé : version actuelle et cible, nouvelles
/// dépendances COCHABLES exactement comme à l'installation, et liste INFORMATIVE des mods qui
/// dépendent de celui-ci.
/// </summary>
/// <remarks>
/// La liste des dépendants n'est jamais bloquante (docs/architecture.md, précision d'architecte) :
/// nos contraintes déclarées sont des bornes minimales (<c>&gt;=</c>), donc monter une version ne
/// peut mathématiquement pas violer une contrainte déclarée par un dépendant. Le risque d'une
/// montée est fonctionnel (rupture d'API non déclarée par l'auteur), pas de version, ce que ce
/// dialogue se contente de signaler pour que l'utilisateur sache qui pourrait être affecté, sans
/// jamais l'empêcher de continuer.
/// </remarks>
public sealed partial class ModUpdatePlanDialogViewModel : ObservableObject
{
    private readonly ModUpdatePlan _plan;
    private readonly Func<IReadOnlyCollection<string>, Task> _confirm;
    private readonly IOverlayService _overlay;

    /// <summary>Construit le dialogue.</summary>
    /// <param name="plan">Plan produit par <see cref="ModInstallService.PrepareUpdateAsync"/>.</param>
    /// <param name="confirm">Applique le plan avec les dépendances retenues.</param>
    /// <param name="overlay">Panneau modal, pour se refermer.</param>
    public ModUpdatePlanDialogViewModel(
        ModUpdatePlan plan,
        Func<IReadOnlyCollection<string>, Task> confirm,
        IOverlayService overlay)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(confirm);
        ArgumentNullException.ThrowIfNull(overlay);

        _plan = plan;
        _confirm = confirm;
        _overlay = overlay;

        Title = UiText.Mods.UpdatePlanTitle(plan.Previous.DisplayName);
        Message = UiText.Mods.UpdatePlanMessage(plan.Previous.Version?.ToString() ?? UiText.Mods.UnknownVersion, plan.Updated.Version.ToString());
        FileNameText = plan.Updated.TargetFileName;

        ShowApproximateWarning = plan.Updated.IsApproximateMatch;
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

        HasDependents = plan.HasDependents;
        DependentsNote = UiText.Mods.UpdateDependentsNote(plan.DependentNames);
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

    /// <summary>Vrai si au moins un mod installé déclare dépendre de celui qu'on met à jour. INFORMATIF, jamais bloquant.</summary>
    public bool HasDependents { get; }

    /// <summary>« X dépend de ce mod. », vide si personne n'en dépend.</summary>
    public string DependentsNote { get; }

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

    /// <summary>Plan sous-jacent, exposé pour les tests.</summary>
    public ModUpdatePlan Plan => _plan;
}
