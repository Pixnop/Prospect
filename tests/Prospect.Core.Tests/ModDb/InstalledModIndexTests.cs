using Prospect.Core.Common;
using Prospect.Core.ModDb;

using Shouldly;

namespace Prospect.Core.Tests.ModDb;

/// <summary>
/// Rapprochement entre une fiche de catalogue et ce qui est réellement dans <c>data/Mods/</c>.
/// Deux clés cohabitent, aucune n'est disponible partout : l'identifiant numérique du ModDB, que
/// seule la provenance enregistre, et le <c>modid</c> textuel, que la fiche peut exposer en
/// plusieurs exemplaires ou pas du tout. Ces tests fixent l'ordre d'essai et les replis.
/// </summary>
public sealed class InstalledModIndexTests
{
    private static InstalledMod Mod(
        string identity,
        string version = "1.0.0",
        int? modDbId = null,
        bool enabled = true)
        => new()
        {
            FilePath = $"/mods/{identity}.zip",
            FileName = $"{identity}.zip",
            IsEnabled = enabled,
            Info = new ModInfo { ModId = identity, Name = identity, Version = ModVersion.Parse(version) },
            Provenance = modDbId is null
                ? null
                : new ModProvenance
                {
                    FileName = $"{identity}.zip",
                    ModId = modDbId.Value,
                    ModIdString = identity,
                    ReleaseId = 1,
                    FileId = 2,
                    Version = ModVersion.Parse(version),
                    InstalledUtc = DateTimeOffset.UnixEpoch,
                },
        };

    [Fact]
    public void Empty_MatchesNothing()
    {
        InstalledModIndex.Empty.IsEmpty.ShouldBeTrue();
        InstalledModIndex.Empty.Find(1783, ["carryon"]).ShouldBeNull();
    }

    [Fact]
    public void AModInstalledByProspect_IsFoundByItsNumericModDbId()
    {
        var index = InstalledModIndex.From([Mod("configlib", "1.11.1", modDbId: 890)]);

        // La fiche n'annonce même pas le modid textuel : la provenance suffit.
        var match = index.Find(890, []).ShouldNotBeNull();
        match.Identity.ShouldBe("configlib");
        match.Version.ShouldBe(ModVersion.Parse("1.11.1"));
        match.IsEnabled.ShouldBeTrue();
    }

    [Fact]
    public void AModDroppedByHand_IsFoundByItsTextualModId()
    {
        // Aucune provenance : c'est le cas du zip posé à la main dans data/Mods/.
        var index = InstalledModIndex.From([Mod("carryon", "1.8.0")]);

        index.Find(1783, ["carryon"]).ShouldNotBeNull().Version.ShouldBe(ModVersion.Parse("1.8.0"));
    }

    [Fact]
    public void AListingSeveralModIds_MatchesOnAnyOfThem()
    {
        var index = InstalledModIndex.From([Mod("carryonlib")]);

        index.Find(1783, ["carryon", "carryonlib"]).ShouldNotBeNull().Identity.ShouldBe("carryonlib");
    }

    [Fact]
    public void TheTextualIdIsCaseInsensitive()
    {
        var index = InstalledModIndex.From([Mod("CarryOn")]);

        index.Find(1783, ["carryon"]).ShouldNotBeNull();
    }

    [Fact]
    public void AnUnrelatedListing_MatchesNothing()
    {
        var index = InstalledModIndex.From([Mod("configlib", modDbId: 890)]);

        index.Find(1783, ["carryon"]).ShouldBeNull();
        index.Find(1783, null).ShouldBeNull();
    }

    /// <summary>
    /// Un mod désactivé est TOUJOURS installé : le zip est là, le réinstaller l'écraserait. La carte
    /// doit donc le voir, à charge pour la fiche de dire qu'il dort.
    /// </summary>
    [Fact]
    public void ADisabledMod_CountsAsInstalledAndSaysSo()
    {
        var index = InstalledModIndex.From([Mod("carryon", enabled: false)]);

        index.Find(1783, ["carryon"]).ShouldNotBeNull().IsEnabled.ShouldBeFalse();
    }

    /// <summary>
    /// Archive illisible mais posée par Prospect : le modinfo ne donne rien, la provenance donne
    /// tout. Sans ce repli, un mod cassé repasserait pour absent et serait réinstallé en double.
    /// </summary>
    [Fact]
    public void AnUnreadableArchiveWithProvenance_IsStillMatched()
    {
        var mod = new InstalledMod
        {
            FilePath = "/mods/mystere.zip",
            FileName = "mystere.zip",
            IsEnabled = true,
            Problem = ModInfoProblem.MissingModInfo,
            Provenance = new ModProvenance
            {
                FileName = "mystere.zip",
                ModId = 1783,
                ModIdString = "carryon",
                ReleaseId = 1,
                FileId = 2,
                Version = ModVersion.Parse("1.8.0"),
                InstalledUtc = DateTimeOffset.UnixEpoch,
            },
        };

        var index = InstalledModIndex.From([mod]);

        index.Find(1783, []).ShouldNotBeNull().Version.ShouldBe(ModVersion.Parse("1.8.0"));
        index.Find(0, ["carryon"]).ShouldNotBeNull().Version.ShouldBe(ModVersion.Parse("1.8.0"));
    }

    [Fact]
    public void From_NullArgument_IsRejected()
        => Should.Throw<ArgumentNullException>(() => InstalledModIndex.From(null!));
}