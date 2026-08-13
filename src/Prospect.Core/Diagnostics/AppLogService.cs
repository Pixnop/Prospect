using System.IO.Abstractions;
using System.IO.Compression;

using Prospect.Core.Common;
using Prospect.Core.Storage;

namespace Prospect.Core.Diagnostics;

/// <summary>
/// Ce qui se lit et ce qui s'emporte du dossier <c>logs/</c> : la fin du journal d'application pour
/// la page Journaux, et une archive de tous les journaux pour un rapport de terrain.
/// </summary>
/// <remarks>
/// <para>
/// Le service n'écrit rien dans les journaux, il les CONSULTE : l'écriture reste le domaine de
/// <see cref="FileAppLog"/> et de <c>GameLauncher</c>. Les deux responsabilités sont séparées parce
/// que la lecture a le droit d'échouer bruyamment (l'utilisateur a demandé quelque chose et attend
/// une réponse) là où l'écriture doit rester silencieuse.
/// </para>
/// <para>
/// Chaque lecture est un INSTANTANÉ complet, et ce service n'en orchestre aucun suivi : pas
/// d'observateur de système de fichiers, pas de lecture incrémentale, pas de gestion de troncature.
/// C'est la page qui décide à quelle fréquence relire (voir <c>LogsViewModel</c>, qui la rappelle
/// périodiquement tant qu'elle est affichée), et cette répartition est délibérée : relire un fichier
/// plafonné à <see cref="FileAppLog.MaxSizeBytes"/> coûte assez peu pour qu'une boucle suffise, là
/// où un vrai <c>tail -f</c> demanderait les trois mécanismes ci-dessus pour le même résultat.
/// </para>
/// </remarks>
public sealed class AppLogService
{
    /// <summary>Motif des journaux de lancement d'instance, écrits par <c>GameLauncher</c>.</summary>
    public const string InstanceLogPattern = "instance-*.log";

    /// <summary>Nombre de lignes rendues par défaut par <see cref="ReadTail"/>.</summary>
    public const int DefaultTailLines = 500;

    private readonly IFileSystem _fileSystem;
    private readonly AppPaths _appPaths;

    /// <summary>Construit le service.</summary>
    /// <param name="fileSystem">Système de fichiers abstrait.</param>
    /// <param name="appPaths">Arborescence de l'application, pour <c>logs/</c>.</param>
    public AppLogService(IFileSystem fileSystem, AppPaths appPaths)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(appPaths);

        _fileSystem = fileSystem;
        _appPaths = appPaths;
    }

    /// <summary>Chemin du journal d'application, qu'il existe ou non.</summary>
    public string AppLogPath => _fileSystem.Path.Combine(_appPaths.LogsDirectory, FileAppLog.FileName);

    /// <summary>
    /// Les <paramref name="maxLines"/> dernières lignes du journal d'application, de la plus
    /// ancienne à la plus récente. Liste VIDE quand le fichier n'existe pas encore ou ne contient
    /// rien : une absence normale n'est pas une erreur, et la page a un état vide à afficher.
    /// </summary>
    public IReadOnlyList<AppLogEntry> ReadTail(int maxLines = DefaultTailLines)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxLines);

        var path = AppLogPath;
        if (!_fileSystem.File.Exists(path))
        {
            return [];
        }

        string[] lines;
        try
        {
            lines = _fileSystem.File.ReadAllLines(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Journal verrouillé ou illisible : l'état vide de la page dit déjà « rien à montrer »,
            // et faire remonter une exception depuis un bouton « Rafraîchir » n'apprendrait rien.
            return [];
        }

        return lines
            .Reverse()
            .SkipWhile(string.IsNullOrWhiteSpace)
            .Take(maxLines)
            .Reverse()
            .Select(line => new AppLogEntry(line, ParseLevel(line)))
            .ToArray();
    }

    /// <summary>
    /// Journaux présents dans <c>logs/</c>, chemins absolus : le journal d'application d'abord,
    /// puis les journaux de lancement d'instance par ordre alphabétique. C'est exactement ce que
    /// <see cref="ExportAsync"/> met dans l'archive, exposé à part pour que l'interface puisse dire
    /// « rien à exporter » sans avoir à écrire un fichier pour le découvrir.
    /// </summary>
    public IReadOnlyList<string> FindLogFiles()
    {
        var directory = _appPaths.LogsDirectory;
        if (!_fileSystem.Directory.Exists(directory))
        {
            return [];
        }

        var files = new List<string>();
        if (_fileSystem.File.Exists(AppLogPath))
        {
            files.Add(AppLogPath);
        }

        files.AddRange(_fileSystem.Directory
            .GetFiles(directory, InstanceLogPattern)
            .OrderBy(path => path, StringComparer.Ordinal));

        return files;
    }

    /// <summary>
    /// Écrit un zip contenant tous les journaux présents et rend leur nombre.
    /// </summary>
    /// <param name="destinationPath">Fichier à écrire. Écrasé s'il existe.</param>
    /// <param name="cancellationToken">Annulation.</param>
    /// <returns>Nombre de journaux écrits dans l'archive. Zéro quand il n'y en avait aucun.</returns>
    /// <remarks>
    /// L'archive est écrite même vide plutôt que refusée : l'utilisateur a choisi une destination,
    /// et un zip sans entrée est une réponse plus claire qu'un échec. C'est l'appelant qui décide
    /// s'il vaut la peine de proposer l'export, à partir de <see cref="FindLogFiles"/>.
    /// </remarks>
    /// <exception cref="IOException">La destination n'a pas pu être écrite.</exception>
    public async Task<int> ExportAsync(string destinationPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(destinationPath);

        var files = FindLogFiles();

        var parent = _fileSystem.Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(parent))
        {
            _fileSystem.Directory.CreateDirectory(parent);
        }

        var stream = _fileSystem.File.Create(destinationPath);
        await using (stream.ConfigureAwait(false))
        {
            using var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true);
            var written = 0;

            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Une lecture par fichier, en flux : un journal d'instance peut peser autant que la
                // sortie complète d'une session de jeu.
                var source = _fileSystem.File.OpenRead(file);
                await using (source.ConfigureAwait(false))
                {
                    var entry = archive.CreateEntry(_fileSystem.Path.GetFileName(file), CompressionLevel.Optimal);
                    var target = entry.Open();
                    await using (target.ConfigureAwait(false))
                    {
                        await source.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
                    }
                }

                written++;
            }

            return written;
        }
    }

    /// <summary>
    /// Niveau relu de l'étiquette posée par <see cref="FileAppLog"/>, ou <see langword="null"/>.
    /// Cherché APRÈS l'horodatage et entre crochets, pas n'importe où dans la ligne : un message
    /// qui cite « [ERROR] » ne doit pas se teindre en rouge pour autant.
    /// </summary>
    private static AppLogLevel? ParseLevel(string line)
    {
        var open = line.IndexOf('[', StringComparison.Ordinal);
        if (open < 0)
        {
            return null;
        }

        var close = line.IndexOf(']', open);
        if (close < 0)
        {
            return null;
        }

        // L'étiquette suit immédiatement l'horodatage ISO 8601, soit 21 caractères et une espace.
        // Au-delà, c'est le message qui parle, pas l'entête.
        if (open > 21)
        {
            return null;
        }

        return line[(open + 1)..close] switch
        {
            "INFO" => AppLogLevel.Info,
            "WARN" => AppLogLevel.Warning,
            "ERROR" => AppLogLevel.Error,
            _ => null,
        };
    }
}