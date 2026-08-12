using System.IO.Abstractions;

using Prospect.Core.Storage;

namespace Prospect.Core.Migration;

/// <summary>
/// Détecte une installation de VS Launcher sur la machine (docs/research/vslauncher-et-distribution.md,
/// section f) : chemins par défaut par OS via <see cref="VslPaths"/>, présence de
/// <c>config.json</c> et/ou des dossiers de convention, puis délègue le contenu à
/// <see cref="VslConfigParser"/>. Ne lève jamais pour un contenu VS Launcher illisible : toute
/// anomalie se lit dans le <see cref="VslDetectionResult"/> renvoyé (docs/architecture.md, principe
/// « absence normale = retour, pas exception »).
/// </summary>
public sealed class VslDetector
{
    private readonly IFileSystem _fileSystem;
    private readonly IAppEnvironment _environment;

    public VslDetector(IFileSystem fileSystem, IAppEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(environment);

        _fileSystem = fileSystem;
        _environment = environment;
    }

    /// <summary>
    /// Détecte une installation de VS Launcher.
    /// </summary>
    /// <param name="rootOverride">
    /// Racine <c>appData</c> explicite (voir <see cref="VslPaths"/>), pour le dossier manuel
    /// proposé par les réglages quand la détection automatique ne trouve rien.
    /// <see langword="null"/> pour le chemin par défaut de l'OS courant.
    /// </param>
    public async Task<VslDetectionResult> DetectAsync(string? rootOverride = null, CancellationToken cancellationToken = default)
    {
        var paths = new VslPaths(_environment, rootOverride);

        var hasInstallationsFolder = _fileSystem.Directory.Exists(paths.InstallationsDirectory);
        var hasGameVersionsFolder = _fileSystem.Directory.Exists(paths.GameVersionsDirectory);
        var hasConfigFile = _fileSystem.File.Exists(paths.ConfigFilePath);

        if (!hasInstallationsFolder && !hasGameVersionsFolder && !hasConfigFile)
        {
            return VslDetectionResult.NotDetected(paths.RootDirectory);
        }

        if (!hasConfigFile)
        {
            return VslDetectionResult.DetectedWithoutConfig(paths.RootDirectory);
        }

        var json = await _fileSystem.File.ReadAllTextAsync(paths.ConfigFilePath, cancellationToken).ConfigureAwait(false);

        try
        {
            var parsed = VslConfigParser.Parse(json, paths.ConfigFilePath);

            return VslDetectionResult.Detected(paths.RootDirectory, parsed);
        }
        catch (CorruptedFileException ex)
        {
            return VslDetectionResult.DetectedWithCorruptedConfig(paths.RootDirectory, ex.Message);
        }
    }
}