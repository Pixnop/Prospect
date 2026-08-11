using System.Globalization;

namespace Prospect.Desktop.Formatting;

/// <summary>
/// Met en forme les tailles et les débits, pour les valeurs machine affichées en monospace
/// (design/readme.md, « Content fundamentals »).
/// </summary>
/// <remarks>
/// Unités et séparateur décimal repris tels quels du catalogue officiel et des maquettes
/// (« 590.5 MB », « 4.2 MB/s », « 6.0 GB ») : la chaîne <c>filesize</c> du catalogue est affichée
/// à côté de tailles que nous calculons nous-mêmes, et les voir écrites dans deux conventions
/// différentes sur la même ligne serait déroutant. D'où la culture invariante, y compris dans une
/// interface en français.
/// </remarks>
public static class ByteSizeFormatter
{
    private const long Kilobyte = 1000L;
    private const long Megabyte = Kilobyte * 1000L;
    private const long Gigabyte = Megabyte * 1000L;

    /// <summary>Taille lisible, par exemple <c>590.5 MB</c>.</summary>
    public static string Format(long bytes)
    {
        if (bytes < 0)
        {
            return "0 B";
        }

        return bytes switch
        {
            < Kilobyte => string.Create(CultureInfo.InvariantCulture, $"{bytes} B"),
            < Megabyte => string.Create(CultureInfo.InvariantCulture, $"{bytes / (double)Kilobyte:0.0} KB"),
            < Gigabyte => string.Create(CultureInfo.InvariantCulture, $"{bytes / (double)Megabyte:0.0} MB"),
            _ => string.Create(CultureInfo.InvariantCulture, $"{bytes / (double)Gigabyte:0.00} GB"),
        };
    }

    /// <summary>Débit lisible, par exemple <c>4.2 MB/s</c>. Vide tant qu'aucun débit n'est mesuré.</summary>
    public static string FormatSpeed(double bytesPerSecond)
        => bytesPerSecond <= 0d ? string.Empty : $"{Format((long)bytesPerSecond)}/s";

    /// <summary>
    /// Avancement lisible, par exemple <c>230.1 MB / 590.5 MB</c>. Quand la taille totale est
    /// inconnue, seuls les octets reçus sont affichés plutôt qu'un total inventé.
    /// </summary>
    public static string FormatProgress(long receivedBytes, long? totalBytes)
        => totalBytes is > 0 ? $"{Format(receivedBytes)} / {Format(totalBytes.Value)}" : Format(receivedBytes);
}