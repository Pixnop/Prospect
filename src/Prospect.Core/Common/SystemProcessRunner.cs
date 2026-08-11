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
}