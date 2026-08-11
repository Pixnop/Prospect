namespace Prospect.Core.Modpacks;

/// <summary>
/// Emplacements fixes à l'intérieur d'une archive de modpack (docs/architecture.md, « Manifest de
/// modpack ») : le nom de fichier du manifest, qu'il voyage seul ou zippé, et le préfixe sous
/// lequel voyage l'éventuel dossier <c>ModConfig/</c> de l'instance. Partagé entre
/// <see cref="ModpackExportService"/> et <see cref="ModpackImportService"/> pour que les deux
/// s'accordent sur la même forme d'archive.
/// </summary>
public static class ModpackArchiveLayout
{
    /// <summary>Nom du fichier manifest, seul ou à la racine d'une archive.</summary>
    public const string ManifestFileName = "prospect-pack.json";

    /// <summary>Nom du dossier de réglages de mods dans <c>data/</c> d'une instance.</summary>
    public const string ModConfigFolderName = "ModConfig";

    /// <summary>Préfixe (avec son <c>/</c> final) sous lequel le contenu de <c>data/ModConfig/</c> voyage dans l'archive.</summary>
    public const string ModConfigEntryPrefix = ModConfigFolderName + "/";
}