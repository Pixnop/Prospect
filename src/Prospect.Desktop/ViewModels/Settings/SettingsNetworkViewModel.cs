using System.Diagnostics.CodeAnalysis;

using CommunityToolkit.Mvvm.ComponentModel;

using Prospect.Core.Settings;

namespace Prospect.Desktop.ViewModels.Settings;

/// <summary>
/// Section Réseau de l'écran Réglages : téléchargements simultanés, le seul réglage réseau
/// consommé aujourd'hui (branché sur <c>DownloadManager</c> à sa construction, voir
/// <c>CompositionRoot.AddGameVersions</c>). Le sélecteur ne propose que les choix bornés
/// d'<see cref="DownloadPreferences.AllowedChoices"/> : aucune saisie libre, donc aucune valeur
/// hors bornes ne peut être choisie depuis cette vue (le modèle se protège quand même lui-même,
/// voir <see cref="DownloadPreferences.Clamped"/>, pour un fichier modifié à la main).
/// </summary>
public sealed partial class SettingsNetworkViewModel : ObservableObject
{
    private readonly SettingsService _settings;

    public SettingsNetworkViewModel(SettingsService settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        _settings = settings;
        _maxParallelDownloads = settings.Current.Downloads.MaxParallelDownloads;
    }

    /// <summary>Les seuls choix proposés par le sélecteur (design : « N téléchargements simultanés »).</summary>
    [SuppressMessage(
        "Performance",
        "CA1822:Mark members as static",
        Justification = "Propriété liée en XAML depuis l'instance du DataContext (x:DataType) : rester une " +
            "propriété d'instance est le contrat attendu par le binding, même si la valeur ne dépend d'aucun " +
            "champ aujourd'hui.")]
    public IReadOnlyList<int> AvailableConcurrencyChoices => DownloadPreferences.AllowedChoices;

    [ObservableProperty]
    private int _maxParallelDownloads;

    partial void OnMaxParallelDownloadsChanged(int value)
        => _ = _settings.UpdateAsync(current => current with { Downloads = current.Downloads with { MaxParallelDownloads = value } });
}