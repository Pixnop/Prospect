using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Prospect.Core.Backups;
using Prospect.Core.Common;
using Prospect.Core.Instances;
using Prospect.Core.Storage;
using Prospect.Desktop.Resources;
using Prospect.Desktop.Services;
using Prospect.Desktop.ViewModels.Toasts;

namespace Prospect.Desktop.ViewModels.Instance;

/// <summary>
/// Onglet Options de la page de détail : édition des arguments de lancement et des variables
/// d'environnement propres à une instance (sauvegardés via
/// <see cref="InstanceService.UpdateLaunchSettingsAsync"/>), plus le bloc Sauvegardes
/// (<see cref="Backups"/>). Écart assumé par rapport à la maquette : celle-ci n'a pas d'onglet
/// dédié aux sauvegardes, Options est la maison naturelle des réglages d'instance, exactement
/// comme les arguments de lancement déjà présents ici.
/// </summary>
/// <remarks>
/// Deux choix d'édition documentés ici. Les arguments supplémentaires (<see cref="ExtraArgsText"/>)
/// sont saisis un par ligne plutôt qu'en une seule chaîne façon ligne de commande : tokeniser une
/// chaîne façon shell (espaces, guillemets) réintroduirait exactement l'ambiguïté que
/// docs/research/vslauncher-et-distribution.md reproche à VS Launcher (implication 9), alors
/// qu'une ligne par argument correspond une fois pour toutes à un élément de la liste
/// <see cref="InstanceLaunchSettings.ExtraArgs"/>, sans règle de découpage à documenter ni à
/// maintenir. Les variables d'environnement (<see cref="EnvVarsText"/>) suivent le même principe,
/// au format <c>CLE=valeur</c> par ligne. <c>mesa_glthread</c> (confort Linux mentionné par la
/// recherche) a sa propre case à cocher plutôt que de vivre dans le texte libre : c'est la seule
/// variable que Prospect connaît par son nom, une case dédiée évite à l'utilisateur de retenir sa
/// syntaxe exacte. Elle est stockée en champ TYPÉ de <see cref="InstanceLaunchSettings"/> et posée
/// par la seule stratégie de lancement Linux : rangée dans les variables libres, elle suivrait
/// l'instance sur les systèmes où elle n'a aucun effet.
/// </remarks>
public sealed partial class InstanceOptionsTabViewModel : ObservableObject
{
    private readonly string _slug;
    private readonly InstanceService _instanceService;
    private readonly IToastService _toasts;

    public InstanceOptionsTabViewModel(
        string slug,
        string instanceName,
        InstanceLaunchSettings launchSettings,
        InstanceBackupSettings backupSettings,
        InstanceService instanceService,
        InstanceBackupService backupService,
        IAppEnvironment appEnvironment,
        IOverlayService overlay,
        IClock clock,
        IToastService toasts)
    {
        ArgumentException.ThrowIfNullOrEmpty(slug);
        ArgumentException.ThrowIfNullOrEmpty(instanceName);
        ArgumentNullException.ThrowIfNull(launchSettings);
        ArgumentNullException.ThrowIfNull(backupSettings);
        ArgumentNullException.ThrowIfNull(instanceService);
        ArgumentNullException.ThrowIfNull(backupService);
        ArgumentNullException.ThrowIfNull(appEnvironment);
        ArgumentNullException.ThrowIfNull(overlay);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(toasts);

        _slug = slug;
        _instanceService = instanceService;
        _toasts = toasts;
        ShowMesaGlThreadToggle = appEnvironment.CurrentOperatingSystem == AppOperatingSystem.Linux;

        _extraArgsText = string.Join(Environment.NewLine, launchSettings.ExtraArgs);
        _envVarsText = string.Join(Environment.NewLine, launchSettings.Env.Select(pair => $"{pair.Key}={pair.Value}"));
        _mesaGlThreadEnabled = launchSettings.MesaGlThread;

        Backups = new InstanceBackupsSectionViewModel(slug, instanceName, backupSettings, instanceService, backupService, overlay, clock, toasts);
    }

    /// <summary>Vrai uniquement sur Linux : <c>mesa_glthread</c> n'a d'effet que sur les pilotes Mesa de cette plateforme.</summary>
    public bool ShowMesaGlThreadToggle { get; }

    /// <summary>Bloc Sauvegardes de l'onglet Options : toggle auto-avant-lancement, rétention, liste, création/restauration/suppression.</summary>
    public InstanceBackupsSectionViewModel Backups { get; }

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

        IsSaving = true;
        try
        {
            var settings = new InstanceLaunchSettings
            {
                ExtraArgs = ParseExtraArgs(ExtraArgsText),
                Env = env,
                MesaGlThread = MesaGlThreadEnabled,
            };
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