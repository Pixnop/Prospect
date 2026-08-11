using System.Text.Json;

namespace Prospect.Core.Modpacks;

/// <summary>
/// Lit et écrit un <see cref="ModpackManifest"/> depuis/vers un flux, qu'il vienne d'un fichier
/// <c>.json</c> autonome ou d'une entrée de zip : les deux formes d'export partagent exactement ce
/// même contenu (docs/architecture.md, « Manifest de modpack »), seul le contenant change. Isolé de
/// <see cref="ModpackExportService"/>/<see cref="ModpackImportService"/> pour rester testable sans
/// système de fichiers ni réseau : la validation du schéma est un pur calcul sur le JSON lu.
/// </summary>
public static class ModpackManifestSerializer
{
    /// <summary>Sérialise <paramref name="manifest"/> en JSON indenté, lisible par un humain.</summary>
    public static async Task WriteAsync(Stream stream, ModpackManifest manifest, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(manifest);

        await JsonSerializer.SerializeAsync(stream, manifest, ModpackJsonContext.Default.ModpackManifest, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Lit et valide un manifest. Ne renvoie jamais un manifest sémantiquement incomplet : toute
    /// anomalie (JSON invalide, schéma non pris en charge, champ obligatoire vide) lève une
    /// <see cref="ModpackManifestInvalidException"/> avec un message directement montrable.
    /// </summary>
    public static async Task<ModpackManifest> ReadAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        ModpackManifest? manifest;
        try
        {
            manifest = await JsonSerializer.DeserializeAsync(stream, ModpackJsonContext.Default.ModpackManifest, cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException exception)
        {
            throw new ModpackManifestInvalidException(
                $"Le manifest n'a pas pu être lu : {exception.Message}",
                exception);
        }

        if (manifest is null)
        {
            throw new ModpackManifestInvalidException("Le manifest est vide.");
        }

        // Le type porte des membres `required` : le désérialiseur construit alors l'objet sans
        // passer par son constructeur implicite, ce qui court-circuite l'initialiseur `= []` de
        // Mods quand la clé JSON est absente. Sans ce filet, un manifest sans tableau `mods` (pack
        // qui ne fixe qu'une version de jeu) laisserait Mods à null plutôt qu'à une liste vide.
        if (manifest.Mods is null)
        {
            manifest = manifest with { Mods = [] };
        }

        Validate(manifest);

        return manifest;
    }

    private static void Validate(ModpackManifest manifest)
    {
        if (manifest.SchemaVersion <= 0 || manifest.SchemaVersion > ModpackManifest.CurrentSchemaVersion)
        {
            throw new ModpackManifestInvalidException(
                $"Ce manifest est au schéma v{manifest.SchemaVersion}, cette version de Prospect ne sait lire que le schéma v{ModpackManifest.CurrentSchemaVersion}.");
        }

        if (string.IsNullOrWhiteSpace(manifest.Name))
        {
            throw new ModpackManifestInvalidException("Le manifest n'a pas de nom.");
        }

        for (var index = 0; index < manifest.Mods.Count; index++)
        {
            if (string.IsNullOrWhiteSpace(manifest.Mods[index].ModId))
            {
                throw new ModpackManifestInvalidException($"L'entrée n°{index + 1} de la liste des mods n'a pas d'identifiant.");
            }
        }
    }
}