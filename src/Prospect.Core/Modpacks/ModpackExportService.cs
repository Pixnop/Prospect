using System.IO.Abstractions;
using System.IO.Compression;

using Prospect.Core.Instances;
using Prospect.Core.ModDb;

namespace Prospect.Core.Modpacks;

/// <summary>
/// Façade du domaine Modpacks pour l'export (docs/architecture.md, « Services applicatifs par
/// domaine ») : lit une instance et ses mods installés par <see cref="IInstalledModRepository"/> —
/// exactement les mêmes ports que le reste du Core, aucun accès disque propre à ce service — et
/// produit soit le manifest seul, soit une archive.
/// </summary>
public sealed class ModpackExportService
{
    private readonly IInstanceRepository _instances;
    private readonly IInstalledModRepository _mods;
    private readonly IFileSystem _fileSystem;

    /// <summary>Construit le service.</summary>
    /// <param name="instances">Instances, pour charger nom et version de jeu.</param>
    /// <param name="mods">Mods installés de l'instance à exporter.</param>
    /// <param name="fileSystem">Système de fichiers abstrait.</param>
    public ModpackExportService(IInstanceRepository instances, IInstalledModRepository mods, IFileSystem fileSystem)
    {
        ArgumentNullException.ThrowIfNull(instances);
        ArgumentNullException.ThrowIfNull(mods);
        ArgumentNullException.ThrowIfNull(fileSystem);

        _instances = instances;
        _mods = mods;
        _fileSystem = fileSystem;
    }

    /// <summary>
    /// Exporte une instance vers <paramref name="destinationPath"/>. Les mods « non identifiés »
    /// (sans <c>modinfo.json</c> lisible, ou sans version exploitable) ne peuvent pas voyager : ils
    /// sont listés dans le résultat plutôt que d'être silencieusement omis.
    /// </summary>
    /// <param name="slug">Instance à exporter.</param>
    /// <param name="destinationPath">Fichier à écrire (créé, ou remplacé s'il existe déjà).</param>
    /// <param name="options">Forme (manifest seul ou archive) et inclusion de <c>ModConfig/</c>.</param>
    /// <param name="cancellationToken">
    /// Annulation. Un fichier de destination partiellement écrit est supprimé avant que
    /// l'annulation ne remonte : contrairement à l'import, l'export ne crée rien d'autre à nettoyer.
    /// </param>
    /// <exception cref="InstanceNotFoundException">Aucune instance pour <paramref name="slug"/>.</exception>
    public async Task<ModpackExportResult> ExportAsync(
        string slug,
        string destinationPath,
        ModpackExportOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        ArgumentNullException.ThrowIfNull(options);

        var instance = await _instances.LoadAsync(slug, cancellationToken).ConfigureAwait(false);
        var installed = await _mods.ScanAsync(slug, cancellationToken).ConfigureAwait(false);
        var (entries, skipped) = await BuildEntriesAsync(installed, cancellationToken).ConfigureAwait(false);

        var manifest = new ModpackManifest
        {
            SchemaVersion = ModpackManifest.CurrentSchemaVersion,
            Name = instance.Metadata.Name,
            GameVersion = instance.Metadata.GameVersion,
            Mods = entries,
        };

        EnsureDestinationDirectoryExists(destinationPath);

        try
        {
            if (options.Format == ModpackExportFormat.Archive)
            {
                await WriteArchiveAsync(slug, destinationPath, manifest, options.IncludeModConfig, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await WriteManifestOnlyAsync(destinationPath, manifest, cancellationToken).ConfigureAwait(false);
            }
        }
        catch
        {
            DeleteIfExists(destinationPath);
            throw;
        }

        return new ModpackExportResult(destinationPath, entries.Count, skipped);
    }

    private void EnsureDestinationDirectoryExists(string destinationPath)
    {
        var directory = _fileSystem.Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(directory))
        {
            _fileSystem.Directory.CreateDirectory(directory);
        }
    }

