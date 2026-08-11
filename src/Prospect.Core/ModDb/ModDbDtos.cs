using System.Text.Json.Serialization;

namespace Prospect.Core.ModDb;

/// <summary>
/// Enveloppe commune aux réponses de l'API v1 (docs/research/moddb-api.md, section « Gestion
/// d'erreurs ») : <c>fail()</c> côté serveur n'appelle jamais <c>http_response_code()</c>, donc un
/// 404 applicatif arrive avec un vrai <c>HTTP 200</c> et le seul indicateur d'échec est ce champ.
/// Il est déclaré en <see langword="string"/> parce que le serveur l'encode toujours en chaîne
/// (<c>"200"</c>, <c>"404"</c>, <c>"410"</c>), jamais en nombre nu.
/// </summary>
internal interface IModDbV1Envelope
{
    /// <summary>Code applicatif sous forme de chaîne, ou <see langword="null"/> si le corps n'en portait pas.</summary>
    string? StatusCode { get; }
}

/// <summary>Réponse de <c>GET /api/mods</c> : le catalogue entier, sans pagination.</summary>
internal sealed record ModDbModListResponseDto : IModDbV1Envelope
{
    [JsonPropertyName("statuscode")]
    public string? StatusCode { get; init; }

    [JsonPropertyName("mods")]
    public List<ModDbModSummaryDto>? Mods { get; init; }
}

/// <summary>
/// Une entrée de <c>GET /api/mods</c>. DTO volontairement tout-nullable : la recherche a mesuré
/// sur le dump complet que <c>logo</c> manque sur 32 % des mods et <c>urlalias</c> sur 40 %, que
/// <c>modidstrs</c> peut être vide ou porter plusieurs identifiants, et que <c>tags</c> vaut
/// parfois <c>[""]</c> (artefact d'un <c>GROUP_CONCAT</c> nul) au lieu de <c>[]</c>.
/// </summary>
internal sealed record ModDbModSummaryDto
{
    [JsonPropertyName("modid")]
    public int ModId { get; init; }

    [JsonPropertyName("assetid")]
    public int AssetId { get; init; }

    [JsonPropertyName("downloads")]
    public int Downloads { get; init; }

    [JsonPropertyName("follows")]
    public int Follows { get; init; }

    [JsonPropertyName("trendingpoints")]
    public int TrendingPoints { get; init; }

    [JsonPropertyName("comments")]
    public int Comments { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("summary")]
    public string? Summary { get; init; }

    [JsonPropertyName("modidstrs")]
    public List<string>? ModIdStrings { get; init; }

    [JsonPropertyName("author")]
    public string? Author { get; init; }

    [JsonPropertyName("urlalias")]
    public string? UrlAlias { get; init; }

    /// <summary>Vocabulaire de la fiche web (<c>client</c>/<c>server</c>/<c>both</c>), pas celui du modinfo.json.</summary>
    [JsonPropertyName("side")]
    public string? Side { get; init; }

    [JsonPropertyName("type")]
    public string? Type { get; init; }

    [JsonPropertyName("logo")]
    public string? Logo { get; init; }

    [JsonPropertyName("tags")]
    public List<string>? Tags { get; init; }

    /// <summary>Date SQL <c>"YYYY-MM-DD HH:MM:SS"</c> en v1, jamais un timestamp Unix.</summary>
    [JsonPropertyName("lastreleased")]
    public string? LastReleased { get; init; }
}

/// <summary>Réponse de <c>GET /api/mod/{id}</c>.</summary>
internal sealed record ModDbModDetailResponseDto : IModDbV1Envelope
{
    [JsonPropertyName("statuscode")]
    public string? StatusCode { get; init; }

    [JsonPropertyName("mod")]
    public ModDbModDetailDto? Mod { get; init; }
}

/// <summary>
/// Fiche complète d'un mod. <c>logofilename</c> est ignoré volontairement : le code du site le
/// marque lui-même <c>@obsolete</c> avec le commentaire « this is not the filename, but just the
/// link again ».
/// </summary>
internal sealed record ModDbModDetailDto
{
    [JsonPropertyName("modid")]
    public int ModId { get; init; }

