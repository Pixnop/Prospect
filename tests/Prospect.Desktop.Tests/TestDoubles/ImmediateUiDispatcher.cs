using Prospect.Desktop.Services;

namespace Prospect.Desktop.Tests.TestDoubles;

/// <summary>
/// Double de test d'<see cref="IUiDispatcher"/> qui exécute immédiatement, sur le fil appelant.
/// Les assertions voient donc l'état à jour dès le retour de l'appel, sans dépendre d'une boucle
/// de dispatcher.
/// </summary>
internal sealed class ImmediateUiDispatcher : IUiDispatcher
{
    public void Post(Action action) => action();
}