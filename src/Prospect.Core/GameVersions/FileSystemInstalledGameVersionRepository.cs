using System.IO.Abstractions;

using Prospect.Core.Common;
using Prospect.Core.Storage;

namespace Prospect.Core.GameVersions;

/// <summary>
/// Implémentation d'<see cref="IInstalledGameVersionRepository"/> adossée au système de fichiers
/// abstrait. Une installation existe parce que son dossier existe et porte le fichier sentinelle,
/// pas parce qu'un index quelque part le prétend : le disque reste la source de vérité
/// (docs/architecture.md, principes).
/// </summary>
public sealed class FileSystemInstalledGameVersionRepository : IInstalledGameVersionRepository
{
    /// <summary>
    /// Fichier écrit en toute fin d'installation. Son absence dans un dossier de
    /// <c>versions/</c> signifie que l'installation a été coupée en route (crash, coupure de
    /// courant, annulation brutale) et que ce qui s'y trouve n'est pas fiable.
    /// </summary>
    public const string CompletionMarkerFileName = ".prospect-complete";

    private readonly IFileSystem _fileSystem;
    private readonly AppPaths _appPaths;

    public FileSystemInstalledGameVersionRepository(IFileSystem fileSystem, AppPaths appPaths)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(appPaths);

        _fileSystem = fileSystem;
        _appPaths = appPaths;
    }

    /// <inheritdoc />
    public string GetVersionDirectory(GameVersion version)
        => _fileSystem.Path.Combine(_appPaths.VersionsDirectory, version.ToString());

    /// <inheritdoc />
    public bool IsInstalled(GameVersion version) => _fileSystem.File.Exists(GetMarkerPath(GetVersionDirectory(version)));

    /// <inheritdoc />
    public InstalledGameVersion? Find(GameVersion version)
    {
        var directory = GetVersionDirectory(version);
        var markerPath = GetMarkerPath(directory);

        return _fileSystem.File.Exists(markerPath) ? Describe(version, directory, markerPath) : null;
    }

    /// <inheritdoc />
    public Task<GameInstallScanResult> ScanAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var installed = new List<InstalledGameVersion>();
        var broken = new List<BrokenGameInstall>();

        if (!_fileSystem.Directory.Exists(_appPaths.VersionsDirectory))
        {
            return Task.FromResult(new GameInstallScanResult(installed, broken));
        }

        foreach (var directory in _fileSystem.Directory.GetDirectories(_appPaths.VersionsDirectory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            Classify(directory, installed, broken);
        }

        var sorted = installed.OrderByDescending(entry => entry.Version).ToArray();

        return Task.FromResult(new GameInstallScanResult(sorted, broken));
    }

    /// <inheritdoc />
    public void PrepareDirectory(GameVersion version)
    {
        var directory = GetVersionDirectory(version);
        if (_fileSystem.Directory.Exists(directory))
        {
            _fileSystem.Directory.Delete(directory, recursive: true);
        }

        _fileSystem.Directory.CreateDirectory(directory);
    }

    /// <inheritdoc />
    public Task MarkCompleteAsync(GameVersion version, CancellationToken cancellationToken = default)
    {
        var directory = GetVersionDirectory(version);
        _fileSystem.Directory.CreateDirectory(directory);

        return _fileSystem.File.WriteAllTextAsync(GetMarkerPath(directory), version.ToString(), cancellationToken);
    }

    /// <inheritdoc />
    public void Remove(GameVersion version)
    {
        var directory = GetVersionDirectory(version);
        if (_fileSystem.Directory.Exists(directory))
        {
            _fileSystem.Directory.Delete(directory, recursive: true);
        }
    }

    /// <inheritdoc />
    public Task RemoveAsync(
        GameVersion version,
        IProgress<DirectoryDeleteProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var directory = GetVersionDirectory(version);

        // Task.Run assumé, pour la même raison qu'InstanceService.DeleteAsync : une installation
        // pèse plusieurs centaines de mégaoctets, System.IO.Abstractions est synchrone de bout en
        // bout, et attendre un appel synchrone ne déporte rien. Sans ça, l'interface gèle le temps
        // de la suppression.
        return Task.Run(() => DirectoryDeleter.Delete(_fileSystem, directory, progress), CancellationToken.None);
    }

    private void Classify(string directory, List<InstalledGameVersion> installed, List<BrokenGameInstall> broken)
    {
        var folderName = _fileSystem.Path.GetFileName(directory);

        if (!GameVersion.TryParse(folderName, out var version))
        {
            broken.Add(new BrokenGameInstall(directory, folderName, GameInstallBrokenReason.UnreadableVersionName));
            return;
        }

        var markerPath = GetMarkerPath(directory);
        if (!_fileSystem.File.Exists(markerPath))
        {
            broken.Add(new BrokenGameInstall(directory, folderName, GameInstallBrokenReason.MissingCompletionMarker));
            return;
        }

        installed.Add(Describe(version, directory, markerPath));
    }

    private InstalledGameVersion Describe(GameVersion version, string directory, string markerPath)
        => new(version, directory, ComputeSize(directory), _fileSystem.FileInfo.New(markerPath).LastWriteTimeUtc);

    private long ComputeSize(string directory)
        => _fileSystem.Directory
            .GetFiles(directory, "*", SearchOption.AllDirectories)
            .Sum(path => _fileSystem.FileInfo.New(path).Length);

    private string GetMarkerPath(string directory) => _fileSystem.Path.Combine(directory, CompletionMarkerFileName);
}