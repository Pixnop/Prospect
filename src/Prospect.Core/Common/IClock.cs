namespace Prospect.Core.Common;

/// <summary>
/// Source du temps courant, injectée plutôt qu'appelée directement via
/// <see cref="DateTimeOffset.UtcNow"/>.
/// </summary>
/// <remarks>
/// Tout service du Core qui a besoin de l'heure (horodatage de création d'une instance,
/// calcul de playtime, expiration d'un cache HTTP...) dépend de cette interface plutôt que
/// de l'API statique du framework. Sans elle, ces comportements ne seraient pas testables de
/// façon déterministe : un test qui appelle <see cref="DateTimeOffset.UtcNow"/> obtient une
/// valeur différente à chaque exécution, alors qu'une fausse horloge permet de fixer le temps,
/// de le faire avancer pas à pas, et d'écrire des assertions exactes plutôt qu'approximatives.
/// </remarks>
public interface IClock
{
    /// <summary>
    /// Heure actuelle, en UTC.
    /// </summary>
    DateTimeOffset UtcNow { get; }
}