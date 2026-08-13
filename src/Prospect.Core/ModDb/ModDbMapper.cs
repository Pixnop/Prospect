using System.Globalization;

using Prospect.Core.Common;

namespace Prospect.Core.ModDb;

/// <summary>
/// Convertit les DTOs bruts de l'API en modèles de domaine. C'est ici que se concentrent toutes
/// les tolérances relevées par la recherche : champs nuls, tableaux à chaîne vide, dates dans deux
/// formats selon la génération d'endpoint, versions parfois illisibles. Une entrée inexploitable
/// (pas de nom, pas de version, pas de fichier) est écartée plutôt que de faire échouer la
/// conversion de tout le document : sur 7 994 mods, une seule anomalie ne doit pas vider l'écran.
/// </summary>
internal static class ModDbMapper
{
    /// <summary>Base publique du site, préfixe des chemins relatifs de v2 et des pages de fiche.</summary>
    public static readonly Uri SiteBaseUrl = new("https://mods.vintagestory.at/");

    /// <summary>Convertit les entrées de catalogue exploitables, dans l'ordre reçu.</summary>
    public static IReadOnlyList<ModDbModSummary> ToSummaries(IEnumerable<ModDbModSummaryDto>? dtos)
        => dtos is null ? [] : dtos.Select(ToSummary).OfType<ModDbModSummary>().ToArray();

    /// <summary>Convertit une entrée de catalogue, ou <see langword="null"/> si elle est inexploitable.</summary>
    public static ModDbModSummary? ToSummary(ModDbModSummaryDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        if (dto.ModId <= 0 || string.IsNullOrWhiteSpace(dto.Name))
        {
            return null;
        }

        return new ModDbModSummary
        {
            ModId = dto.ModId,
            AssetId = dto.AssetId,
            Name = dto.Name,
            Summary = dto.Summary ?? string.Empty,
            Author = dto.Author ?? string.Empty,
            ModIdStrings = CleanStrings(dto.ModIdStrings),
            LogoUrl = ToAbsoluteUri(dto.Logo),
            Side = ParseSide(dto.Side),
            Type = dto.Type ?? string.Empty,
            Tags = CleanStrings(dto.Tags),
            Downloads = dto.Downloads,
            Follows = dto.Follows,
            TrendingPoints = dto.TrendingPoints,
            LastReleasedUtc = ParseSqlDate(dto.LastReleased),
            PageUrl = BuildPageUrl(dto.AssetId, dto.UrlAlias),
        };
    }

    /// <summary>Convertit une fiche complète, releases triées de la plus récente à la plus ancienne.</summary>
    public static ModDbModDetail? ToDetail(ModDbModDetailDto? dto)
    {
        if (dto is null || dto.ModId <= 0 || string.IsNullOrWhiteSpace(dto.Name))
        {
            return null;
        }

        var releases = (dto.Releases ?? [])
            .Select(ToRelease)
            .OfType<ModDbRelease>()
            .OrderByDescending(release => release.Version)
            .ToArray();

        return new ModDbModDetail
        {
            ModId = dto.ModId,
            AssetId = dto.AssetId,
            Name = dto.Name,
            Author = dto.Author ?? string.Empty,
            DescriptionHtml = dto.Text ?? string.Empty,
            LogoUrl = ToAbsoluteUri(dto.LogoFile),
            Side = ParseSide(dto.Side),
            Tags = CleanStrings(dto.Tags),
            Downloads = dto.Downloads,
            Follows = dto.Follows,
            PageUrl = BuildPageUrl(dto.AssetId, dto.UrlAlias),
            HomepageUrl = ToExternalUri(dto.HomepageUrl),
            SourceCodeUrl = ToExternalUri(dto.SourceCodeUrl),
            IssueTrackerUrl = ToExternalUri(dto.IssueTrackerUrl),
            WikiUrl = ToExternalUri(dto.WikiUrl),
            Releases = releases,
        };
    }

