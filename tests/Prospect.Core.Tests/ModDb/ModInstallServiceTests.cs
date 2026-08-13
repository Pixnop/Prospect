using System.IO.Abstractions.TestingHelpers;
using System.Net;
using System.Text.Json;

using Prospect.Core.Common;
using Prospect.Core.Http;
using Prospect.Core.Instances;
using Prospect.Core.Instances.Migrations;
using Prospect.Core.ModDb;
using Prospect.Core.Storage;
using Prospect.Core.Tests.Common;
using Prospect.Core.Tests.Http;
using Prospect.Core.Tests.Storage;

using Shouldly;

namespace Prospect.Core.Tests.ModDb;

public sealed class ModInstallServiceTests
{
    private const string Slug = "homestead-121";

    private static readonly AppPaths Paths = new(new FakeAppEnvironment(), "/data/prospect");
    private static readonly DateTimeOffset Noon = new(2026, 8, 11, 12, 0, 0, TimeSpan.Zero);

    private sealed record Harness(
        ModInstallService Service,
        IInstalledModRepository Repository,
        MockFileSystem FileSystem,
        FakeModDbServer Server,
        FakeHttpMessageHandler Handler,
        DownloadManager Downloads)
    {
        /// <summary>Nombre de <c>GET</c> réellement partis vers le CDN pour une archive donnée.</summary>
        public int ArchiveRequests(string cdnFileName) => Handler.Requests.Count(
            request => request.Method == HttpMethod.Get && request.Url.AbsolutePath == $"/{cdnFileName}");

        /// <summary>
        /// Nombre de téléchargements ENGAGÉS pour une archive : ce que le popover affiche et ce que
        /// le journal consigne, réutilisation du cache comprise.
        /// </summary>
        public int DownloadOperations(string targetFileName) => Downloads.Operations.Count(
            operation => operation.FileName == targetFileName);
    }

    private static Harness Create(string gameVersion = "1.21.3")
    {
        var fileSystem = new MockFileSystem();
        var clock = new FakeClock(Noon);
        var store = new JsonFileStore(fileSystem);
        var instances = new FileSystemInstanceRepository(fileSystem, Paths, store, new InstanceMetadataMigrationPipeline([]));
        var archiveReader = new ModArchiveReader(fileSystem);
        var repository = new FileSystemInstalledModRepository(
            fileSystem,
            instances,
            archiveReader,
            new DisabledSuffixModStateConvention(),
            store);

        var server = new FakeModDbServer();
        var handler = new FakeHttpMessageHandler(server.Respond);
        var client = new ModDbClient(
            new HttpClient(handler),
            store,
            Paths,
            clock,
            new RetryPolicy(RetryOptions.NoDelay, (_, _) => Task.CompletedTask));
        var downloads = new DownloadManager(new HttpClient(handler), fileSystem, Paths, clock);

        SeedInstance(fileSystem, gameVersion);

        return new Harness(
            new ModInstallService(client, downloads, repository, instances, archiveReader, fileSystem, clock),
            repository,
            fileSystem,
            server,
            handler,
            downloads);
    }

    private static void SeedInstance(MockFileSystem fileSystem, string gameVersion)
    {
        var metadata = new InstanceMetadata
        {
            SchemaVersion = InstanceMetadata.CurrentSchemaVersion,
            Id = Guid.NewGuid(),
            Name = "Homestead 1.21",
            GameVersion = GameVersion.Parse(gameVersion),
            CreatedUtc = Noon,
        };

        fileSystem.AddFile(
            fileSystem.Path.Combine(Paths.InstancesDirectory, Slug, "instance.json"),
            new MockFileData(JsonSerializer.Serialize(metadata, InstanceJsonContext.Default.InstanceMetadata)));
        fileSystem.AddDirectory(fileSystem.Path.Combine(Paths.InstancesDirectory, Slug, "data", "Mods"));
    }

    // ── Choix de release ────────────────────────────────────────────────────────────

    [Fact]
    public async Task PrepareAsync_PicksTheNewestReleaseTaggedForTheInstanceGameVersion()
    {
        var harness = Create("1.21.3");

        var plan = await harness.Service.PrepareAsync(Slug, 1783, cancellationToken: CancellationToken.None);

        plan.Primary.Version.ShouldBe(ModVersion.Parse("1.11.1"));
        plan.Primary.IsApproximateMatch.ShouldBeFalse();
        plan.GameVersion.ShouldBe(GameVersion.Parse("1.21.3"));
    }

    /// <summary>
    /// Aucune release déclarée pour cette version de jeu : le plan s'ouvre QUAND MÊME, sur la
    /// meilleure release publiée, marquée pour ce qu'elle est.
    /// </summary>
    /// <remarks>
    /// Refuser de préparer fermait la seule porte : le docteur d'instance propose « Installer
    /// "carryonlib" », un mod dont aucune release ne coche la version courante, et l'échec de
    /// préparation empêchait le dialogue de s'ouvrir. Rien n'est installé en douce pour autant — le
    /// verdict de compatibilité voyage avec le plan, l'écran l'affiche, et c'est un clic de plus qui
    /// installe.
    /// </remarks>
    [Fact]
    public async Task PrepareAsync_NoReleaseForThatGameVersion_StillPlansTheBestPublishedOneAndFlagsIt()
    {
        var harness = Create("1.19.8");

        var plan = await harness.Service.PrepareAsync(Slug, 1783, cancellationToken: CancellationToken.None);

        plan.Primary.IsDeclaredIncompatible.ShouldBeTrue();
        plan.Primary.Version.ShouldBe(ModVersion.Parse("1.12.0"));
        plan.Primary.Release.CompatibleGameVersionTags.ShouldNotContain("1.19.8");

        // Et le sélecteur du dialogue a bien de quoi travailler : toutes les releases publiées y
        // sont, chacune avec son verdict.
        plan.AvailableReleases.ShouldNotBeEmpty();
        plan.AvailableReleases.ShouldAllBe(choice => choice.IsDeclaredIncompatible);
    }

