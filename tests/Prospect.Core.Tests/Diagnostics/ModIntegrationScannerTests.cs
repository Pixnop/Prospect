using System.IO.Abstractions.TestingHelpers;
using System.IO.Compression;
using System.Text;

using Prospect.Core.Diagnostics;

using Shouldly;

namespace Prospect.Core.Tests.Diagnostics;

/// <summary>
/// <see cref="ModIntegrationScanner"/> : ce qu'une archive annonce d'elle-même. Les formes de
/// patch reproduites ici sont celles de mods réels (un patch conditionné par <c>dependsOn</c>, un
/// patch qui vise directement le domaine d'un autre mod), lues dans des archives publiées.
/// </summary>
public sealed class ModIntegrationScannerTests
{
    private const string ArchivePath = "/data/prospect/instances/survie/data/Mods/carryon-2.0.0.zip";

    private static (ModIntegrationScanner Scanner, MockFileSystem FileSystem) Create(IReadOnlyDictionary<string, string> entries)
    {
        var fileSystem = new MockFileSystem();
        fileSystem.AddFile(ArchivePath, new MockFileData(BuildArchive(entries)));

        return (new ModIntegrationScanner(fileSystem), fileSystem);
    }

    private static byte[] BuildArchive(IReadOnlyDictionary<string, string> entries)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                var entry = archive.CreateEntry(name);
                using var stream = entry.Open();
                stream.Write(Encoding.UTF8.GetBytes(content));
            }
        }

        return buffer.ToArray();
    }

    [Fact]
    public void Scan_ArchiveThatDoesNotExist_ReturnsNothing()
    {
        var scanner = new ModIntegrationScanner(new MockFileSystem());

        scanner.Scan("/data/prospect/instances/survie/data/Mods/absent.zip", "carryon").ShouldBeEmpty();
    }

    [Fact]
    public void Scan_FileThatIsNotAZip_ReturnsNothingInsteadOfThrowing()
    {
        var fileSystem = new MockFileSystem();
        fileSystem.AddFile(ArchivePath, new MockFileData("ceci n'est pas une archive"));

        new ModIntegrationScanner(fileSystem).Scan(ArchivePath, "carryon").ShouldBeEmpty();
    }

    [Fact]
    public void Scan_PatchTargetingItsOwnDomain_IsNotAnIntegration()
    {
        var (scanner, _) = Create(new Dictionary<string, string>
        {
            ["modinfo.json"] = """{ "modid": "carryon", "name": "Carry On" }""",
            ["assets/carryon/patches/self.json"] = """[{ "file": "carryon:blocktypes/crate", "op": "add", "path": "/x", "value": 1 }]""",
        });

        scanner.Scan(ArchivePath, "carryon").ShouldBeEmpty();
    }

    [Fact]
    public void Scan_PatchTargetingTheBaseGame_IsNotAnIntegration()
    {
        var (scanner, _) = Create(new Dictionary<string, string>
        {
            ["assets/carryon/patches/game.json"] = """[{ "file": "game:blocktypes/chest", "op": "add", "path": "/behaviors/-", "value": {} }]""",
        });

        scanner.Scan(ArchivePath, "carryon").ShouldBeEmpty();
    }

    [Fact]
    public void Scan_PatchTargetingAnotherModWithoutCondition_IsAnUnconditionalSignal()
    {
        var (scanner, _) = Create(new Dictionary<string, string>
        {
            ["assets/carryon/patches/crates.json"] = """[{ "file": "bettercrates:blocktypes/bettercrates", "op": "add", "path": "/behaviors/-", "value": {} }]""",
        });

        var signal = scanner.Scan(ArchivePath, "carryon").ShouldHaveSingleItem();
        signal.TargetDomain.ShouldBe("bettercrates");
        signal.Conditional.ShouldBeFalse();
        signal.Evidence.ShouldBe("assets/carryon/patches/crates.json");
    }

    [Fact]
    public void Scan_PatchGuardedByDependsOn_IsAConditionalSignal()
    {
        var (scanner, _) = Create(new Dictionary<string, string>
        {
            ["assets/carryon/patches/crates.json"] = """
            [{
              "file": "bettercrates:blocktypes/bettercrates",
              "op": "add", "path": "/behaviors/-", "value": {},
              "dependsOn": [{ "modid": "bettercrates" }]
            }]
            """,
        });

        scanner.Scan(ArchivePath, "carryon").ShouldHaveSingleItem().Conditional.ShouldBeTrue();
    }

    /// <summary>
    /// Le cas qu'on raterait sans lire <c>dependsOn</c> : le patch vise le contenu du mod
    /// lui-même, et n'existe que si l'autre mod est là. C'est exactement ainsi qu'un mod de
    /// contenu rend ses blocs transportables par un mod de transport.
    /// </summary>
    [Fact]
    public void Scan_PatchOnItsOwnContentThatDependsOnAnotherMod_IsStillAnIntegration()
    {
        var (scanner, _) = Create(new Dictionary<string, string>
        {
            ["assets/primitivesurvival/patches/ps-carryon.json"] = """
            [{
              "file": "primitivesurvival:blocktypes/wood/treehollowplaced.json",
              "op": "add", "path": "/behaviors/-", "value": { "name": "Carryable" },
              "dependsOn": [{ "modid": "carryon" }]
            }]
            """,
        });

        var signal = scanner.Scan(ArchivePath, "primitivesurvival").ShouldHaveSingleItem();
        signal.TargetDomain.ShouldBe("carryon");
        signal.Conditional.ShouldBeTrue();
    }

    [Fact]
    public void Scan_AssetsFiledUnderAnotherModsDomain_IsAnIntegration()
    {
        var (scanner, _) = Create(new Dictionary<string, string>
        {
            ["assets/hearthside/blocktypes/oven.json"] = "{}",
        });

        scanner.Scan(ArchivePath, "carryon").ShouldHaveSingleItem().TargetDomain.ShouldBe("hearthside");
    }

    [Fact]
    public void Scan_TheSameTargetTwice_IsReportedOnceAndKeepsTheStrongerSignal()
    {
        var (scanner, _) = Create(new Dictionary<string, string>
        {
            ["assets/carryon/patches/a.json"] = """
            [{ "file": "bettercrates:blocktypes/a", "op": "add", "path": "/x", "value": 1, "dependsOn": [{ "modid": "bettercrates" }] }]
            """,
            ["assets/carryon/patches/b.json"] = """[{ "file": "bettercrates:blocktypes/b", "op": "add", "path": "/x", "value": 1 }]""",
        });

        var signal = scanner.Scan(ArchivePath, "carryon").ShouldHaveSingleItem();
        signal.TargetDomain.ShouldBe("bettercrates");
        signal.Conditional.ShouldBeFalse();
    }

    [Fact]
    public void Scan_PatchWrittenAsASingleObject_IsReadLikeAnArray()
    {
        var (scanner, _) = Create(new Dictionary<string, string>
        {
            ["assets/carryon/patches/single.json"] = """{ "file": "bettercrates:blocktypes/a", "op": "add", "path": "/x", "value": 1 }""",
        });

        scanner.Scan(ArchivePath, "carryon").ShouldHaveSingleItem().TargetDomain.ShouldBe("bettercrates");
    }

    [Fact]
    public void Scan_PatchWithCommentsAndTrailingCommas_IsStillRead()
    {
        var (scanner, _) = Create(new Dictionary<string, string>
        {
            ["assets/carryon/patches/loose.json"] = """
            [
              // ces fichiers sont écrits à la main
              { "File": "bettercrates:blocktypes/a", "op": "add", "path": "/x", "value": 1, },
            ]
            """,
        });

        scanner.Scan(ArchivePath, "carryon").ShouldHaveSingleItem().TargetDomain.ShouldBe("bettercrates");
    }

    [Fact]
    public void Scan_MalformedPatchFile_IsSkippedWithoutFailingTheRest()
    {
        var (scanner, _) = Create(new Dictionary<string, string>
        {
            ["assets/carryon/patches/broken.json"] = "{ pas du json",
            ["assets/carryon/patches/good.json"] = """[{ "file": "bettercrates:blocktypes/a", "op": "add", "path": "/x", "value": 1 }]""",
        });

        scanner.Scan(ArchivePath, "carryon").ShouldHaveSingleItem().TargetDomain.ShouldBe("bettercrates");
    }

    [Fact]
    public void Scan_PatchWithoutAFileProperty_IsIgnored()
    {
        var (scanner, _) = Create(new Dictionary<string, string>
        {
            ["assets/carryon/patches/odd.json"] = """["une chaîne", { "op": "add" }, 42]""",
        });

        scanner.Scan(ArchivePath, "carryon").ShouldBeEmpty();
    }

    [Fact]
    public void Scan_TargetWithoutADomain_IsIgnored()
    {
        var (scanner, _) = Create(new Dictionary<string, string>
        {
            ["assets/carryon/patches/relative.json"] = """[{ "file": "blocktypes/crate", "op": "add", "path": "/x", "value": 1 }]""",
        });

        scanner.Scan(ArchivePath, "carryon").ShouldBeEmpty();
    }

    /// <summary>
    /// Un mod de contenu peut porter des centaines de fichiers de patch et cette analyse tourne à
    /// l'ouverture d'un onglet : le plafond est ce qui empêche l'un de faire attendre l'autre.
    /// </summary>
    [Fact]
    public void Scan_ArchiveWithMorePatchFilesThanTheCeiling_StopsAtIt()
    {
        var entries = Enumerable
            .Range(0, ModIntegrationScanner.MaxPatchFiles + 20)
            .ToDictionary(
                index => $"assets/carryon/patches/target{index}.json",
                index => $$"""[{ "file": "mod{{index}}:blocktypes/a", "op": "add", "path": "/x", "value": 1 }]""");

        var (scanner, _) = Create(entries);

        scanner.Scan(ArchivePath, "carryon").Count.ShouldBe(ModIntegrationScanner.MaxPatchFiles);
    }
}