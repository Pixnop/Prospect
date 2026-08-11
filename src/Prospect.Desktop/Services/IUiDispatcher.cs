using Avalonia.Threading;

namespace Prospect.Desktop.Services;

/// <summary>
/// Port vers le fil d'exécution de l'interface. Les évènements du <c>DownloadManager</c> et les
/// rapports d'avancement d'une installation arrivent depuis un fil de fond ; toute mise à jour de
/// collection observable doit repasser par le fil UI. Passer par une interface évite d'avoir un
/// <see cref="Dispatcher"/> réel dans les tests de ViewModel, qui n'ont pas d'application Avalonia
/// en cours d'exécution.
/// </summary>
public interface IUiDispatcher
{
    /// <summary>Exécute <paramref name="action"/> sur le fil de l'interface, sans attendre.</summary>
    void Post(Action action);
}

/// <summary>
/// Implémentation d'<see cref="IUiDispatcher"/> adossée au <see cref="Dispatcher.UIThread"/>
/// d'Avalonia. Adaptateur trivial, sans logique propre.
/// </summary>
public sealed class AvaloniaUiDispatcher : IUiDispatcher
{
    /// <inheritdoc />
    public void Post(Action action) => Dispatcher.UIThread.Post(action);
}