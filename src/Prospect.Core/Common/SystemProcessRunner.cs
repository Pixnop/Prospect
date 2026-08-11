using System.Diagnostics;

namespace Prospect.Core.Common;

/// <summary>
/// Implémentation d'<see cref="IProcessRunner"/> adossée au vrai
/// <see cref="System.Diagnostics.Process"/>. Comme <see cref="SystemClock"/> ou
/// <see cref="Prospect.Core.Storage.SystemAppEnvironment"/>, c'est un adaptateur système sans
/// logique propre : son unique rôle est d'exposer l'effet de bord derrière le port, ce qui
/// explique qu'il soit exclu de la mesure de couverture.
/// </summary>
public sealed class SystemProcessRunner : IProcessRunner
{
    /// <inheritdoc />
    public async Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var startInfo = new ProcessStartInfo(request.FileName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var argument in request.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Le processus '{request.FileName}' n'a pas pu être démarré.");

        var standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardError = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        return new ProcessRunResult(
            process.ExitCode,
            await standardOutput.ConfigureAwait(false),
            await standardError.ConfigureAwait(false));
    }

    /// <inheritdoc />
    public IRunningProcess Start(ProcessStartRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var startInfo = new ProcessStartInfo(request.FileName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var argument in request.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        if (!string.IsNullOrEmpty(request.WorkingDirectory))
        {
            startInfo.WorkingDirectory = request.WorkingDirectory;
        }

        if (request.EnvironmentVariables is not null)
        {
            foreach (var (key, value) in request.EnvironmentVariables)
            {
                startInfo.Environment[key] = value;
            }
        }

        return new SystemRunningProcess(startInfo);
    }

    /// <summary>
    /// Enveloppe un <see cref="System.Diagnostics.Process"/> réel derrière <see cref="IRunningProcess"/> :
    /// démarre le processus dans son constructeur, republie ses évènements de sortie ligne par
    /// ligne (en ignorant les lignes <see langword="null"/> qui marquent la fin de flux côté BCL).
    /// </summary>
    private sealed class SystemRunningProcess : IRunningProcess
    {
        private readonly Process _process;

        public SystemRunningProcess(ProcessStartInfo startInfo)
        {
            _process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            _process.OutputDataReceived += (_, e) => RaiseIfPresent(OutputDataReceived, e.Data);
            _process.ErrorDataReceived += (_, e) => RaiseIfPresent(ErrorDataReceived, e.Data);

            if (!_process.Start())
            {
                throw new InvalidOperationException($"Le processus '{startInfo.FileName}' n'a pas pu être démarré.");
            }

            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();
        }

        public int Id => _process.Id;

        public event EventHandler<string>? OutputDataReceived;

        public event EventHandler<string>? ErrorDataReceived;

        public async Task<int> WaitForExitAsync(CancellationToken cancellationToken = default)
        {
            await _process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

            return _process.ExitCode;
        }

        public void Kill(bool includeChildren = true) => _process.Kill(includeChildren);

        public void Dispose() => _process.Dispose();

        private void RaiseIfPresent(EventHandler<string>? handler, string? data)
        {
            if (data is not null)
            {
                handler?.Invoke(this, data);
            }
        }
    }
}