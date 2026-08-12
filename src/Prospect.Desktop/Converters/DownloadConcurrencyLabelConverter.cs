using System.Globalization;

using Avalonia.Data.Converters;

using Prospect.Desktop.Resources;

namespace Prospect.Desktop.Converters;

/// <summary>
/// Formate un entier (un des <c>DownloadPreferences.AllowedChoices</c>) en libellé du sélecteur de
/// téléchargements simultanés (section Réseau des Réglages) : <c>ItemTemplate</c> de la
/// <c>ComboBox</c> liée à <c>SettingsNetworkViewModel.AvailableConcurrencyChoices</c>, la valeur
/// choisie elle-même reste un entier nu (<c>SelectedItem</c> lié directement à
/// <c>MaxParallelDownloads</c>, sans détour par ce convertisseur).
/// </summary>
public sealed class DownloadConcurrencyLabelConverter : IValueConverter
{
    public static readonly DownloadConcurrencyLabelConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is int count ? UiText.Settings.ConcurrencyChoiceLabel(count) : value;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException($"{nameof(DownloadConcurrencyLabelConverter)} est à sens unique.");
}