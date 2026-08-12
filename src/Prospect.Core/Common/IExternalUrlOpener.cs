using Prospect.Core.Storage;

namespace Prospect.Core.Common;

/// <summary>
/// Port vers l'ouverture d'une URL dans le navigateur du système. Existe pour un seul usage
/// aujourd'hui, le bouton « Ouvrir sur le ModDB » de la fiche d'un mod, mais servira à tout lien
/// sortant : le Core ne lance jamais un processus directement.
/// </summary>
public interface IExternalUrlOpener
{
    /// <summary>
    /// Ouvre <paramref name="url"/> dans le navigateur par défaut.
    /// </summary>
    /// <returns>
    /// Vrai si la commande d'ouverture s'est terminée normalement. L'échec n'est jamais une
    /// exception : ne pas réussir à ouvrir un navigateur est une déception, pas une panne, et
    /// l'appelant se contente d'en informer l'utilisateur.
    /// </returns>
    Task<bool> OpenAsync(Uri url, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ouvre le gestionnaire de fichiers du système sur <paramref name="path"/> (bouton « Ouvrir le
    /// dossier » des Réglages, section Jeu). Contrairement à <see cref="OpenAsync"/>,
    /// <paramref name="path"/> n'est jamais filtré par schéma : cette méthode existe précisément
    /// pour un chemin LOCAL calculé par Prospect lui-même (<c>AppPaths.RootDirectory</c>), jamais
    /// une valeur reçue d'une réponse d'API tierce — c'est cette provenance tierce, documentée sur
    /// <see cref="OpenAsync"/>, qui justifie son filtre http(s) strict, et qui ne s'applique pas ici.
    /// </summary>
    /// <returns>Même contrat que <see cref="OpenAsync"/> : un échec est rapporté, jamais lancé en exception.</returns>
    Task<bool> OpenFolderAsync(string path, CancellationToken cancellationToken = default);
}

/// <summary>
/// Implémentation d'<see cref="IExternalUrlOpener"/> par la commande d'ouverture de chaque système
/// (<c>xdg-open</c>, <c>open</c>, <c>explorer</c>), passée par <see cref="IProcessRunner"/> plutôt
/// que par <c>Process.Start</c>.
/// </summary>
/// <remarks>
/// Ce détour par le port de processus existant a un effet secondaire précieux : contrairement aux
/// autres adaptateurs système du Core (<see cref="SystemProcessRunner"/>,
/// <see cref="SystemAppEnvironment"/>), cette classe reste entièrement testable sur les trois OS
/// depuis n'importe quelle machine, et n'a donc pas besoin d'être exclue de la mesure de
/// couverture.
/// </remarks>
public sealed class ExternalUrlOpener : IExternalUrlOpener
{
    private readonly IProcessRunner _processRunner;
    private readonly IAppEnvironment _environment;

    /// <summary>Construit l'adaptateur.</summary>
    /// <param name="processRunner">Port d'exécution de processus.</param>
    /// <param name="environment">Source de l'OS courant.</param>
    public ExternalUrlOpener(IProcessRunner processRunner, IAppEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(processRunner);
        ArgumentNullException.ThrowIfNull(environment);

        _processRunner = processRunner;
        _environment = environment;
    }

    /// <inheritdoc />
    public Task<bool> OpenAsync(Uri url, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(url);

        // Seuls http et https sortent d'ici. Une URL vient toujours d'une réponse d'API, donc d'un
        // tiers : passer file:// ou un schéma exotique à la commande d'ouverture du système
        // reviendrait à lui laisser choisir ce qu'on exécute.
        return url.Scheme is not ("http" or "https")
            ? Task.FromResult(false)
            : RunOpenCommandAsync(url.AbsoluteUri, cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> OpenFolderAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        return RunOpenCommandAsync(path, cancellationToken);
    }

    // Commune à OpenAsync et OpenFolderAsync : seul l'argument passé à la commande d'ouverture du
    // système change (URL http(s) ou chemin local), pas la façon de la choisir ni d'interpréter
    // son code de sortie.
    private async Task<bool> RunOpenCommandAsync(string argument, CancellationToken cancellationToken)
    {
        var fileName = _environment.CurrentOperatingSystem switch
        {
            AppOperatingSystem.Windows => "explorer",
            AppOperatingSystem.MacOs => "open",
            _ => "xdg-open",
        };

        try
        {
            var result = await _processRunner
                .RunAsync(new ProcessRunRequest(fileName, [argument]), cancellationToken)
                .ConfigureAwait(false);

            // explorer.exe rend un code non nul même quand il a bien ouvert le navigateur ou le
            // dossier : sur Windows, avoir démarré sans exception est le seul signal exploitable.
            return _environment.CurrentOperatingSystem == AppOperatingSystem.Windows || result.ExitCode == 0;
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }
}