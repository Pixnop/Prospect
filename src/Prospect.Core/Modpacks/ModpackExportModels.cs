namespace Prospect.Core.Modpacks;

/// <summary>Forme produite par <see cref="ModpackExportService"/>.</summary>
public enum ModpackExportFormat
{
    /// <summary>Le manifest seul, en <c>.json</c>.</summary>
    ManifestOnly,

    /// <summary>Une archive <c>.zip</c> contenant le manifest et, en option, <c>ModConfig/</c>.</summary>
    Archive,
}

/// <summary>Réglages d'un export.</summary>
/// <param name="Format">Manifest seul, ou archive.</param>
/// <param name="IncludeModConfig">
/// Vrai pour embarquer le dossier <c>ModConfig/</c> de l'instance dans l'archive. Sans effet en
/// <see cref="ModpackExportFormat.ManifestOnly"/> : un fichier <c>.json</c> ne peut porter que le
/// manifest.
/// </param>
public sealed record ModpackExportOptions(ModpackExportFormat Format, bool IncludeModConfig = false);

/// <summary>Pourquoi un mod installé n'a pas pu voyager dans le manifest.</summary>
public enum ModpackExportSkipReason
{
    /// <summary>Archive sans <c>modinfo.json</c> lisible : aucun <c>modId</c> à écrire.</summary>
    UnreadableModInfo,

    /// <summary>Archive identifiée, mais sans version lisible : le champ obligatoire manquerait.</summary>
    MissingVersion,
}

/// <summary>Un mod installé laissé de côté par l'export, jamais silencieusement.</summary>
/// <param name="FileName">Nom de fichier dans <c>data/Mods/</c>, pour que l'utilisateur le retrouve.</param>
/// <param name="Reason">Raison de l'exclusion.</param>
public sealed record ModpackExportSkippedMod(string FileName, ModpackExportSkipReason Reason);

/// <summary>Résultat d'un export.</summary>
/// <param name="DestinationPath">Fichier écrit (manifest <c>.json</c> ou archive <c>.zip</c>).</param>
/// <param name="ModsExported">Nombre de mods présents dans le manifest.</param>
/// <param name="SkippedMods">Mods installés qui n'ont pas pu voyager, avec leur raison.</param>
public sealed record ModpackExportResult(
    string DestinationPath,
    int ModsExported,
    IReadOnlyList<ModpackExportSkippedMod> SkippedMods)
{
    /// <summary>Vrai si au moins un mod installé a dû être laissé de côté.</summary>
    public bool HasSkippedMods => SkippedMods.Count > 0;
}