    /// <summary>
    /// Une fiche SANS AUCUNE release reste une erreur : il n'y a rien à proposer, ni compatible ni
    /// pas. C'est le seul cas qui lève encore.
    /// </summary>
    [Fact]
    public async Task PrepareAsync_AModWithoutAnyReleaseAtAll_IsStillReportedAsAnError()
    {
        var harness = Create("1.21.3");
        harness.Server.PublishesNoRelease = true;

        var exception = await Should.ThrowAsync<ModReleaseNotFoundException>(
            () => harness.Service.PrepareAsync(Slug, 1783, cancellationToken: CancellationToken.None));

        exception.GameVersion.ShouldBe(GameVersion.Parse("1.21.3"));
    }

    /// <summary>
    /// Le cas de terrain exact : une release taguée sur la même série mineure que l'instance, mais
    /// pas sur sa version. Elle est retenue, et signalée comme SUPPOSÉE et non comme non déclarée.
    /// </summary>
    [Fact]
    public async Task PrepareAsync_OnlyTheSameMinorSeriesIsTagged_PlansItAsApproximateRatherThanRefusing()
    {
        var harness = Create("1.21.9");

        var plan = await harness.Service.PrepareAsync(Slug, 1783, cancellationToken: CancellationToken.None);

        plan.Primary.IsApproximateMatch.ShouldBeTrue();
        plan.Primary.IsDeclaredIncompatible.ShouldBeFalse();
        plan.Primary.Version.ShouldBe(ModVersion.Parse("1.11.1"));
    }

    [Fact]
    public async Task PrepareAsync_WidenedToTheMinorSeries_AcceptsAnApproximateMatchAndSaysSo()
    {
        var harness = Create("1.21.9");

        var plan = await harness.Service.PrepareAsync(
            Slug,
            1783,
            ModCompatibilityMode.WidenToMinorSeries,
            cancellationToken: CancellationToken.None);

        plan.Primary.IsApproximateMatch.ShouldBeTrue();
        plan.NeedsConfirmation.ShouldBeTrue();
    }

    [Fact]
    public async Task PrepareAsync_ListsEveryReleaseWithItsVerdict_CompatibleOnesFirst()
    {
        var harness = Create("1.21.3");

        var plan = await harness.Service.PrepareAsync(Slug, 1783, cancellationToken: CancellationToken.None);

        // Toutes les releases, chacune étiquetée : le dialogue n'en montre que les compatibles tant
        // qu'on ne lui demande pas le contraire, mais il doit avoir les autres sous la main.
        plan.AvailableReleases.Select(choice => choice.Release.Version.ToString()).ShouldBe(["1.11.1", "1.10.0", "1.12.0"]);
        plan.AvailableReleases.Take(2).ShouldAllBe(choice => choice.Compatibility == ModReleaseCompatibility.Declared);
        plan.AvailableReleases[2].Compatibility.ShouldBe(ModReleaseCompatibility.NotDeclared);

        // Et la sélection automatique reste ce qu'elle était : la plus récente DÉCLARÉE compatible.
        plan.AvailableReleases[0].Release.ReleaseId.ShouldBe(plan.Primary.Release.ReleaseId);
        plan.Primary.IsDeclaredIncompatible.ShouldBeFalse();
    }

    [Fact]
    public async Task PrepareAsync_WithAnExplicitRelease_PlansThatOneAndKeepsTheSameChoices()
    {
        // Le chemin du sélecteur de version : rien d'autre ne change, ni le mode de compatibilité,
        // ni la résolution de dépendances, ni la mécanique préparer/consentir/appliquer.
        var harness = Create("1.21.3");

        var plan = await harness.Service.PrepareAsync(Slug, 1783, releaseId: 37000, cancellationToken: CancellationToken.None);

        plan.Primary.Version.ShouldBe(ModVersion.Parse("1.10.0"));
        plan.Primary.Release.ReleaseId.ShouldBe(37000);
        plan.Primary.TargetFileName.ShouldBe("configlib-1.10.0.zip");
        plan.AvailableReleases.Count.ShouldBe(3);
    }

    [Fact]
    public async Task PrepareAsync_WithAnExplicitlyIncompatibleRelease_InstallsItAndMarksIt()
    {
        // « Il faut pouvoir installer des incompatibles » : les tags sont cochés à la main et
        // prennent du retard. Rien n'est élargi en silence, mais un choix explicite est honoré —
        // et laisse une trace, dans le plan puis dans la provenance.
        var harness = Create("1.21.3");

        var plan = await harness.Service.PrepareAsync(Slug, 1783, releaseId: 39980, cancellationToken: CancellationToken.None);

        plan.Primary.Version.ShouldBe(ModVersion.Parse("1.12.0"));
        plan.Primary.Compatibility.ShouldBe(ModReleaseCompatibility.NotDeclared);
        plan.Primary.IsDeclaredIncompatible.ShouldBeTrue();
        plan.Primary.IsApproximateMatch.ShouldBeTrue();
        plan.NeedsConfirmation.ShouldBeTrue();
    }

    [Fact]
    public async Task ApplyAsync_AnExplicitlyIncompatibleRelease_IsRecordedAsSuchInTheProvenance()
    {
        // La provenance ne doit pas faire passer ce choix pour une compatibilité confirmée : le
        // docteur d'instance pourra plus tard distinguer un mod posé en connaissance de cause d'un
        // mod devenu incompatible parce que l'instance a changé de version depuis.
        var harness = Create("1.21.3");
        var plan = await harness.Service.PrepareAsync(Slug, 1783, releaseId: 39980, cancellationToken: CancellationToken.None);

        var outcome = await harness.Service.ApplyAsync(Slug, plan, cancellationToken: CancellationToken.None);

        var provenance = outcome.Installed.ShouldHaveSingleItem().Provenance.ShouldNotBeNull();
        provenance.DeclaredIncompatible.ShouldBeTrue();
        provenance.ApproximateMatch.ShouldBeTrue();
    }

    [Fact]
    public async Task ApplyAsync_AReleaseTheAuthorDeclared_LeavesTheIncompatibleFlagOff()
    {
        var harness = Create("1.21.3");
        var plan = await harness.Service.PrepareAsync(Slug, 1783, cancellationToken: CancellationToken.None);

        var outcome = await harness.Service.ApplyAsync(Slug, plan, cancellationToken: CancellationToken.None);

        var provenance = outcome.Installed[0].Provenance.ShouldNotBeNull();
        provenance.DeclaredIncompatible.ShouldBeFalse();
        provenance.ApproximateMatch.ShouldBeFalse();
    }

