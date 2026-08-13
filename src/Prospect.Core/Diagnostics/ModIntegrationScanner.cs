using System.IO.Abstractions;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

using Prospect.Core.ModDb;

namespace Prospect.Core.Diagnostics;

/// <summary>
/// Ce que l'archive d'un mod annonce d'une intégration : le domaine visé, et si le mod a pris la
/// peine de conditionner cette visée à la présence de l'autre mod.
/// </summary>
/// <param name="TargetDomain">Domaine étranger visé, c'est-à-dire le <c>modid</c> de l'autre mod.</param>
/// <param name="Conditional">
/// Vrai quand le patch porte un <c>dependsOn</c> : l'auteur a prévu l'absence de l'autre mod, le
/// jeu ignorera silencieusement ce patch. Faux quand rien ne le conditionne, ce qui produira une
/// erreur au chargement si l'autre mod n'est pas là (docs/architecture.md, niveau 3 : « une cible
/// étrangère sans condition est une dépendance probablement oubliée »).
/// </param>
/// <param name="Evidence">Entrée d'archive qui porte le signal, pour pouvoir le montrer.</param>
public sealed record ModIntegrationSignal(string TargetDomain, bool Conditional, string Evidence);

/// <summary>
/// Analyse STATIQUE d'une archive de mod, en complément du journal : quels autres mods ce zip
/// vise-t-il ? Deux signaux, tous deux lisibles sans lancer le jeu. Un patch JSON dont le fichier
/// cible appartient au domaine d'un autre mod, ou qui se déclare <c>dependsOn</c> d'un autre mod,
/// et des assets rangés directement sous le domaine d'un autre mod.
/// </summary>
/// <remarks>
/// Même discipline que <see cref="ModArchiveReader"/> : rien n'est extrait sur le disque, seules
/// des entrées connues sont lues en flux, et une archive illisible rend une liste vide au lieu de
/// lever : un signal informatif ne doit jamais faire échouer ce qui l'a demandé. Le travail est
/// borné par <see cref="MaxPatchFiles"/> et <see cref="MaxPatchFileBytes"/> : un mod de contenu
/// peut porter des centaines de patches, et cette analyse tourne à l'ouverture d'un onglet.
/// </remarks>
public sealed class ModIntegrationScanner
{
    /// <summary>Fichiers de patch ouverts au maximum par archive.</summary>
    public const int MaxPatchFiles = 60;

    /// <summary>Taille au-delà de laquelle un fichier de patch n'est pas lu.</summary>
    public const int MaxPatchFileBytes = 512 * 1024;

    private const string AssetsPrefix = "assets/";
    private const string PatchesSegment = "/patches/";

    private static readonly JsonDocumentOptions TolerantOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly IFileSystem _fileSystem;

