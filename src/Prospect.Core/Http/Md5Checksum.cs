using System.Diagnostics.CodeAnalysis;
using System.IO.Abstractions;
using System.Security.Cryptography;

namespace Prospect.Core.Http;

/// <summary>
/// Calcul de l'empreinte MD5 d'un fichier, en flux et par blocs, pour ne jamais charger 600 Mo
/// en mémoire.
/// </summary>
/// <remarks>
/// MD5 n'est évidemment pas un algorithme de sécurité, et il n'est pas utilisé comme tel ici :
/// c'est le seul condensat que publie le catalogue officiel du jeu
/// (docs/research/vslauncher-et-distribution.md, section b), et il sert à détecter un
/// téléchargement tronqué ou corrompu, pas à authentifier une source. Le choix de l'algorithme
/// nous est imposé par l'amont ; le rejeter reviendrait à ne rien vérifier du tout, ce que
/// faisait VS Launcher.
/// </remarks>
[SuppressMessage("Security", "CA5351:Do Not Use Broken Cryptographic Algorithms", Justification = "Contrôle d'intégrité imposé par le catalogue officiel, aucun usage de sécurité. Voir la remarque de la classe.")]
[SuppressMessage("Major Code Smell", "S4790:Using weak hashing algorithms is security-sensitive", Justification = "Contrôle d'intégrité imposé par le catalogue officiel, aucun usage de sécurité. Voir la remarque de la classe.")]
internal static class Md5Checksum
{
    /// <summary>Empreinte hexadécimale minuscule du fichier <paramref name="path"/>.</summary>
    public static async Task<string> ComputeAsync(IFileSystem fileSystem, string path, int bufferSize, CancellationToken cancellationToken)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
        var buffer = new byte[bufferSize];

        var stream = fileSystem.File.OpenRead(path);
        await using (stream.ConfigureAwait(false))
        {
            int read;
            while ((read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                hash.AppendData(buffer, 0, read);
            }
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    /// <summary>
    /// Compare deux empreintes hexadécimales sans tenir compte de la casse : le catalogue les
    /// écrit en minuscules, mais rien ne le garantit.
    /// </summary>
    public static bool Matches(string expected, string actual)
        => string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase);
}