    [Fact]
    public async Task PrepareAsync_ExplicitReleaseChangesTheDependenciesRead_FromTheChosenArchive()
    {
        // Les dépendances viennent du modinfo.json de l'archive TÉLÉCHARGÉE, jamais de l'API :
        // changer de release change donc réellement le plan, ce qui est la raison d'être du
        // recalcul déclenché par le sélecteur.
        var harness = Create("1.21.3");

        var newest = await harness.Service.PrepareAsync(Slug, 1783, cancellationToken: CancellationToken.None);
        var older = await harness.Service.PrepareAsync(Slug, 1783, releaseId: 37000, cancellationToken: CancellationToken.None);

        newest.MissingDependencies.Select(item => item.ModIdString).ShouldContain("vsimgui");
        older.MissingDependencies.ShouldBeEmpty();
        older.Issues.ShouldBeEmpty();
    }

    [Fact]
    public async Task PrepareAsync_ReleaseIdThatDoesNotExist_FallsBackToTheBestOneRatherThanFailing()
    {
        var harness = Create("1.21.3");

        // Un identifiant qui n'existe sur aucune release de la fiche : la seule cause plausible est
        // une fiche modifiée entre l'ouverture du dialogue et le clic. Refuser d'installer serait
        // une punition disproportionnée.
        var plan = await harness.Service.PrepareAsync(Slug, 1783, releaseId: 999999, cancellationToken: CancellationToken.None);

        plan.Primary.Version.ShouldBe(ModVersion.Parse("1.11.1"));
    }

    [Fact]
    public async Task ApplyAsync_AfterChoosingAnOlderRelease_InstallsThatVersionAndRecordsItsProvenance()
    {
        var harness = Create("1.21.3");
        var plan = await harness.Service.PrepareAsync(Slug, 1783, releaseId: 37000, cancellationToken: CancellationToken.None);

        var outcome = await harness.Service.ApplyAsync(Slug, plan, cancellationToken: CancellationToken.None);

        var installed = outcome.Installed.ShouldHaveSingleItem();
        installed.FileName.ShouldBe("configlib-1.10.0.zip");
        installed.Provenance.ShouldNotBeNull().ReleaseId.ShouldBe(37000);
        installed.Provenance.Version.ShouldBe(ModVersion.Parse("1.10.0"));
    }

    [Fact]
    public async Task PrepareAsync_UnknownMod_SurfacesTheApplicationLevelNotFound()
    {
        var harness = Create();

        var exception = await Should.ThrowAsync<ModDbApiException>(
            () => harness.Service.PrepareAsync(Slug, 999999, cancellationToken: CancellationToken.None));

        exception.IsNotFound.ShouldBeTrue();
    }

    // ── Nommage et provenance ───────────────────────────────────────────────────────

    [Fact]
    public async Task ApplyAsync_WritesTheArchiveUnderTheModIdAndVersionConvention()
    {
        var harness = Create();
        var plan = await harness.Service.PrepareAsync(Slug, 1783, cancellationToken: CancellationToken.None);

        await harness.Service.ApplyAsync(Slug, plan, cancellationToken: CancellationToken.None);

        var expected = harness.FileSystem.Path.Combine(harness.Repository.GetModsDirectory(Slug), "configlib-1.11.1.zip");
        harness.FileSystem.File.Exists(expected).ShouldBeTrue();
    }

    [Fact]
    public async Task ApplyAsync_NeverReusesThePublishedFileName()
    {
        // ExtraInfo-v2.2.1.zip côté ModDB pour une version qui vaut 2.2.1 : le nom publié est libre
        // et vient d'une source distante, il n'a rien à faire tel quel sur notre disque.
        var harness = Create("1.22.0");
        var plan = await harness.Service.PrepareAsync(Slug, 4400, cancellationToken: CancellationToken.None);

        await harness.Service.ApplyAsync(Slug, plan, cancellationToken: CancellationToken.None);

        var mods = harness.FileSystem.Directory.GetFiles(harness.Repository.GetModsDirectory(Slug));
        mods.ShouldHaveSingleItem().ShouldEndWith("extrainfo-2.2.1.zip");
    }

    [Fact]
    public async Task ApplyAsync_RecordsTheProvenanceTheZipCannotKnow()
    {
        var harness = Create();
        var plan = await harness.Service.PrepareAsync(Slug, 1783, cancellationToken: CancellationToken.None);

        await harness.Service.ApplyAsync(Slug, plan, cancellationToken: CancellationToken.None);

        var provenance = (await harness.Repository.LoadProvenanceAsync(Slug, CancellationToken.None))["configlib-1.11.1.zip"];
        provenance.ModId.ShouldBe(1783);
        provenance.ModIdString.ShouldBe("configlib");
        provenance.ReleaseId.ShouldBe(38314);
        provenance.FileId.ShouldBe(84120);
        provenance.Version.ShouldBe(ModVersion.Parse("1.11.1"));
        provenance.InstalledUtc.ShouldBe(Noon);
        provenance.ApproximateMatch.ShouldBeFalse();
    }

    [Fact]
    public async Task ApplyAsync_ApproximateMatch_IsRememberedInTheProvenance()
    {
        var harness = Create("1.21.9");
        var plan = await harness.Service.PrepareAsync(Slug, 1783, ModCompatibilityMode.WidenToMinorSeries, cancellationToken: CancellationToken.None);

        await harness.Service.ApplyAsync(Slug, plan, cancellationToken: CancellationToken.None);

        (await harness.Repository.LoadProvenanceAsync(Slug, CancellationToken.None))
            .Values.ShouldHaveSingleItem().ApproximateMatch.ShouldBeTrue();
    }

