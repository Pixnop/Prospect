using Prospect.Core.ModDb;

namespace Prospect.Core.Diagnostics;

/// <summary>
/// Gravité d'une ligne de journal de jeu, dans l'ordre croissant : l'ordre naturel de
/// l'énumération sert de clé de tri, comme <see cref="InstanceDoctorSeverity"/>.
/// </summary>
public enum GameLogSeverity
{
    /// <summary>Notification, évènement, debug : rien à signaler.</summary>
    Info,

    /// <summary>Le jeu a écrit un avertissement attribuable à ce mod.</summary>
    Warning,

    /// <summary>Le jeu a écrit une erreur (ou une exception) attribuable à ce mod.</summary>
    Error,
}

/// <summary>
/// Identité d'un mod telle qu'un journal peut la nommer. Le jeu désigne un mod tantôt par son
/// <c>modid</c> (<c>[carryon] …</c>), tantôt par son nom de fichier quand le
/// <c>modinfo.json</c> n'a pas pu être lu (<c>[monmod-1.0.0.zip] …</c>), tantôt par le nom
/// affiché (<c>started 'Carry On' mod</c>) : les trois formes mènent au même mod, et cette
/// identité est ce qui permet de le reconnaître sous n'importe laquelle.
/// </summary>
/// <param name="ModId">Identifiant du mod, ou son nom de fichier à défaut : la clé du rapport.</param>
/// <param name="FileName">Nom du zip, si connu.</param>
/// <param name="DisplayName">Nom affiché du <c>modinfo.json</c>, si connu.</param>
public sealed record ModLogIdentity(string ModId, string? FileName = null, string? DisplayName = null);

/// <summary>
/// Ce que le journal du dernier lancement dit d'UN mod : combien d'erreurs et d'avertissements lui
/// sont attribuables, et quelques lignes en exemple.
/// </summary>
/// <param name="ModId">Clé d'attribution (voir <see cref="ModLogIdentity.ModId"/>).</param>
/// <param name="ErrorCount">Nombre d'entrées de niveau erreur ou fatal attribuées à ce mod.</param>
/// <param name="WarningCount">Nombre d'entrées de niveau avertissement attribuées à ce mod.</param>
/// <param name="Samples">
/// Les premières lignes en cause, dans l'ordre du journal et bornées par
/// <see cref="GameLogAnalyzer.MaxSamplesPerMod"/> : de quoi montrer DE QUOI on parle sans recopier
/// un journal entier dans une infobulle.
/// </param>
public sealed record ModLogInsight(string ModId, int ErrorCount, int WarningCount, IReadOnlyList<string> Samples)
{
    /// <summary>Verdict du mod : erreurs d'abord, avertissements ensuite, sain sinon.</summary>
    public GameLogSeverity Severity => ErrorCount > 0
        ? GameLogSeverity.Error
        : WarningCount > 0 ? GameLogSeverity.Warning : GameLogSeverity.Info;

    /// <summary>Vrai si rien n'est reproché à ce mod par le dernier lancement.</summary>
    public bool IsHealthy => ErrorCount == 0 && WarningCount == 0;
}

/// <summary>
/// Nature d'une intégration entre deux mods, telle que l'heuristique la voit. Aucune de ces
/// valeurs n'est un verdict de qualité : ce sont des OBSERVATIONS, jamais un blocage
/// (docs/architecture.md, niveau 3 des dépendances).
/// </summary>
public enum ModIntegrationNature
{
    /// <summary>
    /// Le mod cible est installé et le contenu visé a été trouvé : « fonctionne avec ».
    /// </summary>
    Resolved,

    /// <summary>
    /// Le contenu visé manque : soit le jeu l'a dit au dernier lancement (patch dont le fichier
    /// cible est introuvable), soit le mod cible n'est pas installé alors que le patch ne pose
    /// aucune condition. « Attend du contenu de … ».
    /// </summary>
    Missing,

    /// <summary>
    /// Intégration facultative annoncée par le zip (patch sous condition <c>dependsOn</c>) dont la
    /// cible n'est pas installée : le mod est CONÇU pour fonctionner avec, sans rien attendre.
    /// </summary>
    Optional,
}

