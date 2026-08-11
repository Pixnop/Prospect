using System.IO.Abstractions.TestingHelpers;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

using Prospect.Core.Storage;

using Shouldly;

namespace Prospect.Core.Tests.Storage;

public class JsonFileStoreTests
{
    private static readonly JsonTypeInfo<SampleSettings> TypeInfo = SampleSettingsJsonContext.Default.SampleSettings;

    [Fact]
    public void Constructor_NullFileSystem_ThrowsArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() => new JsonFileStore(null!));
    }

    [Fact]
    public async Task WriteAsync_ThenReadAsync_RoundTripsValue()
    {
        var fileSystem = new MockFileSystem();
        var store = new JsonFileStore(fileSystem);
        var path = fileSystem.Path.Combine("data", "prospect", "prospect.json");
        var value = new SampleSettings("Homestead", 3);

        await store.WriteAsync(path, value, TypeInfo, CancellationToken.None);
        var readBack = await store.ReadAsync(path, TypeInfo, CancellationToken.None);

        readBack.ShouldBe(value);
    }

    [Fact]
    public async Task ReadAsync_FileDoesNotExist_ReturnsNull()
    {
        var fileSystem = new MockFileSystem();
        var store = new JsonFileStore(fileSystem);

        var result = await store.ReadAsync("does/not/exist.json", TypeInfo, CancellationToken.None);

        result.ShouldBeNull();
    }

    [Fact]
    public async Task ReadAsync_CorruptedJson_ThrowsCorruptedFileExceptionWithPath()
    {
        var fileSystem = new MockFileSystem();
        var path = fileSystem.Path.Combine("data", "prospect.json");
        fileSystem.AddFile(path, new MockFileData("{ ceci n'est pas du JSON"));
        var store = new JsonFileStore(fileSystem);

        var exception = await Should.ThrowAsync<CorruptedFileException>(() => store.ReadAsync(path, TypeInfo, CancellationToken.None));

        exception.Path.ShouldBe(path);
    }

    [Fact]
    public async Task WriteAsync_ParentDirectoryMissing_CreatesIt()
    {
        var fileSystem = new MockFileSystem();
        var store = new JsonFileStore(fileSystem);
        var path = fileSystem.Path.Combine("data", "prospect", "instances", "prospect.json");

        await store.WriteAsync(path, new SampleSettings("Homestead", 1), TypeInfo, CancellationToken.None);

        fileSystem.Directory.Exists(fileSystem.Path.Combine("data", "prospect", "instances")).ShouldBeTrue();
    }

    [Fact]
    public async Task WriteAsync_Success_LeavesNoTempFileResidue()
    {
        var fileSystem = new MockFileSystem();
        var store = new JsonFileStore(fileSystem);
        var path = fileSystem.Path.Combine("data", "prospect.json");

        await store.WriteAsync(path, new SampleSettings("Homestead", 1), TypeInfo, CancellationToken.None);

        fileSystem.File.Exists(path + ".tmp").ShouldBeFalse();
        fileSystem.File.Exists(path).ShouldBeTrue();
    }

    [Fact]
    public async Task WriteAsync_WritesIndentedUtf8WithoutBom()
    {
        var fileSystem = new MockFileSystem();
        var store = new JsonFileStore(fileSystem);
        var path = fileSystem.Path.Combine("data", "prospect.json");

        await store.WriteAsync(path, new SampleSettings("Homestead", 1), TypeInfo, CancellationToken.None);

        var bytes = fileSystem.File.ReadAllBytes(path);
        bytes[0].ShouldNotBe((byte)0xEF, "le fichier ne doit pas commencer par un BOM UTF-8");

        // Un JSON compact tiendrait sur une seule ligne : la présence d'un saut de ligne prouve
        // que l'indentation est bien appliquée, indépendamment de la configuration du contexte
        // source-gen appelant (SampleSettingsJsonContext ne force pas WriteIndented lui-même).
        var text = fileSystem.File.ReadAllText(path);
        text.ShouldContain("\n");
    }

    [Fact]
    public async Task WriteAsync_SerializationFails_LeavesOriginalContentIntact()
    {
        var fileSystem = new MockFileSystem();
        var store = new JsonFileStore(fileSystem);
        var path = fileSystem.Path.Combine("data", "prospect.json");
        var original = new SampleSettings("Homestead", 1);
        await store.WriteAsync(path, original, TypeInfo, CancellationToken.None);
        var originalContent = fileSystem.File.ReadAllText(path);

        var broken = new SampleSettings("Homestead", 1, double.NaN);
        await Should.ThrowAsync<Exception>(() => store.WriteAsync(path, broken, TypeInfo, CancellationToken.None));

        fileSystem.File.ReadAllText(path).ShouldBe(originalContent);
        fileSystem.File.Exists(path + ".tmp").ShouldBeFalse();
    }
}

internal sealed record SampleSettings(string Name, int Count, double Ratio = 1.0);

[JsonSerializable(typeof(SampleSettings))]
internal sealed partial class SampleSettingsJsonContext : JsonSerializerContext;