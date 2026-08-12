using System.IO.Compression;
using System.Text;

namespace Prospect.Desktop.Tests.TestDoubles;

/// <summary>
/// Construit une vignette PNG minuscule et valide (RFC 2083), à la main, à l'octet près : quelques
/// pixels RGBA opaques d'une seule couleur. Écrit en BCL pur, SANS dépendre d'Avalonia — ce fichier
/// sert aussi bien <see cref="ModDbDoubles"/> (utilisé par des tests xUnit ordinaires, pas
/// seulement <c>[AvaloniaFact]</c>) que les tests qui décodent réellement le résultat en
/// <see cref="Avalonia.Media.Imaging.Bitmap"/>, qui eux doivent être <c>[AvaloniaFact]</c>.
/// </summary>
internal static class TinyPng
{
    private static readonly byte[] Signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>Construit un PNG RGBA <paramref name="size"/>×<paramref name="size"/> d'une seule couleur (copper-500 par défaut).</summary>
    public static byte[] Create(int size = 2, byte r = 0xC4, byte g = 0x85, byte b = 0x4F, byte a = 0xFF)
    {
        var raw = new byte[size * (1 + (size * 4))];
        var offset = 0;
        for (var y = 0; y < size; y++)
        {
            raw[offset++] = 0; // Filtre de ligne "None".
            for (var x = 0; x < size; x++)
            {
                raw[offset++] = r;
                raw[offset++] = g;
                raw[offset++] = b;
                raw[offset++] = a;
            }
        }

        using var idat = new MemoryStream();
        using (var zlib = new ZLibStream(idat, CompressionLevel.Fastest, leaveOpen: true))
        {
            zlib.Write(raw);
        }

        using var png = new MemoryStream();
        png.Write(Signature);
        WriteChunk(png, "IHDR", BuildIhdr(size));
        WriteChunk(png, "IDAT", idat.ToArray());
        WriteChunk(png, "IEND", []);

        return png.ToArray();
    }

    private static byte[] BuildIhdr(int size)
    {
        var ihdr = new byte[13];
        WriteUInt32BigEndian(ihdr, 0, (uint)size); // largeur
        WriteUInt32BigEndian(ihdr, 4, (uint)size); // hauteur
        ihdr[8] = 8; // profondeur : 8 bits par canal
        ihdr[9] = 6; // type de couleur : 6 = RGBA
        ihdr[10] = 0; // compression : seule valeur valide
        ihdr[11] = 0; // filtre : seule valeur valide
        ihdr[12] = 0; // entrelacement : aucun

        return ihdr;
    }

    private static void WriteChunk(Stream stream, string type, byte[] data)
    {
        var typeBytes = Encoding.ASCII.GetBytes(type);
        var length = new byte[4];
        WriteUInt32BigEndian(length, 0, (uint)data.Length);
        stream.Write(length);
        stream.Write(typeBytes);
        stream.Write(data);

        var crc = new byte[4];
        WriteUInt32BigEndian(crc, 0, Crc32(typeBytes, data));
        stream.Write(crc);
    }

    private static void WriteUInt32BigEndian(byte[] buffer, int offset, uint value)
    {
        buffer[offset] = (byte)(value >> 24);
        buffer[offset + 1] = (byte)(value >> 16);
        buffer[offset + 2] = (byte)(value >> 8);
        buffer[offset + 3] = (byte)value;
    }

    // CRC32 standard (polynôme 0xEDB88320), tel qu'exigé par la spécification PNG pour chaque chunk.
    private static uint Crc32(byte[] type, byte[] data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var value in type)
        {
            crc = Crc32Step(crc, value);
        }

        foreach (var value in data)
        {
            crc = Crc32Step(crc, value);
        }

        return crc ^ 0xFFFFFFFFu;
    }

    private static uint Crc32Step(uint crc, byte value)
    {
        crc ^= value;
        for (var i = 0; i < 8; i++)
        {
            crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
        }

        return crc;
    }
}