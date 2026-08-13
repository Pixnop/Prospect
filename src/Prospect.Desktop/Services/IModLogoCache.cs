using Avalonia.Media.Imaging;

namespace Prospect.Desktop.Services;

/// <summary>
/// Cache mémoire des logos affichés par les cartes du navigateur de mods
/// (design/ui_kits/launcher/screen-mods.jsx, zone visuelle de <c>ModCard</c>). Volontairement PAS
/// <see cref="Prospect.Core.Http.IDownloadManager"/> : celui-ci est taillé pour de gros fichiers,
/// avec file d'attente visible dans le popover Téléchargements, reprise par <c>Range</c> et
/// vérification de checksum — tout l'inverse d'une vignette décorative de quelques dizaines de Ko
/// qui doit rester invisible de cette file et échouer en silence.
/// </summary>
public interface IModLogoCache
{
    /// <summary>
    /// Récupère le logo à <paramref name="logoUrl"/>, depuis le cache mémoire si déjà téléchargé et
    /// décodé avec succès lors d'un appel précédent. Ne lève jamais pour un échec réseau ou un
    /// contenu non décodable comme une image : rend <see langword="null"/> dans les deux cas, pour
    /// que la carte retombe sur son pictogramme générique plutôt que d'afficher une erreur.
    /// </summary>
    /// <param name="logoUrl">URL absolue du logo, telle qu'exposée par <c>ModDbModSummary.LogoUrl</c>.</param>
    /// <param name="cancellationToken">
    /// Annulation côté appelant (une carte remplacée avant la fin du chargement, voir
    /// <c>ModCardViewModel.Dispose</c>) : dans ce seul cas, <see cref="OperationCanceledException"/>
    /// est bien relancée, à distinguer d'un simple échec réseau.
    /// </param>
    Task<Bitmap?> GetAsync(Uri logoUrl, CancellationToken cancellationToken = default);

    /// <summary>
    /// Même contrat, pour une image affichée PLUS GRANDE qu'une vignette de carte : les
    /// illustrations d'une description de fiche, larges comme la colonne de lecture.
    /// </summary>
    /// <remarks>
    /// La largeur d'usage est un paramètre plutôt qu'une constante parce que c'est elle, et rien
    /// d'autre, qui décide du coût mémoire : une image est décodée puis RÉDUITE à cette largeur,
    /// et c'est cette réduction qui a évité l'épuisement mémoire mesuré sur le catalogue réel. Une
    /// seule infrastructure sert les deux usages, avec la même borne de téléchargements
    /// simultanés, le même plafond d'entrées et la même règle absolue de ne jamais libérer un
    /// bitmap déjà distribué (voir <c>ModLogoCache</c>).
    /// </remarks>
    /// <param name="imageUrl">URL absolue de l'image, telle que l'écrit l'auteur de la fiche.</param>
    /// <param name="maxWidth">Largeur d'affichage maximale, en pixels. Une image déjà plus petite n'est jamais agrandie.</param>
    /// <param name="cancellationToken">Annulation côté appelant.</param>
    Task<Bitmap?> GetAsync(Uri imageUrl, int maxWidth, CancellationToken cancellationToken = default);
}