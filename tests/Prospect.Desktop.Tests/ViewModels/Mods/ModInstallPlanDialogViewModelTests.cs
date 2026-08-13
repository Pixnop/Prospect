using System.IO.Abstractions.TestingHelpers;

using Prospect.Core.Common;
using Prospect.Core.Instances;
using Prospect.Core.Instances.Migrations;
using Prospect.Core.ModDb;
using Prospect.Core.Storage;
using Prospect.Desktop.Tests.TestDoubles;
using Prospect.Desktop.ViewModels.Mods;

using Shouldly;

namespace Prospect.Desktop.Tests.ViewModels.Mods;

/// <summary>
/// Le dialogue d'installation : ce qu'il dit d'une dépendance qu'il n'a pas su résoudre, et ce que
/// son sélecteur de version recalcule.
/// </summary>
/// <remarks>
/// <para>
/// Sur les dépendances, le dialogue doit dire la VÉRITÉ. Il n'avait qu'un seul texte, « Introuvable
/// sur le ModDB », servi indistinctement dans les deux cas — d'où le mensonge relevé en conditions
/// réelles sur <c>carryonlib</c>, dont la fiche existe bel et bien.
/// </para>
/// <para>
/// Sur le sélecteur, le scénario est monté sur la fiche RÉELLE de Carry On (deux releases
/// compatibles, l'une avec une dépendance déclarée et l'autre sans) parce que c'est là qu'il cesse
/// d'être cosmétique : les dépendances se lisent dans le <c>modinfo.json</c> de l'archive
/// téléchargée, pas dans l'API, donc changer de release change le plan pour de vrai — verdicts de
/// résolution compris.
/// </para>
/// </remarks>
public sealed class ModInstallPlanDialogViewModelTests
{
    private const int NewestReleaseId = 52086;
    private const int OlderReleaseId = 51002;

    private static readonly AppPaths Paths = new(new SystemAppEnvironment(), "/data/prospect");
    private static readonly DateTimeOffset Now = new(2026, 8, 13, 12, 0, 0, TimeSpan.Zero);

    // ── Verdicts de résolution des dépendances ───────────────────────────────────────────────

    private static ModInstallPlan PlanWith(string gameVersion, params UnresolvedModDependency[] unresolved)
    {
        var release = new ModDbRelease
        {
            ReleaseId = 50130,
            FileId = 109190,
            ModIdString = "carryon",
            Version = ModVersion.Parse("2.0.0-pre.8"),
            FileName = "CarryOn-2.0.0-pre.8.zip",
            DownloadUrl = new Uri("https://moddbcdn.vintagestory.at/carryon_2.0.0-pre.8.zip"),
            CompatibleGameVersions = [GameVersion.Parse(gameVersion)],
            CreatedUtc = null,
            Changelog = null,
        };

        var primary = new ModInstallItem(890, "Carry On", release, IsApproximateMatch: false, "carryon-2.0.0-pre.8.zip", 1024);

        return new ModInstallPlan(primary, [], [], unresolved, GameVersion.Parse(gameVersion));
    }

    // Le sélecteur de version n'entre pas en jeu sur ces plans-là : ils ne portent aucune release
    // candidate, donc le rappel de recalcul n'est jamais invoqué.
    private static ModInstallPlanDialogViewModel Dialog(ModInstallPlan plan)
        => new(
            plan,
            "Homestead 1.22",
            (_, _) => Task.CompletedTask,
            _ => Task.FromResult(plan),
            new RecordingOverlayService(),
            new FakeExternalUrlOpener(),
            new FakeModLogoCache());

