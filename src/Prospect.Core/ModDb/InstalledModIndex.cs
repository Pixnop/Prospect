using Prospect.Core.Common;

namespace Prospect.Core.ModDb;

/// <summary>
/// Ce qu'une fiche de catalogue correspond, ou non, à un mod déjà présent dans une instance.
/// </summary>
/// <param name="Identity">Identifiant du mod installé (<c>modid</c> du modinfo, ou repli).</param>
/// <param name="Version">Version installée, <see langword="null"/> si l'archive est illisible et sans provenance.</param>
/// <param name="IsEnabled">Faux quand le zip est présent mais désactivé.</param>
public sealed record InstalledModMatch(string Identity, ModVersion? Version, bool IsEnabled);

/// <summary>
/// Rapproche le catalogue ModDB de ce qui est réellement dans <c>data/Mods/</c> d'une instance.
///
/// Le rapprochement n'est pas évident, et c'est pour cela qu'il vit ici plutôt que dans un
/// ViewModel. Deux clés cohabitent et aucune n'est disponible partout : l'identifiant NUMÉRIQUE du
/// ModDB, que seule la provenance enregistre (donc absent d'un zip déposé à la main), et le
/// <c>modid</c> TEXTUEL du modinfo.json, que la fiche de catalogue peut exposer en plusieurs
/// exemplaires ou pas du tout (outils externes). L'index essaie les deux, dans cet ordre : le
/// numérique quand Prospect a posé le fichier lui-même, le textuel sinon.
/// </summary>
public sealed class InstalledModIndex
{
    private readonly Dictionary<int, InstalledModMatch> _byModDbId = [];
    private readonly Dictionary<string, InstalledModMatch> _byIdentity = new(StringComparer.OrdinalIgnoreCase);

    private InstalledModIndex()
    {
    }

    /// <summary>Index vide : aucune instance ciblée, ou scan impossible.</summary>
    public static InstalledModIndex Empty { get; } = new();

    /// <summary>Vrai quand l'index ne connaît aucun mod installé.</summary>
    public bool IsEmpty => _byModDbId.Count == 0 && _byIdentity.Count == 0;

    /// <summary>Construit l'index à partir d'un scan de mods installés.</summary>
    public static InstalledModIndex From(IEnumerable<InstalledMod> installedMods)
    {
        ArgumentNullException.ThrowIfNull(installedMods);

        var index = new InstalledModIndex();
        foreach (var mod in installedMods)
        {
            var match = new InstalledModMatch(mod.Identity, mod.Version, mod.IsEnabled);

            if (mod.Provenance is { } provenance)
            {
                index._byModDbId[provenance.ModId] = match;
                // La provenance connaît aussi le modid textuel, y compris quand l'archive elle-même
                // est illisible : c'est ce qui garde le rapprochement possible sur un zip cassé.
                index._byIdentity[provenance.ModIdString] = match;
            }

            index._byIdentity[mod.Identity] = match;
        }

        return index;
    }

    /// <summary>
    /// Cherche le mod installé qui correspond à une fiche de catalogue.
    /// </summary>
    /// <param name="modDbModId">Identifiant numérique de la fiche.</param>
    /// <param name="modIdStrings">Identifiants modinfo rattachés à la fiche (souvent un, parfois zéro ou plusieurs).</param>
    /// <returns>Le mod installé correspondant, ou <see langword="null"/>.</returns>
    public InstalledModMatch? Find(int modDbModId, IReadOnlyList<string>? modIdStrings)
    {
        if (_byModDbId.TryGetValue(modDbModId, out var byId))
        {
            return byId;
        }

        if (modIdStrings is null)
        {
            return null;
        }

        foreach (var identity in modIdStrings)
        {
            if (_byIdentity.TryGetValue(identity, out var byIdentity))
            {
                return byIdentity;
            }
        }

        return null;
    }
}