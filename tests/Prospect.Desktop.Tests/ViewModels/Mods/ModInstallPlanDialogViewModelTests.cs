using System.IO.Abstractions.TestingHelpers;

using Prospect.Core.Common;
using Prospect.Core.Instances;
using Prospect.Core.Instances.Migrations;
using Prospect.Core.ModDb;
using Prospect.Core.Storage;
using Prospect.Desktop.Resources;
using Prospect.Desktop.Services;
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
            new FakeModLogoCache(),
            new FakeModLogoDirectory());

    /// <summary>
    /// Le libellé suit ce que l'opération FAIT : ajouter tant que rien n'est installé, remplacer dès
    /// qu'une copie est là. Laisser « installer » sur un remplacement laisserait croire à un joueur
    /// qui change de version de mod qu'il en pose une seconde à côté.
    /// </summary>
    [Fact]
    public void APlanOverAnAlreadyInstalledMod_SaysReplaceRatherThanAdd()
    {
        var fresh = Dialog(PlanWith("1.22.6"));
        fresh.Title.ShouldContain("Installer");
        fresh.Message.ShouldContain("installée");

        var existing = new InstalledMod
        {
            FilePath = "/mods/carryon-1.9.0.zip",
            FileName = "carryon-1.9.0.zip",
            IsEnabled = true,
            Info = new ModInfo { ModId = "carryon", Name = "Carry On", Version = ModVersion.Parse("1.9.0") },
        };
        var replacement = Dialog(PlanWith("1.22.6") with { Existing = existing });

        replacement.Title.ShouldContain("Remplacer");
        replacement.Message.ShouldContain("1.9.0");
        replacement.Message.ShouldContain("2.0.0-pre.8");
    }

    /// <summary>Une copie installée dont le modinfo.json est illisible reste un remplacement, sans version à nommer.</summary>
    [Fact]
    public void APlanOverAnUnreadableInstalledCopy_StillSaysReplace()
    {
        var existing = new InstalledMod
        {
            FilePath = "/mods/carryon.zip",
            FileName = "carryon.zip",
            IsEnabled = true,
            Problem = ModInfoProblem.MissingModInfo,
        };

        var dialog = Dialog(PlanWith("1.22.6") with { Existing = existing });

        dialog.Title.ShouldContain("Remplacer");
        dialog.Message.ShouldContain("2.0.0-pre.8");
    }

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
    {
        var dialog = Dialog(PlanWith("1.22.6"));

        dialog.HasReleaseChoice.ShouldBeFalse();
        dialog.HasIncompatibleReleases.ShouldBeFalse();
    }

    [Fact]
    public void ADependencyWithNothingToOffer_ShowsNoInstallAnywayLine()
    {
        var dialog = Dialog(PlanWith(
            "1.22.6",
            new UnresolvedModDependency("mod-fantome", ModDependencyResolution.NotOnModDb)));

        dialog.HasInstallableAnyway.ShouldBeFalse();
        dialog.InstallableAnyway.ShouldBeEmpty();
    }

    // ── Sélecteur de version ─────────────────────────────────────────────────────────────────

    private sealed record Fixture(
        ModBrowserViewModel Browser,
        RecordingOverlayService Overlay,
        IInstalledModRepository Mods,
        FakeModDbHandler Handler,
        string Slug,
        ModInstallService InstallService);

    private static async Task<Fixture> CreateAsync(IModLogoDirectory? logoDirectory = null)
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
        var installService = ModDbDoubles.CreateInstallService(fileSystem, instances, mods, Paths, clock, handler);
        var browser = new ModBrowserViewModel(
            ModDbDoubles.CreateClient(fileSystem, Paths, clock, handler),
            installService,
            mods,
            instances,
            new FakeExternalUrlOpener(),
            overlay,
            new RecordingToastService(),
            ModDbDoubles.CreateLogoCache(handler),
            logoDirectory ?? ModDbDoubles.CreateLogoDirectory());

        await browser.InitializeCommand.ExecuteAsync(null);

        return new Fixture(browser, overlay, mods, handler, record.Slug, installService);
    }

    /// <summary>
    /// Ouvre le dialogue sur un mod désigné par son <c>modid</c> textuel, comme le fait le docteur
    /// d'instance quand il propose d'installer une dépendance manquante.
    /// </summary>
    private static async Task<ModInstallPlanDialogViewModel> OpenByIdentifierAsync(Fixture fixture, string modIdString)
    {
        var plan = await fixture.InstallService.PrepareAsync(fixture.Slug, modIdString, cancellationToken: CancellationToken.None);

        return new ModInstallPlanDialogViewModel(
            plan,
            "Homestead",
            (_, _) => Task.CompletedTask,
            releaseId => fixture.InstallService.PrepareAsync(fixture.Slug, modIdString, releaseId: releaseId, cancellationToken: CancellationToken.None),
            fixture.Overlay,
            new FakeExternalUrlOpener(),
            new FakeModLogoCache(),
            new FakeModLogoDirectory());
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
    public async Task SingleCompatibleRelease_StillOffersTheRevealWhenOtherReleasesExist()
    {
        var fixture = await CreateAsync();
        var dialog = await OpenAsync(fixture);

        // Config lib n'a qu'une release taguée 1.21.3 dans le faux serveur, mais elle en publie
        // une autre : le sélecteur reste utile, à condition de dévoiler.
        await fixture.Browser.Results.Single(card => card.Name == "Config lib").InstallCommand.ExecuteAsync(null);
        var single = fixture.Overlay.Shown.OfType<ModInstallPlanDialogViewModel>().Last();

        single.ShouldNotBe(dialog);
        single.Releases.Count.ShouldBe(1);
        single.ReleaseCountText.ShouldBe("1 version compatible");
        single.HasIncompatibleReleases.ShouldBeTrue();
    }

    // ── Dévoilement des releases non déclarées compatibles ───────────────────────────────────

    [Fact]
    public async Task Open_ShowsOnlyTheCompatibleReleasesUntilTheRevealIsAskedFor()
    {
        var fixture = await CreateAsync();

        var dialog = await OpenAsync(fixture);

        // Carry On publie une troisième release taguée pour une autre série : elle existe, mais
        // elle n'est pas offerte tant que rien ne l'a demandée. Pas d'élargissement silencieux.
        dialog.Releases.Count.ShouldBe(2);
        dialog.HasIncompatibleReleases.ShouldBeTrue();
        dialog.ShowsAllReleases.ShouldBeFalse();
        dialog.ToggleAllReleasesText.ShouldBe("Montrer toutes les versions");
        dialog.Releases.ShouldAllBe(release => !release.IsDeclaredIncompatible);
    }

    [Fact]
    public async Task ToggleAllReleases_RevealsTheIncompatibleOnesWithoutChangingThePlan()
    {
        var fixture = await CreateAsync();
        var dialog = await OpenAsync(fixture);
        var before = dialog.Plan;

        dialog.ToggleAllReleasesCommand.Execute(null);

        dialog.ShowsAllReleases.ShouldBeTrue();
        dialog.ToggleAllReleasesText.ShouldBe("Ne montrer que les compatibles");
        dialog.Releases.Count.ShouldBe(3);
        var revealed = dialog.Releases.Single(release => release.IsDeclaredIncompatible);
        revealed.VersionText.ShouldBe("1.13.0");
        revealed.CompatibilityTag.ShouldBe("non déclarée compatible");
        revealed.GameVersionsText.ShouldContain("1.20.4");

        // Dévoiler n'installe rien et ne change pas la présélection.
        dialog.Plan.ShouldBe(before);
        dialog.SelectedRelease.ShouldNotBeNull().ReleaseId.ShouldBe(NewestReleaseId);
        dialog.ShowIncompatibleWarning.ShouldBeFalse();
    }

    [Fact]
    public async Task SelectARevealedIncompatibleRelease_RecomputesThePlanAndWarnsWithoutBlocking()
    {
        var fixture = await CreateAsync();
        var dialog = await OpenAsync(fixture);
        dialog.ToggleAllReleasesCommand.Execute(null);

        dialog.SelectedRelease = dialog.Releases.Single(release => release.IsDeclaredIncompatible);
        await dialog.ReloadCompletion;

        dialog.Plan.Primary.Version.ShouldBe(ModVersion.Parse("1.13.0"));
        dialog.Plan.Primary.IsDeclaredIncompatible.ShouldBeTrue();
        dialog.FileNameText.ShouldBe("carryon-1.13.0.zip");

        // Averti, jamais bloqué : le bouton d'installation reste actif.
        dialog.ShowIncompatibleWarning.ShouldBeTrue();
        dialog.IncompatibleWarning.ShouldContain("1.21.3");
        dialog.IncompatibleWarning.ShouldContain("1.20.4");
        dialog.ShowApproximateWarning.ShouldBeFalse("les deux avertissements ne disent pas la même chose");
        dialog.ConfirmCommand.CanExecute(null).ShouldBeTrue();
    }

    [Fact]
    public async Task ConfirmARevealedIncompatibleRelease_InstallsItAndMarksItsProvenance()
    {
        var fixture = await CreateAsync();
        var dialog = await OpenAsync(fixture);
        dialog.ToggleAllReleasesCommand.Execute(null);
        dialog.SelectedRelease = dialog.Releases.Single(release => release.IsDeclaredIncompatible);
        await dialog.ReloadCompletion;

        await dialog.ConfirmCommand.ExecuteAsync(null);

        var installed = (await fixture.Mods.ScanAsync(fixture.Slug, CancellationToken.None))
            .Single(mod => mod.FileName == "carryon-1.13.0.zip");
        installed.Provenance.ShouldNotBeNull().DeclaredIncompatible.ShouldBeTrue();
    }

    [Fact]
    public async Task ToggleAllReleases_WhileAnIncompatibleOneIsSelected_StaysRevealed()
    {
        // Remasquer une release qu'on a choisie la ferait disparaître de l'écran tout en restant
        // celle qui s'installe : le dévoilement reste donc ouvert.
        var fixture = await CreateAsync();
        var dialog = await OpenAsync(fixture);
        dialog.ToggleAllReleasesCommand.Execute(null);
        dialog.SelectedRelease = dialog.Releases.Single(release => release.IsDeclaredIncompatible);
        await dialog.ReloadCompletion;

        dialog.ToggleAllReleasesCommand.Execute(null);

        dialog.ShowsAllReleases.ShouldBeTrue();
        dialog.Releases.Count.ShouldBe(3);
    }

    // ── Dépendance sans release compatible, installable quand même ───────────────────────────

    /// <summary>
    /// Renversement de défaut, décision produit : les librairies déclarées par un modinfo.json sont
    /// NÉCESSAIRES au fonctionnement du mod. Décochée, l'installation aboutissait sur un jeu qui ne
    /// démarre pas, sans que l'utilisateur fasse le lien. Le consentement reste — la case se décoche
    /// et l'avertissement reste attaché — mais le défaut devient « ça marchera ».
    /// </summary>
    [Fact]
    public async Task ADependencyWithNoCompatibleRelease_IsOfferedTickedWithItsRealTags()
    {
        var fixture = await CreateAsync();
        fixture.Handler.CarryOnLibGameVersions = ["1.22.0"];
        var dialog = await OpenAsync(fixture);

        dialog.HasNoCompatibleRelease.ShouldBeTrue("le constat honnête reste");
        dialog.HasInstallableAnyway.ShouldBeTrue("et il devient actionnable");

        var offer = dialog.InstallableAnyway.ShouldHaveSingleItem();
        offer.DisplayName.ShouldBe("CarryOnLib");
        offer.VersionText.ShouldBe("1.2.0");
        offer.ReasonText.ShouldContain("1.22.0");
        offer.IsDeclaredIncompatible.ShouldBeTrue();
        offer.IsSelected.ShouldBeTrue();

        // Le bandeau ambré n'est pas rendu deux fois : quand il y a des cases, il vit juste
        // au-dessus d'elles, jamais en plus dans un bloc à part.
        dialog.HasNoCompatibleReleaseWithoutOffer.ShouldBeFalse();
    }

    // ── Mod PRINCIPAL sans release compatible ────────────────────────────────────────────────

    /// <summary>
    /// Le chemin du docteur d'instance : « Installer "carryonlib" » ouvre un plan dont le mod
    /// PRINCIPAL n'a aucune release déclarée pour la version de l'instance.
    /// </summary>
    /// <remarks>
    /// Ce chemin ne menait nulle part. La préparation refusait de produire un plan, donc le dialogue
    /// ne s'ouvrait pas, donc le mod que le docteur venait de désigner comme manquant était
    /// impossible à installer — alors que le MÊME mod, atteint comme dépendance de <c>carryon</c>,
    /// se voyait proposé « installer quand même » depuis toujours. Le dialogue s'ouvre désormais
    /// d'emblée sur toutes les versions, puisque la liste des compatibles serait vide, et il porte
    /// l'avertissement qui dit pour quelles versions l'auteur a réellement déclaré la sienne.
    /// </remarks>
    [Fact]
    public async Task APlanWhoseMainModHasNoCompatibleRelease_OpensOnAllVersionsWithTheWarning()
    {
        var fixture = await CreateAsync();

        // Une fiche publiée dont aucune release ne touche la série 1.21 de l'instance.
        fixture.Handler.CarryOnLibGameVersions = ["1.19.8"];

        var dialog = await OpenByIdentifierAsync(fixture, "carryonlib");

        // Le dévoilement est déjà posé : sans lui, le sélecteur serait vide.
        dialog.ShowsAllReleases.ShouldBeTrue();
        dialog.HasReleaseChoice.ShouldBeTrue();
        dialog.ToggleAllReleasesText.ShouldBe(UiText.Mods.ShowCompatibleReleasesOnly);

        // Une release sélectionnable, et c'est la meilleure publiée.
        var release = dialog.Releases.ShouldHaveSingleItem();
        release.IsDeclaredIncompatible.ShouldBeTrue();
        dialog.SelectedRelease.ShouldNotBeNull().ReleaseId.ShouldBe(release.ReleaseId);

        // Et l'avertissement, avec les versions réellement taguées.
        dialog.ShowIncompatibleWarning.ShouldBeTrue();
        dialog.IncompatibleWarning.ShouldContain("1.19.8");
        dialog.ShowApproximateWarning.ShouldBeFalse("« non déclarée » n'est pas « supposée »");
    }

    /// <summary>Et l'installation aboutit vraiment : le dialogue n'est pas qu'un constat.</summary>
    [Fact]
    public async Task APlanWhoseMainModHasNoCompatibleRelease_CanStillBeConfirmed()
    {
        var fixture = await CreateAsync();
        fixture.Handler.CarryOnLibGameVersions = ["1.19.8"];

        var plan = await fixture.InstallService.PrepareAsync(fixture.Slug, "carryonlib", cancellationToken: CancellationToken.None);
        await fixture.InstallService.ApplyAsync(fixture.Slug, plan, cancellationToken: CancellationToken.None);

        var installed = await fixture.Mods.ScanAsync(fixture.Slug, CancellationToken.None);
        var mod = installed.ShouldHaveSingleItem();
        mod.Identity.ShouldBe("carryonlib");

        // La provenance garde la trace du choix : c'est elle qui saura, plus tard, que ce fichier a
        // été posé sans compatibilité déclarée.
        mod.Provenance.ShouldNotBeNull().DeclaredIncompatible.ShouldBeTrue();
    }

    /// <summary>
    /// Le cas voisin, et il ne doit PAS se confondre avec le précédent : une release taguée sur la
    /// même série mineure que l'instance est supposée compatible, pas non déclarée. Le dialogue
    /// s'ouvre alors normalement, sur la liste des compatibles.
    /// </summary>
    [Fact]
    public async Task APlanWhoseMainModIsOnlyTaggedForTheSameMinorSeries_OpensNormallyAndSaysApproximate()
    {
        var fixture = await CreateAsync();

        // 1.21.0 est de la même série que la 1.21.3 de l'instance, sans être cette version-là.
        fixture.Handler.CarryOnLibGameVersions = ["1.21.0"];

        var dialog = await OpenByIdentifierAsync(fixture, "carryonlib");

        dialog.ShowsAllReleases.ShouldBeFalse();
        dialog.ShowApproximateWarning.ShouldBeTrue();
        dialog.ShowIncompatibleWarning.ShouldBeFalse();
        dialog.Releases.ShouldHaveSingleItem().IsDeclaredIncompatible.ShouldBeFalse();
    }

    /// <summary>Une dépendance ordinaire, elle, était déjà cochée d'avance et le reste.</summary>
    [Fact]
    public async Task AnOrdinaryDependency_IsTickedTooAndTheTwoListsAgree()
    {
        var fixture = await CreateAsync();
        var dialog = await OpenAsync(fixture);

        dialog.Dependencies.ShouldHaveSingleItem().IsSelected.ShouldBeTrue();
        dialog.HasAnyDependencyNews.ShouldBeTrue();
    }

    [Fact]
    public async Task ConfirmAsIs_InstallsTheModAndTheOfferedDependency()
    {
        var fixture = await CreateAsync();
        fixture.Handler.CarryOnLibGameVersions = ["1.22.0"];
        var dialog = await OpenAsync(fixture);

        await dialog.ConfirmCommand.ExecuteAsync(null);

        var installed = await fixture.Mods.ScanAsync(fixture.Slug, CancellationToken.None);
        installed.Select(mod => mod.Provenance?.ModIdString).OrderBy(id => id).ShouldBe(["carryon", "carryonlib"]);

        // 1.22.0 sur une instance en 1.21.3 : autre série mineure. La provenance continue de le
        // marquer, même si la case était cochée d'avance.
        var lib = installed.Single(mod => mod.Provenance?.ModIdString == "carryonlib").Provenance!;
        lib.DeclaredIncompatible.ShouldBeTrue();
    }

    /// <summary>Décocher reste possible, et reste obéi : c'est le défaut qui a changé, pas le contrôle.</summary>
    [Fact]
    public async Task UntickingTheOffer_InstallsTheModAlone()
    {
        var fixture = await CreateAsync();
        fixture.Handler.CarryOnLibGameVersions = ["1.22.0"];
        var dialog = await OpenAsync(fixture);
        dialog.InstallableAnyway.ShouldHaveSingleItem().IsSelected = false;

        await dialog.ConfirmCommand.ExecuteAsync(null);

        var installed = await fixture.Mods.ScanAsync(fixture.Slug, CancellationToken.None);
        installed.Select(mod => mod.Provenance?.ModIdString).ShouldBe(["carryon"]);
    }

    // ── Vignettes ────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// L'en-tête montre CE QU'ON INSTALLE, à côté du titre qui le nomme. L'identifiant vient du
    /// plan lui-même : c'est le domaine qui l'a résolu, le dialogue ne cherche rien.
    /// </summary>
    [Fact]
    public async Task LEnTete_DemandeLeLogoDuModDuPlan()
    {
        var directory = new FakeModLogoDirectory((890, "https://moddbcdn.vintagestory.at/CarryOnLogo.png"));
        using var dialog = new ModInstallPlanDialogViewModel(
            PlanWith("1.22.6"),
            "Homestead 1.22",
            (_, _) => Task.CompletedTask,
            _ => Task.FromResult(PlanWith("1.22.6")),
            new RecordingOverlayService(),
            new FakeExternalUrlOpener(),
            new FakeModLogoCache(),
            directory);

        await dialog.Thumbnail.LoadCompletion;

        directory.RequestedModIds.ShouldBe([890]);
    }

    /// <summary>
    /// Le volet des dépendances NOMME des mods : il peut donc les montrer. Chaque ligne demande sa
    /// propre fiche, y compris les lignes « installer quand même ».
    /// </summary>
    [Fact]
    public async Task LesDependances_DemandentChacuneLeurLogo()
    {
        var directory = new FakeModLogoDirectory((4687, "https://moddbcdn.vintagestory.at/carryonlib.png"));
        var fixture = await CreateAsync(directory);
        var dialog = await OpenAsync(fixture);

        var dependency = dialog.Dependencies.Concat(dialog.InstallableAnyway).ShouldHaveSingleItem();
        await dependency.Thumbnail.LoadCompletion;

        directory.RequestedModIds.ShouldContain(4687);
    }

    /// <summary>
    /// Changer de release recalcule les dépendances : les anciennes lignes sont jetées, donc
    /// disposées, sinon chaque aller-retour dans le sélecteur laisserait une vignette en vol.
    /// </summary>
    [Fact]
    public async Task ChangerDeRelease_RemplaceLesLignesDeDependanceEtLeursVignettes()
    {
        var fixture = await CreateAsync(new FakeModLogoDirectory((4687, "https://moddbcdn.vintagestory.at/carryonlib.png")));
        var dialog = await OpenAsync(fixture);
        var before = dialog.Dependencies.Concat(dialog.InstallableAnyway).Single();

        dialog.SelectedRelease = dialog.Releases.Single(release => release.ReleaseId == OlderReleaseId);
        await dialog.ReloadCompletion;

        dialog.Dependencies.Concat(dialog.InstallableAnyway)
            .ShouldNotContain(row => ReferenceEquals(row, before), "les lignes de dépendance sont reconstruites");
    }
}