    [JsonPropertyName("assetid")]
    public int AssetId { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    /// <summary>Description longue, en HTML brut d'éditeur WYSIWYG.</summary>
    [JsonPropertyName("text")]
    public string? Text { get; init; }

    [JsonPropertyName("author")]
    public string? Author { get; init; }

    [JsonPropertyName("urlalias")]
    public string? UrlAlias { get; init; }

    [JsonPropertyName("logofile")]
    public string? LogoFile { get; init; }

    [JsonPropertyName("homepageurl")]
    public string? HomepageUrl { get; init; }

    [JsonPropertyName("sourcecodeurl")]
    public string? SourceCodeUrl { get; init; }

    [JsonPropertyName("issuetrackerurl")]
    public string? IssueTrackerUrl { get; init; }

    [JsonPropertyName("wikiurl")]
    public string? WikiUrl { get; init; }

    [JsonPropertyName("downloads")]
    public int Downloads { get; init; }

    [JsonPropertyName("follows")]
    public int Follows { get; init; }

    [JsonPropertyName("comments")]
    public int Comments { get; init; }

    [JsonPropertyName("side")]
    public string? Side { get; init; }

    [JsonPropertyName("type")]
    public string? Type { get; init; }

    [JsonPropertyName("created")]
    public string? Created { get; init; }

    [JsonPropertyName("lastreleased")]
    public string? LastReleased { get; init; }

    [JsonPropertyName("tags")]
    public List<string>? Tags { get; init; }

    [JsonPropertyName("releases")]
    public List<ModDbReleaseDto>? Releases { get; init; }
}

/// <summary>
/// Une release telle qu'exposée par v1 (<c>mod.releases[i]</c> et <c>updates[key]</c>, même forme
/// au <c>changelog</c> près). <c>mainfile</c> est déjà l'URL CDN finale, contrairement au
/// <c>fileUrl</c> relatif de v2.
/// </summary>
internal sealed record ModDbReleaseDto
{
    [JsonPropertyName("releaseid")]
    public int ReleaseId { get; init; }

    [JsonPropertyName("fileid")]
    public int FileId { get; init; }

    [JsonPropertyName("mainfile")]
    public string? MainFile { get; init; }

    [JsonPropertyName("filename")]
    public string? FileName { get; init; }

    [JsonPropertyName("downloads")]
    public int Downloads { get; init; }

    /// <summary>Versions de jeu compatibles : des chaînes exactes, jamais de plage ni de wildcard.</summary>
    [JsonPropertyName("tags")]
    public List<string>? Tags { get; init; }

    [JsonPropertyName("modidstr")]
    public string? ModIdString { get; init; }

    [JsonPropertyName("modversion")]
    public string? ModVersion { get; init; }

    [JsonPropertyName("changelog")]
    public string? Changelog { get; init; }

    [JsonPropertyName("created")]
    public string? Created { get; init; }
}

/// <summary>
/// Réponse de <c>GET /api/updates</c>. La clé du dictionnaire est le <c>modidstr</c> envoyé dans la
/// requête, et n'apparaît QUE pour les mods réellement en retard : un mod à jour est simplement
/// absent, ce qui ne distingue pas « à jour » de « modidstr inconnu du ModDB » (docs/research/moddb-api.md,
/// section « GET /api/updates »). Réutilise <see cref="ModDbReleaseDto"/> : même forme que
/// <c>mod.releases[i]</c>, changelog en moins.
/// </summary>
internal sealed record ModDbUpdatesResponseDto : IModDbV1Envelope
{
    [JsonPropertyName("statuscode")]
    public string? StatusCode { get; init; }

    [JsonPropertyName("updates")]
    public Dictionary<string, ModDbReleaseDto>? Updates { get; init; }
}

/// <summary>Réponse de <c>GET /api/tags</c>.</summary>
internal sealed record ModDbTagListResponseDto : IModDbV1Envelope
{
    [JsonPropertyName("statuscode")]
    public string? StatusCode { get; init; }

