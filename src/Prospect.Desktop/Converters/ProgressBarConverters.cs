using System;
using System.Collections.Generic;
using System.Globalization;

using Avalonia.Data.Converters;

namespace Prospect.Desktop.Converters;

/// <summary>
/// Calcule la largeur du remplissage déterminé de <c>ProgressBar</c> (Value/Minimum/Maximum
/// rapportés à la largeur du conteneur).
///
/// La classe <c>ProgressBar</c> d'Avalonia fait normalement ce calcul elle-même en C#, en
/// réagissant aux changements de taille de son <c>PART_Indicator</c> parent. Ce mécanisme
/// ne se redéclenche pas de façon fiable dans cet environnement (constaté à l'écran : la
/// barre reste vide même à Value non nulle) ; ce convertisseur reproduit le même calcul via
/// une liaison XAML ordinaire, qui se réévalue correctement à chaque changement de valeur ou
/// de taille.
/// </summary>
public sealed class ProgressFillWidthConverter : IMultiValueConverter
{
    public static readonly ProgressFillWidthConverter Instance = new();

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values is not [double value, double minimum, double maximum, double containerWidth, ..])
        {
            return 0d;
        }

        var range = maximum - minimum;
        if (range <= 0 || containerWidth <= 0)
        {
            return 0d;
        }

        var percent = Math.Clamp((value - minimum) / range, 0, 1);
        return containerWidth * percent;
    }
}

/// <summary>
/// Largeur du chunk indéterminé (35 % de la largeur du conteneur, comme
/// <c>components.css</c> : <c>.pr-progress--indeterminate .pr-progress__fill{width:35%}</c>),
/// pour la même raison que <see cref="ProgressFillWidthConverter"/> : recalculée par
/// liaison plutôt que par le mécanisme interne de <c>ProgressBar</c>.
/// </summary>
public sealed class ProgressIndeterminateWidthConverter : IValueConverter
{
    public static readonly ProgressIndeterminateWidthConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is double containerWidth && containerWidth > 0 ? containerWidth * 0.35 : 0d;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}