using System.IO.Abstractions.TestingHelpers;

using Prospect.Core.Common;
using Prospect.Core.Instances;
using Prospect.Core.Instances.Migrations;
using Prospect.Core.ModDb;
using Prospect.Core.Storage;
using Prospect.Desktop.Tests.TestDoubles;
using Prospect.Desktop.ViewModels.Mods;
using Prospect.Desktop.ViewModels.Toasts;

using Shouldly;

namespace Prospect.Desktop.Tests.ViewModels.Mods;

public sealed class ModBrowserViewModelTests
{
    private static readonly AppPaths Paths = new(new SystemAppEnvironment(), "/data/prospect");
    private static readonly DateTimeOffset Now = new(2026, 8, 11, 12, 0, 0, TimeSpan.Zero);

    private sealed record Fixture(
        ModBrowserViewModel Browser,
        FakeModDbHandler Handler,
        RecordingOverlayService Overlay,
        RecordingToastService Toasts,
        IInstalledModRepository Mods,
        MockFileSystem FileSystem,
        FakeExternalUrlOpener Opener,
        string Slug);

    private static async Task<Fixture> CreateAsync(string gameVersion = "1.21.3")
    {
        var fileSystem = new MockFileSystem();
        var clock = new FakeClock(Now);
        var instances = new FileSystemInstanceRepository(fileSystem, Paths, new JsonFileStore(fileSystem), new InstanceMetadataMigrationPipeline([]));
        var service = new InstanceService(instances, fileSystem, clock);
        var record = await service.CreateAsync("Homestead", GameVersion.Parse(gameVersion));

        var handler = new FakeModDbHandler();
        var mods = ModDbDoubles.CreateRepository(fileSystem, instances, Paths);
        var installService = ModDbDoubles.CreateInstallService(fileSystem, instances, mods, Paths, clock, handler);
        var client = ModDbDoubles.CreateClient(fileSystem, Paths, clock, handler);
        var logoCache = ModDbDoubles.CreateLogoCache(handler);
        var overlay = new RecordingOverlayService();
        var toasts = new RecordingToastService();

        var opener = new FakeExternalUrlOpener();
        var browser = new ModBrowserViewModel(client, installService, mods, instances, opener, overlay, toasts, logoCache);

        return new Fixture(browser, handler, overlay, toasts, mods, fileSystem, opener, record.Slug);
    }

    [Fact]
    public async Task InitializeAsync_LoadsTheCatalogTagsAndTargetInstances()
    {
        var fixture = await CreateAsync();

        await fixture.Browser.InitializeCommand.ExecuteAsync(null);

        fixture.Browser.Results.Count.ShouldBe(2);
        fixture.Browser.Tags.Select(tag => tag.Name).ShouldBe(["Exploration", "Utility", "Worldgen"]);
        fixture.Browser.TargetInstances.Count.ShouldBe(2);
        fixture.Browser.SubtitleText.ShouldContain("2 mods indexés");
    }

    [Fact]
    public async Task SearchText_FiltersInMemoryWithoutAnotherNetworkCall()
    {
        var fixture = await CreateAsync();
        await fixture.Browser.InitializeCommand.ExecuteAsync(null);

        fixture.Browser.SearchText = "ruins";

        fixture.Browser.Results.ShouldHaveSingleItem().Name.ShouldBe("BetterRuins");
    }

    [Fact]
    public async Task ClearSearch_RestoresEveryResult()
    {
        var fixture = await CreateAsync();
        await fixture.Browser.InitializeCommand.ExecuteAsync(null);
        fixture.Browser.SearchText = "ruins";

        fixture.Browser.ClearSearchCommand.Execute(null);

        fixture.Browser.Results.Count.ShouldBe(2);
    }

    [Fact]
    public async Task ToggleTag_FiltersOnThatCategoryThenReleasesItOnASecondClick()
    {
        var fixture = await CreateAsync();
        await fixture.Browser.InitializeCommand.ExecuteAsync(null);
        var utility = fixture.Browser.Tags.Single(tag => tag.Name == "Utility");

        await fixture.Browser.ToggleTagCommand.ExecuteAsync(utility);

        fixture.Browser.Results.ShouldHaveSingleItem().Name.ShouldBe("Config lib");
        utility.IsActive.ShouldBeTrue();

        await fixture.Browser.ToggleTagCommand.ExecuteAsync(utility);

        fixture.Browser.Results.Count.ShouldBe(2);
        utility.IsActive.ShouldBeFalse();
    }

