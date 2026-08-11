using System.Globalization;

using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Styling;

namespace Prospect.Desktop.Converters;

/// <summary>
/// Résout une clé d'icône texte (ex. <c>"layers"</c>, exposée par un ViewModel) vers la
/// <see cref="Avalonia.Media.StreamGeometry"/> correspondante de <c>Styles/Icons.axaml</c>
/// (ressource <c>Icon.&lt;clé&gt;</c>). Existe pour que les ViewModels (Sidebar, EmptyState,
/// choix d'icône du wizard...) exposent une simple chaîne plutôt qu'un type Avalonia, ce qui
/// les garde constructibles sans UI : seule cette conversion, exécutée par le binding, touche
/// au système de ressources.
/// </summary>
public sealed class IconKeyToGeometryConverter : IValueConverter
{
    public static readonly IconKeyToGeometryConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string key || string.IsNullOrEmpty(key))
        {
            return null;
        }

        return Application.Current?.TryGetResource($"Icon.{key}", ThemeVariant.Default, out var resource) == true
            ? resource
            : null;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException($"{nameof(IconKeyToGeometryConverter)} est à sens unique.");
}