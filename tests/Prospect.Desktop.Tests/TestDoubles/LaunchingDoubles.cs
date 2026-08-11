using Prospect.Core.Common;
using Prospect.Core.Runtime;

namespace Prospect.Desktop.Tests.TestDoubles;

/// <summary>
/// Double de test d'<see cref="IProcessRunner"/> : enregistre ce qu'on a voulu lancer et rend ce
/// que le test a choisi. Aucun processus n'est démarré. Copie locale de l'équivalent de
/// Prospect.Core.Tests (assemblies distinctes, pas de visibilité <c>internal</c> partagée).
/// </summary>
internal sealed class FakeProcessRunner : IProcessRunner
{
    public List<ProcessRunRequest> Requests { get; } = [];

    public int ExitCode { get; set; }

    public string StandardOutput { get; set; } = string.Empty;

    public string StandardError { get; set; } = string.Empty;

    public List<ProcessStartRequest> StartRequests { get; } = [];

    public Func<ProcessStartRequest, IRunningProcess> NextProcessFactory { get; set; } = _ => new FakeRunningProcess();

    public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken = default)
    {
        Requests.Add(request);

        return Task.FromResult(new ProcessRunResult(ExitCode, StandardOutput, StandardError));
    }

    public IRunningProcess Start(ProcessStartRequest request)
    {
        StartRequests.Add(request);

        return NextProcessFactory(request);
    }
}

/// <summary>Double de test d'<see cref="IRunningProcess"/> : le test déclenche lui-même la sortie du processus.</summary>
internal sealed class FakeRunningProcess : IRunningProcess
{
    private readonly TaskCompletionSource<int> _exited = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public int Id { get; init; } = 4242;

    public bool IsKilled { get; private set; }

    public event EventHandler<string>? OutputDataReceived;

    public event EventHandler<string>? ErrorDataReceived;

    public void EmitOutput(string line) => OutputDataReceived?.Invoke(this, line);

    public void EmitError(string line) => ErrorDataReceived?.Invoke(this, line);

    public void CompleteWith(int exitCode) => _exited.TrySetResult(exitCode);

    public Task<int> WaitForExitAsync(CancellationToken cancellationToken = default) => _exited.Task;

    public void Kill(bool includeChildren = true) => IsKilled = true;

    public void Dispose()
    {
    }
}

/// <summary>
/// Double de test d'<see cref="IDotnetLocator"/> : rend un verdict fixé par le test plutôt que de
/// lire un vrai <c>runtimeconfig.json</c> ou d'appeler <c>dotnet --list-runtimes</c>.
/// </summary>
internal sealed class FakeDotnetLocator : IDotnetLocator
{
    public RuntimeCheckResult Result { get; set; } =
        RuntimeCheckResult.Present(GameRuntimeRequirement.Known("Microsoft.NETCore.App", new Version(8, 0, 0)));

    public Task<IReadOnlyList<DotnetRuntimeInfo>> GetInstalledRuntimesAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<DotnetRuntimeInfo>>([]);

    public Task<GameRuntimeRequirement> ReadRequirementAsync(string installDirectory, CancellationToken cancellationToken = default)
        => Task.FromResult(Result.Requirement);

    public Task<RuntimeCheckResult> CheckAsync(string installDirectory, CancellationToken cancellationToken = default)
        => Task.FromResult(Result);
}