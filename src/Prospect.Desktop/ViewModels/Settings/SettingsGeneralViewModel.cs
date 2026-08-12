using System.Diagnostics.CodeAnalysis;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Prospect.Core.Settings;

namespace Prospect.Desktop.ViewModels.Settings;

/// <summary>
/// Section Général de l'écran Réglages : choix du thème (persisté, appliqué à chaud par
/// <c>Prospect.Desktop.Services.ThemeService</c> qui écoute <see cref="SettingsService.Changed"/>)
/// et langue de l'UI, verrouillée sur le français tant que l'app n'est pas traduite. Ne touche
/// jamais Avalonia directement : <see cref="SettingsService"/> suffit, ce qui garde ce ViewModel
/// constructible et testable sans application graphique (docs/architecture.md, MVVM strict).
/// </summary>
public sealed partial class SettingsGeneralViewModel : ObservableObject
{
    private readonly SettingsService _settings;

    public SettingsGeneralViewModel(SettingsService settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        _settings = settings;
        _themeChoice = settings.Current.Theme;
    }

    /// <summary>Choix de thème courant, reflété par les boutons radio de la vue.</summary>
    [ObservableProperty]
    private ThemePreference _themeChoice;

    partial void OnThemeChoiceChanged(ThemePreference value) => _ = _settings.UpdateAsync(current => current with { Theme = value });

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

    // Les deux propriétés qui suivent sont liées en XAML depuis l'instance du DataContext
    // (x:DataType) : rester des propriétés d'instance est le contrat attendu par le binding, même
    // si leur valeur ne dépend d'aucun champ tant que l'anglais n'existe pas.
    [SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Voir le commentaire ci-dessus.")]
    public string SelectedLanguageLabel => "Français";

    /// <summary>
    /// Faux tant que l'anglais n'existe pas : pilote la désactivation du sélecteur de langue côté
    /// vue, et sert de garde testable indépendamment de tout rendu (« langue verrouillée »).
    /// </summary>
    [SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Voir le commentaire ci-dessus.")]
    public bool IsLanguageEditable => false;
}