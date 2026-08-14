using System.Buffers.Binary;
using System.Text;

namespace Prospect.Core.GameVersions.Inno;

/// <summary>
/// Curseur séquentiel sur un bloc d'en-tête décompressé.
/// </summary>
/// <remarks>
/// <para>
/// Le bloc n'a NI index NI marqueurs : c'est une suite d'enregistrements collés bout à bout, chacun
/// de longueur variable, et la position de l'un ne se déduit que de la lecture complète du
/// précédent. Sauter un champ, ou en lire un de trop, ne provoque pas d'erreur immédiate : cela
/// décale tout le reste, et ce qui sort ensuite sont des tailles et des chemins vraisemblables mais
/// faux. D'où deux règles ici. Chaque champ déclaré par le format est CONSOMMÉ, même quand
/// Prospect n'en a aucun usage. Et toute lecture qui dépasse la fin du bloc lève, plutôt que de
/// rendre des zéros.
/// </para>
/// <para>
/// Cette classe couvre la famille 6.4, où tous les entiers font 32 bits et toutes les chaînes sont
/// en UTF-16LE. Les variantes 16 bits et les pages de code des installeurs antérieurs à Inno Setup 6
/// n'ont pas de raison d'exister ici : ces versions-là sont refusées en amont.
/// </para>
/// </remarks>
internal sealed class InnoRecordReader
{
    private readonly byte[] _data;

    /// <summary>Construit le curseur sur un bloc déjà décompressé.</summary>
    /// <param name="data">Contenu du bloc.</param>
    public InnoRecordReader(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);

        _data = data;
    }

    /// <summary>Position courante, en octets depuis le début du bloc.</summary>
    public int Position { get; private set; }

    /// <summary>Octets restants à lire.</summary>
    public int Remaining => _data.Length - Position;

    /// <summary>Lit <paramref name="count"/> octets bruts.</summary>
    public ReadOnlySpan<byte> Read(int count)
    {
        if (count < 0 || count > Remaining)
        {
            throw InnoFormatException.Corrupt(
                $"lecture de {count} octets à la position {Position} alors qu'il en reste {Remaining}");
        }

        var span = _data.AsSpan(Position, count);
        Position += count;

        return span;
    }

    /// <summary>Avance de <paramref name="count"/> octets sans les interpréter.</summary>
    public void Skip(int count) => Read(count);

    /// <summary>Lit un octet.</summary>
    public byte ReadByte() => Read(1)[0];

    /// <summary>Lit un entier non signé 16 bits.</summary>
    public ushort ReadUInt16() => BinaryPrimitives.ReadUInt16LittleEndian(Read(2));

    /// <summary>Lit un entier non signé 32 bits.</summary>
    public uint ReadUInt32() => BinaryPrimitives.ReadUInt32LittleEndian(Read(4));

    /// <summary>Lit un entier signé 32 bits.</summary>
    public int ReadInt32() => BinaryPrimitives.ReadInt32LittleEndian(Read(4));

    /// <summary>Lit un entier non signé 64 bits.</summary>
    public ulong ReadUInt64() => BinaryPrimitives.ReadUInt64LittleEndian(Read(8));

    /// <summary>Lit un entier signé 64 bits.</summary>
    public long ReadInt64() => BinaryPrimitives.ReadInt64LittleEndian(Read(8));

    /// <summary>
    /// Lit une chaîne préfixée de sa longueur EN OCTETS, sans la décoder.
    /// </summary>
    /// <remarks>
    /// La plupart des chaînes de l'en-tête ne servent à rien à Prospect : les traverser sans les
    /// convertir évite des milliers d'allocations sur un installeur qui en compte plus de vingt
    /// mille.
    /// </remarks>
    public void SkipString() => Skip(checked((int)ReadUInt32()));

    /// <summary>Lit une chaîne préfixée de sa longueur en octets et la décode en UTF-16LE.</summary>
    public string ReadString()
    {
        var length = checked((int)ReadUInt32());

        return Encoding.Unicode.GetString(Read(length));
    }

    /// <summary>
    /// Lit un jeu de drapeaux empaqueté : un octet par tranche de huit drapeaux déclarés.
    /// </summary>
    /// <remarks>
    /// Le nombre de drapeaux dépend de la version, et c'est LUI qui décide du nombre d'octets
    /// consommés : c'est pour ça que l'appelant le passe explicitement plutôt que de lire un octet
    /// « par défaut ». Inno Setup complète par ailleurs les jeux de trois octets à quatre, seule
    /// irrégularité de l'encodage.
    /// </remarks>
    /// <param name="declaredFlags">Nombre de drapeaux que cette version déclare.</param>
    public ulong ReadFlags(int declaredFlags)
    {
        var byteCount = (declaredFlags + 7) / 8;
        var bytes = Read(byteCount);

        ulong value = 0;
        for (var i = 0; i < bytes.Length; i++)
        {
            value |= (ulong)bytes[i] << (8 * i);
        }

        if (byteCount == 3)
        {
            Skip(1);
        }

        return value;
    }

    /// <summary>
    /// Consomme un intervalle de versions de Windows : deux bornes de dix octets chacune.
    /// </summary>
    /// <remarks>
    /// Chaque borne porte une version Windows (build 16 bits, mineur, majeur), une version NT au
    /// même format, et un service pack sur deux octets. Prospect n'en fait rien mais doit les
    /// traverser : c'est le champ le plus fréquent du format, présent dans presque tous les
    /// enregistrements.
    /// </remarks>
    public void SkipWindowsVersionRange() => Skip(20);
}