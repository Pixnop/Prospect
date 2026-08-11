using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Prospect.Core.Common;
using Prospect.Core.Instances;
using Prospect.Desktop.Resources;
using Prospect.Desktop.Services;

namespace Prospect.Desktop.ViewModels.Wizard;

/// <summary>
/// Wizard de création d'instance en 4 étapes (design/ui_kits/launcher/screen-wizard.jsx) : nom
/// (avec aperçu du slug), version du jeu, icône, récapitulatif + création. Décision documentée
/// dans le corps de la PR : la version est pour l'instant un champ texte validé par
/// <see cref="GameVersion.TryParse"/>, le vrai sélecteur branché sur le catalogue arrivant avec la
/// PR des versions du jeu.
/// </summary>
public sealed partial class WizardViewModel : ObservableObject
{
    /// <summary>
    /// Icônes proposées à l'étape 3 : aucune icône d'instance n'est fournie par le handoff design
    /// (voir design/readme.md, ICONOGRAPHY), donc un sous-ensemble curé du jeu de glyphes existant
    /// en attendant la personnalisation par fichier, prévue plus tard.
    /// </summary>
    private static readonly (string Key, string IconKey, string Label)[] IconCatalog =
    [
        ("default", "layers", "Par défaut"),
        ("package", "package", "Caisse"),
        ("star", "star", "Étoile"),
        ("hard-drive", "hard-drive", "Disque"),
        ("image", "image", "Image"),
    ];

    private readonly InstanceService _instanceService;
    private readonly IOverlayService _overlay;

    public WizardViewModel(InstanceService instanceService, IOverlayService overlay)
    {
        ArgumentNullException.ThrowIfNull(instanceService);
        ArgumentNullException.ThrowIfNull(overlay);

        _instanceService = instanceService;
        _overlay = overlay;
        _selectedIconKey = IconCatalog[0].Key;

        RecomputeSteps();
        RecomputeIconChoices();
    }

    /// <summary>Levé une fois l'instance effectivement créée (icône appliquée comprise). L'abonné referme le wizard.</summary>
    public event EventHandler<InstanceRecord>? Created;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGoNext))]
    [NotifyPropertyChangedFor(nameof(CanGoBack))]
    [NotifyPropertyChangedFor(nameof(IsLastStep))]
    [NotifyPropertyChangedFor(nameof(IsNameStep))]
    [NotifyPropertyChangedFor(nameof(IsVersionStep))]
    [NotifyPropertyChangedFor(nameof(IsIconStep))]
    [NotifyPropertyChangedFor(nameof(IsSummaryStep))]
    [NotifyCanExecuteChangedFor(nameof(NextCommand))]
    [NotifyCanExecuteChangedFor(nameof(BackCommand))]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    private int _currentStepIndex;

    partial void OnCurrentStepIndexChanged(int value) => RecomputeSteps();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SlugPreview))]
    [NotifyPropertyChangedFor(nameof(NameError))]
    [NotifyPropertyChangedFor(nameof(IsNameStepValid))]
    [NotifyPropertyChangedFor(nameof(CanGoNext))]
    [NotifyCanExecuteChangedFor(nameof(NextCommand))]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    private string _name = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VersionError))]
    [NotifyPropertyChangedFor(nameof(IsVersionStepValid))]
    [NotifyPropertyChangedFor(nameof(CanGoNext))]
    [NotifyCanExecuteChangedFor(nameof(NextCommand))]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    private string _versionText = string.Empty;

    [ObservableProperty]
    private string _selectedIconKey;

    partial void OnSelectedIconKeyChanged(string value) => RecomputeIconChoices();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    private bool _isCreating;

    [ObservableProperty]
    private string? _createError;

    public IReadOnlyList<string> StepLabels { get; } = UiText.Wizard.StepLabels;

    [ObservableProperty]
    private IReadOnlyList<WizardStepIndicatorViewModel> _steps = [];

    [ObservableProperty]
    private IReadOnlyList<IconChoiceOption> _iconChoices = [];

    public bool IsNameStep => CurrentStepIndex == 0;

    public bool IsVersionStep => CurrentStepIndex == 1;

    public bool IsIconStep => CurrentStepIndex == 2;

    public bool IsSummaryStep => CurrentStepIndex == 3;

    /// <summary>Aperçu du slug de dossier, recalculé à chaque frappe (voir <see cref="InstanceSlugGenerator.Slugify"/>).</summary>
    public string SlugPreview => InstanceSlugGenerator.Slugify(Name);

    public string? NameError => string.IsNullOrWhiteSpace(Name) ? UiText.Wizard.NameRequired : null;

    public bool IsNameStepValid => NameError is null;

    public string? VersionError
    {
        get
        {
            if (string.IsNullOrWhiteSpace(VersionText))
            {
                return UiText.Wizard.VersionRequired;
            }

            return GameVersion.TryParse(VersionText, out _) ? null : UiText.Wizard.VersionInvalidFormat;
        }
    }

    public bool IsVersionStepValid => VersionError is null;

    private bool IsCurrentStepValid => CurrentStepIndex switch
    {
        0 => IsNameStepValid,
        1 => IsVersionStepValid,
        _ => true,
    };

    public bool CanGoNext => !IsLastStep && IsCurrentStepValid;

    public bool CanGoBack => CurrentStepIndex > 0;

    public bool IsLastStep => CurrentStepIndex == StepLabels.Count - 1;

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private void Next() => CurrentStepIndex++;

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void Back() => CurrentStepIndex--;

    [RelayCommand]
    private void Cancel() => _overlay.Close();

    [RelayCommand(CanExecute = nameof(CanCreate))]
    private async Task CreateAsync()
    {
        if (!GameVersion.TryParse(VersionText, out var version))
        {
            return;
        }

        IsCreating = true;
        CreateError = null;
        try
        {
            var created = await _instanceService.CreateAsync(Name.Trim(), version).ConfigureAwait(true);

            if (SelectedIconKey != IconCatalog[0].Key)
            {
                created = await _instanceService.SetIconAsync(created.Slug, $"builtin:{SelectedIconKey}").ConfigureAwait(true);
            }

            Created?.Invoke(this, created);
            _overlay.Close();
        }
        catch (InstanceNameInvalidException ex)
        {
            CreateError = ex.Message;
        }
        finally
        {
            IsCreating = false;
        }
    }

    private bool CanCreate() => IsLastStep && IsNameStepValid && IsVersionStepValid && !IsCreating;

    private void RecomputeSteps()
    {
        var steps = new WizardStepIndicatorViewModel[StepLabels.Count];
        for (var index = 0; index < StepLabels.Count; index++)
        {
            steps[index] = new WizardStepIndicatorViewModel(index + 1, StepLabels[index], isDone: index < CurrentStepIndex, isCurrent: index == CurrentStepIndex);
        }

        Steps = steps;
    }

    private void RecomputeIconChoices()
        => IconChoices = IconCatalog
            .Select(icon => new IconChoiceOption(icon.Key, icon.IconKey, icon.Label, icon.Key == SelectedIconKey, SelectIcon))
            .ToArray();

    private void SelectIcon(string key) => SelectedIconKey = key;
}