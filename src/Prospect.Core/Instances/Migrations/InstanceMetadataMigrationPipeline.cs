using System.Text.Json.Nodes;

namespace Prospect.Core.Instances.Migrations;

/// <summary>
/// Enchaîne les <see cref="IInstanceMetadataMigration"/> disponibles pour amener un document
/// <c>instance.json</c> de son schéma d'origine au schéma cible, en incrémentant
/// <c>schemaVersion</c> après chaque étape. Injecté par constructeur avec la liste des migrations
/// connues plutôt que de les découvrir via un registre statique : sans migration réelle à ce jour
/// (le schéma v1 est le premier), la liste de production est vide, et le jour où une migration
/// v1 → v2 existera, l'ajouter ici suffira.
/// </summary>
public sealed class InstanceMetadataMigrationPipeline
{
    private readonly Dictionary<int, IInstanceMetadataMigration> _migrationsByFromVersion;

    public InstanceMetadataMigrationPipeline(IEnumerable<IInstanceMetadataMigration> migrations)
    {
        ArgumentNullException.ThrowIfNull(migrations);

        _migrationsByFromVersion = migrations.ToDictionary(migration => migration.FromSchemaVersion);
    }

    /// <summary>
    /// Applique en chaîne les migrations nécessaires pour amener <paramref name="document"/> de
    /// <paramref name="fromSchemaVersion"/> à <paramref name="targetSchemaVersion"/>.
    /// </summary>
    /// <param name="document">Document au schéma <paramref name="fromSchemaVersion"/>.</param>
    /// <param name="fromSchemaVersion">Schéma d'origine, lu dans le document avant l'appel.</param>
    /// <param name="targetSchemaVersion">Schéma visé, typiquement <see cref="InstanceMetadata.CurrentSchemaVersion"/>.</param>
    /// <param name="sourcePath">Chemin du fichier d'origine, uniquement pour le message d'erreur.</param>
    /// <exception cref="InstanceSchemaVersionUnsupportedException">
    /// Aucune migration enregistrée ne permet de poursuivre la chaîne jusqu'à
    /// <paramref name="targetSchemaVersion"/>.
    /// </exception>
    public JsonObject Apply(JsonObject document, int fromSchemaVersion, int targetSchemaVersion, string sourcePath)
    {
        ArgumentNullException.ThrowIfNull(document);

        var current = document;
        var currentVersion = fromSchemaVersion;

        while (currentVersion < targetSchemaVersion)
        {
            if (!_migrationsByFromVersion.TryGetValue(currentVersion, out var migration))
            {
                throw new InstanceSchemaVersionUnsupportedException(sourcePath, fromSchemaVersion, targetSchemaVersion);
            }

            current = migration.Migrate(current);
            currentVersion++;
            current["schemaVersion"] = currentVersion;
        }

        return current;
    }
}