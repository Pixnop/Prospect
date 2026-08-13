using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;

using Prospect.Core.ModDb;
using Prospect.Desktop.Tests.TestDoubles;
using Prospect.Desktop.ViewModels.Mods;

using Shouldly;

namespace Prospect.Desktop.Tests.ViewModels.Mods;

/// <summary>
/// <see cref="ModCardViewModel"/> en isolation : son constructeur ne dépend que d'un
/// <see cref="Prospect.Desktop.Services.IModLogoCache"/> injectable (<see cref="FakeModLogoCache"/>),
/// pas besoin de monter tout <see cref="Prospect.Desktop.ViewModels.Mods.ModBrowserViewModel"/>.
/// Couvre le résumé exposé à la vue pour sa troncature, le repli quand le catalogue ne fournit pas
/// de logo, et l'annulation du chargement à la fermeture de la carte.
/// </summary>
public sealed class ModCardViewModelTests
{
    private static readonly Uri LogoUrl = new("https://moddbcdn.vintagestory.at/example.png");

    private static ModDbModSummary CreateSummary(string summary = "", Uri? logoUrl = null) => new()
    {
        ModId = 1,
        Name = "Test Mod",
        Author = "Quelqu'un",
        Summary = summary,
        LogoUrl = logoUrl,
    };

    private static Task NoOp(ModCardViewModel card) => Task.CompletedTask;

    [Fact]
    public void Constructor_SummaryWithoutLogo_NeverCallsTheCacheAndKeepsThePlaceholder()
    {
        var cache = new FakeModLogoCache();

        using var card = new ModCardViewModel(CreateSummary(), ModCompatibilityBadge.None, installed: null, NoOp, NoOp, cache);

        card.HasLogo.ShouldBeFalse();
        card.LogoBitmap.ShouldBeNull();
        cache.CallCount.ShouldBe(0);
    }

    [Fact]
    public void Constructor_SummaryWithLogo_AsksTheCacheForThatExactUrl()
    {
        var cache = new FakeModLogoCache();

        using var card = new ModCardViewModel(CreateSummary(logoUrl: LogoUrl), ModCompatibilityBadge.None, installed: null, NoOp, NoOp, cache);

        cache.CallCount.ShouldBe(1);
        cache.LastUrl.ShouldBe(LogoUrl);
    }

    [AvaloniaFact]
    public async Task Constructor_LogoResolves_ExposesTheDecodedBitmapAndHasLogoBecomesTrue()
    {
        using var bitmap = new Bitmap(new MemoryStream(TinyPng.Create()));
        var cache = new FakeModLogoCache(result: bitmap);

        using var card = new ModCardViewModel(CreateSummary(logoUrl: LogoUrl), ModCompatibilityBadge.None, installed: null, NoOp, NoOp, cache);
        await card.LogoLoadCompletion;

        card.HasLogo.ShouldBeTrue();
        card.LogoBitmap.ShouldBeSameAs(bitmap);
    }

    [Fact]
    public void Constructor_EmptySummary_HasDescriptionIsFalse()
    {
        using var card = new ModCardViewModel(CreateSummary(), ModCompatibilityBadge.None, installed: null, NoOp, NoOp, new FakeModLogoCache());

        card.HasDescription.ShouldBeFalse();
        card.Description.ShouldBe(string.Empty);
    }

    [Fact]
    public void Constructor_SummaryWithHtmlEntities_ExposesItDecodedAndHasDescriptionIsTrue()
    {
        using var card = new ModCardViewModel(CreateSummary(summary: "Cook &amp; Craft"), ModCompatibilityBadge.None, installed: null, NoOp, NoOp, new FakeModLogoCache());

        card.HasDescription.ShouldBeTrue();
        card.Description.ShouldBe("Cook & Craft");
    }

    [Fact]
    public void Dispose_LogoStillLoading_CancelsTheTokenPassedToTheCache()
    {
        var cache = new FakeModLogoCache(hangUntilCanceled: true);
        var card = new ModCardViewModel(CreateSummary(logoUrl: LogoUrl), ModCompatibilityBadge.None, installed: null, NoOp, NoOp, cache);

        card.Dispose();

        cache.LastCancellationToken.IsCancellationRequested.ShouldBeTrue();
    }

    [Fact]
    public void Constructor_NullArguments_AreRejected()
    {
        var summary = CreateSummary();
        var cache = new FakeModLogoCache();

        Should.Throw<ArgumentNullException>(() => new ModCardViewModel(null!, ModCompatibilityBadge.None, installed: null, NoOp, NoOp, cache));
        Should.Throw<ArgumentNullException>(() => new ModCardViewModel(summary, ModCompatibilityBadge.None, installed: null, null!, NoOp, cache));
        Should.Throw<ArgumentNullException>(() => new ModCardViewModel(summary, ModCompatibilityBadge.None, installed: null, NoOp, null!, cache));
        Should.Throw<ArgumentNullException>(() => new ModCardViewModel(summary, ModCompatibilityBadge.None, installed: null, NoOp, NoOp, null!));
    }
}