    private void DeleteIfExists(string path)
    {
        try
        {
            if (_fileSystem.File.Exists(path))
            {
                _fileSystem.File.Delete(path);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Rien de plus à tenter : l'exception d'origine (annulation ou échec d'écriture) reste
            // celle qui remonte à l'appelant.
        }
    }

    private async Task WriteManifestOnlyAsync(string destinationPath, ModpackManifest manifest, CancellationToken cancellationToken)
    {
        var stream = _fileSystem.File.Create(destinationPath);
        await using (stream.ConfigureAwait(false))
        {
            await ModpackManifestSerializer.WriteAsync(stream, manifest, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task WriteArchiveAsync(
        string slug,
        string destinationPath,
        ModpackManifest manifest,
        bool includeModConfig,
        CancellationToken cancellationToken)
    {
        var stream = _fileSystem.File.Create(destinationPath);
        await using (stream.ConfigureAwait(false))
        {
            using var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true);

            var manifestEntry = archive.CreateEntry(ModpackArchiveLayout.ManifestFileName);
            var manifestStream = manifestEntry.Open();
            await using (manifestStream.ConfigureAwait(false))
            {
                await ModpackManifestSerializer.WriteAsync(manifestStream, manifest, cancellationToken).ConfigureAwait(false);
            }

            if (includeModConfig)
            {
                await WriteModConfigEntriesAsync(archive, slug, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task WriteModConfigEntriesAsync(ZipArchive archive, string slug, CancellationToken cancellationToken)
    {
        var modConfigDirectory = _fileSystem.Path.Combine(_instances.GetDataDirectory(slug), ModpackArchiveLayout.ModConfigFolderName);
        if (!_fileSystem.Directory.Exists(modConfigDirectory))
        {
            return;
        }

        foreach (var filePath in _fileSystem.Directory.EnumerateFiles(modConfigDirectory, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var relative = _fileSystem.Path.GetRelativePath(modConfigDirectory, filePath).Replace('\\', '/');
            var entry = archive.CreateEntry(ModpackArchiveLayout.ModConfigEntryPrefix + relative);

            var source = _fileSystem.File.OpenRead(filePath);
            await using (source.ConfigureAwait(false))
            {
                var target = entry.Open();
                await using (target.ConfigureAwait(false))
                {
                    await source.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
                }
            }
        }
    }

    // sha256 calculé sur CHAQUE zip présent qui rejoint le manifest (docs/architecture.md) : c'est
    // le seul garde-fou d'intégrité disponible côté import, faute de somme de contrôle exposée par
    // le ModDB.
    private async Task<(IReadOnlyList<ModpackManifestMod> Entries, IReadOnlyList<ModpackExportSkippedMod> Skipped)> BuildEntriesAsync(
        IReadOnlyList<InstalledMod> installed,
        CancellationToken cancellationToken)
    {
        var entries = new List<ModpackManifestMod>();
        var skipped = new List<ModpackExportSkippedMod>();

        foreach (var mod in installed)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!mod.IsIdentified)
            {
                skipped.Add(new ModpackExportSkippedMod(mod.FileName, ModpackExportSkipReason.UnreadableModInfo));
                continue;
            }

            if (mod.Version is not { } version)
            {
                skipped.Add(new ModpackExportSkippedMod(mod.FileName, ModpackExportSkipReason.MissingVersion));
                continue;
            }

            var sha256 = await Sha256Checksum.ComputeAsync(_fileSystem, mod.FilePath, cancellationToken).ConfigureAwait(false);

            entries.Add(new ModpackManifestMod
            {
                ModId = mod.Identity,
                Version = version,
                FileId = mod.Provenance?.FileId,
                Sha256 = sha256,
                Enabled = mod.IsEnabled ? null : false,
            });
        }

        return (entries.OrderBy(entry => entry.ModId, StringComparer.OrdinalIgnoreCase).ToArray(), skipped);
    }
}