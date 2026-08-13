using System.IO.Abstractions;

using Prospect.Core.Common;

namespace Prospect.Core.GameVersions;

/// <summary>
/// Installation macOS : même mécanique que Linux (extraction du <c>.tar.gz</c> puis restauration
/// des bits d'exécution), mais avec deux plateformes possibles, <c>mac-arm64</c> en priorité et
/// <c>mac-x64</c> en repli, comme le fait le catalogue officiel.
/// </summary>
/// <remarks>
/// macOS est traité comme une cible réelle dès maintenant, téléchargement et installation
/// compris, même si le bouton « Jouer » attendra une vraie machine de test. VS Launcher a laissé
/// ses utilisateurs mac des années avec un « not yet supported » faute d'avoir anticipé
/// (docs/research/vslauncher-et-distribution.md, implication 12).
/// </remarks>
public sealed class MacOsGameInstallStrategy : IGameInstallStrategy
{
    private readonly TarGzGameInstaller _installer;

    public MacOsGameInstallStrategy(IFileSystem fileSystem, IUnixFilePermissions permissions)
    {
        _installer = new TarGzGameInstaller(fileSystem, permissions);
    }

    /// <inheritdoc />
    public IReadOnlyList<string> PlatformKeys { get; } = [GamePlatforms.MacArm64, GamePlatforms.MacX64];

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// Corrigé sur pièce le 2026-08-13, en lisant les premières entrées des archives 1.22.6
    /// <c>osx-x64</c> et <c>osx-arm64</c> par requête de plage HTTP (300 Ko, pas 600 Mo ; le relevé
    /// est rejoué par <c>GameArchiveLayoutLiveTests</c>). Ce qu'elles contiennent réellement n'est
    /// PAS le bundle canonique que cette liste supposait : l'unique dossier racine s'appelle
    /// <c>Vintage Story.app</c> (avec une espace), et il porte directement <c>assets/</c>,
    /// <c>Lib/</c>, <c>Mods/</c>, <c>Info.plist</c> et le binaire <c>Vintagestory</c>. Il n'y a
    /// aucun <c>Contents/MacOS/</c> pour le jeu lui-même : seul le rapporteur de plantage embarqué
    /// (<c>VSCrashReporter.app/Contents/MacOS/</c>) a cette forme.
    /// </para>
    /// <para>
    /// Comme <see cref="TarGzGameInstaller"/> aplatit le dossier racine unique, le binaire arrive à
    /// la RACINE du dossier de version, exactement comme sous Linux : c'est le premier emplacement.
    /// Le second couvre le cas où l'aplatissement ne s'appliquerait pas parce qu'un build futur
    /// ajouterait une seconde racine à côté du bundle.
    /// </para>
    /// </remarks>
    public IReadOnlyList<GameExecutableLocation> ExpectedExecutables { get; } =
    [
        GameExecutableLocation.Of("Vintagestory"),
        GameExecutableLocation.Of("Vintage Story.app", "Vintagestory"),
    ];

    /// <inheritdoc />
    public Task InstallAsync(
        string archivePath,
        string targetDirectory,
        IProgress<GameInstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
        => _installer.InstallAsync(archivePath, targetDirectory, progress, cancellationToken);
}