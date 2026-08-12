using System.Text.Json.Nodes;

namespace Prospect.Core.Settings.Migrations;

/// <summary>
/// Une étape du pipeline de migration de <c>prospect.json</c> : transforme un document du schéma
/// <see cref="FromSchemaVersion"/> vers celui juste au-dessus. Miroir exact
/// d'<see cref="Instances.Migrations.IInstanceMetadataMigration"/> (docs/architecture.md, pattern
/// « pipeline de migrations » répliqué du domaine Instances) : une classe par migration, testée
/// isolément ; <see cref="SettingsMigrationPipeline"/> les enchaîne et gère lui-même
/// l'incrémentation du champ <c>schemaVersion</c>.
/// </summary>
public interface ISettingsMigration
{
    /// <summary>Version de schéma que cette migration sait consommer en entrée.</summary>
    int FromSchemaVersion { get; }

    /// <summary>
    /// Transforme <paramref name="document"/>, actuellement au schéma
    /// <see cref="FromSchemaVersion"/>, vers la forme attendue au schéma suivant. Ne doit pas
    /// modifier <c>schemaVersion</c> : c'est le pipeline appelant qui s'en charge après l'appel.
    /// </summary>
    JsonObject Migrate(JsonObject document);
}