    [JsonPropertyName("tags")]
    public List<ModDbTagDto>? Tags { get; init; }
}

/// <summary>
/// Un tag de catégorie. <c>tagid</c> est ici une CHAÎNE JSON (le <c>SELECT</c> du site ne passe pas
/// par <c>intval()</c> sur cet endpoint), à l'opposé exact du <c>tagid</c> numérique de
/// <c>/api/gameversions</c> : les deux ne se modélisent pas avec le même type.
/// </summary>
internal sealed record ModDbTagDto
{
    [JsonPropertyName("tagid")]
    public string? TagId { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("color")]
    public string? Color { get; init; }
}

/// <summary>
/// Réponse de <c>GET /api/v2/mods/{modId}/releases/latest</c>. Deux champs ne se comportent pas
/// comme leurs équivalents v1 : <c>created</c> est un timestamp Unix numérique et <c>fileUrl</c>
/// est un chemin relatif à préfixer, qui répond en 302 vers le CDN.
/// </summary>
internal sealed record ModDbV2ReleaseDto
{
    [JsonPropertyName("releaseId")]
    public int ReleaseId { get; init; }

    [JsonPropertyName("identifier")]
    public string? Identifier { get; init; }

    [JsonPropertyName("version")]
    public string? Version { get; init; }

    [JsonPropertyName("compatibleGameVersions")]
    public List<string>? CompatibleGameVersions { get; init; }

    [JsonPropertyName("created")]
    public long Created { get; init; }

    [JsonPropertyName("fileName")]
    public string? FileName { get; init; }

    [JsonPropertyName("fileUrl")]
    public string? FileUrl { get; init; }
}

/// <summary>
/// Réponse de <c>GET /api/v2/mods/install-information</c>. Les clés <c>resolved</c> et
/// <c>warnings</c> n'apparaissent qu'avec <c>resolve-deps=true</c> ; la recherche n'a pas obtenu
/// d'échantillon live où elles étaient peuplées, elles sont donc modélisées de façon tolérante et
/// leur absence n'est jamais une erreur : la vérification locale du modinfo.json téléchargé reste
/// le filet de sécurité (voir <see cref="ModDependencyResolver"/>).
/// </summary>
internal sealed record ModDbV2InstallInformationResponseDto
{
    [JsonPropertyName("data")]
    public Dictionary<string, ModDbV2InstallInformationEntryDto>? Data { get; init; }

    [JsonPropertyName("resolved")]
    public Dictionary<string, ModDbV2InstallInformationEntryDto>? Resolved { get; init; }

    [JsonPropertyName("warnings")]
    public List<string>? Warnings { get; init; }
}

/// <summary>Une entrée d'<c>install-information</c>, par identifiant de mod.</summary>
internal sealed record ModDbV2InstallInformationEntryDto
{
    [JsonPropertyName("recommendedUpgrade")]
    public string? RecommendedUpgrade { get; init; }

    [JsonPropertyName("version")]
    public string? Version { get; init; }

    [JsonPropertyName("fileName")]
    public string? FileName { get; init; }

    [JsonPropertyName("fileUrl")]
    public string? FileUrl { get; init; }

    /// <summary>Code d'erreur par entrée (4001 spec illisible, 4041 introuvable...), 0 si tout va bien.</summary>
    [JsonPropertyName("errorCode")]
    public int ErrorCode { get; init; }
}

/// <summary>
/// Document de cache disque du catalogue et des tags. Les DTOs y sont réécrits tels quels plutôt
/// que la réponse brute : le catalogue pèse 3,5 Mo, l'encapsuler dans une chaîne JSON échappée le
/// gonflerait encore et imposerait un double parsing à chaque lecture.
/// </summary>
internal sealed record ModDbCacheDocument
{
    /// <summary>Version de schéma courante du cache : un document d'une autre version est ignoré.</summary>
    public const int CurrentSchemaVersion = 1;

    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; init; }

    [JsonPropertyName("retrievedUtc")]
    public DateTimeOffset RetrievedUtc { get; init; }

    [JsonPropertyName("mods")]
    public List<ModDbModSummaryDto>? Mods { get; init; }

    [JsonPropertyName("tags")]
    public List<ModDbTagDto>? Tags { get; init; }
}