    [Fact]
    public async Task SortIndex_ChangesTheOrderWithoutRefetching()
    {
        var fixture = await CreateAsync();
        await fixture.Browser.InitializeCommand.ExecuteAsync(null);

        fixture.Browser.SortIndex = (int)ModCatalogSort.Name;

        fixture.Browser.Results.Select(card => card.Name).ShouldBe(["BetterRuins", "Config lib"]);
    }

    [Fact]
    public async Task SelectedInstance_DrivesCompatibilityBadgesAndFiltersOutIncompatibleMods()
    {
        var fixture = await CreateAsync();
        fixture.Handler.CompatibleModIds = [1783];

        await fixture.Browser.InitializeCommand.ExecuteAsync(null);

        // Une instance réelle est sélectionnée d'office, comme dans la maquette.
        fixture.Browser.SelectedInstance!.Slug.ShouldBe(fixture.Slug);
        var card = fixture.Browser.Results.ShouldHaveSingleItem();
        card.Name.ShouldBe("Config lib");
        card.ShowCompatibility.ShouldBeTrue();
        card.CompatibilityTone.ShouldBe("stable");
        card.CompatibilityText.ShouldBe("1.21.3");
    }

    [Fact]
    public async Task SelectedInstance_AllVersions_ShowsEverythingWithoutBadges()
    {
        var fixture = await CreateAsync();
        await fixture.Browser.InitializeCommand.ExecuteAsync(null);

        fixture.Browser.SelectedInstance = fixture.Browser.TargetInstances[0];

        fixture.Browser.Results.Count.ShouldBe(2);
        fixture.Browser.Results.ShouldAllBe(card => !card.ShowCompatibility);
    }

    [Fact]
    public async Task PendingInstanceSlug_PreselectsThatInstanceOnLoad()
    {
        var fixture = await CreateAsync();
        fixture.Browser.PendingInstanceSlug = fixture.Slug;

        await fixture.Browser.InitializeCommand.ExecuteAsync(null);

        fixture.Browser.SelectedInstance!.Slug.ShouldBe(fixture.Slug);
        fixture.Browser.PendingInstanceSlug.ShouldBeNull();
    }

    [Fact]
    public async Task InitializeAsync_ModDbUnreachable_ShowsTheOfflineBannerAndItsEmptyState()
    {
        var fixture = await CreateAsync();
        fixture.Handler.IsOnline = false;

        await fixture.Browser.InitializeCommand.ExecuteAsync(null);

        fixture.Browser.IsOffline.ShouldBeTrue();
        fixture.Browser.Results.ShouldBeEmpty();
        fixture.Browser.ShowEmptyState.ShouldBeTrue();
        fixture.Browser.EmptyStateTitle.ShouldBe("Aucun résultat hors ligne");
    }