    /// <summary>
    /// Convertit une release, ou <see langword="null"/> s'il manque de quoi la télécharger ou la
    /// comparer (identifiant, version lisible, URL de fichier).
    /// </summary>
    public static ModDbRelease? ToRelease(ModDbReleaseDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        if (string.IsNullOrWhiteSpace(dto.ModIdString)
            || !ModVersion.TryParse(dto.ModVersion, out var version)
            || ToAbsoluteUri(dto.MainFile) is not { } downloadUrl)
        {
            return null;
        }

        var tags = CleanStrings(dto.Tags);

        return new ModDbRelease
        {
            ReleaseId = dto.ReleaseId,
            FileId = dto.FileId,
            ModIdString = dto.ModIdString,
            Version = version,
            FileName = string.IsNullOrWhiteSpace(dto.FileName) ? $"{dto.ModIdString}-{version}.zip" : dto.FileName,
            DownloadUrl = downloadUrl,
            CompatibleGameVersions = ParseGameVersions(tags),
            CompatibleGameVersionTags = tags,
            CreatedUtc = ParseSqlDate(dto.Created),
            Downloads = dto.Downloads,
            Changelog = dto.Changelog,
        };
    }

    /// <summary>Convertit la réponse v2 <c>releases/latest</c>, dont le contrat diffère de v1 sur deux champs.</summary>
    public static ModDbRelease? ToRelease(ModDbV2ReleaseDto dto, int fileId)
    {
        ArgumentNullException.ThrowIfNull(dto);

        if (string.IsNullOrWhiteSpace(dto.Identifier)
            || !ModVersion.TryParse(dto.Version, out var version)
            || ToAbsoluteUri(dto.FileUrl) is not { } downloadUrl)
        {
            return null;
        }

        var tags = CleanStrings(dto.CompatibleGameVersions);

        return new ModDbRelease
        {
            ReleaseId = dto.ReleaseId,
            FileId = fileId,
            ModIdString = dto.Identifier,
            Version = version,
            FileName = string.IsNullOrWhiteSpace(dto.FileName) ? $"{dto.Identifier}-{version}.zip" : dto.FileName,
            DownloadUrl = downloadUrl,
            CompatibleGameVersions = ParseGameVersions(tags),
            CompatibleGameVersionTags = tags,
            CreatedUtc = dto.Created > 0 ? DateTimeOffset.FromUnixTimeSeconds(dto.Created) : null,
        };
    }

    /// <summary>
    /// Convertit la réponse de <c>/api/updates</c> : une release par <c>modidstr</c> réellement en
    /// retard. La clé retenue est celle du dictionnaire reçu (le <c>modidstr</c> envoyé dans la
    /// requête), pas <see cref="ModDbReleaseDto.ModIdString"/> : c'est elle que le client doit
    /// comparer aux clés envoyées pour ne pas confondre « à jour » et « modidstr inconnu ».
    /// </summary>
    public static IReadOnlyDictionary<string, ModDbRelease> ToUpdates(IReadOnlyDictionary<string, ModDbReleaseDto>? dtos)
    {
        var result = new Dictionary<string, ModDbRelease>(StringComparer.OrdinalIgnoreCase);
        if (dtos is null)
        {
            return result;
        }

        foreach (var (modIdString, dto) in dtos)
        {
            if (string.IsNullOrWhiteSpace(modIdString) || ToRelease(dto) is not { } release)
            {
                continue;
            }

            result[modIdString] = release;
        }

        return result;
    }

    /// <summary>Convertit les tags de catégorie exploitables.</summary>
    public static IReadOnlyList<ModDbTag> ToTags(IEnumerable<ModDbTagDto>? dtos)
        => dtos is null
            ? []
            : dtos
                .Where(dto => !string.IsNullOrWhiteSpace(dto.TagId) && !string.IsNullOrWhiteSpace(dto.Name))
                .Select(dto => new ModDbTag(dto.TagId!, dto.Name!))
                .ToArray();

