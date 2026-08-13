using System.Text.Json;

using Prospect.Core.Common;
using Prospect.Core.ModDb;

using Shouldly;

namespace Prospect.Core.Tests.ModDb;

/// <summary>
/// Le convertisseur qui absorbe l'ambiguïté de <c>json_encode</c> : une map associative vide sort
/// en <c>[]</c> côté PHP, la même map peuplée sort en objet. Éprouvé ici sur l'enveloppe qui a
/// révélé le défaut, <c>/api/updates</c>, à travers le vrai contexte source-gen plutôt que sur des
/// options fabriquées pour le test.
/// </summary>
public sealed class PhpAssociativeArrayConverterTests
{
    private static ModDbUpdatesResponseDto Read(string payload)
        => JsonSerializer.Deserialize(payload, ModDbJsonContext.Default.ModDbUpdatesResponseDto)!;

    private static string Write(Dictionary<string, ModDbReleaseDto>? updates)
        => JsonSerializer.Serialize(
            new ModDbUpdatesResponseDto { StatusCode = "200", Updates = updates },
            ModDbJsonContext.Default.ModDbUpdatesResponseDto);

    [Fact]
    public void AnEmptyPhpArray_ReadsAsAnEmptyMap()
        => Read("""{"statuscode":"200","updates":[]}""").Updates.ShouldBeEmpty();

    [Fact]
    public void AnEmptyObject_ReadsAsAnEmptyMap()
        => Read("""{"statuscode":"200","updates":{}}""").Updates.ShouldBeEmpty();

    [Fact]
    public void AMissingField_StaysNullRatherThanBecomingAnEmptyMap()
    {
        // L'absence et la map vide ne disent pas la même chose : la première signale une réponse
        // d'une forme qu'on ne connaît pas, la seconde un lot dont rien n'est en retard.
        Read("""{"statuscode":"200"}""").Updates.ShouldBeNull();
        Read("""{"statuscode":"200","updates":null}""").Updates.ShouldBeNull();
    }

    [Fact]
    public void APopulatedObject_ReadsEveryEntryWithItsKey()
    {
        var updates = Read("""
        {
          "statuscode": "200",
          "updates": {
            "configlib": { "releaseid": 39980, "modidstr": "configlib", "modversion": "1.12.0" },
            "carryon": { "releaseid": 41002, "modidstr": "carryon", "modversion": "2.0.0-pre.9" }
          }
        }
        """).Updates.ShouldNotBeNull();

        updates.Count.ShouldBe(2);
        updates["configlib"].ReleaseId.ShouldBe(39980);
        updates["carryon"].ModVersion.ShouldBe("2.0.0-pre.9");
    }

    /// <summary>
    /// Un tableau NON vide n'a pas de clés : rien n'en ferait une map, et le rendre vide en silence
    /// jetterait des données. Refus franc, donc, sur une forme que <c>json_encode</c> ne peut pas
    /// produire à partir d'une map de <c>modidstr</c>.
    /// </summary>
    [Fact]
    public void ANonEmptyArray_IsRefused()
        => Should.Throw<JsonException>(() => Read("""{"statuscode":"200","updates":[{"releaseid":1}]}"""));

    [Theory]
    [InlineData("5")]
    [InlineData("\"configlib\"")]
    [InlineData("true")]
    public void AScalarWhereAMapBelongs_IsRefused(string value)
        => Should.Throw<JsonException>(() => Read($$"""{"statuscode":"200","updates":{{value}}}"""));

    /// <summary>
    /// L'écriture rend toujours un OBJET, y compris vide : nos propres documents n'ont aucune raison
    /// de reproduire l'ambiguïté de PHP, et la relecture d'un objet vide ne pose aucun problème.
    /// </summary>
    [Fact]
    public void WritingAnEmptyMap_EmitsAnObjectAndNotAnArray()
    {
        var json = Write([]);

        json.ShouldContain("\"updates\":{}");
        Read(json).Updates.ShouldBeEmpty();
    }

    [Fact]
    public void WritingAPopulatedMap_RoundTripsThroughItsKeys()
    {
        var json = Write(new Dictionary<string, ModDbReleaseDto>(StringComparer.Ordinal)
        {
            ["configlib"] = new() { ReleaseId = 39980, ModIdString = "configlib", ModVersion = "1.12.0" },
        });

        var updates = Read(json).Updates.ShouldNotBeNull();
        updates.ShouldContainKey("configlib");
        updates["configlib"].ReleaseId.ShouldBe(39980);
        updates["configlib"].ModVersion.ShouldBe("1.12.0");
    }

    [Fact]
    public void WritingANullMap_EmitsNull()
        => Write(null).ShouldContain("\"updates\":null");

    /// <summary>
    /// Le mapper doit rendre la même chose sur les deux formes du vide : c'est la fin du chemin qui
    /// remontait « Le ModDB est injoignable » sur un lot parfaitement à jour.
    /// </summary>
    [Fact]
    public void BothShapesOfEmptiness_ReachTheMapperAsNoUpdateAtAll()
    {
        ModDbMapper.ToUpdates(Read("""{"statuscode":"200","updates":[]}""").Updates).ShouldBeEmpty();
        ModDbMapper.ToUpdates(Read("""{"statuscode":"200","updates":{}}""").Updates).ShouldBeEmpty();
    }

    [Fact]
    public void TheV2InstallInformation_TakesTheSameTwoShapes()
    {
        var empty = JsonSerializer.Deserialize(
            """{"data":[],"resolved":[],"warnings":[]}""",
            ModDbJsonContext.Default.ModDbV2InstallInformationResponseDto)!;

        empty.Data.ShouldBeEmpty();
        empty.Resolved.ShouldBeEmpty();

        var populated = JsonSerializer.Deserialize(
            """{"data":{"carryon":{"recommendedUpgrade":"2.0.0"}},"resolved":{"carryonlib":{"version":"1.0.0"}}}""",
            ModDbJsonContext.Default.ModDbV2InstallInformationResponseDto)!;

        populated.Data.ShouldNotBeNull().ShouldContainKey("carryon");
        populated.Resolved.ShouldNotBeNull().ShouldContainKey("carryonlib");
    }
}