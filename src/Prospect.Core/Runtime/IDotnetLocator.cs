namespace Prospect.Core.Runtime;

/// <summary>
/// Port vers la détection du runtime .NET (docs/architecture.md, section « 3. Lancement ») : ce
/// qu'une installation du jeu requiert, ce qui est installé sur la machine, et le verdict qui
/// combine les deux. Nommée <c>IDotnetLocator</c> plutôt qu'un nom générique : c'est le nom retenu
/// dès la conception, pensé comme la porte ouverte à un runtime géré façon Prism/Java après le
/// MVP (le jour où Prospect saura aussi télécharger le runtime manquant plutôt que seulement le
/// diagnostiquer, cette même interface pourra le faire sans changer ses appelants).
/// </summary>
public interface IDotnetLocator
{
    /// <summary>
    /// Runtimes .NET installés sur la machine (<c>dotnet --list-runtimes</c> via
    /// <see cref="Prospect.Core.Common.IProcessRunner"/>), en cache court pour ne pas relancer le
    /// processus à chaque vérification rapprochée (par exemple plusieurs instances lancées coup
    /// sur coup).
    /// </summary>
    Task<IReadOnlyList<DotnetRuntimeInfo>> GetInstalledRuntimesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Runtime requis par l'installation du jeu de <paramref name="installDirectory"/>, lu depuis
    /// son <c>Vintagestory.runtimeconfig.json</c>. Rend <see cref="GameRuntimeRequirement.Unknown"/>
    /// si le fichier est absent ou illisible plutôt que de lever : un vieux build ou un cas
    /// inattendu ne doit jamais empêcher Prospect de tenter le lancement.
    /// </summary>
    Task<GameRuntimeRequirement> ReadRequirementAsync(string installDirectory, CancellationToken cancellationToken = default);

    /// <summary>Verdict combiné : le runtime requis par cette installation est-il présent sur la machine ?</summary>
    Task<RuntimeCheckResult> CheckAsync(string installDirectory, CancellationToken cancellationToken = default);
}