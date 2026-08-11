using System.Text.Json.Nodes;

using Prospect.Core.Instances;
using Prospect.Core.Instances.Migrations;

using Shouldly;

namespace Prospect.Core.Tests.Instances.Migrations;

/// <summary>
/// Le schéma v1 d'<c>instance.json</c> est le premier schéma réel : il n'existe donc aucune vraie
/// migration à ce jour. Ces tests prouvent le pipeline lui-même (chaînage, incrémentation du
/// schemaVersion, échec propre sur un schéma non couvert) avec des migrations factices, pour que
/// la première vraie migration (v1 → v2, le jour où le schéma bougera) n'ait qu'à implémenter
/// <see cref="IInstanceMetadataMigration"/> et à s'enregistrer, sans toucher au pipeline.
/// </summary>
public class InstanceMetadataMigrationPipelineTests
{
    [Fact]
    public void Constructor_NullMigrations_ThrowsArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() => new InstanceMetadataMigrationPipeline(null!));
    }

    [Fact]
    public void Apply_SourceAlreadyAtTargetVersion_ReturnsDocumentUnchanged()
    {
        var pipeline = new InstanceMetadataMigrationPipeline([]);
        var document = new JsonObject { ["schemaVersion"] = 1, ["name"] = "Homestead" };

        var result = pipeline.Apply(document, fromSchemaVersion: 1, targetSchemaVersion: 1, sourcePath: "instance.json");

        result["name"]!.GetValue<string>().ShouldBe("Homestead");
        result["schemaVersion"]!.GetValue<int>().ShouldBe(1);
    }

    [Fact]
    public void Apply_SingleMigrationRegistered_AppliesItAndBumpsSchemaVersion()
    {
        var migration = new FakeInstanceMetadataMigration(fromSchemaVersion: 0, markerPropertyName: "migratedFromV0");
        var pipeline = new InstanceMetadataMigrationPipeline([migration]);
        var document = new JsonObject { ["schemaVersion"] = 0, ["name"] = "Homestead" };

        var result = pipeline.Apply(document, fromSchemaVersion: 0, targetSchemaVersion: 1, sourcePath: "instance.json");

        result["schemaVersion"]!.GetValue<int>().ShouldBe(1);
        result["migratedFromV0"]!.GetValue<bool>().ShouldBeTrue();
    }

    [Fact]
    public void Apply_TwoChainedMigrations_AppliesBothInOrder()
    {
        var v0ToV1 = new FakeInstanceMetadataMigration(fromSchemaVersion: 0, markerPropertyName: "migratedFromV0");
        var v1ToV2 = new FakeInstanceMetadataMigration(fromSchemaVersion: 1, markerPropertyName: "migratedFromV1");
        var pipeline = new InstanceMetadataMigrationPipeline([v0ToV1, v1ToV2]);
        var document = new JsonObject { ["schemaVersion"] = 0 };

        var result = pipeline.Apply(document, fromSchemaVersion: 0, targetSchemaVersion: 2, sourcePath: "instance.json");

        result["schemaVersion"]!.GetValue<int>().ShouldBe(2);
        result["migratedFromV0"]!.GetValue<bool>().ShouldBeTrue();
        result["migratedFromV1"]!.GetValue<bool>().ShouldBeTrue();
    }

    [Fact]
    public void Apply_NoMigrationRegisteredForSourceVersion_ThrowsSchemaVersionUnsupported()
    {
        var pipeline = new InstanceMetadataMigrationPipeline([]);
        var document = new JsonObject { ["schemaVersion"] = 0 };

        var exception = Should.Throw<InstanceSchemaVersionUnsupportedException>(
            () => pipeline.Apply(document, fromSchemaVersion: 0, targetSchemaVersion: 1, sourcePath: "instance.json"));

        exception.FoundSchemaVersion.ShouldBe(0);
        exception.CurrentSchemaVersion.ShouldBe(1);
        exception.Path.ShouldBe("instance.json");
    }

    [Fact]
    public void Apply_ChainBreaksPartway_ThrowsSchemaVersionUnsupported()
    {
        // Une seule migration enregistrée (0 -> 1) alors que la cible est 2 : la chaîne s'arrête
        // au milieu, faute de migration 1 -> 2.
        var v0ToV1 = new FakeInstanceMetadataMigration(fromSchemaVersion: 0, markerPropertyName: "migratedFromV0");
        var pipeline = new InstanceMetadataMigrationPipeline([v0ToV1]);
        var document = new JsonObject { ["schemaVersion"] = 0 };

        Should.Throw<InstanceSchemaVersionUnsupportedException>(
            () => pipeline.Apply(document, fromSchemaVersion: 0, targetSchemaVersion: 2, sourcePath: "instance.json"));
    }

    [Fact]
    public void Constructor_DuplicateFromSchemaVersion_ThrowsArgumentException()
    {
        var first = new FakeInstanceMetadataMigration(fromSchemaVersion: 0, markerPropertyName: "a");
        var second = new FakeInstanceMetadataMigration(fromSchemaVersion: 0, markerPropertyName: "b");

        Should.Throw<ArgumentException>(() => new InstanceMetadataMigrationPipeline([first, second]));
    }
}