using System.IO.Abstractions.TestingHelpers;

using Prospect.Core.ModDb;

using Shouldly;

namespace Prospect.Core.Tests.ModDb;

public sealed class ModArchiveReaderTests
{
    private const string ArchivePath = "/data/prospect/instances/homestead/data/Mods/configlib-1.12.0.zip";

    private static readonly byte[] IconBytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private static (ModArchiveReader Reader, MockFileSystem FileSystem) CreateReader(byte[] archive)
    {
        var fileSystem = new MockFileSystem();
        fileSystem.AddFile(ArchivePath, new MockFileData(archive));

        return (new ModArchiveReader(fileSystem), fileSystem);
    }

    [Fact]
    public void Read_RealisticArchive_ReturnsModInfoAndIcon()
    {
        var (reader, _) = CreateReader(ModInfoSamples.BuildArchive(ModInfoSamples.ConfigLib, IconBytes));

        var content = reader.Read(ArchivePath);

        content.Result.Info.ShouldNotBeNull().ModId.ShouldBe("configlib");
        content.Icon.ShouldBe(IconBytes);
    }

    [Fact]
    public void Read_ArchiveWithoutIcon_StillReadsModInfo()
    {
        var (reader, _) = CreateReader(ModInfoSamples.BuildArchive(ModInfoSamples.JsonPatchesLib));

        var content = reader.Read(ArchivePath);

        content.Result.IsIdentified.ShouldBeTrue();
        content.Icon.ShouldBeNull();
    }

    [Fact]
    public void Read_IconDeclaredWithARelativePrefix_IsStillFound()
    {
        const string Json = """{ "modid": "x", "name": "X", "iconPath": "./art/logo.png" }""";
        var (reader, _) = CreateReader(ModInfoSamples.BuildArchive(Json, IconBytes, iconEntryName: "art/logo.png"));

        reader.Read(ArchivePath).Icon.ShouldBe(IconBytes);
    }

    [Fact]
    public void Read_ModInfoNestedUnderAParentFolder_IsStillFound()
    {
        var archive = ModInfoSamples.BuildArchive(ModInfoSamples.ExtraInfo, modInfoEntryName: "ExtraInfo/modinfo.json");
        var (reader, _) = CreateReader(archive);

        reader.Read(ArchivePath).Result.Info.ShouldNotBeNull().ModId.ShouldBe("extrainfo");
    }

    [Fact]
    public void Read_ArchiveWithoutModInfo_ReportsTheMissingFileWithoutThrowing()
    {
        var (reader, _) = CreateReader(ModInfoSamples.BuildArchive(modInfoJson: null));

        reader.Read(ArchivePath).Result.Problem.ShouldBe(ModInfoProblem.MissingModInfo);
    }

    [Fact]
    public void Read_ArchiveWithMalformedModInfo_ReportsTheProblemWithoutThrowing()
    {
        var (reader, _) = CreateReader(ModInfoSamples.BuildArchive(ModInfoSamples.Malformed));

        reader.Read(ArchivePath).Result.Problem.ShouldBe(ModInfoProblem.MalformedJson);
    }

    [Fact]
    public void Read_FileThatIsNotAZip_ReportsAnUnreadableArchive()
    {
        var (reader, _) = CreateReader("ceci n'est pas une archive"u8.ToArray());

        reader.Read(ArchivePath).Result.Problem.ShouldBe(ModInfoProblem.UnreadableArchive);
    }

    [Fact]
    public void Read_MissingFile_ReportsAnUnreadableArchive()
    {
        var reader = new ModArchiveReader(new MockFileSystem());

        reader.Read(ArchivePath).Result.Problem.ShouldBe(ModInfoProblem.UnreadableArchive);
    }

    [Fact]
    public void Read_ModInfoWithAByteOrderMark_IsParsedWithoutAParasiteCharacter()
    {
        var withBom = "﻿" + ModInfoSamples.ConfigLib;
        var (reader, _) = CreateReader(ModInfoSamples.BuildArchive(withBom));

        reader.Read(ArchivePath).Result.Info.ShouldNotBeNull().ModId.ShouldBe("configlib");
    }

    [Fact]
    public void Read_OversizedIcon_IsIgnoredRatherThanLoadedIntoMemory()
    {
        var oversized = new byte[ModArchiveReader.MaxIconBytes + 1];
        var (reader, _) = CreateReader(ModInfoSamples.BuildArchive(ModInfoSamples.ConfigLib, oversized));

        var content = reader.Read(ArchivePath);

        content.Result.IsIdentified.ShouldBeTrue();
        content.Icon.ShouldBeNull();
    }

    [Fact]
    public void Read_EmptyPath_IsRejected()
        => Should.Throw<ArgumentException>(() => new ModArchiveReader(new MockFileSystem()).Read("  "));
}