    /// <summary>
    /// Page publique d'un mod. Deux routes existent côté site, et le <c>modid</c> n'en ouvre
    /// AUCUNE des deux : <c>show/mod/{N}</c> attend l'<c>assetid</c>, pas l'identifiant de mod.
    /// </summary>
    /// <param name="assetId">
    /// Identifiant d'asset de la fiche, celui que route <c>show/mod/{N}</c>. Les deux espaces
    /// d'identifiants divergent pour de vrai : Carry On porte <c>modid</c> 890 et <c>assetid</c>
    /// 4405, CarryOnLib <c>modid</c> 4687 et <c>assetid</c> 27960. Construire la page depuis le
    /// <c>modid</c>, comme le faisait cette méthode, ouvrait donc la fiche d'un AUTRE mod.
    /// </param>
    /// <param name="urlAlias">
    /// Alias d'URL choisi par l'auteur, quand il en a défini un (absent sur 40 % du catalogue).
    /// C'est la route la plus lisible (<c>mods.vintagestory.at/carryon</c>) et celle que le site
    /// lui-même met en avant ; elle est donc préférée quand elle existe.
    /// </param>
    /// <returns>
    /// L'alias s'il y en a un, sinon <c>show/mod/{assetid}</c>. La racine du site en dernier
    /// recours, quand la fiche n'a ni alias ni <c>assetid</c> exploitable : mieux vaut un lien
    /// inutile qu'un lien qui ouvre la fiche de quelqu'un d'autre.
    /// </returns>
    public static Uri BuildPageUrl(int assetId, string? urlAlias)
    {
        var alias = urlAlias?.Trim();
        if (!string.IsNullOrEmpty(alias))
        {
            // L'alias vient du serveur mais reste une chaîne saisie par un auteur : l'échapper
            // empêche qu'un '/' ou un '?' malencontreux compose une tout autre URL.
            return new Uri(SiteBaseUrl, Uri.EscapeDataString(alias));
        }

        return assetId > 0
            ? new Uri(SiteBaseUrl, $"show/mod/{assetId.ToString(CultureInfo.InvariantCulture)}")
            : SiteBaseUrl;
    }

    /// <summary>
    /// Traduit le vocabulaire de la fiche web. <c>both</c> et jamais <c>universal</c> : c'est un
    /// <c>ENUM('client','server','both')</c> en base, à ne pas confondre avec le vocabulaire du
    /// modinfo.json.
    /// </summary>
    public static ModDbSide ParseSide(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "client" => ModDbSide.Client,
        "server" => ModDbSide.Server,
        "both" => ModDbSide.Both,
        _ => ModDbSide.Unknown,
    };

    /// <summary>
    /// Retire les chaînes vides d'un tableau de l'API. Nécessaire des deux côtés : <c>/api/mods</c>
    /// rend <c>[""]</c> quand un mod n'a aucun tag, là où <c>/api/mod/{id}</c> rend <c>[]</c> pour
    /// exactement le même cas.
    /// </summary>
    public static IReadOnlyList<string> CleanStrings(IEnumerable<string?>? values)
        => values is null
            ? []
            : values.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value!.Trim()).ToArray();

    /// <summary>Lit une date SQL <c>"YYYY-MM-DD HH:MM:SS"</c> de v1, et rien d'autre.</summary>
    public static DateTimeOffset? ParseSqlDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateTime.TryParseExact(
            value.Trim(),
            "yyyy-MM-dd HH:mm:ss",
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var parsed)
            ? new DateTimeOffset(parsed, TimeSpan.Zero)
            : null;
    }

    private static List<GameVersion> ParseGameVersions(IEnumerable<string> tags)
    {
        var versions = new List<GameVersion>();
        foreach (var tag in tags)
        {
            if (GameVersion.TryParse(tag, out var version))
            {
                versions.Add(version);
            }
        }

        return versions;
    }

    /// <summary>
    /// Lien externe déclaré sur la fiche (site, code source, tickets, wiki). Contrairement aux URL
    /// de fichier, un chemin relatif n'a AUCUN sens ici : ce sont des adresses saisies à la main
    /// par l'auteur, et les préfixer par le site du ModDB fabriquerait un lien inventé. Tout ce
    /// qui n'est pas une URL <c>http</c>/<c>https</c> absolue est donc écarté.
    /// </summary>
    public static Uri? ToExternalUri(string? value)
        => string.IsNullOrWhiteSpace(value) || !Uri.TryCreate(value.Trim(), UriKind.Absolute, out var absolute)
            ? null
            : absolute.Scheme is "http" or "https" ? absolute : null;

    // Le mainfile de v1 est déjà absolu ; le fileUrl de v2 est relatif au site et répond en 302
    // vers cette même URL CDN, d'où le préfixage plutôt qu'un rejet.
    private static Uri? ToAbsoluteUri(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (Uri.TryCreate(value, UriKind.Absolute, out var absolute))
        {
            return absolute.Scheme is "http" or "https" ? absolute : null;
        }

        return Uri.TryCreate(SiteBaseUrl, value, out var combined) ? combined : null;
    }
}