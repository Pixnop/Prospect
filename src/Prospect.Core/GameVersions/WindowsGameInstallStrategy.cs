using System.IO.Abstractions;

using Prospect.Core.Common;

namespace Prospect.Core.GameVersions;

/// <summary>
/// Installation Windows : exécution silencieuse de l'installeur Inno Setup officiel.
/// </summary>
/// <remarks>
/// Il n'existe aucun build Windows portable, uniquement cet installeur
/// (docs/research/vslauncher-et-distribution.md, section b), donc pas d'archive à extraire ici :
/// on reprend le jeu d'options éprouvé par VS Launcher, où <c>/CURRENTUSER</c> évite l'élévation
/// UAC et <c>/DIR</c> pose le jeu directement dans le dossier de la version.
/// </remarks>
public sealed class WindowsGameInstallStrategy : IGameInstallStrategy
{
    /// <summary>
    /// Options passées à l'installeur, dans l'ordre. Exposées pour que le test vérifie exactement
    /// la ligne de commande : une faute de frappe ici se traduirait par un installeur qui ouvre une
    /// fenêtre en pleine installation silencieuse, ou qui installe ailleurs que prévu.
    /// </summary>
    public static readonly IReadOnlyList<string> SilentArguments =
    [
        "/VERYSILENT",
        "/SUPPRESSMSGBOXES",
        "/NORESTART",
        "/CURRENTUSER",
        "/NOICONS",
    ];

    private readonly IFileSystem _fileSystem;
    private readonly IProcessRunner _processRunner;

    public WindowsGameInstallStrategy(IFileSystem fileSystem, IProcessRunner processRunner)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(processRunner);

        _fileSystem = fileSystem;
        _processRunner = processRunner;
    }

    /// <inheritdoc />
    public IReadOnlyList<string> PlatformKeys { get; } = [GamePlatforms.Windows];

    /// <inheritdoc />
    public async Task InstallAsync(string archivePath, string targetDirectory, CancellationToken cancellationToken = default)
    {
        _fileSystem.Directory.CreateDirectory(targetDirectory);

        var arguments = new List<string>(SilentArguments) { $"/DIR={targetDirectory}" };
        var result = await _processRunner
            .RunAsync(new ProcessRunRequest(archivePath, arguments), cancellationToken)
            .ConfigureAwait(false);

        if (result.ExitCode != 0)
        {
            throw GameInstallFailedException.ForInstallerExitCode(archivePath, result.ExitCode, result.StandardError);
        }
    }
}