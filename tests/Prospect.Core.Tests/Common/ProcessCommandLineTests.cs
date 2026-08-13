using System.Text;

using Prospect.Core.Common;
using Prospect.Core.GameVersions;

using Shouldly;

namespace Prospect.Core.Tests.Common;

/// <summary>
/// L'audit de la transmission d'arguments, pour TOUS les appelants d'<see cref="IProcessRunner"/> :
/// installeur Windows, lancement du jeu, <c>dotnet --list-runtimes</c>, ouverture d'une URL ou d'un
/// dossier.
/// </summary>
/// <remarks>
/// <para>
/// Le raisonnement : sur Windows, la BCL COLLE la liste d'arguments en une seule ligne de commande
/// que le programme lancé re-découpe lui-même. Deux découpeurs différents nous concernent, celui du
/// CRT / <c>CommandLineToArgvW</c> (le jeu, <c>dotnet</c>, <c>explorer</c>) et celui de Delphi
/// (l'installeur Inno Setup). Ces tests reconstituent les DEUX et vérifient qu'un chemin
/// d'instance à espaces, apostrophes, accents ou esperluettes revient intact des deux côtés.
/// </para>
/// <para>
/// Sur Linux et macOS, la question ne se pose pas : la liste part telle quelle en <c>argv</c>.
/// C'est d'ailleurs pour ça que ces tests sont du calcul pur et tournent identiquement sur les trois
/// systèmes de la matrice CI.
/// </para>
/// </remarks>
public sealed class ProcessCommandLineTests
{
    /// <summary>
    /// Un chemin par famille de piège, tel qu'un vrai dossier d'instance ou de version peut l'être.
    /// Le guillemet en fait partie : il est INTERDIT dans un chemin Windows, mais parfaitement légal
    /// sous Linux, où un joueur peut nommer son instance comme il veut.
    /// </summary>
    public static TheoryData<string> AwkwardPaths => new()
    {
        @"C:\Users\Leon\AppData\Roaming\Prospect\versions\1.22.6",
        @"C:\Users\Jean Dupont\AppData\Roaming\Prospect\versions\1.22.6",
        @"C:\Users\Léon\Prospect\instances\forêt-d'été\data",
        @"C:\Prospect\instances\rock & roll\data",
        @"C:\Prospect\instances\100% survie\data",
        @"C:\Prospect\instances\a^b`c$d\data",
        @"C:\Prospect\instances\guillemet""interne\data",
        @"C:\Prospect\instances\fin-antislash\",
        @"C:\Prospect\instances\double  espace\data",
        "/home/leon/.local/share/prospect/instances/forêt d'été/data",
    };

    /// <summary>
    /// Les mêmes pièges, restreints à ce qu'un dossier Windows peut réellement s'appeler : le
    /// guillemet en est exclu par le système de fichiers lui-même, et c'est heureux, parce que le
    /// découpeur de Delphi ne saurait pas le rendre (voir
    /// <see cref="InnoSetupParser_CannotCarryALiteralQuote_WhichWindowsPathsForbidAnyway"/>).
    /// </summary>
    public static TheoryData<string> AwkwardWindowsDirectories => new()
    {
        @"C:\Users\Leon\AppData\Roaming\Prospect\versions\1.22.6",
        @"C:\Users\Jean Dupont\AppData\Roaming\Prospect\versions\1.22.6",
        @"C:\Users\Léon\Prospect\versions\1.22.0-rc.1",
        @"C:\Prospect\rock & roll\versions\1.22.6",
        @"C:\Prospect\100% survie\versions\1.22.6",
        @"C:\Prospect\a^b`c$d\versions\1.22.6",
        @"C:\Prospect\fin-antislash\versions\1.22.6\",
        @"C:\Prospect\double  espace\versions\1.22.6",
    };

    /// <summary>
    /// La seule limite connue du découpeur d'Inno Setup, épinglée pour qu'elle ne soit jamais prise
    /// pour un défaut de notre échappement : un guillemet littéral ne lui survit pas. Sans
    /// conséquence, un chemin Windows ne peut pas en contenir — mais le découpeur du CRT, lui, le
    /// rend bien, ce qui compte pour le lancement du jeu sous Linux.
    /// </summary>
    [Fact]
    public void InnoSetupParser_CannotCarryALiteralQuote_WhichWindowsPathsForbidAnyway()
    {
        const string WithQuote = @"/DIR=C:\a""b";

        // L'antislash d'échappement reste littéral et le guillemet disparaît : Delphi ne connaît pas
        // la convention d'échappement du CRT.
        InnoSetupParamParser.Parse(ProcessCommandLine.Render("setup.exe", [WithQuote])).ShouldBe([@"/DIR=C:\a\b"]);
        WindowsArgvParser.Parse(ProcessCommandLine.Render("setup.exe", [WithQuote])).ShouldBe([WithQuote]);
    }