    [Fact]
    public async Task ApplyAsync_ReturnsTheInstalledModWithItsParsedMetadata()
    {
        var harness = Create();
        var plan = await harness.Service.PrepareAsync(Slug, 1783, cancellationToken: CancellationToken.None);

        var outcome = await harness.Service.ApplyAsync(Slug, plan, cancellationToken: CancellationToken.None);

        var installed = outcome.Installed.ShouldHaveSingleItem();
        installed.Identity.ShouldBe("configlib");
        installed.IsEnabled.ShouldBeTrue();
        installed.Provenance.ShouldNotBeNull();
    }

    // ── Plan de dépendances ─────────────────────────────────────────────────────────

    [Fact]
    public async Task PrepareAsync_MissingDependencyDeclaredInTheDownloadedModInfo_IsProposed()
    {
        var harness = Create();

        var plan = await harness.Service.PrepareAsync(Slug, 1783, cancellationToken: CancellationToken.None);

        plan.MissingDependencies.ShouldHaveSingleItem().ModIdString.ShouldBe("vsimgui");
        plan.Issues.ShouldContain(issue => issue.ModIdString == "vsimgui" && issue.Status == ModDependencyStatus.Missing);
        plan.NeedsConfirmation.ShouldBeTrue();
    }

    [Fact]
    public async Task PrepareAsync_DependencyReportedOnlyByInstallInformation_IsAlsoProposed()
    {
        // Le mod demandé ne déclare rien, c'est resolve-deps qui signale la dépendance transitive.
        var harness = Create("1.22.0");
        harness.Server.ResolvedDependencies = ["vsimgui"];

        var plan = await harness.Service.PrepareAsync(Slug, 4400, cancellationToken: CancellationToken.None);

        plan.MissingDependencies.ShouldHaveSingleItem().ModIdString.ShouldBe("vsimgui");
        plan.Issues.ShouldHaveSingleItem().ReportedByModDb.ShouldBeTrue();
    }

    [Fact]
    public async Task PrepareAsync_DependencyAlreadyInstalled_IsNotProposedAgain()
    {
        var harness = Create();
        await InstallDependencyAsync(harness);

        var plan = await harness.Service.PrepareAsync(Slug, 1783, cancellationToken: CancellationToken.None);

        plan.MissingDependencies.ShouldBeEmpty();
        plan.Issues.ShouldBeEmpty();
        plan.NeedsConfirmation.ShouldBeFalse();
    }

    [Fact]
    public async Task PrepareAsync_DependencyUnknownToTheModDb_IsReportedAsUnresolvedRatherThanIgnored()
    {
        var harness = Create();
        harness.Server.KnownDependency = false;

        var plan = await harness.Service.PrepareAsync(Slug, 1783, cancellationToken: CancellationToken.None);

        plan.MissingDependencies.ShouldBeEmpty();
        var unresolved = plan.UnresolvedDependencies.ShouldHaveSingleItem();
        unresolved.ModIdString.ShouldBe("vsimgui");
        unresolved.Reason.ShouldBe(ModDependencyResolution.NotOnModDb);

        // Pas de fiche, donc pas de nom : on ne peut pas nommer ce qu'on n'a pas trouvé.
        unresolved.ModName.ShouldBeNull();
        unresolved.DisplayName.ShouldBe("vsimgui");
        plan.NeedsConfirmation.ShouldBeTrue();
    }

    /// <summary>
    /// Le cas réel de la session de test Windows, reproduit sur la topologie exacte du ModDB :
    /// installer « Carry On » 2.0.0-pre.8 (tagué jusqu'à 1.22.6) sur une instance en 1.22.6, alors
    /// que sa dépendance <c>carryonlib</c> a bien une fiche mais dont la dernière release s'arrête à
    /// 1.22.4. Le plan annonçait « Introuvable sur le ModDB : carryonlib », ce qui est faux : la
    /// fiche existe (modid 4687), seules ses RELEASES manquent pour cette version du jeu.
    /// </summary>
    [Fact]
    public async Task PrepareAsync_DependencyOnTheModDbWithNoReleaseForThisGameVersion_IsNotCalledMissing()
    {
        var harness = Create(gameVersion: "1.22.6");

        var plan = await harness.Service.PrepareAsync(Slug, 890, cancellationToken: CancellationToken.None);

        plan.MissingDependencies.ShouldBeEmpty();
        var unresolved = plan.UnresolvedDependencies.ShouldHaveSingleItem();
        unresolved.ModIdString.ShouldBe("carryonlib");
        unresolved.Reason.ShouldBe(ModDependencyResolution.NoCompatibleRelease);

        // La fiche a été trouvée : on sait donc la NOMMER, ce qui est déjà la preuve qu'elle existe.
        unresolved.ModName.ShouldBe("CarryOnLib");
        unresolved.DisplayName.ShouldBe("CarryOnLib");
        plan.NeedsConfirmation.ShouldBeTrue();
    }

    /// <summary>
    /// Et la suite du même cas réel : le constat honnête ne suffit pas, il faut pouvoir agir. La
    /// meilleure release publiée de la fiche est proposée avec ses vrais tags, sans jamais être
    /// installée d'office.
    /// </summary>
    [Fact]
    public async Task PrepareAsync_DependencyWithNoCompatibleRelease_OffersItsBestPublishedReleaseAnyway()
    {
        var harness = Create(gameVersion: "1.22.6");

        var plan = await harness.Service.PrepareAsync(Slug, 890, cancellationToken: CancellationToken.None);

        var offer = plan.InstallableAnyway.ShouldHaveSingleItem();
        offer.ModIdString.ShouldBe("carryonlib");
        offer.BestAvailable.ShouldNotBeNull().Version.ToString().ShouldBe("1.0.0-pre.8");

        // 1.22.4 sur une instance en 1.22.6 : même série mineure, donc une compatibilité SUPPOSÉE,
        // pas une absence totale de déclaration. La nuance décide de ce que la provenance écrira.
        offer.BestAvailable.Compatibility.ShouldBe(ModReleaseCompatibility.SameMinorSeries);
        offer.BestAvailableGameVersions.ShouldContain("1.22.4");
        offer.BestAvailableGameVersions.ShouldNotContain("1.22.6");
    }

