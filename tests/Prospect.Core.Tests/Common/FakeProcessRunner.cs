using Prospect.Core.Common;

namespace Prospect.Core.Tests.Common;

/// <summary>
/// Double de test d'<see cref="IProcessRunner"/> : enregistre ce qu'on a voulu lancer et rend ce
/// que le test a choisi. Aucun processus n'est démarré. Partagé entre plusieurs domaines
/// (installation Windows via l'installeur Inno, détection du runtime via <c>dotnet
/// --list-runtimes</c>, lancement du jeu) : c'est pour ça qu'il vit sous <c>Tests/Common/</c>,
/// à côté de <see cref="FakeClock"/>, plutôt que dans le dossier de test d'un seul domaine.
/// </summary>
internal sealed class FakeProcessRunner : IProcessRunner
{
    public List<ProcessRunRequest> Requests { get; } = [];

    public int ExitCode { get; set; }

    public string StandardOutput { get; set; } = string.Empty;

    public string StandardError { get; set; } = string.Empty;

    public List<ProcessStartRequest> StartRequests { get; } = [];

    /// <summary>Exception levée par <see cref="RunAsync"/>, pour simuler une commande absente du système.</summary>
    public Exception? Failure { get; set; }

    /// <summary>
    /// Effet de bord joué avant de rendre la main, pour simuler ce que le vrai processus aurait
    /// écrit sur le disque (typiquement l'installeur Windows qui pose <c>Vintagestory.exe</c>).
    /// </summary>
    public Action<ProcessRunRequest>? OnRun { get; set; }

    /// <summary>
    /// Processus rendu par <see cref="Start"/>. Un test qui a besoin de contrôler plusieurs
    /// lancements successifs peut réassigner cette fabrique entre deux appels.
    /// </summary>
    public Func<ProcessStartRequest, IRunningProcess> NextProcessFactory { get; set; } = _ => new FakeRunningProcess();

    public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken = default)
    {
        Requests.Add(request);
        OnRun?.Invoke(request);

        return Failure is null
            ? Task.FromResult(new ProcessRunResult(ExitCode, StandardOutput, StandardError))
            : Task.FromException<ProcessRunResult>(Failure);
    }

    public IRunningProcess Start(ProcessStartRequest request)
    {
        StartRequests.Add(request);

        return NextProcessFactory(request);
    }
}

/// <summary>
/// Double de test d'<see cref="IRunningProcess"/> : le test déclenche lui-même les évènements de
/// sortie et la fin du processus, aucun vrai processus n'est démarré ni tué.
/// </summary>
internal sealed class FakeRunningProcess : IRunningProcess
{
    private readonly TaskCompletionSource<int> _exited = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public int Id { get; init; } = 4242;

    public bool IsKilled { get; private set; }

    public bool? KilledIncludingChildren { get; private set; }

    public bool IsDisposed { get; private set; }

    public event EventHandler<string>? OutputDataReceived;

    public event EventHandler<string>? ErrorDataReceived;

    public void EmitOutput(string line) => OutputDataReceived?.Invoke(this, line);

    public void EmitError(string line) => ErrorDataReceived?.Invoke(this, line);

    /// <summary>Simule la sortie du processus : débloque tout appelant en attente sur <see cref="WaitForExitAsync"/>.</summary>
    public void CompleteWith(int exitCode) => _exited.TrySetResult(exitCode);

    public Task<int> WaitForExitAsync(CancellationToken cancellationToken = default) => _exited.Task;

    public void Kill(bool includeChildren = true)
    {
        IsKilled = true;
        KilledIncludingChildren = includeChildren;
    }

    public void Dispose() => IsDisposed = true;
}