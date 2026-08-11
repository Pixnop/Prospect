namespace Prospect.Desktop.ViewModels.Common;

/// <summary>
/// Page « bientôt disponible » sobre (Mods, Versions, Réglages : voir docs/architecture.md,
/// décision de navigation) : un simple <c>EmptyState</c>, sans logique. Un ViewModel à trois
/// champs figés suffit, aucune propriété observable n'est nécessaire.
/// </summary>
public sealed class PlaceholderPageViewModel(string iconKey, string title, string description)
{
    /// <summary>Clé résolue par <see cref="Prospect.Desktop.Converters.IconKeyToGeometryConverter"/>.</summary>
    public string IconKey { get; } = iconKey;

    public string Title { get; } = title;

    public string Description { get; } = description;
}