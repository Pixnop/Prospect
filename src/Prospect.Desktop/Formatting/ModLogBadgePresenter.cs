using Prospect.Core.Diagnostics;
using Prospect.Core.ModDb;
using Prospect.Desktop.Resources;

namespace Prospect.Desktop.Formatting;

/// <summary>
/// Ce qu'une ligne de mod montre du dernier lancement : les décomptes qui décident des pastilles,
/// et les textes déjà mis en mots. Décision pure, sans UI, donc vérifiable sans fenêtre.
/// </summary>
/// <param name="ErrorCount">Erreurs attribuées à ce mod au dernier lancement.</param>
/// <param name="WarningCount">Avertissements attribués à ce mod au dernier lancement.</param>
/// <param name="ProblemTooltip">Les lignes en cause, en exemple, ou vide s'il n'y en a pas.</param>
/// <param name="IntegrationText">Libellé de la pastille d'intégration, ou vide s'il n'y a rien à dire.</param>
/// <param name="IntegrationTooltip">Le détail des intégrations, une par ligne.</param>
public sealed record ModLogBadges(
    int ErrorCount,
    int WarningCount,
    string ProblemTooltip,
    string IntegrationText,
    string IntegrationTooltip)
{
    /// <summary>Rien à afficher : mod sain, sans intégration relevée.</summary>
    public static ModLogBadges None { get; } = new(0, 0, string.Empty, string.Empty, string.Empty);

    /// <summary>Vrai quand la pastille d'erreurs doit s'afficher.</summary>
    public bool HasErrors => ErrorCount > 0;

    /// <summary>Vrai quand la pastille d'avertissements doit s'afficher.</summary>
    public bool HasWarnings => WarningCount > 0;

    /// <summary>Vrai quand la pastille d'intégration doit s'afficher.</summary>
    public bool HasIntegration => IntegrationText.Length > 0;
}

/// <summary>
/// Traduit ce que <see cref="GameLogInsightsService"/> a relevé en pastilles de la ligne de mod.
/// </summary>
/// <remarks>
/// Deux règles portent tout le reste. Une intégration RÉSOLUE prime sur une référence manquante
/// dans le libellé de la pastille : « fonctionne avec » est ce que l'utilisateur cherche, et une
/// référence manquante est déjà comptée par la pastille d'erreurs, puisque le jeu l'a écrite dans
/// son journal. Et les intégrations FACULTATIVES (celles qu'un <c>dependsOn</c> conditionne, dont
/// la cible n'est pas installée) ne s'affichent pas : un mod de contenu en annonce des dizaines,
/// et une ligne de liste ne peut pas porter la liste des mods avec lesquels il POURRAIT
/// fonctionner sans noyer ceux avec lesquels il fonctionne.
/// </remarks>
public static class ModLogBadgePresenter
{
    /// <summary>Intégrations détaillées dans l'infobulle au maximum : au-delà, elle ne se lit plus.</summary>
    public const int MaxTooltipIntegrations = 6;

    /// <summary>
    /// Décrit la ligne de <paramref name="mod"/>.
    /// </summary>
    /// <param name="mod">Mod installé dont on construit la ligne.</param>
    /// <param name="insights">Dernier résultat d'analyse connu, ou <see cref="InstanceLogInsights.None"/>.</param>
    /// <param name="displayNames">
    /// Noms affichables par <c>modid</c>, pour que la pastille dise « fonctionne avec Carry On »
    /// plutôt que « fonctionne avec carryon ». Un mod absent de cette table garde son identifiant :
    /// c'est le cas d'une cible qui n'est pas installée, et l'identifiant est alors la seule chose
    /// vraie qu'on puisse en dire.
    /// </param>
    public static ModLogBadges Describe(InstalledMod mod, InstanceLogInsights insights, IReadOnlyDictionary<string, string> displayNames)
    {
        ArgumentNullException.ThrowIfNull(mod);
        ArgumentNullException.ThrowIfNull(insights);
        ArgumentNullException.ThrowIfNull(displayNames);

        var verdict = insights.FindMod(mod);
        var integrations = insights.FindIntegrations(mod)
            .Where(integration => integration.Nature != ModIntegrationNature.Optional)
            .OrderBy(integration => integration.Nature == ModIntegrationNature.Resolved ? 0 : 1)
            .ThenBy(integration => integration.TargetModId, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (verdict is null && integrations.Count == 0)
        {
            return ModLogBadges.None;
        }

        return new ModLogBadges(
            verdict?.ErrorCount ?? 0,
            verdict?.WarningCount ?? 0,
            verdict is null ? string.Empty : UiText.Mods.LogProblemTooltip(verdict.Samples),
            IntegrationLabel(integrations, displayNames),
            IntegrationTooltip(integrations, displayNames));
    }

    /// <summary>Table des noms affichables, à passer à <see cref="Describe"/>.</summary>
    public static IReadOnlyDictionary<string, string> DisplayNames(IEnumerable<InstalledMod> mods)
    {
        ArgumentNullException.ThrowIfNull(mods);

        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var mod in mods)
        {
            names[mod.Identity] = mod.DisplayName;
        }

        return names;
    }

    private static string IntegrationLabel(List<ModIntegration> integrations, IReadOnlyDictionary<string, string> displayNames)
    {
        if (integrations.Count == 0)
        {
            return string.Empty;
        }

        var resolved = integrations.Where(integration => integration.Nature == ModIntegrationNature.Resolved).ToList();
        var shown = resolved.Count > 0 ? resolved : integrations;
        var name = Name(shown[0].TargetModId, displayNames);

        return resolved.Count > 0
            ? UiText.Mods.WorksWithBadge(name, shown.Count - 1)
            : UiText.Mods.ExpectsContentBadge(name, shown.Count - 1);
    }

    private static string IntegrationTooltip(List<ModIntegration> integrations, IReadOnlyDictionary<string, string> displayNames)
    {
        if (integrations.Count == 0)
        {
            return string.Empty;
        }

        var lines = integrations
            .Take(MaxTooltipIntegrations)
            .Select(integration => UiText.Mods.IntegrationTooltipLine(
                Name(integration.TargetModId, displayNames),
                integration.Nature == ModIntegrationNature.Resolved));

        return UiText.Mods.IntegrationTooltip([.. lines]);
    }

    private static string Name(string modId, IReadOnlyDictionary<string, string> displayNames)
        => displayNames.TryGetValue(modId, out var name) && name.Length > 0 ? name : modId;
}