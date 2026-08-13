using System.Reflection;

namespace Prospect.Desktop.Tests.TestDoubles;

/// <summary>
/// Le HTML RÉEL d'une fiche ModDB, embarqué depuis la même source que la suite du Core (voir
/// l'<c>EmbeddedResource</c> lié dans le csproj).
/// </summary>
/// <remarks>
/// Il sert à confronter le RENDU à ce que les auteurs publient vraiment, et pas à ce qu'on
/// imagine : 29 Ko, 154 paragraphes, 256 entrées de liste imbriquées jusqu'à trois niveaux, 226
/// sauts de ligne, 62 titres, 34 liens et 4 images hébergées hors du CDN du ModDB. La fiche de
/// deux paragraphes du faux serveur ne tend aucune boîte ; celle-ci les tend toutes.
/// </remarks>
internal static class RealModDbSamples
{
    /// <summary>Le champ <c>text</c> de <c>https://mods.vintagestory.at/api/mod/carryon</c>, relevé le 2026-08-13.</summary>
    public static string CarryOnDescriptionHtml { get; } = Read("carryon-description.html");

    /// <summary>
    /// La fiche complète de Carry On au format de <c>/api/mod/{id}</c>, avec sa description réelle
    /// et deux releases compatibles pour exercer le sélecteur de version.
    /// </summary>
    /// <param name="gameVersion">Version de jeu taguée sur les deux releases.</param>
    public static string CarryOnDetailJson(string gameVersion = "1.21.3")
    {
        // Le HTML est réinjecté tel quel dans du JSON : il porte des guillemets et des retours à
        // la ligne, donc il doit être échappé comme le ferait le serveur.
        var text = System.Text.Json.JsonSerializer.Serialize(CarryOnDescriptionHtml);

        return $$"""
        {
          "statuscode": "200",
          "mod": {
            "modid": 890, "assetid": 4405, "name": "Carry On", "text": {{text}},
            "author": "NerdScurvy", "urlalias": "carryon",
            "logofile": "https://moddbcdn.vintagestory.at/CarryOnLogo.png",
            "homepageurl": "", "sourcecodeurl": "https://github.com/NerdScurvy/CarryOn",
            "issuetrackerurl": "https://github.com/NerdScurvy/CarryOn/issues", "wikiurl": "",
            "downloads": 2841233, "follows": 12044, "comments": 640,
            "side": "both", "type": "mod", "tags": ["Utility", "QoL"],
            "created": "2019-03-02 10:00:00", "lastreleased": "2026-08-07 10:22:05",
            "releases": [
              { "releaseid": 52086, "fileid": 113014,
                "mainfile": "https://moddbcdn.vintagestory.at/carryon_1.14.3.zip",
                "filename": "CarryOn-1.22.0_v1.14.3.zip", "downloads": 6729,
                "tags": ["{{gameVersion}}"], "modidstr": "carryon", "modversion": "1.14.3",
                "changelog": "<ul><li>Block trunks and collapsed chests from being attached to boats.</li><li>Fixed <strong>containers</strong> losing their storage slots.</li></ul>",
                "created": "2026-08-07 10:22:05" },
              { "releaseid": 51002, "fileid": 111900,
                "mainfile": "https://moddbcdn.vintagestory.at/carryon_1.14.2.zip",
                "filename": "CarryOn-1.22.0_v1.14.2.zip", "downloads": 40118,
                "tags": ["{{gameVersion}}"], "modidstr": "carryon", "modversion": "1.14.2",
                "changelog": null, "created": "2026-06-19 08:11:00" }
            ]
          }
        }
        """;
    }

    private static string Read(string name)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resource = assembly.GetManifestResourceNames().Single(candidate => candidate.EndsWith(name, StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resource)!;
        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    }
}