    /// <summary>Le cas réel : la fiche existe, seules ses releases manquent pour cette version.</summary>
    [Fact]
    public void ADependencyPublishedWithoutACompatibleRelease_IsNeverCalledNotFound()
    {
        var dialog = Dialog(PlanWith(
            "1.22.6",
            new UnresolvedModDependency("carryonlib", ModDependencyResolution.NoCompatibleRelease, "CarryOnLib")));

        dialog.HasUnresolved.ShouldBeFalse("la fiche existe : rien à annoncer comme introuvable");
        dialog.UnresolvedMessage.ShouldBeEmpty();

        dialog.HasNoCompatibleRelease.ShouldBeTrue();
        dialog.NoCompatibleReleaseMessage.ShouldNotContain("Introuvable");

        // Le texte nomme la fiche ET la version du jeu : sans elle, l'utilisateur ne sait pas
        // pourquoi rien ne convient.
        dialog.NoCompatibleReleaseMessage.ShouldContain("CarryOnLib");
        dialog.NoCompatibleReleaseMessage.ShouldContain("1.22.6");
    }

    /// <summary>Et l'inverse : « introuvable » reste dit, mais seulement quand c'est vrai.</summary>
    [Fact]
    public void ADependencyThatTheModDbReallyDoesNotPublish_IsStillCalledNotFound()
    {
        var dialog = Dialog(PlanWith(
            "1.22.6",
            new UnresolvedModDependency("mod-fantome", ModDependencyResolution.NotOnModDb)));

        dialog.HasUnresolved.ShouldBeTrue();
        dialog.UnresolvedMessage.ShouldContain("Introuvable");
        dialog.UnresolvedMessage.ShouldContain("mod-fantome");

        dialog.HasNoCompatibleRelease.ShouldBeFalse();
        dialog.NoCompatibleReleaseMessage.ShouldBeEmpty();
    }

    /// <summary>Les deux verdicts peuvent coexister, et restent alors séparés à l'écran.</summary>
    [Fact]
    public void BothVerdictsAtOnce_AreReportedSeparately()
    {
        var dialog = Dialog(PlanWith(
            "1.22.6",
            new UnresolvedModDependency("mod-fantome", ModDependencyResolution.NotOnModDb),
            new UnresolvedModDependency("carryonlib", ModDependencyResolution.NoCompatibleRelease, "CarryOnLib")));

        dialog.HasUnresolved.ShouldBeTrue();
        dialog.HasNoCompatibleRelease.ShouldBeTrue();
        dialog.UnresolvedMessage.ShouldNotContain("CarryOnLib");
        dialog.NoCompatibleReleaseMessage.ShouldNotContain("mod-fantome");
    }

    [Fact]
    public void NoUnresolvedDependencyAtAll_ShowsNeitherBand()
    {
        var dialog = Dialog(PlanWith("1.22.6"));

        dialog.HasUnresolved.ShouldBeFalse();
        dialog.HasNoCompatibleRelease.ShouldBeFalse();
        dialog.Plan.NeedsConfirmation.ShouldBeFalse();
    }

    [Fact]
    public void PlanWithoutAnyCandidateRelease_HidesTheSelectorInsteadOfShowingAnEmptyOne()
        => Dialog(PlanWith("1.22.6")).HasReleaseChoice.ShouldBeFalse();

    // ── Sélecteur de version ─────────────────────────────────────────────────────────────────

    private sealed record Fixture(
        ModBrowserViewModel Browser,
        RecordingOverlayService Overlay,
        IInstalledModRepository Mods,
        FakeModDbHandler Handler,
        string Slug);

    private static async Task<Fixture> CreateAsync()
    {
        var fileSystem = new MockFileSystem();
        var clock = new FakeClock(Now);
        var instances = new FileSystemInstanceRepository(fileSystem, Paths, new JsonFileStore(fileSystem), new InstanceMetadataMigrationPipeline([]));
        var record = await new InstanceService(instances, fileSystem, clock).CreateAsync("Homestead", GameVersion.Parse("1.21.3"));

        var handler = new FakeModDbHandler
        {
            CatalogJson = FakeModDbHandler.CatalogWith(FakeModDbHandler.CarryOnCatalogEntry),
            CompatibleModIds = [1783, 792, 890],
        };

        var mods = ModDbDoubles.CreateRepository(fileSystem, instances, Paths);
        var overlay = new RecordingOverlayService();
        var browser = new ModBrowserViewModel(
            ModDbDoubles.CreateClient(fileSystem, Paths, clock, handler),
            ModDbDoubles.CreateInstallService(fileSystem, instances, mods, Paths, clock, handler),
            instances,
            new FakeExternalUrlOpener(),
            overlay,
            new RecordingToastService(),
            ModDbDoubles.CreateLogoCache(handler));

        await browser.InitializeCommand.ExecuteAsync(null);

        return new Fixture(browser, overlay, mods, handler, record.Slug);
    }