    [Fact]
    public async Task ApplyAsync_WithoutTickingTheOffer_InstallsTheModAloneAndReportsTheSkip()
    {
        // L'option part décochée : ne rien cocher doit donner exactement le comportement d'avant.
        var harness = Create(gameVersion: "1.22.6");
        var plan = await harness.Service.PrepareAsync(Slug, 890, cancellationToken: CancellationToken.None);

        var outcome = await harness.Service.ApplyAsync(Slug, plan, cancellationToken: CancellationToken.None);

        outcome.Installed.ShouldHaveSingleItem().Provenance!.ModIdString.ShouldBe("carryon");
        outcome.SkippedDependencies.ShouldContain("carryonlib");
    }

    [Fact]
    public async Task ApplyAsync_TickingTheOffer_InstallsTheDependencyInTheSameGesture()
    {
        // Le bout du cas réel : installer Carry On 2.0.0-pre.8 sur une instance en 1.22.6 ET
        // cocher CarryOnLib 1.0.0-pre.8, taguée jusqu'à 1.22.4, en un seul geste.
        var harness = Create(gameVersion: "1.22.6");
        var plan = await harness.Service.PrepareAsync(Slug, 890, cancellationToken: CancellationToken.None);

        var outcome = await harness.Service.ApplyAsync(Slug, plan, ["carryonlib"], cancellationToken: CancellationToken.None);

        outcome.Installed.Select(mod => mod.Provenance!.ModIdString).ShouldBe(["carryon", "carryonlib"]);
        outcome.SkippedDependencies.ShouldBeEmpty();

        var lib = outcome.Installed[1].Provenance.ShouldNotBeNull();
        lib.Version.ToString().ShouldBe("1.0.0-pre.8");
        lib.ApproximateMatch.ShouldBeTrue("la compatibilité n'est que supposée, jamais déclarée");
        lib.DeclaredIncompatible.ShouldBeFalse("1.22.4 et 1.22.6 sont la même série mineure");
    }

    [Fact]
    public async Task PrepareAsync_DependencyThatTheModDbDoesNotPublish_HasNothingToOffer()
    {
        // Contrôle négatif : on ne propose pas d'installer quand même ce qui n'existe pas.
        var harness = Create();
        harness.Server.KnownDependency = false;

        var plan = await harness.Service.PrepareAsync(Slug, 1783, cancellationToken: CancellationToken.None);

        plan.UnresolvedDependencies.ShouldHaveSingleItem().BestAvailable.ShouldBeNull();
        plan.InstallableAnyway.ShouldBeEmpty();
    }

    /// <summary>
    /// Le contrôle négatif du test précédent : sur une instance dont la version EST couverte par les
    /// tags de <c>carryonlib</c>, la même dépendance se résout normalement. Sans lui, le test
    /// ci-dessus passerait tout aussi bien si la résolution était cassée pour de bon.
    /// </summary>
    [Fact]
    public async Task PrepareAsync_SameDependencyOnAGameVersionItDoesCover_ResolvesNormally()
    {
        var harness = Create(gameVersion: "1.22.4");

        var plan = await harness.Service.PrepareAsync(Slug, 890, cancellationToken: CancellationToken.None);

        plan.UnresolvedDependencies.ShouldBeEmpty();
        var dependency = plan.MissingDependencies.ShouldHaveSingleItem();
        dependency.ModIdString.ShouldBe("carryonlib");
        dependency.Version.ToString().ShouldBe("1.0.0-pre.8");
    }

    [Fact]
    public async Task PrepareAsync_LeavesTheInstanceUntouchedUntilApplyIsCalled()
    {
        var harness = Create();

        await harness.Service.PrepareAsync(Slug, 1783, cancellationToken: CancellationToken.None);

        harness.FileSystem.Directory.GetFiles(harness.Repository.GetModsDirectory(Slug)).ShouldBeEmpty();
    }

    [Fact]
    public async Task ApplyAsync_UncheckedDependency_IsNeverInstalledSilently()
    {
        var harness = Create();
        var plan = await harness.Service.PrepareAsync(Slug, 1783, cancellationToken: CancellationToken.None);

        var outcome = await harness.Service.ApplyAsync(Slug, plan, selectedDependencies: null, cancellationToken: CancellationToken.None);

        outcome.Installed.ShouldHaveSingleItem().Identity.ShouldBe("configlib");
        outcome.SkippedDependencies.ShouldBe(["vsimgui"]);
    }

    [Fact]
    public async Task ApplyAsync_CheckedDependency_IsInstalledAlongsideTheMod()
    {
        var harness = Create();
        var plan = await harness.Service.PrepareAsync(Slug, 1783, cancellationToken: CancellationToken.None);

        var outcome = await harness.Service.ApplyAsync(Slug, plan, ["vsimgui"], cancellationToken: CancellationToken.None);

        outcome.Installed.Select(mod => mod.Identity).ShouldBe(["configlib", "vsimgui"]);
        outcome.SkippedDependencies.ShouldBeEmpty();
        harness.FileSystem.File
            .Exists(harness.FileSystem.Path.Combine(harness.Repository.GetModsDirectory(Slug), "vsimgui-1.3.0.zip"))
            .ShouldBeTrue();
    }

    // ── Garde-fou de taille, seul substitut à l'absence de checksum ──────────────────

    [Fact]
    public async Task PrepareAsync_TruncatedDownload_IsRejectedThankToTheAnnouncedContentLength()
    {
        var harness = Create();
        harness.Server.TruncateDownloads = true;

        await Should.ThrowAsync<ModInstallFailedException>(
            () => harness.Service.PrepareAsync(Slug, 1783, cancellationToken: CancellationToken.None));
    }

    // ── Désinstallation et vérification inverse ─────────────────────────────────────

    [Fact]
    public async Task PrepareUninstallAsync_ModThatOthersDependOn_NamesThem()
    {
        var harness = Create();
        var plan = await harness.Service.PrepareAsync(Slug, 1783, cancellationToken: CancellationToken.None);
        await harness.Service.ApplyAsync(Slug, plan, ["vsimgui"], cancellationToken: CancellationToken.None);

        var installed = await harness.Repository.ScanAsync(Slug, CancellationToken.None);
        var target = installed.Single(mod => mod.Identity == "vsimgui");

        var impact = await harness.Service.PrepareUninstallAsync(Slug, target, CancellationToken.None);

        impact.HasDependents.ShouldBeTrue();
        impact.DependentNames.ShouldBe(["Config lib"]);
    }

