using System.Text.Json.Nodes;

using Prospect.Core.Instances;
using Prospect.Core.Instances.Migrations;

using Shouldly;

namespace Prospect.Core.Tests.Instances.Migrations;

/// <summary>
/// La première vraie migration du pipeline (voir sa docstring) : prouve isolément que
/// <see cref="InstanceMetadataV1ToV2Migration.Migrate"/> pose le bloc <c>backups</c> avec les
/// défauts documentés, sans toucher au reste du document ni à <c>schemaVersion</c> (propriété du
/// pipeline appelant, voir <see cref="IInstanceMetadataMigration"/>). Le comportement bout-en-bout
/// via <see cref="FileSystemInstanceRepository"/> (chargement v1, persistance v2, rechargement
/// stable, version future toujours en erreur typée) est couvert séparément dans
/// FileSystemInstanceRepositoryTests.
/// </summary>
public sealed class InstanceMetadataV1ToV2MigrationTests
{
    [Fact]
    public void FromSchemaVersion_IsOne()
    {
        new InstanceMetadataV1ToV2Migration().FromSchemaVersion.ShouldBe(1);
    }

    [Fact]
    public void Migrate_NullDocument_ThrowsArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() => new InstanceMetadataV1ToV2Migration().Migrate(null!));
    }

    [Fact]
    public void Migrate_V1Document_AddsBackupsBlockWithSaneDefaults()
    {
        var document = new JsonObject { ["schemaVersion"] = 1, ["name"] = "Homestead" };

        var result = new InstanceMetadataV1ToV2Migration().Migrate(document);

        result["backups"].ShouldNotBeNull();
        result["backups"]!["autoBeforeLaunch"]!.GetValue<bool>().ShouldBeFalse();
        result["backups"]!["keepCount"]!.GetValue<int>().ShouldBe(5);
    }

    [Fact]
    public void Migrate_SaneDefaults_MatchInstanceBackupSettingsDefault()
    {
        // Les littéraux du test précédent doivent rester synchronisés avec le modèle : celui-ci le
        // vérifie par calcul plutôt que par une seconde paire de littéraux qui pourrait diverger en
        // silence si InstanceBackupSettings.Default changeait un jour.
        var document = new JsonObject { ["schemaVersion"] = 1 };

        var result = new InstanceMetadataV1ToV2Migration().Migrate(document);

        result["backups"]!["autoBeforeLaunch"]!.GetValue<bool>().ShouldBe(InstanceBackupSettings.Default.AutoBeforeLaunch);
        result["backups"]!["keepCount"]!.GetValue<int>().ShouldBe(InstanceBackupSettings.Default.KeepCount);
    }

    [Fact]
    public void Migrate_DoesNotModifySchemaVersion()
    {
        // Contrat d'IInstanceMetadataMigration : c'est InstanceMetadataMigrationPipeline qui
        // incrémente schemaVersion après l'appel, jamais la migration elle-même.
        var document = new JsonObject { ["schemaVersion"] = 1 };

        var result = new InstanceMetadataV1ToV2Migration().Migrate(document);

        result["schemaVersion"]!.GetValue<int>().ShouldBe(1);
    }

    [Fact]
    public void Migrate_PreservesExistingFields()
    {
        var document = new JsonObject
        {
            ["schemaVersion"] = 1,
            ["id"] = "0c9c1f57-8b2e-4f2a-9c41-3d8a12f7b6e0",
            ["name"] = "Homestead 1.21",
            ["gameVersion"] = "1.21.3",
        };

        var result = new InstanceMetadataV1ToV2Migration().Migrate(document);

        result["id"]!.GetValue<string>().ShouldBe("0c9c1f57-8b2e-4f2a-9c41-3d8a12f7b6e0");
        result["name"]!.GetValue<string>().ShouldBe("Homestead 1.21");
        result["gameVersion"]!.GetValue<string>().ShouldBe("1.21.3");
    }

    [Fact]
    public void Migrate_DocumentAlreadyHasABackupsBlock_OverwritesIt()
    {
        // Un v1 ne devrait normalement jamais porter de backups (le champ n'existait pas avant ce
        // schéma), mais la migration reste défensive : elle pose toujours les défauts sains plutôt
        // que de faire confiance à un contenu antérieur inattendu.
        var document = new JsonObject
        {
            ["schemaVersion"] = 1,
            ["backups"] = new JsonObject { ["autoBeforeLaunch"] = true, ["keepCount"] = 42 },
        };

        var result = new InstanceMetadataV1ToV2Migration().Migrate(document);

        result["backups"]!["autoBeforeLaunch"]!.GetValue<bool>().ShouldBeFalse();
        result["backups"]!["keepCount"]!.GetValue<int>().ShouldBe(5);
    }
}