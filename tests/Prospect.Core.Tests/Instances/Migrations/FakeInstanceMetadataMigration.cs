using System.Text.Json.Nodes;

using Prospect.Core.Instances.Migrations;

namespace Prospect.Core.Tests.Instances.Migrations;

/// <summary>
/// Migration factice utilisée uniquement par les tests pour prouver le pipeline : le schéma v1
/// est le premier schéma réel d'<c>instance.json</c>, il n'existe donc aucune vraie migration à
/// ce jour. Applique une transformation arbitraire et vérifiable (ajout d'une propriété) plutôt
/// qu'un no-op, pour que les tests distinguent « la migration a tourné » de « le document n'a pas
/// bougé ».
/// </summary>
internal sealed class FakeInstanceMetadataMigration(int fromSchemaVersion, string markerPropertyName) : IInstanceMetadataMigration
{
    public int FromSchemaVersion { get; } = fromSchemaVersion;

    public JsonObject Migrate(JsonObject document)
    {
        document[markerPropertyName] = true;
        return document;
    }
}