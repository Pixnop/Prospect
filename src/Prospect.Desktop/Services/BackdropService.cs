using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

using CommunityToolkit.Mvvm.ComponentModel;

using Prospect.Core.Settings;

namespace Prospect.Desktop.Services;

/// <summary>
/// Traduit la clé de fond persistée par <see cref="SettingsService"/>
/// (<see cref="ProspectSettings.Backdrop"/>, vocabulaire <see cref="BackdropCatalog"/>) en une
/// image effectivement affichable, et notifie quand elle change. Pendant de
/// <see cref="ThemeService"/> pour le troisième réglage d'apparence, et le seul point du Desktop à
/// décoder les assets de fond.
/// </summary>
/// <remarks>
/// <para>
/// Décision d'architecture : le fond s'applique À CHAUD, à la différence de la langue
/// (docs/architecture.md, section « Fond de fenêtre »). La raison est de nature technique, pas de
/// périmètre : un fond est UNE SOURCE D'IMAGE, c'est-à-dire une propriété liable qui se relit
/// quand elle notifie, là où un texte statique est un <c>{StaticResource}</c> résolu une fois pour
/// toutes à la construction du contrôle.
/// </para>
/// <para>
/// Même facture que <see cref="ThemeService"/> sur le point qui compte : CONSTRUIRE ce service ne
/// fait rien. Il ne décode aucune image, ne touche à aucun état applicatif, et se contente
/// d'armer un abonnement à <see cref="SettingsService.Changed"/>. Le premier décodage a lieu à la
/// première lecture de <see cref="Source"/>, c'est-à-dire quand une vue affiche réellement le
/// fond — un test qui résout le conteneur DI sans jamais montrer de fenêtre ne paie donc rien.
/// </para>
/// <para>
/// Aucun <c>Bitmap</c> n'est JAMAIS libéré ici, et c'est la même règle que celle de
/// <see cref="Prospect.Desktop.Services.ModLogoCache"/> : un <c>Image.Source</c> qui pointe vers un
/// bitmap disposé fait lever une <c>NullReferenceException</c> dans la passe de rendu suivante, et
/// rien ici ne peut savoir si la fenêtre a déjà repeint. Changer de fond remplace donc la
/// référence et laisse le ramasse-miettes reprendre la mémoire native du précédent. Ce que ça
/// coûte est borné : une seule image pleine taille vivante à la fois (les autres deviennent
/// injoignables au changement suivant), plus les vignettes du sélecteur, décodées à
/// <see cref="ThumbnailDecodeWidth"/> points de large et gardées pour la session — onze vignettes
/// réduites tiennent dans quelques mégaoctets, et les redécoder à chaque ouverture de l'écran
/// Réglages ferait clignoter la grille.
/// </para>
/// </remarks>
public sealed class BackdropService : ObservableObject
{
    /// <summary>Dossier d'assets où vit un fichier par clé du <see cref="BackdropCatalog"/>.</summary>
    public const string AssetDirectory = "avares://Prospect.Desktop/Assets/Backdrops";

    /// <summary>
    /// Largeur de décodage des vignettes du sélecteur, en pixels. Le double des 160 points
    /// d'affichage, pour rester net sur un écran à densité doublée sans porter les 1920 points de
    /// l'image entière (onze fois).
    /// </summary>
    public const int ThumbnailDecodeWidth = 320;

    private readonly SettingsService _settings;
    private readonly Dictionary<string, Bitmap> _thumbnails = new(StringComparer.Ordinal);

    private string _key;
    private Bitmap? _source;

    public BackdropService(SettingsService settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        _settings = settings;
        _key = ProspectSettings.NormalizeBackdrop(settings.Current.Backdrop);

        // Abonnement passif : il arme un futur callback, il ne décode rien tout de suite (voir la
        // remarque de la classe). C'est lui qui porte l'application à chaud — l'écran Réglages
        // n'appelle jamais ce service, il écrit le réglage et le reste suit.
        _settings.Changed += OnSettingsChanged;
    }

    /// <summary>
    /// Clé du fond actuellement en vigueur, toujours une clé du <see cref="BackdropCatalog"/>
    /// (le réglage est normalisé, voir <see cref="ProspectSettings.NormalizeBackdrop"/>).
    /// </summary>
    public string Key
    {
        get => _key;
        private set
        {
            if (SetProperty(ref _key, value))
            {
                _source = null;
                OnPropertyChanged(nameof(Source));
            }
        }
    }

    /// <summary>
    /// Image de fond courante, celle que l'<c>Image</c> de <c>MainWindow</c> affiche sous le voile
    /// et le grain. Décodée à la première lecture puis mémorisée jusqu'au prochain changement de
    /// réglage.
    /// </summary>
    public IImage Source => _source ??= Decode(AssetUriFor(_key), decodeWidth: null);

    /// <summary>URI de l'asset correspondant à <paramref name="key"/>, replis compris.</summary>
    public static Uri AssetUriFor(string? key)
        => new($"{AssetDirectory}/{ProspectSettings.NormalizeBackdrop(key)}.jpg");

    /// <summary>
    /// Vignette réduite du fond <paramref name="key"/>, pour la grille du sélecteur. Mémorisée par
    /// clé : la deuxième ouverture de l'écran Réglages ne redécode rien.
    /// </summary>
    public IImage Thumbnail(string? key)
    {
        var normalized = ProspectSettings.NormalizeBackdrop(key);

        if (!_thumbnails.TryGetValue(normalized, out var thumbnail))
        {
            thumbnail = Decode(AssetUriFor(normalized), ThumbnailDecodeWidth);
            _thumbnails[normalized] = thumbnail;
        }

        return thumbnail;
    }

    private void OnSettingsChanged(object? sender, ProspectSettings current)
        => Key = ProspectSettings.NormalizeBackdrop(current.Backdrop);

    private static Bitmap Decode(Uri uri, int? decodeWidth)
    {
        using var stream = AssetLoader.Open(uri);

        // DecodeToWidth plutôt qu'un décodage complet suivi d'une mise à l'échelle : le décodeur ne
        // matérialise jamais les 1920x1080 points, ce qui est tout l'intérêt pour une vignette.
        return decodeWidth is { } width ? Bitmap.DecodeToWidth(stream, width) : new Bitmap(stream);
    }
}