using Avalonia;
using Avalonia.Styling;

using Prospect.Core.Settings;

namespace Prospect.Desktop.Services;

/// <summary>
/// Traduit le <see cref="ThemePreference"/> persisté par <see cref="SettingsService"/> en
/// <see cref="ThemeVariant"/> Avalonia appliqué à <see cref="Application.RequestedThemeVariant"/> —
/// le seul point du Desktop à toucher ce type, exactement comme <see cref="Prospect.Core.Common.ExternalUrlOpener"/>
/// est le seul point du Core à lancer un processus. <see cref="ThemePreference.System"/> délègue à
/// l'OS via <c>Application.PlatformSettings</c> (docs/architecture.md, section Réglages) plutôt que
/// de dupliquer une détection.
/// </summary>
/// <remarks>
/// Point de conception qui mérite une ligne : le constructeur ne mute JAMAIS
/// <see cref="Application.RequestedThemeVariant"/> lui-même, seulement <see cref="ApplyStartupTheme"/>
/// et les deux abonnements passifs (changement de réglage, changement de préférence OS) le font.
/// <see cref="ThemeService"/> est un singleton résolu par <see cref="Prospect.Desktop.CompositionRoot"/>
/// transitivement dès qu'un test construit <c>MainWindow</c>/<c>ShellViewModel</c> (qui traverse
/// <c>SettingsViewModel</c>) : si le simple fait de le CONSTRUIRE changeait déjà le thème appliqué,
/// des scénarios de test qui posent délibérément une variante avant de résoudre la fenêtre
/// (<c>Prospect.Desktop.Tests.Support.ResponsiveScenario.ShowWindow</c>) se la feraient écraser par
/// la valeur par défaut dès la résolution du conteneur — un ordre d'appel accidentel plutôt qu'un
/// choix. <see cref="ApplyStartupTheme"/> est donc un appel EXPLICITE, réservé à
/// <c>App.OnFrameworkInitializationCompleted</c> (et au test dédié du cycle réglage → application),
/// jamais déclenché comme effet de bord de la construction du graphe DI.
/// </remarks>
public sealed class ThemeService
{
    private readonly SettingsService _settings;
    private readonly Application _application;

    public ThemeService(SettingsService settings, Application application)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(application);

        _settings = settings;
        _application = application;

        // Abonnements passifs : ils arment un futur callback, ils ne mutent rien tout de suite
        // (voir la remarque de la classe).
        _settings.Changed += (_, current) => Apply(current.Theme);

        if (_application.PlatformSettings is { } platformSettings)
        {
            platformSettings.ColorValuesChanged += (_, _) => ApplyIfFollowingSystem();
        }
    }

    /// <summary>
    /// Applique le thème actuellement en vigueur (<see cref="SettingsService.Current"/>). À appeler
    /// une seule fois, explicitement, après <see cref="SettingsService.LoadAsync"/> et avant la
    /// construction de la première fenêtre (voir la remarque de la classe).
    /// </summary>
    public void ApplyStartupTheme() => Apply(_settings.Current.Theme);

    // Un changement de préférence système ne doit rejouer l'application que si l'utilisateur suit
    // effectivement le système ; sinon un thème Clair/Sombre choisi explicitement se ferait
    // basculer par un évènement qui ne le concerne pas.
    private void ApplyIfFollowingSystem()
    {
        if (_settings.Current.Theme == ThemePreference.System)
        {
            Apply(ThemePreference.System);
        }
    }

    private void Apply(ThemePreference preference)
    {
        _application.RequestedThemeVariant = preference switch
        {
            ThemePreference.Light => ThemeVariant.Light,
            ThemePreference.Dark => ThemeVariant.Dark,
            _ => ResolveSystemVariant(),
        };
    }

    // Repli sur Sombre si la plateforme n'expose aucun PlatformSettings (jamais vu en pratique, y
    // compris en tête headless — voir ThemeServiceTests — mais IPlatformSettings est nullable dans
    // le contrat d'Avalonia, donc un repli explicite plutôt qu'un NullReferenceException.
    private ThemeVariant ResolveSystemVariant()
        => _application.PlatformSettings is { } platformSettings
            ? (ThemeVariant)platformSettings.GetColorValues().ThemeVariant
            : ThemeVariant.Dark;
}