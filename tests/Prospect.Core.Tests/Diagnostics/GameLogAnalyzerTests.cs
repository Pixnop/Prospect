using System.Reflection;

using Prospect.Core.Diagnostics;

using Shouldly;

namespace Prospect.Core.Tests.Diagnostics;

/// <summary>
/// <see cref="GameLogAnalyzer"/> confronté aux formes RÉELLEMENT observées dans les journaux du
/// jeu (session client et session serveur de Vintage Story 1.22.6, relevées pendant
/// l'implémentation) : préfixe de mod posé par le chargeur, erreurs du chargeur de patches JSON,
/// bloc des systèmes qui relie une archive à son modid, piles d'exception. Les échantillons sont
/// synthétiques mais leurs formes ne le sont pas.
/// </summary>
public sealed class GameLogAnalyzerTests
{
    private static readonly string[] EmptyLog = [];

    private static string[] Lines(string text)
        => text.ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);

    // Le journal complet d'un lancement, embarqué plutôt que collé dans une chaîne : quarante
    // lignes de journal dans un littéral C# ne se relisent pas, et ce fichier-là se compare
    // directement à une vraie session.
    private static string[] FixtureLines()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resource = assembly.GetManifestResourceNames().Single(name => name.EndsWith("launch-session.log", StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resource)!;
        using var reader = new StreamReader(stream);

        return Lines(reader.ReadToEnd());
    }

    [Fact]
    public void Analyze_EmptyLog_ReportsNothing()
    {
        var report = GameLogAnalyzer.Analyze(EmptyLog);

        report.Mods.ShouldBeEmpty();
        report.Integrations.ShouldBeEmpty();
        report.ObservedModIds.ShouldBeEmpty();
    }

    [Fact]
    public void Analyze_OnlyNotifications_LeavesEveryModHealthy()
    {
        var report = GameLogAnalyzer.Analyze(Lines("""
        13.8.2026 21:08:23 [Client Notification] Mods, sorted by dependency: carryon, game, survival
        13.8.2026 21:08:23 [Client Notification] Instantiated 152 mod systems from 6 enabled mods
        13.8.2026 21:08:24 [Client Event] started 'Carry On' mod
        """));

        report.Mods.ShouldBeEmpty();
        report.ObservedModIds.ShouldBe(["carryon"]);
    }

    [Fact]
    public void Analyze_ErrorPrefixedWithAModId_AttributesItToThatMod()
    {
        var report = GameLogAnalyzer.Analyze(Lines("""
        13.8.2026 21:08:23 [Client Notification] Mods, sorted by dependency: hearthside, game
        13.8.2026 21:08:23 [Client Error] [hearthside] Could not resolve some dependencies:
        13.8.2026 21:08:23 [Client Error] [hearthside]     saltyseas - Missing
        """));

        var mod = report.Mods.ShouldHaveSingleItem();
        mod.ModId.ShouldBe("hearthside");
        mod.ErrorCount.ShouldBe(2);
        mod.WarningCount.ShouldBe(0);
        mod.Severity.ShouldBe(GameLogSeverity.Error);
        mod.IsHealthy.ShouldBeFalse();
    }

    /// <summary>
    /// L'erreur nomme le mod AVANT que le journal n'ait publié sa liste de mods : c'est le cas
    /// réel des échecs de dépendance, écrits avant « Mods, sorted by dependency ». Sans une
    /// seconde passe d'attribution, ces lignes-là resteraient orphelines.
    /// </summary>
    [Fact]
    public void Analyze_ErrorBeforeTheModListIsKnown_IsStillAttributedAfterwards()
    {
        var report = GameLogAnalyzer.Analyze(Lines("""
        13.8.2026 21:08:23 [Client Error] [hearthside] Could not resolve some dependencies:
        13.8.2026 21:08:23 [Client Notification] Mods, sorted by dependency: hearthside, game
        """));

        report.Mods.ShouldHaveSingleItem().ModId.ShouldBe("hearthside");
    }

    [Fact]
    public void Analyze_UnknownBracketToken_IsNotMistakenForAMod()
    {
        var report = GameLogAnalyzer.Analyze(Lines("""
        13.8.2026 21:08:23 [Client Notification] Mods, sorted by dependency: hearthside, game
        13.8.2026 21:08:23 [Client Error] [GuiManager] OnMouseDown handled by nothing at all
        """));

        report.Mods.ShouldBeEmpty();
    }

    /// <summary>
    /// Un mod dont le <c>modinfo.json</c> est illisible n'a pas de modid : le jeu le nomme par son
    /// archive, et il n'apparaît dans aucune des listes du journal puisqu'il n'a pas été chargé.
    /// </summary>
    [Fact]
    public void Analyze_ArchiveNamedInBrackets_IsAcceptedAsAModIdentity()
    {
        var report = GameLogAnalyzer.Analyze(Lines("""
        13.8.2026 21:08:23 [Client Error] [ruinedcompass-0.4.2.zip] An exception was thrown trying to to load the ModInfo:
        """));

        report.Mods.ShouldHaveSingleItem().ModId.ShouldBe("ruinedcompass-0.4.2.zip");
    }

    [Fact]
    public void Analyze_StackFrames_AreAttachedToTheEntryTheyExplain()
    {
        var report = GameLogAnalyzer.Analyze(Lines("""
        13.8.2026 21:08:26 [Client Error] Failed to run mod phase Start
           at CarryOn.CarrySystem.Start(ICoreAPI api)
           at Vintagestory.Common.ModLoader.TryRunModPhase(Mod mod)
        13.8.2026 21:08:26 [Client Notification] Mods, sorted by dependency: carryon, game
        """));

        var mod = report.Mods.ShouldHaveSingleItem();
        mod.ModId.ShouldBe("carryon");
        mod.ErrorCount.ShouldBe(1);
    }

    /// <summary>
    /// Deux mods du même auteur partagent leur racine de namespace : c'est le segment le plus long
    /// qui décrit le type, pas le premier qui correspond.
    /// </summary>
    [Fact]
    public void Analyze_TypeNameSharedBetweenTwoMods_PicksTheMostSpecificOne()
    {
        var report = GameLogAnalyzer.Analyze(Lines("""
        13.8.2026 21:08:23 [Client Notification] Mods, sorted by dependency: carryon, carryonlib, game
        13.8.2026 21:08:26 [Client Error] Failed to start system CarryOn.CarryOnLib.CarryOnLibSystem
        """));

        report.Mods.ShouldHaveSingleItem().ModId.ShouldBe("carryonlib");
    }

    /// <summary>
    /// Le bloc « Started N systems on … » est la seule déclaration explicite du journal : il relie
    /// un nom de type à son mod, ce que le rapprochement de segments ne pourrait pas faire ici (le
    /// namespace ne ressemble pas au modid).
    /// </summary>
    [Fact]
    public void Analyze_SystemTypeDeclaredByTheLog_AttributesLaterLinesToItsMod()
    {
        var report = GameLogAnalyzer.Analyze(Lines("""
        13.8.2026 21:08:26 [Client Notification] Started 129 systems on Client:
        13.8.2026 21:08:26 [Client Notification]     Mod 'seasons-2.0.0.zip' (seasons):
        13.8.2026 21:08:26 [Client Notification]         Meadow.Weather.SeasonSystem
        13.8.2026 21:08:27 [Client Warning] Meadow.Weather.SeasonSystem could not read its config, using defaults
        """));

        var mod = report.Mods.ShouldHaveSingleItem();
        mod.ModId.ShouldBe("seasons");
        mod.WarningCount.ShouldBe(1);
        mod.Severity.ShouldBe(GameLogSeverity.Warning);
    }

    /// <summary>
    /// Le même mod nommé par son archive puis par son modid ne doit pas produire deux lignes de
    /// rapport : le bloc des systèmes fait la jonction, et les comptes se rejoignent.
    /// </summary>
    [Fact]
    public void Analyze_ModNamedByArchiveThenByModId_IsCountedOnce()
    {
        var report = GameLogAnalyzer.Analyze(Lines("""
        13.8.2026 21:08:23 [Client Error] [seasons-2.0.0.zip] Compilation failed
        13.8.2026 21:08:26 [Client Notification]     Mod 'seasons-2.0.0.zip' (seasons):
        13.8.2026 21:08:26 [Client Notification]         Meadow.Weather.SeasonSystem
        13.8.2026 21:08:27 [Client Error] [seasons] An exception was thrown when trying to start the mod:
        """));

        var mod = report.Mods.ShouldHaveSingleItem();
        mod.ModId.ShouldBe("seasons");
        mod.ErrorCount.ShouldBe(2);
        mod.Samples.Count.ShouldBe(2);
    }

    /// <summary>
    /// Variante du cas précédent où les DEUX clés ont déjà des lignes à leur compte quand le bloc
    /// des systèmes les réunit : les décomptes s'additionnent au lieu que l'un écrase l'autre.
    /// </summary>
    [Fact]
    public void Analyze_ModBlamedUnderBothItsNames_AddsUpTheCounts()
    {
        var report = GameLogAnalyzer.Analyze(Lines("""
        13.8.2026 21:08:23 [Client Notification] Mods, sorted by dependency: seasons, game
        13.8.2026 21:08:23 [Client Error] [seasons] first failure
        13.8.2026 21:08:23 [Client Error] [seasons-2.0.0.zip] second failure
        13.8.2026 21:08:26 [Client Notification]     Mod 'seasons-2.0.0.zip' (seasons):
        13.8.2026 21:08:26 [Client Notification]         Meadow.Weather.SeasonSystem
        """));

        var mod = report.Mods.ShouldHaveSingleItem();
        mod.ModId.ShouldBe("seasons");
        mod.ErrorCount.ShouldBe(2);
        mod.Samples.Count.ShouldBe(2);
    }

    [Fact]
    public void Analyze_PatchFailure_IsAttributedToTheDomainThatWroteThePatch()
    {
        var report = GameLogAnalyzer.Analyze(Lines("""
        13.8.2026 21:08:24 [Client Error] Patch 3 in hearthside:patches/kitchen.json: File game:itemtypes/resource.json not found
        """));

        report.Mods.ShouldHaveSingleItem().ModId.ShouldBe("hearthside");
    }

    [Fact]
    public void Analyze_PatchTargetingAnotherModThatIsAbsent_ReportsAMissingIntegration()
    {
        var report = GameLogAnalyzer.Analyze(Lines("""
        13.8.2026 21:08:24 [Client Error] Patch 0 in hearthside:patches/kitchen.json: File saltyseas:blocktypes/barrel.json not found
        """));

        var integration = report.Integrations.ShouldHaveSingleItem();
        integration.SourceModId.ShouldBe("hearthside");
        integration.TargetModId.ShouldBe("saltyseas");
        integration.Nature.ShouldBe(ModIntegrationNature.Missing);
        integration.Evidence.ShouldContain("saltyseas:blocktypes/barrel.json");
    }

    /// <summary>
    /// <c>game</c>, <c>survival</c> et <c>creative</c> ne sont pas des mods : le contenu du jeu
    /// n'a pas à s'afficher comme une intégration entre mods (même règle que le ModDB, voir
    /// <c>ModInfoParser.IsSpecialDependencyId</c>).
    /// </summary>
    [Fact]
    public void Analyze_PatchTargetingTheBaseGame_IsNotAnIntegration()
    {
        var report = GameLogAnalyzer.Analyze(Lines("""
        13.8.2026 21:08:24 [Client Error] Patch 3 in hearthside:patches/kitchen.json: File game:itemtypes/resource.json not found
        """));

        report.Integrations.ShouldBeEmpty();
    }

    [Fact]
    public void Analyze_TheSameMissingReferenceTwice_IsReportedOnce()
    {
        var report = GameLogAnalyzer.Analyze(Lines("""
        13.8.2026 21:08:24 [Client Error] Patch 0 in hearthside:patches/kitchen.json: File saltyseas:blocktypes/barrel.json not found
        13.8.2026 21:08:24 [Client Error] Patch 1 in hearthside:patches/kitchen.json: File saltyseas:blocktypes/crate.json not found
        """));

        report.Integrations.ShouldHaveSingleItem();
        report.Mods.ShouldHaveSingleItem().ErrorCount.ShouldBe(2);
    }

    [Fact]
    public void Analyze_ModThatPrefixesItsOwnMessages_IsRecognisedByThatPrefix()
    {
        var report = GameLogAnalyzer.Analyze(Lines("""
        13.8.2026 21:08:23 [Client Notification] Mods, sorted by dependency: carryon, game
        13.8.2026 21:08:26 [Client Warning] CarryOn: config file is malformed, falling back to defaults
        """));

        report.Mods.ShouldHaveSingleItem().ModId.ShouldBe("carryon");
    }

    [Fact]
    public void Analyze_MessagePrefixedWithACommonWord_IsNotAttributedToAnything()
    {
        var report = GameLogAnalyzer.Analyze(Lines("""
        13.8.2026 21:08:23 [Client Notification] Mods, sorted by dependency: carryon, game
        13.8.2026 21:08:26 [Client Warning] Warning: the world is running low on entropy
        """));

        report.Mods.ShouldBeEmpty();
    }

    /// <summary>
    /// Les journaux que le JEU écrit lui-même n'ont pas de côté dans leur marqueur
    /// (<c>[Error]</c> au lieu de <c>[Client Error]</c>) : la même lecture doit marcher sur les
    /// deux, parce que rien n'interdit de pointer l'analyse sur l'un ou sur l'autre.
    /// </summary>
    [Fact]
    public void Analyze_LogWrittenByTheGameItself_IsReadTheSameWay()
    {
        var report = GameLogAnalyzer.Analyze(Lines("""
        13.8.2026 22:09:27.512 [Notification] Mods, sorted by dependency: hearthside, game
        13.8.2026 22:09:27.548 [Error] [hearthside] Could not resolve some dependencies:
        """));

        report.Mods.ShouldHaveSingleItem().ModId.ShouldBe("hearthside");
    }

    [Fact]
    public void Analyze_ProspectHeaderAndNativeNoise_AreIgnored()
    {
        var report = GameLogAnalyzer.Analyze(Lines("""
        [2026-08-13T19:08:20.8838190+00:00] Lancement de « Survie » (1.22.6)
        [W][21:08:22.268151] pw.conf      | [conf.c: 1209] setting config.name is deprecated
        Sauvegarde automatique avant lancement échouée : disque plein
        """));

        report.Mods.ShouldBeEmpty();
        report.ObservedModIds.ShouldBeEmpty();
    }

    [Fact]
    public void Analyze_ManySamples_KeepsOnlyTheFirstFew()
    {
        var lines = Enumerable
            .Range(0, 10)
            .Select(index => $"13.8.2026 21:08:24 [Client Error] [hearthside] failure number {index}")
            .Prepend("13.8.2026 21:08:23 [Client Notification] Mods, sorted by dependency: hearthside, game");

        var report = GameLogAnalyzer.Analyze(lines);

        var mod = report.Mods.ShouldHaveSingleItem();
        mod.ErrorCount.ShouldBe(10);
        mod.Samples.Count.ShouldBe(GameLogAnalyzer.MaxSamplesPerMod);
        mod.Samples[0].ShouldContain("failure number 0");
    }

    [Fact]
    public void Analyze_AVeryLongLine_IsTruncatedInTheSample()
    {
        var report = GameLogAnalyzer.Analyze(Lines(
            "13.8.2026 21:08:24 [Client Error] [hearthside-1.0.0.zip] " + new string('x', 500)));

        var sample = report.Mods.ShouldHaveSingleItem().Samples.ShouldHaveSingleItem();
        sample.Length.ShouldBe(GameLogAnalyzer.MaxSampleLength + 1);
        sample.ShouldEndWith("…");
    }

    /// <summary>
    /// Le plafond de lignes est ce qui empêche un journal de plusieurs heures de coûter cher :
    /// c'est le DÉBUT qui décrit le lancement, et c'est lui qu'on garde.
    /// </summary>
    [Fact]
    public void Analyze_LogLongerThanTheCeiling_StopsReadingAtIt()
    {
        var read = 0;
        var lines = Enumerable
            .Range(0, GameLogAnalyzer.MaxLines + 500)
            .Select(index =>
            {
                read++;

                return $"13.8.2026 21:08:24 [Client Notification] line {index}";
            });

        GameLogAnalyzer.Analyze(lines);

        read.ShouldBe(GameLogAnalyzer.MaxLines);
    }

    [Fact]
    public void Analyze_KnownMods_MakeAnUnnamedArchiveRecognisable()
    {
        var report = GameLogAnalyzer.Analyze(
            Lines("13.8.2026 21:08:23 [Client Error] [Ruined Compass] failed to load its atlas"),
            [new ModLogIdentity("ruinedcompass", "ruinedcompass-0.4.2.zip", "Ruined Compass")]);

        report.Mods.ShouldHaveSingleItem().ModId.ShouldBe("ruinedcompass");
    }

    [Fact]
    public void Analyze_RealisticSession_ProducesAVerdictPerMod()
    {
        var report = GameLogAnalyzer.Analyze(FixtureLines());

        report.Mods.Select(mod => mod.ModId).ShouldBe(
            ["carryonlib", "hearthside", "ruinedcompass-0.4.2.zip", "primitivesurvival"],
            ignoreOrder: true);

        var hearthside = report.Mods.Single(mod => mod.ModId == "hearthside");
        hearthside.ErrorCount.ShouldBe(4);
        hearthside.WarningCount.ShouldBe(1);

        var primitiveSurvival = report.Mods.Single(mod => mod.ModId == "primitivesurvival");
        primitiveSurvival.ErrorCount.ShouldBe(0);
        primitiveSurvival.WarningCount.ShouldBe(1);

        report.Mods.Single(mod => mod.ModId == "carryonlib").ErrorCount.ShouldBe(3);
        report.Mods.Single(mod => mod.ModId == "ruinedcompass-0.4.2.zip").ErrorCount.ShouldBe(2);
    }

    [Fact]
    public void Analyze_RealisticSession_ReportsTheMissingCrossModReference()
    {
        var report = GameLogAnalyzer.Analyze(FixtureLines());

        var integration = report.Integrations.ShouldHaveSingleItem();
        integration.SourceModId.ShouldBe("hearthside");
        integration.TargetModId.ShouldBe("saltyseas");
        integration.Nature.ShouldBe(ModIntegrationNature.Missing);
    }

    [Fact]
    public void Analyze_RealisticSession_ListsTheModsTheLogNames()
    {
        var report = GameLogAnalyzer.Analyze(FixtureLines());

        report.ObservedModIds.ShouldContain("carryon");
        report.ObservedModIds.ShouldContain("hearthside");
        report.ObservedModIds.ShouldNotContain("game");
        report.ObservedModIds.ShouldNotContain("primitivesurvival-5.1.1.zip");
    }

    /// <summary>
    /// Les verdicts sortent triés du plus grave au moins grave : l'appelant qui n'en montre qu'un
    /// montre le pire, sans avoir à trier lui-même.
    /// </summary>
    [Fact]
    public void Analyze_SeveralMods_OrdersErrorsBeforeWarnings()
    {
        var report = GameLogAnalyzer.Analyze(Lines("""
        13.8.2026 21:08:23 [Client Notification] Mods, sorted by dependency: aaa, zzz, game
        13.8.2026 21:08:24 [Client Warning] [aaa] something mild
        13.8.2026 21:08:24 [Client Error] [zzz] something serious
        """));

        report.Mods.Select(mod => mod.ModId).ShouldBe(["zzz", "aaa"]);
    }
}