    private static async Task<ModInstallPlanDialogViewModel> OpenAsync(Fixture fixture)
    {
        await fixture.Browser.Results.Single(card => card.Name == "Carry On").InstallCommand.ExecuteAsync(null);

        return fixture.Overlay.Shown.OfType<ModInstallPlanDialogViewModel>().Last();
    }

    [Fact]
    public async Task Open_OffersEveryCompatibleRelease_NewestPreselected()
    {
        var fixture = await CreateAsync();

        var dialog = await OpenAsync(fixture);

        dialog.HasReleaseChoice.ShouldBeTrue();
        dialog.Releases.Select(release => release.VersionText).ShouldBe(["1.14.3", "1.14.2"]);
        dialog.SelectedRelease.ShouldNotBeNull().ReleaseId.ShouldBe(NewestReleaseId);
        dialog.Plan.Primary.Version.ShouldBe(ModVersion.Parse("1.14.3"));
        dialog.ReleaseCountText.ShouldBe("2 versions compatibles");
    }

    [Fact]
    public async Task Open_EachRelease_CarriesItsDateDownloadsAndChangelog()
    {
        var fixture = await CreateAsync();

        var dialog = await OpenAsync(fixture);

        var newest = dialog.Releases[0];
        newest.DateText.ShouldBe("2026-08-07");
        newest.IsApproximate.ShouldBeFalse();

        // Le décompte suit la convention de milliers de la langue. L'espace exacte utilisée par
        // fr-FR (fine insécable ou insécable selon la version d'ICU) n'est pas notre décision :
        // c'est la SÉPARATION qui est vérifiée, pas le point de code.
        newest.DownloadsText.Replace(' ', ' ').Replace(' ', ' ').ShouldBe("6 729");

        // Le changelog est du HTML lui aussi : il passe par le même parseur que la description, ce
        // que rien dans l'API ne dit et que seul le relevé réel établit.
        newest.HasChangelog.ShouldBeTrue();
        var list = newest.Changelog.Document.Blocks.ShouldHaveSingleItem().ShouldBeOfType<RichTextList>();
        list.Items.Count.ShouldBe(2);
        list.Items[1].Runs.ShouldContain(run => run.Style.HasFlag(RichTextStyle.Bold));

        // Et l'absence de notes est un état à part entière, pas un bloc vide.
        dialog.Releases[1].HasChangelog.ShouldBeFalse();
    }

    [Fact]
    public async Task SelectAnotherRelease_RecomputesThePlanIncludingItsDependencies()
    {
        var fixture = await CreateAsync();
        var dialog = await OpenAsync(fixture);

        // La 1.14.3 déclare carryonlib, la 1.14.2 non : c'est la preuve que le plan est bien
        // REFAIT et pas seulement ré-étiqueté.
        dialog.HasDependencies.ShouldBeTrue();
        dialog.Dependencies.ShouldHaveSingleItem().ModIdString.ShouldBe("carryonlib");

        dialog.SelectedRelease = dialog.Releases[1];
        await dialog.ReloadCompletion;

        dialog.Plan.Primary.Version.ShouldBe(ModVersion.Parse("1.14.2"));
        dialog.Plan.Primary.Release.ReleaseId.ShouldBe(OlderReleaseId);
        dialog.FileNameText.ShouldBe("carryon-1.14.2.zip");
        dialog.HasDependencies.ShouldBeFalse();
        dialog.Dependencies.ShouldBeEmpty();
        dialog.ReloadFailed.ShouldBeFalse();
    }

