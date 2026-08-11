namespace Prospect.Core.GameVersions;

/// <summary>
/// Document du cache disque du catalogue, dans <c>cache/http/</c>. On y range les charges utiles
/// brutes des deux documents distants plutôt que le modèle déjà converti : le cache survit ainsi
/// à une évolution de notre conversion (un champ qu'on apprendra à lire demain est déjà là), et
/// il n'existe qu'un seul chemin de parsing, celui que les tests couvrent.
/// </summary>
/// <param name="SchemaVersion">Version du schéma de ce fichier de cache.</param>
/// <param name="RetrievedUtc">Date de l'appel réseau qui a produit ces charges utiles.</param>
/// <param name="StableJson">Corps brut de <c>stable.json</c>.</param>
/// <param name="UnstableJson">Corps brut de <c>unstable.json</c>.</param>
internal sealed record GameCatalogCacheDocument(
    int SchemaVersion,
    DateTimeOffset RetrievedUtc,
    string StableJson,
    string UnstableJson)
{
    /// <summary>Schéma courant. Un cache écrit par une version future est ignoré, pas migré : c'est un cache, le jeter ne coûte qu'un appel réseau.</summary>
    public const int CurrentSchemaVersion = 1;
}