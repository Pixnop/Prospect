using System.Globalization;

using Avalonia.Data.Converters;

using Prospect.Desktop.Resources;

namespace Prospect.Desktop.Converters;

/// <summary>
/// Formate un entier (un des <c>InstanceBackupSettings.AllowedKeepCounts</c>) en libellé du
/// sélecteur de rétention du bloc Sauvegardes : <c>ItemTemplate</c> de la <c>ComboBox</c> liée à
/// <c>InstanceBackupsSectionViewModel.AllowedKeepCounts</c>, la valeur choisie elle-même reste un
/// entier nu (<c>SelectedItem</c> lié directement à <c>KeepCount</c>, sans détour par ce
/// convertisseur). Même principe que <see cref="DownloadConcurrencyLabelConverter"/>.
/// </summary>
public sealed class BackupKeepCountLabelConverter : IValueConverter
{
    public static readonly BackupKeepCountLabelConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is int count ? UiText.Instance.Backups.KeepCountChoiceLabel(count) : value;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException($"{nameof(BackupKeepCountLabelConverter)} est à sens unique.");
}