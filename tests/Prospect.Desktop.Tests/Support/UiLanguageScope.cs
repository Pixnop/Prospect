using Avalonia;

using Microsoft.Extensions.DependencyInjection;

using Prospect.Core.Settings;
using Prospect.Desktop.Resources;
using Prospect.Desktop.Services;

namespace Prospect.Desktop.Tests.Support;

/// <summary>
/// Bascule l'application headless en anglais le temps d'un test, puis rétablit le français quoi
/// qu'il arrive.
/// </summary>
/// <remarks>
/// La langue est un état global du processus, des deux côtés : le dictionnaire fusionné dans
/// <see cref="Application.Resources"/> et la table de <see cref="UiText"/>. Le même dispositif
/// existe déjà pour le thème (<c>Application.RequestedThemeVariant</c>, reposé sur Sombre par les
/// tests de variante claire) ; ce scope le rend simplement impossible à oublier. Les tests
/// <c>[AvaloniaFact]</c> s'exécutent l'un après l'autre sur le fil du dispatcher headless, donc
/// aucun autre test ne lit la langue pendant qu'elle est basculée.
/// </remarks>
internal sealed class UiLanguageScope : IDisposable
{
    private UiLanguageScope()
    {
    }

    /// <summary>
    /// Pose <paramref name="language"/> comme le ferait un vrai démarrage, réglage déjà lu :
    /// dictionnaire fusionné puis table fixée. À utiliser quand le test n'a pas de conteneur, ou
    /// pas besoin de traverser <see cref="LanguageService"/>.
    /// </summary>
    public static UiLanguageScope Use(string language)
    {
        LanguageService.MergeStringsDictionary(Application.Current!, language);
        UiText.ResetForTests(language);

        return new UiLanguageScope();
    }

    /// <summary>
    /// Démarre l'application dans la langue déjà persistée, par le VRAI chemin de démarrage
    /// (<see cref="LanguageService.ApplyStartupLanguage"/>), à appeler après
    /// <see cref="SettingsService.LoadAsync"/> et AVANT de résoudre la moindre fenêtre.
    /// </summary>
    public static UiLanguageScope ApplyStartupLanguage(ServiceProvider provider)
    {
        provider.GetRequiredService<LanguageService>().ApplyStartupLanguage();

        return new UiLanguageScope();
    }

    public void Dispose()
    {
        LanguageService.MergeStringsDictionary(Application.Current!, ProspectSettings.French);
        UiText.ResetForTests(ProspectSettings.French);
    }
}