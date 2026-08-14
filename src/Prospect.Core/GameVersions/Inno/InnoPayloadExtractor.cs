using System.Buffers;
using System.IO.Abstractions;
using System.Security.Cryptography;

using SharpCompress.Compressors.LZMA;

namespace Prospect.Core.GameVersions.Inno;

/// <summary>
/// Sort le jeu d'un installeur Inno Setup SANS l'exécuter, en lisant son format.
/// </summary>
/// <remarks>
/// <para>
/// C'est la voie principale de l'installation Windows, et sa raison d'être n'est pas la vitesse :
/// c'est que rien du SCRIPT de l'installeur n'est joué. Pas de script joué, donc pas de
/// <c>MsgBox</c> « une ancienne version a été détectée » posée par <c>InitializeSetup</c>, et pas
/// de clé de désinstallation écrite, donc pas de boîte non plus la fois suivante. Voir
/// docs/architecture.md pour l'enchaînement complet.
/// </para>
/// <para>
/// Ce qui est posé, ce sont les seules entrées destinées à <c>{app}</c>, c'est-à-dire le jeu. Les
/// polices que le script installe dans le dossier système, ses clés de registre, ses raccourcis et
/// son lancement final sont traversés puis laissés de côté : une version installée par Prospect vit
/// entièrement dans son propre dossier, et le build Linux du jeu (un simple <c>.tar.gz</c> sans le
/// moindre effet de bord) démontre que rien de tout cela ne conditionne le démarrage.
/// </para>
/// <para>
/// Chaque fichier écrit est vérifié contre l'empreinte SHA-256 que l'installeur déclare pour lui.
/// C'est ce qui autorise à faire confiance à une lecture de format faite à la main : une grille de
/// lecture fausse ne produit pas discrètement un jeu abîmé, elle échoue à la première empreinte et
/// l'installation repart sur l'installeur officiel.
/// </para>
/// </remarks>
internal sealed class InnoPayloadExtractor
{
    /// <summary>Préfixe des destinations qui composent le jeu lui-même.</summary>
    private const string AppPrefix = "{app}\\";

    /// <summary>Marqueur de début de bloc de données.</summary>
    private static readonly byte[] ChunkMagic = [0x7A, 0x6C, 0x62, 0x1A];

    /// <summary>
    /// Taille au-delà de laquelle une entrée est refusée. Le plus gros fichier du jeu pèse une
    /// vingtaine de mégaoctets ; cette borne protège d'une taille lue de travers qui demanderait un
    /// tampon absurde.
    /// </summary>
    private const long MaxFileSize = 1L << 31;

    private readonly IFileSystem _fileSystem;

