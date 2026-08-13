using Prospect.Core.Instances;

namespace Prospect.Core.Migration;

/// <summary>
/// Conversions pures d'une <see cref="VslInstallation"/> vers les formes attendues par Prospect :
/// aucune entrée/sortie, entièrement testable en isolation. Utilisé par
/// <see cref="VslAdoptionService"/>, séparé de lui pour que chaque conversion (réglages de
/// lancement, dates, durées) se teste sans passer par un système de fichiers, même factice.
/// </summary>
public static class VslInstanceMapper
{
    private static bool IsMesaGlThreadKey(string key)
        => string.Equals(key, Instances.Migrations.InstanceMetadataV2ToV3Migration.LegacyEnvKey, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Construit les réglages de lancement Prospect depuis une installation VSL :
    /// <see cref="VslInstallation.StartParams"/> devient une vraie liste via
    /// <see cref="VslStartParamsTokenizer"/>, <see cref="VslInstallation.EnvVars"/> devient un
    /// dictionnaire via <see cref="VslEnvVarsParser"/>, et <see cref="VslInstallation.MesaGlThread"/>
    /// — une case à cocher dédiée côté VSL — devient la case à cocher dédiée côté Prospect
    /// (<see cref="InstanceLaunchSettings.MesaGlThread"/>), et non une entrée du dictionnaire : chez
    /// nous la variable est posée par la seule stratégie de lancement Linux, sous le nom
    /// <c>mesa_glthread</c> que Mesa lit réellement, là où VS Launcher écrivait
    /// <c>MESA_GLTHREAD</c> (<c>gameHandlers.ts</c>, <c>EXECUTE_GAME</c>). L'ancienne clé, si elle
    /// traîne dans <see cref="VslInstallation.EnvVars"/>, est retirée : gardée, elle ferait deux
    /// variables pour une seule intention, dont une inerte.
    /// </summary>
    public static InstanceLaunchSettings ToLaunchSettings(VslInstallation installation)
    {
        ArgumentNullException.ThrowIfNull(installation);

        var parsed = VslEnvVarsParser.Parse(installation.EnvVars);
        var legacy = parsed.Keys.Where(IsMesaGlThreadKey).ToArray();
        var env = legacy.Length == 0
            ? parsed
            : parsed.Where(pair => !IsMesaGlThreadKey(pair.Key)).ToDictionary(StringComparer.Ordinal);

        return new InstanceLaunchSettings
        {
            ExtraArgs = VslStartParamsTokenizer.Tokenize(installation.StartParams),
            Env = env,
            MesaGlThread = installation.MesaGlThread || legacy.Length > 0,
        };
    }

    /// <summary>
    /// Convertit <see cref="VslInstallation.LastTimePlayedMs"/> (epoch millisecondes, posé par VS
    /// Launcher à la FIN de la dernière session plutôt qu'à son début — voir <c>MainMenu.tsx</c>,
    /// <c>lastTimePlayed: finishedPlaying</c>) en <see cref="InstanceMetadata.LastLaunchedUtc"/>.
    /// Toute valeur négative (pas seulement la sentinelle <c>-1</c> exacte de VSL) est traitée
    /// comme « jamais joué », par tolérance envers un fichier édité à la main.
    /// </summary>
    public static DateTimeOffset? ToLastLaunchedUtc(long lastTimePlayedMs)
        => lastTimePlayedMs < 0 ? null : DateTimeOffset.FromUnixTimeMilliseconds(lastTimePlayedMs);

    /// <summary>
    /// Convertit <see cref="VslInstallation.TotalTimePlayedMs"/> (durée cumulée en millisecondes)
    /// en <see cref="InstanceMetadata.TotalPlaytimeSeconds"/> (secondes). Division entière : les
    /// millisecondes sous la seconde n'ont pas de sens à conserver pour un compteur affiché à la
    /// seconde près. Toute valeur négative (fichier édité à la main) est ramenée à zéro plutôt que
    /// de produire un temps de jeu négatif.
    /// </summary>
    public static long ToTotalPlaytimeSeconds(long totalTimePlayedMs)
        => totalTimePlayedMs <= 0 ? 0L : totalTimePlayedMs / 1000L;
}