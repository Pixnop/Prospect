namespace Prospect.Desktop.Formatting;

/// <summary>
/// Met en forme le temps de jeu cumulé (<c>InstanceMetadata.TotalPlaytimeSeconds</c>) en fragment
/// court pour la ligne meta de la page de détail (design : « joué 128 h »), même esprit que
/// <see cref="ByteSizeFormatter"/> et <see cref="RelativeDateFormatter"/> : classe statique pure,
/// testable par assertions exactes.
/// </summary>
public static class PlaytimeFormatter
{
    /// <summary>Fragment court, par exemple <c>joué 128 h</c>. Jamais joué rend <c>jamais joué</c>.</summary>
    public static string Format(long totalSeconds) => totalSeconds switch
    {
        <= 0 => "jamais joué",
        < 3600 => "joué < 1 h",
        _ => $"joué {totalSeconds / 3600} h",
    };
}