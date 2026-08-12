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
    private const string MesaGlThreadKey = "MESA_GLTHREAD";
    private const string MesaGlThreadValue = "true";

    /// <summary>
    /// Construit les réglages de lancement Prospect depuis une installation VSL :
    /// <see cref="VslInstallation.StartParams"/> devient une vraie liste via
    /// <see cref="VslStartParamsTokenizer"/>, <see cref="VslInstallation.EnvVars"/> devient un
    /// dictionnaire via <see cref="VslEnvVarsParser"/>, et <see cref="VslInstallation.MesaGlThread"/>
    /// — une case à cocher dédiée côté VSL — devient la variable d'environnement
    /// <c>MESA_GLTHREAD=true</c>, exactement comme VS Launcher l'injecte lui-même au lancement
    /// (<c>gameHandlers.ts</c>, <c>EXECUTE_GAME</c>). Une variable <c>MESA_GLTHREAD</c> déjà
    /// présente dans <see cref="VslInstallation.EnvVars"/> est écrasée par la case à cocher : c'est
    /// aussi la variable que Prospect connaît par son nom dans l'onglet Options d'une instance
    /// (voir <c>InstanceOptionsTabViewModel</c>), la cohérence veut que la case l'emporte.
    /// </summary>
    public static InstanceLaunchSettings ToLaunchSettings(VslInstallation installation)
    {
        ArgumentNullException.ThrowIfNull(installation);

        var env = VslEnvVarsParser.Parse(installation.EnvVars);
        if (installation.MesaGlThread)
        {
            env = new Dictionary<string, string>(env, StringComparer.Ordinal) { [MesaGlThreadKey] = MesaGlThreadValue };
        }

        return new InstanceLaunchSettings
        {
            ExtraArgs = VslStartParamsTokenizer.Tokenize(installation.StartParams),
            Env = env,
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