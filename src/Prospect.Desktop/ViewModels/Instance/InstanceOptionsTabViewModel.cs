using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Prospect.Core.Instances;
using Prospect.Core.Storage;
using Prospect.Desktop.Resources;
using Prospect.Desktop.Services;
using Prospect.Desktop.ViewModels.Toasts;

namespace Prospect.Desktop.ViewModels.Instance;

/// <summary>
/// Onglet Options de la page de détail : édition des arguments de lancement et des variables
/// d'environnement propres à une instance, sauvegardés via
/// <see cref="InstanceService.UpdateLaunchSettingsAsync"/>.
/// </summary>
/// <remarks>
/// Deux choix d'édition documentés ici. Les arguments supplémentaires (<see cref="ExtraArgsText"/>)
/// sont saisis un par ligne plutôt qu'en une seule chaîne façon ligne de commande : tokeniser une
/// chaîne façon shell (espaces, guillemets) réintroduirait exactement l'ambiguïté que
/// docs/research/vslauncher-et-distribution.md reproche à VS Launcher (implication 9), alors
/// qu'une ligne par argument correspond une fois pour toutes à un élément de la liste
/// <see cref="InstanceLaunchSettings.ExtraArgs"/>, sans règle de découpage à documenter ni à
/// maintenir. Les variables d'environnement (<see cref="EnvVarsText"/>) suivent le même principe,
/// au format <c>CLE=valeur</c> par ligne. <c>MESA_GLTHREAD</c> (confort Linux mentionné par la
/// recherche) a sa propre case à cocher plutôt que de vivre dans le texte libre : c'est la seule
/// variable que Prospect connaît par son nom, une case dédiée évite à l'utilisateur de retenir sa
/// syntaxe exacte.
/// </remarks>
public sealed partial class InstanceOptionsTabViewModel : ObservableObject
{
    private const string MesaGlThreadKey = "MESA_GLTHREAD";
    private const string MesaGlThreadValue = "true";

    private readonly string _slug;
    private readonly InstanceService _instanceService;
    private readonly IToastService _toasts;

    public InstanceOptionsTabViewModel(
        string slug,
        InstanceLaunchSettings settings,
        InstanceService instanceService,
        IAppEnvironment appEnvironment,
        IToastService toasts)
    {
        ArgumentException.ThrowIfNullOrEmpty(slug);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(instanceService);
        ArgumentNullException.ThrowIfNull(appEnvironment);
        ArgumentNullException.ThrowIfNull(toasts);

        _slug = slug;
        _instanceService = instanceService;
        _toasts = toasts;
        ShowMesaGlThreadToggle = appEnvironment.CurrentOperatingSystem == AppOperatingSystem.Linux;

        _extraArgsText = string.Join(Environment.NewLine, settings.ExtraArgs);
        _envVarsText = string.Join(
            Environment.NewLine,
            settings.Env
                .Where(pair => !string.Equals(pair.Key, MesaGlThreadKey, StringComparison.Ordinal))
                .Select(pair => $"{pair.Key}={pair.Value}"));
        _mesaGlThreadEnabled = settings.Env.TryGetValue(MesaGlThreadKey, out var mesaValue)
            && string.Equals(mesaValue, MesaGlThreadValue, StringComparison.Ordinal);
    }

    /// <summary>Vrai uniquement sur Linux : <c>MESA_GLTHREAD</c> n'a d'effet que sur les pilotes Mesa de cette plateforme.</summary>
    public bool ShowMesaGlThreadToggle { get; }

    [ObservableProperty]
    private string _extraArgsText;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EnvVarsError))]
    [NotifyPropertyChangedFor(nameof(IsValid))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _envVarsText;

    [ObservableProperty]
    private bool _mesaGlThreadEnabled;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool _isSaving;

    /// <summary>Message de validation des variables d'environnement, <see langword="null"/> quand le texte est exploitable.</summary>
    public string? EnvVarsError => TryParseEnvVars(EnvVarsText, out _) ? null : UiText.Instance.EnvVarsInvalidLine;

    public bool IsValid => EnvVarsError is null;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        if (!TryParseEnvVars(EnvVarsText, out var env))
        {
            return;
        }

        if (MesaGlThreadEnabled)
        {
            env[MesaGlThreadKey] = MesaGlThreadValue;
        }

        IsSaving = true;
        try
        {
            var settings = new InstanceLaunchSettings { ExtraArgs = ParseExtraArgs(ExtraArgsText), Env = env };
            await _instanceService.UpdateLaunchSettingsAsync(_slug, settings, CancellationToken.None).ConfigureAwait(true);
            _toasts.Show(ToastTone.Success, UiText.Toasts.LaunchSettingsSavedTitle);
        }
        finally
        {
            IsSaving = false;
        }
    }

    private bool CanSave() => IsValid && !IsSaving;

    private static string[] ParseExtraArgs(string text)
        => text
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToArray();

    private static bool TryParseEnvVars(string text, out Dictionary<string, string> env)
    {
        var parsed = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            var separatorIndex = line.IndexOf('=');
            var key = separatorIndex > 0 ? line[..separatorIndex].Trim() : string.Empty;
            if (separatorIndex <= 0 || key.Length == 0)
            {
                env = new Dictionary<string, string>(StringComparer.Ordinal);

                return false;
            }

            parsed[key] = line[(separatorIndex + 1)..].Trim();
        }

        env = parsed;

        return true;
    }
}