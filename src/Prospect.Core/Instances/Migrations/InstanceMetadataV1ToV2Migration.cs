using System.Text.Json.Nodes;

namespace Prospect.Core.Instances.Migrations;

/// <summary>
/// Première VRAIE migration du pipeline <c>instance.json</c> (v1 → v2, chantier Sauvegardes) :
/// jusqu'ici <see cref="InstanceMetadataMigrationPipeline"/> n'était exercé qu'avec des migrations
/// factices (<c>Prospect.Core.Tests.Instances.Migrations.FakeInstanceMetadataMigration</c>), le
/// schéma v1 étant le premier schéma réel. Celle-ci ajoute le bloc <c>backups</c>
/// (<see cref="InstanceBackupSettings"/>) avec ses défauts sains explicites.
/// </summary>
/// <remarks>
/// Pose <c>autoBeforeLaunch</c>/<c>keepCount</c> explicitement dans le document plutôt que de
/// laisser <see cref="InstanceMetadata.Normalized"/> combler seule le champ absent après
/// désérialisation. <c>Normalized()</c> reste un filet nécessaire (un <c>instance.json</c> édité à
/// la main peut toujours omettre <c>backups</c> même au schéma v2 courant), mais le sens d'une
/// migration de schéma est justement de rendre la transformation explicite et testable
/// isolément : un v1 chargé doit ressortir avec un bloc <c>backups</c> visible dans le document
/// migré, pas dépendre d'un mécanisme de repli qui s'exécute après coup.
/// </remarks>
public sealed class InstanceMetadataV1ToV2Migration : IInstanceMetadataMigration
{
    /// <inheritdoc />
    public int FromSchemaVersion => 1;

    /// <inheritdoc />
    public JsonObject Migrate(JsonObject document)
    {
        ArgumentNullException.ThrowIfNull(document);

        document["backups"] = new JsonObject
        {
            ["autoBeforeLaunch"] = InstanceBackupSettings.Default.AutoBeforeLaunch,
            ["keepCount"] = InstanceBackupSettings.Default.KeepCount,
        };

        return document;
    }
}