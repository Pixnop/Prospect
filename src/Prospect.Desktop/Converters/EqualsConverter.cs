using System.Globalization;

using Avalonia.Data.Converters;

namespace Prospect.Desktop.Converters;

/// <summary>
/// Compare la représentation texte de la valeur liée à celle du paramètre du convertisseur,
/// sans respect de la casse. Sert à activer une classe AXAML (<c>Classes.stable="{Binding
/// Tone, Converter={x:Static conv:EqualsConverter.Instance}, ConverterParameter=stable}"</c>)
/// à partir d'une simple propriété texte exposée par un ViewModel : la façon la plus directe
/// de piloter un badge à plusieurs teintes (voir <see cref="Prospect.Desktop.Formatting.ChannelBadgePresentation"/>)
/// depuis un ViewModel qui reste constructible sans UI.
/// </summary>
public sealed class EqualsConverter : IValueConverter
{
    public static readonly EqualsConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.OrdinalIgnoreCase);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException($"{nameof(EqualsConverter)} est à sens unique.");
}