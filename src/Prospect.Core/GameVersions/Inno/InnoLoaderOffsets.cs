using System.Buffers.Binary;

namespace Prospect.Core.GameVersions.Inno;

/// <summary>
/// Les deux décalages qui ouvrent un installeur Inno Setup : celui de l'en-tête et celui des
/// données.
/// </summary>
/// <param name="HeaderOffset">Position de la chaîne d'identification qui précède les blocs d'en-tête.</param>
/// <param name="DataOffset">Position du premier bloc de données.</param>
internal readonly record struct InnoLoaderOffsets(long HeaderOffset, long DataOffset)
{
    /// <summary>
    /// Marqueurs du chargeur, dans les deux variantes qu'emploient les versions modernes d'Inno
    /// Setup.
    /// </summary>
    private static readonly byte[][] Magics =
    [
        [0x72, 0x44, 0x6C, 0x50, 0x74, 0x53, 0xCD, 0xE6, 0xD7, 0x7B, 0x0B, 0x2A],
        [0x6E, 0x53, 0x35, 0x57, 0x37, 0x64, 0x54, 0x83, 0xAA, 0x1B, 0x0F, 0x6A],
    ];

    /// <summary>
    /// Longueur du préambule fouillé. Le chargeur vit dans le talon exécutable, en tête de fichier ;
    /// au-delà commencent les données du jeu, où la recherche n'aurait plus de sens et coûterait une
    /// lecture de six cents mégaoctets.
    /// </summary>
    private const int SearchWindow = 16 * 1024 * 1024;

    /// <summary>
    /// Retrouve les décalages dans un installeur.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Le chargeur est rangé dans une ressource PE que Setup retrouve par le sommaire du format
    /// exécutable. Prospect le cherche par son marqueur plutôt que de dérouler le sommaire :
    /// beaucoup moins de code à écrire et à maintenir, pour une décision qui n'a de toute façon pas
    /// le droit d'être prise sur la foi du seul marqueur. Chaque candidat est donc VÉRIFIÉ, et la
    /// recherche continue tant qu'un candidat ne tient pas : les décalages doivent rester dans le
    /// fichier, se suivre dans le bon ordre, et l'en-tête doit réellement porter une chaîne
    /// d'identification Inno Setup. Un motif de douze octets qui apparaîtrait par hasard dans une
    /// ressource ne passe aucun de ces trois contrôles.
    /// </para>
    /// </remarks>
    /// <param name="stream">Flux de l'installeur, repositionnable.</param>
    /// <param name="offsets">Décalages trouvés.</param>
    public static bool TryFind(Stream stream, out InnoLoaderOffsets offsets)
    {
        ArgumentNullException.ThrowIfNull(stream);

        offsets = default;

        var length = stream.Length;
        var window = (int)Math.Min(SearchWindow, length);
        var buffer = new byte[window];

        stream.Seek(0, SeekOrigin.Begin);
        var filled = 0;
        while (filled < window)
        {
            var read = stream.Read(buffer, filled, window - filled);
            if (read <= 0)
            {
                break;
            }

            filled += read;
        }

        var haystack = buffer.AsSpan(0, filled);

        foreach (var magic in Magics)
        {
            var searched = 0;
            while (true)
            {
                var found = haystack[searched..].IndexOf(magic);
                if (found < 0)
                {
                    break;
                }

                var at = searched + found;
                searched = at + 1;

                if (TryReadOffsets(haystack, at, length, out offsets) && Confirm(stream, offsets))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool TryReadOffsets(ReadOnlySpan<byte> haystack, int magicAt, long fileLength, out InnoLoaderOffsets offsets)
    {
        offsets = default;

        // Marqueur, révision, un mot réservé, décalage et taille du talon, son empreinte, puis les
        // deux décalages qui nous intéressent.
        const int TableLength = 12 + (7 * 4);
        if (magicAt + TableLength > haystack.Length)
        {
            return false;
        }

        var table = haystack.Slice(magicAt + 12, 7 * 4);
        if (BinaryPrimitives.ReadUInt32LittleEndian(table) != 1u)
        {
            return false;
        }

        var headerOffset = BinaryPrimitives.ReadUInt32LittleEndian(table[(5 * 4)..]);
        var dataOffset = BinaryPrimitives.ReadUInt32LittleEndian(table[(6 * 4)..]);

        if (dataOffset == 0 || headerOffset <= dataOffset || headerOffset + InnoSetupVersion.IdLength > fileLength)
        {
            return false;
        }

        offsets = new InnoLoaderOffsets(headerOffset, dataOffset);

        return true;
    }

    private static bool Confirm(Stream stream, InnoLoaderOffsets candidate)
    {
        var id = new byte[InnoSetupVersion.IdLength];
        stream.Seek(candidate.HeaderOffset, SeekOrigin.Begin);

        var read = 0;
        while (read < id.Length)
        {
            var count = stream.Read(id, read, id.Length - read);
            if (count <= 0)
            {
                return false;
            }

            read += count;
        }

        return InnoSetupVersion.TryParse(id, out _);
    }
}
