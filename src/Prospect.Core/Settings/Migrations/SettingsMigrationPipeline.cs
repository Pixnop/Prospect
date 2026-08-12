using System.Text.Json.Nodes;

namespace Prospect.Core.Settings.Migrations;

/// <summary>
/// Enchaîne les <see cref="ISettingsMigration"/> disponibles pour amener un document
/// <c>prospect.json</c> de son schéma d'origine au schéma cible, en incrémentant
/// <c>schemaVersion</c> après chaque étape. Miroir exact
/// d'<see cref="Instances.Migrations.InstanceMetadataMigrationPipeline"/> : injecté par
/// constructeur avec la liste des migrations connues, vide en production tant que le schéma v1
/// reste le premier (voir <c>CompositionRoot</c>).
/// </summary>
public sealed class SettingsMigrationPipeline
{
    private readonly Dictionary<int, ISettingsMigration> _migrationsByFromVersion;

    public SettingsMigrationPipeline(IEnumerable<ISettingsMigration> migrations)
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
    /// <param name="targetSchemaVersion">Schéma visé, typiquement <see cref="ProspectSettings.CurrentSchemaVersion"/>.</param>
    /// <param name="sourcePath">Chemin du fichier d'origine, uniquement pour le message d'erreur.</param>
    /// <exception cref="SettingsSchemaVersionUnsupportedException">
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
                throw new SettingsSchemaVersionUnsupportedException(sourcePath, fromSchemaVersion, targetSchemaVersion);
            }

            current = migration.Migrate(current);
            currentVersion++;
            current["schemaVersion"] = currentVersion;
        }

        return current;
    }
}