    /// <summary>Construit l'extracteur.</summary>
    /// <param name="fileSystem">Système de fichiers abstrait.</param>
    public InnoPayloadExtractor(IFileSystem fileSystem)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);

        _fileSystem = fileSystem;
    }

    /// <summary>
    /// Extrait le jeu dans <paramref name="targetDirectory"/>.
    /// </summary>
    /// <param name="installerPath">Installeur téléchargé.</param>
    /// <param name="targetDirectory">Dossier de la version.</param>
    /// <param name="progress">
    /// Avancement MESURÉ, et non estimé : les tailles de toutes les entrées sont connues avant de
    /// commencer, donc le dénominateur est exact.
    /// </param>
    /// <param name="cancellationToken">Annulation.</param>
    /// <exception cref="InnoFormatException">
    /// Format inconnu, lecture incohérente, ou empreinte qui ne correspond pas. L'appelant retombe
    /// alors sur l'exécution de l'installeur.
    /// </exception>
    public async Task ExtractAsync(
        string installerPath,
        string targetDirectory,
        IProgress<GameInstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(installerPath);
        ArgumentException.ThrowIfNullOrEmpty(targetDirectory);

        var root = _fileSystem.Path.GetFullPath(targetDirectory);
        _fileSystem.Directory.CreateDirectory(root);

        var installer = _fileSystem.File.OpenRead(installerPath);
        await using (installer.ConfigureAwait(false))
        {
            if (!InnoLoaderOffsets.TryFind(installer, out var offsets))
            {
                throw InnoFormatException.Unsupported("aucun chargeur Inno Setup reconnu dans le fichier");
            }

            var script = InnoSetupScript.Read(installer, offsets.HeaderOffset);
            var plan = BuildPlan(script);

            ExtractPlan(installer, offsets, script, plan, root, progress, cancellationToken);
        }
    }

    /// <summary>
    /// Trie ce qu'il y a à écrire dans l'ordre où le flux le rendra : par bloc, puis par position
    /// dans le bloc.
    /// </summary>
    /// <remarks>
    /// Un bloc de données est un flux compressé SOLIDE : on ne peut pas s'y positionner, seulement
    /// le dérouler. Extraire les fichiers dans l'ordre du script demanderait de le redérouler depuis
    /// le début à chaque fichier, soit huit cent soixante mégaoctets décompressés vingt mille fois.
    /// Les lire dans l'ordre du flux, en traversant sans les écrire les entrées qui ne nous
    /// concernent pas, ne le déroule qu'une fois.
    /// </remarks>
    private static ExtractionPlan BuildPlan(InnoSetupScript script)
    {
        var destinations = new Dictionary<uint, string>();

        foreach (var file in script.Files)
        {
            if (!file.HasData
                || !file.Destination.StartsWith(AppPrefix, StringComparison.OrdinalIgnoreCase)
                || file.Location >= (uint)script.DataEntries.Count)
            {
                continue;
            }

            // Deux entrées peuvent viser la même destination sous des conditions d'installation
            // différentes (variantes 32 et 64 bits). La première gagne, comme le ferait Setup en
            // suivant l'ordre du script.
            destinations.TryAdd(file.Location, file.Destination[AppPrefix.Length..]);
        }

        if (destinations.Count == 0)
        {
            throw InnoFormatException.Corrupt("aucun fichier destiné au dossier d'installation");
        }

        var items = new List<PlannedFile>(destinations.Count);
        foreach (var (location, relativePath) in destinations)
        {
            var entry = script.DataEntries[(int)location];

            if (entry.Encrypted)
            {
                throw InnoFormatException.Unsupported("données chiffrées");
            }

            if (entry.FileSize > MaxFileSize)
            {
                throw InnoFormatException.Corrupt($"entrée de {entry.FileSize} octets");
            }

            items.Add(new PlannedFile(entry, relativePath));
        }

        items.Sort(static (left, right) => Compare(left.Entry, right.Entry));

        var total = 0L;
        foreach (var item in items)
        {
            total += (long)item.Entry.FileSize;
        }

        return new ExtractionPlan(items, total);
    }

    /// <summary>
    /// Ordonne deux entrées comme le flux les rendra.
    /// </summary>
    /// <remarks>
    /// La taille départage les ex æquo, et ce n'est pas une coquetterie : un fichier VIDE partage
    /// son décalage avec celui qui le suit, puisqu'il n'occupe rien. Le tri de la bibliothèque
    /// standard n'est pas stable, il plaçait donc parfois le fichier vide APRÈS son voisin, et la
    /// lecture butait sur une entrée qui semblait revenir en arrière. L'installeur de Vintage Story
    /// en contient exactement un : trier par tailles croissantes met le fichier vide devant, où sa
    /// lecture de zéro octet ne dérange personne.
    /// </remarks>
    private static int Compare(InnoDataEntry left, InnoDataEntry right)
    {
        if (left.ChunkOffset != right.ChunkOffset)
        {
            return left.ChunkOffset.CompareTo(right.ChunkOffset);
        }

        return left.FileOffset != right.FileOffset
            ? left.FileOffset.CompareTo(right.FileOffset)
            : left.FileSize.CompareTo(right.FileSize);
    }

    private void ExtractPlan(
        Stream installer,
        InnoLoaderOffsets offsets,
        InnoSetupScript script,
        ExtractionPlan plan,
        string root,
        IProgress<GameInstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        var written = 0L;
        var lastPercent = -1;
        var index = 0;

        while (index < plan.Files.Count)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var chunkOffset = plan.Files[index].Entry.ChunkOffset;
            var last = index;
            while (last + 1 < plan.Files.Count && plan.Files[last + 1].Entry.ChunkOffset == chunkOffset)
            {
                last++;
            }

            using var chunk = OpenChunk(installer, offsets, script, plan.Files[index].Entry);

            var position = 0UL;
            for (; index <= last; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var planned = plan.Files[index];
                var entry = planned.Entry;

                if (entry.FileOffset < position)
                {
                    throw InnoFormatException.Corrupt("les entrées d'un bloc ne se suivent pas");
                }

                Discard(chunk, entry.FileOffset - position);
                position = entry.FileOffset;

                var size = (int)entry.FileSize;
                var buffer = ArrayPool<byte>.Shared.Rent(size);
                try
                {
                    ReadExactly(chunk, buffer.AsSpan(0, size));
                    position += entry.FileSize;

                    var content = buffer.AsSpan(0, size);
                    if (entry.CallInstructionOptimized)
                    {
                        InnoCallInstructionFilter.Unfilter(content);
                    }

                    VerifyChecksum(content, entry, planned.RelativePath);
                    Write(root, planned.RelativePath, content);
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                }

                written += size;

                if (progress is null || plan.TotalBytes <= 0)
                {
                    continue;
                }

                // Un rapport par point de pourcentage : vingt mille fichiers, et chaque rapport
                // repasse par le dispatcher de l'interface.
                var ratio = Math.Clamp((double)written / plan.TotalBytes, 0d, 1d);
                var percent = (int)(ratio * 100d);
                if (percent != lastPercent)
                {
                    lastPercent = percent;
                    progress.Report(GameInstallProgress.ForInstalling(ratio));
                }
            }
        }
    }

    private static Stream OpenChunk(
        Stream installer,
        InnoLoaderOffsets offsets,
        InnoSetupScript script,
        InnoDataEntry entry)
    {
        installer.Seek(offsets.DataOffset + entry.ChunkOffset, SeekOrigin.Begin);

        Span<byte> magic = stackalloc byte[4];
        ReadExactly(installer, magic);
        if (!magic.SequenceEqual(ChunkMagic))
        {
            throw InnoFormatException.Corrupt("marqueur de bloc de données absent");
        }

        if (!entry.Compressed)
        {
            return new NonDisposingStream(installer);
        }

        return script.Compression switch
        {
            InnoCompression.Lzma2 => OpenLzma(installer, lzma2: true),
            InnoCompression.Lzma1 => OpenLzma(installer, lzma2: false),
            var other => throw InnoFormatException.Unsupported($"compression {other}"),
        };
    }

    private static LzmaStream OpenLzma(Stream installer, bool lzma2)
    {
        // LZMA2 annonce sa taille de dictionnaire sur un octet, LZMA1 ses propriétés sur cinq.
        var properties = new byte[lzma2 ? 1 : 5];
        ReadExactly(installer, properties);

        return LzmaStream.Create(properties, installer, -1, -1, null, lzma2, leaveOpen: true);
    }

    private static void VerifyChecksum(ReadOnlySpan<byte> content, InnoDataEntry entry, string relativePath)
    {
        Span<byte> digest = stackalloc byte[32];
        SHA256.HashData(content, digest);

        if (!digest.SequenceEqual(entry.Sha256))
        {
            throw InnoFormatException.Corrupt($"empreinte SHA-256 incorrecte pour « {relativePath} »");
        }
    }

    private void Write(string root, string relativePath, ReadOnlySpan<byte> content)
    {
        var destination = ResolveSafePath(root, relativePath)
            ?? throw InnoFormatException.Corrupt($"destination hors du dossier de version : « {relativePath} »");

        var parent = _fileSystem.Path.GetDirectoryName(destination);
        if (!string.IsNullOrEmpty(parent))
        {
            _fileSystem.Directory.CreateDirectory(parent);
        }

        using var file = _fileSystem.File.Create(destination);
        file.Write(content);
    }

    // Même garde-fou que pour les archives tar : une entrée dont le chemin remonterait hors de la
    // cible n'est pas écrite. L'installeur est un fichier distant, il ne décide pas où on écrit.
    private string? ResolveSafePath(string root, string relativePath)
    {
        var relative = relativePath.Replace('\\', '/').TrimStart('/');
        if (string.IsNullOrWhiteSpace(relative))
        {
            return null;
        }

        var candidate = _fileSystem.Path.GetFullPath(_fileSystem.Path.Combine(root, relative));
        var prefix = root.EndsWith(_fileSystem.Path.DirectorySeparatorChar) ? root : root + _fileSystem.Path.DirectorySeparatorChar;

        return candidate.StartsWith(prefix, StringComparison.Ordinal) ? candidate : null;
    }

    private static void Discard(Stream stream, ulong count)
    {
        if (count == 0)
        {
            return;
        }

        var buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
        try
        {
            while (count > 0)
            {
                var take = (int)Math.Min((ulong)buffer.Length, count);
                ReadExactly(stream, buffer.AsSpan(0, take));
                count -= (ulong)take;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static void ReadExactly(Stream stream, Span<byte> buffer)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var count = stream.Read(buffer[read..]);
            if (count <= 0)
            {
                throw InnoFormatException.Corrupt("flux de données interrompu avant la fin annoncée");
            }

            read += count;
        }
    }

    private readonly record struct PlannedFile(InnoDataEntry Entry, string RelativePath);

    private sealed record ExtractionPlan(IReadOnlyList<PlannedFile> Files, long TotalBytes);

    /// <summary>
    /// Enveloppe qui empêche la fermeture du flux de l'installeur quand un bloc est stocké sans
    /// compression et donc lu directement.
    /// </summary>
    private sealed class NonDisposingStream : Stream
    {
        private readonly Stream _inner;

        public NonDisposingStream(Stream inner) => _inner = inner;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);

        public override int Read(Span<byte> buffer) => _inner.Read(buffer);

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}