namespace Prospect.Core.Runtime;

/// <summary>Un runtime .NET détecté sur la machine (une ligne de <c>dotnet --list-runtimes</c>).</summary>
/// <param name="FrameworkName">Nom du framework, par exemple <c>Microsoft.NETCore.App</c>.</param>
/// <param name="Version">Version installée.</param>
public sealed record DotnetRuntimeInfo(string FrameworkName, Version Version);

/// <summary>
/// Runtime .NET que le build installé du jeu déclare requérir, lu depuis son propre
/// <c>Vintagestory.runtimeconfig.json</c> plutôt qu'une table codée en dur dans Prospect
/// (docs/architecture.md, section « 3. Lancement ») : ce fichier est écrit par le build du jeu
/// lui-même, c'est la source la plus fraîche possible, qui n'a pas besoin d'être tenue à jour à
/// chaque nouvelle version du jeu.
/// </summary>
public sealed record GameRuntimeRequirement(bool IsKnown, string FrameworkName, Version? Version)
{
    /// <summary>
    /// Le fichier manquait, était illisible, ou ne déclarait rien d'exploitable (vieux builds,
    /// cas inattendu) : repli documenté plutôt qu'une exception. Une exigence inconnue ne bloque
    /// jamais un lancement (voir <see cref="RuntimeAvailability.Indeterminate"/>) : mieux vaut
    /// tenter le lancement et laisser le jeu échouer lui-même que de refuser sur la seule base
    /// d'une incertitude côté Prospect.
    /// </summary>
    public static readonly GameRuntimeRequirement Unknown = new(false, string.Empty, null);

    /// <summary>Exigence effectivement lue dans le fichier.</summary>
    public static GameRuntimeRequirement Known(string frameworkName, Version version) => new(true, frameworkName, version);

    /// <inheritdoc />
    public override string ToString() => IsKnown ? $"{FrameworkName} {Version}" : "runtime inconnu";
}

/// <summary>Verdict de présence d'un runtime requis, rendu par <see cref="IDotnetLocator.CheckAsync"/>.</summary>
public enum RuntimeAvailability
{
    /// <summary>Le runtime requis est installé sur la machine.</summary>
    Present,

    /// <summary>Le runtime requis n'est pas installé : le lancement doit être bloqué avant de spawner un processus.</summary>
    Missing,

    /// <summary>
    /// Impossible de déterminer ce que le jeu requiert (<c>runtimeconfig.json</c> absent ou
    /// illisible). Ne bloque jamais le lancement (voir la docstring de <see cref="GameRuntimeRequirement.Unknown"/>).
    /// </summary>
    Indeterminate,
}

/// <summary>Verdict typé d'<see cref="IDotnetLocator.CheckAsync"/> : présent, absent avec la version exacte requise, ou indéterminable.</summary>
public sealed record RuntimeCheckResult(RuntimeAvailability Availability, GameRuntimeRequirement Requirement)
{
    /// <summary>Verdict pour un lancement qui n'a pas pu déterminer ce que le jeu requiert.</summary>
    public static readonly RuntimeCheckResult Indeterminate = new(RuntimeAvailability.Indeterminate, GameRuntimeRequirement.Unknown);

    public static RuntimeCheckResult Present(GameRuntimeRequirement requirement) => new(RuntimeAvailability.Present, requirement);

    public static RuntimeCheckResult Missing(GameRuntimeRequirement requirement) => new(RuntimeAvailability.Missing, requirement);
}