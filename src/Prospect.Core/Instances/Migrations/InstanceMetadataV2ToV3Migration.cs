using System.Text.Json.Nodes;

namespace Prospect.Core.Instances.Migrations;

/// <summary>
/// Migration v2 → v3 : sort <c>MESA_GLTHREAD</c> du dictionnaire <c>launch.env</c> et en fait le
/// champ typé <c>launch.mesaGlThread</c>.
/// </summary>
/// <remarks>
/// <para>
/// Le champ n'a pas seulement changé de place, il a changé de PORTÉE : la variable est désormais
/// posée par la stratégie de lancement Linux et par elle seule (voir
/// <see cref="InstanceLaunchSettings.MesaGlThread"/>), au lieu de suivre l'instance sur tous les
/// systèmes. Laisser l'ancienne entrée dans <c>env</c> la ferait poser deux fois, une fois sous son
/// nom d'origine que Mesa ne lit pas, donc elle est RETIRÉE.
/// </para>
/// <para>
/// La casse de la clé n'est pas supposée : un <c>instance.json</c> importé de VS Launcher ou édité
/// à la main peut écrire <c>MESA_GLTHREAD</c>, <c>mesa_glthread</c> ou n'importe quelle variante.
/// Toutes valent, et toute valeur autre que <c>false</c> vaut activé, parce qu'une entrée présente
/// dans <c>env</c> signifiait déjà « activé » quelle que soit sa valeur.
/// </para>
/// </remarks>
public sealed class InstanceMetadataV2ToV3Migration : IInstanceMetadataMigration
{
    /// <summary>Clé historique, telle que VS Launcher et les versions précédentes de Prospect l'écrivaient.</summary>
    public const string LegacyEnvKey = "MESA_GLTHREAD";

    /// <inheritdoc />
    public int FromSchemaVersion => 2;

    /// <inheritdoc />
    public JsonObject Migrate(JsonObject document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (document["launch"] is not JsonObject launch)
        {
            return document;
        }

        var enabled = false;
        if (launch["env"] is JsonObject env)
        {
            foreach (var key in env.Select(entry => entry.Key)
                .Where(key => string.Equals(key, LegacyEnvKey, StringComparison.OrdinalIgnoreCase))
                .ToArray())
            {
                enabled |= !string.Equals(env[key]?.ToString(), "false", StringComparison.OrdinalIgnoreCase);
                env.Remove(key);
            }
        }

        launch["mesaGlThread"] = enabled;

        return document;
    }
}