/// <summary>
/// Une intégration observée entre deux mods : <paramref name="SourceModId"/> référence du contenu
/// de <paramref name="TargetModId"/>.
/// </summary>
/// <param name="SourceModId">Mod qui référence (celui dont la ligne s'affiche dans la liste).</param>
/// <param name="TargetModId">Domaine référencé, qui est le <c>modid</c> de l'autre mod.</param>
/// <param name="Nature">Résolue, manquante, ou facultative.</param>
/// <param name="Evidence">
/// La ligne de journal ou le chemin d'archive qui a produit ce signal, pour que l'infobulle puisse
/// montrer sur quoi l'heuristique s'appuie plutôt que de demander qu'on la croie sur parole.
/// </param>
public sealed record ModIntegration(string SourceModId, string TargetModId, ModIntegrationNature Nature, string Evidence);

/// <summary>
/// Ce qu'une lecture du journal de lancement a produit : un verdict par mod et les intégrations
/// que les lignes ont révélées. Purement descriptif, sans une phrase d'interface : la mise en mots
/// reste au Desktop (docs/architecture.md, séparation logique/interface).
/// </summary>
/// <param name="Mods">Un élément par mod auquel au moins une ligne a été attribuée.</param>
/// <param name="Integrations">Paires (source, cible) observées dans les lignes du journal.</param>
/// <param name="ObservedModIds">
/// Vocabulaire de mods reconnu dans le journal (mods chargés, mods refusés) : ce que le jeu a vu,
/// par opposition à ce que le dossier <c>Mods/</c> contient aujourd'hui.
/// </param>
public sealed record GameLogReport(
    IReadOnlyList<ModLogInsight> Mods,
    IReadOnlyList<ModIntegration> Integrations,
    IReadOnlyList<string> ObservedModIds)
{
    /// <summary>Rapport vide : aucun journal, ou un journal sans la moindre ligne exploitable.</summary>
    public static GameLogReport Empty { get; } = new([], [], []);
}

/// <summary>
/// Le produit fini pour une instance : le journal du dernier lancement croisé avec l'état actuel
/// du dossier <c>Mods/</c> (voir <see cref="GameLogInsightsService"/>). C'est ce que l'onglet Mods
/// consomme.
/// </summary>
/// <param name="AnalyzedUtc">Instant de l'analyse, pour horodater ce que l'écran affiche.</param>
/// <param name="HasLog">Faux si l'instance n'a jamais été lancée (aucun journal sur le disque).</param>
/// <param name="Mods">Verdicts par mod, uniquement pour ceux qui ont quelque chose à signaler.</param>
/// <param name="Integrations">Intégrations fusionnées : celles du journal et celles des archives.</param>
public sealed record InstanceLogInsights(
    DateTimeOffset AnalyzedUtc,
    bool HasLog,
    IReadOnlyList<ModLogInsight> Mods,
    IReadOnlyList<ModIntegration> Integrations)
{
    /// <summary>Résultat neutre : rien à afficher, aucune ligne à teinter.</summary>
    public static InstanceLogInsights None { get; } = new(DateTimeOffset.MinValue, false, [], []);

    /// <summary>
    /// Verdict connu pour ce mod installé, ou <see langword="null"/> s'il n'a rien à signaler. Les
    /// trois clés possibles sont essayées dans l'ordre où elles sont fiables : le <c>modid</c> du
    /// <c>modinfo.json</c>, l'identité de repli du mod, puis le nom de fichier, car c'est sous cette
    /// dernière forme que le jeu nomme un mod dont il n'a pas su lire les métadonnées.
    /// </summary>
    public ModLogInsight? FindMod(InstalledMod mod)
    {
        ArgumentNullException.ThrowIfNull(mod);

        return Match(mod, Mods, insight => insight.ModId);
    }

    /// <summary>Intégrations dont ce mod installé est la SOURCE, dans l'ordre où elles ont été observées.</summary>
    public IReadOnlyList<ModIntegration> FindIntegrations(InstalledMod mod)
    {
        ArgumentNullException.ThrowIfNull(mod);

        var keys = KeysOf(mod);

        return [.. Integrations.Where(integration => keys.Any(key => Same(key, integration.SourceModId)))];
    }

    private static TItem? Match<TItem>(InstalledMod mod, IReadOnlyList<TItem> items, Func<TItem, string> key)
        where TItem : class
    {
        foreach (var candidate in KeysOf(mod))
        {
            var found = items.FirstOrDefault(item => Same(key(item), candidate));
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    private static string[] KeysOf(InstalledMod mod)
        => mod.Info?.ModId is { Length: > 0 } modId
            ? [modId, mod.Identity, mod.FileName]
            : [mod.Identity, mod.FileName];

    private static bool Same(string left, string right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}