    [Fact]
    public async Task PrepareUninstallAsync_ModNobodyDependsOn_HasNoWarning()
    {
        var harness = Create();
        var plan = await harness.Service.PrepareAsync(Slug, 1783, cancellationToken: CancellationToken.None);
        await harness.Service.ApplyAsync(Slug, plan, ["vsimgui"], cancellationToken: CancellationToken.None);

        var installed = await harness.Repository.ScanAsync(Slug, CancellationToken.None);
        var target = installed.Single(mod => mod.Identity == "configlib");

        (await harness.Service.PrepareUninstallAsync(Slug, target, CancellationToken.None)).HasDependents.ShouldBeFalse();
    }

    [Fact]
    public async Task UninstallAsync_RemovesTheArchiveAndItsProvenance()
    {
        var harness = Create();
        var plan = await harness.Service.PrepareAsync(Slug, 1783, cancellationToken: CancellationToken.None);
        var outcome = await harness.Service.ApplyAsync(Slug, plan, cancellationToken: CancellationToken.None);

        await harness.Service.UninstallAsync(Slug, outcome.Installed[0], CancellationToken.None);

        (await harness.Repository.ScanAsync(Slug, CancellationToken.None)).ShouldBeEmpty();
        (await harness.Repository.LoadProvenanceAsync(Slug, CancellationToken.None)).ShouldBeEmpty();
    }

    [Fact]
    public async Task SetEnabledAsync_TogglesThroughTheRepositoryConvention()
    {
        var harness = Create();
        var plan = await harness.Service.PrepareAsync(Slug, 1783, cancellationToken: CancellationToken.None);
        var outcome = await harness.Service.ApplyAsync(Slug, plan, cancellationToken: CancellationToken.None);

        var disabled = await harness.Service.SetEnabledAsync(Slug, outcome.Installed[0], enabled: false, CancellationToken.None);

        disabled.IsEnabled.ShouldBeFalse();
        disabled.FilePath.ShouldEndWith(".zip.disabled");
    }

    // ── Convention de nommage ───────────────────────────────────────────────────────

    [Theory]
    [InlineData("configlib", "1.12.0", "configlib-1.12.0.zip")]
    [InlineData("carry_capacity", "1.8.0-rc.2", "carry_capacity-1.8.0-rc.2.zip")]
    [InlineData("../../evil", "1.0.0", "evil-1.0.0.zip")]
    [InlineData("!!!", "1.0.0", "mod-1.0.0.zip")]
    public void BuildFileName_KeepsASimpleNameThatCannotEscapeTheModsFolder(string modId, string version, string expected)
        => ModInstallService.BuildFileName(modId, ModVersion.Parse(version)).ShouldBe(expected);

    // ── Un seul téléchargement par archive ──────────────────────────────────────────

    /// <summary>
    /// Le défaut de terrain : chaque zip de mod partait DEUX fois, à deux secondes d'intervalle,
    /// une fois à la préparation et une fois à l'application.
    /// </summary>
    [Fact]
    public async Task PrepareThenApply_EngagesExactlyOneDownloadPerArchive()
    {
        var harness = Create("1.21.3");

        var plan = await harness.Service.PrepareAsync(Slug, 1783, cancellationToken: CancellationToken.None);
        await harness.Service.ApplyAsync(Slug, plan, cancellationToken: CancellationToken.None);

        harness.DownloadOperations("configlib-1.11.1.zip").ShouldBe(1);
        harness.ArchiveRequests("configlib_1.11.1.zip").ShouldBe(1);
    }

    [Fact]
    public async Task PrepareThenApply_WithADependency_EngagesExactlyOneDownloadPerArchive()
    {
        var harness = Create("1.21.3");

        var plan = await harness.Service.PrepareAsync(Slug, 1783, cancellationToken: CancellationToken.None);
        await harness.Service.ApplyAsync(Slug, plan, ["vsimgui"], cancellationToken: CancellationToken.None);

        harness.DownloadOperations("configlib-1.11.1.zip").ShouldBe(1);
        harness.ArchiveRequests("configlib_1.11.1.zip").ShouldBe(1);

        // La dépendance n'est téléchargée qu'à l'application : elle n'a jamais été préparée, donc
        // son unique téléchargement est bien le sien.
        harness.DownloadOperations("vsimgui-1.3.0.zip").ShouldBe(1);
        harness.ArchiveRequests("vsimgui_1.3.0.zip").ShouldBe(1);
    }

    /// <summary>
    /// Le fichier préparé s'est évaporé entre les deux temps (nettoyage du cache, purge manuelle) :
    /// l'application le retélécharge proprement plutôt que d'échouer.
    /// </summary>
    [Fact]
    public async Task ApplyAsync_ThePreparedFileVanishedFromTheCache_DownloadsItAgainRatherThanFailing()
    {
        var harness = Create("1.21.3");
        var plan = await harness.Service.PrepareAsync(Slug, 1783, cancellationToken: CancellationToken.None);

        harness.FileSystem.File.Delete(harness.FileSystem.Path.Combine(Paths.DownloadsCacheDirectory, "configlib-1.11.1.zip"));

        var outcome = await harness.Service.ApplyAsync(Slug, plan, cancellationToken: CancellationToken.None);

        outcome.Installed.ShouldHaveSingleItem().FileName.ShouldBe("configlib-1.11.1.zip");
        harness.DownloadOperations("configlib-1.11.1.zip").ShouldBe(2);
        harness.ArchiveRequests("configlib_1.11.1.zip").ShouldBe(2);
    }

    private static async Task InstallDependencyAsync(Harness harness)
    {
        var modsDirectory = harness.Repository.GetModsDirectory(Slug);
        harness.FileSystem.AddFile(
            harness.FileSystem.Path.Combine(modsDirectory, "vsimgui-1.3.0.zip"),
            new MockFileData(FakeModDbServer.VsImGuiArchive));

        await Task.CompletedTask;
    }

