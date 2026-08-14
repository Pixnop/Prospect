using Avalonia.Media.Imaging;

using CommunityToolkit.Mvvm.ComponentModel;

using Prospect.Desktop.Services;

namespace Prospect.Desktop.ViewModels.Mods;

/// <summary>
/// La vignette d'un mod, pour tous les écrans qui le nomment sans être le navigateur : rangées de
/// l'onglet Mods d'une instance, en-tête et dépendances des dialogues d'installation et de mise à
/// jour, confirmation de retrait.
/// </summary>
/// <remarks>
/// <para>
/// Le navigateur, lui, ne passe pas par ici : sa carte tient déjà l'entrée de catalogue, donc
/// l'URL du logo, et n'a rien à demander à personne (voir <see cref="ModCardViewModel"/>, dont ce
/// type reprend le comportement à la lettre — chargement en vol jamais attendu, annulation à la
/// disposition, bitmap jamais libéré parce qu'il appartient au cache).
/// </para>
/// <para>
/// Deux paliers de dégradation, tous deux silencieux et tous deux rendus par le MÊME pictogramme
/// générique : pas d'identifiant de fiche (mod déposé à la main, dépendance introuvable sur le
/// ModDB), ou pas de pixels (catalogue jamais relevé, fiche sans logo, réseau coupé). Aucun n'est
/// une erreur et aucun ne réserve de place vide : la tuile garde sa taille et son pictogramme.
/// </para>
/// <para>
/// La largeur de décodage n'est PAS un paramètre, et c'est délibéré. La clé du cache est
/// <c>largeur|url</c> : chaque largeur nouvelle est une entrée de plus pour la même image, donc un
/// budget de vignettes consommé deux fois. Tout ce qui passe par ici décode à
/// <see cref="ModLogoCache.MaxLogoWidth"/>, exactement comme la carte du navigateur, et laisse la
/// vue afficher le résultat à la taille qui convient à sa rangée.
/// </para>
/// </remarks>
public sealed partial class ModThumbnailViewModel : ObservableObject, IDisposable
{
    private readonly CancellationTokenSource _cancellation = new();
    private readonly Task _loadTask = Task.CompletedTask;

    private bool _disposed;

    /// <summary>Construit la vignette et lance son chargement s'il y a une fiche à interroger.</summary>
    /// <param name="modDbModId">
    /// Identifiant numérique de la fiche ModDB, ou <see langword="null"/> quand le mod n'en a pas :
    /// un zip déposé à la main dans <c>data/Mods/</c> n'a aucune provenance, et une dépendance que
    /// le ModDB ne publie pas n'a aucune fiche. Les deux gardent le pictogramme générique, qui est
    /// un état honnête et non un défaut d'affichage.
    /// </param>
    /// <param name="directory">Annuaire des logos, adossé au catalogue mémorisé.</param>
    /// <param name="logoCache">Cache d'images, seul chemin de téléchargement et de décodage.</param>
    public ModThumbnailViewModel(int? modDbModId, IModLogoDirectory directory, IModLogoCache logoCache)
    {
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(logoCache);

        if (modDbModId is { } modId and > 0)
        {
            _loadTask = LoadAsync(modId, directory, logoCache);
        }
    }

    // Construction de None : marquée disposée d'entrée, parce que cette instance est PARTAGÉE par
    // toutes les surfaces qui n'ont rien à montrer. Les rangées la disposent comme les autres à
    // chaque rescan, et il ne faut surtout pas que le premier rescan ferme le jeton d'une valeur
    // que le suivant réutilisera.
    private ModThumbnailViewModel() => _disposed = true;

    /// <summary>
    /// Vignette qui n'a rien à charger et ne le sera jamais : le repli des surfaces qui ne
    /// connaissent aucune fiche, et le défaut des constructions de test.
    /// </summary>
    public static ModThumbnailViewModel None { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLogo))]
    private Bitmap? _bitmap;

    /// <summary>Vrai une fois les pixels obtenus : la vue bascule alors du pictogramme vers l'image.</summary>
    public bool HasLogo => Bitmap is not null;

    /// <summary>
    /// Le chargement en vol, ou <see cref="Task.CompletedTask"/>. Jamais attendu en production :
    /// exposé pour que les tests headless inspectent le rendu après coup plutôt qu'au petit bonheur
    /// (voir <c>InternalsVisibleTo</c>).
    /// </summary>
    internal Task LoadCompletion => _loadTask;

    /// <summary>
    /// Annule un chargement encore en vol. Ne libère PAS <see cref="Bitmap"/> : il appartient au
    /// cache, qui le sert à toutes les surfaces qui montrent le même mod, et libérer un bitmap déjà
    /// distribué fait lever à la passe de mise en page suivante (voir <see cref="ModLogoCache"/>).
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _cancellation.Cancel();
        _cancellation.Dispose();
    }

    // Jamais bloquant pour la construction de la rangée, et jamais une exception qui remonterait
    // jusqu'au rafraîchissement de la liste : la liste des mods installés vient d'un scan DISQUE,
    // elle doit s'afficher entière que le réseau réponde ou non.
    private async Task LoadAsync(int modDbModId, IModLogoDirectory directory, IModLogoCache logoCache)
    {
        try
        {
            if (await directory.FindAsync(modDbModId, _cancellation.Token).ConfigureAwait(true) is not { } logoUrl)
            {
                return;
            }

            Bitmap = await logoCache.GetAsync(logoUrl, _cancellation.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
        }
    }
}