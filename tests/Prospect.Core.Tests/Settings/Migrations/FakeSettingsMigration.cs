using System.Text.Json.Nodes;

using Prospect.Core.Settings.Migrations;

namespace Prospect.Core.Tests.Settings.Migrations;

/// <summary>
/// Migration factice utilisée uniquement par les tests pour prouver le pipeline : le schéma v1 est
/// le premier schéma réel de <c>prospect.json</c>, il n'existe donc aucune vraie migration à ce
/// jour. Applique une transformation arbitraire et vérifiable (ajout d'une propriété) plutôt qu'un
/// no-op, pour que les tests distinguent « la migration a tourné » de « le document n'a pas bougé ».
/// </summary>
internal sealed class FakeSettingsMigration(int fromSchemaVersion, string markerPropertyName) : ISettingsMigration
{
    public int FromSchemaVersion { get; } = fromSchemaVersion;

    public JsonObject Migrate(JsonObject document)
    {
        document[markerPropertyName] = true;
        return document;
    }
}