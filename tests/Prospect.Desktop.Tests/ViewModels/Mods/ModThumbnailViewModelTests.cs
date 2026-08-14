using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;

using Prospect.Desktop.Services;
using Prospect.Desktop.Tests.TestDoubles;
using Prospect.Desktop.ViewModels.Mods;

using Shouldly;

namespace Prospect.Desktop.Tests.ViewModels.Mods;

/// <summary>
/// La vignette partagée par toutes les surfaces qui nomment un mod sans être le navigateur. Deux
/// paliers de dégradation à vérifier : pas d'identifiant de fiche, et pas de pixels.
/// </summary>
public sealed class ModThumbnailViewModelTests
{
    private const string LogoUrl = "https://moddbcdn.vintagestory.at/betterruins.png";

    /// <summary>
    /// Un mod déposé à la main n'a aucune provenance, donc aucun identifiant de fiche : la vignette
    /// ne demande rien à personne, et la vue garde son pictogramme. C'est un état honnête, pas un
    /// défaut d'affichage.
    /// </summary>
    [AvaloniaFact]
    public async Task SansIdentifiantDeFiche_NeDemandeRien()
    {
        var directory = new FakeModLogoDirectory((792, LogoUrl));
        var cache = new FakeModLogoCache();

        using var thumbnail = new ModThumbnailViewModel(null, directory, cache);
        await thumbnail.LoadCompletion;

        directory.RequestedModIds.ShouldBeEmpty();
        cache.CallCount.ShouldBe(0);
        thumbnail.HasLogo.ShouldBeFalse();
    }

    [AvaloniaFact]
    public async Task AvecUnLogoAuCatalogue_LeDemandeAuCacheEtBascule()
    {
        var directory = new FakeModLogoDirectory((792, LogoUrl));
        var cache = new FakeModLogoCache(TinyPng.Decode());

        using var thumbnail = new ModThumbnailViewModel(792, directory, cache);
        await thumbnail.LoadCompletion;

        directory.RequestedModIds.ShouldBe([792]);
        cache.LastUrl.ShouldBe(new Uri(LogoUrl));
        thumbnail.HasLogo.ShouldBeTrue();
        thumbnail.Bitmap.ShouldBeOfType<Bitmap>();
    }

    /// <summary>
    /// La clé du cache est <c>largeur|url</c> : décoder à une largeur inédite ferait une entrée de
    /// plus pour la même image, donc un budget de vignettes consommé deux fois. Tout ce qui passe
    /// par ici décode à la largeur de la carte du navigateur, quelle que soit la taille d'affichage.
    /// </summary>
    [AvaloniaFact]
    public async Task DecodeALaMemeLargeurQueLaCarteDuNavigateur()
    {
        var cache = new FakeModLogoCache(TinyPng.Decode());

        using var thumbnail = new ModThumbnailViewModel(792, new FakeModLogoDirectory((792, LogoUrl)), cache);
        await thumbnail.LoadCompletion;

        cache.LastMaxWidth.ShouldBe(ModLogoCache.MaxLogoWidth);
    }

    /// <summary>Fiche connue mais sans logo : rien n'est demandé au cache, et rien ne s'affiche.</summary>
    [AvaloniaFact]
    public async Task FicheSansLogo_NInterrogePasLeCacheDImages()
    {
        var cache = new FakeModLogoCache(TinyPng.Decode());

        using var thumbnail = new ModThumbnailViewModel(1783, new FakeModLogoDirectory((792, LogoUrl)), cache);
        await thumbnail.LoadCompletion;

        cache.CallCount.ShouldBe(0);
        thumbnail.HasLogo.ShouldBeFalse();
    }

    /// <summary>Hors ligne, le cache rend null pour tout échec réseau : la vue retombe sur le pictogramme.</summary>
    [AvaloniaFact]
    public async Task ReseauCoupe_RetombeSurLePictogrammeSansLever()
    {
        var thumbnail = new ModThumbnailViewModel(792, new FakeModLogoDirectory((792, LogoUrl)), new FakeModLogoCache(result: null));

        await thumbnail.LoadCompletion;

        thumbnail.HasLogo.ShouldBeFalse();
        thumbnail.Dispose();
    }

    /// <summary>
    /// Un rescan de l'onglet Mods jette ses rangées et en construit de neuves : sans cette
    /// annulation, chaque bascule d'interrupteur laisserait un téléchargement derrière elle.
    /// </summary>
    [AvaloniaFact]
    public async Task Dispose_AnnuleUnChargementEnVol()
    {
        var cache = new FakeModLogoCache(hangUntilCanceled: true);
        var thumbnail = new ModThumbnailViewModel(792, new FakeModLogoDirectory((792, LogoUrl)), cache);

        thumbnail.Dispose();
        await thumbnail.LoadCompletion;

        cache.LastCancellationToken.IsCancellationRequested.ShouldBeTrue();
        thumbnail.HasLogo.ShouldBeFalse();
    }

    [AvaloniaFact]
    public void None_NeChargeJamaisRienEtSurvitAUneDisposition()
    {
        ModThumbnailViewModel.None.HasLogo.ShouldBeFalse();

        // Chaque rangée sans vignette dispose cette instance PARTAGÉE à chaque rescan : elle doit
        // rester utilisable après, sinon le second rescan travaillerait sur une valeur fermée.
        ModThumbnailViewModel.None.Dispose();
        ModThumbnailViewModel.None.Dispose();

        ModThumbnailViewModel.None.HasLogo.ShouldBeFalse();
    }

    [AvaloniaFact]
    public void Constructeur_RefuseSesPortsNuls()
    {
        Should.Throw<ArgumentNullException>(() => new ModThumbnailViewModel(792, null!, new FakeModLogoCache()));
        Should.Throw<ArgumentNullException>(() => new ModThumbnailViewModel(792, new FakeModLogoDirectory(), null!));
    }
}