using Avalonia;
using Avalonia.Markup.Xaml.Styling;

using Prospect.Core.Settings;
using Prospect.Desktop.Resources;

namespace Prospect.Desktop.Services;

/// <summary>
/// Traduit la langue persistée par <see cref="SettingsService"/> en un dictionnaire de textes
/// effectivement fusionné dans <see cref="Application.Resources"/>, et fixe la table de
/// <see cref="UiText"/> qui va avec. Pendant symétrique de <see cref="ThemeService"/> pour les
/// deux moitiés du texte de l'interface (XAML d'un côté, C# de l'autre).
/// </summary>
/// <remarks>
/// <para>
/// Décision d'architecture : la langue s'applique AU DÉMARRAGE, pas à chaud (docs/architecture.md,
/// section « Langue de l'interface »). Rien n'écoute donc <see cref="SettingsService.Changed"/>
/// ici, à la différence de <see cref="ThemeService"/> : changer la langue dans les Réglages
/// persiste le choix, et c'est l'ouverture suivante qui l'affiche. Un basculement à chaud
/// demanderait que chaque texte déjà rendu soit une ressource dynamique ET que tout texte calculé
/// par un ViewModel soit renotifié, pour un réglage qu'on change une fois dans la vie de
/// l'installation.
/// </para>
/// <para>
/// Comme <see cref="ThemeService.ApplyStartupTheme"/>, <see cref="ApplyStartupLanguage"/> est un
/// appel EXPLICITE et jamais un effet de bord de la construction du graphe DI : le français est
/// déjà fusionné par <c>App.Initialize</c> (valeur de démarrage sûre, voir sa remarque), donc
/// construire ce service ne doit rien changer à ce qui est en place.
/// </para>
/// </remarks>
public sealed class LanguageService
{
    /// <summary>Dictionnaire français, celui qu'<c>App.Initialize</c> fusionne comme valeur de démarrage.</summary>
    public const string FrenchDictionaryUri = "avares://Prospect.Desktop/Resources/Strings.fr.axaml";

    /// <summary>Dictionnaire anglais.</summary>
    public const string EnglishDictionaryUri = "avares://Prospect.Desktop/Resources/Strings.en.axaml";

    private readonly SettingsService _settings;
    private readonly Application _application;

    public LanguageService(SettingsService settings, Application application)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(application);

        _settings = settings;
        _application = application;
    }

    /// <summary>
    /// URI du dictionnaire correspondant à <paramref name="language"/>. Toute valeur inconnue rend
    /// le dictionnaire français, même repli que <see cref="ProspectSettings.NormalizeLanguage"/>.
    /// </summary>
    public static string DictionaryUriFor(string? language)
        => ProspectSettings.NormalizeLanguage(language) == ProspectSettings.English
            ? EnglishDictionaryUri
            : FrenchDictionaryUri;

    /// <summary>
    /// Remplace le dictionnaire de textes fusionné dans <paramref name="application"/> par celui de
    /// <paramref name="language"/>. Sans effet sur <see cref="UiText"/> : cette moitié-là se fixe
    /// une seule fois (voir <see cref="ApplyStartupLanguage"/>), alors que le dictionnaire, lui,
    /// est posé une première fois par <c>App.Initialize</c> avant que la langue réglée ne soit
    /// connue.
    /// </summary>
    public static void MergeStringsDictionary(Application application, string? language)
    {
        ArgumentNullException.ThrowIfNull(application);

        var merged = application.Resources.MergedDictionaries;

        for (var index = merged.Count - 1; index >= 0; index--)
        {
            if (merged[index] is ResourceInclude include && IsStringsDictionary(include))
            {
                merged.RemoveAt(index);
            }
        }

        var uri = new Uri(DictionaryUriFor(language));
        merged.Add(new ResourceInclude(uri) { Source = uri });
    }

    /// <summary>
    /// Applique la langue actuellement réglée (<see cref="SettingsService.Current"/>). À appeler une
    /// seule fois, après <see cref="SettingsService.LoadAsync"/> et AVANT la construction de la
    /// première fenêtre : les <c>{StaticResource}</c> des vues se résolvent à leur construction, et
    /// <see cref="UiText"/> refuse d'être fixé deux fois.
    /// </summary>
    public void ApplyStartupLanguage()
    {
        var language = _settings.Current.Language;

        MergeStringsDictionary(_application, language);
        UiText.Fix(language);
    }

    private static bool IsStringsDictionary(ResourceInclude include)
        => include.Source is { } source
            && (source.OriginalString == FrenchDictionaryUri || source.OriginalString == EnglishDictionaryUri);
}