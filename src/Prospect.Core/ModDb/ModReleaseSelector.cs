using Prospect.Core.Common;

namespace Prospect.Core.ModDb;

/// <summary>Jusqu'où accepter de chercher une release compatible avec la version de jeu d'une instance.</summary>
public enum ModCompatibilityMode
{
    /// <summary>
    /// Seules les releases dont l'auteur a coché EXACTEMENT cette version de jeu. C'est le mode sûr
    /// et le défaut.
    /// </summary>
    ExactGameVersion,

    /// <summary>
    /// À défaut d'exact, accepter une release taguée sur une autre version de la même série
    /// <c>Major.Minor</c>. APPROXIMATIF : ça revient à parier que l'auteur a oublié de cocher la
    /// version, hypothèse plausible (les tags sont des cases à cocher manuelles) mais jamais
    /// vérifiée. L'UI doit le signaler comme tel.
    /// </summary>
    WidenToMinorSeries,
}

/// <summary>La release retenue, et comment elle l'a été.</summary>
/// <param name="Release">Release choisie.</param>
/// <param name="IsApproximate">Vrai si le choix vient de l'élargissement à la série mineure.</param>
public sealed record ModReleaseChoice(ModDbRelease Release, bool IsApproximate);

/// <summary>
/// Choisit la release à installer dans une instance. Pur calcul sur les tags de compatibilité
/// déclarés par l'auteur.
/// </summary>
/// <remarks>
/// La règle repose entièrement sur <c>releases[].tags</c>, jamais sur le <c>modinfo.json</c>
/// embarqué : ces tags sont des cases cochées à la main sur le site au moment de l'upload
/// (<c>edit-release.php</c>), pré-suggérées depuis le modinfo mais jamais imposées. C'est du
/// déclaratif humain, ce qui explique à la fois pourquoi il fait autorité (c'est l'auteur qui
/// affirme la compatibilité) et pourquoi le mode élargi existe (l'auteur oublie parfois de cocher).
/// </remarks>
public static class ModReleaseSelector
{
    /// <summary>
    /// La release la plus récente compatible avec <paramref name="gameVersion"/>, ou
    /// <see langword="null"/> si aucune ne l'est.
    /// </summary>
    /// <param name="releases">Releases publiées du mod.</param>
    /// <param name="gameVersion">Version de jeu de l'instance cible.</param>
    /// <param name="mode">Strictement exact, ou élargi à la série mineure en second recours.</param>
    public static ModReleaseChoice? Select(
        IReadOnlyList<ModDbRelease> releases,
        GameVersion gameVersion,
        ModCompatibilityMode mode = ModCompatibilityMode.ExactGameVersion)
    {
        var candidates = SelectAll(releases, gameVersion, mode);

        return candidates.Count > 0 ? candidates[0] : null;
    }

    /// <summary>
    /// TOUTES les releases retenues par le même filtre que <see cref="Select"/>, dans le même ordre
    /// de priorité : la première est exactement celle que <see cref="Select"/> aurait choisie.
    /// </summary>
    /// <remarks>
    /// Cette liste est ce que le dialogue d'installation propose. Elle existe parce qu'installer
    /// « la meilleure release compatible » n'est pas toujours ce qu'on veut : un serveur figé sur
    /// une release précise, une régression dans la dernière publication, un modpack à reproduire.
    /// Les exactes précèdent TOUJOURS les approximatives, même quand une approximative porte un
    /// numéro plus élevé : une compatibilité affirmée par l'auteur vaut mieux qu'une compatibilité
    /// supposée par nous, et c'est déjà l'arbitrage que faisait <see cref="Select"/>.
    /// </remarks>
    /// <param name="releases">Releases publiées du mod.</param>
    /// <param name="gameVersion">Version de jeu de l'instance cible.</param>
    /// <param name="mode">Strictement exact, ou élargi à la série mineure en second recours.</param>
    public static IReadOnlyList<ModReleaseChoice> SelectAll(
        IReadOnlyList<ModDbRelease> releases,
        GameVersion gameVersion,
        ModCompatibilityMode mode = ModCompatibilityMode.ExactGameVersion)
    {
        ArgumentNullException.ThrowIfNull(releases);

        var exact = Ordered(releases.Where(release => release.CompatibleGameVersions.Contains(gameVersion)))
            .Select(release => new ModReleaseChoice(release, IsApproximate: false))
            .ToList();

        if (mode != ModCompatibilityMode.WidenToMinorSeries)
        {
            return exact;
        }

        var seen = exact.Select(choice => choice.Release.ReleaseId).ToHashSet();
        var widened = Ordered(releases
                .Where(release => !seen.Contains(release.ReleaseId))
                .Where(release => release.CompatibleGameVersions.Any(candidate => IsSameMinorSeries(candidate, gameVersion))))
            .Select(release => new ModReleaseChoice(release, IsApproximate: true));

        return [.. exact, .. widened];
    }

    /// <summary>Vrai si les deux versions partagent la même série <c>Major.Minor</c>.</summary>
    public static bool IsSameMinorSeries(GameVersion left, GameVersion right)
        => left.Major == right.Major && left.Minor == right.Minor;

    // À versions égales, la release au plus grand identifiant gagne : c'est la plus récemment
    // publiée, ce qui n'arrive qu'en cas de republication d'une même version.
    private static IEnumerable<ModDbRelease> Ordered(IEnumerable<ModDbRelease> releases)
        => releases
            .OrderByDescending(release => release.Version)
            .ThenByDescending(release => release.ReleaseId);
}