    /// <summary>Construit le scanner.</summary>
    /// <param name="fileSystem">Système de fichiers abstrait.</param>
    public ModIntegrationScanner(IFileSystem fileSystem)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);

        _fileSystem = fileSystem;
    }

    /// <summary>
    /// Relève les domaines étrangers visés par l'archive <paramref name="archivePath"/>.
    /// </summary>
    /// <param name="archivePath">Chemin du zip.</param>
    /// <param name="ownDomain">Domaine du mod lui-même (son <c>modid</c>), pour ne pas se compter.</param>
    /// <returns>
    /// Un signal par domaine étranger distinct, dédoublonné. Toujours une liste, jamais une
    /// exception : un fichier absent, tronqué ou qui n'est pas un zip rend une liste vide.
    /// </returns>
    public IReadOnlyList<ModIntegrationSignal> Scan(string archivePath, string ownDomain)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(ownDomain);

        if (!_fileSystem.File.Exists(archivePath))
        {
            return [];
        }

        try
        {
            using var stream = _fileSystem.File.OpenRead(archivePath);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

            var signals = new Dictionary<string, ModIntegrationSignal>(StringComparer.OrdinalIgnoreCase);
            var opened = 0;

            foreach (var entry in archive.Entries)
            {
                var name = entry.FullName.Replace('\\', '/');
                if (!name.StartsWith(AssetsPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var domain = DomainOf(name);
                if (domain is null)
                {
                    continue;
                }

                // Des assets rangés sous le domaine d'un autre mod : le mod ajoute directement du
                // contenu chez lui, ce qui ne peut se comprendre que comme une intégration voulue.
                if (IsForeign(domain, ownDomain))
                {
                    Remember(signals, new ModIntegrationSignal(domain, Conditional: false, name));
                }

                if (opened >= MaxPatchFiles || !IsPatchFile(name) || entry.Length > MaxPatchFileBytes)
                {
                    continue;
                }

                opened++;
                foreach (var signal in ReadPatchTargets(entry, name, ownDomain))
                {
                    Remember(signals, signal);
                }
            }

            return [.. signals.Values];
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return [];
        }
    }

    // Une cible inconditionnelle prime sur une cible conditionnelle pour le même domaine : c'est la
    // plus exigeante des deux, donc celle qu'il faut savoir si l'autre mod venait à manquer.
    private static void Remember(Dictionary<string, ModIntegrationSignal> signals, ModIntegrationSignal signal)
    {
        if (!signals.TryGetValue(signal.TargetDomain, out var existing) || (existing.Conditional && !signal.Conditional))
        {
            signals[signal.TargetDomain] = signal;
        }
    }

    private static bool IsPatchFile(string entryName)
        => entryName.Contains(PatchesSegment, StringComparison.OrdinalIgnoreCase)
            && entryName.EndsWith(".json", StringComparison.OrdinalIgnoreCase);

    // « assets/carryon/patches/x.json » → « carryon ».
    private static string? DomainOf(string entryName)
    {
        var rest = entryName[AssetsPrefix.Length..];
        var slash = rest.IndexOf('/', StringComparison.Ordinal);

        return slash > 0 ? rest[..slash] : null;
    }

    private static bool IsForeign(string domain, string ownDomain)
        => !string.Equals(domain, ownDomain, StringComparison.OrdinalIgnoreCase)
            && !ModInfoParser.IsSpecialDependencyId(domain);

    private static List<ModIntegrationSignal> ReadPatchTargets(ZipArchiveEntry entry, string entryName, string ownDomain)
    {
        var signals = new List<ModIntegrationSignal>();

        try
        {
            using var source = entry.Open();
            using var reader = new StreamReader(source, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            using var document = JsonDocument.Parse(reader.ReadToEnd(), TolerantOptions);

            // Le format canonique est un tableau de patches ; quelques mods en écrivent un seul,
            // nu. Les deux se lisent, comme le fait le jeu.
            if (document.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var patch in document.RootElement.EnumerateArray())
                {
                    ReadPatch(patch, entryName, ownDomain, signals);
                }
            }
            else if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                ReadPatch(document.RootElement, entryName, ownDomain, signals);
            }
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException or IOException or ArgumentException)
        {
            // Un patch illisible n'apprend rien sur les intégrations de ce mod, et n'est pas un
            // défaut à rapporter ici : le JEU le dira au prochain lancement, et c'est le journal
            // qui le rapportera.
        }

        return signals;
    }

    private static void ReadPatch(JsonElement patch, string entryName, string ownDomain, List<ModIntegrationSignal> signals)
    {
        if (patch.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var conditional = HasDependsOn(patch, entryName, ownDomain, signals);

        if (TryGetString(patch, "file") is { } file)
        {
            var separator = file.IndexOf(':', StringComparison.Ordinal);
            if (separator > 0)
            {
                var domain = file[..separator];
                if (IsForeign(domain, ownDomain))
                {
                    signals.Add(new ModIntegrationSignal(domain, conditional, entryName));
                }
            }
        }
    }

    // « dependsOn » sert deux fois : il conditionne le patch, et il NOMME un autre mod, ce qui est
    // en soi une intégration, car un patch peut très bien viser son propre domaine tant qu'un autre
    // mod est là (c'est ainsi qu'un mod ajoute un comportement des autres à son propre contenu).
    private static bool HasDependsOn(JsonElement patch, string entryName, string ownDomain, List<ModIntegrationSignal> signals)
    {
        if (!TryGetProperty(patch, "dependsOn", out var dependsOn) || dependsOn.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var any = false;
        foreach (var dependency in dependsOn.EnumerateArray())
        {
            any = true;
            if (dependency.ValueKind == JsonValueKind.Object && TryGetString(dependency, "modid") is { } modId && IsForeign(modId, ownDomain))
            {
                signals.Add(new ModIntegrationSignal(modId, Conditional: true, entryName));
            }
        }

        return any;
    }

    // Casse des clés tolérée, comme pour modinfo.json : ces fichiers sont écrits à la main.
    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;

                return true;
            }
        }

        value = default;

        return false;
    }

    private static string? TryGetString(JsonElement element, string name)
        => TryGetProperty(element, name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}