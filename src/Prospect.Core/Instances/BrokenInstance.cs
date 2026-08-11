namespace Prospect.Core.Instances;

/// <summary>
/// Entrée d'un dossier candidat sous <c>instances/</c> que <see cref="IInstanceRepository.ScanAsync"/>
/// n'a pas pu charger comme instance valide. Porte la raison plutôt que de laisser le scan
/// échouer entièrement pour un seul dossier illisible : l'écran d'accueil peut lister les
/// instances saines et signaler les autres séparément.
/// </summary>
/// <param name="DirectoryPath">Chemin complet du dossier candidat (sous <c>instances/</c>).</param>
/// <param name="Reason">Catégorie du problème rencontré.</param>
/// <param name="Detail">Message technique associé (chemin du fichier, versions en cause...), destiné aux journaux plutôt qu'à l'UI telle quelle.</param>
public sealed record BrokenInstance(string DirectoryPath, InstanceBrokenReason Reason, string Detail);