    /// <summary>
    /// Serveur ModDB factice : trois fiches, leurs releases et les archives correspondantes, plus
    /// les leviers dont les tests ont besoin (dépendance inconnue, resolve-deps peuplé,
    /// téléchargement tronqué). Aucun appel réseau réel ne peut sortir d'ici.
    /// </summary>
    private sealed class FakeModDbServer
    {
        public static readonly byte[] ConfigLibArchive = ModInfoSamples.BuildArchive(ModInfoSamples.ConfigLib);

        /// <summary>Une release ANTÉRIEURE de la même fiche, celle que le sélecteur de version permet de choisir.</summary>
        public static readonly byte[] ConfigLibOlderArchive = ModInfoSamples.BuildArchive("""
        { "type": "code", "name": "Config lib", "modid": "configlib", "version": "1.10.0", "authors": ["Maltiez"] }
        """);
        public static readonly byte[] ExtraInfoArchive = ModInfoSamples.BuildArchive(ModInfoSamples.ExtraInfo);

        public static readonly byte[] VsImGuiArchive = ModInfoSamples.BuildArchive("""
        { "type": "code", "name": "VS ImGui", "modid": "vsimgui", "version": "1.3.0", "authors": ["Maltiez"] }
        """);

        /// <summary>L'archive de la dépendance qu'on peut choisir d'installer malgré ses tags.</summary>
        public static readonly byte[] CarryOnLibArchive = ModInfoSamples.BuildArchive("""
        { "type": "code", "name": "CarryOnLib", "modid": "carryonlib", "version": "1.0.0-pre.8", "authors": ["NerdScurvy"] }
        """);

        /// <summary>
        /// Topologie du cas réel remonté par la session de test Windows : « Carry On » 2.0.0-pre.8
        /// dépend de <c>carryonlib</c>, dont la fiche EXISTE sur le ModDB (modid 4687, vérifié en
        /// direct) mais dont aucune release ne porte le tag 1.22.6.
        /// </summary>
        public static readonly byte[] CarryOnArchive = ModInfoSamples.BuildArchive("""
        {
            "type": "code", "name": "Carry On", "modid": "carryon", "version": "2.0.0-pre.8",
            "authors": ["NerdScurvy"],
            "dependencies": { "game": "1.22.0", "carryonlib": "1.0.0" }
        }
        """);

        /// <summary>Faux pour simuler une dépendance déclarée que le ModDB ne connaît pas.</summary>
        public bool KnownDependency { get; set; } = true;

        /// <summary>Identifiants que <c>resolve-deps</c> doit remonter.</summary>
        public IReadOnlyList<string> ResolvedDependencies { get; set; } = [];

        /// <summary>Vrai pour livrer moins d'octets que le <c>HEAD</c> n'en annonce.</summary>
        public bool TruncateDownloads { get; set; }

        /// <summary>Vrai pour servir la fiche de Config lib sans la moindre release publiée.</summary>
        public bool PublishesNoRelease { get; set; }

        public HttpResponseMessage Respond(HttpRequestMessage request)
        {
            var url = request.RequestUri!;

            if (url.Host == "moddbcdn.vintagestory.at")
            {
                return File(url.AbsolutePath, request.Method == HttpMethod.Head);
            }

            return url.AbsolutePath switch
            {
                "/api/mod/1783" or "/api/mod/configlib" => FakeHttpMessageHandler.Text(ConfigLibJson),
                "/api/mod/4400" or "/api/mod/extrainfo" => FakeHttpMessageHandler.Text(ExtraInfoJson),
                "/api/mod/vsimgui" when KnownDependency => FakeHttpMessageHandler.Text(VsImGuiJson),
                "/api/mod/890" or "/api/mod/carryon" => FakeHttpMessageHandler.Text(CarryOnJson),
                "/api/mod/4687" or "/api/mod/carryonlib" => FakeHttpMessageHandler.Text(CarryOnLibJson),
                "/api/v2/mods/install-information" => FakeHttpMessageHandler.Text(InstallInformationJson(url.Query)),
                _ => FakeHttpMessageHandler.Text(ModDbSamples.NotFound),
            };
        }

        private HttpResponseMessage File(string path, bool headOnly)
        {
            var payload = path switch
            {
                "/configlib_1.11.1.zip" => ConfigLibArchive,
                "/configlib_1.12.0.zip" => ConfigLibArchive,
                "/configlib_1.10.0.zip" => ConfigLibOlderArchive,
                "/extrainfo_2.2.1.zip" => ExtraInfoArchive,
                "/vsimgui_1.3.0.zip" => VsImGuiArchive,
                "/carryon_2.0.0-pre.8.zip" => CarryOnArchive,
                "/carryonlib_1.0.0-pre.8.zip" => CarryOnLibArchive,
                _ => null,
            };

            if (payload is null)
            {
                return FakeHttpMessageHandler.Status(HttpStatusCode.NotFound);
            }

            var body = !headOnly && TruncateDownloads ? payload[..(payload.Length / 2)] : payload;
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };
            if (headOnly)
            {
                response.Content.Headers.ContentLength = payload.Length;
            }

            return response;
        }

        // Le vrai serveur ne met dans `data` que les identifiants explicitement demandés, et place
        // les dépendances transitives dans `resolved` : le faux fait pareil, sinon il testerait un
        // contrat qui n'existe pas.
        private string InstallInformationJson(string query)
        {
            var requested = System.Web.HttpUtility.ParseQueryString(query)["ids"] ?? string.Empty;
            var resolved = string.Join(
                ',',
                ResolvedDependencies.Select(identifier => $$"""
                "{{identifier}}": { "version": "1.3.0", "fileName": "{{identifier}}.zip", "fileUrl": "/download/1/{{identifier}}.zip" }
                """));

            return $$"""
            { "data": { "{{requested}}": { "recommendedUpgrade": "1.12.0" } }, "resolved": { {{resolved}} } }
            """;
        }

        private string ConfigLibJson => PublishesNoRelease ? ConfigLibWithoutAnyReleaseJson : ConfigLibWithReleasesJson;

