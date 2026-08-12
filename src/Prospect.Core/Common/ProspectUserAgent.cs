using System.Reflection;

namespace Prospect.Core.Common;

/// <summary>
/// En-tête <c>User-Agent</c> commun à tous les clients HTTP du Core, du type
/// <c>Prospect/0.1.0-dev</c> : un client desktop qui parle à un service public doit au minimum se
/// nommer. Vit dans <c>Common</c> parce que deux domaines sans lien entre eux en ont besoin (ModDB
/// et le compte Vintage Story) et que la dépendance de l'un vers l'autre serait absurde
/// (docs/architecture.md, sens unique des dépendances entre domaines).
/// </summary>
public static class ProspectUserAgent
{
    /// <summary>Valeur envoyée sur chaque requête sortante.</summary>
    public static string Value { get; } = $"Prospect/{ResolveVersion()}";

    private static string ResolveVersion()
    {
        var informational = typeof(ProspectUserAgent).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        if (string.IsNullOrWhiteSpace(informational))
        {
            return typeof(ProspectUserAgent).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        }

        // La version informationnelle porte souvent un « +<sha> » ajouté par le SDK : inutile
        // dans un User-Agent, et inutilement bavard sur la machine de build.
        var plus = informational.IndexOf('+', StringComparison.Ordinal);

        return plus < 0 ? informational : informational[..plus];
    }
}