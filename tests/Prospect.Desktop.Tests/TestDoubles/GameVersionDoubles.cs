using System.IO.Abstractions;

using Prospect.Core.Common;
using Prospect.Core.GameVersions;
using Prospect.Core.Http;

namespace Prospect.Desktop.Tests.TestDoubles;

/// <summary>Double de test d'<see cref="IGameVersionCatalog"/> : catalogue figé, ou panne au choix.</summary>
internal sealed class FakeGameVersionCatalog : IGameVersionCatalog
{
    public GameVersionCatalog Catalog { get; set; } = new([], DateTimeOffset.UnixEpoch, GameCatalogFreshness.Live);

    /// <summary>Vrai : chaque consultation lève l'exception typée du domaine, comme hors ligne sans cache.</summary>
    public bool IsUnavailable { get; set; }

    public int Calls { get; private set; }

    public bool LastForceRefresh { get; private set; }

    public Task<GameVersionCatalog> GetAsync(bool forceRefresh = false, CancellationToken cancellationToken = default)
    {
        Calls++;
        LastForceRefresh = forceRefresh;

        return IsUnavailable
            ? throw new GameCatalogUnavailableException()
            : Task.FromResult(Catalog);
    }

    /// <summary>Construit un catalogue à partir de numéros de version, avec un fichier Linux et un fichier Windows chacun.</summary>
    public static GameVersionCatalog Build(params string[] versions)
    {
        var entries = versions
            .Select(GameVersion.Parse)
            .Select(version => new GameVersionCatalogEntry(version, new Dictionary<string, GameVersionAsset>(StringComparer.Ordinal)
            {
                [GamePlatforms.Linux] = Asset(GamePlatforms.Linux, $"vs_client_linux-x64_{version}.tar.gz", version),
                [GamePlatforms.Windows] = Asset(GamePlatforms.Windows, $"vs_install_win-x64_{version}.exe", version),
                [GamePlatforms.MacArm64] = Asset(GamePlatforms.MacArm64, $"vs_client_osx-arm64_{version}.tar.gz", version),
            }))
            .OrderByDescending(entry => entry.Version)
            .ToArray();

        return new GameVersionCatalog(entries, DateTimeOffset.UnixEpoch, GameCatalogFreshness.Live);
    }

    private static GameVersionAsset Asset(string platformKey, string fileName, GameVersion version) => new(
        platformKey,
        fileName,
        "590.5 MB",
        "0123456789abcdef0123456789abcdef",
        new Uri($"https://cdn.example/{version}/{fileName}"),
        new Uri($"https://local.example/{version}/{fileName}"),
        IsLatest: false);
}

/// <summary>
/// Double de test d'<see cref="IGameInstallStrategy"/> : accepte les trois plateformes et se
/// contente de créer le dossier cible, sans rien extraire.
/// </summary>
internal sealed class FakeGameInstallStrategy : IGameInstallStrategy
{
    private readonly IFileSystem _fileSystem;

    public FakeGameInstallStrategy(IFileSystem fileSystem) => _fileSystem = fileSystem;

    public IReadOnlyList<string> PlatformKeys { get; set; } = [GamePlatforms.Linux, GamePlatforms.Windows, GamePlatforms.MacArm64];

    public IReadOnlyList<GameExecutableLocation> ExpectedExecutables { get; set; } = [GameExecutableLocation.Of("Vintagestory")];

    /// <summary>Vrai (défaut) : une installation réussie laisse vraiment un exécutable dans la cible.</summary>
    public bool ProducesExecutable { get; set; } = true;

    public List<string> Installs { get; } = [];

    public Exception? Failure { get; set; }

    /// <summary>Avancements que la stratégie publie avant de rendre la main, comme le fait l'extraction réelle.</summary>
    public List<GameInstallProgress> ScriptedProgress { get; } = [];

    public Task InstallAsync(
        string archivePath,
        string targetDirectory,
        IProgress<GameInstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        Installs.Add(targetDirectory);

        foreach (var report in ScriptedProgress)
        {
            progress?.Report(report);
        }

        if (ProducesExecutable && Failure is null)
        {
            _fileSystem.Directory.CreateDirectory(targetDirectory);
            _fileSystem.File.WriteAllText(ExpectedExecutables[0].ResolveIn(_fileSystem, targetDirectory), "#!/bin/sh");
        }

        return Failure is null ? Task.CompletedTask : Task.FromException(Failure);
    }
}

/// <summary>
/// Double de test d'<see cref="IDownloadManager"/> : ne télécharge rien, publie une progression
/// scriptée et rend un chemin. Permet d'exercer les ViewModels sans toucher au réseau ni au disque.
/// </summary>
internal sealed class FakeDownloadManager : IDownloadManager
{
    private readonly List<DownloadOperation> _operations = [];

    public IReadOnlyList<DownloadOperation> Operations => _operations;

    public event EventHandler? OperationsChanged;

    public List<DownloadRequest> Requests { get; } = [];

    public Exception? Failure { get; set; }

    /// <summary>Avancements publiés à l'appelant avant de rendre la main.</summary>
    public List<DownloadProgress> ScriptedProgress { get; } = [];

    public Task<string> DownloadAsync(DownloadRequest request, IProgress<DownloadProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        Requests.Add(request);
        cancellationToken.ThrowIfCancellationRequested();

        foreach (var report in ScriptedProgress)
        {
            progress?.Report(report);
        }

        return Failure is null ? Task.FromResult($"/downloads/{request.FileName}") : Task.FromException<string>(Failure);
    }

    public void Dismiss(DownloadOperation operation)
    {
        _operations.Remove(operation);
        OperationsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void DismissFinished()
    {
        _operations.RemoveAll(candidate => candidate.IsFinished);
        OperationsChanged?.Invoke(this, EventArgs.Empty);
    }
}