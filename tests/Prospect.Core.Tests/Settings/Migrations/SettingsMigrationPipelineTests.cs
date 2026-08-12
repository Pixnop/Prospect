using System.Text.Json.Nodes;

using Prospect.Core.Settings;
using Prospect.Core.Settings.Migrations;

using Shouldly;

namespace Prospect.Core.Tests.Settings.Migrations;

/// <summary>
/// Miroir exact d'<c>InstanceMetadataMigrationPipelineTests</c> pour le pipeline de Settings : le
/// schéma v1 de <c>prospect.json</c> est le premier, donc aucune vraie migration à ce jour. Ces
/// tests prouvent le pipeline lui-même (chaînage, incrémentation du schemaVersion, échec propre sur
/// un schéma non couvert) avec des migrations factices.
/// </summary>
public class SettingsMigrationPipelineTests
{
    [Fact]
    public void Constructor_NullMigrations_ThrowsArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() => new SettingsMigrationPipeline(null!));
    }

    [Fact]
    public void Apply_SourceAlreadyAtTargetVersion_ReturnsDocumentUnchanged()
    {
        var pipeline = new SettingsMigrationPipeline([]);
        var document = new JsonObject { ["schemaVersion"] = 1, ["theme"] = "Dark" };

        var result = pipeline.Apply(document, fromSchemaVersion: 1, targetSchemaVersion: 1, sourcePath: "prospect.json");

        result["theme"]!.GetValue<string>().ShouldBe("Dark");
        result["schemaVersion"]!.GetValue<int>().ShouldBe(1);
    }

    [Fact]
    public void Apply_SingleMigrationRegistered_AppliesItAndBumpsSchemaVersion()
    {
        var migration = new FakeSettingsMigration(fromSchemaVersion: 0, markerPropertyName: "migratedFromV0");
        var pipeline = new SettingsMigrationPipeline([migration]);
        var document = new JsonObject { ["schemaVersion"] = 0 };

        var result = pipeline.Apply(document, fromSchemaVersion: 0, targetSchemaVersion: 1, sourcePath: "prospect.json");

        result["schemaVersion"]!.GetValue<int>().ShouldBe(1);
        result["migratedFromV0"]!.GetValue<bool>().ShouldBeTrue();
    }

    [Fact]
    public void Apply_TwoChainedMigrations_AppliesBothInOrder()
    {
        var v0ToV1 = new FakeSettingsMigration(fromSchemaVersion: 0, markerPropertyName: "migratedFromV0");
        var v1ToV2 = new FakeSettingsMigration(fromSchemaVersion: 1, markerPropertyName: "migratedFromV1");
        var pipeline = new SettingsMigrationPipeline([v0ToV1, v1ToV2]);
        var document = new JsonObject { ["schemaVersion"] = 0 };

        var result = pipeline.Apply(document, fromSchemaVersion: 0, targetSchemaVersion: 2, sourcePath: "prospect.json");

        result["schemaVersion"]!.GetValue<int>().ShouldBe(2);
        result["migratedFromV0"]!.GetValue<bool>().ShouldBeTrue();
        result["migratedFromV1"]!.GetValue<bool>().ShouldBeTrue();
    }

    [Fact]
    public void Apply_NoMigrationRegisteredForSourceVersion_ThrowsSchemaVersionUnsupported()
    {
        var pipeline = new SettingsMigrationPipeline([]);
        var document = new JsonObject { ["schemaVersion"] = 0 };

        var exception = Should.Throw<SettingsSchemaVersionUnsupportedException>(
            () => pipeline.Apply(document, fromSchemaVersion: 0, targetSchemaVersion: 1, sourcePath: "prospect.json"));

        exception.FoundSchemaVersion.ShouldBe(0);
        exception.CurrentSchemaVersion.ShouldBe(1);
        exception.Path.ShouldBe("prospect.json");
    }

    [Fact]
    public void Apply_ChainBreaksPartway_ThrowsSchemaVersionUnsupported()
    {
        // Une seule migration enregistrée (0 -> 1) alors que la cible est 2 : la chaîne s'arrête
        // au milieu, faute de migration 1 -> 2.
        var v0ToV1 = new FakeSettingsMigration(fromSchemaVersion: 0, markerPropertyName: "migratedFromV0");
        var pipeline = new SettingsMigrationPipeline([v0ToV1]);
        var document = new JsonObject { ["schemaVersion"] = 0 };

        Should.Throw<SettingsSchemaVersionUnsupportedException>(
            () => pipeline.Apply(document, fromSchemaVersion: 0, targetSchemaVersion: 2, sourcePath: "prospect.json"));
    }

    [Fact]
    public void Constructor_DuplicateFromSchemaVersion_ThrowsArgumentException()
    {
        var first = new FakeSettingsMigration(fromSchemaVersion: 0, markerPropertyName: "a");
        var second = new FakeSettingsMigration(fromSchemaVersion: 0, markerPropertyName: "b");

        Should.Throw<ArgumentException>(() => new SettingsMigrationPipeline([first, second]));
    }
}