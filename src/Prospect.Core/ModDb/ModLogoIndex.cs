namespace Prospect.Core.ModDb;

/// <summary>
/// Table des logos du catalogue, indexée par l'identifiant numérique de fiche : la seule clé que
/// porte la provenance d'un mod installé (<see cref="ModProvenance.ModId"/>) et la seule que porte
/// un élément de plan d'installation (<see cref="ModInstallItem.ModDbModId"/>).
/// </summary>
/// <remarks>
/// <para>
/// Du calcul pur, construit une fois pour toutes à partir d'un catalogue déjà obtenu. Il existe
/// parce que les écrans qui ne SONT PAS le navigateur de mods (l'onglet Mods d'une instance, les
/// dialogues d'installation, de mise à jour et de retrait) nomment des mods sans jamais avoir vu
/// l'entrée de catalogue correspondante : ils connaissent un identifiant, pas une fiche. Chercher
/// linéairement dans les huit mille entrées à chaque rangée serait le seul autre chemin.
/// </para>
/// <para>
/// N'indexe QUE les fiches qui annoncent un logo : un tiers du catalogue n'en a pas
/// (docs/research/moddb-api.md), et les faire entrer avec une valeur nulle ferait porter à cette
/// table un tiers d'entrées qui ne répondent jamais rien.
/// </para>
/// <para>
/// Ne connaît volontairement pas le <c>modid</c> textuel. Un zip déposé à la main porte bien un
/// <c>modid</c> qu'on pourrait rapprocher d'une fiche, mais ce rapprochement serait une SUPPOSITION :
/// rien ne prouve que le fichier vienne de la fiche qui revendique cet identifiant, et l'onglet Mods
/// distingue justement, badge à l'appui, ce que Prospect a installé de ce qu'il a trouvé là. Une
/// vignette pour tout le monde effacerait cette distinction.
/// </para>
/// </remarks>
public sealed class ModLogoIndex
{
    private readonly Dictionary<int, Uri> _logosByModId;

    private ModLogoIndex(Dictionary<int, Uri> logosByModId) => _logosByModId = logosByModId;

    /// <summary>Table vide : ce que rend un catalogue jamais obtenu, et ce qui n'affiche aucune vignette.</summary>
    public static ModLogoIndex Empty { get; } = new([]);

    /// <summary>Nombre de fiches indexées, c'est-à-dire de fiches qui annoncent un logo.</summary>
    public int Count => _logosByModId.Count;

    /// <summary>Construit la table depuis les entrées d'un catalogue.</summary>
    /// <param name="mods">Entrées du catalogue, telles que les rend <see cref="IModDbClient.GetCatalogAsync"/>.</param>
    /// <remarks>
    /// Les doublons d'identifiant ne peuvent pas exister côté API (c'est une clé primaire), mais le
    /// premier gagne plutôt que de lever : cette table est un confort d'affichage, elle n'a aucune
    /// raison de faire tomber l'écran qui l'a demandée.
    /// </remarks>
    public static ModLogoIndex Build(IReadOnlyList<ModDbModSummary> mods)
    {
        ArgumentNullException.ThrowIfNull(mods);

        var logos = new Dictionary<int, Uri>(mods.Count);
        foreach (var mod in mods)
        {
            if (mod.LogoUrl is { } logoUrl)
            {
                logos.TryAdd(mod.ModId, logoUrl);
            }
        }

        return new ModLogoIndex(logos);
    }

    /// <summary>
    /// Le logo de cette fiche, ou <see langword="null"/> si elle n'en annonce pas, si elle n'est pas
    /// dans le catalogue indexé, ou si l'identifiant n'en est pas un.
    /// </summary>
    /// <param name="modId">Identifiant numérique de fiche.</param>
    public Uri? Find(int modId) => _logosByModId.GetValueOrDefault(modId);
}