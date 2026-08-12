namespace Prospect.Core.Migration;

/// <summary>
/// Parse <see cref="VslInstallation.EnvVars"/> — une chaîne unique <c>CLE=valeur,CLE2=valeur2</c>,
/// jamais un dictionnaire côté VS Launcher — en paires clé/valeur exploitables, sur le même
/// séparateur que VS Launcher lui-même (<c>envVars.split(",")</c>, <c>gameHandlers.ts</c>,
/// <c>EXECUTE_GAME</c>).
/// </summary>
/// <remarks>
/// Amélioration délibérée par rapport à VS Launcher plutôt qu'un simple portage : son
/// <c>entry.trim().split("=")</c> découpe sur TOUS les signes égal puis ne garde que les deux
/// premiers segments de la déstructuration, perdant silencieusement tout ce qui suit un second
/// "=" dans la valeur (<c>"KEY=a=b"</c> devient <c>KEY=a</c>, le "=b" final disparaît sans
/// avertissement). Prospect découpe sur le PREMIER "=" seulement : la valeur peut donc elle-même
/// contenir des "=" sans perte. Une entrée sans "=" ou à clé vide est ignorée silencieusement
/// (tolérance attendue de ce domaine), jamais bloquante pour les entrées voisines.
/// </remarks>
public static class VslEnvVarsParser
{
    /// <summary>Parse <paramref name="envVars"/>. Chaîne vide ou blanche : dictionnaire vide.</summary>
    public static IReadOnlyDictionary<string, string> Parse(string? envVars)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(envVars))
        {
            return result;
        }

        foreach (var rawEntry in envVars.Split(','))
        {
            var entry = rawEntry.Trim();
            if (entry.Length == 0)
            {
                continue;
            }

            var separatorIndex = entry.IndexOf('=');
            if (separatorIndex <= 0)
            {
                // Pas de "=", ou "=" en toute première position (clé vide) : entrée inexploitable,
                // ignorée plutôt que de faire échouer les variables voisines.
                continue;
            }

            var key = entry[..separatorIndex].Trim();
            if (key.Length == 0)
            {
                continue;
            }

            result[key] = entry[(separatorIndex + 1)..].Trim();
        }

        return result;
    }
}