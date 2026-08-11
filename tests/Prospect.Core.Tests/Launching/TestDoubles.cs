using Prospect.Core.Runtime;

namespace Prospect.Core.Tests.Launching;

/// <summary>
/// Double de test d'<see cref="IDotnetLocator"/> : rend un verdict fixé par le test plutôt que de
/// lire un vrai <c>runtimeconfig.json</c> ou d'appeler <c>dotnet --list-runtimes</c>. Le parsing
/// réel est testé séparément (voir Tests/Runtime/), <c>GameLauncherTests</c> n'a besoin que du
/// verdict combiné.
/// </summary>
internal sealed class FakeDotnetLocator : IDotnetLocator
{
    public RuntimeCheckResult Result { get; set; } =
        RuntimeCheckResult.Present(GameRuntimeRequirement.Known("Microsoft.NETCore.App", new Version(8, 0, 0)));

    public List<string> CheckedDirectories { get; } = [];

    public Task<IReadOnlyList<DotnetRuntimeInfo>> GetInstalledRuntimesAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<DotnetRuntimeInfo>>([]);

    public Task<GameRuntimeRequirement> ReadRequirementAsync(string installDirectory, CancellationToken cancellationToken = default)
        => Task.FromResult(Result.Requirement);

    public Task<RuntimeCheckResult> CheckAsync(string installDirectory, CancellationToken cancellationToken = default)
    {
        CheckedDirectories.Add(installDirectory);

        return Task.FromResult(Result);
    }
}