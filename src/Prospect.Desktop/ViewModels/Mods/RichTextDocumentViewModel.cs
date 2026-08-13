using Avalonia.Media.Imaging;

using CommunityToolkit.Mvvm.ComponentModel;

using Prospect.Core.Common;
using Prospect.Core.ModDb;
using Prospect.Desktop.Services;

namespace Prospect.Desktop.ViewModels.Mods;

/// <summary>
/// Une description de fiche prête à rendre : les blocs produits par
/// <see cref="HtmlRichTextParser"/>, plus les deux choses que le modèle pur ne peut pas porter,
/// l'ouverture d'un lien et le chargement d'une image.
/// </summary>
/// <remarks>
/// La séparation est volontaire et suit docs/architecture.md : l'ANALYSE du HTML est du calcul,
/// donc dans <c>Prospect.Core</c> où elle se teste sans interface ; le RENDU en contrôles est de
/// l'Avalonia, donc dans <c>Prospect.Desktop</c> (voir <c>Controls.RichTextPresenter</c>). Ce
/// ViewModel est la charnière : il ne sait rien du dessin, il ne fait que rattacher un service à
/// chaque bloc qui en réclame un.
/// </remarks>
public sealed class RichTextDocumentViewModel : IDisposable
{
    private readonly IExternalUrlOpener _urlOpener;
    private readonly Dictionary<RichTextImage, RichTextImageViewModel> _images = [];

    /// <summary>Construit la description.</summary>
    /// <param name="document">Blocs analysés.</param>
    /// <param name="urlOpener">Ouverture des liens dans le navigateur du système.</param>
    /// <param name="images">Cache d'images partagé avec les logos de cartes.</param>
    /// <param name="imageWidth">
    /// Largeur de la colonne de lecture, en pixels : c'est à cette taille que les illustrations
    /// sont décodées, jamais à la résolution d'origine.
    /// </param>
    public RichTextDocumentViewModel(
        RichTextDocument document,
        IExternalUrlOpener urlOpener,
        IModLogoCache images,
        int imageWidth)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(urlOpener);
        ArgumentNullException.ThrowIfNull(images);

        Document = document;
        _urlOpener = urlOpener;

        foreach (var image in Collect(document.Blocks))
        {
            // Deux <img> de même source et même lien sont le même record, donc la même entrée :
            // une bannière répétée en tête et en pied ne se télécharge pas deux fois.
            if (!_images.ContainsKey(image))
            {
                _images[image] = new RichTextImageViewModel(image, images, imageWidth);
            }
        }
    }

    /// <summary>Blocs à rendre, dans l'ordre du document.</summary>
    public RichTextDocument Document { get; }

    /// <summary>Vrai quand il n'y a rien à afficher.</summary>
    public bool IsEmpty => Document.IsEmpty;

    /// <summary>Illustrations du document, une par source distincte.</summary>
    public IReadOnlyCollection<RichTextImageViewModel> Images => _images.Values;

    /// <summary>Le ViewModel d'image d'un bloc, jamais <see langword="null"/> pour un bloc de ce document.</summary>
    public RichTextImageViewModel ImageFor(RichTextImage block)
    {
        ArgumentNullException.ThrowIfNull(block);

        return _images.TryGetValue(block, out var image)
            ? image
            : throw new KeyNotFoundException($"L'image « {block.Source} » n'appartient pas à cette description.");
    }

    /// <summary>
    /// Ouvre un lien de la description. Toujours dans le navigateur du système, jamais dans
    /// Prospect : le launcher n'a aucune navigation interne, et le contenu d'une fiche vient d'un
    /// tiers.
    /// </summary>
    public void OpenLink(Uri target)
    {
        ArgumentNullException.ThrowIfNull(target);

        _ = _urlOpener.OpenAsync(target, CancellationToken.None);
    }

    /// <summary>Annule les chargements d'images encore en vol. Ne libère AUCUN bitmap : ils appartiennent au cache.</summary>
    public void Dispose()
    {
        foreach (var image in _images.Values)
        {
            image.Dispose();
        }
    }

    private static IEnumerable<RichTextImage> Collect(IEnumerable<RichTextBlock> blocks)
    {
        foreach (var block in blocks)
        {
            switch (block)
            {
                case RichTextImage image:
                    yield return image;

                    break;

                case RichTextList list:
                    foreach (var item in list.Items)
                    {
                        foreach (var nested in Collect(item.Children))
                        {
                            yield return nested;
                        }
                    }

                    break;

                default:
                    break;
            }
        }
    }

}

/// <summary>
/// Une illustration de description, chargée par la même discipline que les vignettes de cartes.
/// </summary>
/// <remarks>
/// Rien n'est réinventé ici : c'est <see cref="IModLogoCache"/> qui télécharge, borne les
/// téléchargements simultanés, décode À LA TAILLE D'USAGE, plafonne le nombre d'entrées et ne
/// libère jamais un bitmap distribué. Ce dernier point est une correction née d'un plantage réel
/// (un <c>Image.Source</c> pointant vers un bitmap libéré lève dans la passe de mise en page
/// suivante), et il vaut ici exactement comme sur une carte : <see cref="Dispose"/> annule le
/// chargement, il ne touche pas au bitmap.
/// </remarks>
public sealed partial class RichTextImageViewModel : ObservableObject, IDisposable
{
    private readonly CancellationTokenSource _cancellation = new();
    private readonly Task _loadTask;

    /// <summary>Construit l'illustration et lance son chargement.</summary>
    /// <param name="block">Bloc image analysé.</param>
    /// <param name="images">Cache d'images.</param>
    /// <param name="maxWidth">Largeur d'affichage maximale, en pixels.</param>
    public RichTextImageViewModel(RichTextImage block, IModLogoCache images, int maxWidth)
    {
        ArgumentNullException.ThrowIfNull(block);
        ArgumentNullException.ThrowIfNull(images);

        Source = block.Source;
        Link = block.Link;
        AlternateText = block.AlternateText.Length > 0 ? block.AlternateText : block.Source.Segments[^1];
        _loadTask = LoadAsync(images, maxWidth);
    }

    /// <summary>URL de l'image.</summary>
    public Uri Source { get; }

    /// <summary>Lien porté par l'image, quand elle en a un.</summary>
    public Uri? Link { get; }

    /// <summary>Texte de remplacement, montré tant que l'image n'est pas là ou si elle ne vient jamais.</summary>
    public string AlternateText { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBitmap))]
    private Bitmap? _bitmap;

    /// <summary>Vrai une fois l'image décodée : la vue bascule alors du texte de remplacement vers l'image.</summary>
    public bool HasBitmap => Bitmap is not null;

    /// <summary>Le chargement en vol, pour que les tests headless l'attendent sans sondage arbitraire.</summary>
    internal Task LoadCompletion => _loadTask;

    /// <summary>Annule un chargement encore en vol. Ne libère PAS le bitmap, qui appartient au cache.</summary>
    public void Dispose()
    {
        _cancellation.Cancel();
        _cancellation.Dispose();
    }

    private async Task LoadAsync(IModLogoCache images, int maxWidth)
    {
        try
        {
            Bitmap = await images.GetAsync(Source, maxWidth, _cancellation.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // Fiche refermée avant la fin du téléchargement : il n'y a plus rien à afficher.
        }
    }
}