    [Fact]
    public void Render_SimpleArguments_NeedsNoQuoting()
        => ProcessCommandLine.Render("dotnet", ["--list-runtimes"]).ShouldBe("\"dotnet\" --list-runtimes");

    [Fact]
    public void Render_ArgumentWithSpaces_IsQuotedAsAWhole()
        => ProcessCommandLine.RenderArguments([@"/DIR=C:\Program Files\Vintagestory"])
            .ShouldBe(@"""/DIR=C:\Program Files\Vintagestory""");

    [Fact]
    public void Render_TrailingBackslashBeforeTheClosingQuote_IsDoubled()
        => ProcessCommandLine.RenderArguments([@"/DIR=C:\Program Files\jeu\"])
            .ShouldBe(@"""/DIR=C:\Program Files\jeu\\""");

    [Fact]
    public void Render_EmbeddedQuote_IsEscaped()
        => ProcessCommandLine.RenderArguments([@"--nom=a""b"]).ShouldBe(@"""--nom=a\""b""");

    [Fact]
    public void Render_EmptyArgument_BecomesAnEmptyQuotedToken()
        => ProcessCommandLine.RenderArguments(["", "suivant"]).ShouldBe("\"\" suivant");

    [Fact]
    public void Render_ExecutableIsAlwaysQuoted_EvenWithoutSpaces()
        => ProcessCommandLine.Render(@"C:\a\setup.exe", []).ShouldBe(@"""C:\a\setup.exe""");

    [Fact]
    public void Render_ExecutableAlreadyQuoted_IsNotQuotedTwice()
        => ProcessCommandLine.Render(@"""C:\a b\setup.exe""", []).ShouldBe(@"""C:\a b\setup.exe""");

    // ── Aller-retour : ce que le programme lancé récupère vraiment ───────────────────────────────

    /// <summary>
    /// Le contrat qui compte pour le jeu, <c>dotnet</c> et la commande d'ouverture du système :
    /// chaque argument revient comme un jeton unique, quel que soit le chemin.
    /// </summary>
    [Theory]
    [MemberData(nameof(AwkwardPaths))]
    public void RoundTrip_ThroughTheCrtParser_GivesBackEveryArgumentIntact(string path)
    {
        string[] arguments = ["--dataPath=" + path, "--nom", path, "--fin"];

        var recovered = WindowsArgvParser.Parse(ProcessCommandLine.Render("Vintagestory.exe", arguments));

        recovered.ShouldBe(arguments);
    }

    /// <summary>
    /// Le même aller-retour, mais à travers le découpeur de Delphi que l'installeur Inno Setup
    /// utilise (<c>GetParamStr</c>). C'est ce qui prouve que <c>/DIR=</c> arrive ENTIER, y compris
    /// pour un chemin à espaces : Inno retire les guillemets où qu'ils se trouvent dans le jeton,
    /// donc la forme produite ici et la forme <c>/DIR="x:\dirname"</c> de sa documentation lui
    /// donnent rigoureusement la même valeur.
    /// </summary>
    [Theory]
    [MemberData(nameof(AwkwardWindowsDirectories))]
    public void RoundTrip_ThroughTheInnoSetupParser_GivesBackTheDirectoryArgumentIntact(string path)
    {
        var arguments = new List<string>(WindowsGameInstallStrategy.SilentArguments)
        {
            WindowsGameInstallStrategy.BuildDirectoryArgument(path),
        };

        var recovered = InnoSetupParamParser.Parse(ProcessCommandLine.Render(@"C:\cache\vs_install_win-x64_1.22.6.exe", arguments));

        recovered.ShouldBe(arguments);
    }

    [Fact]
    public void Render_NullArguments_ThrowArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() => ProcessCommandLine.Render((ProcessRunRequest)null!));
        Should.Throw<ArgumentNullException>(() => ProcessCommandLine.Render((ProcessStartRequest)null!));
        Should.Throw<ArgumentNullException>(() => ProcessCommandLine.Render(null!, []));
        Should.Throw<ArgumentNullException>(() => ProcessCommandLine.Render("x", null!));
        Should.Throw<ArgumentNullException>(() => ProcessCommandLine.RenderArguments(null!));
    }

