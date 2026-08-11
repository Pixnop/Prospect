using Prospect.Core.ModDb;

namespace Prospect.Desktop.Services;

/// <summary>Ce que <see cref="IModUpdateCheckCache.Changed"/> transporte pour une instance.</summary>
/// <param name="Slug">Instance concernée.</param>
/// <param name="Report">Nouveau résultat connu, ou <see langword="null"/> si l'entrée vient d'être invalidée.</param>
public sealed record ModUpdateCacheChange(string Slug, InstanceUpdateReport? Report);

/// <summary>
/// Dernier résultat de vérification de mises à jour connu par instance, pour la pastille discrète
/// de la carte d'Accueil et l'horodatage de l'onglet Mods (design/ui_kits/launcher/screen-instance.jsx).
/// </summary>
/// <remarks>
/// Volontairement EN MÉMOIRE pour la durée de la session, jamais persisté sur disque : le MVP ne
/// vérifie jamais automatiquement les mises à jour au démarrage (voir
/// <see cref="Prospect.Desktop.ViewModels.Mods.InstanceModsTabViewModel"/>), donc au lancement de
/// l'application, aucune instance n'a encore de résultat à montrer — c'est le comportement voulu,
/// pas une lacune. Persister aurait en plus posé la question de la péremption d'un résultat vieux
/// de plusieurs jours pour un bénéfice mince : la pastille réapparaît de toute façon dès la
/// prochaine vérification explicite, dans la même session ou la suivante.
/// </remarks>
public interface IModUpdateCheckCache
{
    /// <summary>Dernier résultat connu pour cette instance, ou <see langword="null"/> si aucune vérification n'a eu lieu depuis le démarrage.</summary>
    InstanceUpdateReport? TryGet(string slug);

    /// <summary>Enregistre le résultat d'une vérification et notifie <see cref="Changed"/>.</summary>
    void Store(string slug, InstanceUpdateReport report);

    /// <summary>
    /// Invalide le résultat connu d'une instance et notifie <see cref="Changed"/>. Toute
    /// installation, désinstallation, activation ou mise à jour de mod doit appeler ceci : sans ça,
    /// la pastille de l'Accueil continuerait d'afficher un décompte qui ne reflète plus l'état réel
    /// du dossier Mods.
    /// </summary>
    void Invalidate(string slug);

    /// <summary>
    /// Levé après <see cref="Store"/> ou <see cref="Invalidate"/> : la carte d'Accueil de l'instance
    /// concernée s'y abonne pour rester à jour même si elle a été construite avant le changement
    /// (docs/architecture.md, même mécanique que <c>RunningInstanceTracker.StatusChanged</c>).
    /// </summary>
    event EventHandler<ModUpdateCacheChange>? Changed;
}
