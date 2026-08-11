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
            Name = dto.Name,
            Author = dto.Author ?? string.Empty,
            DescriptionHtml = dto.Text ?? string.Empty,
            LogoUrl = ToAbsoluteUri(dto.LogoFile),
            Side = ParseSide(dto.Side),
            Tags = CleanStrings(dto.Tags),
            Downloads = dto.Downloads,
            PageUrl = BuildPageUrl(dto.ModId),
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

    /// <summary>Convertit les tags de catégorie exploitables.</summary>
    public static IReadOnlyList<ModDbTag> ToTags(IEnumerable<ModDbTagDto>? dtos)
        => dtos is null
            ? []
            : dtos
                .Where(dto => !string.IsNullOrWhiteSpace(dto.TagId) && !string.IsNullOrWhiteSpace(dto.Name))
                .Select(dto => new ModDbTag(dto.TagId!, dto.Name!))
                .ToArray();

    /// <summary>Page publique d'un mod, construite depuis son identifiant numérique (jamais depuis un <c>urlalias</c> deviné).</summary>
    public static Uri BuildPageUrl(int modId) => new(SiteBaseUrl, $"show/mod/{modId.ToString(CultureInfo.InvariantCulture)}");

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