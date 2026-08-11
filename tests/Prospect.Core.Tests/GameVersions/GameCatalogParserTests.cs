using System.Text.Json;

using Prospect.Core.Common;
using Prospect.Core.GameVersions;

using Shouldly;

namespace Prospect.Core.Tests.GameVersions;

public sealed class GameCatalogParserTests
{
    [Fact]
    public void Merge_RealSamples_KeepsEveryPublishedVersion()
    {
        var versions = GameCatalogParser.Merge(GameCatalogSamples.Stable, GameCatalogSamples.Unstable);

        versions.Select(entry => entry.Version.ToString())
            .ShouldBe(["1.23.0-rc.1", "1.22.6", "1.21.3"]);
    }

    [Fact]
    public void Merge_RealSamples_DerivesChannelFromVersionName()
    {
        var versions = GameCatalogParser.Merge(GameCatalogSamples.Stable, GameCatalogSamples.Unstable);

        versions.Single(entry => entry.Version == GameVersion.Parse("1.23.0-rc.1")).Channel.ShouldBe(GameVersionChannel.Rc);
        versions.Single(entry => entry.Version == GameVersion.Parse("1.22.6")).Channel.ShouldBe(GameVersionChannel.Stable);
    }

    [Fact]
    public void Merge_RealSamples_ReadsEverySevenPlatformKeys()
    {
        var versions = GameCatalogParser.Merge(GameCatalogSamples.Stable, GameCatalogSamples.Empty);

        var latest = versions.Single(entry => entry.Version == GameVersion.Parse("1.22.6"));
        latest.Assets.Keys.OrderBy(key => key, StringComparer.Ordinal).ShouldBe(
        [
            GamePlatforms.Linux,
            GamePlatforms.LinuxServer,
            GamePlatforms.MacArm64,
            GamePlatforms.MacX64,
            GamePlatforms.Windows,
            GamePlatforms.WindowsServer,
            GamePlatforms.WindowsUpdate,
        ]);
    }

    [Fact]
    public void Merge_RealSamples_KeepsFileSizeAsTheHumanStringAndBothMirrors()
    {
        var versions = GameCatalogParser.Merge(GameCatalogSamples.Stable, GameCatalogSamples.Empty);

        var asset = versions.Single(entry => entry.Version == GameVersion.Parse("1.22.6")).Assets[GamePlatforms.Linux];

        asset.FileName.ShouldBe("vs_client_linux-x64_1.22.6.tar.gz");
        asset.DisplaySize.ShouldBe("590.5 MB");
        asset.Md5.ShouldBe("c00c436c7d8e9f0a1b2c3d4e5f6a7b8c");
        asset.IsLatest.ShouldBeTrue();
        asset.CdnUrl.Host.ShouldBe("cdn.vintagestory.at");
        asset.LocalUrl.Host.ShouldBe("account.vintagestory.at");
        asset.Mirrors.ShouldBe([asset.CdnUrl, asset.LocalUrl]);
    }

    [Fact]
    public void Merge_SameVersionInBothDocuments_PrefersTheStableEntry()
    {
        const string stable = """
        { "1.22.6": { "linux": { "filename": "stable.tar.gz", "filesize": "1 MB", "md5": "aa",
          "urls": { "cdn": "https://cdn.example/stable.tar.gz", "local": "https://local.example/stable.tar.gz" }, "latest": 1 } } }
        """;
        const string unstable = """
        { "1.22.6": { "linux": { "filename": "unstable.tar.gz", "filesize": "1 MB", "md5": "bb",
          "urls": { "cdn": "https://cdn.example/unstable.tar.gz", "local": "https://local.example/unstable.tar.gz" }, "latest": 1 } } }
        """;

        var versions = GameCatalogParser.Merge(stable, unstable);

        versions.Single().Assets[GamePlatforms.Linux].FileName.ShouldBe("stable.tar.gz");
    }

    [Fact]
    public void Merge_UnparsableVersionKey_SkipsThatEntryInsteadOfFailing()
    {
        const string stable = """
        {
          "not-a-version": { "linux": { "filename": "a.tar.gz", "filesize": "1 MB", "md5": "aa",
            "urls": { "cdn": "https://cdn.example/a.tar.gz", "local": "https://local.example/a.tar.gz" }, "latest": 0 } },
          "1.20.0": { "linux": { "filename": "b.tar.gz", "filesize": "1 MB", "md5": "bb",
            "urls": { "cdn": "https://cdn.example/b.tar.gz", "local": "https://local.example/b.tar.gz" }, "latest": 1 } }
        }
        """;

        var versions = GameCatalogParser.Merge(stable, GameCatalogSamples.Empty);

        versions.Single().Version.ShouldBe(GameVersion.Parse("1.20.0"));
    }

