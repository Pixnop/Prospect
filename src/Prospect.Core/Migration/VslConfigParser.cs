using System.Text.Json;

using Prospect.Core.Storage;

namespace Prospect.Core.Migration;

/// <summary>
/// Convertit le contenu brut d'un <c>config.json</c> de VS Launcher en modèle du domaine
/// (docs/research/vslauncher-et-distribution.md, section f). Pur calcul, sans effet de bord :
/// reçoit une chaîne déjà lue par l'appelant (<see cref="VslDetector"/>), jamais un chemin.
/// </summary>
/// <remarks>
/// Deux niveaux de tolérance, à l'image du reste du Core face à un fichier qui n'est pas le
/// nôtre. Un document qui n'est structurellement pas un objet JSON exploitable (JSON invalide, ou
/// racine qui n'est pas un objet) fait échouer tout le parsing avec une
/// <see cref="CorruptedFileException"/>, à charge pour <see cref="VslDetector"/> de la traduire en
/// état de détection plutôt que de la laisser remonter. En revanche, une entrée individuelle de
/// <c>installations[]</c> ou <c>gameVersions[]</c> dont un champ a un type inattendu (JSON écrit ou
/// édité à la main) ne fait échouer QUE cette entrée : elle est reportée dans
/// <see cref="VslConfigParseResult.Issues"/> et les entrées suivantes continuent d'être traitées,
/// exactement comme le reste du Core traite une instance ou une installation de jeu cassée
/// individuellement sans faire échouer tout le scan.
/// </remarks>
public static class VslConfigParser
{
    private const string InstallationsProperty = "installations";
    private const string GameVersionsProperty = "gameVersions";

    /// <summary>
    /// Parse <paramref name="json"/>.
    /// </summary>
    /// <param name="json">Contenu du fichier <c>config.json</c>.</param>
    /// <param name="sourcePath">Chemin du fichier, pour un message d'erreur précis en cas d'échec.</param>
    /// <exception cref="CorruptedFileException">
    /// <paramref name="json"/> n'est pas un document JSON exploitable, ou sa racine n'est pas un objet.
    /// </exception>
    public static VslConfigParseResult Parse(string json, string sourcePath)
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentException.ThrowIfNullOrEmpty(sourcePath);

        using var document = ParseDocument(json, sourcePath);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new CorruptedFileException(sourcePath, new JsonException($"'{sourcePath}' n'est pas un document JSON objet."));
        }

        var installations = new List<VslInstallation>();
        var gameVersions = new List<VslGameVersionEntry>();
        var issues = new List<VslConfigIssue>();

        if (document.RootElement.TryGetProperty(InstallationsProperty, out var installationsElement)
            && installationsElement.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var element in installationsElement.EnumerateArray())
            {
                ConvertInstallation(element, index, installations, issues);
                index++;
            }
        }

        if (document.RootElement.TryGetProperty(GameVersionsProperty, out var gameVersionsElement)
            && gameVersionsElement.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var element in gameVersionsElement.EnumerateArray())
            {
                ConvertGameVersion(element, index, gameVersions, issues);
                index++;
            }
        }

        return new VslConfigParseResult(installations, gameVersions, issues);
    }

    private static JsonDocument ParseDocument(string json, string sourcePath)
    {
        try
        {
            return JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new CorruptedFileException(sourcePath, ex);
        }
    }

    private static void ConvertInstallation(JsonElement element, int index, List<VslInstallation> installations, List<VslConfigIssue> issues)
    {
        VslInstallationDto? dto;
        try
        {
            dto = element.Deserialize(VslJsonContext.Default.VslInstallationDto);
        }
        catch (JsonException)
        {
            issues.Add(new VslConfigIssue($"installations[{index}]", "entrée illisible"));
            return;
        }

        if (dto is null || string.IsNullOrWhiteSpace(dto.Path))
        {
            issues.Add(new VslConfigIssue(LabelOf(dto?.Name, index, InstallationsProperty), "chemin manquant"));
            return;
        }

        installations.Add(new VslInstallation
        {
            Id = dto.Id ?? string.Empty,
            Name = dto.Name ?? string.Empty,
            Path = dto.Path,
            Version = dto.Version ?? string.Empty,
            StartParams = dto.StartParams ?? string.Empty,
            EnvVars = dto.EnvVars ?? string.Empty,
            MesaGlThread = dto.MesaGlThread,
            LastTimePlayedMs = dto.LastTimePlayed ?? -1,
            TotalTimePlayedMs = dto.TotalTimePlayed ?? 0,
        });
    }

    private static void ConvertGameVersion(JsonElement element, int index, List<VslGameVersionEntry> gameVersions, List<VslConfigIssue> issues)
    {
        VslGameVersionDto? dto;
        try
        {
            dto = element.Deserialize(VslJsonContext.Default.VslGameVersionDto);
        }
        catch (JsonException)
        {
            issues.Add(new VslConfigIssue($"gameVersions[{index}]", "entrée illisible"));
            return;
        }

        if (dto is null || string.IsNullOrWhiteSpace(dto.Path) || string.IsNullOrWhiteSpace(dto.Version))
        {
            issues.Add(new VslConfigIssue(LabelOf(dto?.Version, index, GameVersionsProperty), "chemin ou version manquant"));
            return;
        }

        gameVersions.Add(new VslGameVersionEntry { Version = dto.Version, Path = dto.Path });
    }

    private static string LabelOf(string? name, int index, string arrayProperty)
        => string.IsNullOrWhiteSpace(name) ? $"{arrayProperty}[{index}]" : name;
}