using System.Text;

namespace Prospect.Core.Common;

/// <summary>
/// Rend la LIGNE DE COMMANDE Windows qu'un <see cref="ProcessRunRequest"/> ou un
/// <see cref="ProcessStartRequest"/> produira réellement, telle que la BCL l'assemble à partir de
/// <c>ProcessStartInfo.ArgumentList</c>.
/// </summary>
/// <remarks>
/// <para>
/// Deux usages, et un seul mécanisme pour les deux. D'abord la JOURNALISATION : quand une
/// installation atterrit ailleurs que prévu sur la machine d'un utilisateur, la seule pièce qui
/// permette d'arbitrer est la ligne exacte qui a été lancée, pas la liste d'arguments telle que le
/// code la pensait. Ensuite la PREUVE : cette classe est du calcul pur, donc testable depuis
/// n'importe quel OS, ce qui permet d'épingler le traitement des chemins à espaces, guillemets et
/// antislashs sans dépendre d'un vrai processus Windows.
/// </para>
/// <para>
/// Les règles reproduites sont celles de <c>System.Diagnostics.PasteArguments</c>, elles-mêmes
/// celles du parseur d'argv de Windows (<c>CommandLineToArgvW</c>) : un jeton n'est mis entre
/// guillemets que s'il est vide ou s'il contient un blanc ou un guillemet ; les antislashs qui
/// précèdent un guillemet sont doublés ; le guillemet littéral est échappé par un antislash. Le
/// nom de l'exécutable, lui, est TOUJOURS entouré de guillemets (règle spéciale d'argv[0], où les
/// antislashs ne sont jamais des échappements).
/// </para>
/// <para>
/// Sur Linux et macOS, aucune ligne de commande n'est assemblée : la liste d'arguments est passée
/// telle quelle en <c>argv</c> au moment du <c>fork/exec</c>, donc rien n'est à échapper et un
/// chemin à espaces ne pose aucune question. Ce rendu y reste néanmoins utile comme trace lisible,
/// et c'est à ce titre seul qu'il est journalisé sur ces systèmes.
/// </para>
/// </remarks>
public static class ProcessCommandLine
{
    private const char Quote = '"';
    private const char Backslash = '\\';

    /// <summary>Ligne complète (exécutable puis arguments) d'un processus attendu.</summary>
    public static string Render(ProcessRunRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return Render(request.FileName, request.Arguments);
    }

    /// <summary>Ligne complète (exécutable puis arguments) d'un processus suivi.</summary>
    public static string Render(ProcessStartRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return Render(request.FileName, request.Arguments);
    }

    /// <summary>Ligne complète : <paramref name="fileName"/> entre guillemets, puis les arguments échappés.</summary>
    public static string Render(string fileName, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        ArgumentNullException.ThrowIfNull(arguments);

        var builder = new StringBuilder();
        AppendExecutable(builder, fileName.Trim());

        foreach (var argument in arguments)
        {
            builder.Append(' ');
            AppendArgument(builder, argument);
        }

        return builder.ToString();
    }

    /// <summary>Les seuls arguments, sans l'exécutable : ce que reçoit le programme lancé.</summary>
    public static string RenderArguments(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var builder = new StringBuilder();
        foreach (var argument in arguments)
        {
            if (builder.Length != 0)
            {
                builder.Append(' ');
            }

            AppendArgument(builder, argument);
        }

        return builder.ToString();
    }

    // argv[0] a ses propres règles : le parseur s'y arrête au guillemet fermant sans jamais traiter
    // l'antislash comme un échappement, donc un chemin Windows y entre tel quel entre guillemets.
    private static void AppendExecutable(StringBuilder builder, string fileName)
    {
        var alreadyQuoted = fileName.Length > 1 && fileName[0] == Quote && fileName[^1] == Quote;
        if (alreadyQuoted)
        {
            builder.Append(fileName);

            return;
        }

        builder.Append(Quote).Append(fileName).Append(Quote);
    }

    private static void AppendArgument(StringBuilder builder, string argument)
    {
        if (argument.Length != 0 && ContainsNoWhitespaceOrQuote(argument))
        {
            builder.Append(argument);

            return;
        }

        builder.Append(Quote);

        var index = 0;
        while (index < argument.Length)
        {
            var current = argument[index++];

            if (current == Backslash)
            {
                var runLength = 1;
                while (index < argument.Length && argument[index] == Backslash)
                {
                    index++;
                    runLength++;
                }

                if (index == argument.Length)
                {
                    // Un guillemet fermant va suivre : les antislashs doivent être doublés pour ne
                    // pas l'échapper. C'est le piège du chemin qui se termine par un séparateur.
                    builder.Append(Backslash, runLength * 2);
                }
                else if (argument[index] == Quote)
                {
                    builder.Append(Backslash, (runLength * 2) + 1).Append(Quote);
                    index++;
                }
                else
                {
                    builder.Append(Backslash, runLength);
                }

                continue;
            }

            if (current == Quote)
            {
                builder.Append(Backslash).Append(Quote);

                continue;
            }

            builder.Append(current);
        }

        builder.Append(Quote);
    }

    private static bool ContainsNoWhitespaceOrQuote(string value)
    {
        foreach (var character in value)
        {
            if (char.IsWhiteSpace(character) || character == Quote)
            {
                return false;
            }
        }

        return true;
    }
}