    [Fact]
    public async Task SelectAnotherRelease_ReposesBothResolutionVerdicts()
    {
        // Le sélecteur ne doit pas laisser derrière lui un verdict calculé pour l'AUTRE release.
        // Le montage reproduit le cas réel qui a motivé les deux verdicts : CarryOnLib est bien
        // publiée, mais sa seule release ne cible pas la version de l'instance. La 1.14.3 en
        // dépend, la 1.14.2 non — le bandeau doit donc apparaître puis disparaître.
        var fixture = await CreateAsync();
        fixture.Handler.CarryOnLibGameVersions = ["1.22.0"];
        var dialog = await OpenAsync(fixture);

        dialog.HasNoCompatibleRelease.ShouldBeTrue();
        dialog.NoCompatibleReleaseMessage.ShouldContain("CarryOnLib");
        dialog.HasUnresolved.ShouldBeFalse("la fiche existe : rien à annoncer comme introuvable");

        dialog.SelectedRelease = dialog.Releases[1];
        await dialog.ReloadCompletion;

        dialog.HasNoCompatibleRelease.ShouldBeFalse();
        dialog.NoCompatibleReleaseMessage.ShouldBeEmpty();
        dialog.HasUnresolved.ShouldBeFalse();
        dialog.UnresolvedMessage.ShouldBeEmpty();
    }

    [Fact]
    public async Task Confirm_AfterChangingTheRelease_InstallsTheChosenOne()
    {
        var fixture = await CreateAsync();
        var dialog = await OpenAsync(fixture);

        dialog.SelectedRelease = dialog.Releases[1];
        await dialog.ReloadCompletion;
        await dialog.ConfirmCommand.ExecuteAsync(null);

        var installed = await fixture.Mods.ScanAsync(fixture.Slug, CancellationToken.None);
        installed.ShouldHaveSingleItem().FileName.ShouldBe("carryon-1.14.2.zip");
    }

    [Fact]
    public async Task Confirm_WithoutTouchingTheSelector_StillInstallsTheBestMatch()
    {
        // La garantie de non-régression du volet : le chemin d'installation d'avant le sélecteur
        // doit rester exactement celui-là, sans clic de plus.
        var fixture = await CreateAsync();
        var dialog = await OpenAsync(fixture);

        await dialog.ConfirmCommand.ExecuteAsync(null);

        var installed = await fixture.Mods.ScanAsync(fixture.Slug, CancellationToken.None);
        installed.Select(mod => mod.FileName).ShouldContain("carryon-1.14.3.zip");
    }

    [Fact]
    public async Task SelectAnotherRelease_WhenTheModDbGoesAway_KeepsThePlanOnScreenAndSaysSo()
    {
        var fixture = await CreateAsync();
        var dialog = await OpenAsync(fixture);
        var before = dialog.Plan;

        fixture.Handler.IsOnline = false;
        dialog.SelectedRelease = dialog.Releases[1];
        await dialog.ReloadCompletion;

        dialog.ReloadFailed.ShouldBeTrue();
        dialog.Plan.ShouldBe(before);
        dialog.SelectedRelease.ShouldNotBeNull().ReleaseId.ShouldBe(NewestReleaseId);
    }

    [Fact]
    public async Task SingleCompatibleRelease_HidesTheSelectorRatherThanShowingAChoiceOfOne()
    {
        var fixture = await CreateAsync();
        var dialog = await OpenAsync(fixture);

        // Config lib n'a qu'une release taguée 1.21.3 dans le faux serveur.
        await fixture.Browser.Results.Single(card => card.Name == "Config lib").InstallCommand.ExecuteAsync(null);
        var single = fixture.Overlay.Shown.OfType<ModInstallPlanDialogViewModel>().Last();

        single.ShouldNotBe(dialog);
        single.Releases.Count.ShouldBe(1);
        single.HasReleaseChoice.ShouldBeFalse();
    }
}