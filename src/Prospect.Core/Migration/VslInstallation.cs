namespace Prospect.Core.Migration;

/// <summary>
/// Une installation (« Installation » au sens de VS Launcher, docs/research/vslauncher-et-distribution.md
/// section f) telle que lue depuis <c>config.json</c>, sans validation métier Prospect : les
/// champs sont recopiés à l'identique du <c>InstallationType</c> de VSL (<c>global.d.ts</c>), y
/// compris ses deux défauts de conception documentés que la conversion vers le domaine Prospect
/// corrige (<see cref="VslInstanceMapper"/>) — <see cref="StartParams"/> une chaîne unique plutôt
/// qu'une liste, <see cref="EnvVars"/> une chaîne unique plutôt qu'un dictionnaire.
/// </summary>
public sealed record VslInstallation
{
    /// <summary>Identifiant interne VS Launcher (UUID), sans rapport avec le nom ou le dossier.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Nom affiché dans VS Launcher.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Dossier de données (<c>--dataPath</c> du jeu) de cette installation : mondes, configs, mods.
    /// Seul champ réellement requis pour qu'une entrée soit exploitable (voir
    /// <see cref="VslConfigParser"/>) — un chemin choisi librement par l'utilisateur dans VS
    /// Launcher, jamais garanti sous le dossier de convention <c>VSLInstallations/</c>.
    /// </summary>
    public required string Path { get; init; }

    /// <summary>Version du jeu associée, chaîne brute non validée (voir <see cref="Common.GameVersion.TryParse"/> côté adoption).</summary>
    public string Version { get; init; } = string.Empty;

    /// <summary>
    /// Paramètres de lancement additionnels, tels que saisis par l'utilisateur dans VS Launcher :
    /// UNE chaîne unique (le défaut de tokenisation documenté par la recherche, implication 9), à
    /// tokeniser via <see cref="VslStartParamsTokenizer"/> avant usage côté Prospect.
    /// </summary>
    public string StartParams { get; init; } = string.Empty;

    /// <summary>
    /// Variables d'environnement additionnelles, sous forme d'une chaîne unique
    /// <c>CLE=valeur,CLE2=valeur2</c> (encore un champ texte plutôt qu'un dictionnaire côté VSL) :
    /// à parser via <see cref="VslEnvVarsParser"/>.
    /// </summary>
    public string EnvVars { get; init; } = string.Empty;

    /// <summary>
    /// Case à cocher dédiée de VS Launcher pour <c>MESA_GLTHREAD=true</c> (confort pilotes Mesa,
    /// Linux). Devient la variable d'environnement du même nom côté Prospect (voir
    /// <see cref="VslInstanceMapper.ToLaunchSettings"/>), exactement comme VSL l'injecte lui-même
    /// au lancement.
    /// </summary>
    public bool MesaGlThread { get; init; }

    /// <summary>
    /// Horodatage de fin de la dernière session, en millisecondes depuis l'epoch Unix (VS Launcher
    /// le pose à <c>Date.now()</c> quand la partie se termine, pas quand elle démarre — voir
    /// <c>MainMenu.tsx</c>). Sentinelle <c>-1</c> : jamais joué.
    /// </summary>
    public long LastTimePlayedMs { get; init; } = -1;

    /// <summary>Temps de jeu cumulé, en millisecondes.</summary>
    public long TotalTimePlayedMs { get; init; }
}