    [Theory]
    [InlineData("""{ "1.20.0": { "linux": { "filesize": "1 MB", "md5": "aa", "urls": { "cdn": "https://cdn.example/a", "local": "https://local.example/a" } } } }""")]
    [InlineData("""{ "1.20.0": { "linux": { "filename": "a.tar.gz", "filesize": "1 MB", "urls": { "cdn": "https://cdn.example/a", "local": "https://local.example/a" } } } }""")]
    [InlineData("""{ "1.20.0": { "linux": { "filename": "a.tar.gz", "filesize": "1 MB", "md5": "aa" } } }""")]
    [InlineData("""{ "1.20.0": { "linux": { "filename": "a.tar.gz", "filesize": "1 MB", "md5": "aa", "urls": { "cdn": "https://cdn.example/a" } } } }""")]
    public void Merge_PlatformMissingWhatDownloadingNeeds_SkipsTheWholeEntry(string stable)
    {
        var versions = GameCatalogParser.Merge(stable, GameCatalogSamples.Empty);

        versions.ShouldBeEmpty();
    }

    [Fact]
    public void Merge_PlatformWithoutFileSize_FallsBackToAReadableLabel()
    {
        const string stable = """
        { "1.20.0": { "linux": { "filename": "a.tar.gz", "md5": "aa",
          "urls": { "cdn": "https://cdn.example/a", "local": "https://local.example/a" }, "latest": 0 } } }
        """;

        var versions = GameCatalogParser.Merge(stable, GameCatalogSamples.Empty);

        versions.Single().Assets[GamePlatforms.Linux].DisplaySize.ShouldBe("taille inconnue");
        versions.Single().Assets[GamePlatforms.Linux].IsLatest.ShouldBeFalse();
    }

    [Fact]
    public void Merge_NonHttpMirror_IsRejected()
    {
        const string stable = """
        { "1.20.0": { "linux": { "filename": "a.tar.gz", "filesize": "1 MB", "md5": "aa",
          "urls": { "cdn": "file:///etc/passwd", "local": "https://local.example/a" }, "latest": 0 } } }
        """;

        GameCatalogParser.Merge(stable, GameCatalogSamples.Empty).ShouldBeEmpty();
    }

    [Fact]
    public void Merge_EmptyDocuments_YieldsNothing()
    {
        GameCatalogParser.Merge(GameCatalogSamples.Empty, string.Empty).ShouldBeEmpty();
    }

    [Fact]
    public void Merge_MalformedJson_Throws()
    {
        Should.Throw<JsonException>(() => GameCatalogParser.Merge("{ not json", GameCatalogSamples.Empty));
    }

    [Fact]
    public void FindAsset_MacPreference_TakesArm64BeforeX64()
    {
        var entry = GameCatalogParser.Merge(GameCatalogSamples.Stable, GameCatalogSamples.Empty)
            .Single(candidate => candidate.Version == GameVersion.Parse("1.22.6"));

        entry.FindAsset([GamePlatforms.MacArm64, GamePlatforms.MacX64])!.PlatformKey.ShouldBe(GamePlatforms.MacArm64);
    }

    [Fact]
    public void FindAsset_PreferredPlatformAbsent_FallsBackToTheNextOne()
    {
        const string stable = """
        { "1.20.0": { "mac-x64": { "filename": "a.tar.gz", "filesize": "1 MB", "md5": "aa",
          "urls": { "cdn": "https://cdn.example/a", "local": "https://local.example/a" }, "latest": 0 } } }
        """;

        var entry = GameCatalogParser.Merge(stable, GameCatalogSamples.Empty).Single();

        entry.FindAsset([GamePlatforms.MacArm64, GamePlatforms.MacX64])!.PlatformKey.ShouldBe(GamePlatforms.MacX64);
    }

    [Fact]
    public void FindAsset_NoPlatformMatches_ReturnsNull()
    {
        var entry = GameCatalogParser.Merge(GameCatalogSamples.Stable, GameCatalogSamples.Empty)
            .Single(candidate => candidate.Version == GameVersion.Parse("1.21.3"));

        entry.FindAsset([GamePlatforms.MacArm64, GamePlatforms.MacX64]).ShouldBeNull();
    }
}