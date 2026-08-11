using Prospect.Core.Common;

namespace Prospect.Core.ModDb;

/// <summary>
/// Ce que Prospect propose pour mettre à jour un mod déjà installé
/// (<see cref="ModInstallService.PrepareUpdateAsync"/>). Même philosophie que
/// <see cref="ModInstallPlan"/> : rien n'est encore posé sur le disque, le plan est montré à
/// l'utilisateur avant <see cref="ModInstallService.ApplyUpdateAsync"/>.
/// </summary>
/// <param name="Previous">Le mod tel qu'installé avant la mise à jour (fichier, état, provenance).</param>
/// <param name="Updated">La nouvelle version, prête à être posée.</param>
/// <param name="MissingDependencies">
/// Dépendances déclarées par la NOUVELLE version et absentes de l'instance : elles peuvent différer
/// de celles de l'ancienne version, d'où une résolution refaite à neuf plutôt que réutilisée.
/// </param>
/// <param name="Issues">Diagnostic complet des dépendances de la nouvelle version.</param>
/// <param name="UnresolvedDependencies">Dépendances sans release installable trouvée.</param>
/// <param name="Dependents">
/// Mods installés qui déclarent dépendre de celui qu'on met à jour : INFORMATIF, jamais bloquant.
/// Monter la version de <see cref="Previous"/> ne peut mathématiquement pas violer une contrainte
/// déclarée par un dépendant (les contraintes sont des bornes minimales, docs/architecture.md) ; le
/// risque d'une montée est fonctionnel (rupture d'API non déclarée), pas de version, d'où une
/// simple liste plutôt qu'une vérification bloquante.
/// </param>
/// <param name="GameVersion">Version de jeu de l'instance visée, pour le texte d'avertissement d'un rapprochement approximatif.</param>
public sealed record ModUpdatePlan(
    InstalledMod Previous,
    ModInstallItem Updated,
    IReadOnlyList<ModInstallItem> MissingDependencies,
    IReadOnlyList<ModDependencyIssue> Issues,
    IReadOnlyList<string> UnresolvedDependencies,
    IReadOnlyList<InstalledMod> Dependents,
    GameVersion GameVersion)
{
    /// <summary>Vrai s'il y a quoi que ce soit à montrer avant de confirmer (au-delà du simple changement de version).</summary>
    public bool NeedsConfirmation => MissingDependencies.Count > 0 || UnresolvedDependencies.Count > 0 || Dependents.Count > 0;

    /// <summary>Vrai si au moins un mod installé déclare dépendre de <see cref="Previous"/>.</summary>
    public bool HasDependents => Dependents.Count > 0;

    /// <summary>Noms des mods dépendants, pour le texte informatif de la confirmation.</summary>
    public IReadOnlyList<string> DependentNames => Dependents.Select(mod => mod.DisplayName).ToArray();
}

/// <summary>Avancement agrégé d'un « Tout mettre à jour » : un cran par mod, jamais par octet.</summary>
/// <param name="CompletedCount">Mods déjà traités (succès ou échec).</param>
/// <param name="TotalCount">Nombre total de mods à mettre à jour.</param>
/// <param name="CurrentModName">Nom du mod en cours de traitement, vide une fois tous traités.</param>
public sealed record BulkUpdateProgress(int CompletedCount, int TotalCount, string CurrentModName);

/// <summary>Échec de la mise à jour d'un mod pendant un « Tout mettre à jour », sans interrompre les autres.</summary>
/// <param name="ModName">Nom du mod dont la mise à jour a échoué.</param>
/// <param name="Reason">Message d'erreur, tel que porté par l'exception d'origine.</param>
public sealed record BulkUpdateFailure(string ModName, string Reason);

/// <summary>Bilan d'un « Tout mettre à jour ».</summary>
/// <param name="Updated">Mods mis à jour avec succès.</param>
/// <param name="Failed">Mods dont la mise à jour a échoué.</param>
public sealed record BulkUpdateOutcome(IReadOnlyList<InstalledMod> Updated, IReadOnlyList<BulkUpdateFailure> Failed)
{
    /// <summary>Vrai si au moins un échec s'est produit.</summary>
    public bool HasFailures => Failed.Count > 0;
}