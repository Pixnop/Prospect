using Prospect.Core.Diagnostics;

namespace Prospect.Desktop.Services;

/// <summary>
/// Dernière lecture du journal de lancement connue par instance, pour la durée de la session
/// (voir <see cref="GameLogInsightsService"/>).
/// </summary>
/// <remarks>
/// <para>
/// EN MÉMOIRE et jamais persisté, pour une raison plus forte que pour
/// <see cref="IModUpdateCheckCache"/> : le JOURNAL est déjà la persistance. Il survit à la
/// fermeture de Prospect, il est réécrit à chaque lancement, et le relire coûte une lecture de
/// fichier. Écrire un second état à côté ne ferait qu'ajouter une chose à garder cohérente avec
/// lui.
/// </para>
/// <para>
/// Ce cache n'existe donc que pour éviter de relire le même journal à chaque va-et-vient entre
/// onglets. Deux évènements l'invalident : un nouveau LANCEMENT (le journal est tronqué et
/// réécrit, ce qui rend tout verdict précédent faux) et la SUPPRESSION de l'instance (voir
/// <see cref="DeletedInstanceStateCleaner"/>, où le slug redevient libre pour une autre).
/// </para>
/// </remarks>
public interface IGameLogInsightsCache
{
    /// <summary>Dernière analyse connue pour cette instance, ou <see langword="null"/> si aucune n'a eu lieu depuis le démarrage.</summary>
    InstanceLogInsights? TryGet(string slug);

    /// <summary>Enregistre le résultat d'une analyse.</summary>
    void Store(string slug, InstanceLogInsights insights);

    /// <summary>Oublie ce qui est connu de cette instance. Sans effet si rien n'était en cache.</summary>
    void Invalidate(string slug);
}