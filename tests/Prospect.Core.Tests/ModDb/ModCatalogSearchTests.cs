using Prospect.Core.ModDb;

using Shouldly;

namespace Prospect.Core.Tests.ModDb;

public sealed class ModCatalogSearchTests
{
    private static readonly ModDbModSummary Ruins = new()
    {
        ModId = 792,
        Name = "BetterRuins",
        Summary = "Adds many new ruins to your survival world.",
        Author = "NiclAss",
        ModIdStrings = ["betterruins"],
        Tags = ["Worldgen", "Exploration"],
        Downloads = 1095320,
        TrendingPoints = 0,
        LastReleasedUtc = new DateTimeOffset(2026, 7, 28, 18, 59, 32, TimeSpan.Zero),
    };

    private static readonly ModDbModSummary ConfigLib = new()
    {
        ModId = 1783,
        Name = "Config lib",
        Summary = "A universal place to configure your mods.",
        Author = "Maltiez",
        ModIdStrings = ["configlib"],
        Tags = ["Utility"],
        Downloads = 627953,
        TrendingPoints = 42,
        LastReleasedUtc = new DateTimeOffset(2026, 5, 1, 12, 3, 34, TimeSpan.Zero),
    };

    private static readonly ModDbModSummary Carry = new()
    {
        ModId = 200,
        Name = "CarryCapacity",
        Summary = "Carry chests and crates around.",
        Author = "copygirl",
        ModIdStrings = ["carrycapacity"],
        Tags = ["Utility", "QoL"],
        Downloads = 900000,
        TrendingPoints = 7,
        LastReleasedUtc = null,
    };

    private static readonly ModDbModSummary[] Catalog = [Ruins, ConfigLib, Carry];

    [Fact]
    public void Apply_NoCriteria_SortsByDownloadsDescending()
    {
        var result = ModCatalogSearch.Apply(Catalog, new ModCatalogQuery());

        result.Select(mod => mod.Name).ShouldBe(["BetterRuins", "CarryCapacity", "Config lib"]);
    }

    [Theory]
    [InlineData("ruins", "BetterRuins")]
    [InlineData("MALTIEZ", "Config lib")]
    [InlineData("chests", "CarryCapacity")]
    [InlineData("carrycapacity", "CarryCapacity")]
    public void Apply_TextSearch_MatchesNameSummaryAuthorAndIdentifierCaseInsensitively(string needle, string expected)
    {
        var result = ModCatalogSearch.Apply(Catalog, new ModCatalogQuery(Text: needle));

        result.Single().Name.ShouldBe(expected);
    }

    [Fact]
    public void Apply_TagFilter_KeepsOnlyModsCarryingThatCategory()
    {
        var result = ModCatalogSearch.Apply(Catalog, new ModCatalogQuery(TagName: "Utility"));

        result.Select(mod => mod.Name).ShouldBe(["CarryCapacity", "Config lib"]);
    }

    [Fact]
    public void Apply_SortByRecentlyUpdated_PutsUndatedModsLast()
    {
        var result = ModCatalogSearch.Apply(Catalog, new ModCatalogQuery(Sort: ModCatalogSort.RecentlyUpdated));

        result.Select(mod => mod.Name).ShouldBe(["BetterRuins", "Config lib", "CarryCapacity"]);
    }

    [Fact]
    public void Apply_SortByTrending_UsesTheSiteScoreThenDownloads()
    {
        var result = ModCatalogSearch.Apply(Catalog, new ModCatalogQuery(Sort: ModCatalogSort.Trending));

        result.Select(mod => mod.Name).ShouldBe(["Config lib", "CarryCapacity", "BetterRuins"]);
    }

    [Fact]
    public void Apply_SortByName_IsAlphabeticalAndCaseInsensitive()
    {
        var result = ModCatalogSearch.Apply(Catalog, new ModCatalogQuery(Sort: ModCatalogSort.Name));

        result.Select(mod => mod.Name).ShouldBe(["BetterRuins", "CarryCapacity", "Config lib"]);
    }

    [Fact]
    public void Apply_CompatibleOnly_KeepsOnlyModsPresentInTheIndex()
    {
        var index = new ModDbCompatibilityIndex(new HashSet<int> { 792 }, IsApproximate: false, ModDbFreshness.Live);

        var result = ModCatalogSearch.Apply(
            Catalog,
            new ModCatalogQuery(Compatibility: ModCompatibilityFilter.CompatibleOnly),
            index);

        result.Single().Name.ShouldBe("BetterRuins");
    }

    [Fact]
    public void Apply_CompatibleOnlyWithAnUnavailableIndex_ShowsEverythingRatherThanNothing()
    {
        // L'index vide signifie « le ModDB n'a pas répondu », jamais « aucun mod n'est compatible ».
        var result = ModCatalogSearch.Apply(
            Catalog,
            new ModCatalogQuery(Compatibility: ModCompatibilityFilter.CompatibleOnly),
            ModDbCompatibilityIndex.Unavailable);

        result.Count.ShouldBe(3);
    }

    [Fact]
    public void Apply_CombinedCriteria_AppliesAllOfThem()
    {
        var index = new ModDbCompatibilityIndex(new HashSet<int> { 200, 1783 }, IsApproximate: false, ModDbFreshness.Live);

        var result = ModCatalogSearch.Apply(
            Catalog,
            new ModCatalogQuery("c", "Utility", ModCatalogSort.Name, ModCompatibilityFilter.CompatibleOnly),
            index);

        result.Select(mod => mod.Name).ShouldBe(["CarryCapacity", "Config lib"]);
    }
}