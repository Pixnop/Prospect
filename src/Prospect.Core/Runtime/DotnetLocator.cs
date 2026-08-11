using System.IO.Abstractions;

using Prospect.Core.Common;

namespace Prospect.Core.Runtime;

/// <summary>
/// Implémentation d'<see cref="IDotnetLocator"/> : lit <c>Vintagestory.runtimeconfig.json</c> via
/// le système de fichiers abstrait et interroge <c>dotnet --list-runtimes</c> via
/// <see cref="IProcessRunner"/>, jamais directement. C'est ce qui rend la détection testable sans
/// dépendre du runtime réellement installé sur la machine qui exécute les tests.
/// </summary>
public sealed class DotnetLocator : IDotnetLocator
{
    private const string RuntimeConfigFileName = "Vintagestory.runtimeconfig.json";

    /// <summary>
    /// Durée de vie du cache des runtimes installés : assez court pour rester correct si
    /// l'utilisateur installe un runtime pendant que Prospect tourne, assez long pour éviter de
    /// relancer <c>dotnet --list-runtimes</c> à chaque instance lancée en rafale.
    /// </summary>
    public static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(30);

    private readonly IProcessRunner _processRunner;
    private readonly IFileSystem _fileSystem;
    private readonly IClock _clock;
    private readonly object _cacheGate = new();
    private IReadOnlyList<DotnetRuntimeInfo>? _cachedRuntimes;
    private DateTimeOffset _cachedAtUtc;

    public DotnetLocator(IProcessRunner processRunner, IFileSystem fileSystem, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(processRunner);
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(clock);

        _processRunner = processRunner;
        _fileSystem = fileSystem;
        _clock = clock;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DotnetRuntimeInfo>> GetInstalledRuntimesAsync(CancellationToken cancellationToken = default)
    {
        lock (_cacheGate)
        {
            if (_cachedRuntimes is not null && _clock.UtcNow - _cachedAtUtc < CacheDuration)
            {
                return _cachedRuntimes;
            }
        }

        var result = await _processRunner
            .RunAsync(new ProcessRunRequest("dotnet", ["--list-runtimes"]), cancellationToken)
            .ConfigureAwait(false);
        var runtimes = DotnetRuntimeListParser.Parse(result.StandardOutput);

        lock (_cacheGate)
        {
            _cachedRuntimes = runtimes;
            _cachedAtUtc = _clock.UtcNow;
        }

        return runtimes;
    }

    /// <inheritdoc />
    public async Task<GameRuntimeRequirement> ReadRequirementAsync(string installDirectory, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(installDirectory);

        var path = _fileSystem.Path.Combine(installDirectory, RuntimeConfigFileName);
        if (!_fileSystem.File.Exists(path))
        {
            return GameRuntimeRequirement.Unknown;
        }

        var json = await _fileSystem.File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);

        return RuntimeConfigParser.Parse(json);
    }

    /// <inheritdoc />
    public async Task<RuntimeCheckResult> CheckAsync(string installDirectory, CancellationToken cancellationToken = default)
    {
        var requirement = await ReadRequirementAsync(installDirectory, cancellationToken).ConfigureAwait(false);
        if (!requirement.IsKnown)
        {
            return RuntimeCheckResult.Indeterminate;
        }

        var installed = await GetInstalledRuntimesAsync(cancellationToken).ConfigureAwait(false);
        var isPresent = installed.Any(runtime => IsCompatible(runtime, requirement));

        return isPresent ? RuntimeCheckResult.Present(requirement) : RuntimeCheckResult.Missing(requirement);
    }

    // Compatible au sens du roll-forward par défaut de .NET : même famille de framework, même
    // version majeure, et une version installée au moins égale à celle requise. Une politique
    // rollForward explicite (Major, LatestMajor...) du runtimeconfig.json n'est volontairement pas
    // interprétée : aucun build observé pendant la recherche n'en déclare, ce serait de la
    // sur-ingénierie pour un besoin non constaté.
    private static bool IsCompatible(DotnetRuntimeInfo installed, GameRuntimeRequirement requirement)
        => string.Equals(installed.FrameworkName, requirement.FrameworkName, StringComparison.Ordinal)
           && requirement.Version is not null
           && installed.Version.Major == requirement.Version.Major
           && installed.Version >= requirement.Version;
}