        /// <summary>
        /// Une fiche bien publiée mais SANS AUCUNE release. Rare, et c'est justement le seul cas où
        /// la préparation d'un plan n'a rien à proposer, donc le seul qui doive encore échouer.
        /// </summary>
        private const string ConfigLibWithoutAnyReleaseJson = """
        {
          "statuscode": "200",
          "mod": {
            "modid": 1783, "assetid": 9551, "name": "Config lib", "text": "<p>lib</p>", "author": "Maltiez",
            "urlalias": null, "logofile": null, "downloads": 627953, "side": "both", "type": "mod",
            "tags": ["Utility"], "lastreleased": null, "releases": []
          }
        }
        """;

        private const string ConfigLibWithReleasesJson = """
        {
          "statuscode": "200",
          "mod": {
            "modid": 1783, "assetid": 9551, "name": "Config lib", "text": "<p>lib</p>", "author": "Maltiez",
            "urlalias": null, "logofile": null, "downloads": 627953, "side": "both", "type": "mod",
            "tags": ["Utility"], "lastreleased": "2026-05-01 12:03:34",
            "releases": [
              { "releaseid": 39980, "fileid": 88961, "mainfile": "https://moddbcdn.vintagestory.at/configlib_1.12.0.zip",
                "filename": "configlib_1.12.0.zip", "downloads": 1, "tags": ["1.22.0"], "modidstr": "configlib",
                "modversion": "1.12.0", "changelog": null, "created": "2026-05-01 12:03:34" },
              { "releaseid": 38314, "fileid": 84120, "mainfile": "https://moddbcdn.vintagestory.at/configlib_1.11.1.zip",
                "filename": "configlib_1.11.1.zip", "downloads": 90210, "tags": ["1.21.3", "1.21.0"], "modidstr": "configlib",
                "modversion": "1.11.1", "changelog": "<p>Correction du <strong>rechargement</strong>.</p>",
                "created": "2026-02-11 09:22:10" },
              { "releaseid": 37000, "fileid": 82000, "mainfile": "https://moddbcdn.vintagestory.at/configlib_1.10.0.zip",
                "filename": "configlib_1.10.0.zip", "downloads": 41337, "tags": ["1.21.3"], "modidstr": "configlib",
                "modversion": "1.10.0", "changelog": null, "created": "2025-12-02 08:00:00" }
            ]
          }
        }
        """;

        private const string ExtraInfoJson = """
        {
          "statuscode": "200",
          "mod": {
            "modid": 4400, "assetid": 12000, "name": "Extra Info", "text": "<p>info</p>", "author": "Craluminum2413",
            "urlalias": null, "logofile": null, "downloads": 174994, "side": "client", "type": "mod",
            "tags": [], "lastreleased": "2026-04-01 10:00:00",
            "releases": [
              { "releaseid": 40100, "fileid": 90000, "mainfile": "https://moddbcdn.vintagestory.at/extrainfo_2.2.1.zip",
                "filename": "ExtraInfo-v2.2.1.zip", "downloads": 1, "tags": ["1.22.0"], "modidstr": "extrainfo",
                "modversion": "2.2.1", "changelog": null, "created": "2026-04-01 10:00:00" }
            ]
          }
        }
        """;

        // Les tags de release sont ceux relevés en direct le 2026-08-13 : carryon va jusqu'à 1.22.6,
        // carryonlib s'arrête à 1.22.4. C'est cet écart, et lui seul, qui rendait carryonlib
        // « introuvable » sur une instance en 1.22.6.
        private const string CarryOnJson = """
        {
          "statuscode": "200",
          "mod": {
            "modid": 890, "assetid": 559, "name": "Carry On", "text": "<p>carry</p>", "author": "NerdScurvy",
            "urlalias": "carryon", "logofile": null, "downloads": 1200000, "side": "both", "type": "mod",
            "tags": ["QoL"], "lastreleased": "2026-07-22 09:31:34",
            "releases": [
              { "releaseid": 50130, "fileid": 109190, "mainfile": "https://moddbcdn.vintagestory.at/carryon_2.0.0-pre.8.zip",
                "filename": "CarryOn-2.0.0-pre.8.zip", "downloads": 1,
                "tags": ["1.22.0", "1.22.1", "1.22.2", "1.22.3", "1.22.4", "1.22.5", "1.22.6"],
                "modidstr": "carryon", "modversion": "2.0.0-pre.8", "changelog": null, "created": "2026-07-22 09:31:34" }
            ]
          }
        }
        """;

        private const string CarryOnLibJson = """
        {
          "statuscode": "200",
          "mod": {
            "modid": 4687, "assetid": 27960, "name": "CarryOnLib", "text": "<p>lib</p>", "author": "NerdScurvy",
            "urlalias": "carryonlib", "logofile": null, "downloads": 59306, "side": "both", "type": "mod",
            "tags": ["Library"], "lastreleased": "2026-07-22 09:31:34",
            "releases": [
              { "releaseid": 50129, "fileid": 109189, "mainfile": "https://moddbcdn.vintagestory.at/carryonlib_1.0.0-pre.8.zip",
                "filename": "CarryOnLib-1.22.0_v1.0.0-pre.8.zip", "downloads": 1,
                "tags": ["1.22.0", "1.22.1", "1.22.2", "1.22.3", "1.22.4"],
                "modidstr": "carryonlib", "modversion": "1.0.0-pre.8", "changelog": null, "created": "2026-07-22 09:31:34" }
            ]
          }
        }
        """;

        private const string VsImGuiJson = """
        {
          "statuscode": "200",
          "mod": {
            "modid": 2000, "assetid": 8000, "name": "VS ImGui", "text": "", "author": "Maltiez",
            "urlalias": null, "logofile": null, "downloads": 100, "side": "client", "type": "mod",
            "tags": [], "lastreleased": "2026-01-01 10:00:00",
            "releases": [
              { "releaseid": 30000, "fileid": 70001, "mainfile": "https://moddbcdn.vintagestory.at/vsimgui_1.3.0.zip",
                "filename": "vsimgui_1.3.0.zip", "downloads": 1, "tags": ["1.21.3", "1.21.9", "1.22.0"], "modidstr": "vsimgui",
                "modversion": "1.3.0", "changelog": null, "created": "2026-01-01 10:00:00" }
            ]
          }
        }
        """;
    }
}