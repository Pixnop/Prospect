namespace Prospect.Core.Runtime;

/// <summary>
/// Parse la sortie texte de <c>dotnet --list-runtimes</c>, une ligne par runtime installé, au
/// format <c>&lt;Framework&gt; &lt;Version&gt; [&lt;Chemin&gt;]</c> (par exemple
/// <c>Microsoft.NETCore.App 8.0.10 [/usr/share/dotnet/shared/Microsoft.NETCore.App]</c>).
/// Fonction pure, testée sur de simples chaînes : une ligne qui ne respecte pas ce format est
/// ignorée plutôt que de faire échouer tout le relevé, cette sortie texte n'étant pas un contrat
/// versionné de la CLI dotnet.
/// </summary>
internal static class DotnetRuntimeListParser
{
    public static IReadOnlyList<DotnetRuntimeInfo> Parse(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return [];
        }

        var runtimes = new List<DotnetRuntimeInfo>();
        foreach (var line in output.Split('\n'))
        {
            if (TryParseLine(line, out var runtime))
            {
                runtimes.Add(runtime);
            }
        }

        return runtimes;
    }

    private static bool TryParseLine(string line, out DotnetRuntimeInfo runtime)
    {
        runtime = null!;

        var trimmed = line.Trim();
        if (trimmed.Length == 0)
        {
            return false;
        }

        var firstSpace = trimmed.IndexOf(' ');
        if (firstSpace < 0)
        {
            return false;
        }

        var name = trimmed[..firstSpace];
        var rest = trimmed[(firstSpace + 1)..].TrimStart();
        var secondSpace = rest.IndexOf(' ');
        var versionText = secondSpace < 0 ? rest : rest[..secondSpace];

        if (name.Length == 0 || !Version.TryParse(versionText, out var version))
        {
            return false;
        }

        runtime = new DotnetRuntimeInfo(name, version);

        return true;
    }
}