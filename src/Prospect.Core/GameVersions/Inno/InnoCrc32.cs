namespace Prospect.Core.GameVersions.Inno;

/// <summary>
/// CRC-32 (polynôme IEEE 802.3 réfléchi <c>0xEDB88320</c>), tel qu'Inno Setup s'en sert pour
/// contrôler ses blocs d'en-tête.
/// </summary>
/// <remarks>
/// Écrit ici plutôt que pris dans <c>System.IO.Hashing</c> : ce paquet n'appartient pas au
/// framework partagé, et une trentaine de lignes d'un algorithme figé depuis quarante ans ne
/// justifient pas une dépendance de plus. Le contraste avec LZMA est volontaire, et c'est le même
/// critère qui tranche les deux : ici la spécification tient en une table et se vérifie par un
/// vecteur de test, là il s'agissait d'un décodeur entropique complet.
/// </remarks>
internal static class InnoCrc32
{
    private const uint Polynomial = 0xEDB88320u;

    private static readonly uint[] Table = BuildTable();

    /// <summary>Empreinte d'un tampon.</summary>
    public static uint Compute(ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFFu;

        foreach (var b in data)
        {
            crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }

        return crc ^ 0xFFFFFFFFu;
    }

    private static uint[] BuildTable()
    {
        var table = new uint[256];

        for (var i = 0u; i < table.Length; i++)
        {
            var entry = i;
            for (var bit = 0; bit < 8; bit++)
            {
                entry = (entry & 1) != 0 ? (entry >> 1) ^ Polynomial : entry >> 1;
            }

            table[i] = entry;
        }

        return table;
    }
}