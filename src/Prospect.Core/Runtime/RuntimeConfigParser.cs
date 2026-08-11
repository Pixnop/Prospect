using System.Text.Json;

namespace Prospect.Core.Runtime;

/// <summary>
/// Lit <c>runtimeOptions.framework.{name,version}</c> d'un <c>*.runtimeconfig.json</c> (ou le
/// premier élément de <c>runtimeOptions.frameworks</c> pour les rares builds qui en déclarent
/// plusieurs). Fonction pure, sans accès disque : c'est <see cref="IDotnetLocator"/> qui lit le
/// fichier, ce qui permet de tester ce parseur avec de simples chaînes JSON minimalistes plutôt
/// qu'un vrai fichier.
/// </summary>
internal static class RuntimeConfigParser
{
    /// <summary>
    /// Parse le document. Toute forme inattendue (JSON invalide, champ absent, version
    /// illisible) rend <see cref="GameRuntimeRequirement.Unknown"/> plutôt que de lever : un
    /// <c>runtimeconfig.json</c> non standard est une absence normale au même titre qu'un fichier
    /// manquant, pas une erreur pour l'appelant (docs/architecture.md, « repli documenté »).
    /// </summary>
    public static GameRuntimeRequirement Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);

            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("runtimeOptions", out var runtimeOptions)
                || runtimeOptions.ValueKind != JsonValueKind.Object)
            {
                return GameRuntimeRequirement.Unknown;
            }

            if (runtimeOptions.TryGetProperty("framework", out var framework) && TryReadFramework(framework, out var requirement))
            {
                return requirement;
            }

            if (runtimeOptions.TryGetProperty("frameworks", out var frameworks) && frameworks.ValueKind == JsonValueKind.Array)
            {
                foreach (var entry in frameworks.EnumerateArray())
                {
                    if (TryReadFramework(entry, out var fromArray))
                    {
                        return fromArray;
                    }
                }
            }

            return GameRuntimeRequirement.Unknown;
        }
        catch (JsonException)
        {
            return GameRuntimeRequirement.Unknown;
        }
    }

    private static bool TryReadFramework(JsonElement framework, out GameRuntimeRequirement requirement)
    {
        requirement = GameRuntimeRequirement.Unknown;

        if (framework.ValueKind != JsonValueKind.Object
            || !framework.TryGetProperty("name", out var nameElement)
            || nameElement.ValueKind != JsonValueKind.String
            || !framework.TryGetProperty("version", out var versionElement)
            || versionElement.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var name = nameElement.GetString();
        if (string.IsNullOrWhiteSpace(name) || !Version.TryParse(versionElement.GetString(), out var version))
        {
            return false;
        }

        requirement = GameRuntimeRequirement.Known(name, version);

        return true;
    }
}