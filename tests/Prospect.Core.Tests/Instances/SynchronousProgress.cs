namespace Prospect.Core.Tests.Instances;

/// <summary>
/// Implémentation directe d'<see cref="IProgress{T}"/> qui invoque le callback en synchrone, sur
/// le thread appelant. <see cref="Progress{T}"/> du framework capture le
/// <see cref="System.Threading.SynchronizationContext"/> courant et poste le rapport de façon
/// asynchrone (souvent via le thread pool), ce qui rend les tests d'annulation « après le Nième
/// fichier » non déterministes ; ce double garantit que le rapport est visible dès le retour de
/// <c>Report</c>.
/// </summary>
internal sealed class SynchronousProgress<T>(Action<T> onReport) : IProgress<T>
{
    public void Report(T value) => onReport(value);
}