using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

using SharpCompress.Compressors.LZMA;

namespace Prospect.Tests.GameVersions.Inno;

/// <summary>
/// Fabrique des installeurs Inno Setup synthétiques, au format binaire réel.
/// </summary>
/// <remarks>
/// <para>
/// Il n'existe pas de compilateur Inno Setup sur une machine de développement Linux, et commiter un
/// installeur officiel de six cents mégaoctets n'a évidemment pas de sens. Les fixtures sont donc
/// ÉCRITES, octet par octet, par ce constructeur : marqueur du chargeur, table de décalages,
/// blocs d'en-tête à CRC, table des fichiers, table des emplacements, bloc de données.
/// </para>
/// <para>
/// Le bénéfice dépasse le test. Ce constructeur est le miroir exact du lecteur, donc les deux
/// forment une spécification exécutable du format : un champ ajouté d'un côté sans l'autre fait
/// échouer la suite immédiatement, là où un blob binaire opaque n'aurait rien dit du POURQUOI de
/// chaque octet. Ce que ces fixtures ne peuvent pas prouver, en revanche, c'est que la grille de
/// lecture est celle du vrai installeur : elles se contentent d'être cohérentes avec elle. Cette
/// preuve-là est ailleurs, dans <c>InnoPayloadExtractorLiveTests</c>, qui extrait l'installeur
/// officiel et vérifie ses vingt mille empreintes.
/// </para>
/// </remarks>
internal sealed class SyntheticInnoInstaller
{
    /// <summary>Marqueur du chargeur, variante moderne.</summary>
    private static readonly byte[] LoaderMagic =
        [0x72, 0x44, 0x6C, 0x50, 0x74, 0x53, 0xCD, 0xE6, 0xD7, 0x7B, 0x0B, 0x2A];

    private static readonly byte[] ChunkMagic = [0x7A, 0x6C, 0x62, 0x1A];

    private readonly List<Entry> _entries = [];

    /// <summary>Version du format écrite dans la chaîne d'identification.</summary>
    public string DataVersion { get; set; } = "6.4.3";

    /// <summary>Méthode de compression annoncée par l'en-tête.</summary>
    public byte Compression { get; set; } = 4; // LZMA2

    /// <summary>Vrai pour compresser réellement le bloc de données en LZMA2.</summary>
    public bool CompressPayload { get; set; }

    /// <summary>Vrai pour compresser les blocs d'en-tête en LZMA1, comme le fait le vrai outil.</summary>
    public bool CompressHeaders { get; set; }

    /// <summary>Ajoute un fichier au script.</summary>
    /// <param name="destination">Destination complète, constantes comprises (<c>{app}\x.txt</c>).</param>
    /// <param name="content">Contenu du fichier.</param>
    /// <param name="callInstructionOptimized">Vrai pour marquer l'entrée comme filtrée.</param>
    /// <param name="checksumOverride">Empreinte à écrire à la place de la vraie, pour les tests d'intégrité.</param>
    public SyntheticInnoInstaller Add(
        string destination,
        byte[] content,
        bool callInstructionOptimized = false,
        byte[]? checksumOverride = null)
    {
        _entries.Add(new Entry(destination, content, callInstructionOptimized, checksumOverride));

        return this;
    }

    /// <summary>Ajoute une entrée de fichier sans données, comme le désinstalleur.</summary>
    public SyntheticInnoInstaller AddWithoutData(string destination)
    {
        _entries.Add(new Entry(destination, [], false, null) { HasData = false });

        return this;
    }

