using System.IO.Abstractions;

namespace Prospect.Core.Storage;

/// <summary>
/// Copie récursive d'un dossier vers un autre, fichier par fichier, avec progression et
/// annulation. Extrait de la mécanique déjà éprouvée par
/// <see cref="Prospect.Core.Instances.InstanceService.DuplicateAsync"/> : granularité au fichier,
/// y compris pour la vérification d'annulation, dossier cible créé même si la source est vide ou
/// absente. Utilitaire générique de <c>Storage</c> plutôt que d'un domaine précis : il sert la
/// duplication d'instance comme l'adoption d'installations VS Launcher (<c>Migration</c>), et
/// n'a besoin de rien d'autre qu'un système de fichiers abstrait.
/// </summary>
public sealed class DirectoryCopier
{
    private readonly IFileSystem _fileSystem;

    public DirectoryCopier(IFileSystem fileSystem)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);

        _fileSystem = fileSystem;
    }

    /// <summary>
    /// Copie tout le contenu de <paramref name="sourceDirectory"/> (récursivement) vers
    /// <paramref name="targetDirectory"/>, qui est créé même si la source n'existe pas ou est
    /// vide. Un rapport est émis après chaque fichier copié ; l'annulation est vérifiée avant
    /// chaque fichier, jamais en cours d'écriture d'un seul fichier. Le nettoyage d'un dossier
    /// cible partiellement écrit après une annulation ou un échec est la responsabilité de
    /// l'appelant (voir sa propre remarque de classe), pas de cet utilitaire, qui ne connaît pas
    /// la sémantique de ce qu'il copie.
    /// </summary>
    public async Task CopyAsync(
        string sourceDirectory,
        string targetDirectory,
        IProgress<DirectoryCopyProgress>? progress,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(sourceDirectory);
        ArgumentException.ThrowIfNullOrEmpty(targetDirectory);

        cancellationToken.ThrowIfCancellationRequested();
        _fileSystem.Directory.CreateDirectory(targetDirectory);

        var files = _fileSystem.Directory.Exists(sourceDirectory)
            ? _fileSystem.Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories)
            : [];

        for (var index = 0; index < files.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await CopyFileAsync(sourceDirectory, targetDirectory, files[index], cancellationToken).ConfigureAwait(false);
            progress?.Report(new DirectoryCopyProgress(index + 1, files.Length));
        }
    }

    private async Task CopyFileAsync(string sourceDirectory, string targetDirectory, string sourceFile, CancellationToken cancellationToken)
    {
        var relativePath = _fileSystem.Path.GetRelativePath(sourceDirectory, sourceFile);
        var destinationFile = _fileSystem.Path.Combine(targetDirectory, relativePath);
        var destinationDirectory = _fileSystem.Path.GetDirectoryName(destinationFile);
        if (!string.IsNullOrEmpty(destinationDirectory))
        {
            _fileSystem.Directory.CreateDirectory(destinationDirectory);
        }

        var sourceStream = _fileSystem.File.OpenRead(sourceFile);
        await using (sourceStream.ConfigureAwait(false))
        {
            var destinationStream = _fileSystem.File.Create(destinationFile);
            await using (destinationStream.ConfigureAwait(false))
            {
                await sourceStream.CopyToAsync(destinationStream, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}

/// <summary>Progression d'une copie de dossier menée par <see cref="DirectoryCopier"/>.</summary>
/// <param name="FilesCopied">Nombre de fichiers déjà copiés, celui-ci inclus.</param>
/// <param name="TotalFiles">Nombre total de fichiers à copier.</param>
public sealed record DirectoryCopyProgress(int FilesCopied, int TotalFiles);