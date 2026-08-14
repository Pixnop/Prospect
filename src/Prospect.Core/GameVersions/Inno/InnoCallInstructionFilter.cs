namespace Prospect.Core.GameVersions.Inno;

/// <summary>
/// Défait la transformation que le compilateur Inno Setup applique aux exécutables avant de les
/// compresser.
/// </summary>
/// <remarks>
/// <para>
/// Dans un binaire x86, les instructions <c>CALL</c> (<c>0xE8</c>) et <c>JMP</c> (<c>0xE9</c>)
/// portent une adresse RELATIVE à l'instruction suivante. Deux appels à la même fonction depuis
/// deux endroits différents produisent donc deux suites d'octets différentes, que le compresseur ne
/// peut pas mettre en commun. Inno Setup convertit ces adresses en adresses ABSOLUES avant de
/// compresser, ce qui rend les motifs identiques et fait gagner plusieurs points de taux ; la
/// lecture doit rendre la pareille dans l'autre sens.
/// </para>
/// <para>
/// Trois détails font toute la correction de la chose, et les trois se vérifient contre les
/// empreintes SHA-256 des entrées. Les quatre octets d'adresse ne sont JAMAIS réexaminés à la
/// recherche d'un <c>0xE8</c> : le balayage reprend après eux, sinon un octet d'adresse pris pour
/// une instruction décalerait tout le reste. Une instruction qui chevauche une frontière de bloc de
/// 64 Kio est ignorée, parce que le compilateur l'a ignorée aussi. Et l'octet de poids fort est
/// inversé quand le bit 23 du résultat est posé, optimisation ajoutée par Inno Setup 5.3.9 pour que
/// les sauts avant et arrière produisent le même octet de poids fort.
/// </para>
/// </remarks>
internal static class InnoCallInstructionFilter
{
    /// <summary>Taille des blocs sur lesquels le compilateur raisonne.</summary>
    private const int BlockSize = 0x10000;

    /// <summary>
    /// Rétablit les adresses relatives, sur place.
    /// </summary>
    /// <param name="data">Contenu du fichier, tel qu'il sort du flux décompressé.</param>
    public static void Unfilter(Span<byte> data)
    {
        var i = 0;

        while (i + 5 <= data.Length)
        {
            if (data[i] is not (0xE8 or 0xE9))
            {
                i++;
                continue;
            }

            if (BlockSize - (i % BlockSize) < 5)
            {
                i++;
                continue;
            }

            // Le compilateur n'a transformé que les adresses dont l'octet de poids fort valait 0x00
            // ou 0xFF, c'est-à-dire l'extension de signe d'un déplacement sur 24 bits. Les autres
            // ne sont vraisemblablement pas des instructions et sont restées telles quelles.
            if (data[i + 4] is 0x00 or 0xFF)
            {
                var address = (uint)(i + 5) & 0xFFFFFFu;
                var relative = (uint)(data[i + 1] | (data[i + 2] << 8) | (data[i + 3] << 16));
                relative = (relative - address) & 0xFFFFFFu;

                data[i + 1] = (byte)relative;
                data[i + 2] = (byte)(relative >> 8);
                data[i + 3] = (byte)(relative >> 16);

                if ((relative & 0x800000u) != 0)
                {
                    data[i + 4] = (byte)~data[i + 4];
                }
            }

            i += 5;
        }
    }
}
