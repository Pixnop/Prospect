using Prospect.Core.Instances;

using Shouldly;

namespace Prospect.Core.Tests.Instances;

/// <summary>
/// Couvre <see cref="InstanceSlugGenerator"/> exhaustivement : repli des diacritiques,
/// substitution des caractères hors <c>[a-z0-9]</c>, fusion des tirets, bornage de longueur, et
/// résolution d'unicité par suffixe numérique.
/// </summary>
public class InstanceSlugGeneratorTests
{
    [Theory]
    [InlineData("Homestead", "homestead")]
    [InlineData("Écorce brûlée", "ecorce-brulee")]
    [InlineData("Été à la ferme", "ete-a-la-ferme")]
    [InlineData("HOMESTEAD", "homestead")]
    [InlineData("Homestead 1.21", "homestead-1-21")]
    [InlineData("mon_super--monde!!", "mon-super-monde")]
    [InlineData("  Espaces en trop  ", "espaces-en-trop")]
    [InlineData("Café à côté", "cafe-a-cote")]
    [InlineData("naïve façade", "naive-facade")]
    public void Slugify_VariousNames_ProducesExpectedSlug(string name, string expectedSlug)
    {
        InstanceSlugGenerator.Slugify(name).ShouldBe(expectedSlug);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("!!!")]
    [InlineData("---")]
    [InlineData("...")]
    public void Slugify_ProducesEmptyResult_FallsBackToInstance(string name)
    {
        InstanceSlugGenerator.Slugify(name).ShouldBe("instance");
    }

    [Fact]
    public void Slugify_Null_FallsBackToInstance()
    {
        InstanceSlugGenerator.Slugify(null!).ShouldBe("instance");
    }

    [Fact]
    public void Slugify_NameLongerThanBound_IsTruncated()
    {
        var longName = new string('a', 100);

        var slug = InstanceSlugGenerator.Slugify(longName);

        slug.Length.ShouldBeLessThanOrEqualTo(40);
        slug.ShouldBe(new string('a', 40));
    }

    [Fact]
    public void Slugify_TruncationLandsOnDash_TrimsTrailingDash()
    {
        // 39 'a' puis un séparateur : la troncature brute à 40 caractères couperait pile après
        // le tiret, qui doit être nettoyé plutôt que laissé en bordure.
        var name = new string('a', 39) + " " + new string('b', 10);

        var slug = InstanceSlugGenerator.Slugify(name);

        slug.ShouldBe(new string('a', 39));
        slug.EndsWith('-').ShouldBeFalse();
    }

    [Fact]
    public void GenerateUnique_SlugNotTaken_ReturnsBaseSlug()
    {
        var slug = InstanceSlugGenerator.GenerateUnique("Homestead", _ => false);

        slug.ShouldBe("homestead");
    }

    [Fact]
    public void GenerateUnique_SlugTaken_AppendsSuffixTwo()
    {
        var taken = new HashSet<string> { "homestead" };

        var slug = InstanceSlugGenerator.GenerateUnique("Homestead", taken.Contains);

        slug.ShouldBe("homestead-2");
    }

    [Fact]
    public void GenerateUnique_FirstTwoSuffixesTaken_AppendsSuffixThree()
    {
        var taken = new HashSet<string> { "homestead", "homestead-2" };

        var slug = InstanceSlugGenerator.GenerateUnique("Homestead", taken.Contains);

        slug.ShouldBe("homestead-3");
    }

    [Fact]
    public void GenerateUnique_NullPredicate_ThrowsArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() => InstanceSlugGenerator.GenerateUnique("Homestead", null!));
    }

    [Fact]
    public void GenerateUnique_EmptyName_UsesInstanceFallbackAsBase()
    {
        var slug = InstanceSlugGenerator.GenerateUnique("!!!", _ => false);

        slug.ShouldBe("instance");
    }
}