    /// <summary>Assemble l'installeur.</summary>
    public byte[] Build()
    {
        // Ce que le bloc de données contiendra, dans l'ordre des entrées : le contenu FILTRÉ, parce
        // que c'est bien la forme transformée que l'installeur stocke.
        var payload = new MemoryStream();
        var locations = new List<Location>();

        foreach (var entry in _entries)
        {
            if (!entry.HasData)
            {
                continue;
            }

            var offset = (ulong)payload.Length;
            var stored = entry.CallInstructionOptimized ? Filter(entry.Content) : entry.Content;
            payload.Write(stored, 0, stored.Length);

            locations.Add(new Location(
                offset,
                (ulong)entry.Content.Length,
                entry.ChecksumOverride ?? SHA256.HashData(entry.Content),
                entry.CallInstructionOptimized));
        }

        var chunkBody = CompressPayload ? CompressLzma2(payload.ToArray()) : payload.ToArray();

        var primary = WriteBlock(BuildPrimaryBlock(locations.Count));
        var secondary = WriteBlock(BuildSecondaryBlock(locations, (ulong)chunkBody.Length));

        // Talon exécutable factice : le chargeur vit à l'intérieur d'un vrai PE, et la recherche du
        // marqueur doit fonctionner même quand il n'est pas au tout début.
        var stub = new byte[512];
        Array.Fill(stub, (byte)0x90);

        var output = new MemoryStream();
        output.Write(stub);

        var loaderAt = (int)output.Length;
        output.Write(LoaderMagic);
        var tableAt = (int)output.Length;
        output.Write(new byte[7 * 4]); // rempli une fois les décalages connus

        var dataOffset = (int)output.Length;
        output.Write(ChunkMagic);
        if (CompressPayload)
        {
            output.WriteByte(Lzma2DictionaryProperty);
        }

        output.Write(chunkBody);

        var headerOffset = (int)output.Length;
        output.Write(BuildId());
        output.Write(primary);
        output.Write(secondary);

        var bytes = output.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(tableAt), 1u); // révision
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(tableAt + (5 * 4)), (uint)headerOffset);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(tableAt + (6 * 4)), (uint)dataOffset);
        _ = loaderAt;

        return bytes;
    }

    /// <summary>
    /// Applique la transformation des adresses de <c>CALL</c> et <c>JMP</c>, c'est-à-dire l'inverse
    /// exact de ce que fait <c>InnoCallInstructionFilter</c>.
    /// </summary>
    /// <remarks>
    /// Écrire les deux sens permet de vérifier le filtre par un aller-retour sur des données qui
    /// contiennent réellement des octets <c>0xE8</c> et <c>0xE9</c>, y compris à des positions
    /// piégeuses : à cheval sur une frontière de bloc, ou dans les octets d'adresse d'une
    /// instruction précédente.
    /// </remarks>
    public static byte[] Filter(byte[] original)
    {
        var data = (byte[])original.Clone();
        var i = 0;

        while (i + 5 <= data.Length)
        {
            if (data[i] is not (0xE8 or 0xE9) || 0x10000 - (i % 0x10000) < 5)
            {
                i++;
                continue;
            }

            var high = data[i + 4];
            var relative = (uint)(data[i + 1] | (data[i + 2] << 8) | (data[i + 3] << 16));

            // Le compilateur ne transforme que ce que le lecteur retransformera : il faut donc
            // raisonner sur l'octet de poids fort tel qu'il sera VU à la relecture.
            var flipped = (relative & 0x800000u) != 0 ? (byte)~high : high;
            if (flipped is not (0x00 or 0xFF))
            {
                i++;
                continue;
            }

            var address = (uint)(i + 5) & 0xFFFFFFu;
            var absolute = (relative + address) & 0xFFFFFFu;

            data[i + 1] = (byte)absolute;
            data[i + 2] = (byte)(absolute >> 8);
            data[i + 3] = (byte)(absolute >> 16);
            data[i + 4] = flipped;

            i += 5;
        }

        return data;
    }

    private const byte Lzma2DictionaryProperty = 0x12;

    private byte[] BuildId()
    {
        var id = new byte[64];
        var text = Encoding.ASCII.GetBytes($"Inno Setup Setup Data ({DataVersion})");
        text.CopyTo(id, 0);

        return id;
    }

    private byte[] BuildPrimaryBlock(int dataEntryCount)
    {
        var writer = new BlockWriter();

        var version = ParseVersion(DataVersion);
        var stringCount = version >= Encode(6, 4, 2, 0) ? 33 : 32;
        for (var i = 0; i < stringCount + 4; i++)
        {
            writer.String(string.Empty);
        }

        // Langues, messages, permissions, types, composants, tâches : aucun.
        for (var i = 0; i < 6; i++)
        {
            writer.UInt32(0);
        }

        writer.UInt32(0);                        // répertoires
        writer.UInt32((uint)_entries.Count);     // fichiers
        writer.UInt32((uint)dataEntryCount);     // emplacements

        // Icônes, ini, registre, suppressions, suppressions de désinstallation, exécutions,
        // exécutions de désinstallation : aucune.
        for (var i = 0; i < 7; i++)
        {
            writer.UInt32(0);
        }

        writer.Zero(20); // intervalle de versions Windows

        if (version < Encode(6, 4, 0, 1))
        {
            writer.Zero(8); // couleurs de fond
        }

        writer.Zero(1);  // style d'assistant
        writer.Zero(8);  // pourcentages de redimensionnement
        writer.Zero(1);  // format alpha
        writer.Zero(4);  // empreinte du mot de passe
        writer.Zero(44); // sel, itérations, nonce
        writer.Zero(8);  // espace disque supplémentaire
        writer.UInt32(1); // tranches par disque
        writer.Zero(1);  // mode de journal
        writer.Zero(1);  // avertissement de dossier
        writer.Zero(1);  // privilèges
        writer.Zero(1);  // surcharges de privilèges
        writer.Zero(1);  // dialogue de langue
        writer.Zero(1);  // détection de langue
        writer.Byte(Compression);
        writer.Zero(1);  // page de dossier
        writer.Zero(1);  // page de groupe
        writer.Zero(8);  // taille affichée
        writer.Zero(version >= Encode(6, 4, 0, 1) ? 6 : 7); // options

        var location = 0u;
        foreach (var entry in _entries)
        {
            writer.String(entry.Destination);            // source
            writer.String(entry.Destination);            // destination
            writer.String(string.Empty);                 // police
            writer.String(string.Empty);                 // nom fort d'assemblage
            for (var i = 0; i < 6; i++)
            {
                writer.String(string.Empty);             // conditions
            }

            writer.Zero(20);                             // versions Windows
            writer.UInt32(entry.HasData ? location++ : 0xFFFFFFFFu);
            writer.Zero(4);                              // attributs
            writer.Zero(8);                              // taille externe
            writer.Zero(2);                              // permission
            writer.Zero(4);                              // drapeaux
            writer.Zero(1);                              // type
        }

        writer.UInt32(0); // images d'assistant
        writer.UInt32(0); // images d'assistant, petit format

        return writer.ToArray();
    }

    private byte[] BuildSecondaryBlock(List<Location> locations, ulong chunkSize)
    {
        var writer = new BlockWriter();
        var version = ParseVersion(DataVersion);
        var compact = version >= Encode(6, 4, 3, 0);

        foreach (var location in locations)
        {
            writer.UInt32(0);  // première tranche
            writer.UInt32(0);  // dernière tranche
            writer.UInt32(0);  // décalage du bloc
            writer.UInt64(location.Offset);
            writer.UInt64(location.Size);
            writer.UInt64(chunkSize);
            writer.Bytes(location.Sha256);
            writer.Zero(8);    // horodatage
            writer.Zero(8);    // version de fichier

            var flags = 0;
            if (compact)
            {
                if (location.CallInstructionOptimized)
                {
                    flags |= 1 << 2;
                }

                if (CompressPayload)
                {
                    flags |= 1 << 4;
                }

                writer.Byte((byte)flags);
            }
            else
            {
                if (location.CallInstructionOptimized)
                {
                    flags |= 1 << 4;
                }

                if (CompressPayload)
                {
                    flags |= 1 << 7;
                }

                writer.UInt16((ushort)flags);
                writer.Zero(1); // mode de signature
            }
        }

        return writer.ToArray();
    }

    private byte[] WriteBlock(byte[] content)
    {
        var body = CompressHeaders ? CompressLzma1(content) : content;

        var chunked = new MemoryStream();
        for (var offset = 0; offset < body.Length; offset += 4096)
        {
            var take = Math.Min(4096, body.Length - offset);
            var crc = new byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(crc, Crc32(body.AsSpan(offset, take)));
            chunked.Write(crc);
            chunked.Write(body, offset, take);
        }

        var storedSize = (uint)chunked.Length;
        var head = new byte[5];
        BinaryPrimitives.WriteUInt32LittleEndian(head, storedSize);
        head[4] = CompressHeaders ? (byte)1 : (byte)0;

        var output = new MemoryStream();
        var headCrc = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(headCrc, Crc32(head));
        output.Write(headCrc);
        output.Write(head);
        output.Write(chunked.ToArray());

        return output.ToArray();
    }

    /// <summary>
    /// Compresse un bloc d'en-tête en LZMA1 brut, précédé de ses cinq octets de propriétés.
    /// </summary>
    /// <remarks>
    /// Une différence assumée avec l'outil officiel : la fixture pose un marqueur de fin de flux là
    /// où Inno Setup n'en met pas. L'encodeur de SharpCompress produit sinon un flux que son propre
    /// décodeur ne sait pas terminer sans qu'on lui annonce la taille décompressée, taille que le
    /// format ne stocke justement plus depuis Inno Setup 4.0.9. Le marqueur ne change rien à ce que
    /// cette fixture met à l'épreuve — l'enveloppe à CRC, le découpage en tronçons, la disposition
    /// des enregistrements — et le cas SANS marqueur, celui des vrais installeurs, est couvert par
    /// l'étage live.
    /// </remarks>
    private static byte[] CompressLzma1(byte[] content)
    {
        using var body = new MemoryStream();
        byte[] properties;

        using (var encoder = LzmaStream.Create(new LzmaEncoderProperties(eos: true), isLzma2: false, body))
        {
            properties = encoder.Properties;
            encoder.Write(content, 0, content.Length);
        }

        var compressed = body.ToArray();
        var output = new byte[properties.Length + compressed.Length];
        properties.CopyTo(output, 0);
        compressed.CopyTo(output, properties.Length);

        return output;
    }

    /// <summary>
    /// Écrit un flux LZMA2 fait de tronçons NON compressés.
    /// </summary>
    /// <remarks>
    /// SharpCompress sait décoder LZMA2 mais pas l'encoder : son constructeur d'encodeur lève
    /// <see cref="NotImplementedException"/> dès qu'on lui demande LZMA2. Or LZMA2 n'est pas un
    /// codec, c'est un ENVELOPPAGE de LZMA1 qui prévoit justement des tronçons stockés tels quels :
    /// un octet de contrôle, la taille sur deux octets en gros-boutien, les données, et un zéro pour
    /// finir. Les émettre produit un flux LZMA2 parfaitement légal, qui fait passer la fixture par le
    /// même décodeur et le même octet de propriété de dictionnaire que l'installeur réel. Ce que
    /// cela ne couvre pas, c'est le décodage entropique lui-même — mais celui-là appartient à
    /// SharpCompress, et c'est l'étage live qui le met à l'épreuve sur les huit cent soixante
    /// mégaoctets du vrai installeur.
    /// </remarks>
    private static byte[] CompressLzma2(byte[] content)
    {
        const int MaxChunk = 0x10000;
        var output = new List<byte>(content.Length + 16);

        for (var offset = 0; offset < content.Length; offset += MaxChunk)
        {
            var take = Math.Min(MaxChunk, content.Length - offset);

            output.Add(offset == 0 ? (byte)0x01 : (byte)0x02); // stocké, avec puis sans réinitialisation
            output.Add((byte)((take - 1) >> 8));
            output.Add((byte)(take - 1));
            output.AddRange(content.AsSpan(offset, take));
        }

        output.Add(0x00); // fin de flux

        return [.. output];
    }

    private static uint Crc32(ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in data)
        {
            crc ^= b;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
        }

        return crc ^ 0xFFFFFFFFu;
    }

    /// <summary>Version repliée en un entier comparable, pour trancher les bascules de disposition.</summary>
    private static int Encode(int major, int minor, int patch, int revision)
        => (major * 1_000_000) + (minor * 10_000) + (patch * 100) + revision;

    private static int ParseVersion(string text)
    {
        var parts = text.Split('.');
        int Part(int index) => parts.Length > index
            ? int.Parse(parts[index], CultureInfo.InvariantCulture)
            : 0;

        return Encode(Part(0), Part(1), Part(2), Part(3));
    }

    private sealed record Location(ulong Offset, ulong Size, byte[] Sha256, bool CallInstructionOptimized);

    private sealed record Entry(string Destination, byte[] Content, bool CallInstructionOptimized, byte[]? ChecksumOverride)
    {
        public bool HasData { get; init; } = true;
    }

    private sealed class BlockWriter
    {
        private readonly List<byte> _bytes = [];

        public void Byte(byte value) => _bytes.Add(value);

        public void Bytes(byte[] value) => _bytes.AddRange(value);

        public void Zero(int count) => _bytes.AddRange(new byte[count]);

        public void UInt16(ushort value)
        {
            Span<byte> buffer = stackalloc byte[2];
            BinaryPrimitives.WriteUInt16LittleEndian(buffer, value);
            _bytes.AddRange(buffer);
        }

        public void UInt32(uint value)
        {
            Span<byte> buffer = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(buffer, value);
            _bytes.AddRange(buffer);
        }

        public void UInt64(ulong value)
        {
            Span<byte> buffer = stackalloc byte[8];
            BinaryPrimitives.WriteUInt64LittleEndian(buffer, value);
            _bytes.AddRange(buffer);
        }

        public void String(string value) => Bytes(Prefixed(value));

        public byte[] ToArray() => [.. _bytes];

        private static byte[] Prefixed(string value)
        {
            var text = Encoding.Unicode.GetBytes(value);
            var buffer = new byte[4 + text.Length];
            BinaryPrimitives.WriteUInt32LittleEndian(buffer, (uint)text.Length);
            text.CopyTo(buffer, 4);

            return buffer;
        }
    }
}
