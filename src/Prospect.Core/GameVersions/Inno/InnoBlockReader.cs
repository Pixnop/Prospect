using System.Buffers.Binary;
using SharpCompress.Compressors.LZMA;

namespace Prospect.Core.GameVersions.Inno;

/// <summary>
/// Lecture des deux blocs d'en-tête d'un installeur Inno Setup : celui qui décrit le script, puis
/// celui qui décrit l'emplacement des données.
/// </summary>
/// <remarks>
/// <para>
/// Un bloc s'ouvre sur un CRC32 de son propre en-tête, une taille STOCKÉE et un octet de
/// compression. Vient ensuite une suite de tronçons de 4096 octets au plus, chacun précédé de son
/// CRC32. Le piège du format est que la taille stockée compte le bloc ENTIER, préfixes de CRC
/// compris, et pas seulement la charge utile ; la prendre pour la seule charge utile fait lire cent
/// soixante-douze octets de trop sur l'en-tête de Vintage Story et casse le dernier tronçon.
/// </para>
/// <para>
/// Les CRC sont vérifiés, tous. Ils ne servent pas seulement à détecter un fichier abîmé : ils
/// confirment surtout que le décalage auquel on a commencé à lire était le bon. Un faux positif de
/// la recherche du marqueur de chargeur se solde par un CRC qui ne tombe pas juste, donc par un
/// repli, jamais par une extraction silencieusement fausse.
/// </para>
/// <para>
/// La charge compressée est un flux LZMA1 brut : cinq octets de propriétés, puis les données, SANS
/// marqueur de fin. Le décodeur s'arrête donc en même temps que son entrée, ce qui est normal et
/// non une erreur.
/// </para>
/// </remarks>
internal static class InnoBlockReader
{
    /// <summary>Taille maximale d'un tronçon, imposée par le format.</summary>
    private const int ChunkSize = 4096;

    /// <summary>
    /// Garde-fou sur la taille décompressée d'un bloc d'en-tête. Un en-tête réel pèse quelques
    /// mégaoctets ; au-delà de cette borne, c'est qu'on ne lit pas un en-tête.
    /// </summary>
    private const int MaxDecompressedSize = 256 * 1024 * 1024;

    /// <summary>
    /// Lit un bloc à la position courante du flux et rend son contenu décompressé. Le flux est
    /// laissé juste après le bloc, prêt pour le suivant.
    /// </summary>
    /// <param name="stream">Flux de l'installeur, positionné sur le début du bloc.</param>
    public static byte[] Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        Span<byte> head = stackalloc byte[9];
        ReadExactly(stream, head);

        var expectedCrc = BinaryPrimitives.ReadUInt32LittleEndian(head);
        var actualCrc = InnoCrc32.Compute(head[4..]);
        if (expectedCrc != actualCrc)
        {
            throw InnoFormatException.Corrupt("le CRC32 de l'en-tête de bloc ne correspond pas");
        }

        var storedSize = BinaryPrimitives.ReadUInt32LittleEndian(head[4..]);
        var compressed = head[8] != 0;

        var payload = ReadChunks(stream, storedSize);

        return compressed ? Decompress(payload) : payload;
    }

    /// <summary>
    /// Rassemble la charge utile en vérifiant le CRC32 de chaque tronçon.
    /// </summary>
    private static byte[] ReadChunks(Stream stream, uint storedSize)
    {
        var output = new MemoryStream();
        var buffer = new byte[ChunkSize];
        Span<byte> crcBytes = stackalloc byte[4];
        var remaining = (long)storedSize;

        while (remaining > 0)
        {
            if (remaining < 5)
            {
                throw InnoFormatException.Corrupt("le bloc se termine sur un tronçon tronqué");
            }

            ReadExactly(stream, crcBytes);
            remaining -= 4;

            var take = (int)Math.Min(ChunkSize, remaining);
            ReadExactly(stream, buffer.AsSpan(0, take));
            remaining -= take;

            var expected = BinaryPrimitives.ReadUInt32LittleEndian(crcBytes);
            if (expected != InnoCrc32.Compute(buffer.AsSpan(0, take)))
            {
                throw InnoFormatException.Corrupt("le CRC32 d'un tronçon de bloc ne correspond pas");
            }

            output.Write(buffer, 0, take);
        }

        return output.ToArray();
    }

    /// <summary>
    /// Décompresse un bloc LZMA1 brut, dépourvu de marqueur de fin.
    /// </summary>
    private static byte[] Decompress(byte[] payload)
    {
        if (payload.Length < 5)
        {
            throw InnoFormatException.Corrupt("bloc compressé plus court que son en-tête LZMA");
        }

        var properties = payload[..5];
        using var input = new MemoryStream(payload, 5, payload.Length - 5, writable: false);
        using var lzma = LzmaStream.Create(properties, input, leaveOpen: true);

        var output = new MemoryStream();
        var buffer = new byte[64 * 1024];

        try
        {
            int read;
            while ((read = lzma.Read(buffer, 0, buffer.Length)) > 0)
            {
                output.Write(buffer, 0, read);
                if (output.Length > MaxDecompressedSize)
                {
                    throw InnoFormatException.Corrupt("bloc d'en-tête démesuré");
                }
            }
        }
        catch (Exception exception) when (exception is not InnoFormatException)
        {
            // Sans marqueur de fin, le décodeur peut signaler la fin de son entrée par une
            // exception plutôt que par un zéro. Ce qui a déjà été produit reste valide, et c'est
            // la lecture des enregistrements qui dira si le bloc est complet : elle échoue en butant
            // sur la fin si le flux s'est arrêté trop tôt.
            if (output.Length == 0)
            {
                throw InnoFormatException.Corrupt($"décompression du bloc impossible ({exception.Message})");
            }
        }

        return output.ToArray();
    }

    private static void ReadExactly(Stream stream, Span<byte> buffer)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var count = stream.Read(buffer[read..]);
            if (count <= 0)
            {
                throw InnoFormatException.Corrupt("fin de fichier au milieu d'un bloc d'en-tête");
            }

            read += count;
        }
    }
}
