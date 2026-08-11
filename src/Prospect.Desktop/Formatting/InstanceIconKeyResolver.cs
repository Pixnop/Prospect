namespace Prospect.Desktop.Formatting;

/// <summary>
/// Résout <c>InstanceMetadata.Icon</c> (<c>builtin:&lt;nom&gt;</c> ou <c>file:...</c>) vers la clé
/// consommée par <see cref="Prospect.Desktop.Converters.IconKeyToGeometryConverter"/>. Partagé
/// entre la carte d'Accueil et la page de détail : les deux surfaces affichent la même icône pour
/// la même instance, ce doit être le même calcul.
/// </summary>
public static class InstanceIconKeyResolver
{
    private const string BuiltinPrefix = "builtin:";
    private const string FallbackIconKey = "layers";

    /// <summary>Clé d'icône, ou <see cref="FallbackIconKey"/> pour tout ce qui n'est pas une icône intégrée reconnue (dont les icônes <c>file:</c>, pas encore prises en charge).</summary>
    public static string Resolve(string icon)
    {
        ArgumentNullException.ThrowIfNull(icon);

        if (!icon.StartsWith(BuiltinPrefix, StringComparison.Ordinal))
        {
            return FallbackIconKey;
        }

        var key = icon[BuiltinPrefix.Length..];

        return key is "" or "default" ? FallbackIconKey : key;
    }
}