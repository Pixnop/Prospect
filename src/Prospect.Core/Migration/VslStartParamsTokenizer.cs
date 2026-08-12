using System.Text;

namespace Prospect.Core.Migration;

/// <summary>
/// Tokenise <see cref="VslInstallation.StartParams"/> — une chaîne unique, jamais une liste, le
/// défaut de tokenisation documenté par la recherche (docs/research/vslauncher-et-distribution.md,
/// section d et implication 9 : VS Launcher passe cette chaîne telle quelle comme UN SEUL élément
/// d'<c>argv</c>) — en une vraie liste pour <see cref="Prospect.Core.Instances.InstanceLaunchSettings.ExtraArgs"/>.
/// </summary>
/// <remarks>
/// Grammaire façon shell minimale : les espaces séparent les tokens, des guillemets simples ou
/// doubles protègent un segment contenant des espaces (les paramètres de démarrage du jeu
/// acceptent des valeurs de ce type, voir wiki.vintagestory.at/Client_startup_parameters, par
/// exemple <c>-someflag "valeur avec espaces"</c>). Un guillemet resté ouvert en fin de chaîne est
/// toléré : tout ce qui suit devient simplement le dernier token plutôt que de faire échouer toute
/// la conversion — cohérent avec la tolérance attendue de ce domaine face à une chaîne qu'un
/// utilisateur a pu saisir ou éditer à la main dans VS Launcher.
/// </remarks>
public static class VslStartParamsTokenizer
{
    /// <summary>Tokenise <paramref name="startParams"/>. Chaîne vide ou blanche : liste vide.</summary>
    public static IReadOnlyList<string> Tokenize(string? startParams)
    {
        if (string.IsNullOrWhiteSpace(startParams))
        {
            return [];
        }

        var tokens = new List<string>();
        var current = new StringBuilder();
        var hasCurrentToken = false;
        char? activeQuote = null;

        foreach (var c in startParams)
        {
            if (activeQuote is { } quote)
            {
                if (c == quote)
                {
                    activeQuote = null;
                }
                else
                {
                    current.Append(c);
                }

                continue;
            }

            if (c is '"' or '\'')
            {
                // Un guillemet ouvre (ou referme, cas ci-dessus) une protection, mais ne fait pas
                // partie du token lui-même : c'est ce qui permet à -flag "" de rester un token
                // présent mais vide plutôt que d'être avalé par le test de blanc plus bas.
                activeQuote = c;
                hasCurrentToken = true;
                continue;
            }

            if (char.IsWhiteSpace(c))
            {
                if (hasCurrentToken)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                    hasCurrentToken = false;
                }

                continue;
            }

            current.Append(c);
            hasCurrentToken = true;
        }

        if (hasCurrentToken)
        {
            tokens.Add(current.ToString());
        }

        return tokens;
    }
}