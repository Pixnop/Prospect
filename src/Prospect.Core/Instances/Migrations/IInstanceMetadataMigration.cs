using System.Text.Json.Nodes;

namespace Prospect.Core.Instances.Migrations;

/// <summary>
/// Une étape du pipeline de migration d'<c>instance.json</c> : transforme un document du schéma
/// <see cref="FromSchemaVersion"/> vers celui juste au-dessus. Une classe par migration, testée
/// isolément ; <see cref="InstanceMetadataMigrationPipeline"/> les enchaîne et gère lui-même
/// l'incrémentation du champ <c>schemaVersion</c>, pour qu'une migration ne s'occupe que de la
/// forme du document.
/// </summary>
public interface IInstanceMetadataMigration
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