using Prospect.Core.ModDb;

using Shouldly;

namespace Prospect.Core.Tests.ModDb;

/// <summary>
/// La construction de l'adresse de fiche, corrigée après un test réel sous Windows : le bouton
/// « voir la page du mod » ouvrait la fiche d'un AUTRE mod.
/// </summary>
/// <remarks>
/// La cause est un mélange de deux espaces d'identifiants. La route <c>show/mod/{N}</c> du site
/// attend l'<c>assetid</c>, pas le <c>modid</c>, et les deux divergent pour de vrai : Carry On
/// porte <c>modid</c> 890 et <c>assetid</c> 4405, CarryOnLib <c>modid</c> 4687 et <c>assetid</c>
/// 27960 (relevé sur l'API le 2026-08-13). Construire <c>show/mod/890</c> pour Carry On ouvrait
/// donc la fiche de l'asset 890, qui appartient à quelqu'un d'autre.
/// </remarks>
public sealed class ModDbMapperTests
{
    private static readonly Uri Site = new("https://mods.vintagestory.at/");

    [Fact]
    public void BuildPageUrl_WithAnAlias_UsesTheShortRoute()
        => ModDbMapper.BuildPageUrl(4405, "carryon").ShouldBe(new Uri("https://mods.vintagestory.at/carryon"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void BuildPageUrl_WithoutAnAlias_FallsBackToTheAssetRoute(string? alias)
        => ModDbMapper.BuildPageUrl(9551, alias).ShouldBe(new Uri("https://mods.vintagestory.at/show/mod/9551"));

    [Fact]
    public void BuildPageUrl_AliasCasing_IsKeptExactlyAsTheApiServedIt()
    {
        // L'alias est une donnée du serveur, pas une chaîne à normaliser : le remettre en
        // minuscules serait inventer une convention que rien ne documente.
        ModDbMapper.BuildPageUrl(4405, "CarryOn").ShouldBe(new Uri("https://mods.vintagestory.at/CarryOn"));
        ModDbMapper.BuildPageUrl(4405, "  carryon  ").ShouldBe(new Uri("https://mods.vintagestory.at/carryon"));
    }

    [Fact]
    public void BuildPageUrl_AliasWithSeparators_IsEscapedRatherThanComposingAnotherUrl()
    {
        // L'alias est saisi par un auteur : un '/' ou un '?' malencontreux ne doit pas fabriquer
        // une tout autre adresse que celle de sa fiche.
        ModDbMapper.BuildPageUrl(1, "a/b").ShouldBe(new Uri("https://mods.vintagestory.at/a%2Fb"));
        ModDbMapper.BuildPageUrl(1, "a?b=c").ShouldBe(new Uri("https://mods.vintagestory.at/a%3Fb%3Dc"));
    }

    [Fact]
    public void BuildPageUrl_WithoutAliasNorAssetId_FallsBackToTheSiteRatherThanAWrongMod()
        => ModDbMapper.BuildPageUrl(0, null).ShouldBe(Site);

    [Fact]
    public void ToDetail_RealCarryOnShape_BuildsTheAliasUrlAndNotTheModIdOne()
    {
        var detail = ModDbMapper.ToDetail(new ModDbModDetailDto
        {
            ModId = 890,
            AssetId = 4405,
            Name = "Carry On",
            UrlAlias = "carryon",
            SourceCodeUrl = "https://github.com/NerdScurvy/CarryOn?organization=NerdScurvy",
            IssueTrackerUrl = "https://github.com/NerdScurvy/CarryOn/issues",
            HomepageUrl = "",
            WikiUrl = "",
        })!;

        detail.AssetId.ShouldBe(4405);
        detail.PageUrl.ShouldBe(new Uri("https://mods.vintagestory.at/carryon"));
        detail.PageUrl.AbsoluteUri.ShouldNotContain("890");
        detail.SourceCodeUrl.ShouldBe(new Uri("https://github.com/NerdScurvy/CarryOn?organization=NerdScurvy"));
        detail.IssueTrackerUrl.ShouldBe(new Uri("https://github.com/NerdScurvy/CarryOn/issues"));
        detail.HomepageUrl.ShouldBeNull();
        detail.WikiUrl.ShouldBeNull();
    }

    [Fact]
    public void ToDetail_WithoutAlias_UsesTheAssetIdAndNeverTheModId()
    {
        // Forme réelle de Config lib : urlalias null, modid 1783, assetid 9551.
        var detail = ModDbMapper.ToDetail(new ModDbModDetailDto
        {
            ModId = 1783,
            AssetId = 9551,
            Name = "Config lib",
            UrlAlias = null,
        })!;

        detail.PageUrl.ShouldBe(new Uri("https://mods.vintagestory.at/show/mod/9551"));
    }

    [Fact]
    public void ToSummary_CarriesTheAssetIdAndTheSamePageUrlAsTheDetail()
    {
        var summary = ModDbMapper.ToSummary(new ModDbModSummaryDto
        {
            ModId = 890,
            AssetId = 4405,
            Name = "Carry On",
            UrlAlias = "carryon",
        })!;

        summary.AssetId.ShouldBe(4405);
        summary.PageUrl.ShouldBe(new Uri("https://mods.vintagestory.at/carryon"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("github.com/sans-schema")]
    [InlineData("javascript:alert(1)")]
    [InlineData("/relatif")]
    public void ToExternalUri_AnythingButAnAbsoluteHttpUrl_IsDropped(string? value)
        => ModDbMapper.ToExternalUri(value).ShouldBeNull();

    [Theory]
    [InlineData("https://example.invalid/a")]
    [InlineData("http://example.invalid/a")]
    public void ToExternalUri_AbsoluteHttpUrl_IsKept(string value)
        => ModDbMapper.ToExternalUri(value).ShouldBe(new Uri(value));
}