    [Fact]
    public async Task RefreshAsync_AfterComingBackOnline_ClearsTheOfflineBanner()
    {
        var fixture = await CreateAsync();
        fixture.Handler.IsOnline = false;
        await fixture.Browser.InitializeCommand.ExecuteAsync(null);

        fixture.Handler.IsOnline = true;
        await fixture.Browser.RefreshCommand.ExecuteAsync(null);

        fixture.Browser.IsOffline.ShouldBeFalse();
        fixture.Browser.Results.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task OpenCard_ShowsTheDetailDialog()
    {
        var fixture = await CreateAsync();
        await fixture.Browser.InitializeCommand.ExecuteAsync(null);

        await fixture.Browser.Results.Single(card => card.Name == "Config lib").OpenCommand.ExecuteAsync(null);

        var dialog = fixture.Overlay.Shown.ShouldHaveSingleItem().ShouldBeOfType<ModDetailDialogViewModel>();
        dialog.Name.ShouldBe("Config lib");

        // La description ModDB arrive en HTML et elle est RENDUE : le titre reste un titre, le
        // paragraphe reste un paragraphe. L'aplatir en une chaîne de texte, comme le faisait cette
        // assertion, était précisément le défaut relevé en test réel.
        var blocks = dialog.Description.Document.Blocks;
        blocks.Count.ShouldBe(2);
        blocks[0].ShouldBeOfType<RichTextHeading>().Level.ShouldBe(2);
        blocks[1].ShouldBeOfType<RichTextParagraph>().Runs.ShouldHaveSingleItem()
            .Text.ShouldBe("A universal place to configure your mods.");

        // Config lib n'a pas d'urlalias : la page se construit sur son ASSETID (9551), jamais sur
        // son modid (1783), qui désigne un tout autre asset sur le site.
        dialog.PageUrlText.ShouldBe("https://mods.vintagestory.at/show/mod/9551");
    }

    [Fact]
    public async Task OpenCard_OnAModWithAnUrlAlias_UsesTheShortRouteForItsPage()
    {
        var fixture = await CreateAsync();
        await fixture.Browser.InitializeCommand.ExecuteAsync(null);

        await fixture.Browser.Results.Single(card => card.Name == "BetterRuins").OpenCommand.ExecuteAsync(null);

        fixture.Overlay.Shown.ShouldHaveSingleItem().ShouldBeOfType<ModDetailDialogViewModel>()
            .PageUrlText.ShouldBe("https://mods.vintagestory.at/betterruins");
    }

    [Fact]
    public async Task OpenCard_ExternalLinksDeclaredByTheAuthor_AreOfferedAndOpenedOutside()
    {
        var fixture = await CreateAsync();
        fixture.Handler.CatalogJson = FakeModDbHandler.CatalogWith(FakeModDbHandler.CarryOnCatalogEntry);
        fixture.Handler.CompatibleModIds = [1783, 792, 890];
        await fixture.Browser.InitializeCommand.ExecuteAsync(null);

        await fixture.Browser.Results.Single(card => card.Name == "Carry On").OpenCommand.ExecuteAsync(null);
        var dialog = fixture.Overlay.Shown.ShouldHaveSingleItem().ShouldBeOfType<ModDetailDialogViewModel>();

        // Carry On déclare son dépôt et son suivi de tickets, pas de site ni de wiki : les champs
        // vides de l'API (chaîne vide plutôt que null, voir la recherche) ne produisent pas de
        // bouton mort.
        dialog.Links.Select(link => link.Label).ShouldBe(["Code source", "Tickets"]);

        await dialog.Links[0].OpenCommand.ExecuteAsync(null);
        fixture.Opener.Opened.ShouldHaveSingleItem().ShouldBe(new Uri("https://github.com/NerdScurvy/CarryOn"));
    }

    [Fact]
    public async Task OpenCard_ARealDescription_IsRenderedAsBlocksWithItsLinksAndImages()
    {
        var fixture = await CreateAsync();
        fixture.Handler.CatalogJson = FakeModDbHandler.CatalogWith(FakeModDbHandler.CarryOnCatalogEntry);
        fixture.Handler.CompatibleModIds = [1783, 792, 890];
        await fixture.Browser.InitializeCommand.ExecuteAsync(null);

        await fixture.Browser.Results.Single(card => card.Name == "Carry On").OpenCommand.ExecuteAsync(null);
        var dialog = fixture.Overlay.Shown.ShouldHaveSingleItem().ShouldBeOfType<ModDetailDialogViewModel>();

        // 29 Ko d'éditeur WYSIWYG réel : la fiche doit en sortir une structure, pas un mur de texte.
        var blocks = dialog.Description.Document.Blocks;
        blocks.OfType<RichTextHeading>().Count().ShouldBeGreaterThan(20);
        blocks.OfType<RichTextList>().Count().ShouldBeGreaterThan(20);
        blocks.OfType<RichTextImage>().Count().ShouldBe(4);
        dialog.Description.Images.Count.ShouldBe(4);
    }

    [Fact]
    public async Task Install_ShowsThePlanDialogRatherThanInstallingStraightAway()
    {
        var fixture = await CreateAsync();
        await fixture.Browser.InitializeCommand.ExecuteAsync(null);

        await fixture.Browser.Results.Single(card => card.Name == "Config lib").InstallCommand.ExecuteAsync(null);

        var dialog = fixture.Overlay.Shown.ShouldHaveSingleItem().ShouldBeOfType<ModInstallPlanDialogViewModel>();
        dialog.FileNameText.ShouldBe("configlib-1.11.1.zip");
        (await fixture.Mods.ScanAsync(fixture.Slug, CancellationToken.None)).ShouldBeEmpty();
    }

    [Fact]
    public async Task ConfirmingThePlan_InstallsIntoTheSelectedInstanceAndReportsIt()
    {
        var fixture = await CreateAsync();
        await fixture.Browser.InitializeCommand.ExecuteAsync(null);
        await fixture.Browser.Results.Single(card => card.Name == "Config lib").InstallCommand.ExecuteAsync(null);
        var dialog = (ModInstallPlanDialogViewModel)fixture.Overlay.Shown[0];

        await dialog.ConfirmCommand.ExecuteAsync(null);

        (await fixture.Mods.ScanAsync(fixture.Slug, CancellationToken.None)).ShouldHaveSingleItem().Identity.ShouldBe("configlib");
        fixture.Toasts.Shown.ShouldContain(toast => toast.Tone == ToastTone.Success && toast.Title == "Config lib installé");
    }

    [Fact]
    public async Task Install_WithoutATargetInstance_AsksForOneInsteadOfGuessing()
    {
        var fixture = await CreateAsync();
        await fixture.Browser.InitializeCommand.ExecuteAsync(null);
        fixture.Browser.SelectedInstance = fixture.Browser.TargetInstances[0];

        await fixture.Browser.Results[0].InstallCommand.ExecuteAsync(null);

        fixture.Overlay.Shown.ShouldBeEmpty();
        fixture.Toasts.Shown.ShouldContain(toast => toast.Title == "Choisis une instance");
    }

    /// <summary>
    /// Un mod sans release compatible n'est plus refusé d'un toast : le plan s'ouvre, avec son
    /// avertissement et toutes ses versions, et rien n'est posé tant que rien n'est confirmé.
    /// </summary>
    /// <remarks>
    /// La règle est la même partout depuis le défaut du docteur d'instance (voir
    /// <c>ModInstallService.PrepareAsync</c>) : on n'oppose pas une fin de non-recevoir à une
    /// compatibilité que l'auteur a simplement oublié de cocher, on ouvre en le disant. Ce qui
    /// compte, et que ce test garde, c'est que l'ouverture n'installe rien.
    /// </remarks>
    [Fact]
    public async Task Install_ModWithoutACompatibleRelease_OpensThePlanWithItsWarningAndInstallsNothing()
    {
        // BetterRuins n'a de release taguée que pour 1.22.0 : rien pour l'instance en 1.21.3.
        var fixture = await CreateAsync();
        await fixture.Browser.InitializeCommand.ExecuteAsync(null);

        await fixture.Browser.Results.Single(card => card.Name == "BetterRuins").InstallCommand.ExecuteAsync(null);

        var dialog = fixture.Overlay.Shown.OfType<ModInstallPlanDialogViewModel>().Last();
        dialog.ShowsAllReleases.ShouldBeTrue();
        dialog.ShowIncompatibleWarning.ShouldBeTrue();
        dialog.IncompatibleWarning.ShouldContain("1.22.0");
        dialog.SelectedRelease.ShouldNotBeNull();

        (await fixture.Mods.ScanAsync(fixture.Slug, CancellationToken.None)).ShouldBeEmpty();
    }

    // ── Ce que l'instance ciblée contient déjà ────────────────────────────────────────────────
    //
    // « Je ne dois pas pouvoir retélécharger un mod déjà téléchargé pour l'instance où je suis. »
    // Une carte doit donc DIRE ce qui est là, et son bouton ouvrir la fiche — où vivent le
    // sélecteur de release et le remplacement — au lieu de relancer un téléchargement aveugle.

    [Fact]
    public async Task ACardWhoseModIsAlreadyInTheTargetInstance_SaysSoAndOffersToManageIt()
    {
        var fixture = await CreateAsync();
        ModDbDoubles.SeedMod(
            fixture.FileSystem,
            fixture.Mods.GetModsDirectory(fixture.Slug),
            "configlib-1.11.1.zip",
            ModDbDoubles.ModInfo("configlib", "Config lib", "1.11.1"));

        await fixture.Browser.InitializeCommand.ExecuteAsync(null);

        var installed = fixture.Browser.Results.Single(card => card.Name == "Config lib");
        installed.IsInstalled.ShouldBeTrue();
        installed.InstalledText.ShouldBe("Installé · 1.11.1");
        installed.ActionLabel.ShouldBe("Gérer");

        var untouched = fixture.Browser.Results.Single(card => card.Name == "BetterRuins");
        untouched.IsInstalled.ShouldBeFalse();
        untouched.ActionLabel.ShouldBe("Installer");
    }

    [Fact]
    public async Task TheButtonOfAnInstalledCard_OpensItsSheetInsteadOfDownloadingAgain()
    {
        var fixture = await CreateAsync();
        ModDbDoubles.SeedMod(
            fixture.FileSystem,
            fixture.Mods.GetModsDirectory(fixture.Slug),
            "configlib-1.11.1.zip",
            ModDbDoubles.ModInfo("configlib", "Config lib", "1.11.1"));
        await fixture.Browser.InitializeCommand.ExecuteAsync(null);

        await fixture.Browser.Results.Single(card => card.Name == "Config lib").InstallCommand.ExecuteAsync(null);

        fixture.Overlay.Shown.ShouldHaveSingleItem().ShouldBeOfType<ModDetailDialogViewModel>();
    }

    [Fact]
    public async Task SwitchingTargetInstance_RecomputesWhatIsInstalled()
    {
        var fixture = await CreateAsync();
        ModDbDoubles.SeedMod(
            fixture.FileSystem,
            fixture.Mods.GetModsDirectory(fixture.Slug),
            "configlib-1.11.1.zip",
            ModDbDoubles.ModInfo("configlib", "Config lib", "1.11.1"));
        await fixture.Browser.InitializeCommand.ExecuteAsync(null);
        fixture.Browser.Results.Single(card => card.Name == "Config lib").IsInstalled.ShouldBeTrue();

        // « Toutes les versions » n'est l'instance de personne : plus rien à affirmer.
        fixture.Browser.SelectedInstance = fixture.Browser.TargetInstances[0];
        await Task.Yield();

        fixture.Browser.Results.ShouldAllBe(card => !card.IsInstalled);
    }

    [Fact]
    public async Task InstallingFromTheBrowser_FlipsThatCardWithoutLeavingTheScreen()
    {
        var fixture = await CreateAsync();
        await fixture.Browser.InitializeCommand.ExecuteAsync(null);
        var card = fixture.Browser.Results.Single(candidate => candidate.Name == "Config lib");
        card.IsInstalled.ShouldBeFalse();

        await card.InstallCommand.ExecuteAsync(null);
        await ((ModInstallPlanDialogViewModel)fixture.Overlay.Shown[0]).ConfirmCommand.ExecuteAsync(null);

        card.IsInstalled.ShouldBeTrue();
        card.InstalledText.ShouldBe("Installé · 1.11.1");
        card.ActionLabel.ShouldBe("Gérer");
    }

    [Fact]
    public void Constructor_NullArguments_AreRejected()
    {
        var fileSystem = new MockFileSystem();
        var clock = new FakeClock(Now);
        var instances = new FileSystemInstanceRepository(fileSystem, Paths, new JsonFileStore(fileSystem), new InstanceMetadataMigrationPipeline([]));
        var mods = ModDbDoubles.CreateRepository(fileSystem, instances, Paths);
        var installService = ModDbDoubles.CreateInstallService(fileSystem, instances, mods, Paths, clock);
        var client = ModDbDoubles.CreateClient(fileSystem, Paths, clock);
        var opener = new FakeExternalUrlOpener();
        var overlay = new RecordingOverlayService();
        var toasts = new RecordingToastService();
        var logoCache = ModDbDoubles.CreateLogoCache();

        Should.Throw<ArgumentNullException>(() => new ModBrowserViewModel(null!, installService, mods, instances, opener, overlay, toasts, logoCache));
        Should.Throw<ArgumentNullException>(() => new ModBrowserViewModel(client, null!, mods, instances, opener, overlay, toasts, logoCache));
        Should.Throw<ArgumentNullException>(() => new ModBrowserViewModel(client, installService, null!, instances, opener, overlay, toasts, logoCache));
        Should.Throw<ArgumentNullException>(() => new ModBrowserViewModel(client, installService, mods, null!, opener, overlay, toasts, logoCache));
        Should.Throw<ArgumentNullException>(() => new ModBrowserViewModel(client, installService, mods, instances, null!, overlay, toasts, logoCache));
        Should.Throw<ArgumentNullException>(() => new ModBrowserViewModel(client, installService, mods, instances, opener, null!, toasts, logoCache));
        Should.Throw<ArgumentNullException>(() => new ModBrowserViewModel(client, installService, mods, instances, opener, overlay, null!, logoCache));
        Should.Throw<ArgumentNullException>(() => new ModBrowserViewModel(client, installService, mods, instances, opener, overlay, toasts, null!));
    }
}