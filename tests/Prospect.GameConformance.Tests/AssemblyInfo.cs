using Xunit;

// Exigence d'Atlas (voir son README, section Quickstart) : un seul serveur embarqué vit à la fois
// dans le processus (Atlas.XUnit.Internal.HostRegistry est un registre statique par processus),
// donc les classes de scénarios doivent s'exécuter séquentiellement. Sans dépendance de
// compilation vers Atlas : CollectionBehaviorAttribute est un attribut XUnit ordinaire, ce fichier
// compile donc identiquement que PROSPECT_CONFORMANCE_ENGINE soit défini ou non.
[assembly: CollectionBehavior(DisableTestParallelization = true)]