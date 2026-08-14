namespace Prospect.Core.GameVersions.Inno;

/// <summary>
/// Version du FORMAT de données d'un installeur Inno Setup, lue dans la chaîne
/// <c>Inno Setup Setup Data (x.y.z)</c> qui précède l'en-tête compressé.
/// </summary>
/// <remarks>
/// <para>
/// Ce numéro n'est pas celui du compilateur Inno Setup : il n'est incrémenté que lorsque la
/// disposition BINAIRE des enregistrements change, ce qui en fait exactement la clé dont un lecteur
/// a besoin. Deux compilateurs différents qui écrivent la même chaîne écrivent le même format.
/// </para>
/// <para>
/// Prospect ne lit que la famille 6.4, celle de l'installeur que Vintage Story publie aujourd'hui.
/// Deux bascules suffisent à couvrir toute la famille, et elles sont documentées dans les sources
/// d'Inno Setup (<c>Projects/Src/Shared.Struct.pas</c>) :
/// </para>
/// <list type="bullet">
/// <item><description>
/// 6.4.2 ajoute la directive <c>CloseApplicationsFilterExcludes</c>, soit une chaîne de plus dans
/// l'en-tête, juste après les expressions d'architecture.
/// </description></item>
/// <item><description>
/// 6.4.3 allège l'enregistrement d'emplacement de fichier : l'octet <c>Sign</c> disparaît et le jeu
/// de drapeaux passe de neuf à cinq membres RENUMÉROTÉS, ce qui ramène l'enregistrement de 87 à
/// 85 octets.
/// </description></item>
/// </list>
/// <para>
/// Tout ce qui sort de cette famille est refusé plutôt que deviné. Une version 6.5 réorganise
/// franchement l'en-tête (chiffrement sorti dans son propre enregistrement, compteur de clés de
/// signature inséré au milieu des autres) : la lire avec la grille de 6.4 ne produirait pas une
/// erreur franche mais des chemins et des tailles absurdes. Le refus fait retomber l'installation
/// sur l'installeur officiel, qui lui saura toujours s'installer lui-même.
/// </para>
/// </remarks>
/// <param name="Major">Chiffre majeur.</param>
/// <param name="Minor">Chiffre mineur.</param>
/// <param name="Patch">Chiffre de correctif.</param>
/// <param name="Revision">Quatrième chiffre, présent seulement sur certaines versions (<c>6.4.0.1</c>).</param>
internal readonly record struct InnoSetupVersion(int Major, int Minor, int Patch, int Revision)
{
    /// <summary>Préfixe de la chaîne d'identification, en ASCII, tel qu'écrit dans le fichier.</summary>
    public const string IdPrefix = "Inno Setup Setup Data (";

    /// <summary>Longueur du bloc d'identification, remplissage par des zéros compris.</summary>
    public const int IdLength = 64;

    /// <summary>Vrai pour les versions dont Prospect sait lire la disposition.</summary>
    public bool IsSupported => Major == 6 && Minor == 4;

    /// <summary>
    /// Vrai quand l'en-tête porte la chaîne <c>CloseApplicationsFilterExcludes</c> (Inno Setup 6.4.2
    /// et au-delà).
    /// </summary>
    public bool HasCloseApplicationsFilterExcludes => CompareTo(new InnoSetupVersion(6, 4, 2, 0)) >= 0;

    /// <summary>
    /// Vrai quand l'enregistrement d'emplacement de fichier est celui, allégé, d'Inno Setup 6.4.3 :
    /// cinq drapeaux sur un octet et plus d'octet <c>Sign</c>.
    /// </summary>
    public bool HasCompactFileLocationRecord => CompareTo(new InnoSetupVersion(6, 4, 3, 0)) >= 0;

    /// <summary>
    /// Vrai quand l'assistant a perdu ses couleurs de fond et ses drapeaux de fenêtre, retirés par
    /// Inno Setup 6.4.0.1. Deux champs de moins dans l'en-tête, et cinq drapeaux de moins dans son
    /// jeu d'options.
    /// </summary>
    public bool HasFlatWizardColours => CompareTo(new InnoSetupVersion(6, 4, 0, 1)) >= 0;

    /// <summary>
    /// Nombre de drapeaux d'options déclarés par l'en-tête. Il ne sert pas à les interpréter mais à
    /// savoir COMBIEN D'OCTETS ils occupent : c'est le dernier champ du bloc, et se tromper d'un
    /// octet ferait échouer le contrôle de fin de bloc.
    /// </summary>
    public int HeaderFlagCount => HasFlatWizardColours ? 44 : 49;

    /// <summary>
    /// Lit la chaîne d'identification. Rend <see langword="false"/> si le bloc n'en est pas une :
    /// c'est le premier filtre qui protège d'une lecture faite au mauvais décalage.
    /// </summary>
    /// <param name="id">Les <see cref="IdLength"/> octets lus à l'emplacement de l'en-tête.</param>
    /// <param name="version">Version reconnue, en cas de succès.</param>
    public static bool TryParse(ReadOnlySpan<byte> id, out InnoSetupVersion version)
    {
        version = default;

        if (id.Length < IdPrefix.Length + 2)
        {
            return false;
        }

        for (var i = 0; i < IdPrefix.Length; i++)
        {
            if (id[i] != IdPrefix[i])
            {
                return false;
            }
        }

        var end = id[IdPrefix.Length..].IndexOf((byte)')');
        if (end <= 0)
        {
            return false;
        }

        Span<int> parts = stackalloc int[4];
        var count = 0;
        var value = 0;
        var digits = 0;

        foreach (var b in id.Slice(IdPrefix.Length, end))
        {
            if (b is >= (byte)'0' and <= (byte)'9')
            {
                value = (value * 10) + (b - '0');
                digits++;
                continue;
            }

            if (b != (byte)'.' || digits == 0 || count == parts.Length)
            {
                return false;
            }

            parts[count++] = value;
            value = 0;
            digits = 0;
        }

        if (digits == 0 || count == parts.Length)
        {
            return false;
        }

        parts[count++] = value;

        version = new InnoSetupVersion(
            parts[0],
            count > 1 ? parts[1] : 0,
            count > 2 ? parts[2] : 0,
            count > 3 ? parts[3] : 0);

        return true;
    }

    /// <inheritdoc />
    public override string ToString() => Revision == 0
        ? $"{Major}.{Minor}.{Patch}"
        : $"{Major}.{Minor}.{Patch}.{Revision}";

    private int CompareTo(InnoSetupVersion other)
    {
        if (Major != other.Major)
        {
            return Major.CompareTo(other.Major);
        }

        if (Minor != other.Minor)
        {
            return Minor.CompareTo(other.Minor);
        }

        return Patch != other.Patch ? Patch.CompareTo(other.Patch) : Revision.CompareTo(other.Revision);
    }
}
