using System.Diagnostics.CodeAnalysis;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Prospect.Core.Settings;

namespace Prospect.Desktop.ViewModels.Settings;

/// <summary>
/// Section Général de l'écran Réglages : choix du thème (persisté, appliqué à chaud par
/// <c>Prospect.Desktop.Services.ThemeService</c> qui écoute <see cref="SettingsService.Changed"/>)
/// et langue de l'UI (persistée, appliquée au PROCHAIN démarrage, voir
/// <c>Prospect.Desktop.Services.LanguageService</c> et la mention « prend effet au redémarrage »
/// affichée sous le sélecteur). Ne touche jamais Avalonia directement :
/// <see cref="SettingsService"/> suffit, ce qui garde ce ViewModel constructible et testable sans
/// application graphique (docs/architecture.md, MVVM strict).
/// </summary>
public sealed partial class SettingsGeneralViewModel : ObservableObject
{
    private readonly SettingsService _settings;

    public SettingsGeneralViewModel(SettingsService settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        _settings = settings;
        _themeChoice = settings.Current.Theme;
        _selectedLanguageIndex = IndexOf(settings.Current.Language);
    }

    /// <summary>Choix de thème courant, reflété par les boutons radio de la vue.</summary>
    [ObservableProperty]
    private ThemePreference _themeChoice;

    /// <summary>
    /// Rang du choix dans le sélecteur de langue : 0 pour le français, 1 pour l'anglais, dans
    /// l'ordre exact des <c>ComboBoxItem</c> de la vue.
    /// </summary>
    /// <remarks>
    /// Un index plutôt qu'une valeur typée liée à <c>SelectedItem</c> : les deux entrées de la
    /// liste sont des <c>ComboBoxItem</c> dont le contenu vient du dictionnaire de textes
    /// (<c>Settings_LanguageFrench</c>/<c>Settings_LanguageEnglish</c>), donc lier
    /// <c>SelectedIndex</c> évite d'avoir à reconstruire une valeur depuis un objet de vue —
    /// même raison que <see cref="SelectThemeCommand"/> pour les boutons radio du thème.
    /// </remarks>
    [ObservableProperty]
    private int _selectedLanguageIndex;

    /// <summary>Langue actuellement choisie dans le sélecteur, telle qu'elle sera persistée.</summary>
    public string SelectedLanguage => LanguageAt(SelectedLanguageIndex);

    /// <summary>
    /// Le sélecteur de langue est modifiable depuis que l'anglais existe. La propriété reste (elle
    /// pilote <c>IsEnabled</c> côté vue et sert de garde testable) mais ne verrouille plus rien.
    /// </summary>
    [SuppressMessage(
        "Performance",
        "CA1822:Mark members as static",
        Justification = "Propriété liée en XAML depuis l'instance du DataContext (x:DataType) : rester une " +
            "propriété d'instance est le contrat attendu par le binding, même si la valeur ne dépend d'aucun " +
            "champ aujourd'hui.")]
    public bool IsLanguageEditable => true;

    partial void OnThemeChoiceChanged(ThemePreference value) => _ = _settings.UpdateAsync(current => current with { Theme = value });

    // La langue ne s'applique PAS à chaud (voir LanguageService) : on persiste, et la vue affiche
    // sous le sélecteur que le changement prend effet au redémarrage.
    partial void OnSelectedLanguageIndexChanged(int value)
    {
        OnPropertyChanged(nameof(SelectedLanguage));

        var language = LanguageAt(value);

        _ = _settings.UpdateAsync(current => current with { Language = language });
    }

    /// <summary>
    /// Sélectionne un thème (bouton radio cliqué). Une commande plutôt qu'un binding
    /// <c>IsChecked</c> bidirectionnel direct : <c>RadioButton.IsChecked</c> ne peut pas se lier en
    /// double sens à une seule propriété <see cref="ThemePreference"/> partagée par trois boutons
    /// sans un convertisseur qui saurait reconstruire la valeur choisie depuis un simple booléen —
    /// le même choix que <c>InstanceDetailView</c> fait pour ses onglets
    /// (<c>SelectTabCommand</c>/<c>CommandParameter</c>).
    /// </summary>
    [RelayCommand]
    private void SelectTheme(ThemePreference choice) => ThemeChoice = choice;

    private static int IndexOf(string? language)
        => ProspectSettings.NormalizeLanguage(language) == ProspectSettings.English ? 1 : 0;

    private static string LanguageAt(int index) => index == 1 ? ProspectSettings.English : ProspectSettings.French;
}