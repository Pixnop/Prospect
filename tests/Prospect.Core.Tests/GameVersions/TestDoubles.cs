using System.Formats.Tar;
using System.IO.Abstractions;
using System.IO.Compression;

using Prospect.Core.Common;
using Prospect.Core.GameVersions;

namespace Prospect.Core.Tests.GameVersions;

/// <summary>
/// Double de test d'<see cref="IUnixFilePermissions"/> : mémorise les modes posés au lieu
/// d'appeler <c>chmod</c>, ce qui permet de vérifier la restauration des bits d'exécution depuis
/// n'importe quel OS de la matrice CI, Windows compris.
/// </summary>
internal sealed class RecordingUnixFilePermissions : IUnixFilePermissions
{
    public Dictionary<string, UnixFileMode> Modes { get; } = new(StringComparer.Ordinal);

    public void SetMode(string path, UnixFileMode mode) => Modes[path] = mode;
}

/// <summary>Double de test d'<see cref="IGameVersionCatalog"/> nourri d'un catalogue fixe.</summary>
internal sealed class FakeGameVersionCatalog : IGameVersionCatalog
{
    private readonly GameVersionCatalog _catalog;

    public FakeGameVersionCatalog(GameVersionCatalog catalog) => _catalog = catalog;

    public Task<GameVersionCatalog> GetAsync(bool forceRefresh = false, CancellationToken cancellationToken = default)
        => Task.FromResult(_catalog);
}

/// <summary>Double de test d'<see cref="IGameInstallStrategy"/> : note ce qu'on lui demande, échoue sur commande.</summary>
/// <remarks>
/// Il POSE l'exécutable attendu par défaut, parce que c'est ce qu'une installation réussie fait et
/// que <see cref="GameInstallService"/> le vérifie désormais avant d'écrire la sentinelle.
/// <see cref="ProducesExecutable"/> à faux simule exactement le défaut de terrain : un installeur
/// qui rend la main sans erreur après avoir posé le jeu ailleurs.
/// </remarks>
internal sealed class FakeGameInstallStrategy : IGameInstallStrategy
{
    private readonly IFileSystem _fileSystem;

    public FakeGameInstallStrategy(IFileSystem fileSystem) => _fileSystem = fileSystem;

    public IReadOnlyList<string> PlatformKeys { get; set; } = [GamePlatforms.Linux];

    public IReadOnlyList<GameExecutableLocation> ExpectedExecutables { get; set; } = [GameExecutableLocation.Of("Vintagestory")];

    /// <summary>Vrai (défaut) pour qu'une installation réussie laisse vraiment un exécutable dans la cible.</summary>
    public bool ProducesExecutable { get; set; } = true;

    public List<(string ArchivePath, string TargetDirectory)> Installs { get; } = [];

    public Exception? Failure { get; set; }

    public Action<string>? BeforeReturning { get; set; }

    /// <summary>Avancements que la stratégie publie avant de rendre la main, comme le fait l'extraction réelle.</summary>
    public List<GameInstallProgress> ScriptedProgress { get; } = [];

    public Task InstallAsync(
        string archivePath,
        string targetDirectory,
        IProgress<GameInstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        Installs.Add((archivePath, targetDirectory));

        foreach (var report in ScriptedProgress)
        {
            progress?.Report(report);
        }

        if (ProducesExecutable && Failure is null)
        {
            _fileSystem.Directory.CreateDirectory(targetDirectory);
            _fileSystem.File.WriteAllText(ExpectedExecutables[0].ResolveIn(_fileSystem, targetDirectory), "#!/bin/sh");
        }

        BeforeReturning?.Invoke(targetDirectory);

        return Failure is null ? Task.CompletedTask : Task.FromException(Failure);
    }
}

/// <summary>
/// Fabrique d'archives <c>.tar.gz</c> minuscules, écrites avec <see cref="TarWriter"/> de la BCL.
/// Les tests d'extraction travaillent ainsi sur de vraies archives compressées, pas sur un
/// simulacre de format.
/// </summary>
internal static class TarGzSamples
{
    public static byte[] Create(params (string Name, byte[]? Content)[] entries)
    {
        using var output = new MemoryStream();

        using (var compressed = new GZipStream(output, CompressionLevel.Fastest, leaveOpen: true))
        {
            using var writer = new TarWriter(compressed, TarEntryFormat.Pax, leaveOpen: true);

            foreach (var (name, content) in entries)
            {
                if (content is null)
                {
                    writer.WriteEntry(new PaxTarEntry(TarEntryType.Directory, name));
                    continue;
                }

                using var data = new MemoryStream(content);
                writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, name) { DataStream = data });
            }
        }

        return output.ToArray();
    }

    public static byte[] Text(string value) => System.Text.Encoding.UTF8.GetBytes(value);
}