    [Fact]
    public void Render_FromARequest_IsTheSameAsFromItsParts()
    {
        var run = new ProcessRunRequest("setup.exe", ["/A", "/B=x y"]);
        var start = new ProcessStartRequest("jeu", ["--dataPath=x y"]);

        ProcessCommandLine.Render(run).ShouldBe(ProcessCommandLine.Render(run.FileName, run.Arguments));
        ProcessCommandLine.Render(start).ShouldBe(ProcessCommandLine.Render(start.FileName, start.Arguments));
    }
}

/// <summary>
/// Le découpeur d'<c>argv</c> de Windows (<c>CommandLineToArgvW</c> / runtime C), reconstitué pour
/// le test. argv[0] suit des règles à part et est ignoré : ces tests portent sur les ARGUMENTS.
/// </summary>
internal static class WindowsArgvParser
{
    public static IReadOnlyList<string> Parse(string commandLine)
    {
        var tokens = new List<string>();
        var index = SkipExecutable(commandLine);
        var current = new StringBuilder();
        var inQuotes = false;
        var started = false;

        while (index < commandLine.Length)
        {
            var character = commandLine[index];

            if (character == '\\')
            {
                var slashes = 0;
                while (index < commandLine.Length && commandLine[index] == '\\')
                {
                    slashes++;
                    index++;
                }

                if (index < commandLine.Length && commandLine[index] == '"')
                {
                    current.Append('\\', slashes / 2);
                    started = true;

                    if (slashes % 2 == 1)
                    {
                        current.Append('"');
                        index++;
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                        index++;
                    }
                }
                else
                {
                    current.Append('\\', slashes);
                    started = true;
                }

                continue;
            }

            if (character == '"')
            {
                started = true;
                index++;

                // Règle post-2008 : deux guillemets dans une zone citée valent un guillemet littéral.
                if (inQuotes && index < commandLine.Length && commandLine[index] == '"')
                {
                    current.Append('"');
                    index++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }

                continue;
            }

            if (!inQuotes && char.IsWhiteSpace(character))
            {
                if (started)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                    started = false;
                }

                index++;

                continue;
            }

            current.Append(character);
            started = true;
            index++;
        }

        if (started)
        {
            tokens.Add(current.ToString());
        }

        return tokens;
    }

    private static int SkipExecutable(string commandLine)
    {
        var index = 0;
        if (index < commandLine.Length && commandLine[index] == '"')
        {
            index++;
            while (index < commandLine.Length && commandLine[index] != '"')
            {
                index++;
            }

            if (index < commandLine.Length)
            {
                index++;
            }
        }
        else
        {
            while (index < commandLine.Length && !char.IsWhiteSpace(commandLine[index]))
            {
                index++;
            }
        }

        return index;
    }
}

/// <summary>
/// Le découpeur de paramètres de Delphi (<c>System.GetParamStr</c>), celui qu'Inno Setup utilise
/// pour lire sa ligne de commande. Sa particularité, et tout l'intérêt du test : le guillemet n'a
/// pas à entourer le jeton entier, il peut apparaître n'importe où et se contente d'ouvrir ou de
/// fermer une zone où les blancs ne coupent pas.
/// </summary>
internal static class InnoSetupParamParser
{
    public static IReadOnlyList<string> Parse(string commandLine)
    {
        var tokens = new List<string>();
        var index = 0;

        // argv[0] : même découpage, mais on n'en garde rien.
        _ = Next(commandLine, ref index);

        while (true)
        {
            var token = Next(commandLine, ref index);
            if (token is null)
            {
                return tokens;
            }

            tokens.Add(token);
        }
    }

    private static string? Next(string commandLine, ref int index)
    {
        while (true)
        {
            while (index < commandLine.Length && commandLine[index] <= ' ')
            {
                index++;
            }

            if (index + 1 < commandLine.Length && commandLine[index] == '"' && commandLine[index + 1] == '"')
            {
                index += 2;

                continue;
            }

            break;
        }

        if (index >= commandLine.Length)
        {
            return null;
        }

        var builder = new StringBuilder();
        while (index < commandLine.Length && commandLine[index] > ' ')
        {
            if (commandLine[index] == '"')
            {
                index++;
                while (index < commandLine.Length && commandLine[index] != '"')
                {
                    builder.Append(commandLine[index]);
                    index++;
                }

                if (index < commandLine.Length)
                {
                    index++;
                }

                continue;
            }

            builder.Append(commandLine[index]);
            index++;
        }

        return builder.ToString();
    }
}